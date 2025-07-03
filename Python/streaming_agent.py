"""
Unity AI助手流式代理模块
提供Strands Agent的实时流式响应功能
"""

from strands import Agent
import asyncio
import json
import logging
from typing import AsyncGenerator, Dict, Any
import signal
import sys

# 配置日志
logging.getLogger("strands").setLevel(logging.DEBUG)
logger = logging.getLogger(__name__)

# 创建代理实例
agent = Agent()

async def process_message_stream(message: str) -> AsyncGenerator[str, None]:
    """
    处理用户消息并流式返回AI响应
    
    参数:
        message: 用户输入消息
        
    生成:
        包含响应块的JSON字符串
    """
    try:
        # 使用异步流式API
        async for chunk in agent.astream(message):
            # 返回每个块给Unity
            yield json.dumps({
                "type": "chunk",
                "content": chunk,
                "done": False
            })
        
        # 流式完成
        yield json.dumps({
            "type": "complete",
            "content": "",
            "done": True
        })
        
    except Exception as e:
        logger.error(f"流式响应错误: {str(e)}")
        yield json.dumps({
            "type": "error",
            "error": str(e),
            "done": True
        })

def process_message_sync(message: str) -> Dict[str, Any]:
    """
    同步消息处理（后备方案）
    
    参数:
        message: 用户输入消息
        
    返回:
        响应字典
    """
    try:
        response = agent(message)
        return {"success": True, "response": response}
    except Exception as e:
        logger.error(f"同步处理错误: {str(e)}")
        return {"success": False, "error": str(e)}

async def stream_handler(message: str):
    """
    处理命令行测试的流式响应
    
    参数:
        message: 用户输入消息
    """
    print(f"正在处理: {message}")
    print("响应: ", end="", flush=True)
    
    async for chunk in process_message_stream(message):
        data = json.loads(chunk)
        if data["type"] == "chunk":
            print(data["content"], end="", flush=True)
        elif data["type"] == "complete":
            print("\n✓ 完成")
        elif data["type"] == "error":
            print(f"\n✗ 错误: {data['error']}")

def signal_handler(sig, frame):
    """处理中断信号"""
    print("\n已中断")
    sys.exit(0)

async def main():
    """测试的主入口点"""
    signal.signal(signal.SIGINT, signal_handler)
    
    print("Unity AI 流式代理")
    print("输入 'exit' 退出")
    print("-" * 50)
    
    while True:
        try:
            message = input("\n您: ")
            if message.lower() in ['exit', 'quit', '退出']:
                break
            
            await stream_handler(message)
            
        except EOFError:
            break
        except Exception as e:
            print(f"错误: {e}")

# Unity桥接函数
def unity_process_stream(message: str) -> str:
    """
    Unity可调用的流式处理函数
    返回Unity可用于获取块的生成器ID
    
    参数:
        message: 用户输入
        
    返回:
        包含流ID的JSON
    """
    # 在实际实现中，这会启动一个异步任务
    # 并返回Unity可以轮询的ID
    return json.dumps({
        "stream_id": "mock_stream_123",
        "message": "流式处理已启动"
    })

def unity_get_next_chunk(stream_id: str) -> str:
    """
    从流中获取下一个块
    
    参数:
        stream_id: 流标识符
        
    返回:
        包含块数据的JSON
    """
    # 模拟实现
    return json.dumps({
        "type": "chunk",
        "content": "模拟块",
        "done": False
    })

if __name__ == "__main__":
    # 运行交互式测试
    asyncio.run(main())