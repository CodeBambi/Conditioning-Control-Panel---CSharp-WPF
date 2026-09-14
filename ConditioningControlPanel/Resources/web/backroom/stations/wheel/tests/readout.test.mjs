import { test } from 'node:test';
import assert from 'node:assert/strict';
import { createReadout, jackpotChip } from '../readout.js';

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

/* ------------------------------------ the jackpot chip, C2 must-hit-by (10.16.E) */

const t = (key, fallback, vars = {}) => String(fallback).replace(/\{(\w+)\}/g, (_, k) => (k in vars ? vars[k] : `{${k}}`));
const fmt = n => Number(n || 0).toLocaleString('en-US');

test('the chip prints the odds until the pot is at the must-hit line', () => {
  assert.equal(jackpotChip({ amount: 425, odds: '1 in 6,644', wonToday: false, eligible: true, mustHit: false }, t, fmt),
    'Jackpot 425 SP, 1 in 6,644');
  assert.equal(jackpotChip({ amount: 425, odds: '1 in 6,644' }, t, fmt), 'Jackpot 425 SP, 1 in 6,644',
    'a server that does not send mustHit yet reads as false');
});

test('MUST HIT takes the odds line, and the amount is still shown (Law I)', () => {
  assert.equal(jackpotChip({ amount: 1000, odds: '1 in 6,644', wonToday: false, eligible: true, mustHit: true }, t, fmt),
    'Jackpot 1,000 SP, MUST HIT');
});

test('mustHit is a ROOM fact: a young account still sees it', () => {
  assert.equal(jackpotChip({ amount: 1000, odds: 'never', eligible: false, mustHit: true }, t, fmt),
    'Jackpot 1,000 SP, MUST HIT', 'eligible is what the Odds panel says, separately');
});

test('the chip never throws on a jackpot the server did not send', () => {
  assert.equal(jackpotChip(null, t, fmt), 'Jackpot 0 SP, ');
  assert.equal(jackpotChip({}, t, fmt), 'Jackpot 0 SP, ');
  assert.equal(jackpotChip({ amount: 'lots', mustHit: 'yes' }, t, fmt), 'Jackpot 0 SP, ',
    'only a real boolean true turns MUST HIT on');
});

test('the lexicon owns both strings (Law VII)', () => {
  const de = (key, fallback, vars = {}) => String({ br_wheel_jackpot: 'Topf {n} SP, {odds}', br_wheel_must_hit: 'MUSS FALLEN' }[key] || fallback)
    .replace(/\{(\w+)\}/g, (_, k) => (k in vars ? vars[k] : `{${k}}`));
  assert.equal(jackpotChip({ amount: 1000, mustHit: true }, de, fmt), 'Topf 1,000 SP, MUSS FALLEN');
});
