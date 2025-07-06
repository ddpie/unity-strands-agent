"""
MCP（Model Context Protocol）管理器
负责处理MCP服务器连接、工具加载和资源管理
"""

import json
import logging
import os
from typing import List, Dict, Any, Optional

# 配置日志
logger = logging.getLogger(__name__)

# MCP支持检查
MCP_AVAILABLE = False
try:
    from mcp_client import MCPClient, MCPClientInitializationError
    from mcp import StdioServerParameters, stdio_client
    from strands.tools.mcp import MCPClient as StrandsMCPClient
    MCP_AVAILABLE = True
    logger.info("MCP支持模块导入成功")
except ImportError as e:
    logger.warning(f"MCP模块导入失败: {e}")
    logger.warning("将使用无MCP模式")

class MCPManager:
    """MCP管理器，负责管理MCP服务器连接和工具"""
    
    def __init__(self):
        """初始化MCP管理器"""
        self._mcp_clients = []
        self._mcp_tools = []
        self._config = None
        
    def cleanup(self):
        """清理所有MCP资源"""
        try:
            # 清理MCP客户端
            if hasattr(self, '_mcp_clients'):
                for client in self._mcp_clients:
                    try:
                        # 正确退出上下文管理器
                        client.__exit__(None, None, None)
                    except Exception as e:
                        logger.warning(f"清理MCP客户端时出错: {e}")
                self._mcp_clients.clear()
            
            # 清理MCP工具
            if hasattr(self, '_mcp_tools'):
                for tool in self._mcp_tools:
                    try:
                        if hasattr(tool, '_cleanup'):
                            tool._cleanup()
                    except Exception as e:
                        logger.warning(f"清理MCP工具时出错: {e}")
                self._mcp_tools.clear()
                
            logger.info("MCP资源清理完成")
            
        except Exception as e:
            logger.warning(f"清理MCP资源时出错: {e}")
    
    def load_mcp_tools(self):
        """加载MCP工具"""
        if not MCP_AVAILABLE:
            logger.warning("MCP支持不可用")
            return []
        
        mcp_tools = []
        
        try:
            # 尝试读取Unity MCP配置
            mcp_config = self._load_unity_mcp_config()
            
            if not mcp_config:
                logger.warning("MCP配置加载失败")
                return []
            
            logger.info(f"MCP配置内容: enable_mcp={mcp_config.get('enable_mcp')}, servers数量={len(mcp_config.get('servers', []))}")
            
            if not mcp_config.get('enable_mcp', False):
                logger.info("MCP未启用")
                return []
            
            enabled_servers = [server for server in mcp_config.get('servers', []) if server.get('enabled', False)]
            
            if not enabled_servers:
                logger.info("没有启用的MCP服务器")
                return []
            
            logger.info(f"发现 {len(enabled_servers)} 个启用的MCP服务器")
            
            for server_config in enabled_servers:
                try:
                    server_name = server_config.get('name', 'unknown')
                    logger.info(f"连接到MCP服务器 '{server_name}'...")
                    
                    # 创建Strands MCPClient
                    mcp_client = self._create_strands_mcp_client(server_config)
                    
                    if mcp_client:
                        # 手动进入上下文管理器并保持连接
                        mcp_client.__enter__()
                        
                        # 保存客户端引用以便后续使用和清理
                        self._mcp_clients.append(mcp_client)
                        
                        try:
                            logger.info(f"获取MCP服务器 '{server_name}' 的工具列表...")
                            # 使用Strands MCPClient的正确方法
                            raw_tools = mcp_client.list_tools_sync()
                            
                            logger.info(f"MCP客户端类型: {type(mcp_client)}")
                            logger.info(f"返回的工具类型: {type(raw_tools)}")
                            logger.info(f"工具内容: {raw_tools}")
                            
                            if raw_tools:
                                logger.info(f"找到 {len(raw_tools)} 个工具:")
                                for i, tool in enumerate(raw_tools):
                                    tool_name = getattr(tool, 'name', f'tool_{i}')
                                    tool_desc = getattr(tool, 'description', 'No description')
                                    logger.info(f"  - {tool_name}: {tool_desc}")
                                
                                # 添加工具到列表 - Strands MCPClient返回的工具可以直接使用
                                mcp_tools.extend(raw_tools)
                                logger.info(f"从 '{server_name}' 加载了 {len(raw_tools)} 个工具")
                            else:
                                logger.warning(f"MCP服务器 '{server_name}' 没有可用工具")
                        except Exception as tool_error:
                            logger.error(f"获取工具列表失败: {tool_error}")
                            # 如果获取工具失败，从客户端列表中移除并关闭
                            if mcp_client in self._mcp_clients:
                                self._mcp_clients.remove(mcp_client)
                            try:
                                mcp_client.__exit__(None, None, None)
                            except:
                                pass
                            raise
                except Exception as e:
                    logger.error(f"加载MCP服务器 '{server_config.get('name', 'unknown')}' 失败: {e}")
                    logger.error(f"错误类型: {type(e).__name__}")
                    import traceback
                    logger.error(f"堆栈跟踪:\n{traceback.format_exc()}")
                    continue
            
            logger.info(f"总共加载了 {len(mcp_tools)} 个MCP工具")
            self._mcp_tools = mcp_tools
            
        except Exception as e:
            logger.error(f"MCP工具加载过程中出现错误: {e}")
        
        return mcp_tools
    
    def _load_unity_mcp_config(self):
        """从Unity加载MCP配置"""
        try:
            # 调试：打印当前工作目录
            current_dir = os.getcwd()
            logger.info(f"当前Python工作目录: {current_dir}")
            
            # 优先使用项目根目录环境变量构建MCP配置路径
            project_root = os.environ.get('PROJECT_ROOT_PATH')
            if project_root:
                # 使用项目根目录 + 相对路径
                mcp_config_path = os.path.join(project_root, "Assets/UnityAIAgent/mcp_config.json")
                config_paths = [mcp_config_path]
                logger.info(f"使用项目根目录构建MCP配置路径: {mcp_config_path}")
            else:
                # 回退到原有的相对路径搜索逻辑
                logger.info("未找到PROJECT_ROOT_PATH环境变量，使用相对路径搜索")
                config_paths = [
                    "Assets/UnityAIAgent/mcp_config.json",
                    "../Assets/UnityAIAgent/mcp_config.json",
                    "../../Assets/UnityAIAgent/mcp_config.json",
                    "mcp_config.json"
                ]
            
            for config_path in config_paths:
                abs_path = os.path.abspath(config_path)
                logger.debug(f"检查配置路径: {config_path} -> {abs_path} (存在: {os.path.exists(config_path)})")
                if os.path.exists(config_path):
                    with open(config_path, 'r', encoding='utf-8') as f:
                        content = f.read()
                        logger.info(f"从 {config_path} 加载MCP配置")
                        logger.debug(f"JSON内容预览: {content[:200]}...")
                        
                        raw_config = json.loads(content)
                        
                        # 检测配置格式并转换
                        if 'mcpServers' in raw_config:
                            # Anthropic格式，需要转换为内部格式
                            logger.info("检测到Anthropic MCP配置格式")
                            logger.info(f"mcpServers数量: {len(raw_config.get('mcpServers', {}))}")
                            return self._convert_anthropic_config(raw_config)
                        else:
                            # Legacy格式，直接使用
                            logger.info("检测到Legacy MCP配置格式")
                            return raw_config
            
            # 如果找不到配置文件，返回默认配置
            logger.info("未找到MCP配置文件，使用默认配置")
            return {
                "enable_mcp": False,
                "max_concurrent_connections": 3,
                "default_timeout_seconds": 30,
                "servers": []
            }
            
        except Exception as e:
            logger.warning(f"加载Unity MCP配置失败: {e}")
            return None
    
    def _convert_anthropic_config(self, anthropic_config):
        """将Anthropic MCP格式转换为内部格式"""
        try:
            mcp_servers = anthropic_config.get('mcpServers', {})
            converted_servers = []
            
            for server_name, server_config in mcp_servers.items():
                logger.info(f"转换服务器: {server_name}")
                logger.debug(f"服务器配置: {server_config}")
                
                converted_server = {
                    'name': server_name,
                    'enabled': True,  # Anthropic格式中启用的服务器默认为enabled
                    'description': f'MCP服务器: {server_name}',
                }
                
                # 处理不同的传输类型
                if 'command' in server_config:
                    # Stdio传输
                    converted_server.update({
                        'transport_type': 'stdio',
                        'command': server_config.get('command', ''),
                        'args': server_config.get('args', []),
                        'working_directory': server_config.get('working_directory', ''),
                        'env_vars': server_config.get('env', {})
                    })
                elif 'transport' in server_config and 'url' in server_config:
                    # 远程传输
                    transport = server_config.get('transport', 'streamable_http')
                    
                    # 映射传输类型
                    transport_mapping = {
                        'sse': 'sse',
                        'streamable_http': 'streamable_http',
                        'http': 'streamable_http',  # 默认使用streamable_http
                        'https': 'streamable_http'
                    }
                    
                    mapped_transport = transport_mapping.get(transport, 'streamable_http')
                    
                    converted_server.update({
                        'transport_type': mapped_transport,
                        'url': server_config.get('url', ''),
                        'timeout': 30,  # 默认超时
                        'headers': server_config.get('headers', {})
                    })
                    
                elif 'url' in server_config:
                    # 只有URL的情况，默认使用streamable_http
                    converted_server.update({
                        'transport_type': 'streamable_http',
                        'url': server_config.get('url', ''),
                        'timeout': 30,
                        'headers': server_config.get('headers', {})
                    })
                
                converted_servers.append(converted_server)
            
            # 返回转换后的配置
            converted_config = {
                'enable_mcp': len(converted_servers) > 0,
                'max_concurrent_connections': 5,
                'default_timeout_seconds': 30,
                'servers': converted_servers
            }
            
            logger.info(f"Anthropic格式转换完成，共 {len(converted_servers)} 个服务器")
            return converted_config
            
        except Exception as e:
            logger.error(f"转换Anthropic MCP配置失败: {e}")
            return {
                "enable_mcp": False,
                "max_concurrent_connections": 3,
                "default_timeout_seconds": 30,
                "servers": []
            }
    
    def _create_strands_mcp_client(self, server_config):
        """使用Strands MCPClient创建MCP客户端"""
        try:
            server_name = server_config.get('name', 'unknown')
            transport_type = server_config.get('transport_type', 'stdio')
            
            if transport_type == 'stdio':
                # 创建stdio MCP客户端 - 按照示例方式
                command = server_config.get('command')
                args = server_config.get('args', [])
                env = server_config.get('env', {}) or server_config.get('env_vars', {})
                
                if not command:
                    logger.warning(f"MCP服务器 '{server_name}' 缺少命令配置")
                    return None
                
                logger.info(f"=== 启动MCP服务器: {server_name} ===")
                logger.info(f"命令: {command}")
                logger.info(f"参数: {args}")
                logger.info(f"工作目录: 当前目录")
                logger.info(f"环境变量: {env}")
                
                # 创建stdio客户端工厂函数
                def stdio_factory():
                    return stdio_client(
                        StdioServerParameters(
                            command=command,
                            args=args,
                            env=env
                        )
                    )
                
                # 使用Strands MCPClient
                client = StrandsMCPClient(stdio_factory)
                logger.info(f"创建Strands MCP客户端: {command} {' '.join(args)}")
                return client
            else:
                logger.warning(f"暂不支持的传输类型: {transport_type}")
                return None
                
        except Exception as e:
            logger.error(f"创建Strands MCP客户端失败: {e}")
            import traceback
            logger.error(f"详细错误: {traceback.format_exc()}")
            return None
    
    def reload_config(self) -> Dict[str, Any]:
        """重新加载MCP配置"""
        try:
            logger.info("=== 开始重新加载MCP配置 ===")
            
            # 清理现有资源
            self.cleanup()
            
            # 重新加载配置
            mcp_config = self._load_unity_mcp_config()
            self._config = mcp_config
            
            if mcp_config:
                enabled_servers = [s for s in mcp_config.get('servers', []) if s.get('enabled', False)]
                result = {
                    "success": True,
                    "message": "MCP配置重新加载成功",
                    "mcp_enabled": mcp_config.get('enable_mcp', False),
                    "server_count": len(mcp_config.get('servers', [])),
                    "enabled_server_count": len(enabled_servers),
                    "servers": [{
                        "name": s.get('name'),
                        "transport_type": s.get('transport_type'),
                        "enabled": s.get('enabled')
                    } for s in mcp_config.get('servers', [])]
                }
            else:
                result = {
                    "success": False,
                    "message": "MCP配置加载失败",
                    "mcp_enabled": False,
                    "server_count": 0
                }
            
            logger.info(f"MCP配置重新加载结果: {result}")
            return result
            
        except Exception as e:
            logger.error(f"重新加载MCP配置失败: {e}")
            return {
                "success": False,
                "message": f"重新加载MCP配置失败: {str(e)}",
                "error": str(e)
            }