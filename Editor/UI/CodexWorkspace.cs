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
        static readonly string[] SourceKinds = { "cli", "vscode", "exec", "appServer", "unknown" };
        static readonly string[] s_SandboxModes = { "read-only", "workspace-write", "danger-full-access" };

        readonly List<CodexThreadInfo> m_Threads = new List<CodexThreadInfo>();
        readonly List<JObject> m_Turns = new List<JObject>();
        readonly List<CodexModelInfo> m_Models = new List<CodexModelInfo>();
        readonly List<string> m_Efforts = new List<string>();
        readonly Dictionary<string, CodexContextUsage> m_ContextUsage =
            new Dictionary<string, CodexContextUsage>();
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
        public bool CanStop =>
            IsRunning && !string.IsNullOrEmpty(m_ActiveThreadId) && !string.IsNullOrEmpty(m_ActiveTurnId);

        public IReadOnlyList<CodexModelInfo> Models => m_Models;
        public IReadOnlyList<string> Efforts => m_Efforts;
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
            m_Client.ArchiveThread(
                threadId,
                () => OnThreadRemoved(threadId, "Conversation archived"),
                OnFailed);
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
            RebuildEfforts();
            SettingsChanged?.Invoke();
        }

        public void SetEffort(string effort)
        {
            if (string.IsNullOrEmpty(effort) || effort == SelectedEffort)
            {
                return;
            }

            SelectedEffort = effort;
            m_SettingsSource = "custom";
            SettingsChanged?.Invoke();
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

            ApplyThreadSettings(result);
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

        void ApplyThreadSettings(JObject resume)
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
            m_LiveThreadId = threadId;
            Changed?.Invoke();
            StartTurn(threadId, text);
        }

        void StartTurn(string threadId, string text)
        {
            m_ActiveThreadId = threadId;
            Status = "Starting turn...";
            Changed?.Invoke();

            string requested = threadId;
            JObject sandboxPolicy = BuildSandboxPolicy(SelectedSandbox);
            m_Client.StartTurn(
                threadId,
                text,
                SelectedCwd,
                SelectedModel,
                SelectedEffort,
                sandboxPolicy,
                m_ApprovalPolicy,
                result =>
                {
                    if (!IsRunning || m_ActiveThreadId != requested)
                    {
                        return;
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
                case "turn/completed":
                    OnTurnCompleted(parameters);
                    break;
                case "thread/archived":
                    OnThreadRemoved(
                        parameters["threadId"]?.Value<string>(),
                        "Conversation archived");
                    break;
                case "thread/deleted":
                    OnThreadRemoved(
                        parameters["threadId"]?.Value<string>(),
                        "Conversation removed");
                    break;
                case "thread/unarchived":
                    RefreshThreads();
                    break;
                case "thread/name/updated":
                    OnThreadNameUpdated(parameters);
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
            if (!IsRunning)
            {
                return;
            }

            string threadId = parameters["threadId"]?.Value<string>();
            if (string.IsNullOrEmpty(threadId))
            {
                return;
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

        void OnTurnCompleted(JObject parameters)
        {
            string threadId = parameters["threadId"]?.Value<string>();
            if (string.IsNullOrEmpty(threadId) || threadId != m_LiveThreadId)
            {
                return;
            }

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
                m_LiveTurn["items"] = new JArray(items);
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
                    if (!m_ShowAllProjects && !IsInCurrentProject(info.Cwd))
                    {
                        continue;
                    }

                    info.Snippet = result["snippet"]?.Value<string>();
                    m_Threads.Add(info);
                }
                else
                {
                    m_Threads.Add(Parse(result));
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
        }

        void OnDisconnected(string reason)
        {
            m_Ready = false;
            IsRunning = false;
            ThreadsLoaded = false;
            m_ActiveThreadId = null;
            m_ActiveTurnId = null;
            m_LiveTurn = null;
            m_LiveThreadId = null;
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
            ThreadsLoaded = false;
            m_InitialSelectionDone = false;
            StopStreamingUpdates();
        }

        public void Dispose()
        {
            m_Ready = false;
            ThreadsLoaded = false;
            IsRunning = false;
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
            StopStreamingUpdates();
            CancelSearchDebounce();
            m_Connection?.Dispose();
            m_Connection = null;
            m_Client = null;
            m_Process = null;
        }
    }
}
