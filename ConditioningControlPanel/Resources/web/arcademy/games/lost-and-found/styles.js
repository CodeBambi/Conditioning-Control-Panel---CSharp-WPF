/* ============================================================================
 * games/lost-and-found/styles.js - the game's own CSS, self-injected once.
 *
 * styles.css is SHELL CHROME ONLY ("Games own their own CSS ... so a game
 * restyle can never regress the board or the report card"), so everything here
 * is namespaced `.g-lf-*` and every colour is a var() off a shell token - which
 * is what makes the mod palette (init.palette -> :root custom properties) skin
 * this board for free.
 *
 * Look ported from planning/arcademy/mockups/arcademy-mockup.html, the
 * owner-approved Lost & Found section (tile gradients, drifting strips, HUD
 * target chip, peek foot).
 *
 * MOTION CONTRACT: every animation here is a CSS animation or transition, so the
 * shell's reduced-motion rule (html.arc-reduced *) freezes ALL of it without this
 * file knowing - and index.js additionally swaps continuous drift for discrete
 * row steps. Nothing is required for legibility; the board is fully readable
 * frozen.
 * ==========================================================================*/

const STYLE_ID = 'g-lf-style';

export const CSS = [
  '.g-lf { display:flex; flex-direction:column; min-height:420px; }',

  /* ---------------------------------- HUD -------------------------------- */
  '.g-lf-hud { display:flex; align-items:center; gap:10px; flex-wrap:wrap;',
  '  padding:10px 12px; background:var(--panel); border-bottom:1px solid var(--line); }',
  '.g-lf-tchip { display:flex; align-items:center; gap:8px; padding:4px 10px 4px 4px;',
  '  background:var(--navy); border:1px solid var(--pink-deep); border-radius:10px; }',
  '.g-lf-tchip .g-lf-tt { width:34px; height:34px; border-radius:6px; overflow:hidden;',
  '  flex:0 0 auto; background:linear-gradient(135deg,#5E2A55,#B84A8F); position:relative; }',
  '.g-lf-tchip small { font-size:10px; letter-spacing:.1em; text-transform:uppercase;',
  '  color:var(--pink); font-family:var(--disp); }',
  '.g-lf-tchip b { font-family:var(--mono); font-size:12px; color:var(--ink); }',
  '.g-lf-spacer { flex:1 1 auto; }',

  /* --------------------------------- BOARD ------------------------------- */
  '.g-lf-view { position:relative; flex:1 1 auto; overflow:hidden; background:var(--ground);',
  '  min-height:300px; --g-lf-tw:82px; --g-lf-th:74px; --g-lf-gap:5px; }',
  '.g-lf-view.g-lf-lite { --g-lf-tw:64px; --g-lf-th:58px; --g-lf-gap:4px; }',
  '.g-lf-mosaic { display:flex; flex-direction:column; gap:var(--g-lf-gap);',
  '  padding:var(--g-lf-gap) 0; }',
  '.g-lf-row { will-change:transform; }',
  '.g-lf-strip { display:flex; gap:var(--g-lf-gap); width:max-content;',
  '  animation:g-lf-driftL var(--g-lf-dur,26s) linear infinite; }',
  '.g-lf-strip.g-lf-rev { animation-name:g-lf-driftR; }',
  '.g-lf-strip.g-lf-static { animation:none; }',
  '@keyframes g-lf-driftL { from { transform:translateX(0); }',
  '  to { transform:translateX(calc(-100% / var(--g-lf-reps,2))); } }',
  '@keyframes g-lf-driftR { from { transform:translateX(calc(-100% / var(--g-lf-reps,2))); }',
  '  to { transform:translateX(0); } }',

  /* --------------------------------- TILES ------------------------------- */
  '.g-lf-tile { position:relative; flex:0 0 auto; width:var(--g-lf-tw); height:var(--g-lf-th);',
  '  border-radius:8px; overflow:hidden; border:1px solid rgba(58,58,94,.6);',
  '  cursor:pointer; background:var(--navy); }',
  /* The skin carries the look signature (gradient + hue) so the engine's
     .ae-glitch-* classes - which set filter/transform on the TILE itself -
     never fight our own filter. One layer each, no clobbering. */
  '.g-lf-skin { position:absolute; inset:0; filter:hue-rotate(var(--g-lf-hue,0deg));',
  '  transition:opacity .18s ease; }',
  '.g-lf-media { width:100%; height:100%; object-fit:cover; display:block;',
  '  pointer-events:none; }',
  '.g-lf-g1 { background:linear-gradient(135deg,#3A2A55,#7A4A8F); }',
  '.g-lf-g2 { background:linear-gradient(160deg,#2A2A4E,#4E3A72); }',
  '.g-lf-g3 { background:linear-gradient(135deg,#5E2A55,#B84A8F); }',
  '.g-lf-g4 { background:linear-gradient(150deg,#22224A,#3E4E86); }',
  '.g-lf-g5 { background:linear-gradient(135deg,#4A2A3E,#8F4A6E); }',
  '.g-lf-g6 { background:linear-gradient(145deg,#33245C,#6E58B0); }',
  '.g-lf-g7 { background:linear-gradient(135deg,#412A62,#9A6ED8); }',
  '.g-lf-g8 { background:linear-gradient(150deg,#2E1F3E,#66397A); }',

  /* Sheen: the crowd is alive even before any media decodes (mockup .t::after). */
  '.g-lf-tile::after { content:""; position:absolute; inset:0; pointer-events:none;',
  '  background:linear-gradient(115deg, transparent 30%, rgba(242,235,221,.09) 45%, transparent 60%);',
  '  transform:translateX(-100%); animation:g-lf-sheen 5.5s ease-in-out infinite;',
  '  animation-delay:calc(var(--g-lf-i,0) * .37s); }',
  '@keyframes g-lf-sheen { 0%{transform:translateX(-100%)} 45%,100%{transform:translateX(100%)} }',

  '.g-lf-tile.g-lf-found { border-color:var(--pink); box-shadow:0 0 0 2px var(--pink); }',
  /* Pity pulse + near-twin "warm" shimmer: both grade-neutral hints. */
  '.g-lf-tile.g-lf-pity::before, .g-lf-tile.g-lf-warm::before { content:""; position:absolute;',
  '  inset:0; z-index:2; pointer-events:none; animation:g-lf-shimmer .3s linear 1;',
  '  background:linear-gradient(100deg, transparent 35%, rgba(242,235,221,.30) 50%, transparent 65%); }',
  '.g-lf-tile.g-lf-warm::before { animation-duration:.4s;',
  '  background:linear-gradient(100deg, transparent 35%, rgba(184,166,232,.35) 50%, transparent 65%); }',
  '@keyframes g-lf-shimmer { from{transform:translateX(-100%)} to{transform:translateX(100%)} }',
  /* Reduced motion: the hint becomes a static outline instead of a sweep. */
  'html.arc-reduced .g-lf-tile.g-lf-pity, html.arc-reduced .g-lf-tile.g-lf-warm {',
  '  outline:2px dashed var(--lav); outline-offset:-3px; }',

  /* -------------------------- OVERLAYS / CEREMONY ------------------------ */
  '.g-lf-dim { position:absolute; inset:0; z-index:5; pointer-events:none; opacity:0;',
  '  background:rgba(20,20,43,.6); transition:opacity .25s ease; }',
  '.g-lf-dim.on { opacity:1; }',
  '.g-lf-card { position:absolute; left:50%; top:50%; z-index:7;',
  '  transform:translate(-50%,-50%) scale(1); display:flex; flex-direction:column;',
  '  align-items:center; gap:8px; padding:12px; border-radius:12px;',
  '  background:var(--panel); border:1px solid var(--pink-deep);',
  '  box-shadow:0 10px 34px rgba(0,0,0,.5); text-align:center; }',
  '.g-lf-card .g-lf-art { position:relative; width:132px; height:118px; border-radius:8px;',
  '  overflow:hidden; background:linear-gradient(135deg,#5E2A55,#B84A8F); }',
  '.g-lf-card h4 { margin:0; font-family:var(--disp); font-size:13px; letter-spacing:.12em;',
  '  text-transform:uppercase; color:var(--pink); }',
  '.g-lf-card p { margin:0; font-size:11px; color:var(--ink-faint); max-width:210px; }',
  '.g-lf-card.g-lf-collapse { transition:transform .42s ease-in, opacity .42s ease-in;',
  '  transform:translate(-50%,-50%) scale(.25) rotate(-8deg); opacity:0; }',
  '.g-lf-card.g-lf-peekcard { z-index:9; opacity:.92; border-color:var(--gold); }',
  '.g-lf-spot { z-index:8; border-color:var(--pink); }',
  '.g-lf-spot .g-lf-art { width:180px; height:160px; box-shadow:0 0 26px rgba(255,105,180,.45); }',
  '.g-lf-spot h4 { color:var(--gold); }',
  /* Anchor for the SHARED shell stamp: .arc-stamp is inline-block, so handing it
     an in-flow host would shove the mosaic sideways every find. */
  '.g-lf-stamp { position:absolute; left:50%; top:16%; transform:translateX(-50%);',
  '  z-index:9; pointer-events:none; text-align:center; }',
  '.g-lf-taunt { position:absolute; left:50%; bottom:14px; transform:translateX(-50%);',
  '  z-index:9; font-family:var(--disp); font-size:11px; letter-spacing:.14em;',
  '  text-transform:uppercase; color:var(--pink); background:rgba(20,20,43,.85);',
  '  border:1px solid var(--pink-deep); border-radius:999px; padding:5px 14px;',
  '  pointer-events:none; }',

  /* ---------------------------------- FOOT ------------------------------- */
  '.g-lf-foot { display:flex; align-items:center; gap:10px; flex-wrap:wrap;',
  '  padding:9px 12px; background:var(--panel); border-top:1px solid var(--line); }',
  '.g-lf-foot .g-lf-hint { font-size:11px; color:var(--ink-faint); }',
  /* Touch: peek is a persistent thumb-reach control, not a foot button. */
  '.g-lf-view .g-lf-thumbpeek { position:absolute; right:10px; bottom:12px; z-index:9;',
  '  font-family:var(--disp); font-size:10px; letter-spacing:.1em; background:var(--pink);',
  '  color:var(--ground); border:0; border-radius:999px; padding:9px 14px;',
  '  box-shadow:0 4px 14px rgba(255,105,180,.4); touch-action:none; }',
].join('\n');

/** Inject once per document. Idempotent - re-entering the class is free. */
export function injectStyles() {
  if (typeof document === 'undefined' || !document.createElement) return false;
  try {
    if (document.getElementById && document.getElementById(STYLE_ID)) return false;
    const node = document.createElement('style');
    node.id = STYLE_ID;
    node.textContent = CSS;
    const head = document.head || document.documentElement || document.body;
    if (!head || !head.appendChild) return false;
    head.appendChild(node);
    // test-double hook: the scratch DOM shim indexes ids explicitly
    if (typeof document._register === 'function') document._register(STYLE_ID, node);
    return true;
  } catch (e) { return false; }
}

export default injectStyles;
