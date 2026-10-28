using LmInstaller.Core.Archive;
using LmInstaller.Core.Diagnostics;
using LmInstaller.Core.Manifest;
using LmInstaller.Core.Scanning;

namespace LmInstaller.Core.Installation;

/// <summary>
/// Copies a confirmed payload into the application root and registers it in the directory manifest.
/// The copy happens in a staging folder on the same volume, so a failure never leaves a
/// half-written application directory behind.
/// </summary>
public sealed class AppInstaller
{
    private const string StagingFolderName = ".lminstaller-staging";

    private readonly AppRootManifestStore _manifestStore;
    private readonly IClock _clock;

    public AppInstaller(AppRootManifestStore manifestStore, IClock? clock = null)
    {
        _manifestStore = manifestStore ?? throw new ArgumentNullException(nameof(manifestStore));
        _clock = clock ?? SystemClock.Instance;
    }

    public InstallResult Install(InstallRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.AppRootPath))
        {
            throw new InstallException("未选择应用根目录。");
        }

        if (string.IsNullOrWhiteSpace(request.SourceDirectory))
        {
            throw new InstallException("未提供要安装的应用目录。");
        }

        var appRoot = Path.GetFullPath(request.AppRootPath);
        if (!Directory.Exists(appRoot))
        {
            throw new InstallException("应用根目录不存在：" + appRoot);
        }

        var source = Path.GetFullPath(request.SourceDirectory);
        if (!Directory.Exists(source))
        {
            throw new InstallException("待安装的应用目录不存在：" + source);
        }

        var mainExecutableRelative = NormalizeRelativePath(request.MainExecutableRelativePath);
        var mainExecutableFull = Path.GetFullPath(Path.Combine(source, ToOsPath(mainExecutableRelative)));
        if (!ArchivePathSafety.IsWithinRoot(source, mainExecutableFull))
        {
            throw new InstallException("主程序路径不在待安装目录内：" + request.MainExecutableRelativePath);
        }

        if (!File.Exists(mainExecutableFull))
        {
            throw new InstallException("主程序文件不存在：" + mainExecutableRelative);
        }

        var baseName = WindowsPathSanitizer.ToSafeFolderName(
            request.Options.DisplayNameOverride ?? request.ProposedName,
            "App",
            request.Options.MaxFolderNameLength);

        var targetName = ResolveTargetName(appRoot, baseName, request.Options);
        var targetDirectory = Path.Combine(appRoot, targetName);

        var warnings = new List<string>();
        var stagingRootDirectory = Path.Combine(appRoot, StagingFolderName);
        var stagingDirectory = Path.Combine(stagingRootDirectory, Guid.NewGuid().ToString("N"));
        var payloadDirectory = Path.Combine(stagingDirectory, "payload");
        var backupDirectory = Path.Combine(stagingDirectory, "previous");
        var movedPreviousAway = false;

        try
        {
            Directory.CreateDirectory(payloadDirectory);
            DirectoryCopier.CopyTree(source, payloadDirectory);

            if (Directory.Exists(targetDirectory))
            {
                // ConflictPolicy.Overwrite has already been confirmed by the caller.
                Directory.Move(targetDirectory, backupDirectory);
                movedPreviousAway = true;
            }

            Directory.Move(payloadDirectory, targetDirectory);
        }
        catch (Exception ex) when (ex is not InstallException)
        {
            if (movedPreviousAway && !Directory.Exists(targetDirectory) && Directory.Exists(backupDirectory))
            {
                try
                {
                    Directory.Move(backupDirectory, targetDirectory);
                }
                catch (Exception rollbackEx) when (rollbackEx is IOException or UnauthorizedAccessException)
                {
                    warnings.Add("回滚失败，原目录仍在暂存区：" + backupDirectory);
                }
            }

            TempDirectory.Delete(stagingDirectory);
            TempDirectory.DeleteIfEmpty(stagingRootDirectory);
            throw new InstallException("安装失败：" + ex.Message, ex);
        }

        TempDirectory.Delete(stagingDirectory);
        TempDirectory.DeleteIfEmpty(stagingRootDirectory);

        var installedMainExecutable = Path.Combine(targetDirectory, ToOsPath(mainExecutableRelative));
        if (!File.Exists(installedMainExecutable))
        {
            throw new InstallException("安装后未找到主程序，请检查应用目录：" + installedMainExecutable);
        }

        var versionInfo = request.VersionInfo.IsEmpty
            ? PeVersionInfoReader.TryRead(installedMainExecutable)
            : request.VersionInfo;

        var record = new InstalledAppRecord
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = (request.Options.DisplayNameOverride ?? versionInfo.BestDisplayName ?? baseName).Trim(),
            Directory = targetName,
            MainExecutable = mainExecutableRelative,
            InstalledAtUtc = _clock.UtcNow,
            Version = request.Version ?? versionInfo.FileVersion ?? versionInfo.ProductVersion,
            ProductName = versionInfo.ProductName,
            SourceArchiveFileName = string.IsNullOrWhiteSpace(request.SourceArchivePath)
                ? null
                : Path.GetFileName(request.SourceArchivePath),
            SourceArchiveSizeBytes = TryGetFileSize(request.SourceArchivePath),
            SourceArchiveSha256 = request.SourceArchiveSha256,
            Status = "installed",
        };

        try
        {
            var load = _manifestStore.Load(appRoot);
            if (load.Warning is not null)
            {
                warnings.Add(load.Warning);
            }

            _manifestStore.Save(appRoot, AppRootManifestStore.Upsert(load.Manifest, record));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            warnings.Add("应用文件已就位，但目录元数据写入失败：" + ex.Message);
        }

        return new InstallResult(record, targetDirectory, installedMainExecutable, warnings);
    }

    private static string ResolveTargetName(string appRoot, string baseName, InstallOptions options)
    {
        var targetName = baseName;
        if (!Exists(Path.Combine(appRoot, targetName)))
        {
            return targetName;
        }

        switch (options.ConflictPolicy)
        {
            case ConflictPolicy.Fail:
                throw new InstallConflictException(targetName, Path.Combine(appRoot, targetName));

            case ConflictPolicy.Overwrite:
                if (!options.ConfirmOverwrite)
                {
                    throw new InstallConflictException(targetName, Path.Combine(appRoot, targetName));
                }

                return targetName;

            case ConflictPolicy.UseNewName:
                for (var index = 2; index < 1_000; index++)
                {
                    var candidate = $"{baseName} ({index})";
                    if (!Exists(Path.Combine(appRoot, candidate)))
                    {
                        return candidate;
                    }
                }

                throw new InstallException("无法生成不冲突的目标目录名。");

            default:
                throw new InstallException("未知的冲突处理策略。");
        }
    }

    private static bool Exists(string path) => Directory.Exists(path) || File.Exists(path);

    private static long? TryGetFileSize(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return null;
        }

        try
        {
            return new FileInfo(path).Length;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    internal static string NormalizeRelativePath(string? relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            throw new InstallException("主程序相对路径为空。");
        }

        var unified = relativePath.Trim().Replace('\\', '/');
        if (Path.IsPathRooted(unified) || unified.StartsWith('/'))
        {
            throw new InstallException("主程序路径必须是相对路径：" + relativePath);
        }

        var segments = unified.Split('/', StringSplitOptions.RemoveEmptyEntries);
        foreach (var segment in segments)
        {
            if (segment == "..")
            {
                throw new InstallException("主程序路径不能跳出应用目录：" + relativePath);
            }
        }

        var normalized = string.Join('/', segments);
        if (normalized.Length == 0)
        {
            throw new InstallException("主程序相对路径无效：" + relativePath);
        }

        return normalized;
    }

    private static string ToOsPath(string relativePath) =>
        relativePath.Replace('/', Path.DirectorySeparatorChar);
}
