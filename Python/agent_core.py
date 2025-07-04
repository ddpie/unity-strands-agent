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
    import strands_tools.file_read as file_read_module
    import strands_tools.file_write as file_write_module  
    import strands_tools.editor as editor_module
    import strands_tools.python_repl as python_repl_module
    import strands_tools.calculator as calculator_module
    import strands_tools.memory as memory_module
    import strands_tools.current_time as current_time_module
    import strands_tools.shell as shell_module
    import strands_tools.http_request as http_request_module
    
    print("[Python] Strands工具模块导入成功")
    TOOLS_AVAILABLE = True
except ImportError as e:
    print(f"[Python] Strands工具导入失败: {e}")
    print("[Python] 将使用无工具模式")
    TOOLS_AVAILABLE = False

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

class UnityAgent:
    """
    Unity专用的Strands Agent封装类
    配置适合Unity开发的工具集合
    """
    
    def __init__(self):
        """使用Unity开发工具配置初始化代理"""
        try:
            logger.info("========== 初始化Unity Agent ==========")
            
            # 配置Unity开发相关的工具集
            logger.info("开始配置Unity工具集...")
            unity_tools = self._get_unity_tools()
            logger.info(f"工具集配置完成，数量: {len(unity_tools)}")
            
            # 如果SSL未正确配置，为Agent添加SSL配置
            if not ssl_configured:
                logger.warning("SSL证书配置失败，将使用不安全连接")
            
            # 创建带工具的代理，包含Unity专用指令
            unity_system_prompt = """
你是一位专家级的Unity AI助手，你的使命是与用户结对编程，高效地解决Unity开发中的各种挑战。你的风格是专业、友好、主动。请始终使用中文与用户交流。

你的核心能力包括：
- C#脚本编写、优化与调试
- Unity Editor工作流程与API使用
- 游戏对象、组件、预制体（Prefab）的高效管理
- 场景组织、资源优化与性能分析
- 物理、动画、UI（UGUI/UI Toolkit）系统
- 项目架构分析与代码重构建议
- 常见开发错误诊断与解决方案

工作流程指引：
- **主动沟通**：当用户问题不够清晰时，主动提出问题以澄清需求。
- **分步执行**：对于复杂的任务，先向用户说明你的计划，再分步执行。
- **代码质量**：生成的C#代码应遵循社区最佳实践，清晰、可读，并附上必要的注释。

---
**工具使用核心原则（必须严格遵守）：**

1.  **环境限制**：你运行在一个**非交互式**的环境中。任何需要用户在终端输入（如 'y/n' 确认）的工具都会导致系统卡死。**绝对不要**调用会触发交互式提示的命令。

2.  **文件/目录查看**：
    - **首选**：使用 `unity_shell` 工具执行简单的文件操作
    - **示例-列出目录**：`unity_shell(command="ls -la")`
    - **示例-查找C#文件**：`unity_shell(command="find . -name '*.cs' | head -10")`
    - **备选**：使用 `file_read` 工具查看文件内容
    - **示例-查看文件**：`file_read(path="Assets/Scripts/PlayerController.cs", mode="view")`

3.  **系统命令执行**：
    - **推荐**：使用 `unity_shell` 工具执行系统命令（已配置Unity项目目录）
    - **示例-获取当前目录**：`unity_shell(command="pwd")`
    - **示例-列出文件**：`unity_shell(command="ls -la")`
    - **备选方案**：使用 `python_repl` 工具和 Python 的 `subprocess` 模块

4.  **工具失败处理**：如果一个工具调用失败，分析错误信息，尝试用不同的方法或工具解决问题，或向用户解释情况并请求指示。不要盲目地重复失败的尝试。
"""
            
            # 尝试启用工具
            try:
                logger.info("开始创建Strands Agent...")
                logger.info(f"System prompt长度: {len(unity_system_prompt)}")
                logger.info(f"工具列表: {[str(tool) for tool in unity_tools]}")
                
                # 确保所有工具都设置为非交互模式
                from unity_non_interactive_tools import unity_tool_manager
                unity_tool_manager.setup_non_interactive_mode()
                
                self.agent = Agent(system_prompt=unity_system_prompt, tools=unity_tools)
                
                logger.info(f"Unity代理初始化成功，已启用 {len(unity_tools)} 个工具")
                logger.info(f"Agent对象类型: {type(self.agent)}")
                logger.info(f"Agent可用方法: {[method for method in dir(self.agent) if not method.startswith('_')]}")
                
            except Exception as e:
                logger.error(f"带工具初始化失败: {e}")
                logger.error(f"异常类型: {type(e).__name__}")
                import traceback
                logger.error(f"异常堆栈: {traceback.format_exc()}")
                
                logger.warning("回退到无工具模式...")
                try:
                    self.agent = Agent(system_prompt=unity_system_prompt)
                    logger.info("Unity代理初始化成功（无工具模式）")
                except Exception as e2:
                    logger.error(f"无工具模式也失败: {e2}")
                    raise
            
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
            unity_tools.extend([file_read_module, file_write_module, editor_module])
            logger.info("✓ 添加文件操作工具: file_read, file_write, editor")
        except (NameError, ImportError) as e:
            logger.warning(f"文件操作工具不可用: {e}")
        
        # Python执行工具 - 脚本测试和原型开发
        try:
            unity_tools.append(python_repl_module)
            logger.info("✓ 添加Python执行工具: python_repl")
        except (NameError, ImportError) as e:
            logger.warning(f"Python执行工具不可用: {e}")
        
        # 计算工具 - 数学计算、向量运算等
        try:
            unity_tools.append(calculator_module)
            logger.info("✓ 添加计算工具: calculator")
        except (NameError, ImportError) as e:
            logger.warning(f"计算工具不可用: {e}")
        
        # 记忆工具 - 记住项目上下文和用户偏好
        try:
            unity_tools.append(memory_module)
            logger.info("✓ 添加记忆工具: memory")
        except (NameError, ImportError) as e:
            logger.warning(f"记忆工具不可用: {e}")
        
        # 时间工具 - 获取当前时间，用于日志和时间戳
        try:
            unity_tools.append(current_time_module)
            logger.info("✓ 添加时间工具: current_time")
        except (NameError, ImportError) as e:
            logger.warning(f"时间工具不可用: {e}")
        
        # Shell工具 - 使用Unity专用版本，避免交互式确认问题
        try:
            # 导入Unity专用shell工具（符合Strands规范）
            from unity_shell_tool import unity_shell
            unity_tools.append(unity_shell)
            logger.info("✓ 添加Unity Shell工具: unity_shell（自动执行，默认Unity项目目录）")
        except (NameError, ImportError) as e:
            # 如果自定义工具不可用，尝试使用原版（但可能有交互式问题）
            try:
                unity_tools.append(shell_module)
                logger.info("✓ 回退到标准Shell工具: shell（注意：可能需要交互式确认）")
            except (NameError, ImportError) as e2:
                logger.warning(f"Shell工具不可用: {e}, {e2}")
        
        # HTTP工具 - 访问Unity文档、API等
        try:
            unity_tools.append(http_request_module)
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
            logger.info(f"============ 开始流式处理消息 ============")
            logger.info(f"消息内容: {message}")
            logger.info(f"Agent类型: {type(self.agent)}")
            logger.info(f"可用工具数量: {len(self._available_tools) if hasattr(self, '_available_tools') else 0}")
            
            # 获取工具跟踪器
            tool_tracker = get_tool_tracker()
            tool_tracker.reset()
            logger.info("工具跟踪器已重置")
            
            # 工具执行状态跟踪
            tool_start_time = None
            last_tool_progress_time = None
            
            start_time = asyncio.get_event_loop().time()
            
            # 使用Strands Agent的流式API
            logger.info("准备调用agent.stream_async()...")
            logger.info(f"Agent对象: {self.agent}")
            logger.info(f"Stream_async方法存在: {hasattr(self.agent, 'stream_async')}")
            
            chunk_count = 0
            
            logger.info("开始遍历流式响应...")
            
            # 在开始处理之前，立即输出一个测试消息
            yield json.dumps({
                "type": "chunk",
                "content": "\n🔧 **工具追踪系统已启用** - 如果AI使用工具，您将看到详细的执行过程\n",
                "done": False
            }, ensure_ascii=False)
            
            try:
                # 添加强制完成信号检测
                chunk_count = 0
                completed_normally = False
                
                async for chunk in self.agent.stream_async(message):
                    chunk_count += 1
                    current_time = asyncio.get_event_loop().time()
                    
                    logger.debug(f"========== Chunk #{chunk_count} ==========")
                    logger.debug(f"耗时: {current_time - start_time:.1f}s")
                    logger.debug(f"Chunk类型: {type(chunk)}")
                    logger.debug(f"Chunk内容: {str(chunk)[:500]}...")
                    
                    # 检查chunk中是否包含工具信息并记录详细日志
                    if isinstance(chunk, dict):
                        self._log_chunk_details(chunk, chunk_count)
                        
                        # 专门检查file_read工具调用
                        file_read_msg = self._check_file_read_tool(chunk, chunk_count)
                        if file_read_msg:
                            yield json.dumps({
                                "type": "chunk",
                                "content": file_read_msg,
                                "done": False
                            }, ensure_ascii=False)
                        
                        # 强制检查所有可能的工具调用格式并输出到聊天
                        tool_msg = self._force_check_tool_calls(chunk, chunk_count)
                        if tool_msg:
                            yield json.dumps({
                                "type": "chunk",
                                "content": tool_msg,
                                "done": False
                            }, ensure_ascii=False)
                    
                
                    # 提取工具调用信息
                    tool_info_generated = False
                    if isinstance(chunk, dict):
                        # 检查事件字段
                        if 'event' in chunk:
                            tool_info = tool_tracker.process_event(chunk['event'])
                            if tool_info:
                                logger.info(f"生成工具信息: {tool_info}")
                                yield json.dumps({
                                    "type": "chunk",
                                    "content": tool_info,
                                    "done": False
                                }, ensure_ascii=False)
                                tool_info_generated = True
                        
                        # 也检查是否直接包含工具相关信息
                        if any(key in chunk for key in ['contentBlockStart', 'contentBlockDelta', 'contentBlockStop', 'message']):
                            tool_info = tool_tracker.process_event(chunk)
                            if tool_info:
                                logger.info(f"生成工具信息: {tool_info}")
                                yield json.dumps({
                                    "type": "chunk",
                                    "content": tool_info,
                                    "done": False
                                }, ensure_ascii=False)
                                tool_info_generated = True
                        
                        # 检查是否有工具使用但未被上面的逻辑捕获
                        if 'type' in chunk and chunk['type'] == 'tool_use':
                            tool_name = chunk.get('name', '未知工具')
                            tool_input = chunk.get('input', {})
                            logger.info(f"检测到工具使用: {tool_name}")
                            yield json.dumps({
                                "type": "chunk", 
                                "content": f"\n🔧 **检测到工具调用**: {tool_name}\n   📋 输入参数: {json.dumps(tool_input, ensure_ascii=False)[:200]}...\n   ⏳ 开始执行...",
                                "done": False
                            }, ensure_ascii=False)
                            tool_info_generated = True
                    
                    # 然后提取常规文本内容
                    text_content = self._extract_text_from_chunk(chunk)
                    
                    if text_content:
                        logger.debug(f"提取文本内容: {text_content}")
                        yield json.dumps({
                            "type": "chunk",
                            "content": text_content,
                            "done": False
                        }, ensure_ascii=False)
                    elif not tool_info_generated:
                        # 如果既没有工具信息也没有文本内容，检查是否需要显示进度
                        if tool_tracker.current_tool:
                            # 检查工具是否执行时间过长
                            if tool_start_time is None:
                                tool_start_time = current_time
                                last_tool_progress_time = current_time
                            
                            # 每15秒显示一次进度
                            if current_time - last_tool_progress_time >= 15:
                                elapsed = current_time - tool_start_time
                                progress_msg = f"   ⏳ {tool_tracker.current_tool} 仍在执行中... (已执行 {elapsed:.1f}秒，处理了 {chunk_count} 个数据块)"
                                yield json.dumps({
                                    "type": "chunk",
                                    "content": progress_msg,
                                    "done": False
                                }, ensure_ascii=False)
                                last_tool_progress_time = current_time
                                
                                # 如果工具执行超过60秒，发出警告
                                if elapsed > 60:
                                    warning_msg = f"   ⚠️ 警告: {tool_tracker.current_tool} 执行时间已超过60秒，可能需要重新启动"
                                    yield json.dumps({
                                        "type": "chunk",
                                        "content": warning_msg,
                                        "done": False
                                    }, ensure_ascii=False)
                        else:
                            # 工具执行完成，重置时间
                            tool_start_time = None
                            last_tool_progress_time = None
                            # 静默跳过
                            logger.debug(f"跳过无内容chunk: {str(chunk)[:100]}")
                            pass
                
                # 标记正常完成
                completed_normally = True
                
                # 信号完成
                total_time = asyncio.get_event_loop().time() - start_time
                logger.info(f"流式处理完成，总共处理了 {chunk_count} 个chunk，耗时 {total_time:.1f}秒")
                
                # 检查是否有工具还在执行中
                if tool_tracker.current_tool:
                    yield json.dumps({
                        "type": "chunk",
                        "content": f"\n⚠️ 工具 {tool_tracker.current_tool} 可能仍在执行中或已完成但未收到结果\n",
                        "done": False
                    }, ensure_ascii=False)
                
                # 强制发送完成信号
                logger.info("=== 强制发送完成信号 ===")
                yield json.dumps({
                    "type": "complete",
                    "content": "",
                    "done": True
                }, ensure_ascii=False)
                
            except Exception as stream_error:
                logger.error(f"流式循环异常: {stream_error}")
                logger.error(f"流式异常类型: {type(stream_error).__name__}")
                import traceback
                logger.error(f"流式异常堆栈: {traceback.format_exc()}")
                yield json.dumps({
                    "type": "error",
                    "error": f"流式循环错误: {str(stream_error)}",
                    "done": True
                }, ensure_ascii=False)
                return
            
            # 如果没有正常完成，强制发送完成信号
            if not completed_normally:
                logger.warning("=== 流式处理未正常完成，强制发送完成信号 ===")
                yield json.dumps({
                    "type": "complete",
                    "content": "",
                    "done": True
                }, ensure_ascii=False)
                
            # 流式正常结束
            logger.info(f"流式响应正常结束，共处理{chunk_count}个chunk")
            
        except Exception as e:
            logger.error(f"========== 流式处理顶层异常 ==========")
            logger.error(f"异常类型: {type(e).__name__}")
            logger.error(f"异常消息: {str(e)}")
            logger.error(f"已处理chunk数量: {chunk_count if 'chunk_count' in locals() else 0}")
            import traceback
            logger.error(f"完整堆栈:")
            logger.error(traceback.format_exc())
            
            # 确保即使出错也发送完成信号
            yield json.dumps({
                "type": "error",
                "error": f"流式处理错误 ({type(e).__name__}): {str(e)}",
                "done": True
            }, ensure_ascii=False)
        finally:
            # 清理工具跟踪器状态
            try:
                tool_tracker = get_tool_tracker()
                tool_tracker.reset()
                logger.info("工具跟踪器状态已重置")
            except Exception as cleanup_error:
                logger.warning(f"清理工具跟踪器时出错: {cleanup_error}")
    
    def _log_chunk_details(self, chunk, chunk_count):
        """记录chunk的详细信息，特别是工具调用相关的信息"""
        try:
            if 'type' in chunk:
                logger.info(f"Chunk #{chunk_count} 类型: {chunk['type']}")
            
            if 'event' in chunk:
                event = chunk['event']
                if isinstance(event, dict):
                    if 'contentBlockStart' in event:
                        content_block = event['contentBlockStart'].get('contentBlock', {})
                        if content_block.get('type') == 'tool_use':
                            tool_name = content_block.get('name', '未知')
                            logger.info(f"🔧 工具调用开始: {tool_name}")
                            # 专门为file_read工具记录详细日志
                            if 'file_read' in tool_name:
                                logger.info(f"📖 [FILE_READ] 工具开始执行")
                    elif 'contentBlockDelta' in event:
                        logger.info(f"📋 工具参数更新中...")
                    elif 'contentBlockStop' in event:
                        logger.info(f"⏳ 工具调用准备完成")
                    elif 'message' in event:
                        logger.info(f"📥 收到消息事件")
            
            if any(key in chunk for key in ['contentBlockStart', 'contentBlockDelta', 'contentBlockStop', 'message']):
                logger.info(f"Chunk #{chunk_count} 包含工具相关信息")
        except Exception as e:
            logger.warning(f"记录chunk详情时出错: {e}")
    
    def _check_file_read_tool(self, chunk, chunk_count):
        """专门检查file_read工具的调用和结果"""
        try:
            # 检查工具调用开始
            if 'event' in chunk:
                event = chunk['event']
                if isinstance(event, dict):
                    if 'contentBlockStart' in event:
                        content_block = event['contentBlockStart'].get('contentBlock', {})
                        if content_block.get('type') == 'tool_use':
                            tool_name = content_block.get('name', '')
                            if 'file_read' in tool_name:
                                logger.info(f"📖 [FILE_READ] 检测到file_read工具调用开始 (Chunk #{chunk_count})")
                                return f"\n📖 **[FILE_READ]** 工具调用开始 (Chunk #{chunk_count})\n   🔍 准备读取文件..."
                    
                    elif 'contentBlockDelta' in event:
                        delta = event['contentBlockDelta']
                        if 'delta' in delta and 'input' in delta['delta']:
                            input_data = delta['delta']['input']
                            if 'path' in input_data or 'file_path' in input_data:
                                file_path = input_data.get('path') or input_data.get('file_path')
                                logger.info(f"📖 [FILE_READ] 检测到文件路径参数: {file_path}")
                                return f"   📂 **[FILE_READ]** 目标文件: {file_path}"
                    
                    elif 'contentBlockStop' in event:
                        # 检查当前是否是file_read工具
                        tool_tracker = get_tool_tracker()
                        if tool_tracker.current_tool and 'file_read' in tool_tracker.current_tool:
                            logger.info(f"📖 [FILE_READ] 工具参数准备完成，开始执行文件读取...")
                            return f"   ⏳ **[FILE_READ]** 参数准备完成，开始读取文件..."
            
            # 检查工具执行结果
            if 'message' in chunk:
                message = chunk['message']
                if 'content' in message:
                    for content in message['content']:
                        if content.get('type') == 'tool_result':
                            # 检查是否是file_read的结果
                            result = content.get('content', [])
                            if result and isinstance(result, list) and len(result) > 0:
                                result_text = result[0].get('text', '')
                                # 简单检查是否可能是文件内容
                                if len(result_text) > 100:  # 假设文件内容较长
                                    logger.info(f"📖 [FILE_READ] 检测到可能的文件读取结果，长度: {len(result_text)}字符")
                                    lines = result_text.split('\n')
                                    return f"   ✅ **[FILE_READ]** 文件读取完成\n   📄 文件大小: {len(result_text)}字符，{len(lines)}行\n   📝 内容预览: {result_text[:100]}..."
            
            return None
        except Exception as e:
            logger.warning(f"检查file_read工具时出错: {e}")
            return None

    def _force_check_tool_calls(self, chunk, chunk_count):
        """强制检查chunk中的工具调用信息，返回要输出到聊天的消息"""
        try:
            # 检查所有可能包含工具信息的字段
            found_tool_info = False
            detected_pattern = None
            
            # 检查各种可能的工具调用格式
            tool_patterns = [
                'tool_use', 'tool_call', 'function_call', 'action',
                'contentBlockStart', 'contentBlockDelta', 'contentBlockStop',
                'message', 'tool_result', 'input', 'output'
            ]
            
            for pattern in tool_patterns:
                if pattern in chunk:
                    logger.info(f"🔍 在chunk #{chunk_count}中发现工具相关字段: {pattern}")
                    found_tool_info = True
                    detected_pattern = pattern
                    break
            
            # 如果发现工具信息，返回要输出到聊天的消息
            if found_tool_info:
                # 更详细地解析工具信息
                tool_details = self._parse_tool_details(chunk, detected_pattern)
                tool_msg = f"\n🔧 **检测到工具活动** (Chunk #{chunk_count})\n   📋 类型: {detected_pattern}\n{tool_details}\n"
                logger.info(f"强制输出工具信息: {tool_msg}")
                return tool_msg
                
            return None
        except Exception as e:
            logger.warning(f"强制检查工具调用时出错: {e}")
            return None

    def _parse_tool_details(self, chunk, pattern):
        """解析工具详情"""
        try:
            if pattern == 'message' and 'message' in chunk:
                message = chunk['message']
                if 'content' in message:
                    content = message['content']
                    for item in content:
                        if isinstance(item, dict):
                            if item.get('type') == 'tool_use':
                                tool_name = item.get('name', '未知工具')
                                tool_input = item.get('input', {})
                                return f"   🔧 工具: {tool_name}\n   📋 输入: {json.dumps(tool_input, ensure_ascii=False)}"
                            elif item.get('type') == 'tool_result':
                                result = item.get('content', [])
                                if result:
                                    result_text = result[0].get('text', '无结果') if isinstance(result, list) else str(result)
                                    return f"   ✅ 工具结果: {result_text[:200]}..."
            elif 'toolUse' in chunk:
                tool_info = chunk['toolUse']
                tool_name = tool_info.get('name', '未知工具')
                tool_input = tool_info.get('input', {})
                return f"   🔧 工具: {tool_name}\n   📋 输入: {json.dumps(tool_input, ensure_ascii=False)}"
            
            return f"   📋 原始数据: {str(chunk)[:200]}..."
        except Exception as e:
            return f"   ❌ 解析错误: {str(e)}"

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
                    
                    # 工具调用信息已由tool_tracker处理，这里不重复处理
                    if 'contentBlockStart' in event:
                        return None
                    
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