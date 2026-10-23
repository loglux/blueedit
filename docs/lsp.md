# Language servers: errors and warnings in code

*По-русски: [lsp.ru.md](lsp.ru.md)*

BlueEdit talks to **language servers** — the same programs VS Code and other editors use (the Language Server
Protocol) — to show errors and warnings in code as you type. Free servers you install once; nothing leaves the
computer.

What works now:

- errors underlined in the misspelling colour, warnings in their own (theme colour `warning`);
- **Shift+F7** goes to the next one (together with spelling and grammar problems), the status line says what it is:
  `Error (Pyright): "undefined_name" is not defined`;
- **F7** on it offers the server's fixes for it, its preferred one first — csharp-ls: the missing `using`, generate
  the type… — Enter takes one (one undo step; a fix that touches other files opens them, as a rename does). The last
  row, and F7 where the server has no fix, shows the full message. pyright offers no fixes (measured: none for an
  undefined name), so in Python F7 shows the message;
- the status line shows the file's server and counts: `LSP pyright-langserver 2E 1W`
  (`starting`, or `failed: …` with the reason);
- **completion from the server**: after a dot the list of members opens by itself (`os.` → `path`, `sep`…; `s.` →
  `Length`…), each marked with its kind on the right — `m` method, `f` function, `fld` field, `prop` property, `var`
  variable, `cls` class, `mod` module, `kw` keyword, `const`…; after three letters of a word the server's
  candidates come first; **Ctrl+Space** asks the server (waiting at most 0.4 s, not at all while it is starting) and falls back to the words of the
  text. Typing never waits: the question goes in the background and the list opens when the answer comes, unless
  the caret has moved on. **Tab** takes the selected one; Enter only after choosing with the arrows.

- **F12** goes to the definition of the symbol at the caret — in this file or another, which opens in a tab; several
  definitions: a list to pick from. **Shift+F12** goes back to where F12 jumped from (any number of steps).
- **Alt+F1**: what the server knows about the symbol at the caret — its type or signature and documentation.
- **Ctrl+F12** formats the file (also Format → Format code). One undo step (Ctrl+Z); the text on the screen is
  formatted, saved or not. Who formats:
  - **the language server**, when it has a formatter: csharp-ls does (Roslyn: indents, spaces, braces, but a one-line
    block stays on one line). With a selection, only the selected lines.
  - **a formatting program** otherwise: pyright has no formatter, so for Python **ruff**, or **black** when ruff is
    not installed. It is run in the file's folder and told the file's path, so the project's settings apply
    (`pyproject.toml`, `ruff.toml`: line length, quotes…). It formats the whole file, also with a selection. A syntax
    error leaves the text alone and the status line shows the program's message.
  - Neither: the status line says so (`no formatter: Tools > Code tools installs ruff`).

  Install ruff: **Tools → Code tools**, or `tools\lsp-setup\install.ps1 -Python` (winget, `astral-sh.ruff`, for the current user) or
  `tools/lsp-setup/install.sh --python` (ruff's release from GitHub into `~/.local/bin`). black installed with
  `pip install --user black` is found too, although pip leaves its folder off `PATH`.
- **Ctrl+F2** renames the symbol at the caret wherever the server finds it (also Edit → Rename symbol). Type the new
  name (it replaces the old one) and Enter. Files already open get the change as one undo step each; files that are
  not open are opened with it, **unsaved**, so you see what changed — **File → Save all** writes them. In a string or
  a comment it says there is nothing to rename instead of asking for a name. pyright and csharp-ls both rename.
- **Alt+F12**: where the symbol at the caret is used — its definition and every use the server knows, in all files
  (also Search → Symbol references). The list shows each place with its line, starting at the one under the caret;
  Enter opens the file there. **Search → Folder results** opens the last list again. pyright and csharp-ls both find
  references.
- **Ctrl+O** in code: the file's outline from the server — classes, functions, methods, properties, nested as in the
  file, with their kind; what is inside a function (its local variables) is left out. Enter goes there. In
  Markdown and LaTeX Ctrl+O lists the headings, as before. Tried with pyright; csharp-ls offers it too.
- **Parameters while typing a call**: after `(` and `,` the status line shows the function's signature with the
  parameter being typed in brackets — `greet([name: str], times: int = 1)` — while the caret stays on the line; `)`
  or leaving the line hides it. Asked in the background like everything else.

## Servers

**The easy way: Tools → Code tools** (or `be --install-tools` in a shell, over SSH too). The editor installs
these into its own folder — `%LOCALAPPDATA%\BlueEdit\tools` on Windows, `~/.local/share/blueedit/tools` elsewhere
(`BLUEEDIT_TOOLS` moves it) — without administrator rights or system packages, and finds them there itself:

| Tool | For | Brought along | On disk (measured) |
|---|---|---|---|
| pyright | Python: errors, completion, definitions, rename… | Node.js (current LTS, from nodejs.org) | 250 MB on Linux, 120 MB on Windows |
| ruff | Python: formatting | — (one program, from its GitHub releases) | 23 MB |
| csharp-ls | C#: errors, completion, fixes… | the .NET SDK 10 (from Microsoft's release list) | 720 MB |
| typescript | JavaScript, TypeScript: errors, completion, definitions, rename… | — (TypeScript 7, one native program, from the npm registry) | 27 MB |

The window shows each tool as found (and where: one already on `PATH` is used as it is) or what installing it takes;
Enter on it installs or updates it, **Install all that are missing** does the rest, **Remove what was installed here**
deletes the folder. It runs in the background with the steps in the status line, and the servers are started when it
ends. `be --install-tools pyright,ruff` installs only those. Node.js and ruff are checked against the SHA-256
sums their projects publish, the .NET SDK against the SHA-512 in Microsoft's release list, TypeScript against the
SHA-512 the npm registry publishes; what went wrong is in `install.log` in the folder.

A Python, C#, JavaScript or TypeScript file shown with no server installed says so once: `Python: no language server installed …: Tools >
Code tools`.

The servers by hand, if preferred:

| Language | Server | Install |
|---|---|---|
| Python | pyright (Microsoft, MIT) | `npm install -g pyright` (needs [Node.js](https://nodejs.org)) |
| C# | csharp-ls (MIT) | `dotnet tool install -g csharp-ls` (needs the .NET SDK) |
| JavaScript, TypeScript | TypeScript 7's own server, `tsc --lsp --stdio` (Microsoft, Apache-2.0) | Tools → Code tools only: the editor runs its copy by its full path, since a `tsc` on `PATH` is usually TypeScript 5 from npm, which has no server. Another can be named in `languageServers` |

Or: `tools\lsp-setup\install.ps1 -All` (installs or updates both); without switches it lists what BlueEdit finds.
When Node.js or the .NET SDK is missing, the script offers to install it with winget (`OpenJS.NodeJS.LTS`,
`Microsoft.DotNet.SDK.10`) — for the whole computer, so Windows asks for administrator approval; `-Yes` installs
without asking first. Without winget it says what to install by hand.

A server is used when its command is found — on `PATH` (with `.cmd`/`.exe`), or in `%USERPROFILE%\.dotnet\tools`
where dotnet tools go. One that is not installed is simply not used: no message.

**Linux (and WSL)**: `tools/lsp-setup/install.sh --all`. It puts pyright into `~/.local` with npm (`npm install -g`
would need root), csharp-ls into `~/.dotnet/tools`; a missing Node.js or .NET SDK it offers to install with apt
(`nodejs npm`, `dotnet-sdk-10.0`; sudo asks for the password, `--yes` skips the question). The Linux build finds a
server on `PATH`, in `~/.local/bin` and in `~/.dotnet/tools`; under WSL it skips the Windows folders on `PATH`
(`/mnt/c/…`), whose npm shims look executable but need Windows' Node. Checked in WSL (Ubuntu 24.04): pyright 1.1.414 and
csharp-ls 0.28.0, the real-server tests pass.

## How it works

- When a file of a language with a server is shown, the server starts (once per language and **project root**) and
  stops with the editor — a Windows job object ends it even if the editor is killed.
- Project root: the nearest folder up from the file with a marker — C#: `*.sln`, `*.slnx`, `*.csproj`; Python:
  `pyproject.toml`, `pyrightconfig.json`, `setup.py`, `setup.cfg`, `requirements.txt`; JavaScript and TypeScript:
  `tsconfig.json`, `jsconfig.json`, `package.json`; any language: `.git` — otherwise
  the file's own folder. C# needs a project for full checking: a lone `.cs` file gets little.
- The file's text goes to the server after each edit (at most twice a second, from a background thread), unsaved
  text included: findings follow what is on the screen, not what is on disk. Typing is never held up by a server.
- Diagnostics come back whenever the server has them; the screen updates on its own. A server that only answers when
  asked (LSP 3.17 "pull" diagnostics, as TypeScript 7 does) is asked after each edit, for the other open files too
  when it says one file's errors depend on another's.

## Settings

```jsonc
"lsp": true,                                   // false: no language servers at all
"languageServers": {                           // on top of the defaults
    "Python": { "command": "pylsp", "args": [] },                // another server for a language
    "C#": "off"                                                   // a default turned off
},
"formatters": {                                // Ctrl+F12 by a program, used before the server's formatter
    "Python": { "command": "black", "args": ["-q", "--stdin-filename", "{file}", "-"] },
    "C#": "off"                                                   // no formatting for a language
}
```

A formatter reads the text on its standard input and writes the formatted text; `{file}` in its arguments is the
file's path. Any such program works: `prettier --stdin-filepath {file}`, `clang-format --assume-filename={file}`…

Language names are as in the status line: `Python`, `C#`, `PowerShell`, `C/C++`, `JavaScript`, `Go`, `Rust`, `Java`,
`JSON`, `SQL`, `HTML/XML`. Any server that speaks LSP over stdio works (clangd for C/C++, gopls for Go,
rust-analyzer for Rust…); only Python and C# are tried. PowerShell's server (PowerShell Editor Services) needs a
longer start command and is not set up yet.

## When it does not work

| The status line | Cause |
|---|---|
| no `LSP …` for a .py / .cs file | the server is not installed or not found: `tools\lsp-setup\install.ps1`; or `"lsp": false`, or the language is `"off"` |
| `LSP … starting` for long | the server loads the project first: csharp-ls runs MSBuild on the project (seconds to a minute for a big solution) |
| `LSP … failed: …` | it did not start or did not answer `initialize` in 60 s; the reason follows (and is said once in the message line) |
| `LSP … stopped: …` | the server exited; the last line it wrote to its error output follows |

Tests: `tests/BlueEdit.Tests/LspTests.cs` (paths, roots, settings, drawing; real pyright and csharp-ls when
installed), `tests/ui/test_lsp.py` (pyright in the real editor). How it is built: `Lsp/LspClient.cs` (JSON-RPC over
stdio with Content-Length framing; requests from the server answered with empty results), `Lsp/LanguageServers.cs`
(servers per language and root, sync, diagnostics), `EditorView.Diagnostics.cs` (underlines, Shift+F7, F7).
