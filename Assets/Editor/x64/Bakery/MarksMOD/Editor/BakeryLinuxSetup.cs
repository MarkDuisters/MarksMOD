#if UNITY_EDITOR
using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

public static class BakeryLinuxSetup
{
    const string SentinelName = ".bakery-setup-ok";
    const string SessionHint = "Bakery.Linux.SetupHinted";
    const string LogName = "BakeryLinuxSetup.log";

    static Process runningProcess;
    static readonly StringBuilder log = new StringBuilder();
    static bool running;
    static bool cancelled;
    static double startedAt;
    static Action completionCallback;

    public static bool IsRunning
    {
        get { return running; }
    }

    public static bool IsLinuxEditor
    {
        get { return Application.platform == RuntimePlatform.LinuxEditor; }
    }

    public static string WineRootPath
    {
        get
        {
            var wineRoot = Environment.GetEnvironmentVariable("BAKERY_WINE_ROOT");
            if (!string.IsNullOrEmpty(wineRoot)) return wineRoot;

            var xdg = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var root = string.IsNullOrEmpty(xdg) ? Path.Combine(home, ".local", "share") : xdg;
            return Path.Combine(root, "bakery");
        }
    }

    public static string PrefixPath
    {
        get
        {
            var env = Environment.GetEnvironmentVariable("BAKERY_WINEPREFIX");
            if (!string.IsNullOrEmpty(env)) return env;

            return Path.Combine(WineRootPath, "wineprefix");
        }
    }

    public static string ScriptDirectory
    {
        get { return Path.Combine(BakeryToolPaths.NativeDirectory, "wine"); }
    }

    public static bool SetupComplete
    {
        get { return File.Exists(Path.Combine(PrefixPath, SentinelName)); }
    }

    public static bool PrefixLooksReady
    {
        get
        {
            if (!SetupComplete) return false;

            string sys32 = Path.Combine(PrefixPath, "drive_c", "windows", "system32");
            if (!File.Exists(Path.Combine(sys32, "msvcp140.dll"))) return false;
            if (!File.Exists(Path.Combine(sys32, "vcruntime140.dll"))) return false;
            if (!File.Exists(Path.Combine(sys32, "d3d11.dll"))) return false;

            if (Environment.GetEnvironmentVariable("BAKERY_NO_NVIDIA_LIBS") != "1")
            {
                if (!File.Exists(Path.Combine(sys32, "nvcuda.dll"))) return false;
                if (!File.Exists(Path.Combine(sys32, "nvoptix.dll"))) return false;
            }

            return true;
        }
    }

    public static void HintSetupOnce()
    {
        if (!IsLinuxEditor || PrefixLooksReady) return;
        if (SessionState.GetBool(SessionHint, false)) return;

        SessionState.SetBool(SessionHint, true);
        UnityEngine.Debug.LogWarning(
            "[Bakery] Wine setup is not complete. Open Bakery > MarksMOD > Setup Wizard and click PATCH!");
    }

    public static void RunSetup(Action onComplete = null)
    {
        if (!IsLinuxEditor)
        {
            completionCallback = null;
            EditorUtility.DisplayDialog("Bakery", "Wine setup is only used on Linux.", "OK");
            return;
        }
        if (running)
        {
            EditorUtility.DisplayDialog("Bakery", "Wine setup is already running.", "OK");
            return;
        }

        var script = Path.Combine(ScriptDirectory, "bootstrap.sh");
        if (!File.Exists(script))
        {
            completionCallback = null;
            EditorUtility.DisplayDialog("Bakery", "Bootstrap script not found:\n" + script, "OK");
            return;
        }

        completionCallback = onComplete;
        log.Length = 0;
        cancelled = false;
        startedAt = EditorApplication.timeSinceStartup;

        var psi = new ProcessStartInfo
        {
            FileName = "/bin/bash",
            Arguments = "\"" + script + "\"",
            WorkingDirectory = ScriptDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        psi.Environment["BAKERY_WINEPREFIX"] = PrefixPath;

        runningProcess = new Process { StartInfo = psi, EnableRaisingEvents = true };
        runningProcess.OutputDataReceived += OnOutput;
        runningProcess.ErrorDataReceived += OnOutput;

        try
        {
            runningProcess.Start();
            runningProcess.BeginOutputReadLine();
            runningProcess.BeginErrorReadLine();
        }
        catch (Exception e)
        {
            completionCallback = null;
            EditorUtility.DisplayDialog("Bakery", "Could not start setup:\n" + e.Message, "OK");
            return;
        }

        running = true;
        EditorApplication.update += Tick;
    }

    static void OnOutput(object sender, DataReceivedEventArgs e)
    {
        if (e.Data == null) return;
        lock (log) log.AppendLine(e.Data);
    }

    static void Tick()
    {
        if (runningProcess == null || !running)
        {
            Finish();
            return;
        }

        if (runningProcess.HasExited)
        {
            Finish();
            return;
        }

        double elapsed = EditorApplication.timeSinceStartup - startedAt;
        float progress = Mathf.Clamp((float)(elapsed / 300.0), 0.05f, 0.95f);

        if (EditorUtility.DisplayCancelableProgressBar("Bakery Linux setup", "Installing Wine components...", progress))
        {
            cancelled = true;
            try { runningProcess.Kill(); } catch { }
        }
    }

    static void Finish()
    {
        EditorApplication.update -= Tick;
        EditorUtility.ClearProgressBar();
        running = false;

        int exitCode = -1;
        try
        {
            if (runningProcess != null)
            {
                runningProcess.WaitForExit();
                exitCode = runningProcess.ExitCode;
            }
        }
        catch { }
        runningProcess = null;

        string text;
        lock (log) text = log.ToString();
        WriteLog(text);

        if (cancelled)
        {
            completionCallback = null;
            UnityEngine.Debug.LogWarning("[Bakery] Wine setup cancelled.");
            return;
        }

        Action callback = completionCallback;
        completionCallback = null;

        if (exitCode == 0 && SetupComplete)
        {
            UnityEngine.Debug.Log("[Bakery] Wine setup finished: " + PrefixPath);
            if (callback != null)
            {
                try
                {
                    callback();
                }
                catch (Exception e)
                {
                    UnityEngine.Debug.LogError("[Bakery] Post-setup action failed: " + e);
                }
            }
            else
            {
                BakeryMarksModWizard.RefreshIfOpen();
                EditorUtility.DisplayDialog("Bakery", "Wine setup finished.\n\nPrefix:\n" + PrefixPath, "OK");
            }
        }
        else
        {
            BakeryMarksModWizard.RefreshIfOpen();
            UnityEngine.Debug.LogError("[Bakery] Wine setup failed (exit " + exitCode + ").\n" + Tail(text, 40));
            EditorUtility.DisplayDialog(
                "Bakery",
                "Wine setup failed (exit " + exitCode + "). See the Console and:\n" + LogPath,
                "OK");
        }
    }

    static string Tail(string text, int lines)
    {
        if (string.IsNullOrEmpty(text)) return "(no output)";
        var parts = text.Split('\n');
        int start = Math.Max(0, parts.Length - lines);
        return string.Join("\n", parts, start, parts.Length - start);
    }

    public static string LogPath
    {
        get { return Path.Combine(Path.GetTempPath(), LogName); }
    }

    static void WriteLog(string text)
    {
        try { File.WriteAllText(LogPath, text); }
        catch { }
    }
}
#endif
