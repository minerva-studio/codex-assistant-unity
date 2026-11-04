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
- Subagent support: `subAgent*` threads appear in the sidebar; `collabAgentToolCall` and `subAgentActivity`
  render as Subagent sections with Open buttons.
- Collapsible Process groups around intermediate replies, reasoning and tool records; live groups stay
  expanded, history collapses. Short reasoning stays inline, long chains fold under **Full reasoning**;
  settings can hide reasoning or tools, and tool screenshots render as images.
- Optional context-window indicator derived from token-usage notifications.
- Persistent localhost WebSocket app-server per CODEX_HOME; domain reload and play mode only reconnect the
  window, keep a running turn alive and restore the selected thread. Direct stdio remains the fallback.
- Settings panel and configurable CODEX_HOME; changing it restarts the listener and refreshes the model
  catalog. **Reconnect** reloads config/providers/models without changing the home.

## Next

- Approvals: command execution and file change requests, with accept/decline and session scope.
- Thread management: rename, paginated history, unarchive.

## Later

- Unity context attachments: selection, scene snapshot, console, compile errors.
- Unity tools: scene and asset operations with Undo, menu items, play mode control.
- Detailed token usage and cost display.

## Explicitly out of scope for now

- **Mermaid rendering.** Diagrams are shown as a code block. A real renderer needs a JavaScript
  engine or a layout engine, plus a rendering surface; that is a project on its own.
- **Volume control.** The editor preview API has no volume parameter, and direct video audio plays
  at full volume. Sample scaling was tried and rejected; per-drag rescaling stutters.
- **Character-level media.** Media nodes are block level; a reference inside a sentence splits the
  paragraph into text / media / text.
