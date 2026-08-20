# SP-126 — plan checkpoint (step 1). Nothing else in the tree has been touched.

Branch `lane/SP-126-haptic-limb`, worktree `.claude/worktrees/agent-a57fa102fdd16b4a2`, base
`5fd9a8671`. Baseline gate before any edit: `check-warnings.mjs` → **0 warnings / 0 errors, 4
projects**. Floor pin as given: **2332 unit / 144 headless**.

---

## 0. THE DECISION IS NOT WRONG. C+ STANDS. I do not stop on it.

I re-derived the packet's peak-of-sum claim from the shipping bytes rather than from the census
prose, and every link holds:

| link | source, verified this session |
|---|---|
| flash ladder posts at priority 1 | `Services/Haptics/HapticService.cs:786` — `HapticPatterns.Render(rule.Mode, intensity, 250, priority: 1, target: rule.Target)` |
| subliminal pulse posts at priority 1 | `HapticService.cs:880` — `PostEvent(HapticEventKind.SubliminalTrigger, null, duration, null, 1)` |
| sum WITHIN a priority group | `Core/HapticMixer.cs:487-503` — `foreach (var priority in DistinctPriorities(pulses)) { … groupSum += s.Values[k] …; if (groupSum > groupPeak) …; if (groupPeak > 1) groupPeak = 1; if (groupPeak > transient) transient = groupPeak; }` |
| MAX across groups, then over the continuous floor | `HapticMixer.cs:502`, `:506` — `raw = Math.Max(state.Floor, transient)` |
| the master arithmetic | `HapticMixer.cs:509-518` — `v = raw * MasterIntensity; if (raw > 0) v = Math.Max(v, 0.06); v = Math.Min(v, MasterCap); × trim` |
| shipped defaults | `Models/HapticSettings.cs:29` `_globalIntensity = 0.7`; `HapticMixer.cs:77` cap `0.70`; `:83` `MinPerceptibleIntensity = 0.06`; `:79` `DefaultMaxConcurrentPulses = 4`; `HapticEventRule` defaults `Enabled=true, Intensity=0.5` (`HapticSettings.cs:741-742`) seeded from `_flashDisplayIntensity/_subliminalIntensity/_bouncingTextIntensity = 0.5` (`:46`, `:50`, `:53`) |

Worked: two overlapping 0.5 transients in ONE group → sum 1.0 → group clamp 1.0 → `raw` 1.0 →
`1.0 × 0.7 = 0.70` → cap 0.70 → **0.70**. MAX instead → 0.5 → `0.5 × 0.7 = 0.35`, under the cap →
**0.35**. A factor of two, exactly as priced.

**Reachable here, twice over.** The flash ladder (sites 1-3) and the subliminal pulse (sites 14-15)
are both in the wired subset, both at priority 1, and the ladder alone spans 3503 ms — any session
with Flash Images and Subliminals both armed overlaps them routinely. Second instance: bounces post
at priority 0 (`HapticService.cs:821`) and sum with each other; a 60 ms bounce renders as a 158 ms
envelope (below), so two bounces inside 158 ms sum upstream.

**And the evaluator does not need a timer.** Upstream's 10 Hz loop exists because it is the *sender*;
the evaluation itself is a pure function of (active envelopes, layer values, instant). C+ therefore
becomes: the union of every active program's own sample instants is the wake-up set, scheduled
one-shot on the injected `ISessionClock`; at each wake-up every active program is evaluated **at that
same instant** and summed per priority. That is upstream's shared-grid rule
(`HapticMixer.cs:942-974`, `PulseSample.Values` `:1311-1314`) with the grid supplied by the programs
instead of by a poll. Nothing wakes when nothing is playing.

---

## 1. THREE FINDINGS I MUST RAISE BEFORE I WRITE PRODUCT CODE

### FINDING A (BLOCKER) — the census pins the exact LINE NUMBERS of the four files this packet's `fileScopeMustChange` orders me to edit.

`HapticSiteCensusTests.ProbePortCitation` (`client/tests/CcpClient.Tests/HapticSiteCensusTests.cs:486-509`)
does `File.ReadAllLines(full); lines[number - 1].Contains(needle, StringComparison.Ordinal)` against
citations parsed out of `client/docs/haptic-limb-census.md`. **Both files are CLOSED to me.** The
pinned port citations are:

| file | pinned lines | needle |
|---|---|---|
| `Effects/FlashSurfacePresenter.cs` | **294**, **298** | `serve pop / hydra / XP mechanics this port does not have` · `_surfaces.Place(slot, request, frame, draw.Lifetime)` |
| `Effects/MandatoryVideoEffect.cs` | **279**, **363**, **374** | `_surface.Begin(firing.Path, firing.MaxLength, OnClipEnded)` · `not ported and not shown as dead controls: attention` · `RefreshSchedule()` |
| `Effects/SubliminalsEffect.cs` | **56**, **197** | `follow-up (<c>:276-404</c>)` · `_surface?.Show(firing.Card)` |
| `Effects/BouncingTextField.cs` | **223** | `Bounces++` |
| `Haptics/IHapticSink.cs` | **219**, **221** | `a script player this port has not ported at all` · `haptic INPUT` |

So **any line added above any of those lines reds a closed guard**. Ordinary constructor injection
(a `using`, an XML-doc'd field, a ctor parameter, an assignment) adds 6-12 lines above every one of
them. This is a genuine collision between the packet's File Scope and the pin the packet itself
names as authoritative; it is not something I can resolve by being careful.

**What I want (preferred): open `client/docs/haptic-limb-census.md` for DATA-ONLY edits** — the port
trigger line numbers in the `census:sites` table and the port decision line numbers in the
`census:decisions` table, nothing else. No row added, no row removed, no verdict changed, no total
touched, no upstream citation touched. `HapticSiteCensusTests.cs` itself would need **no** edit at
all, because it reads the citations out of the document at test time. That is exactly the
maintenance the pin exists to force when code legitimately moves, and it is the whole reason the
data lives in a document rather than in the test.

**Fallback I can build inside the scope as written, if that is refused** — every pinned file is
edited so that the line count *above* each pin is unchanged:

- the new call goes on a line inserted strictly *after* the pinned statement (allowed: it shifts only
  what is below);
- the field it needs is declared *after* the pinned line too (C# permits fields anywhere in a class)
  and set by a public `AttachHaptics(IHapticLimb)` method also declared below the pin, called by
  `SessionParticipant`;
- `Effects/BouncingTextField.cs` is then **not touched at all**: `BouncingTextSurfacePresenter` (not
  pinned) already drives `Advance` and can read the `Bounces` counter across the call, which is the
  same moment `:223` is;
- `Haptics/IHapticSink.cs:209-223` keeps its now-false paragraph *"Nothing in this build sends
  anything to this sink"* and its stale "THIRTEEN", corrected only in `record.md` and the ledger.

The fallback costs: post-construction mutable wiring instead of ctor injection (against the port's
composition style), one site proven from the presenter rather than at the statement the census
cites, and a knowingly false sentence left in `IHapticSink`. I will build it if told to; I would
rather change nine line numbers in a data table.

### FINDING B (BLOCKER) — the one line that makes the limb reachable in the product is outside my File Scope.

The sink is owned by `HapticParticipant` (`Haptics/**`, open to me). The five trigger points are
owned by `SessionParticipant` (`Session/**`, open to me). **The only thing that connects two
participants is `Lifecycle/CompositionRoot.DefaultParticipants`, and `Lifecycle/**` is closed**
(prose File Scope table; note the Contract table's `fileScopeMustNotChange` omits it — the prose is
stricter and I am obeying the prose). Today `session` is constructed at `CompositionRoot.cs:227` and
`HapticParticipant` at `:291`, so there is not even an ordering that would let one see the other.

I will **not** work around this by having `SessionParticipant` call `HapticSinkFactory.Create()`
itself: that manufactures a SECOND sink, which is harmless only while `AdmittedRoutes` is empty and
becomes two live WebSocket/HTTP clients the day a route is admitted. Building a known future defect
to make a completion criterion read green is the opposite of this packet.

**What I want:** three lines of `Lifecycle/CompositionRoot.cs` — hoist the `new HapticParticipant(…)`
into a local *above* `var session = …` (construction starts nothing, SP-003 §4.4, so this is safe),
pass its limb into the `SessionParticipant` constructor, and leave the participant registered LAST in
the returned array so teardown order is unchanged.

**If refused:** the limb lands complete, proven, and **unreachable on the product path**, and I will
say exactly that in the report and in a board row — D179 would be closed in the modules and re-opened
one layer up. I do not think that is what the packet wants, so I am asking.

### FINDING C (behaviour) — site 4, the luminance layer, is DISABLED at upstream's shipped defaults.

The packet says to port the luminance layer's arithmetic (`autoZeroMs = clamp(lifetimeMs, 100,
30_000)`). The arithmetic is real and I have it (`Services/Flash/FlashService.cs:1598-1635`). But the
*feature switch* in front of it ships OFF:

- `Models/HapticSettings.cs:373` — `private bool _luminanceSyncEnabled = false;`
- `:435-439` — *"Drive the Luminance layer from the average brightness of each displayed flash image.
  **Off by default until the Phase E UI exposes it. When off the hook is a single bool test per
  flash.**"*
- `FlashService.cs:1603` — `if (haptics == null || !haptics.Settings.LuminanceSyncEnabled) return;`
  — *"the whole cost when off"*
- `HapticSettings.cs:526-527` even says so twice: the schema-2 pass enables the LAYER row, and *"The
  FEATURE toggle (LuminanceSyncEnabled, default false) stays the real gate."*

So at shipped defaults a placed flash upstream commands **the decay ladder and nothing else**, and
upstream does not even run the pixel scan. This port carries one persisted haptic setting (`Enabled`)
and no dial for this, and `Views/**` is closed so no control could ever turn it on.

That leaves three options and none of them is "port the arithmetic and drive it":

1. **Drive it ON** — a continuous layer the upstream default user never feels. A knowing behavioural
   divergence *upward*, on the one capability where "louder than expected" is the failure mode that
   matters.
2. **Drive it behind a port-side constant carrying upstream's `false`** — a branch the product never
   takes plus an 8×8 Rec.601 alpha-weighted sampler (`FlashService.cs:1640-1688`) the product never
   runs. Present-and-inert, which is exactly what `HapticSettingsDocument`'s own D93 paragraph bans.
3. **Do not wire site 4; record the divergence** — the port's flash moment commands the ladder, which
   is what the shipped default commands.

**I plan on (3)**, and the `Layer` + auto-zero verb it would have used is built anyway and *is*
reachable, because the video background layer (sites 6, 7, 12) uses exactly that verb and *is*
enabled by default. Overrule me at the checkpoint if you want (1); I will not choose it silently.

---

## 2. WHAT I WILL BUILD — C+, no timer, no loop

All new files under `client/src/CcpClient.Desktop/Haptics/`.

### 2.1 `HapticEnvelope.cs` — the vocabulary (upstream's shapes, ported)

- `HapticPulse(double Intensity, int AttackMs, int HoldMs, int DecayMs, int Priority)` with
  `TotalMs`, `LevelAt(long t)` and `PeakInstant(from, to)` — ported line-for-line in effect from
  `HapticMixer.ActivePulse.Envelope` (`:1214-1224`) and `PeakInstant` (`:1236-1247`), including the
  `t >= TotalMs → 0` clamp that stops a hard-edged pulse rendering at full level one tick past its
  end.
- `HapticStep(int DelayMs, HapticPulse Pulse)` — upstream `HapticPulseStep` (`HapticMixer.cs:8-16`).
- `HapticShape { Constant, Pulse }` and `HapticShapes.Render(shape, intensity, durationMs, priority)`
  — `HapticPatterns.Render` (`:42-134`) with `MinFeltOnMs = 130` (`:36`) and `MinFeltDurationMs = 200`
  (`:40`), and the duration clamp `Math.Clamp(durationMs, 200, 60_000)` (`:47`).
  **Only two of upstream's six modes**, and that is a decision with a reason: the mode is a per-event
  SETTING (`HapticSettings.cs:57-66`) this port does not carry, so at shipped defaults the reachable
  sites use exactly Constant (flash, `_flashDisplayMode`) and Pulse (subliminal `_subliminalMode`,
  bouncing text `_bouncingTextMode`). Wave/Heartbeat/Escalate/Earthquake have **no reachable caller
  here**, and shipping them would be four unreachable renderers — absent rather than inert (D93).
  Recorded as a divergence.
- `HapticEnvelopes` — one factory per wired site, each pinned against upstream's own arithmetic:

| site(s) | factory | arithmetic, cited |
|---|---|---|
| 1,2,3 | `FlashDecayLadder(start = 0.5)` | 8 rungs, `max(start·0.7^i, 0.06)` at `i·450` ms, each `Render(Constant, ·, 250, priority 1)` (`HapticService.cs:781-788`). Constant 250 → attack `clamp(250/6,10,60)=41`, hold 250, decay `clamp(250/4,30,150)=62` → 353 ms per rung → **span 3503 ms**, the code's number, not the comment's (`:761`, D205) |
| 6,7 | `VideoBackgroundLevel()` | `max(VideoIntensity·0.1, 0.06)` = `max(0.05, 0.06)` = **0.06** at the shipped 0.5 (`:832-848`); latched, no auto-zero, exactly as upstream (`:848`) |
| 12 | — | layer → 0 (`:853-858`) |
| 14,15 | `SubliminalPulse(phrase)` | `Render(Pulse, 0.5, TriggerDurationMs(phrase), priority 1)` (`:877-881`); `TriggerDurationMs` = 250 for `cum`/`collapse`/`drop`, 120 for `freeze`/`zap`, else 150 (`:899-909`), multiplier **1.0** because no Buttplug route is admitted (`:903`) |
| 18 | `BouncePulse()` | `Render(Pulse, 0.5, 60, priority 0)` (`:820-821`) — 60 floors to 200, Pulse gives `clamp(200/200,1,40) = 1` tap of `(0.5, attack 8, hold 130, decay 20)` = 158 ms |

### 2.2 `HapticLimb.cs` — the C+ evaluator and scheduler, no timer

State: active transients (with `StartAt`), continuous layer values with their auto-zero deadlines,
the soft-ramped floor, the pending clock handles.

- **Promotion + concurrency cap** — `HapticMixer.PromotePending` (`:880-931`): cap
  `max(1, MaxConcurrentPulses) = 4`; retire a CORPSE first (`now >= StartAt + TotalMs`); otherwise
  evict the weakest priority and **drop the newcomer when the weakest active priority `>=` its own**.
- **Evaluation at one instant** — `HapticMixer.BuildOutputs` (`:441-520`): layers by MAX into
  `floorTarget`; soft ramp `maxRise = 100 / SoftRampMs(800)` on rises, instant on falls (`:470-474`);
  transients summed **per priority at that instant**, group clamped to 1, MAX across groups
  (`:476-503`); `raw = max(floor, transient)` (`:506`); then `Finish`: `× 0.7`, lift to 0.06 when
  `raw > 0`, `min 0.70` (`:509-518`).
- **Wake-ups, no poll** — for each promoted pulse the limb schedules one-shots at
  `{start, start+attack, start+attack+hold, start+total}` ∪ `{start + k·100ms}` on the injected
  `ISessionClock`, and at each it evaluates ALL active programs at that same instant. 100 ms is
  upstream's own `DefaultTickMs` (`:70`), so the ramp resolution is upstream's; the crest instants are
  upstream's `PeakInstant` rule, which is what keeps a short tap audible. Nothing is scheduled while
  nothing is playing.
- **Sending** — for every device key in the injected roster: `SetOutputsAsync(key, [new
  HapticOutput(0, HapticLevel.Of(level))], ct)`. No suppression, no refresh, no re-assert: the packet
  is right that those are delivery, and `HapticContracts.cs:70-73` puts them on the provider.
- **The gate is central, never at a call site** — one `Func<bool>` (`participant.OutputAllowed &&
  participant.Enabled`) tested where upstream tests it, in the equivalent of `Play`
  (`HapticMixer.cs:843`). D204 preserved: the moment is still counted when the gate refuses, because
  upstream's `Announce` runs after `Play` unconditionally (`HapticService.cs:390`, `:791`, `:849`).
- **`StopAll()`** clears every transient and zeroes every layer and calls `sink.StopAllAsync()`.

### 2.3 `IHapticLimb.cs` — the verbs, named for the MOMENT

`FlashPlaced()` · `VideoStarted()` · `VideoStopped()` · `SubliminalShown(string phrase)` ·
`BounceHit()`. No null-object stub: the modules hold `IHapticLimb?` and call through `?.`, so a
module with no limb is *absent*, not *no-op*.

`FlashPlaced` also carries upstream's replace rule: a new ladder **cancels the running flash ladder**
before posting (`HapticService.cs:775-776`), which is why flash-over-flash does not sum even upstream.

### 2.4 The five trigger points, and the envelope each carries

| port statement | sites | what the limb is told, and when |
|---|---|---|
| `Effects/FlashSurfacePresenter.cs:298` (after `_surfaces.Place`) | 1,2,3 | `FlashPlaced()` — once **per placed image**, after the surface is really placed, because upstream fires inside `SpawnFlashWindow` per window and each call replaces the last ladder |
| `Effects/MandatoryVideoEffect.cs:279` (after `_surface.Begin`) | 6,7 | `VideoStarted()` — **only when the outcome is `Available`**; upstream fires at `VideoService.cs:2580`, immediately under its own *"Playback is REAL from here"* (`:2567-2576`) |
| `Effects/MandatoryVideoEffect.cs:374` (`OnClipEnded`) **and** `:299` (`OnDisarmed`) | 12 | `VideoStopped()` — see §3 |
| `Effects/SubliminalsEffect.cs:197` (after `_surface?.Show`) | 14,15 | `SubliminalShown(phrase)` — the duration is keyed off the phrase's own wording, so the card's text travels |
| `Effects/BouncingTextField.cs:223` (after `Bounces++`) | 18 | `BounceHit()` — between the bounce bookkeeping and the 10 % re-roll, the same place upstream sits (`:516` against `:519`) |

Not wired, and each with its reason: **site 4** (§1 Finding C) and the **eight absent-by-decision**
sites, untouched. I checked every one of the eight against its quoted decision and **found none I
believe is reachable**: no pop route of any kind exists (all flash surfaces are `ClickThrough: true`,
`FlashSurfacePresenter.cs:297`), no script player, no attention checks, no toy INPUT verb on the
seam, and neither Bambi phrase is shown.

### 2.5 The anticipation, recorded not built

Upstream fires the subliminal haptic **first** and delays the visual by `SubliminalAnticipationMs`
(250 ms, 1300 on Buttplug — `HapticService.cs:88`); this port shows the card immediately. Building it
would delay a shipped module's *picture* by 250 ms to anticipate a vibration that cannot happen,
which is a visible regression bought for nothing. Recorded as a divergence; the census already books
it as what the 14/15 collapse loses.

---

## 3. THE STOP, AND WHERE IT GOES

Upstream's stop is reached **only** through `Cleanup()` (`VideoService.cs:6580`), while the comment
between its two stops (`:6581-6583`) asserts *"Runs on every teardown path (natural end, skip, panic,
attention retry) because CloseAll is the single funnel for all of them"* — and both stops sit
**beside** `CloseAll(...)`, not inside it. `ForceCleanup` is the panic path and carries no haptic
reference. The start passes no `autoZeroMs` (`:2580` → `HapticService.cs:848`), so the layer latches
unbounded. D203, confirmed by reading.

The port's paths that take a clip off the screen, enumerated rather than assumed:

1. `MandatoryVideoEffect.OnClipEnded` (`:372-376`) — natural end, max-length cap, or the surface
   stopping holding it.
2. `MandatoryVideoEffect.OnDisarmed` (`:299`, `_surface.End()`) — reached from `OwnedSessionEffect.Disarm`
   (`:220`), which is reached from `SessionEngine.Stop` (`:185`, `:228`), which is reached from
   `SessionParticipant.StopAsync` (`:735`). **This is the port's panic/stop/teardown funnel**, and
   `Effects/VideoSurfacePresenter.cs:466` clears the ended callback, so it does NOT reach (1).
3. Generation cancellation reaches `ReleaseWork` only (`PacedSessionEffect:188`, sealed) — it drops
   the pending schedule and cannot start a clip; it does not take a playing one down, so it is not a
   stop path for this layer. Named so the enumeration is complete rather than convenient.

**So the stop goes on BOTH (1) and (2)**, and the limb additionally zeroes every layer in its own
`StopAll`, which the app teardown already reaches through `HapticParticipant.ShutdownStopAsync` in
the reserved pre-drain head slot. Upstream's defect is not copied and the divergence is recorded.

---

## 4. HOW PEAK-OF-SUM IS PROVED WITHOUT A DEVICE

A `RecordingHapticSink` in `client/tests/CcpClient.Tests/`, and the SP-108 hazard is the design
constraint: **it records and never transforms.** It stores the `IReadOnlyList<HapticOutput>` it was
handed, verbatim, with the device key and the order; it does not clamp, round, quantize, coalesce,
de-duplicate or drop. One fact does nothing but prove that: it hands the double an out-of-band
sequence and asserts the recording is bit-identical to what was passed, so a future "tidy" that adds
a clamp reds immediately instead of laundering the clamp it is meant to test. It answers
`ObserveAsync` with one device key so there is something to address, and it never returns
`Available` from anything on a product path.

The proof itself:

- **peak-of-sum bites** — two priority-1 transients at 0.5 overlapped on the shared instant → the
  recorded level is **0.70** (sum 1.0 → ×0.7 → cap). The fact asserts 0.70 **and names 0.35 as the
  value the MAX rule would produce**, so degrading the evaluator to MAX reds it rather than merely
  changing a number nobody reads.
- **the group boundary is real** — a priority-0 bounce concurrent with a priority-1 subliminal is
  MAX'd, not summed.
- **the cap bites before the sum runs away** — three overlapping 0.5s still record 0.70.
- **the concurrency cap bites** — five overlapping transients, weakest priority evicted, newcomer
  dropped when it does not out-rank (`HapticMixer.cs:894-915`).
- **each site's envelope** — the ladder's 8 recorded levels and their instants, its 3503 ms span, the
  replace rule, the three subliminal durations, the bounce tap, the video layer's 0.06 and its zero.
- **nothing moves on the product path** — the real `HapticSinkFactory.Create()` behind the limb, a
  full session's worth of moments, and **zero** `SetOutputsAsync` calls, because there is no device
  key to address and no admitted route to get one.
- **the dot is unchanged** — `HapticParticipant.Dot` is `Enabled && LastObservation is { Confirmed:
  true }`; a limb touches neither conjunct, so `Live` stays unreachable by construction. A fact
  enumerates the reachable values with a limb attached and busy. `Views/**` untouched.

Every wait is `TestWait`; every timing is driven through the injected `ISessionClock`, which is
already fake-able in the unit project.

---

## 5. ONE MORE SEAM GAP, REPORTED NOT FIXED

`HapticServerObservation` carries `DeviceKeys` and no actuator inventory, so a limb can only address
`ActuatorIndex 0`. Upstream fans one intensity across **every** Vibrate feature
(`ButtplugProvider.cs:264-278`). `IHapticSink.cs` is byte-identical by SP-119's own claim and I am
not changing it; this goes in the ledger and the report as the thing a provider packet must close.

---

## 6. FILES I EXPECT TO TOUCH

New: `Haptics/HapticEnvelope.cs`, `Haptics/HapticLimb.cs`, `Haptics/IHapticLimb.cs`;
`tests/CcpClient.Tests/RecordingHapticSink.cs`, `HapticEnvelopeTests.cs`, `HapticLimbTests.cs`,
`HapticLimbSiteTests.cs`.
Edited: `Haptics/HapticParticipant.cs` (owns the limb; its `Dot` remarks currently assert the modules
are silent), `Session/SessionParticipant.cs` (threads it to the five points), `Effects/FlashSurfacePresenter.cs`,
`Effects/MandatoryVideoEffect.cs`, `Effects/SubliminalsEffect.cs`, `Effects/BouncingTextField.cs`
(or `BouncingTextSurfacePresenter.cs` under the Finding A fallback), plus the existing effect facts
those constructors touch.
Docs: `client/docs/wpf-surface-reachability.md` (D210+, divergences only),
`spine-tasks/SP-126-haptic-limb/record.md`, `floor-delta.json`.
Requested amendments: `client/docs/haptic-limb-census.md` (data-only line numbers) and three lines of
`Lifecycle/CompositionRoot.cs`. **Neither touched, and neither will be, without an answer.**

## 7. WHAT THIS PLAN ALREADY CANNOT PROVE

Nothing here will ever show that a device moved, or that a person felt anything: no route is
admitted, `SetOutputsAsync` refuses, and `HapticSinkFactory.DeviceManualGate` names the four-step
gate whose last step no automated step on any platform discharges. Everything below is pure-logic
unit work: no headless frame, no headed capture, no rendering, focus, audio or window behaviour is
touched or claimed. Linux is unchanged and unproven, and for this capability it refuses identically.

---

## 8. CHECKPOINT ANSWER — all three granted, recorded here before any product edit

The orchestrator answered the checkpoint. Recorded verbatim in effect, with the scope each grant
opens, so nobody later has to reconstruct why files outside the packet's own File Scope were touched.

### A — GRANTED. The census document is open for DATA-ONLY line-number edits.

The packet author verified the collision and named it an authoring defect: `HapticSiteCensusTests.cs:506`
is `lines[number - 1].Contains(needle, StringComparison.Ordinal)`, the census carries fifteen pinned
port citations, and the packet closed the census and its guard while opening the very files those
citations pin.

**Opened:** `client/docs/haptic-limb-census.md`, port line NUMBERS only. Nothing else in that
document may move — not a verdict, not a quote, not a decision row, not an upstream citation. The
record must state which numbers changed, from what to what, and why, because a line-number edit
travelling silently beside a semantic one is the drift this port hunts. The guard file itself needs
no edit; it reads the document at runtime.

**Opened:** `client/src/CcpClient.Desktop/Haptics/IHapticSink.cs`, for the `:209-223` paragraph and
its stale "THIRTEEN" ONLY — corrected to what is true once the limb lands. No member changes.

**The fallback is REFUSED by the author, and I agree.** Below-the-pin fields plus post-construction
attachment would contort the product to satisfy a guard, and leaving *"Nothing in this build sends
anything to this sink"* in place once it is false is the stale-sentence class three waves have
already been spent on.

### B — GRANTED, narrowly. `Lifecycle/CompositionRoot.cs` is open for the wiring only.

Hoist the haptic participant's construction above `session`, pass its limb into the session, keep it
registered LAST so teardown order is unchanged. **Nothing else in `Lifecycle/**`.**

The refusal of the `HapticSinkFactory.Create()` workaround is explicitly endorsed: a second sink is
harmless only while `AdmittedRoutes` is empty and becomes two live clients against one server the day
a route is admitted — a latent defect that would have shipped looking like wiring. A limb landing
complete and unreachable was named as equally wrong.

### C — OVERRULED IN MY FAVOUR. Site 4 is NOT wired.

The author verified `Models/HapticSettings.cs:373` (`_luminanceSyncEnabled = false`) and the
*"Off by default until the Phase E UI exposes it"* doc, and ruled: **parity means matching the
shipped default, not the available feature.** Wiring it ON is a divergence upward on the one
capability where louder-than-expected is the failure that matters; wiring it behind an always-false
constant ships a Rec.601 sampler the product never runs, which is dead code with a doc comment.
**Record the divergence.** The `Layer`-plus-auto-zero verb is still built, because the video
background layer uses it and ships enabled.

### Three standing notes carried into the build

1. **The priority-0 bounce-with-bounce overlap goes in the record beside the flash-with-subliminal
   pair.** It is a second, same-kind reachable instance of peak-of-sum that the packet did not name,
   and it strengthens the C+ case rather than merely supporting it.
2. **The `ActuatorIndex 0` gap goes in the LEDGER, not in the code.** `IHapticSink.cs` stays
   byte-identical apart from item A's paragraph. Closing it is a later packet's.
3. **The zero-on-both-teardown-paths decision is the one to hold hardest.** It is what makes this
   port's teardown BETTER than the upstream it ports, because upstream's own stop is unreachable from
   the panic key. It must be proven on both paths and stated in the divergence as a deliberate
   improvement rather than a copy.
