/* ============================================================================
 * games/daily-trigger/styles.js - this class's stylesheet, injected from JS.
 *
 * `styles.css` is the SHELL's chrome only, so the board, the keyboard and the two
 * ceremonies ship here, namespaced `.g-dt-*` (game: daily-trigger). Injected once
 * per document, lazily, and never in a headless import.
 *
 * Every colour is a shell token (`var(--pink)`, `var(--panel2)`, ...) with a
 * hardcoded fallback, so a mod palette (init.palette -> :root custom properties)
 * reskins this class for free and a missing token can never render invisible text.
 * Look ported from planning/arcademy/mockups/arcademy-mockup.html: keyboard under
 * the effects, tiles double-coded colour + glyph, ladder pips in the HUD.
 *
 * Reduced motion is honoured TWICE - the shell stamps `.arc-reduced` on <html>
 * and the media query below neutralises the animations anyway.
 * ==========================================================================*/

const STYLE_ID = 'g-dt-style';

export const STYLE_TEXT = `
.g-dt{--dt-hit:var(--pink,#FF69B4);--dt-near:var(--lav,#B8A6E8);--dt-miss:var(--line,#3A3A5E);
  --dt-cell:clamp(30px,6.2vw,44px);
  position:relative;display:flex;flex-direction:column;align-items:center;gap:12px;
  padding:16px 14px 14px;min-height:420px;color:var(--ink,#F2EBDD)}

/* ---- HUD ---------------------------------------------------------------- */
.g-dt-hud{display:flex;gap:10px;align-items:center;flex-wrap:wrap;justify-content:center;
  position:relative;z-index:5}
.g-dt-rung{display:inline-flex;gap:4px;align-items:center}
.g-dt-rung i{width:14px;height:8px;border-radius:2px;background:var(--line,#3A3A5E);
  transition:background .3s ease,box-shadow .3s ease}
.g-dt-rung i.on{background:var(--pink,#FF69B4);box-shadow:0 0 7px rgba(255,105,180,.6)}

/* ---- board -------------------------------------------------------------- */
.g-dt-board{display:grid;gap:6px;position:relative;z-index:4}
.g-dt-row{display:flex;gap:6px;justify-content:center}
.g-dt-row.shake{animation:g-dt-shake .34s ease}
.g-dt-gap{width:10px;flex:0 0 10px}
.g-dt-cell{width:var(--dt-cell);height:var(--dt-cell);border:2px solid var(--line,#3A3A5E);
  border-radius:6px;display:flex;align-items:center;justify-content:center;position:relative;
  font-family:var(--disp,serif);font-size:calc(var(--dt-cell) * .44);background:var(--navy,#1A1A2E);
  color:var(--ink,#F2EBDD);text-transform:uppercase;user-select:none;
  transition:transform .18s ease,background .25s ease,border-color .25s ease}
.g-dt-cell.typed{border-color:var(--lav,#B8A6E8);color:var(--lav,#B8A6E8)}
.g-dt-cell.active{border-color:var(--pink,#FF69B4);animation:g-dt-caret 1.1s steps(2) infinite}
.g-dt-cell.hinted{border-color:var(--gold,#F0C24B);color:var(--gold,#F0C24B)}
.g-dt-cell.gold{box-shadow:0 0 0 2px rgba(240,194,75,.55),0 0 14px rgba(240,194,75,.35)}
.g-dt-cell.flip{animation:g-dt-flip .32s ease both}
.g-dt-cell.hit{background:var(--dt-hit);border-color:var(--dt-hit);color:var(--ground,#14142B)}
.g-dt-cell.near{background:var(--dt-near);border-color:var(--dt-near);color:var(--ground,#14142B)}
.g-dt-cell.miss{background:var(--dt-miss);border-color:var(--dt-miss);color:var(--ink-faint,#8A84A8)}
/* double-coded: colour PLUS a stamped glyph, always on, never a setting */
.g-dt-cell.hit::after,.g-dt-cell.near::after,.g-dt-cell.miss::after{
  position:absolute;right:2px;bottom:0;font-size:10px;font-family:var(--body,sans-serif);opacity:.85}
.g-dt-cell.hit::after{content:"\\2605"}
.g-dt-cell.near::after{content:"\\25D0"}
.g-dt-cell.miss::after{content:"\\2715";color:var(--ink-faint,#8A84A8)}
.g-dt-cell.wobble{animation:g-dt-wobble .6s ease}

/* ---- message line ------------------------------------------------------- */
.g-dt-msg{min-height:18px;font-size:12px;letter-spacing:.04em;color:var(--ink-dim,#B9B3CE);
  text-align:center;position:relative;z-index:5}
.g-dt-msg.warn{color:var(--gold,#F0C24B)}

/* ---- keyboard (UNDER the effects, per the approved mockup) -------------- */
.g-dt-kb{display:flex;flex-direction:column;gap:5px;align-items:center;position:relative;z-index:4}
.g-dt-krow{display:flex;gap:5px}
.g-dt-key{min-width:26px;height:34px;padding:0 6px;border-radius:5px;
  background:var(--panel2,#2E2E55);border:1px solid #3E3E70;color:var(--ink-dim,#B9B3CE);
  font:600 12px/32px var(--body,sans-serif);text-align:center;cursor:pointer;
  box-shadow:0 2px 0 rgba(0,0,0,.35);position:relative;overflow:hidden;
  transition:transform .08s ease,background .25s ease,color .25s ease}
.g-dt-key:active,.g-dt-key.down{transform:translateY(2px);box-shadow:none}
.g-dt-key:focus-visible{outline:2px solid var(--pink,#FF69B4);outline-offset:2px}
.g-dt-key.hit{background:var(--dt-hit);color:var(--ground,#14142B);border-color:var(--dt-hit)}
.g-dt-key.near{background:var(--dt-near);color:var(--ground,#14142B);border-color:var(--dt-near)}
.g-dt-key.miss{background:#20203E;color:#5A5578;border-color:#2A2A4E}
.g-dt-key.wide{min-width:52px;font-size:10px;letter-spacing:.06em}
.g-dt-key.glitched{color:var(--lav,#B8A6E8);text-shadow:-2px 0 var(--pink,#FF69B4),2px 0 #6EE8E0}
.g-dt-commit{width:100%;max-width:340px}

/* ---- ceremony overlay (absorb + detention) ------------------------------ */
.g-dt-cer{position:absolute;inset:0;z-index:12;display:flex;flex-direction:column;
  align-items:center;justify-content:center;gap:14px;text-align:center;cursor:pointer;
  background:radial-gradient(circle at 50% 45%,rgba(20,20,43,.72),rgba(20,20,43,.94) 70%)}
.g-dt-cer .g-dt-word{font-family:var(--disp,serif);font-size:34px;letter-spacing:.34em;
  padding-left:.34em;color:var(--pink,#FF69B4);
  text-shadow:0 0 24px rgba(255,105,180,.8),0 0 60px rgba(255,105,180,.4);
  animation:g-dt-pulse 1.5s ease-in-out infinite}
.g-dt-cer.bad .g-dt-word{color:var(--gold,#F0C24B);text-shadow:0 0 22px rgba(240,194,75,.65);
  animation:g-dt-invert 1.1s steps(2) infinite}
.g-dt-cer .g-dt-line{font-size:13px;color:var(--ink-dim,#B9B3CE);max-width:34ch}
.g-dt-cer .g-dt-skip{font-size:11px;color:var(--ink-faint,#8A84A8);letter-spacing:.08em}
.g-dt-stamp{font-family:var(--disp,serif);font-size:12px;letter-spacing:.18em;
  color:var(--gold,#F0C24B);border:2px solid var(--gold,#F0C24B);border-radius:8px;
  padding:5px 12px;transform:rotate(-6deg);box-shadow:0 0 14px rgba(240,194,75,.35)}

/* ---- animations -------------------------------------------------------- */
@keyframes g-dt-caret{50%{border-color:var(--line,#3A3A5E)}}
@keyframes g-dt-flip{0%{transform:rotateX(0)}50%{transform:rotateX(-88deg)}100%{transform:rotateX(0)}}
@keyframes g-dt-shake{0%,100%{transform:translateX(0)}20%{transform:translateX(-6px)}
  50%{transform:translateX(5px)}80%{transform:translateX(-3px)}}
@keyframes g-dt-wobble{0%,100%{transform:rotate(0)}30%{transform:rotate(-7deg) scale(1.06)}
  65%{transform:rotate(6deg) scale(1.04)}}
@keyframes g-dt-pulse{50%{transform:scale(1.06);
  text-shadow:0 0 34px rgba(255,105,180,1),0 0 80px rgba(255,105,180,.5)}}
@keyframes g-dt-invert{50%{opacity:.45;letter-spacing:.4em}}

@media (prefers-reduced-motion: reduce){
  .g-dt-cell,.g-dt-key,.g-dt-cer .g-dt-word,.g-dt-row.shake{animation:none !important;
    transition:none !important}
}
.arc-reduced .g-dt-cell,.arc-reduced .g-dt-key,.arc-reduced .g-dt-cer .g-dt-word,
.arc-reduced .g-dt-row.shake{animation:none !important;transition:none !important}

/* ---- coarse pointer / narrow: bigger tiles, full-width commit ----------- */
@media (max-width:560px),(pointer:coarse){
  .g-dt{--dt-cell:clamp(34px,11vw,52px)}
  .g-dt-key{min-width:30px;height:40px;font-size:13px;line-height:38px}
  .g-dt-board{gap:5px}
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
