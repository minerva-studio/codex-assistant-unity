using System;
using System.Collections.Generic;
using System.IO;
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
        Button m_CopyButton;
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
        VisualElement m_LiveContainer;
        VisualElement m_ComposerCard;
        VisualElement m_Popup;
        VisualElement m_PopupScrim;

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
            window.minSize = new Vector2(720, 420);
            if (window.position.width < 720 || window.position.height < 420)
            {
                window.position = new Rect(window.position.x, window.position.y, 1100, 720);
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
        }

        VisualElement BuildToolbar()
        {
            var toolbar = new VisualElement();
            toolbar.style.flexDirection = FlexDirection.Row;
            toolbar.style.alignItems = Align.Center;
            toolbar.style.height = 28;
            toolbar.style.paddingLeft = 8;
            toolbar.style.paddingRight = 8;
            toolbar.style.borderBottomWidth = 1;
            toolbar.style.borderBottomColor = CodexPalette.Border;

            var title = new Label("Codex");
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            toolbar.Add(title);

            m_RefreshButton = new Button(RefreshOrReconnect) { text = "Refresh" };
            m_RefreshButton.style.marginLeft = 12;
            toolbar.Add(m_RefreshButton);

            m_SettingsButton = new Button(CodexAssistantSettingsWindow.Open) { text = "Settings" };
            m_SettingsButton.style.marginLeft = 6;
            m_SettingsButton.tooltip = "Display and composer preferences";
            toolbar.Add(m_SettingsButton);

            m_StatusLabel = new Label("Starting...");
            m_StatusLabel.style.marginLeft = 10;
            m_StatusLabel.style.color = CodexPalette.Muted;
            m_StatusLabel.style.flexGrow = 1;
            toolbar.Add(m_StatusLabel);

            return toolbar;
        }

        VisualElement BuildSidebar()
        {
            var sidebar = new VisualElement();
            sidebar.style.width = 270;
            sidebar.style.flexShrink = 0;
            sidebar.style.borderRightWidth = 1;
            sidebar.style.borderRightColor = CodexPalette.Border;
            sidebar.style.backgroundColor = CodexPalette.Sidebar;

            var filters = new VisualElement();
            filters.style.paddingLeft = 8;
            filters.style.paddingRight = 8;
            filters.style.paddingTop = 8;
            filters.style.paddingBottom = 6;
            filters.style.borderBottomWidth = 1;
            filters.style.borderBottomColor = CodexPalette.Border;

            m_NewButton = new Button(NewConversation) { text = "+  New conversation", name = "codex-new" };
            m_NewButton.style.height = 26;
            m_NewButton.style.marginBottom = 6;
            m_NewButton.style.paddingLeft = 10;
            m_NewButton.style.paddingRight = 10;
            m_NewButton.style.borderTopLeftRadius = 8;
            m_NewButton.style.borderTopRightRadius = 8;
            m_NewButton.style.borderBottomLeftRadius = 8;
            m_NewButton.style.borderBottomRightRadius = 8;
            m_NewButton.style.borderTopWidth = 1;
            m_NewButton.style.borderBottomWidth = 1;
            m_NewButton.style.borderLeftWidth = 1;
            m_NewButton.style.borderRightWidth = 1;
            m_NewButton.style.borderTopColor = CodexPalette.PillBorder;
            m_NewButton.style.borderBottomColor = CodexPalette.PillBorder;
            m_NewButton.style.borderLeftColor = CodexPalette.PillBorder;
            m_NewButton.style.borderRightColor = CodexPalette.PillBorder;
            m_NewButton.style.backgroundColor = CodexPalette.Pill;
            m_NewButton.style.color = CodexPalette.Text;
            m_NewButton.style.unityTextAlign = TextAnchor.MiddleLeft;
            m_NewButton.tooltip = "Start a new conversation on the next message";
            filters.Add(m_NewButton);

            m_SearchField = new TextField { name = "codex-thread-search" };
            m_SearchField.style.height = 24;
            m_SearchField.textEdition.placeholder = "Search conversations...";
            m_SearchField.textEdition.hidePlaceholderOnFocus = false;
            m_SearchField.RegisterValueChangedCallback(evt => m_Workspace?.SetSearchTerm(evt.newValue));
            filters.Add(m_SearchField);

            m_ScopeField = new DropdownField { name = "codex-thread-scope" };
            m_ScopeField.choices = new List<string> { ScopeProject, ScopeAll };
            m_ScopeField.SetValueWithoutNotify(ScopeProject);
            m_ScopeField.style.height = 22;
            m_ScopeField.style.marginTop = 6;
            m_ScopeField.tooltip = "Show conversations from this project or from every folder";
            m_ScopeField.RegisterValueChangedCallback(evt =>
                m_Workspace?.SetShowAllProjects(evt.newValue == ScopeAll));
            filters.Add(m_ScopeField);
            sidebar.Add(filters);

            m_ThreadList = new ScrollView(ScrollViewMode.Vertical);
            m_ThreadList.style.flexGrow = 1;
            m_ThreadList.style.paddingTop = 4;
            sidebar.Add(m_ThreadList);

            return sidebar;
        }

        VisualElement BuildContent()
        {
            var content = new VisualElement();
            content.style.flexGrow = 1;
            content.style.flexDirection = FlexDirection.Column;

            var header = new VisualElement();
            header.style.flexDirection = FlexDirection.Row;
            header.style.alignItems = Align.Center;
            header.style.paddingLeft = 12;
            header.style.paddingRight = 12;
            header.style.paddingTop = 8;
            header.style.paddingBottom = 8;
            header.style.borderBottomWidth = 1;
            header.style.borderBottomColor = CodexPalette.Border;
            content.Add(header);

            var headerText = new VisualElement();
            headerText.style.flexGrow = 1;
            header.Add(headerText);

            m_HeaderTitle = new Label("No conversation selected");
            m_HeaderTitle.style.whiteSpace = WhiteSpace.Normal;
            m_HeaderTitle.style.unityFontStyleAndWeight = FontStyle.Bold;
            headerText.Add(m_HeaderTitle);

            m_HeaderMeta = new Label(string.Empty);
            m_HeaderMeta.style.fontSize = 10;
            m_HeaderMeta.style.color = CodexPalette.Muted;
            m_HeaderMeta.style.whiteSpace = WhiteSpace.Normal;
            headerText.Add(m_HeaderMeta);

            m_ArchiveButton = new Button(ArchiveSelectedThread) { text = "Archive" };
            m_ArchiveButton.style.marginLeft = 8;
            m_ArchiveButton.SetEnabled(false);
            m_ArchiveButton.tooltip = "Archive the selected conversation";
            header.Add(m_ArchiveButton);

            m_CopyButton = new Button(CopyResumeCommand) { text = "Copy resume" };
            m_CopyButton.style.marginLeft = 8;
            m_CopyButton.SetEnabled(false);
            header.Add(m_CopyButton);

            m_Transcript = new ScrollView(ScrollViewMode.Vertical);
            m_Transcript.style.flexGrow = 1;
            m_Transcript.style.paddingTop = 8;
            m_Transcript.verticalScrollerVisibility = ScrollerVisibility.Auto;
            content.Add(m_Transcript);

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

        VisualElement BuildComposer()
        {
            var composer = new VisualElement();
            composer.style.flexShrink = 0;
            composer.style.backgroundColor = CodexPalette.Sidebar;
            composer.style.borderTopWidth = 1;
            composer.style.borderTopColor = CodexPalette.Border;
            composer.style.paddingLeft = 12;
            composer.style.paddingRight = 12;
            composer.style.paddingTop = 8;
            composer.style.paddingBottom = 8;

            m_ComposerCard = new VisualElement();
            m_ComposerCard.style.flexDirection = FlexDirection.Column;
            m_ComposerCard.style.backgroundColor = CodexPalette.ComposerCard;
            m_ComposerCard.style.borderTopWidth = 1;
            m_ComposerCard.style.borderBottomWidth = 1;
            m_ComposerCard.style.borderLeftWidth = 1;
            m_ComposerCard.style.borderRightWidth = 1;
            m_ComposerCard.style.borderTopColor = CodexPalette.PillBorder;
            m_ComposerCard.style.borderBottomColor = CodexPalette.PillBorder;
            m_ComposerCard.style.borderLeftColor = CodexPalette.PillBorder;
            m_ComposerCard.style.borderRightColor = CodexPalette.PillBorder;
            m_ComposerCard.style.borderTopLeftRadius = 10;
            m_ComposerCard.style.borderTopRightRadius = 10;
            m_ComposerCard.style.borderBottomLeftRadius = 10;
            m_ComposerCard.style.borderBottomRightRadius = 10;
            m_ComposerCard.style.paddingLeft = 8;
            m_ComposerCard.style.paddingRight = 8;
            m_ComposerCard.style.paddingTop = 6;
            m_ComposerCard.style.paddingBottom = 6;
            composer.Add(m_ComposerCard);

            m_Composer = new TextField { multiline = true, name = "codex-composer" };
            m_Composer.style.flexGrow = 1;
            m_Composer.style.minHeight = 46;
            m_Composer.style.maxHeight = 160;
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

            var hint = new Label("Enter to send  ·  Shift+Enter for a new line");
            hint.style.fontSize = 9;
            hint.style.color = CodexPalette.Muted;
            hint.style.flexGrow = 1;
            meta.Add(hint);

            m_SettingsSourceLabel = new Label(string.Empty);
            m_SettingsSourceLabel.name = "codex-settings-source";
            m_SettingsSourceLabel.style.fontSize = 9;
            m_SettingsSourceLabel.style.color = CodexPalette.Muted;
            m_SettingsSourceLabel.style.marginLeft = 10;
            m_SettingsSourceLabel.style.whiteSpace = WhiteSpace.Normal;
            m_SettingsSourceLabel.style.unityTextAlign = TextAnchor.MiddleRight;
            meta.Add(m_SettingsSourceLabel);

            var actions = new VisualElement();
            actions.style.flexDirection = FlexDirection.Row;
            actions.style.alignItems = Align.Center;
            actions.style.marginTop = 4;
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
            m_ActionButton.style.height = 26;
            m_ActionButton.style.minWidth = 64;
            m_ActionButton.style.paddingLeft = 12;
            m_ActionButton.style.paddingRight = 12;
            m_ActionButton.style.marginLeft = 2;
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
            button.style.height = 24;
            button.style.minWidth = 28;
            button.style.paddingLeft = 10;
            button.style.paddingRight = 10;
            button.style.paddingTop = 2;
            button.style.paddingBottom = 2;
            button.style.marginRight = 6;
            button.style.borderTopLeftRadius = 12;
            button.style.borderTopRightRadius = 12;
            button.style.borderBottomLeftRadius = 12;
            button.style.borderBottomRightRadius = 12;
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

        void ShowPopup(VisualElement anchor, string title, float width, List<PopupItem> items, bool searchable)
        {
            ClosePopup();
            if (anchor == null)
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

            Rect anchorBounds = anchor.worldBound;
            Rect rootBounds = rootVisualElement.worldBound;
            float left = anchorBounds.x - rootBounds.x;
            left = Mathf.Clamp(left, 8f, Mathf.Max(8f, rootVisualElement.layout.width - width - 8f));
            float top = anchorBounds.y - rootBounds.y - popupHeight - 6f;
            m_Popup.style.left = left;
            m_Popup.style.top = Mathf.Max(8f, top);
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
            m_Workspace.Connect(ProjectRoot, m_CodexHome);
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

            m_Composer.SetEnabled(ready && settingsReady && !running);

            bool actionEnabled = running
                ? m_Workspace != null && m_Workspace.CanStop
                : ready && settingsReady && hasText;

            m_ActionButton.text = running ? "Stop" : "Send";
            m_ActionButton.SetEnabled(actionEnabled);
            m_ActionButton.style.backgroundColor = !actionEnabled
                ? CodexPalette.DisabledAction
                : running ? CodexPalette.StopAction : CodexPalette.SendAction;
            m_ActionButton.tooltip = running ? "Interrupt the running turn" : "Send message";
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

            if (keyEvent.shiftKey || keyEvent.ctrlKey || keyEvent.commandKey || keyEvent.altKey)
            {
                return;
            }

            keyEvent.StopPropagation();
            SendComposer();
        }

        void SendComposer()
        {
            if (m_Workspace == null)
            {
                return;
            }

            string text = m_Composer.value?.Trim();
            if (string.IsNullOrEmpty(text) || !m_Workspace.SendMessage(text))
            {
                return;
            }

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
            row.style.paddingLeft = 8;
            row.style.paddingRight = 8;
            row.style.paddingTop = 6;
            row.style.paddingBottom = 6;
            row.style.marginLeft = 4;
            row.style.marginRight = 4;
            row.style.marginBottom = 2;
            row.style.borderTopLeftRadius = 4;
            row.style.borderTopRightRadius = 4;
            row.style.borderBottomLeftRadius = 4;
            row.style.borderBottomRightRadius = 4;

            if (selected)
            {
                row.style.backgroundColor = CodexPalette.Selected;
            }

            var title = new Label(DisplayTitle(thread));
            title.style.whiteSpace = WhiteSpace.Normal;
            title.style.fontSize = 11;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.color = CodexPalette.Text;
            row.Add(title);

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
                    preview.style.whiteSpace = WhiteSpace.Normal;
                    preview.style.fontSize = 10;
                    preview.style.color = CodexPalette.Muted;
                    preview.style.marginTop = 2;
                    row.Add(preview);
                }
            }

            string metaText = m_Workspace.ShowAllProjects
                ? $"{FolderName(thread.Cwd)}  ·  {Ago(thread.UpdatedAt)}"
                : Ago(thread.UpdatedAt);
            var meta = new Label(metaText);
            meta.style.fontSize = 9;
            meta.style.color = CodexPalette.Muted;
            meta.style.marginTop = 3;
            row.Add(meta);

            string id = thread.Id;
            row.RegisterCallback<ClickEvent>(_ => m_Workspace.SelectThread(id));
            row.RegisterCallback<MouseEnterEvent>(_ =>
            {
                if (id != m_Workspace.SelectedThreadId)
                {
                    row.style.backgroundColor = CodexPalette.Sidebar;
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
            Vector2 previousOffset = m_Transcript.scrollOffset;
            bool stickToBottom = IsTranscriptAtBottom();

            m_Transcript.Clear();
            m_LiveContainer = null;

            if (string.IsNullOrEmpty(m_Workspace.SelectedThreadId))
            {
                AddPlaceholder("Type a message below to start a new conversation, or select one on the left.");
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
                RenderTurn(m_Transcript, m_Workspace.Turns[turnIndex]);
            }

            if (liveTurn != null)
            {
                m_LiveContainer = new VisualElement();
                m_Transcript.Add(m_LiveContainer);
                RenderTurn(m_LiveContainer, liveTurn);
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
                return;
            }

            Vector2 previousOffset = m_Transcript.scrollOffset;
            bool stickToBottom = IsTranscriptAtBottom();

            m_LiveContainer.Clear();
            RenderTurn(m_LiveContainer, liveTurn);

            RestoreScroll(previousOffset, stickToBottom);
        }

        void RenderTurn(VisualElement parent, JObject turn)
        {
            if (turn["items"] is not JArray items)
            {
                return;
            }

            var process = new List<JObject>();
            foreach (JToken token in items)
            {
                if (token is not JObject item)
                {
                    continue;
                }

                string type = item["type"]?.Value<string>();
                if (type == "userMessage" || type == "agentMessage")
                {
                    FlushProcess(parent, process);
                    CodexItemRenderer.Render(parent, item, m_ShowReasoning, m_ShowToolCalls);
                }
                else
                {
                    process.Add(item);
                }
            }

            FlushProcess(parent, process);
        }

        void FlushProcess(VisualElement parent, List<JObject> items)
        {
            if (items.Count == 0)
            {
                return;
            }

            int tools = 0;
            int thoughts = 0;
            foreach (JObject item in items)
            {
                if (item["type"]?.Value<string>() == "reasoning")
                {
                    if (m_ShowReasoning)
                    {
                        thoughts++;
                    }
                }
                else if (m_ShowToolCalls)
                {
                    tools++;
                }
            }

            if (tools > 0 || thoughts > 0)
            {
                var foldout = new Foldout
                {
                    text = ProcessSummary(tools, thoughts),
                    value = false,
                };

                foldout.style.marginLeft = 12;
                foldout.style.marginRight = 12;
                foldout.style.marginTop = 2;
                foldout.style.marginBottom = 6;
                foldout.style.color = CodexPalette.Muted;

                foreach (JObject item in items)
                {
                    CodexItemRenderer.Render(foldout, item, m_ShowReasoning, m_ShowToolCalls);
                }

                parent.Add(foldout);
            }

            items.Clear();
        }

        static string ProcessSummary(int tools, int thoughts)
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

            return text;
        }

        bool IsTranscriptAtBottom()
        {
            float contentHeight = m_Transcript.contentContainer.layout.height;
            return contentHeight <= 0 ||
                   m_Transcript.scrollOffset.y + m_Transcript.layout.height >= contentHeight - 24;
        }

        void RestoreScroll(Vector2 previousOffset, bool stickToBottom)
        {
            m_Transcript.schedule.Execute(() =>
            {
                m_Transcript.scrollOffset = stickToBottom
                    ? new Vector2(0, m_Transcript.contentContainer.layout.height)
                    : previousOffset;
            });
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
                m_CopyButton.SetEnabled(hasThread);
                m_ArchiveButton.SetEnabled(hasThread && m_Workspace.IsConnected && !m_Workspace.IsRunning);
                return;
            }

            m_HeaderTitle.text = DisplayTitle(selected);
            m_HeaderMeta.text = $"{selected.Cwd}  ·  {selected.Id}";
            m_CopyButton.SetEnabled(true);
            m_ArchiveButton.SetEnabled(m_Workspace.IsConnected && !m_Workspace.IsRunning);
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
