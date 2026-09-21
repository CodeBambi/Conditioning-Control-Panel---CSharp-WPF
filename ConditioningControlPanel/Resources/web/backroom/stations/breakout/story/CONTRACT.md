# Breakout story mode - the frozen contract

This file is the truth for the story seam. Where the shared brief and this file disagree, **this file wins**.
Five act lanes fill five act files in parallel. A lane touches ONLY the files listed for it in the brief.
Everything else (game.js, render.js, station.js, station.css, dev.html, `story/index.js`, `story/act-intro.js`,
doors.js, twists/index.js, cues.js, reactions.js) belongs to the scaffold and the integrator. Need something the
seam does not give you? Put it in your report. Do not edit it.

The house game (`createGame()` with no `story` and no `door`) and the door runs are untouched and stay untouched.

## 1. The act file, `story/act<N>.js`

Default-export one object. Pure data, no imports, no functions:

```js
export default {
  id,        // 'monday' | 'habit' | 'notice' | 'enough' | 'out' - fixed, do not rename
  title,     // 'MONDAY' ... - the act title card draws this
  cap,       // the most g.sat may reach while this act plays (0.35 / 0.65 / 0.85 / 0.95 / 1)
  colour,    // the act accent: '#8FA3B8' '#F062A8' '#A774E8' '#EE5A44' '#5FFFD0'
  words,     // the act's word-brick list; [] means the game's own defaults
  boards: [] // the act's walls, IN PLAY ORDER
};
```

`id`, `cap` and `colour` are already set by the scaffold and a lane should not change them: `cap` is the story's
pacing and `colour` is what render.js tints the act's plain bricks with. `title` and `words` are the lane's.

## 2. A board entry, exactly

```js
{ id, name, twist, family, line, rows }
```

| field | rule |
| --- | --- |
| `id` | `st_<act>_<nn>`, two digits, unique across the whole story. A test enforces it. |
| `name` | two words at most, plain, a little dry. No exclamation marks, no em-dashes. |
| `twist` | a twist id (`crumble mirror keys node justone stare shells`) or `null`. |
| `family` | one of `breakthrough sides chambers cascade precision breather`. |
| `line` | one short sentence for the dev harness and the report. Optional but wanted. |
| `rows` | 16 chars wide, at most 12 rows, chars from `doors.js` `LEGEND` only. |

One entry is not a board: `{ house: 'finale' }`. It builds the HOUSE finale (the existing wall 8, the eye and the
Loom core) exactly as the house game builds it, and it is the LAST entry of act 5. Do not add a second one.

`rows` are parsed by `doors.js parseBoard`, and `brickSpec` is the only legend there is. A lane that wants a new
brick flag asks the scaffold; `doors.js` is not lane-owned.

## 3. Wall numbers are COMPUTED, not authored

`story/index.js` lays the five acts' board lists end to end in act order. Wall 1 is act 1's first board. So:

> **An act that ships a different number of boards than `ACT_TARGET` shifts every wall after it.**

`ACT_TARGET` = `{ monday: 5, habit: 7, notice: 7, enough: 6, out: 3 }`, and `out`'s three are two authored boards
plus the `{ house: 'finale' }` entry. Total `STORY_TARGET_WALLS` = 28. Ship exactly your count.

Useful exports: `ACTS`, `STORY_BOARDS` (flat, `{ wall, of, act, actIx, actWall, first, last, board }`),
`STORY_WALLS`, `storyBoardAt(wall)`, `actOfWall(wall)`, `isActStart(wall)`, `capForWall(wall)`, `actById(id)`,
`firstWallOfAct(id)`, `lastWallOfAct(id)`, `storyBoardById(id)`, `isHouseEntry(entry)`, `HOUSE_FINALE`.

`act5.js` must NOT import `story/index.js` (the cycle puts `HOUSE_FINALE` in the temporal dead zone at module
evaluation and the whole story fails to load). Write the literal `{ house: 'finale' }`.

## 4. The run: `createGame({ story: true, from })`

- `story: true` plays `STORY_BOARDS` in order through the SAME `authoredWall()` path the doors use.
- `from` is the wall to start at, 1-based, clamped to `1 .. STORY_WALLS`. Default 1.
- The wall now playing is `snapshot().storyWall`; the act is `storyAct` / `storyActTitle`, the cap `storyCap`.
  `snapshot().story` is true for a story run and false everywhere else.
- The act's `colour` lands on `snapshot().doorColour`, which is what render.js already tints authored plain bricks
  with. `snapshot().door` stays **null**: a story run is not a door run and nothing keyed on `door` changes.
- The act's `words` become the board's word bricks, exactly as a door's words do.
- Each board's `twist` runs through the existing twist seam (`twists/CONTRACT.md`), including the automatic
  `crumble` whenever a board lays clay.

## 5. Events (fixed; station.js and the act lanes key on exactly these)

| event | data | when |
| --- | --- | --- |
| `actStart` | `{ act, title, wall, cap, colour }` | the first wall of an act, before `storyWall` |
| `storyWall` | `{ wall, of, board, name, act, twist, family, house }` | every wall, as it is built |
| `storyClear` | `{ wall, house }` | the story is over. `house: true` = it ended on the house finale |

**Story events are QUEUED and flushed at the top of the next `step()`.** The first wall is built inside
`createGame`, before the caller holds the game object, so emitting there would reach a listener that cannot yet
call `snapshot()`. A test that wants the events must call `step()` at least once (`game.step(0)` is enough).

`storyClear` fires once per run, from whichever of these happens first:
- the last authored wall of the story is cleared (`house: false`), or
- the house finale enters its `outro` phase (`house: true`).

When it ends on the house finale the office pan out is ALREADY PLAYING behind the event. `playStoryEnding` is
what runs over it.

## 6. The saturation cap

`addSat` is clamped to the playing act's `cap`. The clamp NEVER lowers `g.sat` that is already higher (a dev
`?sat=` or a carried-over act), it only refuses to raise it past the cap. The juice ladder itself
(`SPEC.md` rungs at 0.1 steps) is not touched: the story just spends it more slowly.

## 7. The ending, `story/ending.js` (act 5 lane)

```js
export async function playStoryEnding(host, opts) -> boolean
```

`host` = `{ el, canvas, reduced, actions, officeEnding, paintCard }`; `opts` = `{ wall, house }`.
Return **true** if you took the ending over, **false** or nothing to let station.js run its default card. The
scaffold stub returns false for the house case (the office ending owns the screen) and paints the ordinary
ending card otherwise. The new beat (the 0.9 crack across the grey office still, BREAK OUT stamped once, cut to
black, then the card) goes inside this function and nowhere else. The house game's own ending stays untouched.

## 8. The two story twists

`stare` (act 3) and `shells` (act 4) are already registered in `twists/index.js`, `render.js`, `cues.js` and
`reactions.js` as stubs, so a lane never edits an import list. They follow `twists/CONTRACT.md` in full: same
module shape, same `ctx`, same render hooks, same cue and reaction maps. Their event names are fixed:

- `stare`: `stareOn {x,y}` `stareJudge {x,y,n}` `stareOff {}`. A judged brick carries the flag `judged`
  (one extra hit, drawn desaturated). `stare` exports `paintsBrick = ['judged']` so its `brick` painter is
  offered the bricks it flags.
- `shells`: `shellTouch {x,y}` `shellPop {x,y,left}`. Shell state lives on `g.shells`; the ball passes THROUGH a
  shell. Neither twist may touch paddle control or cost a ball.

## 9. Progress and the picker (station.js, scaffold-owned)

localStorage `bo.story.v1` = `{ wall, cleared }` through the station's own `store` helper. `cleared` is the
highest wall finished, `wall` is where Continue picks up. Act one is always open; act N opens when the last wall
of act N-1 is cleared (`actUnlocked`). `?story=1` continues, `?story=1&wall=N` starts at N, `?story=1&unlock=1`
opens every act chip, `?board=st_notice_03` opens one story board on its own and loops it.

## 10. Rules that still apply

Plain ES modules, no dependencies. No em-dashes or en-dashes. Non-ASCII as escapes or entities. Pure sim files
carry no DOM, no clock and no `Math.random`. Reduced motion (`snap.reduced`) keeps every state readable with no
wobble, no shake and no bursts. Audio follows AGENTS.md "Audio design rules learned from Breakout": pitched cues
from `pentatonic()` + `ROOT_HZ` landing on `quantise()`, impacts at `synth.now`, quiet and short, failure
subtracts and never punishes.
