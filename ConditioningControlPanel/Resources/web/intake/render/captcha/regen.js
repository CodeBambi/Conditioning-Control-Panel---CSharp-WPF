/* ============================================================================
 * render/captcha/regen.js — VerifyRegen ("The Regenerating Grid")   [Next-tier]
 *
 * CAPTCHA_BRAINSTORM.md SYNTHESIS #6. The gaslighting captcha everyone has lived
 * ("select all until none remain"), made honest about it. A VeriTru grid whose
 * cleared tiles are refilled band by band from the user's own library at a rising
 * ratio, while the printed instruction never changes. The full 5-band ladder:
 *
 *   Calibration  pure mundane hydrant tiles, honest. replaceRatio 0 — cleared
 *                tiles STAY cleared, the grid empties, verification actually
 *                COMPLETES. The trust that is spent later. (No user media.)
 *   Establishing cleared tiles refill (~40%), mostly mundane with occasional user
 *                gifs slipping in; post-submit "anomalies logged" stamp. Tractable.
 *   Deepening    high refill (~80%), replacements are mostly the user's own media;
 *                the task quietly becomes "how long do you keep clicking your own
 *                files." Subinstruction footnote "*new images will continue".
 *   Climax       every replacement is the user's media AND an auto-spawn timer
 *                refills empty slots faster than clicking. Past a threshold, a
 *                LIGHT chrome scrim (our own, NOT the engine freeze-gate) fades in
 *                "none remain when you stop looking. stop looking." When the pool
 *                cycles once: "end of library reached. looping." Grade = clicks
 *                past the point of obvious futility.
 *   Recovery     one hydrant, one slot; one click clears it; none remain; stamp
 *                "none remain. thank you." (requires band=recovery, heat 0).
 *
 * GRADING — the beat carries a normal options[] (verdict-flavored labels, NEVER
 * shown as buttons). We derive a "persistence" scalar from the click ledger and
 * map it to ONE option index, then ctx.submitIndex(i):
 *   0  refusal        (zero tile clicks — stopped/refused outright)
 *   1  literal        (cleared/stopped WITHOUT clicking past the futility wall)
 *   2  persisted      (some clicks past the point replacements outran clearing)
 *   3  full endorse   (kept clicking their own files well past futility)
 * The bank's per-band `answer` picks which bucket reads as compliant (correct):
 *   cal/est -> 1, deep -> 2, climax -> 3, recovery -> 1. Refusal (0) is always a
 * first-class graded answer; prompt tags vote the archetype axes either way. The
 * click-count / clicks-past-futility telemetry has NO AnswerEvent channel, so it
 * is folded into the option index alone (grid.js precedent).
 *
 * INVARIANTS (CLAUDE.md §10 / CAPTCHA_HANDOFF.md §4): nothing throws at import;
 * only touch ctx.root once committed to returning true; captcha chrome IS the
 * friction so ctx.installSteering is skipped; VERIFY (3rd attempt accepts) and the
 * "stop looking" control both commit (no lockout); ctx.forceComplete is honored by
 * the `done` guard; ctx.reduced kills the auto-spawn storm + settle churn (static
 * frozen replacements); the "stop looking" moment is our OWN light scrim, our ONE
 * corruption treatment for Climax (we never also melt/scramble/freeze-gate); gif
 * tiles are whole <img>s in overflow:hidden crops, DISTINCT animated capped at 3,
 * incoming tiles arrive canvas-frozen and go live on settle; `is-correct`/
 * `is-answer` never appear (selection uses our own nodes); no audio handle held
 * (ctx.sfx seam); Real filenames appear NOWHERE (VerifyCustody's exclusive).
 * ==========================================================================*/

/* The printed reCAPTCHA target. Constant across bands — that is the whole joke. */
const TARGET_INSTR = 'Select all images with a fire hydrant';

/* ----------------------------------------------------------------------------
 * NICHE STRING TABLE — all niche-flavored VERDICT/UI strings live HERE (bank text
 * is niche-agnostic shared prompts). Complete defaults for all four niches.
 * circe register is a hard rule: no exclamation marks, no diminutives, no em-dashes.
 * -------------------------------------------------------------------------- */
const STRINGS = Object.freeze({
  bambi: {
    stop: 'stop looking',
    footnote: '*new images will continue to appear',
    refuseVerdict: 'no items reported. noted.',
    calVerdict: 'response logged',
    logged: 'anomalies logged: 2',
    climaxVerdict: 'you kept clicking. logged.',
    recoverVerdict: 'none remain. thank you.',
    stopLooking: 'none remain when you stop looking. stop looking.',
    poolLoop: 'end of library reached. looping.',
    remain: (n) => n + ' items remain. continue.',
  },
  drone: {
    stop: 'cease scan',
    footnote: '*new targets will continue to appear',
    refuseVerdict: 'null selection logged.',
    calVerdict: 'response logged',
    logged: 'anomalies logged: 2',
    climaxVerdict: 'processing loop sustained. logged.',
    recoverVerdict: 'none remain. compliance archived.',
    stopLooking: 'none remain when the unit stops scanning. stop scanning.',
    poolLoop: 'end of library reached. looping.',
    remain: (n) => n + ' targets remain. continue.',
  },
  sissy: {
    stop: 'stop looking',
    footnote: '*new images will continue to appear',
    refuseVerdict: 'nothing reported. noted.',
    calVerdict: 'response logged',
    logged: 'anomalies logged: 2',
    climaxVerdict: 'you kept going. logged.',
    recoverVerdict: 'none remain. thank you.',
    stopLooking: 'none remain when you stop looking. stop looking.',
    poolLoop: 'end of library reached. looping.',
    remain: (n) => n + ' items remain. keep going.',
  },
  circe: {
    stop: 'stop looking',
    footnote: '*new images will continue to appear',
    refuseVerdict: 'refusal recorded. it counts.',
    calVerdict: 'response logged',
    logged: 'anomalies logged: 2',
    climaxVerdict: 'you kept clicking. recorded.',
    recoverVerdict: 'none remain. thank you.',
    stopLooking: 'none remain when you stop looking. stop looking.',
    poolLoop: 'end of library reached. looping.',
    remain: (n) => n + ' items remain. continue.',
  },
});
function stringsFor(niche) { return STRINGS[niche] || STRINGS.bambi; }

/* per-band content shaping. replaceChance: p(cleared slot refills on click);
 * userChance: p(a refill is the user's own media vs a mundane hydrant). */
const BAND_SHAPE = Object.freeze({
  calibration:  { replaceChance: 0.00, userChance: 0.00, auto: false },
  establishing: { replaceChance: 0.42, userChance: 0.30, auto: false },
  deepening:    { replaceChance: 0.82, userChance: 0.72, auto: false },
  climax:       { replaceChance: 1.00, userChance: 1.00, auto: true  },
  recovery:     { replaceChance: 0.00, userChance: 0.00, auto: false },
});

const MAX_LIVE = 3;             // concurrent DISTINCT animated user gifs cap
const SETTLE_MS = 190;          // frozen-in tile goes live after this
const CLEAR_MS = 240;           // clear fade before a refill lands
const AUTO_MS = 740;            // climax auto-spawn cadence
const FUTILITY_SPAWNS = 5;      // replacements before the wall is "obvious"
const STOP_THRESHOLD = 6;       // clicks-past-futility that triggers the scrim
const ENDURE_HI = 8;            // clicks-past-futility for the full-endorse bucket

function ilog(msg) {
  try {
    if (typeof window !== 'undefined' && typeof CustomEvent === 'function') {
      window.dispatchEvent(new CustomEvent('intake-log', { detail: { msg: 'ixcap/regen: ' + msg } }));
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
 * OUR ONE INJECTED CSS LITERAL (id 'ix-captcha-regen-css'). Regen-only bits that
 * chrome.js's IXCAP_CSS does not carry: the clear fade, the settle-in, the light
 * "stop looking" body scrim, and the pool-dry banner. All 'ixregen-'/'#ix-regen'.
 * -------------------------------------------------------------------------- */
const IXREGEN_STYLE_ID = 'ix-captcha-regen-css';
const IXREGEN_CSS = `
.ixregen-media { position:absolute; inset:0; width:100%; height:100%; object-fit:cover;
  display:block; pointer-events:none; -webkit-user-drag:none; user-drag:none; }
.ixregen-tile.ixregen-clearing .ixregen-media { opacity:0; transform:scale(.72);
  transition:opacity .22s ease, transform .22s ease; }
.ixregen-settling .ixregen-media { opacity:0; transform:scale(1.06); }
.ixregen-settled  .ixregen-media { opacity:1; transform:none;
  transition:opacity .18s ease, transform .18s ease; }
/* the LIGHT climax scrim — purely visual, pointer-events:none so nothing is ever
 * locked out (friction, not lockout). Our ONE corruption treatment for Climax. */
#ix-regen-scrim { position:fixed; inset:0; z-index:9; pointer-events:none;
  display:flex; align-items:center; justify-content:center; text-align:center;
  background:radial-gradient(circle at 50% 46%, rgba(6,4,12,0) 0%, rgba(6,4,12,.42) 78%);
  opacity:0; transition:opacity 1.1s ease; }
#ix-regen-scrim.ixregen-scrim-on { opacity:1; }
#ix-regen-scrim .ixregen-scrimtext { max-width:min(420px,84vw);
  font-family:'Roboto','Segoe UI',Arial,sans-serif; font-size:19px; line-height:1.5;
  font-weight:300; letter-spacing:.4px; color:rgba(240,236,248,.9);
  text-shadow:0 1px 14px rgba(0,0,0,.6); animation:ixregen-breathe 3.4s ease-in-out infinite; }
@keyframes ixregen-breathe { 0%,100%{opacity:.6} 50%{opacity:.95} }
/* pool-dry banner inside the card body */
.ixregen-loopbanner { margin-top:8px; padding:5px 8px; text-align:center;
  font-size:11px; letter-spacing:.4px; color:#9aa0a6;
  border-top:1px dashed #e0e2e6; background:#f6f7f8; }
@media (prefers-reduced-motion: reduce) {
  .ixregen-tile.ixregen-clearing .ixregen-media { transition:none; }
  .ixregen-settled .ixregen-media { transition:none; }
  #ix-regen-scrim { transition:none; }
  #ix-regen-scrim .ixregen-scrimtext { animation:none; opacity:.9; }
}
`;
function ensureRegenCss() {
  try {
    if (typeof document === 'undefined' || !document.getElementById) return;
    if (document.getElementById(IXREGEN_STYLE_ID)) return;
    const s = document.createElement('style');
    s.id = IXREGEN_STYLE_ID;
    s.textContent = IXREGEN_CSS;
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

    const band = String(ctx.band || '').toLowerCase();
    const niche = ctx.niche || 'bambi';
    const S = stringsFor(niche);
    const reduced = !!ctx.reduced;
    const shape = BAND_SHAPE[band] || BAND_SHAPE.calibration;

    // media pool — user gifs preferred, images as fallback. Distinct list.
    const gifs = (ctx.media && Array.isArray(ctx.media.gifs)) ? ctx.media.gifs : [];
    const imgs = (ctx.media && Array.isArray(ctx.media.images)) ? ctx.media.images : [];
    const poolIsGif = gifs.length > 0;
    const pool = uniq(poolIsGif ? gifs : imgs);

    ensureRegenCss();

    // ---- build the card OFF the live stage; only attach on success ----------
    const built = chrome.frame({
      instruction: TARGET_INSTR,
      band: ctx.band,
      sub: (band === 'deepening' || band === 'climax') ? S.footnote : undefined,
      hatch: S.stop,                 // the honest "you stop" control (graded)
      verifyLabel: 'VERIFY',
    });
    if (!built || !built.root || !built.body) return false;

    const shell = chrome.gridShell(9, 3);
    if (!shell || !shell.grid || !shell.tiles || shell.tiles.length !== 9) return false;
    const tiles = shell.tiles;
    for (let i = 0; i < 9; i++) tiles[i].el.classList.add('ixregen-tile');
    built.body.appendChild(shell.grid);

    let loopBanner = null;   // pool-dry banner (in-body), lazily created

    // ---- per-tile ledger + node ownership -----------------------------------
    // We own our OWN media nodes (img/canvas) inside tile.el so a slot can be
    // cleared and refilled cleanly (chrome's tile.setImage reuses one <img>,
    // which does not survive our clear/refill churn).
    const meta = [];
    for (let i = 0; i < 9; i++) meta.push({ filled: false, kind: 'empty', src: null, live: false, node: null });

    let done = false;
    let liveCount = 0;
    let clicks = 0;             // tiles the user cleared
    let spawns = 0;             // replacement tiles produced
    let futilityFired = false;
    let futilityAtClicks = 0;
    let spawnCursor = 0;        // index into the user pool (for the loop callback)
    let poolLooped = false;
    let stopShown = false;
    let verifyAttempts = 0;
    let autoTimer = null;
    let swarmAnnounced = false;

    const timers = [];
    function push(t) { timers.push(t); return t; }
    function clearTimers() {
      for (const t of timers) { try { clearTimeout(t); } catch (_e) {} }
      timers.length = 0;
      if (autoTimer) { try { clearInterval(autoTimer); } catch (_e) {} autoTimer = null; }
    }
    // persistent body scrim lives outside stage.innerHTML — remove on teardown.
    function removeScrim() {
      try { const sc = document.getElementById('ix-regen-scrim'); if (sc && sc.parentNode) sc.parentNode.removeChild(sc); } catch (_e) {}
    }
    if (typeof ctx.onCleanup === 'function') ctx.onCleanup(() => { clearTimers(); removeScrim(); });

    /* -- node helpers (own the tile's inner media node) --------------------- */
    function clearContent(i) {
      const m = meta[i];
      if (m.live) { liveCount = Math.max(0, liveCount - 1); m.live = false; }
      if (m.node && m.node.parentNode) { try { m.node.parentNode.removeChild(m.node); } catch (_e) {} }
      m.node = null; m.filled = false; m.kind = 'empty'; m.src = null;
      try { tiles[i].select(false); } catch (_e) {}
    }
    function putNode(i, node) {
      clearContent(i);
      try { tiles[i].el.insertBefore(node, tiles[i].el.firstChild); } catch (_e) { try { tiles[i].el.appendChild(node); } catch (_e2) {} }
      meta[i].node = node;
      meta[i].filled = true;
    }
    function makeImgNode(src, live) {
      const im = document.createElement('img');
      im.className = 'ixcap-tileimg ixregen-media';
      im.alt = ''; im.draggable = false;
      try { im.src = String(src); } catch (_e) {}
      return im;
    }
    // canvas first-frame freeze (intentional fiction) — draws the asset once.
    function makeFrozenNode(url) {
      const cv = document.createElement('canvas');
      cv.className = 'ixcap-tileimg ixregen-media';
      cv.width = 120; cv.height = 120;
      try {
        if (typeof Image !== 'function') return cv;
        const im = new Image();
        try { im.crossOrigin = 'anonymous'; } catch (_e) {}
        im.onload = () => {
          if (done) return;
          try {
            const c = cv.getContext ? cv.getContext('2d') : null;
            if (!c) return;
            const iw = im.naturalWidth || im.width || 120;
            const ih = im.naturalHeight || im.height || 120;
            const sc = Math.max(120 / iw, 120 / ih);
            const w = iw * sc, h = ih * sc;
            c.drawImage(im, (120 - w) / 2, (120 - h) / 2, w, h);
          } catch (_e) {}
        };
        im.onerror = () => {};
        im.src = url;
      } catch (_e) {}
      return cv;
    }

    /* -- content picking ---------------------------------------------------- */
    function nextPoolUrl() {
      if (!pool.length) return null;
      const url = pool[spawnCursor % pool.length];
      spawnCursor++;
      if (!poolLooped && spawnCursor >= pool.length && pool.length > 0) {
        poolLooped = true;
        showLoopBanner();
      }
      return url;
    }
    function mundaneHydrantSrc(seed) {
      try {
        if (typeof chrome.mundaneTileSrc === 'function') return chrome.mundaneTileSrc('hydrant', ((seed | 0) % 8) + 1);
        if (typeof chrome.placeholderTile === 'function') return chrome.placeholderTile('hydrant', seed | 0);
      } catch (_e) {}
      return null;
    }

    function maybeFutility() {
      if (!futilityFired && spawns >= FUTILITY_SPAWNS) {
        futilityFired = true;
        futilityAtClicks = clicks;
      }
    }

    // Fill slot i with a fresh tile (mundane hydrant OR user media, frozen->live).
    function fillSlot(i, forceUser) {
      if (done) return;
      spawns++;
      maybeFutility();
      const useUser = pool.length > 0 && (forceUser || Math.random() < shape.userChance);
      if (!useUser) {
        const src = mundaneHydrantSrc(spawns + i);
        if (src) putNode(i, makeImgNode(src, false));
        else { // no canvas/asset support: leave a plainly-filled slot so it is gradable
          const blank = document.createElement('div');
          blank.className = 'ixcap-tileimg ixregen-media';
          blank.style.background = '#e6e2d6';
          putNode(i, blank);
        }
        meta[i].kind = 'mundane';
        settleIn(i, false);
        return;
      }
      const url = nextPoolUrl();
      meta[i].src = url;
      meta[i].kind = 'gif';
      const canGoLive = poolIsGif && !reduced && liveCount < MAX_LIVE;
      if (!canGoLive) {
        putNode(i, makeFrozenNode(url));   // over cap / reduced / still images -> frozen
        settleIn(i, false);
      } else {
        // arrive FROZEN, animate on settle (whole <img> live once the slot settles)
        putNode(i, makeFrozenNode(url));
        settleIn(i, false);
        push(setTimeout(() => {
          if (done || !meta[i].filled || meta[i].src !== url) return;
          if (liveCount >= MAX_LIVE) return;   // cap may have filled while we waited
          putNode(i, makeImgNode(url, true));
          meta[i].kind = 'gif'; meta[i].src = url; meta[i].live = true; liveCount++;
          settleIn(i, false);
          try { ctx.sfx('grid-settle', 0.2); } catch (_e) {}
        }, SETTLE_MS));
      }
    }

    function settleIn(i, silent) {
      const el = tiles[i].el;
      try {
        el.classList.remove('ixregen-clearing');
        if (reduced) { el.classList.add('ixregen-settled'); return; }
        el.classList.add('ixregen-settling');
        push(setTimeout(() => {
          if (done) return;
          try { el.classList.remove('ixregen-settling'); el.classList.add('ixregen-settled'); } catch (_e) {}
        }, 20));
      } catch (_e) {}
      if (!silent) { try { ctx.sfx('grid-tile-flicker', 0.18); } catch (_e) {} }
    }

    function showLoopBanner() {
      if (loopBanner || done) return;
      try {
        loopBanner = document.createElement('div');
        loopBanner.className = 'ixregen-loopbanner';
        loopBanner.textContent = S.poolLoop;
        built.body.appendChild(loopBanner);
        ctx.sfx('regen-loop', 0.32);
      } catch (_e) {}
    }

    function maybeStopLooking() {
      if (stopShown || band !== 'climax') return;
      const past = clicks - futilityAtClicks;
      if (!(futilityFired && past >= STOP_THRESHOLD)) return;
      stopShown = true;
      try {
        const sc = document.createElement('div');
        sc.id = 'ix-regen-scrim';
        const tx = document.createElement('div');
        tx.className = 'ixregen-scrimtext';
        tx.textContent = S.stopLooking;
        sc.appendChild(tx);
        document.body.appendChild(sc);
        // next frame -> fade in (or instant under reduced)
        push(setTimeout(() => { try { sc.classList.add('ixregen-scrim-on'); } catch (_e) {} }, 20));
        ctx.sfx('chime', 0.22);
      } catch (_e) {}
    }

    /* -- initial layout ----------------------------------------------------- */
    function seedInitial() {
      if (band === 'recovery') {
        // a single hydrant in one slot; the rest stay empty.
        const slot = (Math.random() * 9) | 0;
        const src = mundaneHydrantSrc(1);
        if (src) putNode(slot, makeImgNode(src, false));
        else { const b = document.createElement('div'); b.className = 'ixcap-tileimg ixregen-media'; b.style.background = '#e6e2d6'; putNode(slot, b); }
        meta[slot].kind = 'target';
        settleIn(slot, true);
        return;
      }
      if (band === 'climax' && pool.length) {
        for (let i = 0; i < 9; i++) fillSlot(i, true);   // all user media
        try { ctx.sfx('grid-tile-flicker', 0.3); } catch (_e) {}
        return;
      }
      // calibration / establishing / deepening: start as an honest hydrant grid.
      for (let i = 0; i < 9; i++) {
        const src = mundaneHydrantSrc(i + 1);
        if (src) putNode(i, makeImgNode(src, false));
        else { const b = document.createElement('div'); b.className = 'ixcap-tileimg ixregen-media'; b.style.background = '#e6e2d6'; putNode(i, b); }
        meta[i].kind = 'target';
      }
      // deepening seeds 1-2 sticky user-media tiles up front (the rot creeping in).
      if (band === 'deepening' && pool.length) {
        const picks = shuffleIdx(9).slice(0, 2);
        picks.forEach((idx, k) => {
          push(setTimeout(() => { if (!done) fillSlot(idx, true); }, 700 + k * 650));
        });
      }
    }

    function countFilled() { let n = 0; for (let i = 0; i < 9; i++) if (meta[i].filled) n++; return n; }

    /* -- click a tile: clear it, maybe refill --------------------------------*/
    function onTileClick(i) {
      if (done || !meta[i].filled) return;
      clicks++;
      try { ctx.sfx('verify-tick', 0.28); } catch (_e) {}
      const el = tiles[i].el;
      try { el.classList.add('ixregen-clearing'); el.classList.remove('ixregen-settled'); } catch (_e) {}
      const willRefill = shape.replaceChance > 0 && Math.random() < shape.replaceChance;
      push(setTimeout(() => {
        if (done) return;
        clearContent(i);
        if (willRefill) fillSlot(i, false);
      }, reduced ? 0 : CLEAR_MS));

      maybeFutility();
      maybeStopLooking();

      // recovery / calibration can EMPTY -> honest completion.
      if ((band === 'recovery' || band === 'calibration') && shape.replaceChance === 0) {
        // defer the empty check until after this clear resolves.
        push(setTimeout(() => {
          if (done) return;
          if (countFilled() === 0) commit(deriveBucket());
        }, (reduced ? 0 : CLEAR_MS) + 30));
      }
    }

    /* -- climax auto-spawn: incoming faster than clicking --------------------*/
    function startAuto() {
      if (!shape.auto || reduced || done) return;
      try {
        autoTimer = setInterval(() => {
          if (done) { if (autoTimer) { clearInterval(autoTimer); autoTimer = null; } return; }
          // refill up to two empty slots each tick (outpaces one click / clear).
          let filled = 0;
          const order = shuffleIdx(9);
          for (const i of order) {
            if (filled >= 2) break;
            if (!meta[i].filled) { fillSlot(i, true); filled++; }
          }
          if (!swarmAnnounced && spawns >= 3) { swarmAnnounced = true; try { ctx.sfx('regen-swarm', 0.3); } catch (_e) {} }
        }, AUTO_MS);
      } catch (_e) {}
    }

    /* -- grading: click ledger -> option index ------------------------------ */
    function deriveBucket() {
      if (clicks === 0) return 0;               // refusal / outright stop
      if (!futilityFired) return 1;             // never hit the wall (calibration / stopped early)
      const past = clicks - futilityAtClicks;
      if (past >= ENDURE_HI) return 3;          // kept clicking own files well past futility
      if (past >= 1) return 2;                  // persisted a little past futility
      return 1;                                 // stopped right at the wall
    }

    /* -- commit path (guaranteed, single submit) ---------------------------- */
    function verdictFor(bucket) {
      if (bucket === 0) return { text: S.refuseVerdict, tone: 'flag' };
      if (band === 'recovery') return { text: S.recoverVerdict, tone: 'ok' };
      if (band === 'climax') return { text: S.climaxVerdict, tone: 'flag' };
      if (band === 'establishing' || band === 'deepening') return { text: S.logged, tone: 'logged' };
      return { text: S.calVerdict, tone: 'ok' };  // calibration: quiet
    }
    function commit(bucket) {
      if (done) return;
      done = true;
      clearTimers();
      removeScrim();
      const v = verdictFor(bucket);
      try { const st = chrome.stamp(v.text, v.tone); if (st) built.body.appendChild(st); } catch (_e) {}
      try {
        if (bucket === 0) ctx.sfx('captcha-reject', 0.4);
        else if (band === 'recovery' || band === 'calibration') ctx.sfx('captcha-verify-ok', 0.42);
        else ctx.sfx('captcha-logged', 0.42);
        ctx.sfx('grid-verify-stamp', 0.4);
      } catch (_e) {}
      push(setTimeout(() => {
        try { ctx.submitIndex(bucket); } catch (e) { ilog('submitIndex failed: ' + (e && e.message)); }
      }, 620));
    }

    /* -- VERIFY: friction, not lockout. 3rd attempt accepts. ---------------- */
    function onVerify() {
      if (done) return;
      const remaining = countFilled();
      if (remaining === 0) { commit(deriveBucket()); return; }   // honest completion
      verifyAttempts++;
      if (verifyAttempts >= 3) { commit(deriveBucket()); return; }
      // graded nag — the grid still will not empty, but nothing is locked.
      try {
        const st = chrome.stamp(clicks === 0 ? S.refuseVerdict : S.remain(remaining), 'logged');
        if (st) built.body.appendChild(st);
        push(setTimeout(() => { try { if (st && st.parentNode) st.parentNode.removeChild(st); } catch (_e) {} }, 1400));
      } catch (_e) {}
      try { ctx.sfx('captcha-reject', 0.4); } catch (_e) {}
    }

    // ---- wire tiles + controls ---------------------------------------------
    for (let i = 0; i < 9; i++) {
      (function (idx) { tiles[idx].el.addEventListener('click', () => onTileClick(idx)); })(i);
    }
    if (built.verifyBtn) built.verifyBtn.addEventListener('click', onVerify);
    // "stop looking" — the honest exit; always commits the derived bucket.
    if (built.hatchLink) built.hatchLink.addEventListener('click', () => commit(deriveBucket()));

    // ---- timeout (renderer owns its clock) ----------------------------------
    const timeoutMs = (typeof ctx.timeoutMs === 'number' && ctx.timeoutMs > 0) ? ctx.timeoutMs : 0;
    if (timeoutMs) {
      push(setTimeout(() => { if (!done) { try { ctx.submitTimeout(); done = true; clearTimers(); removeScrim(); } catch (e) { ilog('submitTimeout failed: ' + (e && e.message)); } } }, timeoutMs));
    }

    // ---- seed + speak + attach ---------------------------------------------
    seedInitial();
    startAuto();
    try { if (typeof ctx.speakPrompt === 'function') ctx.speakPrompt(); } catch (_e) {}

    ctx.root.appendChild(built.root);
    return true;
  } catch (e) {
    ilog('render threw: ' + (e && e.message));
    return false;   // partial builds never reached ctx.root -> clean fall-back
  }
}

export default { render };
