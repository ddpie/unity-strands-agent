# Unity AI Agent 插件需求文档

## 项目概述

开发一个Unity AI插件，通过Python.NET嵌入Python解释器，集成Strands Agent SDK为Unity开发者提供AI对话功能。插件使用AWS Bedrock默认模型，通过本地AWS credentials进行认证。

## 核心设计原则

- **嵌入式Python**：使用Python.NET直接在Unity进程中运行Python
- **虚拟环境隔离**：创建独立虚拟环境管理Python依赖
- **AWS认证**：暂时使用本地AWS credentials，无需手动配置API密钥
- **macOS优先**：首先支持macOS平台
- **流式响应**：提供实时的AI响应流式输出

## 技术架构

### 项目结构
```
UnityAIAgent/
├── Editor/
│   ├── AIAgentWindow.cs          # 主聊天界面窗口
│   ├── PythonManager.cs          # Python环境管理
│   ├── PythonBridge.cs           # Python.NET集成
│   ├── StreamingHandler.cs       # 流式响应处理
│   ├── SetupWizard.cs           # 首次使用向导
│   └── LogWindow.cs              # 日志查看窗口
├── Python/
│   ├── venv/                     # 虚拟环境目录
│   ├── agent_core.py             # Strands Agent封装
│   ├── streaming_agent.py        # 流式响应支持
│   └── requirements.txt          # Python依赖
├── Resources/
│   └── Icons/                    # UI图标资源
└── package.json                  # Unity Package配置
```

### 技术栈
- **Unity**: 最新版本
- **Python**: 3.10+（通过系统动态检测）
- **Python.NET**: 嵌入式Python解释器
- **Strands Agent SDK**: 默认配置（AWS Bedrock）
- **虚拟环境**: venv隔离依赖

## 功能需求

### 1. Python环境管理（动态检测）
- 通过`which python3`动态查找系统Python
- 自动推导Python Framework路径
- 创建并管理虚拟环境
- 自动安装Python依赖

### 2. 首次使用体验
- **设置向导**
  - 自动检测Python环境
  - 实时显示安装进度
  - 步骤指引和错误提示
  - 一键修复常见问题
- **进度反馈**
  - 虚拟环境创建进度条
  - 依赖安装实时日志
  - 错误诊断和解决建议

### 3. 聊天界面功能
- **流式响应**
  - 逐字显示AI回复
  - 打字机效果动画
  - 可中断的响应流
- **消息显示**
  - 支持Markdown渲染
  - 代码块语法高亮
  - 消息历史持久化（保存到用户目录：`~/Documents/UnityAIAgent/chat_history.json`）
- **交互功能**
  - 文本输入框
  - 发送按钮（发送时禁用，防止重复发送）
  - 停止生成按钮（流式响应时显示）
  - 复制消息内容
  - 清空聊天历史
- **界面布局**
  - 可调整窗口大小
  - 滚动查看历史消息
  - 深色/浅色主题切换

### 4. 日志系统
- 显示Python原始输出和错误
- 日志级别过滤（Debug/Info/Warning/Error）
- 日志导出功能
- 自动滚动到最新日志

### 5. AWS集成
- 自动读取本地AWS credentials
- 使用默认配置（无需指定模型）
- 支持AWS profile切换（可选）

## 实现细节

### 重构的PythonManager（基于动态检测）

```csharp
using Python.Runtime;
using UnityEngine;
using UnityEditor;
using System;
using System.IO;
using System.Diagnostics;
using System.Collections;

[InitializeOnLoad]
public static class PythonManager
{
    private static bool isPythonInitialized = false;
    private static string venvPath;
    private static string pythonExecutable;
    private static string pythonHome;
    private static string pythonVersion;
    
    // 初始化进度回调
    public static event Action<string, float> OnInitProgress;
    
    static PythonManager()
    {
        // Unity Editor事件监听
        AssemblyReloadEvents.beforeAssemblyReload += OnBeforeAssemblyReload;
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
    }
    
    public static void EnsureInitialized()
    {
        if (!isPythonInitialized)
        {
            Initialize();
        }
    }
    
    private static void Initialize()
    {
        try
        {
            ReportProgress("Detecting Python installation...", 0.1f);
            
            // 1. 动态检测Python
            DetectPython();
            
            ReportProgress("Creating virtual environment...", 0.3f);
            
            // 2. 创建虚拟环境
            CreateVirtualEnvironment();
            
            ReportProgress("Configuring environment...", 0.6f);
            
            // 3. 配置环境变量
            ConfigureEnvironment();
            
            ReportProgress("Initializing Python engine...", 0.8f);
            
            // 4. 初始化Python引擎
            PythonEngine.Initialize();
            PythonEngine.BeginAllowThreads();
            
            isPythonInitialized = true;
            ReportProgress("Python initialized successfully!", 1.0f);
            Debug.Log("Python initialized successfully");
        }
        catch (Exception e)
        {
            ReportProgress($"Failed: {e.Message}", -1f);
            Debug.LogError($"Failed to initialize Python: {e.Message}");
            throw;
        }
    }
    
    private static void ReportProgress(string message, float progress)
    {
        OnInitProgress?.Invoke(message, progress);
    }
    
    private static void DetectPython()
    {
        // 使用 which python3 查找Python
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "/bin/bash",
                Arguments = "-c \"which python3\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            }
        };
        
        process.Start();
        pythonExecutable = process.StandardOutput.ReadToEnd().Trim();
        process.WaitForExit();
        
        if (string.IsNullOrEmpty(pythonExecutable) || !File.Exists(pythonExecutable))
        {
            throw new Exception("Python 3 not found. Please install Python 3.10 or later.");
        }
        
        Debug.Log($"Found Python at: {pythonExecutable}");
        
        // 获取Python信息
        GetPythonInfo();
        
        // 设置Python DLL路径
        SetPythonDLL();
    }
    
    private static void GetPythonInfo()
    {
        // 获取Python版本和路径信息
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = pythonExecutable,
                Arguments = "-c \"import sys, json; print(json.dumps({'version': f'{sys.version_info.major}.{sys.version_info.minor}', 'prefix': sys.prefix, 'exec_prefix': sys.exec_prefix, 'base_prefix': getattr(sys, 'base_prefix', sys.prefix)}))\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            }
        };
        
        process.Start();
        string output = process.StandardOutput.ReadToEnd().Trim();
        process.WaitForExit();
        
        if (process.ExitCode != 0)
        {
            string error = process.StandardError.ReadToEnd();
            throw new Exception($"Failed to get Python info: {error}");
        }
        
        // 解析JSON输出
        var info = JsonUtility.FromJson<PythonInfo>(output);
        pythonVersion = info.version;
        pythonHome = info.base_prefix;
        
        Debug.Log($"Python version: {pythonVersion}, Home: {pythonHome}");
    }
    
    private static void SetPythonDLL()
    {
        // 构建Python DLL路径
        string dllPath = "";
        
        // macOS路径模式
        string[] possiblePaths = {
            Path.Combine(pythonHome, "Python"), // 直接路径
            Path.Combine(pythonHome, "..", "Python"), // 相对路径
            Path.Combine(pythonHome, "lib", $"libpython{pythonVersion}.dylib"),
            Path.Combine(pythonHome, "Frameworks", "Python.framework", "Versions", pythonVersion, "Python")
        };
        
        foreach (var path in possiblePaths)
        {
            if (File.Exists(path))
            {
                dllPath = path;
                break;
            }
        }
        
        if (string.IsNullOrEmpty(dllPath))
        {
            throw new Exception($"Python DLL not found for Python {pythonVersion} at {pythonHome}");
        }
        
        Runtime.PythonDLL = dllPath;
        Debug.Log($"Python DLL: {dllPath}");
    }
    
    private static void CreateVirtualEnvironment()
    {
        string projectPath = Path.GetDirectoryName(Application.dataPath);
        venvPath = Path.Combine(projectPath, "Python", "venv");
        
        if (!Directory.Exists(venvPath))
        {
            Debug.Log("Creating virtual environment...");
            
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = pythonExecutable,
                    Arguments = $"-m venv \"{venvPath}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                }
            };
            
            process.Start();
            process.WaitForExit();
            
            if (process.ExitCode != 0)
            {
                string error = process.StandardError.ReadToEnd();
                throw new Exception($"Failed to create virtual environment: {error}");
            }
            
            Debug.Log("Virtual environment created successfully");
            
            // 安装依赖
            InstallDependencies();
        }
    }
    
    private static void ConfigureEnvironment()
    {
        // 设置PYTHONHOME为主Python安装目录
        Environment.SetEnvironmentVariable("PYTHONHOME", pythonHome);
        
        // 设置PYTHONPATH包含虚拟环境的site-packages
        string venvLib = Path.Combine(venvPath, "lib", $"python{pythonVersion}");
        string venvSitePackages = Path.Combine(venvLib, "site-packages");
        Environment.SetEnvironmentVariable("PYTHONPATH", venvSitePackages);
        
        // 设置DYLD_LIBRARY_PATH（macOS特定）
        string dylibPath = Path.Combine(pythonHome, "lib");
        string currentDyldPath = Environment.GetEnvironmentVariable("DYLD_LIBRARY_PATH") ?? "";
        Environment.SetEnvironmentVariable("DYLD_LIBRARY_PATH", 
            string.IsNullOrEmpty(currentDyldPath) ? dylibPath : $"{dylibPath}:{currentDyldPath}");
        
        // 配置PythonEngine
        PythonEngine.PythonHome = pythonHome;
        PythonEngine.PythonPath = venvSitePackages;
        
        Debug.Log($"Environment configured - PYTHONHOME: {pythonHome}, PYTHONPATH: {venvSitePackages}");
    }
    
    private static void InstallDependencies()
    {
        string pipPath = Path.Combine(venvPath, "bin", "pip");
        string requirementsPath = Path.Combine(Path.GetDirectoryName(Application.dataPath), 
            "Python", "requirements.txt");
        
        ReportProgress("Installing dependencies...", 0.5f);
        
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = pipPath,
                Arguments = $"install -r \"{requirementsPath}\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            }
        };
        
        // 实时读取输出
        process.OutputDataReceived += (sender, e) => {
            if (!string.IsNullOrEmpty(e.Data))
            {
                Debug.Log($"[pip] {e.Data}");
                ReportProgress($"Installing: {e.Data}", 0.5f);
            }
        };
        
        process.Start();
        process.BeginOutputReadLine();
        process.WaitForExit();
        
        if (process.ExitCode != 0)
        {
            string error = process.StandardError.ReadToEnd();
            throw new Exception($"Failed to install dependencies: {error}");
        }
        
        Debug.Log("Dependencies installed successfully");
    }
    
    // Unity Editor事件处理
    private static void OnBeforeAssemblyReload()
    {
        // 清理C#引用，但不关闭Python引擎
        using (Py.GIL())
        {
            // 清理缓存的Python对象
        }
    }
    
    private static void OnPlayModeStateChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.EnteredPlayMode)
        {
            EnsureInitialized();
        }
    }
    
    // 自动重新初始化
    public static void ReinitializeIfNeeded()
    {
        if (!isPythonInitialized || !PythonEngine.IsInitialized)
        {
            Initialize();
        }
    }
    
    [Serializable]
    private class PythonInfo
    {
        public string version;
        public string prefix;
        public string exec_prefix;
        public string base_prefix;
    }
}
```

### 流式响应支持

```python
# streaming_agent.py
from strands import Agent
import asyncio
import json
import logging

# 配置日志
logging.getLogger("strands").setLevel(logging.DEBUG)

# 创建支持流式响应的Agent
agent = Agent()

async def process_message_stream(message):
    """处理用户消息并流式返回AI响应"""
    try:
        # 使用异步流式API
        async for chunk in agent.astream(message):
            # 返回每个chunk给Unity
            yield json.dumps({
                "type": "chunk",
                "content": chunk,
                "done": False
            })
        
        # 流式结束
        yield json.dumps({
            "type": "complete",
            "content": "",
            "done": True
        })
        
    except Exception as e:
        yield json.dumps({
            "type": "error",
            "error": str(e),
            "done": True
        })

def process_message_sync(message):
    """同步版本的消息处理（后备方案）"""
    try:
        response = agent(message)
        return {"success": True, "response": response}
    except Exception as e:
        return {"success": False, "error": str(e)}
```

### 首次使用向导

```csharp
public class SetupWizard : EditorWindow
{
    private int currentStep = 0;
    private string statusMessage = "";
    private float progress = 0f;
    private bool isProcessing = false;
    
    [MenuItem("Window/AI Assistant/Setup Wizard")]
    public static void ShowWindow()
    {
        var window = GetWindow<SetupWizard>("AI Assistant Setup");
        window.minSize = new Vector2(500, 400);
    }
    
    private void OnEnable()
    {
        PythonManager.OnInitProgress += OnProgressUpdate;
    }
    
    private void OnDisable()
    {
        PythonManager.OnInitProgress -= OnProgressUpdate;
    }
    
    private void OnProgressUpdate(string message, float progress)
    {
        this.statusMessage = message;
        this.progress = progress;
        Repaint();
    }
    
    private void OnGUI()
    {
        GUILayout.Label("Unity AI Assistant Setup", EditorStyles.largeLabel);
        GUILayout.Space(20);
        
        // 步骤显示
        DrawStep(0, "Detect Python", currentStep > 0);
        DrawStep(1, "Create Virtual Environment", currentStep > 1);
        DrawStep(2, "Install Dependencies", currentStep > 2);
        DrawStep(3, "Initialize AI Engine", currentStep > 3);
        
        GUILayout.Space(20);
        
        // 状态消息
        if (!string.IsNullOrEmpty(statusMessage))
        {
            EditorGUILayout.HelpBox(statusMessage, 
                progress < 0 ? MessageType.Error : MessageType.Info);
        }
        
        // 进度条
        if (isProcessing && progress >= 0)
        {
            EditorGUI.ProgressBar(
                EditorGUILayout.GetControlRect(GUILayout.Height(20)), 
                progress, 
                $"{(int)(progress * 100)}%"
            );
        }
        
        GUILayout.Space(20);
        
        // 操作按钮
        using (new EditorGUI.DisabledScope(isProcessing))
        {
            if (GUILayout.Button("Start Setup", GUILayout.Height(40)))
            {
                StartSetup();
            }
        }
        
        if (isProcessing && GUILayout.Button("Cancel"))
        {
            // 取消操作
            isProcessing = false;
        }
    }
    
    private void DrawStep(int step, string title, bool completed)
    {
        using (new GUILayout.HorizontalScope())
        {
            // 步骤图标
            string icon = completed ? "✓" : (currentStep == step ? "▶" : "○");
            GUILayout.Label(icon, GUILayout.Width(20));
            
            // 步骤标题
            var style = new GUIStyle(EditorStyles.label);
            if (completed) style.normal.textColor = Color.green;
            else if (currentStep == step) style.fontStyle = FontStyle.Bold;
            
            GUILayout.Label(title, style);
        }
    }
    
    private async void StartSetup()
    {
        isProcessing = true;
        currentStep = 0;
        
        try
        {
            await Task.Run(() => PythonManager.EnsureInitialized());
            currentStep = 4;
            statusMessage = "Setup completed successfully!";
            
            // 自动打开AI助手窗口
            EditorApplication.delayCall += () => {
                AIAgentWindow.ShowWindow();
                Close();
            };
        }
        catch (Exception e)
        {
            statusMessage = $"Setup failed: {e.Message}";
            Debug.LogError(e);
        }
        finally
        {
            isProcessing = false;
        }
    }
}
```

### 聊天界面示例（支持流式响应）

```
┌─────────────────────────────────────────────┐
│ Unity AI Agent                          [-]│
├─────────────────────────────────────────────┤
│ ┌─────────────────────────────────────────┐ │
│ │ AI: Hello! I'm your Unity AI assistant. │ │
│ │     How can I help you today?           │ │
│ │                                         │ │
│ │ You: How do I rotate an object?        │ │
│ │                                         │ │
│ │ AI: To rotate an object in Unity, you  │ │
│ │     can use several methods:            │ │
│ │                                         │ │
│ │     1. Using transform.Rotate():|       │ │
│ │                                         │ │
│ └─────────────────────────────────────────┘ │
│                                             │
│ [Type your message...             ] [Send]  │
│                           [Stop Generation] │
│                                             │
│ [Clear] [Copy] [Logs] [Settings]            │
└─────────────────────────────────────────────┘
```

## 开发计划

### 第一阶段：基础架构（3天）
- [ ] 实现动态Python检测
- [ ] 设置向导UI
- [ ] 虚拟环境管理
- [ ] 实时进度反馈

### 第二阶段：核心功能（5天）
- [ ] Python.NET集成
- [ ] Strands Agent集成
- [ ] 流式响应实现
- [ ] 基础聊天界面
- [ ] AWS credentials读取

### 第三阶段：界面完善（3天）
- [ ] Markdown渲染支持
- [ ] 代码语法高亮
- [ ] 流式响应UI
- [ ] 消息历史持久化
- [ ] 日志窗口实现

### 第四阶段：优化和文档（2天）
- [ ] 错误处理优化
- [ ] 性能调优
- [ ] 使用文档编写
- [ ] 示例代码准备

## 技术要求

### 系统要求
- macOS 10.15+
- Unity 2022.3 LTS+
- Python 3.10+（系统安装）
- 有效的AWS credentials

### 开发依赖
- Python.NET NuGet包
- Unity UI Toolkit（可选）
- TextMeshPro（文本渲染）

## 限制说明

- **仅支持macOS**：初版只支持macOS平台
- **需要网络连接**：Strands Agent需要访问AWS Bedrock
- **需要AWS账号**：使用本地AWS credentials认证
- **Python版本**：需要Python 3.10+
- **Unity重启说明**：Python引擎在Editor生命周期内保持运行，完全重置需要重启Unity Editor

## 许可证

MIT License - 开源免费使用

## 安装方式

### Unity Package Manager - 本地目录
```
1. 克隆仓库到本地
2. Unity Package Manager → Add package from disk
3. 选择package.json文件
4. 等待依赖安装完成
```

### 首次使用
```
1. Window → AI Assistant → Setup Wizard
2. 按照向导完成Python环境设置
3. 等待依赖安装完成
4. 开始使用AI助手
```

### 技术说明
- **动态Python检测**：通过`which python3`查找系统Python，自动推导所需路径
- **虚拟环境管理**：Python.NET使用主Python DLL，但加载虚拟环境的site-packages
- **流式响应**：使用异步API实现逐字输出，提供现代化交互体验
- **自动恢复**：如果Python崩溃，插件会自动尝试重新初始化
- **并发控制**：发送消息时禁用发送按钮，避免并发请求