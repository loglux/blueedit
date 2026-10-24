import time

import pytest

from harness import DOWN, END, ESC, F1, F9, HOME, PGDN, UP, alt, click, ctrl

WORDS = " ".join(f"w{i:03d}" for i in range(60))             # 299 characters, a word every 5 columns


# ---------------------------------------------------------------- soft wrap
def test_paragraph_wraps_at_word_boundaries(editor, make_file):
    t = editor(make_file("w.md", WORDS + "\ntail\n"), dims=(30, 110))
    rows = [r for r in t.body(5)]
    assert [len(r) for r in rows[:3]] == [104, 104, 89]
    assert " ".join(rows[:3]) == WORDS
    assert rows[3] == "tail"
    assert "WRAP" in t.status()


def test_vertical_movement_follows_visual_rows(editor, make_file):
    t = editor(make_file("w.md", WORDS + "\ntail\n"), dims=(30, 110))
    t.send(DOWN); assert t.pos() == (1, 106)
    t.send(DOWN); assert t.pos() == (1, 211)
    t.send(DOWN); assert t.pos() == (2, 1)
    t.send(UP); assert t.pos() == (1, 211)


def test_home_end_stay_on_the_visual_row(editor, make_file):
    t = editor(make_file("w.md", WORDS + "\n"), dims=(30, 110))
    t.send(DOWN)
    t.send(END); assert t.pos() == (1, 210)
    t.send(HOME); assert t.pos() == (1, 106)


def test_click_on_a_wrapped_row_maps_to_the_right_column(editor, make_file):
    t = editor(make_file("w.md", WORDS + "\n"), dims=(30, 110))
    t.send(click(4, 4))                                          # second visual row, third column
    assert t.pos() == (1, 109)


def test_typing_rewraps_and_alt_z_toggles(editor, make_file):
    t = editor(make_file("w.md", WORDS + "\ntail\n"), dims=(30, 110))
    t.send("A" * 20)
    deadline = time.time() + 2                                    # a busy machine draws the 20 letters a moment later
    while time.time() < deadline and not t.body(3)[0].startswith("A" * 20):
        time.sleep(0.05)
    r = t.body(3)
    assert r[0].startswith("A" * 20 + "w000") and all(len(x) <= 109 for x in r)
    t.send(alt("z"), 0.8)
    assert "WRAP" not in t.status() and t.body()[1] == "tail"
    t.send(alt("z"), 0.8)
    assert "WRAP" in t.status()


def test_line_numbers_are_off_and_alt_g_turns_them_on_for_good(editor, make_file):
    f = make_file("c.py", "".join(f"x{i} = {i}\n" for i in range(1, 12)))
    t = editor(f)
    assert t.body()[0].startswith("x1 = 1")
    t.send(alt("g"), 0.8)
    assert t.body()[0].startswith(" 1 x1 = 1") and t.body()[10].startswith("11 x11 = 11")
    t.send(click(6, 6))                                          # line 4, third column of its text, right of " 4 "
    assert t.pos() == (4, 3)
    t.kill()
    t = editor(f)                                                # remembered in the settings
    assert t.body()[0].startswith(" 1 x1 = 1")
    t.send(alt("g"), 0.8)
    assert t.body()[0].startswith("x1 = 1")


def test_the_status_line_shows_the_line_and_the_number_of_lines(editor, make_file):
    t = editor(make_file("c.py", "".join(f"x{i} = {i}\n" for i in range(1, 12))))
    assert "Ln 1/12  Col 1" in t.status()                           # 11 lines and the empty one after the last newline
    t.send(ctrl("g"), 0.6); t.send("5\r", 0.6)
    assert t.wait_for("Ln 5/12", 5), t.status()                     # waits: failed once with 0.4 s while pyright started
    t.send(END, 0.2); t.send("\r", 0.4)
    assert t.wait_for("Ln 6/13", 5), t.status()


def test_code_files_do_not_wrap_and_text_files_do(editor, make_file):
    t = editor(make_file("c.cs", "// " + WORDS + "\nint a;\n"))
    assert "WRAP" not in t.status() and t.body()[1].startswith("int a;")
    t.kill()
    t = editor(make_file("x.txt", WORDS + "\nz\n"))
    assert "WRAP" in t.status() and t.body()[3] == "z"


def test_resize_reflows_the_text(editor, make_file):
    t = editor(make_file("w.md", WORDS + "\n"))
    t.resize(30, 60)
    rows = [r for r in t.rows()[2:12] if r]
    assert rows and all(len(r) <= 59 for r in rows)
    assert rows[0].startswith("w000 w001")


# ---------------------------------------------------------------- menus
@pytest.mark.parametrize("keys,expected", [
    (alt("f"), "Ctrl+N"), (alt("e"), "Ctrl+Z"), (alt("s"), "Find..."), (alt("o"), "Bold"),
    (alt("t"), "Spelling"), (alt("b"), "Column mode"), (alt("w"), "Previous"), (alt("p"), "Keys and commands"),
    (alt("v"), "Line numbers"), (F9, "Ctrl+N"),
])
def test_every_way_into_the_menu(editor, make_file, keys, expected):
    t = editor(make_file("a.txt", "x\n"))
    t.send(keys, 1.0)
    assert expected in " ".join(t.rows()[1:12])
    t.send(ESC, 0.5)


def test_menu_bar_order(editor, make_file):
    t = editor(make_file("a.txt", "x\n"))
    bar = t.rows()[0].split()
    assert bar == ["File", "Edit", "Search", "View", "Format", "Tools", "Block", "Window", "Help"]


def test_mouse_opens_a_menu(editor, make_file):
    t = editor(make_file("a.txt", "x\n"))
    t.send(click(8, 1), 1.0)                                    # the word "Edit"
    assert "Ctrl+Z" in " ".join(t.rows()[1:8])


def test_alt_shortcuts_do_not_collide_with_menu_hotkeys(editor, make_file):
    """Alt+T opens Tools (it used to apply strikethrough and edit the document)."""
    t = editor(make_file("a.txt", "plain\n"))
    t.send(alt("t"), 1.0)
    assert "Spelling" in " ".join(t.rows()[1:8])
    t.send(ESC, 0.5)
    assert t.body()[0].startswith("plain") and "*" not in t.tabs()


# ---------------------------------------------------------------- help
def test_f1_opens_help_that_scrolls_and_closes(editor, make_file):
    t = editor(make_file("a.txt", "x\n"))
    t.send(F1, 1.2)
    assert t.has_dialog("Help")
    text = t.text()                                              # the contents at the left, Basics (a plain file) open
    assert "Moving and selecting" in text and "Save all and quit" in text
    t.send(DOWN, 0.4)                                            # the next section: Files
    assert "Save as..." in t.text() and "Save all and quit" not in t.text()
    for _ in range(20):                                          # Down at the last section stays there
        t.send(DOWN, 0.2)
    assert "Far Manager" in t.text()
    t.send(ESC, 0.8)
    assert not t.has_dialog()


def test_f1_in_a_markdown_file_opens_on_markdown_and_a_long_section_scrolls(editor, make_file):
    t = editor(make_file("a.md", "# x\n"))
    t.send(F1, 1.2)
    assert "Task checkbox" in t.text()                            # the Markdown section
    t.send(UP * 2, 0.4)                                          # Editing: longer than the window
    assert "Ctrl+Z" in t.text() and "▼" in t.text()
    t.send(PGDN, 0.4)
    assert "▲" in t.text() or "▼" not in t.text()


def test_help_menu_has_about(editor, make_file):
    t = editor(make_file("a.txt", "x\n"))
    t.send(alt("p"), 0.9); t.send(DOWN, 0.4); t.send("\r", 1.0)
    assert t.has_dialog("About") and "BlueEdit" in t.text()
    import re
    assert re.search(r"BlueEdit \d+\.\d+\.\d+", t.text())               # the version is shown


def test_help_is_usable_in_an_80x25_terminal(editor, make_file):
    t = editor(make_file("a.txt", "x\n"), dims=(25, 80))
    t.send(F1, 1.2)
    assert t.has_dialog("Help")
    t.send(ESC, 0.8)
    assert not t.has_dialog()


# ---------------------------------------------------------------- paragraph formatting (#91)
def test_alt_r_reformats_a_paragraph_to_the_right_margin(editor, make_file, work):
    path = make_file("p.txt", WORDS + "\n\nnext\n")
    t = editor(path, dims=(30, 110))
    t.send(alt("r"), 0.6)
    assert "Reformatted" in t.status()
    t.send(ctrl("s"), 0.8)
    lines = open(path, encoding="utf-8").read().split("\n")
    assert all(len(l) <= 78 for l in lines) and lines[:4] == [" ".join(f"w{i:03d}" for i in range(n, min(n + 15, 60))) for n in (0, 15, 30, 45)]
    assert lines[4:] == ["", "next", ""]
