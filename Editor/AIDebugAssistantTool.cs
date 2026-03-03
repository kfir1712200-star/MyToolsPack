using UnityEngine;
using UnityEditor;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

/// <summary>
/// MyTools – AI Debug Assistant
/// Captures Unity Console errors and uses the OpenAI API to explain them
/// and suggest fixes, all without leaving the Unity Editor.
///
/// SETUP: Install "Unity Editor Coroutines" package via Package Manager
///        (com.unity.editorcoroutines), then enter your OpenAI API key
///        in the settings panel.
/// </summary>
public class AIDebugAssistantTool : EditorWindow
{
    // ──────────────────────────────────────────────────────────────────────────
    //  Constants
    // ──────────────────────────────────────────────────────────────────────────
    private const string ApiKeyPref     = "MyTools_AIDebug_ApiKey";
    private const string ModelPref      = "MyTools_AIDebug_Model";
    private const string EndpointUrl    = "https://api.openai.com/v1/chat/completions";
    private const int    MaxLogHistory  = 50;

    // ──────────────────────────────────────────────────────────────────────────
    //  State
    // ──────────────────────────────────────────────────────────────────────────
    private string _apiKey   = "";
    private string _model    = "gpt-4o-mini";
    private bool   _showSettings;

    // Log capture
    private List<LogEntry>  _capturedLogs   = new List<LogEntry>();
    private LogEntry        _selectedEntry  = null;
    private bool            _autoCapture    = true;
    private bool            _captureErrors  = true;
    private bool            _captureWarnings = false;
    private bool            _showOnlyUnread = false;

    // AI response
    private string _aiExplanation = "";
    private string _aiFix         = "";
    private string _aiRefactor    = "";
    private bool   _isQuerying    = false;
    private string _lastError     = "";

    // Layout
    private Vector2 _logScroll;
    private Vector2 _responseScroll;

    // ──────────────────────────────────────────────────────────────────────────
    //  Data types
    // ──────────────────────────────────────────────────────────────────────────
    private class LogEntry
    {
        public string    Message;
        public string    StackTrace;
        public LogType   Type;
        public DateTime  Time;
        public bool      Read;
        public string    AiResponse;
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  Lifecycle
    // ──────────────────────────────────────────────────────────────────────────
    [MenuItem("MyTools/AI Debug Assistant %#d")]
    public static void ShowWindow()
    {
        var w = GetWindow<AIDebugAssistantTool>("AI Debug Assistant");
        w.minSize = new Vector2(460, 600);
    }

    private void OnEnable()
    {
        _apiKey = EditorPrefs.GetString(ApiKeyPref, "");
        _model  = EditorPrefs.GetString(ModelPref,  "gpt-4o-mini");

        Application.logMessageReceived += OnLogMessageReceived;
    }

    private void OnDisable()
    {
        Application.logMessageReceived -= OnLogMessageReceived;
        EditorPrefs.SetString(ApiKeyPref, _apiKey);
        EditorPrefs.SetString(ModelPref,  _model);
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  Log capture callback
    // ──────────────────────────────────────────────────────────────────────────
    private void OnLogMessageReceived(string message, string stackTrace, LogType type)
    {
        if (!_autoCapture) return;
        if (type == LogType.Error   && !_captureErrors)   return;
        if (type == LogType.Warning && !_captureWarnings) return;
        if (type == LogType.Log) return;   // ignore plain Debug.Log

        var entry = new LogEntry
        {
            Message    = message,
            StackTrace = stackTrace,
            Type       = type,
            Time       = DateTime.Now
        };

        _capturedLogs.Add(entry);
        if (_capturedLogs.Count > MaxLogHistory)
            _capturedLogs.RemoveAt(0);

        Repaint();
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  GUI
    // ──────────────────────────────────────────────────────────────────────────
    private void OnGUI()
    {
        DrawHeader();
        DrawSettingsPanel();

        EditorGUILayout.Space(4);
        DrawCaptureBar();

        EditorGUILayout.Space(4);
        DrawLogList();

        if (_selectedEntry != null)
        {
            EditorGUILayout.Space(4);
            DrawSelectedEntry();
            EditorGUILayout.Space(4);
            DrawAIResponse();
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  Header
    // ──────────────────────────────────────────────────────────────────────────
    private void DrawHeader()
    {
        var rect = EditorGUILayout.GetControlRect(false, 48);
        EditorGUI.DrawRect(rect, new Color(0.35f, 0.1f, 0.55f));
        var style = new GUIStyle(EditorStyles.boldLabel)
        {
            fontSize  = 18,
            alignment = TextAnchor.MiddleCenter,
            normal    = { textColor = Color.white }
        };
        GUI.Label(rect, "AI Debug Assistant", style);
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  Settings panel
    // ──────────────────────────────────────────────────────────────────────────
    private void DrawSettingsPanel()
    {
        _showSettings = EditorGUILayout.Foldout(_showSettings, "Settings (API Key & Model)", true);
        if (!_showSettings) return;

        EditorGUI.indentLevel++;

        EditorGUILayout.HelpBox(
            "Your API key is stored in EditorPrefs (local machine only, not committed to source control).",
            MessageType.Info);

        EditorGUILayout.BeginHorizontal();
        _apiKey = EditorGUILayout.PasswordField("OpenAI API Key", _apiKey);
        if (GUILayout.Button("Save", GUILayout.Width(50)))
        {
            EditorPrefs.SetString(ApiKeyPref, _apiKey);
            Debug.Log("[AIDebug] API key saved to EditorPrefs.");
        }
        EditorGUILayout.EndHorizontal();

        _model = EditorGUILayout.TextField("Model (e.g. gpt-4o-mini)", _model);
        if (GUILayout.Button("Save Model"))
        {
            EditorPrefs.SetString(ModelPref, _model);
            Debug.Log($"[AIDebug] Model set to {_model}");
        }

        EditorGUI.indentLevel--;
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  Capture bar
    // ──────────────────────────────────────────────────────────────────────────
    private void DrawCaptureBar()
    {
        EditorGUILayout.BeginHorizontal();
        _autoCapture    = GUILayout.Toggle(_autoCapture,    "Auto-capture",  "Button", GUILayout.Width(100));
        _captureErrors  = GUILayout.Toggle(_captureErrors,  "Errors",        "Button", GUILayout.Width(70));
        _captureWarnings= GUILayout.Toggle(_captureWarnings,"Warnings",      "Button", GUILayout.Width(80));
        _showOnlyUnread = GUILayout.Toggle(_showOnlyUnread, "Unread only",   "Button", GUILayout.Width(90));
        GUILayout.FlexibleSpace();
        if (GUILayout.Button("Clear All", GUILayout.Width(80)))
        {
            _capturedLogs.Clear();
            _selectedEntry = null;
            ClearResponse();
        }
        EditorGUILayout.EndHorizontal();
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  Log list
    // ──────────────────────────────────────────────────────────────────────────
    private void DrawLogList()
    {
        SectionLabel("CAPTURED LOGS");

        var display = _showOnlyUnread
            ? _capturedLogs.FindAll(e => !e.Read)
            : _capturedLogs;

        _logScroll = EditorGUILayout.BeginScrollView(_logScroll, GUILayout.Height(180));

        if (display.Count == 0)
            EditorGUILayout.HelpBox("No logs captured yet. Trigger errors during Play Mode.", MessageType.None);

        for (int i = display.Count - 1; i >= 0; i--)
        {
            var entry = display[i];
            bool selected = entry == _selectedEntry;
            DrawLogRow(entry, selected);
        }

        EditorGUILayout.EndScrollView();
    }

    private void DrawLogRow(LogEntry entry, bool selected)
    {
        Color bg = entry.Type == LogType.Error
            ? (selected ? new Color(0.7f, 0.2f, 0.2f) : new Color(0.45f, 0.1f, 0.1f))
            : (selected ? new Color(0.6f, 0.55f, 0.1f) : new Color(0.4f, 0.35f, 0.05f));

        var style = new GUIStyle(EditorStyles.label)
        {
            normal   = { background = MakeTex(2, 2, bg), textColor = Color.white },
            wordWrap  = false,
            padding   = new RectOffset(4, 4, 2, 2)
        };

        string icon   = entry.Type == LogType.Error ? "✗" : "⚠";
        string unread = entry.Read ? "" : " ●";
        string label  = $"{icon}{unread}  [{entry.Time:HH:mm:ss}]  {Truncate(entry.Message, 70)}";

        if (GUILayout.Button(label, style))
        {
            _selectedEntry = entry;
            entry.Read     = true;
            ClearResponse();
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  Selected entry detail
    // ──────────────────────────────────────────────────────────────────────────
    private void DrawSelectedEntry()
    {
        SectionLabel("SELECTED ERROR");
        EditorGUILayout.TextArea(_selectedEntry.Message, EditorStyles.textArea, GUILayout.MaxHeight(60));

        if (!string.IsNullOrEmpty(_selectedEntry.StackTrace))
        {
            EditorGUILayout.LabelField("Stack Trace:", EditorStyles.miniBoldLabel);
            EditorGUILayout.TextArea(
                Truncate(_selectedEntry.StackTrace, 500),
                EditorStyles.miniTextField,
                GUILayout.MaxHeight(60));
        }

        EditorGUILayout.BeginHorizontal();
        GUI.enabled = !_isQuerying && !string.IsNullOrEmpty(_apiKey);

        if (GUILayout.Button(_isQuerying ? "Asking AI…" : "Explain & Fix", GUILayout.Height(32)))
            _ = QueryOpenAI(_selectedEntry.Message, _selectedEntry.StackTrace, "explain_and_fix");

        if (GUILayout.Button("Suggest Refactor", GUILayout.Height(32)))
            _ = QueryOpenAI(_selectedEntry.Message, _selectedEntry.StackTrace, "refactor");

        GUI.enabled = true;
        EditorGUILayout.EndHorizontal();

        if (string.IsNullOrEmpty(_apiKey))
            EditorGUILayout.HelpBox("Enter your OpenAI API key in Settings above.", MessageType.Warning);
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  AI Response
    // ──────────────────────────────────────────────────────────────────────────
    private void DrawAIResponse()
    {
        if (!string.IsNullOrEmpty(_lastError))
        {
            EditorGUILayout.HelpBox("API Error: " + _lastError, MessageType.Error);
        }

        if (string.IsNullOrEmpty(_aiExplanation) && string.IsNullOrEmpty(_aiFix) && string.IsNullOrEmpty(_aiRefactor))
            return;

        SectionLabel("AI RESPONSE");
        _responseScroll = EditorGUILayout.BeginScrollView(_responseScroll, GUILayout.MaxHeight(300));

        if (!string.IsNullOrEmpty(_aiExplanation))
        {
            ResponseBlock("Explanation", _aiExplanation, new Color(0.2f, 0.4f, 0.7f));
        }

        if (!string.IsNullOrEmpty(_aiFix))
        {
            EditorGUILayout.Space(4);
            ResponseBlock("Suggested Fix", _aiFix, new Color(0.15f, 0.55f, 0.25f));
        }

        if (!string.IsNullOrEmpty(_aiRefactor))
        {
            EditorGUILayout.Space(4);
            ResponseBlock("Refactor Suggestion", _aiRefactor, new Color(0.5f, 0.3f, 0.7f));
        }

        EditorGUILayout.EndScrollView();

        if (GUILayout.Button("Copy Response to Clipboard"))
            GUIUtility.systemCopyBuffer = _aiExplanation + "\n\n" + _aiFix + "\n\n" + _aiRefactor;
    }

    private void ResponseBlock(string title, string content, Color accent)
    {
        var headerRect = EditorGUILayout.GetControlRect(false, 20);
        EditorGUI.DrawRect(headerRect, accent);
        var hStyle = new GUIStyle(EditorStyles.boldLabel)
        {
            normal = { textColor = Color.white },
            padding = new RectOffset(4, 0, 0, 0)
        };
        GUI.Label(headerRect, title, hStyle);

        var areaStyle = new GUIStyle(EditorStyles.textArea)
        {
            wordWrap = true,
            fontSize = 11
        };
        EditorGUILayout.SelectableLabel(content, areaStyle,
            GUILayout.ExpandHeight(true),
            GUILayout.MinHeight(60));
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  OpenAI API query
    // ──────────────────────────────────────────────────────────────────────────
    private static readonly HttpClient _http = new HttpClient();

    private async Task QueryOpenAI(string errorMessage, string stackTrace, string mode)
    {
        _isQuerying = true;
        _lastError  = "";
        ClearResponse();
        Repaint();

        string prompt = BuildPrompt(errorMessage, stackTrace, mode);
        string body   = BuildRequestBody(prompt);

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, EndpointUrl);
            request.Headers.Add("Authorization", $"Bearer {_apiKey}");
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");

            using var response = await _http.SendAsync(request);
            string json = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
                _lastError = $"{(int)response.StatusCode} {response.ReasonPhrase}\n{json}";
            else
                ParseResponse(json, mode);
        }
        catch (Exception ex)
        {
            _lastError = ex.Message;
        }

        _isQuerying = false;
        Repaint();
    }

    private static string BuildPrompt(string error, string stack, string mode)
    {
        string context = $"Unity C# Error:\n{error}\n\nStack Trace:\n{stack}";

        return mode == "refactor"
            ? $"You are an expert Unity C# developer. A developer encountered the following error.\n\n{context}\n\n" +
              "Provide a concise refactoring suggestion that would make the code more robust and prevent this error class in the future."
            : $"You are an expert Unity C# developer. A developer encountered the following error.\n\n{context}\n\n" +
              "1. Briefly explain WHY this error occurs in 2–3 sentences.\n" +
              "2. Provide a clear, concrete code fix. Use a code block.";
    }

    private string BuildRequestBody(string prompt)
    {
        // Minimal JSON serialization to avoid requiring Newtonsoft.Json
        string escaped = prompt
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("\n", "\\n")
            .Replace("\r", "")
            .Replace("\t", "\\t");

        return $"{{\"model\":\"{_model}\",\"messages\":[{{\"role\":\"user\",\"content\":\"{escaped}\"}}],\"max_tokens\":800}}";
    }

    private void ParseResponse(string json, string mode)
    {
        // Extract content between "content":"  and the next unescaped "
        // Using a simple string scan to avoid requiring Newtonsoft.Json
        const string marker = "\"content\":\"";
        int start = json.IndexOf(marker, StringComparison.Ordinal);
        if (start < 0) { _lastError = "Could not parse AI response."; return; }

        start += marker.Length;
        var sb  = new StringBuilder();
        bool escaped = false;
        for (int i = start; i < json.Length; i++)
        {
            char c = json[i];
            if (escaped) { sb.Append(c == 'n' ? '\n' : c); escaped = false; continue; }
            if (c == '\\') { escaped = true; continue; }
            if (c == '"') break;
            sb.Append(c);
        }

        string content = sb.ToString().Trim();

        if (mode == "refactor")
        {
            _aiRefactor = content;
        }
        else
        {
            // Split on "2." for explanation vs fix
            int splitIdx = content.IndexOf("\n2.", StringComparison.Ordinal);
            if (splitIdx >= 0)
            {
                _aiExplanation = content[..splitIdx].Trim().TrimStart('1', '.', ' ');
                _aiFix         = content[(splitIdx + 3)..].Trim();
            }
            else
            {
                _aiExplanation = content;
            }
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  Helpers
    // ──────────────────────────────────────────────────────────────────────────
    private void ClearResponse()
    {
        _aiExplanation = "";
        _aiFix         = "";
        _aiRefactor    = "";
        _lastError     = "";
    }

    private static string Truncate(string s, int max) =>
        s == null ? "" : s.Length <= max ? s : s[..max] + "…";

    private static void SectionLabel(string text)
    {
        var style = new GUIStyle(EditorStyles.boldLabel)
        {
            fontSize = 11,
            normal   = { textColor = new Color(0.8f, 0.7f, 1f) }
        };
        EditorGUILayout.Space(2);
        EditorGUILayout.LabelField(text, style);
        Rect r = EditorGUILayout.GetControlRect(false, 1);
        EditorGUI.DrawRect(r, new Color(0.8f, 0.7f, 1f, 0.35f));
        EditorGUILayout.Space(2);
    }

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
