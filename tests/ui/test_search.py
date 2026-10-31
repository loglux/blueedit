"""Find with options, find previous, interactive replace, find in folder, block indent, Russian keyboard layout."""
from harness import ALT_EQUALS, ALT_F7, ALT_LEFT, ALT_MINUS, ALT_RIGHT, CTRL_BRACKET, CTRL_SHIFT_F12, DOWN, END, ESC, F5, F6, RIGHT, SHIFT_DOWN, SHIFT_F5, SHIFT_F12, SHIFT_TAB, alt, alt_ru, ctrl


def go_to_line(t, n):
    t.send(ctrl("g"), 0.6); t.send(f"{n}\r", 0.6)


def test_back_and_forward_over_jumps_but_not_over_arrows(editor, make_file):
    t = editor(make_file("a.txt", "".join(f"line {i}\n" for i in range(1, 61))))
    t.send(ALT_LEFT, 0.5)
    assert "Nothing to go back to" in t.status()
    go_to_line(t, 20)
    t.send(DOWN + DOWN, 0.4)                                    # on line 22: arrows are not jumps
    go_to_line(t, 50)
    assert t.pos()[0] == 50
    t.send(ALT_LEFT, 0.5)
    assert t.pos()[0] == 22
    t.send(ALT_LEFT, 0.5)
    assert t.pos()[0] == 1
    t.send(ALT_RIGHT, 0.5)
    assert t.pos()[0] == 22
    t.send(ALT_RIGHT, 0.5)
    assert t.pos()[0] == 50
    t.send(ALT_RIGHT, 0.5)
    assert "Nothing to go forward to" in t.status()


def test_the_f12_keys_go_back_and_forward_too_for_terminals_that_keep_alt_arrows(editor, make_file):
    t = editor(make_file("a.txt", "".join(f"line {i}\n" for i in range(1, 61))))
    go_to_line(t, 40)
    t.send(SHIFT_F12, 0.5)
    assert t.pos()[0] == 1
    t.send(CTRL_SHIFT_F12, 0.5)
    assert t.pos()[0] == 40


def test_ctrl_bracket_goes_to_the_pair_and_back_over_lines_and_back_returns_once(editor, make_file):
    t = editor(make_file("a.py", "def f(\n    a,\n    b):\n    return [a, b]\n"))
    t.send(ctrl("g"), 0.6); t.send("1\r", 0.6)
    t.send(RIGHT * 5, 0.4)                                      # on the "("
    assert t.pos() == (1, 6)
    t.send(CTRL_BRACKET, 0.5)
    assert t.pos() == (3, 6)                                    # on the ")"
    t.send(CTRL_BRACKET, 0.5)
    assert t.pos() == (1, 6)
    t.send(CTRL_BRACKET, 0.5)
    assert t.pos() == (3, 6)
    t.send(ALT_LEFT, 0.5)                                       # to and fro is one jump
    assert t.pos() == (1, 6)


def test_ctrl_bracket_works_in_a_formula_of_markdown_and_says_why_not(editor, make_file):
    t = editor(make_file("a.md", "$\\frac{a}{bc}$ word\n"))
    t.send(RIGHT * 6, 0.4)                                      # on the first "{"
    assert t.pos() == (1, 7)
    t.send(CTRL_BRACKET, 0.5)
    assert t.pos() == (1, 9)
    t.send(ctrl("g"), 0.6); t.send("1\r", 0.6)
    t.send(END, 0.3)
    t.send(CTRL_BRACKET, 0.5)
    assert "No bracket at the caret" in t.status()


def test_ctrl_bracket_on_a_bracket_without_a_pair_says_so(editor, make_file):
    t = editor(make_file("a.py", "(x\n"))
    t.send(CTRL_BRACKET, 0.5)
    assert "No pair for this bracket" in t.status() and t.pos() == (1, 1)


def test_alt_equals_selects_wider_by_meaning_and_typing_replaces_it(editor, make_file):
    t = editor(make_file("a.txt", "Food sets (the limit of growth). It slows.\n"))
    t.send(RIGHT * 15, 0.4)                                     # in "limit"
    t.send(ALT_EQUALS + ALT_EQUALS, 0.6)                        # the word, then what is in the brackets
    t.send("new", 0.5)
    assert t.body()[0].startswith("Food sets (new). It slows.")


def test_alt_minus_goes_back_a_step_and_a_move_forgets_the_steps(editor, make_file):
    t = editor(make_file("a.txt", "Food sets (the limit of growth). It slows.\n"))
    t.send(RIGHT * 15, 0.4)
    t.send(ALT_EQUALS + ALT_EQUALS + ALT_EQUALS, 0.6)           # with the brackets
    t.send(ALT_MINUS + ALT_MINUS, 0.6)                          # back to the word
    t.send("X", 0.5)
    assert t.body()[0].startswith("Food sets (the X of growth).")
    t.send(ALT_MINUS, 0.5)                                      # typing ended them: nothing happens
    t.send("Y", 0.5)
    assert t.body()[0].startswith("Food sets (the XY of growth).")


def test_back_after_a_search_returns_to_where_it_began_however_many_times_f5_was_pressed(editor, make_file):
    t = editor(make_file("a.txt", "".join(("word\n" if i % 10 == 0 else f"line {i}\n") for i in range(1, 61))))
    go_to_line(t, 5)
    t.send(ctrl("f"), 0.6); t.send("word\r", 0.6)
    assert t.pos()[0] == 10
    t.send(F5, 0.4); t.send(F5, 0.4)
    assert t.pos()[0] == 30
    t.send(ALT_LEFT, 0.5)
    assert t.pos()[0] == 5


def test_back_goes_across_files_and_the_menu_has_it(editor, make_file):
    t = editor(make_file("a.txt", "".join(f"a{i}\n" for i in range(1, 41))), make_file("b.txt", "".join(f"b{i}\n" for i in range(1, 41))))
    t.send(alt("w"), 0.6); t.send("1", 0.6)
    assert "a.txt" in t.status()
    go_to_line(t, 30)
    go_to_line(t, 10)
    t.send(F6, 0.6)                                             # switching files is not a jump, but back still finds the place
    assert "b.txt" in t.status()
    t.send(alt("s"), 0.6)
    assert "Alt+Left" in t.text() and "Alt+Right" in t.text()
    t.send("b", 0.8)                                            # Search > Go back
    assert "a.txt" in t.status() and t.pos()[0] == 30


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
