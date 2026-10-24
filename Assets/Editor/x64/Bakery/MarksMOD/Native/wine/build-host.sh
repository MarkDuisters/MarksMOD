#!/usr/bin/env bash
set -euo pipefail

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
root="$(cd "$here/.." && pwd)"
out="$root/dist~/bin"
src="$root/src/wine/bakery_wine_host.cpp"

mingw_dir="${BAKERY_MINGW_DIR:-$HOME/.local/share/bakery/mingw}"
toolchain_version="${BAKERY_LLVM_MINGW_VERSION:-20261006}"
toolchain_asset="${BAKERY_LLVM_MINGW_ASSET:-llvm-mingw-20261006-ucrt-ubuntu-22.04-x86_64.tar.xz}"

find_cxx() {
    if [ -n "${BAKERY_CXX:-}" ]; then printf '%s' "$BAKERY_CXX"; return; fi
    if command -v x86_64-w64-mingw32-g++ >/dev/null 2>&1; then command -v x86_64-w64-mingw32-g++; return; fi
    local found
    found="$(find "$mingw_dir" -path '*/bin/x86_64-w64-mingw32-clang++' 2>/dev/null | head -1 || true)"
    if [ -n "$found" ]; then printf '%s' "$found"; return; fi
    found="$(find "$mingw_dir" -path '*/bin/x86_64-w64-mingw32-g++' 2>/dev/null | head -1 || true)"
    if [ -n "$found" ]; then printf '%s' "$found"; fi
    return 0
}

cxx="$(find_cxx || true)"
if [ -z "$cxx" ]; then
    command -v curl >/dev/null 2>&1 || { printf 'curl required to fetch llvm-mingw\n' >&2; exit 1; }
    command -v tar >/dev/null 2>&1 || { printf 'tar required to extract llvm-mingw\n' >&2; exit 1; }

    mkdir -p "$mingw_dir"
    archive="$mingw_dir/$toolchain_asset"
    if [ ! -f "$archive" ]; then
        printf 'downloading llvm-mingw (%s) into %s\n' "$toolchain_version" "$mingw_dir"
        curl -L --fail -o "$archive" "https://github.com/mstorsjo/llvm-mingw/releases/download/$toolchain_version/$toolchain_asset"
    fi

    printf 'extracting toolchain...\n'
    tar -xf "$archive" -C "$mingw_dir"
    cxx="$(find_cxx)"
    [ -n "$cxx" ] || { printf 'llvm-mingw compiler not found after extraction\n' >&2; exit 1; }
fi

mkdir -p "$out"
"$cxx" -std=c++17 -O2 -static -pthread -I "$root/src/wine" -I "$root/src/frender" -o "$out/bakery_wine_host.exe" "$src" -lws2_32
printf 'built %s with %s\n' "$out/bakery_wine_host.exe" "$cxx"
