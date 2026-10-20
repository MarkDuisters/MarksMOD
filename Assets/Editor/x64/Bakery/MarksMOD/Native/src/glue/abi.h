#pragma once

#include <cstdint>

extern "C" {

using ft_wide = char16_t;
using ft_utf8 = char;
using ft_bool = int32_t;

void* RunLocalProcess(const ft_wide* commandline, ft_bool setWorkDir);
void* RunLocalProcessVisible(const ft_wide* commandline);
ft_bool IsProcessFinished(void* proc);
int GetProcessReturnValueAndClose(void* proc);
int StopProcess(void* proc);

int simpleProgressBarShow(const ft_utf8* header, const ft_utf8* msg, float percent, float step, ft_bool onTop);
ft_bool simpleProgressBarCancelled();
void simpleProgressBarEnd();

void* GetForegroundWindow();
int SetForegroundWindow(void* hwnd);
void* GetParent(void* hwnd);
int GetWindowText(void* hwnd, ft_utf8* text, int count);
int GetClassName(void* hwnd, ft_utf8* text, int count);

int halffloat2vb(const ft_wide* inputFilename, void* values, int dataType);
int halffloat2vbm(const ft_wide* inputFilename, void* values, int dataType, float multiplier);

int uvrLoad(const float* inputUVs, int numVerts, const int* inputIndices, int numIndices);
int uvrRepack(float padding, int resolution);
int uvrUnload();

void* xatlasCreateAtlas();
int xatlasAddMesh(void* atlas, int vertexCount, const void* positions, const void* normals, const void* uv, int indexCount, const int* indices32);
int xatlasAddUVMesh(void* atlas, int vertexCount, const void* uv, int indexCount, const int* indices32, ft_bool allowRotate);
void xatlasParametrize(void* atlas);
void xatlasPack(void* atlas, int attempts, float texelsPerUnit, int resolution, int maxChartSize, int padding, ft_bool bruteForce, ft_bool blockAlign);
void xatlasNormalize(void* atlas, const int* atlasSizes, ft_bool preferDensity);
int xatlasGetAtlasCount(void* atlas);
int xatlasGetAtlasIndex(void* atlas, int meshIndex, int chartIndex);
int xatlasGetVertexCount(void* atlas, int meshIndex);
int xatlasGetIndexCount(void* atlas, int meshIndex);
void xatlasGetData(void* atlas, int meshIndex, void* outUV, void* outRef, void* outIndices);
int xatlasClear(void* atlas);

int ExtractZIP(const ft_wide* zipFilename, int skipInnerFolders, const ft_utf8* onlyFolder, const ft_wide* outPath);

}
