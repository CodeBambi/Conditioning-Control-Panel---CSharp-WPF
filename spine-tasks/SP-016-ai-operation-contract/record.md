# SP-016 record — provider-neutral AI operation contract

## Archaeology (READ-ONLY evidence)

### WPF provider inventory + switching
- Cloud companion chat: `ConditioningControlPanel/Services/AiService.cs:27` hardcoded `ProxyBaseUrl = https://codebambi-proxy.vercel.app`; `MaxTokensHardCap = 100`, `Temperature = 0.7` (`AiService.cs:34,341-342`); client-side daily rate limit 100 free / 1000 Patreon (`AiService.cs:31-33`); server-authoritative `RequestsRemaining` (`AiService.cs:312-319`); single-turn `{system, user}` only, no client history (`AiService.cs:246-250`, AI_AUDIT §2 "Memoria").
- Local Ollama: `LocalAiService.cs` — default host `http://localhost:11434/`, user-configurable (`LocalAiService.cs:516`); payload `stream:false, think:false`, NO token cap (AI_AUDIT §1); `EnsureHost` validates URI and builds new client BEFORE disposing old (`LocalAiService.cs:401-415`); 5-minute HttpClient timeout (`LocalAiService.cs:412`); catch-and-return-null on timeout/HttpRequestException (`LocalAiService.cs:392-395`) — string/null inferred failure, rejected by first-attempt lesson.
- Switching: WPF `Services/AIService/AiServiceStrategy.cs:11-12` — live switch via `CompanionPrompt.UseLocalAi`, lazy per-call selection; first-attempt `CCP.Core/Services/AIService/AiServiceStrategy.cs:43-50` — DI singletons, live per-call `Active` getter. No generation/cancellation either side: a response started under provider A can complete and be displayed after switch to B (late result).
- OpenAI-compatible provider exists only in first attempt (`OpenAiService.cs`), with `TestEndpointAsync` diagnostic returning typed `ConnectionDiagnosticResult`/`DiagnosticCategory` (`OpenAiService.cs:419,459`).

### Interactive vs awareness operation shapes
- Interactive: chat textbox → `GetBambiReplyAsync`/`GetBambiReplyExAsync` returning `AiReplyResult(Text, IsAiGenerated, Refusal)` (`ModerationRefusal.cs:36`); quiz = stateless multi-turn `GetRawChatCompletionAsync` (`IAiService.cs:47-51`).
- Awareness: `GetAwarenessReactionAsync` / `GetStillOnReactionAsync` / `GetKeywordCommentAsync` / `GetLockScreenReaction` / `GetVideoDoneReaction` (`AiService.cs:150-236`) — all nullable-string returns, sentinel-string moderation (`ModerationRefusal.cs:44-61`), call sites convert sentinel→null and silently drop.
- Awareness context: window title + URL/domain packaged as `[Category: X | App: Y | Title: Z | Duration: Nm]` into user input (AI_AUDIT §3 "Cosa legge"); consent = master `KeywordTriggerEnabled`; cooldowns: per-trigger `CooldownSeconds`, global `KeywordGlobalCooldownSeconds`, per-keyword hard cooldown `KeywordPerKeywordCooldownSeconds` default 15s, loop-protection temporal mute (`KeywordTriggerService.cs:56-97,584-605,1280-1328`). Cooldown VALUES are owner-questions (pending-owner).

### Moderation surfaces
- `IModerationGuard.CheckInput/CheckOutput` (`IModerationGuard.cs:33-43`), hardcoded wordlist, applied: cloud input+output (`CoreAiService.cs:272,364`), local input+output (`LocalAiService.cs:160,201,285,305`), OpenAI input+output (`OpenAiService.cs:174,200,250,270`), quiz output (`QuizService.cs:592-600`). Verdict taxonomy: `ModerationResult(bool Allow, ProhibitedCategory? Category, string? Note)` — Pass / Block / SoftHit (`IModerationGuard.cs:7-22`); `ProhibitedCategory` 15 values incl. soft `ProfessionalAdvice` and `PromptExtraction` (`ProhibitedCategories.cs:12-42`). Refusal carrier: `ModerationRefusalInfo(Category?, ModerationSource Input|Output)` (`ModerationRefusal.cs:26`).
- WPF gap (AI_AUDIT §7): no hardcoded refusal beyond prompt-side `ExplicitReaction`; user input never category-filtered pre-send in WPF; command-field moderation (subliminal text, mantra, bounce words) NOT evident in `AiCommandService.cs` dispatch — gates are master/per-effect toggles only. First-attempt lesson REJECTs awareness context transmission without explicit consent.

### Command envelope + parsing
- Envelope: `{response, effects[]}`, each effect `{command, data}` with 11 command types (`AiCommandData.cs`, `AiCommandService.cs` dispatch); per-batch cap `MaxCommandsPerResponse = 3` counted only for survivors (`AiCommandService.cs:47,103`); clamps at dispatch (flash amount 0-8/duration 0-10s/size 0-150; bubbles 0-10/min; subliminal text ≤80 chars, opacity 0-60; spiral/pink 0-30; mantra ≤200 chars amount 0-5; haptic 0..MaxAiHapticIntensity default 0.6, duration 0-10s; getbacktome delay 1-600s depth ≤2); media paths root-contained under assets, no `..`, extension allow-list (`AiCommandService.cs:290-309`).
- Parsing failures observed: `AiResponseParser.cs` does markdown-fence extraction, outer-JSON extraction, brace-balancing REPAIR (`BalanceBraces`/`RepairJson`, `AiResponseParser.cs:159-232`), mixed-format salvage (`ParseMixedFormat`, `AiResponseParser.cs:106-130`), and `AiCommandData.ParseCommand` retries with brace-append recovery (`AiCommandData.cs:36-47`) — lenient repair = commands execute from malformed output. First-attempt lesson REJECTs "lenient command repair, partial/no-op effects reported as success".
- Commands dispatch via `async void ExecuteCommand` (`AiCommandService.cs:87`) with per-command try/catch — no per-command typed result; failures are log-only.

### Endpoint inventory (AI_AUDIT §10)
- AI-carrying: cloud `/v2/ai/chat` (companion + awareness, same endpoint), `/v2/quiz/...`, loopback `localhost:11434/api/chat` + `/api/generate` warmup + `/api/tags`. Legacy fallback `/ai/chat` with Patreon bearer (`AiService.cs:395-399`).
- Non-AI user-content: `app.cclabs.app/api/enhancements`, `/v2/bug-report`, `/prompts/manifest`.
- Remote-host policy: `AiOllamaHost` is free-text configurable; first-attempt lesson REJECTs "remote host called 'local'" — `REJECT: Selecting local AI guarantees local-only data. A configurable non-loopback Ollama host is remote.` No loopback classification exists in either codebase (gap).

### Secret storage
- Cloud auth: `X-Auth-Token` (V2) or Patreon Bearer via V2AuthService/PatreonService; OpenRouter key server-side only (AI_AUDIT §9). OpenAI provider key stored via platform secret seam in first attempt. SP-005 declared `ISecretStore` (persistence-migration-contract §9) — declared-only precedent this contract reuses.

### Offline behavior
- Cloud: `IsAvailable => HasCloudIdentity || Patreon.HasAiAccess` (`AiService.cs:52`) — identity check, not connectivity; failures → canned fallback strings (`IsAiGenerated=false`). Local: connection failure → null → fallback phrase (AI_AUDIT §2). No typed offline state in either codebase; fallback strings are indistinguishable from refusal in the string API (fixed by `AiReplyResult` in first attempt).

## First-attempt dispositions (explicit)

| Item | Disposition | Basis |
|---|---|---|
| Strategy seam (provider-neutral `IAiService`, live switch) | **ADAPT** — keep provider-neutral seam vocabulary; add generation invalidation on switch (first attempt had none) | `AiServiceStrategy.cs:43-50`; lesson "Provider switching could leave late responses" |
| `AiReplyResult` typed reply (Text/IsAiGenerated/Refusal) | **ACCEPT** the idea as contract vocabulary (typed outcomes) — not the class | `ModerationRefusal.cs:36` |
| Response parser (`AiResponseParser` + brace repair + mixed salvage) | **REJECT** — strict schema, reject-by-default, zero repair | `AiResponseParser.cs:159-232`; lesson REJECT "lenient command repair" |
| Moderation sandwich (input+output guard, hardcoded wordlist) | **ACCEPT** the every-surface rule; extend to every command field | `CoreAiService.cs:272,364`; AI_AUDIT §7 |
| Sentinel-string refusal (`##CCP_MODERATION_REFUSAL_*##`) | **REJECT** — typed refusal channel only | `ModerationRefusal.cs:44-61` |
| Catch-and-return-null error taxonomy | **REJECT** — typed `OperationOutcome` per SP-004 | `LocalAiService.cs:392-395`; lesson REJECT "string-inferred failures" |
| `ConnectionDiagnosticResult` typed endpoint diagnostic | **ADAPT** — content-free diagnostic record (idea only) | `OpenAiService.cs:419,459` |
| Per-batch cap + clamps + path containment | **ACCEPT** as envelope contract facts (values pending-owner where noted) | `AiCommandService.cs:47,146-260` |
| Local chat history persistence (50-pair cap, `ChatMemoryEnabled`, `ClearHistory`) | **ADAPT** — declared-only memory seam, explicit-clear operation, consent gating as contract; scope/retention values pending-owner | AI_AUDIT §2 "Memoria"; `LocalAiService.cs:36-153` |

## Owner questions (recorded pending-owner — UNANSWERED)
1. Local memory: consent scope (per-feature? global?) and retention (cap values, disable = retain dormant vs delete) — task-board Decisions-needed line 87.
2. Moderation policy specifics: category list values, wordlist governance, soft-hit handling policy.
3. Endpoint allow-list governance: which remote hosts (if any) are admissible; remote-Ollama support question (task-board line 87).
4. Awareness consent granularity + cooldown values (per-trigger/global/per-keyword defaults).
5. Which AI effect commands remain admissible (task-board line 87).

## Consult verdicts

### Pre-approach (Step 1) — solo consult

Requested: `consult` mode=solo (owner direction 2026-07-19: all gates solo Fable; council unavailable T-7, never used). Actual answering model: not exposed in tool output; route per pinned roster is the Fable family (main-session Fable solo route proven ×5 per task-board row 42). Verdict text (abridged):

(a) Provider switch mid-flight: BOTH layers in SP-004's shape — switch = generation invalidation → token cancellation → stale-application discard. Cancel is the mechanism, stale-discard the backstop. Seed test must demonstrate both halves against the real `OperationRegistry`.
(b) Strict envelope: WHOLE-ENVELOPE ATOMIC REJECTION. Spike row wording ("zero command execution on mixed/invalid payloads") is decisive — one bad command poisons the batch. Per-command results remain compatible: validate all before executing any; every entry gets a typed verdict (`Valid`, `UnknownCommand`, `OutOfRange(field)`, `ModerationBlocked(category)`, `MalformedData`); valid siblings in a rejected envelope get `NotExecuted(envelope-rejected)`, never silently dropped. Mark envelope section greenfield-decision (WPF per-command drop + repair is exactly what's superseded).
(c) Gap: "per-command results" is a NAMED acceptance item — give it its own named section (13 sections total). Do NOT add sections for rate limits/quotas — archaeology facts inside relevant sections only.
Caution: diagnostic content-freedom proof must be STRUCTURAL — diagnostic record exposes only enums, stable codes, counts, durations, generation/endpoint-class identifiers; no free-text field (exception `reason` = exception class names or stable codes, never messages — messages can embed user input). Test asserts the serialized property set is exactly the closed allow-list.

## Engine-review presence (T-2)

- Step 1 plan review: `spine_review_step(step=1, type=plan)` → **SKIPPED by engine** (nested reviewer spawn blocked inside worker session; SP-195 — batch engine runs reviews after worker success). Artifact: `.reviews/1-20260721T082721.md`. Not a spawn failure (fail-closed rule not triggered).

### Pre-completion (Step 4) — pending
