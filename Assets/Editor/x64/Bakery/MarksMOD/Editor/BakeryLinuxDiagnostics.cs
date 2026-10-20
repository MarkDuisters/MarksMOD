#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;

public enum BakeryCheckStatus
{
    Ok,
    Warn,
    Fail
}

public class BakeryCheckResult
{
    public string Name;
    public BakeryCheckStatus Status;
    public string Detail;
}

public static class BakeryLinuxDiagnostics
{
    public static List<BakeryCheckResult> Run()
    {
        var list = new List<BakeryCheckResult>();

        bool linux = Application.platform == RuntimePlatform.LinuxEditor;
        Add(list, "Linux editor", linux ? BakeryCheckStatus.Ok : BakeryCheckStatus.Fail,
            linux ? Application.platform.ToString() : "Wine bridge is Linux-only");

        if (!linux) return list;

        string runnerEnv = Environment.GetEnvironmentVariable("BAKERY_RUNNER");
        string wine64 = FindInPath("wine64");
        string wine = FindInPath("wine");
        string proton = FindInPath("proton");
        string umu = FindInPath("umu-run");

        if (!string.IsNullOrEmpty(runnerEnv))
            Add(list, "Runner", BakeryCheckStatus.Ok, "BAKERY_RUNNER=" + runnerEnv);
        else if (wine64 != null)
            Add(list, "Runner", BakeryCheckStatus.Ok, wine64);
        else if (wine != null)
            Add(list, "Runner", BakeryCheckStatus.Ok, wine);
        else if (umu != null)
            Add(list, "Runner", BakeryCheckStatus.Warn, "umu-run found; set BAKERY_RUNNER=umu-run");
        else if (proton != null)
            Add(list, "Runner", BakeryCheckStatus.Warn, "proton found; set BAKERY_RUNNER=\"proton run\"");
        else
            Add(list, "Runner", BakeryCheckStatus.Fail, "install Wine, Proton or umu-launcher");

        Add(list, "winetricks", FindInPath("winetricks") != null ? BakeryCheckStatus.Ok : BakeryCheckStatus.Warn,
            FindInPath("winetricks") != null ? "found" : "needed for setup");

        string gpu = Capture("nvidia-smi", "--query-gpu=name --format=csv,noheader", 3000);
        Add(list, "NVIDIA driver", string.IsNullOrEmpty(gpu) ? BakeryCheckStatus.Fail : BakeryCheckStatus.Ok,
            string.IsNullOrEmpty(gpu) ? "nvidia-smi failed; CUDA/OptiX require the NVIDIA driver" : gpu.Trim());

        string prefix = BakeryLinuxSetup.PrefixPath;
        Add(list, "Wine prefix", Directory.Exists(prefix) ? BakeryCheckStatus.Ok : BakeryCheckStatus.Fail, prefix);

        string sys32 = Path.Combine(prefix, "drive_c", "windows", "system32");
        CheckFile(list, "VC runtime", sys32, new[] { "msvcp140.dll", "vcruntime140.dll" }, BakeryCheckStatus.Fail);
        CheckFile(list, "DXVK (d3d11)", sys32, new[] { "d3d11.dll" }, BakeryCheckStatus.Fail);
        CheckFile(list, "d3dcompiler_47", sys32, new[] { "d3dcompiler_47.dll" }, BakeryCheckStatus.Warn);
        CheckFile(list, "CUDA/OptiX bridge", sys32, new[] { "nvcuda.dll", "nvoptix.dll" }, BakeryCheckStatus.Fail);

        Add(list, "Setup sentinel", BakeryLinuxSetup.SetupComplete ? BakeryCheckStatus.Ok : BakeryCheckStatus.Fail,
            BakeryLinuxSetup.SetupComplete ? "bootstrap completed" : "run Setup Wine Environment");

        string libs = BakeryToolPaths.LibrariesDirectory;
        Add(list, "libbakery_glue.so", File.Exists(Path.Combine(libs, "libbakery_glue.so")) ? BakeryCheckStatus.Ok : BakeryCheckStatus.Warn, libs);
        Add(list, "libfrender.so", File.Exists(Path.Combine(libs, "libfrender.so")) ? BakeryCheckStatus.Ok : BakeryCheckStatus.Warn, libs);

        string host = Path.Combine(BakeryToolPaths.HostDirectory, "bakery_wine_host.exe");
        Add(list, "Wine host", File.Exists(host) ? BakeryCheckStatus.Ok : BakeryCheckStatus.Warn, host);

        string payload = BakeryToolPaths.PayloadDirectory;
        CheckPayload(list, payload, "frender.dll", BakeryCheckStatus.Fail);
        CheckPayload(list, payload, "ftraceRTX9.exe", BakeryCheckStatus.Fail);
        CheckPayload(list, payload, "denoiser.exe", BakeryCheckStatus.Warn);
        CheckPayload(list, payload, "combineMasks.exe", BakeryCheckStatus.Warn);

        bool patched = BakeryLinuxPatcher.IsPatched;
        Add(list, "Bakery patches", patched ? BakeryCheckStatus.Ok : BakeryCheckStatus.Fail,
            patched ? "applied" : "click Patch Bakery in the wizard");

        Add(list, "Unity graphics API", SystemInfo.graphicsDeviceType == GraphicsDeviceType.Direct3D11 ? BakeryCheckStatus.Warn : BakeryCheckStatus.Ok,
            SystemInfo.graphicsDeviceType.ToString());

        return list;
    }

    static void Add(List<BakeryCheckResult> list, string name, BakeryCheckStatus status, string detail)
    {
        list.Add(new BakeryCheckResult { Name = name, Status = status, Detail = detail });
    }

    static void CheckFile(List<BakeryCheckResult> list, string name, string dir, string[] files, BakeryCheckStatus failStatus)
    {
        var missing = new List<string>();
        foreach (var f in files)
        {
            if (!File.Exists(Path.Combine(dir, f))) missing.Add(f);
        }
        if (missing.Count == 0)
            Add(list, name, BakeryCheckStatus.Ok, dir);
        else
            Add(list, name, failStatus, "missing: " + string.Join(", ", missing.ToArray()));
    }

    static void CheckPayload(List<BakeryCheckResult> list, string dir, string file, BakeryCheckStatus failStatus)
    {
        bool ok = File.Exists(Path.Combine(dir, file));
        Add(list, file, ok ? BakeryCheckStatus.Ok : failStatus, ok ? dir : "not found in " + dir);
    }

    static string FindInPath(string exe)
    {
        string path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(path)) return null;
        foreach (var dir in path.Split(Path.PathSeparator))
        {
            if (string.IsNullOrEmpty(dir)) continue;
            var full = Path.Combine(dir, exe);
            if (File.Exists(full)) return full;
        }
        return null;
    }

    static string Capture(string file, string args, int timeoutMs)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = file,
                Arguments = args,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using (var p = Process.Start(psi))
            {
                if (p == null) return null;
                string output = p.StandardOutput.ReadToEnd();
                string error = p.StandardError.ReadToEnd();
                if (!p.WaitForExit(timeoutMs))
                {
                    try { p.Kill(); } catch { }
                    return null;
                }
                return string.IsNullOrEmpty(output) ? error : output;
            }
        }
        catch
        {
            return null;
        }
    }
}
#endif
