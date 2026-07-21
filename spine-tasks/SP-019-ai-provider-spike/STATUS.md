# STATUS: SP-019 — spike cancellable AI providers and strict commands

**Current Step:** Step 5 — testing & verification
**Last Updated:** 2026-07-21 (worker, Step 4 complete)

### Step 1: Provider/command archaeology + spike design + pre-approach consult
**Status:** ✅ Complete

- [x] STATUS.md updated before starting work
- [x] WPF + first-attempt archaeology (READ-ONLY, `File.cs:line`); command-safety REJECT lessons cited
- [x] Spike design (fake OpenAI-compatible loopback endpoint + failure injection; cancellation via SP-004 generations; canary executor; redaction + audit)
- [x] Ollama presence probe (session fact)
- [x] Pre-approach solo Fable 5 consult (verdict + actual answering model in record.md BEFORE checkbox)

### Step 2: Loopback AI lab + redaction/audit core
**Status:** ✅ Complete

- [x] `client/spikes/CcpSpike.AiProvider/` (out of solution; project reference to CcpClient.Desktop permitted)
- [x] Fake `/v1/chat/completions` endpoint with deterministic failure injection
- [x] Cancellable provider client (HttpClient + SP-004 generation tokens); redaction registry + `--audit-logs`

### Step 3: Strict-envelope fuzz evidence (zero-execution)
**Status:** ✅ Complete

- [x] Fuzz matrix vs SP-016 real validator: mixed/invalid/moderated/out-of-range/malformed → NO plan + canary silent; valid → plan + canary records exactly its commands
- [x] Per-command results vocabulary exercised; atomic rejection re-verified
- [x] `--audit-logs` GREEN over the fuzz run

### Step 4: Provider-behavior evidence + WSL2 gate + record + pre-completion consult + board reconciliation
**Status:** ✅ Complete

- [x] Provider matrix: cancellation (no late result), timeout (typed, no hang), 429 (typed, no retry-storm), 5xx, refusal, malformed/truncated (never partial apply)
- [x] Remote-host rejection before socket open (policy test, no real remote); Ollama session fact or named limit; cloud named limit
- [x] WSL2 in-packet gate (`~/ccp-sp019`, never /mnt/e): fuzz + lab green on Linux; contract green (pollution guard)
- [x] `client/docs/ai-provider-spike.md` + record.md complete
- [x] Pre-completion solo Fable 5 consult (verdict in record.md)
- [x] Board row → `WIP` with evidence + named limits (never `DONE`)
- [x] STATUS.md accurate before .DONE

### Step 5: Testing & Verification
**Status:** 🔄 In Progress

- [ ] Contract testCommand green (client 0W/0E + both test projects; spike host builds clean separately)
- [ ] `git diff --check` clean
- [ ] `git status --short` = File Scope only
