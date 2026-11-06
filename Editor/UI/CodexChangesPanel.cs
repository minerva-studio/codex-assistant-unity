using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace Minerva.Codex
{
    /// <summary>
    /// Read-only view of recorded file edits. Workspace turns own the data;
    /// this element retains only selection and reading position between refreshes.
    /// </summary>
    internal sealed class CodexChangesPanel : VisualElement
    {
        sealed class ChangeTurn
        {
            public string Id;
            public string Title;
            public string Time;
            public bool Live;
            public readonly List<ChangeRecord> Records = new List<ChangeRecord>();
        }

        sealed class ChangeRecord
        {
            public string Key;
            public string Path;
            public string PreviousPath;
            public string DisplayPath => PreviousPath == null ? Path : PreviousPath + " → " + Path;
            public string FileName => System.IO.Path.GetFileName(Path.Replace('\\', '/'));
            public string Operation;
            public string Status;
            public string Diff;
            public bool Live;
        }

        readonly Action<VisualElement, List<CodexAssistantWindow.PopupItem>> m_ShowFileMenu;
        readonly Button m_FileButton;
        readonly Label m_FileTitle;
        readonly Label m_FileInfo;
        readonly Button m_Newer;
        readonly Button m_Older;
        readonly Label m_Empty;
        readonly Button m_Earlier;
        readonly Label m_Status;
        readonly CodexPatchView m_Diff;
        readonly Label m_NoPatch;
        readonly Button m_Copy;
        readonly Button m_Open;
        readonly ScrollView m_Patch;
        readonly List<ChangeTurn> m_Turns = new List<ChangeTurn>();
        CodexWorkspace m_Workspace;
        string m_ThreadId;
        string m_TurnId;
        string m_RecordKey;
        bool m_RestoreScroll;
        Vector2 m_PatchOffset;

        /// <param name="showFileMenu">Shows searchable file-change actions in the owning window's popup layer.</param>
        public CodexChangesPanel(Action<VisualElement, List<CodexAssistantWindow.PopupItem>> showFileMenu)
        {
            m_ShowFileMenu = showFileMenu;
            name = "codex-changes";
            style.flexGrow = 1;
            style.minHeight = 0;
            style.paddingLeft = 10;
            style.paddingRight = 10;
            style.paddingTop = 10;
            style.paddingBottom = 10;

            var navigation = new VisualElement { name = "codex-changes-file-navigation" };
            navigation.style.flexDirection = FlexDirection.Row;
            navigation.style.alignItems = Align.Center;
            navigation.style.flexShrink = 0;
            navigation.style.marginBottom = 6;
            m_FileButton = new Button(ShowFileMenu) { name = "codex-change-file-selector", tooltip = "Search files and changes" };
            m_FileButton.style.flexDirection = FlexDirection.Row;
            m_FileButton.style.alignItems = Align.Center;
            m_FileButton.style.flexGrow = 1;
            m_FileButton.style.flexShrink = 1;
            m_FileButton.style.minWidth = 0;
            m_FileButton.style.marginLeft = m_FileButton.style.marginRight = 0;
            m_FileButton.style.paddingTop = m_FileButton.style.paddingBottom = 6;
            m_FileButton.style.backgroundColor = CodexPalette.SurfaceRaised;
            m_FileButton.style.borderTopWidth = m_FileButton.style.borderBottomWidth = 0;
            m_FileButton.style.borderLeftWidth = m_FileButton.style.borderRightWidth = 0;
            var summary = new VisualElement();
            summary.style.flexGrow = 1;
            summary.style.minWidth = 0;
            m_FileTitle = Text("No recorded changes", 11, CodexPalette.Text);
            m_FileTitle.style.whiteSpace = WhiteSpace.NoWrap;
            m_FileTitle.style.overflow = Overflow.Hidden;
            m_FileTitle.style.textOverflow = TextOverflow.Ellipsis;
            m_FileTitle.style.unityFontStyleAndWeight = FontStyle.Bold;
            m_FileInfo = Text("", 9, CodexPalette.Muted);
            m_FileInfo.style.whiteSpace = WhiteSpace.NoWrap;
            m_FileInfo.style.overflow = Overflow.Hidden;
            m_FileInfo.style.textOverflow = TextOverflow.Ellipsis;
            summary.Add(m_FileTitle);
            summary.Add(m_FileInfo);
            m_FileButton.Add(summary);
            m_FileButton.Add(Text("▾", 11, CodexPalette.Muted));
            navigation.Add(m_FileButton);
            m_Newer = new Button(() => SelectAdjacent(-1))
                { name = "codex-changes-newer", text = "‹", tooltip = "Previous change" };
            m_Older = new Button(() => SelectAdjacent(1))
                { name = "codex-changes-older", text = "›", tooltip = "Next change" };
            foreach (Button button in new[] { m_Newer, m_Older })
            {
                button.style.width = 24;
                button.style.height = 30;
                button.style.flexShrink = 0;
                button.style.marginLeft = 3;
                button.style.marginRight = 0;
                button.style.fontSize = 16;
                navigation.Add(button);
            }
            Add(navigation);

            m_Empty = Text("No recorded file changes in the loaded turns.", 11, CodexPalette.Muted);
            m_Empty.name = "codex-changes-empty";
            m_Empty.style.marginTop = 8;
            m_Empty.style.marginBottom = 12;
            Add(m_Empty);
            m_Earlier = new Button(() => m_Workspace?.LoadEarlierTurns())
            {
                name = "codex-changes-earlier", text = "Load earlier turns",
            };
            m_Earlier.style.marginBottom = 6;

            Add(m_Earlier);

            var detailHeader = new VisualElement();
            detailHeader.style.flexDirection = FlexDirection.Row;
            detailHeader.style.alignItems = Align.Center;
            detailHeader.style.marginTop = 10;
            detailHeader.style.marginBottom = 3;
            m_Status = Text("", 9, CodexPalette.Muted);
            m_Status.name = "codex-change-status";
            m_Status.style.flexGrow = 1;
            m_Status.style.flexShrink = 1;
            m_Status.style.minWidth = 0;
            detailHeader.Add(m_Status);
            m_Open = new Button(OpenSelectedFile) { name = "codex-change-open", text = "Open file" };
            m_Open.style.flexShrink = 0;
            m_Open.style.fontSize = 9;
            detailHeader.Add(m_Open);
            m_Copy = new Button(() => UnityEditor.EditorGUIUtility.systemCopyBuffer = SelectedRecord()?.Diff ?? "")
            {
                name = "codex-change-copy", text = "Copy patch",
            };
            m_Copy.style.flexShrink = 0;
            m_Copy.style.fontSize = 9;
            detailHeader.Add(m_Copy);
            Add(detailHeader);
            m_Patch = new ScrollView(ScrollViewMode.VerticalAndHorizontal) { name = "codex-change-patch" };
            m_Patch.style.flexGrow = 1;
            m_Patch.style.minHeight = 0;
            m_Patch.style.backgroundColor = CodexPalette.CodeBackground;
            m_Patch.style.borderTopLeftRadius = 6;
            m_Patch.style.borderTopRightRadius = 6;
            m_Patch.style.borderBottomLeftRadius = 6;
            m_Patch.style.borderBottomRightRadius = 6;
            m_Diff = new CodexPatchView { name = "codex-change-diff" };
            m_Patch.Add(m_Diff);
            m_NoPatch = Text("No patch was provided for this recorded change.", 10, CodexPalette.Muted);
            m_NoPatch.name = "codex-change-no-patch";
            m_NoPatch.style.paddingLeft = m_NoPatch.style.paddingRight = 8;
            m_NoPatch.style.paddingTop = m_NoPatch.style.paddingBottom = 8;
            m_Patch.Add(m_NoPatch);
            Add(m_Patch);
            m_Patch.contentViewport.RegisterCallback<GeometryChangedEvent>(evt =>
            {
                // Stretch every line across the viewport, including patches whose longest line is short.
                m_Diff.style.minWidth = evt.newRect.width;
                RestoreReadingPosition();
            });
        }

        /// <summary>Refreshes from the selected conversation without changing its data.</summary>
        public void Refresh(CodexWorkspace workspace)
        {
            m_Workspace = workspace;
            string threadId = workspace?.SelectedThreadId;
            if (m_ThreadId != threadId)
            {
                m_ThreadId = threadId;
                m_TurnId = null;
                m_RecordKey = null;
                m_PatchOffset = Vector2.zero;
                m_Patch.scrollOffset = Vector2.zero;
            }

            m_Turns.Clear();
            JObject live = workspace?.LiveTurn;
            string liveId = live?["id"]?.Value<string>();
            // A completed turn can remain in LiveTurn until history hydration
            // succeeds. Its per-file records remain useful; its aggregate is stale.
            AddTurn(live, workspace?.IsRunning == true);
            if (workspace != null)
            {
                foreach (JObject turn in workspace.Turns)
                {
                    if (liveId != null && turn["id"]?.Value<string>() == liveId) continue;
                    AddTurn(turn, false);
                }
            }

            int selected = m_Turns.FindIndex(turn => turn.Id == m_TurnId);
            if (selected < 0)
            {
                selected = m_Turns.Count > 0 ? 0 : -1;
                m_TurnId = selected >= 0 ? m_Turns[selected].Id : null;
                m_RecordKey = null;
            }

            m_Empty.style.display = selected >= 0 ? DisplayStyle.None : DisplayStyle.Flex;
            m_Empty.text = string.IsNullOrEmpty(threadId)
                ? "Open a conversation to inspect its recorded changes."
                : "No recorded file changes in the loaded turns. Edits made outside recorded tools may not appear here.";
            bool earlier = workspace != null && (workspace.HasMoreTurns || workspace.IsLoadingMoreTurns);
            m_Earlier.style.display = earlier ? DisplayStyle.Flex : DisplayStyle.None;
            m_Earlier.SetEnabled(earlier && !workspace.IsLoadingMoreTurns);
            m_Earlier.text = workspace?.IsLoadingMoreTurns == true ? "Loading earlier turns…" : "Load earlier turns";
            RenderTurn();
        }

        /// <summary>Preserves reading positions while the shared right column changes tabs.</summary>
        public void SetVisible(bool visible)
        {
            bool wasVisible = style.display.value != DisplayStyle.None;
            if (visible == wasVisible) return;
            if (!visible)
            {
                m_PatchOffset = m_Patch.scrollOffset;
            }

            style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            if (visible)
            {
                m_RestoreScroll = true;
                schedule.Execute(RestoreReadingPosition);
            }
        }

        void RestoreReadingPosition()
        {
            if (!m_RestoreScroll || style.display.value == DisplayStyle.None || m_Patch.contentViewport.layout.height <= 0) return;
            m_RestoreScroll = false;
            m_Patch.scrollOffset = m_PatchOffset;
        }

        void AddTurn(JObject source, bool live)
        {
            if (source?["items"] is not JArray items) return;
            string id = source["id"]?.Value<string>();
            if (string.IsNullOrEmpty(id)) return;
            var turn = new ChangeTurn { Id = id };
            foreach (JToken item in items)
            {
                string type = item["type"]?.Value<string>();
                if (type == "userMessage" && string.IsNullOrEmpty(turn.Title) && item is JObject message)
                    turn.Title = CodexWorkspace.ExtractUserText(message);
                string itemId = item["id"]?.Value<string>();
                if (type == "fileChange" && item["changes"] is JArray changes)
                {
                    for (int index = 0; index < changes.Count; index++)
                    {
                        JToken change = changes[index];
                        JToken operation = change["kind"];
                        string operationName = operation is JObject ? operation["type"]?.Value<string>() : operation?.Value<string>();
                        string path = change["path"]?.Value<string>() ?? "(unknown file)";
                        string move = (operation as JObject)?["move_path"]?.Value<string>();
                        turn.Records.Add(new ChangeRecord
                        {
                            Key = itemId + "#" + index,
                            Path = string.IsNullOrEmpty(move) ? path : move,
                            PreviousPath = string.IsNullOrEmpty(move) ? null : path,
                            Operation = operationName ?? "change",
                            Status = item["status"]?.Value<string>() ?? "unknown",
                            Diff = change["diff"]?.Value<string>() ?? "",
                        });
                    }
                }
                else if (live && type == "turnDiff" && !string.IsNullOrEmpty(item["diff"]?.Value<string>()))
                {
                    turn.Records.Add(new ChangeRecord
                    {
                        Key = itemId, Path = "Live turn diff", Operation = "live", Status = "live snapshot",
                        Diff = item["diff"]?.Value<string>(), Live = true,
                    });
                }
            }

            if (turn.Records.Count == 0) return;
            long timestamp = source["startedAtMs"]?.Value<long?>() ?? 0;
            turn.Time = timestamp > 0
                ? DateTimeOffset.FromUnixTimeMilliseconds(timestamp).LocalDateTime.ToString("MM-dd HH:mm")
                : "Recorded turn";
            turn.Title = string.IsNullOrWhiteSpace(turn.Title) ? "File changes" : turn.Title.Replace('\r', ' ').Replace('\n', ' ').Trim();
            turn.Live = live;
            m_Turns.Add(turn);
        }

        IEnumerable<(ChangeTurn Turn, ChangeRecord Record)> Entries()
        {
            foreach (ChangeTurn turn in m_Turns)
                foreach (ChangeRecord record in turn.Records)
                    yield return (turn, record);
        }

        void RenderTurn()
        {
            ChangeTurn turn = m_Turns.Find(candidate => candidate.Id == m_TurnId);
            if (turn == null || !turn.Records.Exists(record => record.Key == m_RecordKey))
            {
                string next = turn?.Records[0].Key;
                if (next != m_RecordKey)
                {
                    m_RecordKey = next;
                    m_PatchOffset = Vector2.zero;
                    m_Patch.scrollOffset = Vector2.zero;
                }
            }

            ChangeRecord selected = SelectedRecord();
            var entries = Entries().ToList();
            int index = entries.FindIndex(entry => entry.Turn.Id == m_TurnId && entry.Record.Key == m_RecordKey);
            m_FileButton.SetEnabled(selected != null);
            m_FileTitle.text = selected?.FileName ?? "No recorded changes";
            m_FileButton.tooltip = selected == null ? "No recorded changes"
                : selected.DisplayPath + "\n" + turn.Time + " · " + turn.Title + "\nClick to search files and changes";
            m_FileInfo.text = selected == null ? "" : (index + 1) + " / " + entries.Count + " · "
                + (turn.Live ? "LIVE · " : "") + turn.Title;
            m_Newer.SetEnabled(index > 0);
            m_Older.SetEnabled(index >= 0 && index < entries.Count - 1);
            m_Patch.style.display = selected != null ? DisplayStyle.Flex : DisplayStyle.None;
            m_Copy.style.display = selected != null ? DisplayStyle.Flex : DisplayStyle.None;
            m_Copy.SetEnabled(!string.IsNullOrEmpty(selected?.Diff));
            string file = SelectedFilePath();
            m_Open.style.display = selected != null ? DisplayStyle.Flex : DisplayStyle.None;
            m_Open.SetEnabled(file != null);
            m_Open.tooltip = file ?? (selected?.Live == true ? "Aggregate diff has no single file" : "File is not available on this computer");
            m_Status.text = selected == null ? "" : selected.Live
                ? "Live snapshot · available while this turn is running"
                : selected.Operation + " · " + selected.Status;
            m_Status.style.color = StatusColor(selected?.Status);
            m_Diff.SetPatch(selected?.Diff, selected?.Operation);
            bool hasPatch = !string.IsNullOrEmpty(selected?.Diff);
            m_Diff.style.display = hasPatch ? DisplayStyle.Flex : DisplayStyle.None;
            m_NoPatch.style.display = selected != null && !hasPatch ? DisplayStyle.Flex : DisplayStyle.None;
        }

        ChangeRecord SelectedRecord()
        {
            return m_Turns.Find(turn => turn.Id == m_TurnId)?.Records.Find(record => record.Key == m_RecordKey);
        }

        string SelectedFilePath()
        {
            ChangeRecord record = SelectedRecord();
            if (record == null || record.Live) return null;
            string directory = m_Workspace?.SelectedCwd ?? Path.GetDirectoryName(Application.dataPath);
            string file = CodexMarkdown.ResolveLocalPath(record.Path, directory);
            return File.Exists(file) ? file : null;
        }

        void OpenSelectedFile()
        {
            string file = SelectedFilePath();
            if (file != null) CodexMarkdown.OpenLink(file, m_Workspace?.SelectedCwd ?? Path.GetDirectoryName(Application.dataPath));
            else m_Open.SetEnabled(false);
        }

        void SelectRecord(string turnId, string key)
        {
            if (m_TurnId == turnId && m_RecordKey == key) return;
            if (m_Turns.Find(turn => turn.Id == turnId)?.Records.Exists(record => record.Key == key) != true) return;
            m_TurnId = turnId;
            m_RecordKey = key;
            m_PatchOffset = Vector2.zero;
            m_Patch.scrollOffset = Vector2.zero;
            RenderTurn();
        }

        void SelectAdjacent(int offset)
        {
            var entries = Entries().ToList();
            int index = entries.FindIndex(entry => entry.Turn.Id == m_TurnId && entry.Record.Key == m_RecordKey);
            int next = index + offset;
            if (index < 0 || next < 0 || next >= entries.Count) return;
            SelectRecord(entries[next].Turn.Id, entries[next].Record.Key);
        }

        void ShowFileMenu()
        {
            var items = new List<CodexAssistantWindow.PopupItem>();
            int index = 0;
            foreach (var entry in Entries())
            {
                ChangeTurn turn = entry.Turn;
                ChangeRecord record = entry.Record;
                items.Add(new CodexAssistantWindow.PopupItem
                {
                    Label = (++index) + ". " + record.FileName + " · " + turn.Time + " · " + turn.Title
                        + " · " + record.Operation + " · " + record.DisplayPath,
                    Selected = turn.Id == m_TurnId && record.Key == m_RecordKey,
                    Action = () => SelectRecord(turn.Id, record.Key),
                });
            }
            m_ShowFileMenu(m_FileButton, items);
        }

        static Label Text(string text, int size, Color color)
        {
            var label = new Label(text) { enableRichText = false };
            label.style.fontSize = size;
            label.style.color = color;
            label.style.whiteSpace = WhiteSpace.Normal;
            label.style.unityTextAlign = TextAnchor.MiddleLeft;
            label.style.marginTop = label.style.marginBottom = 0;
            return label;
        }

        static Color StatusColor(string status)
        {
            return status switch
            {
                "failed" => CodexPalette.Error,
                "declined" => CodexPalette.Warning,
                "completed" => CodexPalette.Success,
                _ => CodexPalette.Muted,
            };
        }
    }
}
