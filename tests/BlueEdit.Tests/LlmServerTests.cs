using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.Json;
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
    public void A_typed_key_goes_to_its_own_file_and_the_setting_only_names_it()
    {
        var dir = Path.Combine(Path.GetTempPath(), "blueedit-key-" + Guid.NewGuid().ToString("N"));
        try
        {
            Assert.Equal("file:llm-key", LlmEndpoint.SaveKey(" sk-typed \n", dir));
            Assert.Equal("sk-typed", LlmEndpoint.KeyFrom("file:llm-key", dir));
            var file = Path.Combine(dir, "llm-key");
            if (OperatingSystem.IsWindows())
                Assert.DoesNotContain("sk-typed", Encoding.Latin1.GetString(File.ReadAllBytes(file)), StringComparison.Ordinal);   // encrypted (DPAPI)
            else
                Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(file));
            // a key written into a file by hand is read as it is, on Windows too; of several lines, the first
            File.WriteAllText(Path.Combine(dir, "by-hand.txt"), "﻿sk-hand\r\n");
            Assert.Equal("sk-hand", LlmEndpoint.KeyFrom("file:by-hand.txt", dir));
            File.WriteAllText(Path.Combine(dir, "two.txt"), "\r\nsk-first\r\nsk-second\r\n");
            Assert.Equal("sk-first", LlmEndpoint.KeyFrom("file:two.txt", dir));
            // kept as it is when the dialog is left unchanged
            Assert.Equal("file:llm-key", LlmEndpoint.SaveKey("file:llm-key", dir));
            Assert.True(File.Exists(Path.Combine(dir, "llm-key")));
            // a variable, or no key: the file goes
            Assert.Equal("env:OPENAI_API_KEY", LlmEndpoint.SaveKey("env:OPENAI_API_KEY", dir));
            Assert.False(File.Exists(Path.Combine(dir, "llm-key")));
            LlmEndpoint.SaveKey("sk-again", dir);
            Assert.Equal("", LlmEndpoint.SaveKey("", dir));
            Assert.False(File.Exists(Path.Combine(dir, "llm-key")));
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
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
        /// <summary>The status of the answers after the first refusal (200 unless a test sets it).</summary>
        public int Status { get; init; } = 200;

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
                if (ctx.Request.HttpMethod == "GET")                  // its list of models: one, as llama-server has
                {
                    var list = Encoding.UTF8.GetBytes("""{"data":[{"id":"fake-model-1"}]}""");
                    ctx.Response.ContentType = "application/json";
                    await ctx.Response.OutputStream.WriteAsync(list);
                    ctx.Response.Close();
                    continue;
                }
                var body = JsonNode.Parse(await new StreamReader(ctx.Request.InputStream).ReadToEndAsync())!.AsObject();
                lock (Requests) Requests.Add((body, ctx.Request.Headers["Authorization"]));
                string answer;
                if (body.ContainsKey("chat_template_kwargs"))
                {
                    ctx.Response.StatusCode = 400;
                    answer = """{"error":{"message":"Unknown parameter: 'chat_template_kwargs'.","type":"invalid_request_error","param":"chat_template_kwargs"}}""";
                }
                else
                {
                    ctx.Response.StatusCode = Status;
                    answer = Answer?.Invoke(body) ?? """{"model":"fake-model-1","choices":[{"finish_reason":"stop","message":{"role":"assistant","content":"{\"ok\":true}"}}]}""";
                }
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

    // the cloud services asked in their own ways (LlmServer.Cloud.cs), known by the address
    [Theory]
    [InlineData("https://api.anthropic.com/v1", "anthropic", "https://api.anthropic.com/v1/models?limit=1000")]
    [InlineData("https://api.anthropic.com", "anthropic", "https://api.anthropic.com/v1/models?limit=1000")]
    [InlineData("https://generativelanguage.googleapis.com/v1beta", "google", "https://generativelanguage.googleapis.com/v1beta/models?pageSize=1000")]
    [InlineData("https://generativelanguage.googleapis.com/v1beta/openai", "openai-form", "https://generativelanguage.googleapis.com/v1beta/openai/models")]
    [InlineData("https://api.deepseek.com/v1", "deepseek", "https://api.deepseek.com/v1/models")]
    [InlineData("https://api.openai.com/v1", "openai", "https://api.openai.com/v1/models")]
    public void A_cloud_service_is_known_by_its_address(string url, string kind, string models)
    {
        var e = new LlmEndpoint(url, "m", null);
        Assert.Equal(kind, e.IsAnthropic ? "anthropic" : e.IsGoogleNative ? "google" : e.IsDeepSeek ? "deepseek" : e.IsOpenAi ? "openai" : "openai-form");
        Assert.Equal(models, e.ModelsUrl);
    }

    [Fact]
    public void The_key_goes_in_the_header_each_service_wants()
    {
        string Headers(string url)
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, url);
            new LlmEndpoint(url, "m", "sk-x").AddKey(req);
            return string.Join(";", req.Headers.Select(h => $"{h.Key}={string.Join(",", h.Value)}").Order(StringComparer.Ordinal));
        }
        Assert.Equal("anthropic-version=2023-06-01;x-api-key=sk-x", Headers("https://api.anthropic.com/v1"));
        Assert.Equal("x-goog-api-key=sk-x", Headers("https://generativelanguage.googleapis.com/v1beta"));
        Assert.Equal("Authorization=Bearer sk-x", Headers("https://api.deepseek.com/v1"));
        // a key a header cannot carry is that request's failure, not the editor's end
        using var bad = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1");
        Assert.Throws<HttpRequestException>(() => new LlmEndpoint("https://api.openai.com/v1", "m", "sk-a\nsk-b").AddKey(bad));
    }

    // the answers in each service's form, as they came on 23 October 2026 (shortened)
    [Fact]
    public void Each_service_s_answer_is_read()
    {
        (string, bool, string?) Read(string url, string answer)
        {
            using var doc = JsonDocument.Parse(answer);
            return LlmServer.Completion(new LlmEndpoint(url, "m", null), doc.RootElement);
        }
        Assert.Equal(("{\"ok\":true}", false, "claude-haiku-4-5"), Read("https://api.anthropic.com/v1",
            """{"model":"claude-haiku-4-5","stop_reason":"tool_use","content":[{"type":"tool_use","input":{"ok":true}}]}"""));
        Assert.Equal(("{\"ok\":true}", true, "claude-sonnet-5-5"), Read("https://api.anthropic.com/v1",
            """{"model":"claude-sonnet-5-5","stop_reason":"max_tokens","content":[{"type":"text","text":"```json\n{\"ok\":true}\n```"}]}"""));
        Assert.Equal(("{\"ok\":true}", false, "gemini-3.5-flash-lite"), Read("https://generativelanguage.googleapis.com/v1beta",
            """{"model":"models/gemini-3.5-flash-lite","status":"completed","steps":[{"type":"thought"},{"type":"model_output","content":[{"type":"text","text":"{\"ok\":true}"}]}]}"""));
        Assert.Equal(("{\"ok\":true}", false, "deepseek-flash"), Read("https://api.deepseek.com/v1",
            """{"model":"deepseek-flash","choices":[{"finish_reason":"tool_calls","message":{"content":"","tool_calls":[{"function":{"arguments":"{\"ok\":true}"}}]}}]}"""));
        Assert.Throws<InvalidOperationException>(() => Read("https://api.anthropic.com/v1", """{"content":[]}"""));
    }

    // what Anthropic, Google and DeepSeek say when they refuse something, as the plugin found it
    [Fact]
    public void The_cloud_services_refusals_are_understood()
    {
        var claude = LlmServer.At(new LlmEndpoint("https://api.anthropic.com/v1", "claude-x", null));
        Assert.True(claude.Adapt("""{"type":"error","error":{"type":"invalid_request_error","message":"`temperature` is deprecated for this model."}}"""));
        Assert.True(claude.Adapt("""{"type":"error","error":{"message":"tool_choice: type \"tool\" and \"any\" are not supported for this model"}}"""));
        var gemini = LlmServer.At(new LlmEndpoint("https://generativelanguage.googleapis.com/v1beta/openai", "gemini-x", null));
        Assert.True(gemini.Adapt("""[{"error":{"code":400,"message":"Invalid JSON payload received. Unknown name \"chat_template_kwargs\": Cannot find field."}}]"""));
        Assert.True(gemini.Adapt("""[{"error":{"message":"Budget 0 is invalid. This model only works in thinking mode."}}]"""));
        Assert.True(gemini.Adapt("""[{"error":{"message":"Request contains an invalid argument."}}]"""));     // reasoning_effort
        var deepseek = LlmServer.At(new LlmEndpoint("http://127.0.0.1:1/v1", "m", null));
        Assert.True(deepseek.Adapt("""{"error":{"message":"This response_format type is unavailable now","type":"invalid_request_error"}}"""));
    }

    // how long a service said to wait at its limit: Anthropic's and OpenAI's Retry-After, OpenAI's and Google's words
    [Fact]
    public void The_wait_a_service_gives_at_its_limit_is_read()
    {
        Assert.Equal(TimeSpan.FromSeconds(30), LlmServer.RetryAfter(new(TimeSpan.FromSeconds(30)), "", ""));
        Assert.Equal(TimeSpan.FromMilliseconds(1200), LlmServer.RetryAfter(null, "Rate limit reached. Please try again in 1.2s.", ""));
        Assert.Equal(TimeSpan.FromMinutes(54) + TimeSpan.FromSeconds(9.3), LlmServer.RetryAfter(null, "Quota exceeded. Please retry in 54m9.3s.", ""));
        Assert.Equal(TimeSpan.FromSeconds(53), LlmServer.RetryAfter(null, "", """{"error":{"details":[{"retryDelay": "53s"}]}}"""));
        Assert.Null(LlmServer.RetryAfter(null, "Too many requests", ""));
    }

    // a limit reached: said with the time, and nothing more is sent until then
    [Fact]
    public void After_a_limit_nothing_is_sent_until_the_time_given()
    {
        using var fake = new FakeServer { Status = 429, Answer = _ => """{"error":{"message":"Rate limit reached. Please try again in 20s."}}""" };
        var s = LlmServer.At(new LlmEndpoint(fake.Url, "m", null));
        var schema = JsonNode.Parse("""{"type":"object"}""")!;
        var ex = Assert.Throws<LimitReachedException>(() => s.Chat("i", "t", "x", schema, 100));
        Assert.InRange(ex.Until - DateTime.Now, TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(21));
        int asked = fake.Requests.Count;
        Assert.Throws<LimitReachedException>(() => s.Chat("i", "t", "x", schema, 100));
        Assert.Equal(asked, fake.Requests.Count);
    }

    // no model named (the installer keeps none for Ollama, LM Studio, llama-server): the one the server lists is asked for
    [Fact]
    public void Without_a_model_named_the_server_s_own_is_taken()
    {
        using var fake = new FakeServer();
        var s = LlmServer.At(new LlmEndpoint(fake.Url, "", null));
        s.Chat("i", "t", "x", JsonNode.Parse("""{"type":"object"}""")!, 100);
        Assert.Equal("fake-model-1", (string?)fake.Requests[^1].Body["model"]);
    }

    // a server checked before it is kept: its list of models in either form; no answer said as such
    [Fact]
    public void A_server_s_models_are_read_and_one_that_does_not_answer_is_said()
    {
        Assert.Equal(["claude-haiku-5-5", "gpt-5.4-mini"], LlmServer.ModelsIn("""{"data":[{"id":"gpt-5.4-mini"},{"id":"whisper-1"},{"id":"claude-haiku-5-5"}]}"""));
        Assert.Equal(["gemini-3.5-flash-lite"], LlmServer.ModelsIn("""{"models":[{"name":"models/gemini-3.5-flash-lite"},{"name":"models/veo-3.0-generate"}]}"""));
        Assert.Equal(LlmServer.ProbeResult.Unreachable, LlmServer.Probe(new LlmEndpoint("http://127.0.0.1:1/v1", "m", null)).Result);
    }

    [Theory]
    [InlineData("gpt-5.4-mini", true)]
    [InlineData("claude-haiku-5-5", true)]
    [InlineData("gemini-3.5-flash-lite", true)]
    [InlineData("qwen3:8b", true)]
    [InlineData("text-embedding-3-small", false)]
    [InlineData("whisper-1", false)]
    [InlineData("gpt-realtime", false)]
    [InlineData("gpt-5.5-pro", false)]
    [InlineData("veo-3.0-generate", false)]
    [InlineData("nomic-embed-text:latest", false)]
    public void Only_models_that_write_text_are_listed(string id, bool listed) => Assert.Equal(listed, LlmServer.IsTextModel(id));

    [Fact]
    public void Strict_and_closed_schemas_close_every_object_and_strict_requires_every_field()
    {
        var schema = JsonNode.Parse("""{"type":"object","properties":{"issues":{"type":"array","items":{"type":"object","properties":{"a":{"type":"string"},"b":{"type":["string","null"]}},"required":["a"]}}}}""")!;
        var closed = LlmServer.Closed(schema).ToJsonString();
        Assert.Contains("\"additionalProperties\":false", closed, StringComparison.Ordinal);
        Assert.Equal(2, closed.Split("additionalProperties").Length - 1);
        var strict = LlmServer.Strict(schema);
        Assert.Equal("[\"a\",\"b\"]", strict["properties"]!["issues"]!["items"]!["required"]!.ToJsonString());
        Assert.Equal("[\"issues\"]", strict["required"]!.ToJsonString());
        Assert.Null(schema["additionalProperties"]);                                      // the original untouched
    }
}
