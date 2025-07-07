using UnityEngine;
using UnityEditor;
using System.IO;
using System;
using System.Collections.Generic;

namespace UnityAIAgent.Editor
{
    /// <summary>
    /// Python代码自动部署器
    /// 当插件通过Git URL安装时，自动将Python代码复制到项目目录
    /// </summary>
    [InitializeOnLoad]
    public class PythonAutoDeployer
    {
        private const string VERSION_FILE = "python_version.txt";
        private const string PYTHON_DIR = "Python";
        
        static PythonAutoDeployer()
        {
            // 延迟执行，确保Unity完全加载
            EditorApplication.delayCall += CheckAndDeployPython;
        }
        
        /// <summary>
        /// 检查并部署Python代码
        /// </summary>
        private static void CheckAndDeployPython()
        {
            try
            {
                if (ShouldDeployPython())
                {
                    Debug.Log("[PythonAutoDeployer] 检测到Git URL安装，开始自动部署Python代码");
                    DeployPythonToProject();
                }
                else
                {
                    Debug.Log("[PythonAutoDeployer] 使用本地开发模式，跳过Python代码部署");
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[PythonAutoDeployer] Python代码部署失败: {e.Message}");
            }
        }
        
        /// <summary>
        /// 判断是否需要部署Python代码
        /// </summary>
        private static bool ShouldDeployPython()
        {
            string packagePath = GetPackageInstallPath();
            
            // 如果包安装在PackageCache中（Git URL安装），则需要部署
            bool isGitInstall = packagePath.Contains("PackageCache") || packagePath.Contains("Library");
            
            if (!isGitInstall)
            {
                Debug.Log($"[PythonAutoDeployer] 本地开发模式，包路径: {packagePath}");
                return false;
            }
            
            // 检查版本是否需要更新
            string packageVersion = GetPackageVersion();
            string projectVersion = GetProjectPythonVersion();
            
            bool needsUpdate = packageVersion != projectVersion;
            
            if (needsUpdate)
            {
                Debug.Log($"[PythonAutoDeployer] 版本不匹配 - 包版本: {packageVersion}, 项目版本: {projectVersion}");
            }
            
            return needsUpdate;
        }
        
        /// <summary>
        /// 获取包的安装路径
        /// </summary>
        private static string GetPackageInstallPath()
        {
            try
            {
                string assemblyLocation = typeof(PythonAutoDeployer).Assembly.Location;
                return Path.GetDirectoryName(assemblyLocation) ?? "";
            }
            catch
            {
                return "";
            }
        }
        
        /// <summary>
        /// 获取包版本
        /// </summary>
        private static string GetPackageVersion()
        {
            try
            {
                string packagePath = GetPackageInstallPath();
                string packageJsonPath = Path.Combine(Path.GetDirectoryName(packagePath), "package.json");
                
                if (File.Exists(packageJsonPath))
                {
                    string json = File.ReadAllText(packageJsonPath);
                    // 简单的JSON解析获取版本号
                    var match = System.Text.RegularExpressions.Regex.Match(json, @"""version""\s*:\s*""([^""]+)""");
                    if (match.Success)
                    {
                        return match.Groups[1].Value;
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[PythonAutoDeployer] 无法获取包版本: {e.Message}");
            }
            
            return "unknown";
        }
        
        /// <summary>
        /// 获取项目中Python代码的版本
        /// </summary>
        private static string GetProjectPythonVersion()
        {
            try
            {
                string projectRoot = PathManager.GetProjectRootPath();
                string versionFilePath = Path.Combine(projectRoot, PYTHON_DIR, VERSION_FILE);
                
                if (File.Exists(versionFilePath))
                {
                    return File.ReadAllText(versionFilePath).Trim();
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[PythonAutoDeployer] 无法获取项目Python版本: {e.Message}");
            }
            
            return "";
        }
        
        /// <summary>
        /// 部署Python代码到项目目录
        /// </summary>
        private static void DeployPythonToProject()
        {
            try
            {
                string sourcePath = GetPackagePythonPath();
                string targetPath = GetProjectPythonPath();
                
                if (string.IsNullOrEmpty(sourcePath) || !Directory.Exists(sourcePath))
                {
                    Debug.LogError($"[PythonAutoDeployer] 源Python目录不存在: {sourcePath}");
                    return;
                }
                
                // 创建目标目录
                if (!Directory.Exists(targetPath))
                {
                    Directory.CreateDirectory(targetPath);
                    Debug.Log($"[PythonAutoDeployer] 创建目录: {targetPath}");
                }
                
                // 复制Python文件（排除venv目录）
                CopyPythonFiles(sourcePath, targetPath);
                
                // 写入版本文件
                string version = GetPackageVersion();
                string versionFilePath = Path.Combine(targetPath, VERSION_FILE);
                File.WriteAllText(versionFilePath, version);
                
                // 更新PathConfiguration
                UpdatePathConfiguration(targetPath);
                
                Debug.Log($"[PythonAutoDeployer] Python代码部署成功，版本: {version}");
                
                // 刷新AssetDatabase
                AssetDatabase.Refresh();
            }
            catch (Exception e)
            {
                Debug.LogError($"[PythonAutoDeployer] 部署失败: {e.Message}");
                throw;
            }
        }
        
        /// <summary>
        /// 复制Python文件
        /// </summary>
        private static void CopyPythonFiles(string sourceDir, string targetDir)
        {
            var excludePatterns = new HashSet<string> { "venv", "__pycache__", ".pyc", ".git" };
            
            foreach (string file in Directory.GetFiles(sourceDir))
            {
                string fileName = Path.GetFileName(file);
                string extension = Path.GetExtension(file);
                
                // 跳过某些文件类型
                if (extension == ".meta" || excludePatterns.Contains(fileName))
                    continue;
                
                string targetFile = Path.Combine(targetDir, fileName);
                File.Copy(file, targetFile, true);
                Debug.Log($"[PythonAutoDeployer] 复制文件: {fileName}");
            }
            
            // 递归复制子目录（排除venv等）
            foreach (string dir in Directory.GetDirectories(sourceDir))
            {
                string dirName = Path.GetFileName(dir);
                
                if (excludePatterns.Contains(dirName))
                    continue;
                
                string targetSubDir = Path.Combine(targetDir, dirName);
                if (!Directory.Exists(targetSubDir))
                {
                    Directory.CreateDirectory(targetSubDir);
                }
                
                CopyPythonFiles(dir, targetSubDir);
            }
        }
        
        /// <summary>
        /// 获取包中的Python路径
        /// </summary>
        private static string GetPackagePythonPath()
        {
            string packagePath = GetPackageInstallPath();
            return Path.Combine(Path.GetDirectoryName(packagePath), PYTHON_DIR);
        }
        
        /// <summary>
        /// 获取项目中的Python路径
        /// </summary>
        private static string GetProjectPythonPath()
        {
            string projectRoot = PathManager.GetProjectRootPath();
            return Path.Combine(projectRoot, PYTHON_DIR);
        }
        
        /// <summary>
        /// 更新PathConfiguration设置
        /// </summary>
        private static void UpdatePathConfiguration(string pythonPath)
        {
            try
            {
                var pathConfig = PathManager.PathConfig;
                if (pathConfig != null)
                {
                    pathConfig.strandsToolsPath = pythonPath;
                    EditorUtility.SetDirty(pathConfig);
                    AssetDatabase.SaveAssets();
                    Debug.Log($"[PythonAutoDeployer] 更新PathConfiguration.strandsToolsPath = {pythonPath}");
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[PythonAutoDeployer] 更新PathConfiguration失败: {e.Message}");
            }
        }
        
        /// <summary>
        /// 手动检查并更新Python代码（供用户调用）
        /// </summary>
        [MenuItem("Tools/Unity AI Agent/Update Python Code")]
        public static void ManualUpdatePython()
        {
            if (EditorUtility.DisplayDialog(
                "更新Python代码",
                "这将覆盖项目中的Python文件，确定继续？",
                "确定", "取消"))
            {
                try
                {
                    DeployPythonToProject();
                    EditorUtility.DisplayDialog("更新完成", "Python代码已成功更新", "确定");
                }
                catch (Exception e)
                {
                    EditorUtility.DisplayDialog("更新失败", $"更新失败: {e.Message}", "确定");
                }
            }
        }
    }
}