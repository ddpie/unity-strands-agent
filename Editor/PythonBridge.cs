using UnityEngine;
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
        private static dynamic streamingAgent;
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
                    // 导入Python模块
                    agentCore = Py.Import("agent_core");
                    streamingAgent = Py.Import("streaming_agent");
                    
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
                using (Py.GIL())
                {
                    dynamic result = agentCore.process_sync(message);
                    return result.ToString();
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
                await Task.Run(() =>
                {
                    using (Py.GIL())
                    {
                        dynamic asyncio = Py.Import("asyncio");
                        dynamic loop = asyncio.new_event_loop();
                        asyncio.set_event_loop(loop);

                        try
                        {
                            // 获取流式生成器
                            dynamic streamGen = streamingAgent.process_message_stream(message);
                            
                            // 处理流式数据
                            while (true)
                            {
                                try
                                {
                                    dynamic chunk = loop.run_until_complete(streamGen.__anext__());
                                    string chunkStr = chunk.ToString();
                                    
                                    // 解析JSON
                                    var chunkData = JsonUtility.FromJson<StreamChunk>(chunkStr);
                                    
                                    if (chunkData.type == "chunk")
                                    {
                                        UnityMainThreadDispatcher.Enqueue(() => onChunk?.Invoke(chunkData.content));
                                    }
                                    else if (chunkData.type == "complete")
                                    {
                                        UnityMainThreadDispatcher.Enqueue(() => onComplete?.Invoke());
                                        break;
                                    }
                                    else if (chunkData.type == "error")
                                    {
                                        UnityMainThreadDispatcher.Enqueue(() => onError?.Invoke(chunkData.error));
                                        break;
                                    }
                                }
                                catch (PythonException stopIteration) when (stopIteration.Message.Contains("StopAsyncIteration"))
                                {
                                    // 流正常结束
                                    UnityMainThreadDispatcher.Enqueue(() => onComplete?.Invoke());
                                    break;
                                }
                            }
                        }
                        finally
                        {
                            loop.close();
                        }
                    }
                });
            }
            catch (Exception e)
            {
                Debug.LogError($"流式处理出错: {e.Message}");
                onError?.Invoke(e.Message);
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

        [Serializable]
        private class StreamChunk
        {
            public string type;
            public string content;
            public string error;
            public bool done;
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