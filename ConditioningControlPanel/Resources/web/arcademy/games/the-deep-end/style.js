/* ============================================================================
 * games/the-deep-end/style.js - the game injects its OWN stylesheet from JS.
 *
 * THE POOL AT NIGHT. The class root is a full-viewport stage: a square board
 * floats in deep water, lit from above by the casino's caustics (casino.js
 * paints the --de-n-* props; the fallbacks below are the plain pool, so a
 * disarmed casino changes nothing). Composition, exactly the DOM contract:
 *
 *   .g-de-stage[data-phase][data-size]  absolute inset:0, explicit ground,
 *                 padded under the shell's proctor strip; --de-board is the
 *                 one square every bench-level thing is sized from
 *   .g-de-backdrop  the casino's lighting rig (pointer-events:none, z 0)
 *   .g-de-hud     four chips: depth (tier-coloured), clock, score, chain
 *                 (+ .g-de-retake, and .g-de-surface - a real button - on a free swim)
 *   .g-de-bench   flex:1; the board centred; the marquee + the casino's
 *                 overlay (.g-de-cs) live here, around and above the board
 *   .g-de-board   the gesture surface; --de-n (4|5) -> --de-tile / --de-step
 *   .g-de-tile    ABSOLUTE, positioned ONLY by its transform from --r / --c
 *   .g-de-flashwell / .g-de-msg / .g-de-end
 *
 * THE TRANSFORM LAW. index.js (A) writes --r / --c and nothing else about a
 * tile's position; this file owns the transform and its 170ms slide (pass 2:
 * fast start, soft landing; PLAYTEST.MOVE_MS is the same number). The
 * transform is positional ONLY - every pop, ripple, sag, stretch and shimmer
 * lives on the tile's ::before (the body), its ::after (the ripple) or its
 * children, never on the tile's own transform, so A's slide is never fought.
 * Two pairs of decoration vars are composed INTO the transform for the decks:
 *   --de-lie-dx / --de-lie-dy   unitless step offsets (trickster cameo)
 *   --de-lean-x / --de-lean-y   -1..1 lean toward a partner (casino strain)
 * They default to 0 and are removed by the deck that set them. The bench's
 * own lean is the SEPARATE --de-bench-lean-x/y pair (it used to share the
 * tile names, and on any host without @property registration the bench value
 * inherited into every tile's positional transform - an extra transform
 * transition per tile per slide, and again on the spring-back): it tilts the
 * whole bench toward a move and springs it back (casino.slide / bump).
 *
 * PASS 2 - THE SLIDE, THE WALL, THE FACES. index.js marks moving tiles
 * .is-sliding(-<dir>/-x/-y) with --de-td (cells travelled): a child .g-de-trail
 * streaks back toward the origin and the body stretches then squashes. A
 * blocked move shakes .g-de-board (.g-de-bump-<dir>) while the casino flashes
 * the wall (.g-de-wall in the overlay). Vacated cells blink (.is-wake); after
 * a stall, the cells of every direction that would move pulse (.is-hint).
 * Every tile may wear a .g-de-face > img.g-de-media (the player's own media,
 * tinted in the tier hue, darker and greyer at depth), a rim glyph badge, a
 * numeral badge (.g-de-num) and the NAME as a neon stamp on a dark plate with
 * an FX ladder by tier (glow / halo+breathe / chromatic+flicker / bloom+
 * pulse+scanline). Legibility first: the plate is always there.
 *
 * PASS 3 - THE HAND, THE CURRENT. A pointer down on the board puts .g-de-held
 * on it and writes --de-grab-x/y (-1..1, registered INHERITING so they reach
 * the tiles); the tiles lift and lean toward the finger, damped to a third
 * under .g-de-grab-blocked, and the release rides the same 170ms transition
 * straight into the move. Every legal slide sends a flock of .g-de-arrow
 * chevrons through the casino's .g-de-flow layer, pointing and drifting the way
 * the board just went (casino.js places them; one 800ms keyframe, four
 * directions). Reduced motion keeps the lift and the fade, drops both travels.
 *
 * THE GLYPH IS THE TRUTH (Law IV). .g-de-glyph is DRAWN from data-tier, never
 * typed: tiers 1-5 are N thin rings, 6-10 are a filled core plus N-5 rings
 * (the core counts five), 11 is the eclipse - a dark disc with a bright
 * corona. Pass 2 moves it to a small rim badge (top-left) on a dark disc so
 * the media and the name own the face. A trickster may lie on .g-de-name; it
 * cannot lie on the rings or on the numeral.
 *
 * PALETTE: tier colour descends from pale surface light (lavender-cream, dark
 * ink) through pink and violet into indigo and near-black (cream ink), every
 * stop a color-mix of shell tokens so init.palette reskins the whole ladder.
 * Contrast flips at tier 6: light tiles carry ground ink, dark tiles carry
 * cream. .g-de-depth chips share the same table.
 *
 * NOTHING IS EVER STILL (Law III): the backdrop breathes, caustics drift,
 * deep tiles glint, the marquee crawls. REDUCED MOTION twice over
 * (html.arc-reduced + the media query): only the slide transition survives.
 * .g-de-stage.suspended freezes every animation (animation-play-state) so A
 * can hold the pool's breath with the class.
 *
 * PASS 5 - THE DRIFT LAW + THE LITE RUNG (a Chromium trace: the GPU process's
 * main thread at 79% of a core on a 3060 Ti, three roughly equal thirds - face
 * render surfaces, full-screen re-raster, and 16 live 854x480 decodes).
 *   PATTERNS DRIFT BY TRANSFORM, NEVER BY BACKGROUND-POSITION. A
 *   background-position tween re-rasters the WHOLE layer every frame; a
 *   transform is compositor-only. Every converted sheet is oversized by exactly
 *   one tile period on its trailing edge and translated by exactly that period,
 *   so the wrap lands on an identical pixel. Rays and vortex already rotated by
 *   transform and were left alone.
 *   A FACE IS FOUR BOXES AND THERE ARE 25 OF THEM: no isolation, no blend
 *   mode, no filter on a <video>, and the ken-burns pan is for stills only.
 *   .g-de-lite is the whole room one rung down (see the block near the bottom).
 *   THE TOUCH RUNG (html.ae-touch) is NOT that ladder: it is a hardware
 *   ceiling that applies on FULL too and stacks with lite. No blur over a
 *   face, no blend surface, no backdrop-filter - see the block by the lite one.
 * ==========================================================================*/

const STYLE_ID = 'g-de-style';

/* THE GLYPH RULES, generated: one radial-gradient per tier with EXPLICIT em
   stops, so the rings can start outside a hollow centre (a repeating gradient
   would draw the first ring on the dot and tier 1 read as a speck). Tiers 1-5:
   N rings. Tiers 6-10: a filled core (worth five) plus N-5 rings. Tier 11: the
   eclipse. Everything in em, so the mark scales with the tile. */
const GLYPH = Object.freeze({ C0: 0.5, GAP: 0.3, W: 0.11, CORE: 0.3 });
function glyphRules() {
  const em = (v) => v.toFixed(2) + 'em';
  let css = '';
  for (let t = 1; t <= 11; t++) {
    let size;
    let bg;
    if (t <= 10) {
      const rings = t <= 5 ? t : t - 5;
      const stops = [];
      if (t >= 6) stops.push('currentColor 0 ' + em(GLYPH.CORE), 'transparent ' + em(GLYPH.CORE) + ' ' + em(GLYPH.C0));
      else stops.push('transparent 0 ' + em(GLYPH.C0));
      let r = GLYPH.C0;
      for (let k = 0; k < rings; k++) {
        stops.push('currentColor ' + em(r) + ' ' + em(r + GLYPH.W));
        const next = k + 1 < rings ? r + GLYPH.GAP : r + GLYPH.W + 0.3;
        stops.push('transparent ' + em(r + GLYPH.W) + (k + 1 < rings ? ' ' + em(next) : ''));
        r += GLYPH.GAP;
      }
      size = 2 * (GLYPH.C0 + (rings - 1) * GLYPH.GAP + GLYPH.W + 0.08);
      bg = 'radial-gradient(circle, ' + stops.join(', ') + ')';
    } else {
      const disc = GLYPH.C0 + GLYPH.GAP * 2;
      size = 2 * (disc + GLYPH.W + 0.34);
      bg = 'radial-gradient(circle, color-mix(in srgb, var(--ground), black 50%) 0 ' + em(disc)
        + ', currentColor ' + em(disc) + ' ' + em(disc + GLYPH.W * 1.4)
        + ', color-mix(in srgb, var(--pink), transparent 30%) ' + em(disc + GLYPH.W * 1.4 + 0.05)
        + ', transparent ' + em(disc + GLYPH.W * 1.4 + 0.34) + ')';
    }
    css += '.g-de-tile[data-tier="' + t + '"] .g-de-glyph{width:' + em(size) + ';height:' + em(size)
      + ';background:' + bg + ' rgba(8,6,22,.66)}\n';   // pass 2: the rings sit on a dark badge disc
  }
  return css;
}

export const STYLE_TEXT = `
/* ---- registered decoration props: numbers interpolate, so a var change
   MORPHS (the journey) instead of snapping ---------------------------------- */
@property --de-n-depth{syntax:'<number>';inherits:true;initial-value:0}
@property --de-n-a-bands{syntax:'<number>';inherits:true;initial-value:0}
@property --de-n-a-rays{syntax:'<number>';inherits:true;initial-value:0}
@property --de-n-a-lens{syntax:'<number>';inherits:true;initial-value:0}
@property --de-n-a-vortex{syntax:'<number>';inherits:true;initial-value:0}
@property --de-n-a-motes{syntax:'<number>';inherits:true;initial-value:0}
@property --de-n-a-veil{syntax:'<number>';inherits:true;initial-value:.6}
@property --de-n-tilt{syntax:'<number>';inherits:true;initial-value:-18}
@property --de-n-scale{syntax:'<number>';inherits:true;initial-value:1}
@property --de-lean-x{syntax:'<number>';inherits:false;initial-value:0}
@property --de-lean-y{syntax:'<number>';inherits:false;initial-value:0}
@property --de-bench-lean-x{syntax:'<number>';inherits:false;initial-value:0}
@property --de-bench-lean-y{syntax:'<number>';inherits:false;initial-value:0}
@property --de-lie-dx{syntax:'<number>';inherits:false;initial-value:0}
@property --de-lie-dy{syntax:'<number>';inherits:false;initial-value:0}
/* pass 3 - THE HAND: the grab vector lives on the BOARD (index.js writes it on
   pointermove) and has to reach every tile, so unlike the lean pair these two
   INHERIT. -1..1 each; 0,0 is a press with no drag yet. */
@property --de-grab-x{syntax:'<number>';inherits:true;initial-value:0}
@property --de-grab-y{syntax:'<number>';inherits:true;initial-value:0}

/* ---- the stage: the whole window is the pool ------------------------------ */
.g-de-stage{position:absolute;inset:0;overflow:hidden;display:flex;flex-direction:column;
  align-items:center;gap:8px;padding:72px 18px 14px;color:var(--ink);
  --de-board:min(calc(100dvh - 236px), calc(100vw - 48px), 760px);
  --de-gap:clamp(5px,1.1vmin,14px);
  --de-la:var(--de-n-la, color-mix(in srgb, var(--lav), transparent 70%));
  --de-lb:var(--de-n-lb, color-mix(in srgb, var(--pink), transparent 78%));
  --de-breath:var(--de-n-breath,8s);
  --de-drift:var(--de-n-drift,22s);
  --de-calm:0;
  transition:--de-n-depth 1.6s ease, --de-n-a-bands 3.2s ease, --de-n-a-rays 3.2s ease,
    --de-n-a-lens 3.2s ease, --de-n-a-vortex 3.2s ease, --de-n-a-motes 3.2s ease,
    --de-n-a-veil 3.2s ease, --de-n-tilt 4s ease, --de-n-scale 4s ease;
  background:
    radial-gradient(120% 70% at 50% -10%, var(--de-la), transparent 60%),
    radial-gradient(90% 60% at 50% 110%, var(--de-lb), transparent 62%),
    color-mix(in srgb, var(--ground), black 40%)}
.g-de-stage.suspended *{animation-play-state:paused !important}
.g-de-stage.g-de-calm{--de-calm:1}

/* ---- the backdrop: the casino's lighting rig (decoration only) ----------- */
.g-de-backdrop{position:absolute;inset:0;pointer-events:none;z-index:0;overflow:hidden}
.g-de-backdrop *{pointer-events:none}
/* =========================== THE DRIFT LAW (pass 5, measured) ==============
   PATTERNS DRIFT BY TRANSFORM, NEVER BY BACKGROUND-POSITION (compositor-only;
   a background-position tween re-rasters the whole layer every frame).
   These sheets are full-screen. A 'to{background-position:...}' keyframe on one
   of them is a full-viewport repaint at 60Hz, and the trace found SIX of them
   running at once - a third of the GPU process's main thread went on nothing
   but re-painting gradients that had not changed shape.
   The shape every converted layer takes:
     - one background layer per pseudo (::before / ::after). Two layers with
       different tile sizes and different directions cannot share one transform.
     - the pseudo is oversized by exactly one tile period IN THE DIRECTION OF
       TRAVEL ('inset: top right bottom left'), so the vacated edge is always
       already covered. '.g-de-backdrop' has overflow:hidden and does the clip.
     - the keyframe translates by exactly ONE TILE PERIOD, so the wrap at 100%
       lands on an identical pixel and the loop is seamless. An 'alternate'
       layer is exempt (it walks back the way it came).
   Rays and vortex already rotate by transform; they are untouched.
   ======================================================================== */
/* every family layer ken-burns on its own box (scale+pan), the pattern drifts
   on its ::before / ::after (transform) - two motions, no fight */
.g-de-bd{position:absolute;inset:-8%;opacity:0;
  animation:g-de-kb var(--de-n-kb,28s) ease-in-out infinite alternate}
.g-de-bd::before{content:"";position:absolute;inset:0}
@keyframes g-de-kb{from{transform:scale(1) translate(0,0)}to{transform:scale(1.06) translate(1.4%,-1.1%)}}

/* the veil: surface light from above; it breathes (always on, calmer on exhale) */
.g-de-bd-veil{opacity:var(--de-n-a-veil,.6)}
.g-de-bd-veil::before{
  background:radial-gradient(70% 55% at 50% -6%, var(--de-la), transparent 70%);
  animation:g-de-breathe calc(var(--de-breath) * (1 + var(--de-calm) * .8)) ease-in-out infinite alternate}
@keyframes g-de-breathe{from{opacity:.55;transform:scaleY(1)}to{opacity:1;transform:scaleY(1.08)}}

/* bands: rippling light bands at the seed's tilt, two counter-drifting sheets.
   THE DRIFT LAW: one sheet per pseudo, each oversized by its own tile period on
   the trailing edges and translated by exactly that period. */
.g-de-bd-bands{opacity:var(--de-n-a-bands,0)}
.g-de-bd-bands::after{content:"";position:absolute}
.g-de-bd-bands::before{inset:-220px 0 0 -220px;
  background:repeating-linear-gradient(calc(var(--de-n-tilt,-18) * 1deg), transparent 0,
    var(--de-la) calc(18px * var(--de-n-scale,1)), transparent calc(40px * var(--de-n-scale,1)),
    transparent calc(64px * var(--de-n-scale,1)));
  background-size:220px 220px;
  animation:g-de-bands-a var(--de-drift) linear infinite}
@keyframes g-de-bands-a{from{transform:translate3d(0,0,0)}to{transform:translate3d(220px,220px,0)}}
.g-de-bd-bands::after{inset:-340px -340px 0 0;
  background:repeating-linear-gradient(calc(var(--de-n-tilt,-18) * 1deg + 7deg), transparent 0,
    var(--de-lb) calc(26px * var(--de-n-scale,1)), transparent calc(54px * var(--de-n-scale,1)),
    transparent calc(92px * var(--de-n-scale,1)));
  background-size:340px 340px;
  animation:g-de-bands-b var(--de-drift) linear infinite}
@keyframes g-de-bands-b{from{transform:translate3d(0,0,0)}to{transform:translate3d(-340px,340px,0)}}

/* rays: god-rays from the surface, swaying */
.g-de-bd-rays{opacity:var(--de-n-a-rays,0)}
.g-de-bd-rays::before{inset:-30% -10% 0;transform-origin:50% 0;
  background:conic-gradient(from 168deg at 50% 0%, transparent 0 5deg, var(--de-la) 6deg 7.5deg,
    transparent 8deg 13deg, var(--de-lb) 14deg 15deg, transparent 16deg 24deg);
  -webkit-mask-image:linear-gradient(to bottom, #000 10%, transparent 85%);
  mask-image:linear-gradient(to bottom, #000 10%, transparent 85%);
  animation:g-de-sway calc(var(--de-drift) * .7) ease-in-out infinite alternate}
@keyframes g-de-sway{from{transform:rotate(-2.5deg)}to{transform:rotate(2.5deg)}}

/* lens: overlapping caustic nets - two ring fields a hair out of phase, so the
   moire reads as light on a pool floor. Both sheets ALTERNATE, so they walk
   back the way they came and the travel need not be a whole tile period. */
.g-de-bd-lens{opacity:var(--de-n-a-lens,0)}
.g-de-bd-lens::after{content:"";position:absolute}
.g-de-bd-lens::before{inset:-40px 0 0 -60px;mix-blend-mode:screen;
  background:repeating-radial-gradient(circle at 30% 40%, transparent 0 calc(22px * var(--de-n-scale,1)),
    var(--de-la) calc(24px * var(--de-n-scale,1)), transparent calc(28px * var(--de-n-scale,1)));
  background-size:300px 300px;
  animation:g-de-lens-a var(--de-drift) ease-in-out infinite alternate}
@keyframes g-de-lens-a{from{transform:translate3d(0,0,0)}to{transform:translate3d(60px,40px,0)}}
.g-de-bd-lens::after{inset:-70px -50px 0 0;mix-blend-mode:screen;
  background:repeating-radial-gradient(circle at 70% 60%, transparent 0 calc(25px * var(--de-n-scale,1)),
    var(--de-lb) calc(27px * var(--de-n-scale,1)), transparent calc(31px * var(--de-n-scale,1)));
  background-size:360px 360px;
  animation:g-de-lens-b var(--de-drift) ease-in-out infinite alternate}
@keyframes g-de-lens-b{from{transform:translate3d(0,0,0)}to{transform:translate3d(-50px,70px,0)}}

/* vortex: a slow turning whirl, masked to a soft disc, seed-signed spin */
.g-de-bd-vortex{opacity:var(--de-n-a-vortex,0)}
.g-de-bd-vortex::before{inset:-20%;
  background:repeating-conic-gradient(from 0deg, transparent 0 9deg, var(--de-la) 10deg 13deg,
    transparent 14deg 21deg, var(--de-lb) 22deg 23deg, transparent 24deg 30deg);
  -webkit-mask-image:radial-gradient(circle at 50% 50%, #000 10%, transparent 62%);
  mask-image:radial-gradient(circle at 50% 50%, #000 10%, transparent 62%);
  animation:g-de-spin calc(var(--de-drift) * 3) linear infinite}
@keyframes g-de-spin{to{transform:rotate(calc(var(--de-n-spindir,1) * 360deg))}}

/* motes: two drifting sheets of specks rising through the water. The rise is
   exactly one tile height per cycle (110px / 170px), which is what makes the
   wrap invisible; the old few-pixel sideways drift is gone with the
   background-position tween, and the two speeds still read as parallax. */
.g-de-bd-motes{opacity:var(--de-n-a-motes,0)}
.g-de-bd-motes::after{content:"";position:absolute}
.g-de-bd-motes::before{inset:0 0 -110px 0;
  background:radial-gradient(circle, var(--de-la) 0 1.5px, transparent 2.5px);
  background-size:90px 110px;
  animation:g-de-motes-a calc(var(--de-drift) * 1.4) linear infinite}
@keyframes g-de-motes-a{from{transform:translate3d(0,0,0)}to{transform:translate3d(0,-110px,0)}}
.g-de-bd-motes::after{inset:0 0 -170px 0;
  background:radial-gradient(circle, var(--de-lb) 0 1px, transparent 2px);
  background-size:140px 170px;background-position:40px 60px;
  animation:g-de-motes-b calc(var(--de-drift) * 1.9) linear infinite}
@keyframes g-de-motes-b{from{transform:translate3d(0,0,0)}to{transform:translate3d(0,-170px,0)}}

/* the dark: depth dims the room (newDeepest steps it), and a payout flash */
.g-de-bd-dark{inset:0;opacity:calc(var(--de-n-depth,0) * .62);background:#000;animation:none}
.g-de-bd-flash{inset:0;opacity:0;animation:none;
  background:radial-gradient(60% 60% at 50% 50%, var(--de-la), transparent 70%)}
.g-de-bd-flash.g-de-on{animation:g-de-pressure .62s ease-out 1}
@keyframes g-de-pressure{0%{opacity:var(--de-n-pay,.5);transform:scale(.7)}100%{opacity:0;transform:scale(1.25)}}
.g-de-bd-flash.g-de-deep{animation:g-de-pressure-deep 1.3s ease-out 1}
@keyframes g-de-pressure-deep{0%{opacity:.8;transform:scale(.5)}40%{opacity:.45}100%{opacity:0;transform:scale(1.4)}}
/* the royal: the pool floods gold while the ceiling ceremony plays */
.g-de-bd-royal{inset:0;opacity:0;animation:none;transition:opacity 1.2s ease;
  background:radial-gradient(80% 70% at 50% 30%, color-mix(in srgb, var(--gold), transparent 45%), transparent 72%)}
.g-de-stage.g-de-royal .g-de-bd-royal{opacity:1;animation:g-de-royal 2.2s ease-in-out infinite alternate}
@keyframes g-de-royal{from{opacity:.7}to{opacity:1}}
/* vignette, and the bell tightens it */
.g-de-bd-vig{inset:0;opacity:1;animation:none;
  background:radial-gradient(120% 100% at 50% 46%, transparent 52%, rgba(0,0,0,.6) 100%)}
.g-de-stage[data-phase="bell"] .g-de-bd-vig{animation:g-de-vig 1s ease-in-out infinite alternate}
@keyframes g-de-vig{from{opacity:.85}to{opacity:1}}
/* a dim-out never cuts: the rig sighs down to a whisper */
.g-de-stage.g-de-out .g-de-bd{transition:opacity 1.6s ease;opacity:calc(var(--de-n-a-veil,.6) * .35)}
.g-de-stage.g-de-out .g-de-bd-dark,.g-de-stage.g-de-out .g-de-bd-vig{opacity:1}

/* ---- HUD: four chips above the water ------------------------------------- */
.g-de-hud{position:relative;z-index:2;display:flex;flex-wrap:wrap;align-items:center;
  justify-content:center;gap:8px 12px;font-family:var(--mono);font-size:11px;
  letter-spacing:.14em;text-transform:uppercase;color:var(--ink-faint)}
.g-de-chip{display:inline-flex;align-items:center;gap:6px;padding:4px 12px;border-radius:999px;
  border:1px solid color-mix(in srgb, var(--lav), var(--line) 55%);
  background:color-mix(in srgb, var(--panel), transparent 40%);
  color:color-mix(in srgb, var(--ink-dim), var(--lav) 30%);
  font-variant-numeric:tabular-nums;transition:color .3s ease,border-color .3s ease,box-shadow .3s ease}
.g-de-chip.g-de-depth{border-color:var(--de-t-rim,var(--lav));color:var(--de-t-rim,var(--lav));
  box-shadow:0 0 12px color-mix(in srgb, var(--de-t-rim,var(--lav)), transparent 70%)}
.g-de-chip.g-de-score{color:var(--ink)}
.g-de-chip.g-de-chain{border-color:var(--pink);color:var(--pink);
  box-shadow:0 0 12px color-mix(in srgb, var(--pink), transparent 60%)}
/* FREE SWIM: the Surface chip is a REAL button - it resets the UA button box,
   wears the chip skin, and keeps a visible focus ring (Law VI: the way out of a
   free swim is never dim, never moved, and always reachable from the keyboard). */
.g-de-chip.g-de-surface{appearance:none;-webkit-appearance:none;font:inherit;letter-spacing:inherit;
  text-transform:inherit;cursor:pointer;border-color:var(--pink);
  color:color-mix(in srgb, var(--pink), var(--ink) 30%);
  background:color-mix(in srgb, var(--panel), var(--pink) 12%);
  box-shadow:0 0 10px color-mix(in srgb, var(--pink), transparent 78%);
  animation:g-de-surfacechip 3.4s ease-in-out infinite alternate}
.g-de-chip.g-de-surface:hover{color:var(--ink);border-color:var(--gold);
  background:color-mix(in srgb, var(--panel), var(--pink) 22%)}
.g-de-chip.g-de-surface:focus-visible{outline:2px solid var(--gold);outline-offset:3px}
.g-de-chip.g-de-surface:active{transform:translateY(1px)}
.g-de-chip.g-de-surface:disabled{cursor:default;opacity:.45;animation:none}
@keyframes g-de-surfacechip{
  from{box-shadow:0 0 8px color-mix(in srgb, var(--pink), transparent 82%)}
  to{box-shadow:0 0 18px color-mix(in srgb, var(--pink), transparent 58%)}}
/* the free swim's clock counts UP, so it never goes gold; it just sits calm */
.g-de-stage[data-phase="bell"] .g-de-clock{border-color:var(--gold);color:var(--gold);
  animation:g-de-bellchip 1s ease-in-out infinite alternate}
@keyframes g-de-bellchip{from{box-shadow:0 0 6px rgba(240,194,75,.3)}to{box-shadow:0 0 16px rgba(240,194,75,.7)}}

/* ---- the bench: the board fills the frame -------------------------------- */
.g-de-bench{position:relative;z-index:1;flex:1;min-height:0;width:100%;
  display:flex;justify-content:center;align-items:center;
  /* pass 2: the lean (casino.slide / bump set --de-bench-lean-x/y here; a
     spring curve carries it there and back). BUG FIX 2026-08-25: this pair
     used to be --de-lean-x/y, the same names the tiles read - see the header. */
  transform:perspective(1200px) rotateY(calc(var(--de-bench-lean-x,0) * 2.2deg)) rotateX(calc(var(--de-bench-lean-y,0) * -2.2deg))
    translate3d(calc(var(--de-bench-lean-x,0) * 6px), calc(var(--de-bench-lean-y,0) * 6px), 0);
  transition:transform .36s cubic-bezier(.2,1.6,.35,1)}
/* ---- 15 SOMETHING BELOW (the seep's Deep End special) --------------------
   A soft-edged shape drifting once through the shallows, BEHIND the tiles.
   Opacity and transform only, never past .12, and the soft edge is a radial
   gradient rather than a blur - a live filter over a stage that already carries
   decoded loops is a whole GPU pass per frame, and this class is the one that
   measured that. Parented into .g-de-backdrop, which is z 0 under the board's
   z 1 and pointer-events:none for itself and every child. No forwards fill:
   the engine removes the node on the tell's last frame. */
.g-de-seep{position:absolute;left:0;bottom:9%;width:46%;height:5.5%;min-height:16px;
  border-radius:50%;pointer-events:none;z-index:0;opacity:0;
  background:radial-gradient(closest-side,rgba(2,6,9,.95),rgba(2,6,9,0));
  animation:g-de-below var(--de-seep-ms,1600ms) ease-in-out 1}
@keyframes g-de-below{
  0%{transform:translateX(-60%);opacity:0}
  22%{opacity:.12}
  74%{opacity:.12}
  100%{transform:translateX(250%);opacity:0}}
.g-de-stage[data-reduced="1"] .g-de-seep{animation:none;opacity:0}

.g-de-board{position:relative;z-index:1;width:var(--de-board);height:var(--de-board);
  --de-tile:calc((var(--de-board) - (var(--de-n,4) - 1) * var(--de-gap)) / var(--de-n,4));
  --de-step:calc(var(--de-tile) + var(--de-gap));
  /* pass 3 - THE HAND: how far a full lean carries a tile (11% of a step, halved after the owner playtest), and
     the damping factor the blocked board and reduced motion turn down */
  --de-grab-max:calc(var(--de-step) * .11);
  --de-grab-k:1;
  display:grid;grid-template-columns:repeat(var(--de-n,4),1fr);grid-template-rows:repeat(var(--de-n,4),1fr);
  gap:var(--de-gap);padding:0;border-radius:14px;touch-action:none;
  -webkit-user-select:none;user-select:none;
  transition:filter .8s ease,opacity .8s ease}
/* pass 2 - THE WALL: a move that slides nothing shakes the board 5px into the
   blocked wall and back (2-3 oscillations, 220ms). The casino flashes the edge. */
.g-de-board.g-de-bump-left{animation:g-de-bumpl .22s ease-out 1}
.g-de-board.g-de-bump-right{animation:g-de-bumpr .22s ease-out 1}
.g-de-board.g-de-bump-up{animation:g-de-bumpu .22s ease-out 1}
.g-de-board.g-de-bump-down{animation:g-de-bumpd .22s ease-out 1}
@keyframes g-de-bumpl{0%{transform:none}25%{transform:translateX(-5px)}55%{transform:translateX(3px)}80%{transform:translateX(-1.5px)}100%{transform:none}}
@keyframes g-de-bumpr{0%{transform:none}25%{transform:translateX(5px)}55%{transform:translateX(-3px)}80%{transform:translateX(1.5px)}100%{transform:none}}
@keyframes g-de-bumpu{0%{transform:none}25%{transform:translateY(-5px)}55%{transform:translateY(3px)}80%{transform:translateY(-1.5px)}100%{transform:none}}
@keyframes g-de-bumpd{0%{transform:none}25%{transform:translateY(5px)}55%{transform:translateY(-3px)}80%{transform:translateY(1.5px)}100%{transform:none}}
.g-de-cell{position:relative;border-radius:10px;
  background:color-mix(in srgb, var(--panel), black 30%);
  box-shadow:inset 0 2px 8px rgba(0,0,0,.5), inset 0 0 0 1px color-mix(in srgb, var(--lav), transparent 86%);
  transition:background-color .26s ease,box-shadow .3s ease}
/* pass 2 - the wake: a vacated cell blinks once on every legal move (the tick
   a merge-less move still pays). The background transition is the reduced-
   motion version; the keyframe rides on top. */
.g-de-cell.is-wake{background:color-mix(in srgb, var(--panel), var(--lav) 24%);animation:g-de-wake .28s ease-out 1}
@keyframes g-de-wake{
  0%{box-shadow:inset 0 0 0 2px color-mix(in srgb, var(--lav), transparent 20%), inset 0 0 20px color-mix(in srgb, var(--lav), transparent 50%)}
  100%{box-shadow:inset 0 2px 8px rgba(0,0,0,.5), inset 0 0 0 1px color-mix(in srgb, var(--lav), transparent 86%)}}
/* pass 2 - STUCK: the wall cells of every direction that would still move
   pulse once after a long stall. A hint, not a solver. */
.g-de-cell.is-hint{background:color-mix(in srgb, var(--panel), var(--lav) 18%);animation:g-de-hint 1.1s ease-in-out 1}
@keyframes g-de-hint{
  0%,100%{box-shadow:inset 0 2px 8px rgba(0,0,0,.5), inset 0 0 0 1px color-mix(in srgb, var(--lav), transparent 86%)}
  30%,80%{box-shadow:inset 0 0 0 2px var(--lav), inset 0 0 26px color-mix(in srgb, var(--lav), transparent 40%)}
  55%{box-shadow:inset 0 0 0 1px color-mix(in srgb, var(--lav), transparent 45%), inset 0 0 10px color-mix(in srgb, var(--lav), transparent 65%)}}

/* ---- tiles: positioned by transform ONLY ---------------------------------- */
.g-de-tile{position:absolute;left:0;top:0;width:var(--de-tile);height:var(--de-tile);
  display:flex;flex-direction:column;align-items:center;justify-content:center;gap:.55em;
  font-size:clamp(7px, calc(var(--de-tile) * .12), 20px);color:var(--de-t-ink,var(--ground));
  pointer-events:none;will-change:transform;z-index:2;
  transform:translate3d(
    calc((var(--c,0) + var(--de-lie-dx,0)) * var(--de-step) + var(--de-lean-x,0) * 6%
      + var(--de-grab-x,0) * var(--de-grab-k,1) * var(--de-grab-max,0px)),
    calc((var(--r,0) + var(--de-lie-dy,0)) * var(--de-step) + var(--de-lean-y,0) * 6%
      + var(--de-grab-y,0) * var(--de-grab-k,1) * var(--de-grab-max,0px)), 0);
  transition:transform 170ms cubic-bezier(.2,.85,.25,1),opacity .22s ease,filter .22s ease}
/* the body: everything that pops or sags lives here, never on the tile */
.g-de-tile::before{content:"";position:absolute;inset:0;z-index:-1;border-radius:10px;
  background:linear-gradient(160deg, var(--de-t-bg,var(--lav)), var(--de-t-bg2,var(--lav)));
  border:1px solid color-mix(in srgb, var(--de-t-rim,var(--lav)), transparent 30%);
  box-shadow:0 8px 22px rgba(0,0,0,.45), inset 0 1px 0 rgba(255,255,255,.12),
    0 0 var(--de-t-glowr,0px) var(--de-t-glow,transparent);
  transform-origin:50% 100%;transition:transform .12s ease,filter .25s ease,box-shadow .3s ease}
/* the ripple: a pressure ring that only runs on a merge */
.g-de-tile::after{content:"";position:absolute;inset:0;border-radius:10px;opacity:0;
  border:2px solid var(--de-t-rim,var(--lav));pointer-events:none}
.g-de-tile.is-merged::after{animation:g-de-ripple .62s ease-out 1}
@keyframes g-de-ripple{0%{opacity:.7;transform:scale(1)}100%{opacity:0;transform:scale(1.45)}}
.g-de-tile.is-merged::before{animation:g-de-sink .42s cubic-bezier(.2,1.3,.4,1) 1}
@keyframes g-de-sink{0%{transform:scale(1.16)}55%{transform:scale(.96)}100%{transform:scale(1)}}
/* pass 2: the merge ceremony is toned so the NEW name reads at once - the
   glyph pops without the white-out, the name pops in scale only */
.g-de-tile.is-merged .g-de-glyph{animation:g-de-glyphpop .42s ease-out 1}
@keyframes g-de-glyphpop{0%{transform:scale(1.25);filter:brightness(1.3)}100%{transform:none;filter:none}}
/* spawn: sinks in from the surface */
.g-de-tile.is-new::before{animation:g-de-spawn .26s cubic-bezier(.2,1.2,.4,1) 1}
.g-de-tile.is-new .g-de-glyph,.g-de-tile.is-new .g-de-name{animation:g-de-spawnink .26s ease-out 1}
@keyframes g-de-spawn{0%{transform:scale(.35);opacity:0}100%{transform:scale(1);opacity:1}}
@keyframes g-de-spawnink{0%{opacity:0}60%{opacity:0}100%{opacity:1}}
/* the victim dissolves where it landed; A removes it after the transition */
.g-de-tile.is-gone{opacity:0;filter:blur(3px);z-index:1}
/* the deepest tile on the board: a heavier rim, and it breathes */
.g-de-tile.is-deepest::before{
  box-shadow:0 10px 26px rgba(0,0,0,.5), 0 0 0 2px var(--de-t-rim,var(--lav)),
    0 0 26px var(--de-t-glow,var(--lav)), 0 0 60px color-mix(in srgb, var(--de-t-glow,var(--lav)), transparent 50%);
  animation:g-de-deepbreath 2.6s ease-in-out infinite alternate}
@keyframes g-de-deepbreath{from{filter:brightness(1)}to{filter:brightness(1.18)}}
/* strain: two equal deepest tiles, adjacent, blocked - the lean comes from
   --de-lean-x/y (casino.js sets them), the glow is the class */
.g-de-tile.is-strain::before{
  box-shadow:0 10px 26px rgba(0,0,0,.5), 0 0 0 2px var(--pink), 0 0 30px color-mix(in srgb, var(--pink), transparent 30%);
  animation:g-de-strain .7s ease-in-out infinite alternate}
@keyframes g-de-strain{from{filter:brightness(1) saturate(1)}to{filter:brightness(1.25) saturate(1.3)}}
/* silt: inert sediment - no light, no name worth reading, a hatched body */
.g-de-tile.is-silt{color:color-mix(in srgb, var(--ink), var(--slate) 35%)}
.g-de-tile.is-silt::before{
  background:repeating-linear-gradient(135deg, color-mix(in srgb, var(--slate), black 45%) 0 4px,
    color-mix(in srgb, var(--slate), black 60%) 4px 8px);
  border-color:color-mix(in srgb, var(--slate), transparent 40%);box-shadow:0 6px 16px rgba(0,0,0,.5);animation:none}
.g-de-tile.is-silt .g-de-glyph{background:radial-gradient(circle, currentColor 0 1.2px, transparent 1.8px);
  background-size:6px 6px;width:2.4em;height:2.4em;border-radius:4px;opacity:.7;filter:none}
.g-de-tile.is-silt .g-de-name{opacity:.55;text-shadow:none;color:color-mix(in srgb, var(--ink), var(--slate) 35%)}

/* ---- pass 2: THE SLIDE - trail, stretch and squash ------------------------ */
/* index.js marks every moving tile .is-sliding + .is-sliding-<dir> + -x|-y for
   the move and sets --de-td (cells travelled). The trail is a child streak on
   the trailing side that grows back toward the origin as the tile flies, then
   fades; the body stretches along the axis in flight and squashes on landing.
   Never on the tile's own transform (the transform law). Merges keep their own
   sink; the sheen + breath are re-listed so a deep tile's loops do not jump. */
.g-de-trail{position:absolute;z-index:-2;opacity:0;pointer-events:none;border-radius:10px;
  --de-tl:calc(var(--de-td,1) * var(--de-step) * .9)}
.g-de-tile.is-sliding-x .g-de-trail{top:18%;height:64%;width:var(--de-tl);animation:g-de-trailx .31s ease-out 1}
.g-de-tile.is-sliding-y .g-de-trail{left:18%;width:64%;height:var(--de-tl);animation:g-de-traily .31s ease-out 1}
.g-de-tile.is-sliding-left .g-de-trail{left:50%;transform-origin:0 50%;
  background:linear-gradient(to right, var(--de-t-rim,var(--lav)), transparent)}
.g-de-tile.is-sliding-right .g-de-trail{right:50%;transform-origin:100% 50%;
  background:linear-gradient(to left, var(--de-t-rim,var(--lav)), transparent)}
.g-de-tile.is-sliding-up .g-de-trail{top:50%;transform-origin:50% 0;
  background:linear-gradient(to bottom, var(--de-t-rim,var(--lav)), transparent)}
.g-de-tile.is-sliding-down .g-de-trail{bottom:50%;transform-origin:50% 100%;
  background:linear-gradient(to top, var(--de-t-rim,var(--lav)), transparent)}
@keyframes g-de-trailx{0%{opacity:0;transform:scaleX(.1)}35%{opacity:.75;transform:scaleX(1)}100%{opacity:0;transform:scaleX(1)}}
@keyframes g-de-traily{0%{opacity:0;transform:scaleY(.1)}35%{opacity:.75;transform:scaleY(1)}100%{opacity:0;transform:scaleY(1)}}
.g-de-tile.is-sliding:not(.is-merged)::before{transform-origin:50% 50%}
.g-de-tile.is-sliding-x:not(.is-merged):not(.is-silt)::before{
  animation:g-de-deepbreath 2.6s ease-in-out infinite alternate, g-de-stretchx .32s cubic-bezier(.3,.7,.3,1) 1}
.g-de-tile.is-sliding-y:not(.is-merged):not(.is-silt)::before{
  animation:g-de-deepbreath 2.6s ease-in-out infinite alternate, g-de-stretchy .32s cubic-bezier(.3,.7,.3,1) 1}
.g-de-tile.is-sliding-x.is-silt::before{animation:g-de-stretchx .32s cubic-bezier(.3,.7,.3,1) 1}
.g-de-tile.is-sliding-y.is-silt::before{animation:g-de-stretchy .32s cubic-bezier(.3,.7,.3,1) 1}
@keyframes g-de-stretchx{0%{transform:scale(1)}40%{transform:scale(1.07,.94)}78%{transform:scale(.965,1.03)}100%{transform:scale(1)}}
@keyframes g-de-stretchy{0%{transform:scale(1)}40%{transform:scale(.94,1.07)}78%{transform:scale(1.03,.965)}100%{transform:scale(1)}}

/* ---- pass 3: THE HAND - press lifts, drag leans, release slides ----------- */
/* index.js puts .g-de-held on the BOARD while a pointer is down and writes
   --de-grab-x/y (-1..1) as the finger travels; the grab is composed into the
   tile's ONE positional transform above, so a release into a real move never
   snaps back first - the offset dying and the new --r/--c landing are the same
   170ms transition. While held the transition is short so the lean tracks the
   cursor; the 170ms returns the moment the class drops (the move rides it).
   Blocked directions damp to a third: the board resists instead of lying about
   a move it will not make (Law I). */
.g-de-board.g-de-held .g-de-tile{transition-duration:60ms,.22s,.22s}
.g-de-board.g-de-grab-blocked{--de-grab-k:.33}
/* the lift: the body rises 2px and brightens. The tile's own transform is not
   touched (the transform law) and no z-order moves - a held board is still the
   same board. The deepest / straining rims keep their own shadow. */
.g-de-board.g-de-held .g-de-tile::before{transform:translateY(-2px) scale(1.03);filter:brightness(1.07)}
.g-de-board.g-de-held .g-de-tile:not(.is-deepest):not(.is-strain)::before{
  box-shadow:0 13px 28px rgba(0,0,0,.5), inset 0 1px 0 rgba(255,255,255,.18),
    0 0 var(--de-t-glowr,0px) var(--de-t-glow,transparent)}

/* ---- pass 2: MEDIA FACES (pass 5: composited cheap) ----------------------- */
/* THE FACE COMPOSITING LAW (pass 5, measured). A face is FOUR boxes per tile
   and there are up to 25 of them. Anything on a face that forces its own
   render surface is paid 25x, every frame:
     - 'isolation:isolate' made every face a surface for the sake of ONE blend.
     - 'mix-blend-mode:color' on the tint made a SECOND one, and a blend surface
       has to read back what is underneath it before it can write.
     - 'filter:' on a <video> is a full GPU pass over a decoded 854x480 frame,
       per tile, per frame - the single most expensive line in the file.
   The trace put ~86 render surfaces on one board and a third of a GPU core on
   compositing them. So: no isolation, no blend, no filter. The tint is now a
   PLAIN ALPHA wash whose opacity is dialled per tier band, and the deep-tier
   desaturate/darken is baked into the ::before wash gradient instead of being
   computed from the picture. The colour still IS the clarity - it is just
   painted over the picture instead of blended into it.
   .g-de-face still clips the media to the body (border-radius + overflow is a
   fast path, no surface of its own); the tile stays overflow-visible for the
   ripple and the trail. index.js adds .is-loaded once the media has a frame. */
.g-de-face{position:absolute;inset:0;z-index:0;border-radius:10px;overflow:hidden;
  pointer-events:none;opacity:0;transition:opacity .45s ease}
.g-de-tile.is-loaded .g-de-face{opacity:1}
.g-de-media{display:block;position:absolute;inset:0;width:100%;height:100%;object-fit:cover;pointer-events:none}
/* THE KEN-BURNS IS FOR STILLS ONLY. A <video> already moves; panning it as
   well bought nothing and cost a composited transform on a live decode. The
   selector is 'img.g-de-media' on purpose - it beats the bare class, so the
   <video> swap in index.js drops the animation with no extra bookkeeping. */
img.g-de-media{animation:g-de-facekb 16s ease-in-out infinite alternate;
  animation-delay:calc(var(--de-kbp,0) * -16s)}
@keyframes g-de-facekb{from{transform:scale(1) translate(0,0)}to{transform:scale(1.1) translate(-2%,1.5%)}}
/* ::before is the WASH: the shallows (1-3) get a pale tier-coloured wash that
   LIGHTENS the picture toward surface light; from tier 4 down it is a dark
   vignette that grows with depth, and from tier 8 it carries the darkening the
   <video> filter used to do. ::after is the flat tier tint over the top. */
.g-de-face::before{content:"";position:absolute;inset:0;z-index:1;opacity:var(--de-f-vig,.55);
  background:var(--de-f-wash, linear-gradient(160deg, var(--de-t-bg,var(--lav)), var(--de-t-bg2,var(--lav))))}
.g-de-face::after{content:"";position:absolute;inset:0;z-index:2;opacity:var(--de-f-tint,.45);
  background:linear-gradient(160deg, var(--de-t-bg,var(--lav)), var(--de-t-bg2,var(--lav)))}
/* THE TINT LADDER. A 'color' blend at .85 kept the picture's luminance; a plain
   alpha at .85 would erase it. These are the alphas that read as the same tier
   hue while the picture survives - shallow tiles stay legible, deep tiles drown
   on purpose, and the step between bands is what the eye reads as depth. */
.g-de-stage [data-tier="4"],.g-de-stage [data-tier="5"],.g-de-stage [data-tier="6"]{--de-f-vig:.38;--de-f-tint:.40;
  --de-f-wash:radial-gradient(80% 80% at 50% 45%, transparent 40%, rgba(0,0,0,.9) 100%)}
.g-de-stage [data-tier="7"],.g-de-stage [data-tier="8"],.g-de-stage [data-tier="9"]{--de-f-vig:.55;--de-f-tint:.50;
  --de-f-wash:radial-gradient(80% 80% at 50% 45%, transparent 35%, rgba(0,0,0,.95) 100%)}
/* 8-11 used to wear filter:saturate(.35) brightness(.62) ON THE <video>. The
   same read, for free: a heavier vignette that starts opaque at the centre
   instead of transparent, plus more tint over it. Grey and dark, no GPU pass. */
.g-de-stage [data-tier="8"],.g-de-stage [data-tier="9"]{--de-f-vig:.68;--de-f-tint:.56;
  --de-f-wash:radial-gradient(80% 80% at 50% 45%, rgba(0,0,0,.34) 28%, rgba(0,0,0,.97) 100%)}
.g-de-stage [data-tier="10"],.g-de-stage [data-tier="11"]{--de-f-vig:.8;--de-f-tint:.62;
  --de-f-wash:radial-gradient(80% 80% at 50% 45%, rgba(0,0,0,.5) 24%, #000 100%)}

/* ---- the glyph: drawn, never typed ---------------------------------------- */
/* The per-tier ring rules are generated above (glyphRules). The base box is a
   1-ring mark so an unknown tier still shows SOMETHING honest. Pass 2: a rim
   badge in the top-left corner, on a dark disc, in a light tier-hued ink. */
.g-de-glyph{position:relative;border-radius:50%;width:1.4em;height:1.4em;
  background:radial-gradient(circle, transparent 0 .5em, currentColor .5em .61em, transparent .61em) rgba(8,6,22,.66);
  filter:drop-shadow(0 0 .2em color-mix(in srgb, currentColor, transparent 55%))}
${glyphRules()}
.g-de-tile .g-de-glyph{position:absolute;left:6%;top:6%;z-index:3;font-size:.74em;
  color:color-mix(in srgb, var(--de-t-rim,var(--lav)), white 50%);
  filter:drop-shadow(0 0 .25em color-mix(in srgb, currentColor, transparent 40%))}
.g-de-tile[data-tier="11"] .g-de-glyph{animation:g-de-eclipse 3s ease-in-out infinite alternate}
@keyframes g-de-eclipse{from{filter:drop-shadow(0 0 .2em currentColor)}to{filter:drop-shadow(0 0 .6em var(--pink))}}
/* pass 2: the numeral badge (top-right) - numbers are not words. Gold on the
   deepest tile. Silt has no number. */
.g-de-num{position:absolute;right:6%;top:6%;z-index:3;font-family:var(--mono);font-weight:700;
  font-size:clamp(8px, calc(var(--de-tile) * .11), 15px);line-height:1;padding:.3em .55em;border-radius:999px;
  background:rgba(8,6,22,.66);color:color-mix(in srgb, var(--de-t-rim,var(--lav)), white 50%);
  border:1px solid color-mix(in srgb, var(--de-t-rim,var(--lav)), transparent 45%);font-variant-numeric:tabular-nums}
.g-de-tile.is-deepest .g-de-num{color:var(--gold);border-color:var(--gold);
  text-shadow:0 0 .5em color-mix(in srgb, var(--gold), transparent 30%)}
.g-de-tile.is-silt .g-de-num{display:none}
/* pass 2: THE NAME is the neon stamp - bold condensed display face, uppercase,
   sized to the tile, white core with a stacked glow in the tier hue, always on
   a dark plate (the contrast law). The FX ladder below rides data-tier. */
.g-de-tile .g-de-name{position:relative;z-index:3;font-family:var(--disp);font-weight:700;
  font-size:clamp(9px, calc(var(--de-tile) * .15), 26px);letter-spacing:.07em;text-transform:uppercase;line-height:1.05;
  padding:.24em .5em .2em;border-radius:.34em;max-width:92%;white-space:nowrap;overflow:hidden;text-overflow:ellipsis;
  color:#fff;opacity:1;background:rgba(8,6,22,.6);
  box-shadow:0 0 0 1px color-mix(in srgb, var(--de-t-rim,var(--lav)), transparent 55%), 0 3px 12px rgba(0,0,0,.5);
  text-shadow:0 0 .1em #fff, 0 0 .3em var(--de-t-rim,var(--lav)), 0 0 .75em var(--de-t-glow,var(--de-t-rim,var(--lav)))}
/* 4-6: double halo + a slow breathe */
.g-de-tile[data-tier="4"] .g-de-name,.g-de-tile[data-tier="5"] .g-de-name,.g-de-tile[data-tier="6"] .g-de-name{
  text-shadow:0 0 .08em #fff, 0 0 .25em var(--de-t-rim), 0 0 .6em var(--de-t-rim), 0 0 1.3em var(--de-t-glow,var(--de-t-rim));
  animation:g-de-namebreathe 3.2s ease-in-out infinite alternate}
@keyframes g-de-namebreathe{from{filter:brightness(1)}to{filter:brightness(1.3)}}
/* 7-9: chromatic split (two offset coloured copies in the shadow stack) + flicker */
.g-de-tile[data-tier="7"] .g-de-name,.g-de-tile[data-tier="8"] .g-de-name,.g-de-tile[data-tier="9"] .g-de-name{
  text-shadow:-.07em 0 rgba(110,232,224,.85), .07em 0 rgba(255,105,180,.9), 0 0 .3em var(--de-t-rim), 0 0 .9em var(--de-t-glow,var(--de-t-rim));
  animation:g-de-nameflicker 2.7s steps(1,end) infinite}
@keyframes g-de-nameflicker{0%,100%{opacity:1}7%{opacity:.78}9%{opacity:1}41%{opacity:.9}43%{opacity:1}71%{opacity:.62}72%{opacity:1}}
/* 10-11: heavy bloom + pulse + a scanline sweep */
.g-de-tile[data-tier="10"] .g-de-name,.g-de-tile[data-tier="11"] .g-de-name{
  text-shadow:0 0 .1em #fff, 0 0 .35em #fff, 0 0 .7em var(--de-t-rim), 0 0 1.4em var(--de-t-glow), 0 0 2.4em var(--de-t-glow);
  animation:g-de-namepulse 1.6s ease-in-out infinite alternate}
@keyframes g-de-namepulse{from{transform:scale(1);filter:brightness(1)}to{transform:scale(1.05);filter:brightness(1.35)}}
/* THE DRIFT LAW, name-plate edition: the CRT bar used to sweep by tweening
   background-position over a 300%-tall gradient. Translating it instead needs a
   clipping box, and the only candidate is .g-de-name itself - which cannot take
   overflow:hidden, because the tier-10/11 FX ladder IS its 2.4em text-shadow
   bloom and a clip would eat it. (A wrapper element is out too: .g-de-name is
   the trickster's one lie target and index.js repaints it with textContent,
   which would delete any child.) So the bar holds still and BREATHES - opacity
   is compositor-only, the raster happens once. */
.g-de-tile[data-tier="10"] .g-de-name::after,.g-de-tile[data-tier="11"] .g-de-name::after{content:"";position:absolute;inset:0;
  pointer-events:none;border-radius:inherit;
  background:linear-gradient(to bottom, transparent 0 42%, rgba(255,255,255,.3) 50%, transparent 58% 100%);
  animation:g-de-scan 2.4s ease-in-out infinite}
@keyframes g-de-scan{0%,100%{opacity:.12}50%{opacity:1}}
/* a merge pops the new name in scale only - readable within the first frames */
.g-de-tile.is-merged .g-de-name{animation:g-de-namepop .42s cubic-bezier(.2,1.3,.4,1) 1}
@keyframes g-de-namepop{0%{transform:scale(1.22)}100%{transform:scale(1)}}

/* ---- the tier ladder: surface light -> blackout --------------------------- */
/* tier 0 = silt (index.js writes data-tier="0" on an inert tile): sediment,
   a pale grey ink that still reads on it */
.g-de-stage [data-tier="0"]{--de-t-bg:color-mix(in srgb, var(--slate), black 45%);--de-t-bg2:color-mix(in srgb, var(--slate), black 60%);
  --de-t-ink:color-mix(in srgb, var(--ink), var(--slate) 35%);--de-t-rim:var(--slate)}
.g-de-stage [data-tier="1"]{--de-t-bg:color-mix(in srgb, var(--lav) 40%, var(--ink));--de-t-bg2:color-mix(in srgb, var(--lav) 55%, var(--ink));
  --de-t-ink:var(--ground);--de-t-rim:color-mix(in srgb, var(--lav) 70%, var(--ink))}
.g-de-stage [data-tier="2"]{--de-t-bg:color-mix(in srgb, var(--lav) 68%, var(--ink));--de-t-bg2:color-mix(in srgb, var(--lav) 84%, var(--ink));
  --de-t-ink:var(--ground);--de-t-rim:var(--lav)}
.g-de-stage [data-tier="3"]{--de-t-bg:var(--lav);--de-t-bg2:color-mix(in srgb, var(--lav) 82%, var(--pink));
  --de-t-ink:var(--ground);--de-t-rim:var(--lav)}
.g-de-stage [data-tier="4"]{--de-t-bg:color-mix(in srgb, var(--lav) 55%, var(--pink));--de-t-bg2:color-mix(in srgb, var(--lav) 30%, var(--pink));
  --de-t-ink:var(--ground);--de-t-rim:color-mix(in srgb, var(--lav) 40%, var(--pink))}
.g-de-stage [data-tier="5"]{--de-t-bg:var(--pink);--de-t-bg2:color-mix(in srgb, var(--pink) 80%, var(--pink-deep));
  --de-t-ink:var(--ground);--de-t-rim:var(--pink);--de-t-glow:color-mix(in srgb, var(--pink), transparent 60%);--de-t-glowr:10px}
.g-de-stage [data-tier="6"]{--de-t-bg:var(--pink-deep);--de-t-bg2:color-mix(in srgb, var(--pink-deep) 70%, var(--panel2));
  --de-t-ink:var(--ink);--de-t-rim:var(--pink);--de-t-glow:color-mix(in srgb, var(--pink-deep), transparent 50%);--de-t-glowr:14px}
.g-de-stage [data-tier="7"]{--de-t-bg:color-mix(in srgb, var(--pink-deep) 55%, var(--panel2));--de-t-bg2:color-mix(in srgb, var(--pink-deep) 30%, var(--panel2));
  --de-t-ink:var(--ink);--de-t-rim:color-mix(in srgb, var(--pink) 60%, var(--lav));--de-t-glow:color-mix(in srgb, var(--pink-deep), transparent 50%);--de-t-glowr:16px}
.g-de-stage [data-tier="8"]{--de-t-bg:color-mix(in srgb, var(--lav) 32%, var(--panel2));--de-t-bg2:color-mix(in srgb, var(--lav) 14%, var(--panel2));
  --de-t-ink:var(--ink);--de-t-rim:var(--lav);--de-t-glow:color-mix(in srgb, var(--lav), transparent 45%);--de-t-glowr:18px}
.g-de-stage [data-tier="9"]{--de-t-bg:color-mix(in srgb, var(--panel2) 70%, black);--de-t-bg2:color-mix(in srgb, var(--panel2) 45%, black);
  --de-t-ink:var(--ink);--de-t-rim:var(--lav);--de-t-glow:color-mix(in srgb, var(--lav), transparent 40%);--de-t-glowr:22px}
.g-de-stage [data-tier="10"]{--de-t-bg:color-mix(in srgb, var(--navy) 55%, black);--de-t-bg2:color-mix(in srgb, var(--navy) 30%, black);
  --de-t-ink:var(--ink);--de-t-rim:var(--pink);--de-t-glow:color-mix(in srgb, var(--pink), transparent 45%);--de-t-glowr:26px}
.g-de-stage [data-tier="11"]{--de-t-bg:color-mix(in srgb, var(--ground) 35%, black);--de-t-bg2:#000;
  --de-t-ink:var(--ink);--de-t-rim:var(--gold);--de-t-glow:color-mix(in srgb, var(--gold), transparent 40%);--de-t-glowr:30px}
/* deep tiles carry a glint on the body (Law III, tiers 8+).
   THE DRIFT LAW, tile edition: this used to be 'g-de-sheen', a
   background-position crawl across a 260%-wide highlight. On a tile the raster
   is small, but it ran on EVERY deep body and it ran forever, and there is no
   box here that could clip a translating layer - the body IS a ::before, and a
   pseudo cannot own a pseudo. So the glint is now FIXED and the motion comes
   from the animations the deep tiles already had: the deepest tile breathes
   (g-de-deepbreath, inherited from the base .is-deepest rule now that this one
   no longer overrides 'animation'), the name runs its own tier FX, and the face
   moves on its own. Adding a per-tile <i> just to keep a crawl that a loaded
   face covers anyway would have spent render objects to save raster. */
.g-de-tile[data-tier="8"]::before,.g-de-tile[data-tier="9"]::before,
.g-de-tile[data-tier="10"]::before,.g-de-tile[data-tier="11"]::before{
  background-image:linear-gradient(115deg, transparent 30%, rgba(255,255,255,.09) 45%, transparent 60%),
    linear-gradient(160deg, var(--de-t-bg,var(--lav)), var(--de-t-bg2,var(--lav)));
  background-size:260% 100%, 100% 100%;background-position:38% 0, 0 0}

/* ---- the shell's stamp, when index.js targets the bench ---------------------
   ceremonies.stamp({target: bench}) appends the stamp IN FLOW, which in a flex
   bench lands it beside the board. Pin it over the board's centre instead (no
   transform: the shell's .pop keyframe owns that property). */
.g-de-bench > .arc-stamp{position:absolute;left:0;right:0;top:44%;margin:0 auto;width:max-content;
  max-width:90%;z-index:5;text-align:center}

/* ---- the flash well: engine one-shots over the board, never a pointer ----- */
.g-de-flashwell{position:absolute;inset:0;overflow:hidden;pointer-events:none;z-index:6}
.g-de-flashwell *{pointer-events:none}

/* ---- proctor line + end card ---------------------------------------------- */
.g-de-msg{position:relative;z-index:2;margin:0;min-height:1.4em;text-align:center;
  font-family:var(--mono);font-size:11px;letter-spacing:.16em;text-transform:uppercase;
  color:var(--ink-dim);transition:opacity .3s ease}
.g-de-msg:empty{opacity:0}
/* the dive report: index.js appends a title, k/v rows (.g-de-end-row > .g-de-end-k
   + .g-de-end-v; the best-dive row carries data-tier) and the standing dare
   (.g-de-end-dare[data-tier]) straight into .g-de-end - so the node IS the
   panel, centred over the dimmed board ([data-phase="ended"]). */
.g-de-end{position:absolute;left:50%;top:50%;transform:translate(-50%,-50%);z-index:8;
  width:min(460px, 92%);display:flex;flex-direction:column;gap:4px;padding:18px 20px 16px;
  border-radius:16px;background:color-mix(in srgb, var(--navy), transparent 6%);
  border:1px solid color-mix(in srgb, var(--lav), var(--line) 50%);
  box-shadow:0 18px 50px rgba(0,0,0,.55), 0 0 34px color-mix(in srgb, var(--lav), transparent 72%);
  animation:g-de-endin .5s ease-out 1}
@keyframes g-de-endin{from{opacity:0;transform:translate(-50%,-46%)}to{opacity:1;transform:translate(-50%,-50%)}}
.g-de-end-title{font-family:var(--disp);font-size:16px;letter-spacing:.14em;text-transform:uppercase;
  margin:0 0 8px;color:var(--ink);text-align:center;text-shadow:0 0 18px color-mix(in srgb, var(--pink), transparent 60%)}
.g-de-end-row{display:flex;justify-content:space-between;align-items:baseline;gap:12px;
  font-family:var(--mono);font-size:12px;letter-spacing:.1em;text-transform:uppercase;color:var(--ink-dim);
  padding:5px 0;border-bottom:1px dashed color-mix(in srgb, var(--line), transparent 20%)}
.g-de-end-k{color:var(--ink-faint)}
.g-de-end-v{color:var(--ink);font-variant-numeric:tabular-nums}
.g-de-end-best{font-size:13px}
.g-de-end-best .g-de-end-v{color:var(--de-t-rim,var(--lav));
  text-shadow:0 0 12px color-mix(in srgb, var(--de-t-rim,var(--lav)), transparent 55%)}
.g-de-end-ceiling .g-de-end-v{color:var(--gold)}
.g-de-end-dare{margin-top:12px;padding:10px 12px;border-radius:10px;text-align:center;
  border:1px solid color-mix(in srgb, var(--gold), transparent 55%);
  background:color-mix(in srgb, var(--gold), transparent 92%)}
.g-de-end-dare .g-de-end-k{display:block;font-family:var(--mono);font-size:10px;letter-spacing:.22em;
  text-transform:uppercase;color:var(--gold)}
.g-de-end-dare .g-de-end-v{display:block;font-family:var(--disp);font-size:22px;letter-spacing:.08em;margin:4px 0 2px;
  color:var(--de-t-rim,var(--gold));text-shadow:0 0 16px color-mix(in srgb, var(--de-t-rim,var(--gold)), transparent 55%)}
.g-de-end-line{margin:2px 0 0;font-size:12px;color:var(--ink-dim)}
.g-de-end .btn{margin:10px 4px 0}

/* ---- phases ----------------------------------------------------------------- */
.g-de-stage[data-phase="briefing"] .g-de-board{filter:brightness(.8) saturate(.85)}
.g-de-stage[data-phase="briefing"] .g-de-tile::before{animation:none}
.g-de-stage[data-phase="resurface"] .g-de-tile{opacity:0;filter:blur(3px) brightness(1.5);
  transition:opacity .9s ease,filter .9s ease,transform 170ms cubic-bezier(.2,.85,.25,1)}
.g-de-stage[data-phase="resurface"] .g-de-board{filter:brightness(1.15)}
.g-de-stage[data-phase="ceiling"] .g-de-board{filter:brightness(1.12) saturate(1.15)}
.g-de-stage[data-phase="ceiling"] .g-de-cell{box-shadow:inset 0 2px 8px rgba(0,0,0,.5),
  inset 0 0 0 1px color-mix(in srgb, var(--gold), transparent 70%)}
.g-de-stage[data-phase="ended"] .g-de-board{filter:saturate(.6) brightness(.7)}
.g-de-stage[data-phase="ended"] .g-de-tile::before{animation:none}

/* ---- CASINO (House Rules, Deck II) --------------------------------------- */
/* THE MARQUEE: a bulb-chase frame around the BOARD (mounted in the bench,
   sized from --de-board so it hugs the square). Dots are gradients, the chase
   was a background-position crawl - four thin bars. THE DRIFT LAW: the bar is
   now a CLIP (overflow:hidden) and the dots live on its ::before, oversized by
   one dot pitch on the trailing edge and translated by exactly that pitch, so
   the chase is compositor-only and the wrap is invisible. pointer-events:none is
   LAW. Pace (--g-de-mqt) and presence (--g-de-mqa) ride the class heat from
   casino.js; the bell and the ceiling turn it gold. */
.g-de-mq{position:absolute;left:50%;top:50%;z-index:0;pointer-events:none;
  width:calc(var(--de-board) + 26px);height:calc(var(--de-board) + 26px);
  transform:translate(-50%,-50%);border-radius:20px;
  opacity:var(--g-de-mqa,.26);transition:opacity .6s ease}
.g-de-mq i{position:absolute;display:block;overflow:hidden}
.g-de-mq i::before{content:"";position:absolute;
  background-image:radial-gradient(circle, var(--de-n-mq,var(--lav)) 2px, transparent 3px)}
.g-de-mq .mq-t,.g-de-mq .mq-b{left:0;right:0;height:7px}
.g-de-mq .mq-l,.g-de-mq .mq-r{top:0;bottom:0;width:7px}
/* the sheet is one dot pitch (18px) longer at BOTH ends: the phase shift of a
   whole pitch is invisible, and the extra covers the vacated end all cycle */
.g-de-mq .mq-t::before,.g-de-mq .mq-b::before{top:0;bottom:0;left:-18px;right:-18px;
  background-size:18px 7px;background-repeat:repeat-x}
.g-de-mq .mq-l::before,.g-de-mq .mq-r::before{left:0;right:0;top:-18px;bottom:-18px;
  background-size:7px 18px;background-repeat:repeat-y}
.g-de-mq .mq-t{top:0}
.g-de-mq .mq-r{right:0}
.g-de-mq .mq-b{bottom:0}
.g-de-mq .mq-l{left:0}
.g-de-mq .mq-t::before{animation:g-de-mqx var(--g-de-mqt,2s) linear infinite var(--g-de-mqp,0s)}
.g-de-mq .mq-r::before{animation:g-de-mqy var(--g-de-mqt,2s) linear infinite var(--g-de-mqp,0s)}
.g-de-mq .mq-b::before{animation:g-de-mqxr var(--g-de-mqt,2s) linear infinite var(--g-de-mqp,0s)}
.g-de-mq .mq-l::before{animation:g-de-mqyr var(--g-de-mqt,2s) linear infinite var(--g-de-mqp,0s)}
@keyframes g-de-mqx{from{transform:translate3d(0,0,0)}to{transform:translate3d(18px,0,0)}}
@keyframes g-de-mqxr{from{transform:translate3d(0,0,0)}to{transform:translate3d(-18px,0,0)}}
@keyframes g-de-mqy{from{transform:translate3d(0,0,0)}to{transform:translate3d(0,18px,0)}}
@keyframes g-de-mqyr{from{transform:translate3d(0,0,0)}to{transform:translate3d(0,-18px,0)}}
.g-de-mq.g-de-mq-bell{--de-n-mq:var(--gold);filter:drop-shadow(0 0 6px rgba(240,194,75,.75))}
.g-de-mq.g-de-mq-flash{animation:g-de-mqflash .6s ease-out 1}
@keyframes g-de-mqflash{
  0%{opacity:1;filter:brightness(var(--g-de-mqf,1.4)) drop-shadow(0 0 10px rgba(184,166,232,.9))}
  100%{opacity:var(--g-de-mqa,.26);filter:none}}
.g-de-mq.g-de-mq-bell.g-de-mq-flash{animation:g-de-mqflashgold .6s ease-out 1}
@keyframes g-de-mqflashgold{
  0%{opacity:1;filter:brightness(var(--g-de-mqf,1.4)) drop-shadow(0 0 14px rgba(240,194,75,.95))}
  100%{opacity:var(--g-de-mqa,.26);filter:drop-shadow(0 0 6px rgba(240,194,75,.75))}}
.g-de-mq.g-de-mq-out{opacity:0;transition:opacity 1.6s ease}

/* THE OVERLAY: casino + trickster decoration ABOVE the board (bubbles, the
   drain, the strain hum, the ghost swipe). No pointer, ever. */
.g-de-cs{position:absolute;inset:0;z-index:3;pointer-events:none;overflow:hidden}
.g-de-cs *{pointer-events:none}
/* a bubble: born at a tile, rises, pops into nothing */
.g-de-bub{position:absolute;left:var(--x,50%);top:var(--y,50%);width:var(--s,10px);height:var(--s,10px);
  margin:calc(var(--s,10px) * -.5) 0 0 calc(var(--s,10px) * -.5);border-radius:50%;
  border:1px solid var(--de-n-bub, color-mix(in srgb, var(--lav), transparent 35%));
  background:radial-gradient(circle at 35% 35%, rgba(255,255,255,.55), rgba(255,255,255,.08) 55%, transparent 70%);
  animation:g-de-rise var(--d,1.4s) ease-out 1 forwards}
@keyframes g-de-rise{
  0%{opacity:0;transform:translate(0,0) scale(.5)}
  15%{opacity:.9}
  60%{transform:translate(calc(var(--wx,0px) * .6), calc(var(--h,120px) * -.6)) scale(1)}
  100%{opacity:0;transform:translate(var(--wx,0px), calc(var(--h,120px) * -1)) scale(1.15)}}
/* the drain: light runs out the bottom of the board on a resurface */
.g-de-cs::before{content:"";position:absolute;left:50%;top:50%;width:var(--de-board);height:var(--de-board);
  transform:translate(-50%,-50%);border-radius:14px;opacity:0;overflow:hidden;
  background:linear-gradient(to bottom, transparent 0%, var(--de-la) 35%, color-mix(in srgb, var(--ink), transparent 60%) 50%,
    var(--de-la) 65%, transparent 100%);background-size:100% 60%;background-repeat:no-repeat}
.g-de-cs.g-de-draining::before{animation:g-de-drain 1.5s ease-in 1}
@keyframes g-de-drain{0%{opacity:0;background-position:0 -60%}20%{opacity:.85}100%{opacity:0;background-position:0 160%}}
/* the hum: a strain makes the whole square glow from inside for a beat */
.g-de-cs::after{content:"";position:absolute;left:50%;top:50%;width:calc(var(--de-board) + 8px);height:calc(var(--de-board) + 8px);
  transform:translate(-50%,-50%);border-radius:16px;opacity:0;
  box-shadow:inset 0 0 70px color-mix(in srgb, var(--pink), transparent 35%), 0 0 40px color-mix(in srgb, var(--pink), transparent 55%)}
.g-de-cs.g-de-hum::after{animation:g-de-hum 1.4s ease-in-out 1}
@keyframes g-de-hum{0%{opacity:0}30%{opacity:.9}60%{opacity:.5}80%{opacity:.8}100%{opacity:0}}
/* pass 2 - THE WALL: the blocked edge of the board flashes (casino.bump). An
   opacity TRANSITION, not an animation, so reduced motion keeps the flash. */
.g-de-wall{position:absolute;left:50%;top:50%;width:calc(var(--de-board) + 10px);height:calc(var(--de-board) + 10px);
  transform:translate(-50%,-50%);border-radius:16px;opacity:0;transition:opacity .3s ease;pointer-events:none;
  --de-wc:color-mix(in srgb, var(--pink), white 25%)}
.g-de-wall.g-de-wall-on{opacity:1;transition-duration:.05s}
.g-de-wall.g-de-wall-left{box-shadow:inset 16px 0 28px -8px var(--de-wc), inset 3px 0 0 var(--de-wc)}
.g-de-wall.g-de-wall-right{box-shadow:inset -16px 0 28px -8px var(--de-wc), inset -3px 0 0 var(--de-wc)}
.g-de-wall.g-de-wall-up{box-shadow:inset 0 16px 28px -8px var(--de-wc), inset 0 3px 0 var(--de-wc)}
.g-de-wall.g-de-wall-down{box-shadow:inset 0 -16px 28px -8px var(--de-wc), inset 0 -3px 0 var(--de-wc)}
/* pass 3 - THE CURRENT: every legal slide sends a flock of chevrons across the
   room the way the board just went. The layer is a sibling of the wall inside
   the overlay (so the stage's .suspended rule freezes it with everything else)
   and casino.js places each arrow: --de-fa-x/y is where it sits in the bench,
   --de-fa-s its size, --de-fa-rot which way it points (0deg = right), --de-fa-a
   how bright it gets (dim over the board, full in the margins), --de-fa-dx/dy
   how far it drifts, --de-fa-d its place in the wave. One keyframe serves all
   four directions because the drift arrives as a px pair. */
.g-de-flow{position:absolute;inset:0;z-index:2;pointer-events:none;overflow:hidden}
.g-de-arrow{position:absolute;left:var(--de-fa-x,50%);top:var(--de-fa-y,50%);
  width:var(--de-fa-s,18px);height:var(--de-fa-s,18px);
  margin:calc(var(--de-fa-s,18px) * -.5) 0 0 calc(var(--de-fa-s,18px) * -.5);
  opacity:0;pointer-events:none;color:var(--de-n-mq,var(--lav));
  filter:drop-shadow(0 0 6px color-mix(in srgb, currentColor, transparent 45%));
  animation:g-de-flow .8s ease-out 1 both;animation-delay:var(--de-fa-d,0ms)}
/* the chevron is drawn, never typed (Law IV): a square wearing two borders,
   turned 45deg. The second one trails half a body behind in the SAME rotated
   space, so a pair reads as a current and not as a dart. */
.g-de-arrow::before,.g-de-arrow::after{content:"";position:absolute;left:0;top:0;width:100%;height:100%;
  border-top:calc(var(--de-fa-s,18px) * .17) solid currentColor;
  border-right:calc(var(--de-fa-s,18px) * .17) solid currentColor;
  border-radius:calc(var(--de-fa-s,18px) * .14)}
.g-de-arrow::before{transform:rotate(var(--de-fa-rot,0deg)) rotate(45deg)}
.g-de-arrow::after{opacity:.42;transform:rotate(var(--de-fa-rot,0deg)) translateX(-54%) rotate(45deg) scale(.88)}
@keyframes g-de-flow{
  0%{opacity:0;transform:translate3d(0,0,0) scale(.82)}
  38%{opacity:var(--de-fa-a,.55)}
  100%{opacity:0;transform:translate3d(var(--de-fa-dx,0px),var(--de-fa-dy,0px),0) scale(1)}}
/* the royal: gold rain of light through the overlay while the ceiling plays */
.g-de-cs.g-de-royal::after{opacity:1;
  box-shadow:inset 0 0 90px color-mix(in srgb, var(--gold), transparent 35%), 0 0 60px color-mix(in srgb, var(--gold), transparent 45%);
  animation:g-de-royalhum 1.1s ease-in-out infinite alternate}
@keyframes g-de-royalhum{from{opacity:.6}to{opacity:1}}

/* ---- TRICKSTER (Deck III) dressing --------------------------------------- */
/* STAT FLICKER: one beat of chromatic static on the lying chip; the deck
   repaints truth on a deadline. */
.g-de-chip.g-de-statlie{color:var(--lav);text-shadow:-1.5px 0 var(--pink),1.5px 0 #6EE8E0;
  animation:g-de-statlie .45s steps(3) 1}
@keyframes g-de-statlie{0%{opacity:1}50%{opacity:.55}100%{opacity:1}}
/* CROOKED CLOCK: no class at all - a number is not motion, and a face that
   looked different would out the trick. */
/* UNRELIABLE LABEL: the name shivers once as it lies; the glyph never moves */
.g-de-tile .g-de-name.g-de-lielabel{animation:g-de-lielabel .6s steps(4) 1}
@keyframes g-de-lielabel{0%{opacity:1}25%{opacity:.6;letter-spacing:.2em}50%{opacity:1}75%{opacity:.7}100%{opacity:.88}}
/* THE MELT: a shallow tile sags and drips while the player stalls; the body
   deforms (transform-origin bottom), the ink slides, a drip forms under it.
   Snaps back on the next move (class removed, .25s ease). */
.g-de-tile.g-de-melt{z-index:3}
.g-de-tile.g-de-melt::before{animation:g-de-meltbody 2.4s ease-in-out infinite alternate;
  filter:saturate(.8) brightness(.95)}
@keyframes g-de-meltbody{from{transform:scaleY(1) skewX(0)}to{transform:scaleY(1.09) skewX(-3deg) translateY(3%);
  border-radius:10px 10px 16px 18px}}
.g-de-tile.g-de-melt .g-de-glyph{animation:g-de-meltink 2.4s ease-in-out infinite alternate}
.g-de-tile.g-de-melt .g-de-name{animation:g-de-meltink 2.4s ease-in-out infinite alternate;animation-delay:.2s}
@keyframes g-de-meltink{from{transform:translateY(0)}to{transform:translateY(14%) skewX(-4deg)}}
.g-de-tile.g-de-melt .g-de-name::after{content:"";position:absolute;left:50%;bottom:-1.1em;width:.45em;height:1.4em;
  margin-left:-.2em;border-radius:40% 40% 50% 50%;background:var(--de-t-bg2,var(--lav));opacity:.85;
  animation:g-de-drip 2.4s ease-in infinite}
@keyframes g-de-drip{0%{transform:scaleY(.2) translateY(0);opacity:0}40%{opacity:.85}100%{transform:scaleY(1.3) translateY(120%);opacity:0}}
/* GHOST SWIPE: a faint cursor echo crosses the board the house's way, over
   and over, until the player moves. It cannot input. */
.g-de-ghost{position:absolute;left:50%;top:50%;width:16px;height:16px;margin:-8px 0 0 -8px;
  border-radius:50%;opacity:0;
  background:radial-gradient(circle, rgba(255,255,255,.85), color-mix(in srgb, var(--lav), transparent 30%) 50%, transparent 72%);
  box-shadow:0 0 14px color-mix(in srgb, var(--lav), transparent 40%);
  animation:g-de-swipe 1.7s ease-in-out infinite}
.g-de-ghost::before{content:"";position:absolute;left:50%;top:50%;width:calc(var(--de-board) * .22);height:3px;
  margin-top:-1.5px;transform-origin:0 50%;transform:rotate(calc(var(--ga,0) * 1deg + 180deg));
  background:linear-gradient(90deg, color-mix(in srgb, var(--lav), transparent 25%), transparent);border-radius:2px}
.g-de-ghost::after{content:"";position:absolute;left:50%;top:50%;width:10px;height:10px;margin:-5px 0 0 -5px;
  border-right:2px solid var(--ink);border-top:2px solid var(--ink);opacity:.8;
  transform:translate(calc(var(--gx,0) * 12px), calc(var(--gy,0) * 12px)) rotate(calc(var(--ga,0) * 1deg - 45deg))}
@keyframes g-de-swipe{
  0%{opacity:0;transform:translate(calc(var(--gx,0) * var(--de-board) * -.18), calc(var(--gy,0) * var(--de-board) * -.18))}
  20%{opacity:.7}
  70%{opacity:.55}
  100%{opacity:0;transform:translate(calc(var(--gx,0) * var(--de-board) * .3), calc(var(--gy,0) * var(--de-board) * .3))}}
/* DEJA RE-DEAL cameo: the lie offsets snap on without a transition (the tile
   "never moved"), then the class drops and the 110ms slide carries it home. */
.g-de-tile.g-de-lie{transition:opacity .22s ease,filter .22s ease}

/* ---- pass 5: THE LITE RUNG (.g-de-lite on the stage) ---------------------
   'de_perf' = lite, or 'auto' after the rAF probe demoted the class, or reduced
   motion / motionLevel <= 1. index.js also puts '.ae-lite' on <html> so the
   ENGINE's own sheet can lighten in the same breath. This is NOT reduced
   motion: the room still moves, it just stops asking the raster and the video
   decoder for things a weak machine cannot pay for.
     - the backdrop pattern sheets hold still (each one is a full-screen layer)
     - the tile faces stop panning (a composited transform per live media node)
     - the deep-tier glint and the name scan hold still
   Under a 4x CPU throttle even an all-stills board fell to 40fps, so the fix
   has to be the WHOLE frame's budget, not just the videos. */
.g-de-lite .g-de-bd-bands::before,.g-de-lite .g-de-bd-bands::after,
.g-de-lite .g-de-bd-lens::before,.g-de-lite .g-de-bd-lens::after,
.g-de-lite .g-de-bd-motes::before,.g-de-lite .g-de-bd-motes::after,
.g-de-lite .g-de-bd-rays::before,.g-de-lite .g-de-bd-vortex::before,
.g-de-lite .g-de-bd-veil::before{animation:none}
.g-de-lite .g-de-bd{animation:none}
.g-de-lite img.g-de-media{animation:none}
.g-de-lite .g-de-tile .g-de-name::after{animation:none;opacity:.35}

/* ---- pass 5: THE TOUCH RUNG (html.ae-touch) ------------------------------
   index.js puts '.ae-touch' on <html> from the pointer, next to '.ae-lite',
   and engine/style.js keeps its own half of it. This is NOT the lite rung and
   it does not stack behind one: a phone on FULL still gets every cut here,
   because these are not quality dials, they are things a mobile GPU - WebKit's
   most of all - cannot pay for at ANY quality. There is no setting; the device
   is the setting, so desktop is untouched to the byte.
   What a phone actually pays for, in this sheet:
     - A FILTER OVER A FACE IS A GPU PASS PER DECODED FRAME. A tile face is a
       live <video> whenever the pool hands back a webm, and blur() over one is
       the worst of them. The two blurs here both land in a WINDOW where the
       board is already at its busiest - the dissolve at the end of a merge and
       the whole-board resurface - so the hitch lands exactly on the beat the
       player is watching. Both go to opacity, which is compositor-only. The
       resurface KEEPS its brightness lift: one filter on a settling board is
       affordable, a blur over up to 25 decodes is not.
     - A BLEND SURFACE HAS TO READ WHAT IS UNDER IT before it can write, and
       the caustic nets are full-screen. On the pool's near-black ground
       'screen' and plain alpha read almost identically, so the light stays and
       the read-back goes (the same trade the engine makes for its washes).
     - BACKDROP-FILTER is the full-screen read-back-and-blur, every frame the
       pressure glitch is up. The sheet's own tint already carries the read.
   THE DRIFT LAW is untouched: every pattern here still travels by transform. */
html.ae-touch .g-de-tile.is-gone{filter:none}
html.ae-touch .g-de-stage[data-phase="resurface"] .g-de-tile{filter:brightness(1.5)}
html.ae-touch .g-de-bd-lens::before,html.ae-touch .g-de-bd-lens::after{
  mix-blend-mode:normal;opacity:.55}
html.ae-touch .g-de-p-glitch.is-on{backdrop-filter:none;-webkit-backdrop-filter:none}
/* the merge ceremony, transform/opacity only: the glyph still POPS, it just
   stops asking for a brightness pass on the frame the new name lands. */
html.ae-touch .g-de-tile.is-merged .g-de-glyph{animation:g-de-glyphpop-t .42s ease-out 1}
@keyframes g-de-glyphpop-t{0%{transform:scale(1.25);opacity:.72}100%{transform:none;opacity:1}}

/* ---- pass 6b: THE TOUCH GPU DIET (html.ae-touch, 2026-08-25) -------------
   The owner's iPhone 13 Pro Max dropped frames on EVERY tile slide, even with
   two tiles on the board. The per-slide bill, retired here (the JS half -
   flock skip, still merge punch, still faces, grab coalescing - is gated on
   the game's own touch flag):
     - .g-de-arrow wore a drop-shadow FILTER: 10-16 filtered nodes per slide,
       up to 32 live. The flock is not even spawned on touch (casino.js); the
       filter goes too for any host that arms ae-touch without the game flag.
     - the bench lean was a perspective/rotateY/rotateX 3D transform - a 360ms
       re-composite of the WHOLE board subtree per slide. Flat 2D shove now.
     - the seven .g-de-bd layers each ran infinite animations (ken-burns +
       pattern drifts). Frozen exactly like .g-de-lite freezes them - the
       static gradients stay, so the pool still LOOKS dressed. (ae-touch is a
       hardware ceiling: a phone on FULL is owed these cuts too, and the auto
       probe samples an idle board so a phone never demoted on its own.)
     - g-de-deepbreath is a FILTER (brightness) animation: always-on on the
       deepest tile, and re-listed on every sliding tile's body. Dropped; the
       stretch/squash (transform-only) is kept on the slide.
     - the marquee flash animated brightness + drop-shadow over a board-sized
       frame on every merge: opacity-only twin.
     - the ken-burns pan on still faces: a composited transform per face, 16s,
       forever. Off.
   Everything here is also either killed or matched by the reduced-motion
   blocks BELOW this one (their animation:none !important kills and later
   equal-specificity bench rules still win), so reduced motion is unchanged. */
html.ae-touch .g-de-arrow{filter:none}
html.ae-touch .g-de-bench{transform:translate3d(calc(var(--de-bench-lean-x,0) * 6px), calc(var(--de-bench-lean-y,0) * 6px), 0)}
html.ae-touch .g-de-bd,
html.ae-touch .g-de-bd-veil::before,
html.ae-touch .g-de-bd-bands::before,html.ae-touch .g-de-bd-bands::after,
html.ae-touch .g-de-bd-lens::before,html.ae-touch .g-de-bd-lens::after,
html.ae-touch .g-de-bd-motes::before,html.ae-touch .g-de-bd-motes::after,
html.ae-touch .g-de-bd-rays::before,html.ae-touch .g-de-bd-vortex::before{animation:none}
html.ae-touch .g-de-tile.is-deepest::before{animation:none}
html.ae-touch .g-de-tile.is-sliding-x:not(.is-merged):not(.is-silt)::before{
  animation:g-de-stretchx .32s cubic-bezier(.3,.7,.3,1) 1}
html.ae-touch .g-de-tile.is-sliding-y:not(.is-merged):not(.is-silt)::before{
  animation:g-de-stretchy .32s cubic-bezier(.3,.7,.3,1) 1}
html.ae-touch .g-de-mq.g-de-mq-flash,
html.ae-touch .g-de-mq.g-de-mq-bell.g-de-mq-flash{animation:g-de-mqflash-t .6s ease-out 1}
@keyframes g-de-mqflash-t{0%{opacity:1}100%{opacity:var(--g-de-mqa,.26)}}
html.ae-touch img.g-de-media{animation:none}
/* pass 7 (the merge choreography): --de-n-depth is a REGISTERED, INHERITING
   number with a transition on the whole stage, so every new-deepest write is a
   window of per-frame inherited-value recompute across the stage subtree. On a
   phone 1.6s of that lands right on the reward beat; .6s keeps the
   step-darker read and drops the window to a third. The transition list is
   restated whole (a transition property does not merge). Desktop keeps 1.6s. */
html.ae-touch .g-de-stage{
  transition:--de-n-depth .6s ease, --de-n-a-bands 3.2s ease, --de-n-a-rays 3.2s ease,
    --de-n-a-lens 3.2s ease, --de-n-a-vortex 3.2s ease, --de-n-a-motes 3.2s ease,
    --de-n-a-veil 3.2s ease, --de-n-tilt 4s ease, --de-n-scale 4s ease}
/* the OS-level reduced-motion query pins the bench flat at LOWER specificity
   than the touch rule above, so the touch sheet restates it (the class-based
   html.arc-reduced rule below already outranks by order). */
@media (prefers-reduced-motion: reduce){
  html.ae-touch .g-de-bench{transform:none;transition:none}
}

/* ---- reduced motion: the mechanic survives, the motion does not ---------- */
html.arc-reduced .g-de-stage *{animation:none !important}
html.arc-reduced .g-de-stage{transition:none}
html.arc-reduced .g-de-bd{opacity:calc(var(--de-n-a-veil,.6) * .5)}
html.arc-reduced .g-de-bd-bands,html.arc-reduced .g-de-bd-rays,html.arc-reduced .g-de-bd-lens,
html.arc-reduced .g-de-bd-vortex,html.arc-reduced .g-de-bd-motes{opacity:0}
html.arc-reduced .g-de-mq{opacity:.16}
html.arc-reduced .g-de-tile{transition:transform 60ms linear,opacity .2s ease}
html.arc-reduced .g-de-tile.is-gone{filter:none}
html.arc-reduced .g-de-bub,html.arc-reduced .g-de-ghost{display:none}
html.arc-reduced .g-de-tile.g-de-melt::before{transform:scaleY(1.04);filter:saturate(.8)}
html.arc-reduced .g-de-tile.g-de-melt .g-de-name::after{display:none}
/* pass 2 under reduced motion: no trail, no lean, no shake (the animation
   kill above), no ken-burns; the wake, the wall flash and the hint survive as
   transitions, the faces are stills (index.js) and the name keeps its glow. */
html.arc-reduced .g-de-trail{display:none}
html.arc-reduced .g-de-bench{transform:none;transition:none}
html.arc-reduced .g-de-tile .g-de-name::after{display:none}
/* pass 3 under reduced motion: the hand still LIFTS (a transition, not travel)
   but nothing leans, and the current is one fade in place - casino.js sets no
   drift and toggles .is-in instead, because the animation is killed above. */
html.arc-reduced .g-de-board,html.arc-reduced .g-de-board.g-de-grab-blocked{--de-grab-k:0}
html.arc-reduced .g-de-arrow{transition:opacity .4s ease}
html.arc-reduced .g-de-arrow.is-in{opacity:var(--de-fa-a,.55)}
@media (prefers-reduced-motion: reduce){
  .g-de-stage *{animation:none !important}
  .g-de-stage{transition:none}
  .g-de-bd{opacity:calc(var(--de-n-a-veil,.6) * .5)}
  .g-de-bd-bands,.g-de-bd-rays,.g-de-bd-lens,.g-de-bd-vortex,.g-de-bd-motes{opacity:0}
  .g-de-mq{opacity:.16}
  .g-de-tile{transition:transform 60ms linear,opacity .2s ease}
  .g-de-tile.is-gone{filter:none}
  .g-de-bub,.g-de-ghost{display:none}
  .g-de-tile.g-de-melt::before{transform:scaleY(1.04);filter:saturate(.8)}
  .g-de-tile.g-de-melt .g-de-name::after{display:none}
  .g-de-trail{display:none}
  .g-de-bench{transform:none;transition:none}
  .g-de-tile .g-de-name::after{display:none}
  .g-de-board,.g-de-board.g-de-grab-blocked{--de-grab-k:0}
  .g-de-arrow{transition:opacity .4s ease}
  .g-de-arrow.is-in{opacity:var(--de-fa-a,.55)}
}

/* ---- narrow / touch ----------------------------------------------------------- */
@media (max-width:560px){
  .g-de-stage{padding:64px 10px 10px;--de-board:min(calc(100dvh - 210px), calc(100vw - 20px))}
  .g-de-hud{gap:6px 8px;font-size:10px}
  .g-de-chip{padding:3px 9px}
}

/* ---- PRESSURE (House Rules, Deck IV) - pass 4 ----------------------------- */
/* THE TREMOR is not here: pressure.js writes the board's individual translate
   property (never its transform - the bump keyframes own that, the bench lean owns the
   bench's, a tile owns its own translate3d) and the chips' transform from one
   rAF loop. What IS here: the three game-local layers (the pinned wheel under
   the board, the punch ring around it, the full-stage glitch wash) and the
   reduced-motion body of a punch (a box-shadow bloom). Every layer is inside
   the stage (.suspended freezes it) and pointer-events:none is LAW. */
@property --de-p-pa{syntax:'<number>';inherits:false;initial-value:.3}
@property --de-p-ga{syntax:'<number>';inherits:false;initial-value:.45}
@property --de-p-spindir{syntax:'<number>';inherits:false;initial-value:1}
/* the juiced chips: a punch is a transform on the chip itself (nothing else in
   the HUD transforms them); the TEXT never changes (ledger honest) */
.g-de-chip.g-de-score,.g-de-chip.g-de-depth,.g-de-chip.g-de-chain{will-change:transform;transform-origin:50% 50%}
/* the bloom: reduced motion's whole punch, and a flash under normal motion
   when the chips are punched while still. Snaps on, eases off on the chip's
   own .3s box-shadow transition. */
.g-de-chip.g-de-p-bloom{transition-duration:.3s,.3s,.05s;
  box-shadow:0 0 0 1px color-mix(in srgb, var(--pink), transparent 20%), 0 0 22px color-mix(in srgb, var(--pink), transparent 35%)}
/* THE PINNED WHEEL (rung 2): inside the bench, BELOW the board (z0 against the
   board's z1), centred by margins so the spin keyframe owns the transform, soft
   radial mask so it reads as light behind the square and not a poster; pressure.js
   sets the image once per class and --de-p-pa / --de-p-spin by heat. Off by
   default; .is-on fades it in over 1.2s; .is-gold tints it for the bell. */
.g-de-p-pin{position:absolute;left:50%;top:50%;z-index:0;pointer-events:none;
  width:calc(var(--de-board) * 1.45);height:calc(var(--de-board) * 1.45);
  margin:calc(var(--de-board) * -.725) 0 0 calc(var(--de-board) * -.725);
  opacity:0;transition:opacity 1.2s ease,filter 1.2s ease;will-change:transform,opacity;
  background-position:center;background-size:contain;background-repeat:no-repeat;
  background-image:conic-gradient(from 0deg, color-mix(in srgb, var(--pink), transparent 45%), transparent 25%,
    color-mix(in srgb, var(--lav), transparent 50%) 50%, transparent 75%, color-mix(in srgb, var(--pink), transparent 45%));
  mix-blend-mode:screen;
  -webkit-mask-image:radial-gradient(circle at 50% 50%, #000 38%, transparent 70%);
  mask-image:radial-gradient(circle at 50% 50%, #000 38%, transparent 70%);
  animation:g-de-p-spin var(--de-p-spin,30s) linear infinite}
.g-de-p-pin.is-on{opacity:var(--de-p-pa,.3)}
.g-de-p-pin.is-gold{filter:sepia(.9) saturate(2) hue-rotate(-14deg) brightness(1.1)}
@keyframes g-de-p-spin{to{transform:rotate(calc(var(--de-p-spindir,1) * 360deg))}}
/* THE RING: a box-shadow bloom hugging the board (z2, under the casino's
   overlay). .is-hit snaps it on and it eases out; .is-deep is the new-deepest /
   royal weight; .is-gold recolours it for the bell and the ceiling. */
.g-de-p-ring{position:absolute;left:50%;top:50%;z-index:2;pointer-events:none;display:block;
  width:calc(var(--de-board) + 14px);height:calc(var(--de-board) + 14px);
  transform:translate(-50%,-50%);border-radius:17px;opacity:0;transition:opacity .5s ease;
  --de-p-rc:var(--pink);
  box-shadow:0 0 0 2px color-mix(in srgb, var(--de-p-rc), transparent 30%), 0 0 34px color-mix(in srgb, var(--de-p-rc), transparent 45%),
    inset 0 0 40px color-mix(in srgb, var(--de-p-rc), transparent 60%)}
.g-de-p-ring.is-hit{opacity:.75;transition-duration:.04s}
.g-de-p-ring.is-deep{opacity:1;
  box-shadow:0 0 0 3px color-mix(in srgb, var(--de-p-rc), transparent 15%), 0 0 60px color-mix(in srgb, var(--de-p-rc), transparent 30%),
    inset 0 0 70px color-mix(in srgb, var(--de-p-rc), transparent 45%)}
.g-de-p-ring.is-gold{--de-p-rc:var(--gold)}
/* THE GLITCH WASH (rung 4): the dtrh drain look, game-local because the engine's
   drain element is index.js's and has no luminosity blend, no dark base and no
   shudder. One node, full stage, over the bench (z1, appended after it) and under
   the HUD; the pool image arrives as an inline background-image; the dark base +
   luminosity blend keep it drained, not a slideshow. Blur-behind ONLY while lit
   (.is-on) so a resting layer costs nothing; the shudder is steps(2) hue + slip on
   THIS layer, never on the board. */
.g-de-p-glitch{position:absolute;inset:0;z-index:1;pointer-events:none;opacity:0;
  transition:opacity .45s ease;will-change:opacity;
  background-color:#0a0410;background-position:center;background-size:cover;background-repeat:no-repeat;
  background-blend-mode:luminosity}
.g-de-p-glitch.is-on{opacity:var(--de-p-ga,.45);backdrop-filter:blur(5px) saturate(.8);-webkit-backdrop-filter:blur(5px) saturate(.8)}
.g-de-p-glitch.is-shudder{animation:g-de-p-shudder .16s steps(2) infinite}
@keyframes g-de-p-shudder{
  0%{transform:translate(0,0);filter:none}
  50%{transform:translate(-1.2%,0);filter:hue-rotate(35deg) saturate(1.5)}
  100%{transform:translate(1.2%,0);filter:hue-rotate(-25deg)}}
/* reduced motion (both gates): the spin and the shudder die with the sheet's
   animation kill above; the pin sits dimmer and still, the glitch keeps its veil
   (a still), the ring and the chip bloom are transitions and survive - they ARE
   the punch under reduced motion. */
html.arc-reduced .g-de-p-pin.is-on{opacity:calc(var(--de-p-pa,.3) * .6)}
html.arc-reduced .g-de-p-glitch.is-on{backdrop-filter:none;-webkit-backdrop-filter:none}
@media (prefers-reduced-motion: reduce){
  .g-de-p-pin.is-on{opacity:calc(var(--de-p-pa,.3) * .6)}
  .g-de-p-glitch.is-on{backdrop-filter:none;-webkit-backdrop-filter:none}
}

/* ---- THE CLASS RULES SHEET (Deck VI, Law IV: drawn, not told) ------------ */
/* Four vignettes over the dark water, cut from the same tile chrome the board
   uses: because the sheet lives INSIDE .g-de-stage, every .g-de-hw-tile picks
   up the depth palette from the shared [data-tier] block below - one place
   owns the colour of a depth, here included. The sheet takes NO pointer events
   (no key is bound and no dive is dealt while it is up, and a stray click must
   never count as "read"); the GO button takes its own back and is the ONLY
   dismissal. z 9 sits above the bench and the HUD and far below the shell's
   suspend treatment (35), which must always be able to cover it. */
.g-de-howto{position:absolute;left:50%;top:50%;transform:translate(-50%,-50%);z-index:9;
  width:min(540px,92vw);max-height:86vh;overflow:auto;pointer-events:none;
  display:flex;flex-direction:column;gap:2px;padding:20px 22px 18px;border-radius:14px;
  color:var(--ink);
  background:linear-gradient(180deg, rgba(12,14,34,.95), rgba(6,8,22,.97));
  border:1px solid color-mix(in srgb, var(--lav), transparent 68%);
  box-shadow:0 30px 80px rgba(0,0,0,.62), 0 0 44px rgba(184,166,232,.12);
  animation:g-de-hw-in .34s ease-out 1}
@keyframes g-de-hw-in{from{opacity:0;transform:translate(-50%,-46%)}
  to{opacity:1;transform:translate(-50%,-50%)}}
.g-de-hw-title{margin:0 0 8px;text-align:center;font-family:var(--disp);
  font-size:clamp(15px,2.3vmin,19px);letter-spacing:.2em;text-transform:uppercase;
  color:var(--lav);text-shadow:0 0 22px rgba(184,166,232,.5)}
.g-de-hw-row{display:flex;align-items:center;gap:16px;padding:10px 2px}
.g-de-hw-row + .g-de-hw-row{border-top:1px dashed color-mix(in srgb, var(--line), transparent 35%)}
.g-de-hw-fig{flex:0 0 auto;display:flex;align-items:center;gap:9px;pointer-events:none}
.g-de-hw-cap{margin:0;flex:1 1 auto;font-size:12.5px;line-height:1.5;color:var(--ink-dim)}

/* one mini tile: the board's own gradient, rim and numeral, at sheet scale */
.g-de-hw-tile{position:relative;display:flex;align-items:center;justify-content:center;
  width:26px;height:26px;border-radius:6px;flex:0 0 auto;
  background:linear-gradient(160deg, var(--de-t-bg,var(--lav)), var(--de-t-bg2,var(--lav)));
  border:1px solid color-mix(in srgb, var(--de-t-rim,var(--lav)), transparent 30%);
  box-shadow:0 2px 8px rgba(0,0,0,.5), 0 0 var(--de-t-glowr,0px) var(--de-t-glow,transparent)}
.g-de-hw-num{font-family:var(--disp);font-size:11px;line-height:1;
  color:var(--de-t-ink,var(--ground))}

/* 1 - the swipe: the pad points, the whole line slides the way it points */
.g-de-hw-pad{position:relative;display:block;width:34px;height:34px;flex:0 0 auto}
.g-de-hw-arrow{position:absolute;width:0;height:0;border:5px solid transparent}
.g-de-hw-arrow.up{left:50%;top:0;margin-left:-5px;border-bottom-color:var(--lav)}
.g-de-hw-arrow.down{left:50%;bottom:0;margin-left:-5px;border-top-color:var(--lav)}
.g-de-hw-arrow.left{left:0;top:50%;margin-top:-5px;border-right-color:var(--lav)}
.g-de-hw-arrow.right{right:0;top:50%;margin-top:-5px;border-left-color:var(--lav);
  filter:drop-shadow(0 0 6px rgba(184,166,232,.9));
  animation:g-de-hw-point 3.2s ease-in-out infinite}
@keyframes g-de-hw-point{0%,14%{opacity:.45;transform:translateX(0)}
  30%,74%{opacity:1;transform:translateX(3px)}100%{opacity:.45;transform:translateX(0)}}
.g-de-hw-line{display:flex;gap:4px;padding:3px;border-radius:7px;
  border:1px solid color-mix(in srgb, var(--line), transparent 30%);
  background:rgba(6,10,26,.6);overflow:hidden}
.g-de-hw-line.slide .g-de-hw-tile{animation:g-de-hw-slide 3.2s cubic-bezier(.22,.9,.3,1) infinite;
  animation-delay:calc(var(--de-hw-i,0) * .05s)}
@keyframes g-de-hw-slide{0%,18%{transform:translateX(0)}
  40%,76%{transform:translateX(9px)}100%{transform:translateX(0)}}

/* 2 - the merge: two equal tiles close, and one comes back a depth further */
.g-de-hw-scene{position:relative;display:block;width:84px;height:34px;flex:0 0 auto}
.g-de-hw-tile.mrg{position:absolute;top:4px}
.g-de-hw-tile.mrg.a{left:2px;animation:g-de-hw-mrgA 3.2s ease-in-out infinite}
.g-de-hw-tile.mrg.b{right:2px;animation:g-de-hw-mrgB 3.2s ease-in-out infinite}
.g-de-hw-tile.mrg.out{left:50%;margin-left:-13px;opacity:0;
  animation:g-de-hw-mrgOut 3.2s ease-out infinite}
/* THE HAND-OFF IS SEAMLESS ON PURPOSE: at every instant either the two source
   tiles or the merged one is on screen, so a still of this sheet (a headless
   shot, a reduced-motion reader, a paused frame) is never a blank figure. */
@keyframes g-de-hw-mrgA{0%,14%{transform:translateX(0);opacity:1}
  40%,43%{transform:translateX(27px);opacity:1}
  44%,71%{transform:translateX(27px);opacity:0}
  71.5%,100%{transform:translateX(0);opacity:1}}
@keyframes g-de-hw-mrgB{0%,14%{transform:translateX(0);opacity:1}
  40%,43%{transform:translateX(-27px);opacity:1}
  44%,71%{transform:translateX(-27px);opacity:0}
  71.5%,100%{transform:translateX(0);opacity:1}}
@keyframes g-de-hw-mrgOut{0%,43%{opacity:0;transform:scale(.7)}
  50%{opacity:1;transform:scale(1.2)}58%,71%{opacity:1;transform:scale(1)}
  71.5%,100%{opacity:0;transform:scale(1)}}

/* 3 - the resurface: the jam drains, the gauge keeps the mark, water refills */
.g-de-hw-grid{display:grid;grid-template-columns:repeat(2,1fr);gap:4px;padding:4px;
  border-radius:7px;border:1px solid color-mix(in srgb, var(--line), transparent 30%);
  background:rgba(6,10,26,.6)}
.g-de-hw-tile.drain{width:22px;height:22px;
  animation:g-de-hw-drain 3.6s ease-in-out infinite;
  animation-delay:calc(var(--de-hw-i,0) * .08s)}
@keyframes g-de-hw-drain{0%,34%{opacity:1;transform:translateY(0)}
  50%{opacity:0;transform:translateY(9px)}
  66%,100%{opacity:1;transform:translateY(0)}}
.g-de-hw-gauge{position:relative;display:block;width:8px;height:34px;flex:0 0 auto;
  border-radius:4px;overflow:hidden;background:rgba(184,166,232,.14);
  border:1px solid color-mix(in srgb, var(--lav), transparent 65%)}
.g-de-hw-gauge i{position:absolute;left:0;right:0;bottom:0;display:block;height:38%;
  background:linear-gradient(180deg,var(--pink),var(--lav));
  animation:g-de-hw-bank 3.6s ease-out infinite}
@keyframes g-de-hw-bank{0%,34%{height:38%}52%{height:62%}100%{height:62%}}

/* 4 - the ceiling: eleven rungs, and the one the ladder stops on */
.g-de-hw-ladder{display:flex;flex-direction:column-reverse;gap:2px;flex:0 0 auto}
.g-de-hw-ladder i{display:block;width:20px;height:4px;border-radius:2px;
  background:color-mix(in srgb, var(--de-t-rim,var(--lav)), transparent 45%);
  opacity:.3;animation:g-de-hw-rung 4s ease-in-out infinite;
  animation-delay:calc(var(--de-hw-i,0) * .12s)}
@keyframes g-de-hw-rung{0%,10%{opacity:.28}40%,86%{opacity:1}100%{opacity:.28}}
.g-de-hw-ladder i.top{height:6px;box-shadow:0 0 10px var(--de-t-glow,var(--gold))}
.g-de-hw-tile.ceil{width:30px;height:30px;
  animation:g-de-hw-ceil 4s ease-in-out infinite}
@keyframes g-de-hw-ceil{0%,40%{opacity:.25;transform:scale(.86)}
  56%,86%{opacity:1;transform:scale(1)}100%{opacity:.25;transform:scale(.86)}}

/* the ONE live thing on the sheet, and the only way past it */
.g-de-hw-go{align-self:center;margin-top:8px;padding:9px 30px;cursor:pointer;
  pointer-events:auto;border-radius:9px;font-family:var(--disp);font-size:13px;
  letter-spacing:.16em;text-transform:uppercase;color:var(--ground);
  background:linear-gradient(180deg,var(--lav),#6E5F9B);
  border:1px solid var(--lav);box-shadow:0 0 22px rgba(184,166,232,.42)}
.g-de-hw-go:hover{box-shadow:0 0 34px rgba(184,166,232,.64)}
.g-de-hw-go:focus-visible{outline:2px solid var(--gold);outline-offset:2px}

/* REDUCED MOTION: the sheet must still TEACH as a still, so the end states of
   the four vignettes are pinned rather than left mid-keyframe. */
html.arc-reduced .g-de-howto,html.arc-reduced .g-de-hw-tile,html.arc-reduced .g-de-hw-arrow,
html.arc-reduced .g-de-hw-gauge i,html.arc-reduced .g-de-hw-ladder i{animation:none !important}
html.arc-reduced .g-de-hw-tile.mrg.a,html.arc-reduced .g-de-hw-tile.mrg.b{opacity:0}
html.arc-reduced .g-de-hw-tile.mrg.out{opacity:1}
html.arc-reduced .g-de-hw-ladder i{opacity:1}
@media (prefers-reduced-motion: reduce){
  .g-de-howto,.g-de-hw-tile,.g-de-hw-arrow,.g-de-hw-gauge i,.g-de-hw-ladder i{animation:none !important}
  .g-de-hw-tile.mrg.a,.g-de-hw-tile.mrg.b{opacity:0}
  .g-de-hw-tile.mrg.out{opacity:1}
  .g-de-hw-ladder i{opacity:1}
}
@media (max-width:640px),(pointer:coarse){
  .g-de-howto{padding:16px 14px 14px}
  .g-de-hw-row{gap:11px;padding:8px 0}
  .g-de-hw-cap{font-size:11.5px}
  .g-de-hw-tile{width:22px;height:22px}
  .g-de-hw-num{font-size:10px}
}
@media (max-height:620px){
  .g-de-hw-row{padding:6px 2px}
  .g-de-hw-title{margin-bottom:4px}
}

/* ---- THE STICKY WAY PAST THE SHEET (shell exits wave) ----
   The card already declared max-height + overflow:auto, but it declared
   pointer-events:none in the same breath - and a box the pointer cannot hit is
   a box the wheel cannot scroll. So the moment the sheet was taller than the
   window, GO sat below a fold that nothing on the page could reach: the class
   was unenterable and nothing said why. The root takes the pointer back
   (nothing on the sheet is live except GO, so this costs the class nothing)
   and GO rides the bottom of the card as a full-width bar for as long as the
   sheet is up. The look is the game's own - the shell's lit arrow board is for
   terminal screens, and this one is a door in, not a door out. */
.g-de-howto{pointer-events:auto}
.g-de-hw-go{position:sticky;bottom:0;z-index:3;align-self:stretch;margin-top:14px}

/* ---- pass 6c: THE TOUCH GPU DIET, second sweep (html.ae-touch) ------------
   Same rung and the same law as passes 5 and 6b above (the device is the
   setting; desktop is untouched to the byte). 6b took the board's biggest
   filter loop - g-de-deepbreath - and the seven backdrop layers. This pass
   takes the ones that were left, all of the same shape: a keyframe that
   repaints a filter, a box-shadow or a text glow every frame, on nodes there
   can be sixteen of at once.
     - the NAME ladder. Tiers 4-6 breathed brightness and tiers 10-11 pulsed
       it. The neon IS the stacked text-shadow, and it is untouched: 4-6 simply
       hold their light, and 10-11 keep the pulse on scale alone. The tier-7-9
       flicker and the 10-11 scan bar were already opacity-only and still run.
     - the STRAIN glow and the tier-11 ECLIPSE glyph: both froze at their base
       frame, which is the LIT one - the strain rim and the glyph halo are
       reads and they stay on the screen, they just stop pumping.
     - the MELT keyframed border-radius under a filtered body, which is a
       repaint of a filtered layer per frame. The sag is transform-only now;
       the sagging ink and the drip were always transform/opacity and stay.
     - the PRESSURE WHEEL span the board under mix-blend-mode:screen, so every
       frame was a fresh read-back-and-blend of a board-sized node. Frozen: the
       wheel still hangs there as light behind the square.
     - the GLITCH SHUDDER keyframed hue-rotate + saturate on a FULL-STAGE layer
       at steps(2), forever, on top of the backdrop-filter 6b already took off.
       The slip stays (transform), the hue churn goes.
   Reduced motion is unchanged: its animation:none !important kills outrank
   every twin named here. --------------------------------------------------- */
/* THE :not() GUARDS ARE LOAD BEARING. These selectors out-specify every rule
   that hands a name or a glyph a DIFFERENT animation for a moment - the merge
   pop, the melt's sliding ink, the trickster's lie shiver - and a plain tier
   override would silently eat all three on touch only. Excluding those states
   lets them fall through to the cascade they already win on desktop. */
html.ae-touch .g-de-tile[data-tier="4"]:not(.g-de-melt):not(.is-merged) .g-de-name:not(.g-de-lielabel),
html.ae-touch .g-de-tile[data-tier="5"]:not(.g-de-melt):not(.is-merged) .g-de-name:not(.g-de-lielabel),
html.ae-touch .g-de-tile[data-tier="6"]:not(.g-de-melt):not(.is-merged) .g-de-name:not(.g-de-lielabel){animation:none}
html.ae-touch .g-de-tile[data-tier="10"]:not(.g-de-melt):not(.is-merged) .g-de-name:not(.g-de-lielabel),
html.ae-touch .g-de-tile[data-tier="11"]:not(.g-de-melt):not(.is-merged) .g-de-name:not(.g-de-lielabel){
  animation:g-de-namepulse-t 1.6s ease-in-out infinite alternate}
@keyframes g-de-namepulse-t{from{transform:scale(1)}to{transform:scale(1.05)}}
html.ae-touch .g-de-tile[data-tier="11"]:not(.g-de-melt):not(.is-merged) .g-de-glyph{animation:none}
html.ae-touch .g-de-tile.is-strain::before{animation:none}
html.ae-touch .g-de-tile.g-de-melt::before{animation:g-de-meltbody-t 2.4s ease-in-out infinite alternate}
@keyframes g-de-meltbody-t{from{transform:scaleY(1) skewX(0)}
  to{transform:scaleY(1.09) skewX(-3deg) translateY(3%)}}
/* the two forever-glow chips freeze at their bright frame, never dark */
html.ae-touch .g-de-chip.g-de-surface{animation:none;
  box-shadow:0 0 18px color-mix(in srgb, var(--pink), transparent 58%)}
html.ae-touch .g-de-stage[data-phase="bell"] .g-de-clock{animation:none;
  box-shadow:0 0 16px rgba(240,194,75,.7)}
/* the pressure layer */
html.ae-touch .g-de-p-pin{animation:none}
html.ae-touch .g-de-p-glitch.is-shudder{animation:g-de-p-shudder-t .16s steps(2) infinite}
@keyframes g-de-p-shudder-t{0%{transform:translate(0,0)}
  50%{transform:translate(-1.2%,0)}100%{transform:translate(1.2%,0)}}
/* the rules sheet: a drop-shadow on the one arrow that moves */
html.ae-touch .g-de-hw-arrow.right{filter:none}

`;

/** Inject once per document. No-op headless (the DOM double has no head). */
export function injectDeepEndStyle() {
  try {
    if (typeof document === 'undefined' || !document.createElement) return false;
    if (document.getElementById && document.getElementById(STYLE_ID)) return false;
    const tag = document.createElement('style');
    tag.id = STYLE_ID;
    tag.textContent = STYLE_TEXT;
    const host = document.head || document.documentElement || document.body;
    if (!host || !host.appendChild) return false;
    host.appendChild(tag);
    if (document._register) document._register(STYLE_ID, tag);   // harness shim
    return true;
  } catch (e) {
    return false;      // a stylesheet must never be the thing that fails a class
  }
}

export default injectDeepEndStyle;
