#!/usr/bin/env python3
"""
测试MCP连接的脚本
"""

import sys
import os

# 添加当前目录到Python路径
current_dir = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, current_dir)

from agent_core import get_agent, reload_mcp_config

def test_mcp_connection():
    print("=== 测试MCP连接 ===")
    
    # 获取代理实例
    print("1. 获取Unity代理实例...")
    agent = get_agent()
    print(f"   ✓ 代理创建成功: {type(agent)}")
    
    # 检查可用工具
    print("2. 检查可用工具...")
    tools = agent.get_available_tools()
    print(f"   ✓ 可用工具数量: {len(tools)}")
    for tool in tools:
        print(f"     - {tool}")
    
    # 重新加载MCP配置
    print("3. 重新加载MCP配置...")
    result = reload_mcp_config()
    print(f"   ✓ 重新加载结果: {result}")
    
    # 再次检查工具
    print("4. 重新加载后检查工具...")
    agent = get_agent()
    tools = agent.get_available_tools()
    print(f"   ✓ 重新加载后工具数量: {len(tools)}")
    for tool in tools:
        print(f"     - {tool}")

if __name__ == "__main__":
    test_mcp_connection()