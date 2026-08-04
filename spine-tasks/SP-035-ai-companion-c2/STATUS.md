## STATUS: SP-035 — AI companion slice c2: loopback Ollama provider
**Current Step:** Step 3 — LAB matrix + live panic + WSL gate
**Last Updated:** 2026-08-04 (Step 2 complete: provider + lab + 25 new tests, full suite 491/491)
**Blockers:** none

### Step 1: archaeology + lab design + pre-approach consult
- [x] Update STATUS.md before starting work
- [x] WPF archaeology (Ollama client shape, retry placeholder, timeout/cancel, refusal)
- [x] Design (LoopbackOllamaProvider on c1's seam; lab failure-injection shapes)
- [x] Pre-approach solo consult (verdict + actual model in record.md)

### Step 2: provider implementation
- [x] LoopbackOllamaProvider.cs + types
- [x] Unit tests (round-trips, timeout classification, bounded retry, refusal, malformed, remote pre-socket)

### Step 3: LAB matrix + live panic + WSL gate
- [x] LAB both platforms (full failure matrix per SP-019 shapes; stale discard) — WINDOWS-ONLY: WSL gate is the named limit (zero distros); Linux not faked
- [x] Panic re-verified live against real in-flight operation
- [x] WSL2 gate (NAMED LIMIT: `wsl -l -q` empty exit 0 — zero distros; owner decision to provision; recorded verbatim in record.md §3.2)
- [x] Sensitive-logging audit zero hits

### Step 4: evidence consolidation + pre-completion consult
- [ ] record.md complete
- [ ] Pre-completion solo consult
- [ ] STATUS.md accurate

### Step 5: verification
- [ ] testCommand green (verify.mjs exit 0; 0W/0E; ≥466/29 floor)
- [ ] git diff --check clean
- [ ] git status shows File Scope only
