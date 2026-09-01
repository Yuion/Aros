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

        var wanted = Math.Clamp(questionCount, 1, clips.Count);
        var answers = PickWeighted(clips, wanted);

        var questions = answers
            .Select(answer => BuildQuestion(answer, clips))
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

    private QuizQuestion BuildQuestion(TtsClip answer, List<TtsClip> allClips)
    {
        var options = PickDistractors(answer, allClips)
            .Append(answer)
            .OrderBy(_ => Random.Shared.Next())
            .Select(c => new QuizOption(c.Id, c.Sentence))
            .ToList();

        var token = Guid.NewGuid();
        cache.Set(CacheKey(token), new QuestionState { ClipId = answer.Id }, TokenLifetime);

        return new QuizQuestion(token, options);
    }

    /// <summary>
    /// Two wrong options, drawn from the sentences closest in character length to the answer —
    /// length alone shouldn't give the game away.
    /// </summary>
    private static IEnumerable<TtsClip> PickDistractors(TtsClip answer, List<TtsClip> allClips)
    {
        const int poolSize = 12;

        return allClips
            .Where(c => c.Id != answer.Id)
            .OrderBy(c => Math.Abs(c.Sentence.Length - answer.Sentence.Length))
            .ThenBy(_ => Random.Shared.Next())
            .Take(poolSize)
            .OrderBy(_ => Random.Shared.Next())
            .Take(2);
    }

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
