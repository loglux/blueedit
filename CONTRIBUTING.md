# Working on BlueEdit

A DOS-style console text editor (C#, .NET 10, its own terminal layer in `src/Tui`) with spelling, grammar and style
checking and language servers. User docs: [README.md](README.md) and [docs/](docs/).

## Routine operations: `tasks.ps1` (or `make`)

| `.\tasks.ps1 …` / `make …` | What |
|---|---|
| `check` | build with analyzers (warnings are errors) + unit tests, ~30 s — after every change |
| `ui <pytest args>` | some UI tests on a fresh build: `ui test_lsp.py`, `ui test_markdown.py -k grammar` |
| `test-all` | everything incl. all UI tests, ~5 min |
| `publish` / `publish-linux` | the Windows build in `publish\`, the Linux build and its tar.gz in `publish-linux\` |
| `sandbox`, `newcomer`, `dialogs` | the setup scripts, a new user's first minutes, the dialogs in Russian — on a clean Windows in Windows Sandbox |
| `newcomer-linux` | a new user's first minutes with the Linux release in clean containers (a Linux host with Docker: `BLUEEDIT_LINUX_HOST`) |
| `lang-template` | `lang/template/strings.json` written anew after interface strings change (a new language starts from it) |
| `setup`, `setup-proofing`, `setup-lsp` | the optional checkers and language servers (Linux: `tools/proofing-setup/install.sh`) |

On Linux: `tools/ci/check-linux.sh` (`--fast`: build and unit tests). UI tests drive the real executable in a
pseudo-console; once: `pip install -r tests/ui/requirements.txt`.

## Environment variables

None is needed to use the editor; they move its folders, switch things for tests, or turn on measurements.

| Variable | What |
|---|---|
| `BLUEEDIT_CONFIG_DIR` | the settings folder instead of `%APPDATA%\blueedit` / `~/.config/blueedit` (the tests use it, never the real file) |
| `BLUEEDIT_RECOVERY_DIR` | the folder of unsaved-text recovery instead of `…\BlueEdit\recovery` |
| `BLUEEDIT_TOOLS` | where language servers are installed instead of `…\BlueEdit\tools` |
| `BLUEEDIT_MATHIMAGES` | where the formula pictures module is; `off` turns it off |
| `BLUEEDIT_WT_FRAGMENTS` | the Windows Terminal profile fragment folder (the tests use it) |
| `BLUEEDIT_UI_LANGUAGE` | the interface's language over the setting `uiLanguage` (`en`, `ru`) |
| `BLUEEDIT_WELCOME=0` | no welcome on the first start (the UI tests) |
| `BLUEEDIT_KEY_EVENTS=1` / `0` | Windows key events (or the kitty protocol) on or off whatever the settings |
| `BLUEEDIT_TIMING=1` | View > Show timing from the start |
| `BLUEEDIT_TIMELOG=<file>` | wall-clock stamps of each key's arrival (K) and frame drawn (D), for measuring the terminal around the editor |
| `BLUEEDIT_LINUX_HOST=user@host` | the Linux host with Docker for `tasks newcomer-linux` |
| `BLUEEDIT_UI_PATIENCE`, `BLUEEDIT_NO_TIMING=1`, `BLUEEDIT_NO_CLIPBOARD=1` | UI tests: wait longer on a loaded machine; skip the millisecond tests; skip the one that uses the real clipboard |
| `BLUEEDIT_PROOF_BENCH*`, `BLUEEDIT_MATH_BENCH*`, `BLUEEDIT_STYLE_BENCH*`, `BLUEEDIT_LATEX_BENCH*`, `BLUEEDIT_BENCH_CASES`, `BLUEEDIT_BENCH_SETS`, `BLUEEDIT_MATH_CORPUS`, `BLUEEDIT_FASTTEXT_REF`, `BLUEEDIT_EDGE_PROFILE` | the benchmarks in `tests/BlueEdit.Tests` (each file's summary says how) |

## How a change goes

1. A branch per change; a pull request into `main`.
2. Code + tests (unit in `tests/BlueEdit.Tests`, UI in `tests/ui`) + docs.
3. `check`, and the UI tests that touch the change.
4. The pull request is checked by GitHub Actions ([docs/ci.md](docs/ci.md)).

## Rules

- **Typing must never wait.** Anything slow (spelling answers, grammar, language servers, models) runs in the
  background; the screen shows what is known and redraws when answers come (`TuiApp.Post`, `Version` counters).
- Analyzers on, warnings are errors; match the surrounding code's naming, comments and idiom.
- Interface strings go through `T()` (`Ui.cs`; values as `{0}` with `Tf`, never pasted together) and into
  `lang/ru.json` — `UiTests` fails on a `T()` string the table lacks. A menu letter, or the first letters of a
  question's buttons, clash with nothing (`docs/tech/editing.md`).
- F1 help lines at most 86 characters (`HelpTextTests`).
- Nothing tied to one language or one system: languages come from the system or the user's choice.
- Anything that ships a feature also updates the docs (README, `docs/*.md` and the Russian `*.ru.md`, F1 help).
- A design decision (why this way, what was measured, what was rejected) goes into `docs/tech/*.md`.
- Facts in docs come from measurements or the code; say what was not checked.

## Pitfalls met here

- **A fresh Windows has only Windows PowerShell 5.1**: setup scripts must run there — a UTF-8 BOM on any script with
  non-ASCII text, HTTP answers decoded as UTF-8 explicitly.
- **Windows Sandbox with an NVIDIA card** may need `<vGPU>Disable</vGPU>`: with the virtual GPU the connection was lost
  within a minute. `tasks sandbox` sets it.
- **Under WSL, PATH also lists Windows folders** (`/mnt/c/...`): Windows npm shims there look executable.
- C#: `"\x1bD"` is not ESC + D (`\x` takes up to four hex digits) — write `\u001b`.
- `localhost` costs 2 s per request on Windows (IPv6 first): use `127.0.0.1` for local servers.
- pytest splits `PYTEST_ADDOPTS` like a Unix shell: forward slashes in paths.
- A UI test that waits fractions of a second for the editor fails on a busy computer: run the full set when nothing
  heavy runs beside it.

## Where things are

- Terminal layer: `src/Tui` — [docs/tech/tui.md](docs/tech/tui.md).
- Editor: `src/BlueEdit` — `Program.cs` (the command line), `App.cs` (wiring) and `App.*.cs` by area (`Menus`: keys
  and menus, `Status`, `Documents`, `Search`, `Settings`, `Proofing`, `ProofingSetup`, `Code`), `EditorView*.cs`,
  `MainView.cs`, `Settings.cs`, `Themes.cs`, `HelpText.cs` (the F1 help is `lang/help.en.txt`) — [docs/tech/editing.md](docs/tech/editing.md).
- Formulas: `Latex.cs`, `LatexLayout.cs`, `FormulaImages.cs` + the optional module `src/MathImages`.
- Proofing: `Spell.cs`, `HunspellSpell.cs`, `OfficeSpell.cs`, `Grammar.cs`, `LlmGrammar.cs`, `LlmServer.cs`,
  `Style.cs`, `ProofingTools.cs` — [docs/tech/proofing-internals.md](docs/tech/proofing-internals.md).
- Language servers: `src/BlueEdit/Lsp` — [docs/lsp.md](docs/lsp.md).
- Benchmarks: `tools/proofbench`. Setup scripts: `tools/proofing-setup`, `tools/lsp-setup`.
