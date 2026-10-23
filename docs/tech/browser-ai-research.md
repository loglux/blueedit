# Research: built-in AI of Edge and Chrome, Word and Windows proofing

*По-русски: [browser-ai-research.ru.md](browser-ai-research.ru.md)*

Notes from the research done for BlueEdit's spelling and grammar checking (October 2026, Windows 11, RTX 4070 12 GB,
64 GB RAM). Everything here was observed on that machine unless marked otherwise; versions matter, because these
features change from build to build. What was used in the editor is in [proofing-internals.md](proofing-internals.md)
and the user guide [../proofing.md](../proofing.md).

Method: only what a program shows of itself — browser pages, the DevTools protocol, strings in the program files,
configuration files the browser downloaded. No disassembly, no patching.

## 1. Edge's Proofreader API

### What it is

A web API (`Proofreader`) of the W3C Web Machine Learning group's draft ([spec](https://webmachinelearning.github.io/proofreader-api/))
that proofreads text with a small language model running on the computer. Microsoft documents it as an experimental
developer preview in Edge Canary and Dev from version 142 ([docs](https://learn.microsoft.com/en-us/microsoft-edge/web-platform/proofreader-api)).

### Versions on the machine

| | Version | Proofreader |
|---|---|---|
| Edge stable | 154.0.4258.48 | object exists with the switches below, but `availability()` never answers (timeout 20 s) |
| Edge Canary | 157.0.4315.0 | works with the switches below |

### Turning it on: what the docs say and what is true

Microsoft's page says: enable **"Proofreader API for Phi mini"** in `edge://flags` and restart. In Canary 157 there
is no such flag. `edge://flags` (a page of web components: plain text extraction sees nothing, the flag list is in
shadow roots) lists only these on-device AI flags:

- `edge-llm-prompt-api-for-phi-mini`, `edge-llm-summarization-api-for-phi-mini`, `edge-llm-writer-api-for-phi-mini`,
  `edge-llm-rewriter-api-for-phi-mini`, `edge-llm-on-device-model-debug-logs`
- Canary only: `edge-llm-on-device-model-performance-param`, `edge-llm-on-device-model-prerelease`,
  `edge-on-device-speech-recognition`

(Flags can be set without the UI: `browser.enabled_labs_experiments` in the profile's `Local State`, e.g.
`"edge-llm-prompt-api-for-phi-mini@1"`.) With those flags Canary showed `LanguageModel`, `Rewriter`, `Summarizer`,
`LanguageDetector`, `Translator` — but `Proofreader` stayed `undefined`. Guessed names (`AIProofreaderAPIForPhiMini`,
`EdgeLLMProofreaderAPIForPhiMini`, the flag `edge-proofreader-api@1`) did nothing.

The names were found in the program itself: `msedge.dll` of Canary (338 MB) holds 73 distinct strings with
"proofread", among them `AIProofreaderAPI`, `AIProofreadingAPI`, `kAIProofreadingAPI`, `kProofreaderApi`,
`edge-proofreader-api`, `third_party\blink\renderer\modules\ai\proofreader.cc`,
`blink.mojom.AIManagerCreateProofreaderClient` and the flag text "Enables the Proofreader API, allowing you to
proofread a piece of text with the on-device small language model in Microsoft Edge…". A feature of the browser
process and one of the page engine (Blink) both have to be on:

```
msedge.exe --enable-features=AIProofreadingAPI --enable-blink-features=AIProofreaderAPI
```

Then, in Canary: `Proofreader` is a function; `availability({expectedInputLanguages: ['en']})` → `downloadable`,
`['ru']` → `unavailable`. **English only.**

### Requirements (Microsoft's docs)

GPU with 5.5 GB of video memory or more; at least 20 GB free on the drive with the profile — **below 10 GB Edge
deletes the model**; an unmetered connection (no download on a metered one); Windows 10/11 or macOS 13.3+. Whether a
computer qualifies: `edge://on-device-internals`, "Device performance class" High or above.

**It does run on the GPU** (measured 7 October 2026, Edge Canary 157.0.4323.0, RTX 4070 12 GB): while BlueEdit's Edge
checker proofread the British academic set (59 sentences, median 0.4 s), `nvidia-smi` sampled every second showed up
to 12,002 MB of video memory in use (all of the card) and up to 71 % load, with only `msedge.exe` and the desktop
compositor on the GPU. Unlike Chrome's proofreader, which ran on the CPU on the same computer: a computer without a
capable GPU can have Chrome's, not Edge's.

### Driving it from another program

A headless Edge with a profile of its own, driven over the Chrome DevTools Protocol (CDP):

```
msedge.exe --headless=new --remote-debugging-port=0 --user-data-dir=<profile> --no-first-run
           --no-default-browser-check --enable-features=AIProofreadingAPI
           --enable-blink-features=AIProofreaderAPI file:///<profile>/page.html
```

- `--remote-debugging-port=0` lets Edge pick a free port and write it into `<profile>\DevToolsActivePort`.
- `GET http://127.0.0.1:<port>/json` lists the pages; connect a WebSocket to the page's `webSocketDebuggerUrl` and
  call `Runtime.evaluate` with `awaitPromise: true, returnByValue: true, userGesture: true`.

Traps met, and what fixed them:

| Symptom | Cause | Fix |
|---|---|---|
| `NotAllowedError: Requires a user gesture when availability is "downloading" or "downloadable".` | `create()` needs a click while the model is not downloaded | `userGesture: true` in `Runtime.evaluate` |
| `ReferenceError: Proofreader is not defined` on `about:blank` | the API needs a secure context; `about:blank` is not one | an `https://` page — or, offline, a local `file:///…html` page (works) |
| `Proofreader` undefined right after start | the page was listed before its document loaded | wait for `document.readyState == "complete"` (and our page's title) |
| `AbortError: The request was cancelled.` now and then | not found out | call `availability()` first; retry `proofread()` up to 3 times |
| Edge stable 154: `availability()` never answers | — | a 30 s limit and the message "try Edge Canary" |

### The model

After `create()` Edge downloaded the model in about 70 s (first `proofread()` 9 s, later ones 0.15–0.4 s). It lands
in the profile:

```
<profile>\EdgeLLMOnDeviceModel\2026.10.2.1\     1,248 MB
    model.onnx (1 MB), model.onnx.data (1,261 MB), genai_config.json, manifest.json, tokenizer.json (15 MB),
    edge_on_device_model_execution_config.pb (7 KB), program_cache.bin, adapter_cache.bin, encoder_cache.bin
```

- `manifest.json`: "Edge On Device Model", base model **`Aion-1.1-Instruct`**, version 2026.10.2.1, type `webgpu`.
- `genai_config.json`: runtime `onnxruntime-genai`, WebGPU provider; architecture `qwen3`, context 32768,
  hidden size 2048, 28 layers, 24 heads (8 KV heads), vocabulary 200,029. System text: "You are Aion Instruct, a
  large language model trained by Microsoft."

So **the docs are out of date**: they name Phi-4-mini; Canary 157 uses Microsoft's own Aion 1.1. Microsoft announced
Aion 1.0 at Build 2026 (2 June) and promised open weights of Aion 1.0 Instruct on Hugging Face in July 2026; as of
October 2026 they are not public (no such model in a Hugging Face search). Its licence inside Edge was not checked;
we use it only through Edge.

### How Edge proofreads: the harness

`edge_on_device_model_execution_config.pb` (protobuf, readable strings) holds the prompts Edge wraps around the model.
Proofreading, quoted:

> You are a skilled proofreader that can identify and correct grammatical errors in a given text in the
> 'GIVEN_TEXT' section. Your task is to proofread the 'GIVEN_TEXT' and output the 'PROOFREAD_TEXT'. Output ONLY the
> 'PROOFREAD_TEXT' and nothing else.

And a second, labelling prompt (one call per correction):

> You are a skilled proofreading teacher that correctly labels the correction made to the 'ORIGINAL_TEXT' for
> proofreading… 'Correcting <error> to <correction>'… Label the 'CORRECTION' from one of the following labels:
> Spelling, Punctuation, Capitalization, Preposition, Missing words, Grammar. Give the output in the following json
> format: '{"label": "<label0>"}' Output ONLY the json and DO NOT ADD ANY HEADLINE SENTENCES.

Message types: `optimization_guide.proto.ProofreaderApiRequest` / `ProofreaderApiResponse` (also `PromptApiRequest`,
`SummarizeRequest`, `WritingAssistanceApiRequest`, with the prompts of Summarizer, Writer and Rewriter).

**The model only returns the corrected text; Edge finds the corrections by comparing it with the input.** BlueEdit's
language-model checker works the same way (see proofing-internals.md), with a JSON answer that adds explanations.

### What it returns

```json
{"correctedInput": "He goes to school.", "corrections": [{"correction": "goes", "startIndex": 3, "endIndex": 5}]}
```

The W3C draft also defines `includeCorrectionTypes`, `includeCorrectionExplanations`, `correctionExplanationLanguage`
and, per correction, `types` (spelling, punctuation, capitalization, grammar…) and `explanation`. Edge 157 accepts
the options but returns neither.

### Quality

In BlueEdit's benchmark (tools/proofbench, English part: 25 grammar errors, 7 typos, 13 correct sentences): grammar
19/25, typos 5/7, **6 false alarms of 13 — 4 of them British spellings changed to American** (organised → organized,
centre → center, travelled, apologise). Mid-table among the local models; it follows American English.

### The other built-in APIs of Edge (Canary 157)

- **Prompt API** (`LanguageModel`): `availability()` → `available` (same Aion model). It takes *our* prompt with a JSON
  schema (`responseConstraint`), so it can proofread Russian too. A first try with BlueEdit's prompt: «Мы был» →
  «Мы были» (0.7 s), «сказал что» → «сказал, что» (0.8 s), "She don't" → "She does not" (0.5 s); «Я пошли» → «Мы пошли»
  (wrong: changes the meaning; this first call also took 66 s and ran on with blanks). Explanations came in English
  whatever the prompt said. Microsoft says Aion also runs on CPU. Not measured yet (issue #37).
- **Translator**: ru→en and en→ru `downloadable`; after the download, «Вчера мы долго гуляли по парку и разговаривали о
  книгах.» → "Yesterday we walked around the park for a long time and talked about books." (7.4 s with the download),
  en→ru 1.2 s, good quality (issue #39).
- **LanguageDetector**: present; returns languages with confidence and `und` (issue #40).

### Not to be confused with

- **Microsoft Editor** in `edge://settings/languages/writingAssistanceLanguages` ("Enhanced spell check", "Grammar"):
  Microsoft's *cloud* service for text fields on web pages, the one Word and Outlook use. Grammar for English only
  (for Russian the switch is greyed out). No public API.
- **Copilot** "check your work with an AI proofreader" (microsoft.com learning centre): pasting text into the Copilot
  chat. Cloud, no API.
- Browser extensions that popped up in the user's Edge (Reverso, Grammarly, LanguageTool) — not Edge's own.

## 2. Chrome

Chrome stable **154.0.8037.58**, headless with a profile of its own, a local page:

| API | Present by default |
|---|---|
| `LanguageModel` (Prompt API, Gemini Nano) | yes — `availability()` → `downloadable` |
| `Translator`, `LanguageDetector`, `Summarizer` | yes |
| `Writer`, `Rewriter`, `Proofreader` | no |

Google's docs ([proofreader-api](https://developer.chrome.com/docs/ai/proofreader-api)): Proofreader was an **origin
trial in Chrome 141–145** (a test on live sites: a site registers and gets a token that turns the API on for that site
only, for those versions), model Gemini Nano; `includeCorrectionTypes` and `includeCorrectionExplanation` not
supported; local testing via `chrome://flags/#proofreader-api`. Requirements: 22 GB free; a GPU with more than 4 GB,
or a CPU with 16 GB of RAM and 4 cores; unmetered connection.

**It runs on the CPU here, GPU or not** (measured 7 October 2026, Chrome 154.0.8037.98, i9-11900KB, 64 GB, RTX 4070):
while BlueEdit's Chrome checker proofread the British academic set (59 sentences, median 0.6 s each), `nvidia-smi`
sampled every second showed 0–83 MB of video memory in use and about 0 % load, as with Chrome idle; the GPU process was
listed with no memory of its own. A work computer showed the same in Chrome's log: "Created TensorFlow Lite XNNPACK
delegate for CPU" (XNNPACK is LiteRT's CPU back end). So a computer without a capable GPU is not why the checker fails;
the model's download (it needs Google's servers and an unmetered connection), 16 GB of RAM and 22 GB free are.

In 154 the code is still there — `chrome.dll` holds `blink::Proofreader`, `blink::Proofreader::proofread`,
`blink::Proofreader::availability`, `blink::Proofreader::GetCorrectionsTypes`,
`blink::mojom::blink::AIManagerCreateProofreaderClient`, `ProofreaderApiRequest`, `kAIProofreadingAPI`,
`kProofreaderApi`, `proofreader-api` and — new compared with Edge — **`ProofreaderSmallExpert`** and
**`proofreader_small_expert_model_component`**: a separate small "expert" model for proofreading, not only Gemini Nano.
The flag is there too: "Proofreader API — Enables the Proofreader API, allowing you to proofread a piece of text with
a built-in small expert model…". Next to it in `chrome://flags`: `#semantic-embedder-api` (semantic embeddings of
text) and `#gemma4-for-built-in-ai` ("Enables all built-in AI APIs (Prompt, Summarizer, Writer, etc.) to use the
Gemma 4 model").

How "the code is there but the feature is not" works: the program is built whole, and whether a feature shows is
decided by its switch (a feature flag). While it is off, pages see `typeof Proofreader === 'undefined'`, as if it did
not exist. Chrome's flags expire; expired flags come back with `#temporary-unexpire-flags-m153` (154 has `…-m152`
and `…-m153`).

What was tried in a separate profile — `Proofreader` never appeared:

- `--enable-blink-features=AIProofreadingAPI`
- `--enable-features=AIProofreadingAPI,ProofreaderSmallExpert --enable-blink-features=AIProofreadingAPI,AIProofreaderAPI`
- the flag `proofreader-api@1` in `Local State` (alone, Chrome removed it at start; together with
  `temporary-unexpire-flags-m152@1` and `-m153@1` it stayed, but did not help)
- `proofreader-api@1`, `semantic-embedder-api@1`, `gemma4-for-built-in-ai@1` — headless and in a normal window
- on `file://` and on `https://example.com/`.

### It works with the flags *and* Google's experiment configuration

In the user's own Chrome 154 (how to set the flags, step by step: [../proofing-setup.md](../proofing-setup.md#chrome-being-researched-not-in-blueedit-yet)), with `#proofreader-api`, `#semantic-embedder-api` and `#gemma4-for-built-in-ai` set to
Enabled and a full restart, the [playground](https://chrome.dev/web-ai-demos/proofreader-api-playground/) worked;
the model was downloaded when the button was first pressed. The difference from the test profile was not the flags
(the same three) but the **variations seed**: the experiment configuration Chrome downloads from Google's servers and
applies at the next start (`variations_*` keys in `Local State`). A fresh profile had none — and Chrome even dropped
the flags from it at start. Copying into a separate profile only the user's `variations_*` keys and the flag list
(no personal data) made `Proofreader` appear there too: `en` → `downloadable`, `ru` → `unavailable`. So the API is
turned on server side for some users on top of the flag.

**The model** is not Gemini Nano but a tiny one, downloaded into the user data folder:

```
User Data\OptGuideManifestModelfe836b7…6.6.3.1603\     117 MB
    manifest.json: "Optimization Guide On-Device Tinymodel Proofreader", version 2026.6.3.1603
    weights.bin (117 MB), cache.bin, adapter_cache.bin, encoder_cache.bin, _metadataerified_contents.json
```

Looked into on 5 October 2026: `weights.bin` starts with `LITERTLM` — a [LiteRT-LM](https://github.com/google-ai-edge/LiteRT-LM)
container (Google's open-source, Apache-2.0 runtime for on-device models), holding a TFLite prefill/decode model with
18 layers (`kv_cache_k_0` … `_17`) and its chat template in readable text: the user's text followed by ` <ctrl1>`, then
the model's answer — no instructions, a model trained for this one task. So it can probably run without Chrome, in
LiteRT-LM with this template; **not tried yet**, nor whether the answer is the corrected text (as with Edge, which
compares the texts). The weights are Google's, shipped as part of Chrome with no licence of their own: fine to try on
one's own computer, not to ship in BlueEdit. Next to it, `OptGuideOnDeviceModel5.8.8.1141` is Gemini Nano
(`BaseModelSpec` "v3Nano", 4.3 GB, `weights.bin` with no readable header), the model of the Prompt API.

**It returns correction types** (Edge does not): with `includeCorrectionTypes: true`

```json
{"correction": "is", "startIndex": 5, "endIndex": 8, "types": ["grammar"]}
{"correction": "random", "startIndex": 11, "endIndex": 17, "types": ["spelling"]}
{"correction": "doesn't", "startIndex": 4, "endIndex": 9, "types": ["grammar", "punctuation"]}
```

`includeCorrectionExplanations: true` is accepted but gives no `explanation`. First tries: "This are a radnom text
with a few typicla typso." → "This is a random text with a few typical typos." (2.9 s); "She don't like coffee, and
their going home." → "doesn't", "they're" (1.7 s); British "organised", "centre" left alone — but it **removed the
final full stop** of "We organised the meeting at the centre." (a false alarm). On the playground's text it also
lower-cased "the Proofreader API" to "The proofreader API". `create()` took 3.6 s with the download. Russian:
`NotSupportedError: The requested language options are not supported.` English only.

In BlueEdit since #42 (`BrowserProofreader.Chrome`): a hidden Chrome with `%LOCALAPPDATA%\BlueEdit\chrome-profile`,
the user's `variations_*` keys and flag list copied into its `Local State` before every start (only those). Measured
on 6 October 2026 on the British academic set: 21 of 24 grammar errors (20 with the right fix), 2 of 3 punctuation
errors noticed, 26 false alarms of 24 correct sentences — 20 of them the final full stop dropped; with the stop put
back (`KeepFinalStop`) 7: "committee have" / "team were" made singular, "had" dropped twice, "student handbook"
capitalised, once "The" → "Few". 0.65 s a sentence. Headless Chrome worked (the earlier tries failed for want of the
configuration, not for headless).

**Without Chrome** (#104, 6 October 2026): the model runs in Google's LiteRT-LM (`pip install litert-lm`, 0.17.1).
Found on the way:
- `litert-lm describe` reads the file: text only, no thinking, sampler temperature 0, top-k 3. `litert-lm run model
  --prompt "She don't like coffee, and their going home."` → "She doesn't like coffee, and they're going home." (1.3 s
  with loading). The engine goes by the extension: it opens a copy named `.litertlm`, not Chrome's `weights.bin`.
- Its tools keep their state in `~/.litert-lm` (`os.path.expanduser`); with `USERPROFILE` (or `HOME`) pointed elsewhere
  they leave the user's home alone.
- `litert-lm serve` (an OpenAI-compatible server, a new conversation per request) found 14 of 24 errors on the British
  set with or without `temperature: 0`. The Python API with the bare prompt (`text <ctrl1>`, the template's form) gave
  the same for the likeliest answer — "effect" → "affect" right, "weather", "raised" missed.
- **Chrome samples**: two runs of the same set in Chrome differed in 6 of 59 sentences ("findings" rewritten in one
  run, "from" dropped in the other; 7 and 4 false alarms). Sampling in LiteRT-LM (top-k 3) with a fixed seed: 15–17 of
  24 at temperature 1.0 over three seeds, 17 at 0.5 — still below Chrome's 21; what else Chrome does differently was
  not found. The GPU backend would not start for this model (`Failed to create LiteRT-LM engine`); it runs on the CPU
  (XNNPACK).
- BlueEdit's checker (`ChromeModelChecker`) runs `tools/chrome-model/proofread.py` (built into the editor) in the
  virtual environment `install.ps1 -ChromeModel` makes: the model loaded once, lines of JSON in and out, top-k 3 at 0.5
  with seed 1: 17 of 24, 2 false alarms, 0.3 s.

## 3. Microsoft Word (Office 16, COM)

- Word 16.0 through COM: start 1.6 s, 5 words 0.5 s. LanguageID 1049 = ru-RU, 2057 = en-GB.
- **Spelling works**: «превет» → привет, преет, поревёт, прервёт, поревет; "color" flagged in en-GB.
- **Grammar for Russian does not**: `Range.GrammaticalErrors` finds 1 error in "He go to school every days." (the whole
  sentence) but 0 in «Я пошли домой вчера.» and «Мы был в магазине.»; 1 in «Он сказал что придёт.» (punctuation).
  `Application.CheckGrammar()` returns True for all three. Word itself does not underline «Я пошли» either.
- Installed proofing tools: Russian `MSSP7RU.LEX` (spelling) / `MSGR8RU.LEX` (grammar), English `MSSP7EN.LEX` /
  `MSGR8EN.LEX`; `CheckGrammarWithSpelling` and `CheckGrammarAsYouType` on; Russian writing style "Грамматика и
  стиль". So nothing is missing: the classic offline Russian grammar module mostly checks punctuation. The modern
  grammar is Microsoft Editor in the cloud, which has no public API (the Word Online endpoints were not used: terms
  of use).
- What BlueEdit does with Word (OfficeSpell.cs): a background thread in a single-threaded apartment
  (`ApartmentState.STA`) owns `Word.Application`; a hidden document from `Documents.Add(..., false)`; words in batches
  of up to 300 per language; suggestions from `GetSpellingSuggestions`; it quits only a Word it started itself.

## 4. Windows spell checking

- COM `SpellCheckerFactory`, CLSID `7AB36653-1796-484B-BDFA-E74F1DB7C1DC`; `ISpellCheckerFactory` IID
  `8E018A9D-2415-4677-BF08-794EA61F94BB`.
- `ISpellChecker`: the IID first tried (`B6FD0B71-…`) gave `E_NOINTERFACE` (0x80004002); the one that works is
  `E7ED1C71-87F7-4378-A840-C9200DACEE47`.
- Supported tags here: en-029 … en-ZW, ru-MO, ru-RU. The user's languages: `HKCU\Control Panel\International\User Profile`,
  value `Languages` (here `en-GB`, `ru`), filtered by `IsSupported`.
- Windows has no grammar API.

## 5. LanguageTool (free, local)

- `OpenJDK21U-jre_x64_windows_hotspot_21.0.12.1_1.zip` (Adoptium, 46.7 MB, SHA-256 checked) and
  `LanguageTool-stable.zip` (240 MB, LanguageTool 6.6); 531 MB unpacked.
- `java -Xmx512m -cp languagetool-server.jar org.languagetool.server.HTTPServer --port <port>`; ready in 6–7 s;
  640–710 MB of memory.
- `POST /v2/check` with `text`, `language`; `disabledCategories=TYPOS,BRE_STYLE_OXFORD_SPELLING` (typos are the
  spelling provider's job; the Oxford category flagged "organised"), `disabledRules=WHITESPACE_RULE,
  COMMA_PARENTHESIS_WHITESPACE,UPPERCASE_SENTENCE_START`.
- **`localhost` cost 2 s per request** (2071–2134 ms: Windows tries IPv6 first), `127.0.0.1` 58–115 ms.

## 6. Language models through llama.cpp: observations

- With too short a prompt ("you are a proofreader, return JSON with the corrected text") YandexGPT 5 Lite
  **translated** a Russian sentence into English instead of correcting it. With the editor's full prompt (languages,
  "do not rephrase", "do not translate") it corrects. The details of the prompt matter.
- Small models sometimes answer something else: Phi-4-mini translated English sentences into Russian, Gemma 3 4B
  returned unrelated Russian text. The editor's rewrite guard drops such answers.
- An Edge-style prompt (the corrected text only) did worse on these models than JSON with explanations.
- The Vulkan build of llama.cpp on NVIDIA: first answer 16 s (shader compilation), CUDA 0.4 s.

## 7. Other sources looked at

- Hunspell (WeCantSpell.Hunspell 7.0.1, LibreOffice dictionaries) — for a Linux port (issue #23).
- Harper (Apache-2.0, English, `harper-ls` / `harper-cli --format json`) (issue #24).
- Yandex.Speller: free cloud, 10,000 requests and 10 M characters a day, requires the credit "Проверка правописания:
  Яндекс.Спеллер" (issue #27).
- LanguageTool cloud: free 20 requests and 75,000 characters a minute; Premium about €60 a year.
- Not usable: Grammarly (SDK closed 10 January 2024), ORFO (no SDK), Phi Silica (being removed January 2027),
  Glavred and Litera5 (paid).
