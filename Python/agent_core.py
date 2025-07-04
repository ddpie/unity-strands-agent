"""
Unity AI Agent 核心模块
为Unity集成封装Strands Agent SDK，配置Unity开发相关工具
"""

import sys
import os
import ssl
# 确保使用UTF-8编码
if sys.version_info >= (3, 7):
    if hasattr(sys, 'set_int_max_str_digits'):
        sys.set_int_max_str_digits(0)
os.environ['PYTHONIOENCODING'] = 'utf-8'

# 配置SSL证书路径 - Unity环境特殊处理
def configure_ssl_for_unity():
    """为Unity环境配置SSL证书"""
    try:
        import certifi
        # 使用certifi提供的证书束
        cert_path = certifi.where()
        
        # 验证证书文件存在
        if os.path.exists(cert_path):
            os.environ['SSL_CERT_FILE'] = cert_path
            os.environ['REQUESTS_CA_BUNDLE'] = cert_path
            os.environ['CURL_CA_BUNDLE'] = cert_path
            print(f"[Python] ✓ 使用certifi证书路径: {cert_path}")
            return True
        else:
            print(f"[Python] ⚠️ certifi证书文件不存在: {cert_path}")
            
    except ImportError as e:
        print(f"[Python] ⚠️ certifi不可用: {e}")
    
    # 尝试macOS系统证书路径
    macos_cert_paths = [
        '/etc/ssl/cert.pem',  # 标准位置
        '/usr/local/etc/openssl/cert.pem',  # Homebrew OpenSSL
        '/opt/homebrew/etc/openssl/cert.pem',  # Apple Silicon Homebrew
        '/System/Library/OpenSSL/certs/cert.pem',  # 系统OpenSSL
    ]
    
    for cert_path in macos_cert_paths:
        if os.path.exists(cert_path):
            os.environ['SSL_CERT_FILE'] = cert_path
            os.environ['REQUESTS_CA_BUNDLE'] = cert_path
            os.environ['CURL_CA_BUNDLE'] = cert_path
            print(f"[Python] ✓ 使用系统证书路径: {cert_path}")
            return True
    
    print("[Python] ⚠️ 未找到有效的SSL证书，将禁用SSL验证")
    return False

# 执行SSL配置
ssl_configured = configure_ssl_for_unity()

# 配置SSL上下文
try:
    import ssl
    if ssl_configured:
        print("[Python] ✓ SSL验证已启用，使用配置的证书")
    else:
        # 如果找不到证书，临时禁用SSL验证以确保连接
        import urllib3
        urllib3.disable_warnings(urllib3.exceptions.InsecureRequestWarning)
        
        # 设置环境变量禁用SSL验证
        os.environ['PYTHONHTTPSVERIFY'] = '0'
        os.environ['CURL_CA_BUNDLE'] = ''
        os.environ['REQUESTS_CA_BUNDLE'] = ''
        
        print("[Python] ⚠️ SSL验证已禁用 - 仅用于开发环境")
except Exception as e:
    print(f"[Python] SSL配置警告: {e}")
    pass

# 额外的SSL配置用于AWS请求
try:
    import boto3
    import botocore.config
    # 为boto3配置SSL设置
    if not ssl_configured:
        print("[Python] 为AWS Bedrock配置SSL设置")
except ImportError:
    pass

from strands import Agent
import json
import logging
import asyncio
from typing import Dict, Any, Optional

# 导入Strands Agent工具
try:
    from strands_tools import (
        file_read,
        file_write, 
        editor,
        python_repl,
        calculator,
        memory,
        current_time,
        shell,
        http_request
    )
    print("[Python] Strands工具导入成功")
    TOOLS_AVAILABLE = True
except ImportError as e:
    print(f"[Python] Strands工具导入失败: {e}")
    print("[Python] 将使用无工具模式")
    TOOLS_AVAILABLE = False

# Configure logging
logging.basicConfig(
    level=logging.INFO,
    format='%(asctime)s - %(name)s - %(levelname)s - %(message)s'
)
logger = logging.getLogger(__name__)

class UnityAgent:
    """
    Unity专用的Strands Agent封装类
    配置适合Unity开发的工具集合
    """
    
    def __init__(self):
        """使用Unity开发工具配置初始化代理"""
        try:
            # 配置Unity开发相关的工具集
            unity_tools = self._get_unity_tools()
            
            # 使用默认AWS Bedrock配置和工具创建代理
            if unity_tools:
                # 如果SSL未正确配置，为Agent添加SSL配置
                if not ssl_configured:
                    logger.warning("SSL证书配置失败，将使用不安全连接")
                
                self.agent = Agent(tools=unity_tools)
                logger.info(f"Unity代理初始化成功，配置了 {len(unity_tools)} 个工具")
                logger.info(f"可用工具: {[tool.__name__ for tool in unity_tools]}")
            else:
                self.agent = Agent()
                logger.info("Unity代理初始化成功（无工具模式）")
                
        except Exception as e:
            logger.error(f"代理初始化失败: {str(e)}")
            # 如果是SSL相关错误，提供更详细的错误信息
            if 'SSL' in str(e) or 'certificate' in str(e).lower():
                logger.error("SSL证书问题检测到，请检查网络连接和证书配置")
                logger.error("解决方案: 1) 检查网络连接 2) 更新系统证书 3) 联系管理员")
            raise
    
    def _get_unity_tools(self):
        """获取适合Unity开发的工具集合"""
        if not TOOLS_AVAILABLE:
            logger.warning("Strands工具不可用，返回空工具列表")
            return []
        
        unity_tools = []
        
        # 文件操作工具 - Unity项目文件管理
        try:
            unity_tools.extend([file_read, file_write, editor])
            logger.info("✓ 添加文件操作工具: file_read, file_write, editor")
        except (NameError, ImportError) as e:
            logger.warning(f"文件操作工具不可用: {e}")
        
        # Python执行工具 - 脚本测试和原型开发
        try:
            unity_tools.append(python_repl)
            logger.info("✓ 添加Python执行工具: python_repl")
        except (NameError, ImportError) as e:
            logger.warning(f"Python执行工具不可用: {e}")
        
        # 计算工具 - 数学计算、向量运算等
        try:
            unity_tools.append(calculator)
            logger.info("✓ 添加计算工具: calculator")
        except (NameError, ImportError) as e:
            logger.warning(f"计算工具不可用: {e}")
        
        # 记忆工具 - 记住项目上下文和用户偏好
        try:
            unity_tools.append(memory)
            logger.info("✓ 添加记忆工具: memory")
        except (NameError, ImportError) as e:
            logger.warning(f"记忆工具不可用: {e}")
        
        # 时间工具 - 获取当前时间，用于日志和时间戳
        try:
            unity_tools.append(current_time)
            logger.info("✓ 添加时间工具: current_time")
        except (NameError, ImportError) as e:
            logger.warning(f"时间工具不可用: {e}")
        
        # Shell工具 - 执行Unity CLI命令和构建脚本
        try:
            unity_tools.append(shell)
            logger.info("✓ 添加Shell工具: shell")
        except (NameError, ImportError) as e:
            logger.warning(f"Shell工具不可用: {e}")
        
        # HTTP工具 - 访问Unity文档、API等
        try:
            unity_tools.append(http_request)
            logger.info("✓ 添加HTTP工具: http_request")
        except (NameError, ImportError) as e:
            logger.warning(f"HTTP工具不可用: {e}")
        
        if unity_tools:
            logger.info(f"🎉 成功配置 {len(unity_tools)} 个Unity开发工具")
            logger.info(f"可用工具列表: {[tool.__name__ for tool in unity_tools]}")
        else:
            logger.warning("⚠️ 没有可用的Unity开发工具")
        
        return unity_tools
    
    def get_available_tools(self):
        """获取当前可用的工具列表"""
        try:
            # 尝试获取代理的工具信息
            if hasattr(self.agent, 'tools') and self.agent.tools:
                tool_names = []
                for tool in self.agent.tools:
                    if hasattr(tool, '__name__'):
                        tool_names.append(tool.__name__)
                    elif hasattr(tool, 'name'):
                        tool_names.append(tool.name)
                    else:
                        tool_names.append(str(type(tool).__name__))
                return tool_names
            elif hasattr(self.agent, 'tool_names'):
                return self.agent.tool_names
            else:
                logger.info("代理没有配置工具或工具信息不可访问")
                return []
        except Exception as e:
            logger.error(f"获取工具列表时出错: {e}")
            return []
    
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