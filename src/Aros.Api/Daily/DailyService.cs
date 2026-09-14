using Aros.Api.Data;
using Aros.Api.Data.Entities;
using Aros.Api.Grammar;
using Aros.Api.Listening;
using Aros.Api.Scheduling;
using Aros.Api.Vocab;
using Microsoft.EntityFrameworkCore;

namespace Aros.Api.Daily;

/// <summary>
/// One question in a mixed session, whatever kind it is. The fields a kind does not use are null:
/// the alternative is three payloads and three code paths in the client for what is, on screen,
/// the same card with a different middle.
/// </summary>
public record DailyCard(
    string Kind,
    string Track,
    string Label,
    Guid Token,
    bool Typed,
    string? Prompt = null,
    string? PromptLabel = null,
    string? AnswerLabel = null,
    string? Pattern = null,
    IReadOnlyList<string>? Tiles = null,
    string? AudioUrl = null,
    IReadOnlyList<QuizHint>? Hints = null,
    string? Mode = null,
    string? Direction = null);

public record DailySession(IReadOnlyList<DailyCard> Cards, IReadOnlyList<Share> Shares);

/// <summary>An item to ask again, named the way its own trainer names it.</summary>
public record DailyMiss(string Kind, int Id, string? Mode = null, string? Direction = null);

public class DailyException(string message) : Exception(message);

/// <summary>
/// The day's work in one game: listening, vocabulary and grammar shuffled together, with the
/// shares decided by where the ground is weakest rather than fixed in advance.
///
/// It owns no scoring and no schedule of its own. Every card is built by the trainer it belongs
/// to, carries that trainer's token, and is answered through that trainer's endpoint — so a
/// mixed session counts exactly like the separate rounds it replaces, and a change to any ladder
/// reaches it without this class knowing.
///
/// Two modes. The daily session is what is actually due, capped by
/// <see cref="SessionBudget.Daily"/>. Endless practice unlocks once that is done and keeps going
/// for as long as you want: it reaches past the rests into everything unmastered, and spaces
/// repeats so a word you keep missing does not come round every fourth question.
/// </summary>
public class DailyService(
    AppDbContext db,
    ListeningService listening,
    VocabService vocab,
    GrammarService grammar)
{
    /// <summary>How many answers back the accuracy behind each share is measured over.</summary>
    public const int AccuracyDays = 60;

    /// <summary>Questions per batch of endless practice — enough to keep going, small enough to stop.</summary>
    public const int EndlessBatch = 12;

    private static readonly IReadOnlyList<VocabDirection> Directions = Enum.GetValues<VocabDirection>();

    // ------------------------------------------------------------------ planning

    /// <summary>Where each kind of question stands, and how well it has been going lately.</summary>
    public async Task<List<Track>> TracksAsync(CancellationToken ct)
    {
        var since = DateTime.UtcNow.AddDays(-AccuracyDays);
        var tracks = new List<Track>();

        var listeningScores = await db.ListeningAnswers
            .Where(a => a.AnsweredAt >= since)
            .GroupBy(a => a.Mode)
            .Select(g => new { g.Key, Right = g.Count(a => a.Correct), Total = g.Count() })
            .ToListAsync(ct);

        foreach (var mode in ListeningService.Asked)
        {
            var (ready, unmastered) = await listening.StandingAsync(mode, ct);
            var score = listeningScores.FirstOrDefault(s => s.Key == mode);

            tracks.Add(new Track(
                $"listening:{mode}",
                ListeningLabel(mode),
                ready,
                unmastered,
                Accuracy(score?.Right, score?.Total)));
        }

        var vocabScores = await db.VocabAnswers
            .Where(a => a.AnsweredAt >= since)
            .GroupBy(a => a.Direction)
            .Select(g => new { g.Key, Right = g.Count(a => a.Correct), Total = g.Count() })
            .ToListAsync(ct);

        foreach (var direction in Directions)
        {
            var (ready, unmastered) = await vocab.StandingAsync(direction, ct);
            var score = vocabScores.FirstOrDefault(s => s.Key == direction);

            tracks.Add(new Track(
                $"vocab:{direction}",
                VocabLabel(direction),
                ready,
                unmastered,
                Accuracy(score?.Right, score?.Total)));
        }

        var grammarStanding = await grammar.StandingAsync(ct);
        var grammarRight = await db.GrammarAnswers.CountAsync(a => a.AnsweredAt >= since && a.Correct, ct);
        var grammarTotal = await db.GrammarAnswers.CountAsync(a => a.AnsweredAt >= since, ct);

        tracks.Add(new Track(
            "grammar",
            "Grammar · build the sentence",
            grammarStanding.Ready,
            grammarStanding.Unmastered,
            Accuracy(grammarRight, grammarTotal)));

        return tracks;
    }

    private static double? Accuracy(int? right, int? total) =>
        total is >= Track.Judgeable && right is { } hits ? (double)hits / total.Value : null;

    // ------------------------------------------------------------------- sessions

    /// <summary>The day's work: what is due, shared out by weakness, shuffled together.</summary>
    public async Task<DailySession> BuildAsync(CancellationToken ct)
    {
        var tracks = await TracksAsync(ct);
        var shares = DailyPlanner.Allocate(tracks, SessionBudget.Daily);

        if (shares.Count == 0)
            throw new DailyException(
                "Nothing is due. Everything is resting or mastered — endless practice is open if you want more.");

        var cards = await CardsAsync(shares, ignoreRests: false, exclude: null, ct);

        if (cards.Count == 0)
            throw new DailyException("Nothing is due right now. Try again after the next rest is up.");

        return new DailySession(DailyPlanner.Interleave(cards, c => c.Track, c => c.Kind), shares);
    }

    /// <summary>
    /// Practice past the day's work. Rests are set aside — otherwise there would be nothing left
    /// to ask — and <paramref name="recent"/> holds the items just seen, so nothing comes round
    /// again while it is still fresh in mind. That is what makes it feel like practice rather than
    /// like being nagged about the same four words.
    /// </summary>
    public async Task<DailySession> BuildEndlessAsync(
        IReadOnlyList<DailyMiss> recent, int count, CancellationToken ct)
    {
        var tracks = await TracksAsync(ct);

        if (tracks.All(t => t.Unmastered == 0))
            throw new DailyException("Everything is mastered. Add material in the tutor or the TTS library.");

        var shares = DailyPlanner.Allocate(tracks, Math.Max(1, count), endless: true);
        var cards = await CardsAsync(shares, ignoreRests: true, exclude: new Exclusions(recent), ct);

        // A short cooldown list can starve a small pool; better a repeat than an empty round
        if (cards.Count == 0)
            cards = await CardsAsync(shares, ignoreRests: true, exclude: null, ct);

        if (cards.Count == 0) throw new DailyException("Nothing left to ask right now.");

        return new DailySession(DailyPlanner.Interleave(cards, c => c.Track, c => c.Kind), shares);
    }

    /// <summary>Everything missed in the session, asked again, in the order it was missed.</summary>
    public async Task<DailySession> BuildDrillAsync(IReadOnlyList<DailyMiss> misses, CancellationToken ct)
    {
        var cards = new List<DailyCard>();

        foreach (var mode in ListeningService.Asked)
        {
            var clipIds = misses
                .Where(m => m.Kind == "listening" && m.Mode == mode.ToString())
                .Select(m => m.Id)
                .Distinct()
                .ToList();

            if (clipIds.Count == 0) continue;

            var quiz = await listening.BuildDrillAsync(clipIds, mode, ct);
            cards.AddRange(quiz.Questions.Select(q => Card(q, mode)));
        }

        var words = misses
            .Where(m => m.Kind == "vocab" && m.Direction is not null)
            .Select(m => (m.Id, Direction: Enum.Parse<VocabDirection>(m.Direction!)))
            .Distinct()
            .ToList();

        if (words.Count > 0)
        {
            var session = await vocab.BuildDrillAsync(
                words.Select(w => (WordId: w.Id, w.Direction)), ct);

            cards.AddRange(session.Questions.Select(Card));
        }

        var points = misses
            .Where(m => m.Kind == "grammar")
            .Select(m => m.Id)
            .Distinct()
            .ToList();

        if (points.Count > 0)
        {
            var round = await grammar.BuildDrillAsync(points, ct);
            cards.AddRange(round.Questions.Select(Card));
        }

        if (cards.Count == 0) throw new DailyException("Nothing to drill.");

        // Mixed again: the drill is the same game, not a different one
        return new DailySession(DailyPlanner.Interleave(cards, c => c.Track, c => c.Kind), []);
    }

    // -------------------------------------------------------------------- cards

    private async Task<List<DailyCard>> CardsAsync(
        IReadOnlyList<Share> shares, bool ignoreRests, Exclusions? exclude, CancellationToken ct)
    {
        var cards = new List<DailyCard>();

        foreach (var share in shares)
        {
            var (kind, part) = Split(share.Track.Key);

            switch (kind)
            {
                case "listening":
                {
                    var mode = Enum.Parse<ListeningMode>(part!);
                    var quiz = await listening.BuildSliceAsync(
                        mode, share.Count, ignoreRests, exclude?.Listening(mode), ct);

                    cards.AddRange(quiz.Questions.Select(q => Card(q, mode)));
                    break;
                }

                case "vocab":
                {
                    var direction = Enum.Parse<VocabDirection>(part!);
                    var session = await vocab.BuildSliceAsync(
                        direction, share.Count, ignoreRests, exclude?.Vocab(direction), ct);

                    cards.AddRange(session.Questions.Select(Card));
                    break;
                }

                default:
                {
                    var round = await grammar.BuildSliceAsync(
                        share.Count, ignoreRests, exclude?.Grammar, ct);

                    cards.AddRange(round.Questions.Select(Card));
                    break;
                }
            }
        }

        return cards;
    }

    private static DailyCard Card(QuizQuestion question, ListeningMode mode) => new(
        Kind: "listening",
        Track: $"listening:{mode}",
        Label: ListeningLabel(mode),
        Token: question.Token,
        Typed: ListeningService.IsTyped(mode),
        Tiles: question.Tiles,
        AudioUrl: $"/api/listening/audio/{question.Token}",
        Hints: question.Hints,
        Mode: mode.ToString());

    private static DailyCard Card(VocabQuestion question) => new(
        Kind: "vocab",
        Track: $"vocab:{question.Direction}",
        Label: VocabLabel(question.Direction),
        Token: question.Token,
        Typed: question.Typed,
        Prompt: question.Prompt,
        PromptLabel: question.PromptLabel,
        AnswerLabel: question.AnswerLabel,
        Tiles: question.Tiles,
        Direction: question.Direction.ToString());

    private static DailyCard Card(GrammarQuestion question) => new(
        Kind: "grammar",
        Track: "grammar",
        Label: "Grammar · build the sentence",
        Token: question.Token,
        Typed: false,
        Prompt: question.Prompt,
        Pattern: question.Pattern,
        Tiles: question.Tiles);

    // --------------------------------------------------------------- odds and ends

    private static (string Kind, string? Part) Split(string key)
    {
        var colon = key.IndexOf(':');
        return colon < 0 ? (key, null) : (key[..colon], key[(colon + 1)..]);
    }

    private static string ListeningLabel(ListeningMode mode) => mode switch
    {
        ListeningMode.Ordering => "Listening · build what you heard",
        ListeningMode.Pinyin => "Listening · write the pinyin",
        ListeningMode.English => "Listening · write the English",
        _ => $"Listening · {mode}",
    };

    private static string VocabLabel(VocabDirection direction) => direction switch
    {
        VocabDirection.CharactersToPinyin => "Vocabulary · characters → pinyin",
        VocabDirection.CharactersToEnglish => "Vocabulary · characters → English",
        VocabDirection.PinyinToEnglish => "Vocabulary · pinyin → English",
        VocabDirection.EnglishToPinyin => "Vocabulary · English → pinyin",
        VocabDirection.PinyinToCharacters => "Vocabulary · pinyin → characters",
        VocabDirection.EnglishToCharacters => "Vocabulary · English → characters",
        _ => direction.ToString(),
    };

    /// <summary>What not to ask again yet, grouped the way each trainer wants it.</summary>
    private sealed class Exclusions(IReadOnlyList<DailyMiss> recent)
    {
        public IReadOnlySet<int> Listening(ListeningMode mode) =>
            recent.Where(r => r.Kind == "listening" && r.Mode == mode.ToString())
                  .Select(r => r.Id)
                  .ToHashSet();

        public IReadOnlySet<int> Vocab(VocabDirection direction) =>
            recent.Where(r => r.Kind == "vocab" && r.Direction == direction.ToString())
                  .Select(r => r.Id)
                  .ToHashSet();

        public IReadOnlySet<int> Grammar =>
            recent.Where(r => r.Kind == "grammar").Select(r => r.Id).ToHashSet();
    }
}
