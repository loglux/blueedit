# Runs inside Windows Sandbox: the installer (mapped at C:\release) on a clean Windows. Its pages in English and
# Russian are captured into C:\out\NN-name.png, then it installs silently with PATH, "Open with" and Harper chosen,
# what it did is checked, and it is uninstalled and checked again. Last, the grammar page as a computer with a video
# card shows it (BLUEEDIT_VIDEO_CARD: the Sandbox has none), a model server with a key chosen on it, and what the
# installer wrote. Started by start.ps1 -Installer; docs/tech/installer.md.
$out = 'C:\out'
Set-Content "$out\started.txt" (Get-Date)
Start-Transcript -Path "$out\transcript.txt" -Force | Out-Null
Add-Type -AssemblyName System.Windows.Forms, System.Drawing
$shell = New-Object -ComObject WScript.Shell
$n = 0
$failures = 0
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class Win {
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
}
'@
# the screen is scaled: without this a window's rectangle comes in other units than the screen's pixels
[void][Win]::SetProcessDPIAware()
# the window in front (the installer's), not the whole screen
function Shot([string]$name, [int]$wait = 1500) {
    Start-Sleep -Milliseconds $wait
    $r = New-Object Win+RECT
    [void][Win]::GetWindowRect([Win]::GetForegroundWindow(), [ref]$r)
    $b = [Drawing.Rectangle]::Intersect([Drawing.Rectangle]::FromLTRB($r.Left, $r.Top, $r.Right, $r.Bottom), [Windows.Forms.Screen]::PrimaryScreen.Bounds)
    if ($b.Width -lt 50 -or $b.Height -lt 50) { $b = [Windows.Forms.Screen]::PrimaryScreen.Bounds }
    $bmp = New-Object Drawing.Bitmap $b.Width, $b.Height
    $g = [Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen($b.Location, [Drawing.Point]::Empty, $b.Size)
    $script:n++
    $file = Join-Path $out ('{0:00}-{1}.png' -f $script:n, $name)
    $bmp.Save($file, [Drawing.Imaging.ImageFormat]::Png)
    $g.Dispose(); $bmp.Dispose()
    "shot $file"
}
function Check([string]$what, [bool]$ok) {
    if ($ok) { "  ok    $what" } else { "  FAIL  $what"; $script:failures++ }
}
function UserPath { [Environment]::GetEnvironmentVariable('Path', 'User') }

$setup = (Get-ChildItem C:\release -Filter 'blueedit-*-setup.exe' | Select-Object -First 1).FullName
"== $setup"

function CloseSetup($p) {
    Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue
    Get-Process | Where-Object { $_.Path -like "$env:TEMP\is-*" } | Stop-Process -Force -ErrorAction SilentlyContinue
    Start-Sleep 2
}

# the English words: the tasks page, then closed without installing (the licence: "I do not accept" is chosen at
# first; Tab reaches it, Up chooses "I accept")
"`n== the tasks page in English"
$p = Start-Process $setup '/LANG=en' -PassThru
Start-Sleep 4
[void]$shell.AppActivate($p.Id)
[Windows.Forms.SendKeys]::SendWait('{TAB}{UP}{ENTER}')
Start-Sleep 1
[Windows.Forms.SendKeys]::SendWait('{ENTER}')
Shot 'en-tasks'
CloseSetup $p

"`n== silent install with the defaults: nothing downloaded"
$p = Start-Process $setup '/VERYSILENT /SUPPRESSMSGBOXES /LOG=C:\out\setup-defaults.log' -PassThru -Wait
"  exit code $($p.ExitCode)"
$data = Join-Path $env:LOCALAPPDATA 'BlueEdit'
Check "nothing in $data" (-not (Test-Path $data) -or -not (Get-ChildItem $data -Recurse -File | Where-Object Name -ne 'error.log'))
Check 'the defaults put it in PATH' ((UserPath) -split ';' -contains (Join-Path $env:LOCALAPPDATA 'Programs\BlueEdit'))
Check 'the defaults add "Open with BlueEdit"' (Test-Path 'HKCU:\Software\Classes\*\shell\BlueEdit\command')

"`n== silent install: PATH, Open with, Harper"
$sw = [Diagnostics.Stopwatch]::StartNew()
$p = Start-Process $setup '/VERYSILENT /SUPPRESSMSGBOXES /TASKS="path,openwith" /GRAMMAR=harper /LOG=C:\out\setup.log' -PassThru -Wait
"  exit code $($p.ExitCode), $([int]$sw.Elapsed.TotalSeconds) s"
$app = Join-Path $env:LOCALAPPDATA 'Programs\BlueEdit'
Check "be.exe in $app" (Test-Path "$app\be.exe")
Check 'setup scripts beside it' ((Test-Path "$app\tools\proofing-setup\install.ps1") -and (Test-Path "$app\tools\lsp-setup\install.ps1"))
Check 'LICENSE.txt and THIRD-PARTY-NOTICES.md' ((Test-Path "$app\LICENSE.txt") -and (Test-Path "$app\THIRD-PARTY-NOTICES.md"))
Check "the user's PATH has it" ((UserPath) -split ';' -contains $app)
Check 'App Paths (Win+R be)' ((Get-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\App Paths\be.exe' -ErrorAction SilentlyContinue).'(default)' -eq "$app\be.exe")
Check '"Open with BlueEdit" on files' (Test-Path 'HKCU:\Software\Classes\*\shell\BlueEdit\command')
Check 'the Open with list' (Test-Path 'HKCU:\Software\Classes\Applications\be.exe\shell\open\command')
Check 'Start menu shortcut' (Test-Path "$env:APPDATA\Microsoft\Windows\Start Menu\Programs\BlueEdit.lnk")
Check 'no desktop shortcut (not chosen)' (-not (Test-Path "$env:USERPROFILE\Desktop\BlueEdit.lnk"))
Check 'an uninstaller' (Test-Path "$app\unins000.exe")
$harper = Get-ChildItem (Join-Path $env:LOCALAPPDATA 'BlueEdit\harper') -Recurse -Filter 'harper-ls*.exe' -ErrorAction SilentlyContinue | Select-Object -First 1
Check "Harper downloaded ($($harper.FullName))" ([bool]$harper)
$settings = Join-Path $env:APPDATA 'blueedit\settings.json'
Check 'Harper turned on ("grammar": ["harper"])' ((Test-Path $settings) -and ((Get-Content -Raw $settings) -match '"grammar":\s*\["harper"\]'))
# a new console reads PATH from the registry; this one is older than the install
$env:Path = [Environment]::GetEnvironmentVariable('Path', 'Machine') + ';' + (UserPath)
$version = (& cmd /c 'be --version' 2>&1) -join ' '
"  be --version: $version"
Check '"be" runs from PATH' ($version -like 'BlueEdit *')

"`n== silent install again, over it, without PATH"
$p = Start-Process $setup '/VERYSILENT /SUPPRESSMSGBOXES /TASKS="openwith"' -PassThru -Wait
"  exit code $($p.ExitCode)"
Check 'PATH no longer has it' (-not ((UserPath) -split ';' -contains $app))

"`n== silent uninstall"
$p = Start-Process "$app\unins000.exe" '/VERYSILENT /SUPPRESSMSGBOXES' -PassThru -Wait
# the uninstaller runs a copy of itself from %TEMP% and returns at once: wait for the folder to go
$sw = [Diagnostics.Stopwatch]::StartNew()
while ((Test-Path "$app\be.exe") -and $sw.Elapsed.TotalSeconds -lt 60) { Start-Sleep 1 }
Start-Sleep 2
Check 'be.exe gone' (-not (Test-Path "$app\be.exe"))
Check 'App Paths gone' (-not (Test-Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\App Paths\be.exe'))
Check '"Open with BlueEdit" gone' (-not (Test-Path 'HKCU:\Software\Classes\*\shell\BlueEdit'))
Check 'the Open with entry gone' (-not (Test-Path 'HKCU:\Software\Classes\Applications\be.exe'))
Check 'Start menu shortcut gone' (-not (Test-Path "$env:APPDATA\Microsoft\Windows\Start Menu\Programs\BlueEdit.lnk"))
Check 'what was downloaded stays (silent: the question answers no)' ([bool](Get-ChildItem (Join-Path $env:LOCALAPPDATA 'BlueEdit\harper') -Recurse -Filter 'harper-ls*.exe' -ErrorAction SilentlyContinue))

"`n== the whole install in Russian, as a person goes through it (Enter on each page, the defaults kept)"
$p = Start-Process $setup '/LANG=ru' -PassThru
Start-Sleep 4
[void]$shell.AppActivate($p.Id)
Shot 'ru-1-licence'
[Windows.Forms.SendKeys]::SendWait('{TAB}{UP}')
Shot 'ru-2-licence-accepted' 500
[Windows.Forms.SendKeys]::SendWait('{ENTER}')
Shot 'ru-3-folder'
[Windows.Forms.SendKeys]::SendWait('{ENTER}')
Shot 'ru-4-tasks'
[Windows.Forms.SendKeys]::SendWait('{ENTER}')
# the video card is asked on the way: the page comes up filled
Shot 'ru-5-grammar-no-card' 5000
[Windows.Forms.SendKeys]::SendWait('{ENTER}')
Shot 'ru-6-ready'
[Windows.Forms.SendKeys]::SendWait('{ENTER}')
Shot 'ru-7-installing' 300
$sw = [Diagnostics.Stopwatch]::StartNew()
while (-not (Test-Path "$app\unins000.exe") -and $sw.Elapsed.TotalSeconds -lt 60) { Start-Sleep 1 }
Shot 'ru-8-finished' 3000
Check 'installed through the wizard' (Test-Path "$app\be.exe")
CloseSetup $p

"`n== the grammar page with a video card (BLUEEDIT_VIDEO_CARD), a model server and its key chosen"
# uninstalled first: over an installation the folder page is skipped, and the keys below would land elsewhere
$p = Start-Process "$app\unins000.exe" '/VERYSILENT /SUPPRESSMSGBOXES' -PassThru -Wait
$sw = [Diagnostics.Stopwatch]::StartNew()
while ((Test-Path "$app\be.exe") -and $sw.Elapsed.TotalSeconds -lt 60) { Start-Sleep 1 }
Start-Sleep 2
Remove-Item $settings -ErrorAction SilentlyContinue
$env:BLUEEDIT_VIDEO_CARD = '8188 NVIDIA GeForce RTX 4060'
$p = Start-Process $setup '/LANG=ru' -PassThru
Start-Sleep 4
[void]$shell.AppActivate($p.Id)
# the licence taken, the folder and the tasks as they are; then the grammar page
[Windows.Forms.SendKeys]::SendWait('{TAB}{UP}{ENTER}')
Start-Sleep 1
[Windows.Forms.SendKeys]::SendWait('{ENTER}')
Start-Sleep 1
[Windows.Forms.SendKeys]::SendWait('{ENTER}')
Shot 'ru-9-grammar-8gb' 5000
# Claude, chosen with Space (the last lines: OpenAI, Claude, Gemini, DeepSeek, another, Ollama, LM Studio, llama-server)
[Windows.Forms.SendKeys]::SendWait('{END}{UP 6} ')
Shot 'ru-10-grammar-claude' 800
[Windows.Forms.SendKeys]::SendWait('{ENTER}')
Start-Sleep 1
# its address offered (and its model kept): only the key is typed
[Windows.Forms.SendKeys]::SendWait('{TAB}sk-sandbox-secret')
Shot 'ru-11-server-claude' 800
# Next checks the server: this key is not a real one, so Anthropic refuses it, and the installer asks whether to keep it
# anyway (No, the default, goes back to fix it); Yes here
[Windows.Forms.SendKeys]::SendWait('{ENTER}')
Shot 'ru-12-check-refused' 8000
[Windows.Forms.SendKeys]::SendWait('+{TAB}{ENTER}')
Shot 'ru-13-ready'
[Windows.Forms.SendKeys]::SendWait('{ENTER}')
$sw = [Diagnostics.Stopwatch]::StartNew()
while (-not ((Test-Path $settings) -and (Get-Content -Raw $settings) -match '"grammar":\s*\["llm"\]') -and $sw.Elapsed.TotalSeconds -lt 60) { Start-Sleep 1 }
Start-Sleep 2
$text = if (Test-Path $settings) { Get-Content -Raw $settings } else { '' }
Check 'Claude in the settings' ($text -match '"llmServer":\s*"https://api.anthropic.com/v1"' -and $text -match '"llmServerModel":\s*"claude-haiku-5-5"' -and $text -match '"llmModel":\s*"server"')
Check 'the settings name the key file, not the key' ($text -match '"llmApiKey":\s*"file:llm-key"' -and $text -notmatch 'sk-sandbox-secret')
Check 'grammar turned on ("llm")' ($text -match '"grammar":\s*\["llm"\]')
$keyFile = Join-Path $env:APPDATA 'blueedit\llm-key'
Check 'the key file is there, encrypted' ((Test-Path $keyFile) -and -not [Text.Encoding]::UTF8.GetString([IO.File]::ReadAllBytes($keyFile)).Contains('sk-sandbox-secret'))
$leaks = Get-ChildItem C:\out, (Join-Path $env:APPDATA 'blueedit'), (Join-Path $env:LOCALAPPDATA 'BlueEdit') -Recurse -File -ErrorAction SilentlyContinue |
    Where-Object Name -notin 'llm-key', 'transcript.txt' | Select-String -SimpleMatch 'sk-sandbox-secret' -List
Check "the key is in no log or settings file $($leaks.Path)" (-not $leaks)
CloseSetup $p

"`n== Ollama on the grammar page: its address offered, no key asked (closed without installing)"
$p = Start-Process $setup '/LANG=ru' -PassThru
Start-Sleep 4
[void]$shell.AppActivate($p.Id)
[Windows.Forms.SendKeys]::SendWait('{TAB}{UP}{ENTER}')
Start-Sleep 1
[Windows.Forms.SendKeys]::SendWait('{ENTER}')
Start-Sleep 5
[Windows.Forms.SendKeys]::SendWait('{END}{UP 2} {ENTER}')
Shot 'ru-14-server-ollama' 1500
CloseSetup $p
$env:BLUEEDIT_VIDEO_CARD = $null

"`n$failures failure(s)"
Stop-Transcript | Out-Null
Set-Content "$out\done.txt" $failures
