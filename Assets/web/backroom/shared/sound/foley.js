/* Physical cues. Dry transients stay small enough to sit below the win palette. */
const clamp = (n, a, b) => Math.min(b, Math.max(a, Number(n) || 0));
const level = o => o.level == null ? 1 : clamp(o.level, 0, 1);
const noise = (hz, dur, gain, at = 0) => ({ k: 'noise', hz, dur, level: gain, at, q: 1.3, dry: true, part: 'foley' });
const tone = (hz, dur, gain, at = 0) => ({ k: 'tone', hz, dur, level: gain, at, wave: 'sine', dry: true, part: 'body' });
export const FOLEY = Object.freeze({
  'card-slide': (o = {}) => [noise(2100, 0.16, 0.055 * level(o))],
  'card-land': (o = {}) => [noise(850, 0.045, 0.07 * level(o)), tone(165, 0.06, 0.035 * level(o))],
  'card-flip': (o = {}) => [noise(2800, 0.065, 0.045 * level(o)), noise(1400, 0.07, 0.035 * level(o), 0.07)],
  'deck-square': (o = {}) => [noise(950, 0.04, 0.05 * level(o)), tone(210, 0.07, 0.04 * level(o))],
  'cabinet-knock': (o = {}) => [tone(95, 0.16, 0.12 * level(o)), noise(650, 0.06, 0.055 * level(o)), noise(1400, 0.07, 0.025 * level(o), 0.07)],
  'freeze-latch': (o = {}) => [noise(o.released ? 1800 : 2400, 0.04, 0.055 * level(o)), tone(o.released ? 320 : 480, 0.09, 0.045 * level(o), 0.025)],
  'wheel-peg': (o = {}) => [noise(1300 * 2 ** (clamp(o.semis, -24, 24) / 12), 0.023, 0.045 * level(o)), tone(330, 0.024, 0.018 * level(o))],
  'wheel-start': (o = {}) => [noise(470, 0.2, 0.04 * level(o)), tone(115, 0.13, 0.07 * level(o))],
  'roulette-pocket': (o = {}) => [noise(1700, 0.018, 0.05 * level(o)), tone(740, 0.035, 0.035 * level(o))],
  'chip-place': (o = {}) => [tone(2100, 0.04, 0.05 * level(o)), noise(3100, 0.022, 0.04 * level(o)), tone(1600, 0.025, 0.018 * level(o), 0.035)],
  'prize-drop': (o = {}) => [tone(140, 0.12, 0.09 * level(o)), noise(900, 0.05, 0.04 * level(o)), tone(280, 0.07, 0.035 * level(o), 0.08)],
});

// Generated once, bundled locally. No credentials or generation calls in the client.
export const SAMPLE_CUES = Object.freeze(['card-slide', 'cabinet-knock', 'chip-place']);
export async function loadFoleySample(name, context) {
  if (!SAMPLE_CUES.includes(name) || typeof fetch !== 'function' || typeof context.decodeAudioData !== 'function') return null;
  const response = await fetch(new URL('./assets/' + name + '.mp3', import.meta.url));
  if (!response.ok) return null;
  const bytes = await response.arrayBuffer();
  if (bytes.byteLength > 128 * 1024) return null;
  const buffer = await context.decodeAudioData(bytes);
  return buffer.duration > 0 && buffer.duration <= 1.5 ? buffer : null;
}
