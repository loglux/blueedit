<#
.SYNOPSIS
  BlueEdit's standard operations, one entry point:  .\tasks.ps1 <task> [args]   (or  make <task>, see Makefile)

.DESCRIPTION
  .\tasks.ps1 help        lists the tasks. PowerShell 7 (pwsh), which also runs on Linux. CONTRIBUTING.md says when to use
  which; docs/ci.md, docs/proofing-setup.md and docs/lsp.md say more.
#>
param([Parameter(Position = 0)][string]$Task = 'help', [Parameter(Position = 1, ValueFromRemainingArguments = $true)][string[]]$Rest = @())
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
$testBuild = Join-Path ([IO.Path]::GetTempPath()) 'blueedit-testbuild'     # UI tests run a build outside publish\

function Fail($message) { Write-Host $message -ForegroundColor Red; exit 1 }
function Run([scriptblock]$command) { & $command; if ($LASTEXITCODE) { exit $LASTEXITCODE } }
function EditorOpenFromPublish { [bool](Get-Process be -ErrorAction SilentlyContinue | Where-Object { $_.Path -like (Join-Path $PSScriptRoot 'publish') + '*' }) }
function RunnerBusy { (gh run list --limit 5 --json status -q '[.[] | select(.status != "completed")] | length') -ne '0' }
# Builds and tests here while the runner has a job make its UI tests flaky (they wait fractions of a second for the
# editor; a busy CPU misses that) and its proofing tests fail outright. 'force' as the first argument goes ahead.
function NotWhileRunnerBusy {
    if ($Rest.Count -gt 0 -and $Rest[0] -eq 'force') { $script:Rest = @($Rest | Select-Object -Skip 1); return }
    if (RunnerBusy) { Fail 'the CI runner has a job now (tasks status): wait for it, or add "force" (docs/ci.md)' }
}

$tasks = [ordered]@{
    'help'           = @('this list', {
        foreach ($t in $tasks.Keys) { '  {0,-15} {1}' -f $t, $tasks[$t][0] }
        "`n  .\tasks.ps1 <task> [args]   or   make <task> [ARGS=...]"
        "  check, ui, test-all and sandbox wait for a free CI runner: add 'force' to go ahead anyway" })
    'check'          = @('build (analyzers on, warnings are errors) and unit tests, ~30 s', {
        NotWhileRunnerBusy
        Run { ./check.ps1 -Fast } })
    'test-all'       = @('everything incl. UI tests, ~5 min', {
        NotWhileRunnerBusy
        Run { ./check.ps1 -PublishDir $testBuild } })
    'ui'             = @('some UI tests on a fresh build:  ui test_lsp.py   or   ui test_markdown.py -k grammar', {
        NotWhileRunnerBusy
        Run { dotnet publish -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o $testBuild -nologo -v q }
        $env:BLUEEDIT_EXE = Join-Path $testBuild 'be.exe'
        Push-Location tests/ui; python -m pytest -q -p no:cacheprovider @Rest; $code = $LASTEXITCODE; Pop-Location; exit $code })
    'publish'        = @('rebuild publish\be.exe (the editor in use) - close it first', {
        if (EditorOpenFromPublish) { Fail 'be.exe is running from publish\: close it first' }
        Run { dotnet publish -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o publish -nologo -v q }
        'publish\be.exe ' + (Get-Item publish/be.exe).VersionInfo.ProductVersion })
    'publish-linux'  = @('build the Linux version into publish-linux\ and pack it as the release does (try it in WSL)', {
        Run { dotnet publish -c Release -r linux-x64 --self-contained -p:PublishSingleFile=true -o publish-linux -nologo -v q }
        $v = ([xml](Get-Content BlueEdit.csproj)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
        Run { python tools/ci/pack-linux.py publish-linux "publish-linux/blueedit-$v-linux-x64.tar.gz" } })
    'packages'       = @('the .deb and .rpm into publish-packages\, as the release makes them (nfpm; docs/tech/packages.md)', {
        Run { dotnet publish -c Release -r linux-x64 --self-contained -p:PublishSingleFile=true -o publish-linux -nologo -v q }
        Run { dotnet publish src/MathImages -c Release -r linux-x64 --self-contained false -o publish-packages/mathimages -nologo -v q }
        $v = ([xml](Get-Content BlueEdit.csproj)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
        Get-ChildItem publish-packages -Filter 'blueedit*' -ErrorAction SilentlyContinue | Remove-Item
        Run { python tools/packages/build.py $v publish-linux publish-packages/mathimages publish-packages } })
    'packages-test'  = @('the .deb and .rpm of publish-packages\ in clean Debian, Ubuntu, Fedora containers on BLUEEDIT_LINUX_HOST: installed, run, removed, ~3 min', {
        $hostName = $env:BLUEEDIT_LINUX_HOST
        if (-not $hostName) { Fail 'which Linux host with Docker? set BLUEEDIT_LINUX_HOST (user@host)' }
        $files = @(Get-ChildItem publish-packages -Filter 'blueedit*' | Where-Object { $_.Extension -in '.deb', '.rpm' })
        if ($files.Count -ne 2) { Fail 'no packages in publish-packages\: tasks packages first' }
        $dir = 'be-packages-test'
        ssh $hostName "rm -rf ~/$dir && mkdir ~/$dir"
        scp -q @($files.FullName) tools/packages/probe.py tools/packages/test.sh "${hostName}:$dir/"
        ssh $hostName "cd ~/$dir && sh test.sh"
        ssh $hostName "rm -rf ~/$dir" })
    'installer'      = @('the Windows installer into publish-installer\, as the release makes it (Inno Setup 6; docs/tech/installer.md)', {
        $build = Join-Path ([IO.Path]::GetTempPath()) 'blueedit-installer-build'
        Run { dotnet publish -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o $build -nologo -v q }
        ./tools/installer/build.ps1 -Source $build -Output publish-installer })
    'installer-sandbox' = @('the installer on a clean Windows: its pages captured, installed, checked, uninstalled (Windows Sandbox), ~5 min', {
        NotWhileRunnerBusy
        Run { ./tools/sandbox/start.ps1 -Installer } })
    'setup'          = @('install the spelling/grammar checkers and the language servers', {
        Run { ./tools/proofing-setup/install.ps1 -All }; Run { ./tools/lsp-setup/install.ps1 -All } })
    'setup-proofing' = @('LanguageTool, llama.cpp, the recommended model, Edge Canary (docs/proofing-setup.md)', {
        Run { ./tools/proofing-setup/install.ps1 -All } })
    'setup-lsp'      = @('pyright and csharp-ls, with Node.js / .NET SDK if missing (docs/lsp.md)', {
        Run { ./tools/lsp-setup/install.ps1 -All } })
    'setup-math'     = @('build the formula pictures module into %LOCALAPPDATA%\BlueEdit\mathimages (docs/formula-pictures.md)', {
        $dir = Join-Path $env:LOCALAPPDATA 'BlueEdit\mathimages'
        if (Get-Process be -ErrorAction SilentlyContinue | Where-Object { $_.Modules.FileName -like "$dir\*" }) { Fail 'an editor has the module loaded: close it first' }
        Run { dotnet publish src/MathImages -c Release -r win-x64 --self-contained false -o $dir -nologo -v q }
        "formula pictures module in $dir" })
    'sandbox'        = @('try the setup scripts on a clean Windows (Windows Sandbox), ~5 min', {
        NotWhileRunnerBusy
        Run { ./tools/sandbox/start.ps1 } })
    'newcomer'       = @('a new user on a clean Windows: the release zip, be.exe started, screens captured (Windows Sandbox), ~5 min', {
        NotWhileRunnerBusy
        Run { ./tools/sandbox/start.ps1 -Newcomer } })
    'newcomer-linux' = @('a new user on bare Linux: the release tar.gz in clean Debian, Ubuntu, Fedora containers on a Linux host with Docker (BLUEEDIT_LINUX_HOST), screens as text, ~6 min', {
        NotWhileRunnerBusy
        Run { ./tools/linux-newcomer/run.ps1 } })
    'images-sandbox' = @('the README pictures of images in Markdown: the Darwin note in Windows Terminal in Windows Sandbox, into docs/screenshots, ~5 min', {
        NotWhileRunnerBusy
        Run { ./tools/sandbox/start.ps1 -Images } })
    'dialogs'        = @('the dialogs in Russian on a clean Windows, each captured, nothing installed (Windows Sandbox), ~3 min', {
        NotWhileRunnerBusy
        Run { ./tools/sandbox/start.ps1 -Dialogs } })
    'lang-template'  = @('write lang/template/strings.json anew from lang/ru.json (a new language starts from it)', {
        $env:BLUEEDIT_WRITE_TEMPLATE = '1'
        try { Run { dotnet test tests/BlueEdit.Tests --filter LanguageTemplateTests -nologo -v q } }
        finally { Remove-Item Env:BLUEEDIT_WRITE_TEMPLATE } })
    'status'         = @('the runner, recent CI runs, open pull requests, what is installed', {
        gh api 'repos/{owner}/{repo}/actions/runners' -q '.runners[] | "runner: " + .name + " " + .status + (if .busy then " (busy)" else "" end)'
        gh run list --limit 5; gh pr list
        ./tools/proofing-setup/check.ps1; ./tools/lsp-setup/install.ps1 })
    'runner'         = @('the maintainer''s: start the self-hosted CI runner if it is not running (as the pre-push hook does)', {
        ./tools/ci/ensure-runner.ps1; Get-Process Runner.Listener -ErrorAction SilentlyContinue | Select-Object Name, Id, StartTime })
    'pr'             = @("push the current branch and open its pull request (title and text from its last commit)", {
        if ((git branch --show-current) -eq 'main') { Fail 'on main: make a branch first (git checkout -b <name>)' }
        # the last commit sums the branch up; "gh pr create --fill" takes the branch name as the title when there
        # are several commits ("proofing math"), and that title becomes the squash commit on main
        $title = git log -1 --format=%s; $body = (git log -1 --format=%b) -join "`n"
        Run { git push -u origin HEAD }; Run { gh pr create --title $title --body $body } })
    'merge'          = @("wait for the current branch's checks, squash-merge, back to main, rebuild publish if possible", {
        # right after a push GitHub may not list the check yet ("no checks reported"): give it a minute to appear
        for ($i = 0; $i -lt 30 -and -not (gh pr checks 2>$null); $i++) { Start-Sleep 2 }
        gh pr checks --watch --fail-fast; if ($LASTEXITCODE) { Fail 'the checks failed: gh pr checks' }
        Run { gh pr merge --squash --delete-branch }
        Run { git checkout main }; Run { git pull --ff-only }
        if (EditorOpenFromPublish) { 'publish\ not rebuilt: the editor is open (tasks publish later)' } else { & $tasks['publish'][1] } })
    'release'        = @('tag the version already in BlueEdit.csproj on main:  release 0.1.3', {
        $v = $Rest | Select-Object -First 1
        if (-not $v) { Fail 'release <x.y.z>' }
        if ((git branch --show-current) -ne 'main' -or (git status --porcelain)) { Fail 'on a clean main only (the version bump goes in by a pull request first)' }
        Run { git pull --ff-only }
        $inProject = ([xml](Get-Content BlueEdit.csproj)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
        if ($inProject -ne $v) { Fail "BlueEdit.csproj has $inProject, not $v" }
        Run { git tag "v$v" }; Run { git push origin "v$v" }
        "tag v$v pushed: the release workflow makes a draft with the zip (gh release view v$v); write the notes, then publish it" })
}

if (-not $tasks.Contains($Task)) { Fail "no task '$Task': .\tasks.ps1 help" }
& $tasks[$Task][1]
