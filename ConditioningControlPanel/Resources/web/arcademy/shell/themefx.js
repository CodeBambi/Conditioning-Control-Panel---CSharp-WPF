/* ============================================================================
 * shell/themefx.js - THE WEATHER LAYER.
 *
 * A NEW RENDER SURFACE, and the school's fourth (engine effects on #arc-fx,
 * scene.js's `motes` canvas inside a room, campus-walker's SVG, and now this).
 * ONE fixed full-viewport <canvas> on <body>, `pointer-events:none`, painted by
 * ONE rAF loop throttled to ~30fps, carrying at most THEME_FX_BUDGET particles.
 *
 * WHERE IT SITS, AND WHY. The body-level stack is: class stage / report stage
 * 10, campus stage 20 and the end card 20, topbar 30, the fixed exit bar 32,
 * the confirm 36, #arc-fx 40, #arc-ceremony 45, #arc-emi 50, #arc-toast 60.
 * The canvas takes **z-index 25**: above the campus stage (which paints an
 * opaque sky and would bury it) and below every piece of chrome a player has to
 * read or press. Snow falls in front of the quad and behind the topbar, the
 * exit sign, EMI and every toast. It never takes a pointer, so no rect, door or
 * button under it moves.
 *
 * WHY IT DOES NOT BREAK THE WALK.JS LAWS. campus-walker's laws (rects and
 * polylines only, transform+opacity only, NO canvas, budget caps) bind the
 * WALKER'S OWN SVG - a layer that shares a stacking context with the plan and
 * whose every node is hit-tested against the campus's doors. This canvas is
 * outside `.campus-stage` entirely, hangs off <body>, hit-tests nothing, and is
 * the only thing on its layer. It is the same arrangement scene.js's `motes`
 * canvas already has inside a room, held to the same three kill switches.
 *
 * THE FOUR KILL SWITCHES, and any one of them is enough:
 *   1. `html.arc-reduced` (or the reduced flag) - NO LAYER AT ALL. The canvas
 *      is never created, not created-and-paused (scene.js's law).
 *   2. lite (`init.performanceMode`) - same, no layer.
 *   3. the active theme has no fx - same, no layer.
 *   4. a class is running - the layer is torn down for the duration. Games own
 *      the screen; nothing of the school's decoration may paint over one.
 * Plus the free one every timer in this school owes: a hidden tab / blurred
 * window parks the loop and returning re-arms it.
 *
 * THE ANNEX'S LAW: this module imports nothing. `t`, the flags and the log all
 * arrive as caps, so it is importable in bare Node and the whole budget /
 * kill-switch contract is testable against a fake 2d context.
 * ==========================================================================*/

/* ---------------------------------------------------------------------------
 * THE SHEET. A real file (shell/themes.css), linked once and lazily, resolved
 * against THIS MODULE rather than the document - corkboard.js's recipe, and for
 * corkboard.js's reason (shell modules and the document can sit at different
 * roots). Injecting the link from here keeps this wave to new files only.
 * ------------------------------------------------------------------------- */
export const STYLE_ID = 'arc-themes-style';

export const STYLE_HREF = (function resolveSheet() {
  try { return new URL('./themes.css', import.meta.url).href; }
  catch (e) { return 'shell/themes.css'; }
}());

/** Link the sheet once. Idempotent, guarded, a no-op on the node DOM double. */
export function ensureStyles(doc) {
  const d = doc || (typeof document !== 'undefined' ? document : null);
  if (!d || typeof d.createElement !== 'function') return false;
  try {
    if (d.getElementById && d.getElementById(STYLE_ID)) return true;
    const link = d.createElement('link');
    link.id = STYLE_ID;
    link.rel = 'stylesheet';
    link.href = STYLE_HREF;
    const head = d.head || d.body || d.documentElement;
    if (!head || typeof head.appendChild !== 'function') return false;
    head.appendChild(link);
    return true;
  } catch (e) { return false; }
}

/* ---------------------------------------------------------------------------
 * DIALS
 * ------------------------------------------------------------------------- */

/** Hard ceiling on live particles, desktop. Never exceeded, whatever the size
 *  of the window - a 4K screen gets bigger flakes, not more of them. */
export const THEME_FX_BUDGET = 120;

/** The phone's share of that. A hand-held drawing 120 sprites at 30fps under a
 *  webview is how a campus starts dropping frames on a door press. */
export const THEME_FX_BUDGET_MOBILE = 48;

/** ~30fps. The layer is atmosphere; it is never the thing being watched. */
const FRAME_MS = 33;

/** How many particles a viewport this size wants, before the budget clamps it. */
function wantFor(w, h, per) {
  const n = Math.round((Math.max(320, w) * Math.max(320, h)) / per);
  return Math.max(8, n);
}

/* ---------------------------------------------------------------------------
 * THE PAINTERS
 * Each is {density, spawn(rng, w, h, p), step(p, w, h, dt), draw(g, p, w, h)}.
 * `p` is a recycled plain object - a pool, never an allocation per frame.
 * A painter owns no timer, no node and no I/O; it moves numbers and strokes.
 * ------------------------------------------------------------------------- */

/** DRONE PROTOCOL: sparse falling glyph rain. Two characters only, drawn in the
 *  pix font at alpha <= .35 - a tell that the campus is running somebody else's
 *  cartridge, never a curtain you have to read the school through. */
const DRONE_GLYPHS = ['ア', 'ン'];   // katakana A / N

const PAINTERS = Object.freeze({
  drone: Object.freeze({
    density: 26000,
    spawn(rng, w, h, p) {
      p.x = Math.floor(rng() * Math.max(1, w));
      p.y = -rng() * h;
      p.vy = 42 + rng() * 96;
      p.size = 11 + Math.floor(rng() * 8);
      p.a = 0.10 + rng() * 0.25;
      p.g = DRONE_GLYPHS[Math.floor(rng() * DRONE_GLYPHS.length)] || DRONE_GLYPHS[0];
      p.flip = 1400 + Math.floor(rng() * 2600);
      p.age = 0;
    },
    step(p, w, h, dt) {
      p.y += p.vy * dt;
      p.age += dt * 1000;
      // the one flicker: a glyph swaps character now and then, so a column
      // reads as a stream and not as a falling word.
      if (p.age > p.flip) { p.age = 0; p.g = p.g === DRONE_GLYPHS[0] ? DRONE_GLYPHS[1] : DRONE_GLYPHS[0]; }
      return p.y - p.size <= h;
    },
    draw(g, p) {
      g.globalAlpha = p.a;
      g.font = p.size + 'px ' + DRONE_FONT;
      g.fillText(p.g, p.x, p.y);
    },
    prep(g, color) { g.fillStyle = color; g.textBaseline = 'top'; },
    color: '#5CE85C',
  }),

  /** SNOW DAY: slow drift. Round flakes, a lazy sine sway, nothing sparkles. */
  snow: Object.freeze({
    density: 18000,
    spawn(rng, w, h, p) {
      p.x = rng() * Math.max(1, w);
      p.y = -rng() * h;
      p.vy = 14 + rng() * 34;
      p.size = 1 + rng() * 2.4;
      p.a = 0.28 + rng() * 0.42;
      p.sway = 6 + rng() * 16;
      p.phase = rng() * Math.PI * 2;
      p.rate = 0.4 + rng() * 0.9;
      p.t = 0;
    },
    step(p, w, h, dt) {
      p.t += dt;
      p.y += p.vy * dt;
      p.x += Math.sin(p.phase + p.t * p.rate) * p.sway * dt;
      return p.y - p.size <= h;
    },
    draw(g, p) {
      g.globalAlpha = p.a;
      g.beginPath();
      g.arc(p.x, p.y, p.size, 0, Math.PI * 2);
      g.fill();
    },
    prep(g, color) { g.fillStyle = color; },
    color: '#E8F1FA',
  }),
});

/** The pix stack, verbatim from styles.css's `--pix` so the rain is the same
 *  face the campus's micro-labels wear. Read as a literal because a canvas
 *  `font` string cannot take a var(). */
const DRONE_FONT = "'Arcademy Pixel','Cascadia Mono','Consolas','Courier New',monospace";

/** Is this fx key one we can actually paint? */
export function hasPainter(kind) {
  return !!(kind && Object.prototype.hasOwnProperty.call(PAINTERS, String(kind)));
}

/* ---------------------------------------------------------------------------
 * THE LAYER
 * ------------------------------------------------------------------------- */

/**
 * @param {Object} o
 * @param {?Element} o.mount      where the canvas hangs (default document.body)
 * @param {boolean}  o.reduced    html.arc-reduced / motionLevel 0
 * @param {boolean}  o.lite       init.performanceMode
 * @param {boolean=} o.mobile     html.arc-mobile (smaller budget)
 * @param {string|number=} o.seed the day seed - the rain is seeded, never live
 *                                Math.random, so two windows on one night look
 *                                the same and a test can assert a frame
 * @param {Function=} o.log
 * @param {Function=} o.rng       injectable rng factory (tests); default is a
 *                                small seeded LCG owned here (no core import)
 */
export function createThemeFx(o = {}) {
  const say = typeof o.log === 'function' ? o.log : () => {};
  const doc = (typeof document !== 'undefined') ? document : null;
  const reduced = !!o.reduced;
  const lite = !!o.lite;
  const budget = o.mobile ? THEME_FX_BUDGET_MOBILE : THEME_FX_BUDGET;

  let kind = null;          // the fx key we were asked for
  let painter = null;       // the resolved painter, or null
  let canvas = null;
  let g = null;
  let raf = 0;
  let lastAt = 0;
  let held = false;         // parked by a hidden tab
  let inClass = false;      // a class is running (kill switch 4)
  let w = 0; let h = 0; let dpr = 1;
  const pool = [];
  let live = 0;
  let frames = 0;
  let rng = makeSeeded(o.seed);

  /* A 32-bit LCG. Deliberately NOT core/rng.js: this module imports nothing
   * (the annex's law), and a weather layer needs "different every flake",
   * not the hashed determinism a grade depends on. It IS seeded, so the same
   * night draws the same first frame - the no-live-Math.random rule, honoured
   * for a decoration that is never value-bearing. */
  function makeSeeded(seed) {
    let s = 0;
    const str = String(seed == null ? 'themefx' : seed);
    for (let i = 0; i < str.length; i++) s = (s * 31 + str.charCodeAt(i)) >>> 0;
    if (!s) s = 0x9E3779B9;
    return function next() {
      s = (Math.imul(s, 1664525) + 1013904223) >>> 0;
      return s / 4294967296;
    };
  }

  /** Every reason there must be no layer at all, in one place. */
  function suppressed() {
    return reduced || lite || inClass || !painter;
  }

  function measure() {
    try {
      const vw = (typeof window !== 'undefined' && window.innerWidth) || 1280;
      const vh = (typeof window !== 'undefined' && window.innerHeight) || 720;
      const ratio = (typeof window !== 'undefined' && window.devicePixelRatio) || 1;
      w = Math.max(1, Math.round(vw));
      h = Math.max(1, Math.round(vh));
      // A weather layer is not worth a 3x backing store on a phone.
      dpr = Math.min(2, Math.max(1, Number(ratio) || 1));
    } catch (e) { w = 1280; h = 720; dpr = 1; }
    if (!canvas) return;
    try {
      canvas.width = Math.round(w * dpr);
      canvas.height = Math.round(h * dpr);
      if (g && typeof g.setTransform === 'function') g.setTransform(dpr, 0, 0, dpr, 0, 0);
    } catch (e) { /* a canvas that will not size simply paints nothing */ }
  }

  function fill() {
    if (!painter) return;
    const want = Math.min(budget, wantFor(w, h, painter.density));
    while (pool.length < want) pool.push({});
    live = want;
    for (let i = 0; i < live; i++) painter.spawn(rng, w, h, pool[i]);
  }

  function build() {
    if (canvas || !doc || typeof doc.createElement !== 'function') return;
    ensureStyles(doc);
    try {
      canvas = doc.createElement('canvas');
      canvas.className = 'arc-themefx';
      if (canvas.setAttribute) canvas.setAttribute('aria-hidden', 'true');
      const host = o.mount || doc.body || doc.documentElement;
      if (!host || typeof host.appendChild !== 'function') { canvas = null; return; }
      host.appendChild(canvas);
      g = (typeof canvas.getContext === 'function') ? canvas.getContext('2d') : null;
    } catch (e) {
      say('theme fx: no canvas (' + ((e && e.message) || e) + ')');
      canvas = null; g = null;
      return;
    }
    measure();
    fill();
    arm();
  }

  function teardown() {
    disarm();
    if (canvas) { try { canvas.remove(); } catch (e) { /* noop */ } }
    canvas = null; g = null; live = 0; pool.length = 0;
  }

  function arm() {
    if (raf || !canvas || suppressed() || held) return;
    if (typeof requestAnimationFrame !== 'function') return;
    lastAt = 0;
    raf = requestAnimationFrame(tick);
  }

  function disarm() {
    if (!raf) return;
    try { if (typeof cancelAnimationFrame === 'function') cancelAnimationFrame(raf); }
    catch (e) { /* noop */ }
    raf = 0;
  }

  function tick(at) {
    raf = 0;
    if (!canvas || suppressed() || held) return;
    const now = Number(at) || 0;
    if (!lastAt) lastAt = now - FRAME_MS;
    const elapsed = now - lastAt;
    // THE THROTTLE. rAF runs at the display's rate (120Hz on plenty of phones);
    // a decoration takes 30 of those and gives the other 90 back.
    if (elapsed >= FRAME_MS) {
      lastAt = now;
      paint(Math.min(0.1, elapsed / 1000));
    }
    raf = requestAnimationFrame(tick);
  }

  function paint(dt) {
    if (!g || !painter) return;
    frames++;
    try {
      g.clearRect(0, 0, w, h);
      g.globalAlpha = 1;
      painter.prep(g, painter.color);
      for (let i = 0; i < live; i++) {
        const p = pool[i];
        if (!painter.step(p, w, h, dt)) painter.spawn(rng, w, h, p);
        painter.draw(g, p, w, h);
      }
      g.globalAlpha = 1;
    } catch (e) {
      // A painter that throws costs the weather, never the school. One line,
      // then the layer goes away for the session rather than throwing 30x/s.
      say('theme fx painter threw (' + ((e && e.message) || e) + ') - layer off');
      painter = null;
      teardown();
    }
  }

  /** THE PARK. A hidden tab or a blurred window stops the loop dead; coming
   *  back re-arms it. Same law scene.js's alive layer keeps. */
  function onVisibility() {
    let hidden = false;
    try { hidden = !!(doc && doc.hidden); } catch (e) { hidden = false; }
    held = hidden;
    if (held) disarm(); else arm();
  }
  function onResize() { measure(); fill(); }

  const listening = [];
  function listen(target, type, fn) {
    if (!target || typeof target.addEventListener !== 'function') return;
    try { target.addEventListener(type, fn); listening.push([target, type, fn]); }
    catch (e) { /* noop */ }
  }
  listen(doc, 'visibilitychange', onVisibility);
  listen(typeof window !== 'undefined' ? window : null, 'resize', onResize);

  /** Build or tear down to match the current gates. The ONE reconciler; every
   *  public verb ends here so there is exactly one place that can be wrong. */
  function sync() {
    if (suppressed()) { teardown(); return; }
    if (!canvas) build(); else arm();
  }

  return {
    /**
     * Point the layer at an fx key ('drone' | 'snow' | null). An unknown key is
     * LOGGED and treated as null - a table is data, and a theme that names a
     * painter this build does not carry is a theme with no weather, never a
     * throw (scene.js's unknown-kind law).
     */
    setKind(next) {
      const want = next == null ? null : String(next);
      if (want === kind) return;
      kind = want;
      if (want && !hasPainter(want)) { say('theme fx: unknown kind "' + want + '" - no layer'); }
      painter = (want && hasPainter(want)) ? PAINTERS[want] : null;
      // A change of weather is a change of pool: tear down and rebuild rather
      // than re-spawn into a canvas sized for the last one.
      teardown();
      sync();
    },

    /** Kill switch 4. `true` while a class is running. */
    setInClass(on) {
      const next = !!on;
      if (next === inClass) return;
      inClass = next;
      sync();
    },

    /** Re-seed (a new UTC day arrived while the page stayed up). */
    setSeed(seed) { rng = makeSeeded(seed); if (canvas) fill(); },

    /** Test seam: what the layer is actually doing right now. */
    stats() {
      return {
        kind, painting: !!canvas, live: canvas ? live : 0, budget, frames,
        suppressed: suppressed(), held, inClass, reduced, lite,
      };
    },

    /** Test seam: drive one frame without a real rAF. */
    step(dt) { if (!suppressed() && canvas) paint(Number(dt) || FRAME_MS / 1000); },

    destroy() {
      for (const [target, type, fn] of listening.splice(0)) {
        try { target.removeEventListener(type, fn); } catch (e) { /* noop */ }
      }
      painter = null; kind = null;
      teardown();
    },
  };
}

export default createThemeFx;
