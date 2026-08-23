/* ============================================================================
 * games/sort/style.js - SORT injects its OWN stylesheet.
 *
 * styles.css is shell chrome ONLY. Everything this room paints ships here,
 * namespaced `.g-sort-*`, scoped under `.g-sort`, injected exactly once per
 * document, and concatenated with the setup door's sheet (setup-style.js, LOT
 * G2) so there is one style node for the whole class either way.
 *
 * NEVER WRITE A BACKTICK IN A CSS COMMENT IN THIS FILE (CLAUDE.md trap 37). It
 * ends the template literal, the page dies on a ReferenceError, and
 * node --check passes it happily. Three agents paid for that lesson in one day.
 *
 * THE ROOM IS THREE LAYERS AND THEY NEVER TRADE PLACES:
 *   z0  THE WALL     the collage of what you already sorted, dim and behind
 *   z2  THE STACK    three cards, and the top one is the only thing you touch
 *   z4  THE CHROME   the HUD rail, the rung ladder, the verdict word
 * The ring is drawn ON the top card, not over the stage, because it belongs to
 * THAT card: a ring floating in the middle of the room would still be closing
 * while the next card springs up.
 *
 * ONE NUMBER SIZES THE MIDDLE. `--sort-card-w` is the card width; the stack
 * offsets, the stamp size, the ring box and the swipe threshold's visual twin
 * all hang off it. Nobody may re-type its value: read the token.
 *
 * NEVER STILL. The stack BREATHES (1.015 over 3.5s), the faces ken-burns 1.00
 * to 1.06, the ring always turns. Under reduced motion every one of those stops
 * and the ring becomes three brightness STATES on `data-ripe`, which is why the
 * attribute is written even when the animation is running.
 * ==========================================================================*/

import { SETUP_CSS } from './setup-style.js';

export const STYLE_ID = 'g-sort-style';

export const SORT_CSS = `
.g-sort{position:absolute;inset:0;overflow:hidden;isolation:isolate;
  color:#EDEBFF;font-family:var(--body,system-ui,sans-serif);
  --sort-pink:#FF69B4;
  --sort-gold:#FFC94A;
  --sort-navy:#1A1A2E;
  --sort-panel:#252542;
  --sort-line:rgba(237,235,255,.14);
  --sort-card-w:min(42vh,min(340px,72vw));
  --sort-card-h:calc(var(--sort-card-w) * 1.38);
  --sort-dx:0px;
  --sort-tilt:0deg;
  --sort-a-yes:0;
  --sort-a-no:0;
  --sort-ring:1;
  --sort-bg-fade:.35;
  background:radial-gradient(120% 90% at 50% 8%,#232346 0%,var(--sort-navy) 62%,#141428 100%)}

/* ---------------------------------------------------------------- THE WALL */
.g-sort-wall{position:absolute;inset:0;z-index:0;opacity:0;
  transition:opacity 420ms ease;pointer-events:none}
.g-sort-wall.is-on{opacity:var(--sort-bg-fade)}
.g-sort-wall.is-bleed{opacity:1;transition:opacity 620ms ease}
.g-sort-wall-grid{position:absolute;inset:0;display:grid;gap:2px;
  grid-template-columns:repeat(var(--sort-wall-cols,8),1fr);
  grid-auto-rows:1fr;align-content:start}
.g-sort-wall-tile{position:relative;overflow:hidden;border-radius:3px;
  background:hsl(var(--sort-tile-h,320) 42% 22%)}
.g-sort-wall-tile.is-wrong{opacity:.4;filter:grayscale(.75)}
.g-sort-wall-face{position:absolute;inset:0;width:100%;height:100%;
  object-fit:cover;display:block}
.g-sort-wall-tile.thud{animation:g-sort-thud 340ms cubic-bezier(.2,1.5,.4,1) both}
@keyframes g-sort-thud{
  0%{transform:scale(1.5);opacity:0}
  62%{transform:scale(.94);opacity:1}
  100%{transform:scale(1);opacity:1}}

/* --------------------------------------------------------------- THE STACK */
.g-sort-stage{position:absolute;inset:0;z-index:2;display:grid;
  place-items:center;touch-action:none}
.g-sort-stack{position:relative;width:var(--sort-card-w);height:var(--sort-card-h);
  touch-action:none;-webkit-user-select:none;user-select:none;
  animation:g-sort-breath 3500ms ease-in-out infinite}
@keyframes g-sort-breath{
  0%,100%{transform:scale(1)}
  50%{transform:scale(1.015)}}

.g-sort-card{position:absolute;inset:0;border-radius:18px;overflow:hidden;
  background:hsl(var(--sort-card-h-hue,318) 38% 18%);
  border:1px solid var(--sort-line);
  box-shadow:0 18px 44px rgba(0,0,0,.55),0 2px 0 rgba(255,255,255,.05) inset;
  transform:translate3d(0,calc(var(--sort-depth,0) * 12px),0)
    scale(var(--sort-scale,1));
  transition:transform 200ms cubic-bezier(.2,1.4,.4,1),opacity 200ms ease;
  will-change:transform}
.g-sort-card[data-depth="0"]{z-index:3;cursor:grab}
.g-sort-card[data-depth="1"]{z-index:2;opacity:.92}
.g-sort-card[data-depth="2"]{z-index:1;opacity:.72}
.g-sort-card[data-depth="0"].is-held{cursor:grabbing;transition:none;
  transform:translate3d(var(--sort-dx),0,0) rotate(var(--sort-tilt))
    scale(var(--sort-scale,1))}
.g-sort-card.is-band{transition:transform 260ms cubic-bezier(.2,1.5,.4,1)}
.g-sort-card.is-gone{transition:transform 280ms cubic-bezier(.3,.1,.3,1),opacity 280ms ease;
  opacity:.15;pointer-events:none}
.g-sort-card.is-sink{transition:transform 260ms ease,opacity 260ms ease;opacity:0}

/* the face. A loop is a muted video, a still an img, and a card with neither
   is the drawn back: a seeded hue and a soft chevron weave. */
.g-sort-face{position:absolute;inset:0;width:100%;height:100%;
  object-fit:cover;display:block;
  animation:g-sort-kb 9000ms ease-in-out infinite alternate}
@keyframes g-sort-kb{
  0%{transform:scale(1)}
  100%{transform:scale(1.06)}}
.g-sort-back{position:absolute;inset:0;
  background:
    repeating-linear-gradient(135deg,rgba(255,255,255,.05) 0 8px,transparent 8px 16px),
    linear-gradient(160deg,hsl(var(--sort-back-h,318) 48% 26%),hsl(var(--sort-back-h2,268) 44% 16%))}
.g-sort-card[data-seen="1"] .g-sort-back{filter:brightness(1.08)}

/* ---------------------------------------------------------- THE TWO STAMPS */
.g-sort-stamp{position:absolute;top:9%;padding:.28em .6em;border-radius:10px;
  font-family:var(--disp,system-ui,sans-serif);font-weight:800;
  font-size:clamp(18px,4.2vh,34px);letter-spacing:.06em;line-height:1;
  display:flex;align-items:center;gap:.34em;pointer-events:none;
  border:3px solid currentColor;text-transform:uppercase;
  transform:rotate(-11deg);opacity:0}
.g-sort-stamp.yes{left:7%;color:var(--sort-pink);opacity:var(--sort-a-yes);
  text-shadow:0 0 18px rgba(255,105,180,.5)}
.g-sort-stamp.no{right:7%;color:#9FA3C8;transform:rotate(11deg);
  opacity:var(--sort-a-no)}
.g-sort-glyph{font-size:.86em;line-height:1}
.g-sort-card.is-gone .g-sort-stamp{opacity:1}
.g-sort-card.is-wrong .g-sort-stamp{color:#8B8FAE;text-shadow:none}

/* ----------------------------------------------------------- THE RIPE RING */
.g-sort-ringbox{position:absolute;inset:-10px;z-index:4;pointer-events:none}
.g-sort-ringbox svg{width:100%;height:100%;display:block;overflow:visible}
.g-sort-ring-track{fill:none;stroke:rgba(237,235,255,.14);stroke-width:3}
.g-sort-ring-arc{fill:none;stroke:var(--sort-pink);stroke-width:4;
  stroke-linecap:round;
  stroke-dasharray:var(--sort-ring-len,1000);
  stroke-dashoffset:calc(var(--sort-ring-len,1000) * (1 - var(--sort-ring,1)));
  filter:drop-shadow(0 0 6px rgba(255,105,180,.45));
  transition:stroke 160ms linear}
.g-sort-ringbox[data-ripe="ripe"] .g-sort-ring-arc{stroke:var(--sort-gold);
  filter:drop-shadow(0 0 10px rgba(255,201,74,.65))}
.g-sort-ringbox[data-ripe="just"] .g-sort-ring-arc{stroke:#FFF0B8;
  filter:drop-shadow(0 0 14px rgba(255,240,184,.85))}

/* --------------------------------------------------------------- THE CHROME */
.g-sort-hud{position:absolute;left:0;right:0;top:0;z-index:4;
  display:flex;align-items:center;gap:14px;padding:10px 16px;
  pointer-events:none;font-family:var(--mono,ui-monospace,monospace);
  font-size:clamp(11px,1.5vh,14px);letter-spacing:.08em;text-transform:uppercase}
.g-sort-chip{display:flex;align-items:baseline;gap:.5em;padding:.34em .7em;
  border-radius:999px;background:rgba(37,37,66,.72);
  border:1px solid var(--sort-line);color:#B9B6DC}
.g-sort-chip b{color:#EDEBFF;font-size:1.32em;font-weight:700;
  font-variant-numeric:tabular-nums}
.g-sort-chip.is-chain b{color:var(--sort-pink)}
.g-sort-chip.is-chain.is-hot b{color:var(--sort-gold);
  text-shadow:0 0 12px rgba(255,201,74,.55)}
.g-sort-spacer{flex:1 1 auto}

/* the rung ladder: nine pips, and the lit one is where you are standing */
.g-sort-ladder{display:flex;align-items:center;gap:4px;pointer-events:none}
.g-sort-rung{width:9px;height:9px;border-radius:2px;
  background:rgba(237,235,255,.16);transform:rotate(45deg);
  transition:background 220ms ease,box-shadow 220ms ease,transform 220ms ease}
.g-sort-rung.on{background:var(--sort-pink)}
.g-sort-rung.at{background:var(--sort-gold);transform:rotate(45deg) scale(1.42);
  box-shadow:0 0 12px rgba(255,201,74,.7)}
.g-sort-rung.capped{opacity:.28}
.g-sort-ladder.is-fading .g-sort-rung.on{transition:background 1500ms ease}

/* the marquee halo behind the stack: it crawls at rung 0 and chases from 6 */
.g-sort-halo{position:absolute;z-index:1;width:calc(var(--sort-card-w) * 1.9);
  height:calc(var(--sort-card-w) * 1.9);border-radius:50%;
  background:radial-gradient(circle,rgba(255,105,180,.16) 0%,rgba(255,105,180,.05) 46%,transparent 70%);
  opacity:calc(.25 + var(--sort-rung,0) * .085);pointer-events:none;
  animation:g-sort-crawl 7000ms linear infinite}
.g-sort[data-chase="1"] .g-sort-halo{animation-duration:2400ms}
@keyframes g-sort-crawl{
  0%{transform:rotate(0deg) scale(1)}
  50%{transform:rotate(180deg) scale(1.06)}
  100%{transform:rotate(360deg) scale(1)}}

/* the verdict word: one hero per beat, and it never blocks the next card */
.g-sort-word{position:absolute;left:50%;top:calc(50% + var(--sort-card-h) * .58);
  transform:translate(-50%,0);z-index:5;pointer-events:none;
  font-family:var(--disp,system-ui,sans-serif);font-weight:800;
  font-size:clamp(16px,2.6vh,26px);letter-spacing:.14em;text-transform:uppercase;
  opacity:0;color:var(--sort-pink)}
.g-sort-word.show{animation:g-sort-word 900ms ease-out both}
.g-sort-word[data-tone="gold"]{color:var(--sort-gold)}
.g-sort-word[data-tone="grey"]{color:#8B8FAE}
@keyframes g-sort-word{
  0%{opacity:0;transform:translate(-50%,10px) scale(.9)}
  22%{opacity:1;transform:translate(-50%,0) scale(1.04)}
  70%{opacity:1;transform:translate(-50%,0) scale(1)}
  100%{opacity:0;transform:translate(-50%,-8px) scale(1)}}

/* SHIVER: 250ms, the whole stage, and it is the only thing a wrong swipe does
   to the room. It never blocks input and the next card is already grabbable. */
.g-sort-stage.is-shiver{animation:g-sort-shiver 250ms ease-in-out}
@keyframes g-sort-shiver{
  0%,100%{transform:translate3d(0,0,0)}
  20%{transform:translate3d(-7px,1px,0)}
  45%{transform:translate3d(6px,-2px,0)}
  70%{transform:translate3d(-4px,1px,0)}}

/* --------------------------------------------------------- THE RULES SHEET */
.g-sort-howto{position:absolute;inset:0;z-index:8;display:grid;place-items:center;
  background:rgba(14,14,30,.9)}
.g-sort-howto-card{width:min(560px,92vw);padding:26px 26px 18px;border-radius:18px;
  background:linear-gradient(180deg,#2A2A50,#1E1E3A);
  border:1px solid var(--sort-line);box-shadow:0 26px 60px rgba(0,0,0,.6);
  display:flex;flex-direction:column;gap:16px;align-items:center;text-align:center}
.g-sort-howto-h{font-family:var(--disp,system-ui,sans-serif);font-weight:800;
  font-size:clamp(20px,3vh,28px);letter-spacing:.06em;text-transform:uppercase}
/* the drawn card: one mock card between two arrows, and that is the rulebook */
.g-sort-demo{display:flex;align-items:center;gap:18px;width:100%;
  justify-content:center}
.g-sort-demo-card{width:104px;height:144px;border-radius:12px;flex:0 0 auto;
  background:linear-gradient(160deg,hsl(318 48% 30%),hsl(268 44% 20%));
  border:1px solid var(--sort-line);
  box-shadow:0 10px 26px rgba(0,0,0,.5);
  animation:g-sort-demo 3200ms ease-in-out infinite}
@keyframes g-sort-demo{
  0%,44%{transform:translateX(0) rotate(0deg)}
  60%{transform:translateX(26px) rotate(9deg)}
  76%{transform:translateX(0) rotate(0deg)}
  88%{transform:translateX(-26px) rotate(-9deg)}
  100%{transform:translateX(0) rotate(0deg)}}
.g-sort-demo-side{display:flex;flex-direction:column;align-items:center;gap:6px;
  min-width:96px;font-family:var(--mono,ui-monospace,monospace);
  font-size:12px;letter-spacing:.1em;text-transform:uppercase;color:#B9B6DC}
.g-sort-demo-side b{font-size:20px;letter-spacing:.06em}
.g-sort-demo-side.yes b{color:var(--sort-pink)}
.g-sort-demo-side.no b{color:#9FA3C8}
.g-sort-howto-lines{display:flex;flex-direction:column;gap:6px;
  color:#B9B6DC;font-size:clamp(12px,1.7vh,15px);line-height:1.5}
.g-sort-howto-actions{width:100%;display:flex;justify-content:center}

/* -------------------------------------------------------------- THE TICKET */
.g-sort-end{position:absolute;inset:0;z-index:9;display:grid;place-items:safe center;
  padding:16px 0;overflow:auto;background:rgba(12,12,26,.62)}
.g-sort-ticket{width:min(460px,92vw);border-radius:16px;padding:22px 26px 8px;
  background:linear-gradient(180deg,#2C2C54,#1F1F3C);
  border:1px solid var(--sort-line);box-shadow:0 26px 64px rgba(0,0,0,.62);
  display:flex;flex-direction:column;gap:12px;position:relative}
.g-sort-ticket.is-royal{border-color:rgba(255,201,74,.55);
  box-shadow:0 26px 64px rgba(0,0,0,.62),0 0 0 2px rgba(255,201,74,.22) inset}
.g-sort-ticket-h{font-family:var(--disp,system-ui,sans-serif);font-weight:800;
  letter-spacing:.1em;text-transform:uppercase;font-size:clamp(15px,2.2vh,20px)}
.g-sort-rows{display:flex;flex-direction:column;gap:7px}
.g-sort-row{display:flex;align-items:baseline;justify-content:space-between;gap:14px;
  font-size:clamp(12px,1.7vh,15px);color:#B9B6DC;
  border-bottom:1px dashed rgba(237,235,255,.1);padding-bottom:6px}
.g-sort-row b{color:#EDEBFF;font-variant-numeric:tabular-nums;font-size:1.28em}
.g-sort-ticket-stamp{position:relative;min-height:56px;display:grid;place-items:center}
.g-sort-hint{color:#8B8FAE;font-size:clamp(11px,1.5vh,13px);line-height:1.5}
.g-sort-ticket-actions{position:sticky;bottom:0;z-index:2;display:flex;
  justify-content:center;gap:10px;margin:6px -26px 0;padding:12px 26px 14px;
  background:linear-gradient(180deg,rgba(31,31,60,0),#1F1F3C 42%)}

/* the note rail: thin, thin-pile and quick-sort warnings live here */
.g-sort-note{position:absolute;left:50%;transform:translateX(-50%);
  bottom:12px;z-index:5;pointer-events:none;
  padding:.36em .8em;border-radius:999px;background:rgba(37,37,66,.8);
  border:1px solid var(--sort-line);color:#B9B6DC;
  font-family:var(--mono,ui-monospace,monospace);font-size:12px;
  letter-spacing:.06em}

/* --------------------------------------------------------- REDUCED MOTION *
   The room still plays: the ring is three brightness STATES read off
   data-ripe, the travel becomes one 120ms fade, and the keys were always the
   primary input. Nothing here removes information - it removes travel.        */
.g-sort[data-reduced="1"] .g-sort-stack,
.g-sort[data-reduced="1"] .g-sort-face,
.g-sort[data-reduced="1"] .g-sort-halo,
.g-sort[data-reduced="1"] .g-sort-demo-card,
.g-sort[data-reduced="1"] .g-sort-wall-tile.thud{animation:none}
.g-sort[data-reduced="1"] .g-sort-card{transition:opacity 120ms linear}
.g-sort[data-reduced="1"] .g-sort-card.is-gone,
.g-sort[data-reduced="1"] .g-sort-card.is-sink{transition:opacity 120ms linear;
  transform:none;opacity:0}
.g-sort[data-reduced="1"] .g-sort-card.is-held{transition:none}
.g-sort[data-reduced="1"] .g-sort-stage.is-shiver{animation:none;
  outline:2px solid rgba(139,143,174,.7);outline-offset:-2px}
.g-sort[data-reduced="1"] .g-sort-word.show{animation:none;opacity:1}
.g-sort[data-reduced="1"] .g-sort-ring-arc{filter:none;transition:none}
.g-sort[data-reduced="1"] .g-sort-ringbox .g-sort-ring-arc{opacity:.45}
.g-sort[data-reduced="1"] .g-sort-ringbox[data-ripe="ripe"] .g-sort-ring-arc{opacity:.8}
.g-sort[data-reduced="1"] .g-sort-ringbox[data-ripe="just"] .g-sort-ring-arc{opacity:1}

@media (prefers-reduced-motion: reduce){
  .g-sort-stack,.g-sort-face,.g-sort-halo,.g-sort-demo-card,
  .g-sort-wall-tile.thud,.g-sort-word.show{animation:none}
  .g-sort-card,.g-sort-card.is-band,.g-sort-card.is-gone,.g-sort-card.is-sink{
    transition:opacity 120ms linear}
  .g-sort-stage.is-shiver{animation:none}
}

/* A COARSE POINTER PAYS FOR EVERY BLUR AND EVERY BLEND (trap 42). The room has
   no blends to drop, so the touch rung drops the two drop-shadows over a live
   decode and the wall's grayscale pass. */
html.ae-touch .g-sort-ring-arc,
html.ae-touch .g-sort-ringbox[data-ripe="ripe"] .g-sort-ring-arc,
html.ae-touch .g-sort-ringbox[data-ripe="just"] .g-sort-ring-arc{filter:none}
html.ae-touch .g-sort-wall-tile.is-wrong{filter:none;opacity:.34}
html.ae-lite .g-sort-halo{display:none}
html.ae-lite .g-sort-face{animation:none}
`;

/** The whole class sheet: the room, the door (LOT G2) and the decks (LOT D). */
export function styleText() {
  return SORT_CSS + '\n' + (typeof SETUP_CSS === 'string' ? SETUP_CSS : '')
    + '\n' + (typeof DECK_CSS === 'string' ? DECK_CSS : '');
}

/** Inject once per document. An unstyled class is still playable. */
export function ensureStyle() {
  try {
    if (typeof document === 'undefined' || !document.head || !document.getElementById) return false;
    if (document.getElementById(STYLE_ID)) return true;
    const s = document.createElement('style');
    s.id = STYLE_ID;
    s.textContent = styleText();
    document.head.appendChild(s);
    return true;
  } catch (e) { return false; }
}

/* DECK_CSS is deliberately NOT on this object: it is declared BELOW, and an
   object literal evaluated up here would read it inside its temporal dead zone
   and take the whole module down. Import it by name. */
export default { ensureStyle, styleText, SORT_CSS, STYLE_ID };

/* ============================================================================
 * THE HOUSE RULES SHEET (LOT D). Appended, never woven into SORT_CSS above:
 * the room's own look is one thing and the three decks that dress it are
 * another, and a deck that never builds must leave no rule of the room behind.
 * Everything here is scoped under .g-sort exactly like the room's half, every
 * node it styles is pointer-events:none, and NOTHING here is ever the thing a
 * press lands on.
 *
 * SAME TRAP, SAME WARNING: no backtick in a CSS comment in this file (37).
 *
 * WHO WRITES WHAT
 *   casino.js    .g-sort-bulbs (inside nodes.halo), .g-sort-cas and its
 *                children, is-bounce on a card, the stamp GLOW tokens
 *   pressure.js  is-flood on the stage, is-shudder / is-flood on the wall
 *                (through wall.flood), nothing else - the surge is the
 *                ENGINE's effects, not nodes of its own
 *   trickster.js data-tk-* on a card, data-crooked on a ring box, is-flick on
 *                the chain chip, and its own ghost node
 * ==========================================================================*/
export const DECK_CSS = `
/* ================================================================== CASINO */
/* THE MARQUEE. The room already owns the halo (it crawls at rung 0 and chases
   from 6 off data-chase); the casino hangs the BULBS on it. The ring TURNS as
   one layer rather than each lamp lighting in sequence, which is the cheap way
   to buy the same read: the lamps are evenly spaced and identical, so a ring
   rotating at N degrees a second and a light travelling round a fixed ring are
   the same picture - and the seeded lit/dim pattern turning with it IS the
   chase. One composited layer instead of eighteen animations, and no
   background-position anywhere near it (trap 36).
   Tonight's hue is a draw off the UTC DATE, not the class seed, so the hall is
   a different colour tonight than it was last night and the same colour all
   evening. About one night in twenty the draw goes off-arc and it is gold. */
.g-sort{--sort-mq-h:330;--sort-mq-a:.32;--sort-mq-t:7000ms;--sort-heat:.2}
.g-sort-bulbs{position:absolute;inset:0;pointer-events:none;
  opacity:var(--sort-mq-a);
  animation:g-sort-mqspin var(--sort-mq-t) linear infinite}
.g-sort-bulbs i{position:absolute;left:50%;top:50%;width:7px;height:7px;
  margin:-3.5px 0 0 -3.5px;border-radius:50%;
  background:hsl(var(--sort-mq-h) 88% 68%);
  transform:rotate(calc(var(--sort-bulb,0) * 1deg)) translateY(calc(var(--sort-card-w) * -.95));
  box-shadow:0 0 7px hsl(var(--sort-mq-h) 90% 66% / .8);
  opacity:calc(.3 + .7 * var(--sort-bulb-on,0))}
@keyframes g-sort-mqspin{
  0%{rotate:0deg}
  100%{rotate:360deg}}
.g-sort-bulbs.is-payout{animation:g-sort-mqspin var(--sort-mq-t) linear infinite,
  g-sort-payout 620ms ease-out}
@keyframes g-sort-payout{
  0%{opacity:calc(var(--sort-mq-a) * 3.2);filter:brightness(1.9)}
  100%{opacity:var(--sort-mq-a);filter:brightness(1)}}
.g-sort-bulbs.is-gold i{background:var(--sort-gold);
  box-shadow:0 0 14px rgba(255,201,74,.9)}

/* the casino's own layer: sparkles, tokens, the badge, the reveal. It sits
   ABOVE the stack and below the chrome, and nothing in it can be clicked. */
.g-sort-cas{position:absolute;inset:0;z-index:4;pointer-events:none;
  overflow:hidden}
.g-sort-cas *{pointer-events:none}

/* SPARKLE: the PERFECT payout. Eight specks off the card centre, 520ms, and
   they are drawn - never media, never a decode, never an engine kind. */
.g-sort-spark{position:absolute;left:50%;top:50%;width:0;height:0}
.g-sort-spark i{position:absolute;width:6px;height:6px;margin:-3px 0 0 -3px;
  border-radius:50%;background:var(--sort-gold);
  box-shadow:0 0 10px rgba(255,201,74,.85);
  animation:g-sort-spark 520ms cubic-bezier(.2,.9,.3,1) both}
@keyframes g-sort-spark{
  0%{transform:rotate(var(--sort-spark-a,0deg)) translateY(0) scale(.4);opacity:0}
  18%{opacity:1}
  100%{transform:rotate(var(--sort-spark-a,0deg)) translateY(calc(var(--sort-card-w) * -.62)) scale(.1);
    opacity:0}}

/* THE BANK: a token leaves the wall slot the card landed in and is paid into
   the Sorted chip. 500-650ms, one per THUD, and the chip takes the punch. */
.g-sort-token{position:absolute;width:14px;height:14px;border-radius:50%;
  left:0;top:0;background:radial-gradient(circle at 34% 32%,#FFF0B8,var(--sort-gold) 62%,#B57B14);
  box-shadow:0 0 12px rgba(255,201,74,.7);
  animation:g-sort-bank var(--sort-tok-ms,560ms) cubic-bezier(.35,.05,.2,1) both}
@keyframes g-sort-bank{
  0%{transform:translate3d(var(--sort-tok-x0,0px),var(--sort-tok-y0,0px),0) scale(.5);opacity:0}
  14%{opacity:1;transform:translate3d(var(--sort-tok-x0,0px),var(--sort-tok-y0,0px),0) scale(1.1)}
  100%{transform:translate3d(var(--sort-tok-x1,0px),var(--sort-tok-y1,0px),0) scale(.42);opacity:0}}
.g-sort-chip.is-paid b{animation:g-sort-paid 320ms cubic-bezier(.2,1.5,.4,1)}
@keyframes g-sort-paid{
  0%{scale:1}
  40%{scale:1.34;color:var(--sort-gold)}
  100%{scale:1}}

/* THE BADGE: the record ping and the jackpot word. One hero per beat, so it
   never shares the screen with the room's own verdict word (the casino holds
   it back a beat and drops it if the room is already talking). */
.g-sort-badge{position:absolute;left:50%;top:calc(50% - var(--sort-card-h) * .66);
  transform:translate(-50%,0);white-space:nowrap;
  font-family:var(--disp,system-ui,sans-serif);font-weight:800;
  font-size:clamp(12px,1.9vh,18px);letter-spacing:.18em;text-transform:uppercase;
  color:var(--sort-gold);text-shadow:0 0 16px rgba(255,201,74,.6);
  padding:.3em .8em;border-radius:999px;
  background:rgba(24,24,48,.72);border:1px solid rgba(255,201,74,.4);
  opacity:0}
.g-sort-badge.show{animation:g-sort-badge 1100ms ease-out both}
.g-sort-badge[data-tone="record"]{color:#8CE8FF;border-color:rgba(140,232,255,.45);
  text-shadow:0 0 16px rgba(140,232,255,.6)}
@keyframes g-sort-badge{
  0%{opacity:0;transform:translate(-50%,8px) scale(.92)}
  16%{opacity:1;transform:translate(-50%,0) scale(1.05)}
  74%{opacity:1;transform:translate(-50%,0) scale(1)}
  100%{opacity:0;transform:translate(-50%,-6px) scale(1)}}

/* THE REVEAL: the royal, 620ms, and the only full-stage light this class
   ever pays for itself. It is a wash of its own drawing, not an engine kind:
   the royal has to be able to land while the surge already owns every wash. */
.g-sort-reveal{position:absolute;inset:0;
  background:radial-gradient(circle at 50% 48%,rgba(255,201,74,.5) 0%,rgba(255,201,74,.14) 34%,transparent 68%);
  opacity:0;animation:g-sort-reveal 620ms cubic-bezier(.15,.9,.25,1) both}
@keyframes g-sort-reveal{
  0%{opacity:0;scale:.62}
  26%{opacity:1;scale:1.04}
  100%{opacity:0;scale:1.3}}

/* BOUNCE: 80ms on the grab, and it is the only thing the casino does to the
   card the player is holding. It rides scale (an INDIVIDUAL property), not
   transform, because the room writes the transform on every drag frame. */
.g-sort-card.is-bounce{animation:g-sort-bounce 80ms ease-out}
@keyframes g-sort-bounce{
  0%{scale:1}
  60%{scale:1.035}
  100%{scale:1}}

/* GLOW: the stamp lights as it is earned, 180ms in and 300ms out. The room
   already drives the two opacities off the drag; this is what makes them warm
   instead of merely visible. */
.g-sort-stamp{transition:box-shadow 300ms ease}
.g-sort-card.is-held .g-sort-stamp{transition:box-shadow 180ms ease}
.g-sort-card.is-held .g-sort-stamp.yes{
  box-shadow:0 0 calc(26px * var(--sort-a-yes)) rgba(255,105,180,calc(.55 * var(--sort-a-yes)))}
.g-sort-card.is-held .g-sort-stamp.no{
  box-shadow:0 0 calc(20px * var(--sort-a-no)) rgba(159,163,200,calc(.4 * var(--sort-a-no)))}

/* ================================================================ PRESSURE */
/* rung 4: the wall SHUDDERS on the thud. A transform on the grid, never on
   the tiles - one composited layer instead of ninety-six. */
.g-sort-wall.is-shudder .g-sort-wall-grid{animation:g-sort-wshud 240ms ease-in-out}
@keyframes g-sort-wshud{
  0%,100%{transform:translate3d(0,0,0) scale(1)}
  22%{transform:translate3d(-5px,2px,0) scale(1.006)}
  56%{transform:translate3d(4px,-3px,0) scale(1.004)}
  80%{transform:translate3d(-2px,1px,0) scale(1)}}

/* rung 8: THE FLOOD. The collage stops being a backdrop and becomes the room,
   and a swiped card flies INTO it instead of off the edge of the world. */
.g-sort-wall.is-flood{opacity:calc(.55 + .45 * var(--sort-bg-fade,.35));
  transition:opacity 900ms ease}
.g-sort-wall.is-flood .g-sort-wall-grid{gap:0}
.g-sort.is-flood .g-sort-card.is-gone{
  transition:transform 340ms cubic-bezier(.3,.1,.3,1),opacity 340ms ease;
  transform:translate3d(calc(var(--sort-dx) * .32),0,0)
    rotate(var(--sort-tilt)) scale(.14);opacity:0}
.g-sort.is-flood .g-sort-wall-tile.thud{animation-duration:420ms}

/* KEN-BURNS ON THE WALL (Law III: no frame of the room is ever still). The
   tiles drift on a long seeded period; a tile that never decodes drifts its
   drawn back instead, which is the point. It is switched ON by the wall, so a
   capped or reduced class simply never asks for it. */
.g-sort-wall.is-kb .g-sort-wall-face{
  animation:g-sort-wkb var(--sort-wall-kb,17s) ease-in-out infinite alternate}
@keyframes g-sort-wkb{
  0%{transform:scale(1) translate3d(0,0,0)}
  100%{transform:scale(1.06) translate3d(1.5%,-1.5%,0)}}

/* =============================================================== TRICKSTER */
/* THE FREEZE. A loop holds its poster for 400ms and then plays. A video is
   really paused (that is honest); a gif cannot be, so the frost is what stops
   the eye. Never a filter over a live decode (trap 36) - this is one flat
   plate at low alpha. */
.g-sort-tk-frost{position:absolute;inset:0;pointer-events:none;
  background:linear-gradient(160deg,rgba(210,225,255,.2),rgba(120,140,200,.14));
  box-shadow:0 0 0 1px rgba(220,235,255,.2) inset}
.g-sort-card[data-tk-freeze="1"] .g-sort-face{animation-play-state:paused}

/* THE DOPPELGANGER. A repeat arrives mirrored. The ken-burns owns transform on
   the face, so the mirror is a SECOND KEYFRAME rather than a static rule an
   animation would simply outrank. */
.g-sort-card[data-tk-mirror="1"] .g-sort-face{animation-name:g-sort-kb-flip}
@keyframes g-sort-kb-flip{
  0%{transform:scaleX(-1) scale(1)}
  100%{transform:scaleX(-1) scale(1.06)}}

/* GHOST CARD. After a stall, a ghost of the card you are staring at drifts one
   way, as if it had already gone. It cannot be grabbed and it changes nothing. */
.g-sort-tk-ghost{position:absolute;inset:0;border-radius:18px;
  pointer-events:none;overflow:hidden;opacity:0;
  border:1px solid rgba(237,235,255,.18);
  background:linear-gradient(160deg,hsl(var(--sort-back-h,318) 44% 24%),hsl(var(--sort-back-h2,268) 40% 15%));
  animation:g-sort-ghost 1400ms ease-out both}
.g-sort-tk-ghost img{position:absolute;inset:0;width:100%;height:100%;
  object-fit:cover;display:block;opacity:.5}
@keyframes g-sort-ghost{
  0%{opacity:0;transform:translate3d(0,0,0) rotate(0deg) scale(1)}
  24%{opacity:.42}
  100%{opacity:0;
    transform:translate3d(var(--sort-ghost-dx,90px),8px,0) rotate(var(--sort-ghost-r,7deg)) scale(.97)}}

/* THE CROOKED RING. The room keeps writing the TRUTH into --sort-ring every
   tick; while a card is crooked the arc reads a SECOND variable the trickster
   writes instead, and the two meet exactly in the last 15%. Nothing about the
   real ring, the ripe attribute or the verdict moves - only the face. */
.g-sort-ringbox[data-crooked="1"] .g-sort-ring-arc{
  stroke-dashoffset:calc(var(--sort-ring-len,1000) * (1 - var(--sort-ring-bend,1)))}

/* STAT FLICKER. The chain chip reads a number that was never true, then
   corrects itself with a static pop. The ledger did not move; you did. */
.g-sort-chip.is-flick b{animation:g-sort-flick 120ms steps(2,end) 2;
  color:#8B8FAE}
@keyframes g-sort-flick{
  0%{opacity:1;translate:0 0}
  50%{opacity:.55;translate:1px -1px}
  100%{opacity:1;translate:0 0}}

/* UNRELIABLE LABEL. The glyph is the truth and the word is not, so the word is
   the only thing that may move: it is nudged, never restyled, because a lie
   that announces itself is not a lie. */
.g-sort-stamp[data-tk-lie="1"] .g-sort-word-t{letter-spacing:.02em}

/* --------------------------------------------------------- REDUCED MOTION *
   The two cards that are NOT motion survive: STAT FLICKER is a number and
   UNRELIABLE LABEL is a word. Everything else in this sheet stops travelling,
   and the surge is left with wash alpha and nothing that flies.               */
.g-sort[data-reduced="1"] .g-sort-bulbs,
.g-sort[data-reduced="1"] .g-sort-spark i,
.g-sort[data-reduced="1"] .g-sort-token,
.g-sort[data-reduced="1"] .g-sort-reveal,
.g-sort[data-reduced="1"] .g-sort-card.is-bounce,
.g-sort[data-reduced="1"] .g-sort-tk-ghost,
.g-sort[data-reduced="1"] .g-sort-wall.is-kb .g-sort-wall-face,
.g-sort[data-reduced="1"] .g-sort-wall.is-shudder .g-sort-wall-grid{animation:none}
.g-sort[data-reduced="1"] .g-sort-badge.show{animation:none;opacity:1}
.g-sort[data-reduced="1"] .g-sort-chip.is-paid b{animation:none}
.g-sort[data-reduced="1"] .g-sort-chip.is-flick b{animation:none;opacity:.6}
.g-sort[data-reduced="1"] .g-sort-stamp{transition:none}
@media (prefers-reduced-motion: reduce){
  .g-sort-bulbs,.g-sort-spark i,.g-sort-token,.g-sort-reveal,
  .g-sort-card.is-bounce,.g-sort-tk-ghost,
  .g-sort-wall.is-kb .g-sort-wall-face,
  .g-sort-wall.is-shudder .g-sort-wall-grid{animation:none}
}

/* A COARSE POINTER PAYS FOR EVERY GLOW (trap 42): the casino keeps its light
   and loses its shadows, and the wall stops drifting under a finger. */
html.ae-touch .g-sort-bulbs i,
html.ae-touch .g-sort-spark i,
html.ae-touch .g-sort-token{box-shadow:none}
html.ae-touch .g-sort-wall.is-kb .g-sort-wall-face{animation:none}
html.ae-lite .g-sort-bulbs,
html.ae-lite .g-sort-wall.is-kb .g-sort-wall-face{animation:none}
`;
