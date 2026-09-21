# Breakout twists - the frozen contract

This file is the truth for the twist seam. Where the shared brief and this file disagree, this file wins.
Five lanes fill five twists in parallel. A lane touches ONLY its own five files:

    twists/<id>.js  twists/<id>-render.js  twists/<id>.test.js  cues/twist-<id>.js  reactions/twist-<id>.js

`<id>` is one of `crumble mirror keys node justone`. Everything else (game.js, render.js, station.js,
doors.js, twists/index.js, cues.js, reactions.js) belongs to the scaffold and the integrator. Need
something the seam does not give you? Put it in your report. Do not edit it.

All five modules are already imported by `twists/index.js`, `render.js`, `cues.js` and `reactions.js`,
so a lane never edits an import list, and a lane that ships nothing still leaves a playable board.

## 1. The twist module, `twists/<id>.js`

Default-export an object. Every hook is optional; a missing hook is not an error. Nothing here may
touch the DOM, `document`, `window`, `Math.random` or the clock: this half runs under `node --test`.

```js
export default {
  id,                                 // must equal the registry key
  build(g, ctx),                      // once, right after the authored wall is built
  onHit(g, br, ball, ctx),            // a brick took a hit that did NOT break it (steel counts)
  onBreak(g, br, ball, ctx),          // a brick broke, after the game's own `brick` event
  update(g, dt, ctx),                 // every sim step, after the wall and the balls have moved
  onCatch(g, drop, ctx),              // the paddle caught a power drop (`drop` = { kind, treat })
  wallCleared(g),                     // the board is clear, before the next board is built
};
```

A hook that throws is caught and swallowed by the game: a twist's bug never stops the frame. It also
never gets a second chance, so do not rely on that.

## 1b. A board runs a LIST of twists

`g.twists` is the list, `g.twist` is still the board's own one. A board gets its authored twist, PLUS
`crumble` whenever the board lays any clay brick and its own twist is not already crumble. That is the
owner's cracked key: `lock_keys2` runs `keys` and `crumble` together, so priming the wax seal and then
popping one end really does run the chain. Every hook (`build onHit onBreak update onCatch wallCleared`)
goes to every module in the list, in order, each inside its own try / catch. Render walks the same list:
a brick is offered to every painter that claims it, and `under` / `over` run for each.

A twist must therefore not assume it is alone on the board. Keep your state on your own key (`g.crumble`,
`g.keysTwist`, ...) and flag only bricks you own.

## 2. `ctx`, exactly

```js
{
  emit(name, data),                   // the game's own event channel; cues and reactions key on it
  rng(),                              // the game's seeded rng; never Math.random
  at(row, col),                       // the authored brick at that cell, dead or alive, or null.
                                      //   Off the edge is NULL, never the row next door: col -1 and
                                      //   col 16 do not wrap. Walk neighbours freely.
  breakBrick(br, ball),               // break a brick NOW, through every normal consequence.
                                      //   A TRUE KILL at any armour: steel and gate steel are cleared
                                      //   and the hp ladder is spent, so a three-hit mirror twin comes
                                      //   down in one call. It never merely chips.
  schedule(seconds, fn),              // run fn(g, ctx) after `seconds` of SIM time. Deterministic,
                                      //   ordered by due time, frozen with the game. Returns a
                                      //   cancel function. Timers die with the board.
  powers,                             // the powerups module: powers.drop(br), powers.reset()
  startRelapse(),                     // force a relapse now (nothing happens if already in GREY)
  w, h,                               // the field size
}
```

## 3. Brick flags the authored wall sets (doors.js `brickSpec`)

| flag | chars | meaning |
| --- | --- | --- |
| `strength` / `hp` | `1 2 3 c d e w y C G S K Q P U V F T` | the game's own durability pair. `hp` counts down, `strength - hp` is how many cracks render draws. |
| `steel` | `X M W` | never breaks by a ball, never counts toward clearing the board. |
| `gate` | `M` (gold) / `W` (cyan) | steel with a trim colour, sealing a loot box. |
| `key` | `K`->`'M'`, `Q`->`'W'`, `P`->`'clay'` | one hit, and what it opens. |
| `clay` | `c d e` | `strength` 3, `hp` 3 / 2 / 1. |
| `wire` / `cutme` / `powered` | `w` / `y` | `y` is the wire the board wants cut. `powered` starts true. |
| `core` | `C` | the net's heart. |
| `picture` / `spiralBrick` | `G` / `S` | the game's own payload bricks (`gif`, `spiral`) are set too. |
| `powerup` | `U`->multiball, `V`->shield, `F`->fireball | a real drop through the real powerups module. |
| `treat` | `T` | a `fireball` drop that also carries `treat: true`. |

A lane may add its own flags to a brick. It must not remove `alive`, `x`, `y`, `w`, `h`, `row`, `col`.

## 4. Event names (fixed; cues and reactions key on exactly these)

`clayCrack {x,y,hp}` `clayReady {x,y}` `clayChain {x,y,n}` `clayChainEnd {n}`
`mirrorPop {x,y,tx,ty,prize}` `keyTurn {x,y,gate}` `gateOpen {gate,n}` `clayPrimed {n}`
`nodeCut {x,y,n}` `nodeZap {x,y,depth}` `coreDown {x,y,n}`
`treatDrop {x,y}` `treatCatch {doses}` `treatMiss {}` `comedown {doses,count}`
`doorClear {door}` (the game emits this one, not a lane).

## 5. Render, `twists/<id>-render.js`

```js
export function brick(ctx2d, br, snap, t) // inside the brick's transform: origin = its centre,
                                          //   already rotated and scaled. Drawn after the brick face.
                                          //   Only for bricks the twist flags (see below).
export function under(ctx2d, snap, t)     // field coordinates, under the bricks
export function over(ctx2d, snap, t)      // field coordinates, over everything but the HUD
```

`snap` is the live game object (`game.snapshot()`). `t` is `snap.time`. Each hook is wrapped in its own
`save()` / `restore()` and its own try / catch. `brick` is offered for a brick carrying ANY of
`clay wire core key gate treat steel`, plus anything in the twist's own `paintsBrick` list if it
exports one (`export const paintsBrick = ['mine']`). `under` and `over` run for every twist the board
runs (section 1b), and only in COLOUR (GREY is payload-free).

REDUCED MOTION: `snap.reduced` is true. No wobble, no shake, no bursts. State must still read: cracks,
dark wires, a static dust mark. This is a rule, not a preference.

## 6. Cues, `cues/twist-<id>.js`

Export a map `name -> (synth, data) => void` as the default export. `cues.js` spreads all five in.
Do not shadow an existing key (`brick`, `hit`, `powerCatch`, ...): pick your own event names from
section 4. House rules (AGENTS.md, "Audio design rules learned from Breakout"): a pitched cue comes
from `pentatonic()` + `ROOT_HZ` and lands on `quantise()`; a physical impact plays at `synth.now` and
is never quantised. Quiet (gain 0.03 to 0.13) and short (under 300 ms). Return `false` to let the
caller play its older fallback.

## 7. Reactions, `reactions/twist-<id>.js`

Export a map `name -> (fx, d, snapshot) => void` as the default export. `reactions.js` spreads all five
in. Cosmetic only; honour `fx.reduced` and `fx.colour`.

## 8. Seams the scaffold knows are rough

- The treat drop rides the real `fireball` drop and carries `treat: true`. It therefore DRAWS as a
  fireball capsule until the justone lane paints over it in its `over` hook. That is the seam, not a bug.
- `powers.drop(br)` only drops in COLOUR. A twist that wants something to fall in GREY has to fall
  itself, and GREY is payload-free by design, so do not.
- A lane that needs a new flag on the authored wall asks the scaffold. `doors.js` is not lane-owned.

## 9. The door's colour

A door board's plain bricks are tinted from the door accent (`snapshot().doorColour`: fog #F062A8,
wardrobe #A774E8, lock #D9A531, hive #2FCB72, ward #EE5A44). Durability stays BRIGHTNESS inside that
hue, exactly as the house game does it, and the GREY state is left dull. render.js owns this; a twist
never sets a brick colour of its own. If your twist paints a material (clay, wire, gate trim), that
material keeps its own colour on every door: it is a thing, not a mood.
