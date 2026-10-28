using System.Text.Json;
using System.Text.Json.Serialization;
using LmInstaller.Core.Diagnostics;
using LmInstaller.Core.Platform;

namespace LmInstaller.Core.Manifest;

public sealed record ManifestLoadResult(AppRootManifest Manifest, bool RecoveredFromBackup, string? Warning);

/// <summary>Reads and writes the application-root manifest (<c>LmInstaller.apps.json</c>).</summary>
public sealed class AppRootManifestStore
{
    public const string ManifestFileName = "LmInstaller.apps.json";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly IClock _clock;

    public AppRootManifestStore(IClock? clock = null)
    {
        _clock = clock ?? SystemClock.Instance;
    }

    public string GetManifestPath(string appRootPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(appRootPath);
        return Path.Combine(Path.GetFullPath(appRootPath), ManifestFileName);
    }

    public ManifestLoadResult Load(string appRootPath)
    {
        var path = GetManifestPath(appRootPath);
        if (!File.Exists(path))
        {
            return new ManifestLoadResult(
                new AppRootManifest { UpdatedAtUtc = _clock.UtcNow },
                RecoveredFromBackup: false,
                Warning: null);
        }

        try
        {
            var json = File.ReadAllText(path);
            var manifest = JsonSerializer.Deserialize<AppRootManifest>(json, SerializerOptions);
            if (manifest is null)
            {
                return new ManifestLoadResult(
                    new AppRootManifest { UpdatedAtUtc = _clock.UtcNow },
                    RecoveredFromBackup: false,
                    Warning: "应用目录元数据为空，已按空清单处理。");
            }

            return new ManifestLoadResult(
                manifest with { Apps = manifest.Apps ?? Array.Empty<InstalledAppRecord>() },
                RecoveredFromBackup: false,
                Warning: null);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            var backup = path + ".corrupt-" + _clock.UtcNow.ToUnixTimeSeconds();
            try
            {
                File.Move(path, backup, overwrite: true);
            }
            catch (Exception moveEx) when (moveEx is IOException or UnauthorizedAccessException)
            {
                backup = null!;
            }

            return new ManifestLoadResult(
                new AppRootManifest { UpdatedAtUtc = _clock.UtcNow },
                RecoveredFromBackup: backup is not null,
                Warning: $"应用目录元数据无法读取（{ex.Message}），已按空清单处理。");
        }
    }

    public void Save(string appRootPath, AppRootManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        var path = GetManifestPath(appRootPath);
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var normalized = manifest with { UpdatedAtUtc = _clock.UtcNow };
        var json = JsonSerializer.Serialize(normalized, SerializerOptions);
        AtomicFile.WriteAllText(path, json);
    }

    /// <summary>Adds or replaces a record, matching by id first and by folder name second.</summary>
    public static AppRootManifest Upsert(AppRootManifest manifest, InstalledAppRecord record)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(record);

        var apps = manifest.Apps.ToList();
        var index = apps.FindIndex(
            a => string.Equals(a.Id, record.Id, StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(a.Directory, record.Directory, StringComparison.OrdinalIgnoreCase));

        if (index >= 0)
        {
            apps[index] = record;
        }
        else
        {
            apps.Add(record);
        }

        return manifest with { Apps = apps };
    }

    public static AppRootManifest Remove(AppRootManifest manifest, string appIdOrDirectory)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentException.ThrowIfNullOrWhiteSpace(appIdOrDirectory);

        var apps = manifest.Apps
            .Where(a => !string.Equals(a.Id, appIdOrDirectory, StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(a.Directory, appIdOrDirectory, StringComparison.OrdinalIgnoreCase))
            .ToList();

        return manifest with { Apps = apps };
    }
}
