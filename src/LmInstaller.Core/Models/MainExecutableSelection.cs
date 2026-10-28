namespace LmInstaller.Core.Models;

/// <summary>Who produced a main-executable decision.</summary>
public enum SelectionSource
{
    /// <summary>Chosen by the LLM and validated by the program.</summary>
    Llm,

    /// <summary>Chosen by the rule engine.</summary>
    Rules,

    /// <summary>Chosen by the user in the UI.</summary>
    User,
}

/// <summary>
/// A decision about which candidate is the application's main executable.
/// <see cref="RelativePath"/> is relative to the extraction root and uses '/' separators.
/// </summary>
public sealed record MainExecutableSelection
{
    public required string RelativePath { get; init; }

    public required SelectionSource Source { get; init; }

    /// <summary>0.0 - 1.0. Rule-based and user selections use 1.0.</summary>
    public double Confidence { get; init; }

    public string? Reason { get; init; }

    /// <summary>Suggested application name, when the decision came with one.</summary>
    public string? SuggestedName { get; init; }
}
