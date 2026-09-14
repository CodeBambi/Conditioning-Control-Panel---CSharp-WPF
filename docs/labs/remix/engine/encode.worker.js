/* ============================================================================
 * encode.worker.js - gifenc, off the main thread.
 *
 * Frames are rendered on the main thread (the compositor needs a real canvas)
 * and handed over one at a time as RGBA buffers, transferred rather than
 * copied, so only one frame is in flight and a 120 frame export never holds
 * the whole loop in memory twice.
 *
 * Protocol (main -> worker):
 *   { id, cmd:'begin',  w, h, sample:ArrayBuffer }  sample is 8 frames of RGBA
 *                                                   concatenated, for the
 *                                                   global palette
 *   { id, cmd:'frame',  index, data:ArrayBuffer, delay }   delay in ms
 *   { id, cmd:'finish' }
 *   { id, cmd:'abort'  }
 * (worker -> main):
 *   { id, ready:true } | { id, wrote:index } | { id, bytes:ArrayBuffer }
 *   { id, error }
 * ==========================================================================*/

import { GIFEncoder, quantize, applyPalette } from '../vendor/gifenc.esm.js';

const jobs = new Map();

self.onmessage = (e) => {
  const m = e.data || {};
  try {
    if (m.cmd === 'begin') return begin(m);
    if (m.cmd === 'frame') return frame(m);
    if (m.cmd === 'finish') return finish(m);
    if (m.cmd === 'abort') { jobs.delete(m.id); return; }
  } catch (err) {
    jobs.delete(m.id);
    self.postMessage({ id: m.id, error: String((err && err.message) || err) });
  }
};

function begin({ id, w, h, sample }) {
  const palette = quantize(new Uint8Array(sample), 256, { format: 'rgb444' });
  jobs.set(id, { gif: GIFEncoder(), palette, w, h, first: true });
  self.postMessage({ id, ready: true });
}

function frame({ id, index, data, delay }) {
  const job = jobs.get(id);
  if (!job) return;
  const rgba = new Uint8Array(data);
  const indexed = applyPalette(rgba, job.palette, 'rgb444');
  job.gif.writeFrame(indexed, job.w, job.h, {
    palette: job.first ? job.palette : undefined,
    delay,
    repeat: 0,
    first: job.first,
  });
  job.first = false;
  self.postMessage({ id, wrote: index });
}

function finish({ id }) {
  const job = jobs.get(id);
  if (!job) return;
  job.gif.finish();
  const bytes = job.gif.bytes();
  jobs.delete(id);
  const buf = bytes.buffer.slice(bytes.byteOffset, bytes.byteOffset + bytes.byteLength);
  self.postMessage({ id, bytes: buf }, [buf]);
}
