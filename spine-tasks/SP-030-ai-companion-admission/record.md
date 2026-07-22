# SP-030 record — AI companion admission (design-record)

**Task:** SP-030 · **Row:** "Implement AI companion and awareness integration" (P0) · **Deliverable:** `client/docs/ai-companion-admission.md` (design-only, zero product code)

## 1. WPF AI archaeology (READ-ONLY; File.cs:line verified in this worktree)

Performed via the wpf-archaeologist agent plus direct gap-closing greps. Key facts (full set folded into admission §1):

- **Cloud:** `ProxyBaseUrl` hardcoded `Services/AiService.cs:27` (AI_AUDIT's `:26` is stale — corrected). V2 auth `X-Auth-Token` + unified ID body (`:332-352`); V1 legacy `Bearer` + 404 fallback (`:355-361,479-499`); 30s timeout (`:64`); client-side daily circuit 100/1000 (`:32-39`).
- **Provider switching:** `Services/AIService/AiServiceStrategy.cs:22-58` — live per-call selection, lazy construction under lock, **no generation handling** (the rejected shape). Setting: `CompanionPrompt.AiProvider` default Cloud (`CCP.Core/Models/CompanionPromptSettings.cs:27`).
- **Badges/status:** `IsAiGenerated` bool → pink AI badge only when true (`AvatarTube/AvatarTubeWindow.Speech.cs:319-353`, passed at `ChatInput.cs:727`). Availability = identity check (`AiService.cs:52`; `LocalAiService.cs:46` always-true) — rejected shape.
- **Memory (local provider only):** 50-pair cap, `local_chat_history.json` under UserDataPath, `ChatMemoryEnabled` default true (`CompanionPromptSettings.cs:120` — AI_AUDIT's `:54` stale), `ClearHistory()` clears + deletes file (`LocalAiService.cs:92-178`). **AI_AUDIT "Reset Memory" UI claim CONFIRMED** despite a stale in-code comment (`:170-172`): UI callers exist at `MainWindow/MainWindow.Patreon.cs:962,1539,1570`. Persist deferred until output moderation passes; blocked turns rolled back (`:546-586`). Ambient/awareness reactions stateless, never persisted (`:476-502`).
- **Awareness:** consent = `AwarenessModeEnabled` AND `AwarenessConsentGiven`, both default false (`WindowAwarenessService.cs:337-338`; `AppSettings.cs:2982,2993`). Reaction cooldown: effective default **10s** (`AppSettings.cs:3000-3008`, clamp 10-600); the `?? 90` fallback in `WindowAwarenessService.cs:374-388` is **dead code** — discrepancy recorded as an owner-question footnote. Still-on milestones 1/5/10 min (`:405-407`). Context packaging `[Category|App|Title|Duration]` (`AiService.cs:160-163,182-188`).
- **Keyword triggers:** global cooldown 10s (`AppSettings.cs:4294`), per-keyword 15s (`:4314`), loop protection 5s extend-not-shrink (`KeywordTriggerService.cs:158-187`; gates at `:590-591,604-605`). Upstream moderation pre-check deliberately removed H7 (`:1294-1301`).
- **Moderation call sites (repo-wide grep, complete):** exactly 4 — cloud in/out (`AiService.cs:274,409`), local in/out (`LocalAiService.cs:415,555`), quiz output (`QuizService.cs:561`). **Command fields / subliminal text are NEVER moderated in WPF** — the gap the greenfield closes (contract §7 rule 2). Sentinel strings `##CCP_MODERATION_REFUSAL_*` (`ModerationRefusal.cs:53-54`) — rejected. Counter: 3-hit warning / 5-hit cooldown (`ModerationCounter.cs:84-85,108-125`), user-chat-input only.
- **Secrets:** DPAPI `ProtectedData.Protect/Unprotect(..., CurrentUser)` confirmed by direct read (`Services/Auth/SecureAuthTokenStore.cs:40,66`; sibling `DiscordTokenStorage.cs:52-55,87-90`). Settings store opaque routing via Core seam (`CCP.Core/Models/AppSettings.cs:4005-4008`). OpenAI-compatible key in settings (`OpenAiCompatibleService.cs:72-73`) with parallel `SecureApiKeyStore` seam; caches wiped on exit (`App.xaml.cs:3299-3300`).
- **Cancellation/panic:** NO CancellationToken anywhere in the WPF AI path; fire-and-forget at `ChatInput.cs:652,663`, `KeywordTriggerService.cs:1304`, `LocalAiService.cs:586`, `App.xaml.cs:1475`; shutdown = `Ai?.Dispose()` (`App.xaml.cs:3253`) + `ObjectDisposedException` tolerance (`LocalAiService.cs:613-615`). All rejected shapes per SP-004.
- **Local AI (WPF copy):** Ollama default `http://localhost:11434/` (`LocalAiService.cs:24`), model `qwen3.5:latest` (`:25`), `POST api/chat` stream:false think:false (`:619-641`), warm-up `api/generate` keep_alive 30m (`:189-220`), `api/tags` listing; host configurable (`:244-269`); `SemaphoreSlim(1,1)` busy behavior (`:32,330-345,436-441`); retry shape one bounded retry (`OpenAiCompatibleService.cs:425-427` — WPF-observed, not owner-approved).

## 2. Evidence consolidation

- SP-016 contract §§1-13 → design obligations (each admission section names its contract sections).
- SP-019 spike → proven mechanics reused as design facts: mid-stream cancel 0ms typed Cancelled; timeout = failure classifier; bounded retry (exactly 2 lab hits, Retry-After honored); refusal no-retry; malformed/truncated → Unavailable never partial; remote-host pre-socket rejection (send-attempt counter 0); 62/62 fuzz vs real validator with canary; redaction registry audit GREEN both platforms.
- SP-019 named limits → honest placeholders/ledger entries: Ollama absent; no cloud credentials; moderation policy values owner-pending; endpoint allow-list owner-pending; admissible command set owner-pending; F1 duplicate-key gap (implementation-row fix); `NotExecuted(SupersededGeneration)` lands with execution slice; retry values not owner-approved.

## 3. Consults (solo route; per project routing solo = Fable 5; the tool does not echo the answering model id — recorded honestly)

### 3.1 Pre-approach (Step 1)

- **Call 1:** truncated verdict ("The" — unusable; T-7-class output failure on the solo route). Retried once with a compacted brief.
- **Call 2 (used):** mode=solo, answering model = solo-route advisor (Fable 5 per project configuration; model id not echoed by the tool). Verdict (abridged): (1) partition sound except two placement errors — **panic cancellation belongs in c1** (same mechanism: generation invalidation + owned cancellation + bounded drain), re-verified live in c2 against a real in-flight network op, c7 keeps only the headed UI-quiet proof; **F1 duplicate-key fix moves to c1** (first slice touching AI code; SP-019 fuzz cases as regression proof); badge/status *plumbing* in c1 does not discharge "accurate badges/status" — that maps to c7 headed evidence, say so in §8. (2) **SP-005 reuse confirmed** for memory (b2 slot precedent: one `PersistenceStore<T>` with its OWN AsyncOperationOwner per SP-024 lesson); shape so both retention answers (delete vs dormant) are additively implementable. (3) **Cloud named-limit posture correct**; addition: c1 must prove the typed consequence of cloud absence (selected-but-unproven → `AiReply.Unavailable` + SP-006 capability state); record proxy endpoint as inventory-not-admission; name the future owning slice.
- **Applied:** slice cut revised per (1) — c1 now carries F1 + panic + cloud-absence proof; §8 mapping states badges→c7 explicitly.

### 3.2 Pre-completion (Step 3)

- (pending)

## 4. Engine-review presence (T-2)

- Step 1 `spine_review_step type=plan`: **SKIPPED in-worker by design (SP-195)** — tool returned `skipped: true`, `spawnFailed: false`, `reviewLevel: 2` echoed; artifact `.reviews/1-20260722T103145.md`. Engine runs reviews after `.DONE`.

## 5. Decisions + rejected alternatives

- **Memory on SP-005 machinery** (PersistenceStore + own AsyncOperationOwner + schemaVersion/migration journal + quarantine→Degraded) — REJECTED alternative: new dedicated seam (duplicates migration/quarantine machinery for zero gain; consult-confirmed).
- **Cloud provider not implemented in this row's slices** — design-admitted from archaeology shapes; named limit (no credentials exist, SP-019 limit 9). REJECTED: cutting a cloud slice with unverifiable network code (honesty framing).
- **Admission policy placeholder = loopback-only** (SP-019 spike shape) pending owner allow-list — REJECTED: admitting any remote host by default.
- **Linux secret backend = freedesktop Secret Service candidate with typed Unavailable fallback** (never silent plaintext) — decided as design direction; the implementing slice verifies with evidence.

## 6. Owner-question ledger

Recorded in admission §9 (verbatim decree citation + the pending-value list). No pending value decided by this task.

## 7. Durable-lesson candidates (for the orchestrator's land reconcile — worker does NOT edit port-lessons.md)

- The `?? 90` dead-code fallback vs 10s settings default in WPF awareness cooldown: conditional/dead expressions must be recorded as governing expressions, never flattened (mirrors SP-012's conditional-value rule) — the admission names the discrepancy as an owner question rather than picking one.
- Solo consult route can return truncated/garbage verdicts ("The"); retry once with a compacted brief before treating it as a hard failure (first call truncated, second usable, same session).

## 8. Verification (Step 4)

- (pending)
