/* ============================================================================
 * backroom/smoke/lexicon.test.mjs - every br_* key a Back Room page asks the
 * lexicon for has its en.json row, and the row reads exactly like the page's
 * inline English (the host sends en.json's br_* rows in init.lex; other
 * languages fall back to English, never a machine translation).
 *
 *   node --test backroom/smoke/lexicon.test.mjs
 *
 * Static calls are read from the page sources: t('br_x', 'English') and
 * { key: 'br_x', fallback: 'English' }. Keys a page builds at runtime
 * ('br_cards_' + move, a ternary key) are listed below from the pages' own
 * fallback tables.
 * ==========================================================================*/

import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync, readdirSync, statSync } from 'node:fs';
import { join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const BACKROOM = resolve(fileURLToPath(import.meta.url), '../..');
const EN = resolve(BACKROOM, '../../../Localization/Languages/en.json');
const en = JSON.parse(readFileSync(EN, 'utf8'));

const pages = [];
(function walk(dir) {
  for (const n of readdirSync(dir)) {
    const p = join(dir, n);
    if (statSync(p).isDirectory()) { if (!['tests', 'smoke', 'node_modules'].includes(n)) walk(p); }
    else if (/\.(js|html)$/.test(n) && !/^(mock-server\.js|dev\.html)$/.test(n)) pages.push(p);
  }
})(BACKROOM);

const LIT = String.raw`'((?:[^'\\]|\\.)*)'|"((?:[^"\\]|\\.)*)"|` + '`' + String.raw`((?:[^` + '`' + String.raw`\\]|\\.)*)` + '`';
const CALL = new RegExp(String.raw`['"` + '`' + String.raw`](br_(?:slot|cards|roulette|wheel)_[A-Za-z0-9_]+)['"` + '`' + String.raw`]\s*,\s*(?:` + LIT + ')', 'g');
const OBJ = new RegExp(String.raw`key:\s*'(br_[a-z0-9_]+)',\s*fallback:\s*(?:` + LIT + ')', 'g');
const unquote = (s) => s.replace(/\\(['"`])/g, '$1');

const RUNTIME = {
  br_cards_hit: 'Hit', br_cards_stand: 'Stand', br_cards_double: 'Double', br_cards_split: 'Split',
  br_cards_res_net_up: 'Up {n} SP overall.', br_cards_res_net_down: 'Down {n} SP overall.',
  br_slot_gain: '+{n} SP', br_slot_spent: '-{n} SP',
  br_slot_line_emi3: '3 EMI', br_slot_line_gif3same: '3 of the same GIF', br_slot_line_sub3: '3 subliminals', br_slot_line_spiral3: '3 spirals',
  br_slot_line_gif3: '3 GIFs', br_slot_line_sub2: '2 subliminals', br_slot_line_spiral2: '2 spirals', br_slot_line_melt: 'Melt',
  br_roulette_row_sip: 'Sip row', br_roulette_row_sink: 'Sink row', br_roulette_row_deep: 'Deep row',
  br_roulette_spot_sip: 'Sip 1-12', br_roulette_spot_sink: 'Sink 13-24', br_roulette_spot_deep: 'Deep 25-36',
  br_roulette_mat_sip: 'SIP 1-12', br_roulette_mat_sink: 'SINK 13-24', br_roulette_mat_deep: 'DEEP 25-36',
};

function used() {
  const out = new Map();
  for (const f of pages) {
    const src = readFileSync(f, 'utf8');
    for (const m of [...src.matchAll(CALL), ...src.matchAll(OBJ)]) {
      const text = unquote(m[2] ?? m[3] ?? m[4]);
      if (!out.has(m[1])) out.set(m[1], new Set());
      out.get(m[1]).add(text);
    }
  }
  return out;
}

test('every br_* key a station page calls has an en.json row reading like the page', () => {
  const keys = used();
  assert.ok(keys.size > 120, `the scan found the pages' keys (${keys.size})`);
  const missing = [], differ = [];
  for (const [k, texts] of keys) {
    if (texts.size !== 1) differ.push(`${k}: the page has two fallbacks ${JSON.stringify([...texts])}`);
    const text = [...texts][0];
    if (!(k in en)) missing.push(k);
    else if (en[k] !== text) differ.push(`${k}: en.json "${en[k]}" vs page "${text}"`);
  }
  assert.deepEqual(missing, [], 'keys with no en.json row');
  assert.deepEqual(differ, [], 'rows that do not match the page');
});

test('keys the pages build at runtime have their rows too', () => {
  for (const [k, text] of Object.entries(RUNTIME)) assert.equal(en[k], text, k);
});

test('the rows carry no em dash and sit in the br_ block after the wheel', () => {
  const rows = Object.entries(en).filter(([k]) => /^br_(slot|cards|roulette|wheel)_/.test(k));
  for (const [k, v] of rows) assert.ok(!v.includes(String.fromCharCode(0x2014)), k + ' has an em dash');
  const order = Object.keys(en);
  const at = (k) => order.indexOf(k);
  assert.ok(at('br_wheel_slice_dazed') < at('br_wheel_slowly') && at('br_wheel_slowly') < at('br_slot_stage')
    && at('br_slot_stage') < at('br_cards_stage') && at('br_cards_stage') < at('br_roulette_back') && at('br_roulette_back') < at('br_ad_arcademy'),
  'wheel, slot, cards, roulette, then the room ads');
});
