"""Encodings end to end: detection on open, File > Reopen with encoding, File > Convert encoding, line endings."""
import time

from harness import DOWN, ESC, F2, UP, alt


def menu(t, letter):
    t.send(alt("f"), 0.8); t.send(letter, 1.2)


def test_dos_file_opens_as_cp866(editor, make_file):
    t = editor(make_file("dos.txt", "╔══╗\r\n║Привет║\r\n", encoding="cp866"))
    assert "cp866" in t.status()
    assert t.body()[1].startswith("║Привет║")


def test_reopen_with_another_encoding_and_back(editor, make_file):
    path = make_file("dos.txt", "Привет из DOS\r\n", encoding="cp866")
    before = open(path, "rb").read()
    t = editor(path)
    menu(t, "e")
    assert t.has_dialog("Reopen with encoding")
    t.send(UP, 0.3); t.send("\r", 1.0)                         # one above cp866: cp1251
    assert "cp1251" in t.status() and not t.body()[0].startswith("Привет")
    menu(t, "e")
    t.send(DOWN, 0.3); t.send("\r", 1.0)                       # back to cp866
    assert "cp866" in t.status() and t.body()[0].startswith("Привет из DOS")
    t.send(F2, 1.0)
    assert open(path, "rb").read() == before


def test_reopen_asks_before_dropping_changes(editor, make_file):
    t = editor(make_file("a.txt", "x\n"))
    t.send("Q")
    menu(t, "e")
    assert t.has_dialog("Reopen") and "unsaved" in t.text()
    t.send(ESC, 0.8)
    assert not t.has_dialog() and t.body()[0].startswith("Qx")


def test_convert_utf8_to_cp866(editor, make_file):
    path = make_file("a.txt", "Привет\n")
    t = editor(path)
    menu(t, "v")
    assert t.has_dialog("Save in encoding")
    for _ in range(5):
        t.send(DOWN, 0.2)                                      # UTF-8 > BOM > UTF-16 LE > BE > cp1251 > cp866
    t.send("\r", 1.0)
    assert "cp866" in t.status() and "*" in t.tabs()
    t.send(F2, 1.0)
    assert open(path, "rb").read() == "Привет\n".encode("cp866")


def test_convert_warns_about_characters_that_would_be_lost(editor, make_file):
    path = make_file("a.txt", "ok ✓\n")
    t = editor(path)
    menu(t, "v")
    for _ in range(4):
        t.send(DOWN, 0.2)                                      # cp1251 has no ✓
    t.send("\r", 1.0)
    assert t.has_dialog("Convert") and "cannot be stored in cp1251" in t.text()
    t.send(ESC, 0.8)
    assert "UTF-8" in t.status() and "*" not in t.tabs()


def test_line_endings_switch(editor, make_file, work):
    path = work / "a.txt"
    path.write_bytes(b"a\r\nb\r\n")
    t = editor(str(path))
    assert "CRLF" in t.status()
    menu(t, "d")
    assert "LF" in t.status() and "CRLF" not in t.status()
    t.send(F2, 0.3)
    for _ in range(50):                                         # a loaded machine can take a while to save
        if path.read_bytes() == b"a\nb\n":
            break
        time.sleep(0.1)
    assert path.read_bytes() == b"a\nb\n"
