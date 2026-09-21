/**
 * Renders the launcher's hover notes: Resources/sounds/launcher/wood_00.wav .. wood_17.wav.
 *
 * One rung per file, C pentatonic over three octaves from C4 (rung 5 is C5, the room's key), the
 * same ladder Breakout climbs on a combo (shared/sound/kit.js: PENTA [0,2,4,7,9], ROOT_HZ 523.25).
 *
 * WHAT SHIPS is `wood`, the dry pluck the owner picked on 2026-09-21 out of six proposals. The
 * other two recipes are kept because they were the alternatives and are cheap to read:
 *   note  - the bell. The Breakout cabinet's brick hit (stations/breakout/audio.js, case 'brick')
 *           through its small delay room. This is the cue the launcher shipped with.
 *   wood  - a dry pluck. Marimba partials, fast decay, no room. Snappy, close, percussive.
 *   glass - a long chime. Soft attack, an octave and a twelfth above it, a wetter room.
 *
 * Every voice is normalised so its root rung peaks where the old hover.wav did, which is the level
 * HoverScale is set for, and tilted down as it climbs so a high rung is not a loud rung.
 *
 * Offline on purpose: a sample cannot climb a ladder, and the set costs 400 KB and no runtime
 * synth. Re-run after changing the voice:
 *     node scripts/render-launcher-notes.mjs            the shipped voice
 *     node scripts/render-launcher-notes.mjs all        every recipe, to hear them side by side
 * Anything rendered by `all` that is not the shipped voice must come back out before a release.
 */
import { writeFileSync, mkdirSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const HERE = dirname(fileURLToPath(import.meta.url));
const OUT = join(HERE, '..', 'Resources', 'sounds', 'launcher');

const SR = 44100;
const ROOT_HZ = 523.25;          // C5, the room's key
const PENTA = [0, 2, 4, 7, 9];
const RUNGS = 18;                // keep in step with LauncherMelody.Rungs
const ROOT_RUNG = 5;             // rung 5 is C5; the five below it are the octave under
const TILT = -0.35;              // level tilt per octave, so a high rung is not a loud rung
const TARGET_PEAK = 0.7;         // the peak the old hover.wav carried

const semis = rung => 12 * Math.floor(rung / 5) + PENTA[rung % 5] - 12 * Math.floor(ROOT_RUNG / 5);
const SEMI = s => 2 ** (s / 12);

/** RBJ biquad, one channel, applied in place. */
function biquad(buf, { type, hz, q }) {
  const w0 = (2 * Math.PI * hz) / SR, cos = Math.cos(w0), sin = Math.sin(w0), alpha = sin / (2 * q);
  let b0, b1, b2, a0, a1, a2;
  if (type === 'lowpass') {
    b0 = (1 - cos) / 2; b1 = 1 - cos; b2 = b0;
    a0 = 1 + alpha; a1 = -2 * cos; a2 = 1 - alpha;
  } else {                                   // bandpass, constant 0 dB peak
    b0 = alpha; b1 = 0; b2 = -alpha;
    a0 = 1 + alpha; a1 = -2 * cos; a2 = 1 - alpha;
  }
  let x1 = 0, x2 = 0, y1 = 0, y2 = 0;
  for (let i = 0; i < buf.length; i++) {
    const x = buf[i];
    const y = (b0 / a0) * x + (b1 / a0) * x1 + (b2 / a0) * x2 - (a1 / a0) * y1 - (a2 / a0) * y2;
    x2 = x1; x1 = x; y2 = y1; y1 = y;
    buf[i] = y;
  }
  return buf;
}

/** The Web Audio envelope: exponential up over the attack, exponential down to the end. */
function envelope(n, dur, level, attack = 0.01) {
  const a = Math.max(0.003, Math.min(dur * 0.9, dur * attack));
  const t = n / SR;
  if (t >= dur) return 0;
  const lo = 0.0001;
  if (t <= a) return lo * (Math.max(0.0002, level) / lo) ** (t / a);
  return Math.max(0.0002, level) * (lo / Math.max(0.0002, level)) ** ((t - a) / (dur - a));
}

function tone(len, hz, dur, level, { attack, lp } = {}) {
  const buf = new Float64Array(len);
  const n = Math.min(len, Math.ceil(dur * SR));
  for (let i = 0; i < n; i++) buf[i] = Math.sin((2 * Math.PI * hz * i) / SR) * envelope(i, dur, level, attack);
  return lp ? biquad(buf, { type: 'lowpass', hz: lp, q: 0.707 }) : buf;
}

function noise(len, hz, dur, level, q = 1, seed = 1) {
  const buf = new Float64Array(len);
  const n = Math.min(len, Math.ceil(dur * SR));
  let s = seed >>> 0;
  const rnd = () => { s = (s * 1664525 + 1013904223) >>> 0; return (s / 0x100000000) * 2 - 1; };
  const raw = new Float64Array(len);
  for (let i = 0; i < Math.min(len, n + 64); i++) raw[i] = rnd();
  biquad(raw, { type: 'bandpass', hz, q });
  for (let i = 0; i < n; i++) buf[i] = raw[i] * envelope(i, dur, level);
  return buf;
}

/** The room: 110 ms delay, 0.36 feedback under a 3.2 kHz lowpass, tapped at `send`. */
function room(signal, send) {
  const out = new Float64Array(signal.length);
  const d = Math.round(0.11 * SR), line = new Float64Array(d);
  const w0 = (2 * Math.PI * 3200) / SR, alpha = Math.sin(w0) / (2 * 0.707), cos = Math.cos(w0);
  const a0 = 1 + alpha, b0 = (1 - cos) / 2 / a0, b1 = (1 - cos) / a0, b2 = b0;
  const a1 = (-2 * cos) / a0, a2 = (1 - alpha) / a0;
  let x1 = 0, x2 = 0, y1 = 0, y2 = 0, i = 0;
  for (let n = 0; n < signal.length; n++) {
    const x = line[i];
    const y = b0 * x + b1 * x1 + b2 * x2 - a1 * y1 - a2 * y2;
    x2 = x1; x1 = x; y2 = y1; y1 = y;
    out[n] = y;
    line[i] = signal[n] * send + y * 0.36;
    i = (i + 1) % d;
  }
  return out;
}

const VOICES = {
  /** The bell: what the launcher shipped with, one rung per file. */
  note: {
    seconds: 0.62,
    send: 0.28,
    layers: (len, hz, tilt, seed) => ({
      wet: tone(len, hz, 0.28, 0.12 * tilt, { lp: 4200 }),
      dry: [tone(len, hz * 2.76, 0.11, 0.028 * tilt, { lp: 4200 }),
            noise(len, 6000, 0.012, 0.045 * tilt, 0.8, seed)],
    }),
  },
  /** The pluck: marimba partials, fast decay, no room at all. Close and percussive. */
  wood: {
    seconds: 0.26,
    send: 0,
    layers: (len, hz, tilt, seed) => ({
      wet: new Float64Array(len),
      dry: [tone(len, hz, 0.13, 0.15 * tilt, { attack: 0.02, lp: 5200 }),
            tone(len, hz * 4, 0.05, 0.035 * tilt, { attack: 0.02, lp: 6500 }),
            tone(len, hz * 9.2, 0.02, 0.012 * tilt, { attack: 0.03 }),
            noise(len, 3800, 0.008, 0.05 * tilt, 1.4, seed)],
    }),
  },
  /** The chime: soft in, long out, an octave and a twelfth above, a wetter room. */
  glass: {
    seconds: 1.1,
    send: 0.45,
    layers: (len, hz, tilt, seed) => ({
      wet: tone(len, hz, 0.62, 0.10 * tilt, { attack: 0.05, lp: 6000 }),
      dry: [tone(len, hz * 2, 0.42, 0.028 * tilt, { attack: 0.08, lp: 6000 }),
            tone(len, hz * 2.98, 0.2, 0.012 * tilt, { attack: 0.06, lp: 6000 }),
            noise(len, 9000, 0.02, 0.012 * tilt, 0.9, seed)],
    }),
  },
};

mkdirSync(OUT, { recursive: true });

const peakOf = mix => { let p = 0; for (const v of mix) p = Math.max(p, Math.abs(v)); return p; };

// The shipped voice, unless the command line asks for every recipe.
const SHIP = ['wood'];
const wanted = process.argv.includes('all') ? Object.keys(VOICES) : SHIP;

for (const name of wanted) {
  const voice = VOICES[name];
  const len = Math.ceil(voice.seconds * SR);
  const fade = Math.round(0.02 * SR);
  const rendered = [];

  for (let rung = 0; rung < RUNGS; rung++) {
    const hz = ROOT_HZ * SEMI(semis(rung));
    const tilt = (hz / ROOT_HZ) ** TILT;
    const { wet, dry } = voice.layers(len, hz, tilt, rung + 7);
    const tail = voice.send > 0 ? room(wet, voice.send) : null;

    const mix = new Float64Array(len);
    for (let i = 0; i < len; i++) {
      let v = wet[i] + (tail ? tail[i] : 0);
      for (const layer of dry) v += layer[i];
      const left = len - i;
      if (left < fade) v *= left / fade;                  // never end on a step
      mix[i] = v;
    }
    rendered.push({ rung, hz, mix });
  }

  // One gain per voice, anchored on the root rung, so the tilt above is what a rung's level says
  // and nothing else - and so switching voice in the dev switcher is not a jump in volume.
  const gain = TARGET_PEAK / peakOf(rendered[ROOT_RUNG].mix);
  const loudest = Math.max(...rendered.map(r => peakOf(r.mix) * gain));
  if (loudest > 0.99) throw new Error(name + ' would clip at ' + loudest.toFixed(3));

  for (const r of rendered) {
    const data = Buffer.alloc(len * 2);
    for (let i = 0; i < len; i++) {
      const v = Math.max(-1, Math.min(1, r.mix[i] * gain));
      data.writeInt16LE(Math.round(v * 32767), i * 2);
    }
    const head = Buffer.alloc(44);
    head.write('RIFF', 0); head.writeUInt32LE(36 + data.length, 4); head.write('WAVE', 8);
    head.write('fmt ', 12); head.writeUInt32LE(16, 16); head.writeUInt16LE(1, 20); head.writeUInt16LE(1, 22);
    head.writeUInt32LE(SR, 24); head.writeUInt32LE(SR * 2, 28); head.writeUInt16LE(2, 32); head.writeUInt16LE(16, 34);
    head.write('data', 36); head.writeUInt32LE(data.length, 40);
    writeFileSync(join(OUT, name + '_' + String(r.rung).padStart(2, '0') + '.wav'), Buffer.concat([head, data]));
  }

  const kb = Math.round((len * 2 + 44) * RUNGS / 1024);
  console.log(name.padEnd(5) + '  ' + RUNGS + ' rungs  ' + voice.seconds.toFixed(2) + ' s  peak ' + loudest.toFixed(3) + '  ' + kb + ' KB');
}
