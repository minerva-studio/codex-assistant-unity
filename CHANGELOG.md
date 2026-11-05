# Changelog

All notable changes to this package are documented here.

## [0.1.0] - Unreleased

### Added

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
  interaction cards and send the protocol response instead of leaving the turn waiting.
- P1 live updates: command/file output, MCP progress, plan steps, turn diff and resolved server requests;
  running PTY commands accept terminal input.
- Visual refresh of the window: palette, toolbar, compact sidebar rows, card surfaces, composer, empty state
  and a stable leading active-thread indicator.
- Codex output is no longer wrapped in a bubble; user input keeps a compact right-aligned bubble.
- Collapsed Process and tool headers show elapsed time.
- Sidebar context menu with Open, Rename, Copy resume, Fork and Archive.
- Hover message actions: Copy and Fork from here, with fork using the message's turn as `lastTurnId`.
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
