# STATUS: SP-019 — spike cancellable AI providers and strict commands

**Current Step:** Step 1 — provider/command archaeology + spike design + pre-approach consult
**Last Updated:** 2026-07-21 (authored)

### Step 1: Provider/command archaeology + spike design + pre-approach consult
**Status:** ⬜ Not Started

- [ ] STATUS.md updated before starting work
- [ ] WPF + first-attempt archaeology (READ-ONLY, `File.cs:line`); command-safety REJECT lessons cited
- [ ] Spike design (fake OpenAI-compatible loopback endpoint + failure injection; cancellation via SP-004 generations; canary executor; redaction + audit)
- [ ] Ollama presence probe (session fact)
- [ ] Pre-approach solo Fable 5 consult (verdict + actual answering model in record.md BEFORE checkbox)

### Step 2: Loopback AI lab + redaction/audit core
**Status:** ⬜ Not Started

- [ ] `client/spikes/CcpSpike.AiProvider/` (out of solution; project reference to CcpClient.Desktop permitted)
- [ ] Fake `/v1/chat/completions` endpoint with deterministic failure injection
- [ ] Cancellable provider client (HttpClient + SP-004 generation tokens); redaction registry + `--audit-logs`

### Step 3: Strict-envelope fuzz evidence (zero-execution)
**Status:** ⬜ Not Started

- [ ] Fuzz matrix vs SP-016 real validator: mixed/invalid/moderated/out-of-range/malformed → NO plan + canary silent; valid → plan + canary records exactly its commands
- [ ] Per-command results vocabulary exercised; atomic rejection re-verified
- [ ] `--audit-logs` GREEN over the fuzz run

### Step 4: Provider-behavior evidence + WSL2 gate + record + pre-completion consult + board reconciliation
**Status:** ⬜ Not Started

- [ ] Provider matrix: cancellation (no late result), timeout (typed, no hang), 429 (typed, no retry-storm), 5xx, refusal, malformed/truncated (never partial apply)
- [ ] Remote-host rejection before socket open (policy test, no real remote); Ollama session fact or named limit; cloud named limit
- [ ] WSL2 in-packet gate (`~/ccp-sp019`, never /mnt/e): fuzz + lab green on Linux; contract green (pollution guard)
- [ ] `client/docs/ai-provider-spike.md` + record.md complete
- [ ] Pre-completion solo Fable 5 consult (verdict in record.md)
- [ ] Board row → `WIP` with evidence + named limits (never `DONE`)
- [ ] STATUS.md accurate before .DONE

### Step 5: Testing & Verification
**Status:** ⬜ Not Started

- [ ] Contract testCommand green (client 0W/0E + both test projects; spike host builds clean separately)
- [ ] `git diff --check` clean
- [ ] `git status --short` = File Scope only
