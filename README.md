# Codex Assistant for Unity

Unity Editor client for the [Codex app-server](https://learn.chatgpt.com/docs/app-server) protocol.

Conversations are not stored by this package. Codex owns the thread store, so a conversation
started in Unity is the same conversation you can resume from Codex CLI:

```bash
codex resume <thread-id>
```

and the same one Codex Desktop lists in its sidebar (switch it to chronological order; see
[Known limits](#known-limits)).

## Status

Early development. The transport, process supervision, shared-history reads and a read-only
conversation window are in place, verified against Codex CLI 0.166.0 on Unity 6000.3. Sending
messages, approvals and Unity-side tools are next.

## Requirements

- Unity 6000.3 or newer.
- Codex CLI installed and logged in (`codex login`). The package reuses your existing
  `~/.codex` state and credentials; it does not implement its own login.

## Install

Add the package from Git:

```
https://github.com/minerva-studio/codex-assistant-unity.git
```

or point Package Manager at a local clone:

```
"com.minervagamestudio.codex.assistant": "file:../codex-assistant-unity"
```

## Try it

1. Open a project with the package installed.
2. Select **Window > Minerva > Codex Assistant**.
3. Pick a conversation on the left. These are the threads Codex already knows about; **Copy resume**
   puts the matching `codex resume <thread-id>` command on the clipboard.

If the window shows nothing, run **Tools > Minerva > Codex > Diagnose App Server** and check the
Console. It prints the handshake, the thread list and one thread's turns without any UI.

Headless:

```bash
Unity -batchmode -nographics -projectPath <project> \
  -executeMethod Minerva.Codex.Editor.Dev.CodexSmokeTest.Run -quit -logFile -
```

## Known limits

- **Codex Desktop groups its sidebar by project by default**, and hides threads whose folder is not
  registered there. Switch to chronological order to see threads created in Unity or the CLI.
  This is Codex behaviour, not a package setting.
- A thread with zero turns is not persisted by recent Codex versions, so it cannot be resumed from
  the CLI. The package creates threads on the first message rather than on window open.
- One thread should be driven from one place at a time. Codex has no cross-process lock.
- Editing C# from the agent triggers a domain reload. The conversation survives (it lives in Codex),
  but the in-flight turn is interrupted.

## Layout

```
Editor/
  Codex/          transport, process supervision, client calls
  Dev/            diagnostic entry points
```

Protocol messages are kept as `JObject` on purpose. App-server adds fields often, and a typed mirror
of the schema would break on every Codex release.
