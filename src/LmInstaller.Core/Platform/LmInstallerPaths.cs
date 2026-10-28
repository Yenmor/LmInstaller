namespace LmInstaller.Core.Platform;

/// <summary>
/// Locations owned by LmInstaller itself (configuration, logs, cache, temporary workspaces).
/// These are deliberately independent from the user-chosen application root so that moving or
/// cleaning the application root never destroys manager state, and vice versa.
/// </summary>
public sealed class LmInstallerPaths
{
    public LmInstallerPaths(string? baseDirectory = null)
    {
        var root = string.IsNullOrWhiteSpace(baseDirectory)
            ? Path.Combine(GetDefaultBaseDirectory(), "LmInstaller")
            : Path.GetFullPath(baseDirectory);

        BaseDirectory = root;
        ConfigFile = Path.Combine(root, "settings.json");
        LogDirectory = Path.Combine(root, "logs");
        CacheDirectory = Path.Combine(root, "cache");
        TempWorkspaceDirectory = Path.Combine(root, "temp");
    }

    public string BaseDirectory { get; }

    public string ConfigFile { get; }

    public string LogDirectory { get; }

    public string CacheDirectory { get; }

    /// <summary>Root for per-analysis temporary workspaces. Never placed inside the application root.</summary>
    public string TempWorkspaceDirectory { get; }

    public void EnsureDirectories()
    {
        Directory.CreateDirectory(BaseDirectory);
        Directory.CreateDirectory(LogDirectory);
        Directory.CreateDirectory(CacheDirectory);
        Directory.CreateDirectory(TempWorkspaceDirectory);
    }

    private static string GetDefaultBaseDirectory()
    {
        var appData = Environment.GetFolderPath(
            Environment.SpecialFolder.ApplicationData,
            Environment.SpecialFolderOption.DoNotVerify);

        if (string.IsNullOrWhiteSpace(appData))
        {
            // Fall back to the user profile, but never to the application directory or the CWD.
            appData = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        }

        return appData;
    }
}
