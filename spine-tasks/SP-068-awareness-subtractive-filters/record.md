# SP-068 record — Three subtractive privacy filters (F1 incognito drop, F2 title scrub, F3 unsanctioned-link strip)

Status: in progress. Floor at authoring: 903 unit / 35 headless / 2 skipped on Windows (SP-067, `75a09d61`).

## 1. WPF anchors — found vs given (framing a; every offset re-derived by symbol name)

### F1 — incognito hard-drop (audit row A6, ADOPT)

| Symbol | Packet gave | Found | Drift |
|---|---|---|---|
| `AwarenessPrivacyRules.IncognitoMarkers` | ~:192 | :192 (declared) | none |
| `AwarenessPrivacyRules.LooksIncognito` | ~:308 | :308 | none |
| PrivacyRules application | ~:279-280 | :279-280 inside `Evaluate` (`if (LooksIncognito(request.RawTitle)) return Drop(AwarenessDropReason.Incognito)`) | none |
| `AwarenessObserverPolicy.IncognitoMarkers` (2nd list) | ~:169 | :169 | none |
| `AwarenessObserverPolicy.IsIncognitoTitle` | ~:346 | :346 | none |
| ObserverPolicy application | ~:264-266 | :266 (`if (IsIncognitoTitle(title)) return PrivacyVerdict.Dropped(FrameDrop.Incognito)`), comment "Incognito first…" :264-265 | within one line |
| (packet's own example of audit drift) `AwarenessObserverPolicy.cs:319-327` cited by the audit for the incognito drop | — | that range is `ResolveOwnProcessName()`; `:277-279` is a `catch` returning `PolicyUnavailable` — confirmed stale at authoring, re-confirmed here | audit cites stale, semantics present |

Both matchers lowercase the title and match markers as **case-insensitive substrings anywhere in the title** (`ToLowerInvariant` + `Contains(..., Ordinal)` — PrivacyRules.cs:308-316, ObserverPolicy.cs:346-355).

### F2 — title scrubbing (audit row A10, ADOPT)

| Symbol | Packet gave | Found | Drift |
|---|---|---|---|
| `SanitizeTitleForWire` | ~:346 | :346 (body :347-369) | none |
| `MaxTitleLength = 80` | ~:372 | :372 | none |
| `EmailPattern` = `[\w.+-]+@[\w-]+\.[\w.-]+` | ~:444-445 | :444-445 (Compiled \| CultureInvariant) | none |
| `LongDigitsPattern` = `\d{6,}` | ~:447-448 | :447-448 (Compiled \| CultureInvariant) | none |
| whitespace collapse → `SanitizeDisplayName(..., MaxTitleLength)` | ~:367 | collapse loop :353-365; call :367 | none |
| `SanitizeDisplayName` (control-char drop, trim, cap, role-marker drop) | (implied) | `AwarenessText.cs:99-113`; `LooksLikeInstruction` :230-238; `RoleMarkers` :51-60 | — |
| 120-char projection cap | `AwarenessProjection.cs` | :217 (`w.WriteString("title", AwarenessText.SanitizeDisplayName(media.Title, 120))`) | — |

**120-cap disposition: deliberately NOT ported.** It sits on the cloud wire path (`AwarenessProjection`, media title into the cloud projection). The port has no cloud provider — admission §2 rule 6 admits loopback Ollama only — so the cap has no consumer here. Recorded, not silently dropped.

### F3 — unsanctioned-link strip (audit row C3, MERGE — strip half only)

| Symbol | Packet gave | Found | Drift |
|---|---|---|---|
| `CompanionBrain.cs` "before the text reaches the bubble, history, or disk" comment | :263 | :262-263 | one line |
| chat: `UnwrapSpokenSigil` then `StripUnsanctionedLinks` | :264,:269 | :264,:269 | none |
| chat: title rewrite | (C3 third part, not ported) | :274 `RewriteOffPoolTitles` | — |
| chat: "nothing survived" | :279 | :279-284 — `Session.Remove(userTurn)` + canned fallback, `IsAiGenerated: false` | none |
| reaction: strip | :356-357 | unwrap :355, strip :356, emptied :358-361 (event turn removed; empty non-AI result; `AmbientReply` never persisted) | none |
| `AiTextHygiene.UnwrapSpokenSigil` | :89 | :99 (**out of scope**, framing i) | 10 lines |
| `AiTextHygiene.AnyUrl` | :147 | :146-149 `(?:https?://|www\.)[^\s<>""«»]+` (IgnoreCase \| Compiled) | 1 line |
| `AiTextHygiene.StripUnsanctionedLinks` | (named) | :217; `SplitSentences` :161; `InsideAny` :187 | — |

WPF strip semantics (verbatim from `AiTextHygiene.cs:195-260`): fast path — no "http"/"www." substring → return unchanged; else find `AnyUrl` matches; sentence-split with URL spans kept whole (a URL is full of sentence punctuation); **the whole sentence carrying an unsanctioned link goes, not just the URL**; each candidate URL is trailing-trimmed of `.,)]!?;:'"` before the sanction check; kept sentences concatenated, `\s{2,}` collapsed to one space, trimmed; zero sentences dropped → original returned. WPF logs a count-only `[AI-LINK]` line; the port omits even that (narrower; the typed outcome carries observability — see §4).

## 2. F1 — the two marker lists, reconciled (framing d)

Computed programmatically over the two WPF source arrays, not by eye:

- `AwarenessPrivacyRules.IncognitoMarkers`: **35** entries. `AwarenessObserverPolicy.IncognitoMarkers`: **35** entries. Shared: 15. **They DIVERGE.**
- Only in PrivacyRules (20): `private tab`, `private mode`, `privates surfen`, `incógnito`, `ventana privada`, `fenêtre de navigation privée`, `anônima`, `navegação anônima`, `janela anônima`, `navegação privativa`, `приватный просмотр`, `приватное окно`, `プライベートブラウジング`, `プライベートウィンドウ`, `사생활 보호 모드`, `인프라이빗`, `隐身`, `隐私浏览`, `隐私窗口`, `无痕浏览`.
- Only in ObserverPolicy (21): `privatfenster`, `navigation privee`, `fenetre privee`, `modo incógnito`, `modo incognito`, `navegacion privada`, `navegação privada`, `navegacao privada`, `janela privada`, `navigazione anonima`, `finestra anonima`, `privé-venster`, `prive-venster`, `privé venster`, `prywatne`, `okno prywatne`, `приватное`, `無痕`, `隱私瀏覽`, `プライベート`, `프라이빗`.
- **Port decision: the UNION — 55 entries, one definition.** Rationale: this filter only ever *drops*; a marker present in either WPF enforcement path means WPF dropped that title on at least one path, so the union is the only choice that never re-admits a title some WPF path suppressed. Nine entries are substring-subsumed by other union entries under `Contains` matching (`incognito`, `incógnito`, `navigation privée`, `anônima`, `无痕`, `prywatne`, `приватное`, `プライベート`, `프라이빗`) — kept anyway for verbatim fidelity; subsumption is recorded here, not silently deduped. WPF's own comment (ObserverPolicy.cs:204-208) records that the *scrubber* once had two divergent copies and the dead one was deleted — the same one-definition discipline (SP-055 `IsAssetActive` in port terms) is applied here to the marker lists.

## 3. F1 — the blank-title case, established and decided (framing e)

**Empirical establishment (port code path, `AiAwarenessService.cs` `AiWindowTitleCapability.TryCaptureForegroundTitle`):** when a foreground window exists but `GetWindowTextLengthW(hwnd) <= 0`, the method **returns `true` with `title = string.Empty`** — the "a foreground window with an empty title: the observation mechanism works" branch. So YES: an empty title is a *successful* capture in the port, and `ObserveForegroundTitle` surfaces it as `AiTitleObservation.Observed("")`. Whitespace-only titles are equally possible via `GetWindowTextW`.

**WPF's net behavior on blank is a DROP, and it is split across two rules:** `IsIncognitoTitle("")` returns **false** (fail-open in isolation — verified ObserverPolicy.cs:347-348), but (a) frames with neither title nor process are dropped earlier as `FrameDrop.NoForeground` (:254-255), and (b) a blank `RawTitle` that reaches `AwarenessPrivacyRules.Evaluate` is dropped as `AwarenessDropReason.NoTitle` (:276-277) with the comment *"No title means the incognito test cannot run, and the incognito test is the one rule that has no user-facing escape hatch. An unanswerable question is a drop."*

**Port decision: blank/whitespace captures are DROPPED at the observation seam** — typed `AiTitleObservation.Unavailable` with a content-free stable code, never `Observed("")`. Reason: copying `IsIncognitoTitle`'s `return false` would import a fail-open branch into a path whose WPF downstream guard (Evaluate's `NoTitle` drop) does not exist in the port; WPF's net behavior is fail-closed, so the port is fail-closed. This also keeps the F1 question answerable-by-construction at the packaging seam (a blank title never arrives via the wired path).

## 4. F3 — insertion point and emptied-reply typing (framings g, h)

**Port site (`AiOperationPipeline.cs`, the shared live-operation body):** stale check → output moderation (`:319` region) → **memory persist (`:344`)** → reply application (`reply = produced`).

**Derived insertion point: immediately AFTER output moderation passes, BEFORE the memory-persist block and before reply application.** Justification:
- WPF's rule (CompanionBrain.cs:262-269): hygiene is applied to the reply the service *returned* (i.e. after WPF's in-service handling), before the text is appended to session, shown, or persisted — "strip before the text reaches the bubble, history, or disk".
- Port contract §7 rule 1: output moderation sits after the stale check and before reply application. The strip is part of *producing the applied reply*, and it only ever removes whole sentences and collapses whitespace (kept spans are contiguous slices of the moderated original; no token fusion is possible), so moderating the pre-strip text moderates a strict superset of anything that can be shown or persisted. The ordering moderate → strip → persist/apply satisfies both WPF's rule and §7 rule 1.
- **Persistence delta, stated (framing g):** BEFORE this packet, `_memory.Append(...)` at :344 persists the raw model text (`passed.Text`). AFTER, it persists the stripped text — strictly less. An emptied reply persists **nothing** (neither turn is appended — the port equivalent of WPF's `Session.Remove(userTurn)` "don't poison history with an unanswered turn"; the port appends both turns in one block, so skipping the block preserves pairing). This is the ONLY persistence change: no new field, no new file, no new write path — grep proof in §8.

**Sanctioned-link surface:** WPF's default `isSanctioned` is `CompanionLinkIndex.IsSanctioned` — the app-issued media pool. **The port has no link index and issues the model no links** (grep: no `Sanctioned`/`LinkIndex` under `client/src/`; the only `http` literal in the AI tree is the loopback endpoint config). Recorded divergence: in the port **every** reply URL is unsanctioned, so the strip drops every URL-carrying sentence. That is the narrowing direction and matches the audit's "Strip only" sizing.

**Emptied-reply typing (framing h) — `AiReply.Unavailable("reply-stripped-empty")`, chosen from the EXISTING vocabulary:**
- `Generated` is dishonest (no model text survived). `Refused` is dishonest (this is not a moderation verdict; there is no category code — the strip is not the boundary). `Fallback` requires app-authored canned text, and the pipeline seam has none — a `Fallback("")` would surface an empty bubble and a lie about provenance.
- `Unavailable` is the vocabulary's "the operation yields no applicable reply, not an error, distinct from Refused" case (vocabulary doc comment, AiOperationVocabulary.cs:163). The stable code names exactly what happened. Downstream it reproduces WPF's own behavior by path: the awareness keyword route maps `Unavailable` + caller-supplied canned text → typed `Fallback` (WPF chat: "same treatment as a canned fallback", CompanionBrain.cs:279-284); the window-reaction route maps it to a typed drop (WPF reaction: empty non-AI result, :358-361); the interactive surface renders the existing unavailable presentation with the code, never badged (CompanionBubbleModel.ForReply). **No new `AiReply` case; no existing case's meaning changed. `AiOperationVocabulary.cs` is untouched** (out of file scope — the code is a literal at the call site, consistent with existing `http-{status}` literals in LoopbackOllamaProvider.cs).

## 5. Boundary clearance per filter (framing b) — every delta less or equal

| Filter | Observed | Retained | Transmitted | Logged |
|---|---|---|---|---|
| F1 before | raw foreground title returned to caller; any title packaged under consent | nothing new | raw title in the awareness prompt | content-free only |
| F1 after | incognito/blank titles **never returned, never packaged** | nothing | incognito contexts **dropped, zero transmission** | content-free only; +1 stable code, +1 typed drop kind (no title content — §12-clean) |
| F2 before | — | — | raw title crosses in the prompt | — |
| F2 after | — | — | title is scrubbed (emails / 6+ digit runs / control chars removed, whitespace collapsed, role-marker lines dropped, ≤80 chars) or **not carried at all** | — |
| F3 before | — | raw reply persisted (interactive), raw reply applied | raw reply | — |
| F3 after | — | **stripped** reply persisted; emptied reply persists **nothing** | stripped reply; emptied → typed Unavailable, nothing shown | unchanged (WPF's count-only `[AI-LINK]` line deliberately not ported — narrower) |

No new datum is observed (no process name, app id, window class, dwell time — those stay owner-blocked on O2). No new persisted field, no new log line, no network change, no consent-surface change.

**Contract wording (framing c): `ai-operation-contract.md` is NOT edited.** No filter *requires* a contract change to be honest (§7 rule 1 ordering is preserved; §12's schema is untouched — the new codes are content-free stable tokens). Two additive wordings the orchestrator may consider at land, named here and NOT written: (1) §7 rule 1 could name the subtractive hygiene strip as part of reply application; (2) §4 rule 3's drop taxonomy could name the new `PrivacyFiltered` drop kind.

## 6. Design summary (implemented in Step 2)

- **One new file** `client/src/CcpClient.Desktop/Ai/AiPrivacyFilters.cs` — the single shared home (justification: three subtractive filters from one audit; the packet allows at most one file and forbids three; F1/F2/F3 are one mechanism family — "remove or narrow what flows"). Holds: `IncognitoMarkers` (union of 55), `LooksIncognito`, `SanitizeTitleForWire` + `MaxTitleLength` + the two verbatim regexes + the `SanitizeDisplayName`/`RoleMarkers`/`LooksLikeInstruction` chain, `StripUnsanctionedLinks` + `AnyUrl` + `SplitSentences`/`InsideAny`, and the pure observation classifier `ClassifyCapturedTitle` (so the F1 seam logic is unit-testable without a real foreground window).
- **F1 application:** `ObserveForegroundTitle` routes the capture through `ClassifyCapturedTitle` (incognito or blank → typed `Unavailable`, content-free codes); `RunReactionAsync` checks `LooksIncognito(context.Title)` before packaging → typed `Dropped` with **new** `AiAwarenessDropKind.PrivacyFiltered` + content-free diagnostic (machinery vocabulary, additive — not an `AiReply` case); `TryPackage` re-checks and refuses (false, request null, refusal null — documented; WPF's own "re-checked inside the shared rules… defence in depth" pattern, ObserverPolicy.cs:227-229).
- **F2 application:** inside `TryPackage`, the Title field is **moderated raw first** (landed behavior byte-identical — see §7 consult correction), then the **scrubbed** value is assembled into the request; scrub-to-empty → the package carries **no title** (empty slot — WPF `ResolveTitle`→null→`TitleForWire: null` semantics: the frame proceeds title-free, AwarenessPrivacyRules.cs:455-466).
- **F3 application:** in the pipeline body, after output moderation, before persist/apply: `StripUnsanctionedLinks`; changed → `generated with { Text = stripped }`; emptied → `AiReply.Unavailable("reply-stripped-empty")`, Completed, persist skipped entirely.

## 7. Consults

**Pre-approach (solo; asked narrowly, reply capped at 250 words).** The tool returned a complete, un-truncated verdict; the tool response does not surface the answering model's identity (recorded as such — no stitching from reasoning). Verdict: **one real flaw found, everything else sound.**

- **FLAW (corrected before implementation):** my draft F2 order (scrub → then moderate inside `TryPackage`) can **WIDEN** flow — a Title `"forbidden-token@x.com"` that moderation blocks today (zero transmission) would scrub to empty, pass moderation, and the reaction would transmit Category/App/Duration where previously nothing was transmitted. A framing (b) stop condition. **Fix applied to the plan: moderate the RAW field first (landed behavior byte-identical), then assemble the SCRUBBED value** — scrub only removes, so anything blocked stays blocked and the assembled title is strictly narrower (monotone). This also makes F2 consistent with F3's moderate→strip superset argument. Conceded edge, recorded as a non-regression: control-char removal could fuse a blocked token (`for\0bidden` → `forbidden`) that then escapes moderation — but that text was equally transmittable pre-packet (moderation saw `for\0bidden`, not `forbidden`), so flow is still ≤ before.
- Confirmed sound: union-of-55 (drop-only rule; union never re-admits what either WPF path suppressed); blank → typed `Unavailable` (WPF net is fail-closed; copying `return false` imports fail-open into a guardless path); `Unavailable("reply-stripped-empty")` for the emptied reply; F3 after moderation, before persist.
- **Pre-flight check 1 (ran):** this machine's real foreground title is non-blank (length 18) and matches zero union markers — the landed `TitleObservation_GatedByConsentAndCapability_TitleNeverLogged` pin (asserts `Observed` from the real foreground window) is not flipped by F1's observation-seam filter. Title content itself not recorded (privacy discipline; length only).
- **Pre-flight check 2 (ran):** no exhaustive switch / `Enum.GetValues` over `AiAwarenessDropKind` anywhere (the only `GetValues` in the tree is over `AiCooldownKind`, AiAwarenessCooldownTests.cs:34-35); `AiOperationContractTests` round-trips fixed `AiReply` instances, not a case enumeration. Adding `AiAwarenessDropKind.PrivacyFiltered` breaks no shape pin.

## 8. Implementation + bite evidence

(to be filled — diff summary, no-new-datum grep proof, bite matrix, floor bump, run table)

## 9. Honesty cell

(to be filled at Step 4 — including: F1/F2 harden a path with NO product consumer today (framing k); F3's path is live; three rows of one audit table filtered at authoring; row :46 stays OPEN; deferred halves; Linux unproven; filters are lossy by design)
