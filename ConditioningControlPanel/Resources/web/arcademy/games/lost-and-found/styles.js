/* ============================================================================
 * games/lost-and-found/styles.js - the game's own CSS, self-injected once.
 *
 * styles.css is SHELL CHROME ONLY ("Games own their own CSS ... so a game
 * restyle can never regress the board or the report card"), so everything here
 * is namespaced `.g-lf-*` and every colour is a var() off a shell token - which
 * is what makes the mod palette (init.palette -> :root custom properties) skin
 * this board for free.
 *
 * THE WALL IS THE WINDOW (game-immersion wave, owner mandate): the class root
 * is a full-viewport stage now, so the mosaic bleeds edge to edge and every
 * piece of chrome floats OVER the wall as a diegetic object - the target chip
 * is a pinned evidence polaroid, misses/peeks are evidence tags, the briefing
 * and spotlight are bigger polaroids. Tile COUNT never changes with the window
 * (density is a tuned dial); tile SIZE does: board.js publishes --g-lf-rows and
 * the CSS below solves the tile height so the rows always fill the frame.
 * A slim translucent proctor strip (~52px, shell-owned) crosses the top, so the
 * wall starts under it (--g-lf-top) - tiles are click targets and may never
 * hide under chrome.
 *
 * INPUT TRUST: the floating HUD and foot are pointer-events:none containers;
 * only their buttons/chips re-enable pointer events. A decorative layer can
 * never steal a click from a tile.
 *
 * MOTION CONTRACT: every animation here is a CSS animation or transition, so the
 * shell's reduced-motion rule (html.arc-reduced *) freezes ALL of it without this
 * file knowing - and index.js additionally swaps continuous drift for discrete
 * row steps. Nothing is required for legibility; the board is fully readable
 * frozen.
 * ==========================================================================*/

const STYLE_ID = 'g-lf-style';

export const CSS = [
  /* ------------------------------- STAGE --------------------------------- */
  /* Fills whatever root the shell hands us: the fullscreen class stage, or the
     legacy 420px class box (position:absolute composes with both). */
  '.g-lf { position:absolute; inset:0; overflow:hidden;',
  '  --g-lf-top:52px; --g-lf-gap:5px;',
  '  background:var(--ground);',
  '  background-image:',
  '    radial-gradient(1200px 600px at 80% -10%, rgba(255,105,180,.06), transparent 60%),',
  '    radial-gradient(1000px 700px at 8% 110%, rgba(184,166,232,.05), transparent 60%); }',
  /* Edge vignette: depth without a frame. Under the cards, over the tiles. */
  '.g-lf::after { content:""; position:absolute; inset:0; z-index:4; pointer-events:none;',
  '  background:radial-gradient(130% 115% at 50% 45%, transparent 62%, rgba(0,0,0,.42) 100%); }',

  /* ---------------------------- FLOATING HUD ------------------------------ */
  '.g-lf-hud { position:absolute; top:calc(var(--g-lf-top) + 10px); left:14px; right:14px;',
  '  z-index:6; display:flex; align-items:flex-start; gap:10px; flex-wrap:wrap;',
  '  pointer-events:none; }',
  '.g-lf-hud .chip { pointer-events:auto; background:rgba(20,20,43,.82);',
  '  backdrop-filter:blur(4px); box-shadow:0 4px 16px rgba(0,0,0,.45); }',
  /* The evidence polaroid: pinned to the wall, slightly askew. */
  '.g-lf-tchip { pointer-events:auto; position:relative; display:flex; align-items:center;',
  '  gap:9px; padding:7px 12px 7px 7px; transform:rotate(-1.6deg);',
  '  background:linear-gradient(165deg, var(--panel), var(--navy));',
  '  border:1px solid var(--pink-deep); border-radius:10px;',
  '  box-shadow:0 10px 28px rgba(0,0,0,.55), inset 0 1px 0 rgba(255,255,255,.07); }',
  '.g-lf-tchip::before { content:""; position:absolute; top:-5px; left:50%;',
  '  width:9px; height:9px; border-radius:50%; transform:translateX(-50%);',
  '  background:var(--pink); box-shadow:0 0 8px var(--pink), inset 0 -2px 3px rgba(0,0,0,.5); }',
  '.g-lf-tchip .g-lf-tt { width:46px; height:42px; border-radius:6px; overflow:hidden;',
  '  flex:0 0 auto; background:linear-gradient(135deg,#5E2A55,#B84A8F); position:relative;',
  '  border:1px solid rgba(58,58,94,.8); }',
  '.g-lf-tchip small { font-size:10px; letter-spacing:.12em; text-transform:uppercase;',
  '  color:var(--pink); font-family:var(--disp); display:block; }',
  '.g-lf-tchip b { font-family:var(--mono); font-size:12px; color:var(--ink); }',
  /* The find tally: one slot per find, stamped pink as each is claimed. */
  '.g-lf-slots { display:flex; gap:4px; margin-top:3px; }',
  '.g-lf-slot { width:11px; height:13px; border-radius:2px; border:1px solid var(--line);',
  '  background:var(--flap-well); display:inline-block; }',
  '.g-lf-slot.on { background:var(--pink); border-color:var(--pink);',
  '  box-shadow:0 0 7px rgba(255,105,180,.7); animation:g-lf-slotpop .35s cubic-bezier(.2,1.5,.4,1) 1; }',
  '@keyframes g-lf-slotpop { 0% { transform:scale(.4); } 60% { transform:scale(1.25); } 100% { transform:scale(1); } }',
  '.g-lf-spacer { flex:1 1 auto; }',

  /* --------------------------------- BOARD ------------------------------- */
  /* The wall: bleeds to every edge below the proctor strip. Tile height is
     solved from the row count so the rows always fill the frame; width keeps a
     landscape tile ratio. Same tile COUNT as ever - only the size breathes. */
  '.g-lf-view { position:absolute; left:0; right:0; bottom:0; top:var(--g-lf-top);',
  '  overflow:hidden; background:transparent;',
  '  --g-lf-th:calc((100vh - var(--g-lf-top) - (var(--g-lf-rows,4) + 1) * var(--g-lf-gap)) / var(--g-lf-rows,4));',
  '  --g-lf-tw:calc(var(--g-lf-th) * 1.15); }',
  '.g-lf-view.g-lf-lite { --g-lf-tw:calc(var(--g-lf-th) * 1.3); }',
  '.g-lf-mosaic { position:absolute; inset:0; display:flex; flex-direction:column;',
  '  gap:var(--g-lf-gap); padding:var(--g-lf-gap) 0; }',
  '.g-lf-row { will-change:transform; flex:1 1 0; min-height:0; }',
  '.g-lf-strip { display:flex; gap:var(--g-lf-gap); width:max-content; height:100%;',
  '  animation:g-lf-driftL var(--g-lf-dur,26s) linear infinite; }',
  '.g-lf-strip.g-lf-rev { animation-name:g-lf-driftR; }',
  '.g-lf-strip.g-lf-static { animation:none; }',
  '@keyframes g-lf-driftL { from { transform:translateX(0); }',
  '  to { transform:translateX(calc(-100% / var(--g-lf-reps,2))); } }',
  '@keyframes g-lf-driftR { from { transform:translateX(calc(-100% / var(--g-lf-reps,2))); }',
  '  to { transform:translateX(0); } }',

  /* --------------------------------- TILES ------------------------------- */
  '.g-lf-tile { position:relative; flex:0 0 auto; width:var(--g-lf-tw); height:100%;',
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

  /* THE CLAIM: found tile pops and burns a pink rim. One-shot; long over before
     the next relocation could dress this tile with a transform of its own. */
  '.g-lf-tile.g-lf-found { border-color:var(--pink); z-index:3;',
  '  box-shadow:0 0 0 2px var(--pink), 0 0 26px rgba(255,105,180,.75);',
  '  animation:g-lf-claim .5s cubic-bezier(.2,1.4,.4,1) 1; }',
  '@keyframes g-lf-claim {',
  '  0% { transform:scale(1); box-shadow:0 0 0 0 rgba(255,105,180,.9); }',
  '  40% { transform:scale(1.1); box-shadow:0 0 0 10px rgba(255,105,180,.25), 0 0 30px rgba(255,105,180,.8); }',
  '  100% { transform:scale(1); } }',
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
  '  background:rgba(20,20,43,.62); backdrop-filter:blur(1px); transition:opacity .25s ease; }',
  '.g-lf-dim.on { opacity:1; }',
  /* Cards are polaroids now: framed art, a pin, a small rotation, a drop-in. */
  '.g-lf-card { position:absolute; left:50%; top:50%; z-index:7;',
  '  transform:translate(-50%,-50%) rotate(-1.2deg); display:flex; flex-direction:column;',
  '  align-items:center; gap:10px; padding:14px 14px 12px; border-radius:12px;',
  '  background:linear-gradient(170deg, var(--panel), var(--navy));',
  '  border:1px solid var(--pink-deep);',
  '  box-shadow:0 26px 70px rgba(0,0,0,.65), 0 0 40px rgba(255,105,180,.12);',
  '  text-align:center; animation:g-lf-cardin .45s cubic-bezier(.2,1.3,.4,1) both; }',
  '@keyframes g-lf-cardin {',
  '  from { transform:translate(-50%,-64%) rotate(-4deg) scale(.85); opacity:0; }',
  '  to { transform:translate(-50%,-50%) rotate(-1.2deg) scale(1); opacity:1; } }',
  '.g-lf-card::before { content:""; position:absolute; top:-6px; left:50%;',
  '  width:11px; height:11px; border-radius:50%; transform:translateX(-50%);',
  '  background:var(--pink); box-shadow:0 0 10px var(--pink), inset 0 -2px 3px rgba(0,0,0,.5); }',
  '.g-lf-card .g-lf-art { position:relative; width:210px; height:184px; border-radius:8px;',
  '  overflow:hidden; background:linear-gradient(135deg,#5E2A55,#B84A8F);',
  '  border:1px solid rgba(58,58,94,.8); }',
  '.g-lf-card h4 { margin:0; font-family:var(--disp); font-size:14px; letter-spacing:.14em;',
  '  text-transform:uppercase; color:var(--pink); }',
  '.g-lf-card p { margin:0; font-size:12px; color:var(--ink-dim); max-width:230px; }',
  '.g-lf-card.g-lf-collapse { transition:transform .42s ease-in, opacity .42s ease-in;',
  '  transform:translate(-50%,-50%) scale(.22) rotate(-9deg); opacity:0; }',
  '.g-lf-card.g-lf-peekcard { z-index:9; opacity:.94; border-color:var(--gold); }',
  '.g-lf-card.g-lf-peekcard::before { background:var(--gold); box-shadow:0 0 10px var(--gold); }',
  '.g-lf-spot { z-index:8; border-color:var(--pink); }',
  '.g-lf-spot .g-lf-art { width:280px; height:240px; box-shadow:0 0 42px rgba(255,105,180,.5); }',
  '.g-lf-spot h4 { color:var(--gold); font-size:17px; }',
  /* Anchor for the SHARED shell stamp: .arc-stamp is inline-block, so handing it
     an in-flow host would shove the mosaic sideways every find. */
  '.g-lf-stamp { position:absolute; left:50%; top:16%; transform:translateX(-50%);',
  '  z-index:9; pointer-events:none; text-align:center; }',
  '.g-lf-taunt { position:absolute; left:50%; bottom:56px; transform:translateX(-50%);',
  '  z-index:9; font-family:var(--disp); font-size:13px; letter-spacing:.16em;',
  '  text-transform:uppercase; color:var(--pink); background:rgba(20,20,43,.88);',
  '  border:1px solid var(--pink-deep); border-radius:999px; padding:8px 20px;',
  '  pointer-events:none; box-shadow:0 0 22px rgba(255,105,180,.25);',
  '  animation:g-lf-tauntin .3s ease both; }',
  '@keyframes g-lf-tauntin { from { transform:translate(-50%,8px); opacity:0; }',
  '  to { transform:translate(-50%,0); opacity:1; } }',

  /* ---------------------------- FLOATING FOOT ----------------------------- */
  /* Evidence desk, bottom-left: peek button + the miss/peek tags + the hint.
     The container never takes a click; only its controls do (input trust). */
  '.g-lf-foot { position:absolute; left:14px; bottom:14px; right:14px; z-index:6;',
  '  display:flex; align-items:center; gap:10px; flex-wrap:wrap; pointer-events:none; }',
  '.g-lf-foot .arc-peekbtn { pointer-events:auto; background:rgba(37,37,66,.88);',
  '  backdrop-filter:blur(4px); box-shadow:0 6px 18px rgba(0,0,0,.5); }',
  '.g-lf-foot .chip { pointer-events:auto; background:rgba(20,20,43,.82);',
  '  backdrop-filter:blur(4px); transform:rotate(.8deg); }',
  '.g-lf-foot .chip + .chip { transform:rotate(-1deg); }',
  '.g-lf-foot .g-lf-hint { font-size:11px; color:var(--ink-faint);',
  '  background:rgba(20,20,43,.65); border-radius:999px; padding:4px 12px; }',
  /* Touch: peek is a persistent thumb-reach control, not a foot button. */
  '.g-lf-view .g-lf-thumbpeek { position:absolute; right:14px; bottom:16px; z-index:9;',
  '  font-family:var(--disp); font-size:10px; letter-spacing:.1em; background:var(--pink);',
  '  color:var(--ground); border:0; border-radius:999px; padding:11px 16px;',
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
