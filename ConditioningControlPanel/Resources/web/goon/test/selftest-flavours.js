// The flavour card, the online set, and patron-only sending (2026-09-23).
//
//   node Resources/web/goon/test/selftest-flavours.js
//
// 1. ui/flavours.js is a COPY of the Breakout table (the goon tree cannot import across
//    web trees). This reads both and fails the moment they drift.
// 2. The pure helpers, the wire frames and exec/media.js's third set.
// 3. Source pins for the send gate: a free seat never advertises `transfer`, never seeds
//    the consent declaration, and the send switch is opt-in.

import fs from 'node:fs';
import url from 'node:url';
import * as goon from '../ui/flavours.js';
import * as breakout from '../../backroom/stations/breakout/flavours.js';
import { createGoonMediaPool } from '../exec/media.js';
import { S } from '../ui/strings.js';
import { liveLine } from '../ui/screens/flavour.js';
import { PREF_DEFAULTS } from '../ui/prefs.js';

let failures = 0;
let n = 0;
function ok(cond, label, extra = '') {
  n++;
  if (!cond) { failures++; console.error(`  FAIL ${label} ${extra}`); }
}
const read = (p) => fs.readFileSync(url.fileURLToPath(new URL(p, import.meta.url)), 'utf8').replace(/\r\n/g, '\n');
const same = (a, b) => JSON.stringify(a) === JSON.stringify(b);

// ================================================== 1. the table has not drifted
{
  ok(same(goon.FLAVOURS, breakout.FLAVOURS), 'FLAVOURS matches breakout/flavours.js exactly',
    '\n    goon:     ' + JSON.stringify(goon.FLAVOURS) + '\n    breakout: ' + JSON.stringify(breakout.FLAVOURS));
  ok(same(goon.MINE, breakout.MINE), 'MINE matches too');
  ok(goon.MAX_NICHES === breakout.MAX_NICHES, 'and the eight-niche cap');
  // And the text itself, not only the values: a table rebuilt some other way still reads as a copy.
  const grab = (src) => { const m = /export const FLAVOURS = \[[\s\S]*?\n\];/.exec(src); return m ? m[0] : ''; };
  const gt = grab(read('../ui/flavours.js')), bt = grab(read('../../backroom/stations/breakout/flavours.js'));
  ok(gt && gt === bt, 'the FLAVOURS source block is byte-identical in both files');
  ok(same(goon.FLAVOUR_IDS, ['trance', 'pink', 'frills', 'shiny', 'censored', 'mine']), 'the wire ids are the contract\'s');
}

// ================================================== 2. the helpers behave like Breakout's
{
  for (const t of ['r/Bimbos', 'https://www.reddit.com/r/Bimbos/', 'scrolller.com/r/x', 'bad name', 'a', 'x'.repeat(41), '  GoonCaves  ', '/r/Sissyperfection?x=1', '', null]) {
    ok(goon.cleanNiche(t) === breakout.cleanNiche(t), 'cleanNiche agrees on ' + JSON.stringify(t));
  }
  const pink = goon.flavourById('pink');
  ok(same(goon.liveSubs(pink, {}), ['bimbofication', 'Bimbos', 'BimboOrNot']), 'a fresh flavour sends its core subs');
  let c = goon.toggleNiche(pink, {}, 'Bimbos');
  ok(same(goon.liveSubs(pink, c), ['bimbofication', 'BimboOrNot']), 'a core niche switched off leaves');
  c = goon.toggleNiche(pink, c, 'bimbo');
  ok(goon.liveSubs(pink, c).includes('bimbo'), 'an extra switched on joins');
  let r = goon.addNiche(pink, c, 'r/GoonCaves');
  ok(r.error === '' && goon.liveSubs(pink, r.custom).includes('GoonCaves'), 'an added niche joins');
  ok(goon.addNiche(pink, r.custom, 'GoonCaves').error === 'dup', 'adding it again is "dup"');
  ok(goon.addNiche(pink, r.custom, 'no spaces').error === 'bad', 'a non-name is "bad"');
  ok(!!S.flavour.errors.bad && !!S.flavour.errors.dup && !!S.flavour.errors.full, 'every error code has words');
  const back = goon.removeNiche(r.custom, 'GoonCaves');
  ok(!goon.liveSubs(pink, back).includes('GoonCaves'), 'removeNiche takes it out');
  // The breakout helpers give the same lists for the same moves (their errors are words, ours are codes).
  const bc = breakout.addNiche(pink, breakout.toggleNiche(pink, breakout.toggleNiche(pink, {}, 'Bimbos'), 'bimbo'), 'r/GoonCaves');
  ok(same(goon.liveSubs(pink, r.custom), breakout.liveSubs(pink, bc.custom)), 'the same moves give the same live list as Breakout');
  let full = {};
  for (let i = 0; i < 12; i++) full = goon.addNiche(goon.MINE, full, 'n' + i + 'xx').custom;
  ok(goon.liveSubs(goon.MINE, full).length === 8, 'never more than eight live subs');
}

// ================================================== 3. the wire frames
{
  ok(goon.readMediaInit(undefined) === null, 'no init.media -> null (no card, no frame)');
  const m = goon.readMediaInit({ flavour: 'nope', custom: { pink: { on: ['bimbo', 'bad name'], off: 5 }, evil: { on: ['x'] } } });
  ok(m.flavour === '' && m.online === true, 'an unknown flavour reads as first run, online defaults on');
  ok(same(m.custom, { pink: { on: ['bimbo'], off: [], added: [] } }), 'custom is sanitised: bad names and unknown ids dropped', JSON.stringify(m.custom));
  ok(goon.readMediaInit({ flavour: 'shiny', online: false }).online === false, 'online:false survives');
  const f = goon.mediaFlavourFrame({ flavour: 'pink', custom: { pink: { off: ['Bimbos'] } }, online: true });
  ok(f.type === 'media-flavour' && f.flavour === 'pink' && same(f.subs, ['bimbofication', 'BimboOrNot']) && f.online === true,
    'the media-flavour frame carries the computed subs', JSON.stringify(f));
  ok(goon.sameMediaState({ flavour: 'pink', custom: {}, online: true }, { flavour: 'pink', custom: {}, online: true }), 'an unchanged state is the same');
  ok(!goon.sameMediaState({ flavour: 'pink', custom: {}, online: true }, { flavour: 'pink', custom: {}, online: false }), 'online flipped is a change');
  const o = goon.readOnlineFrame({ state: 'ready', images: [{ name: 'a', url: 'https://ccp.assets/.temp/a.jpg' }, { name: 'b' }], videos: null, progress: { have: 1, want: 36 } });
  ok(o.state === 'ready' && o.images.length === 1 && o.videos.length === 0 && o.want === 36, 'readOnlineFrame keeps only entries with a url');
  ok(goon.readOnlineFrame({ state: 'weird' }).state === 'loading', 'an unknown state reads as loading');
}

// ================================================== 4. the deck's third set
{
  const pool = createGoonMediaPool();
  pool.setManifest({ images: [{ name: 'h1', url: 'https://ccp.assets/h1.jpg' }], videos: [] });
  pool.setLocalLibrary([{ kind: 'image', name: 'l1', url: 'blob:l1' }]);
  let c = pool.setOnlineLibrary({ images: [{ name: 'o1', url: 'https://ccp.assets/.temp/o1.jpg' }], videos: [{ name: 'o2', url: 'https://ccp.assets/.temp/o2.mp4' }] });
  ok(c.images === 3 && c.videos === 1, 'the deck is host + local + online', JSON.stringify(c));
  ok(pool.onlineCount() === 2, 'onlineCount says two');
  c = pool.setManifest({ images: [], videos: [] });
  ok(c.images === 2 && c.videos === 1, 'a new manifest does not wipe the online set');
  c = pool.setLocalLibrary([]);
  ok(c.images === 1 && c.videos === 1 && pool.hasMedia(), 'nor does a new local library');
  c = pool.setOnlineLibrary({ images: [], videos: [] });
  ok(c.images === 0 && c.videos === 0 && !pool.hasMedia(), 'an empty online frame takes them back out');
  pool.setOnlineLibrary({ images: [{ name: 'o1', url: 'https://ccp.assets/.temp/o1.jpg' }] });
  const d = pool.draw();
  ok(d && d.url.indexOf('.temp/o1.jpg') > 0, 'an online-only deck draws online pictures');
}

// ================================================== 5. the copy
{
  const all = JSON.stringify(S.flavour) + [S.flavour.live.ready(3), S.flavour.live.loadingN(1), S.flavour.removeNiche('x')].join(' ')
    + S.mediaSetup.note + S.mediaSetup.notePatron + S.mediaSetup.backToFlavours;
  ok(!new RegExp('[' + String.fromCharCode(0x2013, 0x2014) + ']').test(all), 'no em or en dashes in the new copy');
  ok(!/!/.test(all), 'no exclamation marks in the new copy');
  ok(/scrolller/i.test(S.flavour.sub) && /reddit/i.test(S.flavour.sub), 'the card says where pictures come from (the opt-in)');
  ok(liveLine(null, false) === S.flavour.live.idle, 'the live line before a pick invites one');
  ok(liveLine({ state: 'ready', images: [1, 2], videos: [3] }, true) === S.flavour.live.ready(3), 'and counts what is ready');
  ok(liveLine({ state: 'loading', images: [], videos: [] }, true) === S.flavour.live.loading, 'loading with nothing yet');
  ok(S.flavour.live.ready(1) !== S.flavour.live.ready(2), 'singular at one');
  ok(!/scrolller|reddit/i.test(S.mediaSetup.note), 'the free-seat file note never mentions sending');
  ok(!/send/i.test(S.mediaSetup.note), 'and never mentions sending at all');
}

// ================================================== 6. sending: patrons only, opt-in
{
  const boot = read('../boot.js');
  ok(PREF_DEFAULTS.mediaTransferEnabled === false, 'the send pref exists and defaults OFF');
  ok(/const transferCap = !!\(session\.caps && session\.caps\.mediaTransfer === true\);/.test(boot)
    && /transfer: transferCap \}\)/.test(boot), 'the hello advertises transfer only for a seat that may send');
  ok(!/transfer: true \}\)/.test(boot), 'and never a flat transfer: true');
  ok(/prefs\.get\('mediaTransferEnabled'\) === true\s*\n\s*&& !!\(session\.caps && session\.caps\.mediaTransfer === true\)/.test(boot),
    'the consent seed needs BOTH the opt-in and the perk');
  ok(!/mediaTransferEnabled'\) !== false/.test(boot), 'the old default-on read is gone');
  const src = /function createArtifactSource\(\) \{[\s\S]*?\n\}\n/.exec(boot);
  ok(src && !/\bmedia\./.test(src[0]) && !/online/i.test(src[0]), 'what can be SENT is listed off the assets store, never the deck or the online set');
  ok(/visible: \(\) => !!\(session\.caps && session\.caps\.mediaTransfer === true\)/.test(boot), 'the options send switch shows only to a seat that may send');
  const opts = read('../ui/options.js');
  ok(/if \(sending && typeof sending\.visible === 'function' && sending\.visible\(\)\)/.test(opts), 'and options asks before drawing it');
  ok(/bridge\.on\('online-media'/.test(boot) && /session\.media = readMediaInit\(m\.media\)/.test(boot), 'boot reads init.media and the online-media frame');
  // The card only replaces the file step where a host fetches pictures; standalone keeps the picker.
  ok(/mediaPrepPending = mediaFlavour\.available\(\) \? mediaFlavour\.firstRun\(\) : needsMediaSetup\(media\);/.test(boot), 'the join hold asks for a flavour when there is a host to fetch it');
  const card = read('../ui/screens/flavour.js');
  ok(/e\.key !== 'Escape'\) e\.stopPropagation/.test(card), 'the niche field keeps its keys to itself');
}

if (failures) {
  console.error(`\n  selftest-flavours: ${n - failures}/${n} checks passed ${failures} FAILURE(S)`);
  process.exit(1);
}
console.log(`  selftest-flavours: ${n}/${n} checks passed`);
