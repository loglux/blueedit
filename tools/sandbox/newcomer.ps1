# Runs inside Windows Sandbox: what a new user meets. The release zip (mapped at C:\release) is unpacked to the
# Downloads folder, be.exe is started the way a double click starts it, keys are pressed and the screen is captured
# after each step into C:\out\NN-name.png. Started by start.ps1 -Newcomer; see docs/ci.md.
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
    Shot $name
}

"== the machine"
"Windows $([Environment]::OSVersion.Version), culture $((Get-Culture).Name), UI $((Get-UICulture).Name)"
"Windows Terminal: " + [bool](Get-Command wt -ErrorAction SilentlyContinue)
"winget: " + [bool](Get-Command winget -ErrorAction SilentlyContinue)
"java: " + [bool](Get-Command java -ErrorAction SilentlyContinue)
"Visual C++ runtime: " + (Test-Path "$env:windir\System32\vcruntime140.dll")
"Vulkan loader: " + (Test-Path "$env:windir\System32\vulkan-1.dll") + " (the host's file; proves no video card)"

"`n== unpack the release zip into Downloads"
$zip = Get-ChildItem C:\release -Filter 'blueedit-*-win-x64.zip' | Select-Object -First 1
$dest = Join-Path $env:USERPROFILE "Downloads\$($zip.BaseName)"
Expand-Archive $zip.FullName $dest -Force
Get-ChildItem $dest -Recurse -Depth 1 | ForEach-Object { "  " + $_.FullName.Substring($dest.Length) }
# a downloaded file carries the internet zone mark; Expand-Archive does not, so add it as a browser would
foreach ($f in Get-ChildItem $dest -Recurse -File) { Set-Content -Path $f.FullName -Stream Zone.Identifier "[ZoneTransfer]`r`nZoneId=3" }
"video cards Vulkan sees (be --vulkan-devices): " + ((& (Join-Path $dest 'be.exe') --vulkan-devices) -join '; ')

"`n== double click on be.exe"
# through the shell as Explorer does it: a marked file may first get a security question, which would block a
# Start-Process here, so the start runs in a job and the screen is captured while it waits
$exe = Join-Path $dest 'be.exe'
$job = Start-Job { param($exe, $dir) Start-Process $exe -WorkingDirectory $dir } -ArgumentList $exe, $dest
Start-Sleep 4
Shot 'double-click'
$script:editor = Get-Process be -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $editor) {
    "be.exe is not running after 4 s: a security question? (see the picture); answering Run"
    foreach ($try in 1..5) {
        [void]$shell.AppActivate('Open File - Security Warning')       # the question is not always in front
        Start-Sleep -Milliseconds 500
        [Windows.Forms.SendKeys]::SendWait('%r')
        Start-Sleep 3
        $script:editor = Get-Process be -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($editor) { break }
    }
    Shot 'after-run'
}
"job: " + $job.State
"running: " + [bool]$editor
if (-not $editor) { $editor = [pscustomobject]@{ HasExited = $true } }
if (-not $editor.HasExited) {
    Shot 'welcome'                                        # the first start: the welcome (Esc begins)
    Keys '{RIGHT}{RIGHT}{ENTER}' 'welcome-languages'         # its third button: the interface's languages
    Keys '{DOWN}{ENTER}' 'welcome-russian'
    Keys '{RIGHT}{RIGHT}{ENTER}{UP}{ENTER}' 'welcome-english' # and back (the steps below press English letters)
    Keys '{ESC}' 'after-welcome'
    Keys '{F9}' 'menu'                                    # the key bar says F9 Menu (F10 quits)
    Keys '{ESC}{ESC}{F1}' 'help'
    Keys '{ESC}' 'back'
    # a mistake for each checker ("Helo": spelling, "a apple": Harper, "this are": LanguageTool), in the languages
    # this Windows has: a Russian one only where Russian is added
    $text = 'Helo world, this are a test. I ate a apple.'
    if ((Get-WinUserLanguageList).LanguageTag -like 'ru*') { $text += ' Превет мир.' }
    "the system's languages: " + ((Get-WinUserLanguageList).LanguageTag -join ', ')
    Keys "$text{ENTER}" 'typed'
    Keys '{UP}{F7}' 'f7'
    Keys '{ESC}' 'after-f7'
    Keys '{F9}{RIGHT 5}' 'tools-menu'
    Keys 'u' 'set-up-grammar'
    Keys '{DOWN 5}{ENTER}' 'harper-chosen'                 # Harper: ~60 MB, the quickest
    foreach ($s in 1..12) { Start-Sleep 10; if ($editor.HasExited) { break } }
    Shot 'harper-after-2min'
    "Visual C++ runtime in System32 now: " + (Test-Path "$env:windir\System32\vcruntime140.dll")
    "next to harper-ls: " + ((Get-ChildItem "$env:LOCALAPPDATA\BlueEdit\harper" -ErrorAction SilentlyContinue).Name -join ", ")
    Keys '{END}' 'grammar-shown'                        # no typing: an edit would clear the marks until rechecked
    Keys '{F9}{RIGHT 5}' 'tools-menu-2'
    Keys 'u' 'set-up-grammar-2'
    Keys '{DOWN 4}{ENTER}' 'languagetool-chosen'          # LanguageTool: Java and ~530 MB
    foreach ($s in 1..18) { Start-Sleep 10; if ($editor.HasExited) { break } }
    Shot 'languagetool-after-3min'
    Keys '{END}' 'languagetool-shown'
    "still running: " + -not $editor.HasExited
}

"`n== the install log"
Get-Content "$env:LOCALAPPDATA\BlueEdit\tools\install.log" -ErrorAction SilentlyContinue

"`n== done"
Stop-Transcript | Out-Null
Set-Content "$out\done.txt" 'done'
