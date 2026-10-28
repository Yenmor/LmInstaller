using System.Text.Json;
using System.Text.Json.Serialization;
using LmInstaller.Core.Platform;

namespace LmInstaller.Core.Configuration;

/// <summary>
/// Loads and saves <see cref="ManagerSettings"/> as JSON under the manager data directory.
/// Loading is tolerant: a corrupt file is moved aside instead of crashing the application.
/// </summary>
public sealed class ManagerSettingsStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly string _configFilePath;

    public ManagerSettingsStore(LmInstallerPaths paths)
        : this(paths?.ConfigFile ?? throw new ArgumentNullException(nameof(paths)))
    {
    }

    public ManagerSettingsStore(string configFilePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configFilePath);
        _configFilePath = Path.GetFullPath(configFilePath);
    }

    public string ConfigFilePath => _configFilePath;

    public ManagerSettings Load()
    {
        if (!File.Exists(_configFilePath))
        {
            return new ManagerSettings();
        }

        try
        {
            var json = File.ReadAllText(_configFilePath);
            return JsonSerializer.Deserialize<ManagerSettings>(json, SerializerOptions) ?? new ManagerSettings();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            TryQuarantineCorruptFile();
            return new ManagerSettings();
        }
    }

    public void Save(ManagerSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var directory = Path.GetDirectoryName(_configFilePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(settings, SerializerOptions);
        AtomicFile.WriteAllText(_configFilePath, json);
    }

    private void TryQuarantineCorruptFile()
    {
        try
        {
            var backup = _configFilePath + ".corrupt-" + DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            File.Move(_configFilePath, backup, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Nothing else we can do; the caller gets default settings either way.
        }
    }
}
