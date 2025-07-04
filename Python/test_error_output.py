#!/usr/bin/env python3
"""
测试错误输出到聊天界面的脚本
"""

import agent_core
import asyncio
import logging

# 配置日志
logging.basicConfig(level=logging.INFO, format='%(asctime)s - %(name)s - %(levelname)s - %(message)s')

async def test_error_output():
    print("正在测试错误输出...")
    agent = agent_core.UnityAgent()
    
    # 测试1: 引发一个简单的错误
    error_message = """
    引发一个测试错误，看看错误信息是否会显示在聊天界面。
    请执行这个会出错的代码：
    
    ```python
    # 这会引发一个除零错误
    result = 10 / 0
    ```
    """
    
    print("发送错误测试消息...")
    chunk_count = 0
    try:
        async for chunk in agent.process_message_stream(error_message):
            chunk_count += 1
            print(f"Chunk #{chunk_count}: {chunk[:100]}...")
    except Exception as e:
        print(f"捕获到异常: {e}")
    
    print(f"测试完成，共收到{chunk_count}个chunk")

if __name__ == "__main__":
    asyncio.run(test_error_output())