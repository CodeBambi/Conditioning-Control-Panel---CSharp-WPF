import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';

// A DOM small enough to be honest about: only what room/entry.js actually touches. The point of this
// file is to RUN playEntry, not to read it. #1371 shipped a render loop that threw every frame and the
// whole 92-file suite stayed green, because every test read modules instead of executing them.
function stubDom() {
  const timers = [];
  let now = 0;
  const el = (tag) => {
    const node = {
      tagName: tag, children: [], parentNode: null, style: {}, dataset: {}, attrs: {},
      classes: new Set(),
      classList: {
        add: (...c) => c.forEach((x) => node.classes.add(x)),
        remove: (...c) => c.forEach((x) => node.classes.delete(x)),
        contains: (c) => node.classes.has(c),
      },
      get className() { return [...node.classes].join(' '); },
      set className(v) { node.classes = new Set(String(v).split(/\s+/).filter(Boolean)); },
      get isConnected() { return !!node.parentNode; },
      setAttribute: (k, v) => { node.attrs[k] = v; },
      appendChild: (c) => { c.parentNode = node; node.children.push(c); return c; },
      append: (...cs) => cs.forEach((c) => node.appendChild(c)),
      remove: () => { node.parentNode?.children.splice(node.parentNode.children.indexOf(node), 1); node.parentNode = null; },
    };
    return node;
  };
  const stage = el('main');
  const body = el('body');
  body.parentNode = body;   // the body is always attached
  globalThis.document = { body, createElement: el, getElementById: (id) => (id === 'br-stage' ? stage : null) };
  globalThis.matchMedia = () => ({ matches: false });
  globalThis.setTimeout = (fn, ms) => { timers.push({ at: now + (ms || 0), fn }); return timers.length; };
  globalThis.requestAnimationFrame = (fn) => { timers.push({ at: now + 16, fn }); return timers.length; };
  const run = (to) => {
    now = to;
    for (;;) {
      const i = timers.findIndex((t) => t.at <= now);
      if (i < 0) return;
      const [t] = timers.splice(i, 1);
      t.fn();
    }
  };
  return { body, stage, run };
}

const { playEntry, ENTRY_TIMING } = await (async () => {
  stubDom();
  return import('../entry.js');
})();

test('the shutter shuts, tears, opens and takes itself away', () => {
  const { body, stage, run } = stubDom();
  const cover = playEntry(false);

  // It covers the loading card before it reports the card is safe to remove.
  assert.ok(cover > 0 && cover < ENTRY_TIMING.SHUT_MS + ENTRY_TIMING.SLATS * ENTRY_TIMING.STAGGER_MS,
    `cover ${cover} should land just after the slats meet`);

  const node = body.children.find((c) => c.classes.has('br-entry'));
  assert.ok(node, 'the shutter is in the document');
  assert.equal(node.children[0].children.length, ENTRY_TIMING.SLATS);
  assert.ok(node.classes.has('br-entry-shut'));
  // Every slat carries its own delay, or the shutter is one sheet and not slats.
  const delays = new Set(node.children[0].children.map((s) => s.style.animationDelay));
  assert.ok(delays.size > 1, 'slats are staggered');

  run(cover);
  assert.ok(node.classes.has('br-entry-glitch'), 'it tears while it is closed, not while it is open');

  run(cover + ENTRY_TIMING.GLITCH_MS);
  run(cover + ENTRY_TIMING.GLITCH_MS + 48);   // the module waits one frame so the open keyframes restart
  assert.ok(node.classes.has('br-entry-open'), 'it opens');
  assert.ok(!node.classes.has('br-entry-glitch'), 'the tear is over before the room is visible');
  assert.ok(stage.classes.has('br-arrive'), 'the stage rolls out of the zoom as the slats part');

  run(10_000);
  assert.equal(node.isConnected, false, 'the shutter removes itself, it does not sit over the room');
  assert.equal(stage.classes.has('br-arrive'), false, 'and the stage is left with no transform');
  assert.equal(body.children.length, 0);
});

test('a still room gets no shutter and no roll', () => {
  const { body, stage, run } = stubDom();
  assert.equal(playEntry(true), 0);
  run(10_000);
  assert.equal(body.children.length, 0);
  assert.equal(stage.classes.has('br-arrive'), false);
});

test('reduced motion gets no shutter', () => {
  const { body } = stubDom();
  globalThis.matchMedia = () => ({ matches: true });
  assert.equal(playEntry(false), 0);
  assert.equal(body.children.length, 0);
});

test('intro.js imports the shutter AND calls it', () => {
  const src = readFileSync(new URL('../intro.js', import.meta.url), 'utf8');
  assert.match(src, /import\s*\{[^}]*\bplayEntry\b[^}]*\}\s*from\s*'\.\/entry\.js'/,
    'intro.js imports playEntry');
  assert.match(src, /\bplayEntry\s*\(/, 'intro.js actually calls playEntry');
  // The failure path must not open a shutter on a room that never built.
  assert.match(src, /immediate\s*\?\s*0\s*:\s*playEntry/, 'the immediate path skips the shutter');
});

test('the stylesheet is loaded and defines every class the module sets', () => {
  const html = readFileSync(new URL('../../index.html', import.meta.url), 'utf8');
  assert.match(html, /href="room\/entry\.css"/, 'index.html loads room/entry.css');
  const css = readFileSync(new URL('../entry.css', import.meta.url), 'utf8');
  for (const c of ['br-entry', 'br-entry-slat', 'br-entry-slats', 'br-entry-tear',
                   'br-entry-shut', 'br-entry-open', 'br-entry-glitch', 'br-arrive']) {
    assert.ok(css.includes('.' + c), `entry.css styles .${c}`);
  }
  for (const k of ['br-entry-shut', 'br-entry-open', 'br-entry-tear', 'br-entry-split', 'br-arrive']) {
    assert.ok(css.includes('@keyframes ' + k), `entry.css defines @keyframes ${k}`);
  }
  // Reduced motion must switch the whole thing off, not merely shorten it.
  assert.match(css, /prefers-reduced-motion:\s*reduce/);
});
