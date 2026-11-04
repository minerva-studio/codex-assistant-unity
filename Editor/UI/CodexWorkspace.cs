using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using Newtonsoft.Json.Linq;

namespace Minerva.Codex
{
    internal sealed class CodexThreadInfo
    {
        public string Id;
        public string Preview;
        public string Cwd;
        public long UpdatedAt;
    }

    /// <summary>
    /// Owns the app-server connection and the data shown by the window. The UI
    /// subscribes to <see cref="Changed"/> and renders; it does not call the
    /// protocol itself.
    /// </summary>
    internal sealed class CodexWorkspace : IDisposable
    {
        static readonly string[] SourceKinds = { "cli", "vscode", "exec", "appServer", "unknown" };

        readonly List<CodexThreadInfo> m_Threads = new List<CodexThreadInfo>();
        readonly List<JObject> m_Turns = new List<JObject>();
        readonly StringBuilder m_Stderr = new StringBuilder();

        CodexConnection m_Connection;
        CodexClient m_Client;
        Process m_Process;
        bool m_Ready;

        string m_ProjectRoot;
        string m_ActiveThreadId;
        string m_ActiveTurnId;

        public event Action Changed;

        public string Status { get; private set; } = "Not connected";
        public string Error { get; private set; }
        public IReadOnlyList<CodexThreadInfo> Threads => m_Threads;
        public IReadOnlyList<JObject> Turns => m_Turns;
        public string SelectedThreadId { get; private set; }
        public string SelectedCwd { get; private set; }
        public bool IsConnected => m_Ready;
        public bool ThreadsLoaded { get; private set; }
        public bool IsRunning { get; private set; }
        public bool CanStop =>
            IsRunning && !string.IsNullOrEmpty(m_ActiveThreadId) && !string.IsNullOrEmpty(m_ActiveTurnId);

        /// <summary>Thread to select after the first listing, usually restored from prefs.</summary>
        public string InitialThreadId { get; set; }

        public CodexThreadInfo SelectedThread =>
            string.IsNullOrEmpty(SelectedThreadId) ? null : FindThread(SelectedThreadId);

        public void Connect(string projectRoot)
        {
            if (m_Connection != null && m_Ready)
            {
                return;
            }

            if (m_Connection != null)
            {
                Dispose();
            }

            m_ProjectRoot = projectRoot;
            Status = "Starting codex app-server...";
            Error = null;
            ThreadsLoaded = false;
            Changed?.Invoke();

            try
            {
                CodexLaunchSpec spec = CodexPaths.Resolve();
                m_Process = CodexAppServerProcess.Start(spec, projectRoot, m_Stderr);
                m_Connection = new CodexConnection(m_Process);
                m_Connection.Disconnected += OnDisconnected;
                m_Connection.Notification += OnNotification;
                m_Client = new CodexClient(m_Connection);
                m_Client.Initialize(OnInitialized, OnFailed);
            }
            catch (Exception exception)
            {
                OnFailed(exception.Message);
            }
        }

        public void RefreshThreads()
        {
            if (!m_Ready)
            {
                return;
            }

            Status = "Loading conversations...";
            Error = null;
            Changed?.Invoke();
            m_Client.ListThreads(50, SourceKinds, OnThreadsLoaded, OnFailed);
        }

        public void SelectThread(string threadId)
        {
            if (string.IsNullOrEmpty(threadId))
            {
                return;
            }

            SelectedThreadId = threadId;
            SelectedCwd = FindThread(threadId)?.Cwd;
            m_Turns.Clear();
            Status = "Loading conversation...";
            Changed?.Invoke();

            string requested = threadId;
            m_Client.ListTurns(threadId, 30, turns => OnTurnsLoaded(requested, turns), message =>
            {
                if (SelectedThreadId == requested)
                {
                    OnFailed(message);
                }
            });
        }

        public void ReloadSelected()
        {
            if (!string.IsNullOrEmpty(SelectedThreadId))
            {
                SelectThread(SelectedThreadId);
            }
        }

        /// <summary>
        /// Sends <paramref name="text"/> to the selected thread, starting a thread
        /// first when none is selected. Returns false when the message was not
        /// accepted because the connection is not ready, a turn is already running,
        /// or the text is blank.
        /// </summary>
        public bool SendMessage(string text)
        {
            if (!m_Ready || IsRunning || string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            Error = null;
            IsRunning = true;
            Status = "Sending message...";
            Changed?.Invoke();

            if (string.IsNullOrEmpty(SelectedThreadId))
            {
                StartThread(text);
            }
            else
            {
                StartTurn(SelectedThreadId, text);
            }

            return true;
        }

        /// <summary>
        /// Interrupts the running turn. The composer stays disabled until
        /// app-server reports <c>turn/completed</c>.
        /// </summary>
        public void Stop()
        {
            if (!CanStop)
            {
                return;
            }

            m_Client.InterruptTurn(m_ActiveThreadId, m_ActiveTurnId, () => { }, OnFailed);
            Status = "Stopping...";
            Changed?.Invoke();
        }

        void StartThread(string text)
        {
            Status = "Starting conversation...";
            Changed?.Invoke();
            m_Client.StartThread(m_ProjectRoot, result => OnThreadStarted(result, text), OnSendFailed);
        }

        void OnThreadStarted(JObject result, string text)
        {
            JObject thread = result?["thread"] as JObject;
            string threadId = thread?["id"]?.Value<string>();
            if (string.IsNullOrEmpty(threadId))
            {
                OnSendFailed("thread/start returned no thread id");
                return;
            }

            CodexThreadInfo info = Parse(thread);
            bool hasStoredPreview =
                !string.IsNullOrEmpty(thread["preview"]?.Value<string>()) ||
                !string.IsNullOrEmpty(thread["name"]?.Value<string>()) ||
                !string.IsNullOrEmpty(thread["title"]?.Value<string>());
            if (!hasStoredPreview)
            {
                info.Preview = text;
            }

            // A thread is only persisted once it has a turn, so it is absent from
            // thread/list. Show the thread app-server returned; the next refresh
            // replaces it with the stored version.
            m_Threads.Insert(0, info);
            SelectedThreadId = threadId;
            SelectedCwd = info.Cwd ?? m_ProjectRoot;
            m_Turns.Clear();
            Changed?.Invoke();
            StartTurn(threadId, text);
        }

        void StartTurn(string threadId, string text)
        {
            m_ActiveThreadId = threadId;
            Status = "Starting turn...";
            Changed?.Invoke();

            string requested = threadId;
            m_Client.StartTurn(threadId, text, SelectedCwd, result =>
            {
                if (!IsRunning || SelectedThreadId != requested)
                {
                    return;
                }

                string turnId = result?["turn"]?["id"]?.Value<string>();
                if (!string.IsNullOrEmpty(turnId))
                {
                    m_ActiveTurnId = turnId;
                }

                Status = "Codex is working...";
                Changed?.Invoke();
            }, OnSendFailed);
        }

        void OnNotification(JObject message)
        {
            string method = message["method"]?.Value<string>();
            if (message["params"] is not JObject parameters)
            {
                return;
            }

            switch (method)
            {
                case "turn/started":
                    OnTurnStarted(parameters);
                    break;
                case "turn/completed":
                    OnTurnCompleted(parameters);
                    break;
            }
        }

        void OnTurnStarted(JObject parameters)
        {
            string threadId = parameters["threadId"]?.Value<string>();
            if (threadId != SelectedThreadId)
            {
                return;
            }

            m_ActiveThreadId = threadId;
            string turnId = parameters["turn"]?["id"]?.Value<string>();
            if (!string.IsNullOrEmpty(turnId))
            {
                m_ActiveTurnId = turnId;
            }

            if (!IsRunning)
            {
                IsRunning = true;
                Status = "Codex is working...";
                Changed?.Invoke();
            }
        }

        void OnTurnCompleted(JObject parameters)
        {
            string threadId = parameters["threadId"]?.Value<string>();
            if (threadId != m_ActiveThreadId)
            {
                return;
            }

            FinishTurn(threadId);
        }

        void FinishTurn(string threadId)
        {
            IsRunning = false;
            m_ActiveThreadId = null;
            m_ActiveTurnId = null;

            if (threadId != SelectedThreadId)
            {
                Changed?.Invoke();
                return;
            }

            Status = "Loading conversation...";
            Changed?.Invoke();

            string requested = threadId;
            m_Client.ListTurns(threadId, 30, turns => OnTurnsLoaded(requested, turns), OnFailed);
        }

        void OnInitialized(JObject result)
        {
            m_Ready = true;
            Status = "Connected";
            Error = null;
            Changed?.Invoke();
            RefreshThreads();
        }

        void OnThreadsLoaded(JArray threads)
        {
            m_Threads.Clear();
            foreach (JToken token in threads)
            {
                if (token is JObject thread)
                {
                    m_Threads.Add(Parse(thread));
                }
            }

            ThreadsLoaded = true;
            Status = m_Threads.Count == 1 ? "1 conversation" : $"{m_Threads.Count} conversations";
            Changed?.Invoke();

            bool selectionStillExists =
                !string.IsNullOrEmpty(SelectedThreadId) && FindThread(SelectedThreadId) != null;

            if (selectionStillExists)
            {
                return;
            }

            if (!string.IsNullOrEmpty(InitialThreadId) && FindThread(InitialThreadId) != null)
            {
                SelectThread(InitialThreadId);
                return;
            }

            if (m_Threads.Count > 0)
            {
                SelectThread(m_Threads[0].Id);
                return;
            }

            SelectedThreadId = null;
            SelectedCwd = null;
            m_Turns.Clear();
            Changed?.Invoke();
        }

        void OnTurnsLoaded(string threadId, JArray turns)
        {
            if (SelectedThreadId != threadId)
            {
                return;
            }

            m_Turns.Clear();
            if (turns != null)
            {
                foreach (JToken token in turns)
                {
                    if (token is JObject turn)
                    {
                        m_Turns.Add(turn);
                    }
                }
            }

            Status = m_Turns.Count == 1 ? "1 turn" : $"{m_Turns.Count} turns";
            Changed?.Invoke();
        }

        void OnDisconnected(string reason)
        {
            m_Ready = false;
            IsRunning = false;
            m_ActiveThreadId = null;
            m_ActiveTurnId = null;
            Status = "Disconnected";
            Error = reason;
            Changed?.Invoke();
        }

        void OnSendFailed(string message)
        {
            IsRunning = false;
            m_ActiveThreadId = null;
            m_ActiveTurnId = null;
            OnFailed(message);
        }

        void OnFailed(string message)
        {
            Status = "Error";
            Error = message;

            if (m_Stderr.Length > 0)
            {
                string diagnostics = m_Stderr.ToString().Trim();
                if (diagnostics.Length > 0)
                {
                    Error = $"{message}\n{diagnostics}";
                }
            }

            Changed?.Invoke();
        }

        CodexThreadInfo FindThread(string threadId)
        {
            foreach (CodexThreadInfo thread in m_Threads)
            {
                if (thread.Id == threadId)
                {
                    return thread;
                }
            }

            return null;
        }

        static CodexThreadInfo Parse(JObject thread)
        {
            return new CodexThreadInfo
            {
                Id = thread["id"]?.Value<string>(),
                Preview = thread["preview"]?.Value<string>()
                          ?? thread["name"]?.Value<string>()
                          ?? thread["title"]?.Value<string>()
                          ?? "(untitled)",
                Cwd = thread["cwd"]?.Value<string>(),
                UpdatedAt = thread["updatedAt"]?.Value<long>() ?? 0,
            };
        }

        public void Dispose()
        {
            m_Ready = false;
            ThreadsLoaded = false;
            IsRunning = false;
            m_ActiveThreadId = null;
            m_ActiveTurnId = null;
            m_Connection?.Dispose();
            m_Connection = null;
            m_Client = null;
            m_Process = null;
        }
    }
}
