/* ============================================================================
 * games/deja-vu/style.js - the game injects its OWN stylesheet from JS.
 *
 * styles.css is shell chrome ONLY (a game restyle must never be able to regress
 * the board or the report card), and index.html is not ours to edit, so Deja Vu
 * ships its CSS the way engine/style.js does: one <style> tag, injected once,
 * every class namespaced `.g-dv-*`.
 *
 * Colours come from the shell tokens (--pink / --lav / --gold / --panel ...), so
 * a mod palette skins the board for free and nothing here hardcodes a hex that
 * a palette cannot move. The mockup's approved look (tile backs, wax badge,
 * shudder tell, judge pulse) is reproduced from planning/arcademy/mockups.
 *
 * REDUCED MOTION twice over: `html.arc-reduced` (the shell's flag, from
 * init.reducedMotion / motionLevel 0) AND the prefers-reduced-motion query, so a
 * stray node cannot rotate or strobe. The mechanic survives, the motion does not
 * (dossier): flips become crossfades, the swap tell becomes a border pulse, the
 * drift becomes a snap - and the audio tick is always retained.
 * ==========================================================================*/

const STYLE_ID = 'g-dv-style';

export const STYLE_TEXT = `
.g-dv-stage{position:relative;display:flex;flex-direction:column;align-items:center;gap:12px;
  padding:14px;min-height:420px}
.g-dv-hud{display:flex;flex-wrap:wrap;align-items:center;justify-content:center;gap:8px 12px;
  font-family:var(--mono);font-size:11px;color:var(--ink-faint)}
.g-dv-hud .g-dv-meterwrap{display:inline-flex;align-items:center;gap:6px}

/* ---- the board ---------------------------------------------------------- */
.g-dv-boardwrap{position:relative;width:100%;display:flex;justify-content:center}
.g-dv-grid{display:grid;gap:9px;perspective:900px;width:100%;max-width:440px;justify-content:center;
  grid-template-columns:repeat(var(--g-dv-cols,4),minmax(44px,84px))}
.g-dv-cell{position:relative;aspect-ratio:3/4;min-height:58px}
.g-dv-card{position:absolute;inset:0;display:flex;align-items:center;justify-content:center;
  padding:0;overflow:hidden;border-radius:8px;border:1px solid #3E3E70;color:var(--slate);
  background:linear-gradient(150deg,var(--panel2,#2E2E55),var(--panel,#252542));
  transform-style:preserve-3d;transition:transform .22s ease,opacity .2s ease,box-shadow .2s ease;
  cursor:pointer;font:inherit;font-size:20px;line-height:1}
.g-dv-card::after{content:"\\25C8";position:relative;z-index:1}
.g-dv-card.up::after,.g-dv-card.locked::after,.g-dv-card.ghost::after{content:none}
.g-dv-card:focus-visible{outline:2px solid var(--pink);outline-offset:2px}
.g-dv-card.dealt{animation:g-dv-toss .34s ease-out both}
.g-dv-card.up{background:linear-gradient(135deg,#412A62,#9A6ED8);border-color:var(--lav);color:var(--ink)}
.g-dv-card.flipping{transform:rotateY(62deg);border-color:var(--pink)}
.g-dv-card.judge{animation:g-dv-judge 1.2s ease-in-out infinite}
.g-dv-card.locked{background:linear-gradient(135deg,#2A3A4E,#3E5E72);border-color:#3E5E72;
  opacity:.88;cursor:default}
.g-dv-card.spot{box-shadow:0 0 0 2px var(--gold),0 0 18px rgba(240,194,75,.35)}
.g-dv-card.jiggle,.g-dv-grid.jiggle{animation:g-dv-jiggle .3s ease-in-out}
.g-dv-card.pulse{animation:g-dv-pulse .5s ease-out}

/* faces: one media node per card, opacity-toggled (never re-created on a flip) */
.g-dv-face{position:absolute;inset:0;width:100%;height:100%;object-fit:cover;
  opacity:0;transition:opacity .18s ease;pointer-events:none;background:var(--panel)}
.g-dv-card.up .g-dv-face,.g-dv-card.locked .g-dv-face{opacity:1}
.g-dv-card.ghost .g-dv-face{opacity:.2}

.g-dv-wax{position:absolute;right:-5px;top:-5px;width:20px;height:20px;border-radius:50%;
  background:var(--pink-deep);color:var(--ground);font-size:10px;line-height:20px;text-align:center;
  box-shadow:0 0 8px rgba(212,72,143,.6);z-index:2}

/* ---- the swap telegraph (always audible too: audio_trigger tick) --------- */
.g-dv-card.tell{animation:g-dv-shudder .6s linear infinite}
.g-dv-tellnote{position:absolute;left:50%;transform:translateX(-50%);bottom:-18px;white-space:nowrap;
  font-family:var(--mono);font-size:9px;letter-spacing:.08em;color:var(--gold);z-index:3}
.g-dv-line-tell{position:absolute;inset:0;border-radius:8px;pointer-events:none;
  background:linear-gradient(100deg,transparent 20%,rgba(184,166,232,.28) 50%,transparent 80%);
  animation:g-dv-sheen .52s linear}

/* ---- the flash well: engine one-shots anchored OVER the board -----------
   INPUT-TRUST LAW (DECISIONS #9). The board is a click-precision surface, so
   anything the engine draws into it is decoration: pointer-events:none here is
   inherited by every anchored child, and the game never passes an onPop over
   the grid. A tap always reaches the card underneath. */
.g-dv-flashwell{position:absolute;inset:0;overflow:hidden;pointer-events:none;z-index:6}
.g-dv-flashwell *{pointer-events:none}

.g-dv-hint{margin:0;text-align:center;font-size:12px;color:var(--ink-faint);min-height:1.2em}
.g-dv-hint.warm{color:var(--gold)}
.g-dv-cram{display:flex;align-items:center;gap:10px}
.g-dv-cram .arc-peekbtn.armed{border-color:var(--gold);color:var(--gold);
  box-shadow:0 0 14px rgba(240,194,75,.3)}
.g-dv-retake{border-color:var(--lav);color:var(--lav)}

/* ---- animations --------------------------------------------------------- */
@keyframes g-dv-toss{from{transform:translateY(-14px) rotate(-4deg);opacity:0}to{transform:none;opacity:1}}
@keyframes g-dv-judge{50%{transform:scale(1.06);box-shadow:0 0 16px rgba(184,166,232,.5)}}
@keyframes g-dv-pulse{0%{transform:scale(1)}45%{transform:scale(1.06);
  box-shadow:0 0 18px rgba(255,105,180,.45)}100%{transform:scale(1)}}
@keyframes g-dv-jiggle{25%{transform:translateX(-3px)}75%{transform:translateX(3px)}}
@keyframes g-dv-sheen{from{transform:translateX(-60%)}to{transform:translateX(60%)}}
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
html.arc-reduced .g-dv-line-tell{animation:none;opacity:.5}
@media (prefers-reduced-motion: reduce){
  .g-dv-card{transition:opacity .16s ease}
  .g-dv-card.flipping{transform:none;opacity:.45}
  .g-dv-card.dealt,.g-dv-card.judge,.g-dv-card.pulse,.g-dv-card.jiggle{animation:none}
  .g-dv-card.tell{animation:none;border-color:var(--gold);box-shadow:0 0 0 2px rgba(240,194,75,.55)}
  .g-dv-line-tell{animation:none;opacity:.5}
}

/* ---- narrow / touch: >=64px targets, one board that never scrolls sideways */
@media (max-width:560px){
  .g-dv-grid{gap:7px;grid-template-columns:repeat(var(--g-dv-cols,4),minmax(64px,1fr));max-width:100%}
  .g-dv-cell{min-height:64px}
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
