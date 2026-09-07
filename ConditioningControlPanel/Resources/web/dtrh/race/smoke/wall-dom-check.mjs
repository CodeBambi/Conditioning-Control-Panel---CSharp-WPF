/* ============================================================================
 * race/smoke/wall-dom-check.mjs - node self-check for race/wallDom.js, the online
 * feed hung on the race's tube wall as DOM.
 *
 *   node race/smoke/wall-dom-check.mjs      (exits 0 on pass, 1 with a count)
 *
 * Sibling of remote-feed-check.mjs, which holds the pool end of the same road; this
 * one holds the wall end. Nothing here touches the network: the module's loader is a
 * `new Image()` whose src is never fetched, because Image is stubbed and the test
 * fires onload by hand. That is deliberate. The whole reason this module exists is
 * that the feed's CDN answers a cross-origin request with nothing at all, and a
 * smoke that needed the CDN would be the first thing to rot.
 *
 * What it holds:
 *   1. an empty remote pool builds NOTHING - no element, no loader, no cost.
 *   2. the set is drawn ONCE up front, and not one poster is placed until a picture
 *      has finished loading. "Draw them beforehand and place them" is the owner's
 *      instruction and it is the difference between a wall that fills in and a wall
 *      that stalls on the CDN mid-corner.
 *   3. the occlusion discipline: behind the lens, past the far window, and turned
 *      away at a bend are each hidden, hidden means opacity 0, and the fade means a
 *      poster arrives instead of popping.
 *   4. THE TRANSFORM. The matrix3d strings are parsed back out of the styles and run
 *      through the CSS chain by hand (translateZ * camera * object, then the
 *      perspective divide), and compared with what three.js's own projection puts on
 *      screen for the same point. This is the one thing most likely to be subtly
 *      wrong, so it is checked against a known position rather than eyeballed.
 *   5. the painted fill placards stand down while pictures are up (walls.js's rule,
 *      restated here as the predicate it compiles to: walls.js wants a renderer).
 *   6. dispose leaves no element behind.
 *
 * three resolves to the fork's vendored ESM through the same hook gltf-smoke.mjs uses.
 * ==========================================================================*/

import { registerHooks } from 'node:module';
import { pathToFileURL } from 'node:url';
import path from 'node:path';

const VENDOR = pathToFileURL(path.resolve(import.meta.dirname, '../../vendor/three/') + path.sep).href;
registerHooks({
  resolve(spec, ctx, next) {
    if (spec === 'three') return { url: VENDOR + 'three.module.min.js', shortCircuit: true };
    if (spec.startsWith('three/addons/')) return { url: VENDOR + 'addons/' + spec.slice('three/addons/'.length), shortCircuit: true };
    return next(spec, ctx);
  },
});

/* ---- the stub DOM: styles, a tree, and an Image that never leaves the process ---- */
const VW = 1280, VH = 720;
const loaders = [];

class Elem {
  constructor(tag) {
    this.tagName = String(tag).toUpperCase();
    this.children = []; this.parentNode = null; this.style = {}; this.attrs = {};
    this.alt = ''; this.className = ''; this.decoding = '';
  }
  get src() { return this.attrs.src || ''; }
  set src(v) { this.attrs.src = v; }
  getAttribute(k) { return this.attrs[k] === undefined ? null : this.attrs[k]; }
  setAttribute(k, v) { this.attrs[k] = v; }
  appendChild(n) { n.parentNode = this; this.children.push(n); return n; }
  removeChild(n) { const i = this.children.indexOf(n); if (i >= 0) this.children.splice(i, 1); n.parentNode = null; return n; }
}
class StubImage extends Elem {
  constructor() { super('img'); this.naturalWidth = 0; this.naturalHeight = 0; this.onload = null; this.onerror = null; }
  /** what the browser would do a network round trip later. */
  finish(w = 800, h = 600) { this.naturalWidth = w; this.naturalHeight = h; if (this.onload) this.onload(); }
}
globalThis.document = { createElement: (t) => (t === 'img' ? new StubImage() : new Elem(t)) };
/** `new Image()` is the module's LOADER; document.createElement('img') is a slot element
 *  on the wall. Only the loaders are tracked, so the test can tell the two apart. */
globalThis.Image = class LoaderImage extends StubImage { constructor() { super(); loaders.push(this); } };
globalThis.window = { innerWidth: VW, innerHeight: VH, devicePixelRatio: 1 };

const THREE = await import('three');
const { createHostMediaSource } = await import('../../hostMedia.js');
const { createWallDomPosters } = await import('../wallDom.js');
const { BAND_LO, BAND_HI } = await import('../walls.js');
const { RADIUS } = await import('../consts.js');

let fails = 0;
const ok = (cond, what) => { if (!cond) { console.error('FAIL ' + what); fails++; } else console.log('  ok  ' + what); };
/** the module's loaders sit behind two promise hops; a macrotask flushes them all. */
const settle = () => new Promise((r) => setTimeout(r, 0));

/** The feed, as the browser host shim posts it (remote-feed-check.mjs's FEED). */
const feedMedia = (n) => {
  const m = createHostMediaSource();
  m.setManifest({
    images: Array.from({ length: n }, (_, i) => ({ name: `online100:pic${i}.jpg`, url: `https://images.scrolller.com/pic${i}.jpg` })),
    videos: [], skipped: 0,
  });
  return m;
};

/** A dead straight tube down -Z: the ceiling is +y, so every number below can be
 *  worked out on paper instead of trusted. */
const straightLayout = (room = 'casino', totalDepth = 4000) => ({
  totalDepth,
  wrap: (d) => ((d % totalDepth) + totalDepth) % totalDepth,
  roomAtDepth: () => room,
  frameAtDepth: (d) => ({
    pos: new THREE.Vector3(0, 0, -d),
    tangent: new THREE.Vector3(0, 0, -1),
    normal: new THREE.Vector3(0, 1, 0),
    binormal: new THREE.Vector3(1, 0, 0),
  }),
});
const rngOf = (v) => () => v;   // a fixed rng: angle, roll and size are then all knowable
const newRoot = () => new Elem('div');
function makeCamera() {
  const cam = new THREE.PerspectiveCamera(72, VW / VH, 0.1, 400);
  cam.position.set(0, 0, 0); cam.lookAt(0, 0, -10); cam.updateMatrixWorld();
  return cam;
}
const shots = (root) => root.children[0].children[0].children;
const up = (root) => shots(root).filter((el) => el.style.visibility === 'visible' && Number(el.style.opacity || 0) > 0);
const allDark = (root) => shots(root).every((el) => Number(el.style.opacity || 0) === 0);

/* ---- 1. an empty remote pool builds nothing ------------------------------- */
{
  const root = newRoot();
  const media = createHostMediaSource();
  media.setManifest({ images: [{ name: 'mine.png', url: 'https://ccp.assets/images/mine.png' }], videos: [], skipped: 0 });
  loaders.length = 0;
  const layer = createWallDomPosters({ root, layout: straightLayout(), camera: makeCamera(), media, rng: Math.random });
  await settle();
  ok(root.children.length === 0, 'a local-only manifest builds NO layer: nothing is appended to the root');
  ok(loaders.length === 0, 'and starts no loader: an empty feed costs nothing at all');
  ok(layer.ready() === false, 'the layer reports nothing ready');
  layer.update(0); layer.setRoom('casino'); layer.setHidden(true); layer.dispose();
  ok(root.children.length === 0, 'and every method on the inert layer is a safe no-op');
  const old = createWallDomPosters({ root, layout: straightLayout(), camera: makeCamera(), media: { hasUserMedia: () => false }, rng: Math.random });
  ok(old.ready() === false && root.children.length === 0, 'a media object with no drawRemoteDom (an older host) builds nothing either');
}

/* ---- 2. drawn beforehand, then placed ------------------------------------ */
{
  loaders.length = 0;
  const root = newRoot();
  const media = feedMedia(14);
  let draws = 0;
  const counted = { ...media, drawRemoteDom: (k) => { draws++; return media.drawRemoteDom(k); } };
  const layer = createWallDomPosters({ root, layout: straightLayout(), camera: makeCamera(), media: counted, rng: rngOf(0.5) });
  ok(root.children.length === 1, 'a feed manifest builds exactly one layer element');
  ok(draws === 10, `the set is drawn ONCE up front: ${draws} draws at creation`);
  await settle();
  ok(loaders.length === 10, 'and all ten start loading immediately, before a single frame');

  layer.update(0);
  ok(layer.ready() === false, 'with nothing loaded yet the layer is not ready');
  ok(up(root).length === 0, 'and NOT ONE poster is placed while the pictures are still loading');

  const before = draws;
  loaders.forEach((img, i) => img.finish(800, i % 2 ? 600 : 1000));
  ok(layer.ready() === true, 'once the pictures are in, the layer reports ready');
  layer.update(0);
  ok(up(root).length > 0, `and the wall fills on the very next frame (${up(root).length} posters up)`);
  // the odometer starts where the camera stands: a layer built mid-lap must not hang its
  // band back at depth zero, a whole lap from anyone who could see it
  {
    loaders.length = 0;
    const mid = newRoot();
    const cam = makeCamera();
    const at = 288.47;
    cam.position.set(0, 0, -at); cam.lookAt(0, 0, -at - 10); cam.updateMatrixWorld();
    const midLayer = createWallDomPosters({ root: mid, layout: straightLayout(), camera: cam, media: feedMedia(14), rng: rngOf(0.5) });
    await settle();
    loaders.forEach((img) => img.finish(800, 600));
    midLayer.update(at);
    ok(up(mid).length > 0, 'a layer whose first frame is 288 m down the road hangs its band THERE, not back at zero');
    midLayer.dispose();
  }
  ok(draws === before, 'placing a poster draws NOTHING new from the pool: the set is fixed');
  ok(up(root).every((el) => /^https:\/\/images\.scrolller\.com\//.test(el.attrs.src)), 'every poster points at a feed url that finished loading');

  // a slot that falls behind re-points at the SAME set, it never loads anything new
  const loadersBefore = loaders.length;
  for (let d = 0; d < 400; d += 8) layer.update(d);
  ok(loaders.length === loadersBefore, 'lapping 400 m of road loads nothing new: a recycled slot re-points inside the set');
  ok(draws === before, 'and draws nothing new from the pool either');
  layer.dispose();
}

/* ---- 3. the occlusion discipline ----------------------------------------- */
{
  loaders.length = 0;
  const root = newRoot();
  const cam = makeCamera();
  const layer = createWallDomPosters({ root, layout: straightLayout(), camera: cam, media: feedMedia(14), rng: rngOf(0.5) });
  await settle();
  loaders.forEach((img) => img.finish(800, 600));
  layer.update(0);
  ok(up(root).length > 0, 'looking down a straight tube, the band ahead is up');

  // BEHIND: the same road, the lens 400 m past every slot (update(0) recycles nothing)
  cam.position.set(0, 0, -400); cam.lookAt(0, 0, -410); cam.updateMatrixWorld();
  layer.update(0);
  ok(allDark(root), 'every poster behind the camera is hidden, none is left painted');

  // TURNED AWAY: the same posters, seen from outside the wall they are stuck to
  cam.position.set(0, RADIUS * 2.4, -20); cam.lookAt(0, RADIUS * 2.4, -30); cam.updateMatrixWorld();
  layer.update(0);
  ok(allDark(root), 'a poster whose wall normal faces away is hidden, so nothing shows through a bend');

  // PAST THE WINDOW: the same road, the lens a long way short of the band
  cam.position.set(0, 0, 300); cam.lookAt(0, 0, 290); cam.updateMatrixWorld();
  layer.update(0);
  ok(allDark(root), 'nothing past the forward depth window is placed');

  // THE FADE
  cam.position.set(0, 0, 0); cam.lookAt(0, 0, -10); cam.updateMatrixWorld();
  layer.update(0);
  const ops = up(root).map((el) => Number(el.style.opacity));
  ok(ops.length > 0 && ops.every((o) => o > 0 && o <= 0.94), 'a placed poster is never solid and never over the layer alpha');
  ok(ops.some((o) => o < 0.9), 'and the far end of the window is faded, so a poster arrives rather than pops');
  ok(BAND_LO > 0 && BAND_HI < Math.PI / 2, 'the band shared with walls.js stays in the upper wall, off the road, by definition');

  // the whole layer stands down on demand, in the same tick
  layer.setHidden(true);
  ok(root.children[0].style.display === 'none', 'setHidden(true) takes the layer off screen at once, not on the next frame');
  ok(root.children[0].style.display === 'none', 'which is what the menu and the Brake need: neither of them calls update()');
  layer.setHidden(false); layer.update(0);
  ok(root.children[0].style.display === '', 'and setHidden(false) brings it back');
  layer.dispose();

  // the Tea Garden keeps a bare wall, exactly as it does for the painted signs
  loaders.length = 0;
  const tea = newRoot();
  const teaLayer = createWallDomPosters({ root: tea, layout: straightLayout('teagarden'), camera: makeCamera(), media: feedMedia(14), rng: rngOf(0.5) });
  await settle();
  loaders.forEach((img) => img.finish(800, 600));
  teaLayer.update(0);
  ok(up(tea).length === 0 && tea.children[0].style.display === 'none', 'the Tea Garden hangs nothing: zero density, same as the painted signs');
  teaLayer.dispose();
}

/* ---- 4. THE TRANSFORM, against a known position -------------------------- */
{
  const M = (s) => { const m = /matrix3d\(([^)]*)\)/.exec(s); return m ? m[1].split(',').map(Number) : null; };
  /** column-major 4x4 multiply and point apply: three's convention, and CSS's. */
  const mul = (a, b) => {
    const o = new Array(16).fill(0);
    for (let c = 0; c < 4; c++) for (let r = 0; r < 4; r++) { let v = 0; for (let k = 0; k < 4; k++) v += a[k * 4 + r] * b[c * 4 + k]; o[c * 4 + r] = v; }
    return o;
  };
  const apply = (m, v) => [0, 1, 2, 3].map((r) => m[r] * v[0] + m[4 + r] * v[1] + m[8 + r] * v[2] + m[12 + r] * v[3]);

  loaders.length = 0;
  const root = newRoot();
  const cam = makeCamera();
  cam.position.set(0.6, -1.2, 4); cam.lookAt(0.2, 0.4, -26); cam.updateMatrixWorld();
  const layer = createWallDomPosters({ root, layout: straightLayout(), camera: cam, media: feedMedia(14), rng: rngOf(0.5) });
  await settle();
  loaders.forEach((img) => img.finish(800, 600));
  layer.update(0);

  const persp = Number(String(root.children[0].style.perspective).replace('px', ''));
  ok(Math.abs(persp - 0.5 * VH / Math.tan(cam.fov * Math.PI / 360)) < 0.01, 'the CSS perspective is solved from the live fov and the viewport height');
  const camM = M(root.children[0].children[0].style.transform);
  const tz = [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, persp, 1];
  let checked = 0;
  for (const el of up(root)) {
    const objM = M(el.style.transform);
    // the element's centre: its translate(-50%,-50%) parks it on the matrix's translation
    const p = apply(mul(tz, mul(camM, objM)), [0, 0, 0, 1]);
    const w = 1 - p[2] / persp;                    // the CSS perspective divide
    const sx = VW / 2 + p[0] / w, sy = VH / 2 + p[1] / w;
    // and where three.js would put that same world point. objCss flips the y COLUMN,
    // which leaves the translation column alone, so this reads straight back off it.
    const ndc = new THREE.Vector3(objM[12], objM[13], objM[14]).project(cam);
    const tx = (ndc.x * 0.5 + 0.5) * VW, ty = (1 - (ndc.y * 0.5 + 0.5)) * VH;
    ok(Math.abs(sx - tx) < 0.6 && Math.abs(sy - ty) < 0.6,
      `poster ${checked}: the CSS chain lands where three.js projects it (css ${sx.toFixed(1)},${sy.toFixed(1)} vs gl ${tx.toFixed(1)},${ty.toFixed(1)})`);
    if (++checked >= 3) break;
  }
  ok(checked > 0, 'at least one poster was on screen to check the transform against');
  layer.dispose();
}

/* ---- 5. the painted fill placards stand down ----------------------------- */
/* race/walls.js wants a renderer and a scene, so its one line is restated here as the
 * predicate it compiles to. The base placards are unconditional; only the fill set
 * yields, and it yields to the player's own library OR to the DOM posters. */
{
  const fill = (media, hasDomPosters) => !(media && typeof media.hasUserMedia === 'function' && media.hasUserMedia())
    && !(typeof hasDomPosters === 'function' && hasDomPosters());
  const empty = { hasUserMedia: () => false }, own = { hasUserMedia: () => true };
  ok(fill(empty, null) === true, 'no library and no feed: the fill placards dress the bare wall, as before');
  ok(fill(empty, () => false) === true, 'a layer that exists but has nothing loaded yet does NOT stand the placards down');
  ok(fill(empty, () => true) === false, 'the moment the feed pictures are up, the fill placards stand down');
  ok(fill(own, () => false) === false, 'the player\'s own library still stands them down on its own');
  ok(fill(own, () => true) === false, 'and both at once is still down, not back up');
}

/* ---- 6. dispose ---------------------------------------------------------- */
{
  loaders.length = 0;
  const root = newRoot();
  const layer = createWallDomPosters({ root, layout: straightLayout(), camera: makeCamera(), media: feedMedia(14), rng: rngOf(0.5) });
  await settle();
  loaders.slice(0, 5).forEach((img) => img.finish(800, 600));
  layer.update(0);
  ok(root.children.length === 1 && shots(root).length === 10, 'the layer holds one element per slot');
  layer.dispose();
  ok(root.children.length === 0, 'dispose leaves no element behind on the root');
  ok(loaders.every((img) => img.onload === null || img.naturalWidth > 0), 'and lets go of every loader still in flight');
  layer.update(0); layer.setHidden(false); layer.setRoom('casino');
  ok(root.children.length === 0, 'a call after dispose is a no-op, not a resurrection');
}

console.log(fails ? `\n${fails} failure(s)` : '\nwall-dom-check: all good');
process.exit(fails ? 1 : 0);
