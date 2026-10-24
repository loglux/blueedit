#!/bin/sh
# The .deb and .rpm in clean Debian 12, Ubuntu 24.04 and Fedora 41 containers: installed with the system's own tool
# (what it recommends comes too), `be --version`, the formula pictures module found (probe.py), removed, nothing
# left. Run on a Linux host with Docker, in a folder with the two packages and probe.py (tasks packages-test).
set -u
deb=$(ls blueedit_*_amd64.deb); rpm=$(ls blueedit-*.x86_64.rpm)
for image in debian:12 ubuntu:24.04 fedora:41; do
  echo "===== $image"
  case $image in
    fedora*) inst="dnf install -y -q ./$rpm python3 >/dev/null 2>&1; echo installed: \$?"; rm_="dnf remove -y -q blueedit >/dev/null 2>&1"
             pulled="rpm -qa | grep -E '^(libicu|openssl-libs|ca-certificates)-' | sort";;
    *)       inst="apt-get update -qq >/dev/null && DEBIAN_FRONTEND=noninteractive apt-get install -y -qq ./$deb python3-minimal >/dev/null 2>&1; echo installed: \$?"; rm_="apt-get remove -y -qq blueedit >/dev/null 2>&1"
             pulled="dpkg -l | awk '/^ii/ && \$2 ~ /^(libicu|libssl|ca-certificates)/ {print \$2}'";;
  esac
  docker run --rm -v "$PWD:/p" -w /p "$image" sh -c "
    $inst
    ls -l /usr/bin/be; be --version
    echo 'recommended, installed:'; $pulled
    printf '# x\n' > /tmp/a.md
    python3 /p/probe.py
    $rm_; echo removed: \$?; ls /usr/bin/be /usr/lib/blueedit 2>&1 | head -2
  "
done
