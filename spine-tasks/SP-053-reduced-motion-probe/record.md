# SP-053 — Webview prefers-reduced-motion inheritance probe — Record

## Step 1 — probe design + pre-approach consult

### Design (pre-consult)

**What is measured.** The DTRH page's own probe (`ConditioningControlPanel/Resources/web/dtrh/shared/capability.js:35` — READ-ONLY) is
`window.matchMedia('(prefers-reduced-motion: reduce)').matches`; `:57` turns `reduced` into 2D mode, `canTry3d:false`.
The probe measures what the EMBEDDED engine (Windows WebView2, the admitted shape per dtrh-admission §5) answers for that exact
query against the OS animation state, read host-side with the exact API WPF's own OS cap wraps
(`SystemParameters.ClientAreaAnimation`, `MotionFx.cs:37-54` → `SystemParametersInfo(SPI_GETCLIENTAREAANIMATION=0x1042)`).
Probe-first: the verdict is whatever the engine reports. No assumption about Chromium behavior.

**Measurement path (three parts, all typed):**

1. **Page side** — extend the harness-only transport probe `client/src/CcpClient.Desktop/Features/Dtrh/overlay/probe.html`
   (in File Scope; the SP-023 probe page driven via `--dtrh-demo --dtrh-page probe.html`). A motion block evaluates the
   EXACT query string at module eval and posts `{type:'probe-motion', phase:'initial', reduced:<bool>}` through the
   native page→host transport (`window.chrome.webview.postMessage`) plus the bridge.log mirror; a `change` listener on
   the MediaQueryList re-posts `phase:'change'` so a live OS toggle is also captured if the engine tracks it.
   The host's existing dispatch already logs any `probe-*` UnknownType verbatim
   (`DtrhHostWindow.HandleWebMessageBody`) — zero new dispatch code for page→host.

2. **Host side** — new `client/src/CcpClient.Desktop/Features/Dtrh/DtrhMotionPreference.cs`:
   - `MotionInheritanceVerdict { Holds, Fails, Unknown }` + pure `Evaluate(bool? osClientAreaAnimation, bool? engineReduced)`:
     Holds iff both known and `engineReduced == !osClientAreaAnimation` (the OS can only REMOVE motion — engine reduced
     must equal OS-animations-off); Unknown if either side null; Fails otherwise. This is the unit-testable seam —
     tests pin the verdict mapping and the query literal, NEVER the OS inheritance itself.
   - `ReadOsClientAreaAnimation()` — Windows: `SystemParametersInfo(SPI_GETCLIENTAREAANIMATION)` P/Invoke (user32;
     the exact API `SystemParameters.ClientAreaAnimation` wraps); non-Windows: null (Linux half named limit, never faked).
   - `ProbeQuery` const = `"(prefers-reduced-motion: reduce)"` — the measurement PATH pinned against capability.js:35.
   - `DtrhHostWindow`: at probe-page `NavigationCompleted`, log the OS read
     (`dtrh-motion: host OS ClientAreaAnimation=<bool|null> source=SystemParametersInfo|named-limit`); on each
     `probe-motion` page message, RE-READ the OS state at that moment and log the paired typed line
     (`dtrh-motion: verdict=<Holds|Fails|Unknown> osAnimation=<bool> engineReduced=<bool>`). OS-side verified
     immediately before each engine reading is interpreted.

3. **The run (headed, Windows)** — two auto-closing demonstrator runs, stderr captured:
   - Record baseline OS state (GET).
   - Run A (baseline): `dotnet run --project client/src/CcpClient.Desktop -- --dtrh-demo --dtrh-page probe.html --dtrh-quick --dtrh-auto-close 15` → `evidence/motion-run-baseline.log`.
   - Toggle OS Animation effects OFF via `SystemParametersInfo(SPI_SETCLIENTAREAANIMATION=0x1043, FALSE, SPIF_UPDATEINIFILE|SPIF_SENDCHANGE)`
     from a HARNESS PowerShell script under `evidence/` with `try/finally` restore (the exact call the Settings app
     makes; SET never enters product code — consult correction 2); VERIFY OS-side (GET returns false) before the engine
     read; ~1–2 s settle for the SENDCHANGE broadcast.
   - Run B → `evidence/motion-run-off.log`.
   - Restore the baseline state; VERIFY OS-side. The restore is part of the probe run contract.
   Diagnostics land on stderr (`DebugLogSink`) — transcripts are the captured stderr.

**Failure-contingent mechanism (sketch only — built ONLY if the verdict is Fails).** If the embedded engine ignores the
OS state, the host owes a typed honoring mechanism that never silently betrays the page's own probe. **Primary shape
(post-consult):** the Chromium-native `--force-prefers-reduced-motion` delivered via the package's environment-creation
browser-args seam (`EnvironmentOptions`/`AdditionalBrowserArguments` — 12.0.1 binary-verified below), applied ONLY when
the host-read OS state says animations off — engine-level, unraceable, forces in the one direction the OS can move
(`MotionFx.cs:37-54` parity). **Fallback shape:** a document-start injection (`AddScriptToExecuteOnDocumentCreated`,
also binary-verified) wrapping `window.matchMedia` for ONLY that one query — with the recorded race caveat (a
post-navigation `InvokeScript` can land after `capability.js:35` already ran — silent non-fix; document-start or
nothing). REJECTED shape: a host→page message the page would consume — `ConditioningControlPanel/**` is READ-ONLY, no
page-side consumer may be added. The page keeps calling its own probe and receives the OS-honest answer in every
accepted shape (honoring, never betrayal). Unit tests pin the mechanism's script/args content + the `Evaluate`
verdict, never the engine.

**Linux half.** Unproven named limit (WSL zero-distros; WebKitGTK inheritance unknown) — recorded, never claimed.

### Pre-approach solo consult

**Mode:** solo (T-7: council unproven; `kimi-api` unregistered — PROMPT Do-NOT). **Requested route:** Opus 5 main
(2026-08-04 rewire). **Actual answering model:** NOT surfaced by the consult tool response (no model identity header —
recorded honestly, same provenance discipline as SP-022…027, SP-049/SP-050). **Three calls:** the first two verdicts
TRUNCATED mid-sentence (the SP-027/SP-050 truncation class); the third (terse-completion request) completed the verdict.
Truncations recorded, never silently stitched.

**Verdict: design sound — APPROVE with four corrections.**

1. **Verdict recorded per OS state (two rows) with a consequence column.** Only OS-OFF + engine `reduce=false` is the
   betrayal that triggers the mechanism obligation; OS-ON + engine `reduce=true` is a conservative-safe mismatch (no
   forcing mechanism can fix it — `MotionFx.cs:37-54`: the OS can only REMOVE motion). `Evaluate` stays pure 3-valued;
   the asymmetry lives in record.md prose, not the enum. Confounder caveat: Chromium's reduced-motion feature has other
   sources (`--force-prefers-reduced-motion`, blink overrides, DevTools emulation, `WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS`) —
   a Holds claim is attributable to OS inheritance only if none are in play. Record the **WebView2 runtime version** in
   each transcript (engine-version-scoped evidence, not eternal truth).
2. **Product code = GET only.** SET/restore lives in a harness PowerShell script under the task's `evidence/` with
   `try/finally` restore — a crashed run must never leave the box in reduced-motion state.
3. **Two distinct P/Invoke signatures:** GET `SystemParametersInfoW(0x1042, 0, out int, 0)`; SET
   `SystemParametersInfoW(0x1043, 0, (IntPtr)value /* by-value in pvParam */, SPIF_UPDATEINIFILE|SPIF_SENDCHANGE)`.
   Check BOOL return + `Marshal.GetLastWin32Error()`; GET failure → null → `Unknown`, never a defaulted boolean.
4. **Greps before coding (DONE — results below).**

**(b) toggle protocol:** the call is what Settings makes; transcript order per run = baseline GET → SET → GET-verify →
~1–2 s settle (SENDCHANGE broadcast) → launch → engine read → close → restore SET → GET-verify. **Two runs are the
primary evidence** (whether WebView2's browser process picks up WM_SETTINGCHANGE in an embedded host is exactly the
unknown); the MediaQueryList `change` listener is a BONUS fact — a live flip observed proves live tracking, its absence
proves nothing (recorded explicitly).

**(c) failure-contingent shape (if Fails):** check the Chromium-native path FIRST — `--force-prefers-reduced-motion`
via AdditionalBrowserArguments at environment creation (engine-level, no page monkey-patching, unraceable; only forces
in the one direction the OS can move). The matchMedia-wrap is FALLBACK only, and note the race: an `InvokeScript` after
navigation can land after `capability.js:35` already ran — a silent non-fix. Document-start injection or nothing.

**Correction-#4 grep results (pre-coding, 2026-08-05):**

- **(a) injection/browser-args seam EXISTS in the 12.0.1 binary** (string table of
  `avalonia.controls.webview/12.0.1/lib/net10.0/Avalonia.Controls.WebView.dll`): `AddScriptToExecuteOnDocumentCreated`
  on the package's own `ICoreWebView2*` interop; `get_/set_AdditionalBrowserArguments` (+ backing field); public
  `EnvironmentOptions` / `ExplicitEnvironment` / `EnvironmentRequested` surface. The contingent mechanism is NOT
  sketched against a missing API.
- **(b) no confounders in this environment:** `env | grep WEBVIEW2` → empty (no
  `WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS`); no `--force-prefers-reduced-motion` anywhere in the client tree (product
  code never sets browser args).
- **(c) payload consumers:** `matchMedia`/`prefers-reduced-motion` in the READ-ONLY payload = ONLY
  `shared/capability.js:35` (the probe) + `:40` (pointer: coarse, unrelated) + `:57` (the 2D consequence). One consumer
  to honor — a mechanism, if built, covers all of them.

**Engine-review presence:** Step 1 plan review requested via `spine_review_step` (type=plan) after commit 17b9ad45 →
verdict null, SKIPPED by design ("Nested reviewer spawn blocked inside pi worker session — the batch engine runs
reviews after worker success (SP-195)"; artifact `.reviews/1-20260811T152704.md`; spawnFailed=false). No engine
reviewer ran in-worker on any call so far; recorded per the T-2 heading discipline.

## Step 2 — the probe + measurement

### Seam (unit-testable — asserts the measurement PATH, never the OS inheritance)

- `client/src/CcpClient.Desktop/Features/Dtrh/DtrhMotionPreference.cs` (NEW): `ProbeQuery` const (the exact
  `capability.js:35` literal), pure `Evaluate(bool?, bool?) -> Holds/Fails/Unknown` (Unknown on any null — never a
  defaulted boolean), `ReadOsClientAreaAnimation()` — GET-only `SystemParametersInfoW(0x1042)` on Windows (the exact
  API `SystemParameters.ClientAreaAnimation` wraps; GET failure -> null), null off-Windows (named limit).
- `overlay/probe.html` (harness page, in scope): motion block evaluates the EXACT query at module eval and posts
  `{type:'probe-motion', phase, query, reduced}` via the native page->host transport + bridge.log mirror; a
  MediaQueryList `change` listener re-posts `phase:'change'` (bonus live-tracking fact; absence proves nothing).
- `DtrhHostWindow.axaml.cs`: probe-page `NavigationCompleted` logs the host OS read; the existing `probe-*`
  UnknownType branch pairs each `probe-motion` arrival with a FRESH OS read and logs the typed verdict line
  (`LogMotionPairing` — tolerant parse, malformed -> Unknown).
- `client/tests/CcpClient.Tests/DtrhMotionPreferenceTests.cs` (NEW): 11 facts — the query literal == capability.js:35,
  the 4-cell known-sides mapping, the 5 null-combination Unknown cases, the host-read smoke fact. 11/11 green.

### Headed measurement run (Windows, 2026-08-05; WebView2 runtime HKLM pv=151.0.4129.72 — engine-version-scoped)

Protocol per run (consult-hardened): baseline GET -> (SET -> GET-verify -> 1.5 s settle for SENDCHANGE) -> launch ->
engine read -> close -> restore SET -> GET-verify. Toggle via `evidence/motion-toggle.ps1` (the exact
`SystemParametersInfo(0x1043, by-value, SPIF_UPDATEINIFILE|SPIF_SENDCHANGE)` call Settings makes; baseline captured
first, restored after, re-verified).

**Run A — baseline (OS Animation effects ON; GET ClientAreaAnimation=1):** `evidence/motion-run-baseline.log`, app exit 0.

    dtrh-motion: host OS ClientAreaAnimation=True source=SystemParametersInfo(GET)
    dtrh: {"type":"probe-motion","phase":"initial","query":"(prefers-reduced-motion: reduce)","reduced":false}
    dtrh-motion: verdict=Holds phase=initial osAnimation=True engineReduced=False query=(prefers-reduced-motion: reduce)

**SET 0 -> `set=0 verify ClientAreaAnimation=0` (OS-side verified before the engine read).**

**Run B — OS Animation effects OFF:** `evidence/motion-run-off.log`, app exit 0.

    dtrh-motion: host OS ClientAreaAnimation=False source=SystemParametersInfo(GET)
    dtrh: {"type":"probe-motion","phase":"initial","query":"(prefers-reduced-motion: reduce)","reduced":true}
    dtrh-motion: verdict=Holds phase=initial osAnimation=False engineReduced=True query=(prefers-reduced-motion: reduce)

**Restore: SET 1 -> `set=1 verify ClientAreaAnimation=1`; final `-Get` -> `ClientAreaAnimation=1`. The box is back at
its baseline.** No `phase:'change'` messages in either run (each run launched after the state settled — the
live-tracking bonus question is outside the two-run protocol; absence recorded, nothing claimed).

**Engine-review presence (Step 2):** plan review requested via `spine_review_step` (type=plan) after commit 4261c9b6 ->
verdict null, SKIPPED by design (SP-195 — the batch engine runs reviews after worker success; artifact
`.reviews/2-20260811T153450.md`; spawnFailed=false).

## Step 3 — verdict + mechanism + evidence + pre-completion consult

### Verdict (probe-first — the answer is what the embedded engine reported)

**INHERITANCE HOLDS on Windows WebView2 (runtime 151.0.4129.72, engine-version-scoped).** Per-OS-state table
(consult correction 1 — consequence column; only OS-OFF + engine `reduce=false` would be the betrayal):

| OS ClientAreaAnimation | engine `(prefers-reduced-motion: reduce)` | class | consequence |
|---|---|---|---|
| OFF (False) | reduce=true | **Holds** | a reduced-motion user's page goes 2D as its own probe intends (`capability.js:57`) |
| ON (True) | reduce=false | **Holds** | full motion for users who did not ask for reduction |

Confounder discipline (consult correction 1): no `WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS` in this environment (grep,
Step 1); the product never sets browser arguments; no `--force-prefers-reduced-motion` anywhere in the client tree;
no DevTools emulation in the harness run. The Holds claim is attributable to OS inheritance. The engine read
`SPI_GETCLIENTAREAANIMATION` at browser-process start in BOTH runs; live WM_SETTINGCHANGE tracking was not exercised
(two-run protocol is the primary evidence; the `change` listener fired in neither run — nothing claimed either way).

**Both runs measured the Windows WebView2 surface** (load-bearing quote, `grep -n "surface" evidence/motion-run-*.log`):
`dtrh: NavigationCompleted success=True (surface embedded)` � baseline log :24, off log :23. No dialog/unsupported
fallback was in play. Harness/product equivalence: the probe ran in the SAME `DtrhHostWindow` the product flow uses �
same surface selection, same environment-creation path; only the `page` ctor argument differs (`_dtrh.PageUrl(_page)`) �
so the probe measured the product's engine, not a different host (consult fix 3).

**The page consumes the query exactly ONCE, at boot** (payload call-site grep, consult fix 2iii): `detectMode` is
imported and called at `boot.js:19,77` ONLY � a boot-time decision. State-at-page-load is therefore the only thing
the page can consume, which makes the two-run protocol provably sufficient and retires the live-`change` question as
IRRELEVANT (not merely unmeasured): no run observed a `phase:'change'` message and none was needed.

**User-observable consequence: none.** The pre-existing obligation is DISCHARGED for WebView2 151.0.4129.72 on Windows:
the embedded engine inherits the OS/user motion preference, so the page's own probe (`capability.js:35`) is never
silently betrayed. A WebView2 runtime regression re-opens the row; Linux (WebKitGTK) stays unproven � WSL
zero-distros named limit (never faked). The verbatim probe-motion bodies carry only a boolean, a phase string and a
fixed query literal � no user content, �4.8-safe.

### Mechanism: NOT BUILT (contingent on a Fails verdict)

The failure-contingent honoring mechanism (Step 1 sketch: Chromium-native `--force-prefers-reduced-motion` via the
12.0.1-binary-verified `AdditionalBrowserArguments` environment seam, applied only when the host-read OS state says
animations off; document-start `matchMedia`-wrap as fallback) was designed and its seams verified, but the verdict is
Holds — building it would be speculative code against a non-defect. The sketch + seam evidence stay in this record as
the ready shape if a future engine regression flips the verdict.

### Linux half — named limit (recorded, never faked)

Unproven: WSL zero-distros on this laptop; WebKitGTK's `prefers-reduced-motion` inheritance unknown. WPF's own OS cap
is Windows-only (`SystemParameters.ClientAreaAnimation`, MotionFx.cs:37-54), so the Windows-first probe is honest
parity work. No Wayland claims.

### Pre-completion solo consult

**Mode:** solo (T-7; PROMPT Do-NOT). **Requested route:** Opus 5 main (2026-08-04 rewire). **Actual answering model:**
NOT surfaced by the consult tool response (recorded honestly, same discipline as Step 1). **Two calls:** the first
verdict TRUNCATED mid-(b) (the Step-1 truncation class); the second (terse-completion request) delivered the remainder
in full.

**Verdict: the work is sound and the Holds call is right � do NOT build the mechanism. Four concrete fixes before
`.DONE`, then run the contract properly.** (a) the record's "engine read SPI at browser-process start" was an
unmeasured claim (fix 1); run B reporting the CHANGED value after the flip empirically rules out caching (fix 2i);
quote the `surface embedded` NavigationCompleted line from BOTH transcripts (fix 2ii); grep the payload's detectMode
call sites � boot-time-only consumption makes the two-run protocol provably sufficient (fix 2iii); state the
harness/product window equivalence in one line (fix 3). (b) diff scope clean; one convention gap �
`[SupportedOSPlatform("windows")]` on the extern matching `DtrhCapabilityProbes.cs` (fix 4); logging �4.8-safe.
(c) mechanism-not-built CONFIRMED: Holds in the only direction that matters; any mechanism would be unfalsifiable
code (the engine already honors � nothing could prove the mechanism does anything); the Step-1 sketch + binary
evidence is the correct deliverable. Contract discipline: exact testCommand, warnings measured on `-t:Rebuild`, TRX
loggers landed under evidence/, counts vs the >=669/33 floor, `git diff --check`, File-Scope-only `git status`, final
`-Get` proof the box is not left reduced.

**Fix application:** fixes 1-4 applied in this record and the code (extern annotated); contract run per the consult's
concrete list in Step 4 below.

**Engine-review presence (Step 3):** plan review requested via `spine_review_step` after the step commit � recorded
in Step 4's summary line (same SP-195 skip class as Steps 1-2 if skipped).
