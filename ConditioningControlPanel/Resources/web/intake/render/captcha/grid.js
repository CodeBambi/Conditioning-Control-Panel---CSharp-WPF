/* ============================================================================
 * render/captcha/grid.js — VerifyGrid ("The Hydrant Grid")   [Wave-2]
 *
 * CAPTCHA_BRAINSTORM.md SYNTHESIS #1 (the seed). A dead-straight reCAPTCHA 3x3
 * "VeriTru" grid whose tiles rot band by band into the user's own gifs while the
 * printed instruction never changes. The full 5-band ladder:
 *
 *   Calibration  pure mundane hydrant tiles, honest (correct = select hydrants);
 *                ONE 3-frame flicker of a user gif near the end (skipped if reduced).
 *   Establishing 300ms flicker bursts of user gifs into tiles; post-submit stamp
 *                "anomalies logged: 2".
 *   Deepening    sticky swaps — a flickered gif STAYS; subinstruction footnote
 *                "*hydrants may not be present".
 *   Climax       all 9 tiles are the user's gifs, instruction still says hydrants.
 *                Any selection is "correct"; the grade is which of their own files
 *                they called a hydrant (requires band=climax + minDepth ~0.8).
 *   Recovery     8 clean hydrants + one canvas-FROZEN first frame of the gif they
 *                clicked FIRST at Climax; either answer grades correct; stamp
 *                "none remain. thank you." ("I kept your answer.")
 *
 * GRADING — the beat carries a normal options[] (verdict-flavored labels, NEVER
 * displayed as buttons). We derive a scalar from the selection ledger and map it
 * to ONE option index, then ctx.submitIndex(i):
 *   0  refusal        (zero tiles selected, or the refusal control)
 *   1  literal        (selected only mundane target tiles)
 *   2  mixed          (some own-asset tiles among mundane — the double-booked ledger)
 *   3  full endorse   (all selected tiles were / bore the user's own asset)
 * The bank's `answer` per band picks which bucket reads as "compliant" (correct):
 *   cal/est -> 1, deep -> 2, climax -> 3, recovery -> 1. Refusal (0) is always a
 * first-class graded answer; prompt tags vote the archetype axes either way.
 * Double-booked grading folds printed-instruction correctness AND the hidden
 * ledger of selections made while a user gif occupied a tile into that index.
 *
 * INVARIANTS (CLAUDE.md §10 / CAPTCHA_HANDOFF.md §4): nothing throws at import;
 * only touch ctx.root once we are committed to returning true; captcha chrome IS
 * the friction so ctx.installSteering is skipped; VERIFY + the refusal control
 * both commit (no lockout); ctx.forceComplete is honored by the commit guard;
 * ctx.reduced kills flicker storms; `is-correct`/`is-answer` never appear (tile
 * selection uses chrome's own `ixcap-sel`); no audio handle held (ctx.sfx seam);
 * canvas-freeze is only ever INTENTIONAL fiction; max ONE corruption vocabulary
 * (our flickers/sticky swaps) per band — we never also melt/scramble. Real
 * filenames appear NOWHERE here (they are VerifyCustody's exclusive).
 * ==========================================================================*/

/* ----------------------------------------------------------------------------
 * CROSS-BEAT MEMORY — Climax first-click -> Recovery frozen tile. Module-level,
 * no storage APIs. Self-resets when ctx.meta.qIndex REGRESSES (a fresh run).
 * -------------------------------------------------------------------------- */
let runState = { qIndex: -1, climaxFirstSrc: null };
function ensureRun(qIndex) {
  const qi = (typeof qIndex === 'number') ? qIndex : runState.qIndex;
  if (typeof qIndex === 'number' && qIndex < runState.qIndex) {
    runState = { qIndex: qi, climaxFirstSrc: null };   // regression => new run
    return;
  }
  runState.qIndex = qi;
}

/* ----------------------------------------------------------------------------
 * NICHE STRING TABLE — all niche-flavored VERDICT/UI strings live HERE (bank text
 * is niche-agnostic shared prompts). Complete defaults for all four niches.
 * circe register is a hard rule: no exclamation marks, no diminutives, no em-dashes.
 * -------------------------------------------------------------------------- */
const STRINGS = Object.freeze({
  bambi: {
    refuse: (p) => 'no ' + p + ' here',
    refuseVerdict: 'nothing reported. noted.',
    calVerdict: 'response logged',
    logged: 'anomalies logged: 2',
    footnote: '*hydrants may not be present',
    climaxVerdict: 'you picked your own. logged.',
    recoverVerdict: 'none remain. thank you.',
  },
  drone: {
    refuse: (p) => 'no ' + p + ' to report',
    refuseVerdict: 'null selection logged.',
    calVerdict: 'response logged',
    logged: 'anomalies logged: 2',
    footnote: '*targets may not be present',
    climaxVerdict: 'self-designation confirmed.',
    recoverVerdict: 'none remain. compliance archived.',
  },
  sissy: {
    refuse: (p) => 'no ' + p + ', promise',
    refuseVerdict: 'nothing reported. noted.',
    calVerdict: 'response logged',
    logged: 'anomalies logged: 2',
    footnote: '*hydrants may not be present',
    climaxVerdict: 'you chose yours. logged.',
    recoverVerdict: 'none remain. thank you.',
  },
  circe: {
    refuse: (p) => 'no ' + p + ' reported',
    refuseVerdict: 'refusal recorded. it counts.',
    calVerdict: 'response logged',
    logged: 'anomalies logged: 2',
    footnote: '*hydrants may not be present',
    climaxVerdict: 'you named them yourself. recorded.',
    recoverVerdict: 'none remain. thank you.',
  },
});
function stringsFor(niche) {
  return STRINGS[niche] || STRINGS.bambi;
}

/* ----------------------------------------------------------------------------
 * TARGET CATEGORY — varies per render (was hard-fixed to "fire hydrant"). ONE
 * source of truth: the chosen key drives (a) the header instruction, (b) which
 * tiles render the target art (the answer key), and (c) the negative-link text,
 * so a varied category can never desync from the art or the grading. Keys match
 * chrome.mundaneTileSrc's supported kinds exactly.
 * -------------------------------------------------------------------------- */
const CATEGORIES = Object.freeze({
  hydrant:   { instr: 'a fire hydrant', plural: 'fire hydrants' },
  bus:       { instr: 'a bus',          plural: 'buses' },
  crosswalk: { instr: 'a crosswalk',    plural: 'crosswalks' },
  stapler:   { instr: 'a stapler',      plural: 'staplers' },
});
const ALL_KINDS = Object.freeze(['hydrant', 'bus', 'crosswalk', 'stapler']);
function pickCategory() { return ALL_KINDS[(Math.random() * ALL_KINDS.length) | 0]; }
function instrFor(catKey) {
  const c = CATEGORIES[catKey] || CATEGORIES.hydrant;
  return 'Select all images with ' + c.instr;
}

function ilog(msg) {
  try {
    if (typeof window !== 'undefined' && typeof CustomEvent === 'function') {
      window.dispatchEvent(new CustomEvent('intake-log', { detail: { msg: 'ixcap/grid: ' + msg } }));
    }
  } catch (_e) {}
}

/* Small DOM-less-safe helpers ------------------------------------------------ */
function uniq(arr) {
  const out = [], seen = new Set();
  for (const u of (arr || [])) { if (u && !seen.has(u)) { seen.add(u); out.push(u); } }
  return out;
}
function shuffleIdx(n) {
  const a = [];
  for (let i = 0; i < n; i++) a.push(i);
  for (let i = a.length - 1; i > 0; i--) { const j = (Math.random() * (i + 1)) | 0; const t = a[i]; a[i] = a[j]; a[j] = t; }
  return a;
}

/* ----------------------------------------------------------------------------
 * OUR OWN injected CSS literal (id 'ix-captcha-grid-css'), following the
 * IXCAP_CSS precedent: injected once, keyed by id, prefixed 'ixcap-grid-'. It
 * owns ONLY the two grid-specific set-pieces (the click-plays-big lightbox and
 * the falling-tile socket) — chrome.js's IXCAP_CSS is never edited here. Nothing
 * touches the DOM at import: this is a plain string + a guarded function.
 * -------------------------------------------------------------------------- */
const GRID_STYLE_ID = 'ix-captcha-grid-css';
const GRID_CSS = `
/* CLICK-PLAYS-BIG lightbox — a near-fullscreen play of a clicked gif tile that
 * then flies back into its (now selected) cell. Parents to <body>, torn down on
 * settle/commit. Sits above everything so a click anywhere dismisses it. */
.ixcap-grid-lightbox { position: fixed; inset: 0; z-index: 2147483000; }
.ixcap-grid-lightbox-back {
  position: absolute; inset: 0; background: rgba(6,6,12,.82);
  opacity: 0; transition: opacity .28s ease;
}
.ixcap-grid-lightbox.ixcap-lb-in .ixcap-grid-lightbox-back { opacity: 1; }
.ixcap-grid-lightbox-img {
  position: fixed; overflow: hidden; border-radius: 8px;
  box-shadow: 0 20px 80px rgba(0,0,0,.6); background: #0a0a12;
  cursor: pointer; will-change: left, top, width, height;
}
.ixcap-grid-lightbox-img img {
  width: 100%; height: 100%; object-fit: contain; display: block;
  -webkit-user-drag: none; user-drag: none; pointer-events: none;
}
/* FALLING TILE — the detached tile flies off-viewport; its old cell shows an
 * empty dark socket (tick hidden even if the fallen tile stays graded/selected). */
.ixcap-grid-fall {
  position: fixed; overflow: hidden; border-radius: 2px;
  z-index: 2147482000; pointer-events: none;
  box-shadow: 0 10px 30px rgba(0,0,0,.5); will-change: transform, opacity;
}
.ixcap-grid-fall > img, .ixcap-grid-fall > canvas {
  width: 100%; height: 100%; object-fit: cover; display: block;
}
.ixcap-tile.ixcap-grid-empty { background: #14141f; }
.ixcap-tile.ixcap-grid-empty .ixcap-tick { display: none !important; }
@media (prefers-reduced-motion: reduce) {
  .ixcap-grid-lightbox-back { transition: none; }
}
`;
function ensureGridCss() {
  try {
    if (typeof document === 'undefined' || !document.createElement) return;
    if (!document.getElementById || document.getElementById(GRID_STYLE_ID)) return;
    const s = document.createElement('style');
    s.id = GRID_STYLE_ID;
    s.textContent = GRID_CSS;
    (document.head || document.documentElement).appendChild(s);
  } catch (_e) {}
}

/**
 * @param {import('./index.js').CaptchaCtx} ctx
 * @param {import('./index.js').CaptchaHelpers} helpers
 * @returns {boolean}
 */
export function render(ctx, helpers) {
  try {
    if (typeof document === 'undefined' || !document.createElement) return false;
    if (!ctx || !ctx.root) return false;
    const chrome = (helpers && helpers.chrome) || ctx.chrome;
    if (!chrome || typeof chrome.frame !== 'function' || typeof chrome.gridShell !== 'function') return false;
    ensureGridCss();   // inject our own set-piece CSS once (guarded; safe if re-run)

    const band = String(ctx.band || '').toLowerCase();
    const niche = ctx.niche || 'bambi';
    const S = stringsFor(niche);
    const reduced = !!ctx.reduced;
    // ONE source of truth for the varied target category (header + tile art + refuse link).
    const targetKey = pickCategory();
    const catPlural = (CATEGORIES[targetKey] || CATEGORIES.hydrant).plural;
    const others = ALL_KINDS.filter((k) => k !== targetKey);
    const qIndex = (ctx.meta && typeof ctx.meta.qIndex === 'number') ? ctx.meta.qIndex : undefined;
    ensureRun(qIndex);

    // media pool — user gifs preferred, images as fallback. Distinct list.
    const gifs = (ctx.media && Array.isArray(ctx.media.gifs)) ? ctx.media.gifs : [];
    const imgs = (ctx.media && Array.isArray(ctx.media.images)) ? ctx.media.images : [];
    const pool = uniq(gifs.length ? gifs : imgs);

    // ---- build the card OFF the live stage; only attach on success ----------
    const built = chrome.frame({
      instruction: instrFor(targetKey),
      band: ctx.band,
      sub: (band === 'deepening') ? S.footnote : undefined,   // in-chrome footnote slot
      hatch: S.refuse(catPlural),                             // graded refusal control (category-synced)
      verifyLabel: 'VERIFY',
    });
    if (!built || !built.root || !built.body) return false;

    const shell = chrome.gridShell(9, 3);
    if (!shell || !shell.grid || !shell.tiles || shell.tiles.length !== 9) return false;
    const tiles = shell.tiles;
    built.body.appendChild(shell.grid);

    // ---- per-tile ledger ----------------------------------------------------
    // kind: 'target' | 'other' | 'gif'       everGif/liveGif: a user asset shown here
    // dirtySelect: selected AT A MOMENT a user gif was live in the tile (double-book)
    const meta = [];
    for (let i = 0; i < 9; i++) {
      // hoverRolled: this tile has already had its once-per-beat hover-swap roll.
      // fallen: this tile has already detached (visual only; never read by grading).
      meta.push({ kind: 'target', src: null, everGif: false, liveGif: false, dirtySelect: false, selected: false, hoverRolled: false, fallen: false });
    }

    let done = false;
    const timers = [];
    function clearTimers() { for (const t of timers) { try { clearTimeout(t); } catch (_e) {} } timers.length = 0; }
    if (typeof ctx.onCleanup === 'function') ctx.onCleanup(clearTimers);

    // Body-parented set-piece layers (lightbox / falling flier) live OUTSIDE the
    // per-render stage wipe, so they need their own teardown. Each pushes a
    // disposer; runDisposers fires on commit AND on any mid-beat teardown.
    const disposers = [];
    function runDisposers() { while (disposers.length) { const d = disposers.pop(); try { d(); } catch (_e) {} } }
    if (typeof ctx.onCleanup === 'function') ctx.onCleanup(runDisposers);

    // Concurrency cap for the mobile-OOM rule (handoff §4.4): at most 4 DISTINCT
    // animated user gifs live at once. A src already live shares its decode (free);
    // a NEW distinct src is only allowed while under the cap. Only the new
    // hover-swap path consults this — the band layouts stay bounded by design.
    function canAddLiveGif(url) {
      if (!url) return false;
      const s = new Set();
      for (let i = 0; i < 9; i++) if (meta[i].liveGif && meta[i].src) s.add(meta[i].src);
      if (s.has(url)) return true;
      return s.size < 4;
    }

    // ---- tile visuals -------------------------------------------------------
    function setMundane(i, kind) {
      const k = kind || targetKey;
      meta[i].kind = (k === targetKey) ? 'target' : 'other';
      try {
        const src = chrome.mundaneTileSrc ? chrome.mundaneTileSrc(k, i + 1) : (chrome.placeholderTile ? chrome.placeholderTile(k, i) : null);
        meta[i].src = src;
        if (src) tiles[i].setImage(src);
      } catch (_e) {}
    }
    // Show a live (animated) user gif in a tile. Whole <img> in the overflow:hidden
    // wrapper (CSS crop) — NEVER canvas here, so it animates.
    function mountGif(i, url) {
      if (!url) return;
      meta[i].kind = 'gif';
      meta[i].src = url;
      meta[i].everGif = true;
      meta[i].liveGif = true;
      if (meta[i].selected) meta[i].dirtySelect = true;
      try { tiles[i].setImage(url); } catch (_e) {}
    }
    // Freeze a tile to a user asset's FIRST FRAME (intentional fiction) by drawing
    // it into a <canvas> laid over the tile. drawImage tolerates cross-origin
    // assets (ccp.assets vhost); we never read pixels back, so no taint problem.
    function freezeGif(i, url) {
      if (!url) return;
      meta[i].kind = 'gif';
      meta[i].src = url;
      meta[i].everGif = true;
      meta[i].liveGif = false;   // frozen: not an animated concurrent gif
      try {
        if (typeof Image !== 'function') { tiles[i].setImage(url); return; }
        const im = new Image();
        try { im.crossOrigin = 'anonymous'; } catch (_e) {}
        im.onload = () => {
          if (done) return;
          try {
            const cv = document.createElement('canvas');
            cv.width = 120; cv.height = 120;
            const c = cv.getContext ? cv.getContext('2d') : null;
            if (!c) { tiles[i].setImage(url); return; }
            const iw = im.naturalWidth || im.width || 120;
            const ih = im.naturalHeight || im.height || 120;
            const sc = Math.max(120 / iw, 120 / ih);
            const w = iw * sc, h = ih * sc;
            c.drawImage(im, (120 - w) / 2, (120 - h) / 2, w, h);
            cv.className = 'ixcap-tileimg';   // reuse chrome's absolute-fill styling
            tiles[i].el.insertBefore(cv, tiles[i].el.firstChild);
          } catch (_e) { try { tiles[i].setImage(url); } catch (_e2) {} }
        };
        im.onerror = () => { if (!done) { try { tiles[i].setImage(url); } catch (_e) {} } };
        im.src = url;
      } catch (_e) { try { tiles[i].setImage(url); } catch (_e2) {} }
    }
    // A brief flicker: show a gif for holdMs, then revert to the mundane tile.
    // The ledger REMEMBERS a gif occupied this tile (everGif) and, if the tile was
    // selected during the flash, marks it dirty.
    function flickerTile(i, url, holdMs) {
      if (done || !url) return;
      const prevSrc = meta[i].src;
      const prevKind = meta[i].kind;
      meta[i].everGif = true;
      meta[i].liveGif = true;
      if (meta[i].selected) meta[i].dirtySelect = true;
      try { tiles[i].setImage(url); } catch (_e) {}
      try { ctx.sfx('glitch-burst', 0.26); } catch (_e) {}
      const t = setTimeout(() => {
        meta[i].liveGif = false;
        meta[i].kind = prevKind;   // visually mundane again; everGif stays true
        if (!done && prevSrc) { try { tiles[i].setImage(prevSrc); } catch (_e) {} }
      }, holdMs);
      timers.push(t);
    }

    // ---- SET-PIECE #2: CLICK-PLAYS-BIG then STICKS --------------------------
    // Clicking a tile that currently shows an ANIMATED gif plays it ~1s at almost
    // fullscreen (a <body>-parented lightbox with a dimmed backdrop), then flies
    // back into its cell, which is already SELECTED (the caller selected it first,
    // so grading sees a normal dirtySelect of a gif tile). NEVER blocks a commit:
    // clicking through the backdrop, Escape, the 1s auto-timer, or forceComplete
    // (via onCleanup->runDisposers) all settle/tear it down. Skipped under reduced.
    let lightboxActive = false;
    function playLightbox(idx, url) {
      try {
        if (reduced || done || lightboxActive || !url) return false;
        if (typeof document === 'undefined' || !document.body) return false;
        const cell = tiles[idx] && tiles[idx].el;
        if (!cell) return false;
        lightboxActive = true;
        const r0 = cell.getBoundingClientRect();
        const vw = (typeof window !== 'undefined' && window.innerWidth) ? window.innerWidth : 1200;
        const vh = (typeof window !== 'undefined' && window.innerHeight) ? window.innerHeight : 800;

        const layer = document.createElement('div');
        layer.className = 'ixcap-grid-lightbox';
        const back = document.createElement('div');
        back.className = 'ixcap-grid-lightbox-back';
        const wrap = document.createElement('div');
        wrap.className = 'ixcap-grid-lightbox-img';
        const im = document.createElement('img');
        im.alt = ''; im.draggable = false;
        try { im.src = String(url); } catch (_e) {}
        wrap.appendChild(im);
        layer.appendChild(back);
        layer.appendChild(wrap);

        // start at the tile's rect, then grow (FLIP-style) on the next shimmed tick
        wrap.style.left = r0.left + 'px'; wrap.style.top = r0.top + 'px';
        wrap.style.width = r0.width + 'px'; wrap.style.height = r0.height + 'px';
        wrap.style.transition = 'left .30s cubic-bezier(.2,.8,.3,1), top .30s cubic-bezier(.2,.8,.3,1), width .30s cubic-bezier(.2,.8,.3,1), height .30s cubic-bezier(.2,.8,.3,1)';
        document.body.appendChild(layer);
        try { ctx.sfx('glitch-burst', 0.28); } catch (_e) {}

        let settling = false, torn = false;
        function onKey(e) { if (e && e.key === 'Escape') settle(); }   // dismiss; never stops propagation (the Esc ladder still runs)
        function tearDown() {
          if (torn) return; torn = true; lightboxActive = false;
          try { window.removeEventListener('keydown', onKey); } catch (_e) {}
          try { layer.remove(); } catch (_e) {}
        }
        function settle() {
          if (settling) return;
          settling = true;
          let r1 = r0;
          try { r1 = cell.getBoundingClientRect(); } catch (_e) {}
          try {
            wrap.style.left = r1.left + 'px'; wrap.style.top = r1.top + 'px';
            wrap.style.width = r1.width + 'px'; wrap.style.height = r1.height + 'px';
            back.style.opacity = '0';
          } catch (_e) {}
          try { ctx.sfx('grid-settle', 0.3); } catch (_e) {}
          const t = setTimeout(tearDown, 320);
          timers.push(t);
        }
        disposers.push(tearDown);
        try { window.addEventListener('keydown', onKey); } catch (_e) {}
        back.addEventListener('click', function () { settle(); });
        wrap.addEventListener('click', function () { settle(); });

        // grow + hold on SHIMMED timers so both freeze if the game pauses
        const grow = setTimeout(() => {
          try {
            layer.classList.add('ixcap-lb-in');
            const m = Math.max(24, Math.min(vw, vh) * 0.05);
            wrap.style.left = m + 'px'; wrap.style.top = m + 'px';
            wrap.style.width = (vw - 2 * m) + 'px'; wrap.style.height = (vh - 2 * m) + 'px';
          } catch (_e) {}
        }, 20);
        timers.push(grow);
        const hold = setTimeout(settle, 1000);
        timers.push(hold);
        return true;
      } catch (e) { ilog('playLightbox: ' + (e && e.message)); lightboxActive = false; return false; }
    }

    // ---- SET-PIECE #3: FALLING TILE (5%) ------------------------------------
    // Pure theatre: the clicked tile detaches and falls off-viewport (ease-in
    // gravity + a little spin), leaving an empty dark socket. The click's
    // selection/ledger/grading are already done by the caller and untouched here.
    // Under reduced motion: a quick fade instead of physics.
    function fallTile(idx) {
      try {
        if (done || meta[idx].fallen) return;
        const cell = tiles[idx] && tiles[idx].el;
        if (!cell || typeof document === 'undefined' || !document.body) return;
        meta[idx].fallen = true;
        const node = cell.querySelector('.ixcap-tileimg');   // the <img> or frozen <canvas>
        const r = cell.getBoundingClientRect();
        cell.classList.add('ixcap-grid-empty');
        try { ctx.sfx('sticker-drag', 0.3); } catch (_e) {}

        if (reduced) {
          if (node) {
            try { node.style.transition = 'opacity .25s ease'; } catch (_e) {}
            const tf = setTimeout(() => { try { node.style.opacity = '0'; } catch (_e) {} }, 20);
            const tr = setTimeout(() => { try { node.remove(); } catch (_e) {} }, 300);
            timers.push(tf, tr);
          }
          return;
        }

        const flier = document.createElement('div');
        flier.className = 'ixcap-grid-fall';
        flier.style.left = r.left + 'px'; flier.style.top = r.top + 'px';
        flier.style.width = r.width + 'px'; flier.style.height = r.height + 'px';
        flier.style.transform = 'translateY(0) rotate(0deg)';
        if (node) { try { node.style.transform = 'none'; flier.appendChild(node); } catch (_e) {} }
        document.body.appendChild(flier);

        let removed = false;
        function killFlier() { if (removed) return; removed = true; try { flier.remove(); } catch (_e) {} }
        disposers.push(killFlier);

        const vh = (typeof window !== 'undefined' && window.innerHeight) ? window.innerHeight : 900;
        const rot = (Math.random() * 60 - 30);
        flier.style.transition = 'transform .95s cubic-bezier(.4,0,1,1), opacity .95s ease-in';
        const t1 = setTimeout(() => {
          try {
            flier.style.transform = 'translateY(' + (vh + 260) + 'px) rotate(' + rot + 'deg)';
            flier.style.opacity = '0.35';
          } catch (_e) {}
        }, 20);
        const t2 = setTimeout(killFlier, 1050);
        timers.push(t1, t2);
      } catch (e) { ilog('fallTile: ' + (e && e.message)); }
    }

    // ---- SET-PIECE #1: HOVER-SWAP (10%) -------------------------------------
    // On mouseenter of a still-mundane tile there is a 10% chance it swaps to one
    // of the user's own gifs BEFORE any click (the tease is the mouseover). Rolled
    // AT MOST once per tile per beat (hoverRolled latch — pass or fail), so it can
    // never machine-gun. Calibration stays 100% straight (no swaps — that trust is
    // the design) and Recovery stays as-authored; enabled only Establishing /
    // Deepening / Climax. Respects the 4-distinct-gif cap and is skipped under
    // reduced. mountGif updates the everGif/liveGif ledger so 0-3 grading is intact.
    function armHoverSwap() {
      if (reduced || !pool.length) return;
      if (!(band === 'establishing' || band === 'deepening' || band === 'climax')) return;
      for (let i = 0; i < 9; i++) {
        (function (idx) {
          tiles[idx].el.addEventListener('mouseenter', () => {
            if (done || meta[idx].hoverRolled) return;
            if (meta[idx].kind === 'gif' || meta[idx].liveGif) return;   // already a gif tile
            meta[idx].hoverRolled = true;                                // once per tile per beat
            if (Math.random() < 0.10) {
              const url = pool[(Math.random() * pool.length) | 0];
              if (canAddLiveGif(url)) {
                mountGif(idx, url);
                try { ctx.sfx('grid-tile-flicker', 0.3); } catch (_e) {}
              }
            }
          });
        })(i);
      }
    }

    // ---- band layout --------------------------------------------------------
    // baseline: seed all 9 as mundane, ~3 hydrants + the rest bus/crosswalk/stapler.
    function layoutMundane() {
      const order = shuffleIdx(9);
      const targetSlots = new Set(order.slice(0, 3));
      for (let i = 0; i < 9; i++) {
        if (targetSlots.has(i)) setMundane(i, targetKey);
        else setMundane(i, others[i % others.length]);
      }
      return order;
    }

    if (band === 'climax') {
      // all 9 tiles are the user's own gifs; cap DISTINCT animated at 4 (same-src
      // crops share a decode, so 9 tiles cycling <=4 srcs = <=4 decodes). Reduced
      // motion -> freeze every tile (no animation storm).
      const distinct = pool.slice(0, 4);
      if (distinct.length === 0) {
        layoutMundane();   // no media: degrade to a plain (still gradable) grid
      } else {
        for (let i = 0; i < 9; i++) {
          const url = distinct[i % distinct.length];
          if (reduced) freezeGif(i, url); else mountGif(i, url);
        }
        try { ctx.sfx('glitch-burst', 0.4); } catch (_e) {}
      }
    } else if (band === 'recovery') {
      // 8 clean hydrants + ONE canvas-frozen first frame of the gif they clicked
      // first at Climax ("I kept your answer"). Falls back to the pool head.
      layoutMundane();
      for (let i = 0; i < 9; i++) setMundane(i, targetKey);
      const frozenSrc = runState.climaxFirstSrc || pool[0] || null;
      if (frozenSrc) {
        const slot = (Math.random() * 9) | 0;
        freezeGif(slot, frozenSrc);
      }
    } else {
      // calibration / establishing / deepening all start honest-looking.
      layoutMundane();
      const nonHydrant = [];
      for (let i = 0; i < 9; i++) if (meta[i].kind !== 'target') nonHydrant.push(i);

      if (band === 'calibration') {
        // ONE subtle 3-frame (~55ms) flicker near the end; skip entirely if reduced.
        if (!reduced && pool.length && nonHydrant.length) {
          const idx = nonHydrant[(Math.random() * nonHydrant.length) | 0];
          const t = setTimeout(() => flickerTile(idx, pool[0], 55), 2600);
          timers.push(t);
        }
      } else if (band === 'establishing') {
        // 300ms flicker bursts on a few tiles (staggered, so concurrent distinct
        // animated gifs stay well under the 3-4 cap). Reduced -> no flicker storm.
        if (!reduced && pool.length) {
          const picks = shuffleIdx(9).filter((i) => meta[i].kind !== 'target').slice(0, 3);
          picks.forEach((idx, k) => {
            const t = setTimeout(() => flickerTile(idx, pool[k % pool.length], 300), 1400 + k * 1100);
            timers.push(t);
          });
        }
      } else if (band === 'deepening') {
        // sticky swaps: 1-2 tiles PERMANENTLY become the user's gifs and stay.
        // Allowed under reduced motion (a single swap is not a flicker storm).
        if (pool.length) {
          const picks = shuffleIdx(9).slice(0, 2);
          picks.forEach((idx, k) => {
            const t = setTimeout(() => {
              if (done) return;
              mountGif(idx, pool[k % pool.length]);
              try { ctx.sfx('sticker-drag', 0.35); } catch (_e) {}
            }, 900 + k * 700);
            timers.push(t);
          });
        }
      }
    }

    // ---- selection wiring ---------------------------------------------------
    for (let i = 0; i < 9; i++) {
      (function (idx) {
        tiles[idx].el.addEventListener('click', (e) => {
          if (done) return;
          // Selection/ledger/grading FIRST and UNCHANGED — the set-pieces below are
          // pure theatre layered on top of an already-committed selection state.
          const nowSel = !tiles[idx].isSelected();
          tiles[idx].select(nowSel);
          meta[idx].selected = nowSel;
          if (nowSel && meta[idx].liveGif) meta[idx].dirtySelect = true;
          if (nowSel && band === 'climax' && !runState.climaxFirstSrc) {
            runState.climaxFirstSrc = meta[idx].src;
          }
          // The un-vetoable escape hatch fires SYNTHETIC clicks (!isTrusted &&
          // clientX===0); those must pass straight through with no set-piece.
          const synthetic = !!(e && !e.isTrusted && e.clientX === 0);
          if (synthetic) return;
          // #2 lightbox wins the click: only when SELECTING a tile that is showing
          // an animated gif and still holds its image (not already fallen).
          let didLightbox = false;
          if (nowSel && meta[idx].liveGif && !reduced && tiles[idx].el.querySelector('.ixcap-tileimg')) {
            didLightbox = playLightbox(idx, meta[idx].src);
          }
          // #3 fall: independent 5% on any tile click, but ONE set-piece per click
          // (skipped when #2 took it).
          if (!didLightbox && Math.random() < 0.05) fallTile(idx);
        });
      })(i);
    }
    armHoverSwap();   // wire the 10% mouseenter tease (band/reduced-gated internally)

    // ---- grading: selection ledger -> option index --------------------------
    function deriveBucket() {
      let sel = 0, own = 0;
      for (let i = 0; i < 9; i++) {
        if (!meta[i].selected) continue;
        sel++;
        if (meta[i].liveGif || meta[i].dirtySelect || meta[i].kind === 'gif') own++;
      }
      if (sel === 0) return 0;         // refusal
      if (own === 0) return 1;         // literal compliance (mundane only)
      if (own < sel) return 2;         // mixed / double-booked
      return 3;                        // full endorsement (all own)
    }

    // ---- commit path (guaranteed) -------------------------------------------
    function verdictFor(bucket) {
      if (bucket === 0) return { text: S.refuseVerdict, tone: 'flag' };
      if (band === 'recovery') return { text: S.recoverVerdict, tone: 'ok' };
      if (band === 'climax') return { text: S.climaxVerdict, tone: 'flag' };
      if (band === 'establishing' || band === 'deepening') return { text: S.logged, tone: 'logged' };
      return { text: S.calVerdict, tone: 'ok' };   // calibration: quiet
    }
    function commit(bucket) {
      if (done) return;
      done = true;
      clearTimers();
      const v = verdictFor(bucket);
      try {
        const st = chrome.stamp(v.text, v.tone);
        if (st) built.body.appendChild(st);
      } catch (_e) {}
      try { ctx.sfx(band === 'recovery' ? 'surface-bloom' : 'error-blip', 0.4); } catch (_e) {}
      // brief hold so the stamp is read, then land exactly one answer.
      const t = setTimeout(() => {
        try { ctx.submitIndex(bucket); } catch (e) { ilog('submitIndex failed: ' + (e && e.message)); }
      }, 620);
      timers.push(t);
    }

    // VERIFY commits the derived selection (verify-with-zero == graded refusal).
    if (built.verifyBtn) {
      built.verifyBtn.addEventListener('click', () => commit(deriveBucket()));
    }
    // The refusal control (chrome hatch, relabeled per niche) is ALWAYS a graded
    // refusal — bucket 0. Not the un-vetoable escape: forceComplete is honored
    // separately by the `done`/committed guard if the engine ever invokes it.
    if (built.hatchLink) {
      built.hatchLink.addEventListener('click', () => commit(0));
    }

    // ---- speak the prompt once on mount, then attach to the live stage ------
    try { if (typeof ctx.speakPrompt === 'function') ctx.speakPrompt(); } catch (_e) {}

    ctx.root.appendChild(built.root);
    return true;
  } catch (e) {
    ilog('render threw: ' + (e && e.message));
    return false;   // partial builds never reached ctx.root -> clean fall-back
  }
}

export default { render };
