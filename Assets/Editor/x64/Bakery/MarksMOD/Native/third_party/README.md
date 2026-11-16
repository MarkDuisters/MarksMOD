# Third-party dependencies

Build-time and runtime dependencies are supplied by the system or by Bakery's Windows payload.
Do not vendor them into the repository.

| Dependency | Used by | Notes |
|---|---|---|
| lz4 | `libbakery_glue.so` | system library, required by CMake |
| MinGW-w64 or llvm-mingw | `bakery_wine_host.exe` | `wine/build-host.sh` uses a system compiler or fetches llvm-mingw |
| NVIDIA nvidia-libs | Wine prefix | `nvcuda.dll` and `nvoptix.dll`, fetched by `wine/bootstrap.sh` |
| Wine, winetricks and DXVK | runtime | installed by the user |
| Bakery Windows payload | Wine host | `frender.dll`, `ftraceRTX9.exe`, denoisers and `xatlasLib.dll`; not redistributed |

Record the license and provenance for each dependency.
