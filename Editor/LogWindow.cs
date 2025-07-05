using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System;

namespace UnityAIAgent.Editor
{
    public class LogWindow : EditorWindow
    {
        private List<LogEntry> logs = new List<LogEntry>();
        private Vector2 scrollPosition;
        private LogLevel filterLevel = LogLevel.All;
        private bool autoScroll = true;
        private string searchFilter = "";

        // [MenuItem("Window/AI Assistant/Logs")]
        public static void ShowWindow()
        {
            var window = GetWindow<LogWindow>("AI Assistant Logs");
            window.minSize = new Vector2(600, 400);
        }

        private void OnEnable()
        {
            // Subscribe to Unity log events
            Application.logMessageReceived += HandleLog;
            
            // Subscribe to Python log events
            PythonLogger.OnLogReceived += HandlePythonLog;
        }

        private void OnDisable()
        {
            Application.logMessageReceived -= HandleLog;
            PythonLogger.OnLogReceived -= HandlePythonLog;
        }

        private void OnGUI()
        {
            DrawToolbar();
            DrawLogList();
        }

        private void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

            // Filter dropdown
            filterLevel = (LogLevel)EditorGUILayout.EnumPopup(filterLevel, EditorStyles.toolbarDropDown, GUILayout.Width(100));

            // Search field
            searchFilter = EditorGUILayout.TextField(searchFilter, EditorStyles.toolbarSearchField, GUILayout.ExpandWidth(true));

            // Auto scroll toggle
            autoScroll = GUILayout.Toggle(autoScroll, "Auto Scroll", EditorStyles.toolbarButton, GUILayout.Width(80));

            // Clear button
            if (GUILayout.Button("Clear", EditorStyles.toolbarButton, GUILayout.Width(50)))
            {
                logs.Clear();
            }

            // Export button
            if (GUILayout.Button("Export", EditorStyles.toolbarButton, GUILayout.Width(50)))
            {
                ExportLogs();
            }

            EditorGUILayout.EndHorizontal();
        }

        private void DrawLogList()
        {
            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);

            foreach (var log in logs)
            {
                if (!ShouldShowLog(log)) continue;

                DrawLogEntry(log);
            }

            EditorGUILayout.EndScrollView();

            // Auto scroll to bottom
            if (autoScroll && Event.current.type == EventType.Repaint)
            {
                scrollPosition.y = float.MaxValue;
            }
        }

        private void DrawLogEntry(LogEntry entry)
        {
            var backgroundColor = GetLogColor(entry.level);
            var originalColor = GUI.backgroundColor;
            GUI.backgroundColor = backgroundColor;

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUI.backgroundColor = originalColor;

            // Header
            EditorGUILayout.BeginHorizontal();
            
            var icon = GetLogIcon(entry.level);
            if (icon != null)
            {
                GUILayout.Label(icon, GUILayout.Width(20), GUILayout.Height(20));
            }

            GUILayout.Label($"[{entry.timestamp:HH:mm:ss}]", EditorStyles.miniLabel, GUILayout.Width(60));
            GUILayout.Label(entry.source, EditorStyles.boldLabel, GUILayout.Width(80));
            GUILayout.Label(entry.level.ToString(), GetLogStyle(entry.level), GUILayout.Width(60));
            
            GUILayout.FlexibleSpace();

            if (GUILayout.Button("Copy", EditorStyles.miniButton, GUILayout.Width(40)))
            {
                EditorGUIUtility.systemCopyBuffer = entry.message;
            }

            EditorGUILayout.EndHorizontal();

            // Message
            EditorGUILayout.LabelField(entry.message, EditorStyles.wordWrappedLabel);

            // Stack trace (if available)
            if (!string.IsNullOrEmpty(entry.stackTrace))
            {
                if (GUILayout.Button("Show Stack Trace", EditorStyles.miniButton))
                {
                    entry.showStackTrace = !entry.showStackTrace;
                }

                if (entry.showStackTrace)
                {
                    EditorGUILayout.TextArea(entry.stackTrace, EditorStyles.textArea);
                }
            }

            EditorGUILayout.EndVertical();
            GUILayout.Space(2);
        }

        private bool ShouldShowLog(LogEntry entry)
        {
            // Filter by level
            if (filterLevel != LogLevel.All && entry.level != filterLevel)
                return false;

            // Filter by search
            if (!string.IsNullOrEmpty(searchFilter))
            {
                var searchLower = searchFilter.ToLower();
                return entry.message.ToLower().Contains(searchLower) ||
                       entry.source.ToLower().Contains(searchLower);
            }

            return true;
        }

        private void HandleLog(string logString, string stackTrace, LogType type)
        {
            var entry = new LogEntry
            {
                timestamp = DateTime.Now,
                source = "Unity",
                level = ConvertLogType(type),
                message = logString,
                stackTrace = stackTrace
            };

            logs.Add(entry);
            
            // Limit log count
            if (logs.Count > 1000)
            {
                logs.RemoveAt(0);
            }

            Repaint();
        }

        private void HandlePythonLog(string source, LogLevel level, string message, string stackTrace)
        {
            var entry = new LogEntry
            {
                timestamp = DateTime.Now,
                source = source,
                level = level,
                message = message,
                stackTrace = stackTrace
            };

            logs.Add(entry);

            // Limit log count
            if (logs.Count > 1000)
            {
                logs.RemoveAt(0);
            }

            Repaint();
        }

        private LogLevel ConvertLogType(LogType type)
        {
            switch (type)
            {
                case LogType.Error:
                case LogType.Exception:
                    return LogLevel.Error;
                case LogType.Warning:
                    return LogLevel.Warning;
                case LogType.Log:
                    return LogLevel.Info;
                default:
                    return LogLevel.Debug;
            }
        }

        private Color GetLogColor(LogLevel level)
        {
            switch (level)
            {
                case LogLevel.Error:
                    return new Color(1f, 0.3f, 0.3f, 0.3f);
                case LogLevel.Warning:
                    return new Color(1f, 0.8f, 0.3f, 0.3f);
                case LogLevel.Info:
                    return new Color(0.3f, 0.7f, 1f, 0.3f);
                case LogLevel.Debug:
                    return new Color(0.7f, 0.7f, 0.7f, 0.3f);
                default:
                    return Color.white;
            }
        }

        private GUIStyle GetLogStyle(LogLevel level)
        {
            var style = new GUIStyle(EditorStyles.label);
            
            switch (level)
            {
                case LogLevel.Error:
                    style.normal.textColor = Color.red;
                    break;
                case LogLevel.Warning:
                    style.normal.textColor = new Color(1f, 0.6f, 0f);
                    break;
                case LogLevel.Info:
                    style.normal.textColor = new Color(0.3f, 0.7f, 1f);
                    break;
                case LogLevel.Debug:
                    style.normal.textColor = Color.gray;
                    break;
            }

            return style;
        }

        private Texture2D GetLogIcon(LogLevel level)
        {
            switch (level)
            {
                case LogLevel.Error:
                    return EditorGUIUtility.IconContent("console.erroricon").image as Texture2D;
                case LogLevel.Warning:
                    return EditorGUIUtility.IconContent("console.warnicon").image as Texture2D;
                case LogLevel.Info:
                    return EditorGUIUtility.IconContent("console.infoicon").image as Texture2D;
                default:
                    return null;
            }
        }

        private void ExportLogs()
        {
            var path = EditorUtility.SaveFilePanel("Export Logs", "", "ai_assistant_logs.txt", "txt");
            if (!string.IsNullOrEmpty(path))
            {
                var content = new System.Text.StringBuilder();
                foreach (var log in logs)
                {
                    content.AppendLine($"[{log.timestamp:yyyy-MM-dd HH:mm:ss}] [{log.source}] [{log.level}] {log.message}");
                    if (!string.IsNullOrEmpty(log.stackTrace))
                    {
                        content.AppendLine(log.stackTrace);
                    }
                    content.AppendLine();
                }
                
                System.IO.File.WriteAllText(path, content.ToString());
                EditorUtility.DisplayDialog("Export Complete", $"Logs exported to:\n{path}", "OK");
            }
        }

        private class LogEntry
        {
            public DateTime timestamp;
            public string source;
            public LogLevel level;
            public string message;
            public string stackTrace;
            public bool showStackTrace;
        }

        public enum LogLevel
        {
            All,
            Debug,
            Info,
            Warning,
            Error
        }
    }

    // Static class for Python logging integration
    public static class PythonLogger
    {
        public static event Action<string, LogWindow.LogLevel, string, string> OnLogReceived;

        public static void Log(string source, LogWindow.LogLevel level, string message, string stackTrace = "")
        {
            OnLogReceived?.Invoke(source, level, message, stackTrace);
        }
    }
}