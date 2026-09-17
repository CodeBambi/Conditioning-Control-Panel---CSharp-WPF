// Frame decoding for browsers without WebCodecs, including the HTTP phone preview.
// GIF uses the existing MIT omggif reader. WebP follows the RIFF ANMF container:
// https://developers.google.com/speed/webp/docs/riff_container
// Keep one compositing canvas and one frame bitmap, never an unbounded filmstrip.
import { MediaLimitError } from './media-limits.js';
import { GifReader } from '../../dtrh/vendor/omggif/omggif.module.js';
const text = (b, p, n = 4) => String.fromCharCode(...b.subarray(p, p + n));
const u24 = (b, p) => b[p] | b[p + 1] << 8 | b[p + 2] << 16;

export function webpFrames(data) {
  const b = new Uint8Array(data), v = new DataView(data), frames = [];
  if (text(b, 0) !== 'RIFF' || text(b, 8) !== 'WEBP') throw new Error('Not WebP');
  let width = 0, height = 0, background = 'rgba(0,0,0,0)';
  for (let p = 12; p + 8 <= b.length;) {
    const tag = text(b, p), size = v.getUint32(p + 4, true), start = p + 8, end = start + size;
    if (end > b.length) throw new Error('Truncated WebP');
    if (tag === 'VP8X' && size >= 10) { width = u24(b, start + 4) + 1; height = u24(b, start + 7) + 1; }
    if (tag === 'ANIM' && size >= 6) background = `rgba(${b[start + 2]},${b[start + 1]},${b[start]},${b[start + 3] / 255})`;
    if (tag === 'ANMF' && size >= 16) {
      const payload = b.slice(start + 16, end), file = new Uint8Array(payload.length + 30);
      file.set([82,73,70,70], 0); new DataView(file.buffer).setUint32(4, file.length - 8, true);
      file.set([87,69,66,80], 8);
      // Alpha subchunks require an extended header even in a single-frame WebP.
      file.set([86,80,56,88,10,0,0,0,16,0,0,0], 12);
      file.set(b.subarray(start + 6, start + 12), 24); file.set(payload, 30);
      frames.push({ x: u24(b,start)*2, y: u24(b,start+3)*2, width: u24(b,start+6)+1, height: u24(b,start+9)+1,
        delay: u24(b,start+12), disposal: b[start+15]&1, replace: !!(b[start+15]&2), file });
    }
    p = end + (size & 1);
  }
  return { width, height, background, frames };
}

export async function compatibilityDecoder(data, type) {
  if (data.byteLength > 32 * 1024 * 1024) throw new MediaLimitError('Image transfer exceeds media budget');
  let gif = null, frames, width, height, background = 'rgba(0,0,0,0)';
  if (type === 'image/gif') {
    gif = new GifReader(new Uint8Array(data)); width = gif.width; height = gif.height;
    frames = Array.from({ length: gif.numFrames() }, (_, i) => ({ ...gif.frameInfo(i), delay: gif.frameInfo(i).delay * 10 }));
  } else if (type === 'image/webp' && typeof createImageBitmap === 'function') {
    ({ width, height, background, frames } = webpFrames(data));
  } else return null;
  if (!frames.length || !width || !height || width * height > 4 * 1024 * 1024 || frames.length > 2000) throw new MediaLimitError('Animation exceeds media budget');
  if (frames.some(f => f.x + f.width > width || f.y + f.height > height)) throw new MediaLimitError('Animation frame exceeds image bounds');
  const canvas = document.createElement('canvas'); canvas.width = width; canvas.height = height;
  const g = canvas.getContext('2d', { willReadFrequently: !!gif });
  let last = -1, restore = null, closed = false;
  function clear(f) {
    g.clearRect(f.x, f.y, f.width, f.height);
    if (!gif) { g.fillStyle = background; g.fillRect(f.x, f.y, f.width, f.height); }
  }
  return {
    tracks: { ready: Promise.resolve(), selectedTrack: { frameCount: frames.length } }, completed: Promise.resolve(),
    async decode({ frameIndex: i }) {
      if (closed || (i !== 0 && i !== last + 1)) throw new Error('Closed or nonsequential image');
      const f = frames[i];
      if (i === 0) { clear({ x:0, y:0, width, height }); restore = null; }
      else {
        const prev = frames[last];
        if (prev.disposal === (gif ? 2 : 1)) clear(prev);
        if (gif && prev.disposal === 3 && restore) g.putImageData(restore, 0, 0);
      }
      restore = gif && f.disposal === 3 ? g.getImageData(0, 0, width, height) : null;
      if (gif) {
        const pixels = g.getImageData(0, 0, width, height);
        gif.decodeAndBlitFrameRGBA(i, pixels.data); g.putImageData(pixels, 0, 0);
      } else {
        const bitmap = await createImageBitmap(new Blob([f.file], { type:'image/webp' }));
        if (closed) { bitmap.close(); throw new Error('Closed image'); }
        if (f.replace) g.clearRect(f.x, f.y, f.width, f.height);
        g.drawImage(bitmap, f.x, f.y); bitmap.close();
      }
      last = i;
      // Match VideoFrame's interface used by decodedSource; canvas is the drawable.
      canvas.displayWidth = width; canvas.displayHeight = height;
      canvas.duration = (f.delay < 20 ? 100 : f.delay) * 1000; canvas.close = () => {};
      return { image: canvas };
    },
    close() { closed = true; restore = null; canvas.width = canvas.height = 1; },
  };
}
