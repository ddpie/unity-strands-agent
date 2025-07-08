using System;
using System.Threading;
using UnityEngine;
using UnityEditor;

namespace UnityAIAgent.Editor
{
    /// <summary>
    /// Unity编辑器线程保护工具类
    /// 提供在Unity模式切换时保护后台线程的机制
    /// </summary>
    public static class ThreadProtection
    {
        private static bool isUnityChangingMode = false;
        
        static ThreadProtection()
        {
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            AssemblyReloadEvents.beforeAssemblyReload += OnBeforeAssemblyReload;
            AssemblyReloadEvents.afterAssemblyReload += OnAfterAssemblyReload;
        }
        
        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            switch (state)
            {
                case PlayModeStateChange.ExitingEditMode:
                case PlayModeStateChange.ExitingPlayMode:
                    isUnityChangingMode = true;
                    break;
                    
                case PlayModeStateChange.EnteredEditMode:
                case PlayModeStateChange.EnteredPlayMode:
                    isUnityChangingMode = false;
                    break;
            }
        }
        
        private static void OnBeforeAssemblyReload()
        {
            isUnityChangingMode = true;
            
            // 通知StreamingManager停止所有流式处理
            try
            {
                StreamingManager.StopAllStreaming();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[ThreadProtection] 停止流式处理时出错: {e.Message}");
            }
        }
        
        private static void OnAfterAssemblyReload()
        {
            isUnityChangingMode = false;
        }
        
        /// <summary>
        /// 检查Unity是否正在切换模式
        /// </summary>
        public static bool IsUnityChangingMode => isUnityChangingMode;
        
        /// <summary>
        /// 在保护模式下运行操作
        /// </summary>
        public static void RunProtected(Action action, Action<Exception> onError = null)
        {
            if (isUnityChangingMode)
            {
                Debug.LogWarning("[ThreadProtection] Unity正在切换模式，操作被阻止");
                onError?.Invoke(new InvalidOperationException("Unity正在切换模式"));
                return;
            }
            
            try
            {
                action();
            }
            catch (ThreadAbortException)
            {
                Thread.ResetAbort();
                Debug.LogWarning("[ThreadProtection] 检测到线程中止，已重置");
                onError?.Invoke(new InvalidOperationException("线程被Unity中止"));
            }
            catch (Exception ex)
            {
                onError?.Invoke(ex);
            }
        }
        
        /// <summary>
        /// 创建一个受保护的后台线程
        /// </summary>
        public static Thread CreateProtectedThread(ThreadStart threadStart)
        {
            var thread = new Thread(() =>
            {
                try
                {
                    threadStart();
                }
                catch (ThreadAbortException)
                {
                    Thread.ResetAbort();
                    Debug.LogWarning("[ThreadProtection] 后台线程被中止，已重置");
                }
            })
            {
                IsBackground = true,
                Name = "Unity AI Protected Thread"
            };
            
            return thread;
        }
        
        /// <summary>
        /// 安全地等待操作完成
        /// </summary>
        public static bool WaitForCompletion(Func<bool> isCompleted, int timeoutMs = 30000)
        {
            var startTime = DateTime.Now;
            
            while (!isCompleted())
            {
                if (isUnityChangingMode)
                {
                    Debug.LogWarning("[ThreadProtection] Unity模式切换中，停止等待");
                    return false;
                }
                
                if ((DateTime.Now - startTime).TotalMilliseconds > timeoutMs)
                {
                    Debug.LogWarning("[ThreadProtection] 等待超时");
                    return false;
                }
                
                Thread.Sleep(100);
            }
            
            return true;
        }
    }
}