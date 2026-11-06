# Changes Navigation Implementation Plan

> Execute inline with the existing Editor and feature branch. The user has asked for direct UI iteration.

**Goal:** Make the upper Changes navigation readable at sidebar widths without reducing patch reading space.

**Architecture:** Keep workspace turns authoritative. The scope menu chooses Latest turn (following newer recorded turns) or a pinned historical turn. A single vertical scroll view renders every record in that turn as an independently collapsible filename/path header and inline patch. Stable record views retain folding and reading position during refresh; a turn change resets them. The existing popup also offers optional file jumps within the current scope. Patch classification supplies per-file and scope addition/deletion counts, excluding duplicate live aggregates from totals. Open file, raw copy, history pagination and live/history deduplication remain unchanged.

**Tech Stack:** Unity 6000.3 UI Toolkit and the existing CodexChangesPanel.

- [x] Reuse the existing user-message text extraction for readable turn summaries, excluding attached Unity context.
- [x] Replace file selection with a latest/historical turn scope and continuous per-record patches. Reconcile views by record key instead of rebuilding unchanged records.
- [x] Add per-file folding, repeated-edit labels, raw patch copy, file opening and classified line counts. Keep one vertical scroll area and wrap long patch lines only in Changes.
- [x] Add current-scope file jumps and collapse/expand all. Live aggregate starts collapsed and does not double count per-file totals.
- [x] Adapt focused checks to multi-file visibility, fold retention, history pinning/latest following, repeat records, file jumps, raw copy and line counts. Keep file-path availability checks.
- [x] Compile in the existing Editor, run focused Changes/Patch checks and inspect normal/narrow multi-file layouts with isolated data. Verify scroll preservation after a background update and a tab switch. Do not send conversation messages or start another Editor.
- [x] Update feature documentation, request a fresh-context review and commit locally without push/merge.
- [x] Compact file headers to a single 26px row, move full paths to tooltips and reduce Changes-only patch padding. Translate each original header within its record for sticky reading; retain normal file-boundary push-off and hit testing without writing scroll state.

Revisions: The first accordion was rejected because a long history produced a long permanent menu. The replacement turn selector plus file rows required two selection levels; the subsequent single-file picker hid the overview and still required constant switching. The approved direction, informed by the supplied T3 Code and ChatGPT screenshots, is continuous multi-file reading with optional navigation.

Validation: Unity 6000.3.25f1 compiled with zero console errors/warnings. Focused Changes and Patch smoke checks passed, including continuous file sections, persistent folding, latest following versus history pinning, 200-turn bounded rendering, raw copy source and line counts. Isolated Editor fixtures at 560px, 360px and 240px were inspected. Background patch update and tab hiding/showing preserved a 120px reading offset after layout; the real file-jump callback revealed the last record. Fresh-context review returned ACCEPT. Native clipboard round-trip passed initially but later returned empty even for direct EditorGUIUtility.systemCopyBuffer writes, so OS clipboard delivery remains unverified; the focused test checks the button's exact raw source without depending on ambient clipboard availability. No conversation messages were sent, another Editor was not started, and no external file opener was launched during validation.

Sticky-header follow-up: existing Changes smoke passed and compilation remained at zero errors/warnings. A long-file fixture verified the original header at the viewport top after a 300px scroll, correct pointer picking, push-off at the next file boundary and takeover by the next filename. Collapsing a pinned file reduced its record to 27.2px including DPI rounding/border. At 240px, background updates and tab switching preserved the 300px reading offset and restored the sticky position. A fresh-context review returned ACCEPT. Only isolated fixture windows were used.
