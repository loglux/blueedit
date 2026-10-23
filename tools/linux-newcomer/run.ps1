# A new user's first minutes with the Linux release on bare systems, on a Linux host with Docker (-HostName, or the
# environment variable BLUEEDIT_LINUX_HOST: user@host, reached by ssh with a key): the release tar.gz from publish-linux\, walk.py and the UI harness are copied to the host, run there, and
# the screens come back as text into %TEMP%\blueedit-linux-newcomer. By default in clean containers; with -Vm in
# virtual machines of minimal servers instead (vm.sh: Ubuntu 24.04 minimal, Debian 12 genericcloud). See docs/ci.md.
param(
    [string]$HostName = $env:BLUEEDIT_LINUX_HOST,
    [string[]]$Images = @('debian:12', 'ubuntu:24.04', 'fedora:41'),
    [switch]$Vm,
    [switch]$LanguageTool,
    [string]$Lang = ''          # the system's language, e.g. de_DE.UTF-8 (walk.py --lang); none by default
)
$ErrorActionPreference = 'Stop'
if (-not $HostName) { throw 'which Linux host with Docker? -HostName user@host, or set BLUEEDIT_LINUX_HOST' }
$repo = Resolve-Path (Join-Path $PSScriptRoot '..\..')
$release = Get-ChildItem (Join-Path $repo 'publish-linux') -Filter 'blueedit-*-linux-x64.tar.gz' |
    Sort-Object LastWriteTime -Descending | Select-Object -First 1
if (-not $release) { throw 'no release tar.gz in publish-linux\: run tasks publish-linux first' }
"release: $($release.Name), $($release.LastWriteTime)"
$out = Join-Path $env:TEMP 'blueedit-linux-newcomer'
if (Test-Path $out) { Remove-Item $out -Recurse -Force }

$dir = '/tmp/be-newcomer'
ssh $HostName "rm -rf $dir && mkdir -p $dir"
scp -q $release.FullName (Join-Path $PSScriptRoot 'walk.py') (Join-Path $PSScriptRoot 'vm.sh') (Join-Path $repo 'tests\ui\harness.py') "${HostName}:$dir/"
if ($Vm) {
    ssh $HostName "sh $dir/vm.sh start"
    $Images = @('ssh:ubuntu@127.0.0.1:2201', 'ssh:debian@127.0.0.1:2202')
}
# the harness's packages in a virtual environment of their own, kept between runs; the containers get nothing
$venv = '~/.cache/be-newcomer-venv'
$lt = if ($LanguageTool) { '--languagetool' } else { '' }
if ($Lang) { $lt += " --lang=$Lang" }
try {
    ssh $HostName "test -x $venv/bin/python || python3 -m venv $venv && $venv/bin/pip -q install pyte ptyprocess && cd $dir && BE_VM_KEY=~/be-vm/key $venv/bin/python walk.py $($release.Name) out $($Images -join ' ') $lt"
    scp -q -r "${HostName}:$dir/out" $out
}
finally {
    if ($Vm) { ssh $HostName "sh $dir/vm.sh stop" }
    ssh $HostName "rm -rf $dir"
}
"screens: $out"
