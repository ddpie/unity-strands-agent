"""
Unity AI Agent 核心模块
为Unity集成封装Strands Agent SDK，配置Unity开发相关工具
"""

import sys
import os
import ssl
from unity_system_prompt import UNITY_SYSTEM_PROMPT
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

from strands import Agent
import json
import logging
import asyncio
from typing import Dict, Any, Optional
from tool_tracker import get_tool_tracker

# 导入Strands预定义工具
try:
    # 添加strands tools路径到sys.path
    import sys
    strands_tools_path = "/Users/caobao/projects/strands/tools/src"
    if strands_tools_path not in sys.path:
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
    
    print("[Python] Strands预定义工具导入成功")
    TOOLS_AVAILABLE = True
except ImportError as e:
    print(f"[Python] Strands工具导入失败: {e}")
    print("[Python] 将使用无工具模式")
    TOOLS_AVAILABLE = False

# 尝试导入MCP支持
MCP_AVAILABLE = False
try:
    from mcp import StdioServerParameters, stdio_client
    from strands.tools.mcp import MCPClient as StrandsMCPClient
    import asyncio
    import subprocess
    import threading
    from datetime import timedelta
    from concurrent.futures import Future, ThreadPoolExecutor
    import weakref
    
    class MCPClientInitializationError(Exception):
        """MCP客户端初始化错误"""
        pass
    
    class MCPClient:
        """基于strands实现的MCP客户端，支持stdio、http和sse传输"""
        
        def __init__(self, client_factory, timeout_seconds=30):
            self.client_factory = client_factory
            self.timeout_seconds = timeout_seconds
            self.client = None
            self.background_thread = None
            self.loop = None
            self.executor = ThreadPoolExecutor(max_workers=1)
            self._started = False
            self._subprocess = None  # 存储subprocess引用用于清理
            self._client_context = None  # 存储异步上下文管理器
        
        def __enter__(self):
            self.start()
            return self
        
        def __exit__(self, exc_type, exc_val, exc_tb):
            self.stop()
            return False  # 允许异常传播
        
        def start(self):
            """启动MCP客户端连接"""
            if self._started:
                return
            
            try:
                # 在后台线程中启动异步客户端
                future = Future()
                
                def background_worker():
                    try:
                        self.loop = asyncio.new_event_loop()
                        asyncio.set_event_loop(self.loop)
                        
                        async def init_client():
                            # 对于异步上下文管理器，使用 async with
                            client_context = self.client_factory()
                            self.client = await client_context.__aenter__()
                            # 保存上下文管理器以便后续清理
                            self._client_context = client_context
                            # 如果客户端有subprocess引用，保存它
                            if hasattr(self.client, '_subprocess'):
                                self._subprocess = self.client._subprocess
                            elif hasattr(self.client, 'process'):
                                self._subprocess = self.client.process
                            return self.client
                        
                        client = self.loop.run_until_complete(init_client())
                        future.set_result(client)
                        
                        # 保持事件循环运行
                        self.loop.run_forever()
                    except Exception as e:
                        future.set_exception(e)
                    finally:
                        # 确保事件循环正确关闭
                        try:
                            if self.loop and not self.loop.is_closed():
                                # 取消所有挂起的任务
                                pending = asyncio.all_tasks(self.loop)
                                for task in pending:
                                    task.cancel()
                                if pending:
                                    self.loop.run_until_complete(asyncio.gather(*pending, return_exceptions=True))
                                self.loop.close()
                        except Exception as e:
                            logger.warning(f"关闭事件循环时出错: {e}")
                
                self.background_thread = threading.Thread(target=background_worker, daemon=True)
                self.background_thread.start()
                
                # 等待初始化完成
                self.client = future.result(timeout=self.timeout_seconds)
                self._started = True
                
            except Exception as e:
                raise MCPClientInitializationError(f"MCP客户端初始化失败: {e}")
        
        def stop(self):
            """停止MCP客户端连接"""
            if not self._started:
                return
            
            try:
                # 1. 关闭异步上下文管理器
                if hasattr(self, '_client_context') and self._client_context:
                    try:
                        if self.loop and not self.loop.is_closed():
                            async def cleanup_context():
                                await self._client_context.__aexit__(None, None, None)
                            asyncio.run_coroutine_threadsafe(cleanup_context(), self.loop).result(timeout=3)
                    except Exception as e:
                        logger.warning(f"关闭MCP上下文管理器时出错: {e}")
                
                # 2. 关闭MCP客户端连接
                if self.client:
                    try:
                        if hasattr(self.client, 'close'):
                            if asyncio.iscoroutinefunction(self.client.close):
                                if self.loop and not self.loop.is_closed():
                                    asyncio.run_coroutine_threadsafe(self.client.close(), self.loop)
                            else:
                                self.client.close()
                    except Exception as e:
                        logger.warning(f"关闭MCP客户端时出错: {e}")
                
                # 3. 关闭subprocess（如果存在）
                if self._subprocess:
                    try:
                        if self._subprocess.poll() is None:  # 进程仍在运行
                            self._subprocess.terminate()  # 温和终止
                            try:
                                self._subprocess.wait(timeout=3)  # 等待3秒
                            except subprocess.TimeoutExpired:
                                self._subprocess.kill()  # 强制终止
                                self._subprocess.wait()
                        
                        # 确保所有文件描述符都关闭
                        if hasattr(self._subprocess, 'stdin') and self._subprocess.stdin:
                            self._subprocess.stdin.close()
                        if hasattr(self._subprocess, 'stdout') and self._subprocess.stdout:
                            self._subprocess.stdout.close()
                        if hasattr(self._subprocess, 'stderr') and self._subprocess.stderr:
                            self._subprocess.stderr.close()
                            
                    except Exception as e:
                        logger.warning(f"关闭subprocess时出错: {e}")
                    finally:
                        self._subprocess = None
                
                # 4. 停止事件循环
                if self.loop and not self.loop.is_closed():
                    self.loop.call_soon_threadsafe(self.loop.stop)
                
                # 5. 等待后台线程结束
                if self.background_thread and self.background_thread.is_alive():
                    self.background_thread.join(timeout=5)
                    if self.background_thread.is_alive():
                        logger.warning("后台线程未能在5秒内结束")
                
                # 6. 关闭线程池
                if self.executor:
                    self.executor.shutdown(wait=True, timeout=3)
                    
                self._started = False
                
            except Exception as e:
                logger.warning(f"MCP客户端停止时出错: {e}")
        
        def list_tools_sync(self, timeout_seconds=30):
            """同步获取工具列表"""
            if not self._started or not self.client:
                logger.warning("客户端未启动或不存在")
                return []
            
            try:
                logger.info(f"开始获取MCP工具列表，超时{timeout_seconds}秒")
                future = Future()
                
                def run_async():
                    try:
                        async def get_tools():
                            logger.info("调用client.list_tools()")
                            
                            # 调试：检查客户端对象类型和方法
                            logger.info(f"客户端对象类型: {type(self.client)}")
                            logger.info(f"客户端对象: {self.client}")
                            
                            # 列出所有可用方法
                            methods = [method for method in dir(self.client) if not method.startswith('_')]
                            logger.info(f"客户端可用方法: {methods}")
                            
                            if hasattr(self.client, 'list_tools'):
                                result = await self.client.list_tools()
                                logger.info(f"获取到结果类型: {type(result)}")
                                logger.info(f"结果内容: {result}")
                                
                                if hasattr(result, 'tools'):
                                    tools = result.tools
                                    logger.info(f"找到 {len(tools)} 个工具")
                                    for i, tool in enumerate(tools):
                                        logger.info(f"工具 {i+1}: {tool}")
                                    return tools
                                else:
                                    logger.warning("结果对象没有tools属性")
                                    return []
                            else:
                                logger.warning("客户端没有list_tools方法")
                                # 检查是否有其他可能的方法
                                possible_methods = [m for m in methods if 'tool' in m.lower()]
                                logger.info(f"包含'tool'的方法: {possible_methods}")
                                return []
                        
                        if self.loop and not self.loop.is_closed():
                            tools = asyncio.run_coroutine_threadsafe(get_tools(), self.loop).result(timeout=timeout_seconds)
                            future.set_result(tools)
                        else:
                            logger.warning("事件循环不可用")
                            future.set_result([])
                            
                    except Exception as e:
                        logger.error(f"获取工具异步操作失败: {e}")
                        future.set_exception(e)
                
                self.executor.submit(run_async)
                result = future.result(timeout=timeout_seconds)
                logger.info(f"最终返回 {len(result)} 个工具")
                return result
                
            except Exception as e:
                logger.error(f"获取MCP工具失败: {e}")
                import traceback
                logger.error(f"堆栈跽踪: {traceback.format_exc()}")
                return []
        
        def call_tool_sync(self, tool_use_id, name, arguments, read_timeout_seconds=None):
            """同步调用MCP工具"""
            if not self._started or not self.client:
                return {"status": "error", "error": "MCP客户端未启动"}
            
            timeout = read_timeout_seconds or timedelta(seconds=30)
            if isinstance(timeout, timedelta):
                timeout = timeout.total_seconds()
            
            try:
                future = Future()
                
                def run_async():
                    try:
                        async def call_tool():
                            if hasattr(self.client, 'call_tool'):
                                result = await self.client.call_tool(
                                    name=name,
                                    arguments=arguments
                                )
                                return {
                                    "status": "success",
                                    "result": result.content if hasattr(result, 'content') else result
                                }
                            return {"status": "error", "error": "工具调用方法不可用"}
                        
                        if self.loop and not self.loop.is_closed():
                            result = asyncio.run_coroutine_threadsafe(call_tool(), self.loop).result(timeout=timeout)
                            future.set_result(result)
                        else:
                            future.set_result({"status": "error", "error": "事件循环不可用"})
                            
                    except Exception as e:
                        future.set_exception(e)
                
                self.executor.submit(run_async)
                return future.result(timeout=timeout)
                
            except Exception as e:
                logger.warning(f"调用MCP工具失败: {e}")
                return {"status": "error", "error": str(e)}
    
    print("[Python] MCP支持模块导入成功")
    MCP_AVAILABLE = True
except ImportError as e:
    print(f"[Python] MCP模块导入失败: {e}")
    print("[Python] 将使用无MCP模式")
    MCP_AVAILABLE = False

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
            
            # 创建优化的Unity专用系统提示词，基于Strands最佳实践
            # 尝试启用工具
            try:
                logger.info("开始创建Strands Agent...")
                logger.info(f"System prompt长度: {len(UNITY_SYSTEM_PROMPT)}")
                logger.info(f"工具列表: {[str(tool) for tool in unity_tools]}")
                
                # 确保所有工具都设置为非交互模式
                from unity_non_interactive_tools import unity_tool_manager
                unity_tool_manager.setup_non_interactive_mode()
                
                self.agent = Agent(system_prompt=UNITY_SYSTEM_PROMPT, tools=unity_tools)
                
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
                    self.agent = Agent(system_prompt=UNITY_SYSTEM_PROMPT)
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
    
    def __del__(self):
        """析构函数，确保资源清理"""
        try:
            self._cleanup_resources()
        except Exception as e:
            logger.warning(f"析构函数中清理资源时出错: {e}")
    
    def _cleanup_resources(self):
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

        # shell工具
        try:
            unity_tools.append(shell_module)
            logger.info("✓ 添加shell工具: shell")
        except (NameError, ImportError) as e:
            logger.warning(f"shell工具不可用: {e}")
        
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
        
        # HTTP工具 - 访问Unity文档、API等
        try:
            unity_tools.append(http_request_module)
            logger.info("✓ 添加HTTP工具: http_request")
        except (NameError, ImportError) as e:
            logger.warning(f"HTTP工具不可用: {e}")
        
        # MCP工具 - 外部工具和服务集成
        if MCP_AVAILABLE:
            try:
                mcp_tools = self._load_mcp_tools()
                if mcp_tools:
                    unity_tools.extend(mcp_tools)
                    logger.info(f"✓ 添加MCP工具: {len(mcp_tools)} 个工具")
                    # 存储MCP工具引用
                    self._mcp_tools = mcp_tools
            except Exception as e:
                logger.warning(f"MCP工具加载失败: {e}")
        else:
            logger.info("ℹ️ MCP支持不可用，跳过MCP工具加载")
        
        if unity_tools:
            logger.info(f"🎉 成功配置 {len(unity_tools)} 个Unity开发工具")
            logger.info(f"可用工具列表: {[tool.__name__ if hasattr(tool, '__name__') else str(tool) for tool in unity_tools]}")
        else:
            logger.warning("⚠️ 没有可用的Unity开发工具")
        
        return unity_tools
    
    def get_available_tools(self):
        """获取当前可用的工具列表"""
        try:
            # 返回存储的工具列表（即使当前未启用）
            if hasattr(self, '_available_tools') and self._available_tools:
                # 如果是字符串列表，直接返回
                if isinstance(self._available_tools[0], str):
                    return self._available_tools
                # 如果是模块对象，提取名称
                return [tool.__name__ if hasattr(tool, '__name__') else str(tool) for tool in self._available_tools]
            
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
                return ["file_read", "file_write", "editor", "shell", "python_repl", "calculator", "memory", "current_time", "http_request"] if TOOLS_AVAILABLE else []
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
            import traceback
            full_traceback = traceback.format_exc()
            logger.error(f"完整错误堆栈:\n{full_traceback}")
            
            # 格式化错误信息，包含完整堆栈
            error_message = f"\n❌ **Python执行错误**\n\n"
            error_message += f"**错误类型**: {type(e).__name__}\n"
            error_message += f"**错误信息**: {str(e)}\n\n"
            error_message += "**错误堆栈**:\n```python\n"
            error_message += full_traceback
            error_message += "```\n"
            
            return {
                "success": False,
                "error": str(e),
                "error_detail": error_message,
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
            logger.info(f"Agent类型: {type(self.agent)}")
            logger.info(f"Stream_async方法存在: {hasattr(self.agent, 'stream_async')}")
            
            # 先测试agent是否正常工作
            try:
                logger.info("测试agent是否响应...")
                test_response = self.agent("简单回答：你好")
                logger.info(f"Agent测试响应: {test_response[:100]}...")
            except Exception as test_error:
                logger.error(f"Agent测试失败: {test_error}")
                logger.error("这可能是导致流式处理异常的原因")
            
            chunk_count = 0
            
            logger.info("开始遍历流式响应...")
            
            # 静默启动，不显示工具系统提示
            pass
            
            logger.info("=== 开始进入流式处理循环 ===")
            
            try:
                # 添加强制完成信号检测
                chunk_count = 0
                completed_normally = False
                last_tool_time = asyncio.get_event_loop().time()
                
                async for chunk in self.agent.stream_async(message):
                    chunk_count += 1
                    current_time = asyncio.get_event_loop().time()
                    
                    logger.info(f"========== Chunk #{chunk_count} ==========")
                    logger.info(f"耗时: {current_time - start_time:.1f}s")
                    logger.info(f"Chunk类型: {type(chunk)}")
                    logger.info(f"Chunk内容: {str(chunk)[:500]}...")
                    
                    # 立即检查是否是空的或无效的chunk
                    if chunk is None:
                        logger.warning(f"收到None chunk #{chunk_count}")
                        continue
                    
                    if not chunk:
                        logger.warning(f"收到空chunk #{chunk_count}")
                        continue
                    
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
                            
                            # 更新工具执行时间
                            last_tool_time = current_time
                            
                            # 特别监控shell工具
                            if 'shell' in tool_name.lower():
                                command = tool_input.get('command', '')
                                logger.info(f"💻 [SHELL_MONITOR] 检测到shell工具调用: {command}")
                                yield json.dumps({
                                    "type": "chunk", 
                                    "content": f"\n<details>\n<summary>Shell工具执行 - {tool_name}</summary>\n\n**命令**: `{command}`\n\n⏳ 正在执行shell命令...\n</details>\n",
                                    "done": False
                                }, ensure_ascii=False)
                            elif 'file_read' in tool_name.lower():
                                file_path = tool_input.get('path', tool_input.get('file_path', ''))
                                logger.info(f"📖 [FILE_READ_MONITOR] 检测到file_read工具调用: {file_path}")
                                if file_path == '.':
                                    logger.warning(f"⚠️ [FILE_READ_MONITOR] 警告：尝试读取当前目录，这可能导致卡死！")
                                    yield json.dumps({
                                        "type": "chunk", 
                                        "content": f"\n<details>\n<summary>安全提示 - 文件读取操作</summary>\n\n**工具**: {tool_name}  \n**路径**: `{file_path}`  \n\n⚠️ **注意**: 检测到尝试读取目录，建议使用shell工具进行目录浏览\n</details>\n",
                                        "done": False
                                    }, ensure_ascii=False)
                                else:
                                    yield json.dumps({
                                        "type": "chunk", 
                                        "content": f"\n<details>\n<summary>文件读取 - {tool_name}</summary>\n\n**文件路径**: `{file_path}`\n\n⏳ 正在读取文件...\n</details>\n",
                                        "done": False
                                    }, ensure_ascii=False)
                            else:
                                # 生成工具图标
                                tool_icon = "🔧"
                                if 'python' in tool_name.lower():
                                    tool_icon = "🐍"
                                elif 'calculator' in tool_name.lower():
                                    tool_icon = "🧮"
                                elif 'memory' in tool_name.lower():
                                    tool_icon = "🧠"
                                elif 'http' in tool_name.lower():
                                    tool_icon = "🌐"
                                elif 'time' in tool_name.lower():
                                    tool_icon = "⏰"
                                elif 'write' in tool_name.lower():
                                    tool_icon = "✏️"
                                elif 'editor' in tool_name.lower():
                                    tool_icon = "📝"
                                
                                # 格式化输入参数
                                formatted_input = json.dumps(tool_input, ensure_ascii=False, indent=2)
                                # 增加截断长度限制，避免过度截断
                                if len(formatted_input) > 1000:
                                    formatted_input = formatted_input[:1000] + "...\n}"
                                
                                yield json.dumps({
                                    "type": "chunk", 
                                    "content": f"\n<details>\n<summary>工具执行 - {tool_name}</summary>\n\n**输入参数**:\n```json\n{formatted_input}\n```\n\n⏳ 正在执行...\n</details>\n",
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
                            # 检查工具是否执行过长时间
                            time_since_last_tool = current_time - last_tool_time
                            if time_since_last_tool > 30:  # 30秒无工具活动
                                logger.warning(f"⚠️ [TOOL_TIMEOUT] 工具执行超过30秒无响应，可能卡死")
                                yield json.dumps({
                                    "type": "chunk",
                                    "content": f"\n<details>\n<summary>执行状态 - 工具超时提醒</summary>\n\n**状态**: 已超过30秒无响应  \n**可能原因**: 工具处理大文件或遇到问题  \n**建议**: 如持续无响应可停止执行\n</details>\n",
                                    "done": False
                                }, ensure_ascii=False)
                                last_tool_time = current_time  # 重置以避免重复警告
                            
                            # 工具执行完成，重置时间
                            tool_start_time = None
                            last_tool_progress_time = None
                            # 静默跳过
                            logger.debug(f"跳过无内容chunk: {str(chunk)[:100]}")
                            pass
                
                # 检查是否真的有内容输出
                if chunk_count <= 0:
                    logger.warning("=== 警告：没有收到任何有效chunk！ ===")
                    yield json.dumps({
                        "type": "chunk",
                        "content": "\n⚠️ **警告**：没有收到Agent的响应内容，可能存在问题\n",
                        "done": False
                    }, ensure_ascii=False)
                
                # 标记正常完成
                completed_normally = True
                
                # 信号完成
                total_time = asyncio.get_event_loop().time() - start_time
                logger.info(f"=== 流式处理循环结束 ===")
                logger.info(f"总共处理了 {chunk_count} 个chunk，耗时 {total_time:.1f}秒")
                
                # 检查是否有工具还在执行中
                if tool_tracker.current_tool:
                    logger.warning(f"工具 {tool_tracker.current_tool} 可能仍在执行中")
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
                full_traceback = traceback.format_exc()
                logger.error(f"流式异常堆栈: {full_traceback}")
                
                # 将错误信息发送到聊天界面
                error_message = f"\n❌ **流式处理错误**\n\n"
                error_message += f"**错误类型**: {type(stream_error).__name__}\n"
                error_message += f"**错误信息**: {str(stream_error)}\n\n"
                error_message += "**错误堆栈**:\n```python\n"
                error_message += full_traceback
                error_message += "```\n"
                
                yield json.dumps({
                    "type": "chunk",
                    "content": error_message,
                    "done": False
                }, ensure_ascii=False)
                
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
            full_traceback = traceback.format_exc()
            logger.error(f"完整堆栈:")
            logger.error(full_traceback)
            
            # 将完整的错误信息发送到聊天界面
            error_message = f"\n❌ **Python执行错误**\n\n"
            error_message += f"**错误类型**: {type(e).__name__}\n"
            error_message += f"**错误信息**: {str(e)}\n"
            error_message += f"**已处理Chunk数**: {chunk_count if 'chunk_count' in locals() else 0}\n\n"
            error_message += "**错误堆栈**:\n```python\n"
            error_message += full_traceback
            error_message += "```\n"
            
            # 先发送错误信息作为聊天内容
            yield json.dumps({
                "type": "chunk",
                "content": error_message,
                "done": False
            }, ensure_ascii=False)
            
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
            
            # 清理MCP客户端连接和文件描述符
            try:
                if hasattr(self, '_mcp_clients'):
                    for client in self._mcp_clients:
                        try:
                            # 正确退出上下文管理器
                            client.__exit__(None, None, None)
                        except Exception as e:
                            logger.warning(f"清理MCP客户端时出错: {e}")
                    self._mcp_clients.clear()
                    
                # 强制垃圾回收以清理未关闭的资源
                import gc
                gc.collect()
                
            except Exception as cleanup_error:
                logger.warning(f"清理MCP资源时出错: {cleanup_error}")
    
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
                tool_msg = f"\n<details>\n<summary>🔧 工具调用</summary>\n\n{tool_details}\n</details>\n"
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
                                # 格式化工具输入，支持更长的内容显示
                                formatted_input = json.dumps(tool_input, ensure_ascii=False, indent=2)
                                if len(formatted_input) > 800:
                                    formatted_input = formatted_input[:800] + "..."
                                return f"   🔧 工具: {tool_name}\n   📋 输入:\n```json\n{formatted_input}\n```"
                            elif item.get('type') == 'tool_result':
                                result = item.get('content', [])
                                if result:
                                    result_text = result[0].get('text', '无结果') if isinstance(result, list) else str(result)
                                    # 显示更多工具结果内容
                                    if len(result_text) > 500:
                                        result_text = result_text[:500] + "..."
                                    return f"   ✅ 工具结果: {result_text}"
            elif 'toolUse' in chunk:
                tool_info = chunk['toolUse']
                tool_name = tool_info.get('name', '未知工具')
                tool_input = tool_info.get('input', {})
                # 格式化工具输入，支持更长的内容显示
                formatted_input = json.dumps(tool_input, ensure_ascii=False, indent=2)
                if len(formatted_input) > 800:
                    formatted_input = formatted_input[:800] + "..."
                return f"   🔧 工具: {tool_name}\n   📋 输入:\n```json\n{formatted_input}\n```"
            
            # 显示更多原始数据内容
            chunk_str = str(chunk)
            if len(chunk_str) > 800:
                chunk_str = chunk_str[:800] + "..."
            return f"   📋 原始数据: {chunk_str}"
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
    
    def _load_mcp_tools(self):
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
                        if not hasattr(self, '_mcp_clients'):
                            self._mcp_clients = []
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
                    logger.error(f"堆栈跽踪:\n{traceback.format_exc()}")
                    continue
            
            logger.info(f"总共加载了 {len(mcp_tools)} 个MCP工具")
            
            # 存储MCP客户端引用以便后续清理
            if not hasattr(self, '_mcp_clients'):
                self._mcp_clients = []
            
            # 注意：这里不能直接存储客户端，因为with语句已经关闭了它们
            # 但我们可以在工具包装器中添加清理逻辑
            
        except Exception as e:
            logger.error(f"MCP工具加载过程中出现错误: {e}")
        
        return mcp_tools
    
    def _convert_mcp_tools_to_unity_tools(self, mcp_tools, mcp_client, server_name):
        """将MCP工具转换为Unity可用的工具"""
        converted_tools = []
        
        try:
            for mcp_tool in mcp_tools:
                # 提取工具信息
                tool_name = getattr(mcp_tool, 'name', str(mcp_tool))
                tool_description = getattr(mcp_tool, 'description', f"MCP工具来自 {server_name}")
                tool_schema = getattr(mcp_tool, 'inputSchema', {})
                
                # 创建Unity工具包装器
                def create_unity_tool_wrapper(name, description, schema, client):
                    def unity_tool_function(**kwargs):
                        """Unity工具包装器，调用MCP工具"""
                        try:
                            # 生成唯一的工具使用ID
                            import uuid
                            tool_use_id = str(uuid.uuid4())
                            
                            # 调用MCP工具
                            result = client.call_tool_sync(
                                tool_use_id=tool_use_id,
                                name=name,
                                arguments=kwargs,
                                read_timeout_seconds=30
                            )
                            
                            if result.get("status") == "success":
                                return result.get("result", "工具执行成功，但无返回结果")
                            else:
                                error_msg = result.get("error", "未知错误")
                                return f"MCP工具执行失败: {error_msg}"
                                
                        except Exception as e:
                            logger.error(f"调用MCP工具 '{name}' 失败: {e}")
                            return f"工具调用异常: {e}"
                    
                    # 设置函数属性
                    unity_tool_function.__name__ = f"mcp_{server_name}_{name}".replace("-", "_")
                    unity_tool_function.__doc__ = description
                    
                    # 添加工具元数据
                    unity_tool_function._tool_info = {
                        "name": name,
                        "description": description,
                        "schema": schema,
                        "server": server_name,
                        "type": "mcp_tool"
                    }
                    
                    return unity_tool_function
                
                # 创建包装器并添加到列表
                unity_tool = create_unity_tool_wrapper(tool_name, tool_description, tool_schema, mcp_client)
                converted_tools.append(unity_tool)
                logger.debug(f"转换MCP工具: {tool_name} -> {unity_tool.__name__}")
                
                # 为工具添加清理方法
                def cleanup_tool():
                    try:
                        if hasattr(mcp_client, 'stop'):
                            mcp_client.stop()
                    except Exception as e:
                        logger.warning(f"清理工具 {tool_name} 的MCP客户端时出错: {e}")
                
                unity_tool._cleanup = cleanup_tool
                
        except Exception as e:
            logger.error(f"转换MCP工具时出错: {e}")
        
        return converted_tools
    
    def _load_unity_mcp_config(self):
        """从Unity加载MCP配置"""
        try:
            # 尝试从Unity Assets目录加载配置
            import json
            import os
            
            # 调试：打印当前工作目录
            current_dir = os.getcwd()
            logger.info(f"当前Python工作目录: {current_dir}")
            logger.info(f"Python脚本位置: {__file__}")
            
            # Unity项目的MCP配置路径
            config_paths = [
                "Assets/UnityAIAgent/mcp_config.json",
                "../Assets/UnityAIAgent/mcp_config.json",
                "../../Assets/UnityAIAgent/mcp_config.json",
                "../../../CubeVerse/Assets/UnityAIAgent/mcp_config.json",  # CubeVerse项目
                "/Users/caobao/projects/unity/CubeVerse/Assets/UnityAIAgent/mcp_config.json",  # 绝对路径
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

    def _create_mcp_client(self, server_config):
        """根据配置创建MCP客户端"""
        try:
            transport_type = server_config.get('transport_type', 'stdio')
            server_name = server_config.get('name', 'unknown')
            
            if transport_type == 'stdio':
                # 创建stdio MCP客户端
                command = server_config.get('command', '')
                args = server_config.get('args', [])
                working_dir = server_config.get('working_directory', '')
                env_vars = server_config.get('env_vars', {})
                
                if not command:
                    logger.warning(f"MCP服务器 '{server_name}' 缺少命令配置")
                    return None
                
                # 设置环境变量
                import os
                env = os.environ.copy()
                env.update(env_vars)
                
                # 创建stdio客户端工厂
                def stdio_factory():
                    logger.info(f"=== 启动MCP服务器: {server_name} ===")
                    logger.info(f"命令: {command}")
                    logger.info(f"参数: {args}")
                    logger.info(f"工作目录: {working_dir if working_dir else '当前目录'}")
                    logger.info(f"环境变量: {env_vars}")
                    
                    # stdio_client返回的是一个异步上下文管理器
                    return stdio_client(
                        StdioServerParameters(
                            command=command,
                            args=args,
                            env=env,
                            cwd=working_dir if working_dir else None
                        )
                    )
                
                client = MCPClient(stdio_factory, timeout_seconds=30)
                logger.info(f"创建stdio MCP客户端: {command} {' '.join(args)}")
                return client
                
            elif transport_type == 'streamable_http':
                # 创建streamable HTTP MCP客户端
                url = server_config.get('url', '')
                if not url:
                    logger.warning(f"MCP服务器 '{server_name}' 缺少URL配置")
                    return None
                
                async def http_factory():
                    return await streamablehttp_client(url)
                
                client = MCPClient(http_factory, timeout_seconds=30)
                logger.info(f"创建streamable HTTP MCP客户端: {url}")
                return client
                
            elif transport_type == 'sse':
                # 创建SSE MCP客户端（Legacy支持）
                url = server_config.get('url', '')
                if not url:
                    logger.warning(f"MCP服务器 '{server_name}' 缺少URL配置")
                    return None
                
                async def sse_factory():
                    return await sse_client(url)
                
                client = MCPClient(sse_factory, timeout_seconds=30)
                logger.info(f"创建SSE MCP客户端: {url}")
                return client
                
            elif transport_type in ['http', 'https']:
                # 向后兼容：http类型默认使用streamable_http
                url = server_config.get('url', '')
                if not url:
                    logger.warning(f"MCP服务器 '{server_name}' 缺少URL配置")
                    return None
                
                async def http_factory():
                    return await streamablehttp_client(url)
                
                client = MCPClient(http_factory, timeout_seconds=30)
                logger.info(f"创建HTTP MCP客户端 (streamable): {url}")
                return client
            
            else:
                logger.warning(f"不支持的MCP传输类型: {transport_type}")
                logger.info(f"支持的传输类型: stdio, streamable_http, sse, http")
                return None
                
        except Exception as e:
            logger.error(f"创建MCP客户端失败: {e}")
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

def test_unity_directory() -> str:
    """测试Unity调用时的工作目录"""
    import os
    import json
    try:
        current_dir = os.getcwd()
        script_dir = os.path.dirname(__file__)
        
        result = {
            "current_dir": current_dir,
            "script_dir": script_dir,
            "script_file": __file__,
            "files_in_current": os.listdir(current_dir)[:10],  # 只显示前10个文件避免太长
            "config_paths_exist": {}
        }
        
        # 检查所有配置路径
        config_paths = [
            "Assets/UnityAIAgent/mcp_config.json",
            "../Assets/UnityAIAgent/mcp_config.json",
            "../../Assets/UnityAIAgent/mcp_config.json",
            "../../../CubeVerse/Assets/UnityAIAgent/mcp_config.json",
            "/Users/caobao/projects/unity/CubeVerse/Assets/UnityAIAgent/mcp_config.json",
            "mcp_config.json"
        ]
        
        for path in config_paths:
            result["config_paths_exist"][path] = {
                "exists": os.path.exists(path),
                "absolute_path": os.path.abspath(path)
            }
        
        return json.dumps(result, indent=2, ensure_ascii=False)
    except Exception as e:
        return json.dumps({"error": str(e)}, ensure_ascii=False)

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
        mcp_config = _agent_instance._load_unity_mcp_config()
        
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

def diagnose_unity_mcp_issue() -> str:
    """诊断Unity环境下MCP连接问题"""
    try:
        import subprocess
        import sys
        import threading
        
        logger.info("=== Unity环境MCP连接诊断 ===")
        
        result = {
            "success": True,
            "environment": {
                "python_version": sys.version,
                "current_thread": threading.current_thread().name,
                "is_main_thread": threading.current_thread() == threading.main_thread(),
                "working_directory": os.getcwd()
            },
            "subprocess_tests": [],
            "mcp_tests": [],
            "asyncio_tests": [],
            "diagnosis": []
        }
        
        # 测试1: 基本子进程功能
        try:
            proc_result = subprocess.run(['echo', 'test'], capture_output=True, text=True, timeout=5)
            result["subprocess_tests"].append({
                "name": "基本echo测试",
                "success": True,
                "output": proc_result.stdout.strip(),
                "returncode": proc_result.returncode
            })
            logger.info("✅ 基本子进程功能正常")
        except Exception as e:
            result["subprocess_tests"].append({
                "name": "基本echo测试", 
                "success": False,
                "error": str(e)
            })
            result["diagnosis"].append("❌ Unity环境无法创建基本子进程")
            logger.error(f"❌ 基本子进程测试失败: {e}")
        
        # 测试1.5: 测试PATH环境变量
        try:
            path_env = os.environ.get('PATH', '')
            result["environment"]["path_env"] = path_env[:200] + "..." if len(path_env) > 200 else path_env
            logger.info(f"PATH环境变量: {path_env[:100]}...")
            
            # 测试which node
            proc_result = subprocess.run(['which', 'node'], capture_output=True, text=True, timeout=5)
            result["subprocess_tests"].append({
                "name": "which node测试",
                "success": proc_result.returncode == 0,
                "output": proc_result.stdout.strip() if proc_result.returncode == 0 else proc_result.stderr.strip(),
                "returncode": proc_result.returncode
            })
            if proc_result.returncode == 0:
                logger.info(f"✅ 找到node路径: {proc_result.stdout.strip()}")
            else:
                logger.warning(f"⚠️ 找不到node命令: {proc_result.stderr}")
        except Exception as e:
            result["subprocess_tests"].append({
                "name": "which node测试",
                "success": False,
                "error": str(e)
            })
            logger.error(f"❌ which node测试失败: {e}")
        
        # 测试2: Node.js可用性
        try:
            proc_result = subprocess.run(['node', '--version'], capture_output=True, text=True, timeout=5)
            node_success = proc_result.returncode == 0
            result["subprocess_tests"].append({
                "name": "Node.js版本检测",
                "success": node_success,
                "output": proc_result.stdout.strip() if node_success else proc_result.stderr.strip(),
                "returncode": proc_result.returncode
            })
            if node_success:
                logger.info(f"✅ Node.js可用: {proc_result.stdout.strip()}")
            else:
                logger.error(f"❌ Node.js不可用: {proc_result.stderr}")
                result["diagnosis"].append("❌ Node.js在Unity环境下不可用")
        except Exception as e:
            result["subprocess_tests"].append({
                "name": "Node.js版本检测",
                "success": False,
                "error": str(e)
            })
            result["diagnosis"].append("❌ 无法在Unity环境下执行Node.js")
            logger.error(f"❌ Node.js测试失败: {e}")
        
        # 测试2.5: 使用绝对路径的Node.js测试
        node_paths = [
            '/usr/local/bin/node',
            '/opt/homebrew/bin/node',
            '/usr/bin/node',
            '/Users/caobao/.nvm/current/bin/node'
        ]
        
        for node_path in node_paths:
            if os.path.exists(node_path):
                try:
                    proc_result = subprocess.run([node_path, '--version'], capture_output=True, text=True, timeout=5)
                    node_abs_success = proc_result.returncode == 0
                    result["subprocess_tests"].append({
                        "name": f"Node.js绝对路径测试 ({node_path})",
                        "success": node_abs_success,
                        "output": proc_result.stdout.strip() if node_abs_success else proc_result.stderr.strip(),
                        "returncode": proc_result.returncode
                    })
                    if node_abs_success:
                        logger.info(f"✅ Node.js绝对路径可用: {node_path} -> {proc_result.stdout.strip()}")
                        break  # 找到一个可用的就停止
                    else:
                        logger.warning(f"⚠️ Node.js绝对路径失败: {node_path}")
                except Exception as e:
                    result["subprocess_tests"].append({
                        "name": f"Node.js绝对路径测试 ({node_path})",
                        "success": False,
                        "error": str(e)
                    })
                    logger.error(f"❌ Node.js绝对路径测试失败: {node_path} -> {e}")
                break  # 只测试第一个存在的路径
        
        # 测试3: MCP服务器文件存在性
        mcp_server_path = "/Users/caobao/projects/unity/CubeVerse/Library/PackageCache/com.gamelovers.mcp-unity@fe27f2b491/Server/build/index.js"
        mcp_server_exists = os.path.exists(mcp_server_path)
        result["mcp_tests"].append({
            "name": "MCP服务器文件检查",
            "success": mcp_server_exists,
            "path": mcp_server_path,
            "exists": mcp_server_exists
        })
        
        if not mcp_server_exists:
            result["diagnosis"].append("❌ MCP服务器文件不存在")
            logger.error("❌ MCP服务器文件不存在")
        else:
            logger.info("✅ MCP服务器文件存在")
        
        # 测试4: MCP服务器启动测试（只有在前面测试通过时才执行）
        if len([t for t in result["subprocess_tests"] if t["success"]]) > 0 and mcp_server_exists:
            try:
                env = os.environ.copy()
                env['UNITY_PORT'] = '8090'
                
                # 使用Popen来测试stdio通信
                proc = subprocess.Popen(
                    ['node', mcp_server_path],
                    stdin=subprocess.PIPE,
                    stdout=subprocess.PIPE,
                    stderr=subprocess.PIPE,
                    env=env,
                    text=True
                )
                
                # 等待短时间
                import time
                time.sleep(1)
                
                if proc.poll() is None:
                    # 进程仍在运行，这是好兆头
                    result["mcp_tests"].append({
                        "name": "MCP服务器启动测试",
                        "success": True,
                        "message": "MCP服务器成功启动并保持运行"
                    })
                    logger.info("✅ MCP服务器可以在Unity环境下启动")
                    
                    # 尝试简单的stdio通信
                    try:
                        init_msg = '{"jsonrpc": "2.0", "method": "initialize", "id": 1, "params": {"protocolVersion": "2024-11-05", "capabilities": {}, "clientInfo": {"name": "unity-test", "version": "1.0"}}}\n'
                        proc.stdin.write(init_msg)
                        proc.stdin.flush()
                        time.sleep(0.5)
                        
                        result["mcp_tests"].append({
                            "name": "MCP stdio通信测试",
                            "success": True,
                            "message": "成功发送初始化消息"
                        })
                        logger.info("✅ MCP stdio通信正常")
                    except Exception as stdio_e:
                        result["mcp_tests"].append({
                            "name": "MCP stdio通信测试",
                            "success": False,
                            "error": str(stdio_e)
                        })
                        result["diagnosis"].append(f"❌ MCP stdio通信失败: {str(stdio_e)}")
                        logger.error(f"❌ MCP stdio通信失败: {stdio_e}")
                else:
                    # 进程已经退出
                    stdout, stderr = proc.communicate()
                    result["mcp_tests"].append({
                        "name": "MCP服务器启动测试",
                        "success": False,
                        "returncode": proc.returncode,
                        "stdout": stdout[:200] if stdout else "",
                        "stderr": stderr[:200] if stderr else ""
                    })
                    result["diagnosis"].append(f"❌ MCP服务器启动后立即退出，返回码: {proc.returncode}")
                    logger.error(f"❌ MCP服务器启动失败，返回码: {proc.returncode}")
                
                # 清理进程
                try:
                    if proc.poll() is None:
                        proc.terminate()
                        proc.wait(timeout=2)
                except:
                    try:
                        proc.kill()
                    except:
                        pass
                        
            except Exception as e:
                result["mcp_tests"].append({
                    "name": "MCP服务器启动测试",
                    "success": False,
                    "error": str(e)
                })
                result["diagnosis"].append(f"❌ MCP服务器启动异常: {str(e)}")
                logger.error(f"❌ MCP服务器启动异常: {e}")
        
        # 测试5: 异步环境检查
        try:
            import asyncio
            
            # 检查当前事件循环
            try:
                loop = asyncio.get_event_loop()
                result["asyncio_tests"].append({
                    "name": "当前事件循环检查",
                    "success": True,
                    "running": loop.is_running(),
                    "closed": loop.is_closed()
                })
                logger.info(f"✅ 当前事件循环状态: 运行={loop.is_running()}, 关闭={loop.is_closed()}")
            except RuntimeError as e:
                result["asyncio_tests"].append({
                    "name": "当前事件循环检查",
                    "success": False,
                    "error": str(e)
                })
                logger.info(f"ℹ️ 无当前事件循环: {e}")
            
            # 测试创建新事件循环
            try:
                new_loop = asyncio.new_event_loop()
                result["asyncio_tests"].append({
                    "name": "新事件循环创建",
                    "success": True,
                    "message": "可以创建新的事件循环"
                })
                new_loop.close()
                logger.info("✅ 可以创建新的事件循环")
            except Exception as e:
                result["asyncio_tests"].append({
                    "name": "新事件循环创建",
                    "success": False,
                    "error": str(e)
                })
                result["diagnosis"].append(f"❌ 无法创建异步事件循环: {str(e)}")
                logger.error(f"❌ 无法创建异步事件循环: {e}")
                
        except Exception as e:
            result["asyncio_tests"].append({
                "name": "asyncio模块检查",
                "success": False,
                "error": str(e)
            })
            result["diagnosis"].append(f"❌ asyncio模块检查失败: {str(e)}")
            logger.error(f"❌ asyncio模块检查失败: {e}")
        
        # 生成最终诊断
        if not result["diagnosis"]:
            result["diagnosis"].append("✅ Unity环境支持MCP所需的所有功能")
            logger.info("✅ Unity环境MCP支持正常")
        else:
            logger.warning(f"⚠️ 发现 {len(result['diagnosis'])} 个问题")
        
        logger.info(f"Unity MCP诊断完成: {len(result['diagnosis'])} 个问题")
        return json.dumps(result, ensure_ascii=False, indent=2)
        
    except Exception as e:
        logger.error(f"诊断过程失败: {e}")
        import traceback
        logger.error(f"诊断异常堆栈: {traceback.format_exc()}")
        return json.dumps({
            "success": False, 
            "error": str(e),
            "traceback": traceback.format_exc()
        }, ensure_ascii=False)