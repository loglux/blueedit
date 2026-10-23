import os
import sys
from pathlib import Path

import pytest

sys.path.insert(0, os.path.dirname(__file__))
import harness  # noqa: E402


# Tests that must not share the computer with others: they measure time, or start the real checkers (Word,
# LanguageTool, a language model, language servers), which load the CPU and the GPU. check.ps1 runs the rest in
# parallel first (pytest-xdist), then these one by one: `-m "not serial" -n N`, then `-m serial`.
SERIAL = {
    "test_performance.py": None,                                   # the whole file
    "test_lsp.py": None,
    "test_tui_layer.py": {"test_input_latency_is_a_few_milliseconds"},
    "test_completion.py": {"test_keywords_of_the_language_are_offered"},      # may start csharp-ls
    "test_markdown.py": {
        "test_office_spelling_from_the_menu",
        "test_grammar_with_languagetool_underlines_and_f7_fixes",
        "test_grammar_with_a_local_language_model",
        "test_ctrl_f7_suggests_a_better_wording_and_applies_the_ticked_changes",
    },
}


# Tests of what only the Windows build has: the Windows Terminal profile, the Windows clipboard (read through
# PowerShell). Skipped elsewhere, with the reason in the output.
WINDOWS_ONLY = {
    "test_settings.py": {
        "test_in_windows_terminal_the_profile_is_added_and_follows_the_theme",
        "test_outside_windows_terminal_or_in_hint_mode_nothing_is_written",
        "test_terminal_font_dialog_installs_and_removes",
    },
    "test_editing.py": {"test_system_clipboard_paste_and_copy"},
}


# Tests that hold the editor to milliseconds. BLUEEDIT_NO_TIMING=1 skips them where the computer is shared with other
# work and its timings say nothing about the editor: the Linux CI runner shares its server with other services.
TIMED = {
    "test_performance.py": {
        "test_typing_in_a_large_markdown_file_is_fast_inside_the_editor",
        "test_held_arrow_key_scrolls_without_falling_behind_in_markdown",
        "test_held_arrow_key_scrolls_without_falling_behind_in_code",
        "test_key_to_glyph_latency_of_the_editor",
    },
    "test_tui_layer.py": {"test_input_latency_is_a_few_milliseconds"},
}


def pytest_configure(config):
    config.addinivalue_line("markers", "serial: run alone, not in parallel with other tests (see SERIAL)")


def pytest_collection_modifyitems(config, items):
    for item in items:
        names = SERIAL.get(item.path.name, set())
        if names is None or item.originalname in names:
            item.add_marker(pytest.mark.serial)
        if not harness.WINDOWS and item.originalname in WINDOWS_ONLY.get(item.path.name, set()):
            item.add_marker(pytest.mark.skip(reason="Windows only"))
        if os.environ.get("BLUEEDIT_NO_TIMING") and item.originalname in TIMED.get(item.path.name, set()):
            item.add_marker(pytest.mark.skip(reason="timings off here (BLUEEDIT_NO_TIMING)"))


@pytest.fixture(scope="session", autouse=True)
def _editor_is_built():
    if not Path(harness.EDITOR).exists():
        pytest.exit(f"{harness.EDITOR} not found - run `dotnet publish` first (see check.ps1)", returncode=2)


@pytest.fixture(autouse=True)
def _isolated_settings(tmp_path, monkeypatch):
    """Every test gets its own empty settings folder: the user's settings never leak in, and are never touched."""
    monkeypatch.setenv("BLUEEDIT_CONFIG_DIR", str(tmp_path / "config"))
    monkeypatch.setenv("BLUEEDIT_WT_FRAGMENTS", str(tmp_path / "wt-fragments"))
    monkeypatch.setenv("BLUEEDIT_RECOVERY_DIR", str(tmp_path / "recovery"))   # copies of unsaved text (Recovery.cs)
    monkeypatch.delenv("WT_SESSION", raising=False)          # tests that need Windows Terminal set it themselves
    monkeypatch.setenv("BLUEEDIT_MATHIMAGES", "off")         # formula pictures (a module installed here) are not drawn
    monkeypatch.setenv("BLUEEDIT_WELCOME", "0")              # no first-start welcome (test_settings.py shows it)


@pytest.fixture
def work(tmp_path):
    return tmp_path


@pytest.fixture
def make_file(work):
    def _make(name: str, text: str, encoding: str = "utf-8") -> str:
        p = work / name
        p.write_bytes(text.encode(encoding))
        return str(p)
    return _make


@pytest.fixture
def editor():
    """start_editor(...) that guarantees the process is killed at the end of the test."""
    started = []

    def _start(*args, **kw):
        t = harness.start_editor(*args, **kw)
        started.append(t)
        return t

    yield _start
    for t in started:
        t.kill()
