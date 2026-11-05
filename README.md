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
  an **Archive** action for the selected thread. Archiving reloads the list, verifies the thread is gone,
  and keeps the archived id filtered, so a stale list response cannot bring the conversation back.
- **New conversation** starts a thread on the first message without creating a zero-turn thread. After the
  first turn it generates a short title, using the conversation model or a separate title model from Settings.
- **Fork** copies the selected conversation into a new thread and opens it. The source conversation is
  unchanged.
- Subagent tool calls and activity render as **Subagent** sections with **Open** buttons. Subagent threads
  are opened from their parent item, not listed as separate sidebar conversations; the header shows
  **← Parent** while viewing one.
- A minimal **Subagents** rail on the right lists the selected conversation's agents with status, model and
  current activity. Click a row to open that agent; the toolbar **Agents N** button hides/shows the rail.
- Running work has a pulsing hint: a dot in the toolbar, a dot on the active sidebar conversation, and
  pulsing dots on running subagent rows. Static green/yellow/red dots mark completed/failed agents.
  A historical subagent with a stale “running” state is shown as **idle** unless it appears in the current
  live turn, so an old agent is never animated as if it were still working.
- When Codex asks a `request_user_input` question, a card appears above the composer with its options and an
  optional free-text answer. The composer waits until every question is answered, then sends one response.
  Stored `send_user_message_question_reply` messages render as a question / answer card instead of raw JSON.
- Command, file-change and permission approval requests, plus MCP form/URL elicitations, use the same
  interaction card. The card shows the command/cwd, changed files or requested permissions and sends the
  decision back to the app-server.
- Live command/file output, MCP progress, plan steps and the turn diff update inside Process while the turn
  runs. A running command with a PTY process id gets an inline terminal input row.
- Codex output is plain document text; only user input uses a compact, right-aligned bubble (max 68% of the
  transcript width). Process and approval/question surfaces remain cards because they are interaction
  containers.
- Collapsed Process groups and tool foldouts show their elapsed time in the header, e.g.
  `Process · 22 tools · 2 replies · 9m 55s` or `$ npm test · 4s`.
- A modernized dark/light visual pass: compact toolbar and sidebar rows, accent selections, card-based
  Process/interaction surfaces, a roomier composer and a dedicated empty state. The active-thread indicator
  now uses a reserved leading slot, so it no longer drifts with wrapped titles.
- Right-click a sidebar conversation for **Open**, **Rename...**, **Copy resume**, **Fork** and **Archive...**.
- Every user or Codex message has a compact footer with **Select**, **Copy** and its local time.
  **Select** switches only that message to a read-only text field so its text can be dragged and copied;
  **Done** restores the formatted message. **Fork** appears only on a completed turn's final Codex message:
  `thread/fork` is turn-granular (`lastTurnId`), so mid-turn and intermediate messages cannot fork exactly
  in place. UI Toolkit rich labels themselves do not expose text selection.
- Codex attachment annotations (“Files mentioned by the user… My request:”) are collapsed to the actual
  request; base64 image attachments render as images instead of dumping their payload into the transcript.
  Historical Process groups build their hidden tool rows lazily, so long threads don't create thousands of
  elements during a rebuild.
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
projects**. **Archive** hides the selected thread from future lists. **Fork** creates a new conversation with
the same persisted history and selects it.

**Settings** (also **Window > Minerva > Codex Assistant Settings**) controls the context indicator, whether
reasoning and tool records are shown, automatic conversation titles and their optional model, and the
CODEX_HOME used by app-server. Changing CODEX_HOME restarts and reconnects the app-server; an empty value
means the process default (`~/.codex`). **Reconnect** reloads `config.toml`, providers and models after
editing the active home. Environment variables such as an API key are inherited when app-server starts, so
restart Unity after changing one.

`Tools > Minerva > Codex > Diagnose App Server` runs the same connection path without any UI and prints the
handshake, the thread list and one thread's turns to the Console. `Tools > Minerva > Codex > Send Smoke Test`
sends two real messages, one after creating a thread, and checks streaming and history reload.

## Known limits

- **Codex Desktop groups its sidebar by project by default** and hides threads whose folder is not registered
  there. Switch it to chronological order to see threads created in Unity or the CLI.
- A thread with zero turns is not persisted by recent Codex versions, so it cannot be resumed from the CLI.
  The package creates threads on the first message rather than on window open.
- One thread should be driven from one place at a time. Codex has no cross-process lock.
- Automatic titles run one small extra **ephemeral** turn and send the first user/assistant exchange to the
  configured title model. If that model is unavailable, the conversation keeps its preview title.
- **Fork** copies persisted history into an independent thread; the source is unchanged. The fork is not
  auto-titled.
- Subagent threads are not listed in the sidebar; open one from its parent's Subagent section, then use
  **← Parent** to return. The package does not add a manual "spawn subagent" button; the model decides when
  to delegate using the enabled `multi_agent` tools.
- `request_user_input`, command/file-change/permission approvals and standard MCP form/URL elicitations are
  handled. Legacy `execCommandApproval`/`applyPatchApproval`, MCP device verification and non-standard
  OpenAI elicitation forms are not fully supported.
- Plan/diff cards are live-turn state; they are not persisted as separate history panels. Standalone
  `command/exec` and `process/spawn` sessions are not exposed by the window; terminal input is available
  when a running `commandExecution` item exposes a PTY `processId`.
- A CODEX_HOME change restarts the app-server. That home has its own threads, auth and model provider; moving
  between homes does not migrate conversations.
- The context indicator needs a token-usage notification. It shows `ctx —` until the conversation has run a
  turn with the indicator enabled.
- Some providers emit a reasoning placeholder with empty `summary` and `content`; the window hides it
  instead of showing a `1 thought` row with nothing inside.
- Reasoning summaries are requested with `summary: "auto"`. Some GPT-family model builds persist only
  encrypted reasoning with an empty plaintext `summary`; old turns cannot be backfilled, and those models
  will show no Thinking text even though reasoning occurred.
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
