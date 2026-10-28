namespace LmInstaller.Core.Models;

/// <summary>Coarse classification of a discovered executable entry point.</summary>
public enum CandidateKind
{
    NativeExecutable,
    DosExecutable,
    CommandScript,
    PowerShellScript,
    VisualBasicScript,
    JavaArchive,
    Other,
}

/// <summary>
/// A file inside the analysed archive that could be an application entry point.
/// Paths are relative to the extraction root and always use '/' separators.
/// </summary>
public sealed record CandidateExecutable
{
    public required string RelativePath { get; init; }

    /// <summary>Absolute path inside the temporary workspace. Never persisted as an install location.</summary>
    public required string FullPath { get; init; }

    public required string FileName { get; init; }

    /// <summary>Relative parent directory using '/' separators; empty string when the file sits at the root.</summary>
    public required string RelativeDirectory { get; init; }

    public required CandidateKind Kind { get; init; }

    public required long SizeBytes { get; init; }

    /// <summary>Number of directory levels below the extraction root (0 = extraction root).</summary>
    public int Depth { get; init; }

    public ExecutableVersionInfo VersionInfo { get; init; } = ExecutableVersionInfo.Empty;

    /// <summary>Heuristic score produced by the rule engine. Higher means "more likely the main program".</summary>
    public int RuleScore { get; init; }

    public IReadOnlyList<string> RuleNotes { get; init; } = Array.Empty<string>();

    public bool IsNativeExecutable => Kind is CandidateKind.NativeExecutable or CandidateKind.DosExecutable;
}
