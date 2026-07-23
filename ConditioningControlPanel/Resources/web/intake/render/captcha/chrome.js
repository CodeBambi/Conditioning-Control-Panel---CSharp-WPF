/* ============================================================================
 * render/captcha/chrome.js — SHARED CAPTCHA CHROME KIT for "Graded Intake"
 *
 * The pitch-perfect fake-verification widget every captcha item renderer builds
 * on. Deadpan corporate captcha chrome — a clean white card on the dark stage,
 * a generic verification vendor ("VeriTru · human verification"), a reCAPTCHA-
 * style tile grid, "Verifying…" spinners and rubber-stamp verdicts. The POINT
 * is that at Calibration it reads 100% authentic; only the CONTENT rots later
 * (band by band) while this chrome stays constant. See CAPTCHA_HANDOFF.md §1
 * and CAPTCHA_BRAINSTORM.md "DESIGN PRINCIPLES".
 *
 * Exports (all plain functions + a default aggregate `chrome`):
 *   ensureCss(doc)             inject the ONE IXCAP_CSS literal once (id 'ix-captcha-css')
 *   frame(opts)                build the captcha card -> {root, header, body, footer, verifyBtn}
 *   spinner(label)             "Verifying…" ring -> {el, setLabel(t), resolve(ok)}
 *   stamp(text, tone)          rubber-stamp verdict el ('ok'|'logged'|'flag'), scale-in pop
 *   gridShell(n, cols)         reCAPTCHA tile grid -> {grid, tiles[]} (tile.select/.isSelected/.setImage)
 *   placeholderTile(kind,seed) canvas-generated drab mundane tile, data-URI <img>, deterministic
 *   mundaneTileSrc(kind, i)    real asset if the manifest has it, else placeholderTile()
 *
 * HARD RULES honored (see CLAUDE.md §10 + CAPTCHA_HANDOFF.md §4):
 *   - NOTHING touches document/window at IMPORT time — every DOM/canvas/fetch
 *     touch is inside a function, guarded on `typeof document/window`. DOM-less
 *     node import must succeed (the standing import-sweep gate).
 *   - ALL captcha chrome CSS lives in the injected IXCAP_CSS literal (prefixed
 *     'ixcap-'), NEVER in styles.css (grep already misses the sibling literals).
 *   - NO audio handle is held here: SFX fires through the 'intake-sfx' window
 *     CustomEvent seam, exactly like effects.js / steering.js. NO import of
 *     audio.js / effects.js.
 *   - `is-correct` / `is-answer` are DOM hooks elsewhere and never appear here;
 *     tile selection uses its OWN chrome class (`ixcap-sel`), never those hooks.
 * ==========================================================================*/

import { hash01, clamp01 } from '../../core/contracts.js';

/* ----------------------------------------------------------------------------
 * BRAND (deadpan generic verification vendor). One place so a rename is trivial.
 * -------------------------------------------------------------------------- */
export const VENDOR_NAME = 'VeriTru';
export const VENDOR_TAGLINE = 'human verification';

/* Asset base for authored mundane tiles (glob-included under the web tree). The
 * page lives at .../intake/index.html so this resolves to .../intake/assets/captcha/. */
const ASSET_BASE = 'assets/captcha/';
const MANIFEST_URL = ASSET_BASE + 'manifest.json';

/* ----------------------------------------------------------------------------
 * SEAMS — no coupling. SFX rides the 'intake-sfx' window CustomEvent (audio.js
 * listens + routes to audio.sfx); logs ride 'intake-log'. Both guarded + silent.
 * -------------------------------------------------------------------------- */
function sfxCue(id, intensity) {
  try {
    if (typeof window !== 'undefined' && typeof CustomEvent === 'function') {
      window.dispatchEvent(new CustomEvent('intake-sfx', {
        detail: { id, intensity: (intensity == null ? 1 : clamp01(intensity)) },
      }));
    }
  } catch (_e) {}
}
function ilog(msg) {
  try {
    if (typeof window !== 'undefined' && typeof CustomEvent === 'function') {
      window.dispatchEvent(new CustomEvent('intake-log', { detail: { msg: 'ixcap: ' + msg } }));
    }
  } catch (_e) {}
}

/* ----------------------------------------------------------------------------
 * DOM helpers (render-time only; never called at import).
 * -------------------------------------------------------------------------- */
function hasDoc() { return typeof document !== 'undefined' && !!document.createElement; }
function el(tag, cls, text) {
  const e = document.createElement(tag);
  if (cls) e.className = cls;
  if (text != null) e.textContent = text;
  return e;
}

/* ----------------------------------------------------------------------------
 * CSS — the ONE injected literal (id 'ix-captcha-css'). Follows the IB_CSS
 * precedent: injected once, keyed by id, all classes prefixed 'ixcap-'.
 * -------------------------------------------------------------------------- */
const IXCAP_STYLE_ID = 'ix-captcha-css';
export function ensureCss(doc) {
  const d = doc || (hasDoc() ? document : null);
  if (!d || !d.getElementById || d.getElementById(IXCAP_STYLE_ID)) return;
  const s = d.createElement('style');
  s.id = IXCAP_STYLE_ID;
  s.textContent = IXCAP_CSS;
  (d.head || d.documentElement).appendChild(s);
}

/* ----------------------------------------------------------------------------
 * frame(opts) — the captcha card scaffold.
 *   opts: { instruction, band, sub, noir, hatch, verifyLabel }
 *     instruction  header line ("Select all images with fire hydrants")
 *     band         Band.* -> data-band accent-creep hook (Calibration = pristine)
 *     sub          optional secondary header line (small, grey)
 *     noir         truthy -> dark-noir card variant hook (data-noir)
 *     hatch        truthy or string -> render the in-fiction skip link slot
 *     verifyLabel  VERIFY button text (default 'VERIFY')
 *   returns { root, header, body, footer, verifyBtn, hatchLink }
 *     - body is EMPTY (item renderers fill it)
 *     - footer holds the brand roundel + right-aligned VERIFY (+ optional hatch)
 *     - renderers wire verifyBtn / hatchLink themselves
 * -------------------------------------------------------------------------- */
export function frame(opts) {
  opts = opts || {};
  if (!hasDoc()) return null;
  ensureCss();

  const root = el('div', 'ixcap-card');
  if (opts.band) root.setAttribute('data-band', String(opts.band));
  if (opts.noir) root.setAttribute('data-noir', '1');

  // header — the instruction typography ("Select all images with…")
  const header = el('div', 'ixcap-header');
  const instr = el('div', 'ixcap-instr', opts.instruction != null ? opts.instruction : '');
  header.appendChild(instr);
  if (opts.sub) header.appendChild(el('div', 'ixcap-subinstr', opts.sub));
  root.appendChild(header);

  // body — item renderers fill this
  const body = el('div', 'ixcap-body');
  root.appendChild(body);

  // footer — brand roundel (left) + hatch slot + VERIFY (right)
  const footer = el('div', 'ixcap-footer');
  footer.appendChild(roundel());

  const actions = el('div', 'ixcap-actions');
  let hatchLink = null;
  if (opts.hatch) {
    const label = typeof opts.hatch === 'string' ? opts.hatch : 'skip verification (flagged)';
    hatchLink = el('button', 'ixcap-hatch', label);
    hatchLink.type = 'button';
    actions.appendChild(hatchLink);
  }
  const verifyBtn = el('button', 'ixcap-verify', opts.verifyLabel != null ? opts.verifyLabel : 'VERIFY');
  verifyBtn.type = 'button';
  actions.appendChild(verifyBtn);
  footer.appendChild(actions);
  root.appendChild(footer);

  // microtext strip — the boring "Privacy · Terms" corporate footer that sells it
  const micro = el('div', 'ixcap-microfoot');
  micro.appendChild(el('span', 'ixcap-micro', 'Privacy'));
  micro.appendChild(el('span', 'ixcap-micro-dot', '·'));
  micro.appendChild(el('span', 'ixcap-micro', 'Terms'));
  root.appendChild(micro);

  return { root, header, body, footer, verifyBtn, hatchLink };
}

/** The fake brand roundel: a small badge + "VeriTru / human verification". */
export function roundel() {
  if (!hasDoc()) return null;
  const wrap = el('div', 'ixcap-brand');
  wrap.appendChild(el('div', 'ixcap-roundel'));   // CSS-drawn badge mark
  const txt = el('div', 'ixcap-brandtext');
  txt.appendChild(el('div', 'ixcap-brandname', VENDOR_NAME));
  txt.appendChild(el('div', 'ixcap-tagline', VENDOR_TAGLINE));
  wrap.appendChild(txt);
  return wrap;
}

/* ----------------------------------------------------------------------------
 * spinner(label) — the "Verifying…" interstitial ring.
 *   returns { el, setLabel(text), resolve(ok) }
 *     setLabel  swap the cycling label ("verifying…" -> "verifying you…")
 *     resolve   stop spinning; ok=true -> green check, ok=false -> grey dash
 * -------------------------------------------------------------------------- */
export function spinner(label) {
  if (!hasDoc()) return { el: null, setLabel() {}, resolve() {} };
  const root = el('div', 'ixcap-spinner');
  const ring = el('div', 'ixcap-ring');
  root.appendChild(ring);
  const lab = el('div', 'ixcap-spinlabel', label != null ? label : 'Verifying…');
  root.appendChild(lab);
  let done = false;
  return {
    el: root,
    setLabel(text) { if (!done) { try { lab.textContent = String(text == null ? '' : text); } catch (_e) {} } },
    resolve(ok) {
      if (done) return;
      done = true;
      try {
        ring.classList.add('ixcap-ring-done');
        root.classList.add(ok ? 'ixcap-spin-ok' : 'ixcap-spin-dash');
        // glyph swap: green check vs grey dash
        ring.textContent = ok ? '✓' : '—';
        sfxCue(ok ? 'chime' : 'incorrect-feedback', 0.5);
      } catch (_e) {}
    },
  };
}

/* ----------------------------------------------------------------------------
 * stamp(text, tone) — a rubber-stamp verdict element with a brief scale-in pop.
 *   tone: 'ok' (green) | 'logged' (grey, default) | 'flag' (accent)
 *   used for "anomalies logged: 2", "LOGGED", "VERIFICATION WAIVED", etc.
 * -------------------------------------------------------------------------- */
export function stamp(text, tone) {
  if (!hasDoc()) return null;
  const t = (tone === 'ok' || tone === 'flag') ? tone : 'logged';
  const e = el('div', 'ixcap-stamp ixcap-stamp-' + t, text != null ? text : '');
  sfxCue('stamp', t === 'flag' ? 0.7 : 0.45);
  return e;
}

/* ----------------------------------------------------------------------------
 * gridShell(n, cols) — the reCAPTCHA-style tile grid.
 *   n     tile count (default 9)
 *   cols  columns (default 3 -> 3x3)
 *   returns { grid, tiles[] } where each tile is an overflow:hidden wrapper with:
 *     tile.el            the wrapper element
 *     tile.setImage(src) mount a whole <img> (CSS-crop pattern: whole image inside
 *                        overflow:hidden, offset via object-fit/position — NEVER a
 *                        canvas drawImage of a gif unless a freeze is intended)
 *     tile.setPlaceholder(kind, seed)  fill with a generated drab mundane tile
 *     tile.select(on)    toggle the reCAPTCHA corner-tick selection overlay
 *     tile.isSelected()  -> boolean
 *     tile.img           the current <img> (or null)
 *   Selection styling lives on `ixcap-sel` (NOT is-correct/is-answer). No click
 *   wiring here — item renderers own selection semantics + grading.
 * -------------------------------------------------------------------------- */
export function gridShell(n, cols) {
  if (!hasDoc()) return { grid: null, tiles: [] };
  const count = (typeof n === 'number' && n > 0) ? (n | 0) : 9;
  const columns = (typeof cols === 'number' && cols > 0) ? (cols | 0) : 3;
  const grid = el('div', 'ixcap-grid');
  grid.style.setProperty('--ixcap-cols', String(columns));
  const tiles = [];
  for (let i = 0; i < count; i++) {
    const wrap = el('div', 'ixcap-tile');
    const tick = el('div', 'ixcap-tick', '✓');   // corner tick, hidden until selected
    wrap.appendChild(tick);
    let selected = false;
    let img = null;
    const tile = {
      index: i,
      el: wrap,
      get img() { return img; },
      setImage(src) {
        if (src == null) return;
        if (!img) { img = el('img', 'ixcap-tileimg'); img.alt = ''; img.draggable = false; wrap.insertBefore(img, tick); }
        try { img.src = String(src); } catch (_e) {}
        return img;
      },
      setPlaceholder(kind, seed) {
        const src = placeholderTile(kind, seed == null ? i : seed);
        if (src) this.setImage(src);
        return img;
      },
      select(on) {
        selected = !!on;
        wrap.classList.toggle('ixcap-sel', selected);
        return selected;
      },
      isSelected() { return selected; },
    };
    tiles.push(tile);
    grid.appendChild(wrap);
  }
  return { grid, tiles };
}

/* ----------------------------------------------------------------------------
 * placeholderTile(kind, seed) — a canvas-GENERATED mundane tile as a data-URI
 * img src. Deliberately reCAPTCHA-drab (beige/overexposed, JPEG-mushy, blocky
 * low-contrast silhouette + heavy noise + a faint corner label). These stand in
 * until real nano-banana art lands in assets/captcha/. Deterministic per
 * (kind, seed) — the same pair always draws the same tile. Must look boring, not
 * broken. Guarded: returns '' with no canvas support (renderer keeps a blank tile).
 * -------------------------------------------------------------------------- */
const TILE_KINDS = ['hydrant', 'bus', 'crosswalk', 'stapler'];
export function placeholderTile(kind, seed) {
  if (!hasDoc()) return '';
  const k = TILE_KINDS.indexOf(kind) >= 0 ? kind : TILE_KINDS[Math.abs(hash01(String(kind || '')) * 4 | 0) % 4];
  const rng = mulberry32(hashSeed(k + '|' + String(seed)));
  let cv;
  try {
    cv = document.createElement('canvas');
    cv.width = 120; cv.height = 120;
    const ctx = cv.getContext ? cv.getContext('2d') : null;
    if (!ctx) return '';
    // beige, slightly overexposed ground
    const baseL = 74 + rng() * 12;                 // light
    ctx.fillStyle = `hsl(${38 + rng() * 10}, ${16 + rng() * 12}%, ${baseL}%)`;
    ctx.fillRect(0, 0, 120, 120);
    // a soft washed vignette (overexposed top-left)
    const g = ctx.createLinearGradient(0, 0, 120, 120);
    g.addColorStop(0, 'rgba(255,255,255,0.28)');
    g.addColorStop(1, 'rgba(120,110,90,0.10)');
    ctx.fillStyle = g;
    ctx.fillRect(0, 0, 120, 120);
    // low-contrast blocky silhouette suggesting the kind
    drawDrabSilhouette(ctx, k, rng);
    // JPEG-mushy feel: a couple of translucent block passes
    for (let p = 0; p < 3; p++) {
      ctx.fillStyle = `rgba(${120 + rng() * 60 | 0},${110 + rng() * 50 | 0},${90 + rng() * 50 | 0},0.05)`;
      const bx = rng() * 96, by = rng() * 96, bw = 16 + rng() * 24, bh = 16 + rng() * 24;
      ctx.fillRect(bx | 0, by | 0, bw | 0, bh | 0);
    }
    // heavy speckle noise
    const dots = 260 + (rng() * 120 | 0);
    for (let i = 0; i < dots; i++) {
      const v = 40 + rng() * 180 | 0;
      ctx.fillStyle = `rgba(${v},${v},${v - 10},${0.05 + rng() * 0.09})`;
      ctx.fillRect(rng() * 120 | 0, rng() * 120 | 0, 1 + (rng() * 2 | 0), 1 + (rng() * 2 | 0));
    }
    // faint corner label (the reCAPTCHA "attribution" microtext feel)
    ctx.fillStyle = 'rgba(70,64,54,0.32)';
    ctx.font = '9px sans-serif';
    ctx.fillText(k, 5, 114);
    // JPEG artifacting: export as low-quality jpeg for the mushy look
    return cv.toDataURL('image/jpeg', 0.4);
  } catch (e) {
    ilog('placeholderTile failed: ' + (e && e.message));
    return '';
  }
}

/** Draw a boring low-contrast silhouette hinting at the kind. Amplitude only. */
function drawDrabSilhouette(ctx, kind, rng) {
  ctx.save();
  ctx.translate(60, 62);
  ctx.fillStyle = `rgba(${96 + rng() * 30 | 0},${92 + rng() * 26 | 0},${80 + rng() * 24 | 0},0.42)`;
  ctx.strokeStyle = 'rgba(60,56,48,0.30)';
  ctx.lineWidth = 2;
  const jitter = () => (rng() - 0.5) * 6;
  switch (kind) {
    case 'bus': {
      ctx.fillRect(-34 + jitter(), -18, 68, 36);
      ctx.fillStyle = 'rgba(70,66,58,0.20)';
      for (let i = -1; i <= 1; i++) ctx.fillRect(i * 20 - 8, -12, 12, 10);   // windows
      ctx.beginPath(); ctx.arc(-18, 20, 6, 0, Math.PI * 2); ctx.arc(18, 20, 6, 0, Math.PI * 2); ctx.fill();
      break;
    }
    case 'crosswalk': {
      ctx.fillStyle = 'rgba(90,86,76,0.30)';
      for (let i = -3; i <= 3; i++) ctx.fillRect(i * 9 - 3, -30, 6, 60);      // stripes
      break;
    }
    case 'stapler': {
      ctx.beginPath();
      ctx.moveTo(-30, 8); ctx.lineTo(28, -6); ctx.lineTo(32, 4); ctx.lineTo(-26, 18); ctx.closePath();
      ctx.fill();
      break;
    }
    case 'hydrant':
    default: {
      ctx.fillRect(-11, -26, 22, 46);                                        // body
      ctx.beginPath(); ctx.arc(0, -28, 10, 0, Math.PI * 2); ctx.fill();       // cap
      ctx.fillRect(-20, -8, 40, 8);                                          // arms
      ctx.fillRect(-16, 20, 32, 8);                                         // base
      break;
    }
  }
  ctx.restore();
}

/* ----------------------------------------------------------------------------
 * mundaneTileSrc(kind, i) — real authored asset if the manifest lists it, else a
 * generated placeholderTile. The manifest probe is fire-and-forget + cached; the
 * file's absence is normal and SILENT (real art lands later). Because the probe
 * is async, early calls fall back to the placeholder — which is the intended,
 * safe default until assets/captcha/ is populated.
 * -------------------------------------------------------------------------- */
let _manifest = null;          // resolved manifest (Set of filenames) or false (absent)
let _manifestPromise = null;
function probeManifest() {
  if (_manifestPromise) return _manifestPromise;
  if (typeof fetch !== 'function') { _manifest = false; _manifestPromise = Promise.resolve(false); return _manifestPromise; }
  _manifestPromise = fetch(MANIFEST_URL, { cache: 'force-cache' })
    .then((r) => (r && r.ok ? r.json() : null))
    .then((j) => { _manifest = normalizeManifest(j); return _manifest; })
    .catch(() => { _manifest = false; return false; });   // absence is normal + silent
  return _manifestPromise;
}
/** Accept an array of filenames, {files:[...]}, or {kind:count} — return a Set of filenames. */
function normalizeManifest(j) {
  try {
    if (!j) return false;
    if (Array.isArray(j)) return new Set(j.map(String));
    if (Array.isArray(j.files)) return new Set(j.files.map(String));
    const set = new Set();
    for (const kind of Object.keys(j)) {
      const cnt = j[kind];
      if (typeof cnt === 'number') for (let i = 1; i <= cnt; i++) set.add('mundane_' + kind + '_' + i + '.png');
    }
    return set.size ? set : false;
  } catch (_e) { return false; }
}
export function mundaneTileSrc(kind, i) {
  const idx = (typeof i === 'number' && i >= 0) ? (i | 0) : 1;
  const file = 'mundane_' + String(kind) + '_' + idx + '.png';
  probeManifest();                                        // fire-and-forget (cached)
  if (_manifest && _manifest.has && _manifest.has(file)) return ASSET_BASE + file;
  return placeholderTile(kind, idx);                      // generated fallback
}

/* ----------------------------------------------------------------------------
 * THE ONE INJECTED CSS LITERAL. All classes prefixed 'ixcap-'. Style: clean,
 * corporate, slightly boring — reads as authentic reCAPTCHA/Turnstile chrome.
 * The card is white/very-light on the dark stage; [data-band] lets deeper bands
 * tint faintly toward var(--intake-accent) (Calibration = zero accent, pristine);
 * [data-noir] is a dark variant hook. Calibration MUST stay straight.
 * -------------------------------------------------------------------------- */
const IXCAP_CSS = `
.ixcap-card {
  position: relative; z-index: 2;
  box-sizing: border-box;
  width: min(400px, 92vw);
  background: #fff;
  color: #202124;
  border: 1px solid #d3d6da;
  border-radius: 6px;
  box-shadow: 0 2px 10px rgba(0,0,0,.35), 0 12px 40px rgba(0,0,0,.28);
  font-family: 'Roboto', 'Segoe UI', Arial, sans-serif;
  overflow: hidden;
}
/* HEADER — the reCAPTCHA blue instruction band ("Select all images with…"). */
.ixcap-header {
  background: #4a90d9; color: #fff;
  padding: 14px 16px 16px;
}
.ixcap-instr { font-size: 17px; font-weight: 400; line-height: 1.25; }
.ixcap-subinstr { font-size: 12px; opacity: .82; margin-top: 4px; }

/* BODY — item renderers fill it (grid / checkbox / field / docket). */
.ixcap-body { padding: 12px 14px; min-height: 24px; }

/* FOOTER — brand roundel (left) + actions (right). */
.ixcap-footer {
  display: flex; align-items: center; justify-content: space-between;
  gap: 12px; padding: 8px 14px 10px;
  border-top: 1px solid #eceef0;
}
.ixcap-brand { display: flex; align-items: center; gap: 8px; }
.ixcap-roundel {
  width: 30px; height: 30px; border-radius: 50%;
  background:
    radial-gradient(circle at 50% 42%, #fff 0 5px, transparent 6px),
    conic-gradient(from -40deg, #5a8fd0, #4a90d9 40%, #3f78bf 70%, #5a8fd0);
  box-shadow: inset 0 0 0 2px rgba(255,255,255,.6);
  flex: 0 0 auto;
}
.ixcap-brandtext { line-height: 1.05; }
.ixcap-brandname { font-size: 12px; font-weight: 600; color: #5f6368; letter-spacing: .2px; }
.ixcap-tagline { font-size: 9px; color: #9aa0a6; text-transform: lowercase; letter-spacing: .3px; }

.ixcap-actions { display: flex; align-items: center; gap: 10px; }
.ixcap-verify {
  appearance: none; cursor: pointer; font: inherit;
  font-size: 13px; font-weight: 600; letter-spacing: .4px;
  color: #fff; background: #1a73e8; border: 0;
  border-radius: 4px; padding: 9px 18px;
  transition: background .12s ease, box-shadow .12s ease;
}
.ixcap-verify:hover { background: #1666c8; box-shadow: 0 1px 4px rgba(26,115,232,.4); }
.ixcap-verify:active { background: #135ab0; }
.ixcap-verify:disabled { background: #b8c4d4; cursor: default; box-shadow: none; }
/* in-fiction escape hatch ("skip verification (flagged)") — deliberately meek. */
.ixcap-hatch {
  appearance: none; cursor: pointer; font: inherit;
  background: none; border: 0; padding: 4px 2px;
  font-size: 11px; color: #9aa0a6; text-decoration: underline;
}
.ixcap-hatch:hover { color: #d93025; }

/* MICROFOOT — the boring "Privacy · Terms" strip that sells authenticity. */
.ixcap-microfoot {
  display: flex; align-items: center; gap: 6px;
  padding: 6px 14px 10px; font-size: 10px; color: #9aa0a6;
}
.ixcap-micro { text-decoration: none; }
.ixcap-micro-dot { color: #c4c8cc; }

/* GRID — reCAPTCHA tile grid; --ixcap-cols set inline (default 3). */
.ixcap-grid {
  display: grid;
  grid-template-columns: repeat(var(--ixcap-cols, 3), 1fr);
  gap: 3px; background: #d3d6da; padding: 0; border-radius: 2px;
}
.ixcap-tile {
  position: relative; overflow: hidden;
  aspect-ratio: 1 / 1; background: #e9ebed;
  cursor: pointer; user-select: none;
}
.ixcap-tileimg {
  position: absolute; inset: 0; width: 100%; height: 100%;
  object-fit: cover; display: block; pointer-events: none;
  -webkit-user-drag: none; user-drag: none;
}
/* corner tick — hidden until the tile is selected (ixcap-sel). Its OWN chrome
 * class; NEVER is-correct/is-answer (those stay unstyled DOM hooks). */
.ixcap-tick {
  position: absolute; top: 4px; left: 4px; z-index: 3;
  width: 22px; height: 22px; border-radius: 50%;
  background: #1a73e8; color: #fff;
  display: flex; align-items: center; justify-content: center;
  font-size: 14px; line-height: 1;
  transform: scale(0); opacity: 0;
  transition: transform .14s cubic-bezier(.2,.9,.3,1.4), opacity .14s ease;
}
.ixcap-tile.ixcap-sel .ixcap-tick { transform: scale(1); opacity: 1; }
.ixcap-tile.ixcap-sel { outline: 3px solid #1a73e8; outline-offset: -3px; }
.ixcap-tile.ixcap-sel .ixcap-tileimg { transform: scale(.86); transition: transform .16s ease; }

/* SPINNER — the "Verifying…" interstitial ring. */
.ixcap-spinner { display: flex; flex-direction: column; align-items: center; gap: 12px; padding: 20px 8px; }
.ixcap-ring {
  width: 44px; height: 44px; border-radius: 50%;
  border: 4px solid #e2e6ea; border-top-color: #1a73e8;
  animation: ixcap-spin 0.9s linear infinite;
  display: flex; align-items: center; justify-content: center;
  font-size: 20px; color: transparent;
}
@keyframes ixcap-spin { to { transform: rotate(360deg); } }
.ixcap-ring-done { animation: none; border-color: #e2e6ea; }
.ixcap-spin-ok .ixcap-ring { border-color: #1e8e3e; color: #1e8e3e; }
.ixcap-spin-dash .ixcap-ring { border-color: #9aa0a6; color: #9aa0a6; }
.ixcap-spinlabel { font-size: 13px; color: #5f6368; }

/* STAMP — rubber-stamp verdict, brief scale-in pop. */
.ixcap-stamp {
  display: inline-block; padding: 6px 14px;
  font-size: 15px; font-weight: 800; letter-spacing: 1px; text-transform: uppercase;
  border: 3px solid currentColor; border-radius: 6px;
  transform: rotate(-7deg) scale(0.2); opacity: 0;
  animation: ixcap-stamp-in .28s cubic-bezier(.2,.9,.3,1.5) forwards;
}
@keyframes ixcap-stamp-in { to { transform: rotate(-7deg) scale(1); opacity: .9; } }
.ixcap-stamp-ok { color: #1e8e3e; }
.ixcap-stamp-logged { color: #5f6368; }
.ixcap-stamp-flag { color: var(--intake-accent, #d93025); }

/* ---- BAND ACCENT CREEP -----------------------------------------------------
 * Calibration + Establishing: ZERO accent — pristine corporate chrome, so the
 * trust is real before it is spent. Deepening: a faint accent border/shadow.
 * Climax: a stronger accent halo. Recovery: gentle, cooled. The card body stays
 * white throughout — only the frame breathes accent, so the rot reads as the
 * interface decompensating, not as a themed skin. */
.ixcap-card[data-band="calibration"],
.ixcap-card[data-band="establishing"] { /* pristine — no accent */ }
.ixcap-card[data-band="deepening"] {
  border-color: color-mix(in srgb, var(--intake-accent, #d93025) 22%, #d3d6da);
  box-shadow: 0 2px 10px rgba(0,0,0,.35), 0 12px 40px rgba(0,0,0,.28),
              0 0 0 1px color-mix(in srgb, var(--intake-accent, #d93025) 14%, transparent);
}
.ixcap-card[data-band="climax"] {
  border-color: color-mix(in srgb, var(--intake-accent, #d93025) 48%, #d3d6da);
  box-shadow: 0 2px 10px rgba(0,0,0,.4), 0 14px 46px rgba(0,0,0,.34),
              0 0 22px color-mix(in srgb, var(--intake-accent, #d93025) 30%, transparent);
}
.ixcap-card[data-band="climax"] .ixcap-header {
  background: color-mix(in srgb, var(--intake-accent, #d93025) 26%, #4a90d9);
}
.ixcap-card[data-band="recovery"] {
  border-color: color-mix(in srgb, var(--intake-accent, #d93025) 12%, #d3d6da);
}

/* ---- DARK-NOIR VARIANT HOOK ------------------------------------------------
 * Opt-in per card via data-noir="1" (deeper set-pieces). Calibration cards must
 * never set it. The header keeps its blue so the widget still reads as a captcha. */
.ixcap-card[data-noir] {
  background: #14141f; color: #e7e2ee;
  border-color: #33334a;
}
.ixcap-card[data-noir] .ixcap-footer { border-top-color: #2a2a3d; }
.ixcap-card[data-noir] .ixcap-microfoot { color: #6b6b82; }
.ixcap-card[data-noir] .ixcap-brandname { color: #b9b4c8; }
.ixcap-card[data-noir] .ixcap-spinlabel { color: #b9b4c8; }
.ixcap-card[data-noir] .ixcap-grid { background: #2a2a3d; }

@media (prefers-reduced-motion: reduce) {
  .ixcap-ring { animation-duration: 2.4s; }
  .ixcap-tick { transition: none; }
  .ixcap-stamp { animation: none; transform: rotate(-7deg) scale(1); opacity: .9; }
}
`;

/* ----------------------------------------------------------------------------
 * Default aggregate — item modules + beats.js import this as `chrome`.
 * -------------------------------------------------------------------------- */
export const chrome = {
  VENDOR_NAME, VENDOR_TAGLINE,
  ensureCss, frame, roundel, spinner, stamp, gridShell,
  placeholderTile, mundaneTileSrc,
};
export default chrome;
