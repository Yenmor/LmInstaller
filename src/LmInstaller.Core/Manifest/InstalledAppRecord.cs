namespace LmInstaller.Core.Manifest;

/// <summary>
/// One registered portable application. <see cref="Directory"/> and <see cref="MainExecutable"/>
/// are stored relative to the application root so the whole directory can be moved or synced.
/// </summary>
public sealed record InstalledAppRecord
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    /// <summary>Folder name under the application root.</summary>
    public required string Directory { get; init; }

    /// <summary>Main executable path relative to <see cref="Directory"/>, using '/' separators.</summary>
    public required string MainExecutable { get; init; }

    public required DateTimeOffset InstalledAtUtc { get; init; }

    public string? Version { get; init; }

    public string? ProductName { get; init; }

    public string? SourceArchiveFileName { get; init; }

    public long? SourceArchiveSizeBytes { get; init; }

    public string? SourceArchiveSha256 { get; init; }

    /// <summary>Free-form status such as "installed" or "missing".</summary>
    public string Status { get; init; } = "installed";

    public DateTimeOffset? LastVerifiedUtc { get; init; }
}
