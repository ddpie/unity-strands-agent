#!/usr/bin/env python3
"""
测试超时机制
用于验证流式响应的超时保护是否正常工作
"""

import asyncio
import json
import time
from agent_core import get_agent

async def test_timeout_protection():
    """测试超时保护机制"""
    print("🧪 开始测试超时保护机制...")
    
    # 获取代理实例
    agent = get_agent()
    
    # 测试1: 正常响应（不应该超时）
    print("\n📝 测试1: 正常响应")
    try:
        async for chunk in agent.process_message_stream("你好"):
            chunk_data = json.loads(chunk)
            if chunk_data.get("type") == "chunk":
                print(f"✅ 正常chunk: {chunk_data['content'][:50]}...")
            elif chunk_data.get("type") == "complete":
                print("✅ 正常完成")
                break
            elif chunk_data.get("type") == "error":
                print(f"❌ 错误: {chunk_data['error']}")
                break
    except Exception as e:
        print(f"❌ 测试1失败: {e}")
    
    # 测试2: 模拟超时情况（使用一个可能导致超时的复杂查询）
    print("\n📝 测试2: 复杂查询（可能触发超时）")
    start_time = time.time()
    chunk_count = 0
    
    try:
        async for chunk in agent.process_message_stream("请详细分析Unity中的内存管理机制，包括GC、对象池、内存优化等所有方面，并提供具体的代码示例"):
            chunk_data = json.loads(chunk)
            chunk_count += 1
            
            if chunk_data.get("type") == "chunk":
                print(f"📨 chunk #{chunk_count}: {chunk_data['content'][:30]}...")
            elif chunk_data.get("type") == "complete":
                elapsed = time.time() - start_time
                print(f"✅ 完成，耗时: {elapsed:.2f}秒，总chunk数: {chunk_count}")
                break
            elif chunk_data.get("type") == "error":
                elapsed = time.time() - start_time
                print(f"⚠️ 错误（耗时{elapsed:.2f}秒）: {chunk_data['error']}")
                break
                
    except Exception as e:
        elapsed = time.time() - start_time
        print(f"❌ 测试2异常（耗时{elapsed:.2f}秒）: {e}")
    
    print("\n🎯 超时保护测试完成")

if __name__ == "__main__":
    asyncio.run(test_timeout_protection())