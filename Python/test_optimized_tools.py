#!/usr/bin/env python3
"""
测试优化后的Unity工具
"""

import asyncio
import logging
from agent_core import UnityAgent

# 配置日志
logging.basicConfig(
    level=logging.INFO,
    format='%(asctime)s - %(name)s - %(levelname)s - %(message)s'
)

async def test_optimized_tools():
    print("=== 测试优化后的Unity工具 ===\n")
    
    # 创建agent
    print("初始化Unity Agent...")
    agent = UnityAgent()
    print("Agent初始化完成\n")
    
    # 测试用例
    test_cases = [
        "使用unity_shell执行pwd命令",
        "使用unity_directory_list列出当前目录的内容",
        "使用unity_project_info获取Unity项目信息",
        "使用unity_analyze_csharp分析一个C#文件：Editor/AIAgentWindow.cs"
    ]
    
    for i, test_case in enumerate(test_cases, 1):
        print(f"\n{'='*60}")
        print(f"测试 {i}: {test_case}")
        print('='*60)
        
        try:
            chunk_count = 0
            response_chunks = []
            
            async for chunk in agent.process_message_stream(test_case):
                chunk_count += 1
                response_chunks.append(chunk)
                
                # 打印前几个chunk
                if chunk_count <= 3:
                    print(f"Chunk #{chunk_count}: {chunk[:100]}...")
            
            print(f"\n总共收到 {chunk_count} 个chunks")
            
            # 检查是否使用了正确的工具
            full_response = "".join(response_chunks)
            if "unity_" in full_response:
                print("✅ 成功使用了Unity工具")
            else:
                print("⚠️ 可能没有使用Unity工具")
                
        except Exception as e:
            print(f"❌ 测试失败: {e}")
            import traceback
            traceback.print_exc()
        
        # 稍微延迟，避免请求过快
        await asyncio.sleep(1)
    
    print("\n\n=== 测试完成 ===")

if __name__ == "__main__":
    asyncio.run(test_optimized_tools())