"""
Unity AI Agent 核心模块
为Unity集成封装Strands Agent SDK，配置Unity开发相关工具
"""

import sys
import os
import json
import logging
from typing import Optional

# 基础配置和导入
from ssl_config import configure_ssl_for_unity, get_ssl_config

# 确保使用UTF-8编码
if sys.version_info >= (3, 7):
    if hasattr(sys, 'set_int_max_str_digits'):
        sys.set_int_max_str_digits(0)
os.environ['PYTHONIOENCODING'] = 'utf-8'

# SSL配置已移至独立模块ssl_config.py
# 执行SSL配置
ssl_configured = configure_ssl_for_unity()

# 获取SSL配置实例并配置AWS SSL
ssl_config_instance = get_ssl_config()
ssl_config_instance.configure_aws_ssl()

# 输出SSL配置状态
if ssl_configured:
    print("[Python] ✓ SSL验证已启用，使用配置的证书")
else:
    print("[Python] ⚠️ SSL验证已禁用 - 仅用于开发环境")

# 导入重构的模块
from unity_agent import UnityAgent

# Configure detailed logging for debugging
logging.basicConfig(
    level=logging.DEBUG,
    format='%(asctime)s - %(name)s - %(levelname)s - %(message)s',
    handlers=[
        logging.StreamHandler(),  # Console output
        # Unity will capture this via Python.NET
    ]
)

# Enable verbose logging for all related modules
logger = logging.getLogger(__name__)
logger.setLevel(logging.DEBUG)

# Enable Strands SDK logging
strands_logger = logging.getLogger("strands")
strands_logger.setLevel(logging.DEBUG)

# Enable HTTP/network logging
logging.getLogger("urllib3").setLevel(logging.DEBUG)
logging.getLogger("botocore").setLevel(logging.DEBUG)
logging.getLogger("boto3").setLevel(logging.DEBUG)

# Global agent instance
_agent_instance: Optional[UnityAgent] = None

def get_agent() -> UnityAgent:
    """
    获取或创建全局代理实例
    
    返回:
        UnityAgent实例
    """
    global _agent_instance
    if _agent_instance is None:
        _agent_instance = UnityAgent()
    return _agent_instance

# Unity直接调用的函数
def process_sync(message: str) -> str:
    """
    同步处理消息（供Unity调用）
    
    参数:
        message: 用户输入
        
    返回:
        包含响应的JSON字符串
    """
    agent = get_agent()
    result = agent.process_message(message)
    return json.dumps(result, ensure_ascii=False, separators=(',', ':'))

def health_check() -> str:
    """
    健康检查端点（供Unity调用）
    
    返回:
        包含状态的JSON字符串
    """
    agent = get_agent()
    result = agent.health_check()
    return json.dumps(result, ensure_ascii=False, separators=(',', ':'))

def reload_mcp_config() -> str:
    """
    重新加载MCP配置（供Unity调用）
    
    返回:
        包含结果的JSON字符串
    """
    global _agent_instance
    
    try:
        logger.info("=== 开始重新加载MCP配置 ===")
        
        # 清理现有的MCP资源
        if _agent_instance is not None:
            logger.info("清理现有MCP资源...")
            _agent_instance._cleanup_resources()
        
        # 重新创建代理实例
        logger.info("重新创建Unity代理实例...")
        _agent_instance = UnityAgent()
        
        # 获取新的MCP配置信息
        mcp_config = _agent_instance.mcp_manager._load_unity_mcp_config()
        
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
        return json.dumps(result, ensure_ascii=False, separators=(',', ':'))
        
    except Exception as e:
        logger.error(f"重新加载MCP配置失败: {e}")
        return json.dumps({
            "success": False,
            "message": f"重新加载MCP配置失败: {str(e)}",
            "error": str(e)
        }, ensure_ascii=False, separators=(',', ':'))

# 诊断函数（供Unity调用）
def test_unity_directory_call() -> str:
    """测试Unity调用时的工作目录"""
    from diagnostic_utils import test_unity_directory
    return test_unity_directory()

def diagnose_unity_mcp_issue_call() -> str:
    """诊断Unity环境下MCP连接问题"""
    from diagnostic_utils import diagnose_unity_mcp_issue
    return diagnose_unity_mcp_issue()

if __name__ == "__main__":
    # 测试代理
    print("测试Unity代理...")
    agent = get_agent()
    
    # 测试同步处理
    result = agent.process_message("你好，你能帮我做什么？")
    print(f"同步结果: {result}")
    
    # 测试健康检查
    health = agent.health_check()
    print(f"健康检查: {health}")