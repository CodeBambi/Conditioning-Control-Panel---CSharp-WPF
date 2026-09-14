import { test } from 'node:test';
import assert from 'node:assert/strict';
import { createReadout } from '../readout.js';

function hookCtx(sp) {
  const room = { sp, owed: 0, shown: null, thuds: 0 };
  const paint = () => String(room.shown ?? Math.max(0, room.sp - room.owed));
  return { room, paint, ctx: { hostBack: true, sp: () => room.sp,
    spReadout: { set(v) { room.shown = v; }, owe(n) { room.owed = n; }, thud() { room.thuds++; }, target() { return null; } } } };
}

test('with ctx.spReadout the room chip rule holds the pay back until THE BANK lands it (Law I)', () => {
  const { room, paint, ctx } = hookCtx(57);
  const r = createReadout({ ctx, doc: null });
  assert.equal(r.kind, 'hook');
  room.sp = 62;                 // the loader adopts body.sp the moment the reply lands
  r.owe(5); r.setServer(62);
  assert.equal(paint(), '57');
  assert.equal(r.value, 57);
  r.show(59);
  assert.equal(paint(), '59');
  r.show(99);
  assert.equal(r.value, 62, 'a tick is never ahead of the server');
  r.thud(false);
  assert.equal(room.thuds, 1);
  r.settle();
  assert.equal(paint(), '62');
  assert.equal(room.shown, null);
});

test('Back mid-flight hands the plain server number over', () => {
  const { paint, ctx } = hookCtx(62);
  const r = createReadout({ ctx, doc: null });
  r.setServer(62); r.owe(5); r.show(58);
  r.dispose();
  assert.equal(paint(), '62');
});

test('standalone: the station chip, formatted', () => {
  const own = { textContent: '' };
  const r = createReadout({ ctx: { sp: () => 40 }, own, doc: null, format: n => `${n} SP` });
  assert.equal(r.kind, 'own');
  r.setServer(40);
  assert.equal(own.textContent, '40 SP');
  r.owe(100);
  assert.equal(own.textContent, '0 SP', 'never below zero');
});
