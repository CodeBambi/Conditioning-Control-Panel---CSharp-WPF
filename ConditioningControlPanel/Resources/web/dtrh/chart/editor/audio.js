/* ============================================================================
 * editor/audio.js - the pure half: peaks and the file hash (chart/EDITOR.md, PR E1).
 *
 * CARVE-OUT: the canonical copy of this file lives on the Chart Room / Track
 * Maker chain and also carries install() (the <audio> element, the clock, the
 * decode). Only the functions that touch no DOM are here, so the web build can
 * import the waveform walk and CHART.md's hash from app main before the rest
 * lands. Keep these bodies byte for byte in step with the chain.
 *
 * The audio never leaves the machine. The hash is CHART.md's: SHA1 of the byte
 * length as 8 bytes little endian plus the first 1 MiB, so a chart can say which
 * file it was written against (tools/racechart/align.py computes the same).
 * ==========================================================================*/

const HASH_HEAD = 1024 * 1024;
const PEAKS_PER_SEC = 50;

/* ---- peaks (pure, chart/smoke/peaks-check.mjs runs these under node) ------ */

/** min/max of every sample in bins [b0, b1) into out, channels mixed down. */
export function peaksInto(out, chans, length, per, b0, b1) {
  const n = chans.length;
  for (let b = b0; b < b1; b++) {
    const s = b * per, e = Math.min(length, s + per);
    let lo = 0, hi = 0;
    if (e > s) {
      lo = 1; hi = -1;
      for (let i = s; i < e; i++) {
        let v = 0;
        for (let c = 0; c < n; c++) v += chans[c][i];
        v /= n;
        if (v < lo) lo = v;
        if (v > hi) hi = v;
      }
    }
    out[b * 2] = lo;
    out[b * 2 + 1] = hi;
  }
  return out;
}

export function binsPer(sampleRate, perSec) { return Math.max(1, Math.round(sampleRate / perSec)); }
export function binCount(length, per) { return Math.max(1, Math.ceil(length / per)); }

/** Float32Array of min,max pairs, perSec bins per second of audio. */
export function peaksFromChannels(chans, length, sampleRate, perSec = PEAKS_PER_SEC) {
  const per = binsPer(sampleRate, perSec), bins = binCount(length, per);
  return peaksInto(new Float32Array(bins * 2), chans, length, per, 0, bins);
}

/* ---- hash ---------------------------------------------------------------- */

/** CHART.md source.hash: SHA1 hex of the byte length (8 bytes LE) + the first 1 MiB. */
export async function hashFile(file) {
  const head = new Uint8Array(await file.slice(0, HASH_HEAD).arrayBuffer());
  const buf = new Uint8Array(8 + head.length);
  new DataView(buf.buffer).setBigUint64(0, BigInt(file.size), true);
  buf.set(head, 8);
  const sum = new Uint8Array(await crypto.subtle.digest('SHA-1', buf));
  return [...sum].map((b) => b.toString(16).padStart(2, '0')).join('');
}
