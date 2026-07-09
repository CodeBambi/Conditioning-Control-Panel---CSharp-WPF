/* ============================================================================
 * lessons.js - the lessons engine + first-times, ported from ChaosLessons.cs /
 * ChaosLessonHooks.cs. Gameplay proofs that unlock Toybox purchases: the run
 * brain drops one-line calls in from its bubble/draft/wave seams, this class
 * turns them into progress. Progress accumulates locally during the run and
 * flushes over the bridge at loop boundaries + run end; a COMPLETION persists
 * immediately (lesson-progress {complete:true}) and fires onComplete so the
 * run can announce + bark. First-times bank through the first-time op (the
 * amount table is C#-owned).
 * ==========================================================================*/

import { LESSONS, lessonById, LESSONLESS, FIRST_TIMES } from './catalog.js';

// ---- tunables (ChaosLessonHooks.cs, one block) ----
const VIBE_WINDOW_SEC = 5.0;
const CHAIN_BURST_LIFETIME_SEC = 0.40;
const CHAIN_OVERLAP_RADIUS_PX = 170;
const CHAIN_MAX_BURSTS = 64;
const PULL_REST_MAX_PX = 3.0;
const PULL_SAMPLE_MIN_AGE_SEC = 0.15;
const PULL_SAMPLE_MAX_AGE_SEC = 0.60;
const LAST_BREATH_BRINK_SEC = 0.8;
// blindfold: estimated on-screen lifetime per payload family
const BUSY_SEC = {
  flash: 1.5, subliminal: 1.2, overlay: 3.0, bambifreeze: 3.0,
  bouncingtext: 3.0, video: 15.0, gifcascade: 8.0, htlink: 8.0,
};

export function createLessonTracker({ bridge, onComplete, onFirstTime }) {
  // ---- lifetime state (seeded per run from the meta snapshot) ----
  let prog = new Map();        // id -> value (monotonic)
  let complete = new Set();
  let awarded = new Set();     // first-times banked
  let dirty = new Set();
  let allowCurses = true;

  // ---- run-scoped trackers ----
  let vibePops = [];                       // timestamps (s)
  let bursts = [];                         // {x, y, t}
  let cursorSample = null, cursorSamplePrev = null;   // {x, y, t}
  let lastChannelTotal = 0, channelFraction = 0;
  let busyUntil = 0;                       // blindfold screen-busy window (s, perf clock)
  let loopDefuses = 0, loopDirty = false;
  let curX = 0, curY = 0, haveCursor = false;
  let isCoveredProbe = null;   // run brain: "a video cover / heavy effect is live"

  const now = () => performance.now() / 1000;

  const onMove = (e) => { curX = e.clientX; curY = e.clientY; haveCursor = true; };
  window.addEventListener('pointermove', onMove, { passive: true });

  function seed(meta, cursesOn) {
    const m = meta || {};
    prog = new Map(Object.entries(m.lessonProgress || {}).map(([k, v]) => [k, v | 0]));
    complete = new Set(m.lessonsComplete || []);
    awarded = new Set(m.firstTimesAwarded || []);
    dirty.clear();
    allowCurses = cursesOn !== false;
    vibePops = []; bursts = [];
    cursorSample = cursorSamplePrev = null;
    lastChannelTotal = 0; channelFraction = 0;
    busyUntil = 0; loopDefuses = 0; loopDirty = false;
  }

  const isComplete = (id) => !lessonById(id) || LESSONLESS.has(id) || complete.has(id);

  function setProgress(def, value) {
    prog.set(def.id, value);
    dirty.add(def.id);
    if (value < def.target) return;
    if (complete.has(def.id)) return;
    complete.add(def.id);
    dirty.delete(def.id);
    bridge.send({ type: 'meta-command', op: 'lesson-progress', id: def.id, value, complete: true });
    bridge.send({ type: 'bark', event: 'lesson-complete', id: def.id });
    try { if (onComplete) onComplete(def.id); } catch (e) { /* never hurt the run */ }
  }

  function tick(id, amount = 1) {
    const def = lessonById(id);
    if (!def || amount <= 0 || isComplete(id)) return;
    setProgress(def, Math.min(def.target, (prog.get(id) | 0) + amount));
  }

  function raiseTo(id, value) {
    const def = lessonById(id);
    if (!def || isComplete(id)) return;
    if (value <= (prog.get(id) | 0)) return;
    setProgress(def, Math.min(def.target, value));
  }

  /** Push accumulated (non-complete) progress over the bridge - loop + run end. */
  function flush() {
    for (const id of dirty) {
      bridge.send({ type: 'meta-command', op: 'lesson-progress', id, value: prog.get(id) | 0 });
    }
    dirty.clear();
  }

  // ============================ first-times ============================

  function firstTime(id) {
    const def = FIRST_TIMES[id];
    if (!def || awarded.has(id)) return;
    awarded.add(id);
    bridge.send({ type: 'meta-command', op: 'first-time', id });
    try { if (onFirstTime) onFirstTime(id, def.amount, def.label); } catch (e) { /* ignore */ }
  }

  // ============================ screen-busy (blindfold) ============================

  function registerScreenBusy(kind) {
    if (isComplete('blindfold')) return;
    const sec = BUSY_SEC[String(kind || '').toLowerCase()] || 0;
    if (sec <= 0) return;
    const until = now() + sec;
    if (until > busyUntil) busyUntil = until;
  }

  // ============================ cursor rest (the_pull) ============================

  /** Called from the 250ms run tick - keeps two samples old enough to witness movement. */
  function sampleCursor() {
    if (isComplete('the_pull') || !haveCursor) return;
    cursorSamplePrev = cursorSample;
    cursorSample = { x: curX, y: curY, t: now() };
  }

  function cursorAtRest() {
    if (!haveCursor) return false;
    const t = now();
    for (const s of [cursorSample, cursorSamplePrev]) {
      if (!s) continue;
      const age = t - s.t;
      if (age < PULL_SAMPLE_MIN_AGE_SEC || age > PULL_SAMPLE_MAX_AGE_SEC) continue;
      const dx = curX - s.x, dy = curY - s.y;
      return dx * dx + dy * dy <= PULL_REST_MAX_PX * PULL_REST_MAX_PX;
    }
    return false;
  }

  // ============================ the hook surface ============================

  return {
    seed, flush, tick, raiseTo, firstTime,
    isComplete,
    progress: (id) => prog.get(id) | 0,

    /** An ordinary treat popped (golden/heart/droplet/prism route elsewhere). */
    onTreatPopped(spec, x, y) {
      const t = now();
      registerScreenBusy(spec.payload && spec.payload.kind);
      firstTime('first_taste');
      if (spec.variantId === 'subliminal') tick('intrusive_thoughts');

      if (!isComplete('vibe_popping')) {
        vibePops.push(t);
        while (vibePops.length && t - vibePops[0] > VIBE_WINDOW_SEC) vibePops.shift();
        raiseTo('vibe_popping', vibePops.length);
      } else if (vibePops.length) vibePops = [];

      if (!isComplete('chain_reaction') && x != null) {
        let pairs = 0;
        for (let i = bursts.length - 1; i >= 0; i--) {
          if (t - bursts[i].t > CHAIN_BURST_LIFETIME_SEC) { bursts.splice(i, 1); continue; }
          const dx = bursts[i].x - x, dy = bursts[i].y - y;
          if (dx * dx + dy * dy <= CHAIN_OVERLAP_RADIUS_PX * CHAIN_OVERLAP_RADIUS_PX) pairs++;
        }
        if (pairs > 0) tick('chain_reaction', pairs);
        if (bursts.length >= CHAIN_MAX_BURSTS) bursts.shift();
        bursts.push({ x, y, t });
      } else if (bursts.length) bursts = [];

      if (!isComplete('the_pull') && cursorAtRest()) tick('the_pull');
    },

    onSpecialPopped() { firstTime('first_taste'); },
    onPrismPopped() { tick('taking_chances'); },
    onRabbitCaught() { tick('rabbit_caller'); },
    /** First smack on a rabbit (Spanker worn - catching is impossible then). */
    onRabbitSmacked(firstSmack) { if (firstSmack) tick('rabbit_caller'); },
    onFreezeCaught() { tick('freeze_trigger'); },

    /** Any defuse landed. fuseSecLeft = the channel-start fuse (the grip holds it). */
    onDefuseCompleted(fuseSecLeft, viaChannel) {
      firstTime('first_snap');
      if (viaChannel && fuseSecLeft <= LAST_BREATH_BRINK_SEC) tick('last_breath');
      loopDefuses++;
      raiseTo('snap_field', loopDefuses);
      if (!isComplete('blindfold') && (now() < busyUntil || (isCoveredProbe && isCoveredProbe()))) tick('blindfold');
    },

    onDetonation() { loopDirty = true; },

    onLoopCompleted() {
      if (!loopDirty) tick('silk_touch');
      loopDirty = false;
      loopDefuses = 0;
      flush();
    },

    /** ranFullCourse = the clock ran out (an early surface teaches nothing here). */
    onRunCompleted(shieldsLeft, difficulty, ranFullCourse) {
      if (ranFullCourse) {
        if (!loopDirty) tick('silk_touch');       // the final loop ends at the buzzer
        if (shieldsLeft > 0) tick('popup_notification');
        if (difficulty === 'Hard' || difficulty === 'Extreme') tick('extreme_tier');
      }
      loopDirty = false; loopDefuses = 0;
      flush();
    },

    onDraftCardTaken(isSin) {
      tick('draft4');
      firstTime('first_whisper');
      if (isSin) { tick('surrender'); firstTime('first_yes'); }
    },

    onToyFired() { firstTime('first_play'); },

    /** A video payload ran its whole slice (cover lifted while the run lives). */
    onVideoEndured() { tick('porn_dvd'); },

    /** A payload fired (detonations + prisms) - feeds the blindfold busy window. */
    onPayloadFired(kind) { registerScreenBusy(kind); },

    /** slow_fuses + nothing else: whole seconds of channel time, fraction carried.
     * Driven from the run tick with the field's monotonic channelSeconds total. */
    noteChannelTotal(totalSec) {
      const delta = totalSec - lastChannelTotal;
      lastChannelTotal = totalSec;
      if (delta <= 0 || isComplete('slow_fuses')) return;
      channelFraction += delta;
      const whole = Math.floor(channelFraction);
      if (whole > 0) {
        channelFraction -= whole;
        tick('slow_fuses', whole);
      }
    },

    sampleCursor,

    setCoveredProbe(fn) { isCoveredProbe = fn; },

    dispose() { window.removeEventListener('pointermove', onMove); },
  };
}
