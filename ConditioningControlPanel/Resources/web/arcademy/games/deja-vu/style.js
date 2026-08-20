/* ============================================================================
 * games/deja-vu/style.js - the game injects its OWN stylesheet from JS.
 *
 * THE MEMORY LAB (immersion wave, campus style approved dde28c0cf). The class
 * root is a FULL-VIEWPORT stage now: this file paints the whole window - a cold
 * specimen lab at night - and scales the board so the CHOSEN size fills the
 * frame (a 6-pair board gets huge lightbox plates, never a small grid floating
 * in darkness). Composition:
 *
 *   .g-dv-stage   absolute inset:0, explicit ground (never inherits), padded
 *                 under the shell's ~56px proctor strip
 *   .g-dv-lab     ambience layer (pointer-events:none): corner monitor glows,
 *                 scanlines, a slow oscilloscope sweep, vignette
 *   .g-dv-hud     lab readout strip (mono, letter-spaced, glowing)
 *   .g-dv-boardwrap  the bench - flex:1, the grid centered and height-fitted
 *                 via --g-dv-cols/--g-dv-rows calc (plates are 4/3 landscape)
 *   .g-dv-rack    right-edge specimen rack - one slide chip per matched pair
 *                 (pure decoration, pointer-events:none)
 *
 * Cards are specimen slides: glass sheen, etched monogram back; face-up = a
 * lightbox plate; locked = archived (desaturated, filed tag). The preview beam
 * (.g-dv-grid.scanning) reads as "the machine shows you"; the swap tell keeps
 * its 600ms shudder and gains a static-noise pass (dressing on the SAME class -
 * when/what swaps is untouched, law 2 of index.js).
 *
 * styles.css is shell chrome ONLY, so Deja Vu ships its CSS the way
 * engine/style.js does: one <style> tag, injected once, everything namespaced
 * .g-dv-*. Colours come from the shell tokens (--pink / --lav / --gold /
 * --panel ... via color-mix), so a mod palette reskins the lab for free.
 *
 * REDUCED MOTION twice over: `html.arc-reduced` AND the media query. The
 * mechanic survives, the motion does not: flips become crossfades, the swap
 * tell becomes a border pulse, the beam/sweep/rack-pop are simply gone - and
 * the audio tick is always retained. A suspend freezes every animation via
 * .g-dv-stage.suspended (animation-play-state), so the lab holds its breath
 * with the class.
 * ==========================================================================*/

const STYLE_ID = 'g-dv-style';

export const STYLE_TEXT = `
/* ---- the stage: the whole window is the lab ------------------------------ */
.g-dv-stage{position:absolute;inset:0;overflow:hidden;display:flex;flex-direction:column;
  align-items:center;gap:8px;padding:72px 18px 14px;
  --g-dv-gap:14px;
  background:
    radial-gradient(1000px 560px at 10% -6%, color-mix(in srgb, var(--lav), transparent 80%), transparent 62%),
    radial-gradient(1000px 560px at 90% -6%, color-mix(in srgb, var(--lav), transparent 83%), transparent 62%),
    radial-gradient(1300px 760px at 50% 118%, color-mix(in srgb, var(--pink), transparent 88%), transparent 64%),
    color-mix(in srgb, var(--ground), black 34%)}
.g-dv-stage.suspended *{animation-play-state:paused !important}

/* ---- ambience layer (decoration only, never catches a pointer) ----------- */
.g-dv-lab{position:absolute;inset:0;pointer-events:none;z-index:0}
.g-dv-lab *{pointer-events:none}
.g-dv-scanlines{position:absolute;inset:0;opacity:.055;
  background:repeating-linear-gradient(0deg, var(--ink) 0 1px, transparent 1px 3px)}
.g-dv-sweep{position:absolute;left:0;right:0;top:-2px;height:2px;opacity:.35;
  background:linear-gradient(90deg, transparent, color-mix(in srgb, var(--lav), transparent 20%), transparent);
  box-shadow:0 0 12px color-mix(in srgb, var(--lav), transparent 55%);
  animation:g-dv-osweep 9s linear infinite}
.g-dv-vig{position:absolute;inset:0;
  background:radial-gradient(120% 100% at 50% 44%, transparent 56%, rgba(0,0,0,.55) 100%)}
@keyframes g-dv-osweep{from{transform:translateY(0)}to{transform:translateY(100vh)}}

/* ---- HUD: a lab readout, not a chip tray --------------------------------- */
.g-dv-hud{position:relative;z-index:2;display:flex;flex-wrap:wrap;align-items:center;
  justify-content:center;gap:8px 16px;font-family:var(--mono);font-size:11px;
  letter-spacing:.14em;text-transform:uppercase;color:var(--ink-faint)}
.g-dv-hud .chip{background:color-mix(in srgb, var(--panel), transparent 45%);
  border-color:color-mix(in srgb, var(--lav), var(--line) 60%);
  color:color-mix(in srgb, var(--ink-dim), var(--lav) 30%);letter-spacing:.1em}
.g-dv-hud .g-dv-meterwrap{display:inline-flex;align-items:center;gap:6px}

/* ---- the bench: the board fills the frame -------------------------------- */
.g-dv-boardwrap{position:relative;z-index:1;flex:1;min-height:0;width:100%;
  display:flex;justify-content:center;align-items:center}
.g-dv-grid{position:relative;display:grid;gap:var(--g-dv-gap);perspective:1100px;justify-content:center;
  grid-template-columns:repeat(var(--g-dv-cols,4),1fr);
  /* height-fit: plates are 4/3 landscape, so width = (rowHeight * 4/3 * cols)
     + gaps. ~250px of chrome (proctor strip + hud + hint + pads) is reserved. */
  width:min(94%, calc(((100dvh - 250px - (var(--g-dv-rows,3) - 1) * var(--g-dv-gap))
    / var(--g-dv-rows,3)) * 1.3333 * var(--g-dv-cols,4)
    + (var(--g-dv-cols,4) - 1) * var(--g-dv-gap)))}
.g-dv-cell{position:relative;aspect-ratio:4/3}

/* ---- specimen slides ------------------------------------------------------ */
.g-dv-card{position:absolute;inset:0;display:flex;align-items:center;justify-content:center;
  padding:0;overflow:hidden;border-radius:10px;color:var(--slate);
  border:1px solid color-mix(in srgb, var(--lav), var(--line) 42%);
  background:
    linear-gradient(165deg, color-mix(in srgb, var(--panel2), var(--lav) 14%), var(--panel) 58%,
      color-mix(in srgb, var(--panel), black 12%));
  box-shadow:0 10px 26px rgba(0,0,0,.5), inset 0 1px 0 rgba(255,255,255,.07);
  transform-style:preserve-3d;transition:transform .22s ease,opacity .2s ease,
    box-shadow .2s ease,filter .25s ease;
  cursor:pointer;font:inherit;font-size:clamp(20px,3.2vmin,34px);line-height:1}
/* glass sheen over everything on the slide */
.g-dv-card::before{content:"";position:absolute;inset:0;z-index:2;pointer-events:none;
  background:linear-gradient(118deg, rgba(255,255,255,.09) 0%, transparent 26%,
    transparent 72%, rgba(255,255,255,.04) 100%)}
/* etched monogram back */
.g-dv-card::after{content:"\\25C8";position:relative;z-index:1;
  color:color-mix(in srgb, var(--lav), var(--panel) 55%);
  text-shadow:0 1px 0 rgba(0,0,0,.6), 0 0 14px color-mix(in srgb, var(--lav), transparent 78%)}
.g-dv-card.up::after,.g-dv-card.locked::after,.g-dv-card.ghost::after{content:none}
.g-dv-card:hover:not(:disabled){border-color:color-mix(in srgb, var(--lav), var(--line) 25%);
  box-shadow:0 12px 30px rgba(0,0,0,.55), 0 0 16px color-mix(in srgb, var(--lav), transparent 82%),
    inset 0 1px 0 rgba(255,255,255,.09)}
.g-dv-card:focus-visible{outline:2px solid var(--pink);outline-offset:2px}
.g-dv-card.dealt{animation:g-dv-toss .34s ease-out both}
/* face-up = a lightbox plate: lit from within */
.g-dv-card.up{border-color:var(--lav);color:var(--ink);
  background:linear-gradient(135deg, color-mix(in srgb, var(--lav), var(--panel2) 55%),
    color-mix(in srgb, var(--lav), var(--panel) 20%));
  box-shadow:0 10px 30px rgba(0,0,0,.55), 0 0 22px color-mix(in srgb, var(--lav), transparent 70%),
    inset 0 0 18px color-mix(in srgb, var(--lav), transparent 82%)}
.g-dv-card.flipping{transform:rotateY(62deg);border-color:var(--pink)}
.g-dv-card.judge{animation:g-dv-judge 1.2s ease-in-out infinite}
/* locked = archived: filed under glass, drained of heat */
.g-dv-card.locked{border-color:color-mix(in srgb, var(--lav), var(--line) 45%);
  background:linear-gradient(135deg, color-mix(in srgb, var(--panel2), black 10%),
    color-mix(in srgb, var(--panel), black 22%));
  filter:saturate(.5) brightness(.92);opacity:.92;cursor:default}
.g-dv-card.spot{box-shadow:0 0 0 2px var(--gold),0 0 22px color-mix(in srgb, var(--gold), transparent 60%)}
.g-dv-card.jiggle,.g-dv-grid.jiggle{animation:g-dv-jiggle .3s ease-in-out}
.g-dv-card.pulse{animation:g-dv-pulse .5s ease-out}

/* faces: one media node per card, opacity-toggled (never re-created on a flip) */
.g-dv-face{position:absolute;inset:0;width:100%;height:100%;object-fit:cover;
  opacity:0;transition:opacity .18s ease;pointer-events:none;background:var(--panel)}
.g-dv-face.g-dv-glyph{display:flex;align-items:center;justify-content:center;
  font-size:clamp(30px,7vmin,76px);line-height:1;
  color:color-mix(in srgb, var(--ground), var(--lav) 34%);
  text-shadow:0 0 20px color-mix(in srgb, var(--ground), transparent 45%);
  background:radial-gradient(80% 80% at 50% 42%,
    color-mix(in srgb, var(--lav), var(--panel) 68%),
    color-mix(in srgb, var(--panel), black 6%) 78%)}
.g-dv-card.up .g-dv-face,.g-dv-card.locked .g-dv-face{opacity:1}
.g-dv-card.ghost .g-dv-face{opacity:.2}

/* the filed tag on an archived pair (index.js's wax node, reskinned) */
.g-dv-wax{position:absolute;right:-6px;top:-6px;min-width:22px;height:22px;border-radius:6px;
  background:var(--pink-deep);color:var(--ground);font-size:11px;line-height:22px;text-align:center;
  box-shadow:0 0 10px color-mix(in srgb, var(--pink-deep), transparent 40%);z-index:3;
  transform:rotate(8deg)}

/* ---- the preview beam: the machine shows you ----------------------------- */
.g-dv-grid.scanning::after{content:"";position:absolute;left:-3%;right:-3%;top:0;height:3px;
  z-index:4;pointer-events:none;border-radius:2px;
  background:linear-gradient(90deg, transparent, var(--lav) 30%, var(--ink) 50%, var(--lav) 70%, transparent);
  box-shadow:0 0 18px color-mix(in srgb, var(--lav), transparent 40%),
    0 0 44px color-mix(in srgb, var(--lav), transparent 70%);
  animation:g-dv-beam 1.7s ease-in-out infinite}
@keyframes g-dv-beam{0%{top:-1%}50%{top:99%}100%{top:-1%}}

/* ---- the swap telegraph (always audible too: audio_trigger tick) ---------
   Same .tell class, same 600ms, same schedule - this is dressing only. */
.g-dv-card.tell{animation:g-dv-shudder .6s linear infinite}
.g-dv-card.tell::before{background:repeating-linear-gradient(0deg,
    rgba(255,255,255,.10) 0 1px, transparent 1px 3px, rgba(0,0,0,.16) 3px 4px);
  mix-blend-mode:screen;animation:g-dv-static .18s steps(3) infinite}
.g-dv-tellnote{position:absolute;left:50%;transform:translateX(-50%);bottom:-18px;white-space:nowrap;
  font-family:var(--mono);font-size:9px;letter-spacing:.14em;text-transform:uppercase;
  color:var(--gold);z-index:3}
.g-dv-line-tell{position:absolute;inset:0;border-radius:10px;pointer-events:none;
  background:linear-gradient(100deg,transparent 20%,color-mix(in srgb, var(--lav), transparent 72%) 50%,transparent 80%);
  animation:g-dv-sheen .52s linear}

/* ---- the flash well: engine one-shots anchored OVER the board -----------
   INPUT-TRUST LAW (DECISIONS #9). The board is a click-precision surface, so
   anything the engine draws into it is decoration: pointer-events:none here is
   inherited by every anchored child, and the game never passes an onPop over
   the grid. A tap always reaches the card underneath. */
.g-dv-flashwell{position:absolute;inset:0;overflow:hidden;pointer-events:none;z-index:6}
.g-dv-flashwell *{pointer-events:none}

/* ---- the specimen rack: matched pairs, filed at the frame's edge --------- */
.g-dv-rack{position:absolute;right:14px;top:50%;transform:translateY(-50%);z-index:2;
  display:flex;flex-direction:column;align-items:center;gap:7px;pointer-events:none}
.g-dv-rack *{pointer-events:none}
.g-dv-rack-label{font-family:var(--mono);font-size:8.5px;letter-spacing:.3em;
  text-transform:uppercase;color:var(--ink-faint);writing-mode:vertical-rl;margin-bottom:4px;
  opacity:.75}
.g-dv-slide{display:flex;align-items:center;justify-content:center;width:26px;height:34px;
  border-radius:5px;font-size:13px;line-height:1;color:color-mix(in srgb, var(--lav), var(--ink) 35%);
  border:1px solid color-mix(in srgb, var(--lav), var(--line) 45%);
  background:linear-gradient(160deg, color-mix(in srgb, var(--panel2), var(--lav) 8%), var(--panel));
  box-shadow:0 4px 12px rgba(0,0,0,.5), 0 0 10px color-mix(in srgb, var(--pink), transparent 82%);
  animation:g-dv-file .4s cubic-bezier(.2,1.4,.4,1) both}
@keyframes g-dv-file{from{transform:translateX(26px) rotate(9deg);opacity:0}
  to{transform:none;opacity:1}}

/* ---- hint + cram ---------------------------------------------------------- */
.g-dv-hint{position:relative;z-index:2;margin:0;text-align:center;font-family:var(--mono);
  font-size:11px;letter-spacing:.16em;text-transform:uppercase;color:var(--ink-faint);
  min-height:1.3em}
.g-dv-hint.warm{color:var(--gold);text-shadow:0 0 12px color-mix(in srgb, var(--gold), transparent 60%)}
.g-dv-cram{position:relative;z-index:2;display:flex;align-items:center;gap:10px}
.g-dv-cram .arc-peekbtn.armed{border-color:var(--gold);color:var(--gold);
  box-shadow:0 0 14px rgba(240,194,75,.3)}
.g-dv-retake{border-color:var(--lav);color:var(--lav)}

/* ---- animations --------------------------------------------------------- */
@keyframes g-dv-toss{from{transform:translateY(-14px) rotate(-4deg);opacity:0}to{transform:none;opacity:1}}
@keyframes g-dv-judge{50%{transform:scale(1.05);
  box-shadow:0 0 20px color-mix(in srgb, var(--lav), transparent 50%)}}
@keyframes g-dv-pulse{0%{transform:scale(1)}45%{transform:scale(1.06);
  box-shadow:0 0 22px color-mix(in srgb, var(--pink), transparent 55%)}100%{transform:scale(1)}}
@keyframes g-dv-jiggle{25%{transform:translateX(-3px)}75%{transform:translateX(3px)}}
@keyframes g-dv-sheen{from{transform:translateX(-60%)}to{transform:translateX(60%)}}
@keyframes g-dv-static{from{opacity:.9}to{opacity:.5}}
@keyframes g-dv-shudder{
  0%,100%{transform:none;box-shadow:none}
  20%{transform:translateX(-2px);box-shadow:-3px 0 0 rgba(255,105,180,.35),3px 0 0 rgba(110,232,224,.3)}
  45%{transform:translateX(2px)}
  70%{transform:translateX(-1px);box-shadow:3px 0 0 rgba(255,105,180,.35),-3px 0 0 rgba(110,232,224,.3)}
}

/* ---- reduced motion: the mechanic survives, the motion does not --------- */
html.arc-reduced .g-dv-card{transition:opacity .16s ease}
html.arc-reduced .g-dv-card.flipping{transform:none;opacity:.45}
html.arc-reduced .g-dv-card.dealt,
html.arc-reduced .g-dv-card.judge,
html.arc-reduced .g-dv-card.pulse,
html.arc-reduced .g-dv-card.jiggle,
html.arc-reduced .g-dv-grid.jiggle{animation:none}
html.arc-reduced .g-dv-card.tell{animation:none;border-color:var(--gold);
  box-shadow:0 0 0 2px rgba(240,194,75,.55)}
html.arc-reduced .g-dv-card.tell::before{animation:none;background:none}
html.arc-reduced .g-dv-line-tell{animation:none;opacity:.5}
html.arc-reduced .g-dv-sweep,
html.arc-reduced .g-dv-grid.scanning::after{animation:none;opacity:0}
html.arc-reduced .g-dv-slide{animation:none}
@media (prefers-reduced-motion: reduce){
  .g-dv-card{transition:opacity .16s ease}
  .g-dv-card.flipping{transform:none;opacity:.45}
  .g-dv-card.dealt,.g-dv-card.judge,.g-dv-card.pulse,.g-dv-card.jiggle{animation:none}
  .g-dv-card.tell{animation:none;border-color:var(--gold);box-shadow:0 0 0 2px rgba(240,194,75,.55)}
  .g-dv-card.tell::before{animation:none;background:none}
  .g-dv-line-tell{animation:none;opacity:.5}
  .g-dv-sweep,.g-dv-grid.scanning::after{animation:none;opacity:0}
  .g-dv-slide{animation:none}
}

/* ---- narrow / touch: >=64px targets, the rack folds away ----------------- */
@media (max-width:560px){
  .g-dv-stage{padding:64px 10px 10px}
  .g-dv-grid{gap:8px;--g-dv-gap:8px;width:100%;
    grid-template-columns:repeat(var(--g-dv-cols,4),minmax(64px,1fr))}
  .g-dv-cell{min-height:64px}
  .g-dv-rack{display:none}
}
`;

/** Inject once per document. No-op headless (the DOM double has no head). */
export function injectDejaVuStyle() {
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

export default injectDejaVuStyle;
