namespace LmInstaller.Core.Archive;

/// <summary>
/// Hard limits applied before and during extraction. They defend against path traversal,
/// absolute paths, extreme entry counts, decompression bombs and oversized payloads.
/// </summary>
public sealed record ArchiveLimits
{
    public static readonly ArchiveLimits Default = new();

    public int MaxEntries { get; init; } = 20_000;

    public long MaxTotalUncompressedBytes { get; init; } = 8L * 1024 * 1024 * 1024;

    public int MaxEntryNameLength { get; init; } = 512;

    public int MaxPathDepth { get; init; } = 32;

    /// <summary>Total uncompressed size divided by the archive size on disk.</summary>
    public double MaxCompressionRatio { get; init; } = 2_000d;

    /// <summary>Per-entry ratio, only enforced for entries larger than <see cref="CompressionRatioFreeBytes"/>.</summary>
    public double MaxEntryCompressionRatio { get; init; } = 5_000d;

    public long CompressionRatioFreeBytes { get; init; } = 1024 * 1024;
}
