# Runs inside Windows Sandbox: the README pictures of images in Markdown (docs/screenshots/images-*.png). Started by
# start.ps1 -Images (tasks images-sandbox), which puts in C:\release be.exe, mathimages\, the files of
# tools/screenshots/samples/darwin, settings.json and wt\, a portable Windows Terminal. The note is opened in Windows
# Terminal, captured at the top and two pages down, and each capture is cut to the window into C:\out.
$out = 'C:\out'
Start-Transcript -Path "$out\transcript.txt" -Force | Out-Null
Add-Type -AssemblyName System.Windows.Forms, System.Drawing
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class Win {
    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr hwnd, int attr, out RECT rect, int size);
}
'@
[void][Win]::SetProcessDPIAware()                       # the window's bounds and the capture in the same pixels
$shell = New-Object -ComObject WScript.Shell

function Shot([string]$name) {
    Start-Sleep -Milliseconds 3000
    $b = [Windows.Forms.Screen]::PrimaryScreen.Bounds
    $bmp = New-Object Drawing.Bitmap $b.Width, $b.Height
    $g = [Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen($b.Location, [Drawing.Point]::Empty, $b.Size)
    $g.Dispose()
    # the window as drawn (DWMWA_EXTENDED_FRAME_BOUNDS), without the invisible borders GetWindowRect counts
    $w = Get-Process WindowsTerminal -ErrorAction SilentlyContinue | Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object -First 1
    $r = New-Object Win+RECT
    if ($w -and [Win]::DwmGetWindowAttribute($w.MainWindowHandle, 9, [ref]$r, 16) -eq 0) {
        $rect = [Drawing.Rectangle]::FromLTRB([Math]::Max(0, $r.Left), [Math]::Max(0, $r.Top), [Math]::Min($b.Width, $r.Right), [Math]::Min($b.Height, $r.Bottom))
        $cut = $bmp.Clone($rect, $bmp.PixelFormat)
        $bmp.Dispose(); $bmp = $cut
    }
    $bmp.Save("$out\$name.png", [Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
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
Copy-Item C:\release\* C:\be -Recurse -Force
New-Item -ItemType Directory C:\be\cfg -Force | Out-Null
Copy-Item C:\be\settings.json C:\be\cfg\settings.json
$env:BLUEEDIT_CONFIG_DIR = 'C:\be\cfg'                 # the reading view, no Windows Terminal profile message
$env:BLUEEDIT_WELCOME = '0'
"Windows $([Environment]::OSVersion.Version)"
Start-Process C:\be\wt\WindowsTerminal.exe -ArgumentList '--size', '110,31', '--pos', '40,40', 'C:\be\be.exe', 'C:\be\darwin.md'
Start-Sleep 10
Shot 'images-text'
Keys '{PGDN}' 'images-middle'
Keys '{PGDN}' 'images-diagram'
"be running: " + [bool](Get-Process be -ErrorAction SilentlyContinue)
Stop-Transcript | Out-Null
Set-Content "$out\done.txt" 'done'
