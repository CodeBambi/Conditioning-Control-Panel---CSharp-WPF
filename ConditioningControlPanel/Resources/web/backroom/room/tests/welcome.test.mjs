/* backroom/room/tests/welcome.test.mjs - the first-visit card (CONTRACT section 13).
 * node --test ConditioningControlPanel/Resources/web/backroom/room/tests/*.test.mjs */

import { test } from 'node:test';
import assert from 'node:assert/strict';
import { createWelcome, shouldShow, stepKeys, FALLBACK, CLOSE_KEYS, HERO_SRC, PAGE2, installWelcome, reopenWelcome, dismissWelcome } from '../welcome.js';

/* Just enough DOM to RUN the card: nodes with children, text, attributes and listeners. */
function stubDom() {
  const make = (tag) => {
    const n = { tag, className: '', textContent: null, attrs: {}, children: [], listeners: {}, parent: null, focused: 0 };
    n.setAttribute = (k, v) => { n.attrs[k] = v; };
    n.append = (...kids) => { for (const k of kids) { k.parent = n; n.children.push(k); } };
    n.remove = () => { if (n.parent) { n.parent.children = n.parent.children.filter(c => c !== n); n.parent = null; } };
    n.addEventListener = (t, fn) => { (n.listeners[t] ||= []).push(fn); };
    n.fire = (t, ev = {}) => { for (const fn of n.listeners[t] || []) fn({ target: n, ...ev }); };
    n.focus = () => { n.focused++; };
    return n;
  };
  const win = { listeners: {}, addEventListener(t, fn) { (win.listeners[t] ||= []).push(fn); },
    removeEventListener(t, fn) { win.listeners[t] = (win.listeners[t] || []).filter(f => f !== fn); },
    key(key) { const ev = { key, stopped: 0, prevented: 0, stopPropagation() { ev.stopped++; }, preventDefault() { ev.prevented++; } };
      for (const fn of Array.from(win.listeners.keydown || [])) fn(ev); return ev; } };
  return { doc: { createElement: make }, win, layer: make('div') };
}
const walk = (n, out = []) => { out.push(n); for (const c of n.children) walk(c, out); return out; };
const byClass = (root, cls) => walk(root).filter(n => String(n.className).split(' ').includes(cls));

test('shouldShow: only a host that recorded a dismissal keeps the card away', () => {
  assert.equal(shouldShow({ welcomeSeen: true }), false);
  assert.equal(shouldShow({ welcomeSeen: false }), true);
  assert.equal(shouldShow({}), true);              // an older host without the field
  assert.equal(shouldShow(undefined), true);
  assert.equal(shouldShow({ welcomeSeen: 'true' }), true);   // a string is not a dismissal
});

test('the phone gets the stick wording where the desk line names keys, and step 3 is shared', () => {
  assert.deepEqual(stepKeys(false), ['br_welcome_step1', 'br_welcome_step2', 'br_welcome_step3']);
  assert.deepEqual(stepKeys(true), ['br_welcome_step1_touch', 'br_welcome_step2_touch', 'br_welcome_step3']);
  for (const k of [...stepKeys(false), ...stepKeys(true)]) assert.ok(FALLBACK[k], k + ' has an English fallback');
});

test('the card mounts on the layer with the hero, the title, three numbered steps and one button', () => {
  const { doc, win, layer } = stubDom();
  const seen = [];
  const w = createWelcome({ layer, doc, win, touch: false, lex: (k, f) => { seen.push(k); return f; }, onDone: () => {} });
  assert.equal(layer.children.length, 1);
  assert.equal(w.open(), true);
  const [hero] = byClass(w.root, 'br-welcome-hero');
  assert.equal(hero.src, HERO_SRC);
  const [p1, p2] = byClass(w.root, 'br-welcome-page');
  assert.equal(byClass(p1, 'br-welcome-title')[0].textContent, FALLBACK.br_welcome_title);
  assert.deepEqual(byClass(p1, 'br-welcome-num').map(n => n.textContent), ['1', '2', '3']);
  assert.deepEqual(byClass(p1, 'br-welcome-lead').map(n => n.textContent),
    [FALLBACK.br_welcome_step1_lead, FALLBACK.br_welcome_step2_lead, FALLBACK.br_welcome_step3_lead]);
  assert.equal(byClass(p1, 'br-welcome-body')[0].textContent, FALLBACK.br_welcome_step1);
  assert.equal(byClass(p2, 'br-welcome-title')[0].textContent, FALLBACK.br_welcome_p2_title);
  assert.deepEqual(byClass(p2, 'br-welcome-lead').map(n => n.textContent), PAGE2.map(s => FALLBACK[s.lead]));
  assert.deepEqual(byClass(p2, 'br-welcome-body').map(n => n.textContent), PAGE2.map(s => FALLBACK[s.body]));
  assert.equal(byClass(w.root, 'br-welcome-go').length, 1);
  assert.equal(byClass(w.root, 'br-welcome-dot').length, 2);
  assert.ok(seen.includes('br_welcome_go'), 'every line goes through the lexicon');
  assert.equal(byClass(w.root, 'br-card')[0].attrs.role, 'dialog');
});

test('a translation lands, and a lexicon that answers nothing falls back to English', () => {
  const { doc, win, layer } = stubDom();
  const de = createWelcome({ layer, doc, win, touch: true, lex: (k, f) => k === 'br_welcome_go' ? 'Lass mich rein' : f });
  assert.equal(byClass(de.root, 'br-welcome-go')[0].textContent, 'Lass mich rein');
  assert.equal(byClass(de.root, 'br-welcome-body')[0].textContent, FALLBACK.br_welcome_step1_touch);
  const none = createWelcome({ layer, doc, win, touch: false, lex: () => '' });
  assert.equal(byClass(none.root, 'br-welcome-title')[0].textContent, FALLBACK.br_welcome_title);
});

test('the button dismisses once: the node leaves the layer, onDone fires exactly once, the key hook is gone', () => {
  const { doc, win, layer } = stubDom();
  let done = 0;
  const w = createWelcome({ layer, doc, win, touch: false, onDone: () => { done++; } });
  assert.equal(win.listeners.keydown.length, 1);
  byClass(w.root, 'br-welcome-go')[0].fire('click');
  assert.equal(w.open(), false);
  assert.equal(layer.children.length, 0);
  assert.equal(done, 1);
  assert.equal(win.listeners.keydown.length, 0);
  assert.equal(w.dismiss(), false, 'a second dismiss answers false and fires nothing');
  assert.equal(done, 1);
});

test('while the card is up every key is swallowed; Escape, Enter and Space close it', () => {
  for (const key of CLOSE_KEYS) {
    const { doc, win, layer } = stubDom();
    const w = createWelcome({ layer, doc, win, touch: false });
    const walkKey = win.key('w');
    assert.equal(walkKey.stopped, 1, 'W does not reach the room');
    assert.equal(walkKey.prevented, 0, 'but it is not cancelled - nothing else claims it');
    assert.equal(w.open(), true);
    const close = win.key(key);
    assert.equal(close.stopped, 1);
    assert.equal(close.prevented, 1);
    assert.equal(w.open(), false);
  }
});

test('a tap on the veil closes it; a tap on the card does not', () => {
  const { doc, win, layer } = stubDom();
  const w = createWelcome({ layer, doc, win, touch: false });
  const card = byClass(w.root, 'br-card')[0];
  w.root.fire('pointerdown', { target: card });
  assert.equal(w.open(), true);
  w.root.fire('pointerdown', { target: w.root });
  assert.equal(w.open(), false);
});

test('two pages: page 1 shows Next, page 2 shows Back and Let me in; arrows, Next, Back and the dots turn them', () => {
  const { doc, win, layer } = stubDom();
  const w = createWelcome({ layer, doc, win, touch: false });
  const [p1, p2] = byClass(w.root, 'br-welcome-page');
  const next = byClass(w.root, 'br-welcome-next')[0], prev = byClass(w.root, 'br-welcome-prev')[0], go = byClass(w.root, 'br-welcome-go')[0];
  const dots = byClass(w.root, 'br-welcome-dot');
  assert.equal(w.page(), 0);
  assert.deepEqual([p1.hidden, p2.hidden, next.hidden, prev.hidden, go.hidden], [false, true, false, true, true]);
  assert.deepEqual(dots.map(d => d.attrs['data-on']), ['1', '0']);
  const right = win.key('ArrowRight');
  assert.equal(right.prevented, 1);
  assert.equal(w.page(), 1);
  assert.deepEqual([p1.hidden, p2.hidden, next.hidden, prev.hidden, go.hidden], [true, false, true, false, false]);
  assert.deepEqual(dots.map(d => d.attrs['data-on']), ['0', '1']);
  assert.equal(w.root.children[0].attrs['aria-label'], FALLBACK.br_welcome_p2_title);
  win.key('ArrowRight');
  assert.equal(w.page(), 1, 'the last page clamps');
  win.key('ArrowLeft');
  assert.equal(w.page(), 0);
  next.fire('click'); assert.equal(w.page(), 1);
  prev.fire('click'); assert.equal(w.page(), 0);
  dots[1].fire('click'); assert.equal(w.page(), 1);
  assert.equal(w.open(), true, 'turning pages never closes it');
  go.fire('click');
  assert.equal(w.open(), false);
});

test('a card can start on page 2; Enter still closes from either page', () => {
  const { doc, win, layer } = stubDom();
  const w = createWelcome({ layer, doc, win, touch: false, page: 1 });
  assert.equal(w.page(), 1);
  win.key('ArrowLeft');
  assert.equal(w.page(), 0);
  win.key('Enter');
  assert.equal(w.open(), false);
});

test('the Parlour re-opens it: installWelcome once, reopenWelcome lays page 1 and writes nothing, dismissWelcome closes it', () => {
  const { doc, win, layer } = stubDom();
  let done = 0;
  installWelcome(null);
  assert.equal(reopenWelcome(), null, 'nothing installed, nothing opens');
  installWelcome({ layer, doc, win, touch: false, onDone: () => { done++; } });
  const w = reopenWelcome();
  assert.ok(w && w.open());
  assert.equal(w.page(), 0);
  assert.equal(layer.children.length, 1);
  const again = reopenWelcome(1);
  assert.equal(again, w, 'a second call turns the page of the card already up');
  assert.equal(w.page(), 1);
  assert.equal(dismissWelcome(), true);
  assert.equal(w.open(), false);
  assert.equal(done, 0, 'a re-open never posts welcomeSeen');
  assert.equal(dismissWelcome(), false);
  installWelcome(null);
});
