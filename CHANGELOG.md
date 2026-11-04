# Changelog

All notable changes to this package are documented here.

## [0.1.0] - Unreleased

### Added

- JSON-RPC over newline-delimited JSON transport for `codex app-server`, with the reader on a
  background thread and callbacks dispatched to the Unity main thread.
- Codex executable discovery, including Windows `.cmd` shims and macOS locations that a GUI-launched
  Editor does not inherit from the shell.
- `CodexClient` wrappers for `initialize`, `thread/list`, `thread/read`, `thread/resume`,
  `thread/turns/list`, `turn/start` and `turn/interrupt`.
- `Tools > Minerva > Codex > Diagnose App Server` and a batchmode smoke test that reads the shared
  Codex thread store.
- Read-only `Window > Minerva > Codex Assistant`: thread list, turn history, item rendering for user
  messages, agent messages, reasoning, commands, file changes, MCP calls, plans and reviews.
  `Copy resume` copies the CLI command for the selected thread.
