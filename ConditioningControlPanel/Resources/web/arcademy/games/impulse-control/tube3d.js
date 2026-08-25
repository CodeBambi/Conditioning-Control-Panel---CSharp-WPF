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
 * through render.js) births a TUBE IDENTITY: a hue pair, a spin direction, a
 * band pitch, a flow temperament, a PALETTE and a PATTERN JOURNEY. Same seed,
 * same tube - a retake replays its class in the same skin; a new class grows a
 * new one. All of it rides makeRng, never Math.random, except throwaway
 * transients (particle scatter, glitch jitter).
 *
 * THE PATTERN IS THE LIGHT (the House Rules pass). The chute BODY stays dark
 * opaque resin so the load's glow and the crater keep their contrast; what is
 * bright is the decoration marching down it - and it is bright: saturated neon
 * strokes with white-hot cores, painted in COLOUR onto the emissive canvas (the
 * material's emissive tint is white, so the canvas IS the colour; gold and red
 * stay semantic tints laid over it on a hot streak / a denyHit). The palette
 * is seeded from a pool of bold triads and quads (magenta+cyan+lime,
 * gold+violet+white, rose+teal+amber, ...) with a ~28% chance of the classic
 * violet->rose pair made bright; the lines then HUE-CYCLE in the shader (one
 * uniform, a gray-axis rotation on the emissive sample - no redraw): bold
 * palettes spin the whole wheel, the classic pair only SWAYS about its arc so
 * it stays on-brand. The cycle quickens with the streak (setMood).
 *
 * THREE FAMILIES, ONE JOURNEY. Every decoration is a point in ONE of three
 * parameter spaces, and within a family the wall MORPHS (glide, never switch):
 *   VEIN   columns of light: rings, chevron, lace, waves, eyes, runes, candy
 *          (seamless diagonal stripes), zigzag - bend/wave/zig/slant/rails/
 *          beads/eye/ticks, drawn as polylines.
 *   FIELD  a dotted wall: polka, bubbles (hollow), sparkle (four-point stars) -
 *          dotR/hollow/star/rows/cols/stagger/jitter/sizeVar. Tileable by
 *          construction (integer rows and columns; dots near an edge are
 *          painted again across it).
 *   GRID   a lattice: fishnet, diamond (knotted), checker (harlequin cells) -
 *          two diagonal line sets whose pitch and slope are integers of the
 *          tile (nx columns, m columns per wrap) so the lattice closes around
 *          the bore with no seam; lineW/knotR/fill morph.
 * Across families the wall CROSSFADES (~2-3s, additive, seeded length) between
 * two cached family canvases - never a cut. The journey is 4-6 stops drawn
 * from all 14 archetypes (at least one FIELD or GRID stop is guaranteed), the
 * class's progress walks it (stop k arrives at progress (k-0.5)/(n-1)), and the
 * streak PUSHES it: every milestone (5/10/15/20 links) advances the journey
 * 0.6 of a stop early, a broken streak (4+ down to 0) pulls it 0.6 back toward
 * the calmer stop it came from. The band canvas is repainted at <=8Hz ONLY
 * while a glide or a crossfade is actually moving; a held stop costs nothing.
 *
 * THE LIVING MATERIAL: the whole rig sits in one rotating group (slow hypnotic
 * spin - this is a hypno app, the spiral turning IS the aesthetic) and the
 * chute wears one emissive canvas (512x256, 256x128 under perf; painted in
 * 256x128 pattern units) that marches toward the basin like a slow swallow. On top of the march the texture ken-burns: the pattern rolls
 * slowly around the bore (offset.y) while its pitch breathes (repeat.x) - the
 * wall never holds still, and none of it costs a redraw. A comet light patrols
 * mouth->basin between loads (dimming while a real load travels - the load is
 * the star), the key light orbits against the spin so a specular sheen forever
 * crawls the coils, and the crater breathes: dishMat.color (unlit, so it cannot
 * break the hole-not-dome ramp) tints with the palette's lead colour, exhales
 * lavender on a denyPass, and warms when a streak runs hot, while a faint
 * pooled glow sprite (palette lead, hue-cycled with the lines) blooms in the
 * belly at each reveal.
 *
 * CONTRACT WITH render.js (all methods must never throw):
 *   setTravel(p|null) · loadPulse() · reveal() · pop(good) · denyHit()
 *   denyPass() · setMood({progress,streak}) · suspend(on) · resize() · landing()
 *   opts.onLanding({x,y}) fires once at build with the reveal's anchor in % (THE LANDING)
 *   destroy() · kind:'3d'
 *
 * BEATS (the tube participates in every verdict):
 *   pop(true)  basin ring + particle burst + a light racing UP the coil +
 *              the swallow GULPS (band march surges, lines flare)
 *   denyHit()  spin jolt + band tinted red + flow REVERSES + the pattern
 *              scrambles around the bore for half a second
 *   denyPass() lavender ring + ambient swell + the crater exhales lavender
 *
 * RESILIENCE: three.js is imported DYNAMICALLY and the WebGL context probed in
 * try/catch - any throw/reject means "use the 2D tube". Under the DOM double
 * this module is never even imported.
 *
 * MOTION: reduced -> no spin, no march, no morphing or crossfading (the
 * journey's first stop is drawn once and held), no hue cycle, no ken-burns, no
 * comet, no orbit, no particles; pulses are opacity steps. perf -> half the
 * particles, band redraw throttled to 4Hz (resolution is already floored for
 * everyone by the arcade-cabinet pass below).
 *
 * TRAPS (each one cost real time): widening the crater's lit lip turns it into
 * a lit egg; nothing may run near-parallel to the chute (z-fight sawtooth);
 * the dish is MeshBasicMaterial because lit-by-scene renders as a dome; the
 * headless rig renders on SwiftShader (slow, faithful). The band texture is
 * sRGB now that it carries colour - a linear-tagged colour canvas washes the
 * mid tones out.
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
const R_OUT = 14.2;          // helix radius at the mouth - OFF-FRAME, see above
const R_IN = 2.5;            // the held radius === the basin's rim circle
const H_TOP = 4.4;           // mouth height above the basin plane
const Y_END = 1.06;          // height of the held ring: TUBE_R + 0.06, so the
                             // chute's belly clears the collar's crest (0.0)
const TUBE_R = 1.00;         // chute body radius (opaque) - bore dia 2.0. Was
                             // 0.80 until the reveal grew x1.25 (style.js
                             // --ic-basin-d clamp(132px,17.5vmin,238px)); the
                             // bubble must FIT THE BORE, so the two move
                             // together (R_OUT, Y_END, MOUTH_Y/R followed)
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
const MOUTH_R = 1.95;        // the opening's centre: just inside the rim, and
                             // never over the middle - its innermost edge still
                             // stands ~0.95 off the axis (was 1.75 / ~1.2 with
                             // the 0.8 bore), so the composition law above
                             // (nothing passes over the centre) holds
const MOUTH_Y = 0.74;        // how high the opening rides over the bowl (0.54 +
                             // the 0.2 the bore grew, same margins). The dip
                             // is FLAT for the first half of the peel on purpose:
                             // the peel is still crossing OVER the collar and the
                             // lit lip there, and its belly clears them by 0.066
                             // and 0.05. Drop MOUTH_Y and the drop starts sooner,
                             // and that margin is the first thing it eats
const AIM_Y = -0.15;         // the height of the point it is aimed AT, on the
                             // axis: about the rim plane, i.e. the middle of the
                             // crater's MOUTH - the centre of the platform the
                             // bubble lands on (-0.35 with the lower 0.54 mouth;
                             // raised with it so the tilt stays ~25deg). Aiming further down (the
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

/* THE PATTERN SPACE. Three families; inside a family every archetype is a point
   in one parameter space, so any two of them lerp and the wall reads as
   transforming; across families the wall crossfades. Strokes are painted in
   PALETTE COLOUR with white-hot cores - the canvas is the colour, the material
   only tints it (white at rest, gold on a hot streak, red on a denyHit). */
const ARCHETYPES = {
  /* VEIN - columns of light. bend folds a ring into a chevron, beadR grows lace
     out of rails, eyeA opens an iris mid-column, slant winds the column into a
     seamless diagonal stripe (1 = exactly one column per wrap, so the helix
     closes on itself), zig trades the sine sway for a triangle. */
  rings:   { fam: 'vein', bend: 0,   wave: 0,  freq: 1.0, railA: 0.55, railGap: 26, beadR: 0,   eyeA: 0,   ticks: 0,   coreW: 6.0, slant: 0,   zig: 0 },
  chevron: { fam: 'vein', bend: -24, wave: 0,  freq: 1.0, railA: 0,    railGap: 26, beadR: 0,   eyeA: 0,   ticks: 0,   coreW: 6.0, slant: 0,   zig: 0 },
  lace:    { fam: 'vein', bend: 0,   wave: 0,  freq: 1.0, railA: 0.9,  railGap: 42, beadR: 6,   eyeA: 0,   ticks: 0,   coreW: 4.2, slant: 0,   zig: 0 },
  waves:   { fam: 'vein', bend: 0,   wave: 15, freq: 1.6, railA: 0.5,  railGap: 18, beadR: 0,   eyeA: 0,   ticks: 0,   coreW: 5.0, slant: 0,   zig: 0 },
  eyes:    { fam: 'vein', bend: 0,   wave: 5,  freq: 0.8, railA: 0,    railGap: 26, beadR: 0,   eyeA: 0.9, ticks: 0,   coreW: 4.2, slant: 0,   zig: 0 },
  runes:   { fam: 'vein', bend: -8,  wave: 0,  freq: 1.0, railA: 0,    railGap: 26, beadR: 3.5, eyeA: 0,   ticks: 0.8, coreW: 5.2, slant: 0,   zig: 0 },
  candy:   { fam: 'vein', bend: 0,   wave: 0,  freq: 1.0, railA: 0.75, railGap: 24, beadR: 0,   eyeA: 0,   ticks: 0,   coreW: 9.0, slant: 1,   zig: 0 },
  zigzag:  { fam: 'vein', bend: 0,   wave: 13, freq: 2.0, railA: 0.6,  railGap: 22, beadR: 0,   eyeA: 0,   ticks: 0,   coreW: 4.6, slant: 0,   zig: 1 },
  /* FIELD - a dotted wall. hollow turns a polka dot into a bubble ring, star
     turns it into a four-point sparkle; rows/cols stay integers (tileable). */
  polka:   { fam: 'field', dotR: 11,  hollow: 0, star: 0, rows: 4, cols: 5, stag: 0.5, jit: 0,   sizeVar: 0 },
  bubbles: { fam: 'field', dotR: 10,  hollow: 1, star: 0, rows: 4, cols: 5, stag: 0.5, jit: 0.7, sizeVar: 0.6 },
  sparkle: { fam: 'field', dotR: 7,   hollow: 0, star: 1, rows: 4, cols: 6, stag: 0.5, jit: 0.9, sizeVar: 0.7 },
  /* GRID - a lattice. The slope and pitch are the SEED's (gridNx columns per
     tile, gridM columns crossed per wrap - integers, so the net closes around
     the bore); the archetypes only differ in weight, knots and cell fill. */
  fishnet: { fam: 'grid', lineW: 2.0, knotR: 0,   fill: 0 },
  diamond: { fam: 'grid', lineW: 3.4, knotR: 3.8, fill: 0 },
  checker: { fam: 'grid', lineW: 1.2, knotR: 0,   fill: 1 },
};
const FAM_KEYS = {
  vein:  ['bend', 'wave', 'freq', 'railA', 'railGap', 'beadR', 'eyeA', 'ticks', 'coreW', 'slant', 'zig'],
  field: ['dotR', 'hollow', 'star', 'rows', 'cols', 'stag', 'jit', 'sizeVar'],
  grid:  ['lineW', 'knotR', 'fill'],
};

/* THE PALETTES - [hue, sat, light] triads and quads, all bold. Index 0 is the
   lead (the crater tint, the pool glow, the basin light). 'arc' is built per
   seed from the classic violet->rose pair (made bright) plus a white-hot. */
const PALETTES = [
  { name: 'neon',   cols: [[312, 1, 0.58], [186, 1, 0.56], [84, 1, 0.58]] },               // magenta cyan lime
  { name: 'royal',  cols: [[46, 1, 0.60], [268, 1, 0.66], [0, 0, 0.96]] },                 // gold violet white
  { name: 'sunset', cols: [[338, 1, 0.62], [168, 0.9, 0.56], [36, 1, 0.60]] },             // rose teal amber
  { name: 'candy',  cols: [[326, 1, 0.60], [222, 1, 0.62], [0, 0, 0.95]] },                // hot pink electric blue white
  { name: 'acid',   cols: [[76, 1, 0.55], [276, 1, 0.62], [336, 1, 0.58]] },               // lime violet hot pink
  { name: 'ice',    cols: [[188, 1, 0.66], [262, 1, 0.80], [0, 0, 0.97]] },                // cyan lavender white
  { name: 'ember',  cols: [[22, 1, 0.58], [46, 1, 0.60], [312, 1, 0.58]] },                // orange gold magenta
  { name: 'ultra',  cols: [[282, 1, 0.62], [150, 1, 0.55], [52, 1, 0.62], [196, 1, 0.60]] }, // violet mint gold sky
];
const ARC_CHANCE = 0.28;     // share of seeds that keep the classic pair (bright)

export async function createTube3D(opts = {}) {
  const mount = opts.mount;
  if (!mount || typeof document === 'undefined') throw new Error('no mount');
  const reduced = !!opts.reduced;
  const perf = !!opts.perf;

  /* ------------------------------------------------- the tube's identity
     One rng stream, drawn in a FIXED order (append-only, or every shipped
     tube changes skin). Draws 1..N are the Semester-1 identity and are kept
     in place (hue pair, spin, pitch, temperament, comet, columns, phases, and
     the legacy three-stop journey - STILL DRAWN so the stream stays aligned,
     but no longer the journey that ships). Everything the House Rules pass
     added is drawn AFTER them: palette, cycle, the 4-6 stop journey, the
     transition lengths, the lattice integers. */
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
    const cols = rng() < 0.5 ? 3 : 4;             // vein columns per 256px wrap
    const colOff = rng() * 64;
    const phases = [rng() * 6.28, rng() * 6.28, rng() * 6.28, rng() * 6.28];
    /* LEGACY journey draws (Semester 1): consumed so every draw after them
       lands where it always did. Not used for the shipped journey. */
    const legacyPool = ['chevron', 'eyes', 'lace', 'rings', 'runes', 'waves'];
    for (let i = legacyPool.length - 1; i > 0; i--) {
      const j = Math.min(i, Math.floor(rng() * (i + 1)));
      const sw = legacyPool[i]; legacyPool[i] = legacyPool[j]; legacyPool[j] = sw;
    }
    for (let s = 0; s < 3; s++) {
      rng(); rng(); rng(); rng();
      if (ARCHETYPES[legacyPool[s]].beadR > 0) rng();
    }

    /* ---- APPENDED (House Rules pass) - every draw below is new ---- */
    /* the palette: classic pair made bright, or one of the bold sets */
    let palette, palName, cycleMode;
    if (rng() < ARC_CHANCE) {
      palette = [[hue, 1, 0.66], [hue2, 1, 0.70], [0, 0, 0.96]];
      palName = 'arc'; cycleMode = 'sway';
    } else {
      const p = PALETTES[Math.min(PALETTES.length - 1, Math.floor(rng() * PALETTES.length))];
      palette = p.cols.map((c) => c.slice());
      palName = p.name; cycleMode = 'spin';
    }
    /* rotate which colour leads (the lead = crater / pool / basin light) */
    const rot = Math.floor(rng() * palette.length) % palette.length;
    palette = palette.slice(rot).concat(palette.slice(0, rot));
    const cycleBase = 6.2832 / (45 + rng() * 40);  // rad/s: one wheel in 45-85s
    const cycleSign = rng() < 0.5 ? -1 : 1;

    /* the journey: 4-6 DISTINCT stops over all 14 archetypes, at least one of
       them a FIELD or GRID stop (the owner asked for fishnet and polka dots,
       so a class must be able to show one), each stop's genes jittered once */
    const nStops = 4 + Math.floor(rng() * 3);
    const pool = Object.keys(ARCHETYPES).sort();
    for (let i = pool.length - 1; i > 0; i--) {
      const j = Math.min(i, Math.floor(rng() * (i + 1)));
      const sw = pool[i]; pool[i] = pool[j]; pool[j] = sw;
    }
    const picked = pool.slice(0, nStops);
    if (!picked.some((k) => ARCHETYPES[k].fam !== 'vein')) {
      const alt = pool.slice(nStops).find((k) => ARCHETYPES[k].fam !== 'vein');
      if (alt) picked[nStops - 1] = alt;
    }
    const stops = picked.map((k) => {
      const base = ARCHETYPES[k];
      const out = { key: k, fam: base.fam };
      for (const f of FAM_KEYS[base.fam]) out[f] = base[f];
      /* five jitter draws per stop, always five, whatever the family */
      const j1 = rng(), j2 = rng(), j3 = rng(), j4 = rng(), j5 = rng();
      if (base.fam === 'vein') {
        out.bend *= 0.8 + j1 * 0.5;
        out.wave *= 0.8 + j2 * 0.5;
        out.freq = out.slant > 0 ? out.freq : out.freq * (0.85 + j3 * 0.4);
        out.coreW = Math.max(3.4, out.coreW * (0.85 + j4 * 0.4));
        if (out.beadR > 0) out.beadR *= 0.8 + j5 * 0.5;
      } else if (base.fam === 'field') {
        out.dotR *= 0.85 + j1 * 0.4;
        out.jit = Math.min(1, out.jit + (j2 - 0.5) * 0.3);
        out.sizeVar = Math.min(1, Math.max(0, out.sizeVar + (j3 - 0.5) * 0.3));
        out.stag = j4 < 0.5 ? 0.5 : 0.33;
      } else {
        out.lineW *= 0.85 + j1 * 0.4;
        if (out.knotR > 0) out.knotR *= 0.85 + j2 * 0.4;
        out.fill = out.fill > 0 ? Math.min(1, out.fill * (0.8 + j3 * 0.25)) : 0;
      }
      return out;
    });
    const morphT = 4.5 + rng() * 2.5;             // s, a glide within a family
    const xfadeT = 1.8 + rng() * 1.0;             // s, a crossfade across families
    const gridNx = 8 + 2 * Math.floor(rng() * 3); // 8 / 10 / 12 columns per tile
    const gridM = 3 + Math.floor(rng() * 3);      // 3 / 4 / 5 columns per wrap
    return { hue, hue2, spinSign, pitch, flowMul, spinMul, kbV, cometPeriod, cols, colOff, phases,
             palette, palName, cycleMode, cycleBase, cycleSign, stops, morphT, xfadeT, gridNx, gridM };
  })();

  const THREE = await import(VENDOR);

  const canvas = document.createElement('canvas');
  canvas.className = 'g-ic-tube-canvas';
  /* THE PICTURE IS CHUNKY ON PURPOSE (the arcade-cabinet pass, owner
     2026-08-24): the tube renders at 1/PIXEL of the viewport and the
     stylesheet's image-rendering:pixelated does a nearest-neighbour upscale,
     so every band, glow and particle lands as a fat screen texel. AA stays
     OFF - it would soften the texels before the upscale and cost the look its
     edges - and the whole thing is cheaper than the old 2x-dpr render. The
     DOM bubble stays crisp on top, a sprite over the cabinet screen. */
  const PIXEL = 3;                        // CSS px per rendered texel
  const renderer = new THREE.WebGLRenderer({
    canvas, alpha: true, antialias: false, powerPreference: 'low-power',
  });
  // Some webviews hand back a dead context without throwing; probe it.
  const gl = renderer.getContext();
  if (!gl || (typeof gl.isContextLost === 'function' && gl.isContextLost())) {
    throw new Error('webgl context unavailable');
  }
  renderer.setPixelRatio(1 / PIXEL);
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

  /* ------------------------------------------------------------ THE LANDING
     Where the DOM reveal sits. Screen centre is the crater's belly by the
     lookAt above - but this camera looks DOWN 59deg over a chute that rings
     the crater ABOVE its rim, so the held ring's NEAR side hides the lower
     half of the bowl: the belly projects at ~49.5% of the viewport and the
     near coil's inner crest at ~49.0%. A bubble centred there reads as
     sitting ON THE TUBE (owner 2026-08-22), not in the dish. The hole the
     player actually SEES runs from the far coil's inner belly down to the
     near coil's inner crest, and the landing is the axis point that projects
     into the middle of it - solved, not eyeballed, so a camera tweak moves it
     for free. NDC y is aspect-blind, so one solve at build time is enough.
     The DOM basin follows via opts.onLanding({x,y} in %); the pool glow and
     the pop particles land on the same point so the 2D and 3D halves agree. */
  const LAND = (() => {
    const fallback = { y: -0.9, pct: { x: 50, y: 50 } };
    try {
      /* project() reads matrixWorldInverse: a camera that has never rendered
         still carries the identity, so update it by hand first */
      camera.updateMatrixWorld(true);
      camera.updateProjectionMatrix();
      const v = new THREE.Vector3();
      const ndcY = (x, y, z) => v.set(x, y, z).project(camera).y;
      const farY = ndcY(0, Y_END - TUBE_R, -(R_IN - TUBE_R));   // far coil, inner belly
      const nearY = ndcY(0, Y_END + TUBE_R, R_IN - TUBE_R);     // near coil, inner crest
      const want = (farY + nearY) / 2;
      let lo = -3, hi = 5;
      for (let i = 0; i < 40; i++) { const mid = (lo + hi) / 2; if (ndcY(0, mid, 0) < want) lo = mid; else hi = mid; }
      const y = (lo + hi) / 2;
      const pctY = (1 - want) / 2 * 100;
      if (!Number.isFinite(y) || !Number.isFinite(pctY) || pctY < 15 || pctY > 85) return fallback;
      return { y, pct: { x: 50, y: +pctY.toFixed(2) } };
    } catch (e) { return fallback; }
  })();
  if (typeof opts.onLanding === 'function') { try { opts.onLanding({ x: LAND.pct.x, y: LAND.pct.y }); } catch (e) { /* cosmetic */ } }

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
    // same (11.7 / 3.87 turns = 3.02, against a bore of 2.0 - they can never
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

  /* The band canvas: ONE surface the material samples, redrawn in place (no
     re-alloc). TubeGeometry maps u ALONG the path, so vertical
     elements here become rings that march down the chute when offset.x moves.
     Two more canvases of the same size sit behind it as the CROSSFADE cache: each holds
     one family's render of a journey stop, painted once per stop, and a
     crossfade is two drawImages into the band - so at most ONE GPU texture and
     three small canvases are ever alive for the pattern. */
  const BAND_S = perf ? 1 : 2;               // canvas pixels per pattern unit
  const BAND_W = 256 * BAND_S, BAND_H = 128 * BAND_S;
  const bandCanvas = document.createElement('canvas');
  bandCanvas.width = BAND_W; bandCanvas.height = BAND_H;
  const bandTex = new THREE.CanvasTexture(bandCanvas);
  bandTex.wrapS = THREE.RepeatWrapping;
  bandTex.wrapT = THREE.RepeatWrapping;
  bandTex.repeat.set(ID.pitch, 1);
  try { bandTex.colorSpace = THREE.SRGBColorSpace; } catch (e) { /* older three */ }
  const cacheCanvas = [document.createElement('canvas'), document.createElement('canvas')];
  const cacheIdx = [-1, -1];
  for (const cv of cacheCanvas) { cv.width = BAND_W; cv.height = BAND_H; }

  /* ------------------------------------------------------ the palette ink */
  const PAL = ID.palette;
  const PAL_N = PAL.length;
  const palCss = PAL.map((c) => 'hsl(' + Math.round(c[0]) + ',' + Math.round(c[1] * 100) + '%,' + Math.round(c[2] * 100) + '%)');
  const WHITE_HOT = 'rgba(255,255,255,0.92)';
  const ink = (k) => palCss[((k % PAL_N) + PAL_N) % PAL_N];

  /* ------------------------------------------------------ the morph engine
     The journey is walked by jPos (a real number over the stop indices). The
     pair (i, i+1) around it and the fraction w are what gets drawn: same
     family -> params lerp (glide), different family -> crossfade between the
     two cached family canvases. renderBand is the ONLY painter, and the frame
     loop calls it only while jPos is moving (and never faster than MORPH_DT).
     Every family painter paints a TILEABLE 256x128: x wraps along the chute,
     y wraps around the bore. */
  const P_CUR = {};
  const smooth = (u) => (u <= 0 ? 0 : u >= 1 ? 1 : u * u * (3 - 2 * u));
  const triAt = (y) => Math.max(0, 1 - Math.abs((y - 64) / 72));
  /* a cheap deterministic hash for per-dot variation (NOT the rng: the tile
     has to paint identically every time it is repainted) */
  const h01 = (a, b) => {
    let x = (a * 374761393 + b * 668265263) | 0;
    x = Math.imul(x ^ (x >>> 13), 1274126177);
    return ((x ^ (x >>> 16)) >>> 0) / 4294967296;
  };

  function veinX(P, xc, y, phase, spacing) {
    const th = (y / 128) * P.freq * 6.283 + phase;
    const sn = Math.sin(th);
    const wv = P.zig > 0.001 ? sn * (1 - P.zig) + (0.6366 * Math.asin(sn)) * P.zig : sn;
    return xc + P.bend * triAt(y) + P.wave * wv + P.slant * spacing * ((y - 64) / 128);
  }
  function polyline(c, P, xc, dx, phase, spacing, seg) {
    c.beginPath();
    for (let k = 0; k <= seg; k++) {
      const y = -8 + (k * 144) / seg;
      const x = veinX(P, xc + dx, y, phase, spacing);
      if (k === 0) c.moveTo(x, y); else c.lineTo(x, y);
    }
  }
  function drawVeinElement(c, xc, P, phase, ci, spacing) {
    /* one column of light: a folding/swaying polyline in palette colour with a
       soft under-glow and a WHITE-HOT core - thin luminous neon in dark resin,
       never plush stripes. Three copies so bends/slants tile across x. */
    const seg = P.zig > 0.001 ? 24 : 12;
    const col = ink(ci);
    for (const dx of [-256, 0, 256]) {
      polyline(c, P, xc, dx, phase, spacing, seg);
      c.strokeStyle = col;
      c.globalAlpha = 0.26; c.lineWidth = P.coreW * 3.0; c.stroke();
      c.globalAlpha = 1.0; c.lineWidth = P.coreW; c.stroke();
      c.strokeStyle = WHITE_HOT;
      c.globalAlpha = 0.8; c.lineWidth = Math.max(1.2, P.coreW * 0.34); c.stroke();
      if (P.railA > 0.03) {
        polyline(c, P, xc + P.railGap, dx, phase, spacing, seg);
        c.strokeStyle = ink(ci + 1);
        c.globalAlpha = P.railA * 0.95; c.lineWidth = Math.max(1.6, P.coreW * 0.55); c.stroke();
        c.strokeStyle = WHITE_HOT;
        c.globalAlpha = P.railA * 0.5; c.lineWidth = Math.max(1, P.coreW * 0.2); c.stroke();
      }
    }
    if (P.beadR > 0.4) {
      for (const y of [20, 52, 84, 116]) {
        const x = xc + P.railGap * 0.5 + P.bend * triAt(y);
        for (const dx of [-256, 0, 256]) {
          c.globalAlpha = 0.3; c.fillStyle = ink(ci + 2);
          c.beginPath(); c.arc(x + dx, y, P.beadR * 1.8, 0, 6.283); c.fill();
          c.globalAlpha = 1;
          c.beginPath(); c.arc(x + dx, y, P.beadR, 0, 6.283); c.fill();
          c.globalAlpha = 0.9; c.fillStyle = WHITE_HOT;
          c.beginPath(); c.arc(x + dx, y, P.beadR * 0.42, 0, 6.283); c.fill();
        }
      }
    }
    if (P.eyeA > 0.03) {
      /* an iris opening mid-column - this is a hypno app */
      for (const dx of [-256, 0, 256]) {
        const ex = xc + dx + P.bend * 0.4;
        c.globalAlpha = P.eyeA * 0.95; c.lineWidth = 3; c.strokeStyle = ink(ci + 1);
        c.beginPath(); c.ellipse(ex, 64, 15, 30, 0, 0, 6.283); c.stroke();
        c.globalAlpha = P.eyeA * 0.55; c.lineWidth = 1.2; c.strokeStyle = WHITE_HOT;
        c.beginPath(); c.ellipse(ex, 64, 15, 30, 0, 0, 6.283); c.stroke();
        c.globalAlpha = P.eyeA * 0.85; c.fillStyle = ink(ci);
        c.beginPath(); c.arc(ex, 64, 4.8, 0, 6.283); c.fill();
        c.globalAlpha = P.eyeA * 0.9; c.fillStyle = WHITE_HOT;
        c.beginPath(); c.arc(ex, 64, 2, 0, 6.283); c.fill();
      }
    }
    if (P.ticks > 0.05) {
      c.globalAlpha = P.ticks * 0.85; c.lineWidth = 2.4; c.strokeStyle = WHITE_HOT;
      for (const y of [30, 64, 98]) {
        const x = xc - 12 + P.bend * triAt(y);
        for (const dx of [-256, 0, 256]) {
          c.beginPath(); c.moveTo(x + dx - 6, y); c.lineTo(x + dx + 6, y); c.stroke();
        }
      }
    }
    c.globalAlpha = 1;
  }
  function drawVein(c, P) {
    c.lineCap = 'round'; c.lineJoin = 'round';
    const spacing = 256 / ID.cols;
    for (let ci = 0; ci < ID.cols; ci++) {
      const xc = ((ci * spacing + ID.colOff) % 256 + 256) % 256;
      drawVeinElement(c, xc, P, ID.phases[ci], ci, spacing);
    }
  }

  function starPath(c, x, y, R) {
    c.beginPath();
    for (let k = 0; k < 8; k++) {
      const a = k * 0.7854 - 1.5708;
      const r = (k & 1) ? R * 0.36 : R;
      const px = x + Math.cos(a) * r, py = y + Math.sin(a) * r;
      if (k === 0) c.moveTo(px, py); else c.lineTo(px, py);
    }
    c.closePath();
  }
  /* the tile is 2:1 in pixels over a ~1:1 patch of wall (6.4 units along at
     pitch 36, 6.3 around a 1.0 bore), so a canvas circle lands ~2x tall on the
     chute - dots and knots are painted squashed by this much to come out
     ROUND in the world */
  const Y_ROUND = 0.48;
  function blobOne(c, x, y, rad, k, P) {
    const col = ink(k);
    c.save();
    c.translate(x, y); c.scale(1, Y_ROUND);
    x = 0; y = 0;
    c.globalAlpha = 0.15; c.fillStyle = col;
    c.beginPath(); c.arc(x, y, rad * 1.55, 0, 6.283); c.fill();
    if (P.star > 0.01) {
      c.globalAlpha = P.star; c.fillStyle = col;
      starPath(c, x, y, rad * 1.6); c.fill();
      c.globalAlpha = P.star * 0.9; c.fillStyle = WHITE_HOT;
      starPath(c, x, y, rad * 0.75); c.fill();
    }
    const disc = 1 - P.star;
    if (disc > 0.01) {
      const solid = disc * (1 - P.hollow);
      if (solid > 0.01) {
        c.globalAlpha = solid; c.fillStyle = col;
        c.beginPath(); c.arc(x, y, rad, 0, 6.283); c.fill();
        c.globalAlpha = solid * 0.85; c.fillStyle = WHITE_HOT;
        c.beginPath(); c.arc(x - rad * 0.18, y - rad * 0.18, rad * 0.36, 0, 6.283); c.fill();
      }
      const ring = disc * P.hollow;
      if (ring > 0.01) {
        c.globalAlpha = ring; c.strokeStyle = col; c.lineWidth = Math.max(1.5, rad * 0.42);
        c.beginPath(); c.arc(x, y, rad * 0.82, 0, 6.283); c.stroke();
        c.globalAlpha = ring * 0.85; c.fillStyle = WHITE_HOT;
        c.beginPath(); c.arc(x - rad * 0.36, y - rad * 0.36, rad * 0.2, 0, 6.283); c.fill();
      }
    }
    c.restore();
  }
  function blob(c, x, y, rad, k, P) {
    /* paint the dot and its wrapped twins, so a dot on an edge tiles */
    const m = rad * 2.1;
    for (const dx of [0, 256, -256]) {
      const xx = x + dx;
      if (xx < -m || xx > 256 + m) continue;
      for (const dy of [0, 128, -128]) {
        const yy = y + dy;
        if (yy < -m || yy > 128 + m) continue;
        blobOne(c, xx, yy, rad, k, P);
      }
    }
  }
  function drawField(c, P) {
    const rows = Math.max(1, Math.round(P.rows)), cols = Math.max(1, Math.round(P.cols));
    for (let r = 0; r < rows; r++) {
      const y0 = (r + 0.5) * (128 / rows);
      for (let q = 0; q < cols; q++) {
        const a = h01(r * 31 + 7, q * 17 + 3), b = h01(q * 29 + 11, r * 13 + 5);
        const x = (((q + P.stag * (r & 1)) * (256 / cols) + ID.colOff) % 256 + 256) % 256 + P.jit * 10 * (a - 0.5);
        const y = y0 + P.jit * 8 * (b - 0.5);
        const rad = Math.max(1.5, P.dotR * (1 + P.sizeVar * (a * 1.3 - 0.65)));
        blob(c, x, y, rad, r + q, P);
      }
    }
    c.globalAlpha = 1;
  }

  function drawGrid(c, P) {
    /* two diagonal line sets, x = k*gx +- s*y. nx columns per tile and m
       columns per wrap are integers, so the net is seamless in both axes */
    const nx = ID.gridNx, m = ID.gridM, gx = 256 / nx, s = (m * gx) / 128;
    c.lineCap = 'butt';
    if (P.fill > 0.01) {
      /* harlequin: the diamond cells between the lines, alternate parity
         filled (parity is seamless under both wraps) */
      const u0 = Math.floor(-s * 128 / gx) - 1, u1 = Math.ceil(256 / gx) + 1;
      const v0 = -1, v1 = Math.ceil((256 + s * 128) / gx) + 1;
      for (let i = u0; i <= u1; i++) {
        for (let j = v0; j <= v1; j++) {
          const even = ((i + j) & 1) === 0;
          c.globalAlpha = even ? P.fill * 0.9 : P.fill * 0.38;
          c.fillStyle = even ? ink(0) : ink(1);
          c.beginPath();
          c.moveTo((i + j) * gx / 2, (j - i) * gx / (2 * s));
          c.lineTo((i + 1 + j) * gx / 2, (j - i - 1) * gx / (2 * s));
          c.lineTo((i + 1 + j + 1) * gx / 2, (j + 1 - i - 1) * gx / (2 * s));
          c.lineTo((i + j + 1) * gx / 2, (j + 1 - i) * gx / (2 * s));
          c.closePath(); c.fill();
        }
      }
    }
    const lineSet = (sign, col) => {
      const k0 = sign > 0 ? -m - 1 : -1, k1 = sign > 0 ? nx + 1 : nx + m + 1;
      c.beginPath();
      for (let k = k0; k <= k1; k++) {
        c.moveTo(k * gx, 0);
        c.lineTo(k * gx + sign * s * 128, 128);
      }
      c.strokeStyle = col;
      c.globalAlpha = 0.24; c.lineWidth = P.lineW * 3; c.stroke();
      c.globalAlpha = 1; c.lineWidth = P.lineW; c.stroke();
      if (P.lineW >= 1.8) {
        c.strokeStyle = WHITE_HOT;
        c.globalAlpha = 0.7; c.lineWidth = Math.max(0.8, P.lineW * 0.34); c.stroke();
      }
    };
    lineSet(1, ink(0));
    lineSet(-1, ink(1));
    if (P.knotR > 0.4) {
      /* knots at every crossing: x = (j+k)gx/2, y = (j-k)gx/(2s) */
      const col = ink(2);
      for (let k = -m - 1; k <= nx + 1; k++) {
        for (let j = -1; j <= nx + m + 1; j++) {
          const y = (j - k) * gx / (2 * s);
          if (y < -P.knotR || y > 128 + P.knotR) continue;
          const x = (j + k) * gx / 2;
          if (x < -P.knotR || x > 256 + P.knotR) continue;
          c.save(); c.translate(x, y); c.scale(1, Y_ROUND);
          c.globalAlpha = 0.2; c.fillStyle = col;
          c.beginPath(); c.arc(0, 0, P.knotR * 1.6, 0, 6.283); c.fill();
          c.globalAlpha = 1;
          c.beginPath(); c.arc(0, 0, P.knotR, 0, 6.283); c.fill();
          c.globalAlpha = 0.9; c.fillStyle = WHITE_HOT;
          c.beginPath(); c.arc(0, 0, P.knotR * 0.4, 0, 6.283); c.fill();
          c.restore();
        }
      }
    }
    c.globalAlpha = 1;
  }

  function paintFamily(cv, fam, P) {
    const c = cv.getContext('2d');
    if (!c) return;
    /* every painter works in 256x128 PATTERN UNITS; the canvas is BAND_S times
       that so the strokes stay crisp along the long coils */
    c.setTransform(BAND_S, 0, 0, BAND_S, 0, 0);
    c.globalCompositeOperation = 'source-over';
    c.globalAlpha = 1;
    c.fillStyle = '#000';
    c.fillRect(0, 0, 256, 128);
    if (fam === 'vein') drawVein(c, P);
    else if (fam === 'field') drawField(c, P);
    else drawGrid(c, P);
    c.globalAlpha = 1;
  }
  function ensureCache(slot, stopIdx) {
    if (cacheIdx[slot] === stopIdx) return;
    const S = ID.stops[stopIdx];
    paintFamily(cacheCanvas[slot], S.fam, S);
    cacheIdx[slot] = stopIdx;
  }
  /* paint the band for journey position jp: pair (i, i+1), fraction w */
  function renderBand(jp) {
    const n = ID.stops.length;
    let i = Math.floor(jp);
    if (i >= n - 1) i = n - 2;
    if (i < 0) i = 0;
    const w = n > 1 ? Math.max(0, Math.min(1, jp - i)) : 0;
    const A = ID.stops[i], B = ID.stops[Math.min(n - 1, i + 1)];
    const ws = smooth(w);
    if (A.fam === B.fam || ws <= 0 || ws >= 1) {
      const S = ws >= 1 ? B : A;
      if (A.fam === B.fam) {
        for (const f of FAM_KEYS[A.fam]) P_CUR[f] = A[f] + (B[f] - A[f]) * ws;
        paintFamily(bandCanvas, A.fam, P_CUR);
      } else {
        paintFamily(bandCanvas, S.fam, S);
      }
    } else {
      ensureCache(0, i); ensureCache(1, i + 1);
      const c = bandCanvas.getContext('2d');
      if (!c) return;
      c.setTransform(1, 0, 0, 1, 0, 0);
      c.globalCompositeOperation = 'source-over';
      c.globalAlpha = 1; c.fillStyle = '#000'; c.fillRect(0, 0, BAND_W, BAND_H);
      c.globalCompositeOperation = 'lighter';
      c.globalAlpha = 1 - ws; c.drawImage(cacheCanvas[0], 0, 0);
      c.globalAlpha = ws; c.drawImage(cacheCanvas[1], 0, 0);
      c.globalCompositeOperation = 'source-over';
      c.globalAlpha = 1;
    }
    bandTex.needsUpdate = true;
  }
  renderBand(0);

  /* THE MATERIAL. Dark resin body; the emissive map IS the colour (white
     emissive tint), so the palette shows through at full saturation. A hue
     rotation rides in the shader as ONE uniform - a gray-axis rotation of the
     emissive sample - which is how the lines cycle colour every frame without
     a single canvas redraw. Reduced motion leaves the uniform at 0. */
  const hueU = { value: 0 };
  const tubeMat = new THREE.MeshStandardMaterial({
    color: 0x24244a, roughness: 0.3, metalness: 0.42,
    emissive: 0xffffff, emissiveIntensity: 0.95,
    emissiveMap: bandTex,
  });
  tubeMat.onBeforeCompile = (shader) => {
    shader.uniforms.uIcHue = hueU;
    shader.fragmentShader = shader.fragmentShader
      .replace('#include <common>',
        '#include <common>\nuniform float uIcHue;\n'
        + 'vec3 icHueRot(vec3 c, float a){ const vec3 k = vec3(0.57735026919); float cs = cos(a); float sn = sin(a);'
        + ' return c * cs + cross(k, c) * sn + k * dot(k, c) * (1.0 - cs); }\n')
      .replace('#include <emissivemap_fragment>',
        '#ifdef USE_EMISSIVEMAP\n'
        + '\tvec4 emissiveColor = texture2D( emissiveMap, vEmissiveMapUv );\n'
        + '\ttotalEmissiveRadiance *= clamp(icHueRot(emissiveColor.rgb, uIcHue), 0.0, 1.0);\n'
        + '#endif\n');
  };
  tubeMat.customProgramCacheKey = () => 'ic-tube-hue';
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
  halo.scale.set(3.3, 3.3, 1);   // ~1.65x the bore: light bleeds, the load does not
  halo.renderOrder = 10;
  world.add(halo);

  let ghost = null;
  try {
    const tex = new THREE.TextureLoader().load(new URL('./assets/bubble.png', import.meta.url).href);
    ghost = new THREE.Sprite(new THREE.SpriteMaterial({
      map: tex, transparent: true, opacity: 0.0, depthWrite: false, depthTest: false,
      blending: THREE.AdditiveBlending, color: 0xffd2ec,
    }));
    ghost.scale.set(1.45, 1.45, 1);   // INSIDE the bore (dia 2.0), never over it
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
  pool.position.set(0, LAND.y, 0);   // right where the DOM bubble lands (see THE LANDING)
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
      /* out from under the bubble: they start just below the landing */
      pPos[i * 3] = 0; pPos[i * 3 + 1] = LAND.y - 0.4; pPos[i * 3 + 2] = 0;
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
  /* semantic tints laid OVER the coloured band: white at rest, gold on a hot
     streak, red on a denyHit. The palette itself lives in the canvas. */
  const TINT_WHITE = new THREE.Color(0xffffff);
  const TINT_GOLD = new THREE.Color(0xffd23f);
  const TINT_RED = new THREE.Color(0xff3a5e);
  const tintTarget = TINT_WHITE.clone();
  const LEAD = PAL[0];    // [h, s, l] - the palette's lead colour
  const DISH_TINT = new THREE.Color().setHSL((LEAD[0] / 360) % 1, Math.min(0.6, LEAD[1] * 0.5), 0.82);
  const DISH_LAV = new THREE.Color(0xcabdf0);
  const DISH_WARM = new THREE.Color(0xf0d9a8);
  const WHITE = new THREE.Color(0xffffff);
  const scratchColor = new THREE.Color();
  let redFlash = 0;       // denyHit band flash, decays
  let surge = 0;          // pop(true) swallow-gulp, decays
  let scramT = 0;         // denyHit pattern scramble, counts down
  let dishExhale = 0;     // denyPass crater tint, decays
  let kbY = 0;            // accumulated ken-burns bore-roll
  let lastMorphAt = -1;   // redraw throttle clock stamp
  let lastDrawnPos = 0;   // the jPos the band was last painted at
  const MORPH_DT = perf ? 0.25 : 0.125;
  /* the journey walk: jPos glides toward jTarget (an integer stop index);
     advance is the streak's push, milestone the highest rung earned since the
     last break */
  let jPos = 0, jTarget = 0, advance = 0, milestone = 0, lastStreak = 0;
  const STOPS_N = ID.stops.length;
  /* the hue wheel: angle in radians for the shader; pool/basin light follow.
     swayPh is the sway's own phase, integrated so a streak change cannot jump it */
  let hueAng = 0, swayPh = 0;
  /* the key light's orbit basis (its authored position, in polar form) */
  const KEY_R = Math.hypot(-4, 3);
  const KEY_A0 = Math.atan2(3, -4);

  /* the palette's lead colour leads the basin light too - the coils near the
     crater catch the same colour the lines wear */
  basinLight.color.setHSL((LEAD[0] / 360) % 1, Math.min(1, LEAD[1]), Math.max(0.55, LEAD[2]));

  function retarget() {
    /* stop k arrives at progress (k - 0.5) / (n - 1), nudged by the streak */
    const raw = mood.progress * (STOPS_N - 1) + advance;
    jTarget = Math.max(0, Math.min(STOPS_N - 1, Math.round(raw)));
  }
  function applyMood(m) {
    mood = { progress: Math.max(0, Math.min(1, Number(m && m.progress) || 0)),
             streak: Math.max(0, Math.round(Number(m && m.streak) || 0)) };
    if (!reduced) {
      spinVel = (SPIN_BASE + (SPIN_MAX - SPIN_BASE) * mood.progress) * ID.spinMul;
      flowSpeed = (FLOW_BASE + (FLOW_MAX - FLOW_BASE) * mood.progress) * ID.flowMul;
    }
    /* the streak pushes the journey: a new milestone (5/10/15/20) advances it
       0.6 of a stop early; a chain of 4+ breaking to 0 pulls it 0.6 back */
    const rung = mood.streak >= 20 ? 4 : mood.streak >= 15 ? 3 : mood.streak >= 10 ? 2 : mood.streak >= 5 ? 1 : 0;
    if (rung > milestone) {
      advance = Math.min(STOPS_N - 1, advance + 0.6 * (rung - milestone));
      milestone = rung;
    }
    if (mood.streak === 0 && lastStreak >= 4) {
      advance = Math.max(0, advance - 0.6);
      milestone = 0;
    }
    lastStreak = mood.streak;
    if (!reduced) retarget();
    // a hot streak warms the lines toward gold; red is the denyHit's alone
    tintTarget.copy(TINT_WHITE).lerp(TINT_GOLD, mood.streak >= 10 ? 0.42 : mood.streak >= 5 ? 0.28 : 0);
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

    /* the journey: glide jPos toward its target stop at the pair's own pace
       (a morph takes morphT, a crossfade xfadeT) and repaint the band only
       while it is actually moving, never faster than MORPH_DT */
    if (!reduced && STOPS_N > 1) {
      if (jPos !== jTarget) {
        let i = Math.floor(Math.min(jPos, jTarget));
        if (i >= STOPS_N - 1) i = STOPS_N - 2;
        if (i < 0) i = 0;
        const same = ID.stops[i].fam === ID.stops[Math.min(STOPS_N - 1, i + 1)].fam;
        const step = dt / (same ? ID.morphT : ID.xfadeT);
        const d = jTarget - jPos;
        jPos += Math.abs(d) <= step ? d : Math.sign(d) * step;
      }
      if (jPos !== lastDrawnPos && (clock - lastMorphAt >= MORPH_DT || jPos === jTarget)) {
        renderBand(jPos);
        lastDrawnPos = jPos; lastMorphAt = clock;
      }
    }

    /* the hue wheel: bold palettes spin it, the classic pair only sways about
       its arc; the streak quickens both. One uniform, zero redraws. */
    if (!reduced) {
      const rate = ID.cycleBase * (1 + Math.min(3, mood.streak / 4));
      if (ID.cycleMode === 'sway') {
        swayPh += rate * 2.2 * dt;
        hueAng = 0.5 * Math.sin(swayPh * ID.cycleSign);   // +-29 degrees about the arc
      } else {
        hueAng += rate * ID.cycleSign * dt;
        if (hueAng > 6.2832) hueAng -= 6.2832;
        if (hueAng < -6.2832) hueAng += 6.2832;
      }
      hueU.value = hueAng;
    }

    /* band tint: chase the mood tint, punch red on a denyHit; the gulp flares
       the lines too */
    redFlash *= Math.pow(0.02, dt);
    tubeMat.emissive.copy(tintTarget).lerp(TINT_RED, Math.min(1, redFlash));
    const breathe = reduced ? 0 : Math.sin(clock * 0.9) * 0.12;
    tubeMat.emissiveIntensity = 0.95 + breathe + redFlash * 0.5 + surge * 0.22;

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

    /* the crater breathes: lead tint at rest, lavender on the exhale, warm on
       a hot streak - all through the unlit dish's free colour multiplier, so
       the hole-not-dome ramp itself is never touched */
    dishExhale *= Math.pow(0.15, dt);
    const dBreath = reduced ? 0 : 0.05 * Math.sin(clock * 0.5);
    const dAmt = Math.min(0.5, 0.12 + dBreath + dishExhale * 0.3 + (mood.streak >= 5 ? 0.15 : 0));
    scratchColor.copy(mood.streak >= 5 ? DISH_WARM : DISH_TINT).lerp(DISH_LAV, Math.min(1, dishExhale));
    dishMat.color.copy(WHITE).lerp(scratchColor, dAmt);

    /* the pool glow in the belly: near-invisible idle, blooms on the reveal.
       It wears the palette's lead colour, cycled with the lines (setHSL
       mutates in place - no allocation) - never white, which turns the crater
       milky and costs the hole its darkness */
    pool.material.opacity = Math.min(0.4, 0.02 + (reduced ? 0 : 0.012 * Math.sin(clock * 1.1)) + rimBoost * 0.16 + dishExhale * 0.07);
    const leadH = ((LEAD[0] + hueAng * 57.2958) / 360) % 1;
    pool.material.color.setHSL(leadH < 0 ? leadH + 1 : leadH, Math.min(1, LEAD[1]), Math.max(0.6, LEAD[2]));
    if (!reduced && ID.cycleMode === 'spin') {
      basinLight.color.setHSL(leadH < 0 ? leadH + 1 : leadH, Math.min(1, LEAD[1]), Math.max(0.55, LEAD[2]));
    }

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
  /* THE VISIBILITY GUARD (mobile web): a hidden tab schedules no frames at
     all. Coming back cannot teleport the animations: kick() zeroes `last`
     (first dt defaults to 0.016) and loop() clamps dt to 0.05 regardless. */
  let hidden = false;
  const onVis = () => {
    try {
      const h = !!(typeof document !== 'undefined' && document.hidden);
      if (h === hidden) return;
      hidden = h;
      if (h) {
        if (rafId && typeof cancelAnimationFrame === 'function') cancelAnimationFrame(rafId);
        rafId = 0;
      } else if (!dead && !suspended) kick();
    } catch (e) { /* noop */ }
  };
  try {
    if (typeof document !== 'undefined' && document.addEventListener) {
      hidden = !!document.hidden;
      document.addEventListener('visibilitychange', onVis);
    }
  } catch (e) { /* noop */ }
  function loop(ts) {
    if (dead || suspended || hidden) return;
    const dt = last ? Math.min(0.05, (ts - last) / 1000) : 0.016;
    last = ts;
    frame(dt);
    const raf = (typeof requestAnimationFrame === 'function') ? requestAnimationFrame : null;
    if (raf) rafId = raf(loop);
  }
  function kick() {
    if (hidden) return;    // onVis kicks again when the tab comes back
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
    landing() { return { x: LAND.pct.x, y: LAND.pct.y, worldY: LAND.y }; },
    destroy() {
      dead = true;
      try { if (rafId && typeof cancelAnimationFrame === 'function') cancelAnimationFrame(rafId); } catch (e) { /* noop */ }
      try { if (typeof document !== 'undefined' && document.removeEventListener) document.removeEventListener('visibilitychange', onVis); } catch (e) { /* noop */ }
      try {
        tubeGeo.dispose(); tubeMat.dispose();
        dish.geometry.dispose(); dishMat.dispose(); dishTex.dispose();
        rim.geometry.dispose(); rimMat.dispose();
        bore.geometry.dispose(); boreMat.dispose(); boreTex.dispose();
        mouthWall.geometry.dispose(); noseMat.dispose();
        mouthLip.geometry.dispose(); mouthLipMat.dispose();
        lip.geometry.dispose(); lipMat.dispose();
        bandTex.dispose(); haloTex.dispose();
        /* the crossfade cache is plain canvases (no GPU side) - shrink them so
           the bitmaps go with the class, not with the GC's mood */
        for (const cv of cacheCanvas) { cv.width = 1; cv.height = 1; }
        bandCanvas.width = 1; bandCanvas.height = 1;
        pool.material.dispose(); poolTex.dispose();
        pGeo.dispose(); pMat.dispose();
        renderer.dispose();
        if (canvas.parentNode) canvas.parentNode.removeChild(canvas);
      } catch (e) { /* noop */ }
    },
  };
}
