using System.Diagnostics;
using System.Text.Json.Nodes;

/// <summary>
/// The grammar checkers' programs, installed by the editor itself — the same parts and sources as
/// tools/proofing-setup/install.ps1 and install.sh, into the folders the checkers look in: a language model (llama.cpp
/// from its GitHub release, checked against the SHA-256 GitHub gives, and a .gguf model chosen from
/// <see cref="Models"/>, checked against Hugging Face's SHA-256), Harper (its GitHub release, a pinned version and
/// sum) and, for those who want it, LanguageTool (Java from Eclipse Temurin with Adoptium's SHA-256; LanguageTool's
/// archive has no published sum, so it is checked for the server jar). No administrator, nothing outside those folders.
/// Spelling dictionaries for Hunspell come from LibreOffice's repository at a pinned commit, checked by SHA-256.
/// Tools → Grammar checking offers it for a checker that is missing; `be --install-proofing` does it in a shell.
/// </summary>
static class ProofingTools
{
    /// <summary>Where each part goes: the folders the checkers are given (settings may move them).</summary>
    public sealed record Homes(string LanguageTool, string Llm, string Harper, string Hunspell);

    /// <summary>A model tested with BlueEdit (tools/proofbench): its key, Hugging Face repository and file, size of
    /// the file (measured), licence, what it is good at, and the video memory it wants in GB (the file and llama.cpp's
    /// working space for a paragraph, as the notes say).</summary>
    public sealed record Model(string Key, string Repo, string File, string Size, string Licence, string Note, int MemoryGb);

    public static readonly Model[] Models =
    [
        new("qwen3-8b", "Qwen/Qwen3-8B-GGUF", "Qwen3-8B-Q4_K_M.gguf", "4.7 GB", "Apache 2.0", "many languages, ~6 GB of video memory", 6),
        new("yandexgpt", "yandex/YandexGPT-5-Lite-8B-instruct-GGUF", "YandexGPT-5-Lite-8B-instruct-Q4_K_M.gguf", "4.6 GB",
            "Yandex's own licence", "the best at Russian here, ~6 GB of video memory", 6),
        new("gemma3-12b", "bartowski/google_gemma-3-12b-it-GGUF", "google_gemma-3-12b-it-Q4_K_M.gguf", "6.8 GB", "Gemma terms (Google)",
            "many languages, ~8 GB", 8),
        new("gemma3-4b", "bartowski/google_gemma-3-4b-it-GGUF", "google_gemma-3-4b-it-Q4_K_M.gguf", "2.3 GB", "Gemma terms (Google)",
            "for small video cards, ~3 GB", 3),
        new("phi4-mini", "bartowski/microsoft_Phi-4-mini-instruct-GGUF", "microsoft_Phi-4-mini-instruct-Q4_K_M.gguf", "2.3 GB", "MIT",
            "the fastest and weakest, ~3 GB", 3),
    ];

    /// <summary>A Hunspell spelling dictionary from LibreOffice's repository: the language, its .dic/.aff there
    /// (without the extension), their SHA-256 at <see cref="DictionaryCommit"/> and the size of both.</summary>
    public sealed record SpellDictionary(string Tag, string Source, string DicSha256, string AffSha256, string Size);

    /// <summary>LibreOffice/dictionaries pinned to one commit, so the sums below hold (12 October 2026).</summary>
    const string DictionaryCommit = "67032ee957631908098a72edf48dc81cac27e369";

    public static readonly SpellDictionary[] Dictionaries =
    [
        new("de-DE", "de/de_DE_frami", "4ca3c958b0e5545910999bc246f668840bf8ede3df8e5e6790d05edd5a586c38", "646bf3333ac69c23e9d794533ee5241d6f755c359e8fe10a648f87613743d594", "4.4 MB"),
        new("en-GB", "en/en_GB", "04e90f34f5263bf26780e9c4a442e9ad16584e227af49ddd1b3b21b01df5b29c", "0fd6ed120ef28957847d98ba5149b117e27116cf81b5aa36208453f6755a36fd", "1.3 MB"),
        new("en-US", "en/en_US", "f0b1a234bd178bdd01875b2a392a9647f888b8fe879f79c52aae62c2759b3647", "e746c882dd6f303c2c46e7452804b9201115a6942cfeb15f18f8edf774d2e24e", "0.6 MB"),
        new("es-ES", "es/es_ES", "6975dddec3d5d2c676069537bc67b4b5f786c65c5d4cf6703a82acf779ac9ec1", "e73a9bf8e1383f4986a5dc9e2fbed49371c0c61f511c626d15586bd433c1cad9", "0.9 MB"),
        new("fr-FR", "fr_FR/dictionaries/fr", "b78a868e31dd6e373b6c3217969afb898a9acde828a5e7ef97308da42218c88c", "c176610cd5dc4846806a65ddd029f422d87978bf58f224aa44222662a16a2de5", "1.4 MB"),
        new("it-IT", "it_IT/it_IT", "bae1e3501dcd2a923669592493b3fde6c02aae7c7aab83bf5e5b49077e73dd64", "951afaa19272f13555b8823e8bcf9ccf78f8fe1a07835bdfb912ab3e4d537c2b", "1.4 MB"),
        new("pl-PL", "pl_PL/pl_PL", "c0848440599eb88e5aca500418d5f389e562ec2c157b63dbe39d354658ffba49", "82973651651aa930335c865b339b98db376ca3dbf3a661b70b9eeb71fdf41dca", "5.5 MB"),
        new("pt-BR", "pt_BR/pt_BR", "a38bfb26b68ece2834e79fe83e48d5792652970ace12db89d1b9674bf9933183", "21d8ad2a769a60e17e2b5ea4ef11d4d593a58b9e2a82d642ef82d6a4c5523865", "5.5 MB"),
        new("pt-PT", "pt_PT/pt_PT", "e29ba2d7aa8a2ad43e9cb46ac6473064b661545c87002aea90e18899d98d3cc9", "975a209fcc892cb382fa5f34a28c391a39668661ce373ae071287809c5fcae24", "1.6 MB"),
        new("ru-RU", "ru_RU/ru_RU", "f6047416a0204adbecf3a451b874ec8a97ee37e2cbc714466ef04d8dbcc0d6fc", "38ce7d4af78e211e9bafe4bf7e3d6a2c420591136cb738ec6648f8fdf6524cd7", "3.5 MB"),
        new("uk-UA", "uk_UA/uk_UA", "2e5a9e67be63bdb089b3459addb5d71113319d13768e277bcae20f3cc1ad5a93", "2219dd15e9802adebc45722c60943b1472640260491af38dd3e43b07e75585e6", "9.1 MB"),
    ];

    /// <summary>The language's dictionary in BlueEdit's own Hunspell folder (other folders are the system's).</summary>
    public static bool HasDictionary(Homes h, SpellDictionary d) =>
        File.Exists(Path.Combine(h.Hunspell, d.Tag.Replace('-', '_') + ".dic")) && File.Exists(Path.Combine(h.Hunspell, d.Tag.Replace('-', '_') + ".aff"));

    /// <summary>The llama.cpp release BlueEdit was tested with (as in the setup scripts).</summary>
    const string LlamaBuild = "b11371";

    // Harper's language server, the version BlueEdit was tested with; the SHA-256 GitHub shows for each asset
    const string HarperVersion = "v2.12.0";
    const string HarperSha256Windows = "455f82163054ac54a489b01e64ec62916dec5560165074f756633fe3f53d994e";
    const string HarperSha256Linux = "da87a01c49e535d6d6157f388ffc87edd9d4065fe04cc13b7a10b227defb1ba7";

    static string Exe(string name) => OperatingSystem.IsWindows() ? name + ".exe" : name;
    static string LlamaServer(Homes h) => Path.Combine(h.Llm, "bin", Exe("llama-server"));

    public static bool HasLlama(Homes h) => File.Exists(LlamaServer(h));
    public static bool HasModel(Homes h, Model m) => File.Exists(Path.Combine(h.Llm, "models", m.File));
    public static bool HasHarper(Homes h) => File.Exists(Path.Combine(h.Harper, Exe("harper-ls")));
    public static bool HasLanguageTool(Homes h) => LanguageToolChecker.InstalledIn(h.LanguageTool);

    // ---------- the video card: which llama.cpp build ----------

    /// <summary>The NVIDIA driver's major version from nvidia-smi, or null without one.</summary>
    static int? NvidiaDriver()
    {
        var o = RunQuiet("nvidia-smi", "--query-gpu=driver_version --format=csv,noheader");
        return o != null && int.TryParse(o.Trim().Split('\n')[0].Split('.')[0], out int v) ? v : null;
    }

    // its output when it exits well within the time (milliseconds, -1: no limit); one that hangs is stopped
    static string? RunQuiet(string exe, string args, int timeout = -1)
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo(exe, args) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true });
            if (p == null) return null;
            var o = p.StandardOutput.ReadToEndAsync();
            _ = p.StandardError.ReadToEndAsync();
            if (!p.WaitForExit(timeout)) { p.Kill(true); return null; }
            return p.ExitCode == 0 ? o.Result : null;
        }
        catch (System.ComponentModel.Win32Exception) { return null; }
    }

    /// <summary>Which llama.cpp build suits this computer, as the setup scripts choose: CUDA with an NVIDIA driver
    /// (13.4 from driver 580), else Vulkan where Vulkan sees a video card, else the CPU. Found once a run; asking
    /// takes up to a few seconds the first time after a start of Windows (nvidia-smi 0.7 s, Vulkan's drivers 2.7 s,
    /// measured), so a menu's opening starts it in the background (<see cref="LookForVideoCard"/>).</summary>
    public static string LlamaKind() => Kind.Value.Result;

    public static void LookForVideoCard() => _ = Kind.Value;

    static readonly Lazy<Task<string>> Kind = new(() => Task.Run(() =>
    {
        var driver = NvidiaDriver();
        return LlamaKind(OperatingSystem.IsWindows(), driver, driver == null ? VulkanGpu() : null);
    }));

    /// <summary>When Vulkan could not be asked (null), Windows keeps the Vulkan build, which also runs on the
    /// processor, and Linux takes the CPU build.</summary>
    internal static string LlamaKind(bool windows, int? nvidiaDriver, bool? vulkanGpu) =>
        nvidiaDriver is int d ? (d >= 580 ? "cuda-13.4" : windows ? "cuda-12.4" : "cuda-12.8")
        : (vulkanGpu ?? windows) ? "vulkan" : "cpu";

    /// <summary>Whether Vulkan sees a video card, asked of this program in a process of its own
    /// (<see cref="VideoCards"/>); null when it could not be asked.</summary>
    static bool? VulkanGpu() =>
        Environment.ProcessPath is { } be && RunQuiet(be, "--vulkan-devices", 15000) is { } o ? VideoCards.Parse(o)?.Exists(d => d.IsGpu) : null;

    /// <summary>A video card a model can be put in: its name and memory in MB.</summary>
    public sealed record VideoCard(string Name, int MemoryMb);

    /// <summary>The video card with the most memory of its own: NVIDIA's from nvidia-smi, else what Vulkan sees as
    /// a discrete (or virtual) card. Null without one: an integrated card borrows the computer's memory and runs a
    /// model about as slowly as the processor, so it counts as none. Takes up to a few seconds (as LlamaKind).</summary>
    public static VideoCard? FindVideoCard()
    {
        // "12282 NVIDIA GeForce RTX 4070" or "none": the installer's page as another computer would show it, in
        // Windows Sandbox, which has no video card (tools/sandbox/installer.ps1)
        if (Environment.GetEnvironmentVariable("BLUEEDIT_VIDEO_CARD") is { Length: > 0 } given)
            return given.Split(' ', 2) is [var mb, var name] && int.TryParse(mb, out int m) ? new VideoCard(name, m) : null;
        if (RunQuiet("nvidia-smi", "--query-gpu=name,memory.total --format=csv,noheader,nounits") is { } o)
        {
            var cards = o.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(l => l.Split(',', StringSplitOptions.TrimEntries))
                .Where(f => f.Length == 2 && int.TryParse(f[1], out _))
                .Select(f => new VideoCard(f[0], int.Parse(f[1], System.Globalization.CultureInfo.InvariantCulture))).ToList();
            if (cards.Count > 0) return cards.MaxBy(c => c.MemoryMb);
        }
        var devices = Environment.ProcessPath is { } be && RunQuiet(be, "--vulkan-devices", 15000) is { } v ? VideoCards.Parse(v) : null;
        return BestCard(devices);
    }

    internal static VideoCard? BestCard(List<VideoCards.Device>? devices) =>
        devices?.Where(d => d.IsDedicated && d.MemoryMb > 0).OrderByDescending(d => d.MemoryMb).Select(d => new VideoCard(d.Name, d.MemoryMb)).FirstOrDefault();

    /// <summary>Whether the model fits in the card's memory. A card sold as 8 GB reports a little under 8192 MB
    /// (8188 here), so a GB counts as 1000 MB.</summary>
    public static bool Fits(Model m, VideoCard? card) => card != null && card.MemoryMb >= m.MemoryGb * 1000;

    /// <summary>The best model by the benchmark (the order Auto uses, LlmGrammarChecker.Preferred) that fits the card; null
    /// when none does.</summary>
    public static Model? Recommended(VideoCard? card) =>
        Models.Where(m => Fits(m, card)).OrderBy(m => LlmGrammarChecker.Rank(m.File)).FirstOrDefault();

    /// <summary>What <c>be --video-card</c> prints for the installer's page: "card MB name" (or "card none"), a line a
    /// model, "fits KEY" or "needs KEY GB", and "recommended KEY" when one fits.</summary>
    public static string VideoCardReport(VideoCard? card)
    {
        var lines = new List<string> { card == null ? "card none" : $"card {card.MemoryMb} {card.Name}" };
        lines.AddRange(Models.Select(m => Fits(m, card) ? $"fits {m.Key}" : $"needs {m.Key} {m.MemoryGb}"));
        if (Recommended(card) is { } best) lines.Add($"recommended {best.Key}");
        return string.Join('\n', lines);
    }

    /// <summary>The release's archives for that build: the program, and for CUDA its runtime libraries.</summary>
    internal static IEnumerable<string> LlamaAssets(bool windows, string kind) => windows
        ? kind.StartsWith("cuda", StringComparison.Ordinal)
            ? [$"llama-{LlamaBuild}-bin-win-{kind}-x64.zip", $"cudart-llama-bin-win-{kind}-x64.zip"]
            : [$"llama-{LlamaBuild}-bin-win-{kind}-x64.zip"]
        : kind.StartsWith("cuda", StringComparison.Ordinal)
            ? [$"llama-{LlamaBuild}-bin-ubuntu-{kind}-x64.tar.gz", $"cudart-llama-{LlamaBuild}-bin-ubuntu-{kind}-x64.tar.gz"]
            : [$"llama-{LlamaBuild}-bin-ubuntu-{(kind == "cpu" ? "" : kind + "-")}x64.tar.gz"];

    // ---------- removing ----------

    /// <summary>A part BlueEdit installed in its own folders: its key ("llama", "languagetool", "harper", a model's
    /// file name, a dictionary's tag), what to show, its files or folders and their size.</summary>
    public sealed record InstalledPart(string Key, string Name, string[] Paths, long Bytes);

    /// <summary>What is installed in these folders, the biggest kinds first: llama.cpp, the models, LanguageTool with
    /// its Java, Harper, the spelling dictionaries.</summary>
    public static List<InstalledPart> InstalledParts(Homes h)
    {
        var parts = new List<InstalledPart>();
        void Add(string key, string name, params string[] paths)
        {
            var there = paths.Where(p => File.Exists(p) || Directory.Exists(p)).ToArray();
            if (there.Length > 0) parts.Add(new(key, name, there, there.Sum(SizeOf)));
        }
        Add("llama", "llama.cpp", Path.Combine(h.Llm, "bin"));
        var models = Path.Combine(h.Llm, "models");
        if (Directory.Exists(models))
            foreach (var m in Directory.EnumerateFiles(models, "*.gguf").Order(StringComparer.OrdinalIgnoreCase)) Add(Path.GetFileName(m), Path.GetFileName(m), m);
        if (HasLanguageTool(h)) Add("languagetool", "LanguageTool + Java", Path.Combine(h.LanguageTool, "languagetool"), Path.Combine(h.LanguageTool, "jre"));
        Add("harper", "Harper", h.Harper);
        foreach (var d in Dictionaries.Where(d => HasDictionary(h, d)))
        {
            var stem = Path.Combine(h.Hunspell, d.Tag.Replace('-', '_'));
            Add(d.Tag, d.Tag, stem + ".dic", stem + ".aff");
        }
        return parts;
    }

    static long SizeOf(string path) => File.Exists(path) ? new FileInfo(path).Length
        : Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories).Sum(f => new FileInfo(f).Length);

    /// <summary>Deletes a part; a checker stopped just before may hold its files a moment longer, so a few tries. The
    /// reason when it could not, else null.</summary>
    public static async Task<string?> Remove(InstalledPart part)
    {
        foreach (var p in part.Paths)
            for (int attempt = 0; ; attempt++)
            {
                try
                {
                    if (File.Exists(p)) File.Delete(p);
                    else if (Directory.Exists(p)) Directory.Delete(p, true);
                    break;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    if (attempt >= 10) { CodeTools.Log($"FAILED removing {p}: {ex}"); return ex.Message; }
                    await Task.Delay(300);
                }
            }
        CodeTools.Log("removed " + string.Join(", ", part.Paths));
        return null;
    }

    // ---------- installing ----------

    /// <summary>Installs one part: "languagetool", "harper", "llama", a model's key or a dictionary's language ("ru-RU"); the reason when it failed,
    /// else null. Runs on the caller's thread: call it in the background.</summary>
    public static async Task<string?> Install(string part, Homes h, Action<string> say, CancellationToken ct = default)
    {
        try
        {
            switch (part)
            {
                case "languagetool": await InstallLanguageTool(h, say, ct); break;
                case "harper": await InstallHarper(h, say, ct); break;
                case "llama": await InstallLlama(h, say, ct); break;
                case var tag when Dictionaries.FirstOrDefault(d => d.Tag.Equals(tag, StringComparison.OrdinalIgnoreCase)) is { } dict:
                    await InstallDictionary(h, dict, say, ct);
                    break;
                default:
                    var m = Models.FirstOrDefault(x => x.Key == part) ?? throw new InvalidOperationException($"unknown part '{part}'");
                    await InstallModel(h, m, say, ct);
                    break;
            }
            return null;
        }
        catch (Exception ex) when (ex is IOException or HttpRequestException or InvalidOperationException or InvalidDataException
                                   or UnauthorizedAccessException or TaskCanceledException or System.Text.Json.JsonException)
        {
            CodeTools.Log("FAILED " + part + ": " + ex);
            return ex.Message;
        }
    }

    // unpacks into a new folder next to dest and puts it in dest's place; an archive with a single top folder gives
    // that folder's contents (Temurin's jdk-21…-jre/, LanguageTool-6.x/)
    static void UnpackInto(string archive, string dest)
    {
        var tmp = dest + ".new";
        CodeTools.Unpack(archive, tmp);
        File.Delete(archive);
        var top = Directory.GetFileSystemEntries(tmp);
        var src = top.Length == 1 && Directory.Exists(top[0]) ? top[0] : tmp;
        if (Directory.Exists(dest)) Directory.Delete(dest, true);
        Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
        Directory.Move(src, dest);
        if (Directory.Exists(tmp)) Directory.Delete(tmp, true);
    }

    static async Task InstallLanguageTool(Homes h, Action<string> say, CancellationToken ct)
    {
        Directory.CreateDirectory(h.LanguageTool);
        if (!File.Exists(Path.Combine(h.LanguageTool, "jre", "bin", Exe("java"))))
        {
            say("Java: which is the newest 21...");
            var os = OperatingSystem.IsWindows() ? "windows" : "linux";
            var info = JsonNode.Parse(await CodeTools.Http.GetStringAsync(
                $"https://api.adoptium.net/v3/assets/latest/21/hotspot?architecture=x64&image_type=jre&os={os}&vendor=eclipse", ct));
            var pkg = info?[0]?["binary"]?["package"] ?? throw new InvalidOperationException("no Java 21 in Adoptium's answer");
            var version = (string?)info?[0]?["version"]?["openjdk_version"] ?? "21";
            var archive = await CodeTools.Download((string)pkg["link"]!, (string?)pkg["checksum"], $"Java {version}", say, ct, h.LanguageTool);
            say($"Java {version}: unpacking...");
            UnpackInto(archive, Path.Combine(h.LanguageTool, "jre"));
        }
        var lt = await CodeTools.Download("https://languagetool.org/download/LanguageTool-stable.zip", null, "LanguageTool", say, ct,
            h.LanguageTool, noSumPublished: true);
        say("LanguageTool: unpacking...");
        UnpackInto(lt, Path.Combine(h.LanguageTool, "languagetool"));
        if (!HasLanguageTool(h)) throw new InvalidDataException("languagetool-server.jar is not in the archive from languagetool.org");
    }

    // harper-ls and llama.cpp on Windows are built with Microsoft's C++ compiler and need its runtime (vcruntime140.dll,
    // vcruntime140_1.dll, msvcp140.dll), which a clean Windows does not have: in Windows Sandbox harper-ls did not
    // start ("no answer to initialize"), nothing said why. Microsoft allows the three DLLs next to the program that
    // uses them ("app-local"), so they are put there, no administrator needed: taken out of Microsoft's own installer
    // (its signature checked) with Windows' expand.exe, chosen by the name and machine written in each file, and each
    // file's signature checked too. docs/tech/proofing-internals.md.
    const string VcRedistUrl = "https://aka.ms/vs/17/release/vc_redist.x64.exe";
    static readonly string[] VcRuntimeFiles = ["vcruntime140.dll", "vcruntime140_1.dll", "msvcp140.dll"];
    static string VcRuntimeCache => Path.Combine(CodeTools.Root, "vcruntime");

    /// <summary>Whether a program in dir finds the runtime: in System32 (installed for everyone) or in dir itself.</summary>
    internal static bool HasVcRuntime(string dir) => !OperatingSystem.IsWindows()
        || VcRuntimeFiles.All(f => File.Exists(Path.Combine(Environment.SystemDirectory, f)))
        || VcRuntimeFiles.All(f => File.Exists(Path.Combine(dir, f)));

    // puts the runtime next to the programs in dir when they would not find it
    static async Task EnsureVcRuntime(string dir, Action<string> say, CancellationToken ct)
    {
        if (HasVcRuntime(dir)) return;
        if (!VcRuntimeFiles.All(f => File.Exists(Path.Combine(VcRuntimeCache, f)))) await FetchVcRuntime(say, ct);
        foreach (var f in VcRuntimeFiles) File.Copy(Path.Combine(VcRuntimeCache, f), Path.Combine(dir, f), true);
    }

    static async Task FetchVcRuntime(Action<string> say, CancellationToken ct)
    {
        const string what = "Microsoft Visual C++ runtime";
        say($"{what} is missing: getting it from Microsoft...");
        var work = Path.Combine(CodeTools.Root, ".vcredist");
        if (Directory.Exists(work)) Directory.Delete(work, true);
        var file = await CodeTools.Download(VcRedistUrl, null, what, say, ct, work, noSumPublished: true);
        try
        {
            // no sum is published for the permanent link: Windows checks the signature instead
            if (SignedByMicrosoft([file]) is { } bad)
                throw new InvalidDataException($"{what}: the installer from {VcRedistUrl} is not signed by Microsoft ({bad})");
            say($"{what}: unpacking...");
            // the installer (a WiX bundle) carries its packages as cabinet files after its own code; the package
            // cabinets hold more cabinets, those the DLLs, under names of their own (vcruntime140.dll_amd64)
            var bytes = await File.ReadAllBytesAsync(file, ct);
            var cabs = new List<string>();
            foreach (int at in CabinetsIn(bytes))
            {
                var cab = Path.Combine(work, $"c{at}.cab");
                await File.WriteAllBytesAsync(cab, bytes.AsMemory(at, (int)BitConverter.ToUInt32(bytes, at + 8)), ct);
                cabs.Add(cab);
            }
            var found = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < cabs.Count; i++)
            {
                var into = cabs[i] + ".d";
                Directory.CreateDirectory(into);
                if (RunQuiet(Path.Combine(Environment.SystemDirectory, "expand.exe"), $"\"{cabs[i]}\" -F:* \"{into}\"") == null) continue;
                foreach (var f in Directory.EnumerateFiles(into))
                {
                    if (IsCabinet(f)) { File.Move(f, f + ".cab"); cabs.Add(f + ".cab"); continue; }
                    var name = FileVersionInfo.GetVersionInfo(f).OriginalFilename;
                    if (name != null && VcRuntimeFiles.Contains(name, StringComparer.OrdinalIgnoreCase) && IsX64(f)) found[name] = f;
                }
            }
            if (VcRuntimeFiles.FirstOrDefault(f => !found.ContainsKey(f)) is { } missing)
                throw new InvalidDataException($"{what}: {missing} is not in Microsoft's installer");
            if (SignedByMicrosoft(VcRuntimeFiles.Select(f => found[f]).ToList()) is { } badDll)
                throw new InvalidDataException($"{what}: a DLL in the installer is not signed by Microsoft ({badDll})");
            Directory.CreateDirectory(VcRuntimeCache);
            foreach (var f in VcRuntimeFiles) File.Copy(found[f], Path.Combine(VcRuntimeCache, f), true);
        }
        finally { Directory.Delete(work, true); }
    }

    // where cabinet files start: "MSCF", four zero bytes, then the cabinet's size
    internal static IEnumerable<int> CabinetsIn(byte[] b)
    {
        for (int i = 0; i + 12 <= b.Length; i += 8)
        {
            int k = b.AsSpan(i).IndexOf("MSCF\0\0\0\0"u8);
            if (k < 0) yield break;
            i += k;
            if (i + 12 <= b.Length && BitConverter.ToUInt32(b, i + 8) is var size && size > 0 && i + size <= b.Length) yield return i;
        }
    }

    static bool IsCabinet(string file)
    {
        using var s = File.OpenRead(file);
        Span<byte> head = stackalloc byte[4];
        return s.Read(head) == 4 && head.SequenceEqual("MSCF"u8);
    }

    // a PE file for x64 (the installer holds the ARM64 ones too, named alike)
    static bool IsX64(string file)
    {
        var b = File.ReadAllBytes(file);
        if (b.Length < 0x40 || b[0] != 'M' || b[1] != 'Z') return false;
        int pe = BitConverter.ToInt32(b, 0x3c);
        return pe > 0 && pe + 6 <= b.Length && BitConverter.ToUInt16(b, pe + 4) == 0x8664;
    }

    // null when Windows finds every file validly signed by Microsoft Corporation, else what it found. The signer's name
    // differs: the installer is "CN=Microsoft Corporation, O=Microsoft Corporation, …"; a DLL Windows knows from its own
    // catalog (Windows Sandbox) is "CN=Microsoft Windows, O=Microsoft Corporation, …" — the organisation is compared
    internal static bool MicrosoftSigned(string line) =>
        line.StartsWith("Valid|CN=", StringComparison.Ordinal) && line.Contains(", O=Microsoft Corporation,", StringComparison.Ordinal);

    static string? SignedByMicrosoft(List<string> files)
    {
        var list = string.Join(",", files.Select(f => "'" + f.Replace("'", "''", StringComparison.Ordinal) + "'"));
        var o = RunQuiet("powershell.exe", "-NoProfile -Command \"" + list
            + " | ForEach-Object { $s = Get-AuthenticodeSignature -LiteralPath $_; $s.Status.ToString() + '|' + $s.SignerCertificate.Subject }\"");
        if (o == null) return "the signature could not be checked";
        var lines = o.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return lines.Length == files.Count && lines.All(MicrosoftSigned) ? null
            : string.Join("; ", lines);
    }

    static async Task InstallHarper(Homes h, Action<string> say, CancellationToken ct)
    {
        bool win = OperatingSystem.IsWindows();
        var file = win ? "harper-ls-x86_64-pc-windows-msvc.zip" : "harper-ls-x86_64-unknown-linux-gnu.tar.gz";
        var archive = await CodeTools.Download($"https://github.com/Automattic/harper/releases/download/{HarperVersion}/{file}",
            win ? HarperSha256Windows : HarperSha256Linux, $"Harper {HarperVersion}", say, ct, h.Harper);
        var tmp = Path.Combine(h.Harper, ".new");
        CodeTools.Unpack(archive, tmp);
        File.Delete(archive);
        // only harper-ls is taken: the folder may hold harper-cli too, put there by hand
        var exe = Directory.EnumerateFiles(tmp, Exe("harper-ls"), SearchOption.AllDirectories).FirstOrDefault()
            ?? throw new InvalidDataException("harper-ls is not in Harper's archive");
        File.Move(exe, Path.Combine(h.Harper, Exe("harper-ls")), true);
        Directory.Delete(tmp, true);
        await EnsureVcRuntime(h.Harper, say, ct);
        if (Check(Path.Combine(h.Harper, Exe("harper-ls")), "--version") is { } error) throw new InvalidOperationException("harper-ls does not start: " + error);
    }

    static async Task InstallLlama(Homes h, Action<string> say, CancellationToken ct)
    {
        bool win = OperatingSystem.IsWindows();
        var kind = LlamaKind();
        say($"llama.cpp {LlamaBuild}, the {kind} build...");
        var rel = JsonNode.Parse(await CodeTools.Http.GetStringAsync($"https://api.github.com/repos/ggml-org/llama.cpp/releases/tags/{LlamaBuild}", ct));
        var assets = (rel?["assets"] as JsonArray)?.OfType<JsonObject>().ToList() ?? [];
        var bin = Path.Combine(h.Llm, "bin");
        var tmp = bin + ".new";
        if (Directory.Exists(tmp)) Directory.Delete(tmp, true);
        Directory.CreateDirectory(tmp);
        foreach (var name in LlamaAssets(win, kind))
        {
            var a = assets.FirstOrDefault(x => (string?)x["name"] == name) ?? throw new InvalidOperationException($"no {name} in llama.cpp {LlamaBuild}");
            var archive = await CodeTools.Download((string)a["browser_download_url"]!, ((string?)a["digest"])?.Replace("sha256:", "", StringComparison.Ordinal),
                name, say, ct, h.Llm);
            say($"{name}: unpacking...");
            var part = Path.Combine(h.Llm, ".part");
            CodeTools.Unpack(archive, part);
            File.Delete(archive);
            var top = Directory.GetFileSystemEntries(part);
            var src = top.Length == 1 && Directory.Exists(top[0]) ? top[0] : part;       // the Linux archives have a top folder
            foreach (var f in Directory.EnumerateFiles(src, "*", SearchOption.AllDirectories))
            {
                var to = Path.Combine(tmp, Path.GetRelativePath(src, f));
                Directory.CreateDirectory(Path.GetDirectoryName(to)!);
                File.Move(f, to, true);
            }
            Directory.Delete(part, true);
        }
        if (!File.Exists(Path.Combine(tmp, Exe("llama-server")))) throw new InvalidDataException("llama-server is not in llama.cpp's archive");
        if (Directory.Exists(bin)) Directory.Delete(bin, true);
        Directory.Move(tmp, bin);
        await EnsureVcRuntime(bin, say, ct);
        // it may need a system library the computer lacks: said now, not when the first paragraph is checked (a NAS
        // without libgomp.so.1, OpenMP, measured)
        if (Check(LlamaServer(h), "--version") is { } error)
            throw new InvalidOperationException("llama-server does not start: " + error
                + (error.Contains("libgomp", StringComparison.Ordinal) ? " (on Debian or Ubuntu: sudo apt install libgomp1)" : ""));
    }

    // runs a program once; its first error line when it fails, else null
    static string? Check(string exe, string args)
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo(exe, args) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true });
            if (p == null) return "did not start";
            var err = p.StandardError.ReadToEndAsync();
            p.StandardOutput.ReadToEnd();
            if (!p.WaitForExit(30_000)) { p.Kill(); return "no answer in 30 s"; }
            if (p.ExitCode == unchecked((int)0xC0000135)) return "a system library is missing (STATUS_DLL_NOT_FOUND)";
            return p.ExitCode == 0 ? null : err.Result.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0) ?? $"exit code {p.ExitCode}";
        }
        catch (System.ComponentModel.Win32Exception ex) { return ex.Message; }
    }

    // the .aff first: a .dic without its .aff is not taken by HunspellSpell, so a half-done install leaves nothing used
    static async Task InstallDictionary(Homes h, SpellDictionary d, Action<string> say, CancellationToken ct)
    {
        var name = d.Tag.Replace('-', '_');
        foreach (var (ext, sum) in new[] { (".aff", d.AffSha256), (".dic", d.DicSha256) })
        {
            var file = await CodeTools.Download($"https://raw.githubusercontent.com/LibreOffice/dictionaries/{DictionaryCommit}/{d.Source}{ext}",
                sum, d.Tag + ext, say, ct, h.Hunspell);
            File.Move(file, Path.Combine(h.Hunspell, name + ext), true);
        }
    }

    static async Task InstallModel(Homes h, Model m, Action<string> say, CancellationToken ct)
    {
        if (!HasLlama(h)) await InstallLlama(h, say, ct);
        say($"{m.Key}: asking Hugging Face for its checksum...");
        var tree = JsonNode.Parse(await CodeTools.Http.GetStringAsync($"https://huggingface.co/api/models/{m.Repo}/tree/main", ct)) as JsonArray;
        var entry = tree?.OfType<JsonObject>().FirstOrDefault(e => (string?)e["path"] == m.File)
            ?? throw new InvalidOperationException($"{m.File} is not in huggingface.co/{m.Repo}");
        var dir = Path.Combine(h.Llm, "models");
        Directory.CreateDirectory(dir);
        long size = (long?)entry["lfs"]?["size"] ?? 0;
        long free = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(dir))!).AvailableFreeSpace;
        if (size > 0 && free < size + (1L << 30))
            throw new IOException($"{m.File} needs {size >> 30} GB and the disk has {free >> 30} GB free");
        var file = await CodeTools.Download($"https://huggingface.co/{m.Repo}/resolve/main/{m.File}", (string?)entry["lfs"]?["oid"], m.Key, say, ct, dir);
        File.Move(file, Path.Combine(dir, m.File), true);
    }
}
