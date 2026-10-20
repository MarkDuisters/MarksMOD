#if UNITY_EDITOR
using System.IO;
using UnityEngine;

public static class BakeryToolPaths
{
    public static bool IsLinuxEditor
    {
        get { return Application.platform == RuntimePlatform.LinuxEditor; }
    }

    static string EditorRoot
    {
        get { return Application.dataPath + "/Editor/x64/Bakery"; }
    }

    public static string NativeDirectory
    {
        get { return EditorRoot + "/MarksMOD/Native"; }
    }

    public static string PayloadDirectory
    {
        get { return EditorRoot; }
    }

    public static string ToolsDirectory
    {
        get { return IsLinuxEditor ? NativeDirectory + "/dist~/bin" : EditorRoot; }
    }

    public static string HostDirectory
    {
        get { return NativeDirectory + "/dist~/bin"; }
    }

    public static string LibrariesDirectory
    {
        get { return Application.dataPath + "/Plugins/x86_64"; }
    }

    public static string NativeBuildLibraryDirectory
    {
        get { return NativeDirectory + "/dist~/lib"; }
    }

    public static string GetToolName(string logicalName)
    {
        return IsLinuxEditor ? logicalName : logicalName + ".exe";
    }

    public static string GetToolPath(string logicalName)
    {
        return Path.Combine(ToolsDirectory, GetToolName(logicalName));
    }
}
#endif
