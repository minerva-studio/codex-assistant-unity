using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Debug = UnityEngine.Debug;

namespace Minerva.Codex
{
    /// <summary>Error returned by app-server for a request, or by the transport.</summary>
    internal sealed class CodexRpcError
    {
        public int Code;
        public string Message;
        public JToken Data;

        public override string ToString()
        {
            return Data == null ? $"{Message} (code {Code})" : $"{Message} (code {Code}): {Data}";
        }
    }

    /// <summary>
    /// JSON-RPC 2.0 over newline-delimited JSON on app-server's stdin/stdout.
    /// Reads on a background thread; every callback is dispatched to the main thread.
    /// </summary>
    internal sealed class CodexConnection : IDisposable
    {
        readonly Process m_Process;
        readonly StreamWriter m_Stdin;
        readonly StreamReader m_Stdout;
        readonly ClientWebSocket m_Socket;
        readonly object m_SendLock = new object();
        readonly Thread m_ReaderThread;
        readonly ConcurrentDictionary<long, Action<JToken, CodexRpcError>> m_Pending =
            new ConcurrentDictionary<long, Action<JToken, CodexRpcError>>();

        long m_LastId;
        int m_Failed;
        volatile bool m_Closed;

        public event Action<JObject> Notification;
        public event Action<JObject> ServerRequest;
        public event Action<string> Disconnected;

        public bool IsOpen =>
            !m_Closed && (m_Socket == null || m_Socket.State == WebSocketState.Open);

        public CodexConnection(ClientWebSocket socket)
        {
            m_Socket = socket;
            m_ReaderThread = new Thread(ReadWebSocketLoop)
            {
                IsBackground = true,
                Name = "Codex app-server websocket reader",
            };
            m_ReaderThread.Start();
        }

        public CodexConnection(Process process)
        {
            m_Process = process;
            m_Stdin = new StreamWriter(process.StandardInput.BaseStream, new UTF8Encoding(false))
            {
                AutoFlush = true,
                NewLine = "\n",
            };
            m_Stdout = new StreamReader(process.StandardOutput.BaseStream, new UTF8Encoding(false));
            m_ReaderThread = new Thread(ReadLoop)
            {
                IsBackground = true,
                Name = "Codex app-server reader",
            };
            m_ReaderThread.Start();
        }

        public void Call(string method, JObject parameters, Action<JToken, CodexRpcError> callback)
        {
            if (m_Closed)
            {
                EditorDispatcher.Enqueue(() => callback?.Invoke(null, TransportError("app-server connection is closed")));
                return;
            }

            long id = Interlocked.Increment(ref m_LastId);
            if (callback != null)
            {
                m_Pending[id] = callback;
            }

            var message = new JObject
            {
                ["id"] = id,
                ["method"] = method,
            };

            if (parameters != null)
            {
                message["params"] = parameters;
            }

            Send(message);
        }

        public void Notify(string method, JObject parameters)
        {
            if (m_Closed)
            {
                return;
            }

            var message = new JObject { ["method"] = method };
            if (parameters != null)
            {
                message["params"] = parameters;
            }

            Send(message);
        }

        public void Respond(JToken id, JToken result)
        {
            Send(new JObject
            {
                ["id"] = id,
                ["result"] = result ?? new JObject(),
            });
        }

        public void RespondError(JToken id, int code, string message)
        {
            Send(new JObject
            {
                ["id"] = id,
                ["error"] = new JObject
                {
                    ["code"] = code,
                    ["message"] = message,
                },
            });
        }

        void Send(JObject message)
        {
            string line = message.ToString(Formatting.None);
            try
            {
                lock (m_SendLock)
                {
                    if (m_Socket != null)
                    {
                        var segment = new ArraySegment<byte>(Encoding.UTF8.GetBytes(line));
                        m_Socket.SendAsync(
                                segment,
                                WebSocketMessageType.Text,
                                true,
                                CancellationToken.None)
                            .GetAwaiter()
                            .GetResult();
                        return;
                    }

                    m_Stdin.WriteLine(line);
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[Codex] Failed to write to app-server: {exception.Message}");
            }
        }

        void ReadLoop()
        {
            try
            {
                string line;
                while ((line = m_Stdout.ReadLine()) != null)
                {
                    HandleLine(line);
                }
            }
            catch (Exception exception)
            {
                if (!m_Closed)
                {
                    Debug.LogWarning($"[Codex] app-server stream failed: {exception.Message}");
                }
            }
            finally
            {
                NotifyClosed("app-server exited");
            }
        }

        void ReadWebSocketLoop()
        {
            var buffer = new byte[64 * 1024];
            var message = new StringBuilder();
            try
            {
                while (!m_Closed)
                {
                    WebSocketReceiveResult result = m_Socket
                        .ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None)
                        .GetAwaiter()
                        .GetResult();

                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        break;
                    }

                    message.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
                    if (!result.EndOfMessage)
                    {
                        continue;
                    }

                    string text = message.ToString();
                    message.Clear();
                    foreach (string line in text.Split('\n'))
                    {
                        HandleLine(line.TrimEnd('\r'));
                    }
                }
            }
            catch (Exception exception)
            {
                if (!m_Closed)
                {
                    Debug.LogWarning($"[Codex] app-server websocket failed: {exception.Message}");
                }
            }
            finally
            {
                NotifyClosed("app-server disconnected");
            }
        }

        void HandleLine(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                return;
            }

            JObject message;
            try
            {
                message = JObject.Parse(line);
            }
            catch (JsonException exception)
            {
                Debug.LogWarning($"[Codex] Ignoring malformed app-server output: {exception.Message}");
                return;
            }

            Route(message);
        }

        void NotifyClosed(string reason)
        {
            if (m_Closed)
            {
                return;
            }

            m_Closed = true;
            FailPending("app-server connection closed");
            Action<string> handler = Disconnected;
            if (handler != null)
            {
                EditorDispatcher.Enqueue(() => handler(reason));
            }
        }

        void Route(JObject message)
        {
            JToken id = message["id"];
            string method = message["method"]?.Value<string>();

            if (id != null && method != null)
            {
                JObject request = message;
                EditorDispatcher.Enqueue(() => ServerRequest?.Invoke(request));
                return;
            }

            if (id != null)
            {
                Complete(id, message);
                return;
            }

            if (method != null)
            {
                JObject notification = message;
                EditorDispatcher.Enqueue(() => Notification?.Invoke(notification));
            }
        }

        void Complete(JToken id, JObject message)
        {
            if (id.Type != JTokenType.Integer || !m_Pending.TryRemove(id.Value<long>(), out var callback))
            {
                return;
            }

            JToken error = message["error"];
            CodexRpcError rpcError = null;
            if (error != null)
            {
                rpcError = new CodexRpcError
                {
                    Code = error["code"]?.Value<int>() ?? 0,
                    Message = error["message"]?.Value<string>() ?? "unknown app-server error",
                    Data = error["data"],
                };
            }

            JToken result = message["result"];
            EditorDispatcher.Enqueue(() => callback(result, rpcError));
        }

        void FailPending(string reason)
        {
            if (Interlocked.Exchange(ref m_Failed, 1) == 1)
            {
                return;
            }

            foreach (var entry in m_Pending)
            {
                if (m_Pending.TryRemove(entry.Key, out var callback))
                {
                    CodexRpcError error = TransportError(reason);
                    EditorDispatcher.Enqueue(() => callback(null, error));
                }
            }
        }

        static CodexRpcError TransportError(string message)
        {
            return new CodexRpcError { Code = 0, Message = message };
        }

        public void Dispose()
        {
            m_Closed = true;

            if (m_Socket != null)
            {
                try
                {
                    if (m_Socket.State == WebSocketState.Open ||
                        m_Socket.State == WebSocketState.CloseReceived)
                    {
                        m_Socket
                            .CloseAsync(
                                WebSocketCloseStatus.NormalClosure,
                                "closing",
                                CancellationToken.None)
                            .Wait(500);
                    }
                }
                catch (Exception)
                {
                    // Already closed.
                }

                try
                {
                    m_Socket.Dispose();
                }
                catch (Exception)
                {
                    // Nothing useful to do on shutdown.
                }
            }

            if (m_Stdin != null)
            {
                try
                {
                    lock (m_SendLock)
                    {
                        m_Stdin.Dispose();
                    }
                }
                catch (Exception)
                {
                    // Already closed.
                }
            }

            if (m_Process != null)
            {
                try
                {
                    if (!m_Process.HasExited)
                    {
                        m_Process.Kill();
                    }
                }
                catch (Exception)
                {
                    // Raced with exit.
                }
            }

            if (m_ReaderThread != null && m_ReaderThread.IsAlive)
            {
                m_ReaderThread.Join(500);
            }

            try
            {
                m_Process?.Dispose();
            }
            catch (Exception)
            {
                // Nothing useful to do on shutdown.
            }
        }
    }
}
