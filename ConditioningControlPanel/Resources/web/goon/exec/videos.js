/* ============================================================================
 * exec/videos.js — GoonElement.Videos (1) + GoonPayloadKind.Video (3).
 *
 * Plays clips from the PLAYER'S OWN pool on #gg-stage (z20 — under the HUD,
 * under the mercy button, over the screens). The sustained element chains one
 * clip after another for as long as the ramp keeps it up; the payload plays for
 * exactly duration_ms, looping a short source rather than ending early.
 *
 * Uniform renderer shape — see the banner in exec/flashes.js.
 *
 * SOUND: unmuted by default at a modest volume (this is a duel, the audio is
 * half the pressure), but Chromium's autoplay policy can still refuse a play()
 * with sound if the page has had no user gesture. That refusal is HANDLED, not
 * logged and dropped: we retry muted, so the picture never silently fails to
 * appear. The one thing we never do is give up on the visual.
 * ==========================================================================*/

const MAX_SOURCE_TRIES = 3;   // a broken entry costs one redraw, not the run

const clamp01 = (n) => (typeof n === 'number' && n === n ? (n < 0 ? 0 : n > 1 ? 1 : n) : 0);
const lerp = (a, b, t) => a + (b - a) * clamp01(t);
const soon = (fn, ms) => {
  const t = setTimeout(fn, Math.max(0, ms | 0));
  if (t && typeof t.unref === 'function') t.unref();
  return t;
};

/** Cue intensity -> playback volume. Modest on purpose: the HUD must stay audible. */
export function volumeFor(intensity) {
  return +lerp(0.12, 0.55, clamp01(intensity)).toFixed(3);
}

export function createVideos({ layers, media, audio, logger } = {}) {
  const log = logger || null;
  const warn = (m) => { if (log && log.warn) log.warn(`[gg:videos] ${m}`); };
  const info = (m) => { if (log && log.info) log.info(`[gg:videos] ${m}`); };

  let sustained = null;               // the chained element run
  const runs = new Set();             // every live run (element + payloads)

  const stage = () => (layers && typeof layers.get === 'function' ? layers.get('stage') : null);

  /**
   * One playing surface. `chain` keeps drawing a new clip when one ends;
   * `runMs` (payload) cuts the run at a hard deadline instead.
   */
  function createRun({ intensity, chain, runMs, onDone }) {
    const run = {
      alive: true, intensity: clamp01(intensity), chain: !!chain,
      wrap: null, video: null, handle: null, tries: 0, endTimer: 0, finished: false,
    };
    runs.add(run);

    const settle = (endured) => {
      if (run.finished) return;
      run.finished = true;
      run.alive = false;
      teardown();
      runs.delete(run);
      if (typeof onDone === 'function') { try { onDone(endured); } catch (e) { warn(`done() threw: ${e && e.message}`); } }
    };

    function releaseSource() {
      try { if (run.handle && run.handle.release) run.handle.release(); } catch (_e) { /* ignore */ }
      run.handle = null;
    }

    function teardown() {
      try { clearTimeout(run.endTimer); } catch (_e) { /* ignore */ }
      const v = run.video;
      if (v) {
        try { v.pause(); } catch (_e) { /* ignore */ }
        try { v.removeAttribute('src'); v.load(); } catch (_e) { /* ignore */ }
        v.onended = null; v.onerror = null;
      }
      releaseSource();
      const w = run.wrap;
      if (w) {
        w.classList.remove('is-on');
        soon(() => { try { w.remove(); } catch (_e) { /* ignore */ } }, 280);
      }
      run.wrap = null;
      run.video = null;
    }

    function mount() {
      if (typeof document === 'undefined') return false;
      const host = stage();
      if (!host) return false;
      const wrap = document.createElement('div');
      wrap.className = 'gg-vid-wrap';
      const video = document.createElement('video');
      video.className = 'gg-vid';
      video.playsInline = true;
      video.autoplay = true;
      video.controls = false;
      video.preload = 'auto';
      video.volume = volumeFor(run.intensity);
      wrap.appendChild(video);
      host.appendChild(wrap);
      soon(() => wrap.classList.add('is-on'), 16);
      run.wrap = wrap;
      run.video = video;
      return true;
    }

    /** Draw the next clip. Returns false when the pool has nothing playable. */
    function next() {
      if (!run.alive) return false;
      if (!run.video && !mount()) return false;
      if (!media || typeof media.drawKind !== 'function') return false;

      const entry = media.drawKind('video');
      if (!entry) return false;
      const handle = (typeof media.acquire === 'function') ? media.acquire(entry) : null;
      if (!handle || !handle.url) return false;

      releaseSource();
      run.handle = handle;

      const v = run.video;
      v.loop = !run.chain;          // payload: loop a short source until the deadline
      v.onerror = () => {
        run.tries++;
        if (run.tries >= MAX_SOURCE_TRIES) { onExhausted(); return; }
        warn(`source failed (${run.tries}/${MAX_SOURCE_TRIES}), redrawing`);
        if (!next()) onExhausted();
      };
      v.onended = () => {
        if (!run.alive) return;
        if (run.chain) { if (!next()) onExhausted(); }
        // non-chained runs are cut by their deadline, not by 'ended' (loop=true)
      };
      try { v.src = handle.url; } catch (_e) { return false; }

      const p = (typeof v.play === 'function') ? v.play() : null;
      if (p && typeof p.catch === 'function') {
        p.catch(() => {
          // Autoplay policy (no user gesture yet): keep the PICTURE, drop the sound.
          try { v.muted = true; const q = v.play(); if (q && q.catch) q.catch(() => { /* give up quietly */ }); }
          catch (_e) { /* ignore */ }
          info('autoplay refused with sound — playing muted');
        });
      }
      return true;
    }

    /**
     * The pool has no playable video. This is the RECEIVER'S library gap, not a
     * failure of the sender's payload, so the render is a silent no-op and the
     * receipt goes out as COMPLETED (endured=false: nothing was endured, so no
     * charge is awarded — and the sender is not refunded either, since only
     * rejected_* statuses refund).
     */
    function onExhausted() { settle(false); }

    run.settle = settle;
    run.begin = () => {
      if (!next()) { onExhausted(); return false; }
      if (runMs > 0) run.endTimer = soon(() => settle(true), runMs);
      return true;
    };
    run.retune = (v) => {
      run.intensity = clamp01(v);
      if (run.video) { try { run.video.volume = volumeFor(run.intensity); } catch (_e) { /* ignore */ } }
    };
    return run;
  }

  return {
    name: 'videos',

    start(cue) {
      const intensity = clamp01(cue && cue.intensity);
      if (sustained && sustained.alive) { sustained.retune(intensity); return; }
      sustained = createRun({ intensity, chain: true, runMs: 0, onDone: null });
      sustained.begin();
    },

    setIntensity(v) { if (sustained && sustained.alive) sustained.retune(v); },

    stop() {
      if (!sustained) return;
      sustained.settle(false);   // no receipt path on the element run (onDone is null)
      sustained = null;
    },

    /** One clip for duration_ms. Ran to the deadline = endured. */
    renderPayload(payload, done) {
      const p = payload || {};
      const runMs = Math.max(1000, (p.duration_ms | 0) || 30000);
      const run = createRun({
        intensity: p.intensity !== undefined ? p.intensity : 0.6,
        chain: false,
        runMs,
        onDone: done,
      });
      run.begin();
      return () => run.settle(false);
    },
  };
}

export default createVideos;
