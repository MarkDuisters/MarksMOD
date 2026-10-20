#!/usr/bin/env bash
set -euo pipefail

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
root="$(cd "$here/.." && pwd)"
src="$root/dist~/lib"
assets="$(cd "$root/../../../../.." && pwd)"
dst="$assets/Plugins/x86_64"

[ -f "$src/libfrender.so" ] || { printf 'missing %s/libfrender.so (build first)\n' "$src" >&2; exit 1; }
[ -f "$src/libbakery_glue.so" ] || { printf 'missing %s/libbakery_glue.so (build first)\n' "$src" >&2; exit 1; }

mkdir -p "$dst"

cp -f "$src/libfrender.so" "$dst/libfrender.so"
cp -f "$src/libbakery_glue.so" "$dst/libbakery_glue.so"

for alias in simpleProgressBar halffloat2vb uvrepack xatlasLib user32; do
    cp -f "$src/libbakery_glue.so" "$dst/lib$alias.so"
done

printf 'installed plugins to %s\n' "$dst"
ls -1 "$dst"
