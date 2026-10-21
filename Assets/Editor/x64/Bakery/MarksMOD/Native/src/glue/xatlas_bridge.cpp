#include "abi.h"

#include "xatlas.h"

#include <cfloat>
#include <cmath>
#include <cstdint>
#include <cstring>
#include <new>

#if defined(_WIN32)
#define XATLAS_BRIDGE_API extern "C" __declspec(dllexport)
#else
#define XATLAS_BRIDGE_API extern "C" __attribute__((visibility("default")))
#endif

namespace {

struct AtlasHandle
{
    xatlas::Atlas* atlas = nullptr;
    bool chartsComputed = false;
};

AtlasHandle* handle_of(void* ptr)
{
    return static_cast<AtlasHandle*>(ptr);
}

int map_add_mesh_error(xatlas::AddMeshError error)
{
    switch (static_cast<int>(error))
    {
    case 2: return 1;
    case 3: return 2;
    default: return 0;
    }
}

uint32_t next_power_of_two(uint32_t value)
{
    value |= value >> 1;
    value |= value >> 2;
    value |= value >> 4;
    value |= value >> 8;
    value |= value >> 16;
    return value + 1;
}

struct AtlasBounds
{
    float minU = FLT_MAX;
    float minV = FLT_MAX;
    float maxU = -FLT_MAX;
    float maxV = -FLT_MAX;
    float scaleU = 1.0f;
    float scaleV = 1.0f;
};

} // namespace

XATLAS_BRIDGE_API void* xatlasCreateAtlas()
{
    AtlasHandle* handle = new (std::nothrow) AtlasHandle();
    if (!handle) return nullptr;
    handle->atlas = xatlas::Create();
    if (!handle->atlas)
    {
        delete handle;
        return nullptr;
    }
    return handle;
}

XATLAS_BRIDGE_API int xatlasAddMesh(void* atlas, int vertexCount, const void* positions, const void* normals, const void* uv, int indexCount, const int* indices32)
{
    AtlasHandle* handle = handle_of(atlas);
    if (!handle || !handle->atlas) return 0;
    if (indexCount < 0 || (indexCount % 3) != 0) return 2;

    xatlas::MeshDecl decl;
    decl.vertexPositionData = positions;
    decl.vertexPositionStride = 12;
    decl.vertexNormalData = normals;
    decl.vertexNormalStride = normals ? 12 : 0;
    decl.vertexUvData = uv;
    decl.vertexUvStride = uv ? 8 : 0;
    decl.vertexCount = static_cast<uint32_t>(vertexCount);
    decl.indexData = indices32;
    decl.indexCount = static_cast<uint32_t>(indexCount);
    decl.indexFormat = xatlas::IndexFormat::UInt32;

    handle->chartsComputed = false;
    return map_add_mesh_error(xatlas::AddMesh(handle->atlas, decl));
}

XATLAS_BRIDGE_API int xatlasAddUVMesh(void* atlas, int vertexCount, const void* uv, int indexCount, const int* indices32, ft_bool allowRotate)
{
    (void)allowRotate;
    AtlasHandle* handle = handle_of(atlas);
    if (!handle || !handle->atlas) return 0;
    if (indexCount < 0 || (indexCount % 3) != 0) return 2;

    xatlas::UvMeshDecl decl;
    decl.vertexUvData = uv;
    decl.vertexStride = 8;
    decl.vertexCount = static_cast<uint32_t>(vertexCount);
    decl.indexData = indices32;
    decl.indexCount = static_cast<uint32_t>(indexCount);
    decl.indexFormat = xatlas::IndexFormat::UInt32;

    handle->chartsComputed = false;
    return map_add_mesh_error(xatlas::AddUvMesh(handle->atlas, decl));
}

XATLAS_BRIDGE_API void xatlasParametrize(void* atlas)
{
    AtlasHandle* handle = handle_of(atlas);
    if (!handle || !handle->atlas) return;
    xatlas::ComputeCharts(handle->atlas);
    handle->chartsComputed = true;
}

XATLAS_BRIDGE_API void xatlasPack(void* atlas, int attempts, float texelsPerUnit, int resolution, int maxChartSize, int padding, ft_bool bruteForce, ft_bool blockAlign)
{
    (void)attempts;
    AtlasHandle* handle = handle_of(atlas);
    if (!handle || !handle->atlas) return;

    if (!handle->chartsComputed)
    {
        xatlas::ComputeCharts(handle->atlas);
        handle->chartsComputed = true;
    }

    xatlas::PackOptions options;
    options.maxChartSize = maxChartSize > 0 ? static_cast<uint32_t>(maxChartSize) : 0;
    options.padding = padding > 0 ? static_cast<uint32_t>(padding) : 0;
    options.texelsPerUnit = texelsPerUnit;
    options.resolution = resolution > 0 ? static_cast<uint32_t>(resolution) : 0;
    options.bilinear = true;
    options.blockAlign = blockAlign != 0;
    options.bruteForce = bruteForce != 0;
    options.createImage = false;
    options.rotateChartsToAxis = false;
    options.rotateCharts = false;
    xatlas::PackCharts(handle->atlas, options);
}

XATLAS_BRIDGE_API void xatlasNormalize(void* atlas, const int* atlasSizes, ft_bool preferDensity)
{
    AtlasHandle* handle = handle_of(atlas);
    if (!handle || !handle->atlas) return;
    xatlas::Atlas* data = handle->atlas;
    const uint32_t atlasCount = data->atlasCount;
    if (atlasCount == 0) return;

    AtlasBounds* bounds = new (std::nothrow) AtlasBounds[atlasCount];
    if (!bounds) return;

    for (uint32_t m = 0; m < data->meshCount; m++)
    {
        const xatlas::Mesh& mesh = data->meshes[m];
        uint32_t atlasIndex = mesh.chartCount > 0 ? mesh.chartArray[0].atlasIndex : 0;
        if (atlasIndex >= atlasCount) atlasIndex = 0;
        AtlasBounds& b = bounds[atlasIndex];
        for (uint32_t v = 0; v < mesh.vertexCount; v++)
        {
            const float u = mesh.vertexArray[v].uv[0];
            const float w = mesh.vertexArray[v].uv[1];
            if (u < b.minU) b.minU = u;
            if (u > b.maxU) b.maxU = u;
            if (w < b.minV) b.minV = w;
            if (w > b.maxV) b.maxV = w;
        }
    }

    if (atlasSizes)
    {
        const uint32_t maxAtlasExtent = data->width > data->height ? data->width : data->height;
        for (uint32_t i = 0; i < atlasCount; i++)
        {
            AtlasBounds& b = bounds[i];
            const int raw = static_cast<int>(std::fmax(b.maxU - b.minU, b.maxV - b.minV));
            const uint32_t preferred = preferDensity
                ? maxAtlasExtent
                : next_power_of_two(static_cast<uint32_t>(raw - 1));
            uint32_t size = preferred > 16 ? preferred : 16;
            if (size >= maxAtlasExtent) size = maxAtlasExtent;
            const_cast<int*>(atlasSizes)[i] = static_cast<int>(size);
            if (preferDensity)
            {
                b.maxU = static_cast<float>(size);
                b.maxV = static_cast<float>(size);
            }
        }
    }

    for (uint32_t i = 0; i < atlasCount; i++)
    {
        AtlasBounds& b = bounds[i];
        if (preferDensity)
        {
            b.scaleU = 1.0f / b.maxU;
            b.scaleV = 1.0f / b.maxV;
        }
        else
        {
            b.scaleU = 1.0f / (b.maxU - b.minU);
            b.scaleV = 1.0f / (b.maxV - b.minV);
        }
    }

    for (uint32_t m = 0; m < data->meshCount; m++)
    {
        xatlas::Mesh& mesh = data->meshes[m];
        uint32_t atlasIndex = mesh.chartCount > 0 ? mesh.chartArray[0].atlasIndex : 0;
        if (atlasIndex >= atlasCount) atlasIndex = 0;
        const AtlasBounds& b = bounds[atlasIndex];
        for (uint32_t v = 0; v < mesh.vertexCount; v++)
        {
            mesh.vertexArray[v].uv[0] = (mesh.vertexArray[v].uv[0] - b.minU) * b.scaleU;
            mesh.vertexArray[v].uv[1] = (mesh.vertexArray[v].uv[1] - b.minV) * b.scaleV;
        }
    }

    delete[] bounds;
}

XATLAS_BRIDGE_API int xatlasGetAtlasCount(void* atlas)
{
    AtlasHandle* handle = handle_of(atlas);
    if (!handle || !handle->atlas) return 0;
    return static_cast<int>(handle->atlas->atlasCount);
}

XATLAS_BRIDGE_API int xatlasGetAtlasIndex(void* atlas, int meshIndex, int chartIndex)
{
    AtlasHandle* handle = handle_of(atlas);
    if (!handle || !handle->atlas) return 0;
    xatlas::Atlas* data = handle->atlas;
    if (meshIndex < 0 || static_cast<uint32_t>(meshIndex) >= data->meshCount) return 0;
    const xatlas::Mesh& mesh = data->meshes[meshIndex];
    if (chartIndex < 0 || static_cast<uint32_t>(chartIndex) >= mesh.chartCount) return 0;
    return static_cast<int>(mesh.chartArray[chartIndex].atlasIndex);
}

XATLAS_BRIDGE_API int xatlasGetVertexCount(void* atlas, int meshIndex)
{
    AtlasHandle* handle = handle_of(atlas);
    if (!handle || !handle->atlas) return 0;
    xatlas::Atlas* data = handle->atlas;
    if (meshIndex < 0 || static_cast<uint32_t>(meshIndex) >= data->meshCount) return 0;
    return static_cast<int>(data->meshes[meshIndex].vertexCount);
}

XATLAS_BRIDGE_API int xatlasGetIndexCount(void* atlas, int meshIndex)
{
    AtlasHandle* handle = handle_of(atlas);
    if (!handle || !handle->atlas) return 0;
    xatlas::Atlas* data = handle->atlas;
    if (meshIndex < 0 || static_cast<uint32_t>(meshIndex) >= data->meshCount) return 0;
    return static_cast<int>(data->meshes[meshIndex].indexCount);
}

XATLAS_BRIDGE_API void xatlasGetData(void* atlas, int meshIndex, void* outUV, void* outRef, void* outIndices)
{
    AtlasHandle* handle = handle_of(atlas);
    if (!handle || !handle->atlas) return;
    xatlas::Atlas* data = handle->atlas;
    if (meshIndex < 0 || static_cast<uint32_t>(meshIndex) >= data->meshCount) return;
    const xatlas::Mesh& mesh = data->meshes[meshIndex];

    if (outUV)
    {
        float* uv = static_cast<float*>(outUV);
        for (uint32_t i = 0; i < mesh.vertexCount; i++)
        {
            uv[i * 2 + 0] = mesh.vertexArray[i].uv[0];
            uv[i * 2 + 1] = mesh.vertexArray[i].uv[1];
        }
    }
    if (outRef)
    {
        uint32_t* ref = static_cast<uint32_t*>(outRef);
        for (uint32_t i = 0; i < mesh.vertexCount; i++)
            ref[i] = mesh.vertexArray[i].xref;
    }
    if (outIndices && mesh.indexArray)
        std::memcpy(outIndices, mesh.indexArray, mesh.indexCount * sizeof(uint32_t));
}

XATLAS_BRIDGE_API int xatlasClear(void* atlas)
{
    AtlasHandle* handle = handle_of(atlas);
    if (!handle) return 0;
    if (handle->atlas) xatlas::Destroy(handle->atlas);
    delete handle;
    return 0;
}
