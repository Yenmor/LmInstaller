namespace LmInstaller.Core.Configuration;

public enum AppRootValidationStatus
{
    Valid,
    EmptyPath,
    InvalidPath,
    SameAsManagerData,
    SameAsTemporaryDirectory,
    CannotCreate,
    NotWritable,
    NotADirectory,
}

public sealed record AppRootValidationResult(
    AppRootValidationStatus Status,
    string? NormalizedPath,
    string Message)
{
    public bool IsValid => Status == AppRootValidationStatus.Valid;
}

/// <summary>
/// Validates the user-chosen application root before it is saved or used.
/// The root must never resolve to the manager data directory, the temporary directory,
/// or the current working directory by accident.
/// </summary>
public sealed class AppRootValidator
{
    private readonly IReadOnlyList<string> _forbiddenPaths;

    public AppRootValidator(IEnumerable<string>? forbiddenPaths = null)
    {
        _forbiddenPaths = (forbiddenPaths ?? Array.Empty<string>())
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => TrimDirectorySeparators(Path.GetFullPath(p)))
            .ToList();
    }

    public AppRootValidationResult Validate(string? candidatePath, bool createIfMissing = true)
    {
        if (string.IsNullOrWhiteSpace(candidatePath))
        {
            return new AppRootValidationResult(
                AppRootValidationStatus.EmptyPath,
                null,
                "请选择用于存放 portable 应用的应用根目录。");
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(candidatePath.Trim());
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return new AppRootValidationResult(
                AppRootValidationStatus.InvalidPath,
                null,
                "路径格式无效：" + ex.Message);
        }

        if (IsForbidden(fullPath))
        {
            return new AppRootValidationResult(
                AppRootValidationStatus.SameAsManagerData,
                fullPath,
                "该目录与 LmInstaller 自身的数据或临时目录重叠，请选择其他目录。");
        }

        if (File.Exists(fullPath))
        {
            return new AppRootValidationResult(
                AppRootValidationStatus.NotADirectory,
                fullPath,
                "所选路径是一个文件，请选择目录。");
        }

        if (!Directory.Exists(fullPath))
        {
            if (!createIfMissing)
            {
                return new AppRootValidationResult(
                    AppRootValidationStatus.CannotCreate,
                    fullPath,
                    "目录不存在。");
            }

            try
            {
                Directory.CreateDirectory(fullPath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
            {
                return new AppRootValidationResult(
                    AppRootValidationStatus.CannotCreate,
                    fullPath,
                    "无法创建目录：" + ex.Message);
            }
        }

        var writable = TryWriteProbe(fullPath);
        if (!writable)
        {
            return new AppRootValidationResult(
                AppRootValidationStatus.NotWritable,
                fullPath,
                "目录不可写，请检查权限或选择其他目录。");
        }

        return new AppRootValidationResult(AppRootValidationStatus.Valid, fullPath, "目录可用。");
    }

    private bool IsForbidden(string fullPath)
    {
        var normalized = TrimDirectorySeparators(fullPath);
        foreach (var forbidden in _forbiddenPaths)
        {
            if (string.Equals(normalized, forbidden, PathComparison) ||
                IsUnder(normalized, forbidden) ||
                IsUnder(forbidden, normalized))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsUnder(string path, string ancestor)
    {
        return path.StartsWith(ancestor + Path.DirectorySeparatorChar, PathComparison);
    }

    private static StringComparison PathComparison =>
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private static string TrimDirectorySeparators(string path)
    {
        if (path.Length <= 1)
        {
            return path;
        }

        return path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private static bool TryWriteProbe(string directory)
    {
        var probe = Path.Combine(directory, ".lminstaller-write-probe-" + Guid.NewGuid().ToString("N"));
        try
        {
            File.WriteAllText(probe, string.Empty);
            File.Delete(probe);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return false;
        }
        finally
        {
            try
            {
                if (File.Exists(probe))
                {
                    File.Delete(probe);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Ignored: a leftover probe file does not block validation.
            }
        }
    }
}
