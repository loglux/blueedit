import re
import time

import pytest

import harness
from harness import CTRL_F7, CTRL_RIGHT, DOWN, END, ESC, F7, F8, HOME, SHIFT_F7, UP, alt, ctrl


# the spelling provider the editor uses by default here: Windows' own, or Hunspell on Linux
SPELLER = "Windows" if harness.WINDOWS else "Hunspell"


def ctrl_f7():
    return CTRL_F7

MD = (
    "# Main title\n"                                                  # 0
    "\n"
    "Some **bold** and *italic* with a [link](http://example.com) here.\n"   # 2
    "\n"
    "- bullet one\n"                                                  # 4
    "- [ ] task open\n"                                               # 5
    "- [x] task done\n"                                               # 6
    "\n"
    "> a quote\n"                                                     # 8
    "\n"
    "---\n"                                                           # 10
    "\n"
    "```cs\n"                                                         # 12
    "int x = **keep**;\n"                                             # 13
    "```\n"                                                           # 14
    "\n"
    "Last line with `code` and $a^2$.\n"                              # 16
)


@pytest.fixture
def live(editor, make_file):
    return editor(make_file("live.md", MD))


def test_live_preview_hides_markup_but_keeps_the_caret_line_raw(live):
    r = live.body(17)
    assert r[0] == "# Main title"                                       # caret line is raw
    assert r[2] == "Some bold and italic with a link here."
    assert r[4:7] == ["• bullet one", "☐ task open", "☑ task done"]
    assert r[8] == "│ a quote"
    assert set(r[10]) == {"─"}
    assert "int x = **keep**;" in r[13]                                 # code fences are left as written
    assert r[16] == "Last line with code and a²."
    assert "LIVE" in live.status()


def test_moving_the_caret_swaps_which_line_is_raw(live):
    live.send(DOWN + DOWN)
    r = live.body(4)
    assert r[0] == "Main title"                                         # heading is now concealed
    assert r[2].startswith("Some **bold** and *italic* with a [link](")


def test_reading_mode_is_read_only_and_ctrl_e_returns(live):
    live.send(ctrl("e"), 0.8)
    assert "READ" in live.status()
    before = live.body(17)
    live.send("zzz"); live.send("\r"); live.send("\x7f"); live.send(ctrl("b"))
    assert live.body(17) == before
    live.send(ctrl("e"), 0.8)
    assert "LIVE" in live.status()


def test_alt_v_opens_the_view_menu_and_s_l_r_pick_a_view(live):
    live.send(alt("v"), 0.7); live.send("r", 0.7); assert "READ" in live.status()
    live.send(alt("v"), 0.7); live.send("s", 0.7); assert "SRC" in live.status()
    live.send(alt("v"), 0.7); live.send("l", 0.7); assert "LIVE" in live.status()


def test_math_is_rendered_in_unicode(editor, make_file):
    src = ("# Formulas\n\n"
           "Inline: $E = mc^2$ and $\\alpha + \\beta \\le \\gamma$ here.\n"
           "Price is $5 and $10 total, not math.\n"
           "A sum $\\sum_{i=1}^{n} i = \\frac{n(n+1)}{2}$ done.\n\n"
           "$$\n\\int_0^1 x^2\\,dx = \\frac{1}{3}\n\\sqrt{a^2+b^2} \\ge 0\n$$\n\n"
           "One line: $$x \\in \\mathbb{R}$$ end.\n")
    t = editor(make_file("m.md", src))
    r = t.body(20)
    assert r[2] == "Inline: E = mc² and α + β ≤ γ here."
    assert r[3] == "Price is $5 and $10 total, not math."
    # a line with a fraction inside is laid out over rows, its words on the formula's baseline
    k = next(i for i, row in enumerate(r) if row.startswith("A sum"))
    assert "∑" in r[k] and "─" in r[k] and r[k].rstrip().endswith("done.") and "n(n + 1)" in r[k - 1]
    # the $$ block is laid out over rows in Live preview too (#49): the $$ lines blank, a tall integral, a root
    rest = "\n".join(r[k + 2:])
    assert "⌠1" in rest and "⌡0" in rest and "√" in rest
    assert "One line: x ∈ ℝ end." in r


def test_caret_on_a_formula_line_shows_the_latex(editor, make_file):
    t = editor(make_file("m.md", "# t\n\nInline $E = mc^2$ here\n"))
    t.send(DOWN + DOWN)
    assert t.body(4)[2] == "Inline $E = mc^2$ here"


def test_a_formula_that_would_not_typeset_is_an_error_shift_f7_finds_and_f7_explains(editor, make_file):
    t = editor(make_file("bad.md", "# t\n\nGood $x^2$ here.\n\nThen $\\frac{a}$ is wrong.\n"))
    assert t.wait_for("TeX 1E", 5)
    t.send(SHIFT_F7, 0.8)
    assert "Error (LaTeX): Missing argument for \\frac" in t.status() and t.pos()[0] == 5
    t.send(F7, 1.0)
    assert t.has_dialog("Error") and "Missing argument for \\frac" in t.text()


def test_a_rule_of_writing_in_a_formula_is_a_warning_f7_puts_in_its_fix(editor, make_file):
    t = editor(make_file("rule.md", "# t\n\nThen $a <= b$ holds.\n"))
    assert t.wait_for("TeX 1W", 5)
    t.send(SHIFT_F7, 0.8)
    assert "Warning (LaTeX): <= is set as 2 separate characters" in t.status()
    t.send(F7, 1.0)
    assert t.has_dialog("LaTeX") and "<= is set as 2 separate characters" in t.text() and "\\le" in t.text()
    t.send("\r", 0.8)
    assert t.body()[2].startswith("Then $a \\le b$ holds.") and "TeX" not in t.status()


def test_format_shortcuts(editor, make_file):
    t = editor(make_file("f.md", "plain line\nsecond\n"))
    t.send(ctrl("b")); assert t.body()[0].startswith("**plain** line")
    t.send(ctrl("b")); assert t.body()[0].startswith("plain line")
    for expected in ("# plain", "## plain"):
        t.send(alt("h")); assert t.body()[0].startswith(expected)
    t.send(alt("h") * 5); assert t.body()[0].startswith("plain")
    t.send(alt("x"))                                                    # checkbox
    assert t.body()[0].startswith("☐") or t.body()[0].startswith("- [ ] plain")
    t.send(ESC)                                                         # drop the selection left by the bold toggle
    t.send(HOME); t.send(ctrl("k"))
    assert "[]()" in t.body()[0]


def test_outline_lists_headings_and_jumps(editor, make_file):
    t = editor(make_file("o.md", "# One\ntext\n```\n# not a heading\n```\n## Two\nbody\n"))
    t.send("\x0f", 1.0)                                                 # Ctrl+O
    box = " ".join(r for r in t.rows() if "┃" in r)
    assert "One" in box and "Two" in box and "not a heading" not in box
    t.send(DOWN); t.send("\r", 0.8)
    assert t.pos()[0] == 6


def _spell_works(t):
    """Spelling is on, and its first answers are on the screen: Hunspell (Linux) loads its dictionaries in the
    background, so its underlines come a moment after the text (every test here has a misspelling to wait for)."""
    if "SPELL" not in t.status():
        return False
    deadline = time.time() + 10
    while time.time() < deadline and not any(t.cell(y, x).underscore for y in range(2, 20) for x in range(80)):
        time.sleep(0.1)
    return True


def test_misspelled_words_are_underlined_and_f7_fixes_them(editor, make_file):
    t = editor(make_file("s.md", "# Title\nSome helo text here\n"))
    if not _spell_works(t):
        pytest.skip("Windows spell checking unavailable")
    t.send(DOWN, 0.6)
    row = 3                                                             # screen row of the text line
    marks = "".join("^" if t.cell(row, x).underscore else " " for x in range(40)).strip()
    assert marks == "^^^^"
    # caret onto "helo": Home, then Ctrl+Right twice
    t.send(HOME); t.send(CTRL_RIGHT)                                    # caret on "helo"
    t.send(F7, 1.0)
    assert t.has_dialog('Spelling: "helo"')
    # the suggestions depend on the dictionaries installed (en-GB offers "hello" first, en-US "Help"): take the first
    # (buttons, the first one focused: "[► hello ◄]")
    first = re.search(r"\[► (.+?) ◄\]", t.text()).group(1)
    assert first and first != "helo"
    t.send("\r", 0.8)
    assert f"Some {first} text here" in t.body()[1]


def _word_installed():
    if not harness.WINDOWS:
        return False
    import winreg
    try:
        winreg.OpenKey(winreg.HKEY_CLASSES_ROOT, r"Word.Application\CLSID").Close()
        return True
    except OSError:
        return False


def test_office_spelling_from_the_menu(editor, make_file):
    if not _word_installed():
        pytest.skip("Microsoft Word not installed")
    t = editor(make_file("s.md", "# Title\nSome helo text here\n"))
    t.send(alt("t"), 0.8); t.send("p", 1.0)                             # Tools > Spelling provider
    assert t.has_dialog("Spelling provider")
    t.send(DOWN, 0.3); t.send("\r", 1.0)                                # Microsoft Office
    assert "Spelling: Office" in t.status()
    t.send(DOWN, 0.3)                                                   # (moving clears the message)
    assert "SPELL Office" in t.status()
    deadline = time.time() + 20                                         # Word starts and answers in the background
    while time.time() < deadline and not t.cell(3, 5).underscore:
        time.sleep(0.2)
    assert "".join("^" if t.cell(3, x).underscore else " " for x in range(40)).strip() == "^^^^"


def _checkers_home():
    # where the editor looks for its checkers: %LOCALAPPDATA%\BlueEdit, or ~/.local/share/BlueEdit on Linux
    import os
    base = os.environ.get("LOCALAPPDATA") or os.environ.get("XDG_DATA_HOME") or os.path.expanduser("~/.local/share")
    return os.path.join(base, "BlueEdit")


def _exe(name):
    return name + ".exe" if harness.WINDOWS else name


def _languagetool_installed():
    import os
    home = _checkers_home()
    return os.path.exists(os.path.join(home, "jre", "bin", _exe("java"))) and \
        os.path.exists(os.path.join(home, "languagetool", "languagetool-server.jar"))


def test_grammar_with_languagetool_underlines_and_f7_fixes(editor, make_file):
    if not _languagetool_installed():
        pytest.skip("LanguageTool not installed in %LOCALAPPDATA%\\BlueEdit")
    import os
    os.makedirs(os.environ["BLUEEDIT_CONFIG_DIR"], exist_ok=True)
    with open(os.path.join(os.environ["BLUEEDIT_CONFIG_DIR"], "settings.json"), "w", encoding="utf-8") as f:
        f.write('{ "grammar": "languagetool" }')
    t = editor(make_file("g.md", "# Note\n\nHe go to school.\n\nМы был в магазине.\n"))
    assert "GRAMMAR" in t.status()
    deadline = time.time() + 60                                         # Java and LanguageTool start, then answer
    while time.time() < deadline and not (t.cell(4, 3).underscore and t.cell(6, 4).underscore):
        time.sleep(0.25)
    assert t.cell(4, 3).underscore and t.cell(4, 3).fg == "ffff55"     # "go", in the grammar colour
    assert t.cell(6, 4).underscore                                      # "был" (Russian)
    t.send(DOWN, 0.2); t.send(DOWN, 0.2); t.send(CTRL_RIGHT, 0.3)       # caret onto "go"
    t.send(F7, 1.5)
    assert t.has_dialog() and "goes" in t.text()
    t.send("\r", 1.0)                                                   # the first replacement
    assert t.body()[2].startswith("He goes to school.")
    t.send(DOWN, 0.2); t.send(DOWN, 0.2); t.send(HOME, 0.2); t.send(CTRL_RIGHT, 0.3)
    t.send(F7, 1.5)
    assert "спряжение" in t.text()                                      # LanguageTool explains in Russian


def _llm_installed():
    import os
    d = os.path.join(_checkers_home(), "llm")
    return os.path.exists(os.path.join(d, "bin", _exe("llama-server"))) and \
        os.path.exists(os.path.join(d, "models", "microsoft_Phi-4-mini-instruct-Q4_K_M.gguf"))


def test_grammar_with_a_local_language_model(editor, make_file):
    if not _llm_installed():
        pytest.skip("llama.cpp and Phi-4-mini not installed in %LOCALAPPDATA%\\BlueEdit\\llm")
    import os
    os.makedirs(os.environ["BLUEEDIT_CONFIG_DIR"], exist_ok=True)
    with open(os.path.join(os.environ["BLUEEDIT_CONFIG_DIR"], "settings.json"), "w", encoding="utf-8") as f:
        f.write('{ "grammar": ["llm"], "llmModel": "microsoft_Phi-4-mini-instruct-Q4_K_M.gguf" }')
    t = editor(make_file("g.md", "# Note\n\nМы был в магазине.\n"))
    assert "GRAMMAR LLM" in t.status()
    deadline = time.time() + 60
    while time.time() < deadline and not t.cell(4, 3).underscore:
        time.sleep(0.25)
    assert t.cell(4, 3).underscore and t.cell(4, 3).fg == "ffff55"     # "был" in the grammar colour
    t.send(DOWN, 0.2); t.send(DOWN, 0.2); t.send(CTRL_RIGHT, 0.3)       # caret onto "был"
    t.send(F7, 1.5)
    assert t.has_dialog() and "были" in t.text()
    t.send("\r", 1.0)
    assert t.body()[2].startswith("Мы были в магазине.")


def test_a_grammar_checker_that_cannot_start_is_named_with_the_reason(editor, make_file, tmp_path):
    import os
    config = os.environ["BLUEEDIT_CONFIG_DIR"]
    os.makedirs(config, exist_ok=True)
    empty = str(tmp_path / "nothing").replace("\\", "\\\\")
    with open(os.path.join(config, "settings.json"), "w", encoding="utf-8") as f:
        f.write('{ "grammar": ["languagetool", "edge"], "languageToolDir": "%s", "edgePath": "%s\\\\msedge.exe" }' % (empty, empty))
    t = editor(make_file("g.md", "text\n"), dims=(24, 400))               # wide: both reasons fit
    deadline = time.time() + 5
    while time.time() < deadline and "Grammar (" not in t.status():
        time.sleep(0.2)
    assert "Grammar (LanguageTool): LanguageTool not installed" in t.status()     # why, in the message line
    assert "Grammar (Edge): Microsoft Edge not found" in t.status()
    t.send(END, 0.4)                                                    # the message goes with the next move
    assert "GRAMMAR LT+EDGE failed" in t.status()


def test_ctrl_f7_suggests_a_better_wording_and_applies_the_ticked_changes(editor, make_file):
    if not _llm_installed():
        pytest.skip("llama.cpp and Phi-4-mini not installed in %LOCALAPPDATA%\\BlueEdit\\llm")
    import os
    os.makedirs(os.environ["BLUEEDIT_CONFIG_DIR"], exist_ok=True)
    with open(os.path.join(os.environ["BLUEEDIT_CONFIG_DIR"], "settings.json"), "w", encoding="utf-8") as f:
        f.write('{ "llmModel": "microsoft_Phi-4-mini-instruct-Q4_K_M.gguf" }')
    para = "Следует отметить, что в настоящее время имеет место тенденция к увеличению количества пользователей."
    t = editor(make_file("s.md", "# Note\n\n" + para + "\n"))
    t.send(DOWN, 0.2); t.send(DOWN, 0.2)
    t.send(ctrl_f7(), 0.5)
    deadline = time.time() + 90                                         # the model starts, then answers
    while time.time() < deadline and not t.has_dialog("Style (") and "nothing to improve" not in t.status():
        time.sleep(0.5)
    if not t.has_dialog("Style ("):
        pytest.fail("no suggestions dialog: " + t.status())
    assert "→" in t.text()
    t.send("\r", 1.0)                                                   # take all of them
    assert t.body()[2] != para and "Style:" in t.status() and "applied" in t.status()
    t.send(ctrl("z"), 0.5)
    assert t.body()[2] == para                                          # one undo step


def test_grammar_model_is_chosen_from_the_tools_menu(editor, make_file, tmp_path):
    import os
    models = tmp_path / "llm" / "models"
    models.mkdir(parents=True)
    for name in ("microsoft_Phi-4-mini-instruct-Q4_K_M.gguf", "google_gemma-3-12b-it-Q4_K_M.gguf"):
        (models / name).write_bytes(b"x")
    config = os.environ["BLUEEDIT_CONFIG_DIR"]
    os.makedirs(config, exist_ok=True)
    settings = os.path.join(config, "settings.json")
    with open(settings, "w", encoding="utf-8") as f:
        f.write('{\n    "grammar": "off",\n    "llmDir": "%s"\n}\n' % str(tmp_path / "llm").replace("\\", "\\\\"))
    t = editor(make_file("m.md", "text\n"))
    t.send(alt("t"), 0.4)
    t.send("e", 0.8)
    assert t.has_dialog("Grammar model") and "Auto: the best one installed (Gemma 3 12B)" in t.text()
    assert "Phi-4-mini" in t.text()
    t.send(DOWN, 0.2); t.send(DOWN, 0.2); t.send("\r", 0.8)            # the second model: Phi-4-mini
    assert '"llmModel": "microsoft_Phi-4-mini-instruct-Q4_K_M.gguf"' in open(settings, encoding="utf-8").read()
    assert "Grammar model: Phi-4-mini" in t.status()


def test_shift_f7_walks_through_problems_and_f7_repairs_a_mixed_layout_word(editor, make_file):
    t = editor(make_file("p.md", "# Title\nfirst speling\nmore text with пpивет here\nsee https://exampel.com ok\n"))
    if not _spell_works(t):
        pytest.skip("Windows spell checking unavailable")
    # the mixed-layout word is marked at once, "speling" only when the speller answers (Hunspell loads its dictionary
    # in the background): wait for it, or Shift+F7 goes to the mixed word first
    deadline = time.time() + 10
    while time.time() < deadline and not any(t.cell(3, x).underscore for x in range(80)):    # screen row of line 2
        time.sleep(0.1)
    t.send(SHIFT_F7, 0.8)
    assert f"Spelling ({SPELLER}): speling" in t.status() and t.pos() == (2, 14)
    t.send(SHIFT_F7, 0.8)
    assert "mixed" in t.status() and t.pos()[0] == 3
    t.send(F7, 1.0)
    assert t.has_dialog('Spelling: "пpивет"') and "Latin and Cyrillic letters mixed" in t.text()
    t.send("\r", 0.8)                                                   # the word in one alphabet
    assert "with привет here" in t.body()[2]
    t.send(SHIFT_F7, 0.8)
    assert f"Spelling ({SPELLER}): speling" in t.status()                            # round again: the address was not a problem


def test_f8_turns_spell_checking_off(editor, make_file):
    t = editor(make_file("s.txt", "helo\n"))
    if not _spell_works(t):
        pytest.skip("Windows spell checking unavailable")
    assert t.cell(2, 0).underscore
    t.send(F8, 0.8)
    assert not t.cell(2, 0).underscore


def test_hyphenated_words_are_not_flagged(editor, make_file):
    """Regression: "по-русски" was split at the hyphen and "русски" was underlined as a mistake."""
    # Russian and English: on Windows its languages, elsewhere the locale's ("auto" reads LANGUAGE)
    t = editor(make_file("h.md", "first\nПишем по-русски и well-known слова, но превет ошибка.\n"), env={"LANGUAGE": "ru_RU:en_GB"})
    if not _spell_works(t):
        pytest.skip("Windows spell checking unavailable")
    row = 3
    line = "".join(t.cell(row, x).data for x in range(60))
    marks = "".join("^" if t.cell(row, x).underscore else " " for x in range(60))
    flagged = [line[m.start():m.end()] for m in re.finditer(r"\^+", marks)]
    assert flagged == ["превет"], f"flagged {flagged} in {line!r}"


def test_a_wide_character_scrolled_down_the_screen_leaves_nothing_behind(editor, make_file):
    # "⚫" takes two columns: counted as one, each row the line passed kept its last letter (a column of "d")
    text = "".join(f"line {i}\n" for i in range(40)) + "### ⚫ Unknown — Not Yet Investigated\n" + "".join(f"tail {i}\n" for i in range(30))
    t = editor(make_file("w.md", text), dims=(20, 80))
    for _ in range(70):
        t.send(DOWN, 0.02)
    for _ in range(40):
        t.send(UP, 0.05)
    time.sleep(0.5)
    assert any("Unknown" in r for r in t.body(16))
    assert not [r for r in t.body(16) if r.rstrip().endswith(" d") or r.rstrip() == "d"], "\n".join(t.body(16))


@pytest.mark.parametrize("line", ["cafe\u0301 au lait, the end", "👍🏽 and 🇬🇧 and 👨‍👩‍👧, the end", "⚠️ warning, the end"])
def test_accents_and_emoji_sequences_scrolled_leave_nothing_behind(editor, make_file, line):
    # an accent written apart takes no column; emoji sequences are drawn so that no terminal joins them
    text = "".join(f"line {i}\n" for i in range(40)) + line + "\n" + "".join(f"tail {i}\n" for i in range(30))
    t = editor(make_file("w.txt", text), dims=(20, 80))
    for _ in range(70):
        t.send(DOWN, 0.02)
    for _ in range(40):
        t.send(UP, 0.05)
    time.sleep(0.5)
    rows = [r.rstrip() for r in t.body(16)]
    assert any("the end" in r for r in rows)
    assert not [r for r in rows if r.endswith(" d") or r.endswith(" t") or r.endswith(" e")], "\n".join(rows)
