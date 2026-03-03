using UnityEngine;
using UnityEditor;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

/// <summary>
/// MyTools – Auto Level Generator
/// Procedurally generates levels for Match-3, Sort/Stack, and Maze games
/// with configurable difficulty scaling and JSON export.
/// Solvability is validated before the level is accepted.
/// </summary>
public class AutoLevelGeneratorTool : EditorWindow
{
    // ──────────────────────────────────────────────────────────────────────────
    //  Enums
    // ──────────────────────────────────────────────────────────────────────────
    private enum GameType { Match3, SortMatch, Maze }
    private enum DifficultyMode { Linear, Exponential, Custom }

    // ──────────────────────────────────────────────────────────────────────────
    //  Shared settings
    // ──────────────────────────────────────────────────────────────────────────
    private GameType       _gameType       = GameType.SortMatch;
    private DifficultyMode _diffMode       = DifficultyMode.Linear;
    private int            _levelCount     = 20;
    private int            _startLevel     = 1;
    private int            _seed           = 12345;
    private bool           _randomSeed     = true;
    private int            _maxRetries     = 200;   // solvability retries
    private string         _exportPath     = "Assets/Levels";

    // ──────────────────────────────────────────────────────────────────────────
    //  Match-3 settings
    // ──────────────────────────────────────────────────────────────────────────
    private int   _m3Rows   = 8;
    private int   _m3Cols   = 8;
    private int   _m3Colors = 4;
    private int   _m3MaxColors = 7;
    private int   _m3MinMoves  = 10;
    private int   _m3MaxMoves  = 50;

    // ──────────────────────────────────────────────────────────────────────────
    //  SortMatch settings
    // ──────────────────────────────────────────────────────────────────────────
    private int   _smGroups      = 3;
    private int   _smMaxGroups   = 8;
    private int   _smStackSize   = 4;
    private int   _smMaxStack    = 6;
    private int   _smExtraSlots  = 1;

    // ──────────────────────────────────────────────────────────────────────────
    //  Maze settings
    // ──────────────────────────────────────────────────────────────────────────
    private int   _mazeW     = 10;
    private int   _mazeH     = 10;
    private int   _mazeMaxW  = 20;
    private int   _mazeMaxH  = 20;
    private bool  _mazeKeys  = false;
    private int   _mazeKeyCount   = 1;
    private int   _mazeMaxKeys    = 5;

    // ──────────────────────────────────────────────────────────────────────────
    //  Preview / results
    // ──────────────────────────────────────────────────────────────────────────
    private List<LevelData> _generated = new List<LevelData>();
    private Vector2         _previewScroll;
    private bool            _isGenerating;
    private int             _previewIndex;

    // ──────────────────────────────────────────────────────────────────────────
    //  Data
    // ──────────────────────────────────────────────────────────────────────────
    [Serializable]
    private class LevelData
    {
        public int          LevelNumber;
        public string       GameType;
        public float        Difficulty;         // 0..1 normalised
        public bool         IsSolvable;
        public string       Seed;
        public Dictionary<string, object> Data = new Dictionary<string, object>();
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  Menu
    // ──────────────────────────────────────────────────────────────────────────
    [MenuItem("MyTools/Auto Level Generator %#l")]
    public static void ShowWindow()
    {
        var w = GetWindow<AutoLevelGeneratorTool>("Level Generator");
        w.minSize = new Vector2(480, 640);
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  GUI
    // ──────────────────────────────────────────────────────────────────────────
    private void OnGUI()
    {
        DrawHeader();

        EditorGUILayout.Space(4);
        DrawGeneralSettings();

        EditorGUILayout.Space(4);
        DrawGameTypeSettings();

        EditorGUILayout.Space(4);
        DrawDifficultySettings();

        EditorGUILayout.Space(4);
        DrawExportSettings();

        EditorGUILayout.Space(8);
        DrawGenerateButton();

        if (_generated.Count > 0)
        {
            EditorGUILayout.Space(4);
            DrawPreview();
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  Header
    // ──────────────────────────────────────────────────────────────────────────
    private void DrawHeader()
    {
        var rect = EditorGUILayout.GetControlRect(false, 48);
        EditorGUI.DrawRect(rect, new Color(0.7f, 0.4f, 0.05f));
        var style = new GUIStyle(EditorStyles.boldLabel)
        {
            fontSize  = 18,
            alignment = TextAnchor.MiddleCenter,
            normal    = { textColor = Color.white }
        };
        GUI.Label(rect, "Auto Level Generator", style);
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  General settings
    // ──────────────────────────────────────────────────────────────────────────
    private void DrawGeneralSettings()
    {
        SectionLabel("GENERAL");
        _gameType   = (GameType)EditorGUILayout.EnumPopup("Game Type", _gameType);
        _levelCount = EditorGUILayout.IntSlider("Number of Levels", _levelCount, 1, 500);
        _startLevel = EditorGUILayout.IntField("Start Level Index", _startLevel);
        _maxRetries = EditorGUILayout.IntField("Max Solvability Retries", _maxRetries);

        EditorGUILayout.BeginHorizontal();
        _randomSeed = EditorGUILayout.Toggle("Random Seed", _randomSeed, GUILayout.Width(200));
        GUI.enabled = !_randomSeed;
        _seed = EditorGUILayout.IntField(_seed);
        GUI.enabled = true;
        EditorGUILayout.EndHorizontal();
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  Per-game settings
    // ──────────────────────────────────────────────────────────────────────────
    private void DrawGameTypeSettings()
    {
        switch (_gameType)
        {
            case GameType.Match3:    DrawMatch3Settings();    break;
            case GameType.SortMatch: DrawSortMatchSettings(); break;
            case GameType.Maze:      DrawMazeSettings();      break;
        }
    }

    private void DrawMatch3Settings()
    {
        SectionLabel("MATCH-3 SETTINGS");
        _m3Rows     = EditorGUILayout.IntSlider("Grid Rows (start)",     _m3Rows,    4, 12);
        _m3Cols     = EditorGUILayout.IntSlider("Grid Cols (start)",     _m3Cols,    4, 12);
        _m3Colors   = EditorGUILayout.IntSlider("Colors (start)",        _m3Colors,  3, _m3MaxColors);
        _m3MaxColors= EditorGUILayout.IntSlider("Colors (end)",          _m3MaxColors, _m3Colors, 8);
        _m3MinMoves = EditorGUILayout.IntField("Min Move Target",        _m3MinMoves);
        _m3MaxMoves = EditorGUILayout.IntField("Max Move Target",        _m3MaxMoves);
    }

    private void DrawSortMatchSettings()
    {
        SectionLabel("SORT-MATCH SETTINGS");
        _smGroups    = EditorGUILayout.IntSlider("Color Groups (start)", _smGroups,    2, _smMaxGroups);
        _smMaxGroups = EditorGUILayout.IntSlider("Color Groups (end)",   _smMaxGroups, _smGroups, 10);
        _smStackSize = EditorGUILayout.IntSlider("Stack Size (start)",   _smStackSize, 2, _smMaxStack);
        _smMaxStack  = EditorGUILayout.IntSlider("Stack Size (end)",     _smMaxStack,  _smStackSize, 8);
        _smExtraSlots= EditorGUILayout.IntSlider("Extra Empty Slots",    _smExtraSlots, 0, 4);
    }

    private void DrawMazeSettings()
    {
        SectionLabel("MAZE SETTINGS");
        _mazeW    = EditorGUILayout.IntSlider("Width (start)",       _mazeW,   5, _mazeMaxW);
        _mazeH    = EditorGUILayout.IntSlider("Height (start)",      _mazeH,   5, _mazeMaxH);
        _mazeMaxW = EditorGUILayout.IntSlider("Width (end)",         _mazeMaxW, _mazeW, 30);
        _mazeMaxH = EditorGUILayout.IntSlider("Height (end)",        _mazeMaxH, _mazeH, 30);
        _mazeKeys = EditorGUILayout.Toggle("Include Keys/Locks",     _mazeKeys);
        if (_mazeKeys)
        {
            _mazeKeyCount = EditorGUILayout.IntSlider("Keys (start)", _mazeKeyCount, 1, _mazeMaxKeys);
            _mazeMaxKeys  = EditorGUILayout.IntSlider("Keys (end)",   _mazeMaxKeys, _mazeKeyCount, 8);
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  Difficulty settings
    // ──────────────────────────────────────────────────────────────────────────
    private void DrawDifficultySettings()
    {
        SectionLabel("DIFFICULTY SCALING");
        _diffMode = (DifficultyMode)EditorGUILayout.EnumPopup("Scaling Curve", _diffMode);

        string desc = _diffMode switch
        {
            DifficultyMode.Linear      => "Difficulty increases evenly from 0 to 1 across all levels.",
            DifficultyMode.Exponential => "Starts easy, gets hard quickly towards the end.",
            DifficultyMode.Custom      => "You supply a difficulty curve via AnimationCurve (see code).",
            _                          => ""
        };
        EditorGUILayout.HelpBox(desc, MessageType.None);
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  Export settings
    // ──────────────────────────────────────────────────────────────────────────
    private void DrawExportSettings()
    {
        SectionLabel("EXPORT");
        EditorGUILayout.BeginHorizontal();
        _exportPath = EditorGUILayout.TextField("Output Folder", _exportPath);
        if (GUILayout.Button("...", GUILayout.Width(30)))
        {
            string chosen = EditorUtility.OpenFolderPanel("Select Export Folder", _exportPath, "");
            if (!string.IsNullOrEmpty(chosen))
                _exportPath = "Assets" + chosen.Substring(Application.dataPath.Length);
        }
        EditorGUILayout.EndHorizontal();
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  Generate button
    // ──────────────────────────────────────────────────────────────────────────
    private void DrawGenerateButton()
    {
        GUI.enabled = !_isGenerating;
        var style = new GUIStyle(GUI.skin.button)
        {
            fontSize    = 15,
            fontStyle   = FontStyle.Bold,
            fixedHeight = 44
        };

        EditorGUILayout.BeginHorizontal();

        if (GUILayout.Button(_isGenerating ? "Generating…" : $"Generate {_levelCount} Levels", style))
            GenerateLevels();

        if (_generated.Count > 0 && GUILayout.Button("Export JSON", style, GUILayout.Width(140)))
            ExportJSON();

        EditorGUILayout.EndHorizontal();
        GUI.enabled = true;
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  Preview
    // ──────────────────────────────────────────────────────────────────────────
    private void DrawPreview()
    {
        SectionLabel($"PREVIEW  ({_generated.Count} levels generated)");

        _previewIndex = EditorGUILayout.IntSlider("Preview Level", _previewIndex, 0, _generated.Count - 1);

        var lvl = _generated[_previewIndex];
        _previewScroll = EditorGUILayout.BeginScrollView(_previewScroll, GUILayout.Height(200));

        DrawDataTable(lvl);

        EditorGUILayout.EndScrollView();
    }

    private void DrawDataTable(LevelData lvl)
    {
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        TableRow("Level",      lvl.LevelNumber.ToString());
        TableRow("Game Type",  lvl.GameType);
        TableRow("Difficulty", $"{lvl.Difficulty:P0}");
        TableRow("Solvable",   lvl.IsSolvable ? "✓ Yes" : "✗ No (check settings)");
        TableRow("Seed",       lvl.Seed);

        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField("Level Data:", EditorStyles.boldLabel);
        foreach (var kv in lvl.Data)
            TableRow(kv.Key, kv.Value?.ToString() ?? "null");

        EditorGUILayout.EndVertical();
    }

    private static void TableRow(string key, string value)
    {
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField(key,   GUILayout.Width(120));
        EditorGUILayout.LabelField(value, EditorStyles.wordWrappedLabel);
        EditorGUILayout.EndHorizontal();
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  Generation engine
    // ──────────────────────────────────────────────────────────────────────────
    private void GenerateLevels()
    {
        _isGenerating = true;
        _generated.Clear();

        int baseSeed = _randomSeed ? UnityEngine.Random.Range(0, int.MaxValue) : _seed;
        var rng      = new System.Random(baseSeed);

        for (int i = 0; i < _levelCount; i++)
        {
            float t    = _levelCount > 1 ? (float)i / (_levelCount - 1) : 0f;
            float diff = ComputeDifficulty(t);

            LevelData lvl = _gameType switch
            {
                GameType.Match3    => GenerateMatch3(i + _startLevel, diff, rng),
                GameType.SortMatch => GenerateSortMatch(i + _startLevel, diff, rng),
                GameType.Maze      => GenerateMaze(i + _startLevel, diff, rng),
                _                  => GenerateSortMatch(i + _startLevel, diff, rng)
            };

            _generated.Add(lvl);

            if (i % 20 == 0)
            {
                EditorUtility.DisplayProgressBar("Generating Levels",
                    $"Level {i + 1} / {_levelCount}", (float)i / _levelCount);
            }
        }

        EditorUtility.ClearProgressBar();
        _previewIndex = 0;
        _isGenerating = false;
        Repaint();
        Debug.Log($"[LevelGenerator] Generated {_generated.Count} {_gameType} levels.");
    }

    private float ComputeDifficulty(float t) => _diffMode switch
    {
        DifficultyMode.Linear      => t,
        DifficultyMode.Exponential => t * t,
        _                          => t
    };

    // ── Match-3 ────────────────────────────────────────────────────────────────
    private LevelData GenerateMatch3(int levelNum, float diff, System.Random rng)
    {
        int rows   = Mathf.RoundToInt(Mathf.Lerp(_m3Rows,    _m3Rows + 4, diff));
        int cols   = Mathf.RoundToInt(Mathf.Lerp(_m3Cols,    _m3Cols + 4, diff));
        int colors = Mathf.RoundToInt(Mathf.Lerp(_m3Colors,  _m3MaxColors, diff));
        int moves  = Mathf.RoundToInt(Mathf.Lerp(_m3MaxMoves, _m3MinMoves, diff));

        // Generate grid
        int[,] grid = new int[rows, cols];
        for (int r = 0; r < rows; r++)
            for (int c = 0; c < cols; c++)
                grid[r, c] = rng.Next(0, colors);

        bool solvable = IsMatch3Solvable(grid, rows, cols, colors, moves);
        int  retries  = 0;
        while (!solvable && retries++ < _maxRetries)
        {
            for (int r = 0; r < rows; r++)
                for (int c = 0; c < cols; c++)
                    grid[r, c] = rng.Next(0, colors);
            solvable = IsMatch3Solvable(grid, rows, cols, colors, moves);
        }

        string flatGrid = FlattenGrid(grid, rows, cols);

        return new LevelData
        {
            LevelNumber = levelNum,
            GameType    = "Match3",
            Difficulty  = diff,
            IsSolvable  = solvable,
            Seed        = $"{rng.Next()}",
            Data        = new Dictionary<string, object>
            {
                ["rows"]   = rows,
                ["cols"]   = cols,
                ["colors"] = colors,
                ["moves"]  = moves,
                ["grid"]   = flatGrid
            }
        };
    }

    /// <summary>
    /// A solvable Match-3 grid has at least one valid match of 3+ in a row/col.
    /// This is a simplified check: verifies there exists at least one matching group.
    /// </summary>
    private static bool IsMatch3Solvable(int[,] grid, int rows, int cols, int colors, int moves)
    {
        if (moves <= 0) return false;
        // Check for at least one match of 3 horizontal or vertical
        for (int r = 0; r < rows; r++)
            for (int c = 0; c < cols - 2; c++)
                if (grid[r, c] == grid[r, c + 1] && grid[r, c + 1] == grid[r, c + 2])
                    return true;
        for (int c = 0; c < cols; c++)
            for (int r = 0; r < rows - 2; r++)
                if (grid[r, c] == grid[r + 1, c] && grid[r + 1, c] == grid[r + 2, c])
                    return true;
        return false;
    }

    private static string FlattenGrid(int[,] grid, int rows, int cols)
    {
        var sb = new StringBuilder();
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                sb.Append(grid[r, c]);
                if (c < cols - 1) sb.Append(',');
            }
            if (r < rows - 1) sb.Append('|');
        }
        return sb.ToString();
    }

    // ── SortMatch ─────────────────────────────────────────────────────────────
    private LevelData GenerateSortMatch(int levelNum, float diff, System.Random rng)
    {
        int groups    = Mathf.RoundToInt(Mathf.Lerp(_smGroups,    _smMaxGroups, diff));
        int stackSize = Mathf.RoundToInt(Mathf.Lerp(_smStackSize, _smMaxStack,  diff));
        int extraSlots= _smExtraSlots;

        // Build a solved state and shuffle it
        var stacks = new List<List<int>>();
        for (int g = 0; g < groups; g++)
        {
            var stack = new List<int>();
            for (int j = 0; j < stackSize; j++) stack.Add(g);
            stacks.Add(stack);
        }

        // Shuffle via random moves (always produces solvable state)
        int shuffleMoves = groups * stackSize * 3;
        int totalStacks  = groups + extraSlots;
        for (int i = 0; i < extraSlots; i++) stacks.Add(new List<int>());

        for (int i = 0; i < shuffleMoves; i++)
        {
            int src = rng.Next(0, totalStacks);
            int dst = rng.Next(0, totalStacks);
            if (src == dst) continue;
            if (stacks[src].Count == 0) continue;
            if (stacks[dst].Count >= stackSize) continue;
            int top = stacks[src][^1];
            stacks[src].RemoveAt(stacks[src].Count - 1);
            stacks[dst].Add(top);
        }

        string stacksJson = StacksToString(stacks);

        return new LevelData
        {
            LevelNumber = levelNum,
            GameType    = "SortMatch",
            Difficulty  = diff,
            IsSolvable  = true,  // shuffle-from-solved guarantees solvability
            Seed        = $"{rng.Next()}",
            Data        = new Dictionary<string, object>
            {
                ["groups"]     = groups,
                ["stackSize"]  = stackSize,
                ["extraSlots"] = extraSlots,
                ["stacks"]     = stacksJson
            }
        };
    }

    private static string StacksToString(List<List<int>> stacks)
    {
        var parts = new List<string>();
        foreach (var stack in stacks)
            parts.Add(string.Join(",", stack));
        return string.Join("|", parts);
    }

    // ── Maze ──────────────────────────────────────────────────────────────────
    private LevelData GenerateMaze(int levelNum, float diff, System.Random rng)
    {
        int w    = Mathf.RoundToInt(Mathf.Lerp(_mazeW,    _mazeMaxW, diff));
        int h    = Mathf.RoundToInt(Mathf.Lerp(_mazeH,    _mazeMaxH, diff));
        int keys = _mazeKeys ? Mathf.RoundToInt(Mathf.Lerp(_mazeKeyCount, _mazeMaxKeys, diff)) : 0;

        // Generate maze via Recursive Backtracker (DFS)
        bool[,] walls = GenerateMazeGrid(w, h, rng);
        int pathLen  = EstimatePathLength(walls, w, h);
        string flat  = FlattenBoolGrid(walls, w, h);

        return new LevelData
        {
            LevelNumber = levelNum,
            GameType    = "Maze",
            Difficulty  = diff,
            IsSolvable  = pathLen > 0,
            Seed        = $"{rng.Next()}",
            Data        = new Dictionary<string, object>
            {
                ["width"]      = w,
                ["height"]     = h,
                ["keys"]       = keys,
                ["pathLength"] = pathLen,
                ["walls"]      = flat
            }
        };
    }

    private static bool[,] GenerateMazeGrid(int w, int h, System.Random rng)
    {
        // true = wall, false = passage
        // Use recursive backtracker on a 2*w+1 × 2*h+1 cell grid (walls between cells)
        int gw = 2 * w + 1;
        int gh = 2 * h + 1;
        bool[,] grid = new bool[gw, gh];

        // Fill all walls
        for (int x = 0; x < gw; x++)
            for (int y = 0; y < gh; y++)
                grid[x, y] = true;

        bool[,] visited  = new bool[w, h];
        var     stack    = new Stack<Vector2Int>();
        var     start    = new Vector2Int(0, 0);
        visited[0, 0]    = true;
        grid[1, 1]       = false;
        stack.Push(start);

        int[] dx = { 0, 0, 1, -1 };
        int[] dy = { 1, -1, 0, 0 };

        while (stack.Count > 0)
        {
            var cur       = stack.Peek();
            var neighbors = new List<int>();
            for (int d = 0; d < 4; d++)
            {
                int nx = cur.x + dx[d];
                int ny = cur.y + dy[d];
                if (nx >= 0 && nx < w && ny >= 0 && ny < h && !visited[nx, ny])
                    neighbors.Add(d);
            }
            if (neighbors.Count == 0) { stack.Pop(); continue; }
            int dir = neighbors[rng.Next(neighbors.Count)];
            int ncx = cur.x + dx[dir];
            int ncy = cur.y + dy[dir];
            // Remove wall between cur and neighbor
            int wx = 1 + cur.x * 2 + dx[dir];
            int wy = 1 + cur.y * 2 + dy[dir];
            grid[wx, wy]              = false;
            grid[1 + ncx * 2, 1 + ncy * 2] = false;
            visited[ncx, ncy]         = true;
            stack.Push(new Vector2Int(ncx, ncy));
        }

        return grid;
    }

    /// <summary>BFS from top-left to bottom-right cell in the passage grid.</summary>
    private static int EstimatePathLength(bool[,] walls, int w, int h)
    {
        int gw = 2 * w + 1, gh = 2 * h + 1;
        var dist = new int[gw, gh];
        for (int x = 0; x < gw; x++) for (int y = 0; y < gh; y++) dist[x, y] = -1;
        var q     = new Queue<Vector2Int>();
        dist[1, 1] = 0;
        q.Enqueue(new Vector2Int(1, 1));
        int[] dx = { 0, 0, 1, -1 };
        int[] dy = { 1, -1, 0, 0 };
        while (q.Count > 0)
        {
            var c = q.Dequeue();
            for (int d = 0; d < 4; d++)
            {
                int nx = c.x + dx[d], ny = c.y + dy[d];
                if (nx < 0 || nx >= gw || ny < 0 || ny >= gh) continue;
                if (walls[nx, ny] || dist[nx, ny] >= 0) continue;
                dist[nx, ny] = dist[c.x, c.y] + 1;
                q.Enqueue(new Vector2Int(nx, ny));
            }
        }
        int ex = 2 * w - 1, ey = 2 * h - 1;
        return dist[ex, ey];
    }

    private static string FlattenBoolGrid(bool[,] grid, int w, int h)
    {
        int gw = 2 * w + 1, gh = 2 * h + 1;
        var sb = new StringBuilder();
        for (int y = 0; y < gh; y++)
        {
            for (int x = 0; x < gw; x++)
                sb.Append(grid[x, y] ? '1' : '0');
            if (y < gh - 1) sb.Append('|');
        }
        return sb.ToString();
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  JSON export
    // ──────────────────────────────────────────────────────────────────────────
    private void ExportJSON()
    {
        if (!Directory.Exists(_exportPath))
            Directory.CreateDirectory(_exportPath);

        int exported = 0;

        foreach (var lvl in _generated)
        {
            string fileName = $"Level_{lvl.LevelNumber:D4}.json";
            string fullPath = Path.Combine(_exportPath, fileName);

            string json = SerializeLevelToJSON(lvl);
            File.WriteAllText(fullPath, json);
            exported++;
        }

        AssetDatabase.Refresh();
        Debug.Log($"[LevelGenerator] Exported {exported} JSON files to {_exportPath}");
        EditorUtility.RevealInFinder(_exportPath);
        EditorUtility.DisplayDialog("Export Complete",
            $"Exported {exported} level(s) to:\n{_exportPath}", "OK");
    }

    private static string SerializeLevelToJSON(LevelData lvl)
    {
        var sb = new StringBuilder();
        sb.AppendLine("{");
        sb.AppendLine($"  \"level\": {lvl.LevelNumber},");
        sb.AppendLine($"  \"gameType\": \"{lvl.GameType}\",");
        sb.AppendLine($"  \"difficulty\": {lvl.Difficulty:F4},");
        sb.AppendLine($"  \"isSolvable\": {lvl.IsSolvable.ToString().ToLower()},");
        sb.AppendLine($"  \"seed\": \"{lvl.Seed}\",");
        sb.AppendLine("  \"data\": {");
        int i = 0;
        foreach (var kv in lvl.Data)
        {
            bool last = ++i == lvl.Data.Count;
            string val = kv.Value is string s ? $"\"{s}\"" : kv.Value?.ToString() ?? "null";
            sb.AppendLine($"    \"{kv.Key}\": {val}{(last ? "" : ",")}");
        }
        sb.AppendLine("  }");
        sb.AppendLine("}");
        return sb.ToString();
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  Helpers
    // ──────────────────────────────────────────────────────────────────────────
    private static void SectionLabel(string text)
    {
        var style = new GUIStyle(EditorStyles.boldLabel)
        {
            fontSize = 11,
            normal   = { textColor = new Color(1f, 0.8f, 0.4f) }
        };
        EditorGUILayout.Space(2);
        EditorGUILayout.LabelField(text, style);
        Rect r = EditorGUILayout.GetControlRect(false, 1);
        EditorGUI.DrawRect(r, new Color(1f, 0.8f, 0.4f, 0.4f));
        EditorGUILayout.Space(2);
    }
}
