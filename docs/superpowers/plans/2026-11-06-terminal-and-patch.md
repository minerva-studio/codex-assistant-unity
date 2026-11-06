# Terminal and Patch Implementation Plan

> **For agentic workers:** Use executing-plans for the main terminal work. Independent patch and buffer implementations have explicitly separated file ownership.

**Goal:** Replace the argv form with a persistent shell terminal, repair Process input ownership, and add patch backgrounds.

**Architecture:** Workspace owns a CodexTerminalSession rather than duplicated console fields. Session owns protocol/lifecycle/history, buffer owns VT display state, window owns UI. Patch view owns derived display only.

**Tech Stack:** Unity 6000.3 UI Toolkit, C# 9, Newtonsoft.Json, existing app-server PTY/stdin/resize APIs.

**Spec:** `docs/superpowers/specs/2026-11-06-terminal-and-patch-design.md`

## Global constraints

- Reuse feat/changes-panel and the existing markdown-proto-2 Editor. No push/merge/new Editor/Codex conversation messages.
- Default platform shell, persistent cwd/variables, direct stdin, Ctrl+C; no silent per-command shell restart.
- Preserve existing Changes and user work. Comments/documentation English, user communication Chinese.

## Review focus

- Startup prompt, late process completion and restart races cannot enable or kill the wrong session.
- Input and ANSI/OSC sequences split across chunks remain correct; terminal control metadata stays hidden.
- Ctrl+C interrupts the current command while keeping the shell; input during execution reaches the program.
- Closing/rebinding/reloading uses the correct workspace and unsubscribes old events.
- Patch headers vs +++/--- code, raw copy and Changes scroll remain correct.

## Tasks

- [x] Observe a failing Process-input regression, then remove the standalone-session input callback/API from transcript rendering.
- [x] Verify real PowerShell PTY via existing connection (ls, cwd, variable, prompt metadata), without modifying conversations.
- [x] Add CodexTerminalBuffer and focused smoke (independent agent owns only its new buffer/test files).
- [x] Add CodexTerminalSession and extend client PTY/stdin/resize requests; replace old workspace Console state with the owned session.
- [x] Rebuild Console as a terminal: compact toolbar/directory, selectable output, bottom command row, history, stdin, interrupt/restart/clear/copy.
- [x] Share existing caret reset logic between composer and terminal input to prevent stale Unity text-editing indices.
- [x] Add CodexPatchView, Changes integration and focused smoke (independent agent owns only patch/Changes files); reuse view in transcript diffs.
- [x] Compile in the live Editor and run the focused smoke and protocol/interaction checks listed in the spec.
- [x] Obtain fresh-context read-only review, fix material findings and update documentation.
- [x] Commit locally; do not push or merge.

## Evidence and rulings

- RED observed: a running conversation command with processId exposes codex-terminal-input even though its id is not a standalone command/exec session.
- Real PowerShell PTY probe: bare ls listed files; $x=7 and cd persisted to subsequent commands; exit returned final command/exec exit 0.
- User requested implementation rather than another design approval round. Main work continues inline; patch/buffer tasks have non-overlapping file ownership and do not operate the Editor.
- Fresh-context read-only review reported three P2 findings: reconnect lost cwd, Folder disabled after failed startup, and unbounded combining text per terminal cell. All three were fixed. No second delegated review was requested.
- RED/GREEN: replacement session previously started in project root instead of cd's parent; after the fix both initial start and real prompt confirm the previous cwd. Closing/reopening the view also retains the shell variable and cwd.
- Recovery: an actual local shell exit leaves Folder enabled and restarting at a valid selected directory reaches Ready. A missing cwd starts PowerShell in HOME on this host rather than failing; the native folder chooser itself was not automated.
- RED/GREEN: 2048 combining marks on one base character previously produced 2049 units in one cell. The cell limit and actual visible-text budget now bound both one cell and a dense 256k-unit screen; recent output is retained.
- Live checks: Sandboxed and Local, UI Enter/pipeline, Up/Down history, clear caret, Read-Host stdin, Ctrl+C during Start-Sleep then use the same variable, literal Chinese/accent/emoji output, native command failure, reconnect and view close/reopen. No conversation messages were sent.
- Screenshot inspection caught proportional font fallback and duplicate output during ConPTY resize. FontDefinition, stretched viewport and stable gutters fixed it; real ls now shows each header/file once, and measured i/M widths match. A panel guard prevents pre-attachment DPI warnings.
- Final build: Unity 6000.3.25f1 reports compilationFailed=false, compiling=false, consoleErrors=0 and consoleWarnings=0 after the last script compile. All four focused smoke checks pass. Changes/long transcript patch and terminal screenshots were inspected.
- Limits: lightweight plain VT rather than a full-screen TUI/SGR renderer; no scrollback reflow. Bash/macOS/Linux remains untested. Connection loss/reload starts a fresh shell, retaining directory/draft/mode only.
