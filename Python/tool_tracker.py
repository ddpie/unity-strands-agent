"""
工具调用跟踪器
用于跟踪和格式化AI助手的工具调用过程
"""

import json
import logging
from typing import Dict, Any, Optional

logger = logging.getLogger(__name__)

class ToolTracker:
    """跟踪工具调用并生成用户友好的消息"""
    
    def __init__(self):
        self.current_tool = None
        self.tool_input = None
        self.tool_output = None
        self.tool_count = 0
        self.current_tool_id = None
        
    def process_event(self, event: Dict[str, Any]) -> Optional[str]:
        """处理Strands事件，返回格式化的工具调用信息"""
        
        try:
            # 检测工具调用开始
            if 'contentBlockStart' in event:
                content_block = event['contentBlockStart'].get('contentBlock', {})
                if content_block.get('type') == 'tool_use':
                    self.current_tool = content_block.get('name', '未知工具')
                    self.current_tool_id = content_block.get('id', '')
                    self.tool_count += 1
                    self.tool_input = None
                    self.tool_output = None
                    
                    # 获取工具的中文描述
                    tool_desc = self._get_tool_description(self.current_tool)
                    return f"\n🔧 **工具调用 #{self.tool_count}: {self.current_tool}**\n   {tool_desc}\n   ⏳ 正在准备参数..."
            
            # 检测工具输入
            if 'contentBlockDelta' in event:
                delta = event['contentBlockDelta']
                if delta.get('contentBlockIndex') is not None and self.current_tool:
                    # 这是工具输入的一部分
                    if 'delta' in delta and 'input' in delta['delta']:
                        input_data = delta['delta']['input']
                        # 格式化输入参数以便更好的显示
                        formatted_input = self._format_tool_input(self.current_tool, input_data)
                        return f"   📋 参数: {formatted_input}"
            
            # 检测工具调用完成
            if 'contentBlockStop' in event and self.current_tool:
                # 工具输入收集完成
                return f"   ⏳ 参数准备完成，开始执行工具..."
            
            # 检测消息中的工具结果
            if 'message' in event:
                message = event['message']
                if 'content' in message:
                    for content in message['content']:
                        if content.get('type') == 'tool_result':
                            tool_id = content.get('tool_use_id', '')
                            result = content.get('content', [])
                            if result and isinstance(result, list) and len(result) > 0:
                                result_text = result[0].get('text', '无结果')
                                # 格式化结果显示
                                formatted_result = self._format_tool_result(self.current_tool, result_text)
                                tool_name = self.current_tool
                                self.current_tool = None  # 重置当前工具
                                return f"   ✅ 工具执行完成: {formatted_result}\n   📋 工具 **{tool_name}** 执行结束\n"
            
            return None
            
        except Exception as e:
            logger.warning(f"处理工具事件时出错: {e}")
            return None
    
    def _get_tool_description(self, tool_name: str) -> str:
        """获取工具的中文描述"""
        # 标准化工具名称（移除模块前缀）
        clean_name = tool_name.split('.')[-1] if '.' in tool_name else tool_name
        
        tool_descriptions = {
            'file_read': '📖 读取文件内容',
            'file_write': '📝 写入文件内容',
            'editor': '✏️ 编辑文件',
            'python_repl': '🐍 执行Python代码',
            'calculator': '🔢 数学计算',
            'memory': '🧠 记忆存储',
            'current_time': '⏰ 获取当前时间',
            'shell': '💻 执行Shell命令',
            'unity_shell': '🎮 执行Unity Shell命令',
            'http_request': '🌐 发送HTTP请求'
        }
        return tool_descriptions.get(clean_name, f'🔧 执行工具: {clean_name}')
    
    def _format_tool_input(self, tool_name: str, input_data: dict) -> str:
        """格式化工具输入参数以便用户友好的显示"""
        try:
            # 标准化工具名称
            clean_name = tool_name.split('.')[-1] if '.' in tool_name else tool_name
            
            if clean_name == 'file_read':
                # 增加详细的file_read日志
                logger.info(f"📖 [TOOL_TRACKER] file_read工具输入参数: {input_data}")
                if 'path' in input_data:
                    file_path = input_data['path']
                    logger.info(f"📖 [TOOL_TRACKER] file_read目标文件: {file_path}")
                    return f"读取文件: {file_path}"
                elif 'file_path' in input_data:
                    file_path = input_data['file_path']
                    logger.info(f"📖 [TOOL_TRACKER] file_read目标文件: {file_path}")
                    return f"读取文件: {file_path}"
            elif clean_name == 'file_write':
                if 'path' in input_data:
                    content_preview = input_data.get('content', '')[:50]
                    return f"写入文件: {input_data['path']} (内容: {content_preview}...)"
            elif clean_name == 'editor':
                if 'path' in input_data:
                    return f"编辑文件: {input_data['path']}"
            elif clean_name == 'python_repl':
                if 'code' in input_data:
                    code_preview = input_data['code'][:100].replace('\n', ' ')
                    return f"执行代码: {code_preview}..."
            elif clean_name == 'shell' or clean_name == 'unity_shell':
                if 'command' in input_data:
                    return f"执行命令: {input_data['command']}"
            elif clean_name == 'calculator':
                if 'expression' in input_data:
                    return f"计算: {input_data['expression']}"
            elif clean_name == 'http_request':
                if 'url' in input_data:
                    method = input_data.get('method', 'GET')
                    return f"{method} 请求: {input_data['url']}"
            
            # 默认格式化
            return json.dumps(input_data, ensure_ascii=False, separators=(',', ':'))[:100]
        except Exception as e:
            return f"参数解析错误: {str(e)}"
    
    def _format_tool_result(self, tool_name: str, result_text: str) -> str:
        """格式化工具执行结果以便用户友好的显示"""
        try:
            # 标准化工具名称
            clean_name = tool_name.split('.')[-1] if '.' in tool_name else tool_name
            
            # 截断过长的结果
            if len(result_text) > 300:
                result_text = result_text[:300] + "..."
            
            if clean_name == 'file_read':
                # 增加详细的file_read结果日志
                logger.info(f"📖 [TOOL_TRACKER] file_read工具结果长度: {len(result_text)}字符")
                logger.info(f"📖 [TOOL_TRACKER] file_read结果前100字符: {result_text[:100]}")
                
                if result_text.startswith('Error'):
                    logger.info(f"📖 [TOOL_TRACKER] file_read执行失败: {result_text}")
                    return f"❌ 文件读取失败: {result_text}"
                else:
                    lines = result_text.split('\n')
                    logger.info(f"📖 [TOOL_TRACKER] file_read成功，文件有{len(lines)}行")
                    if len(lines) > 10:
                        return f"📖 文件内容 ({len(lines)}行): {lines[0][:50]}..."
                    else:
                        return f"📖 文件内容: {result_text[:100]}..."
            elif clean_name == 'file_write':
                if 'successfully' in result_text.lower() or 'success' in result_text.lower():
                    return f"✅ 文件写入成功"
                else:
                    return f"❌ 文件写入失败: {result_text}"
            elif clean_name == 'python_repl':
                if result_text.strip():
                    return f"🐍 执行结果: {result_text}"
                else:
                    return f"🐍 代码执行完成"
            elif clean_name == 'shell' or clean_name == 'unity_shell':
                if result_text.strip():
                    return f"💻 命令输出: {result_text}"
                else:
                    return f"💻 命令执行完成"
            elif clean_name == 'calculator':
                return f"🔢 计算结果: {result_text}"
            elif clean_name == 'http_request':
                if result_text.startswith('{') or result_text.startswith('['):
                    return f"🌐 HTTP响应: JSON数据 ({len(result_text)}字符)"
                else:
                    return f"🌐 HTTP响应: {result_text[:100]}..."
            
            # 默认格式化
            return result_text
        except Exception as e:
            return f"结果格式化错误: {str(e)}"
    
    def reset(self):
        """重置跟踪器状态"""
        self.current_tool = None
        self.tool_input = None
        self.tool_output = None

# 全局工具跟踪器实例
_tool_tracker = ToolTracker()

def get_tool_tracker() -> ToolTracker:
    """获取全局工具跟踪器实例"""
    return _tool_tracker