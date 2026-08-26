/* ============================================================================
 * games/daily-trigger/styles.js - this class's stylesheet, injected from JS.
 *
 * "NIGHT HOMEROOM" (game-immersion wave, after the campus hub landed): the class
 * root is a FULL-VIEWPORT stage, so this game paints the whole room - chalkboard
 * wall, desk row, lamp pools, dust motes, the door-sign neon bleeding in from the
 * left. No boxes: the guess grid is a wall-scale letterboard on a chalkboard
 * slab, the keyboard is a desk surface anchored to the bottom edge, and the HUD
 * chips hang on the wall as a corner stack. The shell overlays a slim proctor
 * strip across the top (~56px), so nothing interactive lives in the top band.
 *
 * `styles.css` is the SHELL's chrome only, so everything here ships namespaced
 * `.g-dt-*` (game: daily-trigger). Injected once per document, lazily, and never
 * in a headless import.
 *
 * Every colour is a shell token (`var(--pink)`, `var(--panel2)`, ...) with a
 * hardcoded fallback, so a mod palette (init.palette -> :root custom properties)
 * reskins this class for free and a missing token can never render invisible
 * text. Ambience layers are pointer-events:none and aria-hidden - decoration may
 * occlude nothing and intercept nothing (input-trust law).
 *
 * Reduced motion is honoured TWICE - the shell stamps `.arc-reduced` on <html>
 * and the media query below neutralises the animations anyway.
 * ==========================================================================*/

const STYLE_ID = 'g-dt-style';

export const STYLE_TEXT = `
.g-dt{--dt-hit:var(--pink,#FF69B4);--dt-near:var(--lav,#B8A6E8);--dt-miss:var(--line,#3A3A5E);
  --dt-cell:clamp(34px,min(7vh,5.2vw),68px);
  position:absolute;inset:0;display:flex;flex-direction:column;align-items:center;
  padding:64px 0 0;color:var(--ink,#F2EBDD);overflow:hidden;
  /* the room: wall falling to floor, lit from two lamps and the door neon */
  background:
    radial-gradient(900px 420px at 18% 8%, rgba(240,194,75,.10), transparent 62%),
    radial-gradient(760px 380px at 84% 12%, rgba(240,194,75,.07), transparent 60%),
    radial-gradient(1100px 620px at 50% 108%, rgba(46,46,85,.55), transparent 70%),
    linear-gradient(180deg,
      color-mix(in srgb, var(--ground,#14142B), black 42%) 0%,
      color-mix(in srgb, var(--ground,#14142B), black 20%) 56%,
      color-mix(in srgb, var(--navy,#1A1A2E), black 6%) 100%)}

/* ---- ambience (decoration: never a hitbox, never a mark) ----------------- */
.g-dt-room{position:absolute;inset:0;pointer-events:none;z-index:1}
/* TONIGHT ONLY (House Rules): casino.js re-tints the room nightly via the
   --dt-n-* props, seeded from the UTC date alone - the same room for every
   player on earth, expiring at midnight. The fallbacks are the classic room. */
.g-dt-neonbleed{position:absolute;left:0;top:6%;width:32%;height:78%;
  background:radial-gradient(closest-side at 0% 42%, var(--dt-n-neon, rgba(255,105,180,.28)), transparent 74%);
  filter:blur(30px);animation:g-dt-neonflicker 7s steps(1) infinite}
.g-dt-lamp{position:absolute;width:38vw;height:34vh;border-radius:50%;filter:blur(46px);
  background:radial-gradient(closest-side, var(--dt-n-lamp, rgba(240,194,75,.22)), transparent 70%);
  animation:g-dt-lamppulse 5s ease-in-out infinite}
.g-dt-lamp.b{animation-delay:2.4s}
.g-dt-floor{position:absolute;left:0;right:0;bottom:0;height:30%;
  background:linear-gradient(180deg, transparent,
    color-mix(in srgb, var(--navy,#1A1A2E), var(--lav,#B8A6E8) 4%) 88%);opacity:.65}
.g-dt-mote{position:absolute;width:3px;height:3px;border-radius:50%;
  background:rgba(242,235,221,.5);box-shadow:0 0 6px 1px rgba(242,235,221,.25);
  left:var(--x);top:var(--y);opacity:0;
  animation:g-dt-mote var(--t) ease-in-out infinite;animation-delay:var(--dl)}
.g-dt-vignette{position:absolute;inset:0;
  background:radial-gradient(130% 105% at 50% 44%, transparent 55%, rgba(0,0,0,.6) 100%)}

/* ---- HUD: chips hang on the wall, top-left, under the proctor strip ------ */
.g-dt-hud{position:absolute;left:18px;top:72px;z-index:5;
  display:flex;flex-direction:column;gap:8px;align-items:flex-start}
.g-dt-hud .chip{background:color-mix(in srgb, var(--navy,#1A1A2E), transparent 20%);
  backdrop-filter:blur(3px)}
.g-dt-rung{display:inline-flex;gap:4px;align-items:center;padding:4px 2px}
.g-dt-rung i{width:16px;height:8px;border-radius:2px;background:var(--line,#3A3A5E);
  transition:background .3s ease,box-shadow .3s ease}
.g-dt-rung i.on{background:var(--pink,#FF69B4);box-shadow:0 0 8px rgba(255,105,180,.65)}

/* ---- the lesson wall: chalkboard slab holding the letterboard ------------ */
.g-dt-stagezone{flex:1 1 auto;display:flex;flex-direction:column;align-items:center;
  justify-content:center;gap:10px;position:relative;z-index:4;width:100%;min-height:0}
.g-dt-lesson{font-family:var(--mono,monospace);font-size:12px;letter-spacing:.46em;
  text-indent:.46em;color:color-mix(in srgb, var(--ink-faint,#8A84A8), var(--gold,#F0C24B) 30%);
  text-transform:uppercase;position:relative;z-index:2}
/* the lesson wall is WALL-scale: one wide chalkboard, ruled like a real one,
   with chalk-doodle columns filling the flanks so the width reads as designed */
.g-dt-slab{position:relative;width:min(90vw,1120px);min-height:min(56vh,560px);
  display:flex;flex-direction:column;align-items:center;justify-content:center;
  gap:clamp(10px,1.8vh,20px);
  padding:clamp(18px,3vh,34px) clamp(22px,3.4vw,44px);
  border-radius:6px;
  background:
    repeating-linear-gradient(180deg, transparent 0 44px, rgba(184,166,232,.045) 44px 45px),
    radial-gradient(120% 90% at 50% 0%, rgba(242,235,221,.035), transparent 60%),
    linear-gradient(168deg,
      color-mix(in srgb, var(--navy,#1A1A2E), black 30%),
      color-mix(in srgb, var(--ground,#14142B), black 34%));
  border:2px solid color-mix(in srgb, var(--panel2,#2E2E55), var(--gold,#F0C24B) 12%);
  box-shadow:inset 0 0 70px rgba(0,0,0,.55), 0 24px 70px rgba(0,0,0,.55);
}
/* faint chalk work on the flanks (pure graphic - never text) */
.g-dt-doodle{position:absolute;top:16%;bottom:14%;width:clamp(60px,9vw,150px);
  opacity:.55;pointer-events:none;
  background:
    repeating-linear-gradient(0deg, transparent 0 30px, rgba(242,235,221,.07) 30px 32px),
    repeating-linear-gradient(90deg, transparent 0 22px, rgba(242,235,221,.04) 22px 23px)}
.g-dt-doodle.l{left:3.5%;transform:rotate(-1deg)}
.g-dt-doodle.r{right:3.5%;transform:rotate(1.2deg)}
/* chalk tray under the slab */
.g-dt-slab::after{content:"";position:absolute;left:8%;right:8%;bottom:-10px;height:8px;
  border-radius:3px;background:color-mix(in srgb, var(--panel2,#2E2E55), black 18%);
  box-shadow:0 4px 10px rgba(0,0,0,.5)}
/* a stub of chalk on the tray */
.g-dt-slab::before{content:"";position:absolute;right:14%;bottom:-7px;width:26px;height:4px;
  border-radius:2px;background:rgba(242,235,221,.65);transform:rotate(-2deg)}

/* ---- board -------------------------------------------------------------- */
.g-dt-board{display:grid;gap:clamp(5px,.9vh,9px);position:relative;z-index:4}
.g-dt-row{display:flex;gap:clamp(5px,.8vw,9px);justify-content:center;position:relative}
.g-dt-row.shake{animation:g-dt-shake .34s ease}
/* the solved row earns a chalk underline sweep */
.g-dt-row.solved::after{content:"";position:absolute;left:2%;right:2%;bottom:-7px;height:3px;
  border-radius:2px;background:rgba(242,235,221,.85);
  box-shadow:0 0 8px rgba(242,235,221,.5);transform-origin:left center;
  animation:g-dt-chalkline .5s ease-out both}
.g-dt-gap{width:clamp(10px,1.4vw,20px);flex:0 0 auto}
.g-dt-cell{width:var(--dt-cell);height:var(--dt-cell);border:2px solid var(--line,#3A3A5E);
  border-radius:6px;display:flex;align-items:center;justify-content:center;position:relative;
  font-family:var(--disp,serif);font-size:calc(var(--dt-cell) * .46);
  background:color-mix(in srgb, var(--navy,#1A1A2E), black 12%);
  color:var(--ink,#F2EBDD);text-transform:uppercase;user-select:none;
  box-shadow:inset 0 2px 6px rgba(0,0,0,.35);
  transition:transform .18s ease,background .25s ease,border-color .25s ease}
.g-dt-cell.typed{border-color:var(--lav,#B8A6E8);color:var(--lav,#B8A6E8)}
.g-dt-cell.active{border-color:var(--pink,#FF69B4);animation:g-dt-caret 1.1s steps(2) infinite}
.g-dt-cell.hinted{border-color:var(--gold,#F0C24B);color:var(--gold,#F0C24B)}
.g-dt-cell.gold{box-shadow:0 0 0 2px rgba(240,194,75,.55),0 0 14px rgba(240,194,75,.35)}
.g-dt-cell.flip{animation:g-dt-flip .32s ease both}
.g-dt-cell.hit{background:var(--dt-hit);border-color:var(--dt-hit);color:var(--ground,#14142B);
  box-shadow:0 0 16px rgba(255,105,180,.35)}
.g-dt-cell.near{background:var(--dt-near);border-color:var(--dt-near);color:var(--ground,#14142B)}
.g-dt-cell.miss{background:var(--dt-miss);border-color:var(--dt-miss);color:var(--ink-faint,#8A84A8)}
/* double-coded: colour PLUS a stamped glyph, always on, never a setting */
.g-dt-cell.hit::after,.g-dt-cell.near::after,.g-dt-cell.miss::after{
  position:absolute;right:2px;bottom:0;font-size:10px;font-family:var(--body,sans-serif);opacity:.85}
.g-dt-cell.hit::after{content:"\\2605"}
.g-dt-cell.near::after{content:"\\25D0"}
.g-dt-cell.miss::after{content:"\\2715";color:var(--ink-faint,#8A84A8)}
.g-dt-cell.wobble{animation:g-dt-wobble .6s ease}

/* ---- CASINO (House Rules, Deck II): the marquee around the slab ---------- */
/* Bulb-chase frame on the chalkboard's own edge. Dots are gradients, the
   chase is a background-position crawl - four thin bars, cheap to paint.
   pointer-events:none is LAW; pace (--g-dt-mqt) and presence (--g-dt-mqa)
   ride the ladder's heat from casino.js; gold outbids heat for the absorb. */
.g-dt-mq{position:absolute;inset:-3px;z-index:1;pointer-events:none;
  opacity:var(--g-dt-mqa,.25);transition:opacity .6s ease}
.g-dt-mq i{position:absolute;display:block;
  background-image:radial-gradient(circle, var(--dt-n-mq,var(--pink,#FF69B4)) 2px, transparent 3px)}
.g-dt-mq .mq-t,.g-dt-mq .mq-b{left:0;right:0;height:7px;
  background-size:18px 7px;background-repeat:repeat-x}
.g-dt-mq .mq-l,.g-dt-mq .mq-r{top:0;bottom:0;width:7px;
  background-size:7px 18px;background-repeat:repeat-y}
.g-dt-mq .mq-t{top:0;animation:g-dt-mqx var(--g-dt-mqt,2s) linear infinite var(--g-dt-mqp,0s)}
.g-dt-mq .mq-r{right:0;animation:g-dt-mqy var(--g-dt-mqt,2s) linear infinite var(--g-dt-mqp,0s)}
.g-dt-mq .mq-b{bottom:0;animation:g-dt-mqxr var(--g-dt-mqt,2s) linear infinite var(--g-dt-mqp,0s)}
.g-dt-mq .mq-l{left:0;animation:g-dt-mqyr var(--g-dt-mqt,2s) linear infinite var(--g-dt-mqp,0s)}
@keyframes g-dt-mqx{to{background-position-x:18px}}
@keyframes g-dt-mqxr{to{background-position-x:-18px}}
@keyframes g-dt-mqy{to{background-position-y:18px}}
@keyframes g-dt-mqyr{to{background-position-y:-18px}}
.g-dt-mq.g-dt-mq-gold{--dt-n-mq:var(--gold,#F0C24B);
  filter:drop-shadow(0 0 6px rgba(240,194,75,.75))}
.g-dt-mq.g-dt-mq-flash{animation:g-dt-mqflash .6s ease-out 1}
@keyframes g-dt-mqflash{
  0%{opacity:1;filter:brightness(var(--g-dt-mqf,1.4)) drop-shadow(0 0 10px rgba(255,105,180,.9))}
  100%{opacity:var(--g-dt-mqa,.25);filter:none}}
.g-dt-mq.g-dt-mq-out{opacity:0;transition:opacity 1.4s ease}
html.arc-reduced .g-dt-mq{opacity:.16}

/* THE ALMOST: on a one-letter-away row, the solved underline starts to draw,
   stops at ~62%, and evaporates. The reel stopped one short. Row theatre only
   - a cell mark is never repainted by an effect. */
.g-dt-almostline{position:absolute;left:2%;right:2%;bottom:-7px;height:3px;
  border-radius:2px;pointer-events:none;background:rgba(242,235,221,.85);
  box-shadow:0 0 8px rgba(242,235,221,.5);transform-origin:left center;
  animation:g-dt-almost 1.1s ease-out both}
@keyframes g-dt-almost{0%{transform:scaleX(0);opacity:1}
  55%{transform:scaleX(.62);opacity:.9}75%{transform:scaleX(.62);opacity:.9}
  100%{transform:scaleX(.62);opacity:0}}

/* ---- TRICKSTER (Deck III) dressing --------------------------------------- */
/* STAT FLICKER: the chip wearing a lie gets one beat of chromatic static;
   index repaints the truth on a deadline. */
.chip.g-dt-statlie{color:var(--lav,#B8A6E8);
  text-shadow:-1.5px 0 var(--pink,#FF69B4),1.5px 0 #6EE8E0;
  animation:g-dt-statlie .45s steps(3) 1}
@keyframes g-dt-statlie{0%{opacity:1}50%{opacity:.55}100%{opacity:1}}
/* CHALK WHISPER: a ghost hand writes a lie under the message line. The text
   lies; the glyphs on the board are the truth (Unreliable Label trains you
   to stop reading). Fainter and slanted harder than the proctor's chalk. */
.g-dt-whisper{min-height:16px;margin:0;font-size:12px;font-style:italic;
  letter-spacing:.09em;text-align:center;position:relative;z-index:5;
  color:color-mix(in srgb, var(--lav,#B8A6E8), transparent 30%);
  text-shadow:0 0 12px rgba(0,0,0,.6);transform:rotate(-.6deg);
  animation:g-dt-whisperin 3.4s ease-in-out both}
.g-dt-whisper.plain{animation:none;opacity:.6}
@keyframes g-dt-whisperin{0%{opacity:0;filter:blur(3px)}
  18%{opacity:.85;filter:blur(0)}78%{opacity:.85}100%{opacity:0;filter:blur(2px)}}

/* ---- message line: chalk handwriting under the slab ----------------------- */
.g-dt-msg{min-height:20px;font-size:13px;font-style:italic;letter-spacing:.05em;
  color:var(--ink-dim,#B9B3CE);text-align:center;position:relative;z-index:5;
  text-shadow:0 0 10px rgba(0,0,0,.6);margin:2px 0 6px}
.g-dt-msg.warn{color:var(--gold,#F0C24B)}

/* ---- keyboard: the desk row, anchored full-width to the bottom edge ------- */
.g-dt-kb{display:flex;flex-direction:column;gap:6px;align-items:center;position:relative;
  z-index:4;width:100%;padding:16px 12px calc(14px + env(safe-area-inset-bottom,0px));
  background:
    linear-gradient(180deg,
      color-mix(in srgb, var(--panel,#252542), black 8%) 0%,
      color-mix(in srgb, var(--navy,#1A1A2E), black 16%) 100%);
  border-top:1px solid color-mix(in srgb, var(--line,#3A3A5E), var(--lav,#B8A6E8) 18%);
  box-shadow:0 -18px 50px rgba(0,0,0,.45)}
/* desk edge highlight */
.g-dt-kb::before{content:"";position:absolute;left:0;right:0;top:0;height:1px;
  background:linear-gradient(90deg, transparent, rgba(184,166,232,.35), transparent)}
.g-dt-krow{display:flex;gap:clamp(5px,.6vw,8px)}
.g-dt-key{min-width:clamp(36px,3.8vw,60px);height:clamp(42px,6vh,60px);padding:0 10px;border-radius:7px;
  background:var(--panel2,#2E2E55);border:1px solid #3E3E70;color:var(--ink-dim,#B9B3CE);
  font:600 clamp(13px,1.9vh,17px)/1 var(--body,sans-serif);
  display:inline-flex;align-items:center;justify-content:center;cursor:pointer;
  /* a keycap is TAPPED, fast: no double-tap zoom, no 300ms wait (styles.css's
     html/body floor says the same, this is the belt to that pair of braces) */
  touch-action:manipulation;-webkit-tap-highlight-color:transparent;
  box-shadow:0 3px 0 rgba(0,0,0,.4);position:relative;overflow:hidden;
  transition:transform .08s ease,background .25s ease,color .25s ease}
.g-dt-key:active,.g-dt-key.down{transform:translateY(2px);box-shadow:none}
.g-dt-key:focus-visible{outline:2px solid var(--pink,#FF69B4);outline-offset:2px}
.g-dt-key.hit{background:var(--dt-hit);color:var(--ground,#14142B);border-color:var(--dt-hit)}
.g-dt-key.near{background:var(--dt-near);color:var(--ground,#14142B);border-color:var(--dt-near)}
.g-dt-key.miss{background:#20203E;color:#5A5578;border-color:#2A2A4E}
.g-dt-key.wide{min-width:clamp(52px,5.4vw,84px);font-size:11px;letter-spacing:.06em}
.g-dt-key.glitched{color:var(--lav,#B8A6E8);text-shadow:-2px 0 var(--pink,#FF69B4),2px 0 #6EE8E0}
.g-dt-commit{width:100%;max-width:420px;margin-top:2px}

/* ---- ceremony overlay (absorb + detention): now a full-room moment -------- */
.g-dt-cer{position:absolute;inset:0;z-index:12;display:flex;flex-direction:column;
  align-items:center;justify-content:center;gap:16px;text-align:center;cursor:pointer;
  background:radial-gradient(circle at 50% 45%,rgba(20,20,43,.72),rgba(20,20,43,.94) 70%)}
.g-dt-cer .g-dt-word{font-family:var(--disp,serif);
  font-size:clamp(40px,7.5vw,92px);letter-spacing:.3em;padding-left:.3em;
  color:var(--pink,#FF69B4);
  text-shadow:0 0 28px rgba(255,105,180,.8),0 0 90px rgba(255,105,180,.4);
  animation:g-dt-pulse 1.5s ease-in-out infinite}
.g-dt-cer.bad .g-dt-word{color:var(--gold,#F0C24B);text-shadow:0 0 24px rgba(240,194,75,.65);
  animation:g-dt-invert 1.1s steps(2) infinite}
.g-dt-cer .g-dt-line{font-size:15px;color:var(--ink-dim,#B9B3CE);max-width:44ch}
.g-dt-cer .g-dt-skip{font-size:11px;color:var(--ink-faint,#8A84A8);letter-spacing:.08em}
.g-dt-stamp{font-family:var(--disp,serif);font-size:14px;letter-spacing:.18em;
  color:var(--gold,#F0C24B);border:2px solid var(--gold,#F0C24B);border-radius:8px;
  padding:6px 14px;transform:rotate(-6deg);box-shadow:0 0 14px rgba(240,194,75,.35);
  animation:g-dt-stamppop .45s cubic-bezier(.2,1.5,.4,1) both}

/* ---- animations -------------------------------------------------------- */
@keyframes g-dt-caret{50%{border-color:var(--line,#3A3A5E)}}
@keyframes g-dt-flip{0%{transform:rotateX(0)}50%{transform:rotateX(-88deg) scale(1.04)}
  100%{transform:rotateX(0)}}
@keyframes g-dt-shake{0%,100%{transform:translateX(0)}20%{transform:translateX(-6px)}
  50%{transform:translateX(5px)}80%{transform:translateX(-3px)}}
@keyframes g-dt-wobble{0%,100%{transform:rotate(0)}30%{transform:rotate(-7deg) scale(1.06)}
  65%{transform:rotate(6deg) scale(1.04)}}
@keyframes g-dt-pulse{50%{transform:scale(1.06);
  text-shadow:0 0 40px rgba(255,105,180,1),0 0 110px rgba(255,105,180,.5)}}
@keyframes g-dt-invert{50%{opacity:.45;letter-spacing:.38em}}
@keyframes g-dt-chalkline{from{transform:scaleX(0)}to{transform:scaleX(1)}}
@keyframes g-dt-stamppop{0%{transform:rotate(-6deg) scale(2.2);opacity:0}
  60%{transform:rotate(-6deg) scale(.94);opacity:1}100%{transform:rotate(-6deg) scale(1)}}
@keyframes g-dt-lamppulse{0%,100%{opacity:.75}50%{opacity:1}}
@keyframes g-dt-neonflicker{0%,100%{opacity:1}6%{opacity:.55}7%{opacity:1}
  47%{opacity:.8}49%{opacity:1}81%{opacity:.6}82%{opacity:1}}
@keyframes g-dt-mote{0%{opacity:0;transform:translate(0,0)}15%{opacity:.7}
  55%{opacity:.35;transform:translate(18px,-30px)}85%{opacity:.6}
  100%{opacity:0;transform:translate(-10px,-56px)}}

@media (prefers-reduced-motion: reduce){
  .g-dt-cell,.g-dt-key,.g-dt-cer .g-dt-word,.g-dt-row.shake,.g-dt-mote,.g-dt-lamp,
  .g-dt-neonbleed,.g-dt-row.solved::after,.g-dt-stamp,.g-dt-mq,.g-dt-mq i,
  .g-dt-almostline,.g-dt-whisper,.chip.g-dt-statlie{animation:none !important;
    transition:none !important}
  .g-dt-row.solved::after{transform:scaleX(1)}
  .g-dt-almostline{display:none}
}
.arc-reduced .g-dt-cell,.arc-reduced .g-dt-key,.arc-reduced .g-dt-cer .g-dt-word,
.arc-reduced .g-dt-row.shake,.arc-reduced .g-dt-mote,.arc-reduced .g-dt-lamp,
.arc-reduced .g-dt-neonbleed,.arc-reduced .g-dt-row.solved::after,
.arc-reduced .g-dt-stamp,.arc-reduced .g-dt-mq,.arc-reduced .g-dt-mq i,
.arc-reduced .g-dt-almostline,.arc-reduced .g-dt-whisper,
.arc-reduced .chip.g-dt-statlie{animation:none !important;transition:none !important}
.arc-reduced .g-dt-row.solved::after{transform:scaleX(1)}
.arc-reduced .g-dt-almostline{display:none}

/* ---- THE CLASS RULES SHEET (Deck VI, Law IV: drawn, not told) ------------ */
/* Three vignettes on a chalk-dusted card over the lesson wall. The sheet takes
   NO pointer events - the keyboard under it is not bound yet and a stray click
   must never count as "read" - and the GO button takes its own back. It sits at
   z 9: above the room (1), the stage zone (4) and the HUD (5), and far below the
   shell's suspend treatment (35), which must always be able to cover it. */
.g-dt-howto{position:absolute;left:50%;top:50%;transform:translate(-50%,-50%);z-index:9;
  width:min(520px,92vw);max-height:86vh;overflow:auto;pointer-events:none;
  display:flex;flex-direction:column;gap:2px;padding:20px 22px 18px;border-radius:14px;
  color:var(--ink,#F2EBDD);
  background:linear-gradient(180deg,
    color-mix(in srgb, var(--navy,#1A1A2E), transparent 4%),
    color-mix(in srgb, var(--ground,#14142B), transparent 2%));
  border:1px solid var(--line,#3A3A5E);
  box-shadow:0 28px 74px rgba(0,0,0,.58), 0 0 40px rgba(255,105,180,.10);
  animation:g-dt-hw-in .34s ease-out 1}
@keyframes g-dt-hw-in{from{opacity:0;transform:translate(-50%,-46%)}
  to{opacity:1;transform:translate(-50%,-50%)}}
.g-dt-hw-title{margin:0 0 8px;text-align:center;font-family:var(--disp,serif);
  font-size:clamp(15px,2.3vmin,19px);letter-spacing:.22em;text-transform:uppercase;
  color:var(--pink,#FF69B4);text-shadow:0 0 20px rgba(255,105,180,.45)}
.g-dt-hw-row{display:flex;align-items:center;gap:16px;padding:11px 2px}
.g-dt-hw-row + .g-dt-hw-row{border-top:1px dashed rgba(58,58,94,.6)}
.g-dt-hw-fig{flex:0 0 auto;display:flex;align-items:center;gap:10px;pointer-events:none}
.g-dt-hw-cap{margin:0;flex:1 1 auto;font-size:12.5px;line-height:1.5;color:var(--ink-dim,#B9B3CE)}

/* the two mini letterboards: same tokens, same glyph badges as the real cells */
.g-dt-hw-line{display:flex;gap:4px}
.g-dt-hw-cell{position:relative;width:26px;height:26px;border-radius:4px;flex:0 0 auto;
  display:flex;align-items:center;justify-content:center;
  font-family:var(--disp,serif);font-size:13px;line-height:1;
  border:2px solid var(--line,#3A3A5E);color:var(--ink-faint,#8A84A8);
  background:rgba(20,20,43,.5)}
.g-dt-hw-cell.typed{border-color:var(--lav,#B8A6E8);color:var(--lav,#B8A6E8);
  opacity:0;animation:g-dt-hw-type 3.4s ease-out infinite;
  animation-delay:calc(var(--dt-hw-i,0) * .22s)}
@keyframes g-dt-hw-type{0%{opacity:0;transform:translateY(-4px)}
  9%,86%{opacity:1;transform:translateY(0)}100%{opacity:0;transform:translateY(0)}}
.g-dt-hw-cell.caret{border-color:var(--pink,#FF69B4);
  animation:g-dt-hw-caret 1.1s steps(2) infinite}
@keyframes g-dt-hw-caret{0%,49%{box-shadow:inset 0 -3px 0 var(--pink,#FF69B4)}
  50%,100%{box-shadow:none}}
.g-dt-hw-cell.hit{background:var(--dt-hit,#FF69B4);border-color:var(--dt-hit,#FF69B4);
  color:var(--ground,#14142B)}
.g-dt-hw-cell.near{background:var(--dt-near,#B8A6E8);border-color:var(--dt-near,#B8A6E8);
  color:var(--ground,#14142B)}
.g-dt-hw-cell.miss{background:var(--dt-miss,#3A3A5E);border-color:var(--dt-miss,#3A3A5E);
  color:var(--ink-faint,#8A84A8)}
.g-dt-hw-cell.hit::after,.g-dt-hw-cell.near::after,.g-dt-hw-cell.miss::after{
  position:absolute;right:2px;bottom:0;font-size:8px;line-height:1;opacity:.85}
.g-dt-hw-cell.hit::after{content:"\\2605"}
.g-dt-hw-cell.near::after{content:"\\25D0"}
.g-dt-hw-cell.miss::after{content:"\\2715";color:var(--ink-faint,#8A84A8)}
.g-dt-hw-cell.flip{animation:g-dt-hw-flip 3.4s ease-in-out infinite;
  animation-delay:calc(var(--dt-hw-i,0) * .3s)}
@keyframes g-dt-hw-flip{0%,6%{transform:rotateX(90deg)}16%,88%{transform:rotateX(0)}
  100%{transform:rotateX(90deg)}}

/* the keycap that commits the row */
.g-dt-hw-key{flex:0 0 auto;min-width:52px;text-align:center;padding:7px 10px;border-radius:7px;
  font-family:var(--mono,monospace);font-size:10px;letter-spacing:.12em;text-transform:uppercase;
  color:var(--ink,#F2EBDD);background:linear-gradient(180deg,var(--panel2,#2E2E55),#1A1A32);
  border:1px solid var(--line,#3A3A5E);border-bottom-width:3px;touch-action:manipulation;
  box-shadow:0 2px 0 rgba(0,0,0,.45);animation:g-dt-hw-press 3.4s ease-in-out infinite}
.g-dt-hw-key.wide{min-width:74px}
@keyframes g-dt-hw-press{0%,84%{transform:translateY(0)}
  90%{transform:translateY(2px);box-shadow:0 0 0 rgba(0,0,0,.45)}100%{transform:translateY(0)}}

/* the budget: six slabs, two spent, and the rung strip climbing beside them */
.g-dt-hw-stack{display:flex;flex-direction:column;gap:3px}
.g-dt-hw-slab{display:block;width:46px;height:7px;border-radius:2px;
  border:1px solid rgba(58,58,94,.9);background:rgba(20,20,43,.55)}
.g-dt-hw-slab.spent{background:var(--dt-miss,#3A3A5E);border-color:var(--dt-miss,#3A3A5E)}
.g-dt-hw-slab.next{border-color:var(--pink,#FF69B4);
  animation:g-dt-hw-nextrow 1.8s ease-in-out infinite}
@keyframes g-dt-hw-nextrow{0%,100%{box-shadow:0 0 0 rgba(255,105,180,0)}
  50%{box-shadow:0 0 10px rgba(255,105,180,.55)}}
.g-dt-hw-rungs{display:flex;flex-direction:column-reverse;gap:3px}
.g-dt-hw-rungs i{display:block;width:16px;height:6px;border-radius:2px;
  background:var(--line,#3A3A5E)}
.g-dt-hw-rungs i.on{background:var(--pink,#FF69B4);box-shadow:0 0 8px rgba(255,105,180,.65);
  animation:g-dt-hw-climb 3.4s ease-in-out infinite;
  animation-delay:calc(var(--dt-hw-i,0) * .5s)}
@keyframes g-dt-hw-climb{0%,20%{opacity:.25}34%,88%{opacity:1}100%{opacity:.25}}

/* the ONE live thing on the sheet, and the only way past it */
.g-dt-hw-go{align-self:center;margin-top:8px;padding:9px 30px;cursor:pointer;
  pointer-events:auto;border-radius:9px;font-family:var(--disp,serif);font-size:13px;
  letter-spacing:.16em;text-transform:uppercase;color:var(--ground,#14142B);
  background:linear-gradient(180deg,var(--pink,#FF69B4),var(--pink-deep,#D4488F));
  border:1px solid var(--pink,#FF69B4);box-shadow:0 0 22px rgba(255,105,180,.4)}
.g-dt-hw-go:hover{box-shadow:0 0 32px rgba(255,105,180,.6)}
.g-dt-hw-go:focus-visible{outline:2px solid var(--gold,#F0C24B);outline-offset:2px}

.arc-reduced .g-dt-howto,.arc-reduced .g-dt-hw-cell,.arc-reduced .g-dt-hw-key,
.arc-reduced .g-dt-hw-slab,.arc-reduced .g-dt-hw-rungs i{animation:none !important}
.arc-reduced .g-dt-hw-cell.typed{opacity:1}
@media (prefers-reduced-motion: reduce){
  .g-dt-howto,.g-dt-hw-cell,.g-dt-hw-key,.g-dt-hw-slab,.g-dt-hw-rungs i{animation:none !important}
  .g-dt-hw-cell.typed{opacity:1}
}
@media (max-width:560px),(pointer:coarse){
  .g-dt-howto{padding:16px 14px 14px;gap:0}
  .g-dt-hw-row{gap:11px;padding:9px 0}
  .g-dt-hw-cap{font-size:11.5px}
  .g-dt-hw-cell{width:22px;height:22px;font-size:11px}
}
@media (max-height:560px){
  .g-dt-hw-row{padding:7px 2px}
  .g-dt-hw-title{margin-bottom:4px}
}

/* ---- coarse pointer / narrow: bigger tiles, full-width commit ----------- */
@media (max-width:560px),(pointer:coarse){
  .g-dt{--dt-cell:clamp(34px,11vw,52px)}
  .g-dt-key{min-width:30px;height:44px;font-size:13px}
  .g-dt-board{gap:5px}
  .g-dt-hud{top:64px;left:10px}
}
/* very short viewports (the old 420px classroot fallback): drop the flourishes */
@media (max-height:560px){
  .g-dt{padding-top:10px}
  .g-dt-hud{position:static;flex-direction:row;align-items:center}
  .g-dt-lesson{display:none}
  .g-dt-slab{padding:10px 14px;border-width:1px}
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
.g-dt-howto{pointer-events:auto}
.g-dt-hw-go{position:sticky;bottom:0;z-index:3;align-self:stretch;margin-top:14px}

/* ---- THE PHONE CEILING (html.ae-touch) ----------------------------------
   The shell arms .ae-touch on <html> for coarse-pointer devices at boot. A
   phone GPU pays for every blurred layer and for every keyframe that repaints
   a filter, a box-shadow or a text-shadow; the room's two biggest nodes (the
   lamp at 38vw x 34vh and the neon bleed) were both wearing large blurs. The
   room keeps its colours - the gradients are the light, the blur only softened
   the edge - and every animation that carries a read is frozen at its lit
   state or given a transform/opacity twin, never hidden. Desktop is untouched.
   ------------------------------------------------------------------------- */
/* the lamps: the gradient IS the glow. Drop the 46px blur, widen the falloff
   so the edge stays soft, keep the slow breath (opacity only = compositor). */
html.ae-touch .g-dt-lamp{filter:none;
  background:radial-gradient(closest-side,
    var(--dt-n-lamp, rgba(240,194,75,.22)) 0%,
    color-mix(in srgb, var(--dt-n-lamp, rgba(240,194,75,.22)), transparent 55%) 42%,
    transparent 84%)}
/* the door neon: cheap blur, and the 7s flicker freezes lit (as arc-reduced) */
html.ae-touch .g-dt-neonbleed{filter:blur(12px);animation:none;opacity:1}
/* the marquee's gold drop-shadow re-rasterised every frame of the crawl */
html.ae-touch .g-dt-mq.g-dt-mq-gold{filter:none}
html.ae-touch .g-dt-mq.g-dt-mq-flash{animation:g-dt-mqflash-t .6s ease-out 1}
@keyframes g-dt-mqflash-t{0%{opacity:1}100%{opacity:var(--g-dt-mqa,.25)}}
/* HUD chips: no live blur behind glass, so the glass turns solid instead */
html.ae-touch .g-dt-hud .chip{backdrop-filter:none;-webkit-backdrop-filter:none;
  background:color-mix(in srgb, var(--navy,#1A1A2E), transparent 6%)}
/* the chalk whisper wrote itself in with a blur ramp: opacity-only twin */
html.ae-touch .g-dt-whisper{animation:g-dt-whisperin-t 3.4s ease-in-out both}
@keyframes g-dt-whisperin-t{0%{opacity:0}18%{opacity:.85}78%{opacity:.85}100%{opacity:0}}
/* the certificate word pulsed a 110px text-shadow forever: scale-only twin */
html.ae-touch .g-dt-cer .g-dt-word{animation:g-dt-pulse-t 1.5s ease-in-out infinite}
/* the bad certificate keeps ITS loop (letter-spacing + opacity, no shadow) -
   the touch rule above out-specifies .g-dt-cer.bad, so hand it back by name */
html.ae-touch .g-dt-cer.bad .g-dt-word{animation:g-dt-invert 1.1s steps(2) infinite}
@keyframes g-dt-pulse-t{50%{transform:scale(1.06)}}
/* the rules sheet: two infinite box-shadow loops freeze at their lit frame,
   and the keycap keeps its press on transform alone */
html.ae-touch .g-dt-hw-cell.caret{animation:none;
  box-shadow:inset 0 -3px 0 var(--pink,#FF69B4)}
html.ae-touch .g-dt-hw-slab.next{animation:none;
  box-shadow:0 0 10px rgba(255,105,180,.55)}
html.ae-touch .g-dt-hw-key{animation:g-dt-hw-press-t 3.4s ease-in-out infinite}
@keyframes g-dt-hw-press-t{0%,84%{transform:translateY(0)}
  90%{transform:translateY(2px)}100%{transform:translateY(0)}}
/* reduced motion still wins on touch (the sheet's kills carry !important),
   but the twins above are new names, so say the freeze once more for them */
html.arc-reduced.ae-touch .g-dt-cer .g-dt-word,
html.arc-reduced.ae-touch .g-dt-whisper,
html.arc-reduced.ae-touch .g-dt-hw-key{animation:none !important}
@media (prefers-reduced-motion: reduce){
  html.ae-touch .g-dt-cer .g-dt-word,html.ae-touch .g-dt-whisper,
  html.ae-touch .g-dt-hw-key{animation:none !important}
}

`;

/** Inject once per document. No-op headless (and never throws). */
export function injectStyles() {
  try {
    if (typeof document === 'undefined' || !document.createElement) return false;
    if (document.getElementById && document.getElementById(STYLE_ID)) return false;
    const tag = document.createElement('style');
    tag.id = STYLE_ID;
    tag.textContent = STYLE_TEXT;
    const host = document.head || document.documentElement || document.body;
    if (!host || !host.appendChild) return false;
    host.appendChild(tag);
    return true;
  } catch (e) { return false; }
}

export default injectStyles;
