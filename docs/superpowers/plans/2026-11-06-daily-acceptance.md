# Daily Workflow Acceptance

Scope: verify ordinary sending, recovery and reading; fix reproduced blockers. No Git diff feature, real test conversations, Play Mode toggles or another Unity launch.

- [x] Reproduce premature composer clearing with an inert JSON-RPC sink.
- [x] Keep text/images in a serialized composer draft through UI rebuild/domain reload. Preview textures remain runtime resources.
- [x] Clear only after server acknowledgment for send/queue/steer; retain draft on rejection, transport failure and reconnect. Block duplicate submission while awaiting acknowledgment. Keep the server-owned queue authoritative.
- [x] Complete transport callbacks on write failure. Connection replacement cancels the window's pending submission identity and preserves the draft, without replaying it or firing disposed metadata requests into a new session.
- [x] Verify failed/successful submissions, serialization, queue hydration and transport closure without sending an inference turn.
- [x] Measure isolated long history, large patch and streaming/buffer workloads; fix only reproduced blockers.
- [x] Mark Changes as recorded tool edits using the existing summary/tooltip, with no additional permanent row.
- [x] Run focused checks, fresh-context review and compile; preserve the user's active window/draft and commit locally.

Results: initial composer retention assertion failed against baseline and passed after acknowledgment-based
clearing. The expanded daily smoke passed send/queue/steer success and rejection, duplicate submission,
late acknowledgments, server queue hydration, first-turn failure, image serialization and transport write
failure. Fresh-context review found a P2 close/register race that could write after failure; the closed
check now always returns and send/close transitions share the send lock. The small fix is handled inline,
with no second reviewer. Workload construction/processing measurements: 200 turns 730ms, 200 deltas with
20 live redraws 385ms total/35ms slowest, 4000 terminal chunks 448ms with bounded output, 2000-line patch
1028ms first build. The latter is recorded as a remaining initial-load hotspot; this acceptance pass does
not introduce a virtualization/scheduling redesign. These are synthetic timings, not end-to-end FPS.

Final log inspection also reproduced a disposed-workspace initialization callback throwing from RequestThreads.
The fixture now queues that acknowledgment before disposal and fails on asynchronous Editor errors. The
regression failed before guarding Initialize callbacks by originating client identity and passed afterward.
Final daily validation produced no Editor errors; its two warnings deliberately exercise steer rejection
and write failure. Recompilation clears those injected diagnostics before the final clean-console check.
