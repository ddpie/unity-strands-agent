using UnityEngine;
using UnityEditor;
using Python.Runtime;
using System;
using System.Threading.Tasks;

namespace UnityAIAgent.Editor
{
    /// <summary>
    /// Python.NET桥接类，处理C#与Python之间的通信
    /// </summary>
    public static class PythonBridge
    {
        private static dynamic agentCore;
        private static bool isInitialized = false;

        /// <summary>
        /// 初始化Python桥接
        /// </summary>
        public static void Initialize()
        {
            if (isInitialized) return;

            try
            {
                PythonManager.EnsureInitialized();

                using (Py.GIL())
                {
                    // 调试：检查Python路径
                    dynamic sys = Py.Import("sys");
                    dynamic pathList = sys.path;
                    var paths = new System.Collections.Generic.List<string>();
                    
                    // 使用Python的len()函数获取列表长度
                    using (var builtins = Py.Import("builtins"))
                    {
                        int pathCount = (int)builtins.InvokeMethod("len", pathList);
                        for (int i = 0; i < pathCount; i++)
                        {
                            paths.Add(pathList[i].ToString());
                        }
                    }
                    Debug.Log($"Python sys.path: {string.Join(", ", paths)}");
                    
                    // 手动添加插件路径到sys.path（如果不存在）
                    string pluginPath = PathManager.GetUnityAgentPythonPath();
                    if (!string.IsNullOrEmpty(pluginPath) && !paths.Contains(pluginPath))
                    {
                        sys.path.insert(0, pluginPath);
                        Debug.Log($"手动添加插件路径到sys.path: {pluginPath}");
                    }
                    
                    // 导入Python模块
                    agentCore = Py.Import("agent_core");
                    // streaming_agent functionality is now integrated into agent_core
                    
                    // 配置Python日志输出到Unity Console
                    ConfigurePythonLogging();
                    
                    Debug.Log("Python桥接初始化成功");
                    isInitialized = true;
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"Python桥接初始化失败: {e.Message}");
                throw;
            }
        }

        /// <summary>
        /// 同步处理消息
        /// </summary>
        /// <param name="message">用户输入消息</param>
        /// <returns>AI响应</returns>
        public static string ProcessMessage(string message)
        {
            EnsureInitialized();

            try
            {
                Debug.Log($"[Unity] 开始处理同步消息: {message.Substring(0, Math.Min(message.Length, 50))}{(message.Length > 50 ? "..." : "")}");
                
                using (Py.GIL())
                {
                    dynamic result = agentCore.process_sync(message);
                    string response = result.ToString();
                    
                    Debug.Log($"[Unity] 同步处理完成，响应长度: {response.Length}字符");
                    
                    return response;
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"处理消息时出错: {e.Message}");
                return $"{{\"success\": false, \"error\": \"{e.Message}\"}}";
            }
        }

        /// <summary>
        /// 异步流式处理消息
        /// </summary>
        /// <param name="message">用户输入消息</param>
        /// <param name="onChunk">收到数据块时的回调</param>
        /// <param name="onComplete">完成时的回调</param>
        /// <param name="onError">出错时的回调</param>
        public static async Task ProcessMessageStream(
            string message, 
            Action<string> onChunk, 
            Action onComplete, 
            Action<string> onError)
        {
            EnsureInitialized();

            try
            {
                Debug.Log($"[Unity] 开始流式处理消息: {message.Substring(0, Math.Min(message.Length, 50))}{(message.Length > 50 ? "..." : "")}");
                
                // 使用EditorCoroutine代替Task.Run来避免线程中止
                var processCompleted = false;
                var processError = "";
                
                // 检查Unity是否正在切换模式
                if (ThreadProtection.IsUnityChangingMode)
                {
                    Debug.LogWarning("[Unity] Unity正在切换模式，取消流式处理");
                    EditorApplication.delayCall += () => onError?.Invoke("Unity正在切换模式");
                    return;
                }
                
                // 使用受保护的线程
                var thread = ThreadProtection.CreateProtectedThread(() =>
                {
                    try
                    {
                        // 在线程开始时再次检查Unity状态和Python状态
                        if (ThreadProtection.IsUnityChangingMode)
                        {
                            Debug.LogWarning("[Unity] 线程启动时Unity正在切换模式，取消处理");
                            return;
                        }
                        
                        if (!PythonEngine.IsInitialized)
                        {
                            Debug.LogWarning("[Unity] 线程启动时PythonEngine未初始化，取消处理");
                            return;
                        }

                        using (Py.GIL())
                        {
                        dynamic asyncio = Py.Import("asyncio");
                        dynamic loop = asyncio.new_event_loop();
                        asyncio.set_event_loop(loop);

                        try
                        {
                            // 获取流式生成器
                            Debug.Log("[Unity] 创建流式生成器");
                            // 使用agent_core的流式处理功能
                            dynamic unityAgent = agentCore.get_agent();
                            dynamic streamGen = unityAgent.process_message_stream(message);
                            Debug.Log("[Unity] 流式生成器创建成功，开始处理流...");
                            
                            // 处理流式数据
                            int chunkIndex = 0;
                            while (true)
                            {
                                try
                                {
                                    // 在每个循环中检查Unity状态
                                    if (ThreadProtection.IsUnityChangingMode || !PythonEngine.IsInitialized)
                                    {
                                        Debug.LogWarning("[Unity] 检测到Unity状态变化或Python引擎关闭，退出流处理");
                                        EditorApplication.delayCall += () => onError?.Invoke("Unity状态变化，流处理被中断");
                                        break;
                                    }
                                    
                                    chunkIndex++;
                                    Debug.Log($"[Unity] 等待第 {chunkIndex} 个chunk...");
                                    dynamic chunk = loop.run_until_complete(streamGen.__anext__());
                                    string chunkStr = chunk.ToString();
                                    Debug.Log($"[Unity] 收到第 {chunkIndex} 个chunk，长度: {chunkStr.Length}");
                                    
                                    // 解析JSON
                                    var chunkData = JsonUtility.FromJson<StreamChunk>(chunkStr);
                                    Debug.Log($"[Unity] 解析chunk数据: type={chunkData.type}");
                                    
                                    if (chunkData.type == "chunk")
                                    {
                                        string content = chunkData.content;
                                        Debug.Log($"[Unity] 收到Agent响应块: {content.Substring(0, Math.Min(content.Length, 100))}{(content.Length > 100 ? "..." : "")}");
                                        
                                        // 专门检查file_read相关内容
                                        if (content.Contains("[FILE_READ]") || content.Contains("file_read"))
                                        {
                                            Debug.Log($"[Unity] 📖 检测到FILE_READ相关内容: {content}");
                                        }
                                        
                                        EditorApplication.delayCall += () => onChunk?.Invoke(content);
                                    }
                                    else if (chunkData.type == "complete")
                                    {
                                        Debug.Log("[Unity] Agent流式响应完成");
                                        EditorApplication.delayCall += () => onComplete?.Invoke();
                                        break;
                                    }
                                    else if (chunkData.type == "error")
                                    {
                                        Debug.LogError($"[Unity] Agent响应错误: {chunkData.error}");
                                        EditorApplication.delayCall += () => onError?.Invoke(chunkData.error);
                                        break;
                                    }
                                }
                                catch (PythonException stopIteration) when (stopIteration.Message.Contains("StopAsyncIteration"))
                                {
                                    // 流正常结束
                                    Debug.Log($"[Unity] Agent流式响应正常结束，总共处理了 {chunkIndex} 个chunk");
                                    EditorApplication.delayCall += () => onComplete?.Invoke();
                                    break;
                                }
                                catch (System.Threading.ThreadAbortException)
                                {
                                    // Unity进入播放模式或重新编译时的正常行为
                                    Debug.LogWarning($"[Unity] 线程被中止（通常因为Unity进入播放模式）");
                                    EditorApplication.delayCall += () => onError?.Invoke("AI响应被中断（Unity进入播放模式）");
                                    break;
                                }
                                catch (Exception chunkError)
                                {
                                    Debug.LogError($"[Unity] 处理第 {chunkIndex} 个chunk时出错: {chunkError.Message}");
                                    Debug.LogError($"[Unity] 错误详情: {chunkError}");
                                    // 继续处理下一个chunk，不要中断整个流
                                }
                            }
                        }
                        finally
                        {
                            loop.close();
                        }
                        } // 关闭 using (Py.GIL())
                    }
                    catch (System.Threading.ThreadAbortException)
                    {
                        Debug.LogWarning("[Unity] Python处理线程被中止");
                        processError = "处理被中断（Unity模式切换）";
                        System.Threading.Thread.ResetAbort(); // 重置中止状态
                    }
                    catch (System.InvalidOperationException ex) when (ex.Message.Contains("PythonEngine is not initialized"))
                    {
                        Debug.LogWarning("[Unity] PythonEngine已关闭，这通常发生在Unity模式切换时");
                        processError = "Python引擎已关闭（Unity模式切换）";
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError($"[Unity] 处理线程异常: {ex}");
                        processError = ex.Message;
                    }
                    finally
                    {
                        processCompleted = true;
                    }
                });
                
                // 设置为后台线程
                thread.IsBackground = true;
                thread.Start();
                
                // 等待线程完成
                await Task.Run(() =>
                {
                    while (!processCompleted && thread.IsAlive)
                    {
                        System.Threading.Thread.Sleep(100);
                    }
                });
                
                if (!string.IsNullOrEmpty(processError))
                {
                    EditorApplication.delayCall += () => onError?.Invoke(processError);
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"流式处理出错: {e.Message}");
                EditorApplication.delayCall += () => onError?.Invoke(e.Message);
            }
        }

        /// <summary>
        /// 健康检查
        /// </summary>
        /// <returns>健康状态JSON</returns>
        public static string HealthCheck()
        {
            try
            {
                EnsureInitialized();

                using (Py.GIL())
                {
                    dynamic result = agentCore.health_check();
                    return result.ToString();
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"健康检查失败: {e.Message}");
                return $"{{\"status\": \"unhealthy\", \"error\": \"{e.Message}\", \"ready\": false}}";
            }
        }

        /// <summary>
        /// 获取Python版本信息
        /// </summary>
        /// <returns>Python版本字符串</returns>
        public static string GetPythonVersion()
        {
            try
            {
                using (Py.GIL())
                {
                    dynamic sys = Py.Import("sys");
                    return sys.version.ToString();
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"获取Python版本失败: {e.Message}");
                return "Unknown";
            }
        }

        /// <summary>
        /// 执行Python代码片段（调试用）
        /// </summary>
        /// <param name="code">Python代码</param>
        /// <returns>执行结果</returns>
        public static string ExecutePython(string code)
        {
            try
            {
                EnsureInitialized();

                using (Py.GIL())
                {
                    var scope = Py.CreateScope();
                    scope.Exec(code);
                    return "执行成功";
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"Python代码执行失败: {e.Message}");
                return $"错误: {e.Message}";
            }
        }

        private static void EnsureInitialized()
        {
            if (!isInitialized)
            {
                Initialize();
            }
        }

        /// <summary>
        /// 配置Python日志输出到Unity Console
        /// </summary>
        private static void ConfigurePythonLogging()
        {
            try
            {
                using (Py.GIL())
                {
                    // 创建自定义日志处理器，将Python日志转发到Unity Console
                    string loggerSetupCode = @"
import logging
import sys

class UnityLogHandler(logging.Handler):
    def emit(self, record):
        msg = self.format(record)
        # 通过sys.stdout发送到Unity
        print(f'[Python] {msg}')
        sys.stdout.flush()

# 获取根logger和相关logger
loggers = ['agent_core', 'strands']
unity_handler = UnityLogHandler()
unity_handler.setLevel(logging.INFO)
formatter = logging.Formatter('%(name)s - %(levelname)s - %(message)s')
unity_handler.setFormatter(formatter)

# 为每个logger添加Unity处理器
for logger_name in loggers:
    logger = logging.getLogger(logger_name)
    logger.addHandler(unity_handler)
    logger.setLevel(logging.INFO)

print('[Python] Unity日志处理器配置完成')
";
                    
                    var scope = Py.CreateScope();
                    scope.Exec(loggerSetupCode);
                    
                    Debug.Log("Python日志配置完成");
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"配置Python日志失败: {e.Message}");
            }
        }

        [Serializable]
        private class StreamChunk
        {
            public string type;
            public string content;
            public string error;
            public bool done;
        }
        
        /// <summary>
        /// 清理Python桥接资源
        /// </summary>
        public static void Shutdown()
        {
            if (isInitialized)
            {
                try
                {
                    using (Py.GIL())
                    {
                        // 清理 Python 对象
                        agentCore = null;
                        // 使用Python内置垃圾回收
                        using (var gc = Py.Import("gc"))
                        {
                            gc.InvokeMethod("collect");
                        }
                    }
                    Debug.Log("Python桥接已清理");
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"清理 Python 对象时出错: {e.Message}");
                }
                finally
                {
                    isInitialized = false;
                }
            }
        }
    }

    /// <summary>
    /// Unity主线程调度器，用于在主线程执行回调
    /// </summary>
    public static class UnityMainThreadDispatcher
    {
        private static readonly System.Collections.Generic.Queue<Action> _executionQueue = 
            new System.Collections.Generic.Queue<Action>();

        static UnityMainThreadDispatcher()
        {
            EditorApplication.update += Update;
        }

        public static void Enqueue(Action action)
        {
            lock (_executionQueue)
            {
                _executionQueue.Enqueue(action);
            }
        }

        private static void Update()
        {
            lock (_executionQueue)
            {
                while (_executionQueue.Count > 0)
                {
                    _executionQueue.Dequeue().Invoke();
                }
            }
        }
    }
}