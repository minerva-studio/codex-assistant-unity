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
            Type panelType = typeof(CodexChangesPanel);
            List<CodexAssistantWindow.PopupItem> menu = null;
            var panel = new CodexChangesPanel((_, items) => menu = items);
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
                var currentHeader = panel.Q<Button>("codex-change-turn-selector");
                Require(currentHeader.Query<Label>().ToList().Exists(label => label.text == "Fix the roof"), "turn navigation uses user text without attached Unity context");
                Require(!panel.Q<Button>("codex-changes-newer").enabledSelf && panel.Q<Button>("codex-changes-older").enabledSelf,
                    "newest turn disables newer navigation and enables older");
                var rows = panel.Query<Button>("codex-change-record").ToList();
                Require(rows[0].Query<Label>().ToList().Exists(label => label.text == "Example.cs"), "file basename is the primary navigation label");
                Require(rows[0].Query<Label>().ToList().Exists(label => label.text == "Assets · edit 1")
                    && rows[1].Query<Label>().ToList().Exists(label => label.text == "Assets · edit 2"), "repeated paths show distinct edit order");
                Require(panel.Q<CodexPatchView>("codex-change-diff").Patch == "first patch", "newest turn selected initially");
                Submit(panel.Q<Button>("codex-changes-older"));
                Require(panel.Q<CodexPatchView>("codex-change-diff").Patch == "older patch", "historical turn can be selected");
                Require(panel.Query<Button>("codex-change-record").ToList().Count == 1, "only the selected turn has file rows");
                Require(!panel.Q<Button>("codex-changes-older").enabledSelf && panel.Q<Button>("codex-changes-newer").enabledSelf,
                    "oldest turn disables older navigation and enables newer");
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
                Submit(currentHeader);
                Require(menu.Count == 2, "live turn deduplicates history turn with same id in the picker");
                menu[0].Action();
                Require(panel.Query<Button>("codex-change-record").ToList().Count == 3, "aggregate live patch has a separate entry");
                panelType.GetMethod("SelectRecord", Fields).Invoke(panel, new object[] { "live-turn-diff" });
                Require(panel.Q<CodexPatchView>("codex-change-diff").Patch == "aggregate live patch", "live patch can be inspected");
                Require(!panel.Q<Button>("codex-change-open").enabledSelf, "aggregate diff has no single file to open");
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

                for (int index = 0; index < 200; index++) turns.Add(Turn("turn-" + index, "completed", "patch-" + index));
                SelectThread(workspace, "long-history");
                refresh();
                int buttonCount = panel.Query<Button>().ToList().Count;
                Require(buttonCount <= 7 && panel.Query<Button>("codex-change-record").ToList().Count == 1,
                    "200 turns do not create a growing permanent menu");
                Submit(currentHeader);
                Require(menu.Count == 200 && menu[0].Selected, "all recorded turns remain available in the search popup");
                menu[199].Action();
                Require(panel.Q<CodexPatchView>("codex-change-diff").Patch == "patch-199", "picker can jump directly to the last of 200 turns");
                Submit(panel.Q<Button>("codex-changes-newer"));
                Require(panel.Q<CodexPatchView>("codex-change-diff").Patch == "patch-198", "adjacent navigation follows picker selection");
                Require(panel.Query<Button>().ToList().Count == buttonCount, "permanent navigation size remains constant across turn selection");

                var wholeFile = Turn("whole-file", "completed", "class Example {}\n\n");
                var change = (JObject)((JArray)((JObject)((JArray)wholeFile["items"])[0])["changes"])[0];
                change["kind"] = new JObject { ["type"] = "add" };
                turns.Clear();
                turns.Add(wholeFile);
                SelectThread(workspace, "whole-file");
                refresh();
                var patch = panel.Q<CodexPatchView>("codex-change-diff");
                Require(patch.childCount == 2 && patch[0].ClassListContains("codex-patch-addition")
                    && patch[1].ClassListContains("codex-patch-addition"), "Changes treats the whole new file, including blank lines, as added");
                change["kind"] = new JObject { ["type"] = "delete" };
                refresh();
                Require(patch[0].ClassListContains("codex-patch-deletion") && patch.Patch == "class Example {}\n\n",
                    "Changes treats whole deleted files as removed while preserving raw copy text");

                string directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "codex-open-file-" + Guid.NewGuid().ToString("N"));
                System.IO.Directory.CreateDirectory(directory);
                string original = System.IO.Path.Combine(directory, "Original.cs");
                string renamed = System.IO.Path.Combine(directory, "Renamed.cs");
                try
                {
                    System.IO.File.WriteAllText(original, "// fixture");
                    System.IO.File.WriteAllText(renamed, "// fixture");
                    typeof(CodexWorkspace).GetProperty("SelectedCwd").SetValue(workspace, directory);
                    change["path"] = "Original.cs";
                    change["kind"] = new JObject { ["type"] = "update" };
                    refresh();
                    var open = panel.Q<Button>("codex-change-open");
                    Require(open.enabledSelf && open.tooltip == original, "relative file path resolves against the conversation cwd");
                    change["path"] = original;
                    refresh();
                    Require(open.enabledSelf && open.tooltip == original, "absolute file path stays absolute");
                    change["kind"] = new JObject { ["type"] = "update", ["move_path"] = "Renamed.cs" };
                    refresh();
                    Require(open.enabledSelf && open.tooltip == renamed, "renamed record opens the destination rather than its display text or source");
                    System.IO.File.Delete(renamed);
                    var fileRow = panel.Q<Button>("codex-change-record");
                    using (var evt = PointerDownEvent.GetPooled(new UnityEngine.Event
                    {
                        type = UnityEngine.EventType.MouseDown, button = 0, clickCount = 2,
                    }))
                    {
                        evt.target = fileRow;
                        fileRow.SendEvent(evt);
                    }
                    Require(!open.enabledSelf, "a file removed after render disables Open without launching an application");
                    refresh();
                    Require(!open.enabledSelf, "missing files remain unavailable");
                    Require(CodexMarkdown.ResolveLocalPath(new Uri(original).AbsoluteUri, directory) == original,
                        "the shared resolver accepts a file URI");
                    Require(CodexMarkdown.ResolveLocalPath("bad\0path", directory) == null, "malformed local paths cannot break the panel");
                }
                finally
                {
                    System.IO.File.Delete(original);
                    System.IO.File.Delete(renamed);
                    System.IO.Directory.Delete(directory);
                }
                return "PASS: navigation/history, raw patches, Open file availability, absolute/relative/renamed paths, file disappearance, aggregate guard and shared URI resolution";
            }
            finally
            {
                host.Close();
            }
        }

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
