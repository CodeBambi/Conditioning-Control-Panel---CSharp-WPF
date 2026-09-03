/* ============================================================================
 * ui/sd/index.js — the sudden-death presenter + inputs, assembled.
 *
 * The RUNNER calls us, never the other way round: core/suddenDeath.js drives a
 * presenter (render this, tear that down) and consumes four input feeds. This
 * module implements both halves over #scr-sd (the ladder, the intro card, the
 * verdict card) and #gg-stage (anything that takes a finger).
 *
 *   createSuddenDeathUi({audio}) -> {presenter, inputs, dispose}
 *
 * PRESENTER  -> ui/sd/{intro,quickdraw,staring,reaction,bubbles,verdict}.js
 * INPUTS     -> emitters raised by those modules; the names mirror
 *               rounds/model.js's fakeRoundInputs (raiseSolved / raiseAbandoned /
 *               raisePress / raiseRendered / raisePop) so a test can drive this
 *               object exactly like the fake one.
 *
 * Everything is optional-chained and try-wrapped: a presenter that throws must
 * not take a match down (model.js swallows it, but we do not rely on that).
 *
 * Node-import-safe: no DOM at import, only inside createSuddenDeathUi().
 * ==========================================================================*/

import { GoonRoundKind } from '../../core/contracts.js';
import { createEmitter } from '../../core/rounds/model.js';
import { createIntro, ROUND_COPY } from './intro.js';
import { createVerdict } from './verdict.js';
import { createQuickDraw } from './quickdraw.js';
import { createStaring } from './staring.js';
import { createReaction } from './reaction.js';
import { createBubbles } from './bubbles.js';

/** -3..+3 — seven notches, three each way (GoonConsts.SuddenDeathNetLoss). */
const NET_MIN = -3;
const NET_MAX = 3;

const doc = () => (typeof document !== 'undefined' ? document : null);

function el(tag, cls, text) {
  const d = doc();
  if (!d || typeof d.createElement !== 'function') return null;
  const n = d.createElement(tag);
  if (cls && n) n.className = cls;
  if (text != null && n) n.textContent = String(text);
  return n;
}

function add(parent, child) {
  if (parent && child && typeof parent.appendChild === 'function') parent.appendChild(child);
  return child;
}

function cls(node, name, on) {
  if (!node || !node.classList) return;
  try { node.classList[on ? 'add' : 'remove'](name); } catch (_e) { /* stub DOM */ }
}

function text(node, value) {
  if (node) node.textContent = value == null ? '' : String(value);
}

function byId(id) {
  const d = doc();
  return d && typeof d.getElementById === 'function' ? d.getElementById(id) : null;
}

/**
 * @param {object}   [o]
 * @param {object}   [o.audio]    {sfx(id)} — the stub is fine, call it anyway
 * @param {Function} [o.getClock] () => MatchClock, for a countdown synced to fireAtMatchMs
 * @param {Function} [o.onLog]
 * @returns {{presenter:object, inputs:object, dispose:Function, netScore:Function}}
 */
export function createSuddenDeathUi({ audio = null, getClock = null, onLog = null } = {}) {
  const owned = [];
  const own = (fn) => { if (typeof fn === 'function') owned.push(fn); };

  // ---- input feeds (the exact model.js shape, plus the raise* drivers) -----
  const solved = createEmitter();
  const abandoned = createEmitter();
  const blink = createEmitter();
  const sample = createEmitter();
  const press = createEmitter();
  const rendered = createEmitter();
  const popped = createEmitter();

  const inputs = {
    lockCard: { onSolved: solved.on, onAbandoned: abandoned.on },
    attention: { onBlinkDetected: blink.on, onAttentionSample: sample.on },
    reaction: { onInputPressed: press.on, onStimulusRendered: rendered.on },
    bubbles: { onBubblePopped: popped.on },

    // model.js fakeRoundInputs parity — the play-test driver pokes these.
    raiseSolved(mistakes = 0) { solved.emit({ mistakes: mistakes | 0 }); },
    raiseAbandoned() { abandoned.emit(); },
    raiseBlink() { blink.emit(); },
    raiseSample(attention01, lookingAway = false) { sample.emit({ attention01, lookingAway }); },
    raisePress() { press.emit(); },
    raiseRendered() { rendered.emit(); },
    raisePop(index) { popped.emit({ index: index | 0 }); },
  };

  // ---- a single rAF pump shared by every round surface --------------------
  const frames = new Set();
  let raf = 0;
  function startFrames() {
    if (raf || frames.size === 0 || typeof requestAnimationFrame !== 'function') return;
    raf = requestAnimationFrame(() => {
      raf = 0;
      for (const fn of Array.from(frames)) { try { fn(); } catch (_e) { /* one painter must not stop the rest */ } }
      startFrames();
    });
  }
  function onFrame(fn) {
    if (typeof fn !== 'function') return () => {};
    frames.add(fn);
    startFrames();
    return () => frames.delete(fn);
  }
  own(() => {
    frames.clear();
    if (raf && typeof cancelAnimationFrame === 'function') { try { cancelAnimationFrame(raf); } catch (_e) { /* gone */ } }
    raf = 0;
  });

  // ---- exec/lockCards.js is written in parallel; treat it as optional -----
  let lockCardFactory = null;
  let lockCardTried = false;
  try {
    Promise.resolve(import('../../exec/lockCards.js')).then(
      (m) => { lockCardFactory = (m && (m.createLockCardView || m.default)) || null; lockCardTried = true; },
      () => { lockCardFactory = null; lockCardTried = true; },
    );
  } catch (_e) { lockCardTried = true; }

  // ---- roots (built on first use, torn down by dispose) -------------------
  let screenRoot = null;
  let stageRoot = null;
  let unhidScreen = false;
  let ladder = null;
  let marker = null;
  let notches = [];
  let roundLabel = null;

  function ensureScreen() {
    // The router hides AND empties every section when it hands the viewport to
    // the HUD (router.hide()), and boot never shows #scr-sd for us — so we own
    // making it visible, and we rebuild if anything wiped our node.
    if (screenRoot && screenRoot.isConnected !== false && screenRoot.parentNode) return screenRoot;
    const host = byId('scr-sd');
    if (host && host.hidden) { host.hidden = false; unhidScreen = true; }
    screenRoot = el('div', 'gg-sd');
    if (!screenRoot) return null;

    roundLabel = add(screenRoot, el('div', 'gg-sd-round', 'sudden death'));
    ladder = add(screenRoot, el('div', 'gg-sd-track'));
    notches = [];
    for (let v = NET_MIN; v <= NET_MAX; v++) {
      const notch = add(ladder, el('i', 'gg-sd-notch'));
      if (notch && notch.setAttribute) notch.setAttribute('data-gg-net', String(v));
      cls(notch, 'is-end', v === NET_MIN || v === NET_MAX);
      notches.push(notch);
    }
    marker = add(ladder, el('b', 'gg-sd-marker'));
    add(screenRoot, el('div', 'gg-sd-track-label', 'three clear rounds ends it.'));

    if (host) add(host, screenRoot);
    else if (doc() && doc().body) add(doc().body, screenRoot);
    setNet(0);
    return screenRoot;
  }

  function ensureStage() {
    // layers.stopAll() empties #gg-stage; rebuild rather than render into a
    // detached node if that ever lands mid-ladder.
    if (stageRoot && stageRoot.isConnected !== false && stageRoot.parentNode) return stageRoot;
    const host = byId('gg-stage');
    stageRoot = el('div', 'gg-sd-stage');
    if (!stageRoot) return null;
    if (host) add(host, stageRoot);
    else if (doc() && doc().body) add(doc().body, stageRoot);
    return stageRoot;
  }

  function setNet(net) {
    ensureScreen();
    const v = Math.max(NET_MIN, Math.min(NET_MAX, net | 0));
    const pct = ((v - NET_MIN) / (NET_MAX - NET_MIN)) * 100;
    if (marker && marker.style) marker.style.left = pct.toFixed(2) + '%';
    cls(ladder, 'is-edge', Math.abs(v) >= NET_MAX);
    cls(ladder, 'is-close', Math.abs(v) === NET_MAX - 1);
    for (let i = 0; i < notches.length; i++) {
      const notchValue = NET_MIN + i;
      cls(notches[i], 'is-lit', v >= 0 ? (notchValue > 0 && notchValue <= v) : (notchValue < 0 && notchValue >= v));
    }
    if (ladder && ladder.setAttribute) ladder.setAttribute('data-gg-net', String(v));
  }

  function setRoundLabel(roundNo, kind) {
    ensureScreen();
    const copy = ROUND_COPY[kind] || { title: 'round' };
    text(roundLabel, 'round ' + Math.max(1, roundNo | 0) + ' · ' + copy.title);
  }

  function sfx(id) {
    try { if (audio && typeof audio.sfx === 'function') audio.sfx(id); } catch (_e) { /* stub */ }
  }

  const ctx = {
    el, add, cls, text, sfx, own, onFrame,
    mountScreen(node) { const host = ensureScreen(); if (host) add(host, node); },
    mountStage(node) { const host = ensureStage(); if (host) add(host, node); },
    /** Cards that must sit ABOVE the round surface (intro, verdict) — same layer,
     *  appended later, so they never end up under #gg-stage's z20. */
    mountOverlay(node) { const host = ensureStage(); if (host) add(host, node); },
    clock() { try { return typeof getClock === 'function' ? getClock() : null; } catch (_e) { return null; } },
    lockCardView() { return lockCardFactory; },
    setNet,
    setRoundLabel,
    raise: {
      solved(mistakes) { inputs.raiseSolved(mistakes); },
      abandoned() { inputs.raiseAbandoned(); },
      press() { inputs.raisePress(); },
      rendered() { inputs.raiseRendered(); },
      pop(index) { inputs.raisePop(index); },
    },
    log(entry) {
      if (typeof onLog !== 'function') return;
      try { onLog(entry); } catch (_e) { /* never load-bearing */ }
    },
  };

  const intro = createIntro(ctx);
  const verdict = createVerdict(ctx);
  const quickdraw = createQuickDraw(ctx);
  const staring = createStaring(ctx);
  const reaction = createReaction(ctx);
  const bubbles = createBubbles(ctx);

  /** Every presenter member is wrapped: a render fault never reaches the ladder. */
  function guard(name, fn) {
    return function guarded(arg) {
      try { return fn(arg); }
      catch (e) { ctx.log({ t: 'sd-presenter-threw', member: name, message: e && e.message }); return undefined; }
    };
  }

  const presenter = {
    showRoundIntro: guard('showRoundIntro', (i) => {
      ensureScreen();
      ensureStage();
      setRoundLabel(i && i.roundNo, i && i.kind);
      intro.show(i || {});
      ctx.log({ t: 'sd-round', roundNo: i && i.roundNo, kind: i && i.kind, difficulty: i && i.difficulty });
    }),
    showLockCard: guard('showLockCard', (spec) => { intro.hide(); quickdraw.show(spec || {}); }),
    hideLockCard: guard('hideLockCard', () => quickdraw.hide()),
    startStaringContest: guard('startStaringContest', (spec) => { intro.hide(); staring.start(spec || {}); }),
    endStaringContest: guard('endStaringContest', () => staring.end()),
    armReactionDuel: guard('armReactionDuel', (spec) => { intro.hide(); reaction.arm(spec || {}); }),
    fireReactionStimulus: guard('fireReactionStimulus', (kind) => reaction.fire(kind)),
    endReactionDuel: guard('endReactionDuel', () => reaction.end()),
    startBubbleRace: guard('startBubbleRace', (spec) => { intro.hide(); bubbles.start(spec || {}); }),
    endBubbleRace: guard('endBubbleRace', () => bubbles.end()),
    showRoundVerdict: guard('showRoundVerdict', (outcome) => verdict.show(outcome)),
  };

  return {
    presenter,
    inputs,
    /** Whether the shared lock-card view was available (false = inline fallback). */
    lockCardStatus() { return { tried: lockCardTried, available: !!lockCardFactory, fallback: quickdraw.usedFallback() }; },
    kinds: GoonRoundKind,
    setNet,
    dispose() {
      for (const part of [intro, verdict, quickdraw, staring, reaction, bubbles]) {
        try { part.dispose(); } catch (_e) { /* keep unwinding */ }
      }
      while (owned.length) { const fn = owned.pop(); try { fn(); } catch (_e) { /* keep unwinding */ } }
      if (stageRoot) { try { stageRoot.remove(); } catch (_e) { /* gone */ } stageRoot = null; }
      if (screenRoot) { try { screenRoot.remove(); } catch (_e) { /* gone */ } screenRoot = null; }
      if (unhidScreen) {
        const host = byId('scr-sd');
        if (host) host.hidden = true;    // hand the section back the way we found it
        unhidScreen = false;
      }
      ladder = null; marker = null; notches = []; roundLabel = null;
    },
  };
}
