"""
Unity调试工具 - 简化版本，用于快速诊断问题
"""

import os
import logging
from typing import Any, Dict, List
from strands.types.tools import ToolResult, ToolUse

logger = logging.getLogger(__name__)

def debug_directory(tool: ToolUse, **kwargs: Any) -> ToolResult:
    """调试目录内容 - 简化版本"""
    tool_use_id = tool.get("toolUseId", "default-id")
    tool_input = tool.get("input", {})
    
    path = tool_input.get("path", ".")
    max_files = tool_input.get("max_files", 20)
    
    try:
        # 展开路径
        path = os.path.expanduser(path)
        
        # 获取绝对路径
        abs_path = os.path.abspath(path)
        
        content_lines = [f"调试目录: {abs_path}"]
        
        if not os.path.exists(path):
            content_lines.append("❌ 路径不存在")
            return {
                "toolUseId": tool_use_id,
                "status": "error",
                "content": [{"text": "\n".join(content_lines)}]
            }
        
        if not os.path.isdir(path):
            content_lines.append("❌ 路径不是目录")
            return {
                "toolUseId": tool_use_id,
                "status": "error", 
                "content": [{"text": "\n".join(content_lines)}]
            }
        
        # 列出目录内容
        try:
            items = os.listdir(path)
            content_lines.append(f"✅ 找到 {len(items)} 个项目")
            content_lines.append("")
            
            # 限制显示数量
            shown_items = items[:max_files]
            
            for item in shown_items:
                item_path = os.path.join(path, item)
                if os.path.isdir(item_path):
                    content_lines.append(f"📁 {item}/")
                else:
                    content_lines.append(f"📄 {item}")
            
            if len(items) > max_files:
                content_lines.append(f"... 还有 {len(items) - max_files} 个项目未显示")
                
        except PermissionError:
            content_lines.append("❌ 权限不足，无法读取目录")
        except Exception as e:
            content_lines.append(f"❌ 读取目录时出错: {str(e)}")
        
        return {
            "toolUseId": tool_use_id,
            "status": "success",
            "content": [{"text": "\n".join(content_lines)}]
        }
        
    except Exception as e:
        error_msg = f"调试工具出错: {str(e)}"
        logger.error(error_msg)
        return {
            "toolUseId": tool_use_id,
            "status": "error",
            "content": [{"text": error_msg}]
        }

# 工具规范
TOOL_SPEC = {
    "name": "debug_directory",
    "description": "调试目录内容 - 快速查看目录结构",
    "inputSchema": {
        "json": {
            "type": "object",
            "properties": {
                "path": {
                    "type": "string",
                    "description": "要查看的目录路径，默认为当前目录"
                },
                "max_files": {
                    "type": "integer", 
                    "description": "最大显示文件数量，默认20"
                }
            }
        }
    }
}