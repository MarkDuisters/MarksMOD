# Wine bridge

The bridge keeps Bakery's original native engine and runs its Windows binaries under Wine.
A native Linux Unity loads `libfrender.so`, which exports the same ABI and forwards calls to
`bakery_wine_host.exe`, a Wine-hosted process that loads the real `frender.dll`.

```
Linux Unity -> libfrender.so (proxy) --TCP 127.0.0.1:27778--> bakery_wine_host.exe (Wine)
                                                                  -> frender.dll
Linux Unity -> libbakery_glue.so -> RunLocalProcess -> wine ftraceRTX9.exe / denoiser*.exe
```

## Packaging decision

Do not vendor a Wine build into the add-on. Use the system Wine plus `winetricks`.

Reasons:
- Size: Wine + a prefix is hundreds of MB to over 1 GB per install; it does not belong in a Unity asset.
- Licensing: Wine is LGPL-2.1+, DXVK is zlib, vkd3d is LGPL, winetricks is LGPL. Redistributing a bundle
  triggers source/notice obligations and conflicts with the Asset Store EULA conversation.
- Host dependencies remain: the NVIDIA driver, Vulkan loader, X11/Wayland libs and the kernel driver
  are still required, so bundling Wine does not make the add-on self-contained.
- Updates and security: distro-managed Wine and DXVK are patched; a bundled copy goes stale.

Provide instead:
1. Auto-detection of `wine64`/`wine`.
2. `BAKERY_WINE` and `BAKERY_WINEPREFIX` environment overrides.
3. `bootstrap.sh` to create a 64-bit prefix and install the runtime pieces.
4. `probe.sh` to verify the environment before a bake.
5. Clear instructions for users who have no Wine.

If a fully self-contained experience is required, ship it as a separate downloadable runtime
(AppImage/Flatpak launcher), not inside `Assets/`.

## Prerequisites

- 64-bit Wine (`wine64` preferred, otherwise `wine`).
- `winetricks` (used for the VC++ runtime, DXVK and d3dcompiler).
- NVIDIA proprietary driver with CUDA support; `nvidia-smi` must work.
- Vulkan loader and a Vulkan-capable driver.
- The Bakery store payload (contains `cudart64_91.dll`, `optix*.dll`, `d3dcompiler_47.dll`, OIDN, TBB).

## Setup

```
./bootstrap.sh
./probe.sh
```

The prefix defaults to `${XDG_DATA_HOME:-$HOME/.local/share}/bakery/wineprefix`. Override with
`BAKERY_WINEPREFIX=/path/to/prefix`.

### In-editor setup

`BakeryLinuxSetup.cs` + `BakeryMarksModWizard.cs` provide the setup commands and a status wizard:

- Open `Bakery > MarksMOD > Setup Wizard` manually; there is no automatic startup dialog.
- **PATCH!** runs the requirements check, runs `bootstrap.sh` with a progress bar and cancel when
  the Wine prefix is missing or incomplete, then patches the Bakery scripts.
- **Activate xatlas** installs Bakery's xatlas proxy.
- **Restore Bakery** restores the patched files, removes the MarksMOD folder and the Linux plugin
  shims, stops the Wine host, and deletes the Bakery-owned Wine prefix. Downloaded `nvidia-libs`,
  other Wine prefixes and user Bakery assets are kept.

The wizard (`BakeryLinuxDiagnostics`) reports OK/WARN/FAIL per component: Linux editor, runner
(`BAKERY_RUNNER`/Wine), winetricks, NVIDIA driver, prefix, VC runtime, DXVK
(`d3d11.dll`), `d3dcompiler_47`, `nvcuda.dll` mapping, setup sentinel, `libbakery_glue.so`,
`libfrender.so`, `bakery_wine_host.exe`, the Windows payload (`frender.dll`, `ftraceRTX9.exe`,
denoisers, combine tools), and the Unity graphics API.

Setup is considered complete when `$BAKERY_WINEPREFIX/.bakery-setup-ok` exists (written by
`bootstrap.sh` on success). If setup is incomplete, the editor logs a one-time warning pointing at
the wizard; the wizard's diagnostics continue to report the missing components until the environment
is installed.

## Bridge test

`probe.sh --ping` starts `bakery_wine_host.exe` if port 27778 is not already open, waits for it,
and sends a `CMD_PING` over TCP. The wizard's **Test Bridge** button runs the same thing and shows
the output in the window.

The host needs `BAKERY_FRENDER_DIR` set to the Bakery payload folder (where `frender.dll` lives);
`probe.sh` sets it. It also needs the GPU environment described below.

## CUDA and OptiX

Windows binaries call `nvcuda.dll`, which Linux does not have. A symlink from `nvcuda.dll` to the
host `libcuda.so.1` does **not** work on Wine 10+ (Wine rejects the ELF as "invalid .so").

The correct approach is Wine's `nvcuda` unixlib, shipped prebuilt by
[SveSop/nvidia-libs](https://github.com/SveSop/nvidia-libs) together with `nvoptix` (OptiX) and
`nvapi`/`nvml`. `bootstrap.sh` fetches the pinned release (`BAKERY_NVIDIA_LIBS_VERSION`, default
`v1.0.2`) into `$BAKERY_WINE_ROOT/nvidia-libs`, installs `nvcuda.dll` + `nvoptix.dll` into the
prefix with DLL overrides, and writes a `.bakery-nvlibs-ok` marker.

Requirements and limits:
- NVIDIA proprietary driver 525+ (580+ recommended).
- `nvidia-libs` supports CUDA SDK up to 13.2 and OptiX ABI up to 118 (OptiX 9.1).
- Do not move or delete the extracted `nvidia-libs` folder: the prefix symlinks point into it.
- Verify with `bin/cudatest.exe` from the package; it should print `cuInit success!` and your GPU.
- Set `BAKERY_NO_NVIDIA_LIBS=1` to skip (bring your own Wine with builtin nvcuda), or
  `BAKERY_NVIDIA_LIBS_DIR=/path` to use an existing extraction.

## Hybrid GPU (NVIDIA + iGPU)

On systems with both an NVIDIA GPU and an AMD/Intel iGPU, DXVK's `dxgi.hideNvidiaGpu` (Auto)
hides the NVIDIA GPU and reports the iGPU. `ftrace` would then use the iGPU for D3D11 while
CUDA/OptiX uses NVIDIA, which can hang or misdetect the hardware.

The bootstrap and `probe.sh` set:

```
DXVK_CONFIG="dxgi.hideNvidiaGpu = False"
VK_ICD_FILENAMES=/usr/share/vulkan/icd.d/nvidia_icd.json
```

so all D3D11 work goes to the NVIDIA GPU. The tools and the host inherit these variables.
`ftraceRTX9.exe hwtest` should then report only the NVIDIA device (exit code 4 = "new driver",
i.e. use the RTX9 path).

## D3D11

`frender`, `ftrace`, `uvgbuffergen`, `alphabuffergen` and `lmrebake` use D3D11 compute and
`D3DCompile`. Install DXVK in the prefix (`winetricks dxvk`); `d3dcompiler_47.dll` is bundled by
Bakery or can come from `winetricks d3dcompiler_47`.

## Unity plugin events

Unity triggers some `frender` work through `GL.IssuePluginEvent(3/4/5/6/7)`. The proxy exports
`UnityRenderEvent` and `UnitySetGraphicsDevice` so Unity calls the proxy, which forwards the event
ID to the worker. The worker calls the real `frender.dll!UnityRenderEvent` and owns its D3D11 device.

## ABI coverage and progress UI

`libfrender.so` forwards the Linux export path to the host:

- `InitShaders`, `LoadScene`, `UnityRenderEvent`
- `SetAlbedos`, `FreeAlbedoCopies`
- `SetAlphasFromRAM`, `CopyHeightmapsFromRAM`
- `SaveGBufferMapFromRAM`, `SaveCookieFromRAM`
- `SetCompression`, `SetFixPos`, `SetUVGBFlags`, `SetUVGBTangents`, `ToggleABGDither`
- `ftGenerateAlphaBuffer`, `ftRenderUVGBuffer`, `GetABGErrorCode`, `GetUVGBErrorCode`

The host creates its own D3D11 device (DXVK) and calls `UnitySetGraphicsDevice(device, 2, 0)`
so `frender` initializes its `uvgbuffergen`/`alphabuffergen` entry points. It preloads those DLLs
from the payload.

Progress is handled by `MarksMOD/Core/BakeryProgress.cs`: the native Windows popup is kept on
Windows, while Linux uses `EditorUtility.DisplayCancelableProgressBar` with real cancel support.
Already-shown bars are polled through the same getter the original code used.

xatlas runs through Bakery's own `scripts/xatlas/xatlasLib.dll` in the host. The Linux
bootstrap starts the host at editor startup and keeps it available for bakes and import-time
xatlas unwrapping; it shuts down when the editor quits. The wizard's **Test Bridge** starts it
if needed and sends `CMD_PING`.

## Host binary: ship it, don't build it

`bakery_wine_host.exe` is our own code, so it is shipped **prebuilt** in
`MarksMOD/Native/dist~/bin/bakery_wine_host.exe`. End users do not need a compiler. Developers
rebuild it with `build-host.sh`:

```
./build-host.sh
```

The script uses a system `x86_64-w64-mingw32-g++` if present; otherwise it downloads a
Linux-hosted `llvm-mingw` toolchain into `~/.local/share/bakery/mingw` (no sudo required).

Release packaging copies the locally built binary into `dist~/bin/`. The repo keeps the source, the
build script and the prebuilt binary; there is no CI build.

## Runner: Wine

`libbakery_glue.so` resolves tool names passed by the managed code (for example `ftraceRTX9`) into
`<tools-dir>/<name>.exe` and launches them through Wine. Environment:

| Variable | Default | Meaning |
|---|---|---|
| `BAKERY_RUNNER` | `wine` | Wine executable; use `wine64` when available |
| `BAKERY_TOOLS_DIR` | `<libdir>/../bin` | directory containing the tool executables |
| `BAKERY_TOOL_EXT` | `.exe` if a runner is set, else empty | extension appended to logical tool names |
| `BAKERY_WINEPREFIX` | unset | exported as `WINEPREFIX` for the child process |

## Limitations

- The non-DX11 host path is used on Linux, so data crosses the bridge as CPU buffers
  (`SetAlphasFromRAM`, `CopyHeightmapsFromRAM`, `SaveGBufferMapFromRAM`), not shared GPU textures.
- No AMD support (CUDA/OptiX).
- **xatlas is supported** through the host and `libxatlasLib_wine.so`; both pack and unwrap
  paths are covered. The legacy `uvrepack` alias remains unverified.
- **`halffloat2vb`/`halffloat2vbm` are implemented** and covered by vertex-lightmap and volume
  tests.
- **`ExtractZIP`** (the updater) is unimplemented and now returns an error.
- **OptiX 5/6 denoiser selections** are mapped to OptiX 7 on Linux. OptiX 7, OIDN v1 and
  OIDN v2 are supported.
- The bridge is covered by end-to-end bakes, including Bakery xatlas packing/unwrapping and denoisers.
