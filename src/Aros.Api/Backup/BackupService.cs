using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace Aros.Api.Backup;

/// <summary>What the page shows about one stored snapshot.</summary>
public record Snapshot(string Id, DateTime TakenAt, string Host, long Bytes);

/// <summary>The outcome of a run, with everything the tool printed.</summary>
public record RunResult(bool Ok, IReadOnlyList<string> Output);

/// <summary>
/// The backup, as the website sees it.
///
/// The work itself stays in Scripts\backup.ps1 and Scripts\restore.ps1, which are published
/// alongside the API and are also what a bare machine runs before there is an API to ask. This
/// class drives those scripts and reads the repository directly for anything read-only, so
/// there is one description of how a backup is taken rather than two that drift.
/// </summary>
public partial class BackupService(
    IOptions<BackupOptions> options,
    IWebHostEnvironment environment,
    ILogger<BackupService> log)
{
    private readonly BackupOptions settings = options.Value;

    /// <summary>
    /// One at a time. Two backups at once would fight over the staging folder, and restic
    /// locks the repository against the second one anyway - this just makes the refusal quick
    /// and legible instead of a timeout.
    /// </summary>
    private static readonly SemaphoreSlim Gate = new(1, 1);

    public bool Busy => Gate.CurrentCount == 0;

    [GeneratedRegex(@"^\s*\$env:(\w+)\s*=\s*'(.*)'\s*$", RegexOptions.Multiline)]
    private static partial Regex Assignment();

    // ------------------------------------------------------------------ credentials

    /// <summary>
    /// The credentials, as the scripts store them. The values are read here because restic needs
    /// them; they are never handed back to the browser - see <see cref="Describe"/>.
    /// </summary>
    private Dictionary<string, string> ReadCredentials()
    {
        var found = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(settings.CredentialsPath)) return found;

        foreach (Match match in Assignment().Matches(File.ReadAllText(settings.CredentialsPath)))
            found[match.Groups[1].Value] = match.Groups[2].Value;

        return found;
    }

    /// <summary>
    /// What the page is allowed to know: whether each secret is set, and the two values that are
    /// not secret. The passphrase and the application key never travel back out, because a page
    /// that can display them is a page that leaks them to anything reading the screen or the
    /// browser cache.
    /// </summary>
    public object Describe()
    {
        var credentials = ReadCredentials();

        // Present but empty is not set. The file always carries all four names, so asking
        // whether the name exists would tell the page a blank passphrase was a stored one.
        bool Has(string name) => !string.IsNullOrWhiteSpace(credentials.GetValueOrDefault(name));

        return new
        {
            configured = Has("RESTIC_REPOSITORY") && Has("RESTIC_PASSWORD") && Has("AWS_SECRET_ACCESS_KEY"),
            repository = credentials.GetValueOrDefault("RESTIC_REPOSITORY", ""),
            keyId = credentials.GetValueOrDefault("AWS_ACCESS_KEY_ID", ""),
            passphraseSet = Has("RESTIC_PASSWORD"),
            applicationKeySet = Has("AWS_SECRET_ACCESS_KEY"),
        };
    }

    /// <summary>
    /// Writes the credentials back. A blank passphrase or key means "leave the one that is
    /// already there" - the page cannot show them, so it cannot send them back either, and
    /// without this an edit to the repository URL would wipe the secrets beside it.
    /// </summary>
    public void SaveCredentials(string repository, string keyId, string? passphrase, string? applicationKey)
    {
        var existing = ReadCredentials();

        var values = new Dictionary<string, string>
        {
            ["RESTIC_REPOSITORY"] = repository.Trim(),
            ["AWS_ACCESS_KEY_ID"] = keyId.Trim(),
            ["RESTIC_PASSWORD"] = string.IsNullOrWhiteSpace(passphrase)
                ? existing.GetValueOrDefault("RESTIC_PASSWORD", "")
                : passphrase.Trim(),
            ["AWS_SECRET_ACCESS_KEY"] = string.IsNullOrWhiteSpace(applicationKey)
                ? existing.GetValueOrDefault("AWS_SECRET_ACCESS_KEY", "")
                : applicationKey.Trim(),
        };

        var text = new StringBuilder();
        text.AppendLine("# Credentials for the Aros backup. Read by Scripts\\backup.ps1 and Scripts\\restore.ps1,");
        text.AppendLine("# and written by the Backup page. Outside the repository and outside C:\\Aros\\api on");
        text.AppendLine("# purpose, so that neither a commit nor a redeploy can carry it anywhere.");
        text.AppendLine("#");
        text.AppendLine("# RESTIC_PASSWORD is the only thing here that cannot be made again: the Backblaze key");
        text.AppendLine("# can be reissued from the account, the passphrase cannot be reissued by anyone.");
        text.AppendLine();

        foreach (var (name, value) in values)
        {
            // Single quotes, because PowerShell expands nothing inside them and a generated key
            // is entitled to contain a dollar sign
            text.AppendLine($"$env:{name} = '{value.Replace("'", "''")}'");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(settings.CredentialsPath)!);
        File.WriteAllText(settings.CredentialsPath, text.ToString(), Encoding.UTF8);

        // Deliberately says nothing about what was written
        log.LogInformation("Backup credentials updated");
    }

    // -------------------------------------------------------------------- read-only

    /// <summary>Every snapshot in the repository, newest first.</summary>
    public async Task<IReadOnlyList<Snapshot>> SnapshotsAsync(CancellationToken ct)
    {
        var result = await ResticAsync(["snapshots", "--json"], ct);
        if (!result.Ok) return [];

        var json = string.Concat(result.Output);
        if (string.IsNullOrWhiteSpace(json)) return [];

        using var document = JsonDocument.Parse(json);

        return [.. document.RootElement.EnumerateArray()
            .Select(row => new Snapshot(
                row.GetProperty("short_id").GetString() ?? "",
                row.GetProperty("time").GetDateTimeOffset().LocalDateTime,
                row.TryGetProperty("hostname", out var host) ? host.GetString() ?? "" : "",
                row.TryGetProperty("summary", out var summary)
                    && summary.TryGetProperty("total_bytes_processed", out var bytes)
                        ? bytes.GetInt64()
                        : 0))
            .OrderByDescending(s => s.TakenAt)];
    }

    /// <summary>
    /// What is on this machine waiting to be backed up. Shown beside the latest snapshot so the
    /// two can be compared at a glance - a backup that is a week old is not obvious from a date
    /// alone.
    /// </summary>
    /// <summary>
    /// The settings file this backup is about. The script's own default points at the copy in a
    /// clone of the repository, which is right for a person running it by hand and wrong for a
    /// service running out of C:\Aros\api, so the path is always passed explicitly.
    /// </summary>
    private string SettingsFile => string.IsNullOrWhiteSpace(settings.SettingsPath)
        ? Path.Combine(environment.ContentRootPath, "appsettings.json")
        : settings.SettingsPath;

    public object Local()
    {
        var media = new DirectoryInfo(settings.MediaPath);
        var clips = media.Exists ? media.GetFiles("*", SearchOption.AllDirectories) : [];

        return new
        {
            clips = clips.Length,
            mediaBytes = clips.Sum(f => f.Length),
            settingsPath = SettingsFile,
        };
    }

    /// <summary>
    /// Forgets one snapshot and reclaims the space it was holding.
    ///
    /// There is no undo and no recycle bin: restic removes the snapshot and then deletes the
    /// packs nothing else refers to. The id is checked against the repository first, so a typo
    /// is a refusal rather than a surprise, and the last remaining snapshot needs <paramref
    /// name="force"/> - deleting it leaves nothing stored anywhere at all.
    /// </summary>
    public async Task<RunResult> ForgetAsync(string snapshot, bool force, CancellationToken ct)
    {
        if (!await Gate.WaitAsync(0, ct))
            return new RunResult(false, ["Something else is running."]);

        try
        {
            var stored = await SnapshotsAsync(ct);

            if (stored.All(s => !s.Id.Equals(snapshot, StringComparison.OrdinalIgnoreCase)))
                return new RunResult(false, [$"There is no snapshot {snapshot} in the repository."]);

            if (stored.Count == 1 && !force)
                return new RunResult(false,
                [
                    "That is the only snapshot there is. Deleting it leaves nothing stored anywhere.",
                ]);

            log.LogInformation("Forgetting snapshot {Snapshot}", snapshot);
            return await ResticAsync(["forget", snapshot, "--prune"], ct);
        }
        finally
        {
            Gate.Release();
        }
    }

    /// <summary>Verifies the stored data itself, not just that the index lists it.</summary>
    public Task<RunResult> VerifyAsync(CancellationToken ct) =>
        ResticAsync(["check", "--read-data-subset=10%"], ct);

    // ----------------------------------------------------------------------- doing it

    /// <summary>Takes a snapshot now, by running the same script the nightly task runs.</summary>
    public async Task<RunResult> BackupAsync(CancellationToken ct)
    {
        if (!await Gate.WaitAsync(0, ct))
            return new RunResult(false, ["A backup is already running."]);

        try
        {
            return await ScriptAsync("backup.ps1",
                ["-Settings", SettingsFile, "-Secrets", settings.CredentialsPath], ct);
        }
        finally
        {
            Gate.Release();
        }
    }

    /// <summary>
    /// Fetches a snapshot to look at. Nothing on this machine changes: the database, the audio
    /// and the settings stay exactly as they are, and the snapshot is unpacked where it can be
    /// opened. Putting one back is deliberately not offered here - see the controller.
    /// </summary>
    public async Task<RunResult> FetchAsync(string snapshot, CancellationToken ct)
    {
        if (!await Gate.WaitAsync(0, ct))
            return new RunResult(false, ["A backup is already running."]);

        try
        {
            return await ScriptAsync("restore.ps1",
                ["-Snapshot", snapshot, "-To", settings.InspectPath, "-Secrets", settings.CredentialsPath], ct);
        }
        finally
        {
            Gate.Release();
        }
    }

    // ------------------------------------------------------------------------ running

    /// <summary>
    /// Runs one of the scripts. Every caller passes both -Settings and -Secrets, and neither is
    /// optional in practice: the script's defaults are written for a person standing in a clone
    /// of the repository, and letting a service fall back to them is how a scratch API pointed
    /// at a scratch repository wrote its test snapshots into the real one instead.
    /// </summary>
    private async Task<RunResult> ScriptAsync(string name, IReadOnlyList<string> arguments, CancellationToken ct)
    {
        // Beside the binary, not beside the settings: the scripts are copied to the output
        // folder, which is the deploy folder once published but not the project folder when
        // the API is run from source
        var script = Path.Combine(AppContext.BaseDirectory, "Scripts", name);
        if (!File.Exists(script)) return new RunResult(false, [$"{script} is missing."]);

        var start = new ProcessStartInfo("powershell.exe");
        start.ArgumentList.Add("-NonInteractive");
        start.ArgumentList.Add("-ExecutionPolicy");
        start.ArgumentList.Add("Bypass");
        start.ArgumentList.Add("-File");
        start.ArgumentList.Add(script);
        foreach (var argument in arguments) start.ArgumentList.Add(argument);

        return await RunAsync(start, ct);
    }

    private async Task<RunResult> ResticAsync(IReadOnlyList<string> arguments, CancellationToken ct)
    {
        if (!File.Exists(settings.ResticPath))
            return new RunResult(false, [$"restic is not at {settings.ResticPath}."]);

        var credentials = ReadCredentials();
        if (!credentials.ContainsKey("RESTIC_REPOSITORY"))
            return new RunResult(false, ["No backup credentials are set yet."]);

        var start = new ProcessStartInfo(settings.ResticPath);
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        foreach (var (name, value) in credentials) start.Environment[name] = value;

        return await RunAsync(start, ct);
    }

    private async Task<RunResult> RunAsync(ProcessStartInfo start, CancellationToken ct)
    {
        start.RedirectStandardOutput = true;
        start.RedirectStandardError = true;
        start.UseShellExecute = false;
        start.CreateNoWindow = true;

        var output = new List<string>();

        using var process = new Process { StartInfo = start };
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) lock (output) output.Add(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) lock (output) output.Add(e.Data); };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromMinutes(settings.TimeoutMinutes));

        try
        {
            await process.WaitForExitAsync(deadline.Token);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { /* already gone */ }
            lock (output) output.Add($"Gave up after {settings.TimeoutMinutes} minutes.");
            return new RunResult(false, output);
        }

        lock (output) return new RunResult(process.ExitCode == 0, [.. output]);
    }
}
