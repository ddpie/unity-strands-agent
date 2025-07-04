using UnityEngine;
using UnityEditor;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Threading;

namespace UnityAIAgent.Editor
{
    /// <summary>
    /// 流式响应处理器，管理AI响应的实时流式输出
    /// </summary>
    public class StreamingHandler
    {
        private bool isStreaming = false;
        private CancellationTokenSource cancellationTokenSource;
        private Queue<StreamChunk> chunkQueue = new Queue<StreamChunk>();
        private readonly object queueLock = new object();
        
        // 事件回调
        public event Action<string> OnChunkReceived;
        public event Action OnStreamStarted;
        public event Action OnStreamCompleted;
        public event Action<string> OnStreamError;
        public event Action OnStreamCancelled;
        
        public bool IsStreaming => isStreaming;
        
        /// <summary>
        /// 开始流式处理消息
        /// </summary>
        /// <param name="message">用户输入消息</param>
        public async Task StartStreaming(string message)
        {
            if (isStreaming)
            {
                Debug.LogWarning("已有正在进行的流式处理，请先停止当前流");
                return;
            }
            
            isStreaming = true;
            cancellationTokenSource = new CancellationTokenSource();
            
            OnStreamStarted?.Invoke();
            
            try
            {
                await ProcessStreamAsync(message, cancellationTokenSource.Token);
            }
            catch (OperationCanceledException)
            {
                Debug.Log("流式处理已取消");
                OnStreamCancelled?.Invoke();
            }
            catch (Exception e)
            {
                Debug.LogError($"流式处理出错: {e.Message}");
                OnStreamError?.Invoke(e.Message);
            }
            finally
            {
                isStreaming = false;
                
                // 安全地释放CancellationTokenSource
                if (cancellationTokenSource != null)
                {
                    try
                    {
                        cancellationTokenSource.Dispose();
                    }
                    catch (ObjectDisposedException)
                    {
                        // 忽略已释放的异常
                    }
                    cancellationTokenSource = null;
                }
            }
        }
        
        /// <summary>
        /// 停止当前的流式处理
        /// </summary>
        public void StopStreaming()
        {
            if (isStreaming && cancellationTokenSource != null)
            {
                try
                {
                    cancellationTokenSource.Cancel();
                    Debug.Log("正在停止流式处理...");
                }
                catch (ObjectDisposedException)
                {
                    Debug.Log("流式处理已经结束");
                }
            }
        }
        
        /// <summary>
        /// 异步处理流式响应
        /// </summary>
        private async Task ProcessStreamAsync(string message, CancellationToken cancellationToken)
        {
            // 添加超时控制
            var timeoutCts = new CancellationTokenSource(TimeSpan.FromMinutes(5)); // 5分钟总超时
            var combinedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
            
            var lastChunkTime = DateTime.UtcNow;
            var chunkTimeoutSeconds = 60; // 60秒无新chunk超时
            
            try
            {
                await PythonBridge.ProcessMessageStream(
                    message,
                    onChunk: (chunk) => {
                        lastChunkTime = DateTime.UtcNow;
                        try
                        {
                            if (!combinedCts.Token.IsCancellationRequested && !timeoutCts.Token.IsCancellationRequested)
                            {
                                Debug.Log($"[StreamingHandler] 接收到chunk: {chunk}");
                                EnqueueChunk(new StreamChunk { Type = "chunk", Content = chunk });
                            }
                        }
                        catch (ObjectDisposedException)
                        {
                            // CancellationTokenSource已被释放，忽略此回调
                        }
                    },
                    onComplete: () => {
                        try
                        {
                            if (!combinedCts.Token.IsCancellationRequested && !timeoutCts.Token.IsCancellationRequested)
                            {
                                Debug.Log("[StreamingHandler] 接收到complete");
                                EnqueueChunk(new StreamChunk { Type = "complete" });
                            }
                        }
                        catch (ObjectDisposedException)
                        {
                            // CancellationTokenSource已被释放，忽略此回调
                        }
                    },
                    onError: (error) => {
                        try
                        {
                            if (!combinedCts.Token.IsCancellationRequested && !timeoutCts.Token.IsCancellationRequested)
                            {
                                Debug.Log($"[StreamingHandler] 接收到error: {error}");
                                EnqueueChunk(new StreamChunk { Type = "error", Error = error });
                            }
                        }
                        catch (ObjectDisposedException)
                        {
                            // CancellationTokenSource已被释放，忽略此回调
                        }
                    }
                );
                
                // 处理队列中的数据块
                ProcessChunkQueue();
            }
            catch (OperationCanceledException)
            {
                // 检查是否是超时导致的取消
                if (timeoutCts.Token.IsCancellationRequested)
                {
                    var timeSinceLastChunk = DateTime.UtcNow - lastChunkTime;
                    string timeoutMessage;
                    if (timeSinceLastChunk.TotalSeconds > chunkTimeoutSeconds)
                    {
                        timeoutMessage = $"响应超时：超过{chunkTimeoutSeconds}秒无新数据";
                    }
                    else
                    {
                        timeoutMessage = "响应超时：处理时间过长";
                    }
                    
                    EnqueueChunk(new StreamChunk { Type = "error", Error = timeoutMessage });
                    Debug.LogWarning($"流式响应超时: {timeoutMessage}");
                }
                else
                {
                    throw; // 重新抛出用户取消的异常
                }
            }
            finally
            {
                // 安全地释放资源，避免ObjectDisposedException
                try
                {
                    timeoutCts?.Dispose();
                }
                catch (ObjectDisposedException)
                {
                    // 忽略已释放的异常
                }
                
                try
                {
                    combinedCts?.Dispose();
                }
                catch (ObjectDisposedException)
                {
                    // 忽略已释放的异常
                }
            }
        }
        
        /// <summary>
        /// 将数据块加入队列
        /// </summary>
        private void EnqueueChunk(StreamChunk chunk)
        {
            lock (queueLock)
            {
                chunkQueue.Enqueue(chunk);
            }
            
            // 在主线程中处理
            EditorApplication.delayCall += ProcessNextChunk;
        }
        
        /// <summary>
        /// 处理下一个数据块
        /// </summary>
        private void ProcessNextChunk()
        {
            StreamChunk chunk = null;
            
            lock (queueLock)
            {
                if (chunkQueue.Count > 0)
                {
                    chunk = chunkQueue.Dequeue();
                }
            }
            
            if (chunk != null)
            {
                HandleChunk(chunk);
            }
        }
        
        /// <summary>
        /// 处理所有队列中的数据块
        /// </summary>
        private void ProcessChunkQueue()
        {
            while (true)
            {
                StreamChunk chunk = null;
                
                lock (queueLock)
                {
                    if (chunkQueue.Count > 0)
                    {
                        chunk = chunkQueue.Dequeue();
                    }
                    else
                    {
                        break;
                    }
                }
                
                if (chunk != null)
                {
                    EditorApplication.delayCall += () => HandleChunk(chunk);
                }
            }
        }
        
        /// <summary>
        /// 处理单个数据块
        /// </summary>
        private void HandleChunk(StreamChunk chunk)
        {
            Debug.Log($"[StreamingHandler] 处理数据块类型: {chunk.Type}");
            
            switch (chunk.Type)
            {
                case "chunk":
                    Debug.Log($"[StreamingHandler] 触发OnChunkReceived事件，内容: {chunk.Content}");
                    OnChunkReceived?.Invoke(chunk.Content);
                    break;
                    
                case "complete":
                    Debug.Log("[StreamingHandler] 触发OnStreamCompleted事件");
                    OnStreamCompleted?.Invoke();
                    break;
                    
                case "error":
                    Debug.Log($"[StreamingHandler] 触发OnStreamError事件，错误: {chunk.Error}");
                    OnStreamError?.Invoke(chunk.Error);
                    break;
            }
        }
        
        /// <summary>
        /// 清理资源
        /// </summary>
        public void Dispose()
        {
            try
            {
                StopStreaming();
                
                lock (queueLock)
                {
                    chunkQueue.Clear();
                }
                
                OnChunkReceived = null;
                OnStreamStarted = null;
                OnStreamCompleted = null;
                OnStreamError = null;
                OnStreamCancelled = null;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"StreamingHandler清理时出错: {e.Message}");
            }
        }
        
        /// <summary>
        /// 流式数据块结构
        /// </summary>
        private class StreamChunk
        {
            public string Type { get; set; }
            public string Content { get; set; }
            public string Error { get; set; }
        }
    }
    
    /// <summary>
    /// 流式响应管理器，提供全局的流式处理功能
    /// </summary>
    public static class StreamingManager
    {
        private static StreamingHandler currentHandler;
        
        /// <summary>
        /// 创建新的流式处理器
        /// </summary>
        public static StreamingHandler CreateHandler()
        {
            return new StreamingHandler();
        }
        
        /// <summary>
        /// 获取或创建全局流式处理器
        /// </summary>
        public static StreamingHandler GetGlobalHandler()
        {
            if (currentHandler == null)
            {
                currentHandler = new StreamingHandler();
            }
            return currentHandler;
        }
        
        /// <summary>
        /// 停止所有流式处理
        /// </summary>
        public static void StopAllStreaming()
        {
            currentHandler?.StopStreaming();
        }
        
        /// <summary>
        /// 清理全局处理器
        /// </summary>
        public static void Cleanup()
        {
            currentHandler?.Dispose();
            currentHandler = null;
        }
    }
    
    /// <summary>
    /// 流式响应状态
    /// </summary>
    public enum StreamingStatus
    {
        Idle,       // 空闲
        Starting,   // 启动中
        Streaming,  // 流式处理中
        Completed,  // 完成
        Error,      // 错误
        Cancelled   // 已取消
    }
    
    /// <summary>
    /// 流式响应统计信息
    /// </summary>
    public class StreamingStats
    {
        public int TotalChunks { get; set; }
        public int TotalCharacters { get; set; }
        public TimeSpan Duration { get; set; }
        public float ChunksPerSecond => Duration.TotalSeconds > 0 ? TotalChunks / (float)Duration.TotalSeconds : 0;
        public float CharactersPerSecond => Duration.TotalSeconds > 0 ? TotalCharacters / (float)Duration.TotalSeconds : 0;
        
        public override string ToString()
        {
            return $"块数: {TotalChunks}, 字符数: {TotalCharacters}, " +
                   $"耗时: {Duration.TotalSeconds:F1}s, " +
                   $"速度: {CharactersPerSecond:F1} 字符/秒";
        }
    }
}