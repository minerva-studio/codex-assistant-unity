# Changelog

All notable changes to this package are documented here.

## [0.1.0] - Unreleased

### Added

- Right-side **Changes / Subagents** tabs with a shared resizable column. Changes lists loaded turns and
  individual file edits with their status, read-only patch detail, copy and history pagination. Repeated
  paths remain separate; live aggregate patches disappear on completion. Selection and reading position
  survive tab switches and background updates. Patches in Changes and the transcript use green/red text
  and row backgrounds for additions/deletions, keeping their signs and original copy text.
- Changes history uses collapsible turn summaries instead of timestamp/id dropdown navigation, with
  left-aligned filenames, secondary directories, compact operation markers and repeated-edit numbering.
  Its navigation height is bounded so patch detail retains most of the sidebar.

- JSON-RPC over newline-delimited JSON transport for `codex app-server`, with the reader on a
  background thread and callbacks dispatched to the Unity main thread.
- Persistent localhost WebSocket app-server per `CODEX_HOME`, with stdio fallback. Domain reloads and
  play-mode transitions reconnect the window without interrupting a running turn, and restore the
  selected thread.
- Intermediate agent replies join the collapsible Process group, short reasoning summaries are inline,
  long chains fold under **Full reasoning**, and `imageView` tool results render as images. Empty reasoning
  placeholders are ignored instead of creating an empty thought row.
- Steering and queuing while a turn runs: Enter persists a queued follow-up through
  `thread/queue/add`, Ctrl+Enter steers through `turn/steer`, and queued messages show above the composer.
- Reasoning-effort changes use `thread/settings/update` and later turns omit the per-turn effort override.
- The transcript follows the bottom by default and stops following when the reader scrolls up.
- New conversations generate a short title after the first turn using an ephemeral title turn; the title
  model is configurable in Settings.
- **Fork** copies the selected conversation into a new independent thread.
- Subagent activity and collaboration tool calls render as Subagent sections with Open buttons; subagent
  threads stay out of the sidebar and the header provides **← Parent** navigation.
- A minimal right-side Subagents rail shows agent status/model/activity and opens a row's conversation.
- Pulsing indicators mark the running turn in the toolbar/sidebar and running subagent rows; stale historical
  running states are shown as idle instead of animated.
- `request_user_input` questions render as an answer card above the composer and are sent back as a proper
  app-server response. Stored question/answer wrappers render as question/answer cards instead of raw JSON.
- Command, file-change and permission approval requests plus standard MCP form/URL elicitations render as
  interaction cards and send the protocol response instead of leaving the turn waiting. Legacy
  `execCommandApproval`/`applyPatchApproval` requests use the same cards and send `ReviewDecision` responses.
- P1 live updates: command/file output, MCP progress, plan steps, turn diff and resolved server requests.
  Conversation process output and observed stdin remain read-only; standalone session ids are no longer
  incorrectly sent to a conversational command's process id.
- Standalone **Terminal** window with a persistent PowerShell/bash PTY, direct shell syntax, working
  directory/variables, bottom command/stdin input, history, interrupt, restart, folder selection, copy and
  clear. Sandboxed `command/exec` and Local `process/spawn` use independent UTF-8 decoders for stdout/stderr.
  A bounded VT text buffer handles cursor/erase/progress and hidden prompt metadata. The window rebinds
  after Assistant reconnects and retains its last directory; domain reload creates a fresh shell.
- Visual refresh of the window: palette, toolbar, compact sidebar rows, card surfaces, composer, empty state
  and a stable leading active-thread indicator.
- Codex output is no longer wrapped in a bubble; user input keeps a compact right-aligned bubble.
- Collapsed Process and tool headers show elapsed time.
- Sidebar context menu with Open, Rename, Copy resume, Fork and Archive.
- Turn history is paginated: the transcript loads the newest 30 turns and a **Load earlier turns** control
  appends older pages through `nextCursor`, skipping repeats and keeping the reader on the same message.
- The sidebar scope selector has an **Archived** view backed by `thread/list { archived: true }`. Archived
  conversations open without resuming, so they are read-only until **Restore** (or **Restore and open** in
  the row menu) calls `thread/unarchive` and reopens the thread.
- Per-message footer with Select / Copy and local time; Select swaps only that message to a read-only
  selectable text field. Fork is shown only on a completed turn's final Codex message because
  `thread/fork` supports turn granularity, not mid-turn branching.
- Command, skill and file references live behind the circular **+** menu at the composer's bottom-left
  (typing `/`, `$`, `@` still opens them directly), serialized as Codex mention text; local file/skill links
  in messages open on click.
- Image attachments: file picker, drag/drop, Play Mode Game view capture, thumbnail removal, and `localImage`
  sending (including image-only messages).
- The composer's **Unity state** toggle prepends a send-time snapshot of the active scene or prefab stage,
  selection, compile state and recent console errors/warnings. Reading the console temporarily clears the
  Console toolbar/search filters so hidden severities still produce entry text, then restores them; slash
  commands are sent unchanged, and queued rows show the user's text without the snapshot. In the transcript
  the snapshot renders as a compact **Unity state · scene · errors** chip whose details expand on click;
  Select/Copy still use the full raw message. Thread previews, fallback titles and auto-title input strip the
  snapshot before truncation, so a new conversation is not named after its Editor state.
- Clearing the composer after a send now resets the text-editing caret indices too; typing the next message
  no longer throws `ArgumentOutOfRangeException` from `TextEditingUtilities.ReplaceSelection`.
- Transcript auto-follow now re-scrolls on content and viewport geometry changes instead of relying on one
  scheduled pass. Long messages and composer resizing no longer leave the transcript above the bottom, and
  programmatic scroll updates cannot turn follow off.
- Optional JSON numbers are read as nullable (`Value<long?>()`), so a live item whose `completedAtMs` is
  still null no longer throws `InvalidCastException` during rendering.
- The sidebar loads 50 threads and appends older pages with **Load more** (`thread/list` cursor); the
  archived view pages the same way, and searching stays unpaged.
- JSON responses keep date-like strings verbatim (`DateParseHandling.None`). Newtonsoft was parsing
  `thread/list` cursors into `DateTime` and re-formatting them in the current culture, which the server
  rejected as an invalid cursor.
- The sidebar header is two rows: the heading row carries the scope dropdown and a compact **+**
  new-conversation button, and the search field takes the full width below. Search and dropdown now draw a
  single custom shell instead of stacking the stock field chrome inside an outer box.
- On Windows the Chinese IME candidate window is placed under the composer through IMM32
  (`ImmSetCandidateWindow` + `ImmSetCompositionWindow`), because UI Toolkit in 6000.3 reports the caret in
  element-local coordinates and leaves the candidate window at the top-left of the editor window.
- The composer wraps long text and grows with it again: the multiline field now keeps
  `white-space: normal`, constrains its multiline container to the card width and carries the
  `unity-text-field--multiline` class, so long Chinese sentences no longer stay on one clipped line and the
  IME caret is computed inside the visible input.
- Composer caret after a reference insertion is now moved through `TextField.SelectRange` plus the
  text-editing utilities' unvalidated indices; the previous reflection path silently did nothing, leaving
  the keyboard/IME cursor at the old position and throwing on the next insert.
- Composer uses legacy Text Core and synchronizes `TextEditingUtilities` after insertions, fixing stale IME
  selection that could leave Chinese input candidate windows detached from the caret.
- Any non-image file can be dragged into the composer as a reference; image and reference chips can be dragged
  out to Project/Explorer. File and skill references also appear as removable chips below the input.
- Resizable conversation sidebar and subagent rail, with persisted widths.
- Code blocks preserve leading indentation and syntax colors; bare `$skill` tokens render as inline skill pills.
- Reference popups support Up/Down selection and Enter confirmation while the search field stays focused;
  inserted references leave the caret after the token instead of selecting it.
- Full-width `￥`/`¥`/`＄`, `＠` and `／` work as reference triggers during Chinese input; the first async
  skill load now also delivers its waiting popup.
- Archive now verifies the thread is actually gone after `thread/archive`, refreshes the thread list, and
  filters the archived id; if another Codex client still holds it, the window keeps it and explains why.
- Codex file/image attachment annotations are stripped to the actual request, and data-URI image
  attachments are decoded instead of printed as base64.
- Historical Process groups render their hidden tool rows lazily, keeping long-thread rebuilds responsive.
- Reasoning summaries are requested with `summary: "auto"` and live summary deltas are merged into history
  when the model provides plaintext summaries.
- Codex executable discovery, including Windows `.cmd` shims and macOS locations that a GUI-launched
  Editor does not inherit from the shell.
- `CodexClient` wrappers for `initialize`, `thread/list`, `thread/read`, `thread/resume`,
  `thread/turns/list`, `turn/start` and `turn/interrupt`.
- `Tools > Minerva > Codex > Diagnose App Server` and a batchmode smoke test that reads the shared
  Codex thread store.
- Read-only `Window > Minerva > Codex Assistant`: thread list, turn history, item rendering for user
  messages, agent messages, reasoning, commands, file changes, MCP calls, plans and reviews.
  `Copy resume` copies the CLI command for the selected thread.
