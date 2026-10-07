/* ============================================================================
 * smoke/niche-choice-smoke.mjs - the saved picture choice (2026-09-30).
 *
 * The owner's rule, as node checks: the first start asks, the pick is saved,
 * the second start does not ask, and the manager's edits persist. The shared
 * logic is backroom/shared/niches/choice.js; the host side is modelled by the
 * two stores that really keep it (the desktop's AppSettings rule and the web
 * shim's localStorage rule), fed through the chess page's own bridge.
 *
 *   node smoke/niche-choice-smoke.mjs        (no server, no browser)
 * ==========================================================================*/

import assert from 'node:assert/strict';
import {
  readChoice, needsChoice, choose, choiceFrame, sameChoice, editNiches, pickOf, statusLine, ownLine,
  toggleNiche, addNiche, removeNiche, FLAVOURS, MINE,
} from '../../backroom/shared/niches/choice.js';

let checks = 0;
const ok = (cond, name) => { assert.ok(cond, name); checks++; };
const eq = (a, b, name) => { assert.deepEqual(a, b, name); checks++; };

/* ---- the table is Breakout's ------------------------------------------- */
eq(FLAVOURS.map((f) => f.id), ['trance', 'pink', 'frills', 'shiny', 'censored'], 'five flavours, in Breakout order');
eq(MINE.id, 'mine', 'Mine is the sixth');

/* ---- nothing from the host: nothing is asked ---------------------------- */
eq(readChoice(null), null, 'no frame, no state');
ok(!needsChoice(null), 'no host: never ask');

/* ---- a fresh desktop: never chosen -------------------------------------- */
const fresh = readChoice({ flavour: '', last: '', custom: {}, online: true, appWide: false, chosen: false });
ok(needsChoice(fresh), 'first start asks');
ok(!needsChoice(fresh, 'classic'), 'Classic has no pictures: no ask');
eq(pickOf(fresh), 'own', 'nothing picked reads as own');
ok(fresh.library && fresh.canOnline, 'absent capability flags read as the desktop');

/* ---- picking ------------------------------------------------------------ */
const pink = choose(fresh, 'pink');
ok(pink.chosen && pink.online && pink.flavour === 'pink' && pink.last === 'pink', 'a flavour pick is chosen, online, that flavour');
ok(!needsChoice(pink), 'chosen: no ask');
const own = choose(fresh, 'own');
ok(own.chosen && !own.online && own.flavour === '', '"no online pictures" is a choice too');
ok(!needsChoice(own), 'own chosen: no ask');
const app = choose({ ...fresh, appWide: true }, 'app');
ok(app.online && app.flavour === '' && pickOf(app) === 'app', 'the app\'s pictures: online, no flavour');
const phone = readChoice({ online: false, canOnline: false, library: true, chosen: false });
eq(choose(phone, 'pink').online, false, 'a host that cannot fetch online makes every pick "own"');

/* ---- the frame ---------------------------------------------------------- */
{
  const f = choiceFrame(pink);
  eq([f.flavour, f.online, f.chosen], ['pink', true, true], 'frame: flavour, online, chosen');
  ok(f.subs.includes('bimbofication') && !f.subs.includes('bimbo'), 'frame: the niches on, extras off');
  eq(choiceFrame(own).subs, [], 'own sends no niches');
  eq(choiceFrame({ ...pink, online: false }).flavour, '', 'online off drops the flavour');
  ok(sameChoice(pink, { ...pink }), 'same state, same frame');
  ok(!sameChoice(pink, own), 'different picks differ');
}

/* ---- manager edits ------------------------------------------------------ */
let s = pink;
s = editNiches(s, 'pink', (f, c) => toggleNiche(f, c, 'Bimbos')).state;
ok(!choiceFrame(s).subs.includes('Bimbos'), 'a core niche switched off');
s = editNiches(s, 'pink', (f, c) => toggleNiche(f, c, 'bimbo')).state;
ok(choiceFrame(s).subs.includes('bimbo'), 'an extra switched on');
{
  const r = editNiches(s, 'pink', (f, c) => addNiche(f, c, 'https://www.reddit.com/r/Bimbo_Lounge/'));
  eq(r.error, '', 'a pasted link adds its niche');
  s = r.state;
  ok(choiceFrame(s).subs.includes('Bimbo_Lounge'), 'the added niche is on');
  ok(editNiches(s, 'pink', (f, c) => addNiche(f, c, 'not a niche!')).error.startsWith('That'), 'junk refuses with a line');
  ok(editNiches(s, 'pink', (f, c) => addNiche(f, c, 'r/bimbofication')).error === 'Already in.', 'a duplicate refuses');
}
s = editNiches(s, 'pink', (f, c) => removeNiche(c, 'Bimbo_Lounge')).state;
ok(!choiceFrame(s).subs.includes('Bimbo_Lounge'), 'removed');
eq(editNiches(s, 'nope', (f, c) => c).state, s, 'an unknown flavour edits nothing');

/* ---- the round trip, through two model hosts ---------------------------- */
// The DESKTOP rule (PieceByPieceHostService.Media.cs + PbpMediaRules.HasSavedChoice):
// any media-flavour frame is the player's choice; a stored flavour or online:false counts too.
function desktop() {
  const set = { flavour: '', custom: {}, subs: [], online: true, chosen: false };
  return {
    state: () => ({ type: 'pbp:media-state', flavour: set.flavour, last: set.flavour, custom: set.custom, online: set.online,
      appWide: false, chosen: set.chosen || set.flavour !== '' || !set.online }),
    frame(m) { Object.assign(set, { flavour: m.flavour, custom: m.custom, subs: m.subs, online: m.online, chosen: true }); },
    set,
  };
}
// The SITE rule (cclabs-web pbp-web-ext/web-shim.js): localStorage pbp.web.media.v1, library:false.
function site(storage = new Map()) {
  const KEY = 'pbp.web.media.v1';
  const read = () => { try { return JSON.parse(storage.get(KEY) || 'null') || {}; } catch { return {}; } };
  return {
    state: () => { const j = read(); return { type: 'pbp:media-state', flavour: j.chosen ? (j.flavour || '') : '', last: j.last || '', custom: j.custom || {},
      online: j.online !== false, appWide: false, chosen: j.chosen === true, library: false }; },
    frame(m) { storage.set(KEY, JSON.stringify({ flavour: m.flavour, last: m.flavour || read().last || '', custom: m.custom, subs: m.subs, online: m.online, chosen: true })); },
    storage,
  };
}
for (const [name, make] of [['desktop', desktop], ['site', site]]) {
  const host = make();
  let st = readChoice(host.state());
  ok(needsChoice(st), `${name}: first start asks`);
  host.frame(choiceFrame(choose(st, 'frills')));                     // the pick
  st = readChoice(host.state());                                     // the next launch
  ok(!needsChoice(st), `${name}: second start does not ask`);
  eq(st.flavour, 'frills', `${name}: the pick comes back active`);
  const edited = editNiches(st, 'frills', (f, c) => addNiche(f, c, 'lacefetish')).state;
  host.frame(choiceFrame(edited));                                   // the manager closes
  st = readChoice(host.state());
  ok(choiceFrame(st).subs.includes('lacefetish'), `${name}: a manager edit persists`);
  host.frame(choiceFrame(choose(st, 'own')));
  st = readChoice(host.state());
  ok(!needsChoice(st) && st.online === false, `${name}: "no online pictures" persists and is not asked again`);
  host.frame(choiceFrame(choose(st, 'shiny')));
  st = readChoice(host.state());
  ok(st.flavour === 'shiny' && (st.custom.frills?.added || []).includes('lacefetish'), `${name}: switching keeps the other flavour's edits`);
}
ok(ownLine(readChoice(site().state())).includes('without'), 'the site says own means no pictures');

/* ---- the chess page's own wiring, through a fake webview --------------- */
{
  const heard = [];
  const posted = [];
  globalThis.window = { chrome: { webview: {
    addEventListener: (t, fn) => { if (t === 'message') heard.push(fn); },
    postMessage: (m) => posted.push(m),
  } } };
  const { pictureChoice, picturesFrame } = await import('../ui/pictures.js');
  const send = (data) => heard.forEach((fn) => fn({ data }));
  ok(!pictureChoice.available() && !pictureChoice.needsChoice(), 'before the host speaks: nothing to ask');
  eq(await pictureChoice.ask(), true, 'nothing owed: a start goes on');
  send({ type: 'pbp:media-state', flavour: '', last: '', custom: {}, online: true, appWide: false, chosen: false });
  ok(pictureChoice.available() && pictureChoice.needsChoice(), 'the host says nothing is saved: a start asks');
  send({ type: 'pbp:media-state', flavour: 'pink', last: 'pink', custom: {}, online: true, appWide: false, chosen: true });
  ok(!pictureChoice.needsChoice(), 'saved: no ask');
  eq(await pictureChoice.ask(), true, 'saved: the start goes straight on');
  eq(picturesFrame(choose(fresh, 'trance')).type, 'pbp:media-flavour', 'typed for the chess host');
  send({ type: 'pbp:online-media', state: 'ready', have: 14, want: 36 });
  eq(pictureChoice.debug.status(), '14 online pictures in the mix.', 'the status line follows the host');
  delete globalThis.window;
}

/* ---- copy --------------------------------------------------------------- */
{
  const lines = [statusLine(fresh), statusLine(own), statusLine(pink, { state: 'ready', have: 9 }), statusLine(pink, { state: 'error' }),
    statusLine(phone), ownLine(fresh), ownLine({ library: false })];
  ok(lines.every(Boolean), 'every status has words');
  const dashes = new RegExp('[!' + String.fromCharCode(0x2014, 0x2013) + ']');
  ok(!dashes.test(lines.join('')), 'no exclamation marks or dashes');
}

console.log(`niche-choice-smoke: ${checks} checks passed`);
