"""Makes blueedit_<version>_amd64.deb and blueedit-<version>-1.x86_64.rpm from the Linux build and the formula pictures
module, with nfpm (tools/packages/nfpm.yaml). nfpm is downloaded once, its SHA-256 checked against the one written
here (from its release's checksums.txt), and kept in the output folder's .nfpm/. Runs on Windows (the release
workflow) and on Linux. docs/tech/packages.md

    python tools/packages/build.py VERSION LINUX_PUBLISH_DIR MODULE_DIR OUT_DIR
"""
import hashlib
import pathlib
import platform
import subprocess
import sys
import tarfile
import urllib.request
import zipfile

NFPM = "2.48.0"
# from https://github.com/goreleaser/nfpm/releases/download/v2.48.0/checksums.txt
ARCHIVES = {
    "Windows": ("nfpm_2.48.0_Windows_x86_64.zip", "8ce12620212f5db557e370e35e2279ac7b106ea51e6a546a2be37a1a5d5bd8de"),
    "Linux": ("nfpm_2.48.0_Linux_x86_64.tar.gz", "6d7f9a28b2f397934403950fcbba83f598b08d02edbe79a0815b3b4897341750"),
}
ROOT = pathlib.Path(__file__).resolve().parents[2]


def nfpm(cache: pathlib.Path) -> pathlib.Path:
    exe = cache / ("nfpm.exe" if platform.system() == "Windows" else "nfpm")
    if exe.exists():
        return exe
    name, sha = ARCHIVES[platform.system()]
    cache.mkdir(parents=True, exist_ok=True)
    archive = cache / name
    urllib.request.urlretrieve(f"https://github.com/goreleaser/nfpm/releases/download/v{NFPM}/{name}", archive)
    got = hashlib.sha256(archive.read_bytes()).hexdigest()
    if got != sha:
        archive.unlink()
        sys.exit(f"{name}: SHA-256 {got}, expected {sha}")
    if name.endswith(".zip"):
        with zipfile.ZipFile(archive) as z:
            z.extract(exe.name, cache)
    else:
        with tarfile.open(archive) as t:
            t.extract(exe.name, cache, filter="data")
        exe.chmod(0o755)
    return exe


def main():
    version, linux, module, out = sys.argv[1], *(pathlib.Path(a).resolve() for a in sys.argv[2:5])
    out.mkdir(parents=True, exist_ok=True)
    tool = nfpm(out / ".nfpm")
    # nfpm leaves ${...} in the files' paths as written: the values are put in here, into a copy beside nfpm
    values = {"BLUEEDIT_VERSION": version, "BLUEEDIT_LINUX": linux.as_posix(), "BLUEEDIT_MODULE": module.as_posix(),
              "BLUEEDIT_ROOT": ROOT.as_posix()}
    config = (ROOT / "tools/packages/nfpm.yaml").read_text(encoding="utf-8")
    for k, v in values.items():
        config = config.replace("${" + k + "}", v)
    filled = out / ".nfpm" / "nfpm.yaml"
    filled.write_text(config, encoding="utf-8")
    for kind in ("deb", "rpm"):
        subprocess.run([str(tool), "package", "-f", str(filled), "-p", kind, "-t", str(out)], check=True)
    for p in sorted(out.glob(f"blueedit*{version}*")):
        print(p)


if __name__ == "__main__":
    main()
