/* ============================================================================
 * audio/trance.js - the Distraction mode's sound bed.
 *
 * Mort's ladder (2026-09-28): the match starts as plain game noise, then a
 * heartbeat comes in, then a soft binaural hum under it, then little whispered
 * snippets of the player's OWN brain drain clips, and as the meter climbs the
 * whispers get louder and the whisper filter opens until they are just voices.
 * The way DtRH plays short portions of videos, but with the audio files.
 *
 *   stage        on from meter   what it does
 *   heartbeat    heartbeat.from  lub-dub, slow and quiet, faster and fuller
 *   binaural     binaural.from   two sines a few Hz apart, left and right
 *   whispers     whisper.from    a random 2-4 s slice of a random clip, through
 *                                a highpass + presence band (the whisper), the
 *                                highpass sliding down as the meter rises
 *
 * Everything feeds the sfx master, so the page's volume slider, the hidden-tab
 * mute and the game's own mute all apply. The meter only arrives in
 * Distraction (the ramp pushes 0 in Classic and at game over), so Classic stays
 * plain game noise with no special case here.
 *
 * Clips: window.PBP.settings.whispers is the host's list of urls (the player's
 * brain drain folder first, a mod-appropriate fallback second). No list, no
 * whispers: the heartbeat and the hum still play. No synthetic speech, ever.
 *
 * Pure parts (stageLevels) are exported for tests; createTrance needs a live
 * AudioContext and is driven by audio/sfx.js.
 * ==========================================================================*/

export const TRANCE = Object.freeze({
  heartbeat: Object.freeze({
    from: 0.12,                 // Mort: "bring in the heartbeat sound earlier"
    bpmLow: 56, bpmHigh: 82,
    gainLow: 0.10, gainHigh: 0.28,
    lubHz: 58, dubHz: 50, sec: 0.16, dubAt: 0.26,
  }),
  binaural: Object.freeze({
    from: 0.30,
    carrierHz: 170,
    beatHzLow: 7, beatHzHigh: 4,  // alpha drifting down to theta as it deepens
    gainMax: 0.045,
    glideSec: 1.5,
  }),
  whisper: Object.freeze({
    from: 0.48,
    gapMsSlow: 15000, gapMsFast: 4500,
    sliceSecMin: 1.8, sliceSecMax: 3.6,
    fadeSec: 0.35,
    gainLow: 0.12, gainHigh: 0.55,
    // the whisper filter: a highpass that thins the voice to breath, sliding
    // down as the meter rises until the voice is whole again
    hpHzLow: 1900, hpHzHigh: 140,
    presenceHz: 3200, presenceGainLow: 6, presenceGainHigh: 0,
    breathGainLow: 0.05, breathGainHigh: 0.0,
    maxDecoded: 8,
  }),
});

const clamp01 = (v) => Math.max(0, Math.min(1, Number(v) || 0));
const lerp = (a, b, t) => a + (b - a) * clamp01(t);
/** 0 below `from`, 1 at a full meter. */
export const stageRamp = (m, from) => (m <= from ? 0 : clamp01((m - from) / Math.max(0.01, 1 - from)));

/** What each stage is doing at this meter. Pure: the tests pin the ladder. */
export function stageLevels(meter, t = TRANCE) {
  const m = clamp01(meter);
  const hb = stageRamp(m, t.heartbeat.from);
  const bi = stageRamp(m, t.binaural.from);
  const wh = stageRamp(m, t.whisper.from);
  return {
    heartbeat: {
      on: m > t.heartbeat.from,
      bpm: lerp(t.heartbeat.bpmLow, t.heartbeat.bpmHigh, hb),
      gain: m > t.heartbeat.from ? lerp(t.heartbeat.gainLow, t.heartbeat.gainHigh, hb) : 0,
    },
    binaural: {
      on: m > t.binaural.from,
      beatHz: lerp(t.binaural.beatHzLow, t.binaural.beatHzHigh, bi),
      gain: m > t.binaural.from ? t.binaural.gainMax * Math.min(1, bi * 2.5) : 0,
    },
    whisper: {
      on: m > t.whisper.from,
      gapMs: lerp(t.whisper.gapMsSlow, t.whisper.gapMsFast, wh),
      gain: m > t.whisper.from ? lerp(t.whisper.gainLow, t.whisper.gainHigh, wh) : 0,
      hpHz: lerp(t.whisper.hpHzLow, t.whisper.hpHzHigh, wh),
      presenceDb: lerp(t.whisper.presenceGainLow, t.whisper.presenceGainHigh, wh),
      breath: lerp(t.whisper.breathGainLow, t.whisper.breathGainHigh, wh),
    },
  };
}

/**
 * createTrance({ ctx, master, noise, clips, canBeat, rng, fetcher })
 *   setMeter(m)  follow the meter (cheap; call every frame if you like)
 *   stop()       silence everything now (Classic, game over, dispose)
 *   debug()      what is on
 */
export function createTrance({ ctx, master, noise = null, clips = () => [], canBeat = () => true,
                               rng = Math.random, fetcher = (typeof fetch === 'function' ? fetch : null),
                               tuning = TRANCE } = {}) {
  const T = tuning;
  let meter = 0;
  let levels = stageLevels(0, T);
  let disposed = false;
  let beatTimer = null;
  let whisperTimer = null;
  let whispersPlayed = 0;
  let lastClip = '';
  const decoded = new Map();     // url -> AudioBuffer | Promise | null (failed)
  const live = new Set();        // playing whisper sources, so stop() can cut them

  // --- binaural: two sines, hard left and hard right, one shared gain -------
  let bi = null;
  function ensureBinaural() {
    if (bi || !ctx || !master) return bi;
    try {
      const out = ctx.createGain();
      out.gain.value = 0;
      out.connect(master);
      const merger = typeof ctx.createChannelMerger === 'function' ? ctx.createChannelMerger(2) : null;
      const left = ctx.createOscillator();
      const right = ctx.createOscillator();
      left.type = 'sine'; right.type = 'sine';
      left.frequency.value = T.binaural.carrierHz;
      right.frequency.value = T.binaural.carrierHz + levels.binaural.beatHz;
      if (merger) {
        left.connect(merger, 0, 0);
        right.connect(merger, 0, 1);
        merger.connect(out);
      } else { left.connect(out); right.connect(out); }
      left.start(); right.start();
      bi = { out, left, right };
    } catch { bi = null; }
    return bi;
  }
  function applyBinaural() {
    const want = levels.binaural.gain;
    if (!bi && want <= 0) return;
    const b = ensureBinaural();
    if (!b) return;
    const now = ctx.currentTime;
    try {
      b.out.gain.setTargetAtTime(want, now, T.binaural.glideSec / 3);
      b.right.frequency.setTargetAtTime(T.binaural.carrierHz + levels.binaural.beatHz, now, T.binaural.glideSec / 3);
    } catch { b.out.gain.value = want; }
  }

  // --- heartbeat: lub-dub, rescheduled beat by beat so the tempo follows ----
  function thump(at, hz, gain) {
    const t0 = ctx.currentTime + at;
    const osc = ctx.createOscillator();
    const env = ctx.createGain();
    const lp = ctx.createBiquadFilter();
    osc.type = 'sine';
    osc.frequency.setValueAtTime(hz * 1.6, t0);
    osc.frequency.exponentialRampToValueAtTime(hz, t0 + 0.05);
    lp.type = 'lowpass';
    lp.frequency.value = 180;
    env.gain.setValueAtTime(0.0001, t0);
    env.gain.exponentialRampToValueAtTime(Math.max(0.0002, gain), t0 + 0.012);
    env.gain.exponentialRampToValueAtTime(0.0001, t0 + T.heartbeat.sec);
    osc.connect(lp); lp.connect(env); env.connect(master);
    osc.start(t0);
    osc.stop(t0 + T.heartbeat.sec + 0.03);
  }
  function beat() {
    beatTimer = null;
    if (disposed || !levels.heartbeat.on) return;
    // the check pulse is its own low thump; two at once reads as a stumble
    if (canBeat()) {
      try {
        const g = levels.heartbeat.gain;
        thump(0, T.heartbeat.lubHz, g);
        thump(T.heartbeat.dubAt * (60 / levels.heartbeat.bpm), T.heartbeat.dubHz, g * 0.7);
      } catch { /* a context that went away */ }
    }
    beatTimer = setTimeout(beat, Math.round(60000 / levels.heartbeat.bpm));
  }
  function applyHeartbeat() {
    if (levels.heartbeat.on && !beatTimer) beatTimer = setTimeout(beat, 200);
    if (!levels.heartbeat.on && beatTimer) { clearTimeout(beatTimer); beatTimer = null; }
  }

  // --- whispers: a slice of a player's clip through the whisper filter ------
  function load(url) {
    if (decoded.has(url)) return decoded.get(url);
    if (!fetcher || !ctx || decoded.size >= T.whisper.maxDecoded * 2) return null;
    const p = fetcher(url)
      .then(r => (r && r.ok ? r.arrayBuffer() : Promise.reject(new Error('http ' + (r && r.status)))))
      .then(buf => new Promise((res, rej) => {
        // the callback form still works on older WebView2 builds
        const out = ctx.decodeAudioData(buf, res, rej);
        if (out && typeof out.then === 'function') out.then(res, rej);
      }))
      .then(ab => { decoded.set(url, ab); return ab; })
      .catch(() => { decoded.set(url, null); return null; });
    decoded.set(url, p);
    return p;
  }
  function pickClip() {
    const list = (clips() || []).filter(u => typeof u === 'string' && u && decoded.get(u) !== null);
    if (!list.length) return null;
    const pool = list.length > 1 ? list.filter(u => u !== lastClip) : list;
    return pool[Math.floor(rng() * pool.length) % pool.length];
  }
  function playSlice(buffer) {
    const W = T.whisper;
    const L = levels.whisper;
    const len = Math.min(buffer.duration, W.sliceSecMin + rng() * (W.sliceSecMax - W.sliceSecMin));
    const offset = Math.max(0, (buffer.duration - len) * rng());
    const t0 = ctx.currentTime + 0.02;
    const src = ctx.createBufferSource();
    src.buffer = buffer;
    const hp = ctx.createBiquadFilter();
    hp.type = 'highpass'; hp.frequency.value = L.hpHz; hp.Q.value = 0.7;
    const presence = ctx.createBiquadFilter();
    presence.type = 'peaking'; presence.frequency.value = W.presenceHz; presence.Q.value = 0.9;
    presence.gain.value = L.presenceDb;
    const env = ctx.createGain();
    const fade = Math.min(W.fadeSec, len / 3);
    env.gain.setValueAtTime(0.0001, t0);
    env.gain.exponentialRampToValueAtTime(Math.max(0.0002, L.gain), t0 + fade);
    env.gain.setValueAtTime(Math.max(0.0002, L.gain), t0 + len - fade);
    env.gain.exponentialRampToValueAtTime(0.0001, t0 + len);
    // a little panned drift, so it sits beside the ear rather than in the middle
    let tail = env;
    if (typeof ctx.createStereoPanner === 'function') {
      const pan = ctx.createStereoPanner();
      pan.pan.value = (rng() * 2 - 1) * 0.6;
      env.connect(pan); tail = pan;
    }
    src.connect(hp); hp.connect(presence); presence.connect(env); tail.connect(master);
    src.start(t0, offset, len + 0.05);
    live.add(src);
    src.onended = () => live.delete(src);
    // the breath under a filtered voice is what makes it read as a whisper
    if (noise && L.breath > 0.001) {
      const b = ctx.createBufferSource();
      b.buffer = noise; b.loop = true;
      const bf = ctx.createBiquadFilter();
      bf.type = 'bandpass'; bf.frequency.value = 2400; bf.Q.value = 0.6;
      const be = ctx.createGain();
      be.gain.setValueAtTime(0.0001, t0);
      be.gain.exponentialRampToValueAtTime(L.breath, t0 + fade);
      be.gain.setValueAtTime(L.breath, t0 + len - fade);
      be.gain.exponentialRampToValueAtTime(0.0001, t0 + len);
      b.connect(bf); bf.connect(be); be.connect(tail === env ? master : tail);
      b.start(t0); b.stop(t0 + len + 0.05);
      live.add(b);
      b.onended = () => live.delete(b);
    }
    whispersPlayed += 1;
  }
  function whisper() {
    whisperTimer = null;
    if (disposed || !levels.whisper.on) return;
    const url = pickClip();
    const next = () => {
      if (!disposed && levels.whisper.on) {
        // jitter the gap so the slices never settle into a metronome
        whisperTimer = setTimeout(whisper, Math.round(levels.whisper.gapMs * (0.7 + rng() * 0.6)));
      }
    };
    if (!url) { next(); return; }
    lastClip = url;
    const got = load(url);
    const run = (buf) => { if (buf && !disposed && levels.whisper.on) { try { playSlice(buf); } catch { /* gone */ } } next(); };
    if (got && typeof got.then === 'function') got.then(run); else run(got);
  }
  function applyWhisper() {
    if (levels.whisper.on && !whisperTimer) {
      // warm the first couple of clips so the first slice lands on time
      for (const u of (clips() || []).slice(0, 2)) load(u);
      whisperTimer = setTimeout(whisper, Math.round(1500 + rng() * 2500));
    }
    if (!levels.whisper.on && whisperTimer) { clearTimeout(whisperTimer); whisperTimer = null; }
  }

  function setMeter(m) {
    if (disposed || !ctx || !master) return;
    const next = clamp01(m);
    if (Math.abs(next - meter) < 0.004 && next !== 0) return;
    meter = next;
    levels = stageLevels(meter, T);
    applyHeartbeat();
    applyBinaural();
    applyWhisper();
    if (meter === 0) cutWhispers();
  }
  function cutWhispers() {
    for (const s of [...live]) { try { s.stop(); } catch { /* ended */ } live.delete(s); }
  }
  function stop() {
    meter = 0;
    levels = stageLevels(0, T);
    if (beatTimer) { clearTimeout(beatTimer); beatTimer = null; }
    if (whisperTimer) { clearTimeout(whisperTimer); whisperTimer = null; }
    cutWhispers();
    if (bi) { try { bi.out.gain.setTargetAtTime(0, ctx.currentTime, 0.1); } catch { bi.out.gain.value = 0; } }
  }
  function dispose() {
    stop();
    disposed = true;
    if (bi) {
      try { bi.left.stop(); bi.right.stop(); bi.out.disconnect(); } catch { /* gone */ }
      bi = null;
    }
    decoded.clear();
  }
  function debug() {
    return { meter, levels, whispersPlayed, clips: (clips() || []).length, decoded: decoded.size, beating: !!beatTimer };
  }
  return { setMeter, stop, dispose, debug };
}

export default createTrance;
