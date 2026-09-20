/* backroom/room/tests/welcome.test.mjs - the first-visit card (CONTRACT section 13).
 * node --test ConditioningControlPanel/Resources/web/backroom/room/tests/*.test.mjs */

import { test } from 'node:test';
import assert from 'node:assert/strict';
import { createWelcome, shouldShow, stepKeys, FALLBACK, CLOSE_KEYS, PAGE_KEYS, HERO_SRC, PRIZES_SRC, PAGES, MEDIA_STEPS, PRIZE_STEPS } from '../welcome.js';

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
  assert.equal(byClass(w.root, 'br-welcome-title')[0].textContent, FALLBACK.br_welcome_title);
  assert.deepEqual(byClass(w.root, 'br-welcome-num').map(n => n.textContent), ['1', '2', '3']);
  assert.deepEqual(byClass(w.root, 'br-welcome-lead').map(n => n.textContent),
    [FALLBACK.br_welcome_step1_lead, FALLBACK.br_welcome_step2_lead, FALLBACK.br_welcome_step3_lead]);
  assert.equal(byClass(w.root, 'br-welcome-body')[0].textContent, FALLBACK.br_welcome_step1);
  assert.equal(byClass(w.root, 'br-welcome-go').length, 1);
  assert.equal(byClass(w.root, 'br-welcome-go')[0].textContent, FALLBACK.br_welcome_next, 'page one turns, it does not let you in yet');
  assert.ok(seen.includes('br_welcome_next'), 'every line goes through the lexicon');
  assert.equal(byClass(w.root, 'br-welcome-dot').length, PAGES.length);
  assert.equal(byClass(w.root, 'br-welcome-back')[0].hidden, true, 'no Back on the first page');
  assert.equal(byClass(w.root, 'br-card')[0].attrs.role, 'dialog');
});

test('a translation lands, and a lexicon that answers nothing falls back to English', () => {
  const { doc, win, layer } = stubDom();
  const de = createWelcome({ layer, doc, win, touch: true, page: 2, lex: (k, f) => k === 'br_welcome_go' ? 'Lass mich rein' : f });
  assert.equal(byClass(de.root, 'br-welcome-go')[0].textContent, 'Lass mich rein');
  de.show(0);
  assert.equal(byClass(de.root, 'br-welcome-body')[0].textContent, FALLBACK.br_welcome_step1_touch);
  const none = createWelcome({ layer, doc, win, touch: false, lex: () => '' });
  assert.equal(byClass(none.root, 'br-welcome-title')[0].textContent, FALLBACK.br_welcome_title);
});

test('the button turns the pages, then dismisses once: the node leaves the layer, onDone fires exactly once, the key hook is gone', () => {
  const { doc, win, layer } = stubDom();
  let done = 0;
  const w = createWelcome({ layer, doc, win, touch: false, onDone: () => { done++; } });
  assert.equal(win.listeners.keydown.length, 1);
  byClass(w.root, 'br-welcome-go')[0].fire('click');
  assert.equal(w.page(), 1); assert.equal(w.open(), true); assert.equal(done, 0);
  assert.equal(byClass(w.root, 'br-welcome-go')[0].textContent, FALLBACK.br_welcome_next, 'page two turns too');
  byClass(w.root, 'br-welcome-go')[0].fire('click');
  assert.equal(w.page(), 2); assert.equal(w.open(), true); assert.equal(done, 0);
  assert.equal(byClass(w.root, 'br-welcome-go')[0].textContent, FALLBACK.br_welcome_go);
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


test('page two is pictures and sparkles: the room hero, its own title, three steps, no placard', () => {
  const { doc, win, layer } = stubDom();
  const w = createWelcome({ layer, doc, win, touch: true });
  assert.equal(PAGES.length, 3);
  assert.deepEqual(PAGES.map(p => p.id), ['welcome', 'media', 'prizes']);
  assert.deepEqual(PAGES.map(p => !!p.placard), [true, false, true]);
  w.show(1);
  assert.equal(byClass(w.root, 'br-welcome-hero')[0].src, HERO_SRC);
  assert.equal(byClass(w.root, 'br-welcome-title')[0].textContent, 'Pictures and sparkles');
  assert.equal(byClass(w.root, 'br-welcome-sub')[0].textContent, FALLBACK.br_welcome_p2_sub);
  assert.deepEqual(byClass(w.root, 'br-welcome-lead').map(n => n.textContent), MEDIA_STEPS.map(s => FALLBACK[s.lead]));
  assert.deepEqual(byClass(w.root, 'br-welcome-body').map(n => n.textContent), MEDIA_STEPS.map(s => FALLBACK[s.body]), 'no phone wording on page two');
  assert.ok(FALLBACK.br_welcome_media.includes('Options'), 'the media step points at Options > Pictures and GIFs');
  assert.ok(FALLBACK.br_welcome_sp.includes('level up') && FALLBACK.br_welcome_sp.includes('100 bubbles'), 'the sparkle step says how they are earned');
  assert.equal(byClass(w.root, 'br-welcome-back')[0].hidden, false);
  assert.equal(byClass(w.root, 'br-welcome-go')[0].textContent, FALLBACK.br_welcome_next, 'the middle page turns, it does not let you in');
  assert.deepEqual(byClass(w.root, 'br-welcome-dot').map(d => d.className.includes('is-on')), [false, true, false]);
});

test('page three is the Prize Parlour: its own picture, title and three steps; Back returns; the arrows turn pages', () => {
  const { doc, win, layer } = stubDom();
  const w = createWelcome({ layer, doc, win, touch: true });
  assert.equal(PRIZES_SRC, 'room/assets/welcome-prizes.webp');
  w.show(2);
  assert.equal(byClass(w.root, 'br-welcome-hero')[0].src, PRIZES_SRC);
  assert.equal(byClass(w.root, 'br-welcome-title')[0].textContent, FALLBACK.br_welcome_prizes_title);
  assert.equal(byClass(w.root, 'br-welcome-sub')[0].textContent, FALLBACK.br_welcome_prizes_sub);
  assert.deepEqual(byClass(w.root, 'br-welcome-lead').map(n => n.textContent), PRIZE_STEPS.map(s => FALLBACK[s.lead]));
  assert.deepEqual(byClass(w.root, 'br-welcome-body').map(n => n.textContent), PRIZE_STEPS.map(s => FALLBACK[s.body]), 'no phone wording on page two');
  assert.equal(byClass(w.root, 'br-welcome-num').length, 3);
  assert.equal(byClass(w.root, 'br-welcome-back')[0].hidden, false);
  assert.deepEqual(byClass(w.root, 'br-welcome-dot').map(d => d.className.includes('is-on')), [false, false, true]);
  assert.equal(byClass(w.root, 'br-card')[0].attrs['aria-label'], FALLBACK.br_welcome_prizes_title);
  byClass(w.root, 'br-welcome-back')[0].fire('click');
  assert.equal(w.page(), 1);
  byClass(w.root, 'br-welcome-back')[0].fire('click');
  assert.equal(w.page(), 0);
  assert.equal(byClass(w.root, 'br-welcome-hero')[0].src, HERO_SRC);
  assert.equal(byClass(w.root, 'br-welcome-body')[0].textContent, FALLBACK.br_welcome_step1_touch, 'page one keeps the stick wording');
  for (const key of Object.keys(PAGE_KEYS)) assert.ok(!CLOSE_KEYS.includes(key));
  let ev = win.key('ArrowRight'); assert.equal(w.page(), 1); assert.equal(ev.stopped, 1); assert.equal(ev.prevented, 1);
  ev = win.key('ArrowRight'); assert.equal(w.page(), 2);
  ev = win.key('ArrowRight'); assert.equal(w.page(), 2, 'the last page stays');
  win.key('ArrowLeft'); assert.equal(w.page(), 1);
  win.key('ArrowLeft'); assert.equal(w.page(), 0);
  win.key('ArrowLeft'); assert.equal(w.page(), 0, 'the first page stays');
  assert.equal(w.open(), true, 'turning pages never closes the card');
});

test('read mode (a placard on the counter): opens at the asked page, the last button says Close, onDone still fires', () => {
  const { doc, win, layer } = stubDom();
  let done = 0;
  const w = createWelcome({ layer, doc, win, touch: false, page: 2, read: true, onDone: () => { done++; } });
  assert.equal(w.page(), 2);
  assert.equal(byClass(w.root, 'br-welcome-go')[0].textContent, FALLBACK.br_welcome_close);
  byClass(w.root, 'br-welcome-go')[0].fire('click');
  assert.equal(w.open(), false); assert.equal(done, 1); assert.equal(layer.children.length, 0);
  const first = createWelcome({ layer, doc, win, touch: false, page: 0, read: true });
  assert.equal(byClass(first.root, 'br-welcome-go')[0].textContent, FALLBACK.br_welcome_next, 'page one still says Next in read mode');
  byClass(first.root, 'br-welcome-go')[0].fire('click');
  assert.equal(first.page(), 1, 'Next from the welcome placard reaches pictures and sparkles');
  byClass(first.root, 'br-welcome-go')[0].fire('click');
  assert.equal(first.page(), 2, 'and then the Parlour');
  assert.equal(byClass(first.root, 'br-welcome-go')[0].textContent, FALLBACK.br_welcome_close);
  assert.equal(first.open(), true);
  first.dismiss();
  const out = createWelcome({ layer, doc, win, touch: false, page: 9, read: true });
  assert.equal(out.page(), PAGES.length - 1, 'a page past the end lands on the last one');
});
