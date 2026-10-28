namespace LmInstaller.Core.Archive;

/// <summary>Metadata for one archive entry, captured without extracting it.</summary>
public sealed record ArchiveEntryInfo(
    string FullName,
    string NormalizedPath,
    bool IsDirectory,
    long Length,
    long CompressedLength,
    bool IsSymbolicLink);

/// <summary>Result of inspecting an archive before any data is written to disk.</summary>
public sealed record ArchiveInspectionReport
{
    public required string ArchivePath { get; init; }

    public required long ArchiveSizeBytes { get; init; }

    public required IReadOnlyList<ArchiveEntryInfo> Entries { get; init; }

    public required IReadOnlyList<ArchiveViolation> Violations { get; init; }

    public long TotalUncompressedBytes { get; init; }

    /// <summary>Best guess for the application name: the single root folder, or the archive file name.</summary>
    public string? NameHint { get; init; }

    public bool HasFatalViolations => Violations.Any(v => v.IsFatal);

    public bool IsSafeToExtract => !HasFatalViolations;
}
