using LmInstaller.Core.Manifest;
using LmInstaller.Core.Tests.Support;

namespace LmInstaller.Core.Tests;

public sealed class AppRootManifestStoreTests
{
    private static InstalledAppRecord Record(string directory, string id = "id-1") => new()
    {
        Id = id,
        Name = directory,
        Directory = directory,
        MainExecutable = directory + ".exe",
        InstalledAtUtc = new DateTimeOffset(2026, 9, 29, 0, 0, 0, TimeSpan.Zero),
    };

    [Fact]
    public void Load_returns_an_empty_manifest_when_the_file_is_missing()
    {
        using var temp = new TestDirectory();

        var result = new AppRootManifestStore(new TestClock()).Load(temp.Root);

        Assert.Empty(result.Manifest.Apps);
        Assert.Null(result.Warning);
    }

    [Fact]
    public void Save_and_Load_round_trip_through_the_application_root()
    {
        using var temp = new TestDirectory();
        var clock = new TestClock();
        var store = new AppRootManifestStore(clock);

        store.Save(temp.Root, new AppRootManifest { Apps = new[] { Record("MyApp") } });
        var loaded = store.Load(temp.Root);

        var record = Assert.Single(loaded.Manifest.Apps);
        Assert.Equal("MyApp", record.Directory);
        Assert.Equal(clock.UtcNow, loaded.Manifest.UpdatedAtUtc);
        Assert.True(File.Exists(Path.Combine(temp.Root, "LmInstaller.apps.json")));
    }

    [Fact]
    public void Upsert_replaces_a_record_with_the_same_directory()
    {
        var manifest = new AppRootManifest { Apps = new[] { Record("MyApp", "old") } };

        var updated = AppRootManifestStore.Upsert(manifest, Record("MyApp", "new"));

        var record = Assert.Single(updated.Apps);
        Assert.Equal("new", record.Id);
    }

    [Fact]
    public void Upsert_appends_a_new_record()
    {
        var manifest = new AppRootManifest { Apps = new[] { Record("MyApp", "a") } };

        var updated = AppRootManifestStore.Upsert(manifest, Record("OtherApp", "b"));

        Assert.Equal(2, updated.Apps.Count);
    }

    [Fact]
    public void Remove_matches_id_or_directory()
    {
        var manifest = new AppRootManifest { Apps = new[] { Record("MyApp", "a"), Record("OtherApp", "b") } };

        var byDirectory = AppRootManifestStore.Remove(manifest, "MyApp");
        var byId = AppRootManifestStore.Remove(manifest, "b");

        Assert.Single(byDirectory.Apps);
        Assert.Equal("OtherApp", byDirectory.Apps[0].Directory);
        Assert.Single(byId.Apps);
        Assert.Equal("MyApp", byId.Apps[0].Directory);
    }

    [Fact]
    public void Load_quarantines_a_corrupt_manifest_file()
    {
        using var temp = new TestDirectory();
        var path = Path.Combine(temp.Root, AppRootManifestStore.ManifestFileName);
        File.WriteAllText(path, "this is not json");

        var result = new AppRootManifestStore(new TestClock()).Load(temp.Root);

        Assert.Empty(result.Manifest.Apps);
        Assert.NotNull(result.Warning);
        Assert.True(result.RecoveredFromBackup);
        Assert.False(File.Exists(path));
        Assert.NotEmpty(Directory.GetFiles(temp.Root, "LmInstaller.apps.json.corrupt-*"));
    }
}
