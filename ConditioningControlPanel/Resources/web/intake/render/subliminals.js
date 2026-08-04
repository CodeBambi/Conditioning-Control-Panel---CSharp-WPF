/* ============================================================================
 * subliminals.js — the user's subliminal pool, replicated in-page.
 *
 * Mirrors the WPF SubliminalService flash for the "Graded Intake" web core: at
 * random intervals one ENABLED phrase from the user's pool flashes large + bold
 * in an off-centre position, faint, fading in and out. When the phrase has a
 * CONNECTED whisper clip (delivered as a data: URI on config.subliminals[i].audio
 * by IntakeHostService) it plays at the same moment, at a modest volume.
 *
 * This module OWNS its own rendering and its own audio path (a single AudioContext
 * + one gain node kept ~0.5, buffers decoded lazily and kept in a tiny LRU). It
 * does NOT touch render/audio.js — if limiter sharing ever matters we consolidate
 * then. Distinct from effects.js's reward "garnish" word flashes: those are a
 * reward-paired camouflage layer; this is the standing subliminal bed.
 *
 * CONTRACT (mirrors the other render factories):
 *   createSubliminals({ pool, caps?, theme? }) -> {
 *     setDepth(depth),   // 0..1 — tightens cadence, raises opacity + font size
 *     dispose(),         // stop the loop, remove the layer, release audio (idempotent)
 *   }
 *   `pool`: Array<{ text:string, audio?:string }> — already the ENABLED phrases.
 *
 * PAUSE: skips scheduling a flash while a jumpscare freeze (body.ix-freeze) or a
 * briefing / section interstitial (#intake-overlay visible) is up — it reschedules
 * a short beat later instead, so the bed resumes cleanly when the overlay lifts.
 *
 * IMPORTS ARE SIDE-EFFECT FREE: no document/window access at module load; every
 * DOM/audio touch is guarded inside the factory so `import()` succeeds headless.
 * ==========================================================================*/

const SUB_STYLE_ID = 'ixsub-css';
const SUB_CSS = `
.ixsub-root{position:fixed;inset:0;z-index:8;pointer-events:none;overflow:hidden;}
.ixsub-word{position:absolute;left:0;top:0;will-change:opacity,transform;
  transform:translate(-50%,-50%);opacity:0;
  font-family:Arial,"Segoe UI",system-ui,sans-serif;font-weight:800;
  letter-spacing:.01em;line-height:1.05;text-align:center;
  max-width:78vw;white-space:normal;word-break:break-word;
  color:var(--ixsub-color,#fff);
  text-shadow:0 0 10px rgba(0,0,0,.85),0 2px 14px rgba(0,0,0,.75),0 0 2px rgba(0,0,0,.9);
  transition:opacity var(--ixsub-fade,220ms) ease, transform var(--ixsub-fade,220ms) ease;}
.ixsub-word.is-on{opacity:var(--ixsub-op,.5);}
@media (prefers-reduced-motion: reduce){
  .ixsub-word{transition:opacity 260ms ease;transform:translate(-50%,-50%) !important;}
}`;

/* ----------------------------------------------------------------------------
 * pure helpers (no DOM) — exported for headless tests, used by the live path.
 * -------------------------------------------------------------------------- */
export function clamp01(n) { return n < 0 ? 0 : n > 1 ? 1 : (n || 0); }
export function lerp(a, b, t) { return a + (b - a) * clamp01(t); }

/** Cadence window (ms) for a depth: tightens slightly as depth rises (~8-20s band). */
export function cadenceMs(depth, rnd) {
  const d = clamp01(depth);
  const min = lerp(11000, 6500, d);
  const max = lerp(20000, 11000, d);
  const r = (typeof rnd === 'number') ? clamp01(rnd) : Math.random();
  return Math.round(min + (max - min) * r);
}

/** Target opacity for a depth: subtle, ~0.35 rising to ~0.85. */
export function opacityFor(depth, capScale) {
  const base = lerp(0.35, 0.85, clamp01(depth));
  return clamp01(base * (typeof capScale === 'number' ? capScale : 1));
}

/** Font size (px) for a depth, clamped 34..64. */
export function fontPxFor(depth) {
  return Math.round(lerp(34, 64, clamp01(depth)));
}

/* ----------------------------------------------------------------------------
 * factory
 * -------------------------------------------------------------------------- */
export function createSubliminals(opts = {}) {
  const hasDOM = (typeof document !== 'undefined') && !!document.body;

  // Normalise the pool to clean { text, audio? } entries.
  const pool = (Array.isArray(opts.pool) ? opts.pool : [])
    .map((e) => (e && typeof e === 'object')
      ? { text: String(e.text || '').trim(), audio: (typeof e.audio === 'string' && e.audio) ? e.audio : null }
      : (typeof e === 'string' ? { text: e.trim(), audio: null } : null))
    .filter((e) => e && e.text.length > 0);

  const caps = opts.caps || null;
  // A gentle cap hook: subDensity / masterIntensity trim overall presence when set.
  const capScale = caps
    ? clamp01((typeof caps.subDensity === 'number' ? caps.subDensity : 1) *
              (typeof caps.masterIntensity === 'number' ? caps.masterIntensity : 1))
    : 1;
  const accent = (opts.theme && typeof opts.theme.accent === 'string' && opts.theme.accent) || '';

  // No-op module when there's nothing to show or no DOM (headless / empty pool).
  if (!hasDOM || pool.length === 0) {
    return { setDepth() {}, dispose() {} };
  }

  let reduced = false;
  try { reduced = typeof matchMedia === 'function' && matchMedia('(prefers-reduced-motion: reduce)').matches; }
  catch (_e) {}

  let root = null;
  let depthNow = 0;
  let timer = 0;
  let disposed = false;
  const recent = [];            // no-repeat-last-5 rotation of pool indices

  /* ----- audio: one AudioContext + gain (~0.5), lazy decode, LRU ~10 -------- */
  const AUDIO_GAIN = 0.5;
  const AUDIO_LRU_MAX = 10;
  const MAX_CLIP_SECONDS = 8;   // clip length guard (skip long clips)
  let actx = null, gain = null, audioDead = false;
  const bufCache = new Map();    // audio-uri -> AudioBuffer (insertion-ordered LRU)
  const decoding = new Set();    // in-flight decode keys (avoid duplicate fetches)

  function ensureAudio() {
    if (audioDead) return false;
    if (actx && gain) return true;
    try {
      const AC = window.AudioContext || window.webkitAudioContext;
      if (!AC) { audioDead = true; return false; }
      actx = new AC();
      gain = actx.createGain();
      gain.gain.value = AUDIO_GAIN;
      gain.connect(actx.destination);
      return true;
    } catch (_e) { audioDead = true; return false; }
  }

  function lruTouch(key, buf) {
    if (bufCache.has(key)) bufCache.delete(key);
    bufCache.set(key, buf);
    while (bufCache.size > AUDIO_LRU_MAX) {
      const oldest = bufCache.keys().next().value;
      bufCache.delete(oldest);
    }
  }

  function playBuffer(buf) {
    if (!buf || !actx || !gain) return;
    try {
      const src = actx.createBufferSource();
      src.buffer = buf;
      src.connect(gain);
      src.start(0);
    } catch (_e) {}
  }

  function playAudio(uri) {
    if (!uri || !ensureAudio()) return;
    try { if (actx.state === 'suspended') actx.resume().catch(() => {}); } catch (_e) {}

    const cached = bufCache.get(uri);
    if (cached) {
      if (cached.duration <= MAX_CLIP_SECONDS) { lruTouch(uri, cached); playBuffer(cached); }
      return;
    }
    if (decoding.has(uri)) return; // decode already in flight; skip this flash's audio
    decoding.add(uri);
    // Lazy fetch + decode on first use.
    fetch(uri)
      .then((r) => r.arrayBuffer())
      .then((ab) => new Promise((res, rej) => {
        // Callback form for the widest browser support.
        const p = actx.decodeAudioData(ab, res, rej);
        if (p && typeof p.then === 'function') p.then(res, rej);
      }))
      .then((buf) => {
        decoding.delete(uri);
        if (disposed || !buf) return;
        lruTouch(uri, buf);
        if (buf.duration <= MAX_CLIP_SECONDS) playBuffer(buf); // present-time flash still on screen
      })
      .catch(() => { decoding.delete(uri); }); // decode failure = silent (text still flashed)
  }

  /* ----- pause gate: freeze or a briefing / interstitial overlay ----------- */
  function paused() {
    try {
      if (document.body && document.body.classList.contains('ix-freeze')) return true;
      const ov = document.getElementById('intake-overlay');
      if (ov && !ov.hidden && ov.offsetParent !== null) return true;
    } catch (_e) {}
    return false;
  }

  /* ----- DOM ---------------------------------------------------------------- */
  function ensureStyles() {
    try {
      if (document.getElementById(SUB_STYLE_ID)) return;
      const s = document.createElement('style');
      s.id = SUB_STYLE_ID;
      s.textContent = SUB_CSS;
      (document.head || document.body || document.documentElement).appendChild(s);
    } catch (_e) {}
  }

  function mount() {
    if (root) return;
    ensureStyles();
    try {
      root = document.createElement('div');
      root.className = 'ixsub-root';
      root.setAttribute('aria-hidden', 'true');
      if (accent) root.style.setProperty('--ixsub-color', accent);
      document.body.appendChild(root);
    } catch (_e) { root = null; }
  }

  function pickIndex() {
    if (pool.length === 1) return 0;
    const avoid = new Set(recent.slice(-Math.min(5, pool.length - 1)));
    let idx = 0;
    for (let attempt = 0; attempt < 8; attempt++) {
      idx = (Math.random() * pool.length) | 0;
      if (!avoid.has(idx)) break;
    }
    recent.push(idx);
    if (recent.length > 6) recent.shift();
    return idx;
  }

  /** Off-centre, biased to the upper/lower thirds so the question card stays clear. */
  function placement() {
    const topBand = Math.random() < 0.5;
    const yPct = topBand ? lerp(12, 30, Math.random()) : lerp(70, 88, Math.random());
    const xPct = lerp(22, 78, Math.random());
    return { xPct, yPct };
  }

  function flash() {
    if (disposed || !root) return;
    const entry = pool[pickIndex()];
    if (!entry) return;

    const op = opacityFor(depthNow, capScale);
    const fadeMs = reduced ? 260 : 200;
    const holdMs = Math.round(lerp(700, 450, clamp01(depthNow))); // deeper = a touch snappier
    const { xPct, yPct } = placement();

    let word = null;
    try {
      word = document.createElement('div');
      word.className = 'ixsub-word';
      word.textContent = entry.text;
      word.style.left = xPct.toFixed(2) + '%';
      word.style.top = yPct.toFixed(2) + '%';
      word.style.fontSize = fontPxFor(depthNow) + 'px';
      word.style.setProperty('--ixsub-op', String(op));
      word.style.setProperty('--ixsub-fade', fadeMs + 'ms');
      root.appendChild(word);
    } catch (_e) { return; }

    // Connected whisper (if any) rides the visual — same moment as the WPF flash.
    if (entry.audio) playAudio(entry.audio);

    // fade-in -> hold -> fade-out -> remove.
    const w = word;
    requestAnimationFrame(() => {
      if (disposed) { safeRemove(w); return; }
      w.classList.add('is-on');
      setTimeout(() => {
        if (disposed) { safeRemove(w); return; }
        w.classList.remove('is-on');
        setTimeout(() => safeRemove(w), fadeMs + 120);
      }, fadeMs + holdMs);
    });
  }

  function safeRemove(node) {
    try { if (node && node.parentNode) node.parentNode.removeChild(node); } catch (_e) {}
  }

  /* ----- scheduler ---------------------------------------------------------- */
  function schedule(ms) {
    clearTimeout(timer);
    timer = setTimeout(tick, Math.max(500, ms | 0));
  }

  function tick() {
    if (disposed) return;
    if (paused()) { schedule(1500); return; } // hold the bed while covered; retry shortly
    flash();
    schedule(cadenceMs(depthNow));
  }

  // start: mount + first flash a little after the run settles in.
  mount();
  schedule(cadenceMs(depthNow) * 0.6);

  return {
    setDepth(depth) { depthNow = clamp01(depth); },
    dispose() {
      if (disposed) return;
      disposed = true;
      clearTimeout(timer); timer = 0;
      safeRemove(root); root = null;
      bufCache.clear(); decoding.clear();
      try { if (actx && typeof actx.close === 'function') actx.close(); } catch (_e) {}
      actx = null; gain = null;
    },
  };
}
