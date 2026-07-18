# Port lessons — second attempt (living ledger)

Append-only, dated, one-to-three lines per entry. Harvested by the steering loop during batches (see `port-session-prompt.md` §Steering loop). Every spine worker reads this file via `referenceDocs` — keep it short and current; prune entries that are superseded or encoded into a skill/agent/constitution rule (note where they went). This is attempt-2 operational learning; WPF/attempt-1 lessons live in `first-attempt-lessons.md` and `first-attempt-systemic-lessons.md`.

## Entries

- 2026-07-18 — Windows worktree lanes need `git config core.hideDotFiles false` (hidden `.git` gitfile EPERMs pi-spine path rewrite). Applied repo-wide.
- 2026-07-18 — pi-spine fsync bug on Windows patched locally in `node_modules` (`abort.mjs`/`lifecycle-archive.mjs` open flag `"r"`→`"r+"`); does NOT survive reinstall — re-apply after any pi-spine update until fixed upstream.
- 2026-07-18 — Stub workers write only `.DONE`: packets with `fileScopeMustChange` cannot pass contract under `SPINE_WORKER_STUB=1`; use stubs only to smoke packet parsing.
- 2026-07-18 — bpx-consult solo routes: only `anthropic/claude-fable-5` engages outside interactive windows; `uva/*` providers are not registered in child contexts. Keep solo default on Fable.
- 2026-07-18 — Project Avalonia baseline is empirically 12.1.0 on net10.0 (SP-001); Avalonia MCP validation is 11.3.x-pinned — treat its version-specific hints skeptically.
- 2026-07-18 — SP-001 worker skipped STATUS.md updates and in-worker consults during manual-assisted execution; packets must state these as explicit checkboxes, and reviews should check them.
