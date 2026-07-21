/* ============================================================================
 * render/beats.js — RESPONSE MECHANICS for "Graded Intake"  (Agent C)
 *
 * createBeats({ root, effects, audio, steering, reward, caps, background, theme, media })
 *   -> { render(beat) }
 *   render(beat: BeatSpec) -> Promise<AnswerEvent>
 *
 * Implements all 9 Mechanic.* renderers into DOM/canvas:
 *   MC4, YesNo, BubblePop (CENTERPIECE: true floating bubbles that break the
 *   4th wall — they ENTER from a random viewport edge/corner on a fixed
 *   full-viewport layer, drift slowly to a slot over the card, then hold
 *   station with a gentle idle bob (never leaving the viewport); pop to
 *   answer; ONLY the "let it float away" option ever exits — it starts its
 *   own slow drift shortly after landing and commits by drifting fully
 *   offscreen
 *   after the answer window; climax pop-storm decoys; pop = reward chain of
 *   mini bubbles), Mantra (Web Speech -> type-it degrade), CheckIn
 *   (slider), Mono (single agree), Funnel (wrong -> respawns correct),
 *   Destruct (correct shatters but STILL registers), Interlude (non-question
 *   pacing valley: 'watch' spiral stare / 'breathe' cycle, auto-commits).
 *
 * Presentation extras (all additive, all reduced-motion aware):
 *   - REAL BUBBLE SPRITE: when media.bubbleSprite (MediaManifest, data: URI of
 *     the app's mod-aware soap-bubble PNG) is present, every floating answer
 *     bubble uses it as its body (<img> filling the wrapper, label on a scrim
 *     above); absent -> the CSS glass stand-in (standalone browser runs)
 *   - band-flavored card ENTRY + EXIT animations (exit runs AFTER resolve on the
 *     old DOM — zero added latency; the next render clears it)
 *   - typewriter / breathing prompt text in Deepening/Climax
 *   - shrinking-ring pressure cue when beat.timeoutMs > 0 (urgent final 20%)
 *   - streak counter: consecutive-correct across renders -> AnswerEvent.streak
 *     + RewardEvent.streak (interlude/checkin/recovery never count either way)
 *   - background forwarding (ALL guarded — background may be absent/stub):
 *     onAnswer at commit, onReward after effects.play, setSteerWarp while a
 *     heavy steer (roll.intensity >= 0.5) is live (reset on commit/teardown)
 *
 * On commit: resolve reward via reward.resolve(beat.rewardPlan, ev, scoreDelta),
 * then effects.play(rewardEvent, beat.depth) + audio.chime(rewardEvent).
 *
 * Steering (Agent D) is wired PER BEAT through the Phase-0 SteerContext:
 *   - beats.js builds { root, options[{index,el,isCorrect,score}], mechanic, band,
 *     depth, roll, caps, escapeEffort, escapeMs, onCommit, forceComplete,
 *     markProgress } and calls steering.installSteering(ctx); releases the handle
 *     on commit. For BubblePop the handle is installed ONCE (after the first
 *     layout pass) with stable els — the bubbles move via transform on a wrapper,
 *     so steers composing transforms on the buttons keep working.
 *   - INVARIANT #1 (friction, not lockout) is enforced HERE as a backstop too:
 *     after `escapeEffort` vetoed interactions OR `escapeMs` of sustained effort,
 *     the commit lands regardless of steers, and a watchdog force-commits the
 *     user's last-attempted answer if a steer wedges the UI entirely.
 *
 * NO COLOR TELL: the is-answer / is-correct classes stay in the DOM (steering
 * and tests target them) but carry ZERO default styling — every option, bubble
 * or button, is visually identical to its siblings by default.
 *
 * Does NOT touch index.html / styles.css / contracts.js / boot.js / steering.js /
 * effects.js / audio.js. Owns its own scoped <style> (injected lazily). Never
 * touches the DOM or Web Speech at import time — only inside the factory/render.
 * ==========================================================================*/

import {
  Mechanic, MECHANICS, Band, makeAnswerEvent, clamp01,
} from '../core/contracts.js';
import { spiralPalette } from '../core/palette.js';
import { recordSpiral } from '../core/spiralLog.js';
import { noteMedia } from '../core/mediaLog.js';
import { tintColorOptions } from './optionTint.js';

/* ----------------------------------------------------------------------------
 * PURE helpers (no DOM) — unit-tested headless. Exported for the smoke test.
 * -------------------------------------------------------------------------- */

/** Normalize a spoken/typed phrase for fuzzy mantra matching. */
export function normalizePhrase(s) {
  return String(s == null ? '' : s)
    .toLowerCase()
    .replace(/[^\p{L}\p{N}\s]/gu, ' ')
    .replace(/\s+/g, ' ')
    .trim();
}

/**
 * Fuzzy match a spoken/typed answer against the expected mantra text.
 * Containment OR >=60% expected-token overlap. Empty expected -> any utterance.
 */
export function matchMantra(spoken, expected) {
  const a = normalizePhrase(spoken);
  const b = normalizePhrase(expected);
  if (!b) return a.length > 0;
  if (!a) return false;
  if (a.includes(b) || b.includes(a)) return true;
  const at = new Set(a.split(' '));
  const bt = b.split(' ');
  if (!bt.length) return false;
  let hit = 0;
  for (const w of bt) if (at.has(w)) hit++;
  return hit / bt.length >= 0.6;
}

/**
 * Resolve which renderer to use for a beat. Honors beat.mechanic when valid;
 * otherwise falls back by prompt/option shape so a malformed beat never dead-ends.
 */
export function selectMechanic(beat) {
  const m = beat && beat.mechanic;
  if (MECHANICS.includes(m)) return m;
  if (beat && Array.isArray(beat.options) && beat.options.length >= 2) return Mechanic.MC4;
  const ans = beat && beat.prompt ? beat.prompt.answer : undefined;
  if (typeof ans === 'number') return Mechanic.CheckIn;
  if (typeof ans === 'string') return Mechanic.Mantra;
  if (typeof ans === 'boolean') return Mechanic.YesNo;
  return Mechanic.Mono;
}

/**
 * Points a committed answer is worth for a beat — the single source of truth for
 * answer->score mapping. `raw` carries { chosenIndex?, value? } (an AnswerEvent
 * or a partial). Option-backed mechanics score from the chosen option; free-input
 * mechanics score against prompt.answer.
 */
export function scoreDelta(beat, raw) {
  if (!beat) return 0;
  const opts = Array.isArray(beat.options) ? beat.options : [];
  const idx = raw ? raw.chosenIndex : undefined;
  if (typeof idx === 'number' && opts[idx]) {
    const o = opts[idx];
    return typeof o.score === 'number' ? o.score : (o.isCorrect ? 1 : 0);
  }
  const v = raw ? raw.value : undefined;
  const prompt = beat.prompt || {};
  switch (beat.mechanic) {
    case Mechanic.YesNo: {
      const want = !!prompt.answer;
      const got = (v === true || v === 'yes' || v === 1);
      const correct = got === want;
      if (opts.length) {
        const co = opts.find((o) => o.isCorrect);
        return correct ? (co && typeof co.score === 'number' ? co.score : 1) : 0;
      }
      return correct ? 1 : 0;
    }
    case Mechanic.Mantra:
      return matchMantra(v, prompt.answer) ? 1 : 0;
    case Mechanic.CheckIn:
      return clamp01(typeof v === 'number' ? v : 0);
    case Mechanic.Interlude:
      // pacing valley: completing it is always "worth" full marks — an interlude
      // is NEVER scored as wrong (and never counts toward the streak either way).
      return 1;
    case Mechanic.Mono:
    case Mechanic.Funnel:
    case Mechanic.Destruct: {
      const co = opts.find((o) => o.isCorrect);
      if (co) return typeof co.score === 'number' ? co.score : 1;
      return v === undefined ? (raw && raw.timedOut ? 0 : 1) : 1;
    }
    default:
      return 0;
  }
}

/** Build a resolved RewardEvent when reward.js isn't wired (defensive fallback). */
export function fallbackReward(beat, delta) {
  const plan = (beat && beat.rewardPlan) || {};
  const decoupled = !!plan.mode && plan.mode !== 'honest';
  const fire = delta > 0 || decoupled;
  return {
    fire,
    intensity: plan.baseIntensity != null ? clamp01(plan.baseIntensity) : clamp01(beat ? beat.depth : 0),
    kind: plan.kind || 'chime',
    decoupled,
  };
}

/* ----------------------------------------------------------------------------
 * Presentation constants (module scope; no DOM).
 * -------------------------------------------------------------------------- */

/** Band -> card ENTRY animation class (see IB_CSS). */
const BAND_ENTER = {
  [Band.Calibration]:  'ib-enter-crisp',  // crisp clinical slide-in
  [Band.Establishing]: 'ib-enter-crisp',
  [Band.Deepening]:    'ib-enter-melt',   // slow melt-in (blur + settle)
  [Band.Climax]:       'ib-enter-sink',   // sink/dissolve-in, slight rotation drift
  [Band.Recovery]:     'ib-enter-dawn',   // soft dawn fade
};
/** Band -> card EXIT animation class (added AFTER resolve, on the old DOM). */
const BAND_EXIT = {
  [Band.Calibration]:  'ib-exit-crisp',
  [Band.Establishing]: 'ib-exit-crisp',
  [Band.Deepening]:    'ib-exit-melt',
  [Band.Climax]:       'ib-exit-sink',
  [Band.Recovery]:     'ib-exit-dawn',
};

/** Built-in hypnotic filler words for decoys / chain minis (theme.praise joins them). */
const HYPNO_WORDS = ['deeper', 'soft', 'obey', 'blank', 'yes'];

/** Niche-neutral general hypno lines flashed once when the breathe bubble is popped. */
const BREATH_SUBLIMINALS = [
  'let go', 'deeper now', 'so easy', 'sink', 'empty is easy',
  'good', 'drift down', 'no thoughts', 'just breathe', 'obey the pull',
];

/* ----------------------------------------------------------------------------
 * "ARE YOU SURE?" JUMPSCARE EVENT (Doki-Doki flavored).
 *   A discrete WRONG option press on a graded question card, in one of the three
 *   deep bands, may — AT MOST ONCE PER RUN — intercept the press (never commits
 *   it) with a frozen "are you sure?" card whose Yes button drifts away. Catching
 *   Yes triggers a glitch/gif jumpscare + closes the window; No restores and the
 *   beat continues. The latch is module-level (reset per factory = per run).
 * -------------------------------------------------------------------------- */
/** Per-band trigger chance (0 = never). Only these three bands arm the event. */
const IX_EVENT_CHANCE = {
  [Band.Deepening]: 0.02,
  [Band.Climax]:    0.035,
  [Band.Recovery]:  0.05,
};
/** Once-per-run latch (module scope, cleared at the top of createBeats()). */
let _ixEventFired = false;
/** ONCE-PER-RUN eligibility for the mid-run glitch jumpscare: only 1 run in 10
 *  may EVER fire it. Rolled a single time per run (lazily, the first time a wrong
 *  press reaches a genuine fire opportunity) and cached for the rest of the run;
 *  an ineligible run can NEVER fire the event. The per-beat trigger odds/window
 *  (IX_EVENT_CHANCE) stay unchanged WITHIN an eligible run. */
const GLITCH_RUN_CHANCE = 0.10;

/** Chance (per non-control click, once per beat) that a standard card melts away
 *  and skips to the next beat — a rare WHIMSY event (owner tuning: 1 in 45).
 *  Halved in frequency from the original 1-in-30: the melt SKIPS the beat, so
 *  every one of these costs the run a graded answer, and at 1-in-30 it was
 *  landing often enough to read as a mechanic rather than as an accident. */
const MELT_CHANCE = 1 / 45;
/** The card melt takes the VOICE down with it: the prompt line RESUMES from
 *  where it had got to, slowed to this rate (a drawl), then its tail fades out
 *  across the melt — the words sag as the card drips away. */
const MELT_VOICE_RATE = 0.45;
/** Only slur if the line is still plausibly speaking. Prompt clips run several
 *  seconds; past this many seconds since it started we assume it has finished
 *  and simply fade whatever is left (a stale offset would RESTART the clip,
 *  since playVoice clamps an out-of-range offset back to 0). */
const MELT_VOICE_MAX_ELAPSED_S = 4.5;
/** Fraction of the melt that plays at the drawl before the fade-out begins. */
const MELT_VOICE_SAG_FRAC = 0.45;
/** Fade-out length (seconds) of the melting voice. Deliberately LONGER than the
 *  ~950ms card melt: the visual is gone but the voice keeps sagging away under
 *  the next card. It outlives the beat because our own stopVoice() already
 *  detached the handle, so neither teardown nor the next line's one-VO-at-a-time
 *  cut can reach it — it just decays to silence on its own schedule. */
const MELT_VOICE_FADE_S = 2.4;
/** On a slider beat where auto-rise WOULD arm (Climax/Recovery), chance (rolled
 *  ONCE per beat) that the autonomous rise actually arms; otherwise the slider
 *  stays fully manual. Wheel-nudge QoL is unaffected either way. */
const SLIDER_AUTORISE_CHANCE = 0.30;

/* ----------------------------------------------------------------------------
 * CORRUPTED QUESTION EVENT (Deepening + Climax only) — OWNER TUNING BLOCK.
 *   A question beat in one of the two deep bands may roll a CORRUPTION shortly
 *   after the prompt lands. Two flavors, one roll:
 *     A) GLITCH — the spoken VO is chopped/stuttered/pitch-wobbled, the written
 *        prompt datamoshes (RGB-split ghosts + slice tear + character scramble)
 *        and a deliberately BROKEN copy of one of the app's real reward GIFs
 *        punches through over the line. The feed then "recovers" (text restored).
 *     B) MELT — a long, slow blur/drip that dissolves the prompt TEXT ONLY,
 *        leaving the question line empty. Card, options and every control stay
 *        intact and usable (the line's height is pinned so nothing reflows).
 *   Corruption is ATMOSPHERE, never a soft-lock: options are never touched and
 *   the beat still resolves through the normal commit path. Grading is entirely
 *   unchanged — a melted-away prompt still has exactly the same correct option
 *   (the options carry the meaning; the prompt is flavor).
 *   MUTUALLY EXCLUSIVE with the "are you sure?" jumpscare and the FREEZE GATE:
 *   firing sets gateUsedThisBeat, so at most one set-piece ever owns a beat.
 * -------------------------------------------------------------------------- */
/** Per-band chance that a qualifying question beat corrupts. */
const CORRUPT_CHANCE = {
  [Band.Deepening]: 0.05,   // ~5% per qualifying beat
  [Band.Climax]:    0.10,   // ~10% per qualifying beat
};
/** Flavor split: roll < this => GLITCH, else => MELT (0.5 = even coin). */
const CORRUPT_GLITCH_SPLIT = 0.5;
/** How long the GLITCH storm runs before the feed recovers (ms). */
const CORRUPT_GLITCH_MS = 2600;
/** How long the MELT takes to dissolve the prompt to nothing (ms). */
const CORRUPT_MELT_MS = 9000;
/** Earliest graded question (beat.meta.qIndex, 1-based) that may corrupt —
 *  same reasoning as the freeze gate: this early it just reads as a bug. */
const CORRUPT_MIN_Q = 3;
/** Base opacity of the broken-GIF overlay BEFORE caps (flash/master) scale it. */
const CORRUPT_GIF_ALPHA = 0.62;
/** Backing-store width of the broken-GIF canvas — i.e. the pixelation grain
 *  (CSS stretches it over the prompt box with image-rendering: pixelated). */
const CORRUPT_GIF_PX = 56;
/** Redraw period of the broken GIF (ms) — deliberately choppy, ~13fps. */
const CORRUPT_GIF_FRAME_MS = 75;
/** Glyph pool the prompt scrambles into while glitching. */
const CORRUPT_SCRAMBLE = '#@%&*!/|<>_=+$?~^';

/* ----------------------------------------------------------------------------
 * OPTIONS HOLD — the answer buttons are not offered the instant the card mounts.
 * The prompt gets to finish presenting itself first: the deep-band typewriter
 * (~18ms/char) and the spoken VO both need room, and answering over the top of
 * them was the other half of the speedrun problem. The buttons mount inert and
 * invisible, then fade in together.
 *   hold = clamp(TYPEWRITER_MS + AFTER_TYPE, MIN, MAX)
 * A timed beat's clock is EXTENDED by the hold (see the timeout block) so the
 * player never loses answer time to it. */
const OPTIONS_HOLD_MIN_MS = 1500;   // floor — the owner-requested 1.5s
const OPTIONS_HOLD_MAX_MS = 4000;   // ceiling, so a very long prompt can't stall the run
const OPTIONS_HOLD_AFTER_TYPE_MS = 250; // beat of silence after the last character lands
// (the reveal fade itself is the .ib-opts-grid transition, .26s, in the CSS below)

/** Scramble a fraction (`amount`, 0..1) of a string's non-space chars. PURE. */
export function scrambleText(src, amount) {
  const s = String(src == null ? '' : src);
  let out = '';
  for (let i = 0; i < s.length; i++) {
    const ch = s[i];
    if (ch === ' ' || ch === '\n' || Math.random() > amount) out += ch;
    else out += CORRUPT_SCRAMBLE[(Math.random() * CORRUPT_SCRAMBLE.length) | 0];
  }
  return out;
}

/** Inject the corruption visuals lazily (only the first time a beat corrupts). */
const IXCR_STYLE_ID = 'ib-corrupt-style';
function ensureCorruptStyles() {
  if (typeof document === 'undefined' || document.getElementById(IXCR_STYLE_ID)) return;
  const s = document.createElement('style');
  s.id = IXCR_STYLE_ID;
  s.textContent = IXCR_CSS;
  document.head.appendChild(s);
}

/** Inject the event's scoped styles + the freeze rule (only when it first fires). */
const IXEV_STYLE_ID = 'ib-event-style';
function ensureEventStyles() {
  if (typeof document === 'undefined' || document.getElementById(IXEV_STYLE_ID)) return;
  const s = document.createElement('style');
  s.id = IXEV_STYLE_ID;
  s.textContent = IXEV_CSS;
  document.head.appendChild(s);
}

/* ----------------------------------------------------------------------------
 * "ARE YOU SURE?" FREEZE GATE (distinct from the once-per-run jumpscare above).
 *   When a WRONG discrete option is committed on an options beat in a DEEP band
 *   (Deepening/Climax only — see FREEZE_GATE_BANDS), a 3% roll
 *   may FREEZE the whole game (tube, subliminals, ring, steers) instead of taking
 *   the wrong answer: a red typewriter taunt subtitle + a spoken VO taunt
 *   (sure_<niche>_NN) pop up, the correct option is highlighted, and only it is
 *   pressable. Over 20s of stalling a red tint saturates, the subtitle trembles
 *   harder, and a rumble riser climbs; at 20s WE auto-pick the correct option
 *   (visible fake cursor), chime, and move on. Mutually exclusive with the
 *   jumpscare PER BEAT. Repeatable across beats (no once-per-run latch). */
const FREEZE_GATE_CHANCE = 0.03;          // roll on each qualifying wrong press
/** Descent bands the gate may fire in (bands 3 + 4 of the five-band walk).
 *  Calibration/Establishing are never interrupted — the mechanic reads as a bug
 *  that early — and Recovery is the walk-down, which never coerces. */
const FREEZE_GATE_BANDS = [Band.Deepening, Band.Climax];
const FREEZE_GATE_MS = 20000;             // stall window before we auto-pick
const FREEZE_SUB_FALLBACK = 'Are you sure, sweetie?'; // no manifest/clip text
const FREEZE_NICHES = ['bambi', 'sissy', 'drone', 'circe'];
const FREEZE_DOT_Z = 2147480000;          // == steering HIJACK_Z (fake cursor on top)
/** Mechanics with a discrete wrong/correct option set (skip sliders/mantra/
 *  interludes/BubblePop). Mono/Funnel never send a WRONG index to attempt(), so
 *  the gate simply never rolls on them even though they're eligible. */
const FREEZE_MECHS = [Mechanic.MC4, Mechanic.YesNo, Mechanic.Mono, Mechanic.Funnel, Mechanic.Destruct];

/** Inject the freeze-gate visuals lazily (only the first time the gate fires). */
const IXFZ_STYLE_ID = 'ib-freeze-style';
function ensureFreezeStyles() {
  if (typeof document === 'undefined' || document.getElementById(IXFZ_STYLE_ID)) return;
  const s = document.createElement('style');
  s.id = IXFZ_STYLE_ID;
  s.textContent = IXFZ_CSS;
  document.head.appendChild(s);
}

/** Live prefers-reduced-motion probe (called at render time, never at import). */
function prefersReducedMotion() {
  try {
    return typeof window !== 'undefined' && !!window.matchMedia
      && window.matchMedia('(prefers-reduced-motion: reduce)').matches;
  } catch (_e) { return false; }
}

/* ----------------------------------------------------------------------------
 * DOM helpers (module scope; only invoked at render time).
 * -------------------------------------------------------------------------- */
function el(tag, cls, text) {
  const e = document.createElement(tag);
  if (cls) e.className = cls;
  if (text != null) e.textContent = text;
  return e;
}
function mkBtn(label, onClick, cls) {
  const b = document.createElement('button');
  b.type = 'button';
  b.className = 'ib-btn' + (cls ? ' ' + cls : '');
  b.textContent = label;
  if (onClick) b.addEventListener('click', onClick);
  return b;
}

const STYLE_ID = 'ib-beats-style';
function ensureStyles() {
  if (typeof document === 'undefined' || document.getElementById(STYLE_ID)) return;
  const s = document.createElement('style');
  s.id = STYLE_ID;
  s.textContent = IB_CSS;
  document.head.appendChild(s);
}

/* ----------------------------------------------------------------------------
 * BACKDROP-LIVE body-class contract (shared with effects.js — intentional
 * duplicate, no shared module). Ref-counted via a body dataset counter so
 * overlapping fullscreen visual layers (spiral / gif backdrops) that each
 * increment on mount + decrement on cleanup never stomp each other: the
 * `ix-backdrop-live` class is present iff at least one layer is up. beats.js
 * OWNS the counting for its own layers; it adds NO CSS for the class (the
 * effects agent owns the styling of see-through cards).
 * -------------------------------------------------------------------------- */
function backdropRef(on) {
  const b = document.body;
  const n = Math.max(0, (parseInt(b.dataset.ixBackdrop || '0', 10) || 0) + (on ? 1 : -1));
  b.dataset.ixBackdrop = String(n);
  b.classList.toggle('ix-backdrop-live', n > 0);
}

/* ----------------------------------------------------------------------------
 * CROSS-MODULE spiral no-repeat guard (shared with effects.js — intentional
 * duplicate, no shared module). Rolls params via make() and rerolls (up to 4x)
 * until the JSON signature differs from the last spiral shown ANYWHERE in the
 * intake UI (window.__ixSpiralSig is the shared cross-module cursor), so we
 * never show two consecutive identical spirals. Called only at render time.
 * -------------------------------------------------------------------------- */
function freshSpiralParams(make) {   // make: () => params object
  let p = make();
  const sig = (o) => { try { return JSON.stringify(o); } catch (_e) { return String(Math.random()); } };
  for (let i = 0; i < 4 && sig(p) === window.__ixSpiralSig; i++) p = make();
  window.__ixSpiralSig = sig(p);
  // The run's spirals are woven from the player's own harvested colours, so the
  // outro shows them back (core/spiralLog.js -> boot.js runOutro). This is the
  // one place every beats-side mount rolls its params, so it is the one place
  // that has to record. Never throws; returns `p` untouched.
  return recordSpiral(p);
}

/* ----------------------------------------------------------------------------
 * Fullscreen backdrop-layer fade helpers (beats-owned layers: .ib-bubbleloom,
 * .ib-breathback, .ib-loomlayer). fadeLayerIn mounts the layer at opacity 0 and
 * ramps it to full on the next frame over `inMs`. fadeLayerOutRemove (called at
 * beat cleanup) marks the layer fading-out (so a late fade-in ramp can't undo
 * it), transitions opacity to 0 over `outMs`, and removes the node after
 * `removeMs` (guarded so a double-call can't throw). The layer's inner content
 * keeps its own alpha (canvas .25/.24, gif .24) — we fade the whole div 0<->1.
 * -------------------------------------------------------------------------- */
function fadeLayerIn(node, inMs) {
  try {
    node.style.opacity = '0';
    node.style.transition = 'opacity ' + (inMs || 2000) + 'ms ease';
    const ramp = () => {
      try {
        if (node.dataset && node.dataset.ibFadingOut) return;   // teardown won the race
        node.style.opacity = '1';
      } catch (_e) {}
    };
    if (typeof requestAnimationFrame === 'function') requestAnimationFrame(() => requestAnimationFrame(ramp));
    else ramp();
  } catch (_e) {}
}
function fadeLayerOutRemove(node, outMs, removeMs) {
  try { if (node && node.dataset) node.dataset.ibFadingOut = '1'; } catch (_e) {}
  let removed = false;
  const kill = () => { if (removed) return; removed = true; try { node.remove(); } catch (_e) {} };
  try {
    node.style.transition = 'opacity ' + (outMs || 800) + 'ms ease';
    node.style.opacity = '0';
  } catch (_e) {}
  try { setTimeout(kill, removeMs || 1000); } catch (_e) { kill(); }
  return kill;
}

/* ----------------------------------------------------------------------------
 * FACTORY
 * -------------------------------------------------------------------------- */
export function createBeats({ root, effects, audio, steering, reward, caps, background, theme, media, niche, micEnabled } = {}) {
  const stage = root;
  caps = caps || {};
  // Host's MicConsentGiven. Undefined (standalone/harness) => allowed, since there
  // the browser's permission prompt is the only gate that exists.
  const micAllowed = micEnabled !== false;

  // NEW RUN: re-arm the once-per-run "are you sure?" jumpscare latch.
  _ixEventFired = false;
  // NEW RUN: glitch-jumpscare eligibility is UNDECIDED until the first qualifying
  // wrong press rolls it once (GLITCH_RUN_CHANCE). null = unrolled; true = this run
  // may fire the event, false = it never can. Run-scoped (visible to render() below).
  let glitchRunEligible = null;

  // Active bank niche for the freeze-gate VO id contract (sure_<niche>_NN). The
  // niche is validated by the shim before boot; fall back to bambi if a caller
  // ever omits it. Feeds ONLY the freeze-gate taunt clip id + subtitle text.
  const gateNiche = (typeof niche === 'string' && FREEZE_NICHES.indexOf(niche) >= 0) ? niche : 'bambi';

  // Guarded SFX seam: fire a finalized-library cue via audio.sfx (additive; may
  // be absent on the stub). Never throws, never breaks gameplay. Returns the
  // playback handle (with .stop()) for loops, else null.
  function sfx(id, intensity, opts) {
    try {
      if (audio && typeof audio.sfx === 'function') return audio.sfx(id, intensity, opts);
    } catch (_e) {}
    return null;
  }

  // Guarded VOICEOVER seam: speak a beat's directed prompt VO (additive; audio may
  // be a stub without .voice, or the clip/manifest entry may be absent -> silent,
  // text-only). The manifest keys questions as 'q_'+<bank prompt id>; beat.prompt.id
  // IS that bank id for graded prompts (interlude/recovery/synthetic ids simply have
  // no manifest entry and resolve silently). Never throws, never breaks gameplay.
  // TRICK QUESTIONS (`prompt.trick`) are deliberately UNVOICED — no q_*_trk_* clips
  // exist. audio.loadVoBuffer() returns null on a missing manifest entry BEFORE any
  // fetch and without logging, so they simply play text-only; the typewriter, the
  // options hold and the timer ring are all independent of the VO, so timing is
  // identical to a voiced beat. The silence reads as extra deadpan.
  function speakPrompt(beat) {
    try {
      const pid = beat && beat.prompt && beat.prompt.id;
      if (!pid || !audio || typeof audio.voice !== 'function') return;
      audio.voice('q_' + pid);
    } catch (_e) {}
  }
  function stopVoice(fade) {
    try { if (audio && typeof audio.voiceStop === 'function') audio.voiceStop(fade); } catch (_e) {}
  }

  // REAL BUBBLE SPRITE (MediaManifest.bubbleSprite): a data: URI of the app's
  // actual soap-bubble PNG (mod-aware, host-supplied). Null -> CSS fallback.
  const bubbleSprite = (media && typeof media.bubbleSprite === 'string' && media.bubbleSprite)
    ? media.bubbleSprite : null;

  // Backstop budget (invariant #1). Exposed to steering via SteerContext.
  const ESCAPE_EFFORT = 6;   // vetoed interactions before the commit is allowed
  const ESCAPE_MS = 5000;    // sustained effort (ms) before the commit is allowed

  // STREAK: consecutive-correct counter, survives across renders for the whole
  // run. Graded option/mantra mechanics move it; interlude/checkin/recovery
  // never count either way (they are pacing, not grading).
  let streakCount = 0;

  /** Guarded call into the (optional) reactive background. Never throws. */
  function bgCall(name, arg) {
    try {
      if (background && typeof background[name] === 'function') background[name](arg);
    } catch (_e) {}
  }

  function render(beat) {
    return new Promise((resolve) => {
      ensureStyles();
      const t0 = (typeof performance !== 'undefined' ? performance.now() : Date.now());
      if (stage) stage.innerHTML = '';

      // ---- per-beat commit + steering state ------------------------------
      let committed = false;
      let eventOpen = false;           // the "are you sure?" jumpscare overlay is live
      let steeredFlag = false;
      const interceptors = [];         // steering onCommit hooks
      let effort = 0;                  // vetoed interactions expended on the refusal
      let firstEffortAt = 0;
      let watchdog = 0;
      let timeoutTimer = 0;
      let steerHandle = null;
      const cleanups = [];
      let lastAttemptIndex = null;
      let lastAttemptValue;
      let warpSet = false;             // background.setSteerWarp is live

      // ---- "are you sure?" FREEZE GATE per-beat state --------------------
      let gateOptions = null;          // [{index,el,isCorrect,score}] captured at installSteering
      let gateFrozen = false;          // the freeze gate currently holds the scene frozen
      let gateUsedThisBeat = false;    // jumpscare OR freeze gate OR corruption fired this beat (mutual excl.)
      let corruptLive = false;         // a CORRUPTED QUESTION set-piece owns this beat
      const freezeSubs = [];           // (frozen:bool)=>void — ring/heartbeat pause hooks
      function notifyFreeze(v) {
        gateFrozen = !!v;
        for (const f of freezeSubs) { try { f(gateFrozen); } catch (_e) {} }
      }

      const now = () => (typeof performance !== 'undefined' ? performance.now() : Date.now());
      const reduced = prefersReducedMotion();

      function teardown() {
        if (timeoutTimer) { clearTimeout(timeoutTimer); timeoutTimer = 0; }
        if (watchdog) { clearTimeout(watchdog); watchdog = 0; }
        if (warpSet) { warpSet = false; bgCall('setSteerWarp', 0); }
        try { if (steerHandle && steerHandle.release) steerHandle.release(); } catch (_e) {}
        for (const c of cleanups) { try { c(); } catch (_e) {} }
      }

      function finalize(chosenIndex, value, timedOut) {
        if (committed) return;
        committed = true;
        teardown();
        // Contract: exactly one of value/chosenIndex. Free-value mechanics
        // (Y/N bool, mantra string, checkin number, interlude true) carry
        // `value`; option-backed mechanics carry `chosenIndex`. `index` still
        // rides the commit for steering/backstop bookkeeping, but is dropped
        // from the event when a value is present.
        const useValue = value !== undefined;
        const ev = makeAnswerEvent({
          chosenIndex: useValue ? undefined : (typeof chosenIndex === 'number' ? chosenIndex : undefined),
          value: useValue ? value : undefined,
          latencyMs: now() - t0,
          mechanic: beat.mechanic,
          timedOut: !!timedOut,
          steered: steeredFlag,
        });
        const delta = scoreDelta(beat, ev);

        // ---- streak (AnswerEvent.streak; contract-optional, additive) ------
        const skipStreak = beat.mechanic === Mechanic.Interlude
          || beat.mechanic === Mechanic.CheckIn
          || beat.band === Band.Recovery;
        if (!skipStreak) streakCount = delta > 0 ? streakCount + 1 : 0;
        ev.streak = streakCount;       // makeAnswerEvent doesn't carry it; attach here
        // trivially expose the live streak to the HUD (effects agent owns the meter)
        try { if (stage && stage.dataset) stage.dataset.ibStreak = String(streakCount); } catch (_e) {}

        let rewardEvent;
        try {
          rewardEvent = (reward && reward.resolve)
            ? reward.resolve(beat.rewardPlan, ev, delta)
            : fallbackReward(beat, delta);
        } catch (_e) { rewardEvent = fallbackReward(beat, delta); }
        if (!rewardEvent) rewardEvent = fallbackReward(beat, delta);
        // attach streak to the reward event if reward.js didn't (drives the meter)
        try { if (rewardEvent.streak == null) rewardEvent.streak = streakCount; } catch (_e) {}

        // background: answer pulse at commit (guarded — may be absent/stub)
        bgCall('onAnswer', { correct: delta > 0, steered: steeredFlag });

        try { if (effects && effects.play) effects.play(rewardEvent, beat.depth); } catch (_e) {}
        try { if (audio && audio.chime) audio.chime(rewardEvent); } catch (_e) {}

        // background: reward forwarding AFTER effects.play
        bgCall('onReward', rewardEvent);

        resolve(ev);

        // band-flavored EXIT on the old DOM — runs AFTER resolve so it adds
        // ZERO latency; the next render clears the stage over it.
        try {
          if (card) card.classList.add(BAND_EXIT[beat.band] || 'ib-exit-fade');
        } catch (_e) {}
      }

      function backstopTripped() {
        const timeUp = firstEffortAt && (now() - firstEffortAt) >= ESCAPE_MS;
        return effort >= ESCAPE_EFFORT || timeUp;
      }
      function noteEffort() {
        if (!firstEffortAt) firstEffortAt = now();
        effort++;
      }
      function armWatchdog() {
        if (watchdog || committed) return;
        // If a steer wedges the UI so no further click lands, still surface the
        // user's last-attempted answer after the escape window (invariant #1).
        watchdog = setTimeout(() => {
          watchdog = 0;
          if (committed) return;
          steeredFlag = true;
          if (lastAttemptIndex != null || lastAttemptValue !== undefined) {
            finalize(lastAttemptIndex, lastAttemptValue, false);
          }
        }, ESCAPE_MS + 300);
      }

      // The commit gate: runs steering interceptors, honors the backstop.
      async function tryCommit(index, opts) {
        opts = opts || {};
        if (committed) return;
        if (opts.timedOut || opts.force) { finalize(index, opts.value, opts.timedOut); return; }
        if (interceptors.length && !backstopTripped()) {
          for (const fn of interceptors.slice()) {
            let ok = true;
            try { ok = await fn(index); } catch (_e) { ok = true; }
            if (committed) return;
            if (ok === false) { steeredFlag = true; noteEffort(); armWatchdog(); return; }
          }
        }
        finalize(index, opts.value, opts.timedOut);
      }

      // A user gesture toward an answer (index for options, value for free-input).
      function attempt(index, value) {
        if (committed) return;
        if (index != null) lastAttemptIndex = index;
        if (value !== undefined) lastAttemptValue = value;
        // "Are you sure?" jumpscare: a discrete WRONG option press on a graded
        // question card in a deep band may (rarely, once per run) intercept the
        // press with a frozen event card INSTEAD of committing it.
        if (index != null && maybeFireSureEvent(index, value)) return;
        // "ARE YOU SURE?" FREEZE GATE: a wrong discrete press may (3%, per beat,
        // Deepening/Climax only)
        // freeze the whole game and force a re-pick INSTEAD of resolving wrong.
        // Runs AFTER the jumpscare gate (which returns first if it fired) so the
        // two are mutually exclusive per beat. Suppresses the incorrect-feedback
        // below when it fires (the wrong answer is being taken back).
        if (index != null && maybeFireFreezeGate(index, value)) return;
        // Gentle (non-punishing) incorrect-feedback on a WRONG discrete press,
        // AFTER both "are you sure?" gates declined (never on either gate's path).
        if (index != null) {
          let d = 1;
          try { d = scoreDelta(beat, { chosenIndex: index, value }); } catch (_e) { d = 1; }
          if (d <= 0) sfx('incorrect-feedback');
        }
        tryCommit(index, { value });
      }

      // Decide whether this wrong press arms the "are you sure?" event. Returns
      // true (and opens the event) iff it fires — the caller must NOT commit.
      function maybeFireSureEvent(index, value) {
        // gateUsedThisBeat/gateFrozen keep the jumpscare out once the freeze gate
        // has fired/opened this beat (mutual exclusion, both directions).
        if (_ixEventFired || eventOpen || gateUsedThisBeat || gateFrozen || committed) return false;
        // question cards only — never the 4th-wall bubble field or a pacing valley
        if (mech === Mechanic.BubblePop || mech === Mechanic.Interlude) return false;
        const chance = IX_EVENT_CHANCE[beat.band];
        if (!chance) return false;                       // only Deepening/Climax/Recovery
        // only a WRONG discrete option press qualifies (scoreDelta <= 0)
        let delta = 0;
        try { delta = scoreDelta(beat, { chosenIndex: index, value }); } catch (_e) { delta = 1; }
        if (delta > 0) return false;
        // ONCE-PER-RUN eligibility: only 1 run in 10 may EVER fire the glitch. Roll
        // it the first time we reach a real fire opportunity, then cache for the whole
        // run. An ineligible run bails here EVERY time (event run-disabled) WITHOUT
        // setting gateUsedThisBeat — so on this same wrong press the FREEZE GATE is
        // still free to roll (mutual exclusion only kicks in once one of them fires).
        if (glitchRunEligible === null) glitchRunEligible = Math.random() < GLITCH_RUN_CHANCE;
        if (!glitchRunEligible) return false;            // this run never gets the glitch
        if (Math.random() >= chance) return false;       // per-beat trigger roll (unchanged)
        _ixEventFired = true;                            // latch: at most once per run
        gateUsedThisBeat = true;                         // lock the freeze gate out this beat
        startSureEvent();
        return true;
      }

      // Decide whether this wrong press arms the FREEZE GATE. Returns true (and
      // opens the gate) iff it fires — the caller must NOT commit. Options-based
      // beats only; needs a known-correct option element to highlight + re-pick.
      function maybeFireFreezeGate(index, value) {
        if (gateUsedThisBeat || gateFrozen || eventOpen || committed) return false;
        if (FREEZE_MECHS.indexOf(mech) < 0) return false;   // options beats only
        // deep bands only — never in Calibration/Establishing/Recovery
        if (FREEZE_GATE_BANDS.indexOf(beat && beat.band) < 0) return false;
        // TRICK QUESTIONS are exempt (bank prompts flagged `trick`): their only
        // "correct" option is a compliance non-sequitur, so a 20s red-tinted
        // "are you sure?" that forces a re-pick onto it stops reading as a gag
        // and starts reading as a punishment for a question nobody could answer.
        // The miss IS the joke — let it pass and move on.
        if (beat && beat.prompt && beat.prompt.trick) return false;
        const correct = gateOptions && gateOptions.find((o) => o && o.isCorrect && o.el);
        if (!correct) return false;                          // no target to highlight
        // only a WRONG discrete option press qualifies (scoreDelta <= 0)
        let delta = 0;
        try { delta = scoreDelta(beat, { chosenIndex: index, value }); } catch (_e) { delta = 1; }
        if (delta > 0) return false;
        if (Math.random() >= FREEZE_GATE_CHANCE) return false;  // 3% roll
        gateUsedThisBeat = true;                              // lock the jumpscare out this beat
        startFreezeGate(correct);
        return true;
      }

      // Set a freeze-gate subtitle's text: red typewriter reveal (~24ms/char),
      // or instant under reduced motion. Re-runnable (a manifest text upgrade
      // replaces an in-flight fallback reveal). Registers its interval so a
      // mid-beat teardown clears it.
      function setFreezeSubText(node, text, instant) {
        try { if (node._fzTimer) { clearInterval(node._fzTimer); node._fzTimer = 0; } } catch (_e) {}
        if (!node) return;
        if (instant) { try { node.textContent = text; } catch (_e) {} return; }
        let i = 0;
        try { node.textContent = ''; } catch (_e) {}
        node._fzTimer = setInterval(() => {
          i += 1;
          try { node.textContent = text.slice(0, i); } catch (_e) {}
          if (i >= text.length) { try { clearInterval(node._fzTimer); } catch (_e) {} node._fzTimer = 0; }
        }, 24);
      }

      // Build + run the FREEZE GATE. `correct` is the {index,el,...} of the correct
      // option. Freezes everything, shows the red taunt subtitle + VO, highlights
      // the correct option (only it is pressable), escalates over 20s, then auto-
      // picks correct. Resolution flows through the normal correct-answer path; the
      // whole overlay + rumble tear down via cleanups[] (mid-beat teardown safe).
      function startFreezeGate(correct) {
        if (gateFrozen) return;
        ensureEventStyles();   // the body.ix-freeze pause rule + running guard
        ensureFreezeStyles();  // gate visuals (tint / subtitle / fake cursor)

        // OWN the resolution from here: kill the beat's own auto-advance timer so
        // the frozen card can't resolve behind us (same guard jumpscare/melt use).
        if (timeoutTimer) { clearTimeout(timeoutTimer); timeoutTimer = 0; }
        // SUPPRESS STEERS: release the steer handle now — stops every steer ticker
        // (POSITION/REFUSAL_GATE/hijack), restores option transforms/opacity, and
        // drops the steer interceptors so the re-pick commits clean. Idempotent, so
        // teardown()'s later release() is a harmless no-op.
        try { if (steerHandle && steerHandle.release) steerHandle.release(); } catch (_e) {}
        if (warpSet) { warpSet = false; bgCall('setSteerWarp', 0); }

        // FREEZE: body.ix-freeze pauses stage/effects CSS animations + the
        // subliminal bed; setFrozen holds the three.js tube; notifyFreeze pauses
        // our own timer ring + heartbeat.
        notifyFreeze(true);
        try { if (typeof document !== 'undefined') document.body.classList.add('ix-freeze'); } catch (_e) {}
        bgCall('setFrozen', true);

        // ---- overlay DOM (on <body>, above card/steers, below the fake cursor) --
        const tint = el('div', 'ixfz-tint');
        // The taunt is a WRAP + a TEXT child: the wrap owns the slow CSS swell
        // (scale 1 -> 1.12 over the full 20s, skipped under reduced motion) and
        // the child owns the JS tremble transform, so the two compose instead of
        // one stomping the other's `transform`.
        const subWrap = el('div', 'ixfz-subwrap' + (reduced ? '' : ' ixfz-swell'));
        const sub = el('div', 'ixfz-sub' + (reduced ? ' ixfz-reduced' : ''));
        try { subWrap.appendChild(sub); } catch (_e) {}
        try { if (typeof document !== 'undefined') document.body.append(tint, subWrap); } catch (_e) {}

        // taunt line: fallback text immediately, upgrade from the VO manifest.
        setFreezeSubText(sub, FREEZE_SUB_FALLBACK, reduced);
        const nn = String(1 + Math.floor(Math.random() * 20)).padStart(2, '0');
        const voId = 'sure_' + gateNiche + '_' + nn;
        // spoken taunt (audio.voice fails soft on a missing clip/manifest entry).
        try { if (audio && typeof audio.voice === 'function') audio.voice(voId); } catch (_e) {}
        // mirror the SAME line's text as the subtitle once the manifest resolves.
        try {
          if (audio && typeof audio.voiceInfo === 'function') {
            Promise.resolve(audio.voiceInfo(voId)).then((info) => {
              if (gateResolved) return;
              if (info && info.text) setFreezeSubText(sub, info.text, reduced);
            }).catch(() => {});
          }
        } catch (_e) {}

        // rumble riser (synth low-rumble, ramps over the 20s; stops INSTANTLY).
        let rumble = null;
        try { if (audio && typeof audio.rumbleRiser === 'function') rumble = audio.rumbleRiser(FREEZE_GATE_MS / 1000); } catch (_e) {}

        // highlight the CORRECT option; make wrong ones inert while frozen.
        const optEls = (gateOptions || []).filter((o) => o && o.el);
        for (const o of optEls) {
          try {
            if (o.isCorrect) {
              o.el.classList.add('ixfz-correct');
              o.el.disabled = false;
              o.el.style.pointerEvents = 'auto';
            } else {
              o.el.classList.add('ixfz-inert');
              o.el.disabled = true;
            }
          } catch (_e) {}
        }

        // ---- escalation: red tint saturates, subtitle trembles harder --------
        let gateResolved = false;
        let escRaf = 0;
        let dot = null;
        const tStart = now();
        const escStep = () => {
          escRaf = 0;
          if (gateResolved) return;
          const p = Math.min(1, (now() - tStart) / FREEZE_GATE_MS);
          try { tint.style.opacity = (0.5 * p).toFixed(3); } catch (_e) {}
          if (!reduced) {
            const amp = p * p * 10;   // 0 -> ~10px, ramps faster near the end
            const jx = (Math.random() * 2 - 1) * amp;
            const jy = (Math.random() * 2 - 1) * amp * 0.6;
            try { sub.style.transform = 'translate(' + jx.toFixed(1) + 'px,' + jy.toFixed(1) + 'px)'; } catch (_e) {}
          }
          if (p < 1) { escRaf = (typeof requestAnimationFrame === 'function') ? requestAnimationFrame(escStep) : 0; }
          else autoPick();
        };
        escRaf = (typeof requestAnimationFrame === 'function') ? requestAnimationFrame(escStep) : 0;
        // headless / no-rAF host: still honor the 20s auto-pick via a timer.
        let escTimer = 0;
        if (typeof requestAnimationFrame !== 'function') {
          escTimer = setTimeout(() => { escTimer = 0; autoPick(); }, FREEZE_GATE_MS);
        }

        // ---- 20s reached: WE pick — glide a fake cursor to correct, commit ----
        function autoPick() {
          if (gateResolved || committed) return;
          const targetOf = () => {
            try { const r = correct.el.getBoundingClientRect(); return { x: r.left + r.width / 2, y: r.top + r.height / 2 }; }
            catch (_e) { return { x: (window.innerWidth || 1280) / 2, y: (window.innerHeight || 720) / 2 }; }
          };
          let cx = (window.innerWidth || 1280) / 2;
          let cy = (window.innerHeight || 720) * 0.62;
          const commitCorrect = () => {
            if (gateResolved || committed) return;
            steeredFlag = true;                          // WE moved the cursor
            // real correct-answer path: scoring + reward chime + teardown (which
            // runs gateCleanup -> instaclear + rumble stop). force skips steers.
            tryCommit(correct.index, { force: true });
          };
          if (typeof requestAnimationFrame !== 'function') { commitCorrect(); return; }
          dot = el('div', 'ixfz-dot');
          try { document.body.appendChild(dot); } catch (_e) {}
          try { dot.style.transform = 'translate(' + cx.toFixed(1) + 'px,' + cy.toFixed(1) + 'px)'; } catch (_e) {}
          const glideStart = now();
          const GLIDE = reduced ? 220 : 720;
          const dotStep = () => {
            if (gateResolved || committed) return;
            const tp = Math.min(1, (now() - glideStart) / GLIDE);
            const tg = targetOf();
            cx += (tg.x - cx) * 0.18;
            cy += (tg.y - cy) * 0.18;
            try { if (dot) dot.style.transform = 'translate(' + cx.toFixed(1) + 'px,' + cy.toFixed(1) + 'px)'; } catch (_e) {}
            if (tp < 1) requestAnimationFrame(dotStep);
            else commitCorrect();
          };
          requestAnimationFrame(dotStep);
        }

        // ---- teardown: instaclear visuals + rumble, unfreeze. Runs on resolve
        // (finalize -> teardown -> cleanups) AND on any mid-beat teardown
        // (band exit / forceComplete) — a committed-latch double-resolution is
        // already impossible, and gateResolved makes this idempotent.
        let cleaned = false;
        function gateCleanup() {
          if (cleaned) return;
          cleaned = true;
          gateResolved = true;
          if (escRaf) { try { cancelAnimationFrame(escRaf); } catch (_e) {} escRaf = 0; }
          if (escTimer) { try { clearTimeout(escTimer); } catch (_e) {} escTimer = 0; }
          try { if (sub && sub._fzTimer) { clearInterval(sub._fzTimer); sub._fzTimer = 0; } } catch (_e) {}
          try { if (rumble && typeof rumble.stop === 'function') rumble.stop(0); } catch (_e) {}  // INSTANT
          rumble = null;
          try { stopVoice(0.1); } catch (_e) {}
          try { tint.remove(); } catch (_e) {}
          try { subWrap.remove(); } catch (_e) {}   // takes the sub child with it
          try { if (dot) dot.remove(); } catch (_e) {}
          notifyFreeze(false);
          try { if (typeof document !== 'undefined') document.body.classList.remove('ix-freeze'); } catch (_e) {}
          bgCall('setFrozen', false);
        }
        cleanups.push(gateCleanup);
      }

      // Build + run the Doki-Doki "are you sure?" overlay. The wrong press that
      // opened it is NEVER committed: "No" restores everything and the beat stays
      // live (the option is still pressable); catching the drifting "Yes" fires
      // the glitch/gif jumpscare and closes the window as an ABORT.
      function startSureEvent() {
        if (eventOpen) return;
        eventOpen = true;
        ensureEventStyles();

        // stop the run: kill the beat's own auto-advance timer so the frozen
        // card can't resolve behind the overlay, and silence the binaural bed.
        if (timeoutTimer) { clearTimeout(timeoutTimer); timeoutTimer = 0; }
        try { if (audio && typeof audio.suspendBed === 'function') audio.suspendBed(); } catch (_e) {}
        stopVoice(0.12); // the freeze cuts the spoken prompt too (bed already suspended)
        sfx('freeze-sting'); // harsh horror-freeze sting as the bed drops out
        // FREEZE: the injected CSS pauses animations inside the stage + effect
        // layers; the overlay lives on <body>, OUTSIDE those scopes, so it still
        // animates (and a running !important guard doubly protects it).
        try { document.body.classList.add('ix-freeze'); } catch (_e) {}

        const timers = [];
        const after = (fn, ms) => { const id = setTimeout(() => { try { fn(); } catch (_e) {} }, ms); timers.push(id); return id; };
        let rafId = 0;
        let done = false;                 // event resolved (Yes fired OR dismissed)

        // ---- overlay DOM (on <body>, above everything: veil + card) ----------
        const rootEl = el('div', 'ixev-root' + (reduced ? ' ixev-reduced' : ''));
        const veil = el('div', 'ixev-veil');
        const cardEl = el('div', 'ixev-card');
        const title = el('div', 'ixev-title', 'are you sure?');
        const faceEl = el('div', 'ixev-face', ':)');
        const btnRow = el('div', 'ixev-btns');
        const yesBtn = el('button', 'ixev-btn ixev-yes', 'Yes'); yesBtn.type = 'button';
        const noBtn = el('button', 'ixev-btn ixev-no', 'No'); noBtn.type = 'button';
        btnRow.append(yesBtn, noBtn);
        cardEl.append(title, faceEl, btnRow);
        rootEl.append(veil, cardEl);
        if (typeof document !== 'undefined') document.body.appendChild(rootEl);

        function hardCleanup() {
          if (rafId) { try { cancelAnimationFrame(rafId); } catch (_e) {} rafId = 0; }
          for (const id of timers) { try { clearTimeout(id); } catch (_e) {} }
          timers.length = 0;
          try { document.body.classList.remove('ix-freeze'); } catch (_e) {}
          try { rootEl.remove(); } catch (_e) {}
        }
        // teardown safety: if the beat/run tears down mid-event, unwind cleanly
        // (freeze off, bed resumed, overlay gone). The once-per-run latch stays.
        cleanups.push(() => {
          if (!done) { try { if (audio && typeof audio.resumeBed === 'function') audio.resumeBed(); } catch (_e) {} }
          eventOpen = false;
          hardCleanup();
        });

        // "No" (also the uncaught-drift fallback): restore + let the beat continue.
        function dismiss() {
          if (done) return;
          done = true;
          eventOpen = false;
          try { veil.classList.add('ixev-fadeout'); } catch (_e) {}
          try { if (audio && typeof audio.resumeBed === 'function') audio.resumeBed(); } catch (_e) {}
          try { document.body.classList.remove('ix-freeze'); } catch (_e) {}
          after(hardCleanup, 240);
        }
        noBtn.addEventListener('click', dismiss);

        // Close the host as an ABORT (no QuizRunResult). beats.js has no shim
        // handle, so reach it the same way it reaches loomField: a lazy import
        // of the singleton web-shim module (already loaded by boot.js).
        function closeIntakeHost() {
          const fallback = () => {
            try { if (typeof window !== 'undefined' && window.close) window.close(); } catch (_e) {}
            try { if (typeof location !== 'undefined') location.href = 'about:blank'; } catch (_e) {}
          };
          try {
            import('../web-shim.js')
              .then((shim) => { try { if (shim && typeof shim.closeHost === 'function') shim.closeHost(); else fallback(); } catch (_e) { fallback(); } })
              .catch(fallback);
          } catch (_e) { fallback(); }
        }

        // "Yes" caught -> the jumpscare (~1.2s), then close.
        function fireJumpscare() {
          if (done) return;
          done = true;
          eventOpen = false;
          try { yesBtn.style.pointerEvents = 'none'; } catch (_e) {}
          // 1) sounds: glitch burst + a spam of overlapping error blips
          try { if (audio && typeof audio.glitch === 'function') audio.glitch(); } catch (_e) {}
          try { if (audio && typeof audio.errorSpam === 'function') audio.errorSpam(8); } catch (_e) {}
          // 2) screen glitch (reduced motion: a plain black flash instead)
          try { rootEl.appendChild(el('div', reduced ? 'ixev-blackflash' : 'ixev-glitch')); } catch (_e) {}
          // 3) fullscreen gif flash ON TOP of the glitch (~1s)
          const gifs = (media && Array.isArray(media.gifs))
            ? media.gifs.filter((u) => typeof u === 'string' && u.length > 0) : [];
          after(() => {
            try {
              if (!gifs.length) return;
              const g = gifs[Math.floor(Math.random() * gifs.length)];
              const im = document.createElement('img');
              im.className = 'ixev-gifflash';
              im.src = g; im.alt = ''; im.draggable = false;
              rootEl.appendChild(im);
            } catch (_e) {}
          }, 180);
          // 4) close the window (abort) — ~1.2s total
          after(closeIntakeHost, 1200);
        }
        yesBtn.addEventListener('click', fireJumpscare);

        // ---- Yes DRIFT: after ~600ms the button wanders off, shrinking -------
        after(() => {
          if (done) return;
          let rect;
          try { rect = yesBtn.getBoundingClientRect(); } catch (_e) { rect = { left: 0, top: 0, width: 96, height: 48 }; }
          const startX = rect.left, startY = rect.top;
          const bw = rect.width || 96, bh = rect.height || 48;
          // pin it in place so it can leave the card, then move via transform
          try {
            yesBtn.style.position = 'fixed';
            yesBtn.style.left = startX + 'px';
            yesBtn.style.top = startY + 'px';
            yesBtn.style.margin = '0';
            yesBtn.style.zIndex = '3';
            yesBtn.classList.add('ixev-drifting');
          } catch (_e) {}
          const W = (typeof window !== 'undefined' && window.innerWidth) || 1280;
          const H = (typeof window !== 'undefined' && window.innerHeight) || 720;
          // eventual off-screen target: a random edge point well outside the view
          const edge = Math.floor(Math.random() * 4);
          let tx, ty;
          switch (edge) {
            case 0:  tx = Math.random() * W; ty = -H * 0.5; break;         // top
            case 1:  tx = W + W * 0.5; ty = Math.random() * H; break;      // right
            case 2:  tx = Math.random() * W; ty = H + H * 0.5; break;      // bottom
            default: tx = -W * 0.5; ty = Math.random() * H; break;         // left
          }
          const driftDur = 8000 + Math.random() * 4000;      // 8-12s to fully leave
          const scaleMin = Math.max(0.35, Math.min(1, 28 / bh));   // keep >=28px hit height
          // two-sine wander per axis (reduced motion: none -> a straight slow line)
          const ampX = reduced ? 0 : 42 + Math.random() * 50;
          const ampY = reduced ? 0 : 42 + Math.random() * 50;
          const wxA = (2 * Math.PI) / (1.5 + Math.random() * 1.4), pxA = Math.random() * Math.PI * 2;
          const wxB = (2 * Math.PI) / (2.3 + Math.random() * 1.8), pxB = Math.random() * Math.PI * 2;
          const wyA = (2 * Math.PI) / (1.7 + Math.random() * 1.4), pyA = Math.random() * Math.PI * 2;
          const wyB = (2 * Math.PI) / (2.1 + Math.random() * 1.7), pyB = Math.random() * Math.PI * 2;
          const t0d = now();
          const stepDrift = () => {
            rafId = 0;
            if (done) return;
            const ts = (now() - t0d) / 1000;
            const p = Math.min(1.2, (now() - t0d) / driftDur);   // slight overrun offscreen
            let dx = (tx - startX) * Math.min(1, p);
            let dy = (ty - startY) * Math.min(1, p);
            if (!reduced) {
              dx += Math.sin(ts * wxA + pxA) * ampX + Math.sin(ts * wxB + pxB) * ampX * 0.5;
              dy += Math.sin(ts * wyA + pyA) * ampY + Math.sin(ts * wyB + pyB) * ampY * 0.5;
            }
            const scale = 1 - (1 - scaleMin) * Math.min(1, p);
            try { yesBtn.style.transform = 'translate(' + dx.toFixed(1) + 'px,' + dy.toFixed(1) + 'px) scale(' + scale.toFixed(3) + ')'; } catch (_e) {}
            // uncaught fallback: fully out of the viewport -> treat as "No"
            const cx = startX + dx + bw / 2, cy = startY + dy + bh / 2;
            if (cx < -bw || cx > W + bw || cy < -bh || cy > H + bh) { dismiss(); return; }
            if (typeof requestAnimationFrame === 'function') rafId = requestAnimationFrame(stepDrift);
          };
          if (typeof requestAnimationFrame === 'function') rafId = requestAnimationFrame(stepDrift);
        }, 600);
      }

      // ---- SteerContext construction (Phase-0 contract) ------------------
      function installSteering(steerOptions) {
        // Capture the option descriptors for the freeze gate BEFORE any early
        // return (works even when steering is absent/stub). Each entry is
        // {index, el, isCorrect, score} — the single choke point every options
        // renderer routes through.
        gateOptions = Array.isArray(steerOptions) ? steerOptions : null;
        if (!steering || !steering.installSteering) return;
        const ctx = {
          root: stage,
          options: steerOptions,
          mechanic: beat.mechanic,
          band: beat.band,
          depth: beat.depth,
          roll: beat.steerRoll || { primary: 'none', secondary: [], intensity: 0 },
          caps,
          escapeEffort: ESCAPE_EFFORT,
          escapeMs: ESCAPE_MS,
          onCommit: (fn) => {
            if (typeof fn !== 'function') return () => {};
            interceptors.push(fn);
            return () => { const i = interceptors.indexOf(fn); if (i >= 0) interceptors.splice(i, 1); };
          },
          forceComplete: (index) => {
            steeredFlag = true;
            if (index != null) lastAttemptIndex = index;
            tryCommit(index, { force: true });
          },
          markProgress: () => { steeredFlag = true; noteEffort(); armWatchdog(); },
        };
        try { steerHandle = steering.installSteering(ctx); } catch (_e) { steerHandle = null; }
      }

      // ---- build the card scaffold ---------------------------------------
      const mech = selectMechanic(beat);
      const prompt = beat.prompt || {};
      const card = el('div', 'ib-card ib-mech-' + beat.mechanic + ' ' + (BAND_ENTER[beat.band] || 'ib-enter-crisp'));
      const q = el('p', 'ib-q');
      const promptText = (prompt.text || '') + (prompt.flavor ? ` (${prompt.flavor})` : '');
      if (mech === Mechanic.Interlude) q.textContent = promptText;
      else applyPromptText(q, promptText);
      card.appendChild(q);

      // VOICEOVER: speak the prompt line roughly WITH the text reveal (fired just
      // after the typewriter/text is set, not before). Interludes are pacing valleys
      // with a synthetic prompt (no bank id) — never voiced; every other mechanic
      // (incl. BubblePop, which carries a real bank prompt) speaks. A missing clip /
      // manifest entry is silent. Stopped on resolve/teardown via cleanups below.
      // voiceStartedAt lets a set-piece resume the SAME line from roughly where
      // it is now (playVoice takes an offset) instead of restarting it — see
      // meltVoice() on the card-melt whimsy.
      let voiceStartedAt = 0;
      if (mech !== Mechanic.Interlude) {
        speakPrompt(beat);
        voiceStartedAt = Date.now();
        cleanups.push(() => stopVoice(0.3));
      }

      // ---- background steer-warp (heavy steers distort the tube) ----------
      try {
        const ri = beat.steerRoll && typeof beat.steerRoll.intensity === 'number'
          ? beat.steerRoll.intensity : 0;
        if (ri >= 0.5) { bgCall('setSteerWarp', ri); warpSet = true; }
      } catch (_e) {}

      // ---- options hold (see the OPTIONS_HOLD_* block) --------------------
      // Computed here (not after the switch) because the timeout below has to
      // account for it. Mechanics with no option grid resolve to 0.
      const optionsHoldMs = computeOptionsHold();

      // ---- timeout (auto-submit timedOut) + shrinking-ring pressure cue ---
      // The clock is armed at mount but EXTENDED by the options hold, so a timed
      // beat still gives its full intended answer window once the buttons land.
      if (beat.timeoutMs && beat.timeoutMs > 0) {
        const totalMs = beat.timeoutMs + optionsHoldMs;
        timeoutTimer = setTimeout(() => {
          timeoutTimer = 0;
          finalize(lastAttemptIndex, lastAttemptValue, true);
        }, totalMs);
        buildTimerRing(totalMs);
      }

      // Normalize option specs -> {index,label,isCorrect,score}
      const rawOpts = (Array.isArray(beat.options) ? beat.options : []).map((o, i) => ({
        index: typeof o.index === 'number' ? o.index : i,
        label: o.label != null ? o.label : String(o.index != null ? o.index : i),
        isCorrect: !!o.isCorrect,
        score: typeof o.score === 'number' ? o.score : (o.isCorrect ? 1 : 0),
      }));

      // CHANGE 1 shared roll: wireSlider runs INSIDE the switch below (via
      // renderCheckIn) — BEFORE the tilt block further down — and sets this true iff
      // THIS slider beat won its single SLIDER_AUTORISE roll and armed the autonomous
      // rise. The tilt block reads it to force the lean toward the slider's increasing
      // side. ONE roll, two readers (never re-rolled for the tilt).
      let sliderAutoRoseArmed = false;

      switch (mech) {
        case Mechanic.MC4:      renderChoices(rawOpts, 'ib-opts-grid'); break;
        case Mechanic.YesNo:    renderYesNo(); break;
        case Mechanic.Mono:     renderMono(); break;
        case Mechanic.Funnel:   renderFunnel(); break;
        case Mechanic.Destruct: renderDestruct(); break;
        case Mechanic.BubblePop:renderBubblePop(); break;
        case Mechanic.CheckIn:  renderCheckIn(); break;
        case Mechanic.Mantra:   renderMantra(); break;
        case Mechanic.Interlude:renderInterlude(); break;
        default:                renderChoices(rawOpts.length ? rawOpts : [{ index: 0, label: 'Continue', isCorrect: true, score: 1 }], 'ib-opts-grid');
      }

      // COLOUR-NAMED OPTIONS wear their colour (render/optionTint.js): any
      // `.ib-opt` whose label resolves through the harvest's own swatch table
      // gets a dot + tinted border in that colour. Runs AFTER the switch so it
      // covers whichever renderer built the grid, and BEFORE holdOptions so a
      // held grid reveals already cued. From Band.Deepening on the cue
      // deliberately lies (a different colour than the word) — the module owns
      // that roll, including the exemption for the harvest prompts themselves.
      try {
        tintColorOptions(card, { prompt, band: beat.band, depth: beat.depth, caps, reduced });
      } catch (_e) { /* a cue is decoration; it can never cost a beat */ }

      // Hold the freshly-built option grid(s) until the prompt has finished
      // presenting itself. Runs AFTER the switch so every options mechanic is
      // covered, whichever renderer built the grid.
      if (optionsHoldMs > 0) holdOptions(optionsHoldMs);

      // ---- occasional SLOW card tilt + almost-imperceptible sway (lv2+) ---
      // From Establishing (lv2) onward a small fraction (25%) of standard,
      // centered question cards SLOWLY lean into a slight static angle and get a
      // barely-visible slow rotation sway (+-0.6deg, 9-14s, ease-in-out, infinite
      // alternate). Gated on beat.band (NOT qIndex — Calibration/lv1 gets NO tilt;
      // the old qIndex>=4 gate was the "tilting since the start" bug, since q4 is
      // still Calibration). The max magnitude DOUBLES per level — Establishing
      // +-4deg, Deepening +-8deg, Climax +-16deg, Recovery +-32deg — and the
      // actual angle is a random magnitude UP TO that cap (floored at 30% of the
      // cap so it never degrades to a nothing ~0.1deg) with a random sign.
      // Fullscreen interludes and the 4th-wall BubblePop field (its slot geometry
      // reads the card's live rect, which a rotation would skew) are never tilted.
      // Nested wrappers keep this off the card's own transform: an outer
      // static-tilt wrapper + an inner sway wrapper, so the card's band ENTRY/EXIT
      // transforms compose untouched. The card MOUNTS STRAIGHT and eases into the
      // lean after the ~600ms settle over a VERY long transition (see .ib-tiltwrap:
      // ~16s ease-in-out) — a slooooow lean the eye barely catches. Reduced motion:
      // no tilt at all (a slow drift can't be shown; the sway is neutralized too).
      const qIndex = (beat.meta && typeof beat.meta.qIndex === 'number') ? beat.meta.qIndex : 0;
      // per-band max tilt magnitude (deg); doubles each level. Absent band => 0.
      const TILT_CAP = {
        [Band.Establishing]: 4,
        [Band.Deepening]:    8,
        [Band.Climax]:       16,
        [Band.Recovery]:     32,
      };
      const tiltCap = TILT_CAP[beat.band] || 0;    // 0 => Calibration/unknown: no tilt
      const tiltEligible = !reduced
        && tiltCap > 0
        && mech !== Mechanic.Interlude
        && mech !== Mechanic.BubblePop;
      let mountEl = card;
      if (tiltEligible && Math.random() < 0.25) {
        // random magnitude UP TO the band cap, floored at 30% of the cap so it
        // always reads, random sign.
        const mag = tiltCap * (0.3 + Math.random() * 0.7);
        // SIGN: normally a random lean. But when THIS (slider) beat also armed its
        // autonomous rise, force the lean toward the slider's increasing side — values
        // rise to the RIGHT, so a clockwise / POSITIVE rotation leans the card right,
        // reading as the CAUSE of the drift. Magnitude/animation stay exactly as the
        // band dictates; ONLY the sign is forced, and NO extra roll is added (the 30%
        // SLIDER_AUTORISE roll already happened once, in wireSlider).
        const deg = sliderAutoRoseArmed ? mag : (Math.random() < 0.5 ? -mag : mag);
        const tilt = el('div', 'ib-tiltwrap');
        // the card MOUNTS STRAIGHT (0deg) then VERY slowly eases into its target
        // lean: we apply the tilt value ~after the entry animation settles
        // (600ms), and .ib-tiltwrap's long transform transition (~16s
        // ease-in-out) drifts it there — a slooooow lean, not a pre-tilted pop-in.
        // The inner ±0.6deg sway composes on top on .ib-swaywrap, unchanged.
        tilt.style.setProperty('--ib-tilt', '0deg');
        const sway = el('div', 'ib-swaywrap');
        const per = 9 + Math.random() * 5;               // 9..14s per sweep
        sway.style.animationDuration = per.toFixed(2) + 's';
        sway.style.animationDelay = (-Math.random() * per).toFixed(2) + 's'; // desync phase
        sway.appendChild(card);
        tilt.appendChild(sway);
        let tiltTimer = setTimeout(() => {
          tiltTimer = 0;
          try { tilt.style.setProperty('--ib-tilt', deg.toFixed(2) + 'deg'); } catch (_e) {}
        }, 600);
        cleanups.push(() => { if (tiltTimer) { clearTimeout(tiltTimer); tiltTimer = 0; } });
        mountEl = tilt;
      }

      // ---- progressive slow zoom on CLIMAX + RECOVERY question cards --------
      // In the run's last two bands the card, once SETTLED (~600ms after mount,
      // the same beat the tilt eases in), begins a slow continuous zoom-in from
      // scale 1 toward a cap. The cap is simply the target of a long ease-out
      // transition (~15s) so the zoom never finishes abruptly — it keeps
      // creeping for the whole dwell. "More dramatic as we move on": the cap
      // ramps with progression — gentle (~1.06) in early climax, up to ~1.18 by
      // the last recovery beats — derived from qIndex (the 1-based graded
      // counter) against a ~81 horizon, +0.006 per question past ZOOM_Q0, HARD-
      // capped below 1.2 so a 760px card + its buttons stay comfortably on a
      // 1366px viewport. Zoom rides its OWN outermost wrapper (one transform
      // per wrapper — the established tilt/sway pattern), composing untouched
      // with the tilt/sway transitions and the card's own entry/exit + melt.
      // Excluded like the tilt: fullscreen interludes + the 4th-wall BubblePop
      // field. Reduced motion skips it entirely (no wrapper created).
      // Ramp origin — early-climax territory. Tracks BASE_BEATS: after the second
      // dilution pass Climax opens around q65 (21+25+19), so the old q50 origin
      // would start the zoom in mid-DEEPENING and arrive at Climax already pinned
      // near its cap. Kept just under the climax opener so the ramp spans the band.
      const ZOOM_Q0 = 64;
      const zoomEligible = !reduced
        && (beat.band === Band.Climax || beat.band === Band.Recovery)
        && mech !== Mechanic.Interlude
        && mech !== Mechanic.BubblePop;
      if (zoomEligible) {
        const zoomCap = Math.min(1.18, 1.06 + Math.max(0, qIndex - ZOOM_Q0) * 0.006);
        const zoom = el('div', 'ib-zoomwrap');
        zoom.style.setProperty('--ib-zoom', '1');    // mount at 1 (no jump)
        zoom.appendChild(mountEl);
        let zoomTimer = setTimeout(() => {
          zoomTimer = 0;
          try { zoom.style.setProperty('--ib-zoom', zoomCap.toFixed(3)); } catch (_e) {}
        }, 600);
        cleanups.push(() => { if (zoomTimer) { clearTimeout(zoomTimer); zoomTimer = 0; } });
        mountEl = zoom;
      }

      if (stage) stage.appendChild(mountEl);

      // ---- 1-in-45 melt-on-card-click -> SKIP to next beat (standard cards) ----
      // Clicking anywhere on the card that is NOT an interactive control has a
      // MELT_CHANCE (1 in 45, ONCE per beat) to melt the whole card away. The card does
      // NOT re-form: once the melt animation settles the beat RESOLVES and the
      // run advances. The melt is a WHIMSY event, not an answer — it resolves
      // through the gentlest existing skip path (finalize(undefined, undefined,
      // true) — the same "no value, timedOut" resolution the mantra skip and an
      // expired timer use), so it scores nothing, never counts correct/incorrect,
      // and CANNOT arm the "are you sure?" jumpscare (that only fires from
      // attempt(), which this path never touches). Fullscreen interludes and the
      // 4th-wall BubblePop field are excluded. The CARD element itself animates,
      // so it composes INSIDE any tilt/sway wrapper without touching them.
      // Reduced motion swaps the long melt for a quick fade. The melt timer
      // registers with the beat's cleanups[] so mid-melt teardown can't leave an
      // orphaned callback.
      if (mech !== Mechanic.Interlude && mech !== Mechanic.BubblePop) {
        let meltUsed = false;
        let melt1 = 0;
        cleanups.push(() => { if (melt1) clearTimeout(melt1); });
        const onCardClick = (e) => {
          // no melt while the freeze gate holds, and never on a corrupted beat
          // (one set-piece per beat — corruption already owns the card).
          if (committed || meltUsed || gateFrozen || corruptLive) return;
          try {
            if (e.target && e.target.closest
              && e.target.closest('button,input,textarea,select,a,[contenteditable]')) return;
          } catch (_e) {}
          if (Math.random() >= MELT_CHANCE) return;   // 1-in-45 chance, else a normal click
          meltUsed = true;                      // max once per beat
          card.style.pointerEvents = 'none';    // nothing is clickable on a melting card
          // Clear the beat's auto-advance timer so it can't resolve BEHIND the
          // melt (the same guard the jumpscare uses). The melt itself advances
          // the beat once it settles, and finalize()'s committed-latch makes a
          // double-resolution (melt vs. any late timer/steer force-commit)
          // impossible.
          if (timeoutTimer) { clearTimeout(timeoutTimer); timeoutTimer = 0; }
          // drop any (already-settled) band ENTRY class so our animation wins
          card.classList.remove('ib-enter-crisp', 'ib-enter-melt', 'ib-enter-sink', 'ib-enter-dawn');
          card.classList.add('ib-cardmelt-anim');
          sfx('card-melt'); // gooey melt-on-click cue (no reform: fires once, at start)
          const meltMs = reduced ? 200 : 700;
          const holdMs = reduced ? 80 : 250;
          meltVoice(meltMs + holdMs);   // the VOICE sags and dissolves with the card
          melt1 = setTimeout(() => {
            melt1 = 0;
            // no re-form: resolve as a gentle skip and advance to the next beat
            finalize(undefined, undefined, true);
          }, meltMs + holdMs);
        };
        card.addEventListener('click', onCardClick);
        cleanups.push(() => { try { card.removeEventListener('click', onCardClick); } catch (_e) {} });

        // Melt the SPOKEN line along with the card: resume the same clip from
        // roughly where it is now but slowed to a drawl (the audio layer plays one
        // buffer at a time with no live rate control, so a re-trigger at an offset
        // is the only way to bend a line already in flight), then fade the tail
        // out across the melt so the voice dissolves instead of cutting.
        // Reduced motion / a finished line / a missing clip -> plain fade.
        function meltVoice(durMs) {
          try {
            const pid = beat && beat.prompt && beat.prompt.id;
            const elapsed = voiceStartedAt ? (Date.now() - voiceStartedAt) / 1000 : 0;
            const canSlur = !reduced && pid && voiceStartedAt
              && audio && typeof audio.voice === 'function'
              && elapsed > 0.15 && elapsed < MELT_VOICE_MAX_ELAPSED_S;
            if (!canSlur) {
              try { stopVoice(MELT_VOICE_FADE_S); } catch (_e) {}
              return;
            }
            try { audio.voice('q_' + pid, { rate: MELT_VOICE_RATE, offset: elapsed }); } catch (_e) {}
            let voT = setTimeout(() => {
              voT = 0;
              try { stopVoice(MELT_VOICE_FADE_S); } catch (_e) {}
            }, Math.round(durMs * MELT_VOICE_SAG_FRAC));
            cleanups.push(() => { if (voT) { clearTimeout(voT); voT = 0; } });
          } catch (_e) {}
        }
      }

      /* ---- CORRUPTED QUESTION (Deepening / Climax) ----------------------
       * Rolled ONCE per beat, shortly after the prompt has finished revealing
       * (so the glitch/melt hits a fully-typed line, not a half-typed one).
       * See the CORRUPT_* tuning block at the top of the file. Never touches
       * options, never resolves the beat, never throws. */
      maybeArmCorruption();

      function maybeArmCorruption() {
        try {
          if (mech === Mechanic.Interlude || mech === Mechanic.BubblePop) return;
          const chance = CORRUPT_CHANCE[beat.band];
          if (!chance) return;                                   // Deepening/Climax only
          const qNo = (beat && beat.meta && beat.meta.qIndex) || 0;
          if (qNo < CORRUPT_MIN_Q) return;
          if (gateUsedThisBeat || gateFrozen || eventOpen || committed) return;
          if (!promptText) return;                               // nothing to corrupt
          if (Math.random() >= chance) return;
          gateUsedThisBeat = true;    // MUTUAL EXCLUSION: jumpscare + freeze gate locked out
          corruptLive = true;
          const glitch = Math.random() < CORRUPT_GLITCH_SPLIT;
          // wait out the deep-band typewriter reveal (~18ms/char) before breaking it
          const revealMs = reduced ? 200 : (promptText.length * 18 + 260);
          let armT = setTimeout(() => {
            armT = 0;
            if (committed || gateFrozen || eventOpen) { corruptLive = false; return; }
            try { if (glitch) startCorruptGlitch(); else startCorruptMelt(); }
            catch (_e) { corruptLive = false; }
          }, revealMs);
          cleanups.push(() => { if (armT) { clearTimeout(armT); armT = 0; } });
        } catch (_e) { corruptLive = false; }
      }

      /* FLAVOR A — GLITCH. The line datamoshes + scrambles for CORRUPT_GLITCH_MS
       * with a broken reward GIF punching through, then the feed RECOVERS (the
       * original text is restored, so a late reader can still read the prompt).
       * VO: the audio layer plays one buffered clip at a time, so the "garble"
       * is built from what it really supports — hard re-triggers of the same
       * line at random offsets with wobbled playbackRate (a skipping/stuttering
       * record), a final hard cut, and a glitch stab layered over it. */
      function startCorruptGlitch() {
        ensureCorruptStyles();
        const original = (q && q.textContent) || promptText;
        const timers = [];
        const after = (fn, ms) => { const id = setTimeout(() => { try { fn(); } catch (_e) {} }, ms); timers.push(id); return id; };
        let scrambleTimer = 0;
        let killGif = null;
        let cleaned = false;

        try {
          q.dataset.ixcr = original;                 // RGB-split ghost copies read this
          q.classList.add('ixcr-glitch');
          if (reduced) q.classList.add('ixcr-reduced');
        } catch (_e) {}

        // audio: glitch stab (real sample w/ synth fallback) + a couple of blips
        try { if (audio && typeof audio.glitch === 'function') audio.glitch(); } catch (_e) {}
        try { if (audio && typeof audio.errorSpam === 'function' && !reduced) audio.errorSpam(3); } catch (_e) {}

        // VO garble: chop the spoken prompt into stuttering re-triggers.
        const pid = beat && beat.prompt && beat.prompt.id;
        if (pid && audio && typeof audio.voice === 'function') {
          if (reduced) {
            after(() => { try { stopVoice(0.08); } catch (_e) {} }, 60);   // plain hard cut
          } else {
            let t = 0;
            const chops = 3 + Math.floor(Math.random() * 3);               // 3..5 stutters
            for (let i = 0; i < chops; i++) {
              t += 130 + Math.random() * 220;
              after(() => {
                try { stopVoice(0.01); } catch (_e) {}
                try {
                  audio.voice('q_' + pid, {
                    rate: 0.68 + Math.random() * 0.7,                      // 0.68..1.38
                    offset: Math.random() * 1.6,                           // skip into the line
                  });
                } catch (_e) {}
              }, t);
            }
            after(() => { try { stopVoice(0.02); } catch (_e) {} }, t + 200);   // hard cut
          }
        }

        // text scramble (skipped under reduced motion: one static mangle instead)
        if (reduced) {
          try { q.textContent = scrambleText(original, 0.35); } catch (_e) {}
        } else {
          const tStart = now();
          scrambleTimer = setInterval(() => {
            const p = Math.min(1, (now() - tStart) / CORRUPT_GLITCH_MS);
            const amount = 0.55 * (1 - p) + 0.12;      // heavy at first, settling
            try {
              const s = scrambleText(original, amount);
              q.textContent = s;
              q.dataset.ixcr = s;
            } catch (_e) {}
          }, 62);
        }

        killGif = mountBrokenGif();

        function restore() {
          if (cleaned) return;
          cleaned = true;
          corruptLive = false;
          if (scrambleTimer) { try { clearInterval(scrambleTimer); } catch (_e) {} scrambleTimer = 0; }
          for (const id of timers) { try { clearTimeout(id); } catch (_e) {} }
          timers.length = 0;
          try { if (killGif) killGif(); } catch (_e) {}
          killGif = null;
          try {
            q.classList.remove('ixcr-glitch', 'ixcr-reduced');
            delete q.dataset.ixcr;
            q.textContent = original;                 // the feed recovers
          } catch (_e) {}
        }
        after(restore, CORRUPT_GLITCH_MS);
        cleanups.push(restore);                       // mid-beat teardown safe
      }

      /* FLAVOR B — MELT. A long, gradual blur/drip that dissolves the PROMPT
       * TEXT ONLY, ending on an empty line. The line's box height is pinned
       * first so the card/options never reflow, and pointer-events are dropped
       * on the melting text so the beat stays fully usable underneath.
       * VO melts with it: the line is re-triggered slowed (a drawl) and then
       * faded out over ~1.6s instead of being cut. */
      function startCorruptMelt() {
        ensureCorruptStyles();
        const meltMs = reduced ? 1400 : CORRUPT_MELT_MS;
        let doneT = 0;
        let voT = 0;
        let cleaned = false;

        try {
          const h = q.offsetHeight;
          if (h) q.style.minHeight = h + 'px';        // pin the box: no reflow when empty
          q.style.setProperty('--ixcr-melt-ms', meltMs + 'ms');
          q.classList.add('ixcr-melt' + (reduced ? ' ixcr-reduced' : ''));
          q.style.pointerEvents = 'none';
        } catch (_e) {}
        sfx('card-melt');                             // gooey cue, once, at the start

        // slowed VO drawl, then a long fade instead of a cut
        const pid = beat && beat.prompt && beat.prompt.id;
        if (!reduced && pid && audio && typeof audio.voice === 'function') {
          try { audio.voice('q_' + pid, { rate: 0.82 }); } catch (_e) {}
          voT = setTimeout(() => { voT = 0; try { stopVoice(1.6); } catch (_e) {} }, Math.round(meltMs * 0.55));
        }

        // kick the transition on the next frame so it actually animates
        const go = () => {
          try {
            if (cleaned) return;
            q.classList.add('ixcr-melt-go');
          } catch (_e) {}
        };
        if (typeof requestAnimationFrame === 'function') requestAnimationFrame(() => requestAnimationFrame(go));
        else go();

        doneT = setTimeout(() => {
          doneT = 0;
          try {
            q.textContent = '';                       // ends EMPTY (options untouched)
            delete q.dataset.ixcr;
          } catch (_e) {}
          corruptLive = false;
        }, meltMs + 120);

        cleanups.push(() => {
          if (cleaned) return;
          cleaned = true;
          corruptLive = false;
          if (doneT) { clearTimeout(doneT); doneT = 0; }
          if (voT) { clearTimeout(voT); voT = 0; }
        });
      }

      /* Broken-GIF overlay for FLAVOR A: one of the app's REAL reward/flash GIFs
       * (media.gifs, host-supplied data: URIs — the same pool the jumpscare draws
       * from), deliberately destroyed. Method: a tiny backing-store canvas
       * (CORRUPT_GIF_PX wide) that the CSS stretches over the prompt box with
       * image-rendering: pixelated -> hard chunky pixels; redrawn choppily at
       * ~13fps (sampling the live GIF's current frame), with random slice tears
       * + an occasional STUCK frame + crushed contrast. Reduced motion: ONE
       * static frame, no tearing, no redraw. Returns its own kill().
       * Absent media (standalone browser / m2Test harness) -> a no-op. */
      function mountBrokenGif() {
        const noop = () => {};
        try {
          if (typeof document === 'undefined') return noop;
          const gifs = (media && Array.isArray(media.gifs))
            ? media.gifs.filter((u) => typeof u === 'string' && u.length > 0) : [];
          if (!gifs.length) return noop;
          const boxW = q.offsetWidth || 0;
          const boxH = q.offsetHeight || 0;
          if (!boxW || !boxH) return noop;

          const wrap = el('div', 'ixcr-gifwrap');
          const cv = document.createElement('canvas');
          cv.className = 'ixcr-gif';
          cv.width = CORRUPT_GIF_PX;
          cv.height = Math.max(16, Math.round(CORRUPT_GIF_PX * (boxH / boxW)));
          const ctx = cv.getContext ? cv.getContext('2d') : null;
          if (!ctx) return noop;
          try { ctx.imageSmoothingEnabled = false; } catch (_e) {}

          // caps: flashOpacity + masterIntensity scale the overlay's presence
          const fo = (caps && caps.flashOpacity != null) ? clamp01(caps.flashOpacity) : 1;
          const mi = (caps && caps.masterIntensity != null) ? clamp01(caps.masterIntensity) : 1;
          const alpha = clamp01(CORRUPT_GIF_ALPHA * fo * mi);
          if (alpha <= 0.02) return noop;

          wrap.style.left = (q.offsetLeft - 6) + 'px';
          wrap.style.top = (q.offsetTop - 6) + 'px';
          wrap.style.width = (boxW + 12) + 'px';
          wrap.style.height = (boxH + 12) + 'px';
          wrap.style.opacity = alpha.toFixed(3);
          wrap.appendChild(cv);
          card.appendChild(wrap);

          let img = new Image();
          let timer = 0;
          let killed = false;
          const draw = () => {
            if (killed || !img || !img.width) return;
            const W = cv.width, H = cv.height;
            // occasional STUCK frame: skip the redraw entirely
            if (!reduced && Math.random() < 0.18) return;
            try {
              ctx.clearRect(0, 0, W, H);
              ctx.filter = 'contrast(1.9) saturate(0.55) brightness(1.1)';
              // cover-crop the source into the box
              const sr = img.width / img.height, dr = W / H;
              let sw = img.width, sh = img.height, sx = 0, sy = 0;
              if (sr > dr) { sw = img.height * dr; sx = (img.width - sw) / 2; }
              else { sh = img.width / dr; sy = (img.height - sh) / 2; }
              ctx.drawImage(img, sx, sy, sw, sh, 0, 0, W, H);
              ctx.filter = 'none';
              if (!reduced) {
                // slice tear: shove a few random rows sideways (self-copy)
                const slices = 2 + Math.floor(Math.random() * 3);
                for (let i = 0; i < slices; i++) {
                  const y = Math.floor(Math.random() * H);
                  const h = 1 + Math.floor(Math.random() * Math.max(1, H / 6));
                  const dx = Math.round((Math.random() * 2 - 1) * (W / 5));
                  ctx.drawImage(cv, 0, y, W, h, dx, y, W, h);
                }
                // channel bruise: a magenta/cyan wash over one band
                ctx.globalCompositeOperation = 'lighter';
                ctx.globalAlpha = 0.22;
                ctx.fillStyle = Math.random() < 0.5 ? '#ff2bd0' : '#20e8ff';
                const by = Math.floor(Math.random() * H);
                ctx.fillRect(0, by, W, 1 + Math.floor(Math.random() * (H / 5)));
                ctx.globalAlpha = 1;
                ctx.globalCompositeOperation = 'source-over';
              }
            } catch (_e) {}
          };
          const kill = () => {
            if (killed) return;
            killed = true;
            if (timer) { try { clearInterval(timer); } catch (_e) {} timer = 0; }
            try { if (img) { img.onload = null; img.onerror = null; img.src = ''; } } catch (_e) {}
            img = null;
            try { wrap.remove(); } catch (_e) {}
          };
          img.onerror = kill;
          img.onload = () => {
            if (killed) return;
            draw();
            if (!reduced) timer = setInterval(draw, CORRUPT_GIF_FRAME_MS);
          };
          try { img.src = gifs[(Math.random() * gifs.length) | 0]; } catch (_e) { kill(); return noop; }
          return kill;
        } catch (_e) { return noop; }
      }

      /* ===== shared presentation helpers ================================= */

      // How long the option buttons stay out of reach after mount. Sized to the
      // deep-band typewriter (~18ms/char) so the line lands first, floored at
      // OPTIONS_HOLD_MIN_MS and ceilinged so a long prompt can't stall the run.
      // Mechanics without an option grid (interlude valleys, the 4th-wall bubble
      // field, sliders/mantra which have their own affordance) hold nothing.
      function computeOptionsHold() {
        try {
          if (mech === Mechanic.Interlude || mech === Mechanic.BubblePop) return 0;
          if (mech === Mechanic.CheckIn || mech === Mechanic.Mantra) return 0;
          const deepBand = beat.band === Band.Deepening || beat.band === Band.Climax;
          const typeMs = (deepBand && !reduced) ? promptText.length * 18 : 0;
          const want = typeMs + OPTIONS_HOLD_AFTER_TYPE_MS;
          return Math.max(OPTIONS_HOLD_MIN_MS, Math.min(OPTIONS_HOLD_MAX_MS, want));
        } catch (_e) { return OPTIONS_HOLD_MIN_MS; }
      }

      // Mount the option grid(s) inert + transparent, then fade them in together
      // after `ms`. Inert matters as much as invisible: a fast player must not be
      // able to click a button that has not been offered yet. Teardown mid-hold
      // restores the grid (cleanups[]) so nothing can be left permanently hidden.
      function holdOptions(ms) {
        try {
          const grids = card.querySelectorAll ? card.querySelectorAll('.ib-opts-grid') : null;
          if (!grids || !grids.length) return;
          const list = Array.prototype.slice.call(grids);
          list.forEach((g) => { try { g.classList.add('ib-opts-held'); } catch (_e) {} });
          const reveal = () => {
            revealTimer = 0;
            list.forEach((g) => { try { g.classList.remove('ib-opts-held'); } catch (_e) {} });
          };
          let revealTimer = setTimeout(reveal, ms);
          cleanups.push(() => {
            if (revealTimer) { clearTimeout(revealTimer); revealTimer = 0; }
            reveal();
          });
        } catch (_e) {}
      }

      // Deep-band prompt text: typewriter (~18ms/char, click-to-complete) on
      // first render, gentle "breathe" pulse after. Shallow bands: instant.
      function applyPromptText(node, text) {
        const deepBand = beat.band === Band.Deepening || beat.band === Band.Climax;
        if (!deepBand) { node.textContent = text; return; }
        if (reduced) {
          // reduced motion: instant text; ib-breathe is neutered by the media query
          node.textContent = text;
          node.classList.add('ib-breathe');
          return;
        }
        node.classList.add('ib-typing');
        node.textContent = '';
        let i = 0, done = false, timer = 0;
        const finish = () => {
          if (done) return;
          done = true;
          if (timer) { clearInterval(timer); timer = 0; }
          node.textContent = text;
          node.classList.remove('ib-typing');
          node.classList.add('ib-breathe');
        };
        timer = setInterval(() => {
          i += 1;
          node.textContent = text.slice(0, i);
          if (i >= text.length) finish();
        }, 18);
        const onClick = () => finish();
        node.addEventListener('click', onClick);
        cleanups.push(() => {
          if (timer) clearInterval(timer);
          node.removeEventListener('click', onClick);
        });
      }

      // Shrinking SVG ring + accelerating tick tension; final 20% goes urgent.
      function buildTimerRing(totalMs) {
        try {
          const R = 15, C = 2 * Math.PI * R;
          const wrap = el('div', 'ib-timer');
          wrap.innerHTML =
            '<svg viewBox="0 0 36 36" aria-hidden="true">' +
            '<circle class="ib-timer-track" cx="18" cy="18" r="' + R + '"></circle>' +
            '<circle class="ib-timer-arc" cx="18" cy="18" r="' + R + '" stroke-dasharray="' + C.toFixed(2) + '" stroke-dashoffset="0"></circle>' +
            '</svg>';
          card.appendChild(wrap);
          const arc = wrap.querySelector('.ib-timer-arc');
          if (typeof requestAnimationFrame !== 'function') return;
          const start = now();
          let raf = 0;
          // pressure-heartbeat: a looping thud that starts ONCE on urgent-enter
          // (final 20%) and is stopped/faded on beat resolve/teardown below.
          let heartbeat = null;
          // FREEZE-GATE HOLD: while frozen the ring clock stops (frozenAccum banks
          // the paused time so the arc resumes exactly where it stopped) and the
          // pressure-heartbeat is silenced; both resume cleanly on unfreeze.
          let frozenAccum = 0, frozenAt = 0;
          freezeSubs.push((fz) => {
            if (fz) {
              if (!frozenAt) frozenAt = now();
              if (heartbeat && typeof heartbeat.stop === 'function') { try { heartbeat.stop(0.15); } catch (_e) {} heartbeat = null; }
            } else if (frozenAt) {
              frozenAccum += now() - frozenAt; frozenAt = 0;
            }
          });
          const step = () => {
            raf = 0;
            if (committed) return;
            const held = frozenAt ? (now() - frozenAt) : 0;
            const p = Math.min(1, (now() - start - frozenAccum - held) / totalMs);
            if (arc) arc.style.strokeDashoffset = (C * p).toFixed(2);
            if (p >= 0.8 && !frozenAt) {
              wrap.classList.add('is-urgent');
              if (!heartbeat) heartbeat = sfx('pressure-heartbeat', 1, { loop: true });
            }
            if (!reduced && !frozenAt) {
              // accelerating tick: a small pulse whose frequency climbs with p
              const freq = 1 + p * 5;
              const s = 1 + 0.05 * Math.max(0, Math.sin(((now() - start - frozenAccum) / 1000) * freq * Math.PI * 2)) * (0.3 + p);
              wrap.style.transform = 'scale(' + s.toFixed(3) + ')';
            }
            // keep looping while frozen (p is held) so the ring resumes in place.
            if (p < 1 || frozenAt) raf = requestAnimationFrame(step);
          };
          raf = requestAnimationFrame(step);
          cleanups.push(() => {
            if (raf) cancelAnimationFrame(raf);
            if (heartbeat && typeof heartbeat.stop === 'function') {
              try { heartbeat.stop(0.4); } catch (_e) {}
              heartbeat = null;
            }
          });
        } catch (_e) {}
      }

      // ---- shared Loom-spiral backdrop helper --------------------------------
      // Used by BOTH the 'watch' interlude (fullscreen, alpha 1) AND every
      // BubblePop beat (fullscreen faded backdrop, alpha .25). A FRESH random
      // spiral is rolled per mount.

      // guarded seam-log to the host (same 'intake-log' channel effects.js uses)
      function loomLog(msg) {
        try {
          if (typeof window !== 'undefined' && typeof CustomEvent === 'function') {
            window.dispatchEvent(new CustomEvent('intake-log', { detail: { msg: 'ib: ' + msg } }));
          }
        } catch (_e) {}
      }

      // Mount the app's REAL Loom spiral onto `host`, faded to `alpha`. Lazy
      // dynamic import of the shared module (same path + API as effects.js's
      // spiral garnish — READ-only reference; we never touch effects.js). rAF
      // only while live; reduced motion paints ONE static frame. Self-registers
      // teardown in cleanups[]. On import failure: emits a seam-log and calls
      // onFail() so the caller can restore/retire its own fallback visual.
      function mountLoomSpiral(host, alpha, onFail) {
        let cancelled = false, raf = 0, canvas = null;
        cleanups.push(() => {
          cancelled = true;
          if (raf) { try { cancelAnimationFrame(raf); } catch (_e) {} raf = 0; }
          // NOTE: the canvas is intentionally NOT removed here. Every caller
          // wraps it in a fullscreen layer that fades OUT and self-removes
          // (fadeLayerOutRemove), so the last painted frame stays visible
          // through the fade instead of hard-cutting. rAF is still cancelled so
          // nothing keeps drawing into the (now static) detached frame.
        });
        (async () => {
          let m = null;
          try { m = await import('../../dtrh/shared/loomField.js'); }
          catch (e) {
            loomLog('loom import failed: ' + ((e && e.message) ? e.message : String(e)));
            try { if (typeof onFail === 'function') onFail(); } catch (_e) {}
            return;
          }
          if (cancelled || typeof document === 'undefined') return;
          try {
            const vw = Math.max(2, (typeof window !== 'undefined' && window.innerWidth) | 0 || 2);
            const vh = Math.max(2, (typeof window !== 'undefined' && window.innerHeight) | 0 || 2);
            const scaleF = Math.min(1, 900 / Math.max(vw, vh));  // render budget; CSS upscales
            const w = Math.max(2, Math.round(vw * scaleF));
            const h = Math.max(2, Math.round(vh * scaleF));
            canvas = document.createElement('canvas');    // displayed 2D canvas
            canvas.className = 'ib-loomcanvas';
            canvas.width = w; canvas.height = h;
            canvas.style.opacity = String(alpha);
            const ctx = canvas.getContext('2d');
            if (!ctx) { canvas = null; return; }
            host.appendChild(canvas);
            // separate offscreen WebGL field (null -> pure-2D fallback path)
            let field = null;
            try {
              const off = document.createElement('canvas');
              off.width = w; off.height = h;
              field = m.createFieldRenderer(off);
            } catch (_e) { field = null; }
            // widen the color space so consecutive spirals differ VISIBLY: the
            // old 3-color theme palette made every spiral read pink/purple/white.
            // Mix the theme accents with a shuffled slice of the loom swatch set
            // (+ white), deduped, and re-shuffle the order per mount.
            const themed = [
              (theme && typeof theme.accent === 'string' && theme.accent) || '#ff69b4',
              (theme && typeof theme.accent2 === 'string' && theme.accent2) || '#b06cff',
            ];
            const swatches = (Array.isArray(m.LOOM_SWATCHES) ? m.LOOM_SWATCHES.slice() : [])
              .sort(() => Math.random() - 0.5).slice(0, 4);
            // THE PLAYER'S COLOURS WIN. spiralPalette() (core/palette.js) returns
            // the 2-4 colours harvested by the Calibration colour questions, padded
            // to four threads — deliberately NARROWER than the mixed pool below,
            // because "this spiral is made of the colours you chose" is the point.
            // A short/absent harvest falls straight through to the widened themed
            // pool, which is exactly what this used to be.
            const palette = spiralPalette(themed.concat(swatches, '#ffffff')
              .filter((c, i, a) => a.indexOf(c) === i));  // dedup, keep first
            // fresh look per mount AND never the same spiral twice in a row
            // (cross-module no-repeat guard shared with effects.js via a window
            // signature — intentional duplication, no shared module).
            const q = freshSpiralParams(() => m.randomParams2(
              palette.slice().sort(() => Math.random() - 0.5)));
            const span = Math.max(200, m.loopMs2(q));
            const drawFrame = (phase) => {
              try {
                if (field) m.composeFrame(ctx, field, q, phase, w, h);
                else m.drawFallbackFrame(ctx, q, phase, w, h);
              } catch (_e) {}
            };
            if (reduced) { drawFrame(0); return; }          // static single frame
            const start = now();
            const frame = () => {
              if (cancelled) return;
              const p = ((now() - start) % span) / span;
              drawFrame(p);
              raf = requestAnimationFrame(frame);
            };
            if (typeof requestAnimationFrame === 'function') raf = requestAnimationFrame(frame);
            else drawFrame(0);
          } catch (_e) {}
        })();
      }

      /* ===== mechanic renderers (share the closures above) =============== */

      // Plain option buttons (MC4 + default). Installs steering with the els.
      function renderChoices(opts, gridCls) {
        const wrap = el('div', gridCls || 'ib-opts-grid');
        const steerOpts = [];
        opts.forEach((o) => {
          const b = mkBtn(o.label, () => attempt(o.index), 'ib-opt' + (o.isCorrect ? ' is-correct' : ''));
          wrap.appendChild(b);
          steerOpts.push({ index: o.index, el: b, isCorrect: o.isCorrect, score: o.score });
        });
        card.appendChild(wrap);
        installSteering(steerOpts);
      }

      // Yes / No — commits a boolean value (contract: bool for Y/N).
      function renderYesNo() {
        const wrap = el('div', 'ib-opts-grid ib-yesno');
        const want = !!prompt.answer;
        // display labels: use provided options if present, else Yes/No
        const yLabel = rawOpts[0] && rawOpts[0].label ? rawOpts[0].label : 'Yes';
        const nLabel = rawOpts[1] && rawOpts[1].label ? rawOpts[1].label : 'No';
        const yes = mkBtn(yLabel, () => attempt(0, true), 'ib-opt' + (want ? ' is-correct' : ''));
        const no = mkBtn(nLabel, () => attempt(1, false), 'ib-opt' + (!want ? ' is-correct' : ''));
        wrap.append(yes, no);
        card.appendChild(wrap);
        installSteering([
          { index: 0, el: yes, isCorrect: want, score: want ? 1 : 0 },
          { index: 1, el: no, isCorrect: !want, score: !want ? 1 : 0 },
        ]);
      }

      // Mono — a single "agree" path forward. Always completable (friction only).
      function renderMono() {
        const agree = rawOpts.find((o) => o.isCorrect) || rawOpts[0] || { index: 0, label: 'I agree', isCorrect: true, score: 1 };
        const wrap = el('div', 'ib-mono');
        const b = mkBtn(agree.label || 'I agree', () => attempt(agree.index), 'ib-opt is-correct ib-mono-btn');
        wrap.appendChild(b);
        card.appendChild(wrap);
        installSteering([{ index: agree.index, el: b, isCorrect: true, score: agree.score }]);
      }

      // Funnel — clicking a WRONG option dissolves it, then it respawns in place
      // AS the correct answer; the next click commits correct. Marks steered.
      function renderFunnel() {
        const correct = rawOpts.find((o) => o.isCorrect) || rawOpts[0];
        const wrap = el('div', 'ib-opts-grid');
        const steerOpts = [];
        rawOpts.forEach((o) => {
          const b = mkBtn(o.label, null, 'ib-opt' + (o.isCorrect ? ' is-correct' : ''));
          b.addEventListener('click', () => {
            if (committed) return;
            if (o.isCorrect || b._funneled) { attempt(correct.index); return; }
            // wrong: dissolve, then respawn as the correct one
            steeredFlag = true;
            b.disabled = true;
            b.classList.add('ib-dissolve');
            setTimeout(() => {
              if (committed) return;
              b.textContent = correct.label;
              b.classList.remove('ib-dissolve');
              b.classList.add('ib-respawn', 'is-correct');
              b.disabled = false;
              b._funneled = true;
            }, 620);
          });
          wrap.appendChild(b);
          steerOpts.push({ index: o.index, el: b, isCorrect: o.isCorrect, score: o.score });
        });
        card.appendChild(wrap);
        installSteering(steerOpts);
      }

      // Destruct — clicking the CORRECT option shatters/melts it, but the answer
      // STILL registers (after the short break animation). Wrong commits normally.
      function renderDestruct() {
        const wrap = el('div', 'ib-opts-grid');
        const steerOpts = [];
        rawOpts.forEach((o) => {
          const b = mkBtn(o.label, null, 'ib-opt' + (o.isCorrect ? ' is-correct' : ''));
          b.addEventListener('click', () => {
            if (committed) return;
            lastAttemptIndex = o.index;
            if (o.isCorrect) {
              b.classList.add('ib-destruct');
              sfx('destruct-shatter'); // crystalline shatter of the correct option
              b.disabled = true;
              // register AFTER the shatter so the answer is not lost
              setTimeout(() => { if (!committed) tryCommit(o.index, {}); }, 520);
            } else {
              attempt(o.index);
            }
          });
          wrap.appendChild(b);
          steerOpts.push({ index: o.index, el: b, isCorrect: o.isCorrect, score: o.score });
        });
        card.appendChild(wrap);
        installSteering(steerOpts);
      }

      // BubblePop (CENTERPIECE) — TRUE floating bubbles that BREAK THE 4TH
      // WALL. Every bubble lives on a FIXED full-viewport flight layer
      // (pointer-events:none on the layer, auto on the bubbles): it ENTERS
      // from a random viewport edge/corner, drifts a SLOW eased, staggered
      // path (~2.8-4.5s) to a slot over the question card (slot from the
      // card's live rect — read every frame, so resize just works), then
      // HOLDS STATION there for the whole live beat: a barely-moving dual-axis
      // sine bob around the slot (amp <=14px, ~4-7s per axis) is the only idle
      // motion — no travelling, and nothing but the float-away bubble ever
      // crosses a viewport bound. Popping an option SPLITS it (see below) and
      // the last shard commits via attempt() — mid-flight pops land too (the
      // beat is live the moment bubbles spawn).
      //
      // SPLIT CHAIN: an answer bubble does NOT end the beat when popped. Its
      // burst begets 2-3 smaller bubbles that scatter out of the wreck, and
      // those can beget again (up to 3 generations deep) — but a per-beat POP
      // BUDGET (SPLIT_TOTAL, rolled 4-8) decides the family's TOTAL cost up
      // front, so clearing one option is always 4-8 pops no matter how the
      // player works it. Only the LAST pop of the family advances the beat
      // (live count must hit 0), so the answer event fires exactly once. The
      // float-away riser and the storm decoys are exempt (see canSplit).
      // At climax / depth >= .75 a POP STORM of hypnotic-word decoys
      // joins the field (sparkle + tiny nudge, no commit). Every pop bursts
      // into a chain of mini theme-word bubbles; the committed answer's chain
      // is bigger and briefly clickable (pure juice, post-resolve on the
      // detached layer — commit resolves immediately).
      //
      // "LET IT FLOAT AWAY" DRIFT COMMIT: the float-away option's bubble
      // hovers ~2.5-3.5s after landing, then ALONE starts a continuous,
      // clearly visible drift up and out (50-90 px/s, tuned so the full
      // exit lands ~7-10s after the beat opened); everything else keeps
      // hovering in place. Waiting IS choosing it: when it FULLY exits the
      // viewport it counts as pressed — a deliberate, non-timedOut commit of
      // that option through the normal finalize (reward/chime) path. Popping
      // stays possible the whole drift (popping commits it too; hit targets
      // never shrink), the scaffold's timedOut auto-submit stays disarmed for
      // this mechanic, and a hard backstop (exitBudget+10s) finalizes the
      // float-away option if the drift ever wedges — waiting ALWAYS completes
      // the beat (invariant #1). Non-selected bubbles fade out IN PLACE on
      // resolve (the flight layer's is-done fade).
      //
      // Steering: SteerOption.el is the bubble BUTTON (stable, clickable). The
      // wrapper moves via transform; steer transforms on the button compose.
      // The handle is installed ONCE, after the first layout pass (steering
      // effectively begins once bubbles are on-station).
      function renderBubblePop() {
        card.classList.add('ib-bubble-card');
        const field = el('div', 'ib-bubblefield');   // slot-anchor rect over the card
        card.appendChild(field);

        const later = (fn, ms) => setTimeout(() => { try { fn(); } catch (_e) {} }, ms);

        // ---- fixed full-viewport flight layer (the 4th-wall break) --------
        // Lives on <body> (z 5: above stage/hud/aside, below the interstitial
        // overlay). Teardown fades survivors + retires the layer AFTER the
        // pop/chain afterglow (~2.4s) — the commit itself resolves immediately.
        const flight = el('div', 'ib-flightlayer');
        document.body.appendChild(flight);
        cleanups.push(() => {
          try { flight.classList.add('is-done'); } catch (_e) {}
          later(() => { try { flight.remove(); } catch (_e) {} }, 2400);
        });

        // ---- ALWAYS-ON faded Loom backdrop --------------------------------
        // Every BubblePop beat spins up a FRESH fullscreen Loom spiral at 25%
        // opacity for the beat's whole duration, sitting BEHIND the bubbles,
        // the card and all controls (z 1: under stage/card=2 and the flight
        // layer=5, above the page background), inert to clicks. Reduced motion
        // skips it; a loom import failure retires the layer silently (the seam-
        // log fires inside mountLoomSpiral) and never blocks the beat.
        if (!reduced) {
          const loomBack = el('div', 'ib-bubbleloom');
          document.body.appendChild(loomBack);
          backdropRef(true);            // fullscreen backdrop is live (see-through cards)
          fadeLayerIn(loomBack, 2000);  // mount at 0, slow fade IN to full (canvas carries the .25)
          sfx('loom-spiral-up'); // fullscreen loom mounts (one loom per beat)
          // teardown: detach from beat ownership + fade OUT (~.8s) then self-remove;
          // the backdropRef decrement still fires HERE (not delayed) so the
          // see-through-card state releases promptly.
          cleanups.push(() => { sfx('loom-spiral-down'); fadeLayerOutRemove(loomBack, 800, 1000); backdropRef(false); });
          mountLoomSpiral(loomBack, 0.25, () => { try { loomBack.remove(); } catch (_e) {} });
        }

        const opts = rawOpts.length ? rawOpts
          : [{ index: 0, label: (prompt.answer != null ? String(prompt.answer) : '◯'), isCorrect: true, score: 1 }];

        // hypnotic word pool for decoys + chain minis (theme-aware, guarded)
        const pool = ((theme && Array.isArray(theme.praise)) ? theme.praise.slice(0, 12) : [])
          .concat(HYPNO_WORDS);
        const pickWord = () => pool[Math.floor(Math.random() * pool.length)] || 'yes';

        const storm = beat.band === Band.Climax
          || (typeof beat.depth === 'number' && beat.depth >= 0.75);

        // ---- float-away option (escape-commit target) ---------------------
        // The option that semantically means "release it / let it drift off".
        // Banks carry no explicit flag, so: prefer a non-affirming label that
        // reads as release/refusal ("No", "Not really", "let it go", "Float",
        // ...); else the last non-affirming option; else the last option
        // (single-option beats: floating away simply agrees — the beat is
        // still always completable by waiting).
        const FLOAT_RX = /(float|drift)\w*\s*(away|off|on)?|let\s+(it|that|them)\s+(go|float|drift|pass)|release|^\s*(no|nope|not|never|nah|skip|pass)\b/i;
        const floatOpt = opts.find((o) => !o.isCorrect && FLOAT_RX.test(String(o.label)))
          || opts.filter((o) => !o.isCorrect).pop()
          || opts[opts.length - 1];

        // ---- float-away timing: the release bubble RISES out ON ITS OWN ---
        // No escape "window" gates it: the float-away bubble spawns below the
        // bottom edge and rises straight up and out the TOP (see the riser
        // branch in the drift loop), tuned to FULLY leave the viewport ~5.3-7.3s
        // after the beat opened on a 1080p-ish viewport (exitBudget is the
        // nominal estimate the hard backstop is rebased on). The scaffold's
        // generic timedOut auto-submit stays DISARMED for this mechanic.
        // escTimer remains only as the reduced-motion commit path + the endgame
        // respawn flag.
        if (timeoutTimer) { clearTimeout(timeoutTimer); timeoutTimer = 0; }
        // ---- SPLIT-CHAIN BUSY GATE (see the split block below) -------------
        // Clearing an answer bubble costs 4-8 pops (a few seconds), against the
        // riser's ~5.3-7.3s exit — so this gate only has to cover the overhang, not
        // carry a 15-pop marathon. While the player is ACTIVELY working a chain
        // (live descendants AND a pop inside the last 3.5s) the riser crawls and
        // both escape timers re-arm, so the beat can never be stolen out from
        // under their hands; 3.5s of idle releases it and everything resumes at
        // full speed, so waiting STILL always completes the beat (invariant #1)
        // even mid-chain, and a dawdling chain no longer makes the beat drag.
        let splitLive = 0;                 // live un-popped descendants (gen >= 1)
        let lastPopAt = 0;
        const CHAIN_IDLE_MS = 3500;
        const chainBusy = () => splitLive > 0 && (now() - lastPopAt) < CHAIN_IDLE_MS;
        const exitBudget = (8000 + Math.random() * 3000) / 1.5;   // nominal top-exit ~5.3-7.3s
        const escapeAt = (beat.timeoutMs && beat.timeoutMs > 0)
          ? beat.timeoutMs : 12000 + Math.random() * 4000;
        let escaping = false;
        let escTimer = 0;
        const armEsc = (ms) => {
          escTimer = setTimeout(() => {
            if (chainBusy()) { armEsc(2000); return; }   // mid-chain: hold the endgame
            escaping = true;
            if (reduced) {
              try { flight.classList.add('is-done'); } catch (_e) {}
              later(() => { if (!committed) finalize(floatOpt.index, undefined, false); }, 1100);
            }
          }, ms);
        };
        armEsc(escapeAt);
        cleanups.push(() => clearTimeout(escTimer));
        // hard backstop (invariant #1): rebased on exitBudget with a wide margin
        // so it can NEVER fire before even a slow rise completes; if the rise
        // ever wedges, the float-away option still lands as a deliberate
        // (non-timedOut) commit — waiting ALWAYS completes the beat. A live
        // split chain re-arms it (never fires under the player's hands), and the
        // 3.5s idle window guarantees it still lands if they walk away mid-chain.
        let escBackstop = 0;
        const armBackstop = (ms) => {
          escBackstop = setTimeout(() => {
            if (chainBusy()) { armBackstop(4000); return; }
            if (!committed) finalize(floatOpt.index, undefined, false);
          }, ms);
        };
        armBackstop(exitBudget + 12000);
        cleanups.push(() => clearTimeout(escBackstop));

        // ---- detached chain/particle layer (juice only; self-cleans) ------
        // NOT registered in cleanups[]: post-commit chain visuals must survive
        // teardown — the flight layer's delayed retirement clears stragglers.
        // Coordinates are VIEWPORT px (the layer is fixed, inset 0).
        let chainLayer = null;
        const layer = () => {
          if (!chainLayer || !chainLayer.parentNode) {
            chainLayer = el('div', 'ib-chainlayer');
            flight.appendChild(chainLayer);
          }
          return chainLayer;
        };

        function spawnDroplets(x, y, count) {
          try {
            const L = layer();
            for (let i = 0; i < count; i++) {
              const d = el('span', 'ib-droplet');
              d.style.left = x + 'px'; d.style.top = y + 'px';
              d.style.setProperty('--dx', ((Math.random() * 2 - 1) * 84).toFixed(0) + 'px');
              d.style.setProperty('--dy', (((Math.random() * 2 - 1) * 84) - 22).toFixed(0) + 'px');
              L.appendChild(d);
              later(() => d.remove(), 750);
            }
          } catch (_e) {}
        }
        function spawnSparkle(x, y) {
          try {
            const L = layer();
            const s = el('span', 'ib-sparkle', '✦');
            s.style.left = x + 'px'; s.style.top = y + 'px';
            L.appendChild(s);
            later(() => s.remove(), 750);
          } catch (_e) {}
        }
        // POP = REWARD CHAIN: 3-6 mini theme-word bubbles drift out and fade.
        // big (committed answer): 5-8, clickable ~1.2s for chain-pop sparkles.
        function spawnChain(x, y, big) {
          try {
            const L = layer();
            const n = big ? 5 + Math.floor(Math.random() * 4) : 3 + Math.floor(Math.random() * 4);
            for (let i = 0; i < n; i++) {
              const m = el('span', 'ib-mini', pickWord());
              const sz = (big ? 57 : 42) + Math.random() * 29;
              m.style.width = m.style.height = sz.toFixed(0) + 'px';
              m.style.left = x + 'px'; m.style.top = y + 'px';
              if (bubbleSprite) {
                // minis wear the real sprite too (inline bg beats the gradient)
                m.classList.add('ib-mini-sprite');
                m.style.backgroundImage = 'url("' + bubbleSprite + '")';
                m.style.backgroundSize = '100% 100%';
              }
              const ang = Math.random() * Math.PI * 2;
              const dist = (big ? 62 : 40) + Math.random() * (big ? 92 : 58);
              m.style.setProperty('--mx', (Math.cos(ang) * dist).toFixed(0) + 'px');
              m.style.setProperty('--my', ((Math.sin(ang) * dist) - 26).toFixed(0) + 'px');
              if (big) {
                m.classList.add('is-live');   // clickable window: pure juice, no game effect
                m.addEventListener('click', () => {
                  try {
                    const mr = m.getBoundingClientRect();   // viewport coords — the layer is fixed
                    spawnSparkle(mr.left + mr.width / 2, mr.top + mr.height / 2);
                  } catch (_e) {}
                  try { m.remove(); } catch (_e) {}
                });
                later(() => m.classList.remove('is-live'), 1200);
              }
              L.appendChild(m);
              later(() => m.remove(), big ? 2000 : 1700);
            }
          } catch (_e) {}
        }

        // ---- bubble field state -------------------------------------------
        const steerOpts = [];
        const bubbles = [];
        let ord = 0;

        // `kin` (optional) marks a SPLIT CHILD: {gen, root, size}. Children wear
        // the same body/label as their ancestor, are NOT registered for steering
        // (the handle installs once, with the root els) and take their diameter
        // from the split curve instead of the spawn bands below.
        function makeBubble(opt, word, delayMs, kin) {
          const isOpt = !!opt;
          const wrap = el('div', 'ib-bubwrap');
          const btn = document.createElement('button');
          btn.type = 'button';
          // NOTE: is-answer / is-correct remain in the DOM for steering/tests
          // but carry NO default styling — no color tell (see IB_CSS audit).
          btn.className = 'ib-bubble'
            + (isOpt ? ' ib-bub-opt' + (opt.isCorrect ? ' is-answer is-correct' : '') : ' ib-bub-decoy')
            + (bubbleSprite ? ' ib-bub-sprite' : '');
          if (bubbleSprite) {
            // REAL bubble body: the app's soap-bubble PNG fills the wrapper,
            // the label rides a soft scrim above it. Button = click target.
            const img = document.createElement('img');
            img.className = 'ib-bubimg';
            img.src = bubbleSprite;
            img.alt = '';
            img.draggable = false;
            const lab = el('span', 'ib-bublabel', isOpt ? opt.label : word);
            btn.append(img, lab);
          } else {
            btn.textContent = isOpt ? opt.label : word;   // CSS glass fallback
          }
          // answers LARGER than decoys so they stay findable — but the size
          // band is identical for correct vs wrong answers (no tell).
          // Diameters: answers ~195-247px, decoys ~130-169px, scaled with the
          // viewport (clamped so a full answer row still fits a ~1280px
          // window; small windows shrink further, floor 45% — shrink, never
          // overflow).
          const vwNow = (typeof window !== 'undefined' && window.innerWidth) || 1280;
          const bscale = Math.max(0.45, Math.min(1, vwNow / 1100));
          const size = kin ? kin.size
            : (isOpt ? 195 + Math.random() * 52 : 130 + Math.random() * 39) * bscale;
          wrap.style.width = wrap.style.height = size.toFixed(0) + 'px';
          if (kin) {
            // children shrink, so the label/padding must shrink with them or a
            // gen-3 bubble is all text and no bubble (29px/18px are the CSS
            // defaults tuned for a ~220px body).
            btn.classList.add('ib-bub-child');
            btn.style.fontSize = Math.max(12, Math.min(29, Math.round(size * 0.13))) + 'px';
            btn.style.padding = Math.max(5, Math.round(size * 0.08)) + 'px';
            const lab = btn.querySelector('.ib-bublabel');   // sprite path only
            if (lab) {
              lab.style.padding = Math.max(2, Math.round(size * 0.03)) + 'px '
                + Math.max(5, Math.round(size * 0.07)) + 'px';
            }
          }
          wrap.style.transform = 'translate3d(-9999px,-9999px,0)';
          wrap.appendChild(btn);
          flight.appendChild(wrap);   // fixed layer: viewport coords throughout
          const b = {
            wrap, btn, opt: opt || null, size,
            state: 'wait', waitDelay: delayMs, waitUntil: -1,
            x: 0, y: 0, px: -9999, py: -9999,
            // flight: from (fx,fy) at a random viewport edge -> slot (tx,ty),
            // slow + eased (~2.8-4.5s) so the entry reads as a lazy drift-in
            fx: 0, fy: 0, tx: 0, ty: 0, flyStart: 0,
            flyDur: 2800 + Math.random() * 1700,
            // on-station idle: dual-axis sine bob around the SLOT (amp <=14px,
            // ~4-7s per axis, per-bubble phase) — bubbles stay put once landed
            ampX: 7 + Math.random() * 7, ampY: 6 + Math.random() * 8,
            perX: 4 + Math.random() * 3, perY: 4 + Math.random() * 3,
            phase2: Math.random() * Math.PI * 2,
            sway: reduced ? 0 : (10 + Math.random() * 16),
            phase: Math.random() * Math.PI * 2,
            // exit motion (ONLY the float-away bubble ever uses these): it does
            // NOT hover — it spawns below the bottom and rises up and out the top
            evx: 0, evy: 0, driftAt: Infinity,
            riseStart: 0, riseBaseX: 0, riseSpd: 0, riseAmp: 0, risePer: 3,
            // slot spread (options only; assigned right after spawn)
            slotCol: null, slotRow: 0,
            popped: false, seeded: false, ord: ord++,
            // SPLIT CHAIN: gen 0 = the option bubble the beat mounted; root =
            // the gen-0 ancestor (null on a root); live = un-popped members of
            // that family (roots only — the family's clear counter).
            gen: kin ? kin.gen : 0, root: kin ? kin.root : null, live: kin ? 0 : 1,
          };
          if (kin) {
            // smaller body -> proportionally smaller idle bob, so a gen-3
            // bubble never wanders out from under the cursor.
            b.ampX *= 0.5; b.ampY *= 0.5;
          }
          btn.addEventListener('click', () => onPop(b));
          bubbles.push(b);
          if (isOpt && !kin) steerOpts.push({ index: opt.index, el: btn, isCorrect: opt.isCorrect, score: opt.score });
          return b;
        }

        // options: staggered spawn cadence + an even slot column each (0..1
        // across the row, alternating band) so the held stations never stack
        opts.forEach((o, i) => {
          const b = makeBubble(o, null, i * 450 + Math.random() * 250);
          b.slotCol = opts.length > 1 ? i / (opts.length - 1) : 0.5;
          b.slotRow = i % 2;
        });
        // CLIMAX POP STORM: 4-8 hypnotic-word decoys join the drift
        if (storm) {
          const nDecoy = 4 + Math.floor(Math.random() * 5);
          for (let i = 0; i < nDecoy; i++) {
            makeBubble(null, pickWord(), 600 + i * 500 + Math.random() * 300);
          }
        }

        // ---- SPLIT CHAIN (POP BUDGET) --------------------------------------
        // Popping an ANSWER bubble does not end the beat: the burst BEGETS
        // smaller bubbles that scatter out of the wreck, and those can beget
        // again — but the family carries a BUDGET, so the TOTAL number of pops
        // needed to clear it is decided up front and is ALWAYS 4-8.
        //   A full binary chain (1 -> 2 -> 4 -> 8) cost 15 pops and read as a
        // chore, so the depth is no longer what bounds the work: SPLIT_TOTAL
        // does. The family is born owing (SPLIT_TOTAL - 1) unborn bubbles;
        // every split spends 2 (or 3, to land odd totals exactly) out of that
        // purse and stops splitting when the purse is empty. Whatever order the
        // player pops in, the family costs EXACTLY SPLIT_TOTAL pops:
        //   4 = 1 + 3          5 = 1 + 2 + 2        6 = 1 + 3 + 2
        //   7 = 1 + 2 + 2 + 2  8 = 1 + 3 + 2 + 2
        // so the nested "it splits again!" feel survives (up to 3 generations
        // deep on the long plans) at a fraction of the clicks.
        //   Only the LAST pop of the family advances the beat: the live counter
        // must hit 0 before attempt() fires, so the answer event fires once.
        //   Exempt: the float-away riser (it owns the "waiting IS choosing"
        // contract and lives offscreen-bound, so popping it still commits at
        // once) and the storm decoys (they already re-form forever — splitting
        // them WOULD be unbounded).
        const SPLIT_GENS = 3;                        // hard depth cap (belt+braces)
        // Per-beat variety: 4..8 pops. Rolled once per beat so consecutive
        // beats don't feel machined, never outside [4, 8].
        const SPLIT_TOTAL = 4 + Math.floor(Math.random() * 5);
        const SPLIT_SHRINK = [0.72, 0.76, 0.82];     // size factor, per generation
        // Touch/pen floor: no generation ever goes under this, so the last
        // shards stay a comfortable target even in a small window (a gen-3
        // pixel-hunt would turn the toy into a chore). On a narrow window gens
        // 2/3 simply land on the floor together instead of shrinking into
        // confetti.
        const SPLIT_MIN_PX = 54;
        const famOf = (b) => b.root || b;
        // How many children THIS pop begets: 0 once the family's purse is spent
        // (or at the depth cap). An odd purse spends 3 first so the plan lands
        // on its exact total; after that it is always 2 at a time.
        function splitCount(fam, gen) {
          if (gen >= SPLIT_GENS) return 0;
          if (fam.budget == null) fam.budget = SPLIT_TOTAL - 1;
          if (fam.budget < 2) return 0;
          return (fam.budget % 2 === 1) ? 3 : 2;
        }
        const canSplit = (b) => !!b.opt
          && !(opts.length > 1 && b.opt.index === floatOpt.index);

        // gen >= 1 corpses leave the DOM once their burst animation is done
        // (roots stay: steering holds their els). Nothing survives the beat —
        // every wrapper lives on `flight`, which teardown fades and removes.
        function retire(b) {
          later(() => {
            try { b.wrap.remove(); } catch (_e) {}
            const i = bubbles.indexOf(b);
            if (i >= 0) bubbles.splice(i, 1);
          }, 480);
        }

        // The burst begets the children: they are born AT the parent's centre
        // and fly outward to their scatter slots on the existing 'fly' easing,
        // so the parent visibly becomes them.
        function splitBubble(b, cx, cy, n) {
          try {
            const kids = Math.max(2, n | 0);   // the caller already paid for them
            const gen = (b.gen || 0) + 1;
            const shrink = SPLIT_SHRINK[Math.min(SPLIT_SHRINK.length - 1, gen - 1)];
            const size = Math.max(SPLIT_MIN_PX, Math.min(b.size * 0.92, b.size * shrink));
            const v = vp();
            const base = Math.random() * Math.PI * 2;
            const dist = b.size * 0.42 + size * (kids > 2 ? 0.46 : 0.34);
            const fam = famOf(b);
            for (let i = 0; i < kids; i++) {
              const ang = base + (i * Math.PI * 2 / kids) + (Math.random() * 0.5 - 0.25);
              const c = makeBubble(b.opt, null, 0, { gen, root: fam, size });
              c.px = cx - size / 2; c.py = cy - size / 2;
              c.fx = c.px; c.fy = c.py;
              c.tx = Math.max(4, Math.min(v.w - size - 4, cx + Math.cos(ang) * dist - size / 2));
              c.ty = Math.max(4, Math.min(v.h - size - 4, cy + Math.sin(ang) * dist - size / 2));
              c.x = c.tx; c.y = c.ty;
              if (reduced) {
                // no flight under reduced motion: the children simply appear at
                // their slots with the same fade the reduced spawn path uses
                c.state = 'hover'; c.px = c.tx; c.py = c.ty; c.sway = 0;
                c.wrap.classList.add('ib-bub-appear');
              } else {
                c.state = 'fly';
                c.flyStart = -1;                       // stamped on the first frame
                c.flyDur = 300 + Math.random() * 180;  // a burst, not a drift-in
                c.sway = 4;
              }
              setPos(c);
              fam.live = (fam.live || 0) + 1;
              splitLive++;
            }
          } catch (_e) {}
        }

        function respawn(b, gapMs) {
          b.btn.classList.remove('ib-pop');
          // a re-formed family member is live again — put it back on the books
          // BEFORE any branch clears `popped` (every option path below does).
          if (b.opt) {
            const fam = famOf(b);
            fam.live = (fam.live || 0) + 1;
            if ((b.gen || 0) >= 1) {
              // children never re-fly from a viewport edge: they re-form in place
              splitLive++;
              b.popped = false;
              return;
            }
          }
          if (b.state === 'rise' || b.state === 'escape') {
            // the rising float-away bubble: a veto re-form happens in place
            // (still full-size, still clickable) and the rise carries on
            b.popped = false;
            return;
          }
          if (escaping) {
            // endgame veto re-form: the answer is NEVER lost — options re-form
            // in place and keep hovering; decoys stay popped.
            if (!b.opt) return;
            b.popped = false;
            return;
          }
          b.popped = false;
          b.state = 'wait';
          b.waitDelay = gapMs;
          b.waitUntil = -1;
        }

        function onPop(b) {
          if (committed || b.popped) return;
          b.popped = true;
          b.btn.classList.add('ib-pop');    // satisfying scale-burst (CSS)
          const gen = b.gen || 0;
          // tactile bubble-burst — the SAME pop voice as always, stepped up one
          // pitch notch per generation (smaller bubble, tighter pop) and eased
          // down in level so a chain never piles up. intensity is clamped inside
          // audio.pop() by the caps chain (invariant #2).
          try {
            if (audio && typeof audio.pop === 'function') {
              audio.pop(Math.max(0.3, 0.62 - gen * 0.06), 1 + gen * 0.11);
            }
          } catch (_e) {}
          const cx = b.px + b.size / 2, cy = b.py + b.size / 2;
          if (b.opt) {
            lastPopAt = now();
            const fam = famOf(b);
            fam.live = Math.max(0, (fam.live || 1) - 1);
            if (gen >= 1) splitLive = Math.max(0, splitLive - 1);
            // Reserve the children NOW (synchronously), not inside the deferred
            // spawn: a second pop landing inside those 90ms must see the purse
            // already debited, or the family's total would drift off plan.
            const kids = canSplit(b) ? splitCount(fam, gen) : 0;
            if (kids > 0) {
              fam.budget -= kids;
              // A SPLITTING pop advances NOTHING. The burst is the same droplet
              // spray (thinner each generation) and the gen-0 pop still throws
              // its mini-word chain; deeper gens get a sparkle instead, so the
              // chain doesn't blizzard the field. Children spawn a beat later so
              // the burst reads as begetting them.
              spawnDroplets(cx, cy, Math.max(5, 9 - gen * 2));
              if (gen === 0) spawnChain(cx, cy, false); else spawnSparkle(cx, cy);
              later(() => { if (!committed) splitBubble(b, cx, cy, kids); }, 90);
              if (gen >= 1) retire(b);
              return;
            }
            if (fam.live > 0) {
              // TERMINAL pop, siblings still floating: juice only. The beat
              // clears when the LAST of the family is gone, never before.
              spawnDroplets(cx, cy, 6);
              spawnSparkle(cx, cy);
              if (gen >= 1) retire(b);
              return;
            }
            // the family is empty — THIS pop is the one that answers.
            spawnDroplets(cx, cy, 9);
            attempt(b.opt.index);           // commit path (may be steer-vetoed)
            if (committed) {
              // committed answer: bigger burst, minis clickable ~1.2s.
              // resolve() already ran inside finalize — chain is pure afterglow.
              spawnChain(cx, cy, true);
            } else {
              spawnChain(cx, cy, false);
              // a steer vetoed the commit — the answer is NEVER lost: re-form
              later(() => { if (!committed) respawn(b, 250); }, 380);
            }
          } else {
            // decoy: sparkle + a tiny nudge; does NOT commit an answer
            spawnDroplets(cx, cy, 5);
            spawnSparkle(cx, cy);
            spawnChain(cx, cy, false);
            try {
              if (effects && effects.play) effects.play({ fire: true, intensity: 0.12, kind: 'bubble', decoupled: true }, beat.depth);
            } catch (_e) {}
            bgCall('onReward', { fire: true, intensity: 0.15, kind: 'bubble' });
            later(() => {
              if (!committed) { setWord(b, pickWord()); respawn(b, 500 + Math.random() * 700); }
            }, 420);
          }
        }

        // swap a bubble's word without nuking the sprite <img> (sprite path
        // keeps its children — only the scrimmed label text changes)
        function setWord(b, w) {
          try {
            if (bubbleSprite) {
              const lab = b.btn.querySelector('.ib-bublabel');
              if (lab) lab.textContent = w;
            } else {
              b.btn.textContent = w;
            }
          } catch (_e) {}
        }

        // ---- geometry helpers (VIEWPORT coords — the layer is fixed) ------
        const vp = () => ({
          w: (typeof window !== 'undefined' && window.innerWidth) || 0,
          h: (typeof window !== 'undefined' && window.innerHeight) || 0,
        });
        // random entry point just OUTSIDE a random viewport edge (corners fall
        // out naturally when `along` rolls near 0/1)
        function rollEntry(size) {
          const v = vp();
          const side = Math.floor(Math.random() * 4);   // 0 top 1 right 2 bottom 3 left
          const along = Math.random();
          switch (side) {
            case 0:  return { x: along * v.w - size / 2, y: -size * 1.4 };
            case 1:  return { x: v.w + size * 0.4, y: along * v.h - size / 2 };
            case 2:  return { x: along * v.w - size / 2, y: v.h + size * 0.4 };
            default: return { x: -size * 1.4, y: along * v.h - size / 2 };
          }
        }
        // slot over the card: since bubbles now HOLD STATION, answers spread
        // evenly across the row (slight jitter, alternating high/low bands) so
        // the big bubbles never stack over each other; decoys roll random.
        function rollSlot(b, fr) {
          const v = vp();
          const xmin = Math.max(8, fr.left - fr.width * 0.08);
          const xmax = Math.min(v.w - b.size - 8, fr.right + fr.width * 0.08 - b.size);
          const span = Math.max(1, xmax - xmin);
          if (b.slotCol != null) {
            const jit = (Math.random() * 2 - 1) * 0.06;
            b.x = xmin + span * Math.min(1, Math.max(0, b.slotCol + jit));
            b.y = fr.top + fr.height * (b.slotRow ? 0.52 + Math.random() * 0.20 : 0.24 + Math.random() * 0.20);
          } else {
            b.x = xmin + Math.random() * span;
            b.y = fr.top + fr.height * (0.30 + 0.55 * Math.random());
          }
        }
        const easeFly = (p) => 1 - Math.pow(1 - p, 3);   // ease-out cubic
        const setPos = (b) => {
          b.wrap.style.transform = 'translate3d(' + b.px.toFixed(1) + 'px,' + b.py.toFixed(1) + 'px,0)';
        };

        // ---- drift loop: edge entry -> fly to slot -> hover on station -----
        // (the float-away bubble alone rises from the bottom + out the top)
        let raf = 0, lastT = 0, installedSteer = false;
        const installOnce = () => {
          if (!installedSteer) { installedSteer = true; installSteering(steerOpts); }
        };
        const tick = (t) => {
          raf = 0;
          if (committed) return;
          let fr = null;
          try { fr = field.getBoundingClientRect(); } catch (_e) {}
          if (!fr || !fr.width || !fr.height) {
            if (typeof requestAnimationFrame === 'function') raf = requestAnimationFrame(tick);
            return;
          }
          const dt = lastT ? Math.min(64, t - lastT) : 16;
          lastT = t;
          for (const b of bubbles) {
            if (b.popped) continue;
            // ---- FLOAT-AWAY RISER: the release bubble is NOT like the others ----
            // It never takes a hover slot. It spawns just BELOW the bottom edge
            // (random x in the 15-85% band, nudged clear of the card's span when
            // there's easy room) and rises continuously UP and out the TOP with a
            // gentle horizontal sine sway — like a real soap bubble. Fully exiting
            // the top counts as pressed: a deliberate, un-steered, NON-timedOut
            // commit of the float-away option (same finalize/chime path as the old
            // drift exit). Poppable the whole way up (popping commits it too).
            // Reduced motion is EXCLUDED here — it keeps the fixed-slot path below
            // and commits via the escTimer fade (unchanged).
            if (!reduced && b.opt && b.opt.index === floatOpt.index) {
              if (b.state === 'wait') {
                if (b.waitUntil < 0) b.waitUntil = t + (b.waitDelay || 0);
                if (t < b.waitUntil) { b.px = -9999; b.py = -9999; setPos(b); continue; }
                const v = vp();
                const bscale = Math.max(0.45, Math.min(1, v.w / 1100));
                b.seeded = true; b.state = 'rise'; b.riseStart = t;
                // spawn x in the 15-85% band; if the centre lands over the card
                // and a side has easy room, slide it clear of the card's span
                let rx = (0.15 + Math.random() * 0.70) * v.w - b.size / 2;
                try {
                  const cx = rx + b.size / 2;
                  if (cx > fr.left && cx < fr.right) {
                    const leftRoom = fr.left - b.size, rightRoom = v.w - fr.right - b.size;
                    if (Math.max(leftRoom, rightRoom) > b.size * 0.5) {
                      rx = (rightRoom >= leftRoom)
                        ? fr.right + Math.random() * Math.max(0, rightRoom)
                        : Math.random() * Math.max(0, leftRoom);
                    }
                  }
                } catch (_e) {}
                b.riseBaseX = Math.max(4, Math.min(v.w - b.size - 4, rx));
                b.px = b.riseBaseX;
                b.py = v.h + b.size * 0.3;            // just below the bottom edge
                // rise 150-210 px/s (viewport-scaled), tuned so the full top-exit
                // lands ~5.3-7.3s in on a 1080p-ish viewport; sway +-30-50px scaled
                //
                // 50% FASTER than the original 8-11s tuning: at the old speed the
                // riser read as stalled rather than serene — long enough that the
                // player stopped believing it was moving and started hunting for a
                // click. Everything downstream of the exit time is derived from
                // exitBudget, so the two move together (see exitBudget above).
                const travel = v.h + b.size * 1.3;    // spawn-below -> fully above
                const targetSec = (8 + Math.random() * 3) / 1.5;
                b.riseSpd = Math.max(150, Math.min(210, travel / targetSec)) * bscale;
                b.riseAmp = (30 + Math.random() * 20) * bscale;
                b.risePer = 2.6 + Math.random() * 1.4;   // sway period (s)
                setPos(b);
                continue;
              }
              if (b.state === 'rise') {
                // SPLIT-CHAIN COURTESY: while the player is actively clearing a
                // split chain the riser slows to a crawl (and is hard-clamped
                // short of the top edge) so it can NEVER steal the commit out
                // from under a live chain. A 4-8 pop chain is short, so this is
                // a brake, not a stall: it resumes at full speed the moment the
                // chain goes idle (3.5s).
                const busy = chainBusy();
                b.py -= b.riseSpd * (dt / 1000) * (busy ? 0.2 : 1);
                if (busy) b.py = Math.max(b.py, -b.size * 0.4);
                const v = vp();
                const sway = Math.sin(((t - b.riseStart) / 1000) * (Math.PI * 2 / b.risePer) + b.phase) * b.riseAmp;
                b.px = Math.max(-b.size, Math.min(v.w, b.riseBaseX + sway));
                setPos(b);
                if (b.py + b.size < -2) {             // fully exited the TOP
                  b.wrap.style.visibility = 'hidden';
                  // fully above the top = the release IS the answer: a deliberate,
                  // un-steered, NON-timedOut commit of the float-away option.
                  finalize(floatOpt.index, undefined, false);
                  return;
                }
                continue;
              }
              // any other state (shouldn't occur: riser only wait -> rise) falls
              // through to the normal state machine below.
            }
            if (b.state === 'wait') {
              if (b.waitUntil < 0) b.waitUntil = t + (b.waitDelay || 0);
              if (reduced && t >= b.waitUntil) {
                // reduced motion: no flight — appear at the slot with a fade
                // (stagger kept), then hold station (sway is zeroed)
                b.seeded = true; b.state = 'hover';
                rollSlot(b, fr);
                b.y = fr.top + fr.height * (0.12 + 0.68 * ((b.ord % 7) / 7));
                b.px = b.x; b.py = b.y;
                try { b.wrap.classList.remove('ib-bub-appear'); void b.wrap.offsetWidth; } catch (_e) {}
                b.wrap.classList.add('ib-bub-appear');
                setPos(b);
              } else if (t >= b.waitUntil) {
                // ENTER from a random viewport edge/corner, fly to the slot
                b.seeded = true; b.state = 'fly';
                const e = rollEntry(b.size);
                b.fx = e.x; b.fy = e.y;
                rollSlot(b, fr);
                b.tx = b.x; b.ty = b.y;
                b.flyStart = t;
                b.px = b.fx; b.py = b.fy;
                setPos(b);
                continue;
              } else {
                // park far offscreen until the stagger releases it
                b.px = -9999; b.py = -9999; setPos(b);
                continue;
              }
            }
            if (b.state === 'fly') {
              // eased travel; a sine bulge bows the path so it reads organic.
              // Clickable the whole way — eager pops land mid-flight.
              // flyStart -1 = a split child born between frames: stamp it now so
              // its burst-out starts from THIS frame, not from time zero.
              if (b.flyStart < 0) b.flyStart = t;
              const p = Math.min(1, (t - b.flyStart) / b.flyDur);
              const ep = easeFly(p);
              const bow = Math.sin(p * Math.PI) * b.sway * 1.6;
              b.px = b.fx + (b.tx - b.fx) * ep + bow * Math.sin(b.phase);
              b.py = b.fy + (b.ty - b.fy) * ep + bow * Math.cos(b.phase);
              setPos(b);
              if (p >= 1) {
                b.state = 'hover'; b.x = b.tx; b.y = b.ty;
                // (the float-away bubble never flies — it rises from the bottom in
                // its own branch above — so nothing here starts an exit drift)
              }
              continue;
            }
            // 'hover': ON-STATION IDLE — the bubble STAYS AT ITS SLOT for the
            // whole live beat. Only a barely-moving dual-axis sine bob/sway
            // around the slot (amp <=14px, ~4-7s per axis, per-bubble phase)
            // keeps it reading alive. No travelling, no bouncing — ONLY the
            // float-away bubble ever leaves (escape branch above). Viewport
            // clamp is a belt-and-braces guarantee it never crosses a bound.
            const bobX = b.sway ? Math.sin((t / 1000) * (Math.PI * 2 / b.perX) + b.phase) * b.ampX : 0;
            const bobY = b.sway ? Math.sin((t / 1000) * (Math.PI * 2 / b.perY) + b.phase2) * b.ampY : 0;
            const v = vp();
            b.px = Math.max(4, Math.min(v.w - b.size - 4, b.x + bobX));
            b.py = Math.max(4, Math.min(v.h - b.size - 4, b.y + bobY));
            setPos(b);
          }
          // steering installs ONCE, after the first real layout/positioning pass
          installOnce();
          raf = requestAnimationFrame(tick);
        };
        if (typeof requestAnimationFrame === 'function') raf = requestAnimationFrame(tick);
        else installOnce();
        cleanups.push(() => { if (raf) cancelAnimationFrame(raf); });
      }

      // SLIDER QoL + AUTONOMOUS RISE (Task A). Wires an in-beat <input type=range>:
      //   1. WHEEL (every question, ALL levels): wheeling over the slider OR its
      //      row/label container nudges the value ~3% of the range per notch
      //      (deltaY sign => direction), preventDefault so the page never scrolls
      //      (passive:false), firing the SAME `input` event the slider's own handler
      //      listens to so readouts/state update identically. NOT band-gated.
      //   2. RISE (band-gated: Climax=lv4 / Recovery=lv5 ONLY — owner: "only the
      //      sliders from the lv 4 onward should tick up"): the value creeps UP on
      //      its own via a rAF tick registered with the beat's cleanups. The BAND
      //      decides BOTH the initial settle delay AND the rate (qIndex no longer
      //      factors in): Climax leans up SLOWLY (~3%/s) after a ~2.5s settle;
      //      Recovery starts sooner (~1s) and climbs a bit faster (~6%/s). Any user
      //      gesture (pointerdown / real drag `input` / wheel) pauses the creep
      //      ~1.8s, then it resumes from wherever the user left it (never fights the
      //      pointer: a live drag emits trusted `input` events that keep renewing the
      //      pause). Caps at max, NEVER auto-commits (rising only moves the control),
      //      and fires the same `input` path so readouts track it. Reduced motion is
      //      unaffected (the rise is a value, not an animation).
      function wireSlider(slider, container) {
        const lo = parseFloat(slider.min);
        const hiRaw = parseFloat(slider.max);
        const loV = Number.isFinite(lo) ? lo : 0;
        const hiV = Number.isFinite(hiRaw) && hiRaw > loV ? hiRaw : loV + 100;
        const range = hiV - loV;
        const cur = () => { const v = parseFloat(slider.value); return Number.isFinite(v) ? v : loV; };
        const fireInput = () => { try { slider.dispatchEvent(new Event('input', { bubbles: true })); } catch (_e) {} };
        const setVal = (v) => {
          const c = Math.max(loV, Math.min(hiV, v));
          if (String(c) !== slider.value) { slider.value = String(c); fireInput(); }
        };

        // pause window: user interaction defers the auto-rise for ~1.8s
        let pausedUntil = 0;
        const pause = () => { pausedUntil = now() + 1800; };

        // ---- wheel QoL (all sliders, every question) ----------------------
        const onWheel = (e) => {
          if (committed) return;
          try { e.preventDefault(); } catch (_e) {}
          const dir = ((e && e.deltaY) || 0) < 0 ? 1 : -1;   // wheel up => increase
          pause();
          setVal(cur() + dir * range * 0.03);
        };
        slider.addEventListener('wheel', onWheel, { passive: false });
        if (container && container !== slider) container.addEventListener('wheel', onWheel, { passive: false });
        cleanups.push(() => {
          try { slider.removeEventListener('wheel', onWheel, { passive: false }); } catch (_e) {}
          try { if (container && container !== slider) container.removeEventListener('wheel', onWheel, { passive: false }); } catch (_e) {}
        });

        // ---- user-interaction pause (only TRUSTED gestures; our own synthetic
        //      `input` from the rise carries isTrusted=false and must NOT pause) --
        const onUser = (e) => { if (!committed && (!e || e.isTrusted)) pause(); };
        slider.addEventListener('pointerdown', onUser);
        slider.addEventListener('input', onUser);
        slider.addEventListener('keydown', onUser);
        cleanups.push(() => {
          try { slider.removeEventListener('pointerdown', onUser); } catch (_e) {}
          try { slider.removeEventListener('input', onUser); } catch (_e) {}
          try { slider.removeEventListener('keydown', onUser); } catch (_e) {}
        });

        // ---- autonomous rise (Climax / Recovery bands ONLY) ---------------
        // Band-gated per owner: only the deep-band sliders tick up on their own.
        // The band picks BOTH the initial settle delay (the slider holds still
        // that long after mount) AND the rate — qIndex no longer factors in.
        // Climax (lv4): ~2.5s delay then a SLOW ~3%/s rise. Recovery (lv5): ~1s
        // delay then a faster ~6%/s rise. Reduced motion is unaffected (it's a
        // value, not an animation). Outside these two bands there is NO auto-rise
        // (the wheel QoL wired above still works at EVERY level).
        let riseDelayMs, ratePerSec;
        if (beat.band === Band.Climax) {
          riseDelayMs = 2500;            // ~2.5s settle before the slow rise
          ratePerSec = range * 0.03;     // slow: ~3%/s of range
        } else if (beat.band === Band.Recovery) {
          riseDelayMs = 1000;            // ~1s settle
          ratePerSec = range * 0.06;     // faster: ~6%/s of range
        } else {
          return;                        // no auto-rise below lv4
        }
        // Owner tuning: even in the deep bands NOT every slider rises on its own.
        // Roll ONCE per beat — SLIDER_AUTORISE_CHANCE (30%) arms the autonomous
        // rise as below; otherwise this slider stays FULLY manual for the beat
        // (the wheel-nudge QoL wired above is unaffected and still works here).
        if (Math.random() >= SLIDER_AUTORISE_CHANCE) return;
        // Armed: the autonomous rise is on for this beat. Signal the tilt block (which
        // runs AFTER this, at card mount) so any card lean is forced toward the
        // slider's increasing (right) side — the tilt then reads as WHY it drifts.
        sliderAutoRoseArmed = true;
        const riseStartAt = now() + riseDelayMs;   // hold still until the delay elapses
        // ROOT-CAUSE FIX: a <input type=range> quantizes writes to its step
        // (default 1), so writing 50.03 snaps straight back to 50 — reading
        // slider.value each frame therefore never accumulates the sub-integer
        // creep (the "stuck at 50%" bug). Keep the TRUE value in a float
        // accumulator and write only the rounded integer to the control (clean
        // % readout; fires `input` only when the shown step actually changes).
        let acc = cur();
        let lastRise = now(), raf = 0;
        const tickRise = () => {
          raf = 0;
          if (committed) return;
          const t = now();
          const dt = Math.min(200, t - lastRise);   // clamp long frame gaps
          lastRise = t;
          if (t < riseStartAt || t < pausedUntil) {
            acc = cur();   // pre-delay settle, or user is/just-was dragging — absorb their value
          } else if (acc < hiV) {
            acc = Math.min(hiV, acc + ratePerSec * (dt / 1000));   // cap at max; no auto-commit
            setVal(Math.round(acc));   // snap-safe: acc holds the float, DOM shows the step
          }
          if (typeof requestAnimationFrame === 'function') raf = requestAnimationFrame(tickRise);
        };
        if (typeof requestAnimationFrame === 'function') raf = requestAnimationFrame(tickRise);
        cleanups.push(() => { if (raf) { try { cancelAnimationFrame(raf); } catch (_e) {} } });
      }

      // CheckIn — a scale/slider; commits a 0..1 value.
      function renderCheckIn() {
        const wrap = el('div', 'ib-checkin');
        const scale = el('div', 'ib-scale-labels');
        scale.append(el('span', 'ib-scale-lo', 'not really'), el('span', 'ib-scale-hi', 'completely'));
        const slider = document.createElement('input');
        slider.type = 'range'; slider.min = '0'; slider.max = '100'; slider.value = '50';
        slider.className = 'ib-slider';
        const readout = el('div', 'ib-readout', '50%');
        slider.addEventListener('input', () => { readout.textContent = slider.value + '%'; });
        const go = mkBtn('That’s me', () => attempt(undefined, clamp01(+slider.value / 100)), 'ib-opt is-correct');
        wrap.append(scale, slider, readout, go);
        card.appendChild(wrap);
        wireSlider(slider, wrap);   // wheel QoL (always) + autonomous rise (Climax/Recovery only)
        installSteering([]); // free-input: no discrete options
      }

      // Interlude — non-question pacing valley. 'watch' -> the app's REAL
      // generative Loom spiral, FULLSCREEN over everything, with a big "Just
      // watch" title that fades to leave only the spiral; 'breathe' -> a
      // 4s-in/4s-out cycle whose focus is the real bubble PNG breathing over a
      // faded fullscreen gif (or a low-alpha spiral) backdrop. Runs
      // prompt.durationMs (default 5000) then auto-commits value:true. No
      // options, no steering, never scored wrong.
      function renderInterlude() {
        card.classList.add('ib-interlude-card');
        installSteering([]);
        const kind = prompt.interludeKind === 'breathe' ? 'breathe' : 'watch';
        const dur = (typeof prompt.durationMs === 'number' && prompt.durationMs > 0)
          ? prompt.durationMs : 5000;
        const wrap = el('div', 'ib-interlude');

        if (kind === 'watch') {
          // FULLSCREEN Loom spiral over everything for the interlude's duration.
          const layer = el('div', 'ib-loomlayer');
          document.body.appendChild(layer);
          backdropRef(true);            // fullscreen spiral is live (see-through cards)
          fadeLayerIn(layer, reduced ? 250 : 2200);   // mount at 0, slow fade IN to full opacity
          sfx('loom-spiral-up'); // fullscreen 'watch' loom mounts
          // teardown: fade OUT (~.8s, static last frame) then self-remove; the
          // backdropRef decrement fires HERE (not delayed) to release promptly.
          cleanups.push(() => { sfx('loom-spiral-down'); fadeLayerOutRemove(layer, reduced ? 200 : 800, 1000); backdropRef(false); });
          mountLoomSpiral(layer, 1, () => {
            // import failed: retire the fullscreen layer, keep the old card
            // spiral visual (the pre-existing .ib-spiral glyph) as fallback.
            try { layer.remove(); } catch (_e) {}
            wrap.appendChild(el('div', 'ib-spiral'));
          });
          // big BOLD "Just watch" over the spiral; fades out ~2.5-4s in.
          const watchText = el('div', 'ib-watch-text', 'Just watch');
          if (!reduced) watchText.style.animationDuration = (2500 + Math.random() * 1500).toFixed(0) + 'ms';
          layer.appendChild(watchText);
        } else {
          // BREATHE (CARDLESS): the dark rounded card box is dropped entirely —
          // only the (doubled) bubble and the phrase float directly over the
          // fullscreen backdrop. The backdrop (.ib-breathback) is unchanged.
          card.style.display = 'none';   // no card chrome for the breathe valley

          // BACKDROP: a faded fullscreen gif (object-fit cover), else a low-alpha
          // fullscreen Loom spiral, else today's plain look.
          const back = el('div', 'ib-breathback');
          document.body.appendChild(back);
          backdropRef(true);            // fullscreen breathe backdrop is live (see-through cards)
          fadeLayerIn(back, reduced ? 250 : 2000);   // mount at 0, slow fade IN (gif/spiral carries the .24)
          sfx('loom-spiral-up'); // fullscreen 'breathe' backdrop mounts
          // teardown: fade OUT (~.8s) then self-remove; backdropRef decrement
          // fires HERE (not delayed) so the see-through-card state releases promptly.
          cleanups.push(() => { sfx('loom-spiral-down'); fadeLayerOutRemove(back, reduced ? 200 : 800, 1000); backdropRef(false); });
          const gifPool = (media && Array.isArray(media.gifs))
            ? media.gifs.filter((u) => typeof u === 'string' && u.length > 0) : [];
          if (gifPool.length) {
            // noteMedia rides the pick (core/mediaLog.js) so the archive can
            // list the backdrop among the run's issued payloads.
            const g = noteMedia(gifPool[Math.floor(Math.random() * gifPool.length)], 'gif');
            const gimg = document.createElement('img');
            gimg.className = 'ib-breathgif';
            gimg.src = g; gimg.alt = ''; gimg.draggable = false;
            back.appendChild(gimg);
          } else {
            // no gifs -> low-alpha spiral; if that import fails too, drop the
            // (empty) backdrop and keep today's look.
            mountLoomSpiral(back, 0.24, () => { try { back.remove(); } catch (_e) {} });
          }

          // CARDLESS centered stage: a plain full-viewport flex column ABOVE the
          // backdrop, holding just the bubble + phrase (no chrome).
          const bstage = el('div', 'ib-breathstage');
          document.body.appendChild(bstage);
          cleanups.push(() => { try { bstage.remove(); } catch (_e) {} });

          // SLOW EXPAND: an outer wrapper grows scale 1 -> 1.3 linearly across the
          // interlude's own display duration (dur), capped at 20s so a long valley
          // still finishes its bloom. Nested OUTSIDE the bubble's ib-breathcycle so
          // the slow grow composes with the 8s inhale/exhale instead of overwriting.
          const grow = el('div', 'ib-breathgrow');
          const growMs = Math.min(dur, 20000);
          if (!reduced) grow.style.animationDuration = growMs + 'ms';

          // FOCUS: the real bubble PNG (DOUBLE size) breathing — reuses the exact
          // ib-breathcycle timing/keyframes (4s-in/4s-out, synced to the text
          // swaps). No sprite -> the plain circle fallback (also doubled).
          const circ = bubbleSprite
            ? (() => { const im = el('img', 'ib-breathbubble'); im.src = bubbleSprite; im.alt = ''; im.draggable = false; return im; })()
            : el('div', 'ib-breathcircle');
          grow.appendChild(circ);
          const btext = el('div', 'ib-breathtext', 'breathe in…');
          bstage.append(grow, btext);
          let inhale = true;
          const swap = setInterval(() => {
            inhale = !inhale;
            btext.textContent = inhale ? 'breathe in…' : 'let it go…';
          }, 4000);
          cleanups.push(() => clearInterval(swap));

          // ---- POPPABLE breathe bubble -----------------------------------
          // The stage stays click-through (pointer-events:none); ONLY the
          // bubble re-enables hits. A single pop (once, guarded against a
          // double-fire or a pop after the natural timeout via `popped` +
          // `committed`) plays the app's real POP sound, runs a ~250ms pop
          // animation (+ a few cheap fragments; skipped under reduced motion),
          // flashes ONE niche-neutral subliminal, then ENDS THE VALLEY EARLY
          // by calling finalize(undefined, true, false) — the EXACT advance
          // path the interlude's natural timeout uses (line below), so every
          // cleanup runs: backdrop fades, backdropRef balances, timers clear.
          // Not popping preserves today's full-duration behavior.
          circ.classList.add('ib-breathpop');
          let popped = false;
          const fireBreathSubliminal = () => {
            try {
              const word = BREATH_SUBLIMINALS[Math.floor(Math.random() * BREATH_SUBLIMINALS.length)] || 'let go';
              const flash = el('div', 'ib-breathsublim', word);
              document.body.appendChild(flash);
              // self-cleaning + DETACHED from the beat's cleanups so it outlives
              // the interlude teardown (a post-resolve flourish, ~900ms fade).
              const life = reduced ? 500 : 1000;
              setTimeout(() => { try { flash.remove(); } catch (_e) {} }, life);
            } catch (_e) {}
          };
          const spawnPopFragments = (node) => {
            try {
              const r = node.getBoundingClientRect();
              const cx = r.left + r.width / 2, cy = r.top + r.height / 2;
              const n = 6;
              for (let k = 0; k < n; k++) {
                const f = el('div', 'ib-breathfrag');
                const ang = (k / n) * Math.PI * 2 + Math.random() * 0.6;
                const dist = 60 + Math.random() * 90;
                f.style.left = cx + 'px'; f.style.top = cy + 'px';
                f.style.setProperty('--fx', (Math.cos(ang) * dist).toFixed(0) + 'px');
                f.style.setProperty('--fy', (Math.sin(ang) * dist).toFixed(0) + 'px');
                document.body.appendChild(f);
                setTimeout(() => { try { f.remove(); } catch (_e) {} }, 620);
              }
            } catch (_e) {}
          };
          const popBubble = () => {
            if (popped || committed) return;
            popped = true;
            try { if (audio && typeof audio.pop === 'function') audio.pop(0.6); } catch (_e) {}
            try {
              circ.classList.add('ib-breathpop-anim');
              if (!reduced) spawnPopFragments(circ);
            } catch (_e) {}
            fireBreathSubliminal();
            // end early via the interlude's own natural-timeout advance path
            const endMs = reduced ? 60 : 240;   // let the pop read, then advance
            const endT = setTimeout(() => { if (!committed) finalize(undefined, true, false); }, endMs);
            cleanups.push(() => clearTimeout(endT));
          };
          circ.addEventListener('click', popBubble);
          cleanups.push(() => { try { circ.removeEventListener('click', popBubble); } catch (_e) {} });
        }
        card.insertBefore(wrap, q);   // focus above, prompt text faint below
        const t = setTimeout(() => { if (!committed) finalize(undefined, true, false); }, dur);
        cleanups.push(() => clearTimeout(t));
      }

      // Mantra — say-it via Web Speech; degrades to type-it. Commits the string.
      function renderMantra() {
        const phrase = prompt.answer != null ? String(prompt.answer) : (prompt.text || '');
        const say = el('div', 'ib-mantra-say', '“' + phrase + '”');
        const status = el('div', 'ib-mantra-status', '');
        card.append(say, status);

        // SKIP: an always-visible bail-out that completes the beat WITHOUT
        // affirming the mantra. It reuses the scaffold's timeout / non-affirming
        // resolution — finalize(no value, timedOut) — exactly like the timer
        // lapsing, so scoring/reward semantics stay consistent: scoreDelta's
        // Mantra case is matchMantra(undefined, answer) === 0, i.e. this NEVER
        // counts as an affirmed mantra (no `said` value, status never 'good').
        // Appended LAST in every path below so it sits under the controls.
        const skip = mkBtn('Can\'t speak, I got my mouth full right now', () => {
          if (committed) return;
          finalize(undefined, undefined, true);
        }, 'ib-alt ib-mantra-skip');

        const commitSaid = (said) => {
          if (committed) return;
          status.textContent = 'good';
          attempt(undefined, said);
        };
        const reject = (target, msg) => {
          status.textContent = msg || 'not quite — again';
          if (target) { target.classList.remove('ib-shake'); void target.offsetWidth; target.classList.add('ib-shake'); }
        };

        // --- type-it degrade path ---
        const buildTypeIt = (note) => {
          if (note) status.textContent = note;
          const row = el('div', 'ib-mantra-type');
          const inp = document.createElement('input');
          inp.type = 'text'; inp.className = 'ib-mantra-input'; inp.placeholder = 'type it…';
          const go = mkBtn('Say it', () => {
            if (!phrase || matchMantra(inp.value, phrase)) commitSaid(inp.value);
            else reject(inp);
          }, 'ib-opt');
          inp.addEventListener('keydown', (e) => { if (e.key === 'Enter') go.click(); });
          row.append(inp, go);
          card.appendChild(row);
          setTimeout(() => { try { inp.focus(); } catch (_e) {} }, 40);
        };

        // --- lazily probe Web Speech ONLY here (never at import) ---
        let SR = null;
        try { SR = (typeof window !== 'undefined') && (window.SpeechRecognition || window.webkitSpeechRecognition); } catch (_e) { SR = null; }

        if (!SR) { buildTypeIt(''); card.appendChild(skip); installSteering([]); return; }

        let rec = null;
        try {
          rec = new SR();
          rec.lang = 'en-US';
          rec.interimResults = false;
          rec.maxAlternatives = 3;
        } catch (_e) { buildTypeIt('type it'); card.appendChild(skip); installSteering([]); return; }

        const mic = mkBtn('🎙 tap & say it', null, 'ib-opt ib-mic');
        card.appendChild(mic);
        // PILL RULE: mic off in the app => the affordance is shown DISABLED, not
        // hidden and not live. Hiding it would silently remove a feature the user
        // owns; leaving it live would offer a button that can only fail. Greyed
        // says "this exists, turn the mic on" — and type-it is still right there.
        if (!micAllowed) {
          mic.disabled = true;
          mic.classList.add('is-off');
          mic.textContent = '🎙 mic is off';
          mic.title = 'Microphone is disabled in settings.';
          mic.setAttribute('aria-disabled', 'true');
        }
        let typeOffered = false;
        const offerType = mkBtn('type instead', () => {
          if (typeOffered) return; typeOffered = true;
          mic.remove(); offerType.remove();
          buildTypeIt('');
        }, 'ib-alt');
        card.appendChild(offerType);

        rec.onresult = (e) => {
          let best = '';
          let ok = false;
          try {
            for (let i = 0; i < e.results.length; i++) {
              for (let j = 0; j < e.results[i].length; j++) {
                const tr = e.results[i][j].transcript;
                if (!best) best = tr;
                if (matchMantra(tr, phrase)) { ok = true; best = tr; break; }
              }
              if (ok) break;
            }
          } catch (_e) {}
          if (ok || !phrase) commitSaid(best);
          else reject(mic, 'almost — say it again');
        };
        rec.onerror = (ev) => {
          mic.classList.remove('is-live');
          const err = ev && ev.error;
          if (err === 'not-allowed' || err === 'service-not-allowed') {
            if (!typeOffered) { typeOffered = true; mic.remove(); offerType.remove(); buildTypeIt('mic unavailable — type it'); }
          } else {
            status.textContent = 'didn’t catch that';
          }
        };
        rec.onend = () => { mic.classList.remove('is-live'); };
        mic.addEventListener('click', () => {
          if (committed || !micAllowed) return;   // disabled is belt; this is braces
          try { rec.start(); mic.classList.add('is-live'); status.textContent = 'listening…'; }
          catch (_e) { /* start() throws if already running; ignore */ }
        });
        cleanups.push(() => {
          try { if (rec) { rec.onresult = rec.onerror = rec.onend = null; if (rec.abort) rec.abort(); } } catch (_e) {}
        });
        card.appendChild(skip);
        installSteering([]);
      }
    });
  }

  return { render };
}

/* ----------------------------------------------------------------------------
 * SCOPED STYLES (injected once, lazily). All classes are ib-* prefixed and use
 * the shell's CSS custom properties so this never fights styles.css.
 *
 * COLOR-TELL AUDIT: no rule here may visually distinguish is-answer/is-correct
 * from sibling options — those classes are DOM-only hooks for steering/tests.
 * -------------------------------------------------------------------------- */
const IB_CSS = `
.ib-card {
  position: relative; z-index: 2;
  width: min(760px, 92vw);
  /* Permanently translucent "frosted glass over the tube": the panel hue at 0.5
   * alpha, with a backdrop blur+darken so the living biome reads through while
   * the big/bold text keeps its contrast. Covers question, interlude AND bubble
   * cards (all share .ib-card). The ix-backdrop-live rule in styles.css thins the
   * fill further to 0.35 when a fullscreen spiral/gif is up. */
  background: rgba(37, 37, 66, 0.5);
  -webkit-backdrop-filter: blur(9px) brightness(.72); backdrop-filter: blur(9px) brightness(.72);
  border: 1px solid rgba(176,108,255,.28);
  border-radius: 18px; padding: 36px 34px;
  box-shadow: 0 20px 64px rgba(0,0,0,.5);
}
/* sizes/weights track the shell's bumped scale (.intake-q 38px/700,
 * .intake-opt 24px/700, body 18px/600) — everything reads big + bold */
.ib-q { font-size: 38px; font-weight: 700; line-height: 1.32; margin: 0 0 26px; color: var(--intake-text, #f3e9f6); }

/* ---- occasional SLOW card tilt + almost-imperceptible sway (lv2+) ---
 * Two nested wrappers hold the card so its own entry/exit transform is left
 * alone: .ib-tiltwrap carries the static angle (per-card --ib-tilt var, band-
 * scaled +-4/8/16/32deg) and .ib-swaywrap animates a tiny +-0.6deg delta around
 * it. Amplitude only — no scale, no opacity. The tilt eases in over a VERY long
 * transition (~16s) so it reads as a slooooow lean the eye barely catches. */
.ib-tiltwrap { transform: rotate(var(--ib-tilt, 0deg)); transform-origin: 50% 50%; will-change: transform; transition: transform 16s ease-in-out; }
.ib-swaywrap { animation: ib-cardsway 11s ease-in-out infinite alternate; transform-origin: 50% 50%; will-change: transform; }
@keyframes ib-cardsway { from { transform: rotate(-0.6deg); } to { transform: rotate(0.6deg); } }

/* ---- 1-in-30 melt-on-click -> SKIP: a clicked card (not a control) may melt
 * INSTANTLY and stay gone; the beat then resolves (no re-form). The card
 * element itself animates so it composes inside any tilt/sway wrapper. The
 * "forwards !important" pins the melted (opacity 0) end-state so the band EXIT
 * class finalize() adds afterward can't override the animation and briefly
 * re-show the already-melted card. Own compact keyframes (visual language
 * borrowed from the steering MeltAway, not imported). -------- */
.ib-cardmelt-anim {
  animation: ib-cardmelt .7s cubic-bezier(.55,.06,.68,.19) forwards !important;
  transform-origin: 50% 100%; pointer-events: none;
}
@keyframes ib-cardmelt {
  0%   { transform: translateY(0) scaleX(1) scaleY(1); filter: blur(0); opacity: 1; }
  28%  { transform: translateY(6px) scaleX(1.05) scaleY(.86); filter: blur(1.5px); opacity: 1; }
  100% { transform: translateY(60px) scaleX(1.12) scaleY(.1); filter: blur(6px); opacity: 0; }
}

/* ---- band-flavored card ENTRY -------------------------------------------- */
.ib-enter-crisp { animation: ib-in-crisp .3s cubic-bezier(.2,.9,.3,1) both; }
@keyframes ib-in-crisp { from { opacity: 0; transform: translateX(28px); } to { opacity: 1; transform: none; } }
.ib-enter-melt { animation: ib-in-melt .9s ease both; }
@keyframes ib-in-melt {
  from { opacity: 0; filter: blur(14px); transform: translateY(18px) scale(1.02); }
  to   { opacity: 1; filter: blur(0); transform: none; }
}
.ib-enter-sink { animation: ib-in-sink 1.1s cubic-bezier(.2,.7,.3,1) both; }
@keyframes ib-in-sink {
  from { opacity: 0; transform: translateY(-26px) rotate(-1.6deg) scale(1.03); filter: blur(6px); }
  60%  { opacity: 1; }
  to   { opacity: 1; transform: none; filter: blur(0); }
}
.ib-enter-dawn { animation: ib-in-dawn 1.2s ease both; }
@keyframes ib-in-dawn {
  from { opacity: 0; filter: saturate(.4) brightness(1.35); }
  to   { opacity: 1; filter: none; }
}

/* ---- band-flavored card EXIT (added AFTER resolve; old DOM only) --------- */
.ib-exit-crisp, .ib-exit-melt, .ib-exit-sink, .ib-exit-dawn, .ib-exit-fade { pointer-events: none; }
.ib-exit-crisp { animation: ib-out-crisp .22s ease-in forwards; }
@keyframes ib-out-crisp { to { opacity: 0; transform: translateX(-26px); } }
.ib-exit-melt { animation: ib-out-melt .5s ease forwards; }
@keyframes ib-out-melt { to { opacity: 0; filter: blur(12px); transform: scale(.97); } }
.ib-exit-sink { animation: ib-out-sink .6s ease-in forwards; }
@keyframes ib-out-sink { to { opacity: 0; transform: translateY(30px) rotate(1.8deg) scale(.96); filter: blur(8px); } }
.ib-exit-dawn { animation: ib-out-dawn .5s ease forwards; }
@keyframes ib-out-dawn { to { opacity: 0; } }
.ib-exit-fade { animation: ib-fadeout .3s ease forwards; }

/* ---- deep-band prompt: typewriter caret + breathe pulse ------------------ */
.ib-typing { cursor: pointer; }
.ib-typing::after { content: '▍'; opacity: .7; animation: ib-caret 1s steps(1) infinite; }
@keyframes ib-caret { 50% { opacity: 0; } }
.ib-breathe { animation: ib-qbreathe 6s ease-in-out infinite; transform-origin: 50% 50%; }
@keyframes ib-qbreathe {
  0%, 100% { transform: scale(1); opacity: .92; }
  50%      { transform: scale(1.015); opacity: 1; }
}

/* ---- timed pressure: shrinking ring -------------------------------------- */
.ib-timer { position: absolute; top: -14px; right: -14px; width: 56px; height: 56px; z-index: 5; }
.ib-timer svg { width: 100%; height: 100%; transform: rotate(-90deg); display: block; }
.ib-timer-track { fill: none; stroke: rgba(255,255,255,.12); stroke-width: 3; }
.ib-timer-arc { fill: none; stroke: var(--intake-accent, #ff69b4); stroke-width: 3; stroke-linecap: round; }
.ib-timer.is-urgent .ib-timer-arc { stroke: #ff4d6d; filter: drop-shadow(0 0 4px rgba(255,77,109,.8)); }

.ib-opts-grid { display: grid; grid-template-columns: 1fr 1fr; gap: 12px; transition: opacity .26s ease, transform .26s ease; }
/* OPTIONS HOLD: buttons wait out the prompt reveal — invisible AND unclickable.
   visibility (not display) keeps the grid's box, so the card never reflows when
   the options land. See the OPTIONS_HOLD_* constants. */
.ib-opts-grid.ib-opts-held { opacity: 0; visibility: hidden; pointer-events: none; transform: translateY(6px); }
.ib-yesno { grid-template-columns: 1fr 1fr; }
.ib-mono { display: flex; justify-content: center; }
.ib-mono-btn { min-width: 60%; }
.ib-checkin { display: flex; flex-direction: column; gap: 14px; }

.ib-btn, .ib-opt {
  appearance: none; cursor: pointer; font: inherit; font-size: 24px; font-weight: 700;
  color: var(--intake-text, #f3e9f6);
  background: rgba(47, 47, 82, 0.5); -webkit-backdrop-filter: blur(4px); backdrop-filter: blur(4px);
  border: 1px solid rgba(255,105,180,.22);
  border-radius: 12px; padding: 20px 22px;
  transition: transform .12s ease, background .12s ease, opacity .18s ease, filter .18s ease;
}
.ib-btn:hover, .ib-opt:hover { background: rgba(58, 58, 99, 0.62); transform: translateY(-1px); }
.ib-opt:active { transform: translateY(0); }
.ib-opt:disabled { cursor: default; }
.ib-alt {
  background: transparent; border: none; color: var(--intake-dim, #a99cc0);
  text-decoration: underline; padding: 8px 4px; font-size: 18px; font-weight: 600; border-radius: 8px;
}
.ib-alt:hover { background: transparent; color: var(--intake-accent, #ff69b4); transform: none; }

/* funnel: wrong dissolves, then respawns as the correct answer */
.ib-dissolve { animation: ib-dissolve .6s ease forwards; pointer-events: none; }
@keyframes ib-dissolve {
  0% { opacity: 1; filter: blur(0); }
  100% { opacity: 0; filter: blur(10px); transform: scale(.86); }
}
.ib-respawn { animation: ib-respawn .5s cubic-bezier(.2,1.3,.4,1) both; }
@keyframes ib-respawn {
  0% { opacity: 0; transform: scale(.7); filter: blur(6px); }
  100% { opacity: 1; transform: none; filter: none; }
}

/* destruct: correct shatters/falls (but the answer still registers) */
.ib-destruct { animation: ib-destruct .5s ease-in forwards; pointer-events: none; transform-origin: 50% 40%; }
@keyframes ib-destruct {
  0% { opacity: 1; transform: none; }
  30% { transform: translateY(-4px) rotate(-2deg); filter: brightness(1.6); }
  100% { opacity: 0; transform: translateY(90px) rotate(9deg) scale(.82); filter: blur(3px); }
}

/* checkin slider */
.ib-scale-labels { display: flex; justify-content: space-between; color: var(--intake-dim, #a99cc0); font-size: 20px; font-weight: 600; }
.ib-slider { width: 100%; accent-color: var(--intake-accent, #ff69b4); }
.ib-readout { text-align: center; font-size: 32px; font-weight: 700; color: var(--intake-accent, #ff69b4); font-variant-numeric: tabular-nums; }

/* mantra */
.ib-mantra-say { font-size: 40px; font-weight: 700; line-height: 1.28; text-align: center; margin: 6px 0 18px; color: var(--intake-accent, #ff69b4); }
.ib-mantra-status { min-height: 26px; text-align: center; color: var(--intake-dim, #a99cc0); font-size: 20px; font-weight: 600; margin-bottom: 12px; }
.ib-mantra-type { display: flex; gap: 10px; }
.ib-mantra-input {
  flex: 1; padding: 16px 18px; border-radius: 10px; font-size: 20px; font-weight: 600;
  border: 1px solid rgba(176,108,255,.3); background: rgba(31, 31, 57, 0.5); -webkit-backdrop-filter: blur(4px); backdrop-filter: blur(4px); color: var(--intake-text, #f3e9f6); font-family: inherit;
}
.ib-mic { display: block; width: 100%; margin-bottom: 6px; }
/* mantra bail-out: dim secondary, but a real block button under the controls */
.ib-mantra-skip { display: block; margin: 16px auto 0; text-decoration: none; padding: 12px 18px; }
.ib-mic.is-live { background: var(--intake-accent, #ff69b4); color: #21121b; animation: ib-pulse 1.1s ease-in-out infinite; }
/* Mic disabled in app settings — greyed, inert, still legible (WCAG-safe on the
   card's dark face). No hover/active lift: it must not read as pressable. */
.ib-mic.is-off,
.ib-mic.is-off:hover,
.ib-mic.is-off:active {
  opacity: .48;
  filter: grayscale(1);
  cursor: not-allowed;
  transform: none;
  box-shadow: none;
}
@keyframes ib-pulse { 0%,100% { box-shadow: 0 0 0 0 rgba(255,105,180,.5); } 50% { box-shadow: 0 0 0 10px rgba(255,105,180,0); } }
.ib-shake { animation: ib-shake .4s ease; }
@keyframes ib-shake { 0%,100% { transform: translateX(0); } 20%,60% { transform: translateX(-6px); } 40%,80% { transform: translateX(6px); } }

/* interlude: pacing valley — spiral stare / breathe cycle */
.ib-interlude-card { text-align: center; }
.ib-interlude-card .ib-q { opacity: .55; font-size: 24px; margin: 12px 0 0; }
.ib-interlude { display: flex; flex-direction: column; align-items: center; gap: 12px; padding: 8px 0 4px; }
.ib-spiral {
  width: 190px; height: 190px; border-radius: 50%;
  background: repeating-conic-gradient(from 0deg,
    var(--intake-accent, #ff69b4) 0deg 13deg, transparent 13deg 42deg);
  -webkit-mask: radial-gradient(circle, #000 0 61%, transparent 66%);
  mask: radial-gradient(circle, #000 0 61%, transparent 66%);
  opacity: .85;
  animation: ib-spin 14s linear infinite;
}
@keyframes ib-spin { to { transform: rotate(360deg); } }
.ib-breathcircle {
  width: clamp(320px, 40vw, 460px); height: clamp(320px, 40vw, 460px); border-radius: 50%;
  background: radial-gradient(circle at 40% 35%, rgba(255,255,255,.25), rgba(176,108,255,.35) 60%, rgba(255,105,180,.2));
  border: 1px solid rgba(255,255,255,.3);
  box-shadow: 0 0 34px rgba(176,108,255,.3);
  animation: ib-breathcycle 8s ease-in-out infinite;
}
@keyframes ib-breathcycle {
  0%, 100% { transform: scale(.72); opacity: .7; }
  50%      { transform: scale(1.15); opacity: 1; }
}
/* cardless breathe: full-viewport centered column over the backdrop (z 5:
 * above .ib-breathback=1, hud=3, aside=4; inert so it never traps clicks) */
.ib-breathstage {
  position: fixed; inset: 0; z-index: 5; pointer-events: none;
  display: flex; flex-direction: column; align-items: center; justify-content: center;
  gap: 30px; text-align: center; padding: 24px;
}
/* slow-expand wrapper: nested OUTSIDE the bubble so scale composes with breathe */
.ib-breathgrow {
  display: flex; align-items: center; justify-content: center;
  transform-origin: 50% 50%; will-change: transform;
  animation: ib-breathgrow 20s linear forwards;
}
@keyframes ib-breathgrow { from { transform: scale(1); } to { transform: scale(1.3); } }
.ib-breathtext {
  color: #fff; font-size: clamp(24px, 3vw, 34px); font-weight: 800; letter-spacing: .05em;
  min-height: 42px; text-shadow: 0 2px 12px rgba(0,0,0,.78), 0 0 26px rgba(0,0,0,.55);
}

/* interlude 'watch': the REAL Loom spiral, fullscreen OVER everything (z 5:
 * above stage/hud/aside like the flightlayer), with a fading "Just watch" title */
.ib-loomlayer { position: fixed; inset: 0; z-index: 5; overflow: hidden; background: #05030a; }
.ib-loomcanvas { position: absolute; inset: 0; width: 100%; height: 100%; display: block; }
.ib-watch-text {
  position: absolute; inset: 0; z-index: 1; pointer-events: none;
  display: flex; align-items: center; justify-content: center; text-align: center;
  font-size: clamp(40px, 7vw, 80px); font-weight: 800; letter-spacing: .02em; color: #fff;
  text-shadow: 0 0 18px rgba(255,105,180,.9), 0 0 42px rgba(176,108,255,.6);
  animation: ib-watchfade 3.2s ease-out forwards;
}
@keyframes ib-watchfade {
  0%   { opacity: 0; transform: scale(.96); }
  18%  { opacity: 1; transform: scale(1); }
  60%  { opacity: 1; transform: scale(1); }
  100% { opacity: 0; transform: scale(1); }
}

/* interlude 'breathe': faded fullscreen gif/spiral backdrop BEHIND the card
 * (z 1: under stage=2 so the card stays readable) + the real bubble PNG focus */
.ib-breathback { position: fixed; inset: 0; z-index: 1; overflow: hidden; pointer-events: none; }
.ib-breathgif { position: absolute; inset: 0; width: 100%; height: 100%; object-fit: cover; opacity: .24; }
.ib-breathback .ib-loomcanvas { opacity: .24; }
.ib-breathbubble {
  width: clamp(380px, 40vw, 480px); height: clamp(380px, 40vw, 480px);   /* DOUBLE the old size */
  object-fit: contain; user-select: none;
  filter: drop-shadow(0 0 30px rgba(176,108,255,.45));
  animation: ib-breathcycle 8s ease-in-out infinite;   /* SAME timing as the circle */
}

/* POPPABLE breathe bubble: only the bubble re-enables hits (stage stays inert);
 * a click runs a quick pop then ends the valley early. The pop rule follows the
 * breathcycle rules so it wins on same-specificity ordering. */
.ib-breathpop { pointer-events: auto; cursor: pointer; }
.ib-breathpop-anim { animation: ib-breathpopkf .25s ease-out forwards; }
@keyframes ib-breathpopkf {
  0%   { transform: scale(1);    opacity: 1; }
  40%  { transform: scale(1.28); opacity: .92; filter: brightness(1.55); }
  100% { transform: scale(.18);  opacity: 0; }
}
/* cheap pop fragments (fixed, self-cleaning; skipped under reduced motion) */
.ib-breathfrag {
  position: fixed; width: 10px; height: 10px; border-radius: 50%; z-index: 6;
  background: rgba(255,255,255,.82); pointer-events: none;
  transform: translate(-50%,-50%); animation: ib-breathfrag .55s ease-out forwards;
}
@keyframes ib-breathfrag {
  to { transform: translate(calc(-50% + var(--fx,0px)), calc(-50% + var(--fy,0px))) scale(.3); opacity: 0; }
}
/* ONE post-pop subliminal: fixed, centered, big + bold, ~900ms fade */
.ib-breathsublim {
  position: fixed; inset: 0; z-index: 7; pointer-events: none;
  display: flex; align-items: center; justify-content: center; text-align: center;
  color: #fff; font-size: clamp(48px, 8vw, 96px); font-weight: 800; letter-spacing: .03em;
  text-shadow: 0 0 20px rgba(255,105,180,.9), 0 0 46px rgba(176,108,255,.6);
  animation: ib-breathsublim 1s ease-out forwards;
}
@keyframes ib-breathsublim {
  0%   { opacity: 0; transform: scale(.92); }
  22%  { opacity: 1; transform: scale(1); }
  55%  { opacity: 1; transform: scale(1.02); }
  100% { opacity: 0; transform: scale(1.06); }
}

/* progressive slow zoom (climax + recovery cards): outermost wrapper, one
 * transform, long ease-out toward the progression-scaled cap. Reduced motion
 * never creates the wrapper (JS-gated); the media rule below is belt-and-braces. */
.ib-zoomwrap {
  transform-origin: 50% 50%;
  transform: scale(var(--ib-zoom, 1));
  transition: transform 15s ease-out;
  will-change: transform;
}

/* ---- bubble-pop: the centerpiece ----------------------------------------
 * 4th-wall floating field: wrappers live on a FIXED full-viewport flight
 * layer, enter from a random viewport edge and drift via transform (JS); the
 * BUTTON inside is the stable steering/click target. .ib-bubblefield stays in
 * the card purely as the slot-anchor rect. ALL bubbles share ONE body (glass
 * gradient, or the app's real sprite via .ib-bub-sprite) — is-answer /
 * is-correct / ib-bub-decoy are intentionally unstyled (no color tell).
 * ------------------------------------------------------------------------- */
/* ALWAYS-ON faded Loom backdrop behind every BubblePop field (z 1: under the
 * card/stage=2 and the flightlayer=5, above the page background; inert). A
 * fresh spiral is mounted per beat at 25% opacity (set inline on .ib-loomcanvas). */
.ib-bubbleloom { position: fixed; inset: 0; z-index: 1; overflow: hidden; pointer-events: none; }
.ib-bubble-card { width: min(880px, 96vw); min-height: min(74vh, 660px); overflow: hidden; }
.ib-bubble-card .ib-q { position: relative; z-index: 4; }
.ib-bubblefield { position: absolute; inset: 0; overflow: hidden; z-index: 1; }
/* the flight layer is inert; each bubble re-enables its own hits (mid-flight
 * pops land). z 5: above stage/hud/aside (2/3/4), below the overlay (6). */
.ib-flightlayer { position: fixed; inset: 0; overflow: hidden; z-index: 5; pointer-events: none; }
.ib-flightlayer .ib-bubwrap { pointer-events: auto; }
.ib-flightlayer.is-done .ib-bubwrap { opacity: 0; transition: opacity .5s ease; pointer-events: none; }
.ib-bub-appear { animation: ib-fadein .45s ease both; }
.ib-bubwrap { position: absolute; left: 0; top: 0; will-change: transform; }
.ib-bubble {
  position: relative;
  width: 100%; height: 100%; border-radius: 50%;
  display: flex; align-items: center; justify-content: center; text-align: center;
  appearance: none; cursor: pointer; font: inherit; color: var(--intake-text, #f3e9f6);
  font-size: 29px; font-weight: 700; line-height: 1.18; padding: 18px; overflow: hidden;
  background: radial-gradient(circle at 34% 30%, rgba(255,255,255,.35), rgba(176,108,255,.28) 55%, rgba(120,70,190,.22));
  border: 1px solid rgba(255,255,255,.35);
  box-shadow: 0 6px 26px rgba(120,60,180,.35), inset 0 0 22px rgba(255,255,255,.18);
  backdrop-filter: blur(2px);
  transition: filter .15s ease;
}
.ib-bubble:hover { filter: brightness(1.15); }
/* REAL sprite body: the PNG replaces the CSS glass; label rides a soft scrim */
.ib-bub-sprite { background: none; border: none; box-shadow: none; backdrop-filter: none; }
.ib-bubimg {
  position: absolute; inset: 0; width: 100%; height: 100%;
  pointer-events: none; user-select: none;
}
.ib-bublabel {
  position: relative; z-index: 1; padding: 6px 14px; border-radius: 14px;
  background: rgba(20,12,32,.28);
  text-shadow: 0 1px 4px rgba(0,0,0,.75), 0 0 10px rgba(0,0,0,.45);
}
.ib-mini-sprite { border-color: transparent; text-shadow: 0 1px 3px rgba(0,0,0,.7); }
.ib-bub-decoy { font-size: 22px; letter-spacing: .04em; }
.ib-bubble.ib-pop { animation: ib-bpop .32s ease-out forwards; pointer-events: none; }
@keyframes ib-bpop {
  0%   { transform: scale(1); opacity: 1; }
  45%  { transform: scale(1.35); opacity: .95; filter: brightness(1.5); }
  100% { transform: scale(.12); opacity: 0; }
}

/* pop juice: detached chain layer (self-cleaning; survives commit teardown) */
.ib-chainlayer { position: absolute; inset: 0; pointer-events: none; z-index: 3; }
.ib-droplet {
  position: absolute; width: 7px; height: 7px; border-radius: 50%;
  background: rgba(255,255,255,.78); pointer-events: none;
  transform: translate(-50%,-50%);
  animation: ib-droplet .55s ease-out forwards;
}
@keyframes ib-droplet {
  to { transform: translate(calc(-50% + var(--dx, 0px)), calc(-50% + var(--dy, 0px))) scale(.3); opacity: 0; }
}
.ib-mini {
  position: absolute; border-radius: 50%;
  display: flex; align-items: center; justify-content: center; text-align: center;
  color: var(--intake-text, #f3e9f6); font-size: 16px; font-weight: 700; line-height: 1.1; overflow: hidden;
  background: radial-gradient(circle at 34% 30%, rgba(255,255,255,.4), rgba(176,108,255,.3) 55%, rgba(120,70,190,.22));
  border: 1px solid rgba(255,255,255,.32);
  pointer-events: none;
  transform: translate(-50%,-50%);
  animation: ib-minidrift 1.6s ease-out forwards;
}
.ib-mini.is-live { pointer-events: auto; cursor: pointer; }
@keyframes ib-minidrift {
  0%   { transform: translate(-50%,-50%) scale(.45); opacity: .95; }
  22%  { transform: translate(calc(-50% + var(--mx, 0px) * .3), calc(-50% + var(--my, 0px) * .3)) scale(1); opacity: 1; }
  100% { transform: translate(calc(-50% + var(--mx, 0px)), calc(-50% + var(--my, 0px))) scale(.6); opacity: 0; }
}
.ib-sparkle {
  position: absolute; pointer-events: none; color: #fff; font-size: 16px;
  transform: translate(-50%,-50%);
  text-shadow: 0 0 8px var(--intake-accent, #ff69b4);
  animation: ib-sparkle .6s ease-out forwards;
}
@keyframes ib-sparkle {
  0%   { transform: translate(-50%,-50%) scale(.4) rotate(0deg); opacity: 1; }
  100% { transform: translate(-50%,-50%) scale(1.6) rotate(40deg); opacity: 0; }
}

/* generic fades (reduced-motion fallbacks + exit default) */
@keyframes ib-fadein { from { opacity: 0; } to { opacity: 1; } }
@keyframes ib-fadeout { to { opacity: 0; } }
@keyframes ib-breathfade { 0%, 100% { opacity: .55; } 50% { opacity: 1; } }

/* ---- reduced motion: every new animation degrades gracefully ------------- */
@media (prefers-reduced-motion: reduce) {
  .ib-enter-crisp, .ib-enter-melt, .ib-enter-sink, .ib-enter-dawn { animation: ib-fadein .2s ease both; }
  .ib-exit-crisp, .ib-exit-melt, .ib-exit-sink, .ib-exit-dawn, .ib-exit-fade { animation: ib-fadeout .18s ease forwards; }
  .ib-breathe { animation: none; }
  .ib-swaywrap { animation: none; }   /* static tilt survives; the sway does not */
  .ib-tiltwrap { transition: none; }  /* no animated tilt drift; the static lean stays */
  /* melt-on-click -> quick fade out, then the beat skips (no re-form).
   * !important so it overrides the base rule's own !important melt above. */
  .ib-cardmelt-anim { animation: ib-fadeout .2s linear forwards !important; }
  .ib-breathgrow { animation: none; }   /* static bubble, no slow expand */
  .ib-typing::after { animation: none; }
  .ib-spiral { animation-duration: 70s; }
  .ib-breathcircle { animation: ib-breathfade 8s ease-in-out infinite; }
  /* static (non-pulsing) breath focus; title fades with no scale motion */
  .ib-breathbubble { animation: none; }
  /* pop still works under reduced motion, minimal (placed AFTER the animation:none
   * reset above so it wins); fragments drop; subliminal hard-cuts in */
  .ib-breathpop-anim { animation: ib-breathpopkf .16s ease-out forwards; }
  .ib-breathfrag { display: none; }
  .ib-breathsublim { animation: ib-fadein .2s linear both; }
  .ib-zoomwrap { transition: none; }   /* no slow zoom (also JS-gated off) */
  .ib-watch-text { animation: ib-fadeout .8s ease 2.6s both; }
  .ib-bubble.ib-pop { animation-duration: .18s; }
  .ib-bub-appear { animation-duration: .2s; }
  .ib-mini { animation-duration: .5s; }
  .ib-droplet, .ib-sparkle { animation-duration: .3s; }
  .ib-timer { transform: none !important; }
  .ib-dissolve, .ib-respawn, .ib-destruct { animation-duration: .2s; }
  .ib-mic.is-live { animation: none; }
}

@media (max-width: 480px) {
  .ib-opts-grid { grid-template-columns: 1fr; }
  .ib-q { font-size: 30px; }
  .ib-btn, .ib-opt { font-size: 20px; padding: 17px 18px; }
  .ib-mantra-say { font-size: 32px; }
}
`;

/* ----------------------------------------------------------------------------
 * "ARE YOU SURE?" JUMPSCARE EVENT STYLES (injected lazily by ensureEventStyles,
 * only when the event first fires). Includes the FREEZE rule.
 *
 * FREEZE: pause CSS animations inside the beat stage + the effects layers
 * (real class names: .intake-stage / .ixfx-root / .ixfx-amb-root / .ixfx-gl).
 * The overlay (.ixev-root) lives on <body>, OUTSIDE all those scopes, so it is
 * never matched by the pause rule; a `running !important` guard makes doubly
 * sure the event card keeps animating.
 * -------------------------------------------------------------------------- */
const IXEV_CSS = `
body.ix-freeze .intake-stage *,
body.ix-freeze .ixfx-root *,
body.ix-freeze .ixfx-amb-root *,
body.ix-freeze .ixfx-gl * { animation-play-state: paused !important; }
.ixev-root, .ixev-root * { animation-play-state: running !important; }

.ixev-root {
  position: fixed; inset: 0; z-index: 2147483000;
  display: flex; align-items: center; justify-content: center;
  font-family: inherit;
}
/* dark veil (~0.55), fades in over 200ms; absorbs clicks on the frozen scene */
.ixev-veil {
  position: absolute; inset: 0; background: rgba(6,3,12,.55);
  opacity: 0; animation: ixev-veilin .2s ease forwards;
}
@keyframes ixev-veilin { to { opacity: 1; } }
.ixev-veil.ixev-fadeout { animation: ixev-veilout .2s ease forwards; }
@keyframes ixev-veilout { to { opacity: 0; } }

/* the card: large ("close to the POV"), off-white handwritten-feel paper,
 * scale-in 1.15 -> 1.0 over ~250ms */
.ixev-card {
  position: relative; z-index: 1; box-sizing: border-box;
  width: min(80vw, 640px); padding: 48px 42px 40px;
  background: #fbf7ee; color: #2a2431;
  border: 2px solid #e9dcc6; border-radius: 16px;
  box-shadow: 0 26px 90px rgba(0,0,0,.6), 0 5px 0 #d9c7a8;
  text-align: center;
  font-family: "Comic Sans MS", "Segoe Print", "Bradley Hand", "Chalkboard SE", cursive;
  transform: scale(1.15); opacity: 0;
  animation: ixev-cardin .25s cubic-bezier(.2,.9,.3,1) forwards;
}
@keyframes ixev-cardin { to { transform: scale(1); opacity: 1; } }
.ixev-title { font-size: clamp(38px, 6vw, 60px); font-weight: 800; line-height: 1.08; margin-bottom: 6px; }
.ixev-face  { font-size: clamp(50px, 9vw, 88px); font-weight: 800; line-height: 1; margin-bottom: 28px; }
.ixev-btns  { display: flex; gap: 22px; justify-content: center; align-items: center; }
.ixev-btn {
  appearance: none; cursor: pointer; font-family: inherit;
  font-size: 26px; font-weight: 800; color: #2a2431;
  background: #fff; border: 2px solid #cbb790; border-radius: 12px;
  padding: 14px 36px; min-height: 30px;
  box-shadow: 0 3px 0 #cbb790;
  transition: transform .1s ease, background .12s ease;
}
.ixev-btn:hover { background: #fff3d8; transform: translateY(-1px); }
.ixev-btn:active { transform: translateY(1px); box-shadow: 0 1px 0 #cbb790; }
/* the drifting Yes rides its own transform (JS-driven); lift it off the card */
.ixev-yes.ixev-drifting {
  will-change: transform; transform-origin: 50% 50%;
  box-shadow: 0 8px 26px rgba(0,0,0,.55);
}

/* jumpscare screen glitch: rapid clip-path slices + hue-rotate/invert flicker +
 * RGB-split via layered gradients & jittered transforms, ~10 frames over .5s */
.ixev-glitch {
  position: absolute; inset: 0; z-index: 5; pointer-events: none;
  mix-blend-mode: difference; opacity: .92;
  background:
    repeating-linear-gradient(0deg,  rgba(255,0,80,.55) 0, rgba(255,0,80,.55) 2px, transparent 2px, transparent 5px),
    repeating-linear-gradient(90deg, rgba(0,255,255,.45) 0, rgba(0,255,255,.45) 3px, transparent 3px, transparent 7px);
  animation: ixev-glitch .5s steps(10) 1;
}
@keyframes ixev-glitch {
  0%   { clip-path: inset(0 0 82% 0);  transform: translate(-6px,0);   filter: hue-rotate(0deg)   invert(0); }
  10%  { clip-path: inset(70% 0 8% 0); transform: translate(8px,-3px); filter: hue-rotate(90deg)  invert(1); }
  20%  { clip-path: inset(20% 0 55% 0);transform: translate(-10px,4px);filter: hue-rotate(180deg) invert(0); }
  30%  { clip-path: inset(45% 0 30% 0);transform: translate(12px,2px); filter: hue-rotate(45deg)  invert(1); }
  40%  { clip-path: inset(8% 0 78% 0); transform: translate(-14px,-5px);filter: hue-rotate(270deg) invert(0); }
  50%  { clip-path: inset(60% 0 12% 0);transform: translate(10px,6px); filter: hue-rotate(120deg) invert(1); }
  60%  { clip-path: inset(30% 0 40% 0);transform: translate(-8px,-2px);filter: hue-rotate(300deg) invert(0); }
  70%  { clip-path: inset(78% 0 4% 0); transform: translate(14px,3px); filter: hue-rotate(60deg)  invert(1); }
  80%  { clip-path: inset(15% 0 60% 0);transform: translate(-12px,5px);filter: hue-rotate(210deg) invert(0); }
  90%  { clip-path: inset(50% 0 22% 0);transform: translate(6px,-4px); filter: hue-rotate(330deg) invert(1); }
  100% { clip-path: inset(0 0 0 0);    transform: none;                filter: none; }
}
/* reduced motion: a plain 300ms black flash replaces the glitch */
.ixev-blackflash {
  position: absolute; inset: 0; z-index: 5; pointer-events: none;
  background: #000; opacity: 0; animation: ixev-blackflash .3s ease 1;
}
@keyframes ixev-blackflash { 0%,100% { opacity: 0; } 30%,70% { opacity: 1; } }

/* fullscreen gif flash, full opacity, ON TOP of the glitch */
.ixev-gifflash {
  position: absolute; inset: 0; z-index: 6; width: 100%; height: 100%;
  object-fit: cover; pointer-events: none; opacity: 1;
}

/* reduced motion: veil + card static (the mechanic still fires) */
.ixev-reduced .ixev-veil { animation: none; opacity: 1; }
.ixev-reduced .ixev-card { animation: none; transform: none; opacity: 1; }
`;

/* ----------------------------------------------------------------------------
 * "ARE YOU SURE?" FREEZE GATE styles (injected lazily by ensureFreezeStyles the
 * first time the gate fires). The red tint + subtitle live on <body> (outside
 * .intake-stage) so the ix-freeze pause rule never touches them; the correct-
 * option pulse lives INSIDE the stage, so it gets an explicit running-override
 * to keep breathing while everything else is frozen. z-order: tint/subtitle sit
 * above the card + steers but BELOW the fake-cursor dot (== steering HIJACK_Z).
 * (No stray backtick may ever appear inside this template literal.)
 * -------------------------------------------------------------------------- */
const IXFZ_CSS = `
.ixfz-tint {
  position: fixed; inset: 0; z-index: 2147479000; pointer-events: none;
  opacity: 0;
  background: radial-gradient(120% 120% at 50% 50%, rgba(190,0,20,.28), rgba(150,0,16,.72));
}
/* taunt WRAP: owns the placement + the slow swell (scale 1 -> 1.12 across the
 * whole 20s stall). Lives on <body>, so the ix-freeze pause rule never stops it.
 * The tremble transform stays on the .ixfz-sub child and composes with this. */
.ixfz-subwrap {
  position: fixed; left: 4vw; right: 4vw; bottom: 8vh; z-index: 2147479500;
  pointer-events: none; transform-origin: 50% 100%; will-change: transform;
}
.ixfz-swell { animation: ixfz-swell 20000ms cubic-bezier(.33,0,.67,1) forwards; }
@keyframes ixfz-swell {
  from { transform: scale(1); }
  to   { transform: scale(1.12); }
}
.ixfz-sub {
  pointer-events: none; text-align: center;
  font-family: Arial, "Segoe UI", system-ui, sans-serif;
  font-weight: 900; font-size: clamp(30px, 5.2vw, 60px); line-height: 1.12;
  letter-spacing: .01em; color: #ff2b3d;
  text-shadow: 0 0 10px rgba(0,0,0,.9), 0 3px 18px rgba(120,0,0,.8), 0 0 3px rgba(0,0,0,.95);
  will-change: transform; white-space: pre-wrap; word-break: break-word;
}
.ixfz-dot {
  position: fixed; left: 0; top: 0; width: 16px; height: 16px;
  margin-left: -8px; margin-top: -8px; border-radius: 50%;
  z-index: 2147480000; pointer-events: none; will-change: transform;
  background: radial-gradient(circle, rgba(255,150,210,.95), rgba(255,105,180,.55) 45%, rgba(255,105,180,0) 70%);
  box-shadow: 0 0 12px 4px rgba(255,105,180,.8), 0 0 22px 7px rgba(176,108,255,.5);
}
/* correct-option highlight: bold ring + breathing pulse (kept RUNNING while the
 * scene is frozen via the override below). */
.ixfz-correct {
  outline: 3px solid rgba(120,255,170,.95); outline-offset: 2px;
  box-shadow: 0 0 0 3px rgba(120,255,170,.35), 0 0 26px 6px rgba(90,255,150,.55);
  animation: ixfz-pulse 1.15s ease-in-out infinite;
}
@keyframes ixfz-pulse {
  0%,100% { box-shadow: 0 0 0 3px rgba(120,255,170,.30), 0 0 20px 5px rgba(90,255,150,.45); }
  50%     { box-shadow: 0 0 0 5px rgba(120,255,170,.55), 0 0 34px 10px rgba(90,255,150,.75); }
}
body.ix-freeze .ib-opt.ixfz-correct { animation-play-state: running !important; }
/* wrong options: dimmed + inert while frozen */
.ixfz-inert {
  opacity: .32 !important; filter: grayscale(.5);
  pointer-events: none !important; cursor: default !important;
}
/* reduced motion: no subtitle tremble (JS already skips it), no swell (the class
 * is already withheld), no pulse — a static ring */
.ixfz-reduced { transform: none !important; }
@media (prefers-reduced-motion: reduce) {
  .ixfz-correct { animation: none; }
  .ixfz-swell { animation: none; transform: none; }
}
`;

/* ----------------------------------------------------------------------------
 * CORRUPTED QUESTION styles (injected lazily by ensureCorruptStyles the first
 * time a beat corrupts). Everything here is scoped to the PROMPT line (.ib-q)
 * and a small absolutely-positioned overlay INSIDE the card — the options, the
 * card chrome and the rest of the UI are never touched, so a corrupted beat
 * stays fully answerable. Both flavors override the .ib-breathe idle animation
 * (an animation would otherwise beat the melt's transition outright).
 * (No stray backtick may ever appear inside this template literal.)
 * -------------------------------------------------------------------------- */
const IXCR_CSS = `
/* ---- FLAVOR A: datamosh glitch on the prompt line ------------------------ */
.ixcr-glitch {
  position: relative;
  animation: ixcr-jitter .16s steps(2, end) infinite !important;
  text-shadow: 0 0 14px rgba(255,43,208,.35), 0 0 3px rgba(32,232,255,.25);
}
.ixcr-glitch::before, .ixcr-glitch::after {
  content: attr(data-ixcr);
  position: absolute; left: 0; top: 0; width: 100%;
  pointer-events: none; opacity: .7; mix-blend-mode: screen;
  font: inherit; line-height: inherit; letter-spacing: inherit;
  white-space: pre-wrap; word-break: break-word;
}
.ixcr-glitch::before { color: #ff2bd0; animation: ixcr-tearA .27s steps(3, end) infinite; }
.ixcr-glitch::after  { color: #20e8ff; animation: ixcr-tearB .34s steps(3, end) infinite; }
@keyframes ixcr-jitter {
  0%   { transform: translate(0,0) skewX(0deg); }
  33%  { transform: translate(-2px,1px) skewX(-.6deg); }
  66%  { transform: translate(2px,-1px) skewX(.5deg); }
  100% { transform: translate(0,0) skewX(0deg); }
}
@keyframes ixcr-tearA {
  0%   { clip-path: inset(0 0 62% 0); transform: translate(-5px,-1px); }
  50%  { clip-path: inset(12% 0 46% 0); transform: translate(4px,0); }
  100% { clip-path: inset(0 0 57% 0); transform: translate(-2px,2px); }
}
@keyframes ixcr-tearB {
  0%   { clip-path: inset(46% 0 0 0); transform: translate(5px,1px); }
  50%  { clip-path: inset(58% 0 8% 0); transform: translate(-4px,0); }
  100% { clip-path: inset(52% 0 0 0); transform: translate(3px,-2px); }
}

/* broken reward GIF punching through over the line (canvas is tiny; the CSS
 * stretch + pixelated rendering is what makes the chunky broken-feed look) */
.ixcr-gifwrap {
  position: absolute; z-index: 3; pointer-events: none; overflow: hidden;
  mix-blend-mode: screen; border-radius: 6px;
  animation: ib-fadein .12s linear both;
}
.ixcr-gif {
  display: block; width: 100%; height: 100%;
  image-rendering: pixelated; image-rendering: crisp-edges;
}

/* ---- FLAVOR B: slow melt of the prompt TEXT ONLY ------------------------- */
.ixcr-melt {
  animation: none !important;                 /* kill the .ib-breathe idle pulse */
  transform-origin: 50% 0%; will-change: opacity, filter, transform;
  transition:
    opacity var(--ixcr-melt-ms, 9000ms) cubic-bezier(.35,0,.75,.4),
    filter  var(--ixcr-melt-ms, 9000ms) ease-in,
    transform var(--ixcr-melt-ms, 9000ms) cubic-bezier(.5,0,.8,.4);
}
.ixcr-melt.ixcr-melt-go {
  opacity: 0; filter: blur(20px);
  transform: translateY(28px) scaleY(1.35) scaleX(.99);
}
/* reduced motion: a plain, shorter fade (no blur, no drip, no jitter/ghosts) */
.ixcr-melt.ixcr-reduced { transition: opacity var(--ixcr-melt-ms, 1400ms) linear; }
.ixcr-melt.ixcr-reduced.ixcr-melt-go { opacity: 0; filter: none; transform: none; }
.ixcr-glitch.ixcr-reduced { animation: none !important; text-shadow: none; }
.ixcr-glitch.ixcr-reduced::before, .ixcr-glitch.ixcr-reduced::after { display: none; }
@media (prefers-reduced-motion: reduce) {
  .ixcr-glitch { animation: none !important; text-shadow: none; }
  .ixcr-glitch::before, .ixcr-glitch::after { display: none; }
  .ixcr-gifwrap { animation: ib-fadein .3s linear both; }
  .ixcr-melt { transition: opacity var(--ixcr-melt-ms, 1400ms) linear; }
  .ixcr-melt.ixcr-melt-go { opacity: 0; filter: none; transform: none; }
}
`;
