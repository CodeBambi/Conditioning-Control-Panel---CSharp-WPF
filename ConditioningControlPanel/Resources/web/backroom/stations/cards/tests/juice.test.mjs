import test from 'node:test';
import assert from 'node:assert/strict';
import { landing, deckRecoil, flipLift, cardTilt, touchdown } from '../juice.js';
import { createTable } from '../table.js';

test('gestures end exactly at rest and remain still when motion is disabled', () => {
  for (const f of [landing, deckRecoil]) {
    assert.equal(f(-1), 0); assert.equal(f(0), 0); assert.equal(f(1000), 0);
    assert.ok(f(80) > 0); assert.equal(f(80, true), 0);
  }
  assert.equal(flipLift(0), 0); assert.ok(flipLift(.5) > 0); assert.equal(flipLift(.5, true), 0);
  for (let i = 0; i < 12; i++) assert.ok(Math.abs(cardTilt('d', i)) <= .024);
});

test('touchdown is one beat even across repeated frames; quiet adoption stays silent', () => {
  const cues = [], c = {};
  touchdown(c, 500, false, (x) => cues.push(x));
  touchdown(c, 600, false, (x) => cues.push(x));
  touchdown({ quiet: true }, 700, true, (x) => cues.push(x));
  assert.deepEqual(cues, ['card-land']); assert.equal(c.landAt, 500);
});

function fixture() {
  const gradient = { addColorStop() {} };
  const g = new Proxy({}, { get: (o, k) => k in o ? o[k] : k === 'measureText' ? () => ({ width: 5 }) : k.startsWith('create') ? () => gradient : () => {} });
  const canvas = { getContext: () => g, getBoundingClientRect: () => ({ width: 1280, height: 720 }) };
  globalThis.document = { createElement: () => canvas };
  const cues = [], table = createTable(canvas, { onCue: (x) => cues.push(x) });
  const draw = (now, still = false) => table.draw({ now, still, k: 1, full: false, gates: {}, print: '' });
  return { cues, table, draw };
}

test('canvas emits land only at the rendered touchdown, and no delayed sound after quiet restore', () => {
  const { cues, table, draw } = fixture();
  table.addCard({ owner: 0, slot: 0, code: 'Kh' }, 0);
  draw(100); assert.deepEqual(cues, []);
  draw(500); draw(900); assert.deepEqual(cues, ['card-land']);
  table.clear(); table.addCard({ owner: 0, slot: 0, code: 'As', settled: true }, 1000);
  draw(1000); draw(1600); assert.deepEqual(cues, ['card-land']);
  table.dispose();
});

test('motion-off in flight settles once without restarting when motion returns', () => {
  const { cues, table, draw } = fixture();
  table.addCard({ owner: 0, slot: 0, code: 'Kh' }, 0);
  draw(100); draw(200, true); draw(700);
  assert.deepEqual(cues, ['card-land']); assert.equal(table.debug().cards[0].landed, true);
  table.dispose();
});


test('suspend settles existing flights and fan silently, including a quick resume', () => {
  const { cues, table, draw } = fixture();
  table.startFan(0, false);
  table.addCard({ owner: 0, slot: 0, code: 'Kh' }, 0);
  draw(100); table.skip(120);
  assert.equal(table.debug().fan, false);
  assert.equal(table.debug().cards[0].landed, true);
  assert.equal(table.debug().cards[0].face, true);
  draw(130); draw(500); draw(4500);
  assert.deepEqual(cues, [], 'neither old touchdown nor fan-square replays');
  table.addCard({ owner: 0, slot: 1, code: 'As' }, 5000);
  draw(5500); assert.deepEqual(cues, ['card-land'], 'a genuinely new deal still sounds');
  table.dispose();
});

test('previous canvas hand slides out before disposal and Motion Off settles it', () => {
 const {table,draw,cues}=fixture();
 table.addCard({owner:0,slot:0,code:'Kh',settled:true},0);draw(10);
 table.clear(20,true);assert.equal(table.debug().cards.length,0);assert.equal(table.debug().departing,1);
 draw(220);assert.equal(table.debug().departing,1);draw(600);assert.equal(table.debug().departing,0);
 table.addCard({owner:0,slot:0,code:'As',settled:true},700);draw(710);table.clear(720,true);draw(730,true);
 assert.equal(table.debug().departing,0);assert.deepEqual(cues,['card-slide','card-slide']);table.dispose();
});
