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
 * fallback tables. The last test runs the scan the other way: an en.json row
 * nobody asks for is dead weight in all nine locales.
 * ==========================================================================*/

import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync, readdirSync, statSync } from 'node:fs';
import { join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const BACKROOM = resolve(fileURLToPath(import.meta.url), '../..');
const EN = resolve(BACKROOM, '../../../Localization/Languages/en.json');
const en = JSON.parse(readFileSync(EN, 'utf8'));

const pages = [], data = [];
(function walk(dir) {
  for (const n of readdirSync(dir)) {
    const p = join(dir, n);
    if (statSync(p).isDirectory()) { if (!['tests', 'smoke', 'node_modules'].includes(n)) walk(p); }
    else if (/\.(js|html)$/.test(n) && !/^(mock-server\.js|dev\.html)$/.test(n)) pages.push(p);
    else if (/\.json$/.test(n)) data.push(p);   // stations.json names its rows' labelKey outright
  }
})(BACKROOM);

/* The host's own side of the lexicon: BackRoomHostService sends every br_ row in init.lex, and
 * BackRoomMedia / BackRoomStubs name a few rows (the preset words) that no page ever spells out. */
const host = [];
(function walk(dir) {
  for (const n of readdirSync(dir)) {
    const p = join(dir, n);
    if (statSync(p).isDirectory()) walk(p); else if (/\.cs$/.test(n)) host.push(p);
  }
})(resolve(BACKROOM, '../../../Services/BackRoom'));

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

/* ------------------------------------------------------------------- the reverse
 * A row nobody asks for is dead weight in all nine locales: six deleted customization GLBs left
 * their br_custom_ rows behind (#1250). Every br_ row has to be named outright by a page, by
 * stations.json or by the C# host, or built by one of the families below. Each family says where
 * its suffixes come from, so a suffix whose table row or asset is gone has nowhere to hide. */
const read = (rel) => readFileSync(join(BACKROOM, rel), 'utf8');
const stationIds = () => new Set(JSON.parse(read('stations.json')).map((r) => r.id));

/** The picture picker's source values, read from room/hud.js so a new source with no string fails here. */
const mediaSources = () => new Set([...read('room/hud.js')
  .slice(read('room/hud.js').indexOf('MEDIA_SOURCES = ['))
  .slice(0, 200).matchAll(/\['([a-z]+)',/g)].map((m) => m[1]));

const FAMILIES = [
  // room/customization-panel.js: 'br_custom_' + a key from the panel's own item and control tables.
  { prefix: 'br_custom_', suffixes: () => {
    const src = read('room/customization-panel.js'), out = new Set();
    for (const m of src.matchAll(/\bL\(\s*'([a-z0-9_]+)'/g)) out.add(m[1]);
    for (const m of src.matchAll(/\['([a-z0-9_]+)'\s*,\s*'/g)) out.add(m[1]);
    for (const m of src.matchAll(/\?\s*'([a-z0-9_]+)'\s*:\s*'([a-z0-9_]+)'/g)) { out.add(m[1]); out.add(m[2]); }
    return out;
  } },
  // room/emi-interaction.js: 'br_emi_' + the EMI's station id + '_' + the line number, three each.
  { prefix: 'br_emi_', suffixes: () => new Set([...stationIds()].flatMap((id) => [1, 2, 3].map((n) => id + '_' + n))) },
  // stations/wheel/station.js: 'br_wheel_slice_' + the dealt slice id without its _a / _b half.
  { prefix: 'br_wheel_slice_', suffixes: () => new Set([...read('stations/wheel/mock-server.js')
    .matchAll(/\['([a-z0-9_]+)',\s*'/g)].map((m) => m[1].replace(/_[a-z]$/, ''))) },
  // room/walk.js: 'br_station_' + the row id, for a row that carries no labelKey of its own.
  { prefix: 'br_station_', suffixes: stationIds },
  // room/hud.js: 'br_opt_vol_' + one of the three level keys the Options card builds its sliders from.
  { prefix: 'br_opt_vol_', suffixes: () => new Set([...read('room/hud.js')
    .matchAll(/\['(sub|sfx|music)',\s*'/g)].map((m) => m[1])) },
  // room/hud.js: 'br_media_' + a source from MEDIA_SOURCES, and 'br_media_note_' + the same minus
  // 'auto' - 'auto' is what the player picks, never what the host resolves it to, so it has no note.
  { prefix: 'br_media_', suffixes: mediaSources },
  { prefix: 'br_media_note_', suffixes: () => new Set([...mediaSources()].filter((v) => v !== 'auto')) },
];

test('every br_* row in en.json is asked for by somebody', () => {
  const named = new Set(Object.keys(RUNTIME));
  for (const f of [...pages, ...data, ...host]) {
    for (const m of readFileSync(f, 'utf8').matchAll(/br_[a-z0-9_]+/g)) named.add(m[0]);
  }
  const families = FAMILIES.map((f) => ({ prefix: f.prefix, set: f.suffixes() }));
  const dead = Object.keys(en).filter((k) => k.startsWith('br_') && !named.has(k)
    && !families.some((f) => k.startsWith(f.prefix) && f.set.has(k.slice(f.prefix.length))));
  assert.deepEqual(dead, [], 'br_ rows nothing asks for: delete them from all nine locales');
});
