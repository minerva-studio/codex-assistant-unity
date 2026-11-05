# Roadmap

## Done

- `codex app-server` transport (JSONL JSON-RPC), process discovery and supervision.
- Thread list, thread read, turn history, shared state with the Codex CLI and Desktop.
- Markdown rendering: headings, lists, quotes, links, tables, fenced code with syntax highlighting.
- Inline media nodes: images (thumbnails), audio (play/pause, seek, elapsed), video (play/pause,
  seek, audio) placed where the message references them.
- `Copy resume` for the selected thread.
- Composer: send, start a thread on first send, stream assistant deltas, stop a running turn, Enter to send
  or queue, Ctrl+Enter to steer, Shift+Enter for a newline. Queued follow-ups show above the composer and
  auto-dispatch when the turn ends.
- Model, reasoning effort and sandbox selectors, seeded from `config/read`, synced from `thread/resume`, and
  sent on `thread/start` / `turn/start`. Effort changes use `thread/settings/update` to keep the cache path
  stable; the transcript follows the bottom unless the reader scrolls up.
- Thread navigation: content search, current-project/all-projects scope, titles, archive, fork, and a new
  conversation action. New conversations get a generated title after the first turn, using an optional
  dedicated title model.
- Subagent support: `subAgent*` threads are opened from their parent (not listed in the sidebar);
  `collabAgentToolCall` and `subAgentActivity` render as Subagent sections with Open and Parent navigation.
  A minimal right rail shows agent status/model/activity, with pulsing dots for running work.
- `request_user_input` questions with option/free-text answers, plus question/answer cards for stored
  `send_user_message_question_reply` messages.
- Command, file-change and permission approvals with allow-once/session/deny, plus standard MCP form/URL
  elicitations in the same interaction card.
- P1 live updates: command/file output, MCP progress, plan steps, turn diff, terminal input for PTY commands
  and `serverRequest/resolved` cleanup.
- Modern visual pass: palette/surface hierarchy, compact toolbar/sidebar, card-based Process and interaction
  surfaces, roomier composer, empty state and stable active-thread indicator. Codex text is unbubbled; user
  input keeps a compact right-aligned bubble. Process/tool folds show elapsed time. Sidebar context menu
  handles rename/archive/copy/fork; each message has a footer with local time, copy and a per-message
  read-only selection field. Fork is limited to the final Codex message of a completed turn because the
  app-server forks by turn, not by item. The composer has `/`, `$` and `@` reference pickers for commands,
  skills and files, with clickable local links in the transcript, plus image attachments from files,
  drag/drop or a Play Mode Game view capture. Sidebar and subagent columns are resizable with persisted
  widths; code blocks preserve indentation and raw `$skill` tokens render as inline pills. Any file can be
  dropped in as a reference, file/skill chips sit below the composer, and images/reference chips drag out.
- Collapsible Process groups around intermediate replies, reasoning and tool records; live groups stay
  expanded, history collapses. Short reasoning stays inline, long chains fold under **Full reasoning**;
  settings can hide reasoning or tools, and tool screenshots render as images.
- Optional context-window indicator derived from token-usage notifications.
- Persistent localhost WebSocket app-server per CODEX_HOME; domain reload and play mode only reconnect the
  window, keep a running turn alive and restore the selected thread. Direct stdio remains the fallback.
- Settings panel and configurable CODEX_HOME; changing it restarts the listener and refreshes the model
  catalog. **Reconnect** reloads config/providers/models without changing the home.
- Turn pagination (**Load earlier turns**) and an **Archived** sidebar view with restore; the sidebar header
  is two rows (scope dropdown + `+` new conversation, search below).
- Thread-list pagination: the sidebar loads 50 threads and appends older pages with **Load more**.
- Composer wrapping, caret sync after a reference insertion, and Windows IME candidate-window placement via
  IMM32 (the workaround compiles out from 6000.6 on).
- Unity context attachments: a composer toggle prepends the active scene/prefab, selection, compile state and
  the latest console errors/warnings to each normal message, captured at send time.

## Next

- Legacy `execCommandApproval`/`applyPatchApproval`, MCP device verification and non-standard OpenAI
  elicitation forms.
- Standalone `command/exec` / `process/spawn` consoles and persisted plan/diff history panels.
- Verify composer IME placement on 6000.6+ (the workaround compiles out there) and on macOS.

## Later

- Unity tools: scene and asset operations with Undo, menu items, play mode control.
- Detailed token usage and cost display.

## Explicitly out of scope for now

- **Mermaid rendering.** Diagrams are shown as a code block. A real renderer needs a JavaScript
  engine or a layout engine, plus a rendering surface; that is a project on its own.
- **Volume control.** The editor preview API has no volume parameter, and direct video audio plays
  at full volume. Sample scaling was tried and rejected; per-drag rescaling stutters.
- **Character-level media.** Media nodes are block level; a reference inside a sentence splits the
  paragraph into text / media / text.
