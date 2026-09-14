import { test } from 'node:test';
import assert from 'node:assert/strict';
import { install } from './fake-dom.mjs';
import { createMockServer } from '../mock-server.js';

const dom = install();
const { mount } = await import('../station.js');
const wait = (ms = 0) => new Promise((r) => setTimeout(r, ms));

/** A room-like ctx on the mock. `hold` parks requests until release(). */
function room(opts = {}, { hostBack = true, reduced = false, intensity = 'normal' } = {}) {
  const server = createMockServer(opts), root = new dom.El('div'), chip = [], held = [], spSubs = new Set(), setSubs = new Set();
  let hold = false, stood = 0;
  const ctx = {
    root, hostBack, reduced, intensity, gates: { flash: true, subliminal: true, spiral: true, brainDrain: true },
    request: (op, body, idem) => { const go = () => server.handle(op, body, idem); return hold ? new Promise((r) => held.push(() => r(go()))) : go(); },
    sp: () => server.user.sp,
    onSp: (fn) => { spSubs.add(fn); return () => spSubs.delete(fn); },
    onSettings: (fn) => { setSubs.add(fn); return () => setSubs.delete(fn); },
    lex: (key, fallback) => fallback,
    standUp: () => { stood++; },
    spReadout: hostBack ? { set: (v) => chip.push(['set', v]), owe: () => {}, thud: () => chip.push('thud'), target: () => null } : undefined,
  };
  return { server, ctx, root, chip, spSubs, setSubs, get stood() { return stood; },
    hold: (v) => { hold = v; }, release: () => { while (held.length) held.shift()(); } };
}
const card = (root, id) => root.all('counter-card').find((c) => c.dataset.id === id);

test('Back is on screen before state answers; Escape stands up; hostBack hides the station Back', async () => {
  const r = room({ on: '*' }, { hostBack: false });
  const st = await mount(r.ctx);
  r.hold(true);
  const opening = st.open();
  const back = r.root.one('counter-back');
  assert.ok(back && !back.hidden, 'standalone Back before the read');
  assert.equal(r.root.one('counter-station').dataset.phase, 'loading');
  back.click();
  assert.equal(r.stood, 1);
  dom.dispatch('keydown', { key: 'Escape', defaultPrevented: false, preventDefault() {} });
  assert.equal(r.stood, 2);
  await wait();
  r.release();
  await opening;
  assert.equal(r.root.all('counter-card').length, 8);
  st.destroy();

  const hosted = room({ on: '*' });
  const hs = await mount(hosted.ctx);
  await hs.open();
  assert.ok(hosted.root.all('counter-back').every((b) => b.hidden));
  assert.equal(hosted.root.one('counter-station').dataset.hostBack, '');
  hs.destroy();
});

test('cards paint every face, the RT note and the delivery line', async () => {
  const r = room({ sp: 30, on: 'rt_demo,high_roller,flashes_v2', owned: { jackpot_remix: { at: 1, paidSp: 15 } } });
  r.server.linkDiscord(null);
  const st = await mount(r.ctx);
  await st.open();
  const faces = r.root.all('counter-card').map((c) => c.dataset.face);
  assert.deepEqual(faces, ['owned', 'buy', 'discord', 'short', 'soon', 'soon', 'soon', 'soon']);
  assert.equal(card(r.root, 'flashes_v2').one('counter-buy').textContent, 'Short by 210');
  assert.ok(card(r.root, 'flashes_v2').one('counter-buy').disabled);
  assert.ok(card(r.root, 'rt_demo').one('counter-note') && !card(r.root, 'high_roller').one('counter-note'));
  assert.equal(card(r.root, 'bubbles_v2').one('counter-buy'), null, 'soon has no button');
  st.destroy();
});

test('Buy -> confirm (balance after) -> Confirm: Owned, chip set/null/thud; reduced adds no flip', async () => {
  for (const reduced of [false, true]) {
    const r = room({ sp: 100, on: '*', delivery: 'pending' }, { reduced });
    const st = await mount(r.ctx);
    await st.open();
    const c = card(r.root, 'high_roller');
    c.one('counter-buy').click();
    assert.equal(c.one('counter-after').textContent, 'Balance after: 60');
    assert.equal(dom.document.activeElement, c.one('counter-yes'), 'focus moves to Confirm');
    c.one('counter-yes').click();
    assert.equal(c.one('counter-yes').getAttribute('aria-busy'), 'true', 'pending');
    await wait(5);
    assert.equal(c.dataset.face, 'owned');
    assert.equal(c.one('counter-delivery').textContent, 'The role is on its way to Discord.');
    assert.deepEqual(r.chip, [['set', 60], ['set', null], 'thud']);
    assert.equal(c.classList.contains('is-flip'), !reduced);
    assert.equal(r.root.one('counter-station').dataset.still === '', reduced);
    st.destroy();
  }
});

test('a busy reply keeps the confirm with the retry line', async () => {
  const r = room({ sp: 100, on: '*' });
  const st = await mount(r.ctx);
  await st.open();
  const c = card(r.root, 'rt_demo');
  c.one('counter-buy').click();
  r.server.fail('buy', 'busy');
  c.one('counter-yes').click();
  await wait(5);
  assert.ok(c.one('counter-retry') && !c.one('counter-yes').disabled);
  c.one('counter-no').click();
  assert.ok(c.one('counter-confirm').hidden);
  st.destroy();
});

test('keyboard: a repeated Enter or Space presses nothing; busy and a balance repaint keep focus on Confirm', async () => {
  const r = room({ sp: 100, on: '*' });
  const st = await mount(r.ctx);
  await st.open();
  const key = (k, repeat) => { const e = { key: k, repeat, defaultPrevented: false, preventDefault() { e.defaultPrevented = true; } }; dom.dispatch('keydown', e); return e.defaultPrevented; };
  assert.equal(key('Enter', true), true, 'a held Enter never reaches the focused Confirm');
  assert.equal(key(' ', true), true);
  assert.equal(key('Enter', false), false, 'a fresh press still works');
  const c = card(r.root, 'rt_demo');
  c.one('counter-buy').click();
  r.server.fail('buy', 'busy');
  c.one('counter-yes').click();
  await wait(5);
  assert.ok(c.one('counter-retry'));
  assert.equal(dom.document.activeElement, c.one('counter-yes'), 'the retry Confirm has the focus');
  r.server.setSp(90);
  for (const fn of r.spSubs) fn(90);
  assert.equal(c.one('counter-after').textContent, 'Balance after: 70');
  assert.equal(dom.document.activeElement, c.one('counter-yes'), 'a balance repaint keeps it');
  assert.equal(r.stood, 0);
  st.destroy();
});

test('Back during an in-flight buy closes at once; the late reply touches nothing', async () => {
  const r = room({ sp: 100, on: '*' });
  const st = await mount(r.ctx);
  await st.open();
  card(r.root, 'rt_demo').one('counter-buy').click();
  r.hold(true);
  card(r.root, 'rt_demo').one('counter-yes').click();
  await wait();
  await st.close();
  assert.equal(r.root.children.length, 0, 'gone before the reply');
  r.release();
  await wait(5);
  assert.deepEqual(r.chip, []);
  assert.equal(r.root.children.length, 0);
  assert.equal(r.server.user.sp, 80, 'the buy settled on the server');
});

test('destroy frees the page: root, keydown, onSp, onSettings and the stylesheet', async () => {
  const r = room({ on: '*' });
  const st = await mount(r.ctx);
  assert.equal(dom.document.head.children.length, 1, 'stylesheet linked once');
  await st.open();
  assert.equal(r.spSubs.size, 1); assert.equal(r.setSubs.size, 1);
  assert.equal(dom.listeners.get('keydown').size, 1);
  st.destroy();
  assert.equal(r.root.children.length, 0);
  assert.equal(r.spSubs.size, 0); assert.equal(r.setSubs.size, 0);
  assert.equal(dom.listeners.get('keydown').size, 0);
  assert.equal(dom.document.head.children.length, 0);
  st.destroy();   // twice is harmless
});
