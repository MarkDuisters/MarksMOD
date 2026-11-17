#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;

public static class BakeryToolPaths
{
    static string cachedEditorRoot;
    static string cachedLibrariesDirectory;

    public static bool IsLinuxEditor
    {
        get { return Application.platform == RuntimePlatform.LinuxEditor; }
    }

    static string ProjectRoot
    {
        get { return Path.GetDirectoryName(Application.dataPath); }
    }

    public static string EditorRoot
    {
        get
        {
            if (!string.IsNullOrEmpty(cachedEditorRoot)) return cachedEditorRoot;

            string bakeryPath = null;
            try { bakeryPath = ftLightmaps.GetEditorPath(); } catch { }
            if (!string.IsNullOrEmpty(bakeryPath))
            {
                cachedEditorRoot = ToAbsolutePath(bakeryPath);
                return cachedEditorRoot;
            }

            var guids = AssetDatabase.FindAssets("BakeryToolPaths t:MonoScript");
            if (guids != null && guids.Length > 0)
            {
                string scriptPath = AssetDatabase.GUIDToAssetPath(guids[0]);
                string editorDir = Path.GetDirectoryName(scriptPath);
                string modDir = Path.GetDirectoryName(editorDir);
                string root = Path.GetDirectoryName(modDir);
                if (!string.IsNullOrEmpty(root))
                {
                    cachedEditorRoot = ToAbsolutePath(root);
                    return cachedEditorRoot;
                }
            }

            cachedEditorRoot = Path.Combine(Application.dataPath, "Editor/x64/Bakery");
            return cachedEditorRoot;
        }
    }

    public static string MarksModDirectory
    {
        get { return Path.Combine(EditorRoot, "MarksMOD"); }
    }

    public static string NativeDirectory
    {
        get { return Path.Combine(MarksModDirectory, "Native"); }
    }

    public static string PayloadDirectory
    {
        get { return EditorRoot; }
    }

    public static string ToolsDirectory
    {
        get { return IsLinuxEditor ? Path.Combine(NativeDirectory, "dist~/bin") : EditorRoot; }
    }

    public static string HostDirectory
    {
        get { return Path.Combine(NativeDirectory, "dist~/bin"); }
    }

    public static string LibrariesDirectory
    {
        get
        {
            if (!string.IsNullOrEmpty(cachedLibrariesDirectory)) return cachedLibrariesDirectory;

            var guids = AssetDatabase.FindAssets("libxatlasLib_wine", null);
            if (guids != null)
            {
                for (int i = 0; i < guids.Length; i++)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                    string dir = Path.GetDirectoryName(ToAbsolutePath(path));
                    if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                    {
                        cachedLibrariesDirectory = dir;
                        return dir;
                    }
                }
            }

            var files = Directory.GetFiles(Application.dataPath, "libxatlasLib_wine.so", SearchOption.AllDirectories);
            if (files != null && files.Length > 0)
            {
                cachedLibrariesDirectory = Path.GetDirectoryName(files[0]);
                return cachedLibrariesDirectory;
            }

            return Path.Combine(Application.dataPath, "Plugins/x86_64");
        }
    }

    public static string NativeBuildLibraryDirectory
    {
        get { return Path.Combine(NativeDirectory, "dist~/lib"); }
    }

    public static string GetToolName(string logicalName)
    {
        return IsLinuxEditor ? logicalName : logicalName + ".exe";
    }

    public static string GetToolPath(string logicalName)
    {
        return Path.Combine(ToolsDirectory, GetToolName(logicalName));
    }

    public static string ToAbsolutePath(string assetOrAbsolutePath)
    {
        if (string.IsNullOrEmpty(assetOrAbsolutePath)) return assetOrAbsolutePath;
        string normalized = assetOrAbsolutePath.Replace('\\', '/');
        if (Path.IsPathRooted(normalized)) return Path.GetFullPath(normalized);
        normalized = normalized.TrimStart('/');
        if (!normalized.StartsWith("Assets/")) normalized = "Assets/" + normalized;
        return Path.GetFullPath(Path.Combine(ProjectRoot, normalized));
    }

    public static string ToAssetPath(string absolutePath)
    {
        string root = ProjectRoot.Replace('\\', '/') + "/";
        string full = Path.GetFullPath(absolutePath).Replace('\\', '/');
        return full.StartsWith(root, System.StringComparison.Ordinal) ? full.Substring(root.Length) : full;
    }
}
#endif
