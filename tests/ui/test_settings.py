"""Settings file and themes, end to end. conftest gives every test its own empty BLUEEDIT_CONFIG_DIR."""
import os
import re
import time

import pytest

from harness import DOWN, END, ESC, F2, F6, F8, RIGHT, UP, WINDOWS, alt, ctrl


def settings_path():
    return os.path.join(os.environ["BLUEEDIT_CONFIG_DIR"], "settings.json")


def write_settings(text):
    os.makedirs(os.environ["BLUEEDIT_CONFIG_DIR"], exist_ok=True)
    with open(settings_path(), "w", encoding="utf-8") as f:
        f.write(text)


def test_default_theme_is_dos_blue_and_no_file_is_created(editor, make_file):
    t = editor(make_file("a.txt", "hello\n"))
    assert t.cell(2, 0).bg in ("0000aa", "blue")
    t.send(ctrl("q"), 0.5)
    assert not os.path.exists(settings_path())


def test_theme_from_the_menu_applies_at_once_and_is_remembered(editor, make_file):
    f = make_file("a.txt", "hello\n")
    t = editor(f)
    t.send(alt("v"), 0.8); t.send("k", 1.0)                      # View > Theme: dark
    assert t.cell(2, 0).bg == "1e1e1e"
    assert t.cell(0, 0).bg == "3c3c3c"                           # the menu bar follows the theme too
    assert "Theme: dark" in t.status()
    assert '"theme": "dark"' in open(settings_path(), encoding="utf-8").read()
    t.kill()
    t = editor(f)
    assert t.cell(2, 0).bg == "1e1e1e"


def test_light_theme(editor, make_file):
    t = editor(make_file("a.txt", "hello\n"))
    t.send(alt("v"), 0.8); t.send("h", 1.0)                      # View > Theme: light
    assert t.cell(2, 0).bg == "ffffff"


def test_settings_file_opens_in_the_editor_and_saving_applies_it(editor, make_file):
    t = editor(make_file("a.txt", "x\n"))
    t.send(alt("t"), 0.8); t.send("g", 1.0)                      # Tools > Settings...
    t.send(END, 0.3); t.send("\r", 1.2)                          # its last row: settings.json
    assert "settings.json" in t.tabs()
    assert "tabSize" in t.text()
    t.send(ctrl("r"), 0.8); t.send('"tabSize": 4', 0.4); t.send("\t", 0.3)
    t.send('"tabSize": 2', 0.4); t.send("\r", 1.0); t.send("a", 1.0)
    t.send(F2, 1.0)
    assert "Settings applied" in t.status()
    t.send(F6, 0.6)                                               # back to a.txt
    t.send("\t", 0.5)
    assert t.body()[0].startswith("  x")


def test_options_from_the_file_are_used_at_start(editor, make_file):
    write_settings('// mine\n{ "tabSize": 2, "markdownView": "source", "wrapProse": false, }\n')
    t = editor(make_file("n.md", "# Title\n**bold**\n"))
    assert "SRC" in t.status() and "WRAP" not in t.status()
    assert t.body()[1].startswith("**bold**")                     # source view: markup visible
    t.send("\t", 0.5)
    assert t.body()[0].startswith("  # Title")


def test_the_clock_is_at_the_right_of_the_menu_bar_and_view_clock_goes_round_and_is_kept(editor, make_file):
    t = editor(make_file("c.txt", "x\n"))
    bar = t.rows()[0]
    assert re.search(r"\b\d\d:\d\d$", bar), bar                  # on by default: the time
    assert t.cols_n - len(bar) == 1                               # one blank column after it, as before the menus
    t.send(alt("v"), 0.5); t.send("c", 1.0)                       # View > Clock: the date and the time
    assert re.search(r"\S+ \d\d:\d\d$", t.rows()[0]) and '"clock": "date"' in open(settings_path(), encoding="utf-8").read()
    t.send(alt("v"), 0.5); t.send("c", 1.0)                       # off
    assert not re.search(r"\d\d:\d\d$", t.rows()[0])
    assert '"clock": "off"' in open(settings_path(), encoding="utf-8").read()


def test_tools_shows_what_its_windows_hold_without_opening_them(editor, make_file):
    write_settings('''{ "grammar": ["languagetool"], "spellProvider": "hunspell", "ignoredRules": ["A", "B", "C"],
                        "llmModel": "qwen3-14b-q4.gguf", "styleFocus": ["wordiness"] }''')
    t = editor(make_file("a.txt", "x\n"))
    t.send(alt("t"), 0.8)
    row = {name: next(r for r in t.rows() if name in r) for name in
           ("Grammar checking", "Grammar model", "Ignored rules", "Style options", "Spelling provider")}
    assert "√" in row["Grammar checking"] and "LanguageTool" in row["Grammar checking"]
    assert "Qwen3 14B" in row["Grammar model"]
    assert re.search(r"\b3\b", row["Ignored rules"])
    assert re.search(r"\b1/\d+\b", row["Style options"])
    assert "Hunspell" in row["Spelling provider"]


def test_grammar_off_has_no_mark_or_name_in_tools(editor, make_file):
    t = editor(make_file("a.txt", "x\n"))
    t.send(alt("t"), 0.8)
    row = next(r for r in t.rows() if "Grammar checking" in r)
    assert "√" not in row and "LanguageTool" not in row


def test_the_window_menu_lists_the_open_files_and_goes_to_one(editor, make_file):
    t = editor(make_file("first_one.txt", "first\n"), make_file("b.txt", "second\n"))
    t.send(alt("w"), 0.8)
    rows = "\n".join(t.rows())
    assert "1 first_one.txt" in rows and "2 b.txt" in rows      # the underscore in a name stays
    t.send("1", 0.8)
    assert t.body()[0].startswith("first")
    t.send(alt("w"), 0.8)
    assert "•1 first_one.txt" in "\n".join(t.rows())            # the mark on the current one
    t.send("2", 0.8)
    assert t.body()[0].startswith("second")


def test_a_broken_setting_is_reported_and_the_rest_still_applies(editor, make_file):
    write_settings('{ "tabSize": 99, "theme": "dark" }')
    t = editor(make_file("a.txt", "x\n"))
    assert "Settings:" in t.status() and "tabSize" in t.status()
    assert t.cell(2, 0).bg == "1e1e1e"


def test_unreadable_json_does_not_stop_the_editor(editor, make_file):
    write_settings('{ "theme": ')
    t = editor(make_file("a.txt", "x\n"))
    assert "Settings:" in t.status()
    t.send("ok", 0.4)
    assert t.body()[0].startswith("okx")


def fragment_path():
    return os.path.join(os.environ["BLUEEDIT_WT_FRAGMENTS"], "blueedit.json")


def test_in_windows_terminal_the_profile_is_added_and_follows_the_theme(editor, make_file):
    t = editor(make_file("a.txt", "x\n"), env={"WT_SESSION": "test"})
    assert "profile" in t.status() and "added" in t.status()
    text = open(fragment_path(), encoding="utf-8").read()
    assert '"Cascadia Mono"' in text and "#0000AA" in text
    t.send(alt("v"), 0.8); t.send("k", 1.0)                      # Theme: dark rewrites the scheme
    assert "#1E1E1E" in open(fragment_path(), encoding="utf-8").read()


def test_outside_windows_terminal_or_in_hint_mode_nothing_is_written(editor, make_file):
    t = editor(make_file("a.txt", "x\n"))
    t.kill()
    assert not os.path.exists(fragment_path())
    write_settings('{ "fontSetup": "hint" }')
    t = editor(make_file("b.txt", "x\n"), env={"WT_SESSION": "test"})
    assert not os.path.exists(fragment_path())
    t.send(alt("t"), 0.8); t.send("f", 1.2)                      # Tools > Terminal font: the hint
    assert t.has_dialog("Terminal font") and "Recommended: Cascadia Mono, size 12" in t.text()


def test_terminal_font_dialog_installs_and_removes(editor, make_file):
    t = editor(make_file("a.txt", "x\n"))
    t.send(alt("t"), 0.8); t.send("f", 1.2)
    assert t.has_dialog("Terminal font") and "not installed" in t.text()
    t.send("i", 1.0)                                              # Install/update
    assert os.path.exists(fragment_path()) and "restart Windows Terminal" in t.status()
    t.send(alt("t"), 0.8); t.send("f", 1.2)
    assert ": installed" in t.text()
    t.send("r", 1.0)                                              # Remove
    assert not os.path.exists(fragment_path())


def test_spelling_languages_menu_picks_a_language_and_back_to_auto(editor, make_file):
    t = editor(make_file("a.txt", "x\n"))
    assert "SPELL" in t.status()
    t.send(alt("t"), 0.8); t.send("a", 1.0)                      # Tools > Spelling languages
    assert t.has_dialog("Spelling languages") and "[x] Auto: " + ("as in Windows" if os.name == "nt" else "the system's languages") in t.text()
    t.send(DOWN, 0.3); t.send(" ", 0.3); t.send("\r", 1.2)       # just the first language
    text = open(settings_path(), encoding="utf-8").read()
    assert '"spellLanguages": ["' in text
    spell = t.status().split("SPELL ")[1].split()[0]
    assert "/" not in spell                                       # one language now
    t.send(alt("t"), 0.8); t.send("a", 1.0)
    t.send(" ", 0.3); t.send("\r", 1.2)                          # Auto again
    assert '"spellLanguages": "auto"' in open(settings_path(), encoding="utf-8").read()


def test_colour_override(editor, make_file):
    write_settings('{ "colors": { "background": "#102030" } }')
    t = editor(make_file("a.txt", "x\n"))
    assert t.cell(2, 0).bg == "102030"


def test_grammar_checking_is_one_at_a_time_unless_several_are_allowed(editor, make_file):
    from harness import END, ESC, UP
    t = editor(make_file("a.md", "x\n"))
    t.send(alt("t"), 0.8); t.send("m", 1.0)                      # Tools > Grammar checking
    assert t.has_dialog("Grammar checking")
    t.send(" ", 0.3)                                              # LanguageTool on
    t.send(DOWN, 0.2); t.send(" ", 0.3)                           # the model on: LanguageTool off
    text = t.text()
    assert "( ) LanguageTool" in text and "(•) Language model" in text  # one of them: round marks
    t.send(END, 0.2); t.send(" ", 0.3)                            # Several at once
    t.send(UP, 0.2); t.send(" ", 0.3)                             # Chrome on, the model stays
    text = t.text()
    assert "[x] Language model" in text and "[x] Chrome" in text and "[x] Several at once" in text
    t.send(ESC, 0.5)                                              # nothing saved, no checker started
    assert not os.path.exists(settings_path()) or "grammarTogether" not in open(settings_path(), encoding="utf-8").read()


def test_esc_goes_back_from_the_settings_to_the_text(editor, make_file):
    t = editor(make_file("a.txt", "x\n"))
    t.send(alt("t"), 0.8); t.send("g", 1.0)                      # Tools > Settings...
    t.send(END, 0.3); t.send("\r", 1.2)                          # its last row: settings.json
    assert "settings.json" in t.tabs() and "Esc goes back" in t.status()
    t.send(ESC, 1.0)
    assert "settings.json" not in t.tabs() and t.body()[0].startswith("x")
    t.send(ESC, 0.8)                                              # in the text Esc does not close (not started by Far)
    assert t.alive() and "a.txt" in t.tabs()


def test_settings_window_changes_a_setting_in_the_file_and_applies_it(editor, make_file):
    t = editor(make_file("a.txt", "hello\n"))
    t.send(alt("t"), 0.8); t.send("g", 1.0)                      # Tools > Settings...
    assert "Colour scheme" in t.text()                            # its last row, settings.json: the test above
    t.send(RIGHT, 0.4)                                            # the first row: dos-blue -> dark
    assert "dark" in t.text()
    t.send("\r", 1.2)
    assert t.cell(2, 0).bg == "1e1e1e" and "Settings applied" in t.status()
    text = open(settings_path(), encoding="utf-8").read()
    assert '"theme": "dark"' in text and "// " in text              # written into the file, its comments kept


def test_keys_in_any_layout_is_in_the_settings_window(editor, make_file):
    # A trial setting is still one the user finds in the list, not only in the file (the user, 18 October 2026)
    t = editor(make_file("a.txt", "hello\n"))
    t.send(alt("t"), 0.8); t.send("g", 1.0)
    t.send(DOWN * 30, 0.6); t.send(UP, 0.4)                      # the last row before settings.json
    assert "Keys in any layout" in t.text() and "whatever the keyboard layout" in t.text(), t.text()
    t.send(RIGHT, 0.4); t.send("\r", 1.2)                        # on (the default) -> off
    assert '"keyEvents": false' in open(settings_path(), encoding="utf-8").read()


def test_esc_in_the_settings_window_changes_nothing(editor, make_file):
    t = editor(make_file("a.txt", "hello\n"))
    t.send(alt("t"), 0.8); t.send("g", 1.0)
    t.send(RIGHT, 0.3); t.send(ESC, 0.8)
    assert t.cell(2, 0).bg in ("0000aa", "blue") and not os.path.exists(settings_path())


def test_code_tools_lists_each_tool_where_it_is_found_or_what_it_needs(editor, make_file, tmp_path, monkeypatch):
    monkeypatch.setenv("BLUEEDIT_TOOLS", str(tmp_path / "tools"))     # an empty tools folder: nothing installed there
    t = editor(make_file("a.txt", "x\n"))
    t.send(alt("t"), 0.8); t.send("l", 1.0)                         # Tools > Code tools
    assert t.has_dialog() and "Code tools" in t.text(), t.text()
    for name in ("pyright", "ruff", "csharp-ls"):
        row = next((r for r in t.rows() if name in r), "")
        assert "found: " in row or "not installed, " in row, row      # where it is, or what installing it takes
    assert "Install the missing" in t.text() and "Remove what was installed here" in t.text()   # buttons under the table
    t.send(ESC, 0.5)
    assert not t.has_dialog()


def test_get_dictionaries_lists_the_languages(editor, make_file):
    # Tools > Get spelling dictionaries: LibreOffice's, with their sizes; Esc downloads nothing
    t = editor(make_file("a.txt", "hello\n"))
    t.send(alt("t"), 0.8); t.send("d", 1.5)
    text = t.text()
    assert "Spelling dictionaries" in text, text
    for tag in ("ru-RU", "en-GB", "uk-UA", "de-DE"):
        assert tag in text, text
    t.send(ESC, 0.6)
    assert "Spelling dictionaries" not in t.text()


def test_set_up_grammar_offers_the_ways_and_sets_a_model_server(editor, make_file):
    # A new user gets grammar checking working from one window (Tools > Set up grammar checking). The address offered
    # is one where nothing listens (Ollama's own may be running on this computer), so the check says so every time
    os.makedirs(os.environ["BLUEEDIT_CONFIG_DIR"], exist_ok=True)
    with open(settings_path(), "w", encoding="utf-8") as f:
        f.write('{ "llmServer": "http://127.0.0.1:9/v1" }\n')
    t = editor(make_file("a.txt", "hello\n"))
    t.send(alt("t"), 0.8); t.send("u", 1.5)
    text = t.text()
    for way in ("A language model here", "Ollama", "LM Studio", "Cloud service", "Another model server", "LanguageTool", "Harper"):
        assert way in text, text
    t.send(DOWN * 4, 0.3); t.send("\r", 0.8)                      # another model server
    t.send("\r", 0.6)                                             # the address offered (the last one)
    t.send("my-model", 0.4); t.send("\r", 0.6)
    t.send("sk-test-secret", 0.4); t.send("\r", 1.0)               # a key: to a file of its own
    # checked first: nothing listens there, which is said, and kept anyway (the first button)
    for _ in range(50):
        if "does not answer" in t.text():
            break
        time.sleep(0.2)
    assert "does not answer" in t.text() and "Keep it as it is" in t.text(), t.text()
    t.send("\r", 1.0)
    # the last writes may come later on a busy computer (the CI runner once read the file before them)
    for _ in range(25):
        s = open(settings_path(), encoding="utf-8").read()
        if '"llmModel": "server"' in s and '"grammar": ["llm"]' in s:
            break
        time.sleep(0.2)
    assert '"llmServer": "http://127.0.0.1:9/v1"' in s and '"llmServerModel": "my-model"' in s, s
    assert '"llmModel": "server"' in s and '"grammar": ["llm"]' in s, s
    assert '"llmApiKey": "file:llm-key"' in s and "sk-test-secret" not in s, s
    key_file = os.path.join(os.path.dirname(settings_path()), "llm-key")
    key = open(key_file, "rb").read()
    if WINDOWS:
        assert b"sk-test-secret" not in key, key                   # encrypted (DPAPI)
    else:
        assert key == b"sk-test-secret" and os.stat(key_file).st_mode & 0o777 == 0o600, key


def test_f8_and_alt_z_keep_their_setting_so_the_menu_and_settings_agree(editor, make_file):
    # F8 turned spelling off while Tools > Settings still said "on"; now the key writes the setting, as Alt+G does,
    # and the View menu's mark follows
    t = editor(make_file("a.md", "# Title\n\nSome text.\n"))
    t.send(F8, 0.6)
    t.send(alt("z"), 0.6)
    for _ in range(25):
        s = open(settings_path(), encoding="utf-8").read() if os.path.exists(settings_path()) else ""
        if '"spellCheck": false' in s and '"wrapProse": false' in s:
            break
        time.sleep(0.2)
    assert '"spellCheck": false' in s and '"wrapProse": false' in s, s
    t.send(alt("v"), 0.8)
    assert "┃ Soft wrap" in t.text() and "┃√Soft wrap" not in t.text(), t.text()
    t.send(ESC, 0.4)


def test_set_up_grammar_offers_the_cloud_services_by_name(editor, make_file):
    # Claude or OpenAI is chosen from a list, with no address to type: the key is asked for that service, where it is
    # got said; Esc there leaves the settings as they were (nothing is sent anywhere in this test)
    t = editor(make_file("a.txt", "hello\n"))
    t.send(alt("t"), 0.8); t.send("u", 1.5)
    t.send(DOWN * 3, 0.3); t.send("\r", 0.8)                      # cloud service
    text = t.text()
    for service in ("Anthropic Claude", "OpenAI", "Google Gemini", "DeepSeek"):
        assert service in text, text
    t.send("\r", 0.8)                                             # Anthropic Claude
    assert "console.anthropic.com" in t.text(), t.text()
    t.send(ESC, 0.6)
    assert "console.anthropic.com" not in t.text(), t.text()
    assert not os.path.exists(settings_path()) or "anthropic" not in open(settings_path(), encoding="utf-8").read()


class FakeOllama:
    """An OpenAI-compatible /v1/models on a free port, as Ollama answers it: two models and an embedding model."""
    def __init__(self):
        import http.server, json, threading
        body = json.dumps({"object": "list", "data": [{"id": m, "object": "model"} for m in
                           ("gemma3:4b", "nomic-embed-text:latest", "qwen3:8b")]}).encode()

        class Handler(http.server.BaseHTTPRequestHandler):
            def do_GET(self):
                ok = self.path == "/v1/models"
                self.send_response(200 if ok else 404)
                self.send_header("Content-Type", "application/json")
                self.end_headers()
                if ok:
                    self.wfile.write(body)

            def log_message(self, *args):
                pass

        self.server = http.server.ThreadingHTTPServer(("127.0.0.1", 0), Handler)
        self.port = self.server.server_address[1]
        threading.Thread(target=self.server.serve_forever, daemon=True).start()

    def close(self):
        self.server.shutdown()


def test_ollama_lists_its_models_and_one_is_chosen(editor, make_file, monkeypatch):
    # Ollama at OLLAMA_HOST (Ollama's own variable): its models listed, embedding models left out; nothing to type
    fake = FakeOllama()
    try:
        monkeypatch.setenv("OLLAMA_HOST", f"127.0.0.1:{fake.port}")
        t = editor(make_file("a.txt", "hello\n"))
        t.send(alt("t"), 0.8); t.send("u", 1.5)
        t.send(DOWN, 0.3); t.send("\r", 1.5)                      # Ollama
        text = t.text()
        assert "gemma3:4b" in text and "qwen3:8b" in text and "nomic-embed" not in text, text
        t.send(DOWN, 0.3); t.send("\r", 1.0)                      # qwen3:8b
        s = open(settings_path(), encoding="utf-8").read()
        assert f'"llmServer": "http://127.0.0.1:{fake.port}/v1"' in s and '"llmServerModel": "qwen3:8b"' in s, s
        assert '"llmModel": "server"' in s and '"grammar": ["llm"]' in s, s
    finally:
        fake.close()


def test_lm_studio_not_running_says_what_to_do(editor, make_file):
    # nothing at LM Studio's address (port 1234 unused here): the steps to get it going, and nothing changed
    t = editor(make_file("a.txt", "hello\n"))
    t.send(alt("t"), 0.8); t.send("u", 1.5)
    t.send(DOWN * 2, 0.3); t.send("\r", 4.0)                      # LM Studio: a refused connection takes ~2 s on Windows
    text = t.text()
    assert "LM Studio does not answer at 127.0.0.1:1234" in text and "lmstudio.ai" in text, text
    t.send(ESC, 0.5)
    assert not os.path.exists(settings_path()) or '"llmServer"' not in open(settings_path(), encoding="utf-8").read()


def test_remove_lists_what_is_installed_and_esc_removes_nothing(editor, make_file):
    # Set up grammar checking > Remove: this computer's own parts are listed (the runner's too), so only Esc here;
    # the listing and deleting are unit-tested on folders of their own (ProofingToolsTests)
    t = editor(make_file("a.txt", "hello\n"))
    t.send(alt("t"), 0.8); t.send("u", 1.5)
    assert "Remove what was installed here..." in t.text(), t.text()
    t.send("\t", 0.3); t.send("\r", 1.5)                          # Tab to the button under the list
    text = t.text()
    assert t.has_dialog("Remove what was installed here") or "Nothing is installed here" in text, text
    t.send(ESC, 0.6)
    assert not t.has_dialog()


def test_the_first_start_welcomes_once_and_its_language_button_sets_the_interface(editor, make_file, tmp_path):
    # no settings.json yet: the welcome; its third button lists the interface's languages, the one picked is written
    # to the settings (BLUEEDIT_UI_LANGUAGE keeps this screen English); Esc begins; the next start has no welcome
    f = make_file("a.txt", "hello\n")
    t = editor(f, env={"BLUEEDIT_WELCOME": "1"})
    assert t.wait_for("Welcome to BlueEdit", 5), t.text()
    assert "F1 lists every key" in t.text() and "Grammar..." in t.text(), t.text()
    t.send(RIGHT, 0.2); t.send(RIGHT, 0.2); t.send("\r", 1.0)
    assert "Interface language" in t.text() and "English" in t.text() and "Русский" in t.text(), t.text()
    t.send(DOWN, 0.2); t.send("\r", 1.0)
    settings = (tmp_path / "config" / "settings.json").read_text(encoding="utf-8")
    assert re.search(r'"uiLanguage":\s*"ru"', settings), settings
    assert "Welcome to BlueEdit" in t.text(), t.text()                # shown again, in the language chosen
    t.send(ESC, 0.6)
    assert "Welcome to BlueEdit" not in t.text(), t.text()
    t.kill()
    t = editor(f, env={"BLUEEDIT_WELCOME": "1"})
    t.settle()
    assert "Welcome to BlueEdit" not in t.text(), t.text()
