#pragma once

#include <cstdint>

extern "C" {

using ft_utf8 = char;
using ft_bool = int32_t;

struct FTTexInput
{
    void* data;
    uint16_t width;
    uint16_t height;
};

void InitShaders();
void LoadScene(const ft_utf8* path);

void SetAlbedos(int count, void** tex);
int CopyAlbedos();
int CopyHeightmapsFromRAM(int count, const FTTexInput* tex);
void FreeAlbedoCopies();

void SetAlphas(int count, void** tex, const float* alphaRefs, const int* alphaChannels, int numLODs, ft_bool flip);
void SetAlphasFromRAM(int count, void* tex, const float* alphaRefs, const int* alphaChannels, int numLODs, ft_bool flip);
int ToggleABGDither(int on);

void SaveSky(void* tex, float rx, float ry, float rz, float ux, float uy, float uz, float fx, float fy, float fz, const ft_utf8* path, ft_bool isLinear, ft_bool doubleLDR, ft_bool rgbm);
void SaveCookie(void* tex, const ft_utf8* path);
void SaveCookieFromRAM(FTTexInput tex, const ft_utf8* path);

int ftRenderUVGBuffer();
void SetUVGBFlags(int flags);
void SetUVGBTangents(int hasTangents);
void SetFixPos(ft_bool enabled);
void SetCompression(ft_bool enabled);

int ftGenerateAlphaBuffer();
int GetABGErrorCode();
int GetUVGBErrorCode();

int SaveGBufferMap(void* tex, const ft_utf8* path, ft_bool compressed);
int SaveGBufferMapFromRAM(const uint8_t* tex, int size, const ft_utf8* path, ft_bool compressed);

}
