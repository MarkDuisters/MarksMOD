# Bakery Linux native layer

Surface-layer replacement for the Windows-only Bakery native payload. Nothing here modifies
the add-on logic; it provides ABI-identical Linux libraries and tool executables that the
managed code reaches through a `DllImport` resolver (`MarksMOD/Editor/BakeryLinuxBootstrap.cs`).

## Layout

```
MarksMOD/Native/
  src/glue/      libbakery_glue.so   CPU/OS shim (process, progress, user32, half2vb, uvrepack, zip)
                  libxatlasLib.so     ABI proxy to Bakery's Windows xatlasLib.dll
  src/frender/   libfrender.so       ABI proxy to the Wine-hosted frender.dll (or stub)
  src/wine/      bakery_wine_host    Wine worker: loads real frender.dll, serves IPC
  src/ftrace/    ftrace*             (unused in the Wine path; kept for the native contingency)
  src/tools/     combine*, ftServer, seamfixer, ...   CLI helpers
  wine/          bootstrap/probe/build scripts for the Wine bridge
  build/         CMake build
  third_party/   pinned dependency notes
  dist~/lib/      built shared libraries (Unity-ignored; install-libs copies them)
  dist~/bin/      built tool executables (Unity-ignored)
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
name. `install-libs.sh` copies the built libraries into `Assets/Plugins/x86_64` with aliases:

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

## Build

```
cmake -S build -B build/out -DCMAKE_BUILD_TYPE=Release -DBAKERY_FRENDER_WINE=ON
cmake --build build/out -j
  cmake --build build/out --target install-libs   # copies the xatlas proxy + aliases to Assets/Plugins/x86_64

wine/bootstrap.sh       # creates/configures the user's Wine prefix
wine/probe.sh           # validates D3D11/DXVK + CUDA/OptiX bridge
```

Folders ending in `~` are ignored by Unity. That is why build outputs live in `dist~/`: otherwise
Unity would import them as native plugins and report duplicate names alongside
`Assets/Plugins/x86_64`. `install-libs` publishes the loadable copies to `Assets/Plugins/x86_64`.

Outputs land in `dist~/lib` and `dist~/bin`; `install-libs` publishes the loadable plugins.
`bakery_wine_host.exe` is shipped **prebuilt** in `dist~/bin`; rebuild it locally with `wine/build-host.sh`.
`wine/build-host.sh` auto-fetches a Linux-hosted `llvm-mingw` toolchain if no cross compiler is found.
Without `BAKERY_FRENDER_WINE=ON`, `libfrender.so` is a failing stub.

The glue launches tools through `BAKERY_RUNNER` (default `wine`; also `proton run`, `umu-run`, or
`none` for native tools), resolving logical names against `BAKERY_TOOLS_DIR` with `BAKERY_TOOL_EXT`.
See `wine/README.md`.

On first editor load, `BakeryLinuxSetup.cs` offers a **Setup Wine** dialog that runs
`wine/bootstrap.sh` with a progress bar, then opens the status wizard. The only menu entry is
`Bakery > MarksMOD > Setup Wizard` (per-component OK/WARN/FAIL). Use **1. Run Setup**,
**2. Patch Bakery**, then **3. Activate xatlas** in the wizard; the wizard also has a
**Show setup at start** toggle. Setup
completion is the `$BAKERY_WINEPREFIX/.bakery-setup-ok` sentinel.

## xatlas

xatlas runs through Bakery's own `scripts/xatlas/xatlasLib.dll` under the Wine host. The
active `Assets/Plugins/x86_64/libxatlasLib.so` is the ABI proxy; `libxatlasLib_wine.so` is kept
as the source copy used by the wizard's Activate action.

Use **Activate** in the setup wizard if the proxy is not active, then restart Unity because
native plugins may already be loaded. The backend requires a configured Wine runner
(`BAKERY_WINE`), `bakery_wine_host.exe`, and the user's Bakery xatlas DLL.
