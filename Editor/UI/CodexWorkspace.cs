using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.WebSockets;
using System.Text;
using Newtonsoft.Json.Linq;
using UnityEditor;
using Debug = UnityEngine.Debug;

namespace Minerva.Codex
{
    internal sealed class CodexThreadInfo
    {
        public string Id;
        public string Title;
        public string Preview;
        public string Snippet;
        public string Cwd;
        public string ParentThreadId;
        public long UpdatedAt;
    }

    internal readonly struct CodexContextUsage
    {
        public readonly long UsedTokens;
        public readonly long WindowTokens;

        public CodexContextUsage(long usedTokens, long windowTokens)
        {
            UsedTokens = usedTokens;
            WindowTokens = windowTokens;
        }

        public bool HasWindow => WindowTokens > 0;

        public double Fraction =>
            WindowTokens > 0 ? Math.Min(1.0, (double)UsedTokens / WindowTokens) : 0.0;
    }

    internal sealed class CodexQueuedMessage
    {
        public string Id;
        public string Text;
    }

    internal sealed class CodexSubagentInfo
    {
        public string ThreadId;
        public string Path;
        public string Status;
        public string Activity;
        public string Model;
        public string Effort;
        public bool Live;

        public bool IsRunning => Status == "running" || Status == "pending";
    }

    internal sealed class CodexModelInfo
    {
        public string Id;
        public string CatalogId;
        public string DisplayName;
        public string DefaultEffort;
        public bool Hidden;
        public bool IsDefault;
        public readonly List<string> Efforts = new List<string>();

        public bool Matches(string value)
        {
            return value == Id || value == CatalogId;
        }
    }

    /// <summary>
    /// Owns the app-server connection and the data shown by the window. The UI
    /// subscribes to <see cref="Changed"/> and renders; it does not call the
    /// protocol itself.
    /// </summary>
    internal sealed class CodexWorkspace : IDisposable
    {
        const string LocalUserItemId = "local-user";
        const double StreamIntervalSeconds = 0.05;
        const double SearchDebounceSeconds = 0.25;
        // Subagents are opened from their parent item and must not become
        // separate conversations in the sidebar.
        static readonly string[] SourceKinds = { "cli", "vscode", "exec", "appServer", "unknown" };
        static readonly string[] s_SandboxModes = { "read-only", "workspace-write", "danger-full-access" };

        readonly List<CodexThreadInfo> m_Threads = new List<CodexThreadInfo>();
        readonly List<JObject> m_Turns = new List<JObject>();
        readonly List<CodexModelInfo> m_Models = new List<CodexModelInfo>();
        readonly List<string> m_Efforts = new List<string>();
        readonly List<CodexQueuedMessage> m_Queued = new List<CodexQueuedMessage>();
        readonly Dictionary<string, CodexContextUsage> m_ContextUsage =
            new Dictionary<string, CodexContextUsage>();
        readonly Dictionary<string, JArray> m_ReasoningSummaries =
            new Dictionary<string, JArray>();
        readonly HashSet<string> m_ArchivedThreadIds = new HashSet<string>();
        readonly List<CodexQuestionRequest> m_QuestionRequests = new List<CodexQuestionRequest>();
        string m_ArchiveVerifyingThreadId;
        readonly StringBuilder m_Stderr = new StringBuilder();

        CodexConnection m_Connection;
        CodexClient m_Client;
        Process m_Process;
        bool m_Ready;

        string m_ProjectRoot;
        string m_CodexHome;
        string m_ActiveThreadId;
        string m_ActiveTurnId;
        JObject m_LiveTurn;
        string m_LiveThreadId;
        int m_TurnGeneration;

        string m_SearchTerm = string.Empty;
        CodexThreadInfo m_SelectedThreadInfo;
        bool m_ShowAllProjects;
        bool m_InitialSelectionDone;
        int m_ThreadListGeneration;
        bool m_SearchDebounceSubscribed;
        bool m_SearchPending;
        double m_SearchDeadline;
        long m_ConfigContextWindow;

        bool m_ConfigLoaded;
        bool m_SettingsFromThread;
        string m_SettingsError;
        string m_SettingsSource = "config";
        JToken m_ApprovalPolicy;
        string m_EffortSyncedThreadId;

        int m_QueueGeneration;

        string m_AutoTitleThreadId;
        bool m_TitleGenerating;
        string m_TitleThreadId;
        string m_TitleTargetThreadId;
        string m_TitleModel;
        readonly StringBuilder m_TitleText = new StringBuilder();
        double m_TitleDeadline;
        bool m_TitleTimeoutSubscribed;

        bool m_StreamSubscribed;
        bool m_StreamDirty;
        double m_NextStreamFlush;

        public event Action Changed;
        public event Action TranscriptChanged;
        public event Action SettingsChanged;
        public event Action ContextChanged;

        public string Status { get; private set; } = "Not connected";
        public string Error { get; private set; }
        public IReadOnlyList<CodexThreadInfo> Threads => m_Threads;
        public IReadOnlyList<JObject> Turns => m_Turns;
        public string SearchTerm => m_SearchTerm;
        public bool ShowAllProjects => m_ShowAllProjects;
        public bool IsSearching => !string.IsNullOrWhiteSpace(m_SearchTerm);
        public string SelectedThreadId { get; private set; }
        public string SelectedCwd { get; private set; }
        public bool IsConnected => m_Ready;
        public bool ThreadsLoaded { get; private set; }
        public bool IsRunning { get; private set; }
        public bool IsForking { get; private set; }
        public bool CanStop =>
            IsRunning && !string.IsNullOrEmpty(m_ActiveThreadId) && !string.IsNullOrEmpty(m_ActiveTurnId);

        public IReadOnlyList<CodexModelInfo> Models => m_Models;
        public IReadOnlyList<string> Efforts => m_Efforts;
        public IReadOnlyList<CodexQueuedMessage> QueuedMessages => m_Queued;

        /// <summary>Subagents referenced by the selected conversation's turns.</summary>
        public IReadOnlyList<CodexSubagentInfo> Subagents => BuildSubagents();

        public CodexQuestionRequest PendingQuestion
        {
            get
            {
                foreach (CodexQuestionRequest request in m_QuestionRequests)
                {
                    if (request.CurrentQuestion != null)
                    {
                        return request;
                    }
                }

                return null;
            }
        }

        public bool HasPendingQuestion => PendingQuestion != null;
        public IReadOnlyList<string> SandboxModes => s_SandboxModes;
        public bool SettingsLoaded => m_ConfigLoaded;
        public string SettingsError => m_SettingsError;
        public string SettingsSource => m_SettingsSource;
        public string SelectedModel { get; private set; }
        public string SelectedEffort { get; private set; }
        public string SelectedSandbox { get; private set; }
        public string ApprovalDisplay => FormatApproval(m_ApprovalPolicy);

        /// <summary>The turn being streamed, when it belongs to the selected thread.</summary>
        public JObject LiveTurn =>
            m_LiveTurn != null && m_LiveThreadId == SelectedThreadId ? m_LiveTurn : null;

        /// <summary>Thread to select after the first listing, usually restored from prefs.</summary>
        public string InitialThreadId { get; set; }

        public CodexThreadInfo SelectedThread
        {
            get
            {
                if (string.IsNullOrEmpty(SelectedThreadId))
                {
                    return null;
                }

                return FindThread(SelectedThreadId) ??
                       (m_SelectedThreadInfo != null && m_SelectedThreadInfo.Id == SelectedThreadId
                           ? m_SelectedThreadInfo
                           : null);
            }
        }

        public CodexContextUsage? ContextUsage
        {
            get
            {
                if (string.IsNullOrEmpty(SelectedThreadId))
                {
                    return null;
                }

                return m_ContextUsage.TryGetValue(SelectedThreadId, out CodexContextUsage usage)
                    ? usage
                    : (CodexContextUsage?)null;
            }
        }

        public void Connect(string projectRoot, string codexHome = null, bool restartServer = false)
        {
            string requestedHome = string.IsNullOrWhiteSpace(codexHome) ? null : codexHome.Trim();
            bool homeChanged = !string.Equals(m_CodexHome, requestedHome, StringComparison.OrdinalIgnoreCase);

            if (m_Connection != null && m_Ready && !homeChanged && !restartServer)
            {
                return;
            }

            if (m_Connection != null)
            {
                Dispose();
            }

            if (homeChanged)
            {
                ClearSessionForHomeChange();
            }

            m_CodexHome = requestedHome;
            m_ProjectRoot = projectRoot;
            Status = "Starting codex app-server...";
            Error = null;
            ThreadsLoaded = false;
            m_SearchTerm = string.Empty;
            m_ShowAllProjects = false;
            m_InitialSelectionDone = false;
            m_ThreadListGeneration++;
            CancelSearchDebounce();
            m_ContextUsage.Clear();
            m_ConfigContextWindow = 0;
            m_ConfigLoaded = false;
            m_SettingsFromThread = false;
            m_SettingsError = null;
            m_SettingsSource = "config";
            m_ApprovalPolicy = null;
            SelectedModel = null;
            SelectedEffort = null;
            SelectedSandbox = null;
            m_Models.Clear();
            m_Efforts.Clear();
            m_Queued.Clear();
            m_QueueGeneration++;
            m_EffortSyncedThreadId = null;
            Changed?.Invoke();
            SettingsChanged?.Invoke();

            try
            {
                CodexLaunchSpec spec = CodexPaths.Resolve();
                if (CodexWebSocketHost.TryConnect(
                        spec,
                        projectRoot,
                        m_CodexHome,
                        restartServer,
                        m_Stderr,
                        out ClientWebSocket socket))
                {
                    m_Connection = new CodexConnection(socket);
                }
                else
                {
                    m_Process = CodexAppServerProcess.Start(spec, projectRoot, m_CodexHome, m_Stderr);
                    m_Connection = new CodexConnection(m_Process);
                }

                m_Connection.Disconnected += OnDisconnected;
                m_Connection.Notification += OnNotification;
                m_Connection.ServerRequest += OnServerRequest;
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
            CancelSearchDebounce();
            RequestThreads();
        }

        public void SetSearchTerm(string term)
        {
            term ??= string.Empty;
            if (term == m_SearchTerm)
            {
                return;
            }

            m_SearchTerm = term;
            m_ThreadListGeneration++;
            m_SearchPending = true;
            m_SearchDeadline = EditorApplication.timeSinceStartup + SearchDebounceSeconds;
            if (!m_SearchDebounceSubscribed)
            {
                m_SearchDebounceSubscribed = true;
                EditorApplication.update += FlushSearch;
            }
        }

        public void SetShowAllProjects(bool showAll)
        {
            if (m_ShowAllProjects == showAll)
            {
                return;
            }

            m_ShowAllProjects = showAll;
            m_ThreadListGeneration++;
            CancelSearchDebounce();
            RequestThreads();
        }

        public bool ArchiveSelectedThread()
        {
            if (!m_Ready || IsRunning || string.IsNullOrEmpty(SelectedThreadId))
            {
                return false;
            }

            string threadId = SelectedThreadId;
            Status = "Archiving conversation...";
            Changed?.Invoke();

            // A loaded thread cannot be archived persistently; unload it first.
            m_Client.UnsubscribeThread(
                threadId,
                () => ArchiveThread(threadId),
                _ => ArchiveThread(threadId));
            return true;
        }

        void ArchiveThread(string threadId)
        {
            CodexThreadInfo info = FindThread(threadId);
            if (info == null && m_SelectedThreadInfo?.Id == threadId)
            {
                info = m_SelectedThreadInfo;
            }

            string cwd = info?.Cwd;
            m_ArchiveVerifyingThreadId = threadId;
            m_Client.ArchiveThread(
                threadId,
                () => VerifyArchive(threadId, cwd),
                message =>
                {
                    m_ArchiveVerifyingThreadId = null;
                    OnFailed(message);
                });
        }

        void VerifyArchive(string threadId, string cwd)
        {
            var query = new CodexThreadQuery
            {
                Limit = 100,
                Cwd = cwd,
                SourceKinds = SourceKinds,
                IncludeArchived = false,
            };

            m_Client.ListThreads(
                query,
                threads =>
                {
                    bool stillListed = false;
                    if (threads != null)
                    {
                        foreach (JToken token in threads)
                        {
                            if (token?["id"]?.Value<string>() == threadId)
                            {
                                stillListed = true;
                                break;
                            }
                        }
                    }

                    m_ArchiveVerifyingThreadId = null;
                    if (stillListed)
                    {
                        m_ArchivedThreadIds.Remove(threadId);
                        Status = "Archive not persisted";
                        Error =
                            "Codex still lists this conversation after thread/archive. Close it in Codex " +
                            "Desktop or the CLI, then archive again; a loaded thread cannot be archived " +
                            "by another app-server.";
                        Changed?.Invoke();
                        return;
                    }

                    m_ArchivedThreadIds.Add(threadId);
                    OnThreadRemoved(threadId, "Conversation archived");
                    // Invalidate any list request that started before the archive,
                    // then reload so an archived thread cannot reappear from stale data.
                    m_ThreadListGeneration++;
                    RequestThreads();
                },
                message =>
                {
                    // The archive RPC succeeded; verification itself failed. Keep the
                    // conversation out of the way but say that we could not confirm it.
                    m_ArchiveVerifyingThreadId = null;
                    m_ArchivedThreadIds.Add(threadId);
                    OnThreadRemoved(threadId, "Conversation archived");
                    Error = $"Archived, but the thread list could not be verified: {message}";
                    Changed?.Invoke();
                    m_ThreadListGeneration++;
                    RequestThreads();
                });
        }

        void RequestThreads()
        {
            if (!m_Ready)
            {
                return;
            }

            int generation = ++m_ThreadListGeneration;
            bool searching = IsSearching;
            Status = searching ? "Searching conversations..." : "Loading conversations...";
            Error = null;
            Changed?.Invoke();

            var query = new CodexThreadQuery
            {
                Limit = searching ? 100 : 50,
                SourceKinds = SourceKinds,
                Cwd = m_ShowAllProjects ? null : m_ProjectRoot,
                SearchTerm = m_SearchTerm.Trim(),
                IncludeArchived = false,
            };

            if (searching)
            {
                m_Client.SearchThreads(
                    query,
                    results => OnThreadsLoaded(results, generation, true),
                    message =>
                    {
                        if (generation == m_ThreadListGeneration)
                        {
                            OnFailed(message);
                        }
                    });
            }
            else
            {
                m_Client.ListThreads(
                    query,
                    results => OnThreadsLoaded(results, generation, false),
                    message =>
                    {
                        if (generation == m_ThreadListGeneration)
                        {
                            OnFailed(message);
                        }
                    });
            }
        }

        void FlushSearch()
        {
            if (!m_SearchPending)
            {
                CancelSearchDebounce();
                return;
            }

            if (EditorApplication.timeSinceStartup < m_SearchDeadline)
            {
                return;
            }

            m_SearchPending = false;
            CancelSearchDebounce();
            RequestThreads();
        }

        void CancelSearchDebounce()
        {
            if (m_SearchDebounceSubscribed)
            {
                EditorApplication.update -= FlushSearch;
                m_SearchDebounceSubscribed = false;
            }

            m_SearchPending = false;
        }

        public void SelectThread(string threadId)
        {
            if (string.IsNullOrEmpty(threadId))
            {
                return;
            }

            SelectedThreadId = threadId;
            m_SelectedThreadInfo = FindThread(threadId);
            SelectedCwd = m_SelectedThreadInfo?.Cwd;
            m_Turns.Clear();
            m_EffortSyncedThreadId = null;
            m_Queued.Clear();
            m_QueueGeneration++;
            Status = "Loading conversation...";
            Changed?.Invoke();
            ContextChanged?.Invoke();

            string requested = threadId;
            m_Client.ResumeThread(
                threadId,
                true,
                result => OnThreadResumed(requested, result),
                message => OnThreadResumeFailed(requested, message));
        }

        public void ReloadSelected()
        {
            if (!string.IsNullOrEmpty(SelectedThreadId))
            {
                SelectThread(SelectedThreadId);
            }
        }

        /// <summary>
        /// Clears the selection so the composer starts a fresh thread on the next send.
        /// No thread is created until the first message.
        /// </summary>
        public void StartNewConversation()
        {
            if (!m_Ready || IsRunning)
            {
                return;
            }

            SelectedThreadId = null;
            m_SelectedThreadInfo = null;
            SelectedCwd = null;
            m_Turns.Clear();
            m_LiveTurn = null;
            m_LiveThreadId = null;
            m_ActiveThreadId = null;
            m_ActiveTurnId = null;
            m_AutoTitleThreadId = null;
            m_EffortSyncedThreadId = null;
            m_Queued.Clear();
            m_QueueGeneration++;
            m_InitialSelectionDone = true;
            Error = null;
            Status = "New conversation";

            if (m_SettingsFromThread)
            {
                m_SettingsFromThread = false;
                m_SettingsSource = "custom";
            }

            Changed?.Invoke();
            ContextChanged?.Invoke();
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

            text = text.Trim();
            Error = null;
            IsRunning = true;
            m_TurnGeneration++;
            m_ActiveThreadId = SelectedThreadId;
            m_ActiveTurnId = null;
            m_LiveThreadId = SelectedThreadId;
            m_LiveTurn = NewLiveTurn(text);
            Status = string.IsNullOrEmpty(SelectedThreadId) ? "Starting conversation..." : "Sending message...";
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
        /// Adds <paramref name="text"/> to the active turn without interrupting it.
        /// Returns false when there is no steerable turn; the caller can queue instead.
        /// </summary>
        public bool SteerMessage(string text)
        {
            if (!m_Ready || !IsRunning || string.IsNullOrWhiteSpace(text) ||
                string.IsNullOrEmpty(SelectedThreadId) || string.IsNullOrEmpty(m_ActiveTurnId))
            {
                return false;
            }

            string threadId = SelectedThreadId;
            string turnId = m_ActiveTurnId;
            string trimmed = text.Trim();
            Error = null;
            Status = "Steering...";
            Changed?.Invoke();

            m_Client.SteerTurn(
                threadId,
                turnId,
                trimmed,
                result =>
                {
                    if (SelectedThreadId != threadId)
                    {
                        return;
                    }

                    string returnedTurnId = result?["turnId"]?.Value<string>();
                    if (!string.IsNullOrEmpty(returnedTurnId))
                    {
                        m_ActiveTurnId = returnedTurnId;
                    }

                    Status = "Codex is working...";
                    Changed?.Invoke();
                },
                message =>
                {
                    if (SelectedThreadId != threadId)
                    {
                        return;
                    }

                    Debug.LogWarning($"[Codex] turn/steer failed, queueing instead: {message}");
                    if (!QueueMessage(trimmed))
                    {
                        Error = $"Steering failed: {message}";
                        Status = "Steering failed";
                        Changed?.Invoke();
                    }
                });

            return true;
        }

        /// <summary>
        /// Persists a future turn. app-server owns the queue; the next queued item
        /// starts when this thread becomes idle.
        /// </summary>
        public bool QueueMessage(string text)
        {
            if (!m_Ready || string.IsNullOrWhiteSpace(text) || string.IsNullOrEmpty(SelectedThreadId))
            {
                return false;
            }

            string threadId = SelectedThreadId;
            string trimmed = text.Trim();
            Error = null;

            m_Client.AddQueuedTurn(
                threadId,
                Guid.NewGuid().ToString("N"),
                trimmed,
                _ =>
                {
                    if (SelectedThreadId != threadId)
                    {
                        return;
                    }

                    Status = IsRunning ? "Queued for after this turn" : "Starting queued message...";
                    Changed?.Invoke();
                    LoadQueue(threadId);
                },
                message =>
                {
                    if (SelectedThreadId != threadId)
                    {
                        return;
                    }

                    Error = $"Could not queue message: {message}";
                    Status = "Queue failed";
                    Changed?.Invoke();
                });

            return true;
        }

        public void RemoveQueuedMessage(string queuedId)
        {
            if (string.IsNullOrEmpty(queuedId) || string.IsNullOrEmpty(SelectedThreadId))
            {
                return;
            }

            string threadId = SelectedThreadId;
            m_Queued.RemoveAll(message => message.Id == queuedId);
            Changed?.Invoke();
            m_Client.DeleteQueuedTurn(
                threadId,
                queuedId,
                () => LoadQueue(threadId),
                message =>
                {
                    if (SelectedThreadId != threadId)
                    {
                        return;
                    }

                    Error = $"Could not remove queued message: {message}";
                    LoadQueue(threadId);
                    Changed?.Invoke();
                });
        }

        void LoadQueue(string threadId)
        {
            if (!m_Ready || m_Client == null || string.IsNullOrEmpty(threadId))
            {
                return;
            }

            int generation = ++m_QueueGeneration;
            m_Client.ListQueuedTurns(
                threadId,
                100,
                data =>
                {
                    if (generation != m_QueueGeneration || SelectedThreadId != threadId)
                    {
                        return;
                    }

                    m_Queued.Clear();
                    if (data != null)
                    {
                        foreach (JToken token in data)
                        {
                            if (token is not JObject entry)
                            {
                                continue;
                            }

                            string id = entry["id"]?.Value<string>();
                            string text = QueuedText(entry["input"]);
                            if (!string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(text))
                            {
                                m_Queued.Add(new CodexQueuedMessage { Id = id, Text = text });
                            }
                        }
                    }

                    Changed?.Invoke();
                },
                message =>
                {
                    if (generation != m_QueueGeneration || SelectedThreadId != threadId)
                    {
                        return;
                    }

                    m_Queued.Clear();
                    Debug.LogWarning($"[Codex] thread/queue/list failed: {message}");
                    Changed?.Invoke();
                });
        }

        static string QueuedText(JToken input)
        {
            if (input is not JArray items)
            {
                return string.Empty;
            }

            var parts = new List<string>();
            foreach (JToken item in items)
            {
                if (item?["type"]?.Value<string>() == "text")
                {
                    string text = item["text"]?.Value<string>();
                    if (!string.IsNullOrEmpty(text))
                    {
                        parts.Add(text);
                    }
                }
            }

            return string.Join("\n", parts);
        }

        /// <summary>
        /// Interrupts the running turn. The composer remains available for
        /// queueing or steering until <c>turn/completed</c> arrives.
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

        public void SetModel(string model)
        {
            if (string.IsNullOrEmpty(model) || model == SelectedModel)
            {
                return;
            }

            SelectedModel = model;
            CodexModelInfo info = FindModel(model);
            if (info != null && !string.IsNullOrEmpty(info.DefaultEffort))
            {
                SelectedEffort = info.DefaultEffort;
            }

            m_SettingsSource = "custom";
            m_EffortSyncedThreadId = null;
            RebuildEfforts();
            SettingsChanged?.Invoke();
            ApplyEffortToThread();
        }

        public void SetEffort(string effort)
        {
            if (string.IsNullOrEmpty(effort) || effort == SelectedEffort)
            {
                return;
            }

            SelectedEffort = effort;
            m_SettingsSource = "custom";
            m_EffortSyncedThreadId = null;
            SettingsChanged?.Invoke();
            ApplyEffortToThread();
        }

        /// <summary>
        /// Uses the thread-settings API for a change that should apply to later
        /// turns. The next turn can then omit the effort override, which keeps
        /// Codex's prompt cache path stable.
        /// </summary>
        void ApplyEffortToThread()
        {
            if (!m_Ready || m_Client == null || string.IsNullOrEmpty(SelectedThreadId) ||
                string.IsNullOrEmpty(SelectedEffort))
            {
                return;
            }

            string threadId = SelectedThreadId;
            string effort = SelectedEffort;
            m_Client.UpdateThreadSettings(
                threadId,
                effort,
                _ =>
                {
                    if (SelectedThreadId == threadId && SelectedEffort == effort)
                    {
                        m_EffortSyncedThreadId = threadId;
                    }
                },
                message =>
                {
                    if (SelectedThreadId != threadId)
                    {
                        return;
                    }

                    m_EffortSyncedThreadId = null;
                    m_SettingsError = "Reasoning effort will apply on the next turn.";
                    Debug.LogWarning($"[Codex] thread/settings/update failed: {message}");
                    SettingsChanged?.Invoke();
                });
        }

        public void SetSandbox(string sandbox)
        {
            string requested = string.IsNullOrEmpty(sandbox) ? null : sandbox;
            if (requested == SelectedSandbox)
            {
                return;
            }

            SelectedSandbox = requested;
            m_SettingsSource = "custom";
            SettingsChanged?.Invoke();
        }

        void LoadSettings()
        {
            m_Client.ReadConfig(m_ProjectRoot, OnConfigLoaded, OnSettingsFailed);
            m_Client.ListModels(200, OnModelsLoaded, OnSettingsFailed);
        }

        void OnConfigLoaded(JObject config)
        {
            m_ConfigLoaded = true;
            m_SettingsError = null;
            m_ConfigContextWindow = ReadLong(config?["model_context_window"]);

            if (!m_SettingsFromThread)
            {
                string model = config?["model"]?.Value<string>();
                if (!string.IsNullOrEmpty(model))
                {
                    SelectedModel = model;
                }

                string effort = config?["model_reasoning_effort"]?.Value<string>();
                if (!string.IsNullOrEmpty(effort))
                {
                    SelectedEffort = effort;
                }

                string sandbox = config?["sandbox_mode"]?.Value<string>();
                if (!string.IsNullOrEmpty(sandbox))
                {
                    SelectedSandbox = sandbox;
                }

                JToken approval = config?["approval_policy"];
                if (approval != null && approval.Type != JTokenType.Null)
                {
                    m_ApprovalPolicy = approval.DeepClone();
                }

                m_SettingsSource = "config";
            }

            NormalizeSettings();
            SettingsChanged?.Invoke();
            ContextChanged?.Invoke();
            Changed?.Invoke();
        }

        void OnModelsLoaded(JArray models)
        {
            m_Models.Clear();
            if (models != null)
            {
                foreach (JToken token in models)
                {
                    if (token is not JObject model)
                    {
                        continue;
                    }

                    var info = new CodexModelInfo
                    {
                        Id = model["model"]?.Value<string>() ?? model["id"]?.Value<string>(),
                        CatalogId = model["id"]?.Value<string>(),
                        DisplayName = model["displayName"]?.Value<string>(),
                        DefaultEffort = model["defaultReasoningEffort"]?.Value<string>(),
                        Hidden = model["hidden"]?.Value<bool>() ?? false,
                        IsDefault = model["isDefault"]?.Value<bool>() ?? false,
                    };

                    if (string.IsNullOrEmpty(info.DisplayName))
                    {
                        info.DisplayName = info.Id;
                    }

                    if (model["supportedReasoningEfforts"] is JArray efforts)
                    {
                        foreach (JToken effort in efforts)
                        {
                            string value = effort["reasoningEffort"]?.Value<string>();
                            if (!string.IsNullOrEmpty(value) && !info.Efforts.Contains(value))
                            {
                                info.Efforts.Add(value);
                            }
                        }
                    }

                    m_Models.Add(info);
                }
            }

            NormalizeSettings();
            SettingsChanged?.Invoke();
        }

        void OnSettingsFailed(string message)
        {
            m_SettingsError = message;
            m_ConfigLoaded = true;
            Debug.LogWarning($"[Codex] Could not load model settings: {message}");
            SettingsChanged?.Invoke();
            Changed?.Invoke();
        }

        void NormalizeSettings()
        {
            if (string.IsNullOrEmpty(SelectedModel))
            {
                CodexModelInfo defaultModel = m_Models.Find(model => model.IsDefault && !model.Hidden)
                                             ?? m_Models.Find(model => !model.Hidden);
                if (defaultModel != null)
                {
                    SelectedModel = defaultModel.Id;
                }
            }

            RebuildEfforts();

            CodexModelInfo selected = FindModel(SelectedModel);
            if (selected != null &&
                m_Efforts.Count > 0 &&
                (string.IsNullOrEmpty(SelectedEffort) || !m_Efforts.Contains(SelectedEffort)))
            {
                SelectedEffort =
                    !string.IsNullOrEmpty(selected.DefaultEffort) && m_Efforts.Contains(selected.DefaultEffort)
                        ? selected.DefaultEffort
                        : m_Efforts[0];
            }
            else if (string.IsNullOrEmpty(SelectedEffort) && m_Efforts.Count > 0)
            {
                SelectedEffort = m_Efforts[0];
            }
        }

        void RebuildEfforts()
        {
            m_Efforts.Clear();
            CodexModelInfo model = FindModel(SelectedModel);
            if (model != null)
            {
                foreach (string effort in model.Efforts)
                {
                    if (!m_Efforts.Contains(effort))
                    {
                        m_Efforts.Add(effort);
                    }
                }

                if (m_Efforts.Count == 0 && !string.IsNullOrEmpty(model.DefaultEffort))
                {
                    m_Efforts.Add(model.DefaultEffort);
                }
            }

            if (!string.IsNullOrEmpty(SelectedEffort) && !m_Efforts.Contains(SelectedEffort))
            {
                m_Efforts.Add(SelectedEffort);
            }
        }

        CodexModelInfo FindModel(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                return null;
            }

            return m_Models.Find(model => model.Matches(id));
        }

        void OnThreadResumed(string threadId, JObject result)
        {
            if (SelectedThreadId != threadId)
            {
                return;
            }

            if (result?["thread"] is JObject thread)
            {
                m_SelectedThreadInfo = Parse(thread);
            }

            ApplyThreadSettings(threadId, result);
            LoadTurns(threadId);
        }

        void OnThreadResumeFailed(string threadId, string message)
        {
            if (SelectedThreadId != threadId)
            {
                return;
            }

            Debug.LogWarning($"[Codex] thread/resume failed for {threadId}: {message}");
            LoadTurns(threadId);
        }

        void ApplyThreadSettings(string threadId, JObject resume)
        {
            if (resume == null)
            {
                return;
            }

            string model = resume["model"]?.Value<string>();
            if (!string.IsNullOrEmpty(model))
            {
                SelectedModel = model;
            }

            string effort = resume["reasoningEffort"]?.Value<string>();
            if (!string.IsNullOrEmpty(effort))
            {
                SelectedEffort = effort;
                m_EffortSyncedThreadId = threadId;
            }

            string sandbox = MapSandboxPolicy(resume["sandbox"]);
            if (!string.IsNullOrEmpty(sandbox))
            {
                SelectedSandbox = sandbox;
            }

            JToken approval = resume["approvalPolicy"];
            if (approval != null && approval.Type != JTokenType.Null)
            {
                m_ApprovalPolicy = approval.DeepClone();
            }

            m_SettingsFromThread = true;
            m_SettingsSource = "thread";
            NormalizeSettings();
            SettingsChanged?.Invoke();
        }

        void LoadTurns(string threadId)
        {
            int generation = m_TurnGeneration;
            m_Client.ListTurns(
                threadId,
                30,
                turns => OnTurnsLoaded(threadId, turns, generation),
                OnFailed);
        }

        void StartThread(string text)
        {
            m_Client.StartThread(
                m_ProjectRoot,
                SelectedModel,
                SelectedSandbox,
                m_ApprovalPolicy,
                result => OnThreadStarted(result, text),
                OnSendFailed);
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
            m_SelectedThreadInfo = info;
            SelectedCwd = info.Cwd ?? m_ProjectRoot;
            m_Turns.Clear();
            m_AutoTitleThreadId = CodexPreferences.AutoTitle ? threadId : null;
            m_EffortSyncedThreadId = null;
            m_Queued.Clear();
            m_QueueGeneration++;
            m_LiveThreadId = threadId;
            Changed?.Invoke();
            StartTurn(threadId, text);
        }

        /// <summary>
        /// Forks the selected thread after its last completed turn and selects the
        /// new conversation. The source is unchanged.
        /// </summary>
        public bool ForkSelectedThread()
        {
            if (!m_Ready || IsRunning || IsForking || string.IsNullOrEmpty(SelectedThreadId))
            {
                return false;
            }

            IsForking = true;
            string source = SelectedThreadId;
            Status = "Forking conversation...";
            Changed?.Invoke();
            m_Client.ForkThread(
                source,
                true,
                result =>
                {
                    IsForking = false;
                    JObject thread = result?["thread"] as JObject;
                    string threadId = thread?["id"]?.Value<string>();
                    if (string.IsNullOrEmpty(threadId))
                    {
                        OnFailed("thread/fork returned no thread id");
                        return;
                    }

                    CodexThreadInfo info = Parse(thread);
                    if (string.IsNullOrEmpty(info.Preview) && string.IsNullOrEmpty(info.Title))
                    {
                        info.Preview = "Forked conversation";
                    }

                    m_Threads.Insert(0, info);
                    m_SelectedThreadInfo = info;
                    SelectedThreadId = threadId;
                    SelectedCwd = info.Cwd ?? m_ProjectRoot;
                    m_Turns.Clear();
                    m_LiveTurn = null;
                    m_LiveThreadId = null;
                    m_ActiveThreadId = null;
                    m_ActiveTurnId = null;
                    m_AutoTitleThreadId = null;
                    m_EffortSyncedThreadId = null;
                    m_Queued.Clear();
                    m_QueueGeneration++;
                    Changed?.Invoke();
                    SelectThread(threadId);
                },
                message =>
                {
                    IsForking = false;
                    OnFailed(message);
                });
            return true;
        }

        void MaybeStartAutoTitle(string threadId, JArray turns)
        {
            if (!CodexPreferences.AutoTitle || m_TitleGenerating ||
                m_AutoTitleThreadId != threadId || IsRunning ||
                turns == null || turns.Count == 0)
            {
                return;
            }

            m_AutoTitleThreadId = null;
            BeginAutoTitle(threadId, turns);
        }

        void BeginAutoTitle(string threadId, JArray turns)
        {
            string userText = null;
            string assistantText = null;
            for (int index = turns.Count - 1; index >= 0; index--)
            {
                if (turns[index] is not JObject turn || turn["items"] is not JArray items)
                {
                    continue;
                }

                foreach (JToken token in items)
                {
                    if (token is not JObject item)
                    {
                        continue;
                    }

                    string type = item["type"]?.Value<string>();
                    if (userText == null && type == "userMessage")
                    {
                        userText = ExtractTitleUserText(item);
                    }
                    else if (assistantText == null && type == "agentMessage")
                    {
                        assistantText = item["text"]?.Value<string>();
                    }
                }
            }

            if (string.IsNullOrWhiteSpace(userText))
            {
                return;
            }

            string model = CodexPreferences.AutoTitleModel;
            if (string.IsNullOrWhiteSpace(model))
            {
                model = SelectedModel;
            }

            string prompt =
                "Name this conversation with a short title. Use at most 6 words and 60 characters. " +
                "Use the user's language when natural. Reply with the title only: no quotes, markdown or " +
                "ending punctuation.\n\n<user>\n" + TrimSource(userText, 1600) + "\n</user>\n";
            if (!string.IsNullOrWhiteSpace(assistantText))
            {
                prompt += "\n<assistant>\n" + TrimSource(assistantText, 1600) + "\n</assistant>\n";
            }

            m_TitleGenerating = true;
            m_TitleTargetThreadId = threadId;
            m_TitleThreadId = null;
            m_TitleModel = model;
            m_TitleText.Clear();
            m_TitleDeadline = EditorApplication.timeSinceStartup + 45;
            if (!m_TitleTimeoutSubscribed)
            {
                m_TitleTimeoutSubscribed = true;
                EditorApplication.update += CheckAutoTitleTimeout;
            }

            m_Client.StartThread(
                m_ProjectRoot,
                model,
                "read-only",
                new JValue("never"),
                result => OnTitleThreadStarted(result, prompt),
                FailAutoTitle,
                true);
        }

        void OnTitleThreadStarted(JObject result, string prompt)
        {
            string threadId = result?["thread"]?["id"]?.Value<string>();
            if (string.IsNullOrEmpty(threadId))
            {
                FailAutoTitle("ephemeral title thread returned no id");
                return;
            }

            m_TitleThreadId = threadId;
            m_Client.StartTurn(
                threadId,
                prompt,
                m_ProjectRoot,
                m_TitleModel,
                null,
                null,
                null,
                _ => { },
                FailAutoTitle);
        }

        void CompleteAutoTitle(string raw)
        {
            string title = SanitizeTitle(raw);
            if (string.IsNullOrEmpty(title))
            {
                FailAutoTitle("empty title");
                return;
            }

            string target = m_TitleTargetThreadId;
            m_Client.SetThreadName(
                target,
                title,
                () =>
                {
                    CodexThreadInfo info = FindThread(target);
                    if (info != null)
                    {
                        info.Title = title;
                    }

                    if (m_SelectedThreadInfo != null && m_SelectedThreadInfo.Id == target)
                    {
                        m_SelectedThreadInfo.Title = title;
                    }

                    ClearAutoTitle();
                    Changed?.Invoke();
                },
                FailAutoTitle);
        }

        void FailAutoTitle(string message)
        {
            if (m_TitleGenerating)
            {
                Debug.LogWarning($"[Codex] auto title skipped: {message}");
            }

            ClearAutoTitle();
        }

        void ClearAutoTitle()
        {
            m_TitleGenerating = false;
            m_TitleThreadId = null;
            m_TitleTargetThreadId = null;
            m_TitleModel = null;
            m_TitleText.Clear();
            if (m_TitleTimeoutSubscribed)
            {
                m_TitleTimeoutSubscribed = false;
                EditorApplication.update -= CheckAutoTitleTimeout;
            }
        }

        void CheckAutoTitleTimeout()
        {
            if (!m_TitleGenerating)
            {
                ClearAutoTitle();
                return;
            }

            if (EditorApplication.timeSinceStartup >= m_TitleDeadline)
            {
                FailAutoTitle("timed out");
            }
        }

        static string ExtractTitleUserText(JObject item)
        {
            if (item["content"] is not JArray content)
            {
                return string.Empty;
            }

            var builder = new StringBuilder();
            foreach (JToken part in content)
            {
                if (part?["type"]?.Value<string>() == "text")
                {
                    string text = part["text"]?.Value<string>();
                    if (!string.IsNullOrEmpty(text))
                    {
                        builder.AppendLine(text);
                    }
                }
            }

            return CodexAnnotations.Clean(builder.ToString().Trim());
        }

        static string FindTitleAgentText(JObject turn)
        {
            if (turn?["items"] is not JArray items)
            {
                return string.Empty;
            }

            for (int index = items.Count - 1; index >= 0; index--)
            {
                if (items[index] is JObject item &&
                    item["type"]?.Value<string>() == "agentMessage")
                {
                    return item["text"]?.Value<string>() ?? string.Empty;
                }
            }

            return string.Empty;
        }

        static string TrimSource(string text, int limit)
        {
            if (string.IsNullOrEmpty(text) || text.Length <= limit)
            {
                return text ?? string.Empty;
            }

            return text.Substring(0, limit) + "…";
        }

        static string SanitizeTitle(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return string.Empty;
            }

            string title = raw.Replace('\r', ' ').Replace('\n', ' ').Trim();
            if (title.StartsWith("Title:", StringComparison.OrdinalIgnoreCase))
            {
                title = title.Substring(6).Trim();
            }

            title = title.Trim('"', '\'', '`', '#', '*', ' ');
            title = title.TrimEnd('.', '。', '!', '！', '?', '？', ':', '：');
            while (title.Contains("  "))
            {
                title = title.Replace("  ", " ");
            }

            if (title.Length > 64)
            {
                int cut = title.LastIndexOf(' ', 63);
                title = cut > 0 ? title.Substring(0, cut) : title.Substring(0, 64);
            }

            return title.Trim();
        }

        void StartTurn(string threadId, string text)
        {
            m_ActiveThreadId = threadId;
            Status = "Starting turn...";
            Changed?.Invoke();

            string requested = threadId;
            JObject sandboxPolicy = BuildSandboxPolicy(SelectedSandbox);
            bool effortSynced = m_EffortSyncedThreadId == threadId;
            string effort = effortSynced ? null : SelectedEffort;
            m_Client.StartTurn(
                threadId,
                text,
                SelectedCwd,
                SelectedModel,
                effort,
                sandboxPolicy,
                m_ApprovalPolicy,
                result =>
                {
                    if (!IsRunning || m_ActiveThreadId != requested)
                    {
                        return;
                    }

                    if (!string.IsNullOrEmpty(effort))
                    {
                        // turn/start makes the effort sticky for later turns.
                        m_EffortSyncedThreadId = requested;
                    }

                    JObject turn = result?["turn"] as JObject;
                    if (turn != null)
                    {
                        ApplyServerTurn(turn, false);
                    }

                    string turnId = turn?["id"]?.Value<string>() ?? result?["turnId"]?.Value<string>();
                    if (!string.IsNullOrEmpty(turnId))
                    {
                        m_ActiveTurnId = turnId;
                    }

                    Status = "Codex is working...";
                    Changed?.Invoke();
                },
                OnSendFailed);
        }

        void OnNotification(JObject message)
        {
            string method = message["method"]?.Value<string>();
            if (message["params"] is not JObject parameters)
            {
                return;
            }

            if (m_TitleGenerating &&
                parameters["threadId"]?.Value<string>() == m_TitleThreadId)
            {
                if (method == "item/agentMessage/delta")
                {
                    m_TitleText.Append(parameters["delta"]?.Value<string>());
                    return;
                }

                if (method == "turn/completed")
                {
                    string text = FindTitleAgentText(parameters["turn"] as JObject);
                    if (string.IsNullOrWhiteSpace(text))
                    {
                        text = m_TitleText.ToString();
                    }

                    if (string.IsNullOrWhiteSpace(text))
                    {
                        FailAutoTitle("title turn produced no text");
                    }
                    else
                    {
                        CompleteAutoTitle(text);
                    }

                    return;
                }
            }

            switch (method)
            {
                case "turn/started":
                    OnTurnStarted(parameters);
                    break;
                case "item/started":
                case "item/completed":
                    OnItemUpserted(parameters);
                    break;
                case "item/agentMessage/delta":
                    OnAgentMessageDelta(parameters);
                    break;
                case "item/reasoning/summaryPartAdded":
                    OnReasoningSummaryPartAdded(parameters);
                    break;
                case "item/reasoning/summaryTextDelta":
                    OnReasoningSummaryDelta(parameters);
                    break;
                case "turn/completed":
                    OnTurnCompleted(parameters);
                    break;
                case "thread/archived":
                    string archivedThreadId = parameters["threadId"]?.Value<string>();
                    if (archivedThreadId == m_ArchiveVerifyingThreadId)
                    {
                        // VerifyArchive owns the decision while our request is in flight.
                        break;
                    }

                    if (!string.IsNullOrEmpty(archivedThreadId))
                    {
                        m_ArchivedThreadIds.Add(archivedThreadId);
                    }

                    OnThreadRemoved(archivedThreadId, "Conversation archived");
                    break;
                case "thread/deleted":
                    OnThreadRemoved(
                        parameters["threadId"]?.Value<string>(),
                        "Conversation removed");
                    break;
                case "thread/unarchived":
                    string unarchivedThreadId = parameters["threadId"]?.Value<string>();
                    if (!string.IsNullOrEmpty(unarchivedThreadId))
                    {
                        m_ArchivedThreadIds.Remove(unarchivedThreadId);
                    }

                    RefreshThreads();
                    break;
                case "thread/name/updated":
                    OnThreadNameUpdated(parameters);
                    break;
                case "thread/settings/updated":
                    OnThreadSettingsUpdated(parameters);
                    break;
                case "thread/queue/changed":
                    OnQueueChanged(parameters);
                    break;
                case "thread/tokenUsage/updated":
                    OnTokenUsageUpdated(parameters);
                    break;
            }
        }

        void OnThreadNameUpdated(JObject parameters)
        {
            string threadId = parameters["threadId"]?.Value<string>();
            CodexThreadInfo thread = FindThread(threadId);
            if (thread == null)
            {
                return;
            }

            thread.Title = parameters["threadName"]?.Value<string>();
            if (m_SelectedThreadInfo != null && m_SelectedThreadInfo.Id == threadId)
            {
                m_SelectedThreadInfo.Title = thread.Title;
            }

            Changed?.Invoke();
        }

        void OnThreadSettingsUpdated(JObject parameters)
        {
            string threadId = parameters["threadId"]?.Value<string>();
            if (threadId != SelectedThreadId)
            {
                return;
            }

            JObject settings = parameters["threadSettings"] as JObject;
            string effort = settings?["effort"]?.Value<string>();
            if (string.IsNullOrEmpty(effort))
            {
                return;
            }

            SelectedEffort = effort;
            m_EffortSyncedThreadId = threadId;
            m_SettingsError = null;
            m_SettingsSource = "thread";
            SettingsChanged?.Invoke();
        }

        void OnQueueChanged(JObject parameters)
        {
            string threadId = parameters["threadId"]?.Value<string>();
            if (threadId == SelectedThreadId)
            {
                LoadQueue(threadId);
            }
        }

        void OnTokenUsageUpdated(JObject parameters)
        {
            string threadId = parameters["threadId"]?.Value<string>();
            if (string.IsNullOrEmpty(threadId) || parameters["tokenUsage"] is not JObject tokenUsage)
            {
                return;
            }

            long used = ReadLong(tokenUsage["last"]?["totalTokens"]);
            if (used <= 0)
            {
                used = ReadLong(tokenUsage["total"]?["totalTokens"]);
            }

            long window = ReadLong(tokenUsage["modelContextWindow"]);
            if (window <= 0)
            {
                window = m_ConfigContextWindow;
            }

            m_ContextUsage[threadId] = new CodexContextUsage(used, window);
            if (threadId == SelectedThreadId)
            {
                ContextChanged?.Invoke();
            }
        }

        void OnThreadRemoved(string threadId, string status)
        {
            if (string.IsNullOrEmpty(threadId))
            {
                return;
            }

            int index = m_Threads.FindIndex(thread => thread.Id == threadId);
            bool selected = threadId == SelectedThreadId;
            if (index >= 0)
            {
                m_Threads.RemoveAt(index);
            }

            m_ContextUsage.Remove(threadId);

            if (!selected)
            {
                Changed?.Invoke();
                return;
            }

            if (IsRunning)
            {
                IsRunning = false;
                m_ActiveThreadId = null;
                m_ActiveTurnId = null;
                StopStreamingUpdates();
            }

            SelectedThreadId = null;
            m_SelectedThreadInfo = null;
            SelectedCwd = null;
            m_Turns.Clear();
            m_LiveTurn = null;
            m_LiveThreadId = null;
            m_EffortSyncedThreadId = null;
            m_Queued.Clear();
            m_QueueGeneration++;

            if (m_Threads.Count > 0)
            {
                int nextIndex = Math.Min(Math.Max(index, 0), m_Threads.Count - 1);
                SelectThread(m_Threads[nextIndex].Id);
                return;
            }

            Status = status;
            Changed?.Invoke();
            ContextChanged?.Invoke();
        }

        void OnTurnStarted(JObject parameters)
        {
            string threadId = parameters["threadId"]?.Value<string>();
            if (string.IsNullOrEmpty(threadId))
            {
                return;
            }

            if (threadId != SelectedThreadId && threadId != m_LiveThreadId)
            {
                return;
            }

            if (!IsRunning)
            {
                // A queued turn can start from the server after the previous one
                // completes; adopt it so the composer and live view stay in sync.
                IsRunning = true;
                m_TurnGeneration++;
                m_LiveThreadId = threadId;
                m_LiveTurn = new JObject();
                m_ActiveTurnId = null;
            }

            if (string.IsNullOrEmpty(m_LiveThreadId))
            {
                m_LiveThreadId = threadId;
            }

            if (threadId != m_LiveThreadId)
            {
                return;
            }

            m_ActiveThreadId = threadId;
            JObject turn = parameters["turn"] as JObject;
            if (turn != null)
            {
                ApplyServerTurn(turn, false);
                string turnId = turn["id"]?.Value<string>();
                if (!string.IsNullOrEmpty(turnId))
                {
                    m_ActiveTurnId = turnId;
                }
            }

            if (threadId == SelectedThreadId)
            {
                Status = "Codex is working...";
                Changed?.Invoke();
            }
        }

        void OnItemUpserted(JObject parameters)
        {
            if (!TryGetLiveThread(parameters, out string threadId))
            {
                return;
            }

            if (parameters["item"] is not JObject item)
            {
                return;
            }

            ApplyItem(item);
            MarkTranscriptChanged(threadId);
        }

        void OnAgentMessageDelta(JObject parameters)
        {
            if (!TryGetLiveThread(parameters, out string threadId))
            {
                return;
            }

            string itemId = parameters["itemId"]?.Value<string>();
            string delta = parameters["delta"]?.Value<string>();
            if (string.IsNullOrEmpty(delta))
            {
                return;
            }

            JArray items = EnsureLiveItems();
            int index = IndexOfItem(items, itemId);
            JObject item;
            if (index < 0)
            {
                item = new JObject
                {
                    ["id"] = itemId,
                    ["type"] = "agentMessage",
                    ["text"] = delta,
                };
                items.Add(item);
            }
            else
            {
                item = items[index] as JObject;
                if (item == null || item["type"]?.Value<string>() != "agentMessage")
                {
                    return;
                }

                item["text"] = (item["text"]?.Value<string>() ?? string.Empty) + delta;
            }

            MarkTranscriptChanged(threadId);
        }

        void OnReasoningSummaryPartAdded(JObject parameters)
        {
            if (!TryGetLiveThread(parameters, out string threadId))
            {
                return;
            }

            string itemId = parameters["itemId"]?.Value<string>();
            int summaryIndex = parameters["summaryIndex"]?.Value<int>() ?? 0;
            if (string.IsNullOrEmpty(itemId) || summaryIndex < 0)
            {
                return;
            }

            JObject item = EnsureReasoningItem(itemId, out JArray summary);
            while (summary.Count <= summaryIndex)
            {
                summary.Add(new JObject
                {
                    ["type"] = "summary_text",
                    ["text"] = string.Empty,
                });
            }

            m_ReasoningSummaries[SummaryKey(threadId, itemId)] = (JArray)summary.DeepClone();
            MarkTranscriptChanged(threadId);
        }

        void OnReasoningSummaryDelta(JObject parameters)
        {
            if (!TryGetLiveThread(parameters, out string threadId))
            {
                return;
            }

            string itemId = parameters["itemId"]?.Value<string>();
            string delta = parameters["delta"]?.Value<string>();
            int summaryIndex = parameters["summaryIndex"]?.Value<int>() ?? 0;
            if (string.IsNullOrEmpty(itemId) || string.IsNullOrEmpty(delta) || summaryIndex < 0)
            {
                return;
            }

            JObject item = EnsureReasoningItem(itemId, out JArray summary);
            while (summary.Count <= summaryIndex)
            {
                summary.Add(new JObject
                {
                    ["type"] = "summary_text",
                    ["text"] = string.Empty,
                });
            }

            if (summary[summaryIndex] is JObject part)
            {
                part["text"] = (part["text"]?.Value<string>() ?? string.Empty) + delta;
            }
            else
            {
                summary[summaryIndex] = new JObject
                {
                    ["type"] = "summary_text",
                    ["text"] = delta,
                };
            }

            m_ReasoningSummaries[SummaryKey(threadId, itemId)] = (JArray)summary.DeepClone();
            MarkTranscriptChanged(threadId);
        }

        JObject EnsureReasoningItem(string itemId, out JArray summary)
        {
            JArray items = EnsureLiveItems();
            int index = IndexOfItem(items, itemId);
            JObject item;
            if (index < 0)
            {
                item = new JObject
                {
                    ["id"] = itemId,
                    ["type"] = "reasoning",
                };
                items.Add(item);
            }
            else
            {
                item = items[index] as JObject;
                if (item == null)
                {
                    item = new JObject
                    {
                        ["id"] = itemId,
                        ["type"] = "reasoning",
                    };
                    items[index] = item;
                }
            }

            if (item["summary"] is not JArray existing)
            {
                existing = new JArray();
                item["summary"] = existing;
            }

            summary = existing;
            return item;
        }

        static string SummaryKey(string threadId, string itemId)
        {
            return $"{threadId}\n{itemId}";
        }

        void MergeCachedReasoningSummaries(string threadId, JArray items)
        {
            if (items == null)
            {
                return;
            }

            foreach (JToken token in items)
            {
                if (token is not JObject item ||
                    item["type"]?.Value<string>() != "reasoning")
                {
                    continue;
                }

                string itemId = item["id"]?.Value<string>();
                if (string.IsNullOrEmpty(itemId) ||
                    !m_ReasoningSummaries.TryGetValue(SummaryKey(threadId, itemId), out JArray cached))
                {
                    continue;
                }

                if (item["summary"] is not JArray summary || summary.Count == 0)
                {
                    item["summary"] = cached.DeepClone();
                }
            }
        }

        void OnTurnCompleted(JObject parameters)
        {
            string threadId = parameters["threadId"]?.Value<string>();
            if (string.IsNullOrEmpty(threadId) || threadId != m_LiveThreadId)
            {
                return;
            }

            m_QuestionRequests.RemoveAll(request => request.ThreadId == threadId);
            JObject turn = parameters["turn"] as JObject;
            if (turn != null)
            {
                ApplyServerTurn(turn, true);
            }

            if (m_LiveTurn != null)
            {
                m_LiveTurn["status"] = turn?["status"]?.Value<string>() ?? "completed";
            }

            string turnStatus = turn?["status"]?.Value<string>();
            IsRunning = false;
            m_ActiveThreadId = null;
            m_ActiveTurnId = null;
            StopStreamingUpdates();

            if (threadId == SelectedThreadId)
            {
                Status =
                    turnStatus == "failed" ? "Turn failed" :
                    turnStatus == "interrupted" ? "Turn interrupted" :
                    "Loading conversation...";
                Changed?.Invoke();
                LoadTurns(threadId);
            }
            else
            {
                Changed?.Invoke();
            }
        }

        bool TryGetLiveThread(JObject parameters, out string threadId)
        {
            threadId = parameters["threadId"]?.Value<string>();
            if (string.IsNullOrEmpty(threadId))
            {
                return false;
            }

            if (string.IsNullOrEmpty(m_LiveThreadId))
            {
                if (!IsRunning || (!string.IsNullOrEmpty(SelectedThreadId) && threadId != SelectedThreadId))
                {
                    return false;
                }

                m_LiveThreadId = threadId;
            }

            return threadId == m_LiveThreadId;
        }

        void ApplyServerTurn(JObject turn, bool authoritative)
        {
            if (m_LiveTurn == null)
            {
                m_LiveTurn = new JObject();
            }

            string id = turn["id"]?.Value<string>();
            if (!string.IsNullOrEmpty(id))
            {
                m_LiveTurn["id"] = id;
            }

            string status = turn["status"]?.Value<string>();
            if (!string.IsNullOrEmpty(status))
            {
                m_LiveTurn["status"] = status;
            }

            if (turn["items"] is not JArray items || items.Count == 0)
            {
                if (m_LiveTurn["items"] is not JArray)
                {
                    m_LiveTurn["items"] = new JArray();
                }

                return;
            }

            if (authoritative)
            {
                var mergedItems = new JArray(items);
                MergeCachedReasoningSummaries(
                    string.IsNullOrEmpty(m_LiveThreadId) ? SelectedThreadId : m_LiveThreadId,
                    mergedItems);
                m_LiveTurn["items"] = mergedItems;
                return;
            }

            // turn/started and turn/start may arrive after early deltas; merge
            // their items instead of dropping the text already streamed.
            foreach (JToken token in items)
            {
                if (token is JObject item)
                {
                    ApplyItem(item);
                }
            }
        }

        void ApplyItem(JObject item)
        {
            JArray items = EnsureLiveItems();
            string itemId = item["id"]?.Value<string>();
            if (item["type"]?.Value<string>() == "userMessage")
            {
                RemoveLocalUserItem(items);
            }

            int index = IndexOfItem(items, itemId);
            if (index >= 0 && item["type"]?.Value<string>() == "agentMessage")
            {
                string incoming = item["text"]?.Value<string>() ?? string.Empty;
                string existing = items[index]?["text"]?.Value<string>() ?? string.Empty;
                if (incoming.Length < existing.Length)
                {
                    JObject merged = (JObject)item.DeepClone();
                    merged["text"] = existing;
                    item = merged;
                }
            }
            else if (index >= 0 && item["type"]?.Value<string>() == "reasoning")
            {
                // The completed item can arrive with an empty summary even after
                // summary deltas populated the live item.
                if (item["summary"] is not JArray incomingSummary || incomingSummary.Count == 0)
                {
                    if (items[index]?["summary"] is JArray existingSummary && existingSummary.Count > 0)
                    {
                        JObject merged = (JObject)item.DeepClone();
                        merged["summary"] = existingSummary.DeepClone();
                        item = merged;
                    }
                }
            }

            if (index >= 0)
            {
                items[index] = item.DeepClone();
            }
            else
            {
                items.Add(item.DeepClone());
            }
        }

        JArray EnsureLiveItems()
        {
            if (m_LiveTurn == null)
            {
                m_LiveTurn = new JObject();
            }

            if (m_LiveTurn["items"] is not JArray items)
            {
                items = new JArray();
                m_LiveTurn["items"] = items;
            }

            return items;
        }

        static void RemoveLocalUserItem(JArray items)
        {
            for (int index = items.Count - 1; index >= 0; index--)
            {
                if (items[index]?["id"]?.Value<string>() == LocalUserItemId)
                {
                    items.RemoveAt(index);
                }
            }
        }

        static int IndexOfItem(JArray items, string itemId)
        {
            if (string.IsNullOrEmpty(itemId))
            {
                return -1;
            }

            for (int index = 0; index < items.Count; index++)
            {
                if (items[index]?["id"]?.Value<string>() == itemId)
                {
                    return index;
                }
            }

            return -1;
        }

        void MarkTranscriptChanged(string threadId)
        {
            if (threadId != SelectedThreadId)
            {
                return;
            }

            m_StreamDirty = true;
            if (m_StreamSubscribed)
            {
                return;
            }

            m_StreamSubscribed = true;
            EditorApplication.update += FlushStreaming;
        }

        void FlushStreaming()
        {
            if (!m_StreamDirty)
            {
                StopStreamingUpdates();
                return;
            }

            double now = EditorApplication.timeSinceStartup;
            if (now < m_NextStreamFlush)
            {
                return;
            }

            m_StreamDirty = false;
            m_NextStreamFlush = now + StreamIntervalSeconds;
            TranscriptChanged?.Invoke();
        }

        void StopStreamingUpdates()
        {
            if (m_StreamSubscribed)
            {
                EditorApplication.update -= FlushStreaming;
                m_StreamSubscribed = false;
            }

            m_StreamDirty = false;
        }

        void OnInitialized(JObject result)
        {
            m_Ready = true;
            Status = "Connected";
            Error = null;
            Changed?.Invoke();
            RefreshThreads();
            LoadSettings();
        }

        void OnThreadsLoaded(JArray threads, int generation, bool isSearch)
        {
            if (generation != m_ThreadListGeneration)
            {
                return;
            }

            m_Threads.Clear();
            foreach (JToken token in threads)
            {
                if (token is not JObject result)
                {
                    continue;
                }

                if (isSearch)
                {
                    if (result["thread"] is not JObject searchThread)
                    {
                        continue;
                    }

                    CodexThreadInfo info = Parse(searchThread);
                    if (m_ArchivedThreadIds.Contains(info.Id) ||
                        !string.IsNullOrEmpty(info.ParentThreadId) ||
                        (!m_ShowAllProjects && !IsInCurrentProject(info.Cwd)))
                    {
                        continue;
                    }

                    info.Snippet = result["snippet"]?.Value<string>();
                    m_Threads.Add(info);
                }
                else
                {
                    CodexThreadInfo info = Parse(result);
                    if (!m_ArchivedThreadIds.Contains(info.Id) &&
                        string.IsNullOrEmpty(info.ParentThreadId))
                    {
                        m_Threads.Add(info);
                    }
                }
            }

            ThreadsLoaded = true;
            if (isSearch)
            {
                Status = m_Threads.Count switch
                {
                    0 => "No matches",
                    1 => "1 match",
                    _ => $"{m_Threads.Count} matches",
                };
            }
            else if (!m_ShowAllProjects && m_Threads.Count == 0)
            {
                Status = "No conversations in this project";
            }
            else
            {
                Status = m_Threads.Count == 1 ? "1 conversation" : $"{m_Threads.Count} conversations";
            }

            Changed?.Invoke();

            if (IsRunning)
            {
                return;
            }

            bool selectionStillExists =
                !string.IsNullOrEmpty(SelectedThreadId) && FindThread(SelectedThreadId) != null;

            if (m_InitialSelectionDone)
            {
                // Filtering and searching must not steal the open conversation.
                return;
            }

            m_InitialSelectionDone = true;

            if (selectionStillExists)
            {
                // Reconnects keep the selection while the new app-server has not
                // loaded the thread yet; resume it before the first turn.
                SelectThread(SelectedThreadId);
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
            ContextChanged?.Invoke();
        }

        bool IsInCurrentProject(string cwd)
        {
            if (string.IsNullOrEmpty(cwd) || string.IsNullOrEmpty(m_ProjectRoot))
            {
                return false;
            }

            return string.Equals(
                cwd.TrimEnd('\\', '/'),
                m_ProjectRoot.TrimEnd('\\', '/'),
                StringComparison.OrdinalIgnoreCase);
        }

        void OnTurnsLoaded(string threadId, JArray turns, int generation)
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
                        MergeCachedReasoningSummaries(threadId, turn["items"] as JArray);
                        m_Turns.Add(turn);
                    }
                }
            }

            // A daemon keeps the turn running across a domain reload. Hydrating
            // the thread again must put that turn back into the live slot.
            int activeIndex = m_Turns.FindIndex(
                turn => turn["status"]?.Value<string>() == "inProgress");
            if (activeIndex >= 0)
            {
                JObject active = m_Turns[activeIndex];
                m_Turns.RemoveAt(activeIndex);
                m_LiveTurn = active;
                m_LiveThreadId = threadId;
                m_ActiveThreadId = threadId;
                m_ActiveTurnId = active["id"]?.Value<string>();
                IsRunning = true;
            }

            if (!IsRunning && generation == m_TurnGeneration && m_LiveThreadId == threadId)
            {
                m_LiveTurn = null;
                m_LiveThreadId = null;
            }

            Status = IsRunning
                ? "Codex is working..."
                : m_Turns.Count == 1 ? "1 turn" : $"{m_Turns.Count} turns";
            Changed?.Invoke();
            LoadQueue(threadId);
            MaybeStartAutoTitle(threadId, turns);
        }

        void OnServerRequest(JObject request)
        {
            string method = request["method"]?.Value<string>();
            if (method != "item/tool/requestUserInput" ||
                request["params"] is not JObject parameters ||
                request["params"]?["questions"] is not JArray questions)
            {
                return;
            }

            var pending = new CodexQuestionRequest
            {
                Id = request["id"],
                ThreadId = parameters["threadId"]?.Value<string>(),
                TurnId = parameters["turnId"]?.Value<string>(),
                ItemId = parameters["itemId"]?.Value<string>(),
                IsBlocking = parameters["isBlocking"]?.Value<bool>() ?? false,
            };

            foreach (JToken token in questions)
            {
                if (token is not JObject source)
                {
                    continue;
                }

                var question = new CodexQuestion
                {
                    Id = source["id"]?.Value<string>(),
                    Header = source["header"]?.Value<string>(),
                    Prompt = source["question"]?.Value<string>(),
                    IsOther = source["isOther"]?.Value<bool>() ?? false,
                    IsSecret = source["isSecret"]?.Value<bool>() ?? false,
                };

                if (source["options"] is JArray options)
                {
                    foreach (JToken optionToken in options)
                    {
                        if (optionToken is not JObject option)
                        {
                            continue;
                        }

                        question.Options.Add(new CodexQuestionOption
                        {
                            Label = option["label"]?.Value<string>(),
                            Description = option["description"]?.Value<string>(),
                        });
                    }
                }

                if (!string.IsNullOrEmpty(question.Id))
                {
                    pending.Questions.Add(question);
                }
            }

            if (pending.Questions.Count == 0)
            {
                m_Connection?.RespondError(request["id"], -32602, "No supported questions");
                return;
            }

            m_QuestionRequests.Add(pending);
            Error = null;
            Status = "Codex is waiting for your answer";
            Changed?.Invoke();
        }

        /// <summary>Stores one answer and sends the complete response once every question is answered.</summary>
        public bool AnswerQuestion(CodexQuestionRequest request, string answer)
        {
            CodexQuestion question = request?.CurrentQuestion;
            if (question == null || string.IsNullOrWhiteSpace(answer) || m_Connection == null)
            {
                return false;
            }

            question.Answer = answer.Trim();
            if (request.CurrentQuestion != null)
            {
                Status = "Codex is waiting for your answer";
                Changed?.Invoke();
                return true;
            }

            var answers = new JObject();
            foreach (CodexQuestion item in request.Questions)
            {
                answers[item.Id] = new JObject
                {
                    ["answers"] = new JArray(item.Answer),
                };
            }

            m_Connection.Respond(request.Id, new JObject { ["answers"] = answers });
            m_QuestionRequests.Remove(request);
            Status = IsRunning ? "Codex is working..." : "Answer sent";
            Changed?.Invoke();
            return true;
        }

        void OnDisconnected(string reason)
        {
            m_Ready = false;
            IsRunning = false;
            IsForking = false;
            ThreadsLoaded = false;
            m_ActiveThreadId = null;
            m_ActiveTurnId = null;
            m_LiveTurn = null;
            m_LiveThreadId = null;
            m_QuestionRequests.Clear();
            ClearAutoTitle();
            StopStreamingUpdates();
            CancelSearchDebounce();
            m_ContextUsage.Clear();
            Status = "Disconnected";
            Error = reason;
            Changed?.Invoke();
            ContextChanged?.Invoke();
        }

        void OnSendFailed(string message)
        {
            IsRunning = false;
            m_ActiveThreadId = null;
            m_ActiveTurnId = null;
            m_LiveTurn = null;
            m_LiveThreadId = null;
            m_QuestionRequests.Clear();
            StopStreamingUpdates();
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

        List<CodexSubagentInfo> BuildSubagents()
        {
            var map = new Dictionary<string, CodexSubagentInfo>(StringComparer.Ordinal);
            for (int index = m_Turns.Count - 1; index >= 0; index--)
            {
                CollectSubagents(m_Turns[index], map, false);
            }

            CollectSubagents(m_LiveTurn, map, IsRunning);

            foreach (CodexSubagentInfo info in map.Values)
            {
                if (!info.Live && (info.Status == "running" || info.Status == "pending"))
                {
                    info.Status = "idle";
                    info.Activity = string.IsNullOrEmpty(info.Activity)
                        ? "not running"
                        : "last: " + info.Activity;
                }
            }

            var result = new List<CodexSubagentInfo>(map.Values);
            result.Sort((left, right) =>
            {
                int running = (left.IsRunning ? 0 : 1).CompareTo(right.IsRunning ? 0 : 1);
                return running != 0
                    ? running
                    : string.Compare(
                        string.IsNullOrEmpty(left.Path) ? left.ThreadId : left.Path,
                        string.IsNullOrEmpty(right.Path) ? right.ThreadId : right.Path,
                        StringComparison.OrdinalIgnoreCase);
            });
            return result;
        }

        static void CollectSubagents(
            JObject turn,
            Dictionary<string, CodexSubagentInfo> map,
            bool live)
        {
            if (turn?["items"] is not JArray items)
            {
                return;
            }

            foreach (JToken token in items)
            {
                if (token is not JObject item)
                {
                    continue;
                }

                string type = item["type"]?.Value<string>();
                if (type == "collabAgentToolCall")
                {
                    CollectCollabAgents(item, map, live);
                }
                else if (type == "subAgentActivity")
                {
                    CollectSubagentActivity(item, map, live);
                }
            }
        }

        static void CollectCollabAgents(
            JObject item,
            Dictionary<string, CodexSubagentInfo> map,
            bool live)
        {
            string model = item["model"]?.Value<string>();
            string effort = item["reasoningEffort"]?.Value<string>();
            string prompt = SubagentActivityText(item["prompt"]?.Value<string>());

            if (item["agentsStates"] is JObject states)
            {
                foreach (JProperty property in states.Properties())
                {
                    CodexSubagentInfo info = GetSubagent(map, property.Name);
                    if (info == null)
                    {
                        continue;
                    }

                    if (live)
                    {
                        info.Live = true;
                    }

                    string status = NormalizeSubagentStatus(
                        property.Value?["status"]?.Value<string>());
                    if (!string.IsNullOrEmpty(status))
                    {
                        info.Status = status;
                    }

                    string message = property.Value?["message"]?.Value<string>();
                    if (!string.IsNullOrWhiteSpace(message))
                    {
                        info.Activity = SubagentActivityText(message);
                    }

                    ApplySubagentConfig(info, model, effort);
                }
            }

            if (item["receiverThreadIds"] is JArray receivers)
            {
                foreach (JToken receiver in receivers)
                {
                    CodexSubagentInfo info = GetSubagent(map, receiver.Value<string>());
                    if (info == null)
                    {
                        continue;
                    }

                    if (live)
                    {
                        info.Live = true;
                    }

                    if (string.IsNullOrEmpty(info.Status))
                    {
                        info.Status = "pending";
                    }

                    if (string.IsNullOrEmpty(info.Activity) && prompt.Length > 0)
                    {
                        info.Activity = prompt;
                    }

                    ApplySubagentConfig(info, model, effort);
                }
            }
        }

        static void CollectSubagentActivity(
            JObject item,
            Dictionary<string, CodexSubagentInfo> map,
            bool live)
        {
            string threadId = item["agentThreadId"]?.Value<string>();
            CodexSubagentInfo info = GetSubagent(map, threadId);
            if (info == null)
            {
                return;
            }

            if (live)
            {
                info.Live = true;
            }

            string path = item["agentPath"]?.Value<string>();
            if (!string.IsNullOrWhiteSpace(path))
            {
                info.Path = path;
            }

            string kind = item["kind"]?.Value<string>();
            string status = NormalizeSubagentStatus(kind);
            if (!string.IsNullOrEmpty(status))
            {
                info.Status = status;
            }

            switch (kind)
            {
                case "started":
                    info.Activity = "started";
                    break;
                case "interacted":
                    info.Activity = "working";
                    break;
                case "completed":
                    info.Activity = "completed";
                    break;
                case "interrupted":
                    info.Activity = "interrupted";
                    break;
            }

            ApplySubagentConfig(
                info,
                item["model"]?.Value<string>(),
                item["reasoningEffort"]?.Value<string>());
        }

        static CodexSubagentInfo GetSubagent(
            Dictionary<string, CodexSubagentInfo> map,
            string threadId)
        {
            if (string.IsNullOrEmpty(threadId))
            {
                return null;
            }

            if (!map.TryGetValue(threadId, out CodexSubagentInfo info))
            {
                info = new CodexSubagentInfo
                {
                    ThreadId = threadId,
                    Status = "pending",
                };
                map[threadId] = info;
            }

            return info;
        }

        static void ApplySubagentConfig(
            CodexSubagentInfo info,
            string model,
            string effort)
        {
            if (!string.IsNullOrWhiteSpace(model))
            {
                info.Model = model;
            }

            if (!string.IsNullOrWhiteSpace(effort))
            {
                info.Effort = effort;
            }
        }

        static string NormalizeSubagentStatus(string status)
        {
            switch (status?.ToLowerInvariant())
            {
                case "running":
                case "inprogress":
                case "started":
                case "interacted":
                    return "running";
                case "pendinginit":
                    return "pending";
                case "completed":
                    return "completed";
                case "failed":
                case "errored":
                    return "failed";
                case "interrupted":
                case "shutdown":
                    return "interrupted";
                case "notfound":
                    return "missing";
                case null:
                case "":
                    return "pending";
                default:
                    return status;
            }
        }

        static string SubagentActivityText(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }

            string oneLine = text
                .Replace('\r', ' ')
                .Replace('\n', ' ')
                .Trim();
            while (oneLine.Contains("  "))
            {
                oneLine = oneLine.Replace("  ", " ");
            }

            return oneLine.Length <= 140 ? oneLine : oneLine.Substring(0, 140) + "…";
        }

        static CodexThreadInfo Parse(JObject thread)
        {
            return new CodexThreadInfo
            {
                Id = thread["id"]?.Value<string>(),
                Title = thread["name"]?.Value<string>(),
                Preview = thread["preview"]?.Value<string>()
                          ?? thread["title"]?.Value<string>()
                          ?? "(untitled)",
                Cwd = thread["cwd"]?.Value<string>(),
                ParentThreadId = thread["parentThreadId"]?.Value<string>(),
                UpdatedAt = thread["updatedAt"]?.Value<long>() ?? 0,
            };
        }

        static long ReadLong(JToken token, long fallback = 0)
        {
            return token != null && token.Type == JTokenType.Integer
                ? token.Value<long>()
                : fallback;
        }

        static JObject NewLiveTurn(string text)
        {
            return new JObject
            {
                ["status"] = "inProgress",
                ["items"] = new JArray(new JObject
                {
                    ["id"] = LocalUserItemId,
                    ["type"] = "userMessage",
                    ["content"] = new JArray(new JObject
                    {
                        ["type"] = "text",
                        ["text"] = text,
                    }),
                }),
            };
        }

        static string MapSandboxPolicy(JToken sandbox)
        {
            if (sandbox == null)
            {
                return null;
            }

            if (sandbox.Type == JTokenType.String)
            {
                return sandbox.Value<string>();
            }

            string type = sandbox["type"]?.Value<string>();
            return type switch
            {
                "readOnly" => "read-only",
                "workspaceWrite" => "workspace-write",
                "dangerFullAccess" => "danger-full-access",
                _ => null,
            };
        }

        JObject BuildSandboxPolicy(string mode)
        {
            switch (mode)
            {
                case "read-only":
                    return new JObject
                    {
                        ["type"] = "readOnly",
                        ["networkAccess"] = false,
                    };
                case "danger-full-access":
                    return new JObject { ["type"] = "dangerFullAccess" };
                case "workspace-write":
                    var roots = new JArray();
                    if (!string.IsNullOrEmpty(m_ProjectRoot))
                    {
                        roots.Add(m_ProjectRoot);
                    }

                    return new JObject
                    {
                        ["type"] = "workspaceWrite",
                        ["writableRoots"] = roots,
                        ["networkAccess"] = false,
                    };
                default:
                    return null;
            }
        }

        static string FormatApproval(JToken approval)
        {
            if (approval == null || approval.Type == JTokenType.Null)
            {
                return "default";
            }

            if (approval.Type == JTokenType.String)
            {
                return approval.Value<string>();
            }

            return approval["granular"] != null ? "granular" : approval.ToString();
        }

        void ClearSessionForHomeChange()
        {
            IsRunning = false;
            IsForking = false;
            SelectedThreadId = null;
            m_SelectedThreadInfo = null;
            SelectedCwd = null;
            m_ActiveThreadId = null;
            m_ActiveTurnId = null;
            m_LiveTurn = null;
            m_LiveThreadId = null;
            m_Turns.Clear();
            m_Threads.Clear();
            m_ContextUsage.Clear();
            m_ReasoningSummaries.Clear();
            m_ArchivedThreadIds.Clear();
            m_ArchiveVerifyingThreadId = null;
            m_QuestionRequests.Clear();
            m_Queued.Clear();
            m_QueueGeneration++;
            m_AutoTitleThreadId = null;
            m_EffortSyncedThreadId = null;
            ClearAutoTitle();
            ThreadsLoaded = false;
            m_InitialSelectionDone = false;
            StopStreamingUpdates();
        }

        public void Dispose()
        {
            m_Ready = false;
            ThreadsLoaded = false;
            IsRunning = false;
            IsForking = false;
            m_ActiveThreadId = null;
            m_ActiveTurnId = null;
            m_LiveTurn = null;
            m_LiveThreadId = null;
            m_SelectedThreadInfo = null;
            m_ConfigLoaded = false;
            m_SettingsFromThread = false;
            m_Models.Clear();
            m_Efforts.Clear();
            m_ContextUsage.Clear();
            m_ReasoningSummaries.Clear();
            m_ArchivedThreadIds.Clear();
            m_ArchiveVerifyingThreadId = null;
            m_QuestionRequests.Clear();
            m_Queued.Clear();
            m_QueueGeneration++;
            m_AutoTitleThreadId = null;
            m_EffortSyncedThreadId = null;
            ClearAutoTitle();
            StopStreamingUpdates();
            CancelSearchDebounce();
            m_Connection?.Dispose();
            m_Connection = null;
            m_Client = null;
            m_Process = null;
        }
    }
}
