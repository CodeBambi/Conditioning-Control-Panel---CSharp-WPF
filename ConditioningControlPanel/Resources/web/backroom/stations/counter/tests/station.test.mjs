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
  const r = room({ sp: 20, on: 'rt_demo,high_roller,flashes_v2', owned: { jackpot_remix: { at: 1, paidSp: 15 } } });
  r.server.linkDiscord(null);
  const st = await mount(r.ctx);
  await st.open();
  const faces = r.root.all('counter-card').map((c) => c.dataset.face);
  assert.deepEqual(faces, ['owned', 'buy', 'discord', 'short', 'soon', 'soon', 'soon', 'soon']);
  assert.equal(card(r.root, 'flashes_v2').one('counter-buy').textContent, 'Short by 10');
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

test('Try it: the three effect prizes get a preview inside the card, drawn by the page, nothing posted to the host', async () => {
  const r = room({ sp: 30, on: 'jackpot_remix,flashes_v2', owned: { jackpot_remix: { at: 1, paidSp: 15 } } });
  const fx = [], medias = [];
  r.ctx.fx = (...a) => { fx.push(a); return Promise.resolve({}); };
  r.ctx.media = (o) => { medias.push(o); return Promise.resolve({ gifs: [{ key: 'g0', url: 'https://ccp.assets/a/one.gif' }, { key: 'g1', url: 'https://evil.example/x.gif' }], words: [] }); };
  const st = await mount(r.ctx);
  await st.open();
  assert.deepEqual(r.root.all('counter-card').map((c) => [c.dataset.id, !!c.one('counter-try'), c.one('counter-try') ? c.one('counter-try').hidden : null]),
    [['jackpot_remix', true, false], ['rt_demo', false, null], ['high_roller', false, null], ['flashes_v2', true, false], ['bubbles_v2', true, true],
      ['rt_bundle_1', false, null], ['rt_bundle_2', false, null], ['rt_bundle_3', false, null]], 'owned, short and buy cards try; soon hides it; no button elsewhere');
  const c = card(r.root, 'flashes_v2');
  c.one('counter-try').click();
  assert.equal(c.one('counter-art').dataset.demo, 'flashes');
  assert.ok(c.one('counter-demo'), 'the stage is in the art box');
  await wait(40);
  assert.equal(medias.length, 1, 'one deal asked for the visit'); assert.equal(medias[0].count, 4);
  assert.equal(st.debug().demo.kind, 'flashes');
  const pics = c.all('counter-demo-pic').filter((p) => !p.hidden);
  assert.equal(pics.length, 1, 'one flash card');
  assert.equal(pics[0].dataset.url, 'https://ccp.assets/a/one.gif', 'the dealt picture, from a mapped origin only');
  assert.ok(pics[0].style.left && pics[0].style.transform.includes('rotate('));
  card(r.root, 'jackpot_remix').one('counter-try').click();
  assert.equal(c.one('counter-demo'), null, 'one preview at a time');
  assert.equal(card(r.root, 'jackpot_remix').one('counter-art').dataset.demo, 'remix');
  await wait(40);
  assert.deepEqual(medias.length, 1);
  assert.equal(card(r.root, 'jackpot_remix').all('counter-demo-pic').filter((p) => !p.hidden).length, 4);
  assert.equal(card(r.root, 'jackpot_remix').all('counter-demo-pic').filter((p) => p.dataset.url === 'https://evil.example/x.gif').length, 0, 'an unmapped url never loads');
  st.suspend(true);
  assert.equal(st.debug().demo, null, 'suspend stops it');
  card(r.root, 'jackpot_remix').one('counter-try').click();
  assert.equal(st.debug().demo, null, 'and nothing starts while suspended');
  st.suspend(false);
  card(r.root, 'jackpot_remix').one('counter-try').click();
  assert.equal(st.debug().demos, 3);
  await st.close();
  assert.equal(st.debug().demo, null, 'close stops it');
  assert.deepEqual(fx, [], 'never an fx over the bridge');
  assert.equal(r.server.user.sp, 30, 'nothing charged');
  st.destroy();
});

test('Try it under reduced motion: the settled frame, no pictures when the flash gate is off', async () => {
  const r = room({ sp: 300, on: '*' }, { reduced: true });
  r.ctx.gates = { flash: false, subliminal: true, spiral: true, brainDrain: true };
  r.ctx.media = () => Promise.resolve({ gifs: [{ key: 'g0', url: 'https://ccp.assets/a/one.gif' }], words: [] });
  const st = await mount(r.ctx);
  await st.open();
  const c = card(r.root, 'bubbles_v2');
  c.one('counter-try').click();
  await wait(40);
  const a = c.all('counter-demo-pic').filter((p) => !p.hidden).map((p) => [p.style.left, p.style.top, p.dataset.kind]);
  await wait(40);
  const b = c.all('counter-demo-pic').filter((p) => !p.hidden).map((p) => [p.style.left, p.style.top, p.dataset.kind]);
  assert.equal(a.length, 4); assert.deepEqual(a, b, 'held still');
  assert.ok(a.every(([, , k]) => k === 'bubble'));
  assert.ok(c.all('counter-demo-pic').every((p) => !p.dataset.url), 'flash off: plates, no pictures');
  st.destroy();
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


test('prize delivery is success-only, ownership is immediate, and live motion changes never replay it', async () => {
  const r = room({ sp: 100, on: '*', owned: { jackpot_remix: { at: 1, paidSp: 15 } } }, { hostBack: false });
  const st = await mount(r.ctx); await st.open();
  assert.equal(card(r.root, 'jackpot_remix').classList.contains('is-flip'), false);
  const c = card(r.root, 'rt_demo');
  c.one('counter-buy').click(); r.server.fail('buy', 'busy'); c.one('counter-yes').click(); await wait(5);
  assert.equal(c.classList.contains('is-flip'), false);
  c.one('counter-yes').click(); await wait(5);
  assert.equal(c.dataset.face, 'owned');
  assert.equal(c.classList.contains('is-flip'), true);
  assert.ok(c.one('counter-badge').textContent.includes('Owned'));
  r.root.one('counter-back').click(); assert.equal(r.stood, 1, 'Back remains live during delivery');
  r.ctx.motion = 'off'; for (const fn of r.setSubs) fn();
  assert.equal(c.classList.contains('is-flip'), false);
  r.ctx.motion = 'full'; for (const fn of r.setSubs) fn();
  assert.equal(c.classList.contains('is-flip'), false, 'returning to full does not replay a purchase');
  st.destroy();
});

test('suspending settles prize delivery and Calm or Off purchases start settled', async () => {
  for (const mode of ['full', 'off', 'calm']) {
    const r = room({ sp: 100, on: '*' });
    r.ctx.motion = mode === 'off' ? 'off' : 'full';
    r.ctx.intensity = mode === 'calm' ? 'calm' : 'normal';
    const st = await mount(r.ctx); await st.open();
    const c = card(r.root, 'rt_demo');
    c.one('counter-buy').click(); c.one('counter-yes').click(); await wait(5);
    assert.equal(c.dataset.face, 'owned');
    assert.equal(c.classList.contains('is-flip'), mode === 'full');
    st.suspend(true); assert.equal(c.classList.contains('is-flip'), false);
    st.suspend(false); for (const fn of r.spSubs) fn();
    assert.equal(c.classList.contains('is-flip'), false);
    st.destroy(); assert.equal(r.root.children.length, 0);
  }
});
test('demo unlock removes its listing and notifies the room once; return restores without a celebration', async () => {
  const r=room({sp:100,on:'*'}), events=[];
  r.ctx.prizesChanged=(body,id)=>events.push({body,id});
  const st=await mount(r.ctx); await st.open();
  const c=card(r.root,'rt_demo'); c.one('counter-buy').click(); c.one('counter-yes').click(); await wait(15);
  assert.equal(c.hidden,true); assert.equal(events.filter(e=>e.id==='rt_demo').length,1);
  assert.ok(events.find(e=>e.id)?.body.catalog.find(row=>row.id==='rt_demo').owned);
  st.close(); events.length=0; await st.open();
  assert.equal(card(r.root,'rt_demo').hidden,true); assert.equal(events.some(e=>e.id),false);
  st.destroy();
});
