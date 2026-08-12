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

## Step 2 — implementation (pending)

## Step 3 — byte-identity headed evidence (pending)

## Step 4 — final consult + budgets + surprises (pending)

## Step 5 — contract verification (pending)

### Engine-review presence (T-2)

| step | spine_review_step call | result |
|---|---|---|
| 1 (plan) | (to be filled) | |
