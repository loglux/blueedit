import os
import re
import subprocess

import harness
from harness import CTRL_PGDN, CTRL_PGUP, DOWN, ESC, F2, F3, F6, SHIFT_F6, UP, alt, ctrl


def test_save_keeps_encoding_and_line_endings(editor, make_file, work):
    path = make_file("cp1251.txt", "Привет, мир\r\nвторая строка\r\n", encoding="cp1251")
    before = open(path, "rb").read()
    t = editor(path)
    assert "cp1251" in t.status() and "CRLF" in t.status()
    t.send("Z"); t.send(F2, 1.0)
    after = open(path, "rb").read()
    assert after == b"Z" + before


def test_save_keeps_utf8_bom(editor, make_file, work):
    path = work / "bom.txt"
    path.write_bytes(b"\xef\xbb\xbf" + "Привет\n".encode("utf-8"))
    t = editor(str(path))
    assert "BOM" in t.status()
    t.send("Z"); t.send(F2, 1.0)
    assert path.read_bytes() == b"\xef\xbb\xbfZ" + "Привет\n".encode("utf-8")


def test_window_title_names_the_file_and_its_changes(editor, make_file):
    t = editor(make_file("notes.md", "text\n"))
    assert t.screen.title == "notes.md - BlueEdit"
    t.send("Z")
    assert t.screen.title == "notes.md * - BlueEdit"


def test_clean_quit_exits_without_asking(editor, make_file):
    t = editor(make_file("a.txt", "x\n"))
    t.send(ctrl("q"), 0.5)
    assert t.wait_exit(5)


def test_unsaved_changes_prompt_and_save_default(editor, make_file):
    path = make_file("a.txt", "x\n")
    t = editor(path)
    t.send("Q")
    t.send(ctrl("q"), 1.0)
    assert t.has_dialog("Unsaved changes")
    assert "Save changes to a.txt?" in t.text()
    t.send("\r", 1.0)                                         # Save is the default button
    assert t.wait_exit(5)
    assert open(path, encoding="utf-8").read() == "Qx\n"


def test_cancel_at_unsaved_prompt_keeps_editing(editor, make_file):
    t = editor(make_file("a.txt", "x\n"))
    t.send("Q"); t.send(ctrl("q"), 1.0)
    t.send(ESC, 1.0)
    assert t.alive() and not t.has_dialog()


def test_two_documents_switching_keeps_edits_and_undo_per_document(editor, make_file):
    a, b = make_file("a.txt", "AAA\n"), make_file("b.txt", "BBB\n")
    t = editor(a, b)
    assert "1:a.txt" in t.tabs() and "2:b.txt" in t.tabs()
    t.send(F6); t.send("X")                    # the last file is active at start; F6 wraps to the first
    t.send(SHIFT_F6); t.send("Y")
    assert "1:a.txt*" in t.tabs() and "2:b.txt*" in t.tabs()
    t.send(CTRL_PGUP); assert t.body()[0].startswith("XAAA")
    t.send(ctrl("z")); assert t.body()[0].startswith("AAA")
    t.send(CTRL_PGDN); assert t.body()[0].startswith("YBBB")


def test_close_document_asks_when_modified(editor, make_file):
    t = editor(make_file("a.txt", "x\n"), make_file("b.txt", "y\n"))
    t.send(F6); t.send("Q")                    # first document, modified
    t.send(ctrl("w"), 1.0)
    assert t.has_dialog("Unsaved changes")
    t.send(ESC, 0.8)
    assert "1:a.txt*" in t.tabs()


def test_file_picker_opens_navigates_and_closes(editor, make_file, work):
    (work / "sub").mkdir()
    (work / "sub" / "inner.txt").write_text("inner\n")
    t = editor(make_file("a.txt", "x\n"), cwd=str(work))
    t.send(F3, 1.5)
    assert t.has_dialog("Open")
    assert "sub" in t.text() and "a.txt" in t.text()
    t.send(ESC, 0.8)
    assert not t.has_dialog()


def test_file_picker_up_one_folder(editor, make_file, work):
    t = editor(make_file("a.txt", "x\n"))
    t.send(F3, 1.5)
    before = [r for r in t.rows() if "┃ " in r][0]
    t.send("\r", 1.0)                                          # first entry is ".."
    after = [r for r in t.rows() if "┃ " in r][0]
    assert before != after
    t.send(ESC, 0.5)


def test_open_at_line_and_read_only_options(editor, make_file):
    path = make_file("a.txt", "1\n2\n3\n4\n5\n")
    t = editor("+4", path)
    assert t.pos()[0] == 4
    t.kill()
    t = editor("-r", path)
    assert " RO" in t.status()
    t.send("zzz")
    assert t.body()[0].startswith("1") and "*" not in t.tabs()
    t.send(F2, 0.8)
    assert "Read-only" in t.status()


def test_unquoted_name_with_a_space_is_rejoined(editor, make_file, work):
    make_file("note one.md", "# x\n")
    t = editor("note", "one.md", cwd=str(work))
    assert "1:note one.md" in t.tabs() and "2:" not in t.tabs()


def test_missing_file_opens_empty_and_save_creates_it(editor, work):
    path = work / "brand new.txt"
    t = editor(str(path))
    t.send("hello from test"); t.send(F2, 1.0)
    assert path.read_text() == "hello from test"


def _settings(text):
    os.makedirs(os.environ["BLUEEDIT_CONFIG_DIR"], exist_ok=True)
    with open(os.path.join(os.environ["BLUEEDIT_CONFIG_DIR"], "settings.json"), "w", encoding="utf-8") as f:
        f.write(text)


def test_esc_does_not_close_when_not_started_by_far(editor, make_file):
    """Regression guard: "escapeQuits": "auto" leaves Esc as it was unless Far opened the editor."""
    t = editor(make_file("a.txt", "x\n"))
    t.send(ESC, 0.5); t.send(ESC, 0.5); t.send(ESC, 0.8)
    assert t.alive() and t.body()[0].startswith("x")


def test_esc_quits_when_turned_on(editor, make_file):
    _settings('{ "escapeQuits": true }')
    t = editor(make_file("a.txt", "x\n"))
    t.send(ESC, 1.0)
    assert t.wait_exit(5)


def test_esc_asks_before_dropping_changes(editor, make_file):
    _settings('{ "escapeQuits": true }')
    t = editor(make_file("a.txt", "x\n"))
    t.send("Q", 0.3); t.send(ESC, 1.0)
    assert t.has_dialog("Unsaved changes")
    t.send(ESC, 0.8)                                        # Cancel: keep editing
    assert t.alive() and t.body()[0].startswith("Qx")


def test_esc_closes_one_document_at_a_time(editor, make_file):
    _settings('{ "escapeQuits": true }')
    t = editor(make_file("a.txt", "AAA\n"), make_file("b.txt", "BBB\n"))
    t.send(ESC, 1.0)
    assert t.alive() and "2:" not in t.tabs()
    t.send(ESC, 1.0)
    assert t.wait_exit(5)


def test_alt_digit_switches_documents(editor, make_file):
    """Regression: Alt+1..9 arrived as Alt+'№' / Alt+'-' with the 'windows' driver."""
    t = editor(make_file("a.txt", "AAA\n"), make_file("b.txt", "BBB\n"))
    t.send(alt("1")); assert t.body()[0].startswith("AAA")
    t.send(alt("2")); assert t.body()[0].startswith("BBB")


# ---------- Save as: folders, a new folder, replace, a missing folder (#92) ----------

def _save_as(t, wait=1.0):
    from harness import SHIFT_F2
    t.send(SHIFT_F2, wait)
    assert t.has_dialog("Save as") and "File name:" in t.text()


def _clear_name(t, n=40):
    from harness import BACKSPACE
    for _ in range(n):
        t.send(BACKSPACE, 0.01)


def test_save_as_shows_folders_and_saves_into_a_new_one(editor, make_file, work):
    from harness import F7
    t = editor(make_file("a.txt", "x\n"))
    t.send("Q")
    _save_as(t)
    assert "a.txt" in t.text()                                # the folder's files, and the name in the field
    t.send(F7, 0.5)
    assert "New folder:" in t.text()
    t.send("notes\r", 0.8)                                    # made and gone into
    assert os.path.isdir(work / "notes") and "notes" in t.text()
    t.send("\r", 1.0)                                         # the name field still says a.txt
    assert not t.has_dialog()
    assert (work / "notes" / "a.txt").read_text(encoding="utf-8") == "Qx\n"
    assert (work / "a.txt").read_text(encoding="utf-8") == "x\n"


def test_save_as_asks_before_replacing_a_file(editor, make_file, work):
    t = editor(make_file("a.txt", "x\n"))
    make_file("b.txt", "keep\n")
    t.send("Q")
    _save_as(t)
    _clear_name(t)
    t.send("b.txt\r", 1.0)
    assert t.has_dialog("Save as") and "already exists" in t.text()
    t.send(ESC, 0.8)                                          # no: back in the Save as dialog
    assert t.has_dialog("Save as") and "File name:" in t.text()
    t.send("\r", 1.0)                                         # the same name again
    assert "already exists" in t.text()
    t.send("\r", 1.0)                                         # Replace
    assert not t.has_dialog()
    assert (work / "b.txt").read_text(encoding="utf-8") == "Qx\n"


def test_save_as_offers_to_make_a_typed_missing_folder(editor, make_file, work):
    t = editor(make_file("a.txt", "x\n"))
    _save_as(t)
    _clear_name(t)
    t.send(str(work / "deep" / "er" / "c.txt") + "\r", 1.0)
    assert "Make it?" in t.text()
    t.send("\r", 1.0)
    assert not t.has_dialog()
    assert (work / "deep" / "er" / "c.txt").read_text(encoding="utf-8") == "x\n"


def test_file_dialogs_list_the_drives(editor, make_file):
    from harness import ALT_F1
    t = editor(make_file("a.txt", "x\n"))
    _save_as(t)
    t.send(ALT_F1, 0.6)
    assert "Drives" in t.text()
    t.send(ESC, 0.5)
    assert not t.has_dialog()


def test_file_info_counts_words(editor, make_file):
    t = editor(make_file("i.md", "# Title\n\nThe **value** of $x$ is here.\n"))
    t.send(alt("f"), 0.6); t.send("i", 1.0)                   # File > Info
    assert t.has_dialog("Info")
    text = t.text()
    assert "Words of the text" in text and "i.md" in text and "Formulas" in text
    t.send(ESC, 0.5)
    assert not t.has_dialog()


def test_version_and_help_answer_in_the_shell_and_exit(work):
    # a check after unpacking a release: the answer is printed, the terminal is not taken over, nothing is opened
    v = subprocess.run([harness.EDITOR, "--version"], capture_output=True, text=True, timeout=30, cwd=work)
    assert v.returncode == 0 and re.fullmatch(r"BlueEdit \d+\.\d+\.\d+.*\n", v.stdout), v.stdout
    assert "\x1b" not in v.stdout
    h = subprocess.run([harness.EDITOR, "--help"], capture_output=True, text=True, timeout=30, cwd=work)
    assert h.returncode == 0 and "Usage: be " in h.stdout and "--read-only" in h.stdout
    assert not os.listdir(work)
