import {test} from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import {createGame} from './game.js';
import {checkpointFromSnapshot} from './endless-save.js';
import {planPulse} from './haptics.js';

// Skip this board runs from the pause menu, and the frame loop that moves the Endless
// checkpoint (syncEndless) is held while paused. "Save and menu" or closing the game then
// wrote the board the player had skipped, and Continue put them back on it, 20 runs out of 20
// (bug hunt 2026-09-29, CHESS-4).

test('after a skip, the checkpoint the game describes is the board skipped to', () => {
  const game = createGame({endless: true, seed: 548549035, rng: () => .6});
  const before = checkpointFromSnapshot(game.snapshot());
  assert.equal(before.from, 0);
  assert.equal(game.skipBoard(), true);
  const after = checkpointFromSnapshot(game.snapshot());
  assert.equal(after.from, 1);
  assert.equal(after.seed, before.seed);
});

test('the skip moves the saved checkpoint at once, while the game is still paused', () => {
  const src = readFileSync(new URL('./station.js', import.meta.url), 'utf8');
  const at = src.indexOf("action==='skip-board'");
  assert.ok(at >= 0, 'the skip-board action moved: update this test with it');
  const block = src.slice(at, src.indexOf('\n      }', at));
  const skip = block.indexOf('game?.skipBoard()');
  const sync = block.indexOf('syncEndless(game.snapshot())');
  assert.ok(skip >= 0 && sync > skip, 'the skip handler syncs the checkpoint right after skipBoard()');
});

// A skip is not a clear: it fired the whole wall-cleared show, the pink wash with a
// picture (which pays picture XP on the desktop), the chime, the haptic pulse and the
// canvas bursts, and could be repeated from the pause menu (bug hunt 2026-09-29, CHESS-5).

test('a skipped board gets no haptic pulse; a cleared one still does', () => {
  const colour = {state: 'colour', sat: .8};
  assert.equal(planPulse('wall', {skipped: true}, colour), null);
  assert.ok(planPulse('wall', {}, colour).level >= .75);
});

test('a skipped board gets no chime, no host wash and no canvas bursts', () => {
  const station = readFileSync(new URL('./station.js', import.meta.url), 'utf8');
  const wall = station.slice(station.indexOf("case 'wall':"), station.indexOf("case 'crack':"));
  assert.match(wall, /!d\.skipped && cue\('wall'\)\) au\('wallCleared'\)/);
  assert.match(wall, /onWall\([^\n]*d\.skipped\)/);
  const onWall = station.slice(station.indexOf('function onWall('), station.indexOf('/* ---', station.indexOf('function onWall(')));
  assert.match(onWall, /function onWall\(n, mantra, skipped = false\)/);
  assert.match(onWall, /if \(!colour \|\| skipped\)/);
  assert.match(onWall, /media\.redeal\(\)/, 'the picture redeal still runs on a skip');
  const render = readFileSync(new URL('./render.js', import.meta.url), 'utf8');
  assert.match(render, /\} else if \(name === 'wall' && !d\.skipped\) \{/);
});
