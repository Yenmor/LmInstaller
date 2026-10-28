using System.IO.Compression;

namespace LmInstaller.Core.Archive;

/// <summary>
/// Extracts a ZIP into a destination directory while re-validating every entry.
/// Symlinks are never created, limits are enforced while streaming, and each final path is
/// confirmed to stay inside <c>destinationRoot</c> after normalisation.
/// </summary>
public sealed class SafeArchiveExtractor
{
    private const int CopyBufferSize = 81920;

    private readonly ArchiveLimits _limits;

    public SafeArchiveExtractor(ArchiveLimits? limits = null)
    {
        _limits = limits ?? ArchiveLimits.Default;
    }

    public ExtractionResult Extract(string archivePath, string destinationRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(archivePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationRoot);

        var fullArchivePath = Path.GetFullPath(archivePath);
        if (!File.Exists(fullArchivePath))
        {
            throw new FileNotFoundException("找不到压缩包文件。", fullArchivePath);
        }

        using var stream = new FileStream(fullArchivePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Extract(stream, fullArchivePath, destinationRoot);
    }

    public ExtractionResult Extract(Stream stream, string archivePath, string destinationRoot)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationRoot);

        var root = Path.GetFullPath(destinationRoot);
        Directory.CreateDirectory(root);

        var extracted = new List<string>();
        var skipped = new List<string>();
        var seenPaths = new HashSet<string>(
            OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        long totalBytes = 0;
        var entryCount = 0;

        try
        {
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);

            foreach (var entry in archive.Entries)
            {
                entryCount++;
                if (entryCount > _limits.MaxEntries)
                {
                    throw new ArchiveLimitExceededException(
                        entry.FullName,
                        $"压缩包条目数超过上限（{_limits.MaxEntries}）。");
                }

                if (!ArchivePathSafety.TryNormalize(entry.FullName, _limits, out var relativePath, out var violation))
                {
                    throw new UnsafeArchiveException(
                        archivePath,
                        new[] { violation ?? new ArchiveViolation(
                            ArchiveViolationKind.InvalidEntryName,
                            entry.FullName,
                            "条目路径无效。",
                            IsFatal: true) });
                }

                var targetPath = Path.GetFullPath(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
                if (!ArchivePathSafety.IsWithinRoot(root, targetPath))
                {
                    throw new UnsafeArchiveException(
                        archivePath,
                        new[]
                        {
                            new ArchiveViolation(
                                ArchiveViolationKind.PathTraversal,
                                entry.FullName,
                                $"条目规范化后超出目标目录：{relativePath}",
                                IsFatal: true),
                        });
                }

                if (IsSymbolicLink(entry))
                {
                    skipped.Add(relativePath);
                    continue;
                }

                var isDirectory = relativePath.EndsWith('/') ||
                                  entry.FullName.EndsWith('/') ||
                                  entry.FullName.EndsWith('\\') ||
                                  (entry.Name.Length == 0 && entry.Length == 0);

                if (isDirectory)
                {
                    Directory.CreateDirectory(targetPath);
                    continue;
                }

                if (!seenPaths.Add(relativePath))
                {
                    throw new UnsafeArchiveException(
                        archivePath,
                        new[]
                        {
                            new ArchiveViolation(
                                ArchiveViolationKind.DuplicateEntry,
                                entry.FullName,
                                $"压缩包存在重复条目：{relativePath}",
                                IsFatal: true),
                        });
                }

                var parent = Path.GetDirectoryName(targetPath);
                if (!string.IsNullOrEmpty(parent))
                {
                    Directory.CreateDirectory(parent);
                }

                using (var source = entry.Open())
                using (var destination = new FileStream(targetPath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    totalBytes += CopyWithLimit(source, destination, entry.FullName, totalBytes);
                }

                TryApplyTimestamp(targetPath, entry.LastWriteTime);
                extracted.Add(relativePath);
            }
        }
        catch (InvalidDataException ex)
        {
            throw new ArchiveFormatException(archivePath, ex);
        }

        if (totalBytes > _limits.MaxTotalUncompressedBytes)
        {
            throw new ArchiveLimitExceededException(
                string.Empty,
                $"解压后总大小超过上限（{_limits.MaxTotalUncompressedBytes} 字节）。");
        }

        return new ExtractionResult
        {
            DestinationRoot = root,
            ExtractedFiles = extracted,
            SkippedEntries = skipped,
            TotalBytesWritten = totalBytes,
        };
    }

    private long CopyWithLimit(Stream source, Stream destination, string entryName, long bytesSoFar)
    {
        var buffer = new byte[CopyBufferSize];
        long written = 0;
        int read;
        while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
        {
            written += read;
            if (bytesSoFar + written > _limits.MaxTotalUncompressedBytes)
            {
                throw new ArchiveLimitExceededException(
                    entryName,
                    $"解压后总大小超过上限（{_limits.MaxTotalUncompressedBytes} 字节）。");
            }

            destination.Write(buffer, 0, read);
        }

        return written;
    }

    private static bool IsSymbolicLink(ZipArchiveEntry entry)
    {
        var unixMode = (entry.ExternalAttributes >> 16) & 0xFFFF;
        const int fileTypeMask = 0xF000;
        const int symbolicLink = 0xA000;
        return (unixMode & fileTypeMask) == symbolicLink;
    }

    private static void TryApplyTimestamp(string path, DateTimeOffset timestamp)
    {
        try
        {
            var utc = timestamp.ToUniversalTime();
            if (utc.Year is < 1980 or > 2100)
            {
                return;
            }

            File.SetLastWriteTimeUtc(path, utc.UtcDateTime);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentOutOfRangeException)
        {
            // Timestamps are cosmetic; never fail extraction because of them.
        }
    }
}
