#include "frender_abi.h"
#include "ipc.h"

#include <arpa/inet.h>
#include <netinet/in.h>
#include <sys/socket.h>
#include <sys/time.h>
#include <unistd.h>

#include <cstdio>
#include <cstring>
#include <mutex>
#include <string>
#include <vector>

namespace {

std::mutex g_lock;
int g_socket = -1;

bool send_all(int s, const void* src, size_t len)
{
    const char* p = (const char*)src;
    while (len > 0)
    {
        ssize_t n = send(s, p, len, 0);
        if (n <= 0) return false;
        p += n;
        len -= (size_t)n;
    }
    return true;
}

bool recv_all(int s, void* dst, size_t len)
{
    char* p = (char*)dst;
    while (len > 0)
    {
        ssize_t n = recv(s, p, len, MSG_WAITALL);
        if (n <= 0) return false;
        p += n;
        len -= (size_t)n;
    }
    return true;
}

int connect_host()
{
    int s = socket(AF_INET, SOCK_STREAM, 0);
    if (s < 0) return -1;

    sockaddr_in addr = {};
    addr.sin_family = AF_INET;
    addr.sin_port = htons(BAKERY_WINE_PORT);
    inet_pton(AF_INET, "127.0.0.1", &addr.sin_addr);

    if (connect(s, (sockaddr*)&addr, sizeof(addr)) != 0)
    {
        close(s);
        return -1;
    }

    timeval tv = {};
    tv.tv_sec = 3600;
    setsockopt(s, SOL_SOCKET, SO_RCVTIMEO, &tv, sizeof(tv));
    setsockopt(s, SOL_SOCKET, SO_SNDTIMEO, &tv, sizeof(tv));

    return s;
}

int call_host(uint32_t cmd, const void* payload, uint32_t len, int32_t* out)
{
    std::lock_guard<std::mutex> guard(g_lock);

    if (g_socket < 0) g_socket = connect_host();
    if (g_socket < 0)
    {
        std::fprintf(stderr, "frender(linux): wine host unavailable on 127.0.0.1:%d\n", BAKERY_WINE_PORT);
        return -1;
    }

    BakeryIpcHeader header = { BAKERY_IPC_MAGIC, cmd, len };
    int32_t status = -1;

    if (!send_all(g_socket, &header, sizeof(header)) ||
        (len > 0 && !send_all(g_socket, payload, len)) ||
        !recv_all(g_socket, &status, sizeof(status)))
    {
        close(g_socket);
        g_socket = -1;
        return -1;
    }

    if (out) *out = status;
    return 0;
}

int forward(uint32_t cmd, const bakery_ipc::Writer& writer)
{
    int32_t status = -1;
    if (call_host(cmd, writer.data.empty() ? nullptr : writer.data.data(), (uint32_t)writer.data.size(), &status) != 0)
    {
        return -1;
    }
    return status;
}

int forward_int(uint32_t cmd, int value)
{
    bakery_ipc::Writer writer;
    writer.i32(value);
    return forward(cmd, writer);
}

int forward_bool(uint32_t cmd, ft_bool value)
{
    bakery_ipc::Writer writer;
    writer.i32(value != 0 ? 1 : 0);
    return forward(cmd, writer);
}

std::string wine_path(const ft_utf8* path)
{
    if (!path) return std::string();
    std::string p(path);
    if (p.empty() || p[0] != '/') return p;

    std::string out = "Z:";
    for (char c : p) out += (c == '/') ? '\\' : c;
    return out;
}

void not_bridged(const char* fn)
{
    std::fprintf(stderr, "frender(linux): %s is not used on Linux\n", fn);
}

}

extern "C" {

void UnitySetGraphicsDevice(void*, int, int) {}
void UnityPluginLoad(void*) {}
void UnityPluginUnload() {}

void UnityRenderEvent(int eventID)
{
    bakery_ipc::Writer writer;
    writer.i32(eventID);
    forward(CMD_UNITY_RENDER_EVENT, writer);
}

void InitShaders()
{
    call_host(CMD_INIT_SHADERS, nullptr, 0, nullptr);
}

void LoadScene(const ft_utf8* path)
{
    std::string p = wine_path(path);
    call_host(CMD_LOAD_SCENE, p.c_str(), (uint32_t)(p.size() + 1), nullptr);
}

void SetAlbedos(int count, void**)
{
    bakery_ipc::Writer writer;
    writer.i32(count);
    forward(CMD_SET_ALBEDOS, writer);
}

int CopyAlbedos()
{
    not_bridged("CopyAlbedos");
    return 1;
}

int CopyHeightmapsFromRAM(int count, const FTTexInput* tex)
{
    bakery_ipc::Writer writer;
    writer.i32(count);

    for (int i = 0; i < count; i++)
    {
        const FTTexInput& t = tex[i];
        int32_t size = (int32_t)t.width * (int32_t)t.height * 4;
        writer.i32(size);
        writer.u16(t.width);
        writer.u16(t.height);
        if (size > 0 && t.data) writer.raw(t.data, (size_t)size);
    }

    return forward(CMD_COPY_HEIGHTMAPS_FROM_RAM, writer);
}

void FreeAlbedoCopies()
{
    call_host(CMD_FREE_ALBEDO_COPIES, nullptr, 0, nullptr);
}

void SetAlphas(int, void**, const float*, const int*, int, ft_bool)
{
    not_bridged("SetAlphas");
}

void SetAlphasFromRAM(int count, void* tex, const float* alphaRefs, const int* alphaChannels, int numLODs, ft_bool flip)
{
    bakery_ipc::Writer writer;
    writer.i32(count);

    const FTTexInput* inputs = (const FTTexInput*)tex;
    for (int i = 0; i < count; i++)
    {
        const FTTexInput& t = inputs[i];
        int32_t size = (int32_t)t.width * (int32_t)t.height * 4;
        writer.i32(size);
        writer.u16(t.width);
        writer.u16(t.height);
        if (size > 0 && t.data) writer.raw(t.data, (size_t)size);
    }

    for (int i = 0; i < count; i++) writer.f32(alphaRefs[i]);
    for (int i = 0; i < count; i++) writer.i32(alphaChannels[i]);

    writer.i32(numLODs);
    writer.u8(flip != 0 ? 1 : 0);

    forward(CMD_SET_ALPHAS_FROM_RAM, writer);
}

int ToggleABGDither(int on)
{
    return forward_int(CMD_TOGGLE_ABG_DITHER, on);
}

void SaveSky(void*, float, float, float, float, float, float, float, float, float, const ft_utf8*, ft_bool, ft_bool, ft_bool)
{
    not_bridged("SaveSky");
}

void SaveCookie(void*, const ft_utf8*)
{
    not_bridged("SaveCookie");
}

void SaveCookieFromRAM(FTTexInput tex, const ft_utf8* path)
{
    bakery_ipc::Writer writer;

    int32_t size = (int32_t)tex.width * (int32_t)tex.height * 16;
    writer.i32(size);
    writer.u16(tex.width);
    writer.u16(tex.height);
    if (size > 0 && tex.data) writer.raw(tex.data, (size_t)size);
    writer.str(wine_path(path));

    forward(CMD_SAVE_COOKIE_FROM_RAM, writer);
}

int ftRenderUVGBuffer()
{
    int32_t status = -1;
    if (call_host(CMD_RENDER_UVGBUFFER, nullptr, 0, &status) != 0) return -1;
    return status;
}

void SetUVGBFlags(int flags)
{
    forward_int(CMD_SET_UVGB_FLAGS, flags);
}

void SetUVGBTangents(int hasTangents)
{
    forward_int(CMD_SET_UVGB_TANGENTS, hasTangents);
}

void SetFixPos(ft_bool enabled)
{
    forward_bool(CMD_SET_FIX_POS, enabled);
}

void SetCompression(ft_bool enabled)
{
    forward_bool(CMD_SET_COMPRESSION, enabled);
}

int ftGenerateAlphaBuffer()
{
    int32_t status = -1;
    if (call_host(CMD_GENERATE_ALPHA, nullptr, 0, &status) != 0) return -1;
    return status;
}

int GetABGErrorCode()
{
    int32_t status = -1;
    if (call_host(CMD_GET_ABG_ERROR, nullptr, 0, &status) != 0) return -1;
    return status;
}

int GetUVGBErrorCode()
{
    int32_t status = -1;
    if (call_host(CMD_GET_UVGB_ERROR, nullptr, 0, &status) != 0) return -1;
    return status;
}

int SaveGBufferMap(void*, const ft_utf8*, ft_bool)
{
    not_bridged("SaveGBufferMap");
    return 1;
}

int SaveGBufferMapFromRAM(const uint8_t* tex, int size, const ft_utf8* path, ft_bool compressed)
{
    bakery_ipc::Writer writer;
    writer.i32(size);
    if (size > 0 && tex) writer.raw(tex, (size_t)size);
    writer.str(wine_path(path));
    writer.u8(compressed != 0 ? 1 : 0);
    return forward(CMD_SAVE_GBUFFER_MAP_FROM_RAM, writer);
}

}
