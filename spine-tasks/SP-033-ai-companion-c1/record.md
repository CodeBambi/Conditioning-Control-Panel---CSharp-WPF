# SP-033 record — AI companion slice c1: AI foundation

**Task:** SP-033 · **Board row:** "Implement AI companion and awareness integration" (P0), slice c1 of `client/docs/ai-companion-admission.md` §8 · **Lane:** lane-1 · **Date:** 2026-07-22

Evidence classes: **U** = unit/headless tests green on Windows AND Linux. c1 claims no LAB/WH/WX evidence (recorded: no headless-surface tests for c1 — the foundation has no UI surface; `client/tests/CcpClient.HeadlessTests/**` untouched, honestly absent).

---

## 1. Mechanics inventory (SP-016 landed vs declared-only)

| Mechanic | State | Evidence |
|----------|-------|----------|
| `AiCommandEnvelope.cs` — strict validator, per-command verdicts, `AiExecutionPlan` (internal ctor) | **LANDED, real product code** | `client/src/CcpClient.Desktop/Ai/AiCommandEnvelope.cs` (545 lines); exercised by SP-019's 62-case fuzz |
| `AiOperationVocabulary.cs` — endpoint classes + pure classifier, operation classes, admission, moderation verdict taxonomy, `AiReply` variants, reply codes | **LANDED** | `AiOperationVocabulary.cs`; classifier is config-pure (no probes) |
| `AiDiagnosticRecord.cs` — content-free diagnostic record + closed verdict-code mapping | **LANDED** | `AiDiagnosticRecord.cs`; schema-level content-freedom proof exists in `AiOperationContractTests.cs` |
| `IAiMemoryStore.cs` | **DECLARED ONLY** (c4 implements) | `IAiMemoryStore.cs` |
| SP-004 ownership machinery (`OperationRegistry`, `AsyncOperationOwner`, `OperationOutcome`) | **LANDED** | `client/src/CcpClient.Desktop/Lifecycle/OperationRegistry.cs` — `Begin()` generation increment + cancel, stale-completion discard at `Complete` (`OperationRegistry.cs:76-90`), `CancelAndDrainAsync` bounded drain (`:97-135`) |
| SP-006 capability machinery (`CapabilityRegistry`, `CapabilityState`, `CapabilityProbeRunner`) | **LANDED** | `client/src/CcpClient.Desktop/Capabilities/` — registration never yields Available (`CapabilityRegistry.cs:40-53`) |
| `ISecretStore` (SP-005) | **DECLARED ONLY** | `PersistenceStore.cs:501-508` (`Get`/`Set`/`Delete` by name) |
| SP-018 redaction registry | **SPIKE-SCOPED instrument** (`client/spikes/CcpSpike.AiProvider/Redact.cs` + gitignored per-run secrets registry) — no product-code registry exists | c1 interpretation recorded in §6 |

## 2. WPF archaeology (READ-ONLY, `File.cs:line`)

- **Provider model:** strategy seam, live per-call switch with NO generation handling — `CCP.Core/Services/AIService/AiServiceStrategy.cs:22-58` (REJECTED shape); selection setting `CompanionPromptSettings.cs:27` (default Cloud).
- **Operation classes:** interactive = companion chat `Services/AiService.cs:103` (`GetBambiReplyExAsync`), quiz stateless; awareness = `[Category | App | Title | Duration]` packaging `AiService.cs:160-163,182-188`, keyword-trigger routing `Services/KeywordTriggerService.cs:1280-1328`.
- **Availability semantics (both REJECTED):** cloud identity-check `IsAvailable => App.HasCloudIdentity || Patreon.HasAiAccess` — `Services/AiService.cs:52`; local always-true `IsAvailable => true` — `Services/AIService/LocalAiService.cs:46`.
- **Endpoint inventory:** cloud proxy hardcoded `AiService.cs:27` (`https://codebambi-proxy.vercel.app`); auth shapes V2 `X-Auth-Token` + unified-ID `:332-352`, V1 legacy Bearer + 404 fallback `:479-499` (`:355-361`) — **inventory only, not admission** (contract §6 rule 4).
- **Secret seam usage:** `AppSettings.AuthToken` is `[JsonIgnore]` and routes through the Core secret store — settings carry no secret value — `CCP.Core/Models/AppSettings.cs:4005-4008`; DPAPI `ProtectedData.Protect/Unprotect(..., CurrentUser)` + entropy + in-memory cache wiped — `Services/Auth/SecureAuthTokenStore.cs:40,66`.
- **Panic/cancellation (REJECTED shapes):** no CancellationToken anywhere in the WPF AI path; fire-and-forget `AvatarTube/AvatarTubeWindow.ChatInput.cs:652,663`, `KeywordTriggerService.cs:1304`, `LocalAiService.cs:586`, `App.xaml.cs:1475`; dispose-only shutdown `App.xaml.cs:3253`.
- **F1 (SP-019):** `System.Text.Json` accepts duplicate object keys; validator `TryGetProperty` is last-wins while `EnumerateObject` sees all — parser-differential hazard. Fix = **reject duplicates** (only contract-consistent answer, SP-019 limit 6 / admission §3 rule 6).

## 3. Design (pre-consult)

### 3.1 New product files (all under File Scope)

| File | Contents |
|------|----------|
| `client/src/CcpClient.Desktop/Ai/AiOperationPipeline.cs` | The contract-named pipeline (fileScopeMustChange). Owns an `AsyncOperationOwner` ("Ai" participant) from the injected `OperationRegistry`; provider registry + selection; `RunInteractiveAsync` / `RunAwarenessAsync`; `SelectProvider` (switch = `owner.Begin()` → generation invalidation → token cancellation → stale discard); `PanicAsync` (owner.Cancel + bounded drain of own outstanding completions); `SendAttempts` counter at the single provider-I/O gateway; per-operation `AiDiagnosticRecord` emission |
| `client/src/CcpClient.Desktop/Ai/AiProviderSeam.cs` | `AiProviderId` (closed: `Cloud`, `LocalOllama` — stable typed IDs, contract §3 rule 1); `AiProviderDescriptor(Id, EndpointClass)`; `IAiProvider` (Descriptor + `CompleteAsync(request, token)`); `AiRequest`; `AiOperationResult(Outcome, Reply, Admission)` |
| `client/src/CcpClient.Desktop/Ai/AiEndpointAdmission.cs` | `IAiEndpointAdmissionPolicy.IsAdmitted(AiEndpointClass)` + `LoopbackOnlyAdmissionPolicy` (placeholder; allow-list governance owner-pending §9.2 #2 — remote rejected BEFORE any socket, SP-019 item 7 shape) |
| `client/src/CcpClient.Desktop/Ai/AiDiagnostics.cs` | `IAiDiagnosticsSink.Emit(AiDiagnosticRecord)`; pipeline maps result → record (classes, typed outcome, stable code, generation, duration, counts — never text) |
| `client/src/CcpClient.Desktop/Persistence/SecretStores.cs` | `ISecretStore` platform implementations (see §3.4) |
| `client/src/CcpClient.Desktop/Capabilities/CapabilityState.cs` | ADDITIVE only: provider/secret reason codes (e.g. `credentials-absent`) |

### 3.2 Pipeline semantics (contract §§1-4, 11; admission §2)

1. **Owned operations:** every interactive/awareness operation is `owner.RunAsync(...)`; the typed terminal outcome is SP-004's `OperationOutcome`; the `AiReply` domain payload rides with `Completed` inside `AiOperationResult` (contract §1).
2. **Admission order (before ANY provider I/O; each short-circuit = typed result, `SendAttempts` untouched):** (a) awareness consent (awareness class only → `Completed` + `Suppressed(ConsentDenied)`); (b) provider selected? → `Unavailable(not-configured)`; (c) provider capability state `Available`? → else `Unavailable(provider-unproven)` carrying the SP-006 state (pre-approach consult: capability BEFORE policy so the cloud-absence proof exercises the selected-but-unproven path); (d) admission policy admits the selected descriptor's endpoint class? → `Unavailable(endpoint-not-admitted)`; only then (e) `Interlocked.Increment(SendAttempts)` + invoke provider under the generation token.
3. **Selection ≠ availability:** selection is a settings-level fact (a value the pipeline holds; settings-document wiring is a later slice). Capability name convention `ai.provider.{id}`; a registered-but-unprobed provider reports `Unavailable(not-probed)` per SP-006 — that IS the c1 cloud-absence proof: the cloud descriptor exists (endpoint class `FirstPartyCloud`, inventory fact) with no implementation and no credentials → selected cloud yields typed `Unavailable` + typed capability state. Rejected shapes: `AiService.cs:52` identity-check, `LocalAiService.cs:46` always-true.
4. **Switch:** `SelectProvider(id)` = `owner.Begin()` (cancels old generation token, increments generation) + record selection. A provider completion arriving after the switch: if the provider honored the token → `Cancelled`; if it ignored the token and produced a late reply → the pipeline re-checks `_owner.IsLive(generation)` (generation current AND not cancelled — pre-approach consult correction: `IsCurrent` alone is insufficient because `Cancel()` does not invalidate application) at the point of application and discards the reply (typed `Cancelled`, registry `DiscardedStaleCompletions` observable). A reply under A can never surface under B. No network call is performed by the switch itself.
5. **Offline = zero network:** with no provider proven available, interactive + awareness + command-validation paths perform ZERO outbound attempts — the send-attempt counter is the product-side proof seam (single gateway; the integration test drives all three paths and asserts 0). Loopback and cloud degrade independently (separate descriptors, separate capability states).
6. **Panic:** `PanicAsync(boundedWait)` = `owner.Cancel()` + `Task.WhenAny(WhenAll(own outstanding), Delay(bound))`; in-flight work terminates typed `Cancelled`; late completions discarded stale. c2 re-verifies live (mid-stream network cancel); c7 carries the UI-quiet headed proof (acceptance discharges across slices — admission §8 mapping).

### 3.3 F1 fix shape (validator — the ONLY validator change)

Duplicate-key rejection at the three enumeration sites, minimal diff:
- envelope root (`AiCommandEnvelope.cs` root `EnumerateObject` loop): duplicate name → `Reject("duplicate-field")`;
- command object (`ValidateCommand` loop): duplicate → `MalformedData(schemaKnownName, "duplicate")`;
- data object (`CheckFields`): duplicate → `MalformedData(schemaKnownName, "duplicate")`.
Field names stay schema-known (an unknown duplicate trips `unknown-field` first). SP-019's 62 fuzz cases port into `CcpClient.Tests` as the regression suite with the two dup-key cases' expectations flipped to rejected, plus new duplicate cases (root/command/data, both orders).

### 3.4 ISecretStore implementations (Persistence/)

- **Windows:** `WindowsDpapiSecretStore` — `ProtectedData.Protect/Unprotect(CurrentUser)` per name into files under a root dir (WPF discipline `SecureAuthTokenStore.cs:40,66`); round-trip provable in a Windows-only test. `ProtectedData` inbox-availability on net10.0 verified empirically at build time; fallback if not inbox: thin P/Invoke `CryptProtectData` (crypt32) — zero new packages either way (csproj is OUT of File Scope).
- **Linux:** `SecretToolSecretStore` — freedesktop Secret Service via the platform's `secret-tool` CLI (libsecret; no new .NET dependency). Probe: binary on PATH + a real lookup round-trip; where no secret service is reachable (EXPECTED on WSL2 — no session daemon) the capability probe reports typed `Unavailable` and store operations throw typed `SecretStoreUnavailableException` — **never a plaintext fallback**. The typed-Unavailable probe path is the honest Linux evidence (admission §6 rule 2); a working-daemon proof needs a desktop-session box (named limit).
- Admission shape: `PlatformSecretStore.ForCurrentPlatform(rootDir)` → typed `(ISecretStore store, CapabilityState probe)` pair; settings documents carry opaque secret NAMES, never values (contract §10 rule 1) — a serialization test proves a settings-shaped document carries names only.

### 3.5 Diagnostics + redaction (contract §12; SP-018 pattern)

Every pipeline operation emits one `AiDiagnosticRecord` (classes/outcome/stable code/generation/duration/counts). The SP-016 schema-level content-freedom proof already exists; c1 adds: emitted-record assertions per path + a structural re-proof that no pipeline log/diagnostic site carries user text. Product code has NO central redaction registry (SP-018's is spike-scoped) — §6 lists every new log/diagnostic site and what it carries (stable codes only), which is the honest product-side form of the registry discipline.

### 3.6 Tests (CcpClient.Tests)

- `AiOperationPipelineTests.cs` — switch/stale-discard matrix (cooperative + uncooperative fake providers), selection≠availability (unprobed/unavailable/cloud-absent), admission policy (remote rejected pre-socket, counter 0), awareness consent suppression, panic (typed Cancelled + bounded drain), diagnostics emission per path.
- `AiOfflineIntegrationTests.cs` — no-proven-provider: interactive + awareness + envelope-validation paths, `SendAttempts == 0`; loopback-vs-cloud independent degradation.
- `AiEnvelopeFuzzRegressionTests.cs` — SP-019's 62 cases ported (dup expectations flipped) + new duplicate-key cases.
- `SecretStoreTests.cs` — Windows DPAPI round-trip (OS-gated), Linux typed-Unavailable probe (OS-gated, honest either way), secret-names-not-values serialization proof.

## 4. Consults

### 4.1 Pre-approach (Step 1)

**Mode:** solo (council forbidden by packet). **Actual answering model:** the consult tool output did not carry a model identifier (response stream truncated at the tail) — recorded honestly per T-2; verdict text below is verbatim-complete.

**Verdict (verbatim, substantive points):**
1. **CRITICAL — application check must be `IsLive`, not `IsCurrent`:** `owner.Cancel()` does NOT invalidate outcome application (only a new `Begin()` does — `AsyncOperationOwner.Cancel` doc comment). After panic, an uncooperative provider returning a late reply would pass an `IsCurrent` check (same generation) and surface. The pipeline's point-of-application check must be `_owner.IsLive(generation)` (same generation AND not cancelled) plus an explicit `token.IsCancellationRequested` check after the provider returns. Adopted.
2. **Admission order correction — capability BEFORE policy:** with policy-first, the c1 cloud-absence proof would surface `endpoint-not-admitted` (loopback-only placeholder rejects FirstPartyCloud) and never exercise the selected-but-unproven capability path the admission §2 rule 3 names. Adopted order: awareness-consent → selected? → capability `Available`? (`provider-unproven`) → admission policy (`endpoint-not-admitted`, still pre-socket for any proven-but-remote provider) → SendAttempts++ → invoke. Both postures stay distinctly observable.
3. Generation-capture race (pipeline reads `_owner.Generation` separately from `RunAsync`'s locked capture): direction is safe (over-discard, never over-apply); capture inside the body lambda. Documented, accepted.
4. Post-panic semantics: after `PanicAsync`, subsequent operations register under the cancelled token and terminate `Cancelled` until the next `Begin()` (`Start`/`SelectProvider`) — must be documented and asserted in tests. Adopted.
5. F1: three enumeration levels (root/command/data) are complete — no deeper schema nesting exists. Root duplicate → `Reject("duplicate-field")`; command/data duplicate → `MalformedData(schemaKnownName, "duplicate")`. Both SP-019 dup-key fuzz cases flip to rejected.
6. ProtectedData inbox status on net10.0: verify empirically at build time; P/Invoke `CryptProtectData` fallback if not inbox (zero new packages — csproj out of File Scope).

### 4.2 Pre-completion (Step 4)

**Mode:** solo. **Actual answering model:** the consult tool output again carried no model identifier and the stream truncated during point 4 of 5 — recorded honestly per T-2; the four complete points were actionable and are all closed below.

**Verdict: PROCEED after closing five small gaps — no design or honesty-framing violations.** Points + dispositions:
1. **Test-count accounting fudge ("pre-existing delta")** — FIXED: §7 now states exact per-file counts (12 pipeline + 2 fuzz + 2 offline + 4 secrets = 20; 446 + 20 = 466).
2. **WSL headless 29/29 was from the pre-fix rsync** — FIXED: final WSL gate re-run covers BOTH projects on the final tree (§7 Linux transcript).
3. **Contract testCommand must run as the exact single chain in Step 5** — ADOPTED (Step 5 runs the literal `&&` chain; the `-t:Rebuild` warning transcript is kept separately per the packet's own clause).
4. **`git status` cleanliness w.r.t. apply.mjs side effects** — VERIFIED: apply.mjs writes land under `.pi/npm/node_modules/pi-spine/**`, which is gitignored (`git check-ignore` confirmed); `git status --short` clean.
5. (Truncated in transit — the four received points were the actionable set; no open item remains against the completion criteria.)

## 5. Engine review presence (T-2)

| Call | Result |
|------|--------|
| Step 1 plan review (`spine_review_step --step 1 --type plan`) | **Engine review ABSENT (expected)** — nested reviewer spawn blocked inside pi worker session; `skipped: true`, `spawnFailed: false` (SP-195: engine runs reviews after `.DONE`). Artifact: `.reviews/1-20260722T141537.md` |
| Step 2 plan review (`spine_review_step --step 2 --type plan`) | **Engine review ABSENT (expected)** — same SP-195 skip; `spawnFailed: false`. Artifact: `.reviews/2-20260722T143047.md` |
| Step 3 plan review (`spine_review_step --step 3 --type plan`) | **Engine review ABSENT (expected)** — same SP-195 skip; `spawnFailed: false`. Artifact: `.reviews/3-20260722T144127.md` |
| Step 4 plan review (`spine_review_step --step 4 --type plan`) | **Engine review ABSENT (expected)** — same SP-195 skip; `spawnFailed: false`. Artifact: `.reviews/4-20260722T144640.md` |

## 6. Redaction/log-site registry (SP-018 pattern, product-side form)

SP-018's redaction registry is a SPIKE-SCOPED instrument (`client/spikes/CcpSpike.AiProvider/Redact.cs` + a gitignored per-run secrets registry); no product-code central registry exists. c1's product-side form of the same discipline: **every new log/diagnostic site enumerated here, with what it can carry** — and the answer is always "stable codes/classes only, by construction":

| Site | Carries | Content-freedom mechanism |
|------|---------|---------------------------|
| `AiOperationPipeline` → `IAiDiagnosticsSink.Emit` (one record per operation) | `AiDiagnosticRecord`: operation/endpoint CLASSES, typed outcome, stable code, generation, duration, counts | SP-016 schema (no free-text field exists); SP-016 structural property-set proof maintained + `Diagnostics_NeverCarryText_StableCodesOnly` |
| `SecretToolSecretStore` | NO log site. secret-tool stderr is NEVER logged or surfaced raw — classified to the stable detail "secret service unreachable (no session daemon)" (stderr can carry session paths); secret values travel stdin-only (never argv) | code review + `LinuxProbe_TypedOutcome_NeverFaked` |
| `WindowsDpapiSecretStore` | NO log site. Failures are typed (`SecretStoreUnavailableException` with a stable code) or absent-read (corrupt blob deleted, WPF precedent) | `SecretStoreUnavailableException` message is `secret-store: {stableCode}` by construction |
| `AiOperationPipeline` diagnostics `StableCode` on fault | exception CLASS NAME only (contract §12 rule 1 — messages can embed user input) | `StableCodeOf` maps `Failed` → `Exception.GetType().Name` |

Secrets inventory: c1 stores no real secrets (no credentials exist); the seam + tests use synthetic values only. Nothing secret was sent to any advisor/MCP.

## 7. Evidence transcripts

**Environment facts:** Windows 11 lane worktree (this box); WSL2 Ubuntu (`Linux WeeB 6.6.114.1-microsoft-standard-WSL2`), dotnet 10.0.110 both platforms; **no node in WSL** → verify.mjs runs on Windows only (SP-029/032 precedent — it verifies the lane's pi-spine INSTALL state, platform-independent). `ProtectedData` is **NOT inbox** on the plain net10.0 TFM (empirical: `Type.GetType("...ProtectedData, System.Security.Cryptography.ProtectedData")` → null in a scratch net10.0 project) → Windows DPAPI via crypt32 P/Invoke, zero new packages (csproj outside File Scope).

### Windows (lane worktree)

- `node .spine/patches/verify.mjs` → initial **FAIL** (reinstall had removed all 6 project-root patches — same lane condition as SP-028/029/032) → `apply.mjs` re-applied (6 across 2 roots) → verify **exit 0**.
- `dotnet build client/CcpClient.sln -c Debug -t:Rebuild --nologo` → **Build succeeded. 0 Warning(s) 0 Error(s)**.
- `dotnet test client/tests/CcpClient.Tests` → **466/466** (floor 446; +20 new: 12 `AiOperationPipelineTests` + 2 `AiEnvelopeFuzzRegressionTests` (70-case matrix + per-level duplicate facts) + 2 `AiOfflineIntegrationTests` + 4 `SecretStoreTests`).
- `dotnet test client/tests/CcpClient.HeadlessTests` → **29/29** (floor 29 — no new headless tests; c1 has no UI surface, honestly recorded).
- Targeted greens during development: pipeline+fuzz+contract 51/51; offline+secrets 6/6.
- Windows DPAPI round-trip proven: set/get/delete, file bytes never contain plaintext, filesystem-safe names, corrupt-blob → absent-read.

### Linux (WSL2, `~/ccp-sp033`, native ext4, never /mnt/e; staged via rsync of `client/` + the legacy-tree LINKED DTRH payload subtree per the SP-032 gate lesson)

- First run: build 0W/0E; CcpClient.Tests **465/466 — 1 failure** (`SecretStoreTests.LinuxProbe_TypedOutcome_NeverFaked`: `secret-tool` ABSENT on PATH → `Process.Start` threw raw `Win32Exception` instead of the typed `SecretStoreUnavailableException`). Fixed in `SecretToolSecretStore` (start-failure → typed `ToolMissing` → typed exception; probe → `DependencyMissing("secret-tool")`); HeadlessTests 29/29 (pre-fix tree).
- **Final (post-fix tree, both projects re-run — pre-completion consult point 2):** build **0W/0E**; CcpClient.Tests **466/466**; HeadlessTests **29/29**.
- **Secret-store probe facts (honest, never faked):** `secret-tool` is **NOT on PATH** on the WSL2 box (`which secret-tool` → absent) → probe returns typed `DependencyMissing("secret-tool")` with code `secret-service-unreachable`; store operations throw typed `SecretStoreUnavailableException`. This typed-Unavailable path IS the honest Linux evidence (admission §8 c1: no session daemon on WSL2; a working-daemon proof needs a desktop-session box — named limit).
- No Wayland claims anywhere. No LAB/WH/WX evidence claimed for c1.

## 8. Budgets, surprises, durable-lesson candidates

**Budget:** well inside the 4h packet budget (single session, no context-limit exits).

**Surprises:**
1. `ProtectedData` not inbox on plain net10.0 — forced the crypt32 P/Invoke shape (zero-dependency, WPF-equivalent CurrentUser scope). Verified empirically before committing to it.
2. WSL2 first-run failure class: `Process.Start` on a missing binary throws platform `Win32Exception` — typed-failure channels must wrap process START, not just process EXIT. Fixed; test asserts the typed channel on both branches.
3. Pre-approach consult caught a REAL panic hole: `AsyncOperationOwner.Cancel()` does not invalidate outcome application — an uncooperative provider's late reply after panic would have surfaced under an `IsCurrent` check. Fixed to `IsLive` + explicit token check; test `Panic_UncooperativeProvider_LateReplyDiscarded_AndPostPanicOpsCancel` covers it.
4. verify.mjs reinstall drift: 4th occurrence on this lane (SP-028/029/032 + now) — apply.mjs → exit 0.

**Durable-lesson candidates (for the orchestrator's port-lessons reconciliation — enabler 2: the worker does NOT edit port-lessons.md):**
1. **Stale-application checks must be `IsLive`-shaped (generation AND not-cancelled), never `IsCurrent`-alone** — `Cancel()` invalidates the token but not the application check; any post-cancel point of application needs both. (Class: cancellation-correctness; found by pre-approach consult, would have been a live panic leak.)
2. **Subprocess-based platform seams must type their START failures** — a missing binary faults at `Process.Start`, not at exit-code inspection; typed-unavailable channels must wrap both. (Class: cross-platform typed failures.)
3. **WSL gate staging must include the legacy-tree LINKED payload subtree** (SP-032's lesson — re-confirmed necessary and sufficient this packet; `client/` + `ConditioningControlPanel/Resources/web/dtrh/`).
4. **Plain-TFM net10.0 has no inbox `ProtectedData`** — crypt32 P/Invoke is the zero-package DPAPI path when the csproj is outside packet scope. (Class: platform API availability vs TFM assumptions.)
