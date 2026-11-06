using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Minerva.Codex
{
    /// <summary>A persistent shell terminal shared with the Assistant workspace.</summary>
    internal sealed class CodexConsoleWindow : EditorWindow
    {
        [SerializeField] string m_LastDirectory;
        [SerializeField] string m_Draft = "";
        [SerializeField] bool m_Local;
        CodexWorkspace m_Workspace;
        CodexTerminalSession m_Session;
        DropdownField m_Mode;
        Label m_Directory;
        Label m_Status;
        Label m_Error;
        Label m_Output;
        TextField m_Input;
        Button m_Submit;
        Button m_Interrupt;
        Button m_Restart;
        Button m_Folder;
        ScrollView m_Scroll;
        bool m_RefreshScheduled;
        bool m_ProgrammaticScroll;
        bool m_Follow = true;
        double m_NextBindingCheck;
        int m_HistoryIndex;
        string m_HistoryDraft;

        [MenuItem("Window/Minerva/Codex Terminal", priority = 3)]
        public static void Open()
        {
            CodexAssistantWindow.Open();
            Open(GetWindow<CodexAssistantWindow>().Workspace);
        }

        public static void Open(CodexWorkspace workspace)
        {
            var window = GetWindow<CodexConsoleWindow>();
            window.titleContent = new GUIContent("Codex Terminal");
            window.minSize = new Vector2(520, 340);
            window.Bind(workspace);
            window.Show();
        }

        void OnEnable()
        {
            titleContent = new GUIContent("Codex Terminal");
            minSize = new Vector2(520, 340);
            m_RefreshScheduled = false;
            BuildUi();
            EditorApplication.update += CheckBinding;
            EditorApplication.delayCall += CheckBinding;
        }

        void OnDisable()
        {
            EditorApplication.update -= CheckBinding;
            EditorApplication.delayCall -= CheckBinding;
            rootVisualElement.UnregisterCallback<KeyDownEvent>(OnTerminalShortcut);
            m_Draft = m_Input?.value ?? "";
            m_LastDirectory = m_Session?.Directory ?? m_LastDirectory;
            if (m_Session != null) m_Session.Changed -= OnSessionChanged;
            if (m_Workspace != null) m_Workspace.Changed -= OnWorkspaceChanged;
            m_Session = null;
            m_Workspace = null;
        }

        void CheckBinding()
        {
            if (EditorApplication.timeSinceStartup < m_NextBindingCheck) return;
            m_NextBindingCheck = EditorApplication.timeSinceStartup + 1;
            if (m_Workspace == null || !m_Workspace.IsConnected)
            {
                foreach (var assistant in Resources.FindObjectsOfTypeAll<CodexAssistantWindow>())
                {
                    if (assistant.Workspace != null && !ReferenceEquals(assistant.Workspace, m_Workspace))
                    {
                        Bind(assistant.Workspace);
                        return;
                    }
                }
            }
            OnWorkspaceChanged();
        }

        void Bind(CodexWorkspace workspace)
        {
            if (!ReferenceEquals(workspace, m_Workspace))
            {
                if (m_Workspace != null) m_Workspace.Changed -= OnWorkspaceChanged;
                m_Workspace = workspace;
                if (m_Workspace != null) m_Workspace.Changed += OnWorkspaceChanged;
            }
            OnWorkspaceChanged();
        }

        void OnWorkspaceChanged()
        {
            var session = m_Workspace?.Terminal;
            if (!ReferenceEquals(session, m_Session))
            {
                if (m_Session != null)
                {
                    m_LastDirectory = m_Session.Directory ?? m_LastDirectory;
                    m_Session.Changed -= OnSessionChanged;
                }
                m_Session = session;
                if (m_Session != null)
                {
                    m_Session.Changed += OnSessionChanged;
                    m_HistoryIndex = m_Session.History.Count;
                }
            }
            if (m_Workspace?.IsConnected == true && m_Session?.State == CodexShellState.Idle)
            {
                ResizePty();
                m_Session.Start(InitialDirectory(), m_Local);
            }
            Refresh();
        }

        string InitialDirectory() => !string.IsNullOrEmpty(m_LastDirectory) ? m_LastDirectory
            : m_Workspace?.SelectedCwd ?? Path.GetDirectoryName(Application.dataPath);

        void BuildUi()
        {
            var root = rootVisualElement;
            root.Clear();
            root.style.backgroundColor = CodexPalette.CodeBackground;
            root.style.minHeight = 0;
            var mono = EditorGUIUtility.Load("Fonts/RobotoMono/RobotoMono-Regular.ttf") as Font;

            var toolbar = new VisualElement();
            toolbar.style.flexDirection = FlexDirection.Row;
            toolbar.style.alignItems = Align.Center;
            toolbar.style.height = 38;
            toolbar.style.flexShrink = 0;
            toolbar.style.paddingLeft = toolbar.style.paddingRight = 10;
            toolbar.style.backgroundColor = CodexPalette.Panel;
            root.Add(toolbar);
            var title = Text(Application.platform == RuntimePlatform.WindowsEditor ? "PowerShell" : "bash", 12, CodexPalette.Text);
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.marginRight = 12;
            toolbar.Add(title);
            m_Mode = new DropdownField(new List<string> { "Sandboxed", "Local" }, m_Local ? 1 : 0);
            m_Mode.style.width = 108;
            m_Mode.style.fontSize = 10;
            m_Mode.tooltip = "Changing mode restarts the shell. Local runs on the host without a sandbox.";
            m_Mode.RegisterValueChangedCallback(_ =>
            {
                m_Local = m_Mode.index == 1;
                m_Session?.Start(m_Session.Directory ?? InitialDirectory(), m_Local);
            });
            toolbar.Add(m_Mode);
            var spacer = new VisualElement();
            spacer.style.flexGrow = 1;
            toolbar.Add(spacer);
            m_Interrupt = ActionButton(toolbar, "Interrupt", "Interrupt current command (Ctrl+C); keep the shell", () => m_Session?.Interrupt());
            m_Restart = ActionButton(toolbar, "Restart", "Restart the shell and reset its environment", () => m_Session?.Start(m_Session.Directory ?? InitialDirectory(), m_Local));
            ActionButton(toolbar, "Clear", "Clear output (Ctrl+L)", () => m_Session?.Clear());
            ActionButton(toolbar, "Copy", "Copy terminal output", () => EditorGUIUtility.systemCopyBuffer = m_Session?.Output ?? "");

            var directoryRow = new VisualElement();
            directoryRow.style.flexDirection = FlexDirection.Row;
            directoryRow.style.alignItems = Align.Center;
            directoryRow.style.height = 30;
            directoryRow.style.flexShrink = 0;
            directoryRow.style.paddingLeft = directoryRow.style.paddingRight = 10;
            directoryRow.style.borderBottomWidth = 1;
            directoryRow.style.borderBottomColor = CodexPalette.Border;
            root.Add(directoryRow);
            m_Directory = Text("", 10, CodexPalette.Muted);
            m_Directory.style.flexGrow = 1;
            m_Directory.style.flexShrink = 1;
            m_Directory.style.whiteSpace = WhiteSpace.NoWrap;
            m_Directory.style.overflow = Overflow.Hidden;
            m_Directory.style.textOverflow = TextOverflow.Ellipsis;
            directoryRow.Add(m_Directory);
            m_Folder = ActionButton(directoryRow, "Folder…", "Change shell directory", ChooseDirectory);
            m_Status = Text("Connecting…", 10, CodexPalette.Muted);
            m_Status.style.marginLeft = 10;
            directoryRow.Add(m_Status);
            m_Error = Text("", 11, CodexPalette.Error);
            m_Error.style.marginLeft = m_Error.style.marginRight = 10;
            m_Error.style.whiteSpace = WhiteSpace.Normal;
            root.Add(m_Error);

            m_Scroll = new ScrollView(ScrollViewMode.VerticalAndHorizontal) { name = "codex-terminal-output-scroll" };
            m_Scroll.style.flexGrow = 1;
            m_Scroll.style.minHeight = 0;
            m_Scroll.style.paddingLeft = m_Scroll.style.paddingRight = 10;
            m_Scroll.style.paddingTop = m_Scroll.style.paddingBottom = 8;
            // Stable gutters prevent output growth from resizing and repainting the remote PTY.
            m_Scroll.verticalScrollerVisibility = ScrollerVisibility.AlwaysVisible;
            m_Scroll.horizontalScrollerVisibility = ScrollerVisibility.AlwaysVisible;
            m_Scroll.contentViewport.style.alignSelf = Align.Stretch;
            root.Add(m_Scroll);
            m_Output = Text("", 12, CodexPalette.Text);
            m_Output.name = "codex-terminal-output";
            m_Output.style.whiteSpace = WhiteSpace.Pre;
            m_Output.style.unityFontDefinition = FontDefinition.FromFont(mono);
            m_Output.style.unityTextGenerator = TextGeneratorType.Standard;
            m_Output.selection.isSelectable = true;
            m_Scroll.Add(m_Output);
            m_Scroll.verticalScroller.valueChanged += _ => { if (!m_ProgrammaticScroll) m_Follow = AtBottom(); };
            m_Scroll.contentContainer.RegisterCallback<GeometryChangedEvent>(_ => { if (m_Follow) ScrollToBottom(); });
            m_Scroll.contentViewport.RegisterCallback<GeometryChangedEvent>(_ => ResizePty());

            var inputRow = new VisualElement();
            inputRow.style.flexDirection = FlexDirection.Row;
            inputRow.style.alignItems = Align.Center;
            inputRow.style.minHeight = 40;
            inputRow.style.flexShrink = 0;
            inputRow.style.paddingLeft = inputRow.style.paddingRight = 10;
            inputRow.style.paddingTop = inputRow.style.paddingBottom = 6;
            inputRow.style.backgroundColor = CodexPalette.Panel;
            inputRow.style.borderTopWidth = 1;
            inputRow.style.borderTopColor = CodexPalette.Border;
            root.Add(inputRow);
            var prompt = Text(">", 14, CodexPalette.Accent);
            prompt.style.marginRight = 8;
            inputRow.Add(prompt);
            m_Input = new TextField { name = "codex-terminal-command", multiline = true };
            m_Input.style.flexGrow = 1;
            m_Input.style.flexShrink = 1;
            m_Input.style.maxHeight = 90;
            m_Input.style.unityTextGenerator = TextGeneratorType.Standard;
            m_Input.style.unityFontDefinition = FontDefinition.FromFont(mono);
            m_Input.style.fontSize = 12;
            m_Input.SetValueWithoutNotify(m_Draft);
            m_Input.textEdition.hidePlaceholderOnFocus = false;
            m_Input.RegisterCallback<KeyDownEvent>(OnInputKeyDown, TrickleDown.TrickleDown);
            m_Input.RegisterCallback<AttachToPanelEvent>(_ => StyleInput());
            m_Input.schedule.Execute(StyleInput);
            inputRow.Add(m_Input);
            m_Submit = ActionButton(inputRow, "↵", "Enter to submit · Shift+Enter for newline", SendInput);
            root.UnregisterCallback<KeyDownEvent>(OnTerminalShortcut);
            root.RegisterCallback<KeyDownEvent>(OnTerminalShortcut);
            Refresh();
        }

        void StyleInput()
        {
            var input = m_Input.Q(className: "unity-base-text-field__input");
            if (input == null) return;
            input.style.backgroundColor = Color.clear;
            input.style.color = CodexPalette.Text;
            input.style.borderTopWidth = input.style.borderBottomWidth = 0;
            input.style.borderLeftWidth = input.style.borderRightWidth = 0;
            input.style.whiteSpace = WhiteSpace.Normal;
        }

        void OnTerminalShortcut(KeyDownEvent evt)
        {
            if ((evt.ctrlKey || evt.commandKey) && evt.keyCode == KeyCode.L)
            {
                m_Session?.Clear();
                evt.StopPropagation();
            }
        }

        void OnInputKeyDown(KeyDownEvent evt)
        {
            if (!string.IsNullOrEmpty(Input.compositionString)) return;
            if ((evt.ctrlKey || evt.commandKey) && evt.keyCode == KeyCode.C && m_Input.cursorIndex == m_Input.selectIndex)
            {
                m_Session?.Interrupt();
                m_Input.SetValueWithoutNotify("");
                CodexTextEditing.CaretAtEnd(m_Input);
                evt.StopPropagation();
            }
            else if ((evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter) && !evt.shiftKey)
            {
                SendInput();
                evt.StopPropagation();
            }
            else if ((evt.keyCode == KeyCode.UpArrow || evt.keyCode == KeyCode.DownArrow) && !m_Input.value.Contains("\n"))
            {
                var history = m_Session?.History;
                if (history == null || history.Count == 0) return;
                if (m_HistoryIndex >= history.Count) m_HistoryDraft = m_Input.value;
                m_HistoryIndex = Mathf.Clamp(m_HistoryIndex + (evt.keyCode == KeyCode.UpArrow ? -1 : 1), 0, history.Count);
                m_Input.SetValueWithoutNotify(m_HistoryIndex < history.Count ? history[m_HistoryIndex] : m_HistoryDraft ?? "");
                CodexTextEditing.CaretAtEnd(m_Input);
                evt.StopPropagation();
            }
        }

        void SendInput()
        {
            if (m_Session?.Send(m_Input.value) != true) return;
            m_HistoryIndex = m_Session.History.Count;
            m_HistoryDraft = "";
            m_Input.SetValueWithoutNotify("");
            CodexTextEditing.CaretAtEnd(m_Input);
            m_Input.schedule.Execute(() => CodexTextEditing.CaretAtEnd(m_Input));
            m_Follow = true;
            m_Input.Focus();
        }

        void ChooseDirectory()
        {
            string initial = m_Session?.Directory ?? InitialDirectory();
            if (!Directory.Exists(initial)) initial = Path.GetDirectoryName(Application.dataPath);
            string directory = EditorUtility.OpenFolderPanel("Terminal directory", initial, "");
            if (string.IsNullOrEmpty(directory)) return;
            if (m_Session.State == CodexShellState.Exited || m_Session.State == CodexShellState.Idle)
            {
                m_Session.Start(directory, m_Local);
                return;
            }
            string command = Application.platform == RuntimePlatform.WindowsEditor
                ? "Set-Location -LiteralPath '" + directory.Replace("'", "''") + "'"
                : "cd '" + directory.Replace("'", "'\\''") + "'";
            m_Session?.Send(command);
            m_HistoryIndex = m_Session?.History.Count ?? 0;
        }

        void OnSessionChanged()
        {
            if (m_RefreshScheduled) return;
            m_RefreshScheduled = true;
            rootVisualElement.schedule.Execute(() =>
            {
                m_RefreshScheduled = false;
                Refresh();
            }).ExecuteLater(35);
        }

        void Refresh()
        {
            if (m_Input == null) return;
            bool connected = m_Workspace?.IsConnected == true;
            bool canInput = connected && m_Session?.CanInput == true;
            m_Input.SetEnabled(canInput);
            m_Submit.SetEnabled(canInput);
            m_Interrupt.SetEnabled(canInput);
            m_Restart.SetEnabled(connected && m_Session?.State != CodexShellState.Stopping);
            m_Folder.SetEnabled(connected && (m_Session?.State == CodexShellState.Ready || m_Session?.State == CodexShellState.Exited));
            m_Mode.SetEnabled(connected && (m_Session?.State == CodexShellState.Ready || m_Session?.State == CodexShellState.Exited));
            if (m_Session != null && m_Session.State != CodexShellState.Idle && m_Session.State != CodexShellState.Stopping)
            {
                m_Local = m_Session.Local;
                m_Mode.SetValueWithoutNotify(m_Local ? "Local" : "Sandboxed");
            }
            m_Directory.text = m_Session?.Directory ?? InitialDirectory();
            m_Directory.tooltip = m_Directory.text;
            m_Error.text = m_Session?.Error ?? "";
            m_Error.style.display = string.IsNullOrEmpty(m_Error.text) ? DisplayStyle.None : DisplayStyle.Flex;
            m_Status.text = !connected ? "Disconnected" : m_Session?.State switch
            {
                CodexShellState.Ready => m_Session.LastCommandSucceeded == false ? "Command failed" : "Ready",
                CodexShellState.Executing => "Running",
                CodexShellState.Stopping => "Restarting…",
                CodexShellState.Exited => m_Session.ExitCode.HasValue ? "Shell exited · " + m_Session.ExitCode : "Shell exited",
                _ => "Starting…",
            };
            m_Status.style.color = m_Session?.State == CodexShellState.Ready
                ? m_Session.LastCommandSucceeded == false ? CodexPalette.Error : CodexPalette.Success
                : CodexPalette.Muted;
            m_Input.textEdition.placeholder = m_Session?.State == CodexShellState.Executing
                ? "Send input to the running program…" : "Type a command · ↑↓ history · Ctrl+C interrupt";
            string output = m_Session?.Output ?? "";
            if (m_Output.text != output)
            {
                m_Output.text = output;
                if (m_Follow) m_Scroll.schedule.Execute(ScrollToBottom);
            }
            ResizePty();
        }

        void ResizePty()
        {
            if (m_Output.panel == null) return;
            var area = m_Scroll.contentViewport.layout;
            if (area.width <= 0 || area.height <= 0) return;
            Vector2 cell = m_Output.MeasureTextSize("MMMMMMMMMM", 0, VisualElement.MeasureMode.Undefined, 0, VisualElement.MeasureMode.Undefined);
            float lineHeight = m_Output.MeasureTextSize("M\nM", 0, VisualElement.MeasureMode.Undefined, 0, VisualElement.MeasureMode.Undefined).y - cell.y;
            if (cell.x <= 0 || lineHeight <= 0) return;
            m_Session?.Resize((int)(area.width / (cell.x / 10)), (int)(area.height / lineHeight));
        }

        bool AtBottom()
        {
            var scroller = m_Scroll.verticalScroller;
            return scroller.highValue <= 0 || scroller.value >= scroller.highValue - 24;
        }

        void ScrollToBottom()
        {
            m_ProgrammaticScroll = true;
            try { m_Scroll.scrollOffset = new Vector2(m_Scroll.scrollOffset.x, Math.Max(0, m_Scroll.verticalScroller.highValue)); }
            finally { m_ProgrammaticScroll = false; }
        }

        static Label Text(string text, int size, Color color)
        {
            var label = new Label(text) { enableRichText = false };
            label.style.fontSize = size;
            label.style.color = color;
            return label;
        }

        static Button ActionButton(VisualElement parent, string text, string tooltip, Action action)
        {
            var button = new Button(action) { text = text, tooltip = tooltip };
            button.style.height = 24;
            button.style.minWidth = 28;
            button.style.fontSize = 10;
            button.style.marginLeft = 5;
            button.style.paddingLeft = button.style.paddingRight = 7;
            button.style.backgroundColor = CodexPalette.Pill;
            button.style.color = CodexPalette.Text;
            button.style.borderTopWidth = button.style.borderBottomWidth = 0;
            button.style.borderLeftWidth = button.style.borderRightWidth = 0;
            button.style.borderTopLeftRadius = button.style.borderTopRightRadius = 5;
            button.style.borderBottomLeftRadius = button.style.borderBottomRightRadius = 5;
            parent.Add(button);
            return button;
        }
    }
}
