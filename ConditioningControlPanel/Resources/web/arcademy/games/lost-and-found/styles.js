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
  /* The find tally: one slot per find, stamped pink as each is claimed - until
     the class is longer than hud.js's SLOT_CAP (13-26 finds since the
     class-length wave), when the same strip becomes a ten-segment METER filled
     in proportion. Same slots, narrower and tighter, so the chip never grows. */
  '.g-lf-slots { display:flex; gap:4px; margin-top:3px; }',
  '.g-lf-slot { width:11px; height:13px; border-radius:2px; border:1px solid var(--line);',
  '  background:var(--flap-well); display:inline-block; }',
  '.g-lf-slots-meter { gap:2px; }',
  '.g-lf-slots-meter .g-lf-slot { width:7px; height:11px; border-radius:1px; }',
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
  /* THE MOBILE PASS: `dvh`, with the `vh` line kept above it as the fallback an
     engine without dvh reads and a newer one overwrites. On a phone `100vh` is
     the viewport WITH the browser bars retracted, so the mosaic was solving its
     row height against a frame taller than the one it is drawn in and the bottom
     row rode under the URL bar. Every other board in games/ already uses dvh. */
  '  --g-lf-th:calc((100vh - var(--g-lf-top) - (var(--g-lf-rows,4) + 1) * var(--g-lf-gap)) / var(--g-lf-rows,4));',
  '  --g-lf-th:calc((100dvh - var(--g-lf-top) - (var(--g-lf-rows,4) + 1) * var(--g-lf-gap)) / var(--g-lf-rows,4));',
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
  /* hue 0 rotates nothing, and `filter:none` spares the element the render
     surface a filter forces. One tile in seven, x2-3 wrap clones. */
  '.g-lf-skin.g-lf-h0 { filter:none; }',
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
  /* DENSE WALL (board.js adds .g-lf-dense past PLAYTEST.SHEEN_MAX_DENSITY): the
     sheen is one compositor animation per tile ELEMENT and a hard board carries
     ~400 of them for a decoration nobody can read at that size. `contain:paint`
     lets the compositor skip the wrap clones parked outside the strip's clip -
     the tile is already position:relative + overflow:hidden, so nothing it
     hosts (the almost overlay, the melt drip, engine glitch dressing) changes
     its box. The found rim is the tile's OWN box-shadow and still paints. */
  '.g-lf-mosaic.g-lf-dense .g-lf-tile::after { display:none; }',
  '.g-lf-mosaic.g-lf-dense .g-lf-tile { contain:paint; }',

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

  /* ------------------- TRICKSTER (House Rules, Deck III) ------------------ */
  /* THE MELT: the skin sags off its seat like heated wax and a drip runs off
     the bottom edge; at the animation midpoint trickster.js swaps the LOOK to
     another tile (the honest primitive) and this seat re-solidifies. The sag
     lives on .g-lf-skin (transform) - glitch_swap dresses the TILE, so the two
     never fight (same layer discipline as the hue filter). */
  '.g-lf-tile.g-lf-melting { z-index:2; }',
  '.g-lf-tile.g-lf-melting .g-lf-skin { transform-origin:50% 100%;',
  '  animation:g-lf-meltsag 1.15s cubic-bezier(.55,.05,.7,.4) 1 both; }',
  '@keyframes g-lf-meltsag {',
  '  0% { transform:scaleY(1); }',
  '  45% { transform:scaleY(1.07) translateY(3%); border-radius:0 0 40% 45%; }',
  '  100% { transform:scaleY(1.2) translateY(10%); opacity:.22; border-radius:0 0 58% 62%; } }',
  '.g-lf-tile.g-lf-melting::before { content:""; position:absolute; z-index:2;',
  '  left:22%; bottom:-16%; width:9%; height:30%; pointer-events:none;',
  '  border-radius:45% 45% 60% 60%;',
  '  background:linear-gradient(180deg, rgba(255,105,180,.55), rgba(255,105,180,0));',
  '  animation:g-lf-drip 1.15s ease-in 1 both; }',
  '@keyframes g-lf-drip {',
  '  0% { transform:translateY(-70%) scaleY(.3); opacity:0; }',
  '  35% { opacity:.85; }',
  '  100% { transform:translateY(130%) scaleY(1.2); opacity:0; } }',
  /* The receiving seat rises out of the liquid. Transform only - the skin owns
     the hue filter in its base rule and keyframing filter would drop it. */
  '.g-lf-tile.g-lf-reform .g-lf-skin { transform-origin:50% 100%;',
  '  animation:g-lf-reform .7s cubic-bezier(.2,1.2,.4,1) 1; }',
  '@keyframes g-lf-reform {',
  '  0% { transform:scaleY(.72) translateY(14%); opacity:.4; }',
  '  100% { transform:scaleY(1) translateY(0); opacity:1; } }',

  /* GHOST CURSOR: a will-o-wisp cursor echo. pointer-events:none is LAW - it
     is suggestion, never a control; it sits UNDER the dim and every card. */
  '.g-lf-ghost { position:absolute; left:0; top:0; z-index:4; width:15px; height:19px;',
  '  pointer-events:none; opacity:0; will-change:transform;',
  '  clip-path:polygon(0 0, 0 82%, 22% 64%, 38% 100%, 50% 94%, 34% 60%, 60% 60%);',
  '  background:linear-gradient(160deg, rgba(255,105,180,.9), rgba(184,166,232,.55));',
  '  filter:drop-shadow(0 0 6px rgba(255,105,180,.6));',
  '  transition:transform .38s ease-out, opacity .4s ease; }',
  '.g-lf-ghost.g-lf-ghost-on { opacity:.32; }',
  '.g-lf-ghost.g-lf-ghost-lure { opacity:.5;',
  '  animation:g-lf-ghostpulse 1.6s ease-in-out infinite; }',
  '@keyframes g-lf-ghostpulse {',
  '  50% { filter:drop-shadow(0 0 11px rgba(255,105,180,.95)); } }',

  /* GLITCH-TO-ASSET: a beat of the player\'s own library wearing the chrome\'s
     seat. Overlay on the hud wrap, never inside a control; cannot take a click. */
  '.g-lf-chromeglitch { position:absolute; z-index:10; pointer-events:none;',
  '  overflow:hidden; border-radius:8px; border:1px solid var(--pink-deep);',
  '  box-shadow:0 0 18px rgba(255,105,180,.4);',
  '  animation:g-lf-chromejit .13s steps(2) 1; }',
  '.g-lf-chromeglitch img { width:100%; height:100%; object-fit:cover; display:block;',
  '  filter:saturate(1.35) contrast(1.15); }',
  '@keyframes g-lf-chromejit {',
  '  0% { transform:translate(1px,-1px) skewX(2deg); }',
  '  50% { transform:translate(-1px,1px) skewX(-2deg); }',
  '  100% { transform:translate(0,0); } }',

  /* --------------------- CASINO (House Rules, Deck II) -------------------- */
  /* THE MARQUEE: a bulb-chase frame around the wall. Dots are gradients, the
     chase is a background-position crawl - four thin bars, cheap to paint.
     pointer-events:none is LAW; it sits with the vignette (over tiles, under
     every card and chip). Pace (--g-lf-mqt) and presence (--g-lf-mqa) ride the
     class heat from casino.js; the bell turns it gold and outbids heat. */
  '.g-lf-mq { position:absolute; left:0; right:0; bottom:0; top:var(--g-lf-top);',
  '  z-index:5; pointer-events:none; opacity:var(--g-lf-mqa,.3);',
  '  transition:opacity .6s ease; }',
  '.g-lf-mq i { position:absolute; display:block;',
  '  background-image:radial-gradient(circle, var(--g-lf-mqc,var(--pink)) 2.1px, transparent 3.2px); }',
  '.g-lf-mq .mq-t, .g-lf-mq .mq-b { left:0; right:0; height:7px;',
  '  background-size:17px 7px; background-repeat:repeat-x; }',
  '.g-lf-mq .mq-l, .g-lf-mq .mq-r { top:0; bottom:0; width:7px;',
  '  background-size:7px 17px; background-repeat:repeat-y; }',
  /* The chase runs AROUND the frame: top ->, right v, bottom <-, left ^. The
     seeded --g-lf-mqp phase means it never opens on the same bulb twice. */
  '.g-lf-mq .mq-t { top:0; animation:g-lf-mqx var(--g-lf-mqt,1.8s) linear infinite var(--g-lf-mqp,0s); }',
  '.g-lf-mq .mq-r { right:0; animation:g-lf-mqy var(--g-lf-mqt,1.8s) linear infinite var(--g-lf-mqp,0s); }',
  '.g-lf-mq .mq-b { bottom:0; animation:g-lf-mqxr var(--g-lf-mqt,1.8s) linear infinite var(--g-lf-mqp,0s); }',
  '.g-lf-mq .mq-l { left:0; animation:g-lf-mqyr var(--g-lf-mqt,1.8s) linear infinite var(--g-lf-mqp,0s); }',
  '@keyframes g-lf-mqx { to { background-position-x:17px; } }',
  '@keyframes g-lf-mqxr { to { background-position-x:-17px; } }',
  '@keyframes g-lf-mqy { to { background-position-y:17px; } }',
  '@keyframes g-lf-mqyr { to { background-position-y:-17px; } }',
  /* The final bell: gold and glowing until the class ends. */
  '.g-lf-mq.g-lf-mq-bell { --g-lf-mqc:var(--gold);',
  '  filter:drop-shadow(0 0 6px rgba(245,193,92,.75)); }',
  /* A find pays light: one pulse, brighter up the ladder (--g-lf-mqf). */
  '.g-lf-mq.g-lf-mq-flash { animation:g-lf-mqflash .6s ease-out 1; }',
  '@keyframes g-lf-mqflash {',
  '  0% { opacity:1; filter:brightness(var(--g-lf-mqf,1.4)) drop-shadow(0 0 10px rgba(255,105,180,.9)); }',
  '  100% { opacity:var(--g-lf-mqa,.3); filter:none; } }',
  /* A loss sighs out; it never cuts to silence. */
  '.g-lf-mq.g-lf-mq-out { opacity:0; transition:opacity 1.4s ease; }',
  /* Reduced motion: the shell freezes the crawl; we keep a quiet static frame. */
  'html.arc-reduced .g-lf-mq { opacity:.2; }',

  /* THE ALMOST: a warm click ghosts the target\'s look through the clicked
     twin with a slot-reel settle. Inside the seat (overflow hides it), over
     the skin, under the marks; cannot take a click. */
  '.g-lf-almost { position:absolute; inset:0; z-index:3; pointer-events:none;',
  '  overflow:hidden; border-radius:8px;',
  '  animation:g-lf-almostin .68s ease-out 1 both; }',
  '@keyframes g-lf-almostin {',
  '  0% { opacity:0; transform:translateY(16%); }',
  '  30% { opacity:.55; transform:translateY(-3%); }',
  '  60% { opacity:.48; transform:translateY(1%); }',
  '  100% { opacity:0; transform:translateY(0); } }',

  /* ------------------------- CLASS RULES SHEET ---------------------------- */
  /* The how-to polaroid: three drawn vignettes, captions second (Law IV).
     Every animation is CSS, compositor-only (transform / box-shadow pulse on
     tiny nodes), and the sheet reads fine frozen (arc-reduced). */
  '.g-lf-howto { width:min(430px, 88vw); align-items:stretch; text-align:left; gap:6px; }',
  '.g-lf-howto h4 { text-align:center; margin-bottom:2px; }',
  '.g-lf-hw-row { display:flex; align-items:center; gap:14px; padding:8px 4px; }',
  '.g-lf-hw-row + .g-lf-hw-row { border-top:1px dashed rgba(58,58,94,.55); }',
  '.g-lf-hw-row p { margin:0; font-size:12px; line-height:1.45; color:var(--ink-dim);',
  '  flex:1 1 auto; text-align:left; max-width:none; }',
  '.g-lf-hw-fig { flex:0 0 auto; display:flex; align-items:center; gap:8px; }',
  '.g-lf-hw-eq { font-family:var(--disp); font-size:14px; color:var(--pink); }',
  /* her polaroid */
  '.g-lf-hw-pol { display:flex; flex-direction:column; align-items:center; gap:3px;',
  '  padding:4px 4px 3px; border-radius:6px; transform:rotate(-2.5deg);',
  '  background:linear-gradient(165deg, var(--panel), var(--navy));',
  '  border:1px solid var(--pink-deep); box-shadow:0 4px 12px rgba(0,0,0,.5); }',
  '.g-lf-hw-polart { display:block; width:36px; height:32px; border-radius:4px;',
  '  overflow:hidden; position:relative; background:linear-gradient(135deg,#5E2A55,#B84A8F); }',
  '.g-lf-hw-pol small { font-size:8px; letter-spacing:.1em; text-transform:uppercase;',
  '  color:var(--pink); font-family:var(--disp); }',
  /* the drifting mini-wall; the marked tile wears HER look */
  '.g-lf-hw-wall { display:flex; flex-direction:column; gap:3px; width:104px;',
  '  overflow:hidden; padding:4px; border-radius:6px;',
  '  background:rgba(16,16,36,.75); border:1px solid var(--line); }',
  '.g-lf-hw-mrow { display:flex; gap:3px; width:max-content;',
  '  animation:g-lf-hw-slide 5.5s ease-in-out infinite alternate; }',
  '.g-lf-hw-mrow.g-lf-hw-rev { animation-name:g-lf-hw-slideR; animation-duration:4.2s; }',
  '@keyframes g-lf-hw-slide { from { transform:translateX(0); } to { transform:translateX(-16px); } }',
  '@keyframes g-lf-hw-slideR { from { transform:translateX(-16px); } to { transform:translateX(0); } }',
  '.g-lf-hw-tile { flex:0 0 auto; display:block; width:19px; height:15px; border-radius:3px;',
  '  border:1px solid rgba(58,58,94,.6); position:relative; overflow:hidden; }',
  '.g-lf-hw-mark { box-shadow:0 0 0 1.5px var(--pink), 0 0 8px rgba(255,105,180,.7);',
  '  animation:g-lf-hw-pulse 1.6s ease-in-out infinite; }',
  '@keyframes g-lf-hw-pulse {',
  '  0%,100% { box-shadow:0 0 0 1.5px var(--pink), 0 0 6px rgba(255,105,180,.5); }',
  '  50% { box-shadow:0 0 0 2px var(--pink), 0 0 15px rgba(255,105,180,.95); } }',
  /* the tally: two banked, the third mid-stamp */
  '.g-lf-hw-slots { display:flex; gap:5px; padding:2px 6px; }',
  '.g-lf-hw-slot { width:15px; height:18px; border-radius:3px; display:inline-block;',
  '  border:1px solid var(--line); background:var(--flap-well); }',
  '.g-lf-hw-slot.on { background:var(--pink); border-color:var(--pink);',
  '  box-shadow:0 0 7px rgba(255,105,180,.7); }',
  '.g-lf-hw-slot.next { animation:g-lf-hw-stamp 1.5s ease-in-out infinite; }',
  '@keyframes g-lf-hw-stamp {',
  '  0%,30% { background:var(--flap-well); border-color:var(--line); box-shadow:none; }',
  '  55%,100% { background:var(--pink); border-color:var(--pink); box-shadow:0 0 8px rgba(255,105,180,.8); } }',
  /* the peek keycap */
  '.g-lf-hw-key { flex:0 0 auto; min-width:46px; text-align:center; padding:7px 11px;',
  '  border-radius:7px; font-family:var(--mono); font-size:12px; color:var(--ink);',
  '  background:linear-gradient(180deg, var(--panel), var(--navy));',
  '  border:1px solid var(--line); border-bottom-width:3px;',
  '  box-shadow:0 2px 0 rgba(0,0,0,.4); }',
  /* the one way out */
  '.g-lf-hw-go { align-self:center; margin-top:4px; padding:9px 28px; cursor:pointer;',
  '  border-radius:999px; font-family:var(--disp); font-size:13px; letter-spacing:.16em;',
  '  text-transform:uppercase; color:#fff;',
  '  background:linear-gradient(135deg, var(--pink), var(--pink-deep));',
  '  border:1px solid var(--pink); box-shadow:0 0 22px rgba(255,105,180,.35); }',
  '.g-lf-hw-go:hover { box-shadow:0 0 32px rgba(255,105,180,.6); }',

  /* KEN-BURNS (Law III): the MEDIA inside a seat drifts; the seat, the skin
     (hue filter / melt transform) and the hitbox never move. Phase staggers
     off the tile index; the period is seeded per class (--g-lf-kbdur). */
  '.g-lf-kb .g-lf-media { animation:g-lf-kb var(--g-lf-kbdur,18s) ease-in-out infinite alternate;',
  '  animation-delay:calc(var(--g-lf-i,0) * -1.7s); }',
  '@keyframes g-lf-kb {',
  '  from { transform:scale(1.01) translate(0,0); }',
  '  to { transform:scale(1.08) translate(2.2%,-1.8%); } }',

  /* THE STICKY WAY PAST THE SHEET (shell exits wave). Alone among the ten this
     polaroid never declared a bound of any kind, and it lives inside
     .g-lf-view, which clips - so a sheet taller than the view was simply
     guillotined and GO went with it. The class could not be entered and nothing
     on the page said why. Bound it, let it scroll, and pin GO to the bottom of
     the card as a full-width bar. */
  '.g-lf-howto { max-height:calc(100% - 26px); overflow:auto; }',
  '.g-lf-hw-go { position:sticky; bottom:0; z-index:3; align-self:stretch;',
  '  margin-top:10px; }',

  /* --------------------------- MOBILE (arc-mobile) ------------------------ */
  /* On a phone the SHELL already shifts .arc-classroot down by
     --arc-mobile-chrome-h (styles.css), so the proctor strip lives OUTSIDE our
     stage - yet the game still reserved its own 52px band and then pinned the
     HUD 10px BELOW it, covering ~25% of the board on a 390px-tall landscape
     phone while the band sat empty. The fix is placement, not structure: the
     HUD moves INTO the band (zero board overlap) and the chip compacts to fit
     a 46px strip (44px thumb floor + 2; the chip has no button, nothing here
     is a tap target). The chip stays IN THE GAME TREE - hud.chromeEls(), the
     ceremonies' stamp anchors and the trickster's chrome flicker all assume
     it lives here, so it is never reparented into .arc-proctor. */
  'html.arc-mobile .g-lf { --g-lf-top:46px; }',
  'html.arc-mobile .g-lf-hud { top:0; left:8px; right:8px; height:var(--g-lf-top);',
  '  align-items:center; flex-wrap:nowrap; gap:6px; }',
  /* Compact evidence chip: no tilt, no pin, tighter art. */
  'html.arc-mobile .g-lf-tchip { transform:none; padding:4px 9px 4px 4px; gap:7px;',
  '  border-radius:8px; }',
  'html.arc-mobile .g-lf-tchip::before { display:none; }',
  'html.arc-mobile .g-lf-tchip .g-lf-tt { width:34px; height:30px; }',
  /* The tally strip goes; the <b> numerals ("4 / 26") carry the exact truth. */
  'html.arc-mobile .g-lf-slots { display:none; }',
  /* The word-label chip (the "Streak" caption) does not fit the band; the
     streak METER itself mounts on .g-lf-streak (not a .chip) and the clock is
     .chip.num - both survive this rule. */
  'html.arc-mobile .g-lf-hud .chip:not(.num) { display:none; }',
  /* ROW MATH: the shell's chrome strip eats --arc-mobile-chrome-h of the
     100dvh the desktop formula solves against (it is defined on :root, so it
     is readable here), which left the mosaic ~48px too tall and clipped the
     bottom row. Same vh-fallback-then-dvh shape as the base rule; the desktop
     formula above is untouched. */
  'html.arc-mobile .g-lf-view {',
  '  --g-lf-th:calc((100vh - var(--arc-mobile-chrome-h, 0px) - var(--g-lf-top) - (var(--g-lf-rows,4) + 1) * var(--g-lf-gap)) / var(--g-lf-rows,4));',
  '  --g-lf-th:calc((100dvh - var(--arc-mobile-chrome-h, 0px) - var(--g-lf-top) - (var(--g-lf-rows,4) + 1) * var(--g-lf-gap)) / var(--g-lf-rows,4)); }',

  /* --------------------- THE PHONE CEILING (html.ae-touch) ----------------- */
  /* The shell arms .ae-touch on <html> for coarse-pointer devices at boot. This
     wall is the heaviest surface in the school - dozens of live media seats -
     and it was wearing a hue-rotate filter over every one of them plus four
     live backdrop blurs and two keyframes that repaint a drop-shadow. The wall
     itself is the content; the tint, the frosted glass and the glow were the
     nicety. Nothing here hides a mark, a hint or a control: the dim still dims
     (opaque paint instead of blur), the lure ghost still pulses (on opacity),
     the sheet's two hint loops freeze at their LIT frame. Desktop untouched. */
  /* The hue skin: one filtered render surface per seat, x2-3 wrap clones. */
  'html.ae-touch .g-lf-skin { filter:none; }',
  /* Frosted chrome turns solid - a live blur behind glass is the phone tax. */
  'html.ae-touch .g-lf-hud .chip { backdrop-filter:none; -webkit-backdrop-filter:none;',
  '  background:rgba(20,20,43,.94); }',
  'html.ae-touch .g-lf-foot .arc-peekbtn { backdrop-filter:none; -webkit-backdrop-filter:none;',
  '  background:rgba(37,37,66,.97); }',
  'html.ae-touch .g-lf-foot .chip { backdrop-filter:none; -webkit-backdrop-filter:none;',
  '  background:rgba(20,20,43,.94); }',
  /* The ceremony dim blurred the WHOLE wall behind it; paint carries the dim. */
  'html.ae-touch .g-lf-dim { backdrop-filter:none; -webkit-backdrop-filter:none;',
  '  background:rgba(20,20,43,.78); }',
  /* The ghost cursor: drop-shadow on a moving node, and the lure keyframed it. */
  'html.ae-touch .g-lf-ghost { filter:none; }',
  'html.ae-touch .g-lf-ghost.g-lf-ghost-lure { animation:g-lf-ghostpulse-t 1.6s ease-in-out infinite; }',
  '@keyframes g-lf-ghostpulse-t { 50% { opacity:.72; } }',
  /* The bell marquee wore a drop-shadow over four infinitely crawling bars,
     so every frame of the chase re-rasterised the glow. Gold still reads. */
  'html.ae-touch .g-lf-mq.g-lf-mq-bell { filter:none; }',
  'html.ae-touch .g-lf-mq.g-lf-mq-flash { animation:g-lf-mqflash-t .6s ease-out 1; }',
  '@keyframes g-lf-mqflash-t { 0% { opacity:1; } 100% { opacity:var(--g-lf-mqa,.3); } }',
  /* The rules sheet: two infinite box-shadow loops freeze lit (as arc-reduced). */
  'html.ae-touch .g-lf-hw-mark { animation:none;',
  '  box-shadow:0 0 0 2px var(--pink), 0 0 15px rgba(255,105,180,.95); }',
  'html.ae-touch .g-lf-hw-slot.next { animation:none; background:var(--pink);',
  '  border-color:var(--pink); box-shadow:0 0 8px rgba(255,105,180,.8); }',
  /* Reduced motion outranks by order on desktop; the twins are new names, so
     say the freeze once more for the two that touch re-armed. */
  'html.arc-reduced.ae-touch .g-lf-ghost.g-lf-ghost-lure,',
  'html.arc-reduced.ae-touch .g-lf-mq.g-lf-mq-flash { animation:none !important; }',
  '@media (prefers-reduced-motion: reduce) {',
  '  html.ae-touch .g-lf-ghost.g-lf-ghost-lure,',
  '  html.ae-touch .g-lf-mq.g-lf-mq-flash { animation:none !important; } }',
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
