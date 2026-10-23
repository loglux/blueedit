# BlueEdit on Linux

Russian: [linux.ru.md](linux.ru.md).

The same editor as on Windows, built for Linux x64 (one self-contained file, no .NET needed). It runs in any terminal
that speaks the usual VT sequences — GNOME Terminal, Konsole, xterm, kitty, WezTerm, Windows Terminal with WSL, a
terminal over SSH. Status: a preview. What was checked, and where, is said below; what was not, too.

## Install

From a release: download `blueedit-<version>-linux-x64.tar.gz` (and check its SHA-256 against the release notes), then

```bash
tar xzf blueedit-*-linux-x64.tar.gz
cd blueedit-*-linux-x64
./be --version     # prints the version: the build runs here
./be notes.md
```

Put it on your PATH if you like (`ln -s "$PWD/be" ~/.local/bin/be`). From the source: `.\tasks.ps1 publish-linux`
(on Windows: the build and the same archive in `publish-linux\`) or `dotnet publish -c Release -r linux-x64
--self-contained -p:PublishSingleFile=true -o out` on Linux.

## What is on Linux, and how

| Part | On Linux |
|---|---|
| The editor, menus, dialogs, mouse, Markdown live preview | as on Windows: the terminal layer has a Unix back end (termios raw mode) |
| Spelling | **Hunspell**: **Tools → Get spelling dictionaries…** downloads LibreOffice's into `~/.local/share/BlueEdit/hunspell` (no root; `be --install-proofing ru-RU,en-GB` in a shell), or the distribution's: `sudo apt install hunspell-ru hunspell-en-gb hunspell-en-us` (they go to `/usr/share/hunspell`; also read: `~/.local/share/hunspell`, `/usr/share/myspell`, `/usr/local/share/hunspell`, `/opt/homebrew/share/hunspell`, `~/Library/Spelling`) |
| Grammar: LanguageTool, a local language model; style suggestions | `tools/proofing-setup/install.sh --all` (Java, LanguageTool, llama.cpp — CUDA, Vulkan or CPU build — and a model, into `~/.local/share/BlueEdit`) |
| Language servers (Python, C#), ruff | **Tools → Code tools** in the editor, or `be --install-tools` (into `~/.local/share/blueedit/tools`, no root, no apt: also on a NAS); or `tools/lsp-setup/install.sh --all` (apt, sudo) |
| Clipboard | `wl-copy`/`wl-paste` (Wayland), `xclip` or `xsel` (X11), `pbcopy` (macOS), the first that works; with none (a console, SSH) a copy goes to the terminal as OSC 52, which most terminals put on their own clipboard |
| Settings, your dictionary | `~/.config/blueedit/settings.json`, `dictionary.txt` (`$XDG_CONFIG_HOME/blueedit`) |
| Terminal font | set in the terminal's own settings; **Tools → Terminal font** says so (the Windows Terminal profile is Windows-only) |
| Microsoft Word spelling, Edge's proofreader | Windows only |

## WSL

Everything above works in WSL (checked in Ubuntu 24.04 on Windows 11): the editor in a WSL tab of Windows Terminal,
Hunspell, LanguageTool, a model on the computer's NVIDIA card (the CUDA build of llama.cpp works with the Windows
driver; `install.sh` picks it when `nvidia-smi` works), the language servers. The clipboard is shared with Windows
through WSLg's X11 (`sudo apt install xclip`); WSLg's Wayland socket is not where `WAYLAND_DISPLAY` points, so BlueEdit
checks that the socket exists before using `wl-copy`. The Linux build skips Windows folders on `PATH` (`/mnt/c/…`).

## Checked, and not yet

Checked in WSL (Ubuntu 24.04, an RTX 4070): the unit tests (all pass on Linux, one Windows-only skipped); the editor
driven in a pseudo-terminal — drawing, Cyrillic typing, arrows, saving, quitting, spelling underlines and F7, grammar
from LanguageTool and Phi-4-mini underlined within 7 s with F7 showing both, copying into the Windows clipboard; the
real-server tests of LanguageTool, the model, pyright and csharp-ls; both install scripts end to end; the whole UI
test suite against the Linux build (`tools/ci/check-linux.sh`, 5 October 2026: 169 passed, 5 skipped: four test
Windows-only things — the Windows Terminal profile, its font, the Windows clipboard — and one needs Word). Every pull
request is also checked on a Linux CI runner (`docs/ci.md`).

Checked on a Linux desktop (6 October 2026, #89): Ubuntu 26.04.1 Desktop (GNOME on Wayland) in VirtualBox, the build
from `tasks publish-linux`:
- in GNOME's terminal (Ptyxis): drawing, menus, Cyrillic, Hunspell underlines (`hunspell-ru`, `hunspell-en-gb`), the
  reading view with a display formula over several rows; `--version`;
- the Wayland clipboard both ways (`sudo apt install wl-clipboard`): Ctrl+A, Ctrl+C in the editor and `wl-paste`
  gives the text, Cyrillic intact; text put on the clipboard by another program pastes with Ctrl+V. (`wl-copy` run
  from an SSH session does not take GNOME's clipboard: it has no window in focus. That is GNOME's rule, not the
  editor's: over SSH the editor copies with OSC 52.)
- over SSH from Windows (`ssh.exe` in a Windows pseudo-console, as Windows Terminal runs it, to the VM): F9 and Esc,
  Alt+F, arrows, ten rounds each with no key lost or broken into text — also with 60 ± 40 ms and 200 ± 150 ms of delay
  added to every packet coming in to the VM (`tc netem` behind an `ifb`), more than the 25 ms pause that tells a lone
  Esc from the start of a key's sequence. A key's sequence arrives whole; the pause was not changed. Copying over SSH
  sends OSC 52 with the text (seen in the output); that Windows Terminal then puts it on its clipboard was not checked.

Checked on bare systems (23 October 2026; `tasks newcomer-linux`, docs/ci.md): clean Debian 12, Ubuntu 24.04 and
Fedora 41 containers, nothing installed. What the editor needs of the system, and what it does without:
- **ICU (libicu)**, the language data: without it the editor runs in .NET's invariant mode. Typing, Cyrillic and
  letter case work; languages are named by their tags (`ru-RU` where "Russian" would be). The three images have
  none, nor has a minimal Ubuntu 24.04 server (its cloud image, checked in a virtual machine); Debian 12's cloud image
  has it, as do desktops and the servers we use (Ubuntu's `libicu74`, the NAS's own copy in `/usr/builtin/lib`).
- **OpenSSL (libssl) and root certificates**, for downloading dictionaries, checkers, models and code tools over
  https: without them a window says what to install (`sudo apt install openssl ca-certificates`), and the editor
  runs on. Debian 12's image has no libssl, Ubuntu 24.04's no certificates, Fedora 41's both.

Not yet:
- macOS (it should build, nothing was run); X11 desktops (only GNOME on Wayland so far);
- a killed editor leaves LanguageTool, llama-server and language servers running (on Windows a job object ends them;
  a normal exit ends them on Linux too).

The plan and its state: issue #29.
