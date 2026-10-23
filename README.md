# MarksMOD - Bakery Linux compatibility layer

Version: 0.02

MarksMOD adds a Linux/Wine compatibility layer to an existing Bakery installation. It does not contain Bakery itself and does not include Bakery addon files.

## Requirements

- Linux editor with Unity (tested with 6000.3.22f1 and URP 17.3).
- NVIDIA GPU and driver for CUDA/OptiX.
- Wine (tested with 11.17) and winetricks.
- An existing Bakery installation in the project.

## Install

1. Copy `MarksMOD/Assets/*` into your Unity project's `Assets/` folder. This merges the `MarksMOD` folder and the Linux plugin shims.
2. Open `Bakery > MarksMOD > Setup Wizard`.
3. Run `Run Setup` once to prepare the Wine prefix, VC runtime, DXVK and NVIDIA libraries.
4. Click `Patch Bakery`. The patcher edits your own Bakery scripts in place and stores pristine backups under `MarksMOD/Native/backup~`.
5. Use `Re-check` and `Test Bridge` to validate the environment.

`Restore Bakery` reverts the patched Bakery scripts from the stored backups.

The current patcher version is 10 and targets eight Bakery files.

## Tested scope

The tested path is Linux + NVIDIA + URP + RTX9 (ftraceRTX9). The test suite covers full lighting, indirect, shadowmask, distance shadowmask, subtractive, AO-only, directional modes, light types, volumes, sectors, prefabs, transparency, denoisers (OptiX 7, OIDN v1/v2), light/reflection probes, cache reuse, cancel/re-bake, runtime lightmap apply, patcher apply/restore, xatlas atlas packing, xatlas unwrapping, multi-atlas/implicit groups, two-level LOD groups, and source isolation.

## xatlas

- `libxatlasLib.so` is built from the upstream xatlas source (MIT) in `Native/third_party/xatlas`.
- The UV-mesh path (`PackWithXatlas`) matches the Windows `xatlasLib.dll` in tests.
- The import-time AddMesh unwrap path is functional, but its UV layout can differ from the older Windows parameterizer. Bakes remain valid.
- Two-level LOD groups are tested. Very large meshes and the 32-bit index guard are not covered on this Unity version. No independent atlas UV overlap/bounds validation is claimed.

## Known limitations

- The TEMP path must be ASCII. Non-ASCII TEMP paths fail inside the vendor `uvGBufferGen` scene loader. Non-ASCII output paths work.
- The Bakery updater (`ExtractZIP`) is not implemented.
- OptiX 5 and OptiX 6 denoisers are unsupported under Wine. Use OptiX 7, RTX9 or OIDN.
- ECS, terrain, skinned meshes, APV and probe-SH directional mode have no test fixtures and are not claimed as supported. APV was not tested.
- The assembled-prefab example is not bakeable by design; use the prefab bake test path.
- Interactive progress and wizard dialogs require a GUI editor session. Batchmode progress reporting is suppressed.
- The Built-in render pipeline is out of scope; the tested pipeline is URP.
- No Windows-vs-Linux image parity claim is made. The test suite validates artifacts and isolation.

## Notes

- The prebuilt `bakery_wine_host.exe` is included under `MarksMOD/Native/dist~/bin`.
- The Linux plugin shims are provided under `Assets/Plugins/x86_64`.
- D3D11 tools (seamfixer, denoisers, buffer generators) are launched through Wine with explicit DXVK DLL overrides and the configured Wine prefix.
- The Wine host starts automatically when a bake begins and shuts down when the bake completes.
- Bakery UI is left native: use Bakery's Experimental settings for the global Atlas Packer and the Lightmap Group asset inspector for per-group overrides.
- Do not redistribute the Bakery addon or its native payload. Users must install Bakery themselves.
