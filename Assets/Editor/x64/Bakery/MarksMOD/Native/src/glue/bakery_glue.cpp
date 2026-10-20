#include "abi.h"

#include <cerrno>
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <string>
#include <sys/wait.h>
#include <signal.h>
#include <unistd.h>

#include <dlfcn.h>

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

int halffloat2vb(const ft_wide*, void*, int)
{
    std::fprintf(stderr, "bakery_glue: halffloat2vb not implemented\n");
    return -1;
}

int halffloat2vbm(const ft_wide*, void*, int, float)
{
    std::fprintf(stderr, "bakery_glue: halffloat2vbm not implemented\n");
    return -1;
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
