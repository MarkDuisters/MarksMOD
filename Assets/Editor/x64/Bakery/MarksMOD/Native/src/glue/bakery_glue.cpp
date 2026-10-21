#include "abi.h"

#include <cerrno>
#include <climits>
#include <cmath>
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <string>
#include <vector>
#include <sys/wait.h>
#include <signal.h>
#include <unistd.h>

#include <dlfcn.h>
#include <lz4.h>

namespace {

std::string to_utf8(const char16_t* s)
{
    std::string out;
    if (!s) return out;
    while (*s)
    {
        uint32_t c = *s++;
        if (c >= 0xD800 && c <= 0xDBFF && *s >= 0xDC00 && *s <= 0xDFFF)
        {
            uint32_t lo = *s++;
            c = 0x10000u + ((c - 0xD800u) << 10) + (lo - 0xDC00u);
        }
        if (c < 0x80)
        {
            out.push_back((char)c);
        }
        else if (c < 0x800)
        {
            out.push_back((char)(0xC0 | (c >> 6)));
            out.push_back((char)(0x80 | (c & 0x3F)));
        }
        else if (c < 0x10000)
        {
            out.push_back((char)(0xE0 | (c >> 12)));
            out.push_back((char)(0x80 | ((c >> 6) & 0x3F)));
            out.push_back((char)(0x80 | (c & 0x3F)));
        }
        else
        {
            out.push_back((char)(0xF0 | (c >> 18)));
            out.push_back((char)(0x80 | ((c >> 12) & 0x3F)));
            out.push_back((char)(0x80 | ((c >> 6) & 0x3F)));
            out.push_back((char)(0x80 | (c & 0x3F)));
        }
    }
    return out;
}

const char* env_or(const char* name, const char* fallback)
{
    const char* v = std::getenv(name);
    return (v && *v) ? v : fallback;
}

std::string library_dir()
{
    Dl_info info;
    if (dladdr((void*)&library_dir, &info) && info.dli_fname)
    {
        std::string p(info.dli_fname);
        auto pos = p.find_last_of('/');
        if (pos != std::string::npos) return p.substr(0, pos);
    }
    return ".";
}

std::string shell_quote(const std::string& s)
{
    std::string out = "'";
    for (char c : s)
    {
        if (c == '\'') out += "'\\''";
        else out += c;
    }
    out += "'";
    return out;
}

struct LaunchSpec
{
    std::string command;
    std::string cwd;
};

LaunchSpec build_launch(const std::string& raw, bool setWorkDir)
{
    LaunchSpec spec;
    spec.cwd = library_dir();

    size_t split = raw.find_first_of(" \t");
    std::string first = split == std::string::npos ? raw : raw.substr(0, split);
    std::string rest = split == std::string::npos ? std::string() : raw.substr(split);

    std::string runner = env_or("BAKERY_RUNNER", "wine");
    if (runner == "none") runner.clear();

    std::string tools = env_or("BAKERY_TOOLS_DIR", (library_dir() + "/../bin").c_str());
    std::string ext = env_or("BAKERY_TOOL_EXT", runner.empty() ? "" : ".exe");

    std::string exe = first;
    if (first.find('/') == std::string::npos)
    {
        exe = tools + "/" + first;
        if (!ext.empty() && (exe.size() < ext.size() || exe.compare(exe.size() - ext.size(), ext.size(), ext) != 0))
        {
            exe += ext;
        }
    }

    spec.command = runner.empty() ? shell_quote(exe) : (runner + " " + shell_quote(exe));
    spec.command += rest;

    if (!setWorkDir) spec.cwd = "";
    else spec.cwd = tools;

    return spec;
}

struct Proc
{
    pid_t pid;
    int status;
    bool done;
};

Proc* spawn(const LaunchSpec& spec)
{
    if (spec.command.empty()) return nullptr;

    FILE* logfile = std::fopen("/tmp/bakery_launch.log", "a");
    if (logfile)
    {
        std::fprintf(logfile, "cwd=%s cmd=%s\n", spec.cwd.c_str(), spec.command.c_str());
        std::fclose(logfile);
    }

    pid_t pid = fork();
    if (pid < 0) return nullptr;

    if (pid == 0)
    {
        if (!spec.cwd.empty()) chdir(spec.cwd.c_str());

        const char* prefix = std::getenv("BAKERY_WINEPREFIX");
        if (prefix && *prefix) setenv("WINEPREFIX", prefix, 1);

        execl("/bin/sh", "sh", "-c", spec.command.c_str(), (char*)nullptr);
        _exit(127);
    }

    Proc* p = new Proc();
    p->pid = pid;
    p->status = 0;
    p->done = false;
    return p;
}

uint16_t read_u16(const uint8_t* p)
{
    return (uint16_t)((uint16_t)p[0] | ((uint16_t)p[1] << 8));
}

uint32_t read_u32(const uint8_t* p)
{
    return (uint32_t)p[0] | ((uint32_t)p[1] << 8) | ((uint32_t)p[2] << 16) | ((uint32_t)p[3] << 24);
}

bool has_lz4_extension(const std::string& path)
{
    if (path.size() < 4) return false;
    size_t n = path.size();
    return path[n - 4] == '.' &&
           (path[n - 3] == 'l' || path[n - 3] == 'L') &&
           (path[n - 2] == 'z' || path[n - 2] == 'Z') &&
           (path[n - 1] == '4');
}

bool read_binary_file(const std::string& path, std::vector<uint8_t>& out)
{
    std::FILE* f = std::fopen(path.c_str(), "rb");
    if (!f) return false;
    std::fseek(f, 0, SEEK_END);
    long long size = std::ftell(f);
    if (size < 0) { std::fclose(f); return false; }
    std::fseek(f, 0, SEEK_SET);
    out.resize((size_t)size);
    bool ok = out.empty() || std::fread(out.data(), 1, out.size(), f) == out.size();
    std::fclose(f);
    return ok;
}

// Bakery's .lz4 container is a u32 decompressed size followed by one raw LZ4
// block. The decompressed payload is a complete DDS file.
bool read_texture_blob(const std::string& path, std::vector<uint8_t>& blob)
{
    if (!has_lz4_extension(path)) return read_binary_file(path, blob);

    std::FILE* f = std::fopen(path.c_str(), "rb");
    if (!f) return false;
    uint8_t size_bytes[4];
    if (std::fread(size_bytes, 1, 4, f) != 4) { std::fclose(f); return false; }
    uint32_t decompressed_size = read_u32(size_bytes);
    if (decompressed_size > (uint32_t)INT32_MAX) { std::fclose(f); return false; }

    std::fseek(f, 0, SEEK_END);
    long long file_size = std::ftell(f);
    if (file_size < 4) { std::fclose(f); return false; }
    std::fseek(f, 4, SEEK_SET);
    std::vector<uint8_t> compressed((size_t)(file_size - 4));
    bool ok = compressed.empty() || std::fread(compressed.data(), 1, compressed.size(), f) == compressed.size();
    std::fclose(f);
    if (!ok) return false;

    blob.resize(decompressed_size);
    int result = LZ4_decompress_safe((const char*)compressed.data(), (char*)blob.data(),
                                     (int)compressed.size(), (int)decompressed_size);
    return result == (int)decompressed_size;
}

enum class SourceFormat
{
    Unknown,
    Half4,
    Float4,
    RGBA8,
    RGB8,
    R8,
    R16,
    RG16,
    RGBA16,
    R16F,
    RG16F,
    R32F,
    RG32F,
    RGB32F
};

struct Texture
{
    uint32_t width = 0;
    uint32_t height = 0;
    SourceFormat format = SourceFormat::Unknown;
    int bytes_per_pixel = 0;
    uint32_t rmask = 0;
    uint32_t gmask = 0;
    uint32_t bmask = 0;
    uint32_t amask = 0;
    size_t pixel_offset = 128;
    std::vector<uint8_t> blob;

    const uint8_t* pixels() const { return blob.data() + pixel_offset; }
    size_t payload_size() const { return blob.size() - pixel_offset; }
};

void set_rgba8_masks(Texture& t, uint32_t r, uint32_t g, uint32_t b, uint32_t a)
{
    t.format = SourceFormat::RGBA8;
    t.bytes_per_pixel = 4;
    t.rmask = r;
    t.gmask = g;
    t.bmask = b;
    t.amask = a;
}

bool parse_texture(const std::string& path, Texture& tex)
{
    if (!read_texture_blob(path, tex.blob)) return false;
    if (tex.blob.size() < 128 || std::memcmp(tex.blob.data(), "DDS ", 4) != 0) return false;

    const uint8_t* h = tex.blob.data();
    tex.height = read_u32(h + 0x0c);
    tex.width = read_u32(h + 0x10);
    if (tex.width == 0 || tex.height == 0) return false;

    uint32_t pf_flags = read_u32(h + 0x50);
    uint32_t fourcc = read_u32(h + 0x54);
    uint32_t bits = read_u32(h + 0x58);
    tex.pixel_offset = 128;

    const uint32_t DDPF_FOURCC = 0x4;
    const uint32_t DDPF_RGB = 0x40;
    const uint32_t DDPF_LUMINANCE = 0x20000;

    if (fourcc == 0x30315844) // "DX10"
    {
        if (tex.blob.size() < 148) return false;
        uint32_t dxgi = read_u32(h + 0x80);
        tex.pixel_offset = 148;
        switch (dxgi)
        {
        case 2:  tex.format = SourceFormat::Float4;  tex.bytes_per_pixel = 16; break;
        case 6:  tex.format = SourceFormat::RGB32F;  tex.bytes_per_pixel = 12; break;
        case 10: tex.format = SourceFormat::Half4;   tex.bytes_per_pixel = 8; break;
        case 11: tex.format = SourceFormat::RGBA16;  tex.bytes_per_pixel = 8; break;
        case 16: tex.format = SourceFormat::RG32F;   tex.bytes_per_pixel = 8; break;
        case 28:
        case 29: set_rgba8_masks(tex, 0x000000ff, 0x0000ff00, 0x00ff0000, 0xff000000); break;
        case 34: tex.format = SourceFormat::RG16F;   tex.bytes_per_pixel = 4; break;
        case 41: tex.format = SourceFormat::R32F;    tex.bytes_per_pixel = 4; break;
        case 54: tex.format = SourceFormat::R16F;    tex.bytes_per_pixel = 2; break;
        case 60:
        case 61: tex.format = SourceFormat::R8;      tex.bytes_per_pixel = 1; break;
        case 87:
        case 88: set_rgba8_masks(tex, 0x00ff0000, 0x0000ff00, 0x000000ff, 0xff000000); break;
        default: return false;
        }
    }
    else if (pf_flags & DDPF_FOURCC)
    {
        switch (fourcc)
        {
        case 111: tex.format = SourceFormat::R16F;  tex.bytes_per_pixel = 2; break;
        case 112: tex.format = SourceFormat::RG16F; tex.bytes_per_pixel = 4; break;
        case 113: tex.format = SourceFormat::Half4; tex.bytes_per_pixel = 8; break;
        case 114: tex.format = SourceFormat::R32F;  tex.bytes_per_pixel = 4; break;
        case 115: tex.format = SourceFormat::RG32F; tex.bytes_per_pixel = 8; break;
        case 116: tex.format = SourceFormat::Float4; tex.bytes_per_pixel = 16; break;
        case 36:  tex.format = SourceFormat::RGBA16; tex.bytes_per_pixel = 8; break;
        default: return false;
        }
    }
    else if (pf_flags & DDPF_RGB)
    {
        uint32_t r = read_u32(h + 0x5c);
        uint32_t g = read_u32(h + 0x60);
        uint32_t b = read_u32(h + 0x64);
        uint32_t a = read_u32(h + 0x68);
        if (bits == 32)
        {
            set_rgba8_masks(tex, r, g, b, a);
        }
        else if (bits == 24)
        {
            tex.format = SourceFormat::RGB8;
            tex.bytes_per_pixel = 3;
            tex.rmask = r; tex.gmask = g; tex.bmask = b;
        }
        else if (bits == 16)
        {
            tex.format = SourceFormat::RG16;
            tex.bytes_per_pixel = 2;
            tex.rmask = r; tex.gmask = g; tex.bmask = b; tex.amask = a;
        }
        else
        {
            return false;
        }
    }
    else if (pf_flags & DDPF_LUMINANCE)
    {
        if (bits == 8) { tex.format = SourceFormat::R8; tex.bytes_per_pixel = 1; }
        else if (bits == 16) { tex.format = SourceFormat::R16; tex.bytes_per_pixel = 2; }
        else return false;
    }
    else
    {
        return false;
    }

    size_t needed = (size_t)tex.width * (size_t)tex.height * (size_t)tex.bytes_per_pixel;
    if (tex.pixel_offset + needed > tex.blob.size()) return false;
    return true;
}

inline float half_to_float(uint16_t value)
{
    uint32_t sign = ((uint32_t)value & 0x8000u) << 16;
    uint32_t exponent = ((uint32_t)value >> 10) & 0x1Fu;
    uint32_t mantissa = (uint32_t)value & 0x3FFu;
    uint32_t bits;
    if (exponent == 0)
    {
        if (mantissa == 0)
        {
            bits = sign;
        }
        else
        {
            int shift = 0;
            while ((mantissa & 0x400u) == 0) { mantissa <<= 1; ++shift; }
            mantissa &= 0x3FFu;
            uint32_t exp = (uint32_t)(113 - shift);
            bits = sign | (exp << 23) | (mantissa << 13);
        }
    }
    else if (exponent == 31)
    {
        bits = sign | 0x7F800000u | (mantissa << 13);
    }
    else
    {
        bits = sign | ((exponent + 112u) << 23) | (mantissa << 13);
    }
    float result;
    std::memcpy(&result, &bits, sizeof(result));
    return result;
}

inline float read_float(const uint8_t* p)
{
    float v;
    std::memcpy(&v, p, sizeof(v));
    return v;
}

inline float mask_channel(uint32_t value, uint32_t mask, float missing)
{
    if (mask == 0) return missing;
    int shift = 0;
    while (((mask >> shift) & 1u) == 0) ++shift;
    uint32_t max_value = mask >> shift;
    uint32_t component = (value & mask) >> shift;
    return (float)component * (1.0f / (float)max_value);
}

bool decode_pixel(const Texture& tex, size_t index, float out[4])
{
    const uint8_t* pixels = tex.pixels();
    switch (tex.format)
    {
    case SourceFormat::Half4:
    {
        const uint8_t* p = pixels + index * 8;
        out[0] = half_to_float(read_u16(p + 0));
        out[1] = half_to_float(read_u16(p + 2));
        out[2] = half_to_float(read_u16(p + 4));
        out[3] = half_to_float(read_u16(p + 6));
        return true;
    }
    case SourceFormat::Float4:
    {
        const uint8_t* p = pixels + index * 16;
        out[0] = read_float(p + 0);
        out[1] = read_float(p + 4);
        out[2] = read_float(p + 8);
        out[3] = read_float(p + 12);
        return true;
    }
    case SourceFormat::RGB32F:
    {
        const uint8_t* p = pixels + index * 12;
        out[0] = read_float(p + 0);
        out[1] = read_float(p + 4);
        out[2] = read_float(p + 8);
        out[3] = 1.0f;
        return true;
    }
    case SourceFormat::RGBA16:
    {
        const uint8_t* p = pixels + index * 8;
        out[0] = (float)read_u16(p + 0) * (1.0f / 65535.0f);
        out[1] = (float)read_u16(p + 2) * (1.0f / 65535.0f);
        out[2] = (float)read_u16(p + 4) * (1.0f / 65535.0f);
        out[3] = (float)read_u16(p + 6) * (1.0f / 65535.0f);
        return true;
    }
    case SourceFormat::RGBA8:
    {
        uint32_t value = read_u32(pixels + index * 4);
        out[0] = mask_channel(value, tex.rmask, 0.0f);
        out[1] = mask_channel(value, tex.gmask, 0.0f);
        out[2] = mask_channel(value, tex.bmask, 0.0f);
        out[3] = mask_channel(value, tex.amask, 1.0f);
        return true;
    }
    case SourceFormat::RGB8:
    {
        const uint8_t* p = pixels + index * 3;
        uint32_t value = (uint32_t)p[0] | ((uint32_t)p[1] << 8) | ((uint32_t)p[2] << 16);
        out[0] = mask_channel(value, tex.rmask, 0.0f);
        out[1] = mask_channel(value, tex.gmask, 0.0f);
        out[2] = mask_channel(value, tex.bmask, 0.0f);
        out[3] = 1.0f;
        return true;
    }
    case SourceFormat::R8:
        out[0] = (float)pixels[index] * (1.0f / 255.0f);
        out[1] = 0.0f;
        out[2] = 0.0f;
        out[3] = 1.0f;
        return true;
    case SourceFormat::R16:
        out[0] = (float)read_u16(pixels + index * 2) * (1.0f / 65535.0f);
        out[1] = 0.0f;
        out[2] = 0.0f;
        out[3] = 1.0f;
        return true;
    case SourceFormat::RG16:
    {
        const uint8_t* p = pixels + index * 2;
        uint32_t value = read_u16(p);
        out[0] = mask_channel(value, tex.rmask, 0.0f);
        out[1] = mask_channel(value, tex.gmask, 0.0f);
        out[2] = mask_channel(value, tex.bmask, 0.0f);
        out[3] = mask_channel(value, tex.amask, 1.0f);
        return true;
    }
    case SourceFormat::R16F:
        out[0] = half_to_float(read_u16(pixels + index * 2));
        out[1] = 0.0f;
        out[2] = 0.0f;
        out[3] = 1.0f;
        return true;
    case SourceFormat::RG16F:
    {
        const uint8_t* p = pixels + index * 4;
        out[0] = half_to_float(read_u16(p + 0));
        out[1] = half_to_float(read_u16(p + 2));
        out[2] = 0.0f;
        out[3] = 1.0f;
        return true;
    }
    case SourceFormat::R32F:
        out[0] = read_float(pixels + index * 4);
        out[1] = 0.0f;
        out[2] = 0.0f;
        out[3] = 1.0f;
        return true;
    case SourceFormat::RG32F:
    {
        const uint8_t* p = pixels + index * 8;
        out[0] = read_float(p + 0);
        out[1] = read_float(p + 4);
        out[2] = 0.0f;
        out[3] = 1.0f;
        return true;
    }
    default:
        return false;
    }
}

inline uint8_t unorm8_from_float(float value)
{
    float scaled = value * 255.0f;
    float clamped = fminf(255.0f, scaled);
    return (uint8_t)(int)clamped;
}

void encode_rgbm(const float rgba[4], float multiplier, uint8_t out[4])
{
    float r = rgba[0];
    float g = rgba[1];
    float b = rgba[2];
    if (multiplier != 1.0f)
    {
        r *= multiplier;
        g *= multiplier;
        b *= multiplier;
    }
    float vr = sqrtf(r) * 0.125f;
    float vg = sqrtf(g) * 0.125f;
    float vb = sqrtf(b) * 0.125f;

    float m = fmaxf(fmaxf(vr, vg), vb);
    m = fmaxf(m, 1.0f / 255.0f);
    m = fminf(1.0f, m);
    m = ceilf(m * 255.0f) / 255.0f;
    float inv = 1.0f / m;

    out[0] = unorm8_from_float(vr * inv);
    out[1] = unorm8_from_float(vg * inv);
    out[2] = unorm8_from_float(vb * inv);
    out[3] = unorm8_from_float(m);
}

int convert_texture(const std::string& input, void* values, int data_type, float multiplier)
{
    if (!values)
    {
        std::fprintf(stderr, "bakery_glue: halffloat2vb: null output buffer\n");
        return 23;
    }

    Texture tex;
    if (!parse_texture(input, tex))
    {
        std::fprintf(stderr, "bakery_glue: halffloat2vb: can't read texture %s\n", input.c_str());
        return 23;
    }

    const size_t pixel_count = (size_t)tex.width * (size_t)tex.height;
    uint8_t* dst = (uint8_t*)values;

    switch (data_type)
    {
    case 0: // DATATYPE_RGBA16ASRGBM
    {
        for (size_t i = 0; i < pixel_count; ++i)
        {
            float rgba[4];
            if (!decode_pixel(tex, i, rgba)) return 4;
            encode_rgbm(rgba, multiplier, dst + i * 4);
        }
        return 0;
    }
    case 1: // DATATYPE_RGBA8 - raw payload copy (matches the Windows DLL)
        if (tex.payload_size() < pixel_count * 4) return 2;
        std::memcpy(dst, tex.pixels(), pixel_count * 4);
        return 0;
    case 2: // DATATYPE_RGBA16ASRGBA32
    {
        for (size_t i = 0; i < pixel_count; ++i)
        {
            float rgba[4];
            if (!decode_pixel(tex, i, rgba)) return 4;
            if (multiplier != 1.0f)
            {
                rgba[0] *= multiplier;
                rgba[1] *= multiplier;
                rgba[2] *= multiplier;
                rgba[3] *= multiplier;
            }
            std::memcpy(dst + i * 16, rgba, 16);
        }
        return 0;
    }
    case 3: // DATATYPE_R8 - raw payload copy
        if (tex.payload_size() < pixel_count) return 2;
        std::memcpy(dst, tex.pixels(), pixel_count);
        return 0;
    case 4: // DATATYPE_RGBA32 - raw payload copy
        if (tex.payload_size() < pixel_count * 16) return 2;
        std::memcpy(dst, tex.pixels(), pixel_count * 16);
        return 0;
    default:
        return 4;
    }
}

}

extern "C" {

void* RunLocalProcess(const ft_wide* commandline, ft_bool setWorkDir)
{
    return spawn(build_launch(to_utf8(commandline), setWorkDir != 0));
}

void* RunLocalProcessVisible(const ft_wide* commandline)
{
    return spawn(build_launch(to_utf8(commandline), false));
}

ft_bool IsProcessFinished(void* proc)
{
    Proc* p = (Proc*)proc;
    if (!p || p->done) return 1;
    int status = 0;
    pid_t r = waitpid(p->pid, &status, WNOHANG);
    if (r == p->pid || (r < 0 && errno == ECHILD))
    {
        p->status = status;
        p->done = true;
        return 1;
    }
    return 0;
}

int GetProcessReturnValueAndClose(void* proc)
{
    Proc* p = (Proc*)proc;
    if (!p) return -1;
    if (!p->done)
    {
        int status = 0;
        if (waitpid(p->pid, &status, 0) == p->pid) p->status = status;
        p->done = true;
    }
    int code = -1;
    if (WIFEXITED(p->status)) code = WEXITSTATUS(p->status);
    delete p;
    return code;
}

int StopProcess(void* proc)
{
    Proc* p = (Proc*)proc;
    if (!p || p->done) return -1;
    kill(p->pid, SIGTERM);
    return 0;
}

int simpleProgressBarShow(const ft_utf8* header, const ft_utf8* msg, float, float, ft_bool)
{
    std::fprintf(stderr, "bakery_glue: %s - %s\n", header ? header : "", msg ? msg : "");
    return 0;
}

ft_bool simpleProgressBarCancelled() { return 0; }

void simpleProgressBarEnd() {}

void* GetForegroundWindow() { return (void*)1; }
int SetForegroundWindow(void*) { return 1; }
void* GetParent(void*) { return nullptr; }

int GetWindowText(void*, ft_utf8* text, int count)
{
    if (text && count > 0) text[0] = 0;
    return 0;
}

int GetClassName(void*, ft_utf8* text, int count)
{
    if (text && count > 0) text[0] = 0;
    return 0;
}

int halffloat2vb(const ft_wide* inputFilename, void* values, int dataType)
{
    if (!inputFilename) return 23;
    return convert_texture(to_utf8(inputFilename), values, dataType, 1.0f);
}

int halffloat2vbm(const ft_wide* inputFilename, void* values, int dataType, float multiplier)
{
    if (!inputFilename) return 23;
    return convert_texture(to_utf8(inputFilename), values, dataType, multiplier);
}

int uvrLoad(const float*, int, const int*, int) { return 0; }
int uvrRepack(float, int) { return 0; }
int uvrUnload() { return 0; }

void* xatlasCreateAtlas()
{
    std::fprintf(stderr, "bakery_glue: xatlasCreateAtlas not implemented\n");
    return nullptr;
}

int xatlasAddMesh(void*, int, const void*, const void*, const void*, int, const int*) { return 0; }
int xatlasAddUVMesh(void*, int, const void*, int, const int*, ft_bool) { return 0; }
void xatlasParametrize(void*) {}
void xatlasPack(void*, int, float, int, int, int, ft_bool, ft_bool) {}
void xatlasNormalize(void*, const int*, ft_bool) {}
int xatlasGetAtlasCount(void*) { return 0; }
int xatlasGetAtlasIndex(void*, int, int) { return 0; }
int xatlasGetVertexCount(void*, int) { return 0; }
int xatlasGetIndexCount(void*, int) { return 0; }
void xatlasGetData(void*, int, void*, void*, void*) {}
int xatlasClear(void*) { return 0; }

int ExtractZIP(const ft_wide*, int, const ft_utf8*, const ft_wide*)
{
    std::fprintf(stderr, "bakery_glue: ExtractZIP not implemented\n");
    return -1;
}

}
