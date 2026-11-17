#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
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
        string xatlasMissing;
        bool xatlasReady = BakeryXatlasBackendService.RequiredFilesExist(out xatlasMissing);
        Add(list, "xatlas backend", xatlasReady && BakeryXatlasBackendService.IsActive
            ? BakeryCheckStatus.Ok : BakeryCheckStatus.Fail,
            BakeryXatlasBackendService.GetStatus());
        string bakeryXatlas = BakeryXatlasBackendService.BakeryXatlasDllPath;
        bool hasBakeryXatlas = File.Exists(bakeryXatlas);
        Add(list, "Bakery xatlas DLL", hasBakeryXatlas ? BakeryCheckStatus.Ok : BakeryCheckStatus.Fail,
            hasBakeryXatlas ? bakeryXatlas + " (" + new FileInfo(bakeryXatlas).Length + " bytes)" : bakeryXatlas);
        bool hostReady = BakeryWineHost.Ping();
        Add(list, "Wine xatlas host", hostReady ? BakeryCheckStatus.Ok : BakeryCheckStatus.Warn,
            hostReady ? "ready" : "starts on demand");
        string glue = Path.Combine(libs, "libbakery_glue.so");
        Add(list, "libbakery_glue.so", File.Exists(glue) ? BakeryCheckStatus.Ok : BakeryCheckStatus.Warn, libs);
        Add(list, "libfrender.so", File.Exists(Path.Combine(libs, "libfrender.so")) ? BakeryCheckStatus.Ok : BakeryCheckStatus.Warn, libs);

        if (File.Exists(glue))
        {
            string ldd = Capture("ldd", "\"" + glue + "\"", 10000);
            bool linked = !string.IsNullOrEmpty(ldd) && ldd.Contains("liblz4.so.1");
            bool missing = !string.IsNullOrEmpty(ldd) && ldd.Contains("liblz4.so.1 => not found");
            if (missing)
            {
                Add(list, "liblz4.so.1", BakeryCheckStatus.Fail, "missing runtime dependency of libbakery_glue.so");
            }
            else if (linked)
            {
                Add(list, "liblz4.so.1", BakeryCheckStatus.Ok, "resolved by libbakery_glue.so");
            }
            else
            {
                Add(list, "liblz4.so.1", BakeryCheckStatus.Warn, "not referenced by libbakery_glue.so (static/vendored?)");
            }
        }

        string host = Path.Combine(BakeryToolPaths.HostDirectory, "bakery_wine_host.exe");
        Add(list, "Wine host", File.Exists(host) ? BakeryCheckStatus.Ok : BakeryCheckStatus.Warn, host);

        string payload = BakeryToolPaths.PayloadDirectory;
        CheckPayload(list, payload, "frender.dll", BakeryCheckStatus.Fail);
        CheckPayload(list, payload, "ftraceRTX9.exe", BakeryCheckStatus.Fail);
        CheckPayload(list, payload, "denoiser.exe", BakeryCheckStatus.Warn);
        CheckPayload(list, payload, "combineMasks.exe", BakeryCheckStatus.Warn);

        bool patched = BakeryLinuxPatcher.IsPatched;
        Add(list, "Bakery patches", patched ? BakeryCheckStatus.Ok : BakeryCheckStatus.Fail,
            patched ? "applied" : "click PATCH! in the wizard");

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

    internal static string FindInPath(string exe)
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

    internal static string Capture(string file, string args, int timeoutMs, bool includeError = false)
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

            var output = new StringBuilder();
            var error = new StringBuilder();

            using (var p = new Process())
            {
                p.StartInfo = psi;
                p.OutputDataReceived += (sender, e) =>
                {
                    if (e.Data == null) return;
                    lock (output) output.AppendLine(e.Data);
                };
                p.ErrorDataReceived += (sender, e) =>
                {
                    if (e.Data == null) return;
                    lock (error) error.AppendLine(e.Data);
                };

                if (!p.Start()) return null;
                p.BeginOutputReadLine();
                p.BeginErrorReadLine();

                if (!p.WaitForExit(timeoutMs))
                {
                    try { p.Kill(); } catch { }
                    try { p.WaitForExit(1000); } catch { }
                    return null;
                }

                p.WaitForExit();

                string stdout;
                string stderr;
                lock (output) stdout = output.ToString();
                lock (error) stderr = error.ToString();

                if (!includeError) return string.IsNullOrEmpty(stdout) ? stderr : stdout;
                if (string.IsNullOrEmpty(stdout)) return stderr;
                if (string.IsNullOrEmpty(stderr)) return stdout;
                return stdout + "\n" + stderr;
            }
        }
        catch
        {
            return null;
        }
    }
}
#endif
