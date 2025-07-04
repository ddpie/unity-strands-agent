"""
Unity专用的Shell工具
不需要交互式确认，直接执行命令
"""

import subprocess
import os
from typing import Dict, Any

def unity_shell(command: str) -> Dict[str, Any]:
    """
    执行shell命令（Unity专用版本，无需确认）
    
    参数:
        command: 要执行的shell命令
        
    返回:
        包含执行结果的字典
    """
    try:
        # 设置环境变量
        env = os.environ.copy()
        
        # 执行命令
        result = subprocess.run(
            command,
            shell=True,
            capture_output=True,
            text=True,
            env=env,
            timeout=30  # 30秒超时
        )
        
        # 返回结果
        return {
            "success": result.returncode == 0,
            "stdout": result.stdout,
            "stderr": result.stderr,
            "returncode": result.returncode,
            "command": command
        }
        
    except subprocess.TimeoutExpired:
        return {
            "success": False,
            "stdout": "",
            "stderr": "命令执行超时（30秒）",
            "returncode": -1,
            "command": command
        }
    except Exception as e:
        return {
            "success": False,
            "stdout": "",
            "stderr": f"执行错误: {str(e)}",
            "returncode": -1,
            "command": command
        }

# 工具定义，符合Strands工具规范
unity_shell.__tool_name__ = "unity_shell"
unity_shell.__tool_description__ = "执行shell命令（Unity版本，无需确认）"