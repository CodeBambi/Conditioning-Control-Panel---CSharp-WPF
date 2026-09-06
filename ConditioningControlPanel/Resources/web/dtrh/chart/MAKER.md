# Track Maker (`chart/maker.html`)

One screen that turns an mp3 plus its aligned transcript into a race track chart for
The Caucus Race. One page, one job: no tabs, no options panel, no rules table. Pick the
file, the maker finds the words, places a run of bubbles at every trigger it heard, and
the author plays it and slides what is off.

## Files

| file | what |
|------|------|
| `maker.html` | the whole page: a top bar, three lines under one ruler, a side panel, a bottom bar |
| `maker.css` | the look. Its own tokens, shared with nothing. Nothing on the page is under 13 px |
| `maker/model.js` | pure: recipes, placement, the min gap clamp, the pick line, the chart it writes |
| `maker/audio.js` | decode once, keep the peaks, drop the buffer, play through an `<audio>` |
| `maker/words.js` | find the words file, read it, run `maker/triggers.js detect` over the catalogue |
| `maker/timeline.js` | the three lines, the ruler, the waveform, the playhead |
| `maker/pick.js` | picking, sliding, walls, the effect grid, undo |
| `maker/save.js` | the chart file it writes, and the autosave that survives a reload |
| `smoke/maker-check.mjs` | the pure half under node: placement, clamping, the export shape |

Shared, never copied: `editor/audio.js` (`peaksFromChannels`, `hashFile`),
`maker/triggers.js` (`detect`, `TRIGGER_SETS`, `labelInk`) over the catalogue in
`editor/triggerSets.js`, and `race/chart.js` (`normalizeChart`, in the smoke).

## The flow

1. Pick an mp3 (button or drop). It is hashed (CHART.md's recipe), decoded once in a
   throwaway 16 kHz context for the min/max peaks, and then the AudioBuffer is dropped.
   Playback is an object URL on an `<audio>`; the browser owns the clock.
2. The words are looked for at `./words/<stem>.words.json`, then by name and finally by
   hash through an optional `./words/index.json` manifest. Nothing found and the page
   says `no words for this file yet. drop its .words.json here.` and takes a dropped one.
   A words file whose `source.hash` is not this audio's still loads, with a line saying so.
3. Every set in the trigger catalogue that the file actually says gets a row in the side
   panel, most said first: tick box, name, `N in file`, the recipe as beads, `change`.
   Ticked to start: good girl, bambi sleep, drop, blank, empty, obey.
4. A ticked set places its recipe at every hit, first bubble at `word - 0.15 s`, spaced
   `max(min gap, recipe gap)`. Changing a recipe or a tick re-places that trigger from
   scratch, hand moved bubbles of it included, and the status line says so.

## The pieces

- `hit` = `{ id: 'h:<setId>:<n>', t, setId, n }`, one per trigger word heard.
- `bub` = `{ id, t, kind, eff, group, trig }`. `kind` is a race bubble id
  (`subliminal`, `flash`, `braindrain`, `pink`) or `'wall'`; a wall carries `eff`, one of
  `melt, blink, blackout, shake, snap, flash`. `group` is the hit it belongs to, or null
  for a wall dropped by hand.
- Six canned recipes, the whole choice there is: shimmer (`S F S F S F wall:melt`), drain
  (`BD BD wall:blackout`), blink (`wall:blink`), snap (`F wall:snap`), triple (`S S S`),
  pink wash (`pink pink wall:flash`).

## Picking and sliding

Click picks one, ctrl+click adds or removes, shift+click picks every one like it (a wall:
every wall on that effect), a band picks its group and shift+band every group of that
trigger, a tag picks that word and shift+tag every word of that trigger. Drag anything
picked, or press left / right (0.1 s, shift 1 s), and the whole pick slides together,
tags included. Picked bubbles never land closer than the min gap to one that stays put:
the delta is clamped, so a pick pushed into a neighbour stops rather than stacking.
`del` removes the pick, `esc` clears it, ctrl+z undoes (50 deep), ctrl+shift+z or ctrl+y
redoes. The min gap control changes what is placed and dragged next, never what is
already down.

Clicking a wall that is already the only thing picked opens the effect grid; one tap sets
every picked wall. `w` or the button drops a free wall at the playhead and opens the grid.

## Zoom and pan

`+` / `-` (buttons or keys) zoom around the middle, ctrl+wheel zooms around the pointer
and keeps the time under it still, a plain wheel pans, `home` and `end` jump to the ends.
6 to 120 px per second.

## What it writes

`<stem>.chart.json`, chart JSON v1, the shape `race/chart.js normalizeChart` validates
and `race/cues.js` runs (see `race/CHART.md`):

- one event per bubble, `kind: 'trigger'` (`'mark'` for a free wall), `hand: true`,
  `label` = the trigger name, `cue.spawn: [{ kindId, placement: 'lane', x: 0, h: 1, at: 0 }]`.
  `race/cues.js` stamps `sure: true` on hand spawns, so the density knob leaves them alone.
- one event per wall, `cue.wall: 'pink'` plus `cue.fx: [{ id, strength: 1, dur: FX_DUR[id] }]`.
  `race/cues.js` opens a wall into five road bubbles and one over the top, all stamped
  `sure: true`, and `run.js` plays the frame effect with them.
- `source` = `{ name, hash, durationSec, sampleRate: 16000 }`, so the chart says which file
  it was written against.

The working state also autosaves to `localStorage` under `trackmaker:<audio hash>` and comes
back when the same mp3 is picked again (`picked up where you left off`). `start over` in the
panel clears it and places everything again from the recipes.

## Performance

The owner's browser is Firefox and a busy timeline lags there, so: per frame the page
writes the playhead transform and the time, and the time only when the string changed.
Rows and the waveform are rebuilt on a view change or a track change, never on the clock.
Only what is on screen is built. The decoded audio is never kept. No `getComputedStyle`
in a draw path.

## Checks

`node chart/smoke/maker-check.mjs` from `Resources/web/dtrh` covers the pure half:
placement spacing, the min gap clamp, `alike`, the recipe catalogue, and the exported
chart through `normalizeChart`. `chart/smoke/tokens-check.mjs` holds the 13 px floor in
`maker.css` and the house rules over every file under `chart/`.

`chart/words/` is where the aligned transcripts live. It is gitignored and excluded from
the csproj: those files are someone's session audio turned into text and they never go
into the repo or the installer.
