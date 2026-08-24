/* ============================================================================
 * vn/style.js - the FIRST BELL skin, injected once as <style id="arc-vn-style">.
 *
 * WHY NOT styles.css: that file is shell chrome ONLY (its own header says so),
 * and the VN is furniture that ships dark for every returning player. A game
 * injecting its own sheet is the precedent this follows, so the whole opening
 * can be lifted out in one commit without touching a shared file.
 *
 * TWO HOUSE LAWS OBEYED HERE
 *   trap 27  nothing below writes a bare display: on a node the shell toggles
 *            with the hidden attribute, and no competing [hidden] rule is added.
 *   trap 37  NEVER a backtick inside a CSS comment in a template literal - it
 *            ends the literal and the whole sheet dies with a ReferenceError
 *            that node --check cannot see.
 *
 * LAYERING: z-index 58. Above EMI (50, widget.css) so a caption is never split
 * by the mascot, BELOW the toast (60) so shell chatter still wins, and far below
 * the loader / nope card (70) - an error card may never be covered by a
 * celebration (trap 66).
 *
 * Every colour is a var() off the shell's own tokens, so init.palette reskins
 * the opening for free, exactly as it reskins the campus.
 * ==========================================================================*/

export const STYLE_ID = 'arc-vn-style';

export const STYLE_TEXT = `
.arc-vn {
  position:fixed; inset:0; z-index:58; background:#000;
  display:flex; align-items:center; justify-content:center;
  overflow:hidden; cursor:pointer;
  opacity:0; transition:opacity 600ms ease;
  -webkit-tap-highlight-color:transparent;
}
.arc-vn.is-up { opacity:1; }
.arc-vn.is-bare { background:rgba(6,6,16,.72); }

/* THE LETTERBOX. The plates are 1376x768, so the frame is pinned to 16:9 and
   the black bars are the black page under it, never a border drawn on top. */
.arc-vn-frame {
  position:relative; width:min(100vw, 177.78vh); height:min(56.25vw, 100vh);
  overflow:hidden;
}

/* THE PLATE, and the slow camera on it. The transform is the ONLY thing that
   moves (trap 36's law: patterns and stills drift by transform, never by
   background-position), and the plate is oversized so a 2% push or a 4% pan
   can never expose an edge. */
.arc-vn-bg {
  position:absolute; left:-3%; top:-3%; width:106%; height:106%;
  background-position:center; background-size:cover; background-repeat:no-repeat;
  transform:scale(1); transform-origin:52% 62%;
  transition:opacity 600ms ease;
  opacity:0;
}
.arc-vn-bg.is-lit { opacity:1; }
.arc-vn-bg[data-motion="push"] { animation:arc-vn-push 26s ease-out both; }
.arc-vn-bg[data-motion="pan"]  { animation:arc-vn-pan 18s linear both; }
@keyframes arc-vn-push { from { transform:scale(1); } to { transform:scale(1.045); } }
@keyframes arc-vn-pan  { from { transform:translateX(2.2%) scale(1.02); }
                         to   { transform:translateX(-2.2%) scale(1.02); } }

/* The arch neon coming on, pink, one buzz-tick (B2). A tinted wash over the
   plate: plain alpha, no filter over the image, no blend surface. */
.arc-vn-neon {
  position:absolute; inset:0; pointer-events:none; opacity:0;
  background:radial-gradient(120% 70% at 50% 22%,
    color-mix(in srgb, var(--pink), transparent 62%) 0%, transparent 62%);
}
.arc-vn-neon.is-on { animation:arc-vn-buzz 900ms steps(1,end) both; }
@keyframes arc-vn-buzz {
  0%,10%   { opacity:0; }
  14%      { opacity:.85; }
  20%      { opacity:0; }
  26%      { opacity:.7; }
  32%      { opacity:.08; }
  100%     { opacity:.55; }
}

/* ---- THE LOWER THIRD ---------------------------------------------------- */
.arc-vn-cap {
  position:absolute; left:50%; bottom:7%; transform:translateX(-50%) translateY(10px);
  width:min(74ch, 88%); box-sizing:border-box;
  padding:14px clamp(16px, 3vw, 28px);
  background:color-mix(in srgb, var(--navy), transparent 8%);
  border:1px solid color-mix(in srgb, var(--pink), transparent 62%);
  border-left:4px solid var(--pink);
  border-radius:var(--radius);
  box-shadow:0 10px 40px rgba(0,0,0,.6);
  color:var(--ink); font-family:var(--body); font-size:clamp(15px, 1.5vw, 20px);
  line-height:1.5; letter-spacing:.01em;
  opacity:0; transition:opacity 260ms ease, transform 260ms ease;
}
.arc-vn-cap.is-up { opacity:1; transform:translateX(-50%) translateY(0); }

/* ---- THE PAPER ---------------------------------------------------------- */
/* Cream card, navy rule, the shell's varsity display face for the head. It
   slides UP out of the desk tray and tucks back the same way. */
.arc-vn-paper {
  position:absolute; left:50%; top:50%; transform:translate(-50%, -46%) translateY(26px);
  width:min(58ch, 82%); max-height:82%; overflow:auto; box-sizing:border-box;
  padding:clamp(20px, 3.2vw, 34px) clamp(20px, 3.6vw, 40px) clamp(16px, 2.6vw, 28px);
  background:var(--ink);
  color:var(--navy);
  border-radius:3px;
  box-shadow:0 24px 60px rgba(0,0,0,.66), 0 0 0 1px rgba(0,0,0,.25);
  opacity:0; transition:opacity 300ms ease, transform 300ms ease;
}
.arc-vn-paper.is-up { opacity:1; transform:translate(-50%, -50%) translateY(0); }
.arc-vn-paper-title {
  margin:0 0 4px; font-family:var(--disp); font-size:clamp(19px, 2.3vw, 30px);
  letter-spacing:.06em; color:var(--navy);
}
.arc-vn-paper-rule {
  height:3px; margin:0 0 clamp(14px, 2vw, 20px);
  background:var(--navy); opacity:.85;
}
.arc-vn-paper p {
  margin:0 0 clamp(11px, 1.6vw, 16px);
  font-family:var(--body); font-size:clamp(14px, 1.35vw, 17px); line-height:1.62;
  color:color-mix(in srgb, var(--navy), black 6%);
}
.arc-vn-paper-sign {
  margin:clamp(14px, 2vw, 20px) 0 0; font-family:var(--mono);
  font-size:clamp(12px, 1.1vw, 14px); letter-spacing:.04em;
  color:color-mix(in srgb, var(--navy), transparent 22%);
}

/* ---- THE BOARD'S RESERVED WALL ZONE ------------------------------------- */
/* The plate paints a BARE panel here; the shipped split-flap board mounts into
   it and deals. Decorative theatre only: the live board the player actually
   clicks is the campus one under this layer, so the rows here take no pointer
   and no tab stop. */
.arc-vn-boardzone {
  position:absolute; pointer-events:none;
  display:flex; align-items:center; justify-content:center;
  opacity:0; transition:opacity 420ms ease;
}
.arc-vn-boardzone.is-up { opacity:1; }
.arc-vn-boardzone .board {
  /* The shell's board clamps to its container and grows a scrollbar when the
     rows are wider (its own overflow-x:auto). In the painted panel it must lay
     out at NATURAL width (width:max-content - the abs-pos clamp trap) so the
     scale(k) below is what fits it, and never a scrollbar. */
  width:max-content; max-width:none; flex:none; overflow:hidden;
  transform-origin:center center;
  box-shadow:0 0 0 3px rgba(0,0,0,.5), 0 18px 44px rgba(0,0,0,.65),
             0 0 34px color-mix(in srgb, var(--pink), transparent 78%);
}
.arc-vn-boardzone .brow { cursor:default; }
/* The wall warming up under the flaps, before the first row rolls. */
.arc-vn-boardglow {
  position:absolute; pointer-events:none; opacity:0;
  border-radius:14px;
  background:radial-gradient(70% 70% at 50% 50%,
    color-mix(in srgb, var(--pink), transparent 70%) 0%, transparent 70%);
  transition:opacity 520ms ease;
}
.arc-vn-boardglow.is-up { opacity:1; }

/* ---- THE HOLD-TO-SKIP PILL --------------------------------------------- */
/* The Esc-hold grammar, made visible: the same 1200ms reach for the door
   boot.js uses, with the fill showing how much of it is spent. It is on screen
   from frame one (ruling 4) and it never skips a shipped seam, only VN tissue. */
.arc-vn-skip {
  position:absolute; right:clamp(12px, 2.4vw, 30px); bottom:clamp(12px, 2.4vw, 28px);
  z-index:4; margin:0; padding:9px 16px;
  font:inherit; font-family:var(--body); font-size:13px; letter-spacing:.08em;
  text-transform:uppercase; color:var(--ink-dim);
  background:color-mix(in srgb, var(--navy), transparent 22%);
  border:1px solid var(--line); border-radius:999px;
  cursor:pointer; overflow:hidden; -webkit-user-select:none; user-select:none;
  transition:color 180ms ease, border-color 180ms ease;
  touch-action:manipulation;
}
.arc-vn-skip:hover, .arc-vn-skip:focus-visible { color:var(--ink); border-color:var(--pink); }
.arc-vn-skip:focus-visible { outline:2px solid var(--pink); outline-offset:2px; }
.arc-vn-skip-fill {
  position:absolute; left:0; top:0; bottom:0; width:0;
  background:color-mix(in srgb, var(--pink), transparent 62%);
  pointer-events:none;
}
.arc-vn-skip.is-holding .arc-vn-skip-fill { width:100%; transition:width 1200ms linear; }
.arc-vn-skip-label { position:relative; z-index:1; }

/* ---- THE TAP HINT ------------------------------------------------------- */
.arc-vn-tap {
  position:absolute; left:50%; bottom:2.4%; transform:translateX(-50%);
  font-family:var(--body); font-size:11px; letter-spacing:.14em; text-transform:uppercase;
  color:var(--ink-faint); pointer-events:none;
  opacity:0; transition:opacity 300ms ease;
}
.arc-vn-tap.is-up { opacity:.75; animation:arc-vn-breathe 2.6s ease-in-out infinite; }
@keyframes arc-vn-breathe { 0%,100% { opacity:.32; } 50% { opacity:.82; } }

/* ---- REDUCED MOTION: fades become cuts, the pan becomes a still ---------- */
/* Two seams, same treatment: the media query for the OS preference and
   html.arc-reduced for init.reducedMotion / motionLevel 0 (shell.js sets it). */
html.arc-reduced .arc-vn,
html.arc-reduced .arc-vn-bg,
html.arc-reduced .arc-vn-cap,
html.arc-reduced .arc-vn-paper,
html.arc-reduced .arc-vn-boardzone,
html.arc-reduced .arc-vn-boardglow { transition:none !important; }
html.arc-reduced .arc-vn-bg,
html.arc-reduced .arc-vn-neon,
html.arc-reduced .arc-vn-tap { animation:none !important; }
html.arc-reduced .arc-vn-neon.is-on { opacity:.55; }
html.arc-reduced .arc-vn-cap.is-up { transform:translateX(-50%); }
html.arc-reduced .arc-vn-paper.is-up { transform:translate(-50%, -50%); }
html.arc-reduced .arc-vn-skip.is-holding .arc-vn-skip-fill { transition:none; }
@media (prefers-reduced-motion: reduce) {
  .arc-vn, .arc-vn-bg, .arc-vn-cap, .arc-vn-paper,
  .arc-vn-boardzone, .arc-vn-boardglow { transition:none !important; }
  .arc-vn-bg, .arc-vn-neon, .arc-vn-tap { animation:none !important; }
  .arc-vn-neon.is-on { opacity:.55; }
  .arc-vn-cap.is-up { transform:translateX(-50%); }
  .arc-vn-paper.is-up { transform:translate(-50%, -50%); }
  .arc-vn-skip.is-holding .arc-vn-skip-fill { transition:none; }
}
`;

/**
 * Inject the sheet once. Idempotent, guarded, and a no-op on a platform with no
 * document (the node DOM double the suites drive) - the VN must never be the
 * reason an import throws.
 * @param {Object=} doc
 * @returns {boolean} true when the sheet is present after this call
 */
export function injectStyle(doc) {
  const d = doc || (typeof document !== 'undefined' ? document : null);
  if (!d || typeof d.createElement !== 'function') return false;
  try {
    if (d.getElementById && d.getElementById(STYLE_ID)) return true;
    const node = d.createElement('style');
    node.id = STYLE_ID;
    node.textContent = STYLE_TEXT;
    const head = d.head || d.body || d.documentElement;
    if (!head || typeof head.appendChild !== 'function') return false;
    head.appendChild(node);
    return true;
  } catch (e) { return false; }
}

export default injectStyle;
