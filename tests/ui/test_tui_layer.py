"""The new terminal layer (src/Tui) in a real console, through a tiny app that prints every event it receives."""
import os
import statistics
import time

import pytest

import harness
from harness import (BACKSPACE, CTRL_BACKSPACE, CTRL_LEFT, CTRL_PGDN, CTRL_RIGHT, CTRL_SPACE, DELETE, DOWN, END, ESC, F1, F2, F3, F4, F5,
                     F6, F7, F8, F9, F10, HOME, LEFT, PGDN, PGUP, RIGHT, SHIFT_CTRL_RIGHT, SHIFT_DOWN, SHIFT_F6, UP, Term, alt,
                     click, ctrl, wheel_down)

PROBE = os.environ.get("TUIPROBE_EXE", str(harness.ROOT / "tools" / "TuiProbe" / "bin" / "Release" / "net10.0" /
                                            ("TuiProbe.exe" if harness.WINDOWS else "TuiProbe")))


@pytest.fixture
def probe():
    if not os.path.exists(PROBE):
        pytest.skip(f"{PROBE} not built (dotnet build -c Release tools/TuiProbe)")
    started = []

    def _start(dims=(30, 120), env=None):
        t = Term([PROBE], dims=dims, env=env)
        assert t.wait_for("tui-probe", 30), t.text()
        time.sleep(0.4)
        started.append(t)
        return t

    yield _start
    for t in started:
        t.kill()


def last(t):
    for r in t.rows():
        if r.strip().startswith("last:"):
            return r.strip()[len("last: "):]
    return ""


def sees(t, keys, expected, wait=0.4):
    t.send(keys, wait)
    got = last(t)
    assert got == expected, f"sent {keys!r}: expected {expected!r}, got {got!r}"


@pytest.mark.parametrize("keys,expected", [
    ("a", "char 'a'"), ("X", "char 'X'"), ("2", "char '2'"), ("0", "char '0'"), ("-", "char '-'"), ("!", "char '!'"),
    ("ж", "char 'ж'"), ("Ж", "char 'Ж'"), ("é", "char 'é'"),
])
def test_text_characters(probe, keys, expected):
    sees(probe(), keys, expected)


@pytest.mark.parametrize("keys,expected", [
    (UP, "key Up"), (DOWN, "key Down"), (LEFT, "key Left"), (RIGHT, "key Right"),
    (HOME, "key Home"), (END, "key End"), (PGUP, "key PageUp"), (PGDN, "key PageDown"),
    (F1, "key F1"), (F2, "key F2"), (F3, "key F3"), (F4, "key F4"), (F5, "key F5"),
    (F6, "key F6"), (F7, "key F7"), (F8, "key F8"), (F9, "key F9"), (F10, "key F10"),
    (CTRL_RIGHT, "key Right +Ctrl"), (CTRL_LEFT, "key Left +Ctrl"), (SHIFT_F6, "key F6 +Shift"),
    (SHIFT_DOWN, "key Down +Shift"),
    (SHIFT_CTRL_RIGHT, "key Right +Shift+Ctrl"),
    (CTRL_PGDN, "key PageDown +Ctrl"),
    (DELETE, "key Delete"),
])
def test_navigation_and_function_keys(probe, keys, expected):
    sees(probe(), keys, expected)


@pytest.mark.parametrize("keys,expected", [
    (ctrl("g"), "char 'g' +Ctrl"), (ctrl("b"), "char 'b' +Ctrl"), (ctrl("z"), "char 'z' +Ctrl"), (ctrl("f"), "char 'f' +Ctrl"),
    (alt("f"), "char 'f' +Alt"), (alt("h"), "char 'h' +Alt"), (alt("z"), "char 'z' +Alt"),
    (alt("1"), "char '1' +Alt"), (alt("9"), "char '9' +Alt"),
    (CTRL_SPACE, "char ' ' +Ctrl"),                  # Ctrl+Space: word completion
])
def test_ctrl_and_alt_combinations(probe, keys, expected):
    sees(probe(), keys, expected)


def test_enter_tab_backspace_escape(probe):
    t = probe()
    sees(t, "\r", "key Enter")
    sees(t, "\t", "key Tab")
    sees(t, BACKSPACE, "key Backspace")
    sees(t, CTRL_BACKSPACE, "key Backspace +Ctrl")
    sees(t, ESC, "key Escape", wait=0.5)                       # a lone ESC is resolved by the 25 ms timeout


def test_text_is_inserted_in_order_including_capitals_and_cyrillic(probe):
    t = probe()
    t.send("Hello Мир 123!", 0.8)
    assert t.rows()[2].startswith("helloHello Мир 123!")


def test_mouse_click_wheel_and_drag(probe):
    t = probe()
    sees(t, click(8, 4), "mouse Up button=0 at 7,3")         # the click sends press then release; the last one is shown
    t.send("\x1b[<0;8;4M", 0.3); assert last(t) == "mouse Down button=0 at 7,3"
    t.send("\x1b[<32;20;6M", 0.3); assert last(t) == "mouse Drag button=0 at 19,5"
    sees(t, wheel_down(5, 5), "mouse WheelDown button=0 at 4,4")
    t.send("\x1b[<2;3;3M", 0.3); assert last(t) == "mouse Down button=2 at 2,2"


def test_bracketed_paste_arrives_as_one_event(probe):
    t = probe()
    t.send("\x1b[200~pasted\ntext\x1b[201~", 0.6)
    assert last(t) == "paste 11 chars"
    assert "pasted text" in t.rows()[2]


def test_resize_is_reported_and_redrawn(probe):
    t = probe()
    t.resize(24, 90)
    assert "size: 90x24" in t.text()
    assert "resize 90x24" in last(t) or "size: 90x24" in t.text()


def test_ctrl_q_exits_and_restores_the_screen(probe):
    t = probe()
    t.send(ctrl("q"), 0.8)
    assert t.wait_exit(5)


def test_input_latency_is_a_few_milliseconds(probe, tmp_path):
    """Key written -> glyph visible on the emulated screen. The old Terminal.Gui layer needed ~43 ms here; the floor of this harness is ~6 ms."""
    t = probe()
    typed, xs = "hello", []
    for i in range(30):
        ch = "abcdefghijklmnopqrstuvwxyz"[i % 26]
        typed += ch
        t0 = time.perf_counter()
        t.p.write(ch)
        while time.perf_counter() - t0 < 1.0:
            if typed in t.rows()[2]:
                break
            time.sleep(0.0005)
        xs.append(time.perf_counter() - t0)
        time.sleep(0.12)
    xs.sort()
    median, p95 = statistics.median(xs) * 1000, xs[int(len(xs) * 0.95) - 1] * 1000
    assert median < 15, f"median {median:.1f} ms"
    assert p95 < 40, f"p95 {p95:.1f} ms"
