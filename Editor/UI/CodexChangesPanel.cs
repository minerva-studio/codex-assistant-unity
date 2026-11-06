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
            public string Label;
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

        readonly DropdownField m_Turn;
        readonly Label m_Empty;
        readonly Button m_Earlier;
        readonly ScrollView m_Files;
        readonly VisualElement m_FileList;
        readonly Label m_Path;
        readonly Label m_Status;
        readonly Label m_Diff;
        readonly Button m_Copy;
        readonly ScrollView m_Patch;
        readonly List<ChangeTurn> m_Turns = new List<ChangeTurn>();
        CodexWorkspace m_Workspace;
        string m_ThreadId;
        string m_TurnId;
        string m_RecordKey;
        string m_RowSignature;
        bool m_Refreshing;
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

            var hint = Text("File edits recorded by Codex", 10, CodexPalette.Muted);
            hint.style.marginBottom = 8;
            Add(hint);
            m_Turn = new DropdownField { name = "codex-changes-turn" };
            m_Turn.style.flexShrink = 0;
            m_Turn.style.marginBottom = 6;
            m_Turn.RegisterValueChangedCallback(evt =>
            {
                if (m_Refreshing) return;
                int index = m_Turn.choices.IndexOf(evt.newValue);
                if (index < 0 || index >= m_Turns.Count) return;
                m_TurnId = m_Turns[index].Id;
                m_RecordKey = null;
                RenderTurn();
            });
            Add(m_Turn);

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

            m_Files = new ScrollView(ScrollViewMode.Vertical);
            m_Files.style.maxHeight = 220;
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
            m_Diff = Text("", 10, CodexPalette.Text);
            m_Diff.name = "codex-change-diff";
            m_Diff.style.unityFont = UnityEditor.EditorGUIUtility.Load("Fonts/RobotoMono/RobotoMono-Regular.ttf") as Font;
            m_Diff.style.whiteSpace = WhiteSpace.Pre;
            m_Diff.style.paddingLeft = 8;
            m_Diff.style.paddingRight = 8;
            m_Diff.style.paddingTop = 8;
            m_Diff.style.paddingBottom = 8;
            m_Patch.Add(m_Diff);
            Add(m_Patch);
            m_Patch.contentViewport.RegisterCallback<GeometryChangedEvent>(_ => RestoreReadingPosition());
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
                m_RecordKey = null;
            }

            var choices = new List<string>();
            foreach (ChangeTurn turn in m_Turns) choices.Add(turn.Label);
            m_Refreshing = true;
            m_Turn.choices = choices;
            m_Turn.SetValueWithoutNotify(selected >= 0 ? choices[selected] : "No recorded changes");
            m_Refreshing = false;
            m_Turn.SetEnabled(selected >= 0);
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
            string caption = timestamp > 0
                ? DateTimeOffset.FromUnixTimeMilliseconds(timestamp).LocalDateTime.ToString("MM-dd HH:mm:ss")
                : "Turn";
            turn.Label = (live ? "Live · " : "") + caption + " · " + id;
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
            signature.Append('\n').Append(m_RecordKey);
            if (turn != null)
            {
                foreach (ChangeRecord record in turn.Records)
                    signature.Append('\n').Append(record.Key).Append('|').Append(record.Path).Append('|').Append(record.Operation).Append('|').Append(record.Status);
            }

            string rows = signature.ToString();
            if (rows != m_RowSignature)
            {
                m_RowSignature = rows;
                m_FileList.Clear();
                if (turn != null) foreach (ChangeRecord record in turn.Records) AddRow(record);
            }

            ChangeRecord selected = SelectedRecord();
            m_Files.style.display = selected != null ? DisplayStyle.Flex : DisplayStyle.None;
            m_Patch.style.display = selected != null ? DisplayStyle.Flex : DisplayStyle.None;
            m_Copy.style.display = selected != null ? DisplayStyle.Flex : DisplayStyle.None;
            m_Copy.SetEnabled(!string.IsNullOrEmpty(selected?.Diff));
            m_Path.text = selected?.Path ?? "";
            m_Status.text = selected == null ? "" : selected.Live
                ? "Live snapshot · available while this turn is running"
                : selected.Operation + " · " + selected.Status;
            m_Status.style.color = StatusColor(selected?.Status);
            string patch = selected == null ? "" : string.IsNullOrEmpty(selected.Diff)
                ? "No patch was provided for this recorded change."
                : selected.Diff;
            if (m_Diff.text != patch) m_Diff.text = patch;
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

        void AddRow(ChangeRecord record)
        {
            var button = new Button(() => SelectRecord(record.Key)) { name = "codex-change-record", tooltip = record.Path };
            button.style.flexDirection = FlexDirection.Row;
            button.style.paddingTop = 7;
            button.style.paddingBottom = 7;
            button.style.marginBottom = 4;
            button.style.backgroundColor = record.Key == m_RecordKey ? CodexPalette.Selected : CodexPalette.Surface;
            button.style.borderTopWidth = button.style.borderBottomWidth = 0;
            button.style.borderLeftWidth = button.style.borderRightWidth = 0;
            var badge = Text(record.Live ? "LIVE" : record.Operation.ToUpperInvariant(), 8, record.Operation == "delete" ? CodexPalette.Error : CodexPalette.Accent);
            badge.style.width = 48;
            badge.style.flexShrink = 0;
            button.Add(badge);
            var body = new VisualElement();
            body.style.flexGrow = 1;
            body.style.flexShrink = 1;
            body.Add(Text(record.Path, 10, CodexPalette.Text));
            body.Add(Text(record.Status, 9, StatusColor(record.Status)));
            button.Add(body);
            m_FileList.Add(button);
        }

        static Label Text(string text, int size, Color color)
        {
            var label = new Label(text) { enableRichText = false };
            label.style.fontSize = size;
            label.style.color = color;
            label.style.whiteSpace = WhiteSpace.Normal;
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
