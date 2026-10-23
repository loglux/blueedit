"""A new user's first minutes with the Linux release, in a clean container: the release tar.gz unpacked, ./be started
in a terminal, the welcome, the menu, F1, typing, spelling dictionaries, Harper (and with --languagetool LanguageTool)
set up from the editor's own lists. The container gets nothing installed; the terminal is a pty here (pyte, through
tests/ui/harness.py), so each step's screen is saved as text: OUT/IMAGE/NN-name.txt. Started by run.sh; see docs/ci.md.

    python walk.py RELEASE.tar.gz OUT IMAGE [IMAGE...] [--languagetool] [--lang=de_DE.UTF-8]

--lang gives the system a language (LANG): the editor should pick its dictionary and check in it by itself.
"""
from __future__ import annotations

import os
import re
import subprocess
import sys
import time
from pathlib import Path

os.environ.setdefault("BLUEEDIT_UI_PATIENCE", "2")          # read when harness is imported; a shared server
sys.path.insert(0, str(Path(__file__).resolve().parent))
from harness import DOWN, END, ESC, F1, F7, F9, RIGHT, UP, Term  # noqa: E402 - copied next to this file by run.sh

ENTER = "\r"

# a mistake for each checker in the system's language: without --lang these systems name none, so English (en-US):
# "Helo" spelling, "a apple" Harper, "this are" LanguageTool. Harper knows English only: in the others it marks nothing.
TEXTS = {
    "en": "Helo world, this are a test. I ate a apple.",
    "de": "Das ist ein Tesst. Ich habe ein Fehler gemacht.",       # "Tesst" spelling, "ein Fehler" LanguageTool
    "ru": "Превет мир. Мы был в магазине.",                          # "Превет" spelling, "Мы был" LanguageTool
}


def walk(release: Path, out: Path, image: str, languagetool: bool, lang: str) -> None:
    name = "be-newcomer-" + re.sub(r"[^a-z0-9]+", "-", image)
    folder = out / re.sub(r"[^a-z0-9.]+", "-", image)
    folder.mkdir(parents=True, exist_ok=True)
    log = open(folder / "log.txt", "w", encoding="utf-8")
    n = 0

    def say(line: str) -> None:
        print(f"[{image}] {line}", flush=True)
        log.write(line + "\n"); log.flush()

    def shot(step: str) -> None:
        nonlocal n
        n += 1
        lines = []
        for y, row in enumerate(t.rows()):
            lines.append(row)
            # a mark (spelling, grammar) is an underline, which text loses: a row of ~ under the underlined letters
            marks = "".join("~" if t.screen.buffer[y][x].underscore else " " for x in range(len(row))).rstrip()
            if marks:
                lines.append(marks)
        (folder / f"{n:02}-{step}.txt").write_text("\n".join(lines) + "\n", encoding="utf-8")
        say(f"shot {n:02}-{step}")

    def keys(k: str, step: str, wait: float = 1.5) -> None:
        t.send(k, wait)
        shot(step)

    # a target is a Docker image, or a machine as ssh:USER@HOST:PORT (a virtual machine of a minimal server, vm.sh),
    # where the newcomer is that user: its home is cleaned before and after
    ssh = None
    if image.startswith("ssh:"):
        login, port = image[4:].rsplit(":", 1)
        ssh = ["ssh", "-q", "-i", os.environ.get("BE_VM_KEY", "key"), "-p", port, "-o", "StrictHostKeyChecking=no",
               "-o", "UserKnownHostsFile=/dev/null", "-o", "BatchMode=yes", login]

    def inside(cmd: str) -> str:
        argv = ssh + [cmd] if ssh else ["docker", "exec", name, "sh", "-c", cmd]
        r = subprocess.run(argv, capture_output=True, text=True)
        return (r.stdout + r.stderr).strip()

    clean = "rm -rf ~/Downloads/blueedit-* ~/.local/share/BlueEdit ~/.config/blueedit"

    def installed(done, rounds: int, step: str) -> None:
        # an install runs in the background; one that fails says so in a window ("...: not installed"), closed after
        for _ in range(rounds):
            if done() or "not installed" in t.text():
                break
            time.sleep(5)
        shot(step)
        if "not installed" in t.text():
            keys(ESC, step + "-closed")

    # what a person does: unpack the download, go into it, start the editor
    start = f"cd ~/Downloads && tar xzf {release.name} && cd blueedit-*-linux-x64 && {'LANG=' + lang + ' ' if lang else ''}exec ./be"
    text = TEXTS[lang[:2] if lang else "en"]
    if ssh:
        inside(clean + " && mkdir -p ~/Downloads")
        subprocess.run(["scp", "-q", "-i", ssh[3], "-P", port, "-o", "StrictHostKeyChecking=no", "-o", "UserKnownHostsFile=/dev/null",
                        "-o", "BatchMode=yes", str(release), f"{login}:Downloads/"], check=True)
        t = Term(ssh[:1] + ["-tt"] + ssh[1:] + [start], dims=(30, 120))
    else:
        subprocess.run(["docker", "rm", "-f", name], capture_output=True)
        t = Term(["docker", "run", "--rm", "-it", "--name", name, "-e", "TERM=xterm-256color",
                  "-v", f"{release.resolve()}:/root/Downloads/{release.name}:ro", image, "sh", "-c", start],
                 dims=(30, 120))
    try:
        if not t.wait_for("Welcome", 120):
            shot("no-welcome")
            say("no welcome within 120 s")
            return
        t.settle()
        shot("welcome")
        say("the system: " + inside(". /etc/os-release; echo $PRETTY_NAME; ldd --version 2>&1 | head -1"))
        keys(ESC, "after-welcome")
        keys(F9, "menu")
        keys(ESC + ESC, "menu-closed", 0.5)
        keys(F1, "help")
        keys(ESC, "back")
        say("LANG: " + (lang or "(none)"))
        keys(text + ENTER, "typed", 3)
        keys(UP + F7, "f7")
        keys(ESC, "after-f7")
        keys(F9 + RIGHT * 5, "tools-menu")
        keys("d", "dictionaries")
        keys(ENTER, "dictionaries-chosen", 2)
        installed(lambda: "downloaded" in t.text(), 12, "dictionaries-after")
        say("hunspell folder: " + inside("ls ~/.local/share/BlueEdit/hunspell 2>&1"))
        keys(F9 + RIGHT * 5, "tools-menu-2")
        keys("u", "set-up-grammar")
        keys(DOWN * 5 + ENTER, "harper-chosen", 2)
        installed(lambda: "GRAMMAR" in t.status(), 24, "harper-after")
        say("harper folder: " + inside("ls -la ~/.local/share/BlueEdit/harper 2>&1"))
        keys(UP + END, "harper-shown", 6)                 # End: no typing, an edit would clear the marks
        if languagetool:
            keys(F9 + RIGHT * 5, "tools-menu-3")
            keys("u", "set-up-grammar-2")
            keys(DOWN * 4 + ENTER, "languagetool-chosen", 2)
            installed(lambda: "GRAMMAR LT" in t.status(), 60, "languagetool-after")
            keys(END, "languagetool-shown", 15)
        say("still running: " + str(t.alive()))
        say("BlueEdit's folders: " + inside("du -sh ~/.local/share/BlueEdit/* ~/.config/blueedit 2>&1"))
    finally:
        t.kill()
        if ssh:
            inside("pkill -x be; pkill -f harper-ls; " + clean)        # its server ends with it, but not on a killed ssh
        else:
            subprocess.run(["docker", "rm", "-f", name], capture_output=True)
        log.close()


def main() -> None:
    args = [a for a in sys.argv[1:] if not a.startswith("--")]
    release, out, images = Path(args[0]), Path(args[1]), args[2:]
    lang = next((a.split("=", 1)[1] for a in sys.argv if a.startswith("--lang=")), "")
    for image in images:
        walk(release, out, image, "--languagetool" in sys.argv, lang)
    print("done", flush=True)


if __name__ == "__main__":
    main()
