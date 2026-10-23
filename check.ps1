<#
  One command for everything:  .\check.ps1          build (warnings = errors) + unit tests + publish + UI regression tests
                               .\check.ps1 -Fast    build + unit tests only (about 10 seconds)
  UI tests drive the real be.exe in a pseudo-console; they need:  pip install -r tests\ui\requirements.txt
#>
# -PublishDir elsewhere while publish\be.exe is in use; -UiWorkers: UI tests in parallel (1 = one by one)
param([switch]$Fast, [string]$PublishDir = 'publish', [int]$UiWorkers = 6)
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
function Step($name, [scriptblock]$body) {
    Write-Host "`n== $name" -ForegroundColor Cyan
    & $body
    if ($LASTEXITCODE -ne 0) { Write-Host "FAILED: $name" -ForegroundColor Red; exit 1 }
}

# only editors started from the folder being published are in the way (not one the user is working in elsewhere)
$dir = if ([IO.Path]::IsPathRooted($PublishDir)) { $PublishDir } else { Join-Path $PSScriptRoot $PublishDir }
$exe = [IO.Path]::GetFullPath((Join-Path $dir 'be.exe'))
Get-Process be -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $exe } | ForEach-Object {
    if ($PublishDir -eq 'publish') { Write-Host "be.exe is running from $PublishDir; close it or use -PublishDir <other folder>" -ForegroundColor Red; exit 1 }
}
$env:BLUEEDIT_EXE = $exe
Step 'build (analyzers on, warnings are errors)' { dotnet build --no-incremental -nologo -v q }
Step 'unit tests: editor'                         { dotnet test tests\BlueEdit.Tests -nologo -v q --logger "console;verbosity=minimal" }
Step 'unit tests: terminal layer (Tui)'            { dotnet test tests\Tui.Tests -nologo -v q --logger "console;verbosity=minimal" }
if (-not $Fast) {
    Step 'build TuiProbe (terminal-layer test app)' { dotnet build tools\TuiProbe -c Release -nologo -v q }
    Step 'publish single-file exe' { dotnet publish -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o $PublishDir -nologo -v q }
    # in parallel first; then the tests marked serial (timing, the real checkers) one by one, alone (tests\ui\conftest.py)
    $parallel = if ($UiWorkers -gt 1) { @('-n', $UiWorkers) } else { @() }
    Step 'UI regression tests (in parallel)' { Push-Location tests\ui; python -m pytest -q -p no:cacheprovider -m 'not serial' @parallel; $c = $LASTEXITCODE; Pop-Location; $global:LASTEXITCODE = $c }
    Step 'UI regression tests (one by one)'  { Push-Location tests\ui; python -m pytest -q -p no:cacheprovider -m serial; $c = $LASTEXITCODE; Pop-Location; $global:LASTEXITCODE = $c }
}
Write-Host "`nAll checks passed." -ForegroundColor Green
