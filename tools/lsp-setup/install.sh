#!/usr/bin/env bash
# Installs the language servers BlueEdit uses by default (docs/lsp.md) on Linux, or shows what is installed - the
# Linux counterpart of install.ps1.
#
#   --python   pyright (Microsoft, MIT licence) with npm, into ~/.local (no root)      needs Node.js
#              and ruff (Astral, MIT licence), which formats Python (Ctrl+F12): its release from GitHub, into
#              ~/.local/bin (no root, no Python needed)
#   --csharp   csharp-ls (MIT licence): dotnet tool install -g csharp-ls (~/.dotnet/tools)  needs the .NET SDK
#   --all      both
#   --yes      install a missing Node.js or .NET SDK with apt without asking first (apt needs sudo)
#
# Without an option it lists each default server, whether it is found the way BlueEdit looks for it (PATH,
# ~/.local/bin and ~/.dotnet/tools; Windows folders under /mnt/ are skipped in WSL), and its version. Running it
# again updates what is installed.
set -euo pipefail

PY=0; CS=0; YES=0
while [ $# -gt 0 ]; do
    case "$1" in
        --python) PY=1 ;;
        --csharp) CS=1 ;;
        --all) PY=1; CS=1 ;;
        --yes) YES=1 ;;
        -h|--help) sed -n '2,15p' "$0" | sed 's/^# \{0,1\}//'; exit 0 ;;
        *) echo "unknown option: $1 (--help)" >&2; exit 2 ;;
    esac
    shift
done

export PATH="$HOME/.local/bin:$HOME/.dotnet/tools:$PATH"
# a command found the way BlueEdit finds it: not a Windows program under /mnt/ (WSL lists those on PATH too)
have() { local p; p=$(command -v "$1" 2>/dev/null) && [[ "$p" != /mnt/* ]]; }

# ensure TEST PACKAGES WHAT: when TEST fails, offers to install the apt PACKAGES; false when it stays missing
ensure() {
    local test="$1" packages="$2" what="$3"
    if eval "$test"; then return 0; fi
    printf '\033[33m%s is not installed.\033[0m\n' "$what"
    if ! command -v apt-get >/dev/null; then echo "  no apt here: install $what with your package manager, then run this again."; return 1; fi
    if [ $YES = 0 ]; then
        read -r -p "  Install it now with apt ($packages; sudo asks for your password)? [y/N] " answer || answer=''
        [[ "$answer" =~ ^(y|yes|д|да)$ ]] || { echo '  Skipped.'; return 1; }
    fi
    sudo apt-get install -y $packages
    if eval "$test"; then printf '  \033[32m%s installed.\033[0m\n' "$what"; return 0; fi
    echo "  $what was not found after installing; run this again in a new shell."; return 1
}

# csharp-ls needs the SDK, not only the runtime: it loads projects with MSBuild
if [ $PY = 1 ] && ensure 'have npm' 'nodejs npm' 'Node.js (needed by pyright)'; then
    printf '\033[36m== pyright (Python)\033[0m\n'
    # into ~/.local: "npm install -g" would need root for the system's prefix
    npm install -g --prefix "$HOME/.local" pyright
fi
if [ $PY = 1 ]; then
    # pyright has no formatter: ruff formats Python. Its release archive, not pip: Ubuntu 24.04's pip refuses
    # --user installs (PEP 668), and this needs no Python at all
    printf '\033[36m== ruff (Python formatting)\033[0m\n'
    arch=$(uname -m); tmp=$(mktemp -d)
    url="https://github.com/astral-sh/ruff/releases/latest/download/ruff-$arch-unknown-linux-gnu.tar.gz"
    if command -v curl >/dev/null; then curl -fsSL "$url" -o "$tmp/ruff.tar.gz"; else wget -qO "$tmp/ruff.tar.gz" "$url"; fi
    tar xzf "$tmp/ruff.tar.gz" -C "$tmp"
    mkdir -p "$HOME/.local/bin"
    install -m 755 "$tmp"/ruff-*/ruff "$HOME/.local/bin/ruff"
    rm -rf "$tmp"
fi
if [ $CS = 1 ] && ensure 'have dotnet && [ -n "$(dotnet --list-sdks 2>/dev/null)" ]' 'dotnet-sdk-10.0' 'The .NET SDK (needed by csharp-ls)'; then
    printf '\033[36m== csharp-ls (C#)\033[0m\n'
    if dotnet tool list -g | grep -q '^csharp-ls\s'; then dotnet tool update -g csharp-ls; else dotnet tool install -g csharp-ls; fi
fi

printf '\n\033[36m== language servers BlueEdit would use\033[0m\n'
for entry in 'Python|pyright-langserver|pyright --version|python' 'C#|csharp-ls|csharp-ls --version|csharp'; do
    IFS='|' read -r lang cmd ver opt <<< "$entry"
    if have "$cmd"; then printf '  \033[32m[ok]  %-7s %s  (%s)\033[0m\n' "$lang" "$(command -v "$cmd")" "$($ver 2>/dev/null | head -1)"
    else printf '  [--]  %-7s %s not found: install.sh --%s\n' "$lang" "$cmd" "$opt"; fi
done
printf '\n\033[36m== formatters (Ctrl+F12) where the server has none\033[0m\n'
if have ruff; then printf '  \033[32m[ok]  Python  %s  (%s)\033[0m\n' "$(command -v ruff)" "$(ruff --version)"
elif have black; then printf '  \033[32m[ok]  Python  %s  (%s)\033[0m\n' "$(command -v black)" "$(black --version | head -1)"
else printf '  [--]  Python  ruff or black not found: install.sh --python\n'; fi
printf '\n  Open a .py or .cs file in BlueEdit: the status line shows LSP <server>, errors are underlined.\n'
