<#
.SYNOPSIS
  Builds BlueEdit's Windows installer (blueedit.iss, Inno Setup 6) from a published be.exe: tasks installer and the
  release workflow use it. docs/tech/installer.md

.EXAMPLE
  .\tools\installer\build.ps1 -Source publish -Output publish-installer
#>
param([Parameter(Mandatory)][string]$Source, [Parameter(Mandatory)][string]$Output, [string]$Version, [string]$MathImages)
$ErrorActionPreference = 'Stop'
$repo = Split-Path (Split-Path $PSScriptRoot)
if (-not $Version) { $Version = ([xml](Get-Content (Join-Path $repo 'BlueEdit.csproj'))).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1 }
$iscc = @((Get-Command ISCC.exe -ErrorAction SilentlyContinue).Source,
    (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'),
    (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
    (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe')) | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1
if (-not $iscc) { throw 'Inno Setup 6 is not installed: winget install JRSoftware.InnoSetup (jrsoftware.org), or choco install innosetup' }
$Source = (Resolve-Path $Source).Path
# the formula pictures module (src/MathImages, docs/formula-pictures.md), put in by the installer: built here unless given
if (-not $MathImages) {
    $MathImages = Join-Path ([IO.Path]::GetTempPath()) 'blueedit-installer-mathimages'
    if (Test-Path $MathImages) { Remove-Item -Recurse -Force $MathImages }
    dotnet publish (Join-Path $repo 'src/MathImages') -c Release -r win-x64 --self-contained false -o $MathImages -nologo -v q | Out-Host
    if ($LASTEXITCODE) { throw "the mathimages module did not build ($LASTEXITCODE)" }
}
$MathImages = (Resolve-Path $MathImages).Path
New-Item -ItemType Directory -Force $Output | Out-Null
$Output = (Resolve-Path $Output).Path
& $iscc /Q "/DAppVersion=$Version" "/DSource=$Source" "/DMathImages=$MathImages" "/DOutput=$Output" (Join-Path $PSScriptRoot 'blueedit.iss')
if ($LASTEXITCODE) { throw "ISCC failed ($LASTEXITCODE)" }
Join-Path $Output "blueedit-$Version-win-x64-setup.exe"
