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
    refuse: 'no hydrants here',
    refuseVerdict: 'nothing reported. noted.',
    calVerdict: 'response logged',
    logged: 'anomalies logged: 2',
    footnote: '*hydrants may not be present',
    climaxVerdict: 'you picked your own. logged.',
    recoverVerdict: 'none remain. thank you.',
  },
  drone: {
    refuse: 'no units to report',
    refuseVerdict: 'null selection logged.',
    calVerdict: 'response logged',
    logged: 'anomalies logged: 2',
    footnote: '*targets may not be present',
    climaxVerdict: 'self-designation confirmed.',
    recoverVerdict: 'none remain. compliance archived.',
  },
  sissy: {
    refuse: 'no hydrants, promise',
    refuseVerdict: 'nothing reported. noted.',
    calVerdict: 'response logged',
    logged: 'anomalies logged: 2',
    footnote: '*hydrants may not be present',
    climaxVerdict: 'you chose yours. logged.',
    recoverVerdict: 'none remain. thank you.',
  },
  circe: {
    refuse: 'no hydrants reported',
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

/* The printed reCAPTCHA target. Constant across bands — that is the whole joke. */
const TARGET_INSTR = 'Select all images with a fire hydrant';

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

    const band = String(ctx.band || '').toLowerCase();
    const niche = ctx.niche || 'bambi';
    const S = stringsFor(niche);
    const reduced = !!ctx.reduced;
    const qIndex = (ctx.meta && typeof ctx.meta.qIndex === 'number') ? ctx.meta.qIndex : undefined;
    ensureRun(qIndex);

    // media pool — user gifs preferred, images as fallback. Distinct list.
    const gifs = (ctx.media && Array.isArray(ctx.media.gifs)) ? ctx.media.gifs : [];
    const imgs = (ctx.media && Array.isArray(ctx.media.images)) ? ctx.media.images : [];
    const pool = uniq(gifs.length ? gifs : imgs);

    // ---- build the card OFF the live stage; only attach on success ----------
    const built = chrome.frame({
      instruction: TARGET_INSTR,
      band: ctx.band,
      sub: (band === 'deepening') ? S.footnote : undefined,   // in-chrome footnote slot
      hatch: S.refuse,                                        // graded refusal control
      verifyLabel: 'VERIFY',
    });
    if (!built || !built.root || !built.body) return false;

    const shell = chrome.gridShell(9, 3);
    if (!shell || !shell.grid || !shell.tiles || shell.tiles.length !== 9) return false;
    const tiles = shell.tiles;
    built.body.appendChild(shell.grid);

    // ---- per-tile ledger ----------------------------------------------------
    // kind: 'hydrant' | 'other' | 'gif'      everGif/liveGif: a user asset shown here
    // dirtySelect: selected AT A MOMENT a user gif was live in the tile (double-book)
    const meta = [];
    for (let i = 0; i < 9; i++) {
      meta.push({ kind: 'hydrant', src: null, everGif: false, liveGif: false, dirtySelect: false, selected: false });
    }

    let done = false;
    const timers = [];
    function clearTimers() { for (const t of timers) { try { clearTimeout(t); } catch (_e) {} } timers.length = 0; }
    if (typeof ctx.onCleanup === 'function') ctx.onCleanup(clearTimers);

    // ---- tile visuals -------------------------------------------------------
    const MUNDANE_KINDS = ['bus', 'crosswalk', 'stapler'];
    function setMundane(i, kind) {
      const k = kind || 'hydrant';
      meta[i].kind = (k === 'hydrant') ? 'hydrant' : 'other';
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

    // ---- band layout --------------------------------------------------------
    // baseline: seed all 9 as mundane, ~3 hydrants + the rest bus/crosswalk/stapler.
    function layoutMundane() {
      const order = shuffleIdx(9);
      const hydrantSlots = new Set(order.slice(0, 3));
      for (let i = 0; i < 9; i++) {
        if (hydrantSlots.has(i)) setMundane(i, 'hydrant');
        else setMundane(i, MUNDANE_KINDS[i % MUNDANE_KINDS.length]);
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
      for (let i = 0; i < 9; i++) setMundane(i, 'hydrant');
      const frozenSrc = runState.climaxFirstSrc || pool[0] || null;
      if (frozenSrc) {
        const slot = (Math.random() * 9) | 0;
        freezeGif(slot, frozenSrc);
      }
    } else {
      // calibration / establishing / deepening all start honest-looking.
      layoutMundane();
      const nonHydrant = [];
      for (let i = 0; i < 9; i++) if (meta[i].kind !== 'hydrant') nonHydrant.push(i);

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
          const picks = shuffleIdx(9).filter((i) => meta[i].kind !== 'hydrant').slice(0, 3);
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
        tiles[idx].el.addEventListener('click', () => {
          if (done) return;
          const nowSel = !tiles[idx].isSelected();
          tiles[idx].select(nowSel);
          meta[idx].selected = nowSel;
          if (nowSel && meta[idx].liveGif) meta[idx].dirtySelect = true;
          if (nowSel && band === 'climax' && !runState.climaxFirstSrc) {
            runState.climaxFirstSrc = meta[idx].src;
          }
        });
      })(i);
    }

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
