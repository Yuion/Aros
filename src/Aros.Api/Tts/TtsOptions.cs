namespace Aros.Api.Tts;

public class TtsOptions
{
    public const string SectionName = "Tts";

    /// <summary>Narakeet API key. Set in appsettings.json (gitignored) or user-secrets — never in source.</summary>
    public string ApiKey { get; set; } = "";

    /// <summary>Narakeet voice id, lowercase. See narakeet.com/languages/chinese-text-to-speech/.</summary>
    public string Voice { get; set; } = "yifei";

    /// <summary>Folder holding the cached audio. Kept outside the deploy folder so redeploys never wipe it.</summary>
    public string MediaPath { get; set; } = @"C:\Aros\media\tts";

    /// <summary>Guard against fat-fingering a novel into the box. Narakeet's streaming API caps near 1 KB anyway.</summary>
    public int MaxCharacters { get; set; } = 300;
}
