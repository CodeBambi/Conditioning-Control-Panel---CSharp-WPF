# The Arcademy (web) — architecture + gotchas

The T2 mini-game hub. One WebView2 page served from `https://ccp.game/arcademy/index.html`
by `ArcademyHostService`; vanilla ES modules, **no build step, no framework, no bundler**.
Design law lives in `planning/arcademy/` — read `BUILD-CONTRACT.md` → `GROUND-RULES.md` →
`SYNTHESIS-NOTES.md` → `DECISIONS.md` (earlier wins) before changing behaviour here.

This file is the *implementation* companion: what the pieces are, where the traps are.

---

## 1. What owns what

| Concern | Owner | Never |
|---|---|---|
| the day's 3 classes | `core/timetable.js` (pure) | a game, a store, a clock |
| S/A/B/C + caps | `core/grades.js` (pure) | a game grading itself |
| grade tier (Year 1–4) | `games/registry.js` + meta store | a game choosing its tier |
| XP numbers | **C#** (`payout-result`) | any page-side XP table |
| "already paid today" | **C#** (`ArcademyMetaStore.XpPaidKey`) | the page deciding a retake is free |
| settings values | **C#** (`set-setting` → `setting` echo) | the page assuming its own clamp |
| screens, ctx, lifecycle | `shell/shell.js` | a game calling `bridge` directly |
| effects | `engine/` (parallel agent) | a game exceeding `effectsConsumed` |
| media | `provider/` (parallel agent) | a game fetching anything itself |
| sound | `shell/audio.js` | the engine, a game, or C# owning an Audio node |

Screen router: **split-flap board → class → report card** (+ settings, which is a screen).
`boot.js` owns the bridge handshake and the Esc ladder's outer rungs; `shell.js` owns the
inner rungs.

**Two ladders, and they are different.** The ESC ladder is the page's (tap walks
settings → pause → leave class, then unfullscreen; hold exits). The PANIC ladder is the
HOST's: `MainWindow` hands the panic key to `ArcademyHostService.HandlePanicPress()` while
the window is up, press 1 posts `suspend {on:true, reason:'panic'}` and press 2 within 2s
calls `CloseActive()`. See trap 29 - without that hand-off two panic taps exited the whole
app from inside a class.

## 2. Files

```
index.html   one document; ids are the shell's only DOM contract (see boot.js dom{})
styles.css   shell chrome ONLY. Tokens ported from planning/arcademy/mockups/.
boot.js      handshake, heartbeat, boot deadline, Esc ladder, host-frame routing
bridge.js    postMessage seam: queue-until-init out, pre-buffer + multi-subscriber in
core/lexicon.js    t(key, fallback) over init.lexicon  (mod display strings ONLY)
core/timetable.js  §7 seeded generator (PURE)
core/grades.js     §8 rubric + A-caps (PURE)
core/store.js      meta-command client, local cache + write-through
core/rng.js        makeRng/hash01            <- NOT ours (engine agent)
core/caps.js       clampToCaps + heat curve  <- NOT ours (engine agent)
shell/shell.js     screen router + THE class runner (ctx per §11)
shell/splitflap.js departure-board reveal
shell/reportcard.js day summary + THE one share pipeline
shell/settings.js  THE settings page (3 tiers) + SETTING_KEYS
shell/ceremonies.js stamp / 10-segment meter / reward beats (engine-delegated)
shell/peek.js      the shared hold-to-reveal verb (caps the class at A)
shell/keybinds.js  manifest-declared verb slots, one blob, PanicKey conflict check
shell/audio.js     THE consumer of engine 'arcademy-sfx' (WebAudio, procedural)
games/registry.js  guarded allSettled registry + tier math + class_suspended stub
                   + GAME_SEMESTER / OPEN_SEMESTERS (the release gate: a CLOSED semester's
                   games are ABSENT from the pool, never stubs; isOpenSemester())
                   GAME_META here is the PARACHUTE: it must mirror each module's
                   own family/meaty/flagship/timeBudgetSec, because the timetable
                   reads a suspended class's descriptor too
games/<key>/index.js  one folder per game; games NEVER import each other
  daily-trigger/   the daily word (homeroom, flagship)  - bank/board/ladder/words-*
  lost-and-found/  the mosaic hunt (MEATY, flagship)    - board/grade/hud/util
  deja-vu/         the pair memory                      - script (the swap plan)
  impulse-control/ the Drop Tube (pop/withhold)         - lex/schedule/scoring/render/style/tube3d/tube2d
                   (seeded three.js chute, vendored r185 in ../vendor/; tube2d = no-WebGL ladder)
                   + the House Rules decks casino (THE FLOOR: bulb-ring marquee, chime ladder,
                   gate, near-miss staging, jackpot ladder + royal) / pressure (THE SURGE: the
                   STREAK-driven CCP effects ladder + tube/HUD tremor, never the basin) /
                   trickster (the Tell, the crooked ring, ghost cursor, stat flicker);
                   THE LANDING: tube3d projects the middle of its VISIBLE hole into
                   `--ic-basin-x/-y` on `.g-ic` (render.js onLanding) and the basin /
                   ring / flourish / stamp hang off those; THE DUSK (`.g-ic-dusk`, z2 over
                   the tube, render.js) is pressure.js's rung-driven dimmer + FLARE;
                   render owns the drawn class-rules sheet + the lit HUD + the ticket debrief;
                   every deck injects its OWN <style id="g-ic-<deck>-style">; render's only
                   audio node is the grandfathered denied.mp3 sting - every other cue is engine
                   audio_trigger (pitch = the streak)
  the-deep-end/    2048 with trance-depth tiers (MEATY) - board/schedule/grade/lex/style/casino/trickster/pressure
                   the deepest tile is the heat dial; board/schedule/grade pure, casino+trickster+pressure decks
                   (pressure = the rung-by-rung CCP effects ladder + the Balatro board tremor/HUD juice)
  -- Semesters II + III (2026-08-23; every class below ships ALL the House Rules decks from day one:
     style (the look + the drawn class-rules sheet) / casino (THE FLOOR) / trickster (schedule-dealt
     cards, budget 2/4/6/8 by tier) / pressure (THE SURGE, the CCP-effects ladder on the game's own
     streak) + a lex.js <P>_LEX table; decks are dynamic-imported + null-safe so a broken deck never
     takes the class down) --
  misdirection/    the shell game (tracking, 120s)        - shuffle (PURE seeded plan + verifyRound = the
                   TRACKABILITY INVARIANT: occlusion hides at most ONE link of a swap chain and every
                   occlusion carries a tell) / grade / lex MD_LEX; keybinds pick1..pick5; md_stake_mode
                   ask|bank|ride (greed scored UPWARD only, ride cap 5), md_shell_skin themed|minimal|contrast
  echo/            the Simon ring (memory, 105s)          - sequence (PURE: warm start 3..6 off bestLen, decoy
                   plan from tier 2 telegraphed) / grade / lex EC_LEX; keybinds pad1..pad6; six pads always live,
                   the TIER restricts the alphabet; tones = engine audio_trigger 'pad' x pitch (+1 semitone per
                   link, cap 7); a fail is NOT the class (new sequences until the bell); Encore once, auto
  instant-recall/  the vigil (recall, 120s, MEATY)        - vigil (PURE seeded script: stops w/ FINAL-STOP
                   GUARANTEE in the last 15s, layouts rows/mosaic/swirl, density sawtooth, plants, templates
                   LAST_WORD/EFFECT/STING/TWO + MODE tier 4 <=10%) / montage (the stage + the L&F live-window
                   discipline + createLedger = the TRUTH tail, aria-hidden) / grade / lex IR_LEX; ir_density
  anomaly/         the odd-one-out grid (search, 90s)     - rounds (PURE: kinds/deltas at PERCEPTIBLE floors,
                   relocations cap 2/round, drift) / grade / lex AN_LEX; the odd index lives in CLOSURE ONLY -
                   never a DOM attr/class (suite asserts it); decks get a canMelt(i)/meltCandidates() oracle
                   and nothing else; an_kinds all|gentle
  composure/       the sliding picture (puzzle, 120s, MEATY) - board (PURE, seeded SOLVABLE scramble w/ parity)
                   / solver (PURE baseline: optimal 3x3 IDA*, 4x4/5x5 BFS over tracked-tiles+gap - the greedy
                   textbook solver deadlocked 1 board in 5) / grade (par from the solver) / lex CP_LEX;
                   manifest.peek TRUE (the shell's hold-to-reveal = A-cap); cp_mode timed|zen (zen ends
                   {zen:true} = 'pass'), cp_zen_grid; skill-floor rescue after 20s (solver hint + sGate false);
                   locks are MARKERS never freezes (a frozen tile can make a board unsolvable)
```

Each game owns its own lexicon rows; **`ArcademyHostService.NeutralLexicon` mirrors every
one of them** (672 rows as of Semesters II+III, 2026-08-23 - the count is a
floor, never a contract: a scratch script diffs every `t('key'` / lexicon table against the C#
table, see §7) or the shell renders raw keys for the settings
page's `label_key` / `hint_key`. Impulse Control exports its table as data
(`impulse-control/lex.js` `IC_LEX`) - copy the values, do not re-word them.

## 3. Cross-agent seams — change these only with the other side

- **`shell/settings.js` → `SETTING_KEYS`** is the *complete* list of keys the page writes.
  They are **protocol names, not C# property names**: the init projection's own camelCase
  fields, flattened (`masterIntensity`, `caps.flashRate`, `audioLevels.fx`, `audioMute`,
  `hideTutorial`, `effectIntensity`, `keybinds`). `ArcademyHostService.ApplySetting` maps
  them onto AppSettings and re-clamps every one; `effectIntensity` deliberately lands on the
  existing app-wide `ChaosEffectIntensity` rather than minting a duplicate guard.
  - Anything the host does *not* recognise is bagged as a **per-game** knob under the key
    verbatim (no prefix) in `ArcademySettingsJson`. That is why `GLOBAL_RESERVED` in
    `settings.js` is load-bearing: a manifest declaring `flashRate` would otherwise write
    the global ceiling. `isGlobalSettingKey()` is the same fence on the echo path.
  - `keybinds` is sent as an **object**, not a JSON string - the host tests
    `value is JObject`. A non-object is now REFUSED and the reply carries the value that is
    still STORED (it used to store `""`, i.e. one malformed frame wiped every rebind the
    player had made). The blob is also capped at 7000 chars, deliberately below
    `AppSettings.ArcademyKeybindsJson`'s own 8192 wipe cap.
- **Attendance is HOST-owned.** `ArcademyMetaStore` mints `streak`,
  `perfectAttendance`, `lastAttendanceLocalDate` and `todayClasses` from the `class-ended`
  frame (so a stale page cannot forge a streak) and **refuses** a page write to any of them.
  `core/store.js` lists them in `HOST_OWNED_KEYS`, drops such writes locally, and reads the
  numbers back from two places: `payout-result` (which carries `streak` /
  `perfectAttendance` / `classesToday` on the same frame) and the whole-blob snapshot.
  The page still owns `days` (the graded view) and `games` (tier + per-game state).
- **`punchCards` is HOST-owned too** (PUNCHCARD.md §2). `ArcademyPunchCards` is the pure math,
  `ArcademyMetaStore.StampPunchCard` / `EnrollPunchCard` the mints; the key is refused to the page
  and every date is LOCAL. One card per game key:
  `{punches:0..10, dates:["yyyy-MM-dd"], enrolledAt:string|null, house:bool, complete:bool,
  unlockedAt:string|null}`, with `punches` recomputed on every touch so a bad blob self-heals.
  - The **daily stamp rides the attendance credit** on `class-ended`, which makes "any graded
    finish stamps, once a local day" true for free: Esc-leave sends `class-left`, and a Free Swim
    never sends `class-ended` at all (`shell.js finishClass` returns first).
  - The page posts **`enrollment-done {gameKey}`** once, after the enrollment ceremony. It mints
    the two first-run punches and **supersedes that day's daily stamp** (which has already landed,
    the ceremony running after `class-ended`) - day one is exactly 2, never 3, in either ordering.
    Repeat frames are no-ops.
  - The host answers both paths with **`punchcard-result {gameKey, reason:'daily'|'enrollment',
    minted, justUnlocked, holes, card}`** - same-frame truth for the ceremony, the way
    `payout-result` carries the streak. The whole-blob `meta` snapshot is pushed as well.
  - `complete:true` IS the permanent unlock: the shell offers Begin on that room every night
    through the same door path as `devDoor`. Nothing host-side gates which room may start.
- **`meta` arrives in TWO shapes** — `{key, value}` (the reply to a meta-command) and
  `{rev, state}` (the snapshot the host pushes after crediting attendance). Handle both; a
  handler that requires `key` silently drops the authoritative streak.
- **Board size** is a per-game setting under a derived key, `<gameKey>_board_size`
  (`shell/settings.js` `boardSizeKey()`), also surfaced to games as `ctx.settings.boardSize`.
- **`shell/audio.js` accepts an optional `pitch`** on the `arcademy-sfx` detail (0.5-2,
  default 1). It multiplies every frequency in the recipe - oscillator sweep, arpeggio step,
  noise band, stamp thunk - and deliberately NOT the duration, so a pitch ratchet climbs
  instead of speeding up. Anything unusable clamps to 1, so an emitter that never sends the
  field sounds exactly as before.
- **Protocol** (`bridge.PROTOCOL = 1`) must match the host's `PROTOCOL` int. A mismatch
  fails the boot on purpose — a page mis-reading the projection would mis-clamp settings.
- **`engine/index.js` `createEngine(opts)` / `provider/index.js` `createAssets(opts)`** are
  loaded *optionally* (intake's `loadOptional`). Missing or throwing → null object, and the
  class still runs, silent. Never make either a hard import.

## 4. Traps (each one cost real time)

1. **Only the echo moves a setting.** Every control posts `set-setting` and paints
   `pending` until the host echoes `setting`. Never write the model on `input`/`change`;
   the host's clamp is the truth. (Tested: host clamps 0.4 → 0.25, the slider lands on 0.25.)
2. **No remote fonts, ever.** The webview is offline. The mockup's Graduate/Sora/IBM Plex
   are gone — `--disp/--body/--mono` are system stacks. Adding a `fonts.googleapis.com`
   link silently falls back and wastes a boot on a DNS timeout.
3. **`.brow` textContent has no spaces.** Split-flap rows render one element per character
   and spaces as empty `.fl.gap` nodes, so `textContent` is `DAILYTRIGGER`. Any test or
   scraper matching `'DAILY TRIGGER'` fails for the wrong reason.
4. **The reveal is CSS-only.** `.board.play` + `--r`/`--i` custom properties drive one
   keyframe; re-flip is remove-class → force reflow (`void root.offsetWidth`) → add-class.
   Without the reflow read the browser coalesces it and nothing animates. Repaints that
   are *not* a reveal must pass `animate: false` or the board re-flaps on every meta echo.
5. **A small pool cannot satisfy no-repeat-3.** (Written for the 4-game pool; the five-game
   pool relaxes on every dealt day too.) 3-4 rotating games into 2 slots means the
   generator relaxes on most days. Relaxation order is law (flagship → meaty → family →
   no-repeat) and no-repeat *narrows* (3→2→1→off) instead of dying, so the board still
   refuses yesterday's class. Every constraint is also a seeded weight so the preference
   survives relaxation. `day.relaxed` and `day.noRepeatWindow` report what happened.
6. **A pool needs FOUR meaty games to deal one meaty class EVERY night.** no-repeat outranks
   meaty. With the five-game pool no-repeat-3 was unsatisfiable and relaxed first, so two meaty
   games (`lost_and_found`, `the_deep_end`) filled the slot nightly "for free". The TEN-game pool
   re-opened it: no-repeat-3 binds again (`noRepeatWindow === 3` every day) and two meaty classes
   cannot cover a 3-day window - measured 13/28 nights with two, 21/28 with three, **28/28 with four**
   (`scratchpad/ttcheck/check.mjs`). So `instant_recall` and `composure` are meaty too (ruled
   2026-08-23); the flag is a timetable fact, nothing in a module branches on it. A fifth meaty game
   changes nothing; dropping to three silently loses a quarter of the nights.
7. **The timetable's history is an epoch walk, not a recursion.** `EPOCH = '2026-08-01'`
   and the generator walks forward to the target date, memoised. That makes it a fixed
   point (day D-1 computed as history === day D-1 computed on its own — tested). **Moving
   EPOCH reshuffles every past day.** Don't.
8. **UTC seeds content, LOCAL date rolls attendance** (regression #978). `init.utcDateSeed`
   → timetable + per-class seeds; `init.localDate` → streak + day rows. Crossing them
   makes the streak timezone-dependent and the daily word non-global.
9. **Peek's A-cap is the shell's, not the game's.** `ctx.peek` is a shell primitive; the
   runner reads `peek.used` at `endClass` and hands `assists.peek` to the rubric. A game
   cannot opt out, and a game that implements its own peek has broken the rule.
10. **The per-class engine handle has no `suspend()`/`dispose()`.** Lifecycle is the
    shell's, so a game cannot un-suspend itself while a mandatory video plays. It is also
    allowlisted to `manifest.effectsConsumed`; an undeclared `fire`/`sustain` no-ops and
    logs **once per kind**.
11. **`bridge.on` is multi-subscriber** (type → Set), unlike `dtrh/bridge.js`'s single-slot
    Map. `core/store.js` wants `meta`, `shell/settings.js` wants `setting`, `provider/`
    wants `assets`. If you "simplify" it back to one handler per type, the last importer
    silently steals the others' frames.
12. **Perfect-attendance credit is guarded by `streak.lastPerfectDate`, not by the day
    row's `perfect` flag** — `completeDay()` sets that flag in the same breath, and the
    first version of this raced itself into never crediting a perfect day.
13. **The share header is the literal string `'The Arcademy'`**, never `t('arcademy')`. A
    mod-skinned header would out the player's mod in a Discord paste. v1 renders **only**
    Daily Trigger's emoji grid; other payloads are ignored with one log per session.
14. **The engine emits its CustomEvents on `document`** (and additionally on `opts.bus` if
    you pass one). The shell passes `bus: null` on purpose — passing `window` double-logs
    every `arcademy-log` line.
15. **A dead lexicon degrades to English, never to raw keys** (`core/lexicon.js` falls
    back caller → defaults → de-snaked key). Same lesson as the app's `en.json` Fatal path.
16. **The local media manifest is called `localAssets`.** `ArcademyHostService.BuildSettingsBag`
    hangs `{gifs:[...], stills:[...]}` of absolute `https://ccp.assets/...` urls off
    `init.settings.localAssets`; `provider/index.js` `MANIFEST_KEYS` lists that name FIRST and
    `shell.js` passes `settings: src.settings` straight through. Rename either end and every
    draw silently falls back to the six bundled placeholder tiles - which looks like art, not
    like a bug. `shell.assetStats().placeholderFloor === true` is the tell.
17. **`bridge.send` takes ONE object.** It drops anything without a string `msg.type`, so
    `send('assets-request', payload)` posts nothing at all and the host is never asked for
    media. `provider/remote.js` flattens to `send({type, ...payload})` for exactly that reason;
    the loose `bridge` shapes in its header are for other hosts, not a signature we may pick.
18. **`shell/audio.js` is the only thing that may hold an audio node.** It listens for
    `arcademy-sfx` on `document`, SYNTHESISES every cue (there are no sfx files in the build)
    and multiplies sfx level x group level x `masterVolume` x `!audioMute`. Three consequences:
    no `AudioContext` is created until the first pointer/key gesture (autoplay policy - a cue
    before that is counted and dropped, never queued); levels move ONLY on the host's `setting`
    echo, same law as the settings page; and a new engine sfx name that is not in its `SOUNDS`
    table degrades to `blip` rather than going silent. `boot.js` builds it before the shell and
    exposes `audioConsumer()` for the harness.
19. **The panic key is projected TOP-LEVEL** (`init.panicKey` / `init.panicKeyEnabled`), not in
    `init.settings`, and it is a LAUNCH-TIME SNAPSHOT - `ProjectedSetting` does not echo it, so
    rebinding the app's panic key mid-class does not move the page's conflict check until the
    next launch. The page only ever refuses to bind over it; it never handles the key.
20. **`manifest.boardSizes.values[0]` is the SHELL's default.** Both `shell/settings.js`
    (`gameValue(..., bs.values[0])`) and `shell.js`'s `gameSettingsFallback` fall back to the
    FIRST entry, so the list has to be ordered with the intended default first - Lost & Found
    ships `[40, 30, 24, 20, 16, 12]`, descending, for exactly that reason. The A-cap then
    hangs off `par`, not off the list: `chosen < par[tier]` is "below par board". Two ways to
    get this wrong: put the easiest size first and every untouched install plays capped, or
    write a `par` value that is not in `values` and par can never be met.
21. **A manifest settings enum needs `values`, not `options`.** `shell/settings.js` tests
    `s.kind === 'enum' && Array.isArray(s.values)`; an `options` array falls through every
    branch and the row simply **never renders** - no warning, no fallback control, and the
    setting silently keeps its default forever. (`selectRow()` takes `options` internally,
    which is where the confusion comes from.)
22. **`fire('glitch_swap').onSwap` rides the engine's timer registry.** The midpoint callback
    is scheduled through the engine's own timers, and `suspend()` kills them (`timers.kill()`)
    while `dispose()` disposes them. A class that does its content swap *only* in `onSwap`
    loses the swap if a mandatory video lands mid-transition. Games must keep their own
    backstop - resolve the swap promise themselves on a deadline and treat `onSwap` as the
    nicety it is.
23. **XP pays ONCE per (game, UTC day); a retake is a free replay.**
    `ArcademyMetaStore.TryClaimXpDay` is the ledger (host-owned key `xpPaidDays`), and a
    repeat `class-ended` answers `payout-result {xp: 0, retake: true}` while still grading,
    stamping and sharing normally. Three consequences: the page must not compute XP from the
    grade (it never could - trap: `results[key].xp` comes only from the payout frame); the
    day's `days[date].classes[key]` row keeps the **first** grade (`shell.js` skips
    `recordClass` on a retake, so a bad second run cannot erase an S); and attendance is
    untouched, because `RecordAttendance` is idempotent per (local day, gameKey) and runs
    either way - which is what still credits a new LOCAL day that shares a UTC day.
    Board rows for a graded class stay CLICKABLE and wear a `t('retake')` chip;
    `classSpec.retake` tells the game.
24. **`ctx.absorb(word)` / `ctx.sessionWords` is SESSION-ONLY.** A class may add to the day's
    word pool and every engine built *after* that gets the longer list (Daily Trigger absorbs
    the word you solved). Nothing is persisted, nothing is posted to the host, and
    `SubliminalPool` is never written - DECISIONS #10, the ramp-never-writes precedent. Reload
    and it is gone. Validated: <= 40 chars, no control characters, no duplicates, 64 adds max.
25. **The timetable memo is keyed on the calendar's DATE KEYS, not its contents.**
    `core/timetable.js` `signature()` hashes the pool plus `Object.keys(calendar)`, so two
    *different* override calendars that name the same date share one memo entry and the second
    silently gets the first one's board. Invisible in the app (one calendar per page load);
    it will eat a test suite that boots repeatedly. `clearTimetableCache()` exists for that.
26. **A `NeutralLexicon` value longer than 96 characters can never be mod-skinned.**
    `MergeModTable` drops any mod string over `Length > 96`, so the long Impulse Control
    rows (the `ic_slip_*` lines) always render English. If a mod must re-voice one, split
    it into two rows rather than raising the cap. (`ic_tube_rules` is no longer rendered -
    the class-rules sheet is drawn - but its C# row stays; the host table is append-only.)

27. **`[hidden]` IS A USER-AGENT RULE, SO ANY AUTHOR `display:` BEATS IT.** `.arc-loader,
    .arc-nope { position:fixed; inset:0; display:flex }` meant `dom.loader.hidden = true` and
    `#arc-nope[hidden]` did *nothing*: two opaque full-page overlays sat over the live shell at
    z-index 70, the later one painting the "The Arcademy is closed" card, and every click on the
    board landed on it. The whole page was unusable and the log said nothing was wrong (playtest
    2026-08-19, shots 01/02/12/13). `styles.css` now opens with `[hidden] { display:none
    !important; }`. **Never write a bare `display:` on a node the shell toggles with the hidden
    attribute** (`#arc-loader`, `#arc-nope`, `#arc-topbar` today) without re-reading that rule, and
    never add a competing `[hidden]` rule in a game or engine stylesheet - the `tt` suite
    (`test-hostfixes.mjs`) parses styles.css and fails if either happens. dtrh/styles.css:400 has
    the same reset for the same reason; that is where the lesson came from.
28. **A suspend can arrive BEFORE the shell exists, and it is a LEVEL, not an edge.** The host
    seeds current native state immediately after `init` (`ArcademyHostService.SeedNativeState`: a
    mandatory video already playing, an `AudioOnlySession` flip that happened between the launch
    gate and the first frame), and `start()` is async - two dynamic imports - so that frame lands
    while `shell` is still null. `boot.js` buffers the LAST such frame and replays it once the
    shell is live (`bufferedSuspend()` is the test seam). Dropping it dealt a board over a running
    video. Buffering the last one and not a queue is deliberate: an on/off pair collapses to "off",
    which is the correct answer for a video that ended during boot.
29. **THE PANIC LADDER IS THE HOST'S, AND THE PAGE NEVER HANDLES THE PANIC KEY.**
    `MainWindow.xaml.cs` hands the press to `ArcademyHostService.HandlePanicPress()` while
    `IsActive` - a rung that must sit BEFORE the app-wide `_panicPressCount >= 2` branch, because
    that branch calls `Application.Current.Shutdown()`: with no rung, two Esc taps inside the
    Arcademy exited the whole app. Press 1 → `Suspend(true, "panic")`; press 2 within 2s →
    `CloseActive()`. A panic suspend has **no natural end** (a video's ends with the video, an
    audio-only one with the session), so the class_suspended treatment grows a Resume button that
    posts `{type:'resume-request', reason:'panic'}` and the HOST answers with `suspend {on:false}`.
    The host refuses that while a video or an audio-only session still owns the screen, and neither
    `OnVideoEnded` nor the audio-only watch may lift a panic suspend. Trap 19 still holds
    separately: `init.panicKey` is a launch-time snapshot the page only uses to refuse a rebind.
    COROLLARY (live-verified 3/3): one physical Esc press reaches BOTH ladders - the host suspends,
    then the page's own tap ladder fires on keyup and used to walk the suspended class to the board,
    destroying the Resume card ~60ms after it appeared. `escapeStep()` therefore consumes the press
    and does NOTHING while `active.suspendEl` is up (any suspend reason): the overlay's Resume /
    Leave class buttons are the page-side way out, the host's press-2 is the fast exit.
30. **`class-started` has a closing bracket now: `class-left`.** Leaving a class with Esc ends no
    class, so `class-ended` was never sent and the host's `_classActive` stayed true for the rest
    of the session - which kept the tighter mid-class heartbeat limit (12s vs 20s) armed and made
    every log line claim the page was still in a class. `shell.js teardownClass()` is the ONE
    funnel every leave path already went through, so the message is sent from there; the host
    handler is idempotent, and a finished class simply sends it right after `class-ended`.
31. **The C# meta blob is bounded and its save is atomic - do not "simplify" either.**
    `ArcademyMetaStore` caps one value at 32KB and the top level at 64 keys, trims `days` to the
    newest 40 rows on every write that touches it (the same `SkipLast` shape `TryClaimXpDay` uses),
    writes through a temp file + `File.Replace` (which leaves one `.bak` generation), and on load
    walks main → `.bak` → empty, SALVAGING an over-cap blob (shed `days`, then the oldest `games`
    entries, then keep the host-owned keys) and copying the original to a `.corrupt` sidecar before
    anything destructive. A bare `WriteAllText` truncates the file first, so a crash in that window
    left a half-written save that the next launch parsed, failed, and replaced with a fresh one:
    the streak, every grade and the XP ledger, gone.
32. **App exit must call `ShutdownFlush()`, never `CloseActive()`.** The graceful close posts
    `end-run` and waits on a 1200ms `DispatcherTimer` for `exit-done` - and inside `App.OnExit`
    that timer can never tick (the dispatcher is already shutting down and OnExit ends in
    TerminateProcess), so the meta flush and the WebView2 disposal it guards never ran.
    `ShutdownFlush` is the synchronous path: flush, dispose, no round trip.

33. **A jackpot's forced garnish KILLED every held spiral wash (engine, fixed 2026-08-22).**
    `ceremonies.jackpot` forces `drain|spiral`, which re-triggers the ONE wash element per kind
    with a hold; the hold's deadline used to write `opacity:0` - and took a class's
    `sustainForever` wheel with it (IC's rung-3 wheel vanished 3.8s after any jackpot; the Deep
    End's was exposed the same way). `engine/sustained.js startWash` now keeps `forever` +
    `heldAlpha` per element: a later NON-forever trigger at a HIGHER alpha is a flare that falls
    back to the held alpha; a LOWER one is the decks' whisper-out step-down and ends the hold.
    `stop('wash')` clears both. Do not "simplify" the three branches.
34. **`Vector3.project(camera)` on a camera that has never rendered projects through the
    identity.** `matrixWorldInverse` is only refreshed by a render or an explicit
    `camera.updateMatrixWorld(true)`; tube3d's THE LANDING solve ran before the first frame,
    got garbage, and silently fell back to 50%/50% (the bubble sat on the near coil again). Call
    `updateMatrixWorld(true)` + `updateProjectionMatrix()` before any build-time projection, and
    sanity-bound the result (15-85%) with an explicit fallback.
35. **"The effects play behind the tube" was never z-order - it was alpha.** Three in-app
    verdicts in a row (owner 2026-08-22). The CDP compositor shot AND a PrintWindow grab both
    showed the fixed `#arc-fx` layer on top; what the eye saw was the engine's heat-gated
    bursts (0.15-0.75 alpha, 120-270px) and `mix-blend-mode:screen` washes drowned by a
    neon WebGL chute, and neon lines bleeding THROUGH a translucent gif read as "behind".
    The engine's ceilings are law, so the fix is GAME-LOCAL and under the effects: the dusk
    (rung-driven opacity on an empty div over the tube) plus THE FLARE (snap to 0.84/0.92 under
    every gif/flash burst for its hold, ease back). Before touching z-index for a "behind"
    report, inject a solid test box into `.ae-front` and PrintWindow the app - if the box is
    on top, it is alpha.

36. **A WEB PAGE'S FRAME BUDGET IS SPENT ON THREE THINGS, AND NONE OF THEM IS "TOO MANY
    NODES."** Chromium trace of The Deep End, full screen, 16 live video tiles, RTX 3060 Ti:
    the GPU process's main thread at **79% of a core**, in three roughly equal thirds.
    - **RENDER SURFACES.** ~86 per frame. `isolation:isolate` + a `mix-blend-mode` pseudo on
      every tile face is two surfaces *per tile*, and a blend surface must read back what is
      under it before it can write. A `filter:` on a `<video>` is worse still: a full GPU pass
      over a decoded 854x480 frame, per tile, per frame. **Tint with plain alpha and bake a
      "desaturate" into the wash gradient; never put a filter on a live decode.**
    - **PER-FRAME RE-RASTER.** `@keyframes { to { background-position: … } }` re-rasters the
      WHOLE layer every frame; six full-screen sheets doing it is a third of the budget on
      gradients that never changed shape. **PATTERNS DRIFT BY TRANSFORM, NEVER BY
      BACKGROUND-POSITION** (the law is written into `the-deep-end/style.js`): oversize the
      sheet by exactly one tile period on its trailing edge and `translate` it by exactly that
      period, so the wrap lands on an identical pixel. One background layer per pseudo - two
      layers with different tile sizes cannot share one transform. Corollary: a *travelling*
      highlight needs a clipping box, and a `::before` cannot own a `::before`, so a sweep on
      a pseudo (the old `g-de-sheen`, `g-de-scan`) either grows a real element or becomes an
      `opacity` breathe. Prefer the breathe; a per-tile node to save raster is a bad trade.
    - **VIDEO DECODES.** Scrolller's SMALLEST rendition IS 854x480, so asking for a smaller
      file is not a lever - the **decoder COUNT** is the only one. Faces are frozen per TIER,
      so a cap counted in tiles is meaningless (17 tier-1 tiles = 1 file, 17 decodes). The
      Deep End caps **distinct animated tiers** (`FACE_CAP` 6) and keeps the numerous shallow
      tiers on stills (`SHALLOW_STILL_MAX_TIER` 3) - *the shallows are still, depth is alive*.
      The ENGINE shares one budget of its own: `engine/util.js` `budgetedKind('loop')` counts
      the `<video>` nodes `mediaEl` has minted and hands `gif_rain` / `gif_burst` a **still**
      once `VIDEO_BUDGET` (6; 2 under `.ae-lite`) is spent. Anything that mints a decoration
      video must come through `mediaEl` and leave through `timers.release`/`kill`, or the
      count leaks and the budget closes for the session.
    **AND A LADDER, NOT A SWITCH:** under a 4x CPU throttle even an all-stills board fell to
    40fps, so the whole frame has to get cheaper, not just the videos. `de_perf`
    (`auto|full|lite`) is the pattern: `lite` = `.g-de-lite` on the game's stage **and
    `.ae-lite` on `document.documentElement`** (the one seam a game and the engine share -
    the engine never owns that class, it only reads it), and **both come off on destroy or
    the lobby inherits a lit-down room**. `auto` samples rAF deltas for ~3s after the board is
    dealt, skips the first 500ms of first-frame cost, and demotes **once, downward only** - a
    room that changes its own look twice is worse than a room that is simply lighter. With no
    `requestAnimationFrame` (node, the DOM double) the probe must **stay full**: a missing
    frame clock is not evidence of a slow machine.

37. **A backtick inside a CSS comment inside a template-literal stylesheet kills the WHOLE
    sheet.** `` /* `[data-tier]` */ `` in a `STYLE_TEXT` template ends the literal early; the page
    dies with `ReferenceError: data is not defined` and `node --check` passes it - only a browser
    load catches it. Three agents hit it in one day (IC stage, DE howto, MD decks). Never write a
    backtick in a CSS comment in a `.js` stylesheet.
38. **`applySuspend(false)` must re-assert the pause.** The shell leaves a lifted suspend behind
    its pause card on purpose (the Resume button is the way back), but a game's `suspend(false)`
    typically restarts its own loop - Misdirection and The Deep End both played on behind the
    overlay. `applySuspend` now calls `instance.pause()` again when `active.paused` is set.
39. **The spiral pool is bundled + THE LOOM.** `shell.js pickSpiralUrl(seed, settings)` appends
    `init.settings.loomSpirals` (the host's `https://ccp.spirals/loom_<slug>.gif` list, same folder
    DTRH exposes) at weight 20 each, cap 24, validated + de-duplicated. No Loom = byte-identical
    picks. The host maps `ccp.spirals` for the Arcademy too (`ArcademyHostService` mappings).
40. **`core/rng.js makeTaggedRoll` is per-tag mulberry32 now.** The first version hashed
    `seed|tag|n` per call and the trailing counter barely avalanched through FNV-1a (~0.4%
    near-equal consecutive pairs); every deck had worked around it with its own per-tag mulberry32.
    Same contract (tags independent, replay exact), different stream - any golden value recorded
    off the old stream (none were) would move.
41. **"WebKit won't transition an unregistered custom property" is a MYTH - measure before you
    rename.** The Deep End web teaser "teleported" tiles on an iPhone 13 Pro Max (2026-08-23) and the
    first diagnosis was exactly that myth; registered `--cp-r/--cp-c` / `--md-x` twins were even
    built and then reverted. The measurement (peer session, Playwright WebKit 26.5 + Chromium): all
    four variants - unregistered var + `transition:transform`, `@property` var, transition on the
    vars themselves, both - interpolate IDENTICALLY in both engines; on the live Deep End page WebKit
    fires `transitionrun` on transform when `--r/--c` change (keyboard and touch) and a no-var
    plain-transform control traced byte-identical. What reads as "the transition never fired" on a
    phone is a 170ms transition receiving 1-3 FRAMES under load - cut per-frame cost (the
    `html.ae-touch` rung: no blur over a live face, no blend surface, no backdrop-filter, video
    budget 3 on coarse pointers) instead of renaming variables. What IS true: `@property` is
    page-global, so a var name shared with mixed types (`--r` is a number here and an ANGLE in
    misdirection/casino.js) can never be registered globally - if registration is ever genuinely
    needed, use game-prefixed names.

42. **A PHONE NEEDS THE CUTS ON *FULL*, AND THE SEAM IS `html.ae-touch`.** The owner's
    iPhone 13 Pro Max skipped frames on slide/merge and skipped harder when effects fired -
    on the LITE rung (web teaser, 2026-08-23). de_perf's full/lite ladder is a QUALITY
    ladder and it cannot fix this, because two of the three costs are HARDWARE ceilings, not
    frame budget: iOS caps concurrent hardware video decode SESSIONS (three or four before
    VideoToolbox thrashes and every stream stutters at once), and WebKit charges several ms
    a frame for a backdrop-filter or a full-screen blend surface that a desktop GPU eats for
    free. So The Deep End probes the device once per class - `matchMedia('(pointer: coarse)')`
    or `navigator.maxTouchPoints > 1` - and puts **`.ae-touch` on `<html>`**, the same
    document-root seam `.ae-lite` uses (the game sets it, the engine only reads it, and
    BOTH come off on destroy or the lobby inherits a phone's ceiling).
    - It is **NOT a third rung**: it applies on FULL too, and `de_perf: full` does not opt
      out of it. There is no setting and there must never be one - the device is the setting.
    - It **composes with the rung in the PROTECTIVE direction, and the two dials point
      opposite ways**: `faceCap` takes the **MIN** (`FACE_CAP` 6 / `_LITE` 3 / `_TOUCH` 4 -
      fewer decoders wins) while `shallowStillMaxTier` takes the **MAX** (3 / 5 / 4 - more
      of the numerous shallow tiers frozen wins). A `min` on the still-line would hand a lite
      PHONE more animated tiers than a lite desktop. `engine/util.js videoBudget()` mins the
      same way: 6 desktop, 3 touch, 2 lite, 2 touch+lite.
    - `engine/style.js` `.ae-touch` drops what WebKit charges most for, on every rung: the
      drain wash's backdrop-filter, `mix-blend-mode` on the four FULL-SCREEN washes (the
      spiral is 150vmax - over twice the viewport of read-back), the scanline's
      `background-position` roll (per-frame re-raster of a full-screen sheet), and the two
      filters that can land over a live decode (`ae-burst-double` on a gif_burst <video>,
      `ae-mosh`'s blur every frame of a swap). The Deep End's own `.ae-touch` block in
      `games/the-deep-end/style.js` does the same on the game side: no blur on `.is-gone` /
      resurface tiles (blur over a live face), the lens loses its blend surface, the glitch
      payload loses backdrop-filter, the merge glyph pops by transform/opacity only.
    - Low Power Mode caps rAF to 30fps and the auto probe demotes to lite on that. That is
      CORRECT, not a bug: iOS Safari caps rAF to 60 even on ProMotion, so the PASS-5
      thresholds (median 20ms / 25ms x 40%) mean the same thing on a phone as on a desktop.
    - Caveat, accepted deliberately: a Windows touchscreen laptop reports
      `maxTouchPoints: 10` with a fine pointer and therefore also gets the ceiling. It is
      hardware-protective and cheap; do not "fix" it by dropping the maxTouchPoints probe,
      which is the only signal a webview that answers no media query has.

## 5. The game module contract (short version)

```js
export default {
  key, family, meaty, flagship, timeBudgetSec, title,
  manifest: { effectsConsumed:[], assetNeeds:{}, boardSizes:null, keybinds:null,
              settings:[], peek:false },
  create(ctx) { return { start(classSpec), pause(), resume(), suspend(on), destroy() }; },
};
```
`ctx = { root, engine, assets, lexicon:t, caps, rng, settings, keys, peek, ceremonies,
store, endClass({metrics:{composite}, hardGates?, zen?, flavorXp?, share?, assists?}), log }`
plus the additive read-only projection: `platform` (init's `{isTouch, hasHaptics, host}`),
`motion` (`{reducedMotion, motionLevel}`), `audioAudible` (resolved `SubAudioAudible` - FALSE
means a cue is mixed but inaudible, so carry a visual tell), `words` (a COPY of the day pool),
`absorb(word)` / `sessionWords` (trap 24), and `keys.panicKey` (the projected panic key name,
a launch-time snapshot - see trap 19).
`classSpec = { gradeTier 1..4, seed, timeBudgetSec, retake }` - `retake` is true when today
already has a row for this class (trap 23). The seed is unchanged on a retake, on purpose:
the day's script IS the day's script.

The per-class engine handle carries the pinned surface (`setHeat/fire/sustain/stop/setpiece/
beat/ceremony`) **plus** the engine's additive helpers as pass-throughs: `setPhase`, `armTail`,
`rewardRoll`, `isPlainBeat`, `plainShare`, `cadenceMs`, `channels`, `diagnostics`. Only
`fire`/`sustain` are kind-addressed, so only those two are fenced by `effectsConsumed`; the
rest read clamped state or drive the director the class already drives. A NULL engine answers
`undefined` for all of them, which is why a game still needs its own fallback - presence on
the handle is not a promise of an effect.

A game must not: import another game, touch `bridge.js`, re-expose a global setting (the
settings page skips + logs it), grade itself, or call `endClass` twice (the runner ignores
the second call and logs).

The five `games/*/index.js` are **real games** now (Semester 1 plus The Deep End, the first
Semester III class brought forward) - the placeholder stubs are gone. The shell suite therefore keeps a fixture of its own rather than driving a real game's
UI: see §6.

## 6. Verifying changes (no app UI — the owner is remote)

Everything here is testable headless. The harness lives in the session scratchpad, not the
repo: it copies this folder next to a `package.json` with `{"type":"module"}` (node treats
bare `.js` as CommonJS, the browser loads them as modules) plus a ~130-line DOM double, then
drives the real modules. Rebuild it when you need it — the recipe is:

- `core/timetable.js` / `core/grades.js` are pure: import and assert directly.
- `shell/shell.js` runs against a DOM double (`createElement`/`classList`/`appendChild`/
  `addEventListener` + a `dispatch()` helper) and a fake bridge `{send, on}` — that is
  enough to click a board row, finish a class and read the report card.
- `bridge.js` needs `window.chrome.webview = {postMessage, addEventListener}` installed
  **before** the import (it captures the transport at module evaluation).
- node 24: `navigator` is getter-only, so `Object.defineProperty` it.
- Fresh `boot.js` instance = `import('./boot.js?instance=2')` (query defeats the ESM cache).

**The shell suite has a fixture class of its own.** Now that `games/*` are real games, none of
them can be driven board -> `endClass` from one synthetic click, and a SHELL case should not
have to fight a game's UI to assert a meta write. The harness drops
`arc/games/test-class/` (the union of what the four retired stubs each proved, with knobs:
`tc_zen`, `tc_fail_gate`, `tc_absorb`) into its COPY of the web root and patches the COPY of
`games/registry.js` with one opt-in hook - `globalThis.__ARC_TEST_GAMES__ = {key: path}`
read at `loadGames()` time. The repo's registry stays a frozen five-entry table: the shell
must never grow a test seam that ships. Cases opt in through an `overrideCalendar`, so every
other case still sees the shipping five-game pool and the seeded boards it asserts against.
Remember `clearTimetableCache()` between boots (trap 25).

Last full run: **144 assertions, 0 failures** (timetable 27, grades 23, shell 45,
bridge+boot 15, **e2e seams 14**, **host fixes 20**), against the live `engine/` + `provider/`
modules (the note line in the shell run says which). The four game suites (`games-dt`,
`games-lf`, `games-dv`, `games-ic`) drive the REAL games and run green alongside it.

`test-hostfixes.mjs` covers the two seams that are not JavaScript. It **parses the real
`styles.css`** and evaluates the `[hidden]` cascade for every element the shell toggles
(trap 27) - the one assertion that would have caught the playtest blocker - and it **greps the
C# host** for the shape of each host-side fix: the panic rung's position relative to the app's
exit branch, the keybinds refusal, the meta store's day trim / atomic save / salvage ladder,
`ShutdownFlush` in `App.OnExit`, the `CurrentReplaced` rebind and the remote-batch generation
guard. A grep is a tripwire, not a unit test - it exists because `ArcademyHostService.cs` and
`ArcademyMetaStore.cs` have no test host of their own, and the precedent is the lexicon-coverage
check in `test-e2e.mjs`. **The atomic-save and salvage paths themselves are covered by source
shape only; the .NET behaviour is unverified by machine and was reasoned through by hand.**

`test-e2e.mjs` is the cross-agent one: a realistic C# init (with `settings.localAssets`) →
board → class → `assets-request` → a host `assets` reply absorbed by reqId → `class-ended` →
`payout-result` → report card, plus the panic-key projection, the lexicon-coverage check
(it greps `ArcademyHostService.cs` for the table) and `shell/audio.js` against a stub
`AudioContext`. Two shims it needs that `domshim.mjs` does not carry: `document`
`addEventListener`/`dispatchEvent` (the shim's document is a plain object, which is why
audio.js no-ops harmlessly in the other suites) and a fake `AudioContext`.

## 7. Known gaps / open questions (v1)

- ~~`arcademy-sfx` has no consumer.~~ **CLOSED** — `shell/audio.js` owns it (trap 18). What
  is still open: every cue is *synthesised*. Real sfx/vo samples (ccp.content is already a
  mapped origin) would replace `playRecipe()` and nothing else.
- ~~`init` carries no panic key.~~ **CLOSED** — projected top-level (trap 19).
- ~~Mods can only skin the rows the host's `NeutralLexicon` declares.~~ **CLOSED** — the C#
  table now mirrors `DEFAULT_LEXICON` key-for-key plus one `game_<key>` row per registered
  game (asserted by the scratch e2e suite). `MergeModTable` still only merges declared keys,
  which is the point: completing the table is the fix, relaxing the filter is not.
  - ~~**Still unskinnable: per-game setting/keybind labels.**~~ **CLOSED** — every row the
    four Semester-1 games can render is in `NeutralLexicon` (147 added: `dt_*` 28, `dv_*` 26,
    `lf_*` 19, `ic_*` 66, plus `absorbed`, `detention_so_close`, `revision_day(_hint)`,
    `mark_hit/near/miss` and the shell's `retake`). The list is derived mechanically - a
    scratch script extracts every `t('key'` call site and `label_key`/`hint_key` in `games/**`
    and diffs it against the C# table; the three keys built by concatenation
    (`ic_err_`, `ic_lie_`, `mark_`) are enumerated in that script, so a NEW suffix in a game
    means adding it there too. Deja Vu's enum key is **`dv_matched_loops`** (`auto` /
    `keep-playing` / `freeze`) - the stub-era `dv_freeze_matched` never shipped and no longer
    exists anywhere.
- **`init.palette` matches** the host's seven keys (`ground/navy/panel/ink/pink/lavender/
  gold`); `shell.js` `PALETTE_TOKENS` also tolerates `accent`/`accent2`/`line` aliases and
  logs anything unknown.
- ~~**One-meaty pools** (see trap 6) fill the meaty slot ~25% of days.~~ **CLOSED** — The Deep
  End is the second meaty class and a 14-day deal now carries one meaty class every day. No code
  changed: the relaxation order is still flagship → meaty → family → no-repeat.
- **Tier promotion** is `tier = 1 + floor(promotions/2)`, cap 4, promotion = S or A, stored
  per game in meta. Simple by construction; nothing in the design pinned a curve.
- **No entry point yet.** `Services/Arcademy/*` and the launch button are the C# agent's;
  nothing in this folder knows how it gets opened.
- ~~`init.protectBrowserVideo` is projected but nothing acts on it.~~ **CLOSED** — the host hooks
  `BrowserMediaService.PlayingChanged` and posts the same `suspend {reason:'video'}` a mandatory
  video gets, gated on the LIVE `ProtectBrowserVideoPlayback` preference rather than the init
  snapshot. The page still needs no new code: it already honours the frame. (The gate properties
  themselves — `ShouldDeferInterruptions` / `ShouldDeferNewVideo` — are polls, and polling a
  class's freeze state would be worse than not having it, which is why the event is the hook.)
- **Nothing consumes `arcademy-fx`.** The engine narrates every primitive on that event and
  only `arcademy-log` is read (by `boot.js`). It is the obvious hook for a future telemetry
  or "what did the engine just do" debug overlay.
