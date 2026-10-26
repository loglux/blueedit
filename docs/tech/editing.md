# Editing: decisions

Why some editing commands work the way they do. The code is `EditorView*.cs`; user docs are the README and F1.

## Where the code is (`src/BlueEdit`)

The editor began as top-level statements in one `Program.cs`, local functions sharing its locals; at 2,333 lines it
was hard to find anything in. In October 2026 it became a partial class `App`: those locals are its fields (typed as
the compiler had them), the wiring is its constructor in the same order, the local functions its methods, by area in
`App.Menus.cs`, `App.Documents.cs`, `App.Search.cs`, `App.Settings.cs`, `App.Status.cs`, `App.Proofing.cs`,
`App.ProofingSetup.cs` and `App.Code.cs` (100–530 lines each). Done by a program over the syntax tree (Roslyn), not by
hand, so nothing moved but the text; `Program.cs` keeps only the command line (`--help`, `--install-proofing`...).
The sources moved from the root to `src/BlueEdit` beside `src/Tui`; `BlueEdit.csproj` stays in the root, where the
scripts and the version are read from.

## The command is `be`

The program is BlueEdit; the file and the command are `be` (`be.exe`), since 20 October 2026. A console editor is
typed many times a day, and the ones people type are short: `vi`, `nano`, `mc` for Midnight Commander. Before, the
file was `blueedit.exe` with a `be.cmd` beside it that nobody saw. Nothing keeps the old name (no `blueedit` link or
shim): the editor was not public yet, and the one set-up there was (a Far association, `EDITOR`/`VISUAL`, PATH) was
moved over by hand. No other `be` command was found on the systems checked (Windows and two Linux machines). The
folders keep their names (`%APPDATA%\blueedit`, `%LOCALAPPDATA%\BlueEdit`): they hold settings and checkers, not the
command.

## The interface in other languages (`Ui.cs`, `lang/*.json`)

- **A table per language, keyed by the English string.** `lang/ru.json` maps `"_File"` to `"_Файл"`; the code
  writes English and passes it through `T()`. No numbered or named keys: the English text in the code stays the
  readable source, and a string no table has is shown as written. That is the fallback, so a gap or an old table
  never breaks the interface. A table is JSON with comments, built into the editor; the same file name in the
  settings folder's `lang/` adds a language or changes strings without a build.
- **`uiLanguage`**: `auto` (the system's UI culture, which on Linux .NET takes from `LANG`), `en`, or a code;
  `ru-RU` falls back to `ru`. `BLUEEDIT_UI_LANGUAGE` overrides it; the UI tests set it to `en`, so their checks of
  English text hold on any system.
- **A menu letter is a key, not a character.** The menus take Alt before the editor (`TuiApp`), so a Russian menu
  letter must not be one of the editor's Alt keys (C D G H I L M N Q R X Z). Each letter is mapped to its QWERTY key
  (`KeyboardLayout.Latin`, now with Х Ъ Ж Э Б Ю Ё), so Alt+Ф and Alt+A open the same menu whatever the layout. The
  bar is `_Файл Пр_авка П_оиск В_ид Фор_мат Инстр_ументы Б_лок Ок_но Помо_щь`. "Справка" was not possible:
  its only free letter, А, is Правка's. `UiTests` checks every table: each key is still a string in the code; every
  menu label is translated and marks one letter; no two menus, or items of one menu, share a key (`MenuBar` refuses
  them); no menu sits on one of the editor's Alt keys.
- **Values go into the string by number.** `T("Save changes to {0}?", name)` (`Tf` in `App.cs`): the key keeps
  `{0}`, `{1}`, so a translation can put them in its own order ("Символов, которых нет в {1}: {0}"). Pasting
  English pieces together would fix the English order. A user's table whose places are wrong gives the English
  rather than an exception; `UiTests` checks the built-in tables have the same places as the keys.
- **The same English word in two places** can need two words: "Save" under F2 is "Сохр" (the hints are short), on
  a button "Сохранить". `Ui.Tc("hint", "Save")` looks up `"hint|Save"` first, then `"Save"`. The recovery question
  does the same for its buttons (`recover|Discard` is "Удалить копии", while "Discard" of an unsaved file is "Не
  сохранять"). Only where a table needs it: one key per English string is the rule.
- **The terminal layer's own words** (hints such as "Enter = OK   Esc = Cancel", "File name: ", "Drives") pass
  through `Tui.Strings.T`, which the editor points at its tables. `src/Tui` knows nothing of `Ui.cs`.
- **Letters in dialogs are keys too.** A button is pressed by its first letter, now compared as a key
  (`KeyboardLayout.Latin`), so "Сохранить" is the С key in either layout; the buttons of one question start with
  different letters in each table ("Восстановить, Удалить копии, Позже"). A search option marks its letter with an
  underscore as a menu item does (`FormToggle("Match _case")`; "_Учитывать регистр" is Alt+У, the E key).
  Before, the letter was a separate character searched for in the label, which a translated label does not have.
- **Counts take the language's forms.** `Ui.N(n, "Saved {0} file", "Saved {0} files")` (`Tn` in `App.cs`): the
  key is the second; the table gives the forms split by `|` — two as English has, or three for Russian ("Сохранён {0}
  файл|Сохранено {0} файла|Сохранено {0} файлов"): one for 1, 21, 101; few for 2–4, 22–24 but not 12–14; many for
  the rest. The rule is chosen by the number of forms, so a table needs no code; it is Russian's (also Ukrainian's and
  Belarusian's), not Polish's, whose "one" is 1 only.
- **The status line's short words** ("Ln", "Col", "STREAM", "SPELL", "2E 1W") are `status|…` keys: "Стр", "Кол",
  "ПОТОК", "ОРФО", "2о 1п". The UI tests wait for "Ln " to know the editor is up; the Russian ones wait for "Стр ".
- **The tests that switch the language run alone** (an xUnit collection without parallel runs): the language is
  global, and the other tests read English messages.
- **A built-in table is whole.** `UiTests` collects every literal the code hands to `T()`/`Tf()`/`Strings.T()` and
  fails when a built-in table lacks one, so a new dialog string cannot ship untranslated by accident. Tables in the
  settings folder may be partial.
- **Done so far:** the menus, the key hints, the dialogs met every day (unsaved changes, save as, open, changed on
  disk, recovery, find, replace, find in folder, go to line, rename, encodings) and Tools > Settings with its help
  lines; the status line and its messages; the setup dialogs (grammar checking and its models, spelling languages,
  provider and dictionaries, code tools, terminal font, ignored rules, style), File > Info and the formula errors.
  F1 help too. Still English: what other programs say (a language server's messages, an exception's text; a model's
  explanations follow the paragraph's language anyway).
- **F1 help is a text per language in sections** (`lang/help.<code>.md`, built in; one in the settings folder's
  `lang/` comes first), not a table of its lines: a translation writes its own words, in its own order and length.
  `HelpTextTests` keeps it in step: the English sections in the same order (F1 opens one by its place), and every
  F-key and Ctrl shortcut of the English help named, in the same section (Alt+letter is left out: the menus' letters
  differ by language). The file is embedded with `WithCulture="false"`: MSBuild reads the `.ru.` of `help.ru.md` as a
  culture and would put it into a satellite assembly (`ru\be.resources.dll`), which the single-file `be.exe` never
  loads. A new language starts from `lang/template/strings.json` (every interface string, empty) and
  `lang/help.en.md` (`lang/template/README.md`).

## The F1 help: sections, laid out by the editor (`HelpText.cs`, `HelpDialog`)

Until October 2026 the help was one text aligned by hand, `lang/help.en.txt`: two columns of key and action, 155
lines in one scroll, all in one colour, within 86 columns. The user found it hard to read ("the letters blend,
everything is laid out too plainly": no headings that stand out, keys not set apart, no air between the parts) and
poor to translate: a translator had to count spaces to keep the columns and break lines by hand.

Now the source says what each line is and the editor lays it out:

```
# Files

F2, Ctrl+S         | Save
Shift+F2           | Save as...

In Open and Save as: F7 makes a new folder, ...
```

`# Title` starts a section; `keys | what they do` is a row (the spaces before `|` only help the eye); any other line
is a paragraph, lines one after another joined. `HelpDialog` shows the sections' titles at the left and the chosen
one at the right: the title in white, the keys in a column of their own in the menus' hotkey colour (a key too long
for the column, up to 26, gets a line of its own, its text under it), the text wrapped beside them, paragraphs
wrapped to the width, a blank line between the parts. Up and Down choose the section, PgUp/PgDn, Space, Home/End and
the wheel scroll it (▼ ▲ in the keys' row say there is more), Esc, Enter or F1 close.

F1 opens on the section of the file's kind: Markdown in a Markdown file, Code in a language with a language server,
else Basics. A translated help is found by the English titles' places, which is why a translation keeps the
sections in the same order (a test holds it to that).

The content is the old help's, all of it, in 14 sections; a row per key where the
old one paired two keys on a line.


## `be` in any console (`UserPath.cs`)

A release is a zip: unpacked into Downloads, `be.exe` starts only from its folder. **Tools > Command be in any
console** puts that folder in the user's PATH, or takes it out when it is there; Windows only (on Linux and macOS
it shows the `ln -s` into `~/.local/bin`).

- **The user's PATH, not the system's**: `HKCU\Environment\Path` is the user's own, so no administrator rights and
  no UAC question; Windows joins it after the system's PATH in every new process.
- **Written as stored**: read with `DoNotExpandEnvironmentNames` and written back with its kind kept
  (`REG_EXPAND_SZ`). `Environment.SetEnvironmentVariable(…, User)` was not used: it writes plain text, so entries such
  as `%USERPROFILE%\bin` would be saved expanded, and a later change of the variable would no longer reach them.
- **One entry**: the folder is compared with each entry expanded, without a trailing backslash and in any letter
  case; taking it out removes every copy.
- **New consoles only**: after the write, `WM_SETTINGCHANGE "Environment"` goes to all windows, so Explorer and a
  Windows Terminal started afterwards read the new PATH; consoles already open keep their own (the status line says
  so). Not offered on the welcome: it changes the system for every program, so it stays a choice in the menu.

## The first start (`Welcome` in `App.Settings.cs`)

A new user opens a console editor and sees an empty page: the key bar names F1 and F9, but nothing says that
spelling already works or that grammar checking is there to be set up. So the first start shows one dialog: the keys
to begin with (menu, help, save, open, quit), the spelling languages that work already (or where to get
dictionaries, on Linux), one line on grammar checking with its choices, and three buttons: Begin (also Esc and
Enter), Grammar... (opens Tools > Set up grammar checking) and Language... (the interface's languages, each named in
its own words, since the user may not read the one on the screen; English unless another is picked). The button is
not "Русский": the editor is for anyone, and more tables (`lang/*.json`) join the list by themselves.

- **One dialog, not a wizard.** A wizard of several pages fits an installer; here the editor is already running and
  every step it would have is a menu item. The dialog only points at them; nothing is installed or changed unless
  picked. A console wizard would also differ from the editor's own dialogs for no gain.
- **"First start" is "no settings.json".** Whatever is chosen (Esc too) writes settings.json from its template, so the
  welcome is shown once, with no extra flag to keep. An existing user has the file and never sees it; one who
  deletes it sees it again, which is what a fresh start means.
- **Started by git or another program** (EDITOR) on the very first start it shows too: Esc goes on to the file. Not
  worth a check for how the editor was started.
- The UI tests set `BLUEEDIT_WELCOME=0` (each test has an empty settings folder); `test_settings.py` shows it.
- The Sandbox walkthrough (`tasks newcomer`) captures it before pressing Esc.

## Saving (`SafeFile.cs`, `Write` in `App.Documents.cs`)

- **Through a temporary file.** The text goes to `.<name>.<random>.tmp` beside the file, is flushed to the disk, and
  then takes the old file's place in one step (`File.Replace`, Windows' `ReplaceFile`; a rename on Linux). Before,
  the file was written in place (`File.WriteAllBytes`): a power cut, a full disk or the process killed in the middle
  left a cut-off file, the old text already gone.
- **What the old file had is kept.** On Windows `ReplaceFile` keeps its attributes, permissions and creation time;
  on Linux the mode (rwx bits) is copied, since a rename would not. A symbolic link stays a link: the file it points
  to is replaced. Not kept: hard links (the other names still show the old text), and on Linux the owner, when the
  editor runs as another user who may write the folder. Neither case had come up when this was written.
- **Read-only stays read-only.** A rename needs only the folder to be writable, so it would replace a read-only file
  on Linux. The old file is first opened for writing (and closed, unchanged); that fails where writing in place
  failed: no permission, read-only, or held by a program that shares it with nobody (Word holds its documents so).
- **A folder that takes no new files** (only the file itself is writable) is written in place, as before.
- **A save that fails says so** in a "Not saved" dialog with the system's reason; the document stays unsaved, so
  closing still asks. Before, nothing caught the exception: the editor ended, and every unsaved document with it.
  Save as that fails keeps the document's old name. Tests: `EditorTests` (no temporary file left, a locked file, a
  link, Linux permissions) and the UI test `test_a_file_that_cannot_be_written_says_so_and_the_editor_stays`, which
  failed on the code before this change.

## Recovery (`Recovery.cs`, `StoreRecovery` in `App.Documents.cs`)

AutoRecover, as in Word: what was typed since the last save survives the editor being killed, an error, or a power
cut, up to the last few seconds.

- **Copies, never the files.** Every `autoRecoverSeconds` (10 by default, 0 = none) the documents with unsaved
  edits go into `session-<process id>-<random>.json` (a file per start) in `%LOCALAPPDATA%\BlueEdit\recovery` (`~/.local/share/...` on Linux):
  path, text, encoding, line ends. Saving into the user's file by itself was rejected: a half-finished edit would
  land in a file that git, a build or Obsidian is watching.
- **Typing never waits.** On the editor's thread the text is only joined into a string (a copy of the lines it is
  changing); the JSON and the write run in the background, while one write is still going the next is skipped (a later one catches up),
  and the same text is not written twice. A failed write is dropped silently: the next one tries again.
- **An error that ends the editor** (one nothing caught, around `app.Run`) writes the copies at once, gives the
  terminal back, says where the text is, and lets the error end the process as before. Not covered: errors on
  other threads, and a kill or a power cut, where the last periodic copy is what is left.
- **A normal exit removes the copy**; after each save it is rewritten without the saved document.
- **The next start** looks for copies whose editor is not running: a copy names its process by id *and* start time,
  and the process must be a BlueEdit. Process ids are reused: in the UI tests, run in parallel, a killed editor's
  id went to another BlueEdit within seconds, and with the id alone its copy was passed over as a running editor's
  (a CI failure). A file per start also keeps an editor given an old id from writing over, and then
  deleting, the copy of the one that had it. `HasExited` is not asked: another user's process refuses it with
  "Access is denied" (met in the tests with id 0); a start time that cannot be read counts as running. A copy older than its file (the file was saved after it) or saying what the file says is left
  out. The rest are offered in one dialog: *Recover* opens them as unsaved documents (a file's document with the
  file as it is now as the one on disk), *Discard* deletes the copies, *Later* keeps them for the next start.
- Several editors running at once each keep their own copy and never offer each other's.
- Tests: `RecoveryTests` and the UI test `test_unsaved_text_of_a_killed_editor_is_offered_at_the_next_start` (the
  editor is killed after a copy, the next start recovers the text, the file was never touched). The error path
  around `app.Run` has no test: nothing in the editor can be made to throw there on purpose.

## Paragraph formatting (`EditorView.Paragraph.cs`)

DOS editors could reformat a paragraph to a margin and align it; BlueEdit had only soft wrap.

- **What a paragraph is.** Neighbouring non-blank lines. In Markdown a heading or a list item starts a paragraph of its
  own, and lines that are not prose (code blocks, tables, display formulas on their own lines, headings) are not
  touched — the same `ProseLine` test grammar checking uses. The grammar checker's own paragraph (`ParagraphAt`) is
  not reused: it joins paragraphs across a blank line around a formula, which is right for a sentence that goes
  on after a formula and wrong for reformatting.
- **A selection** reformats each paragraph inside it on its own (joining them all into one would destroy the text),
  last first so the earlier line numbers stay valid; all of it is one undo step.
- **Words.** Runs between spaces, except that an inline formula (`$…$`) or code (`` `…` ``) is one word with its spaces:
  a line break inside one breaks the formula. Words are never hyphenated or split; a word longer than the margin gets
  a line of its own.
- **Prefixes.** The first line keeps its indentation, quote marks (`> `) and list marker (`- `, `1. `); the next lines
  take the quote marks and the marker's width in spaces, so a list item's text hangs under itself — what Markdown
  needs for the lines to stay in the item.
- **Right, centre and justify only in plain text.** They work by adding spaces; Markdown collapses spaces when it is
  shown, so in Markdown they would change nothing visible and leave clutter in the source. Reformat and align left are
  fine there. Code files get none of it: a line break or spaces in code change its meaning.
- **Justify** = reformat, then stretch every line but the last: the extra spaces go to the gaps from the left.
  Left, right and centre keep the line breaks as they are.
- **Auto-wrap** (`autoWrap`, off by default) runs after each typed character that is not a space: once the caret is past
  the margin, the line is cut at the last space at or before the margin and the rest goes to a new line with the
  continuation prefix. It belongs to the same undo step as the typing. A space typed past the margin waits for the next
  word. One word longer than the margin is not cut.
- The margin is a setting (`rightMargin`, 78 — the classic width that leaves room for a quote mark in replies), not the
  window's width: the file should not depend on how wide the terminal happened to be.

## Display formulas over several rows (`LatexLayout.cs`)

The reading view showed a display formula as one line of Unicode (`Latex.ToUnicode`): `\frac{x^2+1}{2x-3}` became
`(x²+1)/(2x-3)`. In the notes it is meant for (a vault of maths study notes, measured 6 October 2026: 614 notes,
99,478 inline and 16,205 display formulas) 40 % of the display formulas have a `\frac`, 2,570 have rows (`aligned`, `gathered`,
`array`), and `\left…\right` comes 7,348 times. A fraction read as a slash is the worst of it.

- **How**: a small box layout, as sympy's pretty printer does. Each piece is rows of text with a baseline row; pieces
  go side by side on their baselines, a fraction is numerator / `─` bar / denominator, a root a bar over what it
  covers, `∑` and `lim` take their limits above and below, `\left(…\right)` grows `⎛⎜⎝`, `aligned` / `cases` /
  `array` / matrices are grids (aligned pairs right then left, `cases` with a `⎧⎨⎩`, an array's `l c r`, `\hline` a
  rule), `\vec` and `\overline` a mark above, `\underbrace` a `╰─┬─╯` with its label, `\boxed` a frame, `\tag` at
  the right. Every character is one column.
- **A fraction and a factor next to it** get a space between them (`6/7 x`, not `─x`), and in the one-line form a
  fraction before a factor is put in brackets: `(6/7)x^(7/2)`, not `6/7x^(7/2)`, which read as 6/(7x); `x³/3`, not
  `(x³)/3` (10 October 2026, on a worked integral from the notes).
- **A root of one row** is written in one row, `√3` or `√(b² - 4ac)`: a bar on a row of its own floated above the
  sign, whose glyph does not reach the top of its cell (noticed in use, 10 October 2026). A taller one keeps the bar.
- **Integrals** are `⌠⎮⌡` as tall as the integrand and three rows at least, the limits at its top and bottom right
  (10 October 2026; before, a one-row `∫` beside a three-row fraction looked small). The integrand runs
  to the next relation (`dx = …`) or the next integral, so a fraction after `=` does not stretch the sign. `∮` has no
  tall form in Unicode and stays one row.
- **Scripts stay one row**: superscript characters when Unicode has them all (`x²`, `aᵢⱼ`), otherwise the script
  written out one row up or down (`e` with `-x²/2` above it). Raising whole boxes for every exponent made formulas
  twice as tall for no gain in reading.
- **Spacing as in print** (`Latex.SpacedChar`, `KeepSpace`; 11 October 2026), in the one-line form and the rows alike:
  the source's own spaces are not what is shown, as in TeX. A relation (`=`, `<`, `>`, and `≤`, `≠`, `∈` … from
  commands) gets a space on both sides, `+` and `-` too unless they are a sign (first, or after an operator or an opening
  bracket: `-b`, `(-1)`); exponents, indices and one-row fractions are tight (`e⁻ˣ⁺¹`, `i=1` under `∑`), as TeX sets
  scripts; a space between a number and a letter goes (`2 a` is `2a`); `\text{…}` is left as written. A sign before a
  stacked fraction is set off from its bar ("- ─", not "-─", read as one longer bar). Why: a book converted from PDF in
  the vault writes `a^{2}+\frac{2 a}{n}<2`, and its formulas came out as `a²+─<2` and `2 a`, hard to read, while
  hand-written notes with spaces looked right (noticed in use, 11 October 2026). A monospaced terminal has no kerning: the
  only thing to set is these spaces.
- **Where the rows go**: the editor lays lines out in visual rows already (soft wrap), so a formula's picture is given
  to the lines of its `$$` or `\[ … \]` block in turn, the block's last line taking the rows that are left
  (`Disp.RowWidth`: a "line" whose wrap segments are the picture's rows). Caret, scrolling and the row map work
  unchanged. The `$$`, `\[` and `\]` lines stay blank around it.
- **Live preview** draws the pictures too (10 October 2026), except for the formula the caret is in: all its lines,
  delimiters included, are shown as written while the caret is on any of them, as Obsidian does. Nothing changes height
  under the caret while it moves inside the formula, and entering or leaving it re-lays only that block's lines
  (`FormulaLines`, `LiveRowChanged`). Before, Live preview never drew pictures, because the caret's line alone was
  shown as written and a picture would have changed height under it.
- **A line of prose with a tall inline formula** (`$…$` with a fraction, a tall integral, a script Unicode has no
  characters for, such as `x^{5/2}`) is laid out over rows too, its words on the formulas' baseline
  (`Conceal.InlinePicture`, 10 October 2026): "Интеграл: ∫₁³ (3x^(5/2) + x²)dx" in one row was hard to
  read. A line whose formulas all fit in one row (`x²`, `α ≤ β`) stays one row and wraps as before. In Live preview
  the caret's line is shown as written, so it becomes one row when the caret enters it.
- **When it is not used**, the one-line form is: soft wrap off (one line is one row then); a picture wider than the window
  (a long line with a tall inline formula then wraps in one-row form).
- **Measured**: all 15,859 display formulas of the vault laid out without an exception, 10,489 of them over more than
  one row; width median 23 columns, 90 % within 46, the widest 213; height median 3 rows, 90 % within 6. Commands left
  as written afterwards: `\not` 14 times, `\substack` outside limits 11, a few rarer ones. A 7,525-line note with 815
  display formulas draws in the reading view in 5 ms, the layout of every line included; pictures are cached by the
  formula's text.

## Formula pictures (Sixel)

The second step after the text layout above: display formulas typeset, as pictures, where the terminal can show them
(user docs: [formula-pictures.md](../formula-pictures.md)). It was asked for as a part to switch on, not for everyone
(10 October 2026).

- **An optional module, loaded from a folder** (`src/MathImages`, `FormulaImages.cs`). CSharpMath (a TeX layout
  engine for .NET, MIT) draws with SkiaSharp (MIT), whose native library alone is 11.6 MB, the module 14 MB in all —
  more than the rest of the editor needs, for a feature few use. So it is a release asset of its own
  (`blueedit-mathimages-<version>-win-x64.zip`, `…-linux-x64.tar.gz`), loaded into an `AssemblyLoadContext` of its
  own when found; the editor has no reference to it and finds `Renderer.Render` by name, checking `Renderer.Api`.
  In the editor's process, not a helper process: a helper would carry a .NET runtime of its own (the editor is one
  self-contained file). A formula the module cannot read comes back with CSharpMath's message, and an exception from
  it leaves that formula as text; a crash inside Skia's native code would take the editor down — not seen on the
  17,222 formulas below.
- **Why CSharpMath**: TeX's layout rules and a real math font (Latin Modern Math) in .NET. MathJax and KaTeX need a
  JavaScript engine (Node or a browser); a real LaTeX (MiKTeX with dvipng) is a TeX installation of its own and a
  process per formula (not tried); WpfMath is WPF, Windows only.
- **Coverage**, on those notes' 17,222 display formulas: CSharpMath reads 12,595 as they are (73 %). Rewrites
  of what notes use and it does not know (`equation*` and `align*` wrappers, `gathered`, `\tag`, `\prime`,
  `\boldsymbol`, `\dfrac`, `\bigl`, `\theta=` taken for one command…; `Renderer.Normalize`) bring it to 17,119
  (99.4 %), 17,116 of them drawn. Left as text: `\substack` 28, `aligned` with more than two columns 13, `\overbrace`
  8, `\rvert` 8, `\underset` 6, a few rarer.
- **Speed**, the same formulas, one thread: median 0.58 ms per formula with the Sixel encoding, 90 % within 1.75 ms,
  all of them in 14.7 s; the first one about 0.2 s (the fonts are read). Drawn on a thread of their own, the ones on
  the screen first; a formula is text until its picture is ready.
- **The layout sees new pictures only when it is redone** (`FormulaImages.Taken`, then `FormulaPicturesChanged`): a
  picture arriving between the layout and the drawing of one frame gave a line a different height than its rows had
  (caught by a test). The relayout keeps the row at the top of the window where it is, so pictures arriving above it
  do not move the page.
- **Size**: the font is 0.75 of a cell's height (15 px for Windows Terminal's 20), which puts a fraction in two or
  three rows, as the text layout does. Median 22 columns × 2 rows (the text layout: 23 × 3), 90 % within 43 × 4;
  Sixel output median 3.7 KB, 90 % within 8.5 KB, sent again whenever a picture moves.
- **Sharpness**: Windows Terminal draws Sixel on 10 × 20 pixels a cell whatever the font and scales that to the font
  (14 × 28 in the window measured); thin strokes came out pale beside the text, so coverage is raised to the power
  0.6 before the 16 shades between the background and the theme's formula colour.
- **The terminal**: device attributes and cell size are asked after the start ([tui.md](tui.md)), only when the
  setting is on and the module present; a terminal that does not answer both gets the text layout. In UI tests the
  module is off (`BLUEEDIT_MATHIMAGES=off`): a computer that runs the tests may have it installed, and the tests'
  screen emulator (pyte) shows no pictures.
- **Not done**: inline formulas (`$…$`) as pictures; pictures in the Source view; terminals other than Windows
  Terminal were not tried; the kitty graphics protocol (another way to send pictures) is not used.

## Line numbers (`EditorView.Gutter.cs`)

- **Where.** A column at the left of the text area, as wide as the last line's number plus one space; the caret's line
  in the text colour, the others in the comment colour. Everything that places text on the screen — drawing, soft-wrap
  width, the caret, mouse hits, horizontal scrolling — works in `TextArea`, the area right of the column, so none of
  it needed its own offset. A click on a number puts the caret at the start of that line.
- **Wrapped lines** are numbered on their first row only, as in VS Code; a formula laid out over several rows likewise.
  The column widens when the file passes 9, 99, ... lines; the wrap layout is cached per row width, so it is rebuilt
  then by itself.
- **Off by default, one setting for every file** (`lineNumbers`). Alt+G (or View → Line numbers) turns them on or off
  for all open files and saves the setting, so the next start shows them as they were left. 0.2.4–0.2.5 had them on
  for code and off for prose (`lineNumbersCode`, `lineNumbersProse`) and Alt+G for the current file only, not saved:
  in use the toggle was lost on every file opened and the numbers were in the way when reading, code included. The two
  old keys are passed over without a warning.
- **Narrow windows**: with fewer than 20 columns left for the text the numbers are not shown.
- **Alt+G**: Alt+N (the obvious one) already makes a numbered list; G as in Ctrl+G, go to line.

## The View menu

- Soft wrap, line numbers, the outline, the Markdown views, the themes and the timing display had sat in Tools among
  the checkers and the settings, where line numbers were not found. They are now in their own menu after Search.
- **Alt+V** opens it, so S, L or R after it picks a Markdown view (Source, Live, Reading). Alt+V used to go round the
  three views; the bar takes Alt+letter before the editor does, and every other letter of "View" was taken (Alt+I is
  italic, E is Edit, W is Window). One key more, and the view is named rather than found by going round. Ctrl+E still
  toggles Reading.

## The state in the menus (`MenuItem.On`, `Choice`, `Value`)

**The rule (the user's, 26 October 2026): an option that is on is seen to be on in the menu, so one always knows where
one is.** Every item that switches something has a mark or a value; an item without one is an action, and its name
says what it acts on. A key that switches a setting keeps it in the settings, so the menu and Tools > Settings never
disagree. A new toggle follows this from the start.

The user could not tell what was on: "Spell check on/off", "Line endings CRLF/LF" named both states, and the menu did
not say which held now or what a press would do (26 October 2026). Now, as in Far's menus:

- `√` in the blank column before the label: what is on — soft wrap, line numbers, formula pictures, timing,
  auto-wrap, spelling, column mode. "Spell check on/off" became "Spell check".
- `•`: the chosen one of several — the Markdown view (in a Markdown file only), the theme.
- The value at the right, where a key would stand, for a two-valued setting without a key: "Line endings … CRLF".

The state is asked whenever the menu is drawn (`Func<bool>`), so it is never out of date and never kept twice.

Ctrl+F12 (Format code) is an action, not a toggle, but its result was not seen either: a formatted file looks much the
same, and the status line said only "Formatted by ruff". It now says how many lines changed ("lines changed: 3"), or
that the program changed nothing. The lines are counted without a full diff: the new text's lines the old one lacked,
or the lines that went, whichever is more (`App.ChangedLines`). "(language server)" left the item's name: ruff or
black format too.

Then the user asked whether Align left, right, centre and Justify were modes and which one was on. They are actions,
once, on the paragraph at the caret (or the selected ones); now named "Paragraph: align left", "Paragraph: centre",
and so on. And Tools > Settings said "Spell checking: on" after F8 had turned spelling off, because F8 and Alt+Z changed
only the editor and Alt+G alone kept its setting: now all three keep theirs (Alt+Z for text or for code, by the file).
Formula pictures has its √ while it is on even when no picture is shown, so the reason stands at the right: "no
module", "module failed", "not in this terminal".

## Scrolling in the reading view (`EditorView.ScrollReading`)

- The caret is not drawn in the reading view, but the arrows used to move it as in editing: the page did not move
  until the hidden caret reached its edge, and the first arrow after the mouse wheel jumped back to where the caret
  was (noticed in use, 10 October 2026).
- Now the keys move the page, as in a viewer: ↑/↓ a row, PgUp/PgDn and Space/Shift+Space a page, Home/End the start
  and the end (the last page full); the wheel 3 rows, as elsewhere. The hidden caret is kept on the first row shown,
  so the status line's Ln, a search and leaving the reading view go on from what is on the screen.

## The line count in the status line

`Ln 12/340`: the caret's line and the number of lines. The count is the editor's, the one the line numbers end at and
Ctrl+G goes to: a file that ends with a newline has an empty last line the caret can stand on, so it counts one more
than `wc -l` does.

## The settings window (`ShowSettings`, `OptionsDialog`)

Settings live in `settings.json` with comments, as Windows Terminal and VS Code keep theirs: everything in one place,
explained where it is, easy to copy to another computer. Tools > Settings opened that file and nothing else; a user
asked for a form too, or both (11 October 2026), having not found how to get back from the file to the text either
(Esc does that now, `EditorView.EscapeCloses`).

Both, then: the window lists the everyday settings, each with its values (Space or ← → change one, the selected one's
help underneath), as Far's configuration dialogs do; its last row opens the file, which has all of them. Enter writes
only what changed into the same file with `Settings.SetValue`, which keeps the user's comments and layout, and applies it
at once, as View > Theme always has. So there is one store, and whatever the window cannot show (grammar checkers,
language servers, models, colours) stays in the file. Not in the window: lists and paths, and the settings that have
their own menu already (Grammar checking, Spelling languages, Terminal font).

## The look of the windows, and the F7 window (`Draw.Box`, `FixDialog`)

The windows keep their DOS look: a light window with a heavy frame, the title set into it, and a dark shadow right of
and below it, falling on the text. On 24 October 2026 a Far-like look was tried beside it (a margin of 2 columns and
1 row round a thin double frame, the title centred), both captured in a real console in Windows Sandbox: the user saw
hardly any difference, and the margin cost room (the tall windows, Settings and Help, then covered the menu and key
bars). So it stayed as it was.

What was wrong was the F7 window. It was a list: the fixes, then `[ Ignore ]` and `[ Never show this ]` as rows, with
the checker's explanation in the title, cut to the frame's width ("The plural subject "examples" takes the plural
verb…"). Looking at it the user found three things unclear: the highlighted row, a full-width blue bar, read as a field
to type into; a fix, `check`, read as a command or the word being checked; and the model's name next to it
(`check — claude-haiku-5-5: …`) as a second thing to choose, though the model is set in the settings and cannot be
changed there. Marking the selected row with `►` and adding a line of keys under the list did not help: it was still
not a list of like things.

The window now follows the Obsidian plugin's popup (`obsidian-blueproof`, `src/editor/popup.ts`): what to do, then
why, then, small, who says so and how to hide it. `FixDialog`:

```
┏┥Grammar: "checks"┝━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━┓
┃                                                          ┃
┃ [► check ◄]                                              ┃   the fixes, as buttons
┃                                                          ┃
┃ The plural subject "examples" takes the plural verb      ┃   the explanation, whole, wrapped at words
┃ "check", not "checks".                                   ┃
┃                                                          ┃
┃ [  Ignore  ] [  Never show this  ]     claude-haiku-5-5  ┃   the actions; at the right, in grey, who found it
┗━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━┛
```

- The buttons look and behave as in the other questions (`ButtonDialog`): `► ◄` on the one Enter presses; Tab and
  ← → go along all of them, ↑ ↓ to the first button of the row above or below, Esc closes. No letter keys: the fixes
  are words, and their first letters would clash.
- The title names the words in question. A fix of another stretch of the text (another checker's finding nearby)
  says what it changes: `examples checks → examples check`. Each checker's explanation is shown once, another
  checker's signed with its name. The right of the bottom row lists the checkers, LanguageTool with its rule
  (`LanguageTool · AGREEMENT_SENT_START`): what *Never show this* switches off.
- With no fix (LanguageTool often only names the problem) the fixes' row says so.
- The spelling window (`Spelling: "recieve"`: the suggestions, *Add to dictionary*, *Ignore*, `Windows spelling` at
  the right), the mixed-alphabet word and a formula's rule of writing (`LaTeX`) use the same window.

The other lists (the outline, the file picker, the encodings, the models) stay lists, without the marker: there the
rows are like things, one picked from many, and ↑ ↓ move along them.

The other windows were looked at the same way on 24 October 2026, each opened and captured (a script driving the
editor in a pseudo-console); four had the same fault in smaller ways:

- **Tools > Code tools** listed the tools and, as rows of the same list, *Install all that are missing* and *Remove*;
  Enter on a tool's row installed it, which nothing said. Now the tools are a table to read (found where, installed
  here, or what installing takes), with the buttons *Install the missing*, *Remove what was installed here* and
  *Close* under it (`FixDialog` with `table`: the lines kept, the window as wide as they are).
- **Tools > Set up grammar checking** had *Remove...* as a row among the ways to check grammar: now a button under
  the list (`ListDialog` with `buttons`, reached with Tab, the keys said under it).
- **Tools > Grammar checking** had square boxes, which say several can be ticked, but ticking one cleared the others
  until the last row, *Several at once*, was ticked. Now the checkers have round marks, `(•)`, while one is the rule,
  and boxes, `[x]`, once several are allowed (`CheckListDialog` with `radio`).
- **Lists of one choice** (encodings, the spelling provider, the grammar model, a server's models, the interface
  language) opened on the value in use, but the highlight alone did not say it was the one in use: that row now ends
  in `(now)`.
- **Save as**: its line of keys ran past the frame; a list window is now at least as wide as that line.

The rest were left as they are: the questions with buttons (unsaved changes, the terminal profile, `be` on `PATH`),
the fields (Find, Replace, Go to line), Settings (a value changed with ‹ ›, what the row means under the list), the
check lists (dictionaries, spelling languages, style) and the plain lists (the outline, the files).

## Indent after a block's opener (`EditorView.BlockOpens`)

Enter kept the indentation of the line above and nothing more, so after `def func():` the caret went to column 0 (a
user, 11 October 2026: "it cannot do elementary things"). Now, with `autoIndent`, a line that ends (spaces aside) in a
block's opener gives the new line one step more (`tabSize`): `:` in Python and YAML; `{`, `[`, `(` in C#, C/C++, Java,
JavaScript, Go, Rust, PowerShell, JSON and shell. With the caret between an opener and its closer (`{|}`, which
`autoClose` makes), the closer goes to a line of its own at the opener's indent, as VS Code does. Plain text, Markdown
and other languages keep the indentation only. Not done: a trailing comment after the `:` (`if x:  # note`) is not
seen through, and nothing dedents by itself after `return` or on typing `}`.

## Code editing basics (`EditorView.Code.cs`)

Asked for on 11 October 2026 ("what else does a code editor need"), taken from what VS Code does:

- **Ctrl+/** comments the line or the selected lines out with the language's line comment (`#`, `//`, `--`, `::` for
  Batch), at the lines' smallest indent, or back in when all of them are comments. Terminals send Ctrl+/ as 0x1F
  (Ctrl+_), so both keys do it; checked by hand in Windows Terminal. **Only with a Latin layout**: measured by sending
  the console the key events Windows Terminal sends and reading what a program gets, Ctrl+S with the Russian layout
  (the key event's character 0 or "ы") still arrives as 0x13, because the console maps Ctrl+letters by the key, but
  Ctrl+/ there (the same key types ".") arrives as nothing, or as a plain ".". The terminal protocol has no code for
  Ctrl with punctuation; getting the key itself needs Windows Terminal's win32-input-mode, which other terminals
  lack, so it is left as it is (agreed on 11 October 2026); a second key on a letter is the way if needed.
- **Alt+Up / Alt+Down** move the line or the selected lines past the neighbouring one, the caret and selection with
  them. Not checked: whether Windows Terminal keeps Alt+arrows for moving between panes when a tab has several.
- **A closer typed on an empty line** (spaces only before the caret) in a brace language goes to the indent of the
  line that has its opener, found by counting brackets outside strings and comments.
- **The bracket at the caret** (on it or just before it) **and its pair** are drawn bold and underlined. The pair is
  looked for at most 1,000 lines either way on every frame, outside strings and comments; token lines are cached, so
  the search is a scan of characters.

## Formatting by the language server (`FormatCode` in `App.Code.cs`, `EditorView.ApplyEdits`)

Ctrl+F12 asks the file's server for `textDocument/formatting`, or `textDocument/rangeFormatting` for the selected lines
when the server offers it. Whether it can is read from the capabilities of its `initialize` answer
(`documentFormattingProvider`, `documentRangeFormattingProvider`), kept per server; pyright offers neither (checked
with pyright 1.1.414), csharp-ls both (0.28.0).

- **Typing does not wait.** The answer comes in the background (at most 30 s) and is applied through `TuiApp.Post`
  only when the file's edit version is still the one asked about; otherwise it is dropped with a note.
- **Edits apply as one undo step.** The server's TextEdits all refer to the text it was given, so they are applied
  from the last to the first in a single string; overlapping or out-of-range edits reject the whole answer. The caret
  is moved by what changed before it, so it stays on its text.
- **The key.** Ctrl+Alt+L (JetBrains) was tried first: measured by sending the console the key events Windows Terminal
  sends, Ctrl+Alt+L arrives as plain Ctrl+L (0x0C, no ESC) — the Windows console takes Ctrl+Alt for AltGr — and Ctrl+L
  deletes the line. Shift+Alt+F (VS Code) arrives as ESC F, told from the File menu's Alt+F (ESC f) only by the letter's
  case, which Caps Lock flips. Ctrl+F12 arrives intact (`ESC [24;5~`) and sits with F12 (definition) and Shift+F12
  (back).

### Formatting programs (`Formatters.cs`)

pyright has no formatter, and Python is the language the editor was most used with, so Ctrl+F12 also runs a
formatting program (asked for on 11 October 2026: "calling outside programs is fine, as long as it is well thought out
and gives the user no trouble").

- **Which one.** A formatter named in the setting `formatters` comes first (the user chose it); then the language
  server's, when it has one; then the first installed default — Python: ruff, then black. ruff first: one program
  without Python, and its output for ordinary code is black's (it is built to match).
- **How.** The text goes to the program's standard input, `--stdin-filename` tells it the file's path, and it runs in
  the file's folder: so it finds the project's `pyproject.toml` / `ruff.toml` (tested: ruff's `quote-style`, black's
  `skip-string-normalization`), and the text need not be saved. UTF-8 both ways (`PYTHONIOENCODING` for black on
  Windows). A non-zero exit leaves the text alone; the first line of its errors goes to the status line. 30 s at most;
  the program is killed after that.
- **One edit.** The output replaces only the part between what the old and new texts share at their start and end
  (`Formatters.Diff`), applied by the same `ApplyEdits` as the server's edits: one undo step, the caret outside the
  changed part stays on its text. A real line diff was not needed for that.
- **The whole file.** ruff (`--range`) and black (`--line-ranges`) can format lines, with different arguments and
  minimum versions; not done yet: with a selection the whole file is formatted and the note says so.
- **Finding black.** `pip install --user black` puts `black.exe` in `%APPDATA%\Python\Python3xx\Scripts`, which pip
  only warns is not on `PATH`; `LspClient.Resolve` looks there too. On Linux pip's `~/.local/bin` was already searched.
- **Installing.** `install.ps1 -Python` adds ruff with winget (`astral-sh.ruff`, per user, no administrator);
  `install.sh --python` takes ruff's release archive from GitHub into `~/.local/bin`, because Ubuntu 24.04's pip
  refuses `--user` installs (PEP 668). Checked in WSL: ruff 0.17.0 downloaded and formatted.

## Renaming (`RenameSymbol` in `App.Code.cs`)

Ctrl+F2 asks the server for `textDocument/rename` and applies the WorkspaceEdit it answers with.

- **The key.** F2 renames in VS Code, but F2 saves here (as in Far and DOS editors); Shift+F6 (JetBrains) goes to the
  previous document. Ctrl+F2 keeps F2 in the name and arrives intact (`ESC [1;5Q`).
- **First ask whether.** When the server offers `prepareRename` (pyright and csharp-ls do), it is asked before the name
  dialog: in a string or on a keyword it answers null and the note says so, instead of a dialog that leads nowhere.
  But a null is not trusted on a name in code: on a CI machine pyright answered null on `greet` right after
  starting (why was not found; locally it answers), so there the dialog opens anyway and the rename's own answer decides;
  in a string or comment (the editor's own tokens) the note comes at once.
  Its range or placeholder gives the old name; otherwise the word at the caret. The dialog shows the old name, and
  typing replaces it (`TextInput`'s replace-on-type, as the Find dialog's last term).
- **Files not open.** The edits are applied through the editor to every file — the open ones in place, the others
  opened in a tab — and nothing is written to disk. VS Code does the same; the reason here: a rename across files is
  a change one wants to look at, every file keeps its own undo step, and **File → Save all** (added with it) writes them.
  The note counts the changes, the files and the ones opened.
- **What is refused.** A WorkspaceEdit that also creates, renames or deletes files (`documentChanges` with a `kind`):
  nothing is applied, the note says why. Edits that do not fit a file's text (it changed since the server read it)
  leave that file alone and are named in the note.
- **Not done:** the server is told about the other files' new text only when each is shown (the text of the shown
  file goes to the server each tick), so their underlines may lag until then.

## References (`FindReferences` in `App.Code.cs`)

Alt+F12 asks the server for `textDocument/references` (the declaration included) and shows the places in the list
that Find in folder (Alt+F7) already had: one line per place with the file, the line number and the line's text, Enter
opens the file with the name selected, and Search → Folder results brings the last list back, whichever filled it.

- **The key.** VS Code's Shift+F12 goes back from F12 here; its Shift+Alt+F12 is three keys. Alt+F12 sits with F12
  (definition), Shift+F12 (back) and Ctrl+F12 (format) and arrives intact (`ESC [24;3~`).
- **The lines** come from the open document when the file is open (its unsaved text, which the server also has), and
  from disk otherwise. Paths are shown relative to the project root the server works in.
- **Sorted** by file and line, not in the server's order (pyright's is not sorted); the list starts at the reference
  under the caret, so Up and Down go to the neighbours.

## The outline of code (`ShowOutline` in `App.Search.cs`, `LanguageServers.OutlineFrom`)

Ctrl+O, the headings of a Markdown or LaTeX file, lists a code file's symbols from `textDocument/documentSymbol`.

- **Trees, asked for.** Without `hierarchicalDocumentSymbolSupport` in the client's capabilities pyright answered with
  a flat SymbolInformation list (measured: its locals `name`, `word` came as variables of `greet`) and the range of
  the whole definition (column 0, not the name); with it, a DocumentSymbol tree with the name's own range. Both forms
  are read: in a flat list the level comes from `containerName`.
- **Locals left out.** The children of a function, method or constructor are its parameters and local variables: not
  an outline. They are dropped in both forms (in a flat list by the container's kind).
- **The list** shows each symbol indented by its level with its kind on the right, starts at the last symbol at or
  above the caret, and Enter goes to the name.

## Quick fixes (`QuickFix` in `App.Code.cs`)

F7 on an error or warning in code, which showed its message, first asks the server for `textDocument/codeAction` over
the finding's range, with the finding handed back as the server sent it (`LspDiagnostic.Raw`: Roslyn matches its fixes
by the diagnostic's code and data, which the parsed record did not keep).

- **The key.** VS Code's Ctrl+. cannot be sent by a terminal (measured: `docs/tech/editing.md`, Code editing basics);
  JetBrains' Alt+Enter is Windows Terminal's full screen. F7 already meant "what is wrong here and what to do"
  for spelling and grammar, and Shift+F7 already goes to the next code finding.
- **What the client declares.** `codeActionLiteralSupport` (else servers may answer with bare Commands only),
  `isPreferredSupport`, `disabledSupport`, `dataSupport`, `resolveSupport` for `edit`, and `workspace.applyEdit`.
- **csharp-ls leaves the edit out** (measured, 0.28.0): its actions carry only a title and `data`; the edit comes from
  `codeAction/resolve` when one is chosen. Its preferred `using System.Text;` comes first in the list; the others
  (generate the type in this or a new file…) follow in its order. Disabled actions are not listed.
- **Commands.** An action that is (or has) a command is sent back with `workspace/executeCommand`; the server may then
  send `workspace/applyEdit`, which the client answers at once and the editor applies on its own thread.
- **Applying** is the rename's: each file one undo step, files not open opened unsaved.
- **pyright offers nothing** for an undefined name (measured with 1.1.414): F7 shows the message there, as before.

## Code tools installed by the editor (`CodeTools.cs`)

How the language servers get onto a computer was the hard part (as a user put it, "that is the million-dollar
question: how to do it elegantly"). It was worked out on two Linux machines: a NAS with no apt or opkg and no Node.js
or .NET, and an Ubuntu 24.04 server with node but no npm, where apt needs sudo. Both are x86_64 with glibc 2.39, which
the projects' own builds run on, so the editor installs them itself, into one folder of its own, without root:

- **Node.js**: the newest LTS from `nodejs.org/dist/index.json`, its `.tar.gz` (no xz needed: .NET reads tar and gzip,
  not xz) or `.zip`, checked against `SHASUMS256.txt`. pyright then with that npm into `tools/npm`
  (`npm install --prefix`, so no global folder). A Node.js with npm already on `PATH` is used instead.
- **ruff**: `github.com/astral-sh/ruff/releases/latest/download/<archive>`, checked against the `.sha256` published
  next to it; no GitHub API (no rate limit).
- **.NET SDK 10**: the newest SDK in Microsoft's release metadata
  (`builds.dotnet.microsoft.com/dotnet/release-metadata/10.0/releases.json`), whose entry gives each archive's address
  and SHA-512. Microsoft's `dotnet-install.sh` was used first, but it needs bash, and the NAS has only busybox `sh`
  (measured: "bash: not found"); the metadata way needs no shell and checks the archive. csharp-ls then with
  `dotnet tool install --tool-path`. An SDK already on `PATH` is used instead.
- **Found and started**: `LspClient.Resolve` searches the folder after `PATH`; `LspClient.StartInfo` puts our Node.js and
  .NET first on the started program's `PATH` (pyright's launcher runs `node`) and sets `DOTNET_ROOT` (csharp-ls's
  launcher looks for .NET there when it is not in a system place).
- **Measured**: on Windows (PATH cut to System32) pyright and ruff installed from the menu and from the command line,
  Node.js 24.21.0, pyright 1.1.414, ruff 0.17.0 (Node.js 102 MB, pyright 19 MB, ruff 25 MB), Ctrl+F12 then formatted
  with that ruff. On the Ubuntu 24.04 server `be --install-tools` installed all three; started with only these on
  `PATH`: csharp-ls 0.28.0, pyright 1.1.414, ruff 0.17.0. Sizes there: Node.js 208 MB, pyright 38 MB, .NET 624 MB, csharp-ls 99 MB, ruff 23 MB.
- **On the NAS** (busybox, no package manager): all three installed with `--install-tools`; pyright 1.1.414, ruff
  0.17.0 and csharp-ls 0.28.0 (.NET SDK 10.0.401) run with only these.
- **TypeScript and JavaScript: TypeScript 7's own server.** typescript-language-server (the usual choice, a wrapper
  over the typescript package's `tsserver.js`) was tried first, with npm beside pyright: with `typescript@latest`, now
  7.0.2, it stopped with "Could not find a valid TypeScript installation" (measured), since TypeScript 7 is a native
  program without `tsserver.js`. Its `tsc` has a server of its own, `tsc --lsp --stdio`. It comes as one npm package
  per system, `@typescript/typescript-<os>-<arch>`, which holds the program and its `lib.*.d.ts`; the editor takes the
  tarball the registry names for `latest` and checks the SHA-512 in its `integrity`, so no Node.js and no npm: 9 MB to
  download, 27 MB on disk, against about 240 MB with Node.js for the wrapper. The Linux program is statically linked and ran
  on the NAS. It is run by its full path: a `tsc` on `PATH` is usually TypeScript 5 from npm, which has no `--lsp`.
- **Pull diagnostics.** TypeScript 7 publishes no diagnostics (measured: nothing in 4 s after `didOpen`); it declares
  `diagnosticProvider` and answers `textDocument/diagnostic`. So the client asks every server that declares it after
  each edit, and for all its open files when it says `interFileDependencies`; answers go where pushed ones go.
- **`.cmd` programs in a folder with a space**: npm from `C:\Program Files\nodejs` failed ("'C:\Program' is not
  recognized") because `cmd /s /c` drops the first and last quote of the line; the line is now quoted once more. It
  was met installing with a system npm, which pyright's install would have met too.
- **Not done**: updates on a schedule (installing again updates); macOS archives; the .NET SDK from the metadata on
  Windows was not tried (there an SDK on `PATH` was found and used).
- The list dialog now widens to its longest row (up to the screen's width) instead of cutting rows at 60%.
