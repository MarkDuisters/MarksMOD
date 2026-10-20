#if UNITY_EDITOR
using System;
using System.Runtime.InteropServices;

public static class BakeryProcess
{
#if UNITY_EDITOR_WIN
    const string Lib = "frender";
#else
    const string Lib = "bakery_glue";
#endif

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr RunLocalProcess([MarshalAs(UnmanagedType.LPWStr)] string commandline, bool setWorkDir);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr RunLocalProcessVisible([MarshalAs(UnmanagedType.LPWStr)] string commandline);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern bool IsProcessFinished(IntPtr proc);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern int GetProcessReturnValueAndClose(IntPtr proc);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern int StopProcess(IntPtr proc);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern int ExtractZIP([MarshalAs(UnmanagedType.LPWStr)] string zipFilename, int skipInnerFolders, string onlyFolder, [MarshalAs(UnmanagedType.LPWStr)] string outPath);
}
#endif
