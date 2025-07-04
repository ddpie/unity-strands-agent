"""
Unity专用的非交互式工具版本
自动跳过用户确认，适合在Unity环境中自动执行
"""

import os
import logging
from typing import Any, Dict

logger = logging.getLogger(__name__)

def setup_non_interactive_environment():
    """设置非交互式环境变量"""
    os.environ["BYPASS_TOOL_CONSENT"] = "true"
    os.environ["PYTHON_REPL_INTERACTIVE"] = "false"
    os.environ["SHELL_DEFAULT_TIMEOUT"] = "60"
    logger.info("已设置非交互式环境变量")

def wrap_tool_for_unity(original_tool_func):
    """包装工具函数，确保非交互式执行"""
    def wrapped_tool(tool, **kwargs):
        # 强制设置非交互模式
        kwargs["non_interactive_mode"] = True
        
        # 临时设置环境变量
        old_bypass = os.environ.get("BYPASS_TOOL_CONSENT", "")
        os.environ["BYPASS_TOOL_CONSENT"] = "true"
        
        try:
            return original_tool_func(tool, **kwargs)
        finally:
            # 恢复原来的环境变量
            if old_bypass:
                os.environ["BYPASS_TOOL_CONSENT"] = old_bypass
            else:
                os.environ.pop("BYPASS_TOOL_CONSENT", None)
    
    return wrapped_tool

class UnityToolManager:
    """Unity工具管理器，确保所有工具都以非交互模式运行"""
    
    def __init__(self):
        self.original_tools = {}
        self.setup_non_interactive_mode()
    
    def setup_non_interactive_mode(self):
        """设置非交互模式"""
        setup_non_interactive_environment()
        logger.info("Unity工具管理器已启用非交互模式")
    
    def register_tool(self, tool_name: str, tool_func):
        """注册工具的非交互版本"""
        self.original_tools[tool_name] = tool_func
        wrapped_func = wrap_tool_for_unity(tool_func)
        logger.info(f"已注册非交互式工具: {tool_name}")
        return wrapped_func
    
    def get_tool_config(self) -> Dict[str, Any]:
        """获取Unity专用的工具配置"""
        return {
            "bypass_consent": True,
            "non_interactive": True,
            "auto_approve": True,
            "timeout": 60,
            "error_handling": "continue"
        }

# 全局工具管理器实例
unity_tool_manager = UnityToolManager()