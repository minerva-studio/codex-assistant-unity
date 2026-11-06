# Changes Navigation Implementation Plan

> Execute inline with the existing Editor and feature branch. The user has asked for direct UI iteration.

**Goal:** Make the upper Changes navigation readable at sidebar widths without reducing patch reading space.

**Architecture:** Keep workspace turns authoritative. A fixed current-turn summary and newer/older buttons replace the all-turn accordion. Clicking the summary reuses the owning window's searchable popup, capped at eight visible rows. Only current-turn files are permanent rows. Existing record keys, live/history deduplication and raw patch copy remain unchanged.

**Tech Stack:** Unity 6000.3 UI Toolkit and the existing CodexChangesPanel.

- [x] Reuse the existing user-message text extraction for readable turn summaries, excluding attached Unity context.
- [x] Render a fixed current-turn summary with position, time and record count; reuse the window's search popup for loaded turns and add adjacent navigation with disabled endpoints.
- [x] Keep compact filename/directory rows for the current turn only, capped at 32% of panel height. Leave earlier-history loading reachable even with no recorded changes.
- [x] Adapt the focused Changes check to 200-turn constant-size navigation, direct picker jumps, adjacent navigation, selection retention, context stripping and repeat records.
- [x] Compile in the existing Editor, run the focused check and inspect 360px/240px sample layouts with 200 turns. Exercise the actual searchable popup with 200 isolated actions, filter to one result and press Enter. Do not send conversation messages or start another Editor.
- [x] Update feature documentation and commit locally without push/merge.

Revision: The first accordion was rejected because a long history still produced a long permanent menu. Its all-turn rows and expansion state were removed rather than preserved alongside the selector.

Validation: Unity 6000.3.25f1 compiled with zero console errors/warnings. Focused Changes smoke passed with 200 turns, including a direct jump to the last turn and adjacent navigation. The actual window popup displayed at most eight rows, found one matching title among 200 actions and executed that action on Enter, then closed. At 360px the current-turn file list was 103.2px and patch 456px within a 674.4px panel. Both 360px and 240px sample layouts were inspected; sample data was isolated from conversation history.
