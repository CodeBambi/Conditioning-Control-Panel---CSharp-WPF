/* ============================================================================
 * chart/maker/triggers.js - the trigger detector, pure and on its own
 * (chart/MAKER.md).
 *
 * Everything the Track Maker needs to read a trigger out of an aligned
 * transcript, and nothing else: match a phrase over the words, fold a chant
 * into one moment, narrow by act or window or every-nth, and hand back a list
 * of `{ i0, i1, t, dur, n, reps }`. No DOM, no window, no project state, so it
 * imports cleanly in node and the smoke can run it with no browser.
 *
 * CARVE-OUT: the bodies below are copied byte for byte out of the shelved chart
 * room editor - editor/words.js (normWord, findMatches, clusterSpans),
 * editor/rules.js (actKindAt, matchRule) and editor/triggers.js (luminance,
 * labelInk, setToRule, detect). The catalogue itself is not copied: it is
 * imported from editor/triggerSets.js, which both tools share. If the chart
 * room ever lands, these two copies have to stay in step, or a trigger starts
 * meaning one thing on one page and something else on the other.
 * ==========================================================================*/

import { TRIGGER_SETS, SET_GROUPS, SET_COLORS, normalizeTriggers } from '../editor/triggerSets.js';

export { TRIGGER_SETS, SET_GROUPS, SET_COLORS, normalizeTriggers };

/** What a set matches over before its own options narrow it. */
export const DEFAULT_SCOPE = { acts: null, t0: 0, t1: null, every: 1, offset: 0 };

/* ---- matching, from editor/words.js ------------------------------------- */

/** lowercase, punctuation stripped, exactly what align.py already writes. */
export function normWord(w) {
  return String(w == null ? '' : w).toLowerCase().replace(/[^a-z0-9' ]+/g, ' ').replace(/\s+/g, ' ').trim();
}

function wordArray(words) {
  if (Array.isArray(words)) return words;
  if (words && Array.isArray(words.words)) return words.words;
  return [];
}

function span(ws, i0, i1) {
  const a = ws[i0], b = ws[i1] || a;
  const t = Number(a.t) || 0;
  return { i0, i1, t, dur: Math.max(0, (Number(b.t) || 0) + (Number(b.d) || 0) - t) };
}

function regexMatches(ws, toks, src) {
  let re;
  try { re = new RegExp(src, 'gi'); } catch { return []; }
  const joined = toks.join(' ');
  const starts = []; let at = 0;
  for (const t of toks) { starts.push(at); at += t.length + 1; }
  const out = [];
  let m, guard = 0;
  while ((m = re.exec(joined)) && guard++ < 20000) {
    if (!m[0].length) { re.lastIndex++; continue; }
    const a = m.index, b = a + m[0].length;
    let i0 = 0, i1 = 0;
    for (let i = 0; i < starts.length; i++) { if (starts[i] <= a) i0 = i; if (starts[i] < b) i1 = i; }
    out.push(span(ws, i0, Math.max(i0, i1)));
  }
  return out;
}

/**
 * findMatches(words, phrase, mode) -> [{ i0, i1, t, dur }]
 * A phrase is matched over consecutive words joined by single spaces,
 * case insensitive, punctuation stripped. Matches never overlap.
 *  - 'exact'    every phrase token equals its word
 *  - 'contains' every phrase token is inside its word ("good" finds "goodness")
 *  - 'regex'    the phrase runs over the joined text, mapped back to indices
 */
export function findMatches(words, phrase, mode = 'exact') {
  const ws = wordArray(words);
  const src = String(phrase == null ? '' : phrase);
  if (!ws.length || !src.trim()) return [];
  const toks = ws.map((w) => normWord(w.w));
  if (mode === 'regex') return regexMatches(ws, toks, src);
  const want = normWord(src).split(' ').filter(Boolean);
  if (!want.length) return [];
  const out = [];
  for (let i = 0; i + want.length <= toks.length; i++) {
    let ok = true;
    for (let k = 0; k < want.length; k++) {
      const t = toks[i + k];
      if (mode === 'contains' ? !t.includes(want[k]) : t !== want[k]) { ok = false; break; }
    }
    if (!ok) continue;
    out.push(span(ws, i, i + want.length - 1));
    i += want.length - 1;
  }
  return out;
}

/** A chant is one moment, not three: a match starting within gapSec of the end of
 * the one before is folded in, keeping the first t, growing to the last end and
 * counting the run in `reps`. A gap of 0 leaves the list alone (PR U5). */
export function clusterSpans(matches, gapSec) {
  const gap = Number(gapSec) || 0;
  if (!Array.isArray(matches)) return [];
  if (!(gap > 0)) return matches;
  const out = [];
  for (const m of matches) {
    const prev = out[out.length - 1];
    if (prev && m.t <= prev.t + prev.dur + gap) {
      prev.dur = Math.max(prev.dur, m.t + m.dur - prev.t);
      prev.i1 = m.i1;
      prev.reps++;
    } else out.push({ ...m, reps: Number(m.reps) > 0 ? Number(m.reps) : 1 });
  }
  return out;
}

/* ---- scope, from editor/rules.js ---------------------------------------- */

export function actKindAt(acts, t) {
  for (const a of acts || []) if (t >= (a.t0 || 0) && t < (a.t1 == null ? Infinity : a.t1)) return a.kind;
  return null;
}

/**
 * Every span this rule claims, after clustering, acts / window / every-offset and
 * the scope shift. `durationSec` clamps a shifted time back inside the file. A
 * rule with no `match.cluster` and no `scope.shift` behaves exactly as it did.
 */
export function matchRule(words, rule, acts = [], durationSec = Infinity) {
  if (!rule || !rule.match) return [];
  const sc = rule.scope || {};
  let out = findMatches(words, rule.match.phrase, rule.match.mode || 'exact');
  if (Number(rule.match.cluster) > 0) out = clusterSpans(out, Number(rule.match.cluster));
  const t0 = Number(sc.t0) || 0;
  const t1 = sc.t1 == null || sc.t1 === '' ? Infinity : Number(sc.t1);
  out = out.filter((m) => m.t >= t0 && m.t <= t1);
  if (Array.isArray(sc.acts) && sc.acts.length) {
    const want = new Set(sc.acts);
    out = out.filter((m) => want.has(actKindAt(acts, m.t)));
  }
  const every = Math.max(1, Math.round(Number(sc.every) || 1));
  const offset = Math.max(0, Math.round(Number(sc.offset) || 0));
  if (every > 1 || offset) out = out.filter((_, i) => i >= offset && (i - offset) % every === 0);
  const shift = Number(sc.shift) || 0;
  if (shift) out = out.map((m) => ({ ...m, t: Math.max(0, Math.min(durationSec, m.t + shift)) }));
  return out;
}

/* ---- ink and detect, from editor/triggers.js ---------------------------- */

const INK_DARK = '#0f0f1c', INK_LIGHT = '#f4f2ff';
const INK_DARK_L = 0.00526, INK_LIGHT_L = 0.90953;      // the two inks, measured once

/** WCAG relative luminance of a #rgb or #rrggbb, -1 when it is neither. */
export function luminance(hex) {
  const h = String(hex || '').replace('#', '');
  const full = h.length === 3 ? h[0] + h[0] + h[1] + h[1] + h[2] + h[2] : h;
  if (!/^[0-9a-f]{6}$/i.test(full)) return -1;
  const n = parseInt(full, 16);
  const ch = (v) => { const c = v / 255; return c <= 0.03928 ? c / 12.92 : Math.pow((c + 0.055) / 1.055, 2.4); };
  return 0.2126 * ch((n >> 16) & 255) + 0.7152 * ch((n >> 8) & 255) + 0.0722 * ch(n & 255);
}

/** Dark ink on a light fill, light ink on a dark one: whichever of the two the
 *  fill holds at more contrast, measured the way the guidelines measure it, so
 *  a mid green reads as well as a white one (PR U7 fixed the flat threshold). */
export function labelInk(hex) {
  const L = luminance(hex);
  if (L < 0) return INK_DARK;
  return (L + 0.05) / (INK_DARK_L + 0.05) >= (INK_LIGHT_L + 0.05) / (L + 0.05) ? INK_DARK : INK_LIGHT;
}

/** The rule a set is: `ts-<setId>`, its phrase and mode, the default scope. `opts`
 * is `project.triggers.opts[setId]`; `scope.offsetSec` becomes `scope.shift`. */
export function setToRule(set, opts = {}) {
  const o = opts || {}, os = o.scope || {};
  const scope = { ...DEFAULT_SCOPE, ...os };
  delete scope.offsetSec;
  scope.shift = Number(os.offsetSec == null ? os.shift : os.offsetSec) || 0;
  return { id: 'ts-' + set.id, name: set.name, enabled: true, set: set.id,
    match: { phrase: set.phrase, mode: set.mode || 'exact', cluster: Number(o.cluster == null ? set.cluster : o.cluster) || 0 },
    scope, event: { kind: o.kind || 'trigger', label: set.name }, cue: null, preset: o.preset == null ? null : o.preset };
}

/** Every match of one set over the loaded words: `[{ i0, i1, t, dur, n, reps }]`. */
export function detect(words, set, acts = [], opts = {}) {
  if (!set || !set.phrase) return [];
  return matchRule(words, setToRule(set, opts), acts, Number(opts && opts.durationSec) || Infinity)
    .map((m, n) => ({ ...m, n, reps: Number(m.reps) > 0 ? Number(m.reps) : 1 }));
}
