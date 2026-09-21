/* The story's own ending beat: the crack, the stamp, the black, then the card.
 * The pure half is the real subject; the DOM half is driven through a stub.
 */
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { playStoryEnding, crackPath, crackRng, endingTimeline, beatAt, CRACK_SEED } from './ending.js';

/* ------------------------------------------------------------- the crack */

test('one seed is always the same fracture, and two seeds are not', () => {
  const a = crackPath(CRACK_SEED, 1280, 720);
  const b = crackPath(CRACK_SEED, 1280, 720);
  assert.deepEqual(JSON.parse(JSON.stringify(a)), JSON.parse(JSON.stringify(b)));
  const other = crackPath(CRACK_SEED + 1, 1280, 720);
  assert.notDeepEqual(JSON.parse(JSON.stringify(a)), JSON.parse(JSON.stringify(other)));
  const rng = crackRng(9);
  for (let i = 0; i < 50; i++) { const v = rng(); assert.ok(v >= 0 && v < 1); }
});

test('the crack stays inside the frame, whatever the frame is', () => {
  for (const [w, h] of [[1280, 720], [390, 844], [2560, 1440], [320, 200]]) {
    const lines = crackPath(CRACK_SEED, w, h);
    assert.ok(lines.length >= 2, 'at least the two trunks');
    for (const pts of lines) {
      assert.ok(pts.length >= 2, 'a line is a line');
      for (const [x, y] of pts) {
        assert.ok(x >= 0 && x <= w, 'x ' + x + ' inside ' + w);
        assert.ok(y >= 0 && y <= h, 'y ' + y + ' inside ' + h);
      }
    }
  }
});

test('the fracture branches: the seed the ending draws with forks off its trunks', () => {
  const lines = crackPath(CRACK_SEED, 1280, 720);
  const trunks = lines.filter(p => !p.depth), forks = lines.filter(p => p.depth > 0);
  assert.ok(trunks.length >= 2, 'two trunks cross the still');
  assert.ok(forks.length >= 3, 'and it branches: ' + forks.length + ' forks');
  for (const fork of forks) assert.ok(fork.start > 0 && fork.start < 1, 'a fork starts partway through the growth');
});

/* ---------------------------------------------------------- the timeline */

test('the beats run crack, stamp, black, card, in that order and nowhere else', () => {
  const tl = endingTimeline(false);
  assert.ok(tl.crackAt <= tl.stampAt);
  assert.ok(tl.stampAt < tl.blackAt);
  assert.ok(tl.blackAt < tl.cardAt);
  assert.equal(beatAt(0, tl), 'crack');
  assert.equal(beatAt(tl.crackGrow / 2, tl), 'crack');
  assert.equal(beatAt(tl.stampAt, tl), 'stamp');
  assert.equal(beatAt(tl.blackAt - 0.01, tl), 'stamp');
  assert.equal(beatAt(tl.blackAt, tl), 'black');
  assert.equal(beatAt(tl.cardAt, tl), 'card');
  assert.equal(beatAt(99, tl), 'card');
  assert.ok(tl.crackGrow > 0.8 && tl.crackGrow < 2, 'the crack grows over about a second');
  assert.ok(tl.cardAt - tl.blackAt >= 0.6, 'a real beat of black before the card');
});

test('reduced motion keeps the order and drops the growth and the shake', () => {
  const tl = endingTimeline(true);
  assert.equal(tl.crackGrow, 0, 'the crack is simply there');
  assert.equal(tl.stampShake, 0, 'nothing shakes');
  assert.equal(tl.stampPunch, 0, 'nothing punches in');
  assert.ok(tl.crackAt <= tl.stampAt && tl.stampAt < tl.blackAt && tl.blackAt < tl.cardAt);
  assert.equal(beatAt(0, tl), 'crack');
  assert.equal(beatAt(tl.stampAt, tl), 'stamp');
  assert.equal(beatAt(tl.blackAt, tl), 'black');
  assert.equal(beatAt(tl.cardAt, tl), 'card');
  assert.ok(tl.cardAt < endingTimeline(false).cardAt, 'and it is shorter than the full beat');
});

/* --------------------------------------------------------- the hand-over */

test('playStoryEnding refuses cleanly when it has nothing to draw on', async () => {
  assert.equal(await playStoryEnding(null, { house: true }), false);
  assert.equal(await playStoryEnding({}, { house: true }), false);
  assert.equal(await playStoryEnding({ el: {} }, { house: true }), false, 'no canvas, no take-over');
  assert.equal(await playStoryEnding({ el: {}, canvas: {} }, { house: true }), false, 'no document, no take-over');
});

test('an authored last wall is not this ending: the ordinary card wins', async () => {
  const host = { el: stubEl(), canvas: {}, actions: stubActions() };
  assert.equal(await playStoryEnding(host, { house: false }), false);
  assert.equal(await playStoryEnding(host, {}), false);
  assert.equal(host.el.children.length, 0, 'and nothing was drawn over anything');
});

/* A document just real enough for one canvas layer. */
function stubCanvas() {
  const ops = [];
  const g = new Proxy({ ops }, {
    get(t, k) {
      if (k === 'ops') return ops;
      return (...args) => { ops.push([String(k), ...args]); };
    },
    set() { return true; },
  });
  return { className: '', style: {}, width: 0, height: 0, ops,
    setAttribute() {}, getContext: () => g, remove() { this.removed = true; } };
}
function stubEl() {
  const children = [];
  const canvas = stubCanvas();
  return { children, canvas,
    ownerDocument: { createElement: () => canvas },
    getBoundingClientRect: () => ({ width: 1280, height: 720 }),
    append(node) { children.push(node); } };
}
function stubActions() {
  return { hidden: false, querySelector: () => ({ focus() {} }) };
}

test('the house ending is taken over, waits out the clip, then hands the buttons back', async () => {
  const el = stubEl();
  const actions = stubActions();
  let state = 'playing';
  const office = { get state() { return state; } };
  const host = { el, canvas: {}, reduced: true, actions, officeEnding: office };
  const run = playStoryEnding(host, { wall: 28, house: true });
  await new Promise(r => setTimeout(r, 120));
  assert.equal(actions.hidden, true, 'the Play again buttons stay down while the clip runs');
  assert.equal(el.children.length, 0, 'and the beat has not started');
  state = 'done';
  assert.equal(await run, true, 'the ending owns the screen');
  assert.equal(actions.hidden, false, 'the buttons come back');
  const ops = el.canvas.ops.map(o => o[0]);
  assert.ok(ops.includes('stroke'), 'the crack was stroked');
  assert.ok(el.canvas.ops.some(o => o[0] === 'fillText' && o[1] === 'BREAK OUT'), 'BREAK OUT was stamped');
  assert.ok(ops.includes('fillRect'), 'and it cut to black');
  assert.equal(el.canvas.removed, true, 'the layer is gone before the card');
});
