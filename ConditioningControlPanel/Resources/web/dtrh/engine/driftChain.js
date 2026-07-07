/* ============================================================================
 * driftChain.js (Sissy Fall) - the continuous hypnotic voice layer.
 *
 * Plays the FALL_DRIFT corpus as an endless chain: 4-8 blocks drawn from the
 * drift/sensory/descent pools with short gaps between them, then exactly ONE
 * resolve (the "good girl" payoff, layered with a chime), then a longer hold
 * so the payoff lands against silence, then the cycle re-rolls. Sensory picks
 * are weighted up as the fall gets deeper. This replaces the old card barks /
 * depth narration / ambient giggles - it is the fall's only voice.
 *
 * Channel plumbing (single shared element, per-pool rotation queues, global
 * last-clip guard, play retries, mute + tab-visibility handling) follows the
 * barks.js pattern. The chain is timer-driven, NOT frame-driven, so scene.js
 * must stop()/start() it explicitly on ESC pause, run end and restart.
 * ==========================================================================*/

import { FALL_DRIFT } from '/dtrh/assets/barks/manifest.js';
import { isMuted, onMuteChange } from '../shared/audioMute.js';
import { getLevel, onLevels } from './audioLevels.js';
import { getAudioCtx, makeSfxPlayer } from './audioBus.js';

const BASE = '/dtrh/assets/barks/';
const SFX_BASE = '/dtrh/assets/bubbles/sfx/';
const CHIME_SFX = ['chime1.mp3', 'chime2.mp3', 'chime3.mp3'];

const CHIME_VOL = 0.4;
const START_DELAY_MS = 6000;    // first line lands after the plunge settles
const GAP_MIN_MS = 900;         // breath between chained blocks
const GAP_MAX_MS = 2200;
const RESOLVE_HOLD_MIN_MS = 4500; // longer silence after the payoff (contrast)
const RESOLVE_HOLD_MAX_MS = 7000;
const BLOCKS_MIN = 4;           // blocks per cycle before the resolve fires
const BLOCKS_MAX = 8;
const CLIP_MAX_MS = 25000;      // a lost 'ended' can't stall the chain
const RETRY_GAP_MS = 600;
const FAIL_HOLD_MS = 3000;      // reschedule after a failed play (self-heal)
const SENSORY_DEPTH_REF = 4000; // depth where the sensory weighting saturates
const RATE_BASE_SPEED = 6;      // tube speed at which the voice plays at 1.0x
const RATE_MAX_SPEED = 30;      // tube speed at which the voice hits its ceiling
const RATE_MAX = 1.43;          // +43% tempo flat out (pitch preserved by default)

export function createDriftChain({ getDepth }) {
  // One shared element: the chain speaks one line at a time.
  const el = new Audio();
  el.preload = 'auto';

  // Loudness lives in a gain node on the shared context, NOT el.volume - iOS
  // ignores volume on media elements outright, so the voice slider did nothing
  // there (the voice always spoke at 100%). The element binds to the context
  // once, lazily, on the first play; browsers with no WebAudio keep the
  // el.volume path (it works everywhere elements respect volume).
  let voiceGain = null, voiceBound = false;
  let duck = 1; // scene-side mix duck (the spotlight stage owns the foreground)
  function ensureVoiceRoute() {
    if (voiceBound) return;
    voiceBound = true;
    const ctx = getAudioCtx();
    if (!ctx) return;
    try {
      const src = ctx.createMediaElementSource(el);
      voiceGain = ctx.createGain();
      voiceGain.gain.value = getLevel('voice');
      src.connect(voiceGain); voiceGain.connect(ctx.destination);
      el.volume = 1; // loudness lives in the gain node now
    } catch (e) { voiceGain = null; }
  }
  function applyVoiceVolume() {
    const v = getLevel('voice') * duck;
    if (voiceGain) { try { voiceGain.gain.value = v; } catch (e) { /* ignore */ } }
    else { try { el.volume = v; } catch (e) { /* ignore */ } }
  }
  function setDuck(m) {
    m = Math.max(0, Math.min(1, m));
    if (m === duck) return;
    duck = m;
    applyVoiceVolume();
  }

  // Speech tempo tracks the fall speed: faster tube -> faster whispering, up to
  // RATE_MAX. Pitch is preserved (browsers keep pitch on playbackRate change by
  // default) so it reads as urgency, not a chipmunk. Set on the live clip and
  // re-applied to every new clip in playNext().
  let voiceRate = 1;
  function setSpeed(sp) {
    const t = Math.min(1, Math.max(0, (sp - RATE_BASE_SPEED) / (RATE_MAX_SPEED - RATE_BASE_SPEED)));
    voiceRate = 1 + (RATE_MAX - 1) * t;
    try { if (!el.paused) el.playbackRate = voiceRate; } catch (e) { /* ignore */ }
  }

  // Resolve chime: buffer-backed through the shared player (element clones
  // ignored their volume on iOS - the chime blasted at 100% there). It rides
  // the voice slider + duck so it lands WITH the payoff line it decorates.
  const sfx = makeSfxPlayer();
  sfx.preload(CHIME_SFX.map((n) => SFX_BASE + n));
  function playChime() {
    const name = CHIME_SFX[(Math.random() * CHIME_SFX.length) | 0];
    sfx.play(SFX_BASE + name, CHIME_VOL * getLevel('voice') * duck);
  }

  // Per-pool rotating queues of indices + the last clip fired anywhere -
  // both lifted from barks.js so a line never repeats back to back and a
  // resolve never repeats across consecutive cycles.
  const queues = {};
  function queueFor(id, len) {
    if (!queues[id] || queues[id].length !== len) {
      queues[id] = Array.from({ length: len }, (_, i) => i);
    }
    return queues[id];
  }
  let lastKey = null;
  const keyOf = (b) => b.mod + '/' + b.file;

  function pick(poolId) {
    const pool = FALL_DRIFT[poolId];
    if (!pool || !pool.length) return null;
    const eligible = pool.map((_, i) => i).filter((i) => keyOf(pool[i]) !== lastKey);
    if (!eligible.length) return null;
    let idx;
    if (eligible.length === 1) {
      idx = eligible[0];
    } else {
      const q = queueFor(poolId, pool.length);
      const tail = q[q.length - 1];
      let choices = eligible.filter((i) => i !== tail);
      if (!choices.length) choices = eligible;
      idx = choices[Math.floor(Math.random() * choices.length)];
      const qi = q.indexOf(idx);
      if (qi >= 0) { q.splice(qi, 1); q.push(idx); }
    }
    return pool[idx];
  }

  // Deeper = more pure-texture sensory lines; drift stays the backbone; babble
  // (the brainy pseudo-neuro register) is sprinkled in throughout for variety.
  function pickPool() {
    let depth = 0;
    try { depth = getDepth ? getDepth() : 0; } catch (e) { /* ignore */ }
    const t = Math.min(1, Math.max(0, depth / SENSORY_DEPTH_REF));
    const wDrift = 1.0, wBabble = 0.5, wDescent = 0.35, wSensory = 0.25 + 1.0 * t;
    let r = Math.random() * (wDrift + wBabble + wDescent + wSensory);
    if ((r -= wDrift) < 0) return 'drift';
    if ((r -= wBabble) < 0) return 'babble';
    if ((r -= wDescent) < 0) return 'descent';
    return 'sensory';
  }

  // ---- chain state -----------------------------------------------------------
  const rand = (min, max) => min + Math.random() * (max - min);
  const rollBlocks = () => BLOCKS_MIN + ((Math.random() * (BLOCKS_MAX - BLOCKS_MIN + 1)) | 0);

  let blocksLeft = rollBlocks();
  let started = false;       // armed by start(), disarmed by stop()/dispose()
  let firstStart = true;
  let pendingResume = false; // a clip was cut mid-sentence (stop/mute/hidden)
  let currentIsResolve = false; // what the playing clip is, for advance()
  let disposed = false;
  let gapTimer = null;
  let safetyTimer = null;

  function clearGap() { if (gapTimer) { clearTimeout(gapTimer); gapTimer = null; } }
  function clearSafety() { if (safetyTimer) { clearTimeout(safetyTimer); safetyTimer = null; } }
  function armSafety() {
    clearSafety();
    safetyTimer = setTimeout(() => {
      safetyTimer = null;
      el.onended = null;
      advance(currentIsResolve);
    }, CLIP_MAX_MS);
  }

  function scheduleNext(ms) {
    if (!started) return;
    clearGap();
    gapTimer = setTimeout(playNext, ms);
  }

  function advance(wasResolve) {
    if (wasResolve) {
      blocksLeft = rollBlocks();
      scheduleNext(rand(RESOLVE_HOLD_MIN_MS, RESOLVE_HOLD_MAX_MS));
    } else {
      blocksLeft--;
      scheduleNext(rand(GAP_MIN_MS, GAP_MAX_MS));
    }
  }

  function playNext() {
    gapTimer = null;
    if (!started || disposed) return;
    if (document.hidden || isMuted()) return; // the show/unmute handlers re-arm
    const isResolve = blocksLeft <= 0;
    const bark = isResolve ? pick('resolve') : pick(pickPool());
    if (!bark) { scheduleNext(FAIL_HOLD_MS); return; }
    lastKey = keyOf(bark);
    try {
      el.pause();
      el.currentTime = 0;
      el.onended = null;
      el.src = BASE + bark.mod + '/' + bark.file;
      ensureVoiceRoute();
      applyVoiceVolume();
      try { el.playbackRate = voiceRate; } catch (e) { /* ignore */ }
      pendingResume = false;
      currentIsResolve = isResolve;
      el.onended = () => { el.onended = null; clearSafety(); advance(isResolve); };
      armSafety();
      const mySrc = el.src;
      let chimed = false;
      const attempt = (left) => {
        const p = el.play();
        if (!p || !p.then) { if (isResolve && !chimed) { chimed = true; playChime(); } return; }
        p.then(() => {
          if (isResolve && !chimed && el.src === mySrc) { chimed = true; playChime(); }
        }).catch(() => {
          if (left > 0 && el.src === mySrc && started && !isMuted() && !document.hidden) {
            setTimeout(() => attempt(left - 1), RETRY_GAP_MS);
          } else if (el.src === mySrc) {
            // missing clip / hard autoplay refusal: skip it, keep the chain alive
            el.onended = null;
            clearSafety();
            scheduleNext(FAIL_HOLD_MS);
          }
        });
      };
      attempt(4);
    } catch (e) {
      clearSafety();
      scheduleNext(FAIL_HOLD_MS);
    }
  }

  // Pause the channel without disarming the chain (mute / hidden tab). The
  // safety timer is cleared too - left armed it would fire mid-pause, null
  // onended, and the resumed clip would end into a stalled chain.
  function softPause() {
    clearGap();
    clearSafety();
    try {
      if (!el.paused) { el.pause(); pendingResume = true; }
    } catch (e) { /* ignore */ }
  }
  function softResume() {
    if (!started || disposed || isMuted() || document.hidden) return;
    if (pendingResume) {
      pendingResume = false;
      armSafety();
      const p = el.play();
      if (p && p.catch) p.catch(() => { clearSafety(); scheduleNext(FAIL_HOLD_MS); });
    } else if (!gapTimer && el.paused) {
      scheduleNext(rand(GAP_MIN_MS, GAP_MAX_MS));
    }
  }

  const offMute = onMuteChange((m) => { if (m) softPause(); else softResume(); });

  // live voice-slider: apply to the clip that's already playing
  const offLevels = onLevels((group) => { if (group === 'voice') applyVoiceVolume(); });

  const onVisibility = () => { if (document.hidden) softPause(); else softResume(); };
  document.addEventListener('visibilitychange', onVisibility);

  // ---- public lifecycle --------------------------------------------------------
  function start() {
    if (disposed || started) return;
    started = true;
    if (firstStart) { firstStart = false; scheduleNext(START_DELAY_MS); return; }
    softResume();
  }

  function stop() {
    if (!started) return;
    started = false;
    softPause();
  }

  function dispose() {
    stop();
    disposed = true;
    offMute();
    offLevels();
    document.removeEventListener('visibilitychange', onVisibility);
    try { el.pause(); el.onended = null; el.removeAttribute('src'); } catch (e) { /* ignore */ }
  }

  // Is a voice line playing RIGHT NOW? scene.js sidechains the drone bed off
  // this every frame, so the music dips under a line and swells back in gaps.
  const isSpeaking = () => started && !el.paused;

  // read-only state for the ?e2e hook
  const debugState = () => ({ started, blocksLeft, speaking: !el.paused, routed: !!voiceGain, duck });

  return { start, stop, setSpeed, setDuck, isSpeaking, dispose, debugState };
}
