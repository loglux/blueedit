import subprocess
import time

import pytest

from harness import (CTRL_LEFT, CTRL_RIGHT, DOWN, END, ESC, F4, F5, HOME, SHIFT_CTRL_RIGHT, UP, WINDOWS, click, ctrl, wheel_down)


def test_typing_marks_document_modified(editor, make_file):
    t = editor(make_file("a.txt", "hello\n"))
    t.send("XY")
    assert t.body()[0].startswith("XYhello")
    assert t.pos() == (1, 3)
    assert "1:a.txt*" in t.tabs()


def test_undo_reverts_a_typed_word_in_one_step(editor, make_file):
    t = editor(make_file("a.txt", "hello\n"))
    t.send("abc")
    t.send(ctrl("z"))
    assert t.body()[0].startswith("hello")
    t.send(ctrl("y"))
    assert t.body()[0].startswith("abchello")


def test_word_navigation(editor, make_file):
    t = editor(make_file("a.txt", "hello big world\n"))
    t.send(CTRL_RIGHT); assert t.pos() == (1, 7)
    t.send(CTRL_RIGHT); assert t.pos() == (1, 11)
    t.send(CTRL_LEFT); assert t.pos() == (1, 7)


def test_delete_and_duplicate_line(editor, make_file):
    t = editor(make_file("a.txt", "one\ntwo\nthree\n"))
    t.send(DOWN)
    t.send(ctrl("d"))
    assert [r.strip() for r in t.body()[:4]] == ["one", "two", "two", "three"]
    t.send(ctrl("l"))
    assert [r.strip() for r in t.body()[:3]] == ["one", "two", "three"]


def test_caret_remembers_its_column_across_short_lines(editor, make_file):
    t = editor(make_file("a.txt", "long line here\nx\nlong line here\n"))
    t.send(END)
    t.send(HOME)
    t.send(CTRL_RIGHT)               # column 6
    start = t.pos()[1]
    t.send(DOWN); t.send(DOWN)
    assert t.pos() == (3, start)


def test_mouse_click_positions_the_caret(editor, make_file):
    t = editor(make_file("a.txt", "first line\nsecond line\n"))
    t.send(click(8, 4))               # screen column 8, row 4 -> second text line, column index 7
    assert t.pos() == (2, 8)


def test_mouse_wheel_scrolls(editor, make_file):
    t = editor(make_file("a.txt", "\n".join(f"row {i}" for i in range(100)) + "\n"))
    assert t.body()[0].strip() == "row 0"
    t.send(wheel_down(10, 6), 0.1)
    deadline = time.time() + 3                                  # a loaded machine may take a moment
    while time.time() < deadline and t.body()[0].strip() != "row 3":
        time.sleep(0.05)
    assert t.body()[0].strip() == "row 3"


def test_column_mode_toggle_shows_in_status(editor, make_file):
    t = editor(make_file("a.txt", "abc\n"))
    assert "STREAM" in t.status()
    t.send(F4); assert "COLUMN" in t.status()
    t.send(F4); assert "STREAM" in t.status()


def test_find_next_and_goto(editor, make_file):
    t = editor(make_file("a.txt", "alpha\nbeta\nalpha end\n"))
    t.send(ctrl("f"), 0.8)
    assert t.has_dialog("Find")
    t.send("alpha"); t.send("\r", 0.8)
    assert t.pos() == (3, 6) or t.pos()[0] in (1, 3)       # first match after the caret
    t.send(F5)
    t.send(ctrl("g"), 0.8); t.send("2"); t.send("\r", 0.8)
    assert t.pos()[0] == 2


def test_escape_cancels_a_prompt_without_typing_into_the_text(editor, make_file):
    t = editor(make_file("a.txt", "keep\n"))
    t.send(ctrl("f"), 0.8)
    t.send("zzz", 0.4)
    t.send(ESC, 0.8)
    assert not t.has_dialog()
    assert t.body()[0].startswith("keep")


def test_replace_all_reports_count(editor, make_file):
    t = editor(make_file("a.txt", "line one\nsecond line\nthird\n"))
    t.send(ctrl("r"), 0.8); t.send("line", 0.4); t.send("\t", 0.3)
    t.send("ROW", 0.4); t.send("\r", 1.0)
    assert "Replace this match?" in t.text()
    t.send("a", 1.0)                                              # all
    assert [r.strip() for r in t.body()[:2]] == ["ROW one", "second ROW"]
    assert "Replaced 2" in t.status()


def _ps(command, stdin=None):
    return subprocess.run(["powershell", "-NoProfile", "-Command", command], input=stdin, capture_output=True, text=True,
                          encoding="utf-8").stdout


@pytest.fixture
def own_clipboard():
    """The test needs the real Windows clipboard: whatever the user had there is put back afterwards.
    (Copying something by hand while the test runs can still make it fail.)"""
    saved = _ps("[Console]::OutputEncoding = [Text.Encoding]::UTF8; Get-Clipboard -Raw")
    yield
    if saved:
        _ps("[Console]::InputEncoding = [Text.Encoding]::UTF8; Set-Clipboard -Value ([Console]::In.ReadToEnd())", saved)


@pytest.mark.skipif(bool(__import__("os").environ.get("BLUEEDIT_NO_CLIPBOARD")),
                    reason="uses the real Windows clipboard; off on the CI runner, where the user may be copying")
def test_system_clipboard_paste_and_copy(editor, make_file, own_clipboard):
    _ps("Set-Clipboard -Value 'FROM_SYSTEM'")
    t = editor(make_file("a.txt", "hello big world\n"))
    t.send(ctrl("v"))
    assert t.body()[0].startswith("FROM_SYSTEMhello")
    t.send(SHIFT_CTRL_RIGHT)                                 # select "hello "
    t.send(ctrl("c"))
    assert _ps("Get-Clipboard").strip() == "hello"


def test_delete_word_with_ctrl_backspace(editor, make_file):
    t = editor(make_file("a.txt", "hello big world\n"))
    t.send(END)
    t.send("\x08")                                           # Ctrl+Backspace arrives as BS
    assert t.body()[0].startswith("hello big")


def test_digits_capitals_cyrillic_and_punctuation_are_typed_correctly(editor, make_file):
    """Regression: the 'windows' input driver turned digit keys into '-'."""
    t = editor(make_file("a.txt", "\n"))
    t.send("Test 0123456789 Привет-мир, ЖЗ!")
    assert t.body()[0].startswith("Test 0123456789 Привет-мир, ЖЗ!")


def test_goto_prompt_accepts_digits(editor, make_file):
    t = editor(make_file("a.txt", "a\nb\nc\nd\n"))
    t.send(ctrl("g"), 0.8); t.send("3"); t.send("\r", 0.8)
    assert t.pos()[0] == 3


def test_ctrl_slash_comments_a_line_and_alt_down_moves_it(editor, make_file):
    from harness import ALT_DOWN, CTRL_SLASH
    t = editor(make_file("a.py", "a = 1\nb = 2\n"))
    t.send(CTRL_SLASH, 0.6)
    assert t.body()[:2] == ["# a = 1", "b = 2"]
    t.send(ALT_DOWN, 0.6)
    assert t.body()[:2] == ["b = 2", "# a = 1"]


@pytest.mark.skipif(not WINDOWS, reason="key events are the Windows console's")
def test_ctrl_slash_with_the_russian_layout_comments_with_key_events(editor, make_file):
    # The same key with the Russian layout types "." (#184): VT text loses the key, key events keep it.
    from harness import CTRL, w32
    t = editor(make_file("a.py", "a = 1\n"), env={"BLUEEDIT_KEY_EVENTS": "1"})
    t.send(w32(0xBF, uc=ord("."), cs=CTRL), 0.6)
    assert t.body()[0] == "# a = 1"
    t.send(w32(0x31, cs=CTRL), 0.4)                           # Ctrl+1 arrives too (bound to nothing): no change
    assert t.body()[0] == "# a = 1"


@pytest.mark.skipif(not WINDOWS, reason="key events are the Windows console's")
def test_a_paste_from_windows_terminal_goes_in_as_it_is_with_key_events(editor, make_file):
    # Windows Terminal's paste, as measured (#184): a key event for each character, space and Enter with their keys;
    # every Enter of it taken as typed put the auto-indent before each pasted line (a staircase, 18 October 2026)
    from harness import w32
    t = editor(make_file("a.py", ""), env={"BLUEEDIT_KEY_EVENTS": "1"})
    text = "def f():\r    x = 1\r    return x\r"
    keys = {" ": 0x20, "\r": 0x0D, ":": 0xBA, "=": 0xBB, "(": 0x39, ")": 0x30, "1": 0x31}
    t.send("".join(w32(keys.get(c, 0), uc=ord(c)) for c in text), 1.0)
    assert t.body()[:3] == ["def f():", "    x = 1", "    return x"]
