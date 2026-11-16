# MarksMOD - Bakery Linux compatibility layer

Version: 1.1.0

MarksMOD is a Linux/Wine compatibility layer for an existing Bakery installation. It does not contain Bakery or any of its files.

## Requirements

- Unity Editor on Linux (tested with Unity 6000.3.22f1 and URP 17.3).
- NVIDIA GPU and a driver that supports CUDA and OptiX.
- Wine (tested with wine-11.19) and winetricks, installed through your distribution's package manager.
- Bakery installed in your Unity project before merging MarksMOD (tested with Bakery v1.985). See the [MarksMOD README](https://github.com/MarkDuisters/MarksMOD?tab=readme-ov-file) for details.

## Install

1. Merge this repository's `Assets/` folder into your Unity project's `Assets/` folder. This adds the Linux plugin shims under `Assets/Plugins/x86_64` and places the add-on at `Assets/Editor/x64/Bakery/MarksMOD` when Bakery uses its default path. If Bakery is installed elsewhere, place the mentioned folders relative to the custom Bakery location instead.
2. Open `Bakery > MarksMOD > Setup Wizard`.
3. Click `1. Run Setup` once to prepare the Wine prefix, the VC++ runtime, DXVK and the NVIDIA libraries.
4. Click `2. Patch Bakery`. The patcher edits your installed Bakery files in place and stores pristine backups under `Assets/Editor/x64/Bakery/MarksMOD/Native/backup~`.
5. Click `3. Activate xatlas` to install Bakery's xatlas proxy, then restart Unity.
6. Use `Re-check` and `Test Bridge` to validate the setup.

`Restore Bakery` restores the original Bakery files from the stored backups and removes the `MarksMOD` folder from Bakery. The Linux plugin shims in `Assets/Plugins` are kept.

The current patcher version is 18 and targets seven Bakery files.

## Tested scope

The tested configuration is Linux with an NVIDIA GPU, URP and RTX9 (`ftraceRTX9`).

Validation covered:

- Bake modes: full lighting, indirect, shadowmask, distance shadowmask, subtractive, AO-only and directional modes.
- Scene features: light types, volumes, sectors, prefabs, transparency, light probes and reflection probes.
- Workflows: cache reuse, cancel and re-bake, runtime lightmap apply, and patcher apply/restore.
- Denoisers: OptiX 7, OIDN v1 and OIDN v2.
- xatlas: Bakery's xatlas, atlas packing, unwrapping, multi-atlas and implicit groups, explicit single-atlas ownership, and two-level LOD groups.
- Source isolation: bakes are checked for unintended changes to source scenes and their assets.

## xatlas

Bakery's xatlas backend runs Bakery's own `xatlasLib.dll` through the Wine host. The active `libxatlasLib.so` is the ABI proxy; there is no separate native xatlas build. The pack, unwrap, multi-atlas, explicit single-atlas and default regression tests all pass using Bakery's own implementation. The Wine path uses a persistent host with readiness pings, serialized startup and shutdown, and TCP_NODELAY for practical IPC throughput. Use `3. Activate xatlas` in the setup wizard if the proxy is not active, then restart Unity before baking because native plugins may already be loaded.

Two-level LOD groups are tested. Very large meshes and the 32-bit index guard are not covered on this Unity version. Explicit groups intentionally produce one atlas; multi-atlas coverage uses implicit groups.

## Known limitations

- The `TEMP` environment variable must point to a path that contains only ASCII characters. Non-ASCII `TEMP` paths fail inside the vendor `uvGBufferGen` scene loader. Output paths may contain non-ASCII characters.
- The in-editor Bakery updater (`ExtractZIP`) is not implemented.
- OptiX 5 and OptiX 6 denoiser selections are mapped to OptiX 7 on Linux because the legacy denoisers are not supported by the Wine bridge. OptiX 7, OIDN v1 and OIDN v2 run through their normal backends.
- ECS, terrain, skinned meshes, APV and probe-SH directional mode have no test fixtures and are not claimed as supported.
- The assembled-prefab example is not bakeable by design; bake prefab assets through Bakery's prefab workflow instead.
- Interactive progress and wizard behavior were verified in a GUI editor session. Batchmode runs do not report progress.
- The Built-in render pipeline is out of scope; the tested pipeline is URP.
- Pixel-level parity between Windows and Linux was verified for the tested scenes; re-check new scenes after driver or Bakery changes.

## Notes

- The first `1. Run Setup` needs network access to download the winetricks components and the NVIDIA CUDA/OptiX libraries.
- The prebuilt `bakery_wine_host.exe` under `Assets/Editor/x64/Bakery/MarksMOD/Native/dist~/bin` is required; the setup wizard does not build it. It is a Windows helper launched through your system Wine (`/usr/bin/wine` by default), so Wine must be installed separately.
- The Linux plugin shims are provided under `Assets/Plugins/x86_64`.
- D3D11 tools (seamfixer, denoisers, buffer generators) are launched through Wine with explicit DXVK DLL overrides and the configured Wine prefix.
- The Wine host starts with the editor and stays available for import-time xatlas work. It shuts down when the editor quits.
- Bakery's UI is unchanged: use Bakery's Experimental settings for the global Atlas Packer and the Lightmap Group asset inspector for per-group overrides.
- Do not redistribute the Bakery addon or its native payload. Users must install Bakery themselves.

## License

MarksMOD is licensed under the GNU General Public License v3.0. See [LICENSE](LICENSE). Bakery itself is not included and remains under its own license.

## Screenshots

Bakery menu entry and the MarksMOD setup wizard:

![Bakery menu with the MarksMOD submenu](docs/images/bakery-menu-marksmod.png)
![Setup wizard after patching Bakery](docs/images/wizard-patched.png)
![Activating Bakery's built-in xatlas backend](docs/images/wizard-xatlas.png)
![Wizard environment checks and probe output](docs/images/wizard-checks.png)

Baking in the test scene:

![Bakery render window in RTX mode](docs/images/render-window.png)
![Bakery Progress window during a bake (animated)](docs/images/bake-progress.gif)
![Bakery render window after a re-bake](docs/images/render-window-baked.png)
![Baked lightmaps applied to the test scene](docs/images/bake-result.png)

Tested system:

![System information: Manjaro, KDE Plasma, Ryzen 9 9900X, RTX 4070 Ti SUPER](docs/images/system-info.png)

