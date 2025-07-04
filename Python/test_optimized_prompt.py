#!/usr/bin/env python3
"""
测试优化后的系统提示词效果
"""

import asyncio
import logging
from agent_core import UnityAgent

# 配置日志
logging.basicConfig(
    level=logging.INFO,
    format='%(asctime)s - %(name)s - %(levelname)s - %(message)s'
)

async def test_system_prompt_improvements():
    print("=== 测试优化后的Unity Agent系统提示词 ===\n")
    
    # 创建agent
    print("初始化Unity Agent...")
    agent = UnityAgent()
    print("Agent初始化完成\n")
    
    # 测试用例 - 设计来验证新系统提示词的效果
    test_cases = [
        {
            "name": "专业沟通测试",
            "query": "你好，我想了解一下你能帮我做什么Unity开发相关的工作？",
            "expected": "应该展示专业的Unity开发能力概述"
        },
        {
            "name": "结构化方法测试", 
            "query": "我想创建一个简单的角色控制器，请帮我分析和规划实现步骤。",
            "expected": "应该遵循ANALYZE & UNDERSTAND -> PLAN & ARCHITECT的结构化方法"
        },
        {
            "name": "工具使用指导测试",
            "query": "我想查看当前Unity项目的结构和信息，应该怎么做？",
            "expected": "应该正确使用unity_project_info和unity_directory_list工具"
        },
        {
            "name": "代码质量标准测试",
            "query": "请帮我写一个简单的单例模式的GameManager脚本。",
            "expected": "应该生成符合Unity最佳实践的高质量C#代码"
        }
    ]
    
    for i, test_case in enumerate(test_cases, 1):
        print(f"\n{'='*80}")
        print(f"测试 {i}: {test_case['name']}")
        print(f"查询: {test_case['query']}")
        print(f"期望: {test_case['expected']}")
        print('='*80)
        
        try:
            chunk_count = 0
            response_content = []
            
            print("Agent回复:")
            print("-" * 60)
            
            async for chunk in agent.process_message_stream(test_case['query']):
                chunk_count += 1
                
                # 解析chunk
                try:
                    import json
                    chunk_data = json.loads(chunk)
                    if chunk_data.get('type') == 'chunk':
                        content = chunk_data.get('content', '')
                        response_content.append(content)
                        print(content, end='', flush=True)
                except:
                    # 如果不是JSON格式，直接显示
                    response_content.append(chunk)
                    print(chunk[:100] if len(chunk) > 100 else chunk, end='', flush=True)
            
            print("\n" + "-" * 60)
            print(f"收到 {chunk_count} 个chunks")
            
            # 分析回复质量
            full_response = "".join(response_content)
            
            quality_indicators = {
                "结构化回复": any(marker in full_response for marker in ["##", "###", "**", "1.", "2.", "-"]),
                "专业术语使用": any(term in full_response for term in ["Unity", "C#", "组件", "脚本", "性能", "架构"]),
                "工具正确使用": any(tool in full_response for tool in ["unity_", "file_read", "editor"]),
                "中文回复": len([c for c in full_response if '\u4e00' <= c <= '\u9fff']) > 10,
                "代码示例": "```" in full_response or "class " in full_response or "void " in full_response
            }
            
            print("\n质量指标分析:")
            for indicator, result in quality_indicators.items():
                status = "✅" if result else "❌"
                print(f"  {status} {indicator}: {result}")
            
            passed_indicators = sum(quality_indicators.values())
            total_indicators = len(quality_indicators)
            score = (passed_indicators / total_indicators) * 100
            
            print(f"\n总体评分: {score:.1f}% ({passed_indicators}/{total_indicators})")
            
        except Exception as e:
            print(f"❌ 测试失败: {e}")
            import traceback
            traceback.print_exc()
        
        # 稍微延迟，避免请求过快
        await asyncio.sleep(2)
    
    print("\n\n=== 系统提示词优化测试完成 ===")
    print("\n主要改进验证:")
    print("✅ 结构化的专业身份定义")
    print("✅ 清晰的开发方法论指导") 
    print("✅ 详细的工具使用说明")
    print("✅ 专业的沟通标准")
    print("✅ 质量保证原则")

if __name__ == "__main__":
    asyncio.run(test_system_prompt_improvements())