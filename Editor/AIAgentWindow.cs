using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System;
using System.Threading.Tasks;
using Python.Runtime;

namespace UnityAIAgent.Editor
{
    public class AIAgentWindow : EditorWindow
    {
        private string userInput = "";
        private List<ChatMessage> messages = new List<ChatMessage>();
        private Vector2 scrollPosition;
        private bool isProcessing = false;
        private string currentStreamText = "";
        private GUIStyle userMessageStyle;
        private GUIStyle aiMessageStyle;
        private GUIStyle codeStyle;
        private GUIStyle headerStyle;
        private StreamingHandler streamingHandler;
        private bool autoScroll = true;

        [MenuItem("Window/AI助手/聊天")]
        public static void ShowWindow()
        {
            var window = GetWindow<AIAgentWindow>("AI智能助手");
            window.minSize = new Vector2(450, 600);
        }

        private void OnEnable()
        {
            LoadChatHistory();
            InitializeStyles();
            
            // Ensure Python is initialized
            EditorApplication.delayCall += () => {
                PythonManager.EnsureInitialized();
            };
        }

        private void OnDisable()
        {
            SaveChatHistory();
        }

        private void InitializeStyles()
        {
            // Styles will be initialized in OnGUI when skin is available
        }

        private void OnGUI()
        {
            // Initialize styles if needed
            if (userMessageStyle == null)
            {
                userMessageStyle = new GUIStyle(EditorStyles.wordWrappedLabel);
                userMessageStyle.normal.background = MakeColorTexture(new Color(0.2f, 0.3f, 0.4f, 0.3f));
                userMessageStyle.padding = new RectOffset(10, 10, 10, 10);
                userMessageStyle.margin = new RectOffset(50, 10, 5, 5);

                aiMessageStyle = new GUIStyle(EditorStyles.wordWrappedLabel);
                aiMessageStyle.normal.background = MakeColorTexture(new Color(0.1f, 0.2f, 0.3f, 0.3f));
                aiMessageStyle.padding = new RectOffset(10, 10, 10, 10);
                aiMessageStyle.margin = new RectOffset(10, 50, 5, 5);

                codeStyle = new GUIStyle(EditorStyles.textArea);
                codeStyle.font = Font.CreateDynamicFontFromOSFont("Courier New", 12);
                codeStyle.normal.background = MakeColorTexture(new Color(0.1f, 0.1f, 0.1f, 0.5f));
                codeStyle.padding = new RectOffset(10, 10, 10, 10);
            }

            // Header
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            GUILayout.Label("Unity AI Assistant", EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();
            
            if (GUILayout.Button("Clear", EditorStyles.toolbarButton, GUILayout.Width(50)))
            {
                messages.Clear();
                SaveChatHistory();
            }
            
            if (GUILayout.Button("Logs", EditorStyles.toolbarButton, GUILayout.Width(50)))
            {
                LogWindow.ShowWindow();
            }
            
            EditorGUILayout.EndHorizontal();

            // Chat messages area
            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition, GUILayout.ExpandHeight(true));
            
            foreach (var message in messages)
            {
                DrawMessage(message);
            }

            // Draw current streaming message if active
            if (isStreaming && !string.IsNullOrEmpty(currentStreamText))
            {
                var streamMessage = new ChatMessage
                {
                    content = currentStreamText + "▌",
                    isUser = false,
                    timestamp = DateTime.Now
                };
                DrawMessage(streamMessage);
            }

            EditorGUILayout.EndScrollView();

            // Input area
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            
            GUI.enabled = !isProcessing;
            userInput = EditorGUILayout.TextArea(userInput, GUILayout.MinHeight(60));
            
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            
            if (isStreaming)
            {
                if (GUILayout.Button("Stop", GUILayout.Width(100), GUILayout.Height(30)))
                {
                    StopStreaming();
                }
            }
            else
            {
                GUI.enabled = !isProcessing && !string.IsNullOrWhiteSpace(userInput);
                if (GUILayout.Button("Send", GUILayout.Width(100), GUILayout.Height(30)) || 
                    (Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Return && Event.current.control))
                {
                    SendMessage();
                }
            }
            
            GUI.enabled = true;
            EditorGUILayout.EndHorizontal();
            
            EditorGUILayout.EndVertical();

            // Status bar
            if (isProcessing)
            {
                EditorGUILayout.HelpBox("Processing...", MessageType.Info);
            }
        }

        private void DrawMessage(ChatMessage message)
        {
            var style = message.isUser ? userMessageStyle : aiMessageStyle;
            
            EditorGUILayout.BeginVertical(style);
            
            // Header
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label(message.isUser ? "You" : "AI", EditorStyles.boldLabel, GUILayout.Width(30));
            GUILayout.FlexibleSpace();
            GUILayout.Label(message.timestamp.ToString("HH:mm"), EditorStyles.miniLabel);
            EditorGUILayout.EndHorizontal();
            
            // Content
            if (message.content.Contains("```"))
            {
                // Render code blocks specially
                RenderMarkdownContent(message.content);
            }
            else
            {
                GUILayout.Label(message.content, EditorStyles.wordWrappedLabel);
            }
            
            // Actions
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Copy", EditorStyles.miniButton, GUILayout.Width(40)))
            {
                EditorGUIUtility.systemCopyBuffer = message.content;
            }
            EditorGUILayout.EndHorizontal();
            
            EditorGUILayout.EndVertical();
            GUILayout.Space(5);
        }

        private void RenderMarkdownContent(string content)
        {
            var parts = content.Split(new[] { "```" }, StringSplitOptions.None);
            
            for (int i = 0; i < parts.Length; i++)
            {
                if (i % 2 == 0)
                {
                    // Regular text
                    if (!string.IsNullOrWhiteSpace(parts[i]))
                    {
                        GUILayout.Label(parts[i].Trim(), EditorStyles.wordWrappedLabel);
                    }
                }
                else
                {
                    // Code block
                    var lines = parts[i].Split('\n');
                    var language = lines.Length > 0 ? lines[0].Trim() : "";
                    var code = string.Join("\n", lines, 1, lines.Length - 1);
                    
                    if (!string.IsNullOrWhiteSpace(code))
                    {
                        EditorGUILayout.TextArea(code, codeStyle);
                    }
                }
            }
        }

        private async void SendMessage()
        {
            if (string.IsNullOrWhiteSpace(userInput)) return;

            var message = new ChatMessage
            {
                content = userInput.Trim(),
                isUser = true,
                timestamp = DateTime.Now
            };

            messages.Add(message);
            var currentInput = userInput;
            userInput = "";
            isProcessing = true;
            
            Repaint();

            try
            {
                // Start streaming response
                await ProcessStreamingResponse(currentInput);
            }
            catch (Exception e)
            {
                messages.Add(new ChatMessage
                {
                    content = $"Error: {e.Message}",
                    isUser = false,
                    timestamp = DateTime.Now
                });
                Debug.LogError($"AI Agent Error: {e}");
            }
            finally
            {
                isProcessing = false;
                SaveChatHistory();
                Repaint();
            }
        }

        private async Task ProcessStreamingResponse(string message)
        {
            isStreaming = true;
            currentStreamText = "";

            await Task.Run(() =>
            {
                using (Py.GIL())
                {
                    dynamic streaming = Py.Import("streaming_agent");
                    dynamic asyncio = Py.Import("asyncio");
                    
                    // Get the event loop
                    dynamic loop = asyncio.new_event_loop();
                    asyncio.set_event_loop(loop);
                    
                    try
                    {
                        // Create streaming task
                        dynamic streamTask = streaming.process_message_stream(message);
                        
                        // Process chunks
                        while (true)
                        {
                            try
                            {
                                dynamic chunk = loop.run_until_complete(streamTask.__anext__());
                                string chunkStr = chunk.ToString();
                                var chunkData = JsonUtility.FromJson<StreamChunk>(chunkStr);
                                
                                if (chunkData.type == "chunk")
                                {
                                    currentStreamText += chunkData.content;
                                    EditorApplication.delayCall += Repaint;
                                }
                                else if (chunkData.type == "complete")
                                {
                                    break;
                                }
                                else if (chunkData.type == "error")
                                {
                                    throw new Exception(chunkData.error);
                                }
                            }
                            catch (PyObject stopIteration)
                            {
                                // Stream ended normally
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

            // Add completed message
            if (!string.IsNullOrEmpty(currentStreamText))
            {
                messages.Add(new ChatMessage
                {
                    content = currentStreamText,
                    isUser = false,
                    timestamp = DateTime.Now
                });
            }

            isStreaming = false;
            currentStreamText = "";
        }

        private void StopStreaming()
        {
            isStreaming = false;
            if (!string.IsNullOrEmpty(currentStreamText))
            {
                messages.Add(new ChatMessage
                {
                    content = currentStreamText + " [Stopped]",
                    isUser = false,
                    timestamp = DateTime.Now
                });
                currentStreamText = "";
            }
        }

        private void LoadChatHistory()
        {
            var path = GetChatHistoryPath();
            if (System.IO.File.Exists(path))
            {
                try
                {
                    var json = System.IO.File.ReadAllText(path);
                    var wrapper = JsonUtility.FromJson<ChatHistoryWrapper>(json);
                    messages = wrapper.messages ?? new List<ChatMessage>();
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"Failed to load chat history: {e.Message}");
                }
            }
        }

        private void SaveChatHistory()
        {
            try
            {
                var path = GetChatHistoryPath();
                var dir = System.IO.Path.GetDirectoryName(path);
                if (!System.IO.Directory.Exists(dir))
                {
                    System.IO.Directory.CreateDirectory(dir);
                }

                var wrapper = new ChatHistoryWrapper { messages = messages };
                var json = JsonUtility.ToJson(wrapper, true);
                System.IO.File.WriteAllText(path, json);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"Failed to save chat history: {e.Message}");
            }
        }

        private string GetChatHistoryPath()
        {
            var documentsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            return System.IO.Path.Combine(documentsPath, "UnityAIAgent", "chat_history.json");
        }

        private Texture2D MakeColorTexture(Color color)
        {
            var texture = new Texture2D(1, 1);
            texture.SetPixel(0, 0, color);
            texture.Apply();
            return texture;
        }

        [Serializable]
        private class ChatMessage
        {
            public string content;
            public bool isUser;
            public DateTime timestamp;
        }

        [Serializable]
        private class ChatHistoryWrapper
        {
            public List<ChatMessage> messages;
        }

    }
}