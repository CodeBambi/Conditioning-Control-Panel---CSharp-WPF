# shared/sound - the Back Room's one kit

Every sound the Back Room makes comes out of `kit.js`: one AudioContext for the whole room, one master gain, one small
delay-feedback room on a send bus, and a table of cues that are scored first (pure data) and rendered second. It is
synth-first (oscillators, filtered noise, a delay line) so the room owns every sound it makes and ships no licensed
samples. The race's mp3 chimes the stations used to borrow are retired.

```js
import { kit } from '../../shared/sound/kit.js';
kit.arm();                              // inside a gesture; builds the context once
kit.play('win', { tier: 'big' });       // a cue at the frame; returns the notes scheduled (0 = traced only)
kit.stop('ladder');                     // take a cue back, scheduled-ahead notes included
kit.setMaster(0.8);                     // 0..1, the default; there is no volume setting of its own in the room
kit.suspend(true);                      // Law VI: every voice and bed hold; the ambience comes back on resume
kit.dispose();                          // the room settles: the context closes, the next arm() builds a new one
```

`score(name, opts)` is exported on its own: `{ name, notes, tail }`, checkable in bare node with no audio.

## The rules of the floor (owner, 2026-09-15)

- Wins always rise in pitch, and the bell ladder rolls with the count-up. No win voice ever sweeps down.
- Losses are near-silent: a soft settle, never a descending "fail" sound.
- A near miss gets a rising anticipation that resolves quietly.
- The ambient bed never stops while the room is open (it holds on suspend and comes back on resume).
- Every win tier has a longer and richer tail than the one below.

## The palette

| cue | what it is | length | opts |
|---|---|---|---|
| `ambience` | bed: a low warm hum (55 Hz pair under a 220 Hz lowpass) and a very slow shimmer, -24 dB under master, loops | until stop | |
| `spiral` | bed: two slow-beating pairs (110/114, 220/224.5 Hz), in 250 ms, out 500 ms | until stop, or `ms` | `ms` |
| `tick` / `ticks` | a reel's filtered click, rising ladder per reel; `ticks` is the whole decelerating train | 22 ms / travel | `reel` 0..4, `step`, `ms` |
| `lever` | THE LEVER PULL, one whole gesture: the stroke, the stop at the bottom, the spring back. Four voices (below) | 0.51-0.70 s | `variant` A-D, `level` |
| `reel` | THE REEL ROLL: a loop per reel that follows the drum. Not scored: `play('reel', {reel, variant, speed})`, `setRollSpeed(reel, speed)` each frame, `stop('reel', {reel})` on the stop frame | until stop | `reel` 0..4, `variant` A-D, `speed` 0..1, `level` |
| `reelStop` | the gesture that lands one drum, per reel voice; it carries the thud, so a stop is still one beat | 0.26-0.34 s | `variant` A-D, `reel` 0..4, `level` |
| `riser` | the anticipation: a triangle sweep 180 -> 520 Hz under an opening lowpass, resolving on a quiet top note | `ms` + 0.35 s | `ms` 1500-2500, `level` |
| `almost` | the near miss resolving: the riser's top note, soft and flat | 0.4 s | `level` |
| `settle` | a dead spin, a lost hand, a missed pocket: a felt-wrapped C4 touched once and a breath in the ambience that opens | 0.65 s | `level` |
| `sigh` | EMI's snooze / a push: breathy, soft, flat | 0.9 s | `level` |
| `ladder` | the bell ladder under a count-up: pentatonic, ascending, 2..16 bells, as long as the win | `ms` | `ms`, `plan` [{at, semis}], `semis`, `n` |
| `win` small | two soft notes, root and fifth | 0.6 s | `semis` |
| `win` mid | a warm four-voice chord opening upward | 1.3 s | `semis` |
| `win` big | the chord, a four-bell arpeggio an octave up, a 2 s shimmer | 2.4 s | `semis` |
| `win` hero | a sub-bass bloom, a low chord, a ten-bell chime tree climbing from the root, a crown bell two octaves up, two shimmers | 5.2 s | `semis` |
| `word` | the whisper shimmer bed under a spoken subliminal word: 0.9 s of breathy noise with a gentle downward filter sweep; `index` 1 and 2 sit a whole tone up each | 0.9 s | `index` 0..2, `level` |
| `breath` | a deep slow breath under the melt: lowpass noise in, then out, a 50 Hz sub | 6 s | `ms` |
| `chips` | bright clinks at random pitch | `n` x `gap` | `n`, `gap` |
| `card` | `slide` off the shoe, `flip` shorter and brighter | 0.15 / 0.08 s | `kind` |
| `rattle` | the ball's fret clicks, spreading out and dying down | ~0.8 s | `n` |
| `drop` | the pocket drop: one damped thud | 0.16 s | |
| `clack` | the wheel's peg: one ratchet click | 14 ms | `semis` |
| `clicker` | one dry mechanical click, a slide projector's relay, 30-60 ms, a little random in pitch | 30-60 ms | `level` |
| `thud` | a reel or the pointer settling; `muted` for a no-pay stop | 0.32 s | `semis`, `muted`, `level` |
| `token` | THE BANK's token landing, a rung higher each; `last` is the mini-thud | 0.18 s | `i`, `last` |
| `tap` | a button, a lever, "no more bets" | 30 ms | |
| `launch` | the ball's rising whoosh | 0.3 s | |

## The lever and the drum

The two loudest gestures in the room come in four voices each, so they can be picked by ear. `LEVER_VARIANTS`
and `REEL_VARIANTS` are A-D; `DEFAULT_SFX` is the owner's pair, **lever B over reel C**.

| lever | what it sounds like |
|---|---|
| A **Iron** | 3-4 ratchet ticks rising up the stroke, a heavy metallic clank at the bottom (filtered noise over a low body resonance), a soft spring click coming back |
| B **Candy** | a velvety band-passed whoosh down the stroke, a satisfying muted pop at the bottom, a two-note chime tail on the release. The pink cabinet's own voice |
| C **Toy** | light plastic click-clack, a little knock, a bouncy cartoon spring boing on the way back. The shortest of the four |
| D **Vintage** | one long creaking ratchet over the whole stroke, a deep wooden thunk, then a short "krrr" as the reels are released, handing the beat straight to the roll |

| reel | the roll | the stop |
|---|---|---|
| A **Ticker** | per-symbol ticks, classic mechanical: the tick rate IS the reel speed, each reel a few semitones higher than the one left of it | a thud and a short damped bell |
| B **Purr** | a low band-passed noise loop whose band and level open with the speed, a whir rather than a click | a soft thump |
| C **Rattle and bell** | ticks over a resonant hum; as a drum slows the ticks slow AND climb in pitch, so A1's stretched third reel tells on itself | a ding that climbs reel to reel: first low, second mid, third high |
| D **Hybrid casino** | ticks and purr layered, a busy floor | the thud and a digital chime, a rung higher each reel |

The roll is not scored like the one-shots: it is a live loop per reel, one gain and (per voice) a purr, a hum and
a tick train scheduled a beat ahead on a timer. `tickGap(speed)` is the whole trick: 38 ms a tick at full blur,
260 ms crawling into the stop. `setRollSpeed` is untraced on purpose, because the reels call it every frame.

Levels: a tick peaks at `ROLL_TICK` 0.09 and the purr and hum together at `ROLL_BED` 0.08, both over the
ambient bed's 0.063 (-24 dB), both on the same master and trim chain as everything else, and both held by
`kit.suspend` and `kit.mute` like every other voice.

### Picking a pair

`stations/slot/sound.js` resolves the pair once when the station opens, in this order: `DEFAULT_SFX`, then
`localStorage.br.sfx.variant` (`{"lever":"A","reel":"D"}`, or the compact `"A/D"`), then the URL query
`?lever=A&reel=D`, which wins so a dev page can try a pair without changing what is saved. `pickVariant` is
pure and node-tested (`stations/slot/tests/sound.test.mjs`); nonsense anywhere falls back to the default pair.

### The audition page

`shared/sound/audition.html` plays the eight voices on their own: a button per lever, a button per drum (a whole
3-reel spin with the third reel stretched for the anticipation), a full pull, and the ambient bed to level them
against. `serve.mjs` is the tiny static server the dev pages want, the same shape the smoke checks use:

```
node ConditioningControlPanel/Resources/web/backroom/shared/sound/serve.mjs
```

then open <http://127.0.0.1:8940/backroom/shared/sound/audition.html> (`SOUND_PORT` moves it). "Use this pair in
the game" writes `br.sfx.variant` for that origin; the slot's own harness takes the query instead:
<http://127.0.0.1:8940/backroom/stations/slot/dev.html?lever=A&reel=D>.

`settle` and `clicker` are deduped (`DEDUPE_MS`): two lanes saying `settle` on the same frame make one settle. The
spoken word itself is another lane's speechSynthesis call; the kit never synthesises speech.

Calm turns the whole floor down (`kit.setTrim(0.6)`, the room does it from the settings frame). Reduced motion keeps
every cue: sound is where the beat lives when the travel is gone.

## Per-game cues

| game | frame | cue |
|---|---|---|
| room | first gesture | `ambience` (loops while the room is open) |
| room | suspend / resume | `kit.suspend(on)`: everything holds, the ambience returns |
| room | settle (halt) | `kit.dispose()` |
| room | settings, intensity calm | `setTrim(0.6)` |
| slot | the lever pull (Law VIII, the frame it leans) | `lever` in the picked voice |
| slot | each reel's travel | `reel` started on its first frame, `setRollSpeed` off the same decel curve |
| slot | reel thud (reel 0..2) | `stop('reel', {reel})` and `reelStop` on the same frame (`thud` semis -2, 0, +2 inside it) |
| slot | last reel of a no-pay spin | `settle` |
| slot | reel 2 thud with a hold (A1) | `riser` over the hold, Calm at half level |
| slot | last reel near miss (A2) | `almost` |
| slot | pay (chime / two / thud / reveal) | `win` small / mid / big / hero, root from the streak's semis |
| slot | THE BANK's rollup | `ladder` on feel.ladderPlan (step 0 is the landing note); skip = `stop('ladder')` |
| slot | token landing / last token | `token` i / `token` last |
| slot | a melt landing | `breath` |
| slot | lone-word lane (other lane) | `clicker`, `word` index 0..2 |
| slot | dead-spin lane (other lane) | `settle` (deduped with the muted thud) |
| wheel | peg crossing | `clack` semis from feel.tick |
| wheel | the long last turn | `riser` 2 s |
| wheel | pointer settles / snooze | `thud` / `settle` |
| wheel | party (chime / two / thud / reveal / snooze) | `win` small / mid / big / hero, `sigh` |
| wheel | tokens | `token` |
| roulette | a chip placed | `chips` |
| roulette | "No more bets" | `tap` |
| roulette | launch | `launch` |
| roulette | the fret rattle (ball slowing) | `rattle` + `riser` to the rest (0.8-2.5 s) |
| roulette | every landing | `drop` |
| roulette | miss / near / win / straight, wake / full | `settle` / `almost` / `win` mid + chips / `win` big + chips / `win` hero + chips |
| cards | bets set | `chips` |
| cards | a card dealt / the hole card | `card` slide / `card` flip |
| cards | a blackjack bloom | `win` big |
| cards | settle: win, dealer bust / streak / sweep / lose / push | `win` mid / `win` big / `win` hero / `settle` / `sigh` |
| cards | bust | nothing (the silence is the beat) |
| counter | a buy confirmed | `chips` x3 + `win` small |
| hypno (shared) | fx.loom_spiral fires / its hold releases | `spiral` / `stop('spiral')` |
| hypno (shared) | fx.haze fires, a tunnel breath | `breath` |
| hypno (shared) | a subliminal word step (n words) | `word` index 0..n-1, 0.9 s apart |

The station adapters (`stations/slot/sound.js`, `stations/wheel/sound.js`) keep their old API over the kit, so
station.js and the checks are unchanged; their `trace` still lists the cues with page times for dev.html.

## Tests

`shared/sound/tests/kit.test.mjs` (node --test, a mocked AudioContext): tier -> tail and voices, the rising
invariants, loss silence, the riser's shape, the three named cues, the four levers and the four reel stops, the tick rate,
the drums on a context, ambience and spiral beds, suspend/resume, stop by name, no node leaks over 200 plays,
dedupe, dispose and re-arm, trim. From `Resources/web/backroom`:

```
node --test $(find . -name '*.test.mjs')
```
