// Online pictures in a plain browser (2026-09-24): net/webMedia.js stands in for
// GoonHostService's media handlers when there is no C# host.
//
//   node Resources/web/goon/test/selftest-webmedia.js
//
// 1. The rules mirror GoonOnlineMediaRules (niche grammar, cap, state words, source picks).
// 2. A pick fills 24 stills + 12 clips, media-more adds a fresh wave with no repeats, the
//    deck keeps three waves and retires the oldest, a switched-off pick says 'off'.
// 3. peer-niches fills a peer-media pool, 'declined' when online pictures are off, [] stops it.
// 4. init.media: flavour always '' (the opt-in is per session). Source pins on bridge + boot.

import fs from 'node:fs';
import url from 'node:url';
import {
  cleanSubs, cleanFlavour, stateFor, pickStill, pickClip, mediaInitBlock,
  createWebMediaHost, STILL_TARGET, CLIP_TARGET, MAX_WAVES, ENDPOINT,
} from '../net/webMedia.js';

let failures = 0;
let n = 0;
function ok(cond, label, extra = '') {
  n++;
  if (!cond) { failures++; console.error(`  FAIL ${label} ${extra}`); }
}
const read = (p) => fs.readFileSync(url.fileURLToPath(new URL(p, import.meta.url)), 'utf8').replace(/\r\n/g, '\n');
const flush = async () => { for (let i = 0; i < 50; i++) await Promise.resolve(); };

// ================================================== 1. rules
{
  ok(JSON.stringify(cleanSubs(['r/Bimbos', 'bimbos', 'a', 'bad name', 'ok_2', 5, 'https://x'])) === '["Bimbos","ok_2"]', 'cleanSubs: r/ dropped, case dupes, grammar');
  ok(cleanSubs(Array.from({ length: 12 }, (_, i) => 'sub' + i)).length === 8, 'cleanSubs caps at 8');
  ok(cleanFlavour(' PINK ') === 'pink' && cleanFlavour('evil') === '', 'cleanFlavour');
  ok(stateFor(false, 3, 3, false, false) === 'off' && stateFor(true, 0, 0, false, false) === 'empty'
    && stateFor(true, 2, 5, true, false) === 'loading' && stateFor(true, 2, 5, false, false) === 'ready'
    && stateFor(true, 2, 0, false, true) === 'error', 'stateFor matches the host');
  const src = [
    { url: 'https://c/a.webp', width: 2400 }, { url: 'https://c/b.jpg', width: 1080 }, { url: 'https://c/c.webp', width: 640 },
    { url: 'https://c/v.mp4', width: 1280 }, { url: 'https://c/v2.webm', width: 480 }, { url: 'https://c/v_thumb.webm', width: 600 },
    { url: 'javascript:alert(1).jpg', width: 100 },
  ];
  ok(pickStill(src) === 'https://c/b.jpg', 'still: largest under 1280');
  ok(pickClip(src) === 'https://c/v2.webm', 'clip: largest non-thumb under 640');
  ok(pickStill([{ url: 'https://c/big.jpg', width: 4000 }]) === 'https://c/big.jpg', 'still: none fit = the smallest');
  ok(pickStill([{ url: 'http://c/a.jpg', width: 100 }]) === null, 'still: https only');
}

// A fake Scrolller: every page answers 30 fresh posts, each with one still and one clip.
function fakeScrolller({ fail = false, repeat = false } = {}) {
  let post = 0;
  const calls = [];
  const fetch = async (u, o) => {
    const body = JSON.parse(o.body);
    calls.push({ url: u, body, credentials: o.credentials });
    if (fail) throw new Error('offline');
    const items = [];
    for (let i = 0; i < 30; i++) {
      const id = repeat ? i : ++post;
      items.push({ id, mediaSources: [
        { url: `https://cdn/${body.variables.url.slice(3)}/${id}.webp`, width: 1000 },
        { url: `https://cdn/${body.variables.url.slice(3)}/${id}.mp4`, width: 480 },
      ] });
    }
    return { ok: true, status: 200, json: async () => ({ data: { getSubreddit: { id: 1, children: { iterator: 'it' + calls.length, items } } } }) };
  };
  return { fetch, calls };
}

function rig(opts = {}) {
  const frames = [];
  const saved = [];
  const sc = fakeScrolller(opts);
  const host = createWebMediaHost({
    emit: (f) => frames.push(f), fetch: sc.fetch, savePrefs: (p) => saved.push(p),
    online: opts.online, now: () => 0, sleep: async () => {},
  });
  return { host, frames, saved, sc, last: (t) => frames.filter((f) => f.type === t).pop() };
}

// ================================================== 2. the player's own pool
{
  const r = rig();
  ok(r.host.handle({ type: 'media-flavour', flavour: '', subs: ['Bimbos'], online: true }) === true, 'media-flavour is ours');
  await flush();
  ok(r.frames.length === 0 && r.sc.calls.length === 0, 'no pick = no fetch and no word');

  r.host.handle({ type: 'media-flavour', flavour: 'pink', subs: ['Bimbos', 'r/bimbofication', 'x'], online: true, custom: { pink: { off: [] } } });
  await flush();
  const f = r.last('online-media');
  ok(f && f.state === 'ready', 'a pick ends ready', f && f.state);
  ok(f.images.length === STILL_TARGET && f.videos.length === CLIP_TARGET, 'one wave = 24 stills + 12 clips', `${f.images.length}/${f.videos.length}`);
  ok(JSON.stringify(f.subs) === '["Bimbos","bimbofication"]', 'subs re-cleaned');
  ok(r.frames.some((x) => x.state === 'loading'), 'loading is said first');
  ok(r.sc.calls.every((c) => c.url === ENDPOINT && c.credentials === 'omit'), 'straight to Scrolller, no cookie');
  ok(r.sc.calls.some((c) => c.body.variables.filter === 'PICTURE') && r.sc.calls.some((c) => c.body.variables.filter === 'GIF'), 'stills and clips filters');
  ok(r.saved.at(-1).goonMediaLast === 'pink' && r.saved.every((s) => !('goonMediaFlavour' in s)), 'the edits are remembered, the opt-in is not');

  const firstUrls = new Set([...f.images, ...f.videos].map((e) => e.url));
  r.host.handle({ type: 'media-more' });
  await flush();
  const g = r.last('online-media');
  ok(g.images.length === STILL_TARGET * 2 && g.videos.length === CLIP_TARGET * 2, 'media-more adds a wave');
  const all = [...g.images, ...g.videos].map((e) => e.url);
  ok(new Set(all).size === all.length, 'no url twice in the deck');
  ok(all.filter((u) => firstUrls.has(u)).length === firstUrls.size, 'the first wave is still there');

  for (let i = 0; i < 3; i++) { r.host.handle({ type: 'media-more' }); await flush(); }
  const h = r.last('online-media');
  ok(h.images.length === STILL_TARGET * MAX_WAVES && h.videos.length === CLIP_TARGET * MAX_WAVES, 'three waves kept at most');
  ok(![...h.images, ...h.videos].some((e) => firstUrls.has(e.url)), 'the oldest wave retired');

  r.host.handle({ type: 'media-flavour', flavour: 'pink', subs: ['Bimbos'], online: false });
  await flush();
  ok(r.last('online-media').state === 'off' && r.last('online-media').images.length === 0, 'switched off = off, nothing held');
  const before = r.sc.calls.length;
  r.host.handle({ type: 'media-more' });
  await flush();
  ok(r.sc.calls.length === before, 'no refill while off');
}
{
  const r = rig({ repeat: true });
  r.host.handle({ type: 'media-flavour', flavour: 'trance', subs: ['EroticHypnosis'], online: true });
  await flush();
  // The same 30 posts on every page: the first wave takes 24, a refill only the 6 left, then nothing.
  for (let i = 0; i < 3; i++) { r.host.handle({ type: 'media-more' }); await flush(); }
  const g = r.last('online-media');
  const urls = g.images.map((e) => e.url);
  ok(g.state === 'ready' && g.images.length === 30 && new Set(urls).size === 30, 'a feed that only repeats never deals a url twice, and still ends', `${g.state} ${g.images.length}`);
}
{
  const r = rig({ fail: true });
  r.host.handle({ type: 'media-flavour', flavour: 'shiny', subs: ['ShinyPorn'], online: true });
  await flush();
  ok(r.last('online-media').state === 'error', 'unreachable feed = error');
}
{
  const r = rig();
  r.host.handle({ type: 'media-flavour', flavour: 'mine', subs: [], online: true });
  await flush();
  ok(r.last('online-media').state === 'empty' && r.sc.calls.length === 0, 'a pick with no niches is an honest empty');
}

// ================================================== 3. the opponent's niches
{
  const r = rig();
  r.host.handle({ type: 'peer-niches', subs: ['Sissyperfection', 'bad name'] });
  await flush();
  const p = r.last('peer-media');
  ok(p && p.state === 'ready' && p.images.length === STILL_TARGET, 'peer pool fills');
  ok(JSON.stringify(p.subs) === '["Sissyperfection"]', 'peer names re-cleaned');
  ok(!r.frames.some((x) => x.type === 'online-media'), 'the peer pool never touches the own deck');
  r.host.handle({ type: 'peer-niches', subs: [] });
  await flush();
  ok(r.last('peer-media').state === 'off', 'an empty list stops it');
}
{
  const r = rig({ online: false });
  r.host.handle({ type: 'peer-niches', subs: ['Bimbos'] });
  await flush();
  ok(r.last('peer-media').state === 'declined' && r.sc.calls.length === 0, 'online off = declined, nothing fetched');
  ok(r.host.handle({ type: 'exit' }) === false, 'other frames are not ours');
}

// ================================================== 4. init + source pins
{
  const b = mediaInitBlock({ goonMediaLast: 'pink', goonMediaCustom: { pink: { added: ['x1'] } }, goonMediaOnline: false, goonMediaFlavour: 'pink' });
  ok(b.flavour === '' && b.last === 'pink' && b.online === false && b.custom.pink, 'init.media: flavour always empty, edits kept');
  ok(mediaInitBlock(null).online === true, 'online defaults on');
  const bridge = read('../bridge.js');
  ok(/media: \(typeof fetch === 'function'\) \? mediaInitBlock\(prefs\) : null/.test(bridge), 'standalone init carries the media block');
  ok(/else if \(localHost\) localHost\(msg\)/.test(bridge), 'send reaches the stand-in only without a webview');
  ok(/mediaTransfer: true,/.test(bridge), 'sending stays server-decided (the lobby pref is the opt-in)');
  const boot = read('../boot.js');
  ok(/session\.hosted \|\| bridge\.hasLocalHost\(\)/.test(boot), 'peer-niches goes out in a browser too');
}

console.log(`selftest-webmedia: ${n - failures}/${n} passed`);
if (failures) process.exit(1);
