using UnityEngine;
using UnityEditor;
using UnityEditor.Build.Reporting;
using System;
using System.IO;
using System.Collections.Generic;

/// <summary>
/// MyTools – Build & Store Automation Tool
/// One-click multi-platform builds with auto-versioning, pre-build validation, and backup.
/// </summary>
public class BuildAutomationTool : EditorWindow
{
    // ──────────────────────────────────────────────────────────────────────────
    //  Constants & styles
    // ──────────────────────────────────────────────────────────────────────────
    private static readonly Color HeaderColor   = new Color(0.15f, 0.55f, 0.85f);
    private static readonly Color SuccessColor  = new Color(0.2f,  0.75f, 0.3f);
    private static readonly Color WarningColor  = new Color(1f,    0.75f, 0f);
    private static readonly Color ErrorColor    = new Color(0.9f,  0.2f,  0.2f);

    // ──────────────────────────────────────────────────────────────────────────
    //  Serialized / persistent fields
    // ──────────────────────────────────────────────────────────────────────────
    // Platform toggles
    private bool buildWindows     = true;
    private bool buildAndroid     = false;
    private bool buildWebGL       = false;
    private bool buildMac         = false;
    private bool buildLinux       = false;
    private bool buildIOS         = false;

    // Versioning
    private bool  autoIncrementBuild  = true;
    private bool  autoIncrementMinor  = false;
    private string customVersionSuffix = "";

    // Output
    private string buildOutputPath = "Builds";
    private bool   createTimestampedFolder = true;
    private bool   backupEnabled  = false;
    private string backupPath     = "BuildBackups";

    // Android settings
    private bool   showAndroidSettings = false;
    private string keystorePath        = "";
    private string keystorePass        = "";
    private string keyAliasName        = "";
    private string keyAliasPass        = "";

    // State
    private Vector2  scrollPos;
    private string   lastBuildReport = "";
    private bool     lastBuildSuccess = true;
    private bool     isBuilding       = false;

    // Pre-build validation results
    private List<string> warnings = new List<string>();
    private List<string> errors   = new List<string>();

    // ──────────────────────────────────────────────────────────────────────────
    //  Menu item
    // ──────────────────────────────────────────────────────────────────────────
    [MenuItem("MyTools/Build & Store Automation %#b")]
    public static void ShowWindow()
    {
        var window = GetWindow<BuildAutomationTool>("Build Automation");
        window.minSize = new Vector2(420, 600);
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  GUI
    // ──────────────────────────────────────────────────────────────────────────
    private void OnGUI()
    {
        scrollPos = EditorGUILayout.BeginScrollView(scrollPos);

        DrawHeader();
        EditorGUILayout.Space(4);

        DrawVersionSection();
        EditorGUILayout.Space(4);

        DrawPlatformSection();
        EditorGUILayout.Space(4);

        DrawAndroidSection();
        EditorGUILayout.Space(4);

        DrawOutputSection();
        EditorGUILayout.Space(4);

        DrawValidationSection();
        EditorGUILayout.Space(8);

        DrawBuildButton();
        EditorGUILayout.Space(4);

        DrawBuildReport();

        EditorGUILayout.EndScrollView();
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  Section: Header
    // ──────────────────────────────────────────────────────────────────────────
    private void DrawHeader()
    {
        var rect = EditorGUILayout.GetControlRect(false, 48);
        EditorGUI.DrawRect(rect, HeaderColor);
        var style = new GUIStyle(EditorStyles.boldLabel)
        {
            fontSize  = 18,
            alignment = TextAnchor.MiddleCenter,
            normal    = { textColor = Color.white }
        };
        GUI.Label(rect, "Build & Store Automation", style);
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  Section: Versioning
    // ──────────────────────────────────────────────────────────────────────────
    private void DrawVersionSection()
    {
        SectionLabel("VERSION");

        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("Current Version", GUILayout.Width(120));
        EditorGUILayout.LabelField(PlayerSettings.bundleVersion, EditorStyles.boldLabel);
        if (GUILayout.Button("Edit", GUILayout.Width(50)))
            SettingsService.OpenProjectSettings("Project/Player");
        EditorGUILayout.EndHorizontal();

        autoIncrementBuild = EditorGUILayout.Toggle("Auto-Increment Build", autoIncrementBuild);
        autoIncrementMinor = EditorGUILayout.Toggle("Auto-Increment Minor", autoIncrementMinor);
        customVersionSuffix = EditorGUILayout.TextField("Version Suffix", customVersionSuffix);

        if (GUILayout.Button("Preview Next Version"))
        {
            string next = ComputeNextVersion();
            EditorUtility.DisplayDialog("Next Version", $"Next version will be:\n{next}", "OK");
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  Section: Platforms
    // ──────────────────────────────────────────────────────────────────────────
    private void DrawPlatformSection()
    {
        SectionLabel("TARGET PLATFORMS");

        int selectedCount = 0;
        buildWindows = PlatformToggle("Windows (x86_64)", buildWindows, ref selectedCount);
        buildAndroid = PlatformToggle("Android",           buildAndroid, ref selectedCount);
        buildWebGL   = PlatformToggle("WebGL",             buildWebGL,   ref selectedCount);
        buildMac     = PlatformToggle("macOS",             buildMac,     ref selectedCount);
        buildLinux   = PlatformToggle("Linux (x86_64)",    buildLinux,   ref selectedCount);
        buildIOS     = PlatformToggle("iOS",               buildIOS,     ref selectedCount);

        EditorGUILayout.HelpBox($"{selectedCount} platform(s) selected.", MessageType.None);
    }

    private bool PlatformToggle(string label, bool value, ref int count)
    {
        bool result = EditorGUILayout.Toggle(label, value);
        if (result) count++;
        return result;
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  Section: Android
    // ──────────────────────────────────────────────────────────────────────────
    private void DrawAndroidSection()
    {
        if (!buildAndroid) return;

        showAndroidSettings = EditorGUILayout.Foldout(showAndroidSettings, "Android Keystore Settings", true);
        if (!showAndroidSettings) return;

        EditorGUI.indentLevel++;

        EditorGUILayout.BeginHorizontal();
        keystorePath = EditorGUILayout.TextField("Keystore Path", keystorePath);
        if (GUILayout.Button("...", GUILayout.Width(30)))
            keystorePath = EditorUtility.OpenFilePanel("Select Keystore", "", "keystore,jks");
        EditorGUILayout.EndHorizontal();

        keystorePass  = EditorGUILayout.PasswordField("Keystore Password", keystorePass);
        keyAliasName  = EditorGUILayout.TextField("Key Alias",            keyAliasName);
        keyAliasPass  = EditorGUILayout.PasswordField("Key Alias Password", keyAliasPass);

        if (GUILayout.Button("Save to Project"))
        {
            PlayerSettings.Android.keystoreName     = keystorePath;
            PlayerSettings.Android.keystorePass     = keystorePass;
            PlayerSettings.Android.keyaliasName     = keyAliasName;
            PlayerSettings.Android.keyaliasPass     = keyAliasPass;
            AssetDatabase.SaveAssets();
            Debug.Log("[BuildAutomation] Android keystore settings saved.");
        }

        EditorGUI.indentLevel--;
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  Section: Output & Backup
    // ──────────────────────────────────────────────────────────────────────────
    private void DrawOutputSection()
    {
        SectionLabel("OUTPUT");

        EditorGUILayout.BeginHorizontal();
        buildOutputPath = EditorGUILayout.TextField("Output Folder", buildOutputPath);
        if (GUILayout.Button("...", GUILayout.Width(30)))
        {
            string chosen = EditorUtility.OpenFolderPanel("Select Output Folder", buildOutputPath, "");
            if (!string.IsNullOrEmpty(chosen)) buildOutputPath = chosen;
        }
        EditorGUILayout.EndHorizontal();

        createTimestampedFolder = EditorGUILayout.Toggle("Timestamped Sub-folder", createTimestampedFolder);

        EditorGUILayout.Space(4);
        backupEnabled = EditorGUILayout.Toggle("Enable Backup", backupEnabled);

        if (backupEnabled)
        {
            EditorGUILayout.BeginHorizontal();
            backupPath = EditorGUILayout.TextField("Backup Folder", backupPath);
            if (GUILayout.Button("...", GUILayout.Width(30)))
            {
                string chosen = EditorUtility.OpenFolderPanel("Select Backup Folder", backupPath, "");
                if (!string.IsNullOrEmpty(chosen)) backupPath = chosen;
            }
            EditorGUILayout.EndHorizontal();
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  Section: Pre-build Validation
    // ──────────────────────────────────────────────────────────────────────────
    private void DrawValidationSection()
    {
        SectionLabel("PRE-BUILD VALIDATION");

        if (GUILayout.Button("Run Validation Now"))
            RunValidation();

        foreach (var w in warnings)
            EditorGUILayout.HelpBox(w, MessageType.Warning);

        foreach (var e in errors)
            EditorGUILayout.HelpBox(e, MessageType.Error);

        if (warnings.Count == 0 && errors.Count == 0)
            EditorGUILayout.HelpBox("No issues detected. Ready to build.", MessageType.Info);
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  Section: Build Button
    // ──────────────────────────────────────────────────────────────────────────
    private void DrawBuildButton()
    {
        GUI.enabled = !isBuilding;

        var btnStyle = new GUIStyle(GUI.skin.button)
        {
            fontSize   = 15,
            fontStyle  = FontStyle.Bold,
            fixedHeight = 44
        };

        if (GUILayout.Button(isBuilding ? "Building…" : "BUILD ALL SELECTED PLATFORMS", btnStyle))
            StartBuild();

        GUI.enabled = true;
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  Section: Build Report
    // ──────────────────────────────────────────────────────────────────────────
    private void DrawBuildReport()
    {
        if (string.IsNullOrEmpty(lastBuildReport)) return;

        SectionLabel("LAST BUILD REPORT");

        var style = new GUIStyle(EditorStyles.helpBox)
        {
            richText  = true,
            wordWrap  = true,
            fontSize  = 11
        };

        GUI.color = lastBuildSuccess ? SuccessColor : ErrorColor;
        EditorGUILayout.LabelField(lastBuildReport, style);
        GUI.color = Color.white;
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  Logic: Versioning
    // ──────────────────────────────────────────────────────────────────────────
    private string ComputeNextVersion()
    {
        string raw = PlayerSettings.bundleVersion;
        string[] parts = raw.Split('.');

        int major = 0, minor = 0, build = 0;
        if (parts.Length > 0) int.TryParse(parts[0], out major);
        if (parts.Length > 1) int.TryParse(parts[1], out minor);
        if (parts.Length > 2) int.TryParse(parts[2], out build);

        if (autoIncrementBuild)  build++;
        if (autoIncrementMinor)  minor++;

        string next = $"{major}.{minor}.{build}";
        if (!string.IsNullOrEmpty(customVersionSuffix))
            next += $"-{customVersionSuffix}";

        return next;
    }

    private void ApplyNextVersion()
    {
        string next = ComputeNextVersion();
        PlayerSettings.bundleVersion = next;

        // Android bundle code
        PlayerSettings.Android.bundleVersionCode++;
        Debug.Log($"[BuildAutomation] Version updated to {next}");
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  Logic: Validation
    // ──────────────────────────────────────────────────────────────────────────
    private void RunValidation()
    {
        warnings.Clear();
        errors.Clear();

        // 1. No platform selected
        if (!buildWindows && !buildAndroid && !buildWebGL && !buildMac && !buildLinux && !buildIOS)
            errors.Add("No build platforms selected.");

        // 2. No scenes in build settings
        if (EditorBuildSettings.scenes.Length == 0)
            errors.Add("No scenes added to Build Settings (File > Build Settings).");

        // 3. App identifier
        if (string.IsNullOrEmpty(PlayerSettings.applicationIdentifier) ||
            PlayerSettings.applicationIdentifier == "com.Company.ProductName")
            warnings.Add("Default bundle identifier detected. Update it before publishing.");

        // 4. Android checks
        if (buildAndroid)
        {
            ValidateAndroidSDK();

            if (string.IsNullOrEmpty(PlayerSettings.Android.keystoreName))
                warnings.Add("Android: No keystore set. Release build will use debug signing.");

            if (PlayerSettings.Android.targetSdkVersion == AndroidSdkVersions.AndroidApiLevelAuto)
                warnings.Add("Android: Target SDK is Auto. Consider setting an explicit API level.");
        }

        // 5. iOS checks
        if (buildIOS)
        {
#if UNITY_EDITOR_WIN
            warnings.Add("iOS: Building iOS on Windows is not supported. Use a Mac.");
#endif
        }

        // 6. Scripting backend
        if (buildAndroid && PlayerSettings.GetScriptingBackend(BuildTargetGroup.Android) == ScriptingImplementation.Mono2x)
            warnings.Add("Android: Scripting backend is Mono. IL2CPP is recommended for release.");

        Repaint();
    }

    private void ValidateAndroidSDK()
    {
        string sdk = EditorPrefs.GetString("AndroidSdkRoot", "");
        if (string.IsNullOrEmpty(sdk) || !Directory.Exists(sdk))
            errors.Add("Android SDK not found. Install it via Unity Hub > Installs > Add Modules.");
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  Logic: Build
    // ──────────────────────────────────────────────────────────────────────────
    private void StartBuild()
    {
        RunValidation();

        if (errors.Count > 0)
        {
            EditorUtility.DisplayDialog(
                "Build Blocked",
                $"Fix {errors.Count} error(s) before building:\n\n" + string.Join("\n", errors),
                "OK");
            return;
        }

        if (warnings.Count > 0)
        {
            bool proceed = EditorUtility.DisplayDialog(
                "Warnings Detected",
                $"There are {warnings.Count} warning(s). Build anyway?\n\n" + string.Join("\n", warnings),
                "Build Anyway", "Cancel");
            if (!proceed) return;
        }

        isBuilding = true;
        Repaint();

        if (autoIncrementBuild || autoIncrementMinor)
            ApplyNextVersion();

        string timestamp = createTimestampedFolder
            ? DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss")
            : "Latest";

        string rootOutput = Path.Combine(buildOutputPath, timestamp);
        Directory.CreateDirectory(rootOutput);

        var reportLines = new List<string>();
        bool anyFailed  = false;

        BuildPlatform(BuildTarget.StandaloneWindows64, BuildTargetGroup.Standalone,
            buildWindows, Path.Combine(rootOutput, "Windows",  Application.productName + ".exe"),
            reportLines, ref anyFailed);

        BuildPlatform(BuildTarget.Android, BuildTargetGroup.Android,
            buildAndroid, Path.Combine(rootOutput, "Android",  Application.productName + ".apk"),
            reportLines, ref anyFailed);

        BuildPlatform(BuildTarget.WebGL, BuildTargetGroup.WebGL,
            buildWebGL, Path.Combine(rootOutput, "WebGL",
            Application.productName), // WebGL builds to a folder
            reportLines, ref anyFailed);

        BuildPlatform(BuildTarget.StandaloneOSX, BuildTargetGroup.Standalone,
            buildMac, Path.Combine(rootOutput, "Mac",    Application.productName + ".app"),
            reportLines, ref anyFailed);

        BuildPlatform(BuildTarget.StandaloneLinux64, BuildTargetGroup.Standalone,
            buildLinux, Path.Combine(rootOutput, "Linux",  Application.productName),
            reportLines, ref anyFailed);

        BuildPlatform(BuildTarget.iOS, BuildTargetGroup.iOS,
            buildIOS, Path.Combine(rootOutput, "iOS",    Application.productName),
            reportLines, ref anyFailed);

        if (backupEnabled)
            TryBackup(rootOutput);

        lastBuildReport  = string.Join("\n", reportLines);
        lastBuildSuccess = !anyFailed;
        isBuilding       = false;

        string title  = anyFailed ? "Build Completed with Errors" : "Build Successful!";
        string detail = $"Output: {rootOutput}\n\n" + string.Join("\n", reportLines);
        EditorUtility.DisplayDialog(title, detail, "OK");

        Repaint();
    }

    private void BuildPlatform(
        BuildTarget    target,
        BuildTargetGroup group,
        bool           enabled,
        string         outputPath,
        List<string>   report,
        ref bool       anyFailed)
    {
        if (!enabled) return;

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

        var options = new BuildPlayerOptions
        {
            scenes      = GetEnabledScenes(),
            locationPathName = outputPath,
            target      = target,
            targetGroup = group,
            options     = BuildOptions.None
        };

        BuildReport    result  = BuildPipeline.BuildPlayer(options);
        BuildSummary   summary = result.summary;

        bool ok = summary.result == BuildResult.Succeeded;
        if (!ok) anyFailed = true;

        string icon = ok ? "✓" : "✗";
        string line = $"{icon} {target}: {summary.result}  " +
                      $"({summary.totalSize / 1024 / 1024} MB, {summary.totalTime:mm\\:ss})";
        report.Add(line);
        Debug.Log($"[BuildAutomation] {line}");
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  Logic: Backup
    // ──────────────────────────────────────────────────────────────────────────
    private void TryBackup(string sourceDir)
    {
        try
        {
            string dest = Path.Combine(backupPath, Path.GetFileName(sourceDir));
            CopyDirectory(sourceDir, dest);
            Debug.Log($"[BuildAutomation] Backup saved to: {dest}");
        }
        catch (Exception ex)
        {
            Debug.LogError($"[BuildAutomation] Backup failed: {ex.Message}");
        }
    }

    private static void CopyDirectory(string src, string dst)
    {
        Directory.CreateDirectory(dst);
        foreach (string file in Directory.GetFiles(src))
            File.Copy(file, Path.Combine(dst, Path.GetFileName(file)), true);
        foreach (string dir in Directory.GetDirectories(src))
            CopyDirectory(dir, Path.Combine(dst, Path.GetFileName(dir)));
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  Helpers
    // ──────────────────────────────────────────────────────────────────────────
    private static string[] GetEnabledScenes()
    {
        var scenes = new List<string>();
        foreach (var s in EditorBuildSettings.scenes)
            if (s.enabled) scenes.Add(s.path);
        return scenes.ToArray();
    }

    private static void SectionLabel(string text)
    {
        var style = new GUIStyle(EditorStyles.boldLabel)
        {
            fontSize = 11,
            normal   = { textColor = new Color(0.6f, 0.85f, 1f) }
        };
        EditorGUILayout.Space(2);
        EditorGUILayout.LabelField(text, style);
        Rect r = EditorGUILayout.GetControlRect(false, 1);
        EditorGUI.DrawRect(r, new Color(0.6f, 0.85f, 1f, 0.4f));
        EditorGUILayout.Space(2);
    }
}
