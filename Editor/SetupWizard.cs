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
            "安装Python依赖",
            "初始化AI引擎"
        };
        
        [MenuItem("Window/AI助手/设置向导")]
        public static void ShowWindow()
        {
            var window = GetWindow<SetupWizard>("AI助手设置");
            window.minSize = new Vector2(500, 400);
            window.maxSize = new Vector2(500, 400);
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
            
            Repaint();
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
                EditorGUILayout.HelpBox("欢迎使用Unity AI助手！此向导将帮助您完成初始设置。", MessageType.Info);
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
            
            try
            {
                await Task.Run(() => {
                    PythonManager.EnsureInitialized();
                });
                
                setupCompleted = true;
                currentStep = setupSteps.Length;
                statusMessage = "设置完成！AI助手已就绪。";
                isProcessing = false;
                
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
                    EditorUtility.DisplayDialog("设置失败", 
                        $"设置过程中遇到错误：\n\n{e.Message}\n\n请检查：\n• Python 3.10+是否已安装\n• 网络连接是否正常\n• AWS凭证是否配置", 
                        "确定");
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