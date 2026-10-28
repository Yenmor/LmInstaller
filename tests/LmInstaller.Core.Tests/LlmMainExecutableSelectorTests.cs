using LmInstaller.Core.Llm;
using LmInstaller.Core.Models;
using LmInstaller.Core.Scanning;
using LmInstaller.Core.Tests.Support;

namespace LmInstaller.Core.Tests;

public sealed class LlmMainExecutableSelectorTests
{
    private static (TestDirectory Temp, SelectionContext Context) CreateContext()
    {
        var temp = new TestDirectory();
        temp.WriteFile("MyApp/MyApp.exe", new string('x', 4096));
        temp.WriteFile("MyApp/unins000.exe", "uninstaller");
        temp.WriteFile("MyApp/readme.txt", "readme");
        var candidates = new ExecutableCandidateScanner().Scan(temp.Root, "MyApp");
        return (temp, new SelectionContext(temp.Root, candidates, "MyApp"));
    }

    private static LlmMainExecutableSelector CreateSelector(
        ILlmChatClient client,
        SelectionContext context,
        MainExecutableSelectionOptions? options = null) =>
        new(client, new ReadOnlyWorkspaceToolHost(context.ExtractionRoot, context.Candidates), options);

    [Fact]
    public async Task Accepts_a_valid_high_confidence_answer()
    {
        var (temp, context) = CreateContext();
        using var _ = temp;
        var client = new FakeLlmChatClient(FakeLlmChatClient.Final(
            """{"path":"MyApp/MyApp.exe","reason":"产品主程序","confidence":0.92}"""));

        var attempt = await CreateSelector(client, context).TrySelectAsync(context);

        Assert.True(attempt.Succeeded);
        Assert.NotNull(attempt.Selection);
        Assert.Equal("MyApp/MyApp.exe", attempt.Selection!.RelativePath);
        Assert.Equal(SelectionSource.Llm, attempt.Selection.Source);
        Assert.Equal(0.92, attempt.Selection.Confidence, 2);
    }

    [Fact]
    public async Task Runs_a_tool_round_before_deciding()
    {
        var (temp, context) = CreateContext();
        using var _ = temp;
        var client = new FakeLlmChatClient(
            FakeLlmChatClient.ToolCall("call_1", "find_executables", """{"extension":".exe"}"""),
            FakeLlmChatClient.Final("""{"path":"MyApp/MyApp.exe","reason":"最大且命名匹配","confidence":0.8}"""));

        var attempt = await CreateSelector(client, context).TrySelectAsync(context);

        Assert.True(attempt.Succeeded);
        Assert.Equal(2, client.Requests.Count);

        var followUp = client.Requests[1];
        Assert.Contains(followUp.Messages, m => m.Role == LlmRole.Tool && m.ToolCallId == "call_1");
        Assert.NotNull(client.Requests[0].Tools);
        Assert.Contains(client.Requests[0].Tools!, t => t.Name == "find_executables");
    }

    [Fact]
    public async Task Rejects_a_path_that_is_not_a_candidate()
    {
        var (temp, context) = CreateContext();
        using var _ = temp;
        var client = new FakeLlmChatClient(FakeLlmChatClient.Final(
            """{"path":"MyApp/Other.exe","reason":"guess","confidence":0.9}"""));

        var attempt = await CreateSelector(client, context).TrySelectAsync(context);

        Assert.False(attempt.Succeeded);
        Assert.Contains("候选列表", attempt.Diagnosis!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Rejects_a_script_entry_point()
    {
        var (temp, context) = CreateContext();
        using var _ = temp;
        temp.WriteFile("MyApp/run.bat", "echo hi");
        var refreshed = new ExecutableCandidateScanner().Scan(temp.Root, "MyApp");
        var newContext = new SelectionContext(temp.Root, refreshed, "MyApp");
        var client = new FakeLlmChatClient(FakeLlmChatClient.Final(
            """{"path":"MyApp/run.bat","reason":"batch","confidence":0.9}"""));

        var attempt = await CreateSelector(client, newContext).TrySelectAsync(newContext);

        Assert.False(attempt.Succeeded);
        Assert.Contains(".exe", attempt.Diagnosis!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Parses_a_fenced_json_answer()
    {
        var (temp, context) = CreateContext();
        using var _ = temp;
        const string answer = """
            我选择主程序如下：

            ```json
            {"path": "MyApp/MyApp.exe", "reason": "主程序", "confidence": 0.7}
            ```
            """;
        var client = new FakeLlmChatClient(FakeLlmChatClient.Final(answer));

        var attempt = await CreateSelector(client, context).TrySelectAsync(context);

        Assert.True(attempt.Succeeded);
        Assert.Equal("MyApp/MyApp.exe", attempt.Selection!.RelativePath);
    }

    [Fact]
    public async Task Reports_llm_failures_instead_of_throwing()
    {
        var (temp, context) = CreateContext();
        using var _ = temp;
        var client = new ThrowingLlmChatClient(new LlmException("服务不可用"));

        var attempt = await CreateSelector(client, context).TrySelectAsync(context);

        Assert.False(attempt.Succeeded);
        Assert.Contains("服务不可用", attempt.Diagnosis!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Gives_up_after_the_tool_round_budget()
    {
        var (temp, context) = CreateContext();
        using var _ = temp;
        var client = new FakeLlmChatClient(
            FakeLlmChatClient.ToolCall("a", "list_directory", "{}"),
            FakeLlmChatClient.ToolCall("b", "list_directory", "{}"));

        var selector = CreateSelector(client, context, new MainExecutableSelectionOptions { MaxToolRounds = 1 });

        var attempt = await selector.TrySelectAsync(context);

        Assert.False(attempt.Succeeded);
        Assert.NotNull(attempt.Diagnosis);
    }

    [Fact]
    public async Task Normalizes_backslash_paths_from_the_model()
    {
        var (temp, context) = CreateContext();
        using var _ = temp;
        var client = new FakeLlmChatClient(FakeLlmChatClient.Final(
            """{"path":"MyApp\\MyApp.exe","reason":"windows style","confidence":0.8}"""));

        var attempt = await CreateSelector(client, context).TrySelectAsync(context);

        Assert.True(attempt.Succeeded);
        Assert.Equal("MyApp/MyApp.exe", attempt.Selection!.RelativePath);
    }
}
