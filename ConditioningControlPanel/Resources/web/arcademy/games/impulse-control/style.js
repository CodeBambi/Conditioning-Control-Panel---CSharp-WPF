/* ============================================================================
 * games/impulse-control/style.js - the class injects its OWN stylesheet.
 *
 * styles.css is shell chrome ONLY; everything THE DROP TUBE paints ships here,
 * namespaced `.g-ic-*`, injected exactly once per document.
 *
 * THE MACHINE ROOM: a dark observation deck over an opaque spiral chute. The
 * DOM owns the mood lighting and the reveal; the canvas (tube3d/tube2d) owns
 * the chute body. The fullscreen media loop breathes FADED behind everything -
 * its opacity is runtime-set from the ic_bg_fade setting x caps.bgIntensity,
 * never hardcoded here.
 *
 * ONE NUMBER SIZES THE MIDDLE OF THE ROOM. `--ic-basin-d` is the reveal
 * diameter; the bubble IS it, the stamp rides at 0.79 of it above centre, the
 * casino's marquee slot is a 2x square centred on the same point, its ALMOST
 * word rides 0.62 of it below, and the trickster's crooked ring is the bubble's
 * box grown 1.28x. Change that one token and the whole basin furniture moves
 * together (and only together with tube3d's TUBE_R - see .g-ic-bubble). Nobody
 * may re-type its value: read the token, fall back to the token.
 *
 * THE STAGE IS A BACKDROP ROOT. `.g-ic-bg` carries `isolation:isolate` so the
 * depth ring's backdrop-filter cannot reach past the media it exists to melt -
 * see the note on .g-ic-bg-depth.
 *
 * THE HOUSE RULES WAVE added three rooms to this sheet:
 *   THE SHEET   `.g-ic-howto` - the drawn class rules (Deck VI). Three
 *               vignettes built out of gradients, borders and masks: a chute
 *               mouth dropping a pink bubble into a dish beside a draining
 *               speed bar, an X bubble with its hold ring running out beside a
 *               barred hand, a pale bubble sliding off the rim. No raster art,
 *               no glyph font, and it reads correctly FROZEN.
 *   THE MACHINE the HUD is lit now: a rail that breathes across its top edge
 *               (--ic-lamp), a thread whose head pulses, a topline with a slow
 *               sweep (--ic-sweep), and the score as an ODOMETER (.g-ic-dig,
 *               one fixed-width slot per digit). The score's glow is driven
 *               from --ic-gleam on the CELL, never on .g-ic-score itself, so
 *               the casino deck owns that element's transform and animation
 *               outright and the two never collide.
 *   THE TICKET  `.g-ic-ticket` - the debrief as a printed receipt: punched
 *               perforations top and bottom, one slow light crawling the face
 *               (--ic-tk), a stamp bed for the grade-as-object, a lit pulsing
 *               submit and a ghost recal. `.is-royal` / `.is-perfect` gild it;
 *               `.is-dim` is the losing class - less light, never none.
 *
 * THE SLOTS ARE A CONTRACT. `.g-ic-marquee-slot`, `.g-ic-meter-slot` and
 * `.g-ic-ticket-stamp` are sized and placed HERE and filled by somebody else
 * (casino.js, the shell's 10-segment meter, the shell's stamp ceremony). Their
 * CONTENTS are never this file's business; their box and their
 * pointer-events:none promise always are.
 *
 * INPUT TRUST: only .g-ic-bubble takes pointer events. Every other layer is
 * pointer-events:none, including the flourish, the stamp, both slots and the
 * rules sheet - on which the single GO button re-enables them for itself,
 * because it is the one and only way off that sheet.
 *
 * NOTHING IS EVER STILL (Law III), AND THE STYLESHEET IS WHERE THAT IS SAFE:
 * every breathing lamp, sweep and pulse is a CSS animation, so `.suspended`
 * pauses all of them (elements AND pseudo-elements - the blanket covers both)
 * and prefers-reduced-motion neutralises them, even when a JS path forgets.
 *
 * NO hidden-ATTRIBUTE RULE LIVES HERE, EVER (web CLAUDE.md trap 27): the
 * attribute selector is a user-agent rule and any author `display:` beats it.
 * styles.css owns the one display:none!important reset for it; a competing
 * rule in a game sheet is how two opaque overlays once ate a whole playtest.
 * (The shell suite greps every game .js for the literal selector - that is
 * why this comment spells it out instead of writing it.)
 *
 * REGISTER WHAT YOU KEYFRAME. An unregistered custom property is a token, not
 * a value: animating it is a silent no-op. Every animatable prop below is
 * declared with @property (that is why --ic-k, the hold ring, works).
 * ==========================================================================*/

const STYLE_ID = 'g-ic-style';

export const STYLE_TEXT = `
.g-ic{--ic-pink:var(--pink,#FF69B4);--ic-lav:var(--lav,#B8A6E8);--ic-gold:var(--gold,#F0C24B);
  --ic-ink:var(--ink,#F2EBDD);--ic-dim:var(--ink-dim,#B9B3CE);--ic-faint:var(--ink-faint,#8A84A8);
  --ic-panel:var(--panel,#252542);--ic-line:var(--line,#3A3A5E);--ic-ground:var(--ground,#14142B);
  --ic-basin-d:clamp(132px,17.5vmin,238px);
  position:absolute;inset:0;overflow:hidden;color:var(--ic-ink);
  background:radial-gradient(circle at 50% 46%, #1E1E3C, var(--ic-ground) 74%)}

/* the animatable custom props, registered so keyframes can actually move them */
@property --ic-lamp { syntax: '<number>'; initial-value: .55; inherits: false; }
@property --ic-gleam { syntax: '<number>'; initial-value: .22; inherits: true; }
@property --ic-sweep { syntax: '<percentage>'; initial-value: -45%; inherits: false; }
@property --ic-hk { syntax: '<number>'; initial-value: 1; inherits: false; }
@property --ic-tk { syntax: '<percentage>'; initial-value: -30%; inherits: false; }

/* ------------------------------------------------------------ the backdrop */
/* isolation:isolate is LOAD-BEARING, not decoration. It makes this box the
   BACKDROP ROOT for .g-ic-bg-depth below: without it the depth ring's
   backdrop-filter walks the ancestor chain to the document root (nothing
   between here and <html> declares one), which is how a full-viewport,
   always-on blur ends up hoisting its render surface over the shell's fx layer
   in an accelerated webview and every engine effect reads as BEHIND the tube.
   The ring only ever wanted the two media <img>s under it. */
.g-ic-bg{position:absolute;inset:0;pointer-events:none;z-index:0;overflow:hidden;
  isolation:isolate;
  background:
    radial-gradient(120% 90% at 50% 40%, transparent 40%, rgba(6,6,16,.7) 100%),
    conic-gradient(from 210deg at 50% 46%, #191934, #222244 30%, #14142B 62%, #1d1d3a 100%)}
.g-ic-bg-img{position:absolute;inset:0;width:100%;height:100%;object-fit:cover;
  opacity:0;transition:opacity .9s ease;filter:saturate(.9) blur(2px);transform:scale(1.07)}
.g-ic-bg-img.on{animation:g-ic-kenburns 26s ease-in-out infinite alternate}
@keyframes g-ic-kenburns{from{transform:scale(1.07)}to{transform:scale(1.15) translateY(-1.5%)}}
/* the depth ring: media edges melt into atmosphere, the centre stays legible.
   Its backdrop is the two <img>s above it and NOTHING ELSE - .g-ic-bg isolates
   (see there). Blurring the whole document was never the intent and cost the
   engine's layer its place on top. */
.g-ic-bg-depth{position:absolute;inset:0;pointer-events:none;
  backdrop-filter:blur(14px) saturate(.85);
  -webkit-mask:radial-gradient(72% 58% at 50% 46%, transparent 34%, #000 78%);
  mask:radial-gradient(72% 58% at 50% 46%, transparent 34%, #000 78%)}
@supports not (backdrop-filter:blur(1px)){
  .g-ic-bg-depth{background:radial-gradient(72% 58% at 50% 46%, transparent 40%, rgba(10,10,26,.55) 85%)}}
.g-ic-bg-veil{position:absolute;inset:0;
  background:radial-gradient(90% 70% at 50% 50%, rgba(10,10,26,.25) 30%, rgba(10,10,26,.8) 100%)}

/* ------------------------------------------------------------ tube canvas */
.g-ic-tubewrap{position:absolute;inset:0;pointer-events:none;z-index:1}
/* THE DUSK (render.js nodes.dusk, driven by pressure.js): over the tube (z1),
   under the ring / basin / HUD. Opacity only - never a filter, never a
   backdrop-filter (a backdrop root here would re-sort the canvas, see trap 26). */
.g-ic-dusk{position:absolute;inset:0;pointer-events:none;z-index:2;opacity:0;
  transition:opacity 1.1s ease;
  background:radial-gradient(75% 68% at var(--ic-basin-x,50%) var(--ic-basin-y,50%), rgba(7,6,18,.66) 0%, rgba(7,6,18,.86) 55%, rgba(7,6,18,.94) 100%)}
html.arc-reduced .g-ic-dusk{transition:none}
/* z-index:0 is deliberate on a canvas that is already the only child: an
   ACCELERATED WebGL surface at z-index:auto leaves the compositor to infer its
   place from overlap alone, and this one covers the viewport. Pinned into the
   wrap's context it can never be hoisted over the shell's fx layer. The same
   line, for the same reason, is on dtrh's #sf-canvas. */
.g-ic-tube-canvas{position:absolute;inset:0;width:100%;height:100%;display:block;z-index:0}
/* the last-resort static tube (no canvas at all). NEGATIVE inset on purpose:
   the same composition law as tube3d/tube2d - the spiral has to leave the frame
   rather than stop inside it. */
.g-ic-tube-static{position:absolute;inset:-16%;border-radius:50%;opacity:.6;
  background:
    radial-gradient(circle at 50% 50%, rgba(8,8,22,.95) 0 12%, transparent 13%),
    repeating-conic-gradient(from 0deg, rgba(42,42,78,.85) 0 9deg, rgba(22,22,44,.85) 9deg 18deg);
  -webkit-mask:radial-gradient(circle, transparent 0 10%, #000 12% 70%, transparent 72%);
  mask:radial-gradient(circle, transparent 0 10%, #000 12% 70%, transparent 72%);
  animation:g-ic-static-spin 80s linear infinite}
@keyframes g-ic-static-spin{to{transform:rotate(360deg)}}

/* ------------------------------------------------------- the marquee slot */
/* EMPTY BY CONTRACT. casino.js hangs its bulb ring in here; this file only
   guarantees the box - a 2x --ic-basin-d square centred on the basin, under
   the reveal (z 2 against the basin's z 3) and never a pointer target. */
.g-ic-marquee-slot{position:absolute;left:var(--ic-basin-x,50%);top:var(--ic-basin-y,50%);z-index:2;pointer-events:none;
  width:calc(var(--ic-basin-d) * 2);height:calc(var(--ic-basin-d) * 2);
  transform:translate(-50%,-50%)}

/* -------------------------------------------------------------- the basin */
/* THE BUBBLE MUST FIT THE BORE. It travelled inside the chute, so a reveal
   WIDER than the tube's projected bore breaks the fiction on sight. At the
   camera tube3d.js pins, the bore (dia 1.7 world) lands ~0.18 x viewport-height
   near the basin, and the crater's own mouth (R_IN 2.5, dia 5.0 world) lands
   ~0.53 of it - so the DISH has room to spare and the BORE is the tight one.
   OWNER CALL 2026-08-22: the reveal reads 25% bigger, so the token went
   14vmin -> 17.5vmin (floor 106 -> 132px, ceiling 190 -> 238px). That puts the
   reveal at ~0.97 of the projected bore instead of ~0.8: still inside the
   crater on every sane window, but the bore no longer has visible margin around
   it. To buy the margin back the TUBE has to grow with it - TUBE_R 0.80 -> 1.00
   with R_OUT 14.0 -> 14.2 (the camera clearance law wants R_OUT - TUBE_R >=
   13.15), and tube2d's BORE_F 0.64 -> 0.80. Those are tube3d/tube2d's to make.
   Move this token ONLY together with them. */
/* THE LANDING: --ic-basin-x/-y are set on .g-ic by tube3d (render.js onLanding)
   to the middle of the tube's VISIBLE hole; 50%/50% is tube2d's answer. */
.g-ic-basin{position:absolute;left:var(--ic-basin-x,50%);top:var(--ic-basin-y,50%);width:0;height:0;z-index:3}
.g-ic-bubble{position:absolute;left:0;top:0;width:var(--ic-basin-d);height:var(--ic-basin-d);
  transform:translate(-50%,-50%) scale(.2);margin:0;cursor:pointer;border-radius:50%;
  opacity:0;pointer-events:none;transition:none}
.g-ic-bubble.on{opacity:1;pointer-events:auto;animation:g-ic-reveal .16s cubic-bezier(.2,1.6,.4,1) forwards}
@keyframes g-ic-reveal{from{transform:translate(-50%,-50%) scale(.24)}to{transform:translate(-50%,-50%) scale(1)}}
.g-ic-bubble.pop{animation:g-ic-pop .32s ease-out forwards}
@keyframes g-ic-pop{0%{transform:translate(-50%,-50%) scale(1);opacity:1}
  45%{transform:translate(-50%,-50%) scale(1.28);opacity:.85}
  100%{transform:translate(-50%,-50%) scale(1.55);opacity:0}}
.g-ic-bubble.fade{animation:g-ic-fadeaway .5s ease-in forwards}
@keyframes g-ic-fadeaway{to{transform:translate(-50%,-50%) scale(.6);opacity:0}}
.g-ic-bubble.hit{animation:g-ic-hitburst .4s ease-out forwards}
@keyframes g-ic-hitburst{0%{transform:translate(-50%,-50%) scale(1);filter:none}
  30%{transform:translate(-50%,-50%) scale(1.2);filter:saturate(3) hue-rotate(-40deg) brightness(1.5)}
  100%{transform:translate(-50%,-50%) scale(1.4);opacity:0;filter:saturate(3) hue-rotate(-40deg)}}
.g-ic-bubble-img{position:absolute;inset:0;width:100%;height:100%;object-fit:contain;
  filter:drop-shadow(0 6px 22px rgba(255,105,180,.35));pointer-events:none}
.g-ic-bubble.on .g-ic-bubble-img{animation:g-ic-bob 2.4s ease-in-out infinite}
@keyframes g-ic-bob{0%,100%{transform:translateY(0)}50%{transform:translateY(-4%)}}

/* the X composite - two crossed bars over the classic bubble */
.g-ic-x{position:absolute;inset:14%;display:none;pointer-events:none;
  filter:drop-shadow(0 0 14px rgba(255,64,96,.8))}
.g-ic-x.on{display:block}
.g-ic-x i{position:absolute;left:50%;top:50%;width:76%;height:14%;border-radius:8px;
  background:linear-gradient(180deg,#FF5A78,#D22B4E);border:2px solid rgba(255,255,255,.55)}
.g-ic-x-a{transform:translate(-50%,-50%) rotate(45deg)}
.g-ic-x-b{transform:translate(-50%,-50%) rotate(-45deg)}

/* the 2s hold ring on a denied reveal - CSS-clock countdown. The custom
   property must be REGISTERED or keyframing it is a no-op. */
@property --ic-k { syntax: '<number>'; initial-value: 1; inherits: false; }
.g-ic-holdring{position:absolute;inset:-14%;border-radius:50%;display:none;pointer-events:none;
  background:conic-gradient(var(--ic-lav) calc(var(--ic-k,1)*360deg), rgba(184,166,232,.14) 0);
  -webkit-mask:radial-gradient(circle closest-side, transparent 0 82%, #000 84%);
  mask:radial-gradient(circle closest-side, transparent 0 82%, #000 84%)}
.g-ic-holdring.on{display:block;animation:g-ic-hold var(--ic-hold,2000ms) linear forwards}
@keyframes g-ic-hold{from{--ic-k:1}to{--ic-k:0}}
@supports not (background:conic-gradient(red calc(var(--x)*360deg),blue 0)){
  .g-ic-holdring.on{background:rgba(184,166,232,.18);animation:g-ic-holdfade var(--ic-hold,2000ms) linear forwards}
  @keyframes g-ic-holdfade{from{opacity:1}to{opacity:.15}}}

/* ------------------------------------------------------------ the flourish */
.g-ic-flourish{position:absolute;left:var(--ic-basin-x,50%);top:var(--ic-basin-y,50%);width:0;height:0;pointer-events:none;z-index:4;display:grid;place-items:center;overflow:visible}
.g-ic-flourish-img{width:34vmin;height:34vmin;object-fit:contain;opacity:0;
  animation:g-ic-spiralout 1s ease-out forwards}
@keyframes g-ic-spiralout{0%{opacity:.9;transform:scale(.3) rotate(0)}
  100%{opacity:0;transform:scale(2.4) rotate(300deg)}}

/* --------------------------------------------------------------- the stamp */
/* tracks the bubble: just clear of its crown, still inside the crater's mouth */
.g-ic-stamp{position:absolute;left:var(--ic-basin-x,50%);top:calc(var(--ic-basin-y,50%) - var(--ic-basin-d) * .79);transform:translateX(-50%);
  z-index:5;pointer-events:none;font-weight:800;letter-spacing:.14em;font-size:clamp(15px,2vmin,22px);
  color:var(--ic-ink);opacity:0;text-transform:uppercase}
.g-ic-stamp.on{animation:g-ic-stampin .9s ease-out forwards}
@keyframes g-ic-stampin{0%{opacity:0;transform:translateX(-50%) translateY(8px)}
  15%{opacity:1;transform:translateX(-50%) translateY(0)}
  75%{opacity:1}100%{opacity:0;transform:translateX(-50%) translateY(-10px)}}
.g-ic-stamp.perfect{color:var(--ic-gold);text-shadow:0 0 18px rgba(240,194,75,.6)}
.g-ic-stamp.fast{color:var(--ic-pink)}
.g-ic-stamp.bad{color:#FF5A78;text-shadow:0 0 18px rgba(255,64,96,.6)}
.g-ic-stamp.calm{color:var(--ic-lav)}

/* --------------------------------------------------------- THE LIT MACHINE */
/* The topline: a small plate with a slow light crossing it, so the machine's
   nameplate is never a dead label. */
.g-ic-topline{position:absolute;left:18px;top:64px;z-index:5;pointer-events:none;overflow:hidden;
  display:flex;gap:10px;align-items:baseline;opacity:.85;padding:3px 10px;border-radius:7px;
  background:linear-gradient(180deg,rgba(20,20,44,.44),rgba(20,20,44,.14));
  border:1px solid rgba(58,58,94,.5)}
.g-ic-topline::after{content:"";position:absolute;top:0;bottom:0;width:34%;left:var(--ic-sweep,-45%);
  pointer-events:none;background:linear-gradient(90deg,transparent,rgba(255,255,255,.11),transparent);
  animation:g-ic-sweep 8s linear infinite}
@keyframes g-ic-sweep{0%{--ic-sweep:-45%}55%,100%{--ic-sweep:135%}}
.g-ic-topname{font-weight:700;letter-spacing:.12em;text-transform:uppercase;font-size:12px;color:var(--ic-lav)}
.g-ic-topsub{font-size:11px;color:var(--ic-faint);letter-spacing:.06em}

.g-ic-hud{position:absolute;left:0;right:0;bottom:0;z-index:5;pointer-events:none;
  display:flex;align-items:flex-end;justify-content:space-between;gap:16px;
  padding:14px 22px 16px;
  background:linear-gradient(180deg, transparent, rgba(8,8,20,.72) 60%)}
.g-ic-hud.off{display:none}
/* the machine's edge lamp: one hairline that breathes across the bottom strip */
.g-ic-hud::before{content:"";position:absolute;left:22px;right:22px;top:0;height:1px;pointer-events:none;
  background:linear-gradient(90deg,transparent,
    rgba(184,166,232,calc(.10 + var(--ic-lamp,.55) * .45)) 20%,
    rgba(255,105,180,calc(.16 + var(--ic-lamp,.55) * .62)) 50%,
    rgba(184,166,232,calc(.10 + var(--ic-lamp,.55) * .45)) 80%, transparent);
  animation:g-ic-lamp 4.6s ease-in-out infinite}
@keyframes g-ic-lamp{0%,100%{--ic-lamp:.28}50%{--ic-lamp:1}}
.g-ic-hud-cell{display:flex;flex-direction:column;gap:6px;min-width:120px}
/* the gleam rides the CELL, never .g-ic-score: casino.js owns that element's
   transform and animation, and two owners on one node is a fight */
.g-ic-hud-mid{align-items:center;flex:1;animation:g-ic-gleam 5.4s ease-in-out infinite}
@keyframes g-ic-gleam{0%,100%{--ic-gleam:.16}50%{--ic-gleam:.44}}
.g-ic-hud-right{align-items:flex-end}
.g-ic-score-label,.g-ic-streak-label{font-size:10px;letter-spacing:.18em;text-transform:uppercase;color:var(--ic-faint)}
/* THE ODOMETER: one fixed-width slot per digit, so a transform punch on the
   score can never reflow the number underneath it. BLOCK + text-align, not
   flex, on purpose: the trickster deck writes a bare string over this node for
   its stat flicker and reads the truth back, and a block box lays a raw text
   node out in exactly the same place as the digit slots. */
.g-ic-score{display:block;text-align:center;
  font-size:clamp(26px,4vmin,40px);font-weight:800;color:var(--ic-ink);
  font-variant-numeric:tabular-nums;line-height:1;
  text-shadow:0 0 calc(12px + var(--ic-gleam,.22) * 30px) rgba(255,105,180,var(--ic-gleam,.22))}
.g-ic-dig{display:inline-block;width:.62em;text-align:center;font-style:normal;
  font-variant-numeric:tabular-nums}
.g-ic-rt{font-size:11px;color:var(--ic-dim);font-variant-numeric:tabular-nums;min-height:14px}
.g-ic-counter{font-size:11px;color:var(--ic-dim);letter-spacing:.06em}
/* the film-strip thread: the fill advances, and its HEAD is a live lamp */
.g-ic-thread{position:relative;width:150px;height:3px;border-radius:2px;background:rgba(184,166,232,.18)}
.g-ic-thread-fill{display:block;position:relative;height:100%;border-radius:2px;background:var(--ic-pink);
  width:calc(var(--ic-prog,0)*100%);transition:width .4s ease}
.g-ic-thread-fill::after{content:"";position:absolute;right:-2px;top:50%;width:6px;height:6px;
  border-radius:50%;background:var(--ic-ink);pointer-events:none;
  box-shadow:0 0 10px rgba(255,105,180,.95), 0 0 3px rgba(255,255,255,.8);
  animation:g-ic-head 1.9s ease-in-out infinite}
@keyframes g-ic-head{0%,100%{opacity:.55;transform:translateY(-50%) scale(.75)}
  50%{opacity:1;transform:translateY(-50%) scale(1.25)}}
/* THE METER SLOT: empty, and it stays empty. ctx.ceremonies.streakMeter fills
   it with the SHELL's 10-segment meter (SYNTHESIS #10 - games skin it, never
   fork it). The dashed rail is only what an unfilled slot looks like. */
.g-ic-meter-slot{display:flex;align-items:center;justify-content:flex-end;
  min-width:118px;min-height:12px;pointer-events:none}
.g-ic-meter-slot:empty::after{content:"";display:block;width:112px;height:6px;border-radius:3px;
  border:1px dashed rgba(184,166,232,.28);animation:g-ic-slotwait 3.6s ease-in-out infinite}
@keyframes g-ic-slotwait{0%,100%{opacity:.35}50%{opacity:.8}}

/* ----------------------------------------------------- THE CLASS RULES SHEET */
/* Deck VI, Law IV: drawn, not told. Three vignettes, one way out. The sheet
   itself takes NO pointer events (the bubble under it is not live yet, and
   index.js binds inputs only after GO); the GO button re-enables them for
   itself alone. */
.g-ic-howto{position:absolute;left:50%;top:50%;transform:translate(-50%,-50%);z-index:9;
  width:min(520px,90vw);max-height:86vh;overflow:auto;pointer-events:none;
  display:flex;flex-direction:column;gap:2px;padding:22px 24px 20px;border-radius:16px;
  background:linear-gradient(180deg,rgba(26,26,54,.94),rgba(14,14,32,.96));
  border:1px solid var(--ic-line);
  box-shadow:0 30px 80px rgba(0,0,0,.55), 0 0 44px rgba(255,105,180,.10)}
.g-ic-hw-title{margin:0 0 8px;text-align:center;font-size:clamp(16px,2.4vmin,20px);
  letter-spacing:.2em;text-transform:uppercase;color:var(--ic-pink);
  text-shadow:0 0 22px rgba(255,105,180,.45)}
.g-ic-hw-row{display:flex;align-items:center;gap:16px;padding:11px 2px}
.g-ic-hw-row + .g-ic-hw-row{border-top:1px dashed rgba(58,58,94,.6)}
.g-ic-hw-cap{margin:0;flex:1 1 auto;font-size:12.5px;line-height:1.5;color:var(--ic-dim)}
.g-ic-hw-fig{flex:0 0 auto;display:flex;align-items:center;gap:9px;pointer-events:none}
.g-ic-hw-scene{position:relative;display:block;width:74px;height:56px;flex:0 0 auto}

/* 1 - the drop: mouth, falling light, dish, the pink bubble, the pop ring */
.g-ic-hw-mouth{position:absolute;left:50%;top:0;width:30px;height:13px;transform:translateX(-50%);
  border-radius:50%;background:linear-gradient(180deg,#0A0A1C,#2A2A50);
  border:1px solid rgba(184,166,232,.35);
  box-shadow:inset 0 -3px 6px rgba(0,0,0,.8), 0 0 12px rgba(184,166,232,.25)}
.g-ic-hw-spark{position:absolute;left:50%;top:9px;width:9px;height:9px;border-radius:50%;
  background:var(--ic-pink);opacity:0;box-shadow:0 0 12px rgba(255,105,180,.9);
  animation:g-ic-hw-fall 2.6s cubic-bezier(.4,.1,.7,1) infinite}
@keyframes g-ic-hw-fall{0%{opacity:0;transform:translate(-50%,0) scale(.4)}
  12%{opacity:1;transform:translate(-50%,6px) scale(.7)}
  40%{opacity:1;transform:translate(-50%,26px) scale(.85)}
  46%,100%{opacity:0;transform:translate(-50%,28px) scale(.9)}}
.g-ic-hw-dish{position:absolute;left:50%;bottom:2px;width:56px;height:20px;transform:translateX(-50%);
  border-radius:50%;border:1px solid rgba(184,166,232,.3);
  background:radial-gradient(60% 100% at 50% 0%, rgba(40,40,78,.95), rgba(14,14,32,.95));
  box-shadow:inset 0 4px 10px rgba(0,0,0,.6)}
.g-ic-hw-bub{position:absolute;left:50%;top:-6px;width:20px;height:20px;border-radius:50%;
  box-shadow:0 0 14px rgba(255,105,180,.6);
  background:radial-gradient(35% 32% at 36% 30%, rgba(255,255,255,.9), rgba(255,105,180,.95) 46%, rgba(190,50,120,.9));
  animation:g-ic-hw-land 2.6s ease-out infinite}
@keyframes g-ic-hw-land{0%,40%{opacity:0;transform:translate(-50%,0) scale(.2)}
  48%{opacity:1;transform:translate(-50%,0) scale(1.12)}
  74%{opacity:1;transform:translate(-50%,0) scale(1)}
  84%,100%{opacity:0;transform:translate(-50%,0) scale(1.5)}}
.g-ic-hw-ping{position:absolute;left:50%;top:-6px;width:20px;height:20px;border-radius:50%;
  border:2px solid var(--ic-pink);opacity:0;animation:g-ic-hw-ping 2.6s ease-out infinite}
@keyframes g-ic-hw-ping{0%,78%{opacity:0;transform:translate(-50%,0) scale(.9)}
  82%{opacity:.9;transform:translate(-50%,0) scale(1)}
  100%{opacity:0;transform:translate(-50%,0) scale(2.2)}}
.g-ic-hw-tap{flex:0 0 auto;display:block}
.g-ic-hw-tap.cap{min-width:42px;text-align:center;padding:6px 9px;border-radius:7px;
  font-size:11px;letter-spacing:.06em;color:var(--ic-ink);
  background:linear-gradient(180deg,var(--ic-panel),#1A1A32);
  border:1px solid var(--ic-line);border-bottom-width:3px;box-shadow:0 2px 0 rgba(0,0,0,.45);
  animation:g-ic-hw-press 2.6s ease-in-out infinite}
@keyframes g-ic-hw-press{0%,72%{transform:translateY(0)}
  80%{transform:translateY(2px)}92%,100%{transform:translateY(0)}}
.g-ic-hw-tap.finger{position:relative;width:22px;height:32px}
.g-ic-hw-tap.finger::before{content:"";position:absolute;left:50%;bottom:0;width:14px;height:20px;
  transform:translateX(-50%);border-radius:8px 8px 5px 5px;
  background:linear-gradient(180deg,var(--ic-lav),#6E5F9B);box-shadow:0 0 10px rgba(184,166,232,.35)}
.g-ic-hw-tap.finger::after{content:"";position:absolute;left:50%;top:0;width:8px;height:8px;
  border-radius:50%;background:var(--ic-ink);
  animation:g-ic-hw-tapdot 2.6s ease-in-out infinite}
@keyframes g-ic-hw-tapdot{0%,72%{opacity:.5;transform:translate(-50%,0) scale(.9)}
  82%{opacity:1;transform:translate(-50%,5px) scale(1)}
  92%,100%{opacity:.5;transform:translate(-50%,0) scale(.9)}}
/* the speed bar: it drains while the bubble sits there. Speed IS the score. */
.g-ic-hw-speed{position:relative;flex:0 0 auto;width:7px;height:44px;border-radius:4px;overflow:hidden;
  background:rgba(184,166,232,.16);border:1px solid rgba(184,166,232,.3)}
.g-ic-hw-speed i{position:absolute;left:0;right:0;top:0;display:block;
  background:linear-gradient(180deg,var(--ic-gold),var(--ic-pink));
  animation:g-ic-hw-drain 2.6s linear infinite}
@keyframes g-ic-hw-drain{0%,10%{height:100%}70%{height:6%}72%,100%{height:0}}

/* 2 - the X: the ring runs out and the hand does not move */
.g-ic-hw-xbub{position:absolute;left:50%;top:50%;width:38px;height:38px;transform:translate(-50%,-50%);
  border-radius:50%;box-shadow:0 0 16px rgba(255,64,96,.35);
  background:radial-gradient(35% 32% at 36% 30%, rgba(255,255,255,.75), rgba(150,140,190,.7) 48%, rgba(60,55,100,.85))}
.g-ic-hw-ring{position:absolute;inset:-6px;border-radius:50%;
  background:conic-gradient(var(--ic-lav) calc(var(--ic-hk,1)*360deg), rgba(184,166,232,.14) 0);
  -webkit-mask:radial-gradient(circle, transparent 0 78%, #000 80%);
  mask:radial-gradient(circle, transparent 0 78%, #000 80%);
  animation:g-ic-hw-hold 2.8s linear infinite}
@keyframes g-ic-hw-hold{0%{--ic-hk:1}86%,100%{--ic-hk:0}}
.g-ic-hw-cross{position:absolute;inset:16%;filter:drop-shadow(0 0 8px rgba(255,64,96,.8))}
.g-ic-hw-cross i{position:absolute;left:50%;top:50%;width:74%;height:15%;border-radius:5px;
  background:linear-gradient(180deg,#FF5A78,#D22B4E);border:1px solid rgba(255,255,255,.5)}
.g-ic-hw-xa{transform:translate(-50%,-50%) rotate(45deg)}
.g-ic-hw-xb{transform:translate(-50%,-50%) rotate(-45deg)}
.g-ic-hw-nohand{position:relative;flex:0 0 auto;width:28px;height:32px}
.g-ic-hw-nohand::before{content:"";position:absolute;left:50%;bottom:2px;width:14px;height:22px;
  transform:translateX(-50%);border-radius:8px 8px 5px 5px;opacity:.72;
  background:linear-gradient(180deg,#5A5480,#3A3560)}
.g-ic-hw-nohand::after{content:"";position:absolute;left:50%;top:50%;width:32px;height:3px;border-radius:2px;
  transform:translate(-50%,-50%) rotate(-38deg);background:#FF5A78;
  box-shadow:0 0 10px rgba(255,64,96,.8);animation:g-ic-hw-bar 2.8s ease-in-out infinite}
@keyframes g-ic-hw-bar{0%,100%{opacity:.55}50%{opacity:1}}

/* 3 - the drift: it slides off the rim and costs nothing. No penalty glyph, on
   purpose - the absence IS the lesson. */
.g-ic-hw-pale{position:absolute;left:50%;top:-6px;width:19px;height:19px;border-radius:50%;
  box-shadow:0 0 10px rgba(184,166,232,.28);
  background:radial-gradient(35% 32% at 36% 30%, rgba(255,255,255,.55), rgba(184,166,232,.5) 48%, rgba(80,74,120,.5));
  animation:g-ic-hw-drift 3.4s ease-in infinite}
@keyframes g-ic-hw-drift{0%{opacity:0;transform:translate(-50%,0) scale(.3)}
  14%,46%{opacity:1;transform:translate(-50%,0) scale(1)}
  100%{opacity:0;transform:translate(calc(-50% + 30px),12px) scale(.7)}}

/* the ONE way off the sheet */
.g-ic-hw-go{align-self:center;margin-top:10px;padding:11px 34px;cursor:pointer;pointer-events:auto;
  border-radius:999px;font-size:12.5px;font-weight:700;letter-spacing:.18em;text-transform:uppercase;
  color:#fff;background:linear-gradient(135deg,var(--ic-pink),#B8367E);
  border:1px solid var(--ic-pink);animation:g-ic-hw-go 2.2s ease-in-out infinite}
@keyframes g-ic-hw-go{0%,100%{box-shadow:0 0 18px rgba(255,105,180,.32);transform:scale(1)}
  50%{box-shadow:0 0 34px rgba(255,105,180,.62);transform:scale(1.03)}}
.g-ic-hw-go:hover{box-shadow:0 0 40px rgba(255,105,180,.7)}
.g-ic-hw-go:focus-visible{outline:2px solid var(--ic-ink);outline-offset:3px}

/* --------------------------------------------------------------- the cards */
.g-ic-break{position:absolute;left:50%;top:50%;transform:translate(-50%,-50%);z-index:6;
  text-align:center;max-width:min(560px,86vw);pointer-events:none;
  padding:26px 30px;border-radius:16px;background:rgba(14,14,32,.78);
  border:1px solid var(--ic-line);backdrop-filter:blur(6px)}
.g-ic-break-title{margin:0 0 8px;font-size:clamp(22px,3.4vmin,32px);letter-spacing:.16em;text-transform:uppercase;
  color:var(--ic-pink);text-shadow:0 0 26px rgba(255,105,180,.45)}
.g-ic-break-note{margin:0 0 6px;color:var(--ic-ink);font-size:15px}
.g-ic-break-hint{margin:0;color:var(--ic-faint);font-size:12.5px}

/* ------------------------------------------------------------- THE TICKET */
/* Deck V + VI: the debrief prints. Punched perforations top and bottom, one
   slow light crawling the face, the backdrop still breathing behind it (the
   scrim is translucent on purpose), and a bed where the grade lands as an
   object. .is-dim is the losing class: less light, never none. */
.g-ic-debrief{position:absolute;inset:0;z-index:7;display:grid;place-items:center;
  background:radial-gradient(80% 70% at 50% 42%, rgba(10,10,24,.55), rgba(8,8,20,.9));overflow:auto}
.g-ic-paper{width:min(680px,92vw);border-radius:14px;padding:26px 30px;
  background:linear-gradient(180deg,#20203E,#1A1A32);border:1px solid var(--ic-line);
  box-shadow:0 30px 80px rgba(0,0,0,.5)}
.g-ic-ticket{position:relative;overflow:hidden;
  background:
    repeating-linear-gradient(180deg, rgba(255,255,255,.022) 0 2px, transparent 2px 5px),
    linear-gradient(180deg,#20203E,#1A1A32)}
.g-ic-ticket::before,.g-ic-ticket::after{content:"";position:absolute;left:0;right:0;height:8px;
  pointer-events:none;
  background:radial-gradient(circle at 6px 4px, rgba(10,10,26,1) 3.4px, transparent 3.6px) 0 0/12px 8px repeat-x}
.g-ic-ticket::before{top:0}
.g-ic-ticket::after{bottom:0;transform:scaleY(-1)}
.g-ic-ticket-sweep{position:absolute;top:0;bottom:0;width:26%;left:var(--ic-tk,-30%);
  pointer-events:none;
  background:linear-gradient(100deg,transparent,rgba(255,255,255,.055),transparent);
  animation:g-ic-tsweep 9s linear infinite}
@keyframes g-ic-tsweep{0%{--ic-tk:-30%}62%,100%{--ic-tk:130%}}
.g-ic-ticket.is-dim .g-ic-ticket-sweep{opacity:.45}
.g-ic-ticket.is-dim .g-ic-paper-score b{color:var(--ic-dim);text-shadow:0 0 14px rgba(255,105,180,.16)}
.g-ic-ticket.is-perfect{border-color:rgba(240,194,75,.5);
  box-shadow:0 30px 80px rgba(0,0,0,.5), 0 0 40px rgba(240,194,75,.16)}
.g-ic-ticket.is-royal{border-color:var(--ic-gold);
  box-shadow:0 30px 80px rgba(0,0,0,.5), 0 0 60px rgba(240,194,75,.34)}
.g-ic-ticket.is-royal .g-ic-paper-score b{color:var(--ic-gold);text-shadow:0 0 30px rgba(240,194,75,.6)}
.g-ic-paper-head{display:flex;align-items:baseline;justify-content:space-between;gap:12px;
  border-bottom:1px dashed var(--ic-line);padding-bottom:10px;margin-bottom:14px}
.g-ic-paper-head h2{margin:0;font-size:18px;letter-spacing:.14em;text-transform:uppercase;color:var(--ic-lav)}
.g-ic-paper-sub{color:var(--ic-faint);font-size:12px}
.g-ic-paper-score{position:relative;display:flex;align-items:baseline;gap:10px;margin-bottom:14px}
.g-ic-paper-score b{font-size:44px;color:var(--ic-ink);font-variant-numeric:tabular-nums;line-height:1;
  text-shadow:0 0 24px rgba(255,105,180,.35)}
.g-ic-paper-score span{font-size:11px;letter-spacing:.18em;text-transform:uppercase;color:var(--ic-faint)}
/* THE STAMP BED: empty by contract - index.js drops ctx.ceremonies.stamp here */
.g-ic-ticket-stamp{align-self:center;margin-left:auto;position:relative;pointer-events:none;
  min-width:118px;min-height:46px;display:flex;align-items:center;justify-content:center;
  border:1px dashed rgba(184,166,232,.26);border-radius:9px}
.g-ic-ticket-stamp:empty{opacity:.5;animation:g-ic-slotwait 3.6s ease-in-out infinite}
.g-ic-paper-grid{display:grid;grid-template-columns:repeat(3,1fr);gap:10px;margin-bottom:14px}
.g-ic-cell{background:rgba(10,10,26,.55);border:1px solid var(--ic-line);border-radius:10px;
  padding:10px 12px;display:flex;flex-direction:column;gap:3px}
.g-ic-cell b{font-size:18px;color:var(--ic-ink);font-variant-numeric:tabular-nums}
.g-ic-cell span{font-size:10px;letter-spacing:.14em;text-transform:uppercase;color:var(--ic-faint)}
.g-ic-cell.good b{color:#7BD88F}.g-ic-cell.bad b{color:#FF5A78}.g-ic-cell.gold b{color:var(--ic-gold)}
.g-ic-paper-line{position:relative;margin:0 0 4px;color:var(--ic-dim);font-size:13px}
.g-ic-paper-hint{position:relative;margin:0 0 10px;color:var(--ic-faint);font-size:12px}
.g-ic-paper-actions{position:relative;display:flex;gap:10px;justify-content:flex-end;margin-top:8px;flex-wrap:wrap}
/* one-more framing: submit is lit and breathing, the ghost is honest and live */
.g-ic-submit{position:relative;animation:g-ic-submit 2.4s ease-in-out infinite}
@keyframes g-ic-submit{0%,100%{box-shadow:0 0 16px rgba(255,105,180,.3)}
  50%{box-shadow:0 0 32px rgba(255,105,180,.65)}}
.g-ic-recal{opacity:.72}
.g-ic-recal:hover{opacity:1}

/* --------------------------------------------------------------- the shake */
.g-ic.shake{animation:g-ic-shake .42s cubic-bezier(.36,.07,.19,.97)}
@keyframes g-ic-shake{10%,90%{transform:translate(-2px,1px)}20%,80%{transform:translate(4px,-2px)}
  30%,50%,70%{transform:translate(-7px,3px)}40%,60%{transform:translate(7px,-3px)}100%{transform:none}}
.g-ic.shake::after{content:"";position:absolute;inset:0;pointer-events:none;z-index:8;
  background:radial-gradient(90% 80% at 50% 50%, transparent 55%, rgba(255,42,84,.28) 100%);
  animation:g-ic-redwash .42s ease-out forwards}
@keyframes g-ic-redwash{from{opacity:1}to{opacity:0}}

/* --------------------------------------------------- suspend + motion law */
/* the blanket covers pseudo-elements too - half of the lit machine lives on
   ::before / ::after, and a bare star selector would leave every one running */
.g-ic.suspended *{animation-play-state:paused !important;transition:none !important}
.g-ic.suspended,.g-ic.suspended::before,.g-ic.suspended::after,
.g-ic.suspended *::before,.g-ic.suspended *::after{animation-play-state:paused !important}
@media (prefers-reduced-motion: reduce){
  .g-ic-bg-img.on{animation:none}
  .g-ic-bubble.on{animation:none;transform:translate(-50%,-50%) scale(1)}
  .g-ic-bubble.on .g-ic-bubble-img{animation:none}
  .g-ic.shake{animation:none}
  .g-ic-flourish-img{animation-duration:.24s}
  .g-ic-stamp.on{animation-duration:.5s}
  .g-ic-tube-static{animation:none}
  /* the lit machine and the ticket hold their pose */
  .g-ic-hud::before,.g-ic-hud-mid,.g-ic-topline::after,.g-ic-thread-fill::after,
  .g-ic-meter-slot:empty::after,.g-ic-ticket-sweep,.g-ic-ticket-stamp:empty,
  .g-ic-submit{animation:none}
  /* the sheet must still READ frozen: everything that only exists mid-keyframe
     gets an explicit resting pose */
  .g-ic-hw-spark,.g-ic-hw-bub,.g-ic-hw-ping,.g-ic-hw-tap.cap,.g-ic-hw-tap.finger::after,
  .g-ic-hw-speed i,.g-ic-hw-ring,.g-ic-hw-nohand::after,.g-ic-hw-pale,
  .g-ic-hw-go{animation:none}
  .g-ic-hw-spark,.g-ic-hw-ping{opacity:0}
  .g-ic-hw-bub,.g-ic-hw-pale{opacity:1;transform:translate(-50%,0) scale(1)}
  .g-ic-hw-tap.finger::after{opacity:.85;transform:translate(-50%,0)}
  .g-ic-hw-speed i{height:46%}
  .g-ic-hw-ring{--ic-hk:.62}
  .g-ic-hw-nohand::after{opacity:1}
}
`;

export function ensureStyle() {
  try {
    if (typeof document === 'undefined' || !document.head || !document.getElementById) return;
    if (document.getElementById(STYLE_ID)) return;
    const s = document.createElement('style');
    s.id = STYLE_ID;
    s.textContent = STYLE_TEXT;
    document.head.appendChild(s);
  } catch (e) { /* unstyled is still playable */ }
}
