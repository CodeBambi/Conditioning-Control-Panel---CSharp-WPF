# SP-045 record — DtrhFxRouterTests ManualClock hygiene

**Scope executed:** test-file hygiene only. Zero assertion changes, zero product change, zero new tests. Closes the SP-043 `record.md` §7 item 4 discovery.

## 1. Constructions found + injected (class-wide proof)

`grep -c 'new DtrhNativeEffects(' client/tests/CcpClient.Tests/DtrhFxRouterTests.cs` → **1**.
The single construction is in the shared `Make()` factory (the `:34` construction named in the
SP-043 discovery); all **5** `[Fact]`s route through `Make()`. Injecting in `Make()` *is*
class-wide injection — there is no second construction to miss.

Injection (diff summary, commit `d483b314`):
- Added `using CcpClient.Desktop.Audio;` (`ISoundClock` lives at `client/src/CcpClient.Desktop/Audio/AudioSeams.cs:113`).
- `Make()` now creates `var clock = new ManualClock()` and passes it as the 4th ctor arg.
- Copied the file-local `ManualClock` fake verbatim from `DtrhNativeEffectsTests.cs:335`
  (the SP-043 reference implementation; SP-043 convention is file-local, not shared).

Ctor signature confirmed positional-safe — `client/src/CcpClient.Desktop/Features/Dtrh/DtrhNativeEffects.cs:38-43`:

```csharp
public DtrhNativeEffects(
    IDtrhAudioBackend audio,
    IDtrhVideoBackend video,
    DtrhNativeEffectsOptions options,
    Action<string> log,
    ISoundClock? clock = null)
```

No optional parameter sits between `log` and `clock`; the call `}, _log.Add, clock);` binds correctly.

**Why no `Advance()` calls:** no assertion in this file depends on the 15s segment-cap timer
firing — `FirePayload` → `video.TryPlay` is synchronous (`Assert.Single(video.Played)`).
Adding an `Advance` would be a behavior/assertion change, banned by the packet. The unused
`Advance`/`CancelHandle` members are kept verbatim to match the SP-043 shape (build 0W/0E).

## 2. Proofs (exact commands + output)

**Zero assertion changes** — `git diff -U0 -- client/tests/CcpClient.Tests/DtrhFxRouterTests.cs | grep -E '^[+-]' | grep 'Assert\.'` → no output (grep exit 1, zero matches).

**Zero new tests** — `git diff -U0 -- client/tests/CcpClient.Tests/DtrhFxRouterTests.cs | grep -E '^[+-].*\[Fact\]'` → no output (exit 1). `[Fact]` count is 5 before and after.

**Zero wall-clock** — `grep -nE 'Thread\.Sleep|Task\.Delay|Stopwatch|SpinWait|DateTime\.Now|DateTime\.UtcNow|DateTimeOffset\.Now|DateTimeOffset\.UtcNow|new Timer|System\.Threading\.Timer' client/tests/CcpClient.Tests/DtrhFxRouterTests.cs` → exactly one match:

```
36:        // inject a ManualClock so no test in this file arms a real System.Threading.Timer —
```

A comment line naming the eliminated hazard (the SP-043 proof shape allows deterministic
fake-clock fields/comments only). No executable wall-clock construct remains; the injected
clock is `ManualClock` (due+fire capture, in-order `Advance`, dispose-cancels).

**DTRH classes green** — `dotnet test client/tests/CcpClient.Tests/CcpClient.Tests.csproj -c Debug --nologo --filter "FullyQualifiedName~Dtrh"` → `Passed! - Failed: 0, Passed: 181, Skipped: 0, Total: 181` (covers `DtrhFxRouterTests` + `DtrhNativeEffectsTests` + all other DTRH suites; the filtered run is evidence only — the contract 564/29 floor is proven by the full Step 3 testCommand).

## 3. Consults (solo route per the 2026-08-04 rewire; Opus 5 main / Fable 5 fallback)

**First attempt (pre-approach):** bare `consult` call resolved to council mode and failed with
`No synthesizer model configured (got "kimi-api/k3")` — the banned T-7 route (`kimi-api`
unregistered on this laptop). No verdict synthesized from that call.

**Pre-approach solo consult (actual):** re-invoked with `mode: "solo"`.
- **Actual answering model:** configured solo route = `anthropic/claude-fable-5`
  (`~/.pi/agent/bpx-consult.json` → `modes.solo.model`, read from disk; the tool response
  itself carried no model tag). Per the 2026-08-04 rewire this is the Fable 5 fallback route;
  Opus 5 main is not present in the consult config on this laptop.
- **Verdict (text):** "APPROVE the shape. File-local copy is correct — do not build a shared
  helper." Key points: a shared `TestSupport/ManualClock.cs` would violate this packet's File
  Scope (and force an out-of-scope edit to `DtrhNativeEffectsTests.cs`); one construction in
  `Make()` means injection there is class-wide; no `Advance()` calls is correct, not a gap.
  Advisory notes actioned: (a) the filtered 181-test run is not the contract floor — full
  testCommand with exact 564/29 counts runs in Step 3; (b) ctor signature stated above;
  (c) exact grep commands captured in §2.

**Pre-completion solo consult:** see §5 (added before .DONE per Step 2).

## 4. Engine-review presence (Review Level 1; T-2 heading format)

- **Step 1 plan review (`spine_review_step`, step 1, type plan):** PRESENT-but-SKIPPED —
  tool returned `skipped: true, spawnFailed: false`: "Nested reviewer spawn blocked inside
  pi worker session. Skip in-worker plan/code review — the batch engine runs reviews after
  worker success (SP-195)." Artifact: `.reviews/1-20260804T230842.md`. Not a spawn failure
  (fail-closed does not apply); engine runs the review after `.DONE`.
- **Step 2 plan review (`spine_review_step`, step 2, type plan):** (recorded at the Step 2
  boundary in §5).
- **Step 3 plan review (`spine_review_step`, step 3, type plan):** (recorded at the Step 3
  boundary in §5).

## 5. Pre-completion consult + Step 2/3 evidence

**Router wall-clock audit (pre-completion consult action item 1):**
`grep -nE "Timer|ISoundClock|Task\.Delay|Stopwatch|DateTime(Offset)?\.(Now|UtcNow)" client/src/CcpClient.Desktop/Features/Dtrh/DtrhFxRouter.cs client/src/CcpClient.Desktop/Features/Dtrh/DtrhNativeEffects.cs`
→ **zero matches in `DtrhFxRouter.cs`**; every match in `DtrhNativeEffects.cs` is the
`_clock`/`_videoCapTimer` seam (the SP-043 injectable path, `:35/:36/:43/:51/:240-241/:448-449/:504-505`).
The completion criterion "no real timer armed anywhere in the file" is met in full — no
narrowed claim, no residual discovery.

**Pre-completion solo consult (actual):** invoked with `mode: "solo"` on the evidence + diff.
- **Actual answering model:** configured solo route = `anthropic/claude-fable-5` (same
  config read as above; the tool response carried no model tag).
- **Verdict (text):** "proceed to Step 3 — the diff shape is sound. But do not create `.DONE`
  until four things are closed." The four: (1) router wall-clock audit — done above, criterion
  met in full; (2) record.md placeholders — closed by this edit; (3) actual-answering-model
  field — filled from `bpx-consult.json`; (4) Step 2/3 review-presence records — recorded
  below at each boundary. Step 3 advisory: run the contract testCommand verbatim; any counts
  other than exactly 564/29 → stop and report real numbers.

**Step 2 review presence:** `spine_review_step` (step 2, type plan) → PRESENT-but-SKIPPED
(`skipped: true, spawnFailed: false`, SP-195 nested-spawn block; artifact
`.reviews/2-20260804T231143.md`).

**Step 3 (contract) evidence — `testCommand` run verbatim:**
- `node .spine/patches/verify.mjs` → `OK — all patches applied on all roots` (exit 0).
- `dotnet build client/CcpClient.sln -c Debug --nologo` → `0 Warning(s), 0 Error(s)`.
- `dotnet test client/tests/CcpClient.Tests/CcpClient.Tests.csproj -c Debug --nologo` →
  `Passed! - Failed: 0, Passed: 564, Skipped: 0, Total: 564` — exactly the 564 floor, zero new tests.
- `dotnet test client/tests/CcpClient.HeadlessTests/CcpClient.HeadlessTests.csproj -c Debug --nologo` →
  `Passed! - Failed: 0, Passed: 29, Skipped: 0, Total: 29` — exactly the 29 floor.
- `git diff --check` → clean.
- `git status --short` → only File Scope paths (`spine-tasks/SP-045-dtrhfxrouter-manualclock/*`).

**Step 3 review presence:** `spine_review_step` (step 3, type plan) → PRESENT-but-SKIPPED
(`skipped: true, spawnFailed: false`, SP-195 nested-spawn block; artifact
`.reviews/3-20260804T231429.md`). All three in-worker plan reviews were called and skipped
by the engine per SP-195; the batch engine runs reviews after `.DONE`.
