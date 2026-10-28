using LmInstaller.Core.Models;

namespace LmInstaller.Core.Scanning;

public sealed record ScannerOptions
{
    public static readonly ScannerOptions Default = new();

    public int MaxDepth { get; init; } = 24;

    public int MaxCandidates { get; init; } = 2_000;

    public long MaxScannedFiles { get; init; } = 200_000;

    public bool IncludeScripts { get; init; } = true;

    public bool ReadVersionInfo { get; init; } = true;

    /// <summary>Version resources are only read from files no larger than this.</summary>
    public long MaxVersionInfoReadBytes { get; init; } = 512L * 1024 * 1024;
}

/// <summary>
/// Recursively finds entry-point candidates inside an extracted archive.
/// Nothing is executed; the scanner only reads file metadata.
/// </summary>
public sealed class ExecutableCandidateScanner
{
    private readonly ScannerOptions _options;

    public ExecutableCandidateScanner(ScannerOptions? options = null)
    {
        _options = options ?? ScannerOptions.Default;
    }

    public IReadOnlyList<CandidateExecutable> Scan(string extractionRoot, string? nameHint = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(extractionRoot);

        var root = Path.GetFullPath(extractionRoot);
        if (!Directory.Exists(root))
        {
            throw new DirectoryNotFoundException("解压目录不存在：" + root);
        }

        var candidates = new List<CandidateExecutable>();
        var pending = new Stack<(string Path, int Depth)>();
        pending.Push((root, 0));
        long scannedFiles = 0;

        while (pending.Count > 0 && candidates.Count < _options.MaxCandidates)
        {
            var (directory, depth) = pending.Pop();
            if (depth > _options.MaxDepth)
            {
                continue;
            }

            IEnumerable<string> subdirectories;
            IEnumerable<string> files;
            try
            {
                subdirectories = Directory.EnumerateDirectories(directory);
                files = Directory.EnumerateFiles(directory);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            foreach (var subdirectory in subdirectories)
            {
                var name = Path.GetFileName(subdirectory);
                if (CandidateRules.ShouldSkipDirectory(name) || IsReparsePoint(subdirectory))
                {
                    continue;
                }

                pending.Push((subdirectory, depth + 1));
            }

            foreach (var file in files)
            {
                scannedFiles++;
                if (scannedFiles > _options.MaxScannedFiles || candidates.Count >= _options.MaxCandidates)
                {
                    break;
                }

                if (IsReparsePoint(file))
                {
                    continue;
                }

                var extension = Path.GetExtension(file);
                if (!CandidateRules.IsCandidateExtension(extension))
                {
                    continue;
                }

                var kind = CandidateRules.ClassifyKind(extension);
                if (!_options.IncludeScripts && kind != CandidateKind.NativeExecutable && kind != CandidateKind.DosExecutable)
                {
                    continue;
                }

                long size;
                try
                {
                    size = new FileInfo(file).Length;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    continue;
                }

                var relativePath = Path.GetRelativePath(root, file).Replace(Path.DirectorySeparatorChar, '/');
                var relativeDirectory = Path.GetDirectoryName(relativePath)?.Replace(Path.DirectorySeparatorChar, '/') ?? string.Empty;
                var depthInTree = relativeDirectory.Length == 0
                    ? 0
                    : relativeDirectory.Split('/').Length;

                var versionInfo = ExecutableVersionInfo.Empty;
                if (_options.ReadVersionInfo && kind == CandidateKind.NativeExecutable && size <= _options.MaxVersionInfoReadBytes)
                {
                    versionInfo = PeVersionInfoReader.TryRead(file);
                }

                var scoreInput = new CandidateScoreInput(
                    Path.GetFileName(file),
                    relativeDirectory.Length == 0 ? string.Empty : relativeDirectory.Split('/')[^1],
                    kind,
                    size,
                    depthInTree,
                    nameHint,
                    versionInfo);

                var (score, notes) = CandidateRules.Score(scoreInput);

                candidates.Add(new CandidateExecutable
                {
                    RelativePath = relativePath,
                    FullPath = file,
                    FileName = Path.GetFileName(file),
                    RelativeDirectory = relativeDirectory,
                    Kind = kind,
                    SizeBytes = size,
                    Depth = depthInTree,
                    VersionInfo = versionInfo,
                    RuleScore = score,
                    RuleNotes = notes,
                });
            }
        }

        return candidates
            .OrderByDescending(c => c.RuleScore)
            .ThenBy(c => c.Depth)
            .ThenBy(c => c.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static bool IsReparsePoint(string path)
    {
        try
        {
            var attributes = File.GetAttributes(path);
            return (attributes & FileAttributes.ReparsePoint) != 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return true;
        }
    }
}
