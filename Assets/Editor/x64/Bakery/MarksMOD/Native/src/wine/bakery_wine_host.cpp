#include "ipc.h"
#include "frender_abi.h"

#ifndef NOMINMAX
#define NOMINMAX
#endif
#ifndef WIN32_LEAN_AND_MEAN
#define WIN32_LEAN_AND_MEAN
#endif

#include <winsock2.h>
#include <ws2tcpip.h>
#include <windows.h>

#include <cstdio>
#include <cstring>
#include <string>
#include <vector>
#include <unordered_map>
#include <algorithm>
#include <atomic>
#include <thread>
#include <mutex>

namespace {

using FnInitShaders = void (*)();
using FnUnitySetGraphicsDevice = void (*)(void*, int, int);
using FnLoadScene = void (*)(const char*);
using FnUnityRenderEvent = void (*)(int);
using FnRenderUVGBuffer = int (*)();
using FnGenerateAlphaBuffer = int (*)();
using FnGetErrorCode = int (*)();
using FnSetAlbedos = void (*)(int, void**);
using FnFreeAlbedoCopies = void (*)();
using FnSetAlphasFromRAM = void (*)(int, void*, const float*, const int*, int, ft_bool);
using FnCopyHeightmapsFromRAM = int (*)(int, const FTTexInput*);
using FnSaveGBufferMapFromRAM = int (*)(const uint8_t*, int, const ft_utf8*, ft_bool);
using FnSaveCookieFromRAM = void (*)(FTTexInput, const ft_utf8*);
using FnSetBool = void (*)(ft_bool);
using FnSetInt = void (*)(int);
using FnXCreate = void* (__cdecl*)();
using FnXAddMesh = int (__cdecl*)(void*, int, const void*, const void*, const void*, int, const int*);
using FnXAddUVMesh = int (__cdecl*)(void*, int, const void*, int, const int*, int32_t);
using FnXParametrize = void (__cdecl*)(void*);
using FnXPack = void (__cdecl*)(void*, int, float, int, int, int, int32_t, int32_t);
using FnXNormalize = void (__cdecl*)(void*, const int*, int32_t);
using FnXGetCount = int (__cdecl*)(void*);
using FnXGetMeshCount = int (__cdecl*)(void*, int);
using FnXGetIndex = int (__cdecl*)(void*, int, int);
using FnXGetData = void (__cdecl*)(void*, int, void*, void*, void*);
using FnXClear = int (__cdecl*)(void*);

std::string exe_dir()
{
    char buf[MAX_PATH];
    DWORD n = GetModuleFileNameA(nullptr, buf, MAX_PATH);
    std::string s(buf, n);
    auto pos = s.find_last_of("\\/");
    return pos == std::string::npos ? std::string() : s.substr(0, pos + 1);
}

std::string payload_dir()
{
    char buf[MAX_PATH];
    DWORD n = GetEnvironmentVariableA("BAKERY_FRENDER_DIR", buf, MAX_PATH);
    if (n > 0 && n < MAX_PATH)
    {
        std::string s(buf, n);
        if (!s.empty() && s.back() != '\\' && s.back() != '/') s.push_back('\\');
        return s;
    }
    return exe_dir();
}

const uint32_t MAX_FRAME = 512u * 1024u * 1024u;
const int32_t XATLAS_SETUP_ERROR = -100;

std::unordered_map<uint64_t, void*> g_xatlas;
uint64_t g_next_xatlas = 1;
std::mutex g_command_lock;

template <typename T> T xproc(HMODULE module, const char* name)
{
    return module ? (T)GetProcAddress(module, name) : nullptr;
}

bool xread_handle(bakery_ipc::Reader& r, uint64_t& id, void*& atlas)
{
    if (!r.raw(&id, sizeof(id))) return false;
    auto it = g_xatlas.find(id);
    atlas = it == g_xatlas.end() ? nullptr : it->second;
    return atlas != nullptr;
}

int32_t handle_xatlas(uint32_t cmd, const std::vector<uint8_t>& payload, HMODULE module,
                      FnXCreate create, FnXAddMesh addMesh, FnXAddUVMesh addUvMesh,
                      FnXParametrize parametrize, FnXPack pack, FnXNormalize normalize,
                      FnXGetCount getCount, FnXGetMeshCount getVertexCount, FnXGetMeshCount getIndexCount,
                      FnXGetIndex getIndex, FnXGetData getData,
                      FnXClear clear, std::vector<uint8_t>& response)
{
    if (!module || !create || !addMesh || !addUvMesh || !parametrize || !pack ||
        !normalize || !getCount || !getVertexCount || !getIndexCount || !getIndex || !getData || !clear) return XATLAS_SETUP_ERROR;
    bakery_ipc::Reader r(payload.data(), payload.size());
    bakery_ipc::Writer out;
    uint64_t id = 0;
    void* atlas = nullptr;
    if (cmd == CMD_XATLAS_CREATE)
    {
        atlas = create();
        if (!atlas) return -1;
        id = g_next_xatlas++;
        if (id == 0) id = g_next_xatlas++;
        g_xatlas[id] = atlas;
        out.raw(&id, sizeof(id));
        response.swap(out.data);
        return 0;
    }
    if (!xread_handle(r, id, atlas)) return -101;

    if (cmd == CMD_XATLAS_ADD_MESH)
    {
        int32_t vertices = 0, indices = 0; uint8_t hasNormals = 0, hasUv = 0;
        if (!r.i32(vertices) || vertices < 0 || !r.u8(hasNormals) || !r.u8(hasUv) ||
            !r.i32(indices) || indices < 0 || indices > (int32_t)(MAX_FRAME / 4)) return -2;
        size_t pbytes = (size_t)vertices * 12, nbytes = hasNormals ? (size_t)vertices * 12 : 0;
        size_t ubytes = hasUv ? (size_t)vertices * 8 : 0, ibytes = (size_t)indices * 4;
        if (pbytes + nbytes + ubytes + ibytes > MAX_FRAME) return -2;
        std::vector<uint8_t> p(pbytes), n(nbytes), u(ubytes), i(ibytes);
        if (!r.raw(p.data(), pbytes) || !r.raw(n.data(), nbytes) || !r.raw(u.data(), ubytes) || !r.raw(i.data(), ibytes)) return -2;
        return addMesh(atlas, vertices, p.data(), hasNormals ? n.data() : nullptr, hasUv ? u.data() : nullptr, indices, (const int*)i.data());
    }
    if (cmd == CMD_XATLAS_ADD_UV_MESH)
    {
        int32_t vertices = 0, indices = 0, rotate = 0;
        if (!r.i32(vertices) || vertices < 0 || !r.i32(indices) || indices < 0 || !r.i32(rotate)) return -2;
        size_t ubytes = (size_t)vertices * 8, ibytes = (size_t)indices * 4;
        if (ubytes + ibytes > MAX_FRAME) return -2;
        std::vector<uint8_t> u(ubytes), i(ibytes);
        if (!r.raw(u.data(), ubytes) || !r.raw(i.data(), ibytes)) return -2;
        return addUvMesh(atlas, vertices, u.data(), indices, (const int*)i.data(), rotate);
    }
    if (cmd == CMD_XATLAS_PARAMETRIZE) { parametrize(atlas); return 0; }
    if (cmd == CMD_XATLAS_PACK)
    {
        int32_t attempts = 0, resolution = 0, maxChart = 0, padding = 0, brute = 0, block = 0; float tpu = 0;
        if (!r.i32(attempts) || !r.f32(tpu) || !r.i32(resolution) || !r.i32(maxChart) || !r.i32(padding) || !r.i32(brute) || !r.i32(block)) return -2;
        pack(atlas, attempts, tpu, resolution, maxChart, padding, brute, block); return 0;
    }
    if (cmd == CMD_XATLAS_NORMALIZE)
    {
        int32_t density = 0, count = -1;
        if (!r.i32(density) || !r.i32(count) || count < -1 || count > 65536) return -2;
        std::vector<int32_t> sizes(count > 0 ? (size_t)count : 0);
        if (count > 0 && !r.raw(sizes.data(), sizes.size() * sizeof(int32_t))) return -2;
        normalize(atlas, count > 0 ? sizes.data() : nullptr, density); response.swap(out.data); if (count > 0) response.assign((uint8_t*)sizes.data(), (uint8_t*)sizes.data() + sizes.size() * sizeof(int32_t)); return 0;
    }
    if (cmd == CMD_XATLAS_GET_ATLAS_COUNT) { int32_t v = getCount(atlas); out.i32(v); response.swap(out.data); return 0; }
    if (cmd == CMD_XATLAS_GET_ATLAS_INDEX || cmd == CMD_XATLAS_GET_VERTEX_COUNT || cmd == CMD_XATLAS_GET_INDEX_COUNT)
    {
        int32_t mesh = 0, chart = 0; if (!r.i32(mesh)) return -2;
        int32_t v = 0;
        if (cmd == CMD_XATLAS_GET_ATLAS_INDEX) { if (!r.i32(chart)) return -2; v = getIndex(atlas, mesh, chart); }
        else if (cmd == CMD_XATLAS_GET_VERTEX_COUNT) v = getVertexCount(atlas, mesh);
        else v = getIndexCount(atlas, mesh);
        out.i32(v); response.swap(out.data); return 0;
    }
    if (cmd == CMD_XATLAS_GET_DATA)
    {
        int32_t mesh = 0; if (!r.i32(mesh)) return -2;
        int32_t vertices = 0, indices = 0; // host obtains sizes before allocating output buffers.
        // The caller supplies the sizes obtained through the query commands.
        if (!r.i32(vertices) || !r.i32(indices) || vertices < 0 || indices < 0) return -2;
        if ((uint64_t)vertices * 8 + (uint64_t)vertices * 4 + (uint64_t)indices * 4 > MAX_FRAME) return -2;
        std::vector<float> uv((size_t)vertices * 2); std::vector<uint32_t> refs(vertices), inds(indices);
        getData(atlas, mesh, uv.data(), refs.data(), inds.data());
        out.i32(vertices); out.i32(indices); out.raw(uv.data(), uv.size() * sizeof(float)); out.raw(refs.data(), refs.size() * sizeof(uint32_t)); out.raw(inds.data(), inds.size() * sizeof(uint32_t)); response.swap(out.data); return 0;
    }
    if (cmd == CMD_XATLAS_CLEAR) { int32_t result = clear(atlas); g_xatlas.erase(id); return result; }
    return -3;
}

std::vector<FTTexInput> g_alphaTex;
std::vector<std::vector<uint8_t>> g_alphaBuffers;
std::vector<float> g_alphaRefs;
std::vector<int32_t> g_alphaChannels;
std::vector<FTTexInput> g_heightTex;
std::vector<std::vector<uint8_t>> g_heightBuffers;

void* create_d3d11_device()
{
    HMODULE d3d11 = LoadLibraryA("d3d11.dll");
    if (!d3d11)
    {
        std::fprintf(stderr, "bakery_wine_host: d3d11.dll not found\n");
        return nullptr;
    }

    using FnCreate = long (__stdcall*)(void*, int, void*, unsigned, const void*, unsigned, unsigned, void**, int*, void**);
    auto create = (FnCreate)GetProcAddress(d3d11, "D3D11CreateDevice");
    if (!create)
    {
        std::fprintf(stderr, "bakery_wine_host: D3D11CreateDevice not found\n");
        return nullptr;
    }

    void* device = nullptr;
    void* context = nullptr;
    int featureLevel = 0;
    long hr = create(nullptr, 1, nullptr, 0, nullptr, 0, 7, &device, &featureLevel, &context);
    if (hr < 0 || !device)
    {
        std::fprintf(stderr, "bakery_wine_host: D3D11CreateDevice failed (0x%08lx)\n", (unsigned long)hr);
        return nullptr;
    }

    std::fprintf(stderr, "bakery_wine_host: D3D11 device created\n");
    return device;
}

bool recv_all(SOCKET s, void* dst, int len)
{
    char* p = (char*)dst;
    while (len > 0)
    {
        int n = recv(s, p, len, 0);
        if (n <= 0) return false;
        p += n;
        len -= n;
    }
    return true;
}

bool send_all(SOCKET s, const void* src, int len)
{
    const char* p = (const char*)src;
    while (len > 0)
    {
        int n = send(s, p, len, 0);
        if (n <= 0) return false;
        p += n;
        len -= n;
    }
    return true;
}

int32_t handle_command(uint32_t cmd, const std::vector<uint8_t>& payload,
                       FnInitShaders fnInit,
                       FnLoadScene fnLoadScene,
                       FnUnityRenderEvent fnRenderEvent,
                       FnRenderUVGBuffer fnUVGB,
                       FnGenerateAlphaBuffer fnAlpha,
                       FnGetErrorCode fnABG,
                       FnGetErrorCode fnUVGBErr,
                       FnSetAlbedos fnSetAlbedos,
                       FnFreeAlbedoCopies fnFreeAlbedo,
                       FnSetAlphasFromRAM fnSetAlphas,
                       FnCopyHeightmapsFromRAM fnCopyHeightmaps,
                       FnSaveGBufferMapFromRAM fnSaveGBuffer,
                       FnSaveCookieFromRAM fnSaveCookie,
                       FnSetBool fnSetBool,
                       FnSetInt fnSetInt)
{
    bakery_ipc::Reader r(payload.data(), payload.size());

    switch (cmd)
    {
        case CMD_PING:
            return 1;
        case CMD_INIT_SHADERS:
            if (!fnInit) return -1;
            fnInit();
            return 0;
        case CMD_LOAD_SCENE:
            if (!fnLoadScene || payload.empty() || payload.back() != 0) return -1;
            fnLoadScene((const char*)payload.data());
            return 0;
        case CMD_UNITY_RENDER_EVENT:
        {
            int32_t event = 0;
            if (!r.i32(event)) return -2;
            if (!fnRenderEvent) return -1;
            fnRenderEvent(event);
            return 0;
        }
        case CMD_RENDER_UVGBUFFER:
            return fnUVGB ? fnUVGB() : -1;
        case CMD_GENERATE_ALPHA:
            return fnAlpha ? fnAlpha() : -1;
        case CMD_GET_ABG_ERROR:
            return fnABG ? fnABG() : -1;
        case CMD_GET_UVGB_ERROR:
            return fnUVGBErr ? fnUVGBErr() : -1;
        case CMD_SET_ALBEDOS:
        {
            int32_t count = 0;
            if (!r.i32(count) || count < 0 || count > 65536) return -2;
            if (!fnSetAlbedos) return -1;
            std::vector<void*> ptrs((size_t)count, (void*)nullptr);
            fnSetAlbedos(count, ptrs.data());
            return 0;
        }
        case CMD_FREE_ALBEDO_COPIES:
            if (fnFreeAlbedo) fnFreeAlbedo();
            return 0;
        case CMD_SET_ALPHAS_FROM_RAM:
        {
            int32_t count = 0;
            if (!r.i32(count) || count < 0 || count > 65536) return -2;

            g_alphaTex.assign((size_t)count, FTTexInput());
            g_alphaBuffers.assign((size_t)count, std::vector<uint8_t>());
            g_alphaRefs.assign((size_t)count, 0.0f);
            g_alphaChannels.assign((size_t)count, 0);

            for (int i = 0; i < count; i++)
            {
                int32_t size = 0;
                uint16_t w = 0, h = 0;
                if (!r.i32(size) || !r.u16(w) || !r.u16(h) || size < 0) return -2;
                int64_t expected = (int64_t)w * (int64_t)h * 4;
                if (expected != (int64_t)size || (uint32_t)size > MAX_FRAME) return -2;

                g_alphaBuffers[(size_t)i].resize((size_t)size);
                if (size > 0 && !r.raw(g_alphaBuffers[(size_t)i].data(), (size_t)size)) return -2;
                g_alphaTex[(size_t)i].data = size > 0 ? g_alphaBuffers[(size_t)i].data() : nullptr;
                g_alphaTex[(size_t)i].width = w;
                g_alphaTex[(size_t)i].height = h;
            }

            for (int i = 0; i < count; i++)
            {
                if (!r.f32(g_alphaRefs[(size_t)i])) return -2;
            }
            for (int i = 0; i < count; i++)
            {
                if (!r.i32(g_alphaChannels[(size_t)i])) return -2;
            }

            int32_t numLODs = 0;
            uint8_t flip = 0;
            if (!r.i32(numLODs) || !r.u8(flip)) return -2;
            if (!fnSetAlphas) return -1;

            fnSetAlphas(count, g_alphaTex.data(), g_alphaRefs.data(), g_alphaChannels.data(), numLODs, flip);
            return 0;
        }
        case CMD_COPY_HEIGHTMAPS_FROM_RAM:
        {
            int32_t count = 0;
            if (!r.i32(count) || count < 0 || count > 65536) return -2;

            g_heightTex.assign((size_t)count, FTTexInput());
            g_heightBuffers.assign((size_t)count, std::vector<uint8_t>());

            for (int i = 0; i < count; i++)
            {
                int32_t size = 0;
                uint16_t w = 0, h = 0;
                if (!r.i32(size) || !r.u16(w) || !r.u16(h) || size < 0) return -2;
                int64_t expected = (int64_t)w * (int64_t)h * 4;
                if (expected != (int64_t)size || (uint32_t)size > MAX_FRAME) return -2;

                g_heightBuffers[(size_t)i].resize((size_t)size);
                if (size > 0 && !r.raw(g_heightBuffers[(size_t)i].data(), (size_t)size)) return -2;
                g_heightTex[(size_t)i].data = size > 0 ? g_heightBuffers[(size_t)i].data() : nullptr;
                g_heightTex[(size_t)i].width = w;
                g_heightTex[(size_t)i].height = h;
            }

            if (!fnCopyHeightmaps) return -1;
            return fnCopyHeightmaps(count, g_heightTex.data());
        }
        case CMD_SAVE_GBUFFER_MAP_FROM_RAM:
        {
            int32_t size = 0;
            std::string path;
            uint8_t compressed = 0;
            if (!r.i32(size) || size < 0 || (uint32_t)size > MAX_FRAME) return -2;

            std::vector<uint8_t> data((size_t)size);
            if (size > 0 && !r.raw(data.data(), (size_t)size)) return -2;
            if (!r.str(path) || !r.u8(compressed)) return -2;
            if (!fnSaveGBuffer) return -1;

            return fnSaveGBuffer(data.empty() ? nullptr : data.data(), size, path.c_str(), compressed);
        }
        case CMD_SAVE_COOKIE_FROM_RAM:
        {
            int32_t size = 0;
            uint16_t w = 0, h = 0;
            std::string path;
            if (!r.i32(size) || !r.u16(w) || !r.u16(h) || size < 0) return -2;
            if ((int64_t)w * (int64_t)h * 16 != (int64_t)size || (uint32_t)size > MAX_FRAME) return -2;

            std::vector<uint8_t> data((size_t)size);
            if (size > 0 && !r.raw(data.data(), (size_t)size)) return -2;
            if (!r.str(path)) return -2;
            if (!fnSaveCookie) return -1;

            FTTexInput input = {};
            input.data = data.empty() ? nullptr : data.data();
            input.width = w;
            input.height = h;
            fnSaveCookie(input, path.c_str());
            return 0;
        }
        case CMD_SET_COMPRESSION:
        case CMD_SET_FIX_POS:
        {
            int32_t value = 0;
            if (!r.i32(value)) return -2;
            if (!fnSetBool) return -1;
            fnSetBool(value != 0);
            return 0;
        }
        case CMD_SET_UVGB_FLAGS:
        case CMD_SET_UVGB_TANGENTS:
        case CMD_TOGGLE_ABG_DITHER:
        {
            int32_t value = 0;
            if (!r.i32(value)) return -2;
            if (!fnSetInt) return -1;
            fnSetInt(value);
            return 0;
        }
        default:
            return -3;
    }
}

}

int main()
{
    std::string dir = payload_dir();
    SetCurrentDirectoryA(dir.c_str());

    WSADATA wsa;
    if (WSAStartup(MAKEWORD(2, 2), &wsa) != 0)
    {
        std::fprintf(stderr, "bakery_wine_host: WSAStartup failed\n");
        return 1;
    }

    SOCKET srv = socket(AF_INET, SOCK_STREAM, IPPROTO_TCP);
    if (srv == INVALID_SOCKET)
    {
        std::fprintf(stderr, "bakery_wine_host: socket failed\n");
        WSACleanup();
        return 1;
    }

    int reuse = 1;
    setsockopt(srv, SOL_SOCKET, SO_REUSEADDR, (const char*)&reuse, sizeof(reuse));
    sockaddr_in addr = {};
    addr.sin_family = AF_INET;
    addr.sin_addr.s_addr = htonl(INADDR_LOOPBACK);
    addr.sin_port = htons(BAKERY_WINE_PORT);
    if (bind(srv, (sockaddr*)&addr, sizeof(addr)) != 0 || listen(srv, 4) != 0)
    {
        std::fprintf(stderr, "bakery_wine_host: port %d already owned or bind/listen failed; exiting\n", BAKERY_WINE_PORT);
        closesocket(srv);
        WSACleanup();
        return 2;
    }
    std::fprintf(stderr, "bakery_wine_host: listening on 127.0.0.1:%d\n", BAKERY_WINE_PORT);

    HMODULE uvgb = LoadLibraryA((dir + "uvgbuffergen.dll").c_str());
    HMODULE abg = LoadLibraryA((dir + "alphabuffergen.dll").c_str());
    if (!uvgb) std::fprintf(stderr, "bakery_wine_host: failed to load uvgbuffergen.dll (error %lu)\n", GetLastError());
    if (!abg) std::fprintf(stderr, "bakery_wine_host: failed to load alphabuffergen.dll (error %lu)\n", GetLastError());

    HMODULE lib = LoadLibraryA((dir + "frender.dll").c_str());
    if (!lib)
    {
        std::fprintf(stderr, "bakery_wine_host: failed to load %sfrender.dll (error %lu)\n", dir.c_str(), GetLastError());
        return 1;
    }

    std::string xatlasPath;
    char xatlasOverride[MAX_PATH];
    DWORD xatlasLen = GetEnvironmentVariableA("BAKERY_XATLAS_DLL", xatlasOverride, MAX_PATH);
    if (xatlasLen > 0 && xatlasLen < MAX_PATH) xatlasPath.assign(xatlasOverride, xatlasLen);
    else xatlasPath = dir + "scripts\\xatlas\\xatlasLib.dll";
    HMODULE xatlasLib = LoadLibraryA(xatlasPath.c_str());
    if (!xatlasLib) std::fprintf(stderr, "bakery_wine_host: xatlas DLL unavailable at %s (error %lu)\n", xatlasPath.c_str(), GetLastError());
    auto xCreate = xproc<FnXCreate>(xatlasLib, "xatlasCreateAtlas");
    auto xAddMesh = xproc<FnXAddMesh>(xatlasLib, "xatlasAddMesh");
    auto xAddUvMesh = xproc<FnXAddUVMesh>(xatlasLib, "xatlasAddUVMesh");
    auto xParametrize = xproc<FnXParametrize>(xatlasLib, "xatlasParametrize");
    auto xPack = xproc<FnXPack>(xatlasLib, "xatlasPack");
    auto xNormalize = xproc<FnXNormalize>(xatlasLib, "xatlasNormalize");
    auto xGetAtlasCount = xproc<FnXGetCount>(xatlasLib, "xatlasGetAtlasCount");
    auto xGetAtlasIndex = xproc<FnXGetIndex>(xatlasLib, "xatlasGetAtlasIndex");
    auto xGetVertexCount = xproc<FnXGetMeshCount>(xatlasLib, "xatlasGetVertexCount");
    auto xGetIndexCount = xproc<FnXGetMeshCount>(xatlasLib, "xatlasGetIndexCount");
    auto xGetData = xproc<FnXGetData>(xatlasLib, "xatlasGetData");
    auto xClear = xproc<FnXClear>(xatlasLib, "xatlasClear");

    auto fnSetGfx = (FnUnitySetGraphicsDevice)GetProcAddress(lib, "UnitySetGraphicsDevice");
    void* d3dDevice = create_d3d11_device();
    if (fnSetGfx && d3dDevice) fnSetGfx(d3dDevice, 2, 0);

    auto fnInit = (FnInitShaders)GetProcAddress(lib, "InitShaders");
    auto fnLoadScene = (FnLoadScene)GetProcAddress(lib, "LoadScene");
    auto fnRenderEvent = (FnUnityRenderEvent)GetProcAddress(lib, "UnityRenderEvent");
    auto fnUVGB = (FnRenderUVGBuffer)GetProcAddress(lib, "ftRenderUVGBuffer");
    auto fnAlpha = (FnGenerateAlphaBuffer)GetProcAddress(lib, "ftGenerateAlphaBuffer");
    auto fnABG = (FnGetErrorCode)GetProcAddress(lib, "GetABGErrorCode");
    auto fnUVGBErr = (FnGetErrorCode)GetProcAddress(lib, "GetUVGBErrorCode");
    auto fnSetAlbedos = (FnSetAlbedos)GetProcAddress(lib, "SetAlbedos");
    auto fnFreeAlbedo = (FnFreeAlbedoCopies)GetProcAddress(lib, "FreeAlbedoCopies");
    auto fnSetAlphas = (FnSetAlphasFromRAM)GetProcAddress(lib, "SetAlphasFromRAM");
    auto fnCopyHeightmaps = (FnCopyHeightmapsFromRAM)GetProcAddress(lib, "CopyHeightmapsFromRAM");
    auto fnSaveGBuffer = (FnSaveGBufferMapFromRAM)GetProcAddress(lib, "SaveGBufferMapFromRAM");
    auto fnSaveCookie = (FnSaveCookieFromRAM)GetProcAddress(lib, "SaveCookieFromRAM");

    auto fnSetCompression = (FnSetBool)GetProcAddress(lib, "SetCompression");
    auto fnSetFixPos = (FnSetBool)GetProcAddress(lib, "SetFixPos");
    auto fnSetUVGBFlags = (FnSetInt)GetProcAddress(lib, "SetUVGBFlags");
    auto fnSetUVGBTangents = (FnSetInt)GetProcAddress(lib, "SetUVGBTangents");
    auto fnToggleABGDither = (FnSetInt)GetProcAddress(lib, "ToggleABGDither");

    std::atomic<bool> running(true);
    while (running.load())
    {
        SOCKET cli = accept(srv, nullptr, nullptr);
        if (cli == INVALID_SOCKET) continue;

        int noDelay = 1;
        setsockopt(cli, IPPROTO_TCP, TCP_NODELAY, (const char*)&noDelay, sizeof(noDelay));

        std::thread([&, cli]()
        {

        DWORD rcvTimeout = 60000;
        setsockopt(cli, SOL_SOCKET, SO_RCVTIMEO, (const char*)&rcvTimeout, sizeof(rcvTimeout));

        while (running.load())
        {
            BakeryIpcHeader h = {};
            if (!recv_all(cli, &h, sizeof(h))) break;
            if (h.magic != BAKERY_IPC_MAGIC) break;
            if (h.len > MAX_FRAME) break;

            if (GetEnvironmentVariableA("BAKERY_XATLAS_IPC_LOG", nullptr, 0) > 0)
            {
                char logValue[8] = {};
                GetEnvironmentVariableA("BAKERY_XATLAS_IPC_LOG", logValue, sizeof(logValue));
                if (std::strcmp(logValue, "1") == 0)
                    std::fprintf(stderr, "bakery_wine_host ipc <- %s (%u)\n", bakery_ipc::command_name(h.cmd), h.cmd);
            }

            std::vector<uint8_t> payload(h.len);
            if (h.len > 0 && !recv_all(cli, payload.data(), (int)h.len)) break;

            if (h.cmd == CMD_SHUTDOWN)
            {
                std::lock_guard<std::mutex> shutdownGuard(g_command_lock);
                running.store(false);
                closesocket(srv);
                int32_t status = 0;
                send_all(cli, &status, sizeof(status));
                break;
            }

            int32_t status;
            std::vector<uint8_t> xresponse;
            std::lock_guard<std::mutex> commandGuard(g_command_lock);
            const bool isXatlas = h.cmd >= CMD_XATLAS_CREATE && h.cmd <= CMD_XATLAS_CLEAR;
            if (isXatlas)
            {
                status = handle_xatlas(h.cmd, payload, xatlasLib, xCreate, xAddMesh, xAddUvMesh,
                    xParametrize, xPack, xNormalize, xGetAtlasCount, xGetVertexCount,
                    xGetIndexCount, xGetAtlasIndex, xGetData, xClear, xresponse);
                uint32_t responseLen = (uint32_t)xresponse.size();
                if (!send_all(cli, &status, sizeof(status)) || !send_all(cli, &responseLen, sizeof(responseLen)) ||
                    (responseLen > 0 && !send_all(cli, xresponse.data(), responseLen))) break;
                continue;
            }
            if (h.cmd == CMD_SET_COMPRESSION)
            {
                status = handle_command(h.cmd, payload, fnInit, fnLoadScene, fnRenderEvent, fnUVGB, fnAlpha,
                                        fnABG, fnUVGBErr, fnSetAlbedos, fnFreeAlbedo, fnSetAlphas, fnCopyHeightmaps,
                                        fnSaveGBuffer, fnSaveCookie, fnSetCompression, nullptr);
            }
            else if (h.cmd == CMD_SET_FIX_POS)
            {
                status = handle_command(h.cmd, payload, fnInit, fnLoadScene, fnRenderEvent, fnUVGB, fnAlpha,
                                        fnABG, fnUVGBErr, fnSetAlbedos, fnFreeAlbedo, fnSetAlphas, fnCopyHeightmaps,
                                        fnSaveGBuffer, fnSaveCookie, fnSetFixPos, nullptr);
            }
            else if (h.cmd == CMD_SET_UVGB_FLAGS)
            {
                status = handle_command(h.cmd, payload, fnInit, fnLoadScene, fnRenderEvent, fnUVGB, fnAlpha,
                                        fnABG, fnUVGBErr, fnSetAlbedos, fnFreeAlbedo, fnSetAlphas, fnCopyHeightmaps,
                                        fnSaveGBuffer, fnSaveCookie, nullptr, fnSetUVGBFlags);
            }
            else if (h.cmd == CMD_SET_UVGB_TANGENTS)
            {
                status = handle_command(h.cmd, payload, fnInit, fnLoadScene, fnRenderEvent, fnUVGB, fnAlpha,
                                        fnABG, fnUVGBErr, fnSetAlbedos, fnFreeAlbedo, fnSetAlphas, fnCopyHeightmaps,
                                        fnSaveGBuffer, fnSaveCookie, nullptr, fnSetUVGBTangents);
            }
            else if (h.cmd == CMD_TOGGLE_ABG_DITHER)
            {
                status = handle_command(h.cmd, payload, fnInit, fnLoadScene, fnRenderEvent, fnUVGB, fnAlpha,
                                        fnABG, fnUVGBErr, fnSetAlbedos, fnFreeAlbedo, fnSetAlphas, fnCopyHeightmaps,
                                        fnSaveGBuffer, fnSaveCookie, nullptr, fnToggleABGDither);
            }
            else
            {
                status = handle_command(h.cmd, payload, fnInit, fnLoadScene, fnRenderEvent, fnUVGB, fnAlpha,
                                        fnABG, fnUVGBErr, fnSetAlbedos, fnFreeAlbedo, fnSetAlphas, fnCopyHeightmaps,
                                        fnSaveGBuffer, fnSaveCookie, nullptr, nullptr);
            }

            if (!send_all(cli, &status, sizeof(status))) break;
        }

        shutdown(cli, SD_BOTH);
        closesocket(cli);
        }).detach();
    }

    closesocket(srv);
    WSACleanup();
    return 0;
}
