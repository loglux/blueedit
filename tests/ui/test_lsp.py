"""Language servers in the real editor: errors underlined, Shift+F7 and F7, the status line."""
import os
import re
import shutil
import time

import pytest

from harness import ALT_F1, F7, F12, SHIFT_F7, SHIFT_F12, ctrl


def _pyright():
    return shutil.which("pyright-langserver") is not None


def test_pyright_error_is_underlined_found_and_explained(editor, make_file):
    if not _pyright():
        pytest.skip("pyright not installed (npm install -g pyright)")
    t = editor(make_file("a.py", "x = 1\ny = undefined_name\n"))
    deadline = time.time() + 60
    while time.time() < deadline and not t.cell(3, 4).underscore:      # (screen row, column): "u" of undefined_name
        time.sleep(0.5)
    assert t.cell(3, 4).underscore and not t.cell(3, 2).underscore
    assert "LSP pyright-langserver" in t.status() and "1E" in t.status()
    t.send(SHIFT_F7, 0.8)
    assert "Error (Pyright)" in t.status() and "undefined_name" in t.status()
    t.send(F7, 0.8)
    assert t.has_dialog() and "is not defined" in t.text()


def test_after_a_dot_the_servers_members_are_listed(editor, make_file):
    if not _pyright():
        pytest.skip("pyright not installed (npm install -g pyright)")
    t = editor(make_file("m.py", "import os\n"))
    from harness import DOWN
    t.send(DOWN, 0.3)
    t.send("os.", 0.3)
    # the module's members with their kinds on the right ("cls", "const", "var"...). Which come first depends on the
    # system: on Linux os has POSIX constants (CLD_CONTINUED... const) that sort before Windows' first (DirEntry cls)
    def listed():
        return any(re.search(r"^\s+\w+\s+(cls|const|var|fn|fld|mod|m)$", r) for r in t.rows())
    deadline = time.time() + 30                                     # the server starts, then answers
    while time.time() < deadline and not listed():
        time.sleep(0.5)
    assert listed(), "no list of members:\n" + t.text()
    t.send("pa", 0.5)
    assert any("path " in r and "var" in r for r in t.rows())       # narrowed: "path" (a variable of os)
    t.send("t", 0.5)                                                # "pat": path first (pardir was)
    t.send("\t", 0.6)
    assert t.body()[1].startswith("os.path")


def test_f12_goes_to_a_definition_in_another_file_and_shift_f12_comes_back(editor, make_file):
    if not _pyright():
        pytest.skip("pyright not installed (npm install -g pyright)")
    from harness import ESC, END
    make_file("pyrightconfig.json", "{}")
    make_file("lib.py", "def greet(name: str, times: int = 1) -> str:\n    \"\"\"Says hello.\"\"\"\n    return name\n")
    t = editor(make_file("main.py", "from lib import greet\ngreet\n"))
    from harness import DOWN
    t.send(DOWN, 0.3)                                               # on "greet" of line 2
    deadline = time.time() + 30                                     # the server starts first
    while time.time() < deadline and ("LSP pyright-langserver" not in t.status() or "starting" in t.status()):
        time.sleep(0.5)
    t.send(ALT_F1, 2.0)
    assert t.has_dialog() and "Says hello." in t.text()
    t.send(ESC, 0.5)
    t.send(F12, 2.0)
    assert "lib.py" in t.tabs() and t.pos()[0] == 1 and "Definition: lib.py:1" in t.status()
    t.send(SHIFT_F12, 1.0)
    assert t.pos()[0] == 2 and "main.py" in t.status()
    t.send(END, 0.2)
    t.send("(", 2.0)                                                # the parameters, the first in brackets
    assert "[name: str]" in t.status()



def test_ctrl_f12_formats_python_with_ruff_or_black_or_says_what_to_install(editor, make_file):
    from harness import CTRL_F12
    t = editor(make_file("f.py", "x=1\n"))
    t.send(CTRL_F12, 0.3)
    found = shutil.which("ruff") or shutil.which("black")
    deadline = time.time() + 20                                         # the program runs in the background
    while time.time() < deadline and found and "Formatted" not in t.status():
        time.sleep(0.3)
    if found:
        assert t.body()[0] == "x = 1" and "lines changed: 1" in t.status(), (t.status(), t.body()[:2])
    else:                                                               # pyright has no formatter: what to install
        assert "Code tools installs ruff" in t.status() and t.body()[0] == "x=1", t.status()


def test_ctrl_f2_renames_in_this_file_and_opens_the_other_with_the_change(editor, make_file):
    if not _pyright():
        pytest.skip("pyright not installed (npm install -g pyright)")
    from harness import CTRL_F2, DOWN
    make_file("pyrightconfig.json", "{}")
    make_file("lib.py", "def greet(name):\n    return name\n")
    t = editor(make_file("main.py", "from lib import greet\ngreet(\"a\")\n"))
    t.send(DOWN, 0.3)                                               # on "greet" of line 2
    deadline = time.time() + 60
    while time.time() < deadline and ("LSP pyright-langserver" not in t.status() or "starting" in t.status()):
        time.sleep(0.5)
    from harness import ESC
    t.send(ALT_F1, 0.5)                                             # an answer about greet: the server has read the files
    deadline = time.time() + 30
    while time.time() < deadline and not t.has_dialog():
        time.sleep(0.3)
    t.send(ESC, 0.5)
    t.send(CTRL_F2, 0.5)
    deadline = time.time() + 15                                     # the server is asked first (prepareRename)
    while time.time() < deadline and not t.has_dialog():
        time.sleep(0.3)
    assert t.has_dialog() and "Rename greet to:" in t.text(), t.status()
    t.send("hello", 0.3)                                            # typing replaces the old name
    t.send("\r", 0.3)
    deadline = time.time() + 20
    while time.time() < deadline and "hello:" not in t.status():
        time.sleep(0.3)
    assert "greet → hello: 3 changes in 2 files, 1 opened" in t.status(), t.status()
    assert t.body()[:2] == ["from lib import hello", "hello(\"a\")"]
    assert "lib.py" in t.tabs() and "main.py" in t.status()           # lib.py opened, main.py still in front


def test_alt_f12_lists_the_references_and_enter_goes_to_one(editor, make_file):
    if not _pyright():
        pytest.skip("pyright not installed (npm install -g pyright)")
    from harness import ALT_F12, DOWN, UP, ESC
    make_file("pyrightconfig.json", "{}")
    make_file("lib.py", "def greet(name):\n    return name\n")
    t = editor(make_file("main.py", "from lib import greet\ngreet(\"a\")\n"))
    t.send(DOWN, 0.3)                                               # on "greet" of line 2
    deadline = time.time() + 60
    while time.time() < deadline and ("LSP pyright-langserver" not in t.status() or "starting" in t.status()):
        time.sleep(0.5)
    t.send(ALT_F1, 0.5)                                             # an answer about greet: the server has read the files
    deadline = time.time() + 30
    while time.time() < deadline and not t.has_dialog():
        time.sleep(0.3)
    t.send(ESC, 0.5)
    t.send(ALT_F12, 0.5)
    deadline = time.time() + 20
    while time.time() < deadline and "references to greet" not in t.text():
        time.sleep(0.3)
    assert "3 references to greet in 2 files" in t.text(), t.status()
    assert "def greet(name):" in t.text() and "from lib import greet" in t.text()
    t.send(UP, 0.2); t.send(UP, 0.2)                                # from the call (the caret's) to lib.py's definition
    t.send("\r", 1.0)
    assert "lib.py" in t.status() and t.pos()[0] == 1, t.status()


def test_ctrl_o_in_code_lists_classes_and_functions_and_goes_to_one(editor, make_file):
    if not _pyright():
        pytest.skip("pyright not installed (npm install -g pyright)")
    from harness import DOWN
    t = editor(make_file("o.py", "class Greeter:\n    def greet(self, name):\n        word = name\n        return word\n\n\ndef main():\n    pass\n"))
    deadline = time.time() + 60
    while time.time() < deadline and ("LSP pyright-langserver" not in t.status() or "starting" in t.status()):
        time.sleep(0.5)
    t.send(ctrl("o"), 0.5)
    deadline = time.time() + 15
    while time.time() < deadline and not t.has_dialog():
        time.sleep(0.3)
    assert "Outline: 3 symbols" in t.text(), t.status()
    assert any("greet" in r and "method" in r for r in t.rows()) and not any("word" in r and "variable" in r for r in t.rows())
    t.send(DOWN, 0.2); t.send(DOWN, 0.2)                             # Greeter, greet, main
    t.send("\r", 0.6)
    assert t.pos()[0] == 7, t.status()


def test_f7_on_a_code_error_offers_the_servers_fixes_and_takes_one(editor, make_file):
    if shutil.which("csharp-ls") is None and not os.path.exists(os.path.expanduser("~/.dotnet/tools/csharp-ls.exe")) \
            and not os.path.exists(os.path.expanduser("~/.dotnet/tools/csharp-ls")):
        pytest.skip("csharp-ls not installed (dotnet tool install -g csharp-ls)")
    make_file("t.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework>"
                          "<OutputType>Library</OutputType></PropertyGroup></Project>")
    t = editor(make_file("C.cs", "class C { StringBuilder b = new(); }\n"))
    deadline = time.time() + 180                                    # csharp-ls loads the project first
    while time.time() < deadline and "1E" not in t.status():
        time.sleep(1)
    assert "1E" in t.status(), t.status()
    t.send(SHIFT_F7, 0.8)                                           # to the error
    t.send(F7, 0.5)
    deadline = time.time() + 15
    while time.time() < deadline and not t.has_dialog():
        time.sleep(0.3)
    assert "using System.Text;" in t.text() and "[ The whole message ]" in t.text(), t.status()
    t.send("\r", 0.5)                                               # the preferred fix is the first
    deadline = time.time() + 15
    while time.time() < deadline and not t.body()[0].startswith("using System.Text;"):
        time.sleep(0.3)
    assert t.body()[0].startswith("using System.Text;"), (t.status(), t.body()[:3])
    assert "1 change in 1 file" in t.status()


def _typescript_server():
    """TypeScript 7's tsc where Tools > Code tools puts it (its language server: tsc --lsp)."""
    root = os.environ.get("BLUEEDIT_TOOLS") or (
        os.path.join(os.environ.get("LOCALAPPDATA", ""), "BlueEdit", "tools") if os.name == "nt"
        else os.path.join(os.path.expanduser("~"), ".local", "share", "blueedit", "tools"))
    return os.path.isfile(os.path.join(root, "typescript", "package", "lib", "tsc.exe" if os.name == "nt" else "tsc"))


def test_typescript_error_is_underlined_by_its_own_server(editor, make_file):
    if not _typescript_server():
        pytest.skip("TypeScript not installed (Tools > Code tools)")
    t = editor(make_file("a.ts", "const n: number = 1;\nconst s: string = n;\n"))
    deadline = time.time() + 60                                     # the server starts, then answers
    while time.time() < deadline and not t.cell(3, 6).underscore:   # "s" of "const s" on screen row 3
        time.sleep(0.5)
    assert t.cell(3, 6).underscore and not t.cell(2, 6).underscore, t.status()
    assert "LSP tsc" in t.status() and "1E" in t.status()
    t.send(SHIFT_F7, 0.8)
    assert "not assignable" in t.status(), t.status()
