## STATUS: SP-035 — AI companion slice c2: loopback Ollama provider
**Current Step:** Step 1 — provider archaeology + lab design + pre-approach consult
**Last Updated:** 2026-08-04 (fresh execution; SP-037 floor repair landed — 466/466 + 29/29)
**Blockers:** none

### Step 1: archaeology + lab design + pre-approach consult
- [x] Update STATUS.md before starting work
- [x] WPF archaeology (Ollama client shape, retry placeholder, timeout/cancel, refusal)
- [x] Design (LoopbackOllamaProvider on c1's seam; lab failure-injection shapes)
- [x] Pre-approach solo consult (verdict + actual model in record.md)

### Step 2: provider implementation
- [ ] LoopbackOllamaProvider.cs + types
- [ ] Unit tests (round-trips, timeout classification, bounded retry, refusal, malformed, remote pre-socket)

### Step 3: LAB matrix + live panic + WSL gate
- [ ] LAB both platforms (full failure matrix per SP-019 shapes; stale discard)
- [ ] Panic re-verified live against real in-flight operation
- [ ] WSL2 gate (lab + contract green on Linux; zero external traffic proven)
- [ ] Sensitive-logging audit zero hits

### Step 4: evidence consolidation + pre-completion consult
- [ ] record.md complete
- [ ] Pre-completion solo consult
- [ ] STATUS.md accurate

### Step 5: verification
- [ ] testCommand green (verify.mjs exit 0; 0W/0E; ≥466/29 floor)
- [ ] git diff --check clean
- [ ] git status shows File Scope only
