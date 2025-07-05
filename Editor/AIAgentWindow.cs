using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System;
using System.Threading.Tasks;
using Python.Runtime;
using System.Linq;

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
        
        // Tab system
        private int selectedTab = 0;
        private string[] tabNames = { "AI智能助手", "AI助手设置" };
        
        // Settings variables from SetupWizard
        private int currentStep = 0;
        private string statusMessage = "";
        private float progress = 0f;
        private bool setupCompleted = false;
        private MCPConfiguration mcpConfig;
        private GUIStyle stepStyle;
        private GUIStyle statusStyle;
        
        // MCP configuration
        private int settingsTab = 0;
        private string[] settingsTabNames = { "设置进度", "MCP配置" };
        private string mcpJsonConfig = "";
        private bool mcpConfigExpanded = false;
        private Vector2 mcpScrollPosition;
        private bool showMCPPresets = false;
        
        private readonly string[] setupSteps = {
            "检测Python环境",
            "检测Node.js环境",
            "安装Node.js和npm(如需要)",
            "创建虚拟环境", 
            "安装Strands Agent SDK",
            "安装MCP支持包(可选)",
            "安装SSL证书支持",
            "安装其他依赖包",
            "配置环境变量",
            "配置MCP服务器",
            "初始化Python桥接",
            "验证AWS连接",
            "完成设置"
        };

        [MenuItem("Window/AI助手/AI助手")]
        public static void ShowWindow()
        {
            var window = GetWindow<AIAgentWindow>("AI助手");
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
            
            // Initialize MCP configuration
            LoadMCPConfiguration();
            CheckSetupStatus();
            
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
                
                stepStyle = new GUIStyle(EditorStyles.label);
                statusStyle = new GUIStyle(EditorStyles.helpBox);
            }
            
            // Tab selector
            DrawTabSelector();
            
            // Draw content based on selected tab
            if (selectedTab == 0)
            {
                DrawChatInterface();
            }
            else
            {
                DrawSettingsInterface();
            }
        }
        
        private void DrawTabSelector()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            
            for (int i = 0; i < tabNames.Length; i++)
            {
                bool isSelected = selectedTab == i;
                
                // 使用不同的样式来区分选中和未选中状态
                var style = isSelected ? "toolbarbutton" : "toolbarbutton";
                
                // 设置颜色
                var originalColor = GUI.backgroundColor;
                var originalContentColor = GUI.contentColor;
                
                if (isSelected)
                {
                    // 选中状态：深蓝色背景，白色文字
                    GUI.backgroundColor = new Color(0.2f, 0.4f, 0.8f, 1f);
                    GUI.contentColor = Color.white;
                }
                else
                {
                    // 未选中状态：正常颜色，灰色文字
                    GUI.backgroundColor = EditorGUIUtility.isProSkin ? new Color(0.3f, 0.3f, 0.3f, 1f) : new Color(0.8f, 0.8f, 0.8f, 1f);
                    GUI.contentColor = EditorGUIUtility.isProSkin ? new Color(0.7f, 0.7f, 0.7f, 1f) : new Color(0.4f, 0.4f, 0.4f, 1f);
                }
                
                if (GUILayout.Button(tabNames[i], style, GUILayout.Height(25)))
                {
                    selectedTab = i;
                }
                
                // 恢复颜色
                GUI.backgroundColor = originalColor;
                GUI.contentColor = originalContentColor;
            }
            
            EditorGUILayout.EndHorizontal();
            
            // 添加一条分隔线
            EditorGUILayout.Space(2);
            var rect = EditorGUILayout.GetControlRect(false, 1);
            EditorGUI.DrawRect(rect, EditorGUIUtility.isProSkin ? new Color(0.1f, 0.1f, 0.1f, 1f) : new Color(0.6f, 0.6f, 0.6f, 1f));
            EditorGUILayout.Space(5);
        }
        
        private void DrawChatInterface()
        {

            // Header
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            GUILayout.Label("Unity AI Assistant", EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();
            
            if (GUILayout.Button("Clear", EditorStyles.toolbarButton, GUILayout.Width(50)))
            {
                messages.Clear();
                SaveChatHistory();
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
            // 检查是否包含JSON数据
            if (IsJsonContent(line))
            {
                RenderJsonToolProgress(line);
            }
            else
            {
                RenderRegularToolProgress(line);
            }
        }
        
        private void RenderRegularToolProgress(string line)
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
        
        private void RenderJsonToolProgress(string line)
        {
            string trimmedLine = line.TrimStart();
            
            // 提取JSON部分和前缀
            string prefix = "";
            string jsonContent = "";
            
            if (trimmedLine.Contains("原始数据:"))
            {
                var parts = trimmedLine.Split(new[] { "原始数据:" }, 2, StringSplitOptions.None);
                if (parts.Length == 2)
                {
                    prefix = parts[0] + "原始数据:";
                    jsonContent = parts[1].Trim();
                }
            }
            else if (trimmedLine.Contains(":") && (trimmedLine.Contains("{") || trimmedLine.Contains("[")))
            {
                var colonIndex = trimmedLine.IndexOf(':');
                prefix = trimmedLine.Substring(0, colonIndex + 1);
                jsonContent = trimmedLine.Substring(colonIndex + 1).Trim();
            }
            else
            {
                RenderRegularToolProgress(line);
                return;
            }
            
            // 创建展开/收缩的唯一ID
            string collapseId = $"json_{prefix.GetHashCode()}_{jsonContent.GetHashCode()}";
            if (!collapsedStates.ContainsKey(collapseId))
            {
                collapsedStates[collapseId] = true; // 默认收缩显示
            }
            
            bool isCollapsed = collapsedStates[collapseId];
            
            // 渲染前缀和展开/收缩按钮
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(30); // 缩进
            
            var prefixStyle = new GUIStyle(EditorStyles.label)
            {
                normal = { textColor = new Color(0.6f, 0.8f, 1f) },
                fontSize = 11
            };
            
            // 展开/收缩图标
            string icon = isCollapsed ? "▶" : "▼";
            var iconStyle = new GUIStyle(EditorStyles.label)
            {
                normal = { textColor = new Color(0.8f, 0.8f, 0.8f) },
                fontSize = 10
            };
            
            if (GUILayout.Button($"{icon} {prefix}", iconStyle, GUILayout.ExpandWidth(false)))
            {
                collapsedStates[collapseId] = !isCollapsed;
            }
            
            if (isCollapsed)
            {
                // 收缩状态：显示简化的JSON预览
                var previewStyle = new GUIStyle(EditorStyles.label)
                {
                    normal = { textColor = new Color(0.6f, 0.6f, 0.6f) },
                    fontSize = 10,
                    fontStyle = FontStyle.Italic
                };
                GUILayout.Label(GetJsonPreview(jsonContent), previewStyle);
            }
            
            EditorGUILayout.EndHorizontal();
            
            // 展开状态：显示格式化的JSON
            if (!isCollapsed)
            {
                EditorGUILayout.BeginVertical();
                GUILayout.Space(30 + 10); // 额外缩进
                
                string formattedJson = FormatJsonString(jsonContent);
                
                var jsonStyle = new GUIStyle(EditorStyles.textArea)
                {
                    font = Font.CreateDynamicFontFromOSFont("Courier New", 10),
                    normal = { 
                        background = MakeColorTexture(new Color(0.1f, 0.1f, 0.1f, 0.8f)),
                        textColor = new Color(0.9f, 0.9f, 0.9f)
                    },
                    padding = new RectOffset(10, 10, 8, 8),
                    wordWrap = true
                };
                
                EditorGUILayout.BeginHorizontal();
                GUILayout.Space(40);
                
                var rect = EditorGUILayout.GetControlRect(false, jsonStyle.CalcHeight(new GUIContent(formattedJson), Screen.width - 80));
                GUI.Box(rect, "", jsonStyle);
                GUI.Label(rect, formattedJson, jsonStyle);
                
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.EndVertical();
            }
            
            GUILayout.Space(2);
        }
        
        private bool IsJsonContent(string line)
        {
            string trimmed = line.TrimStart();
            return (trimmed.Contains("原始数据:") && (trimmed.Contains("{") || trimmed.Contains("["))) ||
                   (trimmed.Contains(":") && (trimmed.Contains("{'") || trimmed.Contains("{\"") || 
                    trimmed.Contains("[{") || trimmed.Contains("['") || trimmed.Contains("[\"") ||
                    trimmed.Contains("'message':") || trimmed.Contains("\"message\":")));
        }
        
        private string GetJsonPreview(string jsonContent)
        {
            if (string.IsNullOrEmpty(jsonContent)) return "";
            
            // 简化的JSON预览
            if (jsonContent.Length > 50)
            {
                return jsonContent.Substring(0, 47) + "...";
            }
            return jsonContent;
        }
        
        private string FormatJsonString(string jsonContent)
        {
            if (string.IsNullOrEmpty(jsonContent)) return "";
            
            try
            {
                // 简单的JSON格式化
                string formatted = jsonContent;
                
                // 基本的格式化处理
                formatted = formatted.Replace("{'", "{\n  '")
                                   .Replace("\":", "\": ")
                                   .Replace("',", "',\n  ")
                                   .Replace("\",", "\",\n  ")
                                   .Replace("}", "\n}")
                                   .Replace("[{", "[\n  {")
                                   .Replace("}]", "}\n]")
                                   .Replace("}, {", "},\n  {");
                
                // 修复缩进
                var lines = formatted.Split('\n');
                var result = new System.Text.StringBuilder();
                int indentLevel = 0;
                
                foreach (var line in lines)
                {
                    string trimmedLine = line.Trim();
                    if (string.IsNullOrEmpty(trimmedLine)) continue;
                    
                    // 减少缩进
                    if (trimmedLine.StartsWith("}") || trimmedLine.StartsWith("]"))
                    {
                        indentLevel = Math.Max(0, indentLevel - 1);
                    }
                    
                    // 添加缩进
                    result.AppendLine(new string(' ', indentLevel * 2) + trimmedLine);
                    
                    // 增加缩进
                    if (trimmedLine.EndsWith("{") || trimmedLine.EndsWith("["))
                    {
                        indentLevel++;
                    }
                }
                
                return result.ToString().TrimEnd();
            }
            catch
            {
                // 如果格式化失败，返回原始内容
                return jsonContent;
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
            hasActiveStream = true;
            isProcessing = true;
            
            // 添加超时保护机制 - 90秒后自动重置状态
            EditorApplication.delayCall += () => {
                System.Threading.Tasks.Task.Delay(90000).ContinueWith(_ => {
                    if (isProcessing)
                    {
                        Debug.LogWarning("[AIAgentWindow] 响应超时，自动重置状态");
                        EditorApplication.delayCall += () => {
                            if (this != null && isProcessing)
                            {
                                hasActiveStream = false;
                                isProcessing = false;
                                currentStreamText = "";
                                currentStreamingMessageIndex = -1;
                                
                                // 添加超时提示消息
                                messages.Add(new ChatMessage
                                {
                                    content = "⏰ AI响应超时，界面已自动重置。您可以继续发送新消息。",
                                    isUser = false,
                                    timestamp = DateTime.Now
                                });
                                
                                SaveChatHistory();
                                Repaint();
                            }
                        };
                    }
                });
            };
            
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
        
        private void DrawSettingsInterface()
        {
            // Settings tab selector
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            
            for (int i = 0; i < settingsTabNames.Length; i++)
            {
                bool isSelected = settingsTab == i;
                
                // 设置颜色
                var originalColor = GUI.backgroundColor;
                var originalContentColor = GUI.contentColor;
                
                if (isSelected)
                {
                    // 选中状态：深蓝色背景，白色文字
                    GUI.backgroundColor = new Color(0.2f, 0.4f, 0.8f, 1f);
                    GUI.contentColor = Color.white;
                }
                else
                {
                    // 未选中状态：正常颜色，灰色文字
                    GUI.backgroundColor = EditorGUIUtility.isProSkin ? new Color(0.3f, 0.3f, 0.3f, 1f) : new Color(0.8f, 0.8f, 0.8f, 1f);
                    GUI.contentColor = EditorGUIUtility.isProSkin ? new Color(0.7f, 0.7f, 0.7f, 1f) : new Color(0.4f, 0.4f, 0.4f, 1f);
                }
                
                if (GUILayout.Button(settingsTabNames[i], "toolbarbutton", GUILayout.Height(22)))
                {
                    settingsTab = i;
                }
                
                // 恢复颜色
                GUI.backgroundColor = originalColor;
                GUI.contentColor = originalContentColor;
            }
            
            EditorGUILayout.EndHorizontal();
            
            // 添加一条分隔线
            EditorGUILayout.Space(2);
            var rect = EditorGUILayout.GetControlRect(false, 1);
            EditorGUI.DrawRect(rect, EditorGUIUtility.isProSkin ? new Color(0.15f, 0.15f, 0.15f, 1f) : new Color(0.7f, 0.7f, 0.7f, 1f));
            EditorGUILayout.Space(5);
            
            if (settingsTab == 0)
            {
                DrawSetupProgress();
            }
            else
            {
                DrawMCPConfiguration();
            }
        }
        
        private void DrawSetupProgress()
        {
            // Steps display
            DrawSteps();
            
            GUILayout.Space(20);
            
            // Status message
            DrawStatus();
            
            GUILayout.Space(10);
            
            // Progress bar
            DrawProgressBar();
            
            GUILayout.Space(20);
            
            // Operation buttons
            DrawButtons();
        }
        
        private void DrawSteps()
        {
            for (int i = 0; i < setupSteps.Length; i++)
            {
                DrawStep(i, setupSteps[i]);
            }
        }
        
        private void DrawStep(int step, string title)
        {
            EditorGUILayout.BeginHorizontal();
            
            // Step icon
            string icon;
            Color iconColor = Color.white;
            
            if (step < currentStep || setupCompleted)
            {
                icon = "✓";
                iconColor = Color.green;
            }
            else if (step == currentStep && isProcessing)
            {
                icon = "⟳";
                iconColor = Color.yellow;
            }
            else
            {
                icon = "○";
                iconColor = Color.gray;
            }
            
            var originalColor = GUI.color;
            GUI.color = iconColor;
            GUILayout.Label(icon, GUILayout.Width(20));
            GUI.color = originalColor;
            
            // Step title
            var style = new GUIStyle(stepStyle ?? EditorStyles.label);
            if (step < currentStep || setupCompleted)
            {
                style.normal.textColor = Color.green;
            }
            else if (step == currentStep && isProcessing)
            {
                style.fontStyle = FontStyle.Bold;
                style.normal.textColor = EditorGUIUtility.isProSkin ? Color.yellow : new Color(0.8f, 0.6f, 0f);
            }
            else
            {
                style.normal.textColor = Color.gray;
            }
            
            GUILayout.Label($"{step + 1}. {title}", style);
            
            EditorGUILayout.EndHorizontal();
        }
        
        private void DrawStatus()
        {
            if (!string.IsNullOrEmpty(statusMessage))
            {
                MessageType messageType = MessageType.Info;
                
                if (progress < 0)
                {
                    messageType = MessageType.Error;
                }
                else if (setupCompleted)
                {
                    messageType = MessageType.Info;
                }
                
                EditorGUILayout.HelpBox(statusMessage, messageType);
            }
        }
        
        private void DrawProgressBar()
        {
            if (isProcessing && progress >= 0)
            {
                var rect = EditorGUILayout.GetControlRect(GUILayout.Height(20));
                EditorGUI.ProgressBar(rect, progress, $"{(int)(progress * 100)}%");
            }
            else if (setupCompleted)
            {
                var rect = EditorGUILayout.GetControlRect(GUILayout.Height(20));
                EditorGUI.ProgressBar(rect, 1.0f, "100% - 完成");
            }
        }
        
        private void DrawButtons()
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            
            if (setupCompleted)
            {
                // Buttons after setup completion
                if (GUILayout.Button("打开AI助手", GUILayout.Width(120), GUILayout.Height(35)))
                {
                    selectedTab = 0; // Switch to chat tab
                }
                
                GUILayout.Space(10);
                
                if (GUILayout.Button("重新设置", GUILayout.Width(100), GUILayout.Height(35)))
                {
                    ResetSetup();
                }
            }
            else
            {
                // Buttons during setup process
                GUI.enabled = !isProcessing;
                if (GUILayout.Button("开始设置", GUILayout.Width(120), GUILayout.Height(35)))
                {
                    StartSetup();
                }
                GUI.enabled = true;
                
                if (isProcessing)
                {
                    GUILayout.Space(10);
                    if (GUILayout.Button("取消", GUILayout.Width(80), GUILayout.Height(35)))
                    {
                        CancelSetup();
                    }
                }
            }
            
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
        }
        
        private void DrawMCPConfiguration()
        {
            EditorGUILayout.Space();
            
            // MCP Configuration UI
            EditorGUILayout.BeginVertical("box");
            GUILayout.Label("MCP 服务器配置", EditorStyles.boldLabel);
            
            if (mcpConfig == null)
            {
                EditorGUILayout.HelpBox("MCP配置未初始化", MessageType.Warning);
                if (GUILayout.Button("初始化MCP配置"))
                {
                    InitializeMCPConfig();
                }
                EditorGUILayout.EndVertical();
                return;
            }
            
            // JSON configuration area
            EditorGUILayout.LabelField("JSON配置", EditorStyles.boldLabel);
            mcpScrollPosition = EditorGUILayout.BeginScrollView(mcpScrollPosition, GUILayout.Height(200));
            mcpJsonConfig = EditorGUILayout.TextArea(mcpJsonConfig, GUILayout.ExpandHeight(true));
            EditorGUILayout.EndScrollView();
            
            if (GUILayout.Button("保存配置"))
            {
                SaveMCPConfiguration();
            }
            
            EditorGUILayout.EndVertical();
            
            // Server list
            EditorGUILayout.Space();
            EditorGUILayout.BeginVertical("box");
            GUILayout.Label("服务器列表", EditorStyles.boldLabel);
            
            if (mcpConfig.servers != null && mcpConfig.servers.Count > 0)
            {
                foreach (var server in mcpConfig.servers)
                {
                    EditorGUILayout.BeginHorizontal("box");
                    GUILayout.Label(server.name, EditorStyles.boldLabel);
                    GUILayout.FlexibleSpace();
                    GUILayout.Label($"类型: {server.transportType}", EditorStyles.miniLabel);
                    EditorGUILayout.EndHorizontal();
                }
            }
            else
            {
                EditorGUILayout.HelpBox("没有配置的服务器", MessageType.Info);
            }
            
            EditorGUILayout.EndVertical();
        }
        
        // Settings helper methods
        private void LoadMCPConfiguration()
        {
            string configPath = "Assets/UnityAIAgent/MCPConfig.asset";
            mcpConfig = UnityEditor.AssetDatabase.LoadAssetAtPath<MCPConfiguration>(configPath);
            if (mcpConfig != null)
            {
                mcpJsonConfig = mcpConfig.GenerateAnthropicMCPJson();
            }
            else
            {
                mcpJsonConfig = "{\n  \"mcpServers\": {}\n}";
            }
        }
        
        private void SaveMCPConfiguration()
        {
            try
            {
                // 简化逻辑：直接保存原始JSON到文件
                string jsonConfigPath = "Assets/UnityAIAgent/mcp_config.json";
                
                // 确保目录存在
                string directory = System.IO.Path.GetDirectoryName(jsonConfigPath);
                if (!System.IO.Directory.Exists(directory))
                {
                    System.IO.Directory.CreateDirectory(directory);
                }
                
                // 直接保存原始JSON配置文件
                System.IO.File.WriteAllText(jsonConfigPath, mcpJsonConfig);
                AssetDatabase.Refresh();
                
                Debug.Log($"MCP配置已保存到: {jsonConfigPath}");
                
                // 通知Python端重新加载MCP配置
                ReloadMCPConfigInPython();
                
                EditorUtility.DisplayDialog("应用成功", "MCP JSON配置已成功保存！\\n\\nPython端已重新加载MCP配置。", "确定");
                
                statusMessage = "MCP配置已成功保存";
                
                // 可选：同时更新Unity ScriptableObject用于UI显示
                if (mcpConfig != null)
                {
                    UpdateScriptableObjectFromJson();
                }
            }
            catch (Exception e)
            {
                statusMessage = $"保存配置失败: {e.Message}";
                EditorUtility.DisplayDialog("保存失败", $"保存JSON配置时出错：\\n{e.Message}", "确定");
                Debug.LogError($"保存MCP配置失败: {e}");
            }
        }
        
        private void UpdateScriptableObjectFromJson()
        {
            try
            {
                // 简单解析JSON以更新Unity UI显示
                mcpConfig.servers.Clear();
                mcpConfig.enableMCP = true;
                
                // 基本的JSON解析来更新服务器列表显示
                if (ParseServersFromJson(mcpJsonConfig))
                {
                    EditorUtility.SetDirty(mcpConfig);
                    AssetDatabase.SaveAssets();
                    Debug.Log($"Unity ScriptableObject已更新，服务器总数: {mcpConfig.servers.Count}");
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"更新ScriptableObject失败，但JSON文件已保存: {e.Message}");
            }
        }
        
        private bool ParseServersFromJson(string jsonContent)
        {
            try
            {
                // 寻找mcpServers对象
                int mcpServersStart = jsonContent.IndexOf("\"mcpServers\":");
                if (mcpServersStart == -1) return false;
                
                int braceStart = jsonContent.IndexOf('{', mcpServersStart);
                if (braceStart == -1) return false;
                
                // 找到匹配的结束大括号
                int braceCount = 1;
                int braceEnd = braceStart + 1;
                
                while (braceEnd < jsonContent.Length && braceCount > 0)
                {
                    if (jsonContent[braceEnd] == '{') braceCount++;
                    else if (jsonContent[braceEnd] == '}') braceCount--;
                    braceEnd++;
                }
                
                if (braceCount > 0) return false;
                
                string serversContent = jsonContent.Substring(braceStart + 1, braceEnd - braceStart - 2);
                
                // 简化的服务器解析 - 只寻找顶级服务器定义
                return ParseServerDefinitions(serversContent);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"JSON解析失败: {e.Message}");
                return false;
            }
        }
        
        private bool ParseServerDefinitions(string serversContent)
        {
            int index = 0;
            
            while (index < serversContent.Length)
            {
                // 寻找服务器名称
                int nameStart = serversContent.IndexOf('"', index);
                if (nameStart == -1) break;
                
                int nameEnd = serversContent.IndexOf('"', nameStart + 1);
                if (nameEnd == -1) break;
                
                // 检查是否是服务器定义
                int colonIndex = serversContent.IndexOf(':', nameEnd);
                if (colonIndex == -1) break;
                
                int braceIndex = serversContent.IndexOf('{', colonIndex);
                if (braceIndex == -1) break;
                
                // 确保是顶层定义
                string between = serversContent.Substring(nameEnd + 1, colonIndex - nameEnd - 1).Trim();
                if (!string.IsNullOrEmpty(between))
                {
                    index = nameEnd + 1;
                    continue;
                }
                
                // 提取服务器名称
                string serverName = serversContent.Substring(nameStart + 1, nameEnd - nameStart - 1);
                
                // 找到服务器配置的结束
                int braceCount = 1;
                int configEnd = braceIndex + 1;
                
                while (configEnd < serversContent.Length && braceCount > 0)
                {
                    if (serversContent[configEnd] == '{') braceCount++;
                    else if (serversContent[configEnd] == '}') braceCount--;
                    configEnd++;
                }
                
                if (braceCount == 0)
                {
                    // 提取服务器配置
                    string serverConfigContent = serversContent.Substring(braceIndex + 1, configEnd - braceIndex - 2);
                    
                    // 创建服务器配置 - 泛化解析所有字段
                    var server = CreateServerFromConfig(serverName, serverConfigContent);
                    mcpConfig.servers.Add(server);
                }
                
                index = configEnd;
            }
            
            return true;
        }
        
        private MCPServerConfig CreateServerFromConfig(string serverName, string configContent)
        {
            var server = new MCPServerConfig
            {
                name = serverName,
                enabled = true,
                transportType = MCPTransportType.Stdio,
                environmentVariables = new List<EnvironmentVariable>()
            };
            
            // 泛化解析：command
            server.command = ExtractStringValue(configContent, "command");
            
            // 泛化解析：args数组
            server.args = ExtractArrayValue(configContent, "args");
            
            // 泛化解析：env对象
            ParseEnvironmentVariables(server, configContent);
            
            // 可以在这里添加更多字段的解析，如：
            // - workingDirectory
            // - timeoutSeconds
            // - httpUrl
            // 等等，都使用相同的ExtractStringValue方法
            
            return server;
        }
        
        private string ExtractStringValue(string content, string fieldName)
        {
            string pattern = $"\"{fieldName}\"";
            int fieldIndex = content.IndexOf(pattern);
            if (fieldIndex == -1) return "";
            
            int colonIndex = content.IndexOf(':', fieldIndex);
            if (colonIndex == -1) return "";
            
            int firstQuote = content.IndexOf('"', colonIndex);
            if (firstQuote == -1) return "";
            
            int lastQuote = content.IndexOf('"', firstQuote + 1);
            if (lastQuote == -1) return "";
            
            return content.Substring(firstQuote + 1, lastQuote - firstQuote - 1);
        }
        
        private string[] ExtractArrayValue(string content, string fieldName)
        {
            var result = new List<string>();
            
            string pattern = $"\"{fieldName}\"";
            int fieldIndex = content.IndexOf(pattern);
            if (fieldIndex == -1) return result.ToArray();
            
            int colonIndex = content.IndexOf(':', fieldIndex);
            if (colonIndex == -1) return result.ToArray();
            
            int arrayStart = content.IndexOf('[', colonIndex);
            if (arrayStart == -1) return result.ToArray();
            
            int arrayEnd = content.IndexOf(']', arrayStart);
            if (arrayEnd == -1) return result.ToArray();
            
            string arrayContent = content.Substring(arrayStart + 1, arrayEnd - arrayStart - 1);
            
            // 解析数组元素
            string[] parts = arrayContent.Split(',');
            foreach (string part in parts)
            {
                string trimmed = part.Trim();
                if (trimmed.StartsWith("\"") && trimmed.EndsWith("\"") && trimmed.Length > 1)
                {
                    result.Add(trimmed.Substring(1, trimmed.Length - 2));
                }
            }
            
            return result.ToArray();
        }
        
        private void ParseEnvironmentVariables(MCPServerConfig server, string configContent)
        {
            string envPattern = "\"env\"";
            int envIndex = configContent.IndexOf(envPattern);
            if (envIndex == -1) return;
            
            int colonIndex = configContent.IndexOf(':', envIndex);
            if (colonIndex == -1) return;
            
            int braceStart = configContent.IndexOf('{', colonIndex);
            if (braceStart == -1) return;
            
            // 找到env对象的结束
            int braceCount = 1;
            int braceEnd = braceStart + 1;
            
            while (braceEnd < configContent.Length && braceCount > 0)
            {
                if (configContent[braceEnd] == '{') braceCount++;
                else if (configContent[braceEnd] == '}') braceCount--;
                braceEnd++;
            }
            
            if (braceCount > 0) return;
            
            string envContent = configContent.Substring(braceStart + 1, braceEnd - braceStart - 2);
            
            // 解析环境变量键值对
            string[] lines = envContent.Split(new char[] { ',', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            
            foreach (string line in lines)
            {
                string trimmed = line.Trim();
                if (string.IsNullOrEmpty(trimmed)) continue;
                
                int colonPos = trimmed.IndexOf(':');
                if (colonPos == -1) continue;
                
                string key = trimmed.Substring(0, colonPos).Trim().Trim('"');
                string value = trimmed.Substring(colonPos + 1).Trim().Trim('"');
                
                if (!string.IsNullOrEmpty(key))
                {
                    server.environmentVariables.Add(new EnvironmentVariable
                    {
                        key = key,
                        value = value,
                        isSecret = false
                    });
                }
            }
        }
        
        private void ReloadMCPConfigInPython()
        {
            try
            {
                // 确保Python桥接已初始化
                if (!PythonManager.IsInitialized)
                {
                    Debug.LogWarning("Python未初始化，无法重新加载MCP配置");
                    return;
                }
                
                // 调用Python端的reload_mcp_config函数
                using (Py.GIL())
                {
                    dynamic agentCore = Py.Import("agent_core");
                    string resultJson = agentCore.reload_mcp_config();
                    
                    // 解析结果
                    var result = JsonUtility.FromJson<MCPReloadResult>(resultJson);
                    
                    if (result.success)
                    {
                        Debug.Log($"Python端MCP配置重新加载成功: {result.message}");
                        Debug.Log($"MCP启用: {result.mcp_enabled}, 服务器数: {result.server_count}, 启用数: {result.enabled_server_count}");
                    }
                    else
                    {
                        Debug.LogError($"Python端MCP配置重新加载失败: {result.message}");
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"调用Python reload_mcp_config失败: {e.Message}");
            }
        }
        
        private void InitializeMCPConfig()
        {
            string configPath = "Assets/UnityAIAgent/MCPConfig.asset";
            
            // Create directory if it doesn't exist
            string directory = System.IO.Path.GetDirectoryName(configPath);
            if (!UnityEditor.AssetDatabase.IsValidFolder(directory))
            {
                UnityEditor.AssetDatabase.CreateFolder("Assets", "UnityAIAgent");
            }
            
            // Create new configuration
            mcpConfig = ScriptableObject.CreateInstance<MCPConfiguration>();
            mcpConfig.AddPresetConfigurations();
            
            // Save as asset
            UnityEditor.AssetDatabase.CreateAsset(mcpConfig, configPath);
            UnityEditor.AssetDatabase.SaveAssets();
            
            mcpJsonConfig = mcpConfig.GenerateAnthropicMCPJson();
        }
        
        private void CheckSetupStatus()
        {
            if (PythonManager.IsInitialized)
            {
                currentStep = setupSteps.Length;
                setupCompleted = true;
                statusMessage = "AI助手已就绪！";
            }
        }
        
        private async void StartSetup()
        {
            isProcessing = true;
            statusMessage = "正在初始化设置...";
            currentStep = 0;
            progress = 0f;
            
            try
            {
                // 执行实际的设置步骤
                await PerformSetupSteps();
                
                setupCompleted = true;
                statusMessage = "设置完成！AI助手已就绪。";
                
                EditorUtility.DisplayDialog("设置完成", "AI助手设置已成功完成！\n\n您现在可以开始使用AI助手了。", "确定");
            }
            catch (Exception e)
            {
                Debug.LogError($"设置过程中出现错误: {e.Message}");
                statusMessage = $"设置失败: {e.Message}";
                progress = -1; // 表示错误状态
                
                EditorUtility.DisplayDialog("设置失败", $"设置过程中出现错误:\n{e.Message}\n\n请检查日志获取更多信息。", "确定");
            }
            finally
            {
                isProcessing = false;
                Repaint();
            }
        }
        
        private async Task PerformSetupSteps()
        {
            for (int i = 0; i < setupSteps.Length; i++)
            {
                currentStep = i;
                statusMessage = $"正在执行: {setupSteps[i]}";
                progress = (float)i / setupSteps.Length;
                
                EditorApplication.delayCall += () => Repaint();
                
                // 模拟步骤执行时间
                await Task.Delay(1000);
                
                // 在这里可以添加实际的设置逻辑
                // 例如: await ExecuteSetupStep(i);
            }
            
            currentStep = setupSteps.Length;
            progress = 1f;
        }
        
        private void CancelSetup()
        {
            if (isProcessing)
            {
                isProcessing = false;
                statusMessage = "设置已取消";
                
                EditorUtility.DisplayDialog("设置取消", "设置过程已被用户取消。", "确定");
                Repaint();
            }
        }
        
        private void ResetSetup()
        {
            if (EditorUtility.DisplayDialog("重新设置", "确定要重新开始设置过程吗？\n\n这将清除所有当前的设置进度。", "确定", "取消"))
            {
                currentStep = 0;
                setupCompleted = false;
                isProcessing = false;
                statusMessage = "";
                progress = 0f;
                
                Debug.Log("设置已重置");
                Repaint();
            }
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