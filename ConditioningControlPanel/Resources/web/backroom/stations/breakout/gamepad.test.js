import test from 'node:test';
import assert from 'node:assert/strict';
import { createGamepad, DEADZONE } from './gamepad.js';

function rig() {
  const pad = { index: 0, connected: true, axes: [0, 0], buttons: Array.from({ length: 16 }, () => ({ pressed: false, value: 0 })) };
  let list = [pad];
  const nav = { getGamepads: () => list };
  const input = { x: 400, left: false, right: false, launch: false };
  const gp = createGamepad({ nav });
  return { pad, nav, input, gp, press: (i, on = true) => { pad.buttons[i] = { pressed: on, value: on ? 1 : 0 }; }, unplug: () => { list = [null]; } };
}
const PLAY = { menuOpen: false, paused: false, suspended: false };

test('no pad connected is null and touches nothing', () => {
  const input = { x: 400, left: true, right: false, launch: false };
  assert.equal(createGamepad({ nav: { getGamepads: () => [null, null] } }).poll(input, PLAY), null);
  assert.equal(createGamepad({ nav: {} }).poll(input, PLAY), null);
  assert.deepEqual(input, { x: 400, left: true, right: false, launch: false });
});

test('the stick steers past the deadzone only, and hands the paddle back to the keys when it centres', () => {
  const r = rig();
  r.pad.axes[0] = DEADZONE - 0.05;
  assert.equal(r.gp.poll(r.input, PLAY).moved, false);
  assert.deepEqual(r.input, { x: 400, left: false, right: false, launch: false }, 'inside the deadzone the mouse keeps the paddle');
  r.pad.axes[0] = -0.8; r.gp.poll(r.input, PLAY);
  assert.deepEqual(r.input, { x: null, left: true, right: false, launch: false });
  r.pad.axes[0] = 0.6; r.gp.poll(r.input, PLAY);
  assert.equal(r.input.right, true); assert.equal(r.input.left, false);
  r.pad.axes[0] = 0; r.gp.poll(r.input, PLAY);
  assert.equal(r.input.right, false);
  r.input.left = true;   // an arrow key held with the stick at rest
  r.gp.poll(r.input, PLAY);
  assert.equal(r.input.left, true, 'an idle pad does not fight the keyboard');
});

test('the d-pad steers too', () => {
  const r = rig();
  r.press(14); r.gp.poll(r.input, PLAY);
  assert.equal(r.input.left, true);
  r.press(14, false); r.press(15); r.gp.poll(r.input, PLAY);
  assert.equal(r.input.right, true); assert.equal(r.input.left, false);
});

test('A launches on the press edge only', () => {
  const r = rig();
  r.press(0);
  const first = r.gp.poll(r.input, PLAY);
  assert.equal(r.input.launch, true); assert.equal(first.any, true); assert.equal(first.moved, true);
  r.input.launch = false;
  const held = r.gp.poll(r.input, PLAY);
  assert.equal(r.input.launch, false, 'a held button is one press');
  assert.equal(held.any, false);
  r.press(0, false); r.gp.poll(r.input, PLAY);
  r.press(0); r.gp.poll(r.input, PLAY);
  assert.equal(r.input.launch, true);
});

test('Start pauses on the edge, A resumes a pause, and both start the game from the menu', () => {
  const r = rig();
  r.press(9);
  assert.equal(r.gp.poll(r.input, PLAY).pause, true);
  assert.equal(r.gp.poll(r.input, PLAY).pause, false);
  r.press(9, false); r.gp.poll(r.input, PLAY);

  r.press(0);
  const resume = r.gp.poll(r.input, { ...PLAY, paused: true });
  assert.equal(resume.pause, true); assert.equal(r.input.launch, false, 'the resume press is not a launch');
  r.press(0, false); r.gp.poll(r.input, PLAY);

  r.press(0); r.pad.axes[0] = 1;
  const menu = r.gp.poll(r.input, { ...PLAY, menuOpen: true });
  assert.deepEqual([menu.start, menu.pause, menu.any], [true, false, true]);
  assert.deepEqual(r.input, { x: 400, left: false, right: false, launch: false }, 'the menu holds the paddle');
  r.press(0, false); r.gp.poll(r.input, PLAY);

  r.press(9);
  const asleep = r.gp.poll(r.input, { ...PLAY, suspended: true });
  assert.deepEqual([asleep.pause, asleep.start], [false, false]);
});

test('a pad unplugged mid-steer lets go of the paddle', () => {
  const r = rig();
  r.pad.axes[0] = 1; r.gp.poll(r.input, PLAY);
  assert.equal(r.input.right, true);
  r.unplug();
  assert.equal(r.gp.poll(r.input, PLAY), null);
  assert.equal(r.input.right, false);
});
