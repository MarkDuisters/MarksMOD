#include "abi.h"
#include "ipc.h"

#include <arpa/inet.h>
#include <fcntl.h>
#include <netinet/in.h>
#include <netinet/tcp.h>
#include <sys/socket.h>
#include <unistd.h>

#include <chrono>
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <mutex>
#include <string>
#include <thread>
#include <vector>

#if defined(__GNUC__)
#define XATLAS_WINE_API extern "C" __attribute__((visibility("default")))
#else
#define XATLAS_WINE_API extern "C"
#endif

namespace {
std::mutex g_lock;
int g_socket = -1;
const uint32_t MAX_FRAME = 512u * 1024u * 1024u;

bool io_all(int s, void* data, size_t len, bool write)
{
    char* p = (char*)data;
    while (len) {
        ssize_t n = write ? send(s, p, len, 0) : recv(s, p, len, MSG_WAITALL);
        if (n <= 0) return false;
        p += n; len -= (size_t)n;
    }
    return true;
}

void start_host_if_configured()
{
    const char* script = std::getenv("BAKERY_WINE_SCRIPT");
    if (!script || !*script) return;
    pid_t pid = fork();
    if (pid == 0) {
        int nullfd = open("/dev/null", O_WRONLY);
        if (nullfd >= 0) {
            dup2(nullfd, STDOUT_FILENO);
            dup2(nullfd, STDERR_FILENO);
            close(nullfd);
        }
        execl("/bin/bash", "bash", script, "--start", (char*)nullptr);
        _exit(127);
    }
}

bool ipc_logging()
{
    const char* value = std::getenv("BAKERY_XATLAS_IPC_LOG");
    return value && std::strcmp(value, "1") == 0;
}

int connect_host()
{
    for (int attempt = 0; attempt < 600; attempt++) {
        int s = socket(AF_INET, SOCK_STREAM, 0);
        if (s >= 0) {
            sockaddr_in addr = {};
            addr.sin_family = AF_INET; addr.sin_port = htons(BAKERY_WINE_PORT);
            inet_pton(AF_INET, "127.0.0.1", &addr.sin_addr);
            if (connect(s, (sockaddr*)&addr, sizeof(addr)) == 0) {
                int one = 1;
                setsockopt(s, IPPROTO_TCP, TCP_NODELAY, &one, sizeof(one));
                timeval tv = {}; tv.tv_sec = 3600;
                setsockopt(s, SOL_SOCKET, SO_RCVTIMEO, &tv, sizeof(tv));
                setsockopt(s, SOL_SOCKET, SO_SNDTIMEO, &tv, sizeof(tv));
                return s;
            }
            close(s);
        }
        if (attempt == 0) start_host_if_configured();
        std::this_thread::sleep_for(std::chrono::milliseconds(100));
    }
    std::fprintf(stderr, "xatlas_wine: host unavailable; set BAKERY_WINE_HOST to bakery_wine_host.exe\n");
    return -1;
}

int call(uint32_t cmd, const bakery_ipc::Writer& request, std::vector<uint8_t>& response)
{
    std::lock_guard<std::mutex> guard(g_lock);
    if (ipc_logging()) std::fprintf(stderr, "xatlas_wine ipc -> %s (%u)\n", bakery_ipc::command_name(cmd), cmd);
    if (g_socket < 0) g_socket = connect_host();
    if (g_socket < 0) return -1;
    BakeryIpcHeader h = {BAKERY_IPC_MAGIC, cmd, (uint32_t)request.data.size()};
    int32_t status = -1; uint32_t len = 0;
    if (request.data.size() > MAX_FRAME || !io_all(g_socket, &h, sizeof(h), true) ||
        !io_all(g_socket, (void*)request.data.data(), request.data.size(), true) ||
        !io_all(g_socket, &status, sizeof(status), false) || !io_all(g_socket, &len, sizeof(len), false) || len > MAX_FRAME) {
        close(g_socket); g_socket = -1; return -1;
    }
    response.resize(len);
    if (len && !io_all(g_socket, response.data(), len, false)) { close(g_socket); g_socket = -1; return -1; }
    return status;
}

bool u64(bakery_ipc::Writer& w, void* handle) { uint64_t v = (uint64_t)(uintptr_t)handle; w.raw(&v, sizeof(v)); return true; }
void* handle_from(const std::vector<uint8_t>& data) { if (data.size() < 8) return nullptr; uint64_t v = 0; std::memcpy(&v, data.data(), 8); return (void*)(uintptr_t)v; }
int query(uint32_t cmd, void* atlas, int a, int b = 0)
{
    bakery_ipc::Writer w; u64(w, atlas);
    if (cmd != CMD_XATLAS_GET_ATLAS_COUNT) w.i32(a);
    if (cmd == CMD_XATLAS_GET_ATLAS_INDEX) w.i32(b);
    std::vector<uint8_t> out; int status = call(cmd, w, out); if (status != 0 || out.size() != 4) return status ? status : -1;
    int32_t v = 0; std::memcpy(&v, out.data(), 4); return v;
}
}

XATLAS_WINE_API void* xatlasCreateAtlas()
{
    bakery_ipc::Writer w; std::vector<uint8_t> out; if (call(CMD_XATLAS_CREATE, w, out) != 0 || out.size() != 8) return nullptr; return handle_from(out);
}
XATLAS_WINE_API int xatlasAddMesh(void* a, int vc, const void* p, const void* n, const void* uv, int ic, const int* idx)
{
    if (vc < 0 || ic < 0 || (uint64_t)vc * 32 + (uint64_t)ic * 4 > MAX_FRAME) return -2;
    bakery_ipc::Writer w; u64(w,a); w.i32(vc); w.u8(n ? 1 : 0); w.u8(uv ? 1 : 0); w.i32(ic);
    if (vc) w.raw(p, (size_t)vc * 12); if (n) w.raw(n, (size_t)vc * 12); if (uv) w.raw(uv, (size_t)vc * 8); if (ic) w.raw(idx, (size_t)ic * 4);
    std::vector<uint8_t> out; return call(CMD_XATLAS_ADD_MESH, w, out);
}
XATLAS_WINE_API int xatlasAddUVMesh(void* a, int vc, const void* uv, int ic, const int* idx, ft_bool rotate)
{
    if (vc < 0 || ic < 0 || (uint64_t)vc * 8 + (uint64_t)ic * 4 > MAX_FRAME) return -2;
    bakery_ipc::Writer w; u64(w,a); w.i32(vc); w.i32(ic); w.i32(rotate != 0); if (vc) w.raw(uv,(size_t)vc*8); if (ic) w.raw(idx,(size_t)ic*4);
    std::vector<uint8_t> out; return call(CMD_XATLAS_ADD_UV_MESH,w,out);
}
XATLAS_WINE_API void xatlasParametrize(void* a) { bakery_ipc::Writer w; u64(w,a); std::vector<uint8_t> out; call(CMD_XATLAS_PARAMETRIZE,w,out); }
XATLAS_WINE_API void xatlasPack(void* a,int attempts,float tpu,int res,int maxChart,int padding,ft_bool brute,ft_bool block)
{ bakery_ipc::Writer w; u64(w,a); w.i32(attempts); w.f32(tpu); w.i32(res); w.i32(maxChart); w.i32(padding); w.i32(brute != 0); w.i32(block != 0); std::vector<uint8_t> out; call(CMD_XATLAS_PACK,w,out); }
XATLAS_WINE_API void xatlasNormalize(void* a,const int* sizes,ft_bool density)
{ bakery_ipc::Writer w; u64(w,a); w.i32(density != 0); int count = sizes ? query(CMD_XATLAS_GET_ATLAS_COUNT,a,0) : -1; w.i32(count); if (count > 0) w.raw(sizes,(size_t)count*4); std::vector<uint8_t> out; if (call(CMD_XATLAS_NORMALIZE,w,out) == 0 && sizes && count > 0 && out.size() == (size_t)count * 4) std::memcpy((void*)sizes,out.data(),out.size()); }
XATLAS_WINE_API int xatlasGetAtlasCount(void* a) { return query(CMD_XATLAS_GET_ATLAS_COUNT,a,0); }
XATLAS_WINE_API int xatlasGetAtlasIndex(void* a,int m,int c) { return query(CMD_XATLAS_GET_ATLAS_INDEX,a,m,c); }
XATLAS_WINE_API int xatlasGetVertexCount(void* a,int m) { return query(CMD_XATLAS_GET_VERTEX_COUNT,a,m); }
XATLAS_WINE_API int xatlasGetIndexCount(void* a,int m) { return query(CMD_XATLAS_GET_INDEX_COUNT,a,m); }
XATLAS_WINE_API void xatlasGetData(void* a,int m,void* uv,void* refs,void* indices)
{
    int vc = xatlasGetVertexCount(a,m), ic = xatlasGetIndexCount(a,m); if (vc < 0 || ic < 0) return;
    bakery_ipc::Writer w; u64(w,a); w.i32(m); w.i32(vc); w.i32(ic); std::vector<uint8_t> out;
    if (call(CMD_XATLAS_GET_DATA,w,out) != 0 || out.size() != 8u + (size_t)vc*8 + (size_t)vc*4 + (size_t)ic*4) return;
    const uint8_t* p = out.data()+8; if (uv) std::memcpy(uv,p,(size_t)vc*8); p += (size_t)vc*8; if (refs) std::memcpy(refs,p,(size_t)vc*4); p += (size_t)vc*4; if (indices) std::memcpy(indices,p,(size_t)ic*4);
}
XATLAS_WINE_API int xatlasClear(void* a) { if (!a) return 1; bakery_ipc::Writer w; u64(w,a); std::vector<uint8_t> out; return call(CMD_XATLAS_CLEAR,w,out); }
