using Xunit;

/// <summary>Which llama.cpp build the editor installs (ProofingTools), checked against the release's real file names.</summary>
public sealed class ProofingToolsTests
{
    // the x64 archives of llama.cpp b11371 for Windows and Linux, as its GitHub release lists them (19 October 2026)
    static readonly string[] Released =
    [
        "cudart-llama-bin-win-cuda-12.4-x64.zip", "cudart-llama-bin-win-cuda-13.4-x64.zip",
        "llama-b11371-bin-win-cpu-x64.zip", "llama-b11371-bin-win-cuda-12.4-x64.zip", "llama-b11371-bin-win-cuda-13.4-x64.zip",
        "llama-b11371-bin-win-vulkan-x64.zip",
        "cudart-llama-b11371-bin-ubuntu-cuda-12.8-x64.tar.gz", "cudart-llama-b11371-bin-ubuntu-cuda-13.4-x64.tar.gz",
        "llama-b11371-bin-ubuntu-cuda-12.8-x64.tar.gz", "llama-b11371-bin-ubuntu-cuda-13.4-x64.tar.gz",
        "llama-b11371-bin-ubuntu-vulkan-x64.tar.gz", "llama-b11371-bin-ubuntu-x64.tar.gz",
    ];

    [Theory]
    [InlineData(true, 581, false, "cuda-13.4")]
    [InlineData(true, 560, false, "cuda-12.4")]
    [InlineData(true, null, true, "vulkan")]                // Windows, AMD or Intel
    [InlineData(true, null, false, "cpu")]                  // Windows Sandbox: a Vulkan loader, no device under it
    [InlineData(true, null, null, "vulkan")]                // Vulkan not asked: its build also runs on the processor
    [InlineData(false, 581, null, "cuda-13.4")]
    [InlineData(false, 560, null, "cuda-12.8")]
    [InlineData(false, null, true, "vulkan")]
    [InlineData(false, null, false, "cpu")]                  // Linux without a video card: the NAS
    [InlineData(false, null, null, "cpu")]
    public void The_build_follows_the_video_card(bool windows, int? driver, bool? vulkanGpu, string kind)
    {
        Assert.Equal(kind, ProofingTools.LlamaKind(windows, driver, vulkanGpu));
        var assets = ProofingTools.LlamaAssets(windows, kind).ToList();
        Assert.NotEmpty(assets);
        Assert.All(assets, a => Assert.Contains(a, Released));
        Assert.Equal(kind.StartsWith("cuda", StringComparison.Ordinal) ? 2 : 1, assets.Count);   // CUDA: its runtime too
    }

    // be --vulkan-devices prints them and ProofingTools reads them back; a device of type 4 is the processor
    [Fact]
    public void Vulkan_devices_go_through_their_printed_form_and_only_a_gpu_counts()
    {
        List<VideoCards.Device> devices = [new("NVIDIA GeForce RTX 4070", 2), new("llvmpipe (LLVM 19.1.7, 256 bits)", 4)];
        Assert.Equal(devices, VideoCards.Parse(VideoCards.Print(devices)));
        Assert.Equal(new[] { true, false }, devices.Select(d => d.IsGpu).ToArray());
        Assert.Empty(VideoCards.Parse(VideoCards.Print([]))!);
        Assert.Null(VideoCards.Parse(""));                                       // not be: another program, or nothing
        Assert.Null(VideoCards.Parse("Microsoft (R) Test Execution Command Line Tool"));
    }

    // asked for real where the tests run: no throw and sensible answers, whatever the machine has (the Windows runner
    // has an NVIDIA card, the Linux one in Docker none)
    [Fact]
    public void Vulkan_is_asked_without_failing()
    {
        Assert.All(VideoCards.Vulkan(), d => { Assert.NotEmpty(d.Name); Assert.InRange(d.Type, 0, 4); });
    }

    // a bare Linux (Net.cs): without libssl https is refused before .NET would end the process; a failed TLS connection
    // says why and what to install, and stays a SecureConnectionError, which Download does not retry
    [Fact]
    public void Https_without_openssl_is_refused_with_what_to_install()
    {
        Assert.Contains("sudo apt install openssl", Net.Refusal(new Uri("https://github.com/x"), noOpenSsl: true), StringComparison.Ordinal);
        Assert.Null(Net.Refusal(new Uri("http://127.0.0.1:8081/v2/check"), noOpenSsl: true));      // a local server needs none
        Assert.Null(Net.Refusal(new Uri("https://github.com/x"), noOpenSsl: false));
    }

    [Fact]
    public void A_certificate_that_cannot_be_checked_is_said_in_words()
    {
        var tls = new HttpRequestException(HttpRequestError.SecureConnectionError, "The SSL connection could not be established, see inner exception.",
            new System.Security.Authentication.AuthenticationException("The remote certificate is invalid because of errors in the certificate chain: UntrustedRoot"));
        var e = Net.Explained(tls, new Uri("https://github.com/x"), linux: true);
        Assert.Equal(HttpRequestError.SecureConnectionError, e.HttpRequestError);
        Assert.Contains("github.com: the secure connection failed: The remote certificate is invalid", e.Message, StringComparison.Ordinal);
        Assert.Contains("sudo apt install ca-certificates", e.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("apt", Net.Explained(tls, new Uri("https://github.com/x"), linux: false).Message, StringComparison.Ordinal);
        var refused = new HttpRequestException("refused", null, null);
        Assert.Same(refused, Net.Explained(refused, new Uri("https://github.com/x"), linux: true));
    }

    [Fact]
    public void Every_model_has_its_size_and_licence_and_a_key_of_its_own()
    {
        Assert.Equal(ProofingTools.Models.Length, ProofingTools.Models.Select(m => m.Key).Distinct().Count());
        Assert.All(ProofingTools.Models, m =>
        {
            Assert.EndsWith(".gguf", m.File, StringComparison.Ordinal);
            Assert.NotEmpty(m.Licence);
            Assert.EndsWith("GB", m.Size, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Every_dictionary_has_a_language_of_its_own_and_both_sums()
    {
        var d = ProofingTools.Dictionaries;
        Assert.Equal(d.Length, d.Select(x => x.Tag).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(d, x =>
        {
            Assert.Matches("^[a-z]{2}-[A-Z]{2}$", x.Tag);
            Assert.Matches("^[0-9a-f]{64}$", x.DicSha256);
            Assert.Matches("^[0-9a-f]{64}$", x.AffSha256);
            Assert.EndsWith("MB", x.Size, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void A_dictionary_counts_as_installed_only_with_its_aff()
    {
        var dir = Directory.CreateTempSubdirectory("be-dict").FullName;
        try
        {
            var h = new ProofingTools.Homes("", "", "", dir);
            var ru = ProofingTools.Dictionaries.First(x => x.Tag == "ru-RU");
            File.WriteAllText(Path.Combine(dir, "ru_RU.dic"), "1\nслово\n");
            Assert.False(ProofingTools.HasDictionary(h, ru));
            File.WriteAllText(Path.Combine(dir, "ru_RU.aff"), "SET UTF-8\n");
            Assert.True(ProofingTools.HasDictionary(h, ru));
        }
        finally { Directory.Delete(dir, true); }
    }

    // the Visual C++ installer carries its packages as cabinets after its code: found by "MSCF", four zero bytes and a
    // size that fits; a stray "MSCF" or a size past the end is not one
    [Fact]
    public void Cabinets_are_found_inside_an_installer()
    {
        static byte[] Cab(int size) { var c = new byte[size]; "MSCF"u8.CopyTo(c); BitConverter.GetBytes((uint)size).CopyTo(c, 8); return c; }
        var tooLong = Cab(40);
        BitConverter.GetBytes(4000u).CopyTo(tooLong, 8);
        byte[] file = [.. "MZ code MSCF text "u8, .. Cab(30), .. new byte[7], .. Cab(50), .. tooLong];
        int first = "MZ code MSCF text "u8.Length;
        Assert.Equal(new[] { first, first + 37 }, ProofingTools.CabinetsIn(file).ToArray());
    }

    // what Get-AuthenticodeSignature gave: the installer on this PC, the DLLs in Windows Sandbox (Windows' own catalog)
    [Theory]
    [InlineData("Valid|CN=Microsoft Corporation, O=Microsoft Corporation, L=Redmond, S=Washington, C=US", true)]
    [InlineData("Valid|CN=Microsoft Windows, O=Microsoft Corporation, L=Redmond, S=Washington, C=US", true)]
    [InlineData("HashMismatch|CN=Microsoft Corporation, O=Microsoft Corporation, L=Redmond, S=Washington, C=US", false)]
    [InlineData("Valid|CN=Microsoft Corporation, O=Someone Else, L=Redmond, C=US", false)]
    [InlineData("NotSigned|", false)]
    public void Only_a_valid_signature_of_Microsoft_is_taken(string line, bool taken) =>
        Assert.Equal(taken, ProofingTools.MicrosoftSigned(line));

    // Tools > Set up grammar checking > Remove: what is in BlueEdit's folders is listed with its size and deleted
    [Fact]
    public async Task Installed_parts_are_listed_with_their_size_and_removed()
    {
        var dir = Path.Combine(Path.GetTempPath(), "be-parts-" + Guid.NewGuid().ToString("N"));
        var h = new ProofingTools.Homes(Path.Combine(dir, "lt"), Path.Combine(dir, "llm"), Path.Combine(dir, "harper"), Path.Combine(dir, "hunspell"));
        void Put(string path, int size) { Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllBytes(path, new byte[size]); }
        try
        {
            Put(Path.Combine(h.Llm, "bin", "llama-server.exe"), 100);
            Put(Path.Combine(h.Llm, "bin", "ggml.dll"), 50);
            Put(Path.Combine(h.Llm, "models", "Qwen3-8B-Q4_K_M.gguf"), 1000);
            Put(Path.Combine(h.Llm, "models", "notes.txt"), 5);                    // not a model: not listed
            Put(Path.Combine(h.Harper, "harper-ls.exe"), 60);
            Put(Path.Combine(h.Hunspell, "ru_RU.dic"), 30);
            Put(Path.Combine(h.Hunspell, "ru_RU.aff"), 3);
            Put(Path.Combine(h.Hunspell, "xx_XX.dic"), 7);                         // not one of ours: not listed
            var parts = ProofingTools.InstalledParts(h);
            Assert.Equal(new[] { "llama", "Qwen3-8B-Q4_K_M.gguf", "harper", "ru-RU" }, parts.Select(p => p.Key).ToArray());
            Assert.Equal(new long[] { 150, 1000, 60, 33 }, parts.Select(p => p.Bytes).ToArray());
            Assert.DoesNotContain(parts, p => p.Key == "languagetool");            // nothing of LanguageTool there
            foreach (var p in parts.Where(p => p.Key is "llama" or "ru-RU")) Assert.Null(await ProofingTools.Remove(p));
            Assert.False(Directory.Exists(Path.Combine(h.Llm, "bin")));
            Assert.False(File.Exists(Path.Combine(h.Hunspell, "ru_RU.dic")) || File.Exists(Path.Combine(h.Hunspell, "ru_RU.aff")));
            Assert.True(File.Exists(Path.Combine(h.Hunspell, "xx_XX.dic")));
            Assert.Equal(new[] { "Qwen3-8B-Q4_K_M.gguf", "harper" }, ProofingTools.InstalledParts(h).Select(p => p.Key).ToArray());
        }
        finally { Directory.Delete(dir, true); }
    }
}
