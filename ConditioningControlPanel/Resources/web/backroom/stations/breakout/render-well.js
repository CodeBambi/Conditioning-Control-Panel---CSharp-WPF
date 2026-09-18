/* ============================================================================
 * stations/breakout/render-well.js - the gravity well, drawn as a whirlwind
 * made out of a dealt picture instead of two procedural spirals.
 *
 * The picture is cut into wedges, each wedge wound a little further round than
 * the last, so the frame reads as being spun into a spiral. A rotating radial
 * gradient punches the eye out of the centre and feathers the rim; a noise +
 * blob tear mask, turning the other way, keeps the edge ragged. Torn chunks of
 * the same picture break off the rim as particles (particles.js 'crop').
 *
 * createWellFx({ reduced, rng, noiseTile }) -> { draw(g, well, opts), reset() }
 * opts: { mix, col, media, dt, sat, particles, pink, violet, mint, spiral }
 * The sim only gives us { x, y, r, pull, rot, age, ttl, fade, captured, gif }.
 * ==========================================================================*/

const TAU = Math.PI * 2;
const SLICES = 6, TWIST = 0.55, IN_S = 0.4;
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
  let frames = 0, cap = 0, chunkAt = 0;

  function ensure(r) {
    const want = Math.ceil(r * 2) + 8;
    if (disc && want === size && builtR === r) return;
    size = want; half = size / 2; builtR = r;
    disc = document.createElement('canvas'); disc.width = disc.height = size;
    dg = disc.getContext('2d');
    tear = makeTear(size, r, rng, noiseTile);
  }

  /** Re-cut the picture into the offscreen disc. Called every other frame (once in reduced motion). */
  function compose(frame, r, rot, mix, tight) {
    const fw = frame.width || frame.naturalWidth || 1, fh = frame.height || frame.naturalHeight || 1;
    dg.setTransform(1, 0, 0, 1, 0, 0);
    dg.clearRect(0, 0, size, size);
    dg.save();
    dg.translate(half, half);
    dg.beginPath(); dg.arc(0, 0, r, 0, 7); dg.clip();
    // A backing disc first: a dealt picture with transparency still reads as a solid body of wind.
    const back = dg.createRadialGradient(0, 0, 0, 0, 0, r);
    back.addColorStop(0, 'rgba(74,30,92,.96)'); back.addColorStop(1, 'rgba(30,14,44,.96)');
    dg.fillStyle = back; dg.fillRect(-half, -half, size, size);
    const k = Math.max(2 * r / fw, 2 * r / fh) * 1.25;
    const dw = fw * k, dh = fh * k;
    const n = reduced ? 1 : SLICES, wedge = TAU / n, twist = TWIST * (1 + tight * 0.9);
    for (let i = 0; i < n; i++) {
      const a0 = i * wedge + rot * 0.35;
      dg.save();
      if (n > 1) {
        dg.beginPath(); dg.moveTo(0, 0); dg.arc(0, 0, r * 1.3, a0 - 0.06, a0 + wedge + 0.06); dg.closePath(); dg.clip();
        dg.rotate(rot + i * twist);
        const sc = 1 - i * 0.075;
        dg.scale(sc, sc);
      }
      dg.drawImage(frame, -dw / 2, -dh / 2, dw, dh);
      dg.restore();
    }
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
    const eye = dg.createRadialGradient(ex, ey, 0, ex, ey, r * (0.34 - tight * 0.12));
    eye.addColorStop(0, 'rgba(0,0,0,1)'); eye.addColorStop(0.55, 'rgba(0,0,0,.85)'); eye.addColorStop(1, 'rgba(0,0,0,0)');
    dg.fillStyle = eye; dg.fillRect(-half, -half, size, size);
    // The rim: feathered, never a hard circle.
    const rim = dg.createRadialGradient(0, 0, r * 0.82, 0, 0, r);
    rim.addColorStop(0, 'rgba(0,0,0,0)'); rim.addColorStop(1, 'rgba(0,0,0,.9)');
    dg.fillStyle = rim; dg.fillRect(-half, -half, size, size);
    if (!reduced && tear) { dg.rotate(-rot * 0.5); dg.drawImage(tear, -half, -half); }
    dg.restore();
  }

  /** One torn chunk of the picture, orbiting off the rim (or sucked back in). */
  function chunk(P, frame, well, dir) {
    const fw = frame.width || frame.naturalWidth || 1, fh = frame.height || frame.naturalHeight || 1;
    const sw = Math.max(6, fw * (0.1 + rng() * 0.12)), sh = Math.max(6, fh * (0.1 + rng() * 0.12));
    const a = rng() * TAU;
    P.crop(frame, rng() * (fw - sw), rng() * (fh - sh), sw, sh, {
      size: 7 + rng() * 9, life: 0.75 + rng() * 0.5, cx: well.x, cy: well.y, a,
      r: dir < 0 ? well.r * (1.4 + rng() * 0.5) : well.r * (0.86 + rng() * 0.18),
      w: (rng() < 0.5 ? -1 : 1) * (1.6 + rng() * 1.6) * (dir < 0 ? 1.8 : 1),
      dr: dir < 0 ? -(70 + rng() * 60) : 22 + rng() * 34,
    });
  }

  function draw(g, well, o) {
    const { mix, col, media, dt = 1 / 60, particles: P, pink, violet, mint, spiral } = o;
    const r = well.r || 70;
    const fadeIn = clamp((well.age || 0) / IN_S, 0, 1);
    const a = clamp(well.fade, 0, 1) * fadeIn;
    if (a <= 0 || mix <= 0) return;
    cap += ((well.captured ? 1 : 0) - cap) * Math.min(1, dt * 6);
    const frame = media && typeof media.frame === 'function' ? media.frame(well.gif || 0) : null;
    if (media && typeof media.mark === 'function') media.mark(well.gif || 0, well.x, well.y);

    g.save();
    // The pull radius reads as a soft dent in the field.
    const pull = g.createRadialGradient(well.x, well.y, r * 0.5, well.x, well.y, well.pull || 110);
    pull.addColorStop(0, col(violet, mix, 0.2 * a)); pull.addColorStop(1, col(violet, mix, 0));
    g.fillStyle = pull; g.beginPath(); g.arc(well.x, well.y, well.pull || 110, 0, 7); g.fill();

    if (frame) {
      ensure(r);
      frames++;
      if (frames % (reduced ? 30 : 2) === 1) compose(frame, r, well.rot || 0, mix, cap);
      const pop = 0.82 + 0.18 * fadeIn + cap * 0.04;
      g.save();
      g.globalAlpha = a;
      g.translate(well.x, well.y); g.scale(pop, pop);
      g.shadowColor = col(pink, mix, 0.7); g.shadowBlur = 16 + cap * 16;
      g.drawImage(disc, -half, -half);
      g.restore();
    } else {
      // No picture dealt yet: the old procedural look, so the well is never invisible.
      spiral(g, well.x, well.y, r, well.rot || 0, col(pink, mix), a);
    }
    // The faint procedural arms still ride on top, so the wind has visible lines.
    spiral(g, well.x, well.y, r * 0.88, -(well.rot || 0) * 1.3, col(mint, mix), a * 0.13, 3);
    spiral(g, well.x, well.y, r * 0.5, (well.rot || 0) * 1.7, col(pink, mix), a * 0.16, 2);
    g.restore();

    if (reduced || !P || !frame) return;
    // Rim chunks: converging while the picture dissolves in, drifting out once it is settled, sucked in on a capture.
    chunkAt -= dt;
    if (chunkAt <= 0) {
      chunkAt = 0.075 + rng() * 0.05;
      const dir = (fadeIn < 1 || well.captured || well.fade < 1) ? -1 : 1;
      chunk(P, frame, well, dir);
      if (well.captured && rng() < 0.6) chunk(P, frame, well, -1);
    }
  }

  return { draw, reset() { cap = 0; chunkAt = 0; frames = 0; } };
}
