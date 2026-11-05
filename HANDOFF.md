# Handoff

State of the Codex Assistant Unity package, so work can continue without the previous conversation.

## Product

A Unity Editor window that drives `codex app-server` through a persistent localhost WebSocket listener and
renders conversations the way Codex writes them. Threads are shared with the Codex CLI and Codex Desktop
through the same `CODEX_HOME`; the package stores no conversation state of its own.

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

- Transport: `CodexWebSocketHost` starts `codex app-server --listen ws://127.0.0.1:<port>` for each
  CODEX_HOME and stores the port/pid in `EditorPrefs` (`Minerva.Codex.WebSocket.<homeHash>.Port`/`.Pid`).
  `CodexConnection` reads/writes JSON-RPC over the WebSocket on a background thread and marshals callbacks to
  the main thread. A domain reload closes the client socket but not the listener; `OnEnable` reconnects to the
  same port, so a running turn continues and the selected thread is restored. `Connect(..., restartServer:
  true)` (Reconnect / home change) kills and restarts the listener to reload config. If the listener cannot
  start, `Connect` falls back to a direct stdio `codex app-server`; that path still loses a turn on reload.
  The listener is stopped on `EditorApplication.quitting`; a crashed editor can leave it for the next connect.
- `initialize`, `thread/list`, `thread/search`, `thread/read`, `thread/resume`, `thread/fork`,
  `thread/archive`, `thread/unsubscribe`, `thread/name/set`, `thread/turns/list`, `thread/queue/*`,
  `turn/start`, `turn/steer`, `turn/interrupt`, `config/read`, `model/list` wrappers exist in `CodexClient`.
- Window lists non-archived threads Codex knows about (shared store), shows titles, history and `Copy resume`.
- Sidebar search (`thread/search`), project/all scope filter (`cwd` on `thread/list`, client-side filtering on
  search results), archive for the selected thread, and a **New conversation** button that clears the
  selection without creating a zero-turn thread.
- History uses `thread/turns/list` with `itemsView: "full"`, so tool usage, reasoning and `imageView` items
  are present. Each stretch of process items is wrapped in one collapsed `Process · N tools · M thoughts ·
  K replies` Foldout. Intermediate agent messages are detected by `phase` (`commentary` is always process;
  `final_answer` is always visible; missing/`partial_answer` is visible only when it is the last agent
  message in the turn). Final answers stay as normal bubbles. Expanding Process shows short reasoning
  summaries inline; a chain longer than 280 characters or 4 lines folds under a collapsed **Full reasoning**
  section inside the same bordered inset block. Empty reasoning placeholders (`summary` and `content` both
  empty, as some providers emit) are skipped and are not counted as thoughts. `imageView` paths render
  through `CodexMedia.RenderImage`. The settings panel can hide reasoning or tool records.
- Composer sends messages: no selection starts a thread, otherwise starts a turn. Enter sends while idle,
  Shift+Enter inserts a newline, one button switches Send/Stop. While running the input stays enabled:
  Enter queues through `thread/queue/add`, Ctrl+Enter steers through `turn/steer`. The queued strip above
  the composer lists persisted queue rows and can delete them. 0.166 dispatches queued rows itself once the
  thread is idle; do **not** call `thread/queue/start` manually. A queued `turn/started` arrives while
  `IsRunning` is false; `OnTurnStarted` adopts it and locks the composer again.
- `item/agentMessage/delta` streams into a live turn rendered below history. `item/started`,
  `item/completed` and `turn/started` keep the live turn current; `turn/completed` reloads
  `thread/turns/list` and clears the live turn, so history wins.
- `turn/start` sends `summary: "auto"`. `item/reasoning/summaryPartAdded` and
  `item/reasoning/summaryTextDelta` append to the live reasoning item; summaries are cached per
  thread/item and merged into `item/completed` and history reloads. Important observed limit: the local
  `gpt-6.1-sol` rollouts store reasoning as `encrypted_content` with `summary: []`, even with `auto` or
  `detailed`; those turns have no plaintext summary and cannot be backfilled. The code will display
  summaries when a model/backend actually emits them.
- Questions: `m_Connection.ServerRequest` handles `item/tool/requestUserInput`. `CodexWorkspace` stores the
  request id, thread/turn/item ids, questions, options, `isOther`/`isSecret`, and one answer per question.
  `CodexAssistantWindow` shows the first unanswered question above the composer; option buttons or the free
  text field call `AnswerQuestion`. After the last answer it responds with
  `{ answers: { questionId: { answers: [text] } } }` on the original server-request id. The composer is
  disabled while a question is pending. Pending requests are dropped on `turn/completed`, disconnect, send
  failure and shutdown.
- Approvals: `OnServerRequest` also handles `item/commandExecution/requestApproval`,
  `item/fileChange/requestApproval`, `item/permissions/requestApproval` and
  `mcpServer/elicitation/request`. `CodexApprovalRequest` stores the request id, kind, thread/turn/item ids,
  command/cwd/reason, available decisions, permission profile, file paths (looked up from the matching
  `fileChange` item), or MCP schema details. `CodexAssistantWindow` shows one interaction at a time:
  question first, then approval. Command/file responses are `{decision: accept|acceptForSession|decline|cancel}`;
  permissions grant `{permissions: requestedProfile, scope: turn|session}` and deny `{permissions: {}}`;
  MCP responses are `{action: accept|decline|cancel, content?}`.
  The composer is disabled while any question or approval is pending. Pending approvals are cleared on
  `turn/completed`, disconnect, send failure and shutdown.
- MCP elicitation forms support the standard `form` schema (string/number/boolean/enum/array fields, required
  fields) and `url` mode. `openai/userVerification` is shown but can only be declined/cancelled from the
  window; non-standard OpenAI form schemas fall back to a decline card. Legacy approval methods are not
  registered because `turn/start` uses the newer request methods.
- P1 live handlers in `OnNotification`:
  `item/commandExecution/outputDelta` appends `aggregatedOutput`;
  `item/commandExecution/terminalInteraction` records `terminalInput`;
  `item/fileChange/outputDelta` and `patchUpdated` update the file item;
  `item/mcpToolCall/progress` records the latest progress;
  `item/plan/delta` appends plan text;
  `turn/plan/updated` creates the live `turnPlan` item;
  `turn/diff/updated` creates the live `turnDiff` item;
  `command/exec/outputDelta`/`process/outputDelta` decode base64 and match a live command by process id;
  `process/exited` records the exit code. All go through `MarkTranscriptChanged`, so the existing 50 ms
  live throttle refreshes the Process group.
- The terminal input row is rendered by `RenderCommand` only when a running command has a `processId` and a
  `sendTerminalInput` callback was supplied. The window calls `CodexClient.WriteCommandInput`, which sends
  `command/exec/write { processId, deltaBase64 }`. This is for turn-level PTY commands, not a general
  `process/spawn` console.
- `serverRequest/resolved` removes the matching question/approval by request id; this handles another client
  resolving the prompt and avoids a stale interaction card.
- UI pass: `CodexPalette` now carries the dark/light surface hierarchy and accent colors. The toolbar uses a
  brand row with ghost buttons; the sidebar uses compact one-line rows with a reserved leading indicator slot;
  Process/interaction cards share the raised surface; the composer has a 14 px radius; empty conversations
  get a centered brand state. The active-thread pulse dot is always in the reserved slot, not after the
  flexible title.
- Codex `agentMessage` output renders as a plain text block with a small CODEX caption; only `userMessage`
  uses a bubble. The user bubble is right-aligned, has a max width of 68%, no role caption, compact 9 px
  vertical padding and the neutral `UserBubble` surface. Process/approval/question content remains carded
  because it is an interaction surface.
- Elapsed time: `CodexItemRenderer.FormatDuration` formats milliseconds as `ms`/`s`/`m s`/`h m`.
  `AppendDuration` adds it to command/file/MCP foldout titles; live running items use `UtcNow`.
  `ProcessDuration` takes the minimum item `startedAtMs` and maximum `completedAtMs` in a Process group; live
  groups use now. `ProcessSummary` appends it, so collapsed history and live groups both show elapsed time.
- Sidebar context menu: right-click calls `ShowThreadContextMenu(thread, mousePosition)`, which uses the
  `ShowPopup(..., Vector2? at)` overload. Actions are Open, Rename, Copy resume, Fork and Archive. Rename opens
  `CodexRenameWindow`, which calls `CodexWorkspace.RenameThread` -> `thread/name/set`. Archive uses
  `ArchiveThreadById`; selected-thread archive keeps the confirmation dialog. `ShowPopup` clamps mouse-
  anchored menus to the window bounds.
- Per-message footer: `CodexItemRenderer.Render` takes `copyMessage`, `forkFromTurn`, `turnId`, the turn
  timestamp and `canFork`. User wrappers place the footer under the right-aligned bubble; Codex blocks place
  it under the plain text. The footer is always visible with Select / Copy / local `h:mm tt`; Fork is only
  rendered when `canFork` is true. `RenderTurn` passes `canFork: true` only for a completed turn's final
  Codex message; user messages, live turns and intermediate Process messages pass false. Fork calls
  `CodexWorkspace.ForkThread(source, lastTurnId)` and the app-server `thread/fork` request sets
  `lastTurnId`, which forks through that turn inclusively. Forking is refused while a turn is running.
  Do not show Fork on an intermediate message: the API has no message-level fork and it would silently
  branch after the whole turn.
- Per-message selection: UI Toolkit `Label` exposes `ITextSelection` but reports `isSelectable == false` even
  with `focusable = true`, so the footer's Select button reveals a read-only multiline `TextField` inside
  that one message. It copies the message's raw text and keeps the field selection local to that message;
  Done restores the rich Markdown content. There is no global plain-text transcript mode.
- `BuildAgentsPanel` resets `m_AgentsSignature`; otherwise a UI rebuild can reuse the old signature while
  the new `m_AgentsList` is empty, leaving the rail blank.
- Historical question replies are user messages wrapped as
  `<send_user_message_question_reply>[{ "question": "...", "answer": "..." }]</...>`.
  `CodexQuestionReply.TryParse` extracts every entry and renders a **CODEX ASKED / YOU ANSWERED** card; the
  raw wrapper and JSON are not shown.
- Historical Process groups are lazy: `FlushProcess` stores their items and renders them only on first
  expansion; live groups still render immediately. This dropped a 30-turn/71-tool thread from 1,864
  foldouts and ~1.2 s rebuild to ~35 foldouts and ~0.4 s. Do not eagerly build Process children again.
- Codex annotations are stripped by `CodexAnnotations.Clean`. `JoinUserContent` turns an image content
  item into `[Attached image ... data:image/...]`; `CodexMedia.Tokenize` must detect a data URI in that
  attachment reference and decode it, never pass the data URI as a file path (the old fallback printed the
  whole base64 payload).
- Model, reasoning effort and sandbox pills below the input. Values start from effective `config/read`, then
  `thread/resume { excludeTurns: true }` syncs the selected thread's `model`, `reasoningEffort`, `sandbox`
  and `approvalPolicy`. `thread/start` gets model/sandbox/approval. Effort changes call
  `thread/settings/update { threadId, effort }` and mark the thread synced on success; `turn/start` then
  omits `effort` (falling back to the per-turn override if the API fails). Other turn params still pass
  model/`sandboxPolicy`/approval.
- The transcript follows the bottom while `m_FollowTranscript` is true. It is reset on a thread change or
  send, and cleared when the user scrolls away from the bottom; streaming updates then preserve their
  position instead of yanking them down.
- Auto-title: `OnThreadStarted` marks a user-created thread. Once its first turn is hydrated and idle,
  `MaybeStartAutoTitle` creates a read-only **ephemeral** thread with `AutoTitleModel` (or the conversation
  model) and a title prompt, collects the final agent message, sanitizes it to <=64 chars, and calls
  `thread/name/set`. The server then emits `thread/name/updated`. Failures only log; a 45 s timeout clears
  the state. One title generation runs at a time.
- **Fork** calls `thread/fork { threadId, excludeTurns: true }`, inserts the returned thread at the top of the
  sidebar, selects it, and lets the normal resume/turns path hydrate history. A fork is not auto-titled.
- Subagents are **not sidebar conversations**. `SourceKinds` excludes every `subAgent*` kind, and
  `OnThreadsLoaded` also drops any thread with a non-empty `ParentThreadId` from both list and search
  results. `collabAgentToolCall` renders a `Subagent · spawnAgent/sendInput/...` foldout with status, prompt,
  model/effort, agent states and receiver IDs. `subAgentActivity` renders a compact started/completed row.
  Both expose **Open** buttons that call `SelectThread` on the receiver/agent thread. The `multi_agent`
  feature is stable and enabled by default; whether a model spawns helpers depends on the model/effort, not
  on this UI. `CodexThreadInfo.ParentThreadId` comes from the thread's `parentThreadId`; the header shows
  **← Parent** for those threads and calls `SelectThread(parent)`.
- The minimal right rail is driven by `CodexWorkspace.Subagents`, computed from `collabAgentToolCall`
  (`agentsStates`, receiver IDs, prompt, model/effort) and `subAgentActivity` (`agentPath`, kind) across the
  loaded turns plus the live turn. `CodexAssistantWindow` renders it as 236 px `m_AgentsPanel`; the toolbar
  **Agents N** button toggles it, and rows call `SelectThread` on the child. Only agents observed in the live
  turn while `IsRunning` keep a running/pending status; historical stale running/pending states are rewritten
  to **idle** (`last: …` activity) and get a static muted dot. Completed/failed rows are static; live running
  rows use `CodexPulseDot`.
- Archive: after `thread/archive` succeeds, `VerifyArchive` requests `thread/list` for the thread's cwd and
  only then records `m_ArchivedThreadIds`, clears the selection, invalidates the list generation, and
  reloads. If the thread is still listed, the UI keeps it and shows an actionable error: close it in Codex
  Desktop/CLI and retry, because another app-server still holds it. Both list and search results skip
  successfully archived ids, so an in-flight stale list cannot resurrect the conversation.
- `CodexPulseDot` is the shared animation primitive: a scheduled opacity pulse. It is used by the toolbar
  working dot, the selected running thread row, and running agent rows. The live composer/turn still shows
  the normal status text.
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
  Stop buttons. Sandbox sits on the left; context, model, effort and the action sit on the right.
- Process activity is grouped by message boundary, not shown as a flat list of tool rows. The group is
  expanded while rendering the live turn (`RenderTurn(..., isLive: true)`) and collapsed for history;
  counts subtract items hidden by the reasoning/tool settings.
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
  (default on), show tool calls (default on), auto title (default **on**), title model (empty = conversation
  model), and CODEX_HOME (empty = process default). CODEX_HOME is set as an environment variable when the
  listener starts; changing it starts that home's listener and reconnects.
- `CodexPreferences.ReloadRequested` is the explicit backend reload path: it disposes/restarts the listener
  without changing the home, clearing models and reloading `config/read` + `model/list`. Settings exposes it
  as **Reconnect**; applying an unchanged CODEX_HOME also calls it so edits to `config.toml`/`models.json`
  are picked up.
- `OnTurnsLoaded` treats the newest `inProgress` turn as the live turn (and removes it from history), so a
  reconnect during a running turn restores Stop/live rendering. The WebSocket path was tested: a forced
  `EditorUtility.RequestScriptReload()` kept the same listener PID, restored the selected thread, and the
  running turn completed as the next turn instead of being interrupted.
- Context usage comes from `thread/tokenUsage/updated` notifications. The indicator uses
  `last.totalTokens` / `modelContextWindow` (config `model_context_window` as fallback) and stays hidden until
  the setting is enabled.

## Next task: approvals and thread management

1. Approvals: app-server sends `ServerRequest` for command execution / file change; render accept/decline
   with session scope. `CodexConnection.ServerRequest` already exists but nothing subscribes.
2. Thread management: rename, paginated history, unarchive view.
3. Optional: refresh the sidebar thread entry automatically after the first turn so its preview/updatedAt
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
  `turn/start` takes `model`, `effort`, `sandboxPolicy` (object) and `approvalPolicy`, and always sends
  `summary: "auto"` in this package.
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
  was created on this machine. Its `model_catalog_json` must point at an existing catalog: the file is parsed
  as `ModelsResponse { models: [...] }`. A missing file makes app-server fail config load. The catalog now
  provides DeepSeek models; the user owns the API key in that home.
- `thread/settings/update` returns `{}`; the effective values arrive in `thread/settings/updated` under
  `threadSettings`. `turn/start` must omit `effort` only when the selected thread is marked synced; if the
  update request fails, clear the mark so the next turn still applies the override.
- 0.166's `thread/queue/add` dispatches queued rows itself when the thread becomes idle. A queued
  `turn/started` can therefore arrive after `IsRunning` was set false; `OnTurnStarted` must adopt it instead
  of returning early. Do not call `thread/queue/start` in this flow.
- App-server loads `model_catalog_json` only at startup. Editing it, or changing CODEX_HOME, requires a
  reconnect. The UI clears its model list on reconnect; `Reconnect` in Settings is the manual path, and it
  restarts the WebSocket listener rather than only reconnecting the socket.
- A plain domain reload must **not** restart the listener: that is the whole point of the persistent
  transport. `CodexAssistantWindow.ReloadBackend` passes `restartServer: true`; `OnEnable` and
  `RefreshOrReconnect` pass the default `false`.
- The listener is a console process with stdout/stderr inherited from the editor. It is started with
  `UseShellExecute = false`, no redirection, and `CreateNoWindow = true`; do not add redirected readers that
  live in the domain, or a reload can close the server's pipes. `Process.Dispose()` after readiness is fine;
  it does not kill the OS process.
- The listener binds `127.0.0.1` and has no extra auth beyond that. Keep it localhost-only.
- `CodexWebSocketHost.TryConnect` waits up to 15 s for the socket; the first call blocks the editor's main
  thread while it starts. If startup fails, remove the stale port/pid keys or let the next call replace them;
  the direct stdio fallback is automatic.
- CODEX_HOME and provider API keys are inherited by app-server when the process starts. Setting an API key
  with `setx` after Unity is already running requires a Unity restart before a newly started app-server sees it.
- Codex Desktop groups threads by project by default; switch to chronological order to see CLI/Unity threads.
- GitHub pushes occasionally return HTTP 500; retry. The sandbox also blocks the Windows credential helper,
  so pushing needs a wider sandbox mode.

## Commit convention

`git -C <repo> commit -m "..."` as `Caisusandy <164040662@qq.com>`. Push to `main`.
