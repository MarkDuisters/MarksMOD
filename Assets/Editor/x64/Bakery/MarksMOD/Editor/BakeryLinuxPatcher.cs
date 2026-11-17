#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

public static class BakeryLinuxPatcher
{
    public const int PatchVersion = 18;

    static readonly string[] Targets =
    {
        "BakeryEditorAssembly.asmdef",
        "ftConvertSH.cs",
        "ftDetectSettings.cs",
        "ftUpdater.cs",
        "ftBuildGraphics.cs",
        "ftRenderLightmap.cs",
        "ftClearCache.cs"
    };

    static readonly string[] LegacyTargets =
    {
        "ftModelPostProcessor.cs"
    };

    static readonly string[] PluginShims =
    {
        "libbakery_glue.so",
        "libfrender.so",
        "libxatlasLib.so",
        "libxatlasLib_wine.so",
        "libsimpleProgressBar.so",
        "libhalffloat2vb.so",
        "libuvrepack.so",
        "libuser32.so"
    };

    public static IReadOnlyList<string> PatchTargets
    {
        get { return Targets; }
    }

    public class PatchResult
    {
        public bool Success;
        public bool Changed;
        public readonly List<string> Messages = new List<string>();
        public readonly List<string> Errors = new List<string>();
    }

    public static string ScriptsDirectory
    {
        get { return Path.Combine(BakeryToolPaths.PayloadDirectory, "scripts"); }
    }

    public static string BackupDirectory
    {
        get { return Path.Combine(BakeryToolPaths.NativeDirectory, "backup~"); }
    }

    public static bool IsPatched
    {
        get
        {
            foreach (var name in Targets)
            {
                string path = Path.Combine(ScriptsDirectory, name);
                if (!File.Exists(path)) return false;
                if (!BakeryLinuxPatchRules.IsFilePatched(name, File.ReadAllText(path))) return false;
            }
            return true;
        }
    }

    public static PatchResult Apply()
    {
        return ApplyTo(ScriptsDirectory, BackupDirectory);
    }

    public static PatchResult ApplyTo(string scriptsDirectory, string backupDirectory)
    {
        var result = new PatchResult();
        if (!Directory.Exists(scriptsDirectory))
        {
            result.Errors.Add("Bakery scripts directory not found: " + scriptsDirectory);
            return result;
        }

        var pending = new List<KeyValuePair<string, string>>();

        foreach (var name in Targets)
        {
            string path = Path.Combine(scriptsDirectory, name);
            if (!File.Exists(path))
            {
                result.Errors.Add(name + ": file not found");
                continue;
            }

            string original;
            try
            {
                original = File.ReadAllText(path);
            }
            catch (Exception e)
            {
                result.Errors.Add(name + ": " + e.Message);
                continue;
            }

            if (BakeryLinuxPatchRules.IsFilePatched(name, original))
            {
                result.Messages.Add(name + ": already patched");
                continue;
            }

            var file = new BakeryLinuxPatchFile(name, original);
            try
            {
                BakeryLinuxPatchRules.Apply(name, file);
            }
            catch (Exception e)
            {
                result.Errors.Add(name + ": " + e.Message);
                continue;
            }

            if (file.Errors.Count > 0)
            {
                foreach (var error in file.Errors) result.Errors.Add(name + ": " + error);
                continue;
            }

            if (file.Text == original)
            {
                result.Messages.Add(name + ": no changes");
                continue;
            }

            foreach (var note in file.Notes) result.Messages.Add(name + ": " + note);
            pending.Add(new KeyValuePair<string, string>(path, file.Text));
        }

        if (result.Errors.Count > 0)
        {
            result.Success = false;
            return result;
        }

        foreach (var pair in pending)
        {
            try
            {
                Directory.CreateDirectory(backupDirectory);
                string backup = Path.Combine(backupDirectory, Path.GetFileName(pair.Key) + "." + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".bak");
                File.Copy(pair.Key, backup, true);
                File.WriteAllText(pair.Key, pair.Value, new UTF8Encoding(false));
                result.Changed = true;
            }
            catch (Exception e)
            {
                result.Errors.Add(Path.GetFileName(pair.Key) + ": " + e.Message);
            }
        }

        if (result.Errors.Count == 0 && result.Changed && IsInsideProject(scriptsDirectory))
        {
            AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
            AssetDatabase.SaveAssets();
        }

        result.Success = result.Errors.Count == 0;
        return result;
    }

    public static PatchResult Restore()
    {
        return RestoreFrom(ScriptsDirectory, BackupDirectory, true);
    }

    public static PatchResult RestoreAndRemoveMod()
    {
        var result = new PatchResult();

        if (BakeryLinuxSetup.IsRunning)
        {
            result.Errors.Add("Wine setup is running; cancel it before removing MarksMOD.");
            return result;
        }
        if (BakeryLinuxProbeRunner.Running)
        {
            result.Errors.Add("The bridge test is running; wait for it to finish before removing MarksMOD.");
            return result;
        }
        if (BakeryProgress.IsShown)
        {
            result.Errors.Add("A bake is in progress; wait for it to finish before removing MarksMOD.");
            return result;
        }

        bool hasBackups = false;
        try
        {
            hasBackups = Directory.Exists(BackupDirectory) &&
                Directory.GetFiles(BackupDirectory, "*.bak").Length > 0;
        }
        catch { }

        if (hasBackups)
        {
            result = RestoreFrom(ScriptsDirectory, BackupDirectory, false);
            if (!result.Success) return result;
        }
        else if (AnyPatchApplied())
        {
            result.Errors.Add("No patch backups found; cannot safely restore the patched Bakery files.");
            return result;
        }
        else
        {
            result.Messages.Add("No patch backups found; Bakery appears unpatched.");
        }

        if (Application.platform == RuntimePlatform.LinuxEditor)
        {
            BakeryWineHost.Stop();

            if (BakeryWineHost.IsListening())
            {
                result.Messages.Add("Wine host is still listening; left the Wine prefix in place: " +
                    BakeryLinuxSetup.PrefixPath);
            }
            else
            {
                string prefix = BakeryLinuxSetup.PrefixPath;
                if (!Directory.Exists(prefix))
                {
                    result.Messages.Add("Wine prefix already absent: " + prefix);
                }
                else
                {
                    string error;
                    if (BakeryLinuxSetup.RemoveWinePrefix(out error))
                    {
                        result.Messages.Add("Removed Wine prefix: " + prefix);
                        result.Changed = true;
                    }
                    else
                    {
                        result.Messages.Add("Left Wine prefix in place: " + prefix + " (" + error + ")");
                    }
                }
            }
        }

        RemovePluginShims(result);

        string modAssetPath = BakeryToolPaths.ToAssetPath(BakeryToolPaths.MarksModDirectory);
        try
        {
            if (AssetDatabase.IsValidFolder(modAssetPath))
            {
                if (AssetDatabase.DeleteAsset(modAssetPath))
                {
                    result.Messages.Add("Removed " + modAssetPath);
                    result.Changed = true;
                }
                else
                {
                    result.Errors.Add("Could not delete " + modAssetPath);
                }
            }
            else
            {
                result.Messages.Add("MarksMOD folder not found: " + modAssetPath);
            }
        }
        catch (Exception e)
        {
            result.Errors.Add("Could not delete " + modAssetPath + ": " + e.Message);
        }

        try
        {
            AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
        }
        catch { }

        result.Success = result.Errors.Count == 0;
        return result;
    }

    static bool AnyPatchApplied()
    {
        foreach (var name in Targets)
        {
            string path = Path.Combine(ScriptsDirectory, name);
            if (!File.Exists(path)) continue;

            try
            {
                if (BakeryLinuxPatchRules.IsFilePatched(name, File.ReadAllText(path))) return true;
            }
            catch
            {
                return true;
            }
        }

        return false;
    }

    static void RemovePluginShims(PatchResult result)
    {
        string directory = BakeryToolPaths.LibrariesDirectory;
        if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
        {
            result.Messages.Add("Plugin shim directory not found: " + directory);
            return;
        }

        foreach (var name in PluginShims)
        {
            string path = Path.Combine(directory, name);
            string meta = path + ".meta";
            try
            {
                bool removed = false;
                if (File.Exists(path))
                {
                    File.Delete(path);
                    removed = true;
                }
                if (File.Exists(meta))
                {
                    File.Delete(meta);
                    removed = true;
                }

                if (removed)
                {
                    result.Messages.Add("Removed plugin shim: " + name);
                    result.Changed = true;
                }
            }
            catch (Exception e)
            {
                result.Errors.Add("Could not remove " + name + ": " + e.Message);
            }
        }
    }

    public static PatchResult RestoreFrom(string scriptsDirectory, string backupDirectory, bool refresh = true)
    {
        var result = new PatchResult();
        if (!Directory.Exists(backupDirectory))
        {
            result.Errors.Add("No patch backups found: " + backupDirectory);
            return result;
        }

        var restoreTargets = new List<string>(Targets);
        restoreTargets.AddRange(LegacyTargets);

        foreach (var name in restoreTargets)
        {
            string[] backups = Directory.GetFiles(backupDirectory, name + ".*.bak");
            if (backups.Length == 0)
            {
                if (Array.IndexOf(LegacyTargets, name) >= 0)
                {
                    result.Messages.Add(name + ": no backup; skipped");
                    continue;
                }
                result.Errors.Add(name + ": no backup");
                continue;
            }

            Array.Sort(backups, StringComparer.Ordinal);
            string latest = backups[backups.Length - 1];
            try
            {
                File.Copy(latest, Path.Combine(scriptsDirectory, name), true);
                result.Messages.Add(name + ": restored from " + Path.GetFileName(latest));
                result.Changed = true;
            }
            catch (Exception e)
            {
                result.Errors.Add(name + ": " + e.Message);
            }
        }

        if (refresh && result.Errors.Count == 0 && result.Changed && IsInsideProject(scriptsDirectory))
        {
            AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
            AssetDatabase.SaveAssets();
        }

        result.Success = result.Errors.Count == 0;
        return result;
    }

    static bool IsInsideProject(string directory)
    {
        string root = Path.GetDirectoryName(Application.dataPath);
        string full = Path.GetFullPath(directory);
        return full.StartsWith(Path.GetFullPath(root) + Path.DirectorySeparatorChar, StringComparison.Ordinal);
    }
}
#endif
