using UnityEngine;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine.Rendering;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

/// <summary>
/// MyTools – Automated Performance Analyzer
/// Scans your project and scene for performance issues and prints an
/// actionable report with specific recommendations.
/// </summary>
public class PerformanceAnalyzerTool : EditorWindow
{
    // ──────────────────────────────────────────────────────────────────────────
    //  Internal data
    // ──────────────────────────────────────────────────────────────────────────
    private enum Severity { Info, Warning, Error }

    private class Finding
    {
        public Severity  Severity;
        public string    Category;
        public string    Title;
        public string    Detail;
        public string    Fix;
    }

    private List<Finding> _findings = new List<Finding>();
    private Vector2       _scroll;
    private string        _filterCategory = "All";
    private bool          _analysisRun    = false;
    private bool          _isRunning      = false;

    // Quality thresholds (editable in window)
    private int   maxRecommendedDrawCalls     = 100;
    private int   maxRecommendedTextureSize   = 2048;
    private int   maxRecommendedPolyCount     = 150_000;
    private int   maxRecommendedAudioFiles    = 30;
    private float maxRecommendedLoadSceneTime = 2f;

    // Category tabs
    private static readonly string[] Categories =
        { "All", "Rendering", "Audio", "Physics", "Scripts", "Assets", "Lighting" };

    // ──────────────────────────────────────────────────────────────────────────
    //  Menu
    // ──────────────────────────────────────────────────────────────────────────
    [MenuItem("MyTools/Performance Analyzer %#p")]
    public static void ShowWindow()
    {
        var w = GetWindow<PerformanceAnalyzerTool>("Performance Analyzer");
        w.minSize = new Vector2(500, 620);
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  GUI
    // ──────────────────────────────────────────────────────────────────────────
    private void OnGUI()
    {
        DrawHeader();

        EditorGUILayout.Space(4);
        DrawThresholds();

        EditorGUILayout.Space(4);
        DrawControlBar();

        EditorGUILayout.Space(4);

        if (_analysisRun)
        {
            DrawSummaryBar();
            EditorGUILayout.Space(4);
            DrawCategoryTabs();
            EditorGUILayout.Space(4);
            DrawFindings();
        }
        else
        {
            EditorGUILayout.HelpBox(
                "Click \"Run Analysis\" to scan your project and active scene.",
                MessageType.Info);
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  Header
    // ──────────────────────────────────────────────────────────────────────────
    private void DrawHeader()
    {
        var rect = EditorGUILayout.GetControlRect(false, 48);
        EditorGUI.DrawRect(rect, new Color(0.1f, 0.45f, 0.25f));
        var style = new GUIStyle(EditorStyles.boldLabel)
        {
            fontSize  = 18,
            alignment = TextAnchor.MiddleCenter,
            normal    = { textColor = Color.white }
        };
        GUI.Label(rect, "Performance Analyzer", style);
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  Thresholds
    // ──────────────────────────────────────────────────────────────────────────
    private bool _showThresholds;
    private void DrawThresholds()
    {
        _showThresholds = EditorGUILayout.Foldout(_showThresholds, "Analysis Thresholds", true);
        if (!_showThresholds) return;

        EditorGUI.indentLevel++;
        maxRecommendedDrawCalls   = EditorGUILayout.IntField("Max Draw Calls",       maxRecommendedDrawCalls);
        maxRecommendedTextureSize = EditorGUILayout.IntField("Max Texture Size (px)", maxRecommendedTextureSize);
        maxRecommendedPolyCount   = EditorGUILayout.IntField("Max Poly Count",        maxRecommendedPolyCount);
        maxRecommendedAudioFiles  = EditorGUILayout.IntField("Max Audio Files",       maxRecommendedAudioFiles);
        EditorGUI.indentLevel--;
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  Control bar
    // ──────────────────────────────────────────────────────────────────────────
    private void DrawControlBar()
    {
        EditorGUILayout.BeginHorizontal();

        GUI.enabled = !_isRunning;
        var style = new GUIStyle(GUI.skin.button) { fontStyle = FontStyle.Bold, fixedHeight = 36 };
        if (GUILayout.Button(_isRunning ? "Analyzing…" : "Run Analysis", style))
            RunAnalysis();
        GUI.enabled = true;

        if (_analysisRun && GUILayout.Button("Export Report", GUILayout.Width(110), GUILayout.Height(36)))
            ExportReport();

        if (_analysisRun && GUILayout.Button("Clear", GUILayout.Width(60), GUILayout.Height(36)))
        {
            _findings.Clear();
            _analysisRun = false;
            Repaint();
        }

        EditorGUILayout.EndHorizontal();
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  Summary bar
    // ──────────────────────────────────────────────────────────────────────────
    private void DrawSummaryBar()
    {
        int errors   = _findings.Count(f => f.Severity == Severity.Error);
        int warnings = _findings.Count(f => f.Severity == Severity.Warning);
        int infos    = _findings.Count(f => f.Severity == Severity.Info);

        EditorGUILayout.BeginHorizontal();
        SummaryPill($"✗  {errors} Errors",   new Color(0.9f, 0.2f, 0.2f));
        SummaryPill($"⚠  {warnings} Warnings", new Color(0.9f, 0.65f, 0f));
        SummaryPill($"i  {infos} Info",       new Color(0.3f, 0.55f, 0.9f));
        EditorGUILayout.EndHorizontal();
    }

    private void SummaryPill(string text, Color color)
    {
        var style = new GUIStyle(GUI.skin.box)
        {
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter,
            normal    = { textColor = Color.white, background = MakeTex(2, 2, color) }
        };
        GUILayout.Label(text, style, GUILayout.Height(28), GUILayout.ExpandWidth(true));
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  Category tabs
    // ──────────────────────────────────────────────────────────────────────────
    private void DrawCategoryTabs()
    {
        int selected = Array.IndexOf(Categories, _filterCategory);
        if (selected < 0) selected = 0;
        selected = GUILayout.Toolbar(selected, Categories);
        _filterCategory = Categories[selected];
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  Findings list
    // ──────────────────────────────────────────────────────────────────────────
    private void DrawFindings()
    {
        _scroll = EditorGUILayout.BeginScrollView(_scroll);

        var filtered = _filterCategory == "All"
            ? _findings
            : _findings.Where(f => f.Category == _filterCategory).ToList();

        if (filtered.Count == 0)
        {
            EditorGUILayout.HelpBox("No findings in this category. 🎉", MessageType.Info);
        }
        else
        {
            foreach (var f in filtered)
                DrawFinding(f);
        }

        EditorGUILayout.EndScrollView();
    }

    private void DrawFinding(Finding f)
    {
        Color bg = f.Severity switch
        {
            Severity.Error   => new Color(0.35f, 0.1f,  0.1f),
            Severity.Warning => new Color(0.35f, 0.28f, 0.05f),
            _                => new Color(0.15f, 0.22f, 0.35f)
        };

        var boxStyle = new GUIStyle(EditorStyles.helpBox)
        {
            normal = { background = MakeTex(2, 2, bg) }
        };

        EditorGUILayout.BeginVertical(boxStyle);

        // Title row
        EditorGUILayout.BeginHorizontal();
        string icon = f.Severity switch
        {
            Severity.Error   => "✗",
            Severity.Warning => "⚠",
            _                => "i"
        };
        Color iconColor = f.Severity switch
        {
            Severity.Error   => new Color(1f, 0.4f, 0.4f),
            Severity.Warning => new Color(1f, 0.8f, 0.2f),
            _                => new Color(0.5f, 0.8f, 1f)
        };
        var iconStyle = new GUIStyle(EditorStyles.boldLabel)
        {
            normal = { textColor = iconColor },
            fontSize = 14
        };
        GUILayout.Label(icon, iconStyle, GUILayout.Width(20));

        var titleStyle = new GUIStyle(EditorStyles.boldLabel)
        {
            normal   = { textColor = Color.white },
            wordWrap = true
        };
        GUILayout.Label($"[{f.Category}] {f.Title}", titleStyle);
        EditorGUILayout.EndHorizontal();

        // Detail
        var detailStyle = new GUIStyle(EditorStyles.label)
        {
            normal   = { textColor = new Color(0.85f, 0.85f, 0.85f) },
            wordWrap = true
        };
        if (!string.IsNullOrEmpty(f.Detail))
        {
            EditorGUI.indentLevel++;
            EditorGUILayout.LabelField(f.Detail, detailStyle);
            EditorGUI.indentLevel--;
        }

        // Fix
        if (!string.IsNullOrEmpty(f.Fix))
        {
            var fixStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                normal   = { textColor = new Color(0.6f, 1f, 0.7f) },
                wordWrap = true,
                fontStyle = FontStyle.Italic
            };
            EditorGUI.indentLevel++;
            EditorGUILayout.LabelField("FIX: " + f.Fix, fixStyle);
            EditorGUI.indentLevel--;
        }

        EditorGUILayout.EndVertical();
        EditorGUILayout.Space(2);
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  Analysis Engine
    // ──────────────────────────────────────────────────────────────────────────
    private void RunAnalysis()
    {
        _isRunning = true;
        _findings.Clear();
        Repaint();

        try
        {
            AnalyzeRendering();
            AnalyzeTextures();
            AnalyzeMeshes();
            AnalyzeAudio();
            AnalyzePhysics();
            AnalyzeScripts();
            AnalyzeLighting();
            AnalyzeCameras();
        }
        catch (Exception ex)
        {
            _findings.Add(new Finding
            {
                Severity = Severity.Error,
                Category = "Scripts",
                Title    = "Analysis Exception",
                Detail   = ex.Message
            });
        }

        _analysisRun = true;
        _isRunning   = false;
        Repaint();
        Debug.Log($"[PerformanceAnalyzer] Done. {_findings.Count} finding(s).");
    }

    // ── Rendering ──────────────────────────────────────────────────────────────
    private void AnalyzeRendering()
    {
        // Read batching settings via SerializedObject (GetStaticBatching/GetDynamicBatching
        // were removed in Unity 2022.2+)
        var settingsAssets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset");
        if (settingsAssets.Length > 0)
        {
            var so = new SerializedObject(settingsAssets[0]);
            bool staticBatching  = so.FindProperty("staticBatching")?.boolValue  ?? true;
            bool dynamicBatching = so.FindProperty("dynamicBatching")?.boolValue ?? true;

            if (!staticBatching)
                Add(Severity.Warning, "Rendering", "Static Batching is disabled",
                    "Static batching merges meshes at build time and reduces draw calls.",
                    "Enable it in Edit > Project Settings > Player > Other Settings > Static Batching.");

            if (!dynamicBatching)
                Add(Severity.Warning, "Rendering", "Dynamic Batching is disabled",
                    "Dynamic batching reduces draw calls for small, identical meshes at runtime.",
                    "Enable it in Edit > Project Settings > Player > Other Settings > Dynamic Batching.");
        }

        // GPU instancing: find renderers not using instanced materials
        var renderers = UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None);
        int nonInstanced = renderers
            .SelectMany(r => r.sharedMaterials)
            .Where(m => m != null && !m.enableInstancing)
            .Select(m => m.name)
            .Distinct()
            .Count();

        if (nonInstanced > 5)
            Add(Severity.Warning, "Rendering",
                $"{nonInstanced} materials do not have GPU Instancing enabled",
                "GPU Instancing lets the GPU render many identical objects in one call.",
                "Select each material → enable 'Enable GPU Instancing'.");

        // Overdraw: many transparent objects
        int transparentCount = renderers
            .Where(r => r.sharedMaterials.Any(m =>
                m != null && m.renderQueue >= (int)RenderQueue.Transparent))
            .Count();

        if (transparentCount > 20)
            Add(Severity.Warning, "Rendering",
                $"{transparentCount} transparent renderers found",
                "High transparent renderer count causes overdraw and GPU fill-rate issues.",
                "Minimize alpha-blended objects; prefer alpha-test (cutout) or opaque shaders.");
    }

    // ── Textures ───────────────────────────────────────────────────────────────
    private void AnalyzeTextures()
    {
        string[] guids = AssetDatabase.FindAssets("t:Texture2D");
        int over    = 0;
        int noMip   = 0;
        int notPow2 = 0;
        long totalEstimatedBytes = 0;

        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (!path.StartsWith("Assets/")) continue;

            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) continue;

            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (tex == null) continue;

            if (tex.width > maxRecommendedTextureSize || tex.height > maxRecommendedTextureSize)
                over++;

            if (!importer.mipmapEnabled && importer.textureType != TextureImporterType.Sprite)
                noMip++;

            bool isPow2 = IsPow2(tex.width) && IsPow2(tex.height);
            if (!isPow2) notPow2++;

            // Rough VRAM estimate (uncompressed RGBA)
            totalEstimatedBytes += tex.width * tex.height * 4L;
        }

        if (over > 0)
            Add(Severity.Warning, "Assets",
                $"{over} texture(s) exceed {maxRecommendedTextureSize}px",
                "Large textures consume GPU memory and increase load times.",
                "Reduce max size in the Texture Importer, or use Mip Maps.");

        if (noMip > 0)
            Add(Severity.Warning, "Rendering",
                $"{noMip} non-UI texture(s) have Mip Maps disabled",
                "Mip Maps reduce GPU cache misses at distance.",
                "Enable 'Generate Mip Maps' in the Texture Importer for 3D textures.");

        if (notPow2 > 3)
            Add(Severity.Info, "Assets",
                $"{notPow2} texture(s) are not power-of-two",
                "Non-POT textures cannot be compressed on some platforms.",
                "Resize textures to 64, 128, 256, 512, 1024, 2048 etc.");

        long estimatedMB = totalEstimatedBytes / 1024 / 1024;
        if (estimatedMB > 256)
            Add(Severity.Warning, "Assets",
                $"Estimated uncompressed texture VRAM: ~{estimatedMB} MB",
                "Exceeds a typical mobile VRAM budget.",
                "Enable GPU compression (ASTC/ETC2 for mobile) in Texture Importer.");
    }

    // ── Meshes ─────────────────────────────────────────────────────────────────
    private void AnalyzeMeshes()
    {
        string[] guids = AssetDatabase.FindAssets("t:Mesh");
        int totalTris  = 0;
        int highPolyCount = 0;

        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (!path.StartsWith("Assets/")) continue;

            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (mesh == null) continue;

            int tris = mesh.triangles.Length / 3;
            totalTris += tris;
            if (tris > 10_000) highPolyCount++;
        }

        if (totalTris > maxRecommendedPolyCount)
            Add(Severity.Warning, "Rendering",
                $"Total mesh triangle count: {totalTris:N0}",
                $"Exceeds recommended limit of {maxRecommendedPolyCount:N0} for mobile.",
                "Use LOD Groups, mesh simplification, or remove hidden geometry.");

        if (highPolyCount > 5)
            Add(Severity.Warning, "Rendering",
                $"{highPolyCount} mesh asset(s) have more than 10 000 triangles individually",
                "High-poly meshes should use LOD levels.",
                "Set up LOD Groups (Component > LOD Group) with 3 LOD levels.");
    }

    // ── Audio ──────────────────────────────────────────────────────────────────
    private void AnalyzeAudio()
    {
        string[] guids = AssetDatabase.FindAssets("t:AudioClip");

        int uncompressed    = 0;
        int loadInMemory    = 0;
        int longAmbientPCM  = 0;

        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (!path.StartsWith("Assets/")) continue;

            var importer = AssetImporter.GetAtPath(path) as AudioImporter;
            if (importer == null) continue;

            var s = importer.defaultSampleSettings;

            if (s.compressionFormat == AudioCompressionFormat.PCM) uncompressed++;
            if (s.loadType == AudioClipLoadType.DecompressOnLoad)   loadInMemory++;

            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
            if (clip != null && clip.length > 10f &&
                s.compressionFormat == AudioCompressionFormat.PCM)
                longAmbientPCM++;
        }

        if (guids.Length > maxRecommendedAudioFiles)
            Add(Severity.Info, "Audio",
                $"{guids.Length} audio assets found",
                "Many audio files increase build size.",
                "Archive unused clips; share SFX via AudioMixer snapshots.");

        if (uncompressed > 0)
            Add(Severity.Warning, "Audio",
                $"{uncompressed} audio clip(s) use PCM (uncompressed)",
                "PCM audio greatly increases build size and memory.",
                "Switch to Vorbis (quality 70) for music / long clips, ADPCM for short SFX.");

        if (longAmbientPCM > 0)
            Add(Severity.Error, "Audio",
                $"{longAmbientPCM} long clip(s) are set to DecompressOnLoad + PCM",
                "This loads the full waveform into RAM at startup.",
                "Set Load Type to 'Streaming' for music and ambient audio.");
    }

    // ── Physics ────────────────────────────────────────────────────────────────
    private void AnalyzePhysics()
    {
        var colliders  = UnityEngine.Object.FindObjectsByType<Collider>(FindObjectsSortMode.None);
        int meshCols   = colliders.OfType<MeshCollider>().Count();
        int convexCols = colliders.OfType<MeshCollider>().Count(c => c.convex);
        int nonConvex  = meshCols - convexCols;

        if (nonConvex > 0)
            Add(Severity.Warning, "Physics",
                $"{nonConvex} non-convex MeshCollider(s) found",
                "Non-convex mesh colliders are expensive and unsupported for dynamic Rigidbodies.",
                "Use convex MeshColliders, or replace with primitive colliders (Box/Capsule/Sphere).");

        var rigidbodies = UnityEngine.Object.FindObjectsByType<Rigidbody>(FindObjectsSortMode.None);
        int sleeping    = rigidbodies.Count(rb => rb.IsSleeping());
        if (rigidbodies.Length > 50)
            Add(Severity.Warning, "Physics",
                $"{rigidbodies.Length} Rigidbodies in scene (sleeping: {sleeping})",
                "Large numbers of Rigidbodies strain the physics solver.",
                "Use object pooling, sleep thresholds, and fixed simulation rates.");
    }

    // ── Scripts ────────────────────────────────────────────────────────────────
    private void AnalyzeScripts()
    {
        // Find Update-heavy scripts (heuristic: count MonoBehaviours)
        var monos = UnityEngine.Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None);
        if (monos.Length > 200)
            Add(Severity.Warning, "Scripts",
                $"{monos.Length} MonoBehaviour instances active in scene",
                "Many active MonoBehaviours increase Update() overhead each frame.",
                "Use an ECS/manager pattern; disable or consolidate inactive behaviour scripts.");

        // Scripting backend
        var namedTarget = NamedBuildTarget.FromBuildTargetGroup(
            BuildPipeline.GetBuildTargetGroup(EditorUserBuildSettings.activeBuildTarget));
        var backend = PlayerSettings.GetScriptingBackend(namedTarget);
        if (backend == ScriptingImplementation.Mono2x)
            Add(Severity.Warning, "Scripts",
                "Scripting Backend is Mono",
                "IL2CPP produces faster native code and smaller binary for release builds.",
                "Edit > Project Settings > Player > Other Settings > Scripting Backend → IL2CPP.");
    }

    // ── Lighting ───────────────────────────────────────────────────────────────
    private void AnalyzeLighting()
    {
        var lights = UnityEngine.Object.FindObjectsByType<Light>(FindObjectsSortMode.None);

        int realtimeShadows = lights.Count(l => l.shadows != LightShadows.None && !l.bakingOutput.isBaked);
        if (realtimeShadows > 2)
            Add(Severity.Warning, "Lighting",
                $"{realtimeShadows} lights cast real-time shadows",
                "Each shadow-casting light doubles the draw calls for affected objects.",
                "Bake shadows for static lights (Baked mode); limit real-time shadows to 1–2 directional lights.");

        // Check if lightmaps are baked
        if (LightmapSettings.lightmaps.Length == 0 && lights.Length > 2)
            Add(Severity.Info, "Lighting",
                "No baked lightmaps detected",
                "Baked lighting pre-computes illumination and removes runtime light cost.",
                "Set static lights to 'Baked' or 'Mixed' and run Window > Rendering > Lighting > Generate.");

        // HDR
        var cameras = UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsSortMode.None);
        int hdrCams = cameras.Count(c => c.allowHDR);
        if (hdrCams > 0)
            Add(Severity.Info, "Rendering",
                $"{hdrCams} camera(s) have HDR enabled",
                "HDR requires an extra render pass and more GPU memory.",
                "Disable HDR on cameras where post-processing bloom is not required.");
    }

    // ── Cameras ────────────────────────────────────────────────────────────────
    private void AnalyzeCameras()
    {
        var cameras = UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsSortMode.None);

        int msaaCams = cameras.Count(c => c.allowMSAA);
        if (msaaCams > 0)
            Add(Severity.Info, "Rendering",
                $"{msaaCams} camera(s) have MSAA enabled",
                "MSAA increases GPU memory bandwidth. On mobile prefer FXAA or TAA.",
                "Disable MSAA on the camera and use a post-process anti-aliasing pass instead.");

        if (cameras.Length > 3)
            Add(Severity.Warning, "Rendering",
                $"{cameras.Length} cameras found in scene",
                "Each camera triggers a full render pass.",
                "Merge render targets; use a single camera with render layers.");
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  Export
    // ──────────────────────────────────────────────────────────────────────────
    private void ExportReport()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# Performance Analysis Report");
        sb.AppendLine($"Project : {Application.productName}");
        sb.AppendLine($"Date    : {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"Findings: {_findings.Count}");
        sb.AppendLine();

        foreach (var f in _findings)
        {
            sb.AppendLine($"[{f.Severity.ToString().ToUpper()}] [{f.Category}] {f.Title}");
            if (!string.IsNullOrEmpty(f.Detail)) sb.AppendLine($"  Detail : {f.Detail}");
            if (!string.IsNullOrEmpty(f.Fix))    sb.AppendLine($"  Fix    : {f.Fix}");
            sb.AppendLine();
        }

        string path = EditorUtility.SaveFilePanel(
            "Save Report", Application.dataPath, "PerformanceReport", "md");
        if (string.IsNullOrEmpty(path)) return;

        System.IO.File.WriteAllText(path, sb.ToString());
        EditorUtility.RevealInFinder(path);
        Debug.Log($"[PerformanceAnalyzer] Report saved: {path}");
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  Helpers
    // ──────────────────────────────────────────────────────────────────────────
    private void Add(Severity sev, string cat, string title, string detail = "", string fix = "")
    {
        _findings.Add(new Finding
        {
            Severity = sev,
            Category = cat,
            Title    = title,
            Detail   = detail,
            Fix      = fix
        });
    }

    private static bool IsPow2(int n) => n > 0 && (n & (n - 1)) == 0;

    private static Texture2D MakeTex(int w, int h, Color col)
    {
        var tex = new Texture2D(w, h);
        var pix = new Color[w * h];
        for (int i = 0; i < pix.Length; i++) pix[i] = col;
        tex.SetPixels(pix);
        tex.Apply();
        return tex;
    }
}
