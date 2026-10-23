# Formula pictures

Russian: [formula-pictures.ru.md](formula-pictures.ru.md).

The reading view and Live preview lay display formulas out in rows of text: fractions over a bar, roots, limits,
tall integrals. With the optional **mathimages** module and a terminal that shows Sixel graphics, display formulas
are shown typeset instead, as LaTeX prints them, in the Latin Modern font (Live preview, Windows Terminal 1.24):

![Formula pictures in Live preview](screenshots/formula-pictures.png)

## What it needs

- **A terminal that shows Sixel graphics** and reports its cell size: Windows Terminal 1.22 or later (checked:
  1.24 on Windows 11). Over SSH, too: the Linux build on a server, in Windows Terminal on the PC. Others that know
  Sixel (xterm with `-ti vt340`, WezTerm, foot, mlterm, Konsole) should work by the same rules, but were not tried.
  A terminal without Sixel gets the text layout, as before; if it shows boxes for some symbols, see
  [terminal-fonts.md](terminal-fonts.md).
- **The module**, about 14 MB unpacked (most of it Skia, the drawing library): `blueedit-mathimages-<version>-win-x64.zip`
  or `…-linux-x64.tar.gz` from the same release as the editor. Unpack it **next to the editor**, so that a folder
  `mathimages` lies beside `be.exe`:

  ```
  be.exe
  mathimages\BlueEdit.MathImages.dll, CSharpMath*.dll, SkiaSharp.dll, libSkiaSharp.dll
  ```

  On Linux: `tar xzf blueedit-mathimages-*-linux-x64.tar.gz -C blueedit-*-linux-x64` (`mathimages/` beside
  `blueedit`). The editor also looks in `%LOCALAPPDATA%\BlueEdit\mathimages` (`~/.local/share/BlueEdit/mathimages`),
  where `.\tasks.ps1 setup-math` builds it from the source, and where `BLUEEDIT_MATHIMAGES` points (`off` turns it
  off). The editor checks the module's interface number (`Api`), not its release: a module from another release
  works as long as that number is the same (it has been 1 since the module first shipped). A module with another
  number is refused with a message, and formulas stay text, rather than being called the wrong way.
- **The setting** `"formulaImages": true` (the default), or **View → Formula pictures**, which turns it on and off and
  says what is missing when nothing changes.

## How it behaves

- Pictures are drawn in the background (half a millisecond for a typical formula); a formula is shown as text until
  its picture is ready, then the lines are laid out again. The page does not jump: the line at the top stays there.
- In Live preview the formula the caret is in is shown as written, as before.
- The picture takes the formula colour of the theme over its background, so it follows the theme.
- **Text instead of a picture**: inline formulas (`$…$`); a formula wider than the window; one the module cannot
  read (0.6 % of the formulas of the notes it was measured on: `\substack`, `\overbrace`, `\not` and a few rarer).
- Pictures are as sharp as the terminal allows: Windows Terminal draws Sixel on a grid of 10 × 20 pixels per cell
  whatever the font, and scales it to the font, so a larger font makes them softer.

The design, the choices and the measurements: [tech/editing.md](tech/editing.md#formula-pictures-sixel-49).
