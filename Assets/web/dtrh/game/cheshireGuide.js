/* ============================================================================
 * cheshireGuide.js - the Cheshire tutorial DIRECTOR + reactive router
 * (FTUE overhaul, 2026-07). cheshireVn.js is the mouth; this is the brain.
 *
 * WHAT IT OWNS
 *   - The scripted arc: hub_welcome -> run 1 -> r1_return -> run 2 ->
 *     r2_return -> run 3 -> graduation. Arc position is ONE persisted int,
 *     meta.tutorialStage (0..6; 6 = ARC_DONE), advanced climb-only over the
 *     bridge (set-num). happyPath.js stays the run-1 MECHANICAL rig (threat/
 *     draft/darter); this module owns every word spoken around it.
 *   - Lesson-card suppression while the arc runs: chaosRun's markDiscovered
 *     asks suppressLessons() first. Discoveries are ALWAYS recorded; beats
 *     carry covers:[codexId,...] so covered mechanics never card afterwards
 *     and uncovered ones still card post-arc.
 *   - The reactive layer: chaosRun tees bark(event) through onEvent(); a
 *     claimed event plays a Cheshire commentary line and the native bark
 *     stands down (no double voice). Once-lines persist via seenNarrativeLines
 *     ('cheshire:'-prefixed), pooled cooldowns via narrativeCooldownEnds.
 *   - The hub: supersedes hubGuide via the same {maybeStart, onInterrupt,
 *     isBusy} contract (warren.js unchanged). Old hubGuide is only built when
 *     CHESHIRE_DISABLED.
 *
 * SELF-HEAL (mid-rollout): an existing player (runsCompleted > 0) with
 * tutorialStage 0 and no forced replay pending is stamped ARC_DONE on the
 * first read, BEFORE any suppression decision - veterans keep their lesson
 * cards and never see the tutorial. reset-onboarding zeroes tutorialStage
 * AND sets forceScriptedRun, which exempts it from the heal -> full replay.
 *
 * Every entry point is try/caught: the guide must never hurt a run.
 * ==========================================================================*/

import { CHESHIRE } from '../assets/vn/cheshire_script.js';
import { S } from '../engine/settings.js';

export const TUTORIAL_STAGE = {
  NEW: 0,          // nothing seen
  ARRIVED: 1,      // hub_welcome played
  RUN1_DONE: 2,    // r1_return played
  RUN2_DONE: 3,    // r2_return played
  ARC_DONE: 6,     // graduation played (terminal; lessons re-enable)
};
const ARC_DONE = TUTORIAL_STAGE.ARC_DONE;

const AMBIENT_GAP_MS = 18000;   // min gap between reactive commentary claims
const PFX = 'cheshire:';        // namespaces our rows in the shared narrative stores

export function createCheshireGuide(opts) {
  const vn = opts.vn;
  const bridge = opts.bridge;
  // Shared narration cooldown (owned by chaosRun): VN/VO and lesson cards take turns.
  // Safe no-op default so the CHESHIRE_DISABLED path / older callers still work.
  const narration = opts.narration || { ready: () => true, stamp: () => {} };
  const getMeta = opts.getMeta || (() => null);
  const isFieldHeld = opts.isFieldHeld || (() => false);
  const isDriftSpeaking = opts.isDriftSpeaking || (() => false);
  const coverDiscovered = opts.coverDiscovered || (() => {});
  const log = opts.log || (() => {});
  const script = opts.script || CHESHIRE;

  // ---- arc state ----
  let stage = -1;          // -1 = not yet read from meta
  let hubRunning = false;  // a hub scene sequence is in flight
  let cancelToken = 0;
  let warrenTut = null;    // warren.tutorial handle, attached post-construction (hub.attachWarren)
  const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

  // ---- per-run schedule state ----
  let schedule = null;     // the active run's beat list (script.runs[n])
  const fired = new Set(); // beat ids fired this run

  // ---- reactive state (seeded lazily from the meta snapshot) ----
  let seeded = false;
  const seenLines = new Set();       // line ids (unprefixed) that must never repeat
  const cooldowns = new Map();       // pool key -> epoch ms it may fire again
  let lastSayAt = 0;                 // ambient-gap throttle

  const now = () => Date.now();

  function meta() { const m = getMeta(); return m || null; }

  /** Read stage + reactive stores off the snapshot once, self-heal veterans. */
  function ensureInit() {
    const m = meta();
    if (!m) return false;
    if (stage < 0) {
      stage = m.tutorialStage | 0;
      // Mid-rollout self-heal: an existing save predates the tutorial. Stamp it
      // done BEFORE any suppression decision so veterans keep lesson cards.
      if (stage === 0 && (m.runsCompleted | 0) > 0 && !m.forceScriptedRun) {
        stage = ARC_DONE;
        persistStage(ARC_DONE);
        log('cheshire: self-heal, existing save -> ARC_DONE');
      }
    }
    if (!seeded) {
      seeded = true;
      try {
        for (const id of (m.seenNarrativeLines || [])) {
          if (typeof id === 'string' && id.startsWith(PFX)) seenLines.add(id.slice(PFX.length));
        }
        const cds = m.narrativeCooldownEnds || {};
        for (const k of Object.keys(cds)) {
          if (k.startsWith(PFX)) cooldowns.set(k.slice(PFX.length), cds[k]);
        }
      } catch (e) { /* ignore */ }
    }
    return true;
  }

  function persistStage(n) {
    try { bridge.send({ type: 'meta-command', op: 'set-num', key: 'tutorialStage', value: n }); } catch (e) { /* ignore */ }
  }
  function setStage(n) {
    if (n <= stage) return;
    stage = n;
    persistStage(n);
  }
  function markSeen(lineId) {
    if (seenLines.has(lineId)) return;
    seenLines.add(lineId);
    try { bridge.send({ type: 'meta-command', op: 'add-to-set', set: 'seenNarrativeLines', id: PFX + lineId }); } catch (e) { /* ignore */ }
  }
  function setCooldown(key, ms) {
    const ends = now() + ms;
    cooldowns.set(key, ends);
    try { bridge.send({ type: 'meta-command', op: 'map-set', map: 'narrativeCooldownEnds', id: PFX + key, value: ends }); } catch (e) { /* ignore */ }
  }
  const onCooldown = (key) => (cooldowns.get(key) || 0) > now();
  const pick = (arr) => (arr && arr.length ? arr[Math.floor(Math.random() * arr.length)] : null);

  /** Fire one scheduled arc beat (say or overlay) + stamp its covers. */
  function fireBeat(b) {
    fired.add(b.id);
    if (b.covers) { for (const c of b.covers) { try { coverDiscovered(c); } catch (e) { /* ignore */ } } }
    try {
      if (b.mode === 'overlay') vn.overlay(b);
      else vn.say(b);
      lastSayAt = now();
      narration.stamp();   // a lesson landing right after a beat backs off for the window
    } catch (e) { log('cheshire: fireBeat ' + b.id + ': ' + (e && e.message)); }
  }

  // Stamp a SCENE's covers when it completes (skips included - flags were
  // read; a cancelled scene keeps its remaining covers for the card system).
  function coversOf(beats, upToIndexExclusive) {
    const out = [];
    const n = upToIndexExclusive == null ? beats.length : upToIndexExclusive;
    for (let i = 0; i < n; i++) if (beats[i].covers) out.push(...beats[i].covers);
    return out;
  }
  function stampCovers(beats) {
    for (const c of coversOf(beats)) { try { coverDiscovered(c); } catch (e) { /* ignore */ } }
  }

  /* ======================== run-side hooks (chaosRun) ======================== */

  /** beginCountdown: pick the arc schedule for this descent (or none). */
  function onRunStarted(cfg) {
    try {
      ensureInit();
      fired.clear();
      schedule = null;
      if (!cfg) return;
      if (cfg.scriptedFirstRun) { schedule = script.runs[1] || null; return; }
      if (stage >= ARC_DONE || stage < 1) return;
      // stage 1/2 -> the "Deeper" run, stage 3 -> the sparse run 3. Keyed off the
      // ARC position (not runsCompleted) so a reset-onboarding replay works for
      // veterans whose runsCompleted is huge.
      schedule = (stage <= 2 ? script.runs[2] : script.runs[3]) || null;
    } catch (e) { log('cheshire: onRunStarted: ' + (e && e.message)); }
  }

  function onRunEnded() {
    try { schedule = null; fired.clear(); vn.cancel(); } catch (e) { /* ignore */ }
  }

  /** 250ms run tick. io = { frac (elapsed/duration 0..1), wave }. */
  function tick(io) {
    try {
      if (S.hideTutorial) return;   // guide silenced: no scheduled beats
      if (!schedule || !io) return;
      if (vn.isHolding() || isFieldHeld()) return;   // beats wait out any hold
      for (const b of schedule) {
        if (fired.has(b.id) || !b.at) continue;
        const hit = (b.at.progress != null && io.frac >= b.at.progress)
                 || (b.at.wave != null && io.wave >= b.at.wave);
        if (hit) { fireBeat(b); break; }             // one per tick, no dogpiles
      }
    } catch (e) { log('cheshire: tick: ' + (e && e.message)); }
  }

  /** The bark tee. Returns true when the Cheshire claims the event (the native
   * bark then stands down). Arc event-beats claim first; post-arc the reactive
   * pools take over, throttled + persisted. */
  function onEvent(event, data) {
    try {
      if (S.hideTutorial) return false;   // guide silenced: native bark keeps the event
      if (!ensureInit()) return false;
      // arc: this run's schedule may be waiting on the event
      if (schedule) {
        const b = schedule.find((x) => !fired.has(x.id) && x.at && x.at.event === event);
        if (b) {
          if (b.mode === 'overlay' && (vn.isHolding() || isFieldHeld())) return false;  // never stack holds
          fireBeat(b);
          return true;
        }
        return false;   // while the arc runs, unclaimed events keep their native bark
      }
      if (stage < ARC_DONE) return false;
      const pool = script.reactive && script.reactive.events && script.reactive.events[event];
      if (!pool || !pool.lines) return false;
      if (vn.isBusy() || isDriftSpeaking()) return false;
      if (!narration.ready()) return false;   // shared gate: a lesson/VO just spoke
      if (now() - lastSayAt < AMBIENT_GAP_MS) return false;
      if (onCooldown('e:' + event)) return false;
      if (Math.random() >= (pool.chance != null ? pool.chance : 1)) return false;
      const line = pick(pool.lines.filter((l) => !l.once || !seenLines.has(l.id)));
      if (!line) return false;
      vn.say(line);
      lastSayAt = now();
      narration.stamp();
      if (line.once) markSeen(line.id);
      setCooldown('e:' + event, pool.cd || 60000);
      return true;
    } catch (e) { log('cheshire: onEvent ' + event + ': ' + (e && e.message)); return false; }
  }

  /** markDiscovered's first gate: while the arc runs, NO lesson card fires
   * (discoveries are still recorded upstream; beats' covers close the gaps). */
  function suppressLessons() {
    try {
      if (S.hideTutorial) return false;   // guide silenced: let the plain lesson cards teach
      ensureInit(); return stage >= 0 && stage < ARC_DONE;
    } catch (e) { return false; }
  }

  /** markDiscovered's second gate (post-arc): a first-discovery the Cheshire
   * has lines for becomes HER held card (line + the codex desc as sub-copy)
   * instead of the plain lesson card. Returns true when claimed. */
  function handleDiscovery(codexId, copy) {
    try {
      if (S.hideTutorial) return false;   // guide silenced: fall through to the plain lesson card
      if (!ensureInit()) return false;
      if (stage < ARC_DONE) return false;   // arc: suppressLessons already ate it
      const pool = script.reactive && script.reactive.discovery && script.reactive.discovery[codexId];
      if (!pool || !pool.length) return false;
      if (vn.isHolding()) return false;     // another held beat owns the stage: let the card queue instead
      const line = pick(pool);
      vn.overlay({ ...line, sub: line.sub || (copy && copy.desc) || '' });
      lastSayAt = now();
      narration.stamp();
      return true;
    } catch (e) { log('cheshire: handleDiscovery: ' + (e && e.message)); return false; }
  }

  /** hpIo.vnBeat reroute (happyPath's scripted-run beats). intro1 becomes the
   * run-1 opening scene, intro2 is folded into it, outro1 is covered by the
   * scheduled r113 commentary - both resolve immediately. */
  function happyBeat(beatId) {
    try {
      if (S.hideTutorial) return Promise.resolve(false);   // guide silenced: no run-1 opening scene
      ensureInit();
      if (beatId === 'intro1') {
        const beats = script.scenes && script.scenes.r1_open;
        if (!beats || !beats.length) return Promise.resolve(false);
        return vn.scene(beats).then((done) => { if (done) stampCovers(beats); return done; });
      }
      if (beatId === 'intro2' || beatId === 'outro1') return Promise.resolve(true);
      return Promise.resolve(false);
    } catch (e) { return Promise.resolve(false); }
  }

  /* ======================== the hub (warren's guide contract) ======================== */

  /** A card-tour beat (data field `demo:{card, pane|panes, dwell?}`): fade the
   * scene out, open the REAL warren card, pulse the pane(s) the line names,
   * dwell, then close + fade the scene back. Skips gracefully when warren isn't
   * ready or the pane isn't unlocked ("demo each when unlocked"). */
  async function demoBeat(beat, ctrl) {
    const d = beat && beat.demo;
    const wt = warrenTut;
    // skipped (warren not ready / card still locked) -> false: the scene falls
    // back to normal click-to-advance instead of auto-skipping the beat.
    if (!d || !wt || !d.card || !wt.canOpen(d.card)) return false;
    try {
      await ctrl.cutawayOut();                         // fade the scene away
      if (!ctrl.alive() || !wt.canOpen(d.card)) return true;
      wt.open(d.card);                                 // simulate the user click
      const panes = d.panes || (d.pane ? [d.pane] : []);
      const dwell = d.dwell || 2600;
      for (const p of panes) { if (wt.hasPane(p)) wt.highlightPane(p, dwell); }  // skip locked panes
      await sleep(dwell);
    } catch (e) { log('cheshire: demoBeat ' + (beat && beat.id) + ': ' + (e && e.message)); }
    finally {
      try { wt.close(); } catch (e) { /* ignore */ }
      try { await ctrl.cutawayIn(); } catch (e) { /* ignore */ }   // fade the scene back
    }
    return true;
  }

  async function playHubScene(key, nextStage, flashPass) {
    hubRunning = true;
    const t = ++cancelToken;
    // Stage first (openIntro's pattern): the one-way write makes re-entry
    // impossible even if a snapshot lands mid-sequence.
    setStage(nextStage);
    log('cheshire: hub scene ' + key);
    try {
      const beats = script.scenes && script.scenes[key];
      if (beats && beats.length) {
        const done = await vn.scene(beats, { onBeat: demoBeat });
        // covers stamp even on a skip-out: she said enough, and the ledger
        // write is what keeps the card system from re-teaching it
        stampCovers(beats);
        if (t !== cancelToken) return;
        if (done && flashPass) flashPass();
      } else if (flashPass) flashPass();
    } catch (e) { log('cheshire: hub scene ' + key + ': ' + (e && e.message)); }
    finally { if (t === cancelToken) hubRunning = false; }
  }

  function maybeAmbient(v) {
    try {
      if (stage < ARC_DONE) return;
      const hubPools = script.reactive && script.reactive.hub;
      if (!hubPools) return;
      if (vn.isBusy()) return;
      // one greeting per long stretch (persisted), else the occasional idle line
      const g = hubPools.greeting;
      if (g && !onCooldown('h:greeting') && Math.random() < (g.chance != null ? g.chance : 1)) {
        const line = pick(g.lines);
        if (line) { vn.say(line); lastSayAt = now(); setCooldown('h:greeting', g.cd || 21600000); }
        return;
      }
      const idle = hubPools.idle;
      if (idle && !onCooldown('h:idle') && Math.random() < (idle.chance != null ? idle.chance : 1)) {
        const line = pick(idle.lines);
        if (line) { vn.say(line); lastSayAt = now(); setCooldown('h:idle', idle.cd || 300000); }
      }
    } catch (e) { /* ignore */ }
  }

  const hub = {
    /** warren.show()/refresh(). Returns true when the guide takes ownership of
     * this render's reveal-flash pass (it runs io.flashPass itself, ordered
     * after its scene). Contract-identical to the old hubGuide. */
    maybeStart(v, io) {
      try {
        if (S.hideTutorial) return false;   // guide silenced: no hub scenes / ambient lines
        if (!v || hubRunning || !ensureInit()) return false;
        const flashPass = io && io.flashPass;
        if (stage === TUTORIAL_STAGE.NEW) {
          // keep the legacy flag in step so a later kill-switch flip can't replay the old welcome
          try { bridge.send({ type: 'meta-command', op: 'set-flag', key: 'seenWarrenWelcome' }); } catch (e) { /* ignore */ }
          playHubScene('hub_welcome', TUTORIAL_STAGE.ARRIVED, null);
          return false;
        }
        if (stage === TUTORIAL_STAGE.ARRIVED && v.runs >= 1) {
          try { bridge.send({ type: 'meta-command', op: 'set-flag', key: 'seenFirstReturn' }); } catch (e) { /* ignore */ }
          playHubScene('r1_return', TUTORIAL_STAGE.RUN1_DONE, flashPass);
          return true;   // the toybox/dials reveal flash runs AFTER her scene
        }
        if (stage === TUTORIAL_STAGE.RUN1_DONE && v.runs >= 2) {
          playHubScene('r2_return', TUTORIAL_STAGE.RUN2_DONE, null);
          return false;
        }
        if (stage === TUTORIAL_STAGE.RUN2_DONE && v.runs >= 3) {
          playHubScene('graduation', ARC_DONE, null);
          return false;
        }
        // THE BOUDOIR tour (crafting Part 2): once-ever, after the arc, the
        // moment the station exists (runs >= 5). Flag first (openIntro's
        // pattern) so a mid-scene snapshot can't re-enter.
        if (stage >= ARC_DONE && v.runs >= 5 && !v.seenBoudoirIntro
            && warrenTut && warrenTut.canOpen('boudoir')) {
          try { bridge.send({ type: 'meta-command', op: 'set-flag', key: 'seenBoudoirIntro' }); } catch (e) { /* ignore */ }
          playHubScene('boudoir_intro', stage, flashPass);
          return true;   // the boudoir reveal flash runs AFTER her tour
        }
        maybeAmbient(v);
        return false;
      } catch (e) { log('cheshire: maybeStart: ' + (e && e.message)); return false; }
    },
    /** A station/portal click or Esc mid-sequence: drop the remaining beats.
     * Stage was set up-front, so nothing re-fires. */
    onInterrupt() { cancelToken++; hubRunning = false; try { vn.cancel(); } catch (e) { /* ignore */ } },
    isBusy: () => hubRunning,
    /** warren hands its tutorial surface here post-construction (guide is built
     * before warren), so card-tour beats can open/highlight the real cards. */
    attachWarren(t) { warrenTut = t || null; },
    dispose() { cancelToken++; hubRunning = false; },
  };

  /** resetOnboarding's direct signal (no snapshot-sniffing races): rewind the
   * arc to NEW and drop the local once-line/cooldown mirrors - the C# side
   * purged the persisted 'cheshire:' rows in the same command. */
  function onReset() {
    stage = TUTORIAL_STAGE.NEW;
    seenLines.clear();
    cooldowns.clear();
    schedule = null;
    fired.clear();
    log('cheshire: onboarding reset -> arc rewound');
  }

  const api = {
    onRunStarted, onRunEnded, tick, onEvent,
    suppressLessons, handleDiscovery, happyBeat, onReset,
    hub,
    stage: () => stage,
    dispose() { hub.dispose(); schedule = null; },
  };

  // dev seam: --dtrh-m2test exposes the arc position + a stage jogger
  try {
    if (typeof window !== 'undefined' && window.__dtrhVnTest) {
      window.__cheshire = Object.assign(window.__cheshire || {}, {
        stage: () => stage,
        setStage: (n) => { stage = Math.max(-1, n | 0); persistStage(stage); },
        playScene: (key) => { const b = script.scenes && script.scenes[key]; return b ? vn.scene(b) : Promise.resolve(false); },
        scenes: () => Object.keys(script.scenes || {}),
      });
    }
  } catch (e) { /* ignore */ }

  return api;
}
