/* Circe preset playback for the isolated web playtest. No runtime API calls. */
export function createPresetVoice({ Context = globalThis.AudioContext, fetcher = globalThis.fetch, base = '/backroom/voice/' } = {}) {
  const words = new Set(['drop','relax','let go','sink','deeper','empty','obey','softer','blank']);
  const cache = new Map();
  let ctx, source, gain, epoch = 0;
  function context() { return ctx || (ctx = new Context()); }
  function load(text) {
    if (!cache.has(text)) cache.set(text, fetcher(base + text.replaceAll(' ', '-') + '.mp3')
      .then(r => { if (!r.ok) throw new Error('Missing preset'); return r.arrayBuffer(); })
      .then(data => context().decodeAudioData(data)).catch(() => null));
    return cache.get(text);
  }
  function stop() {
    epoch++;
    try { source?.stop(); } catch (_) { /* already ended */ }
    try { source?.disconnect(); gain?.disconnect(); } catch (_) { /* already detached */ }
    source = gain = null;
  }
  function arm() {
    try { context().resume().catch(() => {}); for (const word of words) load(word); } catch (_) { /* unavailable audio */ }
  }
  async function speak({ text, reversed = false, volume } = {}) {
    stop(); const ticket = epoch;
    const key = String(text || '').trim().toLowerCase().replace(/\s+/g, ' ');
    // Never revive the browser's harsh fallback if a recording cannot play.
    const quiet = { source: 'preset', durationMs: 0 };
    if (!words.has(key)) return quiet;
    try {
      const c = context();
      let timer;
      const buffer = await Promise.race([load(key), new Promise(resolve => { timer = setTimeout(() => resolve(null), 800); })]);
      clearTimeout(timer);
      if (!buffer || ticket !== epoch || c.state !== 'running') return quiet;
      let audio = buffer;
      if (reversed) {
        audio = c.createBuffer(buffer.numberOfChannels, buffer.length, buffer.sampleRate);
        for (let n = 0; n < buffer.numberOfChannels; n++) audio.getChannelData(n).set(buffer.getChannelData(n).slice().reverse());
      }
      const s = c.createBufferSource(), g = c.createGain();
      s.buffer = audio; g.gain.value = 0.65 * (Number.isFinite(volume) ? Math.max(0, Math.min(1, volume)) : 1); s.connect(g); g.connect(c.destination);
      source = s; gain = g;
      s.onended = () => { s.disconnect(); g.disconnect(); if (source === s) source = gain = null; };
      s.start();
      return { source: 'preset', durationMs: Math.ceil(audio.duration * 1000) };
    } catch (_) { return quiet; }
  }
  return { arm, speak, stop };
}
