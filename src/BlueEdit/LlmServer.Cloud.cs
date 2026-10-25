using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

/// <summary>
/// The cloud services, as the Obsidian plugin BlueProof asks them and measured them (its src/checkers/llm.ts and
/// docs/servers.md, 10–16 October 2026): Anthropic (Claude) and Google (Gemini) in their own APIs, DeepSeek in OpenAI's
/// form with its strict function calls; what each refuses known before the first request. docs/tech/proofing-internals.md
/// </summary>
sealed partial class LlmServer
{
    // ---------- what a server takes ----------

    // Room for the thinking of a model that cannot turn it off: 200 to 330 tokens measured on short texts (Gemini 3).
    const int ThinkingRoom = 2048;

    bool _thinks;          // thinks whatever it is asked, and its thinking counts against the length limit
    bool _schemaOutput;    // Anthropic: the answer by its schema format, not a forced tool (Haiku 5.5, Sonnet 5.5 and later)
    bool _strictSchema;    // OpenAI: the schema in strict mode, which its server holds the answer to
    bool _jsonObject;      // any JSON in place of a schema (DeepSeek refuses schemas)
    bool? _strictTool;     // DeepSeek: the answer as a strict function call, at its /beta address; false once that failed
    LlmEndpoint? _known;

    /// <summary>What the plugin found each service and model refusing (KNOWN_PROFILES there, with the measurements), so
    /// that even the first request goes through at once; anything else is learnt from the answers (<see cref="Adapt"/>).</summary>
    void Know(LlmEndpoint e)
    {
        if (ReferenceEquals(_known, e)) return;
        _known = e;
        string model = e.Model.Trim();
        void Omit(params string[] fields) { foreach (var f in fields) _omit.Add(f); }
        if (e.IsOpenAi)
        {
            // every model: "Unknown parameter: 'chat_template_kwargs'"; "Use 'max_completion_tokens' instead"
            Omit("chat_template_kwargs");
            _completionTokens = _strictSchema = true;
            // reasoning models that cannot turn thinking off refuse "none" and any temperature but 1
            if (model is "gpt-6-astra" or "gpt-6.1-sol") Omit("reasoning_effort", "temperature");
        }
        // "`temperature` is deprecated for this model"; they refuse the forced tool, and Anthropic's schema format is its
        // advice for them: Sonnet 5.5 198 of 207 errors found, Haiku 5.5 197 (2026-10-11)
        if (e.IsAnthropic && model is "claude-haiku-5-5" or "claude-sonnet-5-5") { Omit("temperature"); _schemaOutput = true; }
        if (e.IsGoogle)
        {
            Omit("chat_template_kwargs");
            if (model is "gemma-4-31b-it" or "gemma-4-26b-a4b-it") Omit("reasoning_effort", "temperature");   // loops at 0
            if (model == "gemini-3.5-flash-lite") Omit("reasoning_effort");
            // Gemini 3 wants its default temperature ("may lead to … looping") and cannot stop thinking
            if (model.StartsWith("gemini-3", StringComparison.Ordinal)) { Omit("temperature"); _thinks = true; }
        }
        // no schema in response_format, only any JSON; its strict function calls hold the answer to the schema (2026-10-16)
        if (e.IsDeepSeek) { Omit("chat_template_kwargs"); _jsonObject = true; _strictTool ??= true; }
    }

    // The fields a server may refuse that we can do without (not the model, the messages or the token limit).
    static readonly HashSet<string> Optional = new(StringComparer.Ordinal) { "temperature", "response_format", "chat_template_kwargs", "reasoning_effort", "tool_choice" };

    /// <summary>Learns from a 400 answer which field the server refused (OpenAI: "Unknown parameter:
    /// 'chat_template_kwargs'", with error.param; Anthropic, Google and DeepSeek in their own words, as the plugin found
    /// them); true when the request is worth sending again.</summary>
    internal bool Adapt(string answer)
    {
        string? param = null;
        try
        {
            using var doc = JsonDocument.Parse(answer);
            if (doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("error", out var e) && e.ValueKind == JsonValueKind.Object
                && e.TryGetProperty("param", out var p) && p.ValueKind == JsonValueKind.String) param = p.GetString();
        }
        catch (JsonException) { }
        var message = ErrorMessage(answer);
        // in the message, not the raw answer: there Google's 'Unknown name "chat_template_kwargs"' has its quotes escaped
        param ??= Regex.Match(message, @"(?:unknown|unsupported|unrecognized)[^'""]*['""]([a-z_]+)['""]", RegexOptions.IgnoreCase) is { Success: true } m ? m.Groups[1].Value : null;
        // Anthropic: "`temperature` is deprecated for this model"
        param ??= Regex.Match(answer, @"`([a-z_]+)` is (?:deprecated|not supported)", RegexOptions.IgnoreCase) is { Success: true } b ? b.Groups[1].Value : null;
        // Anthropic, a model that refuses the forced tool: "tool_choice: type "tool" and "any" are not supported"
        if (!_schemaOutput && message.StartsWith("tool_choice: type", StringComparison.Ordinal)) { _schemaOutput = true; return true; }
        // DeepSeek refuses a schema and names no field: "This response_format type is unavailable now"
        if (!_jsonObject && message.Contains("response_format type is unavailable", StringComparison.OrdinalIgnoreCase)) { _jsonObject = true; return true; }
        // Google: "Budget 0 is invalid. This model only works in thinking mode."
        if (param == null && !_thinks && message.Contains("only works in thinking mode", StringComparison.OrdinalIgnoreCase)) { _thinks = true; return true; }
        // Google names no field for reasoning_effort "none": "Thinking budget is not supported for this model.",
        // "Request contains an invalid argument."
        if (param == null && Regex.IsMatch(message, "^(?:thinking budget is not supported|request contains an invalid argument)", RegexOptions.IgnoreCase))
            param = "reasoning_effort";
        if (param == null) return false;
        if (param == "max_tokens" && !_completionTokens) { _completionTokens = true; return true; }
        return Optional.Contains(param) && _omit.Add(param);
    }

    /// <summary>The message of an error answer (Google wraps its error in an array), or the answer as it is.</summary>
    internal static string ErrorMessage(string answer)
    {
        try
        {
            using var doc = JsonDocument.Parse(answer);
            var root = doc.RootElement.ValueKind == JsonValueKind.Array && doc.RootElement.GetArrayLength() > 0 ? doc.RootElement[0] : doc.RootElement;
            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("error", out var e) && e.ValueKind == JsonValueKind.Object
                && e.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String) return m.GetString()!;
        }
        catch (JsonException) { }
        return answer;
    }

    // DeepSeek's strict function calls are a beta at an address of their own: should it go, any JSON is asked for instead
    bool StrictToolFailed(LlmEndpoint e, HttpStatusCode status, string answer)
    {
        if (_strictTool != true || !e.IsDeepSeek) return false;
        if (status != HttpStatusCode.NotFound && !(status is HttpStatusCode.BadRequest or HttpStatusCode.UnprocessableEntity
            && Regex.IsMatch(ErrorMessage(answer), "tool|strict|function|beta", RegexOptions.IgnoreCase))) return false;
        _strictTool = false;
        return true;
    }

    // ---------- refusals said plainly; the limit of requests waited out ----------

    string _limitMessage = "";

    /// <summary>Until when the service asked not to be asked again (its limit of requests), or null.</summary>
    public DateTime? PausedUntil { get; private set; }

    // A refusal the user can act on, said as such (the plugin's words): a key refused, no model named, a limit reached.
    // The limit: until the service says (Retry-After, or "retry in 54m9s" in Google's message), else a minute, nothing is
    // sent; before, every paragraph asked again and failed alike.
    void Refused(LlmEndpoint e, HttpResponseMessage resp, string answer)
    {
        var said = ErrorMessage(answer);
        if (resp.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            throw new HttpRequestException($"{e.Host} refused the key ({(int)resp.StatusCode}): check it in Tools > Set up grammar checking");
        if (e.Model.Trim().Length == 0 && Regex.IsMatch(said, @"\bmodel\b", RegexOptions.IgnoreCase))
            throw new HttpRequestException($"{e.Host} needs a model's name: Tools > Grammar model");
        if (resp.StatusCode != HttpStatusCode.TooManyRequests) return;
        var wait = RetryAfter(resp.Headers.RetryAfter, said, answer) ?? TimeSpan.FromMinutes(1);
        PausedUntil = DateTime.Now + wait;
        _limitMessage = said.Length > 200 ? said[..200] : said;
        throw new LimitReachedException(PausedUntil.Value, _limitMessage);
    }

    /// <summary>How long a service that refused for its limit said to wait: Retry-After (seconds or a date), OpenAI's
    /// "try again in 1.2s", Google's "Please retry in 54m9.3s" or its "retryDelay": "53s"; null when it did not say.</summary>
    internal static TimeSpan? RetryAfter(System.Net.Http.Headers.RetryConditionHeaderValue? header, string message, string answer)
    {
        if (header?.Delta is { } delta) return delta;
        if (header?.Date is { } date) return date - DateTimeOffset.Now is var d && d > TimeSpan.Zero ? d : TimeSpan.Zero;
        var said = Regex.Match(message, @"(?:retry|try again) in ((?:\d+(?:\.\d+)?(?:ms|h|m|s))+)", RegexOptions.IgnoreCase) is { Success: true } m ? m.Groups[1].Value
            : Regex.Match(answer, @"""retryDelay"":\s*""([\d.]+s)""") is { Success: true } r ? r.Groups[1].Value : null;
        if (said == null) return null;
        double ms = 0;
        foreach (Match p in Regex.Matches(said, @"(\d+(?:\.\d+)?)(ms|h|m|s)"))
            ms += double.Parse(p.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture)
                * p.Groups[2].Value switch { "ms" => 1, "s" => 1000, "m" => 60_000, _ => 3_600_000 };
        return TimeSpan.FromMilliseconds(ms);
    }

    // Models a server lists that do not write text for a prompt (embeddings, speech, pictures, rerankers) or that the
    // chat endpoint does not serve (OpenAI's audio, realtime, codex and -pro ones; Google's music, video, robots…): by
    // name only, the list says nothing else. The plugin's list, each tried there on 10–11 October 2026.
    static readonly Regex NotForText = new(
        @"embed|minilm|bge-|\be5-|rerank|ocr|whisper|tts|transcrib|speech|dall-e|image|moderation|translat|audio|realtime|(?:^|-)live(?:-|$)|codex|^(?:gpt|o\d)[\w.-]*-pro(?:-\d{4}-\d{2}-\d{2})?$|search|deep-research|lyria|veo-|nano-banana|robotics|computer-use|antigravity|^aqa$|^gemini-omni|customtools",
        RegexOptions.IgnoreCase);

    /// <summary>Whether a model from a server's list can proofread (by its name).</summary>
    internal static bool IsTextModel(string id) => !NotForText.IsMatch(id);

    // ---------- a server checked before it is used ----------

    /// <summary>What a check of a server found: Ok, or what is wrong — Key (refused), Unreachable (no answer: wrong
    /// address, not running, no network), Model (not among its models; Detail lists some), Other (Detail says).</summary>
    public enum ProbeResult { Ok, Key, Unreachable, Model, Other }

    /// <summary>Asks the server for its models, which costs nothing and shows whether it answers, takes the key and has
    /// the model: for the installer's page and the editor's dialog, before the first paragraph is sent.</summary>
    public static (ProbeResult Result, string Detail) Probe(LlmEndpoint e)
    {
        using var http = Net.Client(TimeSpan.FromSeconds(15));
        using var req = new HttpRequestMessage(HttpMethod.Get, e.ModelsUrl);
        try
        {
            e.AddKey(req);
            using var resp = http.Send(req);
            var answer = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            if (resp.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden) return (ProbeResult.Key, ErrorMessage(answer));
            if (!resp.IsSuccessStatusCode) return (ProbeResult.Other, $"{(int)resp.StatusCode}: {ErrorMessage(answer)}");
            var models = ModelsIn(answer);
            var model = e.Model.Trim();
            // a server with one model of its own (llama-server) answers with it whatever is asked: no list to hold to
            // a short name stands for its dated one: Anthropic lists claude-haiku-4-5-20251001 and takes claude-haiku-4-5
            if (model.Length > 0 && models.Count > 1 && !models.Any(m => m.Equals(model, StringComparison.OrdinalIgnoreCase)
                    || m.StartsWith(model + "-", StringComparison.OrdinalIgnoreCase)))
                return (ProbeResult.Model, string.Join(", ", models.Take(8)) + (models.Count > 8 ? ", …" : ""));
            return (ProbeResult.Ok, "");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException or UriFormatException)
        {
            return (ProbeResult.Unreachable, ex.Message);
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException)
        {
            return (ProbeResult.Other, "its list of models could not be read: " + ex.Message);
        }
    }

    /// <summary>The models in a /models answer, sorted, those that write no text left out: OpenAI's form (and
    /// Anthropic's) data[].id; Google's own models[].name, "models/gemini-…".</summary>
    internal static List<string> ModelsIn(string answer)
    {
        using var doc = JsonDocument.Parse(answer);
        var google = !doc.RootElement.TryGetProperty("data", out var data);
        var list = google ? doc.RootElement.GetProperty("models") : data;
        return list.EnumerateArray()
            .Select(m => m.TryGetProperty(google ? "name" : "id", out var id) ? id.GetString()?.Trim().Replace("models/", "", StringComparison.Ordinal) ?? "" : "")
            .Where(m => m.Length > 0 && IsTextModel(m)).Distinct().Order(StringComparer.OrdinalIgnoreCase).ToList();
    }

    // ---------- the requests ----------

    /// <summary>Where a request to this server goes and what it says.</summary>
    (string Url, JsonObject Body) Request(LlmEndpoint e, string instructions, string text, string schemaName, JsonNode schema, int maxTokens, double temperature) =>
        e.IsAnthropic ? (e.Api + "/messages", AnthropicBody(e, instructions, text, schemaName, schema, maxTokens, temperature))
        : e.IsGoogleNative ? (e.Api + "/interactions", GoogleBody(e, instructions, text, schema, maxTokens))
        : (_strictTool == true && e.IsDeepSeek ? "https://api.deepseek.com/beta/chat/completions" : e.ChatUrl, Body(e, instructions, text, schemaName, schema, maxTokens, temperature));

    // Anthropic's Messages: no response_format, so the answer comes as the input of a tool the model must call, whose
    // schema is ours, or for the models that refuse that, by Anthropic's own schema format
    JsonObject AnthropicBody(LlmEndpoint e, string instructions, string text, string schemaName, JsonNode schema, int maxTokens, double temperature)
    {
        var body = new JsonObject
        {
            ["model"] = e.Model.Trim(),
            // at least 1024: Sonnet explains in full, and short Russian sentences were cut off below that; asked by the
            // schema format the model thinks first, so it gets room for that (7 long paragraphs of 12 cut off without)
            ["max_tokens"] = Math.Max(1024, maxTokens) + (_thinks || _schemaOutput ? ThinkingRoom : 0),
            ["temperature"] = temperature,
            ["system"] = instructions,
            ["messages"] = new JsonArray(new JsonObject { ["role"] = "user", ["content"] = text }),
        };
        if (_schemaOutput)
            body["output_config"] = new JsonObject { ["format"] = new JsonObject { ["type"] = "json_schema", ["schema"] = Closed(schema) } };
        else
        {
            body["tools"] = new JsonArray(new JsonObject { ["name"] = schemaName, ["description"] = "Give the answer.", ["input_schema"] = schema.DeepClone() });
            body["tool_choice"] = new JsonObject { ["type"] = "tool", ["name"] = schemaName };
        }
        foreach (var f in _omit) body.Remove(f);
        return body;
    }

    // Google's Interactions, which it recommends for new projects: the answer held to the schema by response_format;
    // store false, as it keeps what is sent otherwise (55 days on the paid tier); no temperature, Gemini 3 wants its own.
    // Through Google's OpenAI form, gemini-3.5-flash-lite lost a long Russian paragraph's errors in 2 runs of 3; here in
    // none of 3 (the plugin, 2026-10-12).
    JsonObject GoogleBody(LlmEndpoint e, string instructions, string text, JsonNode schema, int maxTokens) => new()
    {
        ["model"] = e.Model.Trim(),
        ["input"] = text,
        ["system_instruction"] = instructions,
        ["store"] = false,
        ["response_format"] = new JsonObject { ["type"] = "text", ["mime_type"] = "application/json", ["schema"] = schema.DeepClone() },
        ["generation_config"] = new JsonObject { ["max_output_tokens"] = maxTokens + (_thinks ? ThinkingRoom : 0) },
    };

    /// <summary>The answer's text, whether it stopped at the length limit, and the model that answered, from an answer in
    /// the server's own form. Throws <see cref="InvalidOperationException"/> when it is not an answer.</summary>
    internal static (string Content, bool CutOff, string? Model) Completion(LlmEndpoint? e, JsonElement root)
    {
        string? Str(JsonElement o, string name) => o.ValueKind == JsonValueKind.Object && o.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
        IEnumerable<JsonElement> Items(JsonElement o, string name) =>
            o.ValueKind == JsonValueKind.Object && o.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Array ? v.EnumerateArray() : [];
        var model = Str(root, "model")?.Trim() is { Length: > 0 } who ? who : null;
        if (e is { IsAnthropic: true })
        {
            var blocks = Items(root, "content").ToList();
            var content = blocks.FirstOrDefault(b => Str(b, "type") == "tool_use" && b.TryGetProperty("input", out var i) && i.ValueKind == JsonValueKind.Object) is { ValueKind: JsonValueKind.Object } tool
                ? tool.GetProperty("input").GetRawText()
                : blocks.Select(b => Str(b, "type") == "text" ? Str(b, "text") : null).FirstOrDefault(t => t != null) is { } t ? Unfenced(t)
                : throw new InvalidOperationException("the answer has no message");
            return (content, Str(root, "stop_reason") == "max_tokens", model);
        }
        if (e is { IsGoogleNative: true })
        {
            var texts = Items(root, "steps").Where(s => Str(s, "type") == "model_output")
                .SelectMany(s => Items(s, "content")).Where(c => Str(c, "type") == "text").Select(c => Str(c, "text")!).ToList();
            if (texts.Count == 0) throw new InvalidOperationException("the answer has no message");
            return (string.Concat(texts), Str(root, "status") == "incomplete", model?.Replace("models/", "", StringComparison.Ordinal));
        }
        var choice = Items(root, "choices").FirstOrDefault();
        if (choice.ValueKind != JsonValueKind.Object || !choice.TryGetProperty("message", out var message)) throw new InvalidOperationException("the answer has no message");
        // asked by a function to call (DeepSeek's strict tool), the answer is its arguments and the text is empty
        var args = Items(message, "tool_calls").Select(c => c.TryGetProperty("function", out var f) ? Str(f, "arguments") : null).FirstOrDefault(a => a != null);
        return (args ?? Str(message, "content") ?? "", Str(choice, "finish_reason") == "length", model);
    }

    // ---------- schemas ----------

    /// <summary>The schema with additionalProperties false on every object, which Anthropic's schema format wants.</summary>
    internal static JsonNode Closed(JsonNode schema)
    {
        var copy = schema.DeepClone();
        void Close(JsonNode? n)
        {
            if (n is JsonArray a) { foreach (var x in a) Close(x); return; }
            if (n is not JsonObject o) return;
            if (IsObject(o)) o["additionalProperties"] = false;
            foreach (var (k, v) in o.ToList())
                if (k == "properties" && v is JsonObject props) foreach (var (_, s) in props) Close(s);
                else if (k != "additionalProperties") Close(v);
        }
        Close(copy);
        return copy;
    }

    static bool IsObject(JsonObject o) => o["type"] is JsonValue v && v.TryGetValue<string>(out var t) && t == "object";

    /// <summary>OpenAI's strict mode: every object closed and every field required (an optional list then comes empty).</summary>
    internal static JsonNode Strict(JsonNode schema)
    {
        var copy = Closed(schema);
        void Require(JsonNode? n)
        {
            if (n is JsonArray a) { foreach (var x in a) Require(x); return; }
            if (n is not JsonObject o) return;
            if (IsObject(o) && o["properties"] is JsonObject props) o["required"] = new JsonArray(props.Select(p => (JsonNode?)p.Key).ToArray());
            foreach (var (k, v) in o.ToList())
                if (k == "properties" && v is JsonObject ps) foreach (var (_, s) in ps) Require(s);
                else if (k is not ("required" or "additionalProperties")) Require(v);
        }
        Require(copy);
        return copy;
    }
}
