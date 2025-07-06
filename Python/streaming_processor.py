"""
流式处理器
负责处理Agent的流式响应，包括工具调用监控、进度跟踪和异常处理
"""

import json
import logging
import asyncio
from typing import Dict, Any, AsyncGenerator
from tool_tracker import get_tool_tracker

# 配置日志
logger = logging.getLogger(__name__)

class StreamingProcessor:
    """负责处理Agent的流式响应"""
    
    def __init__(self, agent_instance):
        """
        初始化流式处理器
        
        参数:
            agent_instance: Unity Agent实例
        """
        self.agent_instance = agent_instance
    
    async def process_stream(self, message: str) -> AsyncGenerator[str, None]:
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
            logger.info(f"Agent类型: {type(self.agent_instance.agent)}")
            logger.info(f"可用工具数量: {len(self.agent_instance._available_tools) if hasattr(self.agent_instance, '_available_tools') else 0}")
            
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
            logger.info(f"Agent对象: {self.agent_instance.agent}")
            logger.info(f"Agent类型: {type(self.agent_instance.agent)}")
            logger.info(f"Stream_async方法存在: {hasattr(self.agent_instance.agent, 'stream_async')}")
            
            # 先测试agent是否正常工作
            try:
                logger.info("测试agent是否响应...")
                test_response = self.agent_instance.agent("简单回答：你好")
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
                
                async for chunk in self.agent_instance.agent.stream_async(message):
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
                                tool_icon = self._get_tool_icon(tool_name)
                                
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
                if hasattr(self.agent_instance, 'mcp_manager'):
                    self.agent_instance.mcp_manager.cleanup()
                    
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
    
    def _get_tool_icon(self, tool_name):
        """根据工具名称获取对应的图标"""
        tool_name_lower = tool_name.lower()
        
        if 'python' in tool_name_lower:
            return "🐍"
        elif 'calculator' in tool_name_lower:
            return "🧮"
        elif 'memory' in tool_name_lower:
            return "🧠"
        elif 'http' in tool_name_lower:
            return "🌐"
        elif 'time' in tool_name_lower:
            return "⏰"
        elif 'write' in tool_name_lower:
            return "✏️"
        elif 'editor' in tool_name_lower:
            return "📝"
        else:
            return "🔧"