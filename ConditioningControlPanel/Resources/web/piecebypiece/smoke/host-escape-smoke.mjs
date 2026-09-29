/* ============================================================================
 * smoke/host-escape-smoke.mjs - the Escape the panel kept reaches the board once.
 *
 * Escape is the default panic key. With the board in front the panel keeps a
 * first Escape as the game's pause (PanicPolicy.GameClaimsEscapeAsPause) and
 * hands it to the page as { type: 'pbp:escape' }
 * (PieceByPieceHostService.PostKeptEscape): the board pauses on its own keydown,
 * which never comes while its WebView2 is out of keyboard focus (the title bar
 * clicked), so a kept press used to pause nothing and panic nothing. A focused
 * board hears the real key as well, in either order, and one press is handled
 * once (twice would pause and then leave):
 *   A  unfocused: the frame alone plays one Escape through the page's listeners.
 *   B  focused, the key first (the usual order): the frame is dropped.
 *   C  focused, the frame first: it waits, the key lands, the frame is dropped.
 *   D  on the pause card a later kept press leaves, once, focused or not.
 *   E  a part of the page that keeps the real key for itself (the promotion
 *      picker, the door) does not hide it from the dedupe.
 *   F  the played Escape: its target, its shape, a duplicate frame, dispose.
 *   G  boot.js wiring.
 *
 *   node smoke/host-escape-smoke.mjs        (no server, no browser)
 * ==========================================================================*/

import { readFileSync } from 'node:fs';
import { createHostEscape, HOST_ESCAPE, LOOK_BACK_MS, LOOK_AHEAD_MS } from '../ui/host-escape.js';

let failed = 0;
let passed = 0;
function expect(cond, name) {
  if (cond) { passed++; console.log('  ok   ' + name); }
  else { failed++; console.error('  FAIL ' + name); }
}

class FakeKey {
  constructor(type, init = {}) {
    this.type = type;
    Object.assign(this, init);
    this.stopped = false;
    this.defaultPrevented = false;
  }
  stopImmediatePropagation() { this.stopped = true; }
  stopPropagation() { this.stopped = true; }
  preventDefault() { this.defaultPrevented = true; }
}

/** A page: window capture listeners, then bubble listeners, the order a keydown meets them. */
function makePage() {
  let t = 0;
  const timers = new Map();
  let nextId = 1;
  const later = (fn, ms) => { const id = nextId++; timers.set(id, { fn, at: t + ms }); return id; };
  const cancel = (id) => { timers.delete(id); };
  const advance = (ms) => {
    const end = t + ms;
    for (;;) {
      let next = null;
      for (const [id, tm] of timers) if (tm.at <= end && (!next || tm.at < next[1].at)) next = [id, tm];
      if (!next) break;
      timers.delete(next[0]);
      t = next[1].at;
      next[1].fn();
    }
    t = end;
  };
  const capture = [];
  const bubble = [];
  const listOf = (opt) => (opt === true || (opt && opt.capture)) ? capture : bubble;
  const win = {
    addEventListener(type, fn, opt) { if (type === 'keydown') listOf(opt).push(fn); },
    removeEventListener(type, fn, opt) { const list = listOf(opt); const i = list.indexOf(fn); if (i >= 0) list.splice(i, 1); },
  };
  const run = (ev) => {
    for (const fn of [...capture]) { fn(ev); if (ev.stopped) return; }
    for (const fn of [...bubble]) { fn(ev); if (ev.stopped) return; }
  };
  const element = (name) => ({
    name,
    got: [],
    dispatchEvent(ev) { this.got.push(ev); ev.target = this; run(ev); return !ev.defaultPrevented; },
  });
  const body = element('body');
  const doc = { body, activeElement: body };
  // A real key: the browser dispatches it at the focused element, the played one is dispatched there too.
  const realKey = (key = 'Escape') => { const ev = new FakeKey('keydown', { key, isTrusted: true }); ev.target = doc.activeElement; run(ev); };
  const hostEscape = createHostEscape({
    win, doc, now: () => t, later, cancel,
    makeEvent: () => new FakeKey('keydown', { key: 'Escape', code: 'Escape', bubbles: true, cancelable: true, isTrusted: false }),
  });
  // boot.js's own Escape: pause a game, and an Escape on the pause card leaves.
  const acts = [];
  let paused = false;
  win.addEventListener('keydown', (e) => {
    if (e.key !== 'Escape') return;
    if (paused) { acts.push('leave'); return; }
    paused = true;
    acts.push('pause');
  });
  return { win, doc, element, realKey, hostEscape, acts, advance, timers, capture, bubble, now: () => t };
}

/* ---- A: unfocused, the frame alone ------------------------------------------ */
{
  const p = makePage();
  const waiting = p.hostEscape.kept();
  expect(waiting === true && p.acts.length === 0, 'the frame waits for a real key before it plays');
  p.advance(LOOK_AHEAD_MS - 1);
  expect(p.acts.length === 0, 'nothing plays inside the wait');
  p.advance(1);
  expect(p.acts.join() === 'pause', 'no real key came: the kept Escape pauses the game');
  p.advance(5000);
  expect(p.acts.join() === 'pause', 'and it plays once');
  expect(LOOK_AHEAD_MS > 0 && LOOK_AHEAD_MS <= 500, 'the wait is short enough to read as the key');
}

/* ---- B: focused, the real key first ------------------------------------------ */
{
  const p = makePage();
  p.realKey();
  p.advance(15);
  const waiting = p.hostEscape.kept();
  p.advance(5000);
  expect(waiting === false && p.acts.join() === 'pause', 'the key came first: the frame is dropped, the game pauses once');

  // A panel stalled behind the key still posts its frame inside the look-back.
  const q = makePage();
  q.realKey();
  q.advance(LOOK_BACK_MS - 1);
  q.hostEscape.kept();
  q.advance(5000);
  expect(q.acts.join() === 'pause', 'a late frame inside the look-back is dropped too');

  // An old real key is not this press: the panel never keeps two presses closer than 2 s.
  const r = makePage();
  r.realKey();
  r.advance(LOOK_BACK_MS);
  r.hostEscape.kept();
  r.advance(LOOK_AHEAD_MS);
  expect(r.acts.join() === 'pause,leave', 'a frame past the look-back plays');
  expect(LOOK_BACK_MS < 2000, 'the look-back stays inside the panel\'s 2 s double tap');
}

/* ---- C: focused, the frame first ---------------------------------------------- */
{
  const p = makePage();
  p.hostEscape.kept();
  p.advance(20);
  p.realKey();
  expect(p.acts.join() === 'pause', 'the real key lands inside the wait and pauses');
  expect(p.timers.size === 0, 'and the waiting frame is let go');
  p.advance(5000);
  expect(p.acts.join() === 'pause', 'one press, one pause: never pause and then leave');
}

/* ---- D: the pause card -------------------------------------------------------- */
{
  const p = makePage();
  p.hostEscape.kept();
  p.advance(LOOK_AHEAD_MS);
  p.advance(2500);                              // the next press is past the double tap
  p.hostEscape.kept();
  p.advance(LOOK_AHEAD_MS);
  expect(p.acts.join() === 'pause,leave', 'unfocused: Escape on the pause card leaves');
  p.advance(5000);
  expect(p.acts.join() === 'pause,leave', 'once');

  const q = makePage();
  q.realKey(); q.advance(10); q.hostEscape.kept(); q.advance(2500);
  q.realKey(); q.advance(10); q.hostEscape.kept(); q.advance(5000);
  expect(q.acts.join() === 'pause,leave', 'focused: pause, then leave, each press once');
}

/* ---- E: a part of the page keeps the real key -------------------------------- */
// The promotion picker and the door's back step take Escape in the capture phase with
// stopImmediatePropagation. They attach after boot, so the dedupe (made first) still hears it.
{
  const p = makePage();
  let picker = 0;
  p.win.addEventListener('keydown', (e) => { if (e.key === 'Escape') { picker++; e.stopImmediatePropagation(); } }, true);
  p.realKey();
  p.hostEscape.kept();
  p.advance(5000);
  expect(picker === 1 && p.acts.length === 0, 'the picker kept the real key, and the frame did not replay it');
}

/* ---- F: the played Escape ------------------------------------------------------ */
{
  const p = makePage();
  const resume = p.element('resume');
  p.doc.activeElement = resume;                 // the pause card focuses Resume
  p.hostEscape.kept();
  p.advance(LOOK_AHEAD_MS);
  const ev = resume.got[0];
  expect(resume.got.length === 1 && p.doc.body.got.length === 0, 'played at the focused element, where a real key would land');
  expect(!!ev && ev.key === 'Escape' && ev.code === 'Escape' && ev.bubbles === true && ev.cancelable === true, 'a keydown Escape that bubbles');

  const q = makePage();
  q.doc.activeElement = null;
  q.hostEscape.kept();
  q.advance(LOOK_AHEAD_MS);
  expect(q.doc.body.got.length === 1 && q.acts.join() === 'pause', 'nothing focused: played at the body');

  const r = makePage();
  r.hostEscape.kept();
  r.advance(10);
  expect(r.hostEscape.kept() === false, 'a stray second frame while one waits is dropped');
  r.advance(5000);
  expect(r.acts.join() === 'pause', 'one play');

  const s = makePage();
  s.hostEscape.kept();
  s.hostEscape.dispose();
  s.advance(5000);
  expect(s.acts.length === 0 && s.timers.size === 0, 'dispose lets a waiting frame go');
  expect(!s.capture.length, 'and stops listening');

  const u = makePage();
  u.realKey('Enter');
  u.hostEscape.kept();
  u.advance(LOOK_AHEAD_MS);
  expect(u.acts.join() === 'pause', 'another key is not an Escape');
  expect(HOST_ESCAPE === 'pbp:escape', 'the frame name the host posts');
}

/* ---- G: boot.js wiring ---------------------------------------------------------- */
{
  const boot = readFileSync(new URL('../boot.js', import.meta.url), 'utf8');
  const hostEscapeJs = readFileSync(new URL('../ui/host-escape.js', import.meta.url), 'utf8');
  expect(/import \{ createHostEscape, HOST_ESCAPE \} from '\.\/ui\/host-escape\.js';/.test(boot), 'boot imports the kept Escape');
  const made = boot.indexOf('const hostEscape = createHostEscape();');
  expect(made > boot.indexOf('function main() {') && made < boot.indexOf('createDirector('), 'made at the top of main, before any part of the page listens for keys');
  expect(made < boot.indexOf("import('./board/promote.js')") && made < boot.indexOf("import('./door/door.js')"), 'before the promotion picker and the door, which keep Escape');
  expect(/onHostMessage\(\(m\) => \{ if \(m\.type === HOST_ESCAPE\) hostEscape\.kept\(\); \}\);/.test(boot), 'the host frame plays through it');
  expect(/addEventListener(\?\.)?\('keydown', onKey, true\)/.test(hostEscapeJs), 'it hears real keys in the capture phase');
}

console.log(`\nhost escape smoke: ${passed} passed, ${failed} failed`);
if (failed) process.exit(1);
