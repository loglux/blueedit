"""Starts /usr/bin/be in a pty, turns View > Formula pictures off and on, and says whether the editor found the
formula pictures module: through the /usr/bin/be link it must look beside /usr/lib/blueedit/be. Run by test.sh in a
container; the status line is redrawn in pieces, so only a short phrase of each message is looked for."""
import fcntl
import os
import pty
import re
import select
import struct
import termios
import time

pid, fd = pty.fork()
if pid == 0:
    os.environ["TERM"] = "xterm-256color"
    os.environ["BLUEEDIT_WELCOME"] = "0"
    os.execv("/usr/bin/be", ["be", "/tmp/a.md"])

out = b""


def read(seconds):
    global out
    end = time.time() + seconds
    while time.time() < end:
        if select.select([fd], [], [], 0.1)[0]:
            try:
                out += os.read(fd, 65536)
            except OSError:
                return


os.set_blocking(fd, False)
fcntl.ioctl(fd, termios.TIOCSWINSZ, struct.pack("HHHH", 30, 100, 0, 0))
read(4)
for keys in (b"\x1bv", b"f", b"\x1bv", b"f"):                   # View > Formula pictures: off, then on
    os.write(fd, keys)
    read(1.5)
text = re.sub(rb"\x1b\[[0-9;?]*[A-Za-z]", b" ", out).decode("utf-8", "replace")
print("module:", "found" if "shows Sixel" in text else "NOT FOUND" if "mathimages module" in text else "no answer")
os.write(fd, b"\x11")                                           # Ctrl+Q
read(1)
