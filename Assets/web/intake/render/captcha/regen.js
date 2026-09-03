/* ============================================================================
 * render/captcha/regen.js — VerifyRegen ("The Regenerating Grid")   [Next-tier]
 *
 * CAPTCHA_BRAINSTORM.md SYNTHESIS #6. The gaslighting captcha everyone has lived
 * ("select all until none remain"), made honest about it. A VeriTru grid whose
 * cleared tiles are refilled band by band from the user's own library, while the
 * printed instruction never changes.
 *
 * OWNER RESHAPE (2026-07-24): "it's a normal captcha the first 2 levels" and the
 * replacements "should have a % chance to spawn, not always." Band map:
 *   1 Calibration · 2 Establishing · 3 Deepening · 4 Climax · 5 Recovery.
 *
 * The 5-band ladder AFTER the reshape:
 *
 *   Calibration  (lv1) COMPLETELY NORMAL captcha. A straight reCAPTCHA grid of
 *                mundane tiles (hydrant/bus/crosswalk/stapler via the chrome
 *                mundaneTileSrc seam). Click to select, VERIFY to commit. NO user
 *                media, NO regeneration, NO auto-spawn, NO scrim. Deadpan. This is
 *                the trust that is spent later.
 *   Establishing (lv2) ALSO a completely normal captcha, identical mechanics to
 *                Calibration. Verdict stays deadpan ("response logged").
 *   Deepening    (lv3) the regenerating mechanic switches ON. A clicked tile
 *                CLEARS and (per replaceChance) refills; the refill is a user gif
 *                only with GIF_SPAWN_DEEP probability — otherwise another mundane
 *                tile. Subinstruction footnote "*new images will continue".
 *   Climax       (lv4) same regenerating mechanic + an auto-spawn timer that
 *                refills empty slots. EVERY spawn (click-refill AND auto) rolls
 *                GIF_SPAWN_CLIMAX for gif-vs-mundane. Past a futility threshold a
 *                LIGHT chrome scrim (our own, NOT the engine freeze-gate) fades in
 *                "none remain when you stop looking. stop looking." Pool cycle ->
 *                "end of library reached. looping." Grade = clicks past futility.
 *   Recovery     (lv5) one hydrant, one slot; one click clears it; none remain;
 *                stamp "none remain. thank you." (requires band=recovery, heat 0).
 *
 * GRADING (byte-compatible with the pre-reshape contract) — the beat carries a
 * normal options[] (verdict-flavored labels, NEVER shown as buttons). We derive a
 * scalar from the ledger and map it to ONE option index, then ctx.submitIndex(i):
 *   0  refusal        (nothing selected / cleared — stopped or refused outright)
 *   1  literal        (normal-band selection; or cleared without pushing past the
 *                      futility wall)
 *   2  persisted      (some clicks past the point replacements outran clearing)
 *   3  full endorse   (kept clicking their own files well past futility)
 * The bank's per-band `answer` picks which bucket reads as compliant (correct):
 *   cal/est -> 1, deep -> 2, climax -> 3, recovery -> 1. Refusal (0) is always a
 * first-class graded answer; prompt tags vote the archetype axes either way.
 *
 * INVARIANTS (CLAUDE.md §10 / CAPTCHA_HANDOFF.md §4): nothing throws at import;
 * only touch ctx.root once committed to returning true; captcha chrome IS the
 * friction so ctx.installSteering is skipped; VERIFY (3rd attempt accepts) and the
 * "stop looking" control both commit (no lockout); ctx.forceComplete is honored by
 * the `done` guard + onCleanup; synthetic clicks (`!e.isTrusted`) pass straight
 * through (no tile handler vetoes); ctx.reduced kills the auto-spawn storm + settle
 * churn (static frozen replacements); the "stop looking" moment is our OWN light
 * scrim, our ONE corruption treatment for Climax (we never also melt/scramble/
 * freeze-gate) and it fires ONLY at Climax — the normal bands never scrim; gif
 * tiles are whole <img>s in overflow:hidden crops, DISTINCT animated capped at
 * MAX_LIVE_GIFS (grid.js's distinct-src approach) and when at cap we regenerate
 * MUNDANE instead; incoming gifs arrive canvas-frozen and go live on settle;
 * `is-correct`/`is-answer` never appear (selection uses chrome's own nodes); no
 * audio handle held (ctx.sfx seam, existing ids only); Real filenames appear
 * NOWHERE (VerifyCustody's exclusive).
 * ==========================================================================*/

/* ----------------------------------------------------------------------------
 * TUNABLES (top-of-file, clearly named). GIF_SPAWN_* is the owner's "% chance to
 * spawn, not always": the probability that a REGENERATED / AUTO-SPAWNED tile is a
 * user gif rather than another mundane tile. Only Deepening (lv3) onward spawn any
 * user media at all; Calibration/Establishing (lv1-2) are chance 0 (pure captcha).
 * -------------------------------------------------------------------------- */
const GIF_SPAWN_DEEP = 0.35;    // Deepening: p(a refill/spawn is a user gif)
const GIF_SPAWN_CLIMAX = 0.6;   // Climax:    p(a refill/spawn is a user gif)

const MAX_LIVE_GIFS = 3;        // concurrent DISTINCT animated user gifs cap (3-4 rule)
const SETTLE_MS = 190;          // frozen-in tile goes live after this
const CLEAR_MS = 240;           // clear fade before a refill lands
const AUTO_MS = 740;            // climax auto-spawn cadence
const FUTILITY_SPAWNS = 5;      // replacements before the wall is "obvious"
const STOP_THRESHOLD = 6;       // clicks-past-futility that triggers the scrim
const ENDURE_HI = 8;            // clicks-past-futility for the full-endorse bucket

/* Mundane tile vocabulary. Every one of these flows through the chrome
 * mundaneTileSrc(kind, slot) seam (slot 1..9) so tiles pick up the real PNG art
 * the moment assets/captcha/ lands, with the canvas placeholder until then. */
const MUNDANE_KINDS = ['hydrant', 'bus', 'crosswalk', 'stapler'];

/* ----------------------------------------------------------------------------
 * TARGET CATEGORY — varies per render (was hard-fixed to "fire hydrant"). ONE
 * source of truth: the chosen key drives (a) the header instruction, (b) which
 * tiles seed as the target (the answer key), and (c) the negative-link text, so a
 * varied category can never desync from the art or the grading. Keys are exactly
 * the chrome.mundaneTileSrc kinds (MUNDANE_KINDS).
 * -------------------------------------------------------------------------- */
const CATEGORIES = Object.freeze({
  hydrant:   { instr: 'a fire hydrant', plural: 'fire hydrants' },
  bus:       { instr: 'a bus',          plural: 'buses' },
  crosswalk: { instr: 'a crosswalk',    plural: 'crosswalks' },
  stapler:   { instr: 'a stapler',      plural: 'staplers' },
});
function pickCategory() { return MUNDANE_KINDS[(Math.random() * MUNDANE_KINDS.length) | 0]; }
function instrFor(catKey) {
  const c = CATEGORIES[catKey] || CATEGORIES.hydrant;
  return 'Select all images with ' + c.instr;
}

/* ----------------------------------------------------------------------------
 * NICHE STRING TABLE — all niche-flavored VERDICT/UI strings live HERE (bank text
 * is niche-agnostic shared prompts). Complete defaults for all four niches.
 * circe register is a hard rule: no exclamation marks, no diminutives, no em-dashes.
 * -------------------------------------------------------------------------- */
const STRINGS = Object.freeze({
  bambi: {
    refuse: (p) => 'no ' + p + ' here',
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
    refuse: (p) => 'no ' + p + ' to report',
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
    refuse: (p) => 'no ' + p + ', promise',
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
    refuse: (p) => 'no ' + p + ' reported',
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

/* per-band content shaping.
 *   mode          'normal'  -> straight select-and-verify captcha (lv1-2)
 *                 'regen'   -> the regenerating-tiles mechanic (lv3-4)
 *                 'recovery'-> one honest hydrant, clears to completion (lv5)
 *   replaceChance p(a cleared slot refills on click) — regen bands only.
 *   gifChance     p(a refill/auto-spawn is a user gif vs a mundane tile) — the
 *                 owner's "% chance to spawn, not always" gate.
 *   auto          climax auto-spawn timer on/off.
 *   scrim         the "stop looking" light scrim eligibility (climax only). */
const BAND_SHAPE = Object.freeze({
  calibration:  { mode: 'normal',   replaceChance: 0.00, gifChance: 0.00,             auto: false, scrim: false },
  establishing: { mode: 'normal',   replaceChance: 0.00, gifChance: 0.00,             auto: false, scrim: false },
  deepening:    { mode: 'regen',    replaceChance: 0.82, gifChance: GIF_SPAWN_DEEP,   auto: false, scrim: false },
  climax:       { mode: 'regen',    replaceChance: 1.00, gifChance: GIF_SPAWN_CLIMAX, auto: true,  scrim: true  },
  recovery:     { mode: 'recovery', replaceChance: 0.00, gifChance: 0.00,             auto: false, scrim: false },
});

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
    const mode = shape.mode;                    // 'normal' | 'regen' | 'recovery'
    // ONE source of truth for the varied target category (header + tile art + refuse link).
    const targetKey = pickCategory();
    const catPlural = (CATEGORIES[targetKey] || CATEGORIES.hydrant).plural;
    const others = MUNDANE_KINDS.filter((k) => k !== targetKey);

    // media pool — user gifs preferred, images as fallback. Distinct list.
    const gifs = (ctx.media && Array.isArray(ctx.media.gifs)) ? ctx.media.gifs : [];
    const imgs = (ctx.media && Array.isArray(ctx.media.images)) ? ctx.media.images : [];
    const poolIsGif = gifs.length > 0;
    const pool = uniq(poolIsGif ? gifs : imgs);

    ensureRegenCss();

    // ---- build the card OFF the live stage; only attach on success ----------
    const built = chrome.frame({
      instruction: instrFor(targetKey),
      band: ctx.band,
      // the footnote is deep+ only — normal bands stay pristine.
      sub: (mode === 'regen') ? S.footnote : undefined,
      // normal / recovery: a plain graded refusal control. regen: the honest
      // "stop looking" exit (which commits the derived bucket, not always refusal).
      hatch: (mode === 'regen') ? S.stop : S.refuse(catPlural),   // refuse link is category-synced
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
    // which does not survive our clear/refill churn). Normal-mode tiles skip node
    // ownership entirely and ride chrome's setImage + select() (a plain captcha).
    const meta = [];
    for (let i = 0; i < 9; i++) meta.push({ filled: false, kind: 'empty', src: null, live: false, node: null, selected: false });

    let done = false;
    let clicks = 0;             // tiles the user cleared (regen/recovery)
    let spawns = 0;             // replacement tiles produced (regen)
    let futilityFired = false;
    let futilityAtClicks = 0;
    let spawnCursor = 0;        // index into the user pool (for the loop callback)
    let poolLooped = false;
    let stopShown = false;
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

    /* -- concurrent-animated-gif cap (grid.js's distinct-src approach) -------- */
    // A src already live shares its decode (free); a NEW distinct src is only
    // allowed while under MAX_LIVE_GIFS. Frozen/static tiles never count.
    function canAddLiveGif(url) {
      if (!url) return false;
      const s = new Set();
      for (let i = 0; i < 9; i++) if (meta[i].live && meta[i].src) s.add(meta[i].src);
      if (s.has(url)) return true;
      return s.size < MAX_LIVE_GIFS;
    }

    /* -- node helpers (own the tile's inner media node) --------------------- */
    function clearContent(i) {
      const m = meta[i];
      m.live = false;
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
    function makeImgNode(src) {
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
    function makeBlankNode() {
      const blank = document.createElement('div');
      blank.className = 'ixcap-tileimg ixregen-media';
      blank.style.background = '#e6e2d6';
      return blank;
    }

    /* -- mundane tile source through the chrome seam (slot 1..9) ------------- */
    function mundaneSrc(kind, slot) {
      const k = MUNDANE_KINDS.indexOf(kind) >= 0 ? kind : 'hydrant';
      const s = ((slot | 0) % 9); const idx = (s <= 0 ? 9 : s);   // 1..9
      try {
        if (typeof chrome.mundaneTileSrc === 'function') return chrome.mundaneTileSrc(k, idx);
        if (typeof chrome.placeholderTile === 'function') return chrome.placeholderTile(k, idx);
      } catch (_e) {}
      return null;
    }

    /* -- content picking ---------------------------------------------------- */
    function peekPoolUrl() { return pool.length ? pool[spawnCursor % pool.length] : null; }
    function consumePoolUrl() {
      const url = peekPoolUrl();
      spawnCursor++;
      if (!poolLooped && pool.length > 0 && spawnCursor >= pool.length) { poolLooped = true; showLoopBanner(); }
      return url;
    }

    function maybeFutility() {
      if (!futilityFired && spawns >= FUTILITY_SPAWNS) {
        futilityFired = true;
        futilityAtClicks = clicks;
      }
    }

    // Place a mundane tile in a slot (node-owned). settle=true => pop-in animation.
    function putMundane(i, slot, settle) {
      const kind = MUNDANE_KINDS[(i + slot) % MUNDANE_KINDS.length];
      const src = mundaneSrc(kind, slot);
      putNode(i, src ? makeImgNode(src) : makeBlankNode());
      meta[i].kind = 'mundane';
      if (settle) settleIn(i, false);
    }

    // Place a user tile (gif/image). Animates only when it will not exceed the cap.
    function putUser(i, url) {
      meta[i].src = url;
      meta[i].kind = 'gif';
      const animate = poolIsGif && !reduced && canAddLiveGif(url);
      if (!animate) {
        // still image, reduced motion, or over cap -> static: frozen canvas for a
        // gif so it truly does not animate; a plain <img> for a static image.
        putNode(i, poolIsGif ? makeFrozenNode(url) : makeImgNode(url));
        settleIn(i, false);
        return;
      }
      // arrive FROZEN, animate on settle (whole <img> live once the slot settles).
      putNode(i, makeFrozenNode(url));
      settleIn(i, false);
      push(setTimeout(() => {
        if (done || !meta[i].filled || meta[i].src !== url) return;
        if (!canAddLiveGif(url)) return;   // cap filled while we waited -> stay frozen
        putNode(i, makeImgNode(url));
        meta[i].kind = 'gif'; meta[i].src = url; meta[i].live = true;
        settleIn(i, true);
        try { ctx.sfx('grid-settle', 0.2); } catch (_e) {}
      }, SETTLE_MS));
    }

    // Fill slot i with a fresh regenerated tile. Rolls the gif-vs-mundane gate
    // (the owner's "% chance to spawn, not always"); at the animated cap it spawns
    // mundane instead. spawns/futility bookkeeping lives here (regen bands only).
    function fillSlot(i) {
      if (done) return;
      spawns++;
      maybeFutility();
      let goGif = pool.length > 0 && shape.gifChance > 0 && Math.random() < shape.gifChance;
      if (goGif && poolIsGif && !reduced) {
        // a live/animated gif would count against the cap; at cap -> mundane.
        if (!canAddLiveGif(peekPoolUrl())) goGif = false;
      }
      if (goGif) {
        putUser(i, consumePoolUrl());
      } else {
        putMundane(i, i + 1, true);
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
      if (stopShown || !shape.scrim) return;
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
    // NORMAL (lv1-2): a straight captcha via chrome's own setImage + select().
    function seedNormalGrid() {
      const order = shuffleIdx(9);
      const targetSlots = new Set(order.slice(0, 3));
      for (let i = 0; i < 9; i++) {
        const kind = targetSlots.has(i) ? targetKey : others[i % others.length];
        const src = mundaneSrc(kind, i + 1);
        if (src) { try { tiles[i].setImage(src); } catch (_e) {} }
        meta[i].kind = targetSlots.has(i) ? 'target' : 'mundane';
        meta[i].filled = true;
      }
    }
    // REGEN / RECOVERY: node-owned tiles so slots can clear + refill.
    function seedInitial() {
      if (mode === 'recovery') {
        // a single target tile in one slot; the rest stay empty.
        const slot = (Math.random() * 9) | 0;
        const src = mundaneSrc(targetKey, 1);
        putNode(slot, src ? makeImgNode(src) : makeBlankNode());
        meta[slot].kind = 'target';
        return;
      }
      // deepening / climax START honest: a full mundane grid. The rot arrives only
      // through the gated regeneration (and, at climax, the auto-spawn timer).
      const order = shuffleIdx(9);
      const targetSlots = new Set(order.slice(0, 3));
      for (let i = 0; i < 9; i++) {
        const kind = targetSlots.has(i) ? targetKey : others[i % others.length];
        const src = mundaneSrc(kind, i + 1);
        putNode(i, src ? makeImgNode(src) : makeBlankNode());
        meta[i].kind = targetSlots.has(i) ? 'target' : 'mundane';
      }
    }

    function countFilled() { let n = 0; for (let i = 0; i < 9; i++) if (meta[i].filled) n++; return n; }
    function countSelected() { let n = 0; for (let i = 0; i < 9; i++) if (meta[i].selected) n++; return n; }

    /* -- NORMAL band click: toggle selection (plain captcha) ------------------*/
    function onNormalClick(idx) {
      if (done) return;
      const nowSel = !tiles[idx].isSelected();
      try { tiles[idx].select(nowSel); } catch (_e) {}
      meta[idx].selected = nowSel;
      // no set-pieces, no clearing — synthetic clicks pass straight through here.
    }

    /* -- REGEN / RECOVERY click: clear it, maybe refill --------------------- */
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
        if (willRefill) fillSlot(i);
      }, reduced ? 0 : CLEAR_MS));

      maybeFutility();
      maybeStopLooking();

      // recovery clears to honest completion (no refill).
      if (mode === 'recovery') {
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
          // Each spawn rolls the SAME gif gate inside fillSlot.
          let filled = 0;
          const order = shuffleIdx(9);
          for (const i of order) {
            if (filled >= 2) break;
            if (!meta[i].filled) { fillSlot(i); filled++; }
          }
          if (!swarmAnnounced && spawns >= 3) { swarmAnnounced = true; try { ctx.sfx('regen-swarm', 0.3); } catch (_e) {} }
        }, AUTO_MS);
      } catch (_e) {}
    }

    /* -- grading: ledger -> option index (byte-compatible buckets) ----------- */
    function deriveBucket() {
      if (mode === 'normal') {
        return countSelected() === 0 ? 0 : 1;   // refusal vs literal compliance
      }
      if (mode === 'recovery') {
        return clicks === 0 ? 0 : 1;             // refused vs cleared the one tile
      }
      // regen (deepening / climax)
      if (clicks === 0) return 0;                // refusal / outright stop
      if (!futilityFired) return 1;              // never hit the wall
      const past = clicks - futilityAtClicks;
      if (past >= ENDURE_HI) return 3;           // kept clicking own files past futility
      if (past >= 1) return 2;                   // persisted a little past futility
      return 1;                                  // stopped right at the wall
    }

    /* -- commit path (guaranteed, single submit) ---------------------------- */
    function verdictFor(bucket) {
      if (bucket === 0) return { text: S.refuseVerdict, tone: 'flag' };
      if (mode === 'recovery') return { text: S.recoverVerdict, tone: 'ok' };
      if (band === 'climax') return { text: S.climaxVerdict, tone: 'flag' };
      if (band === 'deepening') return { text: S.logged, tone: 'logged' };
      return { text: S.calVerdict, tone: 'ok' };   // calibration + establishing: deadpan
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
        else if (band === 'deepening' || band === 'climax') ctx.sfx('captcha-logged', 0.42);
        else ctx.sfx('captcha-verify-ok', 0.42);   // calibration / establishing / recovery
        ctx.sfx('grid-verify-stamp', 0.4);
      } catch (_e) {}
      push(setTimeout(() => {
        try { ctx.submitIndex(bucket); } catch (e) { ilog('submitIndex failed: ' + (e && e.message)); }
      }, 620));
    }

    /* -- VERIFY: friction, not lockout. Always commits on the FIRST press. ---
     * In EVERY mode, VERIFY straight-commits the derived grade. Previously the
     * regen bands (deepening/climax) required the grid to be EMPTIED first
     * (countFilled()===0) and otherwise nagged "N items remain. continue.",
     * accepting only on the 3rd press. But the refill mechanic (replaceChance
     * 0.82/1.00) keeps the grid full, so it never empties — which read as the
     * captcha refusing to submit after the user selected the target tiles. The
     * gaslighting now lives purely in the click/regeneration loop (which still
     * runs); VERIFY is the always-open honest exit, and deriveBucket already
     * grades how far the user pushed into the loop. No fixed-count gate. */
    function onVerify() {
      if (done) return;
      commit(deriveBucket());
    }

    // ---- wire tiles + controls ---------------------------------------------
    for (let i = 0; i < 9; i++) {
      (function (idx) {
        tiles[idx].el.addEventListener('click', () => (mode === 'normal' ? onNormalClick(idx) : onTileClick(idx)));
      })(i);
    }
    if (built.verifyBtn) built.verifyBtn.addEventListener('click', onVerify);
    // hatch: normal/recovery -> a plain graded refusal (bucket 0); regen -> the
    // honest "stop looking" exit, which commits the derived bucket (never locks).
    if (built.hatchLink) {
      built.hatchLink.addEventListener('click', () => commit(mode === 'regen' ? deriveBucket() : 0));
    }

    // ---- timeout (renderer owns its clock) ----------------------------------
    const timeoutMs = (typeof ctx.timeoutMs === 'number' && ctx.timeoutMs > 0) ? ctx.timeoutMs : 0;
    if (timeoutMs) {
      push(setTimeout(() => { if (!done) { try { ctx.submitTimeout(); done = true; clearTimers(); removeScrim(); } catch (e) { ilog('submitTimeout failed: ' + (e && e.message)); } } }, timeoutMs));
    }

    // ---- seed + speak + attach ---------------------------------------------
    if (mode === 'normal') { seedNormalGrid(); }
    else { seedInitial(); startAuto(); }
    try { if (typeof ctx.speakPrompt === 'function') ctx.speakPrompt(); } catch (_e) {}

    ctx.root.appendChild(built.root);
    return true;
  } catch (e) {
    ilog('render threw: ' + (e && e.message));
    return false;   // partial builds never reached ctx.root -> clean fall-back
  }
}

export default { render };
