/* ============================================================================
 * bowl.js - the Velvet Vortex bowl on a 2D canvas (CONTRACT 10.13.F), drawn the
 * way hypno-spins-v3.html draws it: pocket order and colours from state.wheel
 * and state.rose, never a copy of its own.
 *
 *   Drifting rim     peripheral-drift print on two rim rings, drawn ONCE into an
 *                    offscreen cache per size and blitted
 *   Lighthouse       law 4: the beam runs on the bowl's own clock (feel.beamAngle),
 *                    against the rotor, and lights each number it passes
 *   The run          feel.planRun: run, drop, fret rattle in slow motion with up
 *                    to three sparks, settle into the server's pocket
 *   Velvet wake      Full only: a brushed-nap sheen behind the ball, 1.3 s to settle
 *   Turret whirl     a Spiral Wake spin: the dish becomes the kit's Loom `whirl`
 *                    field at 0.85 x fade x k, angle = rotor x 2.2 (pulls inward);
 *                    spiral gate off: the dish stays velvet and the rim glows gold
 *   Turret arms      bend back against the rotor (law 3: a = base - k x u)
 *
 * Every spiral is the Loom's: the whirl goes through kit.draw, nothing else here
 * draws a spiral. Still (Calm, reduced): the rotor eases to a stop at rest, the
 * beam and the whirl hold, the ball still runs its plan at the raised floor.
 * ==========================================================================*/

import { FEEL, SEG, beamAngle, beamLit, pocketAngle, whirlAngle, sampleRun, restRel, litNumbers } from './feel.js';
import { HIGHLIGHT_MS } from '../../shared/hypno/callout.js';

const TAU = Math.PI * 2;
const COL = { brass: '#e8c27a', mint: '#5fffd0', rose: '#ff5fa2', plum: '#3a1f5c', text: '#efe6ff', zero: '#1f8f74', roseFelt: '#c8286e' };
const DRIFT = ['#0b0714', '#3a1f5c', '#d8cbe9', '#cfae6e'];
const FONT = 'Segoe UI, Figtree, Arial, sans-serif';
const HINT_MS = 1200, HINT_A0 = -Math.PI * 0.95, HINT_ARC = Math.PI * 1.15;   // THE THROW's arrow: cycle, start, sweep
const clamp = (v, a, b) => Math.max(a, Math.min(b, v));
const hex = (h) => [parseInt(h.slice(1, 3), 16), parseInt(h.slice(3, 5), 16), parseInt(h.slice(5, 7), 16)];
const mixHex = (a, b, t) => { const A = hex(a), B = hex(b); return `rgb(${(A[0] + (B[0] - A[0]) * t) | 0},${(A[1] + (B[1] - A[1]) * t) | 0},${(A[2] + (B[2] - A[2]) * t) | 0})`; };

/**
 * @param {{wheel: number[], rose: number[]}} o   from state
 */
export function createBowl({ wheel, rose }) {
  const W = wheel.map(Number), roseSet = new Set((rose || []).map(Number));
  const colorOf = (v) => (v === 0 ? COL.zero : roseSet.has(v) ? COL.roseFelt : COL.plum);
  const geo = { cx: 0, cy: 0, R: 100 };
  const s = {
    rot: 0, rotVel: FEEL.ROTOR_IDLE, beamT: 0, lastNow: null,
    plan: null, launchAt: 0, rot0: 0, planEndRot: 0, index: -1, seated: false,
    phase: 'idle', speed: 0, rel: 0, r: FEEL.R_REST, tscale: 1,
    wake: false, whirlA: 0, trail: [], ring: null, landedEdge: false, landedShown: false,
    hint: false,   // THE THROW (flick.js): the curved arrow around the rim while the wheel may be flicked
    hitAt: -Infinity,   // THE GLYPH HIT: the landed pocket's rim glow over HIGHLIGHT_MS from the winning frame (callout.js)
  };
  /** The glow's 0..1 at station time `now` (0 outside the window). */
  const hitPulse = (now) => { const q = (now - s.hitAt) / HIGHLIGHT_MS; return q >= 0 && q < 1 ? Math.sin(q * Math.PI) : 0; };
  /** A paying landing: the pocket lights from `now`. */
  function glow(_index, now) { s.hitAt = now; }

  /** Where everything sits. cx, cy, R in CSS px of the canvas. */
  function layout(cx, cy, R) { geo.cx = cx; geo.cy = cy; geo.R = Math.max(40, R); }

  /** Law VIII: the press answers on its frame; the rotor picks up before the server has replied. A throw
   *  (flick.js) hands its own signed speed in, so the wheel leaves the finger the way it was swung. */
  function kick(vel = FEEL.ROTOR_KICK) {
    const v = Number.isFinite(Number(vel)) ? Number(vel) : FEEL.ROTOR_KICK;
    s.rotVel = v < 0 ? Math.min(s.rotVel, v) : Math.max(s.rotVel, v);
  }
  /** THE THROW: the finger drags the rotor round by `d` radians. A planned run owns the rotor, so it refuses then. */
  function turn(d) { if (!s.plan && Number.isFinite(Number(d))) s.rot += Number(d); }
  /** The arrow hint on or off (station.js arms it on the Spin button's own conditions). */
  function setHint(on) { s.hint = !!on; }
  /** Canvas-local CSS px. `angleAt` returns the pointer's angle in the convention `rot` grows in (here: y down). */
  function wheelHit(x, y) { return Math.hypot(x - geo.cx, y - geo.cy) <= geo.R * 1.06; }
  function angleAt(x, y) { return Math.atan2(y - geo.cy, x - geo.cx); }

  /** Start a planned spin at station time `now` (ms). */
  function launch(plan, now, { wake = false } = {}) {
    s.plan = plan; s.launchAt = now; s.rot0 = s.rot; s.index = plan.index; s.seated = false;
    s.wake = !!wake; s.trail.length = 0; s.landedEdge = false; s.landedShown = false; s.phase = 'run';
  }

  /** A ball resting in pocket `index` with no travel (a reopen, a skip, Back). */
  function seat(index, { wake = false } = {}) {
    if (s.plan) { s.rot = s.rot0 + s.plan.rot[s.plan.rot.length - 1]; }
    s.plan = null; s.index = index; s.seated = index >= 0; s.phase = index >= 0 ? 'rest' : 'idle';
    s.rel = restRel(index); s.r = FEEL.R_REST; s.speed = 0; s.tscale = 1; s.wake = !!wake; s.landedShown = index >= 0;
    s.trail.length = 0;
  }
  /** No ball at all. */
  function clear() { s.plan = null; s.index = -1; s.seated = false; s.phase = 'idle'; s.wake = false; s.landedShown = false; s.trail.length = 0; }

  /**
   * Advance to station time `now` (ms). Returns { phase, speed, landed } where `landed` is true on the ONE frame the
   * ball drops into its pocket (the landing moment's frame).
   */
  function update(now, { still = false } = {}) {
    const dt = s.lastNow == null ? 0 : clamp((now - s.lastNow) / 1000, 0, 0.1);
    s.lastNow = now;
    if (!still) s.beamT += dt;
    let landed = false;
    if (s.plan) {
      const sec = (now - s.launchAt) / 1000, x = sampleRun(s.plan, sec);
      const before = s.phase;
      s.rot = s.rot0 + x.rot; s.rel = x.rel; s.r = x.r; s.tscale = x.tscale; s.speed = x.speed; s.phase = x.phase;
      if ((x.phase === 'settle' || x.phase === 'rest') && before !== 'settle' && before !== 'rest') { landed = true; s.landedShown = true; }
      if (x.done) {
        const n = s.plan.rot.length;   // carry the rotor on at the speed the plan ended with
        s.rotVel = n > 1 ? (s.plan.rot[n - 1] - s.plan.rot[n - 2]) / FEEL.DT : FEEL.ROTOR_IDLE;
        s.plan = null; s.seated = true; s.phase = 'rest';
      }
    } else {
      const target = still ? 0 : FEEL.ROTOR_IDLE;
      if (still) s.rotVel += (target - s.rotVel) * Math.min(1, dt * FEEL.ROTOR_CALM_EASE);
      else s.rotVel += (target - s.rotVel) * (1 - Math.exp(-dt * FEEL.ROTOR_EASE));
      s.rot += s.rotVel * dt;
      if (s.seated) s.rel = restRel(s.index);
    }
    const spinning = s.phase === 'run' || s.phase === 'drop' || s.phase === 'rattle';
    s.whirlA += (((spinning || s.phase === 'settle') && s.wake ? 1 : 0) - s.whirlA) * Math.min(1, dt * FEEL.WHIRL_FADE);
    if (s.phase !== 'idle') {
      s.trail.unshift({ a: s.rot + s.rel, r: s.r, t: now / 1000, mv: spinning || s.phase === 'settle' });
      if (s.trail.length > 48) s.trail.length = 48;
    }
    return { phase: s.phase, speed: spinning ? s.speed : 0, landed, tscale: s.tscale };
  }

  function driftRing(dpr) {
    const R = geo.R, key = `${R | 0}:${dpr}`;
    if (s.ring && s.ring.key === key) return s.ring;
    const ro = R * 1.19, size = Math.ceil(ro * 2 + 4);
    const oc = document.createElement('canvas');
    oc.width = oc.height = Math.ceil(size * dpr);
    const c = oc.getContext('2d');
    c.setTransform(dpr, 0, 0, dpr, 0, 0); c.translate(size / 2, size / 2);
    for (const [ri, rO, dir] of [[R * 1.04, R * 1.105, 1], [R * 1.12, R * 1.185, -1]]) {
      const cells = 44, cw = TAU / cells;
      for (let k = 0; k < cells; k++) for (let j = 0; j < 4; j++) {
        const a0 = k * cw + (dir > 0 ? j : 3 - j) * cw / 4, a1 = a0 + cw / 4 + 0.002;
        c.beginPath(); c.arc(0, 0, rO, a0, a1); c.arc(0, 0, ri, a1, a0, true); c.closePath(); c.fillStyle = DRIFT[j]; c.fill();
      }
    }
    s.ring = { key, cv: oc, size, draws: (s.ring ? s.ring.draws : 0) + 1 };
    return s.ring;
  }

  /**
   * Draw the bowl into `g` (CSS px transform already set).
   * view = { dpr, now, k (strengthK), full, spiral (gate), kit, still, slowText }
   */
  function draw(g, view) {
    const { cx, cy, R } = geo, k = view.k, now = view.now;
    const ring = driftRing(view.dpr || 1);
    g.drawImage(ring.cv, cx - ring.size / 2, cy - ring.size / 2, ring.size, ring.size);

    g.save(); g.translate(cx, cy);
    g.fillStyle = '#2b1a30'; g.beginPath(); g.arc(0, 0, R, 0, TAU); g.fill();
    g.strokeStyle = COL.brass; g.lineWidth = 2.5; g.beginPath(); g.arc(0, 0, R, 0, TAU); g.stroke();
    g.fillStyle = '#1a1024'; g.beginPath(); g.arc(0, 0, R * 0.9, 0, TAU); g.fill();
    g.strokeStyle = 'rgba(232,194,122,.35)'; g.lineWidth = 1; g.beginPath(); g.arc(0, 0, R * 0.8, 0, TAU); g.stroke();

    // Velvet wake (Full only): the nap brushed behind the ball, settling over 1.3 s
    if (view.full && s.trail.length > 2) {
      g.save(); g.globalCompositeOperation = 'lighter'; g.lineCap = 'round';
      const tNow = now / 1000;
      for (let i = 1; i < s.trail.length; i++) {
        const a = s.trail[i], b = s.trail[i - 1];
        if (!a.mv) continue;
        const age = clamp((tNow - a.t) / FEEL.WAKE_SETTLE_S, 0, 1);
        g.globalAlpha = (1 - age) * (1 - age) * 0.28 * k; g.strokeStyle = i < 10 ? COL.mint : COL.rose; g.lineWidth = R * (0.02 + 0.05 * (1 - age));
        g.beginPath(); g.moveTo(Math.cos(a.a) * R * a.r, Math.sin(a.a) * R * a.r); g.lineTo(Math.cos(b.a) * R * b.r, Math.sin(b.a) * R * b.r); g.stroke();
        if (i % 3 === 0) {
          g.lineWidth = 1; g.globalAlpha = (1 - age) * 0.35 * k;
          for (const off of [-0.055, 0.055]) { const r1 = R * (a.r + off), r2 = R * (a.r + off * 1.6); g.beginPath(); g.moveTo(Math.cos(a.a) * r1, Math.sin(a.a) * r1); g.lineTo(Math.cos(a.a) * r2, Math.sin(a.a) * r2); g.stroke(); }
        }
      }
      g.restore();
    }

    // Pockets and numbers; the Lighthouse lights the numbers under its beam
    const rNumO = R * 0.76, rNumI = R * 0.65, rPocI = R * 0.55, beamA = beamAngle(s.beamT);
    const showLanded = s.landedShown && s.index >= 0;
    for (let i = 0; i < W.length; i++) {
      const a0 = s.rot + i * SEG, a1 = a0 + SEG, v = W[i];
      g.beginPath(); g.arc(0, 0, rNumO, a0, a1); g.arc(0, 0, rNumI, a1, a0, true); g.closePath();
      g.fillStyle = colorOf(v); g.fill();
      g.beginPath(); g.arc(0, 0, rNumI, a0, a1); g.arc(0, 0, rPocI, a1, a0, true); g.closePath();
      const won = showLanded && i === s.index, pulse = won ? hitPulse(now) : 0;
      g.fillStyle = won ? COL.mint : '#140c1f'; g.globalAlpha = won ? 0.55 + 0.4 * pulse : 1; g.fill(); g.globalAlpha = 1;
      if (pulse > 0) {   // the rim glow of the winning frame: a mint stroke around the pocket, out and back over 400 ms
        g.save(); g.strokeStyle = COL.mint; g.lineWidth = 2 + 2 * pulse; g.shadowColor = COL.mint; g.shadowBlur = 22 * pulse * k; g.globalAlpha = 0.5 + 0.5 * pulse;
        g.beginPath(); g.arc(0, 0, rNumO * (1 + 0.06 * pulse), a0, a1); g.arc(0, 0, rPocI, a1, a0, true); g.closePath(); g.stroke(); g.restore();
      }
      g.strokeStyle = 'rgba(232,194,122,.6)'; g.lineWidth = 1;
      g.beginPath(); g.moveTo(Math.cos(a0) * rPocI, Math.sin(a0) * rPocI); g.lineTo(Math.cos(a0) * rNumO, Math.sin(a0) * rNumO); g.stroke();
      const am = pocketAngle(i, s.rot), rr = (rNumO + rNumI) / 2, lit = beamLit(am, beamA) * (k < 1 ? 0.75 : 1);
      g.save(); g.translate(Math.cos(am) * rr, Math.sin(am) * rr); g.rotate(am + Math.PI / 2);
      if (lit > 0) { g.shadowColor = COL.brass; g.shadowBlur = 12 * lit; }
      g.fillStyle = lit > 0 ? mixHex(COL.text, '#fff3d6', lit) : COL.text;
      g.font = `${lit > 0.5 ? 800 : 600} ${Math.max(8, R * (0.055 + 0.02 * lit))}px ${FONT}`; g.textAlign = 'center'; g.textBaseline = 'middle';
      g.fillText(String(v), 0, 0); g.restore();
    }

    // Sparks: single 0.5 s fades on the fret the ball clipped (law 5), from the plan
    if (s.plan) {
      const sec = (now - s.launchAt) / 1000;
      for (const sp of s.plan.sparks) {
        const p = (sec - sp.at) / FEEL.SPARK_S;
        if (p < 0 || p >= 1) continue;
        const a = s.rot + sp.a;
        g.save(); g.globalAlpha = (1 - p) * 0.9 * (0.5 + 0.5 * k); g.strokeStyle = '#ffffff'; g.lineWidth = 2.5 + (1 - p) * 2; g.shadowColor = COL.mint; g.shadowBlur = 10;
        g.beginPath(); g.moveTo(Math.cos(a) * rPocI, Math.sin(a) * rPocI); g.lineTo(Math.cos(a) * rNumO * (1 + p * 0.06), Math.sin(a) * rNumO * (1 + p * 0.06)); g.stroke(); g.restore();
      }
    }

    // Turret: velvet dish, the Loom whirlpool on a Spiral Wake, arms that trail the rotor
    const tr = R * 0.5;
    const tg = g.createRadialGradient(0, 0, 4, 0, 0, tr);
    tg.addColorStop(0, '#3b2a55'); tg.addColorStop(1, '#1c1230');
    g.fillStyle = tg; g.beginPath(); g.arc(0, 0, tr, 0, TAU); g.fill();
    s.whirlDrawn = false;
    if (s.whirlA > 0.01) {
      if (view.spiral && view.kit) {
        g.save(); g.beginPath(); g.arc(0, 0, tr * 0.98, 0, TAU); g.clip();
        const px = tr * 2 * (view.dpr || 1);
        s.whirlDrawn = view.kit.draw(g, 'whirl', -tr, -tr, tr * 2, tr * 2,
          { angle: whirlAngle(s.rot), now, alpha: FEEL.WHIRL_ALPHA * s.whirlA * k, backing: px > 256 ? 'long' : 'small' });
        g.restore();
      } else {
        // spiral gate off: the dish stays velvet; the wake shows as a gold rim glow (and as text)
        g.save(); g.globalAlpha = 0.8 * s.whirlA * k; g.strokeStyle = COL.brass; g.lineWidth = 3; g.shadowColor = COL.brass; g.shadowBlur = 18;
        g.beginPath(); g.arc(0, 0, R * 1.0, 0, TAU); g.stroke(); g.beginPath(); g.arc(0, 0, tr, 0, TAU); g.stroke(); g.restore();
      }
    }
    g.strokeStyle = COL.brass; g.lineCap = 'round';
    for (let arm = 0; arm < 8; arm++) {
      g.lineWidth = arm % 2 ? 2 : 3.5; g.beginPath();
      for (let u = 0; u <= 1.001; u += 0.1) {
        const r = tr * (0.18 + 0.78 * u), a = s.rot + arm * TAU / 8 - u * 1.1;   // base grows with the rotor, bent back by u
        if (u === 0) g.moveTo(Math.cos(a) * r, Math.sin(a) * r); else g.lineTo(Math.cos(a) * r, Math.sin(a) * r);
      }
      g.stroke();
    }
    g.fillStyle = COL.brass; g.beginPath(); g.arc(0, 0, tr * 0.14, 0, TAU); g.fill();

    // The beam itself: a warm wedge from the turret lamp
    g.save(); g.globalCompositeOperation = 'lighter';
    const bg = g.createRadialGradient(0, 0, tr * 0.9, 0, 0, R * 0.95);
    bg.addColorStop(0, `rgba(232,194,122,${0.5 * k})`); bg.addColorStop(0.55, `rgba(232,194,122,${0.22 * k})`); bg.addColorStop(1, 'rgba(232,194,122,0)');
    g.fillStyle = bg; g.beginPath(); g.moveTo(Math.cos(beamA) * tr * 0.9, Math.sin(beamA) * tr * 0.9); g.arc(0, 0, R * 0.95, beamA - FEEL.BEAM_HALF, beamA + FEEL.BEAM_HALF); g.closePath(); g.fill();
    g.fillStyle = '#fff3d6'; g.shadowColor = COL.brass; g.shadowBlur = 14; g.beginPath(); g.arc(Math.cos(beamA) * tr * 0.92, Math.sin(beamA) * tr * 0.92, tr * 0.06, 0, TAU); g.fill();
    g.restore();
    g.restore();

    // The ball
    if (s.phase !== 'idle') {
      const a = s.rot + s.rel, bx = cx + Math.cos(a) * R * s.r, by = cy + Math.sin(a) * R * s.r, br = Math.max(4, R * 0.03);
      g.fillStyle = 'rgba(0,0,0,.35)'; g.beginPath(); g.arc(bx + 2, by + 3, br, 0, TAU); g.fill();
      g.save(); g.fillStyle = '#fff8ff'; g.shadowColor = COL.mint; g.shadowBlur = 12; g.beginPath(); g.arc(bx, by, br, 0, TAU); g.fill(); g.restore();
    }
    // THE THROW's hint: a curved, half-lit arrow around the rim, breathing on a 1.2 s cycle (still: no breath).
    if (s.hint) {
      const pulse = view.still ? 0.5 : (1 - Math.cos((now % HINT_MS) / HINT_MS * TAU)) / 2;
      const hr = R * (0.935 + 0.012 * pulse), a1 = HINT_A0 + HINT_ARC, alpha = (0.32 + 0.34 * pulse) * k;
      g.save(); g.translate(cx, cy); g.globalCompositeOperation = 'lighter';
      g.strokeStyle = `rgba(95,255,208,${alpha})`; g.lineWidth = Math.max(3, R * 0.045); g.lineCap = 'round';
      g.beginPath(); g.arc(0, 0, hr, HINT_A0, a1); g.stroke();
      const hx = Math.cos(a1) * hr, hy = Math.sin(a1) * hr, hs = Math.max(7, R * 0.12);
      g.translate(hx, hy); g.rotate(a1 + Math.PI / 2);   // the head points along the rim, the way the rotor turns
      g.fillStyle = `rgba(95,255,208,${alpha + 0.12})`;
      g.beginPath(); g.moveTo(hs * 0.62, 0); g.lineTo(-hs * 0.36, hs * 0.46); g.lineTo(-hs * 0.36, -hs * 0.46); g.closePath(); g.fill();
      g.restore();
    }
    if (s.phase === 'rattle' && s.tscale < 0.9 && view.slowText) {
      g.fillStyle = `rgba(232,194,122,${0.7 * (1 - s.tscale)})`; g.font = `500 12px Consolas, "DM Mono", monospace`; g.textAlign = 'center'; g.textBaseline = 'alphabetic';
      g.fillText(view.slowText, cx, cy - R * 1.28);
    }
  }

  /** Pocket `index` as a canvas-local box (the fx.gif_from rect), at the rotor's current angle. */
  function pocketBox(index) {
    const a = pocketAngle(index, s.rot), r = geo.R * FEEL.GIF_RADIUS, { w, h } = FEEL.GIF_BOX;
    return { x: geo.cx + Math.cos(a) * r - w / 2, y: geo.cy + Math.sin(a) * r - h / 2, w, h };
  }

  return {
    layout, kick, launch, seat, clear, update, draw, pocketBox, glow, turn, setHint, wheelHit, angleAt,
    get geo() { return { ...geo }; },
    get phase() { return s.phase; },
    /** Test seam: what the beam lights now, the rotor, the ball, the caches. */
    debug() {
      return { rot: s.rot, rotVel: s.rotVel, beamT: s.beamT, beamAngle: beamAngle(s.beamT), lit: litNumbers(W, s.rot, beamAngle(s.beamT)),
        phase: s.phase, index: s.index, pocket: s.index >= 0 ? W[s.index] : null, wake: s.wake, whirlA: s.whirlA, whirlDrawn: !!s.whirlDrawn,
        ringBuilds: s.ring ? s.ring.draws : 0, planned: s.plan ? { hits: s.plan.hits, restAt: s.plan.restAt, landAt: s.plan.landAt } : null,
        hint: s.hint, geo: { ...geo },
        ballAngle: s.rot + s.rel, rel: s.rel, radius: s.r, speed: s.speed, tscale: s.tscale, hitAt: s.hitAt, hitOn: hitPulse(s.lastNow ?? 0) > 0 };
    },
  };
}
