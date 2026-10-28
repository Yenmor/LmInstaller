namespace LmInstaller.Core.Configuration;

/// <summary>Manager-owned settings. Stored outside the application root.</summary>
public sealed record ManagerSettings
{
    /// <summary>User-chosen directory that holds all managed portable applications.</summary>
    public string? AppRootPath { get; init; }

    public LlmSettings Llm { get; init; } = new();
}

/// <summary>LLM connection settings. Optional: when not configured the manager falls back to manual selection.</summary>
public sealed record LlmSettings
{
    public bool Enabled { get; init; }

    public string? BaseUrl { get; init; }

    public string? Model { get; init; }

    public string? ApiKey { get; init; }

    public int TimeoutSeconds { get; init; } = 120;
}
