## STATUS: SP-046 — AI companion slice c7: companion UI surface
**Current Step:** COMPLETE — all 5 steps done; .DONE
**Last Updated:** 2026-08-05 (worker, Step 5 complete — contract green 601/33)
**Blockers:** none
**Discoveries (scope notes):** the composition wiring forced mechanical expectation updates in 3 pre-existing test files NOT named in File Scope — `CapabilityTests.cs` (capability name list + 2 AI state arms), `CompositionRootValidationTests.cs` + `IntegrationProofTests.cs` (participant count 6→7 + companion arm). No behavior assertion weakened; all three changes are additive enumerations of the new composition. Documented per the file-scope amendment rule; justification also in record.md §5.
**Environment change (2026-08-05):** a REAL Ollama (0.32.5) now listens on 127.0.0.1:11434 on this box (SP-019 limit 1 said absent at spike time). Assertions that assumed absence were written environment-honest (typed state either way); the headed lab stays the deterministic instrument via `--ai-ollama-host` (never the real model).

### Step 1 (COMPLETE): archaeology + design + avalonia-research + pre-approach consult
- [x] Update STATUS.md before starting work
- [x] WPF archaeology (badge, chat input, clear flow, consent/cooldown surfaces, refusal presentation)
- [x] Design (evolved grammar; placement decision recorded; composition wiring for the full AI chain)
- [x] avalonia-research pass (current v12 facts cited for every API used)
- [x] Pre-approach solo consult (verdict + actual model in record.md)

### Step 2 (COMPLETE): companion surface + composition wiring
- [x] Features/Companion/ surface (chat, badge, status, refusal bubbles, clear control, consent/cooldown surfaces)
- [x] Composition wiring (Program/App/CompositionRoot per the wiring-file norm)
- [x] Bool-door retirement (6 files migrated + overload deleted, or hard-stop recorded)
- [x] Unit tests + headless draw-level tests where honest

### Step 3 (COMPLETE): headed evidence via avalonia-live + A-013 advisory + panic-quiet
- [x] Launch with CCP_MCP=1; drive the surface (badge pixels, status, refusal bubble, clear control, consent surfaces)
- [x] Panic-quiet against a real in-flight lab operation
- [x] Captures = screenshot + semantic tree in evidence/; zero binding errors
- [x] A-013 advisory ValidateXaml dispositions recorded

### Step 4 (COMPLETE): evidence consolidation + pre-completion consult
- [x] record.md (archaeology, design, research citations, consults, review presence, evidence index)
- [x] Pre-completion solo consult (verdict + actual model in record.md)
- [x] STATUS.md accurate before .DONE

### Step 5 (COMPLETE): Testing & Verification
- [x] Contract testCommand passes (verify.mjs exit 0 + build 0W/0E on Rebuild + 601/33 vs the 581/29 floor)
- [x] git diff --check clean
- [x] git status --short shows only File Scope paths (3 documented enumeration exceptions)
