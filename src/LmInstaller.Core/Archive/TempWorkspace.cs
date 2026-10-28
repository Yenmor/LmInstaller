namespace LmInstaller.Core.Archive;

/// <summary>
/// An isolated directory used for one analysis. Created under the manager's temporary
/// directory (never inside the application root) and deleted on dispose.
/// </summary>
public sealed class TempWorkspace : IDisposable
{
    private bool _disposed;

    public TempWorkspace(string rootPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        RootPath = Path.GetFullPath(rootPath);
        Directory.CreateDirectory(RootPath);
    }

    public string RootPath { get; }

    public string CreateSubdirectory(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var path = Path.Combine(RootPath, name);
        Directory.CreateDirectory(path);
        return path;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        TempDirectory.Delete(RootPath);
    }
}

public interface ITempWorkspaceFactory
{
    TempWorkspace Create();
}

/// <summary>Creates unique workspaces under a base directory.</summary>
public sealed class TempWorkspaceFactory : ITempWorkspaceFactory
{
    private readonly string _baseDirectory;

    public TempWorkspaceFactory(string? baseDirectory = null)
    {
        _baseDirectory = string.IsNullOrWhiteSpace(baseDirectory)
            ? Path.Combine(Path.GetTempPath(), "LmInstaller", "workspaces")
            : Path.GetFullPath(baseDirectory);
    }

    public string BaseDirectory => _baseDirectory;

    public TempWorkspace Create()
    {
        Directory.CreateDirectory(_baseDirectory);
        var root = Path.Combine(_baseDirectory, Guid.NewGuid().ToString("N"));
        return new TempWorkspace(root);
    }
}

internal static class TempDirectory
{
    /// <summary>Deletes a directory only when it exists and is empty.</summary>
    public static void DeleteIfEmpty(string path)
    {
        try
        {
            if (Directory.Exists(path) && !Directory.EnumerateFileSystemEntries(path).Any())
            {
                Directory.Delete(path);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best effort.
        }
    }

    public static void Delete(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best effort: a leftover workspace is not dangerous.
        }
    }
}
