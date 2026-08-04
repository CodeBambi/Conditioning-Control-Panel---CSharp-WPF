# SP-035 record — AI companion slice c2: loopback Ollama provider (first REAL provider)

**Task:** SP-035 · **Board row:** "Implement AI companion and awareness integration" (P0), slice c2 of `client/docs/ai-companion-admission.md` §8 · **Lane:** lane-1 · **Date:** 2026-08-04 (fresh execution; wave-4 desktop park moot per the 2026-08-04 amendment)

Evidence classes: **U** = unit tests both platforms; **LAB** = deterministic loopback-lab integration (real sockets on 127.0.0.1, zero external network). No WH/WX claims. No Wayland claims.

---

## 0. Session facts (probes, 2026-08-04, this laptop)

- **Ollama presence re-probe** (honesty framing (a) — executable presence + `ollama --version` ONLY, no socket contact, no pull):
  - `where.exe ollama` → `C:\Users\Micha\AppData\Local\Programs\Ollama\ollama.exe`
  - `ollama --version` → `ollama version is 0.32.5` (exit 0)
  - **Fact: Ollama IS installed on this box** (SP-019 limit-1 absence was desktop-scoped). No real-Ollama round-trip is claimed or attempted; the deterministic loopback lab remains the instrument.
- **WSL probe** (Step-3 gate, named limit): `wsl -l -q` → *(empty output)*, exit 0 → **WSL installed with ZERO distros**. The Linux LAB gate cannot run in-packet; provisioning a distro is an owner decision. Linux evidence is NOT faked: LAB matrix runs Windows-only, recorded as the named limit (Amendment 2026-08-04 item 2).

## 1. WPF archaeology (READ-ONLY, `File.cs:line`)

- **Ollama transport (native shape):** `POST {host}api/chat`, payload `{model, messages:[{role,content}], stream:false, think:false}` — `CCP.Core/Services/AIService/LocalAiService.cs:374-390`; doc header `:23-24` (`think=false` = the perf flag cutting reasoning-model latency ~50s→~3s; no `options`/temperature on the stateful path); raw-completion variant with `options.temperature` at `:341-349`. Response extraction: `message.content` string — `ExtractContent :519-530` (catch-and-null REJECTED; c2 types it).
- **Defaults:** host `http://localhost:11434/` when blank — `NormalizeHost :515-517`; model default `qwen3.5:latest` — `:403`; `HttpClient.Timeout = 5 min` — `:341, :412` (cold model-load accommodation).
- **Timeout/cancellation (REJECTED shape):** WPF conflates timeout and cancel — `catch (TaskCanceledException)` → "timed out" `:361, :395`; no CancellationToken anywhere in the AI path (SP-033 record §2). c2: token is the ONLY cancellation mechanism; timeout is a linked-CTS failure classifier disambiguated by origin (SP-019 item 2).
- **Retry shape (placeholder, §9.2 #6 owner-pending VALUES):** `OpenAiCompatibleService.cs:402-427` — `for attempt < 2`; retryable = 429 || status ≥ 500; `attempt == 0 && retryableStatus` → `Task.Delay(1200)` → continue (`:425-427`); never a retry on other statuses or parse failures. SP-019 refined the fixed 1200ms to Retry-After-honored clamped ≤2s (spike limit 8: values are the WPF-observed placeholder, not owner-approved).
- **Refusal (REJECTED shape):** sentinel-string channel `Services/Moderation/ModerationRefusal.cs:53-54` — rejected per contract §7 rule 3. c2: typed `AiModerationRefusal(category, AiModerationSource.Output)` carrier via deterministic shape discrimination, never string-sniffing (SP-019 item 5).
- **Remote host:** WPF accepts ANY absolute URI as Ollama host (`LocalAiService.cs:244-269` EnsureHost/TryCreate) — the "local AI = local-only data" gap. c1's `AiEndpointClassifier.ClassifyOllamaHost` + `LoopbackOnlyAdmissionPolicy` close it; c2 lands the first REAL provider behind that discipline, and extends the same pre-socket rule to the probe.

## 2. Design (pre-approach, consult-validated — §4.1)

### 2.1 Files (all inside File Scope)

| File | Contents |
|------|----------|
| `client/src/CcpClient.Desktop/Ai/LoopbackOllamaProvider.cs` (contract-named, fileScopeMustChange) | The first REAL `IAiProvider`. Options record (Host/Model/RequestTimeout/Retry), typed `AiRetryPolicy` placeholder, Descriptor from pure host classification, SP-006 probe (`api/version`, pre-socket remote rejection), `CompleteAsync` (native `api/chat`, stream-read body, timeout classifier, bounded retry, typed refusal, malformed/truncated → Unavailable never partial), provider-side `SendAttempts` + `BytesReadSoFar` (content-free instruments) |
| `client/src/CcpClient.Desktop/Capabilities/CapabilityState.cs` | ADDITIVE: 1 reason code (`host-unreachable`); the endpoint-not-admitted probe reason reuses the existing `AiReplyCodes.EndpointNotAdmitted` stable token (consult §4.1 point 4 — minimal additive surface). Per-change justification: §9.2-ledger-shaped tokens belong in the shared vocabulary; the probe needs a typed reason for a refused/unreachable loopback host and no existing code fits |
| `client/tests/CcpClient.Tests/AiProviderLab.cs` | The LAB: fresh code reusing SP-019's failure-injection SHAPES (never its code — spike stays quarantined), Ollama-native protocol: `POST /api/chat` + `GET /api/version`, modes Ok/Timeout/Rate429/Error500/Refusal/Malformed/Truncated/HangStream/SlowOk, per-request lab-side records (`completed`/`client-gone`/`released-after-disconnect`), 127.0.0.1 ephemeral port only |
| `client/tests/CcpClient.Tests/LoopbackOllamaProviderTests.cs` | Provider-level unit tests against the in-proc lab |
| `client/tests/CcpClient.Tests/AiProviderLabIntegrationTests.cs` | LAB matrix through the REAL c1 pipeline + live panic + live stale-discard + remote pre-socket + offline re-verify |

### 2.2 Provider semantics

1. **Protocol — NATIVE Ollama `api/chat` (WPF-observed), NOT the OpenAI-compatible `/v1` shape.** The packet's "fake OpenAI-compatible endpoint" phrasing echoes the SP-019 instrument description; the provider is the Ollama provider and WPF speaks native `api/chat` (`LocalAiService.cs:374-390`). Ollama also exposes `/v1/chat/completions`; the ported shape is the WPF-observed native one. Recorded deviation, consult-approved (§4.1 point 1). **Honesty boundary (consult):** the lab's refusal shape (`{"refusal":{"category":"content_filter"}}`), 429, and Retry-After are LAB CONSTRUCTS — the matrix proves the CLIENT handles these HTTP shapes; it never claims a real Ollama produces them.
2. **Request:** `{model, messages:[{role:"user", content:prompt}], stream:false, think:false}` — the WPF stateful-path shape. System-prompt assembly/memory enrichment are later slices (c4/c7); `AiRequest.Prompt` maps to the single user message. Body read as a STREAM (`ResponseHeadersRead` + chunked read observing the token) so mid-stream cancellation has a true partial-body position (`BytesReadSoFar`, content-free) — SP-019 item 1 shape.
3. **Cancellation:** the caller's token is the ONLY cancellation mechanism (contract §2 rule 1). `CompleteAsync` propagates OCE when the external token is cancelled; the pipeline translates to typed `Cancelled`. Timeout = linked-CTS `CancelAfter(RequestTimeout)` with `HttpClient.Timeout = Infinite`; OCE with linked-fired-and-not-external → `AiReply.Unavailable(timeout)` — disambiguation by token ORIGIN, never exception-type guessing (SP-019 item 2).
4. **Timeout default: 5 minutes** — the WPF-observed LOCAL value (`LocalAiService.cs:341,412`; cold model-load accommodation). Placeholder, not owner-approved; tests override to ~800ms. (Consult correction §4.1 point 3: my initial 30s cloud-derived default would have silently diverged from the WPF-observed local behavior.)
5. **Retry — typed placeholder, DEFAULT OFF (consult §4.1 point 2).** `AiRetryPolicy { Enabled, MaxRetries = 1, MaxDelay = 2s }`; `AiRetryPolicy.Off` is the default (conservative-posture reading of "configurable-off by default"; tiebreaker = the c6 none-admitted-default precedent). `AiRetryPolicy.WpfObservedPlaceholder` = the SP-019-proven shape (one retry, 429/5xx only, Retry-After honored clamped ≤2s — `OpenAiCompatibleService.cs:425-427` + spike limit 8), enabled explicitly by tests. Values stay owner-pending (§9.2 #6); the shape is typed and bounded either way.
6. **Refusal:** deterministic shape discrimination — a top-level `refusal` object with a string `category` → `AiReply.Refused(new AiModerationRefusal(category, AiModerationSource.Output))`. Exactly ONE attempt (never retried). No string-sniffing of reply text.
7. **Malformed/truncated:** full-document parse BEFORE any text extraction; invalid JSON, missing `message.content`, or a cut document → `AiReply.Unavailable(malformed-output)`. NEVER a partial apply; a truncated reply prefix is never surfaced (SP-019 item 6).
8. **Remote-host pre-socket rejection (in-product, two layers):** (a) pipeline layer — Descriptor.EndpointClass = `ClassifyOllamaHost(host)`; `LoopbackOnlyAdmissionPolicy` rejects `RemoteHostOllama` before the gateway, pipeline `SendAttempts` stays 0 (c1 landed); (b) provider layer — `CompleteAsync` re-classifies and returns `Unavailable(endpoint-not-admitted)` with the provider-side send counter at 0 (defense in depth; the provider NEVER opens a socket to a non-loopback host).
9. **Probe (SP-006 — the ONLY availability authority):** classify FIRST — non-loopback host → typed `Unavailable` (reason code `endpoint-not-admitted`, reused stable token) with ZERO socket contact (probing a remote host would itself be undeclared remote traffic — the pre-socket discipline extends to probes). Loopback → `GET {host}api/version` (the SP-019 item-8 probe URL) with a short bounded probe timeout (distinct from RequestTimeout — a 5-min request default must not wedge startup probing) → `Available` iff HTTP 200, with Detail honestly scoped: "ollama-shaped HTTP service reachable; model presence unproven" (no `/api/tags`, no pull — network minimization). Connection refused → `Unavailable(host-unreachable)` (the one additive code in `CapabilityReasonCodes`). Probe faults → `Faulted` via the existing runner machinery.
10. **Content-free diagnostics:** the provider emits NO log lines and NO diagnostic text; outcomes ride the typed `AiReply` and the pipeline's existing `AiDiagnosticRecord` emission (stable codes only). The lab's fake payloads (reply text with a per-run GUID) are the only "content"; they never enter committed artifacts — the Step-3 audit greps for them.

### 2.3 LAB matrix (Step 3, through the REAL pipeline unless noted)

| Row | Lab mode | Expected |
|-----|----------|----------|
| ok-roundtrip | Ok | Completed + `Generated(text, Loopback)`; pipeline SendAttempts==1; text matches the lab's per-run reply |
| mid-stream-cancel | HangStream (200 + partial body flush + stall) | token cancel (via SelectProvider) mid-stream → typed Cancelled FAST; generation advanced; lab observes `client-gone`; zero applied |
| timeout | Timeout (accept, never respond) | `Unavailable(timeout)` within the 800ms bound (+slack); external token NOT cancelled (subsequent op succeeds); lab observes `client-gone` after the bounded wait |
| rate-429 | Rate429 ×2 (Retry-After: 1) | retry ENABLED: exactly 2 lab hits, gap ≥ ~1s, final `Unavailable(quota-exhausted)`; retry DEFAULT (off): exactly 1 hit |
| error-500 | Error500 ×2 | retry ENABLED: exactly 2 hits → `Unavailable(http-500)`; no storm |
| refusal | Refusal | exactly 1 hit → `Refused(content_filter, Output)` typed carrier |
| malformed | Malformed (garbage) | exactly 1 hit → `Unavailable(malformed-output)`; no text surfaced |
| truncated | Truncated (valid prefix cut mid-document) | exactly 1 hit → `Unavailable(malformed-output)`; prefix NEVER surfaced |
| slow-ok-stale-discard | SlowOk (1.5s) + test-side uncooperative-transport decorator (forwards `CancellationToken.None` to the REAL provider — SP-019's RequestDetachedAsync shape) | switch mid-flight; late REAL body arrives; exactly 1 stale discard at the application seam (`Cancelled`, zero applied); lab record outcome == `completed` (consult §4.1 point 5) |
| live-panic | HangStream | `PanicAsync` during a REAL in-flight network operation → typed Cancelled + bounded drain + generation invalidation; lab observes `client-gone` |
| remote-pre-socket | none (no socket) | provider configured `http://192.168.1.50:11434/` → probe: typed Unavailable zero-socket; pipeline op: `Unavailable(provider-unproven)`, pipeline SendAttempts==0; direct provider call: `Unavailable(endpoint-not-admitted)`, provider SendAttempts==0 |
| offline-zero-network | none | no proven provider → interactive + awareness ops, SendAttempts==0 (c1 proof re-verified on the c2 tree); lab binds 127.0.0.1 only (proven by construction + assertion) |

WSL2 gate: NAMED LIMIT (§0) — zero distros on this laptop; Linux LAB evidence not faked; Windows-only LAB recorded.

## 3. Step 2–3 evidence (Windows lane worktree, 2026-08-04)

### 3.1 LAB matrix transcript (deterministic loopback lab, real sockets on 127.0.0.1, zero external network)

`dotnet test client/tests/CcpClient.Tests -c Debug --filter "FullyQualifiedName~AiProviderLabIntegrationTests|FullyQualifiedName~LoopbackOllamaProviderTests"` → **25/25 green** (13 provider-level + 12 pipeline-integration):

| Matrix row (SP-019 shape) | Test(s) | Observed |
|---------------------------|---------|----------|
| ok round-trip | `Ok_RoundTrip_Generated_WithLoopbackProvenance` + `Ok_ThroughPipeline_Completed_WithProvenance_AndContentFreeDiagnostic` | `Generated(lab-reply-<per-run>, Loopback)`; pipeline SendAttempts==1; content-free diagnostic (Completed, no stable code) |
| mid-stream cancel | `MidStreamCancel_TruePartialPosition_TokenIsTheMechanism` + `MidStreamCancel_Live_TypedCancelledFast_GenerationAdvanced_LabSeesClientGone` | TRUE partial-body position observed before cancel; typed Cancelled fast (<2s assertion, observed ms-scale); generation advanced via SelectProvider; lab record outcome `client-gone` — a cancelled transport cannot deliver a late result; zero applied |
| timeout | `Timeout_Classifier_Bounded_ExternalTokenNotCancelled` + `Timeout_ThroughPipeline_TypedUnavailable_TokenNotPoisoned_LabSeesClientGone` | `Unavailable(timeout)` inside the 800ms bound (+slack assertion); external token NOT cancelled (second op succeeds through the same pipeline — token not poisoned); lab `client-gone` after the bounded wait |
| 429 + Retry-After | `Rate429_RetryEnabled_ExactlyTwoHits_RetryAfterHonored` + `Rate429_RetryEnabled_ExactlyTwoHits_QuotaExhausted` | retry ENABLED: EXACTLY 2 lab hits (server-side count), gap ≥900ms (Retry-After: 1 honored, ≤2s clamp), `Unavailable(quota-exhausted)`; pipeline gateway counts 1/operation, provider seam 2/attempts |
| 429 default | `Rate429_RetryDefaultOff_ExactlyOneHit` | retry DEFAULT (off — conservative posture): exactly 1 hit |
| 500 | `Error500_RetryEnabled_Bounded_TwoHits` (provider + pipeline) | exactly 2 hits → `Unavailable(http-500)`; no storm |
| refusal | `Refusal_TypedCarrier_ExactlyOneAttempt_NeverRetried` + `Refusal_ThroughPipeline_TypedCarrier_ExactlyOneHit` | EXACTLY 1 hit even with retry enabled; typed `Refused(content_filter, Output)`; diagnostic outcome Refused |
| malformed | `Malformed_Garbage_NeverPartial_TypedUnavailable` + pipeline row | 1 hit → `Unavailable(malformed-output)`; never partial |
| truncated | `Truncated_PrefixCut_NeverSurfaced_TypedUnavailable` + pipeline row | valid-prefix cut mid-document → `Unavailable(malformed-output)`; the partial reply prefix is NEVER surfaced |
| slow-ok stale discard | `SlowOk_LateCompletion_ExactlyOneStaleDiscard_ZeroApplied` | test-side uncooperative-transport decorator (token swallowed; SP-019 RequestDetachedAsync shape) over the REAL socket/lab/body; switch mid-flight; late body REALLY arrives (lab outcome `completed`); EXACTLY 1 stale discard (`Registry.DiscardedStaleCompletions` +1); typed Cancelled; ZERO applied |
| live panic | `Panic_Live_DuringRealInFlightOperation_TypedCancelled_BoundedDrain_ClientGone` | panic during a REAL in-flight network operation (true mid-stream position first): typed Cancelled + bounded drain (<3.5s assertion, observed fast); lab `client-gone` — the c1 mechanism proven against a real socket, not a mock |
| remote pre-socket | `RemoteHost_RejectedPreSocket_ZeroSendAttempts` + `RemoteHost_InProduct_PreSocketRejection_BothLayers_ZeroSendAttempts` | `http://192.168.1.50:11434/` classified RemoteHostOllama; probe rejects typed `endpoint-not-admitted` with ZERO socket (<1s, the code only the pre-socket branch produces); pipeline op → `provider-unproven` with pipeline AND provider SendAttempts==0; direct provider call → `endpoint-not-admitted`, SendAttempts==0 |
| offline zero-network | `Offline_ReVerified_ZeroNetwork_LabBoundLoopbackOnly` | registered-but-unprobed provider: interactive + awareness ops → `provider-unproven`, SendAttempts==0 both layers, lab untouched (HitCount==0); lab binds 127.0.0.1 only (`Host.IsLoopback` asserted) |

### 3.2 WSL2 gate — NAMED LIMIT (never faked)

Verbatim probe (2026-08-04, this laptop): `wsl -l -q` → *(empty output)*, `exit=0` — WSL installed with **ZERO distros**. The packet's designed Linux gate (`~/ccp-sp035`, lab + contract green on Linux) cannot run: provisioning a distro is an owner decision (2026-08-04 amendment item 2). **No Linux evidence is claimed or faked** — the LAB matrix above is Windows-only; offline zero-network is re-verified on the Windows lab runs (lab binds 127.0.0.1 only, asserted in the offline row). The lab/provider code is platform-agnostic .NET (HttpListener + HttpClient loopback); the Linux run remains a pending owner-gated gate.

### 3.3 Sensitive-logging audit (SP-018/SP-019 discipline) — ZERO hits

Registered secrets for this slice (the lab's fake payloads are the only "content"):

| Secret | Shape | Where it may appear |
|--------|-------|---------------------|
| lab reply text | `lab-reply-<per-run-guid>` (runtime-generated) | generator line only (`AiProviderLab.cs:64`); per-run VALUES never committed |
| fake prompt | `lab-prompt` (static test input) | test-source input generator only; never in logs/diagnostics/artifacts |
| refusal category | `content_filter` | a stable machine TOKEN (the typed carrier's code), not content |

Audit commands + outcomes:
- `git grep -n "lab-reply-[0-9a-f]"` over committed files → **zero hits** (no per-run payload value anywhere in the tree).
- `git grep -n '"lab-reply-"'` → 2 hits, both the GENERATOR lines (`client/spikes/.../AiLab.cs:64` pre-existing quarantined spike; `client/tests/CcpClient.Tests/AiProviderLab.cs:64`).
- `git grep -n '"lab-prompt"'` → 2 hits, both test-source input generators.
- `grep -rn "lab-reply\|lab-prompt" spine-tasks/SP-035-ai-companion-c2/` → zero payload hits in artifacts (record.md carries only the stable `content_filter` token in design prose).
- Product-code log sites added by c2: **NONE** — `LoopbackOllamaProvider` emits no log lines and no diagnostic text (outcomes ride typed `AiReply` + the pipeline's content-free `AiDiagnosticRecord`); the lab is a test instrument that logs nothing. The SP-016 schema-level content-freedom proof and c1's `Diagnostics_NeverCarryText` test remain green (full suite).

### 3.4 Full-suite state after Step 3

`dotnet test client/tests/CcpClient.Tests -c Debug` → **491/491** (466 floor + 25 new). HeadlessTests unchanged (no new surface; c2 has no UI — recorded honestly absent in Step 5's contract run).


## 4. Consults

### 4.1 Pre-approach (Step 1)

**Mode:** solo (council forbidden — T-7). **Actual answering model:** the consult tool output carried NO model identifier (same truncation behavior as SP-033) — recorded honestly per T-2. Route per the 2026-08-04 rewire: Opus 5 main / Fable 5 fallback.

**Verdict (substantive points, all adopted):**
1. **Protocol: native `/api/chat` APPROVED** with the deviation recorded; ALSO: the refusal/429/Retry-After shapes are LAB CONSTRUCTS — the matrix proves client shape-handling, never that a real Ollama emits them. Record.md states this explicitly (§2.2 rule 1).
2. **Retry default: OFF, not ON.** "Configurable-off by default per the conservative posture" reads as retry off by default; tiebreaker = the c6 conservative none-admitted default precedent. My initial enabled-by-default reading was corrected. Tests enable the WPF-observed placeholder explicitly; a default-off exactly-1-hit row proves the default.
3. **Timeout default: 5 minutes (WPF-observed LOCAL value), not 30s.** WPF local uses 5 min for cold model loads (`LocalAiService.cs:341,412`); a 30s default would silently diverge from WPF-observed local behavior. Placeholder, overridable; tests use ~800ms. My initial 30s cloud-derived default was corrected.
4. **Probe:** classify-first with zero-socket remote rejection approved; keep additive capability codes minimal — reuse the `endpoint-not-admitted` stable token, add only `host-unreachable`; probe timeout short and DISTINCT from RequestTimeout; Available Detail must not overclaim model presence ("ollama-shaped service reachable; model presence unproven").
5. **Stale-discard decorator: honest** if named as a test-side uncooperative-transport decorator (SP-019 RequestDetachedAsync shape); the matrix must include BOTH the cooperative path (client-gone, no late result can arrive) AND the decorator path (late result arrives, discarded at the seam); assert the lab record outcome == `completed` for the decorator row.
6. Stream-read body approved (true mid-stream partial position).

### 4.2 Pre-completion (Step 4) — lands there

## 5. Engine review presence (T-2)

| Call | Result |
|------|--------|
| Step 1 plan review (`spine_review_step --step 1 --type plan`) | **Engine review ABSENT (expected)** — nested reviewer spawn blocked inside pi worker session; `skipped: true`, `spawnFailed: false` (SP-195: engine runs reviews after `.DONE`). Artifact: `.reviews/1-20260804T132924.md` |
| Step 2 plan review (`spine_review_step --step 2 --type plan`) | **Engine review ABSENT (expected)** — same SP-195 skip; `spawnFailed: false`. Artifact: `.reviews/2-20260804T134008.md` |
