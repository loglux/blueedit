using System.Text.Json.Nodes;
using Tui;

sealed partial class App
{
    internal static string LanguageToolHome(Settings s) => s.LanguageToolDir is { Length: > 0 } d ? d : LanguageToolChecker.DefaultHome;

    internal static string LlmHome(Settings s) => s.LlmDir is { Length: > 0 } d ? d : LlmGrammarChecker.DefaultDir;

    static ProofingTools.Homes ProofingHomes(Settings s) => new(LanguageToolHome(s), LlmHome(s), HarperChecker.DefaultHome, HunspellSpell.DefaultFolders().First());

    void TurnOnGrammar(string engine)
    {
        var on = settings.GrammarTogether ? settings.Grammar.Append(engine).Distinct().ToList() : [engine];
        Settings.SetValue(Settings.FilePath, "grammar", "[" + string.Join(", ", on.Select(p => $"\"{p}\"")) + "]");
        ReloadSettings();
    }

    // installs the parts one after another in the background, then (on the main thread) runs done when all went well
    void InstallProofing(string[] parts, string what, Action done)
    {
        if (installingProofing) { Note(T("Grammar: an installation is running")); return; }
        installingProofing = true;
        var h = ProofingHomes(settings);
        Note(Tf("{0}: installing in the background, the steps show here", what));
        Task.Run(async () =>
        {
            string? error = null;
            foreach (var p in parts)
                if ((error = await ProofingTools.Install(p, h, s => app.Post(() => Note(what + ": " + s)))) != null) break;
            app.Post(() =>
            {
                installingProofing = false;
                // asked for and failed: said in a window, whole, as the status line would cut off what to install
                if (error != null) { note = ""; Dialogs.Ask(app, Tf("{0}: not installed", what), error, [T("OK")], 0); return; }
                done();
            });
        });
    }

    internal static string JsonString(string v) => "\"" + v.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";

    // a model from the tested ones, each with its size and licence; installed (with llama.cpp when missing), then chosen
    void SetUpModelHere()
    {
        var h = ProofingHomes(settings);
        var kind = ProofingTools.LlamaKind();
        var models = ProofingTools.Models;
        var items = models.Select(m => $"{m.Key,-11}{m.Size,7}  {T(m.Licence),-22}{T(m.Note)}" + (ProofingTools.HasModel(h, m) ? "  " + T("(installed)") : "")).ToList();
        var title = kind == "cpu" ? T("Language model — no video card found: slow on the processor") : Tf("Language model — llama.cpp, {0} build", kind);
        if (Dialogs.Pick(app, title, items) is not int i) return;
        var model = models[i];
        void Use()
        {
            Settings.SetValue(Settings.FilePath, "llmModel", JsonString(model.File));
            TurnOnGrammar("llm");
            Note(Tf("Grammar: {0} on, starting (a few seconds)", model.Key));
        }
        if (ProofingTools.HasLlama(h) && ProofingTools.HasModel(h, model)) { Use(); return; }
        InstallProofing([model.Key], model.Key, Use);
    }

    // a model server at an address (Ollama, LM Studio, llama-server elsewhere, a cloud API): its address, model and key
    void SetUpModelServer()
    {
        string address = settings.LlmServer.Length > 0 ? settings.LlmServer : "http://127.0.0.1:11434/v1", model = settings.LlmServerModel, key = settings.LlmApiKey;
        while (true)
        {
            if (Dialogs.Prompt(app, T("Model server"), T("Address (OpenAI-compatible API):"), address) is not { Length: > 0 } a) return;
            if (Dialogs.Prompt(app, T("Model server"), T("Model name there:"), model) is not { Length: > 0 } m) return;
            if (Dialogs.Prompt(app, T("Model server"), T("Key (empty: none; env:NAME reads a variable; kept in a file of its own):"), key) is not { } k) return;
            (address, model, key) = (a.Trim(), m.Trim(), k.Trim());
            // checked before it is kept (its list of models: free, a few seconds at most): a key mistyped or a server
            // not running is said now, not as a failure on the first paragraph; kept anyway if wanted (not running yet)
            Note(Tf("Asking {0}...", address));
            app.Render();
            var (found, detail) = LlmServer.Probe(new LlmEndpoint(address, model, LlmEndpoint.KeyFrom(key)));
            Note("");
            if (found == LlmServer.ProbeResult.Ok) break;
            var host = new LlmEndpoint(address, model, null).Host;
            var why = found switch
            {
                LlmServer.ProbeResult.Key => Tf("{0} did not take the key: {1}", host, detail),
                LlmServer.ProbeResult.Unreachable => Tf("{0} does not answer: {1}", host, detail),
                LlmServer.ProbeResult.Model => Tf("{0} has no model \"{1}\". It has: {2}", host, model, detail),
                _ => Tf("{0} answered: {1}", host, detail),
            };
            var choice = Dialogs.Ask(app, T("Model server"), why, [T("Keep it as it is"), T("Fix it"), T("Cancel")], 2);
            if (choice == 0) break;
            if (choice == 2) return;
        }
        Settings.SetValue(Settings.FilePath, "llmServer", JsonString(address.Trim()));
        Settings.SetValue(Settings.FilePath, "llmServerModel", JsonString(model.Trim()));
        Settings.SetValue(Settings.FilePath, "llmApiKey", JsonString(LlmEndpoint.SaveKey(key)));
        Settings.SetValue(Settings.FilePath, "llmModel", JsonString("server"));
        TurnOnGrammar("llm");
        Note(Tf("Grammar: {0} on {1}; paragraphs are sent there", model.Trim(), address.Trim()));
    }

    // The cloud services as the installer offers them (tools/installer/blueedit.iss, ServerDefaults): the address, the
    // model the Obsidian plugin's benchmark recommends, and where a key is got
    internal static readonly (string Name, string Url, string Model, string KeyAt)[] CloudServices =
    [
        ("Anthropic Claude", "https://api.anthropic.com/v1", "claude-haiku-5-5", "console.anthropic.com"),
        ("OpenAI", "https://api.openai.com/v1", "gpt-5.4-mini", "platform.openai.com"),
        ("Google Gemini", "https://generativelanguage.googleapis.com/v1beta", "gemini-3.5-flash-lite", "aistudio.google.com"),
        ("DeepSeek", "https://api.deepseek.com/v1", "deepseek-flash", "platform.deepseek.com"),
    ];

    // A service's models to choose from, the recommended one first: a dated name of it (Anthropic lists
    // claude-haiku-4-5-20251001 beside claude-haiku-4-5) is not a second row; with no list (not answering), only it
    internal static List<string> CloudModels(string recommended, IEnumerable<string> listed) =>
        listed.Where(m => !m.Equals(recommended, StringComparison.OrdinalIgnoreCase)
                && !m.StartsWith(recommended + "-", StringComparison.OrdinalIgnoreCase))
            .Prepend(recommended).ToList();

    // Set up grammar checking > Cloud service: the service chosen from a list, its key typed, its models listed from
    // the service itself and one chosen; no address or model name to type
    void SetUpCloudService()
    {
        var host = new LlmEndpoint(settings.LlmServer, "", null).Host;
        int now = settings.LlmServer.Length == 0 ? -1
            : Array.FindIndex(CloudServices, c => new LlmEndpoint(c.Url, "", null).Host.Equals(host, StringComparison.OrdinalIgnoreCase));
        if (Dialogs.Pick(app, T("Cloud service"), CloudServices.Select(c => c.Name).ToList(), Math.Max(0, now), current: now) is not int i) return;
        var (name, url, recommended, keyAt) = CloudServices[i];
        // the key kept for this service comes back; another service's is never offered to this one
        string key = i == now ? settings.LlmApiKey : "";
        var models = new List<string>();
        while (true)
        {
            if (Dialogs.Prompt(app, name, Tf("API key ({0}; env:NAME or file:PATH also do):", keyAt), key) is not { Length: > 0 } k) return;
            key = k.Trim();
            Note(Tf("Asking {0} for its models...", name));
            app.Render();
            var endpoint = new LlmEndpoint(url, "", LlmEndpoint.KeyFrom(key));
            var (found, detail) = LlmServer.Probe(endpoint);
            if (found == LlmServer.ProbeResult.Ok) models = LlmServer.ModelsAt(endpoint);
            Note("");
            if (found == LlmServer.ProbeResult.Ok) break;
            var why = found switch
            {
                LlmServer.ProbeResult.Key => Tf("{0} did not take the key: {1}", name, detail),
                LlmServer.ProbeResult.Unreachable => Tf("{0} does not answer: {1}", name, detail),
                _ => Tf("{0} answered: {1}", name, detail),
            };
            var choice = Dialogs.Ask(app, name, why, [T("Keep it as it is"), T("Fix it"), T("Cancel")], 2);
            if (choice == 0) break;
            if (choice == 2) return;
        }
        var list = CloudModels(recommended, models);
        int current = i == now ? list.FindIndex(m => m.Equals(settings.LlmServerModel, StringComparison.OrdinalIgnoreCase)) : -1;
        var rows = list.Select(m => m == recommended ? $"{m}   {T("(recommended)")}" : m).ToList();
        if (Dialogs.Pick(app, Tf("{0}: the model for grammar checking", name), rows, Math.Max(0, current), current: current) is not int pick) return;
        Settings.SetValue(Settings.FilePath, "llmServer", JsonString(url));
        Settings.SetValue(Settings.FilePath, "llmServerModel", JsonString(list[pick]));
        Settings.SetValue(Settings.FilePath, "llmApiKey", JsonString(LlmEndpoint.SaveKey(key)));
        Settings.SetValue(Settings.FilePath, "llmModel", JsonString("server"));
        TurnOnGrammar("llm");
        Note(Tf("Grammar: {0} at {1}; paragraphs are sent there", list[pick], name));
    }

    // Ollama or LM Studio on this computer, at its usual address: its models listed and one chosen, nothing to type.
    // Not running, or without a model: what to do, step by step. Nothing is installed by BlueEdit for them.
    void SetUpModelApp(string name, string url, string[] help)
    {
        Note(Tf("Asking {0} for its models...", name));
        app.Render();
        // embedding models (nomic-embed-text, all-minilm) cannot write text
        var models = LlmServer.ModelsAt(new LlmEndpoint(url, "", null))
            .Where(m => !m.Contains("embed", StringComparison.OrdinalIgnoreCase) && !m.Contains("minilm", StringComparison.OrdinalIgnoreCase)).ToList();
        Note("");
        if (models.Count == 0) { Dialogs.ShowText(app, name, help); return; }
        bool same = settings.LlmModel == "server" && settings.LlmServer.TrimEnd('/') == url;
        int now = same ? models.IndexOf(settings.LlmServerModel) : -1;
        if (Dialogs.Pick(app, Tf("{0}: the model for grammar checking", name), models, Math.Max(0, now), current: now) is not int i) return;
        Settings.SetValue(Settings.FilePath, "llmServer", JsonString(url));
        Settings.SetValue(Settings.FilePath, "llmServerModel", JsonString(models[i]));
        Settings.SetValue(Settings.FilePath, "llmApiKey", JsonString(LlmEndpoint.SaveKey("")));
        Settings.SetValue(Settings.FilePath, "llmModel", JsonString("server"));
        TurnOnGrammar("llm");
        Note(Tf("Grammar: {0} in {1}; paragraphs stay on this computer", models[i], name));
    }

    // Ollama's address: OLLAMA_HOST as Ollama itself reads it (host, host:port or a URL; 0.0.0.0 is where it listens,
    // reached as 127.0.0.1), else 127.0.0.1:11434
    internal static string OllamaUrl()
    {
        var host = Environment.GetEnvironmentVariable("OLLAMA_HOST")?.Trim() ?? "";
        if (host.Length == 0) return "http://127.0.0.1:11434/v1";
        if (!host.Contains("://", StringComparison.Ordinal)) host = "http://" + host;
        if (!Uri.TryCreate(host, UriKind.Absolute, out var u)) return "http://127.0.0.1:11434/v1";
        var name = u.Host is "0.0.0.0" or "" ? "127.0.0.1" : u.Host;
        return $"{u.Scheme}://{name}:{(u.IsDefaultPort && !host.Contains($":{u.Port}", StringComparison.Ordinal) ? 11434 : u.Port)}/v1";
    }

    void SetUpOllama() => SetUpModelApp("Ollama", OllamaUrl(),
    [
        "",
        "  " + Tf("{0} does not answer at {1}, or has no model yet.", "Ollama", OllamaUrl()[..^3]),
        "",
        "  " + Tf("1. Install it: {0}", OperatingSystem.IsWindows() ? "https://ollama.com  (or: winget install Ollama.Ollama)"
                                                                   : "curl -fsSL https://ollama.com/install.sh | sh"),
        "  " + Tf("2. Get a model, in a terminal: {0}", "ollama pull gemma3:4b"),
        "  " + T("   (gemma3:4b is 3.3 GB; qwen3:8b and gemma3:12b find more, with a video card)"),
        "  " + Tf("3. Choose {0} here again.", "Ollama"),
        "",
    ]);

    void SetUpLmStudio() => SetUpModelApp("LM Studio", "http://127.0.0.1:1234/v1",
    [
        "",
        "  " + Tf("{0} does not answer at {1}, or has no model yet.", "LM Studio", "127.0.0.1:1234"),
        "",
        "  " + Tf("1. Install it: {0}", "https://lmstudio.ai"),
        "  " + T("2. Download a model in it, then turn its server on:"),
        "  " + T("   the Developer tab, Status: Running (or in a terminal: lms server start)"),
        "  " + Tf("3. Choose {0} here again.", "LM Studio"),
        "",
    ]);

    void SetUpGrammar()
    {
        if (installingProofing) { Note(T("Grammar: an installation is running")); return; }
        var h = ProofingHomes(settings);
        var kind = ProofingTools.LlamaKind();
        string Have(bool b) => b ? "  " + T("(installed)") : "";
        static string Row(string what, string note) => $"{what,-24} {note}";
        var items = new[]
        {
            Row(T("A language model here"), (kind == "cpu" ? T("llama.cpp and a model; no video card found: slow") : Tf("llama.cpp and a model; uses the video card ({0})", kind))
                + Have(ProofingTools.HasLlama(h))),
            Row("Ollama", T("a program for models (ollama.com); its models are listed")),
            Row("LM Studio", T("a program for models (lmstudio.ai); its models are listed")),
            Row(T("Cloud service"), T("Claude, OpenAI, Gemini, DeepSeek: with a key; the text leaves the computer")),
            Row(T("Another model server"), T("an address: llama-server on the network, any OpenAI-compatible API")),
            Row("LanguageTool", T("rules for 31 languages; no video card (Java, ~530 MB)")) + Have(ProofingTools.HasLanguageTool(h)),
            Row("Harper", T("fast rules for English; no video card (~60 MB)")) + Have(ProofingTools.HasHarper(h)),
        };
        // Remove is not a way to check grammar: a button under the list, not a row of it
        if (Dialogs.Pick(app, T("Set up grammar checking"), items, buttons: [T("Remove what was installed here...")]) is not int i) return;
        switch (i)
        {
            case 0: SetUpModelHere(); break;
            case 1: SetUpOllama(); break;
            case 2: SetUpLmStudio(); break;
            case 3: SetUpCloudService(); break;
            case 4: SetUpModelServer(); break;
            case 5:
                if (ProofingTools.HasLanguageTool(h)) { TurnOnGrammar("languagetool"); Note(T("Grammar: LanguageTool on, starting (a few seconds)")); }
                else InstallProofing(["languagetool"], "LanguageTool", () => { TurnOnGrammar("languagetool"); Note(T("Grammar: LanguageTool installed and on, starting")); });
                break;
            case 6:
                if (ProofingTools.HasHarper(h)) { TurnOnGrammar("harper"); Note(T("Grammar: Harper on")); }
                else InstallProofing(["harper"], "Harper", () => { TurnOnGrammar("harper"); Note(T("Grammar: Harper installed and on")); });
                break;
            case 7: RemoveProofing(); break;
        }
    }

    internal static string SizeText(long bytes) => bytes >= 1_000_000_000 ? $"{bytes / 1e9:0.0} GB" : $"{Math.Max(1, bytes / 1_000_000)} MB";

    // Set up grammar checking > Remove: what BlueEdit installed in its own folders (never a folder the settings point
    // elsewhere, which may be the user's own), with sizes, nothing ticked. A ticked checker is turned off first, so its
    // process ends and lets go of the files; then they are deleted in the background.
    void RemoveProofing()
    {
        var own = new ProofingTools.Homes(LanguageToolChecker.DefaultHome, LlmGrammarChecker.DefaultDir, HarperChecker.DefaultHome,
            HunspellSpell.DefaultFolders().First());
        var parts = ProofingTools.InstalledParts(own);
        if (parts.Count == 0) { Note(T("Nothing is installed here")); return; }
        var items = parts.Select(p => $"{p.Name,-46}{SizeText(p.Bytes),8}").ToList();
        var dialog = app.RunModal(new CheckListDialog(T("Remove what was installed here"), items, parts.Select(_ => false).ToList(),
            hint: T("Space on/off   Enter remove   Esc cancel")));
        if (dialog.Checked is not { } c) return;
        var gone = parts.Where((_, i) => c[i]).ToList();
        if (gone.Count == 0) return;
        var keys = gone.Select(p => p.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        // a model or llama.cpp going: the model here is turned off (a server at an address stays)
        bool llmGoes = settings.LlmModel != "server" && settings.LlmServer.Length == 0
            && (keys.Contains("llama") || gone.Any(p => p.Key.EndsWith(".gguf", StringComparison.OrdinalIgnoreCase)));
        var on = settings.Grammar.Where(g => !(g == "languagetool" && keys.Contains("languagetool")) && !(g == "harper" && keys.Contains("harper"))
            && !(g == "llm" && llmGoes)).ToList();
        if (keys.Contains(settings.LlmModel)) Settings.SetValue(Settings.FilePath, "llmModel", JsonString(""));
        if (on.Count != settings.Grammar.Count) Settings.SetValue(Settings.FilePath, "grammar", "[" + string.Join(", ", on.Select(g => $"\"{g}\"")) + "]");
        if (llmGoes) { llm?.Dispose(); llm = null; llmServer?.Dispose(); llmServer = null; }
        ReloadSettings();
        installingProofing = true;
        Note(Tf("Removing {0}...", string.Join(", ", gone.Select(p => p.Name))));
        Task.Run(async () =>
        {
            var failed = new List<string>();
            foreach (var p in gone)
                if (await ProofingTools.Remove(p) is { } error) failed.Add($"{p.Name} ({error})");
            app.Post(() =>
            {
                installingProofing = false;
                if (gone.Any(p => ProofingTools.Dictionaries.Any(d => d.Tag == p.Key)))
                {
                    spell.Dispose();
                    spell = CreateSpell(settings, spellWanted);
                    editor.Spell = spell;
                    editor.SpellOn = settings.SpellCheck && spell.Available;
                }
                Note(failed.Count == 0 ? Tf("Removed; {0} freed", SizeText(gone.Sum(p => p.Bytes)))
                    : Tf("Not removed: {0}", string.Join("; ", failed)));
            });
        });
    }

    // Tools > Get spelling dictionaries: Hunspell's dictionaries from LibreOffice into BlueEdit's own folder, no root
    // needed. A language the system already has a dictionary for is shown so and not ticked: nothing is fetched twice.
    // Ticked at first: the spelling languages that have no dictionary anywhere.
    void GetDictionaries()
    {
        if (installingProofing) { Note(T("Spelling: an installation is running")); return; }
        var h = ProofingHomes(settings);
        var dicts = ProofingTools.Dictionaries;
        string Where(ProofingTools.SpellDictionary d) =>
            ProofingTools.HasDictionary(h, d) ? "  " + T("(installed)") : HunspellSpell.Find(d.Tag) != null ? "  " + T("(in the system)") : "";
        var items = dicts.Select(d => $"{d.Tag,-7}{d.Size,7}  {Spell.NameBesideTag(d.Tag)}{Where(d)}").ToList();
        var initial = dicts.Select(d => spellWanted.Contains(d.Tag, StringComparer.OrdinalIgnoreCase) && HunspellSpell.Find(d.Tag) == null).ToList();
        var dialog = app.RunModal(new CheckListDialog(T("Spelling dictionaries (LibreOffice's, for Hunspell)"), items, initial,
            hint: T("Space on/off   Enter download   Esc cancel")));
        if (dialog.Checked is not { } c) return;
        var tags = dicts.Where((x, i) => c[i] && HunspellSpell.Find(x.Tag) == null).Select(x => x.Tag).ToArray();
        if (tags.Length == 0) { Note(T("Spelling: nothing to download, the ticked ones are here already")); return; }
        InstallProofing(tags, "Dictionaries", () =>
        {
            // the new languages join the spelling languages (unless they follow Windows); the provider loads them anew
            if (!settings.SpellAuto)
                Settings.SetValue(Settings.FilePath, "spellLanguages",
                    "[" + string.Join(", ", settings.SpellLanguages.Concat(tags).Distinct(StringComparer.OrdinalIgnoreCase).Select(t => $"\"{t}\"")) + "]");
            spell.Dispose();
            spell = CreateSpell(settings, spellWanted);
            editor.Spell = spell;
            ReloadSettings();
            Note(spell.Name == "Hunspell" ? Tf("Spelling: {0} downloaded", string.Join(", ", tags)) : Tf("Spelling: {0} downloaded; used when Tools > Spelling provider is Hunspell", string.Join(", ", tags)));
        });
    }

    // Tools > Grammar checking: any of the local checkers, together; none means off.
    void GrammarSettings()
    {
        var ltHome = LanguageToolHome(settings);
        var llmHome = LlmHome(settings);
        bool ltInstalled = LanguageToolChecker.InstalledIn(ltHome);
        bool llmInstalled = LlmGrammarChecker.InstalledIn(llmHome, settings.LlmModel == "server" ? null : settings.LlmModel) || settings.LlmServer.Length > 0;
        var model = LlmGrammarChecker.ModelIn(llmHome, settings.LlmModel);
        bool edgeInstalled = BrowserProofreader.EdgeIn(settings.EdgePath) != null;
        bool harperInstalled = HarperChecker.ServerIn(HarperChecker.DefaultHome) != null;
        bool chromeInstalled = BrowserProofreader.ChromeIn(null) != null;
        bool chromeModelInstalled = ChromeModelChecker.PythonIn(ChromeModelChecker.DefaultHome) != null && ChromeModelChecker.ModelInChrome() != null;
        // the names' column as wide as the longest name in the interface's language, two spaces after it
        int nameWidth = new[] { "LanguageTool", T("Language model"), "Edge", "Harper", "Chrome", T("Chrome model") }.Max(x => x.Length) + 2;
        string Line(string what, string note) => what.PadRight(nameWidth) + note;
        string Missing(bool installed) => installed ? "" : "   " + T("(not installed)");
        var items = new[]
        {
            Line("LanguageTool", T("rules for many languages, explains each finding")) + Missing(ltInstalled),
            Line(T("Language model"), settings.LlmModel == "server" && RemoteLlm(settings) is { } r ? Tf("{0} on {1} (Tools > Grammar model)", r.Model, r.Host)
                : model != null ? Tf("{0} (Tools > Grammar model)", LlmGrammarChecker.DisplayName(model)) : T("local model in llama.cpp")) + Missing(llmInstalled),
            Line("Edge", T("Edge's built-in proofreader, English only (experimental)")) + Missing(edgeInstalled),
            Line("Harper", T("fast rules for English, British or American by the language")) + Missing(harperInstalled),
            Line("Chrome", T("Chrome's built-in proofreader, English only (its flags on)")) + Missing(chromeInstalled),
            Line(T("Chrome model"), T("the same model without Chrome (LiteRT-LM), English, no GPU")) + Missing(chromeModelInstalled),
        };
        // one checker at a time by default (a choice, like a switch); the last line allows several, their findings together
        int together = items.Length;
        items = items.Append(T("Several at once (their findings shown together)")).ToArray();
        var initial = Settings.GrammarEngines.Select(e => settings.Grammar.Contains(e)).Append(settings.GrammarTogether || settings.Grammar.Count > 1).ToArray();
        void Flip(bool[] on, int i)
        {
            on[i] = !on[i];
            if (i == together)
            {
                if (!on[i]) { int keep = Array.IndexOf(on, true); for (int k = 0; k < together; k++) on[k] = k == keep; }  // back to one
                return;
            }
            if (on[i] && !on[together]) for (int k = 0; k < together; k++) on[k] = k == i;
        }
        var d = app.RunModal(new CheckListDialog(T("Grammar checking"), items, initial, hint: T("Space on/off   Enter OK   Esc cancel   (none = off)"), flip: Flip,
            radio: (on, i) => i < together && !on[together]));
        if (d.Checked is not { } c) return;
        var missing = new List<string>();
        if (c[0] && !ltInstalled) missing.AddRange(new[] { "  " + Tf("LanguageTool needs, in {0}:", ltHome), "    jre\\            " + T("Java 21 JRE (e.g. the Eclipse Temurin zip)"), "    languagetool\\   " + T("LanguageTool-stable.zip from languagetool.org"), "" });
        if (c[1] && !llmInstalled) missing.AddRange(new[] { "  " + Tf("The language model needs, in {0}:", llmHome), "    bin\\            " + T("llama.cpp for Windows (the CUDA or Vulkan zip from its GitHub releases)"), "    models\\         " + T("a .gguf model, e.g. Gemma 3 12B or Phi-4-mini (Q4_K_M)"), "" });
        if (c[2] && !edgeInstalled) missing.AddRange(new[] { "  " + T("Edge's proofreader needs Microsoft Edge Canary (microsoft.com/edge/download/insider),"), "  " + T("or the msedge.exe of an Edge that has it in edgePath. Its model (~1.2 GB) downloads on first use."), "" });
        if (c[5] && !chromeModelInstalled) missing.AddRange(new[] { "  " + T("Chrome's model without Chrome needs LiteRT-LM (tools\\proofing-setup\\install.ps1 -ChromeModel, Python 3.10+)"), "  " + T("and the model in your Chrome: its proofreader's flags on, used once (docs/proofing-setup.md)."), "" });
        if (c[4] && !chromeInstalled) missing.AddRange(new[] { "  " + T("Chrome's proofreader needs Google Chrome with its flags on: docs/proofing-setup.md, \"Chrome\"."), "" });
        if (c[3] && !harperInstalled) missing.AddRange(new[] { "  " + Tf("Harper needs harper-ls, in {0} or on PATH:", HarperChecker.DefaultHome), "    " + T("harper-ls-x86_64-pc-windows-msvc.zip from github.com/Automattic/harper/releases"), "" });
        if (missing.Count > 0)
        {
            Dialogs.ShowText(app, T("Not installed"), new[] { "" }.Concat(missing).Append("  " + T("All of it is portable: unpack, no installer, no administrator rights."))
                .Append("  " + T("Tools > Set up grammar checking installs LanguageTool, Harper or a language model itself.")).Append("").ToList());
            return;
        }
        var picked = Settings.GrammarEngines.Where((_, i) => c[i]).ToList();
        Settings.SetValue(Settings.FilePath, "grammar", picked.Count == 0 ? "\"off\"" : "[" + string.Join(", ", picked.Select(p => $"\"{p}\"")) + "]");
        if (c[together] != settings.GrammarTogether) Settings.SetValue(Settings.FilePath, "grammarTogether", c[together] ? "true" : "false");
        ReloadSettings();
        Note(picked.Count == 0 ? T("Grammar checking off") : Tf("Grammar: {0} starting (a few seconds)", grammar?.Name));
    }

    // The shared language model server for these settings, made on first need (null: llama.cpp or a model is missing).
    LlmServer? SharedLlm(Settings s) => llmServer ??= MakeLlm(s);

    // llama.cpp here first (the .gguf chosen, or the best one); a server at an address when "server" is chosen, or in its
    // place when llama.cpp or a model is missing here or llama.cpp does not start (#95)
    LlmServer? MakeLlm(Settings s)
    {
        var remote = RemoteLlm(s);
        if (remote is { Private: false }) Note(Tf("Language model on {0}: paragraphs are sent there", remote.Host));
        if (s.LlmModel == "server") return remote == null ? null : LlmServer.At(remote);
        var local = LlmGrammarChecker.ServerFor(LlmHome(s), s.LlmModel);
        if (local == null) return remote == null ? null : LlmServer.At(remote, "no llama.cpp or .gguf model here");
        local.Fallback = remote;
        return local;
    }

    internal static LlmEndpoint? RemoteLlm(Settings s) => s.LlmServer.Length == 0 ? null : new LlmEndpoint(s.LlmServer, s.LlmServerModel, LlmEndpoint.KeyFrom(s.LlmApiKey));

    // Tools > Style options: what style suggestions look for; saved as "styleFocus".
    void StyleOptions()
    {
        var f = StyleAdvisor.Focuses;
        var d = app.RunModal(new CheckListDialog(T("Style suggestions look for"), f.Select(x => T(x.Label)).ToList(),
            f.Select(x => settings.StyleFocus.Contains(x.Key)).ToArray(), hint: T("Space on/off   Enter OK   Esc cancel   (none = the defaults)")));
        if (d.Checked is not { } c) return;
        var keys = f.Where((_, i) => c[i]).Select(x => x.Key).ToList();
        if (keys.Count == 0) keys = StyleAdvisor.DefaultFocuses.ToList();
        Settings.SetValue(Settings.FilePath, "styleFocus", "[" + string.Join(", ", keys.Select(k => $"\"{k}\"")) + "]");
        ReloadSettings();
        Note(Tf("Style: {0} of {1} kinds of improvement (Ctrl+F7 on a paragraph)", keys.Count, f.Length));
    }

    // Tools > Grammar model: which of the installed .gguf models the language model checker uses; "Auto" = the best one.
    void GrammarModel()
    {
        var home = LlmHome(settings);
        var models = LlmGrammarChecker.ModelsIn(home);
        // the models of the server at an address, under the ones here (#95): llama.cpp here first, the server second
        var remote = RemoteLlm(settings);
        if (remote != null) { Note(Tf("Asking {0} for its models...", remote.Host)); app.Render(); }
        var served = remote != null ? LlmServer.ModelsAt(remote) : new List<string>();
        if (models.Count == 0 && served.Count == 0)
        {
            Dialogs.ShowText(app, T("No models"), new[] { "", "  " + (remote != null ? Tf("No .gguf model in {0}, and {1} lists none.", Path.Combine(home, "models"), remote.Host)
                    : Tf("No .gguf model in {0}.", Path.Combine(home, "models"))),
                "  " + T("Tools > Set up grammar checking installs a model, or connects Ollama, LM Studio"), "  " + T("or another model server."), "" });
            return;
        }
        var items = new List<string>();
        if (models.Count > 0)
        {
            items.Add(Tf("Auto: the best one installed ({0})", LlmGrammarChecker.DisplayName(models[0])));
            items.AddRange(models.Select(m => $"{LlmGrammarChecker.DisplayName(m),-20}{new FileInfo(m).Length / 1e9,5:0.0} GB   {Path.GetFileName(m)}"));
        }
        int firstServed = items.Count;
        items.AddRange(served.Select(m => $"{m,-25} " + Tf("on {0}", remote!.Host)));
        var current = LlmGrammarChecker.ModelIn(home, settings.LlmModel);
        int selected = settings.LlmModel == "server" ? firstServed + Math.Max(0, served.IndexOf(settings.LlmServerModel))
            : string.IsNullOrWhiteSpace(settings.LlmModel) || models.Count == 0 ? 0 : 1 + models.ToList().FindIndex(m => string.Equals(m, current, StringComparison.OrdinalIgnoreCase));
        // the one in use, when it is in the list: Auto when none is named
        int now = settings.LlmModel == "server" ? (served.IndexOf(settings.LlmServerModel) is >= 0 and var k ? firstServed + k : -1)
            : string.IsNullOrWhiteSpace(settings.LlmModel) ? (models.Count > 0 ? 0 : -1)
            : models.ToList().FindIndex(m => string.Equals(m, current, StringComparison.OrdinalIgnoreCase)) is >= 0 and var j ? 1 + j : -1;
        if (Dialogs.Pick(app, T("Grammar model"), items, Math.Max(0, selected), current: now) is not int i) return;
        static string Json(string v) => "\"" + v.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
        string name;
        if (i >= firstServed)
        {
            name = served[i - firstServed];
            Settings.SetValue(Settings.FilePath, "llmServerModel", Json(name));
            Settings.SetValue(Settings.FilePath, "llmModel", Json("server"));
            name = Tf("{0} on {1}", name, remote!.Host);
        }
        else
        {
            Settings.SetValue(Settings.FilePath, "llmModel", Json(i == 0 ? "" : Path.GetFileName(models[i - 1])));
            name = LlmGrammarChecker.DisplayName(i == 0 ? models[0] : models[i - 1]);
        }
        ReloadSettings();
        Note(settings.Grammar.Contains("llm") ? Tf("Grammar model: {0}, starting (a few seconds)", name) : Tf("Grammar model: {0}; turn on \"Language model\" in Tools > Grammar checking", name));
    }

    // Tools > Spelling languages: "Auto" (the system's languages) or a hand-picked set; saved in settings.json.
    void SpellingLanguages()
    {
        var system = Spell.SystemLanguages();
        var known = spell.Name == "Hunspell" ? HunspellSpell.InstalledTags() : Spell.SupportedTags();
        var tags = system.Concat(spell.Languages).Concat(known).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var found = system.Count > 0 ? string.Join(", ", system) : T("none found");
        var items = new List<string> { OperatingSystem.IsWindows() ? Tf("Auto: as in Windows ({0})", found) : Tf("Auto: the system's languages ({0})", found) };
        items.AddRange(tags.Select(t => $"{t,-7} {Spell.NameBesideTag(t)}"));
        var initial = new List<bool> { settings.SpellAuto };
        initial.AddRange(tags.Select(t => !settings.SpellAuto && spell.Languages.Contains(t, StringComparer.OrdinalIgnoreCase)));
        var d = app.RunModal(new CheckListDialog(T("Spelling languages"), items, initial, firstIsExclusive: true,
            hint: OperatingSystem.IsWindows() ? T("Space on/off   Enter OK   Esc cancel   (more: Windows Settings > Language)")
                : T("Space on/off   Enter OK   Esc cancel   (more: Tools > Get spelling dictionaries)")));
        if (d.Checked is not { } c) return;
        var picked = tags.Where((_, i) => c[i + 1]).ToList();
        var json = c[0] || picked.Count == 0 ? "\"auto\"" : "[" + string.Join(", ", picked.Select(t => $"\"{t}\"")) + "]";
        Settings.SetValue(Settings.FilePath, "spellLanguages", json);
        ReloadSettings();
        Note(Tf("Spelling: {0}", (settings.SpellAuto ? T("auto") + ", " : "") + string.Join("/", spell.Languages.Select(Spell.ShortTag))));
    }

    // Tools > Spelling provider: Windows (default), Microsoft Office or Hunspell (the only one on Linux and macOS).
    void SpellingProvider()
    {
        if (!OperatingSystem.IsWindows()) { Note(Tf("Spelling: Hunspell ({0})", spell.Available ? string.Join("/", spell.Languages) : T("no dictionaries: Tools > Get spelling dictionaries"))); return; }
        bool word = OfficeSpell.Installed;
        var items = new[]
        {
            $"{"Windows",-17}" + T("built in, instant (default)"),
            $"{"Microsoft Office",-17}" + T("Word's dictionaries, checked in the background") + (word ? "" : "   " + T("(Word not found)")),
            $"{"Hunspell",-17}" + Tf("LibreOffice dictionaries in {0}", @"%LOCALAPPDATA%\BlueEdit\hunspell"),
        };
        int current = settings.SpellProvider switch { "office" => 1, "hunspell" => 2, _ => 0 };
        if (Dialogs.Pick(app, T("Spelling provider"), items, current, current: current) is not int i) return;
        if (i == 1 && !word) { Note(T("Microsoft Word is not installed")); return; }
        Settings.SetValue(Settings.FilePath, "spellProvider", i switch { 1 => "\"office\"", 2 => "\"hunspell\"", _ => "\"windows\"" });
        ReloadSettings();
        Note(spell.Name == "Office" ? T("Spelling: Office, Word is starting") : Tf("Spelling: {0}", spell.Name));
    }
}
