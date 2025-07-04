using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System;
using System.Threading.Tasks;
using Python.Runtime;

namespace UnityAIAgent.Editor
{
    public class AIAgentWindow : EditorWindow
    {
        private string userInput = "";
        private List<ChatMessage> messages = new List<ChatMessage>();
        private Vector2 scrollPosition;
        private bool isProcessing = false;
        private string currentStreamText = "";
        private GUIStyle userMessageStyle;
        private GUIStyle aiMessageStyle;
        private GUIStyle codeStyle;
        private GUIStyle headerStyle;
        private StreamingHandler streamingHandler;
        private bool autoScroll = true;

        [MenuItem("Window/AI助手/聊天")]
        public static void ShowWindow()
        {
            var window = GetWindow<AIAgentWindow>("AI智能助手");
            window.minSize = new Vector2(450, 600);
        }

        private void OnEnable()
        {
            LoadChatHistory();
            InitializeStyles();
            
            // Initialize streaming handler
            if (streamingHandler == null)
            {
                streamingHandler = new StreamingHandler();
                streamingHandler.OnChunkReceived += OnStreamChunkReceived;
                streamingHandler.OnStreamCompleted += OnStreamComplete;
                streamingHandler.OnStreamError += OnStreamError;
            }
            
            // Ensure Python is initialized
            EditorApplication.delayCall += () => {
                PythonManager.EnsureInitialized();
            };
        }

        private void OnDisable()
        {
            SaveChatHistory();
            
            // 清理事件订阅
            if (streamingHandler != null)
            {
                streamingHandler.OnChunkReceived -= OnStreamChunkReceived;
                streamingHandler.OnStreamCompleted -= OnStreamComplete;
                streamingHandler.OnStreamError -= OnStreamError;
            }
        }

        private void InitializeStyles()
        {
            // Styles will be initialized in OnGUI when skin is available
        }

        private void OnGUI()
        {
            // Initialize styles if needed
            if (userMessageStyle == null)
            {
                userMessageStyle = new GUIStyle(EditorStyles.wordWrappedLabel);
                userMessageStyle.normal.background = MakeColorTexture(new Color(0.2f, 0.3f, 0.4f, 0.3f));
                userMessageStyle.padding = new RectOffset(10, 10, 10, 10);
                userMessageStyle.margin = new RectOffset(50, 10, 5, 5);

                aiMessageStyle = new GUIStyle(EditorStyles.wordWrappedLabel);
                aiMessageStyle.normal.background = MakeColorTexture(new Color(0.1f, 0.2f, 0.3f, 0.3f));
                aiMessageStyle.padding = new RectOffset(10, 10, 10, 10);
                aiMessageStyle.margin = new RectOffset(10, 50, 5, 5);

                codeStyle = new GUIStyle(EditorStyles.textArea);
                codeStyle.font = Font.CreateDynamicFontFromOSFont("Courier New", 12);
                codeStyle.normal.background = MakeColorTexture(new Color(0.1f, 0.1f, 0.1f, 0.5f));
                codeStyle.padding = new RectOffset(10, 10, 10, 10);
            }

            // Header
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            GUILayout.Label("Unity AI Assistant", EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();
            
            if (GUILayout.Button("Clear", EditorStyles.toolbarButton, GUILayout.Width(50)))
            {
                messages.Clear();
                SaveChatHistory();
            }
            
            if (GUILayout.Button("Logs", EditorStyles.toolbarButton, GUILayout.Width(50)))
            {
                LogWindow.ShowWindow();
            }
            
            EditorGUILayout.EndHorizontal();

            // Chat messages area
            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition, GUILayout.ExpandHeight(true));
            
            foreach (var message in messages)
            {
                DrawMessage(message);
            }

            // 显示当前流式消息（如果激活）
            if (streamingHandler != null && streamingHandler.IsStreaming && !string.IsNullOrEmpty(currentStreamText))
            {
                var streamMessage = new ChatMessage
                {
                    content = currentStreamText + "▌",
                    isUser = false,
                    timestamp = DateTime.Now
                };
                DrawMessage(streamMessage);
            }

            EditorGUILayout.EndScrollView();

            // Input area
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            
            GUI.enabled = !isProcessing;
            userInput = EditorGUILayout.TextArea(userInput, GUILayout.MinHeight(60));
            
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            
            if (streamingHandler != null && streamingHandler.IsStreaming)
            {
                if (GUILayout.Button("停止", GUILayout.Width(100), GUILayout.Height(30)))
                {
                    streamingHandler.StopStreaming();
                }
            }
            else
            {
                GUI.enabled = !isProcessing && !string.IsNullOrWhiteSpace(userInput);
                if (GUILayout.Button("发送", GUILayout.Width(100), GUILayout.Height(30)) || 
                    (Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Return && Event.current.control))
                {
                    SendMessage();
                }
            }
            
            GUI.enabled = true;
            EditorGUILayout.EndHorizontal();
            
            EditorGUILayout.EndVertical();

            // 状态栏
            if (isProcessing)
            {
                EditorGUILayout.HelpBox("🤔 AI正在思考...", MessageType.Info);
            }
            else if (!PythonManager.IsInitialized)
            {
                EditorGUILayout.HelpBox("⚠️ 请先进行设置", MessageType.Warning);
            }
            
            // 自动滚动到底部
            if (autoScroll && Event.current.type == EventType.Repaint)
            {
                scrollPosition.y = float.MaxValue;
            }
        }

        private void DrawMessage(ChatMessage message)
        {
            var style = message.isUser ? userMessageStyle : aiMessageStyle;
            
            EditorGUILayout.BeginVertical(style);
            
            // 头部
            EditorGUILayout.BeginHorizontal();
            string userLabel = message.isUser ? "😊 您" : "🤖 AI";
            GUILayout.Label(userLabel, EditorStyles.boldLabel, GUILayout.Width(50));
            GUILayout.FlexibleSpace();
            GUILayout.Label(message.timestamp.ToString("HH:mm"), EditorStyles.miniLabel);
            EditorGUILayout.EndHorizontal();
            
            // 内容
            if (message.content.Contains("```"))
            {
                // 特殊渲染代码块
                RenderMarkdownContent(message.content);
            }
            else
            {
                GUILayout.Label(message.content, EditorStyles.wordWrappedLabel);
            }
            
            // 操作按钮
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("📋 复制", EditorStyles.miniButton, GUILayout.Width(60)))
            {
                EditorGUIUtility.systemCopyBuffer = message.content;
                Debug.Log("已复制到剪贴板");
            }
            EditorGUILayout.EndHorizontal();
            
            EditorGUILayout.EndVertical();
            GUILayout.Space(5);
        }

        private void RenderMarkdownContent(string content)
        {
            var parts = content.Split(new[] { "```" }, StringSplitOptions.None);
            
            for (int i = 0; i < parts.Length; i++)
            {
                if (i % 2 == 0)
                {
                    // Regular text
                    if (!string.IsNullOrWhiteSpace(parts[i]))
                    {
                        GUILayout.Label(parts[i].Trim(), EditorStyles.wordWrappedLabel);
                    }
                }
                else
                {
                    // Code block
                    var lines = parts[i].Split('\n');
                    var language = lines.Length > 0 ? lines[0].Trim() : "";
                    var code = string.Join("\n", lines, 1, lines.Length - 1);
                    
                    if (!string.IsNullOrWhiteSpace(code))
                    {
                        EditorGUILayout.TextArea(code, codeStyle);
                    }
                }
            }
        }

        private async void SendMessage()
        {
            if (string.IsNullOrWhiteSpace(userInput)) return;

            var message = new ChatMessage
            {
                content = userInput.Trim(),
                isUser = true,
                timestamp = DateTime.Now
            };

            messages.Add(message);
            var currentInput = userInput;
            userInput = "";
            isProcessing = true;
            
            Repaint();

            try
            {
                // 开始流式响应
                await streamingHandler.StartStreaming(currentInput);
            }
            catch (Exception e)
            {
                messages.Add(new ChatMessage
                {
                    content = $"错误: {e.Message}",
                    isUser = false,
                    timestamp = DateTime.Now
                });
                Debug.LogError($"AI助手错误: {e}");
                isProcessing = false;
            }
            finally
            {
                SaveChatHistory();
                Repaint();
            }
        }


        private void LoadChatHistory()
        {
            var path = GetChatHistoryPath();
            if (System.IO.File.Exists(path))
            {
                try
                {
                    var json = System.IO.File.ReadAllText(path);
                    var wrapper = JsonUtility.FromJson<ChatHistoryWrapper>(json);
                    messages = wrapper.messages ?? new List<ChatMessage>();
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"Failed to load chat history: {e.Message}");
                }
            }
        }

        private void SaveChatHistory()
        {
            try
            {
                var path = GetChatHistoryPath();
                var dir = System.IO.Path.GetDirectoryName(path);
                if (!System.IO.Directory.Exists(dir))
                {
                    System.IO.Directory.CreateDirectory(dir);
                }

                var wrapper = new ChatHistoryWrapper { messages = messages };
                var json = JsonUtility.ToJson(wrapper, true);
                System.IO.File.WriteAllText(path, json);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"Failed to save chat history: {e.Message}");
            }
        }

        private string GetChatHistoryPath()
        {
            var documentsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            return System.IO.Path.Combine(documentsPath, "UnityAIAgent", "chat_history.json");
        }

        private Texture2D MakeColorTexture(Color color)
        {
            var texture = new Texture2D(1, 1);
            texture.SetPixel(0, 0, color);
            texture.Apply();
            return texture;
        }

        [Serializable]
        private class ChatMessage
        {
            public string content;
            public bool isUser;
            public DateTime timestamp;
        }

        [Serializable]
        private class ChatHistoryWrapper
        {
            public List<ChatMessage> messages;
        }
        
        // 流式响应回调方法
        private void OnStreamChunkReceived(string chunk)
        {
            currentStreamText += chunk;
            EditorApplication.delayCall += () => {
                if (this != null)
                    Repaint();
            };
        }
        
        private void OnStreamComplete()
        {
            if (!string.IsNullOrEmpty(currentStreamText))
            {
                messages.Add(new ChatMessage
                {
                    content = currentStreamText,
                    isUser = false,
                    timestamp = DateTime.Now
                });
            }
            
            currentStreamText = "";
            isProcessing = false;
            
            EditorApplication.delayCall += () => {
                if (this != null)
                    Repaint();
            };
        }
        
        private void OnStreamError(string error)
        {
            messages.Add(new ChatMessage
            {
                content = $"流式处理错误: {error}",
                isUser = false,
                timestamp = DateTime.Now
            });
            
            currentStreamText = "";
            isProcessing = false;
            
            EditorApplication.delayCall += () => {
                if (this != null)
                    Repaint();
            };
        }

        [Serializable]
        private class StreamChunk
        {
            public string type;
            public string content;
            public string error;
            public bool done;
        }

    }
}