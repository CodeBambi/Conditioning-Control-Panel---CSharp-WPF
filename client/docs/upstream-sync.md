# Upstream Sync Protocol

Use this protocol whenever the shipping WPF product changes and the greenfield client may have new
parity obligations. The WPF tree remains behavioral evidence; do not import its architecture,
platform implementation, or service topology into `client/`.

## Reconcile

1. Identify the upstream range and inspect its changed source, assets, localization, dependencies,
   and user-visible release notes.
2. Classify each change as already represented, a parity obligation, an intentional divergence, an
   owner decision, or irrelevant to the greenfield client.
3. Compare linked payload bytes and the committed payload inventory. A source change is not
   synchronized until the client inventory and copied output agree.
4. Add or update only the necessary task-board rows. Each row identifies behavior evidence,
   platform implications, privacy or security impact, and the verification needed to close it.

## Implementation Rules

- Work from narrow source evidence, including follow-up fixes and reversions when they explain the
  shipped behavior.
- Research current Avalonia and platform guidance before choosing a client mechanism.
- Preserve Windows and Linux as separate acceptance targets. Mark unavailable headed or manual
  evidence explicitly rather than inventing support.
- Keep product changes isolated from synchronization bookkeeping. Shared documents and test-floor
  metadata have a single owner.

## Verification

Run the relevant focused checks for the changed surface. For changes that require the standard
client gates, run:

```powershell
node client/tests/floor/check-warnings.mjs --cold
node client/tests/floor/check-floor.mjs
```

Run headed capture and interaction checks where the upstream change affects pixels, window behavior,
input, media, focus, scaling, compositing, or native integration. Update the task board with the
observed result or exact blocker; no historical log or workflow record substitutes for current
verification.

## 2026-08-22 - v6.8.1 -> v6.8.3

- Baseline pair: `87035e9a7` (v6.8.1) -> `bbfe7f3f4` (v6.8.3). Merge `fb89d087a`, 74 commits,
  97+ files. `feat/crossplatform..origin/main` is now 0.
- Conflicts: none. Zero unresolved paths, no markers. The `CCP.*` delete/modify class that used to
  dominate these merges stayed gone.
- Read-only tree: `HEAD:ConditioningControlPanel` == `main:ConditioningControlPanel` ==
  `ff79481dd`, byte-identical by subtree object identity. The port was untouched: nothing under
  `client/`, `spine-tasks/` or `.spine/` in the merge diff.
- Port health: warning gate 0W/0E cold across 4 projects. Floor moved to 2609 (one new guard, see
  below); the merge itself reddened four guards, all repaired in this same push.

### The four guards the merge moved, and what each cost

| guard | cause | repair |
|---|---|---|
| `GoonGameCensusTests` | 7 pinned §10.4 citations shifted | re-anchored, each verified by needle identity against the base bytes |
| `HapticSiteCensusTests` | the 5 decay-ladder constants shifted a uniform +56 | re-anchored; the `250` was READ at its line, never inferred |
| `UpstreamPayloadInventoryTests` | a new upstream payload tree arrived | `arcademy` recorded, disposition `not-ported` with a board row |
| `FypCensusTests` | a new upstream consumer of the remote-media subsystem | consumer set 17 -> 18 |

The haptic shift being uniform is exactly the evidence that makes a proximity guess feel safe, so
every constant was read at its own line. Six lines of `HapticService.cs` carry the token `250`; the
ladder's is `:842`, and picking the nearest instead of reading would have cited the wrong one.

### The dangerous bucket: 36 upstream fix() commits against landed port code

36 of the 74 commits are `fix()`. Upstream bug fixes to code the port already copied are the
category that hurts, because both sides stay green and neither says so. All 36 were triaged against
port bytes, then every inherited-bug verdict was put to an independent seat briefed to refute it.

### Buckets

1. **New product surface:** Arcademy - `Services/Arcademy/{ArcademyHostService,ArcademyMetaStore}.cs`
   plus `Resources/web/arcademy/` (88 files, a three.js campus shell). Absent from the port
   entirely: zero occurrences of `arcademy` under `client/`. Board row filed.
2. **Parity drift on landed code:** FOUR confirmed, each surviving an independent refutation seat: a Brain Drain clip cut off mid-track (P1), hosted pages losing motion when Windows animation effects are off (P1), Brain Drain clips never hot-reloading (P2), and Reveal in Explorer opening the Desktop (P2). The motion one is the finding a human sweep would have skipped - its subject names `justdrop`, a feature this port does not have, but the fix landed in the shared WebView2 host and governs all five of the port's hosted call sites. Filed as board rows; none is fixed. 30 closed NOT-PORTED, 2 already correct.
3. **Smaller deltas:** 12 `feat()`, 3 `chore()`, 2 `diag()`, plus the balance of the `fix()` set
   that closed NOT-PORTED.
4. **Gaps this sync exposed in the port's own guards:** the read-only-tree rule had no enforcement
   at all and had just been violated by a documentation sweep; `ReadOnlyWpfTreeGuardTests` now
   asserts subtree object identity directly instead of leaving it to a census to notice sideways.
