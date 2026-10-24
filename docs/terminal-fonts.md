# Terminals and fonts: formulas without boxes

*Русская версия: [terminal-fonts.ru.md](terminal-fonts.ru.md)*

A console program cannot choose its font: the terminal draws every character with the font set in the terminal.
Formulas shown as text use characters that many monospaced fonts do not have: letter subscripts (`cₙ`), `⩽`, `∈`,
tall brackets and integrals (`⎛ ⌠`). What happens to a character the font lacks depends on the terminal:

- **Windows Terminal** takes it from another font (Cambria Math, Segoe UI Symbol), so every formula shows. It also
  shows formulas as typeset pictures (Sixel, [formula-pictures.md](formula-pictures.md)). This is the terminal to use
  for formulas, locally and over SSH.
- **MobaXterm, ConEmu, the classic Windows console** draw it with the terminal's own font only, or with one alternative
  font, and show a box `□` where the font has nothing.

## What the common fonts lack

Measured with fontTools on the fonts of Windows 11 (11 October 2026):

| Font | Missing among the characters formulas use |
|---|---|
| Consolas | letter subscripts `ₕ ₖ ₗ ₘ ₙ ₚ ₛ ₜ`; `⩽ ⩾ ∼ ≃ ≅ ∝ ≪ ≫ ∈ ∉ ⊂ ⊆ ⊃ ⊇ ∪ ∧ ∨ ⊕ ⊗ ∓ ⇒ ⇐ ⇔ ⟹ ∇ ∮ ∛ ∜ ∠`; tall brackets `⎛ ⎜ ⎝ ⎞ ⎟ ⎠ ⎡ ⎣ ⎤ ⎦` and `⎮` |
| Cascadia Mono | the same, and all letter subscripts and superscripts except `ⁿ`, and `⁺ ⁻ ⁼ ⁽ ⁾` |
| Courier New | subscript digits `₀ … ₉`, most superscript digits, and the same symbols |
| Lucida Console | all subscripts and most superscripts, and the same symbols |
| Cambria, Cambria Math | nothing: every character the editor uses (but see ConEmu below) |

`≤ ≥ ≠ ≈ ∞ ∑ ∫ √ ± × · → α β π` are in all of them.

## Two ways out

1. **Give the terminal a font that has the characters**, or let it take them from one (below, per terminal).
2. **Ask the editor for basic symbols**: Tools → Settings → *Formula symbols*: *basic*, or `"formulaSymbols": "basic"` in
   `settings.json`. Letter subscripts that few fonts have are then written out (`c_n`, or a row below in a formula laid
   out over rows), and `⩽ ⩾` are shown as `≤ ≥` (the same relations in another cut). Other symbols, such as `∈` or the
   tall brackets, have no plain equivalent and stay. Settings are per computer: on a server you reach over SSH, set it
   in that server's `settings.json` (`~/.config/blueedit/settings.json`).

The default is *full*: where the terminal shows them, the real characters look better than stand-ins.

## Per terminal

### Windows Terminal

Nothing to do. If you start Far Manager from the Start menu, check which window it opens in: Windows 11's *default
terminal application* (Settings → System → For developers → Terminal) decides whether `cmd`, PowerShell and Far open
in Windows Terminal (a window with tabs) or in the classic console. On a test machine it was *Let Windows decide*
(the registry, 11 October 2026); which of the two that gives was not checked, so choose *Windows Terminal* there to
be sure.

### The classic Windows console (conhost)

One font, no characters from others for these symbols, and no Sixel pictures. Use Windows Terminal, or basic symbols.
Not checked: which of these characters conhost draws with font linking.

### ConEmu

ConEmu draws a range of characters with its *alternative font* (Settings → General → Fonts). Its default range,
`2013-25C4`, sends pseudographics **and the mathematical symbols, arrows and subscripts** to that font, Courier New,
which lacks most of them: hence the boxes.

Tried on 11 October 2026, with no good result yet:

- **Cambria Math** as the alternative font, the range
  `2070-209F;2100-23FF;27C0-27FF;2A00-2AFF;1D2C-1DBF;2C7C-2C7C;`. It leaves out the box-drawing characters
  (`2500-257F`), so Far's frames stay in the main font. Every symbol in that range **disappeared** (`∫ √ ≈ ≠ −`). The
  font has them all, but its line height, by the metrics Windows uses to size a font, is 5.6 times its size (Cascadia
  Mono: 1.3): squeezed into a cell, the glyphs are lost.
- **Cambria**, the ordinary font in the same file, has every character too, and a line height of 1.17. The user who
  tried it found the result poor too.

So for formulas in ConEmu, the working choices so far are **basic symbols** (above) or **Windows Terminal**. ConEmu
shows no Sixel pictures either.

### MobaXterm

MobaXterm draws with the font chosen in Settings → Configuration → Terminal (*Terminal font*), and showed boxes for
`ₙ` and `⩽` with its font (a user's screenshot, 11 October 2026). Choose a font that has these characters if one is
installed (DejaVu Sans Mono is often recommended for mathematics: it was not among the fonts measured), or use basic
symbols on that server. Connecting with `ssh` from Windows Terminal instead shows everything (seen the same day), and
the formula pictures too.
