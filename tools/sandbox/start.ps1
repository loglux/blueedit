<#
.SYNOPSIS
  Tries the setup scripts on a clean Windows: starts Windows Sandbox with tools\ mapped read-only, runs inside.ps1
  there, waits for it and prints what it did. The sandbox is thrown away afterwards; nothing here is changed.

.DESCRIPTION
  Needs the Windows feature "Windows Sandbox" (Enable-WindowsOptionalFeature -Online -FeatureName
  Containers-DisposableClientVM, then a restart). The virtual GPU is off: on Insider build 26300 with an NVIDIA card
  the sandbox lost its connection within a minute with it on (Hyper-V-Worker event 33101, "unsupported Virtual PCI
  protocol version"); see docs/ci.md. The output stays in %TEMP%\blueedit-sandbox\out.

.EXAMPLE
  .\tools\sandbox\start.ps1
#>
param([int]$TimeoutMinutes = 30, [switch]$Newcomer, [switch]$Dialogs)
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
if ($Newcomer -or $Dialogs) {
    # the zip as the release workflow makes it: be.exe and the setup scripts
    $inside = if ($Dialogs) { 'dialogs.ps1' } else { 'newcomer.ps1' }
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
