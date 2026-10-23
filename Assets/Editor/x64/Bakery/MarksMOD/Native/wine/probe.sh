#!/usr/bin/env bash
set -uo pipefail

BAKERY_WINE_ROOT="${BAKERY_WINE_ROOT:-${XDG_DATA_HOME:-$HOME/.local/share}/bakery}"
PREFIX="${BAKERY_WINEPREFIX:-$BAKERY_WINE_ROOT/wineprefix}"
SYS32="$PREFIX/drive_c/windows/system32"

export DXVK_CONFIG="${DXVK_CONFIG:-dxgi.hideNvidiaGpu = False}"
export WINEDLLOVERRIDES="${WINEDLLOVERRIDES:-d3d11=n,b;dxgi=n,b;d3dcompiler_47=n,b}"
for icd in /usr/share/vulkan/icd.d/nvidia_icd.json /etc/vulkan/icd.d/nvidia_icd.json /usr/share/vulkan/icd.d/nvidia_icd.x86_64.json; do
    if [ -f "$icd" ]; then
        export VK_ICD_FILENAMES="${VK_ICD_FILENAMES:-$icd}"
        break
    fi
done
NATIVE_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../.." && pwd)"
MARKS_NATIVE="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

pass=0
fail=0

ok()   { printf '  ok    %s\n' "$*"; pass=$((pass+1)); }
bad()  { printf '  FAIL  %s\n' "$*"; fail=$((fail+1)); }
warn() { printf '  warn  %s\n' "$*"; }

find_wine() {
    if [ -n "${BAKERY_WINE:-}" ]; then printf '%s' "$BAKERY_WINE"; return; fi
    if command -v wine64 >/dev/null 2>&1; then command -v wine64; return; fi
    command -v wine 2>/dev/null
}

printf 'Bakery Wine probe\n'
WINE="$(find_wine || true)"
[ -n "${WINE:-}" ] && ok "wine: $("$WINE" --version 2>/dev/null)" || bad "wine not found"

if [ "${1:-}" = "--start" ]; then
    host_exe="$MARKS_NATIVE/dist~/bin/bakery_wine_host.exe"
    ping_py="$(dirname "${BASH_SOURCE[0]}")/host_ping.py"
    if python3 "$ping_py" --wait >/dev/null 2>&1; then exit 0; fi
    if [ ! -f "$host_exe" ]; then
        printf 'bakery_wine_host.exe (PE) missing\n' >&2
        exit 1
    fi
    export BAKERY_FRENDER_DIR="$NATIVE_DIR"
    WINEPREFIX="$PREFIX" WINEDEBUG=-all nohup "${WINE:-wine}" "$host_exe" >/tmp/bakery_wine_host.log 2>&1 &
    echo $! >/tmp/bakery_wine_host.pid
    python3 "$ping_py" --wait >/dev/null 2>&1 && exit 0 || exit 1
fi

command -v winetricks >/dev/null 2>&1 && ok "winetricks present" || warn "winetricks not found"
command -v nvidia-smi >/dev/null 2>&1 && ok "nvidia-smi present: $(nvidia-smi --query-gpu=name --format=csv,noheader 2>/dev/null | head -1)" || warn "nvidia-smi not found"

for r in wine64 wine proton umu-run; do
    command -v "$r" >/dev/null 2>&1 && ok "runner available: $r"
done
[ -n "${BAKERY_RUNNER:-}" ] && ok "BAKERY_RUNNER=$BAKERY_RUNNER" || warn "BAKERY_RUNNER unset (defaults to wine)"
host_exe="$MARKS_NATIVE/dist~/bin/bakery_wine_host.exe"
if [ -f "$host_exe" ] && file "$host_exe" 2>/dev/null | grep -q "PE32"; then
    ok "prebuilt wine host present (PE)"
else
    warn "bakery_wine_host.exe (PE) not built (needs mingw-w64 or CI)"
fi

[ -d "$PREFIX" ] && ok "prefix: $PREFIX" || bad "prefix missing: $PREFIX (run bootstrap.sh)"

for dll in msvcp140.dll vcruntime140.dll d3d11.dll d3dcompiler_47.dll; do
    [ -e "$SYS32/$dll" ] && ok "$dll present" || bad "$dll missing in prefix"
done

if [ -L "$SYS32/nvcuda.dll" ] && readlink "$SYS32/nvcuda.dll" 2>/dev/null | grep -q "libcuda.so"; then
    bad "nvcuda.dll is a libcuda symlink (broken on Wine 10+); run bootstrap.sh"
elif [ -e "$SYS32/nvcuda.dll" ] && [ -e "$SYS32/nvoptix.dll" ]; then
    ok "nvidia-libs installed (nvcuda + nvoptix)"
else
    bad "nvidia-libs missing (nvcuda/nvoptix); run bootstrap.sh"
fi

for f in frender.dll ftraceRTX9.exe combineMasks.exe d3dcompiler_47.dll; do
    [ -e "$NATIVE_DIR/$f" ] && ok "payload: $f" || warn "payload missing: $f"
done

plugins="$(cd "$NATIVE_DIR/../../../Plugins/x86_64" 2>/dev/null && pwd || echo "$NATIVE_DIR/../../../Plugins/x86_64")"
if [ -e "$plugins/libfrender.so" ] && [ -e "$plugins/libbakery_glue.so" ]; then
    ok "plugins installed: $plugins"
else
    bad "plugins missing (run: cmake --build build --target install-libs)"
fi

if [ -e "$plugins/libbakery_glue.so" ]; then
    if command -v ldd >/dev/null 2>&1; then
        glue_ldd="$(ldd "$plugins/libbakery_glue.so" 2>/dev/null || true)"
        if printf '%s' "$glue_ldd" | grep -q 'liblz4\.so\.1 => not found'; then
            bad "liblz4.so.1 missing (runtime dependency of libbakery_glue.so)"
        elif printf '%s' "$glue_ldd" | grep -q 'liblz4\.so\.1 =>'; then
            ok "liblz4.so.1 resolved: $(printf '%s' "$glue_ldd" | grep -m1 'liblz4\.so\.1 =>' | sed 's/^[[:space:]]*//')"
        else
            warn "liblz4.so.1 not referenced by libbakery_glue.so (static/vendored?)"
        fi
    else
        warn "ldd not found; cannot verify liblz4.so.1"
    fi
fi

nvidia_libs_version="${BAKERY_NVIDIA_LIBS_VERSION:-v1.0.2}"
nvidia_libs_dir="${BAKERY_NVIDIA_LIBS_DIR:-$BAKERY_WINE_ROOT/nvidia-libs/nvidia-libs-$nvidia_libs_version}"
if [ -f "$nvidia_libs_dir/bin/cudatest.exe" ]; then
    cu_out="$(WINEPREFIX="$PREFIX" WINEDEBUG=-all timeout 45 "${WINE:-wine}" "$nvidia_libs_dir/bin/cudatest.exe" 2>/dev/null || true)"
    if printf '%s' "$cu_out" | grep -q "cuInit success"; then
        gpu="$(printf '%s' "$cu_out" | grep -m1 -iE 'GeForce|RTX|Quadro|Tesla' | tr -d '\r' || true)"
        ok "CUDA functional: ${gpu:-cuInit success}"
    else
        bad "cudatest failed (CUDA not functional under Wine)"
    fi
else
    warn "cudatest.exe not found in nvidia-libs"
fi

mingw_cxx="$(find "${BAKERY_MINGW_DIR:-$HOME/.local/share/bakery/mingw}" -path '*/bin/x86_64-w64-mingw32-clang++' 2>/dev/null | head -1 || true)"
if command -v x86_64-w64-mingw32-g++ >/dev/null 2>&1 || [ -n "$mingw_cxx" ]; then
    ok "mingw toolchain available"
else
    warn "mingw toolchain not found (run wine/build-host.sh to auto-fetch)"
fi

if [ "${1:-}" = "--ping" ]; then
    ping_py="$(dirname "${BASH_SOURCE[0]}")/host_ping.py"
    if [ ! -f "$host_exe" ] || ! file "$host_exe" 2>/dev/null | grep -q "PE32"; then
        bad "cannot ping: bakery_wine_host.exe (PE) missing"
    else
        if python3 "$ping_py" --wait >/dev/null 2>&1; then
            ok "wine host already listening on 27778"
        else
            export BAKERY_FRENDER_DIR="$NATIVE_DIR"
            WINEPREFIX="$PREFIX" WINEDEBUG=-all nohup "${WINE:-wine}" "$host_exe" >/tmp/bakery_wine_host.log 2>&1 &
            host_pid=$!
            echo "$host_pid" >/tmp/bakery_wine_host.pid
            if python3 "$ping_py" --wait >/dev/null 2>&1; then
                ok "wine host started (pid $host_pid)"
            else
                bad "wine host did not open port 27778 (see /tmp/bakery_wine_host.log)"
            fi
        fi
        if python3 "$ping_py" >/tmp/bakery_ping.log 2>&1; then
            ok "CMD_PING round-trip ok"
        else
            bad "CMD_PING failed ($(tail -1 /tmp/bakery_ping.log 2>/dev/null))"
        fi
    fi
fi

printf '\nresult: %d ok, %d failed\n' "$pass" "$fail"
[ "$fail" -eq 0 ]
