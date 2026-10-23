<#
.SYNOPSIS
  Installs the language servers BlueEdit uses by default (docs/lsp.md), or shows what is installed.

.DESCRIPTION
  -Python   pyright (Microsoft, MIT licence): npm install -g pyright        needs Node.js (nodejs.org)
            and ruff (Astral, MIT licence), which formats Python (Ctrl+F12): winget install astral-sh.ruff
  -CSharp   csharp-ls (MIT licence): dotnet tool install -g csharp-ls       needs the .NET SDK
  -All      both
  Without a switch: lists each default server, whether it is found the way BlueEdit looks for it (PATH, and
  %USERPROFILE%\.dotnet\tools), and its version. The servers install for the current user. Running it again
  updates what is installed.

  When Node.js or the .NET SDK is missing, it offers to install it with winget (OpenJS.NodeJS.LTS,
  Microsoft.DotNet.SDK.10): these install for the whole computer, so Windows asks for administrator approval.
  -Yes installs them without asking first.
#>
[CmdletBinding()]
param([switch]$Python, [switch]$CSharp, [switch]$All, [switch]$Yes)
$ErrorActionPreference = 'Stop'
if ($All) { $Python = $true; $CSharp = $true }

# Reads PATH again from the registry, so what winget just installed is found in this window.
function Update-Path {
    $env:PATH = [Environment]::GetEnvironmentVariable('Path', 'Machine') + ';' + [Environment]::GetEnvironmentVariable('Path', 'User')
}

# Makes sure $present (a test) is true; if not, offers to install $wingetId ($what). False when it stays missing.
function Ensure([scriptblock]$present, $wingetId, $what) {
    if (& $present) { return $true }
    Write-Host "$what is not installed." -ForegroundColor Yellow
    if (-not (Get-Command winget -ErrorAction SilentlyContinue)) {
        Write-Host "  winget is not available either: install $what by hand (see docs/lsp.md), then run this again." -ForegroundColor Yellow
        return $false
    }
    if (-not $Yes) {
        $answer = Read-Host "  Install it now with winget ($wingetId)? Windows will ask for administrator approval [y/N]"
        if ($answer -notmatch '^(y|yes|д|да)$') { Write-Host '  Skipped.'; return $false }
    }
    winget install --id $wingetId --exact --silent --accept-package-agreements --accept-source-agreements
    Update-Path
    if (& $present) { Write-Host "  $what installed." -ForegroundColor Green; return $true }
    Write-Host "  $what was not found after installing (winget exit code $LASTEXITCODE); open a new PowerShell window and run this again." -ForegroundColor Yellow
    return $false
}

function Find($name) {
    $exts = $env:PATHEXT -split ';'
    $dirs = @($env:PATH -split ';') + (Join-Path $env:USERPROFILE '.dotnet\tools')
    foreach ($d in $dirs) { foreach ($e in $exts) { $p = Join-Path $d.Trim('"') "$name$e"; if ($d -and (Test-Path $p)) { return $p } } }
    return $null
}

$hasNode = { [bool](Get-Command npm -ErrorAction SilentlyContinue) }
# csharp-ls needs the SDK, not only the runtime: it loads projects with MSBuild
$hasSdk = { [bool](Get-Command dotnet -ErrorAction SilentlyContinue) -and [bool](dotnet --list-sdks 2>$null) }

if ($Python -and (Ensure $hasNode 'OpenJS.NodeJS.LTS' 'Node.js (needed by pyright)')) {
    Write-Host '== pyright (Python)' -ForegroundColor Cyan
    npm install -g pyright
    if ($LASTEXITCODE) { throw 'npm install -g pyright failed' }
}
if ($Python) {
    # pyright has no formatter: ruff formats Python. One program, no Python needed; winget installs it for the user
    Write-Host '== ruff (Python formatting)' -ForegroundColor Cyan
    if (-not (Get-Command winget -ErrorAction SilentlyContinue)) { Write-Host '  winget is not available: pip install --user ruff' -ForegroundColor Yellow }
    elseif (Find 'ruff') { winget upgrade --id astral-sh.ruff --exact --silent --accept-package-agreements --accept-source-agreements }
    else { winget install --id astral-sh.ruff --exact --silent --accept-package-agreements --accept-source-agreements; Update-Path }
}
if ($CSharp -and (Ensure $hasSdk 'Microsoft.DotNet.SDK.10' 'The .NET SDK (needed by csharp-ls)')) {
    Write-Host '== csharp-ls (C#)' -ForegroundColor Cyan
    if (dotnet tool list -g | Select-String '^csharp-ls\s') { dotnet tool update -g csharp-ls } else { dotnet tool install -g csharp-ls }
    if ($LASTEXITCODE) { throw 'dotnet tool install -g csharp-ls failed' }
}

Write-Host "`n== language servers BlueEdit would use" -ForegroundColor Cyan
foreach ($s in @(@{ Lang = 'Python'; Cmd = 'pyright-langserver'; Ver = { pyright --version } },
                 @{ Lang = 'C#'; Cmd = 'csharp-ls'; Ver = { csharp-ls --version } })) {
    $p = Find $s.Cmd
    if ($p) { Write-Host ('  [ok]  {0,-7} {1}  ({2})' -f $s.Lang, $p, ((& $s.Ver 2>$null) | Select-Object -First 1)) -ForegroundColor Green }
    else { Write-Host ('  [--]  {0,-7} {1} not found: install.ps1 -{2}' -f $s.Lang, $s.Cmd, $(if ($s.Lang -eq 'C#') { 'CSharp' } else { 'Python' })) -ForegroundColor DarkGray }
}
Write-Host "`n== formatters (Ctrl+F12) where the server has none" -ForegroundColor Cyan
$f = @('ruff', 'black') | ForEach-Object { $p = Find $_; if ($p) { $p } } | Select-Object -First 1
if ($f) { Write-Host ('  [ok]  Python  {0}  ({1})' -f $f, ((& $f --version 2>$null) | Select-Object -First 1)) -ForegroundColor Green }
else { Write-Host '  [--]  Python  ruff or black not found: install.ps1 -Python' -ForegroundColor DarkGray }
Write-Host "`n  Open a .py or .cs file in BlueEdit: the status line shows LSP <server>, errors are underlined."
