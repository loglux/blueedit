# Runs inside Windows Sandbox: the editor's dialogs in another interface language, each captured, nothing installed.
# The release zip (mapped at C:\release) is unpacked, be.exe started, the welcome's Language button sets Russian, then
# the menus' dialogs are opened one by one into C:\out\NN-name.png. Started by start.ps1 -Dialogs; see docs/ci.md.
$out = 'C:\out'
Set-Content "$out\started.txt" (Get-Date)
Start-Transcript -Path "$out\transcript.txt" -Force | Out-Null
Add-Type -AssemblyName System.Windows.Forms, System.Drawing
$shell = New-Object -ComObject WScript.Shell
$n = 0
function Shot([string]$name) {
    Start-Sleep -Milliseconds 1500
    $b = [Windows.Forms.Screen]::PrimaryScreen.Bounds
    $bmp = New-Object Drawing.Bitmap $b.Width, $b.Height
    $g = [Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen($b.Location, [Drawing.Point]::Empty, $b.Size)
    $script:n++
    $file = Join-Path $out ('{0:00}-{1}.png' -f $script:n, $name)
    $bmp.Save($file, [Drawing.Imaging.ImageFormat]::Png)
    $g.Dispose(); $bmp.Dispose()
    "shot $file"
}
function Keys([string]$keys, [string]$name) {
    [void]$shell.AppActivate($script:editor.Id)
    Start-Sleep -Milliseconds 300
    [Windows.Forms.SendKeys]::SendWait($keys)
    if ($name) { Shot $name }
}
# a Tools item by its place in the menu (the letters differ between languages), captured, then closed
function Tools([int]$item, [string]$name, [string]$then = '') {
    Keys "{F9}{RIGHT 5}{DOWN $item}{ENTER}$then" $name
    Keys '{ESC}{ESC}' ''
}

$zip = Get-ChildItem C:\release -Filter 'blueedit-*-win-x64.zip' | Select-Object -First 1
$dest = Join-Path $env:USERPROFILE "Downloads\$($zip.BaseName)"
Expand-Archive $zip.FullName $dest -Force
$script:editor = Start-Process (Join-Path $dest 'be.exe') -WorkingDirectory $dest -PassThru
Start-Sleep 4
if (-not $editor.HasExited) {
    Shot 'welcome'
    Keys '{RIGHT}{RIGHT}{ENTER}' 'languages'
    Keys '{DOWN}{ENTER}' 'welcome-russian'
    Keys '{ESC}' 'begin'
    Keys 'Helo world.{ENTER}' ''
    Keys '{F9}{DOWN 8}{ENTER}' 'file-info'                 # File > Info
    Keys '{ESC}{ESC}' ''
    Tools 4 'style-options'
    Tools 6 'spelling-languages'
    Tools 7 'spelling-provider'
    Tools 8 'dictionaries'
    Tools 9 'set-up-grammar'
    Tools 9 'model-here' '{ENTER}'                          # its first line: a language model here, the tested ones
    Tools 10 'grammar-checking'
    Tools 10 'grammar-not-installed' ' {ENTER}'            # LanguageTool ticked without being installed
    Tools 11 'grammar-model'
    Tools 13 'settings'
    Tools 14 'code-tools'
    Tools 15 'terminal-font'
    Tools 15 'terminal-font-by-hand' '{RIGHT}{RIGHT}{ENTER}'
    Keys '{F1}' 'help'
    Keys '{ESC}' ''
    # Tools > Command be in any console: put in PATH, then a new console finds "be"
    Keys "{F9}{RIGHT 5}{DOWN 16}{ENTER}" 'path-question'
    Keys '{ENTER}' 'path-added'
    "user PATH: " + [Environment]::GetEnvironmentVariable('Path', 'User')
    "kind: " + (Get-Item HKCU:\Environment).GetValueKind('Path')
    # a new console gets the system's PATH and then the user's (this script's own process keeps its old one)
    $env:Path = [Environment]::GetEnvironmentVariable('Path', 'Machine') + ';' + [Environment]::GetEnvironmentVariable('Path', 'User')
    "a new console finds: " + (Get-Command be -ErrorAction SilentlyContinue).Source
}
"`n== done"
Stop-Transcript | Out-Null
Set-Content "$out\done.txt" 'done'
