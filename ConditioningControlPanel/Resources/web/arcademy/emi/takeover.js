/* ============================================================================
 * emi/takeover.js - screenTakeover(): the SECOND CANVAS over EMI's glass.
 *
 * face.js is owner-locked and it is untouched: it keeps painting the pink face
 * on `.emi-screen` exactly as it always did, and this module lays a second
 * canvas of the same locked rect on top of it. A takeover is therefore a thing
 * that happens ABOVE the face, never instead of it, and killing one is a matter
 * of hiding one node.
 *
 * FIVE LAWS THIS FILE EXISTS TO KEEP
 * 1. ZERO COST AT REST. One repeating timer (the wheel, ROLL_MS) and four
 *    passive document listeners. No rAF, no fetch, no observer and no media
 *    element exists while no channel is up.
 * 2. ANY INPUT CANCELS, INSTANTLY. Pointer or key, anywhere. On EMI it is the
 *    channel's CAUGHT beat; anywhere else it is a silent blip-off with no line.
 * 3. A SAY OUTRANKS THE GLASS. No takeover starts while a bubble, a chain or a
 *    class is live, and a say that arrives mid-channel kills it first. The hook
 *    is one line in widget.js's `cancelChain()`, which every one of her cancel
 *    paths already funnels through.
 * 4. plan() REFUSES, IT NEVER STUBS. A channel with no material tonight is
 *    ABSENT from the wheel. Nothing here ever paints an apology.
 * 5. face.js IS NOT OURS. This module reads no face state and writes no face
 *    pixel; the only thing it knows about the renderer is the rect.
 *
 * THE MEDIA BRIGHT LINES (NOW WATCHING, and they are not negotiable)
 *  - Remote media reaches the page through the HOST over the bridge
 *    (`provider/` -> `assets-request`), which is the only remote path this
 *    bundle has ever had: the page never talks to a server for media, and the
 *    host fetches Scrolller straight from the player's own machine.
 *  - The gate is the app's: `MediaSource != "local" && HasRemoteMediaConsent`,
 *    resolved host-side and projected as `remoteMediaEnabled` / `remoteConsent`
 *    / `mediaSource` on `init.settings`, read here through `assets.catalog()`.
 *  - NO NSFW FILTERING OF REMOTE CONTENT, EVER. There is no filter in this file
 *    and none may be added.
 *  - Consent off = the player's own local library (`init.settings.localAssets`).
 *    Neither available = `plan()` returns null and the channel does not exist
 *    tonight.
 * ==========================================================================*/

import { SL_DIALS, GLASS_W, GLASS_H, CHANNELS, rollChannel, pickDeepIdle, rollWrong, titleOf } from './channels.js';

const VIDEO_RE = /\.(mp4|webm|mov|m4v)(\?|#|$)/i;

/* ============================================================================
 * THE MEDIA BROKER - the only I/O in the wave, and it is all somebody else's.
 * ==========================================================================*/

/**
 * @param {Object} o
 * @param {Object=} o.assets   provider/index.js createAssets() handle (claim/catalog)
 * @param {Object=} o.settings init.settings (for `localAssets`)
 * @param {Function=} o.rand
 */
export function createMediaBroker({ assets, settings, rand, doc, log } = {}) {
  const rng = typeof rand === 'function' ? rand : Math.random;
  const d = doc || (typeof document !== 'undefined' ? document : null);
  const note = typeof log === 'function' ? log : () => {};

  function localList(kind) {
    const bag = settings && settings.localAssets;
    if (!bag || typeof bag !== 'object') return [];
    const key = kind === 'still' ? 'stills' : 'gifs';
    const a = Array.isArray(bag[key]) ? bag[key] : [];
    if (a.length) return a;
    // A library with only one kind in it is still a library.
    const other = Array.isArray(bag[kind === 'still' ? 'gifs' : 'stills']) ? bag[kind === 'still' ? 'gifs' : 'stills'] : [];
    return other;
  }

  function gate() {
    if (!assets || typeof assets.catalog !== 'function') return false;
    try { return !!assets.catalog().remoteMediaEnabled; } catch (e) { return false; }
  }

  let pool = null;
  let poolAsked = false;

  /** SYNCHRONOUS, and `plan()`'s only question: is there ANY material at all. */
  function ready() {
    return localList('gif').length > 0 || localList('still').length > 0 || gate();
  }

  function ensurePool() {
    if (pool || poolAsked || !gate()) return null;
    poolAsked = true;
    try {
      /* ONE CLAIM PER SITTING, not one per takeover. `canvasSafe` is FALSE on
       * purpose and it is the considered call: the two-pool law exists so a
       * consumer that READS pixels never meets a tainted origin, and nothing
       * here ever reads one back - the glass is drawn to and never sampled, and
       * the reveal card is a plain <img>. A canvasSafe claim would have made
       * the flagship channel local-only for every consenting player. */
      const p = assets.claim({ loops: 2, stills: 2, canvasSafe: false });
      if (p && typeof p.then === 'function') p.then((x) => { pool = x || null; }).catch(() => { pool = null; });
      else pool = p || null;
    } catch (e) { note('media: claim threw - ' + ((e && e.message) || e)); }
    return null;
  }

  function poolUrl(still) {
    if (!pool || typeof pool.next !== 'function') return null;
    try {
      const row = pool.next(still ? 'still' : 'loop');
      return row && row.url ? { url: row.url, remote: !!row.remote } : null;
    } catch (e) { return null; }
  }

  function localUrl(still) {
    const list = localList(still ? 'still' : 'gif');
    if (!list.length) return null;
    const u = list[Math.floor(rng() * list.length)];
    return typeof u === 'string' && u ? { url: u, remote: false } : null;
  }

  /** Mint the element and settle when it can actually be drawn. */
  function load(url, deadline) {
    if (!d || !d.createElement) return Promise.resolve(null);
    return new Promise((resolve) => {
      let done = false;
      const finish = (el) => { if (done) return; done = true; resolve(el); };
      const budget = Math.max(120, deadline - Date.now());
      const timer = setTimeout(() => finish(null), budget);
      const settle = (el) => { clearTimeout(timer); finish(el); };
      try {
        if (VIDEO_RE.test(url)) {
          const v = d.createElement('video');
          v.muted = true; v.loop = true; v.autoplay = true; v.playsInline = true;
          if (v.setAttribute) { v.setAttribute('muted', ''); v.setAttribute('playsinline', ''); }
          v.addEventListener('loadeddata', () => { try { v.play(); } catch (e) { /* noop */ } settle(v); });
          v.addEventListener('error', () => settle(null));
          v.src = url;
        } else {
          const im = d.createElement('img');
          im.addEventListener('load', () => settle(im));
          im.addEventListener('error', () => settle(null));
          im.src = url;
        }
      } catch (e) { settle(null); }
    });
  }

  return {
    ready,
    remote: gate,
    /**
     * ONE ITEM PER TAKEOVER. Resolves null rather than throwing, and null is
     * the runner's cue to SKIP the takeover entirely - never to open on a black
     * glass. The whole call is bounded by FETCH_BUDGET_MS.
     */
    pick({ still, budgetMs } = {}) {
      const deadline = Date.now() + (budgetMs || SL_DIALS.FETCH_BUDGET_MS);
      ensurePool();
      const tryOnce = () => {
        const hit = poolUrl(!!still) || localUrl(!!still) || poolUrl(!still) || localUrl(!still);
        if (!hit) return Promise.resolve(null);
        return load(hit.url, deadline).then((el) => {
          if (!el) return null;
          return {
            el,
            url: hit.url,
            remote: !!hit.remote,
            title: titleOf(hit.url),
            pause() { try { if (el.pause) el.pause(); } catch (e) { /* noop */ } },
            release() {
              try { if (el.pause) el.pause(); } catch (e) { /* noop */ }
              try { el.src = ''; } catch (e) { /* noop */ }
              try { if (el.remove) el.remove(); } catch (e) { /* noop */ }
            },
          };
        });
      };
      // The pool's first batch lands asynchronously; give it the one wait it is
      // owed and then take whatever is actually there.
      if (gate() && !pool) {
        return new Promise((res) => setTimeout(res, Math.min(600, Math.max(0, deadline - Date.now())))).then(tryOnce);
      }
      return tryOnce();
    },
    destroy() {
      try { if (pool && pool.release) pool.release(); } catch (e) { /* noop */ }
      pool = null;
    },
  };
}

/* ============================================================================
 * THE DECK - screenTakeover, the wheel, the blip and every cancel rule.
 * ==========================================================================*/

/**
 * @param {Object} o
 * @param {Element} o.root    the `.arc-emi` layer (the reveal card's host)
 * @param {Element} o.el      the `.emi` element (the poke hit-test)
 * @param {Element} o.glass   the SECOND canvas, over `.emi-screen`
 * @param {Object} o.emi      {raw, play, say, chains, busy, saying}
 * @param {Object} o.state    {hidden, enabled, dragging, reducedMotion, face}
 * @param {Object=} o.media   createMediaBroker() handle
 * @param {Object=} o.data    {days(), labSeen()}
 * @param {Function=} o.onStat  (key) => void, for 'takeovers'|'caught'|'reveals'
 */
export function createDeck(o = {}) {
  const dials = Object.assign({}, SL_DIALS, o.dials || {});
  const doc = o.doc || (typeof document !== 'undefined' ? document : null);
  const win = o.win || (typeof window !== 'undefined' ? window : null);
  const now = typeof o.now === 'function' ? o.now : () => Date.now();
  const rand = typeof o.rand === 'function' ? o.rand : Math.random;
  const note = typeof o.log === 'function' ? o.log : () => {};
  const emi = o.emi || {};
  const state = o.state || {};
  const seed = String(o.seed || 'emi-off-channels');
  const bump = typeof o.onStat === 'function' ? o.onStat : () => {};

  const counters = { takeovers: 0, caught: 0, reveals: 0 };
  const cooldowns = Object.create(null);
  let sessionTakeovers = 0;
  let wrongUsed = false;
  let lastTakeoverEnd = 0;
  let lastInput = now();
  let sceneBusy = false;              // a class / a suspend owns the screen
  let destroyed = false;

  /* ---------------------- the glass ------------------------------------- */
  let ctx2d = null;
  let g = null;
  let off = null;                     // the blip's offscreen snapshot
  let offCtx = null;

  function glassCtx() {
    if (ctx2d) return ctx2d;
    const c = o.glass;
    if (!c || typeof c.getContext !== 'function') return null;
    try {
      c.width = GLASS_W;
      c.height = GLASS_H;
      ctx2d = c.getContext('2d');
      if (ctx2d) ctx2d.imageSmoothingEnabled = false;
    } catch (e) { ctx2d = null; }
    g = ctx2d ? { c: ctx2d, w: GLASS_W, h: GLASS_H, rm: false } : null;
    return ctx2d;
  }

  function showGlass(on) {
    try { if (o.glass) o.glass.hidden = !on; } catch (e) { /* noop */ }
    // BLANK IT ON THE WAY DOWN. Hiding is the real cure (emi.css `.emi-glass
    // [hidden]`), but a canvas holding the blip's last dot is one stale frame a
    // future stylesheet mistake could put back on her face.
    if (!on && ctx2d) { try { ctx2d.clearRect(0, 0, GLASS_W, GLASS_H); } catch (e) { /* noop */ } }
  }

  function snapshot() {
    if (!doc || !doc.createElement || !o.glass) return null;
    try {
      if (!off) {
        off = doc.createElement('canvas');
        off.width = GLASS_W; off.height = GLASS_H;
        offCtx = off.getContext ? off.getContext('2d') : null;
      }
      if (!offCtx) return null;
      offCtx.clearRect(0, 0, GLASS_W, GLASS_H);
      offCtx.drawImage(o.glass, 0, 0);
      return off;
    } catch (e) { return null; }
  }

  /* ---------------------- the live takeover ----------------------------- */
  let live = null;      // {painter, spec, startedAt, capMs, phase, blipAt, snap, uncapped}
  let raf = null;
  let killing = false;
  const timers = new Set();

  function later(fn, ms) {
    const id = setTimeout(() => { timers.delete(id); try { fn(); } catch (e) { /* noop */ } }, ms);
    timers.add(id);
    return id;
  }
  function killTimers() { for (const id of timers) clearTimeout(id); timers.clear(); }

  function stopRaf() {
    if (raf == null) return;
    try { if (typeof cancelAnimationFrame === 'function') cancelAnimationFrame(raf); } catch (e) { /* noop */ }
    raf = null;
  }
  function pumpRaf() {
    if (raf != null || !live) return;
    if (typeof requestAnimationFrame !== 'function') return;
    raf = requestAnimationFrame(step);
  }

  /** THE BLIP, both ways: collapse to a 1px line, then to a dot. */
  function paintBlip(p, dir) {
    if (!g) return;
    const c = g.c;
    c.save();
    c.fillStyle = '#000';
    c.fillRect(0, 0, g.w, g.h);
    // dir 1 = opening (dot -> line -> picture), dir -1 = closing (the reverse)
    const q = dir < 0 ? p : 1 - p;          // 0 = full picture, 1 = the dot
    const LINE_AT = 120 / dials.BLIP_MS;    // the doc's split: 120ms then 60ms
    if (q < LINE_AT) {
      const k = q / LINE_AT;                // picture -> 1px line
      const h = Math.max(1, g.h * (1 - k));
      const src = live && live.snap;
      if (src) {
        try { c.drawImage(src, 0, 0, g.w, g.h, 0, (g.h - h) / 2, g.w, h); } catch (e) { /* noop */ }
      } else {
        c.fillStyle = '#FF69B4';
        c.fillRect(0, (g.h - h) / 2, g.w, h);
      }
      c.fillStyle = 'rgba(255,255,255,' + (0.15 + k * 0.45) + ')';
      c.fillRect(0, g.h / 2 - 1, g.w, 2);
    } else {
      const k = (q - LINE_AT) / (1 - LINE_AT);  // 1px line -> a dot
      const w = Math.max(2, g.w * (1 - k));
      c.fillStyle = 'rgba(255,255,255,' + (0.85 - k * 0.5) + ')';
      c.fillRect((g.w - w) / 2, g.h / 2 - 1, w, 2);
    }
    c.restore();
  }

  function step(ts) {
    raf = null;
    if (!live || destroyed) return;
    const t = typeof ts === 'number' ? ts : now();
    if (live.t0 == null) live.t0 = t;
    const el = t - live.t0;

    if (live.phase === 'in') {
      const p = Math.min(1, el / dials.BLIP_MS);
      paintBlip(p, 1);
      if (p >= 1) { live.phase = 'play'; live.t0 = t; live.snap = null; }
      pumpRaf();
      return;
    }

    if (live.phase === 'play') {
      try { live.painter.frame(g, live.spec, el); }
      catch (e) { note('channel ' + live.painter.id + ' frame threw - ' + ((e && e.message) || e)); kill('error'); return; }
      if (!live.uncapped && el >= live.capMs) { kill('cap'); return; }
      pumpRaf();
      return;
    }

    if (live.phase === 'out') {
      const p = Math.min(1, el / dials.BLIP_MS);
      paintBlip(p, -1);
      if (p >= 1) { finish(); return; }
      pumpRaf();
    }
  }

  function finish() {
    const rec = live;
    live = null;
    stopRaf();
    showGlass(false);
    if (!rec) return;
    try { rec.painter.end(g, rec.spec, rec.reason || 'end'); } catch (e) { /* noop */ }
    lastTakeoverEnd = now();
    if (typeof rec.after === 'function') { const fn = rec.after; rec.after = null; try { fn(); } catch (e) { /* noop */ } }
    /* THE WRONG CHANNEL RIDES THIS EXIT. It owns no slot on the wheel - you
     * catch it in the corner of a CHANNEL CHANGE, which is where signal ghosts
     * live. A rolled channel only, and never after one of its own exits. */
    if (rec.rolled && !arc.open && rec.reason !== 'scene' && rec.reason !== 'hidden') intrude();
  }

  /**
   * screenTakeover(painter, {ms, spec, uncapped, silent}) -> bool
   * The ONE capability of this wave. Returns false when it refused, and a
   * refusal is a normal answer.
   */
  function screenTakeover(painter, opts = {}) {
    if (destroyed || !painter) return false;
    if (live) return false;
    if (!eligible(opts.deep)) return false;
    if (!glassCtx()) return false;
    let spec = opts.spec;
    if (!spec) {
      try { spec = painter.plan(planCtx()); } catch (e) { spec = null; }
      if (!spec) return false;
    }
    g.rm = !!(state.reducedMotion && state.reducedMotion());
    try { painter.start(g, spec); }
    catch (e) { note('channel ' + painter.id + ' start threw - ' + ((e && e.message) || e)); return false; }
    live = {
      painter, spec,
      capMs: typeof opts.ms === 'number' ? opts.ms : dials.TAKEOVER_MAX_MS,
      uncapped: !!(opts.uncapped || painter.uncapped),
      phase: opts.noBlipIn || g.rm ? 'play' : 'in',
      t0: null, snap: null, reason: null, after: null,
      // A ROLLED channel is the only kind an intrusion may ride out of.
      rolled: !opts.deep && painter.id !== 'wrong',
    };
    // The blip's opening frame is the channel's own first frame, collapsed.
    if (live.phase === 'in') {
      try { painter.frame(g, spec, 0); } catch (e) { /* noop */ }
      live.snap = snapshot();
    }
    showGlass(true);
    counters.takeovers += 1;
    bump('takeovers');
    if (!opts.deep) sessionTakeovers += 1;
    if (painter.cooldownMs) cooldowns[painter.id] = now() + painter.cooldownMs;
    pumpRaf();
    return true;
  }

  /** Take the glass down NOW. Never a fade: the channel is simply gone. */
  function kill(reason, after) {
    if (!live || killing) return false;
    killing = true;
    live.reason = reason;
    if (after) live.after = after;
    // MEDIA CUTS INSTANTLY, before the blip has a frame to run.
    if (live.spec && live.spec.item && typeof live.spec.item.pause === 'function') {
      try { live.spec.item.pause(); } catch (e) { /* noop */ }
    }
    const rm = !!(state.reducedMotion && state.reducedMotion());
    const noBlip = rm || reason === 'silent-cut' || (live.painter.caught && live.painter.caught.noBlip && reason === 'poke');
    if (noBlip) {
      killing = false;
      finish();
      return true;
    }
    live.snap = snapshot();
    live.phase = 'out';
    live.t0 = null;
    killing = false;
    pumpRaf();
    return true;
  }

  /* ---------------------- eligibility ----------------------------------- */

  function idleMs() { return now() - lastInput; }

  /** The conjunctive gate. Every leg has to hold, and any leg failing mid
   *  channel cancels it (the roll re-checks, and input cancels on its own). */
  function eligible(deep) {
    if (destroyed) return false;
    if (state.hidden && state.hidden()) return false;
    if (state.enabled && !state.enabled()) return false;
    if (state.dragging && state.dragging()) return false;
    if (emi.busy && emi.busy()) return false;
    if (sceneBusy) return false;
    if (doc && doc.hidden) return false;
    if (arc.open) return false;
    if (deep) return idleMs() >= dials.SAVER_IDLE_MS;
    return idleMs() >= dials.THEATRE_IDLE_MS;
  }

  function planCtx() {
    let days = null;
    try { days = o.data && typeof o.data.days === 'function' ? o.data.days() : null; } catch (e) { days = null; }
    let labSeen = false;
    try { labSeen = !!(o.data && typeof o.data.labSeen === 'function' && o.data.labSeen()); } catch (e) { labSeen = false; }
    return {
      now: now(),
      rand,
      seed: seed + '|' + counters.takeovers,
      reducedMotion: !!(state.reducedMotion && state.reducedMotion()),
      media: o.media || null,
      days,
      labSeen,
      wrongUsed,
      takeovers: counters.takeovers,
      cooldowns,
      face: state.face && state.face(),
    };
  }

  /* ---------------------- the wheel ------------------------------------- */

  let rollTimer = null;

  function roll() {
    rollTimer = null;
    if (!destroyed) rollTimer = setTimeout(roll, dials.ROLL_MS);
    if (live || arc.open || destroyed) return;

    // DEEP IDLE IS DETERMINISTIC. Real screensavers are not lucky, so the
    // saver / off-air look is not on the wheel and skips the cap and the
    // global cooldown outright.
    if (eligible(true)) {
      const deep = pickDeepIdle(planCtx());
      if (deep) screenTakeover(deep.painter, { spec: deep.spec, deep: true, uncapped: true });
      return;
    }
    if (!eligible(false)) return;
    if (sessionTakeovers >= dials.PER_SESSION_CAP) return;
    if (now() - lastTakeoverEnd < dials.GLOBAL_COOLDOWN_MS) return;

    const hit = rollChannel(planCtx());
    if (!hit) return;

    // THE ONE PLACE A CHANNEL MAY WAIT ON MATERIAL, and a miss SKIPS the
    // takeover rather than opening on a black glass.
    if (typeof hit.painter.prepare === 'function') {
      const ctx = planCtx();
      let p = null;
      try { p = hit.painter.prepare(hit.spec, ctx); } catch (e) { p = null; }
      const budget = new Promise((res) => setTimeout(() => res(false), dials.FETCH_BUDGET_MS));
      Promise.race([Promise.resolve(p), budget]).then((okay) => {
        if (!okay || live || !eligible(false)) {
          if (hit.spec && hit.spec.item && hit.spec.item.release) { try { hit.spec.item.release(); } catch (e) { /* noop */ } }
          return;
        }
        screenTakeover(hit.painter, { spec: hit.spec });
      }).catch(() => {});
      return;
    }
    screenTakeover(hit.painter, { spec: hit.spec });
  }

  /**
   * THE WRONG CHANNEL. It owns no slot on the wheel: `finish()` calls this on a
   * rolled channel's EXIT, at 1/40, once a session, `labSeen` only. No blip IN
   * (it INTERRUPTS, like a signal intrusion - the blip only plays on the way
   * out), no Blipese, no line and no caught beat.
   */
  function intrude() {
    if (live || arc.open || wrongUsed) return false;
    const hit = rollWrong(planCtx());
    if (!hit) return false;
    wrongUsed = true;
    const okay = screenTakeover(hit.painter, {
      spec: hit.spec, ms: dials.WRONG_MAX_MS, noBlipIn: true,
    });
    if (okay) sessionTakeovers -= 1;   // an intrusion is not a rolled channel
    return okay;
  }

  /* ---------------------- the caught arc -------------------------------- */

  const arc = { open: false, stage: null, item: null, timer: null, painter: null };

  function arcEnd() {
    arc.open = false;
    arc.stage = null;
    arc.painter = null;
    if (arc.item && typeof arc.item.release === 'function') { try { arc.item.release(); } catch (e) { /* noop */ } }
    arc.item = null;
    hideReveal();
  }

  function pickLine(list) {
    if (!Array.isArray(list) || !list.length) return null;
    return list[Math.floor(rand() * list.length)];
  }

  function sayLine(line, face) {
    if (!line || typeof emi.say !== 'function') return false;
    try { return !!emi.say(line, { face: face || '^_^' }); } catch (e) { return false; }
  }

  function showFace(face, opts) {
    if (typeof emi.raw !== 'function') return;
    try { emi.raw(face, Object.assign({ hold: 900, force: true }, opts || {})); } catch (e) { /* noop */ }
  }

  /** The player touched EMI while a channel was up. This is the whole payoff. */
  function caught(painter, spec) {
    const c = painter.caught;
    if (!c) {
      /* THE WRONG CHANNEL IS NEVER ACKNOWLEDGED. No beat, no line, and it does
       * not even count as a catch - you cannot catch her at something that, as
       * far as she is concerned, did not happen. The glass simply goes. */
      kill('silent-cut');
      return;
    }
    counters.caught += 1;
    bump('caught');
    arc.open = true;
    arc.painter = painter;
    arc.stage = 'caught';
    swallowPet = true;      // the press that caught her is not also a head-pat
    /* THE MEDIA CUTS INSTANTLY - paused here, released at the END of the arc,
     * because the reveal card is still owed the element. `kill()` cannot do it
     * for us any more: the item has left the spec by the time it runs. */
    if (spec && spec.item) {
      try { spec.item.pause(); } catch (e) { /* noop */ }
      arc.item = spec.item;
      spec.item = null;
    }

    const runBeat = () => {
      if (c.chain && typeof emi.play === 'function' && typeof emi.chains === 'function') {
        const table = emi.chains();
        const ch = table && table[c.chain];
        if (ch) { try { emi.play(ch, { force: true }); } catch (e) { /* noop */ } }
        arcEnd();
        return;
      }
      const land = () => {
        showFace(c.face, { hold: c.hold || 900, body: c.body || null, bodyFrame: c.bodyFrame || null });
        const line = rand() < (c.lineOdds == null ? 1 : c.lineOdds) ? pickLine(c.lines) : null;
        if (c.offer) {
          arc.timer = later(() => openOffer(c), (c.hold || 900) + 120);
          return;
        }
        if (line) arc.timer = later(() => { sayLine(line, c.face); arcEnd(); }, (c.hold || 900) + 60);
        else arc.timer = later(arcEnd, (c.hold || 900) + 60);
      };
      if (c.snap) {
        showFace(c.snap.face, { hold: c.snap.hold || 200, bodyFrame: c.snap.bodyFrame || null });
        arc.timer = later(land, c.snap.hold || 200);
      } else land();
    };

    // The gag rides the glass BEFORE the blip: the alt-tab reflex is a frame of
    // the channel, not a face.
    let gagMs = 0;
    if (typeof painter.gag === 'function' && g) {
      try { gagMs = painter.gag(g, spec) || 0; } catch (e) { gagMs = 0; }
    }
    if (gagMs > 0) later(() => kill('poke', runBeat), gagMs);
    else kill('poke', runBeat);
  }

  /** "wanna see?" - held OFFER_MS from the moment the line lands. */
  function openOffer(c) {
    arc.stage = 'offer';
    const line = pickLine(c.offer.lines);
    sayLine(line, '0_0');
    arc.timer = later(() => { if (arc.stage === 'offer') decline(c); }, dials.OFFER_MS + 1600);
  }

  function accept(c) {
    if (arc.stage !== 'offer') return;
    arc.stage = 'reveal';
    counters.reveals += 1;
    bump('reveals');
    showReveal(arc.item);
    showFace(c.offer.accepted.face, { hold: dials.REVEAL_MS, bodyFrame: c.offer.accepted.bodyFrame });
    arc.timer = later(() => {
      hideReveal();
      const line = pickLine(c.offer.accepted.lines);
      sayLine(line, c.offer.accepted.face);
      arcEnd();
    }, dials.REVEAL_MS);
  }

  function decline(c) {
    if (arc.stage !== 'offer') return;
    arc.stage = 'declined';
    const d = c.offer.declined;
    showFace(d.face, { hold: 900, bodyFrame: d.bodyFrame });
    arc.timer = later(() => { sayLine(pickLine(d.lines), d.face); arcEnd(); }, 960);
  }

  /* ---------------------- the reveal card ------------------------------- */
  let card = null;

  function showReveal(item) {
    if (!doc || !doc.createElement || !o.root || !item) return;
    hideReveal();
    try {
      card = doc.createElement('div');
      card.className = 'emi-reveal';
      const shot = doc.createElement(VIDEO_RE.test(item.url) ? 'video' : 'img');
      shot.className = 'emi-reveal-shot';
      if (shot.tagName === 'VIDEO') {
        shot.muted = true; shot.loop = true; shot.autoplay = true; shot.playsInline = true;
      }
      shot.src = item.url;
      const cap = doc.createElement('div');
      cap.className = 'emi-reveal-title';
      cap.textContent = item.title || 'untitled';
      card.appendChild(shot);
      card.appendChild(cap);
      o.root.appendChild(card);
    } catch (e) { card = null; }
  }

  function hideReveal() {
    if (!card) return;
    try { card.remove(); } catch (e) { /* noop */ }
    card = null;
  }

  /* ---------------------- input ----------------------------------------- */

  let swallowPet = false;

  function onEmi(target) {
    if (!target || !o.el) return false;
    if (target === o.el) return true;
    if (target.closest) {
      if (target.closest('.emi-x')) return false;
      return !!target.closest('.emi');
    }
    return false;
  }

  function onPointerDown(ev) {
    lastInput = now();
    const hers = onEmi(ev && ev.target);
    if (live) {
      const painter = live.painter;
      const spec = live.spec;
      if (hers) caught(painter, spec);
      else kill('input');
      return;
    }
    if (arc.open && arc.stage === 'offer') {
      const c = arc.painter && arc.painter.caught;
      if (!c || !c.offer) return;
      if (arc.timer !== null) { clearTimeout(arc.timer); timers.delete(arc.timer); arc.timer = null; }
      if (hers) accept(c);
      else decline(c);
      return;
    }
    if (arc.open && arc.stage === 'reveal') {
      if (arc.timer !== null) { clearTimeout(arc.timer); timers.delete(arc.timer); arc.timer = null; }
      hideReveal();
      const c = arc.painter && arc.painter.caught;
      if (c && c.offer) sayLine(pickLine(c.offer.accepted.lines), c.offer.accepted.face);
      arcEnd();
    }
  }

  /* EMI'S FIRST KEY LISTENER, AND IT IS A PASSIVE READ (see CLAUDE.md trap 78).
   * It never calls preventDefault or stopPropagation and it adds NO rung to the
   * Esc ladder - it exists because "any input cancels" has to mean any input. */
  function onKey() {
    lastInput = now();
    if (live) kill('input');
  }

  function onMove() { lastInput = now(); }

  function onVis() {
    if (doc && doc.hidden && live) kill('hidden');
  }

  if (doc && doc.addEventListener) {
    doc.addEventListener('pointerdown', onPointerDown, true);
    doc.addEventListener('keydown', onKey, { passive: true });
    doc.addEventListener('pointermove', onMove, { passive: true });
    doc.addEventListener('wheel', onMove, { passive: true });
    doc.addEventListener('visibilitychange', onVis);
  }
  rollTimer = setTimeout(roll, dials.ROLL_MS);

  return {
    screenTakeover,
    kill,
    /** True while a channel owns the glass. */
    live() { return !!live; },
    /** Which channel, by id, or null. Test/debug seam. */
    channel() { return live ? live.painter.id : null; },
    /** Where in the caught arc we are: null | caught | offer | reveal | declined. */
    stage() { return arc.open ? arc.stage : null; },
    /** Force one wheel tick (the suites drive time rather than waiting on it). */
    roll,
    /** True when a rAF is currently scheduled: the zero-cost-at-rest assertion. */
    rafLive() { return raf != null; },
    idleMs,
    /** A moment told us the screen changed hands: a class, a suspend, a report. */
    setScene(busy) {
      sceneBusy = !!busy;
      if (sceneBusy && live) kill('scene');
    },
    /** A say/chain took the glass. widget.js's `cancelChain()` is the one caller. */
    preempt() {
      if (live) kill('say');
    },
    /**
     * ONE-SHOT: the press that CAUGHT her must not also land as a pet. Read
     * (and cleared) by widget.js's `pet()`; false is the normal answer, so a
     * pet on an ordinary day is byte-for-byte the pet it always was.
     */
    swallowPet() {
      if (!swallowPet) return false;
      swallowPet = false;
      return true;
    },
    /** THE WRONG CHANNEL rides the EXIT of a rolled channel, never the wheel. */
    intrude,
    counters() { return Object.assign({}, counters); },
    /** Test seam: the live cooldown table. */
    cooldowns() { return Object.assign({}, cooldowns); },
    destroy() {
      destroyed = true;
      killTimers();
      if (rollTimer !== null) { clearTimeout(rollTimer); rollTimer = null; }
      if (arc.timer !== null) { clearTimeout(arc.timer); arc.timer = null; }
      stopRaf();
      if (live) { try { live.painter.end(g, live.spec, 'destroy'); } catch (e) { /* noop */ } live = null; }
      arcEnd();
      showGlass(false);
      if (o.media && typeof o.media.destroy === 'function') { try { o.media.destroy(); } catch (e) { /* noop */ } }
      if (doc && doc.removeEventListener) {
        doc.removeEventListener('pointerdown', onPointerDown, true);
        doc.removeEventListener('keydown', onKey);
        doc.removeEventListener('pointermove', onMove);
        doc.removeEventListener('wheel', onMove);
        doc.removeEventListener('visibilitychange', onVis);
      }
      void win;
    },
  };
}

export { SL_DIALS, CHANNELS };
export default createDeck;
