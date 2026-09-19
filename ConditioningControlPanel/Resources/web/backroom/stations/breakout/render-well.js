/* ============================================================================
 * stations/breakout/render-well.js - the gravity well, drawn as a whirlwind
 * made of one of the Loom's fields (shared/hypno/loom.js: every spiral in the
 * room is the Loom's, never a picture and never a fork).
 *
 * Each well arrives with its own preset, spin factor and hue (game.js picks
 * them at spawn), so the wells vary on the fly. The field is drawn into an
 * offscreen disc: a dark backing, the Loom field turned by the well's rotation
 * and hue-shifted, a rotating radial gradient that punches the eye out of the
 * centre and feathers the rim, and a noise + blob tear mask turning the other
 * way so the edge stays ragged. Torn chunks of the disc break off the rim as
 * particles (particles.js 'crop').
 *
 * createWellFx({ reduced, rng, noiseTile }) -> { draw(g, well, opts), tile(preset, rot, hue, mix, size, frameNo), reset(), dispose() }
 * tile() is the same field in a small disc for a spiral BRICK (and its pop): one
 * offscreen per preset + hue + size, recomposed every third frame, staggered.
 * opts: { mix, col, dt, particles, pink, violet, mint, spiral }
 * The sim gives us { x, y, r, pull, rot, age, ttl, fade, captured, preset, spin, hue }.
 * ==========================================================================*/

import { createLoomKit, LOOM_PRESETS } from '../../shared/hypno/loom.js';

const TAU = Math.PI * 2;
const IN_S = 0.4, FALLBACK_PRESET = 'whirl';
const clamp = (v, a, b) => Math.max(a, Math.min(b, v));

/** Bites out of the rim: blobs plus noise, kept to the outer annulus. Built once per radius. */
function makeTear(size, r, rng, noiseTile) {
  const c = document.createElement('canvas'); c.width = c.height = size;
  const x = c.getContext('2d'), cc = size / 2;
  x.fillStyle = '#fff';
  for (let i = 0; i < 34; i++) {
    const a = rng() * TAU, rr = r * (0.78 + rng() * 0.3), s = 3 + rng() * 9;
    x.globalAlpha = 0.5 + rng() * 0.5;
    x.beginPath(); x.arc(cc + Math.cos(a) * rr, cc + Math.sin(a) * rr, s, 0, 7); x.fill();
  }
  if (noiseTile) {
    const pat = x.createPattern(noiseTile, 'repeat');
    if (pat) { x.globalAlpha = 0.32; x.fillStyle = pat; x.fillRect(0, 0, size, size); }
  }
  x.globalAlpha = 1;
  x.globalCompositeOperation = 'destination-in';
  const grd = x.createRadialGradient(cc, cc, r * 0.7, cc, cc, r * 1.04);
  grd.addColorStop(0, 'rgba(0,0,0,0)'); grd.addColorStop(0.55, 'rgba(0,0,0,.65)'); grd.addColorStop(1, 'rgba(0,0,0,1)');
  x.fillStyle = grd; x.fillRect(0, 0, size, size);
  return c;
}

export function createWellFx({ reduced = false, rng = Math.random, noiseTile = null } = {}) {
  let disc = null, dg = null, tear = null, size = 0, half = 0, builtR = 0;
  let frames = 0, cap = 0, chunkAt = 0, kit = null, kitDead = false;

  /** The page's Loom context, made on the first well and freed on dispose. */
  function loom() {
    if (kit || kitDead) return kit;
    try { kit = createLoomKit({ still: reduced }); } catch (e) { kitDead = true; kit = null; }
    return kit;
  }

  function ensure(r) {
    const want = Math.ceil(r * 2) + 8;
    if (disc && want === size && builtR === r) return;
    size = want; half = size / 2; builtR = r;
    disc = document.createElement('canvas'); disc.width = disc.height = size;
    dg = disc.getContext('2d');
    tear = makeTear(size, r, rng, noiseTile);
  }

  /** Re-draw the field into the offscreen disc. Called every other frame (once in reduced motion). Returns false without the Loom. */
  function compose(well, r, rot, mix, tight) {
    const k = loom();
    if (!k) return false;
    const name = LOOM_PRESETS[well.preset] ? well.preset : FALLBACK_PRESET;
    dg.setTransform(1, 0, 0, 1, 0, 0);
    dg.clearRect(0, 0, size, size);
    dg.save();
    dg.translate(half, half);
    dg.beginPath(); dg.arc(0, 0, r, 0, 7); dg.clip();
    // A backing disc first, so the field's own background never shows a square corner through the tear.
    const back = dg.createRadialGradient(0, 0, 0, 0, 0, r);
    back.addColorStop(0, 'rgba(74,30,92,.96)'); back.addColorStop(1, 'rgba(30,14,44,.96)');
    dg.fillStyle = back; dg.fillRect(-half, -half, size, size);
    // The field, a little larger than the disc so its arms run off the rim, turned by the well's own rotation.
    const d = r * (2.3 + tight * 0.3);
    let drawn = false;
    try {
      if (well.hue && 'filter' in dg) dg.filter = `hue-rotate(${well.hue | 0}deg)`;
      drawn = k.draw(dg, name, -d / 2, -d / 2, d, d, { angle: rot, alpha: 1, backing: 'small' });
    } catch (e) { drawn = false; }
    try { dg.filter = 'none'; } catch (e) { /* no filter support */ }
    if (!drawn) { dg.restore(); return false; }
    // Grey-safe, while the disc is still a solid circle: blending on torn alpha would paint the holes grey.
    if (mix < 0.999) {
      dg.globalCompositeOperation = 'saturation'; dg.globalAlpha = 1 - mix;
      dg.fillStyle = '#808080'; dg.fillRect(-half, -half, size, size);
      dg.globalAlpha = 1; dg.globalCompositeOperation = 'source-over';
    }
    dg.restore();
    dg.save();
    dg.translate(half, half);
    // The eye: a dark hole in the middle, its centre wobbling round with the rotation.
    dg.globalCompositeOperation = 'destination-out';
    const ex = reduced ? 0 : Math.cos(rot) * r * 0.07, ey = reduced ? 0 : Math.sin(rot) * r * 0.07;
    const eye = dg.createRadialGradient(ex, ey, 0, ex, ey, r * (0.3 - tight * 0.1));
    eye.addColorStop(0, 'rgba(0,0,0,1)'); eye.addColorStop(0.55, 'rgba(0,0,0,.85)'); eye.addColorStop(1, 'rgba(0,0,0,0)');
    dg.fillStyle = eye; dg.fillRect(-half, -half, size, size);
    // The rim: a wide feather that reaches full transparency at the edge, so the field fades straight into the screen.
    const rim = dg.createRadialGradient(0, 0, r * 0.62, 0, 0, r);
    rim.addColorStop(0, 'rgba(0,0,0,0)'); rim.addColorStop(0.55, 'rgba(0,0,0,.5)'); rim.addColorStop(1, 'rgba(0,0,0,1)');
    dg.fillStyle = rim; dg.fillRect(-half, -half, size, size);
    if (!reduced && tear) { dg.rotate(-rot * 0.5); dg.drawImage(tear, -half, -half); }
    dg.restore();
    return true;
  }

  /** One torn chunk of the disc, orbiting off the rim (or sucked back in). */
  function chunk(P, well, dir) {
    const sw = Math.max(6, size * (0.08 + rng() * 0.1)), sh = Math.max(6, size * (0.08 + rng() * 0.1));
    const a = rng() * TAU;
    P.crop(disc, rng() * (size - sw), rng() * (size - sh), sw, sh, {
      size: 7 + rng() * 9, life: 0.75 + rng() * 0.5, cx: well.x, cy: well.y, a,
      r: dir < 0 ? well.r * (1.4 + rng() * 0.5) : well.r * (0.86 + rng() * 0.18),
      w: (rng() < 0.5 ? -1 : 1) * (1.6 + rng() * 1.6) * (dir < 0 ? 1.8 : 1),
      dr: dir < 0 ? -(70 + rng() * 60) : 22 + rng() * 34,
    });
  }

  function draw(g, well, o) {
    const { mix, col, dt = 1 / 60, particles: P, pink, violet, mint, spiral } = o;
    const r = well.r || 70;
    // On `born`, the clock that never pauses: `age` stops while the well holds a ball, and a ball caught at the burst
    // used to leave the field near invisible until the release snapped it to full (owner, 2026-09-19).
    const fadeIn = clamp((typeof well.born === 'number' ? well.born : well.age || 0) / IN_S, 0, 1);
    const a = clamp(well.fade, 0, 1) * fadeIn;
    if (a <= 0 || mix <= 0) return;
    cap += ((well.captured ? 1 : 0) - cap) * Math.min(1, dt * 6);

    g.save();
    // The pull radius reads as a soft dent in the field.
    const pull = g.createRadialGradient(well.x, well.y, r * 0.5, well.x, well.y, well.pull || 110);
    pull.addColorStop(0, col(violet, mix, 0.2 * a)); pull.addColorStop(1, col(violet, mix, 0));
    g.fillStyle = pull; g.beginPath(); g.arc(well.x, well.y, well.pull || 110, 0, 7); g.fill();
    // No halo, no rim, no glow (owner, 2026-09-19: "remove the pink circle, the edges faded directly on the screen"):
    // the disc's own feather (compose) is the only edge.
    ensure(r);
    frames++;
    let ok = true;
    if (frames % (reduced ? 30 : 2) === 1) ok = compose(well, r, well.rot || 0, mix, cap);
    if (ok && !kitDead) {
      const pop = 0.82 + 0.18 * fadeIn + cap * 0.04;
      g.save();
      g.globalAlpha = a;
      g.translate(well.x, well.y); g.scale(pop, pop);
      g.drawImage(disc, -half, -half);
      g.restore();
    } else {
      // No Loom (no canvas at all): the old procedural look, so the well is never invisible.
      spiral(g, well.x, well.y, r, well.rot || 0, col(pink, mix), a);
    }
    // The faint procedural arms still ride on top, so the wind has visible lines.
    spiral(g, well.x, well.y, r * 0.7, (well.rot || 0) * 1.3, col(mint, mix), a * 0.22, 3);
    spiral(g, well.x, well.y, r * 0.45, (well.rot || 0) * 1.7, col(pink, mix), a * 0.26, 2);
    g.restore();

    if (reduced || !P || !ok) return;
    // Rim chunks: converging while the field dissolves in, drifting out once it is settled, sucked in on a capture.
    chunkAt -= dt;
    if (chunkAt <= 0) {
      chunkAt = 0.075 + rng() * 0.05;
      const dir = (fadeIn < 1 || well.captured || well.fade < 1) ? -1 : 1;
      chunk(P, well, dir);
      if (well.captured && rng() < 0.6) chunk(P, well, -1);
    }
  }

  /* ---- brick tiles: the field in a small disc, for the spiral bricks and their pops ---- */
  const tiles = new Map();
  const TILE_CAP = 12;
  /** A canvas of `size` px holding preset `preset` turned to `rot` (hue-shifted, grey-safe by `mix`), or null without the Loom.
   *  Recomposed every third frame (once in reduced motion), each tile on its own frame so they never all render together. */
  function tile(preset, rot, hue, mix, size, frameNo) {
    const k = loom();
    if (!k) return null;
    const name = LOOM_PRESETS[preset] ? preset : FALLBACK_PRESET;
    const key = name + '|' + (hue | 0) + '|' + size;
    let t = tiles.get(key);
    if (!t) {
      if (tiles.size >= TILE_CAP) tiles.delete(tiles.keys().next().value);
      const c = document.createElement('canvas'); c.width = c.height = size;
      t = { c, x: c.getContext('2d'), ok: false, ever: false, ph: tiles.size % 3 };
      tiles.set(key, t);
    }
    const due = !t.ever || (!reduced && ((frameNo | 0) + t.ph) % 3 === 0);
    if (!due) return t.ok ? t.c : null;
    t.ever = true;
    const x = t.x, h = size / 2, r = h - 1;
    x.setTransform(1, 0, 0, 1, 0, 0); x.clearRect(0, 0, size, size);
    x.save(); x.translate(h, h);
    x.beginPath(); x.arc(0, 0, r, 0, 7); x.clip();
    x.fillStyle = 'rgba(30,14,44,.96)'; x.fillRect(-h, -h, size, size);
    const d = r * 2.4;
    let drawn = false;
    try {
      if (hue && 'filter' in x) x.filter = `hue-rotate(${hue | 0}deg)`;
      drawn = k.draw(x, name, -d / 2, -d / 2, d, d, { angle: rot, alpha: 1, backing: 'small' });
    } catch (e) { drawn = false; }
    try { x.filter = 'none'; } catch (e) { /* no filter support */ }
    if (drawn && mix < 0.999) {
      x.globalCompositeOperation = 'saturation'; x.globalAlpha = 1 - mix;
      x.fillStyle = '#808080'; x.fillRect(-h, -h, size, size);
      x.globalAlpha = 1; x.globalCompositeOperation = 'source-over';
    }
    x.globalCompositeOperation = 'destination-out';
    const eye = x.createRadialGradient(0, 0, 0, 0, 0, r * 0.22);
    eye.addColorStop(0, 'rgba(0,0,0,.9)'); eye.addColorStop(1, 'rgba(0,0,0,0)');
    x.fillStyle = eye; x.fillRect(-h, -h, size, size);
    const rim = x.createRadialGradient(0, 0, r * 0.8, 0, 0, r);
    rim.addColorStop(0, 'rgba(0,0,0,0)'); rim.addColorStop(1, 'rgba(0,0,0,.85)');
    x.fillStyle = rim; x.fillRect(-h, -h, size, size);
    x.restore();
    t.ok = drawn;
    return drawn ? t.c : null;
  }

  return {
    draw,
    tile,
    reset() { cap = 0; chunkAt = 0; frames = 0; },
    /** Free the Loom context (the station's close). A later draw makes a new one. */
    dispose() { tiles.clear(); if (kit) { try { kit.dispose(); } catch (e) { /* noop */ } kit = null; } kitDead = false; },
  };
}
