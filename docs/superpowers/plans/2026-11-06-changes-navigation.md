# Changes Navigation Implementation Plan

> Execute inline with the existing Editor and feature branch. The user has asked for direct UI iteration.

**Goal:** Make the upper Changes navigation readable at sidebar widths without reducing patch reading space.

**Architecture:** Keep workspace turns authoritative. Replace the timestamp/id dropdown and large file cards with a single accordion of turn summaries and compact file rows. Only the selected turn expands; selecting another turn opens its first recorded change. Existing record keys, live/history deduplication and raw patch copy remain unchanged.

**Tech Stack:** Unity 6000.3 UI Toolkit and the existing CodexChangesPanel.

- [x] Reuse the existing user-message text extraction for readable turn summaries, excluding attached Unity context.
- [x] Render turn headers with summary, time and record count; file rows with filename, directory, operation and exceptional status. Keep repeated paths separate and label their edit order.
- [x] Limit navigation to 42% of panel height; keep patch detail below and move earlier-history loading to the end of navigation.
- [x] Adapt the focused Changes check to real group-button navigation, selection retention, context stripping and repeat records.
- [x] Compile in the existing Editor, run the focused check and inspect 360px/240px sample layouts. Do not send conversation messages or start another Editor.
- [x] Update feature documentation and commit locally without push/merge.

Validation: Unity 6000.3.25f1 compiled with zero console errors/warnings. Focused Changes smoke passed; 360px and 240px sample screenshots inspected. At 360px the navigation was 235.2px and the patch 350.4px within a 674.4px panel. Samples were isolated from conversation history and closed afterward.
