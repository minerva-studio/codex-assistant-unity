using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
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
        const string SidebarWidthKey = "Minerva.Codex.SidebarWidth";
        const string AgentsWidthKey = "Minerva.Codex.AgentsWidth";
        const float DefaultSidebarWidth = 292f;
        const float DefaultAgentsWidth = 250f;
        const float MinSidebarWidth = 230f;
        const float MaxSidebarWidth = 460f;
        const float MinAgentsWidth = 200f;
        const float MaxAgentsWidth = 420f;
        const float MinTranscriptWidth = 320f;

        const string ScopeProject = "This project";
        const string ScopeAll = "All projects";
        const string ScopeArchived = "Archived";

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
        Button m_RestoreButton;
        Button m_SettingsButton;
        Button m_NewButton;
        TextField m_SearchField;
        DropdownField m_ScopeField;
        TextField m_Composer;
        Button m_ActionButton;
        Button m_AddReferenceButton;
        VisualElement m_AttachmentStrip;
        VisualElement m_ReferenceChipStrip;
        readonly List<ReferenceChip> m_ReferenceChips = new List<ReferenceChip>();
        readonly List<CodexImageAttachment> m_Attachments = new List<CodexImageAttachment>();
        int m_ReferenceTriggerIndex = -1;
        Button m_ModelButton;
        Button m_EffortButton;
        Button m_SandboxButton;
        Button m_UnityContextButton;
        VisualElement m_ContextIndicator;
        VisualElement m_ContextDot;
        Label m_ContextLabel;
        Label m_SettingsSourceLabel;
        Label m_ComposerHint;
        VisualElement m_LiveContainer;
        VisualElement m_ComposerCard;
        VisualElement m_QueueStrip;
        VisualElement m_QuestionPanel;
        VisualElement m_Sidebar;
        VisualElement m_SidebarResizer;
        VisualElement m_AgentsResizer;
        float m_SidebarWidth = DefaultSidebarWidth;
        float m_AgentsWidth = DefaultAgentsWidth;
        float m_ResizeStartX;
        float m_ResizeStartWidth;
        int m_ResizePointerId = -1;
        VisualElement m_AgentsPanel;
        ScrollView m_AgentsList;
        Label m_AgentsCountLabel;
        Button m_AgentsButton;
        CodexPulseDot m_WorkingDot;
        bool m_AgentsPanelCollapsed;
        string m_AgentsSignature;
        VisualElement m_Popup;
        VisualElement m_PopupScrim;
        ScrollView m_PopupList;
        readonly List<Button> m_PopupButtons = new List<Button>();
        readonly List<PopupItem> m_PopupVisibleItems = new List<PopupItem>();
        int m_PopupSelectionIndex = -1;
        EventCallback<KeyDownEvent> m_PopupKeyDownHandler;
        EventCallback<KeyUpEvent> m_PopupKeyUpHandler;

        bool m_FollowTranscript = true;
        bool m_ProgrammaticTranscriptScroll;
        string m_LastTranscriptThreadId;
        int m_LastTurnCount;
        bool m_ShowContextIndicator;
        bool m_ShowReasoning = true;
        bool m_ShowToolCalls = true;
        bool m_AttachUnityContext;
        string m_CodexHome;

        sealed class PopupItem
        {
            public string Label;
            public bool Selected;
            public Action Action;
        }

        sealed class ReferenceChip
        {
            public string Raw;
            public string Label;
            public string Target;
            public string AbsolutePath;
            public bool Skill;
            public int Start;
            public int Length;
        }

        static readonly Regex ReferenceLinkPattern = new Regex(
            @"\[([^\]]+)\]\(([^)]+)\)",
            RegexOptions.Compiled);
        static readonly Regex BareSkillPattern = new Regex(
            @"(?<![\w`\[\]$￥¥＄])[$￥¥＄]([A-Za-z_][A-Za-z0-9_.-]*)",
            RegexOptions.Compiled);

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

        internal CodexWorkspace Workspace => m_Workspace;

        void OpenConsole()
        {
            CodexConsoleWindow.Open(m_Workspace);
        }

        void OnEnable()
        {
            m_ShowContextIndicator = CodexPreferences.ShowContextIndicator;
            m_ShowReasoning = CodexPreferences.ShowReasoning;
            m_ShowToolCalls = CodexPreferences.ShowToolCalls;
            m_AttachUnityContext = CodexPreferences.AttachUnityContext;
            m_CodexHome = CodexPreferences.CodexHome;
            m_SidebarWidth = Mathf.Clamp(
                EditorPrefs.GetFloat(SidebarWidthKey, DefaultSidebarWidth),
                MinSidebarWidth,
                MaxSidebarWidth);
            m_AgentsWidth = Mathf.Clamp(
                EditorPrefs.GetFloat(AgentsWidthKey, DefaultAgentsWidth),
                MinAgentsWidth,
                MaxAgentsWidth);
            CodexPreferences.Changed += OnPreferencesChanged;
            CodexPreferences.ReloadRequested += OnBackendReloadRequested;
            EditorApplication.update += PlaceComposerImeWindow;

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
            ClearAttachments();
            EditorApplication.update -= PlaceComposerImeWindow;
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
            main.Add(BuildSidebarResizer());
            main.Add(BuildContent());
            main.Add(BuildAgentsResizer());
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

            var consoleButton = new Button(OpenConsole) { text = "Console" };
            StyleGhostButton(consoleButton);
            consoleButton.tooltip = "Run standalone commands through app-server";
            toolbar.Add(consoleButton);

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

        VisualElement BuildSidebarResizer()
        {
            m_SidebarResizer = BuildResizer("codex-sidebar-resizer", true);
            return m_SidebarResizer;
        }

        VisualElement BuildAgentsResizer()
        {
            m_AgentsResizer = BuildResizer("codex-agents-resizer", false);
            m_AgentsResizer.style.display = DisplayStyle.None;
            return m_AgentsResizer;
        }

        VisualElement BuildResizer(string name, bool sidebar)
        {
            var handle = new VisualElement
            {
                name = name,
                pickingMode = PickingMode.Position,
            };
            handle.style.width = 5;
            handle.style.flexShrink = 0;
            handle.style.backgroundColor = Color.clear;
            handle.tooltip = sidebar
                ? "Drag to resize conversations"
                : "Drag to resize subagents";

            var line = new VisualElement();
            line.style.width = 1;
            line.style.height = Length.Percent(100);
            line.style.alignSelf = Align.Center;
            line.style.backgroundColor = CodexPalette.Border;
            handle.Add(line);

            handle.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button != 0)
                {
                    return;
                }

                m_ResizePointerId = evt.pointerId;
                m_ResizeStartX = evt.position.x;
                m_ResizeStartWidth = sidebar ? m_SidebarWidth : m_AgentsWidth;
                line.style.backgroundColor = CodexPalette.Accent;
                handle.CapturePointer(evt.pointerId);
                evt.StopPropagation();
            });

            handle.RegisterCallback<PointerMoveEvent>(evt =>
            {
                if (!handle.HasPointerCapture(evt.pointerId))
                {
                    return;
                }

                float delta = evt.position.x - m_ResizeStartX;
                if (sidebar)
                {
                    m_SidebarWidth = ClampSidebarWidth(m_ResizeStartWidth + delta);
                    if (m_Sidebar != null)
                    {
                        m_Sidebar.style.width = m_SidebarWidth;
                    }
                }
                else
                {
                    m_AgentsWidth = ClampAgentsWidth(m_ResizeStartWidth - delta);
                    if (m_AgentsPanel != null)
                    {
                        m_AgentsPanel.style.width = m_AgentsWidth;
                    }
                }

                evt.StopPropagation();
            });

            handle.RegisterCallback<PointerUpEvent>(evt =>
            {
                if (handle.HasPointerCapture(evt.pointerId))
                {
                    handle.ReleasePointer(evt.pointerId);
                }

                FinishResize(line);
                evt.StopPropagation();
            });

            handle.RegisterCallback<PointerCancelEvent>(evt =>
            {
                if (handle.HasPointerCapture(evt.pointerId))
                {
                    handle.ReleasePointer(evt.pointerId);
                }

                FinishResize(line);
                evt.StopPropagation();
            });

            handle.RegisterCallback<PointerCaptureOutEvent>(_ => FinishResize(line));
            handle.RegisterCallback<MouseEnterEvent>(_ =>
                line.style.backgroundColor = CodexPalette.Accent);
            handle.RegisterCallback<MouseLeaveEvent>(_ =>
            {
                if (m_ResizePointerId < 0)
                {
                    line.style.backgroundColor = CodexPalette.Border;
                }
            });

            return handle;
        }

        void FinishResize(VisualElement line)
        {
            m_ResizePointerId = -1;
            if (line != null)
            {
                line.style.backgroundColor = CodexPalette.Border;
            }

            EditorPrefs.SetFloat(SidebarWidthKey, m_SidebarWidth);
            EditorPrefs.SetFloat(AgentsWidthKey, m_AgentsWidth);
        }

        float ClampSidebarWidth(float width)
        {
            bool agentsVisible = m_AgentsPanel != null &&
                                 m_AgentsPanel.style.display.value == DisplayStyle.Flex;
            float available = rootVisualElement.layout.width -
                              MinTranscriptWidth -
                              (agentsVisible ? m_AgentsWidth : 0f);
            float upper = Mathf.Clamp(available, MinSidebarWidth, MaxSidebarWidth);
            return Mathf.Clamp(width, MinSidebarWidth, upper);
        }

        float ClampAgentsWidth(float width)
        {
            float available = rootVisualElement.layout.width - MinTranscriptWidth - m_SidebarWidth;
            float upper = Mathf.Clamp(available, MinAgentsWidth, MaxAgentsWidth);
            return Mathf.Clamp(width, MinAgentsWidth, upper);
        }

        VisualElement BuildSidebar()
        {
            var sidebar = new VisualElement { name = "codex-sidebar" };
            m_Sidebar = sidebar;
            sidebar.style.width = m_SidebarWidth;
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

            var header = new VisualElement();
            header.style.flexDirection = FlexDirection.Row;
            header.style.alignItems = Align.Center;
            header.style.marginBottom = 8;

            var section = new Label("CONVERSATIONS");
            section.style.fontSize = 8;
            section.style.unityFontStyleAndWeight = FontStyle.Bold;
            section.style.color = CodexPalette.TextDim;
            section.style.marginLeft = 2;
            section.style.flexGrow = 1;
            header.Add(section);

            m_ScopeField = new DropdownField { name = "codex-thread-scope" };
            m_ScopeField.choices = new List<string> { ScopeProject, ScopeAll, ScopeArchived };
            m_ScopeField.SetValueWithoutNotify(ScopeProject);
            m_ScopeField.style.width = 108;
            m_ScopeField.style.height = 24;
            m_ScopeField.style.marginRight = 6;
            m_ScopeField.style.color = CodexPalette.Text;
            m_ScopeField.tooltip = "Show conversations from this project, from every folder, or archived ones";
            StylePopupField(m_ScopeField);
            m_ScopeField.RegisterValueChangedCallback(evt =>
            {
                if (m_Workspace == null)
                {
                    return;
                }

                if (evt.newValue == ScopeArchived)
                {
                    m_Workspace.SetShowArchived(true);
                }
                else
                {
                    // SetShowAllProjects also leaves the archived view.
                    m_Workspace.SetShowAllProjects(evt.newValue == ScopeAll);
                }
            });
            header.Add(m_ScopeField);

            m_NewButton = new Button(NewConversation) { text = "+", name = "codex-new" };
            m_NewButton.style.width = 22;
            m_NewButton.style.height = 22;
            m_NewButton.style.minWidth = 22;
            m_NewButton.style.paddingLeft = 0;
            m_NewButton.style.paddingRight = 0;
            m_NewButton.style.paddingTop = 0;
            m_NewButton.style.paddingBottom = 0;
            m_NewButton.style.borderTopLeftRadius = 6;
            m_NewButton.style.borderTopRightRadius = 6;
            m_NewButton.style.borderBottomLeftRadius = 6;
            m_NewButton.style.borderBottomRightRadius = 6;
            m_NewButton.style.borderTopWidth = 0;
            m_NewButton.style.borderBottomWidth = 0;
            m_NewButton.style.borderLeftWidth = 0;
            m_NewButton.style.borderRightWidth = 0;
            m_NewButton.style.backgroundColor = CodexPalette.AccentSoft;
            m_NewButton.style.color = CodexPalette.Accent;
            m_NewButton.style.fontSize = 14;
            m_NewButton.style.unityTextAlign = TextAnchor.MiddleCenter;
            m_NewButton.tooltip = "New conversation";
            m_NewButton.RegisterCallback<MouseEnterEvent>(_ =>
            {
                m_NewButton.style.backgroundColor = CodexPalette.Accent;
                m_NewButton.style.color = Color.white;
            });
            m_NewButton.RegisterCallback<MouseLeaveEvent>(_ =>
            {
                m_NewButton.style.backgroundColor = CodexPalette.AccentSoft;
                m_NewButton.style.color = CodexPalette.Accent;
            });
            header.Add(m_NewButton);
            filters.Add(header);

            m_SearchField = new TextField { name = "codex-thread-search" };
            m_SearchField.style.height = 28;
            m_SearchField.textEdition.placeholder = "Search conversations...";
            m_SearchField.textEdition.hidePlaceholderOnFocus = false;
            StyleField(m_SearchField);
            m_SearchField.RegisterValueChangedCallback(evt => m_Workspace?.SetSearchTerm(evt.newValue));
            filters.Add(m_SearchField);
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
            StyleFieldShell(field);
            field.style.paddingLeft = 0;
            field.style.paddingRight = 0;

            VisualElement input = field.Q(className: "unity-base-text-field__input");
            if (input != null)
            {
                ClearFieldChrome(input);
                input.style.paddingLeft = 8;
                input.style.paddingRight = 6;
            }
        }

        static void StylePopupField(DropdownField field)
        {
            StyleFieldShell(field);
            field.style.paddingLeft = 0;
            field.style.paddingRight = 0;

            VisualElement input = field.Q(className: "unity-base-popup-field__input");
            if (input != null)
            {
                ClearFieldChrome(input);
                input.style.paddingLeft = 8;
                input.style.paddingRight = 2;
            }

            VisualElement arrow = field.Q(className: "unity-base-popup-field__arrow");
            if (arrow != null)
            {
                ClearFieldChrome(arrow);
                arrow.style.marginRight = 4;
            }
        }

        static void StyleFieldShell(VisualElement field)
        {
            field.style.backgroundColor = CodexPalette.Pill;
            field.style.borderTopWidth = 1;
            field.style.borderBottomWidth = 1;
            field.style.borderLeftWidth = 1;
            field.style.borderRightWidth = 1;
            field.style.borderTopColor = CodexPalette.PillBorder;
            field.style.borderBottomColor = CodexPalette.PillBorder;
            field.style.borderLeftColor = CodexPalette.PillBorder;
            field.style.borderRightColor = CodexPalette.PillBorder;
            field.style.borderTopLeftRadius = 8;
            field.style.borderTopRightRadius = 8;
            field.style.borderBottomLeftRadius = 8;
            field.style.borderBottomRightRadius = 8;
        }

        // The shipped fields draw their own rounded box inside ours; that double
        // chrome is what made the search box and the dropdown look heavy and
        // mismatched.
        static void ClearFieldChrome(VisualElement element)
        {
            element.style.backgroundColor = Color.clear;
            element.style.borderTopWidth = 0;
            element.style.borderBottomWidth = 0;
            element.style.borderLeftWidth = 0;
            element.style.borderRightWidth = 0;
            element.style.borderTopLeftRadius = 0;
            element.style.borderTopRightRadius = 0;
            element.style.borderBottomLeftRadius = 0;
            element.style.borderBottomRightRadius = 0;
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

            m_RestoreButton = new Button(RestoreSelectedThread) { text = "Restore" };
            m_RestoreButton.style.marginLeft = 4;
            m_RestoreButton.style.display = DisplayStyle.None;
            m_RestoreButton.tooltip = "Move this conversation back to the active list and open it";
            StyleAccentSoftButton(m_RestoreButton);
            header.Add(m_RestoreButton);

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
            {
                if (!m_ProgrammaticTranscriptScroll)
                {
                    m_FollowTranscript = IsTranscriptAtBottom();
                }
            };
            m_Transcript.RegisterCallback<GeometryChangedEvent>(OnTranscriptGeometryChanged);
            m_Transcript.contentContainer.RegisterCallback<GeometryChangedEvent>(OnTranscriptGeometryChanged);
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
            m_AgentsPanel.style.width = m_AgentsWidth;
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
            if (m_AgentsResizer != null)
            {
                m_AgentsResizer.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            }

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

            m_AttachmentStrip = new VisualElement { name = "codex-attachments" };
            m_AttachmentStrip.style.flexDirection = FlexDirection.Row;
            m_AttachmentStrip.style.flexWrap = Wrap.Wrap;
            m_AttachmentStrip.style.marginBottom = 5;
            m_AttachmentStrip.style.display = DisplayStyle.None;
            m_ComposerCard.Add(m_AttachmentStrip);

            m_ComposerCard.RegisterCallback<DragEnterEvent>(OnComposerDragEnter);
            m_ComposerCard.RegisterCallback<DragUpdatedEvent>(OnComposerDragUpdated);
            m_ComposerCard.RegisterCallback<DragPerformEvent>(OnComposerDragPerform);
            m_ComposerCard.RegisterCallback<DragExitedEvent>(_ => ResetComposerDragStyle());

            m_Composer = new TextField { multiline = true, name = "codex-composer" };
            // Keep the composer on legacy Text Core. UI Toolkit's Advanced Text
            // Generator has known caret/IME positioning defects; the transcript
            // can keep ATG, but the input field needs a correct composition cursor.
            m_Composer.style.unityTextGenerator = TextGeneratorType.Standard;
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
            m_Composer.tooltip = "Enter to send · Shift+Enter for a new line · @, $ or / for references";
            m_Composer.RegisterCallback<KeyDownEvent>(OnComposerKeyDown, TrickleDown.TrickleDown);
            m_Composer.RegisterValueChangedCallback(_ =>
            {
                UpdateComposer();
                MaybeShowReferencePicker();
                RebuildReferenceChips();
            });
            m_Composer.RegisterCallback<FocusInEvent>(_ => SetComposerFocus(true));
            m_Composer.RegisterCallback<FocusOutEvent>(_ => SetComposerFocus(false));
            m_Composer.RegisterCallback<AttachToPanelEvent>(_ => StyleComposerInput());
            m_Composer.schedule.Execute(StyleComposerInput);
            m_ComposerCard.Add(m_Composer);

            m_ReferenceChipStrip = new VisualElement { name = "codex-reference-chips" };
            m_ReferenceChipStrip.style.flexDirection = FlexDirection.Row;
            m_ReferenceChipStrip.style.flexWrap = Wrap.Wrap;
            m_ReferenceChipStrip.style.marginTop = 4;
            m_ReferenceChipStrip.style.display = DisplayStyle.None;
            m_ComposerCard.Add(m_ReferenceChipStrip);

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

            m_AddReferenceButton = new Button(ShowAddReferenceMenu)
            {
                text = "+",
                name = "codex-add-reference",
            };
            m_AddReferenceButton.style.width = 26;
            m_AddReferenceButton.style.height = 26;
            m_AddReferenceButton.style.minWidth = 0;
            m_AddReferenceButton.style.marginRight = 6;
            m_AddReferenceButton.style.paddingLeft = 0;
            m_AddReferenceButton.style.paddingRight = 0;
            m_AddReferenceButton.style.paddingTop = 0;
            m_AddReferenceButton.style.paddingBottom = 0;
            m_AddReferenceButton.style.borderTopWidth = 0;
            m_AddReferenceButton.style.borderBottomWidth = 0;
            m_AddReferenceButton.style.borderLeftWidth = 0;
            m_AddReferenceButton.style.borderRightWidth = 0;
            m_AddReferenceButton.style.borderTopLeftRadius = 13;
            m_AddReferenceButton.style.borderTopRightRadius = 13;
            m_AddReferenceButton.style.borderBottomLeftRadius = 13;
            m_AddReferenceButton.style.borderBottomRightRadius = 13;
            m_AddReferenceButton.style.backgroundColor = CodexPalette.Pill;
            m_AddReferenceButton.style.color = CodexPalette.Text;
            m_AddReferenceButton.style.fontSize = 16;
            m_AddReferenceButton.tooltip = "Add a file, skill, command or image";
            m_AddReferenceButton.RegisterCallback<MouseEnterEvent>(_ =>
                m_AddReferenceButton.style.backgroundColor = CodexPalette.Hover);
            m_AddReferenceButton.RegisterCallback<MouseLeaveEvent>(_ =>
                m_AddReferenceButton.style.backgroundColor = CodexPalette.Pill);
            actions.Add(m_AddReferenceButton);

            m_SandboxButton = AddPill(actions, "codex-sandbox", "Sandbox");
            m_SandboxButton.clicked += ShowSandboxMenu;

            BuildUnityContextButton(actions);

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
            UpdateUnityContextButton();
            UpdateComposer();
            UpdateContextIndicator();

            return composer;
        }

        Button AddPill(VisualElement parent, string name, string tooltip)
        {
            var button = new Button { name = name, tooltip = tooltip };
            StylePill(button);
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

        static void StylePill(Button button)
        {
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
        }

        void BuildUnityContextButton(VisualElement parent)
        {
            m_UnityContextButton = new Button(ToggleUnityContext)
            {
                name = "codex-unity-context",
                text = "Unity state",
            };
            StylePill(m_UnityContextButton);
            m_UnityContextButton.RegisterCallback<MouseEnterEvent>(_ =>
            {
                if (m_UnityContextButton.enabledSelf && !m_AttachUnityContext)
                {
                    m_UnityContextButton.style.backgroundColor = CodexPalette.PillHover;
                }
            });
            m_UnityContextButton.RegisterCallback<MouseLeaveEvent>(_ => UpdateUnityContextButton());
            parent.Add(m_UnityContextButton);
            UpdateUnityContextButton();
        }

        void UpdateUnityContextButton()
        {
            if (m_UnityContextButton == null)
            {
                return;
            }

            Color border = m_AttachUnityContext ? CodexPalette.Accent : CodexPalette.PillBorder;
            m_UnityContextButton.style.backgroundColor = m_AttachUnityContext
                ? CodexPalette.AccentSoft
                : CodexPalette.Pill;
            m_UnityContextButton.style.color = m_AttachUnityContext
                ? CodexPalette.Accent
                : CodexPalette.Text;
            m_UnityContextButton.style.borderTopColor = border;
            m_UnityContextButton.style.borderBottomColor = border;
            m_UnityContextButton.style.borderLeftColor = border;
            m_UnityContextButton.style.borderRightColor = border;
            m_UnityContextButton.tooltip = m_AttachUnityContext
                ? "Unity state is attached to each message: scene, selection, compile state and recent console " +
                  "errors. Click to stop attaching it."
                : "Attach the current Unity Editor state to each message: scene, selection, compile state and " +
                  "recent console errors.";
        }

        void ToggleUnityContext()
        {
            CodexPreferences.AttachUnityContext = !m_AttachUnityContext;
            m_AttachUnityContext = CodexPreferences.AttachUnityContext;
            UpdateUnityContextButton();
        }

        void StyleComposerInput()
        {
            if (m_Composer == null)
            {
                return;
            }

            foreach (TextElement textElement in m_Composer.Query<TextElement>().ToList())
            {
                textElement.style.unityTextGenerator = TextGeneratorType.Standard;
            }

            // The multiline input must wrap and be measured against the card's
            // width. Without this the field lays the text out on one 1400px line:
            // the composer never grows, long Chinese sentences are clipped, and
            // the IME caret is computed far outside the visible input.
            m_Composer.AddToClassList("unity-text-field--multiline");
            TextElement input = m_Composer.Q<TextElement>(
                                    className: "unity-text-element--inner-input-field-component") ??
                                m_Composer.Q<TextElement>();
            if (input != null)
            {
                input.style.whiteSpace = WhiteSpace.Normal;
                if (input.parent != null)
                {
                    input.parent.style.width = Length.Percent(100);
                }
            }

            VisualElement inputArea = m_Composer.Q(className: "unity-base-text-field__input")
                                  ?? m_Composer.Q(className: "unity-text-input");
            if (inputArea != null)
            {
                inputArea.style.backgroundColor = Color.clear;
                inputArea.style.borderTopWidth = 0;
                inputArea.style.borderBottomWidth = 0;
                inputArea.style.borderLeftWidth = 0;
                inputArea.style.borderRightWidth = 0;
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

            m_Popup = new VisualElement
            {
                name = "codex-popup",
                pickingMode = PickingMode.Position,
                focusable = true,
            };
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
            m_PopupList = list;
            FillPopupList(list, items, string.Empty);

            m_Popup.RegisterCallback<KeyDownEvent>(OnPopupKeyDown, TrickleDown.TrickleDown);
            m_PopupKeyDownHandler ??= OnPopupKeyDown;
            m_PopupKeyUpHandler ??= OnPopupKeyUp;
            rootVisualElement.RegisterCallback(m_PopupKeyDownHandler, TrickleDown.TrickleDown);
            rootVisualElement.RegisterCallback(m_PopupKeyUpHandler, TrickleDown.TrickleDown);
            if (search != null)
            {
                search.RegisterValueChangedCallback(evt => FillPopupList(list, items, evt.newValue));
                search.RegisterCallback<KeyDownEvent>(OnPopupKeyDown, TrickleDown.TrickleDown);
                search.RegisterCallback<KeyDownEvent>(OnPopupKeyDown);
                search.RegisterCallback<KeyUpEvent>(OnPopupKeyUp);
                search.RegisterCallback<NavigationSubmitEvent>(_ => ActivatePopupSelection());
                VisualElement input = search.Q(className: "unity-base-text-field__input")
                                      ?? search.Q(className: "unity-text-input");
                if (input != null)
                {
                    input.RegisterCallback<KeyDownEvent>(OnPopupKeyDown, TrickleDown.TrickleDown);
                    input.RegisterCallback<KeyUpEvent>(OnPopupKeyUp);
                    input.RegisterCallback<NavigationSubmitEvent>(_ => ActivatePopupSelection());
                }

                search.schedule.Execute(() => search.Focus());
            }
            else
            {
                m_Popup.schedule.Execute(() => m_Popup.Focus());
            }

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
            m_PopupButtons.Clear();
            m_PopupVisibleItems.Clear();
            m_PopupSelectionIndex = -1;
            int matches = 0;
            foreach (PopupItem item in items)
            {
                if (!string.IsNullOrEmpty(query) &&
                    item.Label.IndexOf(query, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                PopupItem captured = item;
                int rowIndex = m_PopupButtons.Count;
                var row = new Button(() =>
                {
                    ClosePopup();
                    captured.Action?.Invoke();
                })
                {
                    text = (captured.Selected ? "✓  " : "     ") + captured.Label,
                };

                row.style.height = 26;
                row.style.marginTop = 0;
                row.style.marginBottom = 0;
                row.style.marginLeft = 0;
                row.style.marginRight = 0;
                row.style.paddingLeft = 6;
                row.style.paddingRight = 6;
                row.style.backgroundColor = Color.clear;
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
                    m_PopupSelectionIndex = rowIndex;
                    RefreshPopupSelection();
                });
                list.Add(row);
                m_PopupButtons.Add(row);
                m_PopupVisibleItems.Add(captured);
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
                return;
            }

            m_PopupSelectionIndex = 0;
            RefreshPopupSelection();
        }

        void RefreshPopupSelection()
        {
            for (int index = 0; index < m_PopupButtons.Count; index++)
            {
                bool selected = m_PopupVisibleItems[index].Selected;
                bool active = index == m_PopupSelectionIndex;
                Color background = Color.clear;
                if (active)
                {
                    background = new Color(
                        CodexPalette.Selected.r,
                        CodexPalette.Selected.g,
                        CodexPalette.Selected.b,
                        0.72f);
                }
                else if (selected)
                {
                    background = new Color(
                        CodexPalette.Selected.r,
                        CodexPalette.Selected.g,
                        CodexPalette.Selected.b,
                        0.42f);
                }

                m_PopupButtons[index].style.backgroundColor = background;
            }

            if (m_PopupSelectionIndex >= 0 &&
                m_PopupSelectionIndex < m_PopupButtons.Count &&
                m_PopupList != null)
            {
                m_PopupList.ScrollTo(m_PopupButtons[m_PopupSelectionIndex]);
            }
        }

        void MovePopupSelection(int delta)
        {
            if (m_PopupButtons.Count == 0)
            {
                return;
            }

            int count = m_PopupButtons.Count;
            m_PopupSelectionIndex = (m_PopupSelectionIndex + delta + count) % count;
            RefreshPopupSelection();
        }

        void ActivatePopupSelection()
        {
            if (m_PopupSelectionIndex < 0 ||
                m_PopupSelectionIndex >= m_PopupVisibleItems.Count)
            {
                return;
            }

            PopupItem item = m_PopupVisibleItems[m_PopupSelectionIndex];
            ClosePopup();
            item.Action?.Invoke();
        }

        void OnPopupKeyUp(KeyUpEvent evt)
        {
            if (IsPopupSubmit(evt.keyCode, evt.character))
            {
                ActivatePopupSelection();
                evt.StopPropagation();
            }
        }

        void OnPopupKeyDown(KeyDownEvent evt)
        {
            if (IsPopupSubmit(evt.keyCode, evt.character))
            {
                ActivatePopupSelection();
                evt.StopPropagation();
                return;
            }

            switch (evt.keyCode)
            {
                case KeyCode.Escape:
                    ClosePopup();
                    evt.StopPropagation();
                    break;
                case KeyCode.UpArrow:
                    MovePopupSelection(-1);
                    evt.StopPropagation();
                    break;
                case KeyCode.DownArrow:
                    MovePopupSelection(1);
                    evt.StopPropagation();
                    break;
            }
        }

        static bool IsPopupSubmit(KeyCode keyCode, char character)
        {
            return keyCode == KeyCode.Return ||
                   keyCode == KeyCode.KeypadEnter ||
                   character == '\n' ||
                   character == '\r';
        }

        void ClosePopup()
        {
            if (m_PopupKeyDownHandler != null)
            {
                rootVisualElement.UnregisterCallback(m_PopupKeyDownHandler, TrickleDown.TrickleDown);
                m_PopupKeyDownHandler = null;
            }

            if (m_PopupKeyUpHandler != null)
            {
                rootVisualElement.UnregisterCallback(m_PopupKeyUpHandler, TrickleDown.TrickleDown);
                m_PopupKeyUpHandler = null;
            }

            m_Popup?.RemoveFromHierarchy();
            m_Popup = null;
            m_PopupScrim?.RemoveFromHierarchy();
            m_PopupScrim = null;
            m_PopupList = null;
            m_PopupButtons.Clear();
            m_PopupVisibleItems.Clear();
            m_PopupSelectionIndex = -1;
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
            bool transcriptChanged =
                m_ShowReasoning != CodexPreferences.ShowReasoning ||
                m_ShowToolCalls != CodexPreferences.ShowToolCalls;
            m_ShowContextIndicator = CodexPreferences.ShowContextIndicator;
            m_ShowReasoning = CodexPreferences.ShowReasoning;
            m_ShowToolCalls = CodexPreferences.ShowToolCalls;
            m_AttachUnityContext = CodexPreferences.AttachUnityContext;
            UpdateUnityContextButton();

            string home = CodexPreferences.CodexHome;
            if (!string.Equals(home, m_CodexHome, StringComparison.OrdinalIgnoreCase))
            {
                m_CodexHome = home;
                ReloadBackend();
                return;
            }

            UpdateContextIndicator();
            if (transcriptChanged)
            {
                RebuildTranscript();
            }
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

            string scope = m_Workspace.ShowArchived
                ? ScopeArchived
                : m_Workspace.ShowAllProjects ? ScopeAll : ScopeProject;
            if (m_ScopeField.value != scope)
            {
                m_ScopeField.SetValueWithoutNotify(scope);
            }

            if (m_SearchField != null)
            {
                m_SearchField.SetEnabled(!m_Workspace.ShowArchived);
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
            bool hasContent = !string.IsNullOrWhiteSpace(m_Composer.value) || m_Attachments.Count > 0;
            bool settingsReady = m_Workspace != null && m_Workspace.SettingsLoaded;
            bool waitingForInteraction = m_Workspace != null && m_Workspace.HasPendingInteraction;
            bool archived = m_Workspace != null && m_Workspace.SelectedIsArchived;

            m_Composer.SetEnabled(ready && settingsReady && !waitingForInteraction && !archived);

            bool actionEnabled = running
                ? m_Workspace != null && m_Workspace.CanStop
                : ready && settingsReady && hasContent && !waitingForInteraction && !archived;

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
            string hint = archived
                ? "Restore this archived conversation to continue"
                : waitingForInteraction
                    ? "Resolve Codex's request above"
                    : running
                        ? $"Enter to queue  ·  {steerKey} to steer"
                        : "Enter to send  ·  Shift+Enter for a new line";
            if (m_ComposerHint != null)
            {
                m_ComposerHint.text = hint;
            }

            m_Composer.tooltip = archived
                ? "Archived conversations are read-only until restored"
                : waitingForInteraction
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
                    if (schema["default"]?.Value<bool?>() == true)
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

                string display = CodexUnityContext.StripContext(message.Text);
                var text = new Label(display);
                text.style.flexGrow = 1;
                text.style.fontSize = 10;
                text.style.color = CodexPalette.Text;
                text.style.whiteSpace = WhiteSpace.Normal;
                text.style.overflow = Overflow.Hidden;
                text.style.maxHeight = 46;
                text.tooltip = display;
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

        /// <summary>
        /// Moves the composer caret to the end after the text is replaced from
        /// code. The TextField's visible selection and its text-editing indices
        /// are different state: if only the text is replaced, the keyboard
        /// handlers and the IME candidate window keep operating on the old caret,
        /// which puts the composition window in the wrong place and can throw
        /// from the next insert.
        /// </summary>
        void PlaceComposerCaretAtEnd()
        {
            if (m_Composer == null)
            {
                return;
            }

            int end = m_Composer.value?.Length ?? 0;

            // Public path: updates the visible selection and the text editing
            // state together.
            m_Composer.SelectRange(end, end);

            try
            {
                object utilities = FindEditingUtilities(m_Composer.Q<TextElement>());
                if (utilities == null)
                {
                    return;
                }

                // The indices the keyboard handlers and the IME position read.
                // Set them without glyph-index validation, which clamps to 0 while
                // the element has not been laid out again.
                SetNonPublicProperty(utilities, "cursorIndexNoValidation", end);
                SetNonPublicProperty(utilities, "selectIndexNoValidation", end);
            }
            catch (Exception)
            {
                // The caret is cosmetic; never fail the reference insertion because
                // this Unity build hides the text-editing utilities.
            }
        }

#if UNITY_EDITOR_WIN && !UNITY_6000_6_OR_NEWER
        void PlaceComposerImeWindow()
        {
            if (m_Composer == null || m_Composer.panel == null || EditorWindow.focusedWindow != this)
            {
                return;
            }

            VisualElement focused = m_Composer.panel.focusController?.focusedElement as VisualElement;
            if (focused == null || !(focused == m_Composer || m_Composer.Contains(focused)))
            {
                return;
            }

            try
            {
                Rect world = m_Composer.worldBound;
                CodexIme.Place(world.x + 4f, world.y + 18f);
            }
            catch (Exception)
            {
                // Best effort; the platform IME is outside our control.
            }
        }

        static class CodexIme
        {
            const int CFS_CANDIDATEPOS = 0x0040;
            const int CFS_EXCLUDE = 0x0080;

            [StructLayout(LayoutKind.Sequential)]
            struct POINT
            {
                public int X;
                public int Y;
            }

            [StructLayout(LayoutKind.Sequential)]
            struct RECT
            {
                public int Left;
                public int Top;
                public int Right;
                public int Bottom;
            }

            [StructLayout(LayoutKind.Sequential)]
            struct CANDIDATEFORM
            {
                public int Index;
                public int Style;
                public POINT CurrentPos;
                public RECT Area;
            }

            [StructLayout(LayoutKind.Sequential)]
            struct COMPOSITIONFORM
            {
                public int Style;
                public POINT CurrentPos;
                public RECT Area;
            }

            [DllImport("imm32.dll")]
            static extern IntPtr ImmGetContext(IntPtr hWnd);

            [DllImport("imm32.dll")]
            static extern bool ImmSetCandidateWindow(IntPtr hIMC, ref CANDIDATEFORM candidate);

            [DllImport("imm32.dll")]
            static extern bool ImmSetCompositionWindow(IntPtr hIMC, ref COMPOSITIONFORM composition);

            [DllImport("imm32.dll")]
            static extern bool ImmReleaseContext(IntPtr hWnd, IntPtr hIMC);

            [DllImport("user32.dll")]
            static extern IntPtr GetForegroundWindow();

            public static void Place(float x, float y)
            {
                IntPtr hwnd = GetForegroundWindow();
                if (hwnd == IntPtr.Zero)
                {
                    return;
                }

                IntPtr context = ImmGetContext(hwnd);
                if (context == IntPtr.Zero)
                {
                    return;
                }

                try
                {
                    var position = new POINT { X = (int)x, Y = (int)y };
                    var area = new RECT { Left = 0, Top = 0, Right = 0, Bottom = 0 };

                    var candidate = new CANDIDATEFORM
                    {
                        Index = 0,
                        Style = CFS_CANDIDATEPOS | CFS_EXCLUDE,
                        CurrentPos = position,
                        Area = area,
                    };
                    ImmSetCandidateWindow(context, ref candidate);

                    var composition = new COMPOSITIONFORM
                    {
                        Style = CFS_CANDIDATEPOS,
                        CurrentPos = position,
                        Area = area,
                    };
                    ImmSetCompositionWindow(context, ref composition);
                }
                finally
                {
                    ImmReleaseContext(hwnd, context);
                }
            }
        }
#else
        // UI Toolkit places the candidate window itself on other platforms and
        // from 6000.6 on (UUM-143713).
        void PlaceComposerImeWindow()
        {
        }
#endif

        static object FindEditingUtilities(TextElement element)
        {
            if (element == null)
            {
                return null;
            }

            object manipulator = null;
            for (Type type = element.GetType(); type != null && manipulator == null; type = type.BaseType)
            {
                foreach (FieldInfo field in type.GetFields(
                             BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance |
                             BindingFlags.DeclaredOnly))
                {
                    if (field.FieldType.Name == "TextEditingManipulator")
                    {
                        manipulator = field.GetValue(element);
                        break;
                    }
                }
            }

            return manipulator?
                .GetType()
                .GetField("editingUtilities", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                ?.GetValue(manipulator);
        }

        static void SetNonPublicProperty(object target, string name, object value)
        {
            target.GetType()
                .GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                ?.SetValue(target, value);
        }

        void RebuildReferenceChips()
        {
            if (m_ReferenceChipStrip == null)
            {
                return;
            }

            string text = m_Composer != null ? m_Composer.value ?? string.Empty : string.Empty;
            var chips = new List<ReferenceChip>();
            var linkRanges = new List<Vector2Int>();

            foreach (Match match in ReferenceLinkPattern.Matches(text))
            {
                if (match.Index > 0 && text[match.Index - 1] == '!')
                {
                    continue;
                }

                string label = match.Groups[1].Value.Trim();
                string target = match.Groups[2].Value.Trim().Trim('<', '>');
                if (IsRemoteReference(target))
                {
                    continue;
                }

                bool skill = label.StartsWith("$", StringComparison.Ordinal) ||
                             target.EndsWith("/SKILL.md", StringComparison.OrdinalIgnoreCase) ||
                             target.EndsWith("\\SKILL.md", StringComparison.OrdinalIgnoreCase);
                chips.Add(new ReferenceChip
                {
                    Raw = match.Value,
                    Label = label,
                    Target = target,
                    AbsolutePath = ResolveReferencePath(target),
                    Skill = skill,
                    Start = match.Index,
                    Length = match.Length,
                });
                linkRanges.Add(new Vector2Int(match.Index, match.Index + match.Length));
            }

            foreach (Match match in BareSkillPattern.Matches(text))
            {
                bool insideLink = false;
                foreach (Vector2Int range in linkRanges)
                {
                    if (match.Index >= range.x && match.Index < range.y)
                    {
                        insideLink = true;
                        break;
                    }
                }

                if (insideLink)
                {
                    continue;
                }

                string name = match.Groups[1].Value;
                string skillPath = m_Workspace != null ? m_Workspace.SkillPath(name) : null;
                chips.Add(new ReferenceChip
                {
                    Raw = match.Value,
                    Label = "$" + name,
                    Target = string.IsNullOrEmpty(skillPath) ? "skill://" + name : skillPath,
                    AbsolutePath = skillPath,
                    Skill = true,
                    Start = match.Index,
                    Length = match.Length,
                });
            }

            chips.Sort((left, right) => left.Start.CompareTo(right.Start));
            m_ReferenceChips.Clear();
            m_ReferenceChips.AddRange(chips);
            m_ReferenceChipStrip.Clear();
            m_ReferenceChipStrip.style.display = chips.Count == 0
                ? DisplayStyle.None
                : DisplayStyle.Flex;

            foreach (ReferenceChip chip in chips)
            {
                AddReferenceChip(chip);
            }
        }

        void AddReferenceChip(ReferenceChip chip)
        {
            var pill = new VisualElement();
            pill.style.flexDirection = FlexDirection.Row;
            pill.style.alignItems = Align.Center;
            pill.style.height = 22;
            pill.style.marginRight = 4;
            pill.style.marginBottom = 4;
            pill.style.paddingLeft = 6;
            pill.style.paddingRight = 3;
            pill.style.borderTopWidth = 1;
            pill.style.borderBottomWidth = 1;
            pill.style.borderLeftWidth = 1;
            pill.style.borderRightWidth = 1;
            pill.style.borderTopColor = CodexPalette.PillBorder;
            pill.style.borderBottomColor = CodexPalette.PillBorder;
            pill.style.borderLeftColor = CodexPalette.PillBorder;
            pill.style.borderRightColor = CodexPalette.PillBorder;
            pill.style.borderTopLeftRadius = 11;
            pill.style.borderTopRightRadius = 11;
            pill.style.borderBottomLeftRadius = 11;
            pill.style.borderBottomRightRadius = 11;
            pill.style.backgroundColor = CodexPalette.Pill;
            pill.tooltip = chip.Target;

            var content = new VisualElement();
            content.style.flexDirection = FlexDirection.Row;
            content.style.alignItems = Align.Center;
            content.style.flexShrink = 1;
            content.tooltip = string.IsNullOrEmpty(chip.AbsolutePath)
                ? chip.Target
                : chip.Target + "\nDrag to export";

            var icon = new Label(chip.Skill ? "$" : "@");
            icon.style.fontSize = 9;
            icon.style.unityFontStyleAndWeight = FontStyle.Bold;
            icon.style.color = CodexPalette.Accent;
            icon.style.marginRight = 3;
            content.Add(icon);

            string display = chip.Skill ? chip.Label.TrimStart('$') : Path.GetFileName(chip.Label);
            var label = new Label(display);
            label.style.fontSize = 9;
            label.style.color = CodexPalette.Text;
            label.style.maxWidth = 160;
            label.style.whiteSpace = WhiteSpace.NoWrap;
            label.style.overflow = Overflow.Hidden;
            label.style.textOverflow = TextOverflow.Ellipsis;
            content.Add(label);
            pill.Add(content);

            var remove = new Button(() => RemoveReferenceChip(chip)) { text = "×" };
            remove.style.width = 16;
            remove.style.height = 16;
            remove.style.minWidth = 0;
            remove.style.marginLeft = 3;
            remove.style.paddingLeft = 0;
            remove.style.paddingRight = 0;
            remove.style.paddingTop = 0;
            remove.style.paddingBottom = 0;
            remove.style.fontSize = 10;
            remove.style.color = CodexPalette.TextDim;
            remove.style.backgroundColor = Color.clear;
            remove.style.borderTopWidth = 0;
            remove.style.borderBottomWidth = 0;
            remove.style.borderLeftWidth = 0;
            remove.style.borderRightWidth = 0;
            pill.Add(remove);

            if (!string.IsNullOrEmpty(chip.AbsolutePath))
            {
                string path = chip.AbsolutePath;
                string title = display;
                content.RegisterCallback<PointerDownEvent>(evt =>
                {
                    if (evt.button == 0)
                    {
                        StartFileDrag(path, title);
                    }
                });
            }

            m_ReferenceChipStrip.Add(pill);
        }

        void RemoveReferenceChip(ReferenceChip chip)
        {
            if (m_Composer == null)
            {
                return;
            }

            string text = m_Composer.value ?? string.Empty;
            if (chip.Start < 0 ||
                chip.Start + chip.Length > text.Length ||
                !string.Equals(text.Substring(chip.Start, chip.Length), chip.Raw, StringComparison.Ordinal))
            {
                return;
            }

            string updated = text.Remove(chip.Start, chip.Length);
            m_Composer.SetValueWithoutNotify(updated);
            UpdateComposer();
            RebuildReferenceChips();
            PlaceComposerCaretAtEnd();
        }

        static bool IsRemoteReference(string target)
        {
            return string.IsNullOrEmpty(target) ||
                   target[0] == '#' ||
                   target.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                   target.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
                   target.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase) ||
                   target.StartsWith("media://", StringComparison.OrdinalIgnoreCase);
        }

        string ResolveReferencePath(string target)
        {
            if (IsRemoteReference(target))
            {
                return null;
            }

            try
            {
                string baseDirectory = m_Workspace != null && !string.IsNullOrEmpty(m_Workspace.SelectedCwd)
                    ? m_Workspace.SelectedCwd
                    : ProjectRoot;
                return Path.IsPathRooted(target)
                    ? Path.GetFullPath(target)
                    : Path.GetFullPath(Path.Combine(baseDirectory, target));
            }
            catch (Exception)
            {
                return null;
            }
        }

        static void StartFileDrag(string path, string title)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                return;
            }

            DragAndDrop.PrepareStartDrag();
            DragAndDrop.paths = new[] { path };
            string normalized = path.Replace('\\', '/');
            int assetsIndex = normalized.IndexOf("/Assets/", StringComparison.OrdinalIgnoreCase);
            if (assetsIndex >= 0)
            {
                UnityEngine.Object asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(
                    normalized.Substring(assetsIndex + 1));
                DragAndDrop.objectReferences = asset != null
                    ? new[] { asset }
                    : new UnityEngine.Object[0];
            }
            else
            {
                DragAndDrop.objectReferences = new UnityEngine.Object[0];
            }

            DragAndDrop.StartDrag(string.IsNullOrEmpty(title) ? "reference" : title);
        }

        void MaybeShowReferencePicker()
        {
            if (m_Composer == null || m_Popup != null || !m_Composer.enabledSelf)
            {
                return;
            }

            string value = m_Composer.value ?? string.Empty;
            if (value.Length == 0)
            {
                return;
            }

            char trigger = value[value.Length - 1];
            bool skillTrigger = trigger == '$' || trigger == '￥' || trigger == '¥' || trigger == '＄';
            bool fileTrigger = trigger == '@' || trigger == '＠';
            bool commandTrigger = trigger == '/' || trigger == '／';
            if (!skillTrigger && !fileTrigger && !commandTrigger)
            {
                return;
            }

            bool fullWidth = trigger == '￥' || trigger == '¥' || trigger == '＄' ||
                             trigger == '＠' || trigger == '／';
            if (!fullWidth && value.Length > 1 && !char.IsWhiteSpace(value[value.Length - 2]))
            {
                return;
            }

            m_ReferenceTriggerIndex = value.Length - 1;
            if (skillTrigger)
            {
                ShowSkillReferenceMenu();
            }
            else if (fileTrigger)
            {
                ShowFileReferenceMenu();
            }
            else
            {
                ShowCommandReferenceMenu();
            }
        }

        void InsertReference(string token)
        {
            if (m_Composer == null || string.IsNullOrWhiteSpace(token))
            {
                return;
            }

            string value = m_Composer.value ?? string.Empty;
            if (m_ReferenceTriggerIndex >= 0 && m_ReferenceTriggerIndex < value.Length)
            {
                value = value.Substring(0, m_ReferenceTriggerIndex);
            }

            m_ReferenceTriggerIndex = -1;
            if (value.Length > 0 && !char.IsWhiteSpace(value[value.Length - 1]))
            {
                value += " ";
            }

            // A popup activated with Enter runs inside the search field's key
            // event. Focusing the composer immediately lets that same Enter
            // reach the newly focused field and replace the inserted reference
            // with a newline/space. Mutate and focus on the next UI tick instead.
            string inserted = value + token + " ";
            m_Composer.schedule.Execute(() =>
            {
                if (m_Composer == null)
                {
                    return;
                }

                m_Composer.SetValueWithoutNotify(inserted);
                m_Composer.Focus();
                PlaceComposerCaretAtEnd();
                m_Composer.schedule.Execute(PlaceComposerCaretAtEnd);
                UpdateComposer();
                RebuildReferenceChips();
            });
        }

        void ShowAddReferenceMenu()
        {
            var items = new List<PopupItem>
            {
                new PopupItem
                {
                    Label = "File reference",
                    Action = () =>
                    {
                        m_ReferenceTriggerIndex = -1;
                        ShowFileReferenceMenu();
                    },
                },
                new PopupItem
                {
                    Label = "Skill reference",
                    Action = () =>
                    {
                        m_ReferenceTriggerIndex = -1;
                        ShowSkillReferenceMenu();
                    },
                },
                new PopupItem
                {
                    Label = "Command reference",
                    Action = () =>
                    {
                        m_ReferenceTriggerIndex = -1;
                        ShowCommandReferenceMenu();
                    },
                },
                new PopupItem
                {
                    Label = "Attach image...",
                    Action = AttachImage,
                },
                new PopupItem
                {
                    Label = "Capture Game view",
                    Action = CaptureGameView,
                },
            };

            ShowPopup(m_AddReferenceButton, "ADD", 240, items, false);
        }

        void ShowFileReferenceMenu()
        {
            string root = m_Workspace != null && !string.IsNullOrEmpty(m_Workspace.SelectedCwd)
                ? m_Workspace.SelectedCwd
                : Path.GetDirectoryName(Application.dataPath);
            var files = new List<string>();
            CollectReferenceFiles(root, root, files);
            files.Sort(StringComparer.OrdinalIgnoreCase);

            var items = new List<PopupItem>();
            foreach (string relative in files)
            {
                string path = relative;
                string fileName = Path.GetFileName(relative);
                items.Add(new PopupItem
                {
                    Label = relative,
                    Action = () => InsertReference($"[{fileName}]({path})"),
                });
            }

            if (items.Count == 0)
            {
                items.Add(new PopupItem { Label = "No project files found" });
            }

            ShowPopup(m_AddReferenceButton, "FILES", 470, items, true);
        }

        static void CollectReferenceFiles(string root, string directory, List<string> files)
        {
            const int maxFiles = 600;
            if (files.Count >= maxFiles || string.IsNullOrEmpty(directory))
            {
                return;
            }

            try
            {
                foreach (string file in Directory.EnumerateFiles(directory))
                {
                    string extension = Path.GetExtension(file);
                    if (extension == ".meta" || extension == ".tmp" || extension == ".lock")
                    {
                        continue;
                    }

                    string relative = Path.GetRelativePath(root, file).Replace('\\', '/');
                    files.Add(relative);
                    if (files.Count >= maxFiles)
                    {
                        return;
                    }
                }

                foreach (string child in Directory.EnumerateDirectories(directory))
                {
                    string name = Path.GetFileName(child);
                    if (IsIgnoredReferenceDirectory(name))
                    {
                        continue;
                    }

                    CollectReferenceFiles(root, child, files);
                    if (files.Count >= maxFiles)
                    {
                        return;
                    }
                }
            }
            catch (UnauthorizedAccessException)
            {
            }
            catch (IOException)
            {
            }
        }

        static bool IsIgnoredReferenceDirectory(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return true;
            }

            switch (name)
            {
                case ".git":
                case ".vs":
                case ".idea":
                case "Library":
                case "Temp":
                case "Logs":
                case "obj":
                case "Build":
                case "Builds":
                case "UserSettings":
                case "MemoryCaptures":
                case "node_modules":
                    return true;
                default:
                    return false;
            }
        }

        void ShowSkillReferenceMenu()
        {
            if (m_Workspace == null)
            {
                return;
            }

            m_Workspace.LoadSkills(skills =>
            {
                if (m_AddReferenceButton == null)
                {
                    return;
                }

                var items = new List<PopupItem>();
                foreach (CodexSkillInfo skill in skills)
                {
                    string description = string.IsNullOrEmpty(skill.ShortDescription)
                        ? skill.Description
                        : skill.ShortDescription;
                    if (description != null && description.Length > 90)
                    {
                        description = description.Substring(0, 87) + "...";
                    }

                    string suffix = string.IsNullOrEmpty(description) ? string.Empty : "  ·  " + description;
                    string name = skill.Name;
                    string path = skill.Path.Replace('\\', '/');
                    items.Add(new PopupItem
                    {
                        Label = "$" + skill.Name + suffix,
                        Action = () => InsertReference("[$" + name + "](" + path + ")"),
                    });
                }

                if (items.Count == 0)
                {
                    items.Add(new PopupItem { Label = "No skills found for this project" });
                }

                ShowPopup(m_AddReferenceButton, "SKILLS", 390, items, true);
            });
        }

        void ShowCommandReferenceMenu()
        {
            var commands = new[]
            {
                ("/review", "Review the current changes"),
                ("/compact", "Compact the current conversation"),
                ("/init", "Create project instructions"),
                ("/new", "Start a new conversation"),
                ("/fork", "Fork the current conversation"),
                ("/skills", "Reference a skill"),
                ("/file", "Reference a file"),
            };

            var items = new List<PopupItem>();
            foreach ((string token, string description) in commands)
            {
                string value = token;
                items.Add(new PopupItem
                {
                    Label = token + "  ·  " + description,
                    Action = () => InsertReference(value),
                });
            }

            ShowPopup(m_AddReferenceButton, "COMMAND REFERENCES", 390, items, true);
        }

        void AttachImage()
        {
            string start = m_Workspace != null && !string.IsNullOrEmpty(m_Workspace.SelectedCwd)
                ? m_Workspace.SelectedCwd
                : Path.GetDirectoryName(Application.dataPath);
            string path = EditorUtility.OpenFilePanelWithFilters(
                "Attach image",
                start,
                new[] { "Images", "png,jpg,jpeg,gif,webp,tga,bmp" });
            AddAttachment(path);
        }

        void CaptureGameView()
        {
            if (!EditorApplication.isPlaying)
            {
                m_StatusLabel.text = "Enter Play Mode to capture the Game view.";
                return;
            }

            try
            {
                Texture2D texture = ScreenCapture.CaptureScreenshotAsTexture();
                if (texture == null)
                {
                    m_StatusLabel.text = "Game view capture returned no image.";
                    return;
                }

                byte[] png = texture.EncodeToPNG();
                UnityEngine.Object.DestroyImmediate(texture);
                if (png == null)
                {
                    m_StatusLabel.text = "Could not encode the Game view capture.";
                    return;
                }

                string directory = Path.Combine(
                    Path.GetDirectoryName(Application.dataPath),
                    "Library",
                    "CodexAssistant",
                    "attachments");
                Directory.CreateDirectory(directory);
                string path = Path.Combine(
                    directory,
                    $"capture-{DateTime.Now:yyyyMMdd-HHmmss}.png");
                File.WriteAllBytes(path, png);
                AddAttachment(path);
            }
            catch (Exception exception)
            {
                m_StatusLabel.text = "Game view capture failed: " + exception.Message;
                Debug.LogWarning("Codex Assistant: Game view capture failed: " + exception);
            }
        }

        void AddAttachment(string path)
        {
            if (m_Attachments.Count >= 4)
            {
                m_StatusLabel.text = "At most 4 images per message.";
                return;
            }

            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                return;
            }

            foreach (CodexImageAttachment existing in m_Attachments)
            {
                if (string.Equals(existing.Path, path, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
            }

            CodexImageAttachment attachment = CodexImageAttachment.TryLoad(path);
            if (attachment == null)
            {
                m_StatusLabel.text = "Could not load that image.";
                return;
            }

            m_Attachments.Add(attachment);
            RebuildAttachments();
            UpdateComposer();
        }

        void AddAttachmentPaths(IEnumerable<string> paths)
        {
            foreach (string path in paths)
            {
                if (IsSupportedImagePath(path))
                {
                    AddAttachment(path);
                }
            }
        }

        void RebuildAttachments()
        {
            if (m_AttachmentStrip == null)
            {
                return;
            }

            m_AttachmentStrip.Clear();
            m_AttachmentStrip.style.display = m_Attachments.Count == 0
                ? DisplayStyle.None
                : DisplayStyle.Flex;

            foreach (CodexImageAttachment attachment in m_Attachments)
            {
                CodexImageAttachment captured = attachment;
                var chip = new VisualElement();
                chip.style.width = 54;
                chip.style.height = 54;
                chip.style.marginRight = 4;
                chip.style.marginBottom = 4;
                chip.style.borderTopWidth = 1;
                chip.style.borderBottomWidth = 1;
                chip.style.borderLeftWidth = 1;
                chip.style.borderRightWidth = 1;
                chip.style.borderTopColor = CodexPalette.Border;
                chip.style.borderBottomColor = CodexPalette.Border;
                chip.style.borderLeftColor = CodexPalette.Border;
                chip.style.borderRightColor = CodexPalette.Border;
                chip.style.borderTopLeftRadius = 8;
                chip.style.borderTopRightRadius = 8;
                chip.style.borderBottomLeftRadius = 8;
                chip.style.borderBottomRightRadius = 8;
                chip.style.overflow = Overflow.Hidden;
                chip.tooltip = attachment.Path;

                var image = new Image
                {
                    image = attachment.Thumbnail,
                    scaleMode = ScaleMode.ScaleAndCrop,
                };
                image.style.width = Length.Percent(100);
                image.style.height = Length.Percent(100);
                image.tooltip = "Drag to export";
                image.RegisterCallback<PointerDownEvent>(evt =>
                {
                    if (evt.button == 0)
                    {
                        StartFileDrag(captured.Path, Path.GetFileName(captured.Path));
                    }
                });
                chip.Add(image);

                var remove = new Button(() => RemoveAttachment(captured)) { text = "×" };
                remove.style.position = Position.Absolute;
                remove.style.top = 2;
                remove.style.right = 2;
                remove.style.width = 18;
                remove.style.height = 18;
                remove.style.paddingLeft = 0;
                remove.style.paddingRight = 0;
                remove.style.paddingTop = 0;
                remove.style.paddingBottom = 0;
                remove.style.fontSize = 12;
                remove.style.unityFontStyleAndWeight = FontStyle.Bold;
                remove.style.color = Color.white;
                remove.style.backgroundColor = new Color(0f, 0f, 0f, 0.82f);
                remove.style.borderTopLeftRadius = 8;
                remove.style.borderTopRightRadius = 8;
                remove.style.borderBottomLeftRadius = 8;
                remove.style.borderBottomRightRadius = 8;
                chip.Add(remove);
                m_AttachmentStrip.Add(chip);
            }
        }

        void RemoveAttachment(CodexImageAttachment attachment)
        {
            m_Attachments.Remove(attachment);
            attachment.Dispose();
            RebuildAttachments();
            UpdateComposer();
        }

        void ClearAttachments()
        {
            foreach (CodexImageAttachment attachment in m_Attachments)
            {
                attachment.Dispose();
            }

            m_Attachments.Clear();
            RebuildAttachments();
        }

        List<string> AttachmentPaths()
        {
            var paths = new List<string>(m_Attachments.Count);
            foreach (CodexImageAttachment attachment in m_Attachments)
            {
                paths.Add(attachment.Path);
            }

            return paths;
        }

        static bool IsSupportedImagePath(string path)
        {
            switch (Path.GetExtension(path).ToLowerInvariant())
            {
                case ".png":
                case ".jpg":
                case ".jpeg":
                case ".gif":
                case ".webp":
                case ".tga":
                case ".bmp":
                    return true;
                default:
                    return false;
            }
        }

        void OnComposerDragEnter(DragEnterEvent evt)
        {
            List<string> paths = DraggedFilePaths();
            DragAndDrop.visualMode = paths.Count == 0
                ? DragAndDropVisualMode.None
                : DragAndDropVisualMode.Copy;
            if (paths.Count > 0)
            {
                SetComposerDragStyle();
            }
        }

        void OnComposerDragUpdated(DragUpdatedEvent evt)
        {
            List<string> paths = DraggedFilePaths();
            DragAndDrop.visualMode = paths.Count == 0
                ? DragAndDropVisualMode.None
                : DragAndDropVisualMode.Copy;
        }

        void OnComposerDragPerform(DragPerformEvent evt)
        {
            AddDroppedPaths(DraggedFilePaths());
            DragAndDrop.AcceptDrag();
            ResetComposerDragStyle();
            evt.StopPropagation();
        }

        void AddDroppedPaths(IEnumerable<string> paths)
        {
            var imagePaths = new List<string>();
            var fileReferences = new List<string>();
            foreach (string path in paths)
            {
                if (!File.Exists(path))
                {
                    continue;
                }

                if (IsSupportedImagePath(path))
                {
                    imagePaths.Add(path);
                }
                else
                {
                    fileReferences.Add(FileReferenceToken(path));
                }
            }

            AddAttachmentPaths(imagePaths);
            if (fileReferences.Count > 0)
            {
                m_ReferenceTriggerIndex = -1;
                InsertReference(string.Join(" ", fileReferences));
            }
        }

        string FileReferenceToken(string path)
        {
            string target = path;
            string root = m_Workspace != null ? m_Workspace.SelectedCwd : null;
            if (!string.IsNullOrEmpty(root))
            {
                string fullRoot = Path.GetFullPath(root)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
                    Path.DirectorySeparatorChar;
                string fullPath = Path.GetFullPath(path);
                if (fullPath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
                {
                    target = Path.GetRelativePath(root, fullPath);
                }
            }

            target = target.Replace('\\', '/');
            return "[" + Path.GetFileName(path) + "](" + target + ")";
        }

        void SetComposerDragStyle()
        {
            m_ComposerCard.style.borderTopColor = CodexPalette.Accent;
            m_ComposerCard.style.borderBottomColor = CodexPalette.Accent;
            m_ComposerCard.style.borderLeftColor = CodexPalette.Accent;
            m_ComposerCard.style.borderRightColor = CodexPalette.Accent;
        }

        void ResetComposerDragStyle()
        {
            m_ComposerCard.style.borderTopColor = CodexPalette.Border;
            m_ComposerCard.style.borderBottomColor = CodexPalette.Border;
            m_ComposerCard.style.borderLeftColor = CodexPalette.Border;
            m_ComposerCard.style.borderRightColor = CodexPalette.Border;
        }

        static List<string> DraggedFilePaths()
        {
            var paths = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string path in DragAndDrop.paths)
            {
                string absolute = ToAbsoluteReferencePath(path);
                if ((File.Exists(absolute) || Directory.Exists(absolute)) && seen.Add(absolute))
                {
                    paths.Add(absolute);
                }
            }

            foreach (UnityEngine.Object reference in DragAndDrop.objectReferences)
            {
                string assetPath = AssetDatabase.GetAssetPath(reference);
                if (string.IsNullOrEmpty(assetPath))
                {
                    continue;
                }

                string absolute = ToAbsoluteReferencePath(assetPath);
                if ((File.Exists(absolute) || Directory.Exists(absolute)) && seen.Add(absolute))
                {
                    paths.Add(absolute);
                }
            }

            return paths;
        }

        static string ToAbsoluteReferencePath(string path)
        {
            if (Path.IsPathRooted(path))
            {
                return Path.GetFullPath(path);
            }

            string root = Path.GetDirectoryName(Application.dataPath);
            return Path.GetFullPath(Path.Combine(root, path));
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

            string text = m_Composer.value?.Trim() ?? string.Empty;
            List<string> imagePaths = AttachmentPaths();
            if (string.IsNullOrEmpty(text) && imagePaths.Count == 0)
            {
                return;
            }

            // Slash commands have to stay at the start of the input; a context
            // block in front of /compact would turn it into ordinary text.
            if (m_AttachUnityContext && !IsSlashCommand(text))
            {
                string context = CodexUnityContext.Capture();
                if (!string.IsNullOrEmpty(context))
                {
                    text = text.Length == 0 ? context : context + "\n\n" + text;
                }
            }

            bool accepted;
            if (m_Workspace.IsRunning)
            {
                accepted = steer
                    ? m_Workspace.SteerMessage(text, imagePaths)
                    : m_Workspace.QueueMessage(text, imagePaths);
                if (!accepted && steer)
                {
                    accepted = m_Workspace.QueueMessage(text, imagePaths);
                }
            }
            else
            {
                accepted = m_Workspace.SendMessage(text, imagePaths);
            }

            if (!accepted)
            {
                return;
            }

            m_FollowTranscript = true;
            m_Composer.SetValueWithoutNotify(string.Empty);
            // SetValueWithoutNotify changes the visible text but leaves the
            // text-editing indices at the old position. The next typed character
            // then inserts at a stale index and throws from ReplaceSelection.
            PlaceComposerCaretAtEnd();
            m_Composer.schedule.Execute(PlaceComposerCaretAtEnd);
            ClearAttachments();
            RebuildReferenceChips();
            UpdateComposer();
        }

        static bool IsSlashCommand(string text)
        {
            return text.Length > 1 && text[0] == '/' && !char.IsWhiteSpace(text[1]);
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
                if (m_Workspace.ShowArchived)
                {
                    message = "No archived conversations.\nArchived conversations stay here until you restore them.";
                }
                else if (m_Workspace.IsSearching)
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

            if (m_Workspace.HasMoreThreads || m_Workspace.IsLoadingMoreThreads)
            {
                var more = new Button(() => m_Workspace.LoadMoreThreads())
                {
                    text = m_Workspace.IsLoadingMoreThreads ? "Loading..." : "Load more",
                };
                more.SetEnabled(!m_Workspace.IsLoadingMoreThreads);
                more.style.height = 26;
                more.style.marginTop = 4;
                more.style.marginBottom = 8;
                more.style.backgroundColor = CodexPalette.Pill;
                more.style.color = CodexPalette.Muted;
                more.style.fontSize = 10;
                more.style.borderTopWidth = 0;
                more.style.borderBottomWidth = 0;
                more.style.borderLeftWidth = 0;
                more.style.borderRightWidth = 0;
                more.style.borderTopLeftRadius = 8;
                more.style.borderTopRightRadius = 8;
                more.style.borderBottomLeftRadius = 8;
                more.style.borderBottomRightRadius = 8;
                m_ThreadList.Add(more);
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
                    ? CodexUnityContext.UserText(thread.Preview)
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

            string metaText = m_Workspace.ShowAllProjects || m_Workspace.ShowArchived
                ? $"{FolderName(thread.Cwd)}  ·  {Ago(thread.UpdatedAt)}"
                : Ago(thread.UpdatedAt);
            var meta = new Label(metaText);
            meta.style.fontSize = 9;
            meta.style.color = CodexPalette.TextDim;
            meta.style.marginTop = 4;
            meta.style.marginLeft = 14;
            row.Add(meta);

            string id = thread.Id;
            row.RegisterCallback<ClickEvent>(_ =>
            {
                if (m_Workspace.ShowArchived)
                {
                    m_Workspace.SelectArchivedThread(id);
                }
                else
                {
                    m_Workspace.SelectThread(id);
                }
            });
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
            int previousTurnCount = m_LastTurnCount;
            float previousContentHeight = m_Transcript.contentContainer.layout.height;

            m_Transcript.Clear();
            m_LiveContainer = null;

            if (string.IsNullOrEmpty(m_Workspace.SelectedThreadId))
            {
                m_LastTurnCount = 0;
                AddEmptyState();
                RestoreScroll(previousOffset, stickToBottom);
                return;
            }

            JObject liveTurn = m_Workspace.LiveTurn;
            if (m_Workspace.Turns.Count == 0 && liveTurn == null)
            {
                m_LastTurnCount = 0;
                string placeholder = m_Workspace.SelectedIsArchived
                    ? "This conversation is archived. Use Restore to open it."
                    : m_Workspace.Error != null
                        ? "Could not load this conversation."
                        : m_Workspace.IsRunning ? "Codex is working..." : "Loading conversation...";
                AddPlaceholder(placeholder);
                RestoreScroll(previousOffset, stickToBottom);
                return;
            }

            if (m_Workspace.HasMoreTurns || m_Workspace.IsLoadingMoreTurns)
            {
                AddLoadEarlierButton();
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

            m_LastTurnCount = m_Workspace.Turns.Count;
            // Appending older turns pushes the current content down; keep the
            // reader on the same message instead of jumping to the new page.
            bool appendedOlderTurns = previousTurnCount > 0 && m_LastTurnCount > previousTurnCount;
            RestoreScroll(
                previousOffset,
                stickToBottom,
                appendedOlderTurns ? previousContentHeight : -1f);
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
            long turnTimestamp = turn["startedAtMs"]?.Value<long?>() ??
                                 turn["completedAtMs"]?.Value<long?>() ??
                                 0;
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
                    FlushProcess(
                        parent,
                        process,
                        isLive,
                        turnId,
                        turnTimestamp,
                        m_Workspace.SelectedCwd);
                    CodexItemRenderer.Render(
                        parent,
                        item,
                        m_ShowReasoning,
                        m_ShowToolCalls,
                        null,
                        null,
                        CopyMessageText,
                        ForkFromTurn,
                        turnId,
                        turnTimestamp,
                        false,
                        m_Workspace.SelectedCwd);
                }
                else if (type == "agentMessage" && IsFinalAgentMessage(item, index, lastAgentIndex))
                {
                    FlushProcess(
                        parent,
                        process,
                        isLive,
                        turnId,
                        turnTimestamp,
                        m_Workspace.SelectedCwd);
                    CodexItemRenderer.Render(
                        parent,
                        item,
                        m_ShowReasoning,
                        m_ShowToolCalls,
                        null,
                        null,
                        CopyMessageText,
                        ForkFromTurn,
                        turnId,
                        turnTimestamp,
                        !isLive,
                        m_Workspace.SelectedCwd);
                }
                else
                {
                    // Intermediate agent text is part of the work, not the answer.
                    process.Add(item);
                }
            }

            FlushProcess(
                parent,
                process,
                isLive,
                turnId,
                turnTimestamp,
                m_Workspace.SelectedCwd);
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
            string turnId,
            long turnTimestamp,
            string linkRoot)
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
                            turnId,
                            turnTimestamp,
                            false,
                            linkRoot);
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
                long itemStart = item["startedAtMs"]?.Value<long?>() ?? 0;
                long itemEnd = item["completedAtMs"]?.Value<long?>() ?? 0;
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

        void OnTranscriptGeometryChanged(GeometryChangedEvent _)
        {
            // Layout can change after the single scheduled scroll in
            // RestoreScroll (new message, markdown/image layout, composer
            // resizing). While following, every geometry change is another
            // chance to reach the real bottom.
            if (m_FollowTranscript)
            {
                ScrollTranscriptToBottom();
            }
        }

        void ScrollTranscriptToBottom()
        {
            float maxOffset = Math.Max(
                m_Transcript.verticalScroller.highValue,
                m_Transcript.contentContainer.layout.height - m_Transcript.layout.height);
            SetTranscriptScrollOffset(maxOffset);
        }

        void SetTranscriptScrollOffset(float y)
        {
            m_ProgrammaticTranscriptScroll = true;
            try
            {
                m_Transcript.scrollOffset = new Vector2(0, Math.Max(0f, y));
            }
            finally
            {
                m_ProgrammaticTranscriptScroll = false;
            }
        }

        void RestoreScroll(Vector2 previousOffset, bool stickToBottom, float previousContentHeight = -1f)
        {
            m_Transcript.schedule.Execute(() =>
            {
                if (stickToBottom)
                {
                    m_FollowTranscript = true;
                    ScrollTranscriptToBottom();
                }
                else
                {
                    float delta = 0f;
                    if (previousContentHeight >= 0f)
                    {
                        float contentHeight = m_Transcript.contentContainer.layout.height;
                        delta = Math.Max(0f, contentHeight - previousContentHeight);
                    }

                    SetTranscriptScrollOffset(previousOffset.y + delta);
                }
            });
        }

        void AddLoadEarlierButton()
        {
            var button = new Button(() => m_Workspace?.LoadEarlierTurns())
            {
                text = m_Workspace.IsLoadingMoreTurns ? "Loading earlier turns..." : "Load earlier turns",
            };
            button.SetEnabled(!m_Workspace.IsLoadingMoreTurns);
            button.style.alignSelf = Align.Center;
            button.style.height = 24;
            button.style.paddingLeft = 14;
            button.style.paddingRight = 14;
            button.style.marginBottom = 10;
            button.style.fontSize = 10;
            button.style.color = CodexPalette.Muted;
            button.style.backgroundColor = CodexPalette.Pill;
            button.style.borderTopLeftRadius = 7;
            button.style.borderTopRightRadius = 7;
            button.style.borderBottomLeftRadius = 7;
            button.style.borderBottomRightRadius = 7;
            m_Transcript.Add(button);
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

            bool browsingArchived = m_Workspace != null && m_Workspace.ShowArchived;

            var title = new Label(browsingArchived ? "Archived conversations" : "Codex Assistant");
            title.style.fontSize = 19;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.color = CodexPalette.Text;
            state.Add(title);

            var copy = new Label(
                browsingArchived
                    ? "Choose one from the sidebar and restore it to continue where it left off."
                    : "Start a conversation below, or choose one from the sidebar.");
            copy.style.fontSize = 11;
            copy.style.color = CodexPalette.Muted;
            copy.style.whiteSpace = WhiteSpace.Normal;
            copy.style.unityTextAlign = TextAnchor.MiddleCenter;
            copy.style.marginTop = 7;
            copy.style.maxWidth = 360;
            state.Add(copy);

            var hint = new Label(
                browsingArchived
                    ? "Archived conversations are hidden from the active list."
                    : "The active Editor project is used as the working directory.");
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
            bool archived = m_Workspace.SelectedIsArchived;
            m_RestoreButton.style.display = archived ? DisplayStyle.Flex : DisplayStyle.None;
            m_RestoreButton.SetEnabled(archived && m_Workspace.IsConnected);

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
                    hasThread && !archived && m_Workspace.IsConnected && !m_Workspace.IsRunning &&
                    !m_Workspace.IsForking);
                m_ArchiveButton.SetEnabled(
                    hasThread && !archived && m_Workspace.IsConnected && !m_Workspace.IsRunning);
                return;
            }

            bool hasParent = !string.IsNullOrEmpty(selected.ParentThreadId);
            m_HeaderTitle.text = DisplayTitle(selected);
            m_HeaderMeta.text = archived
                ? $"{selected.Cwd}  ·  {selected.Id}  ·  archived"
                : hasParent
                    ? $"{selected.Cwd}  ·  {selected.Id}  ·  subagent"
                    : $"{selected.Cwd}  ·  {selected.Id}";
            m_BackButton.style.display = hasParent && !archived ? DisplayStyle.Flex : DisplayStyle.None;
            m_BackButton.SetEnabled(hasParent && m_Workspace.IsConnected);
            m_CopyButton.SetEnabled(true);
            m_ForkButton.SetEnabled(
                !archived && m_Workspace.IsConnected && !m_Workspace.IsRunning && !m_Workspace.IsForking);
            m_ArchiveButton.SetEnabled(
                !archived && m_Workspace.IsConnected && !m_Workspace.IsRunning);
        }

        void RestoreSelectedThread()
        {
            string threadId = m_Workspace?.SelectedThreadId;
            if (string.IsNullOrEmpty(threadId))
            {
                return;
            }

            m_Workspace.UnarchiveThread(threadId, true);
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

            if (m_Workspace.ShowArchived)
            {
                var archivedItems = new List<PopupItem>
                {
                    new PopupItem
                    {
                        Label = "Restore and open",
                        Action = () => m_Workspace.UnarchiveThread(thread.Id, true),
                    },
                    new PopupItem
                    {
                        Label = "Copy resume",
                        Action = () => EditorGUIUtility.systemCopyBuffer = $"codex resume {thread.Id}",
                    },
                };
                ShowPopup(null, "ARCHIVED CONVERSATION", 230, archivedItems, false, position);
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
                return Truncate(CodexUnityContext.UserText(thread.Preview), 110);
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
