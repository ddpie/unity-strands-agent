"""
Unity专用Shell工具 - 符合Strands工具规范
自动执行系统命令，无需用户确认
"""

import os
import subprocess
import logging
from typing import Any, Dict, List, Union
from strands.types.tools import ToolResult, ToolUse

logger = logging.getLogger(__name__)

TOOL_SPEC = {
    "name": "unity_shell",
    "description": "Unity专用Shell工具 - 自动执行命令，无需用户确认。默认在Unity项目根目录执行。",
    "inputSchema": {
        "json": {
            "type": "object",
            "properties": {
                "command": {
                    "type": ["string", "array"],
                    "description": "要执行的shell命令，可以是单个命令字符串或命令数组"
                },
                "work_dir": {
                    "type": "string",
                    "description": "工作目录，默认为Unity项目根目录"
                },
                "timeout": {
                    "type": "integer",
                    "description": "超时时间（秒），默认30秒"
                }
            },
            "required": ["command"]
        }
    }
}

def get_unity_project_root():
    """获取Unity项目根目录"""
    # 基于我们找到的实际Unity项目路径
    potential_projects = [
        "/Users/caobao/projects/unity/Tetris",
        "/Users/caobao/projects/unity/InfiniteRunning", 
        "/Users/caobao/projects/unity/2D-Demo",
        "/Users/caobao/projects/unity/unity-strands-agent"
    ]
    
    # 检查哪个目录存在并包含Unity项目文件
    for project_path in potential_projects:
        if os.path.exists(project_path):
            # 检查是否包含Unity项目标识文件
            unity_indicators = ["Assets", "ProjectSettings"]
            has_unity_files = any(
                os.path.exists(os.path.join(project_path, indicator)) 
                for indicator in unity_indicators
            )
            if has_unity_files:
                logger.info(f"找到Unity项目根目录: {project_path}")
                return project_path
    
    # 回退到默认目录
    default_dir = "/Users/caobao/projects/unity/unity-strands-agent"
    logger.warning(f"未找到Unity项目根目录，使用默认目录: {default_dir}")
    return default_dir

def unity_shell(tool: ToolUse, **kwargs: Any) -> ToolResult:
    """Unity专用Shell工具 - 自动执行，无用户交互"""
    tool_use_id = tool.get("toolUseId", "default-id")
    tool_input = tool.get("input", {})
    
    command = tool_input.get("command")
    work_dir = tool_input.get("work_dir", get_unity_project_root())
    timeout = tool_input.get("timeout", 30)
    
    if not command:
        return {
            "toolUseId": tool_use_id,
            "status": "error",
            "content": [{"text": "命令不能为空"}]
        }
    
    # 处理命令数组
    if isinstance(command, list):
        commands = command
    else:
        commands = [command]
    
    results = []
    
    for cmd in commands:
        try:
            logger.info(f"Unity Shell执行命令: {cmd}")
            logger.info(f"工作目录: {work_dir}")
            
            # 确保工作目录存在
            if not os.path.exists(work_dir):
                logger.warning(f"工作目录不存在，创建: {work_dir}")
                os.makedirs(work_dir, exist_ok=True)
            
            # 使用subprocess执行命令
            result = subprocess.run(
                cmd,
                shell=True,
                capture_output=True,
                text=True,
                timeout=timeout,
                cwd=work_dir
            )
            
            cmd_result = {
                "command": cmd,
                "exit_code": result.returncode,
                "stdout": result.stdout.strip() if result.stdout else "",
                "stderr": result.stderr.strip() if result.stderr else "",
                "status": "success" if result.returncode == 0 else "error",
                "work_dir": work_dir
            }
            
            results.append(cmd_result)
            logger.info(f"命令执行完成: {cmd}, 退出码: {result.returncode}")
            
        except subprocess.TimeoutExpired:
            error_result = {
                "command": cmd,
                "exit_code": -1,
                "stdout": "",
                "stderr": f"命令超时（{timeout}秒）",
                "status": "error",
                "work_dir": work_dir
            }
            results.append(error_result)
            logger.error(f"命令超时: {cmd}")
            
        except Exception as e:
            error_result = {
                "command": cmd,
                "exit_code": -1,
                "stdout": "",
                "stderr": str(e),
                "status": "error",
                "work_dir": work_dir
            }
            results.append(error_result)
            logger.error(f"命令执行异常: {cmd}, 错误: {e}")
    
    # 构建返回结果
    success_count = sum(1 for r in results if r["status"] == "success")
    total_count = len(results)
    
    content_lines = [f"执行了 {total_count} 个命令，成功 {success_count} 个"]
    content_lines.append(f"工作目录: {work_dir}")
    content_lines.append("")
    
    for result in results:
        content_lines.append(f"命令: {result['command']}")
        content_lines.append(f"状态: {result['status']}")
        content_lines.append(f"退出码: {result['exit_code']}")
        
        if result['stdout']:
            content_lines.append(f"输出:\n{result['stdout']}")
        
        if result['stderr']:
            content_lines.append(f"错误:\n{result['stderr']}")
        
        content_lines.append("-" * 50)
    
    overall_status = "success" if success_count == total_count else "error"
    content_text = "\n".join(content_lines)
    
    return {
        "toolUseId": tool_use_id,
        "status": overall_status,
        "content": [{"text": content_text}]
    }