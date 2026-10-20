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
        std::fprintf(stderr, "bakery_wine_host: bind/listen failed\n");
        return 1;
    }

    std::fprintf(stderr, "bakery_wine_host: listening on 127.0.0.1:%d\n", BAKERY_WINE_PORT);

    bool running = true;
    while (running)
    {
        SOCKET cli = accept(srv, nullptr, nullptr);
        if (cli == INVALID_SOCKET) continue;

        DWORD rcvTimeout = 60000;
        setsockopt(cli, SOL_SOCKET, SO_RCVTIMEO, (const char*)&rcvTimeout, sizeof(rcvTimeout));

        while (running)
        {
            BakeryIpcHeader h = {};
            if (!recv_all(cli, &h, sizeof(h))) break;
            if (h.magic != BAKERY_IPC_MAGIC) break;
            if (h.len > MAX_FRAME) break;

            std::vector<uint8_t> payload(h.len);
            if (h.len > 0 && !recv_all(cli, payload.data(), (int)h.len)) break;

            if (h.cmd == CMD_SHUTDOWN)
            {
                running = false;
                int32_t status = 0;
                send_all(cli, &status, sizeof(status));
                break;
            }

            int32_t status;
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
    }

    closesocket(srv);
    WSACleanup();
    return 0;
}
