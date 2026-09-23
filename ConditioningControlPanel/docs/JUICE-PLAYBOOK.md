# Juice Playbook

How this project makes things feel good, and the mistakes it already paid for. Breakout is the main case
study: testers call it Zen and very satisfying, and its sound is the most praised audio in the project.
Read this before you add an effect, a tween or a sound to any game or UI surface.

Paths are relative to `ConditioningControlPanel/`. `BO` below means
`Resources/web/backroom/stations/breakout/`.

---

## 1. Core principles

1. **Nothing appears or vanishes in one frame.** Every effect has an IN and an OUT. A thing that pops
   into existence reads as a bug; a thing that grows in reads as an event.
2. **Things come from somewhere and go somewhere.** A reward flies from the event to the wallet. A
   picture grows out of the bubble that was popped. Motion with an origin tells the player what caused it.
3. **Impact is many small channels at once.** Particles, a knock in real pixels, a squash, a sound, a
   number. Each one alone is subtle; together they read as weight. One big channel reads as noise.
4. **Juice is earned.** The juiciness of Breakout climbs with play (the saturation ladder). Players feel
   progress as the world waking up, not as a number.
5. **Size the party to the event.** A small win gets a chime, the jackpot gets the reveal. If everything
   is loud, nothing is (`shared/win/plan.js`, Law IX).
6. **Never bury the content.** Effects frame the media and the play; they do not cover them. When the
   words on the ball's tail got drowned by the streak, the streak was cut, not the words.
7. **Failure subtracts, it never punishes.** Losing takes juice away (grey, muffle, fewer layers). It
   never adds a harsh cue, a falling sound or a red alarm.
8. **Access and safety are not optional.** Reduced motion, 3 Hz photosafe, perf tiers. An effect that
   breaks under reduced motion is broken.
9. **Pure maths first, wiring second.** Curves, springs, schedules and budgets live in small modules
   with node tests. The DOM, canvas or WPF layer just reads them.
10. **Feel is judged in play.** A green suite and a good screenshot are not "done". The owner plays it.

## 2. Tweening

### Easing vocabulary

| Name | Curve | Use it for |
|---|---|---|
| THUD | `cubic-bezier(.2,1.5,.4,1)` | Entrances that land with weight. Overshoots, then settles. |
| ease-out (cubic/quad) | `1 - (1-t)^3` | Anything arriving: it is fast first, then gentle. Most entrances. |
| ease-in | `t^2` | Anything leaving under its own power (sucked away, falling). |
| ease-in-out / smoothstep | `t*t*(3-2t)` | Holds that release, camera moves, parallax returns. |
| damped spring | `exp(-k u) * cos(w u)` | Squash, jelly, wobble, anything physical that rings. |
| sine loop | `sin(age * hz + phase)` | Idle life: breath, bob, drift. Always phase each copy differently. |

Real examples:

```js
// BO/feedback.js - impact squash: flat at once, round by 40%, one small springy overshoot
const k = u < .4 ? 1 - u / .4 : -.3 * Math.sin((u - .4) / .6 * Math.PI);
return { along: 1 - .38 * k, across: 1 + .24 * k };

// BO/feedback.js - last-brick push-in: cubic rise in 120 ms, hold, smoothstep back by .85 s
const rise = 1 - Math.pow(1 - Math.min(1, t / .12), 3), fall = Math.max(0, (t - .35) / (PUSH_IN_S - .35));
return 1 + .06 * rise * (1 - fall * fall * (3 - 2 * fall));

// BO/brick-wobble.js - damped swing with a squash that rides on it
const k = (1 - u) * (1 - u), s = Math.sin(u * TAU * WOBBLE.swings);
```

WPF: `QuadraticEase` / `CubicEase` EaseOut for arrivals, `SineEase` EaseInOut for ambient loops, and a
two-keyframe `EasingDoubleKeyFrame` pop (peak at 35 percent, settle to 1) as in
`Windows/Launcher/LauncherWindow.Choreo.cs`.

### House timings

| Beat | Time | Source |
|---|---|---|
| THUD (landing entrance) | 340 ms, THUD curve | The Line feel vocabulary |
| SHIVER (small reaction) | 250 ms | The Line |
| REVEAL (big unveil) | 620 ms | The Line |
| BREATH | one per screen | The Line: one idle breath per screen, not five |
| Entrances | 150-350 ms | general |
| Exits | 120-250 ms | exits are quicker than entrances |
| Hover tween | 80-400 ms | `Services/MotionFx.cs` interaction clock |
| Ambient loops | 8-60 s | `MotionFx` ambient clock |
| Glyph highlight, then callout + fx | 400 ms, 80 ms apart | `shared/hypno/callout.js` `HIGHLIGHT_MS`, `FX_DELAY_MS` |
| Announcer zoom | 0.6 to 1.3 over 1 s, hold 200, out 400 | `callout.js` `ZOOM` |
| Subliminal word | in 80, hold 500, out 400 | `callout.js` `WORD_*` |
| Win party by rung | 0 / 500 / 1200 / 2000 / 6000 ms | `shared/win/plan.js` `PARTY.MS` |
| Win glow | 480 ms, in fast out slow | `plan.js` `GLOW_MS` |
| Tile pop | 380 ms, peak 1.06 | `LauncherWindow.Choreo.cs` `PopMs`, `TilePopScale` |
| Launch shockwave | 480 ms, 40 to 700 px | `Choreo.cs` `ShockwaveMs` |
| Exit beat before hide | 450 ms | `Choreo.cs` `ExitBeatMs` |
| Hit-stop, rare events only | 90 / 60 / 40 ms | `BO/game.js` `LAST_STOP_MS`, `JACKPOT_STOP_MS`, `POP_STOP_MS` |
| Last-brick slow-mo | 0.45 s at 0.35x | `BO/game.js` `LAST_SLOW_S`, `LAST_SCALE` |
| Ball squash | 90 ms | `BO/game.js` `SQUASH_S` |
| Brick wobble | 0.75 s life, every 0.45-1.5 s | `BO/brick-wobble.js` `WOBBLE` |

### Anticipation, overshoot, settle

- **Anticipation:** a few frames of gather before the event (the glyphs glow 400 ms before the callout;
  a shrink or glow before a burst). It tells the eye where to look.
- **Overshoot:** go 3-8 percent past the target (tile pop 1.06, push-in 1.06, card lift 1.03). More than
  about 10 percent reads as rubber.
- **Settle:** one small return swing, not three. Springs decay fast (`exp(-4.2 u)` in `jellyScale`).
- **Hold:** long enough to read. A flash held 420 ms against the app's own 5 s flash default read as a
  glitch; the web build moved to 3.8 s.
- **Exit:** quick, and it goes somewhere: shrink back to the origin, dissolve under a smear, get sucked
  toward a target. Never just `display: none`.

### Idle life

A resting thing is never perfectly still: bubbles breathe and wobble (`bubbleIdle`), power-up glyphs bob
1.6 px and breathe 9 percent (`BO/powerups-render.js` `GLYPH_IDLE`), bricks wobble on their own now and
then. Rules: phase every copy by position (never all in sync), keep it tiny, keep it cosmetic (the
renderer's own rng, never the sim's dice), off under reduced motion.

## 3. Impact recipe

Use several of these together, scaled to the event.

1. **Particles.** A burst in the thing's own colour plus a few white sparks
   (`BO/reactions/power.js` `powerCatch`: 18 coloured + 7 white). Sprays follow physics: sparks thrown
   back the way a laser came, gravity on debris. Gate counts on the juice rung and the perf tier.
2. **Shake in real pixels.** Measure what actually moves. Breakout's `cam.kick` is multiplied by
   `shakeGain` (.175 to .35), so a "kick 7.5" moved the field under a pixel and three rounds of "a bit
   more" did nothing. Bricks now have their own knock channel added after the gain:
   ```js
   // BO/render.js - REAL field pixels each way, decays exponentially, off in reduced motion
   export const BRICK_KNOCK = { hit: 2, broke: 4, combo: 4, comboStep: .2, comboMax: 6, decay: 14 };
   ```
   Useful range: 2-3 px for a hit, 4-6 px for a break or combo, more only for the hero moment.
3. **Squash and stretch.** On contact, flatten along the normal, widen across it, spring back
   (`squashScale`, `jellyScale`). Keep area roughly constant.
4. **Hit-stop.** A freeze of 40-90 ms sells a big moment. Only for rare events. Routine collisions must
   never stall (`BO/game.js` `rareStop`: colour only, never in reduced motion, never mid-transition).
5. **Glow tinted, not white.** The owner: "too white and too bright, it's hurting my eyes." The hit glow
   screened two copies of the whole frame over itself. The fix multiplies the copy by pink first, so the
   tint can only remove light, then screens it at a low level that scales from zero:
   ```js
   // BO/render-fx.js
   export const HIT_GLOW = { perCopy: .11, tint: '#ff8cc8' };
   og.globalCompositeOperation = 'multiply'; og.fillStyle = HIT_GLOW.tint; og.fillRect(0, 0, ow, oh);
   ```
   Trap: mixing toward a bright tint with source-atop ADDS light on a dark board. Multiply cannot.
6. **Number pop.** Value must be seen to move (Law XII in `plan.js`). Count up, fly tokens to the wallet,
   signed `+N` in the gain colour. Streak labels grow a little and stop growing (`perfectSize`: +3 px per
   step, capped at x6).
7. **Sound.** A physical impact plays immediately; a pitched reward lands on the beat (section 5).

One hero per beat: when two events overlap, fold them into the bigger one, never the sum
(`plan.js` `mergePlans`, Brake 2). Repetition shrinks the party (Brake 3: the first three get the
fanfare, from the fortieth it is a thud and the tokens).

## 4. Juice as progression

- **One variable drives the mix.** Breakout's saturation (0..1, earned by play) opens the audio low-pass,
  fades music layers in (melody at .4, arp at .7), brightens hits, and unlocks visual rungs
  (`rungs(i)` in `BO/render.js`: pictures at 1, stretch at 2, particles at 3, jelly at 4, knock at 5,
  glitch at 8). The player hears and sees the world wake up without reading a counter.
- **The grey world is bad on purpose.** In grey the paddle is a flat slab with no stretch, bounce, lean
  or face; the ball neither breathes nor squashes; the mix sits at `GREY_LEVEL` 0.7 on a gain after the
  master (`BO/audio.js`). Juice is what the breakout buys. Any new Breakout juice is off in grey by
  default. The same idea applies elsewhere: make the base state plain so the earned state feels like a
  gift.
- **Never let the player turn the ladder.** Options once offered "Colour intensity", which was the juice
  ladder itself. It was removed: juice is earned, not a slider.
- **Calm and reduced are different.** `plan.js`: `reduced` = no travel at all, settled state only.
  `still` (Calm / Motion off) = decoration goes, but the value still visibly moves.

## 5. Sound (the ten rules, condensed)

Reference: `BO/audio.js` (pure helpers `createBeat`, `cutoffFor`, `layerLevel`, `hitSemis` are tested),
`BO/cues/feel.js`, `Services/Launcher/LauncherMelody.cs`.

1. **One key.** Every pitched cue is `ROOT_HZ * SEMI(pentatonic(n))` (C5 root). Pentatonic has no
   semitone clash, so nothing the player triggers can sound wrong. The launcher hover melody follows the
   same rule with rendered rungs.
2. **One clock.** Notes quantise to the music grid (`beat.quantise`: next sixteenth, or the one after if
   under 15 ms away). Up to about 160 ms delay reads as musical. Bodies (thud, crack, glass) play
   immediately. Quantise what is a note, never what is a body.
3. **Gameplay timing comes from the tempo,** so events land near beats before quantising helps.
4. **Skill is a melody.** A combo climbs the scale and resets on the phrase end.
5. **One game variable drives the mix** (section 4). Hz glides exponential, gain glides linear.
6. **Failure subtracts.** Muffle, drop layers, detune slightly; the beat never stops, no cue falls.
7. **Protect the low end, share one space.** Sub bus unfiltered, sfx bypasses the bed filter, one small
   shared delay, hits panned to screen position, a word bus that ducks the rest.
8. **Synthesise when state should shape the sound.** Samples are for the one human sound.
9. **Quiet and short, then tuned by ear.** Voices at 0.03-0.13 gain, most under 300 ms. Budget an ear
   pass; the owner's by-ear cuts are part of the design.
10. **Scheduler hygiene.** 25 ms tick, 120 ms lookahead on the audio clock; after a tab sleep skip, never
    burst; every voice disconnects itself. Pause is a sweep (gate + low-pass after the master, 150 ms
    close, 200 ms open), not a cut, and never suspend/resume the context per pause (that is what made
    the pause "crunchier" each time).

Limit: this works because the bed is generated, so key and grid are known. An mp3 soundtrack cannot lock
cues to it without bpm, key and downbeat metadata.

## 6. Safety and access

- **Reduced motion.** Web: `matchMedia('(prefers-reduced-motion: reduce)')` plus the game's own
  setting; keep information-bearing rings and fades, drop bursts, kicks, flashes and wobble
  (`reactions/power.js` header says exactly this). WPF: `MotionFx.AllowTransitions` (off only at Off)
  and `MotionFx.AllowAmbientLoops` (Full level and an ambient-capable tier). Every `MotionFx` helper
  snaps to the end state when motion is off: do the same in your own code.
- **Photosafe.** Nothing flickers faster than 3 Hz. Reduced motion caps flashes at 3 Hz and slows
  spirals. No full-screen white flashes, ever.
- **Perf tiers.** A low tier gets fewer particles or a still, never a broken or blank effect. Examples:
  `BO/render-budget.js` shrinks the canvas pixel budget when frames run long and grows it back slowly;
  launcher tilt is off on `PerformanceTier.Performance`; `plan.js` Brake 8 caps lite boards at 4 tokens
  and no particles, but keeps the sound.
- **Compositor-only motion.** Animate `transform` and `opacity`, `filter` sparingly. Never animate
  layout (width, top, margin). Repaint big canvases once and scroll them by offset (the Back Room
  marquee repainting a 2048x256 canvas at 20 Hz was half the GPU at rest).
- **WPF equivalents.** `RenderTransform` (Scale/Translate/Rotate) and `Opacity` via
  `element.BeginAnimation`, never `Storyboard.SetTargetName` (it silently no-ops across tab namescopes).
  Fade a root grid's opacity, not `Window.Opacity`, on non-transparent windows (launcher crossfade).
  Painted pixels on a layered window intercept the mouse: make effect overlays `WS_EX_TRANSPARENT`.
- **Juice never changes a result.** Reactions, wobbles and EMI faces are presentation over a result the
  sim or server already decided. No stop weighting, no near-miss rigging, no cosmetic rng touching the
  game's dice.

## 7. Process lessons (all paid for)

- **Measure the output, not the constant.** When the owner says a change is still not visible, measure
  pixels moved or mean frame colour, then look for a gain stage downstream. Scripts that did this:
  `C:/wt-bo/knock-smoke.mjs` (field translate), `C:/wt-bo/flash-smoke.mjs` (mean colour on a hit).
- **Look for gain stages before raising a number.** `shakeGain`, a master gain, a grey dip, a global
  alpha. Do not raise the global gain to fix one event: it scales every other event too. Add a channel.
- **"Less" means about half, not a fifth.** The hit glow went .055 (too weak, and the jolt went with
  it) before landing at .11. "Less intense" is a nudge.
- **Restore what the owner remembers.** When an old look is wanted back, read it out of git history
  (`git log -S`), do not redesign it.
- **The owner judges feel in play, not in stills.** Stills and a green suite are necessary, not enough.
  A parked stack once had both and was "not good enough to present".
- **Boot-check every module you touch.** A shell heredoc ate a regex backslash, turned it into a line
  comment, and broke the live game for 40 minutes while every test stayed green and `node --check` on
  the `.js` passed. Write patches with the Write/Edit tools, boot the page headless, and for live
  probes parse the served file as `.mjs`. `BO/syntax.test.js` now parses every station module.
- **Registry pattern: add juice without merge conflicts.** Breakout routes every event through two
  registries. Add a key, never edit `audio.js`, `station.js` or the render switch:
  ```js
  // BO/cues.js    name -> (synth, data) => void, offered every game event first
  export const CUES = { ...POWER, ...FEEL };
  // BO/reactions.js  name -> (fx, d, snapshot) => void, at the end of render.js onEvent
  export const REACTIONS = { ...POWER };
  ```
  Five lanes built on this with zero conflicts. Split lanes by file ownership with a fixed event-name
  contract written first.
- **Seam bugs hide from fake contexts.** Lanes tested against a fake ctx passed while the real game
  broke between them. Test new mechanics through the real `createGame`.
- **Any "lost" callback on a resource you also release on purpose needs an intent flag,** or the two
  paths feed each other (the pointer-lock ending loop).

## 8. Checklist for any new effect

- [ ] Has an IN (anticipation, entrance with a small overshoot, settle) and an OUT (quick, goes somewhere).
- [ ] Comes from the thing that caused it; goes toward where the value lands.
- [ ] Timings from the table above, entrances 150-350 ms, exits 120-250 ms.
- [ ] Sized to the event (small/good/big/hero); one hero per beat; repetition shrinks it.
- [ ] Impact uses several small channels; shake measured in real pixels after every gain stage.
- [ ] No white full-screen flash; glow tinted by multiply; nothing faster than 3 Hz.
- [ ] Reduced motion: fades or the settled state, still carries the information.
- [ ] Low perf tier: fewer particles or a still, never blank. Compositor-only properties.
- [ ] Earned where it belongs; plain base state (grey) stays plain.
- [ ] Sound: pitched cues in key and quantised, bodies immediate, quiet and short.
- [ ] Does not cover the media or the play; does not change a result.
- [ ] Curves and schedules in a pure module with a node test; DOM or WPF only reads them.
- [ ] Added through a registry key where one exists.
- [ ] Module boots (headless page load, `.mjs` parse). Played by hand before calling it done.

## 9. File pointers

| What | Where |
|---|---|
| Visual rules: squash, jelly, idle, comet, push-in, streak label | `BO/feedback.js` |
| Idle brick wobble | `BO/brick-wobble.js` |
| Glyph bob and breath | `BO/powerups-render.js` |
| Particle pool | `BO/particles.js` |
| Knock channel, shakeGain, rungs, event reactions | `BO/render.js` |
| Hit glow, post pass | `BO/render-fx.js` |
| Reaction registry | `BO/reactions.js`, `BO/reactions/power.js` |
| Cue registry | `BO/cues.js`, `BO/cues/feel.js`, `BO/cues/power.js` |
| Audio engine, beat, grey dip, pause sweep | `BO/audio.js` |
| Hit-stop, slow-mo, squash constants | `BO/game.js` (top constants, `rareStop`) |
| Canvas quality budget | `BO/render-budget.js` |
| Win sizing, party budget, brakes | `Resources/web/backroom/shared/win/tier.js`, `plan.js`, `bank.js`, `ladder.js` |
| Callout, glyph, word and zoom timings | `Resources/web/backroom/shared/hypno/callout.js` |
| Launcher choreography (pop, shockwave, exit beat) | `Windows/Launcher/LauncherWindow.Choreo.cs` |
| Launcher tiles (hover, tilt, glow) and backdrop (parallax, ken burns) | `Windows/Launcher/LauncherWindow.Tiles.cs`, `LauncherWindow.Backdrop.cs` |
| Launcher hover melody | `Services/Launcher/LauncherMelody.cs` |
| WPF motion gate and helpers | `Services/MotionFx.cs` |
| Owner feedback history | `AGENTS.md` (Breakout sections, audio rules, launcher passes, Back Room show and reward pass) |
