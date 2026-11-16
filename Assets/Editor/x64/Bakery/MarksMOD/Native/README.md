# Bakery Linux native layer

Surface-layer replacement for the Windows-only Bakery native payload. Nothing here modifies
the add-on logic; it provides ABI-identical Linux libraries that the managed code reaches
through the `DllImport` names mapped by Mono.

## Layout

```
MarksMOD/Native/
  src/glue/      libbakery_glue.so       CPU/OS shim (process, progress, user32, half2vb, uvrepack, zip)
                  libxatlasLib_wine.so     ABI proxy to Bakery's Windows xatlasLib.dll
  src/frender/   libfrender.so           ABI proxy to the Wine-hosted frender.dll
  src/wine/      bakery_wine_host        Wine worker: loads real frender.dll, serves IPC
  wine/          bootstrap/probe/build scripts for the Wine bridge
  build/         CMake build
  dist~/lib/      built shared libraries (Unity-ignored; install-libs copies them)
  dist~/bin/      prebuilt Wine host (Unity-ignored)
```

The engine stays Bakery's original Windows binaries. They run under Wine; `libfrender.so` is a
proxy, not a reimplementation.

## ABI

- Export the exact Windows symbol names with C linkage (`extern "C"`) and matching signatures.
- Strings: `[MarshalAs(UnmanagedType.LPWStr)]` parameters arrive as **UTF-16** (`char16_t*`);
  default-marshalled `string` parameters arrive as **UTF-8** (`char*`). See `src/glue/abi.h`.
- Structs must match the managed layouts (for example `TexInput`).

## DllImport resolution

This project uses Unity's `NET_UNITY_4_8` profile, so `NativeLibrary` is unavailable and no
`SetDllImportResolver` is used. Mono maps the existing `DllImport` names to shared libraries by
name. `install-libs.sh` copies the built libraries into `MarksMOD/Assets/Plugins/x86_64` with aliases:

| Managed name | Library |
|---|---|
| `frender` | `libfrender.so` |
| `simpleProgressBar` | `libsimpleProgressBar.so` |
| `halffloat2vb` | `libhalffloat2vb.so` |
| `uvrepack` | `libuvrepack.so` |
| `xatlasLib` | `libxatlasLib.so` |
| `user32.dll` | `libuser32.so` |

All alias libraries are copies of `libbakery_glue.so` except `libfrender.so` and `libxatlasLib.so`.

`libxatlasLib.so` is the Wine proxy. It never casts its logical 64-bit atlas handle to a Linux
pointer: all atlas state is owned by `bakery_wine_host.exe`, which loads `BAKERY_XATLAS_DLL`
or `${BAKERY_FRENDER_DIR}/scripts\\xatlas\\xatlasLib.dll`.

## Dependencies

- `libbakery_glue.so` links the system `lz4` library, so `liblz4.so.1` must be available at runtime.
- `bakery_wine_host.exe` needs MinGW-w64 or llvm-mingw only when rebuilding. `wine/build-host.sh`
  uses a system compiler or fetches llvm-mingw automatically.
- The Wine prefix needs `nvcuda.dll` and `nvoptix.dll` from nvidia-libs. `wine/bootstrap.sh` fetches them.
- Wine, winetricks and DXVK are installed by the user through the distribution package manager.
- Bakery's Windows payload is not redistributed and must come from the user's Bakery installation.

## Build outputs and runtime locations

Source paths in the first column are relative to this `Native` folder. Result paths are relative
to the `MarksMOD/Assets/` folder that users merge into their Unity project.

This folder contains build inputs, runtime helper scripts and one prebuilt runtime binary.

| Path | Used for | Result or runtime location |
|---|---|---|
| `src/glue/bakery_glue.cpp` | Linux CPU/OS shim | `MarksMOD/Assets/Plugins/x86_64/libbakery_glue.so` and its aliases |
| `src/glue/xatlas_wine_proxy.cpp` | Linux xatlas proxy | `MarksMOD/Assets/Plugins/x86_64/libxatlasLib_wine.so` and the active `libxatlasLib.so` |
| `src/frender/frender_proxy.cpp` | Linux frender proxy | `MarksMOD/Assets/Plugins/x86_64/libfrender.so` |
| `src/wine/bakery_wine_host.cpp` | Wine worker executable | `MarksMOD/Assets/Editor/x64/Bakery/MarksMOD/Native/dist~/bin/bakery_wine_host.exe` |
| `build/CMakeLists.txt` | CMake build definition | `MarksMOD/Assets/Editor/x64/Bakery/MarksMOD/Native/dist~/lib/` |
| `build/install-libs.sh` | Copies libraries and aliases | `MarksMOD/Assets/Plugins/x86_64/` |
| `wine/build-host.sh` | Rebuilds the Wine host with MinGW | `MarksMOD/Assets/Editor/x64/Bakery/MarksMOD/Native/dist~/bin/bakery_wine_host.exe` |
| `wine/bootstrap.sh` | Creates the Wine prefix during `1. Run Setup` | Run in place from `MarksMOD/Assets/Editor/x64/Bakery/MarksMOD/Native/wine/` |
| `wine/probe.sh` | Runs `Test Bridge` and environment checks | Run in place from `MarksMOD/Assets/Editor/x64/Bakery/MarksMOD/Native/wine/` |
| `wine/host_ping.py` | Sends the host ping used by `probe.sh` | Run in place from `MarksMOD/Assets/Editor/x64/Bakery/MarksMOD/Native/wine/` |
| `dist~/bin/bakery_wine_host.exe` | Prebuilt host required at runtime | Keep in `MarksMOD/Assets/Editor/x64/Bakery/MarksMOD/Native/dist~/bin/` |
| `dist~/lib/` | Temporary CMake output, not shipped | Copy to `MarksMOD/Assets/Plugins/x86_64/` with `install-libs` |

The `.so` files are not loaded from `Native`. They must end up in
`MarksMOD/Assets/Plugins/x86_64/` because Mono resolves the `DllImport` names there after the
folder is merged. The host executable and the Wine scripts stay under
`MarksMOD/Assets/Editor/x64/Bakery/MarksMOD/Native/`.

With Bakery at its default path, `Native` is
`MarksMOD/Assets/Editor/x64/Bakery/MarksMOD/Native`. If Bakery is installed elsewhere, the same
tree lives in that Bakery's `MarksMOD/Native` folder, while the plugin destination remains
`MarksMOD/Assets/Plugins/x86_64/`.

Release packages already contain the built libraries in `MarksMOD/Assets/Plugins/x86_64/` and the
prebuilt host in `MarksMOD/Assets/Editor/x64/Bakery/MarksMOD/Native/dist~/bin/`. Rebuild them only
if you change the native source or want to build everything yourself.

## Build from source

### Linux libraries

```
cmake -S build -B build/out -DCMAKE_BUILD_TYPE=Release
cmake --build build/out -j
cmake --build build/out --target install-libs
```

The last command copies the built libraries into `MarksMOD/Assets/Plugins/x86_64/`. If you do not
use `install-libs.sh`, place the built files like this:

```
MarksMOD/Assets/Plugins/x86_64/
  libbakery_glue.so
  libfrender.so
  libxatlasLib.so          copy of libxatlasLib_wine.so
  libxatlasLib_wine.so
  libsimpleProgressBar.so  copy of libbakery_glue.so
  libhalffloat2vb.so       copy of libbakery_glue.so
  libuvrepack.so           copy of libbakery_glue.so
  libuser32.so             copy of libbakery_glue.so
```

### Wine host

```
wine/build-host.sh
```

This writes `bakery_wine_host.exe` directly to
`MarksMOD/Assets/Editor/x64/Bakery/MarksMOD/Native/dist~/bin/`. Keep it there. This path is
resolved by the add-on, and `dist~` is ignored by Unity so the executable is not imported as a
plugin. The prebuilt host is included in the release; rebuild it only if you change its source.

### Runtime scripts

`bootstrap.sh`, `probe.sh` and `host_ping.py` are runtime helpers, not build outputs. Keep them in
`MarksMOD/Assets/Editor/x64/Bakery/MarksMOD/Native/wine/`; the wizard and `Test Bridge` invoke them
from there and the scripts resolve their own paths relative to that folder.

Folders ending in `~` are ignored by Unity. That is why build outputs live in `dist~/`: otherwise
Unity would import them as native plugins and report duplicate names alongside
`MarksMOD/Assets/Plugins/x86_64`. `install-libs` publishes the loadable copies to
`MarksMOD/Assets/Plugins/x86_64`.

The glue launches tools through `BAKERY_RUNNER` (default `wine`; also `proton run`, `umu-run`, or
`none` for native tools), resolving logical names against `BAKERY_TOOLS_DIR` with `BAKERY_TOOL_EXT`.
See `wine/README.md`.

After replacing a `.so` file, restart Unity before baking because native plugins may already be loaded.

On first editor load, `BakeryLinuxSetup.cs` offers a **Setup Wine** dialog that runs
`wine/bootstrap.sh` with a progress bar, then opens the status wizard. The only menu entry is
`Bakery > MarksMOD > Setup Wizard` (per-component OK/WARN/FAIL). Use **1. Run Setup**,
**2. Patch Bakery**, then **3. Activate xatlas** in the wizard; the wizard also has a
**Show setup at start** toggle. Setup
completion is the `$BAKERY_WINEPREFIX/.bakery-setup-ok` sentinel.

## xatlas

xatlas runs through Bakery's own `scripts/xatlas/xatlasLib.dll` under the Wine host. The
active `MarksMOD/Assets/Plugins/x86_64/libxatlasLib.so` is the ABI proxy; `libxatlasLib_wine.so` is kept
as the source copy used by the wizard's Activate action.

Use **Activate** in the setup wizard if the proxy is not active, then restart Unity because
native plugins may already be loaded. The backend requires a configured Wine runner
(`BAKERY_WINE`), `bakery_wine_host.exe`, and the user's Bakery xatlas DLL.
