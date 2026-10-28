using System.Net;
using System.Text;
using System.Text.Json;
using LmInstaller.Core.Llm;
using LmInstaller.Core.Tests.Support;

namespace LmInstaller.Core.Tests;

public sealed class OpenAiCompatibleChatClientTests
{
    private static LlmOptions Options => new()
    {
        BaseUrl = "https://example.test/v1",
        ApiKey = "test-key",
        Model = "test-model",
    };

    private static OpenAiCompatibleChatClient CreateClient(StubHttpMessageHandler handler, LlmOptions? options = null) =>
        new(new HttpClient(handler), options ?? Options);

    [Fact]
    public async Task Sends_the_model_messages_and_tools()
    {
        string? requestBody = null;
        var handler = new StubHttpMessageHandler(request =>
        {
            requestBody = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"choices":[{"message":{"content":"ok"}}]}""", Encoding.UTF8, "application/json"),
            };
        });

        var client = CreateClient(handler);
        var response = await client.CompleteAsync(new LlmChatRequest
        {
            Messages = new[] { LlmMessage.System("system"), LlmMessage.User("user") },
            Tools = new[] { new LlmToolDefinition("list_directory", "list", """{"type":"object"}""") },
        });

        Assert.Equal("ok", response.Content);
        Assert.Equal("https://example.test/v1/chat/completions", handler.LastRequest!.RequestUri!.ToString());
        Assert.Equal("Bearer", handler.LastRequest.Headers.Authorization!.Scheme);
        Assert.Equal("test-key", handler.LastRequest.Headers.Authorization!.Parameter);

        using var document = JsonDocument.Parse(requestBody!);
        Assert.Equal("test-model", document.RootElement.GetProperty("model").GetString());
        Assert.Equal(2, document.RootElement.GetProperty("messages").GetArrayLength());
        Assert.Equal(
            "list_directory",
            document.RootElement.GetProperty("tools")[0].GetProperty("function").GetProperty("name").GetString());
    }

    [Fact]
    public async Task Parses_tool_calls_from_the_response()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                """
                {"choices":[{"message":{"content":null,"tool_calls":[{"id":"call_1","type":"function","function":{"name":"read_text_file","arguments":"{\"path\":\"a.txt\"}"}}]}}]}
                """,
                Encoding.UTF8,
                "application/json"),
        });

        var response = await CreateClient(handler).CompleteAsync(new LlmChatRequest
        {
            Messages = new[] { LlmMessage.User("hi") },
        });

        var call = Assert.Single(response.ToolCalls);
        Assert.Equal("call_1", call.Id);
        Assert.Equal("read_text_file", call.Name);
        Assert.Contains("a.txt", call.ArgumentsJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Serializes_tool_messages_and_tool_calls()
    {
        string? requestBody = null;
        var handler = new StubHttpMessageHandler(request =>
        {
            requestBody = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"choices":[{"message":{"content":"done"}}]}""", Encoding.UTF8, "application/json"),
            };
        });

        var client = CreateClient(handler);
        await client.CompleteAsync(new LlmChatRequest
        {
            Messages = new[]
            {
                LlmMessage.User("hi"),
                LlmMessage.Assistant(null, new[] { new LlmToolCall("call_1", "list_directory", "{}") }),
                LlmMessage.Tool("call_1", "{\"entries\":[]}"),
            },
        });

        using var document = JsonDocument.Parse(requestBody!);
        var messages = document.RootElement.GetProperty("messages");
        Assert.Equal("assistant", messages[1].GetProperty("role").GetString());
        Assert.Equal("list_directory", messages[1].GetProperty("tool_calls")[0].GetProperty("function").GetProperty("name").GetString());
        Assert.Equal("tool", messages[2].GetProperty("role").GetString());
        Assert.Equal("call_1", messages[2].GetProperty("tool_call_id").GetString());
    }

    [Fact]
    public async Task Throws_a_llm_exception_for_error_status_codes()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized)
        {
            Content = new StringContent("""{"error":"bad key"}"""),
        });

        var exception = await Assert.ThrowsAsync<LlmException>(() =>
            CreateClient(handler).CompleteAsync(new LlmChatRequest { Messages = new[] { LlmMessage.User("hi") } }));

        Assert.Contains("401", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Rejects_unconfigured_options()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));

        Assert.Throws<ArgumentException>(() => CreateClient(handler, new LlmOptions { BaseUrl = "", Model = "" }));
    }
}
