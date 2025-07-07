using UnityEngine;
using UnityEditor;
using System.IO;

namespace UnityAIAgent.Editor
{
    /// <summary>
    /// 路径管理器，统一管理和加载路径配置
    /// </summary>
    [InitializeOnLoad]
    public static class PathManager
    {
        private static PathConfiguration _pathConfig;
        
        /// <summary>
        /// 获取路径配置实例
        /// </summary>
        public static PathConfiguration PathConfig
        {
            get
            {
                if (_pathConfig == null)
                {
                    Debug.Log("PathConfig为空，重新加载配置");
                    LoadPathConfiguration();
                }
                return _pathConfig;
            }
        }
        
        static PathManager()
        {
            // Unity启动时自动加载路径配置
            LoadPathConfiguration();
        }
        
        /// <summary>
        /// 加载路径配置
        /// </summary>
        private static void LoadPathConfiguration()
        {
            string configPath = "Assets/UnityAIAgent/PathConfiguration.asset";
            
            // 添加调试信息
            Debug.Log($"尝试加载路径配置: {configPath}");
            
            // 检查文件是否物理存在
            string fullPath = System.IO.Path.Combine(Application.dataPath.Replace("Assets", ""), configPath);
            bool fileExists = System.IO.File.Exists(fullPath);
            Debug.Log($"配置文件物理存在: {fileExists}, 路径: {fullPath}");
            
            _pathConfig = AssetDatabase.LoadAssetAtPath<PathConfiguration>(configPath);
            
            if (_pathConfig == null)
            {
                if (fileExists)
                {
                    Debug.LogWarning("配置文件存在但加载失败，可能需要刷新AssetDatabase");
                    AssetDatabase.Refresh();
                    _pathConfig = AssetDatabase.LoadAssetAtPath<PathConfiguration>(configPath);
                }
                
                if (_pathConfig == null)
                {
                    Debug.Log("路径配置文件不存在或加载失败，创建默认配置");
                    CreateDefaultConfiguration();
                }
                else
                {
                    Debug.Log("刷新后成功加载路径配置");
                }
            }
            else
            {
                Debug.Log($"已加载路径配置: {_pathConfig.name}");
            }
        }
        
        /// <summary>
        /// 创建默认配置
        /// </summary>
        private static void CreateDefaultConfiguration()
        {
            string directory = "Assets/UnityAIAgent";
            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }
            
            _pathConfig = ScriptableObject.CreateInstance<PathConfiguration>();
            _pathConfig.InitializeDefaults();
            
            string configPath = Path.Combine(directory, "PathConfiguration.asset");
            AssetDatabase.CreateAsset(_pathConfig, configPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            
            Debug.Log($"已创建默认路径配置：{configPath}");
        }
        
        /// <summary>
        /// 重新加载配置
        /// </summary>
        public static void ReloadConfiguration()
        {
            _pathConfig = null;
            LoadPathConfiguration();
        }
        
        /// <summary>
        /// 获取项目根目录
        /// </summary>
        public static string GetProjectRootPath()
        {
            return PathConfig?.projectRootPath ?? Path.GetDirectoryName(Application.dataPath);
        }
        
        /// <summary>
        /// 获取Strands工具路径
        /// </summary>
        public static string GetStrandsToolsPath()
        {
            return PathConfig?.strandsToolsPath ?? "";
        }
        
        /// <summary>
        /// 获取Unity Agent Python模块路径
        /// </summary>
        public static string GetUnityAgentPythonPath()
        {
            if (PathConfig == null) return "";
            
            string currentProjectPath = GetProjectRootPath();
            
            // 查找可能的路径，优先级排序
            string[] possiblePaths = new string[]
            {
                // 1. 优先使用配置的strandsToolsPath
                PathConfig.strandsToolsPath,
                // 2. 当前项目的Python目录
                Path.Combine(currentProjectPath, "Python"),
                // 3. 相邻目录查找（开发环境后备）
                Path.Combine(currentProjectPath, "..", "unity-strands-agent", "Python"),
                Path.Combine(currentProjectPath, "..", "..", "unity-strands-agent", "Python")
            };
            
            foreach (string path in possiblePaths)
            {
                if (string.IsNullOrEmpty(path)) continue;
                
                string normalizedPath = Path.GetFullPath(path);
                if (Directory.Exists(normalizedPath))
                {
                    // 验证这个目录包含agent_core.py
                    if (File.Exists(Path.Combine(normalizedPath, "agent_core.py")))
                    {
                        Debug.Log($"找到Unity Agent Python路径: {normalizedPath}");
                        return normalizedPath;
                    }
                }
            }
            
            return "";
        }
        
        /// <summary>
        /// 检查配置是否有效
        /// </summary>
        public static bool IsConfigurationValid()
        {
            if (PathConfig == null) return false;
            
            // 简单检查：验证strandsToolsPath是否存在且包含agent_core.py
            if (!string.IsNullOrEmpty(PathConfig.strandsToolsPath))
            {
                return Directory.Exists(PathConfig.strandsToolsPath) && 
                       File.Exists(Path.Combine(PathConfig.strandsToolsPath, "agent_core.py"));
            }
            
            return false;
        }
        
        /// <summary>
        /// 获取配置验证错误
        /// </summary>
        public static string[] GetConfigurationErrors()
        {
            if (PathConfig == null) return new string[] { "路径配置未加载" };
            
            var errors = new System.Collections.Generic.List<string>();
            
            if (string.IsNullOrEmpty(PathConfig.strandsToolsPath))
            {
                errors.Add("Strands工具路径未配置");
            }
            else if (!Directory.Exists(PathConfig.strandsToolsPath))
            {
                errors.Add("Strands工具路径不存在");
            }
            else if (!File.Exists(Path.Combine(PathConfig.strandsToolsPath, "agent_core.py")))
            {
                errors.Add("Strands工具路径中未找到agent_core.py");
            }
            
            return errors.ToArray();
        }
        
        /// <summary>
        /// 创建路径配置（公共方法供UI调用）
        /// </summary>
        public static void CreatePathConfiguration()
        {
            CreateDefaultConfiguration();
        }
    }
}