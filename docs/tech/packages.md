# Linux packages: .deb and .rpm (`tools/packages`)

Russian: [packages.ru.md](packages.ru.md).

Every release has, besides the tar.gz, `blueedit_<version>_amd64.deb` (Debian, Ubuntu and the like) and
`blueedit-<version>-1.x86_64.rpm` (Fedora, RHEL and the like), installed with the system's own tool:
`sudo apt install ./blueedit_…deb`, `sudo dnf install ./blueedit-…rpm` ([docs/linux.md](../linux.md)).

## What is in them

| Where | What |
|---|---|
| `/usr/lib/blueedit/be` | the editor, the same file as in the tar.gz |
| `/usr/bin/be` | a link to it: `be` is on PATH |
| `/usr/lib/blueedit/mathimages/` | the formula pictures module: where the editor looks first, beside itself |
| `/usr/lib/blueedit/tools/…/install.sh` | the setup scripts for the checkers and language servers, as in the tar.gz (the `.ps1` ones are left out) |
| `/usr/share/doc/blueedit/copyright` | the licence (MIT) |

The editor's folders are the package's own (listed as directories), so removing it removes them: Fedora's `dnf
remove` first left an empty `/usr/lib/blueedit`, since rpm deletes only the folders a package owns.

Why `/usr/lib/blueedit` and a link, not the file in `/usr/bin`: the module and the scripts go beside the editor, and
`/usr/bin` takes programs only. Through the link the editor still finds the module: .NET takes its own folder from the
real file (`/proc/self/exe`), not from the link; checked by `probe.py` (below).

The module is in the package, not a package of its own: the Windows installer puts it in by default as well, and it
is 14 MB beside the editor's 76 MB.

**Depends on nothing.** The editor is self-contained (.NET inside). What it uses when it is there is recommended,
which apt and dnf install by default: the root certificates and OpenSSL (downloads over https; without them a window
says what to install) and ICU (the languages' names; without it .NET's invariant mode). Debian names its libraries by
version, so the .deb gives alternatives (`libicu76 | libicu74 | libicu72`, `libssl3t64 | libssl3`); the .rpm names
`libicu`, `openssl-libs`, `ca-certificates`. docs/linux.md says what the editor does without each.

## How they are made

`tools/packages/nfpm.yaml` describes both; `tools/packages/build.py VERSION LINUX_BUILD MODULE OUT` fills in the
paths and the version and runs [nfpm](https://github.com/goreleaser/nfpm) (one program, MIT; it makes .deb and .rpm
alike without dpkg or rpm, so on the release's Windows machine too). nfpm 2.48.0 is downloaded from its GitHub release
on first use and its SHA-256 compared with the one written in `build.py` (from that release's `checksums.txt`), as the
setup scripts do for Node.js and ruff. nfpm left `${…}` in the files' paths as written (it expands them in some fields
only), so `build.py` puts the values into a copy of the file.

The release workflow runs it after the tar.gz and the module, with the same Linux build, and attaches both with their
SHA-256. Here: `tasks packages` (into `publish-packages\`), then `tasks packages-test`.

## Checked

`tasks packages-test` (`tools/packages/test.sh`) copies them to the Linux host with Docker (`BLUEEDIT_LINUX_HOST`)
and in clean Debian 12, Ubuntu 24.04 and Fedora 41 containers installs each with apt or dnf, runs `be --version`,
starts `be` in a pseudo-terminal and turns View > Formula pictures off and on (`probe.py`: the editor says it found
the module), removes the package and looks for anything left. On 24 October 2026 all three passed: installed, the
module found, nothing left; apt brought `ca-certificates`, `libicu72` and `libssl3` on Debian, `libicu74` and
`libssl3t64` on Ubuntu; Fedora had its three already.

Not checked: installing a newer version over an older one (it should replace the files; no two versions to try yet),
other distributions (openSUSE takes the .rpm too, untried), arm64 (no build for it).

## Not done

- **Updates through `apt upgrade` and `dnf upgrade`**: they need a package repository of our own (a signing key, the
  index files, hosting such as GitHub Pages). Until then a newer package is installed the same way, over the old.
- **The distributions' own repositories**: they build from source under their own rules; not for now.
- **Snap and Flatpak**: sandboxed, they see the user's files only through portals; a console editor opening any
  file it is given does not suit them.
- **winget and Scoop** (Windows) come next; the plan is in the issue that tracks the packages.
