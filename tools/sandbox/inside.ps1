# Runs inside Windows Sandbox (a clean Windows, as a new user has it): the setup scripts from the release, everything
# logged to the mapped output folder (C:\out), done.txt last. Started by start.ps1; see docs/ci.md.
$out = 'C:\out'
Set-Content "$out\started.txt" (Get-Date)                 # proof the script ran and can write here
Start-Transcript -Path "$out\transcript.txt" -Force | Out-Null
"== the machine"
"Windows $([Environment]::OSVersion.Version), PowerShell $($PSVersionTable.PSVersion)"
"winget: " + [bool](Get-Command winget -ErrorAction SilentlyContinue)
"node: " + [bool](Get-Command node -ErrorAction SilentlyContinue) + ", dotnet: " + [bool](Get-Command dotnet -ErrorAction SilentlyContinue)
Copy-Item -Recurse C:\tools C:\work-tools                 # the mapped folder is read-only
Set-ExecutionPolicy -Scope Process Bypass -Force

"`n== proofing-setup: LanguageTool (Java + LanguageTool) into the default folder"
$t = [Diagnostics.Stopwatch]::StartNew()
try { & C:\work-tools\proofing-setup\install.ps1 -LanguageTool } catch { "FAILED: $_" }
"took {0:0} s" -f $t.Elapsed.TotalSeconds

"`n== proofing-setup: check -Run (LanguageTool answers a sentence)"
try { & C:\work-tools\proofing-setup\check.ps1 -Run } catch { "FAILED: $_" }

"`n== lsp-setup -All -Yes (no winget here: what does it say?)"
try { & C:\work-tools\lsp-setup\install.ps1 -All -Yes } catch { "FAILED: $_" }

"`n== done"
Stop-Transcript | Out-Null
Set-Content "$out\done.txt" 'done'
