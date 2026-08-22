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
 * INPUT TRUST: only .g-ic-bubble takes pointer events. Every other layer is
 * pointer-events:none, including the flourish and the stamp.
 *
 * MOTION: all animation is also neutralised under prefers-reduced-motion, and
 * .suspended pauses every animation via animation-play-state - the CSS denies
 * a strobe even when a JS path forgets.
 * ==========================================================================*/

const STYLE_ID = 'g-ic-style';

export const STYLE_TEXT = `
.g-ic{--ic-pink:var(--pink,#FF69B4);--ic-lav:var(--lav,#B8A6E8);--ic-gold:var(--gold,#F0C24B);
  --ic-ink:var(--ink,#F2EBDD);--ic-dim:var(--ink-dim,#B9B3CE);--ic-faint:var(--ink-faint,#8A84A8);
  --ic-panel:var(--panel,#252542);--ic-line:var(--line,#3A3A5E);--ic-ground:var(--ground,#14142B);
  position:absolute;inset:0;overflow:hidden;color:var(--ic-ink);
  background:radial-gradient(circle at 50% 46%, #1E1E3C, var(--ic-ground) 74%)}

/* ------------------------------------------------------------ the backdrop */
.g-ic-bg{position:absolute;inset:0;pointer-events:none;z-index:0;overflow:hidden;
  background:
    radial-gradient(120% 90% at 50% 40%, transparent 40%, rgba(6,6,16,.7) 100%),
    conic-gradient(from 210deg at 50% 46%, #191934, #222244 30%, #14142B 62%, #1d1d3a 100%)}
.g-ic-bg-img{position:absolute;inset:0;width:100%;height:100%;object-fit:cover;
  opacity:0;transition:opacity .9s ease;filter:saturate(.9) blur(2px);transform:scale(1.07)}
.g-ic-bg-img.on{animation:g-ic-kenburns 26s ease-in-out infinite alternate}
@keyframes g-ic-kenburns{from{transform:scale(1.07)}to{transform:scale(1.15) translateY(-1.5%)}}
/* the depth ring: media edges melt into atmosphere, the centre stays legible */
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
.g-ic-tube-canvas{position:absolute;inset:0;width:100%;height:100%;display:block}
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

/* -------------------------------------------------------------- the basin */
/* THE BUBBLE MUST FIT THE BORE. It travelled inside the chute, so a reveal
   WIDER than the tube's projected bore breaks the fiction on sight. At the
   camera tube3d.js pins, the bore (dia 1.7 world) lands ~0.18 x viewport-height
   near the basin; 14vmin keeps the reveal at ~0.8 of that on any sane window
   while the 106px floor keeps the game's ONE tap target thumb-sized. Move this
   ONLY together with TUBE_R. */
.g-ic-basin{position:absolute;left:50%;top:50%;width:0;height:0;z-index:3}
.g-ic-bubble{position:absolute;left:0;top:0;width:clamp(106px,14vmin,190px);height:clamp(106px,14vmin,190px);
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
  -webkit-mask:radial-gradient(circle, transparent 0 82%, #000 84%);
  mask:radial-gradient(circle, transparent 0 82%, #000 84%)}
.g-ic-holdring.on{display:block;animation:g-ic-hold var(--ic-hold,2000ms) linear forwards}
@keyframes g-ic-hold{from{--ic-k:1}to{--ic-k:0}}
@supports not (background:conic-gradient(red calc(var(--x)*360deg),blue 0)){
  .g-ic-holdring.on{background:rgba(184,166,232,.18);animation:g-ic-holdfade var(--ic-hold,2000ms) linear forwards}
  @keyframes g-ic-holdfade{from{opacity:1}to{opacity:.15}}}

/* ------------------------------------------------------------ the flourish */
.g-ic-flourish{position:absolute;inset:0;pointer-events:none;z-index:4;display:grid;place-items:center;overflow:hidden}
.g-ic-flourish-img{width:34vmin;height:34vmin;object-fit:contain;opacity:0;
  animation:g-ic-spiralout 1s ease-out forwards}
@keyframes g-ic-spiralout{0%{opacity:.9;transform:scale(.3) rotate(0)}
  100%{opacity:0;transform:scale(2.4) rotate(300deg)}}

/* --------------------------------------------------------------- the stamp */
/* tracks the bubble: just clear of its crown, still inside the crater's mouth */
.g-ic-stamp{position:absolute;left:50%;top:calc(50% - clamp(86px,11vmin,150px));transform:translateX(-50%);
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

/* -------------------------------------------------------------- the chrome */
.g-ic-topline{position:absolute;left:18px;top:64px;z-index:5;pointer-events:none;
  display:flex;gap:10px;align-items:baseline;opacity:.85}
.g-ic-topname{font-weight:700;letter-spacing:.12em;text-transform:uppercase;font-size:12px;color:var(--ic-lav)}
.g-ic-topsub{font-size:11px;color:var(--ic-faint);letter-spacing:.06em}

.g-ic-hud{position:absolute;left:0;right:0;bottom:0;z-index:5;pointer-events:none;
  display:flex;align-items:flex-end;justify-content:space-between;gap:16px;
  padding:14px 22px 16px;
  background:linear-gradient(180deg, transparent, rgba(8,8,20,.72) 60%)}
.g-ic-hud.off{display:none}
.g-ic-hud-cell{display:flex;flex-direction:column;gap:6px;min-width:120px}
.g-ic-hud-mid{align-items:center;flex:1}
.g-ic-hud-right{align-items:flex-end}
.g-ic-score-label,.g-ic-streak-label{font-size:10px;letter-spacing:.18em;text-transform:uppercase;color:var(--ic-faint)}
.g-ic-score{font-size:clamp(26px,4vmin,40px);font-weight:800;color:var(--ic-ink);
  font-variant-numeric:tabular-nums;line-height:1;text-shadow:0 0 22px rgba(255,105,180,.3)}
.g-ic-rt{font-size:11px;color:var(--ic-dim);font-variant-numeric:tabular-nums;min-height:14px}
.g-ic-counter{font-size:11px;color:var(--ic-dim);letter-spacing:.06em}
.g-ic-thread{width:150px;height:3px;border-radius:2px;background:rgba(184,166,232,.18);overflow:hidden}
.g-ic-thread-fill{display:block;height:100%;border-radius:2px;background:var(--ic-pink);
  width:calc(var(--ic-prog,0)*100%);transition:width .4s ease}
.g-ic-pips{display:flex;gap:6px}
.g-ic-pip{width:9px;height:9px;border-radius:50%;background:rgba(184,166,232,.18);border:1px solid rgba(184,166,232,.3)}
.g-ic-pip.on{background:var(--ic-pink);box-shadow:0 0 8px rgba(255,105,180,.7)}

/* --------------------------------------------------------------- the cards */
.g-ic-break{position:absolute;left:50%;top:50%;transform:translate(-50%,-50%);z-index:6;
  text-align:center;max-width:min(560px,86vw);pointer-events:none;
  padding:26px 30px;border-radius:16px;background:rgba(14,14,32,.78);
  border:1px solid var(--ic-line);backdrop-filter:blur(6px)}
.g-ic-break-title{margin:0 0 8px;font-size:clamp(22px,3.4vmin,32px);letter-spacing:.16em;text-transform:uppercase;
  color:var(--ic-pink);text-shadow:0 0 26px rgba(255,105,180,.45)}
.g-ic-break-note{margin:0 0 6px;color:var(--ic-ink);font-size:15px}
.g-ic-break-hint{margin:0;color:var(--ic-faint);font-size:12.5px}

.g-ic-debrief{position:absolute;inset:0;z-index:7;display:grid;place-items:center;
  background:radial-gradient(80% 70% at 50% 42%, rgba(10,10,24,.55), rgba(8,8,20,.9));overflow:auto}
.g-ic-paper{width:min(680px,92vw);border-radius:14px;padding:26px 30px;
  background:linear-gradient(180deg,#20203E,#1A1A32);border:1px solid var(--ic-line);
  box-shadow:0 30px 80px rgba(0,0,0,.5)}
.g-ic-paper-head{display:flex;align-items:baseline;justify-content:space-between;gap:12px;
  border-bottom:1px dashed var(--ic-line);padding-bottom:10px;margin-bottom:14px}
.g-ic-paper-head h2{margin:0;font-size:18px;letter-spacing:.14em;text-transform:uppercase;color:var(--ic-lav)}
.g-ic-paper-sub{color:var(--ic-faint);font-size:12px}
.g-ic-paper-score{display:flex;align-items:baseline;gap:10px;margin-bottom:14px}
.g-ic-paper-score b{font-size:44px;color:var(--ic-ink);font-variant-numeric:tabular-nums;line-height:1;
  text-shadow:0 0 24px rgba(255,105,180,.35)}
.g-ic-paper-score span{font-size:11px;letter-spacing:.18em;text-transform:uppercase;color:var(--ic-faint)}
.g-ic-paper-grid{display:grid;grid-template-columns:repeat(3,1fr);gap:10px;margin-bottom:14px}
.g-ic-cell{background:rgba(10,10,26,.55);border:1px solid var(--ic-line);border-radius:10px;
  padding:10px 12px;display:flex;flex-direction:column;gap:3px}
.g-ic-cell b{font-size:18px;color:var(--ic-ink);font-variant-numeric:tabular-nums}
.g-ic-cell span{font-size:10px;letter-spacing:.14em;text-transform:uppercase;color:var(--ic-faint)}
.g-ic-cell.good b{color:#7BD88F}.g-ic-cell.bad b{color:#FF5A78}.g-ic-cell.gold b{color:var(--ic-gold)}
.g-ic-paper-line{margin:0 0 4px;color:var(--ic-dim);font-size:13px}
.g-ic-paper-hint{margin:0 0 10px;color:var(--ic-faint);font-size:12px}
.g-ic-paper-actions{display:flex;gap:10px;justify-content:flex-end;margin-top:8px;flex-wrap:wrap}

/* --------------------------------------------------------------- the shake */
.g-ic.shake{animation:g-ic-shake .42s cubic-bezier(.36,.07,.19,.97)}
@keyframes g-ic-shake{10%,90%{transform:translate(-2px,1px)}20%,80%{transform:translate(4px,-2px)}
  30%,50%,70%{transform:translate(-7px,3px)}40%,60%{transform:translate(7px,-3px)}100%{transform:none}}
.g-ic.shake::after{content:"";position:absolute;inset:0;pointer-events:none;z-index:8;
  background:radial-gradient(90% 80% at 50% 50%, transparent 55%, rgba(255,42,84,.28) 100%);
  animation:g-ic-redwash .42s ease-out forwards}
@keyframes g-ic-redwash{from{opacity:1}to{opacity:0}}

/* --------------------------------------------------- suspend + motion law */
.g-ic.suspended *{animation-play-state:paused !important;transition:none !important}
@media (prefers-reduced-motion: reduce){
  .g-ic-bg-img.on{animation:none}
  .g-ic-bubble.on{animation:none;transform:translate(-50%,-50%) scale(1)}
  .g-ic-bubble.on .g-ic-bubble-img{animation:none}
  .g-ic.shake{animation:none}
  .g-ic-flourish-img{animation-duration:.24s}
  .g-ic-stamp.on{animation-duration:.5s}
  .g-ic-tube-static{animation:none}
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
