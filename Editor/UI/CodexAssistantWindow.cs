using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Minerva.Codex
{
    /// <summary>
    /// Conversation view for the Codex thread store. The window renders what
    /// <see cref="CodexWorkspace"/> exposes and never talks to the protocol itself.
    /// </summary>
    internal sealed class CodexAssistantWindow : EditorWindow
    {
        const string LastThreadKey = "Minerva.Codex.LastThread";

        const string ScopeProject = "This project";
        const string ScopeAll = "All projects";

        CodexWorkspace m_Workspace;
        ScrollView m_ThreadList;
        ScrollView m_Transcript;
        Label m_StatusLabel;
        Label m_HeaderTitle;
        Label m_HeaderMeta;
        Label m_ErrorLabel;
        Button m_RefreshButton;
        Button m_BackButton;
        Button m_CopyButton;
        Button m_ForkButton;
        Button m_ArchiveButton;
        Button m_SettingsButton;
        Button m_NewButton;
        TextField m_SearchField;
        DropdownField m_ScopeField;
        TextField m_Composer;
        Button m_ActionButton;
        Button m_ModelButton;
        Button m_EffortButton;
        Button m_SandboxButton;
        VisualElement m_ContextIndicator;
        VisualElement m_ContextDot;
        Label m_ContextLabel;
        Label m_SettingsSourceLabel;
        Label m_ComposerHint;
        VisualElement m_LiveContainer;
        VisualElement m_ComposerCard;
        VisualElement m_QueueStrip;
        VisualElement m_QuestionPanel;
        VisualElement m_AgentsPanel;
        ScrollView m_AgentsList;
        Label m_AgentsCountLabel;
        Button m_AgentsButton;
        CodexPulseDot m_WorkingDot;
        bool m_AgentsPanelCollapsed;
        string m_AgentsSignature;
        VisualElement m_Popup;
        VisualElement m_PopupScrim;

        bool m_FollowTranscript = true;
        string m_LastTranscriptThreadId;
        bool m_ShowContextIndicator;
        bool m_ShowReasoning = true;
        bool m_ShowToolCalls = true;
        string m_CodexHome;

        sealed class PopupItem
        {
            public string Label;
            public bool Selected;
            public Action Action;
        }

        [MenuItem("Window/Minerva/Codex Assistant", priority = 1)]
        public static void Open()
        {
            var window = GetWindow<CodexAssistantWindow>();
            window.titleContent = new GUIContent("Codex Assistant");
            window.minSize = new Vector2(900, 480);
            if (window.position.width < 900 || window.position.height < 480)
            {
                window.position = new Rect(window.position.x, window.position.y, 1200, 760);
            }

            window.Show();
        }

        void OnEnable()
        {
            m_ShowContextIndicator = CodexPreferences.ShowContextIndicator;
            m_ShowReasoning = CodexPreferences.ShowReasoning;
            m_ShowToolCalls = CodexPreferences.ShowToolCalls;
            m_CodexHome = CodexPreferences.CodexHome;
            CodexPreferences.Changed += OnPreferencesChanged;
            CodexPreferences.ReloadRequested += OnBackendReloadRequested;

            if (m_Workspace == null)
            {
                m_Workspace = new CodexWorkspace();
                m_Workspace.Changed += OnWorkspaceChanged;
                m_Workspace.TranscriptChanged += OnTranscriptChanged;
                m_Workspace.SettingsChanged += OnSettingsChanged;
                m_Workspace.ContextChanged += OnContextChanged;
                m_Workspace.InitialThreadId = EditorPrefs.GetString(LastThreadKey, string.Empty);
            }

            BuildUi();
            m_Workspace.Connect(ProjectRoot, m_CodexHome);
        }

        void OnDisable()
        {
            ClosePopup();
            CodexPreferences.Changed -= OnPreferencesChanged;
            CodexPreferences.ReloadRequested -= OnBackendReloadRequested;

            if (m_Workspace != null)
            {
                m_Workspace.Changed -= OnWorkspaceChanged;
                m_Workspace.TranscriptChanged -= OnTranscriptChanged;
                m_Workspace.SettingsChanged -= OnSettingsChanged;
                m_Workspace.ContextChanged -= OnContextChanged;
                m_Workspace.Dispose();
                m_Workspace = null;
            }
        }

        static string ProjectRoot => Path.GetDirectoryName(Application.dataPath);

        void BuildUi()
        {
            rootVisualElement.Clear();
            rootVisualElement.style.flexDirection = FlexDirection.Column;
            rootVisualElement.style.backgroundColor = CodexPalette.Window;

            rootVisualElement.Add(BuildToolbar());

            var main = new VisualElement();
            main.style.flexDirection = FlexDirection.Row;
            main.style.flexGrow = 1;
            rootVisualElement.Add(main);

            main.Add(BuildSidebar());
            main.Add(BuildContent());
            main.Add(BuildAgentsPanel());
        }

        VisualElement BuildToolbar()
        {
            var toolbar = new VisualElement();
            toolbar.style.flexDirection = FlexDirection.Row;
            toolbar.style.alignItems = Align.Center;
            toolbar.style.height = 48;
            toolbar.style.paddingLeft = 16;
            toolbar.style.paddingRight = 14;
            toolbar.style.backgroundColor = CodexPalette.Panel;
            toolbar.style.borderBottomWidth = 1;
            toolbar.style.borderBottomColor = CodexPalette.Border;

            var brandMark = new Label("◆");
            brandMark.style.fontSize = 16;
            brandMark.style.color = CodexPalette.Accent;
            brandMark.style.marginRight = 7;
            toolbar.Add(brandMark);

            var title = new Label("Codex");
            title.style.fontSize = 13;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.color = CodexPalette.Text;
            toolbar.Add(title);

            var subtitle = new Label("ASSISTANT");
            subtitle.style.fontSize = 8;
            subtitle.style.color = CodexPalette.TextDim;
            subtitle.style.marginLeft = 7;
            subtitle.style.marginTop = 2;
            toolbar.Add(subtitle);

            m_WorkingDot = new CodexPulseDot(CodexPalette.Warning);
            m_WorkingDot.style.marginLeft = 14;
            m_WorkingDot.style.marginRight = 5;
            m_WorkingDot.style.display = DisplayStyle.None;
            toolbar.Add(m_WorkingDot);

            m_StatusLabel = new Label("Starting...");
            m_StatusLabel.style.marginLeft = 4;
            m_StatusLabel.style.fontSize = 10;
            m_StatusLabel.style.color = CodexPalette.Muted;
            m_StatusLabel.style.flexGrow = 1;
            toolbar.Add(m_StatusLabel);

            m_RefreshButton = new Button(RefreshOrReconnect) { text = "Refresh" };
            StyleGhostButton(m_RefreshButton);
            toolbar.Add(m_RefreshButton);

            m_SettingsButton = new Button(CodexAssistantSettingsWindow.Open) { text = "Settings" };
            StyleGhostButton(m_SettingsButton);
            m_SettingsButton.tooltip = "Display and composer preferences";
            toolbar.Add(m_SettingsButton);

            m_AgentsButton = new Button(ToggleAgentsPanel) { text = "Agents" };
            StyleAccentSoftButton(m_AgentsButton);
            m_AgentsButton.style.marginLeft = 6;
            m_AgentsButton.style.flexShrink = 0;
            m_AgentsButton.style.display = DisplayStyle.None;
            m_AgentsButton.tooltip = "Show subagents for this conversation";
            toolbar.Add(m_AgentsButton);

            return toolbar;
        }

        static void StyleGhostButton(Button button, bool accent = false)
        {
            button.style.height = 28;
            button.style.paddingLeft = 10;
            button.style.paddingRight = 10;
            button.style.marginLeft = 4;
            button.style.borderTopWidth = 1;
            button.style.borderBottomWidth = 1;
            button.style.borderLeftWidth = 1;
            button.style.borderRightWidth = 1;
            button.style.borderTopColor = Color.clear;
            button.style.borderBottomColor = Color.clear;
            button.style.borderLeftColor = Color.clear;
            button.style.borderRightColor = Color.clear;
            button.style.borderTopLeftRadius = 7;
            button.style.borderTopRightRadius = 7;
            button.style.borderBottomLeftRadius = 7;
            button.style.borderBottomRightRadius = 7;
            button.style.backgroundColor = Color.clear;
            button.style.color = accent ? CodexPalette.Accent : CodexPalette.Muted;
            button.style.fontSize = 10;
            button.style.unityFontStyleAndWeight = FontStyle.Bold;
            button.RegisterCallback<MouseEnterEvent>(_ =>
            {
                if (button.enabledSelf)
                {
                    button.style.backgroundColor = CodexPalette.Hover;
                }
            });
            button.RegisterCallback<MouseLeaveEvent>(_ =>
            {
                button.style.backgroundColor = Color.clear;
            });
        }

        static void StyleAccentSoftButton(Button button)
        {
            button.style.height = 28;
            button.style.paddingLeft = 11;
            button.style.paddingRight = 11;
            button.style.marginLeft = 4;
            button.style.borderTopWidth = 0;
            button.style.borderBottomWidth = 0;
            button.style.borderLeftWidth = 0;
            button.style.borderRightWidth = 0;
            button.style.borderTopLeftRadius = 7;
            button.style.borderTopRightRadius = 7;
            button.style.borderBottomLeftRadius = 7;
            button.style.borderBottomRightRadius = 7;
            button.style.backgroundColor = CodexPalette.AccentSoft;
            button.style.color = CodexPalette.Accent;
            button.style.fontSize = 10;
            button.style.unityFontStyleAndWeight = FontStyle.Bold;
            button.RegisterCallback<MouseEnterEvent>(_ =>
            {
                if (button.enabledSelf)
                {
                    button.style.backgroundColor = CodexPalette.Selected;
                }
            });
            button.RegisterCallback<MouseLeaveEvent>(_ =>
            {
                button.style.backgroundColor = CodexPalette.AccentSoft;
            });
        }

        VisualElement BuildSidebar()
        {
            var sidebar = new VisualElement();
            sidebar.style.width = 292;
            sidebar.style.flexShrink = 0;
            sidebar.style.borderRightWidth = 1;
            sidebar.style.borderRightColor = CodexPalette.Border;
            sidebar.style.backgroundColor = CodexPalette.Sidebar;

            var filters = new VisualElement();
            filters.style.paddingLeft = 14;
            filters.style.paddingRight = 14;
            filters.style.paddingTop = 14;
            filters.style.paddingBottom = 11;
            filters.style.borderBottomWidth = 1;
            filters.style.borderBottomColor = CodexPalette.Border;

            var section = new Label("CONVERSATIONS");
            section.style.fontSize = 8;
            section.style.unityFontStyleAndWeight = FontStyle.Bold;
            section.style.color = CodexPalette.TextDim;
            section.style.marginLeft = 2;
            section.style.marginBottom = 8;
            filters.Add(section);

            m_NewButton = new Button(NewConversation) { text = "+  New conversation", name = "codex-new" };
            m_NewButton.style.height = 34;
            m_NewButton.style.marginBottom = 10;
            m_NewButton.style.paddingLeft = 12;
            m_NewButton.style.paddingRight = 12;
            m_NewButton.style.borderTopLeftRadius = 9;
            m_NewButton.style.borderTopRightRadius = 9;
            m_NewButton.style.borderBottomLeftRadius = 9;
            m_NewButton.style.borderBottomRightRadius = 9;
            m_NewButton.style.borderTopWidth = 0;
            m_NewButton.style.borderBottomWidth = 0;
            m_NewButton.style.borderLeftWidth = 0;
            m_NewButton.style.borderRightWidth = 0;
            m_NewButton.style.backgroundColor = CodexPalette.SendAction;
            m_NewButton.style.color = Color.white;
            m_NewButton.style.fontSize = 11;
            m_NewButton.style.unityFontStyleAndWeight = FontStyle.Bold;
            m_NewButton.style.unityTextAlign = TextAnchor.MiddleLeft;
            m_NewButton.tooltip = "Start a new conversation on the next message";
            m_NewButton.RegisterCallback<MouseEnterEvent>(_ =>
                m_NewButton.style.backgroundColor = CodexPalette.Accent);
            m_NewButton.RegisterCallback<MouseLeaveEvent>(_ =>
                m_NewButton.style.backgroundColor = CodexPalette.SendAction);
            filters.Add(m_NewButton);

            m_SearchField = new TextField { name = "codex-thread-search" };
            m_SearchField.style.height = 30;
            m_SearchField.style.marginBottom = 7;
            m_SearchField.textEdition.placeholder = "Search conversations...";
            m_SearchField.textEdition.hidePlaceholderOnFocus = false;
            StyleField(m_SearchField);
            m_SearchField.RegisterValueChangedCallback(evt => m_Workspace?.SetSearchTerm(evt.newValue));
            filters.Add(m_SearchField);

            m_ScopeField = new DropdownField { name = "codex-thread-scope" };
            m_ScopeField.choices = new List<string> { ScopeProject, ScopeAll };
            m_ScopeField.SetValueWithoutNotify(ScopeProject);
            m_ScopeField.style.height = 28;
            m_ScopeField.style.marginTop = 0;
            m_ScopeField.style.backgroundColor = CodexPalette.Pill;
            m_ScopeField.style.borderTopLeftRadius = 7;
            m_ScopeField.style.borderTopRightRadius = 7;
            m_ScopeField.style.borderBottomLeftRadius = 7;
            m_ScopeField.style.borderBottomRightRadius = 7;
            m_ScopeField.style.paddingLeft = 8;
            m_ScopeField.style.color = CodexPalette.Muted;
            m_ScopeField.tooltip = "Show conversations from this project or from every folder";
            m_ScopeField.RegisterValueChangedCallback(evt =>
                m_Workspace?.SetShowAllProjects(evt.newValue == ScopeAll));
            filters.Add(m_ScopeField);
            sidebar.Add(filters);

            m_ThreadList = new ScrollView(ScrollViewMode.Vertical);
            m_ThreadList.style.flexGrow = 1;
            m_ThreadList.style.paddingTop = 6;
            m_ThreadList.style.paddingLeft = 8;
            m_ThreadList.style.paddingRight = 8;
            m_ThreadList.style.overflow = Overflow.Hidden;
            m_ThreadList.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            m_ThreadList.contentContainer.style.width = Length.Percent(100);
            sidebar.Add(m_ThreadList);

            return sidebar;
        }

        static void StyleField(TextField field)
        {
            field.style.backgroundColor = CodexPalette.Pill;
            field.style.borderTopWidth = 1;
            field.style.borderBottomWidth = 1;
            field.style.borderLeftWidth = 1;
            field.style.borderRightWidth = 1;
            field.style.borderTopColor = CodexPalette.Border;
            field.style.borderBottomColor = CodexPalette.Border;
            field.style.borderLeftColor = CodexPalette.Border;
            field.style.borderRightColor = CodexPalette.Border;
            field.style.borderTopLeftRadius = 8;
            field.style.borderTopRightRadius = 8;
            field.style.borderBottomLeftRadius = 8;
            field.style.borderBottomRightRadius = 8;
            field.style.paddingLeft = 8;
            field.style.paddingRight = 8;
        }

        VisualElement BuildContent()
        {
            var content = new VisualElement();
            content.style.flexGrow = 1;
            content.style.flexDirection = FlexDirection.Column;

            var header = new VisualElement();
            header.style.flexDirection = FlexDirection.Row;
            header.style.alignItems = Align.Center;
            header.style.paddingLeft = 18;
            header.style.paddingRight = 14;
            header.style.paddingTop = 11;
            header.style.paddingBottom = 11;
            header.style.backgroundColor = CodexPalette.Window;
            header.style.borderBottomWidth = 1;
            header.style.borderBottomColor = CodexPalette.Border;
            content.Add(header);

            var headerText = new VisualElement();
            headerText.style.flexGrow = 1;
            header.Add(headerText);

            m_HeaderTitle = new Label("No conversation selected");
            m_HeaderTitle.style.whiteSpace = WhiteSpace.Normal;
            m_HeaderTitle.style.fontSize = 15;
            m_HeaderTitle.style.unityFontStyleAndWeight = FontStyle.Bold;
            m_HeaderTitle.style.color = CodexPalette.Text;
            headerText.Add(m_HeaderTitle);

            m_HeaderMeta = new Label(string.Empty);
            m_HeaderMeta.style.fontSize = 10;
            m_HeaderMeta.style.color = CodexPalette.Muted;
            m_HeaderMeta.style.whiteSpace = WhiteSpace.Normal;
            m_HeaderMeta.style.marginTop = 2;
            headerText.Add(m_HeaderMeta);

            m_BackButton = new Button(GoToParentThread) { text = "← Parent" };
            m_BackButton.style.marginLeft = 8;
            m_BackButton.style.display = DisplayStyle.None;
            m_BackButton.tooltip = "Return to the parent agent conversation";
            StyleAccentSoftButton(m_BackButton);
            header.Add(m_BackButton);

            m_ArchiveButton = new Button(ArchiveSelectedThread) { text = "Archive" };
            m_ArchiveButton.style.marginLeft = 4;
            m_ArchiveButton.SetEnabled(false);
            m_ArchiveButton.tooltip = "Archive the selected conversation";
            StyleGhostButton(m_ArchiveButton);
            header.Add(m_ArchiveButton);

            m_CopyButton = new Button(CopyResumeCommand) { text = "Copy resume" };
            m_CopyButton.style.marginLeft = 4;
            m_CopyButton.SetEnabled(false);
            StyleGhostButton(m_CopyButton);
            header.Add(m_CopyButton);

            m_ForkButton = new Button(ForkConversation) { text = "Fork" };
            m_ForkButton.style.marginLeft = 4;
            m_ForkButton.SetEnabled(false);
            m_ForkButton.tooltip = "Copy this conversation into a new thread and open it";
            StyleGhostButton(m_ForkButton);
            header.Add(m_ForkButton);

            m_Transcript = new ScrollView(ScrollViewMode.Vertical);
            m_Transcript.style.flexGrow = 1;
            m_Transcript.style.paddingTop = 14;
            m_Transcript.style.paddingLeft = 18;
            m_Transcript.style.paddingRight = 18;
            m_Transcript.verticalScrollerVisibility = ScrollerVisibility.Auto;
            m_Transcript.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            m_Transcript.verticalScroller.valueChanged += _ =>
                m_FollowTranscript = IsTranscriptAtBottom();
            content.Add(m_Transcript);

            m_QuestionPanel = new VisualElement();
            m_QuestionPanel.style.flexShrink = 0;
            m_QuestionPanel.style.display = DisplayStyle.None;
            content.Add(m_QuestionPanel);

            content.Add(BuildComposer());

            m_ErrorLabel = new Label(string.Empty);
            m_ErrorLabel.style.display = DisplayStyle.None;
            m_ErrorLabel.style.whiteSpace = WhiteSpace.Normal;
            m_ErrorLabel.style.color = CodexPalette.Error;
            m_ErrorLabel.style.fontSize = 10;
            m_ErrorLabel.style.paddingLeft = 12;
            m_ErrorLabel.style.paddingRight = 12;
            m_ErrorLabel.style.paddingTop = 6;
            m_ErrorLabel.style.paddingBottom = 6;
            m_ErrorLabel.style.borderTopWidth = 1;
            m_ErrorLabel.style.borderTopColor = CodexPalette.Border;
            content.Add(m_ErrorLabel);

            return content;
        }

        VisualElement BuildAgentsPanel()
        {
            m_AgentsPanel = new VisualElement { name = "codex-agents" };
            m_AgentsPanel.style.width = 250;
            m_AgentsPanel.style.flexShrink = 0;
            m_AgentsPanel.style.flexDirection = FlexDirection.Column;
            m_AgentsPanel.style.backgroundColor = CodexPalette.Sidebar;
            m_AgentsPanel.style.borderLeftWidth = 1;
            m_AgentsPanel.style.borderLeftColor = CodexPalette.Border;
            m_AgentsPanel.style.display = DisplayStyle.None;

            var header = new VisualElement();
            header.style.flexDirection = FlexDirection.Row;
            header.style.alignItems = Align.Center;
            header.style.paddingLeft = 14;
            header.style.paddingRight = 10;
            header.style.paddingTop = 14;
            header.style.paddingBottom = 10;
            header.style.borderBottomWidth = 1;
            header.style.borderBottomColor = CodexPalette.Border;

            var title = new Label("SUBAGENTS");
            title.style.fontSize = 8;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.color = CodexPalette.TextDim;
            header.Add(title);

            m_AgentsCountLabel = new Label(string.Empty);
            m_AgentsCountLabel.style.fontSize = 9;
            m_AgentsCountLabel.style.color = CodexPalette.Muted;
            m_AgentsCountLabel.style.marginLeft = 6;
            m_AgentsCountLabel.style.flexGrow = 1;
            header.Add(m_AgentsCountLabel);

            var collapse = new Button(() =>
            {
                m_AgentsPanelCollapsed = true;
                m_AgentsSignature = null;
                RefreshAgentsPanel();
            })
            {
                text = "×",
            };
            collapse.tooltip = "Hide subagents";
            collapse.style.width = 18;
            collapse.style.height = 18;
            collapse.style.minWidth = 18;
            collapse.style.paddingLeft = 0;
            collapse.style.paddingRight = 0;
            collapse.style.borderTopWidth = 0;
            collapse.style.borderBottomWidth = 0;
            collapse.style.borderLeftWidth = 0;
            collapse.style.borderRightWidth = 0;
            collapse.style.backgroundColor = Color.clear;
            collapse.style.color = CodexPalette.Muted;
            header.Add(collapse);

            m_AgentsList = new ScrollView(ScrollViewMode.Vertical);
            m_AgentsList.style.flexGrow = 1;
            m_AgentsList.style.paddingLeft = 9;
            m_AgentsList.style.paddingRight = 9;
            m_AgentsList.style.paddingTop = 8;
            m_AgentsList.horizontalScrollerVisibility = ScrollerVisibility.Hidden;

            m_AgentsPanel.Add(header);
            m_AgentsPanel.Add(m_AgentsList);
            m_AgentsSignature = null;
            return m_AgentsPanel;
        }

        void ToggleAgentsPanel()
        {
            m_AgentsPanelCollapsed = !m_AgentsPanelCollapsed;
            m_AgentsSignature = null;
            RefreshAgentsPanel();
        }

        void RefreshAgentsPanel()
        {
            if (m_AgentsPanel == null || m_AgentsButton == null)
            {
                return;
            }

            IReadOnlyList<CodexSubagentInfo> agents = m_Workspace?.Subagents;
            int count = agents?.Count ?? 0;
            int running = 0;
            if (agents != null)
            {
                foreach (CodexSubagentInfo agent in agents)
                {
                    if (agent.IsRunning)
                    {
                        running++;
                    }
                }
            }

            m_AgentsButton.style.display = count > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            if (count > 0)
            {
                m_AgentsButton.text = running > 0 ? $"Agents {count} · {running} running" : $"Agents {count}";
                m_AgentsButton.tooltip = m_AgentsPanelCollapsed ? "Show subagents" : "Hide subagents";
            }

            bool visible = count > 0 && !m_AgentsPanelCollapsed;
            m_AgentsPanel.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            if (!visible)
            {
                return;
            }

            m_AgentsCountLabel.text = running > 0
                ? $"{running} running · {count}"
                : count.ToString();

            var signature = new System.Text.StringBuilder();
            foreach (CodexSubagentInfo agent in agents)
            {
                signature.Append(agent.ThreadId).Append('|')
                    .Append(agent.Status).Append('|')
                    .Append(agent.Activity).Append('|')
                    .Append(agent.Path).Append('|')
                    .Append(agent.Model).Append('|')
                    .Append(agent.Effort).Append('\n');
            }

            string key = signature.ToString();
            if (key == m_AgentsSignature)
            {
                return;
            }

            m_AgentsSignature = key;
            m_AgentsList.Clear();
            foreach (CodexSubagentInfo agent in agents)
            {
                AddAgentRow(agent);
            }
        }

        void AddAgentRow(CodexSubagentInfo agent)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.FlexStart;
            row.style.paddingLeft = 9;
            row.style.paddingRight = 8;
            row.style.paddingTop = 9;
            row.style.paddingBottom = 9;
            row.style.marginBottom = 6;
            row.style.borderTopWidth = 1;
            row.style.borderBottomWidth = 1;
            row.style.borderLeftWidth = 1;
            row.style.borderRightWidth = 1;
            row.style.borderTopColor = CodexPalette.Border;
            row.style.borderBottomColor = CodexPalette.Border;
            row.style.borderLeftColor = CodexPalette.Border;
            row.style.borderRightColor = CodexPalette.Border;
            row.style.borderTopLeftRadius = 8;
            row.style.borderTopRightRadius = 8;
            row.style.borderBottomLeftRadius = 8;
            row.style.borderBottomRightRadius = 8;
            row.style.backgroundColor = CodexPalette.Surface;
            row.tooltip = "Open this subagent";
            row.RegisterCallback<MouseEnterEvent>(_ => row.style.backgroundColor = CodexPalette.Hover);
            row.RegisterCallback<MouseLeaveEvent>(_ => row.style.backgroundColor = CodexPalette.Surface);

            Color statusColor = agent.Status switch
            {
                "completed" => CodexPalette.Success,
                "failed" => CodexPalette.Error,
                "interrupted" => CodexPalette.Warning,
                "running" => CodexPalette.Warning,
                _ => CodexPalette.Muted,
            };

            var dot = new CodexPulseDot(statusColor, 7f, agent.IsRunning);
            dot.style.marginTop = 4;
            dot.style.marginRight = 7;
            row.Add(dot);

            var body = new VisualElement();
            body.style.flexGrow = 1;
            body.style.flexShrink = 1;
            row.Add(body);

            string name = string.IsNullOrEmpty(agent.Path)
                ? ShortId(agent.ThreadId)
                : agent.Path;
            var nameLabel = new Label(name);
            nameLabel.style.fontSize = 10;
            nameLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            nameLabel.style.color = CodexPalette.Text;
            nameLabel.style.whiteSpace = WhiteSpace.Normal;
            body.Add(nameLabel);

            string meta = agent.Status;
            if (!string.IsNullOrEmpty(agent.Model))
            {
                meta += $"  ·  {agent.Model}";
            }

            var metaLabel = new Label(meta);
            metaLabel.style.fontSize = 9;
            metaLabel.style.color = statusColor;
            metaLabel.style.whiteSpace = WhiteSpace.Normal;
            body.Add(metaLabel);

            if (!string.IsNullOrEmpty(agent.Activity) &&
                !string.Equals(agent.Activity, agent.Status, StringComparison.OrdinalIgnoreCase))
            {
                var activity = new Label(agent.Activity);
                activity.style.fontSize = 9;
                activity.style.color = CodexPalette.Muted;
                activity.style.whiteSpace = WhiteSpace.Normal;
                activity.style.marginTop = 2;
                body.Add(activity);
            }

            row.RegisterCallback<ClickEvent>(_ => m_Workspace?.SelectThread(agent.ThreadId));
            m_AgentsList.Add(row);
        }

        static string ShortId(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                return "subagent";
            }

            return id.Length <= 14 ? id : $"{id.Substring(0, 8)}…";
        }

        VisualElement BuildComposer()
        {
            var composer = new VisualElement();
            composer.style.flexShrink = 0;
            composer.style.backgroundColor = CodexPalette.Panel;
            composer.style.borderTopWidth = 1;
            composer.style.borderTopColor = CodexPalette.Border;
            composer.style.paddingLeft = 18;
            composer.style.paddingRight = 18;
            composer.style.paddingTop = 11;
            composer.style.paddingBottom = 11;

            m_ComposerCard = new VisualElement();
            m_ComposerCard.style.flexDirection = FlexDirection.Column;
            m_ComposerCard.style.backgroundColor = CodexPalette.Surface;
            m_ComposerCard.style.borderTopWidth = 1;
            m_ComposerCard.style.borderBottomWidth = 1;
            m_ComposerCard.style.borderLeftWidth = 1;
            m_ComposerCard.style.borderRightWidth = 1;
            m_ComposerCard.style.borderTopColor = CodexPalette.Border;
            m_ComposerCard.style.borderBottomColor = CodexPalette.Border;
            m_ComposerCard.style.borderLeftColor = CodexPalette.Border;
            m_ComposerCard.style.borderRightColor = CodexPalette.Border;
            m_ComposerCard.style.borderTopLeftRadius = 14;
            m_ComposerCard.style.borderTopRightRadius = 14;
            m_ComposerCard.style.borderBottomLeftRadius = 14;
            m_ComposerCard.style.borderBottomRightRadius = 14;
            m_ComposerCard.style.paddingLeft = 11;
            m_ComposerCard.style.paddingRight = 11;
            m_ComposerCard.style.paddingTop = 9;
            m_ComposerCard.style.paddingBottom = 9;

            m_QueueStrip = new VisualElement { name = "codex-queue" };
            m_QueueStrip.style.flexDirection = FlexDirection.Column;
            m_QueueStrip.style.marginBottom = 6;
            m_QueueStrip.style.display = DisplayStyle.None;
            composer.Add(m_QueueStrip);

            composer.Add(m_ComposerCard);

            m_Composer = new TextField { multiline = true, name = "codex-composer" };
            m_Composer.style.flexGrow = 1;
            m_Composer.style.minHeight = 54;
            m_Composer.style.maxHeight = 180;
            m_Composer.style.backgroundColor = Color.clear;
            m_Composer.style.borderTopWidth = 0;
            m_Composer.style.borderBottomWidth = 0;
            m_Composer.style.borderLeftWidth = 0;
            m_Composer.style.borderRightWidth = 0;
            m_Composer.style.marginLeft = 2;
            m_Composer.style.marginRight = 2;
            m_Composer.textEdition.placeholder = "Message Codex...";
            m_Composer.textEdition.hidePlaceholderOnFocus = true;
            m_Composer.tooltip = "Enter to send · Shift+Enter for a new line";
            m_Composer.RegisterCallback<KeyDownEvent>(OnComposerKeyDown, TrickleDown.TrickleDown);
            m_Composer.RegisterValueChangedCallback(_ => UpdateComposer());
            m_Composer.RegisterCallback<FocusInEvent>(_ => SetComposerFocus(true));
            m_Composer.RegisterCallback<FocusOutEvent>(_ => SetComposerFocus(false));
            m_Composer.RegisterCallback<AttachToPanelEvent>(_ => StyleComposerInput());
            m_Composer.schedule.Execute(StyleComposerInput);
            m_ComposerCard.Add(m_Composer);

            var meta = new VisualElement();
            meta.style.flexDirection = FlexDirection.Row;
            meta.style.alignItems = Align.Center;
            meta.style.marginTop = 2;
            m_ComposerCard.Add(meta);

            m_ComposerHint = new Label("Enter to send  ·  Shift+Enter for a new line");
            m_ComposerHint.style.fontSize = 9;
            m_ComposerHint.style.color = CodexPalette.TextDim;
            m_ComposerHint.style.flexGrow = 1;
            meta.Add(m_ComposerHint);

            m_SettingsSourceLabel = new Label(string.Empty);
            m_SettingsSourceLabel.name = "codex-settings-source";
            m_SettingsSourceLabel.style.fontSize = 9;
            m_SettingsSourceLabel.style.color = CodexPalette.TextDim;
            m_SettingsSourceLabel.style.marginLeft = 10;
            m_SettingsSourceLabel.style.whiteSpace = WhiteSpace.Normal;
            m_SettingsSourceLabel.style.unityTextAlign = TextAnchor.MiddleRight;
            meta.Add(m_SettingsSourceLabel);

            var actions = new VisualElement();
            actions.style.flexDirection = FlexDirection.Row;
            actions.style.alignItems = Align.Center;
            actions.style.marginTop = 7;
            m_ComposerCard.Add(actions);

            m_SandboxButton = AddPill(actions, "codex-sandbox", "Sandbox");
            m_SandboxButton.clicked += ShowSandboxMenu;

            var spacer = new VisualElement();
            spacer.style.flexGrow = 1;
            actions.Add(spacer);

            m_ContextIndicator = new VisualElement { name = "codex-context" };
            m_ContextIndicator.style.flexDirection = FlexDirection.Row;
            m_ContextIndicator.style.alignItems = Align.Center;
            m_ContextIndicator.style.height = 24;
            m_ContextIndicator.style.paddingLeft = 7;
            m_ContextIndicator.style.paddingRight = 8;
            m_ContextIndicator.style.marginRight = 6;
            m_ContextIndicator.style.borderTopWidth = 1;
            m_ContextIndicator.style.borderBottomWidth = 1;
            m_ContextIndicator.style.borderLeftWidth = 1;
            m_ContextIndicator.style.borderRightWidth = 1;
            m_ContextIndicator.style.borderTopColor = CodexPalette.PillBorder;
            m_ContextIndicator.style.borderBottomColor = CodexPalette.PillBorder;
            m_ContextIndicator.style.borderLeftColor = CodexPalette.PillBorder;
            m_ContextIndicator.style.borderRightColor = CodexPalette.PillBorder;
            m_ContextIndicator.style.borderTopLeftRadius = 12;
            m_ContextIndicator.style.borderTopRightRadius = 12;
            m_ContextIndicator.style.borderBottomLeftRadius = 12;
            m_ContextIndicator.style.borderBottomRightRadius = 12;
            m_ContextIndicator.style.backgroundColor = CodexPalette.Pill;
            m_ContextIndicator.style.display = DisplayStyle.None;
            m_ContextIndicator.tooltip = "Context window usage for the selected conversation";

            m_ContextDot = new VisualElement();
            m_ContextDot.style.width = 7;
            m_ContextDot.style.height = 7;
            m_ContextDot.style.marginRight = 5;
            m_ContextDot.style.borderTopLeftRadius = 4;
            m_ContextDot.style.borderTopRightRadius = 4;
            m_ContextDot.style.borderBottomLeftRadius = 4;
            m_ContextDot.style.borderBottomRightRadius = 4;
            m_ContextDot.style.backgroundColor = CodexPalette.Muted;
            m_ContextIndicator.Add(m_ContextDot);

            m_ContextLabel = new Label("ctx —");
            m_ContextLabel.style.fontSize = 9;
            m_ContextLabel.style.color = CodexPalette.Muted;
            m_ContextIndicator.Add(m_ContextLabel);
            actions.Add(m_ContextIndicator);

            m_ModelButton = AddPill(actions, "codex-model", "Model");
            m_ModelButton.clicked += ShowModelMenu;

            m_EffortButton = AddPill(actions, "codex-effort", "Reasoning effort");
            m_EffortButton.clicked += ShowEffortMenu;

            m_ActionButton = new Button(OnComposerAction) { text = "Send", name = "codex-action" };
            m_ActionButton.style.height = 30;
            m_ActionButton.style.minWidth = 70;
            m_ActionButton.style.paddingLeft = 14;
            m_ActionButton.style.paddingRight = 14;
            m_ActionButton.style.marginLeft = 4;
            m_ActionButton.style.borderTopLeftRadius = 8;
            m_ActionButton.style.borderTopRightRadius = 8;
            m_ActionButton.style.borderBottomLeftRadius = 8;
            m_ActionButton.style.borderBottomRightRadius = 8;
            m_ActionButton.style.borderTopWidth = 0;
            m_ActionButton.style.borderBottomWidth = 0;
            m_ActionButton.style.borderLeftWidth = 0;
            m_ActionButton.style.borderRightWidth = 0;
            m_ActionButton.style.color = Color.white;
            m_ActionButton.style.fontSize = 10;
            m_ActionButton.style.unityFontStyleAndWeight = FontStyle.Bold;
            actions.Add(m_ActionButton);

            UpdateSettingsBar();
            UpdateComposer();
            UpdateContextIndicator();

            return composer;
        }

        Button AddPill(VisualElement parent, string name, string tooltip)
        {
            var button = new Button { name = name, tooltip = tooltip };
            button.style.height = 26;
            button.style.minWidth = 28;
            button.style.paddingLeft = 10;
            button.style.paddingRight = 10;
            button.style.paddingTop = 2;
            button.style.paddingBottom = 2;
            button.style.marginRight = 6;
            button.style.borderTopLeftRadius = 8;
            button.style.borderTopRightRadius = 8;
            button.style.borderBottomLeftRadius = 8;
            button.style.borderBottomRightRadius = 8;
            button.style.borderTopWidth = 1;
            button.style.borderBottomWidth = 1;
            button.style.borderLeftWidth = 1;
            button.style.borderRightWidth = 1;
            button.style.borderTopColor = CodexPalette.PillBorder;
            button.style.borderBottomColor = CodexPalette.PillBorder;
            button.style.borderLeftColor = CodexPalette.PillBorder;
            button.style.borderRightColor = CodexPalette.PillBorder;
            button.style.backgroundColor = CodexPalette.Pill;
            button.style.color = CodexPalette.Text;
            button.style.fontSize = 10;
            button.style.unityTextAlign = TextAnchor.MiddleLeft;
            button.RegisterCallback<MouseEnterEvent>(_ =>
            {
                if (button.enabledSelf)
                {
                    button.style.backgroundColor = CodexPalette.PillHover;
                }
            });
            button.RegisterCallback<MouseLeaveEvent>(_ =>
            {
                button.style.backgroundColor = CodexPalette.Pill;
            });
            parent.Add(button);
            return button;
        }

        void StyleComposerInput()
        {
            if (m_Composer == null)
            {
                return;
            }

            VisualElement input = m_Composer.Q(className: "unity-base-text-field__input")
                                  ?? m_Composer.Q(className: "unity-text-input");
            if (input != null)
            {
                input.style.backgroundColor = Color.clear;
                input.style.borderTopWidth = 0;
                input.style.borderBottomWidth = 0;
                input.style.borderLeftWidth = 0;
                input.style.borderRightWidth = 0;
            }

            m_Composer.style.backgroundColor = Color.clear;
        }

        void SetComposerFocus(bool focused)
        {
            if (m_ComposerCard == null)
            {
                return;
            }

            Color color = focused ? CodexPalette.Selected : CodexPalette.PillBorder;
            m_ComposerCard.style.borderTopColor = color;
            m_ComposerCard.style.borderBottomColor = color;
            m_ComposerCard.style.borderLeftColor = color;
            m_ComposerCard.style.borderRightColor = color;
        }

        void ShowModelMenu()
        {
            if (m_Workspace == null)
            {
                return;
            }

            var items = new List<PopupItem>();
            bool hasSelected = false;
            foreach (CodexModelInfo model in m_Workspace.Models)
            {
                bool selected = model.Matches(m_Workspace.SelectedModel);
                if (model.Hidden && !selected)
                {
                    continue;
                }

                hasSelected |= selected;
                string id = model.Id;
                items.Add(new PopupItem
                {
                    Label = string.IsNullOrEmpty(model.DisplayName) ? id : model.DisplayName,
                    Selected = selected,
                    Action = () => m_Workspace?.SetModel(id),
                });
            }

            if (!hasSelected && !string.IsNullOrEmpty(m_Workspace.SelectedModel))
            {
                items.Insert(0, new PopupItem
                {
                    Label = m_Workspace.SelectedModel,
                    Selected = true,
                    Action = () => { },
                });
            }
            else if (items.Count == 0)
            {
                items.Add(new PopupItem
                {
                    Label = "No models available",
                    Selected = true,
                    Action = () => { },
                });
            }

            ShowPopup(m_ModelButton, "Model", 300, items, true);
        }

        void ShowEffortMenu()
        {
            if (m_Workspace == null)
            {
                return;
            }

            var items = new List<PopupItem>();
            foreach (string effort in m_Workspace.Efforts)
            {
                string value = effort;
                items.Add(new PopupItem
                {
                    Label = FormatEffort(value),
                    Selected = value == m_Workspace.SelectedEffort,
                    Action = () => m_Workspace?.SetEffort(value),
                });
            }

            if (items.Count == 0)
            {
                items.Add(new PopupItem { Label = "No reasoning levels", Selected = true, Action = () => { } });
            }

            ShowPopup(m_EffortButton, "Reasoning effort", 220, items, false);
        }

        void ShowSandboxMenu()
        {
            if (m_Workspace == null)
            {
                return;
            }

            var items = new List<PopupItem>
            {
                new PopupItem
                {
                    Label = "Server default",
                    Selected = string.IsNullOrEmpty(m_Workspace.SelectedSandbox),
                    Action = () => m_Workspace?.SetSandbox(string.Empty),
                },
                new PopupItem
                {
                    Label = "Read only",
                    Selected = m_Workspace.SelectedSandbox == "read-only",
                    Action = () => m_Workspace?.SetSandbox("read-only"),
                },
                new PopupItem
                {
                    Label = "Workspace write",
                    Selected = m_Workspace.SelectedSandbox == "workspace-write",
                    Action = () => m_Workspace?.SetSandbox("workspace-write"),
                },
                new PopupItem
                {
                    Label = "Full access",
                    Selected = m_Workspace.SelectedSandbox == "danger-full-access",
                    Action = () => m_Workspace?.SetSandbox("danger-full-access"),
                },
            };

            ShowPopup(m_SandboxButton, "Sandbox", 240, items, false);
        }

        void ShowPopup(
            VisualElement anchor,
            string title,
            float width,
            List<PopupItem> items,
            bool searchable,
            Vector2? at = null)
        {
            ClosePopup();
            if (anchor == null && !at.HasValue)
            {
                return;
            }

            m_PopupScrim = new VisualElement { name = "codex-popup-scrim", pickingMode = PickingMode.Position };
            m_PopupScrim.style.position = Position.Absolute;
            m_PopupScrim.style.left = 0;
            m_PopupScrim.style.top = 0;
            m_PopupScrim.style.right = 0;
            m_PopupScrim.style.bottom = 0;
            m_PopupScrim.style.backgroundColor = new Color(0f, 0f, 0f, 0.001f);
            m_PopupScrim.RegisterCallback<MouseDownEvent>(_ => ClosePopup());
            rootVisualElement.Add(m_PopupScrim);

            m_Popup = new VisualElement { name = "codex-popup", pickingMode = PickingMode.Position };
            m_Popup.style.position = Position.Absolute;
            m_Popup.style.width = width;
            m_Popup.style.backgroundColor = CodexPalette.Popover;
            m_Popup.style.borderTopWidth = 1;
            m_Popup.style.borderBottomWidth = 1;
            m_Popup.style.borderLeftWidth = 1;
            m_Popup.style.borderRightWidth = 1;
            m_Popup.style.borderTopColor = CodexPalette.PillBorder;
            m_Popup.style.borderBottomColor = CodexPalette.PillBorder;
            m_Popup.style.borderLeftColor = CodexPalette.PillBorder;
            m_Popup.style.borderRightColor = CodexPalette.PillBorder;
            m_Popup.style.borderTopLeftRadius = 10;
            m_Popup.style.borderTopRightRadius = 10;
            m_Popup.style.borderBottomLeftRadius = 10;
            m_Popup.style.borderBottomRightRadius = 10;
            m_Popup.style.paddingLeft = 8;
            m_Popup.style.paddingRight = 8;
            m_Popup.style.paddingTop = 8;
            m_Popup.style.paddingBottom = 8;
            rootVisualElement.Add(m_Popup);

            var header = new Label(title);
            header.style.fontSize = 10;
            header.style.unityFontStyleAndWeight = FontStyle.Bold;
            header.style.color = CodexPalette.Muted;
            header.style.marginBottom = 4;
            m_Popup.Add(header);

            TextField search = null;
            if (searchable)
            {
                search = new TextField { name = "codex-popup-search" };
                search.style.height = 24;
                search.style.marginBottom = 4;
                search.textEdition.placeholder = $"Search {title.ToLowerInvariant()}";
                search.textEdition.hidePlaceholderOnFocus = false;
                m_Popup.Add(search);
            }

            int visible = Mathf.Clamp(items.Count, 1, 8);
            float listHeight = visible * 26f;
            var list = new ScrollView(ScrollViewMode.Vertical);
            list.style.height = listHeight;
            list.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            list.verticalScrollerVisibility = ScrollerVisibility.Auto;
            m_Popup.Add(list);
            FillPopupList(list, items, string.Empty);

            if (search != null)
            {
                search.RegisterValueChangedCallback(evt => FillPopupList(list, items, evt.newValue));
                search.schedule.Execute(() => search.Focus());
            }

            m_Popup.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode == KeyCode.Escape)
                {
                    ClosePopup();
                    evt.StopPropagation();
                }
            }, TrickleDown.TrickleDown);

            float popupHeight = 8 + 16 + 4 + (searchable ? 28 : 0) + listHeight + 8;
            m_Popup.style.height = popupHeight;

            Rect rootBounds = rootVisualElement.worldBound;
            float left;
            float top;
            if (at.HasValue)
            {
                left = at.Value.x - rootBounds.x + 2f;
                top = at.Value.y - rootBounds.y + 2f;
            }
            else
            {
                Rect anchorBounds = anchor.worldBound;
                left = anchorBounds.x - rootBounds.x;
                top = anchorBounds.y - rootBounds.y - popupHeight - 6f;
            }

            left = Mathf.Clamp(left, 8f, Mathf.Max(8f, rootVisualElement.layout.width - width - 8f));
            top = Mathf.Clamp(top, 8f, Mathf.Max(8f, rootVisualElement.layout.height - popupHeight - 8f));
            m_Popup.style.left = left;
            m_Popup.style.top = top;
        }

        void FillPopupList(ScrollView list, List<PopupItem> items, string query)
        {
            list.Clear();
            int matches = 0;
            foreach (PopupItem item in items)
            {
                if (!string.IsNullOrEmpty(query) &&
                    item.Label.IndexOf(query, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                PopupItem captured = item;
                var row = new Button(() =>
                {
                    ClosePopup();
                    captured.Action?.Invoke();
                })
                {
                    text = (captured.Selected ? "✓  " : "     ") + captured.Label,
                };

                Color background = captured.Selected
                    ? new Color(CodexPalette.Selected.r, CodexPalette.Selected.g, CodexPalette.Selected.b, 0.5f)
                    : Color.clear;
                row.style.height = 26;
                row.style.marginTop = 0;
                row.style.marginBottom = 0;
                row.style.marginLeft = 0;
                row.style.marginRight = 0;
                row.style.paddingLeft = 6;
                row.style.paddingRight = 6;
                row.style.backgroundColor = background;
                row.style.color = CodexPalette.Text;
                row.style.fontSize = 11;
                row.style.unityTextAlign = TextAnchor.MiddleLeft;
                row.style.borderTopLeftRadius = 5;
                row.style.borderTopRightRadius = 5;
                row.style.borderBottomLeftRadius = 5;
                row.style.borderBottomRightRadius = 5;
                row.style.borderTopWidth = 0;
                row.style.borderBottomWidth = 0;
                row.style.borderLeftWidth = 0;
                row.style.borderRightWidth = 0;
                row.RegisterCallback<MouseEnterEvent>(_ =>
                {
                    if (!captured.Selected)
                    {
                        row.style.backgroundColor = CodexPalette.PopoverHover;
                    }
                });
                row.RegisterCallback<MouseLeaveEvent>(_ => row.style.backgroundColor = background);
                list.Add(row);
                matches++;
            }

            if (matches == 0)
            {
                var empty = new Label("No matches");
                empty.style.fontSize = 10;
                empty.style.color = CodexPalette.Muted;
                empty.style.paddingLeft = 6;
                empty.style.paddingTop = 6;
                list.Add(empty);
            }
        }

        void ClosePopup()
        {
            m_Popup?.RemoveFromHierarchy();
            m_Popup = null;
            m_PopupScrim?.RemoveFromHierarchy();
            m_PopupScrim = null;
        }

        static string FormatEffort(string effort)
        {
            if (string.IsNullOrEmpty(effort))
            {
                return "Default";
            }

            return char.ToUpperInvariant(effort[0]) + effort.Substring(1);
        }

        static string FormatSandbox(string sandbox)
        {
            return sandbox switch
            {
                null => "Server default",
                "read-only" => "Read only",
                "workspace-write" => "Workspace write",
                "danger-full-access" => "Full access",
                _ => sandbox,
            };
        }

        void RefreshOrReconnect()
        {
            if (m_Workspace == null)
            {
                return;
            }

            if (m_Workspace.IsConnected)
            {
                m_Workspace.RefreshThreads();
            }
            else
            {
                m_Workspace.Connect(ProjectRoot, m_CodexHome);
            }
        }

        void OnWorkspaceChanged()
        {
            if (m_Workspace == null)
            {
                return;
            }

            string status = m_Workspace.Status;
            m_StatusLabel.text = status;
            m_StatusLabel.style.color = string.IsNullOrEmpty(m_Workspace.Error) ? CodexPalette.Muted : CodexPalette.Error;
            m_RefreshButton.text = m_Workspace.IsConnected ? "Refresh" : "Reconnect";

            UpdateSidebarFilters();
            RebuildThreadList();
            RebuildTranscript();
            UpdateHeader();
            UpdateSettingsBar();
            UpdateComposer();
            RefreshQueueStrip();
            RefreshInteractionPanel();
            RefreshAgentsPanel();
            UpdateContextIndicator();

            bool hasError = !string.IsNullOrEmpty(m_Workspace.Error);
            m_ErrorLabel.style.display = hasError ? DisplayStyle.Flex : DisplayStyle.None;
            m_ErrorLabel.text = hasError ? m_Workspace.Error : string.Empty;

            if (!string.IsNullOrEmpty(m_Workspace.SelectedThreadId))
            {
                EditorPrefs.SetString(LastThreadKey, m_Workspace.SelectedThreadId);
            }
        }

        void OnSettingsChanged()
        {
            UpdateSettingsBar();
            UpdateComposer();
            UpdateContextIndicator();
        }

        void OnContextChanged()
        {
            UpdateContextIndicator();
        }

        void OnPreferencesChanged()
        {
            m_ShowContextIndicator = CodexPreferences.ShowContextIndicator;
            m_ShowReasoning = CodexPreferences.ShowReasoning;
            m_ShowToolCalls = CodexPreferences.ShowToolCalls;

            string home = CodexPreferences.CodexHome;
            if (!string.Equals(home, m_CodexHome, StringComparison.OrdinalIgnoreCase))
            {
                m_CodexHome = home;
                ReloadBackend();
                return;
            }

            UpdateContextIndicator();
            RebuildTranscript();
        }

        void OnBackendReloadRequested()
        {
            m_CodexHome = CodexPreferences.CodexHome;
            ReloadBackend();
        }

        void ReloadBackend()
        {
            ClosePopup();
            if (m_Workspace == null)
            {
                return;
            }

            m_Workspace.Dispose();
            m_Workspace.Connect(ProjectRoot, m_CodexHome, true);
        }

        void UpdateSidebarFilters()
        {
            if (m_Workspace == null || m_SearchField == null)
            {
                return;
            }

            if (m_SearchField.value != m_Workspace.SearchTerm)
            {
                m_SearchField.SetValueWithoutNotify(m_Workspace.SearchTerm);
            }

            string scope = m_Workspace.ShowAllProjects ? ScopeAll : ScopeProject;
            if (m_ScopeField.value != scope)
            {
                m_ScopeField.SetValueWithoutNotify(scope);
            }

            if (m_NewButton != null)
            {
                m_NewButton.SetEnabled(m_Workspace.IsConnected && !m_Workspace.IsRunning);
            }
        }

        void UpdateSettingsBar()
        {
            if (m_ModelButton == null || m_Workspace == null)
            {
                return;
            }

            bool available = m_Workspace.SettingsLoaded && !m_Workspace.IsRunning;

            string modelLabel = ModelLabel(m_Workspace.SelectedModel);
            m_ModelButton.text = $"{modelLabel}  ▾";
            m_ModelButton.SetEnabled(available && (m_Workspace.Models.Count > 0 || !string.IsNullOrEmpty(m_Workspace.SelectedModel)));
            m_ModelButton.tooltip = string.IsNullOrEmpty(m_Workspace.SelectedModel)
                ? "Model"
                : $"Model: {m_Workspace.SelectedModel}";

            m_EffortButton.text = $"{FormatEffort(m_Workspace.SelectedEffort)}  ▾";
            m_EffortButton.SetEnabled(available && m_Workspace.Efforts.Count > 0);
            m_EffortButton.tooltip = "Reasoning effort";

            m_SandboxButton.text = $"{FormatSandbox(m_Workspace.SelectedSandbox)}  ▾";
            m_SandboxButton.SetEnabled(available);
            m_SandboxButton.tooltip = "Sandbox";

            if (!m_Workspace.SettingsLoaded)
            {
                m_SettingsSourceLabel.text = "Loading model settings...";
                m_SettingsSourceLabel.tooltip = string.Empty;
            }
            else if (!string.IsNullOrEmpty(m_Workspace.SettingsError))
            {
                m_SettingsSourceLabel.text = "Settings unavailable";
                m_SettingsSourceLabel.tooltip = m_Workspace.SettingsError;
            }
            else
            {
                m_SettingsSourceLabel.text =
                    $"{m_Workspace.SettingsSource} · approval {m_Workspace.ApprovalDisplay}";
                m_SettingsSourceLabel.tooltip = string.Empty;
            }
        }

        string ModelLabel(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                return "Model";
            }

            foreach (CodexModelInfo model in m_Workspace.Models)
            {
                if (model.Matches(id) && !string.IsNullOrEmpty(model.DisplayName))
                {
                    return model.DisplayName;
                }
            }

            return id;
        }

        void UpdateContextIndicator()
        {
            if (m_ContextIndicator == null)
            {
                return;
            }

            if (!m_ShowContextIndicator)
            {
                m_ContextIndicator.style.display = DisplayStyle.None;
                return;
            }

            m_ContextIndicator.style.display = DisplayStyle.Flex;
            m_ContextLabel.style.color = CodexPalette.Text;

            CodexContextUsage? usage = m_Workspace?.ContextUsage;
            if (usage == null)
            {
                m_ContextLabel.text = "ctx —";
                m_ContextLabel.style.color = CodexPalette.Muted;
                m_ContextDot.style.backgroundColor = CodexPalette.Muted;
                m_ContextIndicator.tooltip = "Context usage appears after the next turn.";
                return;
            }

            CodexContextUsage value = usage.Value;
            if (!value.HasWindow)
            {
                m_ContextLabel.text = $"ctx {FormatTokens(value.UsedTokens)}";
                m_ContextDot.style.backgroundColor = CodexPalette.Warning;
                m_ContextIndicator.tooltip =
                    $"About {value.UsedTokens:N0} context tokens; the window size is unknown.";
                return;
            }

            int percent = (int)Math.Round(value.Fraction * 100);
            m_ContextLabel.text = $"ctx {percent}%";
            m_ContextDot.style.backgroundColor = value.Fraction >= 0.9
                ? CodexPalette.Error
                : value.Fraction >= 0.7
                    ? CodexPalette.Warning
                    : CodexPalette.Success;
            m_ContextIndicator.tooltip =
                $"Context window: {value.UsedTokens:N0} / {value.WindowTokens:N0} tokens ({percent}%).";
        }

        static string FormatTokens(long tokens)
        {
            if (tokens >= 1_000_000)
            {
                return $"{tokens / 1_000_000.0:0.#}M";
            }

            if (tokens >= 1_000)
            {
                return $"{tokens / 1_000.0:0.#}k";
            }

            return tokens.ToString();
        }

        void UpdateComposer()
        {
            if (m_Composer == null)
            {
                return;
            }

            bool ready = m_Workspace != null && m_Workspace.IsConnected && m_Workspace.ThreadsLoaded;
            bool running = m_Workspace != null && m_Workspace.IsRunning;
            bool hasText = !string.IsNullOrWhiteSpace(m_Composer.value);
            bool settingsReady = m_Workspace != null && m_Workspace.SettingsLoaded;
            bool waitingForInteraction = m_Workspace != null && m_Workspace.HasPendingInteraction;

            m_Composer.SetEnabled(ready && settingsReady && !waitingForInteraction);

            bool actionEnabled = running
                ? m_Workspace != null && m_Workspace.CanStop
                : ready && settingsReady && hasText && !waitingForInteraction;

            m_ActionButton.text = running ? "Stop" : "Send";
            m_ActionButton.SetEnabled(actionEnabled);
            m_ActionButton.style.backgroundColor = !actionEnabled
                ? CodexPalette.DisabledAction
                : running ? CodexPalette.StopAction : CodexPalette.SendAction;
            m_ActionButton.tooltip = running ? "Interrupt the running turn" : "Send message";
            if (m_WorkingDot != null)
            {
                m_WorkingDot.style.display = running ? DisplayStyle.Flex : DisplayStyle.None;
            }

            string steerKey = Application.platform == RuntimePlatform.OSXEditor ? "Cmd+Enter" : "Ctrl+Enter";
            string hint = waitingForInteraction
                ? "Resolve Codex's request above"
                : running
                    ? $"Enter to queue  ·  {steerKey} to steer"
                    : "Enter to send  ·  Shift+Enter for a new line";
            if (m_ComposerHint != null)
            {
                m_ComposerHint.text = hint;
            }

            m_Composer.tooltip = waitingForInteraction
                ? "Codex is waiting for your response"
                : running
                    ? $"Enter queues for after this turn; {steerKey} steers the active turn"
                    : "Enter to send · Shift+Enter for a new line";
        }

        void RefreshInteractionPanel()
        {
            if (m_QuestionPanel == null)
            {
                return;
            }

            if (m_Workspace?.PendingQuestion != null)
            {
                RefreshQuestionPanel();
                return;
            }

            if (m_Workspace?.PendingApproval != null)
            {
                RefreshApprovalPanel();
                return;
            }

            m_QuestionPanel.Clear();
            m_QuestionPanel.style.display = DisplayStyle.None;
        }

        void RefreshQuestionPanel()
        {
            if (m_QuestionPanel == null)
            {
                return;
            }

            m_QuestionPanel.Clear();
            CodexQuestionRequest request = m_Workspace?.PendingQuestion;
            CodexQuestion question = request?.CurrentQuestion;
            if (request == null || question == null)
            {
                m_QuestionPanel.style.display = DisplayStyle.None;
                return;
            }

            m_QuestionPanel.style.display = DisplayStyle.Flex;

            var card = new VisualElement();
            card.style.backgroundColor = CodexPalette.Surface;
            card.style.borderTopWidth = 1;
            card.style.borderBottomWidth = 1;
            card.style.borderLeftWidth = 1;
            card.style.borderRightWidth = 1;
            card.style.borderTopColor = CodexPalette.Warning;
            card.style.borderBottomColor = CodexPalette.Border;
            card.style.borderLeftColor = CodexPalette.Border;
            card.style.borderRightColor = CodexPalette.Border;
            card.style.borderTopLeftRadius = 12;
            card.style.borderTopRightRadius = 12;
            card.style.borderBottomLeftRadius = 12;
            card.style.borderBottomRightRadius = 12;
            card.style.marginLeft = 18;
            card.style.marginRight = 18;
            card.style.marginTop = 10;
            card.style.marginBottom = 6;
            card.style.paddingLeft = 14;
            card.style.paddingRight = 14;
            card.style.paddingTop = 11;
            card.style.paddingBottom = 12;
            m_QuestionPanel.Add(card);

            var top = new VisualElement();
            top.style.flexDirection = FlexDirection.Row;
            top.style.alignItems = Align.Center;

            var badge = new Label("CODEX ASKED");
            badge.style.fontSize = 9;
            badge.style.unityFontStyleAndWeight = FontStyle.Bold;
            badge.style.color = CodexPalette.Warning;
            badge.style.flexGrow = 1;
            top.Add(badge);

            if (request.Questions.Count > 1)
            {
                var progress = new Label(
                    $"{request.Questions.IndexOf(question) + 1} / {request.Questions.Count}");
                progress.style.fontSize = 9;
                progress.style.color = CodexPalette.Muted;
                top.Add(progress);
            }

            card.Add(top);

            string header = string.IsNullOrWhiteSpace(question.Header)
                ? "Question"
                : question.Header;
            var title = new Label(header);
            title.style.fontSize = 11;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.color = CodexPalette.Text;
            title.style.whiteSpace = WhiteSpace.Normal;
            title.style.marginTop = 4;
            card.Add(title);

            var prompt = new Label(question.Prompt);
            prompt.style.fontSize = 11;
            prompt.style.color = CodexPalette.Text;
            prompt.style.whiteSpace = WhiteSpace.Normal;
            prompt.style.marginTop = 3;
            card.Add(prompt);

            foreach (CodexQuestionOption option in question.Options)
            {
                var optionBlock = new VisualElement();
                optionBlock.style.marginTop = 6;

                string optionLabel = option.Label;
                var button = new Button(() => m_Workspace.AnswerQuestion(request, optionLabel))
                {
                    text = optionLabel,
                };
                button.style.unityTextAlign = TextAnchor.MiddleLeft;
                button.style.whiteSpace = WhiteSpace.Normal;
                button.style.minHeight = 22;
                button.style.paddingLeft = 8;
                button.style.paddingRight = 8;
                optionBlock.Add(button);

                if (!string.IsNullOrWhiteSpace(option.Description))
                {
                    var description = new Label(option.Description);
                    description.style.fontSize = 9;
                    description.style.color = CodexPalette.Muted;
                    description.style.whiteSpace = WhiteSpace.Normal;
                    description.style.marginLeft = 8;
                    description.style.marginTop = 2;
                    optionBlock.Add(description);
                }

                card.Add(optionBlock);
            }

            if (question.IsOther || question.Options.Count == 0)
            {
                if (question.Options.Count > 0)
                {
                    var otherLabel = new Label("Other");
                    otherLabel.style.fontSize = 9;
                    otherLabel.style.color = CodexPalette.Muted;
                    otherLabel.style.marginTop = 7;
                    card.Add(otherLabel);
                }

                var answerRow = new VisualElement();
                answerRow.style.flexDirection = FlexDirection.Row;
                answerRow.style.alignItems = Align.Center;
                answerRow.style.marginTop = 4;

                var field = new TextField();
                field.style.flexGrow = 1;
                field.textEdition.placeholder = question.IsSecret ? "Secret answer" : "Type an answer...";
                answerRow.Add(field);

                var submit = new Button(() =>
                {
                    if (!string.IsNullOrWhiteSpace(field.value))
                    {
                        m_Workspace.AnswerQuestion(request, field.value);
                    }
                })
                {
                    text = "Answer",
                };
                submit.style.marginLeft = 6;
                answerRow.Add(submit);

                field.RegisterCallback<KeyDownEvent>(evt =>
                {
                    if ((evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter) &&
                        !string.IsNullOrWhiteSpace(field.value))
                    {
                        m_Workspace.AnswerQuestion(request, field.value);
                        evt.StopPropagation();
                    }
                });

                card.Add(answerRow);
            }
        }

        void RefreshApprovalPanel()
        {
            m_QuestionPanel.Clear();
            CodexApprovalRequest approval = m_Workspace?.PendingApproval;
            if (approval == null)
            {
                m_QuestionPanel.style.display = DisplayStyle.None;
                return;
            }

            m_QuestionPanel.style.display = DisplayStyle.Flex;
            VisualElement card = CreateInteractionCard(CodexPalette.SendAction);
            m_QuestionPanel.Add(card);

            switch (approval.Kind)
            {
                case CodexApprovalKind.Command:
                    AddApprovalHeader(
                        card,
                        "COMMAND APPROVAL",
                        approval.CommandKind == "writeStdin" ? "Send terminal input" : "Run command",
                        approval.Reason);
                    AddCodeBlock(card, string.IsNullOrEmpty(approval.Command) ? "(no command text)" : approval.Command);
                    if (!string.IsNullOrWhiteSpace(approval.Cwd))
                    {
                        AddWrappedLabel(card, $"Working directory  {approval.Cwd}", 9, CodexPalette.Muted);
                    }

                    AddApprovalDecisionButtons(card, approval);
                    break;
                case CodexApprovalKind.FileChange:
                    AddApprovalHeader(card, "FILE CHANGE", "Apply file changes", approval.Reason);
                    if (approval.Files.Count == 0)
                    {
                        AddWrappedLabel(
                            card,
                            string.IsNullOrEmpty(approval.ItemId)
                                ? "The change details are not available."
                                : $"Change item {approval.ItemId}",
                            9,
                            CodexPalette.Muted);
                    }
                    else
                    {
                        foreach (string path in approval.Files)
                        {
                            AddWrappedLabel(card, path, 10, CodexPalette.Text, 4);
                        }
                    }

                    if (!string.IsNullOrWhiteSpace(approval.Cwd))
                    {
                        AddWrappedLabel(card, $"Root  {approval.Cwd}", 9, CodexPalette.Muted);
                    }

                    AddApprovalDecisionButtons(card, approval);
                    break;
                case CodexApprovalKind.Permissions:
                    AddApprovalHeader(card, "PERMISSION REQUEST", "Grant additional permissions", approval.Reason);
                    AddCodeBlock(
                        card,
                        approval.Permissions == null || !approval.Permissions.HasValues
                            ? "(no permission details)"
                            : approval.Permissions.ToString(Formatting.Indented));
                    if (!string.IsNullOrWhiteSpace(approval.Cwd))
                    {
                        AddWrappedLabel(card, $"Working directory  {approval.Cwd}", 9, CodexPalette.Muted);
                    }

                    VisualElement permissionActions = AddActionRow(card);
                    AddApprovalButton(
                        permissionActions,
                        "Allow for turn",
                        true,
                        () => m_Workspace.RespondApproval(approval, "grant_turn"));
                    AddApprovalButton(
                        permissionActions,
                        "Allow for session",
                        false,
                        () => m_Workspace.RespondApproval(approval, "grant_session"));
                    AddApprovalButton(
                        permissionActions,
                        "Deny",
                        false,
                        () => m_Workspace.RespondApproval(approval, "deny"));
                    break;
                case CodexApprovalKind.McpElicitation:
                    AddMcpApproval(card, approval);
                    break;
            }
        }

        static VisualElement CreateInteractionCard(Color accent)
        {
            var card = new VisualElement();
            card.style.backgroundColor = CodexPalette.Surface;
            card.style.borderTopWidth = 1;
            card.style.borderBottomWidth = 1;
            card.style.borderLeftWidth = 1;
            card.style.borderRightWidth = 1;
            card.style.borderTopColor = accent;
            card.style.borderBottomColor = CodexPalette.Border;
            card.style.borderLeftColor = CodexPalette.Border;
            card.style.borderRightColor = CodexPalette.Border;
            card.style.borderTopLeftRadius = 12;
            card.style.borderTopRightRadius = 12;
            card.style.borderBottomLeftRadius = 12;
            card.style.borderBottomRightRadius = 12;
            card.style.marginLeft = 18;
            card.style.marginRight = 18;
            card.style.marginTop = 10;
            card.style.marginBottom = 6;
            card.style.paddingLeft = 14;
            card.style.paddingRight = 14;
            card.style.paddingTop = 11;
            card.style.paddingBottom = 12;
            return card;
        }

        static void AddApprovalHeader(
            VisualElement card,
            string badge,
            string title,
            string subtitle)
        {
            var badgeLabel = new Label(badge);
            badgeLabel.style.fontSize = 9;
            badgeLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            badgeLabel.style.color = CodexPalette.SendAction;
            card.Add(badgeLabel);

            var titleLabel = new Label(title);
            titleLabel.style.fontSize = 13;
            titleLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            titleLabel.style.color = CodexPalette.Text;
            titleLabel.style.whiteSpace = WhiteSpace.Normal;
            titleLabel.style.marginTop = 3;
            card.Add(titleLabel);

            if (!string.IsNullOrWhiteSpace(subtitle))
            {
                AddWrappedLabel(card, subtitle, 10, CodexPalette.Muted, 3);
            }
        }

        static void AddWrappedLabel(
            VisualElement card,
            string text,
            int fontSize,
            Color color,
            int marginTop = 0)
        {
            var label = new Label(text);
            label.style.fontSize = fontSize;
            label.style.color = color;
            label.style.whiteSpace = WhiteSpace.Normal;
            label.style.marginTop = marginTop;
            card.Add(label);
        }

        static void AddCodeBlock(VisualElement card, string text)
        {
            var block = new Label(text);
            block.style.backgroundColor = CodexPalette.CodeBackground;
            block.style.color = CodexPalette.Text;
            block.style.whiteSpace = WhiteSpace.Normal;
            block.style.paddingLeft = 8;
            block.style.paddingRight = 8;
            block.style.paddingTop = 6;
            block.style.paddingBottom = 6;
            block.style.marginTop = 6;
            block.style.borderTopLeftRadius = 6;
            block.style.borderTopRightRadius = 6;
            block.style.borderBottomLeftRadius = 6;
            block.style.borderBottomRightRadius = 6;
            card.Add(block);
        }

        static VisualElement AddActionRow(VisualElement card)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.flexWrap = Wrap.Wrap;
            row.style.alignItems = Align.Center;
            row.style.marginTop = 4;
            card.Add(row);
            return row;
        }

        static Button AddApprovalButton(
            VisualElement row,
            string text,
            bool primary,
            Action action)
        {
            var button = new Button(action) { text = text };
            button.style.height = 26;
            button.style.minWidth = 70;
            button.style.paddingLeft = 10;
            button.style.paddingRight = 10;
            button.style.marginRight = 6;
            button.style.marginTop = 6;
            button.style.borderTopLeftRadius = 6;
            button.style.borderTopRightRadius = 6;
            button.style.borderBottomLeftRadius = 6;
            button.style.borderBottomRightRadius = 6;
            button.style.borderTopWidth = 1;
            button.style.borderBottomWidth = 1;
            button.style.borderLeftWidth = 1;
            button.style.borderRightWidth = 1;
            button.style.borderTopColor = primary ? Color.clear : CodexPalette.PillBorder;
            button.style.borderBottomColor = primary ? Color.clear : CodexPalette.PillBorder;
            button.style.borderLeftColor = primary ? Color.clear : CodexPalette.PillBorder;
            button.style.borderRightColor = primary ? Color.clear : CodexPalette.PillBorder;
            button.style.backgroundColor = primary ? CodexPalette.SendAction : CodexPalette.Pill;
            button.style.color = primary ? Color.white : CodexPalette.Text;
            row.Add(button);
            return button;
        }

        void AddApprovalDecisionButtons(
            VisualElement card,
            CodexApprovalRequest approval)
        {
            bool hasStringDecisions = false;
            if (approval.AvailableDecisions != null)
            {
                foreach (JToken decision in approval.AvailableDecisions)
                {
                    if (decision.Type == JTokenType.String)
                    {
                        hasStringDecisions = true;
                        break;
                    }
                }
            }

            bool Allowed(string decision)
            {
                if (!hasStringDecisions || approval.AvailableDecisions == null)
                {
                    return true;
                }

                foreach (JToken token in approval.AvailableDecisions)
                {
                    if (token.Type == JTokenType.String && token.Value<string>() == decision)
                    {
                        return true;
                    }
                }

                return false;
            }

            VisualElement actions = AddActionRow(card);
            if (Allowed("accept"))
            {
                AddApprovalButton(
                    actions,
                    "Allow once",
                    true,
                    () => m_Workspace.RespondApproval(approval, "accept"));
            }

            if (Allowed("acceptForSession"))
            {
                AddApprovalButton(
                    actions,
                    "Allow for session",
                    false,
                    () => m_Workspace.RespondApproval(approval, "acceptForSession"));
            }

            if (Allowed("decline"))
            {
                AddApprovalButton(
                    actions,
                    "Decline",
                    false,
                    () => m_Workspace.RespondApproval(approval, "decline"));
            }

            if (Allowed("cancel"))
            {
                AddApprovalButton(
                    actions,
                    "Cancel turn",
                    false,
                    () => m_Workspace.RespondApproval(approval, "cancel"));
            }
        }

        void AddMcpApproval(VisualElement card, CodexApprovalRequest approval)
        {
            string server = string.IsNullOrEmpty(approval.ServerName) ? "MCP server" : approval.ServerName;
            AddApprovalHeader(card, "MCP REQUEST", server, approval.McpMessage);

            if (approval.McpMode == "url")
            {
                AddCodeBlock(card, string.IsNullOrEmpty(approval.McpUrl) ? "(no URL)" : approval.McpUrl);
                VisualElement urlActions = AddActionRow(card);
                AddApprovalButton(
                    urlActions,
                    "Open link",
                    false,
                    () =>
                    {
                        if (!string.IsNullOrEmpty(approval.McpUrl))
                        {
                            Application.OpenURL(approval.McpUrl);
                        }
                    });
                AddApprovalButton(
                    urlActions,
                    "I've completed",
                    true,
                    () => m_Workspace.RespondApproval(approval, "accept"));
                AddApprovalButton(
                    urlActions,
                    "Decline",
                    false,
                    () => m_Workspace.RespondApproval(approval, "decline"));
                return;
            }

            if (approval.IsMcpUserVerification)
            {
                AddWrappedLabel(
                    card,
                    string.IsNullOrEmpty(approval.McpTitle) ? "Verification required" : approval.McpTitle,
                    11,
                    CodexPalette.Text,
                    5);
                AddWrappedLabel(
                    card,
                    string.IsNullOrEmpty(approval.McpDescription)
                        ? "Open the verification flow in your browser, then retry the tool."
                        : approval.McpDescription,
                    10,
                    CodexPalette.Muted,
                    3);
                AddWrappedLabel(
                    card,
                    "Device verification cannot be completed inside this window yet.",
                    9,
                    CodexPalette.Warning,
                    5);
                VisualElement verifyActions = AddActionRow(card);
                AddApprovalButton(
                    verifyActions,
                    "Decline",
                    false,
                    () => m_Workspace.RespondApproval(approval, "decline"));
                AddApprovalButton(
                    verifyActions,
                    "Cancel",
                    false,
                    () => m_Workspace.RespondApproval(approval, "cancel"));
                return;
            }

            if (approval.McpSchema?["properties"] is JObject properties)
            {
                AddMcpForm(card, approval, properties);
                return;
            }

            AddWrappedLabel(
                card,
                "This elicitation has no supported form fields.",
                10,
                CodexPalette.Muted,
                5);
            VisualElement fallbackActions = AddActionRow(card);
            AddApprovalButton(
                fallbackActions,
                "Decline",
                false,
                () => m_Workspace.RespondApproval(approval, "decline"));
            AddApprovalButton(
                fallbackActions,
                "Cancel",
                false,
                () => m_Workspace.RespondApproval(approval, "cancel"));
        }

        void AddMcpForm(
            VisualElement card,
            CodexApprovalRequest approval,
            JObject properties)
        {
            var requiredNames = new HashSet<string>();
            if (approval.McpSchema?["required"] is JArray required)
            {
                foreach (JToken name in required)
                {
                    string value = name.Value<string>();
                    if (!string.IsNullOrEmpty(value))
                    {
                        requiredNames.Add(value);
                    }
                }
            }

            var fields = new List<McpFormField>();
            foreach (JProperty property in properties.Properties())
            {
                if (property.Value is not JObject schema)
                {
                    continue;
                }

                string title = schema["title"]?.Value<string>();
                if (string.IsNullOrWhiteSpace(title))
                {
                    title = property.Name;
                }

                bool isRequired = requiredNames.Contains(property.Name);
                var label = new Label(isRequired ? $"{title} *" : title);
                label.style.fontSize = 10;
                label.style.unityFontStyleAndWeight = FontStyle.Bold;
                label.style.color = CodexPalette.Text;
                label.style.marginTop = 7;
                card.Add(label);

                string description = schema["description"]?.Value<string>();
                if (!string.IsNullOrWhiteSpace(description))
                {
                    AddWrappedLabel(card, description, 9, CodexPalette.Muted, 2);
                }

                JArray enumValues = schema["enum"] as JArray;
                if (enumValues != null && enumValues.Count > 0)
                {
                    var labels = new List<string>();
                    var values = new List<JToken>();
                    foreach (JToken value in enumValues)
                    {
                        labels.Add(value.ToString());
                        values.Add(value.DeepClone());
                    }

                    var dropdown = new DropdownField(labels, 0);
                    dropdown.style.marginTop = 3;
                    card.Add(dropdown);
                    fields.Add(new McpFormField
                    {
                        Key = property.Name,
                        Required = isRequired,
                        Read = () => values.Count == 0
                            ? null
                            : values[Mathf.Clamp(dropdown.index, 0, values.Count - 1)].DeepClone(),
                    });
                    continue;
                }

                if (schema["oneOf"] is JArray oneOf && oneOf.Count > 0)
                {
                    var labels = new List<string>();
                    var values = new List<JToken>();
                    foreach (JToken option in oneOf)
                    {
                        if (option is not JObject optionObject)
                        {
                            continue;
                        }

                        JToken value = optionObject["const"];
                        if (value == null)
                        {
                            continue;
                        }

                        string optionTitle = optionObject["title"]?.Value<string>();
                        labels.Add(string.IsNullOrEmpty(optionTitle) ? value.ToString() : optionTitle);
                        values.Add(value.DeepClone());
                    }

                    if (labels.Count > 0)
                    {
                        var dropdown = new DropdownField(labels, 0);
                        dropdown.style.marginTop = 3;
                        card.Add(dropdown);
                        fields.Add(new McpFormField
                        {
                            Key = property.Name,
                            Required = isRequired,
                            Read = () => values[Mathf.Clamp(dropdown.index, 0, values.Count - 1)].DeepClone(),
                        });
                        continue;
                    }
                }

                string type = schema["type"]?.Value<string>();
                if (type == "boolean")
                {
                    var toggle = new Toggle();
                    toggle.style.marginTop = 3;
                    if (schema["default"]?.Value<bool>() == true)
                    {
                        toggle.value = true;
                    }

                    card.Add(toggle);
                    fields.Add(new McpFormField
                    {
                        Key = property.Name,
                        Required = isRequired,
                        Read = () => new JValue(toggle.value),
                    });
                    continue;
                }

                if (type == "array")
                {
                    var arrayField = new TextField();
                    arrayField.style.marginTop = 3;
                    arrayField.textEdition.placeholder = "Comma-separated values";
                    card.Add(arrayField);
                    fields.Add(new McpFormField
                    {
                        Key = property.Name,
                        Required = isRequired,
                        Read = () =>
                        {
                            string text = arrayField.value;
                            if (string.IsNullOrWhiteSpace(text))
                            {
                                return null;
                            }

                            var array = new JArray();
                            foreach (string part in text.Split(','))
                            {
                                if (!string.IsNullOrWhiteSpace(part))
                                {
                                    array.Add(part.Trim());
                                }
                            }

                            return array.Count == 0 ? null : array;
                        },
                    });
                    continue;
                }

                bool numeric = type == "number" || type == "integer";
                var input = new TextField();
                input.style.marginTop = 3;
                input.textEdition.placeholder = numeric ? "Number" : "Text";
                string defaultValue = schema["default"]?.ToString();
                if (!string.IsNullOrWhiteSpace(defaultValue))
                {
                    input.value = defaultValue;
                }

                card.Add(input);
                fields.Add(new McpFormField
                {
                    Key = property.Name,
                    Required = isRequired,
                    Read = () =>
                    {
                        string text = input.value;
                        if (string.IsNullOrWhiteSpace(text))
                        {
                            return null;
                        }

                        if (type == "integer" && long.TryParse(text, out long integer))
                        {
                            return new JValue(integer);
                        }

                        if (type == "number" && double.TryParse(text, out double number))
                        {
                            return new JValue(number);
                        }

                        return new JValue(text);
                    },
                });
            }

            var error = new Label(string.Empty);
            error.style.display = DisplayStyle.None;
            error.style.fontSize = 9;
            error.style.color = CodexPalette.Error;
            error.style.marginTop = 4;
            card.Add(error);

            VisualElement actions = AddActionRow(card);
            AddApprovalButton(
                actions,
                "Submit",
                true,
                () =>
                {
                    var content = new JObject();
                    bool missing = false;
                    foreach (McpFormField field in fields)
                    {
                        JToken value = field.Read();
                        if (value == null ||
                            (value.Type == JTokenType.String && string.IsNullOrWhiteSpace(value.Value<string>())))
                        {
                            if (field.Required)
                            {
                                missing = true;
                            }

                            continue;
                        }

                        content[field.Key] = value;
                    }

                    if (missing)
                    {
                        error.text = "Fill the required fields.";
                        error.style.display = DisplayStyle.Flex;
                        return;
                    }

                    m_Workspace.RespondApproval(approval, "accept", content);
                });
            AddApprovalButton(
                actions,
                "Decline",
                false,
                () => m_Workspace.RespondApproval(approval, "decline"));
            AddApprovalButton(
                actions,
                "Cancel",
                false,
                () => m_Workspace.RespondApproval(approval, "cancel"));
        }

        sealed class McpFormField
        {
            public string Key;
            public bool Required;
            public Func<JToken> Read;
        }

        void RefreshQueueStrip()
        {
            if (m_QueueStrip == null)
            {
                return;
            }

            m_QueueStrip.Clear();
            IReadOnlyList<CodexQueuedMessage> queued = m_Workspace?.QueuedMessages;
            if (queued == null || queued.Count == 0 || string.IsNullOrEmpty(m_Workspace.SelectedThreadId))
            {
                m_QueueStrip.style.display = DisplayStyle.None;
                return;
            }

            m_QueueStrip.style.display = DisplayStyle.Flex;
            var header = new Label(queued.Count == 1 ? "Queued · 1" : $"Queued · {queued.Count}");
            header.style.fontSize = 9;
            header.style.unityFontStyleAndWeight = FontStyle.Bold;
            header.style.color = CodexPalette.Muted;
            header.style.marginBottom = 3;
            m_QueueStrip.Add(header);

            foreach (CodexQueuedMessage message in queued)
            {
                var row = new VisualElement();
                row.style.flexDirection = FlexDirection.Row;
                row.style.alignItems = Align.Center;
                row.style.backgroundColor = CodexPalette.Pill;
                row.style.borderTopLeftRadius = 7;
                row.style.borderTopRightRadius = 7;
                row.style.borderBottomLeftRadius = 7;
                row.style.borderBottomRightRadius = 7;
                row.style.paddingLeft = 8;
                row.style.paddingRight = 4;
                row.style.paddingTop = 3;
                row.style.paddingBottom = 3;
                row.style.marginBottom = 3;

                var text = new Label(message.Text);
                text.style.flexGrow = 1;
                text.style.fontSize = 10;
                text.style.color = CodexPalette.Text;
                text.style.whiteSpace = WhiteSpace.Normal;
                text.style.overflow = Overflow.Hidden;
                text.style.maxHeight = 46;
                text.tooltip = message.Text;
                row.Add(text);

                var remove = new Button(() => m_Workspace?.RemoveQueuedMessage(message.Id))
                {
                    text = "×",
                };
                remove.tooltip = "Remove from queue";
                remove.style.width = 18;
                remove.style.height = 18;
                remove.style.minWidth = 18;
                remove.style.marginLeft = 6;
                remove.style.paddingLeft = 0;
                remove.style.paddingRight = 0;
                remove.style.borderTopWidth = 0;
                remove.style.borderBottomWidth = 0;
                remove.style.borderLeftWidth = 0;
                remove.style.borderRightWidth = 0;
                remove.style.backgroundColor = Color.clear;
                remove.style.color = CodexPalette.Muted;
                remove.style.fontSize = 12;
                row.Add(remove);

                m_QueueStrip.Add(row);
            }
        }

        void OnComposerAction()
        {
            if (m_Workspace != null && m_Workspace.IsRunning)
            {
                StopTurn();
            }
            else
            {
                SendComposer();
            }
        }

        void OnComposerKeyDown(KeyDownEvent keyEvent)
        {
            if (keyEvent.keyCode != KeyCode.Return && keyEvent.keyCode != KeyCode.KeypadEnter)
            {
                return;
            }

            if (keyEvent.shiftKey || keyEvent.altKey)
            {
                return;
            }

            keyEvent.StopPropagation();
            SendComposer(keyEvent.ctrlKey || keyEvent.commandKey);
        }

        void SendComposer(bool steer = false)
        {
            if (m_Workspace == null)
            {
                return;
            }

            string text = m_Composer.value?.Trim();
            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            bool accepted;
            if (m_Workspace.IsRunning)
            {
                accepted = steer ? m_Workspace.SteerMessage(text) : m_Workspace.QueueMessage(text);
                if (!accepted && steer)
                {
                    accepted = m_Workspace.QueueMessage(text);
                }
            }
            else
            {
                accepted = m_Workspace.SendMessage(text);
            }

            if (!accepted)
            {
                return;
            }

            m_FollowTranscript = true;
            m_Composer.SetValueWithoutNotify(string.Empty);
            UpdateComposer();
        }

        void StopTurn()
        {
            m_Workspace?.Stop();
            UpdateComposer();
        }

        void NewConversation()
        {
            if (m_Workspace == null)
            {
                return;
            }

            if (m_SearchField != null && !string.IsNullOrEmpty(m_SearchField.value))
            {
                m_SearchField.SetValueWithoutNotify(string.Empty);
                m_Workspace.SetSearchTerm(string.Empty);
            }

            m_Workspace.StartNewConversation();
            m_Composer?.Focus();
        }

        void ForkConversation()
        {
            if (m_Workspace == null || !m_Workspace.IsConnected || m_Workspace.IsRunning)
            {
                return;
            }

            if (string.IsNullOrEmpty(m_Workspace.SelectedThreadId))
            {
                return;
            }

            m_ForkButton.SetEnabled(false);
            if (!m_Workspace.ForkSelectedThread())
            {
                m_ForkButton.SetEnabled(true);
            }
        }

        void ArchiveSelectedThread()
        {
            if (m_Workspace == null || !m_Workspace.IsConnected || m_Workspace.IsRunning)
            {
                return;
            }

            string threadId = m_Workspace.SelectedThreadId;
            if (string.IsNullOrEmpty(threadId))
            {
                return;
            }

            string title = DisplayTitle(m_Workspace.SelectedThread);
            if (!Application.isBatchMode &&
                !EditorUtility.DisplayDialog(
                    "Archive conversation",
                    $"Archive \"{title}\"?\n\nIt will be hidden from the conversation list.",
                    "Archive",
                    "Cancel"))
            {
                return;
            }

            m_Workspace.ArchiveSelectedThread();
        }

        void RebuildThreadList()
        {
            m_ThreadList.Clear();

            if (m_Workspace.Threads.Count == 0)
            {
                string message;
                if (m_Workspace.IsSearching)
                {
                    message = m_Workspace.ShowAllProjects
                        ? "No conversations match your search."
                        : "No conversations in this project match your search.\nSwitch to All projects to search everywhere.";
                }
                else if (!m_Workspace.ShowAllProjects)
                {
                    message = "No conversations in this project.\nType a message below to start one, or switch to All projects.";
                }
                else
                {
                    message = "No conversations yet.\nType a message below to start one.";
                }

                var empty = new Label(message);
                empty.style.whiteSpace = WhiteSpace.Normal;
                empty.style.color = CodexPalette.Muted;
                empty.style.fontSize = 11;
                empty.style.paddingLeft = 10;
                empty.style.paddingRight = 10;
                m_ThreadList.Add(empty);
                return;
            }

            foreach (CodexThreadInfo thread in m_Workspace.Threads)
            {
                m_ThreadList.Add(BuildThreadRow(thread));
            }
        }

        VisualElement BuildThreadRow(CodexThreadInfo thread)
        {
            bool selected = thread.Id == m_Workspace.SelectedThreadId;

            var row = new VisualElement();
            row.style.paddingLeft = 9;
            row.style.paddingRight = 10;
            row.style.paddingTop = 9;
            row.style.paddingBottom = 9;
            row.style.marginLeft = 2;
            row.style.marginRight = 2;
            row.style.marginBottom = 4;
            row.style.borderTopLeftRadius = 9;
            row.style.borderTopRightRadius = 9;
            row.style.borderBottomLeftRadius = 9;
            row.style.borderBottomRightRadius = 9;
            row.style.borderLeftWidth = 2;
            row.style.borderLeftColor = selected ? CodexPalette.Accent : Color.clear;
            row.style.backgroundColor = selected ? CodexPalette.Selected : Color.clear;
            row.style.overflow = Overflow.Hidden;

            var titleRow = new VisualElement();
            titleRow.style.flexDirection = FlexDirection.Row;
            titleRow.style.alignItems = Align.FlexStart;

            // Reserve the indicator slot for every row. The running dot then has a
            // stable position and cannot drift with the title's wrapped width.
            var indicatorSlot = new VisualElement();
            indicatorSlot.style.width = 8;
            indicatorSlot.style.marginRight = 6;
            indicatorSlot.style.marginTop = 4;
            indicatorSlot.style.flexShrink = 0;
            titleRow.Add(indicatorSlot);

            if (selected && m_Workspace.IsRunning)
            {
                indicatorSlot.Add(new CodexPulseDot(CodexPalette.Warning, 6f));
            }

            var title = new Label(DisplayTitle(thread));
            title.style.whiteSpace = WhiteSpace.NoWrap;
            title.style.textOverflow = TextOverflow.Ellipsis;
            title.style.fontSize = 12;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.color = CodexPalette.Text;
            title.style.flexGrow = 1;
            title.style.flexShrink = 1;
            title.style.overflow = Overflow.Hidden;
            titleRow.Add(title);

            row.Add(titleRow);

            string detail = !string.IsNullOrEmpty(thread.Snippet)
                ? thread.Snippet
                : !string.IsNullOrEmpty(thread.Title) && !string.IsNullOrEmpty(thread.Preview)
                    ? thread.Preview
                    : null;
            if (!string.IsNullOrEmpty(detail))
            {
                string previewText = Truncate(detail, 110);
                if (!string.Equals(previewText, DisplayTitle(thread), StringComparison.Ordinal))
                {
                    var preview = new Label(previewText);
                    preview.style.whiteSpace = WhiteSpace.NoWrap;
                    preview.style.textOverflow = TextOverflow.Ellipsis;
                    preview.style.fontSize = 10;
                    preview.style.color = CodexPalette.Muted;
                    preview.style.marginTop = 4;
                    preview.style.marginLeft = 14;
                    preview.style.flexShrink = 1;
                    preview.style.overflow = Overflow.Hidden;
                    row.Add(preview);
                }
            }

            string metaText = m_Workspace.ShowAllProjects
                ? $"{FolderName(thread.Cwd)}  ·  {Ago(thread.UpdatedAt)}"
                : Ago(thread.UpdatedAt);
            var meta = new Label(metaText);
            meta.style.fontSize = 9;
            meta.style.color = CodexPalette.TextDim;
            meta.style.marginTop = 4;
            meta.style.marginLeft = 14;
            row.Add(meta);

            string id = thread.Id;
            row.RegisterCallback<ClickEvent>(_ => m_Workspace.SelectThread(id));
            row.RegisterCallback<ContextClickEvent>(evt =>
            {
                evt.StopPropagation();
                ShowThreadContextMenu(thread, evt.mousePosition);
            });
            row.RegisterCallback<MouseEnterEvent>(_ =>
            {
                if (id != m_Workspace.SelectedThreadId)
                {
                    row.style.backgroundColor = CodexPalette.Hover;
                }
            });
            row.RegisterCallback<MouseLeaveEvent>(_ =>
            {
                row.style.backgroundColor = id == m_Workspace.SelectedThreadId
                    ? CodexPalette.Selected
                    : Color.clear;
            });

            return row;
        }

        void RebuildTranscript()
        {
            string threadId = m_Workspace.SelectedThreadId;
            if (!string.Equals(threadId, m_LastTranscriptThreadId, StringComparison.Ordinal))
            {
                m_LastTranscriptThreadId = threadId;
                m_FollowTranscript = true;
            }

            Vector2 previousOffset = m_Transcript.scrollOffset;
            bool stickToBottom = m_FollowTranscript || IsTranscriptAtBottom();

            m_Transcript.Clear();
            m_LiveContainer = null;

            if (string.IsNullOrEmpty(m_Workspace.SelectedThreadId))
            {
                AddEmptyState();
                RestoreScroll(previousOffset, stickToBottom);
                return;
            }

            JObject liveTurn = m_Workspace.LiveTurn;
            if (m_Workspace.Turns.Count == 0 && liveTurn == null)
            {
                string placeholder = m_Workspace.Error != null
                    ? "Could not load this conversation."
                    : m_Workspace.IsRunning ? "Codex is working..." : "Loading conversation...";
                AddPlaceholder(placeholder);
                RestoreScroll(previousOffset, stickToBottom);
                return;
            }

            // The protocol returns newest first; read oldest to newest.
            for (int turnIndex = m_Workspace.Turns.Count - 1; turnIndex >= 0; turnIndex--)
            {
                RenderTurn(m_Transcript, m_Workspace.Turns[turnIndex], false);
            }

            if (liveTurn != null)
            {
                m_LiveContainer = new VisualElement();
                m_Transcript.Add(m_LiveContainer);
                RenderTurn(m_LiveContainer, liveTurn, true);
            }

            RestoreScroll(previousOffset, stickToBottom);
        }

        void OnTranscriptChanged()
        {
            if (m_Workspace == null)
            {
                return;
            }

            JObject liveTurn = m_Workspace.LiveTurn;
            if (liveTurn == null || m_LiveContainer == null)
            {
                RebuildTranscript();
                RefreshAgentsPanel();
                return;
            }

            Vector2 previousOffset = m_Transcript.scrollOffset;
            bool stickToBottom = m_FollowTranscript || IsTranscriptAtBottom();

            m_LiveContainer.Clear();
            RenderTurn(m_LiveContainer, liveTurn, true);

            RestoreScroll(previousOffset, stickToBottom);
            RefreshAgentsPanel();
        }

        void RenderTurn(VisualElement parent, JObject turn, bool isLive)
        {
            if (turn["items"] is not JArray items)
            {
                return;
            }

            string turnId = turn["id"]?.Value<string>();
            int lastAgentIndex = -1;
            for (int index = 0; index < items.Count; index++)
            {
                if (items[index] is JObject item && item["type"]?.Value<string>() == "agentMessage")
                {
                    lastAgentIndex = index;
                }
            }

            var process = new List<JObject>();
            for (int index = 0; index < items.Count; index++)
            {
                if (items[index] is not JObject item)
                {
                    continue;
                }

                string type = item["type"]?.Value<string>();
                if (type == "userMessage")
                {
                    FlushProcess(parent, process, isLive, turnId);
                    CodexItemRenderer.Render(
                        parent,
                        item,
                        m_ShowReasoning,
                        m_ShowToolCalls,
                        null,
                        null,
                        CopyMessageText,
                        ForkFromTurn,
                        turnId);
                }
                else if (type == "agentMessage" && IsFinalAgentMessage(item, index, lastAgentIndex))
                {
                    FlushProcess(parent, process, isLive, turnId);
                    CodexItemRenderer.Render(
                        parent,
                        item,
                        m_ShowReasoning,
                        m_ShowToolCalls,
                        null,
                        null,
                        CopyMessageText,
                        ForkFromTurn,
                        turnId);
                }
                else
                {
                    // Intermediate agent text is part of the work, not the answer.
                    process.Add(item);
                }
            }

            FlushProcess(parent, process, isLive, turnId);
        }

        static bool IsFinalAgentMessage(JObject item, int index, int lastAgentIndex)
        {
            string phase = item["phase"]?.Value<string>();
            if (phase == "commentary")
            {
                return false;
            }

            if (phase == "final_answer")
            {
                return true;
            }

            return index == lastAgentIndex;
        }

        void FlushProcess(
            VisualElement parent,
            List<JObject> items,
            bool isLive,
            string turnId)
        {
            if (items.Count == 0)
            {
                return;
            }

            int tools = 0;
            int thoughts = 0;
            int replies = 0;
            foreach (JObject item in items)
            {
                string type = item["type"]?.Value<string>();
                if (type == "reasoning")
                {
                    if (m_ShowReasoning && CodexItemRenderer.HasReasoningText(item))
                    {
                        thoughts++;
                    }
                }
                else if (type == "agentMessage")
                {
                    replies++;
                }
                else if (m_ShowToolCalls)
                {
                    tools++;
                }
            }

            if (tools > 0 || thoughts > 0 || replies > 0)
            {
                var foldout = new Foldout
                {
                    text = ProcessSummary(tools, thoughts, replies, ProcessDuration(items, isLive)),
                    // Keep live work visible; history stays compact.
                    value = isLive,
                };

                foldout.style.marginLeft = 18;
                foldout.style.marginRight = 18;
                foldout.style.marginTop = 4;
                foldout.style.marginBottom = 10;
                foldout.style.paddingLeft = 8;
                foldout.style.paddingRight = 8;
                foldout.style.paddingTop = 5;
                foldout.style.paddingBottom = 5;
                foldout.style.backgroundColor = CodexPalette.Surface;
                foldout.style.borderTopWidth = 1;
                foldout.style.borderBottomWidth = 1;
                foldout.style.borderLeftWidth = 1;
                foldout.style.borderRightWidth = 1;
                foldout.style.borderTopColor = CodexPalette.Border;
                foldout.style.borderBottomColor = CodexPalette.Border;
                foldout.style.borderLeftColor = CodexPalette.Border;
                foldout.style.borderRightColor = CodexPalette.Border;
                foldout.style.borderTopLeftRadius = 10;
                foldout.style.borderTopRightRadius = 10;
                foldout.style.borderBottomLeftRadius = 10;
                foldout.style.borderBottomRightRadius = 10;
                foldout.style.color = CodexPalette.Text;

                // History groups are collapsed, so building hundreds of hidden
                // tool rows up front makes selecting a long thread very slow.
                // Build that content only when the reader expands the group.
                var pending = new List<JObject>(items);
                bool rendered = false;
                void RenderPending()
                {
                    if (rendered)
                    {
                        return;
                    }

                    rendered = true;
                    foreach (JObject item in pending)
                    {
                        CodexItemRenderer.Render(
                            foldout,
                            item,
                            m_ShowReasoning,
                            m_ShowToolCalls,
                            id => m_Workspace?.SelectThread(id),
                            (processId, text) => m_Workspace?.SendTerminalInput(processId, text),
                            CopyMessageText,
                            ForkFromTurn,
                            turnId);
                    }
                }

                if (isLive)
                {
                    RenderPending();
                }
                else
                {
                    foldout.RegisterValueChangedCallback(evt =>
                    {
                        if (evt.newValue)
                        {
                            RenderPending();
                        }
                    });
                }

                parent.Add(foldout);
            }

            items.Clear();
        }

        static string ProcessDuration(List<JObject> items, bool live)
        {
            long start = 0;
            long end = 0;
            foreach (JObject item in items)
            {
                long itemStart = item["startedAtMs"]?.Value<long>() ?? 0;
                long itemEnd = item["completedAtMs"]?.Value<long>() ?? 0;
                if (itemStart > 0 && (start == 0 || itemStart < start))
                {
                    start = itemStart;
                }

                if (itemEnd > end)
                {
                    end = itemEnd;
                }
            }

            if (start <= 0)
            {
                return string.Empty;
            }

            if (live || end <= start)
            {
                end = System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            }

            return CodexItemRenderer.FormatDuration(start, end);
        }

        static string ProcessSummary(int tools, int thoughts, int replies, string duration)
        {
            string text = "Process";
            if (tools > 0)
            {
                text += tools == 1 ? "  ·  1 tool" : $"  ·  {tools} tools";
            }

            if (thoughts > 0)
            {
                text += thoughts == 1 ? "  ·  1 thought" : $"  ·  {thoughts} thoughts";
            }

            if (replies > 0)
            {
                text += replies == 1 ? "  ·  1 reply" : $"  ·  {replies} replies";
            }

            if (!string.IsNullOrEmpty(duration))
            {
                text += $"  ·  {duration}";
            }

            return text;
        }

        bool IsTranscriptAtBottom()
        {
            Scroller scroller = m_Transcript.verticalScroller;
            return scroller.highValue <= 0 || scroller.value >= scroller.highValue - 24;
        }

        void RestoreScroll(Vector2 previousOffset, bool stickToBottom)
        {
            m_Transcript.schedule.Execute(() =>
            {
                if (stickToBottom)
                {
                    float maxOffset = Math.Max(
                        m_Transcript.verticalScroller.highValue,
                        m_Transcript.contentContainer.layout.height - m_Transcript.layout.height);
                    m_Transcript.scrollOffset = new Vector2(0, Math.Max(0, maxOffset));
                    m_FollowTranscript = true;
                }
                else
                {
                    m_Transcript.scrollOffset = previousOffset;
                }
            });
        }

        void AddEmptyState()
        {
            var state = new VisualElement();
            state.style.flexGrow = 1;
            state.style.justifyContent = Justify.Center;
            state.style.alignItems = Align.Center;
            state.style.paddingLeft = 32;
            state.style.paddingRight = 32;
            state.style.paddingBottom = 48;

            var mark = new Label("◆");
            mark.style.fontSize = 34;
            mark.style.color = CodexPalette.Accent;
            mark.style.marginBottom = 12;
            state.Add(mark);

            var title = new Label("Codex Assistant");
            title.style.fontSize = 19;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.color = CodexPalette.Text;
            state.Add(title);

            var copy = new Label("Start a conversation below, or choose one from the sidebar.");
            copy.style.fontSize = 11;
            copy.style.color = CodexPalette.Muted;
            copy.style.whiteSpace = WhiteSpace.Normal;
            copy.style.unityTextAlign = TextAnchor.MiddleCenter;
            copy.style.marginTop = 7;
            copy.style.maxWidth = 360;
            state.Add(copy);

            var hint = new Label("The active Editor project is used as the working directory.");
            hint.style.fontSize = 9;
            hint.style.color = CodexPalette.TextDim;
            hint.style.marginTop = 14;
            state.Add(hint);

            m_Transcript.Add(state);
        }

        void AddPlaceholder(string text)
        {
            var label = new Label(text);
            label.style.color = CodexPalette.Muted;
            label.style.fontSize = 11;
            label.style.paddingLeft = 14;
            label.style.paddingTop = 10;
            m_Transcript.Add(label);
        }

        void UpdateHeader()
        {
            CodexThreadInfo selected = m_Workspace.SelectedThread;
            if (selected == null)
            {
                bool hasThread = !string.IsNullOrEmpty(m_Workspace.SelectedThreadId);
                m_HeaderTitle.text = hasThread
                    ? "Loading conversation..."
                    : m_Workspace.IsConnected ? "New conversation" : "No conversation selected";
                m_HeaderMeta.text = hasThread
                    ? m_Workspace.SelectedThreadId
                    : m_Workspace.IsConnected ? "Send a message below to start" : string.Empty;
                m_BackButton.style.display = DisplayStyle.None;
                m_CopyButton.SetEnabled(hasThread);
                m_ForkButton.SetEnabled(
                    hasThread && m_Workspace.IsConnected && !m_Workspace.IsRunning && !m_Workspace.IsForking);
                m_ArchiveButton.SetEnabled(hasThread && m_Workspace.IsConnected && !m_Workspace.IsRunning);
                return;
            }

            bool hasParent = !string.IsNullOrEmpty(selected.ParentThreadId);
            m_HeaderTitle.text = DisplayTitle(selected);
            m_HeaderMeta.text = hasParent
                ? $"{selected.Cwd}  ·  {selected.Id}  ·  subagent"
                : $"{selected.Cwd}  ·  {selected.Id}";
            m_BackButton.style.display = hasParent ? DisplayStyle.Flex : DisplayStyle.None;
            m_BackButton.SetEnabled(hasParent && m_Workspace.IsConnected);
            m_CopyButton.SetEnabled(true);
            m_ForkButton.SetEnabled(
                m_Workspace.IsConnected && !m_Workspace.IsRunning && !m_Workspace.IsForking);
            m_ArchiveButton.SetEnabled(m_Workspace.IsConnected && !m_Workspace.IsRunning);
        }

        void GoToParentThread()
        {
            string parentId = m_Workspace?.SelectedThread?.ParentThreadId;
            if (!string.IsNullOrEmpty(parentId))
            {
                m_Workspace.SelectThread(parentId);
            }
        }

        void CopyResumeCommand()
        {
            string threadId = m_Workspace?.SelectedThreadId;
            if (string.IsNullOrEmpty(threadId))
            {
                return;
            }

            EditorGUIUtility.systemCopyBuffer = $"codex resume {threadId}";
        }

        static void CopyMessageText(string text)
        {
            if (!string.IsNullOrEmpty(text))
            {
                EditorGUIUtility.systemCopyBuffer = text;
            }
        }

        void ForkFromTurn(string turnId)
        {
            if (m_Workspace == null || !m_Workspace.IsConnected || m_Workspace.IsRunning)
            {
                return;
            }

            string threadId = m_Workspace.SelectedThreadId;
            if (string.IsNullOrEmpty(threadId) || string.IsNullOrEmpty(turnId))
            {
                return;
            }

            m_Workspace.ForkThread(threadId, turnId);
        }

        void ShowThreadContextMenu(CodexThreadInfo thread, Vector2 position)
        {
            if (thread == null || m_Workspace == null)
            {
                return;
            }

            var items = new List<PopupItem>
            {
                new PopupItem
                {
                    Label = "Open",
                    Action = () => m_Workspace.SelectThread(thread.Id),
                },
                new PopupItem
                {
                    Label = "Rename...",
                    Action = () => CodexRenameWindow.Open(
                        thread.Id,
                        DisplayTitle(thread),
                        (id, title) => m_Workspace.RenameThread(id, title)),
                },
                new PopupItem
                {
                    Label = "Copy resume",
                    Action = () => EditorGUIUtility.systemCopyBuffer = $"codex resume {thread.Id}",
                },
                new PopupItem
                {
                    Label = "Fork",
                    Action = () =>
                    {
                        if (thread.Id == m_Workspace.SelectedThreadId)
                        {
                            ForkConversation();
                        }
                        else
                        {
                            m_Workspace.ForkThread(thread.Id);
                        }
                    },
                },
                new PopupItem
                {
                    Label = "Archive...",
                    Action = () => ArchiveThreadFromMenu(thread),
                },
            };

            ShowPopup(null, "CONVERSATION", 210, items, false, position);
        }

        void ArchiveThreadFromMenu(CodexThreadInfo thread)
        {
            if (m_Workspace == null || !m_Workspace.IsConnected || m_Workspace.IsRunning)
            {
                return;
            }

            if (thread.Id == m_Workspace.SelectedThreadId)
            {
                ArchiveSelectedThread();
                return;
            }

            if (!Application.isBatchMode &&
                !EditorUtility.DisplayDialog(
                    "Archive conversation",
                    $"Archive \"{DisplayTitle(thread)}\"?\n\nIt will be hidden from the conversation list.",
                    "Archive",
                    "Cancel"))
            {
                return;
            }

            m_Workspace.ArchiveThreadById(thread.Id);
        }

        static string DisplayTitle(CodexThreadInfo thread)
        {
            if (thread == null)
            {
                return "Conversation";
            }

            if (!string.IsNullOrWhiteSpace(thread.Title))
            {
                return Truncate(thread.Title.Trim(), 110);
            }

            if (!string.IsNullOrWhiteSpace(thread.Preview))
            {
                return Truncate(thread.Preview, 110);
            }

            return "(untitled)";
        }

        static string Truncate(string text, int length)
        {
            if (string.IsNullOrEmpty(text))
            {
                return string.Empty;
            }

            string singleLine = text.Replace('\n', ' ').Replace('\r', ' ');
            return singleLine.Length <= length ? singleLine : singleLine.Substring(0, length) + "...";
        }

        static string FolderName(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return "(unknown folder)";
            }

            string trimmed = path.TrimEnd('\\', '/');
            int separator = trimmed.LastIndexOfAny(new[] { '\\', '/' });
            return separator >= 0 ? trimmed.Substring(separator + 1) : trimmed;
        }

        static string Ago(long unixSeconds)
        {
            if (unixSeconds <= 0)
            {
                return string.Empty;
            }

            DateTimeOffset time = DateTimeOffset.FromUnixTimeSeconds(unixSeconds);
            TimeSpan delta = DateTimeOffset.Now - time;

            if (delta.TotalMinutes < 1)
            {
                return "just now";
            }

            if (delta.TotalHours < 1)
            {
                return $"{(int)delta.TotalMinutes}m ago";
            }

            if (delta.TotalDays < 1)
            {
                return $"{(int)delta.TotalHours}h ago";
            }

            if (delta.TotalDays < 7)
            {
                return $"{(int)delta.TotalDays}d ago";
            }

            return time.ToLocalTime().ToString("yyyy-MM-dd");
        }
    }
}
