/* ============================================================================
 * games/impulse-control/style.js - the class injects its OWN stylesheet.
 *
 * styles.css is shell chrome ONLY (see its header), so everything this class
 * paints ships here and is namespaced `.g-ic-*` (game: impulse control). Injected
 * exactly once per document, lazily, on the first create().
 *
 * THE EXAMINATION HALL (game-immersion wave, follows the campus hub's law: the
 * window IS the room). The class no longer renders a card in a column - `.g-ic`
 * is a full-bleed stage pinned to the class root: one hard spotlight pool over a
 * much larger aperture, floorboards converging toward a dark horizon, posture
 * lines up the walls, a distant door, and the metrics as a proctor's ledger
 * pinned to the bottom edge. Austerity IS Discipline Hall - but the darkness is
 * dressed as a ROOM, never dead pixels.
 *
 * THE ROOM REACTS (render.js roomBeat): a correct withhold dims the light
 * approvingly, a correct GO brightens it, an error makes the spotlight snap and
 * flicker, an induced (machine-attributed) error tints the whole hall pink.
 * These dress EXISTING feedback events - no timing, scoring or lie logic here.
 *
 * INPUT TRUST: every decorative layer is a pseudo-element or pointer-events:none
 * node; the arena (the whole stage) stays the one bare tap surface, and the
 * stimulus element keeps its original opacity-only reveal - nothing new ever
 * delays a stimulus paint.
 *
 * Colours read the shell's palette tokens with the mockup values as fallbacks,
 * so a mod palette (init.palette -> :root custom properties) re-skins the
 * assessment for free and nothing here is a literal hex that can drift.
 *
 * MOTION: every animation is also neutralised in CSS under
 * `prefers-reduced-motion`, not just in JS - a stray node must never strobe even
 * if a code path forgets to check. The aperture and the stimulus glyph never
 * move under reduced motion; only opacity does.
 * ==========================================================================*/

const STYLE_ID = 'g-ic-style';

export const STYLE_TEXT = `
.g-ic{--ic-pink:var(--pink,#FF69B4);--ic-lav:var(--lav,#B8A6E8);--ic-gold:var(--gold,#F0C24B);
  --ic-ink:var(--ink,#F2EBDD);--ic-dim:var(--ink-dim,#B9B3CE);--ic-faint:var(--ink-faint,#8A84A8);
  --ic-panel:var(--panel,#252542);--ic-line:var(--line,#3A3A5E);--ic-ground:var(--ground,#14142B);
  position:absolute;inset:0;overflow:hidden;
  /* the hall: vignette over posture-line walls over the base gloom */
  background:
    radial-gradient(130% 96% at 50% 44%, transparent 42%, rgba(6,6,16,.62) 100%),
    repeating-linear-gradient(90deg, transparent 0 138px, rgba(184,166,232,.035) 138px 139px),
    radial-gradient(circle at 50% 42%, #1E1E3C, var(--ic-ground) 72%)}

/* floorboards converging toward the dark end of the hall */
.g-ic::before{content:"";position:absolute;left:-32%;right:-32%;bottom:-14%;height:60%;
  pointer-events:none;opacity:.75;z-index:0;
  background:
    repeating-linear-gradient(90deg, rgba(184,166,232,.07) 0 2px, transparent 2px 124px),
    repeating-linear-gradient(0deg, rgba(184,166,232,.05) 0 1.5px, transparent 1.5px 92px),
    linear-gradient(180deg, transparent, rgba(20,20,43,.5));
  transform:perspective(620px) rotateX(58deg);transform-origin:50% 0}

/* THE LIGHT - one hard spotlight pool. The room-feedback beats animate THIS
   layer (it is the light reacting, not the UI). */
.g-ic::after{content:"";position:absolute;inset:0;pointer-events:none;z-index:0;
  background:radial-gradient(52vmin 52vmin at 50% 44%,
    rgba(255,219,178,.11), rgba(255,214,170,.035) 44%, transparent 72%)}
.g-ic.room-go::after{animation:g-ic-roomgo .6s ease-out 1}
.g-ic.room-calm::after{animation:g-ic-roomcalm 1.15s ease-out 1}
.g-ic.room-snap::after{animation:g-ic-roomsnap .42s steps(4,end) 1}
.g-ic.room-lie::after{animation:g-ic-roomlie .9s ease-out 1}
.g-ic.room-armed::after{animation:g-ic-roomarmed 1.7s ease-in-out infinite}
@keyframes g-ic-roomgo{0%{filter:none}22%{filter:brightness(1.9) saturate(1.15)}100%{filter:none}}
@keyframes g-ic-roomcalm{0%{opacity:1}30%{opacity:.4}100%{opacity:1}}
@keyframes g-ic-roomsnap{0%{opacity:1}25%{opacity:.15}50%{opacity:.9}75%{opacity:.25}100%{opacity:1}}
@keyframes g-ic-roomlie{0%{filter:none}25%{filter:hue-rotate(-55deg) saturate(2.4) brightness(1.35)}100%{filter:none}}
@keyframes g-ic-roomarmed{0%,100%{opacity:1}50%{opacity:.66}}

/* the distant door - the one you came in through */
.g-ic-door{position:absolute;left:9%;top:16%;width:56px;height:118px;pointer-events:none;z-index:0;
  border:1px solid rgba(184,166,232,.15);border-bottom:0;border-radius:3px 3px 0 0;opacity:.85}
.g-ic-door::after{content:"";position:absolute;right:8px;top:54%;width:3px;height:3px;border-radius:50%;
  background:rgba(240,194,75,.5);box-shadow:0 0 6px rgba(240,194,75,.4)}

/* ---- exam-sheet header (the stamped letterhead) --------------------------- */
.g-ic-chrome{position:absolute;top:64px;left:50%;transform:translateX(-50%);z-index:5;
  display:flex;flex-wrap:wrap;gap:8px 26px;align-items:center;justify-content:center;
  max-width:92%;padding:10px 30px;pointer-events:none;
  border-top:1px solid var(--ic-line);border-bottom:1px solid var(--ic-line);
  background:linear-gradient(180deg, rgba(20,20,43,.42), rgba(20,20,43,.18));
  font-family:var(--mono,monospace);font-size:10.5px;letter-spacing:.18em;
  text-transform:uppercase;color:var(--ic-faint);text-align:center}
.g-ic-chrome b{color:var(--ic-dim);font-weight:600;letter-spacing:.22em}
.g-ic-chrome .g-ic-warn{color:var(--ic-gold);opacity:0;transition:opacity .25s ease}
.g-ic-chrome .g-ic-warn.on{opacity:1;text-shadow:0 0 10px rgba(240,194,75,.5)}

/* ---- arena + aperture: the whole hall is the tap surface ------------------ */
.g-ic-arena{position:absolute;inset:0;z-index:1;display:flex;align-items:center;justify-content:center;
  background:transparent;cursor:pointer;touch-action:manipulation;-webkit-tap-highlight-color:transparent}
.g-ic-aperture{position:relative;width:clamp(280px,38vmin,440px);height:clamp(280px,38vmin,440px);
  border-radius:50%;border:2px dashed #4A4A80;display:flex;align-items:center;justify-content:center;
  transition:border-color .18s ease,box-shadow .18s ease,transform .3s ease;
  box-shadow:0 0 90px rgba(255,105,180,.07)}
.g-ic-aperture.tight{border-color:var(--ic-pink);box-shadow:0 0 0 1px rgba(255,105,180,.25) inset,0 0 90px rgba(255,105,180,.12)}
.g-ic-aperture.hot{box-shadow:0 0 64px rgba(255,105,180,.4)}
.g-ic-stim{font-family:var(--disp,serif);font-size:clamp(56px,10vmin,116px);letter-spacing:.14em;padding-left:.14em;
  color:var(--ic-ink);text-shadow:0 0 34px rgba(255,105,180,.55);opacity:0;
  transition:opacity .09s linear;text-align:center;line-height:1.05}
.g-ic-stim.on{opacity:1}
.g-ic-stim.nogo{color:var(--ic-lav);text-shadow:0 0 28px rgba(184,166,232,.45)}
.g-ic-stim img,.g-ic-stim video{max-width:min(30vmin,330px);max-height:min(30vmin,330px);border-radius:12px;display:block}
/* NO-GO media twins are CSS-filter transforms of the GO asset - never a canvas
   read, so a CORS-tainted remote loop is legal here (synthesis closure). The
   provider serves mp4 on iOS, so every twin has to hold for <video> too. */
.g-ic-stim .twin-mirror{transform:scaleX(-1)}
.g-ic-stim .twin-hue{filter:hue-rotate(150deg) saturate(1.15)}
.g-ic-stim .twin-flip{transform:rotate(180deg)}
.g-ic-stim .twin-dim{filter:brightness(.72) contrast(1.2)}

.g-ic-ring{position:absolute;inset:0;border:2.5px solid var(--ic-pink);border-radius:50%;
  pointer-events:none;opacity:0}
.g-ic-ring.go{animation:g-ic-ringout .62s ease-out 1}
@keyframes g-ic-ringout{from{transform:scale(.72);opacity:.9}to{transform:scale(1.28);opacity:0}}
.g-ic-ash{position:absolute;inset:0;border-radius:50%;pointer-events:none;opacity:0;
  background:radial-gradient(circle at 50% 50%,rgba(184,166,232,.35),transparent 65%)}
.g-ic-ash.on{animation:g-ic-sip .7s ease-out 1}
@keyframes g-ic-sip{from{opacity:.8;transform:scale(1)}to{opacity:0;transform:scale(.82)}}

.g-ic-rt{position:absolute;left:calc(50% + 21vmin);top:calc(50% - 19vmin);font-family:var(--mono,monospace);font-weight:600;
  font-size:clamp(20px,2.6vmin,30px);color:var(--ic-gold);text-shadow:0 0 14px rgba(240,194,75,.5);opacity:0;
  pointer-events:none}
.g-ic-rt.on{animation:g-ic-rtfloat .95s ease-out 1}
.g-ic-rt.slow{color:var(--ic-dim);text-shadow:none}
.g-ic-rt.best{color:#fff;text-shadow:0 0 18px rgba(255,105,180,.9)}
@keyframes g-ic-rtfloat{from{transform:translateY(8px);opacity:0}25%{opacity:1}to{transform:translateY(-20px);opacity:0}}

/* the window-edge flash: now the whole hall's walls catch the gold */
.g-ic-edge{position:absolute;inset:0;pointer-events:none;opacity:0;
  box-shadow:inset 0 0 0 3px var(--ic-gold),inset 0 0 60px rgba(240,194,75,.25)}
.g-ic-edge.on{animation:g-ic-edge .45s ease-out 1}
@keyframes g-ic-edge{from{opacity:.8}to{opacity:0}}

/* ---- the attribution toast (the game's soul, mid-class) ------------------ */
.g-ic-toast{position:absolute;left:50%;bottom:128px;transform:translateX(-50%);z-index:6;
  width:max-content;max-width:min(560px,86%);pointer-events:none;
  background:rgba(28,26,54,.92);border:1px solid var(--pink-deep,#D4488F);
  border-left:4px solid var(--ic-pink);border-radius:10px;padding:10px 14px;font-size:12.5px;
  color:var(--ic-dim);opacity:0;transition:opacity .22s ease;box-shadow:0 12px 34px rgba(0,0,0,.5)}
.g-ic-toast.on{opacity:1}
.g-ic-toast b{color:var(--ic-pink)}
.g-ic-toast.clean{border-left-color:var(--ic-lav)}
.g-ic-toast.clean b{color:var(--ic-lav)}

/* ---- the proctor's ledger: interference strip + footline, bottom edge ----- */
.g-ic-lielog{position:absolute;left:6%;right:6%;bottom:58px;height:14px;z-index:5;pointer-events:none;
  border-left:1px solid var(--ic-line);border-right:1px solid var(--ic-line);
  background:rgba(20,20,43,.5)}
.g-ic-lielog i{position:absolute;top:2px;width:3px;height:10px;border-radius:1px;background:var(--ic-lav)}
.g-ic-lielog i.err{background:var(--ic-pink);box-shadow:0 0 6px rgba(255,105,180,.8)}
.g-ic-lielog i.clean-err{background:var(--ic-faint)}
.g-ic-base{position:absolute;left:0;right:0;bottom:0;z-index:5;pointer-events:none;
  display:flex;flex-wrap:wrap;gap:6px 36px;justify-content:center;align-items:baseline;
  padding:13px 24px 15px;border-top:1px solid var(--ic-line);
  background:linear-gradient(180deg, rgba(16,16,36,0), rgba(12,12,28,.88) 46%);
  font-family:var(--mono,monospace);font-size:11px;letter-spacing:.16em;
  text-transform:uppercase;color:var(--ic-faint)}
.g-ic-base b{color:var(--ic-ink);font-size:13.5px;letter-spacing:.08em}
.g-ic-meter{position:absolute;left:50%;transform:translateX(-50%);bottom:88px;z-index:5;pointer-events:none}

/* ---- block break card: the hall holds its breath -------------------------- */
.g-ic-break{position:absolute;inset:0;display:flex;flex-direction:column;align-items:center;
  justify-content:center;gap:8px;text-align:center;padding:16px;z-index:7;
  background:radial-gradient(60vmin 60vmin at 50% 46%, rgba(30,30,60,.55), rgba(12,12,28,.9) 78%);
  backdrop-filter:blur(2px)}
.g-ic-break h3{margin:0;font-family:var(--disp,serif);font-size:clamp(22px,3.4vmin,34px);letter-spacing:.14em;color:var(--ic-ink)}
.g-ic-break p{margin:0;font-size:13.5px;color:var(--ic-dim)}
.g-ic-break .g-ic-stampline{font-family:var(--disp,serif);font-size:16px;letter-spacing:.2em;color:var(--ic-gold);
  text-shadow:0 0 16px rgba(240,194,75,.5)}

/* ---- debrief: the report desk under the same roof ------------------------- */
.g-ic-debrief{padding:84px max(28px, calc(50% - 380px)) 44px;overflow-y:auto;display:block}
.g-ic-debrief h3{margin:0 0 2px;font-family:var(--disp,serif);font-size:clamp(22px,3vmin,30px);letter-spacing:.1em}
.g-ic-sub{font-family:var(--mono,monospace);font-size:11px;color:var(--ic-faint);letter-spacing:.12em;
  text-transform:uppercase;margin:0 0 14px}
.g-ic-grid{display:flex;flex-wrap:wrap;gap:10px;margin:0 0 14px}
.g-ic-cell{flex:1 1 140px;background:rgba(30,28,58,.55);border:1px solid var(--ic-line);border-radius:10px;padding:11px 13px}
.g-ic-cell span{display:block;font-size:10px;letter-spacing:.12em;color:var(--ic-faint);text-transform:uppercase}
.g-ic-cell b{display:block;font-family:var(--mono,monospace);font-size:19px;color:var(--ic-ink)}
.g-ic-cell b.gold{color:var(--ic-gold)}
.g-ic-cell b.pink{color:var(--ic-pink)}
.g-ic-timeline{position:relative;height:36px;margin:0 0 6px;border:1px solid var(--ic-line);
  border-radius:8px;background:linear-gradient(180deg,rgba(28,28,56,.7),rgba(37,37,66,.5))}
.g-ic-timeline i{position:absolute;top:5px;width:3px;height:11px;border-radius:1px;background:var(--ic-lav)}
.g-ic-timeline i.err{top:19px;height:12px;background:var(--ic-pink);box-shadow:0 0 6px rgba(255,105,180,.8)}
.g-ic-timeline i.clean-err{top:19px;height:12px;background:var(--ic-faint)}
.g-ic-legend{font-family:var(--mono,monospace);font-size:10px;color:var(--ic-faint);letter-spacing:.06em;margin:0 0 14px}
.g-ic-lines{list-style:none;margin:0 0 16px;padding:0;display:flex;flex-direction:column;gap:6px}
.g-ic-lines li{background:rgba(30,28,58,.55);border-left:3px solid var(--ic-lav);border-radius:8px;
  padding:8px 11px;font-size:12.5px;color:var(--ic-dim)}
.g-ic-lines li.induced{border-left-color:var(--ic-pink)}
.g-ic-lines li b{color:var(--ic-ink)}
.g-ic-lines li em{color:var(--ic-pink);font-style:normal}
.g-ic-slip{font-size:13.5px;color:var(--ic-ink);margin:0 0 16px}
.g-ic-actions{display:flex;flex-wrap:wrap;gap:8px;align-items:center}
.g-ic-hint{font-family:var(--mono,monospace);font-size:10.5px;color:var(--ic-faint);margin:8px 0 0}

/* small stages: the hall keeps working in the old boxed root too */
@media (max-height:560px){
  .g-ic-chrome{top:10px}
  .g-ic-aperture{width:clamp(200px,32vmin,280px);height:clamp(200px,32vmin,280px)}
  .g-ic-door{display:none}
}

/* ---- reduced motion: opacity only, no transforms, no strobe -------------- */
@media (prefers-reduced-motion: reduce){
  .g-ic-ring.go,.g-ic-ash.on,.g-ic-rt.on,.g-ic-edge.on{animation:none!important}
  .g-ic.room-go::after,.g-ic.room-calm::after,.g-ic.room-snap::after,
  .g-ic.room-lie::after,.g-ic.room-armed::after{animation:none!important}
  .g-ic-rt{transition:opacity .2s ease}
  .g-ic-aperture{transition:none}
  .g-ic-stim{transition:opacity .16s linear}
}
`;

/** Inject once per document. Safe to call from a headless import (no DOM = no-op). */
export function injectStyle() {
  if (typeof document === 'undefined' || !document || !document.createElement) return false;
  try {
    if (document.getElementById && document.getElementById(STYLE_ID)) return false;
    const tag = document.createElement('style');
    tag.id = STYLE_ID;
    tag.textContent = STYLE_TEXT;
    const head = document.head || document.body || document.documentElement;
    if (!head || !head.appendChild) return false;
    head.appendChild(tag);
    if (document._register) document._register(STYLE_ID, tag);   // test-double hook
    return true;
  } catch (e) {
    return false;   // a missing stylesheet must never stop the class
  }
}

export default injectStyle;
