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

**Engine-review presence:** none so far (Step 1 plan review requested after this step's commit — presence/absence
recorded there).
