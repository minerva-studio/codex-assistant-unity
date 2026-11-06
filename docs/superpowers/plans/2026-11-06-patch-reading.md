# Patch Reading Implementation Plan

> Execute inline in the existing checkout and Editor. Scope is the three requested reader improvements only.

**Goal:** Add source line numbers, syntax colors and collapsible unchanged context to the shared patch reader.

**Architecture:** CodexPatchView parses the supplied patch into source rows once. Rows own old/new coordinates and the original source text; Patch remains the untouched copy source. Syntax coloring uses the existing ColorCode dependency on reconstructed before/after hunk contents, resetting at omitted regions and file boundaries. The view renders compact gutters and escaped highlighted text. Runs of more than eight provided context lines hide their middle, retaining three lines at each end; a local expand/collapse control reveals exactly those supplied lines. No current-file reads or cumulative diff reconstruction.

**Files:** CodexPatchView.cs owns parsing, coloring and reader controls. CodexChangesPanel.cs and CodexItemRenderer.cs supply actual file paths for language detection. Existing Patch/Changes smoke checks cover both consumers.

- [x] Pin missing line numbers with a failing focused check.
- [x] Add old/new coordinates for unified hunks and whole-file add/delete; retain unknown coordinates as blank.
- [x] Reuse ColorCode languages and palettes, preserving literal source and independent before/after lexical state.
- [x] Add expandable unchanged-context runs; preserve expansion across unchanged refreshes and reset it when scope changes.
- [x] Exercise zero-length/omitted hunk ranges, CRLF, blank lines, diff-looking source, markup literals, unknown languages, multiline comments, multiple files and folding.
- [x] Verify actual wrapped layouts and sticky-header geometry in the existing Editor, request fresh-context review, update documentation and commit locally.

No terminal, Git workspace comparison, Unity tools, protocol features or cost tracking are part of this change.

Validation: the initial source-coordinate check failed against the previous reader. Unity 6000.3 compilation then passed with zero errors/warnings, and focused Patch and Changes checks passed. A fresh-context review found a P2 collision between equally sized context blocks with unavailable coordinates; its regression failed first, then passed after adding a source-row position to that identity. No second reviewer was dispatched for the small fix. Isolated 560px and 320px layouts show syntax colors, old/new gutters and 14-line context expansion. At a 300px scroll after expansion, the original file header remains pinned and wrapped code fits the viewport. A literal containing color/noparse/bold-looking tags retains its source string and measures identically to an equivalently preformatted plain label (408px); generated highlighting does not interpret those source tags. No conversations or user files were modified during fixture validation.
