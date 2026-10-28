using LmInstaller.Core.Llm;
using LmInstaller.Core.Models;
using LmInstaller.Core.Scanning;
using LmInstaller.Core.Tests.Support;

namespace LmInstaller.Core.Tests;

public sealed class MainExecutableSelectorTests
{
    private static CandidateExecutable Candidate(string relativePath, int score = 10) =>
        new()
        {
            RelativePath = relativePath,
            FullPath = "/tmp/" + relativePath,
            FileName = Path.GetFileName(relativePath),
            RelativeDirectory = Path.GetDirectoryName(relativePath)?.Replace('\\', '/') ?? string.Empty,
            Kind = CandidateKind.NativeExecutable,
            SizeBytes = 1024,
            RuleScore = score,
        };

    /// <summary>Creates real files so the LLM result can be validated against the workspace.</summary>
    private static (TestDirectory Temp, SelectionContext Context) CreateWorkspace()
    {
        var temp = new TestDirectory();
        temp.WriteFile("MyApp/MyApp.exe", new string('x', 4096));
        temp.WriteFile("MyApp/helper.exe", "helper");
        var candidates = new ExecutableCandidateScanner().Scan(temp.Root, "MyApp");
        return (temp, new SelectionContext(temp.Root, candidates, "MyApp"));
    }

    private static MainExecutableSelector CreateSelectorWithLlm(
        ILlmChatClient client,
        SelectionContext context) =>
        new(new LlmMainExecutableSelector(
            client,
            new ReadOnlyWorkspaceToolHost(context.ExtractionRoot, context.Candidates)));

    [Fact]
    public async Task Returns_no_candidates_when_nothing_was_found()
    {
        var selector = new MainExecutableSelector();

        var outcome = await selector.SelectAsync(new SelectionContext("/tmp/workspace", Array.Empty<CandidateExecutable>(), "MyApp"));

        Assert.Equal(SelectionStatus.NoCandidates, outcome.Status);
        Assert.Empty(outcome.Candidates);
    }

    [Fact]
    public async Task Falls_back_to_manual_selection_without_an_llm()
    {
        var selector = new MainExecutableSelector();
        var candidates = new[] { Candidate("MyApp/MyApp.exe", 70), Candidate("MyApp/unins000.exe", -100) };

        var outcome = await selector.SelectAsync(new SelectionContext("/tmp/workspace", candidates, "MyApp"));

        Assert.Equal(SelectionStatus.NeedsUserSelection, outcome.Status);
        Assert.NotNull(outcome.SuggestedSelection);
        Assert.Equal("MyApp/MyApp.exe", outcome.SuggestedSelection!.RelativePath);
        Assert.Equal(SelectionSource.Rules, outcome.SuggestedSelection.Source);
        Assert.Contains("未配置 LLM", outcome.Diagnosis!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Accepts_a_confident_llm_decision()
    {
        var (temp, context) = CreateWorkspace();
        using var _ = temp;
        var client = new FakeLlmChatClient(FakeLlmChatClient.Final(
            """{"path":"MyApp/MyApp.exe","reason":"主程序","confidence":0.95}"""));

        var outcome = await CreateSelectorWithLlm(client, context).SelectAsync(context);

        Assert.Equal(SelectionStatus.Selected, outcome.Status);
        Assert.Equal("MyApp/MyApp.exe", outcome.Selection!.RelativePath);
        Assert.False(outcome.RequiresUserDecision);
    }

    [Fact]
    public async Task Asks_the_user_when_confidence_is_low()
    {
        var (temp, context) = CreateWorkspace();
        using var _ = temp;
        var client = new FakeLlmChatClient(FakeLlmChatClient.Final(
            """{"path":"MyApp/helper.exe","reason":"不确定","confidence":0.2}"""));

        var outcome = await CreateSelectorWithLlm(client, context).SelectAsync(context);

        Assert.Equal(SelectionStatus.NeedsUserSelection, outcome.Status);
        Assert.Contains("置信度", outcome.Diagnosis!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Falls_back_when_the_llm_is_unavailable()
    {
        var (temp, context) = CreateWorkspace();
        using var _ = temp;
        var client = new ThrowingLlmChatClient(new LlmException("网络不可达"));

        var outcome = await CreateSelectorWithLlm(client, context).SelectAsync(context);

        Assert.Equal(SelectionStatus.NeedsUserSelection, outcome.Status);
        Assert.Contains("网络不可达", outcome.Diagnosis!, StringComparison.Ordinal);
        Assert.NotNull(outcome.SuggestedSelection);
    }
}
