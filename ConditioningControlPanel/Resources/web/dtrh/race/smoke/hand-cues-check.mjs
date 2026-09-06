/* ============================================================================
 * race/smoke/hand-cues-check.mjs - node self-check for the hand-authored half of a chart.
 *
 *   node race/smoke/hand-cues-check.mjs      (exits 0 on pass, 1 with a count of failures)
 *
 * CHART.md is additive: a chart the auto charter wrote must normalize and cue EXACTLY as it
 * did before this PR, and a chart the editor wrote must come through with its overrides intact and
 * everything it made up thrown away. So this walks both: the untouched path first (no cue, no rule,
 * no hand, no note, no rules table), then sanitizeCue's teeth (a bad placement, an unknown bubble,
 * an unknown fx id, numbers off the road), then cueFor's override path (a wall is six bubbles, a
 * mark with fx is the fx and nothing else, a mark with no cue is nothing at all).
 * ==========================================================================*/

import { normalizeChart, sanitizeCue, demoChart, FX_IDS, EVENT_KINDS } from '../chart.js';
import { cueFor } from '../cues.js';
import { LANE_H, CEILING_H, ROAD_HALF_W } from '../consts.js';
import { KIND_BY_ID } from '../bubbleKinds.js';

let fails = 0;
const ok = (cond, what) => { if (!cond) { console.error('FAIL ' + what); fails++; } else console.log('  ok  ' + what); };

const DUR = 120;
const base = (events, extra = {}) => normalizeChart(Object.assign({
  version: 1, binSec: 0.5, energy: [0.4, 0.4], acts: [{ t0: 0, t1: DUR, kind: 'free' }],
  events, source: { name: 'hand', hash: 'h', durationSec: DUR },
}, extra));

/* ---- 1. the auto chart is untouched ------------------------------------- */
{
  const ch = base([{ id: 'a1', t: 10, kind: 'trigger', label: 'good girl' }]);
  const e = ch.events[0];
  ok(ch.hand === false && Array.isArray(ch.rules) && ch.rules.length === 0, 'a chart with no hand fields is hand:false with an empty rule table');
  ok(!('cue' in e) && !('rule' in e) && !('hand' in e) && !('note' in e), 'and its events carry no cue, rule, hand or note at all');
  const cue = cueFor(e, { rng: () => 0.5, triggerKinds: new Map() });
  ok(cue.spawn.length === 1 && Array.isArray(cue.fx) && cue.fx.length === 0, 'cueFor still reads the auto table: one bubble, no full-frame beat');
  ok(EVENT_KINDS.includes('mark'), 'EVENT_KINDS gained mark');
  ok(FX_IDS.join() === 'blink,blackout,snap,shake,melt,flash', 'FX_IDS is the CHART.md table: ' + FX_IDS.join(', '));
}

/* ---- 2. normalizeChart keeps the hand-authored fields ------------------- */
{
  const ch = base([{ id: 'r3-12', t: 30, kind: 'trigger', label: 'good girl', rule: 'r3', hand: true,
    note: 'x'.repeat(400), cue: { wall: 'pink', fx: [{ id: 'blink', strength: 0.8, dur: 0.45 }], word: 'good girl' } }],
    { hand: true, rules: [{ id: 'r3', name: 'good girl', enabled: true }] });
  const e = ch.events[0];
  ok(ch.hand === true, 'a chart saved by the editor keeps hand:true');
  ok(ch.rules.length === 1 && ch.rules[0].id === 'r3', 'and its rule table rides along for the round trip');
  ok(e.rule === 'r3' && e.hand === true, 'the event keeps the rule that made it and the author edit flag');
  ok(e.note.length === 200, 'the note is cut to 200 chars, never dropped: ' + e.note.length);
  ok(e.cue.wall === 'pink' && e.cue.fx.length === 1 && e.cue.word === 'good girl', 'and the cue survives whole');
  const marks = base([{ id: 'h7', t: 40, kind: 'mark', label: 'the snap', cue: { jump: 7 } }]);
  ok(marks.events.length === 1 && marks.events[0].kind === 'mark', 'a mark is a legal event kind now');
}

/* ---- 3. sanitizeCue throws out what it does not know -------------------- */
{
  const c = sanitizeCue({
    spawn: [
      { kindId: 'pink', placement: 'lane', x: 0, h: LANE_H, at: 0 },
      { kindId: 'pink', placement: 'kerb', x: 0, h: LANE_H, at: 0 },
      { kindId: 'not-a-bubble', placement: 'air', x: 0, h: 2.6, at: 0 },
      { kindId: 'golden', placement: 'air', x: 99, h: 999, at: 900 },
    ],
    fx: [{ id: 'blink' }, { id: 'seizure', strength: 1 }, { id: 'blackout', strength: 4, dur: 90 }],
    wall: 'not-a-bubble', jump: 40, boost: 40, density: 40, holdSec: 4000, mood: 'furious', mix: 'nonsense',
  }, DUR);
  ok(c.spawn.length === 2, 'a bad placement and an unknown bubble kind are dropped: ' + c.spawn.length + ' of 4 kept');
  ok(c.spawn.every((s) => KIND_BY_ID[s.kindId]), 'every surviving spawn names a real bubble');
  const far = c.spawn[1];
  ok(Math.abs(far.x) <= ROAD_HALF_W - 0.4 && far.h <= CEILING_H && far.at <= 30, 'x, h and at are clamped onto the road, into the tube and near the word');
  ok(c.fx.length === 2 && c.fx.map((f) => f.id).join() === 'blink,blackout', 'an unknown fx id is dropped: ' + c.fx.map((f) => f.id).join(', '));
  ok(c.fx[0].strength === 1 && c.fx[0].dur === null, 'a bare fx defaults to full strength and the table duration');
  ok(c.fx[1].strength === 1 && c.fx[1].dur === 10, 'and strength and dur are clamped to 0..1 and 0..10');
  ok(c.wall === null && c.mix === null && c.mood === null, 'an unknown wall, mix and mood all come back null, never played');
  ok(c.jump === 12 && c.boost === 10 && c.density === 4 && c.holdSec === DUR, 'the knobs are capped: jump ' + c.jump + ', boost ' + c.boost + ', hold ' + c.holdSec);
  ok(sanitizeCue(null) === null && sanitizeCue({}) === null && sanitizeCue(7) === null, 'nothing in, nothing out (no throw)');
}

/* ---- 4. cueFor takes the author at their word --------------------------- */
{
  const ctx = { rng: () => 0.5, triggerKinds: new Map() };
  const wall = base([{ id: 'w1', t: 20, kind: 'trigger', label: 'good girl',
    cue: { wall: 'pink', fx: [{ id: 'blink', strength: 0.8, dur: 0.45 }], word: 'good girl' } }]).events[0];
  const wc = cueFor(wall, ctx);
  ok(wc.spawn.length === 6, 'a wall is six bubbles the kart cannot steer around: ' + wc.spawn.length);
  ok(wc.spawn.filter((s) => s.placement === 'lane').length === 5, 'five across the road');
  ok(wc.spawn.map((s) => s.x).join() === '-2.2,-1.1,0,1.1,2.2,0', 'on the five lanes plus one over the top');
  ok(wc.spawn.filter((s) => s.placement === 'lane').every((s) => s.h === LANE_H), 'the road row sits at LANE_H');
  ok(wc.spawn.every((s) => s.kindId === 'pink' && s.at === 0), 'all six are the wall kind, all on the word');
  ok(wc.fx.length === 1 && wc.fx[0].id === 'blink' && wc.fx[0].strength === 0.8, 'and the blink rides with it');
  ok(wc.word === 'good girl' && wc.jump === 0 && wc.mix === null, 'the fields the author left alone stay at their defaults');

  const mark = base([{ id: 'h7', t: 40, kind: 'mark', label: 'the snap', hand: true,
    cue: { fx: [{ id: 'snap', strength: 1 }, { id: 'shake', strength: 0.6, dur: 0.4 }], jump: 7, mix: 'spiral', mood: 'streamed' } }]).events[0];
  const mc = cueFor(mark, ctx);
  ok(mc.spawn.length === 0, 'a mark can be a beat with no bubble at all');
  ok(mc.fx.map((f) => f.id).join() === 'snap,shake', 'its fx come through in order: ' + mc.fx.map((f) => f.id).join(', '));
  ok(mc.jump === 7 && mc.mix === 'spiral' && mc.mood === 'streamed', 'along with the jump, the mix and the mood');

  const bare = base([{ id: 'h8', t: 50, kind: 'mark', label: 'nothing here' }]).events[0];
  ok(cueFor(bare, ctx) === null, 'a mark with no cue is worth nothing: cueFor returns null');

  const hand = sanitizeCue({ spawn: [{ kindId: 'golden', placement: 'air' }] }, DUR);
  const hc = cueFor({ kind: 'mark', cue: hand }, ctx);
  ok(hc.spawn[0].h === 0 && hc.spawn[0].at === 0, 'a spawn with no height keeps the one the author saved (0 is a choice)');
}

/* ---- 5. the demo track carries both, so the headless run covers them ---- */
{
  const ch = demoChart(240);
  const hands = ch.events.filter((e) => e.cue);
  ok(hands.length === 2, 'the demo track has two hand events: ' + hands.map((e) => e.kind).join(', '));
  const wall = hands.find((e) => e.cue.wall), mark = hands.find((e) => e.kind === 'mark');
  ok(!!wall && wall.cue.wall === 'pink' && wall.cue.fx[0].id === 'blink', 'a wall of pink with a blink, on a trigger');
  ok(!!mark && mark.cue.fx.map((f) => f.id).join() === 'snap,shake' && mark.cue.jump === 7 && mark.cue.mix === 'spiral', 'and a mark that snaps, shakes, jumps and pours a spiral');
  const wa = ch.acts.find((a) => a.t0 <= wall.t && wall.t < a.t1), ma = ch.acts.find((a) => a.t0 <= mark.t && mark.t < a.t1);
  ok(wa && wa.kind === 'triggers', 'the wall lands in the toybox: ' + (wa && wa.kind));
  ok(ma && ma.kind === 'build', 'the snap lands in the climb: ' + (ma && ma.kind));
}

console.log(fails ? '\nhand-cues-check: ' + fails + ' failure(s)' : '\nhand-cues-check: all good');
process.exit(fails ? 1 : 0);
