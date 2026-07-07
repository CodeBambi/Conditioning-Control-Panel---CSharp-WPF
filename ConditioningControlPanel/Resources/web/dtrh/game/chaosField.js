/* ============================================================================
 * chaosField.js - the DtRH gameplay bubble field: DOM elements moved by a rAF
 * integrator (translate3d), replacing both the WPF per-bubble windows and the
 * Fall's CSS-animation field. This is the ONE place engine motion code was
 * rewritten for the port - everything here is driven by chaosRun.js through
 * the spawn() API + callbacks, mirroring the C# BubbleService chaos surface:
 *
 *   - treats pop on pointerdown; lives take a 1000ms HOLD channel (the fuse
 *     pauses while held, early release / a stray cursor / a tap detonates)
 *   - fuse rings: yellow -> yellow/red flash (<2.4s) -> solid red (<0.8s brink)
 *   - freeze pickup: catching it hands timeScale 0 to the whole field; channels
 *     run for free while frozen (chaosRun owns that rule via canChannel)
 *   - the Ripple: an expanding wavefront that pops treats paid and snaps lives
 *     clean, hits staggered as the ring passes each bubble
 *   - global timeScale: motion, fuses, treat rot and sway all scale together
 *     (slow-mo 0.12 arrives with M4's darters; freeze = 0 today)
 *
 * Pop/chime SFX stay in-page (audioBus - zero latency); the chaos stingers
 * (trigger/freeze/golden/streak) go native over the bridge from chaosRun.
 * ==========================================================================*/

import { MOTION, RING_FLASH_FROM_MS, RING_BRINK_MS, popWordFor } from './variants.js';
import { makeSfxPlayer } from '../engine/audioBus.js';
import { getLevel } from '../engine/audioLevels.js';

const SFX_BASE = '/dtrh/assets/bubbles/sfx/';
const POP_SFX = ['Pop.mp3', 'Pop2.mp3', 'Pop3.mp3'];
const CHIME_SFX = ['chime1.mp3', 'chime2.mp3', 'chime3.mp3'];

// ---- verb tuning (ChaosTuning.cs) ----
const DEFUSE_HOLD_MS = 1000;     // hold this long over a live one to snap it
const CLICK_THRESHOLD_MS = 180;  // a faster press+release reads as a CLICK
const CHANNEL_MIN_SCALE = 0.55;  // visual shrink floor while channeling

// ---- motion tuning (self-calibrated for the browser field; the C# speeds
// lived inside BubbleService's fixed-step timer and don't translate 1:1) ----
const CROSS_SEC_MIN = 8, CROSS_SEC_MAX = 13;   // vertical travellers: screen-cross time
const SIDE_SEC_MIN = 9, SIDE_SEC_MAX = 14;     // side drifters: width-cross time
const ROAM_SPEED_MIN = 90, ROAM_SPEED_MAX = 150; // px/s bouncing drift
const SWAY_AMP_MIN = 18, SWAY_AMP_MAX = 42;    // px horizontal wobble

const rand = (a, b) => a + Math.random() * (b - a);
const pickOf = (arr) => arr[(Math.random() * arr.length) | 0];

export function createChaosField({ hud, canChannel, onBenignPopped, onFreezeCaught,
  onDefused, onDetonated, onTreatExpired, onChannelBroken }) {
  const layer = document.createElement('div');
  layer.className = 'cf-layer';
  hud.insertBefore(layer, hud.firstChild); // under HUD chrome, above the canvas

  const fx = document.createElement('div');
  fx.className = 'cf-fx';
  hud.appendChild(fx);

  const audio = makeSfxPlayer();
  audio.preload([...POP_SFX, ...CHIME_SFX].map((f) => SFX_BASE + f));
  const playPop = (vol = 0.25) => audio.play(SFX_BASE + pickOf(POP_SFX), vol * getLevel('fx'));
  const playChime = (vol = 0.3) => audio.play(SFX_BASE + pickOf(CHIME_SFX), vol * getLevel('fx'));

  const live = new Set();
  let timeScale = 1;   // slow-mo dial; 0 while frozen
  let frozen = false;
  let held = false;    // pause/draft/video-cover: clock + motion + input all stop
  let inputLocked = false;
  const ripples = [];  // in-flight ripple wavefronts

  const W = () => window.innerWidth, H = () => window.innerHeight;

  // ---- float text -----------------------------------------------------------
  function floatText(text, x, y, cls) {
    const el = document.createElement('div');
    el.className = `cf-pop ${cls || ''}`;
    el.textContent = text;
    el.style.left = `${x}px`;
    el.style.top = `${y}px`;
    fx.appendChild(el);
    el.addEventListener('animationend', () => el.remove(), { once: true });
  }

  // ---- spawn ------------------------------------------------------------------
  function spawn(spec) {
    const size = spec.sizePx;
    const wrap = document.createElement('div');
    wrap.className = `cf-bubble cf-bubble--${spec.kind}`;
    wrap.style.width = wrap.style.height = `${size}px`;

    const body = document.createElement('div');
    body.className = 'cf-bubble-body';
    if (spec.sprite) body.style.backgroundImage = `url('${spec.sprite}')`;
    else body.style.background = `radial-gradient(circle at 32% 30%, rgba(255,255,255,0.75), ${spec.tint} 58%, rgba(0,0,0,0.35))`;
    body.style.setProperty('--tint', spec.tint);
    if (spec.label) {
      const lab = document.createElement('span');
      lab.className = 'cf-bubble-label';
      lab.textContent = spec.label;
      body.appendChild(lab);
    }
    wrap.appendChild(body);

    let ring = null;
    if (spec.kind === 'live') {
      ring = document.createElement('div');
      ring.className = 'cf-fuse-ring';
      wrap.appendChild(ring);
    }
    const chan = document.createElement('div');
    chan.className = 'cf-chan-ring';
    chan.style.display = 'none';
    wrap.appendChild(chan);

    const b = {
      spec, wrap, body, ring, chan,
      size,
      x: 0, y: 0,
      state: 'live',
      fuseLeft: spec.fuseMs,
      fuseTotal: Math.max(1, spec.fuseMs),
      ttlLeft: spec.treatLifeMs || 0,
      swayT: rand(0, Math.PI * 2),
      swayAmp: rand(SWAY_AMP_MIN, SWAY_AMP_MAX),
      swaySpeed: rand(1.2, 2.2),
      channel: null,          // { elapsed } while a hold is running (real-time ms)
      ringPhase: '',
    };

    // motion setup
    const m = spec.motion;
    if (m === MOTION.FloatUp || m === MOTION.RainDown) {
      b.baseX = rand(0.03, 0.88) * W();
      b.x = b.baseX;
      b.vy = (H() + size * 2) / rand(CROSS_SEC_MIN, CROSS_SEC_MAX) * spec.speedMult * (m === MOTION.FloatUp ? -1 : 1);
      b.y = m === MOTION.FloatUp ? H() + size * 0.2 : -size * 1.2;
    } else if (m === MOTION.SideDrift) {
      const fromLeft = Math.random() < 0.5;
      b.vx = (W() + size * 2) / rand(SIDE_SEC_MIN, SIDE_SEC_MAX) * spec.speedMult * (fromLeft ? 1 : -1);
      b.x = fromLeft ? -size : W();
      b.baseY = rand(0.12, 0.72) * H();
      b.y = b.baseY;
    } else { // RoamBounce
      b.x = rand(0.1, 0.8) * W();
      b.y = rand(0.1, 0.7) * H();
      const a = rand(0, Math.PI * 2);
      const sp = rand(ROAM_SPEED_MIN, ROAM_SPEED_MAX) * spec.speedMult;
      b.vx = Math.cos(a) * sp;
      b.vy = Math.sin(a) * sp;
    }
    place(b);

    // ---- input ---------------------------------------------------------------
    wrap.addEventListener('pointerdown', (e) => {
      if (e.button != null && e.button !== 0) return;
      e.preventDefault();
      e.stopPropagation();
      if (b.state !== 'live' || held || inputLocked) return;
      if (spec.kind === 'live') {
        if (canChannel(spec)) beginChannel(b);
        else { try { onChannelBroken(spec, 'nofocus'); } catch (err) { /* ignore */ } detonate(b); }
      } else {
        popBenign(b, e.clientX, e.clientY);
      }
    });
    wrap.addEventListener('pointerup', () => endChannel(b, false));
    wrap.addEventListener('pointercancel', () => endChannel(b, false));
    wrap.addEventListener('pointerleave', () => endChannel(b, false));

    live.add(b);
    layer.appendChild(wrap);
    return b;
  }

  function place(b) {
    b.wrap.style.transform = `translate3d(${b.x - b.size / 2}px, ${b.y - b.size / 2}px, 0)`;
  }

  // ---- verbs ---------------------------------------------------------------------
  // No pointer capture: the cursor straying off the bubble must fire pointerleave
  // (capture would swallow it) - that stray IS the "you let go" break, like C#.
  function beginChannel(b) {
    b.channel = { elapsed: 0 };
    b.chan.style.display = '';
    b.wrap.classList.add('is-channeling');
  }

  // A channel ending BEFORE the hold completes: complete=false -> the bubble
  // triggers in your grip (click vs early-release feedback picked by duration).
  // A hold released while the field is HELD (pause / video cover) just lets go
  // quietly - a paused field can't detonate in your hand.
  function endChannel(b, complete) {
    if (!b.channel || b.state !== 'live') return;
    const ms = b.channel.elapsed;
    b.channel = null;
    b.chan.style.display = 'none';
    b.wrap.classList.remove('is-channeling');
    b.body.style.transform = '';
    if (complete || held) return; // defuse() already ran / paused field lets go clean
    try { onChannelBroken(b.spec, ms < CLICK_THRESHOLD_MS ? 'click' : 'release'); } catch (err) { /* ignore */ }
    detonate(b);
  }

  function popVisual(b, sfxVol = 0.25) {
    b.state = 'popped';
    live.delete(b);
    b.wrap.classList.add('is-pop');
    b.wrap.style.pointerEvents = 'none';
    playPop(sfxVol);
    b.wrap.addEventListener('animationend', () => b.wrap.remove(), { once: true });
    window.setTimeout(() => b.wrap.remove(), 600); // belt-and-suspenders
  }

  function popBenign(b, x, y) {
    const word = popWordFor(b.spec.variantId);
    popVisual(b);
    if (b.spec.kind === 'freeze') {
      floatText(word, x, y, 'cf-pop--freeze');
      try { onFreezeCaught(b.spec); } catch (err) { /* ignore */ }
      return;
    }
    if (word) floatText(word, x, y, b.spec.kind === 'golden' ? 'cf-pop--gold' : 'cf-pop--word');
    try { onBenignPopped(b.spec, x, y); } catch (err) { /* ignore */ }
  }

  function defuse(b, viaChannel) {
    if (b.state !== 'live') return;
    const fuseSecLeft = Math.max(0, b.fuseLeft) / 1000;
    if (b.channel) { b.channel = null; endChannelVisual(b); }
    popVisual(b, 0.2);
    floatText('SNAP', b.x, b.y - b.size * 0.2, 'cf-pop--snap');
    try { onDefused(b.spec, fuseSecLeft, viaChannel); } catch (err) { /* ignore */ }
  }
  function endChannelVisual(b) {
    b.chan.style.display = 'none';
    b.wrap.classList.remove('is-channeling');
    b.body.style.transform = '';
  }

  function detonate(b) {
    if (b.state !== 'live') return;
    b.state = 'popped';
    live.delete(b);
    if (b.channel) { b.channel = null; endChannelVisual(b); }
    b.wrap.classList.add('is-detonate');
    b.wrap.style.pointerEvents = 'none';
    b.wrap.addEventListener('animationend', () => b.wrap.remove(), { once: true });
    window.setTimeout(() => b.wrap.remove(), 700);
    const word = popWordFor(b.spec.variantId);
    if (word) floatText(word, b.x, b.y - b.size * 0.2, 'cf-pop--bad');
    try { onDetonated(b.spec); } catch (err) { /* ignore */ }
  }

  function expire(b) {
    if (b.state !== 'live') return;
    b.state = 'popped';
    live.delete(b);
    b.wrap.classList.add('is-fade');
    b.wrap.style.pointerEvents = 'none';
    window.setTimeout(() => b.wrap.remove(), 800);
    try { onTreatExpired(b.spec); } catch (err) { /* ignore */ }
  }

  // ---- the Ripple ------------------------------------------------------------------
  /** Any live bubble within reach of (x,y)? The cast is only offered near the field. */
  function nearAny(x, y, reach) {
    for (const b of live) {
      const dx = b.x - x, dy = b.y - y;
      if (dx * dx + dy * dy <= reach * reach) return true;
    }
    return false;
  }

  /** Expanding wavefront: treats pop PAID, lives snap clean (no focus cost).
   * Freeze pickups stay cursor-only. Hits land as the ring reaches each bubble. */
  function castRipple(x, y, radiusPx, lifeMs) {
    const ring = document.createElement('div');
    ring.className = 'cf-ripple';
    ring.style.left = `${x}px`;
    ring.style.top = `${y}px`;
    ring.style.setProperty('--r', `${radiusPx}px`);
    ring.style.setProperty('--life', `${lifeMs}ms`);
    fx.appendChild(ring);
    ring.addEventListener('animationend', () => ring.remove(), { once: true });

    const hits = [];
    for (const b of live) {
      const dx = b.x - x, dy = b.y - y;
      const d = Math.hypot(dx, dy);
      if (d <= radiusPx + b.size / 2 && b.spec.kind !== 'freeze') {
        hits.push({ b, at: Math.max(0, (d / radiusPx)) * lifeMs });
      }
    }
    for (const h of hits) {
      window.setTimeout(() => {
        if (h.b.state !== 'live') return;
        if (h.b.spec.kind === 'live') defuse(h.b, false);
        else popBenign(h.b, h.b.x, h.b.y);
      }, h.at);
    }
    return hits.length;
  }

  // ---- per-frame integration ---------------------------------------------------------
  function update(dt) {
    if (held) return;
    const ts = dt * timeScale;
    const w = W(), h = H();

    for (const b of Array.from(live)) {
      // channel runs on REAL time (a frozen field still channels - that's the
      // freeze's reward); the fuse holds while the hand is on it either way.
      if (b.channel) {
        b.channel.elapsed += dt * 1000;
        const p = Math.min(1, b.channel.elapsed / DEFUSE_HOLD_MS);
        b.chan.style.setProperty('--cdeg', `${p * 360}`);
        b.body.style.transform = `scale(${1 - (1 - CHANNEL_MIN_SCALE) * p})`;
        if (p >= 1) { defuse(b, true); }
        continue;   // the grip holds it still: no motion, no fuse, no rot
      } else if (b.spec.kind === 'live' && b.fuseLeft > 0) {
        b.fuseLeft -= ts * 1000;
        if (b.fuseLeft <= 0) { detonate(b); continue; }
      }

      // fuse ring phases
      if (b.ring && b.spec.kind === 'live') {
        const frac = Math.max(0, b.fuseLeft) / b.fuseTotal;
        b.ring.style.setProperty('--deg', `${frac * 360}`);
        const phase = b.fuseLeft <= RING_BRINK_MS ? 'brink'
          : b.fuseLeft <= RING_FLASH_FROM_MS ? 'flash' : 'burn';
        if (phase !== b.ringPhase) {
          b.ringPhase = phase;
          b.ring.classList.toggle('is-flash', phase === 'flash');
          b.ring.classList.toggle('is-brink', phase === 'brink');
          b.wrap.classList.toggle('is-brink', phase === 'brink');
        }
      }

      // treat rot
      if (b.ttlLeft > 0) {
        b.ttlLeft -= ts * 1000;
        if (b.ttlLeft <= 0) { expire(b); continue; }
        if (b.ttlLeft < 1200) b.wrap.style.opacity = String(Math.max(0.25, b.ttlLeft / 1200));
      }

      // motion
      const m = b.spec.motion;
      if (m === MOTION.FloatUp || m === MOTION.RainDown) {
        b.y += b.vy * ts;
        b.swayT += b.swaySpeed * ts;
        b.x = b.baseX + Math.sin(b.swayT) * b.swayAmp;
        if ((m === MOTION.FloatUp && b.y < -b.size) || (m === MOTION.RainDown && b.y > h + b.size)) {
          if (b.spec.kind === 'treat' || b.spec.kind === 'golden') expire(b);
          else { b.state = 'popped'; live.delete(b); b.wrap.remove(); } // freeze/live drift off harmlessly
          continue;
        }
      } else if (m === MOTION.SideDrift) {
        b.x += b.vx * ts;
        b.swayT += b.swaySpeed * ts;
        b.y = b.baseY + Math.sin(b.swayT) * (b.swayAmp * 1.2);
        if (b.x < -b.size || b.x > w + b.size) {
          if (b.spec.kind === 'treat' || b.spec.kind === 'golden') expire(b);
          else { b.state = 'popped'; live.delete(b); b.wrap.remove(); }
          continue;
        }
      } else { // RoamBounce
        b.x += b.vx * ts;
        b.y += b.vy * ts;
        const r = b.size / 2;
        if (b.x < r) { b.x = r; b.vx = Math.abs(b.vx); }
        else if (b.x > w - r) { b.x = w - r; b.vx = -Math.abs(b.vx); }
        if (b.y < r) { b.y = r; b.vy = Math.abs(b.vy); }
        else if (b.y > h - r) { b.y = h - r; b.vy = -Math.abs(b.vy); }
      }
      place(b);
    }
  }

  // ---- field-wide state -------------------------------------------------------------
  function setFrozen(v) {
    v = !!v;
    if (v === frozen) return;
    frozen = v;
    timeScale = v ? 0 : 1;
    layer.classList.toggle('cf-frozen', v);
    if (v) {
      for (const b of live) b.wrap.classList.add('cf-shudder');
      window.setTimeout(() => { for (const b of live) b.wrap.classList.remove('cf-shudder'); }, 260);
    }
  }

  function clearAll() {
    for (const b of live) b.wrap.remove();
    live.clear();
    for (const r of ripples) r.remove?.();
    ripples.length = 0;
  }

  function dispose() {
    clearAll();
    layer.remove();
    fx.remove();
  }

  return {
    spawn,
    update,
    floatText,
    playChime,
    castRipple,
    nearAny,
    clearAll,
    dispose,
    setFrozen,
    isFrozen: () => frozen,
    setTimeScale(f) { if (!frozen) timeScale = Math.max(0, f); },
    setHeld(v) { held = !!v; layer.classList.toggle('cf-held', held); },
    setInputLocked(v) { inputLocked = !!v; },
    count: () => live.size,
    freezeCount: () => { let n = 0; for (const b of live) if (b.spec.kind === 'freeze') n++; return n; },
    minFuseSec: () => {
      let min = null;
      for (const b of live) {
        if (b.spec.kind !== 'live') continue;
        const s = Math.max(0, b.fuseLeft) / 1000;
        if (min == null || s < min) min = s;
      }
      return min;
    },
  };
}
