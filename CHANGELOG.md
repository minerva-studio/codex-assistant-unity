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
  long chains fold under **Full reasoning**, and `imageView` tool results render as images.
- Steering and queuing while a turn runs: Enter persists a queued follow-up through
  `thread/queue/add`, Ctrl+Enter steers through `turn/steer`, and queued messages show above the composer.
- Reasoning-effort changes use `thread/settings/update` and later turns omit the per-turn effort override.
- The transcript follows the bottom by default and stops following when the reader scrolls up.
- Codex executable discovery, including Windows `.cmd` shims and macOS locations that a GUI-launched
  Editor does not inherit from the shell.
- `CodexClient` wrappers for `initialize`, `thread/list`, `thread/read`, `thread/resume`,
  `thread/turns/list`, `turn/start` and `turn/interrupt`.
- `Tools > Minerva > Codex > Diagnose App Server` and a batchmode smoke test that reads the shared
  Codex thread store.
- Read-only `Window > Minerva > Codex Assistant`: thread list, turn history, item rendering for user
  messages, agent messages, reasoning, commands, file changes, MCP calls, plans and reviews.
  `Copy resume` copies the CLI command for the selected thread.
