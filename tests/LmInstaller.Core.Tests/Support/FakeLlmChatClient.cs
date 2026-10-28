using LmInstaller.Core.Llm;

namespace LmInstaller.Core.Tests.Support;

/// <summary>Returns scripted responses in order and records every request for assertions.</summary>
internal sealed class FakeLlmChatClient : ILlmChatClient
{
    private readonly Queue<LlmChatResponse> _responses;

    public FakeLlmChatClient(params LlmChatResponse[] responses)
    {
        _responses = new Queue<LlmChatResponse>(responses);
    }

    public List<LlmChatRequest> Requests { get; } = new();

    public void Enqueue(LlmChatResponse response) => _responses.Enqueue(response);

    public Task<LlmChatResponse> CompleteAsync(
        LlmChatRequest request,
        CancellationToken cancellationToken = default)
    {
        Requests.Add(request);
        if (_responses.Count == 0)
        {
            throw new LlmException("测试脚本没有更多 LLM 响应。");
        }

        return Task.FromResult(_responses.Dequeue());
    }

    public static LlmChatResponse Final(string json) => new(json, Array.Empty<LlmToolCall>());

    public static LlmChatResponse ToolCall(string id, string name, string argumentsJson) =>
        new(null, new[] { new LlmToolCall(id, name, argumentsJson) });
}

internal sealed class ThrowingLlmChatClient : ILlmChatClient
{
    private readonly Exception _exception;

    public ThrowingLlmChatClient(Exception exception) => _exception = exception;

    public Task<LlmChatResponse> CompleteAsync(
        LlmChatRequest request,
        CancellationToken cancellationToken = default) => throw _exception;
}
