<#
.SYNOPSIS
  Shows which spelling and grammar components BlueEdit can use on this computer, and with -Run tries each one.

.DESCRIPTION
  Without -Run it only looks: Windows spelling languages, Microsoft Word, Java + LanguageTool, llama.cpp and the
  models, Edge Canary. With -Run it starts LanguageTool and llama.cpp (the model BlueEdit would pick, or -Model) for a
  moment and asks each to check a sentence with mistakes — the same requests BlueEdit sends. BlueEdit itself does not
  need to be closed, except that a model has to fit in the video memory next to the one BlueEdit may have loaded.

.EXAMPLE
  .\check.ps1
.EXAMPLE
  .\check.ps1 -Run -Model YandexGPT-5-Lite-8B-instruct-Q4_K_M.gguf
#>
[CmdletBinding()]
param([switch]$Run, [string]$Model = '', [string]$Root = (Join-Path $env:LOCALAPPDATA 'BlueEdit'))
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
function Ok($what, $detail) { Write-Host ('  [ok]   {0,-22} {1}' -f $what, $detail) -ForegroundColor Green }
function No($what, $detail) { Write-Host ('  [--]   {0,-22} {1}' -f $what, $detail) -ForegroundColor DarkGray }
function Bad($what, $detail) { Write-Host ('  [FAIL] {0,-22} {1}' -f $what, $detail) -ForegroundColor Red }
# Windows PowerShell 5.1 (all a fresh Windows has) reads an answer without a charset as Latin-1: LanguageTool's
# Russian came out as "ÐÐµÐ²...". The answer's bytes are decoded as UTF-8 here, the JSON parsed after.
function PostUtf8($url, $body, $contentType) {
    $r = Invoke-WebRequest $url -Method Post -Body $body -ContentType $contentType -UseBasicParsing
    [Text.Encoding]::UTF8.GetString($r.RawContentStream.ToArray()) | ConvertFrom-Json
}
function FreePort { $l = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0); $l.Start(); $p = $l.LocalEndpoint.Port; $l.Stop(); $p }

Write-Host "`n== spelling"
$langs = (Get-ItemProperty 'HKCU:\Control Panel\International\User Profile' -ErrorAction SilentlyContinue).Languages
if ($langs) { Ok 'Windows' ("languages added in Windows: " + ($langs -join ', ')) } else { Bad 'Windows' 'no languages found in the registry' }
$word = Get-ItemProperty 'Registry::HKEY_CLASSES_ROOT\Word.Application\CurVer' -ErrorAction SilentlyContinue
if ($word) { Ok 'Microsoft Word' $word.'(default)' } else { No 'Microsoft Word' 'not installed (the "office" spelling provider needs it)' }

Write-Host "`n== grammar"
$java = Join-Path $Root 'jre\bin\java.exe'
$jar = Join-Path $Root 'languagetool\languagetool-server.jar'
$lt = (Test-Path $java) -and (Test-Path $jar)
if ($lt) { Ok 'LanguageTool' ((Get-Content (Join-Path $Root 'jre\release') | Select-String 'IMPLEMENTOR_VERSION').Line + ', ' + (Split-Path $jar)) }
else { No 'LanguageTool' "not installed: install.ps1 -LanguageTool" }

$server = Join-Path $Root 'llm\bin\llama-server.exe'
$models = @(Get-ChildItem (Join-Path $Root 'llm\models') -Filter *.gguf -ErrorAction SilentlyContinue)
if (Test-Path $server) { Ok 'llama.cpp' (Split-Path $server) } else { No 'llama.cpp' 'not installed: install.ps1 -Llm' }
foreach ($m in $models) { Ok '  model' ('{0} ({1:0.0} GB)' -f $m.Name, ($m.Length / 1GB)) }
if ((Test-Path $server) -and $models.Count -eq 0) { Bad '  model' 'no .gguf in llm\models: install.ps1 -Models yandexgpt' }
if (Get-Command nvidia-smi -ErrorAction SilentlyContinue) {
    $g = (& nvidia-smi --query-gpu=name,memory.total,memory.used,driver_version --format=csv,noheader | Select-Object -First 1)
    Ok '  GPU' $g
}

$canary = Join-Path $env:LOCALAPPDATA 'Microsoft\Edge SxS\Application\msedge.exe'
if (Test-Path $canary) { Ok 'Edge Canary' ((Get-Item $canary).VersionInfo.ProductVersion) } else { No 'Edge Canary' 'not installed: install.ps1 -EdgeCanary' }
$edgeModel = Get-ChildItem (Join-Path $Root 'edge-profile\EdgeLLMOnDeviceModel') -Directory -ErrorAction SilentlyContinue | Select-Object -First 1
if ($edgeModel) { Ok '  Edge model' $edgeModel.Name } elseif (Test-Path $canary) { No '  Edge model' 'downloads on first use (~1.2 GB)' }

if (-not $Run) { Write-Host "`n  -Run tries LanguageTool and the language model on a test sentence."; return }

$sentenceRu = 'Мы был в магазине и купили хлеб.'
if ($lt) {
    Write-Host "`n== LanguageTool: `"$sentenceRu`""
    $port = FreePort
    $p = Start-Process $java -ArgumentList '-Xmx768m', '-cp', 'languagetool-server.jar', 'org.languagetool.server.HTTPServer', '--port', $port `
        -WorkingDirectory (Split-Path $jar) -WindowStyle Hidden -PassThru
    try {
        $sw = [Diagnostics.Stopwatch]::StartNew(); $res = $null
        while (-not $res -and $sw.Elapsed.TotalSeconds -lt 60) {
            try { $res = PostUtf8 "http://127.0.0.1:$port/v2/check" @{ text = $sentenceRu; language = 'ru-RU' } 'application/x-www-form-urlencoded' } catch { Start-Sleep -Milliseconds 500 }
        }
        if ($res) { Ok 'answer' ("in {0:0.0} s: " -f $sw.Elapsed.TotalSeconds) ; foreach ($m in $res.matches) { Write-Host "         $($sentenceRu.Substring($m.offset, $m.length)): $($m.message)" } }
        else { Bad 'answer' 'no answer in 60 s' }
    } finally { Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue }
}

if ((Test-Path $server) -and $models.Count) {
    $order = 'YandexGPT-5-Lite-8B-instruct-Q4_K_M.gguf', 'google_gemma-3-12b-it-Q4_K_M.gguf', 'Qwen3-8B-Q4_K_M.gguf', 'Qwen_Qwen3-14B-Q4_K_M.gguf', 'google_gemma-3-4b-it-Q4_K_M.gguf', 'microsoft_Phi-4-mini-instruct-Q4_K_M.gguf'
    $pick = if ($Model) { $models | Where-Object Name -eq $Model } else { $order | ForEach-Object { $n = $_; $models | Where-Object Name -eq $n } | Select-Object -First 1 }
    if (-not $pick) { $pick = $models[0] }
    Write-Host "`n== language model $($pick.Name): `"$sentenceRu`""
    $port = FreePort
    $p = Start-Process $server -ArgumentList '-m', "`"$($pick.FullName)`"", '-ngl', '99', '-c', '4096', '--host', '127.0.0.1', '--port', $port `
        -WorkingDirectory (Split-Path $server) -WindowStyle Hidden -PassThru
    try {
        $sw = [Diagnostics.Stopwatch]::StartNew(); $ready = $false
        while (-not $ready -and $sw.Elapsed.TotalSeconds -lt 180 -and -not $p.HasExited) {
            try { Invoke-RestMethod "http://127.0.0.1:$port/health" | Out-Null; $ready = $true } catch { Start-Sleep -Milliseconds 500 }
        }
        if (-not $ready) { Bad 'start' "llama-server did not get ready (exit code: $($p.ExitCode))"; return }
        Ok 'loaded' ("in {0:0.0} s" -f $sw.Elapsed.TotalSeconds)
        $body = @{
            # BlueEdit's own instructions (LlmGrammar.cs, shortened to the corrected text). A bare "you are a
            # proofreader" made YandexGPT translate the Russian sentence into English instead of correcting it.
            messages = @(@{ role = 'system'; content = 'You are a careful proofreader for Russian and British English (en-GB spelling). Fix only spelling, grammar and punctuation errors in the user''s text. Do not rephrase, do not change style, do not translate, do not change anything that is correct. Answer only with JSON: {"corrected": "<the whole text, corrected, in its own language>"}.' },
                         @{ role = 'user'; content = $sentenceRu })
            temperature = 0; max_tokens = 200; chat_template_kwargs = @{ enable_thinking = $false }
            response_format = @{ type = 'json_schema'; json_schema = @{ name = 'proofread'; schema = @{ type = 'object'; properties = @{ corrected = @{ type = 'string' } }; required = @('corrected') } } }
        } | ConvertTo-Json -Depth 10
        $sw.Restart()
        $r = PostUtf8 "http://127.0.0.1:$port/v1/chat/completions" ([Text.Encoding]::UTF8.GetBytes($body)) 'application/json; charset=utf-8'
        Ok 'answer' ("in {0:0.0} s: {1}" -f $sw.Elapsed.TotalSeconds, ($r.choices[0].message.content | ConvertFrom-Json).corrected)
    } finally { Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue }
}
