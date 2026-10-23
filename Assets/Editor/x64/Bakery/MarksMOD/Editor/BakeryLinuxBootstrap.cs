#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class BakeryLinuxBootstrap
{
    static BakeryLinuxBootstrap()
    {
        if (Application.platform != RuntimePlatform.LinuxEditor) return;

        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("BAKERY_RUNNER")))
        {
            var runner = FindInPath("wine64") ?? FindInPath("wine");
            if (!string.IsNullOrEmpty(runner))
            {
                Environment.SetEnvironmentVariable("BAKERY_RUNNER", runner);
            }
        }

        Environment.SetEnvironmentVariable("BAKERY_TOOLS_DIR", BakeryToolPaths.PayloadDirectory);
        Environment.SetEnvironmentVariable("BAKERY_FRENDER_DIR", BakeryToolPaths.PayloadDirectory);

        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DXVK_CONFIG")))
        {
            Environment.SetEnvironmentVariable("DXVK_CONFIG", "dxgi.hideNvidiaGpu = False");
        }

        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("VK_ICD_FILENAMES")))
        {
            var icd = FindNvidiaIcd();
            if (icd != null) Environment.SetEnvironmentVariable("VK_ICD_FILENAMES", icd);
        }

        var dllOverrides = Environment.GetEnvironmentVariable("WINEDLLOVERRIDES");
        const string requiredOverrides = "d3d11=n,b;dxgi=n,b;d3dcompiler_47=n,b";
        if (string.IsNullOrEmpty(dllOverrides))
        {
            Environment.SetEnvironmentVariable("WINEDLLOVERRIDES", requiredOverrides);
        }
        else if (!dllOverrides.Contains("d3d11=") || !dllOverrides.Contains("dxgi=") || !dllOverrides.Contains("d3dcompiler_47="))
        {
            Environment.SetEnvironmentVariable("WINEDLLOVERRIDES", dllOverrides + ";" + requiredOverrides);
        }

        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("BAKERY_WINEPREFIX")))
        {
            Environment.SetEnvironmentVariable("BAKERY_WINEPREFIX", BakeryLinuxSetup.PrefixPath);
        }

        if (!Directory.Exists(BakeryToolPaths.LibrariesDirectory))
        {
            Debug.LogWarning("[Bakery] Linux plugin directory is missing: " + BakeryToolPaths.LibrariesDirectory);
        }

        if (!BakeryLinuxPatcher.IsPatched)
        {
            Debug.LogWarning("[Bakery] MarksMOD patches are not applied. Open Bakery/MarksMOD/Setup Wizard and click Patch Bakery.");
        }

        BakeryLinuxSetup.ScheduleStartupCheck();
    }

    static string FindInPath(string exe)
    {
        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(path)) return null;

        foreach (var dir in path.Split(Path.PathSeparator))
        {
            if (string.IsNullOrEmpty(dir)) continue;
            var full = Path.Combine(dir, exe);
            if (File.Exists(full)) return full;
        }
        return null;
    }

    static string FindNvidiaIcd()
    {
        string[] paths =
        {
            "/usr/share/vulkan/icd.d/nvidia_icd.json",
            "/etc/vulkan/icd.d/nvidia_icd.json",
            "/usr/share/vulkan/icd.d/nvidia_icd.x86_64.json"
        };

        foreach (var path in paths)
        {
            if (File.Exists(path)) return path;
        }
        return null;
    }
}
#endif
