# Handoff

State of the Codex Assistant Unity package, so work can continue without the previous conversation.

## Product

A Unity Editor window that drives `codex app-server` directly and renders conversations the way Codex writes
them. Threads are shared with the Codex CLI and Codex Desktop through the same `CODEX_HOME`; the package stores
no conversation state of its own.

## Repositories

| Repo | Local path | Notes |
| --- | --- | --- |
| https://github.com/minerva-studio/codex-assistant-unity | `C:\Users\wende\Documents\minerva-game-studio\codex-assistant-unity` | The package, `main`, latest `0b90d66` |
| https://github.com/minerva-studio/umarkdown | `C:\Users\wende\Documents\minerva-game-studio\umarkdown` | MIT fork of helightdev/UMarkdown, `main` `de286d8` |

Package name `com.minervagamestudio.codex.assistant`, Unity `6000.3`.
Dependencies: `com.unity.nuget.newtonsoft-json 3.2.1`, and UMarkdown pinned to
`https://github.com/minerva-studio/umarkdown.git?path=src/UMarkdown/Assets/Plugins/UMarkdown#171fafe43912c4d7d903b7c853424c1aa42ebe19`.

Test projects: `markdown-proto-2` (GUI, has Pipeline installed, port 7800), `markdown-proto`, `install-test`.

## What works

- Transport: JSONL JSON-RPC over stdio, background reader, callbacks marshalled to the main thread.
- `initialize`, `thread/list`, `thread/read`, `thread/resume`, `thread/turns/list`, `turn/start`,
  `turn/interrupt` wrappers exist in `CodexClient`.
- Window lists the threads Codex knows about (shared store), shows history, `Copy resume`.
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

## Next task: composer, send, streaming

1. `CodexWorkspace.SendMessage(string text)`
   - No thread selected: `thread/start` first. Create lazily on first send; zero-turn threads are not persisted.
   - Otherwise `turn/start` with `{ threadId, input: [{ type: "text", text }] }`.
2. Subscribe to notifications: `turn/started`, `item/started`, `item/agentMessage/delta`, `item/completed`,
   `turn/completed`. Assemble the live assistant item from deltas.
3. On `turn/completed`, reload `thread/turns/list`; history is the source of truth.
4. UI: composer at the bottom (Enter sends, Shift+Enter newline), Send/Stop, disable input while running,
   `turn/interrupt` on Stop.
5. Ship in two steps: first "send + wait + reload history" (proves the protocol), then add delta streaming.

After that: approvals (command / file change), thread management (new, rename, search, paging), domain reload
reconnect and resume.

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
- Codex Desktop groups threads by project by default; switch to chronological order to see CLI/Unity threads.
- GitHub pushes occasionally return HTTP 500; retry.

## Commit convention

`git -C <repo> commit -m "..."` as `Caisusandy <164040662@qq.com>`. Push to `main`.
