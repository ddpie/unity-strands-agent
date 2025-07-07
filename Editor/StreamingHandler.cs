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
        private bool isCompleted = false;
        private CancellationTokenSource cancellationTokenSource;
        private Queue<StreamChunk> chunkQueue = new Queue<StreamChunk>();
        private readonly object queueLock = new object();
        
        // 构造函数 - 注册播放模式状态变化监听
        public StreamingHandler()
        {
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }
        
        // 析构函数 - 取消注册
        ~StreamingHandler()
        {
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
        }
        
        // 播放模式状态变化处理
        private void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingEditMode || state == PlayModeStateChange.ExitingPlayMode)
            {
                Debug.LogWarning($"[StreamingHandler] 检测到Unity模式切换: {state}");
                if (isStreaming)
                {
                    Debug.LogWarning("[StreamingHandler] 强制停止流式处理");
                    
                    // 先处理所有队列中的数据，确保已接收的内容不会丢失
                    ProcessAllQueuedChunks();
                    
                    // 发送一个特殊的完成信号，表示因模式切换而中断
                    EnqueueChunk(new StreamChunk { 
                        Type = "interrupted", 
                        Content = $"\n\n⚠️ {LanguageManager.GetText("Unity模式切换，流式处理被中断", "Unity mode switch, streaming interrupted")}" 
                    });
                    
                    // 停止流式处理
                    StopStreaming();
                    
                    // 标记为完成，防止后续数据处理
                    isCompleted = true;
                    OnStreamCompleted?.Invoke();
                }
            }
        }
        
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
            isCompleted = false;
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
                Debug.LogError($"错误堆栈: {e.StackTrace}");
                
                // 发送格式化的错误信息到聊天界面
                var errorMessage = $"\n❌ **Unity流式处理错误**\n\n";
                errorMessage += $"**错误类型**: {e.GetType().Name}\n";
                errorMessage += $"**错误信息**: {e.Message}\n";
                
                OnChunkReceived?.Invoke(errorMessage);
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
            try
            {
                await PythonBridge.ProcessMessageStream(
                    message,
                    onChunk: (chunk) => {
                        try
                        {
                            if (!isCompleted && isStreaming)
                            {
                                Debug.Log($"[StreamingHandler] 接收到chunk: {chunk?.Substring(0, Math.Min(chunk?.Length ?? 0, 100))}...");
                                EnqueueChunk(new StreamChunk { Type = "chunk", Content = chunk });
                            }
                        }
                        catch (ObjectDisposedException)
                        {
                            // CancellationTokenSource已被释放，忽略此回调
                        }
                        catch (Exception ex)
                        {
                            Debug.LogWarning($"[StreamingHandler] 处理chunk时出错: {ex.Message}");
                        }
                    },
                    onComplete: () => {
                        try
                        {
                            if (!isCompleted && isStreaming)
                            {
                                Debug.Log("[StreamingHandler] 接收到complete");
                                EnqueueChunk(new StreamChunk { Type = "complete" });
                            }
                        }
                        catch (ObjectDisposedException)
                        {
                            // CancellationTokenSource已被释放，忽略此回调
                        }
                        catch (Exception ex)
                        {
                            Debug.LogWarning($"[StreamingHandler] 处理complete时出错: {ex.Message}");
                        }
                    },
                    onError: (error) => {
                        try
                        {
                            Debug.Log($"[StreamingHandler] 接收到error: {error}");
                            EnqueueChunk(new StreamChunk { Type = "error", Error = error });
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
                Debug.Log("流式处理被取消");
                throw;
            }
            catch (Exception e)
            {
                Debug.LogError($"流式处理出错: {e.Message}");
                EnqueueChunk(new StreamChunk { Type = "error", Error = e.Message });
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
                Debug.Log($"[StreamingHandler] 入队chunk类型: {chunk.Type}，队列长度: {chunkQueue.Count}");
            }
            
            // 在主线程中处理
            EditorApplication.delayCall += ProcessAllChunks;
        }
        
        /// <summary>
        /// 处理所有待处理的数据块
        /// </summary>
        private void ProcessAllChunks()
        {
            // 一次性处理队列中的所有chunk，确保顺序
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
                    Debug.Log($"[StreamingHandler] 出队处理chunk类型: {chunk.Type}");
                    HandleChunk(chunk);
                }
            }
        }
        
        /// <summary>
        /// 立即处理所有队列中的数据块（用于紧急情况如模式切换）
        /// </summary>
        private void ProcessAllQueuedChunks()
        {
            Debug.Log($"[StreamingHandler] 紧急处理所有队列中的数据块，当前队列长度: {chunkQueue.Count}");
            
            List<StreamChunk> chunksToProcess = new List<StreamChunk>();
            
            // 先取出所有chunk
            lock (queueLock)
            {
                while (chunkQueue.Count > 0)
                {
                    chunksToProcess.Add(chunkQueue.Dequeue());
                }
            }
            
            // 处理所有chunk
            foreach (var chunk in chunksToProcess)
            {
                if (chunk.Type == "chunk" && !string.IsNullOrEmpty(chunk.Content))
                {
                    // 直接触发事件，不经过HandleChunk以避免状态检查
                    OnChunkReceived?.Invoke(chunk.Content);
                }
            }
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
            Debug.Log($"[StreamingHandler] 处理数据块类型: {chunk.Type}，当前完成状态: {isCompleted}");
            
            switch (chunk.Type)
            {
                case "chunk":
                    // 允许chunk通过，由AIAgentWindow决定是否处理
                    Debug.Log($"[StreamingHandler] 触发OnChunkReceived事件，内容: {chunk.Content}");
                    OnChunkReceived?.Invoke(chunk.Content);
                    break;
                    
                case "complete":
                    if (!isCompleted)
                    {
                        Debug.Log("[StreamingHandler] 触发OnStreamCompleted事件");
                        isCompleted = true;
                        OnStreamCompleted?.Invoke();
                    }
                    else
                    {
                        Debug.Log("[StreamingHandler] 忽略重复的complete信号");
                    }
                    break;
                    
                case "error":
                    if (!isCompleted)
                    {
                        Debug.Log($"[StreamingHandler] 触发OnStreamError事件，错误: {chunk.Error}");
                        isCompleted = true;
                        OnStreamError?.Invoke(chunk.Error);
                    }
                    else
                    {
                        Debug.Log($"[StreamingHandler] 忽略完成后的error: {chunk.Error}");
                    }
                    break;
                    
                case "interrupted":
                    // 处理中断信号 - 添加中断提示但不触发错误
                    Debug.Log($"[StreamingHandler] 触发中断信号，内容: {chunk.Content}");
                    if (!string.IsNullOrEmpty(chunk.Content))
                    {
                        OnChunkReceived?.Invoke(chunk.Content);
                    }
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