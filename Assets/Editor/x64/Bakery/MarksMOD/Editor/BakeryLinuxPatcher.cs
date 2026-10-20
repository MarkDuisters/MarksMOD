#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

public static class BakeryLinuxPatcher
{
    public const int PatchVersion = 1;

    static readonly string[] Targets =
    {
        "BakeryEditorAssembly.asmdef",
        "ftConvertSH.cs",
        "ftDetectSettings.cs",
        "ftUpdater.cs",
        "ftBuildGraphics.cs",
        "ftRenderLightmap.cs"
    };

    public class PatchResult
    {
        public bool Success;
        public bool Changed;
        public readonly List<string> Messages = new List<string>();
        public readonly List<string> Errors = new List<string>();
    }

    public static string ScriptsDirectory
    {
        get { return Application.dataPath + "/Editor/x64/Bakery/scripts"; }
    }

    public static string BackupDirectory
    {
        get { return Application.dataPath + "/Editor/x64/Bakery/MarksMOD/Native/backup~"; }
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
        var result = new PatchResult();
        if (!Directory.Exists(ScriptsDirectory))
        {
            result.Errors.Add("Bakery scripts directory not found: " + ScriptsDirectory);
            return result;
        }

        var pending = new List<KeyValuePair<string, string>>();

        foreach (var name in Targets)
        {
            string path = Path.Combine(ScriptsDirectory, name);
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
                Directory.CreateDirectory(BackupDirectory);
                string backup = Path.Combine(BackupDirectory, Path.GetFileName(pair.Key) + "." + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".bak");
                File.Copy(pair.Key, backup, true);
                File.WriteAllText(pair.Key, pair.Value, new UTF8Encoding(false));
                result.Changed = true;
            }
            catch (Exception e)
            {
                result.Errors.Add(Path.GetFileName(pair.Key) + ": " + e.Message);
            }
        }

        if (result.Errors.Count == 0 && result.Changed)
        {
            AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
            AssetDatabase.SaveAssets();
        }

        result.Success = result.Errors.Count == 0;
        return result;
    }

    public static PatchResult Restore()
    {
        var result = new PatchResult();
        if (!Directory.Exists(BackupDirectory))
        {
            result.Errors.Add("No patch backups found: " + BackupDirectory);
            return result;
        }

        foreach (var name in Targets)
        {
            string[] backups = Directory.GetFiles(BackupDirectory, name + ".*.bak");
            if (backups.Length == 0)
            {
                result.Errors.Add(name + ": no backup");
                continue;
            }

            Array.Sort(backups, StringComparer.Ordinal);
            string latest = backups[backups.Length - 1];
            try
            {
                File.Copy(latest, Path.Combine(ScriptsDirectory, name), true);
                result.Messages.Add(name + ": restored from " + Path.GetFileName(latest));
                result.Changed = true;
            }
            catch (Exception e)
            {
                result.Errors.Add(name + ": " + e.Message);
            }
        }

        if (result.Errors.Count == 0 && result.Changed)
        {
            AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
            AssetDatabase.SaveAssets();
        }

        result.Success = result.Errors.Count == 0;
        return result;
    }
}
#endif
