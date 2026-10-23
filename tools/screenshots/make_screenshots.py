"""
Regenerates the README screenshots: runs the real editor in a pseudo-console, plays a few scenarios and
renders the emulated screen (characters, colours, underline, cursor) to PNG files in docs/screenshots/.

    python tools/screenshots/make_screenshots.py

Needs the published exe (dotnet publish ... -o publish) and: pip install pywinpty pyte pillow fonttools; the language
server pictures need pyright and csharp-ls (tasks setup-lsp). docs/screenshots/formula-pictures.png is not made here:
Sixel pictures need a real terminal, so it is a capture of a Windows Terminal window showing samples/math.md.
"""
import os
import re
import shutil
import sys
import tempfile
import time
from pathlib import Path

from fontTools.ttLib import TTCollection, TTFont
from PIL import Image, ImageDraw, ImageFont

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "tests" / "ui"))
import harness  # noqa: E402
from harness import CTRL_RIGHT, CTRL_SPACE, DOWN, END, HOME, SHIFT_F7, Term, alt, ctrl, F1, F7  # noqa: E402

OUT = ROOT / "docs" / "screenshots"
SAMPLES = Path(__file__).resolve().parent / "samples"
ROWS, COLS = 30, 100

# ---------------------------------------------------------------- rendering
FONT_DIR = Path(os.environ.get("WINDIR", r"C:\Windows")) / "Fonts"
PRIMARY = FONT_DIR / "CascadiaMono.ttf"
FALLBACKS = [FONT_DIR / "seguisym.ttf", FONT_DIR / "cambria.ttc"]
SIZE = 17


def cmap_of(path: Path) -> set[int]:
    fonts = TTCollection(str(path)).fonts if path.suffix == ".ttc" else [TTFont(str(path))]
    cps = set()
    for f in fonts:
        cps |= set(f.getBestCmap() or {})
    return cps


FONTS = [(ImageFont.truetype(str(p), SIZE if i == 0 else SIZE - 2), cmap_of(p)) for i, p in enumerate([PRIMARY, *FALLBACKS]) if p.exists()]
MAIN_FONT = FONTS[0][0]
ascent, descent = MAIN_FONT.getmetrics()
CW = round(MAIN_FONT.getlength("M"))
CH = ascent + descent + 2

NAMED = {  # pyte names for the 16 basic colours
    "black": "000000", "red": "aa0000", "green": "00aa00", "brown": "aa5500", "blue": "0000aa", "magenta": "aa00aa",
    "cyan": "00aaaa", "white": "aaaaaa", "brightblack": "555555", "brightred": "ff5555", "brightgreen": "55ff55",
    "brightbrown": "ffff55", "brightblue": "5555ff", "brightmagenta": "ff55ff", "brightcyan": "55ffff", "brightwhite": "ffffff",
}


def rgb(value: str, default: str) -> tuple[int, int, int]:
    v = default if value == "default" else NAMED.get(value, value)
    v = v if len(v) == 6 else default
    return int(v[0:2], 16), int(v[2:4], 16), int(v[4:6], 16)


# Box drawing is painted as lines so frames join seamlessly whatever the font: (up, down, left, right), 1 light, 2 heavy.
BOX = {
    "─": (0, 0, 1, 1), "│": (1, 1, 0, 0), "━": (0, 0, 2, 2), "┃": (2, 2, 0, 0),
    "┌": (0, 1, 0, 1), "┐": (0, 1, 1, 0), "└": (1, 0, 0, 1), "┘": (1, 0, 1, 0),
    "┏": (0, 2, 0, 2), "┓": (0, 2, 2, 0), "┗": (2, 0, 0, 2), "┛": (2, 0, 2, 0),
    "├": (1, 1, 0, 1), "┤": (1, 1, 1, 0), "┬": (0, 1, 1, 1), "┴": (1, 0, 1, 1), "┼": (1, 1, 1, 1),
    "┥": (1, 1, 2, 0), "┝": (1, 1, 0, 2), "═": (0, 0, 2, 2), "║": (2, 2, 0, 0),
    "╔": (0, 2, 0, 2), "╗": (0, 2, 2, 0), "╚": (2, 0, 0, 2), "╝": (2, 0, 2, 0),
}


def paint_box(d, ch, px, py, fg):
    up, down, left, right = BOX[ch]
    cx, cy = px + CW // 2, py + CH // 2
    for weight, (x0, y0, x1, y1) in ((up, (cx, py, cx, cy)), (down, (cx, cy, cx, py + CH)),
                                    (left, (px, cy, cx, cy)), (right, (cx, cy, px + CW, cy))):
        if weight:
            d.line([x0, y0, x1, y1], fill=fg, width=1 if weight == 1 else 3)


def font_for(ch: str):
    cp = ord(ch)
    for font, cps in FONTS:
        if cp in cps:
            return font
    return MAIN_FONT


def render(term: Term, path: Path, title: str):
    pad, bar = 14, 34
    w, h = COLS * CW + 2 * pad, ROWS * CH + 2 * pad + bar
    img = Image.new("RGB", (w, h), (30, 30, 30))
    d = ImageDraw.Draw(img)
    # a plain window frame: dark title bar with the window title
    d.rectangle([0, 0, w, bar], fill=(45, 45, 48))
    for i, c in enumerate([(255, 95, 86), (255, 189, 46), (39, 201, 63)]):
        d.ellipse([pad + i * 20, 11, pad + i * 20 + 12, 23], fill=c)
    d.text((pad + 70, 8), title, font=ImageFont.truetype(str(PRIMARY), 15), fill=(200, 200, 200))

    with term.lock:
        buf = term.screen.buffer
        cur = term.screen.cursor
        for y in range(ROWS):
            for x in range(COLS):
                cell = buf[y][x]
                fg, bg = rgb(cell.fg, "c0c0c0"), rgb(cell.bg, "000000")
                if cell.reverse:
                    fg, bg = bg, fg
                if not cur.hidden and (x, y) == (cur.x, cur.y):
                    fg, bg = bg, (230, 230, 230)
                px, py = pad + x * CW, pad + bar + y * CH
                d.rectangle([px, py, px + CW - 1, py + CH - 1], fill=bg)
                ch = cell.data or " "
                if ch in BOX:
                    paint_box(d, ch, px, py, fg)
                elif ch != " ":
                    d.text((px, py + 1 + ascent), ch, font=font_for(ch), fill=fg, anchor="ls")
                if cell.underscore:
                    d.line([px, py + ascent + 3, px + CW - 1, py + ascent + 3], fill=fg, width=1)
    img.save(path)
    print("wrote", path.relative_to(ROOT), f"{w}x{h}")


# ---------------------------------------------------------------- scenarios
def start(*files: str) -> Term:
    t = Term([harness.EDITOR, *files], dims=(ROWS, COLS), cwd=str(WORK))
    assert t.wait_for("Ln ", 40), t.text()
    time.sleep(1.0)
    return t


def wait_status(t: Term, test, seconds: float = 60):
    """Waits for a language server: until the status line passes the test."""
    deadline = time.time() + seconds
    while time.time() < deadline and not test(t.status()):
        time.sleep(0.5)
    assert test(t.status()), t.status()


def shot(t: Term, name: str, title: str, settle: float = 0.8):
    time.sleep(settle)
    render(t, OUT / name, title)
    t.kill()


if __name__ == "__main__":
    if not Path(harness.EDITOR).exists():
        sys.exit(f"{harness.EDITOR} not found: publish the editor first")
    OUT.mkdir(parents=True, exist_ok=True)
    WORK = Path(tempfile.mkdtemp(prefix="blueedit-shots-"))
    os.environ["BLUEEDIT_CONFIG_DIR"] = str(WORK / "config")      # default settings, whatever the user has
    os.environ["BLUEEDIT_WT_FRAGMENTS"] = str(WORK / "wt")        # never touch the real Windows Terminal profile
    os.environ.pop("WT_SESSION", None)
    os.environ["BLUEEDIT_WELCOME"] = "0"                          # no welcome over the pictures (a first start here)
    for f in SAMPLES.iterdir():
        shutil.copy(f, WORK / f.name)
    notes, code = str(WORK / "notes.md"), str(WORK / "Parser.cs")

    # 1. Markdown live preview: markup hidden except on the caret line (a list item with the typo)
    t = start(notes)
    t.send(DOWN * 10 + END, 0.6)
    shot(t, "markdown-live-preview.png", "notes.md — live preview, spell check, math")

    # 2. Reading mode: everything rendered, read-only
    t = start(notes)
    t.send(ctrl("e"), 0.8)
    t.send(DOWN * 3, 0.3)
    shot(t, "markdown-reading.png", "notes.md — reading mode (Ctrl+E)")

    # 3. C# with syntax highlighting and the Edit menu open; two documents in tabs. Demo.csproj next to it gives
    # csharp-ls a project (without one it stops)
    t = start(notes, code)
    wait_status(t, lambda s: "LSP csharp-ls" in s and "starting" not in s, 90)
    t.send(DOWN * 13 + END, 0.5)
    t.send(alt("e"), 0.8)
    shot(t, "code-and-menu.png", "Parser.cs — syntax highlighting, tabs, menu")

    # 4. Spelling suggestions for a Russian word
    t = start(notes)
    t.send(ctrl("f"), 0.6); t.send("превет"); t.send("\r", 0.8)      # Find leaves the caret on the word
    t.send(F7, 1.0)
    shot(t, "spell-suggestions.png", "F7 — spelling suggestions (Windows dictionaries)")

    # 5. Outline of the document
    t = start(notes)
    t.send(ctrl("o"), 1.0)
    shot(t, "outline.png", "Ctrl+O — outline")

    # 6a. Tables, code blocks by language, hanging indent (reading mode)
    t = start(str(WORK / "features.md"))
    t.send(ctrl("e"), 1.0)
    shot(t, "markdown-tables-code.png", "features.md — tables, code blocks, wrapped lists")

    # 6. Word completion in code: the text's own words first, C# keywords marked "kw"
    t = start(code)
    t.send(DOWN * 27 + END, 0.5)
    t.send("\r", 0.3); t.send("re", 0.3)
    t.send(CTRL_SPACE, 1.0)
    shot(t, "completion.png", "Ctrl+Space — word completion")

    # 8. A language server's error: pyright underlines it, Shift+F7 goes to it and says what it is
    t = start(str(WORK / "stats.py"))
    wait_status(t, lambda s: "1E" in s)
    t.send(SHIFT_F7, 1.0)
    shot(t, "lsp-error.png", "stats.py — an error from pyright (Shift+F7)")

    # 9. A language server's completion: after a dot, the members of the module
    t = start(str(WORK / "stats.py"))
    wait_status(t, lambda s: "LSP pyright-langserver" in s and "starting" not in s)
    t.send(DOWN * 13 + END, 0.3)
    t.send("\r", 0.3); t.send('"mode": statistics.', 0.3)
    deadline = time.time() + 30
    while time.time() < deadline and not any(re.search(r"\s(cls|fn|var|mod)$", r) for r in t.rows()):
        time.sleep(0.5)
    t.send("m", 1.0)
    shot(t, "lsp-completion.png", "stats.py — completion from pyright")

    # 10. Display formulas laid out as text over several rows (reading mode); docs/formula-pictures.md has the same
    # file as typeset pictures, which need a real terminal with Sixel
    t = start(str(WORK / "math.md"))
    t.send(ctrl("e"), 1.0)
    t.send(DOWN * 4, 0.3)
    shot(t, "formula-layout.png", "math.md — display formulas as text")

    # 7. Help
    t = start(notes)
    t.send(F1, 1.0)
    shot(t, "help.png", "F1 — keys and commands")

    shutil.rmtree(WORK, ignore_errors=True)
