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
    const uint CmdPing = 1u;
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

    public static string NativeDirectory
    {
        get
        {
            string configured = Environment.GetEnvironmentVariable("BAKERY_MARKSMOD_NATIVE");
            if (!string.IsNullOrEmpty(configured)) return configured;

            string payload = Environment.GetEnvironmentVariable("BAKERY_FRENDER_DIR");
            if (string.IsNullOrEmpty(payload)) payload = Environment.GetEnvironmentVariable("BAKERY_TOOLS_DIR");
            if (!string.IsNullOrEmpty(payload)) return Path.Combine(payload, "MarksMOD", "Native");

            return Path.Combine(Application.dataPath, "Editor/x64/Bakery/MarksMOD/Native");
        }
    }

    public static string ScriptDirectory
    {
        get { return Path.Combine(NativeDirectory, "wine"); }
    }

    public static string HostDirectory
    {
        get { return Path.Combine(NativeDirectory, "dist~/bin"); }
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

    public static bool Ping()
    {
        if (!IsLinuxEditor) return false;
        try
        {
            using (var client = new TcpClient())
            {
                var result = client.BeginConnect("127.0.0.1", Port, null, null);
                if (!result.AsyncWaitHandle.WaitOne(500)) return false;
                client.EndConnect(result);
                client.ReceiveTimeout = 1500;
                using (var stream = client.GetStream())
                {
                    var writer = new BinaryWriter(stream);
                    writer.Write(Magic);
                    writer.Write(CmdPing);
                    writer.Write(0u);
                    writer.Flush();
                    var reader = new BinaryReader(stream);
                    return reader.ReadInt32() == 1;
                }
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
        if (Ping()) return true;

        if (IsListening())
        {
            StopFromPidFile();
            WaitForPortClosed(10000);
        }

        string script = Path.Combine(ScriptDirectory, "probe.sh");
        string host = Path.Combine(HostDirectory, "bakery_wine_host.exe");
        if (!File.Exists(script) || !File.Exists(host))
        {
            Debug.LogWarning("[Bakery] Wine host is missing: " + host);
            return false;
        }

        Environment.SetEnvironmentVariable("BAKERY_WINE_SCRIPT", script);
        Environment.SetEnvironmentVariable("BAKERY_WINE_HOST", host);
        string xatlasDll = Environment.GetEnvironmentVariable("BAKERY_XATLAS_DLL");
        if (string.IsNullOrEmpty(xatlasDll))
        {
            string payload = Environment.GetEnvironmentVariable("BAKERY_FRENDER_DIR");
            if (string.IsNullOrEmpty(payload)) payload = Environment.GetEnvironmentVariable("BAKERY_TOOLS_DIR");
            if (string.IsNullOrEmpty(payload)) payload = Path.Combine(Application.dataPath, "Editor/x64/Bakery");
            xatlasDll = Path.Combine(payload, "scripts", "xatlas", "xatlasLib.dll");
            Environment.SetEnvironmentVariable("BAKERY_XATLAS_DLL", xatlasDll);
        }
        var configuredRunner = Environment.GetEnvironmentVariable("BAKERY_RUNNER");
        if (!string.IsNullOrEmpty(configuredRunner))
            Environment.SetEnvironmentVariable("BAKERY_WINE", configuredRunner);

        var psi = new ProcessStartInfo
        {
            FileName = "/bin/bash",
            Arguments = "\"" + script + "\" --start",
            WorkingDirectory = ScriptDirectory,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        psi.Environment["BAKERY_WINEPREFIX"] = PrefixPath;
        psi.Environment["BAKERY_WINE_SCRIPT"] = script;
        psi.Environment["BAKERY_WINE_HOST"] = host;
        xatlasDll = Environment.GetEnvironmentVariable("BAKERY_XATLAS_DLL");
        if (!string.IsNullOrEmpty(xatlasDll)) psi.Environment["BAKERY_XATLAS_DLL"] = xatlasDll;
        CopyEnvironment(psi, "BAKERY_RUNNER");
        CopyEnvironment(psi, "BAKERY_WINE");
        CopyEnvironment(psi, "DXVK_CONFIG");
        CopyEnvironment(psi, "VK_ICD_FILENAMES");
        CopyEnvironment(psi, "WINEDLLOVERRIDES");

        try
        {
            using (var process = Process.Start(psi))
            {
                if (process != null) process.WaitForExit(120000);
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning("[Bakery] Could not start Wine host: " + e.Message);
        }

        if (Ping()) return true;
        Debug.LogWarning("[Bakery] Wine host did not become ready. See /tmp/bakery_wine_host.log");
        return false;
    }

    public static void Stop()
    {
        if (!IsLinuxEditor) return;
        if (!IsListening() && !Ping()) return;

        try
        {
            using (var client = new TcpClient())
            {
                client.Connect("127.0.0.1", Port);
                client.ReceiveTimeout = 1500;
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

        WaitForPortClosed(10000);

        if (IsListening())
        {
            StopFromPidFile();
            WaitForPortClosed(5000);
        }

        if (IsListening()) KillFromPidFile();
    }

    static void WaitForPortClosed(int milliseconds)
    {
        int steps = System.Math.Max(1, milliseconds / 100);
        for (int i = 0; i < steps && IsListening(); i++)
        {
            System.Threading.Thread.Sleep(100);
        }
    }

    static void StopFromPidFile()
    {
        SignalPidFile("-TERM");
    }

    static void KillFromPidFile()
    {
        SignalPidFile("-KILL");
    }

    static void SignalPidFile(string signal)
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
                Arguments = signal + " " + pid,
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
