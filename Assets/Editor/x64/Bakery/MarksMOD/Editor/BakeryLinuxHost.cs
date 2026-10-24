#if UNITY_EDITOR
using System;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using UnityEditor;
using UnityEngine;

public static class BakeryLinuxHost
{
    const int Port = 27778;
    static bool starting;

    public static void ScheduleStart()
    {
        if (Application.platform != RuntimePlatform.LinuxEditor) return;
        EditorApplication.delayCall += Start;
    }

    static void Start()
    {
        if (starting || IsListening()) return;

        string script = Path.Combine(BakeryLinuxSetup.ScriptDirectory, "probe.sh");
        string host = Path.Combine(BakeryToolPaths.HostDirectory, "bakery_wine_host.exe");
        if (!File.Exists(script) || !File.Exists(host)) return;

        starting = true;

        var psi = new ProcessStartInfo
        {
            FileName = "/bin/bash",
            Arguments = "\"" + script + "\" --start",
            WorkingDirectory = BakeryLinuxSetup.ScriptDirectory,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        psi.Environment["BAKERY_WINEPREFIX"] = BakeryLinuxSetup.PrefixPath;
        psi.Environment["BAKERY_FRENDER_DIR"] = BakeryToolPaths.PayloadDirectory;
        psi.Environment["BAKERY_XATLAS_DLL"] = Path.Combine(BakeryToolPaths.PayloadDirectory, "scripts/xatlas/xatlasLib.dll");

        CopyEnv(psi, "BAKERY_RUNNER");
        CopyEnv(psi, "DXVK_CONFIG");
        CopyEnv(psi, "VK_ICD_FILENAMES");
        CopyEnv(psi, "WINEDLLOVERRIDES");

        try
        {
            using (var process = Process.Start(psi))
            {
                if (process != null) process.WaitForExit(20000);
            }
        }
        catch (Exception e)
        {
            UnityEngine.Debug.LogWarning("[Bakery] Could not start Wine host: " + e.Message);
        }
        finally
        {
            starting = false;
        }
    }

    static void CopyEnv(ProcessStartInfo psi, string name)
    {
        var value = Environment.GetEnvironmentVariable(name);
        if (!string.IsNullOrEmpty(value)) psi.Environment[name] = value;
    }

    static bool IsListening()
    {
        try
        {
            using (var client = new TcpClient())
            {
                var result = client.BeginConnect("127.0.0.1", Port, null, null);
                if (!result.AsyncWaitHandle.WaitOne(200)) return false;
                client.EndConnect(result);
                return true;
            }
        }
        catch
        {
            return false;
        }
    }
}
#endif
