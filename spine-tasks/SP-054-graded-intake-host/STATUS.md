## STATUS: SP-054 — Graded Intake web-core host
**Current Step:** Step 4 — Testing & Verification
**Last Updated:** 2026-08-11 (Step 3 complete — 6 headed runs, consult discharges executed, 795/795 + 33/33 green)
**Blockers:** none

### Step 1: archaeology + design + pre-approach consult
**Status:** complete (plan review SKIPPED — engine-runs-after-.DONE class, recorded below)

> **Engine-review presence (T-2):** Step-1 plan review call → `skipped: true, spawnFailed: false` — "Nested reviewer spawn blocked inside pi worker session... the batch engine runs reviews after worker success (SP-195)" (artifact `.reviews/1-20260811T171844.md`). Engine review ABSENT in-worker by design; code + final reviews run on the engine after .DONE.
- [x] Update STATUS.md before starting work
- [x] WPF archaeology (window class, full bridge vocabulary, pass state machine, punch card, profiler, init provisions, serving origins)
- [x] Design (Intake/ host, protocol, pass service, punch card, profiler, drafting sink, serving, loom-share)
- [x] Pre-approach solo consult (verdict + actual model in record.md)

### Discoveries
- D1: ping/payload-state C# emit sites REFUTED for intake (grep-verified by archaeology: emits exist only in DtrhHostService/DtrhHostOrchestrator). Authoring obligation resolves as TYPED (page-attested, never-emitted vocabulary) — consult item.
- D2: intake tree is NOT copied to output (csproj glob covers web/dtrh only; csproj out of File Scope). Design: serving root probe (output payload/intake → legacy source tree walk-up → typed Missing). Consult item.
- D3: launch/evidence glue (Program.cs/App.axaml.cs --intake-demo demonstrator) is outside the listed File Scope but has the SP-049 --loom-demo precedent (a42ebdf4 landed with those edits). Consult item; flagged for orchestrator.
- D4: settings keys home — DemoSettings declares itself "NOT a feature model" and Persistence/ is out of scope; design uses a dedicated PersistenceStore<IntakeSettingsDocument> (intake_settings.json). Consult item.
- [ ] WPF archaeology (window class, full bridge vocabulary, pass state machine, punch card, profiler, init provisions, serving origins)
- [ ] Design (Intake/ host, protocol, pass service, punch card, profiler, drafting sink, serving, loom-share)
- [ ] Pre-approach solo consult (verdict + actual model in record.md)

### Step 2: host machinery + bridge + stores + profiler
**Status:** complete (plan review SKIPPED — engine class, see Step 1 note)
- [x] Window class (surface, profile, autoplay, fullscreen persist, watchdog + relaunch-once, exit watchdog, ducking)
- [x] IntakeProtocol typed vocabulary (6 out / 12 in; ping + payload-state pinned/typed)
- [x] IntakePassService + IntakePunchCard + IntakeProfiler + drafting sink
- [x] Unit tests (bridge, pass-week, punch card, profiler matrix, stores, degraded typings)

### Step 3: serving + loom-share + headed evidence + pre-completion consult
**Status:** complete (plan review SKIPPED — engine class, see Step 1 note; artifact `.reviews/3-20260811T222515.md`)
- [x] Intake tree serving through the §4 contract class (audio borrow proven)
- [x] loom-save against the shared b4 store (file-content proof)
- [x] Headed evidence (boot, init provisions, quiz-result → drafting + spend + stamp, abort = no spend, fullscreen, watchdog)
- [x] record.md (archaeology, design, consults, review presence, evidence index)
- [x] Pre-completion solo consult (verdict + actual model in record.md)
- [x] STATUS.md accurate before .DONE

### Step 4: Testing & Verification
- [ ] Contract testCommand passes (verify.mjs + build 0W/0E + ≥683/33 floor; TRX logger)
- [ ] git diff --check clean
- [ ] git status --short shows only File Scope paths
