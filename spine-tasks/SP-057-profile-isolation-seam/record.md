# SP-057 record — Profile isolation seam (APPDATA trap + m2test fixture discipline)

Wave-16 lane-1. Review Level 2. This record is the worker-side evidence bundle; the
orchestrator reconciles the board row at land (ENABLER 2 — the worker never edits the
three hot docs).

## Step 1 — consumer census, trap proof, design, pre-approach consult

### Consumer census (grep-verified 2026-08-12, this worktree)

Every data-root consumer routes through `CompositionRoot.DefaultSettingsPath()`
(`CompositionRoot.cs:85`). Full `SpecialFolder` / `DefaultSettingsPath` /
`SettingsPathFactory` sweep of `client/src/**`:

| file:line | reaches the choke via | what it places under the data root |
|---|---|---|
| `Lifecycle/CompositionRoot.cs:101` | `SettingsPathFactory()` | `PersistenceStore<DemoSettings>` -> `settings.json` |
| `Lifecycle/CompositionRoot.cs:115` | `Path.GetDirectoryName(SettingsPathFactory())` | `DtrhSaveSlots` -> slot documents + active-slot index |
| `Lifecycle/CompositionRoot.cs:122` | same | `DtrhParticipant.DataDirectory` -> `Spirals/` (Loom store), `assets/` (user media) |
| `Lifecycle/CompositionRoot.cs:131` | same | `CompanionParticipant` -> AI memory store file |
| `Lifecycle/CompositionRoot.cs:185` | same | `atomic-filesystem` capability probe target dir |
| `Features/Dtrh/DtrhParticipant.cs:57` | `?? DefaultSettingsPath()` fallback | same data dir (constructor default) |
| `Features/Dtrh/DtrhProfileLock.cs:33` | `DefaultSettingsPath()` direct | `<dataDir>/dtrh/wv2-profile*` WebView2 profile dirs |
| `Features/Intake/IntakeParticipant.cs:42` | `?? DefaultSettingsPath()` fallback | intake data dir (settings/punch card/drafts/keepsakes, `intake_spirals`) |

Named NON-consumers (swept, not data roots):

- `Features/Dtrh/DtrhCapabilityProbes.cs:172-173` — `ProgramFilesX86` /
  `LocalApplicationData`: READ-ONLY WebView2 evergreen runtime locator. Never writes;
  not a profile data root. Whitelisted by the guard test's choke-point rule (it matches
  neither `ApplicationData` nor `UserProfile`).
- `Persistence/SecretStores.cs` — `WindowsDpapiSecretStore` takes `rootDirectory` from
  its caller; `PlatformSecretStore.ForCurrentPlatform` has no product caller yet (tests
  only, `SecretStoreTests.cs:92`). No live consumer.
- `Capabilities/SessionProbe.cs:35,37` — `WAYLAND_DISPLAY`/`DISPLAY` reads; no filesystem.
- `Features/Dtrh/DtrhMeta.cs` `FlagAbsentProgressionMembers` — reads via
  `slots.SlotFilePath()` (derived from the choke through `DtrhSaveSlots`).

**Bypass count: zero.** The SP-055 lesson (inventory said two, reality was three) is
why this table came from grep, not from the packet's citation list — the packet's list
matched reality this time, plus the two static `DtrhProfileLock` entry points.

### The APPDATA trap, proven in this repo

Minimal repro (`net10.0` console, the product's TFM), run 2026-08-12 on the owner
machine (Windows 10.0.26200, .NET 10.0.11):

```
APPDATA env: C:\tmp\appdata-trap-proof\fake
GetFolderPath(ApplicationData): C:\Users\Micha\AppData\Roaming
```

`APPDATA=` does NOT move `Environment.GetFolderPath(SpecialFolder.ApplicationData)` —
the API reads the shell's known-folder state, not the environment block. Every headed
run that "sandboxed" itself with `APPDATA=` was reading/writing the owner's real
profile. (Also reproduced on .NET Framework 4.8 via PowerShell — the trap is not
runtime-specific.)

Linux disposition: **named gate — no WSL distribution is installed on this machine**
(`wsl.exe -l` empty), so no Linux run was possible in this session. Per SP-010
(recorded in `CompositionRoot.cs:88-91`), .NET's Unix mapping of `ApplicationData` IS
`$XDG_CONFIG_HOME` (else `~/.config`), so the trap class does not exist on Linux; the
override still works there by construction (the env check precedes the platform
resolution) and the defaults are pinned by platform-conditional unit tests.

### Seam design (consult-amended)

- `CCP_DATA_ROOT` env var (the `CCP_MCP` precedent, `Program.cs:285`). No counter-case
  from the census; a CLI flag was rejected because the choke point is library-level
  (`DtrhProfileLock` statics never see `Program` args) while the env block reaches
  every consumer with zero plumbing.
- Honored **inside `DefaultSettingsPath()`** — the single function every consumer
  funnels through. Per-call env read, NO cache (consult A2: a cached static freezes on
  whichever test touches it first; per-call is ~free at 8 calls/startup).
- Factored pure core `internal static string ResolveDataRoot(string? overrideValue)`
  (consult A1) so validation tests never mutate the process-wide env block (xunit
  parallelism hazard); only the through-the-real-composition tests set the env, in a
  non-parallel collection with try/finally restore.
- Rules: unset/whitespace -> byte-identical default. Set -> must be
  `Path.IsPathFullyQualified` (rejects relative AND drive-relative `C:foo`, which
  `IsPathRooted` wrongly accepts), normalized via `Path.GetFullPath`,
  `Directory.CreateDirectory` attempted; any failure throws typed
  `DataRootOverrideException`. **No fallback code path exists** — a bad override cannot
  degrade into the real profile (framing d).
- Startup surface: the throw inside composition becomes `StartupOutcome.Failed(Fatal)`
  via `StartupPhaseRunner.cs:52-56` (exit 1 with the reason logged); `Program.Main`
  additionally validates pre-phase (stderr + exit 1, the `--ai-ollama-host`
  convention). When the override is ACTIVE, one startup log line names the resolved
  root (harness-side path only — the real profile path is never newly logged; consult
  A2 evidence line + privacy boundary).
- Future-bypass catch: guard test (FindRepoRoot precedent,
  `UpstreamPayloadInventoryTests.cs:98`) scans `client/src/**/*.cs` and fails if
  `SpecialFolder.ApplicationData` or `SpecialFolder.UserProfile` appears outside
  `Lifecycle/CompositionRoot.cs`.

### m2test declared-fixture design (consult-amended)

- The csproj is OUT of File Scope, so no new Content/EmbeddedResource glob. Consult
  verdict: **keep the fixture in-code** — verbatim JSON raw string literal in
  `Features/Dtrh/DtrhM2TestFixture.cs`, `Load(string json = DefaultFixtureJson)` so a
  future file swap is a one-liner. "Missing" is compile-impossible (const string);
  malformed -> typed `DtrhM2TestFixtureException`, pinned by a round-trip unit test.
- **Sentinel values, not serialized defaults** (consult b2 — SP-052 Run B was a
  confidently-wrong `dealt 7200/True` from live-profile inheritance): the fixture
  carries recognizable declared numbers (`sparks: 777`, `bestScore: 4242`,
  `runsCompleted: 3`) so any log/screenshot instantly reveals fixture-origin data.
  Values stay valid (no unknown ids).
- `DtrhMeta` ctor gains optional `DtrhSlotDocument? testFixture = null`; test mode is
  now `_testState = testFixture ?? DtrhM2TestFixture.Load()` — **the live-doc
  deep-clone path is deleted**. The product path (`DtrhHostWindow`) passes nothing, so
  headed m2test always starts from the committed fixture. The two existing test-mode
  tests (`DtrhMetaTests.cs:486,503`) declare their starting doc explicitly as the
  fixture argument instead of seed-then-clone.

### Consults

| checkpoint | mode | requested model | ACTUAL answering model | verdict |
|---|---|---|---|---|
| pre-approach | solo | (tool default roster) | not surfaced by the tool (see note) | approve direction + amendments A1/A2/b/c |

Note (T-2 honesty): the `consult` tool output does not name the answering model in
what it returns to the worker; the verdict and amendments above are recorded verbatim
in the two consult transcripts (this section summarizes). The first transcript was
truncated mid-amendment A1; the second (compact re-ask) supplied A2, (b), (c).

Consult amendments adopted: A1 pure `ResolveDataRoot`; A2 per-call env read + active-
override log line + non-parallel env-mutating collection; (b) in-code verbatim-JSON
fixture with sentinels + `Load(string json = ...)`; (c) evidence plan: full recursive
manifest with set-equality both directions + directory existence, positive controls
(settings.json, slot docs, slot INDEX, `dtrh/wv2-profile*`, resolved-root log line,
m2test bind line), claim scoped to `%APPDATA%\CcpClient` byte-identity (WebView2/
LibVLC may write under LocalAppData/Temp — outside the seam's claim, stated as a
limit), negative demonstration REASONED (trap proof + unset-env unit test), never
executed against the live profile.

### Rejected alternatives

- **Backup/restore around headed runs** — rejected at authoring (procedural mitigation
  already failed once, SP-052 Run A); re-confirmed here: code seam > procedure.
- **CLI flag instead of env var** — the choke point is library-level; a flag would need
  plumbing through `CompositionRoot` init-only properties AND still miss the
  `DtrhProfileLock` statics without globals. Env reaches all of it.
- **Per-caller patches** — framing (a); the whole point is one choke point.
- **Real .json fixture file via scope amendment** — consult (b): not worth it; the
  in-code literal lifts to a file with a one-line change if a future row wants it.

## Step 2 — implementation

- `Lifecycle/CompositionRoot.cs`: `DataRootOverrideException` (typed, extends
  `InvalidOperationException`); `CompositionRoot.DataRootOverrideVariable = "CCP_DATA_ROOT"`;
  `DefaultSettingsPath()` reads the env per call (no cache, consult A2) and delegates to
  `public static ResolveDataRoot(string)` (consult A1 — pure core, `IsPathFullyQualified`
  rejects relative AND drive-relative, `GetFullPath` normalize, `CreateDirectory` probe,
  every failure typed; NO fallback path exists); `ActiveDataRootOverride()` for Program.
  Defaults byte-identical when unset.
- `Program.cs`: pre-phase validation + exit 1 with usage on a bad override (the
  `--ai-ollama-host` convention); one `data-root override active: CCP_DATA_ROOT -> <path>`
  log line when the seam is live (headed positive control; harness path only).
- `Features/Dtrh/DtrhM2TestFixture.cs` (new): committed verbatim-JSON fixture
  (`DefaultFixtureJson`, sentinels sparks=777 / bestScore=4242 / runsCompleted=3),
  `Load(string json = DefaultFixtureJson)`, `DtrhM2TestFixtureException` on malformed.
- `Features/Dtrh/DtrhMeta.cs`: ctor gains `DtrhSlotDocument? testFixture = null`;
  test mode is now `_testState = testFixture ?? DtrhM2TestFixture.Load()` — the live-doc
  deep-clone is DELETED (and the now-dead `CloneOptions` with it). Class doc updated.
- `Features/Dtrh/DtrhHostWindow.axaml.cs`: two comment/log-line accuracy updates
  ("declared fixture", SP-057 named). No behavior change on the window path.
- Tests (all new under `client/tests/CcpClient.Tests/`):
  - `DataRootOverrideTests.cs` — pure validation (relative / drive-relative `C:foo` /
    uncreatable / valid+creates) + the unset-env platform-default pin (the reasoned
    negative demonstration).
  - `DataRootOverrideEnvTests.cs` (same file) — `[CollectionDefinition(DisableParallelization
    = true)]` env-mutating collection, try/finally restore: per-consumer honoring
    (choke point, `DtrhProfileLock` statics, both `?? DefaultSettingsPath()` fallbacks),
    a FULL real-composition boot (no `SettingsPathFactory` substitute) that mutates +
    saves and asserts `settings.json` / `dtrh_slot1.json` / `dtrh_slots.json` land in the
    override root, and bad-env throws typed at first path resolution.
  - `DataRootChokePointGuardTests.cs` — scans `client/src/**/*.cs`; any
    `SpecialFolder.ApplicationData`/`SpecialFolder.UserProfile` outside
    `Lifecycle/CompositionRoot.cs` fails with file:line (the future-bypass catch).
  - `DtrhM2TestFixtureTests.cs` — fixture round-trip + sentinels, malformed/null typed
    failure, test mode sources the committed fixture (not a 99999-spark live doc),
    explicit fixture wins.
  - `DtrhMetaTests.cs` — `Harness.ResetMeta` gains the fixture argument; the two
    test-mode tests declare their starting documents explicitly (same values the clone
    used to carry, so the payout pins are unchanged).
- Result: build 0W/0E; unit suite 846 passed / 0 failed (floor 833; +13 new).

## Step 3 — byte-identity headed evidence

Evidence bundle: `spine-tasks/SP-057-profile-isolation-seam/evidence/` —
`run.ps1` (the whole bracket), `manifest.ps1` / `diff.ps1`, `drive.ps1` (SP-026
generic driver + SP-057 `-X/-Y` placement params), committed transcripts and manifests.

**Run shape:** real headed `--dtrh-demo --dtrh-quick --dtrh-m2test --dtrh-auto-close 80`
with `CCP_DATA_ROOT=<evidence>/override-root`, WebView2 151.0.4129.72, full page boot
over the loopback, m2test driven page-originated to DONE, clean EXIT=0.

**Verdict (final run, committed `run-drive.log` / `diff-verdict.txt`):**
`DIFF VERDICT: BYTE-IDENTICAL (2677 files, set-equal both directions, all hashes match)`
— pre/post manifests of the real `%APPDATA%\CcpClient` (app not running at capture,
both directions set-equality + per-file length+SHA256 + directory existence; consult c
hole 1). Override root demonstrably populated: 310 files / ~26.5 MB including
`dtrh_slots.json` (the exact index file SP-052 Run A clobbered), `dtrh/wv2-profile`
(the WebView2 UserDataFolder rode the seam), plus the run.log positive controls:
`data-root override active: CCP_DATA_ROOT -> ...`, `M2 TEST MODE`, `meta engine bound
to slot 1 (TEST clone)`, `M2TEST DONE` (consult c hole 2 — byte-identity without these
would be vacuous). The override root itself was deleted after manifesting (26 MB of
browser profile is not committable evidence); `override-manifest.json` (plain paths —
sandbox content, no privacy surface) is the committed record of population.

**Privacy:** real-profile manifests are committed PATH-HASHED (sha256 of each relative
path; lengths + content hashes plain) — set-equality and byte-identity remain fully
verifiable, owner file names never enter git. `manifest.ps1` hashes by default;
`-PlainPaths` is for sandbox roots. Transcripts scrub the expanded owner profile path
to `%APPDATA%\CcpClient` (prior packets never committed the expansion). The headed
capture is committed only as a 140px header strip (`run-header-fixture-sentinel.png`):
the full-frame grabs incidentally included the owner's overlapping desktop windows and
were deleted.

**DISPLAY3 gate (named, honest):** the 2026-08-12 session has ONLY `\\.\DISPLAY1
(0,0) 2880x1800` attached — DISPLAY3 `(-2576,1091)` does not exist in this session's
virtual desktop (an off-screen SetWindowPos "verifies" trivially; the first capture at
the DISPLAY3 origin came back blank and is discarded as evidence). The final run placed
the window at a VISIBLE DISPLAY1 point (100,100), rect-verified, captured with real
content (dark=65.8%, ~337 distinct colors). The byte-identity claim does not depend on
the display; the DISPLAY3 convention is re-run material whenever the monitor is back.

**Fixture-origin proof on screen:** the header strip shows `Tempted` / 981 sparks /
5 gold after m2test: 981 = fixture sentinel 777 + the dry-run payout 204 —
fixture-origin arithmetic visible in the headed UI, matching
`payout-sparks sparks=204 dryRun=true` in run.log.

**The m2test `meta-commands` line reads FAIL (7/8) — explained, deterministic, NOT an
engine regression.** `rev +19 (expected 18)`. Replay of the exact 26-op m2test sequence
against the engine in isolation (fixture start) produces EXACTLY the modeled +18 — the
engine applies/rejects every op identically. The 19th bump is PAGE-ORIGINATED: a
`map-set narrativeCooldownEnds` from cheshireGuide firing a first-gold event line,
plus a pre-measurement burst (cheshire self-heal `set-num tutorialStage` + the reveals
framework queueing/flashing fresh unlocks: dollhouse/toybox/pill_teasing/draft_skip/
variant_* on a runs=3, zero-seen-reveals document). Every prior m2test run cloned the
OWNER's live document — completed arc, seen lines, populated reveal sets — so this
traffic never existed; the payload's expectation model (m2test.js:97-100, engine ops
only, parameterized on m0) never had to account for fresh-profile page chatter. An
instrumented headed run (temporary test-mode op log, reverted after) captured the full
op sequence proving this (`run-diag-instrumented.log` — 18 engine ops in the
measurement window + the 1 page op). Deterministic: +19 on all three headed runs. The
substantive checks all pass (meta-state gold=5/5 dial/flag/boon, crafting-p2, paperwall,
payout-xp-cap, payout-sparks). This IS the SP-052 hazard class made visible: evidence
keyed to a fresh declared state differs from evidence keyed to the owner's live state —
which is the point of the fixture. Future packets using this seam will see the same
7/8 with this explanation; if a fully-green m2test board is ever required, the fix
belongs to the payload's expectation model (read-only WPF tree — an upstream ask, not
a silent fixture tweak; over-fitting the fixture to impersonate a veteran profile was
considered and rejected as less honest).

**Linux/WSLg disposition:** named gate — this machine has NO WSL distribution
installed (`wsl.exe -l` empty), so no Linux headed run was possible. The trap class
does not exist on Linux (.NET's Unix ApplicationData mapping honors XDG_CONFIG_HOME —
SP-010) and the override precedes platform resolution by construction; the Linux
default shape is pinned by the unset-env unit test. First Linux-capable session should
re-run `run.ps1` under WSLg.

**Negative demonstration: REASONED, not executed** (PROMPT Do-NOT). Two legs:
(1) the net10 trap proof (Step 1) — `APPDATA=` does not move the resolution, so an
unsealed run lands in the real profile by construction; (2) the unset-env unit test
pinning `DefaultSettingsPath()` to the real per-user path. The census shows every
writer funnels through that one function, so "no override => real profile" needs no
live demolition against the owner's data.

**Claim scope (consult c):** the proven claim is `%APPDATA%\CcpClient` byte-identity.
WebView2/LibVLC may write under LocalAppData/Temp outside the seam's directory —
outside this claim, stated as a limit, not swept under.

## Step 4 — final consult + budgets + surprises (pending)

## Step 5 — contract verification (pending)

### Engine-review presence (T-2)

| step | spine_review_step call | result |
|---|---|---|
| 1 (plan) | called 2026-08-12T05:50 | SKIPPED engine-owned (SP-195), spawnFailed=false — not a failure |
| 2 (plan) | called 2026-08-12T06:05 | SKIPPED engine-owned (SP-195), spawnFailed=false — not a failure |
| 3 (plan) | (to be filled) | |
