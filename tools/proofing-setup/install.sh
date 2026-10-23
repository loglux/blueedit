#!/usr/bin/env bash
# Installs the free, local grammar checkers BlueEdit can use on Linux (and WSL), from their official sources, with
# checksums - the Linux counterpart of install.ps1. Everything goes into one folder (default
# ${XDG_DATA_HOME:-~/.local/share}/BlueEdit, where the editor looks), laid out as on Windows:
#
#     jre/            Java 21 runtime (Eclipse Temurin, adoptium.net)          for LanguageTool
#     languagetool/   LanguageTool, free open-source edition (languagetool.org)
#     llm/bin/        llama.cpp (github.com/ggml-org/llama.cpp)                for language models
#     llm/models/     .gguf models (huggingface.co)
#     harper/         harper-ls, Harper's rules for English (github.com/Automattic/harper)
#
# Spelling on Linux is Hunspell with the distribution's dictionaries (sudo apt install hunspell-ru hunspell-en-gb);
# Edge's proofreader is Windows-only. Nothing here needs root. A part already installed is skipped (--force reinstalls
# it). Downloads resume and are checked against the SHA-256 their publisher gives (LanguageTool publishes none: its
# archive is checked for the server jar instead). Needs curl, tar and python3 (for JSON and zip).
#
#   ./install.sh --all                          LanguageTool, llama.cpp, the recommended model, Harper
#   ./install.sh --harper                       Harper only (14 MB)
#   ./install.sh --llm --models yandexgpt,gemma3-12b
#   ./install.sh --list                         the models this script knows, and what is installed
#   options: --languagetool --llm --models a,b --harper --all --list --force --gpu auto|cuda|vulkan|cpu --llama-build bNNNNN
#            --root DIR
set -euo pipefail

ROOT="${XDG_DATA_HOME:-$HOME/.local/share}/BlueEdit"
LT=0; LLM=0; HARPER=0; MODELS=""; ALL=0; LIST=0; FORCE=0; GPU=auto; LLAMA_BUILD=b11371
while [ $# -gt 0 ]; do
    case "$1" in
        --languagetool) LT=1 ;;
        --llm) LLM=1 ;;
        --models) MODELS="$2"; shift ;;
        --harper) HARPER=1 ;;
        --all) ALL=1 ;;
        --list) LIST=1 ;;
        --force) FORCE=1 ;;
        --gpu) GPU="$2"; shift ;;
        --llama-build) LLAMA_BUILD="$2"; shift ;;
        --root) ROOT="$2"; shift ;;
        -h|--help) sed -n '2,22p' "$0" | sed 's/^# \{0,1\}//'; exit 0 ;;
        *) echo "unknown option: $1 (--help)" >&2; exit 2 ;;
    esac
    shift
done

# Models tested with BlueEdit (tools/proofbench), best first; Q4_K_M quantization. key|repo|file|note
CATALOG='yandexgpt|yandex/YandexGPT-5-Lite-8B-instruct-GGUF|YandexGPT-5-Lite-8B-instruct-Q4_K_M.gguf|best at Russian, ~6 GB of video memory (licence: Yandex'"'"'s own)
gemma3-12b|bartowski/google_gemma-3-12b-it-GGUF|google_gemma-3-12b-it-Q4_K_M.gguf|strong in Russian and English, ~8 GB
qwen3-8b|Qwen/Qwen3-8B-GGUF|Qwen3-8B-Q4_K_M.gguf|~6 GB
qwen3-14b|bartowski/Qwen_Qwen3-14B-GGUF|Qwen_Qwen3-14B-Q4_K_M.gguf|~10 GB, not better than Qwen3 8B here
gemma3-4b|bartowski/google_gemma-3-4b-it-GGUF|google_gemma-3-4b-it-Q4_K_M.gguf|for small GPUs, ~3 GB
phi4-mini|bartowski/microsoft_Phi-4-mini-instruct-GGUF|microsoft_Phi-4-mini-instruct-Q4_K_M.gguf|fastest, weakest, ~3 GB'
RECOMMENDED=yandexgpt
DOWNLOADS="$ROOT/downloads"

step() { printf '\n\033[36m== %s\033[0m\n' "$1"; }
say() { printf '  %s\n' "$1"; }
ok() { printf '  \033[32m%s\033[0m\n' "$1"; }
need() { command -v "$1" >/dev/null || { echo "needs $1 (e.g. sudo apt install $2)" >&2; exit 1; }; }
need curl curl; need tar tar; need python3 python3

# json URL EXPR: fetches JSON and prints a Python expression over it (d = the parsed JSON)
json() { curl -fsSL "$1" | python3 -c "import json,sys; d=json.load(sys.stdin); print($2)"; }

# get URL NAME [SHA256]: downloads into $DOWNLOADS (resuming), checks the SHA-256 when given, prints the path
get() {
    local url="$1" name="$2" sha="${3:-}" path="$DOWNLOADS/$2"
    mkdir -p "$DOWNLOADS"
    if [ -f "$path" ] && [ -n "$sha" ] && [ "$(sha256sum "$path" | cut -d' ' -f1)" = "${sha,,}" ]; then
        say "already downloaded: $name" >&2; echo "$path"; return
    fi
    say "downloading $name" >&2
    local quiet=''; [ -t 2 ] || quiet='-sS'                 # a progress bar only on a terminal, not in a log
    curl -fL $quiet --retry 3 -C - -o "$path.part" "$url" >&2
    if [ -n "$sha" ]; then
        local got; got=$(sha256sum "$path.part" | cut -d' ' -f1)
        if [ "$got" != "${sha,,}" ]; then rm -f "$path.part"; echo "checksum mismatch for $name: expected $sha, got $got" >&2; exit 1; fi
        say "SHA-256 OK" >&2
    fi
    mv -f "$path.part" "$path"; echo "$path"
}

# into ARCHIVE DEST: unpacks a .tar.gz or .zip into DEST; a single top folder's contents go to DEST itself
into() {
    local archive="$1" dest="$2" tmp; tmp=$(mktemp -d "$DOWNLOADS/unpack-XXXXXX")
    case "$archive" in
        *.zip) python3 -m zipfile -e "$archive" "$tmp" ;;
        *) tar xzf "$archive" -C "$tmp" ;;
    esac
    local top; top=$(find "$tmp" -mindepth 1 -maxdepth 1)
    local src="$tmp"; if [ "$(printf '%s\n' "$top" | wc -l)" = 1 ] && [ -d "$top" ]; then src="$top"; fi
    rm -rf "$dest"; mkdir -p "$(dirname "$dest")"; mv "$src" "$dest"; rm -rf "$tmp"
}

not_running() { if pgrep -f "^$1" >/dev/null; then echo "$1 is running (close BlueEdit first)" >&2; exit 1; fi; }

install_java() {
    step 'Java 21 runtime (Eclipse Temurin)'
    local java="$ROOT/jre/bin/java"
    if [ -x "$java" ] && [ $FORCE = 0 ]; then say "installed: $java"; return; fi
    not_running "$java"
    local api='https://api.adoptium.net/v3/assets/latest/21/hotspot?architecture=x64&image_type=jre&os=linux&vendor=eclipse'
    local link name sum version
    link=$(json "$api" "d[0]['binary']['package']['link']"); name=$(json "$api" "d[0]['binary']['package']['name']")
    sum=$(json "$api" "d[0]['binary']['package']['checksum']"); version=$(json "$api" "d[0]['version']['openjdk_version']")
    into "$(get "$link" "$name" "$sum")" "$ROOT/jre"
    ok "installed: $version"
}

install_languagetool() {
    install_java
    step 'LanguageTool (free open-source edition)'
    local jar="$ROOT/languagetool/languagetool-server.jar"
    if [ -f "$jar" ] && [ $FORCE = 0 ]; then say "installed: $jar"; return; fi
    # languagetool.org publishes no checksum for this file; the archive is checked for the server jar below
    into "$(get https://languagetool.org/download/LanguageTool-stable.zip LanguageTool-stable.zip)" "$ROOT/languagetool"
    [ -f "$jar" ] || { echo "languagetool-server.jar not found after unpacking" >&2; exit 1; }
    ok "installed: $(grep -m1 '^## ' "$ROOT/languagetool/CHANGES.md" || echo LanguageTool)"
}

gpu_kind() {
    if [ "$GPU" != auto ]; then echo "$GPU"; return; fi
    if command -v nvidia-smi >/dev/null && nvidia-smi >/dev/null 2>&1; then echo cuda; return; fi
    # a video card, not llvmpipe: Mesa's software device answers vulkaninfo too, as type CPU
    if command -v vulkaninfo >/dev/null && vulkaninfo --summary 2>/dev/null | grep -Eq 'PHYSICAL_DEVICE_TYPE_(INTEGRATED|DISCRETE|VIRTUAL)_GPU'; then
        echo vulkan; return
    fi
    say 'no NVIDIA driver and no Vulkan found: the CPU build (slow with models; LanguageTool is unaffected)' >&2
    echo cpu
}

install_llama() {
    step 'llama.cpp (runs .gguf language models)'
    local bin="$ROOT/llm/bin" server="$ROOT/llm/bin/llama-server"
    if [ -x "$server" ] && [ $FORCE = 0 ]; then say "installed: $server"; return; fi
    not_running "$server"
    local kind; kind=$(gpu_kind)
    local api="https://api.github.com/repos/ggml-org/llama.cpp/releases/tags/$LLAMA_BUILD"
    [ "$LLAMA_BUILD" = latest ] && api='https://api.github.com/repos/ggml-org/llama.cpp/releases/latest'
    local pattern
    case "$kind" in
        cuda) local driver; driver=$(nvidia-smi --query-gpu=driver_version --format=csv,noheader | head -1 | cut -d. -f1)
              if [ "${driver:-0}" -ge 580 ]; then pattern=ubuntu-cuda-13.4-x64; else pattern=ubuntu-cuda-12.8-x64; fi ;;
        vulkan) pattern=ubuntu-vulkan-x64 ;;
        cpu) pattern=ubuntu-x64 ;;
        *) echo "unknown --gpu $kind" >&2; exit 2 ;;
    esac
    # name|url|sha256 of the main archive, and for CUDA the runtime libraries archive
    local assets; assets=$(json "$api" "'\n'.join(a['name']+'|'+a['browser_download_url']+'|'+(a.get('digest') or '').replace('sha256:','') for a in d['assets'] if a['name'].endswith('-bin-$pattern.tar.gz'))")
    [ -n "$assets" ] || { echo "no llama.cpp asset for $pattern in $LLAMA_BUILD" >&2; exit 1; }
    say "llama.cpp $LLAMA_BUILD, $pattern"
    local tmp="$DOWNLOADS/llm-bin-new"; rm -rf "$tmp"; mkdir -p "$tmp"
    while IFS='|' read -r name url sha; do
        local part="$DOWNLOADS/part-$name"; into "$(get "$url" "$name" "$sha")" "$part"
        cp -a "$part/." "$tmp/"; rm -rf "$part"
    done <<< "$assets"
    rm -rf "$bin"; mkdir -p "$(dirname "$bin")"; mv "$tmp" "$bin"
    [ -x "$server" ] || { echo "llama-server not found after unpacking" >&2; exit 1; }
    ok "installed: $server ($("$server" --version 2>&1 | head -1))"
}

install_model() {
    local line; line=$(printf '%s\n' "$CATALOG" | grep "^$1|" || true)
    [ -n "$line" ] || { echo "unknown model '$1'; known: $(printf '%s\n' "$CATALOG" | cut -d'|' -f1 | tr '\n' ' ')" >&2; exit 2; }
    local repo file note; IFS='|' read -r _ repo file note <<< "$line"
    step "model $1 ($note)"
    local dest="$ROOT/llm/models/$file"
    if [ -f "$dest" ] && [ $FORCE = 0 ]; then say "installed: $dest"; return; fi
    local oid; oid=$(json "https://huggingface.co/api/models/$repo/tree/main" "next((e['lfs']['oid'] for e in d if e['path']=='$file'), '')")
    [ -n "$oid" ] || { echo "$file not found in huggingface.co/$repo" >&2; exit 1; }
    local path; path=$(get "https://huggingface.co/$repo/resolve/main/$file" "$file" "$oid")
    mkdir -p "$ROOT/llm/models"; mv -f "$path" "$dest"
    ok "installed: $dest ($(du -h "$dest" | cut -f1))"
}

# Harper's language server, the version BlueEdit was tested with; the SHA-256 is the one GitHub shows for the asset
HARPER_VERSION=v2.12.0
HARPER_SHA256=da87a01c49e535d6d6157f388ffc87edd9d4065fe04cc13b7a10b227defb1ba7
install_harper() {
    step "Harper $HARPER_VERSION (rules for English)"
    local dest="$ROOT/harper" exe="$ROOT/harper/harper-ls"
    if [ -x "$exe" ] && [ $FORCE = 0 ]; then say "installed: $exe"; return; fi
    [ "$(uname -m)" = x86_64 ] || { echo "Harper: only x86_64 here (see github.com/Automattic/harper/releases)" >&2; exit 1; }
    not_running "$exe"
    local archive; archive=$(get "https://github.com/Automattic/harper/releases/download/$HARPER_VERSION/harper-ls-x86_64-unknown-linux-gnu.tar.gz" "harper-ls-$HARPER_VERSION-linux.tar.gz" "$HARPER_SHA256")
    into "$archive" "$dest"
    [ -x "$exe" ] || { echo "harper-ls not found after unpacking" >&2; exit 1; }
    ok "installed: $exe"
}

show_list() {
    step "models (into $ROOT/llm/models)"
    while IFS='|' read -r key _ file note; do
        local have=''; [ -f "$ROOT/llm/models/$file" ] && have=installed
        printf '  %-11s %-10s %s\n' "$key" "$have" "$note"
    done <<< "$CATALOG"
    printf '\n  recommended: %s; see docs/proofing.md, "Comparison: which is more accurate"\n' "$RECOMMENDED"
}

if [ $LIST = 1 ]; then show_list; exit 0; fi
if [ $ALL = 1 ]; then LT=1; LLM=1; HARPER=1; [ -z "$MODELS" ] && MODELS=$RECOMMENDED; fi
if [ $LT = 0 ] && [ $LLM = 0 ] && [ $HARPER = 0 ] && [ -z "$MODELS" ]; then sed -n '2,22p' "$0" | sed 's/^# \{0,1\}//'; exit 0; fi
say "BlueEdit proofing components into $ROOT"
[ $LT = 1 ] && install_languagetool
[ $LLM = 1 ] && install_llama
if [ -n "$MODELS" ]; then IFS=',' read -ra ms <<< "$MODELS"; for m in "${ms[@]}"; do install_model "$m"; done; fi
[ $HARPER = 1 ] && install_harper
printf '\nDone. In BlueEdit: Tools > Grammar checking (and Tools > Grammar model).\n'
