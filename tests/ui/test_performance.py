"""Latency guard rails. Limits are generous (about 3x what is measured) so the tests catch regressions, not noise."""
import random
import re
import statistics
import time

from harness import DOWN, UP

NOISE = re.compile(r"\x1b\[\?25[hl]|\x1b\[\d+;\d+H")
WORDS = "the quick brown fox jumps over lazy dog markdown editor".split()


def _frame_bytes(chunks, since):
    out = []
    for ts, d in chunks:
        if ts >= since and NOISE.sub("", d):
            out.append((ts, len(d)))
    return out


def _measure(t, keys, n=25, gap=0.2):
    first, sizes = [], []
    for i in range(n):
        t0 = time.perf_counter()
        c0 = len(t.chunks)
        t.p.write(keys(i))
        time.sleep(gap)
        fr = _frame_bytes(t.chunks[c0:], t0)
        if fr:
            first.append(fr[0][0] - t0)
            sizes.append(sum(b for _, b in fr))
        else:
            sizes.append(0)
    return first, sizes


def _big_file(make_file, name, kind, n=2000):
    random.seed(1)
    if kind == "md":
        lines = ["# Title", ""]
        for i in range(n):
            k = i % 5
            if k == 0: lines.append(f"## Section {i}")
            elif k == 1: lines.append("- item with **bold** and *italic* and `code` text " + " ".join(random.choices(WORDS, k=6)))
            elif k == 2: lines.append(" ".join(random.choices(WORDS, k=45)))
            elif k == 3: lines.append("Inline math $x^2 + \\alpha$ and more " + " ".join(random.choices(WORDS, k=10)))
            else: lines.append("")
    else:
        lines = ["class A {"] + [f"    int M{i}(string s) {{ return s.Length + {i}; }} // c {' '.join(random.choices(WORDS, k=4))}" for i in range(n)] + ["}"]
    return make_file(name, "\n".join(lines) + "\n")


def _hud(t):
    m = re.search(r"key>frame ([\d.]+) ms \(handler ([\d.]+)\), draw ([\d.]+) ms", t.status())
    return tuple(float(x) for x in m.groups()) if m else None


def test_typing_in_a_large_markdown_file_is_fast_inside_the_editor(editor, make_file):
    """Measured by the editor itself (View > Show timing). Terminal.Gui adds up to ~20 ms before a key even arrives; that is not counted."""
    t = editor(_big_file(make_file, "big.md", "md"), dims=(30, 240), env={"BLUEEDIT_TIMING": "1"})
    t.send(DOWN * 3, 0.5)
    key_to_frame, handler = [], []
    for i in range(25):
        t.send("abcdefghij"[i % 10], 0.25)
        h = _hud(t)
        if h:
            key_to_frame.append(h[0]); handler.append(h[1])
    assert len(key_to_frame) >= 20
    assert statistics.median(key_to_frame) < 30, f"median key->frame {statistics.median(key_to_frame):.1f} ms"
    assert statistics.median(handler) < 8, f"median key handler {statistics.median(handler):.1f} ms"


def test_arrow_keys_in_code_do_not_repaint_the_screen(editor, make_file):
    t = editor(_big_file(make_file, "big.cs", "cs"))
    t.send(DOWN * 3, 0.5)
    _, sizes = _measure(t, lambda i: DOWN if i % 2 == 0 else UP)
    assert statistics.median(sizes) < 3000, f"median frame {statistics.median(sizes):.0f} bytes (a full repaint is ~12 KB)"


def _hold_down(t, n=60, rate=30):
    """A held arrow key: n presses at the keyboard auto-repeat rate. Returns (seconds after the last press until the
    caret stops moving, bytes written to the terminal). Wrapped lines take several presses, so the caret's final
    line is not known in advance: it has arrived when it has not moved for half a second."""
    c0 = len(t.chunks)
    for _ in range(n):
        t.p.write(DOWN)
        time.sleep(1 / rate)
    t_last = time.perf_counter()
    pos, changed = t.pos(), t_last
    while time.perf_counter() - changed < 0.5 and time.perf_counter() - t_last < 10:
        time.sleep(0.002)
        now = t.pos()
        if now != pos:
            pos, changed = now, time.perf_counter()
    lag = max(0.0, changed - t_last)
    return lag, sum(len(d) for _, d in t.chunks[c0:])


def test_held_arrow_key_scrolls_without_falling_behind_in_markdown(editor, make_file):
    t = editor(_big_file(make_file, "big.md", "md", n=4000), dims=(40, 160))
    t.send(DOWN * 50, 1.5)                                      # start where every press scrolls
    lag, sent = _hold_down(t)
    print(f"markdown: caret arrives {lag * 1000:.0f} ms after the last press, {sent / 60:.0f} bytes per press")
    assert lag < 0.15, f"caret arrives {lag * 1000:.0f} ms after the key is released"


def test_held_arrow_key_scrolls_without_falling_behind_in_code(editor, make_file):
    t = editor(_big_file(make_file, "big.cs", "cs", n=4000), dims=(40, 160))
    t.send(DOWN * 50, 1.5)
    lag, sent = _hold_down(t)
    print(f"code: caret arrives {lag * 1000:.0f} ms after the last press, {sent / 60:.0f} bytes per press")
    assert lag < 0.15, f"caret arrives {lag * 1000:.0f} ms after the key is released"


def test_fast_scroll_moves_rows_in_the_terminal(editor, make_file):
    """"fastScroll": true — the terminal moves the rows (off by default: Windows Terminal showed it wrongly)."""
    import os
    os.makedirs(os.environ["BLUEEDIT_CONFIG_DIR"], exist_ok=True)
    with open(os.path.join(os.environ["BLUEEDIT_CONFIG_DIR"], "settings.json"), "w", encoding="utf-8") as f:
        f.write('{ "fastScroll": true }')
    t = editor(_big_file(make_file, "big.cs", "cs", n=4000), dims=(40, 160))
    t.send(DOWN * 50, 1.5)
    lag, sent = _hold_down(t)
    assert lag < 0.15
    # repainting the whole 160x40 screen is ~5.6 KB per line in code; moving rows brings it to ~0.6 KB
    assert sent / 60 < 2000, f"{sent / 60:.0f} bytes per scrolled line"


def test_idle_editor_stays_quiet(editor, make_file):
    t = editor(_big_file(make_file, "big.cs", "cs", n=200))
    time.sleep(1.0)
    n0 = sum(len(d) for _, d in t.chunks)
    time.sleep(3.0)
    rate = (sum(len(d) for _, d in t.chunks) - n0) / 3.0
    assert rate < 3000, f"idle output {rate:.0f} B/s"


def test_key_to_glyph_latency_of_the_editor(editor, make_file):
    """End to end: key written into the console -> character visible on the screen.
    With Terminal.Gui this was 43-55 ms; the own terminal layer brought it to ~5 ms (harness floor ~6 ms)."""
    t = editor(_big_file(make_file, "big.md", "md"))
    t.send(DOWN * 3, 0.5)
    typed, xs = "", []
    for i in range(30):
        ch = "abcdefghijklmnopqrstuvwxyz"[i % 26]
        typed += ch
        t0 = time.perf_counter()
        t.p.write(ch)
        while time.perf_counter() - t0 < 1.0:
            if typed in "".join(t.rows()[2:8]):
                break
            time.sleep(0.0005)
        xs.append(time.perf_counter() - t0)
        time.sleep(0.12)
    xs.sort()
    median, p95 = statistics.median(xs) * 1000, xs[int(len(xs) * 0.95) - 1] * 1000
    assert median < 20, f"median {median:.1f} ms"
    assert p95 < 50, f"p95 {p95:.1f} ms"
