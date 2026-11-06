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
    /// Read-only, continuous view of recorded edits in one turn. Workspace turns own the data;
    /// the panel retains only the chosen scope, folding and reading position.
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

        sealed class RecordView : VisualElement
        {
            readonly CodexChangesPanel m_Owner;
            readonly Button m_Toggle;
            readonly Label m_Arrow;
            readonly Label m_Operation;
            readonly Label m_Name;
            readonly Label m_Location;
            readonly Label m_Status;
            readonly Label m_Added;
            readonly Label m_Removed;
            readonly Button m_Open;
            readonly Button m_Copy;
            readonly Label m_NoPatch;
            readonly VisualElement m_Body;
            ChangeRecord m_Record;
            public readonly CodexPatchView Patch;
            public bool Expanded { get; private set; }

            public RecordView(CodexChangesPanel owner, ChangeRecord record)
            {
                m_Owner = owner;
                name = "codex-change-record";
                style.flexShrink = 0;
                style.minWidth = 0;
                style.borderBottomWidth = 1;
                style.borderBottomColor = CodexPalette.Border;
                var header = new VisualElement();
                header.style.flexDirection = FlexDirection.Row;
                header.style.alignItems = Align.Center;
                header.style.paddingTop = header.style.paddingBottom = 6;
                header.style.backgroundColor = CodexPalette.Surface;
                m_Toggle = new Button(() =>
                {
                    SetExpanded(!Expanded);
                    m_Owner.UpdateCollapseButton();
                }) { name = "codex-change-toggle" };
                m_Toggle.style.flexDirection = FlexDirection.Row;
                m_Toggle.style.alignItems = Align.Center;
                m_Toggle.style.flexGrow = 1;
                m_Toggle.style.flexShrink = 1;
                m_Toggle.style.minWidth = 0;
                m_Toggle.style.backgroundColor = Color.clear;
                m_Toggle.style.borderTopWidth = m_Toggle.style.borderBottomWidth = 0;
                m_Toggle.style.borderLeftWidth = m_Toggle.style.borderRightWidth = 0;
                m_Arrow = Text("", 11, CodexPalette.Muted);
                m_Arrow.style.width = 14;
                m_Arrow.style.flexShrink = 0;
                m_Toggle.Add(m_Arrow);
                m_Operation = Text("", 12, CodexPalette.Muted);
                m_Operation.name = "codex-change-operation";
                m_Operation.style.width = 16;
                m_Operation.style.flexShrink = 0;
                m_Operation.style.unityFontStyleAndWeight = FontStyle.Bold;
                m_Toggle.Add(m_Operation);
                var title = new VisualElement();
                title.style.flexGrow = 1;
                title.style.minWidth = 0;
                m_Name = Text("", 11, CodexPalette.Text);
                m_Name.name = "codex-change-filename";
                m_Name.style.unityFontStyleAndWeight = FontStyle.Bold;
                Ellipsis(m_Name);
                m_Location = Text("", 9, CodexPalette.Muted);
                Ellipsis(m_Location);
                title.Add(m_Name);
                title.Add(m_Location);
                m_Toggle.Add(title);
                header.Add(m_Toggle);
                m_Status = Text("", 11, CodexPalette.Muted);
                m_Status.name = "codex-change-status";
                m_Status.style.flexShrink = 0;
                m_Status.style.marginRight = 5;
                header.Add(m_Status);
                m_Added = Text("", 10, CodexPalette.Success);
                m_Removed = Text("", 10, CodexPalette.Error);
                foreach (Label count in new[] { m_Added, m_Removed })
                {
                    count.style.flexShrink = 0;
                    count.style.marginRight = 5;
                    header.Add(count);
                }
                m_Open = new Button(() =>
                {
                    string file = m_Owner.FilePath(m_Record);
                    if (file != null) CodexMarkdown.OpenLink(file, m_Owner.Directory);
                    else m_Open.SetEnabled(false);
                }) { name = "codex-change-open", text = "Open", tooltip = "Open file" };
                m_Copy = new Button(() => UnityEditor.EditorGUIUtility.systemCopyBuffer = Patch.Patch)
                    { name = "codex-change-copy", text = "Copy", tooltip = "Copy original patch" };
                foreach (Button action in new[] { m_Open, m_Copy })
                {
                    action.style.flexShrink = 0;
                    action.style.fontSize = 9;
                    action.style.marginLeft = action.style.marginRight = 1;
                    header.Add(action);
                }
                Add(header);
                m_Body = new VisualElement { name = "codex-change-body" };
                m_Body.style.flexShrink = 0;
                m_Body.style.minWidth = 0;
                Patch = new CodexPatchView(wrap: true) { name = "codex-change-diff" };
                m_Body.Add(Patch);
                m_NoPatch = Text("No patch was provided for this recorded change.", 10, CodexPalette.Muted);
                m_NoPatch.name = "codex-change-no-patch";
                m_NoPatch.style.paddingLeft = m_NoPatch.style.paddingRight = 8;
                m_NoPatch.style.paddingTop = m_NoPatch.style.paddingBottom = 8;
                m_Body.Add(m_NoPatch);
                Add(m_Body);
                SetExpanded(!record.Live);
            }

            public void Update(ChangeRecord record, int edit, int edits)
            {
                m_Record = record;
                m_Name.text = record.FileName + (edits > 1 ? " · " + edit + "/" + edits : "");
                m_Operation.text = record.Live ? "●" : record.PreviousPath != null ? "→" : record.Operation switch
                {
                    "add" => "+",
                    "delete" => "−",
                    _ => "●",
                };
                m_Operation.style.color = record.Live || record.PreviousPath != null ? CodexPalette.Accent : record.Operation switch
                {
                    "add" => CodexPalette.Success,
                    "delete" => CodexPalette.Error,
                    _ => CodexPalette.Warning,
                };
                m_Operation.tooltip = record.Live ? "Live aggregate snapshot" : record.PreviousPath != null ? "Renamed file" : record.Operation;
                m_Location.text = record.Live ? "Combined snapshot · overlaps individual edits" : record.DisplayPath;
                string status = record.Live ? "Live snapshot · available while this turn is running"
                    : record.Operation + " · " + record.Status + (edits > 1 ? " · edit " + edit + " / " + edits : "");
                m_Toggle.tooltip = record.DisplayPath + "\n" + status + "\nClick to expand or collapse";
                Patch.SetPatch(record.Diff, record.Operation);
                m_Added.text = "+" + Patch.Additions;
                m_Removed.text = "−" + Patch.Deletions;
                m_Copy.SetEnabled(!string.IsNullOrEmpty(record.Diff));
                string file = m_Owner.FilePath(record);
                m_Open.SetEnabled(file != null);
                m_Open.tooltip = file ?? (record.Live ? "Aggregate diff has no single file" : "File is not available on this computer");
                m_Status.text = record.Status == "failed" || record.Status == "declined" ? "!" : "…";
                m_Status.tooltip = status;
                m_Status.style.display = record.Live || record.Status == "completed" ? DisplayStyle.None : DisplayStyle.Flex;
                m_Status.style.color = StatusColor(record.Status);
                bool hasPatch = !string.IsNullOrEmpty(record.Diff);
                Patch.style.display = hasPatch ? DisplayStyle.Flex : DisplayStyle.None;
                m_NoPatch.style.display = hasPatch ? DisplayStyle.None : DisplayStyle.Flex;
            }

            public void SetExpanded(bool expanded)
            {
                Expanded = expanded;
                m_Arrow.text = expanded ? "▾" : "▸";
                m_Body.style.display = expanded ? DisplayStyle.Flex : DisplayStyle.None;
            }
        }

        readonly Action<VisualElement, string, List<CodexAssistantWindow.PopupItem>> m_ShowMenu;
        readonly Button m_Scope;
        readonly Label m_Title;
        readonly Label m_Added;
        readonly Label m_Removed;
        readonly Button m_Files;
        readonly Button m_Collapse;
        readonly Label m_Empty;
        readonly Button m_Earlier;
        readonly ScrollView m_Content;
        readonly List<ChangeTurn> m_Turns = new List<ChangeTurn>();
        readonly Dictionary<string, RecordView> m_Records = new Dictionary<string, RecordView>();
        CodexWorkspace m_Workspace;
        string m_ThreadId;
        // Null follows the latest recorded turn; a non-null id pins historical scope.
        string m_TurnId;
        string m_RenderedTurnId;
        bool m_RestoreScroll;
        Vector2 m_ReadingOffset;
        string Directory => m_Workspace?.SelectedCwd ?? Path.GetDirectoryName(Application.dataPath);

        /// <param name="showMenu">Shows titled, searchable scope or file-jump actions in the owning window's popup layer.</param>
        public CodexChangesPanel(Action<VisualElement, string, List<CodexAssistantWindow.PopupItem>> showMenu)
        {
            m_ShowMenu = showMenu;
            name = "codex-changes";
            style.flexGrow = 1;
            style.minHeight = 0;
            style.paddingLeft = style.paddingRight = 8;
            style.paddingTop = style.paddingBottom = 8;
            var toolbar = new VisualElement { name = "codex-changes-toolbar" };
            toolbar.style.flexDirection = FlexDirection.Row;
            toolbar.style.alignItems = Align.Center;
            toolbar.style.flexShrink = 0;
            m_Scope = new Button(ShowScopeMenu) { name = "codex-change-scope", text = "Latest turn ▾" };
            m_Scope.style.flexShrink = 1;
            m_Scope.style.minWidth = 0;
            m_Scope.style.height = 28;
            m_Scope.style.unityTextAlign = TextAnchor.MiddleLeft;
            m_Scope.style.fontSize = 11;
            m_Scope.style.marginLeft = 0;
            m_Scope.style.paddingLeft = m_Scope.style.paddingRight = 9;
            m_Scope.style.backgroundColor = CodexPalette.SurfaceRaised;
            m_Scope.style.borderTopWidth = m_Scope.style.borderBottomWidth = 0;
            m_Scope.style.borderLeftWidth = m_Scope.style.borderRightWidth = 0;
            m_Scope.style.borderTopLeftRadius = m_Scope.style.borderTopRightRadius = 12;
            m_Scope.style.borderBottomLeftRadius = m_Scope.style.borderBottomRightRadius = 12;
            toolbar.Add(m_Scope);
            m_Added = Text("", 10, CodexPalette.Success);
            m_Added.name = "codex-changes-added";
            m_Removed = Text("", 10, CodexPalette.Error);
            m_Removed.name = "codex-changes-removed";
            foreach (Label count in new[] { m_Added, m_Removed })
            {
                count.style.flexShrink = 0;
                count.style.marginLeft = 4;
                toolbar.Add(count);
            }
            var space = new VisualElement();
            space.style.flexGrow = 1;
            toolbar.Add(space);
            m_Files = new Button(ShowFileMenu) { name = "codex-changes-files", text = "≡", tooltip = "Jump to file" };
            m_Collapse = new Button(() =>
            {
                bool expand = !m_Records.Values.Any(view => view.Expanded);
                foreach (RecordView view in m_Records.Values) view.SetExpanded(expand);
                UpdateCollapseButton();
            }) { name = "codex-changes-collapse", text = "−", tooltip = "Collapse all files" };
            foreach (Button button in new[] { m_Files, m_Collapse })
            {
                button.style.width = 25;
                button.style.height = 26;
                button.style.flexShrink = 0;
                button.style.fontSize = 14;
                button.style.marginLeft = 4;
                button.style.marginRight = 0;
                button.style.backgroundColor = Color.clear;
                button.style.borderTopWidth = button.style.borderBottomWidth = 0;
                button.style.borderLeftWidth = button.style.borderRightWidth = 0;
                toolbar.Add(button);
            }
            Add(toolbar);
            m_Title = Text("", 10, CodexPalette.Muted);
            m_Title.name = "codex-changes-summary";
            Ellipsis(m_Title);
            m_Title.style.flexShrink = 0;
            m_Title.style.marginTop = 4;
            m_Title.style.marginBottom = 8;
            Add(m_Title);
            m_Empty = Text("No recorded file changes in the loaded turns.", 11, CodexPalette.Muted);
            m_Empty.name = "codex-changes-empty";
            m_Empty.style.marginTop = 8;
            m_Empty.style.marginBottom = 12;
            Add(m_Empty);
            m_Earlier = new Button(() => m_Workspace?.LoadEarlierTurns())
                { name = "codex-changes-earlier", text = "Load earlier turns" };
            m_Earlier.style.flexShrink = 0;
            m_Earlier.style.marginBottom = 6;
            Add(m_Earlier);
            m_Content = new ScrollView(ScrollViewMode.Vertical) { name = "codex-change-content" };
            m_Content.style.flexGrow = 1;
            m_Content.style.minHeight = 0;
            m_Content.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            m_Content.contentContainer.style.minWidth = 0;
            m_Content.contentContainer.style.backgroundColor = CodexPalette.CodeBackground;
            Add(m_Content);
            m_Content.contentViewport.RegisterCallback<GeometryChangedEvent>(_ => RestoreReadingPosition());
        }

        /// <summary>Refreshes recorded data without rebuilding unchanged file sections or losing folding.</summary>
        public void Refresh(CodexWorkspace workspace)
        {
            m_Workspace = workspace;
            string threadId = workspace?.SelectedThreadId;
            if (m_ThreadId != threadId)
            {
                m_ThreadId = threadId;
                m_TurnId = null;
                m_RenderedTurnId = null;
                m_Records.Clear();
                m_Content.Clear();
                ResetReadingPosition();
            }
            m_Turns.Clear();
            JObject live = workspace?.LiveTurn;
            string liveId = live?["id"]?.Value<string>();
            // Completed LiveTurn can outlive history hydration; only its per-file records remain useful.
            AddTurn(live, workspace?.IsRunning == true);
            if (workspace != null)
                foreach (JObject turn in workspace.Turns)
                {
                    if (liveId != null && turn["id"]?.Value<string>() == liveId) continue;
                    AddTurn(turn, false);
                }
            if (m_TurnId != null && !m_Turns.Exists(turn => turn.Id == m_TurnId)) m_TurnId = null;
            m_Empty.style.display = m_Turns.Count == 0 ? DisplayStyle.Flex : DisplayStyle.None;
            m_Empty.text = string.IsNullOrEmpty(threadId)
                ? "Open a conversation to inspect its recorded changes."
                : "No recorded file changes in the loaded turns. Edits made outside recorded tools may not appear here.";
            bool earlier = workspace != null && (workspace.HasMoreTurns || workspace.IsLoadingMoreTurns);
            m_Earlier.style.display = earlier ? DisplayStyle.Flex : DisplayStyle.None;
            m_Earlier.SetEnabled(earlier && !workspace.IsLoadingMoreTurns);
            m_Earlier.text = workspace?.IsLoadingMoreTurns == true ? "Loading earlier turns…" : "Load earlier turns";
            RenderTurn();
        }

        /// <summary>Preserves the continuous reading position while the shared column changes tabs.</summary>
        public void SetVisible(bool visible)
        {
            if (visible == (style.display.value != DisplayStyle.None)) return;
            if (!visible) m_ReadingOffset = m_Content.scrollOffset;
            style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            if (visible)
            {
                m_RestoreScroll = true;
                schedule.Execute(RestoreReadingPosition);
            }
        }

        void ResetReadingPosition()
        {
            m_RestoreScroll = false;
            m_ReadingOffset = Vector2.zero;
            m_Content.scrollOffset = Vector2.zero;
        }

        void RestoreReadingPosition()
        {
            if (!m_RestoreScroll || style.display.value == DisplayStyle.None || m_Content.contentViewport.layout.height <= 0) return;
            m_RestoreScroll = false;
            m_Content.scrollOffset = m_ReadingOffset;
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
                else if (live && type == "turnDiff" && !string.IsNullOrEmpty(item["diff"]?.Value<string>()))
                    turn.Records.Add(new ChangeRecord
                    {
                        Key = itemId, Path = "Live turn diff", Operation = "live", Status = "live snapshot",
                        Diff = item["diff"]?.Value<string>(), Live = true,
                    });
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
            ChangeTurn turn = m_TurnId == null ? m_Turns.FirstOrDefault() : m_Turns.Find(candidate => candidate.Id == m_TurnId);
            if (m_RenderedTurnId != turn?.Id)
            {
                m_RenderedTurnId = turn?.Id;
                m_Records.Clear();
                m_Content.Clear();
                ResetReadingPosition();
            }
            m_Scope.text = m_TurnId == null ? "Latest turn ▾" : "Historical turn ▾";
            m_Scope.tooltip = turn == null ? "No recorded changes" : turn.Time + " · " + turn.Title + "\nChoose a turn to review";
            m_Scope.SetEnabled(turn != null);
            m_Files.SetEnabled(turn != null);
            m_Content.style.display = turn != null ? DisplayStyle.Flex : DisplayStyle.None;
            int added = 0, removed = 0;
            var records = turn?.Records ?? new List<ChangeRecord>();
            var keys = new HashSet<string>(records.Select(record => record.Key));
            foreach (string key in m_Records.Keys.Where(key => !keys.Contains(key)).ToList())
            {
                m_Records[key].RemoveFromHierarchy();
                m_Records.Remove(key);
            }
            var counts = records.Where(record => !record.Live).GroupBy(record => record.Path)
                .ToDictionary(group => group.Key, group => group.Count());
            var edits = new Dictionary<string, int>();
            for (int index = 0; index < records.Count; index++)
            {
                ChangeRecord record = records[index];
                if (!m_Records.TryGetValue(record.Key, out RecordView view))
                {
                    view = new RecordView(this, record);
                    m_Records.Add(record.Key, view);
                    m_Content.Insert(index, view);
                }
                else if (m_Content.IndexOf(view) != index) m_Content.Insert(index, view);
                edits.TryGetValue(record.Path, out int edit);
                edits[record.Path] = ++edit;
                view.Update(record, edit, record.Live ? 1 : counts[record.Path]);
                if (!record.Live)
                {
                    added += view.Patch.Additions;
                    removed += view.Patch.Deletions;
                }
            }
            m_Added.text = turn == null ? "" : "+" + added;
            m_Removed.text = turn == null ? "" : "−" + removed;
            m_Added.tooltip = m_Removed.tooltip = "Recorded edits; live aggregate excluded. Repeated edits are counted separately.";
            int fileCount = counts.Count;
            int editCount = records.Count(record => !record.Live);
            string summary = fileCount + (fileCount == 1 ? " file" : " files")
                + (editCount > fileCount ? " · " + editCount + " edits" : "");
            m_Title.text = turn == null ? "" : (turn.Live ? "LIVE · " : "") + summary + " · " + turn.Title;
            m_Title.tooltip = turn == null ? "" : turn.Title + "\n" + turn.Time;
            UpdateCollapseButton();
        }

        string FilePath(ChangeRecord record)
        {
            if (record.Live) return null;
            string file = CodexMarkdown.ResolveLocalPath(record.Path, Directory);
            return File.Exists(file) ? file : null;
        }

        void UpdateCollapseButton()
        {
            bool expanded = m_Records.Values.Any(view => view.Expanded);
            m_Collapse.SetEnabled(m_Records.Count > 0);
            m_Collapse.text = expanded ? "−" : "+";
            m_Collapse.tooltip = expanded ? "Collapse all files" : "Expand all files";
        }

        void ShowScopeMenu()
        {
            var items = new List<CodexAssistantWindow.PopupItem>
            {
                new CodexAssistantWindow.PopupItem { Label = "Latest turn · follow new changes", Selected = m_TurnId == null,
                    Action = () => { m_TurnId = null; RenderTurn(); } },
            };
            for (int index = 0; index < m_Turns.Count; index++)
            {
                ChangeTurn turn = m_Turns[index];
                items.Add(new CodexAssistantWindow.PopupItem
                {
                    Label = (index + 1) + ". " + turn.Title + " · " + turn.Time,
                    Selected = m_TurnId == turn.Id,
                    Action = () =>
                    {
                        if (!m_Turns.Exists(candidate => candidate.Id == turn.Id)) return;
                        m_TurnId = turn.Id;
                        RenderTurn();
                    },
                });
            }
            m_ShowMenu(m_Scope, "Change history", items);
        }

        void ShowFileMenu()
        {
            string turnId = m_RenderedTurnId;
            ChangeTurn turn = m_Turns.Find(candidate => candidate.Id == turnId);
            if (turn == null) return;
            var items = new List<CodexAssistantWindow.PopupItem>();
            for (int index = 0; index < turn.Records.Count; index++)
            {
                ChangeRecord record = turn.Records[index];
                items.Add(new CodexAssistantWindow.PopupItem
                {
                    Label = (index + 1) + ". " + record.FileName + " · " + record.Operation + " · " + record.DisplayPath,
                    Action = () =>
                    {
                        if (m_RenderedTurnId != turnId || !m_Records.TryGetValue(record.Key, out RecordView view)) return;
                        view.SetExpanded(true);
                        UpdateCollapseButton();
                        schedule.Execute(() =>
                        {
                            if (m_RenderedTurnId == turnId && view.parent != null) m_Content.ScrollTo(view);
                        });
                    },
                });
            }
            m_ShowMenu(m_Files, "Jump to file", items);
        }

        static void Ellipsis(Label label)
        {
            label.style.whiteSpace = WhiteSpace.NoWrap;
            label.style.overflow = Overflow.Hidden;
            label.style.textOverflow = TextOverflow.Ellipsis;
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
                _ => CodexPalette.Muted,
            };
        }
    }
}
