#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

public static class BakeryLinuxRequirements
{
    public const string SupportedBakeryVersion = "1.985";

    static string cachedLdconfig;
    static bool ldconfigChecked;

    public static List<BakeryCheckResult> Run()
    {
        ldconfigChecked = false;
        cachedLdconfig = null;

        var list = new List<BakeryCheckResult>();

        bool linux = Application.platform == RuntimePlatform.LinuxEditor;
        Add(list, "Linux editor", linux ? BakeryCheckStatus.Ok : BakeryCheckStatus.Fail,
            linux ? Application.platform.ToString() : "the Wine bridge is Linux-only");
        if (!linux) return list;

        try
        {
            CheckHostTools(list);
            CheckRuntimeLibraries(list);
            CheckGraphicsStack(list);
            CheckPaths(list);
            CheckWinePrefix(list);
            CheckBakery(list);
            CheckWriteAccess(list);
        }
        catch (Exception e)
        {
            Add(list, "Requirements check", BakeryCheckStatus.Fail,
                e.GetType().Name + ": " + e.Message);
        }

        return list;
    }

    static void CheckHostTools(List<BakeryCheckResult> list)
    {
        string bash = File.Exists("/bin/bash") ? "/bin/bash" : null;
        CheckTool(list, "/bin/bash", bash, BakeryCheckStatus.Fail,
            "/bin/bash is hard-coded for bootstrap.sh and probe.sh");
        CheckCoreTools(list);

        string runner = Environment.GetEnvironmentVariable("BAKERY_RUNNER");
        if (string.IsNullOrEmpty(runner)) runner = Environment.GetEnvironmentVariable("BAKERY_WINE");

        string winePath = null;
        if (!string.IsNullOrEmpty(runner))
        {
            string configured = runner.Trim();
            winePath = Path.IsPathRooted(configured)
                ? (File.Exists(configured) ? configured : null)
                : BakeryLinuxDiagnostics.FindInPath(configured);
        }
        if (winePath == null) winePath = BakeryLinuxDiagnostics.FindInPath("wine64");
        if (winePath == null) winePath = BakeryLinuxDiagnostics.FindInPath("wine");

        if (winePath == null)
        {
            Add(list, "Wine", BakeryCheckStatus.Fail,
                "wine64/wine not found in PATH; install Wine or set BAKERY_RUNNER");
        }
        else
        {
            string versionOutput = BakeryLinuxDiagnostics.Capture(winePath, "--version", 3000);
            bool versionOk = !string.IsNullOrEmpty(versionOutput);
            string version = "version check failed";
            if (versionOk)
            {
                string[] versionLines = versionOutput.Trim().Split('\n');
                version = versionLines[0].Trim();
            }

            string detail = winePath + " (" + version + ")";
            var wineMatch = Regex.Match(version, @"wine-([0-9]+)");
            int wineMajor;
            if (wineMatch.Success && int.TryParse(wineMatch.Groups[1].Value, out wineMajor) && wineMajor < 10)
                detail += "; Wine 10+ is recommended";

            Add(list, "Wine", versionOk ? BakeryCheckStatus.Ok : BakeryCheckStatus.Warn, detail);
        }

        string winetricks = BakeryLinuxDiagnostics.FindInPath("winetricks");
        BakeryCheckStatus winetricksStatus = winetricks != null
            ? BakeryCheckStatus.Ok
            : (BakeryLinuxSetup.PrefixLooksReady ? BakeryCheckStatus.Warn : BakeryCheckStatus.Fail);
        string winetricksDetail = winetricks;
        if (winetricks != null)
        {
            string winetricksVersion = BakeryLinuxDiagnostics.Capture(winetricks, "--version", 3000, true);
            if (!string.IsNullOrEmpty(winetricksVersion))
            {
                string[] versionLines = winetricksVersion.Trim().Split('\n');
                string versionLine = versionLines[0].Trim();
                int space = versionLine.IndexOf(' ');
                if (space > 0) versionLine = versionLine.Substring(0, space);
                winetricksDetail += " (" + versionLine + ")";
            }
        }
        else
        {
            winetricksDetail = "not on PATH. Lutris' bundled winetricks is not visible to Unity; install the distro package or add it to PATH";
        }

        Add(list, "winetricks", winetricksStatus, winetricksDetail);

        CheckTool(list, "python3", BakeryLinuxDiagnostics.FindInPath("python3"),
            BakeryCheckStatus.Fail, "probe.sh and the host ping use it");
        CheckTool(list, "flock", BakeryLinuxDiagnostics.FindInPath("flock"),
            BakeryCheckStatus.Warn, "serializes Wine host startup");
        CheckTool(list, "file", BakeryLinuxDiagnostics.FindInPath("file"),
            BakeryCheckStatus.Warn, "probe.sh checks the host executable type");
        CheckTool(list, "ldd", BakeryLinuxDiagnostics.FindInPath("ldd"),
            BakeryCheckStatus.Warn, "diagnostics check the liblz4 linkage");
        CheckTool(list, "tar", BakeryLinuxDiagnostics.FindInPath("tar"),
            BakeryCheckStatus.Warn, "setup extracts nvidia-libs");

        string downloader = BakeryLinuxDiagnostics.FindInPath("curl");
        if (downloader == null) downloader = BakeryLinuxDiagnostics.FindInPath("wget");
        CheckTool(list, "curl or wget", downloader, BakeryCheckStatus.Warn,
            "setup downloads nvidia-libs");

        BakeryCheckStatus setupToolStatus = BakeryLinuxSetup.PrefixLooksReady
            ? BakeryCheckStatus.Warn
            : BakeryCheckStatus.Fail;
        CheckTool(list, "cabextract", BakeryLinuxDiagnostics.FindInPath("cabextract"),
            setupToolStatus, "winetricks needs it for VC++ archives");
        CheckTool(list, "unzip", BakeryLinuxDiagnostics.FindInPath("unzip"),
            setupToolStatus, "winetricks needs it for DXVK archives");

        string lz4 = FindSharedLibrary("liblz4.so.1");
        Add(list, "liblz4.so.1", lz4 != null ? BakeryCheckStatus.Ok : BakeryCheckStatus.Fail,
            lz4 ?? "missing runtime dependency of libbakery_glue.so");
    }

    static void CheckCoreTools(List<BakeryCheckResult> list)
    {
        string[] names =
        {
            "seq", "sleep", "kill", "nohup", "timeout", "grep", "head", "tail", "sed", "tr",
            "dirname", "basename", "readlink", "ln", "touch", "mkdir", "rm", "mktemp",
            "awk", "sort", "uniq", "wc", "cut", "find", "xargs", "sha256sum"
        };

        var missing = new List<string>();
        foreach (var name in names)
        {
            if (BakeryLinuxDiagnostics.FindInPath(name) == null) missing.Add(name);
        }

        Add(list, "core shell tools", missing.Count == 0 ? BakeryCheckStatus.Ok : BakeryCheckStatus.Fail,
            missing.Count == 0 ? "found" : "missing: " + string.Join(", ", missing.ToArray()));
    }

    static void CheckGraphicsStack(List<BakeryCheckResult> list)
    {
        string gpu = BakeryLinuxDiagnostics.Capture(
            "nvidia-smi", "--query-gpu=name,driver_version --format=csv,noheader", 3000);
        Add(list, "NVIDIA driver",
            string.IsNullOrEmpty(gpu) ? BakeryCheckStatus.Fail : BakeryCheckStatus.Ok,
            string.IsNullOrEmpty(gpu)
                ? "nvidia-smi failed; the proprietary NVIDIA driver with CUDA is required"
                : gpu.Trim());

        string vulkan = FindVulkanLoader();
        Add(list, "Vulkan loader", vulkan != null ? BakeryCheckStatus.Ok : BakeryCheckStatus.Fail,
            vulkan ?? "libvulkan.so.1 not found; DXVK needs the Vulkan loader");

        string icd = FindNvidiaIcd();
        Add(list, "NVIDIA Vulkan ICD", icd != null ? BakeryCheckStatus.Ok : BakeryCheckStatus.Warn,
            icd ?? "not found in the usual locations; set VK_ICD_FILENAMES if DXVK cannot see the GPU");
    }

    static void CheckPaths(List<BakeryCheckResult> list)
    {
        string project = Path.GetDirectoryName(Application.dataPath);
        bool projectAscii = IsAscii(project);
        Add(list, "Project path ASCII", projectAscii ? BakeryCheckStatus.Ok : BakeryCheckStatus.Warn,
            project + (projectAscii ? "" : " - non-ASCII paths are not covered by the test matrix"));

        string temp = Path.GetTempPath();
        bool tempAscii = IsAscii(temp);
        bool tempExists = Directory.Exists(temp);
        bool tempWritable = tempExists && CanWriteDirectory(temp);
        Add(list, "TEMP path", tempExists && tempWritable ? BakeryCheckStatus.Ok : BakeryCheckStatus.Fail,
            temp + (!tempExists
                ? " - does not exist"
                : tempWritable ? "" : " - is not writable"));

        Add(list, "TEMP path ASCII", tempAscii && tempExists ? BakeryCheckStatus.Ok : BakeryCheckStatus.Warn,
            temp + (tempAscii ? "" : " - must be ASCII (vendor uvGBufferGen limitation)"));

        bool tmpWritable = CanWriteDirectory("/tmp");
        Add(list, "/tmp writable", tmpWritable ? BakeryCheckStatus.Ok : BakeryCheckStatus.Fail,
            "/tmp is used for the Wine host pid/log/lock files");
    }

    static void CheckWinePrefix(List<BakeryCheckResult> list)
    {
        string root = BakeryLinuxSetup.WineRootPath;
        string rootParent = root;
        while (!string.IsNullOrEmpty(rootParent) && !Directory.Exists(rootParent))
            rootParent = Path.GetDirectoryName(rootParent);
        bool rootWritable = !string.IsNullOrEmpty(rootParent) && CanWriteDirectory(rootParent);
        Add(list, "Wine root", rootWritable ? BakeryCheckStatus.Ok : BakeryCheckStatus.Fail,
            rootWritable
                ? root
                : "cannot create " + root + "; no writable parent directory");

        string prefix = BakeryLinuxSetup.PrefixPath;
        if (BakeryLinuxSetup.PrefixLooksReady)
        {
            Add(list, "Wine prefix", BakeryCheckStatus.Ok, prefix);
            return;
        }

        if (!BakeryLinuxSetup.SetupComplete)
        {
            string parent = prefix;
            while (!string.IsNullOrEmpty(parent) && !Directory.Exists(parent))
                parent = Path.GetDirectoryName(parent);

            bool writable = !string.IsNullOrEmpty(parent) && CanWriteDirectory(parent);
            Add(list, "Wine prefix", writable ? BakeryCheckStatus.Ok : BakeryCheckStatus.Fail,
                writable
                    ? prefix + " (not created yet; PATCH! will run setup)"
                    : "cannot create " + prefix + "; no writable parent directory");
            return;
        }

        bool prefixWritable = CanWriteDirectory(prefix);
        Add(list, "Wine prefix", prefixWritable ? BakeryCheckStatus.Warn : BakeryCheckStatus.Fail,
            prefixWritable
                ? prefix + " (incomplete; PATCH! will repair it)"
                : prefix + " exists but is not writable");
    }

    static void CheckRuntimeLibraries(List<BakeryCheckResult> list)
    {
        string libs = BakeryToolPaths.LibrariesDirectory;
        Version required = null;
        string requiredFrom = null;

        string[] candidates = { "libbakery_glue.so", "libfrender.so", "libxatlasLib.so", "libxatlasLib_wine.so" };
        foreach (var name in candidates)
        {
            string source;
            Version version = GetRequiredGlibcVersion(Path.Combine(libs, name), out source);
            if (version == null) continue;
            if (required == null || version > required)
            {
                required = version;
                requiredFrom = source;
            }
        }

        Version system = GetSystemGlibcVersion();
        if (system == null)
        {
            Add(list, "glibc", BakeryCheckStatus.Fail,
                "could not determine the system glibc version; the Linux plugin shims require glibc");
        }
        else if (required == null)
        {
            Add(list, "glibc", BakeryCheckStatus.Warn,
                "system " + system + "; could not read the requirement from the plugin shims");
        }
        else if (system < required)
        {
            Add(list, "glibc", BakeryCheckStatus.Fail,
                "system " + system + "; " + requiredFrom + " requires " + required +
                " - rebuild the shims on an older glibc baseline");
        }
        else
        {
            Add(list, "glibc", BakeryCheckStatus.Ok,
                "system " + system + "; shims require " + required);
        }

        CheckDynamicLoader(list, libs);
    }

    static void CheckDynamicLoader(List<BakeryCheckResult> list, string libs)
    {
        string ldd = BakeryLinuxDiagnostics.FindInPath("ldd");
        if (ldd == null)
        {
            Add(list, "Dynamic linker", BakeryCheckStatus.Warn,
                "ldd not found; cannot verify the plugin shims' dependencies");
            return;
        }

        string[] names = { "libbakery_glue.so", "libfrender.so", "libxatlasLib.so", "libxatlasLib_wine.so" };
        var problems = new List<string>();
        int checkedCount = 0;

        foreach (var name in names)
        {
            string path = Path.Combine(libs, name);
            if (!File.Exists(path)) continue;
            checkedCount++;

            string output = BakeryLinuxDiagnostics.Capture(ldd, "\"" + path + "\"", 10000, true);
            if (string.IsNullOrEmpty(output)) continue;

            string[] lines = output.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                if (lines[i].IndexOf("not found", StringComparison.OrdinalIgnoreCase) < 0) continue;
                problems.Add(name + ": " + lines[i].Trim());
                break;
            }
        }

        if (checkedCount == 0)
        {
            Add(list, "Dynamic linker", BakeryCheckStatus.Warn,
                "plugin shims not found; cannot verify their dependencies");
        }
        else
        {
            Add(list, "Dynamic linker", problems.Count == 0 ? BakeryCheckStatus.Ok : BakeryCheckStatus.Fail,
                problems.Count == 0
                    ? "all plugin shim dependencies resolve"
                    : string.Join("; ", problems.ToArray()));
        }
    }

    static Version GetSystemGlibcVersion()
    {
        string output = BakeryLinuxDiagnostics.Capture("ldd", "--version", 3000, true);
        if (string.IsNullOrEmpty(output))
            output = BakeryLinuxDiagnostics.Capture("getconf", "GNU_LIBC_VERSION", 3000, true);
        if (string.IsNullOrEmpty(output)) return null;

        var match = Regex.Match(output, @"(?:GNU libc\)?|glibc)\s+([0-9]+\.[0-9]+(?:\.[0-9]+)?)",
            RegexOptions.IgnoreCase);
        return match.Success ? ParseVersion(match.Groups[1].Value) : null;
    }

    static Version GetRequiredGlibcVersion(string libraryPath, out string source)
    {
        source = null;
        try
        {
            if (!File.Exists(libraryPath)) return null;

            byte[] bytes = File.ReadAllBytes(libraryPath);
            string text = Encoding.ASCII.GetString(bytes);
            var matches = Regex.Matches(text, @"GLIBC_([0-9]+(?:\.[0-9]+)+)");

            Version max = null;
            foreach (Match match in matches)
            {
                Version version = ParseVersion(match.Groups[1].Value);
                if (version == null) continue;
                if (max == null || version > max) max = version;
            }

            if (max != null) source = Path.GetFileName(libraryPath);
            return max;
        }
        catch
        {
            return null;
        }
    }

    static Version ParseVersion(string value)
    {
        Version version;
        return Version.TryParse(value, out version) ? version : null;
    }

    static void CheckWriteAccess(List<BakeryCheckResult> list)
    {
        string scripts = BakeryLinuxPatcher.ScriptsDirectory;
        if (!Directory.Exists(scripts))
        {
            Add(list, "Bakery writable", BakeryCheckStatus.Fail, scripts + " (not found)");
        }
        else
        {
            var readOnly = new List<string>();
            var targets = BakeryLinuxPatcher.PatchTargets;
            for (int i = 0; i < targets.Count; i++)
            {
                string path = Path.Combine(scripts, targets[i]);
                if (!File.Exists(path)) continue;
                if (!CanWriteFile(path)) readOnly.Add(targets[i]);
            }

            Add(list, "Bakery writable", readOnly.Count == 0 ? BakeryCheckStatus.Ok : BakeryCheckStatus.Fail,
                readOnly.Count == 0
                    ? scripts
                    : "not writable: " + string.Join(", ", readOnly.ToArray()));
        }

        string native = BakeryToolPaths.NativeDirectory;
        if (!Directory.Exists(native))
        {
            Add(list, "Patch backups", BakeryCheckStatus.Fail, native + " (not found)");
        }
        else
        {
            string backup = BakeryLinuxPatcher.BackupDirectory;
            string target = Directory.Exists(backup) ? backup : native;
            bool backupWritable = CanWriteDirectory(target);
            Add(list, "Patch backups", backupWritable ? BakeryCheckStatus.Ok : BakeryCheckStatus.Fail,
                backupWritable
                    ? backup
                    : target + " is not writable; patch backups cannot be stored");
        }
    }

    static bool CanWriteFile(string path)
    {
        try
        {
            using (File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite)) { }
            return true;
        }
        catch
        {
            return false;
        }
    }

    static bool CanReadFile(string path)
    {
        try
        {
            using (File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)) { }
            return true;
        }
        catch
        {
            return false;
        }
    }

    static void AddPayloadProblem(List<string> problems, string path, string label)
    {
        if (!File.Exists(path)) problems.Add(label);
        else if (!CanReadFile(path)) problems.Add(label + " (unreadable)");
    }

    static bool CanWriteDirectory(string directory)
    {
        try
        {
            if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory)) return false;

            string probe = Path.Combine(directory, ".bakery-write-test-" + Guid.NewGuid().ToString("N"));
            File.WriteAllText(probe, "test");
            File.Delete(probe);
            return true;
        }
        catch
        {
            return false;
        }
    }

    static void CheckBakery(List<BakeryCheckResult> list)
    {
        string scripts = BakeryLinuxPatcher.ScriptsDirectory;
        bool scriptsExist = Directory.Exists(scripts);
        Add(list, "Bakery scripts", scriptsExist ? BakeryCheckStatus.Ok : BakeryCheckStatus.Fail,
            scriptsExist ? scripts : scripts + " (not found)");

        if (scriptsExist)
            CheckBakeryVersion(list, scripts);
        else
            Add(list, "Bakery version", BakeryCheckStatus.Fail, "Bakery scripts were not found");

        if (scriptsExist)
        {
            var missing = new List<string>();
            var targets = BakeryLinuxPatcher.PatchTargets;
            for (int i = 0; i < targets.Count; i++)
            {
                if (!File.Exists(Path.Combine(scripts, targets[i]))) missing.Add(targets[i]);
            }

            Add(list, "Bakery patch targets",
                missing.Count == 0 ? BakeryCheckStatus.Ok : BakeryCheckStatus.Fail,
                missing.Count == 0
                    ? targets.Count + " files present"
                    : "missing: " + string.Join(", ", missing.ToArray()));
        }
        else
        {
            Add(list, "Bakery patch targets", BakeryCheckStatus.Fail, "Bakery scripts were not found");
        }

        CheckAsmdef(list, scripts, scriptsExist);

        string bootstrap = Path.Combine(BakeryLinuxSetup.ScriptDirectory, "bootstrap.sh");
        string probe = Path.Combine(BakeryLinuxSetup.ScriptDirectory, "probe.sh");
        string ping = Path.Combine(BakeryLinuxSetup.ScriptDirectory, "host_ping.py");
        string host = Path.Combine(BakeryToolPaths.HostDirectory, "bakery_wine_host.exe");
        var missingMod = new List<string>();
        if (!File.Exists(bootstrap)) missingMod.Add("bootstrap.sh");
        else if (!CanReadFile(bootstrap)) missingMod.Add("bootstrap.sh (unreadable)");
        if (!File.Exists(probe)) missingMod.Add("probe.sh");
        else if (!CanReadFile(probe)) missingMod.Add("probe.sh (unreadable)");
        if (!File.Exists(ping)) missingMod.Add("host_ping.py");
        else if (!CanReadFile(ping)) missingMod.Add("host_ping.py (unreadable)");
        if (!File.Exists(host)) missingMod.Add("bakery_wine_host.exe");
        else if (!CanReadFile(host)) missingMod.Add("bakery_wine_host.exe (unreadable)");
        Add(list, "MarksMOD runtime files",
            missingMod.Count == 0 ? BakeryCheckStatus.Ok : BakeryCheckStatus.Fail,
            missingMod.Count == 0
                ? BakeryToolPaths.NativeDirectory
                : "missing: " + string.Join(", ", missingMod.ToArray()) + " in " + BakeryToolPaths.NativeDirectory);

        string libs = BakeryToolPaths.LibrariesDirectory;
        var missingLibs = new List<string>();
        foreach (var name in new[] { "libbakery_glue.so", "libfrender.so", "libxatlasLib_wine.so" })
        {
            string path = Path.Combine(libs, name);
            if (!File.Exists(path)) missingLibs.Add(name);
            else if (!CanReadFile(path)) missingLibs.Add(name + " (unreadable)");
        }
        Add(list, "Linux plugin shims",
            missingLibs.Count == 0 ? BakeryCheckStatus.Ok : BakeryCheckStatus.Fail,
            missingLibs.Count == 0
                ? libs
                : "missing: " + string.Join(", ", missingLibs.ToArray()) + " in " + libs);

        string payload = BakeryToolPaths.PayloadDirectory;
        var missingPayload = new List<string>();
        AddPayloadProblem(missingPayload, Path.Combine(payload, "frender.dll"), "frender.dll");
        AddPayloadProblem(missingPayload, Path.Combine(payload, "ftraceRTX9.exe"), "ftraceRTX9.exe");
        AddPayloadProblem(missingPayload, BakeryXatlasBackendService.BakeryXatlasDllPath, "scripts/xatlas/xatlasLib.dll");
        Add(list, "Bakery native payload",
            missingPayload.Count == 0 ? BakeryCheckStatus.Ok : BakeryCheckStatus.Fail,
            missingPayload.Count == 0
                ? payload
                : "missing: " + string.Join(", ", missingPayload.ToArray()) + " in " + payload);
    }

    static void CheckBakeryVersion(List<BakeryCheckResult> list, string scripts)
    {
        string version = ReadBakeryVersion(Path.Combine(scripts, "ftRenderLightmap.cs"));
        if (string.IsNullOrEmpty(version))
        {
            Add(list, "Bakery version", BakeryCheckStatus.Warn,
                "could not read the version from ftRenderLightmap.cs; MarksMOD is written for v" + SupportedBakeryVersion);
        }
        else if (!IsSupportedBakeryVersion(version))
        {
            Add(list, "Bakery version", BakeryCheckStatus.Fail,
                "found v" + version + "; MarksMOD patches the Bakery v" + SupportedBakeryVersion + " source");
        }
        else
        {
            Add(list, "Bakery version", BakeryCheckStatus.Ok, "v" + version);
        }
    }

    static void CheckAsmdef(List<BakeryCheckResult> list, string scripts, bool scriptsExist)
    {
        if (!scriptsExist)
        {
            Add(list, "Bakery editor asmdef", BakeryCheckStatus.Warn, "Bakery scripts were not found");
            return;
        }

        string text = null;
        try
        {
            string path = Path.Combine(scripts, "BakeryEditorAssembly.asmdef");
            if (File.Exists(path)) text = File.ReadAllText(path);
        }
        catch { }

        if (string.IsNullOrEmpty(text))
        {
            Add(list, "Bakery editor asmdef", BakeryCheckStatus.Warn,
                "BakeryEditorAssembly.asmdef is missing; Bakery may still be importing");
            return;
        }

        bool hasApv = text.IndexOf("\"BakeryAPV\"", StringComparison.Ordinal) >= 0;
        bool hasTemplate = File.Exists(Path.Combine(scripts, "BakeryEditorAssembly_asmdef.txt"));
        if (!hasApv)
        {
            Add(list, "Bakery editor asmdef", BakeryCheckStatus.Warn,
                "the BakeryAPV reference is missing; restart Unity so Bakery can upgrade its asmdef before patching");
        }
        else if (hasTemplate)
        {
            Add(list, "Bakery editor asmdef", BakeryCheckStatus.Warn,
                "BakeryEditorAssembly_asmdef.txt is still present; restart Unity to finish Bakery's first import");
        }
        else
        {
            Add(list, "Bakery editor asmdef", BakeryCheckStatus.Ok, "modern Bakery assembly definition");
        }
    }

    static bool IsSupportedBakeryVersion(string version)
    {
        return version == SupportedBakeryVersion ||
            version.StartsWith(SupportedBakeryVersion + ".", StringComparison.Ordinal);
    }

    static string ReadBakeryVersion(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            string text = File.ReadAllText(path);
            var match = Regex.Match(text, @"Version\s+([0-9]+(?:\.[0-9]+)+)");
            return match.Success ? match.Groups[1].Value : null;
        }
        catch
        {
            return null;
        }
    }

    static string FindVulkanLoader()
    {
        string[] candidates =
        {
            "/usr/lib/x86_64-linux-gnu/libvulkan.so.1",
            "/lib/x86_64-linux-gnu/libvulkan.so.1",
            "/usr/lib64/libvulkan.so.1",
            "/usr/lib/libvulkan.so.1",
            "/usr/local/lib/libvulkan.so.1"
        };

        foreach (var path in candidates)
        {
            if (File.Exists(path)) return path;
        }

        string output = LdconfigOutput();
        if (!string.IsNullOrEmpty(output) && output.IndexOf("libvulkan.so.1", StringComparison.Ordinal) >= 0)
            return "reported by ldconfig";
        return null;
    }

    static string FindSharedLibrary(string name)
    {
        string output = LdconfigOutput();
        if (!string.IsNullOrEmpty(output) && output.IndexOf(name, StringComparison.Ordinal) >= 0)
            return "reported by ldconfig";
        return null;
    }

    static string LdconfigOutput()
    {
        if (!ldconfigChecked)
        {
            ldconfigChecked = true;
            string ldconfig = File.Exists("/sbin/ldconfig") ? "/sbin/ldconfig" : "ldconfig";
            cachedLdconfig = BakeryLinuxDiagnostics.Capture(ldconfig, "-p", 3000);
        }
        return cachedLdconfig;
    }

    static string FindNvidiaIcd()
    {
        string env = Environment.GetEnvironmentVariable("VK_ICD_FILENAMES");
        if (!string.IsNullOrEmpty(env))
        {
            foreach (var path in env.Split(':'))
            {
                if (!string.IsNullOrEmpty(path) && File.Exists(path)) return path;
            }
        }

        string[] candidates =
        {
            "/usr/share/vulkan/icd.d/nvidia_icd.json",
            "/etc/vulkan/icd.d/nvidia_icd.json",
            "/usr/share/vulkan/icd.d/nvidia_icd.x86_64.json",
            "/run/opengl-driver/share/vulkan/icd.d/nvidia_icd.json"
        };

        foreach (var path in candidates)
        {
            if (File.Exists(path)) return path;
        }
        return null;
    }

    static bool IsAscii(string value)
    {
        if (string.IsNullOrEmpty(value)) return true;
        for (int i = 0; i < value.Length; i++)
        {
            if (value[i] > 127) return false;
        }
        return true;
    }

    static void CheckTool(List<BakeryCheckResult> list, string name, string path,
        BakeryCheckStatus missingStatus, string purpose)
    {
        Add(list, name, path != null ? BakeryCheckStatus.Ok : missingStatus,
            path != null ? path : "not found in PATH (" + purpose + ")");
    }

    static void Add(List<BakeryCheckResult> list, string name, BakeryCheckStatus status, string detail)
    {
        list.Add(new BakeryCheckResult { Name = name, Status = status, Detail = detail });
    }
}
#endif
