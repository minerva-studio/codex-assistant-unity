using System;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Minerva.Codex.Editor.Dev
{
    /// <summary>Checks transcript input ownership and shared programmatic caret reset without executing commands.</summary>
    internal static class CodexTerminalSmokeTest
    {
        const BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic;
        static CodexTerminalSession s_Session;
        static CodexConnection s_Connection;
        static CodexClient s_Client;
        static CodexConsoleWindow s_Window;

        /// <summary>Opens an isolated terminal fixture on the existing connection without replacing the user's shell.</summary>
        public static string StartProtocol()
        {
            StopProtocol();
            var assistant = Resources.FindObjectsOfTypeAll<CodexAssistantWindow>().First();
            var workspace = assistant.Workspace;
            s_Connection = (CodexConnection)typeof(CodexWorkspace).GetField("m_Connection", Fields).GetValue(workspace);
            s_Client = (CodexClient)typeof(CodexWorkspace).GetField("m_Client", Fields).GetValue(workspace);
            s_Session = new CodexTerminalSession(s_Client);
            s_Connection.Notification += OnProtocolNotification;
            s_Session.Start(System.IO.Path.GetDirectoryName(Application.dataPath), false);
            var fixture = new CodexWorkspace();
            typeof(CodexWorkspace).GetField("m_Ready", Fields).SetValue(fixture, true);
            typeof(CodexWorkspace).GetField("m_Terminal", Fields).SetValue(fixture, s_Session);
            s_Window = ScriptableObject.CreateInstance<CodexConsoleWindow>();
            typeof(CodexConsoleWindow).GetMethod("Bind", Fields).Invoke(s_Window, new object[] { fixture });
            s_Window.ShowUtility();
            s_Window.titleContent = new GUIContent("Terminal test session");
            return "Isolated PTY fixture started";
        }

        static void OnProtocolNotification(JObject message)
        {
            string method = message["method"]?.Value<string>();
            if (message["params"] is not JObject parameters || s_Session == null) return;
            if (method == "command/exec/outputDelta" || method == "process/outputDelta") s_Session.HandleOutput(parameters);
            else if (method == "process/exited") s_Session.HandleExit(parameters);
        }

        /// <summary>Returns only this fixture's shell state and rendered output.</summary>
        public static JObject ProtocolStatus() => new JObject
        {
            ["state"] = s_Session?.State.ToString(), ["directory"] = s_Session?.Directory,
            ["output"] = s_Session?.Output, ["error"] = s_Session?.Error,
            ["success"] = s_Session?.LastCommandSucceeded, ["local"] = s_Session?.Local,
        };

        /// <summary>Prepares a real input field; Enter is dispatched separately after focus has settled.</summary>
        public static void Draft(string command)
        {
            var input = s_Window.rootVisualElement.Q<TextField>("codex-terminal-command");
            input.SetValueWithoutNotify(command);
            CodexTextEditing.CaretAtEnd(input);
            s_Window.Focus();
            input.Focus();
        }

        /// <summary>Dispatches a keyboard event through the attached terminal input field.</summary>
        public static void Key(KeyCode key, bool control)
        {
            var input = s_Window.rootVisualElement.Q<TextField>("codex-terminal-command");
            using var evt = KeyDownEvent.GetPooled(new Event
            {
                type = EventType.KeyDown, keyCode = key,
                modifiers = control ? EventModifiers.Control : EventModifiers.None,
            });
            evt.target = input;
            input.SendEvent(evt);
        }

        /// <summary>Submits directly to the fixture PTY for protocol checks.</summary>
        public static bool Send(string command) => s_Session.Send(command);

        /// <summary>Restarts the fixture in the requested execution mode.</summary>
        public static void Mode(bool local) => s_Session.Start(s_Session.Directory, local);

        /// <summary>Terminates only the fixture process and removes its temporary subscriptions/window.</summary>
        public static void StopProtocol()
        {
            if (s_Session != null)
            {
                string id = (string)typeof(CodexTerminalSession).GetField("m_Process", Fields).GetValue(s_Session);
                if (id != null)
                {
                    if (s_Session.Local) s_Client.KillProcess(id, null, null);
                    else s_Client.TerminateCommand(id, null, null);
                }
                s_Session.Disconnect("Test complete");
            }
            if (s_Connection != null) s_Connection.Notification -= OnProtocolNotification;
            if (s_Window != null) s_Window.Close();
            s_Session = null;
            s_Connection = null;
            s_Client = null;
            s_Window = null;
        }

        public static string Run()
        {
            var parent = new VisualElement();
            var command = new JObject
            {
                ["type"] = "commandExecution", ["id"] = "test-tool", ["processId"] = "tool-42",
                ["status"] = "inProgress", ["command"] = "sleep 5", ["aggregatedOutput"] = "sample output",
                ["terminalInput"] = "observed input",
            };
            CodexItemRenderer.Render(parent, command, true, true);
            Require(parent.Q<TextField>("codex-terminal-input") == null, "conversation process exposes standalone stdin");
            Require(parent.Query<Label>().ToList().Any(label => label.text == "sample output"), "process output remains visible");
            Require(parent.Query<Foldout>().ToList().Any(foldout => foldout.text == "Terminal input"), "observed stdin history remains visible");

            var host = ScriptableObject.CreateInstance<EditorWindow>();
            try
            {
                var field = new TextField { value = "previous command" };
                host.rootVisualElement.Add(field);
                host.ShowUtility();
                field.SetValueWithoutNotify("");
                CodexTextEditing.CaretAtEnd(field);
                Require(field.cursorIndex == 0 && field.selectIndex == 0, "cleared input retains stale selection");
            }
            finally { host.Close(); }
            return "PASS: conversation process is read-only, observed output/input remain visible, cleared field caret resets";
        }

        static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException("Terminal smoke: " + message);
        }
    }
}
