using LmInstaller.Core.Manifest;
using LmInstaller.Core.Models;

namespace LmInstaller.Core.Installation;

/// <summary>A fully confirmed install: source payload, chosen main executable and target root.</summary>
public sealed record InstallRequest
{
    /// <summary>User-chosen application root. Must already exist and be writable.</summary>
    public required string AppRootPath { get; init; }

    /// <summary>Directory inside the temporary workspace that holds the complete application payload.</summary>
    public required string SourceDirectory { get; init; }

    /// <summary>Main executable, relative to <see cref="SourceDirectory"/>, using '/' separators.</summary>
    public required string MainExecutableRelativePath { get; init; }

    /// <summary>Suggested folder name, usually derived from the archive name.</summary>
    public required string ProposedName { get; init; }

    public string? SourceArchivePath { get; init; }

    public string? SourceArchiveSha256 { get; init; }

    public string? Version { get; init; }

    public ExecutableVersionInfo VersionInfo { get; init; } = ExecutableVersionInfo.Empty;

    public InstallOptions Options { get; init; } = InstallOptions.Default;
}

public sealed record InstallResult(
    InstalledAppRecord Record,
    string InstallDirectory,
    string MainExecutablePath,
    IReadOnlyList<string> Warnings);
