using LmInstaller.Core.Archive;

namespace LmInstaller.Core.Installation;

/// <summary>
/// Copies a directory tree without ever following reparse points (symlinks/junctions) and without
/// letting any destination escape the target root.
/// </summary>
internal static class DirectoryCopier
{
    public static int CopyTree(string sourceRoot, string destinationRoot, long maxBytes = long.MaxValue)
    {
        var source = Path.GetFullPath(sourceRoot);
        var destination = Path.GetFullPath(destinationRoot);

        if (!Directory.Exists(source))
        {
            throw new DirectoryNotFoundException("源目录不存在：" + source);
        }

        Directory.CreateDirectory(destination);

        var pending = new Stack<string>();
        pending.Push(source);
        long copiedBytes = 0;
        var fileCount = 0;

        while (pending.Count > 0)
        {
            var current = pending.Pop();

            foreach (var directory in Directory.EnumerateDirectories(current))
            {
                if (IsReparsePoint(directory))
                {
                    continue;
                }

                var relative = Path.GetRelativePath(source, directory);
                var target = Path.GetFullPath(Path.Combine(destination, relative));
                EnsureWithin(destination, target);
                Directory.CreateDirectory(target);
                pending.Push(directory);
            }

            foreach (var file in Directory.EnumerateFiles(current))
            {
                if (IsReparsePoint(file))
                {
                    continue;
                }

                var relative = Path.GetRelativePath(source, file);
                var target = Path.GetFullPath(Path.Combine(destination, relative));
                EnsureWithin(destination, target);

                var parent = Path.GetDirectoryName(target);
                if (!string.IsNullOrEmpty(parent))
                {
                    Directory.CreateDirectory(parent);
                }

                File.Copy(file, target, overwrite: true);
                copiedBytes += new FileInfo(target).Length;
                fileCount++;

                if (copiedBytes > maxBytes)
                {
                    throw new InstallException("复制内容超过允许的大小上限。");
                }
            }
        }

        return fileCount;
    }

    private static void EnsureWithin(string root, string candidate)
    {
        if (!ArchivePathSafety.IsWithinRoot(root, candidate))
        {
            throw new InstallException("复制目标超出应用目录：" + candidate);
        }
    }

    private static bool IsReparsePoint(string path)
    {
        try
        {
            return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return true;
        }
    }
}
