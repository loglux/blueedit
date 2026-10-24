using System.Text.Json.Nodes;
using Tui;
using static App;

// Linux without ICU (libicu; Debian, Ubuntu and Fedora container images have none): .NET stops the program at its
// first comparison of text. Before anything compares text, .NET's invariant mode is turned on instead: Unicode and
// letter case still work, languages are named by their tags (ru, en-GB). docs/linux.md
if (OperatingSystem.IsLinux() && !IcuPresent()) AppContext.SetSwitch("System.Globalization.Invariant", true);

// --version and --help answer in the shell and exit, before the terminal is taken over (a check after installing)
if (args is ["--version" or "-v" or "-V"]) { Console.WriteLine("BlueEdit " + AppVersion()); return; }
if (args is ["--help" or "-h" or "-?" or "/?"])
{
    Console.WriteLine($"""
        BlueEdit {AppVersion()} - a DOS-style console text editor

        Usage: be [-r] [+LINE] [file[:LINE[:COL]]...]
          -r, --read-only     open the files read-only
          +N, --line N        open the last file at line N
          file:12, file:12:5  open at a line and column, as compilers and grep print locations
          -v, --version       print the version and exit
          -h, --help          print this text and exit
          --install-tools [pyright,ruff,csharp-ls,typescript]
                              install the code tools (all, or those named) into the editor's
                              own folder, no administrator needed; as Tools > Code tools does
          --install-proofing [MODEL,harper,languagetool,ru-RU,...]
                              install grammar checkers and spelling dictionaries (without
                              names: what there is to choose); as Tools > Set up grammar
                              checking and Tools > Get spelling dictionaries do
          --vulkan-devices    the video cards Vulkan sees, a line each: type (1 integrated,
                              2 discrete, 3 virtual, 4 the processor), memory (MB) and name
          --video-card        the video card a model can use and which models fit it
          --grammar NAME,...  the grammar checkers to have on (languagetool, llm, harper, ...)
          --model-server ADDRESS MODEL
                              grammar from a model server; its key, if any, in BLUEEDIT_LLM_KEY
          --check-model-server ADDRESS MODEL
                              whether the server answers, takes the key and has the model

        Press F1 in the editor for the keys.
        """);
    return;
}
// asked by ProofingTools in a process of its own: a video card's driver is loaded and may misbehave
if (args is ["--vulkan-devices"]) { Console.WriteLine(VideoCards.Print(VideoCards.Vulkan())); return; }
// the installer's grammar page (tools/installer): the video card, which models fit it and the one to take
if (args is ["--video-card"]) { Console.WriteLine(ProofingTools.VideoCardReport(ProofingTools.FindVideoCard())); return; }
// the checkers to have on, as Tools > Grammar checking sets them (the installer, once it has installed them)
if (args is ["--grammar", var engines])
{
    var on = engines.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Select(e => e.ToLowerInvariant()).ToList();
    if (on.FirstOrDefault(e => !Settings.GrammarEngines.Contains(e)) is { } unknown)
    {
        Console.WriteLine($"{unknown}: unknown (known: {string.Join(", ", Settings.GrammarEngines)})");
        Environment.Exit(1);
    }
    Settings.SetValue(Settings.FilePath, "grammar", "[" + string.Join(", ", on.Select(JsonString)) + "]");
    return;
}
// a model server for grammar, as Tools > Set up grammar checking > Another model server sets it; the key, if any,
// comes in BLUEEDIT_LLM_KEY, never on the command line (where other programs and logs see it)
// the installer's check of a model server before it is kept: the first line says what was found (ok, key, unreachable,
// model, other), the second the details; the key, as below, in BLUEEDIT_LLM_KEY
if (args is ["--check-model-server", var checkAddress, var checkModel])
{
    var (found, detail) = LlmServer.Probe(new LlmEndpoint(checkAddress.Trim(), checkModel.Trim(), LlmEndpoint.KeyFrom(Environment.GetEnvironmentVariable("BLUEEDIT_LLM_KEY") ?? "")));
    Console.WriteLine(found.ToString().ToLowerInvariant());
    Console.WriteLine(detail.ReplaceLineEndings(" "));
    Environment.Exit(found == LlmServer.ProbeResult.Ok ? 0 : 1);
}
if (args is ["--model-server", var address, var model])
{
    var key = Environment.GetEnvironmentVariable("BLUEEDIT_LLM_KEY") ?? "";
    Settings.SetValue(Settings.FilePath, "llmServer", JsonString(address.Trim()));
    Settings.SetValue(Settings.FilePath, "llmServerModel", JsonString(model.Trim()));
    Settings.SetValue(Settings.FilePath, "llmApiKey", JsonString(LlmEndpoint.SaveKey(key)));
    Settings.SetValue(Settings.FilePath, "llmModel", JsonString("server"));
    return;
}
// --install-tools: the code tools without the editor's screen (servers, scripts): what is there is said, the rest installed
if (args.Length >= 1 && args[0] == "--install-tools")
{
    var wanted = args.Length > 1 ? args[1].Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries) : CodeTools.All.Select(t => t.Name).ToArray();
    int failed = 0;
    Console.WriteLine($"Code tools go into {CodeTools.Root}");
    foreach (var name in wanted)
    {
        if (CodeTools.All.FirstOrDefault(t => t.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) is not { } tool)
        {
            Console.WriteLine($"{name}: unknown (known: {string.Join(", ", CodeTools.All.Select(t => t.Name))})"); failed++; continue;
        }
        if (CodeTools.Find(tool) is { Ours: false } there) { Console.WriteLine($"{tool.Name}: already installed: {there.Path}"); continue; }
        Console.WriteLine($"== {tool.Name} ({tool.Purpose})");
        var error = CodeTools.Install(tool, s => Console.WriteLine("   " + s)).GetAwaiter().GetResult();
        if (error == null) Console.WriteLine($"   ok: {CodeTools.Find(tool)?.Path}");
        else { Console.WriteLine("   FAILED: " + error); failed++; }
    }
    Environment.Exit(failed == 0 ? 0 : 1);
}

// --install-proofing: the grammar checkers without the editor's screen; without names it lists what can be installed
if (args.Length >= 1 && args[0] == "--install-proofing")
{
    var (ps, _) = Settings.Load(Settings.FilePath);
    var homes = new ProofingTools.Homes(LanguageToolHome(ps), LlmHome(ps), HarperChecker.DefaultHome, HunspellSpell.DefaultFolders().First());
    if (args.Length == 1)
    {
        var kind = ProofingTools.LlamaKind();
        Console.WriteLine("Grammar checkers BlueEdit can install (be --install-proofing NAME,NAME):");
        Console.WriteLine();
        Console.WriteLine("  A language model (finds the most), with llama.cpp, " + (kind == "cpu" ? "the CPU build: no video card found, slow" : $"the {kind} build") + ":");
        foreach (var m in ProofingTools.Models)
            Console.WriteLine($"    {m.Key,-12}{m.Size,7}  {m.Licence,-22}{m.Note}" + (ProofingTools.HasModel(homes, m) ? "  (installed)" : ""));
        Console.WriteLine("  Rules, no video card needed:");
        Console.WriteLine("    harper       ~60 MB  Apache 2.0            English" + (ProofingTools.HasHarper(homes) ? "  (installed)" : ""));
        Console.WriteLine("    languagetool  530 MB LGPL, with Java       31 languages" + (ProofingTools.HasLanguageTool(homes) ? "  (installed)" : ""));
        Console.WriteLine();
        Console.WriteLine("A model server at an address (Ollama, LM Studio, a cloud API) needs nothing here: Tools > Set up grammar checking.");
        Console.WriteLine("Then turn the checker on in Tools > Grammar checking.");
        Console.WriteLine();
        Console.WriteLine("Spelling dictionaries (Hunspell, from LibreOffice; " + (OperatingSystem.IsWindows() ? "used when Spelling provider is Hunspell" : "the spelling on this system") + "):");
        foreach (var d in ProofingTools.Dictionaries)
            Console.WriteLine($"    {d.Tag,-7}{d.Size,7}  {Spell.NameBesideTag(d.Tag)}" + (ProofingTools.HasDictionary(homes, d) ? "  (installed)" : HunspellSpell.Find(d.Tag) != null ? "  (in the system)" : ""));
        return;
    }
    int failed = 0;
    foreach (var name in args[1].Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Select(n => n.ToLowerInvariant()))
    {
        if (name is not ("harper" or "languagetool") && ProofingTools.Models.All(m => m.Key != name)
            && ProofingTools.Dictionaries.All(d => !d.Tag.Equals(name, StringComparison.OrdinalIgnoreCase)))
        {
            Console.WriteLine($"{name}: unknown (be --install-proofing lists the names)"); failed++; continue;
        }
        Console.WriteLine($"== {name}");
        var error = ProofingTools.Install(name, homes, s => Console.WriteLine("   " + s)).GetAwaiter().GetResult();
        if (error == null) Console.WriteLine("   ok");
        else { Console.WriteLine("   FAILED: " + error); failed++; }
    }
    Environment.Exit(failed == 0 ? 0 : 1);
}

using (var editorApp = new App(args)) editorApp.Run();
