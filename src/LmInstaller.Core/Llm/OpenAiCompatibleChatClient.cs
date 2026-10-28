using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace LmInstaller.Core.Llm;

/// <summary>
/// Talks to any OpenAI-compatible <c>/chat/completions</c> endpoint (OpenAI, Azure-compatible
/// gateways, local servers such as Ollama or LM Studio). It only performs the HTTP call; the
/// caller decides which tools are exposed and what the results mean.
/// </summary>
public sealed class OpenAiCompatibleChatClient : ILlmChatClient
{
    private readonly HttpClient _httpClient;
    private readonly LlmOptions _options;
    private readonly string _endpoint;

    public OpenAiCompatibleChatClient(HttpClient httpClient, LlmOptions options)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(options);

        if (!options.IsConfigured)
        {
            throw new ArgumentException("LLM 未配置（至少需要 BaseUrl 和 Model）。", nameof(options));
        }

        _httpClient = httpClient;
        _options = options;
        _endpoint = BuildEndpoint(options.BaseUrl!);
    }

    public static HttpClient CreateHttpClient(LlmOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds <= 0 ? 120 : options.TimeoutSeconds),
        };
    }

    private static string BuildEndpoint(string baseUrl)
    {
        var trimmed = baseUrl.Trim().TrimEnd('/');
        return trimmed.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase)
            ? trimmed
            : trimmed + "/chat/completions";
    }

    public async Task<LlmChatResponse> CompleteAsync(
        LlmChatRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        using var content = new StringContent(
            BuildRequestBody(request),
            Encoding.UTF8,
            "application/json");

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, _endpoint) { Content = content };
        if (!string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey!.Trim());
        }

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(
                httpRequest,
                HttpCompletionOption.ResponseContentRead,
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            throw new LlmException("调用 LLM 失败：" + ex.Message, ex);
        }

        using (response)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                throw new LlmException(
                    $"LLM 返回错误状态 {(int)response.StatusCode}：{Truncate(body, 500)}");
            }

            return ParseResponse(body);
        }
    }

    private string BuildRequestBody(LlmChatRequest request)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("model", _options.Model);
            writer.WriteNumber("temperature", _options.Temperature);
            if (_options.MaxTokens > 0)
            {
                writer.WriteNumber("max_tokens", _options.MaxTokens);
            }

            writer.WritePropertyName("messages");
            writer.WriteStartArray();
            foreach (var message in request.Messages)
            {
                WriteMessage(writer, message);
            }

            writer.WriteEndArray();

            if (request.Tools is { Count: > 0 })
            {
                writer.WritePropertyName("tools");
                writer.WriteStartArray();
                foreach (var tool in request.Tools)
                {
                    writer.WriteStartObject();
                    writer.WriteString("type", "function");
                    writer.WritePropertyName("function");
                    writer.WriteStartObject();
                    writer.WriteString("name", tool.Name);
                    writer.WriteString("description", tool.Description);
                    writer.WritePropertyName("parameters");
                    using (var schema = JsonDocument.Parse(tool.ParametersJsonSchema))
                    {
                        schema.RootElement.WriteTo(writer);
                    }

                    writer.WriteEndObject();
                    writer.WriteEndObject();
                }

                writer.WriteEndArray();
            }

            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static void WriteMessage(Utf8JsonWriter writer, LlmMessage message)
    {
        writer.WriteStartObject();
        writer.WriteString("role", message.Role switch
        {
            LlmRole.System => "system",
            LlmRole.User => "user",
            LlmRole.Assistant => "assistant",
            LlmRole.Tool => "tool",
            _ => "user",
        });

        if (message.Role == LlmRole.Tool)
        {
            writer.WriteString("tool_call_id", message.ToolCallId);
            writer.WriteString("content", message.Content ?? string.Empty);
        }
        else if (message.Content is not null)
        {
            writer.WriteString("content", message.Content);
        }
        else if (message.Role == LlmRole.Assistant && message.ToolCalls is null)
        {
            writer.WriteString("content", string.Empty);
        }

        if (message.ToolCalls is { Count: > 0 })
        {
            writer.WritePropertyName("tool_calls");
            writer.WriteStartArray();
            foreach (var call in message.ToolCalls)
            {
                writer.WriteStartObject();
                writer.WriteString("id", call.Id);
                writer.WriteString("type", "function");
                writer.WritePropertyName("function");
                writer.WriteStartObject();
                writer.WriteString("name", call.Name);
                writer.WriteString("arguments", call.ArgumentsJson);
                writer.WriteEndObject();
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
        }

        writer.WriteEndObject();
    }

    private static LlmChatResponse ParseResponse(string body)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(body);
        }
        catch (JsonException ex)
        {
            throw new LlmException("无法解析 LLM 响应：" + Truncate(body, 300), ex);
        }

        using (document)
        {
            if (!document.RootElement.TryGetProperty("choices", out var choices) ||
                choices.ValueKind != JsonValueKind.Array ||
                choices.GetArrayLength() == 0)
            {
                throw new LlmException("LLM 响应缺少 choices 字段：" + Truncate(body, 300));
            }

            if (!choices[0].TryGetProperty("message", out var message))
            {
                throw new LlmException("LLM 响应缺少 message 字段：" + Truncate(body, 300));
            }

            var content = ReadContent(message);
            var toolCalls = ReadToolCalls(message);
            return new LlmChatResponse(content, toolCalls);
        }
    }

    private static string? ReadContent(JsonElement message)
    {
        if (!message.TryGetProperty("content", out var content))
        {
            return null;
        }

        if (content.ValueKind == JsonValueKind.String)
        {
            var text = content.GetString();
            return string.IsNullOrWhiteSpace(text) ? null : text;
        }

        if (content.ValueKind == JsonValueKind.Array)
        {
            var builder = new StringBuilder();
            foreach (var part in content.EnumerateArray())
            {
                if (part.ValueKind == JsonValueKind.Object &&
                    part.TryGetProperty("text", out var textPart) &&
                    textPart.ValueKind == JsonValueKind.String)
                {
                    builder.Append(textPart.GetString());
                }
            }

            return builder.Length == 0 ? null : builder.ToString();
        }

        return null;
    }

    private static IReadOnlyList<LlmToolCall> ReadToolCalls(JsonElement message)
    {
        if (!message.TryGetProperty("tool_calls", out var toolCalls) ||
            toolCalls.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<LlmToolCall>();
        }

        var calls = new List<LlmToolCall>();
        foreach (var item in toolCalls.EnumerateArray())
        {
            if (!item.TryGetProperty("function", out var function))
            {
                continue;
            }

            var name = function.TryGetProperty("name", out var nameElement) ? nameElement.GetString() : null;
            var arguments = function.TryGetProperty("arguments", out var argumentsElement)
                ? argumentsElement.GetString() ?? "{}"
                : "{}";

            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            var id = item.TryGetProperty("id", out var idElement) ? idElement.GetString() : null;
            calls.Add(new LlmToolCall(
                string.IsNullOrWhiteSpace(id) ? Guid.NewGuid().ToString("N") : id!,
                name!,
                arguments));
        }

        return calls;
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength] + "…";
}
