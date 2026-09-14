namespace Aros.Api.Daily;

/// <summary>What one kind of question is worth asking today, before the shares are decided.</summary>
/// <param name="Key">Stable identifier — "listening:Ordering", "vocab:EnglishToCharacters", "grammar".</param>
/// <param name="Ready">Items due right now.</param>
/// <param name="Unmastered">Items not finished, rests included — what endless practice can reach.</param>
/// <param name="Accuracy">Share of recent answers that were right, or null when there are too few to judge.</param>
public record Track(string Key, string Label, int Ready, int Unmastered, double? Accuracy)
{
    /// <summary>
    /// How much of the day this deserves. Weakness is the point: something answered right nine
    /// times in ten does not need the same practice as something answered right six times in ten.
    ///
    /// Clamped at both ends. A track with no failures still gets asked — the floor — because the
    /// only evidence that it is still known is being asked; and a track going badly cannot take
    /// the whole session, because a day spent on one thing is how the rest slides.
    ///
    /// The backlog counts too, under a square root: a mode with forty due needs more of the day
    /// than one with four, but not ten times more.
    /// </summary>
    public double Mass => Weakness * Math.Sqrt(Math.Max(1, Ready));

    public double Weakness => Accuracy is { } accuracy
        ? Math.Clamp(1 - accuracy, MinWeakness, MaxWeakness)
        : UnknownWeakness;

    /// <summary>Never zero: a track answered perfectly is still asked, just less.</summary>
    public const double MinWeakness = 0.15;

    /// <summary>A track going badly gets the most, but not everything.</summary>
    public const double MaxWeakness = 0.85;

    /// <summary>Too few answers to judge — treated as middling rather than as either extreme.</summary>
    public const double UnknownWeakness = 0.5;

    /// <summary>Answers needed before an accuracy means anything.</summary>
    public const int Judgeable = 6;
}

/// <summary>How many questions each track gets, and why.</summary>
public record Share(Track Track, int Count);

/// <summary>
/// Divides a session between the kinds of question. Deliberately its own class with no database
/// and no services: what it does is arithmetic on counts, and arithmetic is worth being able to
/// check by reading.
/// </summary>
public static class DailyPlanner
{
    /// <summary>Every track with something due gets at least this, so nothing is skipped entirely.</summary>
    public const int Coverage = 2;

    /// <summary>
    /// Shares out <paramref name="total"/> questions. Each track with anything due is covered
    /// first, then what is left goes by mass — weakness times the square root of the backlog —
    /// and no track is ever given more than it has.
    /// </summary>
    public static List<Share> Allocate(IReadOnlyList<Track> tracks, int total, bool endless = false)
    {
        var available = tracks
            .Select(t => (Track: t, Pool: endless ? t.Unmastered : t.Ready))
            .Where(t => t.Pool > 0)
            .ToList();

        if (available.Count == 0 || total <= 0) return [];

        var given = available.ToDictionary(t => t.Track.Key, _ => 0);

        // Coverage only when there is room for all of it. A short batch spent two questions each
        // on the smallest pools and ran out before the big ones were reached, which is how a round
        // of twelve came back as nothing but vocabulary — below that size, mass decides everything.
        if (total >= Coverage * available.Count)
            foreach (var (track, pool) in available)
                given[track.Key] = Math.Min(Coverage, pool);

        // Then the rest by mass, repeating because a track that fills up hands its share back
        var remaining = total - given.Values.Sum();

        while (remaining > 0)
        {
            var hungry = available.Where(t => given[t.Track.Key] < t.Pool).ToList();
            if (hungry.Count == 0) break;

            var mass = hungry.Sum(t => t.Track.Mass);
            if (mass <= 0) break;

            var handedOut = 0;

            foreach (var (track, pool) in hungry)
            {
                var want = (int)Math.Round(remaining * track.Mass / mass);
                var room = pool - given[track.Key];
                var take = Math.Clamp(want, 0, Math.Min(room, remaining - handedOut));

                given[track.Key] += take;
                handedOut += take;
            }

            // Rounding can hand out nothing at all; give the hungriest one a question and go again
            if (handedOut == 0)
            {
                var pick = hungry.OrderByDescending(t => t.Track.Mass).First();
                given[pick.Track.Key]++;
                handedOut = 1;
            }

            remaining -= handedOut;
        }

        return
        [
            .. available
                .Select(t => new Share(t.Track, given[t.Track.Key]))
                .Where(s => s.Count > 0)
        ];
    }

    /// <summary>
    /// Orders the cards so the session keeps changing shape all the way through.
    ///
    /// "Not the same as the last one" is not enough on its own. With thirty-five listening cards
    /// out of sixty there are only twenty-five other cards to put between them, so ten listening
    /// cards have to sit next to another one — and a rule that merely alternates leaves all ten in
    /// a block at the end, which is exactly the slog the mixed session exists to avoid.
    ///
    /// So each kind is paced against how much of it is left: the next card comes from whichever
    /// kind has the largest <c>remaining / (already asked + 1)</c>, which spreads a kind evenly
    /// across the whole session and scatters the unavoidable doubles through it. Ties go to a kind
    /// other than the last one. Within a kind the same rule picks the track, so six directions of
    /// vocabulary take turns rather than arriving in a heap.
    /// </summary>
    public static List<T> Interleave<T>(IEnumerable<T> cards, Func<T, string> track, Func<T, string> kind)
    {
        var queues = cards
            .GroupBy(track)
            .ToDictionary(
                group => group.Key,
                group => new Queue<T>(group.OrderBy(_ => Random.Shared.Next())));

        var kindOf = queues.ToDictionary(q => q.Key, q => kind(q.Value.Peek()));

        var askedByKind = new Dictionary<string, int>();
        var askedByTrack = queues.ToDictionary(q => q.Key, _ => 0);

        var ordered = new List<T>();
        string? lastTrack = null;
        string? lastKind = null;

        while (queues.Values.Any(q => q.Count > 0))
        {
            var live = queues.Where(q => q.Value.Count > 0).ToList();

            var chosenKind = live
                .GroupBy(q => kindOf[q.Key])
                .Select(g => (Kind: g.Key, Left: g.Sum(q => q.Value.Count)))
                .OrderByDescending(g => (double)g.Left / (askedByKind.GetValueOrDefault(g.Kind) + 1))
                .ThenBy(g => g.Kind == lastKind ? 1 : 0)
                .ThenBy(_ => Random.Shared.Next())
                .First()
                .Kind;

            var pick = live
                .Where(q => kindOf[q.Key] == chosenKind)
                .OrderByDescending(q => (double)q.Value.Count / (askedByTrack[q.Key] + 1))
                .ThenBy(q => q.Key == lastTrack ? 1 : 0)
                .ThenBy(_ => Random.Shared.Next())
                .First();

            ordered.Add(pick.Value.Dequeue());

            askedByKind[chosenKind] = askedByKind.GetValueOrDefault(chosenKind) + 1;
            askedByTrack[pick.Key]++;
            lastTrack = pick.Key;
            lastKind = chosenKind;
        }

        return ordered;
    }
}
