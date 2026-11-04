# Codex Assistant

Editor client for the Codex app-server protocol.

The package owns no conversation state. Codex stores threads under `CODEX_HOME`, and this package
reads and writes them through `codex app-server`, which is why the same thread can be resumed from
the CLI.

## Entry points

| API | Purpose |
| --- | --- |
| `CodexPaths.Resolve()` | Finds the Codex executable and the arguments to launch. |
| `CodexAppServerProcess.Start(...)` | Starts `codex app-server` with piped stdio. |
| `CodexConnection` | JSON-RPC framing, request/response correlation, notification routing. |
| `CodexClient` | The protocol calls the assistant uses. |

All callbacks arrive on the Unity main thread through `EditorDispatcher`.

## Diagnostics

`Tools > Minerva > Codex > Diagnose App Server` runs the same path as the window without any UI.
