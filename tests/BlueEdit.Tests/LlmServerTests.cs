using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Xunit;

/// <summary>A model server at an address (#95): addresses, keys, what a server refuses, the fallback.</summary>
public class LlmServerTests
{
    static readonly JsonNode Schema = JsonNode.Parse("""{"type":"object","properties":{"ok":{"type":"boolean"}}}""")!;

    [Theory]
    [InlineData("http://127.0.0.1:11434/v1", "http://127.0.0.1:11434/v1/chat/completions")]
    [InlineData("http://127.0.0.1:11434/v1/", "http://127.0.0.1:11434/v1/chat/completions")]
    [InlineData("http://127.0.0.1:8080", "http://127.0.0.1:8080/v1/chat/completions")]
    [InlineData("https://api.openai.com/v1/chat/completions", "https://api.openai.com/v1/chat/completions")]
    public void The_chat_address_comes_from_the_base_address(string url, string chat)
    {
        var e = new LlmEndpoint(url, "m", null);
        Assert.Equal(chat, e.ChatUrl);
        Assert.Equal(chat.Replace("/chat/completions", "/models", StringComparison.Ordinal), e.ModelsUrl);
    }

    [Theory]
    [InlineData("http://127.0.0.1:11434/v1", true)]
    [InlineData("http://localhost:1234/v1", true)]
    [InlineData("http://192.168.1.20:11434/v1", true)]
    [InlineData("http://10.0.0.5/v1", true)]
    [InlineData("http://172.20.1.1/v1", true)]
    [InlineData("http://studio.local:1234/v1", true)]
    [InlineData("http://gpu-box:8080", true)]                     // a bare name: a computer on the network
    [InlineData("https://api.openai.com/v1", false)]
    [InlineData("http://8.8.8.8/v1", false)]
    [InlineData("http://172.32.0.1/v1", false)]
    public void Only_addresses_outside_the_local_network_count_as_leaving_the_computer(string url, bool isPrivate) =>
        Assert.Equal(isPrivate, new LlmEndpoint(url, "m", null).Private);

    [Fact]
    public void The_key_is_trimmed_of_spaces_and_a_byte_order_mark_and_may_come_from_the_environment()
    {
        Assert.Equal("sk-abc", LlmEndpoint.KeyFrom("﻿sk-abc \r\n"));
        Assert.Null(LlmEndpoint.KeyFrom("  "));
        Environment.SetEnvironmentVariable("BLUEEDIT_TEST_KEY", " sk-env\n");
        try { Assert.Equal("sk-env", LlmEndpoint.KeyFrom("env:BLUEEDIT_TEST_KEY")); }
        finally { Environment.SetEnvironmentVariable("BLUEEDIT_TEST_KEY", null); }
        Assert.Null(LlmEndpoint.KeyFrom("env:BLUEEDIT_NO_SUCH_VARIABLE"));
    }

    [Fact]
    public void A_refused_field_is_learnt_from_the_400_answer()
    {
        var s = LlmServer.At(new LlmEndpoint("http://127.0.0.1:1/v1", "m", null));
        Assert.True(s.Adapt("""{"error":{"message":"Unknown parameter: 'chat_template_kwargs'.","param":"chat_template_kwargs"}}"""));
        Assert.False(s.Adapt("""{"error":{"param":"chat_template_kwargs"}}"""));      // already left out
        Assert.True(s.Adapt("Unsupported parameter: 'temperature' is not supported with this model."));
        Assert.True(s.Adapt("""{"error":{"param":"max_tokens"}}"""));                     // → max_completion_tokens
        Assert.False(s.Adapt("""{"error":{"param":"messages"}}"""));                     // not one we can do without
        Assert.False(s.Adapt("Bad Request"));
    }

    // A server on this computer that refuses chat_template_kwargs as OpenAI does, then answers; it records the requests.
    sealed class FakeServer : IDisposable
    {
        readonly HttpListener _listener = new();
        public List<(JsonObject Body, string? Auth)> Requests { get; } = new();
        public string Url { get; }
        /// <summary>The chat answer for a request body (null: the usual one), set by a test.</summary>
        public Func<JsonObject, string?>? Answer { get; init; }

        public FakeServer()
        {
            // a port the system says is free (a random one was once taken on the runner: the v0.2.0 release run), and
            // another try if something takes it between the asking and the listening
            for (int attempt = 0; ; attempt++)
            {
                int port;
                using (var probe = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0)) { probe.Start(); port = ((IPEndPoint)probe.LocalEndpoint).Port; probe.Stop(); }
                _listener.Prefixes.Clear();
                _listener.Prefixes.Add($"http://127.0.0.1:{port}/");
                try { _listener.Start(); Url = $"http://127.0.0.1:{port}/v1"; break; }
                catch (HttpListenerException) when (attempt < 5) { }
            }
            _ = Task.Run(Serve);
        }

        async Task Serve()
        {
            while (_listener.IsListening)
            {
                HttpListenerContext ctx;
                try { ctx = await _listener.GetContextAsync(); } catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException) { return; }
                var body = JsonNode.Parse(await new StreamReader(ctx.Request.InputStream).ReadToEndAsync())!.AsObject();
                lock (Requests) Requests.Add((body, ctx.Request.Headers["Authorization"]));
                string answer;
                if (body.ContainsKey("chat_template_kwargs"))
                {
                    ctx.Response.StatusCode = 400;
                    answer = """{"error":{"message":"Unknown parameter: 'chat_template_kwargs'.","type":"invalid_request_error","param":"chat_template_kwargs"}}""";
                }
                else answer = Answer?.Invoke(body) ?? """{"model":"fake-model-1","choices":[{"finish_reason":"stop","message":{"role":"assistant","content":"{\"ok\":true}"}}]}""";
                var bytes = Encoding.UTF8.GetBytes(answer);
                ctx.Response.ContentType = "application/json";
                await ctx.Response.OutputStream.WriteAsync(bytes);
                ctx.Response.Close();
            }
        }

        public void Dispose() { _listener.Stop(); _listener.Close(); }
    }

    [Fact]
    public void A_server_at_an_address_is_asked_again_without_what_it_refused_and_gets_the_model_and_the_key()
    {
        using var fake = new FakeServer();
        using var s = LlmServer.At(new LlmEndpoint(fake.Url, "fake-model", "sk-test"));
        Assert.Equal("""{"ok":true}""", s.Chat("instructions", "text", "x", Schema, 100));
        Assert.Equal(2, fake.Requests.Count);                                        // refused once, asked again
        var (body, auth) = fake.Requests[1];
        Assert.False(body.ContainsKey("chat_template_kwargs"));
        Assert.Equal("fake-model", (string?)body["model"]);
        Assert.Equal("none", (string?)body["reasoning_effort"]);
        Assert.Equal("Bearer sk-test", auth);
        Assert.Equal("fake-model-1", s.ModelName);                                   // the model that answered
        s.Chat("instructions", "again", "x", Schema, 100);
        Assert.Equal(3, fake.Requests.Count);                                        // remembered: one request now
    }

    [Fact]
    public void When_llama_cpp_does_not_start_the_server_at_an_address_answers()
    {
        using var fake = new FakeServer();
        var dir = Path.Combine(Path.GetTempPath(), "be-nollama-" + Guid.NewGuid().ToString("N"));      // no llama-server there
        using var s = new LlmServer(dir, Path.Combine(dir, "models", "x.gguf")) { Fallback = new LlmEndpoint(fake.Url, "fake-model", null) };
        Assert.Equal("""{"ok":true}""", s.Chat("instructions", "text", "x", Schema, 100));
        Assert.NotNull(s.Remote);
        Assert.Contains("llama.cpp did not start", s.FallbackReason, StringComparison.Ordinal);
    }

    [Fact]
    public void An_answer_cut_off_on_a_formula_is_asked_again_with_the_formulas_as_one_word_and_the_findings_land_in_the_paragraph()
    {
        const string text = "Медиана $\\bar{x}=\\frac{1}{n}\\sum x_i$ может сильно сместится, а $Q_3-Q_1$ нет.";
        static string Reply(string finish, string content) =>
            new JsonObject { ["choices"] = new JsonArray(new JsonObject { ["finish_reason"] = finish, ["message"] = new JsonObject { ["content"] = content } }) }.ToJsonString();
        using var fake = new FakeServer
        {
            Answer = body => (string?)body["messages"]![1]!["content"] is not { } t ? null
                : t.Contains('$', StringComparison.Ordinal) ? Reply("length", "{\"corrected\": \"Медиана $\\\\text{ }\\\\text{ }")   // YandexGPT 5 Lite looping
                : Reply("stop", new JsonObject { ["corrected"] = t.Replace("сместится", "сместиться", StringComparison.Ordinal), ["errors"] = new JsonArray() }.ToJsonString()),
        };
        using var server = LlmServer.At(new LlmEndpoint(fake.Url, "fake-model", null));
        using var g = new LlmGrammarChecker(server, "");
        IReadOnlyList<GrammarIssue>? found = null;
        for (var sw = System.Diagnostics.Stopwatch.StartNew(); found == null && sw.Elapsed < TimeSpan.FromSeconds(20); Thread.Sleep(20))
            found = g.Issues(text, "ru");
        var x = Assert.Single(found!);
        Assert.Equal("сместится", text.Substring(x.Offset, x.Length));
        Assert.Equal("сместиться", x.Replacements[0]);
        Assert.Equal("Медиана X может сильно сместится, а X нет.", (string?)fake.Requests[^1].Body["messages"]![1]!["content"]);
    }
}
