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

- Step 3: `spine_review_step` — engine-skipped (SP-195, same). Artifact: `.reviews/3-20260813T014543.md`.
- Step 4/5: single final `spine_review_step` call at completion (step 4) — pending.

## Step 5 — verification

- Contract testCommand green as suite run R3 on the final code tree: `verify.mjs` exit 0
  (all patches, both roots), build 0W/0E, **897 unit / 35 headless, 0 skipped**, TRX under
  `evidence/trx/`.
- `git diff --check` clean (only LF→CRLF advisories).
- `git status --short` / scope audit: only File Scope paths touched
  (`Program.cs`, `Lifecycle/HarnessEntryPoints.cs`, the two test files, this task folder).

## Honesty cell — what this does NOT close

1. **The `--dtrh-demo --dtrh-auto-close 30` residual hole (and the whole demo+modifier
   class):** a demo flag plus an auto-close/picker-timeout modifier is an unattended,
   evidence-shaped run that this gate still permits, because the row's decree protects
   demo flags from refusal. `--avatar-corrupt-demo` sits in the same bucket (class 2 by
   decree + in-memory-only corruption). Named for the owner; NOT unilaterally extended.
2. **Only the data root is protected.** WebView2 (`%LOCALAPPDATA%`-adjacent user-data
   dirs outside the override when a path is hardcoded — none known today), LibVLC, and
   `%TEMP%` writes are outside SP-057's claim scope and outside this gate's.
3. **The guard binds the literal surface it scans and nothing else:** `"--..."` string
   literals in `client/src/**/*.cs`. A flag spelled via string concatenation,
   interpolation, or `nameof` composition would evade the regex; a flag consumed only in
   `.axaml` markup is unscanned (none exist today — the axaml hits are WebView2 args in
   code-behind `.cs`).
4. **A harness path invoked by something other than `Program.Main` is unprotected** —
   e.g. a future test host or tool constructing `CompositionRoot` directly with harness
   knobs. The gate rides the real product entry point only.
5. **Linux is unproven here:** zero WSL distros on this machine; no Linux run was faked.
   The gate itself is platform-neutral C# (env read + early return), but no headed or
   headless Linux evidence exists for it.
6. **Token-wise scanning:** the gate matches whole argv tokens against the registry; a
   harness flag appearing as another flag's VALUE also refuses (conservative, intended).
   `CCP_DATA_ROOT=""` (empty/whitespace) also refuses — `ActiveDataRootOverride()` treats
   it as unset; conservative and intended.
7. **This is a deliberate BEHAVIOR BREAK for existing tooling/scripts** that launched
   `--dtrh-m2test` / `--dtrh-fx-drive` / `--loom-drive` / `--intake-drive` /
   `--tunnel-drive` / the kill/block injectors unsealed: they now exit 3. Every headed
   evidence script must set `CCP_DATA_ROOT` (SP-057's run.ps1 already does). Filed for
   the orchestrator below.

## Intended board/doc filings (orchestrator reconciles at land — worker sets no row state)

- `client/docs/task-board.md`: the SP-064 row — evidence: gate + registry + guard landed;
  refusal exit 3 real-process proven; profile byte-identical (2677 files, both
  directions); floor now **897 unit / 35 headless, 0 skipped**; board row 49's
  skip/count check is the successor and must pin the NEW count.
- `client/docs/port-lessons.md`: (1) opt-in isolation seams decay into mandatory ones —
  procedural mitigations fail (SP-052 class); (2) a guard over flag literals bites its own
  registry's doc comments — write placeholders unquoted; (3) cold worktrees at this depth
  need `git -c core.longpaths=true worktree add` (legacy WPF asset paths overflow
  MAX_PATH) and `rm -rf` for removal.
- `client/docs/port-workflow.md` §204 rule: the `CCP_DATA_ROOT` rule can now read
  "mandatory for harness entry points — the app refuses (exit 3) when it is unset".

## Step 3 — real-process evidence (four bounded runs; `evidence/run.ps1`, transcript in
`evidence/run-transcript.log`, OVERALL VERDICT EXIT=0)

Machine posture: DISPLAY3 absent (only DISPLAY1 2880x1800 attached) — loud fallback to
(100,100) per the SP-057 amendment, named here. Zero WSL distros: Linux unproven (honesty cell).

- **(a) refusal, real process, unsealed:** `CcpClient.Desktop.exe --dtrh-m2test`,
  `CCP_DATA_ROOT` unset → **exit 3** within 15s; stderr (`run-a-refusal.stderr.log`):
  `refusing to start: --dtrh-m2test is a HARNESS-ONLY entry point ... and CCP_DATA_ROOT is
  not set — set CCP_DATA_ROOT=<fully-qualified absolute directory> ...`. No process/window
  survives the refusal. (The `—` renders as a replacement char in the redirected file —
  console OEM codepage artifact of redirection only; the source string is intact.)
- **(b) real profile untouched by (a):** path-hashed manifests of `%APPDATA%\CcpClient`
  before/after (2677 files each) → `diff-refusal-verdict.txt`: **BYTE-IDENTICAL,
  set-equal both directions, all hashes match**. Positive controls (a crash-at-startup
  also leaves the profile untouched, so byte-identity alone is vacuous): exit code == 3,
  stderr names the variable + the flag + HARNESS-ONLY, no surviving process — all True.
- **(c) sealed harness run still works:** same flag (`--dtrh-demo --dtrh-quick
  --dtrh-m2test --dtrh-auto-close 60`) with `CCP_DATA_ROOT` set to a TEMP scratch root →
  NOT refused; stderr carries `data-root override active: CCP_DATA_ROOT` and the m2test
  signal `M2 TEST MODE`; exit 0 via auto-close. Scratch root manifest (plain paths,
  `sealed-root-manifest.json`, 310 files): `dtrh_slots.json`, `dtrh/`, `wv2-profile*`
  present (SP-057's control set). `settings.json` absent — EXPECTED (no DemoSettings
  mutation in the run; SP-010 observed even a fresh plain launch creates none); named,
  not suppressed.
- **(d) plain launch non-regression, unsealed:** no args, no `CCP_DATA_ROOT` → NOT
  refused; `CCP Client` window rect-verified at (100,100)-(1034,1354) [934x1254] on
  DISPLAY1 (capture `run-d-plain-window.png`, dark=81% = the dark theme, 76 distinct
  colors — not a black surface); CloseMainWindow → **exit 0**. Profile delta across (d):
  **BYTE-IDENTICAL** (2677 files, both directions) — the SP-010 expectation held.

### Suite runs (new exact floor: **897 unit / 35 headless, 0 skipped**)

| Run | Worktree | Cold/warm | Unit | Headless | Skipped |
|---|---|---|---|---|---|
| R1 | lane-1 | warm | 897 passed | 35 passed | 0 / 0 |
| R2 | cold-sp064 (fresh `git worktree add --detach a5ca1d8e`, first-ever build; needed `-c core.longpaths=true` — a legacy WPF asset path overflows MAX_PATH at this depth; worktree removed after) | **cold** | 897 passed | 35 passed | 0 / 0 |
| R3 | lane-1 (full contract: verify.mjs exit 0, build 0W/0E) | warm | 897 passed | 35 passed | 0 / 0 |

TRX attached per run under `evidence/trx/` (sp064-r1/r2-cold/r3 + step2 interim).
All output redirected to files, never tailed. **Ordering:** R3 ran the full contract
command on the final CODE tree; only `record.md` / `STATUS.md` / evidence files were
edited afterward (plus the run-(d) capture correction below).

**Run-(d) capture correction (pre-completion consult):** the first run.ps1 pass saved a
CopyFromScreen PNG at the verified rect — it showed overlapping TERMINAL windows (the CCP
Client window was beneath them), i.e. owner session content that is neither app evidence
nor committable. The PNG was deleted uncommitted-from-evidence (removed from git), the
script now uses `-Action dump` (UIA tree + the verified GetWindowRect line), and the rect
proof stands on the transcript line `GetWindowRect: (100,100)-(1034,1354) [934x1254]` —
drive.ps1 exits 2 when placement does not hold.

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
- **Pre-completion (Step 4), mode: solo.** Verdict: **no blocker, proceed to `.DONE`**
  after the mechanical Step 5 items. The tool again surfaced **no answering-model
  attribution** (recorded, never invented). Guidance followed: (1) ran `git diff --check`
  (clean) + scope audit (`git diff --name-only f8b9214d..HEAD` = exactly the 4 in-scope
  product/test files + this task folder; no bin/obj; no Sp064RedProbe resurrection);
  (2) ordering sentence added to the suite table; (3) run-(d) PNG privacy defect fixed
  (deleted; script now dumps UIA + rect only); (4) honesty cell now names the
  tooling behavior break and the empty-string `CCP_DATA_ROOT` refusal.

## Engine plan reviews (Review Level 2 — T-2 heading presence recorded per call)

- Step 1: `spine_review_step` — engine-skipped (SP-195: nested reviewer spawn blocked in-worker; the batch engine runs reviews after `.DONE`). Artifact: `.reviews/1-20260813T011635.md`.
- Step 2: `spine_review_step` — engine-skipped (SP-195, same as Step 1). Artifact: `.reviews/2-20260813T012533.md`.
