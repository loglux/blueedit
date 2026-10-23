"""Ctrl+Space word completion in the real editor."""
from harness import CTRL_SPACE, DOWN, END, ESC


def test_list_opens_under_the_word_and_enter_takes_the_choice(editor, make_file):
    t = editor(make_file("a.md", "alpha alpine\n\nal"))
    t.send(DOWN, 0.2); t.send(DOWN, 0.2); t.send(END, 0.3)
    t.send(CTRL_SPACE, 0.8)
    assert sum("alpine" in r for r in t.rows()) == 2               # in the text and in the list under "al"
    assert sum("alpha" in r for r in t.rows()) == 2
    t.send(DOWN, 0.3); t.send("\r", 0.8)
    assert t.body()[2].startswith("alpine")


def test_a_single_candidate_goes_in_at_once(editor, make_file):
    t = editor(make_file("a.txt", "unmistakable\n\nunm"))
    t.send(DOWN, 0.2); t.send(DOWN, 0.2); t.send(END, 0.3)
    t.send(CTRL_SPACE, 0.8)
    assert t.body()[2].startswith("unmistakable")


def test_keywords_of_the_language_are_offered(editor, make_file):
    import time
    t = editor(make_file("a.cs", "names"))
    t.send(END, 0.3)
    t.send(CTRL_SPACE, 0.3)
    # C# may have a language server (csharp-ls): its candidates come first, the keywords after them, so "namespace" is
    # either put in at once (the only candidate) or listed
    deadline = time.time() + 3
    while time.time() < deadline and not t.body()[0].startswith("namespace") and not any("namespace" in r and "kw" in r for r in t.rows()):
        time.sleep(0.2)
    if not t.body()[0].startswith("namespace"):
        assert any("namespace" in r and "kw" in r for r in t.rows())
        t.send("p", 0.5)                                               # "namesp": only namespace is left
        t.send("\t", 0.5)
    assert t.body()[0].startswith("namespace")


def test_powershell_commands_complete_with_the_hyphen(editor, make_file):
    t = editor(make_file("a.ps1", ""))
    t.send("$f = Get-Ch", 0.4)
    t.send(CTRL_SPACE, 0.8)
    assert t.body()[0].startswith("$f = Get-ChildItem")


def test_escape_leaves_the_text_alone(editor, make_file):
    t = editor(make_file("a.txt", "alpha alpine\nal"))
    t.send(DOWN, 0.2); t.send(END, 0.3)
    t.send(CTRL_SPACE, 0.8); t.send(ESC, 0.6)
    assert t.body()[1].rstrip() == "al" and "*" not in t.tabs()


def test_nothing_to_complete_says_so(editor, make_file):
    t = editor(make_file("a.txt", "zzz\nqq"))
    t.send(DOWN, 0.2); t.send(END, 0.3)
    t.send(CTRL_SPACE, 0.8)
    assert "No words starting with" in t.status()


def test_in_code_the_list_opens_by_itself_and_tab_takes_the_word(editor, make_file):
    # JavaScript: no language server here, so the list is the text's own words (test_lsp.py covers servers)
    t = editor(make_file("a.js", "maximum_value = 1\n"))
    t.send(DOWN, 0.2)
    t.send("x = max", 0.8)                                          # three letters: the list opens by itself
    assert sum("maximum_value" in r for r in t.rows()) == 2         # in the text and in the list
    t.send("\t", 0.6)
    assert t.body()[1].startswith("x = maximum_value")


def test_enter_in_a_list_that_opened_by_itself_starts_a_new_line(editor, make_file):
    t = editor(make_file("a.js", "maximum_value = 1\n"))
    t.send(DOWN, 0.2)
    t.send("x = max", 0.8)
    t.send("\r", 0.6)
    assert t.body()[1].rstrip() == "x = max" and t.pos()[0] == 3


def test_prose_does_not_open_the_list_by_itself(editor, make_file):
    t = editor(make_file("a.md", "maximum value\n"))
    t.send(DOWN, 0.2)
    t.send("max", 0.8)
    assert sum("maximum" in r for r in t.rows()) == 1               # only the text's own
