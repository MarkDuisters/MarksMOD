#if UNITY_EDITOR
using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

public static class BakeryLinuxProbeRunner
{
    public static bool Running { get; private set; }
    public static bool Done { get; private set; }
    public static bool Success { get; private set; }
    public static string Output { get; private set; }

    static Process process;
    static readonly StringBuilder log = new StringBuilder();
    static volatile bool dirty;

    public static void Start(string args = "--ping")
    {
        if (Running) return;

        if (Application.platform != RuntimePlatform.LinuxEditor)
        {
            Output = "probe.sh is Linux-only.";
            Done = true;
            Success = false;
            return;
        }

        var script = Path.Combine(BakeryLinuxSetup.ScriptDirectory, "probe.sh");
        if (!File.Exists(script))
        {
            Output = "probe.sh not found: " + script;
            Done = true;
            Success = false;
            return;
        }

        log.Length = 0;
        Done = false;
        Success = false;
        Output = "";

        var psi = new ProcessStartInfo
        {
            FileName = "/bin/bash",
            Arguments = "\"" + script + "\" " + args,
            WorkingDirectory = BakeryLinuxSetup.ScriptDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        process.OutputDataReceived += OnOutput;
        process.ErrorDataReceived += OnOutput;

        try
        {
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
        }
        catch (Exception e)
        {
            process = null;
            Output = "Could not start probe.sh: " + e.Message;
            Done = true;
            Success = false;
            return;
        }

        Running = true;
        EditorApplication.update += Tick;
    }

    public static void Stop()
    {
        if (process == null) return;
        try { process.Kill(); } catch { }
    }

    static void OnOutput(object sender, DataReceivedEventArgs e)
    {
        if (e.Data == null) return;
        lock (log)
        {
            log.AppendLine(e.Data);
            Output = log.ToString();
        }
        dirty = true;
    }

    static void Tick()
    {
        if (dirty)
        {
            dirty = false;
            BakeryMarksModWizard.RepaintIfOpen();
        }

        if (process == null || process.HasExited)
        {
            Finish();
        }
    }

    static void Finish()
    {
        EditorApplication.update -= Tick;

        int exitCode = -1;
        try
        {
            if (process != null)
            {
                process.WaitForExit();
                exitCode = process.ExitCode;
            }
        }
        catch { }
        process = null;

        Running = false;
        Done = true;
        Success = exitCode == 0;

        lock (log) Output = log.ToString();
        if (string.IsNullOrEmpty(Output)) Output = "(no output)";

        BakeryMarksModWizard.RefreshIfOpen();
    }
}
#endif
