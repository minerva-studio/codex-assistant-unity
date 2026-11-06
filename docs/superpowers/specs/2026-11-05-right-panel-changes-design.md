# Right-side Changes panel

Status: implemented; focused behavior and layout checks passed in Unity 6000.3.25f1.

## Intent and layout

Extend the existing right-side Subagents area into a shared auxiliary panel with
Changes and Subagents tabs. Keep the conversation list on the left and the
transcript/composer in the center. Preserve a single resizable right-side column.

The user corrected the requested location from left to right and approved the
shared-tab proposal with an instruction to implement and show the result.

## Approaches

- Recommended: reuse the existing right column and switch its content with tabs.
  This keeps chat visible and reuses the current resizing behavior.
- Separate tool windows: more width for diff inspection, but adds window management
  and does not establish the requested right-side panel area.
- Simultaneous Changes and Subagents columns: consumes too much transcript width.

## First-version behavior

- Provide a Changes toolbar entry even when there are no subagents. Keep the
  existing Agents entry as a shortcut to the Subagents tab.
- Switching tabs does not change the selected conversation. Selecting a subagent
  retains the existing behavior of opening its conversation in the center.
- Changes contains a turn selector, a file-change list, and a diff detail area.
  Initially select the newest loaded turn with recorded changes or a live diff.
- Show individual file-change records, including path, operation and item status.
  Preserve repeated edits to the same path as separate records; do not invent a
  combined patch.
- Render diff text with indentation and line breaks preserved. The first version
  is read-only and supports copying its displayed patch.
- Show a live aggregate turn diff as a separate, explicitly live entry when the
  server supplies it. Do not present it as a persisted historical patch.
- Preserve valid turn/file selection, expansion and reading position through
  background updates and tab switches. Reset selection when the thread changes.
- Reuse existing history pagination for loading earlier turns. Empty views say
  that no recorded changes are available, rather than claiming no files changed.

## State and data ownership

CodexWorkspace remains authoritative for loaded turns and the live turn. Changes
reads fileChange.changes[] and the existing synthetic live turnDiff item from those
objects. The panel owns only presentation state, such as selected record and scroll
position. It does not introduce a second mutable conversation store.

CodexAssistantWindow owns the shared right-column visibility, active tab and width.
Replace the existing agents-only layout controls directly rather than layering a
second independently resizable column beside them.

## Boundaries and acceptance

No filesystem mutation, apply/revert actions, Git working-tree browser, new protocol
requests, persisted aggregate-diff cache, or plan-history panel in this version.
Shell edits without fileChange records are outside the recorded change list.

Check history/live deduplication, repeated paths, failed changes, empty data,
thread switching, pagination, live completion, tab switching and pane resizing.
Do not treat the existence of rendering code as proof that every historical
conversation contains a diff; inspect existing history samples during implementation.

The implementation plan and product-code changes follow approval of this design.
