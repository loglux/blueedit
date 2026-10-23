"""Find with options, find previous, interactive replace, find in folder, block indent, Russian keyboard layout."""
from harness import ALT_F7, DOWN, ESC, F5, SHIFT_DOWN, SHIFT_F5, SHIFT_TAB, alt, alt_ru, ctrl


def test_find_with_match_case_then_next_and_previous(editor, make_file):
    t = editor(make_file("a.txt", "Word word\nWORD word\n"))
    t.send(ctrl("f"), 0.8)
    assert t.has_dialog("Find") and "[ ] Match case" in t.text()
    t.send("word", 0.3); t.send(alt("c"), 0.4)
    assert "[x] Match case" in t.text()
    t.send("\r", 0.8)
    assert t.pos() == (1, 10)                                   # the lower-case one, not "Word"
    t.send(F5, 0.5); assert t.pos() == (2, 10)
    t.send(F5, 0.5); assert t.pos() == (1, 10) and "wrapped" in t.status()
    t.send(SHIFT_F5, 0.5); assert t.pos() == (2, 10)


def test_find_dialog_offers_the_last_search_and_typing_replaces_it(editor, make_file):
    t = editor(make_file("a.txt", "alpha beta alpha\n"))
    t.send(ctrl("f"), 0.8); t.send("alpha", 0.3); t.send("\r", 0.8)
    t.send(ctrl("f"), 0.8)
    assert "alpha" in t.text()
    t.send("beta", 0.3); t.send("\r", 0.8)
    assert t.pos() == (1, 11)


def test_regex_search_and_a_broken_pattern(editor, make_file):
    t = editor(make_file("a.txt", "id 12, id 345\n"))
    t.send(ctrl("f"), 0.8); t.send(r"\d{3}", 0.3); t.send(alt("r"), 0.3); t.send("\r", 0.8)
    assert t.pos() == (1, 14)
    t.send(ctrl("f"), 0.8); t.send("(open", 0.3); t.send("\r", 0.8)          # regex is still on
    assert "Search:" in t.status()


def test_replace_asks_at_each_match(editor, make_file):
    path = make_file("a.txt", "cat cat cat\n")
    t = editor(path)
    t.send(ctrl("r"), 0.8); t.send("cat", 0.3); t.send("\t", 0.3); t.send("dog", 0.3); t.send("\r", 0.8)
    assert "Replace this match?" in t.text()
    t.send("y", 0.5); t.send("n", 0.5); t.send("y", 0.8)
    assert t.body()[0].startswith("dog cat dog")
    assert "Replaced 2" in t.status()


def test_replace_answers_work_on_a_russian_layout(editor, make_file):
    t = editor(make_file("a.txt", "a a\n"))
    t.send(ctrl("r"), 0.8); t.send("a", 0.3); t.send("\t", 0.3); t.send("b", 0.3); t.send("\r", 0.8)
    t.send("ф", 0.8)                                            # the A key with the Russian layout: all
    assert t.body()[0].startswith("b b")


def test_find_in_folder_lists_matches_and_opens_the_file_at_the_line(editor, make_file, work):
    make_file("notes.md", "# Notes\nsome needle here\n")
    (work / "sub").mkdir()
    (work / "sub" / "code.cs").write_text("int x;\n\n// needle too\n", encoding="utf-8")
    t = editor(make_file("start.txt", "start\n"))
    t.send(ALT_F7, 0.8)
    assert t.has_dialog("Find in folder")
    t.send("needle", 0.3); t.send("\r", 2.0)
    assert t.has_dialog("2 matches in 2 files")
    assert "notes.md:2:" in t.text() and "code.cs:3:" in t.text()
    t.send(DOWN, 0.3); t.send("\r", 1.2)                        # the second result
    assert "code.cs" in t.tabs() and t.pos() == (3, 10)


def test_tab_and_shift_tab_shift_selected_lines(editor, make_file):
    t = editor(make_file("a.txt", "one\ntwo\nthree\n"))
    t.send(SHIFT_DOWN, 0.2); t.send(SHIFT_DOWN, 0.2)
    t.send("\t", 0.5)
    assert t.body()[:3] == ["    one", "    two", "three"] or [r.rstrip() for r in t.body()[:3]] == ["    one", "    two", "three"]
    t.send(SHIFT_TAB, 0.5)
    assert [r.rstrip() for r in t.body()[:3]] == ["one", "two", "three"]


def test_alt_with_a_russian_layout_opens_the_menu(editor, make_file):
    t = editor(make_file("a.txt", "x\n"))
    t.send(alt_ru("f", "а"), 0.8)                               # Alt+F typed on the Russian layout
    assert "Open..." in t.text()
    t.send(ESC, 0.5)
