/* ============================================================================
 * games/impulse-control/tube2d.js - the Drop Tube without WebGL.
 *
 * Same contract as tube3d.js, drawn on a 2D canvas: an opaque spiral ribbon
 * winding into a concave basin, a soft glow travelling the path, ring pulses on
 * reveal/pop. The 3D tier's living-material pass has a proportionate echo here:
 * the whole spiral slowly ROTATES (hypnotic by design), a dashed energy pass
 * marches along the ribbon toward the basin, good pops burst 2D particles, a
 * denyHit jolts the spin, flashes the ribbon red and briefly reverses the
 * march, and setMood() drifts the ribbon's lit color pink -> lavender (gold on
 * a hot streak) while speeding both motions up with class progress.
 *
 * It carries the 3D tier's composition law too (see tube3d.js' header):
 *   MOUTH  the outer end is sized OFF-CANVAS from the viewport itself, so the
 *          ribbon can never be caught ending in frame at any window shape.
 *   BASIN  the inner end HOLDS at the rim radius for the last stretch, then
 *          PEELS off it and curls INWARD to end as a small open MOUTH facing
 *          the crater's centre - the ribbon delivers, it does not just stop.
 *          It still never spirals into the centre: the opening halts short of
 *          it, the crater is what lives there, and the DOM bubble nests in it.
 *
 * SEEDED, like the 3D tier: the class seed picks this ribbon's hue pair (on
 * the violet->rose arc; gold/red stay semantic), its spin direction, its dash
 * temperament, and the period of a small COMET that forever patrols the ribbon
 * mouth->basin, dimming while a real load travels. Same seed, same ribbon.
 *
 * When even a 2D context is unavailable (the DOM double, a hostile webview) it
 * degrades once more to a STATIC conic-gradient div - every method stays
 * callable and silent.
 * ==========================================================================*/

import { makeRng } from '../../core/rng.js';

const HOLD = 0.86;         // t where the spiral stops shrinking - matches 3D
const SQUASH = 0.8;        // the ground plane's foreshortening at this camera
const RIM_F = 0.20;        // basin rim radius as a fraction of min(W,H)
const BORE_F = 0.64;       // ribbon width as a fraction of the rim radius -
                           // the same bore/rim ratio the 3D tier is built on
const GAP_F = 1.5;         // minimum coil pitch, in ribbon widths
const OUT_F = 1.35;        // how far past the frame corner the mouth reaches

/* THE INNER MOUTH - the 2D echo of the 3D tier's peel. Same shape, same reason:
   a ribbon that levels into the rim and stops delivers nothing, so the last
   sliver turns inward and ends pointing at the middle. Added to the run, not
   carved out of the hold, so the ribbon still paints over the same arc of lit
   rim it always did. Values are the 3D constants divided by R_IN. */
const MOUTH_T = 0.965;     // t where the peel leaves the held ring
const MOUTH_SWEEP = 0.55;  // radians the peel carries around the rim
const MOUTH_F = 0.68;      // the opening's radius, as a fraction of RIM
const MOUTH_L0 = 0.52;     // Hermite tangent lengths (peel-off / aim-in), in RIM
const MOUTH_L1 = 0.50;
const MOUTH_DIP = 0.10;    // screen-down nudge at the opening, in RIM: the 3D
                           // mouth rides lower than its ring, and `lift` is this
                           // projection's convention for exactly that

const SPIN_BASE = 0.07;    // rad/s - matches the 3D tier
const SPIN_MAX = 0.17;
const FLOW_BASE = 30;      // dash-march px/s at SCALE 1
const FLOW_MAX = 85;

export function createTube2D(opts = {}) {
  const mount = opts.mount;
  const reduced = !!opts.reduced;

  /* the ribbon's seeded identity - mirrors the 3D tier's ranges */
  const rng = makeRng(String(opts.seed == null ? 'tube' : opts.seed) + '|tube2d');
  const hsl2rgb = (h, s, l) => {
    const f = (n) => {
      const k = (n + h / 30) % 12;
      const a = s * Math.min(l, 1 - l);
      return Math.round(255 * (l - a * Math.max(-1, Math.min(k - 3, 9 - k, 1))));
    };
    return f(0) + ',' + f(8) + ',' + f(4);
  };
  const ID = (() => {
    const hue = 285 + rng() * 55;
    const hue2 = Math.max(262, Math.min(345, hue + (rng() < 0.5 ? -1 : 1) * (18 + rng() * 22)));
    return {
      rgbA: hsl2rgb(hue, 0.92, 0.67),           // class-open pole
      rgbB: hsl2rgb(hue2, 0.62, 0.72),          // class-close pole
      spinSign: rng() < 0.3 ? -1 : 1,
      dashOn: 0.24 + rng() * 0.2,               // dash/gap temperament (of BORE)
      dashOff: 0.5 + rng() * 0.35,
      cometPeriod: 7 + rng() * 4,
    };
  })();

  /* ------------------------------------------------ static (last resort) */
  function staticTube() {
    try {
      if (mount && typeof document !== 'undefined' && document.createElement) {
        const div = document.createElement('div');
        div.className = 'g-ic-tube-static';
        mount.appendChild(div);
      }
    } catch (e) { /* noop */ }
    const noop = () => {};
    return {
      kind: 'static',
      setTravel: noop, loadPulse: noop, reveal: noop, pop: noop,
      denyHit: noop, denyPass: noop, setMood: noop,
      suspend: noop, resize: noop, destroy: noop,
    };
  }

  let canvas = null, g = null;
  try {
    canvas = document.createElement('canvas');
    canvas.className = 'g-ic-tube-canvas';
    g = canvas.getContext && canvas.getContext('2d');
    if (!g) return staticTube();
    mount.appendChild(canvas);
  } catch (e) { return staticTube(); }

  /* Ghost sprite (best effort). */
  let ghostImg = null;
  try {
    if (typeof Image === 'function') {
      const im = new Image();
      im.onload = () => { ghostImg = im; };
      im.src = new URL('./assets/bubble.png', import.meta.url).href;
    }
  } catch (e) { /* glow-only */ }

  let W = 800, H = 600, CX = 400, CY = 300, SCALE = 1;
  let RIM = 120, BORE = 76, ROUT = 900, TURNS = 3.5;
  function resize() {
    try {
      W = mount.clientWidth || 800; H = mount.clientHeight || 600;
      const dpr = Math.min(2, (typeof window !== 'undefined' && window.devicePixelRatio) || 1);
      canvas.width = Math.round(W * dpr); canvas.height = Math.round(H * dpr);
      canvas.style.width = W + 'px'; canvas.style.height = H + 'px';
      g.setTransform(dpr, 0, 0, dpr, 0, 0);
      /* dead centre, NOT nudged down: the DOM bubble is pinned to 50%/50% and
         the crater has to be the thing it lands in */
      CX = W / 2; CY = H / 2;
      SCALE = Math.min(W, H) * 0.0088;
      /* THE WHOLE COMPOSITION IS DERIVED FROM THE VIEWPORT, not from SCALE.
         The mouth has to clear the frame's widest half-axis - W/2 across, and
         H/(2*SQUASH) up the squashed vertical - on EVERY window shape, or the
         ribbon is caught ending in frame the way the 3D tube used to be. */
      RIM = Math.min(W, H) * RIM_F;
      BORE = RIM * BORE_F;
      ROUT = Math.max(W * 0.5, H / (2 * SQUASH)) * OUT_F;
      /* turns then follow from the span, so coils never crowd into each other
         on a squarer window (where ROUT is relatively closer to RIM) */
      TURNS = Math.max(2.2, Math.min(5.5, (ROUT - RIM) / (BORE * GAP_F)));
    } catch (e) { /* noop */ }
  }

  /* The flattened helix: same parametrisation as 3d, projected with a squash.
     Radius falls LINEARLY to the rim then HOLDS, so the last stretch is a level
     ring around the basin instead of a dive into the centre. `spin` rotates the
     whole spiral; the ghost uses the same at() so the load stays sealed to the
     ribbon under rotation. */
  let spin = 0;
  function at(t) {
    const aEnd = -Math.PI * 0.5 + spin + TURNS * Math.PI * 2;
    if (t > MOUTH_T) {
      /* the peel: one cubic Hermite from the ring's pose (position + its own
         tangent, so no kink) to the opening's pose (just inside the rim, tangent
         pointing dead at the centre). The s->u ease keeps the ribbon's speed
         continuous across the seam and lets the load settle into the mouth. */
      const s = Math.min(1, (t - MOUTH_T) / (1 - MOUTH_T));
      const u = s * (2 - s);
      const u2 = u * u, u3 = u2 * u;
      const h00 = 2 * u3 - 3 * u2 + 1, h10 = u3 - 2 * u2 + u;
      const h01 = -2 * u3 + 3 * u2, h11 = u3 - u2;
      const aM = aEnd + MOUTH_SWEEP;
      const cE = Math.cos(aEnd), sE = Math.sin(aEnd);
      const cM = Math.cos(aM), sM = Math.sin(aM);
      const px = h00 * (cE * RIM) + h10 * (-sE * RIM * MOUTH_L0)
               + h01 * (cM * RIM * MOUTH_F) + h11 * (-cM * RIM * MOUTH_L1);
      const py = h00 * (sE * RIM) + h10 * (cE * RIM * MOUTH_L0)
               + h01 * (sM * RIM * MOUTH_F) + h11 * (-sM * RIM * MOUTH_L1);
      return { x: CX + px, y: CY + py * SQUASH + RIM * MOUTH_DIP * u };
    }
    const tt = t / MOUTH_T;
    const a = -Math.PI * 0.5 + spin + tt * TURNS * Math.PI * 2;
    const k = Math.min(1, tt / HOLD);
    const r = RIM + (ROUT - RIM) * (1 - k);
    const lift = Math.pow(1 - k, 1.8) * RIM * 0.55;   // the outer coils ride high
    return { x: CX + Math.cos(a) * r, y: CY + Math.sin(a) * r * SQUASH - lift };
  }

  let travel = null, rimBoost = 0, mouthBoost = 0, clock = 0;
  const pulses = [];
  let suspended = false, dead = false;

  /* living-material state */
  let spinVel = reduced ? 0 : SPIN_BASE;
  let spinKick = 0;
  let flowSpeed = reduced ? 0 : FLOW_BASE;
  let flowDir = 1, flowRevT = 0, flowOff = 0;
  let redFlash = 0;
  let shakeT = 0;
  let mood = { progress: 0, streak: 0 };
  const parts = [];        // {x,y,vx,vy,t}

  let cometT = rng();      // start mid-patrol, seeded
  let cometLevel = 0;

  function litColor(alpha) {
    if (redFlash > 0.12) return 'rgba(255,64,96,' + alpha + ')';
    if (mood.streak >= 5) return 'rgba(240,194,75,' + alpha + ')';
    /* the seed's two poles trade places across the class, like the 3D tier */
    return 'rgba(' + (mood.progress > 0.5 ? ID.rgbB : ID.rgbA) + ',' + alpha + ')';
  }

  function draw(dt) {
    clock += dt;
    spinKick *= Math.pow(0.06, dt);
    spin += (spinVel + spinKick) * dt * ID.spinSign;
    if (flowRevT > 0) { flowRevT -= dt; if (flowRevT <= 0) flowDir = 1; }
    flowOff -= flowSpeed * SCALE * 0.12 * flowDir * dt;
    redFlash *= Math.pow(0.02, dt);
    shakeT = Math.max(0, shakeT - dt);

    g.clearRect(0, 0, W, H);
    g.save();
    if (shakeT > 0) {
      const s = shakeT * 16;
      g.translate((Math.random() - 0.5) * s, (Math.random() - 0.5) * s);
    }

    /* THE CRATER, drawn FIRST so the chute's held ring lands on its lip. Dark
       at the belly, lit only at the rim - the same "hole, not dome" ramp the 3D
       dish bakes into its texture. */
    const bR = RIM;
    const dish = g.createRadialGradient(CX, CY - bR * 0.10, bR * 0.04, CX, CY, bR);
    dish.addColorStop(0, 'rgba(2,2,7,.99)');
    dish.addColorStop(0.55, 'rgba(9,9,26,.99)');
    dish.addColorStop(0.88, 'rgba(26,24,58,.99)');
    dish.addColorStop(1, 'rgba(64,58,112,.98)');
    g.beginPath(); g.ellipse(CX, CY, bR, bR * SQUASH, 0, 0, Math.PI * 2);
    g.fillStyle = dish; g.fill();

    /* the lit lip, BEFORE the chute: the held ring then paints over the arc of
       rim it covers, which is the occlusion the 3D tier gets for free */
    g.beginPath(); g.ellipse(CX, CY, bR, bR * SQUASH, 0, 0, Math.PI * 2);
    g.strokeStyle = litColor(0.55 + rimBoost * 0.4);
    g.lineWidth = BORE * 0.06 + 2 + rimBoost * 3; g.stroke();

    /* chute body: dark underlay pass then a lit top edge pass */
    const passes = [
      { w: BORE * 1.14, c: 'rgba(16,16,36,.96)', dy: BORE * 0.04 },
      { w: BORE, c: 'rgba(42,42,78,.98)', dy: 0 },
      { w: BORE * 0.34, c: litColor(0.22 + redFlash * 0.4), dy: -BORE * 0.055 },
    ];
    for (const p of passes) {
      g.beginPath();
      for (let i = 0; i <= 220; i++) {
        const q = at(i / 220);
        if (i === 0) g.moveTo(q.x, q.y + p.dy); else g.lineTo(q.x, q.y + p.dy);
      }
      g.strokeStyle = p.c; g.lineWidth = p.w; g.lineCap = 'round'; g.stroke();
    }

    /* the energy march: a dashed pass flowing toward the basin. mouthBoost is
       the load entering off-canvas, so it brightens the march rather than
       ringing a mouth nobody can see any more. */
    if (flowSpeed > 0) {
      g.beginPath();
      for (let i = 0; i <= 220; i++) {
        const q = at(i / 220);
        if (i === 0) g.moveTo(q.x, q.y); else g.lineTo(q.x, q.y);
      }
      g.setLineDash([BORE * ID.dashOn, BORE * ID.dashOff]);
      g.lineDashOffset = flowOff;
      g.strokeStyle = litColor(0.3 + Math.sin(clock * 0.9) * 0.08 + redFlash * 0.4 + mouthBoost * 0.3);
      g.lineWidth = BORE * 0.16 + 2;
      g.stroke();
      g.setLineDash([]);
    }

    /* THE OPENING. Drawn AFTER the ribbon and the march, so both stop at it
       rather than running over it: a dark bore with one thin lit lip. The 3D
       tier gets this from a recessed cone; here a filled ellipse is the whole
       trick, and it is enough because the ribbon around it is already opaque. */
    {
      const m = at(1);
      /* SMALL and QUIET. A wide bore eats the ribbon's end and a bright lip
         turns the pair into a floating eye sitting on the basin - the 2D
         version of the blade the 3D tier's old end-cap was. It only has to be
         a dark spot with an edge; the opaque ribbon around it does the rest. */
      const br = BORE * 0.32;
      g.beginPath();
      g.ellipse(m.x, m.y, br, br * SQUASH, 0, 0, Math.PI * 2);
      g.fillStyle = 'rgba(5,5,16,.96)'; g.fill();
      /* two strokes, same division of labour as the 3D mouth: an UNLIT ring of
         pipe-wall that carries the shape, then a hairline of the lit accent on
         top. One accent-coloured stroke on its own reads as a neon eye. */
      g.strokeStyle = 'rgba(59,54,99,.95)';
      g.lineWidth = Math.max(1.5, BORE * 0.06); g.stroke();
      g.strokeStyle = litColor(0.16 + rimBoost * 0.18);
      g.lineWidth = Math.max(1, BORE * 0.02); g.stroke();
    }

    /* the comet: a short bright breath forever patrolling mouth->basin, dimmed
       while a real load travels (the load is the star) - the 2D echo of the 3D
       tier's patrol light */
    if (!reduced) {
      cometT += dt / ID.cometPeriod;
      if (cometT >= 1) cometT -= 1;
      cometLevel += (((travel == null) ? 0.55 : 0.15) - cometLevel) * Math.min(1, dt * 3);
      if (cometLevel > 0.03) {
        const SPAN = 0.045;
        const t0 = Math.min(1 - SPAN, cometT);
        g.beginPath();
        for (let i = 0; i <= 8; i++) {
          const q = at(t0 + (i / 8) * SPAN);
          if (i === 0) g.moveTo(q.x, q.y); else g.lineTo(q.x, q.y);
        }
        g.strokeStyle = litColor(cometLevel * 0.16);
        g.lineWidth = BORE * 0.66; g.stroke();
        g.strokeStyle = litColor(cometLevel * 0.5);
        g.lineWidth = BORE * 0.24; g.stroke();
      }
    }

    /* travelling ghost - sealed to the ribbon: soft glow first, the faintest
       bubble hint under it, both smaller than the ribbon's width */
    if (travel != null) {
      const q = at(Math.max(0, Math.min(1, travel)));
      const shim = reduced ? 0 : Math.sin(clock * 9) * 0.08;
      const halo = BORE * 0.85;
      const gl = g.createRadialGradient(q.x, q.y, 0, q.x, q.y, halo);
      gl.addColorStop(0, 'rgba(255,158,210,' + (0.5 + shim) + ')');
      gl.addColorStop(1, 'rgba(255,158,210,0)');
      g.beginPath(); g.arc(q.x, q.y, halo, 0, Math.PI * 2);
      g.fillStyle = gl; g.fill();
      if (ghostImg) {
        g.globalAlpha = 0.13 + shim * 0.5;
        const s = BORE * 0.72;   // INSIDE the bore, same law as the 3D ghost
        try { g.drawImage(ghostImg, q.x - s / 2, q.y - s / 2, s, s); } catch (e) { /* noop */ }
        g.globalAlpha = 1;
      }
    }

    /* pop particles */
    for (let i = parts.length - 1; i >= 0; i--) {
      const p = parts[i];
      p.t += dt;
      const k = p.t / 0.9;
      if (k >= 1) { parts.splice(i, 1); continue; }
      p.x += p.vx * dt; p.y += p.vy * dt; p.vy += 130 * SCALE * 0.12 * dt;
      g.beginPath(); g.arc(p.x, p.y, (3.4 * SCALE * 0.35 + 1.6) * (1 - k), 0, Math.PI * 2);
      g.fillStyle = p.c.replace('%A%', String(0.85 * (1 - k)));
      g.fill();
    }

    /* pulses */
    for (let i = pulses.length - 1; i >= 0; i--) {
      const p = pulses[i];
      p.t += dt;
      const k = Math.min(1, p.t / p.dur);
      g.beginPath();
      g.ellipse(CX, CY, bR * (0.55 + k * 2.2), bR * SQUASH * (0.55 + k * 2.2), 0, 0, Math.PI * 2);
      g.strokeStyle = p.c.replace('%A%', String(0.8 * (1 - k)));
      g.lineWidth = 3.5 * (1 - k) + 0.5; g.stroke();
      if (k >= 1) pulses.splice(i, 1);
    }

    g.restore();
    rimBoost *= 0.9; mouthBoost *= 0.88;
  }

  function burst(gold) {
    if (reduced) return;
    const c = gold ? 'rgba(240,194,75,%A%)' : 'rgba(255,143,200,%A%)';
    for (let i = 0; i < 22; i++) {
      const a = Math.random() * Math.PI * 2;
      const v = (30 + Math.random() * 90) * SCALE * 0.12;
      parts.push({ x: CX, y: CY, vx: Math.cos(a) * v * 1.6, vy: -Math.abs(Math.sin(a)) * v * 2.2 - v, t: 0, c });
    }
  }

  let rafId = 0, last = 0;
  function loop(ts) {
    if (dead || suspended) return;
    const dt = last ? Math.min(0.05, (ts - last) / 1000) : 0.016;
    last = ts;
    try { draw(dt); } catch (e) { /* never kill the loop */ }
    const raf = (typeof requestAnimationFrame === 'function') ? requestAnimationFrame : null;
    if (raf) rafId = raf(loop);
  }
  function kick() {
    const raf = (typeof requestAnimationFrame === 'function') ? requestAnimationFrame : null;
    last = 0;
    if (raf) rafId = raf(loop); else { try { draw(0.016); } catch (e) { /* noop */ } }
  }
  resize();
  kick();

  return {
    kind: '2d',
    setTravel(p) { travel = (p == null ? null : Number(p) || 0); },
    loadPulse() { mouthBoost = 1.2; },
    reveal() { rimBoost = 1.5; travel = null; },
    pop(good) {
      pulses.push({ t: 0, dur: reduced ? 0.01 : 0.55, c: good ? 'rgba(255,105,180,%A%)' : 'rgba(240,194,75,%A%)' });
      rimBoost = 1;
      if (good) burst(mood.streak >= 5);
    },
    denyHit() {
      pulses.push({ t: 0, dur: reduced ? 0.01 : 0.5, c: 'rgba(255,64,96,%A%)' });
      rimBoost = 0.8;
      redFlash = 1;
      if (!reduced) { spinKick = 1.7; flowDir = -1; flowRevT = 0.7; shakeT = 0.3; }
    },
    denyPass() { pulses.push({ t: 0, dur: reduced ? 0.01 : 0.8, c: 'rgba(184,166,232,%A%)' }); },
    setMood(m) {
      try {
        mood = { progress: Math.max(0, Math.min(1, Number(m && m.progress) || 0)),
                 streak: Math.max(0, Math.round(Number(m && m.streak) || 0)) };
        if (!reduced) {
          spinVel = SPIN_BASE + (SPIN_MAX - SPIN_BASE) * mood.progress;
          flowSpeed = FLOW_BASE + (FLOW_MAX - FLOW_BASE) * mood.progress;
        }
      } catch (e) { /* cosmetic */ }
    },
    suspend(on) {
      const want = !!on;
      if (want === suspended) return;
      suspended = want;
      if (!want && !dead) kick();
    },
    resize,
    destroy() {
      dead = true;
      try { if (rafId && typeof cancelAnimationFrame === 'function') cancelAnimationFrame(rafId); } catch (e) { /* noop */ }
      try { if (canvas && canvas.parentNode) canvas.parentNode.removeChild(canvas); } catch (e) { /* noop */ }
    },
  };
}
