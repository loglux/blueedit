# Checks

Every pull request and every push to `main` is checked by GitHub Actions on GitHub's own machines
(`.github/workflows/check.yml`):

| Job | Machine | What |
|---|---|---|
| `linux` | Ubuntu 24.04 | `tools/ci/check-linux.sh`: the build with warnings as errors, the unit tests, the Linux build, the UI tests driving it in a pty (with the Hunspell dictionaries for English and Russian from apt) |
| `windows` | Windows Server 2025 | `check.ps1 -Fast`: the build and the unit tests |

There is no language model, LanguageTool, Word or video card on those machines: the tests that need one skip. They
run where those are installed: `.\tasks.ps1 test-all` on Windows (it starts them), `tools/ci/check-linux.sh` on Linux.
The UI tests on Windows are not run there: run them locally (`.\tasks.ps1 test-all`).

A release: bump `<Version>` in `BlueEdit.csproj` (a pull request), then tag it (`git tag v0.4.0; git push origin
v0.4.0`). `.github/workflows/release.yml` builds, on a GitHub Windows machine, the Windows installer and zip, the Linux tar.gz
(`tools/ci/pack-linux.py` keeps the execute bits), the formula pictures module for each, and makes a **draft**
release with their SHA-256; the notes are written and the draft published by hand.

The checks below run on a computer of your own, not in GitHub Actions.

## A clean Windows: the setup scripts in Windows Sandbox

`.\tasks.ps1 sandbox` (`tools\sandbox\start.ps1`) tries the setup scripts the way a new user meets them: it starts
Windows Sandbox (a throw-away Windows with nothing installed) with `tools\` mapped read-only, runs
`tools\sandbox\inside.ps1` there — `proofing-setup\install.ps1 -LanguageTool`, `check.ps1 -Run`,
`lsp-setup\install.ps1 -All -Yes` — prints its transcript and closes the sandbox. Nothing on the host computer changes; the
output stays in `%TEMP%\blueedit-sandbox\out`. It is a manual check before a release or after changing the scripts,
not part of CI. Needs the Windows feature: `Enable-WindowsOptionalFeature -Online -FeatureName
Containers-DisposableClientVM`, then a restart.

What a clean Windows has, as seen there (Windows 10.0.26100, 4 October 2026): Windows PowerShell 5.1 only (no
PowerShell 7), no winget, no Node.js or .NET SDK; `curl.exe` is there. Found by it:

- **Russian answers came out as `ÐÐµÐ²...`** in `check.ps1` under Windows PowerShell 5.1: it reads an HTTP answer
  without a charset as Latin-1. The answers are now decoded as UTF-8 (`PostUtf8`), and the scripts with non-ASCII text
  carry a UTF-8 BOM (5.1 reads a file without one in the ANSI code page — the "да" answer in `lsp-setup` too).
- Java + LanguageTool install in about a minute; `lsp-setup` without winget says what to install by hand.

### The new user's way: `.\tasks.ps1 newcomer`

`start.ps1 -Newcomer` builds the release zip from the checkout (as `release.yml` does: `be.exe` and the setup scripts),
maps it into the sandbox and runs `tools\sandbox\newcomer.ps1` instead: the zip is unpacked into Downloads with the
internet zone mark a browser gives, `be.exe` is started through the shell as a double click starts it, keys are sent
(`SendKeys`) and the screen is captured after each step into `out\NN-step.png`: the welcome, the menu (F9), F1, typing with
a mistake for each checker (spelling, Harper, LanguageTool), in English and, only where that Windows has Russian, in
Russian too, F7, then Tools → Set up grammar checking → Harper and → LanguageTool, waiting for each.
The pictures are read by eye; nothing is asserted. About 5 minutes.

`start.ps1 -Dialogs` (`.\tasks.ps1 dialogs`) builds the same zip and runs `tools\sandbox\dialogs.ps1`: the welcome's Language button sets Russian, then File > Info and each dialog of the Tools menu is opened and captured, nothing installed (about 3 minutes): how a translation looks, which `UiTests` cannot see (a word that does not agree, a column too narrow for the longer words). On 21 October 2026 it found both. Last, Tools > Command be in any console puts the folder in PATH, and the transcript shows the user's PATH, its kind (ExpandString kept) and that a new console finds `be`.

`SendKeys` cannot send Alt+letter to a console window (the letters were typed), so menus are opened with F9 and the
arrows. F10 quits the editor (as in Far), so it is not sent.

Found on 20 October 2026 (Windows 10.0.26100, Russian regional format, English display language):

- **"The publisher could not be verified"**: be.exe is not signed, so Windows asks before the first start of a
  downloaded copy (Run / Cancel; with SmartScreen from Explorer, "Windows protected your PC"). Not fixed, on purpose: a
  certificate costs hundreds a year and does not stop SmartScreen's warning until the file has a reputation; the
  README says what to press.
- **The console is the old one** (conhost, about 110×30): there is no Windows Terminal in the sandbox. The editor
  works in it; formula pictures do not (no Sixel).
- **Nothing said where to begin**: an empty page. Fixed: the first start shows a welcome (docs/tech/editing.md,
  "The first start"); the walkthrough captures it.
- Spelling worked at once for English and Russian (the dictionaries Windows has); F1 and the menus are readable.
- LanguageTool from the menu: Java and LanguageTool installed and "this are" was underlined within 3 minutes.
- **Harper did not start** ("no answer to initialize"): the Microsoft Visual C++ runtime is missing on a clean Windows.
  Fixed: the editor puts Microsoft's runtime DLLs next to harper-ls and llama.cpp, no administrator
  (docs/tech/proofing-internals.md, "Installing from the editor"). In the walkthrough the transcript lists the files
  next to harper-ls and the install log.

Run again on 22 October 2026, before 0.3.0: the welcome in Russian and back, Harper and LanguageTool installed from the
menu, the runtime DLLs next to harper-ls, nothing in System32. Two things about the walkthrough itself:

- Harper does not mark "this are" (`harper-cli lint` gives only the spelling of "Helo"), so its step showed no grammar
  mark. The typed text now also has "a apple", which Harper marks (its AnA rule).
- The last picture of each step typed a space first; the edit cleared LanguageTool's marks until it had checked again,
  so "this are", marked a moment before, looked unmarked. The step now only moves the caret.

Run a third time the same day, with these two changed: Harper marked "a apple", LanguageTool "this are". Also seen
and fixed: the window's title was the path of `be.exe` (now "NONAME * - BlueEdit", docs/tech/tui.md), and the English
F1 help listed the menus' Alt letters without View's V (`UiTests` now compares them). Not fixed: Tools > Set up
grammar checking says "uses the video card (vulkan)" in the sandbox, which runs without a virtual GPU. Choosing the
build by Vulkan's loader was tried and dropped: the transcript showed `System32\vulkan-1.dll` present in the sandbox
(it shares the host's system files, and the host has an NVIDIA driver), so the file says nothing about a video card.
Fixed the next day by asking Vulkan for its devices (docs/tech/proofing-internals.md): the fourth run logged "video
cards Vulkan sees (be --vulkan-devices): none", and the list said "no video card found: slow".

**The virtual GPU is off** (`<vGPU>Disable</vGPU>`). With it on (the default, also when the sandbox is started from
the Start menu) the sandbox showed a black screen and then "connection to the Windows Sandbox environment was lost"
on a test machine (Windows 11 Insider build 26300, NVIDIA RTX 4070); the Hyper-V-Worker log had event 33101,
"Virtual machine requested unsupported Virtual PCI protocol version 0x10006 … two revision levels are incompatible".
With the vGPU off it starts and stays up. Others report the same event on 26300
([microsoft/Windows-Sandbox#131](https://github.com/microsoft/Windows-Sandbox/issues/131)) and lost connections cured
by turning the vGPU off ([#71](https://github.com/microsoft/Windows-Sandbox/issues/71)). For the sandbox started by
hand: the group policy *Allow vGPU sharing for Windows Sandbox* = Disabled, or
`reg add "HKLM\SOFTWARE\Policies\Microsoft\Windows\Sandbox" /v AllowVGPU /t REG_DWORD /d 0 /f` (not tried).

## A bare Linux: the new user's way in clean containers

`.\tasks.ps1 newcomer-linux` (`tools/linux-newcomer/run.ps1`) does on Linux what `newcomer` does on Windows. The
release tar.gz from `publish-linux\` (`tasks publish-linux` first), `walk.py` and the UI harness (`tests/ui/harness.py`)
are copied to a Linux host with Docker (`-HostName user@host`, or the environment variable `BLUEEDIT_LINUX_HOST`). There, for each image
(Debian 12, Ubuntu 24.04 and Fedora 41 by default; `-Images`), a clean container gets nothing installed. The tar.gz
is unpacked in `~/Downloads`, `./be` started in a pty, and keys are sent: the welcome, F9, F1, typing with mistakes,
F7, Tools → Get spelling dictionaries, Tools → Set up grammar checking → Harper (`-LanguageTool`: that too). The
harness's Python packages live in a virtual environment on the host (`~/.cache/be-newcomer-venv`), not in the
containers. The screens come back as text into `%TEMP%\blueedit-linux-newcomer\IMAGE\NN-step.txt`; a row of `~` under
a line marks its underlined letters. A `log.txt` beside them has the system, what was installed, and whether the
editor still runs. Nothing is asserted; the screens are read. About 6 minutes for the three images.

First run, 23 October 2026, with release 0.3.0:
- **Debian 12: `be` ended at once**, "Couldn't find a valid ICU package installed on the system". None of the three
  images has ICU (no `libicuuc.so*` in them), and .NET stops the program at its first comparison of text; it was run
  only in Debian before the change. `be --version` worked, comparing nothing. The editor now turns .NET's invariant
  mode on when no `libicuuc.so.NN` loads (`Program.cs`, docs/linux.md).
- **Debian 12: the first download ended the editor**, with "No usable version of libssl was found" and the text
  typed so far lost. The image has no OpenSSL, and .NET aborts the process when an https connection needs it. Every
  `HttpClient` now goes through `Net.cs`, which refuses https without libssl and says what to install.
- **Ubuntu 24.04: no download worked.** The image has no `ca-certificates`. The editor tried each file five times
  ("the connection broke off, going on...") and then said "The SSL connection could not be established, see inner
  exception". It now says the reason and what to install, and does not retry a TLS failure.
- A failed install's reason was cut off by the status line before the part that says what to install. It is now
  said in a window, wrapped (`ButtonDialog` wraps a line wider than the screen).
- Fedora 41 had everything else: the dictionaries and Harper installed, with "Helo", "Превет" and "a apple" marked.

The run again with these changes: Debian and Ubuntu showed the two messages in a window, whole, and the editor ran
on; Fedora as before.

The typed text is in the system's language: these systems name none, so it is English only. With "Превет мир" in it
too (until 23 October 2026), a server spelling en-US alone underlined every Russian word: it led to the rule that a
word in letters no spelling language uses is unchecked, not wrong (`docs/tech/proofing-internals.md`).

`run.ps1 -Lang de_DE.UTF-8` gives the system a language (`LANG`) and types a text in it with its own mistakes (German,
Russian or English; `walk.py` `TEXTS`). On 23 October 2026 in Fedora 41 with German and `-LanguageTool`: de-DE was
the dictionary ticked and downloaded, the spelling ran in German ("Tesst" marked), LanguageTool marked "ein Fehler"
(for "einen Fehler"), and Harper, English only, marked nothing. Nothing was told the language but the system.

**Minimal servers** (`tools/linux-newcomer/run.ps1 -Vm`): a container is barer than any installed system. So the same
walk also runs in virtual machines of the official cloud images, what a rented server or a server install without a
desktop starts as: Ubuntu 24.04 minimal and Debian 12 genericcloud. `vm.sh` runs them with qemu and KVM inside a
container on the host (nothing installed on it; it needs `/dev/kvm` and Docker), the images checked against the
published sums and kept in `~/be-vm`. Each boots from a fresh disk on top of its image; the newcomer is the image's
normal user (`ubuntu`, `debian`) over ssh, and the disks are dropped after. About 8 minutes with both machines.
Debian's cloud kernel has no driver for qemu's default network card, so the card is virtio.

Run on 23 October 2026: **Ubuntu 24.04 minimal has no ICU** (libssl and the certificates it has). Without the change
above `be` would not have started on a fresh Ubuntu server. With it, everything worked in both machines: the
dictionaries and Harper installed, the same three words marked, languages named by their tags on Ubuntu and by name
on Debian (ICU 72). Seen there and fixed: a dialog as wide as its items cut its title ("Spelling dictionaries
(LibreOffice's, fo"); `Draw.Centered` now makes a box wide enough for its title.

