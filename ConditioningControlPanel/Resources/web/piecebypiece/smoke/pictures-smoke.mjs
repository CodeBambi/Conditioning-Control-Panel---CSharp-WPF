/* ============================================================================
 * smoke/pictures-smoke.mjs - node checks for Distraction's online pictures
 * (2026-09-28): the local + online mix, the refill ask, clips on the right
 * surfaces, the picker's frames and status, and Distraction as the default.
 *
 *   node smoke/pictures-smoke.mjs        (no server, no browser)
 *
 * Exits non-zero on the first failure, with the check that broke.
 * ==========================================================================*/

import assert from 'node:assert/strict';
import { createHostMedia, onlineChance, HOST_MSG, ONLINE_WARM, ONLINE_LOW_SHARE } from '../ramp/media.js';
import { isClip, stillOr, clipRoom, MAX_LIVE_CLIPS } from '../ramp/layers/clip.js';
import { picturesFrame, statusLine } from '../ui/pictures.js';
import { presentation } from '../game/preferences.js';

let checks = 0;
const ok = (cond, name) => { assert.ok(cond, name); checks++; };
const eq = (a, b, name) => { assert.deepEqual(a, b, name); checks++; };

/* ---- onlineChance ------------------------------------------------------- */
eq(onlineChance(0.7, 0, true), 0, 'no online pictures: never online');
eq(onlineChance(0.7, 3, false), 1, 'no local pictures: always online');
eq(onlineChance(0.7, ONLINE_WARM, true), 0.7, 'a warm set takes the full share');
ok(Math.abs(onlineChance(0.7, 1, true) - 0.7 / ONLINE_WARM) < 1e-9, 'a cold set of one takes a sliver');
eq(onlineChance(2, ONLINE_WARM, true), 1, 'share is clamped');
eq(onlineChance(NaN, ONLINE_WARM, true), 0, 'a junk share is no share');

/* ---- a host pool with a fake bridge ------------------------------------ */
function rig(rnd = Math.random) {
  const sent = [];
  const bridge = { postMessage: (m) => sent.push(m), addEventListener() {}, removeEventListener() {} };
  const host = createHostMedia([], { bridge, requestGapMs: 0, rnd });
  return { host, sent };
}
const stills = (n, tag = 's') => Array.from({ length: n }, (_, i) => `https://ccp.assets/.temp/${tag}${i}.jpg`);
const clips = (n, tag = 'c') => Array.from({ length: n }, (_, i) => `https://ccp.assets/.temp/${tag}${i}.webm`);
const local = (n) => Array.from({ length: n }, (_, i) => `https://ccp.assets/lib/l${i}.png`);

{
  // online only: every draw is online, an image slot never gets a clip, a video slot does
  const { host } = rig();
  host.handleMessage({ type: HOST_MSG.online, state: 'ready', share: 70, images: stills(4), clips: clips(4) });
  for (let i = 0; i < 40; i++) ok(!isClip(host.draw('image')), 'an image slot never gets a clip');
  ok(isClip(host.draw('video')), 'the video card takes a clip when there is no local video');
  ok(isClip(host.draw('gif')), 'a gif surface gets the clip, not a poster');
  ok(host.size === 8, 'size counts the online set');
}

{
  // local + a warm online set at 70%: roughly 70% online, both sides seen
  const { host } = rig();
  host.handleMessage({ type: 'pbp:media', images: local(30), gifs: [], videos: [] });
  host.handleMessage({ type: HOST_MSG.online, state: 'ready', share: 70, images: stills(30), clips: [] });
  let on = 0;
  const N = 2000;
  for (let i = 0; i < N; i++) if (host.draw('image').includes('/.temp/')) on++;
  ok(on / N > 0.6 && on / N < 0.8, `a warm mix sits near the share (${(on / N).toFixed(2)})`);
}

{
  // a cold first wave of one picture does not wear every tile
  const { host } = rig();
  host.handleMessage({ type: 'pbp:media', images: local(20), gifs: [], videos: [] });
  host.handleMessage({ type: HOST_MSG.online, state: 'loading', share: 70, images: stills(1), clips: [] });
  let on = 0;
  for (let i = 0; i < 1000; i++) if (host.draw('image').includes('/.temp/')) on++;
  ok(on < 250, `one cold online picture stays a minority (${on}/1000)`);
}

{
  // failure degrades: an online set that goes empty leaves the local deck exactly as before
  const { host } = rig();
  host.handleMessage({ type: 'pbp:media', images: local(5), gifs: [], videos: [] });
  host.handleMessage({ type: HOST_MSG.online, state: 'ready', share: 100, images: stills(6), clips: [] });
  host.handleMessage({ type: HOST_MSG.online, state: 'error', share: 100, images: [], clips: [] });
  for (let i = 0; i < 20; i++) ok(host.draw('image').includes('/lib/'), 'no online set: local only');
}

{
  // the refill ask: once ONLINE_LOW_SHARE of the set was drawn, once per list, re-armed by growth
  const { host, sent } = rig(() => 0);
  host.handleMessage({ type: HOST_MSG.online, state: 'ready', share: 70, images: stills(10), clips: [] });
  const more = () => sent.filter((m) => m.type === HOST_MSG.more).length;
  for (let i = 0; i < Math.ceil(10 * ONLINE_LOW_SHARE) - 1; i++) host.draw('image');
  eq(more(), 0, 'no ask before the low share');
  host.draw('image');
  eq(more(), 1, 'one ask at the low share');
  for (let i = 0; i < 10; i++) host.draw('image');
  eq(more(), 1, 'still one ask for the same list');
  host.handleMessage({ type: HOST_MSG.online, state: 'ready', share: 70, images: [...stills(10), ...stills(10, 'n')], clips: [] });
  for (let i = 0; i < 20; i++) host.draw('image');
  eq(more(), 2, 'a grown list re-arms the ask');
}

{
  // a retired picture leaves the page with the frame that dropped it
  const { host } = rig();
  host.handleMessage({ type: HOST_MSG.online, state: 'ready', share: 100, images: stills(3), clips: [] });
  host.handleMessage({ type: HOST_MSG.online, state: 'ready', share: 100, images: stills(3, 'x'), clips: [] });
  for (let i = 0; i < 30; i++) ok(host.draw('image').includes('/x'), 'only the current set is drawn');
}

{
  // no echo across the two sides: the same picture twice in a row takes the other side
  const { host } = rig();
  host.handleMessage({ type: 'pbp:media', images: local(1), gifs: [], videos: [] });
  host.handleMessage({ type: HOST_MSG.online, state: 'ready', share: 50, images: stills(ONLINE_WARM), clips: [] });
  let prev = null, repeats = 0;
  for (let i = 0; i < 400; i++) { const u = host.draw('image'); if (u === prev) repeats++; prev = u; }
  eq(repeats, 0, 'never the same picture twice in a row');
}

/* ---- clip.js ------------------------------------------------------------ */
ok(isClip('https://ccp.assets/.temp/a.webm') && isClip('x.MP4?v=1') && !isClip('a.gif') && !isClip(null), 'clip urls');
eq(stillOr('a.gif', () => 'still'), 'a.gif', 'a still passes through');
ok(MAX_LIVE_CLIPS >= 1 && clipRoom(), 'there is room for a clip at rest');
eq(stillOr('a.webm', () => 'still'), 'a.webm', 'a clip passes while there is room');

/* ---- the picker --------------------------------------------------------- */
{
  const f = picturesFrame({ flavour: 'pink', custom: { pink: { off: ['Bimbos'], on: [], added: ['mine_1'] } }, online: true });
  eq(f.type, 'pbp:media-flavour', 'the frame is typed for the chess host');
  eq(f.flavour, 'pink', 'flavour rides');
  ok(!f.subs.includes('Bimbos') && f.subs.includes('bimbofication') && f.subs.includes('mine_1'), 'subs = the niches switched on');
  const own = picturesFrame({ flavour: '', custom: {}, online: false });
  eq([own.flavour, own.online, own.subs.length], ['', false, 0], 'own pictures: no flavour, online off');
  eq(picturesFrame({ flavour: 'nope', custom: {} }).flavour, '', 'an unknown flavour is none');
}
ok(statusLine({ online: false }, null).includes('own'), 'status: own only');
ok(statusLine({ online: true, flavour: '' }, null).startsWith('Pick'), 'status: nothing picked');
ok(statusLine({ online: true, flavour: 'pink' }, { state: 'ready', have: 12 }).startsWith('12'), 'status: ready counts');
ok(statusLine({ online: true, flavour: 'pink' }, { state: 'error' }).includes('carry on'), 'status: error falls back');
ok(statusLine({ online: true, flavour: '', appWide: true }, { state: 'loading', have: 0 }).startsWith('Finding'), 'status: app-wide source');
ok(!/[!—]/.test([statusLine({ online: false }), statusLine({ online: true }), statusLine({ online: true, flavour: 'x' }, { state: 'empty' })].join('')), 'no exclamation marks or em-dashes');

/* ---- Distraction is the default ---------------------------------------- */
eq(presentation().experience, 'distraction', 'nobody saved: Distraction');
{
  // a fresh module instance per saved state (the query string defeats the module cache)
  const store = (v) => { globalThis.localStorage = { getItem: () => v, setItem() {} }; };
  store(JSON.stringify({ experience: 'classic' }));
  eq((await import('../game/preferences.js?classic')).presentation().experience, 'classic', 'a saved Classic stays Classic');
  store(JSON.stringify({ volume: 0.3 }));
  eq((await import('../game/preferences.js?volume')).presentation().experience, 'distraction', 'a saved volume alone is Distraction');
  delete globalThis.localStorage;
}

console.log(`pictures-smoke: ${checks} checks passed`);
