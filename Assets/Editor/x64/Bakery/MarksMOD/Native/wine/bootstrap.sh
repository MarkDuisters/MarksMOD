#!/usr/bin/env bash
set -euo pipefail

BAKERY_WINE_ROOT="${BAKERY_WINE_ROOT:-${XDG_DATA_HOME:-$HOME/.local/share}/bakery}"
PREFIX="${BAKERY_WINEPREFIX:-$BAKERY_WINE_ROOT/wineprefix}"
SYS32="$PREFIX/drive_c/windows/system32"

log() { printf '[bakery-wine] %s\n' "$*"; }
die() { printf '[bakery-wine] error: %s\n' "$*" >&2; exit 1; }

find_wine() {
    if [ -n "${BAKERY_WINE:-}" ]; then
        printf '%s' "$BAKERY_WINE"
        return
    fi
    if command -v wine64 >/dev/null 2>&1; then
        command -v wine64
        return
    fi
    if command -v wine >/dev/null 2>&1; then
        command -v wine
        return
    fi
    die "wine64/wine not found; install Wine or set BAKERY_WINE"
}

WINE="$(find_wine)"
log "wine: $WINE ($("$WINE" --version 2>/dev/null || echo unknown))"
log "prefix: $PREFIX"

if ! command -v winetricks >/dev/null 2>&1; then
    die "winetricks not found; install winetricks (distro package or https://github.com/Winetricks/winetricks)"
fi

if ! command -v nvidia-smi >/dev/null 2>&1; then
    log "warning: nvidia-smi not found; CUDA/OptiX require the NVIDIA proprietary driver"
fi

mkdir -p "$BAKERY_WINE_ROOT"

export WINEPREFIX="$PREFIX"
export WINEARCH=win64

if [ ! -d "$PREFIX" ]; then
    log "creating 64-bit prefix"
    "$WINE" wineboot -i
else
    log "reusing existing prefix"
fi

log "installing latest VC++ runtime"
winetricks --force -q vcrun2022 || die "VC++ runtime install failed (try: winetricks --force vcrun2022)"

log "installing d3dcompiler_47"
winetricks -q d3dcompiler_47 || log "warning: d3dcompiler_47 verb failed"

log "installing DXVK (D3D11)"
winetricks -q dxvk || die "DXVK install failed"

if [ -L "$SYS32/nvcuda.dll" ]; then
    target="$(readlink "$SYS32/nvcuda.dll" 2>/dev/null || true)"
    case "$target" in
        *libcuda.so*)
            rm -f "$SYS32/nvcuda.dll"
            log "removed obsolete libcuda symlink (does not work on Wine 10+)"
            ;;
    esac
fi

nvidia_libs_version="${BAKERY_NVIDIA_LIBS_VERSION:-v1.0.2}"
nvidia_libs_root="${BAKERY_WINE_ROOT}/nvidia-libs"
nvidia_libs_dir="${BAKERY_NVIDIA_LIBS_DIR:-$nvidia_libs_root/nvidia-libs-$nvidia_libs_version}"

if [ "${BAKERY_NO_NVIDIA_LIBS:-0}" = "1" ]; then
    log "skipping nvidia-libs (BAKERY_NO_NVIDIA_LIBS=1)"
else
    if [ ! -f "$nvidia_libs_dir/x64/nvcuda.dll" ]; then
        log "fetching nvidia-libs $nvidia_libs_version (nvcuda + nvoptix)"
        mkdir -p "$nvidia_libs_root"
        archive="$nvidia_libs_root/nvidia-libs-$nvidia_libs_version.tar.xz"
        url="https://github.com/SveSop/nvidia-libs/releases/download/$nvidia_libs_version/nvidia-libs-$nvidia_libs_version.tar.xz"
        if command -v curl >/dev/null 2>&1; then
            curl -L --fail -o "$archive" "$url"
        elif command -v wget >/dev/null 2>&1; then
            wget -O "$archive" "$url"
        else
            die "curl or wget required to fetch nvidia-libs"
        fi
        tar -xf "$archive" -C "$nvidia_libs_root"
    fi

    if [ ! -f "$nvidia_libs_dir/x64/nvcuda.dll" ]; then
        die "nvidia-libs not found at $nvidia_libs_dir"
    fi

    log "installing nvidia-libs into the prefix"
    mkdir -p "$SYS32"
    for dll in nvcuda nvoptix; do
        if [ ! -f "$nvidia_libs_dir/x64/$dll.dll" ]; then
            log "warning: $dll.dll missing in nvidia-libs"
            continue
        fi
        WINEPREFIX="$PREFIX" "$WINE" reg add 'HKEY_CURRENT_USER\Software\Wine\DllOverrides' /v "$dll" /d native /f >/dev/null 2>&1 || true
        ln -sf "$nvidia_libs_dir/x64/$dll.dll" "$SYS32/$dll.dll"
    done
    touch "$PREFIX/.bakery-nvlibs-ok"
    log "nvidia-libs installed (CUDA + OptiX)"
fi

touch "$PREFIX/.bakery-setup-ok"
log "done"
log "next: ./probe.sh"
