namespace Aros.Api.Backup;

public class BackupOptions
{
    public const string SectionName = "Backup";

    /// <summary>
    /// The credentials file the scripts read: repository, passphrase and the Backblaze key.
    /// Deliberately outside both the repository and the deploy folder, so that neither a commit
    /// nor a redeploy can carry it anywhere.
    /// </summary>
    public string CredentialsPath { get; set; } = @"C:\Aros\backup.env.ps1";

    /// <summary>restic itself. Kept at a fixed path rather than found on PATH, because a service
    /// does not have the PATH an interactive shell has.</summary>
    public string ResticPath { get; set; } = @"C:\Aros\tools\restic.exe";

    /// <summary>Where a fetched snapshot is unpacked for inspection.</summary>
    public string InspectPath { get; set; } = @"C:\Aros\restore";

    /// <summary>
    /// The settings file that goes into the snapshot, and that the script reads the connection
    /// string from. Empty means the running API's own, which is the live one and the right
    /// answer everywhere except a scratch API pointed at a clone.
    /// </summary>
    public string SettingsPath { get; set; } = "";

    /// <summary>The audio, so the page can say how much of it there is.</summary>
    public string MediaPath { get; set; } = @"C:\Aros\media";

    /// <summary>
    /// How long a single restic or pg_dump run may take before it is given up on. Generous: the
    /// first upload from a new machine is the slow one, and every one after it sends only what
    /// changed.
    /// </summary>
    public int TimeoutMinutes { get; set; } = 30;
}
