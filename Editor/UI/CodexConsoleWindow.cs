using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Minerva.Codex
{
    /// <summary>
    /// Standalone console backed by app-server <c>command/exec</c> (sandboxed)
    /// or <c>process/spawn</c> (unsandboxed). It deliberately has no thread or
    /// turn: output and exit live on <see cref="CodexWorkspace"/> so the window
    /// can be closed and reopened while a command runs.
    /// </summary>
    internal sealed class CodexConsoleWindow : EditorWindow
    {
        CodexWorkspace m_Workspace;
        TextField m_Command;
        TextField m_Cwd;
        DropdownField m_Mode;
        Button m_RunButton;
        Button m_ClearButton;
        Label m_Status;
        ScrollView m_OutputScroll;
        Label m_Output;
        bool m_FollowOutput = true;
        bool m_RefreshScheduled;
        bool m_ProgrammaticScroll;
        bool m_LastConnected;
        double m_NextBindingCheck;

        [MenuItem("Window/Minerva/Codex Console", priority = 3)]
        public static void Open()
        {
            CodexAssistantWindow.Open();
            var assistant = GetWindow<CodexAssistantWindow>();
            Open(assistant.Workspace);
        }

        public static void Open(CodexWorkspace workspace)
        {
            var window = GetWindow<CodexConsoleWindow>();
            window.titleContent = new GUIContent("Codex Console");
            window.minSize = new Vector2(620, 420);
            window.Bind(workspace);
            window.Show();
        }

        void OnEnable()
        {
            BuildUi();
            // A domain reload drops the reference to the workspace while the
            // assistant window reconnects. Pick its workspace up again on the
            // next editor tick instead of requiring a reopen.
            EditorApplication.delayCall += RebindAssistant;
            EditorApplication.update += CheckWorkspaceBinding;
        }

        void CheckWorkspaceBinding()
        {
            if (EditorApplication.timeSinceStartup < m_NextBindingCheck)
            {
                return;
            }

            m_NextBindingCheck = EditorApplication.timeSinceStartup + 1.0;

            bool connected = m_Workspace != null && m_Workspace.IsConnected;
            if (connected != m_LastConnected)
            {
                m_LastConnected = connected;
                Refresh();
            }

            if (connected)
            {
                return;
            }

            foreach (CodexAssistantWindow assistant in
                     Resources.FindObjectsOfTypeAll<CodexAssistantWindow>())
            {
                if (assistant.Workspace != null &&
                    !ReferenceEquals(assistant.Workspace, m_Workspace))
                {
                    Bind(assistant.Workspace);
                    return;
                }
            }
        }

        void RebindAssistant()
        {
            if (m_Workspace != null || this == null)
            {
                return;
            }

            foreach (CodexAssistantWindow assistant in
                     Resources.FindObjectsOfTypeAll<CodexAssistantWindow>())
            {
                if (assistant.Workspace != null)
                {
                    Bind(assistant.Workspace);
                    return;
                }
            }
        }

        void OnDisable()
        {
            EditorApplication.update -= CheckWorkspaceBinding;
            if (m_Workspace != null)
            {
                m_Workspace.ConsoleChanged -= OnConsoleChanged;
            }
        }

        void Bind(CodexWorkspace workspace)
        {
            if (ReferenceEquals(m_Workspace, workspace))
            {
                Refresh();
                return;
            }

            if (m_Workspace != null)
            {
                m_Workspace.ConsoleChanged -= OnConsoleChanged;
            }

            m_Workspace = workspace;
            if (m_Workspace != null)
            {
                m_Workspace.ConsoleChanged += OnConsoleChanged;
            }

            if (rootVisualElement.childCount > 0)
            {
                if (m_Cwd != null && string.IsNullOrEmpty(m_Cwd.value))
                {
                    m_Cwd.SetValueWithoutNotify(DefaultCwd());
                }

                Refresh();
            }
        }

        string DefaultCwd()
        {
            if (m_Workspace != null && !string.IsNullOrEmpty(m_Workspace.SelectedCwd))
            {
                return m_Workspace.SelectedCwd;
            }

            return Path.GetDirectoryName(Application.dataPath);
        }

        void BuildUi()
        {
            rootVisualElement.Clear();
            rootVisualElement.style.backgroundColor = CodexPalette.Window;
            rootVisualElement.style.paddingLeft = 14;
            rootVisualElement.style.paddingRight = 14;
            rootVisualElement.style.paddingTop = 12;
            rootVisualElement.style.paddingBottom = 12;

            var title = new Label("Codex Console");
            title.style.fontSize = 14;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.color = CodexPalette.Text;
            rootVisualElement.Add(title);

            var subtitle = new Label(
                "Run a program with arguments. Shell aliases and commands need a shell, for example " +
                (Application.platform == RuntimePlatform.WindowsEditor ? "cmd.exe /c dir or powershell.exe -NoProfile -Command ls." : "/bin/sh -c \"ls -la\"."));
            subtitle.style.fontSize = 10;
            subtitle.style.color = CodexPalette.Muted;
            subtitle.style.whiteSpace = WhiteSpace.Normal;
            subtitle.style.marginTop = 2;
            subtitle.style.marginBottom = 8;
            rootVisualElement.Add(subtitle);

            var commandRow = new VisualElement();
            commandRow.style.flexDirection = FlexDirection.Row;
            commandRow.style.alignItems = Align.Center;
            rootVisualElement.Add(commandRow);

            m_Mode = new DropdownField(new List<string> { "Sandboxed", "Local" }, 0);
            m_Mode.style.width = 120;
            m_Mode.style.height = 26;
            m_Mode.style.marginRight = 6;
            commandRow.Add(m_Mode);

            m_Command = new TextField { name = "codex-console-command" };
            m_Command.style.flexGrow = 1;
            m_Command.style.height = 26;
            m_Command.textEdition.placeholder = Application.platform == RuntimePlatform.WindowsEditor
                ? "cmd.exe /c dir"
                : "/bin/ls -la";
            m_Command.textEdition.hidePlaceholderOnFocus = true;
            m_Command.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter)
                {
                    evt.StopPropagation();
                    RunOrStop();
                }
            }, TrickleDown.TrickleDown);
            commandRow.Add(m_Command);

            m_RunButton = new Button(RunOrStop) { text = "Run" };
            StyleButton(m_RunButton, true);
            m_RunButton.style.marginLeft = 6;
            commandRow.Add(m_RunButton);

            m_ClearButton = new Button(Clear) { text = "Clear" };
            StyleButton(m_ClearButton, false);
            m_ClearButton.style.marginLeft = 4;
            commandRow.Add(m_ClearButton);

            var copyButton = new Button(CopyOutput) { text = "Copy" };
            StyleButton(copyButton, false);
            copyButton.style.marginLeft = 4;
            commandRow.Add(copyButton);

            var cwdRow = new VisualElement();
            cwdRow.style.flexDirection = FlexDirection.Row;
            cwdRow.style.alignItems = Align.Center;
            cwdRow.style.marginTop = 6;
            rootVisualElement.Add(cwdRow);

            var cwdLabel = new Label("Working directory");
            cwdLabel.style.fontSize = 9;
            cwdLabel.style.color = CodexPalette.Muted;
            cwdLabel.style.width = 110;
            cwdRow.Add(cwdLabel);

            m_Cwd = new TextField { name = "codex-console-cwd" };
            m_Cwd.style.flexGrow = 1;
            m_Cwd.style.height = 24;
            m_Cwd.SetValueWithoutNotify(string.Empty);
            m_Cwd.textEdition.placeholder = "Project root (default)";
            m_Cwd.textEdition.hidePlaceholderOnFocus = false;
            cwdRow.Add(m_Cwd);

            m_Status = new Label("Ready");
            m_Status.style.fontSize = 9;
            m_Status.style.color = CodexPalette.Muted;
            m_Status.style.marginTop = 6;
            m_Status.style.marginBottom = 4;
            m_Status.style.whiteSpace = WhiteSpace.Normal;
            rootVisualElement.Add(m_Status);

            m_OutputScroll = new ScrollView(ScrollViewMode.Vertical);
            m_OutputScroll.style.flexGrow = 1;
            m_OutputScroll.style.minHeight = 160;
            m_OutputScroll.style.backgroundColor = CodexPalette.CodeBackground;
            m_OutputScroll.style.borderTopWidth = 1;
            m_OutputScroll.style.borderBottomWidth = 1;
            m_OutputScroll.style.borderLeftWidth = 1;
            m_OutputScroll.style.borderRightWidth = 1;
            m_OutputScroll.style.borderTopColor = CodexPalette.Border;
            m_OutputScroll.style.borderBottomColor = CodexPalette.Border;
            m_OutputScroll.style.borderLeftColor = CodexPalette.Border;
            m_OutputScroll.style.borderRightColor = CodexPalette.Border;
            m_OutputScroll.style.borderTopLeftRadius = 8;
            m_OutputScroll.style.borderTopRightRadius = 8;
            m_OutputScroll.style.borderBottomLeftRadius = 8;
            m_OutputScroll.style.borderBottomRightRadius = 8;
            m_OutputScroll.style.paddingLeft = 8;
            m_OutputScroll.style.paddingRight = 8;
            m_OutputScroll.style.paddingTop = 6;
            m_OutputScroll.style.paddingBottom = 6;
            m_OutputScroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            m_OutputScroll.verticalScrollerVisibility = ScrollerVisibility.Auto;
            m_OutputScroll.verticalScroller.valueChanged += _ =>
            {
                if (!m_ProgrammaticScroll)
                {
                    m_FollowOutput = IsOutputAtBottom();
                }
            };
            rootVisualElement.Add(m_OutputScroll);

            m_Output = new Label("No output yet.");
            m_Output.style.color = CodexPalette.TextDim;
            m_Output.style.fontSize = 10;
            m_Output.style.whiteSpace = WhiteSpace.Normal;
            m_Output.style.width = Length.Percent(100);
            m_OutputScroll.Add(m_Output);
            m_OutputScroll.contentContainer.RegisterCallback<GeometryChangedEvent>(_ =>
            {
                if (m_FollowOutput)
                {
                    ScrollOutputToBottom();
                }
            });

            Refresh();
        }

        static void StyleButton(Button button, bool primary)
        {
            button.style.height = 24;
            button.style.minWidth = 54;
            button.style.paddingLeft = 9;
            button.style.paddingRight = 9;
            button.style.borderTopWidth = 1;
            button.style.borderBottomWidth = 1;
            button.style.borderLeftWidth = 1;
            button.style.borderRightWidth = 1;
            button.style.borderTopColor = primary ? Color.clear : CodexPalette.PillBorder;
            button.style.borderBottomColor = primary ? Color.clear : CodexPalette.PillBorder;
            button.style.borderLeftColor = primary ? Color.clear : CodexPalette.PillBorder;
            button.style.borderRightColor = primary ? Color.clear : CodexPalette.PillBorder;
            button.style.borderTopLeftRadius = 7;
            button.style.borderTopRightRadius = 7;
            button.style.borderBottomLeftRadius = 7;
            button.style.borderBottomRightRadius = 7;
            button.style.backgroundColor = primary ? CodexPalette.SendAction : CodexPalette.Pill;
            button.style.color = primary ? Color.white : CodexPalette.Text;
            button.style.fontSize = 10;
        }

        void RunOrStop()
        {
            if (m_Workspace == null)
            {
                return;
            }

            if (m_Workspace.ConsoleRunning)
            {
                m_Workspace.StopConsole();
                Refresh();
                return;
            }

            bool local = m_Mode.index == 1;
            if (!m_Workspace.StartConsoleCommand(m_Command.value, m_Cwd.value, local))
            {
                Refresh();
                return;
            }

            m_FollowOutput = true;
            Refresh();
        }

        void Clear()
        {
            m_Workspace?.ClearConsole();
            m_FollowOutput = true;
            Refresh();
        }

        void CopyOutput()
        {
            if (m_Workspace != null)
            {
                EditorGUIUtility.systemCopyBuffer = m_Workspace.ConsoleOutput ?? string.Empty;
            }
        }

        void OnConsoleChanged()
        {
            if (m_RefreshScheduled)
            {
                return;
            }

            m_RefreshScheduled = true;
            rootVisualElement.schedule.Execute(() =>
            {
                m_RefreshScheduled = false;
                Refresh();
            }).ExecuteLater(40);
        }

        bool IsOutputAtBottom()
        {
            Scroller scroller = m_OutputScroll.verticalScroller;
            return scroller.highValue <= 0 || scroller.value >= scroller.highValue - 24;
        }

        void Refresh()
        {
            if (m_Command == null)
            {
                return;
            }

            bool hasWorkspace = m_Workspace != null;
            bool connected = hasWorkspace && m_Workspace.IsConnected;
            m_LastConnected = connected;

            if (!hasWorkspace)
            {
                m_Command.SetEnabled(false);
                m_Cwd.SetEnabled(false);
                m_Mode.SetEnabled(false);
                m_RunButton.text = "Run";
                m_RunButton.style.backgroundColor = CodexPalette.SendAction;
                m_RunButton.SetEnabled(false);
                m_ClearButton.SetEnabled(false);
                m_Status.text = "Open the Codex Assistant first.";
                m_Status.style.color = CodexPalette.Warning;
                return;
            }

            bool running = m_Workspace.ConsoleRunning;
            m_Command.SetEnabled(connected && !running);
            m_Cwd.SetEnabled(connected && !running);
            m_Mode.SetEnabled(connected && !running);
            m_RunButton.text = running ? "Stop" : "Run";
            m_RunButton.tooltip = running ? "Stop the running command" : "Run the command";
            m_RunButton.style.backgroundColor = running
                ? CodexPalette.StopAction
                : CodexPalette.SendAction;
            m_RunButton.SetEnabled(connected);
            m_ClearButton.SetEnabled(m_Workspace.ConsoleOutput.Length > 0 || running);

            if (!connected)
            {
                m_Status.text = "Connecting to app-server...";
                m_Status.style.color = CodexPalette.Warning;
            }
            else if (running)
            {
                m_Status.text = m_Workspace.ConsoleLocal
                    ? "Running · local process (unsandboxed)"
                    : "Running · sandboxed command";
                m_Status.style.color = CodexPalette.Accent;
            }
            else if (!string.IsNullOrEmpty(m_Workspace.ConsoleError))
            {
                m_Status.text = m_Workspace.ConsoleError;
                if (m_Workspace.ConsoleError.IndexOf("program not found", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    m_Status.text += Application.platform == RuntimePlatform.WindowsEditor
                        ? "\nUse an executable name, or run shell commands with powershell.exe -NoProfile -Command \"…\"."
                        : "\nUse an executable path, or run shell commands with /bin/sh -c \"…\".";
                }
                m_Status.style.color = CodexPalette.Error;
            }
            else if (m_Workspace.ConsoleExitCode.HasValue)
            {
                m_Status.text = $"Exited with code {m_Workspace.ConsoleExitCode.Value}";
                m_Status.style.color = m_Workspace.ConsoleExitCode.Value == 0
                    ? CodexPalette.Muted
                    : CodexPalette.Error;
            }
            else
            {
                m_Status.text = "Ready";
                m_Status.style.color = CodexPalette.Muted;
            }

            string output = m_Workspace.ConsoleOutput;
            bool stick = m_FollowOutput || IsOutputAtBottom();
            m_Output.text = string.IsNullOrEmpty(output) ? "No output yet." : output;
            m_Output.style.color = string.IsNullOrEmpty(output)
                ? CodexPalette.TextDim
                : CodexPalette.Text;
            if (stick)
            {
                m_FollowOutput = true;
                m_OutputScroll.schedule.Execute(ScrollOutputToBottom);
            }
        }

        void ScrollOutputToBottom()
        {
            float maxOffset = Math.Max(
                m_OutputScroll.verticalScroller.highValue,
                m_OutputScroll.contentContainer.layout.height - m_OutputScroll.layout.height);
            m_ProgrammaticScroll = true;
            try
            {
                m_OutputScroll.scrollOffset = new Vector2(0, Math.Max(0, maxOffset));
            }
            finally
            {
                m_ProgrammaticScroll = false;
            }
        }
    }
}
