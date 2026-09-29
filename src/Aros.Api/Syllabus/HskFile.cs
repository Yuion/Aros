namespace Aros.Api.Syllabus;

/// <summary>One line of the syllabus file.</summary>
public record HskRow(int Level, string Word, string Pinyin, int Rank);

/// <summary>
/// Reads <c>Syllabus/hsk.tsv</c>, which two things now want: the course's progress through a
/// level, and the tone trainer's bank of syllables. Read once and kept, because it is a quarter
/// of a megabyte that never changes while the process is running.
/// </summary>
public static class HskFile
{
    private static readonly Lazy<List<HskRow>> Loaded = new(Read);

    public static IReadOnlyList<HskRow> Rows() => Loaded.Value;

    private static List<HskRow> Read()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Syllabus", "hsk.tsv");
        var rows = new List<HskRow>();

        if (!File.Exists(path)) return rows;

        foreach (var line in File.ReadLines(path).Skip(1))
        {
            var parts = line.Split('\t');
            if (parts.Length < 4) continue;

            // The top band is one exam covering levels 7 to 9 and the file says so: "7-9". Read
            // as a plain number it parses as nothing and the whole band vanishes, which is how
            // the tone bank quietly lost 5622 words and came out less than half the size it
            // should have been. Its first level is close enough for both readers.
            if (!int.TryParse(parts[0], out var level))
                level = parts[0].StartsWith('7') ? 7 : 0;

            if (level == 0) continue;

            // A blank rank means the frequency list did not cover the word; it sorts last, not first
            _ = int.TryParse(parts[3], out var rank);

            rows.Add(new HskRow(level, parts[1], parts[2], rank));
        }

        return rows;
    }
}
