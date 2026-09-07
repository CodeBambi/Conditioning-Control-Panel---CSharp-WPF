/* ============================================================================
 * chart/smoke/triggers-check.mjs - the trigger catalogue and the detector,
 * checked under node with no browser.
 *
 *   node chart/smoke/triggers-check.mjs     (from Resources/web/dtrh; 0 on pass, 1 on any failure)
 *
 * CARVE-OUT: this is the pure half of chart/smoke/triggers-check.mjs from the
 * Track Maker chain, trimmed to the two files this branch carries
 * (maker/triggers.js, editor/triggerSets.js). Everything that wanted a project,
 * a words file on disk, or the editor's walk / counts / allSets stays on the
 * chain. What is left is what the maker leans on: if a phrase stops matching,
 * or a chant stops folding into one moment, or a scope stops narrowing, the
 * maker puts its bubbles in the wrong places and says nothing about it.
 * ==========================================================================*/

import { TRIGGER_SETS, SET_GROUPS, SET_COLORS, normalizeTriggers } from '../editor/triggerSets.js';
import { clusterSpans, detect, findMatches, labelInk, matchRule, normWord, setToRule } from '../maker/triggers.js';

let fails = 0;
const ok = (cond, what) => { if (!cond) { console.error('FAIL ' + what); fails++; } else console.log('  ok  ' + what); };
const eq = (got, want, what) => ok(got === want, what + ' (got ' + JSON.stringify(got) + ', want ' + JSON.stringify(want) + ')');
const same = (got, want, what) => eq(JSON.stringify(got), JSON.stringify(want), what);
const setById = (id) => TRIGGER_SETS.find((s) => s.id === id);
const hex = (c) => /^#[0-9a-f]{6}$/i.test(c);

/* ---- 1. the catalogue is sound ------------------------------------------- */
const ids = TRIGGER_SETS.map((s) => s.id);
eq(new Set(ids).size, ids.length, 'every set id is unique');
ok(TRIGGER_SETS.every((s) => s.name && s.phrase && s.preset), 'every set has a name, a phrase and a preset');
ok(TRIGGER_SETS.every((s) => hex(s.color)), 'every colour is a six digit hex');
ok(TRIGGER_SETS.every((s) => SET_GROUPS.some((g) => g[0] === s.group)), 'every group is one of the four');
ok(SET_COLORS.length === 12 && SET_COLORS.every(hex), 'the colour rotation is twelve hexes');
const bad = TRIGGER_SETS.filter((s) => {
  if (s.mode !== 'regex') return normWord(s.phrase) !== s.phrase;
  try { new RegExp(s.phrase, 'gi'); return false; } catch (e) { return true; }
});
ok(!bad.length, 'every regex compiles and every plain phrase is already normalised' + (bad.length ? ' (' + bad.map((s) => s.id).join(',') + ')' : ''));
eq(normWord('Good, GIRL!'), 'good girl', 'normWord lowercases and drops the punctuation');
eq(normWord(null), '', 'normWord of nothing');
eq(labelInk('#ffffff'), '#0f0f1c', 'dark ink on a light fill');
eq(labelInk('#4060c0'), '#f4f2ff', 'light ink on a dark fill');
eq(labelInk(''), '#0f0f1c', 'dark ink on junk');
ok(TRIGGER_SETS.every((s) => labelInk(s.color) === '#0f0f1c' || labelInk(s.color) === '#f4f2ff'), 'every set colour picks one of the two inks');

/* ---- 2. findMatches, all three modes ------------------------------------- */
const SCRIPT = ('welcome good girl deeper drop and now good girl sink so soft drop listen pink '
  + 'good girl empty float drop and you are here good girl now drop done goodness').split(' ');
const WORDS = SCRIPT.map((w, i) => ({ i, t: i * 2, d: 0.5, w, conf: 0.9 }));
const ACTS = [{ id: 0, t0: 0, t1: 20, kind: 'induction' }, { id: 1, t0: 20, t1: 60, kind: 'triggers' }];
eq(findMatches(WORDS, 'good girl', 'exact').length, 4, 'exact matches the whole word only');
eq(findMatches(WORDS, 'good', 'exact').length, 4, 'exact does not reach into goodness');
eq(findMatches(WORDS, 'good', 'contains').length, 5, 'contains does');
eq(findMatches(WORDS, 'goo\\w+ girl', 'regex').length, 4, 'regex runs over the joined text');
eq(findMatches(WORDS, 'nope', 'exact').length, 0, 'a phrase the file never says');
eq(findMatches(WORDS, '', 'exact').length + findMatches(null, 'good', 'exact').length, 0, 'no phrase or no words');
eq(findMatches(WORDS, '((', 'regex').length, 0, 'a regex that does not compile is no matches, not a throw');
same(findMatches(WORDS, 'good girl', 'exact')[0], { i0: 1, i1: 2, t: 2, dur: 2.5 }, 'a match carries its word indices and its span');

/* ---- 3. detect: numbering, scope, the words wrapper ---------------------- */
const gg = detect(WORDS, setById('good-girl'), ACTS, {});
eq(gg.length, 4, 'good girl in the fixture');
eq(gg[0].t, 2, 'the first one is at 2 s');
eq(gg[3].n, 3, 'a marker carries its index');
eq(gg[0].reps, 1, 'a lone match is one rep');
eq(detect({ words: WORDS }, setById('good-girl'), [], {}).length, 4, 'detect takes a whole words file as well as an array');
eq(detect(WORDS, setById('w-drop'), ACTS, {}).length, 4, 'drop in the fixture');
eq(detect(WORDS, setById('uniform-lock'), ACTS, {}).length, 0, 'a set the file never says');
eq(detect(null, setById('good-girl'), ACTS, {}).length + detect(WORDS, null, ACTS, {}).length, 0, 'no words or no set');
eq(detect(WORDS, setById('good-girl'), ACTS, { scope: { acts: ['induction'] } }).length, 2, 'the acts scope narrows it');
eq(detect(WORDS, setById('good-girl'), ACTS, { scope: { t0: 20 } }).length, 2, 'a window scope narrows it');

/* ---- 4. clustering a chant ----------------------------------------------- */
const CHANT = [0, 0.4, 0.8, 1.2, 1.6, 2.0, 10, 10.4].map((t, i) => ({ i, t, d: 0.35, w: i % 2 ? 'doll' : 'bimbo', conf: 1 }));
eq(findMatches(CHANT, 'bimbo doll', 'exact').length, 4, 'four bimbo doll before clustering');
const bd = detect(CHANT, setById('bimbo-doll'), [], {});
eq(bd.length, 2, 'the chant is one marker plus the loner');
eq(bd[0].reps, 3, 'the chant counts three reps');
eq(bd[0].t, 0, 'the chant starts where it started');
eq(Number(bd[0].dur.toFixed(2)), 2.35, 'the chant runs to the last word');
eq(bd[1].reps + '@' + bd[1].t, '1@10', 'the loner is one rep at its own time');
eq(clusterSpans(findMatches(CHANT, 'bimbo doll', 'exact'), 0).length, 4, 'a gap of zero leaves the list alone');
eq(clusterSpans(null, 1.5).length, 0, 'clustering nothing');

/* ---- 5. setToRule -------------------------------------------------------- */
const r = setToRule(setById('good-girl'));
eq(r.id, 'ts-good-girl', 'the rule id scheme');
eq(r.set, 'good-girl', 'the rule carries the set id');
eq(r.enabled, true, 'the rule is on');
same(r.match, { phrase: 'good girl', mode: 'exact', cluster: 0 }, 'the match comes from the set');
same(r.event, { kind: 'trigger', label: 'good girl' }, 'the event names the set');
same(r.scope, { acts: null, t0: 0, t1: null, every: 1, offset: 0, shift: 0 }, 'the default scope');
eq(setToRule(setById('bimbo-doll')).match.cluster, 1.5, 'a clustered set keeps its gap');
const rOpt = setToRule(setById('good-girl'), { kind: 'chant', preset: 'pink-blink', scope: { every: 2, offset: 1, offsetSec: -0.2 } });
eq(rOpt.event.kind, 'chant', 'opts pick the event kind');
eq(rOpt.preset, 'pink-blink', 'opts pick the preset');
eq(rOpt.scope.every, 2, 'opts pick every');
eq(rOpt.scope.shift, -0.2, 'offsetSec becomes the scope shift');
eq(rOpt.scope.offsetSec, undefined, 'offsetSec does not survive into the rule scope');

/* ---- 6. matchRule: every, offset, shift, acts ---------------------------- */
const scoped = (sc, dur) => matchRule(WORDS, setToRule(setById('good-girl'), { scope: sc }), ACTS, dur);
eq(scoped({ every: 2 }).length, 2, 'every 2 keeps half of them');
same(scoped({ every: 2 }).map((m) => m.t), [2, 30], 'and keeps the first and the third');
same(scoped({ every: 2, offset: 1 }).map((m) => m.t), [14, 48], 'an offset starts the count later');
same(scoped({ acts: ['triggers'] }).map((m) => m.t), [30, 48], 'the acts scope keeps only the ones inside that act');
eq(scoped({ acts: ['wake'] }).length, 0, 'an act the file has none of');
eq(scoped({ offsetSec: -0.5 }, 60)[1].t, 13.5, 'a shift moves every match');
eq(scoped({ offsetSec: -0.5 }, 60)[0].t, 1.5, 'a shift clamps at the start of the file');
eq(scoped({ offsetSec: 900 }, 60)[0].t, 60, 'a shift clamps at the end of the file');
eq(scoped({ offsetSec: 900 }, undefined)[0].t, 902, 'no duration means no clamp');
eq(matchRule(CHANT, { match: { phrase: 'bimbo doll', mode: 'exact', cluster: 1.5 } }, []).length, 2, 'match.cluster folds the run');
eq(matchRule(CHANT, { match: { phrase: 'bimbo doll', mode: 'exact' } }, []).length, 4, 'no match.cluster leaves it alone');
eq(matchRule(WORDS, null, []).length, 0, 'no rule is no matches');

/* ---- 7. normalizeTriggers ------------------------------------------------ */
same(normalizeTriggers(null), { version: 1, on: [], custom: [], opts: {} }, 'a blank one is the empty shape');
const stored = {
  version: 1, on: ['good-girl', 'bambi-sleep', 'good-girl', 7, ''],
  custom: [{ id: 'c-sink', name: 'sink', group: 'custom', phrase: 'sink', mode: 'exact', color: '#78ffbe' },
    { id: 'c-sink', phrase: 'sink again' }, { name: 'no phrase' }, null, 'junk'],
  opts: { 'good-girl': { preset: 'pink-blink', strength: 0.8, scope: { every: 2 } }, bad: 3 },
};
const t = normalizeTriggers(stored);
same(t.on, ['good-girl', 'bambi-sleep'], 'on is deduped and cleaned');
eq(t.custom.length, 1, 'one custom set survives, the twin does not');
eq(t.custom[0].phrase + ' ' + t.custom[0].color, 'sink #78ffbe', 'the custom set keeps its phrase and its colour');
eq(t.opts['good-girl'].strength, 0.8, 'the options came through');
eq(t.opts.bad, undefined, 'junk options are dropped');
same(normalizeTriggers(t), t, 'a round trip is stable');
eq(normalizeTriggers('nope').on.length, 0, 'junk in, empty out');

console.log(fails ? '\n' + fails + ' FAILED' : '\ntriggers-check: all good');
process.exit(fails ? 1 : 0);
