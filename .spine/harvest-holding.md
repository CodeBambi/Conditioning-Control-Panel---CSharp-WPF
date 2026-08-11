
## Wave 14 (SP-054) — fold at land

- **Transient provider 500 killed the worker AND the watcher (2026-08-11 18:27 UTC).** `500 {"error":{"type":"api_error"...}}` — Anthropic server-side, not quota, not the billing-header class (the patched route probed 200 four hours later with no change). Salvage inspection: `salvageable:false, changedFileCount:0` — MISLEADING as a work signal: the worker had COMMITTED Steps 1-2 (`0efd2304`, `0b2e865d`), so the worktree was legitimately clean. **Lesson: read the lane BRANCH log before trusting `salvageable:false`; a clean worktree after a death means committed work, not lost work.** Recovery = `retry SP-054` + `resume --force`; the replacement worker read its own STATUS.md and continued at Step 3 (22:03 UTC).
- **Same-window kill pattern, second occurrence** (kimi-403 was the first): a provider outage takes the worker and any pi-based watcher together, so a dead watcher is a signal to check the batch, never evidence the batch is fine.
- Step 2 landed 795/795 + 33/33 (112 new tests over the 683 floor).
