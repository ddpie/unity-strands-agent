using UnityEngine;
using System.IO;
using System;

namespace UnityAIAgent.Editor
{
    /// <summary>
    /// 简化的路径配置 - 专为Git URL安装优化
    /// </summary>
    [CreateAssetMenu(fileName = "PathConfiguration", menuName = "UnityAIAgent/Path Configuration")]
    public class PathConfiguration : ScriptableObject
    {
        [Header("项目根目录")]
        [Tooltip("Unity项目的根目录路径")]
        public string projectRootPath = "";
        
        [Header("Python配置")]
        [Tooltip("Python模块路径（自动检测或手动设置）")]
        public string strandsToolsPath = "";
        
        [Header("AWS配置")]
        [Tooltip("AWS Access Key ID")]
        public string awsAccessKey = "";
        
        [Tooltip("AWS Secret Access Key")]
        public string awsSecretKey = "";
        
        [Tooltip("AWS Region")]
        public string awsRegion = "us-east-1";
        
        /// <summary>
        /// 获取绝对路径
        /// </summary>
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
        /// 自动检测基础配置
        /// </summary>
        public void AutoDetectBasicConfiguration()
        {
            Debug.Log("[PathConfiguration] 开始自动检测基础配置...");
            
            // 检测项目根目录
            if (string.IsNullOrEmpty(projectRootPath))
            {
                projectRootPath = Path.GetDirectoryName(Application.dataPath);
                Debug.Log($"[PathConfiguration] 项目根目录: {projectRootPath}");
            }
            
            // 自动检测Python路径
            if (string.IsNullOrEmpty(strandsToolsPath))
            {
                // 首先尝试包中的Python目录
                var packagePythonPath = Path.Combine(projectRootPath, "Library/PackageCache");
                var packageDirs = Directory.GetDirectories(packagePythonPath, "com.ddpie.unity-strands-agent*");
                
                if (packageDirs.Length > 0)
                {
                    var pythonDir = Path.Combine(packageDirs[0], "Python");
                    if (Directory.Exists(pythonDir) && File.Exists(Path.Combine(pythonDir, "agent_core.py")))
                    {
                        strandsToolsPath = pythonDir;
                        Debug.Log($"[PathConfiguration] 检测到包中的Python路径: {strandsToolsPath}");
                    }
                }
                
                // 如果没找到，尝试项目目录中的Python
                if (string.IsNullOrEmpty(strandsToolsPath))
                {
                    var projectPythonPath = Path.Combine(projectRootPath, "Python");
                    if (Directory.Exists(projectPythonPath) && File.Exists(Path.Combine(projectPythonPath, "agent_core.py")))
                    {
                        strandsToolsPath = projectPythonPath;
                        Debug.Log($"[PathConfiguration] 检测到项目中的Python路径: {strandsToolsPath}");
                    }
                }
            }
            
            Debug.Log("[PathConfiguration] 自动检测完成");
        }
        
        /// <summary>
        /// 验证配置是否有效
        /// </summary>
        public bool IsConfigurationValid()
        {
            bool isValid = !string.IsNullOrEmpty(strandsToolsPath) &&
                          Directory.Exists(strandsToolsPath) &&
                          File.Exists(Path.Combine(strandsToolsPath, "agent_core.py"));
            
            if (!isValid)
            {
                Debug.LogWarning("[PathConfiguration] 配置无效 - Python路径不存在或缺少agent_core.py");
            }
            
            return isValid;
        }
        
        /// <summary>
        /// 获取配置摘要
        /// </summary>
        public string GetConfigurationSummary()
        {
            var summary = new System.Text.StringBuilder();
            summary.AppendLine("=== Unity Strands Agent 配置摘要 ===");
            summary.AppendLine($"项目根目录: {projectRootPath}");
            summary.AppendLine($"Python模块路径: {strandsToolsPath}");
            summary.AppendLine($"AWS Region: {awsRegion}");
            summary.AppendLine($"AWS Access Key: {(string.IsNullOrEmpty(awsAccessKey) ? "未配置" : "已配置")}");
            summary.AppendLine($"AWS Secret Key: {(string.IsNullOrEmpty(awsSecretKey) ? "未配置" : "已配置")}");
            summary.AppendLine($"配置状态: {(IsConfigurationValid() ? "✓ 有效" : "✗ 无效")}");
            
            return summary.ToString();
        }
    }
}