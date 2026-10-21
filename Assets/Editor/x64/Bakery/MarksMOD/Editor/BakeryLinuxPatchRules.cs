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
                text.IndexOf("skipping the probe lightmap group", StringComparison.Ordinal) >= 0;
            case "ftRenderLightmap.cs": return text.IndexOf("BakeryProcess.RunLocalProcess", StringComparison.Ordinal) >= 0 &&
                text.IndexOf("ResolveProbeLightingDataAssetPath", StringComparison.Ordinal) >= 0;
            case "ftModelPostProcessor.cs": return text.IndexOf("BAKERY_TEST_DISABLE_IMPORT_UNWRAP", StringComparison.Ordinal) >= 0;
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
            case "ftModelPostProcessor.cs": PatchModelPostProcessor(file); break;
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

        if (f.Text.IndexOf("public static ftGlobalStorage.AtlasPacker atlasPacker = ftGlobalStorage.AtlasPacker.Default;", StringComparison.Ordinal) < 0)
        {
            string oldDeclaration = "    public static ftGlobalStorage.AtlasPacker atlasPacker = ftGlobalStorage.AtlasPacker.xatlas;";
            string newDeclaration = "#if UNITY_EDITOR_LINUX" + f.NewLine +
                                    "    public static ftGlobalStorage.AtlasPacker atlasPacker = ftGlobalStorage.AtlasPacker.Default;" + f.NewLine +
                                    "#else" + f.NewLine +
                                    "    public static ftGlobalStorage.AtlasPacker atlasPacker = ftGlobalStorage.AtlasPacker.xatlas;" + f.NewLine +
                                    "#endif";
            f.Replace(oldDeclaration, newDeclaration);
        }

        PatchPackerAfter(f,
            "                    var packer = group.atlasPacker == BakeryLightmapGroup.AtlasPacker.Auto ? atlasPacker : (ftGlobalStorage.AtlasPacker)group.atlasPacker;",
            "                    ");
        PatchPackerAfter(f,
            "                var packer = lmgroup.atlasPacker == BakeryLightmapGroup.AtlasPacker.Auto ? atlasPacker : (ftGlobalStorage.AtlasPacker)lmgroup.atlasPacker;",
            "                ");

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

    static void PatchModelPostProcessor(BakeryLinuxPatchFile f)
    {
        f.InsertAfter("    static Dictionary<string, ftSavedPadding2> assetSavedPaddingAdjustment = new Dictionary<string, ftSavedPadding2>();",
            "    static bool TestDisableAutoUnwrap" + f.NewLine +
            "    {" + f.NewLine +
            "        get { return !string.IsNullOrEmpty(System.Environment.GetEnvironmentVariable(\"BAKERY_TEST_DISABLE_IMPORT_UNWRAP\")); }" + f.NewLine +
            "    }");

        f.InsertAfter("        Init();",
            "        if (TestDisableAutoUnwrap) return;");

        f.InsertAfter("    void OnPostprocessModel(GameObject g)" + f.NewLine + "    {",
            "        if (TestDisableAutoUnwrap) return;");
    }

    static void PatchClearCache(BakeryLinuxPatchFile f)
    {
        if (f.Text.IndexOf("Path.GetTempPath()", StringComparison.Ordinal) >= 0) return;

        f.Replace("System.Environment.GetEnvironmentVariable(\"TEMP\", System.EnvironmentVariableTarget.Process) + \"\\\\frender\"",
                  "Path.Combine(Path.GetTempPath(), \"frender\")");
    }

    static void PatchPackerAfter(BakeryLinuxPatchFile f, string anchor, string indent)
    {
        string block = "#if UNITY_EDITOR_LINUX" + f.NewLine +
                       indent + "if (packer == ftGlobalStorage.AtlasPacker.xatlas) packer = ftGlobalStorage.AtlasPacker.Default;" + f.NewLine +
                       "#endif";
        f.InsertAfter(anchor, block);
    }

    static void PatchRenderLightmap(BakeryLinuxPatchFile f)
    {
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
            if (renderSettingsStorage != null)
            {
                renderSettingsStorage.renderSettingsRTXMode = rtxMode;
                renderSettingsStorage.renderSettingsDistanceShadowmask = isDistanceShadowmask;
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

        AddRenderPacker(f,
            "            ftBuildGraphics.atlasPacker = (ftGlobalStorage.AtlasPacker)EditorGUILayout.EnumPopup(ftBuildGraphics.atlasPacker, opts);",
            "        ");
        AddRenderPacker(f,
            "        ftBuildGraphics.atlasPacker = storage.renderSettingsAtlasPacker;",
            "        ");
        AddRenderPacker(f,
            "        ftBuildGraphics.atlasPacker = renderSettingsStorage.renderSettingsAtlasPacker;",
            "        ");
    }

    static void AddRenderPacker(BakeryLinuxPatchFile f, string anchor, string indent)
    {
        string block = "#if UNITY_EDITOR_LINUX" + f.NewLine +
                       indent + "if (ftBuildGraphics.atlasPacker == ftGlobalStorage.AtlasPacker.xatlas) ftBuildGraphics.atlasPacker = ftGlobalStorage.AtlasPacker.Default;" + f.NewLine +
                       "#endif";
        f.InsertAfter(anchor, block);
    }
}
