/* ============================================================================
 * exec/brainDrain.js — GoonElement.BrainDrain (6) + GoonPayloadKind.BrainDrain (6).
 *
 * The heavy, ported from DtRH's payloadFx.showBraindrain + .sf-pfx-drain: ONE
 * full-window veil on #gg-fx-drain that dims to near-black, blurs everything
 * behind it, and carries a faint random image from the player's own pool washed
 * over the top — dimmed and part-desaturated by fx.css so it reads as DRAINED,
 * not as a slideshow, but still IN COLOUR (owner call 2026-08-03; the wash used
 * to blend at `luminosity` and came out black and white). Intensity moves one
 * dial, the veil's opacity, across DtRH's exact 0.35..0.62 band.
 *
 * SAFETY, and it is not negotiable: this layer is z30 and the mercy button is
 * z60 with `isolation:isolate` (goon.css's DO-NOT-TOUCH block). The veil can
 * never cover the way out, and nothing in this file may raise a z-index or reach
 * outside its own layer to "cover everything" — the layer IS the cover.
 *
 * COST: backdrop-filter is the most expensive thing this page can draw. Exactly
 * ONE pane exists at a time — the payload does not stack a second one, it raises
 * the running one and puts it back afterwards — and the element and the payload
 * share it through a small dial stack.
 *
 * THE LITE TIER'S SHARE (2026-08-05, second mobile pass). PR #129 already took
 * the backdrop-filter away on phones (fx.css); what was LEFT per-frame here was
 * the WASH ITSELF whenever the pool dealt an animated GIF — a fullscreen,
 * cover-sized animation repainting behind a two-layer blend stack for 7-11s at
 * a time, which on an iPhone is a spiral-sized cost hiding in a "static" veil.
 * So on lite the wash PREFERS A STILL (media.js drawStillImage — a preference,
 * never a filter: an all-GIF library still gets its GIF), and the slow re-pick
 * holds its current image while the load governor says a payload squall is on
 * (exec/loadGovernor.js) — a burst is the worst possible moment to hand the
 * compositor a fresh fullscreen decode. The full tier calls neither.
 *
 * WHOSE PICTURE IS IN THE WASH (2026-08-05, owner play-testing r12: "check IF we
 * actually send our gifs as the braindrain — right now it seems like we are using
 * the local assets of the receiver"). He was right, and the audit answer is worth
 * writing down because nothing was broken: a BrainDrain payload is NOT in
 * net/mediaQueue.js's XFER_KINDS, so it has never carried an `xfer:` tag, and this
 * file only ever asked `media.drawKind('image')` — the receiver's own deck. Every
 * layer worked; the veil simply never asked for their file.
 *
 * So the wash now climbs the SAME peer-first ladder exec/flashes.js has run since
 * PR #126, and for the same reason ("the attacks are a transfer, every time"):
 *
 *   1. an exact artifact named by an `xfer:` tag  (media.drawFor)
 *   2. anything else the opponent has landed this match, on the received store's
 *      least-recently-shown rotation           (media.drawReceived)
 *   3. and only then the receiver's own library (media.drawKind / drawStillImage)
 *
 * RUNG 2 IS THE ONE THAT ACTUALLY FIXES THE REPORT, and it is why this is an exec/
 * change and not a net/ one. A drain runs 45s and re-picks every 7-11s, so it wants
 * four to six pictures; one tag could only ever have paid for the first. Adding
 * BrainDrain to XFER_KINDS would make rung 1 reachable and is a reasonable follow-up,
 * but it is not the fix — the rotation is, and it needs no tags at all.
 *
 * THE LADDER IS PAYLOAD-ONLY. An ELEMENT cue (the ramp's own bed) has no attacker,
 * so it stays on rung 3 forever — which is also the invariant media.js's header
 * promises: draw()/drawKind() may never surface a peer file, and their media appears
 * exactly where their payload asked for it and nowhere else.
 *
 * THE LITE TIER STILL GETS ITS PREFERENCE, on whichever rung answered: `preferStill`
 * narrows the peer rotation the same way drawStillImage narrows the deck. A
 * preference, never a filter — an all-gif opponent still washes their gif, because
 * a phone frame is worth less than the owner's whole point about the transfer lane.
 *
 * ONE PAYLOAD AT A TIME, AND THE NEW ONE WINS INSTANTLY (2026-08-05, owner: "we just
 * instantly swap whats active for whatever's next immediately, should be easier on
 * the performances"). See renderPayload.
 *
 * Uniform renderer shape — see the banner in exec/flashes.js.
 * ==========================================================================*/

import { perfLite } from './perfTier.js';
import { drawStillImage, XFER_TAG_PREFIX } from './media.js';
import { governorBusy } from './loadGovernor.js';

const WASH_MIN_MS = 7000;    // how long one washed image holds before a re-pick
const WASH_MAX_MS = 11000;

const clamp01 = (n) => (typeof n === 'number' && n === n ? (n < 0 ? 0 : n > 1 ? 1 : n) : 0);
const lerp = (a, b, t) => a + (b - a) * clamp01(t);
const rand = (a, b) => a + Math.random() * (b - a);
const soon = (fn, ms) => {
  const t = setTimeout(fn, Math.max(0, ms | 0));
  if (t && typeof t.unref === 'function') t.unref();
  return t;
};
const reducedMotion = () => {
  try { return typeof matchMedia === 'function' && matchMedia('(prefers-reduced-motion: reduce)').matches; }
  catch (_e) { return false; }
};

/**
 * The `xfer:` tags one payload carries, as a spendable list (empty for every payload
 * that has none, which today is every BrainDrain — see the banner). Mirrored locally
 * rather than imported from exec/flashes.js or net/: exec/ modules do not reach into
 * net/, and one four-line reader is cheaper than a dependency between two renderers.
 */
export function xferTags(payload) {
  const raw = (payload && Array.isArray(payload.tags)) ? payload.tags : null;
  if (!raw) return [];
  const out = [];
  for (const t of raw) if (typeof t === 'string' && t.startsWith(XFER_TAG_PREFIX)) out.push(t);
  return out;
}

/** Cue intensity -> the veil dial. The band is DtRH's scaleD(0.35, 0.62, strength). */
export function drainTuning(intensity, calm) {
  const i = clamp01(intensity);
  return {
    opacity: +lerp(0.35, 0.62, i).toFixed(3),
    fadeMs: calm ? 1400 : 900,
  };
}

export function createBrainDrain({ layers, media, audio, logger } = {}) {
  const log = logger || null;
  const warn = (m) => { if (log && log.warn) log.warn(`[gg:braindrain] ${m}`); };
  const calm = reducedMotion();

  let veilEl = null;
  let washHandle = null;         // media handle behind the current wash image
  let washTimer = 0;             // the slow re-pick
  let elementIntensity = null;   // null = element not running
  let payloadIntensity = null;   // null = no payload running
  /**
   * THE ONE payload run that owns the veil right now, or null. Two jobs, and it is
   * the same object for both because they are the same fact:
   *   · it is the ANSWER to "is this veil an attack?" — repickWash climbs the
   *     peer-first ladder only while a run holds it, so an element bed cannot see
   *     peer media (media.js's header invariant);
   *   · it is the SWAP TOKEN — see renderPayload. A run only puts the dial down if
   *     it still owns it, so a run that has been swapped out settles into thin air
   *     instead of tearing down its successor's veil.
   */
  let payloadRun = null;

  const layer = () => (layers && typeof layers.get === 'function' ? layers.get('drain') : null);

  function releaseWash() {
    try { if (washHandle && washHandle.release) washHandle.release(); } catch (_e) { /* ignore */ }
    washHandle = null;
  }

  /**
   * Wash one image over the veil (no pool at all = plain dim).
   *
   * WHOSE image is the whole point — see the banner. While a PAYLOAD owns the veil
   * this is the attacker's picture if they have landed one: exact tag, then their
   * received store, then ours. An element bed skips straight to ours.
   *
   * The shape of the three rungs is copied deliberately from exec/flashes.js
   * showOne() (the `wantPeer && provenance !== 'peer'` re-draw included), because two
   * peer-first ladders that read differently are two ladders that drift apart.
   */
  function repickWash() {
    if (!veilEl || !media || typeof media.drawKind !== 'function') return;
    const run = payloadRun;
    const wantPeer = !!run;
    // LITE: prefer a still — a fullscreen animated wash is the one per-frame
    // cost this veil has left on a phone (see the banner). Full tier: the
    // exact draw it has always made. The preference now rides BOTH sources.
    const lite = perfLite();

    // 1. the exact artifact this payload named, one tag spent per re-pick (a 45s
    //    drain re-picks four to six times; flashes.js spends its tags the same way).
    const drawWith = (run && run.tags.length) ? { tags: [run.tags.shift()] } : null;
    let entry = (drawWith && typeof media.drawFor === 'function')
      ? media.drawFor('image', drawWith)
      : null;
    // 2. …else anything else they have landed this match. drawFor falls THROUGH to
    //    the local deck on a tag miss, so a non-peer answer here is a miss, not a hit.
    if (wantPeer && (!entry || entry.provenance !== 'peer') && typeof media.drawReceived === 'function') {
      entry = media.drawReceived('image', lite ? { preferStill: true } : undefined) || entry;
    }
    // 3. …and only now the receiver's own library — the last resort, not the default.
    if (!entry) entry = lite ? drawStillImage(media) : media.drawKind('image');
    if (!entry) return;
    const handle = (typeof media.acquire === 'function') ? media.acquire(entry) : null;
    if (!handle || !handle.url) return;
    releaseWash();
    washHandle = handle;
    // --gg-drain-img, never background-image: fx.css composes the wash UNDER a
    // dim sheet and a desaturating sheet in one background-image list, and an
    // inline background-image would replace all three with a bare slideshow.
    try { veilEl.style.setProperty('--gg-drain-img', `url("${handle.url}")`); }
    catch (_e) { /* a host without inline style support just keeps the flat dim */ }
  }

  function scheduleWash() {
    try { clearTimeout(washTimer); } catch (_e) { /* ignore */ }
    washTimer = soon(() => {
      if (!veilEl) return;
      // A lite-tier payload squall holds the CURRENT wash: skipping one slow
      // re-pick is invisible (the image was going to sit for 7-11s anyway),
      // and a fresh fullscreen decode mid-burst is not. governorBusy() is
      // false everywhere but a lite tier inside a hold, so the full tier's
      // cadence is untouched. The next tick re-picks as normal.
      if (!governorBusy()) repickWash();
      scheduleWash();
    }, Math.round(rand(WASH_MIN_MS, WASH_MAX_MS)));
  }

  function ensureNodes() {
    if (veilEl && veilEl.isConnected) return true;
    const host = layer();
    if (!host || typeof document === 'undefined') return false;
    veilEl = document.createElement('div');
    veilEl.className = 'gg-drain';
    host.appendChild(veilEl);
    repickWash();
    scheduleWash();
    return true;
  }

  function teardown() {
    try { clearTimeout(washTimer); } catch (_e) { /* ignore */ }
    washTimer = 0;
    if (!veilEl) return;
    const el = veilEl;
    el.classList.remove('is-on');
    soon(() => { try { el.remove(); } catch (_e) { /* ignore */ } }, 1000);
    veilEl = null;
    releaseWash();
  }

  /** The payload outranks the element; whichever is louder wins the dial. */
  function apply() {
    const want = (payloadIntensity === null && elementIntensity === null)
      ? null
      : Math.max(payloadIntensity === null ? 0 : payloadIntensity, elementIntensity === null ? 0 : elementIntensity);

    if (want === null) { teardown(); return; }
    if (!ensureNodes()) return;

    const tune = drainTuning(want, calm);
    veilEl.style.setProperty('--gg-drain-op', String(tune.opacity));
    veilEl.style.setProperty('--gg-drain-fade', `${tune.fadeMs}ms`);
    soon(() => { if (veilEl) veilEl.classList.add('is-on'); }, 16);
  }

  return {
    name: 'brainDrain',

    start(cue) {
      elementIntensity = clamp01(cue && cue.intensity);
      apply();
    },

    setIntensity(v) {
      if (elementIntensity === null) return;
      elementIntensity = clamp01(v);
      apply();
    },

    stop() {
      elementIntensity = null;
      apply();
    },

    /**
     * The once-per-match heavy: ride the veil up for duration_ms, then release.
     *
     * INSTANT SWAP, NEVER A BACKLOG (2026-08-05, owner: "we just instantly swap whats
     * active for whatever's next immediately, should be easier on the performances").
     *
     * WHAT IT USED TO DO, and why it looked like a queue. `payloadIntensity` is ONE
     * scalar and each call closed over its OWN endTimer, so two overlapping drains
     * shared a dial and kept two clocks: the second one's intensity stamped over the
     * first, then the FIRST one's timer expired and put the dial down — killing the
     * veil while the second payload still had thirty seconds of its own duration to
     * run, whose only remaining effect was a late second settle. A backlog of dead
     * durations, exactly as reported, and on a phone it is also a second fullscreen
     * decode inside the frame budget of the first.
     *
     * WHAT IT DOES NOW. `payloadRun` is a one-slot register. The arriving run takes
     * ownership BEFORE the outgoing one is settled, so:
     *   · the outgoing run's settle sees it no longer owns the dial, skips the
     *     teardown entirely, clears its OWN endTimer and reports `endured=false` —
     *     receipt `completed`, no charge, no refund, which is the semantic
     *     exec/executor.js already uses for "it ran and you did not take all of it";
     *   · the veil node, its wash timer and its backdrop-filter pane are NEVER torn
     *     down and re-created, so there is no blank frame and no second pane — the
     *     swap is one opacity write plus one repickWash();
     *   · the dead run's remaining duration dies with its timer. It cannot re-trigger.
     *
     * ELEMENT CUES ARE UNTOUCHED. The element and the payload still hold separate
     * dials and apply() still takes the louder — a swap between payloads cannot pull
     * the veil out from under a running ramp bed.
     */
    renderPayload(payload, done) {
      const p = payload || {};
      const runMs = Math.max(1000, (p.duration_ms | 0) || 45000);

      const run = {
        tags: xferTags(p),
        intensity: Math.max(0.55, clamp01(p.intensity !== undefined ? p.intensity : 0.8)),
        settle: null,
      };

      let finished = false;
      let endTimer = 0;
      const settle = (endured) => {
        if (finished) return;
        finished = true;
        try { clearTimeout(endTimer); } catch (_e) { /* ignore */ }
        // ONLY the owner may put the dial down. A run that was swapped out has
        // already handed the veil to its successor and must not tear it down.
        if (payloadRun === run) {
          payloadRun = null;
          payloadIntensity = null;
          apply();
        }
        if (typeof done === 'function') { try { done(endured); } catch (e) { warn(`done() threw: ${e && e.message}`); } }
      };
      run.settle = settle;

      // The swap itself. Ownership moves first, THEN the outgoing run is settled —
      // reversing those two lines is what would flash a blank frame.
      const prev = payloadRun;
      payloadRun = run;
      if (prev) { try { prev.settle(false); } catch (e) { warn(`swap settle threw: ${e && e.message}`); } }

      payloadIntensity = run.intensity;
      apply();
      repickWash();

      endTimer = soon(() => settle(true), runMs);
      return () => settle(false);
    },
  };
}

export default createBrainDrain;
