/* ============================================================================
 * shell/smoke/exit-sign-check.mjs - THE PAINTED DOOR AND ITS SIGN.
 *
 *   node shell/smoke/exit-sign-check.mjs      (from Resources/web/arcademy; 0 on pass)
 *
 * Two reports, one rect and one stylesheet:
 *
 *   ccp-bugs#1165  "Sort room on app.cclabs.app/arcademy does not have a
 *                   'return to campus' door in the image" - the SCENES row had
 *                   written the painted door off as an alcove, so the room
 *                   rendered no `.arm-exit` at all.
 *   ticket 1544742846899294288  the sign under a painted door at phone width -
 *                   pinned back inside the frame on 2026-09-03, but still
 *                   drawn at LANDSCAPE's scale, which on an upright phone is a
 *                   ~6px smudge.
 *
 * Neither half can be asserted by importing the module: `SCENES` is private and
 * the geometry that matters is the product of a stage-pixel rect, a stage-pixel
 * type size and the `fit()` scale of a real phone. So this reads the two files
 * as TEXT (test-hostfixes.mjs's tripwire shape) and does the arithmetic that
 * the eye was doing badly. It is a floor, not a substitute for a shot at
 * 390x844.
 * ==========================================================================*/

import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, join } from 'node:path';

const HERE = dirname(fileURLToPath(import.meta.url));
const SHELL = join(HERE, '..');

let fails = 0;
const ok = (cond, what) => { if (!cond) { console.error('FAIL ' + what); fails++; } else console.log('  ok  ' + what); };
const ge = (got, want, what) => ok(got >= want, what + ' (got ' + got + ', want >= ' + want + ')');
const le = (got, want, what) => ok(got <= want, what + ' (got ' + got + ', want <= ' + want + ')');

const roomJs = readFileSync(join(SHELL, 'room.js'), 'utf8');
const roomsCss = readFileSync(join(SHELL, 'rooms.css'), 'utf8');
const recordsJs = readFileSync(join(SHELL, 'recordsroom.js'), 'utf8');

/* ---------------------------------------------------- the plane's own law -- */
const num = (src, name) => {
  const m = src.match(new RegExp('const\\s+' + name + '\\s*=\\s*(\\d+)'));
  return m ? Number(m[1]) : NaN;
};
const STAGE_W = num(roomJs, 'STAGE_W');
const STAGE_H = num(roomJs, 'STAGE_H');
const APRON_STAGE_TOP = num(roomJs, 'APRON_STAGE_TOP');
ok(STAGE_W === 1376 && STAGE_H === 768, 'the stage plane is still 1376x768');
ok(APRON_STAGE_TOP === 640, 'the apron still owns the floor from y640');

/* --------------------------------------------------- every painted door --- */
/* room.js's SCENES rows, read off the source: `key: Object.freeze({ ... })`
 * with an optional `exit: Object.freeze([x, y, w, h])` inside it. */
const scenes = {};
const rowRe = /\n {2}([a-z_]+): Object\.freeze\(\{([\s\S]*?)\n {2}\}\),/g;
let m;
while ((m = rowRe.exec(roomJs))) {
  const body = m[2];
  const rect = (field) => {
    const r = body.match(new RegExp(field + ':\\s*Object\\.freeze\\(\\[([-\\d,\\s]+)\\]\\)'));
    return r ? r[1].split(',').map((v) => Number(v.trim())) : null;
  };
  scenes[m[1]] = { hotspot: rect('hotspot'), exit: rect('exit'), freeSwim: rect('freeSwim') };
}
ok(Object.keys(scenes).length === 10, 'all ten class rooms are still in the table (got ' + Object.keys(scenes).length + ')');

/* The Records Office storeroom is the fifth `.arm-exit` in the building - a
 * scene.js hotspot with `quiet: true`, which rooms.css skins with the same
 * class. It rides every rule below, so it is measured with the others. */
const doorRe = /door: Object\.freeze\(\[([-\d,\s]+)\]\)/;
const storeroom = recordsJs.match(doorRe);
ok(!!storeroom, 'the records storeroom rect is still readable');

const doors = [];
for (const [key, row] of Object.entries(scenes)) if (row.exit) doors.push([key, row.exit]);
if (storeroom) doors.push(['records_storeroom', storeroom[1].split(',').map((v) => Number(v.trim()))]);

/* ================================= 1. THE SORTING ROOM HAS ITS DOOR ===== */
{
  const sort = scenes.sort;
  ok(!!sort, 'the sort row is in SCENES');
  ok(!!(sort && sort.exit), 'ccp-bugs#1165: the sorting room has a painted exit rect');
  if (sort && sort.exit) {
    const [x, y, w, h] = sort.exit;
    ge(x, 0, 'sort exit: left edge is on the plane');
    le(x + w, STAGE_W, 'sort exit: right edge is on the plane');
    le(y + h, APRON_STAGE_TOP, 'sort exit: the apron does not sit on it');
    ge(w, 60, 'sort exit: wide enough to be a thumb target at half scale');
    ge(h, 60, 'sort exit: tall enough to be a thumb target at half scale');
    /* The one lit thing may never share pixels with the quiet way out, or the
     * breath and the rim fight over the same press. */
    const [hx, hy, hw, hh] = sort.hotspot;
    const overlap = x < hx + hw && hx < x + w && y < hy + hh && hy < y + h;
    ok(!overlap, 'sort exit: does not overlap the conveyor hotspot');
  }
}

/* ================================= 2. EVERY DOOR IS ON THE PLANE ======== */
for (const [key, [x, y, w, h]] of doors) {
  ge(x, 0, key + ': exit left edge on the plane');
  le(x + w, STAGE_W, key + ': exit right edge on the plane');
  ge(y, 0, key + ': exit top edge on the plane');
}
ok(doors.length === 5, 'four painted class doors plus the storeroom (got ' + doors.length + ')');

/* ================================= 3. THE SIGN AT PHONE WIDTH ========== */
/* The phone rules, read back out of the stylesheet rather than retyped. */
const ruleBody = (selector) => {
  const i = roomsCss.indexOf(selector + ' {');
  if (i < 0) return null;
  return roomsCss.slice(i + selector.length + 2, roomsCss.indexOf('}', i));
};
const px = (body, prop) => {
  if (!body) return NaN;
  const r = body.match(new RegExp('(?:^|[;\\s])' + prop + ':\\s*(-?[\\d.]+)px'));
  return r ? Number(r[1]) : NaN;
};

const LAND = ruleBody('html.arc-mobile .arm-hot.arm-exit .arm-hot-tag');
const PORT_OWN = ruleBody('html.arc-mobile[data-arc-orient="portrait"] .arm-hot.arm-exit .arm-hot-tag');
/* Portrait is `html.arc-mobile` too, so with no rule of its own it RESOLVES to
 * the landscape body - which is the whole bug, and modelling the cascade is
 * what makes the numbers below the real ones rather than NaN. */
const PORT = PORT_OWN || LAND;
ok(!!LAND, 'the phone sign rule is still in rooms.css');
ok(!!PORT_OWN, 'portrait has a sign rule of its own');

/* The 2026-09-03 pin: the sign hangs off the DOOR'S right edge and grows left,
 * so it can never reach the frame however long the localised string runs. */
ok(/right:\s*0/.test(LAND || '') && /left:\s*auto/.test(LAND || '') && /transform:\s*none/.test(LAND || ''),
  'the sign is still pinned to the door right edge, not centred on it');
ok(!/left:\s*50%/.test(PORT_OWN || '') && !/transform:/.test(PORT_OWN || ''),
  'portrait does not undo the pin');

/* `fit()` is min(w / STAGE_W, h / STAGE_H). These are the phones in range. */
const fit = (w, h) => Math.min(w / STAGE_W, h / STAGE_H);
const PHONES = [
  ['360x780 portrait', fit(360, 780), PORT],
  ['390x844 portrait', fit(390, 844), PORT],
  ['430x932 portrait', fit(430, 932), PORT],
  ['844x390 landscape', fit(844, 390), LAND],
];

/* THE SMUDGE FLOOR. A sign is decoration, but decoration that cannot be read is
 * a bug with a nicer name - 10 real px is the corkboard's own floor for body
 * copy on a phone (recordsroom's FIT), so it is the floor here too. */
for (const [name, s, body] of PHONES) {
  const fontStage = px(body, 'font-size');
  ge(Number((fontStage * s).toFixed(1)), 10, name + ': the sign renders at 10+ real px');
}

/* THE FRAME. Tracked display caps measure about 0.74em an advance (0.62 of
 * glyph plus the 0.12em `letter-spacing` the base rule sets). 24 characters is
 * "Back to campus" with room for a language that spends twice the words. */
const LABEL_CHARS = 24;
for (const [name, , body] of PHONES) {
  const font = px(body, 'font-size');
  const pad = (() => {
    const r = (body || '').match(/padding:\s*[\d.]+px\s+([\d.]+)px/);
    return r ? Number(r[1]) * 2 : 0;
  })();
  const wide = LABEL_CHARS * font * 0.74 + pad + 4;
  for (const [key, [x, , w]] of doors) {
    ge(Math.round(x + w - wide), 0, name + ' / ' + key + ': the sign stays on the plane');
  }
}

/* THE FLOOR LINE. `bottom: -N` hangs the sign below the door; the apron owns
 * everything past y640, so the tag has to finish above it. */
for (const [name, , body] of PHONES) {
  const drop = -px(body, 'bottom');
  for (const [key, [, y, , h]] of doors) {
    le(y + h + drop, APRON_STAGE_TOP, name + ' / ' + key + ': the sign finishes above the apron');
  }
}

console.log(fails ? '\n' + fails + ' FAILED' : '\nall green');
process.exit(fails ? 1 : 0);
