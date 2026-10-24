# Installing and setting up spelling and grammar checking

*По-русски: [proofing-setup.ru.md](proofing-setup.ru.md)*

How to install everything BlueEdit checks spelling and grammar with — by script or by hand — and how to make sure it
works. How to use it: [proofing.md](proofing.md); how it is built: [tech/proofing-internals.md](tech/proofing-internals.md);
the research into Edge, Chrome and Word: [tech/browser-ai-research.md](tech/browser-ai-research.md).

All of it is free, without subscriptions, runs on your computer and needs no administrator rights — except a cloud
service you may choose instead of a model here, which takes a key and is paid by use.

## The parts

| Part | For | Needed | Size | Installed by |
|---|---|---|---|---|
| Windows languages | spelling | yes (already there) | — | Windows Settings |
| Microsoft Word | spelling with Office's dictionaries (option) | no | — | Office |
| Java 21 + LanguageTool | rule-based grammar | no | ~540 MB | `install.ps1 -LanguageTool` |
| llama.cpp | runs language models | no | 35–720 MB | `install.ps1 -Llm` |
| a `.gguf` model | grammar by a neural network | for llama.cpp | 2.5–9 GB | `install.ps1 -Models …` |
| Edge Canary | Edge's proofreader (English) | no | ~1 GB + 1.2 GB model | `install.ps1 -EdgeCanary` |
| Harper | rule-based grammar for English, British or American | no | 60 MB | `install.ps1 -Harper` |
| LiteRT-LM | Chrome's proofreader model without Chrome (English) | no | ~200 MB + Python | `install.ps1 -ChromeModel` |

Which to choose: see "Comparison: which is more accurate" in [proofing.md](proofing.md). In short: **LanguageTool +
llama.cpp + YandexGPT 5 Lite** is the fullest setup; without a GPU, LanguageTool.

## Quickest: from the editor

**Tools → Set up grammar checking…** gets one checker working, with nothing to know beforehand:

| Choice | What happens |
|---|---|
| A language model here | a list of the tested models with size and licence (Qwen3 8B: Apache 2.0, YandexGPT 5 Lite: Yandex's own, Gemma 3: Google's terms, Phi-4-mini: MIT); llama.cpp for this computer's video card (CUDA, Vulkan, or the processor when there is none) and the model are installed, the model chosen and grammar checking turned on |
| Ollama | the models of [Ollama](https://ollama.com) running on this computer (127.0.0.1:11434, or `OLLAMA_HOST` as Ollama reads it) are listed and one is chosen; embedding models are left out. Not running or no model yet: the steps (install, `ollama pull gemma3:4b`, choose again). Nothing is installed by BlueEdit, paragraphs stay on this computer |
| LM Studio | the same for [LM Studio](https://lmstudio.ai)'s server (127.0.0.1:1234; turned on in its Developer tab or with `lms server start`) |
| Another model server | an address (llama-server elsewhere, a cloud service), the model's name there and a key: nothing is installed, paragraphs are sent there |
| LanguageTool | for those who want rules for 31 languages without a video card: Java 21 and LanguageTool, ~540 MB |
| Harper | rules for English, ~60 MB |
| **Remove what was installed here...** (a button under the list, Tab) | what the editor installed in its own folders, each with its size, nothing ticked: llama.cpp, each model, LanguageTool with its Java, Harper, the spelling dictionaries. A ticked checker is turned off first, then deleted; a folder the settings point elsewhere (your own LanguageTool) is never offered |

All of it in the background (the steps in the status line, typing goes on), from the sources and with the checksums of
the scripts below, into the same folders. The same in a shell, e.g. on a server without the editor's screen:

```
be --install-proofing                 # what can be installed, and what already is
be --install-proofing qwen3-8b,harper # those parts
```

**Spelling dictionaries** (Hunspell, the spelling on Linux and macOS): **Tools → Get spelling dictionaries…** lists
LibreOffice's for eleven languages with their sizes (0.6–9.1 MB) and downloads the ticked ones into BlueEdit's own folder
(`~/.local/share/BlueEdit/hunspell`), no root needed; the new languages join the spelling languages. A language the
system already has (`apt install hunspell-ru`) is marked "in the system" and not downloaded again; ticked at first are
the spelling languages with no dictionary anywhere. In a shell: `be --install-proofing ru-RU,en-GB`.

On Linux llama.cpp needs the system's OpenMP library (`libgomp.so.1`; Debian and Ubuntu: `sudo apt install libgomp1`):
a computer without it gets that said at once. Edge Canary and Chrome's model stay with the scripts.

## Quick: by script

The scripts are in `tools\proofing-setup\` of the repository. In PowerShell:

```powershell
cd tools\proofing-setup
.\install.ps1 -All          # LanguageTool, llama.cpp, the recommended model (YandexGPT), Edge Canary, Harper
.\check.ps1 -Run            # what is installed + a test check of a sentence with a mistake
```

Part by part:

```powershell
.\install.ps1 -LanguageTool                          # Java 21 and LanguageTool
.\install.ps1 -Llm                                   # llama.cpp (+ YandexGPT when there is no model yet)
.\install.ps1 -Models yandexgpt,gemma3-12b           # models (llama.cpp too, if missing)
.\install.ps1 -EdgeCanary                            # Edge Canary
.\install.ps1 -Harper                                # Harper (harper-ls)
.\install.ps1 -ChromeModel                           # LiteRT-LM for Chrome's model without Chrome (Python 3.10+)
.\install.ps1 -List                                  # the models the script knows, and which are installed
.\install.ps1 -Llm -Force                            # reinstall (e.g. with -LlamaBuild latest)
```

Options:

| Option | What it does |
|---|---|
| `-Gpu auto\|cuda\|vulkan\|cpu` | the llama.cpp build. `auto`: CUDA when an NVIDIA driver is present (CUDA 13.4 with driver 580+, else 12.4 on Windows and 12.8 on Linux), otherwise Vulkan when Vulkan sees a video card, else the CPU build (`be --vulkan-devices` lists what it sees) |
| `-LlamaBuild b11371\|latest` | the llama.cpp version; the default is the one BlueEdit was tested with |
| `-Root <folder>` | where to install; default `%LOCALAPPDATA%\BlueEdit` (then nothing needs setting in BlueEdit) |
| `-Force` | reinstall what is installed |

What the script guarantees:

- **Official sources only**: adoptium.net (Java), languagetool.org, llama.cpp's GitHub releases, Hugging Face (models),
  Microsoft's site (Edge Canary).
- **Integrity**: for Java, llama.cpp and the models, the SHA-256 their publisher gives (Adoptium, GitHub, Hugging Face);
  on a mismatch the file is deleted and the install stops. LanguageTool publishes no checksum: the archive is checked
  for the server. The Edge Canary installer is checked for Microsoft's digital signature.
- **Resuming**: an interrupted download continues where it stopped (files in `%LOCALAPPDATA%\BlueEdit\downloads`,
  which can be deleted after installing).
- **Safe to run again**: what is installed is skipped. If BlueEdit is running and holds a file to be replaced, the
  script stops and asks to close it.

### Linux (and WSL)

`tools/proofing-setup/install.sh` does the same on Linux, into `~/.local/share/BlueEdit` (`$XDG_DATA_HOME/BlueEdit`),
where the Linux build of BlueEdit looks; it needs `curl`, `tar` and `python3`, and no root:

```bash
cd tools/proofing-setup
./install.sh --all                                   # LanguageTool, llama.cpp, the recommended model (YandexGPT), Harper
./install.sh --harper                                # Harper (harper-ls)
./install.sh --languagetool                          # Java 21 and LanguageTool
./install.sh --llm --models yandexgpt,gemma3-12b     # llama.cpp and models
./install.sh --list                                  # the models it knows, and which are installed
```

Options as on Windows: `--gpu auto|cuda|vulkan|cpu` (`auto`: CUDA when `nvidia-smi` works — in WSL too, with the
Windows NVIDIA driver; else Vulkan when `vulkaninfo` lists a video card, not only llvmpipe; else the CPU build), `--llama-build`, `--root`, `--force`.
The same sources and SHA-256 checks; llama.cpp's Linux archives run as they are unpacked (their libraries sit next to
`llama-server`). Spelling on Linux is Hunspell with the distribution's dictionaries (`sudo apt install hunspell-ru
hunspell-en-gb`) or those of Tools → Get spelling dictionaries; Edge's proofreader is Windows-only.

## By hand

Everything goes into one folder, by default `%LOCALAPPDATA%\BlueEdit`:

```
%LOCALAPPDATA%\BlueEdit\
    jre\bin\java.exe                    Java 21
    languagetool\languagetool-server.jar
    llm\bin\llama-server.exe            llama.cpp
    llm\models\*.gguf                   models
    edge-profile\                       made by BlueEdit itself for Edge's proofreader
```

Other places are set with `languageToolDir` (the folder with `jre\` and `languagetool\`), `llmDir` (the folder with
`bin\` and `models\`), `edgePath` (the path of `msedge.exe`).

### Windows languages (spelling)

Settings → Time & language → Language & region → Add a language. The basic language pack is enough for spelling.
English: `en-GB` and `en-US` are different dictionaries; add the one you write. In BlueEdit: **Tools → Spelling
languages…** (`auto` takes the languages added in Windows).

### Microsoft Word (spelling with Office's dictionaries)

Needs Word (Office 2016 or later, Microsoft 365). Proofing languages are set in Office itself (File → Options →
Language); Russian needs the Russian proofing tools. In BlueEdit: **Tools → Spelling provider… → Office**. Word's
grammar is not used for Russian: through COM it finds almost only punctuation (details in the research).

### Hunspell dictionaries (spelling on Linux and macOS; optional on Windows)

Easiest: **Tools → Get spelling dictionaries…** (above), on any system. Linux: also the distribution's packages,
e.g. Debian/Ubuntu `sudo apt install hunspell-ru hunspell-en-gb hunspell-en-us` (they go to `/usr/share/hunspell`). BlueEdit also looks in `~/.local/share/hunspell`, `/usr/share/myspell`,
`/usr/local/share/hunspell`, `/opt/homebrew/share/hunspell` and `~/Library/Spelling`. Windows: put a pair of files —
`ru_RU.dic` + `ru_RU.aff`, `en_GB.dic` + `en_GB.aff` — into `%LOCALAPPDATA%\BlueEdit\hunspell`; LibreOffice's are in
[LibreOffice/dictionaries](https://github.com/LibreOffice/dictionaries) (folders `ru_RU` and `en`); then **Tools →
Spelling provider… → Hunspell**. A language tag finds its file by name: `ru-RU` → `ru_RU.dic`, a bare `ru` → the first
`ru_*.dic`. The Hunspell code (WeCantSpell.Hunspell) is under MPL 1.1 / GPL 2.0 / LGPL 2.1, inherited from Hunspell;
the dictionaries have their own licences, in their packages.

### LanguageTool

1. **Java 21 (JRE)**: [adoptium.net](https://adoptium.net/temurin/releases/?version=21&package=jre&os=windows&arch=x64) →
   Windows x64, JRE, `.zip` (not `.msi`). Unpack so that you get `BlueEdit\jre\bin\java.exe` (the contents of the
   archive's `jdk-21…-jre` folder go into `jre\`).
2. **LanguageTool**: <https://languagetool.org/download/LanguageTool-stable.zip> (~240 MB). Unpack so that you get
   `BlueEdit\languagetool\languagetool-server.jar` (the contents of `LanguageTool-6.x` go into `languagetool\`).
3. In BlueEdit: **Tools → Grammar checking… → LanguageTool**. It starts on first use (5–12 s), takes about 0.7–1 GB of
   memory, and stops with the editor.

To try it by hand (as BlueEdit does):

```powershell
cd $env:LOCALAPPDATA\BlueEdit\languagetool
..\jre\bin\java.exe -Xmx768m -cp languagetool-server.jar org.languagetool.server.HTTPServer --port 8081
# in another window:
Invoke-RestMethod http://127.0.0.1:8081/v2/check -Method Post -Body @{ text = 'Мы был в магазине.'; language = 'ru-RU' }
```

Use `127.0.0.1`, not `localhost`: with `localhost` Windows tries IPv6 first and loses 2 seconds on every request.

### llama.cpp

Releases: <https://github.com/ggml-org/llama.cpp/releases> (builds come out several times a day; BlueEdit was tested
with `b11371`). Which build:

| GPU | Archives |
|---|---|
| NVIDIA, driver 580 or later | `llama-bNNNNN-bin-win-cuda-13.4-x64.zip` **and** `cudart-llama-bin-win-cuda-13.4-x64.zip` |
| NVIDIA, older driver | `…-cuda-12.4-x64.zip` and `cudart-…-cuda-12.4-x64.zip` |
| AMD, Intel, any other | `llama-bNNNNN-bin-win-vulkan-x64.zip` (the first answer is slow: it compiles shaders) |
| no GPU | `llama-bNNNNN-bin-win-cpu-x64.zip` (slow) |

The NVIDIA driver version: `nvidia-smi`. Unpack everything into `BlueEdit\llm\bin\`, so that `llama-server.exe` is
there (for CUDA together with `cudart64_*.dll` and `cublas*.dll` from the second archive). Every file on the release
page has its SHA-256 (`digest`); compare with `Get-FileHash <file>`.

### Models

`.gguf` files go into `BlueEdit\llm\models\`. Tried (Q4_K_M quantization):

| Script key | File | Where (huggingface.co/…) | Video memory |
|---|---|---|---|
| `yandexgpt` | `YandexGPT-5-Lite-8B-instruct-Q4_K_M.gguf` | `yandex/YandexGPT-5-Lite-8B-instruct-GGUF` | ~6 GB |
| `gemma3-12b` | `google_gemma-3-12b-it-Q4_K_M.gguf` | `bartowski/google_gemma-3-12b-it-GGUF` | ~8 GB |
| `qwen3-8b` | `Qwen3-8B-Q4_K_M.gguf` | `Qwen/Qwen3-8B-GGUF` | ~6 GB |
| `qwen3-14b` | `Qwen_Qwen3-14B-Q4_K_M.gguf` | `bartowski/Qwen_Qwen3-14B-GGUF` | ~10 GB |
| `gemma3-4b` | `google_gemma-3-4b-it-Q4_K_M.gguf` | `bartowski/google_gemma-3-4b-it-GGUF` | ~3 GB |
| `phi4-mini` | `microsoft_Phi-4-mini-instruct-Q4_K_M.gguf` | `bartowski/microsoft_Phi-4-mini-instruct-GGUF` | ~3 GB |

Download link: `https://huggingface.co/<repository>/resolve/main/<file>`. The file's SHA-256 is on its Hugging Face
page (Files → the file → "SHA256"). Any other `.gguf` model works too, but models differ a lot as proofreaders: run
it through the benchmark (`tools\proofbench\bench.py`).

The model has to fit in video memory **together with what is already there** (browser, games, another model). If it
does not, llama.cpp does not start and the status line says `GRAMMAR LLM failed`.

In BlueEdit: **Tools → Grammar checking… → Language model**; the model: **Tools → Grammar model…** (Auto takes the
best one installed: YandexGPT, Gemma 3 12B, Qwen3 8B, Qwen3 14B, Gemma 3 4B, Phi-4-mini).

### Edge Canary (Edge's proofreader)

1. The installer from Microsoft: [microsoft.com/edge/download/insider](https://www.microsoft.com/edge/download/insider) →
   Canary. It installs for the current user into `%LOCALAPPDATA%\Microsoft\Edge SxS\`, next to the usual Edge,
   without touching it. (winget's Canary package failed its hash check when this was written, so the script takes
   Microsoft's installer and checks its signature.)
2. No flags to turn on: BlueEdit starts a hidden Canary itself with the switches needed
   (`--enable-features=AIProofreadingAPI --enable-blink-features=AIProofreaderAPI`) and a profile of its own,
   `BlueEdit\edge-profile`. Your Canary and its profile are not touched.
3. In BlueEdit: **Tools → Grammar checking… → Edge**. On first use Edge downloads its model (~1.2 GB, about a minute).

Microsoft's requirements: a GPU with 5.5 GB or more, 20 GB free on the drive with the profile (below 10 GB Edge
deletes the model), an unmetered connection. Whether the computer qualifies: `edge://on-device-internals` in Canary
(Device performance class High or above). English only, American rules. The usual Edge 154 has the proofreader object
but never answers; BlueEdit reports that after 30 s.

### Chrome's proofreader

In Chrome 154 the proofreader (Proofreader API, a "small expert model", 117 MB) is off by default, after its origin
trial in Chrome 141–145. Flags turn it on. They apply to every profile of one Chrome installation (one user data
folder); another installation needs them set separately.

1. In Chrome's address bar type `chrome://flags` and press Enter. The "Experiments" page opens.
2. In **Search flags** at the top type `proofreader`. **Proofreader API** (`#proofreader-api`) shows up. The address
   `chrome://flags/#proofreader-api` opens it directly, highlighted.
3. In the drop-down list to the right of the flag pick **Enabled** (instead of Default).
4. Two more were enabled the same way — whether they are needed is not known, but with them it works:
   - `chrome://flags/#gemma4-for-built-in-ai` — **Gemma 4 for Built-in AI** (the Gemma 4 model for Chrome's built-in APIs);
   - `chrome://flags/#semantic-embedder-api` — **Semantic Embedder API**.
5. If the flag is not listed (it expired): enable `chrome://flags/#temporary-unexpire-flags-m153` (**Temporarily
   unexpire M153 flags**), restart Chrome and go back to step 2.
6. Press **Relaunch** at the bottom of the page. If Chrome keeps running in the background (an icon in the tray by the
   clock), close that too: right-click → Exit, then start Chrome again.
7. Open <https://chrome.dev/web-ai-demos/proofreader-api-playground/> and press **Proofread**. The first press
   downloads the model (117 MB), then the corrected text appears. With **Include correction types** ticked, with the
   kinds of errors.

To check in the console (F12 → Console): `typeof Proofreader` should give `"function"`, and
`await Proofreader.availability({expectedInputLanguages: ["en"]})` `available` or `downloadable`.

Besides the flags the proofreader needs the experiment configuration Chrome gets from Google in a normal profile: a new
empty profile does not have it. So BlueEdit (**Tools → Grammar checking → Chrome**) starts a hidden Chrome with a
profile of its own (`%LOCALAPPDATA%\BlueEdit\chrome-profile`) and, before every start, copies into it from your
Chrome's `Local State` only the flag list and the `variations_*` keys (that configuration) — no history, cookies,
passwords or anything else. The model (117 MB) downloads into that profile on first use. English only. To undo: the
same flags back to **Default**, then Relaunch. Details in the research.

## Checking everything

```powershell
.\check.ps1         # what is installed: Windows languages, Word, LanguageTool, llama.cpp and models, GPU, Edge Canary
.\check.ps1 -Run    # and starts LanguageTool and a model and checks the sentence «Мы был в магазине…»
```

`-Model <file.gguf>` checks a particular model. Sample output:

```
== LanguageTool: "Мы был в магазине и купили хлеб."
  [ok]   answer                 in 5.1 s:
         Мы был: Неверное спряжение глагола или неверное местоимение
== language model YandexGPT-5-Lite-8B-instruct-Q4_K_M.gguf: "Мы был в магазине и купили хлеб."
  [ok]   loaded                 in 3.6 s
  [ok]   answer                 in 0.4 s: Мы были в магазине и купили хлеб.
```

## Updating and removing

- Update: `.\install.ps1 -LanguageTool -Force`, `.\install.ps1 -Llm -LlamaBuild latest -Force` (run `check.ps1 -Run`
  after updating llama.cpp).
- Remove: close BlueEdit and delete `jre`, `languagetool`, `llm` (or single models in `llm\models`), `edge-profile`,
  `downloads` in `%LOCALAPPDATA%\BlueEdit`. Nothing stays in the registry or the system. Edge Canary uninstalls like
  any program (Settings → Apps).

Space taken (on a test machine): `jre` 146 MB, `languagetool` 391 MB, `llm\bin` 712 MB (CUDA; Vulkan ~35 MB),
models 2.3–8.4 GB each, `edge-profile` ~1.7 GB.

## When it does not work

The status line shows which checkers work and which do not: `GRAMMAR LT+LLM`, `GRAMMAR LT+LLM starting`,
`GRAMMAR LT+LLM, EDGE failed` (Edge did not start). The reason is said once in the message line, e.g.
`[Grammar (LanguageTool): LanguageTool not installed in …]`.

| Message | Cause and what to do |
|---|---|
| `Grammar (LanguageTool): LanguageTool not installed in …` | no `jre\bin\java.exe` or `languagetool\languagetool-server.jar` at that path: `install.ps1 -LanguageTool`, `check.ps1` |
| `Grammar (Language model): llama.cpp or a model not found in …` | no `llm\bin\llama-server.exe` or no model in `llm\models`: `install.ps1 -Llm` |
| `Grammar (Language model): language model did not start: …` | not enough video memory (close what takes it, or pick a smaller model in **Tools → Grammar model…**); for CUDA the `cudart-…zip` was not unpacked; the NVIDIA driver is too old (take CUDA 12.4 or Vulkan: `install.ps1 -Llm -Gpu vulkan -Force`) |
| `Grammar (Edge): Microsoft Edge not found` | no Edge Canary (nor Edge): `install.ps1 -EdgeCanary`, or a wrong `edgePath` |
| `Grammar (Edge): … has no proofreader` or `… does not answer (try Edge Canary)` | an Edge without a working proofreader (Edge 154: the object is there but does not answer), or an update removed it: install Canary or point `edgePath` to another build |
| `Grammar (Edge): … unavailable on this computer` | the computer does not meet Microsoft's requirements (`edge://on-device-internals`) |
| `starting` for long | first start: LanguageTool 5–12 s, a model 3–10 s, Edge's proofreader downloads its model on first use (~1 min) |
| the model's first answer is very slow (10–20 s) | the Vulkan build compiles shaders on first use; on NVIDIA the CUDA build is faster |
| the model answers with a translation or a rewrite | BlueEdit's prompt forbids it and rewrites are dropped; with a too short prompt YandexGPT does translate into English; a model of your own may not suit: benchmark it |
| `Settings: … set twice` | a setting is in `settings.json` twice: keep one |
