# AI provider spike — cancellable providers and strict commands

**Date:** 2026-07-21 · **Task:** SP-019 (task-board row: "Spike cancellable AI providers and strict commands") · **Status:** spike evidence; zero product-code change. Quarantined host `client/spikes/CcpSpike.AiProvider/` (NOT in `client/CcpClient.sln`; ProjectReference to `CcpClient.Desktop` only, to exercise SP-016's REAL `AiEnvelopeValidator` / vocabulary). Evidence: `spine-tasks/SP-019-ai-provider-spike/record.md`.

This spike exercises the SP-016 contract (`ai-operation-contract.md`) against a deterministic fake OpenAI-compatible loopback endpoint — the primary instrument, because a live model cannot produce timeout/429/refusal/malformed/mid-stream-cancel ON DEMAND. The strict command envelope is fuzzed against the real validator with a canary executor proving zero execution.

## Named observation per acceptance item

### 1. OpenAI-compatible endpoint behavior (instrument: deterministic fake loopback lab) — cancellation

**OBSERVED (Windows + Linux):** mid-stream cancellation against the lab's `HangStream` mode (200 headers + partial body, then stall): the client received a TRUE mid-stream position (18B partial body) before the token fired; cancellation produced typed `Cancelled` in 0ms (no hang); the owning generation advanced and the cancelled result was discarded at the application seam (SP-004 §3 rule 2); the lab observed `client-gone` — a cancelled transport cannot deliver a late result. **Zero late results applied.**

### 2. Timeout (fake loopback lab)

**OBSERVED (Windows + Linux):** lab `Timeout` mode (accept, never respond): typed `Timeout` outcome at 797ms against an 800ms bound (no hang); the external token was NOT cancelled — timeout is a failure classifier, never the cancellation mechanism (contract §2 rule 1); the token-source disambiguation is enforced by linked-CTS origin, not exception-type guessing; the lab observed `client-gone` after the bounded wait.

### 3. Rate / 429 (fake loopback lab)

**OBSERVED (Windows + Linux):** typed `RateLimited` → `AiReply.Unavailable(quota-exhausted)`; EXACTLY 2 lab hits (initial + one bounded retry — the WPF-observed policy shape, `OpenAiCompatibleService.cs:425-427`); Retry-After: 1 honored (1015ms ≥ ~1s); **no retry-storm** (hit counts are mode-scoped lab records, not client-side hope).

### 4. Error / 5xx (fake loopback lab)

**OBSERVED (Windows + Linux):** typed `ServerError` → `AiReply.Unavailable("http-500")`; EXACTLY 2 lab hits (bounded retry, no storm).

### 5. Refusal (fake loopback lab)

**OBSERVED (Windows + Linux):** typed `Refused` with a typed refusal carrier (`content_filter`, `Output` source) from a deterministic refusal shape — no string-sniffing (contract §7 rule 3: the sentinel-string channel is rejected); EXACTLY 1 lab hit (no retry on refusal).

### 6. Malformed / truncated output (fake loopback lab)

**OBSERVED (Windows + Linux):** both lab `Malformed` (garbage) and `Truncated` (valid-envelope prefix cut mid-document, reply text partially present) → typed `MalformedOutput` → `AiReply.Unavailable(malformed-output)`; EXACTLY 1 lab hit each; **never a partial apply** — no reply text surfaced, no commands; the content-free `AiDiagnosticRecord` records the typed outcome with zero commands (contract §12).

### 7. Remote-host rejection (policy test — no real remote)

**OBSERVED (Windows + Linux):** the spike-local admission policy (loopback-only; allow-list governance pending-owner per contract §6 rule 3) rejected every non-loopback endpoint BEFORE any socket opened — proven by the send-attempt counter staying 0 and sub-millisecond returns: unroutable TEST-NET-1 `192.0.2.1` (classified `ThirdPartyCloud`), a non-loopback Ollama-shaped host `192.168.1.50:11434` (classified `RemoteHostOllama` — the "local AI = local-only data" rejected assumption), a nonexistent DNS name `nonexistent.invalid` (rejected pre-DNS — classification is config-pure, contract §6 rule 2), and the `localhost.` trailing-dot near-miss (literal-only loopback classification — no DNS probe).

### 8. Loopback Ollama

**NAMED LIMIT:** Ollama is ABSENT on this box — `GET http://localhost:11434/api/version` connection failure on Windows AND WSL2; no `ollama` binary on PATH. No real-Ollama round-trip exercisable this session. The Ollama request/response archaeology is recorded in record.md §1.1 (`LocalAiService.cs:439-455,478-535`).

### 9. Cloud (first-party proxy)

**NAMED LIMIT:** no credentials exist on this box and none were invented. The cloud auth/request shapes are archaeology-only (record.md §1.1: `AiService.cs:27,360-466`).

### 10. Strict command schema fuzz — zero execution

**OBSERVED (Windows + Linux): 62 fuzz cases green** against SP-016's real `AiEnvelopeValidator` with a canary executor (spike-side only). Per payload class:

- **mixed (valid+invalid):** whole-envelope atomic rejection re-verified — valid siblings typed `NotExecuted(EnvelopeRejected)`, reply text suppressed (contract §9 rule 4), `Plan == null`, canary silent. Zero execution.
- **invalid schema:** unknown commands + enum near-misses (casing/space), missing/wrong-type/extra fields at command and data level → typed `UnknownCommand`/`MalformedData` verdicts; envelope rejected; canary silent. Model-supplied field names never leak into verdicts (`(unrecognized)` token).
- **moderated:** `Block` verdict → `ModerationBlocked`, envelope accepted, the command is NOT in the plan — the canary never sees it; a valid sibling executes (gating is per-command post-validation, contract §8 rule 6). `SoftHit` pass-through recorded (taxonomy behavior; policy values pending-owner).
- **out-of-range:** numeric bounds (amount ±, overlay 31, haptic 0.61/-0.1, delay 0/601), lengths (81/201 chars; astral UTF-16 boundary 78+astral=80 valid / 79+astral=81 rejected) → typed `OutOfRange` naming field + bound, never the value; canary silent.
- **malformed JSON:** truncated, garbage, trailing comma, comment, bare NaN, depth bomb (64 > MaxDepth 16), envelope-root non-objects → typed envelope rejections; canary silent.
- **valid (falsifiable pair):** every accepted envelope's plan handed to the canary records EXACTLY the plan's commands in order (boundary values 0/8/10/150/100/30/60/5/600, empty-commands plan, cap 4-of-3 → exactly 3 recorded).

**Finding F1 (validator behavior gap, OBSERVED — reported, not fixed):** `System.Text.Json` accepts duplicate object keys; the validator's `TryGetProperty` is last-wins while `EnumerateObject` sees every occurrence. `{"amount":9,"amount":1,...}` (first out-of-range) is ACCEPTED with the last value; reversed order rejects. This is a parser-differential hazard against the contract §8 strict-schema intent (a downstream first-wins re-parser would see a different value than the validator validated) — **follow-up fix filed for the AI implementation row: reject duplicates** (the only answer consistent with the owner-ratified contract §8); zero product-code change in this spike.

The zero-execution proof is enforced at the assembly boundary, not by convention: the spike is a SEPARATE assembly with no `InternalsVisibleTo`, so `AiExecutionPlan`'s internal constructor is genuinely unconstructible from consumer code — an invalid envelope has no executable representation at the type level (contract §8 rule 4).

### 11. No sensitive logs

**OBSERVED (Windows + Linux):** central redaction registry (SP-018 pattern): fake Bearer API key, prompt payloads, and the lab reply text are registered secrets; every log line passes through `Redact.Scrub`; auth/payloads appear as presence+length shapes only. `--audit-logs` self-check re-registers from the gitignored registry and scans every emitted log: **GREEN on both platforms** (zero secret values in logs).

## Session facts

- Windows: fuzz 62/62, matrix 39/39 checks, selftest 15/15, audit GREEN; contract pollution guard green (`client/CcpClient.sln` 0W/0E, 213/213 unit + 22/22 headless — identical counts to SP-016/017/018).
- WSL2 (`~/ccp-sp019`, never /mnt/e): fuzz 62/62, matrix green, selftest green, audit GREEN on Linux (the lab is loopback — real Linux evidence); contract green on Linux (213/213 + 22/22).
- The typed vocabulary, endpoint classifier, envelope validator, and diagnostic codes under test are SP-016's REAL product code (`client/src/CcpClient.Desktop/Ai/`) — unchanged by this spike.

## Named limits (row stays WIP)

1. **Ollama absent** (item 8) — real-Ollama round-trip not exercisable; provider matrix evidence is the deterministic lab.
2. **Cloud credentials** (item 9) — cloud/approved-remote-endpoint paths not exercisable; no credentials invented.
3. **Moderation policy values pending-owner** — moderated = verdict-rejected shape only; category list/wordlist/soft-hit policy are owner decisions (contract §7 rule 5).
4. **Endpoint allow-list governance pending-owner** — the spike demonstrates the enforcement MECHANISM with a minimal loopback-only policy; admissible remote hosts are an owner decision (contract §6 rule 3).
5. **Admissible command set pending-owner** — the 11 WPF command types are the inventory baseline only (contract §8 rule 1).
6. **Finding F1** — duplicate-key validator gap filed as a follow-up fix (reject duplicates) for the AI implementation row; not an owner policy question (contract §8 already ratified strict-schema intent).
7. **`NotExecuted(SupersededGeneration)` is not validator-reachable** — the validator can never emit it (no execution pipeline exists); generation supersession is proven at the operation level (matrix stale rows); the per-command verdict lands with the execution row.
8. Retry policy values (one bounded retry, ≤2s clamp) are the WPF-observed shape used by the spike harness, not an owner-approved contract value.
