#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

public class BakeryMarksModWizard : EditorWindow
{
    List<BakeryCheckResult> results = new List<BakeryCheckResult>();
    Vector2 scroll;
    Vector2 probeScroll;

    [MenuItem("Bakery/MarksMOD/Setup Wizard")]
    public static void ShowWindow()
    {
        var window = GetWindow<BakeryMarksModWizard>(false, "Bakery / MarksMOD", true);
        window.minSize = new Vector2(540, 440);
        window.Recheck();
        window.Show();
    }

    public static void RefreshIfOpen()
    {
        var windows = Resources.FindObjectsOfTypeAll<BakeryMarksModWizard>();
        if (windows == null) return;
        foreach (var window in windows)
        {
            if (window == null) continue;
            window.Recheck();
            window.Repaint();
        }
    }

    public static void RepaintIfOpen()
    {
        var windows = Resources.FindObjectsOfTypeAll<BakeryMarksModWizard>();
        if (windows == null) return;
        foreach (var window in windows)
        {
            if (window != null) window.Repaint();
        }
    }

    void OnEnable()
    {
        Recheck();
    }

    void Recheck()
    {
        results = BakeryLinuxDiagnostics.Run();
    }

    void OnGUI()
    {
        EditorGUILayout.LabelField("Bakery Linux setup wizard", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Verifies the Wine bridge and every component Bakery needs on Linux. " +
            "Run Setup first, then Patch Bakery. Re-check to verify the result.",
            MessageType.None);

        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("xatlas backend", BakeryXatlasBackendService.ActiveBackendName);
        if (GUILayout.Button("Activate", GUILayout.Width(70)))
        {
            string error;
            if (BakeryXatlasBackendService.Activate(out error))
            {
                EditorUtility.DisplayDialog(
                    "xatlas backend",
                    "Activated Bakery's built-in xatlas. Restart Unity before baking; native plugins may already be loaded.",
                    "OK");
                Recheck();
            }
            else
            {
                EditorUtility.DisplayDialog("xatlas backend", error, "OK");
            }
        }
        EditorGUILayout.EndHorizontal();
        EditorGUILayout.HelpBox(
            "xatlas uses the xatlasLib.dll shipped with your Bakery installation " +
            "(Editor/x64/Bakery/scripts/xatlas) through the Wine host.", MessageType.None);
        EditorGUILayout.LabelField(BakeryXatlasBackendService.GetStatus(), EditorStyles.miniLabel);
        if (!BakeryXatlasBackendService.IsActive)
        {
            EditorGUILayout.HelpBox(
                "Bakery's xatlas proxy is not active. Click Activate, then restart Unity.",
                MessageType.Warning);
        }

        EditorGUILayout.Space();
        scroll = EditorGUILayout.BeginScrollView(scroll);
        if (results != null)
        {
            foreach (var result in results) DrawRow(result);
        }
        EditorGUILayout.EndScrollView();

        EditorGUILayout.Space();
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Run Setup")) BakeryLinuxSetup.RunSetup();
        if (GUILayout.Button("Patch Bakery")) PatchBakery();
        if (GUILayout.Button("Restore Bakery")) RestoreBakery();
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space();
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Re-check")) Recheck();
        if (GUILayout.Button("Test Bridge")) BakeryLinuxProbeRunner.Start("--ping");
        if (GUILayout.Button("Open Log")) OpenLog();
        EditorGUILayout.EndHorizontal();

        BakeryLinuxSetup.ShowSetupAtStart = EditorGUILayout.ToggleLeft("Show setup at start", BakeryLinuxSetup.ShowSetupAtStart);

        if (BakeryLinuxProbeRunner.Running)
        {
            EditorGUILayout.HelpBox("Running probe.sh: starting the Wine host and sending CMD_PING...", MessageType.Info);
        }
        else if (BakeryLinuxProbeRunner.Done)
        {
            EditorGUILayout.HelpBox(
                BakeryLinuxProbeRunner.Success ? "probe.sh passed." : "probe.sh reported failures.",
                BakeryLinuxProbeRunner.Success ? MessageType.Info : MessageType.Warning);
        }

        if (!string.IsNullOrEmpty(BakeryLinuxProbeRunner.Output))
        {
            EditorGUILayout.LabelField("probe.sh output", EditorStyles.boldLabel);
            probeScroll = EditorGUILayout.BeginScrollView(probeScroll, GUILayout.Height(180));
            EditorGUILayout.TextArea(BakeryLinuxProbeRunner.Output, GUILayout.ExpandHeight(true));
            EditorGUILayout.EndScrollView();
        }
    }

    static void PatchBakery()
    {
        Report(BakeryLinuxPatcher.Apply(), "Bakery patched. Unity will recompile the scripts.");
    }

    static void RestoreBakery()
    {
        if (!EditorUtility.DisplayDialog(
            "Bakery / MarksMOD",
            "This restores all patched Bakery files and removes the MarksMOD folder from Bakery.\n\n" +
            "Linux plugin shims in Assets/Plugins are kept.\n\nContinue?",
            "Restore and remove",
            "Cancel"))
        {
            return;
        }

        var result = BakeryLinuxPatcher.RestoreAndRemoveMod();
        if (!result.Success)
        {
            Report(result, "Bakery restore failed.");
            return;
        }

        Debug.Log("[Bakery MarksMOD] Bakery restored and MarksMOD removed from Bakery.\n" + string.Join("\n", result.Messages.ToArray()));
        EditorUtility.DisplayDialog("Bakery / MarksMOD", "Bakery restored. MarksMOD was removed from Bakery. Unity will recompile.", "OK");
    }

    static void Report(BakeryLinuxPatcher.PatchResult result, string successMessage)
    {
        if (result.Success)
        {
            Debug.Log("[Bakery MarksMOD] " + successMessage + "\n" + string.Join("\n", result.Messages.ToArray()));
        }
        else
        {
            Debug.LogError("[Bakery MarksMOD] " + string.Join("\n", result.Errors.ToArray()));
        }

        EditorUtility.DisplayDialog(
            "Bakery / MarksMOD",
            result.Success ? successMessage : "Operation failed:\n\n" + string.Join("\n", result.Errors.ToArray()),
            "OK");

        RefreshIfOpen();
    }

    void DrawRow(BakeryCheckResult result)
    {
        if (result == null) return;

        EditorGUILayout.BeginHorizontal();
        var old = GUI.color;
        GUI.color = result.Status == BakeryCheckStatus.Ok
            ? new Color(0.4f, 0.9f, 0.4f)
            : result.Status == BakeryCheckStatus.Warn
                ? new Color(1f, 0.8f, 0.2f)
                : new Color(1f, 0.4f, 0.4f);
        GUILayout.Label(result.Status.ToString().ToUpperInvariant(), GUILayout.Width(48));
        GUI.color = old;
        GUILayout.Label(result.Name, GUILayout.Width(160));
        GUILayout.Label(result.Detail ?? string.Empty);
        EditorGUILayout.EndHorizontal();
    }

    static void OpenLog()
    {
        string path = BakeryLinuxSetup.LogPath;
        if (!File.Exists(path))
        {
            EditorUtility.DisplayDialog("Bakery", "No setup log yet:\n" + path, "OK");
            return;
        }
        EditorUtility.RevealInFinder(path);
    }
}
#endif
