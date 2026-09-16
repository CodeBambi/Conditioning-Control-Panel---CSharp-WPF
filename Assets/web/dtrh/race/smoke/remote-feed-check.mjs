/* ============================================================================
 * race/smoke/remote-feed-check.mjs - node self-check for the online feed's two
 * ends inside the race: the manifest split in dtrh/hostMedia.js, and the menu
 * group's read of `init.settings` in race/menu.js.
 *
 *   node race/smoke/remote-feed-check.mjs   (exits 0 on pass, 1 with a count)
 *
 * The two things it is here to hold:
 *
 *   1. A MANIFEST THAT LANDS AFTER BOOT IS LIVE IMMEDIATELY. run.js builds
 *      payloadFx and the media lane ONCE, both holding this same media object,
 *      and raceBoot's `manifest` handler calls setManifest on it whenever a
 *      frame arrives. So a feed switched on with the menu open has to be in the
 *      pool for the next run with nothing else re-created. That is a property of
 *      a mutable pool, and a property nobody wrote down is a property somebody
 *      refactors away.
 *   2. A REMOTE ENTRY NEVER REACHES THE WebGL LAYER. draw(), drawKind(),
 *      favorite() and urlByName() are the four doors the three.js side uses and
 *      a CORS-tainted CDN url behind any of them is a SecurityError on upload.
 *      Only drawDom() may see one (hostMedia.js's header has the full reason).
 *
 * hostMedia.js touches `window.__sfMedia` on construction, so the file stubs a
 * window and nothing else. menu.js is not imported (it wants three and a DOM);
 * its group-visibility rule is re-stated here as the predicate it compiles to,
 * which is what a desktop regression would break.
 * ==========================================================================*/

globalThis.window = globalThis.window || {};

const { createHostMediaSource } = await import('../../hostMedia.js');

let fails = 0;
const ok = (cond, what) => { if (!cond) { console.error('FAIL ' + what); fails++; } else console.log('  ok  ' + what); };

/** What the browser host shim posts once the feed is on: absolute CDN urls with
 *  the `online<share>:` share marker on the name (dtrh/hostMedia.js SHARE_RE). */
const FEED = {
  images: [
    { name: 'online30:aaa111.jpg', url: 'https://cdn.scrolller.com/aaa111.jpg' },
    { name: 'online30:bbb222.webp', url: 'https://cdn.scrolller.com/bbb222.webp' },
  ],
  videos: [{ name: 'online30:ccc333.mp4', url: 'https://cdn.scrolller.com/ccc333.mp4' }],
  skipped: 0,
};
/** The player's own library, as the desktop host posts it. */
const LOCAL = {
  images: [{ name: 'mine1.png', url: 'https://ccp.assets/images/mine1.png' }],
  videos: [{ name: 'mine2.mp4', url: 'https://ccp.assets/videos/mine2.mp4' }],
  skipped: 1,
};

/* ---- 1. an empty boot manifest, the shape raceBoot posts when unhosted ---- */
const media = createHostMediaSource();
media.setManifest({ images: [], videos: [], skipped: 0 });
ok(media.hasUserMedia() === false, 'an empty manifest is an empty local pool');
ok(media.hasDomMedia() === false, 'and an empty DOM pool');
ok(media.draw() === null, 'draw() on an empty pool is null, not a throw');
ok(media.drawDom() === null, 'drawDom() on an empty pool is null, not a throw');

/* ---- 2. the feed lands LATER, on the same object -------------------------- */
media.setManifest(FEED);
ok(media.hasDomMedia() === true, 'a manifest posted after boot fills the DOM pool with no rebuild');
ok(media.hasUserMedia() === false, 'and leaves the local pool empty: a remote entry is not the player\'s own media');
ok(media.stats().images === 0 && media.stats().videos === 0, 'stats() counts the LOCAL pool only, so the tube still reads as empty');

/* ---- 3. the WebGL doors never see it -------------------------------------- */
{
  let leaked = 0;
  for (let i = 0; i < 200; i++) {
    if (media.draw()) leaked++;
    if (media.drawKind('image')) leaked++;
    if (media.drawKind('video')) leaked++;
  }
  ok(leaked === 0, '200 rounds of draw()/drawKind() hand out NOTHING from the remote pool');
  media.setFavorites(['aaa111.jpg', 'ccc333.mp4']);
  ok(media.favorite() === null, 'favorite() cannot name a remote entry');
  ok(media.urlByName('aaa111.jpg') === null, 'urlByName() cannot resolve a remote entry');
}

/* ---- 4. drawDom() is the one door, and the marker comes off the name ------ */
{
  const seen = new Set();
  let kinds = { image: 0, video: 0 };
  for (let i = 0; i < 300; i++) {
    const p = media.drawDom();
    if (!p) continue;
    seen.add(p.name);
    kinds[p.kind] = (kinds[p.kind] | 0) + 1;
  }
  ok(kinds.image > 0 && kinds.video > 0, 'drawDom() serves both halves of the feed');
  ok([...seen].every((n) => !/^online\d/.test(n)), 'the share marker is STRIPPED off the name it is parsed from');
  ok(seen.has('aaa111.jpg') && seen.has('ccc333.mp4'), 'every feed entry is reachable through drawDom()');
  const one = media.drawDom('image');
  ok(one && one.kind === 'image' && one.remote === true, 'a remote draw is flagged `remote` so the caller can stay on the DOM road');
}

/* ---- 5. the mix: local media coming back does not evict the feed ---------- */
{
  const mixed = createHostMediaSource();
  mixed.setManifest({ images: [...LOCAL.images, ...FEED.images], videos: [...LOCAL.videos, ...FEED.videos], skipped: 1 });
  ok(mixed.hasUserMedia() === true && mixed.hasDomMedia() === true, 'both pools fill off one frame');
  ok(mixed.stats().images === 1 && mixed.stats().videos === 1 && mixed.stats().skipped === 1, 'stats() counts only the local half plus the host\'s skip count');
  let remote = 0, local = 0;
  for (let i = 0; i < 600; i++) { const p = mixed.drawDom(); if (!p) continue; if (p.remote) remote++; else local++; }
  ok(remote > 0 && local > 0, 'drawDom() mixes the two pools rather than picking one');
  // the marker said 30, so roughly a third of the DOM draws should be remote.
  const share = remote / (remote + local);
  ok(share > 0.15 && share < 0.5, `the online30: marker steers the mix (${(share * 100).toFixed(0)}% remote over 600 draws)`);
  let leaked = 0;
  for (let i = 0; i < 300; i++) { const p = mixed.draw(); if (p && !/^mine/.test(p.name)) leaked++; }
  ok(leaked === 0, 'and draw() still only ever hands out the player\'s own files');
}

/* ---- 6. the feed switched OFF: an empty manifest empties the pool --------- */
{
  media.setManifest({ images: [], videos: [], skipped: 0 });
  ok(media.hasDomMedia() === false, 'consent revoked, the host posts an empty manifest, the pool is empty at once');
  ok(media.drawDom() === null, 'and nothing is left behind to draw');
}

/* ---- 7. the menu group's visibility rule --------------------------------- */
/* race/menu.js renders the online feed group only when the host claims the
 * capability AND ships a catalog. The C# host ships neither, so the desktop
 * must fall out of this on the first clause; the browser host shim ships both.
 * Restated here because menu.js needs three and a DOM to import. */
{
  const shows = (s) => s.mediaControls === true
    && Array.isArray(s.remoteCatalog)
    && s.remoteCatalog.filter((r) => r && typeof r.id === 'string' && r.id).length > 0;
  const CATALOG = [{ id: 'hypno', label: 'Hypno' }, { id: 'bimbo', label: 'Bimbo' }];
  ok(shows({}) === false, 'the desktop (no mediaControls, no catalog) never renders the group');
  ok(shows({ remoteCatalog: CATALOG }) === false, 'a catalog without the capability flag is not enough');
  ok(shows({ mediaControls: true }) === false, 'the flag without a catalog is not enough');
  ok(shows({ mediaControls: true, remoteCatalog: [] }) === false, 'an empty catalog is not enough');
  ok(shows({ mediaControls: true, remoteCatalog: [{ label: 'no id' }] }) === false, 'a catalog of rows with no id is not enough');
  ok(shows({ mediaControls: true, remoteCatalog: CATALOG }) === true, 'the browser host shim (flag + catalog) renders it');
  ok(shows({ mediaControls: 'true', remoteCatalog: CATALOG }) === false, 'the flag is checked STRICTLY, so a stringy true is still no');
}

console.log(fails ? `\n${fails} failure(s)` : '\nremote-feed-check: all good');
process.exit(fails ? 1 : 0);
