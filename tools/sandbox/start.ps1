<#
.SYNOPSIS
  Tries the setup scripts on a clean Windows: starts Windows Sandbox with tools\ mapped read-only, runs inside.ps1
  there, waits for it and prints what it did. The sandbox is thrown away afterwards; nothing here is changed.

.DESCRIPTION
  Needs the Windows feature "Windows Sandbox" (Enable-WindowsOptionalFeature -Online -FeatureName
  Containers-DisposableClientVM, then a restart). The virtual GPU is off: on Insider build 26300 with an NVIDIA card
  the sandbox lost its connection within a minute with it on (Hyper-V-Worker event 33101, "unsupported Virtual PCI
  protocol version"); see docs/ci.md. The output stays in %TEMP%\blueedit-sandbox\out.

  -Images: the README pictures of images in Markdown (images.ps1). This build, its formula pictures module, the
  Darwin note (tools/screenshots/samples/darwin) and a portable Windows Terminal (downloaded once from its GitHub
  release, its SHA-256 checked) go in; the captures, cut to the window, are copied to docs/screenshots.

.EXAMPLE
  .\tools\sandbox\start.ps1
#>
param([int]$TimeoutMinutes = 30, [switch]$Newcomer, [switch]$Dialogs, [switch]$Installer, [switch]$Images)
$ErrorActionPreference = 'Stop'
$repoTools = Split-Path $PSScriptRoot
$work = Join-Path ([IO.Path]::GetTempPath()) 'blueedit-sandbox'
$out = Join-Path $work 'out'
if (Test-Path $out) { Remove-Item $out -Recurse -Force }
New-Item -ItemType Directory -Force $out | Out-Null

if (-not (Get-Command WindowsSandbox.exe -ErrorAction SilentlyContinue) -and -not (Get-Command wsb -ErrorAction SilentlyContinue)) {
    throw 'Windows Sandbox is not installed: Enable-WindowsOptionalFeature -Online -FeatureName Containers-DisposableClientVM (then restart)'
}
if (Get-Command wsb -ErrorAction SilentlyContinue) { if (wsb list) { throw 'a sandbox is already running (only one at a time): close it first' } }

$release = Join-Path $work 'release'
$inside = 'inside.ps1'
if ($Newcomer -or $Dialogs -or $Installer -or $Images) {
    # the zip as the release workflow makes it: be.exe and the setup scripts
    $inside = if ($Dialogs) { 'dialogs.ps1' } elseif ($Installer) { 'installer.ps1' } elseif ($Images) { 'images.ps1' } else { 'newcomer.ps1' }
    $repo = Split-Path $repoTools
    $build = Join-Path $work 'build'
    foreach ($d in $release, $build) { if (Test-Path $d) { Remove-Item $d -Recurse -Force } }
    dotnet publish (Join-Path $repo 'BlueEdit.csproj') -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o $build -nologo -v q
    if ($LASTEXITCODE) { throw 'dotnet publish failed' }
    $stage = Join-Path $build 'stage'
    New-Item -ItemType Directory (Join-Path $stage 'tools') | Out-Null
    Copy-Item (Join-Path $build 'be.exe') $stage
    Copy-Item -Recurse (Join-Path $repoTools 'proofing-setup'), (Join-Path $repoTools 'lsp-setup') (Join-Path $stage 'tools')
    New-Item -ItemType Directory $release | Out-Null
    Compress-Archive -Path (Join-Path $stage '*') -DestinationPath (Join-Path $release 'blueedit-dev-win-x64.zip')
    # the installer as the release workflow makes it (tools/installer)
    if ($Installer) { & (Join-Path $repoTools 'installer\build.ps1') -Source $build -Output $release | Out-Null }
    if ($Images) {
        Copy-Item (Join-Path $build 'be.exe') $release
        dotnet publish (Join-Path $repo 'src\MathImages') -c Release -r win-x64 --self-contained false -o (Join-Path $release 'mathimages') -nologo -v q
        if ($LASTEXITCODE) { throw 'the module did not build' }
        Copy-Item (Join-Path $repoTools 'screenshots\samples\darwin\*') $release
        '{ "fontSetup": "hint", "markdownView": "reading" }' | Set-Content (Join-Path $release 'settings.json') -Encoding ascii
        # Windows Terminal, portable (Windows Sandbox has none): its release zip, kept beside the work folder
        $wtVersion = '1.25.2733.0'
        $wtZip = Join-Path ([IO.Path]::GetTempPath()) "WindowsTerminal_${wtVersion}_x64.zip"
        if (-not (Test-Path $wtZip)) {
            Invoke-WebRequest "https://github.com/microsoft/terminal/releases/download/v$wtVersion/Microsoft.WindowsTerminal_${wtVersion}_x64.zip" -OutFile $wtZip
        }
        if ((Get-FileHash $wtZip -Algorithm SHA256).Hash -ne 'BF3EF2012F6C44D8340A4C58125ACC9498D19B580F9890DC043CDF831852E796') {
            Remove-Item $wtZip; throw "$wtZip is not the Windows Terminal $wtVersion zip (SHA-256): removed, run again"
        }
        $wtx = Join-Path $work 'wt'
        if (Test-Path $wtx) { Remove-Item $wtx -Recurse -Force }
        Expand-Archive $wtZip $wtx
        Move-Item (Join-Path $wtx "terminal-$wtVersion") (Join-Path $release 'wt')
        New-Item -ItemType File (Join-Path $release 'wt\.portable') | Out-Null       # its settings in its own folder
    }
} else { New-Item -ItemType Directory -Force $release | Out-Null }

$wsb = Join-Path $work 'test.wsb'
@"
<Configuration>
  <vGPU>Disable</vGPU>
  <Networking>Enable</Networking>
  <MemoryInMB>4096</MemoryInMB>
  <MappedFolders>
    <MappedFolder><HostFolder>$repoTools</HostFolder><SandboxFolder>C:\tools</SandboxFolder><ReadOnly>true</ReadOnly></MappedFolder>
    <MappedFolder><HostFolder>$release</HostFolder><SandboxFolder>C:\release</SandboxFolder><ReadOnly>true</ReadOnly></MappedFolder>
    <MappedFolder><HostFolder>$out</HostFolder><SandboxFolder>C:\out</SandboxFolder><ReadOnly>false</ReadOnly></MappedFolder>
  </MappedFolders>
  <LogonCommand><Command>powershell.exe -NoProfile -ExecutionPolicy Bypass -File C:\tools\sandbox\$inside</Command></LogonCommand>
</Configuration>
"@ | Set-Content $wsb -Encoding utf8

Write-Host "starting Windows Sandbox ($wsb); the setup takes a few minutes"
Start-Process $wsb
$sw = [Diagnostics.Stopwatch]::StartNew()
while (-not (Test-Path (Join-Path $out 'done.txt')) -and $sw.Elapsed.TotalMinutes -lt $TimeoutMinutes) { Start-Sleep 5 }
$transcript = Join-Path $out 'transcript.txt'
if (Test-Path $transcript) { Get-Content $transcript -Encoding utf8 }
if (-not (Test-Path (Join-Path $out 'done.txt'))) { Write-Host "not finished in $TimeoutMinutes min (see $out)" -ForegroundColor Red; exit 1 }
if (Get-Command wsb -ErrorAction SilentlyContinue) { foreach ($id in @(wsb list)) { wsb stop --id $id } }
Write-Host "`nfinished in $([int]$sw.Elapsed.TotalMinutes) min; the sandbox is closed and gone" -ForegroundColor Green
if ($Images) {
    $shots = Join-Path (Split-Path $repoTools) 'docs\screenshots'
    foreach ($name in 'images-text.png', 'images-diagram.png') { Copy-Item (Join-Path $out $name) $shots }
    Write-Host "images-text.png and images-diagram.png copied to $shots (look at them before committing)"
}
