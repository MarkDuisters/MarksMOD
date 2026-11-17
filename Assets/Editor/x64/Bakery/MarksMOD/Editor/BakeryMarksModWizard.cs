#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

public class BakeryMarksModWizard : EditorWindow
{
    List<BakeryCheckResult> results = new List<BakeryCheckResult>();
    List<BakeryCheckResult> requirementResults;
    string requirementSummary;
    MessageType requirementSummaryType;
    string xatlasStatus = "";
    bool xatlasActive;
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
        RefreshXatlasState();
    }

    void RefreshXatlasState()
    {
        xatlasActive = BakeryXatlasBackendService.IsActive;
        xatlasStatus = BakeryXatlasBackendService.GetStatus();
    }

    int RunRequirements()
    {
        requirementResults = BakeryLinuxRequirements.Run();
        scroll = Vector2.zero;

        int ok = 0;
        int warn = 0;
        int fail = 0;
        var text = new StringBuilder();
        text.AppendLine("[Bakery MarksMOD] System requirements:");
        foreach (var result in requirementResults)
        {
            if (result.Status == BakeryCheckStatus.Ok) ok++;
            else if (result.Status == BakeryCheckStatus.Warn) warn++;
            else fail++;

            text.AppendLine(result.Status.ToString().ToUpperInvariant() + "  " + result.Name + ": " + result.Detail);
        }

        requirementSummary = "Requirements: " + ok + " OK, " + warn + " warnings, " + fail + " failures.";
        requirementSummaryType = fail > 0
            ? MessageType.Error
            : warn > 0 ? MessageType.Warning : MessageType.Info;
        Debug.Log(text.ToString());
        return fail;
    }

    void PatchAll()
    {
        if (BakeryLinuxSetup.IsRunning)
        {
            EditorUtility.DisplayDialog("Bakery", "Wine setup is already running.", "OK");
            return;
        }

        int failures = RunRequirements();
        if (failures > 0)
        {
            EditorUtility.DisplayDialog(
                "Bakery / MarksMOD",
                "Requirements check found " + failures + " failure(s).\n\n" +
                "Fix the items marked FAIL in the System requirements list, then press PATCH! again.",
                "OK");
            return;
        }

        if (!BakeryLinuxSetup.PrefixLooksReady)
        {
            BakeryLinuxSetup.RunSetup(PatchBakery);
            return;
        }

        PatchBakery();
    }

    void OnGUI()
    {
        EditorGUILayout.LabelField("Bakery Linux setup wizard", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "PATCH! checks the system requirements, runs Wine setup when needed, then patches Bakery. " +
            "Use Activate xatlas afterwards and restart Unity. Re-check verifies the result.",
            MessageType.None);

        EditorGUILayout.BeginHorizontal();
        using (new EditorGUI.DisabledScope(BakeryLinuxSetup.IsRunning))
        {
            if (GUILayout.Button("PATCH!")) PatchAll();
            if (GUILayout.Button("Activate xatlas")) ActivateXatlas();
        }
        EditorGUILayout.EndHorizontal();
        EditorGUILayout.HelpBox(
            "xatlas uses the xatlasLib.dll shipped with your Bakery installation " +
            "in its scripts/xatlas folder through the Wine host.", MessageType.None);
        EditorGUILayout.LabelField(xatlasStatus, EditorStyles.miniLabel);
        if (!xatlasActive)
        {
            EditorGUILayout.HelpBox(
                "Bakery's xatlas proxy is not active. Use Activate xatlas, then restart Unity.",
                MessageType.Warning);
        }

        EditorGUILayout.Space();
        scroll = EditorGUILayout.BeginScrollView(scroll);
        if (requirementResults != null)
        {
            EditorGUILayout.LabelField("System requirements", EditorStyles.boldLabel);
            if (!string.IsNullOrEmpty(requirementSummary))
            {
                EditorGUILayout.HelpBox(requirementSummary, requirementSummaryType);
            }
            foreach (var result in requirementResults) DrawRow(result);
            EditorGUILayout.Space();
        }

        EditorGUILayout.LabelField("Wine environment", EditorStyles.boldLabel);
        if (results != null)
        {
            foreach (var result in results) DrawRow(result);
        }
        EditorGUILayout.EndScrollView();

        EditorGUILayout.Space();
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Re-check")) Recheck();
        if (GUILayout.Button("Test Bridge")) BakeryLinuxProbeRunner.Start("--ping");
        if (GUILayout.Button("Open Log")) OpenLog();
        if (GUILayout.Button("Restore Bakery")) RestoreBakery();
        EditorGUILayout.EndHorizontal();

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

    static void ActivateXatlas()
    {
        string error;
        if (BakeryXatlasBackendService.Activate(out error))
        {
            EditorUtility.DisplayDialog(
                "xatlas backend",
                "Activated Bakery's built-in xatlas. Restart Unity before baking; native plugins may already be loaded.",
                "OK");
            RefreshIfOpen();
        }
        else
        {
            EditorUtility.DisplayDialog("xatlas backend", error, "OK");
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
            EditorUtility.DisplayDialog(
                "Bakery",
                "No in-editor setup log yet:\n" + path +
                "\n\nThe log is written after Wine setup finishes. A manual bootstrap.sh run writes to the terminal instead.",
                "OK");
            return;
        }
        EditorUtility.RevealInFinder(path);
    }
}
#endif
