using Aros.Api.Data;
using Aros.Api.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace Aros.Api.Listening;

public record QuizOption(int ClipId, string Sentence);
public record QuizQuestion(Guid Token, IReadOnlyList<QuizOption> Options);
public record Quiz(IReadOnlyList<QuizQuestion> Questions);
public record AnswerResult(bool Correct, int CorrectClipId, string CorrectSentence);

public class ListeningException(string message) : Exception(message);

public class ListeningService(AppDbContext db, IMemoryCache cache)
{
    public const int DefaultQuestionCount = 10;
    private const int MinimumClips = 3;
    private static readonly TimeSpan TokenLifetime = TimeSpan.FromHours(2);

    /// <summary>Answer key for one question, held server-side so the page can't read it out of the payload.</summary>
    private sealed class QuestionState
    {
        public required int ClipId { get; init; }
        public bool Answered { get; set; }
    }

    public async Task<Quiz> BuildQuizAsync(int questionCount, CancellationToken ct)
    {
        var clips = await db.TtsClips
            .Include(c => c.Stat)
            .AsNoTracking()
            .ToListAsync(ct);

        if (clips.Count < MinimumClips)
            throw new ListeningException(
                $"Need at least {MinimumClips} sentences to play. Add more in Chinese TTS — you have {clips.Count}.");

        var map = Homophones.BuildMap(await db.HomophoneGroups.AsNoTracking().ToListAsync(ct));
        var audible = clips.ToDictionary(c => c.Id, c => Homophones.AudibleForm(c.Sentence, map));

        // A clip can only be asked about if at least two other sentences sound different from it —
        // otherwise the question would come down to a guess between identical-sounding options.
        var eligible = clips
            .Where(c => DistinctSoundCount(c, clips, audible) >= 2)
            .ToList();

        if (eligible.Count == 0)
            throw new ListeningException(
                "Every sentence sounds like another one in your library, so no fair question can be built. " +
                "Add sentences that differ audibly, or loosen a sound-alike group.");

        var wanted = Math.Clamp(questionCount, 1, eligible.Count);
        var answers = PickWeighted(eligible, wanted);

        var questions = answers
            .Select(answer => BuildQuestion(answer, clips, audible))
            .ToList();

        return new Quiz(questions);
    }

    public async Task<TtsClip> GetClipForTokenAsync(Guid token, CancellationToken ct)
    {
        var state = Lookup(token);
        return await db.TtsClips.FirstOrDefaultAsync(c => c.Id == state.ClipId, ct)
               ?? throw new ListeningException("That clip no longer exists.");
    }

    public async Task<AnswerResult> AnswerAsync(Guid token, int selectedClipId, CancellationToken ct)
    {
        var state = Lookup(token);

        var clip = await db.TtsClips
            .Include(c => c.Stat)
            .FirstOrDefaultAsync(c => c.Id == state.ClipId, ct)
            ?? throw new ListeningException("That clip no longer exists.");

        var correct = selectedClipId == clip.Id;

        // Replaying an already-answered question must not double-count the score.
        if (!state.Answered)
        {
            state.Answered = true;
            RecordScore(clip, correct);
            db.ListeningAnswers.Add(new ListeningAnswer { TtsClipId = clip.Id, Correct = correct });
            await db.SaveChangesAsync(ct);
        }

        return new AnswerResult(correct, clip.Id, clip.Sentence);
    }

    private static void RecordScore(TtsClip clip, bool correct)
    {
        var stat = clip.Stat ??= new TtsClipStat { TtsClipId = clip.Id };

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

    private QuizQuestion BuildQuestion(TtsClip answer, List<TtsClip> allClips, Dictionary<int, string> audible)
    {
        var options = PickDistractors(answer, allClips, audible)
            .Append(answer)
            .OrderBy(_ => Random.Shared.Next())
            .Select(c => new QuizOption(c.Id, c.Sentence))
            .ToList();

        var token = Guid.NewGuid();
        cache.Set(CacheKey(token), new QuestionState { ClipId = answer.Id }, TokenLifetime);

        return new QuizQuestion(token, options);
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

    /// <summary>
    /// Weighted draw without replacement. A clip's weight rises with every miss and halves for
    /// each consecutive correct answer, so weak sentences come back often and mastered ones fade
    /// out without ever disappearing entirely.
    /// </summary>
    private static List<TtsClip> PickWeighted(List<TtsClip> clips, int count)
    {
        var remaining = new List<TtsClip>(clips);
        var picked = new List<TtsClip>(count);

        while (picked.Count < count && remaining.Count > 0)
        {
            var weights = remaining.Select(Weight).ToList();
            var roll = Random.Shared.NextDouble() * weights.Sum();

            var index = 0;
            for (; index < weights.Count - 1; index++)
            {
                roll -= weights[index];
                if (roll <= 0) break;
            }

            picked.Add(remaining[index]);
            remaining.RemoveAt(index);
        }

        return picked;
    }

    private static double Weight(TtsClip clip)
    {
        var stat = clip.Stat;
        if (stat is null) return 1.0;

        var weight = (1 + stat.WrongCount) * Math.Pow(0.5, stat.ConsecutiveCorrect);
        return Math.Max(0.25, weight);
    }

    private QuestionState Lookup(Guid token) =>
        cache.TryGetValue(CacheKey(token), out QuestionState? state) && state is not null
            ? state
            : throw new ListeningException("This round has expired. Start a new game.");

    private static string CacheKey(Guid token) => $"listening:{token}";
}
