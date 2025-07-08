using Python.Runtime;
using UnityEngine;
using UnityEditor;
using System;
using System.IO;
using System.Diagnostics;
using System.Collections;
using System.Runtime.InteropServices;

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
            
            // 监听Unity退出事件
            EditorApplication.quitting += OnUnityQuitting;
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
                
                ReportProgress("正在配置环境...", 0.4f);
                
                // 3. 配置环境变量
                ConfigureEnvironment();
                
                ReportProgress("正在安装Python依赖...", 0.6f);
                
                // 4. 安装必要的Python依赖包
                InstallDependencies();
                
                ReportProgress("正在初始化Python引擎...", 0.8f);
                
                // 4. 初始化Python引擎
                // 重要：必须在主线程初始化Python
                if (!PythonEngine.IsInitialized)
                {
                    try
                    {
                        // 清理可能冲突的环境变量
                        Environment.SetEnvironmentVariable("PYTHONHOME", pythonHome);
                        Environment.SetEnvironmentVariable("PYTHONPATH", Environment.GetEnvironmentVariable("PYTHONPATH"));
                        
                        // 设置Python路径 - 注意：PythonHome和PythonPath必须在Initialize之前设置
                        PythonEngine.PythonHome = pythonHome;
                        PythonEngine.PythonPath = Environment.GetEnvironmentVariable("PYTHONPATH");
                        
                        // 禁用Python的站点包初始化，避免某些初始化问题
                        Environment.SetEnvironmentVariable("PYTHONNOUSERSITE", "1");
                        
                        // 初始化
                        PythonEngine.Initialize();
                        PythonEngine.BeginAllowThreads();
                        
                        // 验证Python是否正确初始化
                        using (Py.GIL())
                        {
                            dynamic sys = Py.Import("sys");
                            dynamic version = sys.version;
                            EditorApplication.delayCall += () => {
                                UnityEngine.Debug.Log($"Python引擎初始化成功: {version.ToString()}");
                            };
                        }
                    }
                    catch (Exception e)
                    {
                        // 如果初始化失败，尝试清理并重新抛出错误
                        if (PythonEngine.IsInitialized)
                        {
                            PythonEngine.Shutdown();
                        }
                        throw new Exception($"Python引擎初始化失败: {e.Message}\n\n" +
                            $"PYTHONHOME: {pythonHome}\n" +
                            $"PYTHONPATH: {Environment.GetEnvironmentVariable("PYTHONPATH")}\n" +
                            $"Python DLL: {Runtime.PythonDLL}", e);
                    }
                }
                
                isPythonInitialized = true;
                ReportProgress("Python初始化成功！", 1.0f);
                EditorApplication.delayCall += () => {
                    UnityEngine.Debug.Log("Python环境初始化成功");
                };
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
            // 强制优先使用Python 3.11，严格按照版本要求检测
            string[] pythonPaths = new string[] {
                // Homebrew Apple Silicon (M1/M2) - 优先级最高
                "/opt/homebrew/bin/python3.11",
                "/opt/homebrew/opt/python@3.11/bin/python3.11",
                "/opt/homebrew/opt/python@3.11/bin/python3",
                "/opt/homebrew/Cellar/python@3.11/3.11.13/bin/python3.11",
                "/opt/homebrew/Cellar/python@3.11/3.11.12/bin/python3.11",
                "/opt/homebrew/Cellar/python@3.11/3.11.11/bin/python3.11",
                "/opt/homebrew/Cellar/python@3.11/3.11.10/bin/python3.11",
                
                // Homebrew Intel Mac
                "/usr/local/bin/python3.11",
                "/usr/local/opt/python@3.11/bin/python3.11",
                "/usr/local/opt/python@3.11/bin/python3",
                "/usr/local/Cellar/python@3.11/3.11.13/bin/python3.11",
                "/usr/local/Cellar/python@3.11/3.11.12/bin/python3.11",
                "/usr/local/Cellar/python@3.11/3.11.11/bin/python3.11",
                
                // Python.org官方安装
                "/Library/Frameworks/Python.framework/Versions/3.11/bin/python3.11",
                "/Library/Frameworks/Python.framework/Versions/3.11/bin/python3",
                
                // MacPorts
                "/opt/local/bin/python3.11",
                
                // 标准位置
                "/usr/bin/python3.11"
                // 注意：移除了 /usr/bin/python3 以避免使用系统Python 3.9
            };
            
            pythonExecutable = null;
            foreach (var path in pythonPaths)
            {
                if (File.Exists(path))
                {
                    // 验证这确实是Python 3.11
                    try
                    {
                        var versionCheck = new Process
                        {
                            StartInfo = new ProcessStartInfo
                            {
                                FileName = path,
                                Arguments = "--version",
                                UseShellExecute = false,
                                RedirectStandardOutput = true,
                                RedirectStandardError = true,
                                CreateNoWindow = true
                            }
                        };
                        versionCheck.Start();
                        string versionOutput = versionCheck.StandardOutput.ReadToEnd() + versionCheck.StandardError.ReadToEnd();
                        versionCheck.WaitForExit();
                        
                        if (versionOutput.Contains("3.11"))
                        {
                            pythonExecutable = path;
                            EditorApplication.delayCall += () => {
                                UnityEngine.Debug.Log($"选择Python 3.11: {path} (版本: {versionOutput.Trim()})");
                            };
                            break;
                        }
                        else
                        {
                            EditorApplication.delayCall += () => {
                                UnityEngine.Debug.LogWarning($"跳过非3.11版本: {path} (版本: {versionOutput.Trim()})");
                            };
                        }
                    }
                    catch (Exception e)
                    {
                        EditorApplication.delayCall += () => {
                            UnityEngine.Debug.LogWarning($"无法检查Python版本 {path}: {e.Message}");
                        };
                    }
                }
            }
            
            // 如果没找到，使用which命令寻找Python 3.11
            if (string.IsNullOrEmpty(pythonExecutable))
            {
                // 优先寻找python3.11
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "/bin/bash",
                        Arguments = "-c \"which python3.11\"",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    }
                };
                
                process.Start();
                pythonExecutable = process.StandardOutput.ReadToEnd().Trim();
                process.WaitForExit();
                
                // 如果找到python3.11，验证版本
                if (!string.IsNullOrEmpty(pythonExecutable) && File.Exists(pythonExecutable))
                {
                    EditorApplication.delayCall += () => {
                        UnityEngine.Debug.Log($"通过which找到Python 3.11: {pythonExecutable}");
                    };
                }
                else
                {
                    // 如果找不到python3.11，记录错误而不是降级到python3
                    pythonExecutable = null;
                }
            }
            
            if (string.IsNullOrEmpty(pythonExecutable) || !File.Exists(pythonExecutable))
            {
                throw new Exception("未找到兼容的Python版本。请安装Python 3.11：\nbrew install python@3.11");
            }
            
            EditorApplication.delayCall += () => {
                UnityEngine.Debug.Log($"找到Python: {pythonExecutable}");
            };
            
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
                    CreateNoWindow = true,
                    // 不要在StartInfo中直接设置Environment，而是使用EnvironmentVariables
                    EnvironmentVariables = {
                        // 清理环境变量，避免干扰
                        ["PYTHONPATH"] = "",
                        ["PYTHONHOME"] = "",
                        ["PYTHONNOUSERSITE"] = "1"
                    }
                }
            };
            
            // 复制必要的环境变量
            process.StartInfo.EnvironmentVariables["PATH"] = Environment.GetEnvironmentVariable("PATH");
            
            process.Start();
            string output = process.StandardOutput.ReadToEnd().Trim();
            string error = process.StandardError.ReadToEnd();
            process.WaitForExit();
            
            if (process.ExitCode != 0 || !string.IsNullOrEmpty(error))
            {
                throw new Exception($"获取Python信息失败: {error}");
            }
            
            // 解析JSON输出
            var info = JsonUtility.FromJson<PythonInfo>(output);
            pythonVersion = info.version;
            pythonHome = info.base_prefix;
            
            // 检查Python版本兼容性
            var versionParts = pythonVersion.Split('.');
            int majorVersion = int.Parse(versionParts[0]);
            int minorVersion = int.Parse(versionParts[1]);
            
            if (majorVersion != 3 || minorVersion > 12)
            {
                throw new Exception($"Python {pythonVersion} 可能不兼容。Python.NET 目前支持 Python 3.7-3.12。\n建议安装 Python 3.11：brew install python@3.11");
            }
            
            EditorApplication.delayCall += () => {
                UnityEngine.Debug.Log($"Python版本: {pythonVersion}, 主目录: {pythonHome}");
            };
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
            EditorApplication.delayCall += () => {
                UnityEngine.Debug.Log($"Python DLL: {dllPath}");
            };
        }
        
        public static void CreateVirtualEnvironment()
        {
            // 使用项目目录下的虚拟环境
            string projectPath = Path.GetDirectoryName(Application.dataPath);
            venvPath = Path.Combine(projectPath, "Python", "venv");
            
            EditorApplication.delayCall += () => {
                UnityEngine.Debug.Log($"使用项目目录虚拟环境: {venvPath}");
            };
            
            if (!Directory.Exists(venvPath))
            {
                EditorApplication.delayCall += () => {
                    UnityEngine.Debug.Log("正在创建虚拟环境...");
                };
                
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
                
                EditorApplication.delayCall += () => {
                    UnityEngine.Debug.Log("虚拟环境创建成功");
                };
                
                // 安装依赖
                InstallDependencies();
            }
        }
        
        private static void ConfigureEnvironment()
        {
            // 设置PYTHONHOME为主Python安装目录
            Environment.SetEnvironmentVariable("PYTHONHOME", pythonHome);
            
            // 设置路径配置相关的环境变量
            SetPathConfigurationEnvironmentVariables();
            
            // 构建完整的PYTHONPATH，包含标准库和虚拟环境
            string venvLib = Path.Combine(venvPath, "lib", $"python{pythonVersion}");
            string venvSitePackages = Path.Combine(venvLib, "site-packages");
            
            // Python标准库路径 - 需要正确处理macOS Homebrew的特殊路径结构
            string pythonStdLib = "";
            string pythonStdLibDynload = "";
            
            // macOS Homebrew Python的标准库在Frameworks目录下
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                // 检查是否是Homebrew的Python（通过路径特征判断）
                if (pythonHome.Contains("/Frameworks/Python.framework/"))
                {
                    // Homebrew Python的标准库路径
                    pythonStdLib = Path.Combine(pythonHome, "lib", $"python{pythonVersion}");
                    pythonStdLibDynload = Path.Combine(pythonStdLib, "lib-dynload");
                }
                else if (pythonHome.Contains("/homebrew/") || pythonHome.Contains("/usr/local/"))
                {
                    // 尝试查找Frameworks路径
                    string frameworksPath = Path.Combine(pythonHome, "..", "..", "Frameworks", "Python.framework", "Versions", pythonVersion);
                    if (Directory.Exists(frameworksPath))
                    {
                        pythonStdLib = Path.Combine(frameworksPath, "lib", $"python{pythonVersion}");
                        pythonStdLibDynload = Path.Combine(pythonStdLib, "lib-dynload");
                    }
                    else
                    {
                        // 使用默认路径
                        pythonStdLib = Path.Combine(pythonHome, "lib", $"python{pythonVersion}");
                        pythonStdLibDynload = Path.Combine(pythonStdLib, "lib-dynload");
                    }
                }
                else
                {
                    // 其他情况使用默认路径
                    pythonStdLib = Path.Combine(pythonHome, "lib", $"python{pythonVersion}");
                    pythonStdLibDynload = Path.Combine(pythonStdLib, "lib-dynload");
                }
            }
            else
            {
                // 其他平台使用默认路径
                pythonStdLib = Path.Combine(pythonHome, "lib", $"python{pythonVersion}");
                pythonStdLibDynload = Path.Combine(pythonStdLib, "lib-dynload");
            }
            
            // 验证标准库路径存在
            if (!Directory.Exists(pythonStdLib))
            {
                throw new Exception($"Python标准库路径不存在: {pythonStdLib}\n请检查Python安装是否完整。");
            }
            
            // 插件Python模块路径
            string assemblyLocation = typeof(PythonManager).Assembly.Location;
            string packagePath = Path.GetDirectoryName(Path.GetDirectoryName(assemblyLocation));
            string pluginPythonPath = Path.Combine(packagePath, "Python");
            
            // 如果路径解析失败，使用路径配置中的路径作为后备
            if (!Directory.Exists(pluginPythonPath))
            {
                pluginPythonPath = PathManager.GetUnityAgentPythonPath();
                EditorApplication.delayCall += () => {
                    UnityEngine.Debug.Log($"使用配置的Python模块路径: {pluginPythonPath}");
                };
            }
            
            EditorApplication.delayCall += () => {
                UnityEngine.Debug.Log($"Assembly位置: {assemblyLocation}");
                UnityEngine.Debug.Log($"包路径: {packagePath}");
                UnityEngine.Debug.Log($"最终插件Python路径: {pluginPythonPath}");
            };
            
            // 组合完整的PYTHONPATH（标准库 + 插件模块 + 虚拟环境）
            // 注意：在某些情况下，我们需要包含Python的基础模块路径
            string pythonBaseLib = Path.Combine(pythonHome, "lib");
            string fullPythonPath = $"{pythonStdLib}:{pythonStdLibDynload}:{pythonBaseLib}:{pluginPythonPath}:{venvSitePackages}";
            
            // 添加Python的zip文件（如果存在）
            string pythonZip = Path.Combine(pythonHome, "lib", $"python{pythonVersion.Split('.')[0]}{pythonVersion.Split('.')[1]}.zip");
            if (File.Exists(pythonZip))
            {
                fullPythonPath = $"{pythonZip}:{fullPythonPath}";
            }
            Environment.SetEnvironmentVariable("PYTHONPATH", fullPythonPath);
            
            // 设置UTF-8编码环境变量
            Environment.SetEnvironmentVariable("PYTHONIOENCODING", "utf-8");
            Environment.SetEnvironmentVariable("LC_ALL", "en_US.UTF-8");
            Environment.SetEnvironmentVariable("LANG", "en_US.UTF-8");
            
            // 设置SSL证书环境变量（解决SSL验证问题）
            Environment.SetEnvironmentVariable("PYTHONHTTPSVERIFY", "1");
            
            // 使用配置的SSL证书路径
            string sslCertDir = PathManager.GetValidSSLCertDirectory();
            string sslCertFile = PathManager.GetValidSSLCertPath();
            
            if (!string.IsNullOrEmpty(sslCertDir))
            {
                Environment.SetEnvironmentVariable("SSL_CERT_DIR", sslCertDir);
            }
            
            if (!string.IsNullOrEmpty(sslCertFile))
            {
                Environment.SetEnvironmentVariable("SSL_CERT_FILE", sslCertFile);
            }
            // 设置certifi证书路径（如果已安装）
            string certifiPath = Path.Combine(venvSitePackages, "certifi", "cacert.pem");
            if (File.Exists(certifiPath))
            {
                Environment.SetEnvironmentVariable("REQUESTS_CA_BUNDLE", certifiPath);
                Environment.SetEnvironmentVariable("CURL_CA_BUNDLE", certifiPath);
                EditorApplication.delayCall += () => {
                    UnityEngine.Debug.Log($"设置certifi证书路径: {certifiPath}");
                };
            }
            else
            {
                // 如果certifi还未安装，先清除这些环境变量，避免pip安装时出错
                Environment.SetEnvironmentVariable("REQUESTS_CA_BUNDLE", null);
                Environment.SetEnvironmentVariable("CURL_CA_BUNDLE", null);
            }
            
            // 设置DYLD_LIBRARY_PATH（macOS特定）
            string dylibPath = Path.Combine(pythonHome, "lib");
            string currentDyldPath = Environment.GetEnvironmentVariable("DYLD_LIBRARY_PATH") ?? "";
            Environment.SetEnvironmentVariable("DYLD_LIBRARY_PATH", 
                string.IsNullOrEmpty(currentDyldPath) ? dylibPath : $"{dylibPath}:{currentDyldPath}");
            
            // 配置PythonEngine
            PythonEngine.PythonHome = pythonHome;
            PythonEngine.PythonPath = fullPythonPath;
            
            EditorApplication.delayCall += () => {
                UnityEngine.Debug.Log($"环境配置完成 - PYTHONHOME: {pythonHome}, PYTHONPATH: {fullPythonPath}");
            };
        }
        
        private static void InstallDependencies()
        {
            string pipPath = Path.Combine(venvPath, "bin", "pip3");
            
            // 首先尝试使用项目目录的requirements.txt
            string projectPath = Path.GetDirectoryName(Application.dataPath);
            string requirementsPath = Path.Combine(projectPath, "Python", "requirements.txt");
            
            // 如果项目目录没有requirements.txt，尝试使用插件目录的
            if (!File.Exists(requirementsPath))
            {
                string packagePythonPath = PathManager.GetUnityAgentPythonPath();
                if (!string.IsNullOrEmpty(packagePythonPath))
                {
                    string pluginRequirementsPath = Path.Combine(packagePythonPath, "requirements.txt");
                    if (File.Exists(pluginRequirementsPath))
                    {
                        requirementsPath = pluginRequirementsPath;
                        EditorApplication.delayCall += () => {
                            UnityEngine.Debug.Log($"使用插件目录的requirements.txt: {requirementsPath}");
                        };
                    }
                }
            }
            else
            {
                EditorApplication.delayCall += () => {
                    UnityEngine.Debug.Log($"使用项目目录的requirements.txt: {requirementsPath}");
                };
            }
            
            if (!File.Exists(requirementsPath))
            {
                EditorApplication.delayCall += () => {
                    UnityEngine.Debug.LogWarning($"requirements.txt不存在，跳过依赖安装");
                };
                return;
            }
            
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
                    // 使用EditorApplication.delayCall确保在主线程中执行
                    EditorApplication.delayCall += () => {
                        UnityEngine.Debug.Log($"[pip3] {e.Data}");
                        ReportProgress($"安装中: {e.Data}", 0.5f);
                    };
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
            
            EditorApplication.delayCall += () => {
                UnityEngine.Debug.Log("依赖安装成功");
            };
        }
        
        
        /// <summary>
        /// 安装单个Python包
        /// </summary>
        public static void InstallPythonPackage(string packageName)
        {
            UnityEngine.Debug.Log($"[pip3] venvPath = {venvPath}");
            string pipPath = Path.Combine(venvPath, "bin", "pip3");
            UnityEngine.Debug.Log($"[pip3] 检查pip3路径: {pipPath}");
            
            if (!File.Exists(pipPath))
            {
                UnityEngine.Debug.LogError($"[pip3] pip3文件不存在: {pipPath}");
                // 尝试macOS的路径
                pipPath = Path.Combine(venvPath, "bin", "pip");
                UnityEngine.Debug.Log($"[pip3] 尝试pip路径: {pipPath}");
                if (!File.Exists(pipPath))
                {
                    throw new InvalidOperationException($"无法找到pip可执行文件: {pipPath}");
                }
            }
            
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = pipPath,
                    Arguments = $"install {packageName} --verbose",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                }
            };
            
            UnityEngine.Debug.Log($"[pip3] 执行命令: {pipPath} install {packageName} --verbose");
            
            process.Start();
            string output = process.StandardOutput.ReadToEnd();
            string error = process.StandardError.ReadToEnd();
            process.WaitForExit();
            
            // 输出详细日志
            if (!string.IsNullOrEmpty(output))
            {
                UnityEngine.Debug.Log($"[pip3 OUTPUT] {output}");
            }
            if (!string.IsNullOrEmpty(error))
            {
                UnityEngine.Debug.LogWarning($"[pip3 ERROR] {error}");
            }
            
            if (process.ExitCode != 0)
            {
                UnityEngine.Debug.LogError($"[pip3] 包安装失败: {packageName} (退出代码: {process.ExitCode})");
                throw new Exception($"包安装失败 ({packageName}): {error}");
            }
            
            UnityEngine.Debug.Log($"[pip3] 包安装成功: {packageName}");
        }
        
        /// <summary>
        /// 批量安装Python包
        /// </summary>
        public static void InstallMultiplePackages(string[] packageNames)
        {
            foreach (var packageName in packageNames)
            {
                InstallPythonPackage(packageName);
            }
        }
        
        /// <summary>
        /// 从requirements.txt文件安装依赖
        /// </summary>
        /// <param name="requirementsPath">requirements.txt文件的完整路径</param>
        public static void InstallFromRequirements(string requirementsPath)
        {
            if (string.IsNullOrEmpty(requirementsPath) || !File.Exists(requirementsPath))
            {
                throw new FileNotFoundException($"requirements.txt文件不存在: {requirementsPath}");
            }
            
            EnsureInitialized();
            
            UnityEngine.Debug.Log($"[pip3] venvPath = {venvPath}");
            string pipPath = Path.Combine(venvPath, "bin", "pip3");
            UnityEngine.Debug.Log($"[pip3] 检查pip3路径: {pipPath}");
            
            if (!File.Exists(pipPath))
            {
                UnityEngine.Debug.LogError($"[pip3] pip3文件不存在: {pipPath}");
                // 尝试macOS的路径
                pipPath = Path.Combine(venvPath, "bin", "pip");
                UnityEngine.Debug.Log($"[pip3] 尝试pip路径: {pipPath}");
                if (!File.Exists(pipPath))
                {
                    throw new InvalidOperationException($"无法找到pip可执行文件: {pipPath}");
                }
            }
            
            var process = new Process();
            process.StartInfo.FileName = pipPath;
            process.StartInfo.Arguments = $"install -r \"{requirementsPath}\" --verbose";
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.RedirectStandardError = true;
            process.StartInfo.CreateNoWindow = true;
            
            UnityEngine.Debug.Log($"[pip3] 执行命令: {pipPath} install -r \"{requirementsPath}\" --verbose");
            
            process.Start();
            string output = process.StandardOutput.ReadToEnd();
            string error = process.StandardError.ReadToEnd();
            process.WaitForExit();
            
            // 输出详细日志
            if (!string.IsNullOrEmpty(output))
            {
                UnityEngine.Debug.Log($"[pip3 OUTPUT] {output}");
            }
            if (!string.IsNullOrEmpty(error))
            {
                UnityEngine.Debug.LogWarning($"[pip3 ERROR] {error}");
            }
            
            if (process.ExitCode != 0)
            {
                UnityEngine.Debug.LogError($"[pip3] 安装失败 (退出代码: {process.ExitCode})");
                throw new Exception($"pip安装失败 (退出代码: {process.ExitCode})\n输出: {output}\n错误: {error}");
            }
            
            UnityEngine.Debug.Log($"[pip3] requirements.txt安装成功完成");
        }
        
        /// <summary>
        /// 配置SSL环境
        /// </summary>
        public static void ConfigureSSLEnvironment()
        {
            // 设置SSL证书路径
            string venvSitePackages = Path.Combine(venvPath, "lib", $"python{pythonVersion}", "site-packages");
            string certifiPath = Path.Combine(venvSitePackages, "certifi", "cacert.pem");
            
            if (File.Exists(certifiPath))
            {
                Environment.SetEnvironmentVariable("REQUESTS_CA_BUNDLE", certifiPath);
                Environment.SetEnvironmentVariable("CURL_CA_BUNDLE", certifiPath);
                
                EditorApplication.delayCall += () => {
                    UnityEngine.Debug.Log($"SSL证书配置完成: {certifiPath}");
                };
            }
            else
            {
                EditorApplication.delayCall += () => {
                    UnityEngine.Debug.LogWarning("未找到certifi证书文件，使用系统默认证书");
                };
                
                // 使用配置的SSL证书文件作为后备
                string fallbackCertFile = PathManager.GetValidSSLCertPath();
                if (!string.IsNullOrEmpty(fallbackCertFile))
                {
                    Environment.SetEnvironmentVariable("REQUESTS_CA_BUNDLE", fallbackCertFile);
                }
            }
        }
        
        /// <summary>
        /// 测试AWS连接
        /// </summary>
        public static void TestAWSConnection()
        {
            try
            {
                // 简单的AWS凭证检查
                var awsFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".aws");
                var credentialsFile = Path.Combine(awsFolder, "credentials");
                var configFile = Path.Combine(awsFolder, "config");
                
                bool hasCredentials = File.Exists(credentialsFile) || 
                                    !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("AWS_ACCESS_KEY_ID"));
                
                if (hasCredentials)
                {
                    EditorApplication.delayCall += () => {
                        UnityEngine.Debug.Log("AWS凭证检查通过");
                    };
                }
                else
                {
                    EditorApplication.delayCall += () => {
                        UnityEngine.Debug.LogWarning("未检测到AWS凭证，请确保已配置AWS访问密钥");
                    };
                }
            }
            catch (Exception e)
            {
                EditorApplication.delayCall += () => {
                    UnityEngine.Debug.LogWarning($"AWS连接检查失败: {e.Message}");
                };
            }
        }
        
        // Unity Editor事件处理
        private static void OnBeforeAssemblyReload()
        {
            // 先停止所有流式处理
            try
            {
                StreamingManager.StopAllStreaming();
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogWarning($"停止流式处理时出错: {e.Message}");
            }
            
            // 清理C#引用，但不关闭Python引擎
            if (isPythonInitialized)
            {
                try
                {
                    // 清理Python桥接
                    PythonBridge.Shutdown();
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
        
        /// <summary>
        /// 设置路径配置相关的环境变量
        /// </summary>
        private static void SetPathConfigurationEnvironmentVariables()
        {
            try
            {
                // 获取路径配置
                var pathConfig = PathManager.PathConfig;
                if (pathConfig == null) return;
                
                // 设置Strands工具路径
                if (!string.IsNullOrEmpty(pathConfig.strandsToolsPath))
                {
                    Environment.SetEnvironmentVariable("STRANDS_TOOLS_PATH", pathConfig.strandsToolsPath);
                }
                
                
                // 设置SSL证书路径
                string sslCertFile = PathManager.GetValidSSLCertPath();
                if (!string.IsNullOrEmpty(sslCertFile))
                {
                    Environment.SetEnvironmentVariable("SSL_CERT_FILE_PATH", sslCertFile);
                }
                
                string sslCertDir = PathManager.GetValidSSLCertDirectory();
                if (!string.IsNullOrEmpty(sslCertDir))
                {
                    Environment.SetEnvironmentVariable("SSL_CERT_DIR_PATH", sslCertDir);
                }
                
                // 设置Shell路径
                string shellPath = PathManager.GetShellExecutablePath();
                if (!string.IsNullOrEmpty(shellPath))
                {
                    Environment.SetEnvironmentVariable("SHELL_EXECUTABLE_PATH", shellPath);
                }
                
                // 设置MCP配置路径
                string mcpConfigPath = PathManager.GetMCPConfigPath();
                if (!string.IsNullOrEmpty(mcpConfigPath))
                {
                    Environment.SetEnvironmentVariable("MCP_CONFIG_PATH", mcpConfigPath);
                }
                
                // 设置MCP Unity服务器路径
                string mcpServerPath = PathManager.GetMCPUnityServerPath();
                if (!string.IsNullOrEmpty(mcpServerPath))
                {
                    Environment.SetEnvironmentVariable("MCP_UNITY_SERVER_PATH", mcpServerPath);
                }
                
                // 设置项目根目录
                string projectRoot = PathManager.GetProjectRootPath();
                if (!string.IsNullOrEmpty(projectRoot))
                {
                    Environment.SetEnvironmentVariable("PROJECT_ROOT_PATH", projectRoot);
                }
                
                EditorApplication.delayCall += () => {
                    UnityEngine.Debug.Log("路径配置环境变量已设置");
                };
            }
            catch (Exception e)
            {
                EditorApplication.delayCall += () => {
                    UnityEngine.Debug.LogWarning($"设置路径配置环境变量失败: {e.Message}");
                };
            }
        }
        
        /// <summary>
        /// Unity退出时调用
        /// </summary>
        private static void OnUnityQuitting()
        {
            UnityEngine.Debug.Log("Unity 正在退出，开始清理 Python 资源...");
            Shutdown();
        }
        
        /// <summary>
        /// 关闭Python引擎和清理资源
        /// </summary>
        public static void Shutdown()
        {
            if (isPythonInitialized)
            {
                try
                {
                    // 先清理 Python 桥接
                    PythonBridge.Shutdown();
                    
                    // 关闭 Python 引擎
                    if (PythonEngine.IsInitialized)
                    {
                        UnityEngine.Debug.Log("正在关闭Python引擎...");
                        PythonEngine.Shutdown();
                        UnityEngine.Debug.Log("Python引擎已关闭");
                    }
                }
                catch (Exception e)
                {
                    UnityEngine.Debug.LogWarning($"关闭 Python 引擎时出错: {e.Message}");
                }
                finally
                {
                    isPythonInitialized = false;
                }
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
}