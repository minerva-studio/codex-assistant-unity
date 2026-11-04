# Roadmap

## Done

- `codex app-server` transport (JSONL JSON-RPC), process discovery and supervision.
- Thread list, thread read, turn history, shared state with the Codex CLI and Desktop.
- Markdown rendering: headings, lists, quotes, links, tables, fenced code with syntax highlighting.
- Inline media nodes: images (thumbnails), audio (play/pause, seek, elapsed), video (play/pause,
  seek, audio) placed where the message references them.
- `Copy resume` for the selected thread.

## Next

- Composer: send a message, stream assistant deltas, stop a run.
- Approvals: command execution and file change requests, with accept/decline and session scope.
- Thread management: new thread, rename, search, paginated history.
- Domain reload: reconnect to the existing thread and restore the last selection.

## Later

- Unity context attachments: selection, scene snapshot, console, compile errors.
- Unity tools: scene and asset operations with Undo, menu items, play mode control.
- Model and mode selectors, token usage display.

## Explicitly out of scope for now

- **Mermaid rendering.** Diagrams are shown as a code block. A real renderer needs a JavaScript
  engine or a layout engine, plus a rendering surface; that is a project on its own.
- **Volume control.** The editor preview API has no volume parameter, and direct video audio plays
  at full volume. Sample scaling was tried and rejected; per-drag rescaling stutters.
- **Character-level media.** Media nodes are block level; a reference inside a sentence splits the
  paragraph into text / media / text.
