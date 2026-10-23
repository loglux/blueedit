using System.Diagnostics;
using System.Formats.Tar;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json.Nodes;

/// <summary>
/// The programs code editing uses (language servers, formatters), installed by the editor into its own folder — no
/// administrator, no system packages, one folder to delete: on Windows %LOCALAPPDATA%\BlueEdit\tools, elsewhere
/// ~/.local/share/blueedit/tools (BLUEEDIT_TOOLS overrides it). From the projects' own sources: Node.js from
/// nodejs.org and ruff from its GitHub releases, both checked against the SHA-256 sums they publish; the .NET SDK from
/// Microsoft's release metadata, checked against its SHA-512; TypeScript 7 from the npm registry, checked against its
/// SHA-512; pyright with npm; csharp-ls with dotnet tool. Tools → Code tools, or
/// `be --install-tools`. Programs found elsewhere (on PATH) are used as they are and not installed again.
/// </summary>
static class CodeTools
{
    /// <summary>One installable tool: its name, what it is for, the command the editor runs, and what it needs.</summary>
    public sealed record Tool(string Name, string Purpose, string Command, string Needs);

    public static Tool[] All =>
    [
        // sizes measured after installing on Linux (on Windows: Node.js and pyright 120 MB, ruff 25 MB, typescript 27 MB as on Linux)
        new("pyright", "Python: errors, completion, rename…", "pyright-langserver", "with Node.js, 250 MB"),
        new("ruff", "Python: formatting (Ctrl+F12)", "ruff", "23 MB"),
        new("csharp-ls", "C#: errors, completion, fixes…", "csharp-ls", "with the .NET SDK, 720 MB"),
        new("typescript", "JS, TypeScript: errors, completion…", TypeScriptExe, "27 MB"),
    ];

    /// <summary>TypeScript 7's compiler, a native program with a language server in it (tsc --lsp --stdio): ours by its
    /// full path, since a "tsc" on PATH is usually TypeScript 5 from npm, which has no --lsp.</summary>
    public static string TypeScriptExe => Path.Combine(Root, "typescript", "package", "lib", OperatingSystem.IsWindows() ? "tsc.exe" : "tsc");

    public static string Root =>
        Environment.GetEnvironmentVariable("BLUEEDIT_TOOLS") is { Length: > 0 } r ? r
        : OperatingSystem.IsWindows() ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.DoNotVerify), "BlueEdit", "tools")
        : Path.Combine(Environment.GetEnvironmentVariable("XDG_DATA_HOME") is { Length: > 0 } x ? x
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share"), "blueedit", "tools");

    static string NodeDir => Path.Combine(Root, "node");
    static string NodeBin => OperatingSystem.IsWindows() ? NodeDir : Path.Combine(NodeDir, "bin");
    static string DotnetDir => Path.Combine(Root, "dotnet");

    /// <summary>Where the tools' programs are, for <see cref="LspClient.Resolve"/>: searched after PATH.</summary>
    public static IEnumerable<string> BinDirs =>
        [Path.Combine(Root, "bin"), Path.Combine(Root, "npm", "node_modules", ".bin"), Path.Combine(Root, "dotnet-tools"), NodeBin, DotnetDir];

    /// <summary>Gives a process started for a tool what it needs: our Node.js and .NET on PATH (pyright's shim runs
    /// "node"), and DOTNET_ROOT for csharp-ls when our .NET is there and none is set.</summary>
    public static void AddToEnvironment(ProcessStartInfo psi)
    {
        var extra = new[] { NodeBin, DotnetDir }.Where(Directory.Exists).ToList();
        if (extra.Count == 0) return;
        var path = psi.Environment.TryGetValue("PATH", out var p) ? p : Environment.GetEnvironmentVariable("PATH");
        psi.Environment["PATH"] = string.Join(Path.PathSeparator, extra.Append(path ?? ""));
        if (Directory.Exists(DotnetDir) && string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DOTNET_ROOT"))) psi.Environment["DOTNET_ROOT"] = DotnetDir;
    }

    /// <summary>Where the tool's program is: (path, true when it is ours), or null when it is not installed.</summary>
    public static (string Path, bool Ours)? Find(Tool t) =>
        LspClient.Resolve(t.Command) is { } p ? (p, p.StartsWith(Root, StringComparison.OrdinalIgnoreCase)) : null;

    // ---------- choosing what to download ----------

    static string Arch(bool rustStyle) => RuntimeInformation.OSArchitecture switch
    {
        Architecture.Arm64 => rustStyle ? "aarch64" : "arm64",
        _ => rustStyle ? "x86_64" : "x64",
    };

    /// <summary>The newest long-term-support Node.js in nodejs.org's index.json ("v24.11.0"); null when none.</summary>
    internal static string? LatestLtsNode(JsonNode? index) =>
        (index as JsonArray)?.OfType<JsonObject>().FirstOrDefault(v => v["lts"] is JsonValue lts && lts.GetValueKind() == System.Text.Json.JsonValueKind.String)?["version"]?.ToString();

    /// <summary>The Node.js archive for this system: tar.gz (no xz needed) or zip on Windows.</summary>
    internal static string NodeArchive(string version, bool windows) =>
        windows ? $"node-{version}-win-{Arch(false)}.zip" : $"node-{version}-linux-{Arch(false)}.tar.gz";

    /// <summary>ruff's release archive for this system.</summary>
    internal static string RuffArchive(bool windows) =>
        windows ? $"ruff-{Arch(true)}-pc-windows-msvc.zip" : $"ruff-{Arch(true)}-unknown-linux-gnu.tar.gz";

    /// <summary>The SHA-256 of <paramref name="file"/> in a "hash  name" list (SHASUMS256.txt, ruff's .sha256: "hash *name").</summary>
    internal static string? SumFor(string sums, string file) =>
        sums.Split('\n').Select(l => l.Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries))
            .Where(p => p.Length == 2 && p[1].TrimStart('*', ' ') == file).Select(p => p[0].ToLowerInvariant()).FirstOrDefault();

    // ---------- installing ----------

    internal static readonly HttpClient Http = CreateHttp();
    static HttpClient CreateHttp()
    {
        var h = Net.Client(TimeSpan.FromMinutes(10));
        h.DefaultRequestHeaders.UserAgent.ParseAdd("BlueEdit-code-tools");
        return h;
    }

    /// <summary>Installs (or updates) a tool and what it needs, reporting steps through <paramref name="say"/>; the
    /// reason when it failed, else null. Runs on the caller's thread: call it in the background.</summary>
    public static async Task<string?> Install(Tool t, Action<string> say, CancellationToken ct = default)
    {
        try
        {
            Directory.CreateDirectory(Root);
            switch (t.Name)
            {
                case "pyright":
                    // a Node.js with npm already here is used; else (or to update ours) Node.js goes into our folder
                    if (LspClient.Resolve("npm") == null || Directory.Exists(NodeDir)) await InstallNode(say, ct);
                    say("pyright: npm install pyright...");
                    await Run(Npm, ["install", "--prefix", Path.Combine(Root, "npm"), "pyright@latest", "--no-fund", "--no-audit", "--loglevel", "error"], ct);
                    break;
                case "typescript":
                    await InstallTypeScript(say, ct);
                    break;
                case "ruff":
                    await InstallRuff(say, ct);
                    break;
                case "csharp-ls":
                    var dotnet = SystemDotnetWithSdk() ?? await InstallDotnet(say, ct);
                    say("csharp-ls: dotnet tool install csharp-ls...");
                    var tools = Path.Combine(Root, "dotnet-tools");
                    bool has = File.Exists(Path.Combine(tools, OperatingSystem.IsWindows() ? "csharp-ls.exe" : "csharp-ls"));
                    await Run(dotnet, ["tool", has ? "update" : "install", "--tool-path", tools, "csharp-ls"], ct);
                    break;
            }
            return Find(t) == null ? $"{t.Command} is not there after installing (see {LogFile})" : null;
        }
        catch (Exception ex) when (ex is IOException or HttpRequestException or InvalidOperationException or InvalidDataException
                                   or UnauthorizedAccessException or System.ComponentModel.Win32Exception or TaskCanceledException)
        {
            Log("FAILED " + t.Name + ": " + ex);
            return ex.Message;
        }
    }

    /// <summary>Removes one tool of ours (and Node.js or .NET once nothing of ours needs them), or all with null.</summary>
    public static void Remove(Tool? t)
    {
        static void Del(string d) { if (Directory.Exists(d)) Directory.Delete(d, true); }
        switch (t?.Name)
        {
            case null: Del(Root); return;
            case "pyright": Del(Path.Combine(Root, "npm")); Del(NodeDir); break;
            case "typescript": Del(Path.Combine(Root, "typescript")); break;
            case "ruff": foreach (var f in new[] { "ruff", "ruff.exe" }) File.Delete(Path.Combine(Root, "bin", f)); break;
            case "csharp-ls": Del(Path.Combine(Root, "dotnet-tools")); Del(DotnetDir); break;
        }
    }

    // TypeScript 7 for this system: the npm registry's package @typescript/typescript-<os>-<arch> holds the native
    // compiler and its lib files; downloaded as the registry gives it, checked against the SHA-512 it publishes. No
    // Node.js: npm is not needed to unpack one package without dependencies.
    static async Task InstallTypeScript(Action<string> say, CancellationToken ct)
    {
        var name = TypeScriptPackage(OperatingSystem.IsWindows());
        say("TypeScript: which is the newest...");
        var meta = JsonNode.Parse(await Http.GetStringAsync($"https://registry.npmjs.org/{name.Replace("/", "%2F", StringComparison.Ordinal)}/latest", ct));
        var (version, url, sha512) = NpmTarball(meta) ?? throw new InvalidOperationException($"no tarball for {name} in the npm registry");
        var archive = await Download(url, sha512, $"TypeScript {version}", say, ct);
        say($"TypeScript {version}: unpacking...");
        var dir = Path.Combine(Root, "typescript");
        Unpack(archive, dir);                                               // a .tgz: package/lib/tsc(.exe) and lib.*.d.ts
        File.Delete(archive);
    }

    /// <summary>The npm package with TypeScript 7's native compiler for this system.</summary>
    internal static string TypeScriptPackage(bool windows) => $"@typescript/typescript-{(windows ? "win32" : "linux")}-{Arch(false)}";

    /// <summary>A package's version, tarball address and SHA-512 (hex) from the npm registry's answer for one version;
    /// null when it gives no sha512 integrity.</summary>
    internal static (string Version, string Url, string Sha512)? NpmTarball(JsonNode? meta) =>
        (string?)meta?["version"] is { } v && (string?)meta?["dist"]?["tarball"] is { } url
        && (string?)meta?["dist"]?["integrity"] is { } integrity && integrity.StartsWith("sha512-", StringComparison.Ordinal)
            ? (v, url, Convert.ToHexStringLower(Convert.FromBase64String(integrity["sha512-".Length..]))) : null;

    static string Npm => Path.Combine(NodeBin, OperatingSystem.IsWindows() ? "npm.cmd" : "npm") is var ours && File.Exists(ours) ? ours : "npm";

    static async Task InstallNode(Action<string> say, CancellationToken ct)
    {
        say("Node.js: which is the current LTS...");
        var index = JsonNode.Parse(await Http.GetStringAsync("https://nodejs.org/dist/index.json", ct));
        var v = LatestLtsNode(index) ?? throw new InvalidOperationException("no LTS version in nodejs.org/dist/index.json");
        var file = NodeArchive(v, OperatingSystem.IsWindows());
        var sums = await Http.GetStringAsync($"https://nodejs.org/dist/{v}/SHASUMS256.txt", ct);
        var archive = await Download($"https://nodejs.org/dist/{v}/{file}", SumFor(sums, file), $"Node.js {v}", say, ct);
        say($"Node.js {v}: unpacking...");
        var tmp = Path.Combine(Root, ".node-new");
        Unpack(archive, tmp);
        File.Delete(archive);
        if (Directory.Exists(NodeDir)) Directory.Delete(NodeDir, true);
        Directory.Move(Directory.GetDirectories(tmp).Single(), NodeDir);           // node-v24.x-linux-x64/ → node/
        Directory.Delete(tmp, true);
    }

    static async Task InstallRuff(Action<string> say, CancellationToken ct)
    {
        var file = RuffArchive(OperatingSystem.IsWindows());
        const string Base = "https://github.com/astral-sh/ruff/releases/latest/download/";
        var sum = SumFor(await Http.GetStringAsync(Base + file + ".sha256", ct), file);
        var archive = await Download(Base + file, sum, "ruff", say, ct);
        var tmp = Path.Combine(Root, ".ruff-new");
        Unpack(archive, tmp);
        File.Delete(archive);
        var exe = Directory.EnumerateFiles(tmp, OperatingSystem.IsWindows() ? "ruff.exe" : "ruff", SearchOption.AllDirectories).First();
        var bin = Path.Combine(Root, "bin");
        Directory.CreateDirectory(bin);
        File.Move(exe, Path.Combine(bin, Path.GetFileName(exe)), true);
        Directory.Delete(tmp, true);
    }

    // a .NET SDK already installed (dotnet on PATH with an SDK): csharp-ls is installed with it
    static string? SystemDotnetWithSdk()
    {
        if (LspClient.Resolve("dotnet") is not { } d || d.StartsWith(Root, StringComparison.OrdinalIgnoreCase)) return null;
        try
        {
            using var p = Process.Start(new ProcessStartInfo(d, "--list-sdks") { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true });
            var o = p?.StandardOutput.ReadToEnd() ?? "";
            p?.WaitForExit();
            return o.Trim().Length > 0 ? d : null;
        }
        catch (System.ComponentModel.Win32Exception) { return null; }
    }

    // The newest .NET 10 SDK from Microsoft's release metadata, which gives each archive's address and SHA-512: no
    // dotnet-install script (it needs bash, which a NAS with only busybox lacks — measured), and the archive is checked.
    static async Task<string> InstallDotnet(Action<string> say, CancellationToken ct)
    {
        bool win = OperatingSystem.IsWindows();
        say(".NET SDK: which is the newest 10.0...");
        var meta = JsonNode.Parse(await Http.GetStringAsync("https://builds.dotnet.microsoft.com/dotnet/release-metadata/10.0/releases.json", ct));
        var rid = (win ? "win-" : "linux-") + Arch(false);
        var (version, url, sha512) = SdkFor(meta, rid) ?? throw new InvalidOperationException($"no .NET SDK for {rid} in the release metadata");
        var archive = await Download(url, sha512, $".NET SDK {version}", say, ct);
        say($".NET SDK {version}: unpacking...");
        var tmp = Path.Combine(Root, ".dotnet-new");
        Unpack(archive, tmp);
        File.Delete(archive);
        if (Directory.Exists(DotnetDir)) Directory.Delete(DotnetDir, true);
        Directory.Move(tmp, DotnetDir);                                     // the archive has dotnet at its top
        return Path.Combine(DotnetDir, win ? "dotnet.exe" : "dotnet");
    }

    /// <summary>The newest SDK in a .NET release metadata file (releases.json) for a runtime id ("linux-x64"): its
    /// version, archive address (.tar.gz or .zip) and SHA-512; null when it lists none.</summary>
    internal static (string Version, string Url, string Sha512)? SdkFor(JsonNode? meta, string rid)
    {
        var latest = (string?)meta?["latest-sdk"];
        var sdk = (meta?["releases"] as JsonArray)?.Select(r => r?["sdk"]).FirstOrDefault(s => (string?)s?["version"] == latest);
        var file = (sdk?["files"] as JsonArray)?.FirstOrDefault(f => (string?)f?["rid"] == rid
            && ((string?)f?["name"])?.EndsWith(rid.StartsWith("win", StringComparison.Ordinal) ? ".zip" : ".tar.gz", StringComparison.Ordinal) == true);
        return latest != null && file?["url"] is { } u && (string?)file["hash"] is { } h ? (latest, (string)u!, h.ToLowerInvariant()) : null;
    }

    // downloads into Root (or dir), checking the hash the project publishes (SHA-256, or SHA-512 by its length); the
    // file's path. Without a hash only when the caller allows it (LanguageTool publishes none) and checks the contents.
    internal static async Task<string> Download(string url, string? sha256, string what, Action<string> say, CancellationToken ct,
                                                string? dir = null, bool noSumPublished = false)
    {
        if (sha256 == null && !noSumPublished) throw new InvalidDataException($"{what}: no checksum published for {url}");
        Directory.CreateDirectory(dir ?? Root);
        var file = Path.Combine(dir ?? Root, ".download-" + Path.GetFileName(new Uri(url).LocalPath));
        // a connection that breaks off is taken up where it stopped, a few times (languagetool.org's once ended after
        // 2.7 MB of 240, measured on the NAS; a model is 2–7 GB)
        long total = 0, got = 0, shown = -1;
        await using (var dst = File.Create(file))
        {
            var buf = new byte[1 << 16];
            for (int attempt = 0; ; attempt++)
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, url);
                if (got > 0) req.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(got, null);
                try
                {
                    using var resp = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
                    resp.EnsureSuccessStatusCode();
                    if (got > 0 && resp.StatusCode != System.Net.HttpStatusCode.PartialContent) { got = 0; dst.SetLength(0); }   // no ranges: again from the start
                    if (got == 0) total = resp.Content.Headers.ContentLength ?? 0;
                    long step = Math.Max(5, (total >> 20) / 50);                          // about fifty steps shown
                    await using var src = await resp.Content.ReadAsStreamAsync(ct);
                    for (int n; (n = await src.ReadAsync(buf, ct)) > 0;)
                    {
                        await dst.WriteAsync(buf.AsMemory(0, n), ct);
                        got += n;
                        long mb = got >> 20;
                        if (mb != shown && mb % step == 0) { shown = mb; say(total > 0 ? $"{what}: downloading, {mb} of {total >> 20} MB..." : $"{what}: downloading, {mb} MB..."); }
                    }
                    if (total == 0 || got >= total) break;
                    throw new IOException("the connection ended early");
                }
                catch (Exception ex) when (attempt < 4 && !ct.IsCancellationRequested && ex is IOException or HttpRequestException { StatusCode: null, HttpRequestError: not HttpRequestError.SecureConnectionError })
                {
                    Log($"download of {url} broke off at {got} bytes ({ex.Message}); again");
                    say($"{what}: the connection broke off, going on...");
                    await Task.Delay(TimeSpan.FromSeconds(2 << attempt), ct);
                }
            }
        }
        if (sha256 == null) { Log($"downloaded {url} (no checksum published)"); return file; }
        await using (var s = File.OpenRead(file))
        {
            var hash = Convert.ToHexStringLower(sha256.Length == 128 ? await SHA512.HashDataAsync(s, ct) : await SHA256.HashDataAsync(s, ct));
            if (hash != sha256) { s.Close(); File.Delete(file); throw new InvalidDataException($"{what}: the download's checksum {hash} is not the published {sha256}"); }
        }
        Log($"downloaded {url} ({sha256})");
        return file;
    }

    internal static void Unpack(string archive, string to)
    {
        if (Directory.Exists(to)) Directory.Delete(to, true);
        Directory.CreateDirectory(to);
        if (archive.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) ZipFile.ExtractToDirectory(archive, to);
        else
        {
            using var gz = new GZipStream(File.OpenRead(archive), CompressionMode.Decompress);
            TarFile.ExtractToDirectory(gz, to, overwriteFiles: true);           // file modes and links kept
        }
    }

    public static string LogFile => Path.Combine(Root, "install.log");
    internal static void Log(string line)
    {
        try { File.AppendAllText(LogFile, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {line}{Environment.NewLine}"); }
        catch (IOException) { }
    }

    // runs a program with our Node.js and .NET on PATH, its output into the log; throws when it fails
    static async Task Run(string command, IReadOnlyList<string> args, CancellationToken ct)
    {
        var psi = LspClient.StartInfo(command, args);
        psi.UseShellExecute = false; psi.CreateNoWindow = true;
        psi.RedirectStandardOutput = true; psi.RedirectStandardError = true;
        psi.WorkingDirectory = Root;
        psi.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        psi.Environment["DOTNET_NOLOGO"] = "1";
        Log("run " + command + " " + string.Join(" ", args));
        using var p = Process.Start(psi) ?? throw new InvalidOperationException(command + " did not start");
        var output = p.StandardOutput.ReadToEndAsync(ct);
        var errors = p.StandardError.ReadToEndAsync(ct);
        await p.WaitForExitAsync(ct);
        var err = await errors;
        Log((await output).TrimEnd() + (err.Length > 0 ? "\n" + err.TrimEnd() : ""));
        if (p.ExitCode != 0)
            throw new InvalidOperationException($"{Path.GetFileName(command)} failed (exit code {p.ExitCode}): "
                + (err.Split('\n').Select(l => l.Trim()).LastOrDefault(l => l.Length > 0) ?? "see " + LogFile));
    }
}
