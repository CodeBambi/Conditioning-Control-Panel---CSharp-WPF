/* ============================================================================
 * games/composure/style.js - the class injects its OWN stylesheet from JS.
 *
 * THE STUDIO. A painter's room after hours: one lamp, a plaster wall, an easel
 * carrying a framed canvas that has been cut into tiles and shuffled. The clip
 * lives on the canvas; the frame, the wall and the lamp are the furniture the
 * decks play with. Composition, exactly the DOM contract (index.js builds it,
 * this file paints it):
 *
 *   .g-cp-stage[data-phase][data-mode][data-n]  absolute inset:0; explicit
 *                 ground; padded under the shell's proctor strip; --cp-board
 *                 is the one square every frame-level thing is sized from.
 *                 [data-mode=zen] is the warm room, [data-mode=timed] the cool.
 *   .g-cp-backdrop  the casino's lighting rig (pointer-events:none, z 0):
 *                 wall / lamp / motes / dark / flash / royal / vig layers
 *   .g-cp-hud     four chips: moves, clock, locked, calm
 *   .g-cp-frame   THE EASEL: the wooden frame + mat around the board, the
 *                 stand drawn under it (::after), the marquee + the casino's
 *                 overlay (.g-cp-cs) and the trickster's .g-cp-preview live
 *                 here, around and above the board
 *   .g-cp-board   the gesture surface (the canvas); --cp-n (3|4|5) ->
 *                 --cp-tile / --cp-step
 *   .g-cp-tile    ABSOLUTE, positioned ONLY by its transform from --r / --c
 *   .g-cp-face    the clipped viewport (overflow hidden) holding .g-cp-media
 *   .g-cp-preview the lie layer (pointer-events:none, over the board)
 *   .g-cp-flashwell / .g-cp-msg / .g-cp-end
 *
 * THE TRANSFORM LAW. index.js writes --r / --c and nothing else about a
 * tile's position; this file owns the transform and its 190ms slide (a quick
 * start and a soft SETTLE - the tile has weight, it does not teleport). The
 * transform is positional ONLY: every lift, pulse, sag and shimmer lives on
 * the tile's ::before (the body), ::after (the ring) or its children, never on
 * the tile's own transform, so index's slide is never fought. Decoration vars
 * composed INTO the frame's transform (never a tile's):
 *   --cp-lean-x / --cp-lean-y   -1..1 easel lean toward a move (casino.slide)
 * and into every tile's transform:
 *   --cp-grab-x / --cp-grab-y   -1..1 grab vector on the BOARD (optional; a
 *                               core that writes them on pointermove gets THE
 *                               HAND for free, one that does not loses nothing)
 *
 * THE VIEWPORT. Every tile shows ONE url: .g-cp-media is sized to the whole
 * board and shifted by the tile's HOME row/col (--hr / --hc on the tile, set
 * by index.js) so the face clips exactly its own square of the picture, gaps
 * included - one decoder, N viewports. A core that positions the media with
 * inline object-position instead simply wins the cascade (inline beats this
 * sheet); nothing here is !important on the media.
 *
 * LOCKS AND THE SOLVE. .is-locked dissolves the SEAM: the face grows by half
 * the gap on every side, the body's bevel fades, a one-beat chroma ring runs,
 * and a slow varnish sheen settles on it (Law III). Two locked neighbours
 * close their seam entirely; a locked tile beside a loose one keeps a hair of
 * it. [data-phase=solved] (and the board's own .is-solved, which outlives a
 * phase change) closes every seam, drops every shadow and sends one light
 * sweep across the canvas while the clip plays clean.
 *
 * THE OTHER CORE HOOKS. .g-cp-board.is-bump (240ms, an illegal press) knocks
 * the whole canvas against the easel - chrome, never a tile. The stage's
 * data-wash="1" (a burial wash is up) dips the lamp through --cp-wash-k,
 * freezes the motes, cools the canvas and hides the numerals so the picture is
 * all there is; data-peek="1" (the shell's reveal is held) drops the board a
 * step under the peek sheet and quietens the HUD. There is no .is-stuck:
 * CORE never emits it.
 *
 * THE RESCUE. .is-hint is the skill-floor assist: the tile the baseline solver
 * would move next breathes a lavender ring and lifts a little. Grade-capped by
 * index.js; this file only makes it visible.
 *
 * THE SHEET (Deck VI, Law IV): .g-cp-howto is drawn, not told. index.js
 * builds three rows, each a .g-cp-hw-fig.g-cp-hw-slide|lock|wash span holding
 * three blank parts (i.g-cp-hw-a/-b/-c); this sheet makes the span a mini 3x3
 * board out of gradients and the parts its moving pieces: a tile sliding into
 * the gap under a finger, a tile snapping home with its seam lighting up and
 * closing, a pink wash breathing over a board where a tile keeps sliding.
 * index.js owns the policy and the captions; the GO button is the only live
 * thing on the sheet.
 *
 * THE END CARD: .g-cp-end is the panel; rows are .g-cp-end-row > .g-cp-end-k
 * + .g-cp-end-v; the grade arrives as an OBJECT - .g-cp-end-seal[data-grade]
 * is a wax seal pressed into the card (Deck VI), the shell's stamp ceremony
 * runs over it. [data-zen] on the seal is the pass seal (no letter).
 *
 * DECK CHROME: this sheet paints the STAGE-level hooks the decks toggle
 * (.g-cp-bd-* backdrop layers, .g-cp-bell / .g-cp-royal / .g-cp-out on the
 * stage, the --cp-n-* identity props with plain-studio fallbacks). Each deck
 * injects its OWN sheet for its own nodes (the House Rules law):
 *   casino.js    g-cp-casino-style    .g-cp-mq marquee, .g-cp-cs overlay,
 *                                     glints, the word, the hum
 *   trickster.js g-cp-trickster-style .g-cp-preview ghosts + shudder variants,
 *                                     .g-cp-statlie, .g-cp-melt, .g-cp-ghost
 *   pressure.js  g-cp-pressure-style  .g-cp-p-haze, .g-cp-p-ring, .g-cp-p-bloom
 *
 * NOTHING IS EVER STILL (Law III): the lamp breathes, the motes drift, locked
 * tiles carry a varnish sheen, the marquee crawls (timed) or breathes (zen).
 * REDUCED MOTION twice over (html.arc-reduced + the media query): the slide
 * transition survives, everything else becomes a fade. .g-cp-stage.suspended
 * freezes every animation so index.js can hold the room with the class.
 * ==========================================================================*/

const STYLE_ID = 'g-cp-style';

export const STYLE_TEXT = `
/* ---- registered decoration props: numbers interpolate, so a var change
   glides (the lamp warms, the lean springs) instead of snapping ------------- */
@property --cp-lean-x{syntax:'<number>';inherits:false;initial-value:0}
@property --cp-lean-y{syntax:'<number>';inherits:false;initial-value:0}
@property --cp-grab-x{syntax:'<number>';inherits:true;initial-value:0}
@property --cp-grab-y{syntax:'<number>';inherits:true;initial-value:0}
@property --cp-n-warm{syntax:'<number>';inherits:true;initial-value:0}
@property --cp-n-lamp{syntax:'<number>';inherits:true;initial-value:.7}
@property --cp-n-dark{syntax:'<number>';inherits:true;initial-value:0}
@property --cp-pv-a{syntax:'<number>';inherits:true;initial-value:.82}

/* ---- the stage: the studio after hours ----------------------------------- */
.g-cp-stage{position:absolute;inset:0;overflow:hidden;display:flex;flex-direction:column;
  align-items:center;gap:10px;padding:72px 18px 16px;color:var(--ink);
  --cp-board:min(calc(100dvh - 262px), calc(100vw - 120px), 620px);
  --cp-gap:clamp(3px,.7vmin,8px);
  --cp-mat:clamp(10px,2.2vmin,24px);
  --cp-wood:clamp(8px,1.6vmin,16px);
  --cp-n:3;
  --cp-hue-a:var(--cp-n-hue-a,292);
  --cp-hue-b:var(--cp-n-hue-b,330);
  --cp-lamp-x:var(--cp-n-lamp-x,38%);
  --cp-lamp-y:var(--cp-n-lamp-y,-8%);
  --cp-breath:var(--cp-n-breath,7s);
  --cp-drift:var(--cp-n-drift,26s);
  --cp-la:hsla(var(--cp-hue-a),60%,74%,.30);
  --cp-lb:hsla(var(--cp-hue-b),70%,70%,.20);
  --cp-warmc:hsla(36,80%,70%,.22);
  transition:--cp-n-warm 2.4s ease, --cp-n-lamp 2s ease, --cp-n-dark 1.6s ease;
  background:
    radial-gradient(70% 55% at var(--cp-lamp-x) var(--cp-lamp-y), var(--cp-la), transparent 62%),
    radial-gradient(90% 60% at 50% 112%, var(--cp-lb), transparent 64%),
    color-mix(in srgb, var(--ground), black 34%)}
.g-cp-stage[data-n="3"]{--cp-n:3}
.g-cp-stage[data-n="4"]{--cp-n:4}
.g-cp-stage[data-n="5"]{--cp-n:5}
/* the two moods: zen is warm (amber lamp, slower breath), timed is cool */
.g-cp-stage[data-mode="zen"]{--cp-n-warm:1;--cp-breath:calc(var(--cp-n-breath,7s) * 1.6);
  --cp-la:hsla(34,70%,72%,.30);--cp-lb:hsla(var(--cp-hue-b),55%,66%,.16)}
.g-cp-stage[data-mode="timed"]{--cp-n-warm:0}
.g-cp-stage.suspended *{animation-play-state:paused !important}

/* ---- the backdrop: the casino's lighting rig (decoration only) ----------- */
.g-cp-backdrop{position:absolute;inset:0;pointer-events:none;z-index:0;overflow:hidden}
.g-cp-backdrop *{pointer-events:none}
.g-cp-bd{position:absolute;inset:-6%;opacity:0;
  animation:g-cp-kb var(--cp-n-kb,30s) ease-in-out infinite alternate}
.g-cp-bd::before{content:"";position:absolute;inset:0}
@keyframes g-cp-kb{from{transform:scale(1) translate(0,0)}to{transform:scale(1.05) translate(1.2%,-.9%)}}
/* the wall: plaster by default; the casino picks an archetype per class */
.g-cp-bd-wall{opacity:var(--cp-n-a-wall,.5);animation:none;inset:0}
.g-cp-bd-wall::before{
  background:
    repeating-linear-gradient(0deg, transparent 0 3px, rgba(255,255,255,.012) 3px 4px),
    repeating-linear-gradient(90deg, transparent 0 3px, rgba(255,255,255,.012) 3px 4px),
    radial-gradient(120% 80% at 50% 0%, rgba(255,255,255,.05), transparent 60%)}
.g-cp-stage.g-cp-wall-linen .g-cp-bd-wall::before{
  background:
    repeating-linear-gradient(0deg, transparent 0 2px, rgba(255,255,255,.02) 2px 3px),
    repeating-linear-gradient(90deg, transparent 0 2px, rgba(255,255,255,.02) 2px 3px)}
.g-cp-stage.g-cp-wall-brick .g-cp-bd-wall::before{
  background:
    repeating-linear-gradient(0deg, transparent 0 26px, rgba(0,0,0,.16) 26px 28px),
    repeating-linear-gradient(90deg, transparent 0 58px, rgba(0,0,0,.12) 58px 60px);
  opacity:.7}
.g-cp-stage.g-cp-wall-velvet .g-cp-bd-wall::before{
  background:
    repeating-linear-gradient(135deg, transparent 0 9px, hsla(var(--cp-hue-b),40%,60%,.05) 9px 18px),
    radial-gradient(80% 60% at 50% 0%, hsla(var(--cp-hue-a),40%,60%,.08), transparent 70%)}
/* an asset-chrome wash (Deck VI): a pool still at a whisper, luminosity only */
.g-cp-bd-wall::after{content:"";position:absolute;inset:0;opacity:var(--cp-n-a-asset,0);
  background-image:var(--cp-n-asset,none);background-size:cover;background-position:center;
  mix-blend-mode:luminosity;filter:blur(2px) saturate(.2)}
/* the lamp: one light from above; it BREATHES (Law III) */
.g-cp-bd-lamp{opacity:calc(var(--cp-n-lamp,.7) * var(--cp-wash-k,1));transition:opacity .8s ease}
.g-cp-bd-lamp::before{
  background:
    radial-gradient(55% 46% at var(--cp-lamp-x) var(--cp-lamp-y), color-mix(in srgb, hsla(var(--cp-hue-a),70%,80%,.55), var(--cp-warmc) calc(var(--cp-n-warm,0) * 100%)), transparent 70%),
    conic-gradient(from 160deg at var(--cp-lamp-x) var(--cp-lamp-y), transparent 0 10deg, rgba(255,255,255,.05) 14deg 20deg, transparent 24deg 40deg, rgba(255,255,255,.04) 44deg 47deg, transparent 50deg);
  animation:g-cp-breathe var(--cp-breath) ease-in-out infinite alternate}
@keyframes g-cp-breathe{from{opacity:.62;transform:scale(1)}to{opacity:1;transform:scale(1.04)}}
/* motes: dust turning in the lamplight */
.g-cp-bd-motes{opacity:var(--cp-n-a-motes,.45)}
.g-cp-bd-motes::before{
  background:
    radial-gradient(circle, rgba(255,255,255,.55) 0 1px, transparent 2px),
    radial-gradient(circle, hsla(var(--cp-hue-a),70%,85%,.5) 0 .8px, transparent 1.8px);
  background-size:120px 140px, 170px 190px;background-position:0 0, 50px 70px;
  -webkit-mask-image:radial-gradient(70% 60% at var(--cp-lamp-x) 20%, #000 20%, transparent 80%);
  mask-image:radial-gradient(70% 60% at var(--cp-lamp-x) 20%, #000 20%, transparent 80%);
  animation:g-cp-motes calc(var(--cp-drift) * 1.3) linear infinite}
@keyframes g-cp-motes{to{background-position:14px -140px, 70px -190px}}
/* the dark: the bell / a dim-out deepens it; a payout flash */
.g-cp-bd-dark{inset:0;opacity:calc(var(--cp-n-dark,0) * .6);background:#000;animation:none}
.g-cp-bd-flash{inset:0;opacity:0;animation:none;
  background:radial-gradient(55% 50% at 50% 50%, hsla(var(--cp-hue-b),80%,78%,.8), transparent 70%)}
.g-cp-bd-flash.g-cp-on{animation:g-cp-pay .6s ease-out 1}
@keyframes g-cp-pay{0%{opacity:var(--cp-n-pay,.45);transform:scale(.8)}100%{opacity:0;transform:scale(1.2)}}
.g-cp-bd-flash.g-cp-deep{animation:g-cp-paydeep 1.2s ease-out 1}
@keyframes g-cp-paydeep{0%{opacity:.75;transform:scale(.6)}45%{opacity:.4}100%{opacity:0;transform:scale(1.35)}}
/* the royal: the studio floods gold for the solve */
.g-cp-bd-royal{inset:0;opacity:0;animation:none;transition:opacity 1.2s ease;
  background:radial-gradient(80% 70% at 50% 40%, color-mix(in srgb, var(--gold), transparent 50%), transparent 72%)}
.g-cp-stage.g-cp-royal .g-cp-bd-royal{opacity:1;animation:g-cp-royal 2.2s ease-in-out infinite alternate}
@keyframes g-cp-royal{from{opacity:.65}to{opacity:1}}
/* zen royal: warm, not gold - the lamp simply comes up */
.g-cp-stage[data-mode="zen"].g-cp-royal .g-cp-bd-royal{
  background:radial-gradient(80% 70% at 50% 40%, hsla(36,80%,72%,.5), transparent 72%)}
/* vignette; the bell tightens it */
.g-cp-bd-vig{inset:0;opacity:1;animation:none;
  background:radial-gradient(120% 100% at 50% 46%, transparent 50%, rgba(0,0,0,.62) 100%)}
.g-cp-stage.g-cp-bell .g-cp-bd-vig{animation:g-cp-vig 1s ease-in-out infinite alternate}
@keyframes g-cp-vig{from{opacity:.85}to{opacity:1}}
/* a dim-out never cuts: the room sighs down to a whisper */
.g-cp-stage.g-cp-out .g-cp-bd{transition:opacity 1.6s ease;opacity:.22}
.g-cp-stage.g-cp-out .g-cp-bd-dark,.g-cp-stage.g-cp-out .g-cp-bd-vig{opacity:1}

/* ---- HUD: four chips above the easel ------------------------------------- */
.g-cp-hud{position:relative;z-index:2;display:flex;flex-wrap:wrap;align-items:center;
  justify-content:center;gap:8px 12px;font-family:var(--mono);font-size:11px;
  letter-spacing:.14em;text-transform:uppercase;color:var(--ink-faint)}
.g-cp-chip{display:inline-flex;align-items:center;gap:6px;padding:4px 12px;border-radius:999px;
  border:1px solid color-mix(in srgb, var(--lav), var(--line) 55%);
  background:color-mix(in srgb, var(--panel), transparent 40%);
  color:color-mix(in srgb, var(--ink-dim), var(--lav) 30%);
  font-variant-numeric:tabular-nums;will-change:transform;transform-origin:50% 50%;
  transition:color .3s ease,border-color .3s ease,box-shadow .3s ease}
.g-cp-chip.g-cp-moves{color:var(--ink)}
.g-cp-chip.g-cp-locked{border-color:var(--lav);color:var(--lav);
  box-shadow:0 0 12px color-mix(in srgb, var(--lav), transparent 70%)}
.g-cp-chip.g-cp-calm{border-color:var(--pink);color:var(--pink);position:relative;overflow:hidden;
  box-shadow:0 0 12px color-mix(in srgb, var(--pink), transparent 62%)}
/* the composure meter: index.js may set --cp-calm (0..1) on the chip; the
   fill rides under the text */
.g-cp-chip.g-cp-calm::before{content:"";position:absolute;left:0;top:0;bottom:0;z-index:-1;
  width:calc(var(--cp-calm,1) * 100%);background:color-mix(in srgb, var(--pink), transparent 82%);
  transition:width .5s ease}
.g-cp-stage[data-mode="zen"] .g-cp-chip.g-cp-clock{opacity:.7}
.g-cp-stage.g-cp-bell .g-cp-chip.g-cp-clock{border-color:var(--gold);color:var(--gold);
  animation:g-cp-bellchip 1s ease-in-out infinite alternate}
@keyframes g-cp-bellchip{from{box-shadow:0 0 6px rgba(240,194,75,.3)}to{box-shadow:0 0 16px rgba(240,194,75,.7)}}

/* ---- the frame: THE EASEL ------------------------------------------------- */
/* the frame node holds the board and the preview; the wood is its ::before
   (under the board), the stand its ::after (behind, below). The lean from
   casino.slide rides --cp-lean-x/y and springs back. */
.g-cp-frame{position:relative;z-index:1;flex:0 0 auto;
  width:calc(var(--cp-board) + 2 * (var(--cp-mat) + var(--cp-wood)));
  height:calc(var(--cp-board) + 2 * (var(--cp-mat) + var(--cp-wood)));
  padding:calc(var(--cp-mat) + var(--cp-wood));
  box-sizing:border-box;
  transform:perspective(1300px) rotateY(calc(var(--cp-lean-x,0) * 1.6deg)) rotateX(calc(var(--cp-lean-y,0) * -1.6deg))
    translate3d(calc(var(--cp-lean-x,0) * 3px), calc(var(--cp-lean-y,0) * 3px), 0);
  transition:transform .38s cubic-bezier(.2,1.5,.35,1)}
/* the wood + the mat */
.g-cp-frame::before{content:"";position:absolute;inset:0;z-index:0;border-radius:6px;
  background:
    linear-gradient(120deg, rgba(255,255,255,.08), transparent 40%, rgba(0,0,0,.18) 100%),
    repeating-linear-gradient(92deg, transparent 0 7px, rgba(0,0,0,.07) 7px 9px, transparent 9px 15px),
    linear-gradient(160deg, color-mix(in srgb, var(--cp-n-wood, #4a3224), black 10%), color-mix(in srgb, var(--cp-n-wood, #4a3224), black 42%));
  box-shadow:0 22px 60px rgba(0,0,0,.6), 0 0 0 1px rgba(255,255,255,.06) inset, 0 0 0 var(--cp-wood) transparent;
  /* the mat: a lighter card inside the wood */
  padding:var(--cp-wood);background-clip:border-box}
.g-cp-frame > .g-cp-board::before{content:"";position:absolute;inset:calc(-1 * var(--cp-mat));z-index:-1;
  border-radius:3px;background:linear-gradient(170deg, color-mix(in srgb, var(--ink), var(--panel) 55%), color-mix(in srgb, var(--ink), var(--panel) 70%));
  box-shadow:inset 0 0 0 1px rgba(0,0,0,.35), inset 0 2px 6px rgba(0,0,0,.35)}
/* the stand: a back leg and a ledge, drawn behind the frame */
.g-cp-frame::after{content:"";position:absolute;left:50%;top:70%;z-index:-1;width:16%;height:60%;
  transform:translateX(-50%) perspective(600px) rotateX(18deg);transform-origin:50% 0;
  background:
    linear-gradient(90deg, transparent 0 42%, color-mix(in srgb, var(--cp-n-wood, #4a3224), black 30%) 42% 58%, transparent 58%),
    linear-gradient(180deg, transparent 0 72%, color-mix(in srgb, var(--cp-n-wood, #4a3224), black 40%) 72% 80%, transparent 80%);
  opacity:.9;filter:drop-shadow(0 10px 14px rgba(0,0,0,.5))}
/* the easel glows for the bell and the royal */
.g-cp-stage.g-cp-bell .g-cp-frame::before{box-shadow:0 22px 60px rgba(0,0,0,.6), 0 0 0 1px rgba(255,255,255,.06) inset, 0 0 34px color-mix(in srgb, var(--gold), transparent 60%)}
.g-cp-stage.g-cp-royal .g-cp-frame::before{box-shadow:0 22px 60px rgba(0,0,0,.6), 0 0 0 1px rgba(255,255,255,.06) inset, 0 0 54px color-mix(in srgb, var(--gold), transparent 40%)}

/* ---- the board: the canvas ------------------------------------------------ */
.g-cp-board{position:relative;z-index:1;width:var(--cp-board);height:var(--cp-board);
  --cp-tile:calc((var(--cp-board) - (var(--cp-n,3) - 1) * var(--cp-gap)) / var(--cp-n,3));
  --cp-step:calc(var(--cp-tile) + var(--cp-gap));
  --cp-grab-max:calc(var(--cp-step) * .1);
  --cp-grab-k:1;
  border-radius:3px;touch-action:none;-webkit-user-select:none;user-select:none;
  /* the bare canvas shows through the gap and the blank: raw linen, dark */
  background:
    repeating-linear-gradient(0deg, transparent 0 2px, rgba(255,255,255,.025) 2px 3px),
    repeating-linear-gradient(90deg, transparent 0 2px, rgba(255,255,255,.025) 2px 3px),
    color-mix(in srgb, var(--ground), black 30%);
  box-shadow:inset 0 0 0 1px rgba(0,0,0,.5), inset 0 3px 12px rgba(0,0,0,.55);
  transition:filter .8s ease,opacity .8s ease}
.g-cp-board.g-cp-held .g-cp-tile{transition-duration:60ms,.22s,.22s}
.g-cp-board.g-cp-grab-blocked{--cp-grab-k:.33}

/* ---- the cells: the canvas floor under the tiles ------------------------- */
/* index.js lays one .g-cp-cell per square (--r/--c); the one with no tile on
   it is the gap - a recess in the linen. Drift (row_drift) targets these. */
.g-cp-cell{position:absolute;left:0;top:0;width:var(--cp-tile);height:var(--cp-tile);z-index:0;border-radius:2px;
  transform:translate3d(calc(var(--c,0) * var(--cp-step)), calc(var(--r,0) * var(--cp-step)), 0);
  background:color-mix(in srgb, var(--ground), black 22%);
  box-shadow:inset 0 2px 8px rgba(0,0,0,.55), inset 0 0 0 1px rgba(255,255,255,.03);pointer-events:none}

/* ---- tiles: positioned by transform ONLY ---------------------------------- */
.g-cp-tile{position:absolute;left:0;top:0;width:var(--cp-tile);height:var(--cp-tile);
  pointer-events:auto;cursor:pointer;will-change:transform;z-index:2;
  transform:translate3d(
    calc(var(--c,0) * var(--cp-step) + var(--cp-grab-x,0) * var(--cp-grab-k,1) * var(--cp-grab-max,0px)),
    calc(var(--r,0) * var(--cp-step) + var(--cp-grab-y,0) * var(--cp-grab-k,1) * var(--cp-grab-max,0px)), 0);
  transition:transform 190ms cubic-bezier(.22,.9,.3,1.04),opacity .22s ease,filter .22s ease}
/* the body: weight. A bevel and a shadow that deepen in flight. */
.g-cp-tile::before{content:"";position:absolute;inset:0;z-index:-1;border-radius:2px;
  background:color-mix(in srgb, var(--panel), black 20%);
  box-shadow:0 2px 6px rgba(0,0,0,.5), inset 0 1px 0 rgba(255,255,255,.14), inset 0 -1px 0 rgba(0,0,0,.4);
  transition:box-shadow .2s ease,transform .2s ease,opacity .6s ease}
/* the ring: the lock's chroma pulse, the hint's breath */
.g-cp-tile::after{content:"";position:absolute;inset:0;border-radius:2px;opacity:0;pointer-events:none;
  border:2px solid var(--lav)}
.g-cp-tile.is-moving{z-index:3}
.g-cp-tile.is-moving::before{box-shadow:0 10px 22px rgba(0,0,0,.6), inset 0 1px 0 rgba(255,255,255,.2);
  transform:translateY(-1px)}
/* the face: the clipped viewport */
.g-cp-face{position:absolute;inset:0;z-index:0;border-radius:2px;overflow:hidden;isolation:isolate;
  pointer-events:none;transition:inset .5s ease,border-radius .5s ease}
.g-cp-media{display:block;position:absolute;left:calc(var(--hc,0) * -1 * var(--cp-step));top:calc(var(--hr,0) * -1 * var(--cp-step));
  width:var(--cp-board);height:var(--cp-board);max-width:none;object-fit:cover;pointer-events:none}
/* a varnish: every face carries a hair of gloss from the lamp */
.g-cp-face::after{content:"";position:absolute;inset:0;z-index:2;pointer-events:none;opacity:.35;
  background:linear-gradient(160deg, rgba(255,255,255,.14), transparent 45%, rgba(0,0,0,.12) 100%)}
/* LOCKED: the seam dissolves - the face grows by half the gap, the bevel
   fades, one chroma ring runs, then a slow varnish sheen (Law III) */
.g-cp-tile.is-locked{z-index:1;cursor:default}
.g-cp-tile.is-locked .g-cp-face{inset:calc(var(--cp-gap) * -.5 - .5px);border-radius:0}
.g-cp-tile.is-locked::before{opacity:0}
.g-cp-tile.is-locked::after{animation:g-cp-lockring .7s ease-out 1;
  border-color:hsla(var(--cp-hue-b),90%,78%,1);inset:calc(var(--cp-gap) * -.5)}
@keyframes g-cp-lockring{0%{opacity:.9;transform:scale(1.05);box-shadow:-2px 0 rgba(255,60,120,.7),2px 0 rgba(60,230,220,.7)}
  60%{opacity:.4;box-shadow:-1px 0 rgba(255,60,120,.3),1px 0 rgba(60,230,220,.3)}
  100%{opacity:0;transform:scale(1);box-shadow:none}}
.g-cp-tile.is-locked .g-cp-face::after{opacity:.5;
  background:linear-gradient(115deg, transparent 30%, rgba(255,255,255,.1) 45%, transparent 60%);
  background-size:260% 100%;animation:g-cp-sheen 7s ease-in-out infinite}
@keyframes g-cp-sheen{0%{background-position:130% 0}100%{background-position:-60% 0}}
/* THE RESCUE: the next baseline move breathes lavender and lifts */
.g-cp-tile.is-hint{z-index:3}
.g-cp-tile.is-hint::after{opacity:1;border-color:var(--lav);inset:-2px;border-radius:3px;
  box-shadow:0 0 16px color-mix(in srgb, var(--lav), transparent 40%), inset 0 0 14px color-mix(in srgb, var(--lav), transparent 60%);
  animation:g-cp-hint 1.2s ease-in-out infinite alternate}
.g-cp-tile.is-hint::before{transform:translateY(-1px);box-shadow:0 8px 18px rgba(0,0,0,.55), inset 0 1px 0 rgba(255,255,255,.2)}
@keyframes g-cp-hint{from{opacity:.45;box-shadow:0 0 8px color-mix(in srgb, var(--lav), transparent 60%)}
  to{opacity:1;box-shadow:0 0 22px color-mix(in srgb, var(--lav), transparent 30%), inset 0 0 14px color-mix(in srgb, var(--lav), transparent 50%)}}
/* THE BUMP: a press into a wall. index.js holds .is-bump on the BOARD for
   240ms; the whole canvas knocks once against the easel - chrome moves, the
   tiles never do (their --r/--c are untouched, the board carries them). */
.g-cp-board.is-bump{animation:g-cp-refuse .24s ease-out 1}
@keyframes g-cp-refuse{0%{transform:none}30%{transform:translate(-3px,0)}60%{transform:translate(3px,0)}80%{transform:translate(-1px,0)}100%{transform:none}}

/* the numeral: a dim mono badge index.js sets on every face; it fades on a
   lock and goes with the seams on the solve (the picture is the point) */
.g-cp-num{position:absolute;right:5%;bottom:5%;z-index:3;font-family:var(--mono);font-weight:700;
  font-size:clamp(8px, calc(var(--cp-tile) * .1), 13px);line-height:1;padding:.25em .5em;border-radius:999px;
  background:rgba(8,6,22,.55);color:color-mix(in srgb, var(--lav), white 40%);opacity:.55;pointer-events:none;
  transition:opacity .6s ease}
.g-cp-tile.is-locked .g-cp-num{opacity:.22}
.g-cp-stage[data-phase="solved"] .g-cp-num{opacity:0}
.g-cp-stage[data-mode="zen"] .g-cp-num{opacity:.4}
/* THE PEEK REVEAL: the shell's verb toggles the hidden attribute on this
   layer (no display rule here, ever - the shell's own reset owns that); it
   sits over the canvas inside the easel and holds one unclipped copy of the
   subject + a caption. */
.g-cp-peeklayer{position:absolute;inset:calc(var(--cp-mat) + var(--cp-wood));z-index:6;overflow:hidden;border-radius:2px;
  background:rgba(10,8,16,.94);pointer-events:none;box-shadow:0 0 0 1px rgba(255,255,255,.08), 0 0 40px rgba(0,0,0,.6)}
.g-cp-peeklayer .g-cp-media{position:absolute;left:0;top:0;width:100%;height:100%;max-width:none;object-fit:cover;opacity:.96}
.g-cp-peek-cap{position:absolute;left:0;right:0;bottom:0;z-index:2;padding:6px 10px;text-align:center;
  font-family:var(--mono);font-size:10px;letter-spacing:.2em;text-transform:uppercase;color:var(--ink-dim);
  background:linear-gradient(to top, rgba(10,8,16,.85), transparent)}
/* the HUD's live buttons: the shell's peek verb (arc-peekbtn is the shell's
   class) and zen's Finish; both wear the chip skin and keep a focus ring */
.g-cp-hud .g-cp-peek,.g-cp-hud .g-cp-finish{appearance:none;-webkit-appearance:none;font:inherit;letter-spacing:inherit;
  text-transform:inherit;cursor:pointer;display:inline-flex;align-items:center;gap:6px;padding:4px 12px;border-radius:999px;
  border:1px solid var(--lav);color:color-mix(in srgb, var(--lav), var(--ink) 30%);
  background:color-mix(in srgb, var(--panel), var(--lav) 10%);
  box-shadow:0 0 10px color-mix(in srgb, var(--lav), transparent 78%);transition:box-shadow .3s ease,color .3s ease}
.g-cp-hud .g-cp-finish{border-color:var(--pink);color:color-mix(in srgb, var(--pink), var(--ink) 30%);
  background:color-mix(in srgb, var(--panel), var(--pink) 12%);animation:g-cp-finishchip 3.4s ease-in-out infinite alternate}
@keyframes g-cp-finishchip{from{box-shadow:0 0 8px color-mix(in srgb, var(--pink), transparent 82%)}to{box-shadow:0 0 18px color-mix(in srgb, var(--pink), transparent 58%)}}
.g-cp-hud .g-cp-peek:hover,.g-cp-hud .g-cp-finish:hover{color:var(--ink);border-color:var(--gold)}
.g-cp-hud .g-cp-peek:focus-visible,.g-cp-hud .g-cp-finish:focus-visible{outline:2px solid var(--gold);outline-offset:3px}
.g-cp-hud .g-cp-peek:active,.g-cp-hud .g-cp-finish:active{transform:translateY(1px)}
.g-cp-hud .g-cp-peek.is-held,.g-cp-hud .g-cp-peek.is-on,.g-cp-hud .g-cp-peek:active{color:var(--ink);border-color:var(--gold);
  box-shadow:0 0 16px color-mix(in srgb, var(--gold), transparent 50%)}
.g-cp-chip.g-cp-retake{border-color:var(--gold);color:var(--gold);opacity:.8}

/* ---- the solve: seams evaporate, the clip plays clean ---------------------- */
/* two hooks, one look: index.js sets data-phase="solved" on the stage AND
   .is-solved on the board (the board class outlives a phase change). */
.g-cp-stage[data-phase="solved"] .g-cp-tile,.g-cp-board.is-solved .g-cp-tile{cursor:default}
.g-cp-stage[data-phase="solved"] .g-cp-tile .g-cp-face,.g-cp-board.is-solved .g-cp-tile .g-cp-face{inset:calc(var(--cp-gap) * -.5);border-radius:0;
  transition:inset 1.2s ease .2s,border-radius 1.2s ease}
.g-cp-stage[data-phase="solved"] .g-cp-tile::before,.g-cp-board.is-solved .g-cp-tile::before{opacity:0;transition:opacity 1.2s ease}
.g-cp-stage[data-phase="solved"] .g-cp-tile .g-cp-face::after,.g-cp-board.is-solved .g-cp-tile .g-cp-face::after{opacity:.25;animation:none}
.g-cp-stage[data-phase="solved"] .g-cp-num,.g-cp-board.is-solved .g-cp-num{opacity:0}
.g-cp-stage[data-phase="solved"] .g-cp-board,.g-cp-board.is-solved{box-shadow:inset 0 0 0 1px rgba(0,0,0,.5), 0 0 40px hsla(var(--cp-hue-b),80%,70%,.35)}
.g-cp-stage[data-phase="solved"] .g-cp-board::after,.g-cp-board.is-solved::after{content:"";position:absolute;inset:0;z-index:5;pointer-events:none;
  background:linear-gradient(115deg, transparent 30%, rgba(255,255,255,.26) 50%, transparent 70%);background-size:300% 100%;
  animation:g-cp-solvesweep 1.8s ease-out .3s 1 both}
@keyframes g-cp-solvesweep{0%{opacity:1;background-position:140% 0}100%{opacity:0;background-position:-40% 0}}

/* ---- the burial (data-wash="1" while a wash is up) --------------------------
   The engine's wash buries the board from its own fixed layer; the studio
   answers from underneath: the lamp dips, the motes freeze, the canvas cools
   toward the wash hue and the numerals hide so the picture is all there is.
   Nothing here touches a tile's place. */
.g-cp-stage[data-wash="1"]{--cp-wash-k:.62}
.g-cp-stage[data-wash="1"] .g-cp-bd-motes{animation-play-state:paused;opacity:.3}
.g-cp-stage[data-wash="1"] .g-cp-board{filter:saturate(.9) brightness(.96);transition:filter .6s ease}
.g-cp-stage[data-wash="1"] .g-cp-num{opacity:0;transition:opacity .3s ease}
.g-cp-stage[data-wash="1"] .g-cp-frame::before{box-shadow:0 22px 60px rgba(0,0,0,.6), 0 0 0 1px rgba(255,255,255,.06) inset, 0 0 40px hsla(var(--cp-hue-a),70%,60%,.35);transition:box-shadow .6s ease}
/* ---- the peek (data-peek="1" while the reveal is held) ----------------------
   shell/peek.js lifts the whole picture over the board (.g-cp-peeklayer, not
   hidden); under it the board falls back a step so the reveal reads as a
   sheet laid on the easel, and the HUD quietens. */
.g-cp-stage[data-peek="1"] .g-cp-board{filter:brightness(.55) saturate(.7);transition:filter .25s ease}
.g-cp-stage[data-peek="1"] .g-cp-hud{opacity:.55;transition:opacity .25s ease}
.g-cp-stage[data-peek="1"] .g-cp-peek{border-color:var(--gold);color:var(--gold);box-shadow:0 0 14px color-mix(in srgb, var(--gold), transparent 55%)}
/* ---- phases ----------------------------------------------------------------- */
.g-cp-stage[data-phase="briefing"] .g-cp-board{filter:brightness(.78) saturate(.85)}
.g-cp-stage[data-phase="ended"] .g-cp-board{filter:saturate(.6) brightness(.7)}
.g-cp-stage[data-phase="ended"] .g-cp-tile::before,.g-cp-stage[data-phase="ended"] .g-cp-tile::after{animation:none}

/* ---- the shell's stamp, when index.js targets the frame ------------------- */
.g-cp-frame > .arc-stamp{position:absolute;left:0;right:0;top:44%;margin:0 auto;width:max-content;
  max-width:90%;z-index:7;text-align:center}

/* ---- the flash well: engine one-shots over the board, never a pointer ----- */
.g-cp-flashwell{position:absolute;inset:0;overflow:hidden;pointer-events:none;z-index:6}
.g-cp-flashwell *{pointer-events:none}

/* ---- proctor line + end card ---------------------------------------------- */
.g-cp-msg{position:relative;z-index:2;margin:0;min-height:1.4em;text-align:center;
  font-family:var(--mono);font-size:11px;letter-spacing:.16em;text-transform:uppercase;
  color:var(--ink-dim);transition:opacity .3s ease}
.g-cp-msg:empty{opacity:0}
.g-cp-end{position:absolute;left:50%;top:50%;transform:translate(-50%,-50%);z-index:8;
  width:min(460px, 92%);box-sizing:border-box;padding:18px 20px 16px;
  border-radius:6px;background:color-mix(in srgb, var(--navy), transparent 4%);
  border:1px solid color-mix(in srgb, var(--lav), var(--line) 50%);
  box-shadow:0 18px 50px rgba(0,0,0,.55), 0 0 34px color-mix(in srgb, var(--lav), transparent 72%);
  animation:g-cp-endin .5s ease-out 1;overflow:hidden}
/* asset chrome (Deck VI): the pool still the casino chose, as the card's paper */
.g-cp-end::before{content:"";position:absolute;inset:0;z-index:-1;opacity:calc(var(--cp-n-a-asset,0) * 1.4);
  background-image:var(--cp-n-asset,none);background-size:cover;background-position:center;
  mix-blend-mode:luminosity;filter:blur(3px) saturate(.1)}
@keyframes g-cp-endin{from{opacity:0;transform:translate(-50%,-46%)}to{opacity:1;transform:translate(-50%,-50%)}}
.g-cp-end-title{font-family:var(--disp);font-size:16px;letter-spacing:.14em;text-transform:uppercase;
  margin:0 0 8px;color:var(--ink);text-align:center;text-shadow:0 0 18px color-mix(in srgb, var(--pink), transparent 60%)}
.g-cp-end-row{display:flex;justify-content:space-between;align-items:baseline;gap:12px;
  font-family:var(--mono);font-size:12px;letter-spacing:.1em;text-transform:uppercase;color:var(--ink-dim);
  padding:5px 0;border-bottom:1px dashed color-mix(in srgb, var(--line), transparent 20%)}
.g-cp-end-k{color:var(--ink-faint)}
.g-cp-end-v{color:var(--ink);font-variant-numeric:tabular-nums}
.g-cp-end-line{margin:2px 0 0;font-size:12px;color:var(--ink-dim)}
.g-cp-end-solved .g-cp-end-v{color:var(--gold)}
.g-cp-end-dare{margin-top:12px;padding:10px 12px;border-radius:6px;text-align:center;
  border:1px solid color-mix(in srgb, var(--gold), transparent 55%);
  background:color-mix(in srgb, var(--gold), transparent 92%)}
.g-cp-end-dare .g-cp-end-k{display:block;font-family:var(--mono);font-size:10px;letter-spacing:.22em;
  text-transform:uppercase;color:var(--gold)}
.g-cp-end-dare .g-cp-end-v{display:block;font-family:var(--disp);font-size:22px;letter-spacing:.08em;margin:4px 0 2px;
  color:var(--gold);text-shadow:0 0 16px color-mix(in srgb, var(--gold), transparent 55%)}
.g-cp-end .btn,.g-cp-end button{margin:10px 4px 0}
/* the grade as an OBJECT: a wax seal pressed into the card */
.g-cp-end-seal{position:relative;width:64px;height:64px;margin:4px auto 8px;border-radius:50%;
  display:flex;align-items:center;justify-content:center;font-family:var(--disp);font-size:26px;font-weight:700;
  color:color-mix(in srgb, var(--ink), transparent 10%);
  --cp-seal:var(--pink-deep);
  background:radial-gradient(circle at 38% 32%, color-mix(in srgb, var(--cp-seal), white 25%), var(--cp-seal) 45%, color-mix(in srgb, var(--cp-seal), black 35%) 100%);
  box-shadow:0 4px 10px rgba(0,0,0,.55), inset 0 0 0 3px color-mix(in srgb, var(--cp-seal), black 25%), inset 0 0 0 5px color-mix(in srgb, var(--cp-seal), white 10%);
  text-shadow:0 1px 0 rgba(0,0,0,.5);transform:rotate(-8deg);
  animation:g-cp-sealin .6s cubic-bezier(.2,1.4,.3,1) 1}
.g-cp-end-seal::before{content:"";position:absolute;inset:-6px;border-radius:50%;z-index:-1;
  background:radial-gradient(circle, color-mix(in srgb, var(--cp-seal), transparent 30%) 55%, transparent 72%);
  clip-path:polygon(50% 0,60% 8%,72% 3%,78% 14%,92% 14%,92% 28%,100% 38%,94% 50%,100% 62%,92% 72%,92% 86%,78% 86%,72% 97%,60% 92%,50% 100%,40% 92%,28% 97%,22% 86%,8% 86%,8% 72%,0 62%,6% 50%,0 38%,8% 28%,8% 14%,22% 14%,28% 3%,40% 8%)}
.g-cp-end-seal[data-grade="S"]{--cp-seal:var(--gold);color:color-mix(in srgb, var(--ground), black 20%)}
.g-cp-end-seal[data-grade="A"]{--cp-seal:var(--pink)}
.g-cp-end-seal[data-grade="B"]{--cp-seal:var(--lav);color:color-mix(in srgb, var(--ground), black 20%)}
.g-cp-end-seal[data-grade="C"]{--cp-seal:var(--slate)}
.g-cp-end-seal[data-zen]{--cp-seal:hsl(34,70%,56%);font-size:18px;letter-spacing:.1em}
@keyframes g-cp-sealin{0%{opacity:0;transform:rotate(-8deg) scale(1.6)}60%{opacity:1;transform:rotate(-8deg) scale(.94)}100%{transform:rotate(-8deg) scale(1)}}

/* ---- THE CLASS RULES SHEET (Deck VI, Law IV) ------------------------------- */
/* Drawn, not told. The sheet takes no pointer events; the GO button re-enables
   them for itself alone. Each figure is ONE span with three blank parts; the mini board
   is a gradient grid, the parts are its moving pieces. */
.g-cp-howto{position:absolute;left:50%;top:50%;transform:translate(-50%,-50%);z-index:9;
  width:min(520px,90vw);max-height:86vh;overflow:auto;pointer-events:none;
  display:flex;flex-direction:column;gap:2px;padding:22px 24px 20px;border-radius:8px;
  background:linear-gradient(180deg,rgba(26,26,54,.95),rgba(14,14,32,.97));
  border:1px solid var(--line);
  box-shadow:0 30px 80px rgba(0,0,0,.55), 0 0 44px rgba(184,166,232,.12)}
.g-cp-hw-title{margin:0 0 8px;text-align:center;font-family:var(--disp);font-size:clamp(16px,2.4vmin,20px);
  letter-spacing:.2em;text-transform:uppercase;color:var(--lav);text-shadow:0 0 22px rgba(184,166,232,.45)}
.g-cp-hw-row{display:flex;align-items:center;gap:16px;padding:11px 2px}
.g-cp-hw-row + .g-cp-hw-row{border-top:1px dashed rgba(58,58,94,.6)}
.g-cp-hw-cap{margin:0;flex:1 1 auto;font-size:12.5px;line-height:1.5;color:var(--ink-dim)}
.g-cp-hw-fig{position:relative;flex:0 0 auto;display:block;width:72px;height:72px;pointer-events:none;
  --hw-c:22px;--hw-g:3px;--hw-s:calc(var(--hw-c) + var(--hw-g));border-radius:3px;overflow:visible;
  background:
    linear-gradient(90deg, transparent 0 var(--hw-c), rgba(0,0,0,.55) var(--hw-c) var(--hw-s), transparent 0),
    linear-gradient(0deg, transparent 0 var(--hw-c), rgba(0,0,0,.55) var(--hw-c) var(--hw-s), transparent 0),
    linear-gradient(135deg, hsl(292,40%,48%), hsl(330,55%,52%) 50%, hsl(262,45%,42%));
  background-size:var(--hw-s) 100%, 100% var(--hw-s), 100% 100%;
  box-shadow:0 4px 14px rgba(0,0,0,.5), inset 0 0 0 1px rgba(255,255,255,.08)}
/* index.js drops three blank parts into every figure: .g-cp-hw-a / -b / -c.
   Each figure decides what they are. All absolute, all one cell by default. */
.g-cp-hw-fig i{position:absolute;display:block;width:var(--hw-c);height:var(--hw-c);border-radius:2px;box-sizing:border-box}
/* 1 - SLIDE: a = the gap (bottom-right), b = the tile beside it sliding in
   and back, c = the finger that taps it */
.g-cp-hw-slide .g-cp-hw-a{right:0;bottom:0;background:rgba(10,8,20,.92);box-shadow:inset 0 2px 6px rgba(0,0,0,.8)}
.g-cp-hw-slide .g-cp-hw-b{right:var(--hw-s);bottom:0;
  background:linear-gradient(135deg, hsl(330,55%,56%), hsl(300,45%,48%));
  box-shadow:0 3px 8px rgba(0,0,0,.6), inset 0 1px 0 rgba(255,255,255,.2);
  animation:g-cp-hwslide 2.6s ease-in-out infinite}
@keyframes g-cp-hwslide{0%,30%{transform:translateX(0)}45%,70%{transform:translateX(var(--hw-s))}85%,100%{transform:translateX(0)}}
.g-cp-hw-slide .g-cp-hw-c{right:calc(var(--hw-s) + 4px);bottom:-8px;width:12px;height:18px;border-radius:6px 6px 4px 4px;
  background:linear-gradient(180deg, var(--lav), #6E5F9B);box-shadow:0 0 8px rgba(184,166,232,.4);
  animation:g-cp-hwtap 2.6s ease-in-out infinite}
@keyframes g-cp-hwtap{0%,22%{transform:translate(0,0);opacity:.7}30%{transform:translate(0,-4px) scale(.92);opacity:1}
  45%,70%{transform:translate(var(--hw-s),-2px);opacity:.9}85%,100%{transform:translate(0,0);opacity:.7}}
/* 2 - LOCK: a = the centre tile snapping home from the right, b = the ring
   that runs when it lands, c = the seam light that closes and fades */
.g-cp-hw-lock .g-cp-hw-a{left:var(--hw-s);top:var(--hw-s);
  background:linear-gradient(135deg, hsl(310,50%,54%), hsl(330,55%,50%));
  animation:g-cp-hwlock 2.6s ease-out infinite}
@keyframes g-cp-hwlock{0%,20%{transform:translateX(var(--hw-s));box-shadow:0 3px 8px rgba(0,0,0,.6)}
  38%{transform:translateX(0);box-shadow:0 3px 8px rgba(0,0,0,.6)}
  46%{box-shadow:0 0 0 2px hsl(330,90%,78%), 0 0 14px hsla(330,90%,70%,.8)}
  60%,88%{transform:translateX(0);box-shadow:0 0 0 0 transparent}
  100%{transform:translateX(var(--hw-s))}}
.g-cp-hw-lock .g-cp-hw-b{left:var(--hw-s);top:var(--hw-s);border:2px solid hsl(330,90%,78%);opacity:0;
  animation:g-cp-hwring 2.6s ease-out infinite}
@keyframes g-cp-hwring{0%,38%{opacity:0;transform:scale(1)}44%{opacity:.9;transform:scale(1.08)}70%,100%{opacity:0;transform:scale(1.3)}}
.g-cp-hw-lock .g-cp-hw-c{left:calc(var(--hw-s) - var(--hw-g));top:var(--hw-s);width:var(--hw-g);height:var(--hw-c);border-radius:0;
  background:hsl(330,90%,82%);opacity:0;animation:g-cp-hwseam 2.6s ease-out infinite}
@keyframes g-cp-hwseam{0%,40%{opacity:0}48%{opacity:1}75%,100%{opacity:0}}
/* 3 - WASH: a = the pink wash veil over the whole board (breathing), b = a
   tile still sliding under it, c = the gap it slides into */
.g-cp-hw-wash .g-cp-hw-c{right:0;top:var(--hw-s);background:rgba(10,8,20,.92);box-shadow:inset 0 2px 6px rgba(0,0,0,.8)}
.g-cp-hw-wash .g-cp-hw-b{right:var(--hw-s);top:var(--hw-s);
  background:linear-gradient(135deg, hsl(330,55%,56%), hsl(300,45%,48%));
  box-shadow:0 3px 8px rgba(0,0,0,.6), inset 0 1px 0 rgba(255,255,255,.2);
  animation:g-cp-hwslide 3s ease-in-out infinite}
.g-cp-hw-wash .g-cp-hw-a{inset:-6px;width:auto;height:auto;border-radius:5px;z-index:2;
  background:radial-gradient(70% 70% at 50% 40%, hsla(330,90%,70%,.75), hsla(292,70%,55%,.55) 60%, hsla(262,60%,40%,.4));
  mix-blend-mode:screen;filter:blur(1px);animation:g-cp-hwwash 3s ease-in-out infinite}
@keyframes g-cp-hwwash{0%,25%{opacity:0}40%,75%{opacity:.9}90%,100%{opacity:0}}
.g-cp-hw-go{pointer-events:auto;align-self:center;margin-top:14px;padding:10px 26px;border-radius:999px;cursor:pointer;
  font-family:var(--disp);font-size:13px;letter-spacing:.18em;text-transform:uppercase;color:var(--ground);
  background:linear-gradient(180deg, color-mix(in srgb, var(--lav), white 15%), var(--lav));
  border:1px solid color-mix(in srgb, var(--lav), white 30%);
  box-shadow:0 0 22px color-mix(in srgb, var(--lav), transparent 45%), 0 3px 0 color-mix(in srgb, var(--lav), black 35%);
  animation:g-cp-gopulse 1.8s ease-in-out infinite alternate}
.g-cp-hw-go:hover,.g-cp-hw-go:focus-visible{outline:2px solid var(--gold);outline-offset:3px;filter:brightness(1.08)}
.g-cp-hw-go:active{transform:translateY(2px);box-shadow:0 0 22px color-mix(in srgb, var(--lav), transparent 45%), 0 1px 0 color-mix(in srgb, var(--lav), black 35%)}
@keyframes g-cp-gopulse{from{box-shadow:0 0 12px color-mix(in srgb, var(--lav), transparent 60%), 0 3px 0 color-mix(in srgb, var(--lav), black 35%)}
  to{box-shadow:0 0 30px color-mix(in srgb, var(--lav), transparent 30%), 0 3px 0 color-mix(in srgb, var(--lav), black 35%)}}

/* ---- reduced motion: the mechanic survives, the motion does not ---------- */
html.arc-reduced .g-cp-stage *{animation:none !important}
html.arc-reduced .g-cp-stage{transition:none}
html.arc-reduced .g-cp-bd-motes{opacity:0}
html.arc-reduced .g-cp-bd-lamp{opacity:calc(var(--cp-n-lamp,.7) * var(--cp-wash-k,1) * .8)}
html.arc-reduced .g-cp-tile{transition:transform 80ms linear,opacity .2s ease}
html.arc-reduced .g-cp-frame{transform:none;transition:none}
html.arc-reduced .g-cp-board,html.arc-reduced .g-cp-board.g-cp-grab-blocked{--cp-grab-k:0}
html.arc-reduced .g-cp-tile.is-hint::after{opacity:1}
@media (prefers-reduced-motion: reduce){
  .g-cp-stage *{animation:none !important}
  .g-cp-stage{transition:none}
  .g-cp-bd-motes{opacity:0}
  .g-cp-bd-lamp{opacity:calc(var(--cp-n-lamp,.7) * var(--cp-wash-k,1) * .8)}
  .g-cp-tile{transition:transform 80ms linear,opacity .2s ease}
  .g-cp-frame{transform:none;transition:none}
  .g-cp-board,.g-cp-board.g-cp-grab-blocked{--cp-grab-k:0}
  .g-cp-tile.is-hint::after{opacity:1}
}

/* ---- narrow / touch ----------------------------------------------------------- */
@media (max-width:560px){
  .g-cp-stage{padding:64px 8px 10px;--cp-board:min(calc(100dvh - 230px), calc(100vw - 56px));--cp-mat:8px;--cp-wood:7px}
  .g-cp-hud{gap:6px 8px;font-size:10px}
  .g-cp-chip{padding:3px 9px}
  .g-cp-frame::after{display:none}
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
.g-cp-howto{pointer-events:auto}
.g-cp-hw-go{position:sticky;bottom:0;z-index:3;align-self:stretch;margin-top:14px}

`;

/** Inject once per document. No-op headless (the DOM double has no head). */
export function injectComposureStyle() {
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

export default injectComposureStyle;
