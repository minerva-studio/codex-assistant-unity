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

Read-only conversation browser. The transport, shared-history reads and media rendering are in place and
verified against Codex CLI 0.166.0 on Unity 6000.3. Sending messages, approvals and Unity-side tools are next.

## Features

- Lists the threads Codex already knows about, including ones created by the CLI.
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

Open **Window > Minerva > Codex Assistant**, pick a conversation on the left, read it on the right.

`Tools > Minerva > Codex > Diagnose App Server` runs the same connection path without any UI and prints the
handshake, the thread list and one thread's turns to the Console.

## Known limits

- **Codex Desktop groups its sidebar by project by default** and hides threads whose folder is not registered
  there. Switch it to chronological order to see threads created in Unity or the CLI.
- A thread with zero turns is not persisted by recent Codex versions, so it cannot be resumed from the CLI.
  The package creates threads on the first message rather than on window open.
- One thread should be driven from one place at a time. Codex has no cross-process lock.
- Editing C# from the agent triggers a domain reload. The conversation survives (it lives in Codex), but the
  in-flight turn is interrupted.
- Media nodes are block level: a media reference inside a sentence splits that paragraph into
  text / media / text. Character-level embedding is not supported by the text renderer.
- Volume is not adjustable. The editor preview API exposes no volume control, and direct video audio is
  played at full volume.

## Credits

- [UMarkdown](https://github.com/minerva-studio/umarkdown), a fork of
  [helightdev/UMarkdown](https://github.com/helightdev/UMarkdown) (MIT), for markdown to UI Toolkit rendering.
- [Markdig](https://github.com/xoofx/markdig) (MIT) and
  [ColorCode](https://github.com/CommunityToolkit/ColorCode-Universal) (MIT) via that package.
- [Newtonsoft Json](https://www.newtonsoft.com/json) (MIT) for the app-server protocol.

See [Third Party Notices](Third%20Party%20Notices.md).
