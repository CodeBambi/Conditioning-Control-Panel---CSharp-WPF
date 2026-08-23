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
 *   .g-ec-phase     THE BANNER (owner verdict A, 2026-08-23). The one node that
 *                   answers "is it me?" from across the room: a drawn glyph
 *                   (listening waves / a reaching hand) plus one word, on its
 *                   own [data-p=ready|listen|yours|miss|clear|over] enum. It is
 *                   TEXT AND COLOUR, never an animation, so motionLevel 0 and
 *                   a single still frame both read it
 *   .g-ec-steps     THE STRIP: one .g-ec-step[data-fill=off|on|bad] per step of
 *                   the sequence. Fills as the room plays, empties on the
 *                   hand-off, fills again under the player, one dot goes red on
 *                   the press that broke it
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
 *     .g-ec-word    THE FACE ITSELF now: the trigger the pad is bound to,
 *                   centred on the glass and sized to be read across the room
 *                   (owner verdict C - the phrase IS the bubble). Empty on a pad
 *                   the pool could not fill, which is what data-face=glyph on
 *                   the PAD means. Still the ONE node a trickster may lie on;
 *                   the glyph, the colour and the place on the ring are the
 *                   truth (Law IV).
 *                   ROUND 2 - it is FROSTED at rest (data-veil=on) and its size
 *                   is FITTED: --ec-word-px, one size for the whole ring,
 *                   written by index.js's fitWords after measuring the glass.
 *                   The clamp below is only the pre-layout / headless floor
 *     .g-ec-hint    the reveal caption ("this one"), drawn on every pad and
 *                   shown ONLY under data-state=reveal
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
 * data-tier, data-faces (words|glyphs|media), data-audible (1|0 - the audio
 * tell), data-reduced, data-telegraph (tier 2's decoy warning), data-encore,
 * data-handoff (the one beat where the turn changes hands). PAD attributes:
 * data-pad, data-state, data-glyph and data-face (word|glyph - what THIS pad
 * actually wears, because a partial trigger pool fills some pads and not others).
 *
 * PAD STATES (the lamp ladder): idle = a dim ember that breathes; lit = the
 * lamp full on, glass bloom, spill and floor pool up, a 90ms attack and a
 * slow 600ms decay so a fast playback still reads as separate notes; pressed =
 * the same light plus the face squashes (scale .94 on the FACE) and a ripple
 * ring runs out from the point of contact; decoy = a COLD light - the hue
 * rolls toward cyan, saturation drops, the glass flickers in steps and the
 * spill goes grey-blue - the telegraphed tell of tier 2 (tier 3+ the core
 * simply marks the decoy lit, and nothing here can tell the difference,
 * which is the point); wrong = the press that broke it, the whole pad forced
 * RED with the face shaking; reveal = the pad you NEEDED, held bright inside a
 * pulsing halo with its "this one" caption up (owner verdict B).
 *
 * THE LISTEN LOCK (owner verdict A). While the room plays, the ring is visibly
 * NOT yours: the pads lose their saturation through --ec-sat (a gradient
 * recompute, never a filter over a live decode - trap 36), the chalk circle
 * dims, and the cursor says not-allowed. The pad currently lit is exempt, so
 * the sequence is the only colour in the room. On the hand-off the whole ring
 * sweeps back to full in one beat (data-handoff).
 *
 * THE PAD SIZE (owner verdict C - "bigger bubbles"). Six pads on a circle is a
 * closed geometry problem: the pad diameter can never exceed the chord between
 * two neighbours (which for six pads is exactly the ring radius R), and
 * R + pad/2 has to stay inside the ring box. --ec-r .34 / --ec-pad .315 sits
 * just under both, and the stage's fixed chrome was cut to 232px to pay for the
 * banner and the strip, so the pad lands 1.28x - 1.51x its old diameter
 * depending on the viewport (1.51x at 1920x1080, ~1.3x where height binds).
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
  align-items:center;gap:6px;padding:64px 14px 12px;color:var(--ink);
  --ec-ring:min(calc(100dvh - 232px), calc(100vw - 40px), 780px);
  --ec-pad:calc(var(--ec-ring) * .315);
  --ec-r:calc(var(--ec-ring) * .34);
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

/* ---- THE PHASE BANNER: whose turn it is, in one word ------------------- */
/* Colour, weight and a drawn glyph all carry the same message, so the banner
   survives a greyscale screenshot, a muted room and motionLevel 0 alike. */
.g-ec-phase{position:relative;z-index:5;display:flex;align-items:center;justify-content:center;gap:12px;
  min-height:38px;padding:5px 22px;border-radius:999px;
  font-family:var(--disp);font-size:clamp(15px, 2.4vh, 25px);letter-spacing:.16em;text-transform:uppercase;
  color:var(--ink-dim);
  background:color-mix(in srgb, var(--panel), transparent 34%);
  border:1px solid color-mix(in srgb, var(--line), transparent 24%);
  box-shadow:0 8px 22px rgba(0,0,0,.42);
  transition:color .22s ease, background .22s ease, border-color .22s ease, box-shadow .22s ease}
.g-ec-phase-text{display:block;line-height:1.1;white-space:nowrap}
/* THE DRAWN HALF. One empty span; the state decides what it is. */
.g-ec-phase-glyph{position:relative;flex:0 0 auto;width:22px;height:22px;pointer-events:none}
/* LISTEN: three arcs leaving a point - a sound coming AT you, not from you */
.g-ec-phase[data-p="listen"]{color:var(--lav);
  border-color:color-mix(in srgb, var(--lav), transparent 52%);
  background:color-mix(in srgb, var(--navy), transparent 20%)}
.g-ec-phase[data-p="listen"] .g-ec-phase-glyph::before{content:"";position:absolute;left:2px;top:50%;
  width:7px;height:7px;border-radius:50%;transform:translateY(-50%);background:var(--lav)}
.g-ec-phase[data-p="listen"] .g-ec-phase-glyph::after{content:"";position:absolute;left:8px;top:50%;
  width:14px;height:14px;transform:translateY(-50%);border-radius:50%;
  border:2px solid var(--lav);border-left-color:transparent;border-top-color:transparent;
  box-shadow:3px 0 0 -1px color-mix(in srgb, var(--lav), transparent 45%);
  animation:g-ec-listenwave 1.6s ease-in-out infinite}
@keyframes g-ec-listenwave{0%,100%{opacity:.45}50%{opacity:1}}
/* YOUR TURN: the room hands it over - pink, lit, a pointer coming down */
.g-ec-phase[data-p="yours"]{color:var(--ground);
  background:linear-gradient(180deg, var(--pink), var(--pink-deep));
  border-color:color-mix(in srgb, var(--pink), white 20%);
  box-shadow:0 8px 26px color-mix(in srgb, var(--pink), transparent 46%),
    inset 0 1px 0 rgba(255,255,255,.34);
  animation:g-ec-yours .9s ease-out 1}
@keyframes g-ec-yours{0%{transform:scale(.94)}55%{transform:scale(1.045)}100%{transform:scale(1)}}
.g-ec-phase[data-p="yours"] .g-ec-phase-glyph::before{content:"";position:absolute;left:9px;top:1px;
  width:4px;height:12px;border-radius:2px;background:var(--ground)}
.g-ec-phase[data-p="yours"] .g-ec-phase-glyph::after{content:"";position:absolute;left:4px;top:9px;
  width:14px;height:12px;border-radius:3px 3px 6px 6px;background:var(--ground)}
/* THE VERDICTS. A miss is red and a clear is gold - two colours nothing else
   in this room uses, so the banner alone answers "did I get it right". */
.g-ec-phase[data-p="miss"]{color:hsl(4 96% 88%);
  background:linear-gradient(180deg, hsl(4 60% 34%) 0%, hsl(4 70% 22%) 100%);
  border-color:hsl(4 90% 62% / .85);
  box-shadow:0 8px 26px hsl(4 90% 50% / .34), inset 0 1px 0 hsl(4 90% 70% / .35)}
.g-ec-phase[data-p="miss"] .g-ec-phase-glyph::before,
.g-ec-phase[data-p="miss"] .g-ec-phase-glyph::after{content:"";position:absolute;left:2px;top:9px;
  width:18px;height:3px;border-radius:2px;background:hsl(4 96% 84%)}
.g-ec-phase[data-p="miss"] .g-ec-phase-glyph::before{transform:rotate(45deg)}
.g-ec-phase[data-p="miss"] .g-ec-phase-glyph::after{transform:rotate(-45deg)}
.g-ec-phase[data-p="clear"]{color:var(--ground);
  background:linear-gradient(180deg, color-mix(in srgb, var(--gold), white 22%), var(--gold));
  border-color:color-mix(in srgb, var(--gold), white 30%);
  box-shadow:0 8px 26px color-mix(in srgb, var(--gold), transparent 48%), inset 0 1px 0 rgba(255,255,255,.4)}
/* a tick, drawn as two bars */
.g-ec-phase[data-p="clear"] .g-ec-phase-glyph::before{content:"";position:absolute;left:3px;top:11px;
  width:8px;height:3px;border-radius:2px;background:var(--ground);transform:rotate(45deg)}
.g-ec-phase[data-p="clear"] .g-ec-phase-glyph::after{content:"";position:absolute;left:8px;top:8px;
  width:13px;height:3px;border-radius:2px;background:var(--ground);transform:rotate(-52deg)}
.g-ec-phase[data-p="ready"],.g-ec-phase[data-p="over"]{color:var(--ink-faint)}

/* ---- THE STEP STRIP: N of len, drawn -------------------------------------- */
.g-ec-steps{position:relative;z-index:5;display:flex;flex-wrap:wrap;justify-content:center;
  align-items:center;gap:7px;min-height:14px;max-width:min(92vw, 620px);pointer-events:none}
.g-ec-step{display:block;width:12px;height:12px;border-radius:50%;
  background:color-mix(in srgb, var(--ink), transparent 88%);
  border:1px solid color-mix(in srgb, var(--line), transparent 30%);
  transition:background .18s ease, border-color .18s ease, box-shadow .18s ease, transform .18s ease}
.g-ec-step[data-fill="on"]{background:var(--lav);border-color:color-mix(in srgb, var(--lav), white 30%);
  box-shadow:0 0 10px color-mix(in srgb, var(--lav), transparent 40%);transform:scale(1.12)}
.g-ec-stage[data-phase="input"] .g-ec-step[data-fill="on"]{background:var(--pink);
  border-color:color-mix(in srgb, var(--pink), white 30%);
  box-shadow:0 0 10px color-mix(in srgb, var(--pink), transparent 40%)}
.g-ec-step[data-fill="bad"]{background:hsl(4 88% 58%);border-color:hsl(4 96% 76%);
  box-shadow:0 0 12px hsl(4 92% 56% / .7);transform:scale(1.2)}
.g-ec-steps:empty{opacity:0}

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
   screen-blended into the lamp, drifting (ken-burns), tinted by the pad.
   OPT-IN ONLY since the owner's verdict: nothing is dealt into it unless
   data-faces="media", so the default ring never wears a gif again. */
.g-ec-media{position:absolute;inset:0;width:100%;height:100%;object-fit:cover;border-radius:50%;
  opacity:0;mix-blend-mode:screen;transition:opacity .8s ease;
  filter:saturate(.7) sepia(.2) hue-rotate(calc(var(--ec-h) * 1deg - 40deg));
  animation:g-ec-kb var(--ec-kb) ease-in-out infinite alternate;
  animation-delay:calc(var(--ec-a,0deg) / 360deg * -26s)}
@keyframes g-ec-kb{from{transform:scale(1.02) translate(0,0)}to{transform:scale(1.1) translate(2%,-1.6%)}}
.g-ec-stage[data-faces="media"] .g-ec-pad.is-loaded .g-ec-media{opacity:calc(.16 + .16 * var(--ec-lamp))}
/* THE GLYPH: the truth node. A drawn mark in the pad's colour. On a pad that
   wears a trigger it steps DOWN to a small mark at the rim (the phrase is the
   face now); on a pad the pool could not fill it is the face. */
.g-ec-glyph{position:absolute;left:50%;top:50%;transform:translate(-50%,-52%);pointer-events:none;
  font:400 clamp(20px, calc(var(--ec-pad) * .36), 52px)/1 var(--body,system-ui,sans-serif);
  color:hsl(var(--ec-h) 90% calc(84% + 10% * var(--ec-lamp)));
  text-shadow:0 0 calc(4px + 14px * var(--ec-lamp)) hsl(var(--ec-h) 95% 70% / .9), 0 1px 0 rgba(0,0,0,.5);
  transition:transform .3s ease, font-size .3s ease}
.g-ec-pad[data-face="word"] .g-ec-glyph{top:auto;bottom:9%;transform:translate(-50%,0);
  font-size:clamp(11px, calc(var(--ec-pad) * .12), 20px);opacity:.72}
/* THE WORD IS THE BUBBLE (owner verdict C). The trigger the pad is bound to,
   centred on the glass, wrapping onto up to three lines and sized off the pad
   so it grows with it. Still the ONE node a trickster may lie on; the glyph,
   the colour and the place on the ring stay the truth. */
.g-ec-word{position:absolute;left:50%;top:47%;transform:translate(-50%,-50%);pointer-events:none;
  width:80%;padding:0;border-radius:0;background:none;
  /* THE FITTED SIZE. index.js measures the glass and writes --ec-word-px on the
     stage; the clamp is the floor before that lands (and headless, where there
     is no layout to measure). The BOX is three lines tall in em, so it is three
     lines at ANY size the fit search picks - that is what lets the search ask
     one question ("does the wrapped phrase fit three lines of THIS size") and
     never have to cut. */
  font-family:var(--disp);
  font-size:var(--ec-word-px, clamp(11px, calc(var(--ec-pad) * .155), 30px));
  line-height:1.1;max-height:3.4em;
  letter-spacing:.01em;text-transform:uppercase;text-align:center;
  overflow-wrap:anywhere;overflow:hidden;
  color:hsl(var(--ec-h) 92% calc(88% + 8% * var(--ec-lamp)));
  text-shadow:0 0 calc(5px + 16px * var(--ec-lamp)) hsl(var(--ec-h) 95% 70% / .95),
    0 2px 4px rgba(0,0,0,.85), 0 0 2px rgba(0,0,0,.9);
  transition:color var(--ec-veil,.2s) ease, text-shadow var(--ec-veil,.2s) ease, opacity .3s ease}
.g-ec-word:empty{opacity:0}

/* THE RULER (index.js measureTokens). Never seen: it is parked off-canvas at a
   reference 100px and asked how wide a single word is, so the fit search knows
   when a size would split one. Its type is COPIED off the live word node at
   measure time - these declarations are only the floor, because a ruler that
   keeps its own copy of the type drifts the moment .g-ec-word changes. */
.g-ec-ruler{position:absolute;left:-9999px;top:0;visibility:hidden;pointer-events:none;
  white-space:pre;font-family:var(--disp);font-size:100px;letter-spacing:.01em;
  text-transform:uppercase;line-height:1}

/* ---- THE VEIL: the phrase is frosted until you look at it --------------- */
/* Owner round 2: "the inside of the bubble is blurred till we mouse over it".
   This is NOT filter:blur() - a filter would mint a render surface per pad and
   sit over the pad's animated lamp (trap 36, and trap 42 charges WebKit for it
   twice over). color:transparent plus a text-shadow of the SAME letterforms is
   the same picture for free: it rasterises with the text, adds no surface, and
   both properties interpolate, so the frost melts instead of snapping. */
.g-ec-pad[data-veil="on"] .g-ec-word{color:transparent;
  text-shadow:0 0 .52em hsl(var(--ec-h) 92% 88% / .72),
    0 0 1.05em hsl(var(--ec-h) 95% 72% / .5),
    0 1px .5em rgba(0,0,0,.6)}
/* THE UNVEIL. Same specificity as the rule above (class + attribute + class vs
   class + pseudo-class + class), so ORDER is what decides - these must stay
   BELOW it. index.js clears data-veil for every non-idle state; these three add
   the pointer, the finger and the keyboard on top of that. */
.g-ec-pad:hover .g-ec-word,
.g-ec-pad:focus-visible .g-ec-word,
.g-ec-pad:focus .g-ec-word,
.g-ec-pad:active .g-ec-word,
.g-ec-pad[data-veil="off"] .g-ec-word{color:hsl(var(--ec-h) 92% calc(88% + 8% * var(--ec-lamp)));
  text-shadow:0 0 calc(5px + 16px * var(--ec-lamp)) hsl(var(--ec-h) 95% 70% / .95),
    0 2px 4px rgba(0,0,0,.85), 0 0 2px rgba(0,0,0,.9)}
/* The GLYPH is the truth node and is NEVER veiled (Law IV) - it is how you know
   which pad is which while the phrase is still frosted. */
/* THE REVEAL CAPTION. On every pad, shown only when the pad IS the answer. */
.g-ec-hint{position:absolute;left:50%;top:calc(100% + 6px);transform:translate(-50%,0);pointer-events:none;
  padding:2px 9px;border-radius:8px;white-space:nowrap;opacity:0;
  font-family:var(--mono);font-size:clamp(9px, calc(var(--ec-pad) * .085), 14px);
  letter-spacing:.14em;text-transform:uppercase;color:var(--ground);
  background:var(--gold);box-shadow:0 4px 14px color-mix(in srgb, var(--gold), transparent 50%);
  transition:opacity .18s ease}
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
/* ---- WRONG: the press that broke it (owner verdict B) ------------------ */
/* The whole pad is forced red - hue, saturation and lamp - so no colour-blind
   reading of "which pad is that" is needed: it is the one that just shook. */
.g-ec-pad[data-state="wrong"]{--ec-lamp:1;--ec-h:4;--ec-sat:92%;transition:--ec-lamp .04s ease-out}
.g-ec-pad[data-state="wrong"] .g-ec-face{border-color:hsl(4 96% 70%);
  box-shadow:0 10px 28px rgba(0,0,0,.5), 0 0 0 3px hsl(4 96% 62% / .85),
    0 0 42px hsl(4 96% 58% / .8), inset 0 -10px 22px rgba(0,0,0,.5);
  animation:g-ec-shake .34s cubic-bezier(.36,.07,.19,.97) 1}
@keyframes g-ec-shake{
  10%,90%{transform:translateX(-3%)}
  20%,80%{transform:translateX(5%)}
  30%,50%,70%{transform:translateX(-7%)}
  40%,60%{transform:translateX(7%)}
  100%{transform:translateX(0)}}
.g-ec-pad[data-state="wrong"] .g-ec-word,.g-ec-pad[data-state="wrong"] .g-ec-glyph{color:hsl(4 96% 92%)}

/* ---- REVEAL: the pad you NEEDED, and it says so ------------------------- */
/* Held bright for REVEAL_MS inside a halo that pulses out twice, with the
   caption up. Nothing else in the room uses gold on a pad. */
.g-ec-pad[data-state="reveal"]{--ec-lamp:1;transition:--ec-lamp .07s ease-out;z-index:2}
.g-ec-pad[data-state="reveal"] .g-ec-face{transform:scale(1.05);
  border-color:hsl(46 96% 70%);
  box-shadow:0 10px 28px rgba(0,0,0,.5), 0 0 0 3px hsl(46 96% 62% / .9),
    0 0 46px hsl(46 96% 60% / .75), inset 0 -10px 22px rgba(0,0,0,.5)}
.g-ec-pad[data-state="reveal"]::before{opacity:1;border-color:hsl(46 96% 74% / .95);
  animation:g-ec-halo .7s ease-out 2}
@keyframes g-ec-halo{0%{opacity:.95;transform:scale(.96)}100%{opacity:0;transform:scale(1.55)}}
.g-ec-pad[data-state="reveal"] .g-ec-hint{opacity:1}

/* ---- THE LISTEN LOCK: the ring is visibly NOT yours --------------------- */
/* --ec-sat is a gradient term, so this is a repaint, never a filter over a
   live decode (trap 36). The pad currently playing keeps its colour: during
   LISTEN the sequence is the only lit thing in the room. */
.g-ec-stage[data-phase="echo"] .g-ec-pad,
.g-ec-stage[data-phase="play"] .g-ec-pad,
.g-ec-stage[data-phase="encore"] .g-ec-pad{--ec-sat:22%;cursor:not-allowed}
.g-ec-stage[data-phase="echo"] .g-ec-pad[data-state="lit"],
.g-ec-stage[data-phase="play"] .g-ec-pad[data-state="lit"],
.g-ec-stage[data-phase="encore"] .g-ec-pad[data-state="lit"],
.g-ec-stage[data-phase="echo"] .g-ec-pad[data-state="decoy"],
.g-ec-stage[data-phase="encore"] .g-ec-pad[data-state="decoy"]{--ec-sat:88%}
/* The LISTEN lock no longer dims the words - the VEIL already hides them, and
   dimming a frosted word twice just made the lit one look washed out. */
.g-ec-stage[data-phase="echo"] .g-ec-pad[data-state="lit"] .g-ec-word,
.g-ec-stage[data-phase="encore"] .g-ec-pad[data-state="lit"] .g-ec-word{opacity:1}
.g-ec-stage[data-phase="echo"] .g-ec-ring,
.g-ec-stage[data-phase="play"] .g-ec-ring,
.g-ec-stage[data-phase="encore"] .g-ec-ring{box-shadow:inset 0 0 90px rgba(0,0,0,.42)}

/* ---- THE HAND-OFF: one beat, the ring comes back up -------------------- */
.g-ec-stage[data-handoff="1"] .g-ec-ring{
  box-shadow:0 0 0 2px color-mix(in srgb, var(--pink), transparent 40%),
    0 0 70px color-mix(in srgb, var(--pink), transparent 62%);
  animation:g-ec-handoff .5s ease-out 1}
@keyframes g-ec-handoff{0%{transform:scale(.985)}45%{transform:scale(1.012)}100%{transform:scale(1)}}

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

/* ---- the stamp well: the shell's stamp, over the middle of the ring ----- */
/* .arc-stamp carries its own rotate transform (and a pop keyframe that rewrites
   it), so it is CENTRED BY ITS HOST rather than by a transform of our own. */
.g-ec-stampwell{position:absolute;left:50%;top:50%;width:100%;height:0;
  transform:translate(-50%,-50%);pointer-events:none;z-index:7;
  display:flex;align-items:center;justify-content:center}
.g-ec-stampwell *{pointer-events:none}
/* Echo's stamp is a whole-round verdict, so it is sized like one. */
.g-ec-stampwell .arc-stamp{font-size:clamp(18px, calc(var(--ec-ring) * .05), 34px);
  padding:8px 20px;letter-spacing:.2em}
/* the shell's CSS floor knows "pink"; a MISS needs the one colour it does not. */
.g-ec-stampwell .arc-stamp.g-ec-stamp-bad{color:hsl(4 96% 82%);border-color:hsl(4 92% 62%);
  box-shadow:0 0 18px hsl(4 92% 56% / .5);background:rgba(38,8,14,.82)}

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
/* THE TURN STILL READS WITH EVERY ANIMATION OFF. The banner keeps its colour
   and its word, the dots keep their fill, the wrong pad keeps its red ring and
   the reveal keeps its halo ring - only the movement goes. */
html.arc-reduced .g-ec-pad[data-state="wrong"] .g-ec-face{animation:none !important}
html.arc-reduced .g-ec-pad[data-state="reveal"]::before{animation:none !important;opacity:.9;transform:scale(1.16)}
html.arc-reduced .g-ec-phase{animation:none !important}
/* The veil still WORKS with motion off - it is a colour, not a movement - but
   it stops crossfading, so a frosted word snaps clear instead of melting. */
html.arc-reduced .g-ec-stage{--ec-veil:0s}
html.arc-reduced .g-ec-phase-glyph::after{animation:none !important;opacity:1}
html.arc-reduced .g-ec-step{transition:background .2s ease, border-color .2s ease}
html.arc-reduced .g-ec-step[data-fill="on"],html.arc-reduced .g-ec-step[data-fill="bad"]{transform:none}
html.arc-reduced .g-ec-media{opacity:.14}
html.arc-reduced .g-ec-howto-go{animation:none !important}
html.arc-reduced .g-ec-howto-art::before,html.arc-reduced .g-ec-howto-art::after{animation:none !important}
@media (prefers-reduced-motion: reduce){
  .g-ec-stage *{animation:none !important}
  .g-ec-stage::before,.g-ec-backdrop::before,.g-ec-backdrop::after,.g-ec-ring::before,.g-ec-face::after,.g-ec-media{animation:none !important}
  .g-ec-pad[data-state="lit"] .g-ec-face,.g-ec-pad[data-state="pressed"] .g-ec-face{transform:none}
  .g-ec-pad[data-state="wrong"] .g-ec-face{animation:none !important}
  .g-ec-pad[data-state="reveal"]::before{animation:none !important;opacity:.9;transform:scale(1.16)}
  .g-ec-step[data-fill="on"],.g-ec-step[data-fill="bad"]{transform:none}
}

/* ---- small screens: the ring shrinks, the HUD tightens ------------------ */
@media (max-width: 560px){
  .g-ec-stage{padding:58px 8px 8px;--ec-ring:min(calc(100dvh - 214px), calc(100vw - 20px), 640px)}
  .g-ec-chip{min-width:64px;padding:6px 9px 5px;font-size:11px}
  .g-ec-chip.g-ec-len{min-width:88px;font-size:22px}
  .g-ec-msg{font-size:13px;bottom:12px}
  .g-ec-phase{min-height:32px;padding:4px 14px;gap:9px;font-size:clamp(13px, 2.1vh, 19px)}
  .g-ec-phase-glyph{width:18px;height:18px}
  .g-ec-steps{gap:5px;min-height:11px}
  .g-ec-step{width:9px;height:9px}
}
/* A SHORT window (a 4:3 rig, a half-height desktop) loses the strip's own row
   before it loses the ring: the dots ride tighter rather than the pads shrinking. */
@media (max-height: 700px){
  .g-ec-stage{padding-top:56px;gap:4px}
  .g-ec-phase{min-height:32px;font-size:clamp(13px, 2.6vh, 20px)}
  .g-ec-steps{gap:5px}
  .g-ec-step{width:10px;height:10px}
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
