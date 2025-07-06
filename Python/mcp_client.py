"""
MCP 客户端模块
基于 strands 实现的 MCP 客户端，支持 stdio、http 和 sse 传输
"""

import asyncio
import subprocess
import threading
import logging
from datetime import timedelta
from concurrent.futures import Future, ThreadPoolExecutor
import weakref

# 获取日志记录器
logger = logging.getLogger(__name__)

try:
    from mcp import StdioServerParameters, stdio_client
    from strands.tools.mcp import MCPClient as StrandsMCPClient
    MCP_AVAILABLE = True
except ImportError as e:
    logger.warning(f"MCP模块导入失败: {e}")
    MCP_AVAILABLE = False


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