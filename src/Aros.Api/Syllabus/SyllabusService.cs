using Aros.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Aros.Api.Syllabus;

public class SyllabusOptions
{
    public const string SectionName = "Syllabus";

    /// <summary>
    /// The HSK level being worked towards. Raise it when the level below is done - the data file
    /// carries every level, so moving the goalposts is this one number.
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

    private static readonly Lazy<Dictionary<int, List<SyllabusWord>>> Levels = new(Load);

    public int Level => settings.Level;

    public async Task<SyllabusProgress> ProgressAsync(CancellationToken ct)
    {
        var syllabus = Levels.Value.GetValueOrDefault(settings.Level, []);

        var known = await db.VocabWords
            .AsNoTracking()
            .Select(w => w.Characters)
            .ToListAsync(ct);

        var mine = known.ToHashSet();
        var onList = syllabus.Select(w => w.Word).ToHashSet();

        // Ordered by how common the word is, not by where the syllabus prints it. The syllabus is
        // alphabetical by pinyin, which would teach 杯子 before 的.
        var nextUp = syllabus
            .Where(w => !mine.Contains(w.Word))
            .OrderBy(w => w.Rank == 0 ? int.MaxValue : w.Rank)
            .Take(Shortlist)
            .ToList();

        return new SyllabusProgress(
            settings.Level,
            syllabus.Count,
            syllabus.Count(w => mine.Contains(w.Word)),
            nextUp,
            [.. known.Where(w => !onList.Contains(w)).Order()]);
    }

    private static Dictionary<int, List<SyllabusWord>> Load()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Syllabus", "hsk.tsv");
        var levels = new Dictionary<int, List<SyllabusWord>>();

        if (!File.Exists(path)) return levels;

        foreach (var line in File.ReadLines(path).Skip(1))
        {
            var parts = line.Split('\t');
            if (parts.Length < 4 || !int.TryParse(parts[0], out var level)) continue;

            // A blank rank means the frequency list did not cover the word; sorted last, not first
            _ = int.TryParse(parts[3], out var rank);

            if (!levels.TryGetValue(level, out var words)) levels[level] = words = [];
            words.Add(new SyllabusWord(parts[1], parts[2], rank));
        }

        return levels;
    }
}
