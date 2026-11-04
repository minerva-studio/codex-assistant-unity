# Handoff

State of the Codex Assistant Unity package, so work can continue without the previous conversation.

## Product

A Unity Editor window that drives `codex app-server` directly and renders conversations the way Codex writes
them. Threads are shared with the Codex CLI and Codex Desktop through the same `CODEX_HOME`; the package stores
no conversation state of its own.

## Repositories

| Repo | Local path | Notes |
| --- | --- | --- |
| https://github.com/minerva-studio/codex-assistant-unity | `C:\Users\wende\Documents\minerva-game-studio\codex-assistant-unity` | The package, `main` (composer, navigation, settings, streaming) |
| https://github.com/minerva-studio/umarkdown | `C:\Users\wende\Documents\minerva-game-studio\umarkdown` | MIT fork of helightdev/UMarkdown, `main` `de286d8` |

Package name `com.minervagamestudio.codex.assistant`, Unity `6000.3`.
Dependencies: `com.unity.nuget.newtonsoft-json 3.2.1`, and UMarkdown pinned to
`https://github.com/minerva-studio/umarkdown.git?path=src/UMarkdown/Assets/Plugins/UMarkdown#171fafe43912c4d7d903b7c853424c1aa42ebe19`.

Test projects: `markdown-proto-2` (GUI, has Pipeline installed, port 7800), `markdown-proto`, `install-test`.

## What works

- Transport: JSONL JSON-RPC over stdio, background reader, callbacks marshalled to the main thread.
- `initialize`, `thread/list`, `thread/search`, `thread/read`, `thread/resume`, `thread/archive`,
  `thread/unsubscribe`, `thread/turns/list`, `turn/start`, `turn/interrupt`, `config/read`, `model/list`
  wrappers exist in `CodexClient`.
- Window lists non-archived threads Codex knows about (shared store), shows titles, history and `Copy resume`.
- Sidebar search (`thread/search`), project/all scope filter (`cwd` on `thread/list`, client-side filtering on
  search results), archive for the selected thread, and a **New conversation** button that clears the
  selection without creating a zero-turn thread.
- History uses `thread/turns/list` with `itemsView: "full"`, so tool usage and reasoning items are present.
  They render as collapsed `Foldout` sections; the settings panel can hide reasoning or tool records.
- Composer sends messages: no selection starts a thread, otherwise starts a turn. Enter sends, Shift+Enter
  inserts a newline, one button switches Send/Stop, and the input is disabled while running.
- `item/agentMessage/delta` streams into a live turn rendered below history. `item/started`,
  `item/completed` and `turn/started` keep the live turn current; `turn/completed` reloads
  `thread/turns/list` and clears the live turn, so history wins.
- Model, reasoning effort and sandbox pills below the input. Values start from effective `config/read`, then
  `thread/resume { excludeTurns: true }` syncs the selected thread's `model`, `reasoningEffort`, `sandbox`
  and `approvalPolicy`. `thread/start` gets model/sandbox/approval; `turn/start` gets model/effort/
  `sandboxPolicy`/approval.
- `Tools/Minerva/Codex/Send Smoke Test` exercises a lazy thread/start, a second turn, live text, and history
  reconciliation with real turns.
- Markdown rendering through the UMarkdown fork: headings, lists, quotes, links, tables, code blocks with
  syntax highlighting.
- Inline media nodes at the position the text references them:
  - images: thumbnails,
  - audio: play/pause, seek, elapsed time,
  - video: play/pause, seek, sound.
- Missing media files keep their path as text instead of rendering a dead control.
- UPM resolves the git dependency; verified by a clean `package resolve` and compile.

## Decisions already made

- Media are **block-level** nodes: a reference inside a sentence splits the paragraph into text / media / text.
- **No volume control**: the editor preview API has no volume parameter; per-drag sample rescaling stutters.
- **No mermaid rendering**: diagrams show as a code block. Recorded as out of scope in `ROADMAP.md`.
- Video sound needs all three: `VideoAudioOutputMode.Direct`, `SetDirectAudioVolume(0, 1f)`, a dummy
  `AudioSource.Play()` to initialize the editor audio subsystem, and temporarily clearing
  `EditorUtility.audioMasterMute` while playing.
- Non-WAV audio is loaded through `UnityWebRequestMultimedia` and played through `AudioUtil.PlayPreviewClip`;
  this is the cross-platform path (Windows and macOS).
- Streaming keeps the live turn separate from `m_Turns`; `LiveTurn` is only exposed when its thread is
  selected. Deltas mutate the live item and fire a throttled `TranscriptChanged` (50 ms) that re-renders only
  the live container; structural changes still rebuild the whole transcript. On `turn/completed` the live
  turn is kept until the history reload replaces it, and a turn generation prevents a stale reload from
  clearing a newer live turn.
- The composer is a single rounded card with the pills inside it. One action button, not separate Send and
  Stop buttons. Sandbox sits on the left; model, effort and the action sit on the right.
- The model/effort/sandbox popup is custom UI Toolkit (rounded panel, search for models, vertical-only
  scrolling) because `GenericDropdownMenu` opened a clipped list with a horizontal scrollbar near the window
  bottom.
- Settings are effective values, not hard-coded defaults. `SelectedSandbox` may be null, which means "let
  the server choose"; the selector shows **Server default** and `turn/start` omits `sandboxPolicy`.
  `workspace-write` maps to `{ type: "workspaceWrite", writableRoots: [projectRoot] }`.
- `thread/search` is gated behind the experimental API; `initialize` declares
  `capabilities.experimentalApi = true`. Search results are full-text with snippets, filtered to the project
  cwd client-side when the scope is **This project**.
- Archived threads are never listed (`archived: false`). Archiving the selected thread first calls
  `thread/unsubscribe`, because a loaded thread's archive does not persist.
- The sidebar never steals the open conversation on a search/scope refresh. `SelectedThread` falls back to a
  cached thread entry when the selected thread is filtered out, so the header keeps its title.
- Settings live in `EditorPrefs` under `Minerva.Codex.*`: context indicator (default **off**), show reasoning
  (default on), show tool calls (default on), and CODEX_HOME (empty = process default). CODEX_HOME is set as
  an environment variable on the app-server process; changing it disposes and reconnects the workspace.
- Context usage comes from `thread/tokenUsage/updated` notifications. The indicator uses
  `last.totalTokens` / `modelContextWindow` (config `model_context_window` as fallback) and stays hidden until
  the setting is enabled.

## Next task: approvals, thread management, reconnect

1. Approvals: app-server sends `ServerRequest` for command execution / file change; render accept/decline
   with session scope. `CodexConnection.ServerRequest` already exists but nothing subscribes.
2. Thread management: rename, paginated history, unarchive view.
3. Domain reload: reconnect to the app-server, restore the selected thread, and resume the turn view.
4. Optional: refresh the sidebar thread entry automatically after the first turn so its preview/updatedAt
   match the store, without triggering the auto-select path in `OnThreadsLoaded`.

## Workflow that works on this machine

Unity CLI + Pipeline package are installed. Use them instead of killing the editor:

```
unity pipeline editors
unity command --project-path <proj> console status
unity command --project-path <proj> console get --level error --tail 5
unity command --project-path <proj> script eval "<C# statements>"
unity command --project-path <proj> package resolve
```

- `script eval` takes the code as a positional argument; `--code` fails with "Give either code or file".
- Changing files in a `file:` package does not always trigger a recompile; call
  `AssetDatabase.Refresh()` + `CompilationPipeline.RequestScriptCompilation()` through `script eval`.
- A domain reload drops the Pipeline connection for a few seconds; retry.
- `-batchmode` fails while a GUI editor holds the same project. Close with `WM_CLOSE`, not `Stop-Process`.

## Gotchas

- Inside `namespace Minerva.Codex.*`, `typeof(Editor)` resolves to the namespace `Minerva.Codex.Editor`.
  Use `typeof(UnityEditor.Editor)`.
- Package classes are `internal`; `script eval` can only touch public types directly.
- `thread/list` defaults to interactive sources; pass `sourceKinds` explicitly when listing.
- `config/read` returns the effective config only with `cwd` set; use the project root. `model/list` needs
  `includeHidden: true` or the effective model can be missing from the catalog.
- `thread/resume` with `excludeTurns: true` is the cheap way to sync a thread's effective settings and make
  `turn/start` safe on threads this process did not create. It returns `model`, `reasoningEffort`, `sandbox`
  (a policy object, not the mode string) and `approvalPolicy` at the top level.
- `thread/start` takes `model`, `sandbox` (mode string) and `approvalPolicy`; it has no effort parameter.
  `turn/start` takes `model`, `effort`, `sandboxPolicy` (object) and `approvalPolicy`.
- The blocking smoke test cannot run `EditorApplication.update`, so throttled `TranscriptChanged` events do
  not fire there. Observe `LiveTurn` from `Changed` (which fires at completion before the reload) instead.
- `thread/turns/list` with `itemsView: "summary"` returns only user/agent messages. Use `"full"` for tool and
  reasoning records.
- `thread/search` fails with code -32600 unless initialize declared `capabilities.experimentalApi = true`.
- Archiving a loaded thread with `thread/archive` returns success but the thread reappears in `thread/list`.
  Call `thread/unsubscribe` first, then archive. The same lock makes `codex archive <id>` fail for a thread
  loaded by another app-server.
- Codex 0.166 rejects `wire_api = "chat"` ("no longer supported"); only `responses` is accepted. DeepSeek's
  native API is Chat Completions, so a Responses-to-Chat bridge is required unless the DeepSeek route exposes
  `/responses`. A dedicated `~/.codex-ds` home with a `deepseek` provider and `deepseek.config.toml` profile
  was created on this machine; the API key is intentionally left to the user as `DEEPSEEK_API_KEY`.
- CODEX_HOME is inherited by app-server when the process starts. Setting an API key with `setx` after Unity is
  already running requires a Unity restart before a newly started app-server sees it.
- Codex Desktop groups threads by project by default; switch to chronological order to see CLI/Unity threads.
- GitHub pushes occasionally return HTTP 500; retry. The sandbox also blocks the Windows credential helper,
  so pushing needs a wider sandbox mode.

## Commit convention

`git -C <repo> commit -m "..."` as `Caisusandy <164040662@qq.com>`. Push to `main`.
