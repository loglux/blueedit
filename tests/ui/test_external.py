"""Working next to other programs (git, builds, coding assistants): files changed on disk, file:line, save and quit."""
import os
import stat
import time

from harness import F2, SHIFT_F10, alt, ctrl


def external(path, text):
    """Another program rewrites the file; its time is moved on so the change is visible at once."""
    with open(path, "w", encoding="utf-8", newline="") as f:
        f.write(text)
    t = time.time() + 5
    os.utime(path, (t, t))


def wait_until(check, timeout=5.0):
    t0 = time.time()
    while time.time() - t0 < timeout:
        if check():
            return True
        time.sleep(0.1)
    return False


def test_unchanged_buffer_follows_the_file_and_undo_brings_the_old_text_back(editor, make_file):
    path = make_file("a.txt", "old text\n")
    t = editor(path)
    external(path, "new text from another program\n")
    assert wait_until(lambda: t.body()[0].startswith("new text from another program"))
    assert "Reloaded" in t.status() and "*" not in t.tabs()
    t.send(ctrl("z"), 0.6)
    assert t.body()[0].startswith("old text")


def test_unsaved_edits_ask_and_keep_mine_wins(editor, make_file):
    path = make_file("a.txt", "base\n")
    t = editor(path)
    t.send("MINE ", 0.4)
    external(path, "theirs\n")
    assert wait_until(lambda: t.has_dialog("Changed on disk"))
    assert "unsaved edits" in t.text()
    t.send("k", 1.0)                                            # Keep mine
    assert t.body()[0].startswith("MINE base") and "Kept your version" in t.status()
    t.send(F2, 1.0)                                             # no second question: the user already chose
    assert not t.has_dialog()
    assert wait_until(lambda: open(path, encoding="utf-8").read() == "MINE base\n")


def test_unsaved_edits_reload_and_undo_returns_them(editor, make_file):
    path = make_file("a.txt", "base\n")
    t = editor(path)
    t.send("MINE ", 0.4)
    external(path, "theirs\n")
    assert wait_until(lambda: t.has_dialog("Changed on disk"))
    t.send("r", 1.0)                                            # Reload
    assert t.body()[0].startswith("theirs")
    t.send(ctrl("z"), 0.6)
    assert t.body()[0].startswith("MINE base")


def test_file_line_col_on_the_command_line(editor, make_file):
    path = make_file("a.txt", "1\n2\nthird line\n4\n")
    t = editor(path + ":3:5")
    assert t.pos() == (3, 5) and "1:a.txt" in t.tabs()


def test_save_all_and_quit(editor, make_file):
    a, b = make_file("a.txt", "a\n"), make_file("b.txt", "b\n")
    t = editor(a, b)
    t.send("B", 0.3)                                            # b.txt is active at start
    t.send(alt("1"), 0.4); t.send("A", 0.3)
    t.send(SHIFT_F10, 1.0)
    assert t.wait_exit(5)
    assert open(a, encoding="utf-8").read() == "Aa\n" and open(b, encoding="utf-8").read() == "Bb\n"


def test_ctrl_s_saves(editor, make_file):
    path = make_file("a.txt", "x\n")
    t = editor(path)
    t.send("S"); t.send(ctrl("s"), 1.0)
    assert open(path, encoding="utf-8").read() == "Sx\n" and "*" not in t.tabs()


def test_a_file_that_cannot_be_written_says_so_and_the_editor_stays(editor, make_file):
    # Before, the exception from the write ended the editor and every unsaved document with it.
    if hasattr(os, "geteuid") and os.geteuid() == 0:
        return                                                  # root writes read-only files anyway
    path = make_file("ro.txt", "old\n")
    t = editor(path)
    os.chmod(path, stat.S_IREAD)
    try:
        t.send("NEW ", 0.4)
        t.send(F2)
        assert wait_until(lambda: t.has_dialog("Not saved"))
        t.send("\r", 0.6)
        assert not t.has_dialog() and t.body()[0].startswith("NEW old") and "*" in t.tabs()
        assert open(path, encoding="utf-8").read() == "old\n"
    finally:
        os.chmod(path, stat.S_IREAD | stat.S_IWRITE)


def test_unsaved_text_of_a_killed_editor_is_offered_at_the_next_start(editor, make_file):
    config = os.environ["BLUEEDIT_CONFIG_DIR"]
    os.makedirs(config, exist_ok=True)
    with open(os.path.join(config, "settings.json"), "w", encoding="utf-8") as f:
        f.write('{ "autoRecoverSeconds": 1 }')
    path = make_file("lost.txt", "saved line\n")
    t = editor(path)
    t.send("UNSAVED ", 0.4)
    recovery = os.environ["BLUEEDIT_RECOVERY_DIR"]
    # the copy itself, not its temporary file (killed before it took its place, the copy was not there: CI, PR #214)
    assert wait_until(lambda: os.path.isdir(recovery) and any(n.startswith("session-") for n in os.listdir(recovery)),
                      5.0), "no copy was written"
    t.kill()                                                    # ends with no chance to save or clean up
    # a copy of a BlueEdit still running is never offered: wait until the killed one is gone (on the CI runner it
    # outlived the kill long enough for the next start to pass its copy over)
    assert wait_until(lambda: not t.p.isalive(), 10.0), "the killed editor is still running"
    t2 = editor()
    assert wait_until(lambda: t2.has_dialog("Recover")), f"not offered; copies: {os.listdir(recovery)}\n{t2.text()}"
    assert "lost.txt" in t2.text()
    t2.send("\r", 1.0)                                          # Recover
    assert t2.body()[0].startswith("UNSAVED saved line") and "*" in t2.tabs()
    assert open(path, encoding="utf-8").read() == "saved line\n"  # the file itself was never touched
    t2.send(F2, 1.0)
    assert open(path, encoding="utf-8").read() == "UNSAVED saved line\n"
