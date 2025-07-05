"""
Unity AI助手流式代理模块
提供Strands Agent的实时流式响应功能
"""

import sys
import os
import ssl
# 确保使用UTF-8编码
if sys.version_info >= (3, 7):
    if hasattr(sys, 'set_int_max_str_digits'):
        sys.set_int_max_str_digits(0)
os.environ['PYTHONIOENCODING'] = 'utf-8'

# 配置SSL证书路径
try:
    import certifi
    # 使用certifi提供的证书束
    cert_path = certifi.where()
    os.environ['SSL_CERT_FILE'] = cert_path
    os.environ['REQUESTS_CA_BUNDLE'] = cert_path
    os.environ['CURL_CA_BUNDLE'] = cert_path
    print(f"[Python] 使用certifi证书路径: {cert_path}")
    
    # 验证证书文件存在
    if os.path.exists(cert_path):
        print(f"[Python] 证书文件验证成功: {cert_path}")
    else:
        print(f"[Python] 警告: 证书文件不存在: {cert_path}")
        
except ImportError:
    # 如果certifi不可用，使用系统默认证书
    print("[Python] certifi不可用，使用系统默认SSL证书")
    os.environ['SSL_CERT_FILE'] = '/etc/ssl/cert.pem'
    os.environ['REQUESTS_CA_BUNDLE'] = '/etc/ssl/cert.pem'

# 配置SSL上下文
try:
    # 配置SSL上下文但保持验证
    import ssl
    # 不使用unverified context，而是确保使用正确的证书
    print("[Python] 保持SSL验证启用，使用配置的证书")
except Exception as e:
    print(f"[Python] SSL配置警告: {e}")
    pass

import asyncio
import json
import logging
from typing import AsyncGenerator, Dict, Any
import signal

# 导入Unity代理类
from agent_core import get_agent

# 配置日志
logging.getLogger("strands").setLevel(logging.DEBUG)
logger = logging.getLogger(__name__)

# 获取工具增强的Unity代理实例
unity_agent = get_agent()
agent = unity_agent.agent

async def process_message_stream(message: str) -> AsyncGenerator[str, None]:
    """
    处理用户消息并流式返回AI响应
    
    参数:
        message: 用户输入消息
        
    生成:
        包含响应块的JSON字符串
    """
    try:
        logger.info(f"开始使用Unity代理处理消息: {message[:50]}...")
        logger.info(f"可用工具: {unity_agent.get_available_tools()}")
        
        # 使用异步流式API - 使用Unity代理的流式方法
        response_text = ""
        seen_chunks = set()  # 用于去重chunk内容
        async for chunk in unity_agent.process_message_stream(message):
            logger.debug(f"收到chunk: {chunk}")
            
            # 处理Unity代理返回的JSON格式块
            try:
                # chunk已经是JSON字符串，需要解析
                if isinstance(chunk, str):
                    chunk_data = json.loads(chunk)
                else:
                    chunk_data = chunk
                
                logger.debug(f"解析chunk数据: {chunk_data}")
                
                # 处理Unity代理的流式响应格式
                if chunk_data.get('type') == 'chunk':
                    content = chunk_data.get('content', '')
                    logger.debug(f"从Unity代理chunk提取内容: '{content}'")
                elif chunk_data.get('type') == 'complete':
                    # 流式完成，跳出循环
                    logger.info("Unity代理流式响应完成")
                    break
                elif chunk_data.get('type') == 'error':
                    # 处理错误
                    error_msg = chunk_data.get('error', '未知错误')
                    logger.error(f"Unity代理流式错误: {error_msg}")
                    raise Exception(error_msg)
                else:
                    # 可能是原始的Strands响应，尝试提取文本内容
                    if isinstance(chunk_data, dict):
                        # 提取纯文本内容，过滤掉元数据
                        if 'event' in chunk_data:
                            event = chunk_data['event']
                            if 'contentBlockDelta' in event:
                                delta = event['contentBlockDelta']
                                if 'delta' in delta and 'text' in delta['delta']:
                                    content = delta['delta']['text']
                                    logger.debug(f"从Strands event提取文本: '{content}'")
                                else:
                                    continue
                            else:
                                continue
                        else:
                            logger.debug(f"跳过非文本chunk: {str(chunk_data)[:100]}...")
                            continue
                    else:
                        logger.debug(f"未知chunk类型: {chunk_data}")
                        continue
                    
            except json.JSONDecodeError as e:
                logger.error(f"JSON解析错误: {e}, chunk: {chunk}")
                continue
            except Exception as e:
                logger.error(f"处理chunk时出错: {e}")
                continue
            
            # 跳过空内容
            if not content or len(content.strip()) == 0:
                logger.debug(f"跳过空内容")
                continue
                
            # 累积响应文本
            response_text += content
            
            # 返回每个文本块给Unity
            if content and content.strip():  # 确保不是空白内容
                # 确保内容是UTF-8编码的字符串
                if isinstance(content, bytes):
                    content = content.decode('utf-8')
                elif not isinstance(content, str):
                    content = str(content)
                
                # 直接输出内容，不进行人工分割
                if content not in seen_chunks:
                    seen_chunks.add(content)
                    logger.debug(f"输出chunk: '{content[:50]}{'...' if len(content) > 50 else ''}'")
                    yield json.dumps({
                        "type": "chunk", 
                        "content": content,
                        "done": False
                    }, ensure_ascii=False, separators=(',', ':'))
                else:
                    logger.debug(f"跳过重复chunk: '{content[:30]}{'...' if len(content) > 30 else ''}')")
        
        # 流式完成
        logger.info(f"Agent流式响应完成，总长度: {len(response_text)}字符")
        yield json.dumps({
            "type": "complete",
            "content": "",
            "done": True
        }, ensure_ascii=False, separators=(',', ':'))
        
    except Exception as e:
        # 提供更详细的错误信息
        error_msg = str(e)
        if 'SSL' in error_msg or 'certificate' in error_msg.lower():
            error_msg = "SSL连接错误，请检查网络连接和证书配置"
            logger.error(f"SSL错误检测: {str(e)}")
        elif 'No such file or directory' in error_msg:
            error_msg = "证书文件未找到，请检查SSL证书配置"
            logger.error(f"证书文件错误: {str(e)}")
        elif isinstance(e, UnicodeEncodeError):
            error_msg = "编码错误: 无法处理某些字符"
            logger.error(f"编码错误: {str(e)}")
        else:
            logger.error(f"流式响应错误: {error_msg}")
                
        yield json.dumps({
            "type": "error",
            "error": error_msg,
            "done": True
        }, ensure_ascii=False, separators=(',', ':'))

def process_message_sync(message: str) -> Dict[str, Any]:
    """
    同步消息处理（后备方案）
    
    参数:
        message: 用户输入消息
        
    返回:
        响应字典
    """
    try:
        logger.info(f"同步处理消息: {message[:50]}...")
        logger.info(f"使用Unity代理，可用工具: {unity_agent.get_available_tools()}")
        
        response = agent(message)
        
        # 确保响应是UTF-8编码的字符串
        if isinstance(response, bytes):
            response = response.decode('utf-8')
        elif not isinstance(response, str):
            response = str(response)
        
        logger.info(f"Unity代理同步响应完成，长度: {len(response)}字符")
        return {"success": True, "response": response}
    except Exception as e:
        # 提供更详细的错误信息
        error_msg = str(e)
        if 'SSL' in error_msg or 'certificate' in error_msg.lower():
            error_msg = "SSL连接错误，请检查网络连接和证书配置"
            logger.error(f"SSL错误检测: {str(e)}")
        elif 'No such file or directory' in error_msg:
            error_msg = "证书文件未找到，请检查SSL证书配置"
            logger.error(f"证书文件错误: {str(e)}")
        else:
            logger.error(f"同步处理错误: {str(e)}")
            
        return {"success": False, "error": error_msg}

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
    
    print("Unity AI 流式代理 (带工具支持)")
    print(f"可用工具: {unity_agent.get_available_tools()}")
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