/* ============================================================================
 * games/impulse-control/style.js - the class injects its OWN stylesheet.
 *
 * styles.css is shell chrome ONLY (see its header), so everything this class
 * paints ships here and is namespaced `.g-ic-*` (game: impulse control). Injected
 * exactly once per document, lazily, on the first create().
 *
 * Colours read the shell's palette tokens with the mockup values as fallbacks,
 * so a mod palette (init.palette -> :root custom properties) re-skins the
 * assessment for free and nothing here is a literal hex that can drift.
 *
 * MOTION: every animation is also neutralised in CSS under
 * `prefers-reduced-motion`, not just in JS - a stray node must never strobe even
 * if a code path forgets to check. The aperture and the stimulus glyph never
 * move under reduced motion; only opacity does.
 * ==========================================================================*/

const STYLE_ID = 'g-ic-style';

export const STYLE_TEXT = `
.g-ic{--ic-pink:var(--pink,#FF69B4);--ic-lav:var(--lav,#B8A6E8);--ic-gold:var(--gold,#F0C24B);
  --ic-ink:var(--ink,#F2EBDD);--ic-dim:var(--ink-dim,#B9B3CE);--ic-faint:var(--ink-faint,#8A84A8);
  --ic-panel:var(--panel,#252542);--ic-line:var(--line,#3A3A5E);--ic-ground:var(--ground,#14142B);
  display:flex;flex-direction:column;min-height:0}

/* ---- chrome strip (the clinical letterhead) ------------------------------ */
.g-ic-chrome{display:flex;flex-wrap:wrap;gap:8px 14px;align-items:center;padding:9px 14px;
  background:var(--ic-panel);border-bottom:1px solid var(--ic-line);border-radius:10px 10px 0 0;
  font-family:var(--mono,monospace);font-size:10.5px;letter-spacing:.08em;color:var(--ic-faint)}
.g-ic-chrome b{color:var(--ic-dim);font-weight:600}
.g-ic-chrome .g-ic-warn{color:var(--ic-gold);opacity:0;transition:opacity .25s ease}
.g-ic-chrome .g-ic-warn.on{opacity:1}

/* ---- arena + aperture --------------------------------------------------- */
.g-ic-arena{position:relative;min-height:250px;display:flex;align-items:center;justify-content:center;
  background:radial-gradient(circle at 50% 46%,#1E1E3C,var(--ic-ground) 70%);
  cursor:pointer;touch-action:manipulation;-webkit-tap-highlight-color:transparent}
.g-ic-aperture{position:relative;width:170px;height:170px;border-radius:50%;
  border:1.5px dashed #4A4A80;display:flex;align-items:center;justify-content:center;
  transition:border-color .18s ease,box-shadow .18s ease,transform .3s ease}
.g-ic-aperture.tight{border-color:var(--ic-pink);box-shadow:0 0 0 1px rgba(255,105,180,.25) inset}
.g-ic-aperture.hot{box-shadow:0 0 26px rgba(255,105,180,.35)}
.g-ic-stim{font-family:var(--disp,serif);font-size:36px;letter-spacing:.16em;padding-left:.16em;
  color:var(--ic-ink);text-shadow:0 0 22px rgba(255,105,180,.55);opacity:0;
  transition:opacity .09s linear;text-align:center;line-height:1.05}
.g-ic-stim.on{opacity:1}
.g-ic-stim.nogo{color:var(--ic-lav);text-shadow:0 0 18px rgba(184,166,232,.45)}
.g-ic-stim img,.g-ic-stim video{max-width:132px;max-height:132px;border-radius:10px;display:block}
/* NO-GO media twins are CSS-filter transforms of the GO asset - never a canvas
   read, so a CORS-tainted remote loop is legal here (synthesis closure). The
   provider serves mp4 on iOS, so every twin has to hold for <video> too. */
.g-ic-stim .twin-mirror{transform:scaleX(-1)}
.g-ic-stim .twin-hue{filter:hue-rotate(150deg) saturate(1.15)}
.g-ic-stim .twin-flip{transform:rotate(180deg)}
.g-ic-stim .twin-dim{filter:brightness(.72) contrast(1.2)}

.g-ic-ring{position:absolute;inset:0;border:2px solid var(--ic-pink);border-radius:50%;
  pointer-events:none;opacity:0}
.g-ic-ring.go{animation:g-ic-ringout .62s ease-out 1}
@keyframes g-ic-ringout{from{transform:scale(.72);opacity:.9}to{transform:scale(1.28);opacity:0}}
.g-ic-ash{position:absolute;inset:0;border-radius:50%;pointer-events:none;opacity:0;
  background:radial-gradient(circle at 50% 50%,rgba(184,166,232,.35),transparent 65%)}
.g-ic-ash.on{animation:g-ic-sip .7s ease-out 1}
@keyframes g-ic-sip{from{opacity:.8;transform:scale(1)}to{opacity:0;transform:scale(.82)}}

.g-ic-rt{position:absolute;right:16%;top:30%;font-family:var(--mono,monospace);font-weight:600;
  font-size:19px;color:var(--ic-gold);text-shadow:0 0 12px rgba(240,194,75,.5);opacity:0}
.g-ic-rt.on{animation:g-ic-rtfloat .95s ease-out 1}
.g-ic-rt.slow{color:var(--ic-dim);text-shadow:none}
.g-ic-rt.best{color:#fff;text-shadow:0 0 16px rgba(255,105,180,.9)}
@keyframes g-ic-rtfloat{from{transform:translateY(6px);opacity:0}25%{opacity:1}to{transform:translateY(-16px);opacity:0}}

.g-ic-edge{position:absolute;inset:0;pointer-events:none;border-radius:12px;opacity:0;
  box-shadow:inset 0 0 0 2px var(--ic-gold)}
.g-ic-edge.on{animation:g-ic-edge .45s ease-out 1}
@keyframes g-ic-edge{from{opacity:.75}to{opacity:0}}

/* ---- the attribution toast (the game's soul, mid-class) ------------------ */
.g-ic-toast{margin:10px 14px 0;background:var(--ic-panel);border:1px solid var(--pink-deep,#D4488F);
  border-left:4px solid var(--ic-pink);border-radius:10px;padding:10px 13px;font-size:12.5px;
  color:var(--ic-dim);position:relative;z-index:4;opacity:0;transition:opacity .22s ease}
.g-ic-toast.on{opacity:1}
.g-ic-toast b{color:var(--ic-pink)}
.g-ic-toast.clean{border-left-color:var(--ic-lav)}
.g-ic-toast.clean b{color:var(--ic-lav)}

/* ---- interference log + footline ---------------------------------------- */
.g-ic-lielog{display:flex;align-items:center;gap:2px;height:14px;margin:12px 14px 0;position:relative;
  border-left:1px solid var(--ic-line);border-right:1px solid var(--ic-line);background:var(--ic-panel)}
.g-ic-lielog i{position:absolute;top:2px;width:3px;height:10px;border-radius:1px;background:var(--ic-lav)}
.g-ic-lielog i.err{background:var(--ic-pink);box-shadow:0 0 6px rgba(255,105,180,.8)}
.g-ic-lielog i.clean-err{background:var(--ic-faint)}
.g-ic-base{display:flex;flex-wrap:wrap;gap:8px 18px;padding:10px 14px 4px;
  font-family:var(--mono,monospace);font-size:11px;color:var(--ic-faint)}
.g-ic-base b{color:var(--ic-dim)}
.g-ic-meter{padding:0 14px}

/* ---- block break card --------------------------------------------------- */
.g-ic-break{position:absolute;inset:0;display:flex;flex-direction:column;align-items:center;
  justify-content:center;gap:6px;background:rgba(20,20,43,.86);border-radius:10px;text-align:center;padding:16px}
.g-ic-break h3{margin:0;font-family:var(--disp,serif);font-size:19px;letter-spacing:.1em;color:var(--ic-ink)}
.g-ic-break p{margin:0;font-size:12.5px;color:var(--ic-dim)}
.g-ic-break .g-ic-stampline{font-family:var(--disp,serif);letter-spacing:.14em;color:var(--ic-gold)}

/* ---- debrief ------------------------------------------------------------ */
.g-ic-debrief{padding:14px}
.g-ic-debrief h3{margin:0 0 2px;font-family:var(--disp,serif);font-size:20px;letter-spacing:.08em}
.g-ic-sub{font-family:var(--mono,monospace);font-size:11px;color:var(--ic-faint);letter-spacing:.08em;margin:0 0 12px}
.g-ic-grid{display:flex;flex-wrap:wrap;gap:8px;margin:0 0 12px}
.g-ic-cell{flex:1 1 120px;background:var(--ic-panel);border:1px solid var(--ic-line);border-radius:10px;padding:9px 11px}
.g-ic-cell span{display:block;font-size:10px;letter-spacing:.1em;color:var(--ic-faint);text-transform:uppercase}
.g-ic-cell b{display:block;font-family:var(--mono,monospace);font-size:17px;color:var(--ic-ink)}
.g-ic-cell b.gold{color:var(--ic-gold)}
.g-ic-cell b.pink{color:var(--ic-pink)}
.g-ic-timeline{position:relative;height:34px;margin:0 0 6px;border:1px solid var(--ic-line);
  border-radius:8px;background:linear-gradient(180deg,#1C1C38,var(--ic-panel))}
.g-ic-timeline i{position:absolute;top:5px;width:3px;height:10px;border-radius:1px;background:var(--ic-lav)}
.g-ic-timeline i.err{top:18px;height:11px;background:var(--ic-pink);box-shadow:0 0 6px rgba(255,105,180,.8)}
.g-ic-timeline i.clean-err{top:18px;height:11px;background:var(--ic-faint)}
.g-ic-legend{font-family:var(--mono,monospace);font-size:10px;color:var(--ic-faint);letter-spacing:.06em;margin:0 0 12px}
.g-ic-lines{list-style:none;margin:0 0 14px;padding:0;display:flex;flex-direction:column;gap:6px}
.g-ic-lines li{background:var(--ic-panel);border-left:3px solid var(--ic-lav);border-radius:8px;
  padding:7px 10px;font-size:12.5px;color:var(--ic-dim)}
.g-ic-lines li.induced{border-left-color:var(--ic-pink)}
.g-ic-lines li b{color:var(--ic-ink)}
.g-ic-lines li em{color:var(--ic-pink);font-style:normal}
.g-ic-slip{font-size:13px;color:var(--ic-ink);margin:0 0 14px}
.g-ic-actions{display:flex;flex-wrap:wrap;gap:8px;align-items:center}
.g-ic-hint{font-family:var(--mono,monospace);font-size:10.5px;color:var(--ic-faint);margin:8px 0 0}

/* ---- reduced motion: opacity only, no transforms, no strobe -------------- */
@media (prefers-reduced-motion: reduce){
  .g-ic-ring.go,.g-ic-ash.on,.g-ic-rt.on,.g-ic-edge.on{animation:none!important}
  .g-ic-rt{transition:opacity .2s ease}
  .g-ic-aperture{transition:none}
  .g-ic-stim{transition:opacity .16s linear}
}
`;

/** Inject once per document. Safe to call from a headless import (no DOM = no-op). */
export function injectStyle() {
  if (typeof document === 'undefined' || !document || !document.createElement) return false;
  try {
    if (document.getElementById && document.getElementById(STYLE_ID)) return false;
    const tag = document.createElement('style');
    tag.id = STYLE_ID;
    tag.textContent = STYLE_TEXT;
    const head = document.head || document.body || document.documentElement;
    if (!head || !head.appendChild) return false;
    head.appendChild(tag);
    if (document._register) document._register(STYLE_ID, tag);   // test-double hook
    return true;
  } catch (e) {
    return false;   // a missing stylesheet must never stop the class
  }
}

export default injectStyle;
