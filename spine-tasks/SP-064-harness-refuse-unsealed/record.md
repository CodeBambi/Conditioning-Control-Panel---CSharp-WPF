# SP-064 — Harness entry points must REFUSE to run unsealed — record

Wave 21, single lane. Lane worktree: `.worktrees/spine-20260813T010705/lane-1`.

## Step 1 — disposition table (built from the tree, grep-verified 2026-08-13)

Enumeration command: `grep -rn -E '"--[a-z0-9-]+"'` plus a `"--[^"]*"` sweep over
`client/src/**/*.cs`. Every `"--..."` string literal in `client/src` is listed below;
nothing was carried over from the Mission's list unchecked. The Mission's class-4 list
named `--no-video-title-show` as a startup modifier — the tree shows it is a **LibVLC
constructor arg** (LibVlcDtrhVideo.cs:144), never a program arg. It and four sibling
non-startup literals get the registry's fifth bucket (`NotAStartupFlag`).

### Class 1 — HARNESS (refuse when CCP_DATA_ROOT is unset)

| Flag | file:line | What it writes to the REAL profile if run unsealed |
|---|---|---|
| `--dtrh-m2test` | Program.cs:191 | Boots the full composition root against `%APPDATA%\CcpClient`: `settings.json` saves, `dtrh_slots.json` / `dtrh_slotN.json` round-trips (the exact SP-052 slot-1 clobber class), `dtrh/wv2-profile*` dirs |
| `--dtrh-fx-drive` | Program.cs:187 | Same root boot + timed raw page JSON through the real dispatch — save/payout traffic lands in the real slot documents |
| `--loom-drive` | Program.cs:200 | Root boot + scripted pointer through the engine; loom file/save traffic in the real profile |
| `--intake-drive` | Program.cs:213 | Root boot + raw page JSON (quiz-result/intake-close/loom-file) — intake state + loom files in the real profile |
| `--tunnel-drive` | Program.cs:226 | Root boot + timed topmost/close/show steps over the real DtrhVideoWindow surface |
| `--dtrh-kill-renderers` | Program.cs:115 | Root boot + kills profile-matched WebView2 children and re-arms on relaunch — corrupts the real `wv2-profile` mid-write |
| `--dtrh-block-route` | Program.cs:116 | Root boot + loopback 403 injection (takes a prefix value) — the app persists degraded-state outcomes into the real profile |
| `--intake-kill-renderers` | Program.cs:214 | Root boot + W17 watchdog-relaunch injection on the intake profile |

Program.cs:110-116 already labels the first two injectors "HARNESS-ONLY failure
injection" in its own comment; the drives are labeled HARNESS-ONLY at their parse sites.

### Class 2 — DEMO / INSPECTION (must NOT refuse — row decree)

| Flag | file:line | Why a human running it unsealed is legitimate |
|---|---|---|
| `--popup-demo` | Program.cs:157 | WSLg demonstrator popup; writes nothing a normal launch wouldn't |
| `--avatartube-demo` | Program.cs:162 | AvatarTube demonstrator; normal-launch writes only |
| `--avatar-corrupt-demo` | Program.cs:163 | Corrupts the pulse pack **in memory only** (typed undecodable-asset path); fabricates nothing persisted — a human observing the failure path against their real profile loses nothing. **Boundary call (consult-confirmed): evidence intent does not override the demo decree; the corruption never reaches disk.** Named in the honesty cell |
| `--dtrh-demo` | Program.cs:175 | The human DTRH flow; the row explicitly protects demo flags |
| `--dtrh-quick` | Program.cs:177 | Skips the save picker (Quick Start outcome); the Mission lists it in class 2 — behaviorally it is a demo modifier, and class 2 vs 4 differs only in the verdict label, not the gate |
| `--loom-demo` | Program.cs:199 | The Loom studio demonstrator |
| `--intake-demo` | Program.cs:212 | The Graded Intake demonstrator |
| `--tunnel-demo` | Program.cs:225 | The chaos tunnel demonstrator |

### Class 3 — PRE-PHASE SELF-CHECK (must NOT refuse; return before any phase)

| Flag | file:line (definition; consumed in Program.cs:22-82) | Reach |
|---|---|---|
| `--verify-assets` | AssetManifest.cs:379 | Reads the embedded manifest attribute; no composition root, no profile |
| `--version` | VersionSelfCheck.cs:15 | Reads the InformationalVersion attribute; no profile |
| `--generate-avatar-packs` | AvatarEvidence.cs:19 | Writes only to a caller-named directory arg |
| `--avatar-strip-decode` | AvatarEvidence.cs:17 | Reads a caller-named bmp; stdout JSON only |
| `--avatar-sequence` | AvatarEvidence.cs:18 | Reads caller-named samples/pack files; stdout verdicts only |

### Class 4 — MODIFIER (no independent verdict; cannot launch alone)

`--capture` (Program.cs:57), `--pack` (:73), `--trace` (:80), `--ai-ollama-host` (:123),
`--avatar-animate` (:165), `--avatar-trace` (:166), `--dtrh-page` (:176),
`--dtrh-picker-timeout` (:178), `--dtrh-auto-close` (:181), `--loom-auto-close` (:201),
`--intake-auto-close` (:215), `--tunnel-auto-close` (:227), `--scan` (AvatarEvidence.cs:22).

### Class 5 — NOT-A-STARTUP-FLAG (registry bucket so the guard binds them too)

| Literal | file:line | What it actually is |
|---|---|---|
| `--no-video-title-show` | LibVlcDtrhVideo.cs:144 | LibVLC constructor option |
| `--avcodec-hw=none` | LibVlcDtrhVideo.cs:144 | LibVLC constructor option |
| `--autoplay-policy=no-user-gesture-required` | DtrhHostWindow.axaml.cs:639, DtrhLoomWindow.axaml.cs:151, IntakeHostWindow.axaml.cs:246 | WebView2 AdditionalBrowserArguments |
| `--disable-direct-composition-video-overlays --disable-features=CalculateNativeWinOcclusion` | ChaosTunnelWindow.cs:164 | WebView2 AdditionalBrowserArguments (one two-option string literal) |
| `--label=ccp-client` | SecretStores.cs:157 | secret-tool argv label |

## Step 2 — implementation

- `Lifecycle/HarnessEntryPoints.cs`: the ONE registry (39 entries: 8 Harness, 8 Demo,
  5 SelfCheck, 13 Modifier, 5 NotAStartupFlag), pure `HarnessFlagsIn`,
  `RefusalExitCode = 3`, `RefusalMessage` (names the flags +
  `CompositionRoot.DataRootOverrideVariable`).
- Gate in `Program.Main` immediately after the SP-057 override block, before
  `new CompositionRoot`: unsealed + any Harness flag → stderr message, `return 3`.
- `HarnessEntryPointGateTests` (4 facts, pure — no env mutation, no ProcessEnvCollection
  need): table-driven refusal/allow over the registry, harness-set pinned at the exact
  eight, message content, unknown-arg tolerance.
- `HarnessEntryPointGuardTests` (1 fact): every `--flag` literal under client/src
  classified (file:line violations, never-skip) + stale-registry reverse check + the
  wiring assertion (gate call after `ActiveDataRootOverride()`, before `new CompositionRoot`).
- **Guard RED captured:** `evidence/guard-red.txt` — injected `--sp064-red-probe` failed
  the guard with `Sp064RedProbe.cs:6` named; probe then deleted. The first guard run also
  bit the registry's own doc comments (quoted `"--..."` placeholders) — reworded; the
  guard binds its own registry file, as designed.
- **New exact floor: 897 unit / 35 headless, 0 skipped** (892 + 5 new facts). Interim
  runs: unit 897/0/0, headless 35/0/0 (`evidence/step2-*.log` + TRX).

## Step 1 — gate design (consult-checked)

- **Registry (one place):** `client/src/CcpClient.Desktop/Lifecycle/HarnessEntryPoints.cs` —
  `enum EntryPointDisposition { Harness, Demo, SelfCheck, Modifier, NotAStartupFlag }`,
  a static `IReadOnlyDictionary<string, EntryPointDisposition>` (Ordinal), a pure
  `HarnessFlagsIn(string[] args)` selector, `const int RefusalExitCode = 3`, and
  `RefusalMessage(flags)`. The gate and the tests both consume this; no second copy.
- **Insertion point:** `Program.Main`, immediately after the SP-057 override-validation
  block (after Program.cs:108's closing brace) and before `new CompositionRoot` (:131).
  Verified write-free up to that point: `InstallPanicHooks` + `DebugLogSink` write only
  to Console.Error/Debug (CompositionRoot.cs:13-20 — no disk), `ActiveDataRootOverride()`
  only reads env, `ResolveDataRoot` only runs (and creates a directory) when the override
  IS set — i.e. exactly the sealed case where the gate does not refuse.
- **Refusal behavior:** stderr message naming each offending flag AND
  `CompositionRoot.DataRootOverrideVariable` (the const, never a retyped string);
  `return 3`. Exit 3 is distinct from 1 (usage/startup failure) and 2 (panic);
  `grep -rn 'return 3' client/src` is empty today, and the pins assert the exact code.
- **`--verify-assets --dtrh-m2test` consequence (named, accepted):** the self-check at
  Program.cs:23-26 returns BEFORE the gate; the m2test token is silently ignored and the
  run is a bounded asset check that never constructs the composition root and never
  touches the profile. The packet mandates the insertion point after the SP-057 block, so
  this combination gets a self-check run instead of a refusal — harmless to the profile,
  named here rather than "fixed" by moving the gate (consult-confirmed).
- **Guard design:** `HarnessEntryPointGuardTests` walks `client/src/**/*.cs` from the
  repo root (FindRepoRoot throws when unresolvable — never skips, the
  DataRootChokePointGuardTests shape), extracts every `"--[^"]*` literal, and fails with
  the offending file:line when a literal is absent from the registry. A second assertion
  in the same fact pins the wiring: in Program.cs the `HarnessEntryPoints.HarnessFlagsIn`
  call must appear after `ActiveDataRootOverride()` and before `new CompositionRoot`
  (keeps the gate on the real entry point in the right order without an in-process
  `Program.Main` call — see consult note below). The same fact also fails on a STALE
  registry entry (classified flag no longer present as a literal in the tree).
- **RED demonstration:** an unclassified `--sp064-red-probe` literal injected into a src
  file, guard failure output captured to `evidence/guard-red.txt`, injection removed.

## Consults

- **Pre-approach (Step 1), mode: solo.** The tool returned **reasoning only — no final
  verdict text and no answering-model attribution was surfaced** (same shape as the
  authoring consult recorded in PROMPT.md Amendments). Recorded, never stitched. The
  reasoning's substantive guidance, all followed: (1) `--avatar-corrupt-demo` stays class
  2 — in-memory-only corruption, demo decree stands, name it in the honesty cell;
  (2) guard scans ALL of `client/src/**/*.cs` with the `NotAStartupFlag` bucket (a
  Program.cs-only scan is evadable via consts defined elsewhere), optional stale-entry
  reverse check "if free" (included — it was free); (3) `--verify-assets --dtrh-m2test`:
  name the consequence, do not move the gate; (4) exit 3 fine — pin it exactly and grep
  that nothing else returns 3 (verified empty); (5) verify nothing before the gate writes
  to disk (verified: DebugLogSink is Console.Error/Debug-only); (6) **do NOT call
  `Program.Main` in-process from a unit test** — a gate regression would hang the suite
  or write the real profile from the test host; pin the wiring with the source-shape
  assertion in the guard instead, and let Step 3's real-process run be the behavioral
  proof. Consequence: no test in this packet mutates process env, so no
  ProcessEnvCollection additions (SP-062 untouched).
- **Pre-completion (Step 4):** pending.

## Engine plan reviews (Review Level 2 — T-2 heading presence recorded per call)

- Step 1: `spine_review_step` — engine-skipped (SP-195: nested reviewer spawn blocked in-worker; the batch engine runs reviews after `.DONE`). Artifact: `.reviews/1-20260813T011635.md`.
- Step 2: pending call.
