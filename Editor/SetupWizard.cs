using UnityEngine;
using UnityEditor;
using System;
using System.Threading.Tasks;

namespace UnityAIAgent.Editor
{
    public class SetupWizard : EditorWindow
    {
        private int currentStep = 0;
        private string statusMessage = "";
        private float progress = 0f;
        private bool isProcessing = false;
        private bool setupCompleted = false;
        private GUIStyle headerStyle;
        private GUIStyle stepStyle;
        private GUIStyle statusStyle;
        
        private readonly string[] setupSteps = {
            "检测Python环境",
            "创建虚拟环境", 
            "安装Strands Agent SDK",
            "安装SSL证书支持",
            "安装其他依赖包",
            "配置环境变量",
            "初始化Python桥接",
            "验证AWS连接",
            "完成设置"
        };
        
        [MenuItem("Window/AI助手/设置向导")]
        public static void ShowWindow()
        {
            var window = GetWindow<SetupWizard>("AI助手设置");
            window.minSize = new Vector2(500, 700); // 增加最小高度到700
            window.maxSize = new Vector2(600, 800); // 增加最大尺寸，宽度也稍微增加
        }
        
        private void OnEnable()
        {
            PythonManager.OnInitProgress += OnProgressUpdate;
            CheckExistingSetup();
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
            
            GUILayout.Space(20);
            
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
                    LogWindow.ShowWindow();
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
                
                // 步骤4: 安装SSL证书支持 (带重试)
                UpdateProgress("安装SSL证书支持...", 0.4f);
                await RetryOperation(() => {
                    PythonManager.InstallPythonPackage("certifi>=2023.0.0");
                }, "SSL证书支持");
                
                // 步骤5: 安装其他依赖包 (带重试)
                UpdateProgress("安装其他依赖包...", 0.6f);
                await RetryOperation(() => {
                    PythonManager.InstallMultiplePackages(new[] {
                        "strands-agents-tools>=0.1.8",
                        "boto3>=1.28.0",
                        "aiofiles>=23.0.0",
                        "colorlog>=6.7.0",
                        "orjson>=3.9.0"
                    });
                }, "其他依赖包");
                
                // 步骤6: 配置环境变量
                UpdateProgress("配置环境变量...", 0.7f);
                await Task.Run(() => {
                    PythonManager.ConfigureSSLEnvironment();
                });
                
                // 步骤7: 初始化Python桥接
                UpdateProgress("初始化Python桥接...", 0.8f);
                await Task.Run(() => {
                    PythonManager.EnsureInitialized();
                });
                
                // 步骤8: 验证AWS连接
                UpdateProgress("验证AWS连接...", 0.9f);
                await Task.Run(() => {
                    PythonManager.TestAWSConnection();
                });
                
                // 步骤9: 完成设置
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
    }
}