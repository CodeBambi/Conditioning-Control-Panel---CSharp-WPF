/* ============================================================================
 * games/misdirection/style.js - the game injects its OWN stylesheet from JS.
 *
 * THE CON ARTIST'S TABLE. The class root is a full-viewport stage: a velvet
 * card table under one hanging lamp, an arc of shells on the felt, the
 * casino's arch of bulbs over it (casino.js paints the --md-n-* identity
 * props; the fallbacks below are the plain violet felt, so a disarmed casino
 * changes nothing). Composition, exactly the DOM contract:
 *
 *   .g-md-stage[data-phase][data-skin]  absolute inset:0, explicit ground,
 *                 padded under the shell's proctor strip; --md-table is the one
 *                 width every table-level thing is sized from
 *   .g-md-backdrop  the casino's lighting rig (pointer-events:none, z 0)
 *   .g-md-hud     four chips: round, clock, pot (gold), streak (pink)
 *   .g-md-table   the felt: a perspective slab with a metal rail; the arch,
 *                 the lamp and the casino's overlays live here
 *   .g-md-arc     the row of shells, each positioned from --x (a FRACTION 0..1
 *                 of the arc width, unitless: slot/(n-1)) and --slot; index.js
 *                 writes the vars (+ --n on the arc), this file owns the
 *                 transform + its slide transition
 *   .g-md-shell   a real <button> - THE hitbox - > .g-md-lid (the cup body that
 *                 lifts) .g-md-face > .g-md-media (what is under it) .g-md-tag
 *                 (the number tag - the ONLY node the trickster may lie on)
 *   .g-md-stake   bank / ride - real buttons, honest, never moved
 *   .g-md-msg / .g-md-flashwell / .g-md-end / .g-md-howto
 *
 * THE TRANSFORM LAW. index.js writes --x / --slot and nothing else about a
 * shell's position; this file owns the transform and its slide (the shuffle
 * swap rides a 280ms ease; index.js's swap beats land on the same number).
 * The transform of .g-md-shell is POSITIONAL ONLY: every lift, lean, sag,
 * feint and breath lives on the lid / face / tag children, never on the shell
 * node, so the hitbox never moves for a lie (Law II). Two decoration vars are
 * composed INTO the children for the decks:
 *   --md-feint     px lean of the cup BODY (trickster Fake Shuffle; the shell
 *                  node and its hitbox stay put)
 *   --md-n-ride    0..6 ride depth (casino; the rims glow deeper)
 *
 * SHELL STATES (index.js toggles; this file draws):
 *   .is-lifted    the lid is up (reveal / verdict / decoy)
 *   .is-decoy     a decoy lift (the lid only half-lifts and snaps back)
 *   .is-fake      the decoy shows a convincing fake (NOT styled - a style
 *                 would be a tell; the face media is index.js's lie)
 *   .is-picked    the player's pick; .is-picked.is-true = the hit (gold bloom,
 *                 the face pops), .is-picked:not(.is-true) = the miss (the lid
 *                 lifts on an empty face, dims)
 *   .is-true      the true shell; alone (after a miss) = the under-rim tease
 *   .is-tell      an occluded swap's peripheral mark (a rim flash)
 *   .is-swapping  mid-swap (the body leans into travel, the slide eases)
 *   .g-md-tk-melt / .g-md-tk-feint  the trickster's wax sag / feint lean
 * The stage wears [data-occluding="1"] during a blackout beat (the felt goes
 * dark; the shells keep a hairline rim so a peripheral tell survives).
 *
 * THREE SKINS (data-skin on the stage; the shell is one DOM shape for all):
 *   themed    velvet cups: a dome in the felt's hue with a metal rim and a
 *             hanging brass number tag; the lid lifts with a spring overshoot
 *   minimal   flat outlined rounded rects, one ink, no gradient
 *   contrast  black body, thick white rims, big tags: rims stay trackable
 *             under any wash
 *
 * THE SHEET (Deck VI, Law IV): .g-md-howto is drawn - four vignettes (a lid
 * lifts / cups slide / a hand or keycap points / the coin stack splits bank
 * or ride) and the GO button, the only live thing on it. THE END CARD: the
 * grade arrives as an OBJECT - .g-md-end-seal is a wax seal bearing the letter.
 *
 * NOTHING IS EVER STILL (Law III): the lamp sways, the weave drifts, the cups
 * breathe, the marquee crawls. REDUCED MOTION twice over (html.arc-reduced +
 * the media query): only the slide transition and the lid lift survive.
 * .g-md-stage.suspended freezes every animation.
 * ==========================================================================*/

const STYLE_ID = 'g-md-style';

export const STYLE_TEXT = `
/* ---- registered decoration props: numbers interpolate, so the journey
   MORPHS instead of snapping ------------------------------------------------ */
@property --md-n-a-diamond{syntax:'<number>';inherits:true;initial-value:0}
@property --md-n-a-herringbone{syntax:'<number>';inherits:true;initial-value:0}
@property --md-n-a-damask{syntax:'<number>';inherits:true;initial-value:0}
@property --md-n-a-pinstripe{syntax:'<number>';inherits:true;initial-value:0}
@property --md-n-a-lace{syntax:'<number>';inherits:true;initial-value:0}
@property --md-n-tilt{syntax:'<number>';inherits:true;initial-value:-12}
@property --md-n-scale{syntax:'<number>';inherits:true;initial-value:1}
@property --md-n-ride{syntax:'<number>';inherits:true;initial-value:0}
@property --md-feint{syntax:'<number>';inherits:false;initial-value:0}
@property --md-hw-k{syntax:'<angle>';inherits:false;initial-value:270deg}

/* ---- the stage: the whole window is the parlour ---------------------------- */
.g-md-stage{position:absolute;inset:0;overflow:hidden;display:flex;flex-direction:column;
  align-items:center;gap:8px;padding:72px 18px 14px;color:var(--ink);
  --md-table:min(calc(100vw - 48px), 980px);
  --md-shell:clamp(64px, calc(var(--md-table) / 6.2), 150px);
  --md-felt:var(--md-n-felt, color-mix(in srgb, var(--lav), #0b0618 74%));
  --md-felt-deep:var(--md-n-felt-deep, color-mix(in srgb, var(--lav), #06030f 86%));
  --md-felt-lit:var(--md-n-felt-lit, color-mix(in srgb, var(--pink), #1a0f2e 60%));
  --md-la:var(--md-n-la, color-mix(in srgb, var(--lav), transparent 72%));
  --md-lb:var(--md-n-lb, color-mix(in srgb, var(--pink), transparent 78%));
  --md-glow:var(--md-n-glow, var(--pink));
  --md-metal:var(--md-n-metal, #d4a64a);
  --md-metal-dk:var(--md-n-metal-dk, #7a5a1c);
  --md-breath:var(--md-n-breath, 9s);
  --md-drift:var(--md-n-drift, 24s);
  transition:--md-n-a-diamond 3.2s ease, --md-n-a-herringbone 3.2s ease, --md-n-a-damask 3.2s ease,
    --md-n-a-pinstripe 3.2s ease, --md-n-a-lace 3.2s ease, --md-n-tilt 4s ease, --md-n-scale 4s ease, --md-n-ride .6s ease;
  background:
    radial-gradient(110% 60% at 50% -6%, var(--md-la), transparent 62%),
    radial-gradient(80% 50% at 50% 112%, var(--md-lb), transparent 60%),
    color-mix(in srgb, var(--ground), black 46%)}
.g-md-stage.suspended *{animation-play-state:paused !important}

/* ---- the backdrop: the casino's lighting rig (decoration only) ------------- */
.g-md-backdrop{position:absolute;inset:0;pointer-events:none;z-index:0;overflow:hidden}
.g-md-backdrop *{pointer-events:none}
.g-md-bd{position:absolute;inset:-6%;opacity:1}
.g-md-bd::before{content:"";position:absolute;inset:0}
/* the felt: the lamp pool over deep velvet; it breathes */
.g-md-bd-felt::before{
  background:radial-gradient(60% 48% at 50% 38%, var(--md-felt-lit), var(--md-felt) 55%, var(--md-felt-deep) 100%);
  animation:g-md-breathe var(--md-breath) ease-in-out infinite alternate}
@keyframes g-md-breathe{from{opacity:.82;transform:scale(1)}to{opacity:1;transform:scale(1.03)}}
/* the weaves: five families, one prop each, all drifting on their own bearing */
.g-md-bd-diamond,.g-md-bd-herringbone,.g-md-bd-damask,.g-md-bd-pinstripe,.g-md-bd-lace{mix-blend-mode:soft-light}
.g-md-bd-diamond::before{opacity:var(--md-n-a-diamond,0);
  background:repeating-linear-gradient(calc(45deg + var(--md-n-tilt,0) * 1deg), rgba(255,255,255,.12) 0 2px, transparent 2px calc(26px * var(--md-n-scale,1))),
    repeating-linear-gradient(calc(-45deg + var(--md-n-tilt,0) * 1deg), rgba(255,255,255,.12) 0 2px, transparent 2px calc(26px * var(--md-n-scale,1)));
  animation:g-md-weave var(--md-drift) linear infinite}
.g-md-bd-herringbone::before{opacity:var(--md-n-a-herringbone,0);
  background:repeating-linear-gradient(calc(60deg + var(--md-n-tilt,0) * 1deg), rgba(255,255,255,.1) 0 3px, transparent 3px calc(14px * var(--md-n-scale,1))),
    repeating-linear-gradient(calc(120deg + var(--md-n-tilt,0) * 1deg), rgba(0,0,0,.18) 0 3px, transparent 3px calc(14px * var(--md-n-scale,1)));
  animation:g-md-weave calc(var(--md-drift) * 1.3) linear infinite reverse}
.g-md-bd-damask::before{opacity:var(--md-n-a-damask,0);
  background:radial-gradient(circle at 25% 25%, rgba(255,255,255,.14) 0 6%, transparent 7% 30%, rgba(255,255,255,.08) 31% 34%, transparent 35%),
    radial-gradient(circle at 75% 75%, rgba(255,255,255,.14) 0 6%, transparent 7% 30%, rgba(255,255,255,.08) 31% 34%, transparent 35%);
  background-size:calc(120px * var(--md-n-scale,1)) calc(120px * var(--md-n-scale,1));
  transform:rotate(calc(var(--md-n-tilt,0) * .5deg));
  animation:g-md-weave calc(var(--md-drift) * 1.6) linear infinite}
.g-md-bd-pinstripe::before{opacity:var(--md-n-a-pinstripe,0);
  background:repeating-linear-gradient(calc(90deg + var(--md-n-tilt,0) * 1deg), rgba(255,255,255,.16) 0 1px, transparent 1px calc(11px * var(--md-n-scale,1)));
  animation:g-md-weave calc(var(--md-drift) * .9) linear infinite}
.g-md-bd-lace::before{opacity:var(--md-n-a-lace,0);
  background:radial-gradient(circle, transparent 40%, rgba(255,255,255,.14) 41% 46%, transparent 47%);
  background-size:calc(48px * var(--md-n-scale,1)) calc(48px * var(--md-n-scale,1));
  transform:rotate(calc(var(--md-n-tilt,0) * 1deg));
  animation:g-md-weave calc(var(--md-drift) * 1.15) linear infinite reverse}
@keyframes g-md-weave{from{background-position:0 0}to{background-position:160px 90px}}
/* the smoke: two soft blobs drifting through the lamp light */
.g-md-bd-smoke::before{opacity:.55;
  background:radial-gradient(30% 22% at 30% 40%, rgba(255,255,255,.07), transparent 70%),
    radial-gradient(26% 20% at 70% 55%, rgba(255,255,255,.06), transparent 70%);
  animation:g-md-smoke calc(var(--md-drift) * 1.4) ease-in-out infinite alternate}
@keyframes g-md-smoke{from{transform:translate(-3%,1%) scale(1)}to{transform:translate(3%,-2%) scale(1.08)}}
/* the dark: bust / dim-out breathe this in; the vignette is always there */
.g-md-bd-dark{opacity:0;background:rgba(4,2,10,.7);transition:opacity .5s ease}
.g-md-bd-vig{background:radial-gradient(80% 70% at 50% 50%, transparent 55%, rgba(2,1,8,.75) 100%)}
.g-md-stage.g-md-bust .g-md-bd-dark{opacity:.55}
.g-md-stage.g-md-out .g-md-bd-dark{opacity:.6;transition:opacity 1.6s ease}
.g-md-stage.g-md-out .g-md-bd-felt::before{animation:none;opacity:.7;transition:opacity 1.6s ease}
.g-md-stage.g-md-royal{--md-felt-lit:color-mix(in srgb, var(--gold), var(--md-felt) 55%)}

/* ---- HUD: four chips above the felt --------------------------------------- */
.g-md-hud{position:relative;z-index:2;display:flex;flex-wrap:wrap;align-items:center;
  justify-content:center;gap:8px 12px;font-family:var(--mono);font-size:11px;
  letter-spacing:.14em;text-transform:uppercase;color:var(--ink-faint)}
.g-md-chip{display:inline-flex;align-items:center;gap:6px;padding:4px 12px;border-radius:999px;
  border:1px solid color-mix(in srgb, var(--lav), var(--line) 55%);
  background:color-mix(in srgb, var(--panel), transparent 40%);
  color:color-mix(in srgb, var(--ink-dim), var(--lav) 30%);
  font-variant-numeric:tabular-nums;transition:color .3s ease,border-color .3s ease,box-shadow .3s ease}
.g-md-chip.g-md-pot{border-color:color-mix(in srgb, var(--gold), var(--line) 40%);color:var(--gold);
  box-shadow:0 0 12px color-mix(in srgb, var(--gold), transparent 72%)}
.g-md-chip.g-md-streak{border-color:var(--pink);color:var(--pink);
  box-shadow:0 0 12px color-mix(in srgb, var(--pink), transparent 62%)}
.g-md-chip.g-md-clock{color:var(--ink)}
.g-md-stage[data-phase="pick"] .g-md-clock{border-color:color-mix(in srgb, var(--ink), var(--line) 40%)}
.g-md-chip.g-md-bell,.g-md-stage.g-md-bell .g-md-clock{border-color:var(--gold);color:var(--gold);
  animation:g-md-bellchip 1s ease-in-out infinite alternate}
@keyframes g-md-bellchip{from{box-shadow:0 0 6px rgba(240,194,75,.3)}to{box-shadow:0 0 16px rgba(240,194,75,.7)}}

/* ---- the table: a velvet slab under the lamp -------------------------------- */
.g-md-table > .arc-stamp{position:absolute;left:0;right:0;top:40%;margin:0 auto;width:max-content;z-index:6;
  font-size:clamp(14px,2.2vmin,20px);padding:7px 16px}
.g-md-table{position:relative;z-index:1;flex:1;min-height:0;width:var(--md-table);
  max-height:calc(var(--md-table) * .52);margin:auto 0;
  --md-rail:var(--md-metal);
  border-radius:46% 46% 18px 18px / 70% 70% 18px 18px;
  background:
    radial-gradient(70% 60% at 50% 30%, color-mix(in srgb, var(--md-felt-lit), transparent 20%), transparent 70%),
    linear-gradient(180deg, var(--md-felt) 0%, var(--md-felt-deep) 100%);
  box-shadow:inset 0 0 0 3px color-mix(in srgb, var(--md-rail), black 30%), inset 0 0 0 6px color-mix(in srgb, var(--md-rail), transparent 45%),
    inset 0 10px 40px rgba(0,0,0,.5), 0 26px 60px rgba(0,0,0,.6), 0 0 40px color-mix(in srgb, var(--md-glow), transparent 78%)}
.g-md-table::before{content:"";position:absolute;inset:8px;border-radius:inherit;pointer-events:none;
  background:radial-gradient(60% 40% at 50% 0%, rgba(255,255,255,.08), transparent 70%)}
.g-md-stage.g-md-shuffling .g-md-table{box-shadow:inset 0 0 0 3px color-mix(in srgb, var(--md-rail), black 30%), inset 0 0 0 6px color-mix(in srgb, var(--md-rail), transparent 45%),
    inset 0 10px 40px rgba(0,0,0,.5), 0 26px 60px rgba(0,0,0,.6), 0 0 54px color-mix(in srgb, var(--md-glow), transparent 62%)}

/* ---- the arc: shells positioned from --x (percent) -------------------------- */
.g-md-arc{position:absolute;left:9%;right:9%;top:22%;bottom:10%;z-index:2;
  transition:transform .6s ease}
.g-md-stage[data-occluding="1"] .g-md-arc{filter:brightness(.5) saturate(.7);transition:filter .1s ease}
.g-md-stage[data-occluding="1"] .g-md-table{filter:brightness(.7)}
.g-md-shell{position:absolute;left:calc(var(--x, .5) * 100%);top:50%;
  width:var(--md-shell);height:calc(var(--md-shell) * 1.08);margin:0;padding:0;border:0;background:none;
  appearance:none;-webkit-appearance:none;cursor:pointer;color:inherit;font:inherit;
  /* POSITIONAL ONLY: the centre, the gentle arc (outer shells sit lower, nearer
     the player) and the slide. Nothing decorative lives here. */
  transform:translate(-50%, calc(-50% + (var(--x, .5) - .5) * (var(--x, .5) - .5) * 110px));
  transition:left .28s cubic-bezier(.3,.7,.25,1), transform .28s cubic-bezier(.3,.7,.25,1);
  -webkit-tap-highlight-color:transparent;touch-action:manipulation;outline:none}
.g-md-shell:focus-visible .g-md-lid{box-shadow:0 0 0 3px color-mix(in srgb, var(--gold), transparent 20%), 0 14px 26px rgba(0,0,0,.55)}
.g-md-shell.is-swapping{transition:left .28s cubic-bezier(.5,.1,.3,1), transform .28s cubic-bezier(.5,.1,.3,1)}
/* the face: what is under the cup - a disc of the player's own media */
.g-md-face{position:absolute;left:50%;bottom:12%;width:62%;aspect-ratio:1;transform:translateX(-50%) scale(.92);
  border-radius:50%;overflow:hidden;z-index:1;opacity:0;
  background:radial-gradient(circle at 40% 35%, color-mix(in srgb, var(--md-glow), white 20%), color-mix(in srgb, var(--md-glow), black 40%));
  box-shadow:0 0 16px color-mix(in srgb, var(--md-glow), transparent 50%), inset 0 0 0 2px rgba(255,255,255,.25);
  transition:opacity .22s ease, transform .3s cubic-bezier(.2,1.4,.4,1)}
.g-md-media{display:block;width:100%;height:100%;object-fit:cover;pointer-events:none}
.g-md-media:not([src]),.g-md-media[src=""]{visibility:hidden}
.g-md-shell.is-lifted .g-md-face{opacity:1;transform:translateX(-50%) scale(1)}
.g-md-shell.is-picked.is-true .g-md-face{transform:translateX(-50%) scale(1.28);
  box-shadow:0 0 28px color-mix(in srgb, var(--gold), transparent 20%), inset 0 0 0 2px rgba(255,255,255,.5)}
.g-md-shell.is-picked:not(.is-true) .g-md-face{opacity:.35;background:rgba(10,6,20,.6);box-shadow:none}
.g-md-face[data-dressed="0"]{background:radial-gradient(circle at 40% 35%, rgba(255,255,255,.12), rgba(10,6,20,.6));box-shadow:inset 0 0 0 2px rgba(255,255,255,.12)}
.g-md-shell.is-picked:not(.is-true) .g-md-media{opacity:0}
/* the lid: the cup body that lifts. Everything decorative composes here:
   the lift, the spring, the lean into travel, the trickster's feint + sag. */
.g-md-lid{position:absolute;left:50%;top:0;width:82%;height:80%;z-index:2;
  transform:translateX(calc(-50% + var(--md-feint,0) * 1px)) translateY(0) rotate(calc(var(--md-feint,0) * -.6deg));
  transform-origin:50% 100%;
  transition:transform .34s cubic-bezier(.2,1.5,.4,1), box-shadow .3s ease, filter .3s ease;
  animation:g-md-cupbreath calc(var(--md-breath) * .8) ease-in-out infinite alternate;
  animation-delay:calc(var(--slot,0) * -1.3s)}
@keyframes g-md-cupbreath{from{translate:0 0}to{translate:0 -2px}}
.g-md-shell.is-lifted .g-md-lid{transform:translateX(calc(-50% + var(--md-feint,0) * 1px)) translateY(-72%) rotate(-7deg)}
.g-md-shell.is-decoy.is-lifted .g-md-lid{transform:translateX(-50%) translateY(-40%) rotate(-4deg);transition:transform .16s ease-out}
.g-md-shell.is-swapping .g-md-lid{transform:translateX(-50%) translateY(-3%) scaleX(.96)}
.g-md-shell.g-md-tk-feint .g-md-lid{transition:transform .13s cubic-bezier(.3,.7,.3,1)}
.g-md-shell.g-md-tk-melt .g-md-lid{transform:translateX(-50%) translateY(6%) scaleY(.88) skewX(4deg);filter:saturate(.8) brightness(.9);transition:transform .42s ease-in}
.g-md-shell.g-md-tk-melt .g-md-lid::after{opacity:1}
/* the shadow on the felt under the cup (lifts with it) */
.g-md-shell::before{content:"";position:absolute;left:50%;bottom:4%;width:78%;height:16%;transform:translateX(-50%);
  border-radius:50%;background:radial-gradient(50% 50% at 50% 50%, rgba(0,0,0,.55), transparent 70%);
  transition:opacity .3s ease, transform .3s ease;z-index:0}
.g-md-shell.is-lifted::before{opacity:.35;transform:translateX(-50%) scale(1.15)}
/* the tag: a hanging number on a thread - the ONLY node the trickster may lie on */
.g-md-tag{position:absolute;left:50%;bottom:-6%;transform:translateX(-50%);z-index:3;min-width:1.7em;padding:2px 6px;
  font:700 clamp(10px,1.5vmin,13px)/1.2 var(--mono);letter-spacing:.08em;text-align:center;border-radius:4px;
  color:color-mix(in srgb, var(--md-metal), white 30%);background:color-mix(in srgb, var(--md-metal-dk), black 40%);
  border:1px solid color-mix(in srgb, var(--md-metal), transparent 35%);box-shadow:0 2px 6px rgba(0,0,0,.5);
  transition:color .2s ease, background .2s ease}
.g-md-tag:empty{opacity:0}
.g-md-shell.g-md-tk-melt .g-md-tag{transform:translateX(-50%) translateY(3px) skewX(-6deg)}

/* ---- SKIN: themed (velvet cups, metal rim) ---------------------------------- */
.g-md-stage[data-skin="themed"] .g-md-lid{border-radius:48% 48% 14% 14% / 70% 70% 10% 10%;
  background:
    radial-gradient(70% 40% at 35% 18%, rgba(255,255,255,.22), transparent 60%),
    linear-gradient(90deg, color-mix(in srgb, var(--md-felt-lit), black 10%) 0%, color-mix(in srgb, var(--md-felt-lit), white 12%) 45%, color-mix(in srgb, var(--md-felt), black 25%) 100%);
  box-shadow:inset 0 -8px 0 color-mix(in srgb, var(--md-metal-dk), black 10%), inset 0 -11px 0 var(--md-metal),
    inset 0 0 0 1px rgba(255,255,255,.08), 0 14px 26px rgba(0,0,0,.55),
    0 0 calc(var(--md-n-ride,0) * 6px) color-mix(in srgb, var(--md-glow), transparent calc(85% - var(--md-n-ride,0) * 9%))}
.g-md-stage[data-skin="themed"] .g-md-lid::before{content:"";position:absolute;left:50%;top:-6%;width:26%;height:18%;transform:translateX(-50%);
  border-radius:50%;background:radial-gradient(circle at 40% 35%, color-mix(in srgb, var(--md-metal), white 40%), var(--md-metal) 50%, var(--md-metal-dk));
  box-shadow:0 2px 4px rgba(0,0,0,.5)}
.g-md-stage[data-skin="themed"] .g-md-lid::after{content:"";position:absolute;left:30%;bottom:-10%;width:10%;height:18%;opacity:0;
  border-radius:0 0 50% 50%;background:color-mix(in srgb, var(--md-felt-lit), white 10%);transition:opacity .3s ease}
.g-md-stage[data-skin="themed"] .g-md-shell.is-picked .g-md-lid{filter:brightness(1.18)}
.g-md-stage[data-skin="themed"] .g-md-shell.is-picked.is-true .g-md-lid{box-shadow:inset 0 -8px 0 color-mix(in srgb, var(--md-metal-dk), black 10%), inset 0 -11px 0 var(--gold),
    0 14px 26px rgba(0,0,0,.55), 0 0 30px color-mix(in srgb, var(--gold), transparent 30%)}
.g-md-stage[data-skin="themed"] .g-md-shell.is-true:not(.is-picked) .g-md-lid{box-shadow:inset 0 -8px 0 color-mix(in srgb, var(--md-metal-dk), black 10%), inset 0 -11px 0 var(--md-metal),
    0 14px 26px rgba(0,0,0,.55), 0 10px 28px color-mix(in srgb, var(--md-glow), transparent 25%)}
.g-md-stage[data-skin="themed"] .g-md-shell.is-tell .g-md-lid{box-shadow:inset 0 -8px 0 color-mix(in srgb, var(--md-metal-dk), black 10%), inset 0 -11px 0 var(--md-metal),
    0 0 0 3px color-mix(in srgb, var(--md-glow), transparent 25%), 0 14px 26px rgba(0,0,0,.55);transition:box-shadow .08s ease}
.g-md-stage[data-skin="themed"] .g-md-shell.is-picked:not(.is-true) .g-md-lid{filter:saturate(.6) brightness(.75)}

/* ---- SKIN: minimal (flat outlines, one ink) ---------------------------------- */
.g-md-stage[data-skin="minimal"] .g-md-lid{border-radius:14px 14px 6px 6px;background:color-mix(in srgb, var(--ground), black 20%);
  border:2px solid color-mix(in srgb, var(--ink), transparent 30%);box-shadow:0 10px 20px rgba(0,0,0,.45);animation:none}
.g-md-stage[data-skin="minimal"] .g-md-tag{background:transparent;border-color:transparent;color:var(--ink);box-shadow:none}
.g-md-stage[data-skin="minimal"] .g-md-shell.is-picked .g-md-lid{border-color:var(--ink)}
.g-md-stage[data-skin="minimal"] .g-md-shell.is-picked.is-true .g-md-lid{border-color:var(--gold)}
.g-md-stage[data-skin="minimal"] .g-md-shell.is-true:not(.is-picked) .g-md-lid{border-color:var(--md-glow)}
.g-md-stage[data-skin="minimal"] .g-md-shell.is-tell .g-md-lid{border-color:var(--md-glow);box-shadow:0 0 0 2px color-mix(in srgb, var(--md-glow), transparent 40%)}
.g-md-stage[data-skin="minimal"] .g-md-shell.is-picked:not(.is-true) .g-md-lid{opacity:.55}
.g-md-stage[data-skin="minimal"] .g-md-face{background:color-mix(in srgb, var(--ink), transparent 80%);box-shadow:inset 0 0 0 2px var(--ink)}

/* ---- SKIN: contrast (black body, thick white rims, big tags) ----------------- */
.g-md-stage[data-skin="contrast"] .g-md-lid{border-radius:46% 46% 10% 10% / 66% 66% 8% 8%;background:#05030a;
  border:4px solid #fff;box-shadow:0 0 0 2px #000, 0 12px 24px rgba(0,0,0,.7)}
.g-md-stage[data-skin="contrast"] .g-md-tag{font-size:clamp(12px,2vmin,16px);color:#000;background:#fff;border-color:#000;min-width:2em}
.g-md-stage[data-skin="contrast"] .g-md-shell.is-picked .g-md-lid{border-color:#ffe08a}
.g-md-stage[data-skin="contrast"] .g-md-shell.is-picked.is-true .g-md-lid{border-color:var(--gold);box-shadow:0 0 0 2px #000, 0 0 30px rgba(240,194,75,.7)}
.g-md-stage[data-skin="contrast"] .g-md-shell.is-true:not(.is-picked) .g-md-lid{border-color:#ff69b4;box-shadow:0 0 0 2px #000, 0 0 24px rgba(255,105,180,.8)}
.g-md-stage[data-skin="contrast"] .g-md-shell.is-tell .g-md-lid{border-color:#ff69b4}
.g-md-stage[data-skin="contrast"] .g-md-shell.is-picked:not(.is-true) .g-md-lid{border-color:#777}
.g-md-stage[data-skin="contrast"][data-occluding="1"] .g-md-arc{filter:brightness(.75)}
.g-md-stage[data-skin="contrast"] .g-md-face{box-shadow:inset 0 0 0 3px #fff, 0 0 16px rgba(255,255,255,.5)}

/* ---- the stake: two real buttons, honest, never moved ------------------------- */
/* index.js toggles the node with the hidden attribute, so NO display here:
   block by default, the buttons inline, centred by text-align. */
.g-md-stake{position:relative;z-index:4;text-align:center;min-height:44px;
  font-family:var(--mono);font-size:11px;letter-spacing:.16em;text-transform:uppercase;color:var(--ink-dim)}
.g-md-stake .g-md-stake-line{display:inline-block;margin:0 8px 0 0;color:var(--ink-faint)}
.g-md-stake button,.g-md-btn{display:inline-block;margin:0 7px;appearance:none;-webkit-appearance:none;font:inherit;letter-spacing:inherit;text-transform:inherit;
  cursor:pointer;padding:9px 18px;border-radius:999px;border:1px solid var(--line);color:var(--ink);
  background:color-mix(in srgb, var(--panel), transparent 30%);transition:background .2s ease, border-color .2s ease, box-shadow .2s ease}
.g-md-stake button:focus-visible,.g-md-btn:focus-visible{outline:2px solid var(--gold);outline-offset:3px}
.g-md-stake button:active{transform:translateY(1px)}
.g-md-btn.g-md-bank{border-color:var(--lav);color:var(--lav)}
.g-md-btn.g-md-bank:hover{background:color-mix(in srgb, var(--panel), var(--lav) 18%);color:var(--ink)}
.g-md-btn.g-md-ride{border-color:var(--pink);color:var(--pink);
  box-shadow:0 0 12px color-mix(in srgb, var(--pink), transparent 70%);animation:g-md-ridepulse 1.6s ease-in-out infinite alternate}
.g-md-btn.g-md-ride:hover{background:color-mix(in srgb, var(--panel), var(--pink) 20%);color:var(--ink)}
@keyframes g-md-ridepulse{from{box-shadow:0 0 8px color-mix(in srgb, var(--pink), transparent 80%)}to{box-shadow:0 0 20px color-mix(in srgb, var(--pink), transparent 50%)}}

/* ---- the flash well: engine one-shots over the table, never a pointer --------- */
.g-md-flashwell{position:absolute;inset:0;overflow:hidden;pointer-events:none;z-index:6}
.g-md-flashwell *{pointer-events:none}

/* ---- proctor line + end card ----------------------------------------------------- */
.g-md-msg{position:relative;z-index:2;margin:0;min-height:1.4em;text-align:center;
  font-family:var(--mono);font-size:11px;letter-spacing:.16em;text-transform:uppercase;
  color:var(--ink-dim);transition:opacity .3s ease}
.g-md-msg:empty{opacity:0}
/* index.js toggles the node with the hidden attribute: NO display here. */
.g-md-end{position:absolute;left:50%;top:50%;transform:translate(-50%,-50%);z-index:8;
  width:min(460px, 92%);padding:18px 20px 16px;
  border-radius:16px;background:color-mix(in srgb, var(--navy), transparent 6%);
  border:1px solid color-mix(in srgb, var(--md-metal), var(--line) 50%);
  box-shadow:0 18px 50px rgba(0,0,0,.55), 0 0 34px color-mix(in srgb, var(--md-glow), transparent 72%);
  animation:g-md-endin .5s ease-out 1}
@keyframes g-md-endin{from{opacity:0;transform:translate(-50%,-46%)}to{opacity:1;transform:translate(-50%,-50%)}}
.g-md-end-title{font-family:var(--disp);font-size:16px;letter-spacing:.14em;text-transform:uppercase;
  margin:0 0 8px;color:var(--ink);text-align:center;text-shadow:0 0 18px color-mix(in srgb, var(--md-glow), transparent 60%)}
.g-md-end-row{display:flex;justify-content:space-between;align-items:baseline;gap:12px;
  font-family:var(--mono);font-size:12px;letter-spacing:.1em;text-transform:uppercase;color:var(--ink-dim);
  padding:5px 0;border-bottom:1px dashed color-mix(in srgb, var(--line), transparent 20%)}
.g-md-end-k{color:var(--ink-faint)}
.g-md-end-v{color:var(--ink);font-variant-numeric:tabular-nums}
.g-md-end-bank .g-md-end-v{color:var(--gold);font-size:14px;text-shadow:0 0 12px color-mix(in srgb, var(--gold), transparent 55%)}
.g-md-end-line,.g-md-end-note{margin:6px 0 0;font-size:12px;color:var(--ink-dim);text-align:center}
/* THE GRADE AS AN OBJECT: a wax seal pressed on the report - index.js may
   append .g-md-end-seal[data-grade=S|A|B|C] with the letter as text */
.g-md-end-seal{position:absolute;right:-14px;top:-18px;width:64px;height:64px;border-radius:50%;display:flex;align-items:center;justify-content:center;
  font:800 26px/1 var(--disp);color:rgba(20,8,14,.85);transform:rotate(-12deg);
  --md-seal:color-mix(in srgb, var(--md-glow), #7a1040 40%);
  background:radial-gradient(circle at 40% 35%, color-mix(in srgb, var(--md-seal), white 22%), var(--md-seal) 55%, color-mix(in srgb, var(--md-seal), black 35%));
  box-shadow:inset 0 0 0 3px rgba(0,0,0,.18), inset 0 0 0 7px rgba(255,255,255,.08), 0 8px 18px rgba(0,0,0,.55);
  animation:g-md-seal .55s cubic-bezier(.2,1.6,.4,1) .25s both}
.g-md-end-seal::before{content:"";position:absolute;inset:-6px;border-radius:50%;border:1px dashed color-mix(in srgb, var(--md-seal), transparent 30%)}
.g-md-end-seal[data-grade="S"]{--md-seal:var(--gold);color:rgba(40,24,4,.9)}
.g-md-end-seal[data-grade="A"]{--md-seal:var(--pink)}
.g-md-end-seal[data-grade="B"]{--md-seal:var(--lav);color:rgba(14,10,30,.85)}
.g-md-end-seal[data-grade="C"]{--md-seal:var(--slate);color:rgba(14,10,30,.85)}
@keyframes g-md-seal{from{opacity:0;transform:rotate(-12deg) scale(1.6)}to{opacity:1;transform:rotate(-12deg) scale(1)}}
.g-md-end .btn,.g-md-end button{margin:10px 4px 0}

/* ---- THE CLASS RULES SHEET (Deck VI, Law IV: drawn, not told) ------------------- */
.g-md-howto{position:absolute;left:50%;top:50%;transform:translate(-50%,-50%);z-index:9;
  width:min(540px,92vw);max-height:86vh;overflow:auto;pointer-events:none;
  display:flex;flex-direction:column;gap:2px;padding:22px 24px 20px;border-radius:16px;
  background:linear-gradient(180deg, color-mix(in srgb, var(--navy), transparent 6%), color-mix(in srgb, var(--ground), black 30%));
  border:1px solid color-mix(in srgb, var(--md-metal), var(--line) 50%);
  box-shadow:0 30px 80px rgba(0,0,0,.55), 0 0 44px color-mix(in srgb, var(--md-glow), transparent 86%)}
.g-md-hw-title{margin:0 0 8px;text-align:center;font:700 clamp(16px,2.4vmin,20px)/1.2 var(--disp);
  letter-spacing:.2em;text-transform:uppercase;color:var(--md-glow);text-shadow:0 0 22px color-mix(in srgb, var(--md-glow), transparent 55%)}
.g-md-hw-row{display:flex;align-items:center;gap:16px;padding:11px 2px}
.g-md-hw-row + .g-md-hw-row{border-top:1px dashed color-mix(in srgb, var(--line), transparent 40%)}
.g-md-hw-cap{margin:0;flex:1 1 auto;font-size:12.5px;line-height:1.5;color:var(--ink-dim)}
.g-md-hw-keys{margin:2px 0 0;font-family:var(--mono);font-size:10.5px;letter-spacing:.1em;color:var(--ink-faint)}
.g-md-hw-fig{flex:0 0 auto;display:flex;align-items:center;gap:9px;pointer-events:none}
.g-md-hw-scene{position:relative;display:block;width:96px;height:58px;flex:0 0 auto;
  border-radius:40% 40% 8px 8px / 60% 60% 8px 8px;background:linear-gradient(180deg, var(--md-felt-lit), var(--md-felt-deep));
  box-shadow:inset 0 0 0 2px color-mix(in srgb, var(--md-metal), transparent 50%)}
.g-md-hw-cup{position:absolute;bottom:10px;width:22px;height:20px;border-radius:48% 48% 14% 14% / 70% 70% 10% 10%;
  background:linear-gradient(90deg, color-mix(in srgb, var(--md-felt-lit), black 10%), color-mix(in srgb, var(--md-felt-lit), white 14%) 45%, color-mix(in srgb, var(--md-felt), black 25%));
  box-shadow:inset 0 -3px 0 var(--md-metal), 0 4px 8px rgba(0,0,0,.5);transform-origin:50% 100%}
.g-md-hw-cup.c1{left:14px}.g-md-hw-cup.c2{left:37px}.g-md-hw-cup.c3{left:60px}
.g-md-hw-gem{position:absolute;bottom:12px;left:41px;width:14px;height:14px;border-radius:50%;
  background:radial-gradient(circle at 40% 35%, #fff, var(--md-glow) 45%, color-mix(in srgb, var(--md-glow), black 40%));
  box-shadow:0 0 10px color-mix(in srgb, var(--md-glow), transparent 30%)}
/* 1 - the reveal: the middle cup lifts, the gem glows, it drops back */
.g-md-hw-watch .c2{animation:g-md-hw-lift 2.8s ease-in-out infinite}
@keyframes g-md-hw-lift{0%,10%{transform:translateY(0) rotate(0)}25%,60%{transform:translateY(-16px) rotate(-8deg)}78%,100%{transform:translateY(0) rotate(0)}}
/* 2 - the shuffle: the outer cups trade places, the middle leans */
.g-md-hw-shuffle .c1{animation:g-md-hw-swapr 2.4s ease-in-out infinite}
.g-md-hw-shuffle .c3{animation:g-md-hw-swapl 2.4s ease-in-out infinite}
.g-md-hw-shuffle .c2{animation:g-md-hw-lean 2.4s ease-in-out infinite}
.g-md-hw-shuffle .g-md-hw-gem{opacity:0}
@keyframes g-md-hw-swapr{0%,15%{transform:translateX(0)}50%,65%{transform:translateX(46px)}100%{transform:translateX(0)}}
@keyframes g-md-hw-swapl{0%,15%{transform:translateX(0)}50%,65%{transform:translateX(-46px)}100%{transform:translateX(0)}}
@keyframes g-md-hw-lean{0%,15%{transform:rotate(0)}35%{transform:rotate(6deg)}55%{transform:rotate(-6deg)}80%,100%{transform:rotate(0)}}
.g-md-hw-blind{position:absolute;inset:0;border-radius:inherit;background:rgba(4,2,10,.75);opacity:0;animation:g-md-hw-blind 2.4s ease-in-out infinite}
@keyframes g-md-hw-blind{0%,28%{opacity:0}34%,40%{opacity:1}46%,100%{opacity:0}}
/* 3 - the pick: a keycap or a finger presses on the third cup, the ring drains */
.g-md-hw-tap{flex:0 0 auto;display:block}
.g-md-hw-tap.cap{min-width:40px;text-align:center;padding:6px 9px;border-radius:7px;font:700 11px/1 var(--mono);letter-spacing:.06em;color:var(--ink);
  background:linear-gradient(180deg, var(--panel), color-mix(in srgb, var(--panel), black 35%));
  border:1px solid var(--line);border-bottom-width:3px;box-shadow:0 2px 0 rgba(0,0,0,.45);animation:g-md-hw-press 2.6s ease-in-out infinite}
@keyframes g-md-hw-press{0%,72%{transform:translateY(0)}80%{transform:translateY(2px)}92%,100%{transform:translateY(0)}}
.g-md-hw-tap.finger{position:relative;width:22px;height:32px;animation:g-md-hw-press 2.6s ease-in-out infinite}
.g-md-hw-tap.finger::before{content:"";position:absolute;left:50%;bottom:0;width:14px;height:20px;transform:translateX(-50%);
  border-radius:8px 8px 5px 5px;background:linear-gradient(180deg, #f1d3c0, #d9a88f);box-shadow:0 2px 6px rgba(0,0,0,.5)}
.g-md-hw-ring{position:absolute;right:6px;top:6px;width:16px;height:16px;border-radius:50%;
  background:conic-gradient(from -90deg, var(--md-glow) 0 var(--md-hw-k,270deg), rgba(255,255,255,.12) var(--md-hw-k,270deg));
  -webkit-mask:radial-gradient(circle, transparent 55%, #000 58%);mask:radial-gradient(circle, transparent 55%, #000 58%);
  animation:g-md-hw-drain 2.6s linear infinite}
@keyframes g-md-hw-drain{from{--md-hw-k:360deg}to{--md-hw-k:0deg}}
.g-md-hw-pick .c3{animation:g-md-hw-lift 2.6s ease-in-out infinite .8s}
.g-md-hw-pick .g-md-hw-gem{left:64px;opacity:0;animation:g-md-hw-gem 2.6s ease-in-out infinite .8s}
@keyframes g-md-hw-gem{0%,20%{opacity:0}30%,60%{opacity:1}78%,100%{opacity:0}}
/* 4 - the stake: a coin stack splits - one half banks left (safe), one half doubles right */
.g-md-hw-coin{position:absolute;bottom:10px;width:16px;height:5px;border-radius:50%;
  background:radial-gradient(circle at 35% 30%, #fff3c4, #f0c24b 45%, #a8771a 100%);box-shadow:0 1px 2px rgba(0,0,0,.6)}
.g-md-hw-stake .g-md-hw-coin.k1{left:40px}.g-md-hw-stake .g-md-hw-coin.k2{left:40px;bottom:15px}
.g-md-hw-stake .g-md-hw-coin.k3{left:40px;bottom:20px;animation:g-md-hw-bank 3s ease-in-out infinite}
.g-md-hw-stake .g-md-hw-coin.k4{left:40px;bottom:25px;animation:g-md-hw-ride 3s ease-in-out infinite}
.g-md-hw-stake .g-md-hw-coin.k5{left:40px;bottom:25px;opacity:0;animation:g-md-hw-ride2 3s ease-in-out infinite}
@keyframes g-md-hw-bank{0%,20%{transform:translate(0,0)}45%,100%{transform:translate(-26px,14px)}}
@keyframes g-md-hw-ride{0%,20%{transform:translate(0,0)}45%,100%{transform:translate(26px,14px)}}
@keyframes g-md-hw-ride2{0%,40%{opacity:0;transform:translate(26px,14px)}60%,100%{opacity:1;transform:translate(26px,8px)}}
.g-md-hw-safe{position:absolute;left:8px;bottom:8px;width:22px;height:22px;border-radius:4px;border:2px solid var(--lav);opacity:.8}
.g-md-hw-x2{position:absolute;right:8px;bottom:8px;font:800 12px/1 var(--mono);color:var(--pink);text-shadow:0 0 8px rgba(255,105,180,.8)}
.g-md-hw-go{pointer-events:auto;align-self:center;margin-top:14px;appearance:none;-webkit-appearance:none;cursor:pointer;
  font:700 12px/1 var(--mono);letter-spacing:.18em;text-transform:uppercase;color:var(--ink);
  padding:11px 26px;border-radius:999px;border:1px solid var(--pink);background:color-mix(in srgb, var(--panel), var(--pink) 16%);
  box-shadow:0 0 16px color-mix(in srgb, var(--pink), transparent 60%);animation:g-md-ridepulse 1.6s ease-in-out infinite alternate}
.g-md-hw-go:hover{background:color-mix(in srgb, var(--panel), var(--pink) 28%)}
.g-md-hw-go:focus-visible{outline:2px solid var(--gold);outline-offset:3px}

/* ---- phases ----------------------------------------------------------------------- */
.g-md-stage[data-phase="briefing"] .g-md-table{filter:brightness(.85) saturate(.9)}
.g-md-stage[data-phase="ended"] .g-md-table{filter:saturate(.6) brightness(.7);transition:filter 1.2s ease}
.g-md-stage[data-phase="ended"] .g-md-lid{animation:none}

/* ---- reduced motion (both gates): the slide and the lift survive, nothing loops --- */
html.arc-reduced .g-md-bd::before,html.arc-reduced .g-md-lid,html.arc-reduced .g-md-btn,html.arc-reduced .g-md-hw-go,
html.arc-reduced .g-md-hw-cup,html.arc-reduced .g-md-hw-gem,html.arc-reduced .g-md-hw-coin,html.arc-reduced .g-md-hw-tap,
html.arc-reduced .g-md-hw-ring,html.arc-reduced .g-md-hw-blind,html.arc-reduced .g-md-clock,html.arc-reduced .g-md-end-seal{animation:none !important}
html.arc-reduced .g-md-stage{transition:none}
@media (prefers-reduced-motion: reduce){
  .g-md-bd::before,.g-md-lid,.g-md-btn,.g-md-hw-go,.g-md-hw-cup,.g-md-hw-gem,.g-md-hw-coin,.g-md-hw-tap,.g-md-hw-ring,.g-md-hw-blind,.g-md-clock,.g-md-end-seal{animation:none !important}
  .g-md-stage{transition:none}
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
.g-md-howto{pointer-events:auto}
.g-md-hw-go{position:sticky;bottom:0;z-index:3;align-self:stretch;margin-top:14px}

`;

/** Inject once per document. No-op headless (the DOM double has no head). */
export function injectMisdirectionStyle() {
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

/**
 * THE DRAWN CLASS RULES SHEET (Deck VI, Law IV) - an optional helper for
 * index.js: builds the four vignettes + the GO button into `host` and returns
 * the sheet node (or null). No raster art; every figure is pointer-events:none;
 * GO is the only live thing and the ONLY dismissal. index.js owns the POLICY
 * (when to show, hideTutorial, howtoTiers) and may build its own sheet instead;
 * the CSS above styles either, as long as the class names match.
 * @param {Object} o  { host, t, onGo, coarse, keyLabel (e.g. '3'), keys (e.g. '1 2 3 4 5') }
 */
export function buildMdHowto(o) {
  const opts = o || {};
  const t = typeof opts.t === 'function' ? opts.t : ((k, f) => f);
  const host = opts.host;
  if (!host || typeof document === 'undefined') return null;
  const el = (tag, cls, parent) => {
    try {
      const n = document.createElement(tag);
      if (cls) n.className = cls;
      if (parent && parent.appendChild) parent.appendChild(n);
      return n;
    } catch (e) { return null; }
  };
  const sheet = el('div', 'g-md-howto', host);
  if (!sheet) return null;
  const h = el('h2', 'g-md-hw-title', sheet);
  if (h) h.textContent = t('md_howto_title', 'Class rules');
  const row = (kind, build, caption, extra) => {
    const r = el('div', 'g-md-hw-row', sheet);
    if (!r) return null;
    const fig = el('span', 'g-md-hw-fig', r);
    if (fig) { try { build(fig, kind); } catch (e) { /* a caption alone still teaches */ } }
    const wrap = el('span', null, r);
    if (wrap) { try { wrap.style.flex = '1 1 auto'; } catch (e) { /* noop */ } }
    const p = el('p', 'g-md-hw-cap', wrap || r);
    if (p) p.textContent = caption;
    if (extra) { const k = el('p', 'g-md-hw-keys', wrap || r); if (k) k.textContent = extra; }
    return r;
  };
  const scene = (fig, kind) => {
    const s = el('span', 'g-md-hw-scene g-md-hw-' + kind, fig);
    if (!s) return null;
    el('span', 'g-md-hw-cup c1', s); el('span', 'g-md-hw-cup c2', s); el('span', 'g-md-hw-cup c3', s);
    return s;
  };
  /* 1 - WATCH: the middle cup lifts, the gem under it glows */
  row('watch', (fig, kind) => { const s = scene(fig, kind); if (s) el('span', 'g-md-hw-gem', s); },
    t('md_howto_watch', 'One shell lifts. What is under it is the only thing you are tracking.'));
  /* 2 - SHUFFLE: the outer cups trade, the hand comes over the table */
  row('shuffle', (fig, kind) => { const s = scene(fig, kind); if (s) { el('span', 'g-md-hw-gem', s); el('span', 'g-md-hw-blind', s); } },
    t('md_howto_shuffle', 'They slide and trade places. The room will do its best to blind you.'));
  /* 3 - PICK: a keycap (or a finger) presses the third cup, the ring drains */
  row('pick', (fig, kind) => {
    const s = scene(fig, kind);
    if (s) { el('span', 'g-md-hw-gem', s); el('span', 'g-md-hw-ring', s); }
    const tap = el('span', 'g-md-hw-tap' + (opts.coarse ? ' finger' : ' cap'), fig);
    if (tap && !opts.coarse) tap.textContent = String(opts.keyLabel || '3');
  }, t('md_howto_pick', 'Point at the shell you followed. Four seconds, every round.'),
  opts.keys ? t('md_howto_keys', 'Keys {keys} pick a shell.').replace('{keys}', String(opts.keys)) : '');
  /* 4 - STAKE: the coin stack splits - safe left, double right */
  row('stake', (fig) => {
    const s = el('span', 'g-md-hw-scene g-md-hw-stake', fig);
    if (!s) return;
    el('span', 'g-md-hw-safe', s);
    for (const k of ['k1', 'k2', 'k3', 'k4', 'k5']) el('span', 'g-md-hw-coin ' + k, s);
    const x2 = el('span', 'g-md-hw-x2', s);
    if (x2) x2.textContent = 'x2';
  }, t('md_howto_stake', 'Right? Bank the pot, or ride it double into a dirtier shuffle.'));
  const go = el('button', 'g-md-hw-go', sheet);
  if (go) {
    go.type = 'button';
    go.textContent = t('md_howto_go', 'Open the table');
    go.setAttribute('autofocus', '');
    go.addEventListener('click', () => { try { if (typeof opts.onGo === 'function') opts.onGo(); } catch (e) { /* noop */ } });
    try { if (typeof go.focus === 'function') go.focus(); } catch (e) { /* noop */ }
  }
  return sheet;
}

/* index.js's seam (it picks showHowto / hideHowto off this module): the sheet
   is appended to o.stage (or o.host), one at a time; hideHowto removes it. */
let liveSheet = null;
export function showHowto(o) {
  const opts = o || {};
  hideHowto();
  const host = opts.host || opts.stage || opts.table;
  const keys = Array.isArray(opts.keys) ? opts.keys : null;
  liveSheet = buildMdHowto({
    host, t: opts.t, onGo: opts.onGo, coarse: !!opts.coarse,
    keyLabel: keys && keys.length >= 3 ? keys[2] : (keys && keys[0]) || '3',
    keys: keys ? keys.join(' ') : '',
  });
  return liveSheet;
}
export function hideHowto() {
  if (liveSheet) { try { liveSheet.remove(); } catch (e) { /* noop */ } }
  liveSheet = null;
}

export default injectMisdirectionStyle;
