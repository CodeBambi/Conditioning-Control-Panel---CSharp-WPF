/* ============================================================================
 * shared/hypno/tests/handedness.js - measures law 3 on pixels (page only; kit-check.mjs runs it through kit.html).
 *
 * Two numbers per field, both read on rings of samples around the centre, where a
 * growing sample angle is clockwise on screen (canvas y points down):
 *   turn  how far the pattern moved between two frames (a larger angle, or a later GIF frame). Positive = clockwise.
 *   rim   how far a ring a few px further out is shifted from the one inside it, in the same frame. Positive = the
 *         arms lead at the rim, which is what makes a clockwise turn read as pulling inward.
 * A preset with a second layer (screen's thin gold threads) is measured again on a colour mask of that layer's one
 * thread, so layer 2 has to turn clockwise and lead at the rim on its own (owner, 2026-09-14: same way as layer 1).
 * A shift is searched within half an arm span, so it can never alias to the next arm.
 * ==========================================================================*/

import { LOOM_PRESETS, spanRad } from '../loom.js';

const TAU = Math.PI * 2;
const N = 1440;
const deg = (s) => (s / N) * 360;

/** RGB samples on a circle of radius r around the centre of a w x h RGBA buffer. */
export function ring(img, w, h, r) {
  if (r < 1 || r >= Math.min(w, h) / 2) throw new Error(`ring radius ${r} does not fit a ${w}x${h} frame`);   // never sample off the picture
  const out = new Float32Array(N * 3), cx = w / 2, cy = h / 2;
  for (let i = 0; i < N; i++) {
    const a = (i / N) * TAU, x = Math.round(cx + r * Math.cos(a)), y = Math.round(cy + r * Math.sin(a)), o = (y * w + x) * 4;
    out[i * 3] = img[o]; out[i * 3 + 1] = img[o + 1]; out[i * 3 + 2] = img[o + 2];
  }
  return out;
}

/** A ring scored by closeness to `hex` (255 on the colour, 0 from 90 RGB units away), in all three channels. */
export function maskRing(img, w, h, r, hex) {
  const n = parseInt(String(hex).slice(1), 16), R = (n >> 16) & 255, G = (n >> 8) & 255, B = n & 255, out = ring(img, w, h, r);
  for (let i = 0; i < N; i++) {
    const d = Math.hypot(out[i * 3] - R, out[i * 3 + 1] - G, out[i * 3 + 2] - B), v = Math.max(0, 1 - d / 90) * 255;
    out[i * 3] = out[i * 3 + 1] = out[i * 3 + 2] = v;
  }
  return out;
}

/** The s (in samples, |s| <= max) for which b[i] best matches a[i - s]. */
export function shift(a, b, max) {
  let best = 0, bestErr = Infinity;
  for (let s = -max; s <= max; s++) {
    let err = 0;
    for (let i = 0; i < N; i++) { const j = ((i - s) % N + N) % N; for (let c = 0; c < 3; c++) { const d = b[i * 3 + c] - a[j * 3 + c]; err += d * d; } }
    if (err < bestErr) { bestErr = err; best = s; }
  }
  return best;
}

const halfArm = (name) => Math.floor(N / LOOM_PRESETS[name].layer.arms / 2) - 1;
const layer2Of = (name) => (LOOM_PRESETS[name].layer2 && LOOM_PRESETS[name].layer2.enabled ? LOOM_PRESETS[name].layer2 : null);

/** Layer 2 alone, on its thread colour: turn between frames A and B, rim lead on A. */
function measureLayer2(name, A, B, w, h, rs, dr) {
  const l2 = layer2Of(name);
  if (!l2) return null;
  const hex = l2.colors[0], max = Math.floor(N / l2.arms / 2) - 1;
  const turn = rs.map((r) => deg(shift(maskRing(A, w, h, r, hex), maskRing(B, w, h, r, hex), max)));
  const rim = rs.map((r) => deg(shift(maskRing(A, w, h, r, hex), maskRing(A, w, h, r + dr, hex), max)));
  return { color: hex, radii: rs, turn, rim, ok: turn.every((v) => v > 0) && rim.every((v) => v > 0) };
}

/** A preset painted by the kit at angle 0.4 and 0.4 + delta, on a 512 square. */
export function measureKit(kit, name, delta = 0.12) {
  const S = 512, max = halfArm(name);
  const cv = Object.assign(document.createElement('canvas'), { width: S, height: S }), g = cv.getContext('2d', { willReadFrequently: true });
  const frame = (angle) => { g.clearRect(0, 0, S, S); kit.paint(cv, name, { angle }); return g.getImageData(0, 0, S, S).data; };
  const A = frame(0.4), B = frame(0.4 + delta);
  const turn = [70, 110, 150].map((r) => deg(shift(ring(A, S, S, r), ring(B, S, S, r), max)));
  const dr = kit.webgl ? 3 : 9;   // the 2D fallback draws 48 stepped rings: step far enough to cross one
  const rim = [60, 90, 120, 150].map((r) => deg(shift(ring(A, S, S, r), ring(A, S, S, r + dr), max)));
  const turnDeg = turn.reduce((x, y) => x + y, 0) / turn.length;
  const layer2 = kit.webgl ? measureLayer2(name, A, B, S, S, [70, 110, 150], dr) : null;   // the 2D fallback draws layer 1 only
  return { name, delta, turn, turnDeg, rim, layer2, ok: turn.every((v) => v > 0) && rim.every((v) => v > 0) && (!layer2 || layer2.ok), webgl: kit.webgl };
}

/** A woven GIF (the loop runs phase 0 -> 1 over its frames): frame 0 against frame 2, rim lead on frame 0. */
export async function measureGif(url, name) {
  const res = await fetch(url);
  const dec = new ImageDecoder({ data: await res.arrayBuffer(), type: 'image/gif' });
  await dec.tracks.ready; await dec.completed;
  const frames = dec.tracks.selectedTrack.frameCount, max = halfArm(name);
  const grab = async (i) => {
    const im = (await dec.decode({ frameIndex: i })).image, w = im.displayWidth, h = im.displayHeight;
    const cv = Object.assign(document.createElement('canvas'), { width: w, height: h }), g = cv.getContext('2d', { willReadFrequently: true });
    g.drawImage(im, 0, 0); im.close();
    return { data: g.getImageData(0, 0, w, h).data, w, h };
  };
  const A = await grab(0), B = await grab(2);
  dec.close();
  const rs = [0.25, 0.4, 0.55].map((f) => Math.round((A.h / 2) * f));
  const turn = rs.map((r) => deg(shift(ring(A.data, A.w, A.h, r), ring(B.data, B.w, B.h, r), max)));
  const rim = rs.map((r) => deg(shift(ring(A.data, A.w, A.h, r), ring(A.data, A.w, A.h, r + 3), max)));
  const expectDeg = (2 / frames) * spanRad(LOOM_PRESETS[name].layer) * (180 / Math.PI);
  const layer2 = measureLayer2(name, A.data, B.data, A.w, A.h, rs, 3);
  return { name, frames, size: A.w + 'x' + A.h, radii: rs, expectDeg, turn, rim, layer2, ok: turn.every((v) => v > 0) && rim.every((v) => v > 0) && (!layer2 || layer2.ok) };
}
