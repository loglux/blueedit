<#
.SYNOPSIS
  Installs the free, local spelling and grammar checkers BlueEdit can use, from their official sources, with checksums.

.DESCRIPTION
  Everything goes into one folder (default %LOCALAPPDATA%\BlueEdit), laid out the way the editor looks for it:

      jre\              Java 21 runtime (Eclipse Temurin, adoptium.net)            for LanguageTool
      languagetool\     LanguageTool, free open-source edition (languagetool.org)
      llm\bin\          llama.cpp (github.com/ggml-org/llama.cpp)                  for language models
      llm\models\       .gguf models (huggingface.co)
      harper\           harper-ls, Harper's rules for English (github.com/Automattic/harper)
      litert\venv\      LiteRT-LM (pypi.org/project/litert-lm), to run Chrome's proofreader model without Chrome

  Edge Canary (for Edge's built-in proofreader) installs per user from Microsoft.
  Nothing needs administrator rights. A part already installed is skipped (-Force reinstalls it). Downloads resume if
  interrupted and are checked against the SHA-256 their publisher gives (LanguageTool publishes none: its archive is
  checked for the server jar instead); installers are checked for Microsoft's signature.

  See docs/proofing-setup.md for what each part is and how to do the same by hand.

.EXAMPLE
  .\install.ps1 -All                                  LanguageTool, llama.cpp, the recommended model, Edge Canary, Harper
.EXAMPLE
  .\install.ps1 -Harper                               Harper only (14 MB)
.EXAMPLE
  .\install.ps1 -ChromeModel                          LiteRT-LM for Chrome's model without Chrome (needs Python 3.10+)
.EXAMPLE
  .\install.ps1 -Llm -Models yandexgpt,gemma3-12b     llama.cpp and two models
.EXAMPLE
  .\install.ps1 -List                                 the models this script knows, and what is installed
#>
[CmdletBinding()]
param(
    [switch]$LanguageTool,
    [switch]$Llm,
    [string[]]$Models = @(),
    [switch]$EdgeCanary,
    [switch]$Harper,
    [switch]$ChromeModel,
    [switch]$All,
    [switch]$List,
    [switch]$Force,
    # llama.cpp build: cuda (NVIDIA), vulkan (any GPU), cpu; auto picks cuda when an NVIDIA driver is present
    [ValidateSet('auto', 'cuda', 'vulkan', 'cpu')][string]$Gpu = 'auto',
    # a llama.cpp release tag (bNNNNN); the default is the one BlueEdit was tested with; 'latest' takes the newest
    [string]$LlamaBuild = 'b11371',
    [string]$Root = (Join-Path $env:LOCALAPPDATA 'BlueEdit')
)
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

# Models tested with BlueEdit (tools/proofbench), best first. Q4_K_M quantization: the size/quality balance used.
$Catalog = [ordered]@{
    'yandexgpt'  = @{ Repo = 'yandex/YandexGPT-5-Lite-8B-instruct-GGUF';   File = 'YandexGPT-5-Lite-8B-instruct-Q4_K_M.gguf';   Note = 'best at Russian, ~6 GB of video memory (licence: Yandex''s own)' }
    'gemma3-12b' = @{ Repo = 'bartowski/google_gemma-3-12b-it-GGUF';       File = 'google_gemma-3-12b-it-Q4_K_M.gguf';          Note = 'strong in Russian and English, ~8 GB' }
    'qwen3-8b'   = @{ Repo = 'Qwen/Qwen3-8B-GGUF';                         File = 'Qwen3-8B-Q4_K_M.gguf';                       Note = '~6 GB' }
    'qwen3-14b'  = @{ Repo = 'bartowski/Qwen_Qwen3-14B-GGUF';              File = 'Qwen_Qwen3-14B-Q4_K_M.gguf';                 Note = '~10 GB, not better than Qwen3 8B here' }
    'gemma3-4b'  = @{ Repo = 'bartowski/google_gemma-3-4b-it-GGUF';        File = 'google_gemma-3-4b-it-Q4_K_M.gguf';           Note = 'for small GPUs, ~3 GB' }
    'phi4-mini'  = @{ Repo = 'bartowski/microsoft_Phi-4-mini-instruct-GGUF'; File = 'microsoft_Phi-4-mini-instruct-Q4_K_M.gguf'; Note = 'fastest, weakest, ~3 GB' }
}
$Recommended = 'yandexgpt'

$Downloads = Join-Path $Root 'downloads'
function Say($text, $color = 'Gray') { Write-Host $text -ForegroundColor $color }
function Step($text) { Write-Host "`n== $text" -ForegroundColor Cyan }

# Downloads $url to $Downloads\$name (resuming a partial file), checks the SHA-256 when given, returns the path.
function Get-File([string]$url, [string]$name, [string]$sha256) {
    New-Item -ItemType Directory -Force $Downloads | Out-Null
    $path = Join-Path $Downloads $name
    if ((Test-Path $path) -and $sha256 -and (Get-FileHash $path -Algorithm SHA256).Hash -eq $sha256) { Say "  already downloaded: $name"; return $path }
    $part = "$path.part"
    Say "  downloading $name"
    & curl.exe -L --fail --retry 3 -C - -o $part $url
    if ($LASTEXITCODE -ne 0) { throw "download failed ($LASTEXITCODE): $url" }
    if ($sha256) {
        $got = (Get-FileHash $part -Algorithm SHA256).Hash
        if ($got -ne $sha256.ToUpperInvariant()) { Remove-Item $part; throw "checksum mismatch for ${name}: expected $sha256, got $got" }
        Say "  SHA-256 OK"
    }
    Move-Item -Force $part $path
    return $path
}

# Unpacks a zip into $dest; when the zip has a single top folder, its contents go to $dest (not $dest\folder).
function Expand-Into([string]$zip, [string]$dest) {
    $tmp = Join-Path $Downloads ('unpack-' + [guid]::NewGuid().ToString('N'))
    Expand-Archive -LiteralPath $zip -DestinationPath $tmp
    $top = @(Get-ChildItem $tmp)
    $src = if ($top.Count -eq 1 -and $top[0].PSIsContainer) { $top[0].FullName } else { $tmp }
    if (Test-Path $dest) { Remove-Item -Recurse -Force $dest }
    New-Item -ItemType Directory -Force (Split-Path $dest) | Out-Null
    Move-Item $src $dest
    if (Test-Path $tmp) { Remove-Item -Recurse -Force $tmp }
}

function Assert-NotRunning([string]$exe) {
    $full = [IO.Path]::GetFullPath($exe)
    if (Get-Process -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $full }) { throw "$full is running (close BlueEdit first)" }
}

function Install-Java {
    Step 'Java 21 runtime (Eclipse Temurin)'
    $java = Join-Path $Root 'jre\bin\java.exe'
    if ((Test-Path $java) -and -not $Force) { Say "  installed: $java"; return }
    Assert-NotRunning $java
    $info = Invoke-RestMethod 'https://api.adoptium.net/v3/assets/latest/21/hotspot?architecture=x64&image_type=jre&os=windows&vendor=eclipse'
    $pkg = @($info)[0].binary.package
    $zip = Get-File $pkg.link $pkg.name $pkg.checksum
    Expand-Into $zip (Join-Path $Root 'jre')
    Say "  installed: $(@($info)[0].version.openjdk_version)" Green
}

function Install-LanguageTool {
    Install-Java
    Step 'LanguageTool (free open-source edition)'
    $jar = Join-Path $Root 'languagetool\languagetool-server.jar'
    if ((Test-Path $jar) -and -not $Force) { Say "  installed: $jar"; return }
    # languagetool.org publishes no checksum for this file; the archive is checked for the server jar below
    $zip = Get-File 'https://languagetool.org/download/LanguageTool-stable.zip' 'LanguageTool-stable.zip' $null
    Expand-Into $zip (Join-Path $Root 'languagetool')
    if (-not (Test-Path $jar)) { throw "languagetool-server.jar not found after unpacking $zip" }
    Say "  installed: $((Get-Content (Join-Path $Root 'languagetool\CHANGES.md') -TotalCount 5 | Select-String '^## ' | Select-Object -First 1).Line)" Green
}

# The video cards Vulkan sees, asked as llama.cpp asks (and the editor: VideoCards.cs). The loader alone proves
# nothing: Windows Sandbox has vulkan-1.dll from its host and no device under it. Type 4 (CPU) is software drawing.
function Get-VulkanGpus {
    Add-Type -TypeDefinition @'
using System; using System.Collections.Generic; using System.Runtime.InteropServices; using System.Text;
public static class BlueEditVulkan {
    [StructLayout(LayoutKind.Sequential)] struct CreateInfo { public int SType; public IntPtr Next; public int Flags; public IntPtr App;
        public int Layers; public IntPtr LayerNames; public int Extensions; public IntPtr ExtensionNames; }
    [DllImport("vulkan-1.dll")] static extern int vkCreateInstance(ref CreateInfo info, IntPtr alloc, out IntPtr instance);
    [DllImport("vulkan-1.dll")] static extern void vkDestroyInstance(IntPtr instance, IntPtr alloc);
    [DllImport("vulkan-1.dll")] static extern int vkEnumeratePhysicalDevices(IntPtr instance, ref uint count, IntPtr[] devices);
    [DllImport("vulkan-1.dll")] static extern void vkGetPhysicalDeviceProperties(IntPtr device, byte[] properties);
    public static string[] Gpus() {
        var gpus = new List<string>();
        var info = new CreateInfo(); info.SType = 1;
        IntPtr instance;
        try { if (vkCreateInstance(ref info, IntPtr.Zero, out instance) != 0) return gpus.ToArray(); }
        catch (DllNotFoundException) { return gpus.ToArray(); }
        try {
            uint count = 0;
            if (vkEnumeratePhysicalDevices(instance, ref count, null) != 0 || count == 0) return gpus.ToArray();
            var devices = new IntPtr[count];
            if (vkEnumeratePhysicalDevices(instance, ref count, devices) < 0) return gpus.ToArray();
            for (int i = 0; i < count; i++) {
                var p = new byte[1024];                       // VkPhysicalDeviceProperties: 824 bytes
                vkGetPhysicalDeviceProperties(devices[i], p);
                int type = BitConverter.ToInt32(p, 16), end = Array.IndexOf(p, (byte)0, 20, 256);
                if (type >= 1 && type <= 3) gpus.Add(Encoding.UTF8.GetString(p, 20, (end < 0 ? 276 : end) - 20));
            }
        } finally { vkDestroyInstance(instance, IntPtr.Zero); }
        return gpus.ToArray();
    }
}
'@
    [BlueEditVulkan]::Gpus()
}

function Get-GpuKind {
    if ($Gpu -ne 'auto') { return $Gpu }
    if (Get-Command nvidia-smi -ErrorAction SilentlyContinue) { return 'cuda' }
    $gpus = @(Get-VulkanGpus | Select-Object -Unique)          # a card may come twice, by two drivers (Intel here)
    if ($gpus) { Say "  no NVIDIA driver; Vulkan sees $($gpus -join ', '): the Vulkan build"; return 'vulkan' }
    Say '  no video card found (Vulkan sees none): the CPU build, slow with models; LanguageTool is unaffected'
    return 'cpu'
}

function Install-Llama {
    Step 'llama.cpp (runs .gguf language models)'
    $bin = Join-Path $Root 'llm\bin'
    $server = Join-Path $bin 'llama-server.exe'
    if ((Test-Path $server) -and -not $Force) { Say "  installed: $server"; return }
    Assert-NotRunning $server
    $kind = Get-GpuKind
    $rel = if ($LlamaBuild -eq 'latest') { @(Invoke-RestMethod 'https://api.github.com/repos/ggml-org/llama.cpp/releases?per_page=1')[0] }
           else { Invoke-RestMethod "https://api.github.com/repos/ggml-org/llama.cpp/releases/tags/$LlamaBuild" }
    $assets = @($rel.assets)
    $pattern = switch ($kind) {
        'cuda'   { if ([version]((& nvidia-smi --query-gpu=driver_version --format=csv,noheader | Select-Object -First 1).Trim()) -ge [version]'580.0') { 'cuda-13.4-x64' } else { 'cuda-12.4-x64' } }
        'vulkan' { 'vulkan-x64' }
        'cpu'    { 'cpu-x64' }
    }
    $main = $assets | Where-Object { $_.name -like "llama-*-bin-win-$pattern.zip" } | Select-Object -First 1
    if (-not $main) { throw "no llama.cpp asset for win-$pattern in $($rel.tag_name)" }
    $parts = @($main)
    if ($kind -eq 'cuda') { $parts += $assets | Where-Object { $_.name -eq "cudart-llama-bin-win-$pattern.zip" } }   # the CUDA runtime DLLs
    Say "  llama.cpp $($rel.tag_name), $pattern"
    $tmp = Join-Path $Downloads 'llm-bin-new'
    if (Test-Path $tmp) { Remove-Item -Recurse -Force $tmp }
    foreach ($a in $parts) {
        $zip = Get-File $a.browser_download_url $a.name ($a.digest -replace '^sha256:', '')
        Expand-Archive -LiteralPath $zip -DestinationPath $tmp -Force
    }
    if (Test-Path $bin) { Remove-Item -Recurse -Force $bin }
    New-Item -ItemType Directory -Force (Split-Path $bin) | Out-Null
    Move-Item $tmp $bin
    Say "  installed: $server" Green
}

function Install-Model([string]$key) {
    $m = $Catalog[$key]
    if (-not $m) { throw "unknown model '$key'; known: $($Catalog.Keys -join ', ')" }
    Step "model $key ($($m.Note))"
    $dir = Join-Path $Root 'llm\models'
    $dest = Join-Path $dir $m.File
    if ((Test-Path $dest) -and -not $Force) { Say "  installed: $dest"; return }
    $tree = Invoke-RestMethod "https://huggingface.co/api/models/$($m.Repo)/tree/main"
    $entry = @($tree) | Where-Object { $_.path -eq $m.File } | Select-Object -First 1
    if (-not $entry) { throw "$($m.File) not found in huggingface.co/$($m.Repo)" }
    $file = Get-File "https://huggingface.co/$($m.Repo)/resolve/main/$($m.File)" $m.File $entry.lfs.oid
    New-Item -ItemType Directory -Force $dir | Out-Null
    Move-Item -Force $file $dest
    Say "  installed: $dest ($([math]::Round((Get-Item $dest).Length / 1GB, 1)) GB)" Green
}

function Install-EdgeCanary {
    Step 'Microsoft Edge Canary (for Edge''s built-in proofreader)'
    $exe = Join-Path $env:LOCALAPPDATA 'Microsoft\Edge SxS\Application\msedge.exe'
    if ((Test-Path $exe) -and -not $Force) { Say "  installed: $exe $((Get-Item $exe).VersionInfo.ProductVersion)"; return }
    # Microsoft's own installer link (winget's Canary package failed its hash check when this was written)
    $setup = Get-File 'https://c2rsetup.officeapps.live.com/c2r/downloadEdge.aspx?platform=Default&source=EdgeInsiderPage&Channel=Canary&language=en&brand=M103' 'MicrosoftEdgeSetupCanary.exe' $null
    $sig = Get-AuthenticodeSignature $setup
    if ($sig.Status -ne 'Valid' -or $sig.SignerCertificate.Subject -notmatch 'O=Microsoft Corporation') { throw "the Edge Canary installer is not signed by Microsoft ($($sig.Status))" }
    Say '  signature OK (Microsoft Corporation); installing for this user'
    Start-Process $setup -ArgumentList '/silent', '/install' -Wait
    for ($i = 0; $i -lt 120 -and -not (Test-Path $exe); $i++) { Start-Sleep 1 }
    if (-not (Test-Path $exe)) { throw 'Edge Canary did not appear in ' + (Split-Path $exe) }
    Say "  installed: $exe $((Get-Item $exe).VersionInfo.ProductVersion)" Green
    Say '  its proofreader model (~1.2 GB) downloads on first use from BlueEdit (Tools > Grammar checking > Edge)'
}

# Harper's language server, the version BlueEdit was tested with; the SHA-256 is the one GitHub shows for the asset
$HarperVersion = 'v2.12.0'
$HarperSha256 = '455f82163054ac54a489b01e64ec62916dec5560165074f756633fe3f53d994e'
function Install-Harper {
    Step "Harper $HarperVersion (rules for English)"
    $dest = Join-Path $Root 'harper'
    $exe = Join-Path $dest 'harper-ls.exe'
    if ((Test-Path $exe) -and -not $Force) { Say "  installed: $exe"; return }
    Assert-NotRunning $exe
    $zip = Get-File "https://github.com/Automattic/harper/releases/download/$HarperVersion/harper-ls-x86_64-pc-windows-msvc.zip" "harper-ls-$HarperVersion-windows.zip" $HarperSha256
    Expand-Into $zip $dest
    if (-not (Test-Path $exe)) { throw "harper-ls.exe not found in $dest" }
    Say "  installed: $exe" Green
}

# Google's LiteRT-LM from PyPI in a virtual environment of its own: it runs the proofreader model Chrome downloads,
# taken from the user's own Chrome when BlueEdit starts it (the model is Google's: never downloaded or shipped here)
$LiteRtVersion = '0.17.1'
function Install-ChromeModel {
    Step "LiteRT-LM $LiteRtVersion (Chrome's proofreader model without Chrome)"
    $venv = Join-Path $Root 'litert\venv'
    $exe = Join-Path $venv 'Scripts\litert-lm.exe'
    if ((Test-Path $exe) -and -not $Force) { Say "  installed: $exe" } else {
        $py = Get-Command py, python -ErrorAction SilentlyContinue | Select-Object -First 1
        if (-not $py) { throw 'Python 3.10 or newer is needed (winget install Python.Python.3.12, or python.org)' }
        Say "  a virtual environment with $($py.Source)"
        & $py.Source -m venv $venv
        if ($LASTEXITCODE -ne 0) { throw 'could not make the virtual environment' }
        & (Join-Path $venv 'Scripts\python.exe') -m pip install --quiet "litert-lm==$LiteRtVersion"
        if ($LASTEXITCODE -ne 0 -or -not (Test-Path $exe)) { throw 'pip could not install litert-lm' }
        Say "  installed: $exe" Green
    }
    $models = Join-Path $env:LOCALAPPDATA 'Google\Chrome\User Data\OptGuideManifestModel'
    $model = Get-ChildItem $models -Recurse -Filter manifest.json -ErrorAction SilentlyContinue |
        Where-Object { (Get-Content $_.FullName -Raw) -match 'Proofreader' } | Select-Object -First 1
    if ($model) { Say "  Chrome's proofreader model is there: $($model.DirectoryName)" }
    else { Say '  no proofreader model in Chrome yet: turn its flags on and use it once (docs/proofing-setup.md, "Chrome")' Yellow }
}

function Show-List {
    Step "models (into $(Join-Path $Root 'llm\models'))"
    foreach ($k in $Catalog.Keys) {
        $m = $Catalog[$k]
        $have = if (Test-Path (Join-Path $Root "llm\models\$($m.File)")) { 'installed' } else { '' }
        '  {0,-11} {1,-10} {2}' -f $k, $have, $m.Note | Write-Host
    }
    Say "`n  recommended: $Recommended; see docs/proofing.md, 'Comparison: which is more accurate'"
}

if ($List) { Show-List; return }
if ($All) { $LanguageTool = $true; $Llm = $true; $EdgeCanary = $true; $Harper = $true; if ($Models.Count -eq 0) { $Models = @($Recommended) } }
if ($Llm -and $Models.Count -eq 0 -and -not (Get-ChildItem (Join-Path $Root 'llm\models') -Filter *.gguf -ErrorAction SilentlyContinue)) { $Models = @($Recommended) }
if (-not ($LanguageTool -or $Llm -or $Models.Count -or $EdgeCanary -or $Harper -or $ChromeModel)) { Get-Help $PSCommandPath -Detailed; return }

Say "BlueEdit proofing components into $Root"
if ($LanguageTool) { Install-LanguageTool }
if ($Llm -or $Models.Count) { Install-Llama }
foreach ($k in $Models) { Install-Model $k.ToLowerInvariant() }
if ($EdgeCanary) { Install-EdgeCanary }
if ($Harper) { Install-Harper }
if ($ChromeModel) { Install-ChromeModel }
Say "`nDone. Check with: .\check.ps1   Then in BlueEdit: Tools > Grammar checking (and Tools > Grammar model)." Green
