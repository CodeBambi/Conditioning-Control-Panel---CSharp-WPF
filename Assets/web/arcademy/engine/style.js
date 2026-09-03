/* ============================================================================
 * engine/style.js — the engine injects its OWN stylesheet from JS.
 *
 * The engine does not own styles.css (the shell agent does), so every class it
 * needs ships here, namespaced `.ae-*` (arcademy engine). Injected exactly once
 * per document, lazily, on the first createEngine() call.
 *
 * Colour tokens read from the shell's CSS variables when present and fall back
 * to the mockup palette, so a mod palette skins the effects for free.
 * `prefers-reduced-motion` is respected IN CSS as well as in JS: the media query
 * below neutralises every animation, so even a stray node cannot strobe.
 * ==========================================================================*/

import { hasDom } from './util.js';

const STYLE_ID = 'ae-engine-style';

export const STYLE_TEXT = `
.ae-layer{position:absolute;inset:0;overflow:hidden;pointer-events:none;z-index:40;
  --ae-pink:var(--ac-pink,#FF69B4);--ae-lav:var(--ac-lavender,#B8A6E8);
  --ae-gold:var(--ac-gold,#F0C24B);--ae-ink:var(--ac-ink,#F2EBDD);--ae-ground:var(--ac-ground,#14142B)}
.ae-layer.ae-suspended{display:none !important}
.ae-back,.ae-mid,.ae-front{position:absolute;inset:0;overflow:hidden;pointer-events:none}
.ae-back{z-index:1}.ae-mid{z-index:2}.ae-front{z-index:3}

/* ---- washes: ONE reused element per kind, opacity-toggled ---------------- */
.ae-wash{position:absolute;inset:0;opacity:0;transition:opacity .45s ease;will-change:opacity;
  background-repeat:no-repeat;background-position:center;background-size:cover}
.ae-wash-pink{background:radial-gradient(circle at 50% 55%,rgba(255,105,180,.55),rgba(255,105,180,.12) 60%,transparent 78%);
  mix-blend-mode:screen}
.ae-wash-spiral{mix-blend-mode:screen;background-image:conic-gradient(from 0deg,rgba(255,105,180,.55),rgba(20,20,43,0) 25%,rgba(184,166,232,.5) 50%,rgba(20,20,43,0) 75%,rgba(255,105,180,.55));
  animation:ae-spin 9s linear infinite;
  /* a SQUARE wider than the viewport's diagonal (sqrt2 x vmax), centred: a
     rotating inset:0 rectangle bares its corners every quarter turn */
  inset:auto;left:50%;top:50%;width:150vmax;height:150vmax;margin:-75vmax 0 0 -75vmax}
.ae-wash-drain{background:radial-gradient(circle at 50% 50%,rgba(0,0,0,.15),rgba(0,0,0,.72) 85%);
  backdrop-filter:blur(6px) saturate(.75);-webkit-backdrop-filter:blur(6px) saturate(.75)}
.ae-wash-sublim{background:linear-gradient(0deg,rgba(184,166,232,.28),rgba(255,105,180,.18));mix-blend-mode:screen}
.ae-wash-static{animation:none !important}
@keyframes ae-spin{from{transform:rotate(0)}to{transform:rotate(360deg)}}

/* ---- crt / scanline / chroma dressing ------------------------------------ */
.ae-crt{position:absolute;inset:0;opacity:0;transition:opacity .5s ease;mix-blend-mode:overlay}
.ae-crt-scanline{background:repeating-linear-gradient(180deg,rgba(0,0,0,.55) 0 1px,rgba(0,0,0,0) 1px 3px)}
.ae-crt-chroma{background:linear-gradient(90deg,rgba(255,0,80,.18),rgba(0,255,220,.14));mix-blend-mode:screen}
.ae-crt-bloom{background:radial-gradient(circle at 50% 50%,rgba(255,255,255,.14),transparent 70%)}
.ae-crt-live{animation:ae-crt-roll 6.5s linear infinite}
@keyframes ae-crt-roll{0%{background-position-y:0}100%{background-position-y:120px}}

/* ---- sub_flash ----------------------------------------------------------- */
.ae-sub{position:absolute;left:50%;top:50%;transform:translate(-50%,-50%);opacity:0;
  animation:ae-sub-blip var(--ae-dur,420ms) ease-out forwards;max-width:44vw;max-height:44vh}
/* HONEST BOX (media variant only - a word box sizes to its text): a cold <img>
   has intrinsic height 0 and painted NOTHING for its whole blip. An intrinsic
   square inside the existing 44vw/44vh maxima + the burst's faint tint, so an
   unloaded card still reads as a card that is filling in. Cross-platform. */
img.ae-sub{width:min(38vw,38vh);aspect-ratio:1;object-fit:cover;border-radius:10px;
  background:rgba(255,105,180,.10)}
.ae-sub-word{font-family:Georgia,'Times New Roman',serif;font-weight:700;letter-spacing:.06em;
  color:var(--ae-ink);text-shadow:0 0 18px var(--ae-pink);font-size:clamp(28px,6vw,64px);white-space:nowrap}
.ae-sub-scatter{left:var(--ae-x,50%);top:var(--ae-y,50%)}
.ae-sub-stamp{border:2px solid var(--ae-pink);padding:.15em .5em;border-radius:4px}
@keyframes ae-sub-blip{0%{opacity:0;transform:translate(-50%,-50%) scale(.94)}
  22%{opacity:var(--ae-alpha,.6)}70%{opacity:var(--ae-alpha,.6)}100%{opacity:0;transform:translate(-50%,-50%) scale(1.03)}}

/* ---- flash_burst / gif_burst nodes -------------------------------------- */
/* HONEST BOX: width-only + a cold media node = intrinsic height 0 = an
   invisible burst for its whole hold (the owner's placeholder-only rain bug,
   same flaw here). aspect-ratio pins the card's height to its width var so an
   unloaded node still reads as a card filling in; the tint was already here.
   Cross-platform by design (A). .ae-burst-cover's explicit height wins over it. */
.ae-burst{position:absolute;left:var(--ae-x,50%);top:var(--ae-y,50%);width:var(--ae-size,180px);
  aspect-ratio:1;
  transform:translate(-50%,-50%) rotate(var(--ae-rot,0deg));opacity:0;border-radius:10px;
  box-shadow:0 0 22px rgba(255,105,180,.35);animation:ae-burst-in var(--ae-dur,900ms) ease-out forwards;
  object-fit:cover;background:rgba(255,105,180,.10)}
.ae-burst-clickable{pointer-events:auto;cursor:pointer}
.ae-burst-double{filter:saturate(1.3) contrast(1.1)}
.ae-burst-ring{border:2px solid var(--ae-pink)}
/* FULL BLEED (additive): one node over the WHOLE layer - CCP's fullscreen GIF.
   No transform of its own (so the scale keyframe cannot shrink it off the
   edges) and object-fit:cover so a portrait loop still fills a landscape stage. */
.ae-burst-cover{left:0;top:0;width:100%;height:100%;border-radius:0;transform:none;
  object-fit:cover;box-shadow:none;animation-name:ae-burst-cover-in}
@keyframes ae-burst-cover-in{0%{opacity:0}14%{opacity:var(--ae-alpha,.75)}
  76%{opacity:var(--ae-alpha,.75)}100%{opacity:0}}
@keyframes ae-burst-in{0%{opacity:0;transform:translate(-50%,-50%) rotate(var(--ae-rot,0deg)) scale(.72)}
  18%{opacity:var(--ae-alpha,.75)}72%{opacity:var(--ae-alpha,.75)}
  100%{opacity:0;transform:translate(-50%,-50%) rotate(var(--ae-rot,0deg)) scale(1.06)}}

/* ---- gif_rain ----------------------------------------------------------- */
/* HONEST BOX: same law as .ae-burst - width only meant a cold <img>/<video>
   fell as a 0-height nothing for its whole 2.9-4.3s life (only the 240x240
   placeholder SVGs ever painted). A square card + a faint pink-violet tint so
   the fall reads even before the media lands. Cross-platform by design (A). */
.ae-rain{position:absolute;top:-22vh;left:var(--ae-x,10%);width:var(--ae-size,140px);border-radius:8px;
  aspect-ratio:1;background:linear-gradient(160deg,rgba(255,105,180,.12),rgba(184,166,232,.10));
  animation:ae-fall var(--ae-fall,3s) linear forwards;opacity:var(--ae-alpha,.8);object-fit:cover}
@keyframes ae-fall{from{transform:translateY(0) rotate(var(--ae-rot,0deg))}
  to{transform:translateY(128vh) rotate(calc(var(--ae-rot,0deg) * 3))}}

/* ---- bubble_field ------------------------------------------------------- */
.ae-bubble{position:absolute;left:var(--ae-x,50%);bottom:-12vh;width:var(--ae-size,72px);height:var(--ae-size,72px);
  border-radius:50%;background:radial-gradient(circle at 34% 30%,rgba(255,255,255,.75),rgba(255,105,180,.30) 45%,rgba(184,166,232,.18) 70%,transparent 74%);
  border:1px solid rgba(255,255,255,.35);opacity:var(--ae-alpha,.4);
  animation:ae-rise var(--ae-dur,7s) linear forwards}
.ae-bubble-clickable{pointer-events:auto;cursor:pointer}
.ae-bubble-pop{animation:ae-pop 260ms ease-out forwards}
@keyframes ae-rise{from{transform:translate(-50%,0)}to{transform:translate(calc(-50% + var(--ae-sway,20px)),-118vh)}}
@keyframes ae-pop{to{transform:translate(-50%,0) scale(1.6);opacity:0}}

/* ---- ambient_field (DOM particles, no canvas) --------------------------- */
.ae-mote{position:absolute;left:var(--ae-x,50%);top:var(--ae-y,50%);width:var(--ae-size,4px);height:var(--ae-size,4px);
  border-radius:50%;background:var(--ae-col,var(--ae-lav));opacity:var(--ae-alpha,.25);
  animation:ae-float var(--ae-dur,14s) linear infinite;animation-delay:var(--ae-delay,0s)}
.ae-mote-fleck{border-radius:1px;width:calc(var(--ae-size,4px) * .7);height:calc(var(--ae-size,4px) * 1.8)}
.ae-mote-star{border-radius:0;clip-path:polygon(50% 0,61% 35%,98% 35%,68% 57%,79% 91%,50% 70%,21% 91%,32% 57%,2% 35%,39% 35%)}
@keyframes ae-float{from{transform:translate(0,0)}
  50%{transform:translate(var(--ae-dx,12px),calc(var(--ae-dy,-40px) * .5))}
  to{transform:translate(0,var(--ae-dy,-40px))}}

/* ---- glitch_swap transitions -------------------------------------------- */
.ae-glitch{position:relative;animation:ae-shudder var(--ae-dur,600ms) steps(2,end) 1}
.ae-glitch-rgbsplit{text-shadow:2px 0 rgba(255,0,80,.9),-2px 0 rgba(0,255,220,.9);filter:contrast(1.15)}
.ae-glitch-vhsroll{animation:ae-vhs var(--ae-dur,600ms) linear 1}
.ae-glitch-datamosh{filter:url(#none) saturate(1.6) hue-rotate(18deg);animation:ae-mosh var(--ae-dur,600ms) steps(3,end) 1}
.ae-glitch-crossfade{transition:opacity var(--ae-dur,600ms) ease}
@keyframes ae-shudder{0%,100%{transform:translate(0,0)}25%{transform:translate(-2px,1px)}
  50%{transform:translate(2px,-1px)}75%{transform:translate(-1px,-2px)}}
@keyframes ae-vhs{0%{clip-path:inset(0 0 0 0)}30%{clip-path:inset(18% 0 42% 0);transform:translateX(6px)}
  60%{clip-path:inset(52% 0 12% 0);transform:translateX(-5px)}100%{clip-path:inset(0 0 0 0);transform:none}}
@keyframes ae-mosh{0%{filter:saturate(1.6) hue-rotate(0)}50%{filter:saturate(2.2) hue-rotate(40deg) blur(1px)}
  100%{filter:none}}

/* ---- row_drift ---------------------------------------------------------- */
.ae-drift{will-change:transform}
.ae-drift-breathe{animation:ae-breathe 5.5s ease-in-out infinite}
@keyframes ae-breathe{0%,100%{opacity:1}50%{opacity:.72}}

/* ---- ceremonies --------------------------------------------------------- */
.ae-stamp{position:absolute;left:50%;top:50%;transform:translate(-50%,-50%) rotate(-9deg) scale(1.6);
  padding:.18em .6em;border:4px solid var(--ae-pink);border-radius:6px;color:var(--ae-ink);
  font-family:Georgia,'Times New Roman',serif;font-weight:800;letter-spacing:.10em;text-transform:uppercase;
  font-size:clamp(22px,5vw,54px);opacity:0;animation:ae-stamp-in 900ms cubic-bezier(.2,1.4,.3,1) forwards;
  text-shadow:0 2px 0 rgba(0,0,0,.45)}
.ae-stamp-bad{border-color:var(--ae-lav);color:var(--ae-lav)}
.ae-stamp-gild{border-color:var(--ae-gold);color:var(--ae-gold);box-shadow:0 0 28px rgba(240,194,75,.5)}
@keyframes ae-stamp-in{0%{opacity:0;transform:translate(-50%,-50%) rotate(-9deg) scale(1.8)}
  35%{opacity:1;transform:translate(-50%,-50%) rotate(-9deg) scale(.96)}
  55%{transform:translate(-50%,-50%) rotate(-9deg) scale(1.02)}
  80%{opacity:1}100%{opacity:0;transform:translate(-50%,-50%) rotate(-9deg) scale(1.04)}}

.ae-meter{position:absolute;left:50%;bottom:3%;transform:translateX(-50%);display:flex;gap:4px;
  padding:5px 8px;border-radius:999px;background:rgba(20,20,43,.55);opacity:0;transition:opacity .3s ease}
.ae-meter-on{opacity:1}
.ae-seg{width:12px;height:6px;border-radius:3px;background:rgba(242,235,221,.18)}
.ae-seg-lit{background:var(--ae-pink);box-shadow:0 0 calc(4px + var(--ae-glow,0) * 12px) var(--ae-pink)}

.ae-jackpot{position:absolute;inset:0;opacity:0;background:radial-gradient(circle at 50% 50%,rgba(240,194,75,.30),transparent 68%);
  animation:ae-jack 2s ease-out forwards;mix-blend-mode:screen}
@keyframes ae-jack{0%{opacity:0}12%{opacity:1}70%{opacity:.7}100%{opacity:0}}
.ae-dim{position:absolute;inset:0;background:#000;opacity:0;animation:ae-dim 250ms ease-out forwards}
@keyframes ae-dim{0%{opacity:0}60%{opacity:.5}100%{opacity:0}}
.ae-spark{position:absolute;left:var(--ae-x,50%);top:var(--ae-y,50%);width:8px;height:8px;border-radius:50%;
  background:var(--ae-col,var(--ae-gold));opacity:.9;animation:ae-spark var(--ae-dur,900ms) ease-out forwards}
@keyframes ae-spark{from{transform:translate(-50%,-50%) scale(.4)}
  to{transform:translate(calc(-50% + var(--ae-dx,0px)),calc(-50% + var(--ae-dy,0px))) scale(1.1);opacity:0}}
.ae-nearmiss{position:absolute;inset:0;background:linear-gradient(0deg,rgba(255,105,180,.5),transparent);
  opacity:0;animation:ae-near 400ms ease-out forwards}
@keyframes ae-near{0%{opacity:0}40%{opacity:var(--ae-alpha,.1)}100%{opacity:0}}

/* ---- the lite rung: html.ae-lite ------------------------------------------
   Set by whoever knows the frame budget (The Deep End's de_perf ladder today;
   the engine never owns the class, it only reads it - the same seam
   util.js's shared decoder budget uses). This is NOT reduced motion: the
   effects still play, they just stop asking for the two things that cost a
   weak machine a whole frame.
     - a backdrop-filter is a full-screen read-back-and-blur, per frame, for as
       long as the drain wash is up. The wash's own gradient carries the drain
       on its own; only the blur goes.
     - the spiral is a 150vmax square rotating forever: the biggest composited
       layer the engine ever makes. It holds still instead of stopping, so a
       held wash still reads as a wash. */
.ae-lite .ae-wash-drain{backdrop-filter:none;-webkit-backdrop-filter:none}
.ae-lite .ae-wash-spiral{animation:none}

/* ---- THE SEEP, class-side (tells 13 and 14) -------------------------------
   engine.deadBeat()'s pixels, and nothing else in this sheet may use them. The
   same five laws the shell's own seep block lives under:
     - compositor only: opacity and transform. No blend modes, no filters, and
       NO background-position drift - the scanline sheet is oversized by exactly
       one tile period and TRANSLATED by that period (trap 36).
     - pointer-events:none on every layer, always. .ae-layer is already
       pointer-events:none by construction; this is the belt to that brace, so a
       frame can never eat a tap however badly it is timed (traps 27 and 59).
     - no 'forwards' fill anywhere (trap 74): the node is removed on its last
       frame, and a welded opacity would outlive it.
     - one pulse per tell, no repeat, sub-quarter-second (photosensitivity).
     - reduced motion never sees either of them - the director retires every
       animated tell before it is ever asked, and the media query at the bottom
       of this sheet is the second brace.
   THE COLD PALETTE reads the shell's tokens when they are there and carries the
   Annex phosphor as its fallback. It is deliberately NOT skinnable through the
   --ac-* mod vars the rest of this sheet uses: the seep is the feed showing
   through, and the whole point is that it does not match the school. */
.ae-seep{position:absolute;pointer-events:none;
  --ae-seep-cold:var(--seep-cold,#8FE0CE);--ae-seep-deep:var(--seep-deep,#0D2724)}

/* 13 THE OVERSEEN FRAME - between rounds the stage is a monitor for a blink. */
.ae-seep-overseen{inset:0;overflow:hidden;opacity:0;
  animation:ae-seep-blink var(--ae-seep-ms,240ms) ease-out 1}
.ae-seep-scan{inset:-6px 0;
  background:repeating-linear-gradient(180deg,rgba(0,0,0,.5) 0 1px,rgba(0,0,0,0) 1px 3px);
  animation:ae-seep-drift .24s linear infinite}
.ae-seep-rec{right:10px;top:8px;color:var(--ae-seep-cold);letter-spacing:.12em;
  font:400 13px/1 ui-monospace,'Cascadia Mono',Consolas,monospace}
@keyframes ae-seep-blink{0%{opacity:0}18%{opacity:.85}72%{opacity:.85}100%{opacity:0}}
@keyframes ae-seep-drift{to{transform:translateY(3px)}}

/* 14 THE RESUME SLATE - a feed reacquiring, between the press and the re-arm. */
.ae-seep-slate{inset:0;overflow:hidden;display:flex;align-items:center;justify-content:center;
  background:var(--ae-seep-deep);
  background-image:repeating-linear-gradient(180deg,rgba(143,224,206,.10) 0 1px,transparent 1px 3px);
  color:var(--ae-seep-cold);letter-spacing:.14em;
  font:400 clamp(16px,3.2vw,26px)/1 ui-monospace,'Cascadia Mono',Consolas,monospace;
  animation:ae-seep-sync var(--ae-seep-ms,120ms) steps(1,end) 1}
.ae-seep-slate-id{position:static}
@keyframes ae-seep-sync{0%,86%{opacity:1}100%{opacity:0}}

/* ---- the touch rung: html.ae-touch ----------------------------------------
   Set the same way .ae-lite is (the game knows the device; the engine only
   reads the class), but it is NOT a quality rung - it is a HARDWARE ceiling
   that applies ON FULL TOO, and it stacks with lite rather than replacing it.
   A phone pays for three things a desktop GPU eats for free, and WebKit pays
   the most for all three:
     - backdrop-filter, the full-screen read-back-and-blur, every frame the
       drain wash is up. Its gradient already carries the drain; only the blur
       goes, exactly as in lite.
     - a BLEND SURFACE has to read what is under it before it can write, and
       these five are FULL-SCREEN (the spiral is 150vmax, over twice the
       viewport; .ae-crt is inset:0 with overlay - it was the one fullscreen
       blend this list originally missed). On the near-black ground screen,
       overlay and plain alpha read almost identically, so the tint stays and
       the read-back goes.
     - a FILTER over a live decode is a whole GPU pass per decoded frame
       (ae-burst-double lands on gif_burst nodes, which are <video> whenever
       the pool hands back a webm), and ae-mosh's blur re-runs that pass every
       frame of the swap. Both drop; datamosh keeps the transform-only shudder
       so a swap still reads as a glitch.
   The scanline roll is per-frame re-raster of a full-screen sheet
   (background-position, the one pattern the perf trace named), so it holds
   still on touch - the scanlines themselves stay. */
.ae-touch .ae-wash-drain{backdrop-filter:none;-webkit-backdrop-filter:none}
.ae-touch .ae-wash-pink,.ae-touch .ae-wash-spiral,.ae-touch .ae-wash-sublim,.ae-touch .ae-jackpot,.ae-touch .ae-crt{mix-blend-mode:normal}
.ae-touch .ae-crt-live{animation:none}
.ae-touch .ae-burst-double{filter:none}
.ae-touch .ae-glitch-datamosh{filter:none;animation:ae-shudder var(--ae-dur,600ms) steps(2,end) 1}
/* the mobile graphics diet (2026-08-26), same ceiling:
     - the spiral holds still exactly as on lite - a held spiral still reads as
       a wash while parked;
     - will-change on every wash pre-promotes a DPR-sized layer per held kind
       even at opacity 0; the .45s opacity transition promotes on demand;
     - rgbsplit keeps its text-shadow, drops the filter pass;
     - vhsroll's clip-path is a per-step re-raster of the whole node - it gets
       the transform-only shudder datamosh already gets;
     - the burst box-shadow is a blur pass per node per frame of its fade. */
.ae-touch .ae-wash-spiral{animation:none}
.ae-touch .ae-wash{will-change:auto}
.ae-touch .ae-glitch-rgbsplit{filter:none}
.ae-touch .ae-glitch-vhsroll{animation:ae-shudder var(--ae-dur,600ms) steps(2,end) 1}
.ae-touch .ae-burst{box-shadow:none}
/* THE STILL PLATE (phone fx diet, measured 2026-08-28; util.js plateEl). On
   touch a url-string spiral wash and a full-bleed gif burst paint the gif's
   FIRST FRAME into a small canvas instead of running the animated gif, which
   decoded every frame on the main thread and re-rastered every covered device
   pixel per frame advance (3.1s of GPU per 8s for one bundled spiral). The
   wash plate fills its element (the 150vmax square, held still by the rule
   above) and takes over the slow spin the CSS conic gives up here: a transform
   on the canvas is compositor-only - no decode, no raster - and it is only
   worn while the hold is live (sustained.js plateActive), never parked at 0.
   No will-change: the animation promotes on demand, like the wash opacity. */
.ae-touch .ae-wash-plate{position:absolute;inset:0;width:100%;height:100%;display:block;object-fit:cover;pointer-events:none}
.ae-touch .ae-wash-plate-spin{animation:ae-spin 18s linear infinite}
/* .ae-mote stays animated on touch ON PURPOSE: ae-float is transform-only
   (compositor-cheap, no re-raster), and the phone cost of the ambient field is
   its NODE COUNT, which the lite ladder now caps (curves.js ambientLite). */

/* ---- reduced motion: neutralise EVERY animation -------------------------- */
@media (prefers-reduced-motion: reduce){
  .ae-layer *{animation-duration:.01ms !important;animation-iteration-count:1 !important;
    transition-duration:.12s !important}
  .ae-wash-spiral,.ae-wash-plate,.ae-crt-live,.ae-mote,.ae-drift-breathe{animation:none !important}
  .ae-sub,.ae-burst,.ae-rain,.ae-bubble,.ae-stamp{animation:none !important;opacity:var(--ae-alpha,.5) !important}
  /* the seep's animated tells are already retired at the director; this is the
     brace that survives a director nobody wired */
  .ae-seep{animation:none !important;opacity:0 !important}
}
`;

/** Inject the engine stylesheet once per document. No-op headless. */
export function injectStyle() {
  if (!hasDom()) return false;
  if (document.getElementById(STYLE_ID)) return false;
  const tag = document.createElement('style');
  tag.id = STYLE_ID;
  tag.textContent = STYLE_TEXT;
  (document.head || document.documentElement).appendChild(tag);
  return true;
}

export default injectStyle;
