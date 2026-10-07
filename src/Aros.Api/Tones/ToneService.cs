using Aros.Api.Data;
using Aros.Api.Data.Entities;
using Aros.Api.Scheduling;
using Aros.Api.Tts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace Aros.Api.Tones;

/// <summary>One question: a sound to play, and a token to answer it with.</summary>
public record ToneQuestion(Guid Token, string AudioLocation);

public record ToneRound(IReadOnlyList<ToneQuestion> Questions);

/// <summary>What answering said: whether it was right, and what it actually was.</summary>
public record ToneResult(bool Correct, int Tone, string Character, string Pinyin);

public record ToneStanding(int Sounds, int WithAudio, int Answered, double? Accuracy);

/// <summary>
/// Telling the four tones apart by ear, with the word taken out of the way.
///
/// The listening trainer already asks for the pinyin of a sentence, which tests the tones along
/// with everything else — and "everything else" is usually what carries it: a sentence you know
/// gives you its own tones. Here a single syllable is played and the only question is which of
/// the four it was, so the ear has nothing to lean on.
///
/// Weighted by what goes wrong, like every other trainer, but kept apart from all of them: a
/// tone missed here never changes when a word is next asked.
/// </summary>
public class ToneService(AppDbContext db, TtsService tts, IMemoryCache cache)
{
    private static readonly TimeSpan TokenLifetime = TimeSpan.FromHours(3);

    public const int DefaultRound = 20;

    private sealed class Pending
    {
        public required string Syllable { get; init; }
        public required int Tone { get; init; }
        public bool Answered { get; set; }
    }

    /// <summary>How the bank stands: how much of it can be played, and how you are doing.</summary>
    public async Task<ToneStanding> StandingAsync(CancellationToken ct)
    {
        var answers = await db.ToneAnswers.AsNoTracking()
            .Select(a => a.Correct)
            .ToListAsync(ct);

        return new ToneStanding(
            ToneBank.Sounds.Count,
            ToneBank.Sounds.Count(Playable),
            answers.Count,
            answers.Count == 0 ? null : (double)answers.Count(c => c) / answers.Count);
    }

    /// <summary>
    /// Synthesises whatever the bank is still missing. Each character costs one synthesis, once,
    /// ever: the file is named after the text, so a character already spoken anywhere is free.
    /// </summary>
    public async Task<int> SpeakMissingAsync(CancellationToken ct)
    {
        var spoken = 0;

        foreach (var sound in ToneBank.Sounds.Where(s => !Playable(s)))
        {
            await tts.SpeakFragmentAsync(sound.Character, ct);
            spoken++;
        }

        return spoken;
    }

    public async Task<ToneRound> BuildAsync(int count, CancellationToken ct)
    {
        var playable = ToneBank.Sounds.Where(Playable).ToList();

        if (playable.Count == 0)
            throw new ToneException("No tone audio yet. Build the sound bank first.");

        var misses = await MissesAsync(ct);

        var picked = DrawWeight.PickWorstFirst(
            playable,
            Math.Clamp(count, 1, Math.Min(playable.Count, SessionBudget.Listening * 2)),
            sound => Weight(sound, misses));

        var questions = new List<ToneQuestion>();

        foreach (var sound in picked)
        {
            var token = Guid.NewGuid();

            cache.Set(
                CacheKey(token),
                new Pending { Syllable = sound.Syllable, Tone = sound.Tone },
                TokenLifetime);

            questions.Add(new ToneQuestion(token, Location(sound)));
        }

        return new ToneRound(questions);
    }

    /// <summary>
    /// The other way to ask it: one syllable in all four tones, shuffled, with the four numbers
    /// to be handed out between them.
    ///
    /// A different test from naming one tone out of the blue. Here nothing has to be recognised
    /// in isolation — the four are in front of you and only have to be told apart — which is the
    /// skill the fourth tone and the first are actually confused on.
    /// </summary>
    public async Task<IReadOnlyList<ToneRound>> BuildSetsAsync(int count, CancellationToken ct)
    {
        var complete = ToneBank.Sounds
            .Where(Playable)
            .GroupBy(s => s.Syllable)
            .Where(g => g.Count() == 4)
            .ToList();

        if (complete.Count == 0)
            throw new ToneException("No syllable has all four tones recorded yet. Build the sound bank first.");

        var misses = await MissesAsync(ct);

        // Weighted like everything else: the syllables you get wrong come round more often, and a
        // set is as heavy as the heaviest sound in it
        var picked = DrawWeight.PickWorstFirst(
            complete,
            Math.Clamp(count, 1, complete.Count),
            group => group.Max(sound => Weight(sound, misses)));

        var sets = new List<ToneRound>();

        foreach (var group in picked)
        {
            var shuffled = group.OrderBy(_ => Random.Shared.Next()).ToList();
            var questions = new List<ToneQuestion>();

            foreach (var sound in shuffled)
            {
                var token = Guid.NewGuid();

                cache.Set(
                    CacheKey(token),
                    new Pending { Syllable = sound.Syllable, Tone = sound.Tone },
                    TokenLifetime);

                questions.Add(new ToneQuestion(token, Location(sound)));
            }

            sets.Add(new ToneRound(questions));
        }

        return sets;
    }

    /// <summary>
    /// The syllable with its tone stripped off: "ma" for a sound that is mā, má, mǎ or mà.
    ///
    /// A hint rather than the answer. Knowing the syllable settles nothing about which of the
    /// four was said, and it rules out the other failure — hearing a sound you cannot place at
    /// all and guessing between four tones of a syllable you never identified.
    /// </summary>
    public string HintFor(Guid token)
    {
        if (!cache.TryGetValue(CacheKey(token), out Pending? state) || state is null)
            throw new ToneException("That round has expired. Start a new one.");

        return state.Syllable;
    }

    public async Task<ToneResult> AnswerAsync(Guid token, int given, int durationMs, CancellationToken ct)
    {
        if (!cache.TryGetValue(CacheKey(token), out Pending? state) || state is null)
            throw new ToneException("That round has expired. Start a new one.");

        var sound = ToneBank.Find(state.Syllable, state.Tone)
                    ?? throw new ToneException("That sound is no longer in the bank.");

        var correct = given == state.Tone;

        // Replaying a question must not score twice, the way every other trainer treats a retry
        if (!state.Answered)
        {
            state.Answered = true;

            db.ToneAnswers.Add(new ToneAnswer
            {
                Syllable = state.Syllable,
                Tone = state.Tone,
                Given = given,
                Correct = correct,
                DurationMs = Math.Max(0, durationMs),
            });

            await db.SaveChangesAsync(ct);
        }

        return new ToneResult(correct, sound.Tone, sound.Character, sound.Pinyin);
    }

    /// <summary>Which tones get confused for which — the thing this trainer exists to show.</summary>
    public async Task<IReadOnlyList<object>> ConfusionsAsync(CancellationToken ct)
    {
        var wrong = await db.ToneAnswers.AsNoTracking()
            .Where(a => !a.Correct)
            .Select(a => new { a.Tone, a.Given })
            .ToListAsync(ct);

        return
        [
            .. wrong
                .GroupBy(a => (a.Tone, a.Given))
                .OrderByDescending(g => g.Count())
                .Take(6)
                .Select(g => (object)new { heard = g.Key.Tone, said = g.Key.Given, times = g.Count() })
        ];
    }

    /// <summary>
    /// The file behind a question's token. Served this way round so the answer never appears in
    /// a URL: the file is named after the character, and the character names the tone.
    /// </summary>
    public string LocationFor(Guid token)
    {
        if (!cache.TryGetValue(CacheKey(token), out Pending? state) || state is null)
            throw new ToneException("That round has expired. Start a new one.");

        var sound = ToneBank.Find(state.Syllable, state.Tone)
                    ?? throw new ToneException("That sound is no longer in the bank.");

        return Location(sound);
    }

    private bool Playable(ToneSound sound) => tts.FileExists(Location(sound));

    private string Location(ToneSound sound) => TtsService.FileNameFor(sound.Character);

    private double Weight(ToneSound sound, MissTally misses) =>
        DrawWeight.For(RestSchedule.VocabularyClean, misses.For(Key(sound), 0), 0, null);

    /// <summary>A sound's identity as a number, for the tally: "cai" in tone 3 is one thing.</summary>
    private static int Key(ToneSound sound) =>
        HashCode.Combine(sound.Syllable, sound.Tone) & 0x7fffffff;

    private async Task<MissTally> MissesAsync(CancellationToken ct)
    {
        var since = MissTally.Since;

        var wrong = await db.ToneAnswers.AsNoTracking()
            .Where(a => !a.Correct && a.At >= since)
            .Select(a => new { a.Syllable, a.Tone, a.At })
            .ToListAsync(ct);

        return MissTally.From(wrong.Select(w =>
            (HashCode.Combine(w.Syllable, w.Tone) & 0x7fffffff, 0, w.At)));
    }

    private static string CacheKey(Guid token) => $"tone:{token}";
}

public class ToneException(string message) : Exception(message);
