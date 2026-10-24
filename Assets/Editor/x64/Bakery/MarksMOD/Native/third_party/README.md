# Third-party dependencies

Pinned, build-time acquisition. Do not vendor binaries into the repository.

| Dependency | Used by | Notes |
|---|---|---|
| CUDA Toolkit | ftrace, denoisers, libfrender (CUDA path) | match driver/OptiX matrix |
| OptiX SDK/runtime (5/6/9) | ftrace, OptiX denoisers | `.ptx` kernels are portable |
| Vulkan loader + headers | libfrender (Vulkan path), seamfixer | `VK_KHR_ray_query` optional |
| oneTBB | concurrency | replaces `tbb*.dll` |
| Intel OIDN | OIDN denoisers | cross-platform |
| zlib / zstd / lz4 | GBuffer compression | match `.lz4` container |
| libpng / libjpeg / OpenEXR | image IO | TGA/DDS/HDR handled in-tree |

Fetch scripts and version pins live in this folder. Record license and provenance for each.
