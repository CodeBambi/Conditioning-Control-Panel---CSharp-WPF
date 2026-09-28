/* node --test page-fx.test.js - the desktop's in-window effects: every id draws in the layer and
 * clears after its life, cancelAll clears everything, an unknown key draws no picture, and the
 * router keeps the five ids off the host on the desktop and leaves the web alone. */
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { createPageFx, routeFx, PAGE_FX, WASH_MS, WORD_MS } from './page-fx.js';

function fakeDoc() {
  const make = (tag) => {
    const n = {
      tagName: tag.toUpperCase(), className: '', style: {}, childNodes: [], parentNode: null, attrs: {}, textContent: '',
      width: 0, height: 0,
      appendChild(c) { c.parentNode = n; n.childNodes.push(c); return c; },
      removeChild(c) { const i = n.childNodes.indexOf(c); if (i >= 0) n.childNodes.splice(i, 1); c.parentNode = null; return c; },
      get firstChild() { return n.childNodes[0] || null; },
      setAttribute(k, v) { n.attrs[k] = v; }, removeAttribute(k) { delete n.attrs[k]; },
      addEventListener() {}, getBoundingClientRect() { return { left: 0, top: 0, width: 1000, height: 600 }; },
      play() { return Promise.resolve(); }, pause() {},
    };
    return n;
  };
  return { hidden: false, createElement: make, body: make('body') };
}
function fakeClock() {
  let t = 0, id = 0; const q = new Map();
  return {
    set(fn, ms) { const k = ++id; q.set(k, { at: t + ms, fn }); return k; },
    clear(k) { q.delete(k); },
    advance(ms) {
      const end = t + ms;
      for (;;) {
        let next = null;
        for (const [k, v] of q) if (v.at <= end && (!next || v.at < next[1].at)) next = [k, v];
        if (!next) break;
        q.delete(next[0]); t = next[1].at; next[1].fn();
      }
      t = end;
    },
    pending: () => q.size,
  };
}
const flush = () => new Promise(r => setImmediate(r));
function rig({ urls = { g1: 'https://ccp.assets/a.gif', c1: 'https://ccp.assets/.temp/b.webm' }, words = [{ key: 'w1', text: 'SINK' }, { key: 'w2', text: 'DROP' }] } = {}) {
  const doc = fakeDoc(), clock = fakeClock(), parent = doc.createElement('section');
  const painted = [];
  const kit = { paint(c, name) { painted.push(name); return true; }, setStill() {}, dispose() { kit.disposed = true; } };
  const media = { urlOf: k => urls[k] || null, words };
  const fx = createPageFx({
    parent, doc, media: () => media, setTimer: clock.set, clearTimer: clock.clear,
    raf: () => 0, caf: () => {}, now: () => 0, loadLoom: async () => ({ createLoomKit: () => kit }),
  });
  const kids = () => (fx.layer ? fx.layer.childNodes : []);
  return { fx, clock, parent, painted, kit, kids };
}

test('the five ids are the page ids', () => {
  assert.deepEqual([...PAGE_FX].sort(), ['fx.gif_from', 'fx.loom_spiral', 'fx.sub_pair', 'fx.sub_single', 'fx.wash']);
});

test('wash draws a tint and the dealt picture inside the station, then clears', () => {
  const { fx, clock, parent, kids } = rig();
  assert.equal(fx.fire('fx.wash', ['g1'], { color: '#ff5fa2', strength: 0.55 }), true);
  assert.equal(parent.childNodes[0].className, 'bo-pagefx');
  const k = kids();
  assert.equal(k.length, 2);
  assert.equal(k[0].className, 'bo-pagefx-wash'); assert.equal(k[0].style.background, '#ff5fa2');
  assert.equal(k[1].tagName, 'IMG'); assert.equal(k[1].src, 'https://ccp.assets/a.gif');
  clock.advance(WASH_MS * 1.6 + 30);
  assert.equal(kids().length, 0);
});

test('gif_from grows the picture and a clip plays in a muted video', () => {
  const { fx, clock, kids } = rig();
  fx.fire('fx.gif_from', ['c1'], { from: { x: 10, y: 20, w: 100, h: 80 }, ms: 1500, scale: 0.8 });
  assert.equal(kids().length, 1);
  assert.equal(kids()[0].tagName, 'VIDEO'); assert.equal(kids()[0].muted, true);
  clock.advance(1600);
  assert.equal(kids().length, 0);
});

test('an unknown key runs the beat with no picture and does not throw', () => {
  const { fx, clock, kids } = rig();
  assert.equal(fx.fire('fx.gif_from', ['nope'], { ms: 1500, scale: 0.8 }), true);
  assert.equal(kids().length, 0);
  fx.fire('fx.wash', ['nope'], { color: '#ff5fa2', strength: 1 });
  assert.equal(kids().length, 1);
  fx.fire('fx.wash', [], null);
  clock.advance(5000);
  assert.equal(kids().length, 0);
});

test('loom spiral paints the Loom, holds, then goes', async () => {
  const { fx, clock, kids, painted } = rig();
  fx.fire('fx.loom_spiral', [], { preset: 'screen', ms: 4200, alpha: 0.6 });
  assert.equal(kids()[0].tagName, 'CANVAS');
  await flush();
  assert.ok(painted.length >= 1);
  clock.advance(1250 + 4200 * 1.8 + 600);
  assert.equal(kids().length, 0);
});

test('sub_single spells the dealt word; sub_pair spells two then spirals', async () => {
  const { fx, clock, kids } = rig();
  fx.fire('fx.sub_single', ['w1']);
  clock.advance(0);
  assert.equal(kids().length, 1); assert.equal(kids()[0].textContent, 'SINK');
  clock.advance(WORD_MS + 30);
  assert.equal(kids().length, 0);
  fx.fire('fx.sub_pair', ['w2']);
  clock.advance(600);
  assert.equal(kids().filter(n => n.className === 'bo-pagefx-word').length, 2);
  clock.advance(500);
  await flush();
  assert.equal(kids().filter(n => n.tagName === 'CANVAS').length, 1);
});

test('sub_pair with wordsShown skips the words; no words dealt means no word node', () => {
  const { fx, clock, kids } = rig({ words: [] });
  fx.fire('fx.sub_single', ['w1']);
  clock.advance(10);
  assert.equal(kids().length, 0);
  fx.fire('fx.sub_pair', ['w1'], { wordsShown: true });
  clock.advance(1);
  assert.equal(kids().filter(n => n.className === 'bo-pagefx-word').length, 0);
});

test('cancelAll removes every node and every pending beat; dispose unmounts', async () => {
  const { fx, clock, parent, kids, kit } = rig();
  fx.fire('fx.wash', ['g1'], { color: '#ff5fa2', strength: 1 });
  fx.fire('fx.loom_spiral', [], { ms: 4200, alpha: 0.6 });
  fx.fire('fx.sub_pair', ['w1']);
  await flush();
  assert.ok(kids().length >= 3);
  fx.cancelAll();
  assert.equal(kids().length, 0);
  assert.equal(clock.pending(), 0);
  clock.advance(10000);
  assert.equal(kids().length, 0);
  fx.dispose();
  assert.equal(parent.childNodes.length, 0);
  assert.equal(kit.disposed, true);
  assert.equal(fx.fire('fx.wash', ['g1'], {}), false);
});

test('a foreign id draws nothing', () => {
  const { fx, kids } = rig();
  assert.equal(fx.fire('fx.melt', [], {}), false);
  assert.equal(kids().length, 0);
});

test('routeFx: hosted keeps the five ids off the host and passes the rest; the web is untouched', () => {
  const hostCalls = [], pageCalls = [];
  const host = (id, s, a) => { hostCalls.push(id); return 'host'; };
  const page = { fire: (id) => { pageCalls.push(id); return true; } };
  const desk = routeFx({ hosted: true, fx: host, page });
  for (const id of PAGE_FX) desk(id, [], {});
  desk('fx.melt', [], {}); desk('fx.jackpot', ['g1']);
  assert.deepEqual(pageCalls, [...PAGE_FX]);
  assert.deepEqual(hostCalls, ['fx.melt', 'fx.jackpot']);
  assert.equal(routeFx({ hosted: false, fx: host, page }), host);
  assert.equal(routeFx({ hosted: false, fx: null, page }), null);
  const noHost = routeFx({ hosted: true, fx: null, page });
  assert.equal(noHost('fx.melt'), undefined);
  assert.equal(noHost('fx.wash', [], {}), true);
});
