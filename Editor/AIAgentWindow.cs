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
        private int currentStreamingMessageIndex = -1; // 当前流式消息在列表中的索引
        private bool scrollToBottom = false; // 是否需要滚动到底部
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
            Event e = Event.current;
            
            // 处理鼠标滚轮事件
            if (e.type == EventType.ScrollWheel)
            {
                scrollPosition.y += e.delta.y * 20;
                e.Use();
                Repaint();
            }
            
            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition, GUILayout.ExpandHeight(true));
            
            foreach (var message in messages)
            {
                DrawMessage(message);
            }

            // 流式消息现在已经包含在messages中，不需要单独显示
            
            // 自动滚动到底部
            if (scrollToBottom)
            {
                EditorApplication.delayCall += () => {
                    scrollPosition.y = float.MaxValue;
                    scrollToBottom = false;
                    Repaint();
                };
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
                string statusText = "🤔 AI正在思考...";
                if (streamingHandler != null && streamingHandler.IsStreaming)
                {
                    statusText = "📡 正在接收响应... (如果长时间无响应，系统将自动超时)";
                }
                EditorGUILayout.HelpBox(statusText, MessageType.Info);
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
            
            // 内容 - 统一使用Markdown渲染
            RenderMarkdownContent(message.content);
            
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
                    // 正常文本 - 进行进一步Markdown解析
                    if (!string.IsNullOrWhiteSpace(parts[i]))
                    {
                        RenderTextWithMarkdown(parts[i].Trim());
                    }
                }
                else
                {
                    // 代码块
                    var lines = parts[i].Split('\n');
                    var language = lines.Length > 0 ? lines[0].Trim() : "";
                    var code = string.Join("\n", lines, 1, lines.Length - 1);
                    
                    if (!string.IsNullOrWhiteSpace(code))
                    {
                        // 显示语言标签
                        if (!string.IsNullOrEmpty(language))
                        {
                            var langStyle = new GUIStyle(EditorStyles.miniLabel)
                            {
                                normal = { textColor = Color.gray }
                            };
                            GUILayout.Label($"[{language}]", langStyle);
                        }
                        
                        // 代码块背景
                        var rect = EditorGUILayout.GetControlRect(false, EditorStyles.textArea.CalcHeight(new GUIContent(code), Screen.width - 40));
                        GUI.Box(rect, "", codeStyle);
                        GUI.Label(rect, code, codeStyle);
                    }
                }
            }
        }
        
        private void RenderTextWithMarkdown(string text)
        {
            var lines = text.Split('\n');
            
            foreach (var line in lines)
            {
                if (string.IsNullOrEmpty(line))
                {
                    GUILayout.Space(3);
                    continue;
                }
                
                // 工具调用处理 - 美化显示
                if (line.StartsWith("Tool #"))
                {
                    // 提取工具编号和名称
                    var match = System.Text.RegularExpressions.Regex.Match(line, @"Tool #(\d+): (.+)");
                    if (match.Success)
                    {
                        var toolNumber = match.Groups[1].Value;
                        var toolName = match.Groups[2].Value;
                        
                        // 创建工具调用样式
                        EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
                        
                        // 工具图标
                        var iconStyle = new GUIStyle(EditorStyles.label)
                        {
                            fontSize = 14,
                            normal = { textColor = new Color(0.5f, 0.8f, 1f) }
                        };
                        GUILayout.Label("🔧", iconStyle, GUILayout.Width(25));
                        
                        // 工具信息
                        var toolStyle = new GUIStyle(EditorStyles.label)
                        {
                            fontStyle = FontStyle.Bold,
                            normal = { textColor = new Color(0.6f, 0.9f, 1f) }
                        };
                        GUILayout.Label($"调用工具 #{toolNumber}: {toolName}", toolStyle);
                        
                        EditorGUILayout.EndHorizontal();
                        GUILayout.Space(3);
                    }
                    else
                    {
                        GUILayout.Label(line, EditorStyles.wordWrappedLabel);
                    }
                }
                // 标题处理
                else if (line.StartsWith("### "))
                {
                    var headerStyle = new GUIStyle(EditorStyles.boldLabel)
                    {
                        fontSize = 13,
                        wordWrap = true,
                        normal = { textColor = new Color(0.8f, 0.8f, 1f) }
                    };
                    GUILayout.Label(line.Substring(4), headerStyle);
                    GUILayout.Space(2);
                }
                else if (line.StartsWith("## "))
                {
                    var headerStyle = new GUIStyle(EditorStyles.boldLabel)
                    {
                        fontSize = 15,
                        wordWrap = true,
                        normal = { textColor = new Color(0.8f, 0.8f, 1f) }
                    };
                    GUILayout.Label(line.Substring(3), headerStyle);
                    GUILayout.Space(3);
                }
                else if (line.StartsWith("# "))
                {
                    var headerStyle = new GUIStyle(EditorStyles.boldLabel)
                    {
                        fontSize = 17,
                        wordWrap = true,
                        normal = { textColor = new Color(0.8f, 0.8f, 1f) }
                    };
                    GUILayout.Label(line.Substring(2), headerStyle);
                    GUILayout.Space(4);
                }
                // 列表项处理
                else if (line.Trim().StartsWith("- ") || line.Trim().StartsWith("* "))
                {
                    var listStyle = new GUIStyle(EditorStyles.wordWrappedLabel);
                    GUILayout.BeginHorizontal();
                    GUILayout.Space(15);
                    GUILayout.Label("•", listStyle, GUILayout.Width(10));
                    GUILayout.Label(line.Trim().Substring(2), listStyle);
                    GUILayout.EndHorizontal();
                }
                // 数字列表处理
                else if (System.Text.RegularExpressions.Regex.IsMatch(line.Trim(), @"^\d+\. "))
                {
                    var listStyle = new GUIStyle(EditorStyles.wordWrappedLabel);
                    GUILayout.BeginHorizontal();
                    GUILayout.Space(15);
                    GUILayout.Label(line.Trim(), listStyle);
                    GUILayout.EndHorizontal();
                }
                // 粗体文本处理
                else if (line.Contains("**"))
                {
                    RenderBoldText(line);
                }
                // 普通文本
                else
                {
                    GUILayout.Label(line, EditorStyles.wordWrappedLabel);
                }
            }
        }
        
        private void RenderBoldText(string text)
        {
            // 简单的粗体文本处理
            var regex = new System.Text.RegularExpressions.Regex(@"\*\*(.*?)\*\*");
            var matches = regex.Matches(text);
            
            if (matches.Count > 0)
            {
                GUILayout.BeginHorizontal();
                
                int lastIndex = 0;
                foreach (System.Text.RegularExpressions.Match match in matches)
                {
                    // 添加前面的普通文本
                    if (match.Index > lastIndex)
                    {
                        string beforeText = text.Substring(lastIndex, match.Index - lastIndex);
                        if (!string.IsNullOrEmpty(beforeText))
                        {
                            GUILayout.Label(beforeText, EditorStyles.wordWrappedLabel, GUILayout.ExpandWidth(false));
                        }
                    }
                    
                    // 添加粗体文本
                    var boldStyle = new GUIStyle(EditorStyles.wordWrappedLabel)
                    {
                        fontStyle = FontStyle.Bold,
                        normal = { textColor = new Color(1f, 1f, 0.8f) } // 轻微高亮
                    };
                    GUILayout.Label(match.Groups[1].Value, boldStyle, GUILayout.ExpandWidth(false));
                    
                    lastIndex = match.Index + match.Length;
                }
                
                // 添加后面的普通文本
                if (lastIndex < text.Length)
                {
                    string afterText = text.Substring(lastIndex);
                    if (!string.IsNullOrEmpty(afterText))
                    {
                        GUILayout.Label(afterText, EditorStyles.wordWrappedLabel, GUILayout.ExpandWidth(false));
                    }
                }
                
                GUILayout.EndHorizontal();
            }
            else
            {
                GUILayout.Label(text, EditorStyles.wordWrappedLabel);
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
            
            // 重置流式状态
            currentStreamText = "";
            currentStreamingMessageIndex = -1;
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
                // 如果有错误，确保重置流式状态
                if (isProcessing)
                {
                    currentStreamText = "";
                    currentStreamingMessageIndex = -1;
                    isProcessing = false;
                }
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
            Debug.Log($"[AIAgentWindow] 接收到流式数据块: {chunk}");
            
            // 如果还没有创建流式消息，创建一个
            if (currentStreamingMessageIndex == -1)
            {
                messages.Add(new ChatMessage
                {
                    content = "",
                    isUser = false,
                    timestamp = DateTime.Now
                });
                currentStreamingMessageIndex = messages.Count - 1;
                Debug.Log($"[AIAgentWindow] 创建新的流式消息，索引: {currentStreamingMessageIndex}");
            }
            
            // 更新流式消息内容
            currentStreamText += chunk;
            if (currentStreamingMessageIndex >= 0 && currentStreamingMessageIndex < messages.Count)
            {
                messages[currentStreamingMessageIndex].content = currentStreamText + "▌"; // 添加光标
                Debug.Log($"[AIAgentWindow] 更新消息内容，当前长度: {currentStreamText.Length}");
            }
            
            // 确保在主线程中更新UI
            EditorApplication.delayCall += () => {
                if (this != null)
                {
                    // 自动滚动到底部
                    scrollToBottom = true;
                    Repaint();
                }
            };
        }
        
        private void OnStreamComplete()
        {
            Debug.Log("[AIAgentWindow] 流式响应完成");
            
            // 更新最终消息内容（移除光标）
            if (currentStreamingMessageIndex >= 0 && currentStreamingMessageIndex < messages.Count)
            {
                messages[currentStreamingMessageIndex].content = currentStreamText;
                Debug.Log($"[AIAgentWindow] 完成消息内容，最终长度: {currentStreamText.Length}");
            }
            
            // 重置流式状态
            currentStreamText = "";
            currentStreamingMessageIndex = -1;
            isProcessing = false;
            
            EditorApplication.delayCall += () => {
                if (this != null)
                {
                    SaveChatHistory();
                    Repaint();
                }
            };
        }
        
        private void OnStreamError(string error)
        {
            Debug.Log($"[AIAgentWindow] 流式响应错误: {error}");
            
            // 格式化错误消息
            string errorMessage = error;
            if (error.Contains("超时"))
            {
                errorMessage = $"⏱️ **响应超时**\n\n{error}\n\n💡 **建议**：\n- 尝试简化您的问题\n- 检查网络连接\n- 稍后再试";
            }
            else if (error.Contains("SSL") || error.Contains("certificate"))
            {
                errorMessage = $"🔒 **SSL连接错误**\n\n{error}\n\n💡 **建议**：\n- 检查网络连接\n- 更新系统证书\n- 检查防火墙设置";
            }
            else
            {
                errorMessage = $"❌ **处理错误**\n\n{error}";
            }
            
            // 如果正在流式处理，更新当前消息为错误信息
            if (currentStreamingMessageIndex >= 0 && currentStreamingMessageIndex < messages.Count)
            {
                messages[currentStreamingMessageIndex].content = errorMessage;
            }
            else
            {
                // 否则添加新的错误消息
                messages.Add(new ChatMessage
                {
                    content = errorMessage,
                    isUser = false,
                    timestamp = DateTime.Now
                });
            }
            
            // 重置流式状态
            currentStreamText = "";
            currentStreamingMessageIndex = -1;
            isProcessing = false;
            
            EditorApplication.delayCall += () => {
                if (this != null)
                {
                    SaveChatHistory();
                    Repaint();
                }
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