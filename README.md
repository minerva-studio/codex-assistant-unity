# Codex Assistant for Unity

An Editor window that runs the Codex agent through [`codex app-server`](https://learn.chatgpt.com/docs/app-server)
and renders the conversation the way Codex actually writes it: markdown, tables, syntax-highlighted code,
images, audio and video.

Conversations are not stored by this package. Codex owns the thread store, so a thread created here is the
same thread you can resume from the CLI:

```bash
codex resume <thread-id>
```

and the same one Codex Desktop lists in its sidebar (switch it to chronological order, see
[Known limits](#known-limits)).

## Status

Conversation client with a composer, searchable/archivable thread navigation, collapsible reasoning and tool
records, and a settings panel. Verified against Codex CLI 0.166.0 on Unity 6000.3. Approvals and Unity-side
tools are next.

## Features

- Lists non-archived threads Codex already knows about, including ones created by the CLI.
- Sidebar search across thread content, a **This project / All projects** scope filter, thread titles, and
  an **Archive** action for the selected thread. Archived threads stay hidden.
- **New conversation** starts a thread on the first message without creating a zero-turn thread.
- Composer at the bottom of the window: Enter sends while idle, Shift+Enter inserts a newline, and one button
  switches between Send and Stop. While a turn runs, Enter queues a follow-up and Ctrl+Enter steers the
  active turn. Queued messages appear above the composer and can be removed individually.
- Assistant replies stream into the transcript as Codex writes them; the completed turn is reloaded from
  history so the transcript matches the stored conversation.
- The app-server runs as a localhost WebSocket listener per CODEX_HOME instead of a stdio child. A domain
  reload or entering play mode only reconnects the window: the running turn continues and the selected
  thread is restored. If the listener cannot start, the package falls back to stdio.
- Model, reasoning effort and sandbox selectors. Their initial values come from the effective Codex config,
  and selecting a conversation syncs the values that thread actually uses. Effort changes go through
  `thread/settings/update`; later turns omit the per-turn effort override, so Codex keeps its normal prompt
  cache path. The window follows the transcript to the bottom by default and stops following while you read
  older messages.
- Tool usage (commands, file edits, MCP calls, web searches), intermediate Codex replies and thinking are
  grouped into one **Process** row per stretch; only the turn's final answer stays as a normal message.
  While the turn is live the row is expanded so work is visible; completed history collapses it. Expanding
  shows intermediate replies and short thinking summaries inline; long reasoning chains fold under a
  collapsed **Full reasoning** section. Individual tool steps stay collapsible.
- Tool screenshots (`imageView`) render as images instead of a JSON block.
- Optional context-window indicator next to the model. It is off by default and shows the last reported token
  usage as a colored dot and percentage.
- Changing CODEX_HOME starts that home's listener and reloads its provider model catalog. **Reconnect** in
  Settings restarts the active listener, so edits to `config.toml` or `models.json` take effect.
- Renders assistant messages with markdown: headings, lists, quotes, links, tables and code blocks with
  syntax highlighting.
- Images, audio and video appear inline, at the position the message references them:
  - images render as thumbnails,
  - audio gets play/pause, a seek bar and elapsed time,
  - video plays in the panel through `VideoPlayer` with sound.
- `Copy resume` puts the matching `codex resume <thread-id>` command on the clipboard.
- Missing media files keep their path in the text instead of producing a dead control.

## Requirements

- Unity 6000.3 or newer.
- Codex CLI installed and logged in (`codex login`). By default the package reuses `~/.codex`; the CODEX_HOME
  setting can point it at a separate home. It does not implement its own login.
- Codex 0.166 accepts only `wire_api = "responses"` for custom providers. A provider that only exposes OpenAI
  Chat Completions needs a Responses-to-Chat bridge in front of it.

## Install

Add the package from Git:

```
https://github.com/minerva-studio/codex-assistant-unity.git
```

The markdown renderer is a separate dependency and is pulled automatically:

```
https://github.com/minerva-studio/umarkdown.git?path=src/UMarkdown/Assets/Plugins/UMarkdown
```

## Use

Open **Window > Minerva > Codex Assistant**. Pick a conversation on the left, or press **+ New conversation**,
then type in the composer at the bottom. The model, effort and sandbox pills apply to the next message; the
sandbox defaults to the effective Codex config rather than a hard-coded mode. **Stop** interrupts the running
turn.

The search field matches thread content. The scope dropdown defaults to **This project** and can show **All
projects**. **Archive** hides the selected thread from future lists.

**Settings** (also **Window > Minerva > Codex Assistant Settings**) controls the context indicator, whether
reasoning and tool records are shown, and the CODEX_HOME used by app-server. Changing CODEX_HOME restarts and
reconnects the app-server; an empty value means the process default (`~/.codex`). **Reconnect** reloads
`config.toml`, providers and models after editing the active home. Environment variables such as an API key
are inherited when app-server starts, so restart Unity after changing one.

`Tools > Minerva > Codex > Diagnose App Server` runs the same connection path without any UI and prints the
handshake, the thread list and one thread's turns to the Console. `Tools > Minerva > Codex > Send Smoke Test`
sends two real messages, one after creating a thread, and checks streaming and history reload.

## Known limits

- **Codex Desktop groups its sidebar by project by default** and hides threads whose folder is not registered
  there. Switch it to chronological order to see threads created in Unity or the CLI.
- A thread with zero turns is not persisted by recent Codex versions, so it cannot be resumed from the CLI.
  The package creates threads on the first message rather than on window open.
- One thread should be driven from one place at a time. Codex has no cross-process lock.
- Approval requests are not handled yet. A turn that asks to run a command or change files will wait for a
  decision the window cannot give.
- A CODEX_HOME change restarts the app-server. That home has its own threads, auth and model provider; moving
  between homes does not migrate conversations.
- The context indicator needs a token-usage notification. It shows `ctx —` until the conversation has run a
  turn with the indicator enabled.
- A domain reload or play-mode transition reconnects to the existing listener, so a running turn continues.
  **Reconnect** or a CODEX_HOME change restarts the listener on purpose; that interrupts an in-flight turn so
  the new config can be loaded.
- The WebSocket listener binds `127.0.0.1` only and has no additional authentication. Any local process can
  connect to it, just like the Codex CLI itself.
- The listener is a separate process per CODEX_HOME. It is stopped on a normal Unity quit; after a crash it
  may remain until the next connect reuses or replaces it.
- Media nodes are block level: a media reference inside a sentence splits that paragraph into
  text / media / text. Character-level embedding is not supported by the text renderer.
- Volume is not adjustable. The editor preview API exposes no volume control, and direct video audio is
  played at full volume.

## Credits

- [UMarkdown](https://github.com/minerva-studio/umarkdown), a fork of
  [helightdev/UMarkdown](https://github.com/helightdev/UMarkdown) (MIT, Copyright (c) 2025 Christoph
  'HelightDev' Feuerer), for markdown to UI Toolkit rendering.
- [Markdig](https://github.com/xoofx/markdig) (BSD-2-Clause, Copyright (c) Alexandre Mutel) and
  [ColorCode](https://github.com/CommunityToolkit/ColorCode-Universal) (MIT, Copyright (c) .NET
  Foundation and Contributors), both distributed inside that package.
- [Newtonsoft Json](https://www.newtonsoft.com/json) (MIT) for the app-server protocol.

See [Third Party Notices](Third%20Party%20Notices.md).
