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
        private bool hasActiveStream = false; // 是否有活跃的流式响应
        private string currentStreamText = "";
        private int currentStreamingMessageIndex = -1; // 当前流式消息在列表中的索引
        private bool scrollToBottom = false; // 是否需要滚动到底部
        private GUIStyle userMessageStyle;
        private GUIStyle aiMessageStyle;
        private GUIStyle codeStyle;
        private GUIStyle headerStyle;
        private StreamingHandler streamingHandler;
        private bool autoScroll = true;
        private bool userScrolledUp = false;
        private float lastScrollPosition = 0f;
        
        // 折叠状态跟踪
        private Dictionary<string, bool> collapsedStates = new Dictionary<string, bool>();

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
            
            // 处理鼠标滚轮事件和触控板滚动
            if (e.type == EventType.ScrollWheel)
            {
                // 检测用户是否主动向上滚动
                if (e.delta.y < 0) // 向上滚动
                {
                    userScrolledUp = true;
                }
                else if (e.delta.y > 0) // 向下滚动
                {
                    // 检查是否已经滚动到底部附近
                    float maxScroll = Mathf.Max(0, GUI.skin.verticalScrollbar.CalcSize(new GUIContent("")).y);
                    if (scrollPosition.y >= maxScroll - 50) // 距离底部50像素内
                    {
                        userScrolledUp = false; // 重新启用自动滚动
                    }
                }
                
                // 应用滚动
                scrollPosition.y += e.delta.y * 20;
                scrollPosition.y = Mathf.Max(0, scrollPosition.y);
                
                e.Use();
                Repaint();
            }
            
            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition, GUILayout.ExpandHeight(true));
            
            foreach (var message in messages)
            {
                DrawMessage(message);
            }

            // 流式消息现在已经包含在messages中，不需要单独显示
            
            // 只有在用户没有主动向上滚动时才自动滚动到底部
            if (scrollToBottom && !userScrolledUp)
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
            
            // 只有在用户没有主动向上滚动时才自动滚动到底部
            if (autoScroll && !userScrolledUp && Event.current.type == EventType.Repaint)
            {
                scrollPosition.y = float.MaxValue;
            }
            
            // 记录滚动位置变化
            if (Event.current.type == EventType.Repaint)
            {
                lastScrollPosition = scrollPosition.y;
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
            // 首先处理HTML标签
            if (text.Contains("<details>") || text.Contains("<strong>") || text.Contains("<em>") || 
                text.Contains("<code>") || text.Contains("<pre>") || text.Contains("<blockquote>"))
            {
                RenderHtmlContent(text);
                return;
            }
            
            // 如果没有HTML标签，使用传统的Markdown渲染
            RenderMarkdownText(text);
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
        
        private void RenderHtmlContent(string text)
        {
            // 按优先级处理各种HTML标签
            // 1. 首先处理details标签（折叠内容）
            if (text.Contains("<details>"))
            {
                RenderDetailsBlocks(text);
                return;
            }
            
            // 2. 处理其他HTML标签
            RenderOtherHtmlTags(text);
        }
        
        private void RenderDetailsBlocks(string text)
        {
            var parts = System.Text.RegularExpressions.Regex.Split(text, @"(<details>.*?</details>)", 
                System.Text.RegularExpressions.RegexOptions.Singleline);
            
            foreach (var part in parts)
            {
                if (string.IsNullOrWhiteSpace(part)) continue;
                
                if (part.StartsWith("<details>") && part.EndsWith("</details>"))
                {
                    RenderDetailsBlock(part);
                }
                else
                {
                    // 继续处理其他HTML标签
                    RenderOtherHtmlTags(part);
                }
            }
        }
        
        private void RenderOtherHtmlTags(string text)
        {
            // 处理strong标签
            text = ProcessStrongTags(text);
            
            // 处理em标签
            text = ProcessEmTags(text);
            
            // 处理code标签
            text = ProcessCodeTags(text);
            
            // 处理pre标签
            text = ProcessPreTags(text);
            
            // 处理blockquote标签
            text = ProcessBlockquoteTags(text);
            
            // 处理列表标签
            text = ProcessListTags(text);
            
            // 如果还有剩余文本，按普通Markdown处理
            if (!string.IsNullOrWhiteSpace(text))
            {
                RenderMarkdownText(text);
            }
        }
        
        private string ProcessStrongTags(string text)
        {
            var regex = new System.Text.RegularExpressions.Regex(@"<strong>(.*?)</strong>", 
                System.Text.RegularExpressions.RegexOptions.Singleline);
            var matches = regex.Matches(text);
            
            if (matches.Count > 0)
            {
                GUILayout.BeginHorizontal();
                
                int lastIndex = 0;
                foreach (System.Text.RegularExpressions.Match match in matches)
                {
                    // 渲染前面的普通文本
                    if (match.Index > lastIndex)
                    {
                        string beforeText = text.Substring(lastIndex, match.Index - lastIndex);
                        if (!string.IsNullOrEmpty(beforeText))
                        {
                            GUILayout.Label(beforeText, EditorStyles.wordWrappedLabel, GUILayout.ExpandWidth(false));
                        }
                    }
                    
                    // 渲染粗体文本
                    var boldStyle = new GUIStyle(EditorStyles.wordWrappedLabel)
                    {
                        fontStyle = FontStyle.Bold,
                        normal = { textColor = new Color(1f, 1f, 0.8f) }
                    };
                    GUILayout.Label(match.Groups[1].Value, boldStyle, GUILayout.ExpandWidth(false));
                    
                    lastIndex = match.Index + match.Length;
                }
                
                // 渲染后面的普通文本
                if (lastIndex < text.Length)
                {
                    string afterText = text.Substring(lastIndex);
                    if (!string.IsNullOrEmpty(afterText))
                    {
                        GUILayout.Label(afterText, EditorStyles.wordWrappedLabel, GUILayout.ExpandWidth(false));
                    }
                }
                
                GUILayout.EndHorizontal();
                return ""; // 已处理完成
            }
            
            return text; // 未找到标签，返回原文本
        }
        
        private string ProcessEmTags(string text)
        {
            var regex = new System.Text.RegularExpressions.Regex(@"<em>(.*?)</em>", 
                System.Text.RegularExpressions.RegexOptions.Singleline);
            var matches = regex.Matches(text);
            
            if (matches.Count > 0)
            {
                GUILayout.BeginHorizontal();
                
                int lastIndex = 0;
                foreach (System.Text.RegularExpressions.Match match in matches)
                {
                    // 渲染前面的普通文本
                    if (match.Index > lastIndex)
                    {
                        string beforeText = text.Substring(lastIndex, match.Index - lastIndex);
                        if (!string.IsNullOrEmpty(beforeText))
                        {
                            GUILayout.Label(beforeText, EditorStyles.wordWrappedLabel, GUILayout.ExpandWidth(false));
                        }
                    }
                    
                    // 渲染斜体文本
                    var italicStyle = new GUIStyle(EditorStyles.wordWrappedLabel)
                    {
                        fontStyle = FontStyle.Italic,
                        normal = { textColor = new Color(0.9f, 0.9f, 1f) }
                    };
                    GUILayout.Label(match.Groups[1].Value, italicStyle, GUILayout.ExpandWidth(false));
                    
                    lastIndex = match.Index + match.Length;
                }
                
                // 渲染后面的普通文本
                if (lastIndex < text.Length)
                {
                    string afterText = text.Substring(lastIndex);
                    if (!string.IsNullOrEmpty(afterText))
                    {
                        GUILayout.Label(afterText, EditorStyles.wordWrappedLabel, GUILayout.ExpandWidth(false));
                    }
                }
                
                GUILayout.EndHorizontal();
                return ""; // 已处理完成
            }
            
            return text; // 未找到标签，返回原文本
        }
        
        private string ProcessCodeTags(string text)
        {
            var regex = new System.Text.RegularExpressions.Regex(@"<code>(.*?)</code>", 
                System.Text.RegularExpressions.RegexOptions.Singleline);
            var matches = regex.Matches(text);
            
            if (matches.Count > 0)
            {
                GUILayout.BeginHorizontal();
                
                int lastIndex = 0;
                foreach (System.Text.RegularExpressions.Match match in matches)
                {
                    // 渲染前面的普通文本
                    if (match.Index > lastIndex)
                    {
                        string beforeText = text.Substring(lastIndex, match.Index - lastIndex);
                        if (!string.IsNullOrEmpty(beforeText))
                        {
                            GUILayout.Label(beforeText, EditorStyles.wordWrappedLabel, GUILayout.ExpandWidth(false));
                        }
                    }
                    
                    // 渲染内联代码
                    var inlineCodeStyle = new GUIStyle(EditorStyles.textField)
                    {
                        font = Font.CreateDynamicFontFromOSFont("Courier New", 11),
                        normal = { 
                            background = MakeColorTexture(new Color(0.2f, 0.2f, 0.2f, 0.8f)),
                            textColor = new Color(0.9f, 1f, 0.9f)
                        },
                        padding = new RectOffset(4, 4, 2, 2)
                    };
                    GUILayout.Label(match.Groups[1].Value, inlineCodeStyle, GUILayout.ExpandWidth(false));
                    
                    lastIndex = match.Index + match.Length;
                }
                
                // 渲染后面的普通文本
                if (lastIndex < text.Length)
                {
                    string afterText = text.Substring(lastIndex);
                    if (!string.IsNullOrEmpty(afterText))
                    {
                        GUILayout.Label(afterText, EditorStyles.wordWrappedLabel, GUILayout.ExpandWidth(false));
                    }
                }
                
                GUILayout.EndHorizontal();
                return ""; // 已处理完成
            }
            
            return text; // 未找到标签，返回原文本
        }
        
        private string ProcessPreTags(string text)
        {
            var regex = new System.Text.RegularExpressions.Regex(@"<pre>(.*?)</pre>", 
                System.Text.RegularExpressions.RegexOptions.Singleline);
            var matches = regex.Matches(text);
            
            if (matches.Count > 0)
            {
                foreach (System.Text.RegularExpressions.Match match in matches)
                {
                    var preContent = match.Groups[1].Value;
                    
                    // 渲染预格式化文本块
                    var preStyle = new GUIStyle(EditorStyles.textArea)
                    {
                        font = Font.CreateDynamicFontFromOSFont("Courier New", 11),
                        normal = { background = MakeColorTexture(new Color(0.1f, 0.1f, 0.1f, 0.9f)) },
                        padding = new RectOffset(10, 10, 10, 10),
                        wordWrap = false
                    };
                    
                    var rect = EditorGUILayout.GetControlRect(false, 
                        preStyle.CalcHeight(new GUIContent(preContent), Screen.width - 40));
                    GUI.Box(rect, "", preStyle);
                    GUI.Label(rect, preContent, preStyle);
                }
                
                // 移除已处理的pre标签
                text = regex.Replace(text, "");
            }
            
            return text;
        }
        
        private string ProcessBlockquoteTags(string text)
        {
            var regex = new System.Text.RegularExpressions.Regex(@"<blockquote>(.*?)</blockquote>", 
                System.Text.RegularExpressions.RegexOptions.Singleline);
            var matches = regex.Matches(text);
            
            if (matches.Count > 0)
            {
                foreach (System.Text.RegularExpressions.Match match in matches)
                {
                    var quoteContent = match.Groups[1].Value.Trim();
                    
                    // 渲染引用块
                    EditorGUILayout.BeginHorizontal();
                    
                    // 左侧引用线
                    var lineRect = EditorGUILayout.GetControlRect(false, GUILayout.Width(4));
                    EditorGUI.DrawRect(lineRect, new Color(0.4f, 0.6f, 1f, 0.8f));
                    
                    GUILayout.Space(8);
                    
                    // 引用内容
                    EditorGUILayout.BeginVertical();
                    var quoteStyle = new GUIStyle(EditorStyles.wordWrappedLabel)
                    {
                        fontStyle = FontStyle.Italic,
                        normal = { textColor = new Color(0.8f, 0.8f, 0.9f) },
                        padding = new RectOffset(0, 0, 5, 5)
                    };
                    GUILayout.Label(quoteContent, quoteStyle);
                    EditorGUILayout.EndVertical();
                    
                    EditorGUILayout.EndHorizontal();
                }
                
                // 移除已处理的blockquote标签
                text = regex.Replace(text, "");
            }
            
            return text;
        }
        
        private string ProcessListTags(string text)
        {
            // 处理无序列表
            var ulRegex = new System.Text.RegularExpressions.Regex(@"<ul>(.*?)</ul>", 
                System.Text.RegularExpressions.RegexOptions.Singleline);
            var ulMatches = ulRegex.Matches(text);
            
            if (ulMatches.Count > 0)
            {
                foreach (System.Text.RegularExpressions.Match match in ulMatches)
                {
                    var listContent = match.Groups[1].Value;
                    RenderUnorderedList(listContent);
                }
                text = ulRegex.Replace(text, "");
            }
            
            // 处理有序列表
            var olRegex = new System.Text.RegularExpressions.Regex(@"<ol>(.*?)</ol>", 
                System.Text.RegularExpressions.RegexOptions.Singleline);
            var olMatches = olRegex.Matches(text);
            
            if (olMatches.Count > 0)
            {
                foreach (System.Text.RegularExpressions.Match match in olMatches)
                {
                    var listContent = match.Groups[1].Value;
                    RenderOrderedList(listContent);
                }
                text = olRegex.Replace(text, "");
            }
            
            return text;
        }
        
        private void RenderUnorderedList(string listContent)
        {
            var liRegex = new System.Text.RegularExpressions.Regex(@"<li>(.*?)</li>", 
                System.Text.RegularExpressions.RegexOptions.Singleline);
            var liMatches = liRegex.Matches(listContent);
            
            foreach (System.Text.RegularExpressions.Match match in liMatches)
            {
                var itemContent = match.Groups[1].Value.Trim();
                
                EditorGUILayout.BeginHorizontal();
                GUILayout.Space(15);
                GUILayout.Label("•", EditorStyles.wordWrappedLabel, GUILayout.Width(10));
                GUILayout.Label(itemContent, EditorStyles.wordWrappedLabel);
                EditorGUILayout.EndHorizontal();
            }
        }
        
        private void RenderOrderedList(string listContent)
        {
            var liRegex = new System.Text.RegularExpressions.Regex(@"<li>(.*?)</li>", 
                System.Text.RegularExpressions.RegexOptions.Singleline);
            var liMatches = liRegex.Matches(listContent);
            
            int index = 1;
            foreach (System.Text.RegularExpressions.Match match in liMatches)
            {
                var itemContent = match.Groups[1].Value.Trim();
                
                EditorGUILayout.BeginHorizontal();
                GUILayout.Space(15);
                GUILayout.Label($"{index}.", EditorStyles.wordWrappedLabel, GUILayout.Width(20));
                GUILayout.Label(itemContent, EditorStyles.wordWrappedLabel);
                EditorGUILayout.EndHorizontal();
                
                index++;
            }
        }
        
        private void RenderMarkdownText(string text)
        {
            // 原有的Markdown处理逻辑，用于处理剩余的普通文本
            var lines = text.Split('\n');
            
            foreach (var line in lines)
            {
                if (string.IsNullOrEmpty(line))
                {
                    GUILayout.Space(3);
                    continue;
                }
                
                // 工具调用处理 - 美化显示
                if ((line.Contains("🔧") && line.Contains("**工具")) || line.StartsWith("Tool #"))
                {
                    // 渲染工具标题
                    RenderToolHeader(line);
                }
                else if (line.StartsWith("   📋") || line.StartsWith("   ⏳") || line.StartsWith("   ✅") || 
                         line.StartsWith("   📖") || line.StartsWith("   💻") || line.StartsWith("   🐍"))
                {
                    // 工具进度信息
                    RenderToolProgress(line);
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
                // Python错误信息处理
                else if (line.StartsWith("❌"))
                {
                    var errorStyle = new GUIStyle(EditorStyles.boldLabel)
                    {
                        fontSize = 14,
                        wordWrap = true,
                        normal = { textColor = new Color(1f, 0.3f, 0.3f) }
                    };
                    GUILayout.Label(line, errorStyle);
                    GUILayout.Space(3);
                }
                // 错误详情行
                else if (line.StartsWith("**错误") || line.StartsWith("**已处理"))
                {
                    var errorDetailStyle = new GUIStyle(EditorStyles.wordWrappedLabel)
                    {
                        normal = { textColor = new Color(1f, 0.6f, 0.6f) },
                        fontStyle = FontStyle.Bold
                    };
                    GUILayout.Label(line, errorDetailStyle);
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
        
        private void RenderDetailsBlock(string detailsBlock)
        {
            // 提取summary和content
            var summaryMatch = System.Text.RegularExpressions.Regex.Match(
                detailsBlock, @"<summary>(.*?)</summary>", 
                System.Text.RegularExpressions.RegexOptions.Singleline);
            
            if (!summaryMatch.Success) return;
            
            var summary = summaryMatch.Groups[1].Value.Trim();
            var content = detailsBlock
                .Replace(summaryMatch.Value, "")
                .Replace("<details>", "")
                .Replace("</details>", "")
                .Trim();
            
            // 生成唯一的折叠ID
            var collapseId = $"details_{summary.GetHashCode()}_{content.GetHashCode()}";
            
            if (!collapsedStates.ContainsKey(collapseId))
            {
                collapsedStates[collapseId] = true; // 默认收缩
            }
            
            var isCollapsed = collapsedStates[collapseId];
            
            // 渲染可点击的summary标题
            EditorGUILayout.BeginHorizontal();
            
            var buttonStyle = new GUIStyle(EditorStyles.label)
            {
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(0.8f, 0.9f, 1f) }
            };
            
            // 折叠/展开图标
            var icon = isCollapsed ? "▶" : "▼";
            if (GUILayout.Button($"{icon} {summary}", buttonStyle, GUILayout.ExpandWidth(true)))
            {
                collapsedStates[collapseId] = !isCollapsed;
            }
            
            EditorGUILayout.EndHorizontal();
            
            // 如果展开，显示内容
            if (!isCollapsed)
            {
                EditorGUILayout.BeginVertical("box");
                GUILayout.Space(5);
                
                // 渲染内容（支持Markdown）
                var contentLines = content.Split('\n');
                foreach (var line in contentLines)
                {
                    if (string.IsNullOrWhiteSpace(line))
                    {
                        GUILayout.Space(2);
                        continue;
                    }
                    
                    // 简单的Markdown支持
                    if (line.StartsWith("**") && line.EndsWith("**"))
                    {
                        var boldText = line.Substring(2, line.Length - 4);
                        var boldStyle = new GUIStyle(EditorStyles.label) { fontStyle = FontStyle.Bold };
                        GUILayout.Label(boldText, boldStyle);
                    }
                    else if (line.StartsWith("```") && line.EndsWith("```"))
                    {
                        var codeText = line.Substring(3, line.Length - 6);
                        var codeStyle = new GUIStyle(EditorStyles.textField) 
                        { 
                            wordWrap = true,
                            normal = { background = null }
                        };
                        GUILayout.Label(codeText, codeStyle);
                    }
                    else
                    {
                        GUILayout.Label(line, EditorStyles.wordWrappedLabel);
                    }
                }
                
                GUILayout.Space(5);
                EditorGUILayout.EndVertical();
            }
            
            GUILayout.Space(3);
        }
        
        private void RenderToolHeader(string line)
        {
            // 匹配多种工具调用格式
            System.Text.RegularExpressions.Match match = null;
            
            // 格式1: "🔧 **工具 #1: file_read**"
            match = System.Text.RegularExpressions.Regex.Match(line, @"🔧 \*\*工具 #(\d+): (.+?)\*\*");
            if (!match.Success)
            {
                // 格式2: "Tool #1: file_read"
                match = System.Text.RegularExpressions.Regex.Match(line, @"Tool #(\d+): (.+)");
            }
            
            if (match.Success)
            {
                var toolNumber = match.Groups[1].Value;
                var toolName = match.Groups[2].Value;
                
                // 创建突出的工具调用样式
                var toolBoxStyle = new GUIStyle(EditorStyles.helpBox)
                {
                    normal = { background = MakeColorTexture(new Color(0.2f, 0.4f, 0.6f, 0.3f)) }
                };
                
                EditorGUILayout.BeginVertical(toolBoxStyle);
                EditorGUILayout.BeginHorizontal();
                
                // 工具图标
                var iconStyle = new GUIStyle(EditorStyles.label)
                {
                    fontSize = 16,
                    normal = { textColor = new Color(0.3f, 0.8f, 1f) }
                };
                GUILayout.Label("🔧", iconStyle, GUILayout.Width(25));
                
                // 工具信息
                var toolStyle = new GUIStyle(EditorStyles.label)
                {
                    fontStyle = FontStyle.Bold,
                    fontSize = 13,
                    normal = { textColor = new Color(0.8f, 1f, 0.8f) }
                };
                GUILayout.Label($"工具调用 #{toolNumber}: {toolName}", toolStyle);
                
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.EndVertical();
                GUILayout.Space(3);
            }
            else
            {
                // 回退到普通文本显示
                GUILayout.Label(line, EditorStyles.wordWrappedLabel);
            }
        }
        
        private void RenderToolProgress(string line)
        {
            // 创建缩进的工具进度样式
            var progressStyle = new GUIStyle(EditorStyles.wordWrappedLabel)
            {
                normal = { textColor = new Color(0.8f, 0.8f, 0.8f) },
                fontSize = 11
            };
            
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(30); // 缩进
            
            // 根据前缀显示不同的状态颜色
            if (line.Contains("📋 参数:"))
            {
                progressStyle.normal.textColor = new Color(0.6f, 0.8f, 1f);
            }
            else if (line.Contains("⏳"))
            {
                progressStyle.normal.textColor = new Color(1f, 0.8f, 0.4f);
            }
            else if (line.Contains("✅"))
            {
                progressStyle.normal.textColor = new Color(0.4f, 1f, 0.4f);
            }
            
            GUILayout.Label(line.TrimStart(), progressStyle);
            EditorGUILayout.EndHorizontal();
            GUILayout.Space(1);
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
            hasActiveStream = true;
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
            Debug.Log($"[AIAgentWindow] 接收到流式数据块: {chunk}，当前活跃流: {hasActiveStream}");
            
            // 严格检查：只有在有活跃流的情况下才处理chunk
            if (!hasActiveStream)
            {
                Debug.Log($"[AIAgentWindow] 无活跃流，忽略chunk: {chunk}");
                return;
            }
            
            // 第一次创建消息
            if (currentStreamingMessageIndex == -1)
            {
                messages.Add(new ChatMessage
                {
                    content = "",
                    isUser = false,
                    timestamp = DateTime.Now
                });
                currentStreamingMessageIndex = messages.Count - 1;
                Debug.Log($"[AIAgentWindow] 创建唯一流式消息，索引: {currentStreamingMessageIndex}");
            }
            
            // 更新消息内容
            currentStreamText += chunk;
            if (currentStreamingMessageIndex >= 0 && currentStreamingMessageIndex < messages.Count)
            {
                messages[currentStreamingMessageIndex].content = currentStreamText + "▌";
                Debug.Log($"[AIAgentWindow] 更新消息，当前长度: {currentStreamText.Length}");
            }
            
            // UI更新
            EditorApplication.delayCall += () => {
                if (this != null)
                {
                    scrollToBottom = true;
                    Repaint();
                }
            };
        }
        
        private void OnStreamComplete()
        {
            Debug.Log($"[AIAgentWindow] 流式响应完成，立即关闭活跃流");
            
            // 立即关闭活跃流，阻止任何后续chunk
            hasActiveStream = false;
            
            // 立即完成当前消息
            if (currentStreamingMessageIndex >= 0 && currentStreamingMessageIndex < messages.Count)
            {
                messages[currentStreamingMessageIndex].content = currentStreamText;
                Debug.Log($"[AIAgentWindow] 完成消息，最终长度: {currentStreamText.Length}");
            }
            
            // 重置所有状态
            currentStreamText = "";
            currentStreamingMessageIndex = -1;
            isProcessing = false;
            
            // UI更新
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
            
            // 立即关闭活跃流
            hasActiveStream = false;
            isProcessing = false;
            
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