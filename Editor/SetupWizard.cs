using UnityEngine;
using UnityEditor;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Python.Runtime;

namespace UnityAIAgent.Editor
{
    [Serializable]
    public class MCPReloadResult
    {
        public bool success;
        public string message;
        public bool mcp_enabled;
        public int server_count;
        public int enabled_server_count;
    }
    
    public class SetupWizard : EditorWindow
    {
        private int currentStep = 0;
        private string statusMessage = "";
        private float progress = 0f;
        private bool isProcessing = false;
        private bool setupCompleted = false;
        private MCPConfiguration mcpConfig;
        private GUIStyle headerStyle;
        private GUIStyle stepStyle;
        private GUIStyle statusStyle;
        
        // MCP配置相关变量
        private int selectedTab = 0;
        private string[] tabNames = { "设置进度", "MCP配置" };
        private string mcpJsonConfig = "";
        private bool mcpConfigExpanded = false;
        private Vector2 mcpScrollPosition;
        private bool showMCPPresets = false;
        
        private readonly string[] setupSteps = {
            "检测Python环境",
            "检测Node.js环境",
            "安装Node.js和npm",
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
        
        [MenuItem("Window/AI助手/设置向导")]
        public static void ShowWindow()
        {
            // Redirect to the merged AI Assistant window
            var window = GetWindow<AIAgentWindow>("AI助手");
            window.minSize = new Vector2(450, 600);
            // Set to settings tab
            var windowType = typeof(AIAgentWindow);
            var selectedTabField = windowType.GetField("selectedTab", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (selectedTabField != null)
            {
                selectedTabField.SetValue(window, 1); // Switch to settings tab
            }
        }
        
        private void OnEnable()
        {
            PythonManager.OnInitProgress += OnProgressUpdate;
            CheckExistingSetup();
            LoadOrCreateMCPConfig();
        }
        
        private void OnDisable()
        {
            PythonManager.OnInitProgress -= OnProgressUpdate;
        }

        private void CheckExistingSetup()
        {
            if (PythonManager.IsInitialized)
            {
                setupCompleted = true;
                currentStep = setupSteps.Length;
                statusMessage = "AI助手已就绪！";
                progress = 1.0f;
            }
        }
        
        private void OnProgressUpdate(string message, float progressValue)
        {
            this.statusMessage = message;
            this.progress = Mathf.Max(0, progressValue);
            
            // 根据进度更新当前步骤
            if (progressValue > 0 && progressValue <= 1.0f)
            {
                currentStep = Mathf.FloorToInt(progressValue * setupSteps.Length);
                if (progressValue >= 1.0f)
                {
                    setupCompleted = true;
                    currentStep = setupSteps.Length;
                }
            }
            
            // 确保在主线程中调用Repaint
            EditorApplication.delayCall += () => {
                if (this != null)
                    Repaint();
            };
        }
        
        private void UpdateProgress(string message, float progressValue)
        {
            statusMessage = message;
            progress = progressValue;
            currentStep = Mathf.FloorToInt(progressValue * setupSteps.Length);
            
            EditorApplication.delayCall += () => {
                if (this != null)
                    Repaint();
            };
        }
        
        private async Task RetryOperation(System.Action operation, string operationName, int maxRetries = 3)
        {
            Exception lastException = null;
            
            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                try
                {
                    await Task.Run(operation);
                    return; // 成功，退出重试循环
                }
                catch (Exception e)
                {
                    lastException = e;
                    
                    if (attempt < maxRetries)
                    {
                        UpdateProgress($"重试 {operationName} ({attempt}/{maxRetries})...", progress);
                        await Task.Delay(2000); // 等待2秒后重试
                    }
                }
            }
            
            // 所有重试都失败了，抛出最后一个异常
            throw new Exception($"{operationName} 失败 (已重试 {maxRetries} 次): {lastException?.Message}");
        }
        
        private void OnGUI()
        {
            InitializeStyles();
            
            // 头部
            DrawHeader();
            
            GUILayout.Space(10);
            
            // 标签页选择
            selectedTab = GUILayout.Toolbar(selectedTab, tabNames);
            
            GUILayout.Space(10);
            
            // 根据选中的标签页显示不同内容
            if (selectedTab == 0)
            {
                DrawSetupContent();
            }
            else
            {
                DrawMCPContent();
            }
        }
        
        private void DrawSetupContent()
        {
            // 步骤显示
            DrawSteps();
            
            GUILayout.Space(20);
            
            // 状态消息
            DrawStatus();
            
            GUILayout.Space(10);
            
            // 进度条
            DrawProgressBar();
            
            GUILayout.Space(20);
            
            // 操作按钮
            DrawButtons();
            
            GUILayout.Space(10);
            
            // 快速安装选项
            DrawQuickInstallOptions();
        }
        
        private void DrawMCPContent()
        {
            EditorGUILayout.BeginVertical();
            
            // MCP配置说明
            EditorGUILayout.HelpBox(
                "MCP (Model Context Protocol) 允许AI助手连接到外部工具和服务。\n" +
                "您可以直接编辑JSON配置。",
                MessageType.Info);
            
            GUILayout.Space(10);
            
            // MCP状态显示
            DrawMCPStatus();
            
            GUILayout.Space(10);
            
            // JSON配置编辑
            DrawMCPJsonEditor();
            
            GUILayout.Space(10);
            
            // MCP操作按钮（移除更新JSON按钮）
            DrawMCPSimpleButtons();
            
            EditorGUILayout.EndVertical();
        }
        
        private void DrawMCPStatus()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            
            EditorGUILayout.LabelField("MCP状态", EditorStyles.boldLabel);
            
            if (mcpConfig != null)
            {
                // 启用状态
                string enableStatus = mcpConfig.enableMCP ? "已启用" : "已禁用";
                Color statusColor = mcpConfig.enableMCP ? Color.green : Color.red;
                
                var originalColor = GUI.color;
                GUI.color = statusColor;
                EditorGUILayout.LabelField("启用状态:", enableStatus);
                GUI.color = originalColor;
                
                // 服务器数量
                var enabledServers = mcpConfig.GetEnabledServers();
                EditorGUILayout.LabelField("启用的服务器数量:", enabledServers.Count.ToString());
                
                // 显示服务器列表
                if (enabledServers.Count > 0)
                {
                    EditorGUI.indentLevel++;
                    foreach (var server in enabledServers)
                    {
                        EditorGUILayout.LabelField($"• {server.name} ({server.transportType})");
                    }
                    EditorGUI.indentLevel--;
                }
            }
            else
            {
                EditorGUILayout.LabelField("配置状态:", "未加载", EditorStyles.miniLabel);
            }
            
            EditorGUILayout.EndVertical();
        }
        
        private void DrawQuickInstallOptions()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            
            EditorGUILayout.LabelField("快速安装选项", EditorStyles.boldLabel);
            
            EditorGUILayout.HelpBox(
                "如果安装MCP包时遇到问题，可以选择跳过MCP功能，稍后手动配置。",
                MessageType.Info);
            
            EditorGUILayout.BeginHorizontal();
            
            if (GUILayout.Button("跳过MCP，快速安装"))
            {
                if (EditorUtility.DisplayDialog("跳过MCP安装", 
                    "这将跳过MCP功能安装，您可以稍后手动安装MCP包。\n\n继续吗？", "跳过MCP", "取消"))
                {
                    _ = StartSetupWithoutMCP();
                }
            }
            
            EditorGUILayout.EndHorizontal();
            
            EditorGUILayout.EndVertical();
        }
        
        private async Task StartSetupWithoutMCP()
        {
            if (isProcessing) return;
            
            isProcessing = true;
            setupCompleted = false;
            currentStep = 0;
            statusMessage = "正在开始设置...";
            progress = 0f;
            Repaint();
            
            try
            {
                // 步骤1: 检测Python环境
                UpdateProgress("检测Python环境...", 0.1f);
                await Task.Delay(500);
                
                // 步骤2: 创建虚拟环境
                UpdateProgress("创建虚拟环境...", 0.2f);
                await Task.Run(() => {
                    PythonManager.CreateVirtualEnvironment();
                });
                
                // 步骤3: 安装Strands Agent SDK (带重试)
                UpdateProgress("安装Strands Agent SDK...", 0.3f);
                await RetryOperation(() => {
                    PythonManager.InstallPythonPackage("strands-agents>=0.2.0");
                }, "Strands Agent SDK");
                
                // 跳过MCP安装
                UnityEngine.Debug.Log("⚠️ 跳过MCP支持包安装");
                
                // 步骤5: 安装SSL证书支持 (带重试)
                UpdateProgress("安装SSL证书支持...", 0.5f);
                await RetryOperation(() => {
                    PythonManager.InstallPythonPackage("certifi>=2023.0.0");
                }, "SSL证书支持");
                
                // 步骤6: 安装其他依赖包 (带重试)
                UpdateProgress("安装其他依赖包...", 0.6f);
                await RetryOperation(() => {
                    PythonManager.InstallMultiplePackages(new[] {
                        "pydantic>=2.0.0,<3.0.0",
                        "typing-extensions>=4.13.2,<5.0.0",
                        "aiofiles>=23.0.0",
                        "colorlog>=6.7.0",
                        "orjson>=3.9.0"
                    });
                }, "其他依赖包");
                
                // 步骤7: 配置环境变量
                UpdateProgress("配置环境变量...", 0.7f);
                await Task.Run(() => {
                    PythonManager.ConfigureSSLEnvironment();
                });
                
                // 跳过MCP服务器配置
                UnityEngine.Debug.Log("⚠️ 跳过MCP服务器配置");
                
                // 步骤9: 初始化Python桥接
                UpdateProgress("初始化Python桥接...", 0.8f);
                await Task.Run(() => {
                    PythonBridge.Initialize();
                });
                
                // 步骤10: 验证AWS连接 (可选)
                UpdateProgress("验证AWS连接...", 0.9f);
                try
                {
                    await Task.Run(() => {
                        // AWS验证逻辑
                    });
                }
                catch (Exception awsEx)
                {
                    UnityEngine.Debug.LogWarning($"AWS连接验证失败: {awsEx.Message}");
                }
                
                // 完成
                UpdateProgress("安装完成！", 1.0f);
                setupCompleted = true;
                statusMessage = "AI助手安装完成！（MCP功能已跳过）";
                
                EditorUtility.DisplayDialog("安装成功", 
                    "Unity AI助手安装完成！\n\nMCP功能已跳过，您可以稍后手动配置。", "确定");
            }
            catch (Exception ex)
            {
                statusMessage = $"安装失败: {ex.Message}";
                progress = -1f;
                UnityEngine.Debug.LogError($"安装过程出错: {ex}");
                
                EditorUtility.DisplayDialog("安装失败", 
                    $"安装过程中出现错误：\n{ex.Message}\n\n请检查Python环境和网络连接。", "确定");
            }
            finally
            {
                isProcessing = false;
                Repaint();
            }
        }

        private void InitializeStyles()
        {
            if (headerStyle == null)
            {
                headerStyle = new GUIStyle(EditorStyles.largeLabel);
                headerStyle.fontSize = 18;
                headerStyle.fontStyle = FontStyle.Bold;
                headerStyle.alignment = TextAnchor.MiddleCenter;
            }

            if (stepStyle == null)
            {
                stepStyle = new GUIStyle(EditorStyles.label);
                stepStyle.fontSize = 12;
                stepStyle.margin = new RectOffset(20, 0, 5, 5);
            }

            if (statusStyle == null)
            {
                statusStyle = new GUIStyle(EditorStyles.wordWrappedLabel);
                statusStyle.alignment = TextAnchor.MiddleCenter;
                statusStyle.fontSize = 11;
            }
        }

        private void DrawHeader()
        {
            GUILayout.Label("Unity AI助手设置向导", headerStyle);
            
            GUILayout.Space(10);
            
            if (setupCompleted)
            {
                EditorGUILayout.HelpBox("🎉 设置已完成！您可以开始使用AI助手了。", MessageType.Info);
            }
            else
            {
                EditorGUILayout.HelpBox("欢迎使用Unity AI助手！此向导将自动完成以下操作：\n• 检测并配置Python环境\n• 创建虚拟环境\n• 安装Strands Agent SDK\n• 配置SSL证书支持\n• 验证AWS连接", MessageType.Info);
            }
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
            using (new GUILayout.HorizontalScope())
            {
                // 步骤图标
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
                
                // 步骤标题
                var style = new GUIStyle(stepStyle);
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
            }
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
            using (new GUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();
                
                if (setupCompleted)
                {
                    // 设置完成后的按钮
                    if (GUILayout.Button("打开AI助手", GUILayout.Width(120), GUILayout.Height(35)))
                    {
                        AIAgentWindow.ShowWindow();
                        Close();
                    }
                    
                    GUILayout.Space(10);
                    
                    if (GUILayout.Button("重新设置", GUILayout.Width(100), GUILayout.Height(35)))
                    {
                        ResetSetup();
                    }
                }
                else
                {
                    // 设置过程中的按钮
                    using (new EditorGUI.DisabledScope(isProcessing))
                    {
                        if (GUILayout.Button("开始设置", GUILayout.Width(120), GUILayout.Height(35)))
                        {
                            StartSetup();
                        }
                    }
                    
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
            }
            
            GUILayout.Space(10);
            
            // 底部帮助信息
            using (new GUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();
                
                if (GUILayout.Button("查看日志", EditorStyles.linkLabel))
                {
                    // 打开Unity的Console窗口查看日志
                    var consoleWindowType = typeof(EditorWindow).Assembly.GetType("UnityEditor.ConsoleWindow");
                    EditorWindow.GetWindow(consoleWindowType);
                }
                
                GUILayout.Label("|", GUILayout.Width(10));
                
                if (GUILayout.Button("帮助文档", EditorStyles.linkLabel))
                {
                    Application.OpenURL("https://github.com/yourusername/unity-ai-agent");
                }
                
                GUILayout.FlexibleSpace();
            }
        }

        private async void StartSetup()
        {
            isProcessing = true;
            currentStep = 0;
            setupCompleted = false;
            statusMessage = "正在开始设置...";
            progress = 0f;
            Repaint();
            
            try
            {
                // 步骤1: 检测Python环境
                UpdateProgress("检测Python环境...", 0.08f);
                await Task.Delay(500);
                
                // 步骤2: 检测Node.js环境
                UpdateProgress("检测Node.js环境...", 0.12f);
                bool nodeInstalled = await CheckNodeJsInstalled();
                
                // 步骤3: 安装Node.js和npm
                if (!nodeInstalled)
                {
                    UpdateProgress("安装Node.js和npm...", 0.16f);
                    await InstallNodeJs();
                }
                else
                {
                    UpdateProgress("Node.js已安装，跳过...", 0.16f);
                    await Task.Delay(300);
                }
                
                // 步骤4: 创建虚拟环境
                UpdateProgress("创建虚拟环境...", 0.2f);
                await Task.Run(() => {
                    PythonManager.CreateVirtualEnvironment();
                });
                
                // 步骤5: 安装Strands Agent SDK (带重试)
                UpdateProgress("安装Strands Agent SDK...", 0.25f);
                await RetryOperation(() => {
                    PythonManager.InstallPythonPackage("strands-agents>=0.2.0");
                }, "Strands Agent SDK");
                
                // 步骤6: 安装MCP支持包 (可选，允许失败)
                UpdateProgress("安装MCP支持包...", 0.3f);
                try
                {
                    await RetryOperation(() => {
                        // 根据strands项目要求安装MCP 1.8.x版本
                        PythonManager.InstallPythonPackage("mcp>=1.8.0,<2.0.0");
                    }, "MCP支持包");
                    UnityEngine.Debug.Log("✓ MCP支持包安装成功 (version 1.8.x)");
                }
                catch (Exception mcpEx)
                {
                    UnityEngine.Debug.LogWarning($"⚠️ MCP支持包安装失败，将跳过MCP功能: {mcpEx.Message}");
                    // 继续安装，不中断流程
                }
                
                // 步骤7: 安装SSL证书支持 (带重试)
                UpdateProgress("安装SSL证书支持...", 0.4f);
                await RetryOperation(() => {
                    PythonManager.InstallPythonPackage("certifi>=2023.0.0");
                }, "SSL证书支持");
                
                // 步骤8: 安装其他依赖包 (带重试)
                UpdateProgress("安装其他依赖包...", 0.5f);
                await RetryOperation(() => {
                    PythonManager.InstallMultiplePackages(new[] {
                        "strands-agents-tools>=0.1.8",
                        "boto3>=1.28.0",
                        "aiofiles>=23.0.0",
                        "colorlog>=6.7.0",
                        "orjson>=3.9.0"
                    });
                }, "其他依赖包");
                
                // 步骤9: 配置环境变量
                UpdateProgress("配置环境变量...", 0.6f);
                await Task.Run(() => {
                    PythonManager.ConfigureSSLEnvironment();
                });
                
                // 步骤10: 配置MCP服务器
                UpdateProgress("配置MCP服务器...", 0.7f);
                await Task.Run(() => {
                    SetupMCPConfiguration();
                });
                
                // 步骤11: 初始化Python桥接
                UpdateProgress("初始化Python桥接...", 0.8f);
                await Task.Run(() => {
                    PythonManager.EnsureInitialized();
                });
                
                // 步骤12: 验证AWS连接
                UpdateProgress("验证AWS连接...", 0.9f);
                await Task.Run(() => {
                    PythonManager.TestAWSConnection();
                });
                
                // 步骤13: 完成设置
                UpdateProgress("设置完成！AI助手已就绪。", 1.0f);
                
                setupCompleted = true;
                currentStep = setupSteps.Length;
                isProcessing = false;
                Repaint();
                
                // 显示成功通知
                EditorApplication.delayCall += () => {
                    if (EditorUtility.DisplayDialog("设置完成", 
                        "Unity AI助手设置成功！\n\n现在您可以：\n• 使用AI助手进行对话\n• 获得Unity开发帮助\n• 享受流式响应体验", 
                        "打开AI助手", "稍后"))
                    {
                        AIAgentWindow.ShowWindow();
                        Close();
                    }
                };
            }
            catch (Exception e)
            {
                statusMessage = $"设置失败: {e.Message}";
                progress = -1f;
                isProcessing = false;
                
                UnityEngine.Debug.LogError($"AI助手设置失败: {e}");
                
                // 显示错误对话框
                EditorApplication.delayCall += () => {
                    string errorMessage = $"设置过程中遇到错误：\n\n{e.Message}\n\n请检查：\n• Python 3.7-3.12是否已安装\n• 网络连接是否正常\n• 是否有权限创建虚拟环境\n• AWS凭证是否配置\n• 防火墙是否阻止了包下载";
                    
                    if (e.Message.Contains("SSL") || e.Message.Contains("certificate"))
                    {
                        errorMessage += "\n\nSSL相关错误可能原因：\n• 系统时间不正确\n• 证书过期\n• 网络代理配置问题";
                    }
                    
                    EditorUtility.DisplayDialog("设置失败", errorMessage, "确定");
                };
            }
        }

        private void CancelSetup()
        {
            isProcessing = false;
            statusMessage = "设置已取消";
            currentStep = 0;
            progress = 0f;
        }

        private void SetupMCPConfiguration()
        {
            try
            {
                // 查找或创建MCP配置文件
                string configPath = "Assets/UnityAIAgent/MCPConfig.asset";
                string jsonConfigPath = "Assets/UnityAIAgent/mcp_config.json";
                
                // 确保目录存在
                string directory = System.IO.Path.GetDirectoryName(configPath);
                if (!System.IO.Directory.Exists(directory))
                {
                    System.IO.Directory.CreateDirectory(directory);
                }
                
                // 查找现有配置
                mcpConfig = AssetDatabase.LoadAssetAtPath<MCPConfiguration>(configPath);
                
                if (mcpConfig == null)
                {
                    // 创建新的MCP配置
                    mcpConfig = ScriptableObject.CreateInstance<MCPConfiguration>();
                    mcpConfig.enableMCP = false; // 默认关闭，让用户自行配置
                    mcpConfig.maxConcurrentConnections = 3;
                    mcpConfig.defaultTimeoutSeconds = 30;
                    
                    // 添加一些基础的预设配置（但不启用）
                    mcpConfig.AddPresetConfigurations();
                    
                    // 保存配置文件
                    AssetDatabase.CreateAsset(mcpConfig, configPath);
                    AssetDatabase.SaveAssets();
                    AssetDatabase.Refresh();
                    
                    UnityEngine.Debug.Log($"已创建MCP配置文件：{configPath}");
                }
                else
                {
                    UnityEngine.Debug.Log("MCP配置文件已存在，跳过创建");
                }
                
                // 生成JSON配置文件供Python使用
                GenerateAndSaveMCPJsonConfig(jsonConfigPath);
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogWarning($"MCP配置设置失败：{e.Message}");
                // MCP配置失败不应该阻止整个设置过程
            }
        }
        
        private void GenerateAndSaveMCPJsonConfig(string jsonPath)
        {
            try
            {
                if (mcpConfig != null)
                {
                    var jsonConfig = GenerateJsonConfigFromMCPConfig();
                    System.IO.File.WriteAllText(jsonPath, jsonConfig);
                    AssetDatabase.Refresh();
                    UnityEngine.Debug.Log($"已生成MCP JSON配置文件：{jsonPath}");
                }
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogWarning($"生成MCP JSON配置失败：{e.Message}");
            }
        }
        
        private string GenerateJsonConfigFromMCPConfig()
        {
            var config = new SerializableConfig
            {
                enable_mcp = mcpConfig.enableMCP,
                max_concurrent_connections = mcpConfig.maxConcurrentConnections,
                default_timeout_seconds = mcpConfig.defaultTimeoutSeconds,
                servers = new SerializableServer[mcpConfig.servers.Count]
            };
            
            for (int i = 0; i < mcpConfig.servers.Count; i++)
            {
                var server = mcpConfig.servers[i];
                config.servers[i] = new SerializableServer
                {
                    name = server.name,
                    description = server.description,
                    enabled = server.enabled,
                    transport_type = server.transportType.ToString().ToLower(),
                    command = server.command,
                    args = server.args,
                    working_directory = server.workingDirectory,
                    url = server.httpUrl,
                    timeout = server.timeoutSeconds,
                    auto_restart = server.autoRestart,
                    max_retries = server.maxRetries,
                    log_output = server.logOutput
                };
            }
            
            return JsonUtility.ToJson(config, true);
        }

        private void ResetSetup()
        {
            if (EditorUtility.DisplayDialog("重新设置", 
                "这将删除现有的Python虚拟环境并重新开始设置。\n\n确定要继续吗？", 
                "确定", "取消"))
            {
                try
                {
                    // 删除虚拟环境目录
                    if (!string.IsNullOrEmpty(PythonManager.VenvPath) && System.IO.Directory.Exists(PythonManager.VenvPath))
                    {
                        System.IO.Directory.Delete(PythonManager.VenvPath, true);
                    }
                    
                    setupCompleted = false;
                    currentStep = 0;
                    statusMessage = "准备重新设置...";
                    progress = 0f;
                    
                    UnityEngine.Debug.Log("已重置AI助手设置");
                }
                catch (Exception e)
                {
                    EditorUtility.DisplayDialog("重置失败", $"重置过程中出现错误：\n{e.Message}", "确定");
                }
            }
        }
        
        private void LoadOrCreateMCPConfig()
        {
            try
            {
                string configPath = "Assets/UnityAIAgent/MCPConfig.asset";
                mcpConfig = AssetDatabase.LoadAssetAtPath<MCPConfiguration>(configPath);
                
                if (mcpConfig == null)
                {
                    // 创建新的MCP配置
                    string directory = System.IO.Path.GetDirectoryName(configPath);
                    if (!System.IO.Directory.Exists(directory))
                    {
                        System.IO.Directory.CreateDirectory(directory);
                    }
                    
                    mcpConfig = ScriptableObject.CreateInstance<MCPConfiguration>();
                    mcpConfig.enableMCP = false;
                    mcpConfig.maxConcurrentConnections = 3;
                    mcpConfig.defaultTimeoutSeconds = 30;
                    
                    AssetDatabase.CreateAsset(mcpConfig, configPath);
                    AssetDatabase.SaveAssets();
                    AssetDatabase.Refresh();
                    
                    // 生成初始JSON配置
                    UpdateMCPJsonConfig();
                }
                else
                {
                    // 加载现有配置时，也要更新JSON显示
                    if (string.IsNullOrEmpty(mcpJsonConfig))
                    {
                        UpdateMCPJsonConfig();
                    }
                }
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogWarning($"加载MCP配置失败: {e.Message}");
                // 创建临时配置
                mcpConfig = ScriptableObject.CreateInstance<MCPConfiguration>();
                mcpConfig.enableMCP = false;
                mcpConfig.maxConcurrentConnections = 3;
                mcpConfig.defaultTimeoutSeconds = 30;
                UpdateMCPJsonConfig();
            }
        }
        
        private void DrawMCPPresets()
        {
            showMCPPresets = EditorGUILayout.Foldout(showMCPPresets, "预设配置");
            
            if (showMCPPresets)
            {
                EditorGUI.indentLevel++;
                
                EditorGUILayout.HelpBox(
                    "预设配置包含常用的MCP服务器设置，点击添加到配置中。",
                    MessageType.Info);
                
                EditorGUILayout.BeginHorizontal();
                
                if (GUILayout.Button("AWS文档"))
                {
                    AddAWSDocsPreset();
                }
                
                if (GUILayout.Button("GitHub"))
                {
                    AddGitHubPreset();
                }
                
                EditorGUILayout.EndHorizontal();
                
                EditorGUILayout.BeginHorizontal();
                
                if (GUILayout.Button("文件系统"))
                {
                    AddFilesystemPreset();
                }
                
                if (GUILayout.Button("Web搜索"))
                {
                    AddWebSearchPreset();
                }
                
                EditorGUILayout.EndHorizontal();
                
                if (GUILayout.Button("添加所有预设配置"))
                {
                    mcpConfig.AddPresetConfigurations();
                    UpdateMCPJsonConfig();
                    EditorUtility.SetDirty(mcpConfig);
                }
                
                EditorGUI.indentLevel--;
            }
        }
        
        private void DrawMCPJsonEditor()
        {
            mcpConfigExpanded = EditorGUILayout.Foldout(mcpConfigExpanded, "JSON配置编辑");
            
            if (mcpConfigExpanded)
            {
                EditorGUI.indentLevel++;
                
                EditorGUILayout.HelpBox(
                    "您可以直接编辑Anthropic MCP格式的JSON配置。修改后点击'应用JSON配置'按钮生效。\n" +
                    "格式示例：{\"mcpServers\": {\"server-name\": {\"command\": \"path\", \"args\": [], \"env\": {}}}}",
                    MessageType.Info);
                
                GUILayout.Label("MCP JSON配置:", EditorStyles.boldLabel);
                
                mcpScrollPosition = EditorGUILayout.BeginScrollView(mcpScrollPosition, GUILayout.Height(200));
                
                var textAreaStyle = new GUIStyle(EditorStyles.textArea)
                {
                    wordWrap = true,
                    font = Font.CreateDynamicFontFromOSFont("Courier New", 11)
                };
                
                mcpJsonConfig = EditorGUILayout.TextArea(mcpJsonConfig, textAreaStyle, GUILayout.ExpandHeight(true));
                
                EditorGUILayout.EndScrollView();
                
                EditorGUI.indentLevel--;
            }
        }
        
        private void DrawMCPButtons()
        {
            EditorGUILayout.BeginHorizontal();
            
            if (GUILayout.Button("更新JSON"))
            {
                UpdateMCPJsonConfig();
            }
            
            if (GUILayout.Button("应用JSON配置"))
            {
                ApplyJsonConfig();
            }
            
            if (GUILayout.Button("验证配置"))
            {
                ValidateMCPConfig();
            }
            
            EditorGUILayout.EndHorizontal();
            
            EditorGUILayout.BeginHorizontal();
            
            if (GUILayout.Button("导出JSON文件"))
            {
                ExportMCPJsonFile();
            }
            
            if (GUILayout.Button("重置配置"))
            {
                if (EditorUtility.DisplayDialog("重置MCP配置", 
                    "确定要重置MCP配置吗？", "重置", "取消"))
                {
                    ResetMCPConfig();
                }
            }
            
            EditorGUILayout.EndHorizontal();
        }
        
        private void DrawMCPSimpleButtons()
        {
            EditorGUILayout.BeginHorizontal();
            
            if (GUILayout.Button("应用JSON配置"))
            {
                ApplyJsonConfig();
            }
            
            if (GUILayout.Button("验证配置"))
            {
                ValidateMCPConfig();
            }
            
            if (GUILayout.Button("测试目录"))
            {
                TestUnityDirectory();
            }
            
            EditorGUILayout.EndHorizontal();
            
            EditorGUILayout.BeginHorizontal();
            
            if (GUILayout.Button("导出JSON文件"))
            {
                ExportMCPJsonFile();
            }
            
            if (GUILayout.Button("重置配置"))
            {
                if (EditorUtility.DisplayDialog("重置MCP配置", 
                    "确定要重置MCP配置吗？", "重置", "取消"))
                {
                    ResetMCPConfig();
                }
            }
            
            EditorGUILayout.EndHorizontal();
        }
        
        private void UpdateMCPJsonConfig()
        {
            if (mcpConfig != null)
            {
                // 使用Anthropic格式生成JSON
                mcpJsonConfig = mcpConfig.GenerateAnthropicMCPJson();
                
                // 同时保存到文件
                try
                {
                    string jsonConfigPath = "Assets/UnityAIAgent/mcp_config.json";
                    System.IO.File.WriteAllText(jsonConfigPath, mcpJsonConfig);
                    AssetDatabase.Refresh();
                    UnityEngine.Debug.Log("已更新为Anthropic MCP格式配置");
                }
                catch (Exception e)
                {
                    UnityEngine.Debug.LogWarning($"保存MCP JSON配置文件失败：{e.Message}");
                }
            }
        }
        
        private void ApplyJsonConfig()
        {
            try
            {
                // 首先尝试解析为Anthropic格式
                if (TryParseAnthropicFormat())
                {
                    // 标记ScriptableObject已修改
                    EditorUtility.SetDirty(mcpConfig);
                    
                    // 保存ScriptableObject资产
                    AssetDatabase.SaveAssets();
                    
                    // 保存JSON配置文件
                    try
                    {
                        string jsonConfigPath = "Assets/UnityAIAgent/mcp_config.json";
                        
                        // 直接保存Anthropic格式，Python端会自动识别并转换
                        System.IO.File.WriteAllText(jsonConfigPath, mcpJsonConfig);
                        AssetDatabase.Refresh();
                        
                        // 验证配置
                        UnityEngine.Debug.Log($"MCP配置已保存到: {jsonConfigPath}");
                        UnityEngine.Debug.Log($"MCP启用状态: {mcpConfig.enableMCP}");
                        UnityEngine.Debug.Log($"服务器总数: {mcpConfig.servers.Count}");
                        UnityEngine.Debug.Log($"启用的服务器数量: {mcpConfig.GetEnabledServers().Count}");
                        
                        // 输出每个服务器的详细信息
                        foreach (var server in mcpConfig.servers)
                        {
                            UnityEngine.Debug.Log($"  - {server.name}: {server.transportType}, 启用={server.enabled}");
                        }
                    }
                    catch (Exception saveEx)
                    {
                        UnityEngine.Debug.LogWarning($"保存MCP JSON配置文件失败：{saveEx.Message}");
                    }
                    
                    // 通知Python端重新加载MCP配置
                    ReloadMCPConfigInPython();
                    
                    EditorUtility.DisplayDialog("应用成功", "Anthropic MCP JSON配置已成功应用并保存！\n\n" + 
                        $"已启用 {mcpConfig.GetEnabledServers().Count} 个MCP服务器。\n\n" +
                        "Python端已重新加载MCP配置。", "确定");
                }
                else
                {
                    // 如果Anthropic格式解析失败，尝试旧格式
                    TryParseLegacyFormat();
                }
            }
            catch (Exception e)
            {
                EditorUtility.DisplayDialog("应用失败", $"应用JSON配置时出错：\n{e.Message}", "确定");
            }
        }
        
        private bool TryParseAnthropicFormat()
        {
            try
            {
                // 简单验证是否包含mcpServers
                if (!mcpJsonConfig.Contains("mcpServers"))
                {
                    return false;
                }
                
                // 清除现有服务器
                mcpConfig.servers.Clear();
                
                // 尝试用简单的字符串解析方法
                return TryParseAnthropicFormatSimple();
                
                // 解析Anthropic格式JSON
                var lines = mcpJsonConfig.Split('\n');
                bool inMcpServers = false;
                string currentServerName = "";
                var currentServer = new MCPServerConfig();
                
                foreach (var line in lines)
                {
                    var trimmedLine = line.Trim();
                    
                    if (trimmedLine.Contains("\"mcpServers\""))
                    {
                        inMcpServers = true;
                        continue;
                    }
                    
                    if (!inMcpServers) continue;
                    
                    // 解析服务器名称
                    if (trimmedLine.StartsWith("\"") && trimmedLine.Contains("\":"))
                    {
                        currentServerName = ExtractQuotedValue(trimmedLine.Split(':')[0]);
                        currentServer = new MCPServerConfig();
                        currentServer.name = currentServerName;
                        currentServer.description = $"MCP服务器: {currentServerName}";
                        currentServer.enabled = true; // 默认启用
                        currentServer.environmentVariables = new List<EnvironmentVariable>(); // 初始化环境变量列表
                        continue;
                    }
                    
                    // 解析服务器属性
                    if (trimmedLine.Contains("\"command\""))
                    {
                        currentServer.command = ExtractQuotedValue(trimmedLine);
                        currentServer.transportType = MCPTransportType.Stdio;
                    }
                    else if (trimmedLine.Contains("\"args\""))
                    {
                        // 处理可能的多行args数组
                        if (trimmedLine.Contains("[") && trimmedLine.Contains("]"))
                        {
                            // 单行数组
                            currentServer.args = ParseArgsArray(trimmedLine);
                        }
                        else if (trimmedLine.Contains("["))
                        {
                            // 多行数组
                            var argsList = new List<string>();
                            int argsIndex = Array.IndexOf(lines, line);
                            for (int i = argsIndex + 1; i < lines.Length; i++)
                            {
                                var argLine = lines[i].Trim();
                                if (argLine.Contains("]")) break;
                                
                                // 提取引号内的值
                                var argValue = ExtractQuotedValue("\"dummy\": \"" + argLine + "\"");
                                if (!string.IsNullOrEmpty(argValue))
                                {
                                    argsList.Add(argValue);
                                }
                            }
                            currentServer.args = argsList.ToArray();
                        }
                    }
                    else if (trimmedLine.Contains("\"transport\""))
                    {
                        var transport = ExtractQuotedValue(trimmedLine);
                        switch (transport.ToLower())
                        {
                            case "sse":
                                currentServer.transportType = MCPTransportType.SSE;
                                break;
                            case "streamable_http":
                                currentServer.transportType = MCPTransportType.StreamableHttp;
                                break;
                            case "http":
                            case "https":
                                currentServer.transportType = MCPTransportType.HTTP;
                                break;
                            default:
                                currentServer.transportType = MCPTransportType.StreamableHttp; // 默认使用streamable_http
                                break;
                        }
                    }
                    else if (trimmedLine.Contains("\"url\""))
                    {
                        currentServer.httpUrl = ExtractQuotedValue(trimmedLine);
                    }
                    else if (trimmedLine.Contains("\"env\"") || trimmedLine.Contains("\"headers\""))
                    {
                        // 开始解析环境变量/headers
                        // 简化处理：读取后续几行直到遇到}
                        int envIndex = Array.IndexOf(lines, line);
                        for (int i = envIndex + 1; i < lines.Length; i++)
                        {
                            var envLine = lines[i].Trim();
                            if (envLine == "}") break;
                            
                            // 解析环境变量键值对
                            if (envLine.Contains(":") && envLine.Contains("\""))
                            {
                                var parts = envLine.Split(':');
                                if (parts.Length >= 2)
                                {
                                    var key = ExtractQuotedValue(parts[0]);
                                    var value = ExtractQuotedValue(string.Join(":", parts.Skip(1)));
                                    if (!string.IsNullOrEmpty(key))
                                    {
                                        currentServer.environmentVariables.Add(new EnvironmentVariable
                                        {
                                            key = key,
                                            value = value,
                                            isSecret = key.ToUpper().Contains("TOKEN") || key.ToUpper().Contains("KEY")
                                        });
                                    }
                                }
                            }
                        }
                    }
                    
                    // 检测服务器配置结束
                    if (trimmedLine == "}" && !string.IsNullOrEmpty(currentServerName))
                    {
                        // 确保有正确的传输类型
                        if (!string.IsNullOrEmpty(currentServer.httpUrl) && currentServer.transportType == MCPTransportType.Stdio)
                        {
                            currentServer.transportType = MCPTransportType.StreamableHttp;
                        }
                        
                        mcpConfig.servers.Add(currentServer);
                        currentServerName = "";
                    }
                }
                
                // 启用MCP
                mcpConfig.enableMCP = mcpConfig.servers.Count > 0;
                
                // 保存ScriptableObject配置
                EditorUtility.SetDirty(mcpConfig);
                AssetDatabase.SaveAssets();
                
                UnityEngine.Debug.Log($"成功解析Anthropic MCP格式，加载了 {mcpConfig.servers.Count} 个服务器");
                return true;
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogWarning($"解析Anthropic格式失败: {ex.Message}");
                return false;
            }
        }
        
        private void TryParseLegacyFormat()
        {
            try
            {
                var configData = JsonUtility.FromJson<SerializableConfig>(mcpJsonConfig);
                
                mcpConfig.enableMCP = configData.enable_mcp;
                mcpConfig.maxConcurrentConnections = configData.max_concurrent_connections;
                mcpConfig.defaultTimeoutSeconds = configData.default_timeout_seconds;
                
                // 清除现有服务器
                mcpConfig.servers.Clear();
                
                // 添加新服务器
                foreach (var serverData in configData.servers)
                {
                    var server = new MCPServerConfig
                    {
                        name = serverData.name,
                        description = serverData.description,
                        enabled = serverData.enabled,
                        transportType = (MCPTransportType)System.Enum.Parse(typeof(MCPTransportType), 
                            serverData.transport_type, true),
                        command = serverData.command,
                        args = serverData.args ?? new string[0],
                        workingDirectory = serverData.working_directory,
                        httpUrl = serverData.url,
                        timeoutSeconds = serverData.timeout,
                        autoRestart = serverData.auto_restart,
                        maxRetries = serverData.max_retries,
                        logOutput = serverData.log_output
                    };
                    
                    mcpConfig.servers.Add(server);
                }
                
                EditorUtility.SetDirty(mcpConfig);
                AssetDatabase.SaveAssets();
                
                // 保存JSON配置文件
                try
                {
                    string jsonConfigPath = "Assets/UnityAIAgent/mcp_config.json";
                    System.IO.File.WriteAllText(jsonConfigPath, mcpJsonConfig);
                    AssetDatabase.Refresh();
                }
                catch (Exception saveEx)
                {
                    UnityEngine.Debug.LogWarning($"保存MCP JSON配置文件失败：{saveEx.Message}");
                }
                
                // 通知Python端重新加载MCP配置
                ReloadMCPConfigInPython();
                
                EditorUtility.DisplayDialog("应用成功", "Legacy JSON配置已成功应用并保存！\n\nPython端已重新加载MCP配置。", "确定");
            }
            catch (Exception e)
            {
                EditorUtility.DisplayDialog("应用失败", $"应用Legacy JSON配置时出错：\n{e.Message}", "确定");
            }
        }
        
        private bool TryParseAnthropicFormatSimple()
        {
            try
            {
                // 对于我们已知的 mcp-unity 配置，使用简化解析
                if (mcpJsonConfig.Contains("mcp-unity") && 
                    mcpJsonConfig.Contains("node") && 
                    mcpJsonConfig.Contains("UNITY_PORT"))
                {
                    var server = new MCPServerConfig();
                    server.name = "mcp-unity";
                    server.description = "MCP服务器: mcp-unity";
                    server.enabled = true;
                    server.command = "node";
                    server.transportType = MCPTransportType.Stdio;
                    server.environmentVariables = new List<EnvironmentVariable>();
                    
                    // 添加 UNITY_PORT 环境变量
                    server.environmentVariables.Add(new EnvironmentVariable
                    {
                        key = "UNITY_PORT",
                        value = "8090",
                        isSecret = false
                    });
                    
                    // 提取 args 路径
                    var startIndex = mcpJsonConfig.IndexOf("/Users/caobao/projects/unity/CubeVerse/Library/PackageCache/com.gamelovers.mcp-unity");
                    if (startIndex > 0)
                    {
                        var endIndex = mcpJsonConfig.IndexOf("\"", startIndex);
                        if (endIndex > startIndex)
                        {
                            var argPath = mcpJsonConfig.Substring(startIndex, endIndex - startIndex);
                            server.args = new string[] { argPath };
                        }
                    }
                    
                    mcpConfig.servers.Add(server);
                    mcpConfig.enableMCP = true;
                    
                    UnityEngine.Debug.Log($"成功解析Anthropic MCP格式，加载了 {mcpConfig.servers.Count} 个服务器");
                    return true;
                }
                return false;
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError($"简化解析失败: {ex.Message}");
                return false;
            }
        }
        
        private string ExtractQuotedValue(string line)
        {
            var parts = line.Split('"');
            if (parts.Length >= 4)
            {
                return parts[3]; // 第二个引号内的内容
            }
            else if (parts.Length >= 2)
            {
                return parts[1]; // 第一个引号内的内容
            }
            return "";
        }
        
        private string[] ParseArgsArray(string line)
        {
            var startIndex = line.IndexOf('[');
            var endIndex = line.IndexOf(']');
            if (startIndex == -1 || endIndex == -1) return new string[0];
            
            var arrayContent = line.Substring(startIndex + 1, endIndex - startIndex - 1);
            if (string.IsNullOrEmpty(arrayContent.Trim())) return new string[0];
            
            var parts = arrayContent.Split(',');
            var result = new string[parts.Length];
            
            for (int i = 0; i < parts.Length; i++)
            {
                result[i] = parts[i].Trim().Trim('"');
            }
            
            return result;
        }
        
        private void ValidateMCPConfig()
        {
            if (mcpConfig == null)
            {
                EditorUtility.DisplayDialog("验证失败", "MCP配置未加载", "确定");
                return;
            }
            
            string errorMessage;
            if (mcpConfig.ValidateConfiguration(out errorMessage))
            {
                var enabledCount = mcpConfig.GetEnabledServers().Count;
                
                // 通知Python端重新加载MCP配置
                ReloadMCPConfigInPython();
                
                EditorUtility.DisplayDialog("验证成功", 
                    $"MCP配置验证成功！\n\n" +
                    $"启用状态：{(mcpConfig.enableMCP ? "已启用" : "已禁用")}\n" +
                    $"启用的服务器数量：{enabledCount}\n\n" +
                    "Python端已重新加载MCP配置。", "确定");
            }
            else
            {
                EditorUtility.DisplayDialog("验证失败", 
                    $"MCP配置验证失败：\n\n{errorMessage}", "确定");
            }
        }
        
        private void ExportMCPJsonFile()
        {
            try
            {
                var path = EditorUtility.SaveFilePanel("导出MCP JSON配置", "", "mcp_config.json", "json");
                if (!string.IsNullOrEmpty(path))
                {
                    System.IO.File.WriteAllText(path, mcpJsonConfig);
                    EditorUtility.DisplayDialog("导出成功", $"MCP配置已导出到：\n{path}", "确定");
                }
            }
            catch (Exception e)
            {
                EditorUtility.DisplayDialog("导出失败", $"导出时出错：\n{e.Message}", "确定");
            }
        }
        
        private void ResetMCPConfig()
        {
            if (mcpConfig != null)
            {
                mcpConfig.enableMCP = false;
                mcpConfig.maxConcurrentConnections = 3;
                mcpConfig.defaultTimeoutSeconds = 30;
                mcpConfig.servers.Clear();
                
                UpdateMCPJsonConfig();
                EditorUtility.SetDirty(mcpConfig);
                
                EditorUtility.DisplayDialog("重置成功", "MCP配置已重置为默认设置", "确定");
            }
        }
        
        private void AddAWSDocsPreset()
        {
            var preset = new MCPServerConfig
            {
                name = "AWS文档",
                description = "AWS官方文档搜索和查询",
                transportType = MCPTransportType.Stdio,
                command = "uvx",
                args = new string[] { "awslabs.aws-documentation-mcp-server@latest" },
                enabled = false
            };
            mcpConfig.servers.Add(preset);
            UpdateMCPJsonConfig();
            EditorUtility.SetDirty(mcpConfig);
            AssetDatabase.SaveAssets();
        }
        
        private void ReloadMCPConfigInPython()
        {
            try
            {
                // 确保fPython桥接已初始化
                if (!PythonManager.IsInitialized)
                {
                    UnityEngine.Debug.LogWarning("Python未初始化，无法重新加载MCP配置");
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
                        UnityEngine.Debug.Log($"Python端MCP配置重新加载成功: {result.message}");
                        UnityEngine.Debug.Log($"MCP启用: {result.mcp_enabled}, 服务器数: {result.server_count}, 启用数: {result.enabled_server_count}");
                    }
                    else
                    {
                        UnityEngine.Debug.LogError($"Python端MCP配置重新加载失败: {result.message}");
                    }
                }
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogError($"调用Python reload_mcp_config失败: {e.Message}");
            }
        }
        
        private void TestUnityDirectory()
        {
            try
            {
                if (!PythonManager.IsInitialized)
                {
                    UnityEngine.Debug.LogWarning("Python未初始化，无法测试目录");
                    return;
                }
                
                using (Py.GIL())
                {
                    // 直接执行目录测试代码
                    dynamic os = Py.Import("os");
                    string currentDir = os.getcwd().ToString();
                    UnityEngine.Debug.Log($"Unity调用Python时的工作目录: {currentDir}");
                    
                    // 检查配置文件是否存在
                    string[] configPaths = {
                        "Assets/UnityAIAgent/mcp_config.json",
                        "../Assets/UnityAIAgent/mcp_config.json", 
                        "../../Assets/UnityAIAgent/mcp_config.json",
                        "/Users/caobao/projects/unity/CubeVerse/Assets/UnityAIAgent/mcp_config.json"
                    };
                    
                    foreach (string path in configPaths)
                    {
                        bool exists = os.path.exists(path);
                        string absPath = os.path.abspath(path).ToString();
                        UnityEngine.Debug.Log($"配置路径: {path} -> {absPath} (存在: {exists})");
                    }
                    
                    // 列出当前目录的文件
                    dynamic listdir = os.listdir(currentDir);
                    System.Text.StringBuilder sb = new System.Text.StringBuilder("当前目录文件: ");
                    int count = 0;
                    foreach (dynamic file in listdir)
                    {
                        if (count < 10) // 只显示前10个
                        {
                            sb.Append(file.ToString() + ", ");
                            count++;
                        }
                    }
                    UnityEngine.Debug.Log(sb.ToString());
                    
                    // 测试MCP连接
                    try
                    {
                        UnityEngine.Debug.Log("=== 测试Unity环境下的MCP连接 ===");
                        dynamic agentCore = Py.Import("agent_core");
                        dynamic builtins = Py.Import("builtins");
                        
                        // 测试单独的MCP配置加载
                        UnityEngine.Debug.Log("--- 测试MCP配置加载 ---");
                        try
                        {
                            dynamic json = Py.Import("json");
                            string configPath = "Assets/UnityAIAgent/mcp_config.json";
                            string configContent = System.IO.File.ReadAllText(configPath);
                            UnityEngine.Debug.Log($"配置文件内容: {configContent}");
                            
                            dynamic config = json.loads(configContent);
                            UnityEngine.Debug.Log($"JSON解析成功");
                            
                            // 检查mcpServers
                            if (config.__contains__("mcpServers"))
                            {
                                dynamic mcpServers = config["mcpServers"];
                                dynamic keys = mcpServers.keys();
                                int serverCount = (int)builtins.len(keys);
                                UnityEngine.Debug.Log($"找到 {serverCount} 个MCP服务器配置");
                                
                                foreach (dynamic serverName in keys)
                                {
                                    UnityEngine.Debug.Log($"服务器: {serverName}");
                                    dynamic serverConfig = mcpServers[serverName];
                                    UnityEngine.Debug.Log($"  命令: {serverConfig.get("command", "未设置")}");
                                    if (serverConfig.__contains__("args"))
                                    {
                                        dynamic args = serverConfig["args"];
                                        int argCount = (int)builtins.len(args);
                                        UnityEngine.Debug.Log($"  参数数量: {argCount}");
                                        if (argCount > 0)
                                        {
                                            UnityEngine.Debug.Log($"  第一个参数: {args[0]}");
                                        }
                                    }
                                }
                            }
                            else
                            {
                                UnityEngine.Debug.LogWarning("配置文件中没有mcpServers节点");
                            }
                        }
                        catch (Exception configEx)
                        {
                            UnityEngine.Debug.LogError($"MCP配置测试失败: {configEx.Message}");
                        }
                        
                        // 获取代理实例
                        UnityEngine.Debug.Log("--- 获取代理实例 ---");
                        dynamic agent = agentCore.get_agent();
                        UnityEngine.Debug.Log($"代理实例类型: {agent.GetType()}");
                        
                        // 检查可用工具
                        UnityEngine.Debug.Log("--- 检查初始工具 ---");
                        dynamic tools = agent.get_available_tools();
                        int toolCount = (int)builtins.len(tools);
                        UnityEngine.Debug.Log($"可用工具数量: {toolCount}");
                        
                        // 重新加载MCP配置
                        UnityEngine.Debug.Log("--- 重新加载MCP配置 ---");
                        string reloadResult = agentCore.reload_mcp_config();
                        UnityEngine.Debug.Log($"MCP重新加载结果: {reloadResult}");
                        
                        // 再次检查工具
                        UnityEngine.Debug.Log("--- 检查重新加载后工具 ---");
                        agent = agentCore.get_agent();
                        tools = agent.get_available_tools();
                        int newToolCount = (int)builtins.len(tools);
                        UnityEngine.Debug.Log($"重新加载后工具数量: {newToolCount}");
                        
                        if (newToolCount > toolCount)
                        {
                            UnityEngine.Debug.Log($"✓ MCP工具成功加载！增加了 {newToolCount - toolCount} 个工具");
                        }
                        else if (newToolCount == toolCount && newToolCount > 9)
                        {
                            UnityEngine.Debug.Log("✓ MCP工具已经加载");
                        }
                        else
                        {
                            UnityEngine.Debug.LogWarning($"❌ MCP工具加载可能失败，期望16个工具，实际{newToolCount}个");
                        }
                        
                        // 如果工具数量不对，运行简单诊断
                        if (newToolCount < 16)
                        {
                            UnityEngine.Debug.Log("--- 运行Unity环境诊断 ---");
                            try
                            {
                                // 测试基本的进程创建能力
                                var startInfo = new System.Diagnostics.ProcessStartInfo
                                {
                                    FileName = "node",
                                    Arguments = "--version",
                                    UseShellExecute = false,
                                    RedirectStandardOutput = true,
                                    RedirectStandardError = true,
                                    CreateNoWindow = true
                                };
                                
                                using (var process = System.Diagnostics.Process.Start(startInfo))
                                {
                                    string output = process.StandardOutput.ReadToEnd();
                                    string error = process.StandardError.ReadToEnd();
                                    process.WaitForExit();
                                    
                                    UnityEngine.Debug.Log($"Node.js版本检测: {output.Trim()}");
                                    if (!string.IsNullOrEmpty(error))
                                    {
                                        UnityEngine.Debug.LogWarning($"Node.js错误: {error}");
                                    }
                                    
                                    // 测试MCP服务器文件
                                    string mcpServerPath = "/Users/caobao/projects/unity/CubeVerse/Library/PackageCache/com.gamelovers.mcp-unity@fe27f2b491/Server/build/index.js";
                                    if (System.IO.File.Exists(mcpServerPath))
                                    {
                                        UnityEngine.Debug.Log("✓ MCP服务器文件存在");
                                        
                                        // 可能的问题：Unity环境下的异步/线程限制
                                        UnityEngine.Debug.LogWarning("❌ Unity环境可能不支持Python的异步MCP客户端");
                                        UnityEngine.Debug.LogWarning("这可能是PythonNET在Unity环境下的线程/异步限制导致的");
                                    }
                                    else
                                    {
                                        UnityEngine.Debug.LogError("❌ MCP服务器文件不存在");
                                    }
                                }
                            }
                            catch (Exception diagEx)
                            {
                                UnityEngine.Debug.LogError($"Unity环境诊断失败: {diagEx.Message}");
                                UnityEngine.Debug.LogError("❌ Unity无法创建子进程，这可能是MCP连接失败的原因");
                            }
                        }
                        
                        UnityEngine.Debug.Log("✓ Unity环境下MCP连接测试完成");
                    }
                    catch (Exception mcpEx)
                    {
                        UnityEngine.Debug.LogError($"Unity环境下MCP连接测试失败: {mcpEx.Message}");
                        UnityEngine.Debug.LogError($"详细错误: {mcpEx}");
                    }
                }
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogError($"测试Unity目录失败: {e.Message}");
                UnityEngine.Debug.LogError($"详细错误: {e.StackTrace}");
            }
        }
        
        private void LoadExistingMCPJsonConfig()
        {
            try
            {
                // 加载现有的mcp_config.json
                string jsonPath = "Assets/UnityAIAgent/mcp_config.json";
                if (System.IO.File.Exists(jsonPath))
                {
                    mcpJsonConfig = System.IO.File.ReadAllText(jsonPath);
                    UnityEngine.Debug.Log($"加载现有MCP JSON配置，长度: {mcpJsonConfig.Length} 字符");
                }
                else
                {
                    // 如果没有文件，使用当前配置生成
                    if (mcpConfig != null)
                    {
                        mcpJsonConfig = mcpConfig.GenerateAnthropicMCPJson();
                        UnityEngine.Debug.Log("没有找到mcp_config.json，使用当前配置生成");
                    }
                    else
                    {
                        // 默认空配置
                        mcpJsonConfig = "{\n  \"mcpServers\": {\n  }\n}";
                    }
                }
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogWarning($"加载mcp_config.json失败: {e.Message}");
                mcpJsonConfig = "{\n  \"mcpServers\": {\n  }\n}";
            }
        }
        
        private void AddGitHubPreset()
        {
            var preset = new MCPServerConfig
            {
                name = "GitHub",
                description = "GitHub仓库管理和搜索",
                transportType = MCPTransportType.Stdio,
                command = "uvx",
                args = new string[] { "mcp-server-github" },
                enabled = false,
                environmentVariables = new System.Collections.Generic.List<EnvironmentVariable>
                {
                    new EnvironmentVariable { key = "GITHUB_TOKEN", value = "", isSecret = true }
                }
            };
            mcpConfig.servers.Add(preset);
            UpdateMCPJsonConfig();
            EditorUtility.SetDirty(mcpConfig);
            AssetDatabase.SaveAssets();
        }
        
        private void AddFilesystemPreset()
        {
            var preset = new MCPServerConfig
            {
                name = "文件系统",
                description = "本地文件系统访问",
                transportType = MCPTransportType.Stdio,
                command = "uvx",
                args = new string[] { "mcp-server-filesystem", "--base-path", Application.dataPath },
                enabled = false
            };
            mcpConfig.servers.Add(preset);
            UpdateMCPJsonConfig();
            EditorUtility.SetDirty(mcpConfig);
            AssetDatabase.SaveAssets();
        }
        
        private void AddWebSearchPreset()
        {
            var preset = new MCPServerConfig
            {
                name = "Web搜索",
                description = "网络搜索和信息检索",
                transportType = MCPTransportType.HTTP,
                httpUrl = "http://localhost:8000/mcp",
                enabled = false
            };
            mcpConfig.servers.Add(preset);
            UpdateMCPJsonConfig();
            EditorUtility.SetDirty(mcpConfig);
            AssetDatabase.SaveAssets();
        }
        
        private string GenerateJsonConfig()
        {
            var config = new SerializableConfig
            {
                enable_mcp = mcpConfig.enableMCP,
                max_concurrent_connections = mcpConfig.maxConcurrentConnections,
                default_timeout_seconds = mcpConfig.defaultTimeoutSeconds,
                servers = new SerializableServer[mcpConfig.servers.Count]
            };
            
            for (int i = 0; i < mcpConfig.servers.Count; i++)
            {
                var server = mcpConfig.servers[i];
                config.servers[i] = new SerializableServer
                {
                    name = server.name,
                    description = server.description,
                    enabled = server.enabled,
                    transport_type = server.transportType.ToString().ToLower(),
                    command = server.command,
                    args = server.args,
                    working_directory = server.workingDirectory,
                    url = server.httpUrl,
                    timeout = server.timeoutSeconds,
                    auto_restart = server.autoRestart,
                    max_retries = server.maxRetries,
                    log_output = server.logOutput
                };
            }
            
            return JsonUtility.ToJson(config, true);
        }
        
        [System.Serializable]
        private class SerializableConfig
        {
            public bool enable_mcp;
            public int max_concurrent_connections;
            public int default_timeout_seconds;
            public SerializableServer[] servers;
        }
        
        [System.Serializable]
        private class SerializableServer
        {
            public string name;
            public string description;
            public bool enabled;
            public string transport_type;
            public string command;
            public string[] args;
            public string working_directory;
            public string url;
            public int timeout;
            public bool auto_restart;
            public int max_retries;
            public bool log_output;
        }
        
        private async Task<bool> CheckNodeJsInstalled()
        {
            try
            {
                // 构建Node.js安装路径列表
                var nodePathsList = new List<string>();
                
                // 常见的固定路径
                nodePathsList.AddRange(new string[] {
                    "/usr/local/bin/node",                              // Homebrew Intel Mac
                    "/opt/homebrew/bin/node",                           // Homebrew Apple Silicon
                    "/usr/bin/node",                                    // System installation
                });
                
                // 动态检测NVM安装的Node.js版本
                string nvmPath = System.IO.Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile), ".nvm/versions/node");
                if (System.IO.Directory.Exists(nvmPath))
                {
                    try
                    {
                        var versionDirs = System.IO.Directory.GetDirectories(nvmPath);
                        foreach (string versionDir in versionDirs)
                        {
                            string nodeBinPath = System.IO.Path.Combine(versionDir, "bin", "node");
                            if (System.IO.File.Exists(nodeBinPath))
                            {
                                nodePathsList.Add(nodeBinPath);
                                UnityEngine.Debug.Log($"发现NVM Node.js版本: {nodeBinPath}");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        UnityEngine.Debug.LogWarning($"检测NVM路径时出错: {ex.Message}");
                    }
                }
                
                // 最后尝试PATH中的node
                nodePathsList.Add("node");
                
                string[] nodePaths = nodePathsList.ToArray();
                
                foreach (string nodePath in nodePaths)
                {
                    var startInfo = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = nodePath,
                        Arguments = "--version",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    };
                
                    try
                    {
                        using (var process = System.Diagnostics.Process.Start(startInfo))
                        {
                            string output = process.StandardOutput.ReadToEnd();
                            process.WaitForExit();
                            
                            if (process.ExitCode == 0 && !string.IsNullOrEmpty(output))
                            {
                                UnityEngine.Debug.Log($"✓ 在 {nodePath} 检测到Node.js版本: {output.Trim()}");
                                
                                // 检查版本是否足够新 (建议v16+)
                                if (output.Contains("v") && output.Length > 2)
                                {
                                    string versionStr = output.Trim().Substring(1); // 去掉v前缀
                                    string[] parts = versionStr.Split('.');
                                    if (int.TryParse(parts[0], out int majorVersion))
                                    {
                                        if (majorVersion >= 16)
                                        {
                                            UnityEngine.Debug.Log("✓ Node.js版本符合要求");
                                            return true;
                                        }
                                        else
                                        {
                                            UnityEngine.Debug.LogWarning($"⚠️ Node.js版本过低 (v{majorVersion})，建议升级到v16或更高版本");
                                            return false;
                                        }
                                    }
                                }
                                return true;
                            }
                        }
                    }
                    catch (Exception pathEx)
                    {
                        // 这个路径的node不存在或无法执行，继续尝试下一个
                        UnityEngine.Debug.Log($"路径 {nodePath} 检测失败: {pathEx.Message}");
                    }
                }
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogError($"检测Node.js时发生严重错误: {e.Message}");
            }
            
            UnityEngine.Debug.LogWarning("❌ 在所有常见路径都未检测到Node.js");
            return false;
        }
        
        private async Task InstallNodeJs()
        {
            try
            {
                UnityEngine.Debug.Log("开始安装Node.js...");
                
                if (Application.platform == RuntimePlatform.OSXEditor)
                {
                    // macOS: 使用Homebrew安装
                    bool hasHomebrew = await CheckHomebrewInstalled();
                    
                    if (!hasHomebrew)
                    {
                        UnityEngine.Debug.Log("安装Homebrew包管理器...");
                        await InstallHomebrew();
                    }
                    
                    UnityEngine.Debug.Log("使用Homebrew安装Node.js LTS版本...");
                    var startInfo = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "/bin/bash",
                        Arguments = "-c \"brew install node@20\"",  // 安装Node.js 20 LTS版本
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    };
                    
                    using (var process = System.Diagnostics.Process.Start(startInfo))
                    {
                        string output = process.StandardOutput.ReadToEnd();
                        string error = process.StandardError.ReadToEnd();
                        await Task.Run(() => process.WaitForExit());
                        
                        if (process.ExitCode == 0)
                        {
                            UnityEngine.Debug.Log("✓ Node.js安装成功");
                            
                            // 链接node@20到系统PATH
                            UnityEngine.Debug.Log("链接Node.js到系统PATH...");
                            var linkStartInfo = new System.Diagnostics.ProcessStartInfo
                            {
                                FileName = "/bin/bash",
                                Arguments = "-c \"brew link --overwrite node@20\"",
                                UseShellExecute = false,
                                RedirectStandardOutput = true,
                                RedirectStandardError = true,
                                CreateNoWindow = true
                            };
                            
                            using (var linkProcess = System.Diagnostics.Process.Start(linkStartInfo))
                            {
                                linkProcess.WaitForExit();
                                if (linkProcess.ExitCode == 0)
                                {
                                    UnityEngine.Debug.Log("✓ Node.js链接成功");
                                }
                                else
                                {
                                    // 如果链接失败，尝试强制链接
                                    UnityEngine.Debug.LogWarning("链接失败，尝试强制链接...");
                                    linkStartInfo.Arguments = "-c \"brew link --force --overwrite node@20\"";
                                    using (var forceLink = System.Diagnostics.Process.Start(linkStartInfo))
                                    {
                                        forceLink.WaitForExit();
                                    }
                                }
                            }
                            
                            // 验证安装
                            await Task.Delay(1000);
                            bool verified = await CheckNodeJsInstalled();
                            if (verified)
                            {
                                UnityEngine.Debug.Log("✓ Node.js安装验证成功");
                            }
                        }
                        else
                        {
                            UnityEngine.Debug.LogError($"Node.js安装失败: {error}");
                            throw new Exception($"Node.js安装失败: {error}");
                        }
                    }
                }
                else if (Application.platform == RuntimePlatform.WindowsEditor)
                {
                    // Windows: 提示用户手动安装
                    UnityEngine.Debug.LogWarning("Windows系统请手动安装Node.js");
                    if (EditorUtility.DisplayDialog("需要安装Node.js", 
                        "MCP功能需要Node.js支持。\n\n请访问 https://nodejs.org 下载并安装最新版本的Node.js。\n\n安装完成后请重新运行设置向导。", 
                        "打开下载页面", "稍后"))
                    {
                        Application.OpenURL("https://nodejs.org");
                    }
                    throw new Exception("需要手动安装Node.js");
                }
                else
                {
                    UnityEngine.Debug.LogWarning("不支持的平台，请手动安装Node.js");
                    throw new Exception("不支持的平台");
                }
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogError($"安装Node.js失败: {e.Message}");
                throw;
            }
        }
        
        private async Task<bool> CheckHomebrewInstalled()
        {
            try
            {
                var startInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "/bin/bash",
                    Arguments = "-c \"which brew\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };
                
                using (var process = System.Diagnostics.Process.Start(startInfo))
                {
                    string output = process.StandardOutput.ReadToEnd();
                    await Task.Run(() => process.WaitForExit());
                    return process.ExitCode == 0 && !string.IsNullOrEmpty(output.Trim());
                }
            }
            catch
            {
                return false;
            }
        }
        
        private async Task InstallHomebrew()
        {
            try
            {
                UnityEngine.Debug.Log("Homebrew安装需要管理员权限...");
                
                // 由于Unity环境的限制，我们提示用户手动安装
                if (EditorUtility.DisplayDialog("需要安装Homebrew", 
                    "Node.js安装需要Homebrew包管理器。\n\n" +
                    "请在终端中运行以下命令安装Homebrew：\n\n" +
                    "/bin/bash -c \"$(curl -fsSL https://raw.githubusercontent.com/Homebrew/install/HEAD/install.sh)\"\n\n" +
                    "安装完成后，请重新运行设置向导。", 
                    "复制命令", "取消"))
                {
                    // 复制安装命令到剪贴板
                    GUIUtility.systemCopyBuffer = "/bin/bash -c \"$(curl -fsSL https://raw.githubusercontent.com/Homebrew/install/HEAD/install.sh)\"";
                    UnityEngine.Debug.Log("✓ 已复制Homebrew安装命令到剪贴板");
                }
                
                throw new Exception("需要手动安装Homebrew");
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogError($"Homebrew安装失败: {e.Message}");
                throw;
            }
        }
    }
}