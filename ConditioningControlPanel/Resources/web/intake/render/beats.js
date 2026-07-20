/* ============================================================================
 * render/beats.js — RESPONSE MECHANICS for "Graded Intake"  (Agent C)
 *
 * createBeats({ root, effects, audio, steering, reward, caps }) -> { render(beat) }
 *   render(beat: BeatSpec) -> Promise<AnswerEvent>
 *
 * Implements all 8 Mechanic.* renderers into DOM/canvas:
 *   MC4, YesNo, BubblePop (CENTERPIECE), Mantra (Web Speech -> type-it degrade),
 *   CheckIn (slider), Mono (single agree), Funnel (wrong -> respawns correct),
 *   Destruct (correct shatters but STILL registers).
 *
 * On commit: resolve reward via reward.resolve(beat.rewardPlan, ev, scoreDelta),
 * then effects.play(rewardEvent, beat.depth) + audio.chime(rewardEvent).
 *
 * Steering (Agent D) is wired PER BEAT through the Phase-0 SteerContext:
 *   - beats.js builds { root, options[{index,el,isCorrect,score}], mechanic, band,
 *     depth, roll, caps, escapeEffort, escapeMs, onCommit, forceComplete,
 *     markProgress } and calls steering.installSteering(ctx); releases the handle
 *     on commit.
 *   - INVARIANT #1 (friction, not lockout) is enforced HERE as a backstop too:
 *     after `escapeEffort` vetoed interactions OR `escapeMs` of sustained effort,
 *     the commit lands regardless of steers, and a watchdog force-commits the
 *     user's last-attempted answer if a steer wedges the UI entirely.
 *
 * Does NOT touch index.html / styles.css / contracts.js / boot.js / steering.js /
 * effects.js / audio.js. Owns its own scoped <style> (injected lazily). Never
 * touches the DOM or Web Speech at import time — only inside the factory/render.
 * ==========================================================================*/

import {
  Mechanic, MECHANICS, makeAnswerEvent, clamp01,
} from '../core/contracts.js';

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
 * FACTORY
 * -------------------------------------------------------------------------- */
export function createBeats({ root, effects, audio, steering, reward, caps } = {}) {
  const stage = root;
  caps = caps || {};

  // Backstop budget (invariant #1). Exposed to steering via SteerContext.
  const ESCAPE_EFFORT = 6;   // vetoed interactions before the commit is allowed
  const ESCAPE_MS = 5000;    // sustained effort (ms) before the commit is allowed

  function render(beat) {
    return new Promise((resolve) => {
      ensureStyles();
      const t0 = (typeof performance !== 'undefined' ? performance.now() : Date.now());
      if (stage) stage.innerHTML = '';

      // ---- per-beat commit + steering state ------------------------------
      let committed = false;
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

      const now = () => (typeof performance !== 'undefined' ? performance.now() : Date.now());

      function teardown() {
        if (timeoutTimer) { clearTimeout(timeoutTimer); timeoutTimer = 0; }
        if (watchdog) { clearTimeout(watchdog); watchdog = 0; }
        try { if (steerHandle && steerHandle.release) steerHandle.release(); } catch (_e) {}
        for (const c of cleanups) { try { c(); } catch (_e) {} }
      }

      function finalize(chosenIndex, value, timedOut) {
        if (committed) return;
        committed = true;
        teardown();
        // Contract: exactly one of value/chosenIndex. Free-value mechanics
        // (Y/N bool, mantra string, checkin number) carry `value`; option-backed
        // mechanics carry `chosenIndex`. `index` still rides the commit for
        // steering/backstop bookkeeping, but is dropped from the event when a
        // value is present.
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
        let rewardEvent;
        try {
          rewardEvent = (reward && reward.resolve)
            ? reward.resolve(beat.rewardPlan, ev, delta)
            : fallbackReward(beat, delta);
        } catch (_e) { rewardEvent = fallbackReward(beat, delta); }
        if (!rewardEvent) rewardEvent = fallbackReward(beat, delta);
        try { if (effects && effects.play) effects.play(rewardEvent, beat.depth); } catch (_e) {}
        try { if (audio && audio.chime) audio.chime(rewardEvent); } catch (_e) {}
        resolve(ev);
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
        tryCommit(index, { value });
      }

      // ---- SteerContext construction (Phase-0 contract) ------------------
      function installSteering(steerOptions) {
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
      const card = el('div', 'ib-card ib-mech-' + beat.mechanic);
      const q = el('p', 'ib-q');
      q.textContent = beat.prompt.text + (beat.prompt.flavor ? ` (${beat.prompt.flavor})` : '');
      card.appendChild(q);

      // ---- timeout (auto-submit timedOut) --------------------------------
      if (beat.timeoutMs && beat.timeoutMs > 0) {
        timeoutTimer = setTimeout(() => {
          timeoutTimer = 0;
          finalize(lastAttemptIndex, lastAttemptValue, true);
        }, beat.timeoutMs);
      }

      // Normalize option specs -> {index,label,isCorrect,score}
      const rawOpts = (Array.isArray(beat.options) ? beat.options : []).map((o, i) => ({
        index: typeof o.index === 'number' ? o.index : i,
        label: o.label != null ? o.label : String(o.index != null ? o.index : i),
        isCorrect: !!o.isCorrect,
        score: typeof o.score === 'number' ? o.score : (o.isCorrect ? 1 : 0),
      }));

      const mech = selectMechanic(beat);
      switch (mech) {
        case Mechanic.MC4:      renderChoices(rawOpts, 'ib-opts-grid'); break;
        case Mechanic.YesNo:    renderYesNo(); break;
        case Mechanic.Mono:     renderMono(); break;
        case Mechanic.Funnel:   renderFunnel(); break;
        case Mechanic.Destruct: renderDestruct(); break;
        case Mechanic.BubblePop:renderBubblePop(); break;
        case Mechanic.CheckIn:  renderCheckIn(); break;
        case Mechanic.Mantra:   renderMantra(); break;
        default:                renderChoices(rawOpts.length ? rawOpts : [{ index: 0, label: 'Continue', isCorrect: true, score: 1 }], 'ib-opts-grid');
      }

      if (stage) stage.appendChild(card);

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
        const want = !!beat.prompt.answer;
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

      // BubblePop (CENTERPIECE) — each option is a floating bubble; popping one
      // answers. Popping the answer bubble drives the reward visual (effects.play).
      function renderBubblePop() {
        card.classList.add('ib-bubble-card');
        const field = el('div', 'ib-bubblefield');
        const opts = rawOpts.length ? rawOpts
          : [{ index: 0, label: (beat.prompt.answer != null ? String(beat.prompt.answer) : '◯'), isCorrect: true, score: 1 }];
        const n = opts.length;
        const steerOpts = [];
        const bubbles = [];
        opts.forEach((o, i) => {
          const b = document.createElement('button');
          b.type = 'button';
          b.className = 'ib-bubble' + (o.isCorrect ? ' is-answer' : '');
          b.textContent = o.label;
          const bx = 12 + (i + 0.5) * (76 / n);
          const by = 26 + Math.random() * 46;
          b.style.left = bx + '%';
          b.style.top = by + '%';
          const drift = (Math.random() * 2 - 1);
          const phase = Math.random() * Math.PI * 2;
          b.addEventListener('click', () => {
            if (committed) return;
            b.classList.add('ib-pop');
            attempt(o.index);
          });
          field.appendChild(b);
          steerOpts.push({ index: o.index, el: b, isCorrect: o.isCorrect, score: o.score });
          bubbles.push({ el: b, drift, phase });
        });
        card.appendChild(field);

        // gentle idle float (self-terminates on teardown)
        const start = now();
        let raf = 0;
        const tick = () => {
          const dt = (now() - start) / 1000;
          for (const bb of bubbles) {
            const dy = Math.sin(dt + bb.phase) * 9;
            const dx = Math.cos(dt * 0.7 + bb.phase) * 7 * bb.drift;
            bb.el.style.setProperty('--fx', dx.toFixed(1) + 'px');
            bb.el.style.setProperty('--fy', dy.toFixed(1) + 'px');
          }
          raf = requestAnimationFrame(tick);
        };
        if (typeof requestAnimationFrame === 'function') { raf = requestAnimationFrame(tick); }
        cleanups.push(() => { if (raf) cancelAnimationFrame(raf); });
        installSteering(steerOpts);
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
        installSteering([]); // free-input: no discrete options
      }

      // Mantra — say-it via Web Speech; degrades to type-it. Commits the string.
      function renderMantra() {
        const phrase = beat.prompt.answer != null ? String(beat.prompt.answer) : (beat.prompt.text || '');
        const say = el('div', 'ib-mantra-say', '“' + phrase + '”');
        const status = el('div', 'ib-mantra-status', '');
        card.append(say, status);

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

        if (!SR) { buildTypeIt(''); installSteering([]); return; }

        let rec = null;
        try {
          rec = new SR();
          rec.lang = 'en-US';
          rec.interimResults = false;
          rec.maxAlternatives = 3;
        } catch (_e) { buildTypeIt('type it'); installSteering([]); return; }

        const mic = mkBtn('🎙 tap & say it', null, 'ib-opt ib-mic');
        card.appendChild(mic);
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
          if (committed) return;
          try { rec.start(); mic.classList.add('is-live'); status.textContent = 'listening…'; }
          catch (_e) { /* start() throws if already running; ignore */ }
        });
        cleanups.push(() => {
          try { if (rec) { rec.onresult = rec.onerror = rec.onend = null; if (rec.abort) rec.abort(); } } catch (_e) {}
        });
        installSteering([]);
      }
    });
  }

  return { render };
}

/* ----------------------------------------------------------------------------
 * SCOPED STYLES (injected once, lazily). All classes are ib-* prefixed and use
 * the shell's CSS custom properties so this never fights styles.css.
 * -------------------------------------------------------------------------- */
const IB_CSS = `
.ib-card {
  position: relative; z-index: 2;
  width: min(600px, 92vw);
  background: var(--intake-panel, #252542);
  border: 1px solid rgba(176,108,255,.28);
  border-radius: 18px; padding: 30px 28px;
  box-shadow: 0 20px 64px rgba(0,0,0,.5);
  animation: ib-cardin .35s ease both;
}
@keyframes ib-cardin { from { opacity: 0; transform: translateY(12px) scale(.985); } to { opacity: 1; transform: none; } }
.ib-q { font-size: 21px; line-height: 1.42; margin: 0 0 22px; color: var(--intake-text, #f3e9f6); }

.ib-opts-grid { display: grid; grid-template-columns: 1fr 1fr; gap: 12px; }
.ib-yesno { grid-template-columns: 1fr 1fr; }
.ib-mono { display: flex; justify-content: center; }
.ib-mono-btn { min-width: 60%; }
.ib-checkin { display: flex; flex-direction: column; gap: 14px; }

.ib-btn, .ib-opt {
  appearance: none; cursor: pointer; font: inherit;
  color: var(--intake-text, #f3e9f6);
  background: #2f2f52; border: 1px solid rgba(255,105,180,.22);
  border-radius: 12px; padding: 15px 16px;
  transition: transform .12s ease, background .12s ease, opacity .18s ease, filter .18s ease;
}
.ib-btn:hover, .ib-opt:hover { background: #3a3a63; transform: translateY(-1px); }
.ib-opt:active { transform: translateY(0); }
.ib-opt:disabled { cursor: default; }
.ib-alt {
  background: transparent; border: none; color: var(--intake-dim, #a99cc0);
  text-decoration: underline; padding: 8px 4px; font-size: 13px; border-radius: 8px;
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
.ib-scale-labels { display: flex; justify-content: space-between; color: var(--intake-dim, #a99cc0); font-size: 13px; }
.ib-slider { width: 100%; accent-color: var(--intake-accent, #ff69b4); }
.ib-readout { text-align: center; font-size: 20px; color: var(--intake-accent, #ff69b4); font-variant-numeric: tabular-nums; }

/* mantra */
.ib-mantra-say { font-size: 26px; line-height: 1.35; text-align: center; margin: 6px 0 14px; color: var(--intake-accent, #ff69b4); }
.ib-mantra-status { min-height: 18px; text-align: center; color: var(--intake-dim, #a99cc0); font-size: 14px; margin-bottom: 12px; }
.ib-mantra-type { display: flex; gap: 10px; }
.ib-mantra-input {
  flex: 1; padding: 13px 14px; border-radius: 10px;
  border: 1px solid rgba(176,108,255,.3); background: #1f1f39; color: var(--intake-text, #f3e9f6); font: inherit;
}
.ib-mic { display: block; width: 100%; margin-bottom: 6px; }
.ib-mic.is-live { background: var(--intake-accent, #ff69b4); color: #21121b; animation: ib-pulse 1.1s ease-in-out infinite; }
@keyframes ib-pulse { 0%,100% { box-shadow: 0 0 0 0 rgba(255,105,180,.5); } 50% { box-shadow: 0 0 0 10px rgba(255,105,180,0); } }
.ib-shake { animation: ib-shake .4s ease; }
@keyframes ib-shake { 0%,100% { transform: translateX(0); } 20%,60% { transform: translateX(-6px); } 40%,80% { transform: translateX(6px); } }

/* bubble-pop: the centerpiece. bubbles float in a full-card field. */
.ib-bubble-card { width: min(760px, 96vw); min-height: min(70vh, 560px); overflow: hidden; }
.ib-bubblefield { position: absolute; inset: 0; }
.ib-bubble {
  position: absolute; transform: translate(-50%, -50%) translate(var(--fx,0), var(--fy,0));
  appearance: none; cursor: pointer; font: inherit; color: var(--intake-text, #f3e9f6);
  min-width: 96px; min-height: 96px; padding: 14px 18px; border-radius: 50%;
  background: radial-gradient(circle at 34% 30%, rgba(255,255,255,.35), rgba(176,108,255,.28) 55%, rgba(120,70,190,.22));
  border: 1px solid rgba(255,255,255,.35);
  box-shadow: 0 6px 26px rgba(120,60,180,.35), inset 0 0 22px rgba(255,255,255,.18);
  backdrop-filter: blur(2px);
  transition: filter .15s ease;
}
.ib-bubble:hover { filter: brightness(1.15); }
.ib-bubble.is-answer {
  background: radial-gradient(circle at 34% 30%, rgba(255,255,255,.5), rgba(255,105,180,.34) 55%, rgba(210,70,150,.26));
  border-color: rgba(255,150,210,.55);
}
.ib-bubble.ib-pop { animation: ib-bpop .34s ease-out forwards; pointer-events: none; }
@keyframes ib-bpop {
  0% { transform: translate(-50%,-50%) translate(var(--fx,0),var(--fy,0)) scale(1); opacity: 1; }
  40% { transform: translate(-50%,-50%) translate(var(--fx,0),var(--fy,0)) scale(1.28); opacity: .9; }
  100% { transform: translate(-50%,-50%) translate(var(--fx,0),var(--fy,0)) scale(.2); opacity: 0; }
}

@media (max-width: 480px) {
  .ib-opts-grid { grid-template-columns: 1fr; }
  .ib-q { font-size: 19px; }
}
`;
