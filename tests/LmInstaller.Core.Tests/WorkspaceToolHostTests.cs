using System.Text.Json;
using LmInstaller.Core.Llm;
using LmInstaller.Core.Scanning;
using LmInstaller.Core.Tests.Support;

namespace LmInstaller.Core.Tests;

public sealed class WorkspaceToolHostTests
{
    private static (TestDirectory Temp, ReadOnlyWorkspaceToolHost Host) CreateHost()
    {
        var temp = new TestDirectory();
        temp.WriteFile("MyApp/MyApp.exe", "binary");
        temp.WriteFile("MyApp/readme.txt", "hello world");
        temp.WriteFile("MyApp/libs/core.dll", "dll");
        var candidates = new ExecutableCandidateScanner().Scan(temp.Root, "MyApp");
        return (temp, new ReadOnlyWorkspaceToolHost(temp.Root, candidates));
    }

    [Fact]
    public async Task List_directory_lists_files_and_directories()
    {
        var (temp, host) = CreateHost();
        using var _ = temp;

        var result = await host.InvokeAsync("list_directory", """{"path":"MyApp"}""");

        Assert.False(result.IsError);
        using var document = JsonDocument.Parse(result.Content);
        var entries = document.RootElement.GetProperty("entries");
        Assert.Contains(entries.EnumerateArray(), e => e.GetProperty("name").GetString() == "MyApp.exe");
        Assert.Contains(entries.EnumerateArray(), e => e.GetProperty("name").GetString() == "libs");
    }

    [Fact]
    public async Task Read_text_file_returns_content()
    {
        var (temp, host) = CreateHost();
        using var _ = temp;

        var result = await host.InvokeAsync("read_text_file", """{"path":"MyApp/readme.txt"}""");

        Assert.False(result.IsError);
        Assert.Contains("hello world", result.Content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Tools_reject_paths_outside_the_workspace()
    {
        var (temp, host) = CreateHost();
        using var _ = temp;

        var traversal = await host.InvokeAsync("read_text_file", """{"path":"../../etc/passwd"}""");
        var absolute = await host.InvokeAsync("list_directory", """{"path":"/etc"}""");

        Assert.True(traversal.IsError);
        Assert.True(absolute.IsError);
    }

    [Fact]
    public async Task Get_file_info_exposes_candidate_metadata()
    {
        var (temp, host) = CreateHost();
        using var _ = temp;

        var result = await host.InvokeAsync("get_file_info", """{"path":"MyApp/MyApp.exe"}""");

        Assert.False(result.IsError);
        using var document = JsonDocument.Parse(result.Content);
        Assert.True(document.RootElement.GetProperty("isCandidate").GetBoolean());
    }

    [Fact]
    public async Task Find_executables_filters_by_extension()
    {
        var (temp, host) = CreateHost();
        using var _ = temp;

        var result = await host.InvokeAsync("find_executables", """{"extension":"dll"}""");

        Assert.False(result.IsError);
        using var document = JsonDocument.Parse(result.Content);
        Assert.Equal(0, document.RootElement.GetProperty("count").GetInt32());
    }

    [Fact]
    public async Task Unknown_tools_return_an_error_instead_of_throwing()
    {
        var (temp, host) = CreateHost();
        using var _ = temp;

        var result = await host.InvokeAsync("run_program", "{}");

        Assert.True(result.IsError);
    }
}
