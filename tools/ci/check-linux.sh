#!/usr/bin/env bash
# Everything on Linux, as check.ps1 does on Windows: build (warnings are errors), unit tests, the Linux build, the UI
# tests driving it in a pty - in parallel, then the ones marked serial one by one (tests/ui/conftest.py).
#   tools/ci/check-linux.sh            all of it
#   tools/ci/check-linux.sh --fast     build and unit tests only
# Needs the .NET 10 SDK and Python 3 with: pip install -r tests/ui/requirements.txt
set -euo pipefail
cd "$(dirname "$0")/../.."
FAST=0; [ "${1:-}" = --fast ] && FAST=1
WORKERS="${UI_WORKERS:-6}"
PY="${PYTHON:-python3}"                      # PYTHON=~/.venvs/x/bin/python to use a venv
PUBLISH="${PUBLISH_DIR:-$PWD/publish}"
step() { printf '\n\033[36m== %s\033[0m\n' "$1"; }

step 'build (analyzers on, warnings are errors)'; dotnet build --no-incremental -nologo -v q
step 'unit tests: editor';                       dotnet test tests/BlueEdit.Tests -nologo -v q
step 'unit tests: terminal layer (Tui)';          dotnet test tests/Tui.Tests -nologo -v q
[ $FAST = 1 ] && { printf '\nAll checks passed.\n'; exit 0; }
step 'build TuiProbe (terminal-layer test app)'; dotnet build tools/TuiProbe -c Release -nologo -v q
step 'publish the Linux build';                  dotnet publish -c Release -r linux-x64 --self-contained -p:PublishSingleFile=true -o "$PUBLISH" -nologo -v q
export BLUEEDIT_EXE="$PUBLISH/be"
cd tests/ui
step 'UI regression tests (in parallel)';        "$PY" -m pytest -q -p no:cacheprovider -m 'not serial' -n "$WORKERS"
step 'UI regression tests (one by one)';         "$PY" -m pytest -q -p no:cacheprovider -m serial
printf '\nAll checks passed.\n'
