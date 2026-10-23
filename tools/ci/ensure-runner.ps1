<#
  Makes sure the self-hosted GitHub runner (docs/ci.md) is running, so a push does not leave its checks waiting in
  the queue. Called by .githooks/pre-push; never fails the push. Starts the runner through its scheduled task
  ("GitHub runner (blueedit)") when it is not running; says so in one line.
#>
$ErrorActionPreference = 'SilentlyContinue'
$running = Get-CimInstance Win32_Process -Filter "Name = 'Runner.Listener.exe'"
if ($running) { exit 0 }
$task = Get-ScheduledTask -TaskName 'GitHub runner (blueedit)'
if ($task) {
    Start-ScheduledTask -TaskName 'GitHub runner (blueedit)'
    Write-Host 'ensure-runner: the GitHub runner was not running; started it (its checks start in a few seconds)'
}
else {
    Write-Host 'ensure-runner: the GitHub runner is not running and has no autostart task; see docs/ci.md'
}
exit 0
