# SP-107 — the per-run verdict lists behind the before/after claim

Committed because the rate table in `record.md` §3 is the most load-bearing claim in this packet and
prose is not evidence. Every line is one launched `check-floor.mjs` run, in order, with its verdict.
**Nothing was ever re-run**; there is no run in these experiments that is missing from these files.

| File | Concurrency | Runs | Red |
|---|---|---|---|
| `before-sequential-20.log` | 1 | 20 | 0 |
| `before-concurrent3-12.log` | 3 | 12 | **8** |
| `after-concurrent3-18-round1.log` | 3 | 18 | 2 |
| `after-concurrent3-18-round2.log` | 3 | 18 | 0 |
| `after-sequential-20-round1.log` | 1 | 20 | 2 |
| `after-sequential-20-round2.log` | 1 | 20 | 0 |

BEFORE totals: 0/20 sequential, 8/12 concurrent. AFTER totals: 2/40 sequential, 2/36 concurrent,
**4/76 overall**. The BEFORE logs were taken on the unchanged tree at `3c1572b4`.

**How an AFTER run was scored.** After the fix the floor reports `total drift: 1476 (pin 1472)` on
every run by design — that is this packet's declared delta, which the orchestrator applies at land.
An AFTER run counts GREEN only when the TRX carries **zero** failed tests in BOTH projects AND that
drift is check-floor's only complaint. The harness records the drift line for every run so the
scoring can be checked rather than trusted.

**Two things these logs do not show.** The declared delta was `+4` while these runs were taken and is
`+5` after review (the headless-assembly no-probe fact was added afterwards), so a re-run today
reports drift to 1477, not 1476. And the four AFTER reds are all the same fact, whose mechanism
`record.md` §4 leaves open — they are not the flake this packet fixed.
