using System;
using System.Collections.Generic;
using System.Reflection;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine.UIElements;

namespace Minerva.Codex.Editor.Dev
{
    /// <summary>Checks Changes selection and rendering without sending a Codex message.</summary>
    internal static class CodexChangesSmokeTest
    {
        const BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic;

        [MenuItem("Tools/Minerva/Codex/Check Changes Panel")]
        public static string Run()
        {
            Type panelType = typeof(CodexWorkspace).Assembly.GetType("Minerva.Codex.CodexChangesPanel", true);
            var panel = (VisualElement)Activator.CreateInstance(panelType, true);
            var host = UnityEngine.ScriptableObject.CreateInstance<EditorWindow>();
            host.titleContent = new UnityEngine.GUIContent("Changes smoke fixture");
            host.rootVisualElement.Add(panel);
            host.ShowUtility();
            try
            {
                var workspace = new CodexWorkspace();
                SelectThread(workspace, "smoke-thread");
                var turns = (List<JObject>)typeof(CodexWorkspace).GetField("m_Turns", Fields).GetValue(workspace);
                Action refresh = () => panelType.GetMethod("Refresh").Invoke(panel, new object[] { workspace });
                refresh();
                Require(panel.Q<Label>("codex-changes-empty").text.Contains("recorded"), "empty state describes recorded data");

                JObject current = Turn("current", "completed", "first patch", "second patch");
                JObject older = Turn("older", "failed", "older patch");
                ((JArray)current["items"]).Add(new JObject
                {
                    ["type"] = "userMessage", ["content"] = new JArray(new JObject
                    {
                        ["type"] = "text", ["text"] = "<unity-context>\nEditor snapshot\n</unity-context>\nFix the roof",
                    }),
                });
                turns.Add(current);
                turns.Add(older);
                refresh();
                Require(panel.Query<Button>("codex-change-record").ToList().Count == 2, "repeated file paths remain separate records");
                var currentHeader = TurnButton(panel, "current");
                Require(currentHeader.Query<Label>().ToList().Exists(label => label.text == "Fix the roof"), "turn navigation uses user text without attached Unity context");
                var rows = panel.Query<Button>("codex-change-record").ToList();
                Require(rows[0].Query<Label>().ToList().Exists(label => label.text == "Example.cs"), "file basename is the primary navigation label");
                Require(rows[0].Query<Label>().ToList().Exists(label => label.text == "Assets · edit 1")
                    && rows[1].Query<Label>().ToList().Exists(label => label.text == "Assets · edit 2"), "repeated paths show distinct edit order");
                Require(panel.Q<CodexPatchView>("codex-change-diff").Patch == "first patch", "newest turn selected initially");
                Submit(TurnButton(panel, "current"));
                Require(panel.Query<Button>("codex-change-record").ToList().Count == 0
                    && panel.Q<CodexPatchView>("codex-change-diff").Patch == "first patch", "collapsing the selected group keeps its patch open");
                Submit(TurnButton(panel, "current"));

                Submit(TurnButton(panel, "older"));
                Require(panel.Q<CodexPatchView>("codex-change-diff").Patch == "older patch", "historical turn can be selected");
                Require(panel.Query<Button>("codex-change-record").ToList().Count == 1, "only the selected turn expands its file rows");
                Require(panel.Q<Label>("codex-change-status").text.Contains("failed"), "failed patch is labelled");
                ((JObject)((JArray)((JObject)((JArray)current["items"])[0])["changes"])[0])["diff"] = "streamed patch";
                refresh();
                Require(panel.Q<CodexPatchView>("codex-change-diff").Patch == "older patch", "background updates preserve explicit selection");

                var live = (JObject)current.DeepClone();
                ((JArray)live["items"]).Add(new JObject { ["id"] = "live-turn-diff", ["type"] = "turnDiff", ["diff"] = "aggregate live patch" });
                typeof(CodexWorkspace).GetField("m_LiveTurn", Fields).SetValue(workspace, live);
                typeof(CodexWorkspace).GetField("m_LiveThreadId", Fields).SetValue(workspace, "smoke-thread");
                typeof(CodexWorkspace).GetProperty("IsRunning").SetValue(workspace, true);
                refresh();
                Require(panel.Query<Button>("codex-change-turn").ToList().Count == 2, "live turn deduplicates history turn with same id");
                Submit(TurnButton(panel, "current"));
                Require(panel.Query<Button>("codex-change-record").ToList().Count == 3, "aggregate live patch has a separate entry");
                panelType.GetMethod("SelectRecord", Fields).Invoke(panel, new object[] { "live-turn-diff" });
                Require(panel.Q<CodexPatchView>("codex-change-diff").Patch == "aggregate live patch", "live patch can be inspected");
                typeof(CodexWorkspace).GetProperty("IsRunning").SetValue(workspace, false);
                live["status"] = "completed";
                refresh();
                Require(panel.Q<CodexPatchView>("codex-change-diff").Patch == "streamed patch", "completion hides live aggregate before history reload succeeds");
                typeof(CodexWorkspace).GetField("m_LiveTurn", Fields).SetValue(workspace, null);
                refresh();
                Require(panel.Q<CodexPatchView>("codex-change-diff").Patch == "streamed patch", "completion removes stale live patch");

                JObject missing = Turn("missing", "declined", "");
                turns.Insert(0, missing);
                SelectThread(workspace, "another-thread");
                refresh();
                Require(panel.Q<Label>("codex-change-status").text.Contains("declined"), "declined patch retains status");
                Require(panel.Q<Label>("codex-change-no-patch").style.display.value == DisplayStyle.Flex, "missing patch has an explicit explanation");
                Require(panel.Q<CodexPatchView>("codex-change-diff").Patch == "", "missing patch explanation does not become patch content");
                turns.Clear();
                refresh();
                Require(panel.Q<CodexPatchView>("codex-change-diff").Patch == "", "empty/thread transition clears old diff");
                typeof(CodexWorkspace).GetField("m_HasMoreTurns", Fields).SetValue(workspace, true);
                refresh();
                Require(panel.Q<Button>("codex-changes-earlier").style.display.value == DisplayStyle.Flex, "earlier history remains accessible from an empty panel");
                return "PASS: readable turn summaries, context stripping, filename/directory hierarchy, repeated edit order, accordion navigation, selection retention, live deduplication/completion, missing patch and pagination";
            }
            finally
            {
                host.Close();
            }
        }

        static Button TurnButton(VisualElement panel, string id) =>
            panel.Query<Button>("codex-change-turn").ToList().Find(button => (string)button.userData == id);

        static void Submit(Button button)
        {
            using var evt = NavigationSubmitEvent.GetPooled();
            evt.target = button;
            button.SendEvent(evt);
        }

        static void SelectThread(CodexWorkspace workspace, string id)
        {
            typeof(CodexWorkspace).GetProperty("SelectedThreadId").SetValue(workspace, id);
        }

        static JObject Turn(string id, string status, params string[] patches)
        {
            var changes = new JArray();
            foreach (string patch in patches)
            {
                changes.Add(new JObject
                {
                    ["path"] = "Assets/Example.cs",
                    ["kind"] = new JObject { ["type"] = "update", ["move_path"] = JValue.CreateNull() },
                    ["diff"] = patch,
                });
            }

            return new JObject
            {
                ["id"] = id,
                ["items"] = new JArray(new JObject
                {
                    ["id"] = id + "-files", ["type"] = "fileChange", ["status"] = status, ["changes"] = changes,
                }),
            };
        }

        static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException("Changes smoke: " + message);
        }
    }
}
