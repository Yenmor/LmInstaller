namespace LmInstaller.Core.Manifest;

/// <summary>
/// Directory-local metadata describing which portable applications live in one application root.
/// It is written to the root of that directory so the folder is self-describing: move the whole
/// directory to another disk or machine and the list of installed applications comes along.
/// </summary>
public sealed record AppRootManifest
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    public DateTimeOffset UpdatedAtUtc { get; init; }

    public IReadOnlyList<InstalledAppRecord> Apps { get; init; } = Array.Empty<InstalledAppRecord>();
}
