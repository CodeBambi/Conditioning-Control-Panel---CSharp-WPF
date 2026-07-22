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

(pending)

## 5. Engine review presence (T-2)

(pending — per-call record of engine-review presence/absence)

## 6. Redaction/log-site registry (SP-018 pattern, product-side form)

(pending — every new log/diagnostic site + what it carries)

## 7. Evidence transcripts

(pending)

## 8. Budgets, surprises, durable-lesson candidates

(pending)
