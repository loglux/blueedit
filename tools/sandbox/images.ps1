# Runs inside Windows Sandbox: the README pictures of images in Markdown (docs/screenshots/images-*.png).
# C:\pack holds be.exe, mathimages\, the files of tools/screenshots/samples/darwin, settings.json
# ({"fontSetup": "hint", "markdownView": "reading"}) and wt\, the portable Windows Terminal (its release zip from
# github.com/microsoft/terminal with an empty .portable file in it); C:\out gets a picture per step. A .wsb maps
# both, as tools/sandbox/start.ps1 writes one, and runs this file. The pictures are then cropped to the window.
$out = 'C:\out'
Start-Transcript -Path "$out\transcript.txt" -Force | Out-Null
Add-Type -AssemblyName System.Windows.Forms, System.Drawing
$shell = New-Object -ComObject WScript.Shell
function Shot([string]$name) {
    Start-Sleep -Milliseconds 3000
    $b = [Windows.Forms.Screen]::PrimaryScreen.Bounds
    $bmp = New-Object Drawing.Bitmap $b.Width, $b.Height
    $g = [Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen($b.Location, [Drawing.Point]::Empty, $b.Size)
    $bmp.Save("$out\$name.png", [Drawing.Imaging.ImageFormat]::Png)
    $g.Dispose(); $bmp.Dispose()
    "shot $name"
}
function Keys([string]$k, [string]$name) {
    $w = Get-Process WindowsTerminal -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($w) { [void]$shell.AppActivate($w.Id) }
    Start-Sleep -Milliseconds 400
    [Windows.Forms.SendKeys]::SendWait($k)
    Shot $name
}
New-Item -ItemType Directory C:\be -Force | Out-Null
Copy-Item C:\pack\* C:\be -Recurse -Force
New-Item -ItemType Directory C:\be\cfg -Force | Out-Null
Copy-Item C:\be\settings.json C:\be\cfg\settings.json
$env:BLUEEDIT_CONFIG_DIR = 'C:\be\cfg'
$env:BLUEEDIT_WELCOME = '0'
"settings: " + (Get-Content C:\be\cfg\settings.json -Raw)
Start-Process C:\be\wt\WindowsTerminal.exe -ArgumentList '--size', '110,31', '--pos', '40,40', 'C:\be\be.exe', 'C:\be\darwin.md'
Start-Sleep 10
Shot '01-top'
Keys '{PGDN}' '02-pgdn'
Keys '{PGDN}' '03-pgdn2'
Keys '{PGDN}' '04-pgdn3'
"be running: " + [bool](Get-Process be -ErrorAction SilentlyContinue)
Stop-Transcript | Out-Null
Set-Content "$out\done.txt" 'done'
