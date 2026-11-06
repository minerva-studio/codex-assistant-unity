# Terminal and patch usability

User-directed revision of the previous Console: default shell execution and usable
terminal layout, removal of misleading input from conversation Process cards, and
patch addition/deletion backgrounds. The user asked to implement the revision.

## Terminal

Use a persistent app-server PTY shell, PowerShell on Windows and bash on Unix.
Each shell retains its own cwd, variables and environment. Human commands are sent
directly to its stdin; users do not type a shell executable prefix. Ctrl+C sends
the PTY interrupt byte. Sandboxed and Local retain their existing meanings.

The compact window has a small shell/mode/actions bar, a directory/status line,
dominant selectable monospaced output, and a command/input row at the bottom.
Enter submits, Shift+Enter adds a line, Up/Down recall command history, Ctrl+C
interrupts, Ctrl+L clears output. Input stays available while programs run.
Directory changes come from the shell prompt; folder selection sends a shell cd.
Restart explicitly starts a fresh shell and environment. Closing the window keeps
the workspace's shell alive; connection loss/domain reload ends the connection-
scoped PTY and the next connection starts a fresh session at the last known cwd.

CodexTerminalSession owns process ids, lifecycle, protocol callbacks, per-stream
UTF-8 decoding, history and prompt metadata. CodexTerminalBuffer owns bounded
plain-text screen/scrollback and essential ANSI cursor/erase parsing. Workspace
owns the current terminal session; the window owns presentation/draft/history index.
Do not keep the old independently writable Console state beside the session.
No embedded browser, external emulator dependency or conversation-state cache.

Prompt metadata uses an OSC control message containing status and a base64 UTF-8 cwd.
The renderer never displays it. Basic command/readline programs are the target;
this is not a complete graphical terminal or a promise of arbitrary full-screen TUIs.

## Conversation Process

Remove its input row and SendTerminalInput chain. A tool item's processId does
not establish ownership of the client-created command/exec session required by
command/exec/write. Existing output and observed terminal-interaction records stay
read-only; normal messages continue through the composer.

## Patch rendering

Use a shared CodexPatchView for Changes and transcript patches. Preserve raw copy,
indentation, CRLF and literal text. Added lines have green text/background, deleted
lines red, hunk headings blue, file headings and context neutral. Header detection
must distinguish actual headers from +++/--- text inside hunks. Backgrounds fill
the visible width. Keep the existing Changes selection and scrolling behavior.

## Checks

Run focused buffer/patch/Changes/Process smoke checks and real PTY checks: bare ls,
persistent cd and variable, pipeline, Unicode, native failure, mid-command stdin,
Ctrl+C with shell surviving, mode/restart, resize, close/reopen and compile state.
Capture actual Editor windows and inspect them. Do not send Codex messages, launch
another Editor, push, merge or introduce a full test-suite run.
