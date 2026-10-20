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
            case "ftBuildGraphics.cs": return text.IndexOf("BakeryProgress.Show", StringComparison.Ordinal) >= 0;
            case "ftRenderLightmap.cs": return text.IndexOf("BakeryProcess.RunLocalProcess", StringComparison.Ordinal) >= 0;
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
        f.ReplaceRegex(@"(?m)^([ \t]*)simpleProgressBarEnd\(\);", m => m.Groups[1].Value + "BakeryProgress.End();");
        f.Replace("= simpleProgressBarCancelled();", "= BakeryProgress.Cancelled;");
    }

    static void PatchUpdater(BakeryLinuxPatchFile f)
    {
        f.Replace("err = ExtractZIP(", "err = BakeryProcess.ExtractZIP(");
    }

    static void PatchBuildGraphics(BakeryLinuxPatchFile f)
    {
        f.Replace("ftRenderLightmap.simpleProgressBarShow(", "BakeryProgress.Show(");
        f.Replace("ftRenderLightmap.simpleProgressBarCancelled()", "BakeryProgress.Cancelled");
        f.Replace("ftRenderLightmap.simpleProgressBarEnd()", "BakeryProgress.End()");
        f.ReplaceRegex(
            @"(?m)^([ \t]*)var tempDir = System\.Environment\.GetEnvironmentVariable\(""TEMP"", System\.EnvironmentVariableTarget\.Process\);\r?\n[ \t]*scenePath = tempDir \+ ""\\\\frender"";",
            m => m.Groups[1].Value + "scenePath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), \"frender\");");

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
        f.Replace("StopProcess(exeProcess);", "BakeryProcess.StopProcess(exeProcess);");
        f.Replace("lastReturnValue = GetProcessReturnValueAndClose(", "lastReturnValue = BakeryProcess.GetProcessReturnValueAndClose(");
        f.Replace("exeProcess = RunLocalProcessVisible(", "exeProcess = BakeryProcess.RunLocalProcessVisible(");
        f.Replace("exeProcess = RunLocalProcess(", "exeProcess = BakeryProcess.RunLocalProcess(");
        f.Replace("return StopProcess(process);", "return BakeryProcess.StopProcess(process);");

        f.ReplaceRegex(
            @"(?m)^([ \t]*)if \(showProgressBar\) simpleProgressBarShow\(""Bakery"", progressBarText, progressBarPercent, progressBarStep, false\);",
            m => m.Groups[1].Value + "BakeryProgress.Reset();" + f.NewLine +
                 m.Groups[1].Value + "if (showProgressBar) BakeryProgress.Show(\"Bakery\", progressBarText, progressBarPercent, progressBarStep, false);");

        f.ReplaceRegex(
            @"(?m)^([ \t]*)if \(showProgressBar\) simpleProgressBarShow\(""Bakery"", progressBarText, progressBarPercent, progressBarStep, onTop\);\r?\n[ \t]*userCanceled = simpleProgressBarCancelled\(\);",
            m => m.Groups[1].Value + "if (showProgressBar)" + f.NewLine +
                 m.Groups[1].Value + "{" + f.NewLine +
                 m.Groups[1].Value + "    BakeryProgress.Show(\"Bakery\", progressBarText, progressBarPercent, progressBarStep, onTop);" + f.NewLine +
                 m.Groups[1].Value + "    userCanceled = BakeryProgress.Cancelled;" + f.NewLine +
                 m.Groups[1].Value + "}");

        f.Replace("userCanceled = simpleProgressBarCancelled();", "userCanceled = BakeryProgress.Cancelled;");
        f.Replace("if (showProgressBar) simpleProgressBarEnd();", "BakeryProgress.End();");
        f.Replace("System.Environment.GetEnvironmentVariable(\"TEMP\", System.EnvironmentVariableTarget.Process) + \"\\\\frender\"",
                  "System.IO.Path.Combine(System.IO.Path.GetTempPath(), \"frender\")");
        f.ReplaceRegex(@"(scenePath\s*\+\s*)""\\\\", m => m.Groups[1].Value + "\"/");

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
