/* ============================================================================
 * games/anomaly/style.js - the game injects its OWN stylesheet from JS.
 *
 * THE DARKROOM. The class root is a full-viewport stage: a contact sheet (the
 * grid) under a red safelight, an enlarger lamp throwing a cone from above,
 * film strips hanging in the margins, dust in the beam. The casino (casino.js)
 * paints the --an-n-* identity props and mounts the lighting layers; the
 * fallbacks below are the plain darkroom, so a disarmed casino changes nothing
 * but the light. Composition, exactly the DOM contract:
 *
 *   .g-an-stage[data-phase=briefing|round|verdict|ended][data-n=3|4|5]
 *   .g-an-backdrop   the lighting rig (pointer-events:none, z0) - casino layers
 *   .g-an-hud        three chips: round / clock / streak (darkroom timer LEDs)
 *   .g-an-grid       the contact sheet; --an-n -> columns; position:relative
 *   .g-an-tile[data-i] > .g-an-face(.g-an-media)
 *   .g-an-msg / .g-an-flashwell / .g-an-end
 *
 * THE TRUTH LAW (read before touching a rule). CORE applies the anomaly as an
 * INLINE filter / transform on .g-an-face. This sheet therefore writes NO
 * filter, NO transform, NO opacity and NO blend on .g-an-face or .g-an-media -
 * ever, in any state. A base filter here would be replaced by the odd tile's
 * inline one and the difference between "mine" and "theirs" would become a
 * second, unintended delta (a tell, or a lie to the ledger). Everything the
 * darkroom does to a frame - the sprocket border, the frame number, the cold
 * veil of an eliminated frame, the found ring - lives on the TILE's own
 * pseudo-elements and box-shadow, over or around the face, never on it. The
 * tile wrapper is never transformed either (it is the hitbox: Law II).
 *
 * THE FRAME is a contact-sheet frame: a film-black border with sprocket holes
 * along the top and bottom edge (::before, a repeating radial gradient), and a
 * frame NUMBER bottom-right in tiny mono (::after via a CSS counter that starts
 * at the casino's seeded --an-n-frame-start - contact sheets never start at 1).
 * ELIMINATED (.is-out / [data-out]) frames go cold: a blue-grey veil on the
 * tile's ::after and a grey filter ON THE TILE WRAPPER (an eliminated frame is
 * by definition not the odd one - the player just proved it). FOUND
 * (.is-found) frames get a ring bloom in the safelight hue; the rest of the
 * sheet dims under .g-an-grid.is-verdict. At a five-streak the sheet's frame
 * IGNITES (.g-an-stage.g-an-lit / .g-an-grid.is-lit).
 *
 * NOTHING IS EVER STILL (Law III): the safelight breathes, the lamp sways, the
 * dust drifts, the strips hang and drift, the grain crawls, the LEDs glow.
 * REDUCED MOTION twice over (html.arc-reduced + the media query): the
 * breathing and the drifts die, a static safelight stays. .g-an-stage.suspended
 * freezes every animation so CORE can hold the room's breath with the class.
 *
 * THE CLASS-RULES SHEET (Deck VI, Law IV): drawn, not told. CORE builds
 * .g-an-howto > .g-an-hw-title, .g-an-hw-row* > (.g-an-hw-fig + .g-an-hw-cap),
 * .g-an-hw-go. The three figures are DRAWN HERE with pseudo-elements on the
 * fig (by row order 1/2/3 or data-fig=same|find|lie): a mini contact sheet
 * whose frames all glow in step; the same sheet with one frame off and a hand
 * tapping it; the sheet with a wash sweeping over all nine - noise. No raster.
 *
 * THE END CARD (.g-an-end): a print in the developing tray - a dark card with
 * a safelight rim, title, k/v rows (.g-an-end-row > .g-an-end-k + .g-an-end-v),
 * the one line (.g-an-end-line) and the buttons (.btn).
 * ==========================================================================*/

const STYLE_ID = 'g-an-style';

export const STYLE_TEXT = `
/* ---- registered decoration props: numbers interpolate, so a var change
   MORPHS (the journey) instead of snapping ---------------------------------- */
@property --an-n-deep{syntax:'<number>';inherits:true;initial-value:0}
@property --an-n-dust{syntax:'<number>';inherits:true;initial-value:.4}
@property --an-n-lamp-a{syntax:'<number>';inherits:true;initial-value:.5}
@property --an-n-pay{syntax:'<number>';inherits:true;initial-value:.4}

/* ---- the stage: the whole window is the darkroom ------------------------- */
.g-an-stage{position:absolute;inset:0;overflow:hidden;display:flex;flex-direction:column;
  align-items:center;gap:10px;padding:72px 18px 14px;color:var(--ink);
  --an-board:min(calc(100dvh - 232px), calc(100vw - 48px), 720px);
  --an-gap:clamp(6px,1.2vmin,14px);
  --an-n:3;
  --an-hue:var(--an-n-hue,354);
  --an-safe:var(--an-n-safe, hsl(354 88% 52%));
  --an-safe-a:var(--an-n-safe-a, hsl(354 88% 56% / .28));
  --an-breath:var(--an-n-breath,7.5s);
  --an-drift:var(--an-n-drift,24s);
  --an-kb:var(--an-n-kb,28s);
  --an-frame:#0b0a0c;
  --an-paper:#17131a;
  counter-reset:an-frame var(--an-n-frame-start,37);
  transition:--an-n-deep 1.6s ease, --an-n-dust 3.2s ease, --an-n-lamp-a 3s ease;
  background:
    radial-gradient(90% 60% at var(--an-n-lamp-x,50%) -10%, var(--an-safe-a), transparent 62%),
    radial-gradient(80% 50% at 50% 112%, hsl(var(--an-hue) 60% 18% / .5), transparent 60%),
    color-mix(in srgb, var(--ground), black 58%)}
.g-an-stage[data-n="3"]{--an-n:3}
.g-an-stage[data-n="4"]{--an-n:4}
.g-an-stage[data-n="5"]{--an-n:5}
.g-an-stage.suspended *{animation-play-state:paused !important}

/* ---- the backdrop: the casino's lighting rig (decoration only) ----------- */
.g-an-backdrop{position:absolute;inset:0;pointer-events:none;z-index:0;overflow:hidden}
.g-an-backdrop *{pointer-events:none}
.g-an-bd{position:absolute;inset:0;opacity:0}
.g-an-bd::before{content:"";position:absolute;inset:0}
/* the safelight: a red glow from the lamp corner, breathing */
.g-an-bd-safe{opacity:1}
.g-an-bd-safe::before{
  background:radial-gradient(70% 60% at var(--an-n-lamp-x,50%) -8%, hsl(var(--an-hue) 90% 55% / .34), hsl(var(--an-hue) 80% 40% / .12) 40%, transparent 72%);
  animation:g-an-breathe var(--an-breath) ease-in-out infinite alternate}
@keyframes g-an-breathe{from{opacity:.55}to{opacity:1}}
/* the enlarger lamp: a cone of light from the top, swaying a hair */
.g-an-bd-lamp{opacity:var(--an-n-lamp-a,.5)}
.g-an-bd-lamp::before{inset:-10% -20% 0;transform-origin:var(--an-n-lamp-x,50%) 0;
  background:conic-gradient(from calc(180deg + var(--an-n-lamp-tilt,0deg)) at var(--an-n-lamp-x,50%) -4%,
    transparent 0deg 154deg, hsl(var(--an-hue) 70% 70% / .10) 160deg, hsl(var(--an-hue) 80% 78% / .22) 180deg,
    hsl(var(--an-hue) 70% 70% / .10) 200deg, transparent 206deg 360deg);
  -webkit-mask-image:linear-gradient(to bottom, #000 0, #000 55%, transparent 92%);
  mask-image:linear-gradient(to bottom, #000 0, #000 55%, transparent 92%);
  animation:g-an-sway calc(var(--an-drift) * .8) ease-in-out infinite alternate}
@keyframes g-an-sway{from{transform:rotate(-1.6deg)}to{transform:rotate(1.6deg)}}
.g-an-bd-lamp.sweep::before{animation:g-an-sweep .9s ease-in-out 1}
@keyframes g-an-sweep{0%{transform:rotate(-1.6deg)}50%{transform:rotate(4deg);filter:brightness(1.5)}100%{transform:rotate(1.6deg)}}
/* hanging film strips in the margins: frames + sprockets, drifting.
   PATTERNS DRIFT BY TRANSFORM, NEVER BY BACKGROUND-POSITION (web CLAUDE.md trap 36): the
   sheet is oversized by exactly one 56px period on its leading edge and translated by that
   period, so the wrap lands on an identical pixel; the vertical bars are y-invariant. */
.g-an-bd-strips{opacity:.55}
.g-an-bd-strips::before{inset:-56px 0 0 0;
  background:
    repeating-linear-gradient(180deg, transparent 0 6px, rgba(0,0,0,.55) 6px 10px, transparent 10px 56px),
    repeating-linear-gradient(90deg, transparent 0 2.5%, rgba(10,8,12,.9) 2.5% 7.5%, transparent 7.5% 9%, transparent 9% 91%, rgba(10,8,12,.9) 91% 96%, transparent 96% 100%),
    repeating-linear-gradient(180deg, transparent 0 12px, hsl(var(--an-hue) 30% 20% / .35) 12px 48px, transparent 48px 56px);
  background-size:100% 100%, 100% 100%, 100% 100%;
  -webkit-mask-image:linear-gradient(90deg, #000 0 10%, transparent 14%, transparent 86%, #000 90%);
  mask-image:linear-gradient(90deg, #000 0 10%, transparent 14%, transparent 86%, #000 90%);
  animation:g-an-strips var(--an-drift) linear infinite}
@keyframes g-an-strips{from{transform:translate3d(0,0,0)}to{transform:translate3d(0,56px,0)}}
/* the advance (a new sheet) rides the WRAPPER so it never fights the drift's transform */
.g-an-bd-strips.advance{animation:g-an-advance .5s ease-out 1}
@keyframes g-an-advance{from{transform:translateY(-8px)}to{transform:translateY(0)}}
/* dust in the beam: two sheets of motes rising through the light */
.g-an-bd-dust{opacity:var(--an-n-dust,.4)}
.g-an-bd-dust::before{
  background:
    radial-gradient(circle, hsl(var(--an-hue) 70% 82% / .7) 0 1.2px, transparent 2.2px),
    radial-gradient(circle, rgba(240,230,220,.5) 0 .8px, transparent 1.8px);
  background-size:170px 190px, 170px 190px;background-position:0 0, 50px 70px;
  animation:g-an-dust calc(var(--an-drift) * 1.6) linear infinite}
.g-an-bd-dust{-webkit-mask-image:radial-gradient(80% 70% at var(--an-n-lamp-x,50%) 10%, #000 20%, transparent 75%);
  mask-image:radial-gradient(80% 70% at var(--an-n-lamp-x,50%) 10%, #000 20%, transparent 75%)}
.g-an-bd-dust::before{inset:0 0 -190px 0}
@keyframes g-an-dust{from{transform:translate3d(0,0,0)}to{transform:translate3d(0,-190px,0)}}
/* film grain: a crawling speckle, faint */
.g-an-bd-grain{opacity:.16;mix-blend-mode:overlay}
.g-an-bd-grain::before{
  background:
    repeating-radial-gradient(circle at 20% 30%, transparent 0 1px, rgba(255,255,255,.08) 1px 2px, transparent 2px 5px),
    repeating-radial-gradient(circle at 70% 60%, transparent 0 1px, rgba(0,0,0,.12) 1px 2px, transparent 2px 6px);
  background-size:220px 220px, 220px 220px;inset:0 -220px 0 0;
  animation:g-an-grain .5s steps(4) infinite}
@keyframes g-an-grain{from{transform:translate3d(0,0,0)}to{transform:translate3d(-220px,0,0)}}
/* the dark: the journey deepens the room */
.g-an-bd-dark{opacity:calc(var(--an-n-deep,0) * .5);background:#000}
/* THE EXPOSURE: the enlarger flashes on a find (white-pink, 420ms) */
.g-an-bd-flash{opacity:0;background:radial-gradient(60% 60% at 50% 50%, hsl(var(--an-hue) 90% 86% / .9), hsl(var(--an-hue) 90% 70% / .4) 40%, transparent 72%)}
.g-an-bd-flash.on{animation:g-an-flash .42s ease-out 1}
@keyframes g-an-flash{0%{opacity:var(--an-n-pay,.4);transform:scale(.8)}100%{opacity:0;transform:scale(1.2)}}
/* THE COLD: a wrong tap dips the room blue for a beat */
.g-an-bd-cold{opacity:0;background:radial-gradient(80% 80% at 50% 50%, rgba(120,150,220,.28), rgba(60,80,140,.12) 50%, transparent 80%)}
.g-an-bd-cold.on{animation:g-an-cold .52s ease-out 1}
@keyframes g-an-cold{0%{opacity:1}100%{opacity:0}}
/* the royal: the darkroom floods gold */
.g-an-bd-royal{opacity:0;transition:opacity 1.2s ease;
  background:radial-gradient(80% 70% at 50% 30%, color-mix(in srgb, var(--gold), transparent 40%), transparent 72%)}
.g-an-stage.g-an-royal .g-an-bd-royal{opacity:1;animation:g-an-royal 2.2s ease-in-out infinite alternate}
@keyframes g-an-royal{from{opacity:.7}to{opacity:1}}
/* vignette; the bell tightens it */
.g-an-bd-vig{opacity:1;background:radial-gradient(120% 100% at 50% 46%, transparent 50%, rgba(0,0,0,.66) 100%)}
.g-an-stage.g-an-bell .g-an-bd-vig{animation:g-an-vig 1s ease-in-out infinite alternate}
@keyframes g-an-vig{from{opacity:.85}to{opacity:1}}
/* a dim-out never cuts: the rig sighs down to a whisper */
.g-an-stage.g-an-out .g-an-bd{transition:opacity 1.6s ease;opacity:.25}
.g-an-stage.g-an-out .g-an-bd-dark,.g-an-stage.g-an-out .g-an-bd-vig{opacity:1}
/* ken-burns on the whole rig (casino sets .g-an-kb when motion is on) */
.g-an-stage.g-an-kb .g-an-bd-strips,.g-an-stage.g-an-kb .g-an-bd-dust{
  animation:g-an-kb var(--an-kb) ease-in-out infinite alternate}
@keyframes g-an-kb{from{transform:scale(1) translate(0,0)}to{transform:scale(1.05) translate(var(--an-n-kb-x,1.2%),var(--an-n-kb-y,-.8%))}}

/* ---- HUD: darkroom timer LEDs ------------------------------------------- */
.g-an-hud{position:relative;z-index:2;display:flex;flex-wrap:wrap;align-items:center;
  justify-content:center;gap:8px 12px;font-family:var(--mono);font-size:11px;
  letter-spacing:.14em;text-transform:uppercase;color:var(--ink-faint);will-change:translate}
.g-an-chip{display:inline-flex;align-items:center;gap:6px;padding:4px 12px;border-radius:999px;
  border:1px solid color-mix(in srgb, hsl(var(--an-hue) 60% 40%), var(--line) 50%);
  background:color-mix(in srgb, #0c0a0e, transparent 20%);
  color:hsl(var(--an-hue) 85% 72%);text-shadow:0 0 8px hsl(var(--an-hue) 90% 60% / .7);
  font-variant-numeric:tabular-nums;will-change:transform;transform-origin:50% 50%;
  transition:color .3s ease,border-color .3s ease,box-shadow .3s ease}
.g-an-chip.g-an-round{color:var(--ink-dim);text-shadow:none}
.g-an-chip.g-an-streak{border-color:hsl(var(--an-hue) 80% 55%);
  box-shadow:0 0 12px hsl(var(--an-hue) 90% 55% / .35)}
.g-an-stage.g-an-bell .g-an-clock,.g-an-stage[data-phase="bell"] .g-an-clock{border-color:var(--gold);color:var(--gold);
  text-shadow:0 0 8px rgba(240,194,75,.7);animation:g-an-bellchip 1s ease-in-out infinite alternate}
@keyframes g-an-bellchip{from{box-shadow:0 0 6px rgba(240,194,75,.3)}to{box-shadow:0 0 16px rgba(240,194,75,.7)}}
.g-an-stage[data-warn="1"] .g-an-clock{border-color:var(--gold);color:var(--gold);text-shadow:0 0 8px rgba(240,194,75,.7);
  animation:g-an-bellchip 1s ease-in-out infinite alternate}
/* the pressure's reduced-motion punch: a bloom, never a move */
.g-an-chip.g-an-p-bloom,.g-an-hud.g-an-p-bloom{
  box-shadow:0 0 0 1px hsl(var(--an-hue) 90% 70% / .8), 0 0 22px hsl(var(--an-hue) 90% 60% / .6);transition:box-shadow .3s ease}

/* ---- the grid: the contact sheet ---------------------------------------- */
.g-an-grid{position:relative;z-index:1;flex:0 0 auto;
  width:var(--an-board);height:var(--an-board);
  display:grid;grid-template-columns:repeat(var(--an-n),1fr);grid-template-rows:repeat(var(--an-n),1fr);
  gap:var(--an-gap);padding:calc(var(--an-gap) * 1.2);box-sizing:border-box;border-radius:10px;
  background:
    linear-gradient(180deg, rgba(255,255,255,.03), transparent 30%),
    var(--an-paper);
  border:1px solid rgba(255,255,255,.07);
  box-shadow:0 30px 70px rgba(0,0,0,.6), inset 0 0 40px rgba(0,0,0,.5), 0 0 0 1px hsl(var(--an-hue) 50% 30% / .25);
  transition:box-shadow .6s ease,border-color .6s ease}
/* the sheet's label strip: a faint header band with a seeded roll number */
.g-an-grid::before{content:"";position:absolute;left:10px;right:10px;top:-7px;height:0;border-top:1px dashed hsl(var(--an-hue) 60% 55% / .28);pointer-events:none}
/* at a five-streak the frame IGNITES */
.g-an-stage.g-an-lit .g-an-grid,.g-an-grid.is-lit{border-color:hsl(var(--an-hue) 90% 62% / .9);
  box-shadow:0 30px 70px rgba(0,0,0,.6), inset 0 0 40px rgba(0,0,0,.5), 0 0 0 2px hsl(var(--an-hue) 90% 62% / .55), 0 0 46px hsl(var(--an-hue) 90% 55% / .45);
  animation:g-an-ignite 1.4s ease-in-out infinite alternate}
@keyframes g-an-ignite{from{box-shadow:0 30px 70px rgba(0,0,0,.6), inset 0 0 40px rgba(0,0,0,.5), 0 0 0 2px hsl(var(--an-hue) 90% 62% / .45), 0 0 30px hsl(var(--an-hue) 90% 55% / .3)}
  to{box-shadow:0 30px 70px rgba(0,0,0,.6), inset 0 0 40px rgba(0,0,0,.5), 0 0 0 2px hsl(var(--an-hue) 90% 62% / .8), 0 0 60px hsl(var(--an-hue) 90% 55% / .6)}}

/* ---- the tile: one contact-sheet frame ---------------------------------- */
.g-an-tile{position:relative;min-width:0;min-height:0;border-radius:5px;overflow:visible;
  background:var(--an-frame);cursor:pointer;
  padding:9% 4% 11%;box-sizing:border-box;
  box-shadow:0 2px 6px rgba(0,0,0,.6), inset 0 0 0 1px rgba(255,255,255,.05);
  -webkit-tap-highlight-color:transparent;user-select:none;
  transition:box-shadow .25s ease}
.g-an-tile:focus-visible{outline:2px solid var(--gold);outline-offset:2px}
/* sprocket holes along the top and bottom edges (the frame's own ::before) */
.g-an-tile::before{content:"";position:absolute;left:0;right:0;top:0;bottom:0;border-radius:5px;pointer-events:none;
  background:
    radial-gradient(circle at 50% 50%, rgba(40,36,44,.95) 0 28%, transparent 34%) 0 0 / 14% 9% repeat-x,
    radial-gradient(circle at 50% 50%, rgba(40,36,44,.95) 0 28%, transparent 34%) 0 100% / 14% 11% repeat-x;
  background-repeat:repeat-x;opacity:.9}
/* the frame number, bottom-right, tiny mono (a CSS counter from the seeded start) */
.g-an-tile::after{counter-increment:an-frame;content:counter(an-frame);position:absolute;right:5%;bottom:1.5%;
  font:600 clamp(7px,1.05vmin,10px)/1 var(--mono);letter-spacing:.1em;color:hsl(var(--an-hue) 70% 68% / .65);
  pointer-events:none;transition:color .3s ease,opacity .3s ease}
/* THE FACE: the truth. CORE writes its inline filter/transform; this sheet
   writes nothing visual on it - no filter, no transform, no opacity, no blend. */
.g-an-face{position:relative;width:100%;height:100%;overflow:hidden;border-radius:2px;background:#000;display:block}
.g-an-face .g-an-media,.g-an-face img,.g-an-face video{width:100%;height:100%;object-fit:cover;display:block;pointer-events:none}
/* ELIMINATED: the frame goes cold. Grey ON THE TILE WRAPPER (the player just
   proved it is not the odd one) + a blue veil over the whole frame. */
.g-an-tile.is-out,.g-an-tile[data-out],.g-an-tile.is-gone,.g-an-tile.is-cold{filter:grayscale(.85) brightness(.72);cursor:default}
.g-an-tile.is-out::before,.g-an-tile[data-out]::before,.g-an-tile.is-gone::before,.g-an-tile.is-cold::before{
  background:
    linear-gradient(180deg, rgba(150,175,230,.22), rgba(90,110,170,.28)),
    radial-gradient(circle at 50% 50%, rgba(40,36,44,.95) 0 28%, transparent 34%) 0 0 / 14% 9% repeat-x,
    radial-gradient(circle at 50% 50%, rgba(40,36,44,.95) 0 28%, transparent 34%) 0 100% / 14% 11% repeat-x;
  background-repeat:no-repeat,repeat-x,repeat-x;opacity:1;z-index:1}
.g-an-tile.is-out::after,.g-an-tile[data-out]::after,.g-an-tile.is-gone::after,.g-an-tile.is-cold::after{color:rgba(170,190,230,.7)}
/* FOUND: the ring blooms in the safelight hue; the rest of the sheet dims */
.g-an-tile.is-found,.g-an-tile[data-found]{
  box-shadow:0 0 0 2px hsl(var(--an-hue) 90% 72%), 0 0 26px hsl(var(--an-hue) 90% 62% / .8), 0 2px 6px rgba(0,0,0,.6);
  animation:g-an-found .45s ease-out 1}
@keyframes g-an-found{0%{box-shadow:0 0 0 0 hsl(var(--an-hue) 90% 80%), 0 0 0 hsl(var(--an-hue) 90% 62% / 0)}
  40%{box-shadow:0 0 0 3px hsl(var(--an-hue) 90% 80%), 0 0 40px hsl(var(--an-hue) 90% 62% / 1)}
  100%{box-shadow:0 0 0 2px hsl(var(--an-hue) 90% 72%), 0 0 26px hsl(var(--an-hue) 90% 62% / .8)}}
.g-an-grid.is-verdict .g-an-tile:not(.is-found):not([data-found])::before,
.g-an-stage[data-phase="verdict"] .g-an-tile:not(.is-found):not([data-found])::before{
  background:rgba(6,4,8,.55);opacity:1;z-index:1;transition:background .3s ease}
/* GHOST: the tap landed where it WAS (CORE marks the tile it LEFT) - lavender, dashed */
.g-an-tile.is-ghost{box-shadow:0 0 0 2px rgba(184,166,232,.85), 0 0 18px rgba(184,166,232,.55), 0 2px 6px rgba(0,0,0,.6);
  filter:none}
.g-an-tile.is-ghost::before{background:
    linear-gradient(180deg, rgba(184,166,232,.18), rgba(184,166,232,.26)),
    radial-gradient(circle at 50% 50%, rgba(40,36,44,.95) 0 28%, transparent 34%) 0 0 / 14% 9% repeat-x,
    radial-gradient(circle at 50% 50%, rgba(40,36,44,.95) 0 28%, transparent 34%) 0 100% / 14% 11% repeat-x;
  background-repeat:no-repeat,repeat-x,repeat-x;opacity:1;z-index:1;
  border:1px dashed rgba(216,204,255,.9)}
/* REVEAL (a timed-out round: CORE marks where it was) */
.g-an-tile.is-reveal,.g-an-tile[data-reveal]{box-shadow:0 0 0 2px rgba(184,166,232,.9), 0 0 22px rgba(184,166,232,.6)}
/* the sheet between rounds */
.g-an-stage[data-phase="briefing"] .g-an-grid{filter:brightness(.8) saturate(.85)}
.g-an-stage[data-phase="ended"] .g-an-grid{filter:saturate(.6) brightness(.7)}
/* hover shimmer hint: fine pointers only (the dossier's coarse-pointer probe) */
@media (hover:hover) and (pointer:fine){
  .g-an-stage[data-phase="round"] .g-an-tile:not(.is-out):not([data-out]):hover{box-shadow:0 2px 6px rgba(0,0,0,.6), 0 0 0 1px hsl(var(--an-hue) 80% 60% / .6), 0 0 14px hsl(var(--an-hue) 80% 55% / .35)}
}

/* ---- the proctor line + the flash well ---------------------------------- */
.g-an-msg{position:relative;z-index:2;margin:0;min-height:1.4em;text-align:center;
  font-family:var(--mono);font-size:12px;letter-spacing:.08em;color:var(--ink-dim);
  text-shadow:0 0 8px hsl(var(--an-hue) 60% 40% / .5);transition:opacity .3s ease;max-width:min(92vw,720px)}
.g-an-msg:empty{opacity:0}
.g-an-flashwell{position:absolute;inset:0;z-index:2;pointer-events:none;overflow:hidden}
.g-an-flashwell *{pointer-events:none}

/* ---- the end card: a print in the tray ---------------------------------- */
.g-an-end{position:absolute;left:50%;top:50%;transform:translate(-50%,-50%);z-index:8;
  width:min(440px,90vw);max-height:86vh;overflow:auto;padding:20px 22px 18px;border-radius:14px;
  background:linear-gradient(180deg, rgba(24,18,26,.96), rgba(12,9,14,.97));
  border:1px solid hsl(var(--an-hue) 60% 45% / .5);
  box-shadow:0 30px 80px rgba(0,0,0,.6), 0 0 44px hsl(var(--an-hue) 80% 50% / .18), inset 0 0 0 1px rgba(255,255,255,.04);
  font-family:var(--body);color:var(--ink);animation:g-an-endin .5s ease-out 1}
@keyframes g-an-endin{from{opacity:0;transform:translate(-50%,-46%)}to{opacity:1;transform:translate(-50%,-50%)}}
.g-an-end-title{font-family:var(--disp);font-size:16px;letter-spacing:.14em;text-transform:uppercase;
  color:hsl(var(--an-hue) 85% 72%);text-shadow:0 0 18px hsl(var(--an-hue) 90% 60% / .5);margin:0 0 10px;text-align:center}
.g-an-end-row{display:flex;justify-content:space-between;align-items:baseline;gap:12px;
  padding:5px 0;border-bottom:1px dashed rgba(255,255,255,.08);font-size:13px}
.g-an-end-k{color:var(--ink-faint)}
.g-an-end-v{color:var(--ink);font-variant-numeric:tabular-nums;font-family:var(--mono)}
.g-an-end-streak .g-an-end-v{color:hsl(var(--an-hue) 85% 72%)}
.g-an-end-line{margin:12px 0 0;font-size:12px;color:var(--ink-dim);text-align:center;font-style:italic}
.g-an-end .btn{margin:12px 4px 0}

/* ---- THE CLASS-RULES SHEET (Deck VI, Law IV): drawn, not told ----------- */
.g-an-howto{position:absolute;left:50%;top:50%;transform:translate(-50%,-50%);z-index:9;
  width:min(520px,90vw);max-height:86vh;overflow:auto;pointer-events:none;
  display:flex;flex-direction:column;gap:2px;padding:22px 24px 20px;border-radius:16px;
  background:linear-gradient(180deg,rgba(26,20,28,.95),rgba(12,9,14,.97));
  border:1px solid hsl(var(--an-hue) 60% 45% / .5);
  box-shadow:0 30px 80px rgba(0,0,0,.55), 0 0 44px hsl(var(--an-hue) 80% 50% / .14)}
.g-an-hw-title{margin:0 0 8px;text-align:center;font-family:var(--disp);font-size:clamp(16px,2.4vmin,20px);
  letter-spacing:.2em;text-transform:uppercase;color:hsl(var(--an-hue) 85% 72%);
  text-shadow:0 0 22px hsl(var(--an-hue) 90% 60% / .45)}
.g-an-hw-row{display:flex;align-items:center;gap:16px;padding:11px 2px}
.g-an-hw-row + .g-an-hw-row{border-top:1px dashed rgba(255,255,255,.08)}
.g-an-hw-cap{margin:0;flex:1 1 auto;font-size:12.5px;line-height:1.5;color:var(--ink-dim)}
.g-an-hw-fig{position:relative;flex:0 0 auto;display:grid;grid-template-columns:repeat(2,1fr);gap:3px;
  width:58px;height:58px;padding:3px;box-sizing:border-box;border-radius:5px;background:var(--an-paper);
  border:1px solid rgba(255,255,255,.08);box-shadow:0 0 14px hsl(var(--an-hue) 90% 55% / .25);pointer-events:none;
  --hw-cell:hsl(var(--an-hue) 80% 58%)}
.g-an-hw-fig.nine{grid-template-columns:repeat(3,1fr)}
/* every cell is the same loop, in step: they breathe together */
.g-an-hw-cell{display:block;min-width:0;min-height:0;border-radius:2px;background:var(--hw-cell);
  box-shadow:0 0 6px hsl(var(--an-hue) 90% 55% / .5);animation:g-an-hw-step 1.6s ease-in-out infinite alternate}
@keyframes g-an-hw-step{from{filter:brightness(.7)}to{filter:brightness(1.3)}}
/* 1 - THE SAME: nothing else. 2 - THE FIND: one cell is off (another hue) + a hand */
.g-an-hw-fig.find .g-an-hw-cell.odd{background:hsl(calc(var(--an-hue) + 150) 80% 60%);
  box-shadow:0 0 10px hsl(calc(var(--an-hue) + 150) 90% 60% / .9)}
.g-an-hw-fig.same .g-an-hw-cell.odd{background:var(--hw-cell);box-shadow:0 0 6px hsl(var(--an-hue) 90% 55% / .5)}
.g-an-hw-tap{position:absolute;left:50%;top:50%;width:15px;height:19px;margin:-4px 0 0 -2px;z-index:2;
  clip-path:polygon(0 0, 0 82%, 22% 64%, 38% 100%, 50% 94%, 34% 60%, 60% 60%);
  background:linear-gradient(160deg, #fff, #d8ccff);filter:drop-shadow(0 0 6px rgba(255,255,255,.6));
  animation:g-an-hw-tap 1.6s ease-in-out infinite}
@keyframes g-an-hw-tap{0%,55%{transform:translate(10px,10px)}70%{transform:translate(0,0) scale(.9)}80%,100%{transform:translate(0,0)}}
.g-an-hw-tap.finger{clip-path:none;width:12px;height:20px;border-radius:6px 6px 4px 4px;background:linear-gradient(180deg,#fff,#d8ccff)}
/* 3 - THE LIE: a wash sweeps over every cell at once - that is noise */
.g-an-hw-fig.lie .g-an-hw-cell.odd{background:var(--hw-cell);box-shadow:0 0 6px hsl(var(--an-hue) 90% 55% / .5)}
.g-an-hw-fig.lie{overflow:hidden}
/* the sweep is a 3x-wide sheet translated across the clipped figure (trap 36: never background-position) */
.g-an-hw-wash,.g-an-hw-fig.lie::after{content:"";position:absolute;inset:0 0 0 -200%;pointer-events:none;
  background:linear-gradient(100deg, transparent 0 20%, hsl(calc(var(--an-hue) + 60) 90% 65% / .6) 45%, hsl(calc(var(--an-hue) + 120) 90% 65% / .6) 55%, transparent 80% 100%);
  mix-blend-mode:screen;animation:g-an-hw-wash 2.2s linear infinite}
@keyframes g-an-hw-wash{from{transform:translate3d(0,0,0)}to{transform:translate3d(66.7%,0,0)}}
.g-an-hw-go{pointer-events:auto;align-self:center;margin-top:14px;appearance:none;-webkit-appearance:none;cursor:pointer;
  font:700 13px/1 var(--body);letter-spacing:.16em;text-transform:uppercase;color:#0c0a0e;
  padding:12px 26px;border-radius:999px;border:0;
  background:linear-gradient(180deg, hsl(var(--an-hue) 90% 72%), hsl(var(--an-hue) 85% 58%));
  box-shadow:0 0 24px hsl(var(--an-hue) 90% 60% / .55), 0 4px 0 hsl(var(--an-hue) 70% 30%);
  animation:g-an-hw-go 1.8s ease-in-out infinite alternate}
.g-an-hw-go:hover{filter:brightness(1.08)}
.g-an-hw-go:focus-visible{outline:2px solid var(--gold);outline-offset:3px}
.g-an-hw-go:active{transform:translateY(2px);box-shadow:0 0 24px hsl(var(--an-hue) 90% 60% / .55), 0 2px 0 hsl(var(--an-hue) 70% 30%)}
@keyframes g-an-hw-go{from{box-shadow:0 0 14px hsl(var(--an-hue) 90% 60% / .4), 0 4px 0 hsl(var(--an-hue) 70% 30%)}to{box-shadow:0 0 34px hsl(var(--an-hue) 90% 60% / .75), 0 4px 0 hsl(var(--an-hue) 70% 30%)}}

/* ---- reduced motion (both gates): a static safelight, no drift ----------- */
html.arc-reduced .g-an-stage *{animation:none !important}
html.arc-reduced .g-an-stage{transition:none}
html.arc-reduced .g-an-bd-safe::before{opacity:.8}
html.arc-reduced .g-an-bd-lamp{opacity:calc(var(--an-n-lamp-a,.5) * .6)}
html.arc-reduced .g-an-bd-dust,html.arc-reduced .g-an-bd-grain{opacity:0}
html.arc-reduced .g-an-bd-strips{opacity:.35}
html.arc-reduced .g-an-tile.is-found,html.arc-reduced .g-an-tile[data-found]{box-shadow:0 0 0 2px hsl(var(--an-hue) 90% 72%), 0 0 26px hsl(var(--an-hue) 90% 62% / .8)}
html.arc-reduced .g-an-hw-tap{transform:none}
html.arc-reduced .g-an-hw-wash,html.arc-reduced .g-an-hw-fig.lie::after{transform:translate3d(33%,0,0)}
@media (prefers-reduced-motion: reduce){
  .g-an-stage *{animation:none !important}
  .g-an-stage{transition:none}
  .g-an-bd-safe::before{opacity:.8}
  .g-an-bd-lamp{opacity:calc(var(--an-n-lamp-a,.5) * .6)}
  .g-an-bd-dust,.g-an-bd-grain{opacity:0}
  .g-an-bd-strips{opacity:.35}
  .g-an-tile.is-found,.g-an-tile[data-found]{box-shadow:0 0 0 2px hsl(var(--an-hue) 90% 72%), 0 0 26px hsl(var(--an-hue) 90% 62% / .8)}
  .g-an-hw-tap{transform:none}
  .g-an-hw-wash,.g-an-hw-fig.lie::after{transform:translate3d(33%,0,0)}
}

/* ---- narrow / touch -------------------------------------------------------- */
@media (max-width:560px){
  .g-an-stage{padding:64px 10px 10px;--an-board:min(calc(100dvh - 206px), calc(100vw - 20px))}
  .g-an-hud{gap:6px 8px;font-size:10px}
  .g-an-chip{padding:3px 9px}
  .g-an-tile{padding:8% 3% 10%}
  .g-an-tile::after{font-size:7px}
}
`;

/** Inject once per document. No-op headless (the DOM double has no head). */
export function injectAnomalyStyle() {
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

/** The contract's name. */
export const injectAnStyle = injectAnomalyStyle;

/* ---------------------------------------------------------------------------
 * THE CLASS-RULES SHEET, drawn (Deck VI, Law IV). CORE calls
 * showAnomalyHowto({mount, onGo, coarse, t, tier, n}) and prefers this markup
 * over its own fallback; the POLICY (when to show, hideTutorial, howtoTiers)
 * stays CORE's. Three figures: a mini contact sheet whose nine cells breathe
 * in step; the same sheet with ONE cell off and a hand tapping it; the sheet
 * with a wash sweeping over all nine - noise. The GO button is the only live
 * thing on it and the only dismissal. No raster art.
 * ------------------------------------------------------------------------- */
let howtoNode = null;
function mk(tag, cls, text) {
  const n = document.createElement(tag);
  if (cls) n.className = cls;
  if (text != null) n.textContent = String(text);
  return n;
}
export function showAnomalyHowto(o) {
  const opts = o || {};
  const mount = opts.mount;
  const t = typeof opts.t === 'function' ? opts.t : (k, f) => (f == null ? k : f);
  if (typeof document === 'undefined' || !mount || !mount.appendChild) return null;
  hideAnomalyHowto();
  const sheet = mk('div', 'g-an-howto');
  sheet.appendChild(mk('h2', 'g-an-hw-title', t('an_howto_title', 'Class rules')));
  const row = (kind, caption) => {
    const r = mk('div', 'g-an-hw-row');
    const fig = mk('span', 'g-an-hw-fig nine ' + kind);
    fig.setAttribute('data-fig', kind);
    fig.setAttribute('aria-hidden', 'true');
    for (let k = 0; k < 9; k++) fig.appendChild(mk('i', 'g-an-hw-cell' + (k === 4 ? ' odd' : '')));
    if (kind === 'find') fig.appendChild(mk('span', 'g-an-hw-tap' + (opts.coarse ? ' finger' : '')));
    if (kind === 'lie') fig.appendChild(mk('i', 'g-an-hw-wash'));
    r.appendChild(fig);
    r.appendChild(mk('p', 'g-an-hw-cap', caption));
    sheet.appendChild(r);
  };
  row('same', t('an_howto_same', 'Every tile is the same loop, playing in step.'));
  row('find', t('an_howto_find', 'One is not. Tap it. The first tap is the one that counts.'));
  row('lie', t('an_howto_lie', 'The room tints, drifts and glitches every tile at once. That is noise.'));
  const go = mk('button', 'g-an-hw-go', t('an_howto_go', 'Open your eyes'));
  go.type = 'button';
  go.setAttribute('autofocus', '');
  go.addEventListener('click', () => { try { if (typeof opts.onGo === 'function') opts.onGo(); } catch (e) { /* noop */ } });
  sheet.appendChild(go);
  mount.appendChild(sheet);
  howtoNode = sheet;
  try { if (typeof go.focus === 'function') go.focus(); } catch (e) { /* noop */ }
  return sheet;
}
export function hideAnomalyHowto() {
  if (howtoNode) { try { howtoNode.remove(); } catch (e) { /* noop */ } howtoNode = null; }
}

export default injectAnomalyStyle;
