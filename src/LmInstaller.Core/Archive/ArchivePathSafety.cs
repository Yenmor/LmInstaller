namespace LmInstaller.Core.Archive;

/// <summary>
/// Normalises and validates archive entry names. Every entry must resolve to a relative path
/// that stays inside the extraction root.
/// </summary>
internal static class ArchivePathSafety
{
    private static readonly string[] ReservedDeviceNames =
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    public static bool TryNormalize(
        string? entryName,
        ArchiveLimits limits,
        out string normalized,
        out ArchiveViolation? violation)
    {
        normalized = string.Empty;
        violation = null;

        if (string.IsNullOrWhiteSpace(entryName))
        {
            violation = new ArchiveViolation(
                ArchiveViolationKind.InvalidEntryName,
                entryName ?? string.Empty,
                "压缩包中存在空条目名。",
                IsFatal: true);
            return false;
        }

        if (entryName.Length > limits.MaxEntryNameLength)
        {
            violation = new ArchiveViolation(
                ArchiveViolationKind.InvalidEntryName,
                entryName,
                $"条目路径过长（{entryName.Length} 个字符）：{Truncate(entryName)}",
                IsFatal: true);
            return false;
        }

        if (entryName.Contains('\0'))
        {
            violation = new ArchiveViolation(
                ArchiveViolationKind.InvalidEntryName,
                entryName,
                "条目名包含空字符。",
                IsFatal: true);
            return false;
        }

        var unified = entryName.Replace('\\', '/');

        if (unified.StartsWith('/') || unified.StartsWith("//", StringComparison.Ordinal))
        {
            violation = new ArchiveViolation(
                ArchiveViolationKind.AbsolutePath,
                entryName,
                $"条目使用了绝对路径：{Truncate(entryName)}",
                IsFatal: true);
            return false;
        }

        if (unified.Length >= 2 && unified[1] == ':')
        {
            violation = new ArchiveViolation(
                ArchiveViolationKind.AbsolutePath,
                entryName,
                $"条目包含盘符或驱动器路径：{Truncate(entryName)}",
                IsFatal: true);
            return false;
        }

        var segments = unified.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0)
        {
            violation = new ArchiveViolation(
                ArchiveViolationKind.InvalidEntryName,
                entryName,
                "条目名为空。",
                IsFatal: true);
            return false;
        }

        if (segments.Length > limits.MaxPathDepth)
        {
            violation = new ArchiveViolation(
                ArchiveViolationKind.PathTooDeep,
                entryName,
                $"条目目录层级过深（{segments.Length} 层）：{Truncate(entryName)}",
                IsFatal: true);
            return false;
        }

        var cleaned = new List<string>(segments.Length);
        foreach (var segment in segments)
        {
            if (segment == ".")
            {
                continue;
            }

            if (segment == "..")
            {
                violation = new ArchiveViolation(
                    ArchiveViolationKind.PathTraversal,
                    entryName,
                    $"条目试图跳出目标目录：{Truncate(entryName)}",
                    IsFatal: true);
                return false;
            }

            if (segment.Contains(':') ||
                segment.IndexOfAny(new[] { '<', '>', '"', '|', '?', '*' }) >= 0)
            {
                violation = new ArchiveViolation(
                    ArchiveViolationKind.InvalidEntryName,
                    entryName,
                    $"条目名包含 Windows 不允许的字符：{Truncate(entryName)}",
                    IsFatal: true);
                return false;
            }

            var nameWithoutExtension = Path.GetFileNameWithoutExtension(segment);
            if (ReservedDeviceNames.Contains(nameWithoutExtension, StringComparer.OrdinalIgnoreCase) ||
                ReservedDeviceNames.Contains(segment, StringComparer.OrdinalIgnoreCase))
            {
                violation = new ArchiveViolation(
                    ArchiveViolationKind.InvalidEntryName,
                    entryName,
                    $"条目名使用了 Windows 保留设备名：{Truncate(entryName)}",
                    IsFatal: true);
                return false;
            }

            cleaned.Add(segment);
        }

        if (cleaned.Count == 0)
        {
            violation = new ArchiveViolation(
                ArchiveViolationKind.InvalidEntryName,
                entryName,
                "条目名无效。",
                IsFatal: true);
            return false;
        }

        normalized = string.Join('/', cleaned);
        return true;
    }

    /// <summary>True when <paramref name="candidateFullPath"/> is the root itself or lives below it.</summary>
    public static bool IsWithinRoot(string rootFullPath, string candidateFullPath)
    {
        var root = Path.GetFullPath(rootFullPath);
        var candidate = Path.GetFullPath(candidateFullPath);

        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        if (string.Equals(root, candidate, comparison))
        {
            return true;
        }

        var rootWithSeparator = root.EndsWith(Path.DirectorySeparatorChar)
            ? root
            : root + Path.DirectorySeparatorChar;

        return candidate.StartsWith(rootWithSeparator, comparison);
    }

    private static string Truncate(string value)
    {
        const int maxLength = 120;
        return value.Length <= maxLength ? value : value[..maxLength] + "…";
    }
}
