"""Packs the Linux build into blueedit-<version>-linux-x64.tar.gz: the editor and the setup scripts, with the execute
bit set on the editor and the .sh scripts. Run on Windows by the release workflow, where neither Compress-Archive nor
Windows' tar can set Unix permissions - without them "./be" and "./install.sh" would not run after unpacking.

    python tools/ci/pack-linux.py <publish folder with the be binary> <out.tar.gz>
    python tools/ci/pack-linux.py --module <published module folder> <out.tar.gz>    (mathimages/ inside)
"""
import sys, tarfile, pathlib

root = pathlib.Path(__file__).resolve().parents[2]


def add(tar: tarfile.TarFile, path: pathlib.Path, name: str, executable: bool) -> None:
    info = tar.gettarinfo(str(path), arcname=f"{top}/{name}")
    info.mode = 0o755 if executable else 0o644
    info.uid = info.gid = 0
    info.uname = info.gname = ""
    with open(path, "rb") as f:
        tar.addfile(info, f)


if sys.argv[1] == "--module":
    # the formula pictures module: its folder as mathimages/, to unpack next to the editor
    folder, out = pathlib.Path(sys.argv[2]), pathlib.Path(sys.argv[3])
    with tarfile.open(out, "w:gz") as tar:
        for f in sorted(folder.iterdir()):
            info = tar.gettarinfo(str(f), arcname=f"mathimages/{f.name}")
            info.mode, info.uid, info.gid, info.uname, info.gname = 0o644, 0, 0, "", ""
            with open(f, "rb") as data:
                tar.addfile(info, data)
    print(out)
    sys.exit(0)

publish, out = pathlib.Path(sys.argv[1]), pathlib.Path(sys.argv[2])
top = out.name.removesuffix(".tar.gz")                    # blueedit-0.1.4-linux-x64/ inside the archive
with tarfile.open(out, "w:gz") as tar:
    add(tar, publish / "be", "be", True)
    for folder in ("tools/proofing-setup", "tools/lsp-setup"):
        for f in sorted((root / folder).iterdir()):
            if f.is_file():
                add(tar, f, f"{folder}/{f.name}", f.suffix == ".sh")
print(out)
