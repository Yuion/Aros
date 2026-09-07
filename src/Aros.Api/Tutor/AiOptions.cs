namespace Aros.Api.Tutor;

public class AiOptions
{
    public const string SectionName = "Ai";

    /// <summary>Never in source control. `Ai:ApiKey` in appsettings.json, which is gitignored.</summary>
    public string ApiKey { get; set; } = "";

    /// <summary>
    /// Required, with no default compiled in. Model names move faster than deploys and a stale
    /// hard-coded id is a 404 on the first call of the day; an empty setting is a clear refusal
    /// naming the setting instead.
    /// </summary>
    public string Model { get; set; } = "";

    /// <summary>Used only when the primary comes back "model not found", so a rename survives.</summary>
    public string FallbackModel { get; set; } = "";

    public string BaseUrl { get; set; } = "https://api.openai.com/v1/";

    /// <summary>
    /// Covers reasoning as well as the answer on models that think before replying, so it needs to
    /// be well above the length of the reply you expect. Too low fails as "incomplete" with nothing
    /// written at all.
    /// </summary>
    public int MaxOutputTokens { get; set; } = 16000;

    // The service is closed, so nobody else can spend this. These bound a *bug* — a retry storm,
    // a loop in a component — which does not care that the port is shut.
    public int DailyTokenBudget { get; set; } = 200_000;
    public int MaxRequestsPerHour { get; set; } = 120;

    public bool IsConfigured => ApiKey.Length > 0 && Model.Length > 0;

    /// <summary>What is missing, phrased so the fix is obvious from the message alone.</summary>
    public string ConfigurationProblem =>
        ApiKey.Length == 0 && Model.Length == 0 ? "Set Ai:ApiKey and Ai:Model in appsettings.json."
        : ApiKey.Length == 0 ? "No OpenAI API key configured. Set Ai:ApiKey in appsettings.json."
        : "No model configured. Set Ai:Model in appsettings.json — there is no default.";
}
