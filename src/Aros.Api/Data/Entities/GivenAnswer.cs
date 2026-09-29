namespace Aros.Api.Data.Entities;

/// <summary>
/// Tidies what the learner typed before it is stored beside the verdict.
///
/// Kept in one place because all three trainers store the same thing for the same reason, and a
/// mistake log where one trainer trims and another does not is a log that cannot be compared.
/// </summary>
public static class GivenAnswer
{
    /// <summary>
    /// Long enough for any sentence the trainers ask for, short enough that a pasted essay or a
    /// key held down cannot turn a row into a page.
    /// </summary>
    private const int Longest = 300;

    public static string? Tidy(string? text)
    {
        var trimmed = text?.Trim();

        if (string.IsNullOrEmpty(trimmed)) return null;

        return trimmed.Length <= Longest ? trimmed : trimmed[..Longest];
    }
}
