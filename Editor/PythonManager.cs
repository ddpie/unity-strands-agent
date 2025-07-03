using Python.Runtime;
using UnityEngine;
using UnityEditor;
using System;
using System.IO;
using System.Diagnostics;
using System.Collections;

namespace UnityAIAgent.Editor
{
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
                ReportProgress("正在检测Python安装...", 0.1f);
                
                // 1. 动态检测Python
                DetectPython();
                
                ReportProgress("正在创建虚拟环境...", 0.3f);
                
                // 2. 创建虚拟环境
                CreateVirtualEnvironment();
                
                ReportProgress("正在配置环境...", 0.6f);
                
                // 3. 配置环境变量
                ConfigureEnvironment();
                
                ReportProgress("正在初始化Python引擎...", 0.8f);
                
                // 4. 初始化Python引擎
                PythonEngine.Initialize();
                PythonEngine.BeginAllowThreads();
                
                isPythonInitialized = true;
                ReportProgress("Python初始化成功！", 1.0f);
                UnityEngine.Debug.Log("Python环境初始化成功");
            }
            catch (Exception e)
            {
                ReportProgress($"失败: {e.Message}", -1f);
                UnityEngine.Debug.LogError($"Python初始化失败: {e.Message}");
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
            
            if (process.ExitCode != 0 || string.IsNullOrEmpty(pythonExecutable) || !File.Exists(pythonExecutable))
            {
                throw new Exception("未找到Python 3。请安装Python 3.10或更高版本。");
            }
            
            UnityEngine.Debug.Log($"找到Python: {pythonExecutable}");
            
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
                throw new Exception($"获取Python信息失败: {error}");
            }
            
            // 解析JSON输出
            var info = JsonUtility.FromJson<PythonInfo>(output);
            pythonVersion = info.version;
            pythonHome = info.base_prefix;
            
            UnityEngine.Debug.Log($"Python版本: {pythonVersion}, 主目录: {pythonHome}");
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
                throw new Exception($"未找到Python {pythonVersion}的DLL文件，路径: {pythonHome}");
            }
            
            Runtime.PythonDLL = dllPath;
            UnityEngine.Debug.Log($"Python DLL: {dllPath}");
        }
        
        private static void CreateVirtualEnvironment()
        {
            string projectPath = Path.GetDirectoryName(Application.dataPath);
            venvPath = Path.Combine(projectPath, "Python", "venv");
            
            if (!Directory.Exists(venvPath))
            {
                UnityEngine.Debug.Log("正在创建虚拟环境...");
                
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
                    throw new Exception($"创建虚拟环境失败: {error}");
                }
                
                UnityEngine.Debug.Log("虚拟环境创建成功");
                
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
            
            UnityEngine.Debug.Log($"环境配置完成 - PYTHONHOME: {pythonHome}, PYTHONPATH: {venvSitePackages}");
        }
        
        private static void InstallDependencies()
        {
            string pipPath = Path.Combine(venvPath, "bin", "pip");
            string requirementsPath = Path.Combine(Path.GetDirectoryName(Application.dataPath), 
                "Python", "requirements.txt");
            
            ReportProgress("正在安装依赖...", 0.5f);
            
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
                    UnityEngine.Debug.Log($"[pip] {e.Data}");
                    ReportProgress($"安装中: {e.Data}", 0.5f);
                }
            };
            
            process.Start();
            process.BeginOutputReadLine();
            process.WaitForExit();
            
            if (process.ExitCode != 0)
            {
                string error = process.StandardError.ReadToEnd();
                throw new Exception($"依赖安装失败: {error}");
            }
            
            UnityEngine.Debug.Log("依赖安装成功");
        }
        
        // Unity Editor事件处理
        private static void OnBeforeAssemblyReload()
        {
            // 清理C#引用，但不关闭Python引擎
            if (isPythonInitialized)
            {
                try
                {
                    using (Py.GIL())
                    {
                        // 清理缓存的Python对象
                    }
                }
                catch (Exception e)
                {
                    UnityEngine.Debug.LogWarning($"清理Python对象时出现警告: {e.Message}");
                }
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

        public static bool IsInitialized => isPythonInitialized;
        public static string VenvPath => venvPath;
        public static string PythonExecutable => pythonExecutable;
        public static string PythonHome => pythonHome;
        public static string PythonVersion => pythonVersion;
        
        [Serializable]
        private class PythonInfo
        {
            public string version;
            public string prefix;
            public string exec_prefix;
            public string base_prefix;
        }
    }
}