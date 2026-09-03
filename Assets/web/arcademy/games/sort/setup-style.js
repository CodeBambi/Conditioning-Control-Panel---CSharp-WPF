/* ============================================================================
 * games/sort/setup-style.js - THE DOOR's sheet.
 *
 * Exported as a STRING, not injected: G1's style loader concatenates SETUP_CSS
 * onto the game's own SORT_CSS and injects one <style id="g-sort-style">, so a
 * class only ever mints one sheet.
 *
 * LAWS THIS SHEET KEEPS
 *   - EVERY rule is scoped under `.g-sort-door`, and that is not a comment, it
 *     is the file's whole safety model. The two sheets share the `.g-sort-*`
 *     namespace and the door's half is concatenated SECOND, so an unscoped
 *     door rule beats the room's rule of the same specificity and paints
 *     inside the class. That is exactly what happened: an unscoped
 *     `.g-sort-glyph` put a 78x58 lavender box on every YES / NO stamp and an
 *     unscoped `.g-sort-note` hung a grey pill off the bottom of the stage.
 *     Never write a bare `.g-sort-x` selector here again.
 *   - The leak runs BOTH ways, and only one of them scoping can fix. The room's
 *     sheet is written bare, so its `.g-sort-note` (an absolutely positioned
 *     pill) still reaches the door's note line; the door answers that where it
 *     matters with an explicit reset, never by re-scoping someone else's file.
 *   - THE STRIP OWNS THE TOP 56px (styles.css CLASS RUNNER). The shell pins
 *     `.arc-proctor` at top 0 with z-index 30 over every class, so the door's
 *     head starts BELOW it (64px, the same number games/impulse-control uses).
 *   - every colour is a var() off a shell token (styles.css :root), so
 *     init.palette re-skins the door for free.
 *   - NO BACKTICK IN A CSS COMMENT (trap 37): this file is a template literal
 *     and a stray backtick ends it early, which node --check happily passes.
 *   - patterns drift by TRANSFORM, never by background-position (trap 36).
 *   - nothing writes a bare display: on a node the shell toggles with the
 *     hidden attribute (trap 27). Where the door DOES set a display on such a
 *     node (the lamp rail), it pays for it with an explicit [hidden] rule.
 *   - touch sizing rides a single dial, --sd-hit, which the door raises on
 *     ctx.platform.isTouch.
 * ==========================================================================*/

export const SETUP_CSS = `
.g-sort-door{
  --sd-hit:34px;              /* minimum tap height for a chip */
  --sd-target:var(--pink);
  --sd-noise:var(--slate);
  --sd-lib:var(--gold);
  --sd-strip:64px;            /* the proctor strip's floor - nothing above it */
  position:absolute; inset:0; overflow:auto;
  display:flex; flex-direction:column;
  background:radial-gradient(120% 80% at 50% 0%, color-mix(in srgb,var(--panel),black 18%), var(--ground) 70%);
  color:var(--ink); font-family:var(--body);
}
.g-sort-door[data-touch="1"]{ --sd-hit:44px; }

/* ---------------------------------------------------------------- header -- */
/* The head starts under the strip. At 18px the title sat at y18..65 and the
   shell's own chrome drew straight over it. */
.g-sort-door .g-sort-head{ padding:var(--sd-strip) 22px 10px; text-align:center; }
.g-sort-door .g-sort-title{ margin:0; font-family:var(--disp); font-size:clamp(20px,3.4vw,30px); letter-spacing:.06em; color:var(--ink); }
.g-sort-door .g-sort-sub{ margin:6px 0 0; color:var(--ink-dim); font-size:14px; }
.g-sort-door .g-sort-tut{ margin:8px auto 0; max-width:46ch; color:var(--lav); font-size:13px; line-height:1.5; }

/* the three step lamps. The rail never moves, only which lamp is lit. */
.g-sort-door .g-sort-rail{ display:flex; gap:8px; justify-content:center; align-items:center; margin:12px 0 4px; }
/* THE RAIL PAYS FOR ITS OWN display (trap 27). Three unlit lamps on a step that
   is not one of the three is a rail describing a journey the player is not on,
   so the "same sort" step hides it - and a bare display:flex would have won
   over the UA's [hidden] rule and shown it anyway. */
.g-sort-door .g-sort-rail[hidden]{ display:none; }
.g-sort-door .g-sort-lamp{
  min-width:64px; padding:4px 10px; border-radius:999px;
  border:1px solid var(--line); background:var(--panel);
  color:var(--ink-faint); font-size:11px; letter-spacing:.08em; text-transform:uppercase;
}
.g-sort-door .g-sort-lamp[data-on="1"]{ border-color:var(--pink); color:var(--pink); box-shadow:0 0 10px rgba(255,105,180,.28); }
.g-sort-door .g-sort-lamp[data-done="1"]{ border-color:var(--lav); color:var(--lav); }

/* ------------------------------------------------------------------ body -- */
/* THE COLUMN SITS IN THE MIDDLE OF WHAT IS LEFT. Top-anchored, a source step on
   a 1600x900 panel left ~680px of dead room under two doors. safe center is
   load-bearing, not decoration: plain center would push the top of a tall step
   out of the scroll container's reach. */
.g-sort-door .g-sort-body{
  flex:1 1 auto; padding:6px 22px 8px; display:flex; flex-direction:column;
  gap:14px; align-items:center; justify-content:safe center;
}
.g-sort-door .g-sort-panel{
  width:min(760px,100%); background:var(--panel); border:var(--hairline);
  border-radius:var(--radius); padding:14px 16px;
}
.g-sort-door .g-sort-panel[data-side="target"]{ border-color:var(--sd-target); box-shadow:0 0 0 1px rgba(255,105,180,.16) inset; }
.g-sort-door .g-sort-panel[data-side="noise"]{ border-color:var(--sd-noise); }
.g-sort-door .g-sort-h{ margin:0 0 2px; font-family:var(--disp); font-size:16px; letter-spacing:.05em; }
.g-sort-door .g-sort-hint{ margin:0 0 10px; color:var(--ink-dim); font-size:13px; line-height:1.5; }
.g-sort-door .g-sort-sub-h{ margin:12px 0 6px; color:var(--ink-faint); font-size:11px; letter-spacing:.1em; text-transform:uppercase; }

/* --------------------------------------------------------- the two doors -- */
.g-sort-door .g-sort-doors{ display:flex; gap:16px; flex-wrap:wrap; justify-content:center; width:min(760px,100%); }
.g-sort-door .g-sort-door-card{
  flex:1 1 220px; min-height:190px; padding:16px; cursor:pointer;
  display:flex; flex-direction:column; align-items:center; justify-content:center; gap:10px;
  background:var(--panel); border:2px solid var(--line); border-radius:var(--radius);
  color:var(--ink); font:inherit; text-align:center;
}
.g-sort-door .g-sort-door-card:hover:not(:disabled){ border-color:var(--pink); box-shadow:0 0 16px rgba(255,105,180,.22); }
.g-sort-door .g-sort-door-card:focus-visible{ outline:2px solid var(--pink); outline-offset:2px; }
.g-sort-door .g-sort-door-card:disabled{ opacity:.42; cursor:default; }
.g-sort-door .g-sort-door-name{ font-family:var(--disp); font-size:17px; letter-spacing:.05em; }
.g-sort-door .g-sort-door-why{ color:var(--ink-faint); font-size:12px; max-width:24ch; }

/* The drawn glyphs: a web tile and a folder, both pure CSS, no art needed.
   SCOPED, or the ::before box lands on the room's stamps as well. */
.g-sort-door .g-sort-glyph{ width:78px; height:58px; position:relative; }
.g-sort-door .g-sort-glyph::before{ content:''; position:absolute; inset:0; border-radius:8px; border:2px solid var(--lav); }
.g-sort-door .g-sort-glyph[data-kind="web"]::after{
  content:''; position:absolute; left:6px; right:6px; top:16px; height:2px;
  background:var(--lav);
  box-shadow:0 10px 0 var(--lav), 0 20px 0 var(--pink);
}
.g-sort-door .g-sort-glyph[data-kind="web"]::before{ border-top-width:12px; }
.g-sort-door .g-sort-glyph[data-kind="folder"]::before{ border-radius:4px 8px 8px 8px; top:10px; }
.g-sort-door .g-sort-glyph[data-kind="folder"]::after{
  content:''; position:absolute; left:0; top:0; width:34px; height:14px;
  border:2px solid var(--lav); border-bottom:none; border-radius:4px 6px 0 0;
}

/* ----------------------------------------------------------------- chips -- */
.g-sort-door .g-sort-chips{ display:flex; flex-wrap:wrap; gap:7px; }
.g-sort-door .g-sort-chip{
  min-height:var(--sd-hit); padding:5px 11px; border-radius:999px; cursor:pointer;
  display:inline-flex; align-items:center; gap:6px;
  background:transparent; border:1px solid var(--line); color:var(--ink-dim);
  font:inherit; font-size:13px; line-height:1.2;
}
.g-sort-door .g-sort-chip:hover:not(:disabled){ border-color:var(--lav); color:var(--ink); }
.g-sort-door .g-sort-chip:focus-visible{ outline:2px solid var(--pink); outline-offset:2px; }
.g-sort-door .g-sort-chip:disabled{ opacity:.38; cursor:default; }
.g-sort-door .g-sort-chip[data-on="1"]{ border-color:var(--sd-target); color:var(--ink); background:rgba(255,105,180,.16); }
.g-sort-door .g-sort-panel[data-side="noise"] .g-sort-chip[data-on="1"]{ border-color:var(--lav); background:rgba(184,166,232,.16); }
.g-sort-door .g-sort-chip[data-lib="1"]{ border-color:var(--sd-lib); color:var(--gold); }
.g-sort-door .g-sort-chip[data-lib="1"][data-on="1"]{ background:rgba(240,194,75,.18); color:var(--ink); }
.g-sort-door .g-sort-chip[data-state="dead"]{ opacity:.3; text-decoration:line-through; }
.g-sort-door .g-sort-chip[data-state="probing"]{ border-style:dashed; }
.g-sort-door .g-sort-count{ font-family:var(--mono); font-size:11px; color:var(--ink-faint); }
.g-sort-door .g-sort-chip[data-on="1"] .g-sort-count{ color:var(--ink-dim); }

/* THE PILL IS A WRAPPER, AND THE X IS THE CHIP'S SIBLING. A button inside a
   button is invalid content in a real browser and a screen reader cannot
   address the inner one, so the remove control sits beside the toggle inside
   .g-sort-pill and the hover / focus reveal hangs off the WRAPPER. */
.g-sort-door .g-sort-pill{ display:inline-flex; align-items:center; gap:2px; }
.g-sort-door .g-sort-pill .g-sort-chip{ padding-right:6px; }
.g-sort-door .g-sort-x{
  width:18px; height:18px; border-radius:50%; margin-left:2px;
  display:inline-flex; align-items:center; justify-content:center;
  border:1px solid transparent; background:transparent; color:var(--ink-faint);
  font:inherit; font-size:11px; line-height:1; cursor:pointer; opacity:0;
}
.g-sort-door .g-sort-pill:hover .g-sort-x,
.g-sort-door .g-sort-pill:focus-within .g-sort-x,
.g-sort-door .g-sort-x:focus-visible{ opacity:1; }
.g-sort-door .g-sort-x:hover, .g-sort-door .g-sort-x:focus-visible{ border-color:var(--pink-deep); color:var(--pink); }

/* ---------------------------------------------------------------- search -- */
.g-sort-door .g-sort-search{ display:flex; gap:8px; align-items:center; flex-wrap:wrap; }
.g-sort-door .g-sort-input{
  flex:1 1 180px; min-height:var(--sd-hit); padding:6px 10px;
  background:var(--flap-well); border:1px solid var(--line); border-radius:8px;
  color:var(--ink); font:inherit; font-size:13px;
}
.g-sort-door .g-sort-input:focus-visible{ outline:2px solid var(--pink); outline-offset:1px; }
/* THE RESET IS THE POINT. The room's own .g-sort-note is an absolutely
   positioned pill hung at the bottom of the stage, and it is written bare, so
   it reaches in here and turned the probe line into an empty grey pill floating
   over the footer. Scoping this file cannot undo that - only naming the
   properties back can. */
.g-sort-door .g-sort-note{
  position:static; left:auto; right:auto; bottom:auto; transform:none; z-index:auto;
  background:none; border:0; border-radius:0; padding:0;
  margin:6px 0 0; font-family:inherit; font-size:12px; letter-spacing:normal;
  color:var(--ink-faint); min-height:1.2em; pointer-events:auto;
}
.g-sort-door .g-sort-note[data-state="probing"]{ color:var(--lav); }
.g-sort-door .g-sort-note[data-state="ok"]{ color:var(--gold); }
.g-sort-door .g-sort-note[data-state="missing"],
.g-sort-door .g-sort-note[data-state="bad"],
.g-sort-door .g-sort-note[data-state="dupe"]{ color:var(--pink); }

/* ------------------------------------------------------------ local lists -- */
.g-sort-door .g-sort-folders{ display:flex; flex-direction:column; gap:4px; max-height:260px; overflow:auto; }
.g-sort-door .g-sort-folder{
  min-height:var(--sd-hit); padding:5px 10px; border-radius:8px; cursor:pointer;
  display:flex; align-items:center; gap:10px; text-align:left;
  background:transparent; border:1px solid var(--line); color:var(--ink-dim); font:inherit; font-size:13px;
}
.g-sort-door .g-sort-folder[data-on="1"]{ border-color:var(--sd-target); color:var(--ink); background:rgba(255,105,180,.14); }
.g-sort-door .g-sort-panel[data-side="noise"] .g-sort-folder[data-on="1"]{ border-color:var(--lav); background:rgba(184,166,232,.14); }
.g-sort-door .g-sort-folder:disabled{ opacity:.34; cursor:default; }
.g-sort-door .g-sort-folder:focus-visible{ outline:2px solid var(--pink); outline-offset:2px; }
.g-sort-door .g-sort-folder-path{ flex:1 1 auto; font-family:var(--mono); font-size:12px; overflow:hidden; text-overflow:ellipsis; white-space:nowrap; }

/* ---------------------------------------------------------------- strips -- */
.g-sort-door .g-sort-strip{
  width:min(760px,100%); padding:8px 12px; border-radius:10px;
  display:flex; align-items:center; gap:10px; flex-wrap:wrap; font-size:13px;
  border:1px solid var(--gold); color:var(--gold); background:rgba(240,194,75,.10);
}
.g-sort-door .g-sort-strip[data-kind="err"]{ border-color:var(--pink); color:var(--pink); background:rgba(255,105,180,.10); }
.g-sort-door .g-sort-strip[data-kind="spice"]{ border-color:var(--line); color:var(--ink-dim); background:transparent; }
.g-sort-door .g-sort-strip[data-kind="spice"][data-heat="hot"]{ border-color:var(--pink-deep); color:var(--pink); }
.g-sort-door .g-sort-strip-txt{ flex:1 1 auto; }

/* ---------------------------------------------------------- the summary --- */
.g-sort-door .g-sort-same{ width:min(760px,100%); display:flex; flex-direction:column; align-items:center; gap:12px; }
.g-sort-door .g-sort-summary{
  font-family:var(--disp); font-size:clamp(16px,2.6vw,22px); letter-spacing:.04em;
  text-align:center; line-height:1.5;
}
.g-sort-door .g-sort-summary b{ color:var(--pink); }
.g-sort-door .g-sort-summary i{ color:var(--slate); font-style:normal; }
.g-sort-door .g-sort-summary em{ color:var(--ink-faint); font-style:normal; padding:0 8px; font-size:.8em; }

/* ------------------------------------------------------------ the ghost --- */
/* TWO CARDS, SIDE BY SIDE, AND BOTH OF THEM VISIBLE. They used to be absolutely
   positioned with no offset inside a centring flex box, which stacked them
   perfectly: the noise card is appended second, so for the first third of a
   second the rulebook was one card hiding the card that teaches the rule. They
   are in FLOW now - the fly transforms still work, because a transform on a
   relatively positioned box travels exactly the same way. */
.g-sort-door .g-sort-ghost{
  width:min(760px,100%); min-height:280px; position:relative;
  display:flex; align-items:center; justify-content:center; gap:24px; flex-wrap:nowrap;
}
.g-sort-door .g-sort-gcard{
  position:relative; flex:0 0 auto; width:min(46%,220px); aspect-ratio:3/4;
  border-radius:14px; overflow:hidden; background:var(--flap-well);
  border:2px solid var(--line);
  transition:transform 620ms cubic-bezier(.2,.7,.3,1), opacity 620ms ease;
  will-change:transform;
}
.g-sort-door .g-sort-gcard[data-tag="target"]{ border-color:var(--sd-target); }
.g-sort-door .g-sort-gcard[data-tag="noise"]{ border-color:var(--sd-noise); }
.g-sort-door .g-sort-gcard[data-fly="right"]{ transform:translate(150%,-6%) rotate(16deg); opacity:0; }
.g-sort-door .g-sort-gcard[data-fly="left"]{ transform:translate(-150%,-6%) rotate(-16deg); opacity:0; }
.g-sort-door .g-sort-gmedia{ position:absolute; inset:0; width:100%; height:100%; object-fit:cover; }
.g-sort-door .g-sort-gstamp{
  position:absolute; top:14px; padding:4px 12px; border-radius:6px;
  font-family:var(--disp); font-size:20px; letter-spacing:.14em;
  border:3px solid currentColor; opacity:0; transform:rotate(-11deg) scale(1.3);
  transition:opacity 180ms ease, transform 180ms cubic-bezier(.2,1.5,.4,1);
}
.g-sort-door .g-sort-gcard[data-tag="target"] .g-sort-gstamp{ left:12px; color:var(--pink); }
.g-sort-door .g-sort-gcard[data-tag="noise"] .g-sort-gstamp{ right:12px; color:var(--slate); }
.g-sort-door .g-sort-gcard[data-stamp="1"] .g-sort-gstamp{ opacity:1; transform:rotate(-11deg) scale(1); }
.g-sort-door .g-sort-gtag{
  position:absolute; left:0; right:0; bottom:0; padding:5px 8px; text-align:center;
  background:linear-gradient(180deg,rgba(20,20,43,0),rgba(20,20,43,.86) 60%);
  color:var(--ink-dim); font-size:12px; letter-spacing:.08em; text-transform:uppercase;
}
/* A narrow door cannot hold two cards beside each other and still show a face,
   so they stack and the rulebook reads top to bottom. */
@media (max-width:520px){
  .g-sort-door .g-sort-ghost{ flex-direction:column; gap:16px; }
  .g-sort-door .g-sort-gcard{ width:min(70%,200px); }
}

/* ------------------------------------------------------------- the busy --- */
.g-sort-door .g-sort-busy{
  position:absolute; inset:0; z-index:5;
  display:flex; align-items:center; justify-content:center; gap:14px; flex-direction:column;
  background:rgba(20,20,43,.82); color:var(--ink);
  font-family:var(--disp); font-size:18px; letter-spacing:.08em;
}
.g-sort-door .g-sort-spin{
  width:38px; height:38px; border-radius:50%;
  border:3px solid var(--line); border-top-color:var(--pink);
  animation:g-sort-spin 900ms linear infinite;
}
@keyframes g-sort-spin{ to{ transform:rotate(360deg); } }

/* ------------------------------------------------------------- the foot --- */
/* The Leave button used to sit at x=0, welded to the edge of the screen. The
   foot wears the body's own gutter so the row reads as part of the column. */
.g-sort-door .g-sort-foot{
  display:flex; gap:10px; align-items:center; justify-content:flex-end; flex-wrap:wrap;
  padding:10px 22px 18px;
}
.g-sort-door .g-sort-foot .g-sort-spacer{ flex:1 1 auto; }

/* --------------------------------------------------------- reduced motion -- */
@media (prefers-reduced-motion: reduce){
  .g-sort-door .g-sort-gcard, .g-sort-door .g-sort-gstamp{ transition-duration:1ms; }
  .g-sort-door .g-sort-spin{ animation:none; border-top-color:var(--pink); }
}
.g-sort-door[data-reduced="1"] .g-sort-gcard,
.g-sort-door[data-reduced="1"] .g-sort-gstamp{ transition-duration:1ms; }
.g-sort-door[data-reduced="1"] .g-sort-spin{ animation:none; }
`;

export default SETUP_CSS;
