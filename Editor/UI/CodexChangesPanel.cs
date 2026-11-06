using System;
using System.Collections.Generic;
using System.Text;
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
            public string Operation;
            public string Status;
            public string Diff;
            public bool Live;
        }

        readonly Label m_Count;
        readonly Label m_Empty;
        readonly Button m_Earlier;
        readonly ScrollView m_Files;
        readonly VisualElement m_FileList;
        readonly Label m_Path;
        readonly Label m_Status;
        readonly CodexPatchView m_Diff;
        readonly Label m_NoPatch;
        readonly Button m_Copy;
        readonly ScrollView m_Patch;
        readonly List<ChangeTurn> m_Turns = new List<ChangeTurn>();
        CodexWorkspace m_Workspace;
        string m_ThreadId;
        string m_TurnId;
        string m_RecordKey;
        string m_RowSignature;
        bool m_TurnExpanded = true;
        bool m_RestoreScroll;
        Vector2 m_FileOffset;
        Vector2 m_PatchOffset;

        public CodexChangesPanel()
        {
            name = "codex-changes";
            style.flexGrow = 1;
            style.minHeight = 0;
            style.paddingLeft = 10;
            style.paddingRight = 10;
            style.paddingTop = 10;
            style.paddingBottom = 10;

            m_Count = Text("Change history", 10, CodexPalette.Muted);
            m_Count.style.marginBottom = 8;
            m_Count.style.flexShrink = 0;
            Add(m_Count);

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

            m_Files = new ScrollView(ScrollViewMode.Vertical);
            m_Files.name = "codex-changes-navigation";
            m_Files.style.maxHeight = Length.Percent(42);
            m_Files.style.minHeight = 0;
            m_Files.style.flexShrink = 1;
            m_Files.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            m_FileList = new VisualElement { name = "codex-change-list" };
            m_Files.Add(m_FileList);
            Add(m_Files);

            var detailHeader = new VisualElement();
            detailHeader.style.flexDirection = FlexDirection.Row;
            detailHeader.style.alignItems = Align.Center;
            detailHeader.style.marginTop = 10;
            detailHeader.style.marginBottom = 3;
            m_Path = Text("", 11, CodexPalette.Text);
            m_Path.style.flexGrow = 1;
            m_Path.style.flexShrink = 1;
            m_Path.style.unityFontStyleAndWeight = FontStyle.Bold;
            m_Path.style.whiteSpace = WhiteSpace.NoWrap;
            m_Path.style.overflow = Overflow.Hidden;
            m_Path.style.textOverflow = TextOverflow.Ellipsis;
            detailHeader.Add(m_Path);
            m_Copy = new Button(() => UnityEditor.EditorGUIUtility.systemCopyBuffer = SelectedRecord()?.Diff ?? "")
            {
                name = "codex-change-copy", text = "Copy patch",
            };
            m_Copy.style.flexShrink = 0;
            m_Copy.style.fontSize = 9;
            detailHeader.Add(m_Copy);
            Add(detailHeader);
            m_Status = Text("", 9, CodexPalette.Muted);
            m_Status.name = "codex-change-status";
            m_Status.style.marginBottom = 6;
            Add(m_Status);

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
                m_TurnExpanded = true;
                m_RecordKey = null;
                m_FileOffset = m_PatchOffset = Vector2.zero;
                m_Files.scrollOffset = m_Patch.scrollOffset = Vector2.zero;
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
                m_TurnExpanded = true;
                m_RecordKey = null;
            }

            m_Count.text = m_Turns.Count == 0 ? "Change history" : "Change history · " + m_Turns.Count + (m_Turns.Count == 1 ? " turn" : " turns");
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
                m_FileOffset = m_Files.scrollOffset;
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
            m_Files.scrollOffset = m_FileOffset;
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
                            Path = string.IsNullOrEmpty(move) ? path : path + " → " + move,
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

            var signature = new StringBuilder(m_TurnId);
            signature.Append('\n').Append(m_RecordKey).Append('|').Append(m_TurnExpanded);
            foreach (ChangeTurn group in m_Turns)
            {
                signature.Append('\n').Append(group.Id).Append('|').Append(group.Title).Append('|').Append(group.Time).Append('|').Append(group.Live);
                foreach (ChangeRecord record in group.Records)
                    signature.Append('\n').Append(record.Key).Append('|').Append(record.Path).Append('|').Append(record.Operation).Append('|').Append(record.Status);
            }

            string rows = signature.ToString();
            if (rows != m_RowSignature)
            {
                m_RowSignature = rows;
                Vector2 offset = m_Files.scrollOffset;
                m_FileList.Clear();
                foreach (ChangeTurn group in m_Turns)
                {
                    AddTurnHeader(group);
                    if (group.Id != m_TurnId || !m_TurnExpanded) continue;
                    for (int index = 0; index < group.Records.Count; index++)
                    {
                        ChangeRecord record = group.Records[index];
                        int edit = 0;
                        if (group.Records.Exists(candidate => !ReferenceEquals(candidate, record) && candidate.Path == record.Path))
                            for (int previous = 0; previous <= index; previous++)
                                if (group.Records[previous].Path == record.Path) edit++;
                        AddRow(record, edit);
                    }
                }
                m_FileList.Add(m_Earlier);
                m_Files.schedule.Execute(() => m_Files.scrollOffset = offset);
            }

            ChangeRecord selected = SelectedRecord();
            m_Files.style.display = m_Turns.Count > 0 || m_Earlier.style.display.value != DisplayStyle.None ? DisplayStyle.Flex : DisplayStyle.None;
            m_Patch.style.display = selected != null ? DisplayStyle.Flex : DisplayStyle.None;
            m_Copy.style.display = selected != null ? DisplayStyle.Flex : DisplayStyle.None;
            m_Copy.SetEnabled(!string.IsNullOrEmpty(selected?.Diff));
            m_Path.text = selected?.Path ?? "";
            m_Path.tooltip = m_Path.text;
            m_Status.text = selected == null ? "" : selected.Live
                ? "Live snapshot · available while this turn is running"
                : selected.Operation + " · " + selected.Status;
            m_Status.style.color = StatusColor(selected?.Status);
            m_Diff.SetPatch(selected?.Diff);
            bool hasPatch = !string.IsNullOrEmpty(selected?.Diff);
            m_Diff.style.display = hasPatch ? DisplayStyle.Flex : DisplayStyle.None;
            m_NoPatch.style.display = selected != null && !hasPatch ? DisplayStyle.Flex : DisplayStyle.None;
        }

        ChangeRecord SelectedRecord()
        {
            return m_Turns.Find(turn => turn.Id == m_TurnId)?.Records.Find(record => record.Key == m_RecordKey);
        }

        void SelectRecord(string key)
        {
            if (m_RecordKey == key) return;
            m_RecordKey = key;
            m_PatchOffset = Vector2.zero;
            m_Patch.scrollOffset = Vector2.zero;
            RenderTurn();
        }

        void AddTurnHeader(ChangeTurn turn)
        {
            bool selected = turn.Id == m_TurnId;
            var header = new Button(() =>
            {
                if (m_TurnId == turn.Id) m_TurnExpanded = !m_TurnExpanded;
                else
                {
                    m_TurnId = turn.Id;
                    m_TurnExpanded = true;
                    m_RecordKey = null;
                }
                RenderTurn();
            }) { name = "codex-change-turn", userData = turn.Id, tooltip = turn.Title + "\n" + turn.Time };
            header.style.flexDirection = FlexDirection.Row;
            header.style.alignItems = Align.Center;
            header.style.paddingTop = header.style.paddingBottom = 6;
            header.style.paddingLeft = 4;
            header.style.marginTop = 2;
            header.style.marginBottom = 2;
            header.style.marginLeft = header.style.marginRight = 0;
            header.style.backgroundColor = selected ? CodexPalette.SurfaceRaised : Color.clear;
            header.style.borderTopWidth = header.style.borderBottomWidth = 0;
            header.style.borderLeftWidth = header.style.borderRightWidth = 0;
            var arrow = Text(selected && m_TurnExpanded ? "▾" : "▸", 11, CodexPalette.Muted);
            arrow.style.width = 16;
            arrow.style.flexShrink = 0;
            header.Add(arrow);
            var body = new VisualElement();
            body.style.flexGrow = 1;
            body.style.minWidth = 0;
            var title = Text(turn.Title, 11, CodexPalette.Text);
            title.style.whiteSpace = WhiteSpace.NoWrap;
            title.style.overflow = Overflow.Hidden;
            title.style.textOverflow = TextOverflow.Ellipsis;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            body.Add(title);
            body.Add(Text((turn.Live ? "LIVE · " : "") + turn.Time + " · " + turn.Records.Count + (turn.Records.Count == 1 ? " change" : " changes"), 9,
                turn.Live ? CodexPalette.Accent : CodexPalette.Muted));
            header.Add(body);
            m_FileList.Add(header);
        }

        void AddRow(ChangeRecord record, int edit)
        {
            var button = new Button(() => SelectRecord(record.Key)) { name = "codex-change-record", tooltip = record.Path };
            button.style.flexDirection = FlexDirection.Row;
            button.style.alignItems = Align.Center;
            button.style.paddingTop = 4;
            button.style.paddingBottom = 4;
            button.style.paddingLeft = 6;
            button.style.marginLeft = 16;
            button.style.marginRight = 0;
            button.style.marginBottom = 1;
            button.style.backgroundColor = record.Key == m_RecordKey ? CodexPalette.Selected : Color.clear;
            button.style.borderTopWidth = button.style.borderBottomWidth = 0;
            button.style.borderLeftWidth = 2;
            button.style.borderLeftColor = record.Key == m_RecordKey ? CodexPalette.Accent : Color.clear;
            button.style.borderRightWidth = 0;
            string operation = record.Live ? "●" : record.Operation switch { "add" => "A", "delete" => "D", "update" => "M", _ => "~" };
            var badge = Text(operation, 10, record.Operation == "delete" ? CodexPalette.Error : record.Operation == "add" ? CodexPalette.Success : CodexPalette.Accent);
            badge.style.width = 20;
            badge.style.flexShrink = 0;
            button.Add(badge);
            var body = new VisualElement();
            body.style.flexGrow = 1;
            body.style.flexShrink = 1;
            body.style.minWidth = 0;
            string path = record.Path.Replace('\\', '/');
            int slash = path.LastIndexOf('/');
            var file = Text(path.Substring(slash + 1), 11, CodexPalette.Text);
            file.style.whiteSpace = WhiteSpace.NoWrap;
            file.style.overflow = Overflow.Hidden;
            file.style.textOverflow = TextOverflow.Ellipsis;
            body.Add(file);
            string directory = slash >= 0 ? path.Substring(0, slash) : "";
            if (edit > 0) directory += (directory.Length > 0 ? " · " : "") + "edit " + edit;
            if (!string.IsNullOrEmpty(directory))
            {
                var location = Text(directory, 9, CodexPalette.Muted);
                location.style.whiteSpace = WhiteSpace.NoWrap;
                location.style.overflow = Overflow.Hidden;
                location.style.textOverflow = TextOverflow.Ellipsis;
                body.Add(location);
            }
            button.Add(body);
            if (record.Status != "completed" && !record.Live)
            {
                var status = Text(record.Status, 9, StatusColor(record.Status));
                status.style.flexShrink = 0;
                status.style.marginLeft = 4;
                button.Add(status);
            }
            m_FileList.Add(button);
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
