/* ============================================================================
 * stations/breakout/particles.js - one pooled particle list for the whole
 * station. Kinds: 'dot' (the classic spark), 'rect' (a tumbling brick shard in
 * the brick's colour), 'shard' (the grey ball splinter), 'crop' (a torn chunk
 * of a dealt picture, used by the whirlwind rim).
 *
 * createParticles({ max, rng }) -> { burst, implode, spray, rects, shatter,
 *   sparkle, crop, after, step, clear, count, list }
 * Nothing here knows about the sim; render.js feeds it positions and colours
 * and calls step(g, dt, mix, col) once a frame. Colour goes through `col` so a
 * grey frame never shows a coloured dot (render.js also clears on relapse).
 * ==========================================================================*/

const TAU = Math.PI * 2;

export function createParticles({ max = 600, rng = Math.random } = {}) {
  const list = [];
  const pending = [];

  /** Oldest out first so a long combo never grows the list past the cap. */
  function add(p) {
    list.push(p);
    if (list.length > max) list.splice(0, list.length - max);
  }

  /** A ring of dots flying out. `rise` lifts them (the old burst pulled -40 on vy). */
  function burst(x, y, rgb, n, speed, life, { rise = 40, gv = 300, r0 = 2, r1 = 3 } = {}) {
    for (let i = 0; i < n; i++) {
      const a = rng() * TAU, s = speed * (0.4 + rng());
      add({ kind: 'dot', x, y, vx: Math.cos(a) * s, vy: Math.sin(a) * s - rise, life, max: life, rgb, gv, r: r0 + rng() * r1 });
    }
  }
  /** The mirror of burst: dots start out on a ring and fall INWARD toward (x, y). */
  function implode(x, y, rgb, n, speed, life, { from = 90 } = {}) {
    for (let i = 0; i < n; i++) {
      const a = rng() * TAU, d = from * (0.6 + rng() * 0.6), s = speed * (0.6 + rng() * 0.8);
      add({ kind: 'dot', x: x + Math.cos(a) * d, y: y + Math.sin(a) * d, vx: -Math.cos(a) * s, vy: -Math.sin(a) * s,
        life, max: life, rgb, gv: 0, r: 1.5 + rng() * 2.5 });
    }
  }
  /** A cone of sparks along `ang` (radians), `spread` wide. */
  function spray(x, y, ang, spread, rgb, n, speed, life, { gv = 380 } = {}) {
    for (let i = 0; i < n; i++) {
      const a = ang + (rng() - 0.5) * spread, s = speed * (0.45 + rng());
      add({ kind: 'dot', x, y, vx: Math.cos(a) * s, vy: Math.sin(a) * s, life: life * (0.6 + rng() * 0.6), max: life, rgb, gv, r: 1.2 + rng() * 2 });
    }
  }
  /** Rectangular shards in the brick's colour, tumbling under gravity. */
  function rects(x, y, rgb, n, { speed = 180, life = 0.7, size = 5 } = {}) {
    for (let i = 0; i < n; i++) {
      const a = rng() * TAU, s = speed * (0.4 + rng());
      add({ kind: 'rect', x, y, vx: Math.cos(a) * s, vy: Math.sin(a) * s - 70, life, max: life, rgb, gv: 520,
        rot: rng() * TAU, vr: (rng() - 0.5) * 16, w: size * (0.5 + rng()), h: size * (0.3 + rng() * 0.5) });
    }
  }
  /** The grey ball splinters (breakout and the shattered wall). */
  function shatter(x, y, n) {
    for (let i = 0; i < n; i++) {
      const a = rng() * TAU, s = 120 + rng() * 260;
      add({ kind: 'shard', x, y, vx: Math.cos(a) * s, vy: Math.sin(a) * s, life: 1.1, max: 1.1, gv: 260,
        rot: rng() * 6, vr: (rng() - 0.5) * 12, size: 3 + rng() * 6 });
    }
  }
  /** One tiny drifting mote, for the ball's sparkle trail. */
  function sparkle(x, y, rgb, life = 0.5) {
    add({ kind: 'dot', x, y, vx: (rng() - 0.5) * 26, vy: (rng() - 0.5) * 26 - 10, life, max: life, rgb, gv: 30, r: 0.8 + rng() * 1.2 });
  }
  /**
   * A torn chunk of a dealt picture: `src` is the decoded canvas, (sx, sy, sw, sh) the crop.
   * `orbit` (cx, cy, a, r, w, dr) keeps it spinning around the whirlwind instead of flying straight.
   */
  function crop(src, sx, sy, sw, sh, o) {
    add({ kind: 'crop', src, sx, sy, sw, sh, size: o.size, life: o.life, max: o.life,
      cx: o.cx, cy: o.cy, a: o.a, r: o.r, w: o.w, dr: o.dr, rot: rng() * TAU, vr: (rng() - 0.5) * 8, x: 0, y: 0, gv: 0 });
  }
  /** Fire `fn` after `delay` seconds, on the next step past it (the two-beat bursts). */
  function after(delay, fn) { pending.push({ t: delay, fn }); }

  function step(g, dt, mix, col) {
    for (let i = pending.length - 1; i >= 0; i--) {
      const p = pending[i]; p.t -= dt;
      if (p.t <= 0) { pending.splice(i, 1); try { p.fn(); } catch (e) { /* cosmetic */ } }
    }
    for (let i = list.length - 1; i >= 0; i--) {
      const p = list[i];
      p.life -= dt;
      if (p.life <= 0) { list.splice(i, 1); continue; }
      const t = p.life / p.max;
      if (p.kind === 'crop') {
        p.a += p.w * dt; p.r = Math.max(2, p.r + p.dr * dt); p.rot += p.vr * dt;
        p.x = p.cx + Math.cos(p.a) * p.r; p.y = p.cy + Math.sin(p.a) * p.r;
        g.save(); g.globalAlpha = Math.min(1, t * 1.6); g.translate(p.x, p.y); g.rotate(p.rot);
        const k = p.size * (0.5 + t * 0.5);
        try { g.drawImage(p.src, p.sx, p.sy, p.sw, p.sh, -k / 2, -k / 2, k, k); } catch (e) { p.life = 0; }
        g.restore();
        continue;
      }
      p.x += p.vx * dt; p.y += p.vy * dt; p.vy += p.gv * dt;
      if (p.kind === 'dot') {
        g.fillStyle = col(p.rgb, mix, t); g.beginPath(); g.arc(p.x, p.y, p.r * t, 0, 7); g.fill();
      } else if (p.kind === 'rect') {
        p.rot += p.vr * dt;
        g.save(); g.translate(p.x, p.y); g.rotate(p.rot); g.globalAlpha = Math.min(1, t * 1.8);
        g.fillStyle = col(p.rgb, mix, 1); g.fillRect(-p.w / 2, -p.h / 2, p.w, p.h);
        g.fillStyle = 'rgba(255,255,255,.2)'; g.fillRect(-p.w / 2, -p.h / 2, p.w, 1);
        g.restore();
      } else {
        p.rot += p.vr * dt;
        g.save(); g.translate(p.x, p.y); g.rotate(p.rot); g.globalAlpha = t;
        g.fillStyle = '#b8b8b8';
        g.beginPath(); g.moveTo(-p.size, 0); g.lineTo(0, -p.size * 0.6); g.lineTo(p.size, 0); g.lineTo(0, p.size * 0.5); g.fill();
        g.restore();
      }
    }
  }

  return { burst, implode, spray, rects, shatter, sparkle, crop, after, step,
    clear() { list.length = 0; pending.length = 0; }, count: () => list.length, list };
}
