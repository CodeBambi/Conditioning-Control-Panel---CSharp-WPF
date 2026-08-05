/* ============================================================================
 * ui/perfProbe.js — THE PLAY-TEST'S STOPWATCH.
 *
 * WHY THIS EXISTS (2026-08-05, phone play-test r9). The owner reports lag on an
 * iPhone 13 Pro Max and three rounds of code-reading have produced three
 * plausible culprits and no evidence. PR #129's perf tier and #130's load
 * governor were both authored from a READING of the render paths; neither can be
 * confirmed or refuted by another reading. The next play-test has to come back
 * with numbers, and the phone has no devtools, no console and — standalone — not
 * even the C# log the desktop writes to. So the page measures itself.
 *
 * WHAT IT MEASURES, and why exactly these three:
 *
 *   · ROLLING FPS. The headline. "It lagged" and "it ran at 19fps for the whole
 *     Live phase" are different bug reports.
 *   · WORST FRAME GAP IN THE LAST ~5s. Mean fps hides the failure mode that
 *     actually reads as lag: 58fps with one 400ms hitch per burst feels far
 *     worse than a steady 40, and averages erase it completely.
 *   · LONG TASKS. The only signal that says WHO: a 300ms task is main-thread
 *     JavaScript or layout, never the GPU, which points at the renderers rather
 *     than at the compositor.
 *
 * SAFARI HAS NO longtask ENTRY TYPE, and that is not a footnote — it is the
 * target device. A PerformanceObserver that silently observes nothing would have
 * shipped a telemetry feature that reports zero long tasks on the one machine
 * the feature exists for. So when the entry type is unavailable the rAF loop
 * itself becomes the detector: a frame gap over the same threshold IS a long
 * task seen from the other side (the main thread was busy and did not paint),
 * and the readout says which of the two it is measuring rather than pretending
 * they are the same number.
 *
 * COST. Nothing here is imported unless `?debug=1` asked for it (boot.js loads
 * the module dynamically inside the branch that already decided to build the
 * overlay), so the shipped page pays ONE dynamic import that never happens.
 * Inside the loop the rules are strict:
 *   · the rAF callback is a NAMED HOISTED FUNCTION passed by reference — no
 *     closure, no bound method, no arrow allocated per frame;
 *   · per frame it does arithmetic on `let` numbers and nothing else — no
 *     objects, no arrays, no strings, no Date;
 *   · the readout is rebuilt at most PERF_PAINT_MS apart (2x/s) and the load
 *     context is read into ONE REUSED bag, so even the slow path allocates a
 *     handful of strings a second rather than sixty.
 *
 * THE FRAME BUDGET IS TWO BUCKETS, NOT A RING BUFFER. "Worst gap in the last 5
 * seconds" wants a sliding window; a sliding window wants an array of samples;
 * an array of samples is an allocation per frame. Two half-windows of
 * PERF_BUCKET_MS each — the one filling now and the one before it — give a
 * window that slides between 2.5s and 5s of history using four numbers and no
 * memory at all. The imprecision is in the window LENGTH, never in the maximum
 * it reports, which is the number anyone actually reads.
 *
 * FENCES. This file lives in ui/ and imports from exec/ — which is the legal
 * direction (exec/ never imports ui/ or net/, and that is the fence that
 * matters). It imports only the three cheap READERS: the load governor, the
 * layer registry and the device tier. It never imports a renderer, never
 * subscribes to anything, and never writes.
 *
 * Import-safe under node: no DOM, no window, no timers at module scope.
 * ==========================================================================*/

import { governorBusy } from '../exec/loadGovernor.js';
import { get as layerNode, fxHeat } from '../exec/layers.js';
import { perfLite } from '../exec/perfTier.js';

/** A task/stall at or over this many ms earns a warn-level line. */
export const PERF_LONGTASK_WARN_MS = 250;
/** The readout is rebuilt no more often than this — "throttled, <= 2x/s". */
export const PERF_PAINT_MS = 500;
/** Half of the worst-gap window. Two of these are what "the last 5s" means here. */
export const PERF_BUCKET_MS = 2500;
/**
 * The floor between two warn lines. A page that is genuinely on fire can fire a
 * long task every frame, and sixty warns a second would flood the 400-char C#
 * log line for line until the useful ones scrolled out — the exact failure the
 * logger's header warns about. Suppressed hits are COUNTED and reported on the
 * next line that gets through, so the rate limit costs a timestamp, not a fact.
 */
export const PERF_WARN_COOLDOWN_MS = 1000;

/* ------------------------------------------------------------------ formatting (pure) */

/**
 * The load context as one short token list. PURE — the caller reads the world,
 * this only names it — so the self-test can pin every token without a DOM.
 *
 * Zero counts are OMITTED. The line has to fit on a phone in 10px monospace next
 * to the fps, and `fl:0 vw:0 bb:0 sb:0 bo:0` is five tokens of "nothing is
 * happening" crowding out the one that says what is.
 *
 * @param {object} b the bag readLoadContext fills
 * @returns {string} e.g. `fx:hot fl:14 vw:2 sp dr gov lite`
 */
export function formatLoadContext(b) {
  const g = b && typeof b === 'object' ? b : {};
  let s = 'fx:' + (g.heat || 'idle');
  if (g.flash > 0) s += ' fl:' + g.flash;      // flashes on screen — a burst, or the bed
  if (g.vwin > 0) s += ' vw:' + g.vwin;        // floating video windows open
  if (g.bubbles > 0) s += ' bb:' + g.bubbles;
  if (g.sub > 0) s += ' sb:' + g.sub;
  if (g.bounce > 0) s += ' bo:' + g.bounce;
  if (g.spiral > 0) s += ' sp';                // the spiral bed is woven
  if (g.drain > 0) s += ' dr';                 // the drain veil is up
  if (g.stage > 0) s += ' st';                 // centre stage is occupied (ramp video, lock card)
  if (g.gov) s += ' gov';                      // a payload squall holds the governor (lite only)
  if (g.lite) s += ' lite';
  return s;
}

/**
 * The one-line readout. PURE, for the same reason as above.
 *
 * `worst` is the gap; `lt` is the long-task tally. When the browser has no
 * longtask entry type the tally is written `lt~` — the tilde is the whole
 * caveat, and it means "counted from frame gaps, not from the API", so nobody
 * compares a Safari number to a Chromium one without noticing.
 *
 * @param {{fps:number, worstMs:number, hits:number, longestMs:number,
 *          observed:boolean, ctx:string}} s
 */
export function formatPerfLine(s) {
  const v = s && typeof s === 'object' ? s : {};
  const fps = Math.max(0, Math.round(v.fps || 0));
  const worst = Math.max(0, Math.round(v.worstMs || 0));
  const hits = Math.max(0, v.hits | 0);
  const longest = Math.max(0, Math.round(v.longestMs || 0));
  let line = fps + 'fps · worst ' + worst + 'ms';
  line += ' · ' + (v.observed ? 'lt ' : 'lt~ ') + hits;
  if (hits > 0) line += '/' + longest + 'ms';
  return line + ' · ' + (v.ctx || '');
}

/* --------------------------------------------------------------- reading the world */

/**
 * The bag, allocated ONCE and refilled in place. The probe reads it twice a
 * second and again on every warn; a fresh object each time would be the only
 * garbage this module produces, and a perf tool that makes garbage is arguing
 * against itself.
 */
const bag = {
  heat: 'idle', flash: 0, vwin: 0, bubbles: 0, sub: 0, bounce: 0,
  spiral: 0, drain: 0, stage: 0, gov: false, lite: false,
};

/** childElementCount of one fx layer, or 0 for "no DOM / no layer / it threw". */
function countIn(name) {
  try {
    const el = layerNode(name);
    return el && typeof el.childElementCount === 'number' ? el.childElementCount : 0;
  } catch (_e) { return 0; }
}

/**
 * Refill `bag` from the cheapest readers the page has. Every one of them is a
 * cached element reference or a single attribute read — exec/layers.js memoizes
 * its lookups and exec/perfTier.js reads one attribute off <html> — so this is
 * a couple of microseconds, twice a second.
 *
 * NOTHING IS SUBSCRIBED TO and nothing is imported that could run code: the
 * probe must be able to observe a wedged page, which rules out asking a
 * renderer how it is doing.
 *
 * @returns {object} the shared bag (never a copy — do not retain it)
 */
export function readLoadContext() {
  try { bag.heat = fxHeat(); } catch (_e) { bag.heat = 'idle'; }
  bag.flash = countIn('flash');
  bag.vwin = countIn('vwin');
  bag.bubbles = countIn('bubbles');
  bag.sub = countIn('sub');
  bag.bounce = countIn('bounce');
  bag.spiral = countIn('spiral');
  bag.drain = countIn('drain');
  bag.stage = countIn('stage');
  try { bag.gov = governorBusy() === true; } catch (_e) { bag.gov = false; }
  try { bag.lite = perfLite() === true; } catch (_e) { bag.lite = false; }
  return bag;
}

/* --------------------------------------------------------------------- the probe */

/**
 * Build the probe. Returns a handle whose every method is a no-op when there is
 * no rAF to hang off (node, a stub harness), so the caller never branches.
 *
 * @param {object} [o]
 * @param {(text:string) => void} [o.setStatus] where the one-line readout goes
 *        (ui/debugOverlay.js setStatus)
 * @param {(msg:string) => void} [o.onWarn] warn-level sink — boot passes
 *        logger.warn, which is the ONE call that reaches the C# host log AND
 *        (via the logger's tee) the overlay's ring at the same time
 * @param {object} [o.win] injectable window, for the self-test
 * @param {() => object} [o.context] injectable load-context reader
 * @param {number} [o.warnMs] TEST AFFORDANCE
 * @param {number} [o.paintMs] TEST AFFORDANCE
 * @param {number} [o.cooldownMs] TEST AFFORDANCE
 */
export function createPerfProbe({
  setStatus = null, onWarn = null, win = null, context = null,
  warnMs, paintMs, cooldownMs,
} = {}) {
  const w = win || (typeof window !== 'undefined' ? window : null);
  const dead = { start() { return false; }, stop() {}, sample() { return null; }, running() { return false; } };
  if (!w || typeof w.requestAnimationFrame !== 'function') return dead;

  const WARN_MS = Number.isFinite(warnMs) ? warnMs : PERF_LONGTASK_WARN_MS;
  const PAINT_MS = Number.isFinite(paintMs) ? paintMs : PERF_PAINT_MS;
  const COOL_MS = Number.isFinite(cooldownMs) ? cooldownMs : PERF_WARN_COOLDOWN_MS;
  const readCtx = typeof context === 'function' ? context : readLoadContext;
  const emit = typeof onWarn === 'function' ? onWarn : null;
  const paintTo = typeof setStatus === 'function' ? setStatus : null;

  /* Every one of these is a plain number the frame callback mutates in place.
   * cur* is the bucket filling now; prev* is the one before it. */
  let running = false;
  let rafId = 0;
  let last = 0;              // timestamp of the previous frame
  let bucketAt = 0;          // when the current bucket opened
  let curFrames = 0;
  let curSpan = 0;
  let curWorst = 0;
  let prevFrames = 0;
  let prevSpan = 0;
  let prevWorst = 0;
  let paintedAt = 0;

  let hits = 0;              // long tasks (or, unobserved, stalls) this session
  let longest = 0;           // the worst one, ms
  /* Last warn emitted, in loop time. It starts a long way in the PAST, not at
   * zero: performance.now() is zero at page load, so `0` would mean "we already
   * warned at boot" and would swallow the first hitch of every session — which
   * on this page is the one during the very first burst. */
  let warnedAt = -1e9;
  let suppressed = 0;        // warns the cooldown ate since then
  let observed = false;      // did PerformanceObserver('longtask') actually take?
  let obs = null;

  /** Now, in the same clock the rAF timestamp uses (so the two are comparable). */
  function nowMs() {
    try {
      if (w.performance && typeof w.performance.now === 'function') return w.performance.now();
    } catch (_e) { /* fall through */ }
    return Date.now();
  }

  /** Rolling fps over both buckets. Falls back to the current one alone at t<2.5s. */
  function fpsNow() {
    const span = curSpan + prevSpan;
    const frames = curFrames + prevFrames;
    return span > 0 ? (frames * 1000) / span : 0;
  }

  /** The worst gap either half-window saw — "the last ~5 seconds". */
  function worstNow() { return curWorst > prevWorst ? curWorst : prevWorst; }

  /**
   * One warn line, rate-limited. `kind` names WHICH detector fired, because the
   * two have different blind spots and a reader has to be able to tell them
   * apart six weeks later in a log file.
   */
  function warnLoad(kind, ms, at) {
    if (!emit) return;
    if (at - warnedAt < COOL_MS) { suppressed++; return; }
    const extra = suppressed ? ' (+' + suppressed + ' more in the last ' + COOL_MS + 'ms)' : '';
    warnedAt = at;
    suppressed = 0;
    try {
      emit('perf: ' + kind + ' ' + Math.round(ms) + 'ms' + extra
        + ' — ' + formatLoadContext(readCtx())
        + ' | ' + Math.round(fpsNow()) + 'fps worst ' + Math.round(worstNow()) + 'ms');
    } catch (_e) { /* a telemetry line can never be the thing that breaks the page */ }
  }

  /** Rebuild the readout. Only ever called from the throttled branch. */
  function paint() {
    if (!paintTo) return;
    try {
      paintTo(formatPerfLine({
        fps: fpsNow(),
        worstMs: worstNow(),
        hits,
        longestMs: longest,
        observed,
        ctx: formatLoadContext(readCtx()),
      }));
    } catch (_e) { /* ditto */ }
  }

  /**
   * THE FRAME CALLBACK. Named and hoisted so `w.requestAnimationFrame(frame)`
   * hands over the SAME function object every frame — an inline arrow here would
   * be one closure allocation per frame, sixty a second, forever, which is the
   * kind of thing this file exists to catch.
   */
  function frame(t) {
    if (!running) return;
    const now = typeof t === 'number' ? t : nowMs();
    if (last > 0) {
      const dt = now - last;
      curFrames++;
      curSpan += dt;
      if (dt > curWorst) curWorst = dt;
      /* THE SAFARI PATH. With no longtask API a gap this size is the only
       * evidence a long task leaves, so it is counted as one — and the readout's
       * `lt~` says that is what happened. When the API IS live this branch stays
       * shut, because the observer already counted the same event and counting
       * it twice would inflate every number on the line. */
      if (!observed && dt >= WARN_MS) {
        hits++;
        if (dt > longest) longest = dt;
        warnLoad('frame stall', dt, now);
      }
    }
    last = now;

    if (now - bucketAt >= PERF_BUCKET_MS) {
      prevFrames = curFrames; prevSpan = curSpan; prevWorst = curWorst;
      curFrames = 0; curSpan = 0; curWorst = 0;
      bucketAt = now;
    }
    if (now - paintedAt >= PAINT_MS) { paintedAt = now; paint(); }

    rafId = w.requestAnimationFrame(frame);
  }

  /** The observer callback — also hoisted, also passed by reference. */
  function onLongTasks(list) {
    let entries = null;
    try { entries = list && typeof list.getEntries === 'function' ? list.getEntries() : null; }
    catch (_e) { return; }
    if (!entries) return;
    for (let i = 0; i < entries.length; i++) {
      const ms = Number(entries[i] && entries[i].duration) || 0;
      hits++;
      if (ms > longest) longest = ms;
      if (ms >= WARN_MS) warnLoad('long task', ms, nowMs());
    }
  }

  /**
   * Wire the observer, or leave `observed` false so the rAF path takes over.
   * `buffered:true` picks up the long tasks that fired during boot, which is
   * exactly the window a debug session most wants and the one it can never
   * otherwise see (the probe starts after the page has already done its worst).
   */
  function armObserver() {
    try {
      const PO = w.PerformanceObserver;
      if (typeof PO !== 'function') return;
      const types = PO.supportedEntryTypes;
      if (Array.isArray(types) && types.indexOf('longtask') < 0) return;
      obs = new PO(onLongTasks);
      obs.observe({ type: 'longtask', buffered: true });
      observed = true;
    } catch (_e) {
      // Safari, and any browser that names the type but refuses the observe.
      obs = null;
      observed = false;
    }
  }

  return {
    start() {
      if (running) return true;
      running = true;
      last = 0;
      bucketAt = nowMs();
      paintedAt = bucketAt;
      armObserver();
      rafId = w.requestAnimationFrame(frame);
      return true;
    },

    stop() {
      running = false;
      try { if (rafId && typeof w.cancelAnimationFrame === 'function') w.cancelAnimationFrame(rafId); }
      catch (_e) { /* ignore */ }
      rafId = 0;
      try { if (obs) obs.disconnect(); } catch (_e) { /* ignore */ }
      obs = null;
      if (paintTo) { try { paintTo(''); } catch (_e) { /* ignore */ } }
    },

    running() { return running; },

    /** The numbers, for a test or a report card. Allocates — never called per frame. */
    sample() {
      return {
        fps: fpsNow(), worstMs: worstNow(), hits, longestMs: longest,
        observed, suppressed, line: formatPerfLine({
          fps: fpsNow(), worstMs: worstNow(), hits, longestMs: longest,
          observed, ctx: formatLoadContext(readCtx()),
        }),
      };
    },
  };
}

export default createPerfProbe;
