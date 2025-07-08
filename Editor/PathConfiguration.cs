using UnityEngine;
using System;
using System.IO;
using System.Collections.Generic;

namespace UnityAIAgent.Editor
{
    /// <summary>
    /// 路径配置ScriptableObject，管理项目中的所有可配置路径
    /// </summary>
    [CreateAssetMenu(fileName = "PathConfiguration", menuName = "UnityAIAgent/Path Configuration")]
    public class PathConfiguration : ScriptableObject
    {
        [Header("项目根目录")]
        [Tooltip("Unity项目的根目录路径，其他路径将相对于此目录")]
        public string projectRootPath = "";
        
        
        [Header("MCP 服务器配置")]
        [Tooltip("MCP Unity服务器构建文件路径（相对于项目根目录）")]
        public string mcpUnityServerPath = "";
        
        [Tooltip("MCP配置文件路径（相对于项目根目录）")]
        public string mcpConfigPath = "Assets/UnityAIAgent/mcp_config.json";
        
        [Header("Python 配置")]
        [Tooltip("Unity AI代理Python模块路径")]
        public string strandsToolsPath = "";
        
        [Tooltip("Unity Agent Python模块路径（相对于包目录）")]
        public string unityAgentPythonPath = "Python";
        
        [Header("Python 可执行文件路径")]
        [Tooltip("Python 3.11 可执行文件路径列表（按优先级排序）")]
        public List<string> pythonExecutablePaths = new List<string>();
        
        
        [Header("系统路径配置")]
        [Tooltip("SSL 证书目录路径列表")]
        public List<string> sslCertDirectories = new List<string>();
        
        [Tooltip("SSL 证书文件路径列表")]
        public List<string> sslCertFiles = new List<string>();
        
        [Tooltip("Shell 可执行文件路径")]
        public string shellExecutablePath = "/bin/bash";
        
        [Header("诊断配置")]
        [Tooltip("用于诊断的配置文件路径列表（相对于项目根目录）")]
        public List<string> diagnosticConfigPaths = new List<string>();
        
        /// <summary>
        /// 获取绝对路径
        /// </summary>
        /// <param name="relativePath">相对路径</param>
        /// <returns>绝对路径</returns>
        public string GetAbsolutePath(string relativePath)
        {
            if (string.IsNullOrEmpty(relativePath))
                return "";
                
            // 处理用户主目录路径
            if (relativePath.StartsWith("~/"))
            {
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), 
                                   relativePath.Substring(2));
            }
            
            // 如果已经是绝对路径，直接返回
            if (Path.IsPathRooted(relativePath))
                return relativePath;
            
            // 相对于项目根目录的路径
            if (string.IsNullOrEmpty(projectRootPath))
                return relativePath;
                
            return Path.Combine(projectRootPath, relativePath);
        }
        
        /// <summary>
        /// 获取相对于项目根目录的相对路径
        /// </summary>
        /// <param name="absolutePath">绝对路径</param>
        /// <returns>相对路径</returns>
        public string GetRelativePath(string absolutePath)
        {
            if (string.IsNullOrEmpty(absolutePath) || string.IsNullOrEmpty(projectRootPath))
                return absolutePath;
                
            try
            {
                Uri projectUri = new Uri(projectRootPath + Path.DirectorySeparatorChar);
                Uri fileUri = new Uri(absolutePath);
                
                if (projectUri.Scheme != fileUri.Scheme)
                    return absolutePath; // 不同的URI方案
                    
                Uri relativeUri = projectUri.MakeRelativeUri(fileUri);
                string relativePath = Uri.UnescapeDataString(relativeUri.ToString());
                
                // 将正斜杠转换为系统路径分隔符
                return relativePath.Replace('/', Path.DirectorySeparatorChar);
            }
            catch
            {
                return absolutePath;
            }
        }
        
        /// <summary>
        /// 验证配置的有效性
        /// </summary>
        /// <returns>验证结果和错误信息</returns>
        public (bool isValid, List<string> errors) ValidateConfiguration()
        {
            var errors = new List<string>();
            
            // 验证项目根目录
            if (string.IsNullOrEmpty(projectRootPath))
            {
                errors.Add("项目根目录路径不能为空");
            }
            else if (!Directory.Exists(projectRootPath))
            {
                errors.Add($"项目根目录不存在: {projectRootPath}");
            }
            
            
            
            // 验证Python路径
            bool foundValidPython = false;
            foreach (string pythonPath in pythonExecutablePaths)
            {
                if (!string.IsNullOrEmpty(pythonPath) && File.Exists(GetAbsolutePath(pythonPath)))
                {
                    foundValidPython = true;
                    break;
                }
            }
            
            if (pythonExecutablePaths.Count > 0 && !foundValidPython)
            {
                errors.Add("未找到有效的Python可执行文件");
            }
            
            return (errors.Count == 0, errors);
        }
        
        /// <summary>
        /// 初始化默认配置
        /// </summary>
        public void InitializeDefaults()
        {
            // 自动检测项目根目录
            AutoDetectProjectRoot();
            
            // 设置默认的相对路径
            mcpConfigPath = "Assets/UnityAIAgent/mcp_config.json";
            unityAgentPythonPath = "Python";
            shellExecutablePath = "/bin/bash";
            
            // 初始化Python路径列表
            pythonExecutablePaths = new List<string>
            {
                // Homebrew Apple Silicon (M1/M2)
                "/opt/homebrew/bin/python3.11",
                "/opt/homebrew/Cellar/python@3.11/3.11.13/bin/python3.11",
                "/opt/homebrew/Cellar/python@3.11/3.11.12/bin/python3.11",
                "/opt/homebrew/Cellar/python@3.11/3.11.11/bin/python3.11",
                "/opt/homebrew/Cellar/python@3.11/3.11.10/bin/python3.11",
                "/opt/homebrew/Cellar/python@3.11/3.11.9/bin/python3.11",
                "/opt/homebrew/opt/python@3.11/bin/python3.11",
                "/opt/homebrew/opt/python@3.11/bin/python3",
                
                // Homebrew Intel Mac
                "/usr/local/bin/python3.11",
                "/usr/local/opt/python@3.11/bin/python3.11",
                "/usr/local/opt/python@3.11/bin/python3",
                "/usr/local/Cellar/python@3.11/3.11.13/bin/python3.11",
                "/usr/local/Cellar/python@3.11/3.11.12/bin/python3.11",
                "/usr/local/Cellar/python@3.11/3.11.11/bin/python3.11",
                "/usr/local/Cellar/python@3.11/3.11.10/bin/python3.11",
                "/usr/local/Cellar/python@3.11/3.11.9/bin/python3.11",
                
                // MacPorts
                "/opt/local/bin/python3.11",
                "/opt/local/Library/Frameworks/Python.framework/Versions/3.11/bin/python3.11",
                
                // pyenv
                "~/.pyenv/versions/3.11.13/bin/python",
                "~/.pyenv/versions/3.11.12/bin/python",
                "~/.pyenv/versions/3.11.11/bin/python",
                "~/.pyenv/versions/3.11.10/bin/python",
                "~/.pyenv/versions/3.11.9/bin/python",
                "~/.pyenv/versions/3.11.8/bin/python",
                "~/.pyenv/versions/3.11.7/bin/python",
                "~/.pyenv/versions/3.11.6/bin/python",
                "~/.pyenv/versions/3.11.5/bin/python",
                "~/.pyenv/shims/python3.11",
                
                // 标准系统位置
                "/usr/local/bin/python3.11",
                "/usr/bin/python3.11",
                
                // Python.org官方安装
                "/Library/Frameworks/Python.framework/Versions/3.11/bin/python3.11",
                "/Library/Frameworks/Python.framework/Versions/3.11/bin/python3",
                
                // conda/miniconda/anaconda
                "~/anaconda3/envs/python311/bin/python",
                "~/miniconda3/envs/python311/bin/python",
                "~/miniforge3/envs/python311/bin/python",
                "/opt/anaconda3/envs/python311/bin/python",
                "/opt/miniconda3/envs/python311/bin/python",
                "/opt/miniforge3/envs/python311/bin/python",
                
                // 其他常见位置
                "/Applications/Python 3.11/python3.11",
                "~/Library/Python/3.11/bin/python3.11"
            };
            
            
            // 初始化SSL证书路径
            sslCertDirectories = new List<string>
            {
                "/etc/ssl/certs",
                "/usr/local/etc/openssl",
                "/opt/homebrew/etc/openssl"
            };
            
            sslCertFiles = new List<string>
            {
                "/etc/ssl/cert.pem",
                "/usr/local/etc/openssl/cert.pem",
                "/opt/homebrew/etc/openssl/cert.pem",
                "/System/Library/OpenSSL/certs/cert.pem"
            };
            
            // 初始化诊断配置路径
            diagnosticConfigPaths = new List<string>
            {
                "Assets/UnityAIAgent/mcp_config.json",
                "../Assets/UnityAIAgent/mcp_config.json",
                "../../Assets/UnityAIAgent/mcp_config.json"
            };
            
            // 自动检测其他路径
            AutoDetectMCPServerPath();
        }
        
        /// <summary>
        /// 自动检测项目根目录
        /// </summary>
        public void AutoDetectProjectRoot()
        {
            // 使用当前Unity项目的根目录
            string currentProjectPath = Path.GetDirectoryName(Application.dataPath);
            projectRootPath = currentProjectPath;
        }
        
        
        /// <summary>
        /// 自动检测MCP服务器路径
        /// </summary>
        public void AutoDetectMCPServerPath()
        {
            if (string.IsNullOrEmpty(projectRootPath))
                return;
                
            // 搜索可能的MCP服务器路径
            string[] searchPatterns = {
                "Library/PackageCache/com.gamelovers.mcp-unity*/Server/build/index.js",
                "Packages/com.gamelovers.mcp-unity/Server/build/index.js"
            };
            
            foreach (string pattern in searchPatterns)
            {
                string searchPath = Path.Combine(projectRootPath, pattern.Replace('*', ' ').Trim());
                string directory = Path.GetDirectoryName(searchPath);
                
                if (Directory.Exists(directory))
                {
                    string[] files = Directory.GetFiles(directory, "index.js", SearchOption.TopDirectoryOnly);
                    if (files.Length > 0)
                    {
                        mcpUnityServerPath = GetRelativePath(files[0]);
                        break;
                    }
                }
                
                // 如果包含通配符，进行模式匹配
                if (pattern.Contains("*"))
                {
                    string baseDir = Path.Combine(projectRootPath, "Library/PackageCache");
                    if (Directory.Exists(baseDir))
                    {
                        string[] dirs = Directory.GetDirectories(baseDir, "com.gamelovers.mcp-unity*");
                        foreach (string dir in dirs)
                        {
                            string serverFile = Path.Combine(dir, "Server/build/index.js");
                            if (File.Exists(serverFile))
                            {
                                mcpUnityServerPath = GetRelativePath(serverFile);
                                break;
                            }
                        }
                    }
                }
            }
        }
        
        /// <summary>
        /// 获取配置摘要信息
        /// </summary>
        /// <returns>配置摘要</returns>
        public string GetConfigurationSummary()
        {
            var summary = new System.Text.StringBuilder();
            summary.AppendLine($"项目根目录: {projectRootPath}");
            summary.AppendLine($"MCP服务器路径: {GetAbsolutePath(mcpUnityServerPath)}");
            summary.AppendLine($"Strands工具路径: {strandsToolsPath}");
            
            var (isValid, errors) = ValidateConfiguration();
            summary.AppendLine($"配置状态: {(isValid ? "有效" : "有错误")}");
            
            if (!isValid)
            {
                summary.AppendLine("错误:");
                foreach (string error in errors)
                {
                    summary.AppendLine($"  - {error}");
                }
            }
            
            return summary.ToString();
        }
        
        /// <summary>
        /// 获取第一个有效的Python可执行文件路径
        /// </summary>
        /// <returns>Python可执行文件的绝对路径</returns>
        public string GetValidPythonPath()
        {
            foreach (string pythonPath in pythonExecutablePaths)
            {
                if (!string.IsNullOrEmpty(pythonPath))
                {
                    string absPath = GetAbsolutePath(pythonPath);
                    if (File.Exists(absPath))
                    {
                        return absPath;
                    }
                }
            }
            return "";
        }
        
        
        /// <summary>
        /// 获取第一个有效的SSL证书文件路径
        /// </summary>
        /// <returns>SSL证书文件的绝对路径</returns>
        public string GetValidSSLCertPath()
        {
            foreach (string certPath in sslCertFiles)
            {
                if (!string.IsNullOrEmpty(certPath))
                {
                    string absPath = GetAbsolutePath(certPath);
                    if (File.Exists(absPath))
                    {
                        return absPath;
                    }
                }
            }
            return "";
        }
        
        /// <summary>
        /// 获取第一个有效的SSL证书目录路径
        /// </summary>
        /// <returns>SSL证书目录的绝对路径</returns>
        public string GetValidSSLCertDirectory()
        {
            foreach (string certDir in sslCertDirectories)
            {
                if (!string.IsNullOrEmpty(certDir))
                {
                    string absPath = GetAbsolutePath(certDir);
                    if (Directory.Exists(absPath))
                    {
                        return absPath;
                    }
                }
            }
            return "";
        }
        
        /// <summary>
        /// 自动检测所有路径
        /// </summary>
        public void AutoDetectAllPaths()
        {
            Debug.Log("开始自动检测所有路径...");
            
            // 检测项目根目录
            if (string.IsNullOrEmpty(projectRootPath))
            {
                AutoDetectProjectRoot();
            }
            
            
            // 检测MCP服务器路径
            AutoDetectMCPServerPath();
            
            // 如果路径列表为空，则初始化默认值
            if (pythonExecutablePaths.Count == 0)
            {
                pythonExecutablePaths = new List<string>
                {
                    // Homebrew Apple Silicon (M1/M2)
                    "/opt/homebrew/bin/python3.11",
                    "/opt/homebrew/Cellar/python@3.11/3.11.13/bin/python3.11",
                    "/opt/homebrew/Cellar/python@3.11/3.11.12/bin/python3.11",
                    "/opt/homebrew/Cellar/python@3.11/3.11.11/bin/python3.11",
                    "/opt/homebrew/Cellar/python@3.11/3.11.10/bin/python3.11",
                    "/opt/homebrew/Cellar/python@3.11/3.11.9/bin/python3.11",
                    "/opt/homebrew/opt/python@3.11/bin/python3.11",
                    "/opt/homebrew/opt/python@3.11/bin/python3",
                    
                    // Homebrew Intel Mac
                    "/usr/local/bin/python3.11",
                    "/usr/local/opt/python@3.11/bin/python3.11",
                    "/usr/local/opt/python@3.11/bin/python3",
                    "/usr/local/Cellar/python@3.11/3.11.13/bin/python3.11",
                    "/usr/local/Cellar/python@3.11/3.11.12/bin/python3.11",
                    "/usr/local/Cellar/python@3.11/3.11.11/bin/python3.11",
                    "/usr/local/Cellar/python@3.11/3.11.10/bin/python3.11",
                    "/usr/local/Cellar/python@3.11/3.11.9/bin/python3.11",
                    
                    // MacPorts
                    "/opt/local/bin/python3.11",
                    "/opt/local/Library/Frameworks/Python.framework/Versions/3.11/bin/python3.11",
                    
                    // pyenv
                    "~/.pyenv/versions/3.11.13/bin/python",
                    "~/.pyenv/versions/3.11.12/bin/python",
                    "~/.pyenv/versions/3.11.11/bin/python",
                    "~/.pyenv/versions/3.11.10/bin/python",
                    "~/.pyenv/versions/3.11.9/bin/python",
                    "~/.pyenv/versions/3.11.8/bin/python",
                    "~/.pyenv/versions/3.11.7/bin/python",
                    "~/.pyenv/versions/3.11.6/bin/python",
                    "~/.pyenv/versions/3.11.5/bin/python",
                    "~/.pyenv/shims/python3.11",
                    
                    // 标准系统位置
                    "/usr/local/bin/python3.11",
                    "/usr/bin/python3.11",
                    
                    // Python.org官方安装
                    "/Library/Frameworks/Python.framework/Versions/3.11/bin/python3.11",
                    "/Library/Frameworks/Python.framework/Versions/3.11/bin/python3",
                    
                    // conda/miniconda/anaconda
                    "~/anaconda3/envs/python311/bin/python",
                    "~/miniconda3/envs/python311/bin/python",
                    "~/miniforge3/envs/python311/bin/python",
                    "/opt/anaconda3/envs/python311/bin/python",
                    "/opt/miniconda3/envs/python311/bin/python",
                    "/opt/miniforge3/envs/python311/bin/python",
                    
                    // 其他常见位置
                    "/Applications/Python 3.11/python3.11",
                    "~/Library/Python/3.11/bin/python3.11"
                };
            }
            
            
            if (sslCertFiles.Count == 0)
            {
                sslCertFiles = new List<string>
                {
                    "/etc/ssl/cert.pem",
                    "/usr/local/etc/openssl/cert.pem",
                    "/opt/homebrew/etc/openssl/cert.pem",
                    "/System/Library/OpenSSL/certs/cert.pem"
                };
            }
            
            if (sslCertDirectories.Count == 0)
            {
                sslCertDirectories = new List<string>
                {
                    "/etc/ssl/certs",
                    "/usr/local/etc/openssl",
                    "/opt/homebrew/etc/openssl"
                };
            }
            
            Debug.Log("自动检测完成");
        }
        
        /// <summary>
        /// 验证所有路径配置
        /// </summary>
        public void ValidateAllPaths()
        {
            var (isValid, errors) = ValidateConfiguration();
            
            if (isValid)
            {
                Debug.Log("✓ 所有路径配置验证通过");
            }
            else
            {
                Debug.LogWarning("⚠ 路径配置验证失败:\n" + string.Join("\n", errors));
            }
        }
        
        /// <summary>
        /// 检查配置是否有效
        /// </summary>
        public bool IsValid()
        {
            var (isValid, _) = ValidateConfiguration();
            return isValid;
        }
        
        
        private string GetProjectRootPath()
        {
            string dataPath = Application.dataPath;
            return Path.GetDirectoryName(dataPath);
        }
    }
}