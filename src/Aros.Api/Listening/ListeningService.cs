using Aros.Api.Data;
using Aros.Api.Data.Entities;
using Aros.Api.Scheduling;
using Aros.Api.Vocab;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace Aros.Api.Listening;

public record QuizOption(int ClipId, string Sentence);

public record QuizQuestion(Guid Token, ListeningMode Mode, bool Typed, IReadOnlyList<QuizOption>? Options);

public record Quiz(ListeningMode Mode, IReadOnlyList<QuizQuestion> Questions);

/// <summary>
/// What the answer was, once it has been given. <paramref name="Expected"/> is the pinyin or the
/// translation in the writing modes and empty when picking the sentence, where the sentence is
/// the answer.
/// </summary>
public record AnswerResult(bool Correct, int CorrectClipId, string CorrectSentence, string Expected, string? Note);

public class ListeningException(string message) : Exception(message);

public class ListeningService(AppDbContext db, IMemoryCache cache)
{
    public const int DefaultQuestionCount = 10;
    private const int MinimumClips = 3;

    private static readonly TimeSpan TokenLifetime = TimeSpan.FromHours(2);

    /// <summary>Picking the sentence needs three options; writing what you heard needs a keyboard.</summary>
    public static bool IsTyped(ListeningMode mode) => mode is not ListeningMode.Characters;

    /// <summary>Answer key for one question, held server-side so the page can't read it out of the payload.</summary>
    private sealed class QuestionState
    {
        public required int ClipId { get; init; }
        public required ListeningMode Mode { get; init; }
        public bool Answered { get; set; }
    }

    /// <summary>
    /// A round of <paramref name="questionCount"/> clips, or — with <paramref name="sweep"/> —
    /// every sentence the mode can ask about that is not resting, once each. The order is drawn by
    /// weight either way, so the ones most due come first; a sweep simply does not stop early.
    /// </summary>
    public async Task<Quiz> BuildQuizAsync(
        int questionCount, ListeningMode mode, bool sweep, CancellationToken ct)
    {
        var clips = await db.TtsClips
            .Include(c => c.Stats)
            .AsNoTracking()
            .ToListAsync(ct);

        // Only the picking mode needs to know what sounds like what
        var audible = mode == ListeningMode.Characters ? await AudibleFormsAsync(clips, ct) : null;

        var eligible = Eligible(clips, mode, audible!);

        if (eligible.Count == 0) throw new ListeningException(NothingToAsk(mode, clips));

        // Mastered and resting sentences drop out of the answers — but in the picking mode they
        // stay available as wrong options, since a known sentence is still a good distractor.
        var askable = Askable(eligible, mode);

        if (askable.Count == 0)
        {
            var tally = Tally(eligible, mode);

            throw new ListeningException(
                tally.NextDueAt is { } due
                    ? $"Every sentence here is resting. The next is due {Availability.Due(due)}."
                    : "Every sentence you can be asked here is mastered. Add new ones in Chinese TTS.");
        }

        var wanted = sweep ? askable.Count : Math.Clamp(questionCount, 1, askable.Count);
        var answers = PickWeighted(askable, mode, wanted);

        var questions = answers
            .Select(answer => BuildQuestion(answer, mode, clips, audible))
            .ToList();

        return new Quiz(mode, questions);
    }

    public async Task<TtsClip> GetClipForTokenAsync(Guid token, CancellationToken ct)
    {
        var state = Lookup(token);
        return await db.TtsClips.FirstOrDefaultAsync(c => c.Id == state.ClipId, ct)
               ?? throw new ListeningException("That clip no longer exists.");
    }

    public async Task<AnswerResult> AnswerAsync(Guid token, int? selectedClipId, string? text, CancellationToken ct)
    {
        var state = Lookup(token);

        var clip = await db.TtsClips
            .Include(c => c.Stats)
            .FirstOrDefaultAsync(c => c.Id == state.ClipId, ct)
            ?? throw new ListeningException("That clip no longer exists.");

        var (correct, note) = Judge(clip, state.Mode, selectedClipId, text);

        // Replaying an already-answered question must not double-count the score.
        if (!state.Answered)
        {
            state.Answered = true;
            RecordScore(clip, state.Mode, correct);
            db.ListeningAnswers.Add(new ListeningAnswer
            {
                TtsClipId = clip.Id,
                Mode = state.Mode,
                Correct = correct,
            });
            await db.SaveChangesAsync(ct);
        }

        return new AnswerResult(correct, clip.Id, clip.Sentence, Expected(clip, state.Mode), note);
    }

    private static (bool Correct, string? Note) Judge(
        TtsClip clip, ListeningMode mode, int? selectedClipId, string? text)
    {
        var given = text ?? "";

        return mode switch
        {
            ListeningMode.Characters => (selectedClipId == clip.Id, null),

            ListeningMode.Pinyin =>
                AnswerCheck.PinyinMatches(clip.Pinyin, given)
                    ? (true, null)
                    : (false, AnswerCheck.IsToneOnlyMistake(clip.Pinyin, given)
                        ? "Right syllables, wrong tone."
                        : null),

            _ => (AnswerCheck.SentenceMatches(clip.English, given), null),
        };
    }

    private static string Expected(TtsClip clip, ListeningMode mode) => mode switch
    {
        ListeningMode.Pinyin => clip.Pinyin,
        ListeningMode.English => clip.English,
        _ => "",
    };

    private static string NothingToAsk(ListeningMode mode, List<TtsClip> clips) => mode switch
    {
        ListeningMode.Characters when clips.Count < MinimumClips =>
            $"Need at least {MinimumClips} sentences to play. Add more in Chinese TTS — you have {clips.Count}.",

        ListeningMode.Characters =>
            "Every sentence sounds like another one in your library, so no fair question can be built. " +
            "Add sentences that differ audibly, or loosen a sound-alike group.",

        ListeningMode.Pinyin =>
            "No sentence has its pinyin yet. Import sentences with pinyin and English in Chinese TTS.",

        _ => "No sentence has an English translation yet. Import sentences with pinyin and English in Chinese TTS.",
    };

    private async Task<Dictionary<int, string>> AudibleFormsAsync(List<TtsClip> clips, CancellationToken ct)
    {
        var map = Homophones.BuildMap(await db.HomophoneGroups.AsNoTracking().ToListAsync(ct));
        return clips.ToDictionary(c => c.Id, c => Homophones.AudibleForm(c.Sentence, map));
    }

    /// <summary>
    /// Sentences that can be the answer in the picking mode: at least two others must sound
    /// different from them, or the question comes down to a guess between identical options.
    /// </summary>
    private static List<TtsClip> Pickable(List<TtsClip> clips, Dictionary<int, string> audible) =>
        clips.Count < MinimumClips
            ? []
            : clips.Where(c => DistinctSoundCount(c, clips, audible) >= 2).ToList();

    private QuizQuestion BuildQuestion(
        TtsClip answer, ListeningMode mode, List<TtsClip> allClips, Dictionary<int, string>? audible)
    {
        var options = mode == ListeningMode.Characters
            ? PickDistractors(answer, allClips, audible!)
                .Append(answer)
                .OrderBy(_ => Random.Shared.Next())
                .Select(c => new QuizOption(c.Id, c.Sentence))
                .ToList()
            : null;

        var token = Guid.NewGuid();
        cache.Set(CacheKey(token), new QuestionState { ClipId = answer.Id, Mode = mode }, TokenLifetime);

        return new QuizQuestion(token, mode, IsTyped(mode), options);
    }

    /// <summary>
    /// The two closest wrong options there are, measured in single-character edits. Candidates are
    /// taken strictly nearest-first, so when a sentence differing by one character exists it is
    /// always used; the shuffle only breaks ties between equally close candidates, which is the
    /// only place variety can come from without loosening the choice.
    /// Sentences that sound identical to the answer are never eligible.
    /// </summary>
    private static IEnumerable<TtsClip> PickDistractors(
        TtsClip answer, List<TtsClip> allClips, Dictionary<int, string> audible)
    {
        var candidates = DistinctSounding(answer, allClips, audible)
            .OrderBy(c => SentenceSimilarity.Distance(answer.Sentence, c.Sentence))
            .ThenBy(_ => Random.Shared.Next());

        // The two wrong options must also differ from each other by ear — a pair of identical
        // sounding distractors could both be ruled out without understanding a thing.
        var chosen = new List<TtsClip>(2);
        var used = new HashSet<string> { audible[answer.Id] };

        foreach (var candidate in candidates)
        {
            if (!used.Add(audible[candidate.Id])) continue;

            chosen.Add(candidate);
            if (chosen.Count == 2) break;
        }

        return chosen;
    }

    private static IEnumerable<TtsClip> DistinctSounding(
        TtsClip answer, List<TtsClip> allClips, Dictionary<int, string> audible) =>
        allClips.Where(c => c.Id != answer.Id && audible[c.Id] != audible[answer.Id]);

    /// <summary>How many genuinely different sounds are available to build wrong options from.</summary>
    private static int DistinctSoundCount(
        TtsClip answer, List<TtsClip> allClips, Dictionary<int, string> audible) =>
        DistinctSounding(answer, allClips, audible).Select(c => audible[c.Id]).Distinct().Count();

    private static void RecordScore(TtsClip clip, ListeningMode mode, bool correct)
    {
        var stat = Stat(clip, mode);

        if (stat is null)
        {
            stat = new TtsClipStat { TtsClipId = clip.Id, Mode = mode };
            clip.Stats.Add(stat);
        }

        if (correct)
        {
            stat.CorrectCount++;
            stat.ConsecutiveCorrect++;
        }
        else
        {
            stat.WrongCount++;
            stat.ConsecutiveCorrect = 0;
        }

        stat.LastSeenAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Drops sentences mastered in this mode for good and resting ones until their rest is up. A
    /// mode with nothing ready simply cannot be played: the start button knows this in advance and
    /// says when the next sentence is due, so there is no reason to cut a rest short behind your back.
    /// </summary>
    private static List<TtsClip> Askable(List<TtsClip> clips, ListeningMode mode) =>
        clips.Where(c => Stat(c, mode) is not { } s
                         || Schedule(s).IsAvailable(s.ConsecutiveCorrect, s.LastSeenAt))
             .ToList();

    /// <summary>A sentence missed even once climbs the longer ladder.</summary>
    private static RestSchedule Schedule(TtsClipStat stat) => RestSchedule.ForListening(stat.WrongCount);

    /// <summary>
    /// What each mode has left to ask. Drives the start button, so a mode whose sentences are all
    /// resting can say so up front rather than failing when the round is built.
    /// </summary>
    public async Task<IReadOnlyList<Availability>> AvailabilityAsync(CancellationToken ct)
    {
        var clips = await db.TtsClips.Include(c => c.Stats).AsNoTracking().ToListAsync(ct);
        var audible = await AudibleFormsAsync(clips, ct);

        return
        [
            .. Enum.GetValues<ListeningMode>()
                .Select(mode => Tally(Eligible(clips, mode, audible), mode))
        ];
    }

    /// <summary>Sentences a mode could ask about at all, before rests and mastery are considered.</summary>
    private static List<TtsClip> Eligible(
        List<TtsClip> clips, ListeningMode mode, Dictionary<int, string> audible) => mode switch
    {
        ListeningMode.Characters => Pickable(clips, audible),
        ListeningMode.Pinyin => clips.Where(c => c.Pinyin.Length > 0).ToList(),
        _ => clips.Where(c => c.English.Length > 0).ToList(),
    };

    private static Availability Tally(List<TtsClip> clips, ListeningMode mode) =>
        Availability.From(mode.ToString(), clips.Select(c => Standing(c, mode)));

    public static Availability.Standing Standing(TtsClip clip, ListeningMode mode) =>
        Stat(clip, mode) is { } stat
            ? new Availability.Standing(Schedule(stat), (stat.ConsecutiveCorrect, stat.LastSeenAt))
            : new Availability.Standing(RestSchedule.ListeningClean, null);

    internal static TtsClipStat? Stat(TtsClip clip, ListeningMode mode) =>
        clip.Stats.FirstOrDefault(s => s.Mode == mode);

    private static List<TtsClip> PickWeighted(List<TtsClip> clips, ListeningMode mode, int count) =>
        DrawWeight.PickWithoutReplacement(clips, count, clip => Weight(clip, mode));

    private static double Weight(TtsClip clip, ListeningMode mode) =>
        Stat(clip, mode) is { } stat
            ? DrawWeight.For(Schedule(stat), stat.WrongCount, stat.ConsecutiveCorrect, stat.LastSeenAt)
            : DrawWeight.Unseen;

    private QuestionState Lookup(Guid token) =>
        cache.TryGetValue(CacheKey(token), out QuestionState? state) && state is not null
            ? state
            : throw new ListeningException("This round has expired. Start a new game.");

    private static string CacheKey(Guid token) => $"listening:{token}";
}
