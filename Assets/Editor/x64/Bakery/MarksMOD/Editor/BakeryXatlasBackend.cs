#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEngine;

public static class BakeryXatlasBackendService
{
    const string ActiveLibraryName = "libxatlasLib.so";
    const string ProxyLibraryName = "libxatlasLib_wine.so";

    public static string ActivePath
    {
        get { return Path.Combine(BakeryToolPaths.LibrariesDirectory, ActiveLibraryName); }
    }

    public static string ProxyPath
    {
        get { return Path.Combine(BakeryToolPaths.LibrariesDirectory, ProxyLibraryName); }
    }

    public static string BakeryXatlasDllPath
    {
        get { return Path.Combine(BakeryToolPaths.PayloadDirectory, "scripts", "xatlas", "xatlasLib.dll"); }
    }

    public static string ActiveBackendName
    {
        get { return IsActive ? "Bakery built-in (Wine)" : "Inactive"; }
    }

    public static bool IsActive
    {
        get { return FilesMatch(ActivePath, ProxyPath); }
    }

    public static string[] RequiredFiles()
    {
        return new[]
        {
            ProxyPath,
            ActivePath,
            Path.Combine(BakeryToolPaths.HostDirectory, "bakery_wine_host.exe"),
            BakeryXatlasDllPath
        };
    }

    public static bool RequiredFilesExist(out string missing)
    {
        var missingFiles = string.Empty;
        foreach (var path in RequiredFiles())
        {
            if (path == ActivePath || File.Exists(path)) continue;
            missingFiles += (missingFiles.Length == 0 ? "" : ", ") + path;
        }
        missing = missingFiles;
        return missingFiles.Length == 0;
    }

    public static bool Activate(out string error)
    {
        error = null;
        string missing;
        if (!RequiredFilesExist(out missing))
        {
            error = "Bakery xatlas cannot be activated because required files are missing:\n" + missing;
            return false;
        }
        if (!BakeryWineHost.EnsureStarted())
        {
            error = "The Wine host did not become ready. See /tmp/bakery_wine_host.log.";
            return false;
        }

        try
        {
            string staging = ActivePath + ".new";
            File.Copy(ProxyPath, staging, true);
            if (File.Exists(ActivePath)) File.Delete(ActivePath);
            File.Move(staging, ActivePath);
            AssetDatabase.ImportAsset(ToAssetPath(ActivePath), ImportAssetOptions.ForceUpdate);
            var importer = AssetImporter.GetAtPath(ToAssetPath(ActivePath)) as PluginImporter;
            if (importer != null)
            {
                importer.SetCompatibleWithEditor(true);
                importer.SetCompatibleWithPlatform(BuildTarget.StandaloneLinux64, true);
                importer.SaveAndReimport();
            }
            return true;
        }
        catch (Exception e)
        {
            error = "Could not activate Bakery xatlas:\n" + e.Message;
            return false;
        }
    }

    public static string GetStatus()
    {
        string missing;
        bool present = RequiredFilesExist(out missing);
        return "Bakery built-in (Wine): " + (IsActive ? "active" : "inactive") +
            (present ? ", required files present" : ", missing: " + missing) +
            ", DLL " + BakeryXatlasDllPath;
    }

    static string ToAssetPath(string path)
    {
        return BakeryToolPaths.ToAssetPath(path);
    }

    static bool FilesMatch(string left, string right)
    {
        if (!File.Exists(left) || !File.Exists(right)) return false;
        var leftInfo = new FileInfo(left);
        var rightInfo = new FileInfo(right);
        if (leftInfo.Length != rightInfo.Length) return false;
        using (var a = File.OpenRead(left))
        using (var b = File.OpenRead(right))
        {
            var leftBuffer = new byte[81920];
            var rightBuffer = new byte[81920];
            int count;
            while ((count = a.Read(leftBuffer, 0, leftBuffer.Length)) > 0)
            {
                if (b.Read(rightBuffer, 0, count) != count) return false;
                for (int i = 0; i < count; i++)
                    if (leftBuffer[i] != rightBuffer[i]) return false;
            }
        }
        return true;
    }
}
#endif
