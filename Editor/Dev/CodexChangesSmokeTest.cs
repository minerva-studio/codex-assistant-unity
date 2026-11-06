using System;
using System.Collections.Generic;
using System.Reflection;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine.UIElements;

namespace Minerva.Codex.Editor.Dev
{
    /// <summary>Checks continuous Changes reading without sending a Codex message.</summary>
    internal static class CodexChangesSmokeTest
    {
        const BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic;

        [MenuItem("Tools/Minerva/Codex/Check Changes Panel")]
        public static string Run()
        {
            List<CodexAssistantWindow.PopupItem> menu = null;
            var panel = new CodexChangesPanel((_, _, items) => menu = items);
            var host = UnityEngine.ScriptableObject.CreateInstance<EditorWindow>();
            host.titleContent = new UnityEngine.GUIContent("Changes smoke fixture");
            host.rootVisualElement.Add(panel);
            host.ShowUtility();
            try
            {
                var workspace = new CodexWorkspace();
                SelectThread(workspace, "smoke-thread");
                var turns = (List<JObject>)typeof(CodexWorkspace).GetField("m_Turns", Fields).GetValue(workspace);
                Action refresh = () => panel.Refresh(workspace);
                refresh();
                Require(panel.Q<Label>("codex-changes-empty").text.Contains("recorded"), "empty state describes recorded data");
                JObject current = Turn("current", "completed", "first patch", "second patch\r\n");
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
                var sections = panel.Query<VisualElement>("codex-change-record").ToList();
                Require(sections.Count == 2 && panel.Query<CodexPatchView>().ToList().Count == 2,
                    "all file records in the current turn are readable in one continuous view");
                Require(sections.TrueForAll(section => section.Q("codex-change-body").style.display.value == DisplayStyle.Flex),
                    "per-file patches start expanded");
                var scope = panel.Q<Button>("codex-change-scope");
                Require(scope.text.Contains("Latest"), "default scope follows latest turn");
                Require(panel.Q<Label>("codex-changes-summary").text.Contains("Fix the roof"), "summary strips attached Unity context");
                Require(sections[0].Q<Label>("codex-change-filename").text.EndsWith("1/2")
                    && sections[1].Q<Label>("codex-change-filename").text.EndsWith("2/2"), "repeated edits keep distinct order in file titles");
                Submit(sections[0].Q<Button>("codex-change-toggle"));
                Require(sections[0].Q("codex-change-body").style.display.value == DisplayStyle.None
                    && sections[1].Q("codex-change-body").style.display.value == DisplayStyle.Flex, "files fold independently");
                ((JObject)((JArray)((JObject)((JArray)current["items"])[0])["changes"])[0])["diff"] = "streamed patch";
                refresh();
                Require(ReferenceEquals(sections[0], panel.Q("codex-change-record"))
                    && sections[0].Q("codex-change-body").style.display.value == DisplayStyle.None,
                    "background refresh retains section identity and folding");
                Require(sections[0].Q<CodexPatchView>().Patch == "streamed patch", "collapsed patches still update");
                Require(sections[1].Q<Button>("codex-change-copy").enabledSelf
                    && sections[1].Q<CodexPatchView>().Patch == "second patch\r\n",
                    "per-file copy source retains exact raw line endings");
                Submit(panel.Q<Button>("codex-changes-files"));
                Require(menu.Count == 2 && menu[0].Label != menu[1].Label, "optional file jumps preserve repeated records");
                Action staleFileJump = menu[0].Action;
                menu[0].Action();
                Require(sections[0].Q("codex-change-body").style.display.value == DisplayStyle.Flex, "file jump expands its target");
                Submit(panel.Q<Button>("codex-changes-collapse"));
                Require(sections.TrueForAll(section => section.Q("codex-change-body").style.display.value == DisplayStyle.None), "collapse all folds every file");
                Submit(panel.Q<Button>("codex-changes-collapse"));
                Require(sections.TrueForAll(section => section.Q("codex-change-body").style.display.value == DisplayStyle.Flex), "expand all restores every file");
                Submit(scope);
                Require(menu.Count == 3 && menu[0].Selected && menu[1].Label.Contains("Fix the roof")
                    && !menu[1].Label.Contains("unity-context"), "scope menu has latest plus searchable loaded turns");
                menu[2].Action();
                Require(panel.Q<CodexPatchView>().Patch == "older patch"
                    && panel.Q<Label>("codex-change-status").tooltip.Contains("failed")
                    && panel.Q<Label>("codex-change-status").style.display.value == DisplayStyle.Flex,
                    "historical scope displays its patches and inline failure indicator");
                staleFileJump();
                Require(panel.Q<CodexPatchView>().Patch == "older patch", "a stale file jump cannot change historical scope");
                turns.Insert(0, Turn("newest", "completed", "newest patch"));
                refresh();
                Require(panel.Q<CodexPatchView>().Patch == "older patch", "historical scope stays pinned when a new turn arrives");
                Submit(scope);
                menu[0].Action();
                Require(panel.Q<CodexPatchView>().Patch == "newest patch", "latest scope follows newly recorded turns");
                turns.RemoveAt(0);
                refresh();

                var live = (JObject)current.DeepClone();
                ((JArray)live["items"]).Add(new JObject { ["id"] = "live-turn-diff", ["type"] = "turnDiff", ["diff"] = "+aggregate live patch" });
                typeof(CodexWorkspace).GetField("m_LiveTurn", Fields).SetValue(workspace, live);
                typeof(CodexWorkspace).GetField("m_LiveThreadId", Fields).SetValue(workspace, "smoke-thread");
                typeof(CodexWorkspace).GetProperty("IsRunning").SetValue(workspace, true);
                refresh();
                sections = panel.Query<VisualElement>("codex-change-record").ToList();
                Require(sections.Count == 3, "live turn deduplicates history and retains a separate aggregate");
                Require(sections[2].Q("codex-change-body").style.display.value == DisplayStyle.None
                    && !sections[2].Q<Button>("codex-change-open").enabledSelf, "aggregate starts folded and has no file target");
                Require(panel.Q<Label>("codex-changes-added").text == "+0", "aggregate is excluded from per-file totals");
                typeof(CodexWorkspace).GetProperty("IsRunning").SetValue(workspace, false);
                live["status"] = "completed";
                refresh();
                Require(panel.Query<CodexPatchView>().ToList().Count == 2, "completion removes live aggregate before history reload");
                typeof(CodexWorkspace).GetField("m_LiveTurn", Fields).SetValue(workspace, null);
                refresh();

                turns.Insert(0, Turn("missing", "declined", ""));
                SelectThread(workspace, "another-thread");
                refresh();
                Require(panel.Q<Label>("codex-change-status").tooltip.Contains("declined"), "declined patch retains status in its inline indicator");
                Require(panel.Q<Label>("codex-change-no-patch").style.display.value == DisplayStyle.Flex
                    && panel.Q<CodexPatchView>().Patch == "", "missing patch explains absence without inventing copy content");
                turns.Clear();
                refresh();
                Require(panel.Query<CodexPatchView>().ToList().Count == 0, "empty transition clears all old file sections");
                typeof(CodexWorkspace).GetField("m_HasMoreTurns", Fields).SetValue(workspace, true);
                refresh();
                Require(panel.Q<Button>("codex-changes-earlier").style.display.value == DisplayStyle.Flex, "earlier history remains accessible from an empty panel");
                for (int index = 0; index < 200; index++) turns.Add(Turn("turn-" + index, "completed", "patch-" + index));
                SelectThread(workspace, "long-history");
                refresh();
                int buttonCount = panel.Query<Button>().ToList().Count;
                Require(buttonCount <= 8 && panel.Query<CodexPatchView>().ToList().Count == 1, "200 turns render only the current scope");
                Submit(scope);
                Require(menu.Count == 201 && menu[0].Selected, "loaded historical turns remain searchable alongside latest scope");
                menu[200].Action();
                Require(panel.Q<CodexPatchView>().Patch == "patch-199", "scope jumps directly to the last of 200 turns");
                Require(panel.Query<Button>().ToList().Count == buttonCount, "history does not enlarge permanent navigation");

                var wholeFile = Turn("whole-file", "completed", "class Example {}\n\n");
                var change = (JObject)((JArray)((JObject)((JArray)wholeFile["items"])[0])["changes"])[0];
                change["kind"] = new JObject { ["type"] = "add" };
                turns.Clear();
                turns.Add(wholeFile);
                SelectThread(workspace, "whole-file");
                refresh();
                var patch = panel.Q<CodexPatchView>();
                Require(patch.childCount == 2 && patch[0].ClassListContains("codex-patch-addition")
                    && patch[1].ClassListContains("codex-patch-addition") && panel.Q<Label>("codex-changes-added").text == "+2",
                    "whole-file additions include blank content lines in rendering and totals");
                change["kind"] = new JObject { ["type"] = "delete" };
                refresh();
                Require(patch[0].ClassListContains("codex-patch-deletion") && patch.Patch == "class Example {}\n\n"
                    && panel.Q<Label>("codex-changes-removed").text == "−2", "whole-file deletions retain raw text and update totals");

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
                    Require(open.enabledSelf && open.tooltip == original, "relative file path resolves against conversation cwd");
                    change["path"] = original;
                    refresh();
                    Require(open.enabledSelf && open.tooltip == original, "absolute file path stays absolute");
                    change["kind"] = new JObject { ["type"] = "update", ["move_path"] = "Renamed.cs" };
                    refresh();
                    Require(open.enabledSelf && open.tooltip == renamed, "renamed record opens its destination");
                    System.IO.File.Delete(renamed);
                    Submit(open);
                    Require(!open.enabledSelf, "file disappearance disables Open without launching an application");
                    refresh();
                    Require(!open.enabledSelf, "missing files remain unavailable");
                    Require(CodexMarkdown.ResolveLocalPath(new Uri(original).AbsoluteUri, directory) == original, "shared resolver accepts file URIs");
                    Require(CodexMarkdown.ResolveLocalPath("bad\0path", directory) == null, "malformed paths cannot break the panel");
                }
                finally
                {
                    System.IO.File.Delete(original);
                    System.IO.File.Delete(renamed);
                    System.IO.Directory.Delete(directory);
                }
                return "PASS: continuous multi-file reading, independent/persistent folding, latest/history scopes, repeated edits, file jumps, raw copy source, totals, live aggregate and file opening guards";
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
                changes.Add(new JObject
                {
                    ["path"] = "Assets/Example.cs",
                    ["kind"] = new JObject { ["type"] = "update", ["move_path"] = JValue.CreateNull() },
                    ["diff"] = patch,
                });
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
