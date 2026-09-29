import {test} from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import {createGame} from './game.js';
import {checkpointFromSnapshot} from './endless-save.js';

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
