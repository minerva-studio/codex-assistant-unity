# Right-side Changes Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [x]`) syntax for tracking.

**Goal:** Add a read-only Changes view beside chat, sharing the right column with Subagents.

**Architecture:** CodexAssistantWindow owns right-column visibility, tab and width. A CodexChangesPanel renders references to the workspace's loaded turns and live turn and owns only UI selection. Console diagnostics stay separate from this data path.

**Tech Stack:** C# 9, Unity 6000.3 UI Toolkit, Newtonsoft.Json, existing app-server protocol.

**Spec:** `docs/superpowers/specs/2026-11-05-right-panel-changes-design.md`

## Global Constraints

- Reuse this task's checkout and open markdown-proto-2 Editor; branch feat/changes-panel from 50fb23a. Do not launch another Editor.
- Keep the conversation list on the left and transcript/composer in the center.
- One resizable right column, Changes/Subagents tabs; no duplicate conversation store.
- No file mutation, Git diff operations, new protocol methods, or persisted aggregate-diff cache.
- Preserve existing user work. Do not push or merge.

## Review Focus

- A live turn represented in both inputs appears once; repeated edits to a path remain separate.
- Failed/declined changes are labelled by their status and never implied to have applied.
- User-selected historical records and diff scroll survive unrelated stream updates.
- Live aggregate patches disappear when their source disappears; thread switches cannot show an old patch.
- Empty, archived and paginated histories remain navigable without inventing missing diffs.

## Task 1: Console diagnostic and Changes behavior checks

Files: `Editor/Dev/CodexChangesSmokeTest.cs`, existing console UI, temporary protocol bindings outside the repository.

- [x] Inspect current console state before changing it; validate command completion using cmd.exe /c echo and program-not-found using a nonexistent executable.
- [x] Add a focused smoke entry point exercising the proposed Changes panel against synthetic workspace turns: repeated paths, duplicate live turn, failed item, missing diff, pagination, thread switching and stable selection.
- [x] Run the smoke check before implementing the panel and observe the missing panel failure.
- [x] Replace protocol-only console help with product-level executable/argv guidance and a platform example. Preserve argv semantics.

## Task 2: Changes view and right-column integration

Files: create `Editor/UI/CodexChangesPanel.cs`; modify `Editor/UI/CodexAssistantWindow.cs`.

Interface: `CodexChangesPanel.Refresh(CodexWorkspace workspace)` reads `SelectedThreadId`, `Turns`, `LiveTurn`, `HasMoreTurns`, `IsLoadingMoreTurns`; pagination invokes the same workspace's `LoadEarlierTurns()`.

- [x] Build a turn selector, labelled change-record rows and a read-only, copyable, whitespace-preserving patch detail.
- [x] Key records by turn id, item id and change index. Treat an aggregate live patch as a separate record. Do not merge path duplicates.
- [x] Default to the newest turn containing recorded changes, keep valid selected turn/record, and clear selection on thread switches. Refresh only changed list/detail content.
- [x] Replace agents-only layout state with shared right-column state. Provide Changes and Subagents tabs, a persistent Changes toolbar entry, and retain Agents as a shortcut with its counts.
- [x] Reuse the old width preference value under its existing EditorPrefs key and support a wider diff-reading range. Keep 320px minimum transcript width.
- [x] Refresh Changes from both workspace and transcript events and preserve explicit close/tab choices through live updates.
- [x] Run the smoke check to green and compile in the existing Editor.

## Task 3: Acceptance and handoff

Files: `HANDOFF.md`, `README.md`, `ROADMAP.md`, `CHANGELOG.md`.

- [x] Exercise both right-side tabs, closing/reopening the column, resizing, live aggregate completion and history pagination through targeted Editor scripts.
- [x] Capture and inspect the actual window, showing representative fixture data in an isolated preview window when the selected real conversation has no changes. Clearly label fixture images.
- [x] Read current compile state and filtered new console errors; distinguish pre-existing errors from this change.
- [x] Obtain one fresh-context, read-only review of the complete change. Fix actionable findings and rerun focused checks.
- [x] Update documentation, commit scoped changes, and deliver the result without pushing or merging.

## Execution notes

- User explicitly requested continuous implementation; no additional planning approval gate.
- UI-only styling does not get implementation-mirroring unit tests. The focused smoke check covers observable data/selection behavior.
- Initial selected conversation has six turns and no recorded fileChange items.

## Execution record

- Base: 50fb23a; checkout reused on feat/changes-panel; existing Editor PID 50776, project markdown-proto-2, Unity 6000.3.25f1. No Editor launched and no conversation message sent.
- Console diagnosis: the existing ls request had finished with program-not-found. A real cmd.exe /c echo request streamed output and exited 0; a nonexistent executable returned a failure and cleared Running. UI guidance now names valid Windows shell invocations.
- RED: the focused smoke check first failed because CodexChangesPanel did not exist. GREEN: empty data, repeated paths, failed/declined records, explicit history selection, live/history deduplication, missing patch, thread transition and pagination passed.
- Fresh-context review found one P2: completed LiveTurn can retain a synthetic aggregate while history hydration is pending or fails. The regression was observed failing, then fixed by requiring workspace.IsRunning for the aggregate. The updated smoke check passed with the completed live object still present.
- Layout checks: tab hide/show restored patch offset 150; a background patch update retained offset 150; Subagents/Changes switching and closing/reopening passed. A 900px layout retained at least 320px for chat. Monospace font resource was verified locally.
- Actual-window and explicitly labelled sample-data PNGs were captured and inspected. The isolated sample window was closed afterward. The user's selected conversation was unchanged and Changes was left open.
- Ruling: current conversation has no fileChange records, so illustrative patch screenshots use an isolated synthetic workspace rather than modifying real history.
- Ruling: reuse the task checkout on a new feature branch, preserving the live local-package reference; no new worktree or Editor import was needed.
- Ruling: use focused smoke/layout checks rather than Unity Test Runner or broad suites. Existing historical Pipeline errors are distinct from current ground-truth console and compilation status.
