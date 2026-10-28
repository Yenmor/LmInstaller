namespace LmInstaller.Core.Pipeline;

/// <summary>
/// Decides which directory inside the extraction root becomes the installed application folder.
/// When the archive wraps everything in a single top-level folder, that folder is the payload;
/// otherwise the extraction root itself is used so no required files are left behind.
/// </summary>
public static class PayloadRootResolver
{
    public static (string PayloadRoot, string MainExecutableRelativePath) Resolve(
        string extractionRoot,
        string mainExecutableRelativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(extractionRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(mainExecutableRelativePath);

        var root = Path.GetFullPath(extractionRoot);
        var relative = mainExecutableRelativePath.Replace('\\', '/').Trim('/');
        var firstSegment = relative.Split('/', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();

        if (string.IsNullOrEmpty(firstSegment))
        {
            throw new ArgumentException("主程序相对路径无效。", nameof(mainExecutableRelativePath));
        }

        if (!relative.Contains('/'))
        {
            return (root, relative);
        }

        var topLevelEntries = EnumerateTopLevel(root);
        if (topLevelEntries.Count == 1 &&
            string.Equals(topLevelEntries[0].Name, firstSegment, StringComparison.OrdinalIgnoreCase) &&
            topLevelEntries[0].IsDirectory)
        {
            var payloadRoot = Path.Combine(root, topLevelEntries[0].Name);
            var nested = relative[(firstSegment.Length + 1)..];
            return (payloadRoot, nested);
        }

        return (root, relative);
    }

    private static List<(string Name, bool IsDirectory)> EnumerateTopLevel(string root)
    {
        var entries = new List<(string, bool)>();
        foreach (var directory in Directory.EnumerateDirectories(root))
        {
            entries.Add((Path.GetFileName(directory), true));
        }

        foreach (var file in Directory.EnumerateFiles(root))
        {
            entries.Add((Path.GetFileName(file), false));
        }

        return entries;
    }
}
