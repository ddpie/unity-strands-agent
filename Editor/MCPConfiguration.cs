using UnityEngine;
using System;
using System.Collections.Generic;

namespace UnityAIAgent.Editor
{
    /// <summary>
    /// MCP传输类型
    /// </summary>
    public enum MCPTransportType
    {
        Stdio,              // 标准输入输出
        StreamableHttp,     // Streamable HTTP (推荐)
        SSE,                // Server-Sent Events (Legacy)
        HTTP                // HTTP (向后兼容，映射到StreamableHttp)
    }

    /// <summary>
    /// MCP服务器配置
    /// </summary>
    [Serializable]
    public class MCPServerConfig
    {
        [Header("基本信息")]
        public string name = "";
        public string description = "";
        public bool enabled = true;

        [Header("传输配置")]
        public MCPTransportType transportType = MCPTransportType.Stdio;
        
        [Header("Stdio配置")]
        public string command = "";
        public string[] args = new string[0];
        public string workingDirectory = "";

        [Header("HTTP配置")]
        public string httpUrl = "";
        public int timeoutSeconds = 30;

        [Header("环境变量")]
        public List<EnvironmentVariable> environmentVariables = new List<EnvironmentVariable>();

        [Header("高级选项")]
        public bool autoRestart = true;
        public int maxRetries = 3;
        public bool logOutput = false;
    }

    /// <summary>
    /// 环境变量配置
    /// </summary>
    [Serializable]
    public class EnvironmentVariable
    {
        public string key = "";
        public string value = "";
        public bool isSecret = false; // 是否为敏感信息，如API密钥
    }

    /// <summary>
    /// MCP配置管理器
    /// </summary>
    [CreateAssetMenu(fileName = "MCPConfig", menuName = "AI助手/MCP配置")]
    public class MCPConfiguration : ScriptableObject
    {
        [Header("全局设置")]
        public bool enableMCP = false;
        public int maxConcurrentConnections = 5;
        public int defaultTimeoutSeconds = 30;

        [Header("MCP服务器列表")]
        public List<MCPServerConfig> servers = new List<MCPServerConfig>();

        /// <summary>
        /// 获取启用的服务器配置
        /// </summary>
        public List<MCPServerConfig> GetEnabledServers()
        {
            var enabledServers = new List<MCPServerConfig>();
            foreach (var server in servers)
            {
                if (server.enabled)
                {
                    enabledServers.Add(server);
                }
            }
            return enabledServers;
        }


        /// <summary>
        /// 验证配置是否有效
        /// </summary>
        public bool ValidateConfiguration(out string errorMessage)
        {
            errorMessage = "";

            if (!enableMCP)
            {
                errorMessage = "MCP功能未启用";
                return true; // MCP未启用时返回true但提供说明
            }

            if (servers == null || servers.Count == 0)
            {
                errorMessage = "MCP已启用但没有配置任何服务器";
                return false;
            }

            var enabledServers = GetEnabledServers();
            if (enabledServers.Count == 0)
            {
                errorMessage = "MCP已启用但没有配置任何可用的服务器";
                return false;
            }

            foreach (var server in enabledServers)
            {
                if (string.IsNullOrEmpty(server.name))
                {
                    errorMessage = "服务器名称不能为空";
                    return false;
                }

                if (server.transportType == MCPTransportType.Stdio)
                {
                    if (string.IsNullOrEmpty(server.command))
                    {
                        errorMessage = $"服务器 '{server.name}' 的命令不能为空";
                        return false;
                    }
                }
                else if (server.transportType == MCPTransportType.HTTP || 
                         server.transportType == MCPTransportType.StreamableHttp || 
                         server.transportType == MCPTransportType.SSE)
                {
                    if (string.IsNullOrEmpty(server.httpUrl))
                    {
                        errorMessage = $"服务器 '{server.name}' 的HTTP URL不能为空";
                        return false;
                    }

                    if (!Uri.TryCreate(server.httpUrl, UriKind.Absolute, out _))
                    {
                        errorMessage = $"服务器 '{server.name}' 的HTTP URL格式无效";
                        return false;
                    }
                }
            }

            return true;
        }

        /// <summary>
        /// 生成Anthropic MCP配置JSON格式
        /// </summary>
        public string GenerateAnthropicMCPJson()
        {
            var json = "{\n  \"mcpServers\": {\n";
            var enabledServers = GetEnabledServers();
            
            for (int i = 0; i < enabledServers.Count; i++)
            {
                var server = enabledServers[i];
                json += $"    \"{server.name}\": {{\n";

                if (server.transportType == MCPTransportType.Stdio)
                {
                    // Stdio传输配置
                    json += $"      \"command\": \"{EscapeJsonString(server.command)}\",\n";
                    
                    // Args数组
                    json += "      \"args\": [";
                    if (server.args != null && server.args.Length > 0)
                    {
                        for (int j = 0; j < server.args.Length; j++)
                        {
                            json += $"\"{EscapeJsonString(server.args[j])}\"";
                            if (j < server.args.Length - 1) json += ", ";
                        }
                    }
                    json += "],\n";

                    // 环境变量
                    json += "      \"env\": {\n";
                    for (int j = 0; j < server.environmentVariables.Count; j++)
                    {
                        var envVar = server.environmentVariables[j];
                        if (!string.IsNullOrEmpty(envVar.key))
                        {
                            json += $"        \"{EscapeJsonString(envVar.key)}\": \"{EscapeJsonString(envVar.value)}\"";
                            if (j < server.environmentVariables.Count - 1) json += ",";
                            json += "\n";
                        }
                    }
                    json += "      }\n";
                }
                else if (server.transportType == MCPTransportType.HTTP || 
                         server.transportType == MCPTransportType.StreamableHttp || 
                         server.transportType == MCPTransportType.SSE)
                {
                    // 远程服务器配置
                    string transport;
                    switch (server.transportType)
                    {
                        case MCPTransportType.SSE:
                            transport = "sse";
                            break;
                        case MCPTransportType.StreamableHttp:
                            transport = "streamable_http";
                            break;
                        case MCPTransportType.HTTP:
                        default:
                            transport = "streamable_http"; // HTTP映射到streamable_http
                            break;
                    }
                    
                    json += $"      \"transport\": \"{transport}\",\n";
                    json += $"      \"url\": \"{EscapeJsonString(server.httpUrl)}\"";
                    
                    // 环境变量作为headers处理
                    if (server.environmentVariables.Count > 0)
                    {
                        json += ",\n      \"headers\": {\n";
                        for (int j = 0; j < server.environmentVariables.Count; j++)
                        {
                            var envVar = server.environmentVariables[j];
                            if (!string.IsNullOrEmpty(envVar.key))
                            {
                                json += $"        \"{EscapeJsonString(envVar.key)}\": \"{EscapeJsonString(envVar.value)}\"";
                                if (j < server.environmentVariables.Count - 1) json += ",";
                                json += "\n";
                            }
                        }
                        json += "      }\n";
                    }
                    else
                    {
                        json += "\n";
                    }
                }

                json += "    }";
                if (i < enabledServers.Count - 1) json += ",";
                json += "\n";
            }
            
            json += "  }\n}";
            return json;
        }

        /// <summary>
        /// 转义JSON字符串中的特殊字符
        /// </summary>
        private string EscapeJsonString(string str)
        {
            if (string.IsNullOrEmpty(str)) return "";
            
            return str.Replace("\\", "\\\\")
                     .Replace("\"", "\\\"")
                     .Replace("\n", "\\n")
                     .Replace("\r", "\\r")
                     .Replace("\t", "\\t");
        }

        /// <summary>
        /// 从Anthropic MCP JSON格式解析配置
        /// </summary>
        public bool LoadFromAnthropicJson(string jsonContent, out string errorMessage)
        {
            errorMessage = "";
            
            try
            {
                // 使用简单的JSON解析来处理Anthropic格式
                // 这里我们先验证基本格式，然后交给SetupWizard处理详细解析
                if (string.IsNullOrEmpty(jsonContent.Trim()))
                {
                    errorMessage = "JSON内容为空";
                    return false;
                }
                
                if (!jsonContent.Contains("mcpServers"))
                {
                    errorMessage = "JSON格式错误：缺少 'mcpServers' 字段";
                    return false;
                }
                
                // 基本JSON结构验证
                var trimmed = jsonContent.Trim();
                if (!trimmed.StartsWith("{") || !trimmed.EndsWith("}"))
                {
                    errorMessage = "JSON格式错误：必须是一个有效的JSON对象";
                    return false;
                }
                
                return true;
            }
            catch (Exception ex)
            {
                errorMessage = $"解析JSON失败: {ex.Message}";
                return false;
            }
        }
        
        /// <summary>
        /// 解析Anthropic MCP JSON并应用到当前配置
        /// 注意：这是一个简化的解析器，用于基本的配置导入
        /// 复杂的JSON解析应该在SetupWizard中使用Unity的JsonUtility处理
        /// </summary>
        public bool ApplyAnthropicJsonConfig(string jsonContent, out string errorMessage)
        {
            errorMessage = "此方法已简化，请使用SetupWizard进行完整的JSON配置导入";
            
            // 基本验证
            if (string.IsNullOrWhiteSpace(jsonContent))
            {
                errorMessage = "JSON内容为空";
                return false;
            }
            
            if (!jsonContent.Contains("mcpServers"))
            {
                errorMessage = "JSON格式错误：缺少 'mcpServers' 字段";
                return false;
            }
            
            // 启用MCP
            enableMCP = true;
            
            // 提示用户使用SetupWizard进行完整配置
            Debug.LogWarning("MCPConfiguration.ApplyAnthropicJsonConfig: 请使用SetupWizard窗口进行完整的JSON配置导入");
            
            return true;
        }

        /// <summary>
        /// 生成Python MCP客户端配置代码
        /// </summary>
        public string GeneratePythonMCPConfig()
        {
            if (!enableMCP || GetEnabledServers().Count == 0)
            {
                return "# MCP未启用或无可用服务器\nmcp_servers = []";
            }

            var pythonCode = "# MCP服务器配置\n";
            pythonCode += "import os\n";
            pythonCode += "from mcp import stdio_client, streamablehttp_client\n";
            pythonCode += "from mcp.client.stdio import StdioServerParameters\n\n";

            pythonCode += "mcp_servers = [\n";

            foreach (var server in GetEnabledServers())
            {
                pythonCode += $"    # {server.name} - {server.description}\n";
                pythonCode += "    {\n";
                pythonCode += $"        'name': '{server.name}',\n";
                pythonCode += $"        'description': '{server.description}',\n";
                pythonCode += $"        'transport_type': '{server.transportType.ToString().ToLower()}',\n";

                if (server.transportType == MCPTransportType.Stdio)
                {
                    pythonCode += $"        'command': '{server.command}',\n";
                    if (server.args.Length > 0)
                    {
                        pythonCode += "        'args': [";
                        for (int i = 0; i < server.args.Length; i++)
                        {
                            pythonCode += $"'{server.args[i]}'";
                            if (i < server.args.Length - 1) pythonCode += ", ";
                        }
                        pythonCode += "],\n";
                    }
                    if (!string.IsNullOrEmpty(server.workingDirectory))
                    {
                        pythonCode += $"        'working_directory': '{server.workingDirectory}',\n";
                    }
                }
                else
                {
                    pythonCode += $"        'url': '{server.httpUrl}',\n";
                    pythonCode += $"        'timeout': {server.timeoutSeconds},\n";
                }

                if (server.environmentVariables.Count > 0)
                {
                    pythonCode += "        'env_vars': {\n";
                    foreach (var envVar in server.environmentVariables)
                    {
                        if (!string.IsNullOrEmpty(envVar.key))
                        {
                            pythonCode += $"            '{envVar.key}': os.getenv('{envVar.key}', '{envVar.value}'),\n";
                        }
                    }
                    pythonCode += "        },\n";
                }

                pythonCode += "    },\n";
            }

            pythonCode += "]\n";
            return pythonCode;
        }
    }
}