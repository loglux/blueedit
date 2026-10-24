# The Windows installer (`tools/installer`)

Russian: [installer.ru.md](installer.ru.md).

Besides the zip (one self-contained `be.exe` and the setup scripts), each release has
`blueedit-<version>-win-x64-setup.exe`, made by [Inno Setup 6](https://jrsoftware.org/isinfo.php) from
`tools/installer/blueedit.iss` (`tools/installer/build.ps1`; `.\tasks.ps1 installer` locally, `release.yml` for a
tag). Added on 23 October 2026.

## What it does

| | |
|---|---|
| Where | `%LOCALAPPDATA%\Programs\BlueEdit`: `be.exe`, `tools\proofing-setup`, `tools\lsp-setup`, `LICENSE.txt`, `THIRD-PARTY-NOTICES.md` |
| Always | Start menu shortcut; Programs and Features entry (uninstaller); App Paths (Win+R `be`); Explorer's "Open with" list for `.txt`, `.md`, `.markdown`, `.log`, `.json`, `.ini`, `.py`, `.cs` |
| Ticked by default | `be` in the user's PATH; "Open with BlueEdit" on every file in Explorer; the formula pictures module (`mathimages`, ~14 MB, inside the installer: nothing downloaded) |
| Not ticked | a desktop shortcut; the language servers for code |
| Grammar checking (a page of its own) | nothing chosen: Harper, LanguageTool, a language model here (one of five, by the video card) or a model server (address, model, key) |
| Languages | English and Russian, by Windows' interface language (asked when it is neither) |

A checker, model or code tool that is chosen is downloaded at the end by `be.exe` itself — `be --install-proofing
harper,qwen3-8b`, `be --install-tools pyright` — in a console window of its own, where the downloads show, and the
grammar checkers chosen are turned on (`be --grammar harper,llm`) once they are there. That is
the same code as **Tools → Set up grammar checking** and **Tools → Code tools**: the same sources and SHA-256 checks
(`ProofingTools`, `CodeTools`), the same folders. If one fails, the window waits for a key so what failed can be read,
and the installer says the editor itself is installed and those menus can try again. `InstallerTests` keeps the names
the installer offers equal to `ProofingTools.Models` and `CodeTools.All`, each with its words in both languages, and
the video memory a model's line names equal to what the editor checks.

### The grammar page

One page after the tasks; its heading says nothing on it is final — all of it can be changed later in the editor.
Three parts:

- **Rules, no video card needed**: Harper (English), LanguageTool (31 languages) — tick either or both.
- **A language model here** — one of: none (the default) or the five models. The group's
  line names the video card and its memory. A model the card has too little memory for is greyed, with what it needs
  ("needs ~8 GB of video memory"); with no card all five are. The best that fits by the benchmark (the order Auto
  uses) says "recommended"; it is not chosen for the user, as a model is gigabytes.
- **Or a model somewhere else**, in the same choice of one: OpenAI, Claude (Anthropic), Gemini (Google), DeepSeek, another
  service with a key; Ollama, LM Studio, a llama-server of the user's own.
- **The line under the list** says what the highlighted line is: what the checker is good at, its licence, what it
  needs; for the server, that paragraphs are sent there.

The card is asked of `be.exe` itself, taken out of the installer into its temporary folder when the user leaves the
tasks page: `be --video-card` prints the card ("card 12282 NVIDIA GeForce RTX 4070", or "card none"), "fits KEY" or
"needs KEY GB" for each model, and "recommended KEY". The card is the one the editor already finds for llama.cpp
(nvidia-smi, else Vulkan in a process of its own, `ProofingTools`), with its memory: nvidia-smi's `memory.total`, or
Vulkan's largest device-local heap. An integrated card counts as none: here Vulkan reports the Intel UHD's memory as
32 GB — the computer's, which it borrows — and a model on it runs about as slowly as on the processor. A card sold as
8 GB reports a little less (8188 MB), so a GB counts as 1000 MB. When the card could not be asked, every model may be
chosen, as in the editor.

**A model somewhere else** brings a second page: the address, offered for each (Ollama's `http://127.0.0.1:11434/v1`,
LM Studio's `…:1234`, llama-server's `…:8080`, each service's own), and for a service that takes one, the key (shown as
dots). Nobody types a model's name: a cloud service gets the one the Obsidian plugin's benchmark recommends
(gpt-5.4-mini, claude-haiku-5-5, gemini-3.5-flash-lite, deepseek-flash); a server of the user's own none, and the editor
takes the model it lists (`LlmServer`: the first, the only one for llama-server); **Tools → Grammar model** chooses
another from the server's list. The key is optional: left empty, nothing is asked, and it can be given later in the
editor. A key typed is checked on Next: `be --check-model-server` asks the service for its models (free, a few seconds)
and says what it found ("api.anthropic.com did not take the key: invalid x-api-key", no answer, …), asking whether to
keep it anyway (No, the default: back to fix it) — the installer may run without a network. The model and the address
are not checked there: what a server has is for the editor's set-up. `be --model-server ADDRESS MODEL` writes them
into the settings as **Tools → Set up grammar checking → Another model server** does (which checks the same way, the
model too: "has no model …; it has: …"); the key reaches it in the environment
(`BLUEEDIT_LLM_KEY`, set in the installer's own process just for that call), never on a command line, where other
programs and logs would see it, and goes to `llm-key` encrypted for the user (DPAPI; `proofing-internals.md`). The
ready page lists the choices, the server by address and model, never the key. Under the fields a line a sentence:
the label's own wrapping broke Russian words in two in Windows Sandbox (Inno's own Russian texts too: "опц/ии"; not
checked on a Russian Windows).

Installing again over it (a newer version) keeps the choices made last time (Inno's `UsePreviousTasks`); unticking
PATH takes the folder out of PATH. **Tools → Command be in any console** in the editor changes the same PATH entry, so
the two agree.

Uninstalling removes the program, PATH entry, shortcuts and registry keys, and asks whether to remove what the editor
downloaded too (`%LOCALAPPDATA%\BlueEdit`: checkers, models, code tools; **No** is the default, also when silent —
models are gigabytes, slow to fetch again). Settings and the user dictionary (`%APPDATA%\blueedit`) stay.

Silent install, e.g. for scripts: `blueedit-…-setup.exe /VERYSILENT /SUPPRESSMSGBOXES /TASKS="path,openwith"
/GRAMMAR=harper,yandexgpt` (the listed tasks are the ones chosen, names in `blueedit.iss`; `/GRAMMAR` takes
`be --install-proofing`'s names and turns them on; no video card check, and no model server: its key would be on the
command line).

## Why this way

- **Inno Setup, not MSI or MSIX.** A tasks page with groups and one-of-several choices (the models), running a program
  of our own at the end and Pascal code for PATH are built in; per-user installs without administrator are its
  ordinary case; it is free and its compiler is one program (`ISCC.exe`). MSI (WiX) is what companies deploy through
  group policy or Intune, but a custom page there is much more work; nobody has asked for it yet. MSIX installs only
  signed packages, and `be.exe` is not signed.
- **Per user, no administrator.** Everything the editor downloads (checkers, models, code tools) lives in the user's
  `%LOCALAPPDATA%\BlueEdit` anyway; an all-users install would put the program in Program Files and the checkers in
  the administrator's profile. PATH is the user's (`HKCU\Environment`), kept as `REG_EXPAND_SZ` so its `%variables%`
  stay.
- **Downloads by the editor, not by the installer.** The installer stays small (one `be.exe`, compressed) and works
  offline when nothing is ticked; and there is one place that knows the sources, versions and checksums. The
  PowerShell scripts are installed too (`install.ps1 -EdgeCanary`, `-ChromeModel` have no counterpart in `be.exe`).
- **Nothing large ticked by default.** Spelling works at once with Windows' own; what to add depends on the languages
  and the video card, and models are gigabytes.
- **A page of its own for grammar**, not the tasks list it began in: the tasks page cannot grey a line or say why,
  nor ask for an address and a key, and a model offered to a computer without a video card only wastes gigabytes.
- **Not signed**, like the zip: SmartScreen warns on a new download until the file has a reputation.

## How it is checked

`.\tasks.ps1 installer-sandbox` (`tools/sandbox/start.ps1 -Installer`, `tools/sandbox/installer.ps1`) builds the
installer from the checkout and runs it in Windows Sandbox, a clean Windows (about 3 minutes):

- a silent install with the defaults, which must download nothing;
- a silent install with PATH, "Open with" and Harper (`/GRAMMAR=harper`), each result checked (files, PATH, App Paths,
  the menu keys, the shortcut, Harper downloaded and turned on, `be --version` from a new console's PATH);
- an install over it without PATH (the entry goes); a silent uninstall (all gone, the downloads kept);
- the whole install in Russian through the wizard, Enter on each page, one picture of the installer's window per
  step (licence, folder, tasks, the grammar page with no video card — all five models greyed —, ready, unpacking,
  finished), and the tasks page in English;
- the grammar page as a computer with an 8 GB card shows it (`BLUEEDIT_VIDEO_CARD`, as the Sandbox has none): all five
  fit, YandexGPT recommended; Claude chosen, a key typed that is not a real one, Anthropic refusing it on Next and Yes
  keeping it; then checked: the settings name Anthropic, claude-haiku-5-5 and `file:llm-key`, the key file is
  encrypted, the key is in no settings file or log;
- Ollama chosen: its address offered and no key asked.

On 23 October 2026 the installer was 25.6 MB (`be.exe` alone 72 MB). The pictures earned their place on the first
run: a sub-item of the model or the code tools not marked `unchecked` ticks its group, so the model's group came
ticked with Qwen3 8B and the code tools with all four — Next on every page would have downloaded about 6 GB. The silent
checks named their tasks and missed it; the install with the defaults now catches it.

