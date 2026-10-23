#if UNITY_EDITOR
using System.Runtime.InteropServices;
using UnityEditor;
using UnityEngine;

public static class BakeryProgress
{
#if UNITY_EDITOR_WIN
    [DllImport("simpleProgressBar", CallingConvention = CallingConvention.Cdecl)]
    static extern int simpleProgressBarShow(string header, string msg, float percent, float step, bool onTop);

    [DllImport("simpleProgressBar", CallingConvention = CallingConvention.Cdecl)]
    static extern bool simpleProgressBarCancelled();

    [DllImport("simpleProgressBar", CallingConvention = CallingConvention.Cdecl)]
    static extern void simpleProgressBarEnd();
#endif

    static bool shown;
    static bool cancelled;
    static bool cancelRequested;
    static string lastHeader = "Bakery";
    static string lastMessage = "";
    static float lastPercent;

    public static int ShowCount { get; private set; }
    public static int EndCount { get; private set; }

    public static bool IsShown
    {
        get { return shown; }
    }

    public static string Header
    {
        get { return lastHeader; }
    }

    public static string Message
    {
        get { return lastMessage; }
    }

    public static float Percent
    {
        get { return lastPercent; }
    }

    public static bool CancelRequested
    {
        get { return cancelRequested; }
    }

    public static void RequestCancel()
    {
        cancelRequested = true;
    }

    public static void ResetTestCounters()
    {
        ShowCount = 0;
        EndCount = 0;
    }

    public static void Show(string header, string msg, float percent, float step, bool onTop)
    {
        lastHeader = header;
        lastMessage = msg;
        lastPercent = Mathf.Clamp01(percent);
        shown = true;
        ShowCount++;

#if UNITY_EDITOR_WIN
        simpleProgressBarShow(header, msg, percent, step, onTop);
        cancelled = simpleProgressBarCancelled();
#else
        BakeryProgressWindow.ShowOrUpdate();
#endif
    }

    public static bool Cancelled
    {
        get
        {
#if UNITY_EDITOR_WIN
            cancelled = simpleProgressBarCancelled();
#else
            cancelled = cancelRequested;
#endif
            return cancelled;
        }
    }

    public static void Reset()
    {
        cancelled = false;
        cancelRequested = false;
    }

    public static void End()
    {
        EndCount++;
#if UNITY_EDITOR_WIN
        simpleProgressBarEnd();
#else
        if (shown) BakeryProgressWindow.Hide();
#endif
        shown = false;
        cancelled = false;
    }
}
#endif
