# SP-051 — ChaosSfx cue→fallback-chain audit + typed resolution

Board row: `client/docs/task-board.md` P1 "Audit the WPF ChaosSfx cue→fallback-chain map against greenfield sfx resolution" (OPEN, filed 2026-08-05). Invariant: user-observable hearing parity (owner decree 2026-08-04). Precedent: SP-049 `boon_pick` → `chime2.mp3`, page-supplied scale kept, test-pinned.

WPF sources are READ-ONLY behavioral evidence. Greenfield sfx content = the DTRH payload pool
`ConditioningControlPanel/Resources/web/dtrh/assets/bubbles/sfx/` (8 files: Burst.mp3, GG.mp3,
Pop.mp3, Pop2.mp3, Pop3.mp3, chime1.mp3, chime2.mp3, chime3.mp3). The WPF chaos sound library
(`ConditioningControlPanel/Resources/sounds/`, incl. `chaos/` with 66 files) is WPF-tree content —
a future content row, never copied ad hoc into this slice.

> **USER-OBSERVABLE BEHAVIOR CHANGE (for the land-time board reconciliation):**
> `wave_clear` and `ripple_cast` were previously resolved off-chain in the greenfield
> (chime1 / Pop2 stand-ins — not members of those WPF chains). Per the audit binary +
> pre-approach consult ruling, both are now typed named gaps: **the greenfield goes SILENT
> on `wave_clear` / `ripple_cast` / `ticktock` (and all generic chaos page cues) until the
> WPF chaos sound-library content row lands.** WPF today plays `lvup.mp3` / `ripple_cast.mp3`
> / `ticktock.mp3` etc. Suggested board-row evidence sentence: "Audit complete (SP-051):
> 3 chains resolve per WPF (boon_reveal_rare→chime1, boon_reveal_common→Pop2,
> boon_pick→chime2); every other chaos cue is a typed named content gap pending the WPF
> chaos sound-library content row (wave_clear/ripple_cast lose their off-chain stand-ins —
> silent until the content row)." **Follow-up row to file:** port the WPF chaos sound
> library (`Resources/sounds/chaos/` + `lvup.mp3` + `chime1-3.mp3`, 66+ files) into the
> greenfield content — closes every named gap in this audit at once.

## Step 1 — the complete cue→chain map

### Tier A — fixed fallback chains (`Services/Chaos/ChaosSfx.cs`)

| # | Cue | WPF chain (override → fallback) | Scale | Cite | WPF library outcome (no mod) | Greenfield pool | Classification |
|---|-----|--------------------------------|-------|------|------------------------------|-----------------|----------------|
| A1 | `wave_clear` | `chaos/wave_clear.mp3` → `lvup.mp3` | 0.8 fixed (helper ignores page scale, `DtrhHostService.cs:260`) | `ChaosSfx.cs:22` | `chaos/wave_clear.mp3` ABSENT → **`lvup.mp3` HEARD** | neither member present | **NAMED GAP** |
| A2 | `boon_reveal` (rare) | `chaos/dling.mp3` → `chime1.mp3` | 0.6 fixed | `ChaosSfx.cs:25-30` | `dling.mp3` present → HEARD | `chime1.mp3` present | **RESOLVES → `chime1.mp3` @0.6** |
| A3 | `boon_reveal` (common) | `chaos/thud.mp3` → `bubbles/Pop2.mp3` | 0.65 fixed | `ChaosSfx.cs:25-30` | `thud.mp3` present → HEARD | `Pop2.mp3` present | **RESOLVES → `Pop2.mp3` @0.65** |
| A4 | `boon_pick` | `chaos/boon_pick.mp3` → `chime2.mp3` | 0.7 helper (`ChaosSfx.cs:33`); DTRH page path rides the generic chain @ page scale (`DtrhHostService.cs:262` → `ChaosSfx.cs:47`) | `ChaosSfx.cs:33` | `boon_pick.mp3` present → HEARD | `chime2.mp3` present | **RESOLVES → `chime2.mp3`, page scale kept (SP-049)** |
| A5 | `ticktock` | `chaos/ticktock.mp3` (single member) | 0.45 helper (`ChaosSfx.cs:37`); page path generic @ page scale | `ChaosSfx.cs:37` | `ticktock.mp3` present → HEARD | absent | **NAMED GAP** |
| A6 | `ripple_cast` | `chaos/ripple_cast.mp3` → `chaos/snap.mp3` | 0.6 fixed (helper ignores page scale, `DtrhHostService.cs:261`) | `ChaosSfx.cs:41` | `ripple_cast.mp3` present → HEARD | neither member present | **NAMED GAP** |

Resolution mechanics (all chains): first candidate that exists on disk wins
(`ChaosSfx.cs:62-79` `PlayFirstAvailable`); absent-everything = silent no-op, logged
(`ChaosSfx.cs:75-77`); volume = `master × scale` clamped 0..1 (`ChaosSfx.cs:96-103`).
Candidates resolve through `ModResourceResolver.ResolveAudioPath` — an ACTIVE MOD can
override any chain member before the fallback order runs (pre-completion consult note);
the greenfield analogue is `DtrhNativeEffectsOptions.SfxRoots` overlay-first ordering
(`DtrhHostWindow.axaml.cs:304`).

### Tier B — page-sent cues riding the generic chain

Generic chain: `chaos/{name}.mp3`, single member, page-supplied scale (default 0.6) —
`ChaosSfx.cs:47` (`Play(name, scale)`). Wire: `DtrhHostService.cs:262` (DTRH page),
`LoomHostService.cs:124` (Loom page). Greenfield wire: `DtrhFxRouter.cs:28` →
`DtrhNativeEffects.PlaySfx`.

39 literal page-sent names (grep of `sfx('<name>'` over `Resources/web/dtrh/**/*.js`) +
`unlock_card` (`warren.js:246` dynamic `data.cue || 'unlock_card'`):

`boon_pick`(A4), `collar_save`, `countdown_tick`, `defuse_hiss`, `depth_change`,
`detonate_thud`†, `dive`†, `dvd_launch`, `estim_zap`, `fall_in`, `focus_empty`,
`freeze_catch`, `freeze_shatter`, `freeze_trigger`, `fx_drain`, `glass_shatter`,
`golden_pop`, `heartbeat`, `rabbit_spawn`, `resist_absorb`, `reveal_chime`,
`ripple_cast`(A6), `sin_accept`, `sink`, `streak_milestone`, `surface`, `ticktock`(A5),
`time_slow_in`, `time_slow_out`, `toy_denied`, `toy_ready`, `trigger`, `tunnel_zone`,
`ui_click`, `ui_deepen`, `ui_denied`, `ui_unlock`, `vibe_buzz`, `wave_clear`(A1)

(† `detonate_thud`, `dive`: absent even from the WPF chaos library — **silent in WPF too**;
greenfield matches WPF by silence, still recorded as named gaps.)

**Classification: every Tier B cue is a NAMED GAP in the greenfield** — none of the names
exist in the payload pool (flat `{name}.mp3` lookup against the 8 pool files). WPF hears
all of them from `Resources/sounds/chaos/{name}.mp3` except the two † WPF-silent cues.

### Tier C — WPF-native-only riders of the generic chain (no greenfield wire path today)

Literal call sites (WPF tree, `CCP.*` first-attempt excluded): `cards_in`
(`ChaosOverlayWindow.xaml.cs:219,960`), `chip_pop` (`:797`), `count_tick`
(`ChaosHubWindow.xaml.cs:321`, `ChaosOverlayWindow.xaml.cs:761`), `dvd_bounce`
(`ChaosDvdOverlay.cs:317,331,367`), `dvd_launch` (`ChaosDvdOverlay.cs:383`,
`ChaosModeService.cs:2654`), `ui_click` (`ChaosHubWindow.xaml.cs:40`,
`ChaosOverlayWindow.xaml.cs:1008`), `ui_deepen` (`ChaosHubWindow.xaml.cs:859`),
`ui_denied` (`ChaosHubWindow.Bench.cs:277`, `ChaosHubWindow.xaml.cs:520,842,861`),
`ui_equip`/`ui_unequip` (`ChaosHubWindow.xaml.cs:528,827,1027,1072,1104,1136`),
`ui_unlock` (`ChaosHubWindow.Bench.cs:289`, `ChaosModeService.cs:1613`), `reveal_chime`
(`ChaosHubWindow.Reveals.cs:211,215`), `fall_in` (`ChaosModeService.cs:342`),
`toy_ready` (`:987,2681,2694,2939`), `sin_accept` (`:1086,1569`),
`resist_absorb`/`resist_crumble` (`:1348,1381,1787,2100` — ternary),
`trigger` (`:1355,2134`; `glass_shatter`-or-`trigger` ternary `:1374`),
`toy_denied` (`:1401,2522,2613,2617,2638`), `golden_pop` (`:1799,1814`),
`rabbit_spawn` (`:1893,2836`), `focus_empty` (`:1951`),
`time_slow_in`/`time_slow_out` (`:2343,2390`), `vibe_buzz` (`:2631`),
`freeze_trigger` (`:2640,2669`), `freeze_shatter` (`:2987`),
`streak_milestone` (`:3007`), `depth_change` (`:3037`),
`surface` (`ChaosOverlayWindow.xaml.cs:580`), `pb_fanfare` (`:584`),
`rank_settle` (`:717`), `sin_reveal` (`:282`),
`sink`/`countdown_tick` (`:176` — ternary),
`collar_save` (`ChaosModeService.cs:2113`), `estim_zap` (`:2714`),
`tunnel_ambient` (`ChaosTunnelService.cs:357` via `ResolvePath`),
`defuse_hiss` (`BubbleService.cs:1349` via `ResolvePath`),
`shield_thunk`→`toy_denied` (`BubbleService.cs:4108-4109` via `ResolvePath`).

Dynamic call sites: payload stingers `fx_drain`/`fx_freeze`/`fx_rain_start`
(`ChaosModeService.cs:2255-2263` `PlayPayloadStinger`), tunnel page cues
(`ChaosTunnelService.cs:290` page-driven name), unlock-card cues
(`ChaosUnlockCardOverlay.cs:248` `CueFor(d)`), chaos bubble-outcome cues
(`BubbleService.cs:1829` `PlayChaosCue` → `ResolvePath(name)`).

**Classification: no greenfield consumer exists for these call sites (future chaos feature
rows). All are named content gaps on the same generic chain; the typed generic-gap
mechanism covers any of them the moment a consumer sends the name.**

### Tier C-bis — consumer-side chains assembled at WPF call sites (audible outcomes)

Not ChaosSfx-internal chains, but user-observable hearing behavior the future chaos
feature rows must carry (pre-completion consult addition):

- `BubbleService.cs:1829-1833` `PlayChaosCue(name)`: `ResolvePath(name)` empty →
  **`PlayPopSound(false)` — an AUDIBLE ambient-pop fallback, not silence.** The greenfield
  gap outcome for these cues is silence; when the bubble feature row lands, its consumer
  must reproduce the pop fallback or the row records the deviation.
- `BubbleService.cs:4108-4109`: `shield_thunk` → `toy_denied` two-step `ResolvePath` chain.
- `BubbleService.cs:1349`: `defuse_hiss` presence gate (`ResolvePath(...).Length > 0`)
  switches a defuse outcome sound on/off.
- `ChaosModeService.cs:1374`: `glass_shatter`-or-`trigger` presence-selected ternary.

### Tier D — WPF library content with no identifiable call site (content ahead of code)

`capstone_reached`, `chain_pop`, `fx_text`, `menu_theme`, `pocket_sewn`, `rabbit_catch`,
`rank_up`, `tally_tick`, `tunnel_exit`, `tunnel_fall`, `tunnel_powerup_collect`,
`tunnel_powerup_spawn` (likely tunnel-page dynamic names, `ChaosTunnelService.cs:290` — no
static C# reference). Recorded for the future content row; no chain beyond the generic one.

### `ResolvePath(name)` (`ChaosSfx.cs:51-59`)

Same single-member chain (`chaos/{name}.mp3`), returns a path for the POOLED bubble player
or `""` (absent). Consumers: `BubbleService.cs:1349,1829,4108-4109`,
`ChaosTunnelService.cs:357`. Greenfield: no pooled-bubble consumer yet — recorded; the
audit covers its content rule (same named-gap classification).

## Step 1 — gap classification summary

- **RESOLVES per the chain (3):** `boon_reveal` rare → `chime1.mp3` @0.6 fixed;
  `boon_reveal` common → `Pop2.mp3` @0.65 fixed; `boon_pick` → `chime2.mp3` @ page scale.
- **NAMED GAPS (fixed chains, 3):** `wave_clear` (WPF hears `lvup.mp3`), `ripple_cast`
  (WPF hears `ripple_cast.mp3`), `ticktock` (WPF hears `ticktock.mp3`). All three are
  WPF-sound-library content — a future content row.
- **NAMED GAPS (generic page cues, 36):** every Tier B name except the three folded into
  Tier A specials. `detonate_thud`/`dive` are silent in WPF too.
- **Pre-audit drift removed:** the greenfield previously substituted `chime1.mp3` for the
  `wave_clear` chain and `Pop2.mp3` for the `ripple_cast` chain — neither file is a member
  of those WPF chains. Per the audit framing (resolve per the chain OR named gap, never an
  off-chain substitution), both substitutions are removed and reclassified as typed named
  gaps. The `boon_pick` chain (SP-049) is untouched.

## Step 1 — resolution design (post-consult, advisor-adjusted)

`DtrhNativeEffects` gains a static audited-chain table (cue token → candidate
basenames, fixed-scale-or-page-scale, WPF chain string, `File.cs:line` cite) covering
A1-A6 — including `boon_reveal_rare`/`boon_reveal_common` rows (WPF fires them
overlay-side at `ChaosOverlayWindow.xaml.cs:282-283,324`; no page wire today, so they
are table rows resolvable through the shared resolution entry point, NOT invented
wire-token special-cases). The page's own reveal cue `reveal_chime` stays its own
generic-chain cue (named gap) — never mapped onto the boon_reveal chain (off-chain
substitution). `PlaySfx` is a thin wrapper over the shared resolve: table lookup,
then the generic chain (`{name}.mp3`, page scale). Token matching is ordinal-exact
(WPF `DtrhHostService.cs:260-261` parity); FILE matching against the pool stays
case-insensitive (Linux honest). Resolution outcome typed:
`Resolved(path, effectiveScale)` or `NamedGap(cue, wpfChain, cite)` — a gap logs
"named content gap (WPF chain …, cite — WPF sound-library content, future content row)"
and never touches the pool; an unknown unlisted cue keeps the existing silent-no-op log.
Presence+shape logging discipline unchanged (cue names are stable tokens; no user paths).

## Step 2 — typed chain resolution + tests (landed)

`DtrhNativeEffects.cs`:
- `AuditedChains` — the static ordinal-keyed table (A1-A4, A6 fixed chains + `ticktock`
  as a page-scale row so its gap log self-cites the helper chain; `boon_reveal_rare`/
  `boon_reveal_common` as table rows for future consumers, per consult ruling 2).
- `ChaosSfxChain` / `ChaosSfxResolution` — the typed chain + outcome records
  (`Resolved(path, scale)` vs `GapNote` carrying the WPF chain + cite).
- `ResolveSfxCue(name, pageScale)` — the shared resolution entry point (PlaySfx is a thin
  wrapper; tests + future consumers call it directly). Table row → fixed scale or page
  scale passthrough (`FixedScale ?? pageScale`); generic arm → `{name}.mp3` @ page scale.
  Two honest gap phrasings (pre-completion consult): an audited table row logs "named
  content gap (WPF chain …, cite — WPF sound-library content, future content row)"
  (members verified present in the WPF library); a generic-arm cue logs "named content
  gap (WPF chain chaos/{name}.mp3, ChaosSfx.cs:47 — no chain member in the payload sfx
  pool)" (its file may not exist even in the WPF library — detonate_thud/dive don't).
  Null/empty cue → plain silent no-op.
- The off-chain substitutions (wave_clear→chime1, ripple_cast→Pop2) are REMOVED
  (consult ruling 1). Prior stand-in knowledge preserved: chime1 had been chosen as the
  "rewarding-chime outcome" stand-in, Pop2 as the "dull-thud outcome" stand-in.

Tests (`DtrhNativeEffectsTests.cs`, all green — 669/33 total, floor 629/33):
- `Sfx_AuditedChains_ResolvePerChain_AndGenericResolution` — boon_reveal_rare→chime1 @0.6,
  boon_reveal_common→Pop2 @0.65 (page scale ignored), generic pop @ page scale.
- `Sfx_FixedChainGaps_TypedAndRecorded` — wave_clear/ripple_cast/ticktock: no player, gap
  log names the cue + the exact WPF chain + the cite.
- `Sfx_GenericPageCues_NamedGaps` — theory over all 36 generic page-sent cues + unlock_card:
  each a typed named gap with `chaos/{cue}.mp3` + `ChaosSfx.cs:47` cited.
- `Sfx_ChainFallsBack_ResolvesWhenDedicatedAbsent` — wave_clear→lvup.mp3 @0.8,
  boon_reveal_rare→chime1 @0.6 when the fallback member is present.
- `Sfx_DedicatedFile_WinsOverFallback` + `Sfx_BoonReveal_DedicatedFile_WinsOverFallback` —
  first-exists order per chain (wave_clear.mp3 over lvup.mp3; dling/thud over fallbacks).
- `Sfx_ResolveSfxCue_TypedOutcomes` — the typed entry point: resolved scale + path, gap
  notes, null-name plain no-op.
- `Sfx_BoonPick_ChainFallsBackToChime2_KeepingPageScale` — untouched, still green (SP-049).

The complete cue→chain table is the Step 1 section above (the audit deliverable).

## Consults

### Pre-approach solo consult (Step 1)

- Route: solo. Configured solo model on this laptop: `anthropic/claude-fable-5`
  (`~/.pi/agent/bpx-consult.json`) — the Fable 5 fallback per the pause protocol (the
  Opus 5 main route is not registered here).
- Actual answering model: **anthropic/claude-fable-5** (verified via bpx-consult.json).
- Verdict (response truncated mid-sentence at the Q3 scale-semantics coda; the
  substantive rulings were complete):
  1. **Remove the off-chain substitutions (wave_clear→chime1, ripple_cast→Pop2).** The
     packet's binary framing (resolve per the chain OR typed named gap, "never an
     unrecorded drop") forces it; an audible off-chain stand-in masks exactly the gap
     class the board row was filed to expose. Surface the removal as a user-observable
     behavior change in record + STATUS so the owner can prioritize the content row;
     preserve the prior stand-in knowledge in the record.
  2. **boon_reveal_rare/common: table rows, not invented wire tokens.** Put them in the
     audited table resolvable through the same resolution entry point tests call
     directly; do NOT special-case them in PlaySfx's live switch as if the page sends
     them. Note: page-sent `reveal_chime` is the page's reveal cue and stays a generic
     named gap — mapping it onto the boon_reveal chain would be another off-chain
     substitution.
  3. **Design approved in shape:** one table + one resolve function, PlaySfx a thin
     wrapper, ordinal-exact token matching (WPF `DtrhHostService.cs:260` parity), simple
     record outcome — no ceremony types.
- Applied: design section rewritten post-consult; behavior-change note added to STATUS.

### Pre-completion solo consult (Step 3)

- Route: solo. Actual answering model: **anthropic/claude-fable-5** (bpx-consult.json —
  the configured solo route on this laptop; Opus 5 main not registered here).
- Verdict: map COMPLETE (every ChaosSfx.cs chain member accounted for). Four findings,
  all applied:
  1. **Record the mod-resolution layer** — `ModResourceResolver.ResolveAudioPath` lets an
     active mod override any chain member pre-fallback; greenfield analogue = overlay-first
     `SfxRoots`. Added to the map's resolution mechanics.
  2. **Consumer-side chains were under-recorded** — `BubbleService.PlayChaosCue`'s
     `ResolvePath`-miss → `PlayPopSound(false)` is an AUDIBLE ambient-pop fallback (not
     silence), plus shield_thunk→toy_denied, the defuse_hiss presence gate, and the
     glass_shatter/trigger ternary. Added as Tier C-bis with the future-row obligation.
  3. **Overclaim in the generic gap log (fixed in code)** — claiming "WPF sound-library
     content" for every unresolved cue was false for detonate_thud/dive/arbitrary tokens.
     `ResolveSfxCue` now emits two phrasings: table rows cite the verified WPF-library
     content row; the generic arm claims only "no chain member in the payload sfx pool".
  4. **ticktock under-cited at runtime** — promoted to a page-scale table row so its gap
     log self-cites `ChaosSfx.cs:37; page path ChaosSfx.cs:47`. No behavior change
     (single-member chain, page scale — identical to the generic arm).
  Plus a land-time ambiguity risk: the wave_clear/ripple_cast silence must be unmissable
  at board reconciliation — addressed with the banner + suggested board-paste sentence +
  the named follow-up content row at the top of this record.
- Scale-semantics check (verdict): fixed vs page scale per WPF routing confirmed correct —
  helper-routed cues (wave_clear/ripple_cast via DtrhHostService.cs:260-261) ignore the
  page scale in WPF and in the greenfield; boon_pick's union chain + page scale is the
  packet-blessed SP-049 precedent; volume = master × scale clamped 0..1 both sides.

## Engine-review presence (T-2)

- Step 1 plan review: ABSENT (in-worker reviewer spawn blocked, SP-195; engine runs reviews after .DONE — artifact `.reviews/1-20260805T085456.md`)
- Step 2 plan review: ABSENT (in-worker reviewer spawn blocked, SP-195; engine runs reviews after .DONE)
- Step 3 plan review: ABSENT (in-worker reviewer spawn blocked, SP-195; engine runs reviews after .DONE)

## Durable-lesson candidates

- **Gap logs must claim only what was verified.** A template that asserts "WPF-library
  content, future content row" for every unresolved cue overclaims the moment a cue is
  absent from the WPF library too (detonate_thud/dive). Two phrasings: verified-content
  gaps vs absence-only gaps. (port-lessons candidate: honesty in typed-gap messaging.)
- **Off-chain "close enough" substitutions hide parity gaps.** The pre-audit chime1/Pop2
  stand-ins made wave_clear/ripple_cast audible while masking that the WPF chain content
  was never ported — the exact gap class the board row exists to expose. Chain-or-named-
  gap, never a silent third option.
- **Consumer-side fallback chains live outside the resolver.** WPF hearing outcomes are
  also assembled at call sites (PlayChaosCue → ambient-pop fallback; presence-gated
  ternaries). A resolver-level audit must enumerate call-site chains too or the map
  under-reports what users hear.
