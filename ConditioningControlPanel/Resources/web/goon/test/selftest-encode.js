// Lane B — the page-side gif/awebp -> MP4 encoder, under node.
//
//   node Resources/web/goon/test/selftest-encode.js
//
// What it proves:
//   1. THE IMPORT SWEEP. encode/gifDecode.js, encode/encodeWorker.js and the
//      edited ui/assetsStore.js all import clean with no browser globals in
//      sight — an import throw inside WebView2 is a SILENT INFINITE LOADER, and
//      lane B is imported from the assets screen's own module graph;
//   2. the vendored muxer is really there, at the path encodeWorker.js asks for,
//      and asks for it RELATIVELY (the harnesses serve goon/ as the root);
//   3. the codec string is chosen BY RESOLUTION — the spike trap: Baseline
//      level 3.0 (42E01E) refuses 720p, so a 1280x720 encode must ask 3.1;
//   4. the frame maths: the 900-frame ceiling's uniform stride, the fps thinning
//      that gives a dropped frame's time to its predecessor, the preview slice;
//   5. the GIF 0-delay convention (0/absent/<20ms all mean 100 ms), because a
//      filmstrip that believes the 0 muxes a zero-length mp4;
//   6. the cache-put chunking: ≤4 MB of base64 per part, gapless seq from 0, and
//      an over-16-part artifact that FAILS BEFORE A SINGLE FRAME GOES OUT (the
//      host refuses part 17 and the job would hang until its stale timer);
//   7. the driver end to end against a fake bridge + fake worker: progress is
//      forwarded as cache-req {op:'encode-progress'}, the parts go out in order,
//      encode-done carries the part count and the real dimensions, and every
//      failure path closes the host's dispatched slot instead of abandoning it;
//   8. the driver adds NO bridge.on() calls — assetsStore.js still owns exactly
//      the five it always did (selftest-assets scans for duplicates and would
//      otherwise go red).
//
// Nothing here encodes a real frame: VideoEncoder/ImageDecoder/createImageBitmap
// are stubbed, and every unit under test is a pure function on purpose.

import * as fs from 'node:fs';
import * as path from 'node:path';
import { fileURLToPath } from 'node:url';

let failures = 0;
let n = 0;
function ok(cond, label, extra = '') {
  n++;
  if (!cond) { failures++; console.error(`  FAIL ${label} ${extra}`); }
}
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
const here = path.dirname(fileURLToPath(import.meta.url));
const read = (rel) => fs.readFileSync(path.join(here, rel), 'utf8');

// ------------------------------------------------------------ 1. the sweep

const decodeMod = await import('../encode/gifDecode.js');
const workerMod = await import('../encode/encodeWorker.js');
const storeMod = await import('../ui/assetsStore.js');

const {
  MAX_FRAMES, FALLBACK_FRAME_US, MIN_FRAME_US,
  mimeForKind, normalizeDurUs, strideFor, pickFrameIndices, fitBox, fitHeight, fpsOf,
  decodeFilmstrip, closeFilmstrip,
} = decodeMod;

const {
  CODEC_BASELINE_30, CODEC_BASELINE_31, CODEC_MAIN_31,
  MAX_ART_BITRATE, MIN_ART_BITRATE, PREVIEW_MAX_FPS, GOP_SECONDS,
  codecForSize, clampArtBitrate, gopFor, chooseFps, selectByFps, planPreviewFrames,
  stagePct, hasVideoEncoder, encodeSelection, runEncodeJob, handleWorkerMessage,
} = workerMod;

const {
  PUT_MAX_B64_CHARS, PUT_MAX_PARTS, PUT_PART_BYTES, ENCODE_HEARTBEAT_MS, WANT_PREVIEW,
  planPutParts, bytesToB64, createEncodeDriver,
} = storeMod;

ok(typeof decodeFilmstrip === 'function', 'gifDecode.js exports decodeFilmstrip()');
ok(typeof runEncodeJob === 'function', 'encodeWorker.js exports runEncodeJob()');
ok(typeof createEncodeDriver === 'function', 'assetsStore.js exports createEncodeDriver()');
ok(typeof storeMod.createAssetsStore === 'function', 'and still exports createAssetsStore()');
// The worker file must not have wired itself to a message port under node.
ok(typeof globalThis.onmessage !== 'function', 'importing encodeWorker.js under node wires no onmessage');

// -------------------------------------------------- 2. the vendored muxer

{
  const src = read('../encode/encodeWorker.js');
  const m = src.match(/from\s+'(\.\.\/vendor\/mp4-muxer\/[^']+)'/);
  ok(!!m, 'encodeWorker.js imports the muxer by a RELATIVE specifier (an absolute one 404s in the harness)');
  ok(!/from\s+'\/vendor\//.test(src), 'and never by an absolute /vendor/ path');
  ok(!/from\s+'\.\.\/\.\.\/vendor\//.test(src),
    'nor from the shared Resources/web/vendor — the muxer is vendored INSIDE goon/ on purpose');
  if (m) {
    const target = path.resolve(here, '..', 'encode', m[1]);
    ok(fs.existsSync(target), 'and the file it names really exists', target);
    const bytes = fs.statSync(target).size;
    ok(bytes > 20000, 'and is the real bundle, not a stub', String(bytes));
  }
  const muxer = await import('../vendor/mp4-muxer/mp4-muxer.mjs');
  ok(typeof muxer.Muxer === 'function' && typeof muxer.ArrayBufferTarget === 'function',
    'the muxer exports Muxer + ArrayBufferTarget');
  ok(fs.existsSync(path.resolve(here, '../vendor/mp4-muxer/LICENSE')), 'and ships its MIT LICENSE');
}

// --------------------------------------------- 3. the codec, by resolution

{
  ok(codecForSize(640, 360) === CODEC_BASELINE_30, '360p asks with Baseline 3.0', codecForSize(640, 360));
  ok(codecForSize(854, 480) === CODEC_BASELINE_30, '480p asks with Baseline 3.0', codecForSize(854, 480));
  ok(codecForSize(640, 480) === CODEC_BASELINE_30, '4:3 480p too', codecForSize(640, 480));
  // THE SPIKE TRAP: 42E01E is level 3.0, whose macroblock budget stops at
  // 720x480. A 720p configure() against it comes back unsupported.
  ok(codecForSize(1280, 720) === CODEC_BASELINE_31, '720p asks with Baseline 3.1, NOT 3.0', codecForSize(1280, 720));
  ok(codecForSize(720, 720) === CODEC_BASELINE_31, 'a 720-square gif (2,025 MBs) is a 3.1 encode too',
    codecForSize(720, 720));
  // The rule is the macroblock budget, not the height: a PORTRAIT 406x720 is
  // 1,170 macroblocks and level 3.0 takes it happily.
  ok(codecForSize(406, 720) === CODEC_BASELINE_30, 'a portrait 406x720 is judged on macroblocks, not on "720"',
    codecForSize(406, 720));
  ok(workerMod.macroblocksFor(1280, 720) === 3600, '1280x720 is exactly level 3.1\'s MaxFS',
    String(workerMod.macroblocksFor(1280, 720)));
  ok(workerMod.macroblocksFor(854, 480) === 1620, 'and 854x480 exactly level 3.0\'s',
    String(workerMod.macroblocksFor(854, 480)));
  ok(codecForSize(1920, 1080) === CODEC_MAIN_31, 'past 720p the guard string takes over', codecForSize(1920, 1080));
  ok(CODEC_BASELINE_30 === 'avc1.42E01E' && CODEC_BASELINE_31 === 'avc1.42E01F',
    'the two Baseline strings are exactly what probeEncode() asks with');
  // The probe in assetsStore.js and this table have to agree, or the host is
  // told "yes I can encode" by one rule and refused by the other.
  const probeSrc = read('../ui/assetsStore.js');
  ok(probeSrc.indexOf(CODEC_BASELINE_31) > 0, 'and probeEncode() names the 3.1 string');
}

// ------------------------------------------------------ 3b. the rate rails

{
  ok(clampArtBitrate(1800000) === MAX_ART_BITRATE, 'the host default rides through unchanged');
  ok(clampArtBitrate(9000000) === MAX_ART_BITRATE, 'anything greedier is clamped to 1.8 Mbps',
    String(clampArtBitrate(9000000)));
  ok(clampArtBitrate(1000) === MIN_ART_BITRATE, 'and a nonsense request floors out', String(clampArtBitrate(1000)));
  ok(clampArtBitrate(undefined) === MAX_ART_BITRATE, 'a missing bitrate is the default, not zero');
  ok(clampArtBitrate('nope') === MAX_ART_BITRATE, 'and so is junk');

  ok(chooseFps(60, 30) === 30, 'a 60 fps source is capped at the host ceiling', String(chooseFps(60, 30)));
  ok(chooseFps(12, 30) === 12, 'a slow source keeps its own rate', String(chooseFps(12, 30)));
  ok(chooseFps(0, 30) === 1, 'and a degenerate one never becomes 0 fps', String(chooseFps(0, 30)));

  ok(gopFor(30) === 60, 'a keyframe every 2 s at 30 fps', String(gopFor(30)));
  ok(gopFor(10) === 20, 'and at 10 fps', String(gopFor(10)));
  ok(gopFor(0.2) >= 1, 'never a gop of 0 — that would mean "all delta, no seek point"', String(gopFor(0.2)));
  ok(GOP_SECONDS === 2, 'the cadence is the documented 2 s');

  ok(stagePct('decode', 0) === 0 && stagePct('decode', 1) === 40, 'decode owns 0-40%');
  ok(stagePct('art', 0) === 40 && stagePct('art', 1) === 90, 'the artifact owns 40-90%');
  ok(stagePct('prev', 1) === 100, 'and the preview finishes the bar');
  ok(stagePct('art', 5) === 90 && stagePct('art', -3) === 40, 'out-of-range fractions are clamped, not wrapped');
}

// --------------------------------------------- 4. the stride + fps thinning

{
  ok(MAX_FRAMES === 900, 'the frame ceiling is 30 s at 30 fps', String(MAX_FRAMES));
  ok(strideFor(120) === 1, 'a short gif is never subsampled', String(strideFor(120)));
  ok(strideFor(900) === 1, 'and neither is one exactly at the cap', String(strideFor(900)));
  ok(strideFor(901) === 2, 'one frame over the cap halves it', String(strideFor(901)));
  // Ceiling division, the AnimatedWebp FramePlan shape: 2000/900 -> 3, never 2
  // (which would leave 1000 frames, still over).
  ok(strideFor(2000) === 3, '2,000 frames take a stride of 3', String(strideFor(2000)));
  ok(pickFrameIndices(2000).length <= MAX_FRAMES, 'and the pick really lands under the cap',
    String(pickFrameIndices(2000).length));
  ok(pickFrameIndices(2000).length === 667, 'at exactly 667 frames', String(pickFrameIndices(2000).length));
  const idx = pickFrameIndices(10, 4);
  ok(idx[0] === 0, 'the pick always starts at frame 0');
  ok(idx.join(',') === '0,3,6,9', 'and steps uniformly', idx.join(','));
  ok(idx.length <= 4, 'inside the requested ceiling', String(idx.length));
  ok(strideFor(0) === 1 && pickFrameIndices(0).length === 0, 'an empty track is not a divide by zero');

  // fps thinning: 20 frames of 50 ms (20 fps) capped at 10 fps keeps every other
  // one, and the dropped frame's time goes to the frame BEFORE it.
  const strip = [];
  for (let i = 0; i < 20; i++) strip.push({ tsUs: i * 50000, durUs: 50000 });
  const thinned = selectByFps(strip, 10);
  ok(thinned.length === 10, '20 fps thinned to 10 fps keeps half the frames', String(thinned.length));
  ok(thinned.every((f, k) => f.i === k * 2), 'every other frame, in order');
  ok(thinned.every((f) => f.durUs === 100000), 'and each kept frame inherits its neighbour\'s time',
    thinned.map((f) => f.durUs).join(','));
  const totalUs = thinned.reduce((a, f) => a + f.durUs, 0);
  ok(totalUs === 1000000, 'so the thinned strip runs for the SOURCE\'s wall-clock length', String(totalUs));

  ok(selectByFps(strip, 30).length === 20, 'a cap above the source rate keeps everything',
    String(selectByFps(strip, 30).length));
  ok(selectByFps([], 30).length === 0, 'an empty strip thins to nothing, not a throw');
  const one = selectByFps([{ tsUs: 0, durUs: 100000 }], 15);
  ok(one.length === 1 && one[0].durUs === 100000, 'a single-frame strip survives intact');

  // The exact-boundary case the 0.999 slack exists for: 100 ms frames at a 10
  // fps cap must keep EVERY frame, not every other one.
  const exact = [];
  for (let i = 0; i < 6; i++) exact.push({ tsUs: i * 100000, durUs: 100000 });
  ok(selectByFps(exact, 10).length === 6, '10 fps content against a 10 fps cap loses nothing',
    String(selectByFps(exact, 10).length));
}

// --------------------------------------------------- 4b. the preview slice

{
  const strip = [];
  for (let i = 0; i < 100; i++) strip.push({ tsUs: i * 100000, durUs: 100000 });   // 10 s at 10 fps
  const prev = planPreviewFrames(strip, 2000, PREVIEW_MAX_FPS);
  ok(prev.length > 0, 'the preview takes a real slice');
  ok(prev[0].tsUs === 0, 'starting at the head of the strip (no second decode)');
  const end = prev[prev.length - 1].tsUs + prev[prev.length - 1].durUs;
  ok(end <= 2000000, 'and stopping at prevMs, not at whatever the last frame\'s delay was', String(end));
  ok(PREVIEW_MAX_FPS === 15, 'the preview never runs above 15 fps', String(PREVIEW_MAX_FPS));
  const fast = [];
  for (let i = 0; i < 120; i++) fast.push({ tsUs: i * 16666, durUs: 16666 });      // 2 s at 60 fps
  const prevFast = planPreviewFrames(fast, 2000, PREVIEW_MAX_FPS);
  ok(prevFast.length <= 31, 'a 60 fps source is thinned into the preview budget', String(prevFast.length));
  ok(planPreviewFrames([], 2000).length === 0, 'an empty strip has no preview and no throw');
}

// ------------------------------------------- 5. the 0-delay GIF convention

{
  ok(normalizeDurUs(0) === FALLBACK_FRAME_US, 'a 0 delay means 100 ms', String(normalizeDurUs(0)));
  ok(normalizeDurUs(null) === FALLBACK_FRAME_US, 'and so does an absent one');
  ok(normalizeDurUs(undefined) === FALLBACK_FRAME_US, 'and an undefined one');
  ok(normalizeDurUs(NaN) === FALLBACK_FRAME_US, 'and a NaN');
  ok(normalizeDurUs(-5) === FALLBACK_FRAME_US, 'and a negative one');
  // The <20 ms rule: 25 years of renderers read "faster than 50 fps" as "the
  // author meant 0", and a 400-frame gif at 10 ms would otherwise mux 4 s long.
  ok(normalizeDurUs(10000) === FALLBACK_FRAME_US, 'a 10 ms delay is the 0 convention too',
    String(normalizeDurUs(10000)));
  ok(normalizeDurUs(MIN_FRAME_US) === MIN_FRAME_US, 'exactly 20 ms is believed', String(normalizeDurUs(MIN_FRAME_US)));
  ok(normalizeDurUs(50000) === 50000, 'and an ordinary 50 ms delay rides through');
  ok(normalizeDurUs(1e12) <= 10000000, 'a corrupt multi-hour delay is capped', String(normalizeDurUs(1e12)));

  ok(mimeForKind('gif') === 'image/gif', 'kind "gif" -> image/gif');
  ok(mimeForKind('awebp') === 'image/webp', 'kind "awebp" -> image/webp (the host\'s word, not the mime)');
  ok(mimeForKind('') === 'image/gif', 'and an unknown kind guesses gif rather than throwing');
}

// ------------------------------------------------- 5b. even-dimension fits

{
  // maxBox is a SQUARE the frame must fit inside — the LONG edge is the one
  // that lands on 720, which is what keeps a 4000px-wide banner gif honest.
  const a = fitBox(1600, 900, 720);
  ok(a.w === 720 && a.h === 404, '1600x900 fits a 720 box as 720x404 (long edge wins)', a.w + 'x' + a.h);
  ok(Math.max(a.w, a.h) === 720, 'the long edge is exactly the box');
  const b = fitBox(1001, 999, 720);
  ok(b.w % 2 === 0 && b.h % 2 === 0, 'odd sources round to EVEN dims — 4:2:0 chroma has no odd row',
    b.w + 'x' + b.h);
  ok(fitBox(100, 100, 720).w === 100, 'a small source is never upscaled', String(fitBox(100, 100, 720).w));
  ok(fitBox(1, 1, 720).w >= 2, 'and a 1px source still makes a legal encode', String(fitBox(1, 1, 720).w));
  const p = fitHeight(1280, 720, 240);
  ok(p.h === 240 && p.w === 426, 'the preview fits to HEIGHT: 1280x720 -> 426x240', p.w + 'x' + p.h);
  ok(p.w % 2 === 0 && p.h % 2 === 0, 'evenly');
  ok(fpsOf(30, 1000) === 30, 'fpsOf counts frames per second', String(fpsOf(30, 1000)));
  ok(fpsOf(1, 0) === 1, 'and never divides by zero', String(fpsOf(1, 0)));
}

// ------------------------------------------ 6. the cache-put chunk planner

{
  ok(PUT_MAX_B64_CHARS === 4 * 1024 * 1024, 'the b64 ceiling matches GoonCacheBridge.MaxPutB64Chars');
  ok(PUT_MAX_PARTS === 16, 'and the part ceiling matches MaxPutParts');
  ok(PUT_PART_BYTES === 3145728, 'so a part carries 3 MB of BYTES (4 b64 chars per 3 bytes)',
    String(PUT_PART_BYTES));
  ok(Math.ceil(PUT_PART_BYTES / 3) * 4 <= PUT_MAX_B64_CHARS, 'and its base64 can never overflow the ceiling');

  const small = planPutParts(1000);
  ok(small.ok && small.parts.length === 1, 'a small artifact is one part', String(small.parts.length));
  ok(small.parts[0].seq === 0 && small.parts[0].start === 0 && small.parts[0].end === 1000, 'covering all of it');

  const three = planPutParts(PUT_PART_BYTES * 2 + 5);
  ok(three.ok && three.parts.length === 3, 'a 6 MB+ artifact takes three parts', String(three.parts.length));
  ok(three.parts.map((p) => p.seq).join(',') === '0,1,2', 'seq is gapless and starts at 0',
    three.parts.map((p) => p.seq).join(','));
  ok(three.parts.every((p, i) => i === 0 || p.start === three.parts[i - 1].end),
    'and the ranges tile the artifact with no gap and no overlap');
  ok(three.parts[three.parts.length - 1].end === PUT_PART_BYTES * 2 + 5, 'ending exactly at the last byte');
  ok(three.parts.every((p) => p.end - p.start <= PUT_PART_BYTES), 'no part exceeds the byte budget');

  const edge = planPutParts(PUT_PART_BYTES * PUT_MAX_PARTS);
  ok(edge.ok && edge.parts.length === PUT_MAX_PARTS, 'exactly 16 parts is still legal', String(edge.parts.length));
  const over = planPutParts(PUT_PART_BYTES * PUT_MAX_PARTS + 1);
  ok(!over.ok && over.reason === 'too-big', 'one byte more is refused HERE, not by the host',
    over.reason);
  ok(over.parts.length === 0, 'and no parts are handed back to send');
  ok(!planPutParts(0).ok, 'an empty artifact is a failure, not a zero-part upload');

  // 48 MiB of headroom is double the 24 MiB wire cap — a lane-B artifact can
  // never legitimately hit the ceiling.
  ok(PUT_PART_BYTES * PUT_MAX_PARTS > 24 * 1024 * 1024,
    'the 16-part ceiling sits well past the 24 MiB transfer cap',
    String(PUT_PART_BYTES * PUT_MAX_PARTS));

  const bytes = new Uint8Array([0, 1, 2, 250, 251, 252, 253]);
  const b64 = bytesToB64(bytes);
  ok(typeof b64 === 'string' && b64.length > 0, 'bytesToB64 answers a string');
  ok(Buffer.from(b64, 'base64').equals(Buffer.from(bytes)), 'that round-trips byte for byte', b64);
  ok(bytesToB64(new Uint8Array(0)) === '', 'and an empty range is an empty string');
}

// ---------------------------------------------- 7. decodeFilmstrip, stubbed

{
  const FRAMES = [
    { durUs: 0 },        // the 0-delay convention
    { durUs: 50000 },
    { durUs: 100000 },
    { durUs: 40000 },
  ];
  const decoded = [];
  const madeBitmaps = [];
  let decoderClosed = false;

  globalThis.ImageDecoder = class {
    constructor({ data, type }) {
      this.data = data; this.type = type;
      this.tracks = { ready: Promise.resolve(), selectedTrack: { frameCount: FRAMES.length } };
      this.completed = Promise.resolve();
    }
    async decode({ frameIndex }) {
      const f = FRAMES[frameIndex];
      if (!f) throw new Error('frame out of range');
      decoded.push(frameIndex);
      return {
        image: {
          displayWidth: 1600, displayHeight: 900, duration: f.durUs,
          closed: false, close() { this.closed = true; },
        },
      };
    }
    close() { decoderClosed = true; }
  };
  globalThis.createImageBitmap = async (_src, o) => ({
    width: o.resizeWidth, height: o.resizeHeight, closed: false, close() { this.closed = true; },
  });

  const strip = await decodeFilmstrip(new ArrayBuffer(64), 'image/gif', { maxBox: 720 });
  ok(strip.frames.length === 4, 'every frame of the track is decoded', String(strip.frames.length));
  ok(decoded.join(',') === '0,1,2,3', 'in order, once each', decoded.join(','));
  ok(strip.w === 720 && strip.h === 404, '1600x900 came back as a 720-box fit', strip.w + 'x' + strip.h);
  ok(strip.frames.every((f) => f.bitmap.width === 720 && f.bitmap.height === 404),
    'and the bitmaps really are the resized ones, at even dims');
  ok(strip.frames[0].durUs === FALLBACK_FRAME_US, 'the 0-delay frame is 100 ms', String(strip.frames[0].durUs));
  ok(strip.frames[1].durUs === 50000, 'the 50 ms one is believed');
  ok(strip.frames.map((f) => f.tsUs).join(',') === '0,100000,150000,250000',
    'timestamps accumulate from the durations', strip.frames.map((f) => f.tsUs).join(','));
  ok(strip.durMs === 290, 'and the strip runs 290 ms', String(strip.durMs));
  ok(strip.stride === 1 && strip.srcFrames === 4, 'a 4-frame gif is not subsampled');
  ok(decoderClosed === true, 'the ImageDecoder is CLOSED — one live decoder per gif blew the browser cap once');

  closeFilmstrip(strip);
  ok(strip.frames.every((f) => f.bitmap.closed), 'closeFilmstrip() releases every bitmap (they are GPU memory)');
  closeFilmstrip(strip);
  ok(true, 'and is safe to call twice');

  // A decoder that lies about frameCount stops at the truth instead of failing.
  decoded.length = 0;
  globalThis.ImageDecoder = class {
    constructor() {
      this.tracks = { ready: Promise.resolve(), selectedTrack: { frameCount: 50 } };
      this.completed = Promise.resolve();
    }
    async decode({ frameIndex }) {
      if (frameIndex >= 3) throw new Error('truncated');
      return { image: { displayWidth: 100, displayHeight: 100, duration: 100000, close() {} } };
    }
    close() {}
  };
  const partial = await decodeFilmstrip(new ArrayBuffer(8), 'image/gif', { maxBox: 720 });
  ok(partial.frames.length === 3, 'a truncated gif yields the frames that DID decode', String(partial.frames.length));

  // Nothing at all decodes -> a throw, never a zero-frame "success".
  globalThis.ImageDecoder = class {
    constructor() { this.tracks = { ready: Promise.resolve(), selectedTrack: { frameCount: 4 } }; this.completed = Promise.resolve(); }
    async decode() { throw new Error('broken'); }
    close() {}
  };
  let threw = '';
  try { await decodeFilmstrip(new ArrayBuffer(8), 'image/gif', {}); } catch (e) { threw = e.message; }
  ok(threw === 'broken', 'a gif that decodes nothing throws rather than returning an empty strip', threw);

  delete globalThis.ImageDecoder;
  delete globalThis.createImageBitmap;
  let noDec = '';
  try { await decodeFilmstrip(new ArrayBuffer(8), 'image/gif', {}); } catch (e) { noDec = e.message; }
  ok(noDec === 'no-image-decoder', 'and a runtime with no ImageDecoder says so by name', noDec);
}

// -------------------------------------------- 7b. the encoder refuses early

{
  ok(hasVideoEncoder() === false, 'node has no VideoEncoder, and the module says so honestly');
  let why = '';
  try {
    await encodeSelection([], [{ i: 0, tsUs: 0, durUs: 1000 }], { w: 320, h: 240, bitrate: 900000, fps: 10 });
  } catch (e) { why = e.message; }
  ok(why === 'unsupported', 'encodeSelection refuses with "unsupported" — the driver\'s signal to fall back', why);
  let why2 = '';
  try { await runEncodeJob(new ArrayBuffer(8), 'image/gif', {}); } catch (e) { why2 = e.message; }
  ok(why2 === 'unsupported', 'and so does a whole job, before it fetches a decoder', why2);

  // The worker's message handler must translate that into a fail frame, never a
  // rejected promise the driver can never see.
  const posted = [];
  await handleWorkerMessage({ kind: 'encode', jobId: 'w1', buf: new ArrayBuffer(8), mime: 'gif', cfg: {} },
    (m) => posted.push(m));
  const fail = posted.find((m) => m.kind === 'fail');
  ok(!!fail && fail.jobId === 'w1', 'handleWorkerMessage answers a fail frame for the right job');
  ok(fail && fail.reason === 'unsupported', 'carrying the reason verbatim', fail && fail.reason);
  posted.length = 0;
  await handleWorkerMessage({ kind: 'nonsense' }, (m) => posted.push(m));
  ok(posted.length === 0, 'and ignores a frame it does not know');
}

// ------------------------------------------------------ 8. the driver e2e

function fakeBridge() {
  const sent = [];
  return {
    isHosted: true,
    _sent: sent,
    on() { throw new Error('the driver must never register a bridge handler'); },
    off() { throw new Error('the driver must never unregister a bridge handler'); },
    send(m) { sent.push(m); },
    puts() { return sent.filter((m) => m.type === 'cache-put'); },
    progress() { return sent.filter((m) => m.type === 'cache-req' && m.op === 'encode-progress'); },
    done() { return sent.filter((m) => m.type === 'encode-done'); },
  };
}

function fakeWorker() {
  const w = {
    posted: [], terminated: false, onmessage: null, onerror: null,
    postMessage(m, transfer) { w.posted.push({ m, transfer }); },
    terminate() { w.terminated = true; },
    emit(m) { if (w.onmessage) w.onmessage({ data: m }); },
  };
  return w;
}

/** A believable mp4: `ftyp` at offset 4, exactly what the host magic-checks. */
function fakeMp4(bytes) {
  const u8 = new Uint8Array(bytes);
  u8[3] = 0x18;
  u8[4] = 0x66; u8[5] = 0x74; u8[6] = 0x79; u8[7] = 0x70;   // 'ftyp'
  u8[8] = 0x69; u8[9] = 0x73; u8[10] = 0x6f; u8[11] = 0x6d; // 'isom'
  return u8.buffer;
}

const REQ = {
  type: 'encode-request', jobId: 'job-1', id: 'job-1',
  srcUrl: 'https://ccp.assets/loops/spin.gif', kind: 'gif',
  maxBox: 720, bitrate: 1800000, maxFps: 30,
  prevHeight: 240, prevMs: 2000, prevBitrate: 350000,
};

function driverRig(o = {}) {
  const bridge = fakeBridge();
  const worker = fakeWorker();
  const src = new ArrayBuffer(2048);
  const fetches = [];
  const fetchImpl = async (url) => {
    fetches.push(url);
    if (o.fetchFails) throw new Error('offline');
    return { ok: true, status: 200, arrayBuffer: async () => src };
  };
  const driver = createEncodeDriver({
    bridge, logger: null, caps: o.caps || { videoEncoder: true, gif: true },
    workerFactory: () => worker, fetchImpl,
  });
  return { bridge, worker, driver, fetches, src };
}

{
  // --- the happy path ------------------------------------------------------
  const rig = driverRig();
  rig.driver.handle(REQ);
  await sleep(10);

  ok(rig.fetches.length === 1 && rig.fetches[0] === REQ.srcUrl, 'the driver fetches the source itself',
    rig.fetches.join(','));
  ok(rig.worker.posted.length === 1, 'and posts exactly one encode job to the worker',
    String(rig.worker.posted.length));
  const post = rig.worker.posted[0];
  ok(post.m.kind === 'encode' && post.m.jobId === 'job-1', 'the job carries the host\'s jobId', post.m.jobId);
  ok(post.m.mime === 'image/gif', 'and the mime the host\'s `kind` meant', post.m.mime);
  ok(Array.isArray(post.transfer) && post.transfer[0] === rig.src,
    'the buffer is TRANSFERRED, not copied — a 20 MB gif must not exist twice');
  ok(post.m.cfg.maxBox === 720 && post.m.cfg.maxFps === 30, 'the host\'s targets ride through verbatim');
  ok(post.m.cfg.wantPrev === WANT_PREVIEW && WANT_PREVIEW === false,
    'and the preview is OFF: the shipped cache-put takes one blob per job, with no art/prv discriminator');
  ok(rig.driver.busy === true && rig.driver.jobId === 'job-1', 'the driver holds exactly one job');

  // progress is forwarded as the verb that resets the host's 3-minute stale timer
  rig.worker.emit({ kind: 'progress', jobId: 'job-1', pct: 37 });
  const prog = rig.bridge.progress();
  ok(prog.length >= 1, 'worker progress becomes a cache-req frame', String(prog.length));
  ok(prog[prog.length - 1].op === 'encode-progress' && prog[prog.length - 1].pct === 37,
    'op encode-progress, with the percentage', JSON.stringify(prog[prog.length - 1]));
  ok(prog[prog.length - 1].jobId === 'job-1', 'quoting the jobId the host dispatched');
  rig.worker.emit({ kind: 'progress', jobId: 'job-1', pct: 37 });
  ok(rig.bridge.progress().length === prog.length, 'and an unchanged percentage is not re-sent per frame');

  // done -> parts -> encode-done
  const art = fakeMp4(PUT_PART_BYTES + 1000);
  rig.worker.emit({ kind: 'done', jobId: 'job-1', ok: true, art, prv: null, w: 1280, h: 720, durMs: 2500 });
  const puts = rig.bridge.puts();
  ok(puts.length === 2, 'a 3 MB+ artifact goes out as two cache-put parts', String(puts.length));
  ok(puts.map((p) => p.seq).join(',') === '0,1', 'seq gapless from 0', puts.map((p) => p.seq).join(','));
  ok(puts.every((p) => p.jobId === 'job-1'), 'every part quotes the jobId');
  ok(puts.every((p) => typeof p.b64 === 'string' && p.b64.length <= PUT_MAX_B64_CHARS),
    'and none breaks the 4 MB base64 ceiling',
    puts.map((p) => p.b64.length).join(','));
  ok(rig.bridge.done().length === 0, 'no encode-done until the parts are acknowledged');

  rig.driver.handle({ type: 'cache-put-result', jobId: 'job-1', seq: 0, ok: true });
  ok(rig.bridge.done().length === 0, 'still none after the FIRST part');
  rig.driver.handle({ type: 'cache-put-result', jobId: 'job-1', seq: 1, ok: true });
  const done = rig.bridge.done();
  ok(done.length === 1, 'the last ack commits the job', String(done.length));
  ok(done[0].ok === true && done[0].parts === 2, 'encode-done claims the part count the host counted',
    JSON.stringify(done[0]));
  ok(done[0].ext === 'mp4', 'and an mp4 extension — the only thing the magic check will accept', done[0].ext);
  ok(done[0].w === 1280 && done[0].h === 720 && done[0].durMs === 2500,
    'with the real dimensions and duration', JSON.stringify(done[0]));

  rig.driver.handle({ type: 'cache-put-result', jobId: 'job-1', seq: -1, ok: true, done: true });
  ok(rig.driver.busy === false, 'the commit reply (seq -1) releases the driver for the next job');
  rig.driver.dispose();
  ok(rig.worker.terminated === true, 'dispose() terminates the worker');
}

{
  // --- an artifact too big to upload FAILS BEFORE IT SENDS ANYTHING --------
  const rig = driverRig();
  rig.driver.handle(REQ);
  await sleep(10);
  const huge = fakeMp4(PUT_PART_BYTES * PUT_MAX_PARTS + 1);
  rig.worker.emit({ kind: 'done', jobId: 'job-1', ok: true, art: huge, w: 1280, h: 720, durMs: 9000 });
  ok(rig.bridge.puts().length === 0, 'not one cache-put frame goes out', String(rig.bridge.puts().length));
  const d = rig.bridge.done();
  ok(d.length === 1 && d[0].ok === false, 'the job is closed as a failure');
  ok(d[0].fail === 'too-big', 'naming the reason', d[0].fail);
  ok(rig.driver.busy === false, 'and the driver frees itself — a hung job is requeued and encoded twice');
  rig.driver.dispose();
}

{
  // --- output that is not an mp4 is caught here, not by the host -----------
  const rig = driverRig();
  rig.driver.handle(REQ);
  await sleep(10);
  const notMp4 = new Uint8Array(4096);
  notMp4[0] = 0x47; notMp4[1] = 0x49; notMp4[2] = 0x46;   // "GIF"
  rig.worker.emit({ kind: 'done', jobId: 'job-1', ok: true, art: notMp4.buffer, w: 100, h: 100, durMs: 100 });
  ok(rig.bridge.puts().length === 0, 'a non-ISO-BMFF blob is never uploaded');
  ok(rig.bridge.done()[0].fail === 'not-mp4', 'and the failure names the real problem',
    rig.bridge.done()[0].fail);
  rig.driver.dispose();
}

{
  // --- a refused part closes the job instead of stranding it --------------
  const rig = driverRig();
  rig.driver.handle(REQ);
  await sleep(10);
  rig.worker.emit({ kind: 'done', jobId: 'job-1', ok: true, art: fakeMp4(2048), w: 320, h: 240, durMs: 400 });
  ok(rig.bridge.puts().length === 1, 'a small artifact is a single part');
  rig.driver.handle({ type: 'cache-put-result', jobId: 'job-1', seq: 0, ok: false, error: 'bad-seq' });
  const d = rig.bridge.done();
  ok(d.length === 1 && d[0].ok === false && d[0].fail === 'bad-seq',
    'a refused part becomes an honest encode-done failure', JSON.stringify(d[0]));
  ok(rig.driver.busy === false, 'and releases the slot');
  rig.driver.dispose();
}

{
  // --- the encoder says "unsupported" and there is no recorder either -----
  const rig = driverRig();
  rig.driver.handle(REQ);
  await sleep(10);
  rig.worker.emit({ kind: 'fail', jobId: 'job-1', reason: 'unsupported' });
  const d = rig.bridge.done();
  ok(d.length === 1 && d[0].ok === false, 'the job is closed');
  ok(d[0].fail === 'no-encoder', 'with the reason the host can log: no-encoder', d[0].fail);
  rig.driver.dispose();
}

{
  // --- any other worker failure rides through verbatim --------------------
  const rig = driverRig();
  rig.driver.handle(REQ);
  await sleep(10);
  rig.worker.emit({ kind: 'fail', jobId: 'job-1', reason: 'no-frames' });
  ok(rig.bridge.done()[0].fail === 'no-frames', 'the worker\'s reason reaches the host',
    rig.bridge.done()[0].fail);
  rig.driver.dispose();
}

{
  // --- a source that will not fetch ---------------------------------------
  const rig = driverRig({ fetchFails: true });
  rig.driver.handle(REQ);
  await sleep(10);
  ok(rig.worker.posted.length === 0, 'nothing is handed to the worker');
  const d = rig.bridge.done();
  ok(d.length === 1 && d[0].fail === 'fetch-failed', 'and the job fails as fetch-failed', JSON.stringify(d[0]));
  rig.driver.dispose();
}

{
  // --- serial dispatch: a new jobId means the host gave up on the old -----
  const rig = driverRig();
  rig.driver.handle(REQ);
  await sleep(10);
  rig.driver.handle(Object.assign({}, REQ, { jobId: 'job-2', id: 'job-2' }));
  await sleep(10);
  ok(rig.driver.jobId === 'job-2', 'the driver follows the host', String(rig.driver.jobId));
  // A late frame for the abandoned job must not resurrect it.
  rig.worker.emit({ kind: 'done', jobId: 'job-1', ok: true, art: fakeMp4(2048), w: 8, h: 8, durMs: 10 });
  ok(rig.bridge.puts().length === 0, 'and a late frame for the ABANDONED job is ignored',
    String(rig.bridge.puts().length));
  rig.driver.dispose();
}

{
  // --- frames for nobody -------------------------------------------------
  const rig = driverRig();
  rig.driver.handle({ type: 'cache-put-result', jobId: 'ghost', seq: 0, ok: true });
  rig.driver.handle({ type: 'something-else' });
  rig.driver.handle(null);
  ok(rig.bridge._sent.length === 0, 'an unsolicited result frame produces nothing at all',
    String(rig.bridge._sent.length));
  rig.driver.dispose();
}

// -------------------------------------- 9. no new bridge.on, and a heartbeat

{
  const storeSrc = read('../ui/assetsStore.js');
  const ons = Array.from(storeSrc.matchAll(/bridge\.on\(\s*'([^']+)'/g)).map((m) => m[1]);
  ok(ons.length === 5, 'assetsStore.js still registers exactly five bridge handlers', ons.join(','));
  ok(ons.join(',') === 'cache-state,cache-list,cache-progress,encode-request,cache-put-result',
    'the same five it always did — the driver rides the onEncodeRequest seam', ons.join(','));
  ok(/onEncodeRequest\(encoder\.handle\)/.test(storeSrc), 'and the driver is attached through that seam');
  ok(/createEncodeDriver\(\{ bridge, logger, caps \}\)/.test(storeSrc),
    'built with the probe result, so a runtime with no encoder never spawns a worker');

  // The worker URL has to resolve under BOTH document roots (the app serves
  // Resources/web, the headless harness serves goon/). Only a relative
  // new URL(..., import.meta.url) does that.
  ok(/new URL\('\.\.\/encode\/encodeWorker\.js', import\.meta\.url\)/.test(storeSrc),
    'the worker URL is relative to the module, not to the document root');
  ok(/\{ type: 'module' \}/.test(storeSrc), 'and it is constructed as a MODULE worker (it imports the muxer)');

  ok(ENCODE_HEARTBEAT_MS <= 30000, 'the progress heartbeat fires at least every 30 s', String(ENCODE_HEARTBEAT_MS));
  ok(ENCODE_HEARTBEAT_MS * 4 < 180000,
    'comfortably inside TransferCompressionService.PageJobStaleMs (3 min), which requeues a silent job',
    String(ENCODE_HEARTBEAT_MS));
  ok(/setInterval\(/.test(storeSrc), 'and there really is an interval keeping it alive');
}

// ------------------------------------- 10. the store still builds a driver

{
  // A runtime that CAN encode gets a driver on the seam automatically; node,
  // which cannot, must get none — and must not have paid for a worker to
  // find out.
  const handlers = new Map();
  const bridge = {
    isHosted: true,
    on(t, fn) { if (handlers.has(t)) throw new Error('dupe ' + t); handlers.set(t, fn); },
    off(t) { return handlers.delete(t); },
    send() {},
  };
  const store = storeMod.createAssetsStore({ bridge, session: { caps: {} }, logger: null });
  await sleep(20);
  ok(store.encoder === null, 'node probes as "no encoder" and the store builds none', String(store.encoder));
  store.dispose();

  globalThis.MediaRecorder = { isTypeSupported: (t) => t === 'video/mp4;codecs=avc1.42E01E' };
  const handlers2 = new Map();
  const bridge2 = {
    isHosted: true,
    on(t, fn) { if (handlers2.has(t)) throw new Error('dupe ' + t); handlers2.set(t, fn); },
    off(t) { return handlers2.delete(t); },
    send() {},
  };
  const store2 = storeMod.createAssetsStore({ bridge: bridge2, session: { caps: {} }, logger: null });
  await sleep(20);
  ok(store2.encoder !== null, 'a runtime with only the MediaRecorder fallback still gets a driver');
  ok(typeof store2.encoder.handle === 'function', 'and it is the seam handler shape');
  ok(handlers2.size === 5, 'built without adding a sixth bridge handler', String(handlers2.size));
  store2.dispose();
  delete globalThis.MediaRecorder;

  const handlers3 = new Map();
  const bridge3 = {
    isHosted: true,
    on(t, fn) { handlers3.set(t, fn); },
    off(t) { return handlers3.delete(t); },
    send() {},
  };
  const store3 = storeMod.createAssetsStore({ bridge: bridge3, session: { caps: {} }, logger: null, autoEncoder: false });
  await sleep(20);
  ok(store3.encoder === null, 'autoEncoder:false leaves the seam empty, exactly as before lane B');
  store3.dispose();
}

console.log(`\nselftest-encode: ${n - failures}/${n} checks passed`);
if (failures > 0) {
  console.error(`${failures} FAILURE(S)`);
  process.exit(1);
}
process.exit(0);
