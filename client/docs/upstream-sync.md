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

## 2026-08-23 - v6.8.3 -> v6.8.4 "Relapse"

Baseline `bbfe7f3f4` (v6.8.3) -> `85bc6c86c` (v6.8.4). Four commits, 31 files, +4467/-393. Merge
`6a8b4c724`, **zero conflicts**. Read-only boundary verified by subtree object identity:
`git rev-parse main:ConditioningControlPanel` == `git rev-parse HEAD:ConditioningControlPanel` ==
`93c726aa6`. `git diff --name-only HEAD~1 HEAD | grep ^client/` was empty - the merge touched no port
file. (The only `<<<<<<<` hits are byte patterns inside two `libvlc` binaries, not markers.)

Port health on the merged tree: `dotnet build client/CcpClient.sln -c Debug` = **0 Warning(s), 0
Error(s)**. `UpstreamPayloadInventoryTests` 19/19. `CitationNeedleTests`, `CitationSelfTestGateTests`,
`ExecutionCensusTests`, `UpstreamPayloadInventoryTests` together 47/47.

### The whole delta is Arcademy, and the important half is that upstream SHUT THE DOOR

Two of the four commits are Arcademy (`3372919d8`, `cb8c1fde7`); the other two are the release chore
and its merge. The user-facing notes are about auth and update repair, but **none of that touched code
this port has landed** - see the buckets.

`ArcademyHostService.DoorAvailable` is now a `static readonly bool = false`
(`ArcademyHostService.cs:116`), the Play card ships `Visibility="Collapsed"`
(`Views/Tabs/PlayTabView.xaml:1312`) re-asserted from that flag on every repaint
(`MainWindow/MainWindow.PlayTab.cs:106-112`), and `Launch()` refuses on the door *before* the T2 bar
(`ArcademyHostService.cs:136-141`). Upstream's reason, in its own words: Semester 1 "landed on main
(PR #241) ahead of its public reveal" and 6.8.4 "is an auth/stability patch that must ship those fixes
without also shipping an unannounced feature". A hide rather than a lockband, "because a lockband
advertises something the account could buy".

That is a **porting constraint, not a detail**: the port's open Arcademy row would have shipped this
surface visible. It now owns a door gate ahead of its T2 gate, and it also means the surface cannot be
headed-verified against the shipping app, because the door is shut there too.

### The guard that should have caught the payload growth, and could not

`Resources/web/arcademy/` went **88 -> 91 files** and `UpstreamPayloadInventoryTests` stayed green.
`fileCountAtBaseline` is recorded in the inventory as "record data, not an assertion", and the per-tree
manifest tests that do pin counts exist only for trees the client actually serves. The guard catches a
tree *arriving* and a tree *disappearing*, never a tree *changing* - which is the permanent state of
every not-ported tree. Corrected by hand to 91; filed as its own board row rather than papered over,
because hand-maintained counts are the exact failure the citation-regenerate work is removing elsewhere.

### Citation rot found, and correctly NOT attributed to this sync

Three port citations into upstream localization resolve to the wrong lines
(`Localization/Languages/en.json:3394`, `:4704`, `:4705`, cited by `Haptics/HapticGate.cs:89` and
`Features/Dtrh/DtrhGate.cs:45`). Re-verified while writing this: `msg_haptic_feedback_patreon_only`
is at `:3403`, nine lines off, and `:4704-4705` land on achievement text rather than the tier and
upgrade strings the citation names.

**A FOURTH ONE I NAMED HERE WAS MY OWN MISREADING, AND IT IS RETRACTED.** I listed
`Features/Goon/GoonProtocol.cs:122`'s `UpdateService.AppVersion (:319)` as rot. It is correct as
written: that `:319` is a BARE CONTINUATION of the enclosing `GoonHostService.cs` context the same
doc-comment establishes at `:318` and `:347`, and `GoonHostService.cs:319` reads
`appVersion = UpdateService.AppVersion,` exactly. I read the bare `:319` as pointing at
`UpdateService.cs` and did not check the enclosing context before writing it down - which is the
same failure mode this section is about, committed while documenting it.
**All four were already wrong at the v6.8.3 baseline** - checked with `git show <base>:<file>` rather
than assumed - so they belong to the existing citation-inventory and uncited-reference rows, not to
this sync. Recording the check matters as much as the finding: a sync that claims credit for
pre-existing rot makes the next one distrust its own ledger.

### Buckets

1. **New product surface:** none. Arcademy already had a row; this sync changed its *shape* (door shut,
   88 -> 91 files, `ArcademyHostService.cs` +51 lines with 13 new `ic_*` lexicon entries from the
   Impulse Control House Rules wave: `casino.js`, `pressure.js`, `trickster.js` added, plus
   `index/lex/render/style/tube2d/tube3d` edited).
2. **Parity drift on landed port code: no BEHAVIOUR drift, but one landed EVIDENCE break that the first
   pass missed.** `GoonGameCensusTests.EveryPinnedCitation_IsOnTheExactLineItClaims` went red on the merged
   tree: the Arcademy visibility block added 11 lines to `MainWindow/MainWindow.PlayTab.cs`, moving the two
   citations the goon census pins (`playtab-send-rung` 112 -> 123, `playtab-host-rung` 113 -> 124). Corrected
   in `client/docs/goon-game-census.md`. **The first pass of this ledger claimed bucket 2 was empty, and that
   claim was wrong** - it rested on a hand-picked subset of guards (inventory, citations, execution census,
   read-only tree) rather than on the suite. The lesson is the one the protocol already states and this pass
   did not honour: a sync is not verified until the WHOLE suite runs on the merged tree, because the guards
   that pin upstream line numbers are exactly the ones a line-shifting merge breaks, and they are not the
   guards a human thinks to name. No user-observable port behaviour changed. The other changed
   WPF areas are Arcademy (absent from the port - zero `arcademy` references under `client/`),
   `Services/Update/UpdateService.cs` (not ported; the port's version is deliberately `0.1.0` and
   `client/Directory.Build.props` states the client "must not impersonate" the WPF product's version, so
   the `6.8.3 -> 6.8.4` constant carries no obligation), the Play tab (Arcademy card only), and nine
   localization files whose entire key delta is `btn_v6_8_4_is_out` and `tooltip_v6_8_4_relapse` -
   neither referenced anywhere under `client/`.
3. **Smaller deltas:** the release chore (`csproj` version, `installer.iss`, `build-installer.bat`,
   `notes-v6.8.4.txt`) and the update-banner strings in nine language files.
4. **Gaps this sync exposed in the port's own guards:** the not-ported-tree drift blind spot above,
   filed as a board row.

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
