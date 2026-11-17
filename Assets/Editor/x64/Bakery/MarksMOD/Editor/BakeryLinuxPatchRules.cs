using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

public class BakeryLinuxPatchFile
{
    public readonly string Name;
    public string Text;
    public readonly List<string> Notes = new List<string>();
    public readonly List<string> Errors = new List<string>();

    public BakeryLinuxPatchFile(string name, string text)
    {
        Name = name;
        Text = text;
    }

    public string NewLine
    {
        get { return Text.IndexOf("\r\n", StringComparison.Ordinal) >= 0 ? "\r\n" : "\n"; }
    }

    public bool Replace(string oldValue, string newValue, bool required = true)
    {
        if (string.IsNullOrEmpty(oldValue))
        {
            if (required) Errors.Add("Empty anchor");
            return false;
        }

        int count = Count(Text, oldValue);
        if (count == 0)
        {
            if (Text.IndexOf(newValue, StringComparison.Ordinal) >= 0) return false;
            if (required) Errors.Add("Missing anchor: " + Abbreviate(oldValue));
            return false;
        }

        Text = Text.Replace(oldValue, newValue);
        Notes.Add("Replaced " + count + "x " + Abbreviate(oldValue));
        return true;
    }

    public bool ReplaceLines(string oldValue, string newValue, bool required = true)
    {
        string oldNormalized = NormalizeNewLines(oldValue);
        string newNormalized = NormalizeNewLines(newValue);
        return Replace(oldNormalized, newNormalized, required);
    }

    string NormalizeNewLines(string value)
    {
        return value.Replace("\r\n", "\n").Replace("\n", NewLine);
    }

    public bool ReplaceRegex(string pattern, MatchEvaluator evaluator, bool required = true)
    {
        var regex = new Regex(pattern, RegexOptions.Multiline);
        int count = regex.Matches(Text).Count;
        if (count == 0)
        {
            if (required) Errors.Add("Missing pattern: " + Abbreviate(pattern));
            return false;
        }

        Text = regex.Replace(Text, evaluator);
        Notes.Add("Replaced " + count + "x " + Abbreviate(pattern));
        return true;
    }

    public bool InsertAfter(string anchor, string block)
    {
        if (string.IsNullOrEmpty(anchor))
        {
            Errors.Add("Empty anchor");
            return false;
        }

        int index = Text.IndexOf(anchor, StringComparison.Ordinal);
        if (index < 0)
        {
            Errors.Add("Missing anchor: " + Abbreviate(anchor));
            return false;
        }

        int at = index + anchor.Length;
        int window = Math.Min(400, Text.Length - at);
        if (window > 0 && Text.Substring(at, window).IndexOf(block, StringComparison.Ordinal) >= 0) return false;

        Text = Text.Insert(at, NewLine + block);
        Notes.Add("Inserted block after " + Abbreviate(anchor));
        return true;
    }

    static int Count(string text, string value)
    {
        if (string.IsNullOrEmpty(value)) return 0;

        int count = 0;
        int index = 0;
        while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }
        return count;
    }

    static string Abbreviate(string value)
    {
        string oneLine = value.Replace("\r", string.Empty).Replace("\n", " ");
        return oneLine.Length <= 60 ? oneLine : oneLine.Substring(0, 57) + "...";
    }
}

public static class BakeryLinuxPatchRules
{
    public static bool IsFilePatched(string fileName, string text)
    {
        switch (fileName)
        {
            case "BakeryEditorAssembly.asmdef": return text.IndexOf("MarksMOD.Core", StringComparison.Ordinal) >= 0;
            case "ftConvertSH.cs": return text.IndexOf("BakeryProcess.RunLocalProcess", StringComparison.Ordinal) >= 0;
            case "ftDetectSettings.cs": return text.IndexOf("BakeryProcess.RunLocalProcess", StringComparison.Ordinal) >= 0;
            case "ftUpdater.cs": return text.IndexOf("BakeryProcess.ExtractZIP", StringComparison.Ordinal) >= 0;
            case "ftBuildGraphics.cs": return text.IndexOf("BakeryProgress.Show", StringComparison.Ordinal) >= 0 &&
                text.IndexOf("AddTempPinnedHandle", StringComparison.Ordinal) >= 0 &&
                text.IndexOf("MoveObjectToAtlasGroup", StringComparison.Ordinal) >= 0 &&
                text.IndexOf("skipping the probe lightmap group", StringComparison.Ordinal) >= 0 &&
                text.IndexOf("public static ftGlobalStorage.AtlasPacker atlasPacker = ftGlobalStorage.AtlasPacker.Default;", StringComparison.Ordinal) < 0 &&
                text.IndexOf("if (packer == ftGlobalStorage.AtlasPacker.xatlas) packer = ftGlobalStorage.AtlasPacker.Default;", StringComparison.Ordinal) < 0;
            case "ftRenderLightmap.cs": return text.IndexOf("BakeryProcess.RunLocalProcess", StringComparison.Ordinal) >= 0 &&
                text.IndexOf("ResolveProbeLightingDataAssetPath", StringComparison.Ordinal) >= 0 &&
                text.IndexOf("BakeryWineHost.EnsureStarted", StringComparison.Ordinal) >= 0 &&
                text.IndexOf("startInfo.FileName.EndsWith(\"ftrace.exe\")", StringComparison.Ordinal) >= 0 &&
                text.IndexOf("linuxLegacyDenoiserWarned", StringComparison.Ordinal) >= 0 &&
                text.IndexOf("GUI.enabled = oldEnabled && ftBuildGraphics.unwrapUVs;", StringComparison.Ordinal) >= 0 &&
                text.IndexOf("EditorGUILayout.LabelField(\"Atlas packer\"", StringComparison.Ordinal) < 0 &&
                text.IndexOf("lmname + \"_final_Dir\" + (compressedOutput ? \".lz4\" : \".dds\") + \"\\\"\"", StringComparison.Ordinal) >= 0 &&
                text.IndexOf("if (ftBuildGraphics.atlasPacker == ftGlobalStorage.AtlasPacker.xatlas) ftBuildGraphics.atlasPacker = ftGlobalStorage.AtlasPacker.Default;", StringComparison.Ordinal) < 0;
            case "ftClearCache.cs": return text.IndexOf("Path.GetTempPath()", StringComparison.Ordinal) >= 0 &&
                text.IndexOf("GetEnvironmentVariable(\"TEMP\"", StringComparison.Ordinal) < 0;
            default: return false;
        }
    }

    public static void Apply(string fileName, BakeryLinuxPatchFile file)
    {
        switch (fileName)
        {
            case "BakeryEditorAssembly.asmdef": PatchAssembly(file); break;
            case "ftConvertSH.cs": PatchConvertSH(file); break;
            case "ftDetectSettings.cs": PatchDetectSettings(file); break;
            case "ftUpdater.cs": PatchUpdater(file); break;
            case "ftBuildGraphics.cs": PatchBuildGraphics(file); break;
            case "ftRenderLightmap.cs": PatchRenderLightmap(file); break;
            case "ftClearCache.cs": PatchClearCache(file); break;
            default: file.Errors.Add("Unknown file: " + fileName); break;
        }
    }

    static void PatchAssembly(BakeryLinuxPatchFile f)
    {
        if (f.Text.IndexOf("MarksMOD.Core", StringComparison.Ordinal) >= 0) return;
        f.Replace("\"BakeryAPV\"", "\"BakeryAPV\"," + f.NewLine + "        \"MarksMOD.Core\"");
    }

    static void PatchConvertSH(BakeryLinuxPatchFile f)
    {
        f.Replace("ftRenderLightmap.RunLocalProcess(", "BakeryProcess.RunLocalProcess(");
        f.Replace("ftRenderLightmap.IsProcessFinished(", "BakeryProcess.IsProcessFinished(");
        f.Replace("ftRenderLightmap.GetProcessReturnValueAndClose(", "BakeryProcess.GetProcessReturnValueAndClose(");
    }

    static void PatchDetectSettings(BakeryLinuxPatchFile f)
    {
        f.Replace("= RunLocalProcess(", "= BakeryProcess.RunLocalProcess(");
        f.Replace("!IsProcessFinished(", "!BakeryProcess.IsProcessFinished(");
        f.Replace("lastReturnValue = GetProcessReturnValueAndClose(", "lastReturnValue = BakeryProcess.GetProcessReturnValueAndClose(");
        f.Replace("simpleProgressBarShow(progressHeader,", "BakeryProgress.Show(progressHeader,");
        f.Replace("if (benchParams.showWindow) simpleProgressBarEnd();", "if (benchParams.showWindow) BakeryProgress.End();");
        f.ReplaceRegex(@"(?m)^([ \t]*)simpleProgressBarEnd\(\);", m => m.Groups[1].Value + "BakeryProgress.End();", false);
        f.Replace("= simpleProgressBarCancelled();", "= BakeryProgress.Cancelled;");
    }

    static void PatchUpdater(BakeryLinuxPatchFile f)
    {
        f.Replace("err = ExtractZIP(", "err = BakeryProcess.ExtractZIP(");
    }

    static void PatchBuildGraphics(BakeryLinuxPatchFile f)
    {
        RemoveBuildGraphicsXatlasFallbacks(f);

        f.ReplaceLines(
            "            // Create additional lightmaps\n            AllocateAutoAtlas(atlasCount-1, lmgroup, data, atlasSizes);",
            "            // Create additional lightmaps\n            int generatedAtlasStart = autoAtlasGroups.Count;\n            AllocateAutoAtlas(atlasCount-1, lmgroup, data, atlasSizes);");

        f.ReplaceLines(
            "            }\n\n            for(int i=0; i<=bSize; i++)\n            {\n                // Get data from xatlas",
            "            }\n            else if (atlasCount > 1)\n            {\n                // xatlas UVs are local to their atlas. Explicit groups must therefore\n                // use the generated group for every non-zero atlas as well.\n                for(int i=0; i<=bSize; i++)\n                {\n                    int atlasIndex = xatlas.xatlasGetAtlasIndex(atlas, i, 0);\n                    var holder = holderObjs[bStart + i];\n                    var targetGroup = atlasIndex == 0 ? lmgroup : autoAtlasGroups[generatedAtlasStart + atlasIndex - 1];\n                    if (atlasIndex != 0) targetGroup.parentName = lmgroup.parentName;\n                    MoveObjectToAtlasGroup(holder, targetGroup, data);\n\n                    for(int o=0; o<objsToWriteHolder.Count; o++)\n                    {\n                        if (objsToWriteHolder[o] != holder) continue;\n                        data.objsToWriteGroup[o] = targetGroup;\n                    }\n\n                    Debug.Log(\"[BakeryXatlasExplicit] holder=\" + holder.name + \" atlas=\" + atlasIndex +\n                        \" group=\" + targetGroup.name + \" resolution=\" + targetGroup.resolution);\n                }\n            }\n\n            for(int i=0; i<=bSize; i++)\n            {\n                // Get data from xatlas",
            false);

        f.ReplaceLines(
            "    static void MoveObjectToImplicitGroup(GameObject holder, BakeryLightmapGroup newGroup, ExportSceneData data)",
            "    static void MoveObjectToAtlasGroup(GameObject holder, BakeryLightmapGroup newGroup, ExportSceneData data)\n    {\n        var storage = data.storages[data.sceneToID[holder.scene]];\n        tempStorage.implicitGroupMap[holder] = newGroup;\n\n        for(int k=0; k<storage.implicitGroupedObjects.Count; k++)\n        {\n            if (storage.implicitGroupedObjects[k] != holder) continue;\n            storage.implicitGroups[k] = newGroup;\n            return;\n        }\n\n        storage.implicitGroupedObjects.Add(holder);\n        storage.implicitGroups.Add(newGroup);\n    }\n\n    static void MoveObjectToImplicitGroup(GameObject holder, BakeryLightmapGroup newGroup, ExportSceneData data)");

        if (f.Text.IndexOf("static void AddTempPinnedHandle(GCHandle handle)", StringComparison.Ordinal) < 0)
        {
            f.Replace("tempPinnedHandles.Add(handle);", "AddTempPinnedHandle(handle);");
        }
        f.InsertAfter("    static List<GCHandle> tempPinnedHandles;",
            "    static void AddTempPinnedHandle(GCHandle handle)" + f.NewLine +
            "    {" + f.NewLine +
            "        if (tempPinnedHandles == null) tempPinnedHandles = new List<GCHandle>();" + f.NewLine +
            "        tempPinnedHandles.Add(handle);" + f.NewLine +
            "    }");

        f.Replace("ftRenderLightmap.simpleProgressBarShow(", "BakeryProgress.Show(");
        f.Replace("ftRenderLightmap.simpleProgressBarCancelled()", "BakeryProgress.Cancelled");
        f.Replace("ftRenderLightmap.simpleProgressBarEnd()", "BakeryProgress.End()");
        if (f.Text.IndexOf("scenePath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), \"frender\");", StringComparison.Ordinal) < 0)
        {
            f.ReplaceRegex(
                @"(?m)^([ \t]*)var tempDir = System\.Environment\.GetEnvironmentVariable\(""TEMP"", System\.EnvironmentVariableTarget\.Process\);\r?\n[ \t]*scenePath = tempDir \+ ""\\\\frender"";",
                m => m.Groups[1].Value + "scenePath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), \"frender\");");
        }

        f.ReplaceLines(
@"        var probes = LightmapSettings.lightProbes;
        if (probes == null)
        {
            DebugLogError(""No probes in LightingDataAsset"");
            yield break;
        }
        var positions = probes.positions;",
@"        var probes = LightmapSettings.lightProbes;
        if (probes == null || probes.count == 0)
        {
            ftRenderLightmap.hasAnyProbes = false;
            DebugLogWarning(""No probes in LightingDataAsset; skipping the probe lightmap group"");
            yield break;
        }
        var positions = probes.positions;");
    }

    static void PatchClearCache(BakeryLinuxPatchFile f)
    {
        if (f.Text.IndexOf("Path.GetTempPath()", StringComparison.Ordinal) >= 0) return;

        f.Replace("System.Environment.GetEnvironmentVariable(\"TEMP\", System.EnvironmentVariableTarget.Process) + \"\\\\frender\"",
                  "Path.Combine(Path.GetTempPath(), \"frender\")");
    }

    static bool RemoveBuildGraphicsXatlasFallbacks(BakeryLinuxPatchFile f)
    {
        string wrappedDeclaration = "#if UNITY_EDITOR_LINUX" + f.NewLine +
                                    "    public static ftGlobalStorage.AtlasPacker atlasPacker = ftGlobalStorage.AtlasPacker.Default;" + f.NewLine +
                                    "#else" + f.NewLine +
                                    "    public static ftGlobalStorage.AtlasPacker atlasPacker = ftGlobalStorage.AtlasPacker.xatlas;" + f.NewLine +
                                    "#endif";
        bool removed = f.ReplaceLines(wrappedDeclaration,
            "    public static ftGlobalStorage.AtlasPacker atlasPacker = ftGlobalStorage.AtlasPacker.xatlas;",
            false);

        removed |= f.ReplaceRegex(
            @"(?m)^[ \t]*#if UNITY_EDITOR_LINUX\r?\n[ \t]*if \(packer == ftGlobalStorage\.AtlasPacker\.xatlas\) packer = ftGlobalStorage\.AtlasPacker\.Default;\r?\n[ \t]*#endif\r?\n",
            m => string.Empty,
            false);

        return removed;
    }

    static void PatchRenderLightmap(BakeryLinuxPatchFile f)
    {
        f.Replace("lmname + \"_final_Dir\" + (compressedOutput ? \".lz4\" : \".dds\");",
                  "lmname + \"_final_Dir\" + (compressedOutput ? \".lz4\" : \".dds\") + \"\\\"\";",
                  false);

        AddHostLifecycleRules(f);

        f.ReplaceLines(
@"        if (settingsMode >= SettingsMode.Advanced)
        {
            this.minSize = new Vector2(position.height >= scrollHeight ? 250 : 270, 700);
        }
        this.maxSize = new Vector2(this.minSize.x, settingsMode >= SettingsMode.Advanced ? 2000 : this.minSize.y + 1);",
@"        if (Event.current.type == EventType.Layout)
        {
            if (settingsMode >= SettingsMode.Advanced)
            {
                this.minSize = new Vector2(position.height >= scrollHeight ? 250 : 270, 700);
            }
            this.maxSize = new Vector2(this.minSize.x, settingsMode >= SettingsMode.Advanced ? 2000 : this.minSize.y + 1);
        }",
            false);

        f.Replace(
            "if (settingsMode == SettingsMode.Experimental && ftBuildGraphics.unwrapUVs)",
            "if (settingsMode == SettingsMode.Experimental)",
            false);

        f.ReplaceLines(
@"            var prev = unwrapper;
            unwrapper = (ftGlobalStorage.Unwrapper)EditorGUILayout.EnumPopup(unwrapper, opts);
            if (unwrapper != prev)",
@"            bool oldEnabled = GUI.enabled;
            GUI.enabled = oldEnabled && ftBuildGraphics.unwrapUVs;
            var prev = unwrapper;
            unwrapper = (ftGlobalStorage.Unwrapper)EditorGUILayout.EnumPopup(unwrapper, opts);
            GUI.enabled = oldEnabled;
            if (ftBuildGraphics.unwrapUVs && unwrapper != prev)",
            false);

        f.ReplaceLines(
@"    public static ftGlobalStorage.DenoiserType denoiserType = ftGlobalStorage.DenoiserType.OpenImageDenoise;",
@"    public static ftGlobalStorage.DenoiserType denoiserType = ftGlobalStorage.DenoiserType.OpenImageDenoise;
    static bool linuxLegacyDenoiserWarned;",
            false);

        f.Replace(
            "if (denoiserType == ftGlobalStorage.DenoiserType.Optix5 && !gstorage.runsOptix5)",
            "if (Application.platform != RuntimePlatform.LinuxEditor && denoiserType == ftGlobalStorage.DenoiserType.Optix5 && !gstorage.runsOptix5)",
            false);
        f.Replace(
            "else if (denoiserType == ftGlobalStorage.DenoiserType.Optix6 && !gstorage.runsOptix6)",
            "else if (Application.platform != RuntimePlatform.LinuxEditor && denoiserType == ftGlobalStorage.DenoiserType.Optix6 && !gstorage.runsOptix6)",
            false);

        f.ReplaceLines(
@"    string GetDenoiseMode()
    {
        string denoiseMod;
        switch(denoiserType)
        {",
@"    string GetDenoiseMode()
    {
        if (Application.platform == RuntimePlatform.LinuxEditor &&
            (denoiserType == ftGlobalStorage.DenoiserType.Optix5 ||
             denoiserType == ftGlobalStorage.DenoiserType.Optix6))
        {
            if (!linuxLegacyDenoiserWarned)
            {
                DebugLogWarning(""OptiX 5/6 denoiser is not supported by the Linux bridge; using OptiX 7"");
                linuxLegacyDenoiserWarned = true;
            }
            return ""72"";
        }

        string denoiseMod;
        switch(denoiserType)
        {",
            false);

        f.ReplaceLines(
@"                    var app = Path.GetFileNameWithoutExtension(deferredCommands[i].FileName);
                    var origApp = app;",
@"#if UNITY_EDITOR_LINUX
                    if (startInfo.FileName.EndsWith(""ftrace.exe"") || startInfo.FileName.EndsWith(""ftraceRTX.exe""))
                    {
                        startInfo.FileName = Path.Combine(Path.GetDirectoryName(startInfo.FileName), ftraceExe9);
                        deferredCommands[i] = startInfo;
                    }
#endif

                    var app = Path.GetFileNameWithoutExtension(deferredCommands[i].FileName);
                    var origApp = app;",
            false);

        f.Replace("if (settingsMode == SettingsMode.Advanced) scrollHeight += 160;",
                  "if (settingsMode == SettingsMode.Advanced) scrollHeight += 100;",
                  false);

        f.ReplaceLines(
@"        if (settingsMode >= SettingsMode.Advanced)
        {
            EditorGUILayout.Space();
            EditorGUILayout.Space();
            EditorGUILayout.Space();
            EditorGUILayout.LabelField(""Atlas packer"", EditorStyles.boldLabel);
            ftBuildGraphics.atlasPacker = (ftGlobalStorage.AtlasPacker)EditorGUILayout.EnumPopup(ftBuildGraphics.atlasPacker, opts);
        }",
@"        if (settingsMode == SettingsMode.Experimental)
        {
            EditorGUILayout.Space();
            EditorGUILayout.Space();
            EditorGUILayout.Space();
            ftBuildGraphics.atlasPacker = (ftGlobalStorage.AtlasPacker)EditorGUILayout.EnumPopup(ftBuildGraphics.atlasPacker, opts);
        }",
            false);

        if (RemoveRenderLightmapXatlasFallbacks(f)) return;

        f.Replace("var exeProcess = RunLocalProcess(", "var exeProcess = BakeryProcess.RunLocalProcess(");
        f.Replace("while(!IsProcessFinished(", "while(!BakeryProcess.IsProcessFinished(");
        if (f.Text.IndexOf("BakeryProcess.StopProcess(exeProcess);", StringComparison.Ordinal) < 0)
        {
            f.Replace("StopProcess(exeProcess);", "BakeryProcess.StopProcess(exeProcess);");
        }
        f.Replace("lastReturnValue = GetProcessReturnValueAndClose(", "lastReturnValue = BakeryProcess.GetProcessReturnValueAndClose(");
        f.Replace("exeProcess = RunLocalProcessVisible(", "exeProcess = BakeryProcess.RunLocalProcessVisible(");
        f.Replace("exeProcess = RunLocalProcess(", "exeProcess = BakeryProcess.RunLocalProcess(");
        f.Replace("return StopProcess(process);", "return BakeryProcess.StopProcess(process);");

        f.ReplaceRegex(
            @"(?m)^([ \t]*)if \(showProgressBar\) simpleProgressBarShow\(""Bakery"", progressBarText, progressBarPercent, progressBarStep, false\);",
            m => m.Groups[1].Value + "BakeryProgress.Reset();" + f.NewLine +
                 m.Groups[1].Value + "if (showProgressBar) BakeryProgress.Show(\"Bakery\", progressBarText, progressBarPercent, progressBarStep, false);", false);

        f.ReplaceRegex(
            @"(?m)^([ \t]*)if \(showProgressBar\) simpleProgressBarShow\(""Bakery"", progressBarText, progressBarPercent, progressBarStep, onTop\);\r?\n[ \t]*userCanceled = simpleProgressBarCancelled\(\);",
            m => m.Groups[1].Value + "if (showProgressBar)" + f.NewLine +
                 m.Groups[1].Value + "{" + f.NewLine +
                 m.Groups[1].Value + "    BakeryProgress.Show(\"Bakery\", progressBarText, progressBarPercent, progressBarStep, onTop);" + f.NewLine +
                 m.Groups[1].Value + "    userCanceled = BakeryProgress.Cancelled;" + f.NewLine +
                 m.Groups[1].Value + "}", false);

        f.Replace("userCanceled = simpleProgressBarCancelled();", "userCanceled = BakeryProgress.Cancelled;");
        f.Replace("if (showProgressBar) simpleProgressBarEnd();", "BakeryProgress.End();");
        f.Replace("System.Environment.GetEnvironmentVariable(\"TEMP\", System.EnvironmentVariableTarget.Process) + \"\\\\frender\"",
                  "System.IO.Path.Combine(System.IO.Path.GetTempPath(), \"frender\")");
        f.ReplaceRegex(@"(scenePath\s*\+\s*)""\\\\", m => m.Groups[1].Value + "\"/", false);

        bool hasProbeFixes = f.Text.IndexOf("HasValidLightingDataAsset", StringComparison.Ordinal) >= 0;
        if (!hasProbeFixes)
        {
        f.ReplaceLines(
@"    public IEnumerator InitializeLightProbes(bool optional)
    {
        hasAnyProbes = true;
        var probeGroups = FindObjectsOfType(typeof(LightProbeGroup)) as LightProbeGroup[];",
@"    public IEnumerator InitializeLightProbes(bool optional)
    {
        hasAnyProbes = true;
        bool probeSavedRtxMode = rtxMode;
        bool probeSavedDistanceShadowmask = isDistanceShadowmask;
        var probeSavedRenderDirMode = renderDirMode;
        var probeSavedLightProbeMode = lightProbeMode;
        var probeGroups = FindObjectsOfType(typeof(LightProbeGroup)) as LightProbeGroup[];");

        f.ReplaceLines(
@"            if (!useUnityForOcclsusionProbes)
            {
            //for(int i=0; i<3; i++)
            //{
                //bool success = true;
                try
                {
                    File.Copy(lmdataPath, newPath, true);
                }
                catch
                {
                    //success = false;
                    Debug.LogError(""Failed copying LightingDataAsset"");
                }
                //if (success) break;
                //yield return new WaitForSeconds(1);
            //}
            }",
@"            if (!useUnityForOcclsusionProbes)
            {
                lmdataPath = ResolveProbeLightingDataAssetPath(tempScenePath);
                bool copiedProbeAsset = false;
                for(int copyAttempt = 0; copyAttempt < 3 && !copiedProbeAsset; copyAttempt++)
                {
                    if (!string.IsNullOrEmpty(lmdataPath) && TryCopyLightingDataAsset(lmdataPath, newPath))
                    {
                        copiedProbeAsset = true;
                    }
                    else
                    {
                        AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
                        AssetDatabase.SaveAssets();
                        lmdataPath = ResolveProbeLightingDataAssetPath(tempScenePath);
                    }
                }

                if (!copiedProbeAsset)
                {
                    TryDeleteEmptyFile(newPath);
                    hasAnyProbes = false;
                    DebugLogWarning(""Probe LightingDataAsset could not be copied; skipping the probe bake"");
                }
            }");

        f.ReplaceLines(
@"        string lmdataPath = null;
        string newPath = null;
        newAssetLData = null;",
@"        string lmdataPath = null;
        string newPath = null;
        string tempScenePath = null;
        newAssetLData = null;");

        f.ReplaceLines(
@"            var bakeryRuntimePath = ftLightmaps.GetRuntimePath();
            var tempScenePath = bakeryRuntimePath + ""_tempScene.unity"";",
@"            var bakeryRuntimePath = ftLightmaps.GetRuntimePath();
            tempScenePath = bakeryRuntimePath + ""_tempScene.unity"";");

        f.ReplaceLines(
@"            LoadRenderSettings(); // prevent curSector reference from unloading
            ftLightmaps.RefreshFull();
        }

        PatchLightingDataIfRequested();
    }",
@"            renderSettingsStorage = FindRenderSettingsStorage();
            if (renderSettingsStorage != null && renderSettingsStorage.renderSettingsSector != null)
            {
                curSector = (BakerySector)renderSettingsStorage.renderSettingsSector;
            }
            rtxMode = probeSavedRtxMode;
            isDistanceShadowmask = probeSavedDistanceShadowmask;
            renderDirMode = probeSavedRenderDirMode;
            lightProbeMode = probeSavedLightProbeMode;
            if (renderSettingsStorage != null)
            {
                renderSettingsStorage.renderSettingsRTXMode = rtxMode;
                renderSettingsStorage.renderSettingsDistanceShadowmask = isDistanceShadowmask;
                renderSettingsStorage.renderSettingsRenderDirMode = (int)renderDirMode;
                renderSettingsStorage.renderSettingsLightProbeMode = (int)lightProbeMode;
                EditorUtility.SetDirty(renderSettingsStorage);
            }
            ftLightmaps.RefreshFull();
        }

        PatchLightingDataIfRequested();
    }");

        f.ReplaceLines(
@"    static LightingDataAsset CreateEmptyLightingDataAsset()
    {
        var bakeryRuntimePath = ftLightmaps.GetRuntimePath();",
@"    static bool HasValidLightingDataAsset()
    {
        var asset = Lightmapping.lightingDataAsset;
        if (asset == null) return false;
        var assetPath = AssetDatabase.GetAssetPath(asset);
        if (string.IsNullOrEmpty(assetPath)) return false;
        if (!File.Exists(assetPath)) return false;
        return new FileInfo(assetPath).Length > 0;
    }

    static string ResolveProbeLightingDataAssetPath(string tempScenePath)
    {
        var asset = Lightmapping.lightingDataAsset;
        string assetPath = asset != null ? AssetDatabase.GetAssetPath(asset) : null;
        if (!string.IsNullOrEmpty(assetPath) && File.Exists(assetPath) && new FileInfo(assetPath).Length > 0) return assetPath;

        AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
        AssetDatabase.SaveAssets();

        asset = Lightmapping.lightingDataAsset;
        assetPath = asset != null ? AssetDatabase.GetAssetPath(asset) : null;
        if (!string.IsNullOrEmpty(assetPath) && File.Exists(assetPath) && new FileInfo(assetPath).Length > 0) return assetPath;

        string dir = string.IsNullOrEmpty(tempScenePath) ? null : Path.GetDirectoryName(tempScenePath);
        if (!string.IsNullOrEmpty(dir))
        {
            string specificDir = Path.Combine(dir, Path.GetFileNameWithoutExtension(tempScenePath));
            var candidates = Directory.Exists(specificDir) ? Directory.GetFiles(specificDir, ""LightingData.asset"", SearchOption.AllDirectories) : new string[0];
            for(int i=0; i<candidates.Length; i++)
            {
                if (new FileInfo(candidates[i]).Length > 0) return candidates[i].Replace('\\', '/');
            }

            if (Directory.Exists(dir))
            {
                candidates = Directory.GetFiles(dir, ""LightingData.asset"", SearchOption.AllDirectories);
                for(int i=0; i<candidates.Length; i++)
                {
                    if (new FileInfo(candidates[i]).Length > 0) return candidates[i].Replace('\\', '/');
                }
            }
        }

        return null;
    }

    static bool TryCopyLightingDataAsset(string sourcePath, string destinationPath)
    {
        if (string.IsNullOrEmpty(sourcePath) || !File.Exists(sourcePath)) return false;
        if (new FileInfo(sourcePath).Length <= 0) return false;

        string destinationDir = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrEmpty(destinationDir) && !Directory.Exists(destinationDir)) Directory.CreateDirectory(destinationDir);

        try
        {
            if (File.Exists(destinationPath) && new FileInfo(destinationPath).Length == 0) File.Delete(destinationPath);
            File.Copy(sourcePath, destinationPath, true);
            return File.Exists(destinationPath) && new FileInfo(destinationPath).Length > 0;
        }
        catch
        {
            return false;
        }
    }

    static void TryDeleteEmptyFile(string path)
    {
        try
        {
            if (File.Exists(path) && new FileInfo(path).Length == 0) File.Delete(path);
        }
        catch { }
    }

    static LightingDataAsset CreateEmptyLightingDataAsset()
    {
        var bakeryRuntimePath = ftLightmaps.GetRuntimePath();");

        f.ReplaceLines(
@"        if (File.Exists(outName)) ValidateFileAttribs(outName);
        if (AssetDatabase.CopyAsset(bakeryRuntimePath + ""emptyLightingData.asset"",  outName))",
@"        if (File.Exists(outName)) ValidateFileAttribs(outName);
        if (File.Exists(outName) && new FileInfo(outName).Length == 0) File.Delete(outName);
        string destinationDir = Path.GetDirectoryName(outName);
        if (!string.IsNullOrEmpty(destinationDir) && !Directory.Exists(destinationDir)) Directory.CreateDirectory(destinationDir);
        if (AssetDatabase.CopyAsset(bakeryRuntimePath + ""emptyLightingData.asset"",  outName))");

        f.ReplaceLines(
@"        // Init probes
        bool renderProbesNow = ((lightProbeMode == LightProbeMode.L1 | lightProbeMode == LightProbeMode.L2) && !selectedOnly) && !apv;",
@"        // Init probes
        bool needsLightingDataAsset = (userRenderMode == RenderMode.Shadowmask || userRenderMode == RenderMode.Subtractive) && !HasValidLightingDataAsset();
        bool renderProbesNow = (((lightProbeMode == LightProbeMode.L1 | lightProbeMode == LightProbeMode.L2) && !selectedOnly) || needsLightingDataAsset) && !apv;");

        f.ReplaceLines(
@"            var probes = LightmapSettings.lightProbes;
            if (probes == null)
            {
                DebugLogError(""No probes in LightingDataAsset"");
                yield break;
            }
            var positions = probes.positions;",
@"            var probes = LightmapSettings.lightProbes;
            if (probes == null || probes.count == 0)
            {
                hasAnyProbes = false;
                DebugLogWarning(""No probes in LightingDataAsset; skipping probe color application"");
                yield break;
            }
            var positions = probes.positions;");

        f.ReplaceLines(
@"            AssetDatabase.Refresh();
            ApplyLightingDataAsset(newPath);
            EditorSceneManager.MarkAllScenesDirty();
            EditorSceneManager.SaveOpenScenes();
        }
#endif

        // Store lightmap flags",
@"            AssetDatabase.Refresh();
            ApplyLightingDataAsset(newPath);
            EditorSceneManager.MarkAllScenesDirty();
            EditorSceneManager.SaveOpenScenes();
        }
#endif

#if UNITY_2017_3_OR_NEWER
        if ((userRenderMode == RenderMode.Shadowmask || userRenderMode == RenderMode.Subtractive) && !HasValidLightingDataAsset())
        {
            var fallbackAsset = CreateEmptyLightingDataAsset();
            if (fallbackAsset != null)
            {
                AssetDatabase.Refresh();
                ApplyLightingDataAsset(AssetDatabase.GetAssetPath(fallbackAsset));
                EditorSceneManager.MarkAllScenesDirty();
                EditorSceneManager.SaveOpenScenes();
            }
            else
            {
                DebugLogWarning(""No LightingDataAsset could be generated for render mode "" + userRenderMode);
            }
        }
#endif

        // Store lightmap flags");
        }
    }

    static void AddHostLifecycleRules(BakeryLinuxPatchFile f)
    {
        string nl = f.NewLine;

        if (f.Text.IndexOf("BakeryWineHost.EnsureStarted", StringComparison.Ordinal) < 0)
        {
            string[] signatures =
            {
                "IEnumerator RenderReflProbesFunc()",
                "IEnumerator RenderLightProbesFunc()",
                "public IEnumerator RenderLightmapFunc()"
            };

            foreach (string signature in signatures)
            {
                int method = f.Text.IndexOf(signature, StringComparison.Ordinal);
                if (method < 0)
                {
                    f.Errors.Add("Missing host insertion anchor: " + signature);
                    continue;
                }

                int brace = f.Text.IndexOf('{', method);
                if (brace < 0)
                {
                    f.Errors.Add("Missing coroutine body in: " + signature);
                    continue;
                }

                int at = brace + 1;
                f.Text = f.Text.Insert(at, nl + "        BakeryWineHost.EnsureStarted();");
            }
        }

    }

    static bool RemoveRenderLightmapXatlasFallbacks(BakeryLinuxPatchFile f)
    {
        return f.ReplaceRegex(
            @"(?m)^[ \t]*#if UNITY_EDITOR_LINUX\r?\n[ \t]*if \(ftBuildGraphics\.atlasPacker == ftGlobalStorage\.AtlasPacker\.xatlas\) ftBuildGraphics\.atlasPacker = ftGlobalStorage\.AtlasPacker\.Default;\r?\n[ \t]*#endif\r?\n",
            m => string.Empty,
            false);
    }
}
