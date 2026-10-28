namespace LmInstaller.Core.Archive;

/// <summary>Files written by <see cref="SafeArchiveExtractor"/> into a temporary workspace.</summary>
public sealed record ExtractionResult
{
    public required string DestinationRoot { get; init; }

    public required IReadOnlyList<string> ExtractedFiles { get; init; }

    public required IReadOnlyList<string> SkippedEntries { get; init; }

    public long TotalBytesWritten { get; init; }
}
