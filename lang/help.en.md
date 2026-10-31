# Basics

F9                 | The menu; Alt+F/E/S/V/O/T/B/W/P opens one, or click the top bar
F1                 | This help: Up/Down pick a section, PgUp/PgDn scroll it, Esc closes
Esc                | Cancels; opened from Far it also closes the file, as Far's editor does (Esc in the settings goes back to the text; setting "escapeQuits": "auto", true or false)
Shift+F10          | Save all and quit (for git commit and other programs that wait)
Ctrl+Q, F10        | Quit (asks to save changes)

# Files

F2, Ctrl+S         | Save
Shift+F2           | Save as...
F3                 | Open (file picker)
Ctrl+N             | New document
Ctrl+W             | Close document
File > Info        | Lines, paragraphs, words, characters (file and selection), encoding

In Open and Save as: F7 makes a new folder, Alt+F1 lists the drives, Backspace goes up a folder. Save as: Tab moves between the list and the name; it asks before replacing a file.

A file changed on disk is reloaded by itself (Ctrl+Z gets the old text back); with unsaved edits you are asked: Reload or Keep mine.

# Encodings

A file is saved in the encoding it was read in (shown in the status line). Opening: UTF-8 when the bytes are valid UTF-8, otherwise Windows-1251 or DOS 866.

File > Reopen with encoding | Garbled text? Read the same file again as another one
File > Convert encoding     | Save in another encoding; warns about lost characters
File > Line endings         | Switch between CRLF (Windows) and LF (Unix)

# Documents

F6                 | Next document
Shift+F6           | Previous document
Ctrl+PgDn          | Next document
Ctrl+PgUp          | Previous document
Alt+1..Alt+9       | Document by number
Window menu        | The open files, • on the current one: Alt+W, then the digit
Mouse              | Click a tab

# Moving and selecting

Arrows             | Move
Ctrl+Left/Right    | Word by word
Home / End         | Start / end of line
Ctrl+Home/End      | Start / end of file
PgUp / PgDn        | Page up / down
Shift+move         | Select
Ctrl+A             | Select all
Alt+= / Alt+-      | Select by meaning, wider and back: word, inside quotes or brackets and with them, sentence, line, paragraph, section, file; in code also the block
F4                 | Column block mode on/off
Mouse              | Click, drag to select, double click selects a word, the wheel scrolls

# Editing

Ctrl+Z             | Undo
Ctrl+Y             | Redo
Ctrl+X/C/V         | Cut / copy / paste (system clipboard; pasted text is one undo step)
Shift+Del, Ctrl+Ins, Shift+Ins | The same, as in Far and DOS
Ctrl+L             | Delete line
Ctrl+D             | Duplicate line
Ctrl+/             | Comment lines out / in
Alt+Up/Down        | Move lines up / down
Ctrl+Bksp          | Delete the word before
Ctrl+Del           | Delete the word after
Tab                | On the lines of a selection: indent them
Shift+Tab          | Unindent
Ctrl+Space         | Complete the word: words of the open files and language keywords (kw). In code it opens by itself after 3 letters: Tab takes the word, Enter only after choosing with the arrows (setting completeWhileTyping)
( [ { " '          | In code the closer is added, typing it steps over it, Backspace removes an empty pair, over a selection it wraps (setting autoClose)
Enter              | Keeps the indent; one step deeper after ":" in Python or "{" in C#, JavaScript and the like; between { and } the } goes to its own line; a } typed on an empty line goes back to its {'s indent

A mouse selection is not copied by itself: Ctrl+C (Shift+drag: the terminal selects). In code the bracket at the caret and its pair are underlined.

# Search

Ctrl+F             | Find
F5 / Shift+F5      | Next / previous
Ctrl+R             | Replace: at each match Y yes, N no, A all the rest, Esc stop
Alt+F7             | Find in folder (a list of matches; Enter opens the file at the line)
Ctrl+G             | Go to line
Alt+Left / Alt+Right | Back to where a jump left from, and forward again: Ctrl+G, search, the outline, problems, F12; across files
Ctrl+]             | To the pair of the bracket at the caret, and back: ( [ { in code, in formulas and in text
Shift+F12 / Ctrl+Shift+F12 | The same as Alt+Left / Alt+Right, for Windows Terminal with panes: there Alt+arrows may move to the next pane

In the dialogs: Alt+C match case, Alt+W whole word, Alt+R regular expression. A regex replace inserts the groups with $1, $2. Hotkeys and Y/N/A also work with the Russian keyboard layout.

# Markdown

Ctrl+B             | Bold
Alt+I              | Italic
Alt+C              | Inline code
Alt+D              | Strikethrough
Alt+M              | Inline math $...$
Ctrl+K             | Link
Alt+H              | Heading level (cycles)
Alt+L              | Bullet list
Alt+N              | Numbered list
Alt+Q              | Quote
Alt+X              | Task checkbox, toggles [ ] and [x]
Alt+R              | Reformat the paragraph (or each selected one) to the right margin
Format menu        | Paragraph: align left, right, centre, justify — once, on the paragraph at the caret or the selected ones (the last three in plain text); Auto-wrap: a word typed past the margin goes to a new line

The margin is "rightMargin" in the settings (78). Alt+Z only shows long lines wrapped.

# View and formulas

Alt+Z              | Soft wrap on/off
Ctrl+O             | Outline / symbols
Ctrl+E             | Reading mode on/off
Alt+V S/L/R        | Markdown view: Source / Live / Reading (themes are in the View menu too)
Alt+G              | Line numbers on/off for every file, remembered (setting lineNumbers)

Live preview hides markup except on the caret line; $math$ is shown as Unicode, over rows when it needs them (a fraction, x^{5/2}). $$display$$ and \[ \] formulas are laid out: fractions, roots, limits, grids; in Live preview the formula the caret is in is shown as written.

View > Images: a Markdown line that is only an image (![alt](path), ![[name.png]]) shows the picture, PNG or JPEG, with the same module and terminal as formula pictures.
View > Formula pictures: display formulas typeset, in terminals that show Sixel (Windows Terminal 1.22+), with the mathimages module (docs/formula-pictures.md). Boxes instead of symbols: the terminal's font lacks them; Tools > Settings > Formula symbols: basic avoids the rarest (docs/terminal-fonts.md).

A formula that would not typeset (an unclosed brace, \left without \right) is an error: TeX 1E in the status line, Shift+F7 goes to it (setting latexCheck). cos without \, \cos{(x)}, 2*3, <=, ..., 5 cm in italics: warnings (TeX 1W) that F7 fixes or never shows again (setting latexStyle).

Reading mode scrolls: arrows, PgUp/PgDn, Space/Shift+Space, Home/End, the wheel. Tables are aligned; ```cs, ```python ... blocks are coloured as that language; wrapped list items and quotes continue under their text.

# Code

Language servers (pyright, csharp-ls...) underline errors and warnings (LSP 2E 1W in the status line). After a dot the server's members are listed (m method, fld field, cls class...).

Shift+F7           | The next error or warning
F7                 | The server's fixes, or the message
F12                | Go to the definition (another file opens)
Shift+F12          | Back
Alt+F1             | What the server knows about the symbol: type, documentation
Ctrl+F12           | Format the file: by the server, or ruff/black for Python (docs/lsp.md)
Ctrl+F2            | Rename the symbol in every file (others open unsaved: File > Save all)
Alt+F12            | Where the symbol is used: a list, Enter goes there
( ,                | The function's parameters in the status line, the current one in [ ]
Tools > Code tools | Installs the servers and ruff into the editor's folder

# Spelling

F7                 | Suggestions for the word at the caret: replace, add to the dictionary, ignore
F8                 | Spell checking on/off
Shift+F7           | Next spelling or grammar problem (selected: F7 fixes it)
Tools > Spelling languages | Auto (the system's languages) or your own pick; a word in letters none of them uses is not checked
Tools > Spelling provider | Windows (instant), Microsoft Office or Hunspell
Tools > Get spelling dictionaries | Hunspell's, from LibreOffice (Linux, macOS), no root; those the system has are not downloaded again

Not checked: web addresses, e-mail, paths. Words mixing Latin and Cyrillic look-alikes ("пpивет") are always marked; F7 offers the word in one alphabet. Add to dictionary keeps the word in dictionary.txt next to settings.json.

# Grammar and style

F7                 | On an underlined finding: the fixes as buttons, why, and who found it
Ctrl+F7            | The language model suggests a better wording of the paragraph: tick the changes to take (Ctrl+Z undoes them)
Tools > Set up grammar checking | A model here, Ollama, LM Studio, a cloud service (Claude, OpenAI, Gemini, DeepSeek), a server at an address, LanguageTool or Harper; then turned on
Tools > Grammar checking | LanguageTool, a model, Edge, Chrome, Harper: findings underlined in another colour
Tools > Grammar model | The model: here (Auto: the best one) or a server's
Tools > Ignored rules | What F7 > Never show this hid; untick to see it again
Tools > Style options | What style suggestions look for: wordiness, repetition...

A paragraph is checked in its language among the spelling ones: by its letters, else by a built-in model (English or German...). In Markdown "language: de" on top says it. Formulas ($…$, $$…$$) are read with their sentence but never underlined or changed.

# Settings

Tools > Settings   | The usual settings as a list: Space or Left/Right changes, Enter saves and applies. Its last row opens settings.json (all of them, with comments), where F2 applies the changes
View > Theme       | DOS blue, dark or light; single colours are set in the settings
View > Clock       | The time at the right of the menu bar; again: the date and the time; again: off (remembered, setting clock)
showTiming         | For developers, in settings.json: the status line shows how long the last key took to reach the screen
Interface language | Setting uiLanguage: auto, en or ru (menus, messages). Alt+Ф and Alt+A open the same menu, whatever the keyboard layout
Tools > Terminal font | Own Windows Terminal profile with the font, or a how-to
Tools > Command be | Puts the editor's folder in your PATH (or takes it out): "be" then starts it in any new console

# Other programs

be file:LINE[:COL] | Opens the file at the line (also be +LINE file; -r read-only)
EDITOR, VISUAL     | Set them to be.exe to use it from git and other programs
Far Manager        | Commands > File associations > Edit command (F4): "path\be.exe" "!.!"
