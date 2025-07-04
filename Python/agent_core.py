"""
Unity AI Agent 核心模块
为Unity集成封装Strands Agent SDK
"""

import sys
import os
import ssl
# 确保使用UTF-8编码
if sys.version_info >= (3, 7):
    if hasattr(sys, 'set_int_max_str_digits'):
        sys.set_int_max_str_digits(0)
os.environ['PYTHONIOENCODING'] = 'utf-8'

# 配置SSL证书路径
try:
    import certifi
    # 使用certifi提供的证书束
    cert_path = certifi.where()
    os.environ['SSL_CERT_FILE'] = cert_path
    os.environ['REQUESTS_CA_BUNDLE'] = cert_path
    os.environ['CURL_CA_BUNDLE'] = cert_path
    print(f"[Python] 使用certifi证书路径: {cert_path}")
    
    # 验证证书文件存在
    if os.path.exists(cert_path):
        print(f"[Python] 证书文件验证成功: {cert_path}")
    else:
        print(f"[Python] 警告: 证书文件不存在: {cert_path}")
        
except ImportError:
    # 如果certifi不可用，使用系统默认证书
    print("[Python] certifi不可用，使用系统默认SSL证书")
    os.environ['SSL_CERT_FILE'] = '/etc/ssl/cert.pem'
    os.environ['REQUESTS_CA_BUNDLE'] = '/etc/ssl/cert.pem'

# 配置SSL上下文
try:
    # 配置SSL上下文但保持验证
    import ssl
    # 不使用unverified context，而是确保使用正确的证书
    print("[Python] 保持SSL验证启用，使用配置的证书")
except Exception as e:
    print(f"[Python] SSL配置警告: {e}")
    pass

from strands import Agent
import json
import logging
import asyncio
from typing import Dict, Any, Optional

# Configure logging
logging.basicConfig(
    level=logging.INFO,
    format='%(asctime)s - %(name)s - %(levelname)s - %(message)s'
)
logger = logging.getLogger(__name__)

class UnityAgent:
    """
    Unity专用的Strands Agent封装类
    """
    
    def __init__(self):
        """使用默认配置初始化代理"""
        try:
            # 使用默认AWS Bedrock配置创建代理
            self.agent = Agent()
            logger.info("Unity代理初始化成功")
        except Exception as e:
            logger.error(f"代理初始化失败: {str(e)}")
            raise
    
    def process_message(self, message: str) -> Dict[str, Any]:
        """
        同步处理消息
        
        参数:
            message: 用户输入消息
            
        返回:
            包含响应或错误的字典
        """
        try:
            logger.info(f"正在处理消息: {message[:50]}...")
            response = self.agent(message)
            # 确保响应是UTF-8编码的字符串
            if isinstance(response, bytes):
                response = response.decode('utf-8')
            elif not isinstance(response, str):
                response = str(response)
            
            # 记录完整响应到日志
            logger.info(f"Agent同步响应完成，长度: {len(response)}字符")
            logger.info(f"Agent响应内容: {response[:200]}{'...' if len(response) > 200 else ''}")
            
            return {
                "success": True,
                "response": response,
                "type": "complete"
            }
        except Exception as e:
            logger.error(f"处理消息时出错: {str(e)}")
            return {
                "success": False,
                "error": str(e),
                "type": "error"
            }
    
    async def process_message_stream(self, message: str):
        """
        处理消息并返回流式响应
        
        参数:
            message: 用户输入消息
            
        生成:
            包含响应块的JSON字符串
        """
        try:
            logger.info(f"开始流式处理消息: {message[:50]}...")
            
            # 使用异步流式API
            async for chunk in self.agent.astream(message):
                yield json.dumps({
                    "type": "chunk",
                    "content": chunk,
                    "done": False
                })
            
            # 信号完成
            yield json.dumps({
                "type": "complete",
                "content": "",
                "done": True
            })
            
        except Exception as e:
            logger.error(f"流式处理出错: {str(e)}")
            yield json.dumps({
                "type": "error",
                "error": str(e),
                "done": True
            })
    
    def health_check(self) -> Dict[str, Any]:
        """
        检查代理是否健康且就绪
        
        返回:
            状态字典
        """
        try:
            # Simple health check - try to get agent info
            return {
                "status": "healthy",
                "agent_type": type(self.agent).__name__,
                "ready": True
            }
        except Exception as e:
            return {
                "status": "unhealthy",
                "error": str(e),
                "ready": False
            }

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