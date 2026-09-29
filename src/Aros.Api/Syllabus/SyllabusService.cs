using Aros.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Aros.Api.Syllabus;

public class SyllabusOptions
{
    public const string SectionName = "Syllabus";

    /// <summary>
    /// The lowest HSK level to work towards. The level actually aimed at moves up on its own as
    /// each one is finished, so this is the floor rather than the goal - it exists to skip levels
    /// that were never the point, not to be edited every few months.
    /// </summary>
    public int Level { get; set; } = 1;
}

public record SyllabusWord(string Word, string Pinyin, int Rank);

/// <summary>Where the course stands against the level it is aiming at.</summary>
public record SyllabusProgress(
    int Level,
    int Total,
    int Taught,
    IReadOnlyList<SyllabusWord> NextUp,
    IReadOnlyList<string> OffList);

/// <summary>
/// The word list the course is actually working towards, and how much of it is done.
///
/// This exists because "the goal is HSK1" was one sentence of prose in the instructions, which
/// the model could agree with while teaching something else entirely - and did: twenty-four
/// lessons in, 51 of the 300 words were taught while 22 words outside the list had been, most
/// of them one long run of transport vocabulary. Prose cannot be checked. A list can.
/// </summary>
public class SyllabusService(AppDbContext db, IOptions<SyllabusOptions> options)
{
    private readonly SyllabusOptions settings = options.Value;

    /// <summary>How many of the remaining words to name. Enough to choose from, few enough to read.</summary>
    private const int Shortlist = 15;

    /// <summary>The top band the file carries: one exam covering 7 to 9, filed under 7.</summary>
    private const int TopLevel = 7;

    private static readonly Lazy<Dictionary<int, List<SyllabusWord>>> Levels = new(Load);

    public int Level => settings.Level;

    public async Task<SyllabusProgress> ProgressAsync(CancellationToken ct)
    {
        var known = await db.VocabWords
            .AsNoTracking()
            .Select(w => w.Characters)
            .ToListAsync(ct);

        var mine = known.ToHashSet();

        var level = AimingAt(mine);
        var syllabus = Levels.Value.GetValueOrDefault(level, []);
        var onList = syllabus.Select(w => w.Word).ToHashSet();

        // Ordered by how common the word is, not by where the syllabus prints it. The syllabus is
        // alphabetical by pinyin, which would teach 杯子 before 的.
        var nextUp = syllabus
            .Where(w => !mine.Contains(w.Word))
            .OrderBy(w => w.Rank == 0 ? int.MaxValue : w.Rank)
            .Take(Shortlist)
            .ToList();

        return new SyllabusProgress(
            level,
            syllabus.Count,
            syllabus.Count(w => mine.Contains(w.Word)),
            nextUp,
            [.. known.Where(w => !onList.Contains(w)).Order()]);
    }

    /// <summary>
    /// The level being worked towards: the configured floor, then upwards past every level whose
    /// words are all in the pool.
    ///
    /// Finishing HSK1 should move the goal to HSK2 by itself. Left to a setting, the course would
    /// go on being told to aim at a list it had already finished, which reads as "nothing left to
    /// teach" — and the tutor would have to invent what comes next.
    /// </summary>
    private int AimingAt(HashSet<string> known)
    {
        var level = Math.Clamp(settings.Level, 1, TopLevel);

        while (level < TopLevel
               && Levels.Value.GetValueOrDefault(level, []) is { Count: > 0 } words
               && words.All(w => known.Contains(w.Word)))
        {
            level++;
        }

        return level;
    }

    private static Dictionary<int, List<SyllabusWord>> Load()
    {
        var levels = new Dictionary<int, List<SyllabusWord>>();

        foreach (var row in HskFile.Rows())
        {
            if (!levels.TryGetValue(row.Level, out var words)) levels[row.Level] = words = [];
            words.Add(new SyllabusWord(row.Word, row.Pinyin, row.Rank));
        }

        return levels;
    }
}
