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
| settings values | **C#** (`set-setting` → `setting` echo) | the page assuming its own clamp |
| screens, ctx, lifecycle | `shell/shell.js` | a game calling `bridge` directly |
| effects | `engine/` (parallel agent) | a game exceeding `effectsConsumed` |
| media | `provider/` (parallel agent) | a game fetching anything itself |
| sound | `shell/audio.js` | the engine, a game, or C# owning an Audio node |

Screen router: **split-flap board → class → report card** (+ settings, which is a screen).
`boot.js` owns the bridge handshake and the Esc ladder's outer rungs; `shell.js` owns the
inner rungs.

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
games/<key>/index.js  one folder per game; games NEVER import each other
```

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
    `value is JObject` and silently drops a string, so a rebind would vanish.
- **Attendance is HOST-owned.** `ArcademyMetaStore` mints `streak`,
  `perfectAttendance`, `lastAttendanceLocalDate` and `todayClasses` from the `class-ended`
  frame (so a stale page cannot forge a streak) and **refuses** a page write to any of them.
  `core/store.js` lists them in `HOST_OWNED_KEYS`, drops such writes locally, and reads the
  numbers back from two places: `payout-result` (which carries `streak` /
  `perfectAttendance` / `classesToday` on the same frame) and the whole-blob snapshot.
  The page still owns `days` (the graded view) and `games` (tier + per-game state).
- **`meta` arrives in TWO shapes** — `{key, value}` (the reply to a meta-command) and
  `{rev, state}` (the snapshot the host pushes after crediting attendance). Handle both; a
  handler that requires `key` silently drops the authoritative streak.
- **Board size** is a per-game setting under a derived key, `<gameKey>_board_size`
  (`shell/settings.js` `boardSizeKey()`), also surfaced to games as `ctx.settings.boardSize`.
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
5. **A 4-game pool cannot satisfy no-repeat-3.** 3 rotating games into 2 slots means the
   generator relaxes on most days. Relaxation order is law (flagship → meaty → family →
   no-repeat) and no-repeat *narrows* (3→2→1→off) instead of dying, so the board still
   refuses yesterday's class. Every constraint is also a seeded weight so the preference
   survives relaxation. `day.relaxed` and `day.noRepeatWindow` report what happened.
6. **A pool with exactly one meaty game cannot be meaty every day** — no-repeat outranks
   meaty, so the meaty slot fills ~1 day in 4. Contract-correct; see §7 open questions.
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
and `classSpec = { gradeTier 1..4, seed, timeBudgetSec }`.

A game must not: import another game, touch `bridge.js`, re-expose a global setting (the
settings page skips + logs it), grade itself, or call `endClass` twice (the runner ignores
the second call and logs).

The four files in `games/*/index.js` today are **placeholder stubs**: they render a
"class placeholder" card proving the ctx wiring and end the class with a B. The game agents
replace them wholesale. Each stub deliberately exercises one shell mechanism —
Daily Trigger the share payload and an out-of-manifest effect refusal, Lost & Found peek +
board sizes + an asset claim, Déjà Vu peek-as-cram-assist, Impulse Control a keybind verb
and a failed hard gate.

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

Last full run: **113 assertions, 0 failures** (timetable 27, grades 23, shell 37, bridge+boot 13,
**e2e seams 13**), against the live `engine/` + `provider/` modules (the note line in the shell
run says which), plus the engine agent's own 238 + 84.

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
  - **Still unskinnable: per-game setting/keybind labels.** `shell/settings.js` renders them
    as `t(manifest.settings[].label_key, key)` (`dt_hard_mode`, `lf_zen`, `ic_go_key`, …).
    Those keys come from game manifests and the stubs' are placeholders, so they were left
    out on purpose — each game agent adds its own final rows to `NeutralLexicon`.
- **`init.palette` matches** the host's seven keys (`ground/navy/panel/ink/pink/lavender/
  gold`); `shell.js` `PALETTE_TOKENS` also tolerates `accent`/`accent2`/`line` aliases and
  logs anything unknown.
- **One-meaty pools** (see trap 6) fill the meaty slot ~25% of days. If the design wants a
  meaty class every day, either tag a second game meaty-eligible or promote `meaty` above
  `no-repeat` in the relaxation order — an owner/design call, not a code call.
- **Tier promotion** is `tier = 1 + floor(promotions/2)`, cap 4, promotion = S or A, stored
  per game in meta. Simple by construction; nothing in the design pinned a curve.
- **No entry point yet.** `Services/Arcademy/*` and the launch button are the C# agent's;
  nothing in this folder knows how it gets opened.
- **Nothing consumes `arcademy-fx`.** The engine narrates every primitive on that event and
  only `arcademy-log` is read (by `boot.js`). It is the obvious hook for a future telemetry
  or "what did the engine just do" debug overlay.
