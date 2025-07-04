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
from tool_tracker import get_tool_tracker

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
            
            
            # 如果SSL未正确配置，为Agent添加SSL配置
            if not ssl_configured:
                logger.warning("SSL证书配置失败，将使用不安全连接")
            
            # 创建带工具的代理，包含Unity专用指令
            unity_system_prompt = """
你是Unity AI助手，专门为Unity游戏开发提供帮助。你擅长：

1. **Unity开发支持**：
   - C# 脚本编写和调试
   - Unity Editor 功能和工作流程
   - 游戏对象、组件、预制体管理
   - 场景管理和资源优化
   - 物理系统、动画、UI系统

2. **项目分析**：
   - 当用户询问项目分析时，请要求用户提供项目的具体信息
   - 分析项目结构、脚本架构、性能问题
   - 提供代码改进建议和最佳实践

3. **问题解决**：
   - 调试常见Unity错误
   - 性能优化建议
   - 跨平台开发指导

请用中文回复，提供详细、实用的建议。如果用户询问当前项目分析，请引导用户提供项目的具体文件、脚本或问题描述。

**重要提示**：
- 当用户要求执行shell命令时，优先使用file_read工具的find模式来列出目录内容
- 对于需要执行系统命令的场景，可以考虑使用python_repl工具通过Python的subprocess模块执行
- 避免直接使用shell工具，因为它可能需要交互式确认
"""
            
            # 尝试启用工具
            try:
                self.agent = Agent(system_prompt=unity_system_prompt, tools=unity_tools)
                logger.info(f"Unity代理初始化成功，已启用 {len(unity_tools)} 个工具")
                logger.info(f"启用的工具: {[tool.__name__ for tool in unity_tools]}")
            except Exception as e:
                logger.warning(f"带工具初始化失败: {e}，回退到无工具模式")
                self.agent = Agent(system_prompt=unity_system_prompt)
                logger.info("Unity代理初始化成功（无工具模式）")
            
            # 存储工具列表以供将来使用
            self._available_tools = unity_tools if unity_tools else []
                
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
        
        # Shell工具 - 使用Unity专用版本，避免交互式确认问题
        try:
            # 尝试导入自定义的Unity shell工具
            from unity_shell_tool import unity_shell
            unity_tools.append(unity_shell)
            logger.info("✓ 添加Shell工具: unity_shell（Unity专用版本）")
        except (NameError, ImportError) as e:
            # 如果自定义工具不可用，尝试使用原版（但可能有交互式问题）
            try:
                unity_tools.append(shell)
                logger.info("✓ 添加Shell工具: shell（注意：可能需要交互式确认）")
            except (NameError, ImportError) as e2:
                logger.warning(f"Shell工具不可用: {e}, {e2}")
        
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
            # 返回存储的工具列表（即使当前未启用）
            if hasattr(self, '_available_tools') and self._available_tools:
                return [tool.__name__ for tool in self._available_tools]
            
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
            
            # 获取工具跟踪器
            tool_tracker = get_tool_tracker()
            tool_tracker.reset()
            
            # 添加超时控制
            start_time = asyncio.get_event_loop().time()
            chunk_timeout = 300  # 5分钟总超时
            last_chunk_time = start_time
            chunk_interval_timeout = 60  # 60秒无新chunk超时
            
            # 使用Strands Agent的流式API
            logger.info("开始调用agent.stream_async()...")
            chunk_count = 0
            
            async for chunk in self.agent.stream_async(message):
                chunk_count += 1
                logger.info(f"收到第 {chunk_count} 个chunk: {str(chunk)[:200]}...")
                # 检查总超时
                current_time = asyncio.get_event_loop().time()
                if current_time - start_time > chunk_timeout:
                    logger.error(f"流式响应总超时，超过{chunk_timeout}秒")
                    yield json.dumps({
                        "type": "error",
                        "error": f"响应超时：处理时间超过{chunk_timeout}秒",
                        "done": True
                    }, ensure_ascii=False)
                    break
                
                # 检查chunk间隔超时
                if current_time - last_chunk_time > chunk_interval_timeout:
                    logger.error(f"chunk间隔超时，超过{chunk_interval_timeout}秒无新数据")
                    yield json.dumps({
                        "type": "error",
                        "error": f"响应中断：超过{chunk_interval_timeout}秒无新数据",
                        "done": True
                    }, ensure_ascii=False)
                    break
                
                last_chunk_time = current_time
                
                # 首先尝试提取工具调用信息
                if isinstance(chunk, dict) and 'event' in chunk:
                    logger.info(f"处理工具事件: {chunk['event']}")
                    tool_info = tool_tracker.process_event(chunk['event'])
                    if tool_info:
                        logger.info(f"生成工具信息chunk: {tool_info}")
                        yield json.dumps({
                            "type": "chunk",
                            "content": tool_info,
                            "done": False
                        }, ensure_ascii=False)
                
                # 然后提取常规文本内容
                text_content = self._extract_text_from_chunk(chunk)
                logger.debug(f"提取到文本内容: {text_content}")
                
                if text_content:
                    logger.info(f"生成文本chunk: {text_content}")
                    yield json.dumps({
                        "type": "chunk",
                        "content": text_content,
                        "done": False
                    }, ensure_ascii=False)
            
            # 信号完成
            logger.info(f"流式处理完成，总共处理了 {chunk_count} 个chunk")
            yield json.dumps({
                "type": "complete",
                "content": "",
                "done": True
            }, ensure_ascii=False)
            
        except Exception as e:
            logger.error(f"流式处理出错: {str(e)}")
            yield json.dumps({
                "type": "error",
                "error": str(e),
                "done": True
            }, ensure_ascii=False)
    
    def _extract_text_from_chunk(self, chunk):
        """从chunk中提取纯文本内容，过滤掉元数据，但保留工具调用信息"""
        try:
            # 如果是字符串，直接返回
            if isinstance(chunk, str):
                return chunk
            
            # 如果是字节，解码
            if isinstance(chunk, bytes):
                return chunk.decode('utf-8')
            
            # 如果是字典，尝试提取文本和工具信息
            if isinstance(chunk, dict):
                # 跳过元数据事件
                if any(key in chunk for key in ['init_event_loop', 'start', 'start_event_loop']):
                    return None
                
                # 检测工具调用事件
                if 'event' in chunk:
                    event = chunk['event']
                    
                    # 检测工具使用开始
                    if 'contentBlockStart' in event:
                        content_block = event['contentBlockStart']
                        if content_block.get('contentBlock', {}).get('type') == 'tool_use':
                            tool_name = content_block['contentBlock'].get('name', '未知工具')
                            return f"\n🔧 **正在调用工具: {tool_name}**\n"
                    
                    # 检测工具使用结束
                    if 'contentBlockStop' in event:
                        # 可以添加工具完成标记
                        return None
                    
                    # 提取常规文本内容
                    if 'contentBlockDelta' in event:
                        delta = event['contentBlockDelta']
                        if 'delta' in delta and 'text' in delta['delta']:
                            return delta['delta']['text']
                    
                    # 跳过其他事件类型
                    return None
                
                # 检测工具执行结果
                if 'tool_result' in chunk:
                    tool_result = chunk['tool_result']
                    tool_name = tool_result.get('tool_name', '未知工具')
                    success = tool_result.get('success', False)
                    if success:
                        return f"✅ **工具 {tool_name} 执行成功**\n"
                    else:
                        return f"❌ **工具 {tool_name} 执行失败**\n"
                
                # 跳过包含复杂元数据的响应
                if any(key in chunk for key in ['agent', 'event_loop_metrics', 'traces', 'spans']):
                    return None
                
                # 如果有text字段，提取它
                if 'text' in chunk:
                    return chunk['text']
                
                # 如果有content字段，提取它
                if 'content' in chunk:
                    return chunk['content']
            
            # 其他情况返回None，过滤掉
            return None
            
        except Exception as e:
            logger.warning(f"提取chunk文本时出错: {e}")
            return None
    
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