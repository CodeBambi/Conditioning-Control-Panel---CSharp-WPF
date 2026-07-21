# SP-019 record — spike cancellable AI providers and strict commands

**Task:** task-board row "Spike cancellable AI providers and strict commands" (P0, Phase 4).
**Worker:** kimi-coding/k3 · **Batch:** 20260721T130248 lane-1 · **Review Level:** 2 (Plan and Code)

Engine-review presence per `spine_review_step` call (T-2 fixed format) is recorded per step below.

---

## Step 1 — provider/command archaeology + spike design + pre-approach consult

### 1.1 WPF + first-attempt archaeology (READ-ONLY, `File.cs:line`)

**Cloud proxy client (WPF, `ConditioningControlPanel/Services/AiService.cs`):**
- Endpoint: hardcoded `https://codebambi-proxy.vercel.app` (`AiService.cs:27`); AI_AUDIT §10 inventory.
- Auth shapes: V2 `POST /v2/ai/chat` with `unified_id` body field + `X-Auth-Token` header (`AiService.cs:360-379`); legacy `POST /ai/chat` with Patreon `Bearer` token (`AiService.cs:447-466`). 404 on V2 → silent legacy fallback (`AiService.cs:370-376`) — the contract §3 rule 4 REJECTS silent endpoint fallback.
- Timeout: `HttpClient.Timeout = 30s` (`AiService.cs:62-66`); `TaskCanceledException` → null (`AiService.cs:421-424`) — timeout and cancellation are indistinguishable; the contract §2 rule 1 makes timeout a failure classifier, never the cancellation mechanism.
- Rate: client-side circuit breaker only, free 100 / Patreon 1000 daily (`AiService.cs:33-42`); server `RequestsRemaining` is authoritative when present (`AiService.cs:398-405`). **No 429 handling at all** — any non-success status logs status+body and returns null (`AiService.cs:383-388`). No retry, no backoff.
- Response shape: `ProxyChatResponse { content, error, requestsRemaining? }` (`AiService.cs:390-396`); empty content → null.
- Refusal: sentinel strings `ModerationRefusal.InputSentinel/OutputSentinel` threaded through the legacy string API (`AiService.cs:95-108`) — contract §7 rule 3 REJECTS the sentinel channel.
- `IsAvailable` is an identity check (cloud identity or Patreon access — `AiService.cs:52`), evidence of the wrong shape per contract §11 rule 2 (SP-006: selection/registration never prove availability).

**Ollama client (first-attempt Core port, faithful to WPF local contract — `ConditioningControlPanel/CCP.Core/Services/AIService/LocalAiService.cs`):**
- Endpoint: `{host}api/chat`, default `http://localhost:11434/` (`LocalAiService.cs:516-521` NormalizeHost); `EnsureHost` accepts ANY absolute URI with a log-only warning (`LocalAiService.cs:478-489`) — the contract §6 `RemoteHostOllama` gap: a non-loopback "local" host is remote.
- Request shape: `{ model, messages[{role,content}], stream=false, think=false }`, no options on the chat path (`LocalAiService.cs:439-447`); raw completion (quiz) adds `options.temperature` (`LocalAiService.cs:386-403`).
- Response shape: `{ message: { content } }` via `ExtractContent` (`LocalAiService.cs:526-535`); malformed JSON → catch-swallowed null.
- Timeout: `HttpClient.Timeout = 5min` (`LocalAiService.cs:486`); `TaskCanceledException` → null "timed out" (`LocalAiService.cs:455`).
- Queue/drop: single-flight `_isProcessing` gate — second user click → "still thinking" phrase; ambient reactions dropped while busy (`LocalAiService.cs:158-164,295`).
- History: 50-pair cap, `local_chat_history.json`, `ChatMemoryEnabled` gate (`LocalAiService.cs:74,404-431`); moderation sandwich with the rollback rule — output block rolls back BOTH turns and persists nothing (`LocalAiService.cs:222-232`).

**OpenAI-compatible provider (WPF `ConditioningControlPanel/Services/AIService/OpenAiCompatibleService.cs`; first-attempt `CCP.Core/Services/AIService/OpenAiService.cs`):**
- Endpoint normalization is EXACT: strip a trailing `/chat/completions`, then append exactly `chat/completions` (`OpenAiCompatibleService.cs:134-136,248`; `OpenAiService.cs:43-46,473-474`) — reproduce or 404.
- Auth: `Authorization: Bearer <apiKey>` (`OpenAiCompatibleService.cs:266,415`).
- Timeout 60s (`OpenAiCompatibleService.cs:110-112`; `OpenAiService.cs:86`).
- Retry: ONE retry on 429 / >=500 / transport errors (`OpenAiCompatibleService.cs:425-427`; `OpenAiService.cs:335`) — bounded retry is the WPF-observed policy shape; the contract names no retry policy (owner value), so the spike asserts NO retry-storm against an explicit bounded cap.
- Diagnostics: typed `ConnectionDiagnosticResult`/`DiagnosticCategory` (Success/MissingConfiguration/Endpoint/Authentication/Model/Timeout/Connection/Http/Unknown — `OpenAiService.cs:574`, `OpenAiCompatibleService.cs:330`) — ADAPTED per contract §12 as content-free typed diagnostics.

**Effects command execution (the zero-execution ground truth):**
- Lenient parse: `AiResponseParser` brace-balancing + regex repair (trailing-comma stripping, unquoted-key quoting, newline escaping) + mixed-format salvage (`CCP.Core/Services/AIService/AiResponseParser.cs:159-232` BalanceBraces/RepairJson) — contract §8 rule 2 REJECTS all of it (zero repair).
- Dispatch: `CCP.Avalonia/Services/Commands/AiCommandService.cs:87` `async void ExecuteCommand` fire-and-forget; pipeline: null-drop → master gate → per-effect gate → batch cap 3 (`MaxCommandsPerResponse`, `:52,103`) → execute. An invalid/unallowed command is DROPPED while siblings execute (`:87-103`) — the exact partial-execution shape the contract §8 rule 3 REJECTS ("partial/no-op effects reported as success").
- Silent clamps everywhere: `Math.Clamp` on every numeric field (`AiCommandService.cs:146-260`) — contract §8 rule 5 replaces clamp-to-valid with a typed `OutOfRange` rejection verdict.
- Media path validation (`GetValidatedPath`, `:290-309`): traversal/UNC reject, root-containment with trailing separator, extension allow-list — ACCEPTED by contract §8 rule 5 as envelope rules (implemented in SP-016's validator).
- Command-safety REJECT lessons cited: first-attempt-lessons.md:105-110 (REJECT string-inferred failures, no cancellation, silent endpoint fallback, remote host called "local", lenient command repair, partial/no-op effects as success) and the systemic rejects first-attempt-lessons.md:166-167 ("Parsed AI output implies an effect executed safely" — strict validation + moderation + permission + cancellation + per-command results required; "Selecting local AI guarantees local-only data" — non-loopback Ollama is remote).

### 1.2 Spike design

Quarantined host `client/spikes/CcpSpike.AiProvider/` (console, net10.0, **NOT** in `client/CcpClient.sln`; `ProjectReference` to `client/src/CcpClient.Desktop/CcpClient.Desktop.csproj` to exercise SP-016's REAL validator — references are not product-code changes):

1. **`AiLab`** — HttpListener on 127.0.0.1 ephemeral port (SP-018 Lab pattern) serving the OpenAI-compatible `POST /v1/chat/completions` shape with a deterministic failure-injection control surface (next-response mode): `ok` (valid strict-envelope JSON), `timeout` (accept + never respond until the client goes away), `rate429` (429 + Retry-After), `error500`, `refusal` (provider-refusal JSON shape), `malformed` (syntactically invalid JSON), `truncated` (cut mid-JSON), `hang-stream` (headers + partial body, then stall — the mid-stream cancellation instrument).
2. **`SpikeAiClient`** — HttpClient-based cancellable provider client: SP-004 generation discipline (per-request generation id; a completion arriving for a stale generation is discarded at the point of application); typed outcomes per contract §1/§11 (`AiReply.Generated/Refused/Unavailable/Fallback` + `AiReplyCodes.Timeout/QuotaExhausted/MalformedOutput/Offline`); bounded per-request timeout; bounded retry (cap 1, matching the WPF-observed shape) asserted non-storm by lab hit-count; content-free `AiDiagnosticRecord` per operation (contract §12). **Endpoint admission policy (spike-local, pending-owner values): only `AiEndpointClass.Loopback` is admitted — non-loopback endpoints are rejected BEFORE any socket opens** (the remote-host policy test; allow-list governance stays pending-owner per contract §6 rule 3).
3. **Fuzz runner** — payload classes × SP-016's real `AiEnvelopeValidator`: valid (falsifiable pair), mixed valid+invalid, invalid schema (wrong types / missing / extra fields), moderated (policy `ModerateText` returns `Block` — verdict-rejected shape only; policy values pending-owner), out-of-range (numeric bounds, lengths, enum near-misses), malformed JSON (truncated / duplicate keys / depth bombs within and beyond the validator's declared MaxDepth=16). **`CanaryExecutor`** records every command it is handed (kind + shape tokens only, no payload text); rejected envelopes produce NO `AiExecutionPlan` (type-enforced — `Plan` is null) so the canary is never invoked; valid envelopes must produce plans whose commands the canary records EXACTLY. The pair is falsifiable both directions.
4. **Redact + SpikeLog + `--audit-logs`** (SP-018 pattern): registered secrets include the fake Bearer API key, prompt/user-text payloads sent to the lab, and any token-bearing URL component; every log line passes through `Redact.Scrub`; `--audit-logs DIR` re-registers from the gitignored registry and FAILS on any hit.

### 1.3 Ollama presence probe (session fact, 2026-07-21)

`GET http://localhost:11434/api/version` → connection failure (curl exit, HTTP 000); no `ollama` binary on PATH. **Ollama is ABSENT on this box → named limit** (real-Ollama round-trip not exercisable; WSL2 gate re-probes on Linux in Step 4).

### 1.4 Pre-approach consult (solo Fable 5 — council unavailable per T-7)

**Route:** solo consult (Fable-sanctioned per board gate history; actual answering model not self-identified in output, recorded honestly) · 2026-07-21.

**Verdict: design ADEQUATE with additions.** Applied to the design:
1. **Fuzz matrix additions:** (a) envelope-root rejection classes — root-not-object (array/string/number/bool/null), unknown root field, `reply`/`commands` wrong type; (b) **duplicate-key probe** — `JsonDocument.Parse` ACCEPTS duplicate keys (first-wins via TryGetProperty while EnumerateObject sees all): a payload whose first occurrence is in-range and second out-of-range may be silently accepted → record as an observed validator finding, assert current behavior honestly, do not overclaim rejection; (c) cap-exceeded case (4 valid commands, cap 3 → 4th = `NotExecuted(CapExceeded)`, envelope accepted, plan = 3); (d) consent-gated classes (master off; per-effect off); (e) SoftHit pass-through (only Block gates); (f) boundary lengths (80/81 chars, 200/201, Unicode astral chars near the limit); (g) number shape near-misses (1.0 vs 1 for int fields, huge exponents, NaN/Infinity as bare tokens); (h) empty `commands: []` (accepted, empty plan); (i) media-path classes (traversal/UNC/bad extension/escapes-root/path-and-random); (j) getbacktome boundaries (delay 0/1/600/601, empty token); (k) rejected-envelope reply-null invariant (contract §9 rule 4).
2. **Provider-matrix silent-pass traps:** (a) timeout vs cancellation disambiguation — `HttpClient.Timeout` TaskCanceledException carries an inner `TimeoutException` on .NET 5+; assert token NOT cancelled for the timeout row and token-cancelled for the cancel row, otherwise a timeout mislabeled as cancellation silently passes; (b) stale-discard must be LIVE — release the lab hang AFTER cancellation and assert a real late completion ARRIVED (lab-side request-finished record) and was discarded (applied-results == 0 + explicit stale-discard log line); zero applied results alone is vacuous if the request died in transport; (c) retry non-storm = EXACT lab hit counts (429: 2 = initial+1 retry; 500: 2; timeout: bounded; malformed/refusal: EXACTLY 1 — no retry on parse/refusal); (d) refusal rows must count lab hits and assert the typed variant — refusing on any string parse failure would pass on the wrong shape.
3. **Loopback-only admission policy = honest shape** (minimal conservative policy compatible with the loopback lab; allow-list governance stays pending-owner). Rejection demonstrated on BOTH `RemoteHostOllama` and `ThirdPartyCloud` classes — a policy that rejects only one class could be an ollama special-case passing accidentally. Include a DNS-name-to-loopback near-miss (e.g. `localhost.` with trailing dot, or a name that would resolve to 127.0.0.1): `Uri.IsLoopback` is literal-only, classification is config-pure, rejection proves no DNS probe.
4. **Cancellation/generation proof hardening:** (a) instrument a single client send seam (send-attempt counter) — policy-rejected endpoints assert counter == 0 (proof no socket, not just fast return); also use an unroutable-but-non-loopback IP (192.0.2.1 TEST-NET-1) and a nonexistent DNS name: policy rejection must be instant + code = policy (never connect-refused/DNS-error); (b) hang-stream row asserts partial-body-bytes > 0 before cancel (true mid-stream, not header-time); (c) **dual-transport stale proof:** cancelled transport dies by token (no late result exists), so ALSO drive a detached/lab-delayed completion path that ignores the token and returns after generation advance — assert exactly one discard at the application seam (SP-004's actual mechanism is at application time); (d) assert cancelled rows leave no in-flight work (pending-count zero, no unobserved task exceptions).

**Step 1 engine-review presence (T-2):** `spine_review_step(step=1, type=plan)` → `skipped=true` (nested_spawn_blocked by design, SP-195/SP-278), `spawnFailed=false`, `reviewLevel=2` echoed. Engine runs plan/code review after .DONE.

Design updated accordingly; Step 2 implements.

---

## Step 2 — loopback AI lab + redaction/audit core

`client/spikes/CcpSpike.AiProvider/` (quarantined, NOT in `client/CcpClient.sln`; ProjectReference to `CcpClient.Desktop` only):
- `AiLab.cs` — HttpListener 127.0.0.1 ephemeral, POST `/v1/chat/completions` only; in-proc failure-injection mode queue (Ok/Timeout/Rate429/Error500/Refusal/Malformed/Truncated/HangStream); per-request lab-side records (auth presence+len, body byte count, client-gone detection — the falsifiable server side).
- `SpikeAiClient.cs` — cancellable client: loopback-only admission policy (SendAttempts counter proves zero sockets on rejection), linked-CTS per-request timeout disambiguated from external cancellation (`OperationCanceledException when ct.IsCancellationRequested` vs linked-fired-only), exactly-one bounded retry on 429/5xx with clamped Retry-After, stream reads with partial-byte counting, SP-004 application seam (`ApplyResult` — stale generations discarded with explicit STALE-DISCARD record), detached completion path for the dual-transport stale proof, content-free `AiDiagnosticRecord` per operation; envelope validation via SP-016's REAL `AiEnvelopeValidator`.
- `Redact.cs`/`SpikeLog.cs`/`Program.cs` — SP-018 pattern; `--audit-logs DIR`.
- `SelfTest.cs` — Step-2 smoke: Ok round-trip (typed Completed + canary pair), policy rejection (ThirdPartyCloud 192.0.2.1, 0 send attempts, 0ms), bounded timeout (812ms for an 800ms bound, no hang). ALL PASS on Windows; `--audit-logs` GREEN.

**Step 2 engine-review presence (T-2):** `spine_review_step(step=2, type=plan)` → `skipped=true` (nested_spawn_blocked by design), `spawnFailed=false`, `reviewLevel=2` echoed.

---

## Step 3 — strict-envelope fuzz evidence (zero-execution)

`Fuzz.cs`: **62 cases GREEN on Windows** (`--fuzz`), `--audit-logs` GREEN over the fuzz run. Every rejected class: `Plan == null` (type-enforced — the validator's internal `AiExecutionPlan` ctor), `Reply == null` (contract §9 rule 4), canary silent. Every accepted class: canary records EXACTLY the plan's commands in order.

Coverage: valid falsifiable pairs (single, numeric boundaries 0/8/10/150/100 + 30 + 60 + 5 + 600, text limits 80/200, astral-char UTF-16 boundary 78+astral=80 valid / 79+astral=81 rejected, empty commands, getbacktome 1/600) · envelope-root rejections (array/string/number/bool/null, unknown field, reply/commands wrong type) · malformed JSON (truncated, garbage, trailing comma, comment, NaN token, depth-64 bomb > MaxDepth 16, balanced shallow array → root-not-object) · duplicate-key probe (below) · invalid schema (unknown command ×1 + enum near-misses casing/space, missing/wrong-type command/data fields, extra fields at command+data level, string/float/float-int/bool wrong types, int32-overflow-as-wrong-type observed) · mixed atomic rejection (valid siblings → `NotExecuted(EnvelopeRejected)`, reply suppressed, canary silent — contract §8 rule 3 re-verified) · out-of-range (amount ±, 81/201 text, 31 overlay, haptic 0.61/-0.1, delay 0/601, empty token) · media paths (traversal, UNC, extension, escapes-root, path-and-random, valid-under-root) · moderated (`Block` → `ModerationBlocked` verdict, envelope accepted, command NOT in plan, canary never sees it; mixed block+valid → valid sibling executes, moderated never; `SoftHit` pass-through recorded) · consent-gated (master off, per-effect off) · cap (4-of-3: 4th = `NotExecuted(CapExceeded)`, canary exactly 3). Vocabulary coverage asserted: all 7 verdict types + both fuzz-reachable NotExecuted reasons; `AiDiagnosticCodes.VerdictCode` closed mapping never returns "unknown".

**Finding F1 (duplicate-key semantics, OBSERVED, honestly recorded):** `System.Text.Json` accepts duplicate object keys; the validator's `TryGetProperty` is last-wins while `EnumerateObject` sees every occurrence. A payload with `amount:9, amount:1` (first out-of-range, last in-range) is ACCEPTED with the last value; the reverse order rejects. Duplicate keys are NOT rejected per se — a validator behavior gap vs strict-schema intent, recorded for the owner/follow-up (no product-code change in this spike).

**Step 3 engine-review presence (T-2):** `spine_review_step(step=3, type=plan)` → `skipped=true` (nested_spawn_blocked by design), `spawnFailed=false`, `reviewLevel=2` echoed.

---

## Step 4 — provider-behavior evidence + WSL2 gate

`Matrix.cs`: **39 checks GREEN on Windows, GREEN on Linux** (run in `~/ccp-sp019`, never /mnt/e — the lab is loopback = real Linux evidence). Two first-run failures found and fixed (honest record): (1) lab hang/timeout modes' disconnect probe (WriteByte without flush) never detected a dead client → strengthened to write+flush; (2) 429 hit-count raced with the row-2 timeout request's LATE lab record → hit assertions made mode-scoped (per-mode record counts), and the timeout row now synchronizes on the lab's client-gone record before the hit-count rows. Evidence detail in `client/docs/ai-provider-spike.md` §1-§9 (named observation per acceptance item).

Key rows: mid-stream cancel (18B partial body before cancel, typed Cancelled in 0ms, generation advanced, lab observed client-gone — no late result can arrive on a cancelled transport) · timeout (797ms vs 800ms bound, token NOT cancelled — linked-CTS origin disambiguation) · 429 (typed quota-exhausted, EXACTLY 2 hits, Retry-After 1015ms honored, no storm) · 500 (typed, exactly 2) · refusal (typed Refused/content_filter/Output, exactly 1 hit) · malformed + truncated (typed MalformedOutput, exactly 1 hit, never partial apply — the truncated reply-text prefix never surfaced) · **LIVE stale discard** (lab SlowOk: a real late completion ARRIVED after generation advance; exactly 1 STALE-DISCARD at the application seam, zero applied — the dual-transport proof per the pre-approach consult) · remote-host rejection ×4 (ThirdPartyCloud 192.0.2.1, RemoteHostOllama 192.168.1.50, nonexistent.invalid pre-DNS, `localhost.` trailing-dot near-miss — all: policy code, sendAttempts=0, sub-ms) · Ollama probe ABSENT (named limit) · cloud named limit · hygiene: zero unobserved task exceptions.

**WSL2 gate (`~/ccp-sp019`):** spike build 0E; fuzz 62/62; matrix GREEN; selftest GREEN; `--audit-logs` GREEN on Linux. Contract pollution guard green on BOTH platforms — Windows: sln 0W/0E, 213/213 unit + 22/22 headless; WSL2: 213/213 + 22/22 (identical counts to SP-016/017/018).

**Step 4 engine-review presence (T-2):** `spine_review_step(step=4, type=plan)` → `skipped=true` (nested_spawn_blocked by design), `spawnFailed=false`, `reviewLevel=2` echoed.

### 4b — Pre-completion consult (solo Fable 5, 2026-07-21)

**Route:** solo consult (Fable-sanctioned per board gate history; actual answering model not self-identified in output, recorded honestly).

**Verdict: evidence sound — proceed, with four corrections (all applied):**
1. Doc acceptance-item headings renamed to name the instrument ("fake loopback lab") — the "Approved OpenAI-compatible endpoint" heading overclaimed (no real approved endpoint contacted); board wording leads with the fake lab as primary instrument.
2. Named limit added: `NotExecuted(SupersededGeneration)` is not validator-reachable (execution-pipeline vocabulary; supersession proven at operation level; verdict lands with the execution row).
3. Zero-execution strengthened: the spike is a separate assembly with NO `InternalsVisibleTo` anywhere in `client/src` (verified by grep) — `AiExecutionPlan`'s internal ctor is genuinely unconstructible from consumer code; type-enforcement is proven at the assembly boundary.
4. Finding F1 recategorized: NOT a Decisions-needed owner question — it is an engineering defect against the owner-ratified contract §8 strict-schema intent (parser-differential hazard); filed as a follow-up fix (reject duplicates) for the AI implementation row.

---

## Step 5 — testing & verification

- Contract testCommand (Windows): `dotnet build client/CcpClient.sln -c Debug` **0W/0E**; `CcpClient.Tests` **213/213**; `CcpClient.HeadlessTests` **22/22** — pollution guard green.
- Contract on WSL2 (`~/ccp-sp019`): build 0E, 213/213 + 22/22 — green.
- Spike host builds clean separately (0W/0E); fuzz 62/62 + matrix 39 checks + selftest + `--audit-logs` GREEN on Windows AND Linux.
- `git diff --check` clean; `git status --short` = File Scope only.
- Quarantine audit (`git diff --name-only 42c0739f..HEAD`): only `client/spikes/CcpSpike.AiProvider/**`, `client/docs/ai-provider-spike.md`, `client/docs/task-board.md`, `spine-tasks/SP-019-ai-provider-spike/**` — zero `client/src` / `client/tests` / `client/CcpClient.sln` / `ConditioningControlPanel/**` / `.spine/**` changes.
- Both new docs valid UTF-8 (iconv verified — CP1252 lesson).
- `fileScopeMustChange` ✓ both changed; `artifactsMustExist` ✓ both exist; `fileScopeMustNotChange` ✓ untouched.

**Step 5 engine-review presence (T-2):** `spine_review_step(step=5, type=plan)` → recorded after the call below.
