/* ============================================================================
 * games/echo/style.js - the game injects its OWN stylesheet from JS.
 *
 * THE MUSIC ROOM AFTER HOURS. The class root is a full-viewport stage: a dark
 * rehearsal room, one warm spotlight from above, and SIX PADS standing in a
 * ring on the floor like lit instruments - each its own colour, each a drum
 * head of glass over a lamp. When a pad plays it does not just change colour:
 * the lamp comes up, the glass blooms, and the light FALLS OFF onto the floor
 * around it (a spill pseudo twice the pad's size, a floor pool under it) so
 * the room itself reads the sequence. Composition, exactly the DOM contract:
 *
 *   .g-ec-stage[data-phase=briefing|play|echo|input|encore|ended]
 *                   absolute inset:0, explicit ground, padded under the shell's
 *                   proctor strip; --ec-ring is the one circle everything on
 *                   the floor is sized from
 *   .g-ec-backdrop  the casino's lighting rig (pointer-events:none, z 0)
 *   .g-ec-hud       four chips: len (big, the score of this class), clock,
 *                   streak (pink), best (gold)
 *   .g-ec-ring      the floor circle; the pads hang off its centre
 *   .g-ec-pad[data-pad=0..5][data-state=idle|lit|pressed|decoy]
 *                   ABSOLUTE, positioned ONLY by its transform from --ec-a
 *                   (its angle on the ring) and --ec-r (the ring radius); the
 *                   transform is positional ONLY - every bloom, squash, ripple
 *                   and shiver lives on .g-ec-face, the ::before (the ripple)
 *                   or the ::after (the spill), never on the pad's own
 *                   transform, so the hitbox never moves (Law II)
 *     .g-ec-face    the glass: lamp gradient + rim; inside it .g-ec-media
 *                   wears the player's own pool media at LOW alpha (Deck VI
 *                   asset chrome; shown only once the pad is .is-loaded),
 *                   screen-blended, ken-burns drifting, and .g-ec-glyph - the
 *                   TRUTH NODE: the pad's mark, in the pad's colour, never
 *                   re-written by anyone
 *     .g-ec-word    the pad's word on a dark plate at the rim (empty under
 *                   data-faces=glyphs) - the ONE node a trickster may lie on;
 *                   the glyph, the colour and the place on the ring are the
 *                   truth (Law IV)
 *   .g-ec-msg / .g-ec-flashwell / .g-ec-end (the core toggles .g-ec-end and
 *                   the streak chip with the hidden attribute, so NEITHER
 *                   carries a display: here - trap 27)
 *   .g-ec-howto     the drawn class-rules sheet (Deck VI): three vignettes
 *                   DRAWN ENTIRELY IN CSS on an empty span (.g-ec-howto-art
 *                   [data-art=watch|repeat|decoy] - box-shadow lamps on the
 *                   ::before, a keycap / a cold pad with a bar through it on
 *                   the ::after) - and one GO
 *
 * STAGE ATTRIBUTES the core writes and this file reads: data-phase,
 * data-tier, data-faces (words|glyphs), data-audible (1|0 - the audio tell),
 * data-reduced, data-telegraph (tier 2's decoy warning), data-encore.
 *
 * PAD STATES (the lamp ladder): idle = a dim ember that breathes; lit = the
 * lamp full on, glass bloom, spill and floor pool up, a 90ms attack and a
 * slow 600ms decay so a fast playback still reads as separate notes; pressed =
 * the same light plus the face squashes (scale .94 on the FACE) and a ripple
 * ring runs out from the point of contact; decoy = a COLD light - the hue
 * rolls toward cyan, saturation drops, the glass flickers in steps and the
 * spill goes grey-blue - the telegraphed tell of tier 2 (tier 3+ the core
 * simply marks the decoy lit, and nothing here can tell the difference,
 * which is the point).
 *
 * AUDIO-INAUDIBLE TELL: .g-ec-stage[data-audible="0"] makes lit/pressed pads
 * flash brighter and hold longer (the contract's mandatory visual tell when
 * ctx.audioAudible === false). The core sets the attribute; we size it.
 *
 * PALETTE: six pad hues spread around the wheel so no two neighbours rhyme
 * (pink, gold, teal, violet, coral, sky); the casino may rotate the whole set
 * with --ec-n-rot on the stage (seeded identity) and retint the room with
 * --ec-n-la / --ec-n-lb / --ec-n-spot. Every other colour is a shell token
 * mix, so init.palette reskins the room.
 *
 * NOTHING IS EVER STILL (Law III): the spotlight breathes, idle pads ember,
 * the media drifts, the casino's halo crawls. REDUCED MOTION twice over
 * (html.arc-reduced + the media query): transitions only, no keyframes.
 * .g-ec-stage.suspended freezes every animation so the core can hold the
 * room's breath with the class.
 *
 * The rule of this file: NO `display:` on a node the shell toggles with the
 * hidden attribute, and NEVER the bracketed hidden selector (trap 27).
 * ==========================================================================*/

const STYLE_ID = 'g-ec-style';

/* the six pads, their hues and their angles on the ring: generated so the
   numbers live in one place. Six pads = 60deg steps from the top; a ring
   that carries only four pads (no data-pad=4) sits them on the diamond. */
const PAD_HUES = Object.freeze([330, 46, 178, 264, 20, 205]);
function padRules() {
  let css = '';
  for (let i = 0; i < 6; i++) {
    css += '.g-ec-pad[data-pad="' + i + '"]{--ec-h-base:' + PAD_HUES[i] + ';--ec-a:var(--ang,' + (-90 + 60 * i) + 'deg)}\n';
  }
  for (let i = 0; i < 4; i++) {
    css += '.g-ec-ring:not(:has(.g-ec-pad[data-pad="4"])) .g-ec-pad[data-pad="' + i + '"]{--ec-a:var(--ang,' + (-90 + 90 * i) + 'deg)}\n';
  }
  return css;
}

export const STYLE_TEXT = `
/* ---- registered decoration props: numbers interpolate, so a var change
   TWEENS instead of snapping --------------------------------------------- */
@property --ec-n-rot{syntax:'<number>';inherits:true;initial-value:0}
@property --ec-n-spot{syntax:'<number>';inherits:true;initial-value:.7}
@property --ec-lamp{syntax:'<number>';inherits:true;initial-value:0}

/* ---- the stage: the whole window is the room ---------------------------- */
.g-ec-stage{position:absolute;inset:0;overflow:hidden;display:flex;flex-direction:column;
  align-items:center;gap:8px;padding:72px 18px 14px;color:var(--ink);
  --ec-ring:min(calc(100dvh - 250px), calc(100vw - 60px), 640px);
  --ec-pad:calc(var(--ec-ring) * .255);
  --ec-r:calc(var(--ec-ring) * .355);
  --ec-la:var(--ec-n-la, color-mix(in srgb, var(--gold), transparent 82%));
  --ec-lb:var(--ec-n-lb, color-mix(in srgb, var(--pink), transparent 84%));
  --ec-breath:var(--ec-n-breath,7s);
  --ec-kb:var(--ec-n-kb,26s);
  --ec-n-rot:0;
  transition:--ec-n-rot 2.4s ease, --ec-n-spot 2s ease;
  background:
    radial-gradient(60% 46% at 50% 2%, var(--ec-la), transparent 70%),
    radial-gradient(90% 50% at 50% 112%, var(--ec-lb), transparent 64%),
    linear-gradient(180deg, color-mix(in srgb, var(--navy), black 30%) 0%, color-mix(in srgb, var(--ground), black 46%) 100%)}
.g-ec-stage.suspended *{animation-play-state:paused !important}
.g-ec-stage,.g-ec-stage *{box-sizing:border-box}

/* the floorboards: a faint plank grain under everything, perspective-squashed */
.g-ec-stage::before{content:"";position:absolute;left:-20%;right:-20%;top:46%;bottom:-4%;z-index:0;
  pointer-events:none;opacity:.22;transform:perspective(700px) rotateX(58deg);transform-origin:50% 100%;
  background:
    repeating-linear-gradient(90deg, transparent 0 58px, color-mix(in srgb, var(--ink), transparent 86%) 58px 60px),
    repeating-linear-gradient(0deg, transparent 0 7px, color-mix(in srgb, var(--ink), transparent 94%) 7px 8px);
  -webkit-mask-image:radial-gradient(60% 70% at 50% 40%, #000 20%, transparent 100%);
  mask-image:radial-gradient(60% 70% at 50% 40%, #000 20%, transparent 100%)}
/* the wall line: a picture rail where the wall meets the dark */
.g-ec-stage::after{content:"";position:absolute;left:0;right:0;top:calc(46% - 1px);height:2px;z-index:0;
  pointer-events:none;background:linear-gradient(90deg, transparent, color-mix(in srgb, var(--gold), transparent 70%) 30% 70%, transparent);
  opacity:.35}

/* ---- the backdrop: the casino's lighting rig (decoration only) ---------- */
.g-ec-backdrop{position:absolute;inset:0;pointer-events:none;z-index:0;overflow:hidden}
.g-ec-backdrop *{pointer-events:none}
/* the spotlight cone: ALWAYS there (style-owned, so a disarmed casino still
   lights the room), breathing on the seed's period */
.g-ec-backdrop::before{content:"";position:absolute;left:50%;top:-6%;width:150%;height:120%;
  transform:translateX(-50%);
  background:
    conic-gradient(from 164deg at 50% 0%, transparent 0 9deg, var(--ec-la) 13deg 19deg, transparent 23deg 32deg),
    radial-gradient(46% 40% at 50% 4%, color-mix(in srgb, var(--gold), transparent 70%), transparent 70%);
  opacity:var(--ec-n-spot,.7);
  animation:g-ec-breathe var(--ec-breath) ease-in-out infinite alternate;
  -webkit-mask-image:linear-gradient(to bottom, #000 0, #000 55%, transparent 92%);
  mask-image:linear-gradient(to bottom, #000 0, #000 55%, transparent 92%)}
@keyframes g-ec-breathe{from{opacity:calc(var(--ec-n-spot,.7) * .72);transform:translateX(-50%) scaleX(1)}
  to{opacity:var(--ec-n-spot,.7);transform:translateX(-50%) scaleX(1.05)}}
/* dust in the beam */
.g-ec-backdrop::after{content:"";position:absolute;inset:0;opacity:.5;
  background:
    radial-gradient(1.5px 1.5px at 22% 18%, color-mix(in srgb, var(--ink), transparent 55%), transparent 70%),
    radial-gradient(1px 1px at 61% 32%, color-mix(in srgb, var(--ink), transparent 60%), transparent 70%),
    radial-gradient(1.5px 1.5px at 44% 52%, color-mix(in srgb, var(--gold), transparent 60%), transparent 70%),
    radial-gradient(1px 1px at 72% 61%, color-mix(in srgb, var(--ink), transparent 65%), transparent 70%),
    radial-gradient(1px 1px at 33% 74%, color-mix(in srgb, var(--ink), transparent 68%), transparent 70%),
    radial-gradient(1.5px 1.5px at 84% 22%, color-mix(in srgb, var(--gold), transparent 66%), transparent 70%);
  background-size:420px 380px;
  animation:g-ec-dust calc(var(--ec-kb) * 1.4) linear infinite}
@keyframes g-ec-dust{to{background-position:-60px 380px}}

/* ---- the HUD --------------------------------------------------------------- */
.g-ec-hud{position:relative;z-index:4;display:flex;gap:10px;align-items:stretch;justify-content:center;
  flex-wrap:wrap;width:min(100%, 760px);font-family:var(--mono);font-size:12px;letter-spacing:.12em;
  text-transform:uppercase;color:var(--ink-dim)}
.g-ec-chip{position:relative;text-align:center;line-height:1.2;
  min-width:86px;padding:7px 14px 6px;border-radius:11px;
  background:color-mix(in srgb, var(--panel), transparent 22%);
  border:1px solid color-mix(in srgb, var(--line), transparent 20%);
  box-shadow:0 6px 18px rgba(0,0,0,.35), inset 0 1px 0 color-mix(in srgb, var(--ink), transparent 88%);
  transition:box-shadow .3s ease, border-color .3s ease}
.g-ec-chip::before{content:attr(aria-label);display:block;font-size:9px;letter-spacing:.22em;color:var(--ink-faint);margin-bottom:2px;font-family:var(--mono)}
/* the shell's 10-segment meter rides inside the streak chip */
.g-ec-chip.g-ec-streak > *{margin-top:4px}
/* pressure.js's reduced-motion punch: a bloom, never a move */
.g-ec-chip.g-ec-p-bloom{box-shadow:0 0 0 2px color-mix(in srgb, var(--pink), transparent 45%), 0 0 22px color-mix(in srgb, var(--pink), transparent 40%) !important;transition:box-shadow .3s ease}
.g-ec-chip.g-ec-len{min-width:118px;font-family:var(--disp);font-size:26px;letter-spacing:.06em;color:var(--ink);
  border-color:color-mix(in srgb, var(--pink), transparent 55%);
  text-shadow:0 0 16px color-mix(in srgb, var(--pink), transparent 45%)}
.g-ec-chip.g-ec-len::before{font-family:var(--mono);letter-spacing:.22em}
.g-ec-chip.g-ec-clock{font-variant-numeric:tabular-nums;font-size:15px;color:var(--ink)}
.g-ec-chip.g-ec-streak{color:var(--pink)}
.g-ec-chip.g-ec-best{color:var(--gold)}
.g-ec-chip.g-ec-retake{color:var(--lav);border-style:dashed}
/* the input-window ring: a chip-sized conic face, --ec-k 1 -> 0 (index.js
   writes the var and toggles the chip hidden; no display: here - trap 27) */
.g-ec-chip.g-ec-timer{min-width:0;width:40px;height:40px;padding:0;border-radius:50%;vertical-align:middle;
  background:
    radial-gradient(circle, color-mix(in srgb, var(--panel), transparent 10%) 0 58%, transparent 60%),
    conic-gradient(from -90deg, var(--lav) 0deg, var(--lav) calc(var(--ec-k,1) * 360deg), color-mix(in srgb, var(--line), transparent 40%) calc(var(--ec-k,1) * 360deg));
  box-shadow:0 6px 18px rgba(0,0,0,.35), 0 0 14px color-mix(in srgb, var(--lav), transparent 70%)}
.g-ec-chip.g-ec-timer::before{content:none}
.g-ec-stage[data-phase="input"] .g-ec-chip.g-ec-clock{border-color:color-mix(in srgb, var(--lav), transparent 50%)}

/* ---- the ring: the floor circle ----------------------------------------- */
.g-ec-ring{position:relative;z-index:3;flex:0 0 auto;width:var(--ec-ring);height:var(--ec-ring);
  margin:auto 0;border-radius:50%;
  /* a chalk circle on the boards, and the pool of spotlight inside it */
  background:
    radial-gradient(circle at 50% 46%, color-mix(in srgb, var(--gold), transparent 88%) 0%, transparent 58%),
    radial-gradient(circle, transparent 0 calc(50% - 3px), color-mix(in srgb, var(--ink), transparent 84%) calc(50% - 2px) calc(50% - 1px), transparent 50%);
  touch-action:manipulation}
/* the centre mark: where the playback sits */
.g-ec-ring::before{content:"";position:absolute;left:50%;top:50%;width:14%;height:14%;border-radius:50%;
  transform:translate(-50%,-50%);pointer-events:none;opacity:.55;
  background:radial-gradient(circle, color-mix(in srgb, var(--ink), transparent 80%) 0 28%, transparent 30% 60%,
    color-mix(in srgb, var(--ink), transparent 88%) 62% 64%, transparent 66%);
  animation:g-ec-centre calc(var(--ec-breath) * 1.3) ease-in-out infinite alternate}
@keyframes g-ec-centre{from{opacity:.35;transform:translate(-50%,-50%) scale(.92)}to{opacity:.7;transform:translate(-50%,-50%) scale(1.04)}}
.g-ec-stage[data-phase="echo"] .g-ec-ring::before{animation-duration:1.2s}
.g-ec-stage[data-phase="input"] .g-ec-ring::before{opacity:.25;animation:none}

/* ---- the pads: six lit instruments -------------------------------------- */
${padRules()}
.g-ec-pad{position:absolute;left:50%;top:50%;width:var(--ec-pad);height:var(--ec-pad);margin:0;padding:0;
  border:0;border-radius:50%;background:transparent;cursor:pointer;color:var(--ink);
  -webkit-appearance:none;appearance:none;-webkit-tap-highlight-color:transparent;user-select:none;
  --ec-h:calc(var(--ec-h-base,330) + var(--ec-n-rot,0));
  --ec-lamp:0;
  --ec-sat:88%;
  transform:translate(-50%,-50%) rotate(var(--ec-a,0deg)) translate(var(--ec-r)) rotate(calc(-1 * var(--ec-a,0deg)));
  transition:--ec-lamp .6s ease-out;
  outline:none;z-index:1}
.g-ec-pad:focus-visible .g-ec-face{box-shadow:0 0 0 3px color-mix(in srgb, var(--ink), transparent 40%), 0 10px 28px rgba(0,0,0,.5)}
/* THE FACE: a drum head of glass over a lamp. The lamp var drives everything. */
.g-ec-face{position:absolute;inset:0;border-radius:50%;overflow:hidden;pointer-events:none;
  background:
    radial-gradient(circle at 50% 38%, hsl(var(--ec-h) var(--ec-sat) 82% / calc(.25 + .75 * var(--ec-lamp))) 0%,
      hsl(var(--ec-h) var(--ec-sat) 62% / calc(.18 + .62 * var(--ec-lamp))) 34%,
      hsl(var(--ec-h) var(--ec-sat) 30% / calc(.55 + .35 * var(--ec-lamp))) 72%,
      hsl(var(--ec-h) 40% 10% / .95) 100%),
    color-mix(in srgb, var(--ground), black 30%);
  border:2px solid hsl(var(--ec-h) 70% calc(40% + 35% * var(--ec-lamp)) / .85);
  box-shadow:
    0 10px 28px rgba(0,0,0,.5),
    inset 0 -10px 22px rgba(0,0,0,.55),
    inset 0 2px 0 hsl(var(--ec-h) 80% 90% / calc(.25 + .5 * var(--ec-lamp))),
    0 0 calc(10px + 34px * var(--ec-lamp)) hsl(var(--ec-h) 95% 70% / calc(.15 + .7 * var(--ec-lamp)));
  transform:scale(1);
  transition:transform .14s cubic-bezier(.2,1.4,.4,1), box-shadow .16s ease-out, border-color .16s ease-out, filter .3s ease}
/* the glass highlight: a crescent that brightens with the lamp */
.g-ec-face::before{content:"";position:absolute;left:14%;top:8%;width:58%;height:34%;border-radius:50%;
  background:linear-gradient(180deg, rgba(255,255,255,calc(.14 + .3 * var(--ec-lamp))), transparent);
  filter:blur(1px)}
/* the idle ember: every pad breathes a little on its own phase (Law III) */
.g-ec-face::after{content:"";position:absolute;inset:28%;border-radius:50%;
  background:radial-gradient(circle, hsl(var(--ec-h) 90% 72% / .5), transparent 70%);
  opacity:.35;
  animation:g-ec-ember calc(var(--ec-breath) * .9) ease-in-out infinite alternate;
  animation-delay:calc(var(--ec-a,0deg) / 360deg * -7s)}
@keyframes g-ec-ember{from{opacity:.18;transform:scale(.86)}to{opacity:.5;transform:scale(1.08)}}
/* THE SPILL: the light falls off the pad onto the floor (a pseudo twice the
   pad, pointer-events:none so the hitbox stays the pad) */
.g-ec-pad::after{content:"";position:absolute;left:50%;top:50%;width:240%;height:240%;border-radius:50%;
  transform:translate(-50%,-46%);pointer-events:none;z-index:-1;
  background:radial-gradient(circle, hsl(var(--ec-h) 95% 70% / .58) 0%, hsl(var(--ec-h) 90% 60% / .22) 28%, transparent 62%);
  opacity:calc(.06 + .94 * var(--ec-lamp));
  transition:opacity .6s ease-out}
/* THE RIPPLE: runs out from the face on a press (the ::before of the pad) */
.g-ec-pad::before{content:"";position:absolute;inset:0;border-radius:50%;pointer-events:none;
  border:2px solid hsl(var(--ec-h) 95% 78% / .9);opacity:0;transform:scale(1)}
/* the media: the player's own pool at LOW alpha (Deck VI asset chrome),
   screen-blended into the lamp, drifting (ken-burns), tinted by the pad */
.g-ec-media{position:absolute;inset:0;width:100%;height:100%;object-fit:cover;border-radius:50%;
  opacity:0;mix-blend-mode:screen;transition:opacity .8s ease;
  filter:saturate(.7) sepia(.2) hue-rotate(calc(var(--ec-h) * 1deg - 40deg));
  animation:g-ec-kb var(--ec-kb) ease-in-out infinite alternate;
  animation-delay:calc(var(--ec-a,0deg) / 360deg * -26s)}
@keyframes g-ec-kb{from{transform:scale(1.02) translate(0,0)}to{transform:scale(1.1) translate(2%,-1.6%)}}
.g-ec-pad.is-loaded .g-ec-media{opacity:calc(.16 + .16 * var(--ec-lamp))}
/* THE GLYPH: the truth node. A drawn mark in the pad's colour, centred on the
   glass; under word faces it rides a little higher to leave the rim to the word */
.g-ec-glyph{position:absolute;left:50%;top:50%;transform:translate(-50%,-52%);pointer-events:none;
  font:400 clamp(20px, calc(var(--ec-pad) * .36), 46px)/1 var(--body,system-ui,sans-serif);
  color:hsl(var(--ec-h) 90% calc(84% + 10% * var(--ec-lamp)));
  text-shadow:0 0 calc(4px + 14px * var(--ec-lamp)) hsl(var(--ec-h) 95% 70% / .9), 0 1px 0 rgba(0,0,0,.5);
  transition:transform .3s ease}
.g-ec-stage[data-faces="words"] .g-ec-glyph{top:44%;font-size:clamp(16px, calc(var(--ec-pad) * .28), 36px)}
/* THE WORD: a lit letter on a dark plate at the rim. The ONE node a trickster
   may lie on. Empty under glyph faces: it simply is not there to read. */
.g-ec-word{position:absolute;left:50%;bottom:11%;transform:translate(-50%,0);pointer-events:none;
  max-width:84%;padding:2px 8px;border-radius:7px;
  font-family:var(--disp);font-size:clamp(10px, calc(var(--ec-pad) * .13), 18px);letter-spacing:.08em;
  text-transform:uppercase;white-space:nowrap;overflow:hidden;text-overflow:ellipsis;
  color:hsl(var(--ec-h) 90% calc(84% + 10% * var(--ec-lamp)));
  background:rgba(8,6,22,calc(.55 - .2 * var(--ec-lamp)));
  text-shadow:0 0 calc(4px + 12px * var(--ec-lamp)) hsl(var(--ec-h) 95% 70% / .9);
  transition:color .2s ease, background .3s ease, opacity .3s ease}
.g-ec-word:empty{opacity:0}
/* ---- the lamp ladder: states ------------------------------------------- */
.g-ec-pad[data-state="lit"]{--ec-lamp:1;transition:--ec-lamp .09s ease-out}
.g-ec-pad[data-state="lit"] .g-ec-face{transform:scale(1.04);filter:brightness(1.08)}
.g-ec-pad[data-state="lit"] .g-ec-face::after{opacity:.9;animation:none;transform:scale(1.3)}
.g-ec-pad[data-state="pressed"]{--ec-lamp:1;transition:--ec-lamp .05s ease-out}
.g-ec-pad[data-state="pressed"] .g-ec-face{transform:scale(.94);filter:brightness(1.18)}
.g-ec-pad[data-state="pressed"] .g-ec-face::after{opacity:1;animation:none;transform:scale(1.4)}
.g-ec-pad[data-state="pressed"]::before{animation:g-ec-ripple .55s ease-out 1}
@keyframes g-ec-ripple{0%{opacity:.9;transform:scale(.9)}100%{opacity:0;transform:scale(1.7)}}
/* hover/active on the hitbox itself: a breath of light, never a move */
.g-ec-stage[data-phase="input"] .g-ec-pad:hover{--ec-lamp:.32;transition:--ec-lamp .12s ease-out}
.g-ec-pad:active .g-ec-face{transform:scale(.95)}
/* THE DECOY: a cold light. Tier 2's telegraphed tell - the hue rolls to the
   cold side, the saturation drops, the glass flickers in steps, the spill is
   grey-blue. Tier 3+ the core marks decoys lit and this rule never fires. */
.g-ec-pad[data-state="decoy"]{--ec-lamp:.85;--ec-sat:30%;--ec-h:196;transition:--ec-lamp .09s ease-out}
.g-ec-pad[data-state="decoy"] .g-ec-face{filter:saturate(.5) contrast(1.15);
  animation:g-ec-decoyflick .26s steps(3) infinite}
.g-ec-pad[data-state="decoy"] .g-ec-face::after{opacity:.7;animation:none;transform:scale(1.2);
  background:radial-gradient(circle, hsl(196 40% 72% / .5), transparent 70%)}
.g-ec-pad[data-state="decoy"]::after{background:radial-gradient(circle, hsl(196 30% 70% / .4) 0%, hsl(200 20% 55% / .16) 30%, transparent 62%)}
.g-ec-pad[data-state="decoy"] .g-ec-word{color:hsl(196 30% 82%);text-shadow:-1px 0 hsl(330 90% 70% / .6), 1px 0 hsl(178 80% 60% / .6)}
@keyframes g-ec-decoyflick{0%{opacity:1}50%{opacity:.72}100%{opacity:.92}}
/* the audio-inaudible tell (ctx.audioAudible false): brighter, longer */
.g-ec-stage[data-audible="0"] .g-ec-pad[data-state="lit"] .g-ec-face,
.g-ec-stage[data-audible="0"] .g-ec-pad[data-state="pressed"] .g-ec-face{filter:brightness(1.35) saturate(1.2)}
.g-ec-stage[data-audible="0"] .g-ec-pad{transition:--ec-lamp 1s ease-out}
.g-ec-stage[data-audible="0"] .g-ec-pad[data-state="lit"],
.g-ec-stage[data-audible="0"] .g-ec-pad[data-state="pressed"]{transition:--ec-lamp .05s ease-out}
/* tier 2 telegraphs the decoy: the proctor line wears a cold edge while it says so */
.g-ec-stage[data-telegraph="1"] .g-ec-msg{border-color:hsl(196 40% 60% / .6);box-shadow:0 8px 24px rgba(0,0,0,.4), 0 0 18px hsl(196 50% 60% / .25)}

/* ---- phases ------------------------------------------------------------- */
/* playback: the room leans in - the spotlight narrows a hair, the HUD dims */
.g-ec-stage[data-phase="echo"] .g-ec-hud{opacity:.72}
.g-ec-stage[data-phase="echo"] .g-ec-pad{cursor:default}
/* your turn: the ring's chalk line wakes */
.g-ec-stage[data-phase="input"] .g-ec-ring{
  box-shadow:0 0 0 1px color-mix(in srgb, var(--lav), transparent 70%), 0 0 40px color-mix(in srgb, var(--lav), transparent 88%)}
/* encore: the room goes warm and slow (casino.encore sets the pace; this is the tint) */
.g-ec-stage[data-phase="encore"],.g-ec-stage[data-encore="1"]{--ec-la:color-mix(in srgb, var(--gold), transparent 70%);
  --ec-lb:color-mix(in srgb, var(--gold), transparent 86%)}
.g-ec-stage[data-phase="encore"] .g-ec-pad,.g-ec-stage[data-encore="1"] .g-ec-pad{transition:--ec-lamp 1.2s ease-out}
.g-ec-stage[data-phase="encore"] .g-ec-pad[data-state="lit"],.g-ec-stage[data-encore="1"] .g-ec-pad[data-state="lit"],
.g-ec-stage[data-phase="encore"] .g-ec-pad[data-state="pressed"],.g-ec-stage[data-encore="1"] .g-ec-pad[data-state="pressed"]{transition:--ec-lamp .2s ease-out}
/* ended: the lamps go down, the spotlight stays on the empty ring */
.g-ec-stage[data-phase="ended"] .g-ec-pad{--ec-lamp:0 !important;cursor:default;opacity:.55;transition:opacity 1.2s ease, --ec-lamp 1.2s ease}
.g-ec-stage[data-phase="ended"] .g-ec-face::after{animation:none;opacity:.1}
.g-ec-stage[data-phase="ended"] .g-ec-ring{box-shadow:none}
.g-ec-stage[data-phase="briefing"] .g-ec-pad{--ec-lamp:0}

/* ---- the proctor line, the flashwell, the end card --------------------- */
.g-ec-msg{position:absolute;left:50%;bottom:22px;transform:translateX(-50%);z-index:6;
  max-width:min(92vw, 640px);padding:8px 16px;border-radius:10px;text-align:center;
  font-family:var(--body);font-size:14px;line-height:1.4;color:var(--ink);
  background:color-mix(in srgb, var(--panel), transparent 20%);
  border:1px solid color-mix(in srgb, var(--line), transparent 30%);
  box-shadow:0 8px 24px rgba(0,0,0,.4);pointer-events:none;
  transition:opacity .3s ease}
.g-ec-msg:empty{opacity:0}
.g-ec-flashwell{position:absolute;inset:0;pointer-events:none;z-index:5;overflow:hidden}
.g-ec-flashwell *{pointer-events:none}
/* the class report: the core appends a title, k/v rows (.g-ec-end-row > .g-ec-end-k
   + .g-ec-end-v) and a closing line straight into .g-ec-end, so the node IS
   the card. The grade itself is the shell's stamp - an object, never a string. */
.g-ec-end{position:absolute;left:50%;top:50%;transform:translate(-50%,-50%);z-index:8;overflow:auto;max-height:calc(100% - 90px);
  width:min(92vw, 380px);padding:20px 22px 18px;border-radius:16px;text-align:left;
  color:var(--ink);font-family:var(--body);font-size:14px;
  background:
    linear-gradient(180deg, color-mix(in srgb, var(--panel), transparent 4%), color-mix(in srgb, var(--navy), transparent 4%)),
    var(--panel);
  border:1px solid color-mix(in srgb, var(--gold), transparent 55%);
  box-shadow:0 24px 60px rgba(0,0,0,.6), 0 0 0 1px rgba(0,0,0,.4), inset 0 1px 0 color-mix(in srgb, var(--ink), transparent 86%);
  animation:g-ec-endin .5s ease-out 1}
.g-ec-end:empty{opacity:0;pointer-events:none}
@keyframes g-ec-endin{from{opacity:0;transform:translate(-50%,-46%)}to{opacity:1;transform:translate(-50%,-50%)}}
/* a neon rule under the title, the pad hues strung along it like a strip of lamps */
.g-ec-end-title{font-family:var(--disp);font-size:16px;letter-spacing:.14em;text-transform:uppercase;
  margin:0 0 12px;padding-bottom:10px;color:var(--gold);
  border-bottom:1px solid color-mix(in srgb, var(--line), transparent 20%);position:relative}
.g-ec-end-title::after{content:"";position:absolute;left:0;right:0;bottom:-2px;height:3px;border-radius:2px;opacity:.8;
  background:linear-gradient(90deg, hsl(330 90% 70%), hsl(46 90% 70%), hsl(178 80% 65%), hsl(264 80% 72%), hsl(20 90% 68%), hsl(205 85% 68%))}
.g-ec-end-row{display:flex;justify-content:space-between;align-items:baseline;gap:12px;
  padding:4px 0;border-bottom:1px dashed color-mix(in srgb, var(--line), transparent 55%)}
.g-ec-end-row:last-of-type{border-bottom:0}
.g-ec-end-k{color:var(--ink-faint)}
.g-ec-end-v{color:var(--ink);font-variant-numeric:tabular-nums}
.g-ec-end-best .g-ec-end-v{font-family:var(--disp);font-size:22px;color:var(--pink);
  text-shadow:0 0 14px color-mix(in srgb, var(--pink), transparent 40%)}
.g-ec-end-tail{margin-top:10px}
.g-ec-end-record{display:block;margin:2px 0 4px;font-family:var(--disp);font-size:13px;letter-spacing:.12em;text-transform:uppercase;
  color:var(--gold);text-shadow:0 0 12px color-mix(in srgb, var(--gold), transparent 40%)}
.g-ec-end-line{margin:4px 0 0;font-size:12px;color:var(--ink-dim);font-style:italic}
.g-ec-end .btn{margin:10px 4px 0}
/* the melody strip: the core may append .g-ec-end-strip > i[data-pad] - a
   spoiler-free tick row of the run's colours */
.g-ec-end-strip{display:flex;gap:4px;margin:10px 0 2px;flex-wrap:wrap}
.g-ec-end-strip i{display:block;width:10px;height:14px;border-radius:3px;
  --ec-h:calc(var(--ec-h-base,330) + var(--ec-n-rot,0));
  background:hsl(var(--ec-h) 85% 65%);box-shadow:0 0 6px hsl(var(--ec-h) 90% 65% / .7)}
.g-ec-end-strip i[data-pad="0"]{--ec-h-base:330}.g-ec-end-strip i[data-pad="1"]{--ec-h-base:46}
.g-ec-end-strip i[data-pad="2"]{--ec-h-base:178}.g-ec-end-strip i[data-pad="3"]{--ec-h-base:264}
.g-ec-end-strip i[data-pad="4"]{--ec-h-base:20}.g-ec-end-strip i[data-pad="5"]{--ec-h-base:205}

/* ---- the drawn class-rules sheet (Deck VI, Law IV) --------------------- */
/* The core builds .g-ec-howto > h3.g-ec-howto-title + .g-ec-howto-rows > N x
   (.g-ec-howto-row > span.g-ec-howto-art[data-art] + p.g-ec-howto-line) +
   button.g-ec-howto-go. The art span is EMPTY: every figure is drawn here on
   its two pseudo-elements. The ::before is a point that casts three lamps as
   box-shadows (a disc each, a glow each - box-shadow lists interpolate, so the
   lamps light in sequence on one keyframe); the ::after is the second actor. */
.g-ec-howto{position:absolute;left:50%;top:50%;transform:translate(-50%,-50%);z-index:9;
  width:min(92vw, 440px);padding:18px 20px 16px;border-radius:16px;color:var(--ink);
  background:linear-gradient(180deg, color-mix(in srgb, var(--panel), transparent 2%), var(--navy));
  border:1px solid color-mix(in srgb, var(--lav), transparent 55%);
  box-shadow:0 24px 60px rgba(0,0,0,.6), inset 0 1px 0 color-mix(in srgb, var(--ink), transparent 86%);
  animation:g-ec-endin .45s ease-out 1}
.g-ec-howto-title{margin:0 0 6px;font-family:var(--disp);font-size:15px;letter-spacing:.16em;text-transform:uppercase;color:var(--lav)}
.g-ec-howto-rows{display:flex;flex-direction:column}
.g-ec-howto-row{display:flex;align-items:center;gap:14px;padding:9px 0;
  border-top:1px dashed color-mix(in srgb, var(--line), transparent 50%)}
.g-ec-howto-line{margin:0;font-family:var(--body);font-size:13px;line-height:1.4;color:var(--ink-dim)}
.g-ec-howto-art{position:relative;flex:0 0 auto;width:96px;height:64px;pointer-events:none;
  --l1:330;--l2:46;--l3:178}
/* the three lamps: the ::before IS lamp one (a 20px disc), lamps two and three
   are its zero-spread shadows, and each lamp's glow is a blurred shadow of
   its own - box-shadow lists and background-color both interpolate, so the
   lamps light in sequence on one keyframe */
.g-ec-howto-art::before{content:"";position:absolute;left:11px;top:20px;width:20px;height:20px;border-radius:50%;
  background:hsl(var(--l1) 60% 38%);
  box-shadow:
    0 0 10px 2px hsl(var(--l1) 95% 70% / 0),
    27px 0 0 0 hsl(var(--l2) 60% 38%), 27px 0 10px 2px hsl(var(--l2) 95% 70% / 0),
    54px 0 0 0 hsl(var(--l3) 60% 38%), 54px 0 10px 2px hsl(var(--l3) 95% 70% / 0);
  animation:g-ec-hw-lamps 2.6s ease-in-out infinite}
@keyframes g-ec-hw-lamps{
  0%,64%,100%{background:hsl(var(--l1) 60% 38%);box-shadow:
    0 0 10px 2px hsl(var(--l1) 95% 70% / 0),
    27px 0 0 0 hsl(var(--l2) 60% 38%), 27px 0 10px 2px hsl(var(--l2) 95% 70% / 0),
    54px 0 0 0 hsl(var(--l3) 60% 38%), 54px 0 10px 2px hsl(var(--l3) 95% 70% / 0)}
  8%{background:hsl(var(--l1) 95% 78%);box-shadow:
    0 0 14px 4px hsl(var(--l1) 95% 70% / .85),
    27px 0 0 0 hsl(var(--l2) 60% 38%), 27px 0 10px 2px hsl(var(--l2) 95% 70% / 0),
    54px 0 0 0 hsl(var(--l3) 60% 38%), 54px 0 10px 2px hsl(var(--l3) 95% 70% / 0)}
  18%{background:hsl(var(--l1) 60% 38%);box-shadow:
    0 0 10px 2px hsl(var(--l1) 95% 70% / 0),
    27px 0 0 0 hsl(var(--l2) 60% 38%), 27px 0 10px 2px hsl(var(--l2) 95% 70% / 0),
    54px 0 0 0 hsl(var(--l3) 60% 38%), 54px 0 10px 2px hsl(var(--l3) 95% 70% / 0)}
  28%{background:hsl(var(--l1) 60% 38%);box-shadow:
    0 0 10px 2px hsl(var(--l1) 95% 70% / 0),
    27px 0 0 0 hsl(var(--l2) 95% 78%), 27px 0 14px 4px hsl(var(--l2) 95% 70% / .85),
    54px 0 0 0 hsl(var(--l3) 60% 38%), 54px 0 10px 2px hsl(var(--l3) 95% 70% / 0)}
  38%{background:hsl(var(--l1) 60% 38%);box-shadow:
    0 0 10px 2px hsl(var(--l1) 95% 70% / 0),
    27px 0 0 0 hsl(var(--l2) 60% 38%), 27px 0 10px 2px hsl(var(--l2) 95% 70% / 0),
    54px 0 0 0 hsl(var(--l3) 60% 38%), 54px 0 10px 2px hsl(var(--l3) 95% 70% / 0)}
  48%{background:hsl(var(--l1) 60% 38%);box-shadow:
    0 0 10px 2px hsl(var(--l1) 95% 70% / 0),
    27px 0 0 0 hsl(var(--l2) 60% 38%), 27px 0 10px 2px hsl(var(--l2) 95% 70% / 0),
    54px 0 0 0 hsl(var(--l3) 95% 78%), 54px 0 14px 4px hsl(var(--l3) 95% 70% / .85)}}
/* WATCH: an ear listens beside the lamps */
.g-ec-howto-art[data-art="watch"]::after{content:"";position:absolute;right:0;top:14px;width:16px;height:28px;
  border-radius:50% 50% 50% 50% / 40% 40% 60% 60%;
  border:2px solid color-mix(in srgb, var(--ink), transparent 40%);border-left-color:transparent;opacity:.7;
  animation:g-ec-hw-ear 2.6s ease-in-out infinite}
@keyframes g-ec-hw-ear{0%,100%{transform:scale(1);opacity:.5}8%,28%,48%{transform:scale(1.12);opacity:.95}}
/* REPEAT: the lamps light a beat later, under a keycap that taps them in order */
.g-ec-howto-art[data-art="repeat"]::before{animation-delay:.35s}
.g-ec-howto-art[data-art="repeat"]::after{content:"";position:absolute;left:13px;top:2px;width:16px;height:14px;border-radius:4px;
  background:var(--panel2,#2E2E55);border:1px solid var(--line);box-shadow:0 2px 0 rgba(0,0,0,.6);
  animation:g-ec-hw-tap 2.6s ease-in-out infinite}
@keyframes g-ec-hw-tap{0%,3%{transform:translate(0,0)}8%{transform:translate(0,8px)}14%{transform:translate(0,0)}
  23%{transform:translate(27px,0)}28%{transform:translate(27px,8px)}34%{transform:translate(27px,0)}
  43%{transform:translate(54px,0)}48%{transform:translate(54px,8px)}54%{transform:translate(54px,0)}
  64%,100%{transform:translate(0,0)}}
/* DECOY: a fourth lamp, cold, flickers out of turn below the row - a bar through it */
.g-ec-howto-art[data-art="decoy"]::after{content:"";position:absolute;left:38px;top:42px;width:20px;height:20px;border-radius:50%;
  background:
    linear-gradient(-35deg, transparent 44%, var(--pink) 46% 54%, transparent 56%),
    radial-gradient(circle at 50% 38%, hsl(196 30% 82%) 0%, hsl(196 30% 55%) 45%, hsl(200 20% 16%) 100%);
  border:1px solid hsl(196 30% 60% / .8);
  animation:g-ec-hw-cold 2.6s steps(3) infinite}
@keyframes g-ec-hw-cold{0%,12%,100%{opacity:.45;box-shadow:none}16%{opacity:1;box-shadow:0 0 14px 3px hsl(196 40% 70% / .8)}22%{opacity:.6}26%{opacity:.45}}
.g-ec-howto-go{display:block;width:100%;margin:14px 0 0;padding:11px 14px;border-radius:12px;cursor:pointer;
  font:700 14px/1.2 var(--body);letter-spacing:.08em;text-transform:uppercase;color:var(--ground);
  background:linear-gradient(180deg, var(--pink), var(--pink-deep));border:0;
  box-shadow:0 6px 18px color-mix(in srgb, var(--pink), transparent 55%), inset 0 1px 0 rgba(255,255,255,.3);
  animation:g-ec-hw-pulse 1.8s ease-in-out infinite}
.g-ec-howto-go:hover,.g-ec-howto-go:focus-visible{filter:brightness(1.08);outline:2px solid color-mix(in srgb, var(--ink), transparent 40%);outline-offset:2px}
@keyframes g-ec-hw-pulse{50%{box-shadow:0 6px 26px color-mix(in srgb, var(--pink), transparent 30%), inset 0 1px 0 rgba(255,255,255,.3)}}

/* ---- reduced motion: transitions only, no keyframes -------------------- */
html.arc-reduced .g-ec-stage *{animation:none !important}
html.arc-reduced .g-ec-stage::before,html.arc-reduced .g-ec-backdrop::before,html.arc-reduced .g-ec-backdrop::after,
html.arc-reduced .g-ec-ring::before,html.arc-reduced .g-ec-face::after,html.arc-reduced .g-ec-media{animation:none !important}
html.arc-reduced .g-ec-face{transition:box-shadow .2s ease, border-color .2s ease}
html.arc-reduced .g-ec-pad[data-state="lit"] .g-ec-face,html.arc-reduced .g-ec-pad[data-state="pressed"] .g-ec-face{transform:none}
html.arc-reduced .g-ec-pad[data-state="decoy"] .g-ec-face{animation:none !important;opacity:.85}
html.arc-reduced .g-ec-media{opacity:.14}
html.arc-reduced .g-ec-howto-go{animation:none !important}
html.arc-reduced .g-ec-howto-art::before,html.arc-reduced .g-ec-howto-art::after{animation:none !important}
@media (prefers-reduced-motion: reduce){
  .g-ec-stage *{animation:none !important}
  .g-ec-stage::before,.g-ec-backdrop::before,.g-ec-backdrop::after,.g-ec-ring::before,.g-ec-face::after,.g-ec-media{animation:none !important}
  .g-ec-pad[data-state="lit"] .g-ec-face,.g-ec-pad[data-state="pressed"] .g-ec-face{transform:none}
}

/* ---- small screens: the ring shrinks, the HUD tightens ------------------ */
@media (max-width: 560px){
  .g-ec-stage{padding:64px 10px 10px;--ec-ring:min(calc(100dvh - 230px), calc(100vw - 28px), 640px)}
  .g-ec-chip{min-width:64px;padding:6px 9px 5px;font-size:11px}
  .g-ec-chip.g-ec-len{min-width:88px;font-size:22px}
  .g-ec-msg{font-size:13px;bottom:12px}
}
`;

/** Inject once per document. No-op headless (the DOM double has no head). */
export function injectEchoStyle() {
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

export { PAD_HUES };
export default injectEchoStyle;
