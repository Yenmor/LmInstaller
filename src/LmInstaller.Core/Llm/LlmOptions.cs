using LmInstaller.Core.Configuration;

namespace LmInstaller.Core.Llm;

/// <summary>Transport settings for an OpenAI-compatible chat completions endpoint.</summary>
public sealed record LlmOptions
{
    public string? BaseUrl { get; init; }

    public string? ApiKey { get; init; }

    public string? Model { get; init; }

    public int TimeoutSeconds { get; init; } = 120;

    public double Temperature { get; init; }

    public int MaxTokens { get; init; } = 1_024;

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(BaseUrl) && !string.IsNullOrWhiteSpace(Model);

    public static LlmOptions FromSettings(LlmSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return new LlmOptions
        {
            BaseUrl = settings.BaseUrl,
            ApiKey = settings.ApiKey,
            Model = settings.Model,
            TimeoutSeconds = settings.TimeoutSeconds <= 0 ? 120 : settings.TimeoutSeconds,
        };
    }
}

/// <summary>Tuning for the main-executable selection step.</summary>
public sealed record MainExecutableSelectionOptions
{
    public static readonly MainExecutableSelectionOptions Default = new();

    /// <summary>Minimum LLM confidence before the decision is accepted without asking the user.</summary>
    public double ConfidenceThreshold { get; init; } = 0.6;

    public int MaxToolRounds { get; init; } = 6;

    /// <summary>Cap on candidates handed to the LLM so the prompt stays bounded.</summary>
    public int MaxCandidatesInPrompt { get; init; } = 200;
}
