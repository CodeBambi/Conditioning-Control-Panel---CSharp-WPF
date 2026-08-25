/* ============================================================================
 * games/instant-recall/style.js - the game injects its OWN stylesheet from JS.
 *
 * THE LECTURE HALL AT NIGHT. The class root is a full-viewport stage: a dark
 * hall, rows of empty seats sketched along the bottom, one projector beam
 * falling from above onto THE SCREEN - and the screen IS the wall, a full-bleed
 * mosaic of the player's own media that keeps changing itself. Nothing here
 * decorates a tile's MEDIA: the wall is CORE's live window (many animated
 * tiles) and the PERF LAW for this class is absolute - no filter, no per-frame
 * paint on `.g-ir-tile` or `.g-ir-media`. The one animation a tile is allowed
 * is the SWAP (opacity, and a transform on the face, for ~620ms, at most four
 * tiles at a time), because the wall turning itself over IS the class.
 * Everything else that breathes lives on chrome: the beam, the dust, the seats,
 * the frame, the slip.
 *
 * Composition, exactly the DOM contract (CORE builds, this file styles):
 *
 *   .g-ir-stage[data-phase=briefing|vigil|stop|verdict|ended][data-layout=wall]
 *               absolute inset:0, explicit ground, padded under the shell's
 *               proctor strip
 *   .g-ir-backdrop   the hall (pointer-events:none, z 0) - the casino hangs its
 *                    beam / dust / seat layers here
 *   .g-ir-hud        three chips: clock, stops, density (the density chip is a
 *                    METER: it reads --ir-density 0..1 when CORE paints it,
 *                    and degrades to a plain chip when it does not)
 *   .g-ir-montage    THE SCREEN: flex:1, the lit rectangle, holding ONE
 *                    .g-ir-wall grid (--ir-cols / --ir-rows, solved from the
 *                    stage aspect by montage.js) of .g-ir-tile > TWO .g-ir-face
 *                    layers > .g-ir-media. `.is-swapping` + data-swap on the
 *                    tile and `.is-in` / `.is-out` on the faces drive the turn.
 *   .g-ir-ledger     the truth tail - VISUALLY hidden, aria-hidden (CORE)
 *   .g-ir-stop       THE EXAM SLIP: a paper card that arrives from the desk
 *                    while the screen holds its breath; .g-ir-q the question,
 *                    .g-ir-opts > .g-ir-opt[data-i] four real buttons (1-4
 *                    keycaps drawn from data-i), .g-ir-timer the answer ring
 *                    (reads --ir-left 1..0 from CORE; --ir-t is the old name)
 *   .g-ir-msg / .g-ir-flashwell / .g-ir-end   proctor line, engine anchor, the
 *                    end card (title + k/v rows, DE's shape)
 *   .g-ir-howto      the drawn class-rules sheet (Deck VI, Law IV): three
 *                    vignettes (THE WALL, with a drawn flash / spiral / bubble
 *                    over it / THE FREEZE and the slip / THE PICK, four
 *                    keycaps) and ONE live button, .g-ir-go - all CORE class
 *                    names (.g-ir-howto-*), art = pure CSS on empty boxes
 *
 * THE FREEZE IS A HELD BREATH, AND IT LANDS MID-SWAP. [data-phase="stop"]: the
 * wall's own opacity drops (no filter - a composited opacity on the container
 * is free), `.g-ir-montage.is-frozen *` PAUSES every swap animation exactly
 * where it was (which is why the turn is a keyframe and not a transition - a
 * transition cannot be paused), the hall's veil (.g-ir-veil, a sibling of the
 * screen, not a filter) darkens, the beam narrows, the slip slides up with a
 * paper tilt. [data-phase="verdict"] keeps the slip and lets the picked option
 * settle. Resume releases all of it on the same transitions, so the breath goes
 * out as it came in - and the half-finished swaps finish where they stopped.
 *
 * REDUCED MOTION twice over (html.arc-reduced + the media query): every
 * keyframe dies, transitions survive (a fade is not motion).
 * .g-ir-stage.suspended freezes every animation (animation-play-state) so
 * CORE can hold the hall with the class.
 *
 * DECK STYLES live in their own sheets: casino.js (g-ir-casino-style),
 * trickster.js (g-ir-trickster-style), pressure.js (g-ir-pressure-style).
 * This file is the hall; they are the lights, the lies and the weather.
 * ==========================================================================*/

const STYLE_ID = 'g-ir-style';

export const STYLE_TEXT = `
/* ---- registered decoration props: numbers interpolate, so the casino's
   identity MORPHS instead of snapping ------------------------------------- */
@property --ir-n-beam{syntax:'<number>';inherits:true;initial-value:.55}
@property --ir-n-veil{syntax:'<number>';inherits:true;initial-value:0}
@property --ir-density{syntax:'<number>';inherits:true;initial-value:0}
@property --ir-t{syntax:'<number>';inherits:false;initial-value:1}
@property --ir-left{syntax:'<number>';inherits:false;initial-value:1}
@property --ir-hw-k{syntax:'<angle>';inherits:false;initial-value:300deg}

/* ---- the stage: the whole window is the hall ------------------------------ */
.g-ir-stage{position:absolute;inset:0;overflow:hidden;display:flex;flex-direction:column;
  align-items:center;gap:8px;padding:64px 12px 12px;color:var(--ink);
  --ir-hue-a:var(--ir-n-hue-a,292);
  --ir-hue-b:var(--ir-n-hue-b,330);
  --ir-la:hsla(var(--ir-hue-a),55%,70%,.18);
  --ir-lb:hsla(var(--ir-hue-b),70%,68%,.14);
  --ir-breath:var(--ir-n-breath,9s);
  --ir-screen-w:min(100%, 1480px);
  background:
    radial-gradient(80% 46% at 50% 0%, var(--ir-la), transparent 70%),
    radial-gradient(120% 40% at 50% 108%, var(--ir-lb), transparent 64%),
    color-mix(in srgb, var(--ground), black 46%)}
.g-ir-stage.suspended *{animation-play-state:paused !important}

/* ---- the backdrop: the hall itself (decoration only) ---------------------- */
.g-ir-backdrop{position:absolute;inset:0;pointer-events:none;z-index:0;overflow:hidden}
.g-ir-backdrop *{pointer-events:none}
/* the projector beam: a cone from the ceiling, breathing; narrows for a stop */
.g-ir-backdrop::before{content:"";position:absolute;left:50%;top:-6%;width:140%;height:92%;
  transform:translateX(-50%);
  background:conic-gradient(from 180deg at 50% 0%, transparent 0deg, transparent 156deg,
    hsla(var(--ir-hue-a),70%,82%,calc(var(--ir-n-beam) * .4)) 164deg,
    hsla(var(--ir-hue-b),80%,88%,calc(var(--ir-n-beam) * .62)) 180deg,
    hsla(var(--ir-hue-a),70%,82%,calc(var(--ir-n-beam) * .4)) 196deg,
    transparent 204deg, transparent 360deg);
  -webkit-mask:linear-gradient(180deg, #000 0%, rgba(0,0,0,.7) 55%, transparent 100%);
  mask:linear-gradient(180deg, #000 0%, rgba(0,0,0,.7) 55%, transparent 100%);
  opacity:.9;animation:g-ir-beam var(--ir-breath) ease-in-out infinite alternate;
  transition:transform 1.4s ease, opacity 1.4s ease}
@keyframes g-ir-beam{from{opacity:.72;transform:translateX(-50%) scaleX(1)}to{opacity:1;transform:translateX(-50%) scaleX(1.08)}}
/* the seats: tiered arcs of dim silhouettes along the floor */
.g-ir-backdrop::after{content:"";position:absolute;left:-10%;right:-10%;bottom:-2%;height:34%;
  background:
    repeating-radial-gradient(ellipse 120% 200% at 50% 130%,
      rgba(0,0,0,0) 0 6.2%, rgba(0,0,0,.55) 6.2% 7.2%, rgba(40,36,70,.35) 7.2% 7.6%, rgba(0,0,0,0) 7.6% 9%),
    linear-gradient(180deg, transparent, rgba(0,0,0,.62));
  opacity:.85}

/* ---- the HUD: three chips on the lectern ---------------------------------- */
.g-ir-hud{position:relative;z-index:2;display:flex;flex-wrap:wrap;align-items:center;justify-content:center;
  gap:8px;width:var(--ir-screen-w)}
.g-ir-chip{display:inline-flex;align-items:center;gap:6px;padding:4px 12px;border-radius:999px;
  font-family:var(--mono);font-size:11.5px;letter-spacing:.12em;text-transform:uppercase;
  color:var(--ink-dim);background:color-mix(in srgb, var(--panel), transparent 20%);
  border:1px solid color-mix(in srgb, var(--line), transparent 20%);
  box-shadow:inset 0 1px 0 rgba(255,255,255,.04), 0 4px 14px rgba(0,0,0,.35);
  font-variant-numeric:tabular-nums;white-space:nowrap;position:relative;overflow:hidden}
.g-ir-clock{color:var(--ink)}
.g-ir-stops{color:var(--lav)}
/* the density METER: a bar that fills behind the chip's text from --ir-density
   (CORE paints it on the chip or the stage); plain chip when it is never set */
.g-ir-density{padding-left:30px}
.g-ir-density::before{content:"";position:absolute;left:10px;top:50%;width:14px;height:14px;margin-top:-7px;
  border-radius:50%;background:conic-gradient(var(--pink) calc(var(--ir-density) * 360deg), rgba(255,255,255,.08) 0);
  -webkit-mask:radial-gradient(circle, transparent 3.5px, #000 4px);mask:radial-gradient(circle, transparent 3.5px, #000 4px);
  transition:--ir-density .6s ease}
.g-ir-density::after{content:"";position:absolute;left:0;bottom:0;height:2px;width:calc(var(--ir-density) * 100%);
  background:linear-gradient(90deg, var(--lav), var(--pink));opacity:.8;transition:width .6s ease}

/* ---- THE SCREEN: the WALL is the lit rectangle ------------------------------ */
.g-ir-montage{position:relative;z-index:1;flex:1;min-height:0;width:var(--ir-screen-w);
  display:flex;flex-direction:column;padding:4px;border-radius:12px;overflow:hidden;
  background:#0b0a16;
  border:1px solid color-mix(in srgb, var(--lav), transparent 72%);
  box-shadow:0 0 0 1px rgba(0,0,0,.6), 0 24px 60px rgba(0,0,0,.55),
    0 0 60px hsla(var(--ir-hue-b),80%,70%,.12);
  opacity:1;transition:opacity .55s ease, box-shadow .8s ease}
/* the screen's own vignette - on the CONTAINER's pseudo, never a tile */
.g-ir-montage::after{content:"";position:absolute;inset:0;pointer-events:none;z-index:5;border-radius:inherit;
  background:radial-gradient(120% 90% at 50% 50%, transparent 66%, rgba(0,0,0,.42) 100%)}

/* THE WALL: one grid, solved from the stage's aspect by montage.js and written
   onto --ir-cols / --ir-rows. Hairline gaps, small radii, cover fits - the FYP
   feed's own vocabulary (Resources/web/fyp/fyp.css). Tiles fill edge to edge. */
.g-ir-wall{position:relative;flex:1;min-height:0;display:grid;gap:3px;
  grid-template-columns:repeat(var(--ir-cols,4), minmax(0,1fr));
  grid-template-rows:repeat(var(--ir-rows,3), minmax(0,1fr))}
.g-ir-tile{position:relative;min-width:0;min-height:0;border-radius:6px;overflow:hidden;
  background:#0d0d18;
  box-shadow:inset 0 0 0 1px rgba(255,255,255,.05)}
/* THE SWAP. Each tile owns TWO faces and turns itself over on its own seeded
   dwell; montage.js paints the back face, flips .is-in / .is-out and hangs
   data-swap on the tile. The turn is a KEYFRAME, never a transition, because
   the freeze has to be able to land in the MIDDLE of one (a transition cannot
   be paused; an animation can - see .is-frozen below). No filter anywhere near
   a face: it may hold a live decode (PERF LAW, web CLAUDE.md trap 36). */
.g-ir-face{position:absolute;inset:0;overflow:hidden;border-radius:inherit;opacity:1;z-index:1}
.g-ir-face.is-out{opacity:0;z-index:0}
.g-ir-face.is-in{opacity:1;z-index:2}
.g-ir-tile.is-swapping .g-ir-face.is-in{animation:g-ir-in var(--ir-swap,620ms) cubic-bezier(.3,.7,.3,1) both}
.g-ir-tile.is-swapping .g-ir-face.is-out{animation:g-ir-out var(--ir-swap,620ms) cubic-bezier(.3,.7,.3,1) both}
@keyframes g-ir-in{from{opacity:0}to{opacity:1}}
@keyframes g-ir-out{from{opacity:1}to{opacity:0}}
.g-ir-tile[data-swap="rise"].is-swapping .g-ir-face.is-in{animation-name:g-ir-in-rise}
.g-ir-tile[data-swap="rise"].is-swapping .g-ir-face.is-out{animation-name:g-ir-out-rise}
@keyframes g-ir-in-rise{from{opacity:0;transform:translateY(22%)}to{opacity:1;transform:translateY(0)}}
@keyframes g-ir-out-rise{from{opacity:1;transform:translateY(0)}to{opacity:0;transform:translateY(-16%)}}
.g-ir-tile[data-swap="slide"].is-swapping .g-ir-face.is-in{animation-name:g-ir-in-slide}
.g-ir-tile[data-swap="slide"].is-swapping .g-ir-face.is-out{animation-name:g-ir-out-slide}
@keyframes g-ir-in-slide{from{opacity:0;transform:translateX(24%)}to{opacity:1;transform:translateX(0)}}
@keyframes g-ir-out-slide{from{opacity:1;transform:translateX(0)}to{opacity:0;transform:translateX(-18%)}}
.g-ir-tile img,.g-ir-tile video,.g-ir-media{display:block;position:absolute;inset:0;width:100%;height:100%;
  object-fit:cover;pointer-events:none}
/* THE FREEZE IS MID-SWAP. Pausing the animation (never cancelling it) leaves
   both faces exactly where the eye last saw them - that stillness IS the
   mechanic, and it is why the swap is an animation in the first place. */
.g-ir-montage.is-frozen{opacity:.42}
.g-ir-montage.is-frozen *{animation-play-state:paused !important}

/* the truth tail: in the tree for the harness, never on the glass */
.g-ir-ledger{position:absolute;width:1px;height:1px;overflow:hidden;clip:rect(0 0 0 0);clip-path:inset(50%);
  white-space:nowrap;opacity:0;pointer-events:none}

/* the veil: the hall's held breath. A sibling the casino mounts in the stage
   (it never filters the screen - it lies OVER it). Styled here so the phase
   rule can drive it even when the casino is disarmed (CORE may mount one too). */
.g-ir-veil{position:absolute;inset:0;z-index:3;pointer-events:none;opacity:0;
  background:radial-gradient(70% 60% at 50% 45%, rgba(8,6,20,.25), rgba(8,6,20,.82) 100%);
  transition:opacity .6s ease}

/* ---- THE EXAM SLIP ---------------------------------------------------------- */
.g-ir-stop{position:absolute;left:50%;top:50%;z-index:7;width:min(560px, 92%);
  transform:translate(-50%,-44%) rotate(.6deg);opacity:0;pointer-events:none;
  display:flex;flex-direction:column;gap:12px;padding:20px 22px 18px;border-radius:6px;
  color:#1d1a2e;
  background:
    linear-gradient(180deg, rgba(255,255,255,.35), transparent 30%),
    repeating-linear-gradient(180deg, transparent 0 26px, rgba(90,60,140,.08) 26px 27px),
    linear-gradient(160deg, #f6efe2, #ece2d2 70%, #e2d6c4);
  border:1px solid rgba(60,40,90,.25);
  box-shadow:0 30px 70px rgba(0,0,0,.6), 0 2px 0 rgba(255,255,255,.5) inset,
    0 0 0 3px rgba(255,255,255,.06)}
/* CORE shows the slip by flipping .hidden (the shell's own rule), so the arrival
   is an ANIMATION, not a transition - it plays fresh every time the node returns */
@keyframes g-ir-slipin{from{opacity:0;transform:translate(-50%,-44%) rotate(.6deg)}to{opacity:1;transform:translate(-50%,-50%) rotate(-.4deg)}}
/* the tape + the seal are decoration: pointer-events:none and UNDER the options
   (Law II - nothing decorative may sit between a finger and a button) */
.g-ir-stop::before{content:"";position:absolute;left:14px;top:-7px;width:54px;height:14px;border-radius:2px;z-index:0;pointer-events:none;
  background:rgba(255,105,180,.55);transform:rotate(-4deg);box-shadow:0 1px 3px rgba(0,0,0,.25)}
.g-ir-stop::after{content:"";position:absolute;right:16px;bottom:14px;width:54px;height:54px;border-radius:50%;z-index:0;pointer-events:none;
  border:2px dashed rgba(212,72,143,.45);opacity:.55;transform:rotate(-12deg)}
.g-ir-stage[data-phase="stop"] .g-ir-stop,.g-ir-stage[data-phase="verdict"] .g-ir-stop{
  opacity:1;pointer-events:auto;transform:translate(-50%,-50%) rotate(-.4deg);
  animation:g-ir-slipin .42s cubic-bezier(.2,.9,.25,1.05) 1}
.g-ir-stage[data-phase="stop"] .g-ir-montage,.g-ir-stage[data-phase="verdict"] .g-ir-montage{opacity:.42;
  box-shadow:0 0 0 1px rgba(0,0,0,.6), 0 24px 60px rgba(0,0,0,.7)}
.g-ir-stage[data-phase="stop"] .g-ir-veil,.g-ir-stage[data-phase="verdict"] .g-ir-veil{opacity:1}
.g-ir-stage[data-phase="stop"] .g-ir-backdrop::before,.g-ir-stage[data-phase="verdict"] .g-ir-backdrop::before{
  transform:translateX(-50%) scaleX(.55);opacity:.5}
.g-ir-q{position:relative;z-index:1;margin:0;font-family:var(--disp);font-size:clamp(15px,2.3vmin,21px);letter-spacing:.04em;line-height:1.3;
  color:#2a2144;text-shadow:0 1px 0 rgba(255,255,255,.6)}
.g-ir-q::before{content:"";display:inline-block;width:10px;height:10px;margin:0 10px 1px 0;border-radius:50%;
  background:var(--pink);box-shadow:0 0 10px rgba(255,105,180,.6)}
.g-ir-opts{position:relative;z-index:1;display:grid;grid-template-columns:1fr 1fr;gap:10px;margin:0;padding:0;list-style:none}
.g-ir-opt{position:relative;display:flex;align-items:center;gap:12px;min-height:52px;padding:10px 12px 10px 52px;
  border-radius:8px;text-align:left;cursor:pointer;font:600 14px/1.3 var(--body);color:#241d3a;
  background:linear-gradient(180deg, rgba(255,255,255,.55), rgba(255,255,255,.18));
  border:1px solid rgba(60,40,90,.28);box-shadow:0 2px 0 rgba(60,40,90,.18);
  transition:transform .14s ease, box-shadow .2s ease, background .2s ease}
.g-ir-opt:hover{transform:translateY(-1px);box-shadow:0 4px 10px rgba(60,40,90,.22)}
.g-ir-opt:focus-visible{outline:2px solid var(--pink);outline-offset:2px}
.g-ir-opt:active{transform:translateY(1px);box-shadow:0 0 0 rgba(0,0,0,0)}
/* the keycap is CORE's .g-ir-opt-n span (1-4) drawn as a key - Law IV, the glyph
   is the truth; .g-ir-opt-t is the label the trickster may repaint */
.g-ir-opt-n{position:absolute;left:10px;top:50%;width:30px;height:30px;margin-top:-15px;
  display:flex;align-items:center;justify-content:center;border-radius:7px;
  font:800 13px/1 var(--mono);color:var(--ink);
  background:linear-gradient(180deg, var(--panel2), var(--navy));
  border:1px solid var(--line);border-bottom-width:3px;box-shadow:0 2px 0 rgba(0,0,0,.45);pointer-events:none}
.g-ir-opt-t{flex:1 1 auto;min-width:0}
/* the audition button for sting options: a real button that NEVER selects */
.g-ir-hear{flex:0 0 auto;margin-left:auto;padding:5px 9px;border-radius:999px;cursor:pointer;
  font:700 10px/1 var(--mono);letter-spacing:.12em;text-transform:uppercase;color:#3b2a5a;
  background:rgba(184,166,232,.35);border:1px solid rgba(90,60,140,.35)}
.g-ir-hear:hover{background:rgba(184,166,232,.55)}
.g-ir-hear:focus-visible{outline:2px solid var(--pink);outline-offset:1px}
/* the 44px thumb rule is for a TEXT option's little icon only - scoped away
   from the media options below, which are the whole point of their card */
.g-ir-opt:not(.g-ir-opt-media) img,.g-ir-opt:not(.g-ir-opt-media) video{display:block;width:44px;height:44px;object-fit:cover;border-radius:6px;pointer-events:none}
/* ---- MEDIA OPTIONS: the card asks with pictures ---------------------------
   SPIRAL / WALL_PICK / WALL_TWICE / WALL_GONE render a preview instead of a
   label. There is deliberately NO .g-ir-opt-t inside one, which is how the
   trickster's Unreliable Label folds on these cards - it has no text surface to
   lie on. Never a filter and never a blend on a face: a preview can hold a live
   decode (PERF LAW, web CLAUDE.md trap 36). */
.g-ir-stop[data-kind="media"]{width:min(720px,94%)}
.g-ir-opt-media{align-items:stretch;flex-direction:column;gap:6px;padding:10px 10px 8px;min-height:0}
.g-ir-opt-media .g-ir-opt-n{top:8px;margin-top:0}
.g-ir-opt-face{position:relative;width:100%;aspect-ratio:16/10;max-height:22vh;border-radius:6px;overflow:hidden;
  background:#0b0a16;border:1px solid rgba(60,40,90,.35);pointer-events:none}
.g-ir-opt-face img,.g-ir-opt-face video,.g-ir-opt-face .g-ir-media{position:absolute;inset:0;width:100%;height:100%;object-fit:cover;display:block}
/* a spiral preview is the asset AS SHIPPED - square, unspun, uncoloured. The
   wash showed it screen-blended and turning; the arm shape and the palette are
   what the player actually recalls, so that is what the option shows. */
.g-ir-opt-face[data-media="spiral"]{aspect-ratio:1/1;width:clamp(84px,14vh,132px);max-height:none;margin:0 auto;
  background:#0b0716;border:2px solid rgba(184,166,232,.55)}
.g-ir-opt-media.is-true .g-ir-opt-face{box-shadow:0 0 0 3px rgba(240,194,75,.8)}
.g-ir-opt-media.is-wrong .g-ir-opt-face{opacity:.55}
/* WALL_SEEN's single face: the thing being asked about, above two text options */
.g-ir-q-face{position:relative;z-index:1;width:min(360px,100%);aspect-ratio:16/10;max-height:24vh;margin:0 auto;
  border-radius:6px;overflow:hidden;background:#0b0a16;border:1px solid rgba(60,40,90,.35)}
.g-ir-q-face img,.g-ir-q-face video{position:absolute;inset:0;width:100%;height:100%;object-fit:cover}
/* THE PROOF: the tiles that really wore the answer, ringed at the verdict */
.g-ir-tile.is-truth{box-shadow:0 0 0 3px var(--gold),0 0 22px rgba(240,194,75,.7);z-index:3}
/* THE SHROUD: a wall question's answer is ON the wall, so the wall goes out
   while the card is up and comes back for the verdict (which IS the proof) */
.g-ir-stage[data-shroud="1"] .g-ir-montage{opacity:.03;transition-duration:.25s}
/* a phrase option can be a whole sentence (Echo's lesson): clamp, never cut */
.g-ir-opt-t{display:-webkit-box;-webkit-box-orient:vertical;-webkit-line-clamp:3;overflow:hidden;overflow-wrap:anywhere}
.g-ir-stop[data-family="HEARD"] .g-ir-opt{font-size:clamp(12px,1.6vmin,14px)}
@media (pointer: coarse){
  .g-ir-opt-face{max-height:18vh}
  .g-ir-opt-face[data-media="spiral"]{width:clamp(68px,12vh,108px)}
}
html.ae-touch .g-ir-opt-face{max-height:18vh}
.g-ir-opt.is-picked,.g-ir-opt[aria-pressed="true"]{background:linear-gradient(180deg, rgba(255,105,180,.28), rgba(255,105,180,.12));
  border-color:rgba(212,72,143,.6)}
.g-ir-opt.is-true,.g-ir-opt.is-correct,.g-ir-opt.is-truth,.g-ir-opt[data-verdict="correct"]{
  background:linear-gradient(180deg, rgba(240,194,75,.4), rgba(240,194,75,.14));border-color:rgba(200,150,40,.7);
  box-shadow:0 0 0 2px rgba(240,194,75,.35), 0 0 18px rgba(240,194,75,.35)}
.g-ir-opt.is-wrong,.g-ir-opt[data-verdict="wrong"]{background:linear-gradient(180deg, rgba(120,60,90,.28), rgba(120,60,90,.1));
  border-color:rgba(120,60,90,.5);color:#5b4a6d;text-decoration:line-through}
.g-ir-opt:disabled,.g-ir-opt.is-dead{cursor:default;opacity:.85}
/* the answer ring: a conic face that reads --ir-left (CORE, 1 full -> 0 out;
   --ir-t is the older name) with the seconds digit on a paper disc in the middle */
.g-ir-timer{position:absolute;right:-14px;top:-14px;width:44px;height:44px;border-radius:50%;z-index:2;
  display:flex;align-items:center;justify-content:center;font:800 13px/1 var(--mono);color:#2a2144;
  background:
    radial-gradient(circle, #f6efe2 13px, transparent 14px),
    conic-gradient(var(--pink) calc(var(--ir-left, var(--ir-t)) * 360deg), rgba(60,40,90,.18) 0);
  box-shadow:0 0 14px rgba(255,105,180,.35);transition:--ir-left .2s linear, --ir-t .2s linear}
.g-ir-timer:empty{font-size:0}
.g-ir-timer.is-low,.g-ir-timer[data-low="1"]{animation:g-ir-timer-low .5s ease-in-out infinite alternate}
@keyframes g-ir-timer-low{from{box-shadow:0 0 10px rgba(255,105,180,.35)}to{box-shadow:0 0 22px rgba(255,105,180,.9)}}
/* the truth replay: the ledger's last beads on a ribbon, the answer's own moment
   pinned in gold, a planted decoy struck through - proof, not decoration */
.g-ir-truth{position:relative;z-index:1;margin:2px 0 0;padding-top:10px;border-top:1px dashed rgba(60,40,90,.3)}
.g-ir-ribbon{display:flex;flex-wrap:wrap;gap:5px;align-items:center}
.g-ir-bead{display:inline-flex;align-items:center;max-width:100%;padding:3px 8px;border-radius:999px;
  font:700 10.5px/1.2 var(--mono);letter-spacing:.08em;text-transform:uppercase;color:#3b2a5a;
  background:rgba(90,60,140,.12);border:1px solid rgba(90,60,140,.25);white-space:nowrap;overflow:hidden;text-overflow:ellipsis}
.g-ir-bead[data-ch="sub_flash"],.g-ir-bead[data-ch="word"]{background:rgba(255,105,180,.16);border-color:rgba(212,72,143,.4)}
.g-ir-bead[data-ch="audio_trigger"],.g-ir-bead[data-ch="sting"]{background:rgba(184,166,232,.22);border-color:rgba(120,100,190,.4)}
.g-ir-bead[data-ch="layout"]{background:rgba(60,40,90,.1);border-style:dashed}
.g-ir-bead.is-pin{color:#2a2144;background:linear-gradient(180deg, rgba(240,194,75,.55), rgba(240,194,75,.25));
  border-color:rgba(200,150,40,.8);box-shadow:0 0 0 2px rgba(240,194,75,.3), 0 0 12px rgba(240,194,75,.4)}
.g-ir-bead.is-plant{text-decoration:line-through;opacity:.8;border-style:dashed}
.g-ir-truth-line{margin:8px 0 0;font-size:12px;font-style:italic;color:#4b3f66}
/* the SHARED shell stamp (.arc-stamp) is inline-block, so appended to the slip it
   lands as the last flex row. Pin it over the slip's centre instead (no transform:
   the shell's .pop keyframe owns that property; pointer-events:none is the shell's). */
.g-ir-stop > .arc-stamp{position:absolute;left:0;right:0;top:42%;margin:0 auto;width:max-content;
  max-width:90%;z-index:3;text-align:center}

/* ---- proctor line + end card ---------------------------------------------- */
.g-ir-flashwell{position:absolute;inset:0;overflow:hidden;pointer-events:none;z-index:6;
  opacity:1;transition:opacity .18s ease}
.g-ir-flashwell *{pointer-events:none}
/* ---- THE SUB WORD, LEGIBLE (owner: "the text on the sub is pretty faded") --
   NOT an intensity raise - the node's opacity is still the engine's --ae-alpha
   and the CEILING RULE is untouched. What this buys is CONTRAST: a plain rgba
   plate under the letters, a hard outline, and a size that reads across a
   bright moving wall. GAME-LOCAL by construction (the nodes land in our own
   .g-ir-flashwell / .g-ir-stop, both under .g-ir-stage), so no other class
   inherits it. No filter, no blend - the wall behind is a live decode. */
.g-ir-stage .ae-sub-word{font-size:clamp(36px,6.5vw,84px);line-height:1.05;white-space:normal;
  max-width:min(72vw,16ch);text-align:center;text-wrap:balance;padding:.14em .5em;border-radius:.16em;
  color:#fff;letter-spacing:.08em;
  background:rgba(10,6,22,.78);
  box-shadow:0 0 0 2px rgba(255,105,180,.38),0 16px 44px rgba(0,0,0,.55);
  text-shadow:0 0 2px #000,0 0 2px #000,0 2px 0 rgba(0,0,0,.65),0 0 18px var(--ae-pink)}
.g-ir-stage .ae-sub-stamp{border-width:3px}
.g-ir-msg{position:relative;z-index:2;margin:0;min-height:1.4em;text-align:center;
  font-family:var(--mono);font-size:11px;letter-spacing:.16em;text-transform:uppercase;
  color:var(--ink-dim);transition:opacity .3s ease}
.g-ir-msg:empty{opacity:0}
/* the end card: a title + k/v rows (.g-ir-end-row > .g-ir-end-k + .g-ir-end-v);
   the node IS the panel, centred over the dimmed hall */
.g-ir-end{position:absolute;left:50%;top:50%;transform:translate(-50%,-50%);z-index:8;
  width:min(460px, 92%);display:flex;flex-direction:column;gap:4px;padding:18px 20px 16px;
  border-radius:16px;background:color-mix(in srgb, var(--navy), transparent 6%);
  border:1px solid color-mix(in srgb, var(--lav), var(--line) 50%);
  box-shadow:0 18px 50px rgba(0,0,0,.55), 0 0 34px hsla(var(--ir-hue-b),70%,70%,.25);
  animation:g-ir-endin .5s ease-out 1}
@keyframes g-ir-endin{from{opacity:0;transform:translate(-50%,-46%)}to{opacity:1;transform:translate(-50%,-50%)}}
.g-ir-end-title{font-family:var(--disp);font-size:16px;letter-spacing:.14em;text-transform:uppercase;
  margin:0 0 8px;color:var(--ink);text-align:center;text-shadow:0 0 18px color-mix(in srgb, var(--pink), transparent 60%)}
.g-ir-end-row{display:flex;justify-content:space-between;align-items:baseline;gap:12px;
  font-family:var(--mono);font-size:12px;letter-spacing:.1em;text-transform:uppercase;color:var(--ink-dim);
  padding:5px 0;border-bottom:1px dashed color-mix(in srgb, var(--line), transparent 20%)}
.g-ir-end-k{color:var(--ink-faint)}
.g-ir-end-v{color:var(--ink);font-variant-numeric:tabular-nums}
.g-ir-end-best .g-ir-end-v,.g-ir-end-perfect .g-ir-end-v{color:var(--gold);text-shadow:0 0 12px rgba(240,194,75,.45)}
.g-ir-end-plants .g-ir-end-v{color:var(--lav)}
.g-ir-end-blank .g-ir-end-v{color:var(--ink-faint);font-style:italic}
.g-ir-end-line{margin:2px 0 0;font-size:12px;color:var(--ink-dim)}
.g-ir-end .btn{margin:10px 4px 0}

/* ---- phases ----------------------------------------------------------------- */
.g-ir-stage[data-phase="briefing"] .g-ir-montage{opacity:.55}
.g-ir-stage[data-phase="ended"] .g-ir-montage{opacity:.3}
.g-ir-stage[data-phase="ended"] .g-ir-veil{opacity:1}
.g-ir-stage[data-phase="ended"] .g-ir-backdrop::before{opacity:.35}

/* ----------------------------------------------------- THE CLASS RULES SHEET */
/* Deck VI, Law IV: drawn, not told. CORE mounts .g-ir-howto > .g-ir-howto-title,
   .g-ir-howto-row > 3x .g-ir-howto-card (> .g-ir-howto-art[data-art=wall|freeze|answer],
   which may hold .g-ir-howto-ico[data-ico=flash|spiral|bubble] + .g-ir-howto-cap),
   .g-ir-howto-note, .g-ir-go. Every vignette is PURE CSS on the empty art box -
   no image, no text, nothing to translate. The sheet takes NO pointer events;
   only the GO button re-enables them for itself. */
.g-ir-howto{position:absolute;left:50%;top:50%;transform:translate(-50%,-50%);z-index:9;
  width:min(640px,92vw);max-height:88vh;overflow:auto;pointer-events:none;
  display:flex;flex-direction:column;gap:6px;padding:22px 24px 20px;border-radius:16px;
  background:linear-gradient(180deg,rgba(26,26,54,.94),rgba(14,14,32,.96));
  border:1px solid var(--line);
  box-shadow:0 30px 80px rgba(0,0,0,.55), 0 0 44px rgba(255,105,180,.10)}
.g-ir-howto-title{margin:0 0 10px;text-align:center;font-family:var(--disp);font-size:clamp(16px,2.4vmin,20px);
  letter-spacing:.2em;text-transform:uppercase;color:var(--pink);
  text-shadow:0 0 22px rgba(255,105,180,.45)}
.g-ir-howto-row{display:grid;grid-template-columns:repeat(3, minmax(0,1fr));gap:14px}
.g-ir-howto-card{display:flex;flex-direction:column;gap:8px;min-width:0}
.g-ir-howto-cap{margin:0;font-size:12px;line-height:1.45;color:var(--ink-dim);text-align:center}
.g-ir-howto-note{margin:8px 0 0;text-align:center;font-family:var(--mono);font-size:11px;letter-spacing:.12em;
  text-transform:uppercase;color:var(--lav)}
/* the little screen: a lit rectangle every vignette is set on */
.g-ir-howto-art{position:relative;width:100%;aspect-ratio:5/3;border-radius:8px;overflow:hidden;
  background:#0c0b1c;border:1px solid rgba(184,166,232,.35);box-shadow:0 0 14px rgba(184,166,232,.18)}
.g-ir-howto-art::before,.g-ir-howto-art::after{content:"";position:absolute;pointer-events:none}
/* 1 - THE WALL: a grid of tiles, one of them mid-turn, with three of the pool's
   own icons drawn over it (a flash, a spiral, a bubble - CORE mounts them as
   .g-ir-howto-ico[data-ico], the only nodes inside an art box) */
.g-ir-howto-art[data-art="wall"]::before,.g-ir-howto-art[data-art="freeze"]::before{inset:6px 7px;
  background:linear-gradient(135deg, var(--pink), var(--lav) 55%, var(--gold));
  -webkit-mask:linear-gradient(#000 0 0) 0 0/23% 31% space;mask:linear-gradient(#000 0 0) 0 0/23% 31% space;
  animation:g-ir-hw-blink 2.4s ease-in-out infinite}
@keyframes g-ir-hw-blink{0%,100%{opacity:.45}50%{opacity:.95}}
/* the tile that is mid-swap: one cell crossfading on its own beat */
.g-ir-howto-art[data-art="wall"]::after{left:31%;top:36%;width:23%;height:31%;border-radius:2px;
  background:linear-gradient(200deg, #fff, var(--lav));box-shadow:0 0 12px rgba(255,255,255,.5);
  animation:g-ir-hw-turn 2.4s ease-in-out infinite}
@keyframes g-ir-hw-turn{0%,40%{opacity:0}52%{opacity:.95}88%{opacity:.95}100%{opacity:0}}
.g-ir-howto-ico{position:absolute;display:block;pointer-events:none;z-index:2}
.g-ir-howto-ico[data-ico="flash"]{left:9%;bottom:14%;width:26%;height:22%;border-radius:3px;
  background:linear-gradient(180deg, #fff, #ffd9ee);box-shadow:0 0 16px rgba(255,255,255,.9);
  animation:g-ir-hw-flash 2.4s ease-out infinite}
@keyframes g-ir-hw-flash{0%,58%{opacity:0;transform:scale(.82)}64%{opacity:1;transform:scale(1.06)}
  80%{opacity:1;transform:scale(1)}90%,100%{opacity:0;transform:scale(1)}}
.g-ir-howto-ico[data-ico="spiral"]{right:8%;top:12%;width:24%;aspect-ratio:1;border-radius:50%;
  background:conic-gradient(from 0deg, var(--pink), var(--lav) 42%, rgba(255,255,255,.05) 72%, var(--pink));
  -webkit-mask:radial-gradient(circle, #000 60%, transparent 65%);mask:radial-gradient(circle, #000 60%, transparent 65%);
  animation:g-ir-hw-spin 4s linear infinite}
@keyframes g-ir-hw-spin{to{transform:rotate(360deg)}}
.g-ir-howto-ico[data-ico="bubble"]{right:13%;bottom:11%;width:19%;aspect-ratio:1;border-radius:50%;
  background:radial-gradient(circle at 34% 30%, rgba(255,255,255,.85), rgba(255,105,180,.35) 48%,
    rgba(184,166,232,.18) 72%, transparent 78%);
  animation:g-ir-hw-float 3.2s ease-in-out infinite alternate}
@keyframes g-ir-hw-float{from{transform:translateY(8%)}to{transform:translateY(-16%)}}
/* 2 - the freeze: the tiles go dark and a slip rises from the desk */
.g-ir-howto-art[data-art="freeze"]::before{animation:g-ir-hw-dim 3s ease-in-out infinite}
@keyframes g-ir-hw-dim{0%,35%{opacity:.9}50%,100%{opacity:.25}}
.g-ir-howto-art[data-art="freeze"]::after{left:50%;bottom:-6px;width:46%;height:52%;transform:translate(-50%,60%) rotate(2deg);
  border-radius:3px;border:1px solid rgba(60,40,90,.3);box-shadow:0 8px 18px rgba(0,0,0,.6);
  background:
    linear-gradient(rgba(42,33,68,.55) 0 0) 14% 28%/46% 8% no-repeat,
    linear-gradient(rgba(42,33,68,.3) 0 0) 14% 52%/62% 8% no-repeat,
    linear-gradient(rgba(42,33,68,.3) 0 0) 14% 74%/40% 8% no-repeat,
    linear-gradient(160deg,#f6efe2,#e2d6c4);
  opacity:0;animation:g-ir-hw-slip 3s cubic-bezier(.2,.9,.25,1.05) infinite}
@keyframes g-ir-hw-slip{0%,38%{opacity:0;transform:translate(-50%,60%) rotate(2deg)}52%{opacity:1;transform:translate(-50%,6%) rotate(-1deg)}
  92%{opacity:1;transform:translate(-50%,6%) rotate(-1deg)}100%{opacity:0}}
/* 3 - the answer: four keycaps, one lit, the slip's ring running down */
.g-ir-howto-art[data-art="answer"]::before{left:12%;top:22%;width:44%;height:56%;
  background:
    radial-gradient(circle at 25% 25%, rgba(255,105,180,.9) 0 14%, transparent 15%) 0 0/100% 100% no-repeat,
    linear-gradient(180deg, var(--panel2), var(--navy));
  -webkit-mask:linear-gradient(#000 0 0) 0 0/44% 44% space;mask:linear-gradient(#000 0 0) 0 0/44% 44% space;
  border-radius:4px;animation:g-ir-hw-press 2.6s ease-in-out infinite}
@keyframes g-ir-hw-press{0%,60%{transform:translateY(0)}68%{transform:translateY(2px)}82%,100%{transform:translateY(0)}}
.g-ir-howto-art[data-art="answer"]::after{right:12%;top:50%;width:30%;aspect-ratio:1;transform:translateY(-50%);border-radius:50%;
  background:conic-gradient(var(--pink) 0deg, var(--pink) var(--ir-hw-k), rgba(255,255,255,.08) 0);
  -webkit-mask:radial-gradient(circle, transparent 38%, #000 42%);mask:radial-gradient(circle, transparent 38%, #000 42%);
  animation:g-ir-hw-ring 2.6s linear infinite}
@keyframes g-ir-hw-ring{from{--ir-hw-k:360deg}to{--ir-hw-k:0deg}}
.g-ir-go{margin:14px auto 0;pointer-events:auto;cursor:pointer;padding:10px 26px;border-radius:999px;
  font:700 13px/1 var(--body);letter-spacing:.14em;text-transform:uppercase;color:#1a1028;
  background:linear-gradient(180deg, #ffd1ea, var(--pink));border:1px solid rgba(255,255,255,.35);
  box-shadow:0 0 22px rgba(255,105,180,.5), 0 6px 16px rgba(0,0,0,.45);
  animation:g-ir-hw-gopulse 1.8s ease-in-out infinite}
.g-ir-go:focus-visible{outline:2px solid #fff;outline-offset:3px}
@keyframes g-ir-hw-gopulse{50%{box-shadow:0 0 34px rgba(255,105,180,.85), 0 6px 16px rgba(0,0,0,.45)}}
@media (max-width: 560px){.g-ir-howto-row{grid-template-columns:1fr}.g-ir-howto-art{max-width:220px;margin:0 auto}}

/* ---- THE PAYOUT LAYER (CORE's reward pixels) ------------------------------
   One solid gold bloom over the whole stage when a fully-correct stop's
   variable-ratio roll fires. GAME-LOCAL CHROME by law: it is not an engine
   kind, it writes no ledger entry, and it wears no blend mode / no filter /
   no backdrop-filter - a composited opacity keyframe is the entire cost, so
   it is legal over a wall of live decodes. z 3: over the screen, under the
   flashwell (6) and the slip (7), so it can never obscure a quiz card or be
   read as one. .is-big is the jackpot stop - stronger gradient, same law
   (trap 37: never a backtick inside this template). */
.g-ir-payout{position:absolute;inset:0;pointer-events:none;z-index:3;opacity:0;
  background:radial-gradient(circle at 50% 40%, rgba(240,194,75,.34), transparent 70%)}
.g-ir-payout.is-big{background:radial-gradient(circle at 50% 40%, rgba(240,194,75,.5), transparent 72%)}
.g-ir-payout.is-on{animation:g-ir-payout 900ms ease-out 1 both}
@keyframes g-ir-payout{0%{opacity:0}22%{opacity:1}100%{opacity:0}}

/* ---- reduced motion (both gates): keyframes die, transitions survive -------
   ONE exception, and it is deliberate: the wall's face swap survives as a PLAIN
   CROSSFADE. A fade is not motion (this file's own doctrine), and Law III says
   the wall is never still - killing the swap outright would make every turn a
   hard cut, which is more jarring than the fade, not less. montage.js already
   forces the 'fade' style and a longer dwell on this rung. */
html.arc-reduced .g-ir-stage *{animation:none !important}
html.arc-reduced .g-ir-tile.is-swapping .g-ir-face.is-in{animation:g-ir-in var(--ir-swap,900ms) linear both !important}
html.arc-reduced .g-ir-tile.is-swapping .g-ir-face.is-out{animation:g-ir-out var(--ir-swap,900ms) linear both !important}
html.arc-reduced .g-ir-howto-art::before,html.arc-reduced .g-ir-howto-art::after{animation:none !important;opacity:1}
html.arc-reduced .g-ir-howto-art[data-art="freeze"]::after{transform:translate(-50%,6%) rotate(-1deg)}
html.arc-reduced .g-ir-howto-art[data-art="wall"]::after{opacity:.95}
html.arc-reduced .g-ir-howto-ico{animation:none !important;opacity:1}
/* the payout survives reduced as a HALVED opacity fade - a fade is not motion
   (this file's own doctrine), and the reward has to exist on every rung */
html.arc-reduced .g-ir-payout{background:radial-gradient(circle at 50% 40%, rgba(240,194,75,.17), transparent 70%)}
html.arc-reduced .g-ir-payout.is-big{background:radial-gradient(circle at 50% 40%, rgba(240,194,75,.25), transparent 72%)}
html.arc-reduced .g-ir-payout.is-on{animation:g-ir-payout 450ms ease-out 1 both !important}
/* ---- 16 THE UNLISTED FRAME (the seep) ------------------------------------
   The campus plan, rendered the way the monitors downstairs render it: cold
   ground, dead-channel scanlines and blueprint linework. It is CSS, not an
   asset - one more tile-sized image on a wall that already carries a dozen
   decodes is a cost the pass-5 budget will not pay, and the look is four
   gradients and a border.

   It hangs INSIDE one .g-ir-tile, above both faces (z 3, over .is-in's z 2) and
   pointer-events:none, so it covers a face without ever touching one. Opacity
   only; no blend mode, no filter, nothing over a live decode. montage.js
   removes the node - there is no forwards fill anywhere near it. */
.g-ir-seep{position:absolute;inset:0;display:block;pointer-events:none;z-index:3;
  border-radius:inherit;overflow:hidden;opacity:0;
  background:var(--seep-deep,#0D2724);
  background-image:repeating-linear-gradient(180deg,rgba(143,224,206,.10) 0 1px,transparent 1px 3px);
  animation:g-ir-seep var(--ir-seep-ms,400ms) ease-out 1}
.g-ir-seep::after{content:'';position:absolute;inset:16%;
  border:1px solid var(--seep-blue,#8ECBFF);opacity:.85}
.g-ir-seep::before{content:'';position:absolute;left:16%;right:16%;top:52%;height:1px;
  background:var(--seep-blue,#8ECBFF);opacity:.45}
@keyframes g-ir-seep{0%{opacity:0}16%{opacity:.92}76%{opacity:.92}100%{opacity:0}}

@media (prefers-reduced-motion: reduce){
  .g-ir-stage *{animation:none !important}
  .g-ir-tile.is-swapping .g-ir-face.is-in{animation:g-ir-in var(--ir-swap,900ms) linear both !important}
  .g-ir-tile.is-swapping .g-ir-face.is-out{animation:g-ir-out var(--ir-swap,900ms) linear both !important}
  .g-ir-howto-art::before,.g-ir-howto-art::after{animation:none !important;opacity:1}
  .g-ir-howto-art[data-art="freeze"]::after{transform:translate(-50%,6%) rotate(-1deg)}
  .g-ir-howto-art[data-art="wall"]::after{opacity:.95}
  .g-ir-howto-ico{animation:none !important;opacity:1}
  .g-ir-payout{background:radial-gradient(circle at 50% 40%, rgba(240,194,75,.17), transparent 70%)}
  .g-ir-payout.is-big{background:radial-gradient(circle at 50% 40%, rgba(240,194,75,.25), transparent 72%)}
  .g-ir-payout.is-on{animation:g-ir-payout 450ms ease-out 1 both !important}
}
/* touch: bigger options, no hover lift */
@media (pointer: coarse){
  .g-ir-opt{min-height:58px}
  .g-ir-opt:hover{transform:none}
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
.g-ir-howto{pointer-events:auto}
.g-ir-go{position:sticky;bottom:0;z-index:3;align-self:stretch;margin-top:14px}

`;

/** Inject once per document. No-op headless (the DOM double has no head). */
export function injectInstantRecallStyle() {
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

export default injectInstantRecallStyle;
