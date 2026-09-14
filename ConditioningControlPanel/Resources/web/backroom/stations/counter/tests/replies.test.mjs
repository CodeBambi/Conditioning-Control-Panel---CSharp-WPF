/* replies.test.mjs - replies in the real host shape and out of order (review of lane C-counter). */
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { classify, createCounter } from '../cards.js';
import { createMockServer } from '../mock-server.js';

const wait = (ms = 0) => new Promise((r) => setTimeout(r, ms));
let n = 0;
const mint = () => `repliesidem${String(++n).padStart(8, '0')}`;

test('the host relay: a 500 with a JSON body is offline, and offline or timeout with a body is a retry', () => {
  // BackRoomApi.Read: ok = 2xx && body.ok; a body with no reason gets reason "offline" and still rides along.
  assert.equal(classify({ ok: false, status: 500, reason: 'offline', body: { error: 'internal_server_error' } }).kind, 'retry');
  assert.equal(classify({ ok: false, status: 504, reason: 'timeout', body: {} }).kind, 'retry');
  assert.equal(classify({ ok: true, status: 500, body: { error: 'internal_server_error' } }).kind, 'retry');
  assert.equal(classify({ ok: false, status: 200, reason: 'busy', body: { ok: false, reason: 'busy' } }).kind, 'retry');
  assert.equal(classify({ ok: false, status: 200, reason: 'bad_input', body: { ok: false, reason: 'bad_input' } }).kind, 'refresh');
  assert.equal(classify({ ok: false, status: 403, reason: 'closed', body: { ok: false, reason: 'closed' } }).kind, 'closed');
});

test('a buy that settled but answered 500: the confirm stays with its idem, the retry replays, charged once', async () => {
  const server = createMockServer({ sp: 100, on: '*' });
  const sent = [];
  let crash = false;
  const request = async (op, body, idem) => {
    sent.push({ op, idem });
    const res = await server.handle(op, body, idem);
    if (crash && op === 'buy') { crash = false; return { ok: false, status: 500, reason: 'offline', body: { error: 'internal_server_error' } }; }
    return res;
  };
  const counter = createCounter({ request, sp: () => server.user.sp, mint, onChange: () => {}, chime: () => {} });
  await counter.open();
  assert.ok(counter.ask('rt_demo'));
  const idem = counter.confirm.idem;
  crash = true;
  assert.equal(await counter.buy(), 'retry');
  assert.ok(counter.confirm && counter.confirm.retry && counter.confirm.idem === idem, 'the confirm and its idem survive');
  assert.equal(server.user.sp, 80);
  assert.equal(await counter.buy(), 'ok');
  assert.deepEqual(sent.filter((x) => x.op === 'buy').map((x) => x.idem), [idem, idem]);
  assert.equal(server.user.sp, 80, 'charged once');
});

test('a state read sent before a later buy settled never paints over it; the page reads again', async () => {
  const server = createMockServer({ sp: 1000, on: '*' });
  const parked = [], sent = [];
  let park = false;
  const request = (op, body, idem) => {
    sent.push(op);
    const reply = server.handle(op, body, idem);   // the server answers now; the reply may arrive late
    return park && op === 'state' ? new Promise((r) => parked.push(() => r(reply))) : reply;
  };
  const counter = createCounter({ request, sp: () => server.user.sp, mint, onChange: () => {}, chime: () => {} });
  await counter.open();
  park = true;
  counter.ask('jackpot_remix');
  assert.equal(await counter.buy(), 'ok');   // its follow-up state is answered (sp 985) and parked
  assert.equal(parked.length, 1);
  counter.ask('rt_demo');
  assert.equal(await counter.buy(), 'ok');   // settles after that state was answered (sp 965)
  park = false;
  parked.shift()();                           // the older state arrives last
  await wait(5);
  const rt = counter.view().find((r) => r.id === 'rt_demo');
  assert.equal(rt.face, 'owned', 'the stale read did not turn an owned card back into Buy');
  assert.equal(counter.state.sp, 965);
  assert.equal(sent.filter((x) => x === 'state').length, 4, 'open, two follow-ups, and one read again');
});
