using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

/// <summary>
/// User settings, kept as JSON with comments in %APPDATA%\blueedit\settings.json (~/.config/blueedit on Linux).
/// BLUEEDIT_CONFIG_DIR points elsewhere (the tests use it so they never touch the real file).
/// Missing or invalid values fall back to the defaults, so a broken file never stops the editor.
/// </summary>
sealed class Settings
{
    public string Theme { get; set; } = "dos-blue";
    /// <summary>The interface's language: "auto" (the system's), "en", or the code of a table (Ui.cs).</summary>
    public string UiLanguage { get; set; } = "auto";
    public int TabSize { get; set; } = 4;
    /// <summary>Paragraph formatting's right margin (Format > Reformat, Align, auto-wrap), in characters.</summary>
    public int RightMargin { get; set; } = 78;
    /// <summary>Wrap while typing at the right margin (real line breaks), in text and Markdown.</summary>
    public bool AutoWrap { get; set; }
    /// <summary>The words of the text in the status line (Markdown: without markup, formulas and code).</summary>
    public bool WordCount { get; set; }
    public bool AutoIndent { get; set; } = true;
    /// <summary>Seconds between copies of unsaved documents for recovery after the editor ended unsaved; 0 = none.</summary>
    public int AutoRecoverSeconds { get; set; } = 10;
    /// <summary>Brackets and quotes typed in pairs: "code" (in code files), "always", "off".</summary>
    public string AutoClose { get; set; } = "code";
    /// <summary>The completion list opening by itself after three letters: "code", "always", "off".</summary>
    public string CompleteWhileTyping { get; set; } = "code";
    public bool SpellCheck { get; set; } = true;
    /// <summary>The fallback when the system names no language (a server's C locale): English.</summary>
    public IReadOnlyList<string> SpellLanguages { get; set; } = new[] { "en-US" };
    /// <summary>"spellLanguages": "auto" — the system's languages (<see cref="Spell.SystemLanguages"/>: the languages
    /// added in Windows that it can spell-check, elsewhere the locale's); <see cref="SpellLanguages"/> is then only the
    /// fallback.</summary>
    public bool SpellAuto { get; set; } = true;
    /// <summary>"windows" (default), "office" or "hunspell": who answers spelling questions (Hunspell always on
    /// Linux and macOS).</summary>
    public string SpellProvider { get; set; } = "windows";
    /// <summary>Let the terminal move scrolled lines itself (much less output). Off: Windows Terminal showed it wrongly.</summary>
    public bool FastScroll { get; set; }
    /// <summary>Shortcuts found by the key whatever the layout (#184): Windows key events, elsewhere the kitty protocol.</summary>
    public bool KeyEvents { get; set; } = true;
    /// <summary>Esc closes the file (and the editor with the last one) when there is nothing to cancel:
    /// "auto" (default) only when Far Manager opened the editor, "on" always, "off" never.</summary>
    public string EscapeQuits { get; set; } = "auto";
    /// <summary>Grammar checkers to use together: "languagetool", "llm" (a local language model), "edge" (Edge's
    /// built-in proofreader, English only), "harper" (Harper's rules, English only), "chrome" (Chrome's proofreader, English only), "chromemodel" (its model without Chrome). Empty (default,
    /// "off" in the file) means no grammar checking.</summary>
    public IReadOnlyList<string> Grammar { get; set; } = Array.Empty<string>();
    /// <summary>Tools > Grammar checking allows several checkers at once (their findings together); off: choosing one
    /// switches the others off. A "grammar" list of several in the file works either way.</summary>
    public bool GrammarTogether { get; set; }
    public static readonly string[] GrammarEngines = { "languagetool", "llm", "edge", "harper", "chrome", "chromemodel" };
    /// <summary>Folder with jre\ and languagetool\; empty means %LOCALAPPDATA%\BlueEdit.</summary>
    public string LanguageToolDir { get; set; } = "";
    /// <summary>Folder with bin\llama-server.exe and models\*.gguf; empty means %LOCALAPPDATA%\BlueEdit\llm.</summary>
    public string LlmDir { get; set; } = "";
    /// <summary>The .gguf model in models\ (or a full path); empty picks the best one present; "server" is
    /// <see cref="LlmServerModel"/> on <see cref="LlmServer"/>.</summary>
    public string LlmModel { get; set; } = "";
    /// <summary>A model server at an address (OpenAI-compatible: Ollama, LM Studio, llama-server elsewhere, a cloud
    /// API), e.g. http://127.0.0.1:11434/v1; empty: none. Used when chosen, or when llama.cpp here is missing or fails.</summary>
    public string LlmServer { get; set; } = "";
    /// <summary>The model to ask that server for (its name there, e.g. "qwen3.5:9b").</summary>
    public string LlmServerModel { get; set; } = "";
    /// <summary>Its API key, or "env:NAME" to read it from an environment variable; empty: none.</summary>
    public string LlmApiKey { get; set; } = "";
    /// <summary>Language servers on (errors and warnings in code, docs/lsp.md).</summary>
    public bool Lsp { get; set; } = true;
    /// <summary>Servers by language name ("Python", "C#"...) on top of the defaults; null turns a default off.</summary>
    public IReadOnlyDictionary<string, ServerCommand?> LanguageServers { get; set; } = new Dictionary<string, ServerCommand?>();
    /// <summary>Formatting programs by language (Formatters.cs); null: that language's formatting off.</summary>
    public IReadOnlyDictionary<string, ServerCommand?> Formatters { get; set; } = new Dictionary<string, ServerCommand?>();
    /// <summary>What style suggestions (Ctrl+F7) look for: keys of StyleAdvisor.Focuses; all but "informal" (the
    /// academic register) by default.</summary>
    public IReadOnlyList<string> StyleFocus { get; set; } = StyleAdvisor.DefaultFocuses;
    /// <summary>LanguageTool's rules: switched off (rule ids, categories), off-by-default ones switched on, and its level
    /// ("default" or "picky"). The defaults are LanguageToolOptions'.</summary>
    public IReadOnlyList<string> LanguageToolDisabledRules { get; set; } = LanguageToolOptions.DefaultDisabledRules;
    public IReadOnlyList<string> LanguageToolDisabledCategories { get; set; } = LanguageToolOptions.DefaultDisabledCategories;
    public IReadOnlyList<string> LanguageToolEnabledRules { get; set; } = Array.Empty<string>();
    public string LanguageToolLevel { get; set; } = "default";
    /// <summary>LanguageTool's Java heap, like "768m" or "1g" (LanguageToolOptions.DefaultMemory: why 768m).</summary>
    public string LanguageToolMemory { get; set; } = LanguageToolOptions.DefaultMemory;
    public LanguageToolOptions LanguageToolOptions =>
        new(LanguageToolDisabledRules, LanguageToolDisabledCategories, LanguageToolEnabledRules, LanguageToolLevel, LanguageToolMemory);
    /// <summary>Findings never shown, whichever checker gives them (F7 > Never show this; Tools > Ignored rules):
    /// LanguageTool rule ids, the language model's and Edge's fixes.</summary>
    public IReadOnlyList<string> IgnoredRules { get; set; } = Array.Empty<string>();
    /// <summary>msedge.exe whose proofreader is used; empty means Edge Canary, then Edge.</summary>
    public string EdgePath { get; set; } = "";
    public bool WrapProse { get; set; } = true;
    public bool WrapCode { get; set; }
    /// <summary>Line numbers at the left, for every file (Alt+G).</summary>
    public bool LineNumbers { get; set; }
    public string MarkdownView { get; set; } = "live";
    /// <summary>Display formulas as typeset pictures where the terminal shows Sixel graphics and the mathimages module
    /// is installed (docs/formula-pictures.md); off: rows of text.</summary>
    public bool FormulaImages { get; set; } = true;
    /// <summary>Formulas that would not typeset (an unclosed brace, \left without \right...) shown as errors.</summary>
    public bool LatexCheck { get; set; } = true;
    /// <summary>Also the rules of writing in formulas (cos without \, * for \times, &lt;= for \le) as warnings.</summary>
    public bool LatexStyle { get; set; } = true;
    /// <summary>"full": every Unicode character formulas can use; "basic": not those most fonts lack (letter subscripts
    /// such as ₙ, ⩽ ⩾ shown as ≤ ≥), for a terminal that shows a box for a character its font does not have.</summary>
    public string FormulaSymbols { get; set; } = "full";
    /// <summary>Colour overrides on top of the theme: name -> "#rrggbb".</summary>
    public IReadOnlyDictionary<string, string> Colors { get; set; } = new Dictionary<string, string>();
    /// <summary>"auto": keep a "BlueEdit" profile in Windows Terminal with the font below; "hint": only explain.</summary>
    public string FontSetup { get; set; } = "auto";
    public string Font { get; set; } = "Cascadia Mono";
    public int FontSize { get; set; } = 12;

    public static string Directory =>
        Environment.GetEnvironmentVariable("BLUEEDIT_CONFIG_DIR") is { Length: > 0 } dir
            ? dir
            : System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData, Environment.SpecialFolderOption.DoNotVerify), "blueedit");

    public static string FilePath => System.IO.Path.Combine(Directory, "settings.json");

    static readonly JsonDocumentOptions ReadOptions = new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

    /// <summary>Reads the file; returns the defaults when it does not exist. Error describes the first problem found, if any.</summary>
    public static (Settings Settings, string? Error) Load(string path)
    {
        var s = new Settings();
        if (!File.Exists(path)) return (s, null);
        try { return Parse(File.ReadAllText(path)); }
        catch (IOException ex) { return (s, ex.Message); }
    }

    public static (Settings Settings, string? Error) Parse(string json)
    {
        var s = new Settings();
        string? error = null;
        void Problem(string text) => error ??= text;
        // languageServers, formatters: a command with its arguments per language, or "off"
        Dictionary<string, ServerCommand?>? Commands(JsonElement v, string key)
        {
            if (v.ValueKind != JsonValueKind.Object) { Problem(key + ": expected { \"Python\": { \"command\": \"...\", \"args\": [...] }, ... }"); return null; }
            var map = new Dictionary<string, ServerCommand?>(StringComparer.OrdinalIgnoreCase);
            foreach (var e in v.EnumerateObject())
            {
                if (e.Value.ValueKind == JsonValueKind.String && e.Value.GetString() == "off") { map[e.Name] = null; continue; }
                if (e.Value.ValueKind == JsonValueKind.Object && e.Value.TryGetProperty("command", out var cmd) && cmd.ValueKind == JsonValueKind.String)
                {
                    var args = e.Value.TryGetProperty("args", out var a) && a.ValueKind == JsonValueKind.Array
                        ? a.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!).ToArray() : [];
                    map[e.Name] = new ServerCommand(cmd.GetString()!, args);
                    continue;
                }
                Problem($"{key}.{e.Name}: expected \"off\" or {{ \"command\": \"...\", \"args\": [...] }}");
            }
            return map;
        }
        JsonDocument doc;
        try { doc = JsonDocument.Parse(json, ReadOptions); }
        catch (JsonException ex) { return (s, "settings.json: " + ex.Message); }
        using (doc)
        {
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return (s, "settings.json: the top level must be { ... }");
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in doc.RootElement.EnumerateObject())
            {
                var v = p.Value;
                if (!seen.Add(p.Name)) Problem($"{p.Name}: set twice, the last one is used");
                switch (p.Name.ToLowerInvariant())
                {
                    case "theme":
                        if (v.ValueKind == JsonValueKind.String && Themes.Names.Contains(v.GetString()!)) s.Theme = v.GetString()!;
                        else Problem($"theme: expected one of {string.Join(", ", Themes.Names)}");
                        break;
                    case "uilanguage":
                        if (v.ValueKind == JsonValueKind.String && v.GetString() is { Length: > 0 } ul) s.UiLanguage = ul;
                        else Problem("uiLanguage: expected \"auto\" or a language code such as \"ru\"");
                        break;
                    case "tabsize":
                        if (v.TryGetInt32(out int t) && t is >= 1 and <= 16) s.TabSize = t; else Problem("tabSize: expected a number from 1 to 16");
                        break;
                    case "rightmargin":
                        if (v.TryGetInt32(out int rm) && rm is >= 20 and <= 500) s.RightMargin = rm; else Problem("rightMargin: expected a number from 20 to 500");
                        break;
                    case "autowrap": if (Bool(v) is bool aw) s.AutoWrap = aw; else Problem("autoWrap: expected true or false"); break;
                    case "wordcount": if (Bool(v) is bool words) s.WordCount = words; else Problem("wordCount: expected true or false"); break;
                    case "autorecoverseconds":
                        if (v.TryGetInt32(out int ar) && ar is >= 0 and <= 3600) s.AutoRecoverSeconds = ar; else Problem("autoRecoverSeconds: expected a number from 0 to 3600");
                        break;
                    case "autoindent": if (Bool(v) is bool ai) s.AutoIndent = ai; else Problem("autoIndent: expected true or false"); break;
                    case "autoclose":
                        if (v.ValueKind == JsonValueKind.String && v.GetString()!.ToLowerInvariant() is "code" or "always" or "off" && v.GetString() is { } ac) s.AutoClose = ac.ToLowerInvariant();
                        else if (Bool(v) is bool acb) s.AutoClose = acb ? "code" : "off";
                        else Problem("autoClose: expected \"code\", \"always\" or \"off\"");
                        break;
                    case "completewhiletyping":
                        if (v.ValueKind == JsonValueKind.String && v.GetString()!.ToLowerInvariant() is "code" or "always" or "off" && v.GetString() is { } cw) s.CompleteWhileTyping = cw.ToLowerInvariant();
                        else if (Bool(v) is bool cwb) s.CompleteWhileTyping = cwb ? "code" : "off";
                        else Problem("completeWhileTyping: expected \"code\", \"always\" or \"off\"");
                        break;
                    case "spellcheck": if (Bool(v) is bool sc) s.SpellCheck = sc; else Problem("spellCheck: expected true or false"); break;
                    case "wrapprose": if (Bool(v) is bool wp) s.WrapProse = wp; else Problem("wrapProse: expected true or false"); break;
                    case "wrapcode": if (Bool(v) is bool wc) s.WrapCode = wc; else Problem("wrapCode: expected true or false"); break;
                    case "linenumbers": if (Bool(v) is bool ln) s.LineNumbers = ln; else Problem("lineNumbers: expected true or false"); break;
                    case "latexcheck": if (Bool(v) is bool lc) s.LatexCheck = lc; else Problem("latexCheck: expected true or false"); break;
                    case "latexstyle": if (Bool(v) is bool ls) s.LatexStyle = ls; else Problem("latexStyle: expected true or false"); break;
                    case "formulaimages": if (Bool(v) is bool fi) s.FormulaImages = fi; else Problem("formulaImages: expected true or false"); break;
                    case "formulasymbols":
                        if (v.ValueKind == JsonValueKind.String && v.GetString()?.ToLowerInvariant() is "full" or "basic") s.FormulaSymbols = v.GetString()!.ToLowerInvariant();
                        else Problem("formulaSymbols: expected \"full\" or \"basic\"");
                        break;
                    case "linenumbersprose" or "linenumberscode": break;      // 0.2.4-0.2.5: one setting since
                    case "spelllanguages":
                        if (v.ValueKind == JsonValueKind.String && string.Equals(v.GetString(), "auto", StringComparison.OrdinalIgnoreCase)) s.SpellAuto = true;
                        else if (v.ValueKind == JsonValueKind.Array && v.EnumerateArray().All(x => x.ValueKind == JsonValueKind.String))
                        {
                            s.SpellLanguages = v.EnumerateArray().Select(x => x.GetString()!).ToArray();
                            s.SpellAuto = false;
                        }
                        else Problem("spellLanguages: expected \"auto\" or a list like [\"ru-RU\", \"en-GB\"]");
                        break;
                    case "markdownview":
                        if (v.ValueKind == JsonValueKind.String && v.GetString() is "source" or "live" or "reading") s.MarkdownView = v.GetString()!;
                        else Problem("markdownView: expected \"source\", \"live\" or \"reading\"");
                        break;
                    case "colors":
                        if (v.ValueKind != JsonValueKind.Object) { Problem("colors: expected { \"keyword\": \"#ffffff\", ... }"); break; }
                        var colors = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                        foreach (var c in v.EnumerateObject())
                        {
                            if (c.Value.ValueKind == JsonValueKind.String && ParseColor(c.Value.GetString()!) is not null && Themes.ColorNames.Contains(c.Name))
                                colors[c.Name] = c.Value.GetString()!;
                            else Problem($"colors.{c.Name}: unknown name or not a \"#rrggbb\" colour");
                        }
                        s.Colors = colors;
                        break;
                    case "spellprovider":
                        if (v.ValueKind == JsonValueKind.String && v.GetString()?.ToLowerInvariant() is "windows" or "office" or "hunspell") s.SpellProvider = v.GetString()!.ToLowerInvariant();
                        else Problem("spellProvider: expected \"windows\", \"office\" or \"hunspell\"");
                        break;
                    case "fastscroll": if (Bool(v) is bool fsc) s.FastScroll = fsc; else Problem("fastScroll: expected true or false"); break;
                    case "keyevents": if (Bool(v) is bool ke) s.KeyEvents = ke; else Problem("keyEvents: expected true or false"); break;
                    case "escapequits":
                        if (Bool(v) is bool eq) s.EscapeQuits = eq ? "on" : "off";
                        else if (v.ValueKind == JsonValueKind.String && v.GetString()?.ToLowerInvariant() is "auto" or "on" or "off") s.EscapeQuits = v.GetString()!.ToLowerInvariant();
                        else Problem("escapeQuits: expected \"auto\", true or false");
                        break;
                    case "grammartogether": if (Bool(v) is bool gt) s.GrammarTogether = gt; else Problem("grammarTogether: expected true or false"); break;
                    case "grammar":
                        {
                            // "off", one name, or a list: ["languagetool", "llm"]
                            var names = v.ValueKind switch
                            {
                                JsonValueKind.String => new[] { v.GetString()!.ToLowerInvariant() },
                                JsonValueKind.Array when v.EnumerateArray().All(x => x.ValueKind == JsonValueKind.String) =>
                                    v.EnumerateArray().Select(x => x.GetString()!.ToLowerInvariant()).ToArray(),
                                _ => null,
                            };
                            names = names?.Where(n => n != "off").Distinct().ToArray();
                            if (names != null && names.All(GrammarEngines.Contains)) s.Grammar = names;
                            else Problem("grammar: expected \"off\", \"languagetool\", \"llm\", \"edge\", \"harper\", \"chrome\", \"chromemodel\" or a list of them");
                            break;
                        }
                    case "llmdir":
                        if (v.ValueKind == JsonValueKind.String) s.LlmDir = v.GetString()!.Trim();
                        else Problem("llmDir: expected a folder path");
                        break;
                    case "stylefocus":
                        {
                            var keys = v.ValueKind == JsonValueKind.Array && v.EnumerateArray().All(x => x.ValueKind == JsonValueKind.String)
                                ? v.EnumerateArray().Select(x => x.GetString()!.ToLowerInvariant()).Distinct().ToArray() : null;
                            if (keys != null && keys.All(StyleAdvisor.AllFocuses.Contains)) s.StyleFocus = keys;
                            else Problem("styleFocus: expected a list of " + string.Join(", ", StyleAdvisor.AllFocuses.Select(k => $"\"{k}\"")));
                            break;
                        }
                    case "lsp": if (Bool(v) is bool lspOn) s.Lsp = lspOn; else Problem("lsp: expected true or false"); break;
                    case "languageservers":
                        // { "Python": { "command": "pyright-langserver", "args": ["--stdio"] }, "C#": "off" }
                        if (Commands(v, "languageServers") is { } servers) s.LanguageServers = servers;
                        break;
                    case "formatters":
                        // { "Python": { "command": "black", "args": ["-q", "-"] }, "C#": "off" }
                        if (Commands(v, "formatters") is { } formatters) s.Formatters = formatters;
                        break;
                    case "llmmodel":
                        if (v.ValueKind == JsonValueKind.String) s.LlmModel = v.GetString()!.Trim();
                        else Problem("llmModel: expected a .gguf file name");
                        break;
                    case "llmserver":
                        if (v.ValueKind == JsonValueKind.String) s.LlmServer = v.GetString()!.Trim();
                        else Problem("llmServer: expected an address like http://127.0.0.1:11434/v1");
                        break;
                    case "llmservermodel":
                        if (v.ValueKind == JsonValueKind.String) s.LlmServerModel = v.GetString()!.Trim();
                        else Problem("llmServerModel: expected the model's name on the server");
                        break;
                    case "llmapikey":
                        if (v.ValueKind == JsonValueKind.String) s.LlmApiKey = v.GetString()!.Trim();
                        else Problem("llmApiKey: expected the key, or \"env:NAME\" for an environment variable");
                        break;
                    case "edgepath":
                        if (v.ValueKind == JsonValueKind.String) s.EdgePath = v.GetString()!.Trim();
                        else Problem("edgePath: expected the path of msedge.exe");
                        break;
                    case "languagetooldisabledrules":
                        if (Strings(v) is { } dr) s.LanguageToolDisabledRules = dr; else Problem("languageToolDisabledRules: expected a list of rule ids");
                        break;
                    case "languagetooldisabledcategories":
                        if (Strings(v) is { } dc)
                            // the old default written into a file is not a choice: today's default (#105)
                            s.LanguageToolDisabledCategories = dc.SequenceEqual(LanguageToolOptions.OldDefaultDisabledCategories) ? LanguageToolOptions.DefaultDisabledCategories : dc;
                        else Problem("languageToolDisabledCategories: expected a list of category ids");
                        break;
                    case "languagetoolenabledrules":
                        if (Strings(v) is { } er) s.LanguageToolEnabledRules = er; else Problem("languageToolEnabledRules: expected a list of rule ids");
                        break;
                    case "languagetoolmemory":
                        if (v.ValueKind == JsonValueKind.String && System.Text.RegularExpressions.Regex.IsMatch(v.GetString()!.Trim(), "^[1-9][0-9]*[mMgG]$")) s.LanguageToolMemory = v.GetString()!.Trim().ToLowerInvariant();
                        else Problem("languageToolMemory: expected a size like \"768m\" or \"1g\"");
                        break;
                    case "languagetoollevel":
                        if (v.ValueKind == JsonValueKind.String && v.GetString()!.ToLowerInvariant() is "default" or "picky") s.LanguageToolLevel = v.GetString()!.ToLowerInvariant();
                        else Problem("languageToolLevel: expected \"default\" or \"picky\"");
                        break;
                    case "ignoredrules":
                        if (Strings(v) is { } ir) s.IgnoredRules = ir; else Problem("ignoredRules: expected a list");
                        break;
                    case "languagetooldir":
                        if (v.ValueKind == JsonValueKind.String) s.LanguageToolDir = v.GetString()!.Trim();
                        else Problem("languageToolDir: expected a folder path");
                        break;
                    case "fontsetup":
                        if (v.ValueKind == JsonValueKind.String && v.GetString() is "auto" or "hint") s.FontSetup = v.GetString()!;
                        else Problem("fontSetup: expected \"auto\" or \"hint\"");
                        break;
                    case "font":
                        if (v.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(v.GetString())) s.Font = v.GetString()!.Trim();
                        else Problem("font: expected a font name like \"Cascadia Mono\"");
                        break;
                    case "fontsize":
                        if (v.TryGetInt32(out int fs) && fs is >= 6 and <= 48) s.FontSize = fs; else Problem("fontSize: expected a number from 6 to 48");
                        break;
                    default:
                        Problem($"unknown setting \"{p.Name}\"");
                        break;
                }
            }
        }
        return (s, error);
    }

    static bool? Bool(JsonElement v) => v.ValueKind switch { JsonValueKind.True => true, JsonValueKind.False => false, _ => null };

    public static int? ParseColor(string text)
    {
        var t = text.Trim().TrimStart('#');
        return t.Length == 6 && int.TryParse(t, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int rgb) ? rgb : null;
    }

    /// <summary>The file written the first time settings are opened: every option with an explanation.</summary>
    public static string Template(Settings s)
    {
        var langs = string.Join(", ", s.SpellLanguages.Select(l => $"\"{l}\""));
        var sb = new StringBuilder();
        sb.Append("// BlueEdit settings. Save (F2) to apply; lines starting with // are comments.\n");
        sb.Append("{\n");
        sb.Append(CultureInfo.InvariantCulture, $"    // colour scheme: {string.Join(", ", Themes.Names.Select(n => $"\"{n}\""))}\n");
        sb.Append(CultureInfo.InvariantCulture, $"    \"theme\": \"{s.Theme}\",\n\n");
        sb.Append("    // the interface's language: \"auto\" (the system's), \"en\", \"ru\"; a file lang/<code>.json in this folder\n");
        sb.Append("    // adds a language or changes strings (English string -> translation)\n");
        sb.Append(CultureInfo.InvariantCulture, $"    \"uiLanguage\": \"{s.UiLanguage}\",\n\n");
        sb.Append("    // spaces inserted by the Tab key\n");
        sb.Append(CultureInfo.InvariantCulture, $"    \"tabSize\": {s.TabSize},\n\n");
        sb.Append("    // the right margin of Format > Reformat paragraph (Alt+R), Align and wrapping while typing\n");
        sb.Append(CultureInfo.InvariantCulture, $"    \"rightMargin\": {s.RightMargin},\n");
        sb.Append("    // wrap while typing: a word typed past the right margin goes to a new line (text and Markdown)\n");
        sb.Append(CultureInfo.InvariantCulture, $"    \"autoWrap\": {Lower(s.AutoWrap)},\n\n");
        sb.Append("    // the number of words in the status line (in Markdown without markup, formulas and code; File > Info has all counts)\n");
        sb.Append(CultureInfo.InvariantCulture, $"    \"wordCount\": {Lower(s.WordCount)},\n\n");
        sb.Append("    // Enter keeps the indentation of the line above, one step deeper after a block's opener (Python's \":\",\n");
        sb.Append("    // \"{\" in C#, JavaScript and other brace languages)\n");
        sb.Append(CultureInfo.InvariantCulture, $"    \"autoIndent\": {Lower(s.AutoIndent)},\n\n");
        sb.Append("    // copies of documents with unsaved edits, every so many seconds, kept apart from the files; after the editor\n");
        sb.Append("    // ended without saving, the next start offers them back. 0: no copies\n");
        sb.Append(CultureInfo.InvariantCulture, $"    \"autoRecoverSeconds\": {s.AutoRecoverSeconds},\n\n");
        sb.Append("    // brackets and quotes typed in pairs: ( adds ), typing ) steps over it, Backspace removes an empty pair,\n");
        sb.Append("    // and an opener typed over a selection wraps it: \"code\" (in code files), \"always\", \"off\"\n");
        sb.Append(CultureInfo.InvariantCulture, $"    \"autoClose\": \"{s.AutoClose}\",\n\n");
        sb.Append("    // the completion list (Ctrl+Space) opens by itself after three letters of a word: \"code\" (in code files,\n");
        sb.Append("    // not in strings or comments), \"always\", \"off\". Tab takes the word; Enter only after choosing with the arrows\n");
        sb.Append(CultureInfo.InvariantCulture, $"    \"completeWhileTyping\": \"{s.CompleteWhileTyping}\",\n\n");
        sb.Append("    // spell checking with the Windows dictionaries (Tools > Spelling languages). \"auto\": the languages added in\n");
        sb.Append("    // Windows (Settings > Time & language > Language); or a list like [\"ru-RU\", \"en-GB\"]\n");
        sb.Append(CultureInfo.InvariantCulture, $"    \"spellCheck\": {Lower(s.SpellCheck)},\n");
        sb.Append(CultureInfo.InvariantCulture, $"    \"spellLanguages\": {(s.SpellAuto ? "\"auto\"" : "[" + langs + "]")},\n");
        sb.Append("    // who checks: \"windows\" (instant), \"office\" (Microsoft Word's dictionaries, in the background) or\n");
        sb.Append("    // \"hunspell\" (LibreOffice dictionaries; always on Linux and macOS)\n");
        sb.Append(CultureInfo.InvariantCulture, $"    \"spellProvider\": \"{s.SpellProvider}\",\n\n");
        sb.Append("    // scrolling: let the terminal move the lines itself (far less output); experimental, Windows Terminal\n");
        sb.Append("    // has shown it wrongly\n");
        sb.Append(CultureInfo.InvariantCulture, $"    \"fastScroll\": {Lower(s.FastScroll)},\n\n");
        sb.Append("    // shortcuts with punctuation and digits (Ctrl+/, Ctrl+1) found by the key in any keyboard layout: on Windows\n");
        sb.Append("    // keys read as key events, elsewhere the kitty keyboard protocol where the terminal has it; false: keys read\n");
        sb.Append("    // as text, as before\n");
        sb.Append(CultureInfo.InvariantCulture, $"    \"keyEvents\": {Lower(s.KeyEvents)},\n\n");
        sb.Append("    // Esc closes the file (the editor with the last one) once there is nothing left to cancel, as in Far's editor:\n");
        sb.Append("    // \"auto\" only when Far Manager opened it, true always, false never\n");
        sb.Append(CultureInfo.InvariantCulture, $"    \"escapeQuits\": {(s.EscapeQuits switch { "on" => "true", "off" => "false", _ => "\"auto\"" })},\n\n");
        sb.Append("    // grammar and style of Markdown and text (Tools > Grammar checking), external programs you install yourself,\n");
        sb.Append("    // running locally (nothing leaves the computer): \"off\", or a list of\n");
        sb.Append("    //   \"languagetool\"  a LanguageTool server: jre\\ and languagetool\\ in languageToolDir (default %LOCALAPPDATA%\\BlueEdit)\n");
        sb.Append("    //   \"llm\"           a language model in llama.cpp: bin\\llama-server.exe and models\\*.gguf in llmDir\n");
        sb.Append("    //                   (default %LOCALAPPDATA%\\BlueEdit\\llm); llmModel picks the .gguf (empty: the best one there)\n");
        sb.Append("    //   \"edge\"          Edge's built-in proofreader, English only (American spelling): Edge Canary, or\n");
        sb.Append("    //                   the msedge.exe in edgePath; turned on by hidden Edge switches, so it may stop working\n");
        sb.Append("    //   \"harper\"        Harper's rules for English (British for en-GB): harper-ls in %LOCALAPPDATA%\\BlueEdit\\harper\n");
        sb.Append("    //                   or on PATH; fast, no model\n");
        sb.Append("    //   \"chrome\"        Chrome's built-in proofreader, English only: a hidden Chrome with a profile of its own;\n");
        sb.Append("    //                   needs its flags on in your Chrome (docs/proofing-setup.md)\n");
        sb.Append("    //   \"chromemodel\"   Chrome's proofreader model without Chrome, in LiteRT-LM (install.ps1 -ChromeModel)\n");
        sb.Append(CultureInfo.InvariantCulture, $"    \"grammar\": {(s.Grammar.Count == 0 ? "\"off\"" : "[" + string.Join(", ", s.Grammar.Select(g => $"\"{g}\"")) + "]")},\n");
        sb.Append("    // Tools > Grammar checking: false = one checker at a time (choosing one switches the others off), true = several\n");
        sb.Append(CultureInfo.InvariantCulture, $"    \"grammarTogether\": {Lower(s.GrammarTogether)},\n");
        sb.Append(CultureInfo.InvariantCulture, $"    \"languageToolDir\": \"{Json(s.LanguageToolDir)}\",\n");
        sb.Append(CultureInfo.InvariantCulture, $"    \"llmDir\": \"{Json(s.LlmDir)}\",\n");
        sb.Append(CultureInfo.InvariantCulture, $"    \"llmModel\": \"{Json(s.LlmModel)}\",\n");
        sb.Append("    // a model server at an address instead of (or, when llama.cpp is missing or fails, in place of) llama.cpp here:\n");
        sb.Append("    // Ollama, LM Studio, llama-server on another computer, a cloud API (then paragraphs leave the computer).\n");
        sb.Append("    // llmModel \"server\" chooses it (Tools > Grammar model lists its models); llmApiKey: the key or \"env:NAME\"\n");
        sb.Append(CultureInfo.InvariantCulture, $"    \"llmServer\": \"{Json(s.LlmServer)}\",\n");
        sb.Append(CultureInfo.InvariantCulture, $"    \"llmServerModel\": \"{Json(s.LlmServerModel)}\",\n");
        sb.Append(CultureInfo.InvariantCulture, $"    \"llmApiKey\": \"{Json(s.LlmApiKey)}\",\n");
        sb.Append(CultureInfo.InvariantCulture, $"    \"edgePath\": \"{Json(s.EdgePath)}\",\n");
        sb.Append("    // LanguageTool's rules (the id and category of a finding are shown by F7): switched off, switched on\n");
        sb.Append("    // (off by default), and \"default\" or \"picky\" (more style rules). Its dictionary rules are always left out\n");
        sb.Append("    // (spelling is the spelling provider's job); the space rules are off because the editor blanks markup with spaces\n");
        sb.Append(CultureInfo.InvariantCulture, $"    \"languageToolDisabledRules\": {JsonList(s.LanguageToolDisabledRules)},\n");
        sb.Append(CultureInfo.InvariantCulture, $"    \"languageToolDisabledCategories\": {JsonList(s.LanguageToolDisabledCategories)},\n");
        sb.Append(CultureInfo.InvariantCulture, $"    \"languageToolEnabledRules\": {JsonList(s.LanguageToolEnabledRules)},\n");
        sb.Append(CultureInfo.InvariantCulture, $"    \"languageToolLevel\": \"{s.LanguageToolLevel}\",\n");
        sb.Append("    // LanguageTool's memory (Java's -Xmx): 768m checks five languages in turn as fast as 1g; 512m was 2.6 times slower\n");
        sb.Append(CultureInfo.InvariantCulture, $"    \"languageToolMemory\": \"{s.LanguageToolMemory}\",\n");
        sb.Append("    // findings never shown, from any checker (F7 > Never show this; Tools > Ignored rules)\n");
        sb.Append(CultureInfo.InvariantCulture, $"    \"ignoredRules\": {JsonList(s.IgnoredRules)},\n\n");
        sb.Append("    // language servers: errors and warnings in code, underlined; Shift+F7 goes through them, F7 explains.\n");
        sb.Append("    // Used when installed: pyright-langserver (Python: npm install -g pyright), csharp-ls (C#: dotnet tool\n");
        sb.Append("    // install -g csharp-ls). languageServers adds or replaces one, \"off\" turns one off, e.g.\n");
        sb.Append("    //   \"languageServers\": { \"Python\": { \"command\": \"pylsp\", \"args\": [] }, \"C#\": \"off\" }\n");
        sb.Append(CultureInfo.InvariantCulture, $"    \"lsp\": {Lower(s.Lsp)},\n");
        sb.Append("    \"languageServers\": {},\n");
        sb.Append("    // Ctrl+F12 formats code: by the language server when it can (csharp-ls), otherwise by a program found\n");
        sb.Append("    // installed — Python: ruff, then black. formatters names one (used before the server's; \"{file}\" is the\n");
        sb.Append("    // file's path, the text comes on standard input) or \"off\", e.g.\n");
        sb.Append("    //   \"formatters\": { \"Python\": { \"command\": \"black\", \"args\": [\"-q\", \"-\"] } }\n");
        sb.Append("    \"formatters\": {},\n\n");
        sb.Append("    // style suggestions (Ctrl+F7, by the language model): what to look for (Tools > Style options)\n");
        sb.Append(CultureInfo.InvariantCulture, $"    \"styleFocus\": [{string.Join(", ", s.StyleFocus.Select(f => $"\"{f}\""))}],\n\n");
        sb.Append("    // soft wrap of long lines: prose (Markdown, LaTeX, text) and code\n");
        sb.Append(CultureInfo.InvariantCulture, $"    \"wrapProse\": {Lower(s.WrapProse)},\n");
        sb.Append(CultureInfo.InvariantCulture, $"    \"wrapCode\": {Lower(s.WrapCode)},\n\n");
        sb.Append("    // line numbers at the left, for every file (Alt+G turns them on or off and saves it here)\n");
        sb.Append(CultureInfo.InvariantCulture, $"    \"lineNumbers\": {Lower(s.LineNumbers)},\n\n");
        sb.Append("    // how Markdown files open: \"source\", \"live\" (markup hidden except on the caret line) or \"reading\"\n");
        sb.Append(CultureInfo.InvariantCulture, $"    \"markdownView\": \"{s.MarkdownView}\",\n\n");
        sb.Append("    // display formulas as typeset pictures: needs the mathimages module and a terminal that shows Sixel\n");
        sb.Append("    // graphics, such as Windows Terminal 1.22 or later (View > Formula pictures); false: rows of text\n");
        sb.Append(CultureInfo.InvariantCulture, $"    \"formulaImages\": {Lower(s.FormulaImages)},\n\n");
        sb.Append("    // formulas that would not typeset (an unclosed brace, \\left without \\right, \\frac with one argument)\n");
        sb.Append("    // underlined as errors in Markdown and LaTeX files: Shift+F7 goes to them, F7 explains\n");
        sb.Append(CultureInfo.InvariantCulture, $"    \"latexCheck\": {Lower(s.LatexCheck)},\n\n");
        sb.Append("    // with latexCheck, also the rules of writing as warnings, each with its fix (F7): cos without \\, \\cos{(x)},\n");
        sb.Append("    // * between numbers, <= and ->, ..., a unit such as cm outside \\text{}; F7 > Never show this drops one rule\n");
        sb.Append(CultureInfo.InvariantCulture, $"    \"latexStyle\": {Lower(s.LatexStyle)},\n\n");
        sb.Append("    // characters of formulas shown as text: \"full\", or \"basic\" without the ones most fonts lack (subscript\n");
        sb.Append("    // letters such as ₙ; ⩽ ⩾ as ≤ ≥), for a terminal that shows boxes for them (docs/terminal-fonts.md)\n");
        sb.Append(CultureInfo.InvariantCulture, $"    \"formulaSymbols\": \"{s.FormulaSymbols}\",\n\n");
        sb.Append("    // A console program cannot change its own font: the terminal draws the text. With \"auto\", BlueEdit keeps\n");
        sb.Append("    // its own profile \"BlueEdit\" in Windows Terminal (your other profiles are not touched) with this font,\n");
        sb.Append("    // size and the theme colours. With \"hint\" it only explains how to set the font (Tools > Terminal font).\n");
        sb.Append(CultureInfo.InvariantCulture, $"    \"fontSetup\": \"{s.FontSetup}\",\n");
        sb.Append(CultureInfo.InvariantCulture, $"    \"font\": \"{s.Font.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal)}\",\n");
        sb.Append(CultureInfo.InvariantCulture, $"    \"fontSize\": {s.FontSize},\n\n");
        sb.Append("    // colour overrides on top of the theme, e.g. \"keyword\": \"#ffcc00\"\n");
        sb.Append(CultureInfo.InvariantCulture, $"    // names: {string.Join(", ", Themes.ColorNames)}\n");
        sb.Append("    \"colors\": {\n");
        sb.Append(string.Join(",\n", s.Colors.Select(c => $"        \"{c.Key}\": \"{c.Value}\"")));
        if (s.Colors.Count > 0) sb.Append('\n');
        sb.Append("    }\n");
        sb.Append("}\n");
        return sb.ToString();
    }

    static string Lower(bool b) => b ? "true" : "false";
    // a JSON list of strings (trimmed, empty ones dropped), or null when it is something else
    static string[]? Strings(JsonElement v) =>
        v.ValueKind == JsonValueKind.Array && v.EnumerateArray().All(x => x.ValueKind == JsonValueKind.String)
            ? v.EnumerateArray().Select(x => x.GetString()!.Trim()).Where(x => x.Length > 0).Distinct().ToArray() : null;

    internal static string JsonList(IEnumerable<string> items) => "[" + string.Join(", ", items.Select(i => $"\"{Json(i)}\"")) + "]";

    static string Json(string s) =>s.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);

    /// <summary>Changes one top-level value (string, number, true/false or a flat list) in place, keeping the user's
    /// comments and layout.</summary>
    public static void SetValue(string path, string key, string jsonValue)
    {
        string text = File.Exists(path) ? File.ReadAllText(path) : Template(new Settings());
        // only a key of the outer object counts: not one inside "colors", in a comment or in a string
        var rx = new Regex($"(\"{Regex.Escape(key)}\"\\s*:\\s*)(\\[[^\\]]*\\]|\"[^\"]*\"|[^,\\r\\n}}]+)", RegexOptions.IgnoreCase);
        var depth = Depths(text);
        var found = rx.Matches(text).FirstOrDefault(m => depth[m.Index] == 1);
        if (found != null)
            text = text[..found.Index] + found.Groups[1].Value + jsonValue + text[(found.Index + found.Length)..];
        else if (Array.IndexOf(depth, 1) is int open and > 0)
        {
            // a new key goes first in the outer object, on its own line, with the file's line ending
            string nl = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
            text = text.Insert(open, nl + "    \"" + key + "\": " + jsonValue + ",");
        }
        else { File.Move(path, path + ".bak", true); SetValue(path, key, jsonValue); return; }   // no object at all: start from the template, keep the old file
        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    // How deep in { } each position is, not counting braces in strings and // comments: 1 = the outer object.
    static int[] Depths(string text)
    {
        var d = new int[text.Length + 1];
        int depth = 0;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '"')
            {
                int j = i + 1;
                while (j < text.Length && text[j] != '"' && text[j] != '\n') j += text[j] == '\\' ? 2 : 1;
                for (int k = i; k <= Math.Min(j, text.Length - 1); k++) d[k] = depth;
                i = j;
                continue;
            }
            if (c == '/' && i + 1 < text.Length && text[i + 1] == '/')
            {
                int j = text.IndexOf('\n', i);
                if (j < 0) j = text.Length;
                for (int k = i; k < j; k++) d[k] = -1;              // a comment is nowhere
                i = j - 1;
                continue;
            }
            if (c == '}') depth--;
            d[i] = depth;
            if (c == '{') depth++;
        }
        d[text.Length] = depth;
        return d;
    }

    /// <summary>Creates the file with all options and their explanations if it does not exist yet.</summary>
    public static void EnsureFile(string path, Settings current)
    {
        if (File.Exists(path)) return;
        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        File.WriteAllText(path, Template(current));
    }
}
