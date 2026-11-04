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

Conversation client with a composer. The transport, shared-history reads, sending, streamed replies, stop,
model settings and media rendering are in place and verified against Codex CLI 0.166.0 on Unity 6000.3.
Approvals and Unity-side tools are next.

## Features

- Lists the threads Codex already knows about, including ones created by the CLI.
- Composer at the bottom of the window: Enter sends, Shift+Enter inserts a newline, and one button switches
  between Send and Stop. Input is disabled while a turn runs.
- Sending with no conversation selected starts a new thread on the first message.
- Assistant replies stream into the transcript as Codex writes them; the completed turn is reloaded from
  history so the transcript matches the stored conversation.
- Model, reasoning effort and sandbox selectors. Their initial values come from the effective Codex config,
  and selecting a conversation syncs the values that thread actually uses.
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
- Codex CLI installed and logged in (`codex login`). The package reuses your existing `~/.codex` state; it
  does not implement its own login.

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

Open **Window > Minerva > Codex Assistant**, pick a conversation on the left, and type in the composer at the
bottom. The model, effort and sandbox pills apply to the next message; the sandbox defaults to the effective
Codex config rather than a hard-coded mode. **Stop** interrupts the running turn.

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
- Editing C# from the agent triggers a domain reload. The conversation survives (it lives in Codex), but the
  in-flight turn is interrupted and the window has to reconnect.
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
