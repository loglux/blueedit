# The terminal layer (`src/Tui`)

Russian: [tui.ru.md](tui.ru.md).

BlueEdit does not use a TUI library. `src/Tui` (about 1,800 lines, its own project `Tui.csproj`, no dependencies) reads
the terminal's input, keeps a grid of cells and sends the terminal only what changed. The editor (`EditorView`,
`MainView`, `App`) draws into that grid and takes its events.

## Why our own

The editor first ran on [Terminal.Gui](https://github.com/gui-cs/Terminal.Gui). It was replaced on 2 October 2026
(commits `31925c6`, `fc31e26`, merge `8016836`) for these reasons:

- **Typing latency.** Terminal.Gui polled the console for input every 20 ms. Key pressed → character on the screen
  was **43–55 ms** with it, measured with a 2,000-line Markdown file. On the own layer: **4.8 ms median, p95 9 ms**
  (120×30), 9 ms at 220×50. The input thread now blocks on the console and wakes the moment a byte arrives. "Typing must
  never wait" is the project's first rule, and this was the biggest single cost.
- **Ctrl+Z.** .NET's console stream (and `ReadFile`) treats Ctrl+Z as end of input on Windows: input stopped after
  the first Ctrl+Z. The Windows backend reads with `ReadConsoleW`, which does not.
- **Control of the output.** Only changed cells are sent, in one write per frame; 24-bit colour; SGR mouse; bracketed
  paste (pasted text arrives as one event, so it is one undo step and gets no auto-indent).
- **Porting.** Everything except reading bytes, raw mode and the window size is the same on every system. The Linux
  backend (`UnixPlatform`, October 2026) was about 100 lines, and the whole editor then ran on Linux.

The cost: dialogs, menus and widgets are ours to write and maintain (`Dialogs.cs`, `MenuBar.cs`, `Widgets.cs`), and
anything a library would give for free (wide characters, for one) has to be added by hand.

## How it fits together

```
 terminal ──bytes──► ITerminalPlatform ──chars──► InputParser ──events──► queue ──► TuiApp loop ──► Root/Menu/Modal
 (Windows Terminal,   (WindowsPlatform,            (keys, mouse,           ▲                │
  xterm, ...)          UnixPlatform)                paste)                 │ WakeInput       │ draw
                                                                     TuiApp.Post(work)       ▼
 terminal ◄──one write per frame── ConsoleHost.Present ◄── Screen.Flush (changed cells only) ◄─ Screen
```

| File | What it does |
|---|---|
| `Platform.cs` | `ITerminalPlatform`: read input, the output stream, the window size, raw mode on and off. `WindowsPlatform` (console API) and `UnixPlatform` (termios); `CreateForCurrentOs` picks one. |
| `ConsoleHost.cs` | Owns the terminal while the app runs: raw mode, alternate screen, mouse and paste reporting on; an input thread; a size thread; restores everything on exit. |
| `InputParser.cs` | VT input → `KeyInput`, `MouseInput`, `PasteInput`. One parser for all systems. |
| `Events.cs` | The event types and `Key` / `Mods`. |
| `Screen.cs` | The cell grid (double buffer) and `Flush`, which builds the output that turns what the terminal shows into the new frame. |
| `TuiApp.cs` | The main loop, `Post` / `Wake`, modal dialogs, drawing order; `Theme`; box drawing helpers. |
| `MenuBar.cs`, `Dialogs.cs`, `Widgets.cs` | The menu bar, dialogs (prompt, check list, form, buttons, list, one-key question, the Open / Save as file picker) and widgets (one-line text input, list box). |
| `KeyboardLayout.cs` | Russian ЙЦУКЕН letter → the key's Latin letter, so Alt+F and the Y/N answers work on either layout. |

### The platforms

- **Windows** (`WindowsPlatform`): console modes set to VT input (`ENABLE_VIRTUAL_TERMINAL_INPUT`, no line input, no
  echo, no Ctrl+C processing) and VT output; code pages switched to UTF-8 (65001) and back on exit. Input is read
  as console records with `ReadConsoleInputW`; in VT input mode each record is one UTF-16 character, the same text
  `ReadConsoleW` gave. With `keyEvents` on, VT input is turned off and the records carry the keys (below).
- **Linux and macOS** (`UnixPlatform`): termios raw mode via libc's `cfmakeraw` (no line editing, no echo, Ctrl+C and
  Ctrl+Z reach the editor as keys). `struct termios` differs between Linux and macOS, so it is kept as opaque bytes: read
  with `tcgetattr`, made raw by libc, written back. Input is UTF-8 bytes from `read(0)`, decoded with a decoder that
  keeps a character split between two reads; a read interrupted by a signal (`EINTR`) is retried. On Linux "libc" is
  loaded as `libc.so.6`.

### Input

- `ConsoleHost` runs a thread that blocks in `ReadInput`; what it reads goes through the parser into a queue that the
  main loop waits on. No polling, so a key is handled as soon as it arrives.
- **A lone ESC** is either the Escape key or the start of a sequence. The parser keeps it, and the host's timer
  (`EscapeTimeoutMs`, 25 ms) flushes it as Escape when nothing follows. `ESC x` is Alt+x.
- Understood: cursor and function keys with xterm modifiers (`ESC [ 1 ; 5 C` = Ctrl+Right), SGR mouse (`ESC [ < b ; x ;
  y M/m`: press, release, drag, wheel), bracketed paste (`ESC [ 200 ~ … ESC [ 201 ~`), modifyOtherKeys
  (`ESC [ 27 ; mod ; code ~`) and CSI u (`ESC [ code ; mod u`, kitty). Control characters become Ctrl+letter; BS is
  Ctrl+Backspace (what Windows Terminal sends). Characters outside the Basic Multilingual Plane arrive as `?` (the grid
  holds one UTF-16 unit per cell).
- **Window size** is polled every 60 ms by its own thread. Windows consoles in VT-input mode send no resize events,
  and polling also spares a SIGWINCH handler on Unix. The thread starts from the screen's size, not by asking the terminal
  again: for four weeks it asked, so a change between creating the screen and starting (a pane split as the editor
  opened, `wt be a.txt ; split-pane`) was taken as the size all along, and the editor drew at the old width until the
  next resize (seen in Windows Terminal in Sandbox, 31 October 2026; a unit test failed on it before the fix).

### Key events on Windows (`WindowsKeys`)

A terminal program gets text, and text has no code for Ctrl with punctuation: with the Russian layout the "/" key
types ".", and Ctrl+/ arrived as "." or nothing. Far's shortcuts work in any layout because Far reads key events.
`"keyEvents": true` (the default; `BLUEEDIT_KEY_EVENTS=1` or `0` overrides it) does the same. It was a trial, off by
default, for an evening: Ctrl+/ with the Russian layout was tried by hand in Windows Terminal 1.25.2733 (2026-10-18),
it worked, and it was made the default.

**Measured** (2026-10-18) with a probe in a pseudo-console (pywinpty, as the UI tests), sent keys as Windows Terminal
sends them (win32-input-mode), read with `ReadConsoleInputW`:

| sent | VT input on | VT input off |
|---|---|---|
| F1, Up, Ctrl+Right | the VT sequence, a character per record, vk 0 | vk 0x70 / 0x26 / 0x27, Ctrl in the state |
| Ctrl+/ with the Russian layout | "." | vk 0xBF, Ctrl |
| Ctrl+1 | "№" | vk 0x31, Ctrl |
| Alt+F with the Russian layout | ESC "а" | vk 0x46, "а", Alt |
| an SGR mouse report | its characters | a mouse event record |
| a bracketed paste | its characters with the markers | the text, vk 0, markers gone |
| the answer to `ESC [ c` (device attributes) | its characters | **nothing** |
| the answers to `ESC [ 16 t` and OSC 4 | their characters | their characters, vk 0 |

An earlier note said key records keep their vk with VT input on; this probe did not see that, with one
exception: with VT input on, NUL (Ctrl+Space) comes as the console's own Ctrl+@ (vk 0x40, no character), which
`ReadConsoleW` read as NUL. The first version dropped it and broke Ctrl+Space (caught by CI). The key that types "@"
depends on the layout (on a CI machine it was not vk 0x40), so any Ctrl record without a character is NUL now.

**How it works.** The records are turned back into the VT text the parser already reads, so nothing after the
platform changes: special keys into xterm sequences; Ctrl or Alt with punctuation or a digit into CSI u with the key's
character on a US layout (`ESC [ 47 ; 5 u`, Ctrl+/); Ctrl+letter into its control character as before; Alt+letter
into ESC and the typed letter (Cyrillic letters are mapped by `KeyboardLayout`, as before); AltGr (right Alt with left
Ctrl) types its character. Enter, Tab and Escape stay as VT input gave them: nothing binds them with Ctrl, and
Ctrl+Enter must still be Enter. Mouse records become SGR reports (presses and releases from the change in buttons
held, a move with a button held is a drag, the wheel). Text that starts with ESC and has no key code is the
terminal's answer and passes as it is.

**A paste** (measured with a probe run in Windows Terminal 1.25.2733 itself, 2026-10-18): the console turns it into
key records, a press and a release for each character, hundreds in one read (512 at a time to a probe asking for
512). Characters the active layout has no key for (Latin letters under the Russian layout) come with vk 0; the rest
(space, Enter, digits, punctuation) with the key the console found for them. Typing gives one record a read. The first
version took only vk-0 characters for a paste, so every Enter of a paste was a typed Enter with auto-indent: a
staircase (seen on a screenshot). Now text (plain characters, Enter, Tab, keyless characters) is collected over one
read. What a paste typed in would harm is its line breaks, so it goes out as one bracketed paste when it has a line
break, or 16 presses or more; a short line is typed (completion opens, and a closing bracket of the text steps over
the one paired typing added). An Enter that ends it stays a typed Enter: a name and Enter typed in a burst (the UI
tests do that; "three presses are a paste" broke their dialogs) still answers the dialog. Releases are ignored and do
not cut a paste. Reads take up to 4096 records, so a paste comes in few pieces.

**The answer to `ESC [ c` needs VT input** (Sixel is learnt from it): `QueryGraphics` turns VT input on, and key
events come back with the answer or after a second. Keys typed in that second arrive as text, as before.

**Checked**: the unit tests (`WindowsKeysTests`); all 203 UI tests with `BLUEEDIT_KEY_EVENTS=1` (the harness sends
keys as Windows Terminal does); a UI test that types Ctrl+/ with the Russian layout, which fails with VT input.
**Not checked**: a real Windows Terminal (how it sends a paste: if as keys, not text, a pasted text would arrive as
typing, with auto-indent), conhost without Windows Terminal, ConEmu, and Windows Terminal 1.25's kitty protocol.
Over SSH to Linux only a terminal protocol can help: the next section.

### The kitty keyboard protocol on Linux and macOS

Over SSH the editor gets what the terminal sends as text, so Ctrl+/ with the Russian layout is a ".". The kitty keyboard
protocol lets a program ask for keys as `ESC [ code : shifted : base ; mods u`, where *base* is the key in the
standard Latin layout. With `keyEvents` on (the default) the host sends the query `ESC [ ? u`; only a terminal that
answers it (`ESC [ ? flags u`) gets the push `ESC [ > 5 u` (flags 1, keys with modifiers as escape codes, and 4, the
alternate keys), and the pop `ESC [ < u` when the setting goes off and on exit. A terminal without the protocol gets
only the query, which such terminals ignore (Neovim sends the same). With Ctrl or Alt the parser takes the base key;
plain text, Enter, Tab and Backspace still arrive as before (flag 8, all keys as codes, is not asked for).

Measured on 19 October 2026 with a probe on a Linux server reached over SSH, `ssh` from Windows Terminal 1.25
(PowerShell tab):

| key | arrived |
|---|---|
| answer to `ESC [ ? u` | `ESC [ ? 0 u` |
| Ctrl+/, Russian layout | `ESC [ 1105 :: 47 ; 5 u` (47 is "/") |
| Ctrl+/, English layout | `ESC [ 47 ; 5 u` |
| Ctrl+1, Russian layout | `ESC [ 8470 :: 49 ; 5 u` ("№", key "1") |
| Alt+x | `ESC [ 120 ; 3 u` |
| Esc | `ESC [ 27 ; 129 u` (128: Num Lock on, ignored) |
| Enter, a letter | `\r`, the letter |
| Ctrl+S | `ESC [ 115 ; 5 u` |

The same `ssh` in a pseudo-console of the system conhost (as the UI tests run programs), with keys sent as Windows
Terminal sends them, got plain text: "." for the Russian Ctrl+/, "№" for Ctrl+1, and no answer to the query. Windows
Terminal runs its own console (OpenConsole), which knows the protocol; the system one on build 26300 does not. On
Windows the editor uses its own key events (above), so the protocol is asked for only where those are not.

**Checked**: the unit tests (`InputParserTests` with the measured sequences, `KittyKeysTests` for the query, push and
pop); by hand on 20 October 2026, the Linux build over SSH to that server in Windows Terminal 1.25: Ctrl+/ with the
Russian layout comments a line, the rest of the keys as before. **Not checked**: kitty, WezTerm, foot, Ghostty.

### The main loop (`TuiApp`)

`Run` repeats: run posted work, draw, wait for an event (at most 250 ms), handle it. Rules that keep typing fast:

- **Input that is already waiting is handled before the next frame**: a held arrow key or fast typing never leaves
  the screen behind. This is capped at `CoalesceMs` (40 ms) so a flood of input still shows progress.
- **Nothing slow runs on this thread.** Spelling answers, grammar, language servers and models work in the
  background. When an answer arrives, the background code calls `TuiApp.Post(action)`: the action is queued and
  `Wake()` puts a `WakeInput` into the event queue, so a waiting loop returns at once and runs it. Views that only need
  a redraw compare a `Version` counter instead.
- `Tick` (every `TickMs`, 500 ms) is for periodic checks; the editor's checks for files changed on disk, reports
  grammar checkers that failed, and sends edits to the language server. It does not run while a menu or dialog is
  open.
- **Dialogs** (`RunModal`) run a nested loop and return their result directly, so code reads
  `if (Dialogs.Pick(...) is int i) ...`.
- The file picker (`FileDialog`) follows Far: Enter goes into a folder, `..` up and from a drive's root to the
  list of drives (Alt+F1 jumps there), F7 makes a folder. With a file name it is Save as: a name field under the list
  takes what is typed, Tab moves between them. It does not ask itself: whether to replace a file or make a typed
  path's missing folder is asked by the caller (`Dialogs.SaveFile`) with button dialogs, and a No reopens the picker
  where it was. A long path in its header keeps its end (the folder one is in), not its start.
- `Normalize` turns Alt/Ctrl + a Cyrillic letter into Alt/Ctrl + the Latin letter of the same key.
- Drawing order: root view (the editor with its bars), the menu bar, an open drop-down, dialogs top to bottom; the
  cursor is the topmost one's.

### Output (`Screen`)

- Two buffers: what was drawn now, and what the terminal shows. `Flush` compares them and emits only the differing
  cells, re-sending a colour or attribute only when it changes, plus the cursor. One frame is one `Write`; nothing
  changed means no output at all.
- Colours are 24-bit (`Style.Rgb`), or -1 for the terminal's own. Attributes: bold, underline, reverse, italic, dim.
- `Resize` and `Invalidate` force a full repaint on the next flush.
- **Scroll regions** (`UseScrollRegions`, setting `fastScroll`): when rows only moved, ask the terminal to move them
  (scroll region + SU/SD) instead of resending them. Off by default: Windows Terminal showed lines out of place with
  it, although the same output is right through ConPTY with a plain VT emulator.
- **Wide characters** (`CharWidth`): East Asian wide and full-width characters and the BMP emoji shown as pictures
  by default (`⚫`, `⭐`, `✅`) take two terminal columns. Counted as one, they shifted the rest of their row by a
  column, and the diff never cleared that extra column: scrolling a Markdown note with `### ⚫ Unknown — Not Yet
  Investigated` left a column of "d"s down the screen (seen in MobaXterm and reproduced under `script` on Ubuntu,
  replayed through pyte; 0.2.4 and earlier). Now a wide character is followed by a `Continuation` cell that nothing
  is written into; a change in either half rewrites the whole character, and half of one left without its other half
  (a popup's edge over it) is drawn as a blank. The editor makes its display text the same way
  (`Conceal.FitWidths`, the step after concealing), so the caret, the mouse and soft wrap count columns as the
  terminal does; a hard wrap never splits a wide character or a surrogate pair. Emoji outside the BMP (`🔴`) are
  surrogate pairs, two cells already, which matched their width before too.
- **Zero-width characters** (variation selectors, joiners) are left out of what is drawn: `⚠️` is drawn as `⚠`, one
  column everywhere. With the selector, terminals disagree on its width (one or two), so no count would be right in all.
- **Combining marks** (Unicode Mn, Me: an accent written apart from its letter, `e` + `◌́`, Hebrew and Arabic vowel
  signs) are drawn in the cell of the character before them (`Screen.Set` with marks, `Disp.Marks`), as every
  terminal draws them; one at the start of a line goes on a dotted circle `◌`.
- **Emoji sequences** are drawn so that no terminal joins them, since terminals disagree on whether they do (joined:
  one picture in two columns; apart: each part in its own): parts joined by ZWJ (`👨‍👩‍👧`) show as the parts, the
  joiner not being drawn; a skin tone (`👍🏽`) is left out; a flag's two regional indicators (`🇬🇧`) are drawn as their
  letters, `GB`. The text keeps them all.
- **The caret and Delete/Backspace** step over whole characters as a reader sees them (Unicode grapheme clusters,
  `StringInfo.GetNextTextElementLength`): a letter with its marks, a surrogate pair, an emoji sequence, a flag.
  Before, Left could stop between the halves of a surrogate pair and Backspace remove one half, leaving a broken
  character.
- **Pictures** (`Screen.Picture`, `CellPicture`, the editor's formula pictures): a picture covers whole cells and is
  sent as Sixel. What Windows Terminal 1.24 was seen to do (10 October 2026, a probe program and screenshots): it
  keeps a picture per cell, and text written into a cell erases the picture there; a picture 20 or 40 pixels tall
  (not whole six-pixel Sixel bands) leaves the row below alone; afterwards the cursor stands on the row below the
  picture, so one on the last row would scroll the screen. Hence: a picture's rows are marked in the cell buffer;
  `Flush` sends each run of changed rows of one picture as one Sixel image at its place, before the text, and the
  text pass skips those cells. A picture that moved or went is erased by the text written over its old cells; a menu
  over one covers its cells, and when it closes those rows are sent again. Nothing goes on the last row, and scroll
  regions are not used while a picture is on the screen. A picture is a coverage mask painted in its cell's
  foreground over the background (16 shades), every pixel, so whatever was under it is covered. Its cells are
  erased first (ECH, `CSI n X`): the text a picture is drawn over stays in the terminal under it, and a glyph that
  reaches past its cell (the stroke of a text integral `⎮`) showed a stray pixel or two on the row below the picture
  after scrolling (seen in Windows Terminal 1.24, gone with the erase).
- **Asking the terminal** (`ConsoleHost.QueryGraphics`): device attributes (`CSI c`; 4 in the answer = Sixel) and the
  cell size in Sixel pixels (`CSI 16 t`, answered `CSI 6 ; h ; w t`), sent after the start (in raw mode: before it,
  the answer would be echoed as text) and the cell size again after a resize. The answers are parsed as
  `TerminalReply` and kept by the host (`Sixel`, `CellPixels`), not passed on as input. Windows Terminal answers
  `?61;4;6;7;14;21;22;23;24;28;32;42;52c` and `6;20;10t`: 10 × 20 pixels a cell, whatever the font.
- **The window's title** (`Screen.Title`, `TuiApp.Title`): asked for with every frame and sent in it (OSC 0) only
  when it changed, without control characters (a file name could carry one). The editor's is "notes.md * - BlueEdit".
  Before (0.3.0 and earlier) nothing set it: the console window showed the path of `be.exe` (seen in the Windows
  Sandbox walkthrough). The title before ours is put back on exit: on Windows `Console.Title` is read at the start
  and sent back; elsewhere it is pushed onto xterm's title stack (`CSI 22;0 t`) and popped (`CSI 23;0 t`), which a
  terminal without the stack ignores. Through ConPTY the title reaches the terminal (the UI test reads it in pyte);
  the put-back title was not checked by a test.

## How it is checked

- `tests/Tui.Tests`: the parser (keys, modifiers, mouse, paste, split sequences), the screen diff, widgets and forms.
  They run on Windows and Linux.
- `tools/TuiProbe`: a tiny app on the layer that shows the last event it got. `tests/ui/test_tui_layer.py` drives it
  in a real console (winpty on Windows, ptyprocess on Linux) and checks what keys and pastes arrive as, and the latency: key written →
  glyph on the emulated screen, median under 15 ms, p95 under 40 ms (the harness itself costs about 6 ms).
- `tests/ui/test_performance.py`, the whole editor: key → glyph median under 20 ms, p95 under 50 ms on a large
  Markdown file; a key handled in under 8 ms (median); arrow keys in code do not repaint the screen (median frame
  under 3,000 bytes; a full repaint is about 12 KB).
- On Linux the editor was checked in a pseudo-terminal under WSL (ptyprocess + pyte): drawing, Cyrillic input,
  arrows, saving, quitting, spelling underlines. The whole UI suite runs there too: `tools/ci/check-linux.sh`.

## Pitfalls met

- C#: `"\x1b[2J"` is fine, but `"\x1bD"` is not ESC + D: `\x` takes up to four hex digits. Write `\u001b` before a
  letter that is a hex digit.
- Windows: `ReadFile` and `Console.In` end input at Ctrl+Z: use `ReadConsoleW`.
- Windows consoles in VT-input mode give no resize events: poll the size.
- The old console's font (Consolas in conhost, the console a double click on be.exe opens when Windows Terminal is
  not there) lacks `⟦ ⟧`: the buttons were boxes with question marks (seen in Windows Sandbox, 21 October 2026). They
  are `[ ]` now; `► ◄` are in it.
- WSL passes Windows Terminal's `WT_SESSION` to Linux programs; it does not mean the Windows build.
- A UI test that waits fractions of a second for the editor fails when the computer is busy (docs/ci.md).
