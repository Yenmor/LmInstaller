using LmInstaller.Core.Models;
using LmInstaller.Core.Scanning;
using LmInstaller.Core.Tests.Support;

namespace LmInstaller.Core.Tests;

public sealed class ExecutableCandidateScannerTests
{
    private static TestDirectory CreateSampleTree()
    {
        var temp = new TestDirectory();
        temp.WriteFile("MyApp/MyApp.exe", new string('a', 4096));
        temp.WriteFile("MyApp/unins000.exe", "uninstaller");
        temp.WriteFile("MyApp/helper.exe", "helper");
        temp.WriteFile("MyApp/readme.txt", "docs");
        temp.WriteFile("MyApp/libs/core.dll", "dll");
        temp.WriteFile("MyApp/tools/script.bat", "echo hi");
        return temp;
    }

    [Fact]
    public void Scan_finds_executables_and_ignores_other_files()
    {
        using var temp = CreateSampleTree();

        var candidates = new ExecutableCandidateScanner().Scan(temp.Root, "MyApp");

        var paths = candidates.Select(c => c.RelativePath).ToList();
        Assert.Contains("MyApp/MyApp.exe", paths);
        Assert.Contains("MyApp/unins000.exe", paths);
        Assert.Contains("MyApp/tools/script.bat", paths);
        Assert.DoesNotContain("MyApp/readme.txt", paths);
        Assert.DoesNotContain("MyApp/libs/core.dll", paths);
    }

    [Fact]
    public void Scan_reports_the_relative_directory_and_kind()
    {
        using var temp = CreateSampleTree();

        var candidates = new ExecutableCandidateScanner().Scan(temp.Root);
        var script = candidates.Single(c => c.FileName == "script.bat");

        Assert.Equal("MyApp/tools", script.RelativeDirectory);
        Assert.Equal(2, script.Depth);
        Assert.Equal(CandidateKind.CommandScript, script.Kind);
        Assert.True(script.IsNativeExecutable == false);
    }

    [Fact]
    public void Scan_ranks_the_product_executable_above_an_uninstaller()
    {
        using var temp = CreateSampleTree();

        var candidates = new ExecutableCandidateScanner().Scan(temp.Root, "MyApp");
        var main = candidates.Single(c => c.FileName == "MyApp.exe");
        var uninstaller = candidates.Single(c => c.FileName == "unins000.exe");

        Assert.True(main.RuleScore > uninstaller.RuleScore);
        Assert.Contains(uninstaller.RuleNotes, note => note.Contains("卸载", StringComparison.Ordinal));
        Assert.Equal("MyApp/MyApp.exe", candidates[0].RelativePath);
    }

    [Fact]
    public void Scan_can_exclude_scripts()
    {
        using var temp = CreateSampleTree();

        var candidates = new ExecutableCandidateScanner(new ScannerOptions { IncludeScripts = false }).Scan(temp.Root);

        Assert.DoesNotContain(candidates, c => c.Kind == CandidateKind.CommandScript);
    }

    [Fact]
    public void Scan_skips_macos_metadata_directories()
    {
        using var temp = new TestDirectory();
        temp.WriteFile("MyApp/MyApp.exe", "binary");
        temp.WriteFile("__MACOSX/._MyApp.exe", "metadata");

        var candidates = new ExecutableCandidateScanner().Scan(temp.Root);

        Assert.DoesNotContain(candidates, c => c.RelativePath.StartsWith("__MACOSX/", StringComparison.Ordinal));
        Assert.Contains(candidates, c => c.RelativePath == "MyApp/MyApp.exe");
    }

    [Fact]
    public void Scan_returns_an_empty_list_when_nothing_is_found()
    {
        using var temp = new TestDirectory();
        temp.WriteFile("docs/readme.txt", "nothing to run");

        var candidates = new ExecutableCandidateScanner().Scan(temp.Root);

        Assert.Empty(candidates);
    }
}
