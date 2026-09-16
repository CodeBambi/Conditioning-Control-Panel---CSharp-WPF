/* ============================================================================
 * make-samples.mjs - draws the three bundled sample loops.
 *
 *   node remix/test/make-samples.mjs
 *
 * The empty canvas is never blank: before the first drop it shows a mosaic
 * built from these. They are drawn here, pixel by pixel, out of two brand
 * hues and the ground colour, so nothing third party is ever bundled. Every
 * loop wraps exactly: the phase runs one whole turn across the frames.
 *
 * Output: remix/assets/sample/{drift,pulse,spin}.gif
 * ==========================================================================*/

import { writeFileSync, mkdirSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { GIFEncoder, quantize, applyPalette } from '../vendor/gifenc.esm.js';

const HERE = dirname(fileURLToPath(import.meta.url));
const OUT = join(HERE, '..', 'assets', 'sample');

const W = 200;
const H = 112;
const FRAMES = 12;
const DELAY = 80;
const MAX_BYTES = 60 * 1024;

const GROUND = [0x14, 0x14, 0x2b];
const PINK = [0xff, 0x69, 0xb4];
const LAV = [0xb8, 0xa6, 0xe8];

const TAU = Math.PI * 2;

const LOOPS = {
  drift: (x, y, phase) => {
    const v = Math.sin(x * 0.045 + y * 0.02 + phase) * 0.5 + 0.5;
    const w = Math.sin(y * 0.05 - phase * 2) * 0.5 + 0.5;
    return { mix: v * 0.75 + 0.12, hue: w };
  },
  pulse: (x, y, phase) => {
    const dx = x - W / 2, dy = (y - H / 2) * 1.6;
    const r = Math.hypot(dx, dy);
    const v = Math.sin(r * 0.09 - phase * 2) * 0.5 + 0.5;
    const fade = Math.max(0, 1 - r / (W * 0.62));
    return { mix: v * fade * 0.95 + 0.08, hue: fade };
  },
  spin: (x, y, phase) => {
    const dx = x - W / 2, dy = (y - H / 2) * 1.6;
    const a = Math.atan2(dy, dx);
    const r = Math.hypot(dx, dy);
    const v = Math.sin(a * 3 + r * 0.07 - phase) * 0.5 + 0.5;
    const fade = Math.max(0, 1 - r / (W * 0.7));
    return { mix: Math.pow(v, 1.6) * fade + 0.06, hue: 1 - fade };
  },
};

function frameRgba(shape, phase) {
  const data = new Uint8Array(W * H * 4);
  let i = 0;
  for (let y = 0; y < H; y++) {
    for (let x = 0; x < W; x++) {
      const { mix, hue } = shape(x, y, phase);
      const m = clamp(mix, 0, 1);
      const h = clamp(hue, 0, 1);
      const cr = PINK[0] + (LAV[0] - PINK[0]) * h;
      const cg = PINK[1] + (LAV[1] - PINK[1]) * h;
      const cb = PINK[2] + (LAV[2] - PINK[2]) * h;
      data[i++] = Math.round(GROUND[0] + (cr - GROUND[0]) * m);
      data[i++] = Math.round(GROUND[1] + (cg - GROUND[1]) * m);
      data[i++] = Math.round(GROUND[2] + (cb - GROUND[2]) * m);
      data[i++] = 255;
    }
  }
  return data;
}

function clamp(v, lo, hi) { return v < lo ? lo : v > hi ? hi : v; }

function build(name, shape, colours) {
  const frames = [];
  for (let f = 0; f < FRAMES; f++) frames.push(frameRgba(shape, (TAU * f) / FRAMES));

  // one palette for the whole loop, from four frames spread across it
  const sample = new Uint8Array(4 * W * H * 4);
  for (let i = 0; i < 4; i++) sample.set(frames[Math.floor((i * FRAMES) / 4)], i * W * H * 4);
  const palette = quantize(sample, colours, { format: 'rgb444' });

  const gif = GIFEncoder();
  frames.forEach((rgba, i) => {
    gif.writeFrame(applyPalette(rgba, palette, 'rgb444'), W, H, {
      palette: i === 0 ? palette : undefined,
      delay: DELAY,
      repeat: 0,
      first: i === 0,
    });
  });
  gif.finish();
  return Buffer.from(gif.bytes());
}

mkdirSync(OUT, { recursive: true });
let worst = 0;
for (const [name, shape] of Object.entries(LOOPS)) {
  let bytes = null;
  for (const colours of [32, 24, 16, 8]) {
    bytes = build(name, shape, colours);
    if (bytes.length <= MAX_BYTES) break;
  }
  if (bytes.length > MAX_BYTES) throw new Error(`${name} is ${bytes.length} bytes, over the cap`);
  writeFileSync(join(OUT, name + '.gif'), bytes);
  worst = Math.max(worst, bytes.length);
  console.log(`${name}.gif  ${W}x${H}  ${FRAMES} frames  ${(bytes.length / 1024).toFixed(1)} KB`);
}
console.log(`largest ${(worst / 1024).toFixed(1)} KB, cap ${MAX_BYTES / 1024} KB`);
