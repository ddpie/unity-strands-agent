"""
Unity开发工具配置模块
管理Unity Agent的预定义工具和MCP工具集成
"""

import sys
import os
import logging
import asyncio
from typing import List, Dict, Any, Optional

logger = logging.getLogger(__name__)

# 全局工具可用性标识
TOOLS_AVAILABLE = False
MCP_AVAILABLE = False

# 工具模块引用
file_read_module = None
file_write_module = None
editor_module = None
python_repl_module = None
calculator_module = None
memory_module = None
current_time_module = None
shell_module = None
http_request_module = None


class UnityToolsManager:
    """Unity开发工具管理器"""
    
    def __init__(self):
        self.tools_available = False
        self.mcp_available = False
        self.tool_modules = {}
        self.mcp_tools = []
        self._initialize_tools()
    
    def _initialize_tools(self):
        """初始化所有工具"""
        self._load_strands_tools()
        self._load_mcp_support()
    
    def _load_strands_tools(self):
        """加载Strands预定义工具"""
        global TOOLS_AVAILABLE, file_read_module, file_write_module, editor_module
        global python_repl_module, calculator_module, memory_module, current_time_module
        global shell_module, http_request_module
        
        try:
            # 从Unity PathManager获取strands tools路径
            # 注意：这里需要通过Unity C#接口获取路径配置
            # 暂时使用环境变量或配置文件作为后备方案
            strands_tools_path = os.environ.get('STRANDS_TOOLS_PATH', "/Users/caobao/projects/strands/tools/src")
            if strands_tools_path and strands_tools_path not in sys.path:
                sys.path.insert(0, strands_tools_path)
            
            # 导入预定义工具模块
            import strands_tools.file_read as file_read_module
            import strands_tools.file_write as file_write_module  
            import strands_tools.editor as editor_module
            import strands_tools.python_repl as python_repl_module
            import strands_tools.calculator as calculator_module
            import strands_tools.memory as memory_module
            import strands_tools.current_time as current_time_module
            import strands_tools.shell as shell_module
            import strands_tools.http_request as http_request_module
            
            # 存储工具模块引用
            self.tool_modules = {
                'file_read': file_read_module,
                'file_write': file_write_module,
                'editor': editor_module,
                'python_repl': python_repl_module,
                'calculator': calculator_module,
                'memory': memory_module,
                'current_time': current_time_module,
                'shell': shell_module,
                'http_request': http_request_module
            }
            
            print("[Python] Strands预定义工具导入成功")
            TOOLS_AVAILABLE = True
            self.tools_available = True
            
        except ImportError as e:
            print(f"[Python] Strands工具导入失败: {e}")
            print("[Python] 将使用无工具模式")
            TOOLS_AVAILABLE = False
            self.tools_available = False
    
    def _load_mcp_support(self):
        """加载MCP支持"""
        global MCP_AVAILABLE
        
        try:
            from mcp import StdioServerParameters, stdio_client
            from strands.tools.mcp import MCPClient as StrandsMCPClient
            import asyncio
            import subprocess
            import threading
            from datetime import timedelta
            from concurrent.futures import Future, ThreadPoolExecutor
            import weakref
            
            MCP_AVAILABLE = True
            self.mcp_available = True
            print("[Python] MCP支持加载成功")
            
        except ImportError as e:
            print(f"[Python] MCP支持加载失败: {e}")
            MCP_AVAILABLE = False
            self.mcp_available = False
    
    def get_unity_tools(self, include_mcp=True, agent_instance=None):
        """获取适合Unity开发的工具集合"""
        if not self.tools_available:
            logger.warning("Strands工具不可用，返回空工具列表")
            return []
        
        unity_tools = []
        
        # 文件操作工具 - Unity项目文件管理
        try:
            unity_tools.extend([
                self.tool_modules['file_read'],
                self.tool_modules['file_write'],
                self.tool_modules['editor']
            ])
            logger.info("✓ 添加文件操作工具: file_read, file_write, editor")
        except KeyError as e:
            logger.warning(f"文件操作工具不可用: {e}")

        # shell工具
        try:
            unity_tools.append(self.tool_modules['shell'])
            logger.info("✓ 添加shell工具: shell")
        except KeyError as e:
            logger.warning(f"shell工具不可用: {e}")
        
        # Python执行工具 - 脚本测试和原型开发
        try:
            unity_tools.append(self.tool_modules['python_repl'])
            logger.info("✓ 添加Python执行工具: python_repl")
        except KeyError as e:
            logger.warning(f"Python执行工具不可用: {e}")
        
        # 计算工具 - 数学计算、向量运算等
        try:
            unity_tools.append(self.tool_modules['calculator'])
            logger.info("✓ 添加计算工具: calculator")
        except KeyError as e:
            logger.warning(f"计算工具不可用: {e}")
        
        # 记忆工具 - 记住项目上下文和用户偏好
        try:
            unity_tools.append(self.tool_modules['memory'])
            logger.info("✓ 添加记忆工具: memory")
        except KeyError as e:
            logger.warning(f"记忆工具不可用: {e}")
        
        # 时间工具 - 获取当前时间，用于日志和时间戳
        try:
            unity_tools.append(self.tool_modules['current_time'])
            logger.info("✓ 添加时间工具: current_time")
        except KeyError as e:
            logger.warning(f"时间工具不可用: {e}")
        
        # HTTP工具 - 访问Unity文档、API等
        try:
            unity_tools.append(self.tool_modules['http_request'])
            logger.info("✓ 添加HTTP工具: http_request")
        except KeyError as e:
            logger.warning(f"HTTP工具不可用: {e}")
        
        # MCP工具 - 外部工具和服务集成
        if include_mcp and self.mcp_available and agent_instance:
            try:
                # 如果提供了agent实例，使用其MCP加载方法
                if hasattr(agent_instance, '_load_mcp_tools'):
                    mcp_tools = agent_instance._load_mcp_tools()
                    if mcp_tools:
                        unity_tools.extend(mcp_tools)
                        logger.info(f"✓ 添加MCP工具: {len(mcp_tools)} 个工具")
                        # 存储MCP工具引用
                        self.mcp_tools = mcp_tools
                else:
                    logger.warning("Agent实例不支持MCP工具加载")
            except Exception as e:
                logger.warning(f"MCP工具加载失败: {e}")
        else:
            if include_mcp and self.mcp_available:
                logger.info("ℹ️ MCP工具需要agent实例，跳过MCP工具加载")
            else:
                logger.info("ℹ️ MCP支持不可用，跳过MCP工具加载")
        
        if unity_tools:
            logger.info(f"🎉 成功配置 {len(unity_tools)} 个Unity开发工具")
            logger.info(f"可用工具列表: {[tool.__name__ if hasattr(tool, '__name__') else str(tool) for tool in unity_tools]}")
        else:
            logger.warning("⚠️ 没有可用的Unity开发工具")
        
        return unity_tools
    
    def _load_mcp_tools(self):
        """加载MCP工具"""
        if not self.mcp_available:
            logger.warning("MCP支持不可用")
            return []
        
        # 由于MCP工具加载逻辑复杂且依赖agent_core中的其他方法，
        # 这里返回空列表，实际的MCP工具加载仍在agent_core中处理
        logger.info("MCP工具加载将在agent_core中处理")
        return []
    
    def get_available_tool_names(self):
        """获取可用工具名称列表"""
        if not self.tools_available:
            return []
        
        base_tools = [
            "file_read", "file_write", "editor", "shell", 
            "python_repl", "calculator", "memory", 
            "current_time", "http_request"
        ]
        
        if self.mcp_available and self.mcp_tools:
            # 添加MCP工具名称
            mcp_names = [tool.name if hasattr(tool, 'name') else str(tool) for tool in self.mcp_tools]
            base_tools.extend(mcp_names)
        
        return base_tools
    
    def is_tools_available(self):
        """检查工具是否可用"""
        return self.tools_available
    
    def is_mcp_available(self):
        """检查MCP是否可用"""
        return self.mcp_available


# 全局工具管理器实例
_unity_tools_manager = None

def get_unity_tools_manager():
    """获取全局Unity工具管理器实例"""
    global _unity_tools_manager
    if _unity_tools_manager is None:
        _unity_tools_manager = UnityToolsManager()
    return _unity_tools_manager


# 便捷函数
def get_unity_tools(include_mcp=True, agent_instance=None):
    """获取Unity开发工具集合的便捷函数"""
    manager = get_unity_tools_manager()
    return manager.get_unity_tools(include_mcp, agent_instance)


def get_available_tool_names():
    """获取可用工具名称列表的便捷函数"""
    manager = get_unity_tools_manager()
    return manager.get_available_tool_names()


def is_tools_available():
    """检查工具是否可用的便捷函数"""
    manager = get_unity_tools_manager()
    return manager.is_tools_available()


def is_mcp_available():
    """检查MCP是否可用的便捷函数"""
    manager = get_unity_tools_manager()
    return manager.is_mcp_available()


# 向后兼容性：导出全局变量
def update_global_availability():
    """更新全局可用性变量"""
    global TOOLS_AVAILABLE, MCP_AVAILABLE
    manager = get_unity_tools_manager()
    TOOLS_AVAILABLE = manager.is_tools_available()
    MCP_AVAILABLE = manager.is_mcp_available()


# 初始化时更新全局变量
update_global_availability()