using System.IO.Compression;

namespace LmInstaller.Core.Archive;

/// <summary>
/// Reads a ZIP archive and reports everything the rest of the pipeline needs to know
/// before a single byte is written to disk: entry metadata, size accounting and safety violations.
/// </summary>
public sealed class ArchiveInspector
{
    private readonly ArchiveLimits _limits;

    public ArchiveInspector(ArchiveLimits? limits = null)
    {
        _limits = limits ?? ArchiveLimits.Default;
    }

    public ArchiveInspectionReport Inspect(string archivePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(archivePath);

        var fullPath = Path.GetFullPath(archivePath);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("找不到压缩包文件。", fullPath);
        }

        using var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Inspect(stream, fullPath, stream.Length);
    }

    public ArchiveInspectionReport Inspect(Stream stream, string archivePath, long archiveSizeBytes)
    {
        ArgumentNullException.ThrowIfNull(stream);

        var violations = new List<ArchiveViolation>();
        var entries = new List<ArchiveEntryInfo>();
        var seenPaths = new HashSet<string>(
            OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        long totalUncompressed = 0;
        var entryCount = 0;

        try
        {
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);

            foreach (var entry in archive.Entries)
            {
                entryCount++;
                if (entryCount > _limits.MaxEntries)
                {
                    violations.Add(new ArchiveViolation(
                        ArchiveViolationKind.TooManyEntries,
                        entry.FullName,
                        $"压缩包条目数超过上限（{_limits.MaxEntries}）。",
                        IsFatal: true));
                    break;
                }

                var isDirectory = entry.FullName.EndsWith('/') ||
                                  entry.FullName.EndsWith('\\') ||
                                  (entry.Name.Length == 0 && entry.Length == 0);
                var isSymbolicLink = IsSymbolicLink(entry);

                var normalized = entry.FullName;
                if (!ArchivePathSafety.TryNormalize(entry.FullName, _limits, out var safePath, out var violation))
                {
                    if (violation is not null)
                    {
                        violations.Add(violation);
                    }

                    // Record the entry so callers can show what was rejected, but do not treat the
                    // path as usable.
                    entries.Add(new ArchiveEntryInfo(
                        entry.FullName,
                        normalized,
                        isDirectory,
                        entry.Length,
                        entry.CompressedLength,
                        isSymbolicLink));
                    continue;
                }

                if (isSymbolicLink)
                {
                    violations.Add(new ArchiveViolation(
                        ArchiveViolationKind.SymbolicLink,
                        entry.FullName,
                        $"压缩包包含符号链接条目，将被跳过：{safePath}",
                        IsFatal: false));
                }

                if (!isDirectory && !seenPaths.Add(safePath))
                {
                    violations.Add(new ArchiveViolation(
                        ArchiveViolationKind.DuplicateEntry,
                        entry.FullName,
                        $"压缩包存在重复条目：{safePath}",
                        IsFatal: true));
                }

                if (!isDirectory)
                {
                    totalUncompressed += entry.Length;

                    if (entry.Length > _limits.CompressionRatioFreeBytes &&
                        entry.CompressedLength > 0 &&
                        (double)entry.Length / entry.CompressedLength > _limits.MaxEntryCompressionRatio)
                    {
                        violations.Add(new ArchiveViolation(
                            ArchiveViolationKind.CompressionRatio,
                            entry.FullName,
                            $"条目压缩比异常，疑似解压炸弹：{safePath}",
                            IsFatal: true));
                    }
                }

                entries.Add(new ArchiveEntryInfo(
                    entry.FullName,
                    safePath,
                    isDirectory,
                    entry.Length,
                    entry.CompressedLength,
                    isSymbolicLink));
            }
        }
        catch (InvalidDataException ex)
        {
            throw new ArchiveFormatException(archivePath, ex);
        }

        if (totalUncompressed > _limits.MaxTotalUncompressedBytes)
        {
            violations.Add(new ArchiveViolation(
                ArchiveViolationKind.TooLargeUncompressed,
                string.Empty,
                $"解压后总大小（{totalUncompressed} 字节）超过上限（{_limits.MaxTotalUncompressedBytes} 字节）。",
                IsFatal: true));
        }

        if (archiveSizeBytes > 0 && (double)totalUncompressed / archiveSizeBytes > _limits.MaxCompressionRatio)
        {
            violations.Add(new ArchiveViolation(
                ArchiveViolationKind.CompressionRatio,
                string.Empty,
                "压缩包整体压缩比异常，疑似解压炸弹。",
                IsFatal: true));
        }

        return new ArchiveInspectionReport
        {
            ArchivePath = archivePath,
            ArchiveSizeBytes = archiveSizeBytes,
            Entries = entries,
            Violations = violations,
            TotalUncompressedBytes = totalUncompressed,
            NameHint = GuessNameHint(entries, archivePath),
        };
    }

    private static bool IsSymbolicLink(ZipArchiveEntry entry)
    {
        // Zip entries created on Unix store the file mode in the high 16 bits of ExternalAttributes.
        var unixMode = (entry.ExternalAttributes >> 16) & 0xFFFF;
        const int fileTypeMask = 0xF000;
        const int symbolicLink = 0xA000;
        return (unixMode & fileTypeMask) == symbolicLink;
    }

    private static string? GuessNameHint(IReadOnlyList<ArchiveEntryInfo> entries, string archivePath)
    {
        var usable = entries.Where(e => e.NormalizedPath.Length > 0).ToList();
        var topLevel = usable
            .Select(e => e.NormalizedPath.Split('/')[0])
            .Where(name => name.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (topLevel.Count == 1)
        {
            var only = topLevel[0];
            var everyEntryInsideFolder = usable.All(
                e => string.Equals(e.NormalizedPath, only, StringComparison.OrdinalIgnoreCase) ||
                     e.NormalizedPath.StartsWith(only + "/", StringComparison.OrdinalIgnoreCase));

            if (everyEntryInsideFolder)
            {
                // A single root-level file is not a folder name; use its stem instead.
                var isFolder = usable.Any(e => e.IsDirectory &&
                                               string.Equals(e.NormalizedPath, only, StringComparison.OrdinalIgnoreCase));
                return isFolder ? only : Path.GetFileNameWithoutExtension(only);
            }
        }

        var stem = Path.GetFileNameWithoutExtension(archivePath);
        return string.IsNullOrWhiteSpace(stem) ? null : stem;
    }
}
