# Changes Navigation Implementation Plan

> Execute inline with the existing Editor and feature branch. The user has asked for direct UI iteration.

**Goal:** Make the upper Changes navigation readable at sidebar widths without reducing patch reading space.

**Architecture:** Keep workspace turns authoritative. A single file-change selector replaces separate turn and file selection. Clicking it reuses the owning window's searchable popup, capped at eight visible rows, with every loaded file record and its turn context. Previous/next navigates individual records. No permanent file list remains. Existing record keys, live/history deduplication, Open file and raw patch copy remain unchanged.

**Tech Stack:** Unity 6000.3 UI Toolkit and the existing CodexChangesPanel.

- [x] Reuse the existing user-message text extraction for readable turn summaries, excluding attached Unity context.
- [x] Render filename, record position and cleaned turn summary in one selector; reuse the window's search popup for file records and add adjacent-record navigation with disabled endpoints.
- [x] Remove the permanent file list and repeated path heading. Leave earlier-history loading reachable even with no recorded changes and keep Open file/Copy patch beside status.
- [x] Adapt the focused Changes check to 200-turn constant-size navigation, direct picker jumps, adjacent navigation, selection retention, context stripping and repeat records.
- [x] Compile in the existing Editor, run the focused check and inspect 360px/240px sample layouts with 200 turns. Exercise the actual searchable popup with 200 isolated actions, filter to one result and press Enter. Do not send conversation messages or start another Editor.
- [x] Update feature documentation and commit locally without push/merge.

Revisions: The first accordion was rejected because a long history produced a long permanent menu. The replacement turn selector plus file rows was then rejected for requiring two selection levels. The current design combines them into one file-change picker and removes the secondary list.

Validation: Unity 6000.3.25f1 compiled with zero console errors/warnings. Focused Changes smoke passed with 200 turns, direct picker jumps, adjacent records across turns and repeated paths, Live aggregate selection, selection retention and Open file availability. The shared searchable popup previously passed its eight-row cap, search and Enter checks; its implementation is unchanged. Both 360px and 240px sample layouts were inspected; the selector shrinks to keep both adjacent buttons visible. Sample layouts use isolated data rather than real conversation history.
