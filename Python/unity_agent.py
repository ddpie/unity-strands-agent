"""
Unity代理核心类
封装Strands Agent SDK，提供Unity开发相关功能
"""

import logging
from typing import Dict, Any
from strands import Agent
from unity_system_prompt import UNITY_SYSTEM_PROMPT
from unity_tools import get_unity_tools

# 配置日志
logger = logging.getLogger(__name__)

class UnityAgent:
    """
    Unity专用的Strands Agent封装类
    配置适合Unity开发的工具集合
    """
    
    def __init__(self):
        """使用Unity开发工具配置初始化代理"""
        try:
            logger.info("========== 初始化Unity Agent ==========")
            
            # 初始化MCP管理器
            from mcp_manager import MCPManager
            self.mcp_manager = MCPManager()
            
            # 配置Unity开发相关的工具集
            logger.info("开始配置Unity工具集...")
            unity_tools = get_unity_tools(include_mcp=True, agent_instance=self)
            logger.info(f"工具集配置完成，数量: {len(unity_tools)}")
            
            # 创建流处理器
            from streaming_processor import StreamingProcessor
            self.streaming_processor = StreamingProcessor(self)
            
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
        """清理所有资源"""
        try:
            # 清理MCP资源
            if hasattr(self, 'mcp_manager'):
                self.mcp_manager.cleanup()
            
            logger.info("所有资源清理完成")
            
        except Exception as e:
            logger.warning(f"清理资源时出错: {e}")
    
    def _load_mcp_tools(self):
        """加载MCP工具（供unity_tools调用）"""
        try:
            if hasattr(self, 'mcp_manager'):
                return self.mcp_manager.load_mcp_tools()
            else:
                logger.warning("MCP管理器未初始化")
                return []
        except Exception as e:
            logger.error(f"加载MCP工具失败: {e}")
            return []
    
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
                try:
                    from unity_tools import get_available_tool_names
                    return get_available_tool_names()
                except ImportError:
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
        async for chunk in self.streaming_processor.process_stream(message):
            yield chunk
    
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