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
        
    def process_event(self, event: Dict[str, Any]) -> Optional[str]:
        """处理Strands事件，返回格式化的工具调用信息"""
        
        try:
            # 检测工具调用开始
            if 'contentBlockStart' in event:
                content_block = event['contentBlockStart'].get('contentBlock', {})
                if content_block.get('type') == 'tool_use':
                    self.current_tool = content_block.get('name', '未知工具')
                    tool_id = content_block.get('id', '')
                    return f"\n📌 **调用工具: {self.current_tool}**"
            
            # 检测工具输入
            if 'contentBlockDelta' in event:
                delta = event['contentBlockDelta']
                if delta.get('contentBlockIndex') is not None and self.current_tool:
                    # 这是工具输入的一部分
                    if 'delta' in delta and 'input' in delta['delta']:
                        input_text = json.dumps(delta['delta']['input'], ensure_ascii=False, indent=2)
                        return f"   输入参数: ```json\n{input_text}\n```"
            
            # 检测工具调用完成
            if 'contentBlockStop' in event and self.current_tool:
                # 工具输入收集完成
                return f"   ⏳ 执行中..."
            
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
                                # 截断过长的结果
                                if len(result_text) > 200:
                                    result_text = result_text[:200] + "..."
                                return f"   ✅ 结果: {result_text}"
            
            return None
            
        except Exception as e:
            logger.warning(f"处理工具事件时出错: {e}")
            return None
    
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