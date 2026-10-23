"""The interface in another language (Ui.cs, #199): the other tests run in English (harness sets BLUEEDIT_UI_LANGUAGE).
In Russian the status line starts "Стр", not "Ln": the editor is ready when it shows."""
from harness import ESC, F10, alt, alt_ru, ctrl


def test_russian_menus_and_hints_and_a_menus_key_in_either_layout(editor, make_file):
    t = editor(make_file("a.txt", "text\n"), env={"BLUEEDIT_UI_LANGUAGE": "ru"}, ready="Стр ")
    assert t.wait_for("Файл", 5)
    screen = t.text().splitlines()
    assert "Пр" in screen[0] and "Помо" in screen[0]                   # the menu bar
    assert "Помощь" in screen[-1] and "Сохр" in screen[-1]             # the key hints
    t.send(alt("a"), 0.6)                                              # Ф is the key of A
    assert "Открыть..." in t.text() and "Сохранить все" in t.text()
    t.send(ESC, 0.4)
    t.send(alt_ru("a", "ф"), 0.6)                                      # the same key, the Russian layout on
    assert "Открыть..." in t.text()
    t.send(ESC, 0.4)
    t.send(alt("f"), 0.6)                                              # F is the key of А: Правка
    assert "Отменить" in t.text()


def test_russian_dialogs_and_an_options_letter_in_either_layout(editor, make_file):
    t = editor(make_file("a.txt", "Text text\n"), env={"BLUEEDIT_UI_LANGUAGE": "ru"}, ready="Стр ")
    assert t.wait_for("Файл", 5)
    t.send(ctrl("f"), 0.6)
    assert "Найти:" in t.text() and "[ ] Учитывать регистр" in t.text() and "Esc = отмена" in t.text()
    t.send(alt_ru("e", "у"), 0.4)                                      # У is the key of E: "_Учитывать регистр"
    assert "[x] Учитывать регистр" in t.text()
    t.send("Text\r", 0.6)
    t.send("x", 0.3)                                                   # an unsaved change, then quit
    t.send(F10, 0.6)
    assert t.wait_for("Сохранить изменения в a.txt?", 5)
    assert "Не сохранять" in t.text() and "Отмена" in t.text()


def test_russian_status_line_and_messages(editor, make_file):
    t = editor(make_file("a.txt", "one two\n"), env={"BLUEEDIT_UI_LANGUAGE": "ru"}, ready="Стр ")
    assert t.wait_for("Стр 1/", 5)
    assert "Кол 1" in t.text() and "ПОТОК" in t.text()
    t.send(ctrl("f"), 0.6)
    t.send("zzz\r", 0.8)
    assert t.wait_for('Не найдено: "zzz"', 5)
