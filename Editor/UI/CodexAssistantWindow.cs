using System;
using System.IO;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Minerva.Codex
{
    /// <summary>
    /// Read-only view of the Codex thread store. The window renders what
    /// <see cref="CodexWorkspace"/> exposes and never talks to the protocol itself.
    /// </summary>
    internal sealed class CodexAssistantWindow : EditorWindow
    {
        const string LastThreadKey = "Minerva.Codex.LastThread";

        CodexWorkspace m_Workspace;
        ScrollView m_ThreadList;
        ScrollView m_Transcript;
        Label m_StatusLabel;
        Label m_HeaderTitle;
        Label m_HeaderMeta;
        Label m_ErrorLabel;
        Button m_RefreshButton;
        Button m_CopyButton;

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
            if (m_Workspace == null)
            {
                m_Workspace = new CodexWorkspace();
                m_Workspace.Changed += OnWorkspaceChanged;
                m_Workspace.InitialThreadId = EditorPrefs.GetString(LastThreadKey, string.Empty);
            }

            BuildUi();
            m_Workspace.Connect(ProjectRoot);
        }

        void OnDisable()
        {
            if (m_Workspace != null)
            {
                m_Workspace.Changed -= OnWorkspaceChanged;
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

            m_CopyButton = new Button(CopyResumeCommand) { text = "Copy resume" };
            m_CopyButton.style.marginLeft = 8;
            m_CopyButton.SetEnabled(false);
            header.Add(m_CopyButton);

            m_Transcript = new ScrollView(ScrollViewMode.Vertical);
            m_Transcript.style.flexGrow = 1;
            m_Transcript.style.paddingTop = 8;
            m_Transcript.verticalScrollerVisibility = ScrollerVisibility.Auto;
            content.Add(m_Transcript);

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
                m_Workspace.Connect(ProjectRoot);
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

            RebuildThreadList();
            RebuildTranscript();
            UpdateHeader();

            bool hasError = !string.IsNullOrEmpty(m_Workspace.Error);
            m_ErrorLabel.style.display = hasError ? DisplayStyle.Flex : DisplayStyle.None;
            m_ErrorLabel.text = hasError ? m_Workspace.Error : string.Empty;

            if (!string.IsNullOrEmpty(m_Workspace.SelectedThreadId))
            {
                EditorPrefs.SetString(LastThreadKey, m_Workspace.SelectedThreadId);
            }
        }

        void RebuildThreadList()
        {
            m_ThreadList.Clear();

            if (m_Workspace.Threads.Count == 0)
            {
                var empty = new Label("No conversations found.\nStart one in Codex CLI, then press Refresh.");
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

            var preview = new Label(Truncate(thread.Preview, 110));
            preview.style.whiteSpace = WhiteSpace.Normal;
            preview.style.fontSize = 11;
            preview.style.color = CodexPalette.Text;
            row.Add(preview);

            var meta = new Label($"{FolderName(thread.Cwd)}  ·  {Ago(thread.UpdatedAt)}");
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
            m_Transcript.Clear();

            if (string.IsNullOrEmpty(m_Workspace.SelectedThreadId))
            {
                AddPlaceholder("Select a conversation on the left.");
                return;
            }

            if (m_Workspace.Turns.Count == 0)
            {
                AddPlaceholder(m_Workspace.Error == null ? "Loading conversation..." : "Could not load this conversation.");
                return;
            }

            // The protocol returns newest first; read oldest to newest.
            for (int turnIndex = m_Workspace.Turns.Count - 1; turnIndex >= 0; turnIndex--)
            {
                JObject turn = m_Workspace.Turns[turnIndex];
                if (turn["items"] is not JArray items)
                {
                    continue;
                }

                foreach (JToken token in items)
                {
                    if (token is JObject item)
                    {
                        CodexItemRenderer.Render(m_Transcript, item);
                    }
                }
            }
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
                m_HeaderTitle.text = "No conversation selected";
                m_HeaderMeta.text = string.Empty;
                m_CopyButton.SetEnabled(false);
                return;
            }

            m_HeaderTitle.text = Truncate(selected.Preview, 120);
            m_HeaderMeta.text = $"{selected.Cwd}  ·  {selected.Id}";
            m_CopyButton.SetEnabled(true);
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
