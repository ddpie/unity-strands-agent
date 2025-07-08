"""
诊断工具模块
提供Unity环境下的系统诊断、MCP连接测试等功能
"""

import json
import logging
import os
import subprocess
import sys
import threading
import time
from typing import Dict, Any

# 配置日志
logger = logging.getLogger(__name__)

def test_unity_directory() -> str:
    """测试Unity调用时的工作目录"""
    try:
        current_dir = os.getcwd()
        script_dir = os.path.dirname(__file__)
        
        result = {
            "current_dir": current_dir,
            "script_dir": script_dir,
            "script_file": __file__,
            "files_in_current": os.listdir(current_dir)[:10],  # 只显示前10个文件避免太长
            "config_paths_exist": {}
        }
        
        # 检查所有配置路径
        # 从环境变量获取配置路径
        mcp_config_path = os.environ.get('MCP_CONFIG_PATH')
        
        if mcp_config_path:
            config_paths = [mcp_config_path]
        else:
            config_paths = [
                "Assets/UnityAIAgent/mcp_config.json",
                "../Assets/UnityAIAgent/mcp_config.json",
                "../../Assets/UnityAIAgent/mcp_config.json",
                "mcp_config.json"
            ]
        
        for path in config_paths:
            result["config_paths_exist"][path] = {
                "exists": os.path.exists(path),
                "absolute_path": os.path.abspath(path)
            }
        
        return json.dumps(result, indent=2, ensure_ascii=False)
    except Exception as e:
        return json.dumps({"error": str(e)}, ensure_ascii=False)

def diagnose_unity_mcp_issue() -> str:
    """诊断Unity环境下MCP连接问题"""
    try:
        logger.info("=== Unity环境MCP连接诊断 ===")
        
        result = {
            "success": True,
            "environment": {
                "python_version": sys.version,
                "current_thread": threading.current_thread().name,
                "is_main_thread": threading.current_thread() == threading.main_thread(),
                "working_directory": os.getcwd()
            },
            "subprocess_tests": [],
            "mcp_tests": [],
            "asyncio_tests": [],
            "diagnosis": []
        }
        
        # 测试1: 基本子进程功能
        try:
            proc_result = subprocess.run(['echo', 'test'], capture_output=True, text=True, timeout=5)
            result["subprocess_tests"].append({
                "name": "基本echo测试",
                "success": True,
                "output": proc_result.stdout.strip(),
                "returncode": proc_result.returncode
            })
            logger.info("✅ 基本子进程功能正常")
        except Exception as e:
            result["subprocess_tests"].append({
                "name": "基本echo测试", 
                "success": False,
                "error": str(e)
            })
            result["diagnosis"].append("❌ Unity环境无法创建基本子进程")
            logger.error(f"❌ 基本子进程测试失败: {e}")
        
        # 测试1.5: 测试PATH环境变量
        try:
            path_env = os.environ.get('PATH', '')
            result["environment"]["path_env"] = path_env[:200] + "..." if len(path_env) > 200 else path_env
            logger.info(f"PATH环境变量: {path_env[:100]}...")
            
            # 测试which node
            proc_result = subprocess.run(['which', 'node'], capture_output=True, text=True, timeout=5)
            result["subprocess_tests"].append({
                "name": "which node测试",
                "success": proc_result.returncode == 0,
                "output": proc_result.stdout.strip() if proc_result.returncode == 0 else proc_result.stderr.strip(),
                "returncode": proc_result.returncode
            })
            if proc_result.returncode == 0:
                logger.info(f"✅ 找到node路径: {proc_result.stdout.strip()}")
            else:
                logger.warning(f"⚠️ 找不到node命令: {proc_result.stderr}")
        except Exception as e:
            result["subprocess_tests"].append({
                "name": "which node测试",
                "success": False,
                "error": str(e)
            })
            logger.error(f"❌ which node测试失败: {e}")
        
        # 测试2: Node.js可用性
        try:
            proc_result = subprocess.run(['node', '--version'], capture_output=True, text=True, timeout=5)
            node_success = proc_result.returncode == 0
            result["subprocess_tests"].append({
                "name": "Node.js版本检测",
                "success": node_success,
                "output": proc_result.stdout.strip() if node_success else proc_result.stderr.strip(),
                "returncode": proc_result.returncode
            })
            if node_success:
                logger.info(f"✅ Node.js可用: {proc_result.stdout.strip()}")
            else:
                logger.error(f"❌ Node.js不可用: {proc_result.stderr}")
                result["diagnosis"].append("❌ Node.js在Unity环境下不可用")
        except Exception as e:
            result["subprocess_tests"].append({
                "name": "Node.js版本检测",
                "success": False,
                "error": str(e)
            })
            result["diagnosis"].append("❌ 无法在Unity环境下执行Node.js")
            logger.error(f"❌ Node.js测试失败: {e}")
        
        # 测试2.5: 使用绝对路径的Node.js测试
        # 使用默认的Node.js路径列表
        node_paths = [
            '/usr/local/bin/node',
            '/opt/homebrew/bin/node',
            '/usr/bin/node',
            os.path.expanduser('~/.nvm/current/bin/node')
        ]
        
        for node_path in node_paths:
            if os.path.exists(node_path):
                try:
                    proc_result = subprocess.run([node_path, '--version'], capture_output=True, text=True, timeout=5)
                    node_abs_success = proc_result.returncode == 0
                    result["subprocess_tests"].append({
                        "name": f"Node.js绝对路径测试 ({node_path})",
                        "success": node_abs_success,
                        "output": proc_result.stdout.strip() if node_abs_success else proc_result.stderr.strip(),
                        "returncode": proc_result.returncode
                    })
                    if node_abs_success:
                        logger.info(f"✅ Node.js绝对路径可用: {node_path} -> {proc_result.stdout.strip()}")
                        break  # 找到一个可用的就停止
                    else:
                        logger.warning(f"⚠️ Node.js绝对路径失败: {node_path}")
                except Exception as e:
                    result["subprocess_tests"].append({
                        "name": f"Node.js绝对路径测试 ({node_path})",
                        "success": False,
                        "error": str(e)
                    })
                    logger.error(f"❌ Node.js绝对路径测试失败: {node_path} -> {e}")
                break  # 只测试第一个存在的路径
        
        # 测试3: MCP服务器文件存在性
        # 从环境变量获取MCP服务器路径
        mcp_server_path = os.environ.get('MCP_UNITY_SERVER_PATH', '')
        mcp_server_exists = os.path.exists(mcp_server_path) if mcp_server_path else False
        result["mcp_tests"].append({
            "name": "MCP服务器文件检查",
            "success": mcp_server_exists,
            "path": mcp_server_path,
            "exists": mcp_server_exists
        })
        
        if not mcp_server_exists:
            result["diagnosis"].append("❌ MCP服务器文件不存在")
            logger.error("❌ MCP服务器文件不存在")
        else:
            logger.info("✅ MCP服务器文件存在")
        
        # 测试4: MCP服务器启动测试（只有在前面测试通过时才执行）
        if len([t for t in result["subprocess_tests"] if t["success"]]) > 0 and mcp_server_exists:
            try:
                env = os.environ.copy()
                env['UNITY_PORT'] = '8090'
                
                # 使用Popen来测试stdio通信
                proc = subprocess.Popen(
                    ['node', mcp_server_path],
                    stdin=subprocess.PIPE,
                    stdout=subprocess.PIPE,
                    stderr=subprocess.PIPE,
                    env=env,
                    text=True
                )
                
                # 等待短时间
                time.sleep(1)
                
                if proc.poll() is None:
                    # 进程仍在运行，这是好兆头
                    result["mcp_tests"].append({
                        "name": "MCP服务器启动测试",
                        "success": True,
                        "message": "MCP服务器成功启动并保持运行"
                    })
                    logger.info("✅ MCP服务器可以在Unity环境下启动")
                    
                    # 尝试简单的stdio通信
                    try:
                        init_msg = '{"jsonrpc": "2.0", "method": "initialize", "id": 1, "params": {"protocolVersion": "2024-11-05", "capabilities": {}, "clientInfo": {"name": "unity-test", "version": "1.0"}}}\n'
                        proc.stdin.write(init_msg)
                        proc.stdin.flush()
                        time.sleep(0.5)
                        
                        result["mcp_tests"].append({
                            "name": "MCP stdio通信测试",
                            "success": True,
                            "message": "成功发送初始化消息"
                        })
                        logger.info("✅ MCP stdio通信正常")
                    except Exception as stdio_e:
                        result["mcp_tests"].append({
                            "name": "MCP stdio通信测试",
                            "success": False,
                            "error": str(stdio_e)
                        })
                        result["diagnosis"].append(f"❌ MCP stdio通信失败: {str(stdio_e)}")
                        logger.error(f"❌ MCP stdio通信失败: {stdio_e}")
                else:
                    # 进程已经退出
                    stdout, stderr = proc.communicate()
                    result["mcp_tests"].append({
                        "name": "MCP服务器启动测试",
                        "success": False,
                        "returncode": proc.returncode,
                        "stdout": stdout[:200] if stdout else "",
                        "stderr": stderr[:200] if stderr else ""
                    })
                    result["diagnosis"].append(f"❌ MCP服务器启动后立即退出，返回码: {proc.returncode}")
                    logger.error(f"❌ MCP服务器启动失败，返回码: {proc.returncode}")
                
                # 清理进程
                try:
                    if proc.poll() is None:
                        proc.terminate()
                        proc.wait(timeout=2)
                except:
                    try:
                        proc.kill()
                    except:
                        pass
                        
            except Exception as e:
                result["mcp_tests"].append({
                    "name": "MCP服务器启动测试",
                    "success": False,
                    "error": str(e)
                })
                result["diagnosis"].append(f"❌ MCP服务器启动异常: {str(e)}")
                logger.error(f"❌ MCP服务器启动异常: {e}")
        
        # 测试5: 异步环境检查
        try:
            import asyncio
            
            # 检查当前事件循环
            try:
                loop = asyncio.get_event_loop()
                result["asyncio_tests"].append({
                    "name": "当前事件循环检查",
                    "success": True,
                    "running": loop.is_running(),
                    "closed": loop.is_closed()
                })
                logger.info(f"✅ 当前事件循环状态: 运行={loop.is_running()}, 关闭={loop.is_closed()}")
            except RuntimeError as e:
                result["asyncio_tests"].append({
                    "name": "当前事件循环检查",
                    "success": False,
                    "error": str(e)
                })
                logger.info(f"ℹ️ 无当前事件循环: {e}")
            
            # 测试创建新事件循环
            try:
                new_loop = asyncio.new_event_loop()
                result["asyncio_tests"].append({
                    "name": "新事件循环创建",
                    "success": True,
                    "message": "可以创建新的事件循环"
                })
                new_loop.close()
                logger.info("✅ 可以创建新的事件循环")
            except Exception as e:
                result["asyncio_tests"].append({
                    "name": "新事件循环创建",
                    "success": False,
                    "error": str(e)
                })
                result["diagnosis"].append(f"❌ 无法创建异步事件循环: {str(e)}")
                logger.error(f"❌ 无法创建异步事件循环: {e}")
                
        except Exception as e:
            result["asyncio_tests"].append({
                "name": "asyncio模块检查",
                "success": False,
                "error": str(e)
            })
            result["diagnosis"].append(f"❌ asyncio模块检查失败: {str(e)}")
            logger.error(f"❌ asyncio模块检查失败: {e}")
        
        # 生成最终诊断
        if not result["diagnosis"]:
            result["diagnosis"].append("✅ Unity环境支持MCP所需的所有功能")
            logger.info("✅ Unity环境MCP支持正常")
        else:
            logger.warning(f"⚠️ 发现 {len(result['diagnosis'])} 个问题")
        
        logger.info(f"Unity MCP诊断完成: {len(result['diagnosis'])} 个问题")
        return json.dumps(result, ensure_ascii=False, indent=2)
        
    except Exception as e:
        logger.error(f"诊断过程失败: {e}")
        import traceback
        logger.error(f"诊断异常堆栈: {traceback.format_exc()}")
        return json.dumps({
            "success": False, 
            "error": str(e),
            "traceback": traceback.format_exc()
        }, ensure_ascii=False)