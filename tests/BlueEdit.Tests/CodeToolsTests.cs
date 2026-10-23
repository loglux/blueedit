using System.Diagnostics;
using Xunit;

// BLUEEDIT_TOOLS points the tools folder elsewhere for the whole process: alone, so that no other test looks for a
// program meanwhile and finds the fake ones here
[CollectionDefinition("code tools", DisableParallelization = true)]
public sealed class CodeToolsCollection;

/// <summary>The code tools the editor installs (CodeTools.cs): what is chosen to download, and how the editor finds
/// and starts what is in its folder. The real downloads were tried by hand (docs/tech/editing.md), not here.</summary>
[Collection("code tools")]
public sealed class CodeToolsTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "be-tools-" + Guid.NewGuid().ToString("N"));
    readonly string? _before = Environment.GetEnvironmentVariable("BLUEEDIT_TOOLS");

    public CodeToolsTests()
    {
        Directory.CreateDirectory(_dir);
        Environment.SetEnvironmentVariable("BLUEEDIT_TOOLS", _dir);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("BLUEEDIT_TOOLS", _before);
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }

    [Fact]
    public void The_newest_lts_node_and_the_archive_for_this_system_are_chosen()
    {
        var index = System.Text.Json.Nodes.JsonNode.Parse("""
            [ { "version": "v25.1.0", "lts": false }, { "version": "v24.21.0", "lts": "Krypton" }, { "version": "v22.20.0", "lts": "Jod" } ]
            """);
        Assert.Equal("v24.21.0", CodeTools.LatestLtsNode(index));
        Assert.Null(CodeTools.LatestLtsNode(System.Text.Json.Nodes.JsonNode.Parse("""[ { "version": "v25.1.0", "lts": false } ]""")));
        Assert.Matches(@"^node-v24\.21\.0-win-(x64|arm64)\.zip$", CodeTools.NodeArchive("v24.21.0", windows: true));
        Assert.Matches(@"^node-v24\.21\.0-linux-(x64|arm64)\.tar\.gz$", CodeTools.NodeArchive("v24.21.0", windows: false));
        Assert.Matches(@"^ruff-(x86_64|aarch64)-unknown-linux-gnu\.tar\.gz$", CodeTools.RuffArchive(windows: false));
    }

    [Fact]
    public void The_newest_dotnet_sdk_archive_and_its_sha512_come_from_the_release_metadata()
    {
        var meta = System.Text.Json.Nodes.JsonNode.Parse("""
            { "latest-sdk": "10.0.401", "releases": [
                { "sdk": { "version": "10.0.401", "files": [
                  { "name": "dotnet-sdk-linux-x64.pkg", "rid": "linux-x64", "url": "https://x/sdk.pkg", "hash": "AA" },
                  { "name": "dotnet-sdk-linux-x64.tar.gz", "rid": "linux-x64", "url": "https://x/sdk-linux.tar.gz", "hash": "BB" },
                  { "name": "dotnet-sdk-win-x64.zip", "rid": "win-x64", "url": "https://x/sdk-win.zip", "hash": "CC" } ] } },
                { "sdk": { "version": "10.0.300", "files": [] } } ] }
            """);
        Assert.Equal(("10.0.401", "https://x/sdk-linux.tar.gz", "bb"), CodeTools.SdkFor(meta, "linux-x64"));
        Assert.Equal(("10.0.401", "https://x/sdk-win.zip", "cc"), CodeTools.SdkFor(meta, "win-x64"));
        Assert.Null(CodeTools.SdkFor(meta, "linux-arm64"));
    }

    [Fact]
    public void TypeScript_comes_from_the_npm_registry_with_its_sha512()
    {
        Assert.Matches(@"^@typescript/typescript-win32-(x64|arm64)$", CodeTools.TypeScriptPackage(windows: true));
        Assert.Matches(@"^@typescript/typescript-linux-(x64|arm64)$", CodeTools.TypeScriptPackage(windows: false));
        var meta = System.Text.Json.Nodes.JsonNode.Parse("""
            { "name": "@typescript/typescript-linux-x64", "version": "7.0.2",
              "dist": { "tarball": "https://registry.npmjs.org/x.tgz", "integrity": "sha512-AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8gISIjJCUmJygpKissLS4vMDEyMzQ1Njc4OTo7PD0+Pw==" } }
            """);
        var (version, url, sha512) = CodeTools.NpmTarball(meta)!.Value;
        Assert.Equal(("7.0.2", "https://registry.npmjs.org/x.tgz"), (version, url));
        Assert.Equal(string.Concat(Enumerable.Range(0, 64).Select(i => i.ToString("x2", System.Globalization.CultureInfo.InvariantCulture))), sha512);
        Assert.Null(CodeTools.NpmTarball(System.Text.Json.Nodes.JsonNode.Parse("""{ "version": "1", "dist": { "tarball": "u", "integrity": "sha1-AAAA" } }""")));
    }

    [Fact]
    public void A_published_sum_is_found_for_its_file_only()
    {
        const string shasums = "aaa111  node-v24.21.0-darwin-x64.tar.gz\nBBB222  node-v24.21.0-linux-x64.tar.gz\n";
        Assert.Equal("bbb222", CodeTools.SumFor(shasums, "node-v24.21.0-linux-x64.tar.gz"));
        Assert.Null(CodeTools.SumFor(shasums, "node-v24.21.0-win-x64.zip"));
        Assert.Equal("ccc333", CodeTools.SumFor("ccc333 *ruff-x86_64-pc-windows-msvc.zip\n", "ruff-x86_64-pc-windows-msvc.zip"));   // ruff's form
    }

    [Fact]
    public void A_cmd_shim_in_a_folder_with_a_space_runs_with_its_arguments()
    {
        if (!OperatingSystem.IsWindows()) return;                       // .cmd shims are Windows' (npm in C:\Program Files\nodejs)
        var dir = Path.Combine(_dir, "Program Files", "nodejs");
        Directory.CreateDirectory(dir);
        var shim = Path.Combine(dir, "npm.cmd");
        File.WriteAllText(shim, "@echo [%~1] [%~2]\r\n");
        var psi = LspClient.StartInfo(shim, ["install", "a b"]);
        psi.UseShellExecute = false; psi.RedirectStandardOutput = true;
        using var p = Process.Start(psi)!;
        var output = p.StandardOutput.ReadToEnd().Trim();
        p.WaitForExit();
        Assert.Equal("[install] [a b]", output);
        Assert.Equal("\"a&b\"", LspClient.CmdQuote("a&b"));
        Assert.Equal("--stdio", LspClient.CmdQuote("--stdio"));
    }

    [Fact]
    public void What_is_in_the_tools_folder_is_found_and_started_with_its_node_and_dotnet()
    {
        Assert.StartsWith(_dir, CodeTools.Root, StringComparison.Ordinal);
        var bin = Path.Combine(_dir, "bin");
        Directory.CreateDirectory(bin);
        var fake = Path.Combine(bin, OperatingSystem.IsWindows() ? "be-fake-tool.exe" : "be-fake-tool");
        File.WriteAllText(fake, "");
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(fake, UnixFileMode.UserRead | UnixFileMode.UserExecute);
        Assert.Equal(fake, LspClient.Resolve("be-fake-tool"), ignoreCase: true);         // PATHEXT spells it .EXE

        var node = Path.Combine(_dir, "node", OperatingSystem.IsWindows() ? "" : "bin");
        Directory.CreateDirectory(node);
        Directory.CreateDirectory(Path.Combine(_dir, "dotnet"));
        var psi = new ProcessStartInfo("x");
        CodeTools.AddToEnvironment(psi);
        Assert.StartsWith(node.TrimEnd(Path.DirectorySeparatorChar) + Path.PathSeparator, psi.Environment["PATH"], StringComparison.Ordinal);
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DOTNET_ROOT")))
            Assert.Equal(Path.Combine(_dir, "dotnet"), psi.Environment["DOTNET_ROOT"]);
    }
}
