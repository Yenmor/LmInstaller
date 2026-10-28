using LmInstaller.Core.Archive;
using LmInstaller.Core.Installation;
using LmInstaller.Core.Llm;
using LmInstaller.Core.Manifest;
using LmInstaller.Core.Pipeline;
using LmInstaller.Core.Tests.Support;

namespace LmInstaller.Core.Tests;

public sealed class InstallerPipelineTests
{
    private static string CreateArchive(TestDirectory temp)
    {
        return ZipFixture.Write(temp.Combine("MyApp.zip"), archive =>
        {
            archive.AddText("MyApp/MyApp.exe", new string('x', 4096));
            archive.AddText("MyApp/unins000.exe", "uninstaller");
            archive.AddText("MyApp/libs/core.dll", "dll");
            archive.AddText("MyApp/readme.txt", "documentation");
        });
    }

    private static InstallerPipeline CreatePipeline(TestDirectory temp, ILlmChatClient client)
    {
        var selector = new MainExecutableSelector(
            new LlmMainExecutableSelector(client, new ReadOnlyWorkspaceToolHostFactory()));

        return new InstallerPipeline(
            selector: selector,
            workspaceFactory: new TempWorkspaceFactory(temp.Combine("workspaces")),
            installer: new AppInstaller(new AppRootManifestStore(new TestClock())));
    }

    [Fact]
    public async Task Analyse_selects_the_main_program_and_installs_the_full_directory()
    {
        using var temp = new TestDirectory();
        var archive = CreateArchive(temp);
        var appRoot = temp.CreateSubdirectory("apps");
        var client = new FakeLlmChatClient(FakeLlmChatClient.Final(
            """{"path":"MyApp/MyApp.exe","reason":"产品主程序，名称与目录一致","confidence":0.93}"""));
        var pipeline = CreatePipeline(temp, client);

        using var analysis = await pipeline.AnalyzeAsync(archive);

        Assert.Equal(SelectionStatus.Selected, analysis.Selection.Status);
        Assert.Equal("MyApp/MyApp.exe", analysis.Selection.Selection!.RelativePath);
        Assert.Contains(analysis.Candidates, c => c.FileName == "unins000.exe");

        var result = pipeline.Install(
            analysis,
            analysis.Selection.Selection!,
            appRoot,
            new InstallOptions { ConflictPolicy = ConflictPolicy.Fail });

        Assert.Equal("MyApp", result.Record.Directory);
        Assert.True(File.Exists(Path.Combine(appRoot, "MyApp", "MyApp.exe")));
        Assert.True(File.Exists(Path.Combine(appRoot, "MyApp", "libs", "core.dll")));

        var manifest = new AppRootManifestStore().Load(appRoot).Manifest;
        var record = Assert.Single(manifest.Apps);
        Assert.Equal("MyApp", record.Directory);
        Assert.Equal("MyApp.exe", record.MainExecutable);
        Assert.Equal("MyApp.zip", record.SourceArchiveFileName);
        Assert.NotNull(record.SourceArchiveSha256);
    }

    [Fact]
    public async Task Analyse_keeps_the_manual_selection_path_available_for_low_confidence()
    {
        using var temp = new TestDirectory();
        var archive = CreateArchive(temp);
        var client = new FakeLlmChatClient(FakeLlmChatClient.Final(
            """{"path":"MyApp/MyApp.exe","reason":"可能","confidence":0.3}"""));
        var pipeline = CreatePipeline(temp, client);

        using var analysis = await pipeline.AnalyzeAsync(archive);

        Assert.Equal(SelectionStatus.NeedsUserSelection, analysis.Selection.Status);
        Assert.True(analysis.RequiresUserDecision);
        Assert.NotNull(analysis.Selection.SuggestedSelection);
        Assert.Equal("MyApp/MyApp.exe", analysis.Selection.SuggestedSelection!.RelativePath);
    }

    [Fact]
    public async Task Analyse_rejects_an_unsafe_archive_before_extracting()
    {
        using var temp = new TestDirectory();
        var archive = ZipFixture.Write(temp.Combine("evil.zip"), zip =>
        {
            zip.AddText("../escaped.txt", "evil");
            zip.AddText("MyApp/MyApp.exe", "binary");
        });
        var client = new FakeLlmChatClient(FakeLlmChatClient.Final(
            """{"path":"MyApp/MyApp.exe","reason":"x","confidence":0.9}"""));
        var pipeline = CreatePipeline(temp, client);

        await Assert.ThrowsAsync<UnsafeArchiveException>(() => pipeline.AnalyzeAsync(archive));

        Assert.False(File.Exists(temp.Combine("escaped.txt")));
        var workspaceBase = temp.Combine("workspaces");
        Assert.True(!Directory.Exists(workspaceBase) || Directory.GetDirectories(workspaceBase).Length == 0);
    }

    [Fact]
    public async Task Disposing_the_analysis_removes_the_temporary_workspace()
    {
        using var temp = new TestDirectory();
        var archive = CreateArchive(temp);
        var client = new FakeLlmChatClient(FakeLlmChatClient.Final(
            """{"path":"MyApp/MyApp.exe","reason":"main","confidence":0.9}"""));
        var pipeline = CreatePipeline(temp, client);

        string workspaceRoot;
        using (var analysis = await pipeline.AnalyzeAsync(archive))
        {
            workspaceRoot = analysis.Workspace.RootPath;
            Assert.True(Directory.Exists(workspaceRoot));
        }

        Assert.False(Directory.Exists(workspaceRoot));
    }

    [Fact]
    public async Task Analyse_without_an_llm_still_returns_candidates()
    {
        using var temp = new TestDirectory();
        var archive = CreateArchive(temp);
        var pipeline = new InstallerPipeline(
            workspaceFactory: new TempWorkspaceFactory(temp.Combine("workspaces")));

        using var analysis = await pipeline.AnalyzeAsync(archive);

        Assert.Equal(SelectionStatus.NeedsUserSelection, analysis.Selection.Status);
        Assert.NotEmpty(analysis.Candidates);
        Assert.Contains("未配置 LLM", analysis.Selection.Diagnosis!, StringComparison.Ordinal);
    }
}
