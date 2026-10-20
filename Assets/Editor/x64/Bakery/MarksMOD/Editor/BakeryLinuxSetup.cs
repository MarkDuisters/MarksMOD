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
    const string PrefDismissed = "Bakery.Linux.SetupDismissed";
    const string LogName = "BakeryLinuxSetup.log";

    static Process runningProcess;
    static readonly StringBuilder log = new StringBuilder();
    static bool running;
    static bool cancelled;
    static double startedAt;

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

    public static bool SetupComplete
    {
        get { return File.Exists(Path.Combine(PrefixPath, SentinelName)); }
    }

    public static void ScheduleStartupCheck()
    {
        if (!IsLinuxEditor) return;
        EditorApplication.delayCall += StartupCheck;
    }

    static void StartupCheck()
    {
        if (Application.isBatchMode) return;
        if (running || SetupComplete) return;
        if (EditorPrefs.GetBool(PrefDismissed, false)) return;

        int choice = EditorUtility.DisplayDialogComplex(
            "Bakery on Linux",
            "Bakery needs a Wine environment to run its GPU engine on Linux.\n\n" +
            "Setup creates a 64-bit prefix, installs the Visual C++ runtime, DXVK and maps the " +
            "NVIDIA driver. It can take several minutes.",
            "Setup Wine",
            "Open Wizard",
            "Don't ask again");

        if (choice == 0)
        {
            RunSetup();
            BakeryMarksModWizard.ShowWindow();
        }
        else if (choice == 1)
        {
            BakeryMarksModWizard.ShowWindow();
        }
        else
        {
            EditorPrefs.SetBool(PrefDismissed, true);
        }
    }

    [MenuItem("Bakery/MarksMOD/Run Wine Setup")]
    public static void RunSetup()
    {
        if (!IsLinuxEditor)
        {
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
            EditorUtility.DisplayDialog("Bakery", "Bootstrap script not found:\n" + script, "OK");
            return;
        }

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
            EditorUtility.DisplayDialog("Bakery", "Could not start setup:\n" + e.Message, "OK");
            return;
        }

        running = true;
        EditorApplication.update += Tick;
    }

    [MenuItem("Bakery/MarksMOD/Reset Setup Prompt")]
    public static void ResetPrompt()
    {
        EditorPrefs.DeleteKey(PrefDismissed);
        EditorUtility.DisplayDialog("Bakery", "The setup prompt will appear again on the next editor load.", "OK");
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
            UnityEngine.Debug.LogWarning("[Bakery] Wine setup cancelled.");
            return;
        }

        BakeryMarksModWizard.RefreshIfOpen();

        if (exitCode == 0 && SetupComplete)
        {
            BakeryLinuxHost.ScheduleStart();
            EditorUtility.DisplayDialog("Bakery", "Wine setup finished.\n\nPrefix:\n" + PrefixPath, "OK");
        }
        else
        {
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
