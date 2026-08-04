# SP-042 record — AI companion slice c5: awareness (consent, cooldowns, context packaging, keyword routing)

**Task:** SP-042 · **Board row:** "Implement AI companion and awareness integration" (P0), slice c5 of `client/docs/ai-companion-admission.md` §8 · **Lane:** lane-1 · **Date:** 2026-08-04

Evidence class: **U** (Windows) + Windows title-observation session facts + named Linux limit. **WSL2 named limit (honesty framing (g), probed on this laptop by SP-035/038 precedent):** `wsl -l -q` → empty, exit 0 — WSL installed with ZERO distros; the admission's "WX session facts for window-title observation (X11 only)" is UNDISCHARGEABLE on this machine; recorded as owner-gated (provision a distro), NEVER faked. Windows title-observation facts are the session evidence. No Wayland claim anywhere.

---

## 1. WPF archaeology (READ-ONLY, `File.cs:line`; all lines re-verified 2026-08-04)

### 1.1 Consent pair (both default false)

- `Services/UI/WindowAwarenessService.cs:341-347`: `Start()` gates on `App.Settings?.Current?.AwarenessModeEnabled != true || AwarenessConsentGiven != true` → returns without starting ("feature disabled or no consent").
- `CCP.Core/Models/AppSettings.cs:2982,2993` (verified): `_awarenessModeEnabled = false`, `_awarenessConsentGiven = false`. XML docs: "Requires explicit consent. Privacy-focused: only categorizes, never logs titles" / "Must be true for awareness mode to function."

### 1.2 Reaction cooldown mechanism + the `?? 90` dead-code discrepancy

- `WindowAwarenessService.cs:375-388` (`CanReact()`/`CanStillOnReact()`): `var cooldownSeconds = _nextReactionCooldownSeconds > 0 ? _nextReactionCooldownSeconds : (App.Settings?.Current?.AwarenessReactionCooldownSeconds ?? 90);` — **the `?? 90` is DEAD CODE**: `AwarenessReactionCooldownSeconds` is a non-nullable `int` default **10**, setter-clamped 10–600 (`AppSettings.cs:3000-3008`, verified). **Owner question carried verbatim: 10 or 90?** (§9.2 #4.)
- `RollCooldownSeconds()` (`:397-412`): random in [base, max] when `AwarenessCooldownMaxSeconds > base`, else fixed base. `MarkReaction()`/`MarkStillOnReaction()` (`:421-433`) stamp the time and roll the next cooldown.
- Live-check boundary: `(DateTime.Now - _lastReactionTime).TotalSeconds >= cooldownSeconds` — **admitted at exact equality**.
- Still-on milestones: `StillOnMilestonesMinutes = { 1, 5, 10 }` (`:436-438`).

### 1.3 Keyword machinery (trigger/global/per-keyword/loop-protection + gates)

- **Global gate** (`KeywordTriggerService.cs:923,1108,1274`): `(now - _lastGlobalTriggerTime).TotalSeconds < settings.KeywordGlobalCooldownSeconds` → suppressed. Default 10s, clamp 1–300 (`AppSettings.cs:4294` region, verified `_keywordGlobalCooldownSeconds = 10`). XML doc: "hard ceiling on trigger frequency regardless of how many matches are on screen. Primarily prevents the OCR feedback loop."
- **Per-keyword hard cooldown + loop protection = ONE merged mute dict** (`RecordFire`, `:158-187`): `finalMs = Math.Max(loopMs if AwarenessLoopProtectionEnabled else 0, KeywordPerKeywordCooldownSeconds * 1000)`; write is **extend-not-shrink**: `if (!_mutedKeywords.TryGetValue(keyword, out var existing) || existing < expiresAt) _mutedKeywords[keyword] = expiresAt;` (`:178-181`). Defaults: per-keyword 15s clamp 1–600 (`:4314` region, verified `_keywordPerKeywordCooldownSeconds = 15`); loop protection enabled default true, 5000ms (`:4438,4450` region, verified).
- **Live check** (`IsKeywordMuted`, `:82-99`): `DateTime.UtcNow < expiresAt` → muted; expired entries pruned on access. Exact equality = expired = admitted.
- **Per-trigger cooldown** (`CCP.Core/Models/KeywordTrigger.cs:226,230`): `LastTriggeredAt` + `IsOnCooldown => (DateTime.Now - LastTriggeredAt).TotalSeconds < CooldownSeconds`; gate at scan (`KeywordTriggerService.cs:939,1116,1287` `if (trigger.IsOnCooldown) continue;`).

### 1.4 AvatarComment routing + the two admission corrections

- `DispatchAvatarComment` (`KeywordTriggerService.cs:1622-1673`): `RequireAiAvailable && !aiAvailable` → `PickCannedPhrase` → `ShowAvatarLine(canned, aiGenerated: false)` (`:1626-1632`); else **fire-and-forget `_ = Task.Run(...)` (`:1650`)** → `GetKeywordCommentAsync` → null/empty line → canned phrase → `ShowAvatarLine(line, aiGenerated: fromAi)` — **the badge reflects the actual source** (`:1652-1667`).
- Prompt shape (`Services/AiService.cs:196-206`): `IsAvailable` gate → `promptTemplate.Replace("{keyword}", keyword)` or default `"You just caught the user on the word '{keyword}'. React in character, one short line."`
- **Correction 1 (admission §5 rule 4):** the `Task.Run` becomes an SP-004 **owned awareness operation** (panic-cancellable) — c5 routes through `AiOperationPipeline.RunAwarenessAsync`, whose operations are owned on the pipeline's `AsyncOperationOwner`.
- **Correction 2:** the "AI unavailable → canned phrase unbadged" string channel becomes typed `AiReply.Fallback`/`Unavailable` visibility — the reply VARIANT is the badge authority (contract §1: badge derives from `Generated` and nothing else).
- **Removed keyword pre-check (H7)** (`KeywordTriggerService.cs:1638-1646`, comment verified): the upstream `CheckInput` was REMOVED because the service boundary already moderates at the HTTP edge; double-checking produced two log entries + two counter hits per keyword event. Greenfield: one uniform boundary at the operation edge (c3); c5 adds no second check.

### 1.5 Context packaging shape

- `Services/AiService.cs:160-163`: `[Category: {category} | App: {website} | Title: {tabName} | Duration: 0m]`; still-on variant `:182-188` formats duration `Xs`/`Xm`/`Xh`. Local-service packagers (`LocalAiService.cs:405-419`, per SP-038 record §1.1 #3) flow into the same input-moderated response path.

## 2. Design (pre-approach; consult verdict + dispositions in §4.1)

All product code in ONE new file `client/src/CcpClient.Desktop/Ai/AiAwarenessService.cs` (contract-named, fileScopeMustChange). New tests: `client/tests/CcpClient.Tests/AiAwarenessTests.cs` + `AiAwarenessCooldownTests.cs`.

1. **Typed consent (`AiAwarenessConsent`)** — `NotGiven` placeholder default (conservative: no awareness work until an owner-decided consent exists); record shaped so per-field/per-source granularity lands additively (§9.2 #4 owner-pending). **Additive pipeline overload** `RunAwarenessAsync(AiRequest, AiAwarenessConsent)` (consult §4.1 (a)): the typed state reaches the pipeline's admission point; the existing `bool` overload stays (4 out-of-scope test files call it; c4 additive/lane-disjointness precedent) and delegates. Residual bool door recorded honestly.
2. **`AiCooldownRegistry`** — 4 typed classes (`PerTrigger` keyed by triggerId, `Global`, `PerKeyword` keyed by keyword, `LoopProtection` keyed by keyword); injectable `Func<DateTimeOffset>` clock (c3 `AiModerationEscalation` precedent); extend-not-shrink per (kind, key): new expiry = `max(existing live expiry, now + duration)`; live iff `now < expiry` (admitted at exact equality, WPF boundary); expired pruned on access; one lock (escalation shape). **Equivalence note (consult §4.1 (c)):** WPF merges loop-protection and per-keyword into one mute entry with `Math.Max` of the two durations; the greenfield union-of-classes (suppressed if EITHER class is live) is behaviorally identical — recorded, never claimed as a new mechanism. Rolled randomization (`RollCooldownSeconds`, §1.2) deliberately NOT ported: nondeterminism in a test-critical mechanism; `AwarenessCooldownMaxSeconds` recorded as owner-pending value.
3. **`Suppressed(cooldown)` observable outcomes** — service-admission suppression → `AiOperationResult(Completed, null, AiAdmission.Suppressed(AiSuppressionKind.Cooldown))` + content-free diagnostic `suppressed:cooldown` (the kind rides the typed result; keyword NEVER enters diagnostics; `Generation = -1` = no operation began; `DurationMilliseconds = 0`). `SendAttempts` untouched (offline zero-network preserved).
4. **Context packaging** — `AiAwarenessContext(Category, App, Title, DurationText)`; `TryPackage` runs EVERY field through `boundary.EvaluateInput(field, AiModerationSurfaces.AwarenessContextFields)` BEFORE assembly; any `Block` → typed `AiReply.Refused` result, ZERO transmission (no pipeline call); assembled shape is the WPF `[Category: X | App: Y | Title: Z | Duration: N]` verbatim. Fields never into diagnostics/memory (sentinel-string proof test; pipeline memory-append is Interactive-only from c4).
5. **Keyword routing** — `RunKeywordCommentAsync(triggerId, keyword, promptTemplate?, fallbackText?)`: consent → cooldown gates (global, per-keyword, loop-protection, per-trigger — suppress if ANY live) → RecordFire (extend-not-shrink, stamped BEFORE dispatch per WPF `LastTriggeredAt = now` discipline) → prompt assembly (WPF `{keyword}` substitution / default line, §1.4) → pipeline owned awareness operation (panic-cancellable) → typed routing result `AiAwarenessRoutingResult`: `Visible(AiReply.Generated)` | `Visible(AiReply.Fallback(canned, code))` — **keyword path ONLY, and only for provider-Unavailable** (WPF canned-on-unavailable shape, typed per §2 rule 4; badge reflects true source because Fallback is never badged) | `Dropped(AiAwarenessDropKind)` for Refused (BY TYPE — the routing-layer drop c3 deferred; no refusal bubble), provider-Unavailable without canned, Cancelled, and suppressed admissions. **Consult corrections adopted (§4.1 (d)):** NO canned substitution on the window-reaction path (WPF has none there); refusal NEVER falls back to canned — recorded divergence: WPF showed canned on refusal because its string channel collapses refusal→null; greenfield distinguishes by type deliberately. Caller-supplied `fallbackText` inherits c3's app-authored-canned moderation non-claim ONLY if genuinely app-authored — recorded at the seam (WPF sources phrases from `CompanionPhrases`/mods, `:1676-1685`; a mod-phrase consumer would NOT inherit the non-claim).
6. **Window-reaction operation** — `RunReactionAsync(AiAwarenessContext)`: consent → reaction cooldown (`PerTrigger`-class entry keyed by a fixed reaction slot — the WPF `CanReact`/`MarkReaction` mechanism; still-on shares the cooldown mechanism with milestone scheduling recorded as values, §1.2) → packaging (rule 4) → pipeline owned operation → routing result (Unavailable/Refused → Dropped; Generated → Visible).
7. **Title-observation capability** — `"ai.awareness.window-title"` registered via SP-006 probe discipline. Windows probe: `GetForegroundWindow` + `GetWindowTextW` P/Invoke → `Available` with CONTENT-FREE detail (title LENGTH only, never the title); `hwnd == 0` → honest `Unavailable` transient ("no foreground window" — WPF `:596-604` discipline: lock screen/secure desktop). Linux probe → typed `Unavailable` (WSL zero-distro named limit; X11 session facts owner-gated; NO Wayland claim). Capture consumed only under consent + capability Available; titles never logged, never diagnostics, never memory.

## 3. File-scope constraint resolutions

- **No pipeline signature change:** 4 out-of-scope test files call `RunAwarenessAsync(request, awarenessConsent: bool)`. Resolution: additive overload (design rule 1); bool overload preserved.
- **Inventory row stays `Reserved` (consult §4.1 (b)):** `AiModerationCoverageTests.cs` hardcodes 5-Wired/6-Reserved counts + one switch arm per Wired row and is OUT of file scope; flipping `awareness-context-fields` to Wired would red the suite on immutable tests. The row stays Reserved; its `ReservedFor` text is updated IN-SCOPE (string only — no disposition/count change) to state that c5 landed the packaging wiring and the flip is deferred by file scope. Packaging moderation is proven DIRECTLY by new `AiAwareness*` tests against the `awareness-context-fields` surface ID. **Discovery for orchestrator follow-up (mechanical):** flip disposition → Wired; update counts 6/5; add a switch arm asserting blocking-policy refusal on a context field. (SP-038's own recorded limitation: nothing executable flips Reserved→Wired.)

## 4. Consults

### 4.1 Pre-approach (Step 1)

**Mode:** solo (council forbidden — T-7). Route per the 2026-08-04 rewire: Opus 5 main / Fable 5 fallback. **Actual answering model:** the consult tool output carried NO model identifier — recorded honestly per T-2 (same as SP-033/035/038). The received points were complete and actionable.

**Verdict (substantive points) + dispositions:**
1. **(a) Consent seam — leaning adopted:** callers can bypass the service and call the pipeline with a fabricated bool; make the typed consent the admission vocabulary by adding an ADDITIVE typed pipeline overload `RunAwarenessAsync(AiRequest, AiAwarenessConsent)`; keep the bool overload for the out-of-scope tests and RECORD the residual bool door. ADOPTED (design rule 1).
2. **(b) Inventory row — adopted:** a wired surface labeled Reserved is a live false statement in the honesty inventory, but flipping breaks immutable out-of-scope tests. Keep Reserved; update the `ReservedFor` TEXT in-scope to say c5 landed the wiring and the flip is deferred; record the exact mechanical follow-up (counts 6/5, new assertion arm) as a discovery. ADOPTED (§3).
3. **(c) Cooldown classes — equivalence verified:** WPF collapses loop-protection and per-keyword into ONE mute entry with `Math.Max(durations)`; the four-class union (suppressed if either is live) is behaviorally identical. Extend-not-shrink must hold per (kind, key) — `expiry = max(existing live expiry, now + duration)`; live iff `now < expiry` (admitted at equality). Do NOT port the rolled randomization into a test-critical mechanism; record `AwarenessCooldownMaxSeconds` as an owner-pending value. ADOPTED (design rule 2).
4. **(d) Routing/drop semantics — two corrections, both adopted:** (i) canned-fallback substitution exists in WPF ONLY on the keyword AvatarComment path — window reactions return null with no canned; scope `Visible(Fallback)` to the keyword path only. (ii) WPF shows canned on REFUSAL too (its string channel collapses refusal→null); greenfield deliberately distinguishes by type and DROPS refusals (contract §4 rule 3) — record the divergence explicitly. (iii) caller-supplied fallback text inherits c3's app-authored-canned moderation non-claim ONLY when genuinely app-authored; WPF sources phrases from `CompanionPhrases`/mods — a mod-phrase consumer would not inherit the non-claim; record at the seam. ADOPTED (design rule 5).

## 5. Engine review presence (T-2)

| Call | Result |
|------|--------|
| Step 1 plan review | (recorded at step boundary) |

## 6. Evidence / budgets / surprises / durable-lesson candidates

(filled in Step 4)
