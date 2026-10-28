using LmInstaller.Core.Installation;
using LmInstaller.Core.Manifest;
using LmInstaller.Core.Tests.Support;

namespace LmInstaller.Core.Tests;

public sealed class AppInstallerTests
{
    private static (TestDirectory Temp, string AppRoot, string Source) CreatePayload()
    {
        var temp = new TestDirectory();
        var appRoot = temp.CreateSubdirectory("apps");
        var source = temp.CreateSubdirectory("source", "MyApp");
        File.WriteAllText(Path.Combine(source, "MyApp.exe"), "binary-v1");
        Directory.CreateDirectory(Path.Combine(source, "libs"));
        File.WriteAllText(Path.Combine(source, "libs", "core.dll"), "dll");
        return (temp, appRoot, source);
    }

    private static InstallRequest CreateRequest(string appRoot, string source, InstallOptions? options = null) => new()
    {
        AppRootPath = appRoot,
        SourceDirectory = source,
        MainExecutableRelativePath = "MyApp.exe",
        ProposedName = "MyApp",
        Options = options ?? InstallOptions.Default,
    };

    [Fact]
    public void Installs_the_full_directory_and_writes_directory_metadata()
    {
        var (temp, appRoot, source) = CreatePayload();
        using var _ = temp;
        var installer = new AppInstaller(new AppRootManifestStore(new TestClock()));

        var result = installer.Install(CreateRequest(appRoot, source));

        Assert.True(File.Exists(Path.Combine(appRoot, "MyApp", "MyApp.exe")));
        Assert.True(File.Exists(Path.Combine(appRoot, "MyApp", "libs", "core.dll")));
        Assert.Equal("MyApp", result.Record.Directory);
        Assert.Equal("MyApp.exe", result.Record.MainExecutable);
        Assert.Empty(result.Warnings);

        var manifestPath = Path.Combine(appRoot, AppRootManifestStore.ManifestFileName);
        Assert.True(File.Exists(manifestPath));
        var manifest = new AppRootManifestStore().Load(appRoot).Manifest;
        var record = Assert.Single(manifest.Apps);
        Assert.Equal("MyApp", record.Directory);
    }

    [Fact]
    public void Fails_on_a_conflict_by_default()
    {
        var (temp, appRoot, source) = CreatePayload();
        using var _ = temp;
        var installer = new AppInstaller(new AppRootManifestStore());
        installer.Install(CreateRequest(appRoot, source));

        Assert.Throws<InstallConflictException>(() => installer.Install(CreateRequest(appRoot, source)));
    }

    [Fact]
    public void Can_install_next_to_an_existing_folder()
    {
        var (temp, appRoot, source) = CreatePayload();
        using var _ = temp;
        var installer = new AppInstaller(new AppRootManifestStore());
        installer.Install(CreateRequest(appRoot, source));

        var result = installer.Install(CreateRequest(appRoot, source, new InstallOptions
        {
            ConflictPolicy = ConflictPolicy.UseNewName,
        }));

        Assert.Equal("MyApp (2)", result.Record.Directory);
        Assert.True(File.Exists(Path.Combine(appRoot, "MyApp (2)", "MyApp.exe")));
    }

    [Fact]
    public void Overwrite_requires_explicit_confirmation()
    {
        var (temp, appRoot, source) = CreatePayload();
        using var _ = temp;
        var installer = new AppInstaller(new AppRootManifestStore());
        installer.Install(CreateRequest(appRoot, source));

        Assert.Throws<InstallConflictException>(() => installer.Install(
            CreateRequest(appRoot, source, new InstallOptions { ConflictPolicy = ConflictPolicy.Overwrite })));
    }

    [Fact]
    public void Overwrite_replaces_the_existing_folder_when_confirmed()
    {
        var (temp, appRoot, source) = CreatePayload();
        using var _ = temp;
        var installer = new AppInstaller(new AppRootManifestStore());
        installer.Install(CreateRequest(appRoot, source));

        File.WriteAllText(Path.Combine(source, "MyApp.exe"), "binary-v2");
        installer.Install(CreateRequest(appRoot, source, new InstallOptions
        {
            ConflictPolicy = ConflictPolicy.Overwrite,
            ConfirmOverwrite = true,
        }));

        Assert.Equal("binary-v2", File.ReadAllText(Path.Combine(appRoot, "MyApp", "MyApp.exe")));
        Assert.False(Directory.Exists(Path.Combine(appRoot, ".lminstaller-staging")));
    }

    [Fact]
    public void Rejects_a_main_executable_outside_the_payload()
    {
        var (temp, appRoot, source) = CreatePayload();
        using var _ = temp;
        var installer = new AppInstaller(new AppRootManifestStore());
        var request = CreateRequest(appRoot, source) with { MainExecutableRelativePath = "../evil.exe" };

        Assert.Throws<InstallException>(() => installer.Install(request));
    }

    [Fact]
    public void Rejects_a_missing_main_executable()
    {
        var (temp, appRoot, source) = CreatePayload();
        using var _ = temp;
        var installer = new AppInstaller(new AppRootManifestStore());
        var request = CreateRequest(appRoot, source) with { MainExecutableRelativePath = "not-there.exe" };

        Assert.Throws<InstallException>(() => installer.Install(request));
    }

    [Fact]
    public void Registers_multiple_applications_in_one_manifest()
    {
        var (temp, appRoot, source) = CreatePayload();
        using var _ = temp;
        var secondSource = temp.CreateSubdirectory("source", "OtherApp");
        File.WriteAllText(Path.Combine(secondSource, "OtherApp.exe"), "binary");

        var installer = new AppInstaller(new AppRootManifestStore());
        installer.Install(CreateRequest(appRoot, source));
        installer.Install(CreateRequest(appRoot, secondSource) with
        {
            ProposedName = "OtherApp",
            MainExecutableRelativePath = "OtherApp.exe",
        });

        var manifest = new AppRootManifestStore().Load(appRoot).Manifest;
        Assert.Equal(2, manifest.Apps.Count);
        Assert.Contains(manifest.Apps, a => a.Directory == "MyApp");
        Assert.Contains(manifest.Apps, a => a.Directory == "OtherApp");
    }

    [Fact]
    public void Sanitizes_the_target_folder_name()
    {
        var (temp, appRoot, source) = CreatePayload();
        using var _ = temp;
        var installer = new AppInstaller(new AppRootManifestStore());

        var result = installer.Install(CreateRequest(appRoot, source) with { ProposedName = "Bad:Name?" });

        Assert.Equal("Bad Name", result.Record.Directory);
        Assert.True(Directory.Exists(Path.Combine(appRoot, "Bad Name")));
    }
}
