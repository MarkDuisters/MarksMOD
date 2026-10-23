#if UNITY_EDITOR
using System;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using UnityEngine;
using Debug = UnityEngine.Debug;

public static class BakeryWineHost
{
    const int Port = 27778;
    const uint Magic = 0x42414B31u;
    const uint CmdShutdown = 7u;

    public static bool IsLinuxEditor
    {
        get { return Application.platform == RuntimePlatform.LinuxEditor; }
    }

    public static string PrefixPath
    {
        get
        {
            var env = Environment.GetEnvironmentVariable("BAKERY_WINEPREFIX");
            if (!string.IsNullOrEmpty(env)) return env;

            var wineRoot = Environment.GetEnvironmentVariable("BAKERY_WINE_ROOT");
            if (!string.IsNullOrEmpty(wineRoot)) return Path.Combine(wineRoot, "wineprefix");

            var xdg = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var root = string.IsNullOrEmpty(xdg) ? Path.Combine(home, ".local", "share") : xdg;
            return Path.Combine(root, "bakery", "wineprefix");
        }
    }

    public static string ScriptDirectory
    {
        get { return Application.dataPath + "/Editor/x64/Bakery/MarksMOD/Native/wine"; }
    }

    public static string HostDirectory
    {
        get { return Application.dataPath + "/Editor/x64/Bakery/MarksMOD/Native/dist~/bin"; }
    }

    public static bool IsListening()
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

    public static bool EnsureStarted()
    {
        if (!IsLinuxEditor) return false;
        if (IsListening()) return true;

        string script = Path.Combine(ScriptDirectory, "probe.sh");
        string host = Path.Combine(HostDirectory, "bakery_wine_host.exe");
        if (!File.Exists(script) || !File.Exists(host))
        {
            Debug.LogWarning("[Bakery] Wine host is missing: " + host);
            return false;
        }

        var psi = new ProcessStartInfo
        {
            FileName = "/bin/bash",
            Arguments = "\"" + script + "\" --start",
            WorkingDirectory = ScriptDirectory,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        psi.Environment["BAKERY_WINEPREFIX"] = PrefixPath;
        CopyEnvironment(psi, "BAKERY_RUNNER");
        CopyEnvironment(psi, "DXVK_CONFIG");
        CopyEnvironment(psi, "VK_ICD_FILENAMES");
        CopyEnvironment(psi, "WINEDLLOVERRIDES");

        try
        {
            using (var process = Process.Start(psi))
            {
                if (process != null) process.WaitForExit(20000);
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning("[Bakery] Could not start Wine host: " + e.Message);
        }

        return IsListening();
    }

    public static void Stop()
    {
        if (!IsLinuxEditor || !IsListening()) return;

        try
        {
            using (var client = new TcpClient())
            {
                client.Connect("127.0.0.1", Port);
                using (var stream = client.GetStream())
                {
                    var writer = new BinaryWriter(stream);
                    writer.Write(Magic);
                    writer.Write(CmdShutdown);
                    writer.Write(0u);
                    writer.Flush();
                    var reader = new BinaryReader(stream);
                    reader.ReadInt32();
                }
            }
        }
        catch { }

        for (int i = 0; i < 20 && IsListening(); i++)
        {
            System.Threading.Thread.Sleep(100);
        }

        if (IsListening()) StopFromPidFile();
    }

    static void StopFromPidFile()
    {
        try
        {
            string pidFile = "/tmp/bakery_wine_host.pid";
            if (!File.Exists(pidFile)) return;
            string pid = File.ReadAllText(pidFile).Trim();
            if (string.IsNullOrEmpty(pid)) return;

            var psi = new ProcessStartInfo
            {
                FileName = "kill",
                Arguments = "-TERM " + pid,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            Process.Start(psi);
        }
        catch { }
    }

    static void CopyEnvironment(ProcessStartInfo psi, string name)
    {
        var value = Environment.GetEnvironmentVariable(name);
        if (!string.IsNullOrEmpty(value)) psi.Environment[name] = value;
    }
}
#endif
