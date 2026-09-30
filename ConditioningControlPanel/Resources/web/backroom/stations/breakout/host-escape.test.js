import {test} from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import * as shared from '../../shared/host-escape.js';
import * as race from '../../../dtrh/race/hostEscape.js';

// The Escape the desktop panel kept as the game's pause reaches Breakout (and the race) once, focused or
// not (tester report 2026-09-30: Escape closed the whole Breakout window; with the page out of keyboard
// focus nothing paused at all). Same shape as piecebypiece/ui/host-escape.js and its smoke.
const read = f => readFileSync(new URL(f, import.meta.url), 'utf8');

class FakeKey {
  constructor(init) { Object.assign(this, init); this.stopped = false; }
  stopImmediatePropagation() { this.stopped = true; }
  stopPropagation() { this.stopped = true; }
  preventDefault() { this.defaultPrevented = true; }
}

/** A page: window capture listeners, then bubble listeners, and a game that pauses on Escape. */
function makePage(mod) {
  let t = 0, nextId = 1;
  const timers = new Map();
  const later = (fn, ms) => { const id = nextId++; timers.set(id, {fn, at: t + ms}); return id; };
  const cancel = id => { timers.delete(id); };
  const advance = ms => {
    const end = t + ms;
    for (;;) {
      let next = null;
      for (const [id, tm] of timers) if (tm.at <= end && (!next || tm.at < next[1].at)) next = [id, tm];
      if (!next) break;
      timers.delete(next[0]); t = next[1].at; next[1].fn();
    }
    t = end;
  };
  const capture = [], bubble = [];
  const listOf = opt => (opt === true || (opt && opt.capture)) ? capture : bubble;
  const win = {
    addEventListener(type, fn, opt) { if (type === 'keydown') listOf(opt).push(fn); },
    removeEventListener(type, fn, opt) { const l = listOf(opt); const i = l.indexOf(fn); if (i >= 0) l.splice(i, 1); },
  };
  const run = ev => { for (const fn of [...capture, ...bubble]) { fn(ev); if (ev.stopped) return; } };
  const body = { got: [], dispatchEvent(ev) { this.got.push(ev); ev.target = this; run(ev); return true; } };
  const doc = { body, activeElement: body };
  const realKey = () => { const ev = new FakeKey({key: 'Escape', isTrusted: true}); ev.target = body; run(ev); };
  const hostEscape = mod.createHostEscape({win, doc, now: () => t, later, cancel,
    makeEvent: () => new FakeKey({key: 'Escape', code: 'Escape', isTrusted: false})});
  const acts = [];
  let paused = false;
  win.addEventListener('keydown', e => { if (e.key !== 'Escape') return; acts.push(paused ? 'leave' : 'pause'); paused = true; });
  return {hostEscape, realKey, acts, advance, capture, body};
}

for (const [name, mod] of [['shared', shared], ['race', race]]) {
  test(`${name}: unfocused, the kept press plays one Escape after the short wait`, () => {
    const p = makePage(mod);
    assert.equal(p.hostEscape.kept(), true);
    p.advance(mod.LOOK_AHEAD_MS - 1);
    assert.deepEqual(p.acts, []);
    p.advance(1);
    assert.deepEqual(p.acts, ['pause']);
    p.advance(5000);
    assert.deepEqual(p.acts, ['pause'], 'once');
    assert.equal(p.body.got[0].code, 'Escape', 'the race reads e.code');
  });

  test(`${name}: focused, the real key first, the frame is dropped`, () => {
    const p = makePage(mod);
    p.realKey(); p.advance(15);
    assert.equal(p.hostEscape.kept(), false);
    p.advance(5000);
    assert.deepEqual(p.acts, ['pause']);
  });

  test(`${name}: focused, the frame first, the real key wins and the frame never plays`, () => {
    const p = makePage(mod);
    assert.equal(p.hostEscape.kept(), true);
    p.advance(40); p.realKey(); p.advance(5000);
    assert.deepEqual(p.acts, ['pause']);
  });

  test(`${name}: a later kept press plays again (Escape, Escape leaves)`, () => {
    const p = makePage(mod);
    p.hostEscape.kept(); p.advance(mod.LOOK_AHEAD_MS);
    p.advance(2100);
    p.hostEscape.kept(); p.advance(mod.LOOK_AHEAD_MS);
    assert.deepEqual(p.acts, ['pause', 'leave']);
  });

  test(`${name}: dispose drops a waiting press and the listener`, () => {
    const p = makePage(mod);
    p.hostEscape.kept(); p.hostEscape.dispose(); p.advance(5000);
    assert.deepEqual(p.acts, []);
    assert.equal(p.capture.length, 0);
  });

  test(`${name}: the frame name matches the host and the look-back stays inside the panel's 2 s double tap`, () => {
    assert.equal(mod.HOST_ESCAPE, 'kept-escape');
    assert.ok(mod.LOOK_BACK_MS < 2000);
    assert.ok(mod.LOOK_AHEAD_MS > 0 && mod.LOOK_AHEAD_MS <= 500);
  });
}

test('the two copies share one body', () => {
  const body = src => src.slice(src.indexOf('export const LOOK_BACK_MS'));
  assert.equal(body(read('../../../dtrh/race/hostEscape.js')), body(read('../../shared/host-escape.js')));
});

test('the hosts post the name the pages listen for', () => {
  const cs = f => read('../../../../../Services/' + f);
  assert.match(cs('BackRoom/BackRoomHostService.cs'), /KeptEscapeType = "kept-escape";/);
  assert.match(cs('Chaos/CaucusHostService.cs'), /KeptEscapeType = "kept-escape";/);
});

test('play.html makes the listener before the station and plays the host frame through it', () => {
  const page = read('./play.html');
  const made = page.indexOf('const hostEscape = createHostEscape();');
  assert.ok(made > 0 && made < page.indexOf('await mount(ctx)'), 'made before the station mounts');
  assert.match(page, /bridge\.on\(HOST_ESCAPE, \(\) => hostEscape\.kept\(\)\);/);
  assert.match(page, /if \(m\.breakoutStandalone\) ctx\.hostKeepsEscape = true;/, 'only the desktop window leaves on Escape, Escape');
});

test('station: a held Escape is one press; on the desktop an Escape on an Escape pause leaves, any other pause arms first', () => {
  const s = read('./station.js');
  const repeat = s.indexOf("if(e.key==='Escape'&&e.repeat)");
  assert.ok(repeat > 0 && repeat < s.indexOf('setPaused(!paused);if(paused){escPaused'), 'repeats are dropped before the toggle');
  assert.match(s, /if\(e\.key==='Escape'&&paused&&ctx\.hostKeepsEscape\)\{if\(escPaused\)back\(\);else escPaused=true;return;\}/);
  assert.match(s, /setPaused\(!paused\);if\(paused\)\{escPaused=e\.key==='Escape';/);
  assert.match(s, /if \(!p\) escPaused = false;/, 'resuming clears the mark');
  assert.match(s, /demoComplete&&!ui\.demoCard\.hidden\)\{e\.preventDefault\(\);ui\.demoCard\.querySelector\('\[data-demo="menu"\]'\)\.click\(\)/,
    'Escape on the demo card is its Menu button, never a dead key');
});

test('raceBoot makes the race listener at boot and plays the host frame through it', () => {
  const boot = read('../../../dtrh/raceBoot.js');
  assert.match(boot, /import \{ createHostEscape, HOST_ESCAPE \} from '\.\/race\/hostEscape\.js';/);
  const made = boot.indexOf('const hostEscape = createHostEscape();');
  assert.ok(made > 0 && made < boot.indexOf("import('./race/run.js')"), 'made before the race and its input');
  assert.match(boot, /bridge\.on\(HOST_ESCAPE, \(\) => hostEscape\.kept\(\)\);/);
});
