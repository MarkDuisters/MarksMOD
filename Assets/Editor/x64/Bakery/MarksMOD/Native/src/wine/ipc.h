#pragma once

#include <cstdint>
#include <cstring>
#include <string>
#include <vector>

#define BAKERY_WINE_PORT 27778
#define BAKERY_IPC_MAGIC 0x42414B31u

enum BakeryIpcCmd : uint32_t
{
    CMD_PING = 1,
    CMD_INIT_SHADERS = 2,
    CMD_LOAD_SCENE = 3,
    CMD_UNITY_RENDER_EVENT = 4,
    CMD_RENDER_UVGBUFFER = 5,
    CMD_GENERATE_ALPHA = 6,
    CMD_SHUTDOWN = 7,
    CMD_GET_ABG_ERROR = 8,
    CMD_GET_UVGB_ERROR = 9,
    CMD_SET_ALBEDOS = 10,
    CMD_FREE_ALBEDO_COPIES = 11,
    CMD_SET_ALPHAS_FROM_RAM = 12,
    CMD_COPY_HEIGHTMAPS_FROM_RAM = 13,
    CMD_SAVE_GBUFFER_MAP_FROM_RAM = 14,
    CMD_SAVE_COOKIE_FROM_RAM = 15,
    CMD_SET_COMPRESSION = 16,
    CMD_SET_FIX_POS = 17,
    CMD_SET_UVGB_FLAGS = 18,
    CMD_SET_UVGB_TANGENTS = 19,
    CMD_TOGGLE_ABG_DITHER = 20
};

#pragma pack(push, 1)
struct BakeryIpcHeader
{
    uint32_t magic;
    uint32_t cmd;
    uint32_t len;
};
#pragma pack(pop)

namespace bakery_ipc
{

class Writer
{
public:
    std::vector<uint8_t> data;

    void raw(const void* src, size_t len)
    {
        const uint8_t* p = (const uint8_t*)src;
        data.insert(data.end(), p, p + len);
    }

    void u8(uint8_t v) { data.push_back(v); }
    void u16(uint16_t v) { raw(&v, sizeof(v)); }
    void i32(int32_t v) { raw(&v, sizeof(v)); }
    void f32(float v) { raw(&v, sizeof(v)); }

    void str(const std::string& s)
    {
        i32((int32_t)s.size());
        raw(s.data(), s.size());
    }
};

class Reader
{
public:
    Reader(const void* src, size_t len) : p((const uint8_t*)src), n(len) {}

    bool raw(void* dst, size_t len)
    {
        if (n < len) return false;
        std::memcpy(dst, p, len);
        p += len;
        n -= len;
        return true;
    }

    bool u8(uint8_t& v) { return raw(&v, sizeof(v)); }
    bool u16(uint16_t& v) { return raw(&v, sizeof(v)); }
    bool i32(int32_t& v) { return raw(&v, sizeof(v)); }
    bool f32(float& v) { return raw(&v, sizeof(v)); }

    bool str(std::string& s)
    {
        int32_t len = 0;
        if (!i32(len) || len < 0 || (size_t)len > n) return false;
        s.assign((const char*)p, (size_t)len);
        p += len;
        n -= (size_t)len;
        return true;
    }

private:
    const uint8_t* p;
    size_t n;
};

}
