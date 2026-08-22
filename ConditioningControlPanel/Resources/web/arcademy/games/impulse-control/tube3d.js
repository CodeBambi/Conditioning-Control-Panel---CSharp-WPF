/* ============================================================================
 * games/impulse-control/tube3d.js - the Drop Tube's three.js body.
 *
 * A top-down three-quarter view of an OPAQUE spiral chute winding into a
 * concave reveal basin at screen centre. THE SEAL IS THE FICTION: the bubble in
 * transit is never a visible object. What travels is light - a point light
 * inside the geometry lighting the wall locally, a soft procedural glow sprite
 * bleeding through the resin, and the faintest tinted hint of the classic
 * bubble sprite UNDER that glow (scaled inside the bore, never above it). The
 * reveal in the basin is the only moment a real bubble exists on screen, and
 * that one is DOM (render.js), not ours.
 *
 * THE TWO ENDS ARE BOTH HIDDEN, AND THAT IS A COMPOSITION LAW:
 *   MOUTH  R_OUT is sized so the outer coil's flat end-cap sits OUTSIDE the
 *          frustum at EVERY spin angle and every sane aspect (checked to 3:1 -
 *          see the note by R_OUT). The chute must read as arriving from
 *          somewhere off-frame; a visible cap turns the machine into a prop.
 *   BASIN  the last two thirds of a turn HOLD at R_IN, so the chute's own body
 *          is the basin's rim - and then the tail PEELS OFF that ring, turns
 *          inward and slightly down, and ends as an OPEN MOUTH aimed at the
 *          crater's centre, a little above the lip: a slide emptying onto a
 *          dish. That is the whole point of the shape - the DOM bubble reveals
 *          at screen centre because it just dropped out of there. The aim
 *          survives the spin for free: the rig turns about that same centre, so
 *          a mouth pointed at the middle stays pointed at the middle at every
 *          angle. The chute still NEVER passes over or under the centre point -
 *          the opening stops short of it - and the crater is what lives there.
 *
 * THE TUBE IS GROWN, NOT BUILT. The class seed (threaded down from index.js
 * through render.js) births a TUBE IDENTITY: a hue somewhere in the app's
 * violet->pink family, a spin direction, a band pitch, a flow temperament, and
 * a three-stop PATTERN JOURNEY drawn from a pool of archetypes (rings, chevrons,
 * lace, waves, eyes, runes). Same seed, same tube - a retake replays its class
 * in the same skin; a new class grows a new one. All of it rides makeRng, never
 * Math.random, except throwaway transients (particle scatter, glitch jitter).
 *
 * THE LIVING MATERIAL: the whole rig sits in one rotating group (slow hypnotic
 * spin - this is a hypno app, the spiral turning IS the aesthetic) and the
 * chute wears one 256x128 emissive canvas that marches toward the basin like a
 * slow swallow. The pattern does not SWITCH, it MORPHS: every archetype is a
 * point in one shared parameter space (bend/wave/rails/beads/eye/ticks), so a
 * ring physically bends into a chevron and sprouts beads into lace - redrawn in
 * place at <=8Hz only while the interpolation is actually moving. On top of the
 * march the texture ken-burns: the pattern rolls slowly around the bore
 * (offset.y) while its pitch breathes (repeat.x) - the wall never holds still,
 * and none of it costs a redraw. A comet light patrols mouth->basin between
 * loads (dimming while a real load travels - the load is the star), the key
 * light orbits against the spin so a specular sheen forever crawls the coils,
 * and the crater breathes: dishMat.color (unlit, so it cannot break the
 * hole-not-dome ramp) tints with the seed hue, exhales lavender on a denyPass,
 * and warms when a streak runs hot, while a faint pooled glow sprite blooms in
 * the belly at each reveal.
 *
 * CONTRACT WITH render.js (all methods must never throw):
 *   setTravel(p|null) · loadPulse() · reveal() · pop(good) · denyHit()
 *   denyPass() · setMood({progress,streak}) · suspend(on) · resize()
 *   destroy() · kind:'3d'
 *
 * BEATS (the tube participates in every verdict):
 *   pop(true)  basin ring + particle burst + a light racing UP the coil +
 *              the swallow GULPS (band march surges, veins flare)
 *   denyHit()  spin jolt + band flash to red + flow REVERSES + the pattern
 *              scrambles around the bore for half a second
 *   denyPass() lavender ring + ambient swell + the crater exhales lavender
 *
 * RESILIENCE: three.js is imported DYNAMICALLY and the WebGL context probed in
 * try/catch - any throw/reject means "use the 2D tube". Under the DOM double
 * this module is never even imported.
 *
 * MOTION: reduced -> no spin, no march, no morphing (the journey's first stop
 * is drawn once and held), no ken-burns, no comet, no orbit, no particles;
 * pulses are opacity steps. perf -> pixelRatio 1, antialias off, half the
 * particles, morph redraw throttled to 4Hz.
 * ==========================================================================*/

import { makeRng } from '../../core/rng.js';

const VENDOR = '../../vendor/three.module.min.js';

/* GEOMETRY. The camera below is fixed (0,11.4,6.4) / 40deg vertical fov, so the
   frustum's widest reach at the mouth's height y=H_TOP-TUBE_R is ~10.9 units
   from the axis at 2.05:1 and ~13.0 at 3:1. R_OUT - TUBE_R = 13.15 clears both,
   which is why the mouth may not be shrunk without moving the camera too. */
const TURNS = 4.5;           // wraps before the basin (was 2.35 - too few coils)
const HOLD = 0.86;           // t where the spiral stops shrinking: the last
                             // 0.63 turns ring the basin at a CONSTANT radius,
                             // so the chute's own body is the rim it feeds
const R_OUT = 14.0;          // helix radius at the mouth - OFF-FRAME, see above
const R_IN = 2.5;            // the held radius === the basin's rim circle
const H_TOP = 4.4;           // mouth height above the basin plane
const Y_END = 0.86;          // height of the held ring
const TUBE_R = 0.80;         // chute body radius (opaque) - bore dia 1.6
const RIM_R = 0.30;          // collar cross-section (the exposed arc of rim)
const Y_RIM = -0.30;         // collar centre: its crest (0.0) clears the chute's
                             // belly (0.06). NOTHING may intersect the chute -
                             // two near-parallel surfaces z-fight into a sawtooth
const DISH_THETA = 0.62;     // sphere-cap start (xPI): lower = deeper crater.
                             // 0.62 -> depth 1.70 against a 2.5 mouth. A SHALLOW
                             // cap reads as a dome no matter how it is shaded:
                             // the depth is what puts a visible far WALL above
                             // the belly, and the wall is the concavity cue
const BAND_REPEAT = 36;      // BASE ring repeats along the path (~6 units apart,
                             // the same physical pitch the old 7-over-2.35-turns
                             // had). The seed drifts this +-6 per class.

/* THE MOUTH. The chute used to level into the hold ring and simply STOP: a
   capped cross-section floating over the bowl pointing TANGENTIALLY, delivering
   nothing. The last sliver of the run now PEELS off that ring and turns inward,
   ending aimed at the crater's centre. The peel is ADDED to the run, not carved
   out of the hold, so the chute still dresses exactly the arc of rim it always
   did and the collar's bare seam is unmoved. */
const MOUTH_T = 0.965;       // t where the peel leaves the hold ring. Sized so
                             // the parametric SPEED matches across the seam (see
                             // the ease in helixAt) - a load must not brake
const MOUTH_SWEEP = 0.55;    // extra radians the peel carries around the rim,
                             // turning the same way the spiral already turns
const MOUTH_R = 1.75;        // the opening's centre: just inside the rim, and
                             // never over the middle - its innermost edge still
                             // stands ~1.2 off the axis, so the composition law
                             // above (nothing passes over the centre) holds
const MOUTH_Y = 0.54;        // how high the opening rides over the bowl. The dip
                             // is FLAT for the first half of the peel on purpose:
                             // the peel is still crossing OVER the collar and the
                             // lit lip there, and its belly clears them by 0.066
                             // and 0.05. Drop MOUTH_Y and the drop starts sooner,
                             // and that margin is the first thing it eats
const AIM_Y = -0.35;         // the height of the point it is aimed AT, on the
                             // axis: a hair under the rim plane, i.e. the middle
                             // of the crater's MOUTH - the centre of the platform
                             // the bubble lands on. Aiming further down (the
                             // belly, y -1.05) is the same fiction but tips the
                             // opening 42deg over instead of 24, and this camera
                             // looks DOWN 59deg: past about 30 the opening turns
                             // its back on us at every spin angle and the bore
                             // can never be seen. Flatter aim, readable hole
const MOUTH_L0 = 1.30;       // Hermite tangent lengths: peel-off / aim-in. Both
const MOUTH_L1 = 1.25;       // near the chord, which keeps the arc from bulging
const BORE_LEN = 0.48;       // how deep the darkened bore recedes
const BORE_F = 0.86;         // bore radius as a fraction of TUBE_R - the 14% left
                             // over is the pipe's apparent WALL, drawn as a thin
                             // annulus

const SPIN_BASE = 0.07;      // rad/s at class start (one turn ~90s)
const SPIN_MAX = 0.17;       // rad/s at class end
const FLOW_BASE = 0.22;      // band-march speed (uv/s) at class start
const FLOW_MAX = 0.62;

/* THE PATTERN SPACE. Every archetype is a point in ONE parameter space, so any
   two of them can be lerped and the wall reads as transforming, not crossfading:
   bend folds a ring into a chevron, beadR grows lace out of rails, eyeA opens
   an iris mid-column. All strokes are GRAYSCALE - the emissive tint is the
   material's, the texture is only the mask. */
const PRESETS = {
  rings:   { bend: 0,   wave: 0,  freq: 1.0, railA: 0.55, railGap: 26, beadR: 0,   eyeA: 0,   ticks: 0,   coreW: 6.0 },
  chevron: { bend: -24, wave: 0,  freq: 1.0, railA: 0,    railGap: 26, beadR: 0,   eyeA: 0,   ticks: 0,   coreW: 6.0 },
  lace:    { bend: 0,   wave: 0,  freq: 1.0, railA: 0.9,  railGap: 42, beadR: 6,   eyeA: 0,   ticks: 0,   coreW: 4.2 },
  waves:   { bend: 0,   wave: 15, freq: 1.6, railA: 0.5,  railGap: 18, beadR: 0,   eyeA: 0,   ticks: 0,   coreW: 5.0 },
  eyes:    { bend: 0,   wave: 5,  freq: 0.8, railA: 0,    railGap: 26, beadR: 0,   eyeA: 0.9, ticks: 0,   coreW: 4.2 },
  runes:   { bend: -8,  wave: 0,  freq: 1.0, railA: 0,    railGap: 26, beadR: 3.5, eyeA: 0,   ticks: 0.8, coreW: 5.2 },
};
const P_KEYS = ['bend', 'wave', 'freq', 'railA', 'railGap', 'beadR', 'eyeA', 'ticks', 'coreW'];

export async function createTube3D(opts = {}) {
  const mount = opts.mount;
  if (!mount || typeof document === 'undefined') throw new Error('no mount');
  const reduced = !!opts.reduced;
  const perf = !!opts.perf;

  /* ------------------------------------------------- the tube's identity
     One rng stream, drawn in a FIXED order (append-only, or every shipped
     tube changes skin). The ranges are the brand fence: hue lives on the
     violet->rose arc only, and gold/red stay semantic (streak/error). */
  const seed = String(opts.seed == null ? 'tube' : opts.seed);
  const rng = makeRng(seed + '|tube3d');
  const ID = (() => {
    const hue = 285 + rng() * 55;                 // 285 violet .. 340 rose
    const hue2 = Math.max(262, Math.min(345, hue + (rng() < 0.5 ? -1 : 1) * (18 + rng() * 22)));
    const spinSign = rng() < 0.3 ? -1 : 1;        // some tubes turn widdershins
    const pitch = Math.round(BAND_REPEAT + (rng() - 0.5) * 12);
    const flowMul = 0.88 + rng() * 0.26;
    const spinMul = 0.9 + rng() * 0.22;
    const kbV = (0.006 + rng() * 0.008) * (rng() < 0.5 ? -1 : 1);  // bore-roll uv/s
    const cometPeriod = 7 + rng() * 4;            // one mouth->basin patrol, s
    const cols = rng() < 0.5 ? 3 : 4;             // columns per 256px wrap
    const colOff = rng() * 64;
    const phases = [rng() * 6.28, rng() * 6.28, rng() * 6.28, rng() * 6.28];
    /* the journey: three DISTINCT stops through the pattern space, each with
       its genes jittered once so two classes sharing a stop still differ */
    const pool = Object.keys(PRESETS).sort();     // stable order under the rng
    for (let i = pool.length - 1; i > 0; i--) {
      const j = Math.min(i, Math.floor(rng() * (i + 1)));
      const sw = pool[i]; pool[i] = pool[j]; pool[j] = sw;
    }
    const journey = pool.slice(0, 3).map((k) => {
      const base = PRESETS[k], out = {};
      for (const f of P_KEYS) out[f] = base[f];
      out.bend *= 0.8 + rng() * 0.5;
      out.wave *= 0.8 + rng() * 0.5;
      out.freq *= 0.85 + rng() * 0.4;
      out.coreW = Math.max(3.4, out.coreW * (0.85 + rng() * 0.4));
      if (out.beadR > 0) out.beadR *= 0.8 + rng() * 0.5;
      return out;
    });
    return { hue, hue2, spinSign, pitch, flowMul, spinMul, kbV, cometPeriod, cols, colOff, phases, journey };
  })();

  const THREE = await import(VENDOR);

  const canvas = document.createElement('canvas');
  canvas.className = 'g-ic-tube-canvas';
  const renderer = new THREE.WebGLRenderer({
    canvas, alpha: true, antialias: !perf, powerPreference: 'low-power',
  });
  // Some webviews hand back a dead context without throwing; probe it.
  const gl = renderer.getContext();
  if (!gl || (typeof gl.isContextLost === 'function' && gl.isContextLost())) {
    throw new Error('webgl context unavailable');
  }
  renderer.setPixelRatio(Math.min(perf ? 1 : 2, (typeof window !== 'undefined' && window.devicePixelRatio) || 1));
  mount.appendChild(canvas);

  const scene = new THREE.Scene();
  const camera = new THREE.PerspectiveCamera(40, 1, 0.1, 70);
  camera.position.set(0, 11.4, 6.4);
  /* aimed at the dish's INTERIOR, not the rim plane: the crater's belly is what
     the DOM bubble (screen centre, always) has to look like it is sitting in */
  camera.lookAt(0, -1.05, -0.02);

  /* Everything that must stay coherent under the spin lives in one group. */
  const world = new THREE.Group();
  scene.add(world);

  /* ------------------------------------------------------------ the chute */
  /* THE PEEL'S BASIS, solved once (plain numbers - helixAt is called every
     frame by three followers and must not mint anything but its result).
     The hold ring ends at A_END pointing TANGENTIALLY; the opening has to end
     pointing AT the crater. Two poses plus two tangents is a cubic Hermite, and
     m0 is literally the ring's own tangent - which is why the join has no kink
     and, because that tangent's y is zero, why the dip starts perfectly flat
     instead of digging into the collar it is still passing over. */
  const A_END = -Math.PI * 0.5 + TURNS * Math.PI * 2;
  const A_MOUTH = A_END + MOUTH_SWEEP;
  const M0X = Math.cos(A_END) * R_IN, M0Z = Math.sin(A_END) * R_IN;
  const M1X = Math.cos(A_MOUTH) * MOUTH_R, M1Z = Math.sin(A_MOUTH) * MOUTH_R;
  const T0X = -Math.sin(A_END) * MOUTH_L0, T0Z = Math.cos(A_END) * MOUTH_L0;
  /* the aim: a unit vector from the opening to the crater's centre. It is both
     the curve's terminal tangent and the normal of the opening's face, and
     everything that has to agree with it - the bore, the wall annulus, the lip
     - is built off this one vector, so they cannot drift apart. */
  const AIM = (() => {
    const dx = -M1X, dy = AIM_Y - MOUTH_Y, dz = -M1Z;
    const n = Math.hypot(dx, dy, dz) || 1;
    return { x: dx / n, y: dy / n, z: dz / n };
  })();
  const T1X = AIM.x * MOUTH_L1, T1Y = AIM.y * MOUTH_L1, T1Z = AIM.z * MOUTH_L1;

  const helixAt = (t) => {
    // t 0 (mouth, off-frame) -> 1 (the OPENING over the crater). The radius
    // falls LINEARLY over the first HOLD of the run so every coil is spaced the
    // same (11.5 / 3.87 turns = 2.97, against a bore of 1.6 - they can never
    // intersect), then HOLDS at R_IN: two thirds of a turn is a level ring
    // around the basin, and that ring IS the rim the chute feeds. The last
    // 1-MOUTH_T peels off it and aims into the bowl.
    if (t > MOUTH_T) {
      /* ease-out on the way in: du/ds is 2 at the seam, which is exactly the
         factor that makes the peel's world-speed match the ring's (the peel is
         short in t but shorter still in length), and 0 at the end, so a load
         SETTLES into the opening instead of arriving at full tilt. It also
         crowds the samples where the bend is tightest, for free. */
      const s = Math.min(1, (t - MOUTH_T) / (1 - MOUTH_T));
      const u = s * (2 - s);
      const u2 = u * u, u3 = u2 * u;
      const h00 = 2 * u3 - 3 * u2 + 1, h10 = u3 - 2 * u2 + u;
      const h01 = -2 * u3 + 3 * u2, h11 = u3 - u2;
      return new THREE.Vector3(
        h00 * M0X + h10 * T0X + h01 * M1X + h11 * T1X,
        h00 * Y_END + h01 * MOUTH_Y + h11 * T1Y,
        h00 * M0Z + h10 * T0Z + h01 * M1Z + h11 * T1Z
      );
    }
    const tt = t / MOUTH_T;
    const a = -Math.PI * 0.5 + tt * TURNS * Math.PI * 2;
    const k = Math.min(1, tt / HOLD);
    const r = R_IN + (R_OUT - R_IN) * (1 - k);
    const y = Y_END + (H_TOP - Y_END) * Math.pow(1 - k, 1.8);
    return new THREE.Vector3(Math.cos(a) * r, y, Math.sin(a) * r);
  };
  const pts = [];
  for (let i = 0; i <= 350; i++) pts.push(helixAt(i / 350));
  const curve = new THREE.CatmullRomCurve3(pts);
  /* the peel owns only 3.5% of the index range but that is ~17 rings of mesh
     across its 90 degrees of turn, which is where the extra segments went */
  const tubeGeo = new THREE.TubeGeometry(curve, perf ? 320 : 500, TUBE_R, perf ? 10 : 16, false);

  /* The band canvas: ONE 256x128 surface, redrawn in place (no re-alloc).
     TubeGeometry maps u ALONG the path, so vertical elements here become rings
     that march down the chute when offset.x moves. */
  const bandCanvas = document.createElement('canvas');
  bandCanvas.width = 256; bandCanvas.height = 128;
  const bandTex = new THREE.CanvasTexture(bandCanvas);
  bandTex.wrapS = THREE.RepeatWrapping;
  bandTex.wrapT = THREE.RepeatWrapping;
  bandTex.repeat.set(ID.pitch, 1);

  /* ------------------------------------------------------ the morph engine
     paramsAt(s) walks the seed's three-stop journey with PLATEAUS: hold stop
     A, glide A->B, hold B, glide B->C. drawMorph paints whatever point of the
     pattern space it is handed; the frame loop only calls it while the params
     are actually moving (and never faster than the redraw throttle), so a
     plateau costs zero redraws and a glide costs a handful of 256x128 fills. */
  const P_CUR = {}, P_LAST = { _never: true };
  function plateau(t) {
    const a = 0.22, b = 0.78;
    if (t <= a) return 0;
    if (t >= b) return 1;
    const u = (t - a) / (b - a);
    return u * u * (3 - 2 * u);
  }
  function paramsAt(s) {
    const J = ID.journey;
    const half = Math.max(0, Math.min(1, s)) * 2;
    const A = half < 1 ? J[0] : J[1];
    const B = half < 1 ? J[1] : J[2];
    const w = plateau(half < 1 ? half : half - 1);
    for (const f of P_KEYS) P_CUR[f] = A[f] + (B[f] - A[f]) * w;
    return P_CUR;
  }
  const triAt = (y) => Math.max(0, 1 - Math.abs((y - 64) / 72));
  function drawElement(c, xc, P, phase) {
    /* one vein: a 12-segment polyline whose x(y) folds (bend) and sways (wave).
       Soft under-glow pass first, then the core - thin luminous veins in dark
       resin, never plush stripes. */
    const seg = 12;
    c.beginPath();
    for (let k = 0; k <= seg; k++) {
      const y = -8 + (k * 144) / seg;
      const x = xc + P.bend * triAt(y) + P.wave * Math.sin((y / 128) * P.freq * 6.283 + phase);
      if (k === 0) c.moveTo(x, y); else c.lineTo(x, y);
    }
    c.globalAlpha = 0.22; c.lineWidth = P.coreW * 2.8; c.stroke();
    c.globalAlpha = 0.95; c.lineWidth = P.coreW; c.stroke();
    if (P.railA > 0.03) {
      c.beginPath();
      for (let k = 0; k <= seg; k++) {
        const y = -8 + (k * 144) / seg;
        const x = xc + P.railGap + P.bend * triAt(y) + P.wave * Math.sin((y / 128) * P.freq * 6.283 + phase);
        if (k === 0) c.moveTo(x, y); else c.lineTo(x, y);
      }
      c.globalAlpha = P.railA * 0.8; c.lineWidth = Math.max(1.4, P.coreW * 0.6); c.stroke();
    }
    if (P.beadR > 0.4) {
      c.globalAlpha = 0.9;
      for (const y of [20, 52, 84, 116]) {
        const x = xc + P.railGap * 0.5 + P.bend * triAt(y);
        c.beginPath(); c.arc(x, y, P.beadR, 0, 6.283); c.fill();
      }
    }
    if (P.eyeA > 0.03) {
      /* an iris opening mid-column - this is a hypno app */
      c.globalAlpha = P.eyeA * 0.85; c.lineWidth = 2.6;
      c.beginPath(); c.ellipse(xc + P.bend * 0.4, 64, 15, 30, 0, 0, 6.283); c.stroke();
      c.globalAlpha = P.eyeA * 0.7;
      c.beginPath(); c.arc(xc + P.bend * 0.4, 64, 4.5, 0, 6.283); c.fill();
    }
    if (P.ticks > 0.05) {
      c.globalAlpha = P.ticks * 0.8; c.lineWidth = 2.4;
      for (const y of [30, 64, 98]) {
        const x = xc - 12 + P.bend * triAt(y);
        c.beginPath(); c.moveTo(x - 6, y); c.lineTo(x + 6, y); c.stroke();
      }
    }
    c.globalAlpha = 1;
  }
  function drawMorph(P) {
    const c = bandCanvas.getContext('2d');
    if (!c) return;
    c.globalAlpha = 1;
    c.fillStyle = '#000';
    c.fillRect(0, 0, 256, 128);
    c.strokeStyle = '#fff'; c.fillStyle = '#fff'; c.lineCap = 'round';
    const spacing = 256 / ID.cols;
    for (let ci = 0; ci < ID.cols; ci++) {
      const xc = ((ci * spacing + ID.colOff) % 256 + 256) % 256;
      /* three copies so bends/waves that spill the edge tile seamlessly */
      drawElement(c, xc - 256, P, ID.phases[ci]);
      drawElement(c, xc, P, ID.phases[ci]);
      drawElement(c, xc + 256, P, ID.phases[ci]);
    }
    let d = 0;
    for (const f of P_KEYS) { d += Math.abs(P[f] - (P_LAST[f] || 0)); P_LAST[f] = P[f]; }
    delete P_LAST._never;
    bandTex.needsUpdate = true;
    return d;
  }
  drawMorph(paramsAt(0));

  const tubeMat = new THREE.MeshStandardMaterial({
    color: 0x2a2a4e, roughness: 0.3, metalness: 0.42,
    emissive: 0xff69b4, emissiveIntensity: 0.36,
    emissiveMap: bandTex,
  });
  const tube = new THREE.Mesh(tubeGeo, tubeMat);
  world.add(tube);

  /* ------------------------------------------------------------ the basin
     THE CRATER. A sphere cap opened upward (BackSide, so we look at its inside)
     whose mouth is exactly the held ring's radius, sunk so its lip tucks under
     the chute's belly, wearing a BAKED radial ramp: lit at the lip, black at the
     belly. Concavity has to be PAINTED, not just modelled - a BackSide cap has
     its normals pointing away from us, so the scene lights read it inside-out
     (they light the belly brightest, which reads convex, a dome not a dish).
     Hence MeshBasicMaterial: the ramp is the shading, and it cannot be lied to. */
  const basin = new THREE.Group();
  const DISH_R = R_IN / Math.sin(Math.PI * DISH_THETA);
  const dishCanvas = document.createElement('canvas');
  dishCanvas.width = 8; dishCanvas.height = 128;
  (() => {
    const c = dishCanvas.getContext('2d');
    if (!c) return;
    /* SphereGeometry writes uv.y = 1 at thetaStart (the lip) and CanvasTexture
       flips Y, so canvas TOP === the lip and canvas BOTTOM === the belly. */
    /* A THIN lit lip over a deep black well. The ramp has to fall off fast: a
       broad bright band up top renders as a lit sphere, not as a hole - the eye
       reads "wide gradient = round solid, hard edge + darkness = cavity". */
    const g2 = c.createLinearGradient(0, 0, 0, 128);
    /* The ramp is read RADIALLY and the sphere-cap crowds v near the lip, so the
       lit band has to live inside the first ~4% of the canvas or it becomes the
       whole visible wall - which renders as a lit egg, the exact failure this
       basin had. Bright hairline lip, then straight down into the dark. */
    g2.addColorStop(0, '#b3aae8');
    g2.addColorStop(0.035, '#4b4578');
    g2.addColorStop(0.09, '#1e1b3c');
    g2.addColorStop(0.18, '#100e28');
    g2.addColorStop(0.40, '#08071a');
    g2.addColorStop(1, '#020207');
    c.fillStyle = g2;
    c.fillRect(0, 0, 8, 128);
  })();
  const dishTex = new THREE.CanvasTexture(dishCanvas);
  try { dishTex.colorSpace = THREE.SRGBColorSpace; } catch (e) { /* older three */ }
  const dishMat = new THREE.MeshBasicMaterial({ map: dishTex, side: THREE.BackSide });
  const dish = new THREE.Mesh(
    new THREE.SphereGeometry(DISH_R, perf ? 32 : 48, perf ? 14 : 24, 0, Math.PI * 2, Math.PI * DISH_THETA, Math.PI * (1 - DISH_THETA)),
    dishMat
  );
  dish.position.y = Y_RIM - DISH_R * Math.cos(Math.PI * DISH_THETA);
  basin.add(dish);

  /* THE COLLAR. The chute's held ring only wraps two thirds of the basin; this
     dresses the exposed arc of lip so the crater never ends on a raw cut edge.
     It carries the chute's BASE colour, not a contrasting one - a bright ring
     under a dark pipe just draws a line around the seam. */
  const rimMat = new THREE.MeshStandardMaterial({
    color: 0x2c2c50, roughness: 0.32, metalness: 0.5,
    emissive: 0xff69b4, emissiveIntensity: 0.05,
  });
  const rim = new THREE.Mesh(new THREE.TorusGeometry(R_IN, RIM_R, perf ? 8 : 12, perf ? 44 : 72), rimMat);
  rim.rotation.x = Math.PI / 2;
  rim.position.y = Y_RIM;
  basin.add(rim);

  /* THE MOUTH, in three parts. TubeGeometry is open at BOTH ends and caps
     nothing, so the terminal cross-section used to be closed by a flat disc -
     which was right while the end pointed sideways and read as nothing at all.
     Now the end is the delivery, and it has to read as a HOLE the bubble fell
     out of. A hole is cheapest to fake with recessed dark geometry, not with
     shading:
       bore   a tapering cone, BackSide (we look at its inside), wearing a ramp
              that is dimly lit at the opening and black in the throat.
       wall   the annulus between bore and wall IS the pipe's thickness. It is
              what still reads when the opening is turned away from us, which -
              see AIM_Y - is most of the spin: an outlined ring says "cut pipe",
              a filled face says BLADE, which is what the old cap taught us.
       lip    a hairline of light ON the opening's edge, the same trick and the
              same restraint as the crater's lip. It draws the circle; it never
              fills it. No bead.
     Nothing here runs near-parallel to the chute: bore and wall share only a
     boundary with it, and the lip crosses at a steep angle. */
  const mouthP = helixAt(1);
  const mouthAim = new THREE.Vector3(AIM.x, AIM.y, AIM.z);
  const Q_AXIS = new THREE.Quaternion().setFromUnitVectors(new THREE.Vector3(0, 1, 0), mouthAim);
  const Q_FACE = new THREE.Quaternion().setFromUnitVectors(new THREE.Vector3(0, 0, 1), mouthAim);
  const BORE_R = TUBE_R * BORE_F;

  const boreCanvas = document.createElement('canvas');
  boreCanvas.width = 4; boreCanvas.height = 64;
  (() => {
    const c = boreCanvas.getContext('2d');
    if (!c) return;
    /* CylinderGeometry writes v=1 at +Y (the opening) and CanvasTexture flips Y,
       so canvas TOP === the lip of the bore and canvas BOTTOM === its throat. */
    const g2 = c.createLinearGradient(0, 0, 0, 64);
    g2.addColorStop(0, '#4e4880');
    g2.addColorStop(0.22, '#221f4a');
    g2.addColorStop(0.55, '#0a0920');
    g2.addColorStop(1, '#03030c');
    c.fillStyle = g2;
    c.fillRect(0, 0, 4, 64);
  })();
  const boreTex = new THREE.CanvasTexture(boreCanvas);
  try { boreTex.colorSpace = THREE.SRGBColorSpace; } catch (e) { /* older three */ }
  const boreMat = new THREE.MeshBasicMaterial({ map: boreTex, side: THREE.BackSide });
  /* A CONE, tapering to a POINT, and both halves of that matter. Tapering is
     what a throat looks like from a shallow angle - a straight sleeve reads as
     a ring, a taper reads as depth. The point is what closes it: the first cut
     of this was a sleeve plus a flat floor disc, and a flat disc set back inside
     a BENDING tube pokes its edge out past the wall on the outside of the bend -
     a green crescent in the debug pass, an unexplained sliver in the real one.
     A cone's every point lies within BORE_R of the opening's centre, so it is
     strictly inside the chute's own solid and can never surface. */
  const bore = new THREE.Mesh(
    new THREE.CylinderGeometry(BORE_R, 0, BORE_LEN, perf ? 16 : 28, 1, true), boreMat
  );
  bore.quaternion.copy(Q_AXIS);
  bore.position.copy(mouthP).addScaledVector(mouthAim, -BORE_LEN * 0.5);
  basin.add(bore);

  /* its OWN material - not rimMat (the frame loop drives that one with rimBoost,
     and a mouth that flares on every reveal is a lamp hung over the crater).
     UNLIT, and that is the point: this annulus faces down into the bowl, so a
     lit material turns it black (the key light is overhead) and the opening
     vanishes into the crater it is hanging over. A fixed mid tone keeps the
     ring of pipe-wall readable at EVERY spin angle - which is the whole promise
     of aiming at the centre. It stays a RING, never a disc: a filled face this
     bright standing across the bore is the blade the old cap taught us about. */
  const noseMat = new THREE.MeshBasicMaterial({ color: 0x3b3663, side: THREE.DoubleSide });
  const mouthWall = new THREE.Mesh(
    new THREE.RingGeometry(BORE_R, TUBE_R * 0.999, perf ? 16 : 28), noseMat
  );
  mouthWall.quaternion.copy(Q_FACE);
  mouthWall.position.copy(mouthP);
  basin.add(mouthWall);

  const mouthLipMat = new THREE.MeshBasicMaterial({
    color: 0xffb8dd, transparent: true, opacity: 0.30, depthWrite: false,
  });
  const mouthLip = new THREE.Mesh(
    new THREE.TorusGeometry(TUBE_R * 0.955, 0.034, 8, perf ? 24 : 40), mouthLipMat
  );
  mouthLip.quaternion.copy(Q_FACE);
  mouthLip.position.copy(mouthP);
  basin.add(mouthLip);

  /* A hairline of light ON the lip - reads the crater's edge against the dark
     belly without lighting the belly itself. */
  const lipMat = new THREE.MeshBasicMaterial({
    color: 0xffb8dd, transparent: true, opacity: 0.22, depthWrite: false,
  });
  const lip = new THREE.Mesh(new THREE.TorusGeometry(R_IN - RIM_R * 0.5, 0.035, 8, 72), lipMat);
  lip.rotation.x = Math.PI / 2;
  lip.position.y = Y_RIM + RIM_R * 0.86;   // rides the collar's inner shoulder
  basin.add(lip);
  world.add(basin);

  /* -------------------------------------------- the sealed travelling load
     The glow is drawn AT the helix centre (inside the bore) with no depth
     test: it bleeds through the opaque wall as subsurface light instead of
     sitting on top as an object. The bubble hint is smaller than the bore and
     barely-there - a suggestion, never a ball. */
  const glow = new THREE.PointLight(0xff9ed2, 0, 6.6, 2);
  world.add(glow);

  function softDiscTexture(inner, outer) {
    const cv = document.createElement('canvas');
    cv.width = 128; cv.height = 128;
    const c = cv.getContext('2d');
    if (c) {
      const g2 = c.createRadialGradient(64, 64, 4, 64, 64, 62);
      g2.addColorStop(0, inner);
      g2.addColorStop(0.45, outer);
      g2.addColorStop(1, 'rgba(255,120,190,0)');
      c.fillStyle = g2;
      c.fillRect(0, 0, 128, 128);
    }
    return new THREE.CanvasTexture(cv);
  }
  const haloTex = softDiscTexture('rgba(255,236,248,0.95)', 'rgba(255,130,200,0.5)');
  const halo = new THREE.Sprite(new THREE.SpriteMaterial({
    map: haloTex, transparent: true, opacity: 0, depthWrite: false, depthTest: false,
    blending: THREE.AdditiveBlending, color: 0xffc4e4,
  }));
  halo.scale.set(2.7, 2.7, 1);   // ~1.7x the bore: light bleeds, the load does not
  halo.renderOrder = 10;
  world.add(halo);

  let ghost = null;
  try {
    const tex = new THREE.TextureLoader().load(new URL('./assets/bubble.png', import.meta.url).href);
    ghost = new THREE.Sprite(new THREE.SpriteMaterial({
      map: tex, transparent: true, opacity: 0.0, depthWrite: false, depthTest: false,
      blending: THREE.AdditiveBlending, color: 0xffd2ec,
    }));
    ghost.scale.set(1.16, 1.16, 1);   // INSIDE the bore (dia 1.6), never over it
    ghost.renderOrder = 11;
    world.add(ghost);
  } catch (e) { /* halo-only ghost is fine */ }

  /* ---------------------------------------------------- the coil runner
     A light that races UP the chute on a good pop - the tube swallowing the
     reward back out. Idle between beats. */
  const runner = new THREE.PointLight(0xffb2d8, 0, 4.6, 2);
  world.add(runner);
  let runT = -1;             // -1 idle · 1 -> 0 racing basin -> mouth

  /* ------------------------------------------------------------ the comet
     A faint light forever patrolling mouth->basin INSIDE the bore - the tube
     digesting nothing in particular. It keeps the machine alive between loads
     and DIMS while a real load travels: the load is the star. */
  const comet = new THREE.PointLight(0xd8a6ff, 0, 5, 2);
  world.add(comet);
  let cometT = rng();        // start mid-patrol, seeded
  let cometLevel = 0;

  /* ---------------------------------------------------------- the pool glow
     A small additive breath lying in the crater belly. This is the sanctioned
     way to put light in the bowl: a SEPARATE element, never a wider dish ramp
     (a broad lit band re-inflates the crater into an egg - see the dish note).
     It idles near-invisible and blooms on the reveal, right where the DOM
     bubble lands. */
  const poolTex = softDiscTexture('rgba(255,150,205,0.5)', 'rgba(190,120,215,0.22)');
  const pool = new THREE.Sprite(new THREE.SpriteMaterial({
    /* its OWN texture: haloTex has a near-white heart, and a white heart at
       the crater's centre reads as a milky ball floating in the hole */
    map: poolTex, transparent: true, opacity: 0, depthWrite: false, depthTest: false,
    blending: THREE.AdditiveBlending, color: 0xffc4e4,
  }));
  pool.scale.set(1.8, 1.8, 1);   // inside the 2.5 crater mouth, clear of the chute
  pool.position.set(0, -0.9, 0);
  pool.renderOrder = 8;
  world.add(pool);

  /* -------------------------------------------------------- pop particles */
  const P_COUNT = perf ? 22 : 42;
  const pGeo = new THREE.BufferGeometry();
  const pPos = new Float32Array(P_COUNT * 3);
  pGeo.setAttribute('position', new THREE.BufferAttribute(pPos, 3));
  const pMat = new THREE.PointsMaterial({
    color: 0xff8fc8, size: 0.22, transparent: true, opacity: 0,
    depthWrite: false, blending: THREE.AdditiveBlending, map: haloTex,
  });
  const points = new THREE.Points(pGeo, pMat);
  points.renderOrder = 9;
  world.add(points);
  const pVel = new Float32Array(P_COUNT * 3);
  let pLife = -1;            // -1 idle · counts up to P_DUR
  const P_DUR = 0.9;
  function burstParticles(gold) {
    if (reduced) return;
    pMat.color.setHex(gold ? 0xf0c24b : 0xff8fc8);
    for (let i = 0; i < P_COUNT; i++) {
      /* out of the CRATER, not off a plate: they start at the belly */
      pPos[i * 3] = 0; pPos[i * 3 + 1] = -1.6; pPos[i * 3 + 2] = 0;
      const a = Math.random() * Math.PI * 2;
      const up = 2.1 + Math.random() * 2.8;
      const out = 0.6 + Math.random() * 2.2;
      pVel[i * 3] = Math.cos(a) * out;
      pVel[i * 3 + 1] = up;
      pVel[i * 3 + 2] = Math.sin(a) * out;
    }
    pGeo.attributes.position.needsUpdate = true;
    pLife = 0;
  }

  /* ------------------------------------------------------------- lighting */
  const ambient = new THREE.AmbientLight(0x9a90c8, 0.62);
  scene.add(ambient);
  const key = new THREE.DirectionalLight(0xb8a6e8, 0.85);
  key.position.set(-4, 9, 3);
  scene.add(key);
  const basinLight = new THREE.PointLight(0xff69b4, 0.5, 6, 2);
  basinLight.position.set(0, -0.15, 0);
  scene.add(basinLight);

  /* ------------------------------------------------------------ transient */
  const pulses = [];    // {mesh, t, dur, grow}
  function ringPulse(colorHex, dur) {
    /* born ON the lip and washing OUTWARD across the coils - depthTest stays on,
       so the chute occludes it where it crosses and the ripple reads as being IN
       the scene. It must be THIN and start at the rim: a fat ring starting mid
       bowl just paints a plate over the crater and flattens it. */
    const m = new THREE.Mesh(
      new THREE.RingGeometry(R_IN * 0.93, R_IN * 1.0, 64),
      new THREE.MeshBasicMaterial({ color: colorHex, transparent: true, opacity: 0.8, side: THREE.DoubleSide, depthWrite: false })
    );
    m.rotation.x = -Math.PI / 2;
    m.position.y = Y_RIM + RIM_R * 0.9;
    scene.add(m);
    pulses.push({ mesh: m, t: 0, dur: reduced ? 1 : dur, grow: 2.4 });
  }

  /* --------------------------------------------------------------- state */
  let travel = null;      // 0..1 | null
  let rimBoost = 0;       // decays in the loop
  let glowBoost = 0;
  let ambientSwell = 0;   // denyPass exhale
  let suspended = false;
  let dead = false;
  let clock = 0;

  /* the living-material state */
  let spinVel = reduced ? 0 : SPIN_BASE;
  let spinKick = 0;       // denyHit impulse, decays
  let flowSpeed = reduced ? 0 : FLOW_BASE;
  let flowDir = 1;        // denyHit reverses briefly
  let flowRevT = 0;
  let mood = { progress: 0, streak: 0 };
  /* the seed's two colour poles; gold and red stay semantic and fixed */
  const EMISSIVE_A = new THREE.Color().setHSL((ID.hue / 360) % 1, 0.92, 0.67);
  const EMISSIVE_B = new THREE.Color().setHSL((ID.hue2 / 360) % 1, 0.62, 0.72);
  const EMISSIVE_GOLD = new THREE.Color(0xf0c24b);
  const EMISSIVE_RED = new THREE.Color(0xff3a5e);
  const emissiveTarget = EMISSIVE_A.clone();
  const DISH_TINT = new THREE.Color().setHSL((ID.hue / 360) % 1, 0.45, 0.82);
  const DISH_LAV = new THREE.Color(0xcabdf0);
  const DISH_WARM = new THREE.Color(0xf0d9a8);
  const WHITE = new THREE.Color(0xffffff);
  const scratchColor = new THREE.Color();
  let redFlash = 0;       // denyHit band flash, decays
  let surge = 0;          // pop(true) swallow-gulp, decays
  let scramT = 0;         // denyHit pattern scramble, counts down
  let dishExhale = 0;     // denyPass crater tint, decays
  let progSmooth = 0;     // eased class progress - the morph glides, never steps
  let kbY = 0;            // accumulated ken-burns bore-roll
  let lastMorphAt = -1;   // redraw throttle clock stamp
  const MORPH_DT = perf ? 0.25 : 0.125;
  /* the key light's orbit basis (its authored position, in polar form) */
  const KEY_R = Math.hypot(-4, 3);
  const KEY_A0 = Math.atan2(3, -4);

  /* seed the material's own tint immediately - applyMood refines it later */
  tubeMat.emissive.copy(EMISSIVE_A);

  function applyMood(m) {
    mood = { progress: Math.max(0, Math.min(1, Number(m && m.progress) || 0)),
             streak: Math.max(0, Math.round(Number(m && m.streak) || 0)) };
    if (!reduced) {
      spinVel = (SPIN_BASE + (SPIN_MAX - SPIN_BASE) * mood.progress) * ID.spinMul;
      flowSpeed = (FLOW_BASE + (FLOW_MAX - FLOW_BASE) * mood.progress) * ID.flowMul;
    }
    // a hot streak turns the veins gold; otherwise the seed's own two poles
    // trade places across the class (A opens, B closes)
    emissiveTarget.copy(
      mood.streak >= 5 ? EMISSIVE_GOLD
        : scratchColor.copy(EMISSIVE_A).lerp(EMISSIVE_B, mood.progress)
    );
  }

  function place(p) {
    const pos = helixAt(Math.max(0, Math.min(1, p)));
    glow.position.copy(pos);
    halo.position.copy(pos);
    if (ghost) ghost.position.copy(pos);
  }

  function frame(dt) {
    clock += dt;

    /* the spin - slow, hypnotic, seeded in direction, jolted by a denyHit */
    spinKick *= Math.pow(0.06, dt);            // sharp decay
    world.rotation.y += (spinVel + spinKick) * dt * ID.spinSign;

    /* the band march - the swallow; a pop's gulp surges it, a denyHit reverses
       it. surge multiplies rather than adds so tier-4 gulps stay proportional */
    if (flowRevT > 0) { flowRevT -= dt; if (flowRevT <= 0) flowDir = 1; }
    surge *= Math.pow(0.05, dt);
    if (flowSpeed > 0) bandTex.offset.x -= flowSpeed * (1 + surge * 1.6) * flowDir * dt;

    /* ken-burns: the pattern rolls slowly around the bore while its pitch
       breathes - uniform transforms only, no redraw. A denyHit scrambles the
       roll for half a second (transient - Math.random is fine here). */
    if (!reduced) {
      kbY += ID.kbV * dt;
      scramT = Math.max(0, scramT - dt);
      bandTex.offset.y = kbY + (scramT > 0 ? scramT * 0.4 * (Math.random() - 0.5) : 0);
      bandTex.repeat.x = ID.pitch * (1 + 0.035 * Math.sin(clock * 0.4));
    }

    /* the morph: glide progSmooth toward the class's real progress and redraw
       the band only while the journey params are actually moving */
    if (!reduced) {
      progSmooth += (mood.progress - progSmooth) * Math.min(1, dt * 0.8);
      if (clock - lastMorphAt >= MORPH_DT) {
        const P = paramsAt(progSmooth);
        let moved = 0;
        for (const f of P_KEYS) moved += Math.abs(P[f] - (P_LAST[f] || 0));
        if (moved > 0.6 || P_LAST._never) { drawMorph(P); lastMorphAt = clock; }
      }
    }

    /* band color: chase the mood target, punch red on a denyHit; the gulp
       flares the veins too */
    redFlash *= Math.pow(0.02, dt);
    tubeMat.emissive.copy(emissiveTarget).lerp(EMISSIVE_RED, Math.min(1, redFlash));
    const breathe = reduced ? 0 : Math.sin(clock * 0.9) * 0.09;
    tubeMat.emissiveIntensity = 0.42 + breathe + redFlash * 0.55 + surge * 0.14;

    /* the key light crawls AGAINST the spin, so a specular sheen forever
       migrates across the coils - the cheapest living-light in the scene */
    if (!reduced) {
      const ka = KEY_A0 - clock * 0.045 * ID.spinSign;
      key.position.set(Math.cos(ka) * KEY_R, 9, Math.sin(ka) * KEY_R);
    }

    /* the comet patrol - dim while a real load travels */
    if (!reduced) {
      cometT += dt / ID.cometPeriod;
      if (cometT >= 1) cometT -= 1;
      comet.position.copy(helixAt(cometT));
      cometLevel += (((travel == null) ? 0.9 : 0.22) - cometLevel) * Math.min(1, dt * 3);
      comet.intensity = cometLevel * (1 + 0.15 * Math.sin(clock * 3));
    }

    /* the crater breathes: seed tint at rest, lavender on the exhale, warm on
       a hot streak - all through the unlit dish's free colour multiplier, so
       the hole-not-dome ramp itself is never touched */
    dishExhale *= Math.pow(0.15, dt);
    const dBreath = reduced ? 0 : 0.05 * Math.sin(clock * 0.5);
    const dAmt = Math.min(0.5, 0.12 + dBreath + dishExhale * 0.3 + (mood.streak >= 5 ? 0.15 : 0));
    scratchColor.copy(mood.streak >= 5 ? DISH_WARM : DISH_TINT).lerp(DISH_LAV, Math.min(1, dishExhale));
    dishMat.color.copy(WHITE).lerp(scratchColor, dAmt);

    /* the pool glow in the belly: near-invisible idle, blooms on the reveal.
       It keeps the seed's own colour - washing it toward white turns the
       crater milky and costs the hole its darkness */
    pool.material.opacity = Math.min(0.4, 0.02 + (reduced ? 0 : 0.012 * Math.sin(clock * 1.1)) + rimBoost * 0.16 + dishExhale * 0.07);
    pool.material.color.copy(emissiveTarget);

    /* the sealed load */
    if (travel == null) {
      glow.intensity += (0 - glow.intensity) * 0.25;
      halo.material.opacity += (0 - halo.material.opacity) * 0.25;
      if (ghost) ghost.material.opacity += (0 - ghost.material.opacity) * 0.25;
    } else {
      place(travel);
      const shimmer = reduced ? 0 : Math.sin(clock * 9) * 0.22;
      glow.intensity += ((3.2 + glowBoost + shimmer) - glow.intensity) * 0.35;
      halo.material.opacity += ((0.5 + shimmer * 0.25) - halo.material.opacity) * 0.3;
      if (ghost) ghost.material.opacity += (0.15 - ghost.material.opacity) * 0.3;
    }

    /* the coil runner */
    if (runT >= 0) {
      runT -= dt / 0.45;
      if (runT <= 0) { runT = -1; runner.intensity = 0; }
      else {
        const pos = helixAt(runT);
        runner.position.copy(pos);
        runner.intensity = 2.6 * runT;
      }
    }

    /* particles */
    if (pLife >= 0) {
      pLife += dt;
      const k = Math.min(1, pLife / P_DUR);
      for (let i = 0; i < P_COUNT; i++) {
        pPos[i * 3] += pVel[i * 3] * dt;
        pPos[i * 3 + 1] += pVel[i * 3 + 1] * dt;
        pPos[i * 3 + 2] += pVel[i * 3 + 2] * dt;
        pVel[i * 3 + 1] -= 3.4 * dt;           // soft gravity
      }
      pGeo.attributes.position.needsUpdate = true;
      pMat.opacity = 0.9 * (1 - k);
      if (k >= 1) { pLife = -1; pMat.opacity = 0; }
    }

    rimBoost *= 0.9; glowBoost *= 0.88; ambientSwell *= Math.pow(0.1, dt);
    /* base stays LOW: the collar is unmapped, so every point of it glows at
       once - the 0.32 the old thin torus wore turns this one into a flat lamp */
    rimMat.emissiveIntensity = 0.05 + rimBoost * 0.7;
    ambient.intensity = 0.62 + ambientSwell;
    if (!reduced) {
      basinLight.intensity = 0.5 + Math.sin(clock * 1.6) * 0.08;
    }
    for (let i = pulses.length - 1; i >= 0; i--) {
      const p = pulses[i];
      p.t += dt;
      const k = Math.min(1, p.t / p.dur);
      const s = 1 + p.grow * k;
      p.mesh.scale.set(s, s, 1);
      p.mesh.material.opacity = 0.85 * (1 - k);
      if (k >= 1) { scene.remove(p.mesh); p.mesh.geometry.dispose(); p.mesh.material.dispose(); pulses.splice(i, 1); }
    }
    renderer.render(scene, camera);
  }

  /* rAF resolved at call time - absent (harness) means "render on demand". */
  let rafId = 0;
  let last = 0;
  function loop(ts) {
    if (dead || suspended) return;
    const dt = last ? Math.min(0.05, (ts - last) / 1000) : 0.016;
    last = ts;
    frame(dt);
    const raf = (typeof requestAnimationFrame === 'function') ? requestAnimationFrame : null;
    if (raf) rafId = raf(loop);
  }
  function kick() {
    const raf = (typeof requestAnimationFrame === 'function') ? requestAnimationFrame : null;
    last = 0;
    if (raf) rafId = raf(loop); else frame(0.016);
  }

  function resize() {
    try {
      const w = mount.clientWidth || 800, h = mount.clientHeight || 600;
      renderer.setSize(w, h, false);
      camera.aspect = w / h;
      camera.updateProjectionMatrix();
      if (suspended || dead) return;
    } catch (e) { /* noop */ }
  }
  resize();
  kick();

  return {
    kind: '3d',
    setTravel(p) { travel = (p == null ? null : Math.max(0, Math.min(1, Number(p) || 0))); },
    loadPulse() { glowBoost = 1.4; },
    reveal() { rimBoost = 1.6; travel = null; },
    pop(good) {
      ringPulse(good ? 0xff69b4 : 0xf0c24b, 0.55);
      rimBoost = good ? 1.2 : 0.6;
      if (good) {
        burstParticles(mood.streak >= 5);
        if (!reduced) { runT = 1; surge = 1; }   // the swallow gulps
      }
    },
    denyHit() {
      ringPulse(0xff3a5e, 0.5);
      rimBoost = 0.8;
      redFlash = 1;
      if (!reduced) {
        spinKick = 1.7;
        flowDir = -1; flowRevT = 0.7;
        scramT = 0.5;                            // the pattern loses its footing
      }
    },
    denyPass() { ringPulse(0xb8a6e8, 0.8); ambientSwell = 0.35; dishExhale = 1; },
    setMood(m) { try { applyMood(m); } catch (e) { /* cosmetic */ } },
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
      try {
        tubeGeo.dispose(); tubeMat.dispose();
        dish.geometry.dispose(); dishMat.dispose(); dishTex.dispose();
        rim.geometry.dispose(); rimMat.dispose();
        bore.geometry.dispose(); boreMat.dispose(); boreTex.dispose();
        mouthWall.geometry.dispose(); noseMat.dispose();
        mouthLip.geometry.dispose(); mouthLipMat.dispose();
        lip.geometry.dispose(); lipMat.dispose();
        bandTex.dispose(); haloTex.dispose();
        pool.material.dispose(); poolTex.dispose();
        pGeo.dispose(); pMat.dispose();
        renderer.dispose();
        if (canvas.parentNode) canvas.parentNode.removeChild(canvas);
      } catch (e) { /* noop */ }
    },
  };
}
