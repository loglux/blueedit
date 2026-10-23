"""Drives the real editor inside a pseudo-terminal and reads back what a terminal would show: be.exe in a
pseudo-console (ConPTY, winpty) on Windows, the Linux build in a pty (ptyprocess) on Linux."""
from __future__ import annotations

import os
import re
import sys
import threading
import time
from pathlib import Path

import pyte

WINDOWS = sys.platform == "win32"
if WINDOWS:
    from winpty import PtyProcess
else:
    from ptyprocess import PtyProcessUnicode as PtyProcess

ROOT = Path(__file__).resolve().parents[2]
EDITOR = os.environ.get("BLUEEDIT_EXE", str(ROOT / "publish" / ("be.exe" if WINDOWS else "be")))

# Keys are sent the way Windows Terminal sends them: as win32-input-mode key events
# (ESC [ Vk ; Sc ; Uc ; Kd ; Cs ; Rc _). A bare "ESC f" would be mis-translated by the test console.
SHIFT, ALT, CTRL, ENHANCED = 0x10, 0x02, 0x08, 0x100


# Scan codes of the main block of an ANSI keyboard (what a real terminal reports; the driver needs them to resolve digits).
_ROWS = {"1234567890": 0x02, "QWERTYUIOP": 0x10, "ASDFGHJKL": 0x1E, "ZXCVBNM": 0x2C}
SCAN = {ch: base + i for row, base in _ROWS.items() for i, ch in enumerate(row)}


def w32(vk: int, uc: int = 0, cs: int = 0) -> str:
    sc = SCAN.get(chr(vk), 0)
    return f"\x1b[{vk};{sc};{uc};1;{cs};1_\x1b[{vk};{sc};{uc};0;{cs};1_"


F1, F2, F3, F4, F5, F6, F7, F8, F9, F10 = (w32(0x6F + n) for n in range(1, 11))
UP, DOWN, LEFT, RIGHT = (w32(v, cs=ENHANCED) for v in (0x26, 0x28, 0x25, 0x27))
HOME, END, PGUP, PGDN = (w32(v, cs=ENHANCED) for v in (0x24, 0x23, 0x21, 0x22))
CTRL_RIGHT, CTRL_LEFT = w32(0x27, cs=CTRL | ENHANCED), w32(0x25, cs=CTRL | ENHANCED)
SHIFT_DOWN = w32(0x28, cs=SHIFT | ENHANCED)
SHIFT_CTRL_RIGHT = w32(0x27, cs=SHIFT | CTRL | ENHANCED)
ESC = w32(0x1B, uc=27)
SHIFT_F6 = w32(0x75, cs=SHIFT)
SHIFT_F10 = w32(0x79, cs=SHIFT)
CTRL_PGDN, CTRL_PGUP = w32(0x22, cs=CTRL | ENHANCED), w32(0x21, cs=CTRL | ENHANCED)


def ctrl(letter: str) -> str:
    return w32(ord(letter.upper()), uc=ord(letter.upper()) - 64, cs=CTRL)


def alt(letter: str) -> str:
    return w32(ord(letter.upper()), uc=ord(letter.lower()), cs=ALT)


def alt_ru(key: str, typed: str) -> str:
    """Alt + a key while the Russian layout is active: the key's virtual key code, but a Cyrillic character."""
    return w32(ord(key.upper()), uc=ord(typed), cs=ALT)


SHIFT_TAB = w32(0x09, uc=9, cs=SHIFT)
CTRL_SPACE = w32(0x20, uc=0x20, cs=CTRL)
SHIFT_F5 = w32(0x74, cs=SHIFT)
ALT_F7 = w32(0x76, cs=ALT)
DELETE = w32(0x2E, cs=ENHANCED)
BACKSPACE, CTRL_BACKSPACE = w32(0x08, uc=8), w32(0x08, uc=8, cs=CTRL)
SHIFT_F7, CTRL_F7 = w32(0x76, cs=SHIFT), w32(0x76, cs=CTRL)
F12, SHIFT_F12, ALT_F1 = w32(0x7B), w32(0x7B, cs=SHIFT), w32(0x70, cs=ALT)
SHIFT_F2 = w32(0x71, cs=SHIFT)
CTRL_SLASH = w32(0xBF, uc=0x1F, cs=CTRL)          # as Windows Terminal sends it: the control character 0x1F
ALT_UP, ALT_DOWN = w32(0x26, cs=ALT | ENHANCED), w32(0x28, cs=ALT | ENHANCED)
CTRL_F12, CTRL_F2, ALT_F12 = w32(0x7B, cs=CTRL), w32(0x71, cs=CTRL), w32(0x7B, cs=ALT)

if not WINDOWS:
    # A Linux terminal (xterm, as TERM=xterm-256color says) sends plain VT sequences: the same names, other bytes.
    F1, F2, F3, F4 = "\x1bOP", "\x1bOQ", "\x1bOR", "\x1bOS"
    F5, F6, F7, F8, F9, F10 = (f"\x1b[{n}~" for n in (15, 17, 18, 19, 20, 21))
    UP, DOWN, RIGHT, LEFT = "\x1b[A", "\x1b[B", "\x1b[C", "\x1b[D"
    HOME, END, PGUP, PGDN = "\x1b[H", "\x1b[F", "\x1b[5~", "\x1b[6~"
    CTRL_RIGHT, CTRL_LEFT = "\x1b[1;5C", "\x1b[1;5D"
    SHIFT_DOWN = "\x1b[1;2B"
    SHIFT_CTRL_RIGHT = "\x1b[1;6C"
    ESC = "\x1b"
    SHIFT_F6, SHIFT_F10, SHIFT_F5, ALT_F7 = "\x1b[17;2~", "\x1b[21;2~", "\x1b[15;2~", "\x1b[18;3~"
    CTRL_PGDN, CTRL_PGUP = "\x1b[6;5~", "\x1b[5;5~"
    SHIFT_TAB = "\x1b[Z"
    CTRL_SLASH = "\x1f"
    ALT_UP, ALT_DOWN = "\x1b[1;3A", "\x1b[1;3B"
    CTRL_F12, CTRL_F2, ALT_F12 = "\x1b[24;5~", "\x1b[1;5Q", "\x1b[24;3~"
    CTRL_SPACE = "\x00"
    DELETE = "\x1b[3~"
    BACKSPACE, CTRL_BACKSPACE = "\x7f", "\x08"       # most Linux terminals send BS for Ctrl+Backspace
    SHIFT_F7, CTRL_F7 = "\x1b[18;2~", "\x1b[18;5~"
    F12, SHIFT_F12, ALT_F1 = "\x1b[24~", "\x1b[24;2~", "\x1b[1;3P"
    SHIFT_F2 = "\x1b[1;2Q"

    def ctrl(letter: str) -> str:            # noqa: F811 - the Linux form of the same key
        return chr(ord(letter.lower()) & 0x1F)

    def alt(letter: str) -> str:             # noqa: F811
        return "\x1b" + letter.lower()

    def alt_ru(key: str, typed: str) -> str:  # noqa: F811 - the terminal sends the Cyrillic letter after ESC
        return "\x1b" + typed

def click(x: int, y: int) -> str:
    """Left click at 1-based screen column x, row y (SGR mouse encoding)."""
    return f"\x1b[<0;{x};{y}M\x1b[<0;{x};{y}m"


def wheel_down(x: int, y: int) -> str:
    return f"\x1b[<65;{x};{y}M"


class Term:
    def __init__(self, cmd: list[str], dims: tuple[int, int] = (30, 120), cwd: str | None = None, env: dict | None = None):
        self.rows_n, self.cols_n = dims
        self.screen = pyte.Screen(self.cols_n, self.rows_n)
        self.stream = pyte.Stream(self.screen)
        self.lock = threading.Lock()
        self.raw = ""
        self.chunks: list[tuple[float, str]] = []
        self._buf = ""
        self.reply_da = False           # answer device-attribute / cursor queries (needed by Far, not by the editor)
        full_env = dict(os.environ)
        if not WINDOWS:
            full_env.setdefault("TERM", "xterm-256color")      # a pty has no terminal type of its own
        full_env.setdefault("BLUEEDIT_UI_LANGUAGE", "en")     # the checks read English text, whatever the system
        if env:
            full_env.update(env)
        self.p = PtyProcess.spawn(cmd, dimensions=dims, cwd=cwd, env=full_env)
        threading.Thread(target=self._read, daemon=True).start()

    def _read(self):
        while True:
            try:
                d = self.p.read(65536)
            except Exception:
                break
            self.raw += d
            self.chunks.append((time.perf_counter(), d))
            self._buf += d
            for m in re.finditer(r"\x1b\]4;(\d+);\?\x1b\\", self._buf):
                g = (int(m.group(1)) * 7) % 256
                self.p.write(f"\x1b]4;{m.group(1)};rgb:{g:02x}/{g:02x}/{g:02x}\x1b\\")
            for m in re.finditer(r"\x1b\](1[01]);\?\x1b\\", self._buf):
                self.p.write(f"\x1b]{m.group(1)};rgb:{'c0/c0/c0' if m.group(1) == '10' else '00/00/00'}\x1b\\")
            if self.reply_da and re.search(r"\x1b\[0?c", self._buf):
                self.p.write("\x1b[?62;22c")
            self._buf = self._buf[-64:]
            with self.lock:
                self.stream.feed(d)

    # ---- reading the screen ----
    def rows(self) -> list[str]:
        with self.lock:
            return [line.rstrip() for line in self.screen.display]

    def text(self) -> str:
        return "\n".join(self.rows())

    def cell(self, y: int, x: int):
        with self.lock:
            return self.screen.buffer[y][x]

    def status(self) -> str:
        found = [r for r in self.rows() if " Ln " in r and "Col" in r]
        return found[-1] if found else ""

    def pos(self) -> tuple[int, int]:
        m = re.search(r"Ln (\d+)/\d+\s+Col (\d+)", self.status())
        return (int(m.group(1)), int(m.group(2))) if m else (-1, -1)

    def body(self, n: int = 16) -> list[str]:
        """Text rows under the menu bar and the tab strip."""
        return self.rows()[2:2 + n]

    def tabs(self) -> str:
        return self.rows()[1].strip()

    def has_dialog(self, title: str | None = None) -> bool:
        return any("┏┥" + (title or "") in r for r in self.rows())

    # ---- driving ----
    @staticmethod
    def _encode(keys: str) -> str:
        """Plain text becomes key events (capitals carry Shift); escape sequences pass through untouched. On Linux a
        terminal sends text as it is."""
        if "\x1b" in keys or not WINDOWS:
            return keys
        out = []
        for ch in keys:
            if "A" <= ch <= "Z":
                out.append(w32(ord(ch), uc=ord(ch), cs=SHIFT))
            elif "a" <= ch <= "z":
                out.append(w32(ord(ch.upper()), uc=ord(ch)))
            elif "0" <= ch <= "9":
                out.append(w32(ord(ch), uc=ord(ch)))
            else:
                out.append(ch)
        return "".join(out)
    def send(self, keys: str, wait: float | None = None):
        """Types the keys. With a `wait` given, sleeps that long (a test waiting for a timer or a checker says so).
        Without one, returns as soon as the editor has answered: its output went quiet for QUIET seconds after it
        started, or NOTHING seconds passed without any output; never longer than 0.5 s, the old fixed wait."""
        n = len(self.chunks)
        self.p.write(self._encode(keys))
        if wait is not None:
            time.sleep(wait)
            return
        self.settle(n)

    # BLUEEDIT_UI_PATIENCE stretches the waits on a loaded machine: the CI runners share theirs (the Linux one with a
    # server's services), and there a key now and then took longer than NOTHING to show at all (PR #99, #103)
    PATIENCE = float(os.environ.get("BLUEEDIT_UI_PATIENCE", "1"))
    QUIET, NOTHING, LONGEST = 0.08 * PATIENCE, 0.25 * PATIENCE, 0.5 * PATIENCE

    def settle(self, since: int | None = None):
        """Waits until the editor's output has gone quiet (see send)."""
        n = len(self.chunks) if since is None else since
        t0 = last = time.perf_counter()
        seen = False
        while True:
            time.sleep(0.01)
            now = time.perf_counter()
            if now - t0 >= self.LONGEST:
                return
            c = len(self.chunks)
            if c != n:
                n, last, seen = c, now, True
            elif (seen and now - last >= self.QUIET) or (not seen and now - t0 >= self.NOTHING):
                return

    def wait_for(self, needle: str, timeout: float = 30) -> bool:
        t0 = time.time()
        while time.time() - t0 < timeout:
            if needle in self.text():
                return True
            time.sleep(0.1)
        return False

    def resize(self, rows: int, cols: int, wait: float = 2.0):
        """Resize the console and rebuild the emulated screen from what the editor redraws afterwards."""
        c0 = len(self.chunks)
        self.p.setwinsize(rows, cols)
        time.sleep(wait)
        with self.lock:
            self.rows_n, self.cols_n = rows, cols
            self.screen = pyte.Screen(cols, rows)
            self.stream = pyte.Stream(self.screen)
            for _, d in self.chunks[c0:]:
                self.stream.feed(d)

    def alive(self) -> bool:
        return self.p.isalive()

    def wait_exit(self, timeout: float = 5) -> bool:
        t0 = time.time()
        while time.time() - t0 < timeout:
            if not self.p.isalive():
                return True
            time.sleep(0.1)
        return False

    def kill(self):
        try:
            self.p.terminate(force=True)
        except Exception:
            pass


def start_editor(*args: str, dims: tuple[int, int] = (30, 120), env: dict | None = None, cwd: str | None = None, ready: str = "Ln ") -> Term:
    t = Term([EDITOR, *args], dims=dims, cwd=cwd, env=env)
    assert t.wait_for(ready, 40), "editor did not draw its status line:\n" + t.text()
    t.settle()                      # the rest of the first frame (was a fixed 0.5 s)
    return t
