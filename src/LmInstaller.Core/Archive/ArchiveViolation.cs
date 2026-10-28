namespace LmInstaller.Core.Archive;

public enum ArchiveViolationKind
{
    AbsolutePath,
    PathTraversal,
    InvalidEntryName,
    DuplicateEntry,
    SymbolicLink,
    TooManyEntries,
    TooLargeUncompressed,
    CompressionRatio,
    PathTooDeep,
    UnreadableArchive,
}

/// <summary>A single safety problem found while inspecting an archive.</summary>
public sealed record ArchiveViolation(
    ArchiveViolationKind Kind,
    string EntryName,
    string Message,
    bool IsFatal);

public sealed class UnsafeArchiveException : Exception
{
    public UnsafeArchiveException(string archivePath, IReadOnlyList<ArchiveViolation> violations)
        : base(BuildMessage(archivePath, violations))
    {
        ArchivePath = archivePath;
        Violations = violations;
    }

    public string ArchivePath { get; }

    public IReadOnlyList<ArchiveViolation> Violations { get; }

    private static string BuildMessage(string archivePath, IReadOnlyList<ArchiveViolation> violations)
    {
        var fatal = violations.Where(v => v.IsFatal).Select(v => v.Message).Distinct().Take(3);
        return $"压缩包未通过安全检查：{archivePath}；" + string.Join("；", fatal);
    }
}

public sealed class ArchiveFormatException : Exception
{
    public ArchiveFormatException(string archivePath, Exception? innerException = null)
        : base($"无法读取压缩包（不是有效的 ZIP 文件或文件已损坏）：{archivePath}", innerException)
    {
        ArchivePath = archivePath;
    }

    public string ArchivePath { get; }
}

public sealed class ArchiveLimitExceededException : Exception
{
    public ArchiveLimitExceededException(string entryName, string message)
        : base(message)
    {
        EntryName = entryName;
    }

    public string EntryName { get; }
}
