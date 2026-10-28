namespace LmInstaller.Core.Llm;

public enum LlmRole
{
    System,
    User,
    Assistant,
    Tool,
}

public sealed record LlmToolCall(string Id, string Name, string ArgumentsJson);

public sealed record LlmMessage
{
    public required LlmRole Role { get; init; }

    public string? Content { get; init; }

    public string? ToolCallId { get; init; }

    /// <summary>Tool calls requested by the assistant. Only set for assistant messages.</summary>
    public IReadOnlyList<LlmToolCall>? ToolCalls { get; init; }

    public static LlmMessage System(string content) => new() { Role = LlmRole.System, Content = content };

    public static LlmMessage User(string content) => new() { Role = LlmRole.User, Content = content };

    public static LlmMessage Assistant(string? content, IReadOnlyList<LlmToolCall>? toolCalls = null) =>
        new() { Role = LlmRole.Assistant, Content = content, ToolCalls = toolCalls };

    public static LlmMessage Tool(string toolCallId, string content) =>
        new() { Role = LlmRole.Tool, ToolCallId = toolCallId, Content = content };
}

public sealed record LlmToolDefinition(string Name, string Description, string ParametersJsonSchema);

public sealed record LlmChatRequest
{
    public required IReadOnlyList<LlmMessage> Messages { get; init; }

    public IReadOnlyList<LlmToolDefinition>? Tools { get; init; }
}

public sealed record LlmChatResponse(string? Content, IReadOnlyList<LlmToolCall> ToolCalls);

/// <summary>
/// Minimal chat-completion abstraction. Implementations must not execute anything themselves;
/// tool calls are handled by the caller through <see cref="IWorkspaceToolHost"/>.
/// </summary>
public interface ILlmChatClient
{
    Task<LlmChatResponse> CompleteAsync(LlmChatRequest request, CancellationToken cancellationToken = default);
}

public sealed class LlmException : Exception
{
    public LlmException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
