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
    static string lastHeader = "Bakery";
    static string lastMessage = "";
    static float lastPercent;

    public static void Show(string header, string msg, float percent, float step, bool onTop)
    {
        lastHeader = header;
        lastMessage = msg;
        lastPercent = Mathf.Clamp01(percent);
        shown = true;

#if UNITY_EDITOR_WIN
        simpleProgressBarShow(header, msg, percent, step, onTop);
        cancelled = simpleProgressBarCancelled();
#else
        cancelled = EditorUtility.DisplayCancelableProgressBar(header, msg, lastPercent);
#endif
    }

    public static bool Cancelled
    {
        get
        {
#if UNITY_EDITOR_WIN
            cancelled = simpleProgressBarCancelled();
#else
            if (shown) cancelled = EditorUtility.DisplayCancelableProgressBar(lastHeader, lastMessage, lastPercent);
#endif
            return cancelled;
        }
    }

    public static void Reset()
    {
        cancelled = false;
    }

    public static void End()
    {
#if UNITY_EDITOR_WIN
        simpleProgressBarEnd();
#else
        if (shown) EditorUtility.ClearProgressBar();
#endif
        shown = false;
        cancelled = false;
    }
}
#endif
