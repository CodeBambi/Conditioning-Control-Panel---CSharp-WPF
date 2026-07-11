/* ============================================================================
 * spawner.js - the recycling ring buffer of wall cards for the endless fall.
 *
 * Cards spawn ~120u ahead of the camera and despawn ~30u behind it, so only a
 * dozen or so ever exist no matter how long the fall runs or how big the drop
 * was. Content comes exclusively from the user media pool; we ship no default
 * images, so with nothing dropped the tunnel is nearly blank - only generated
 * subliminal word cards drift past (alongside the bubble field + veils).
 *
 * The proven video plumbing is copied from js/rabbit-hole/sectorBuilder.js:
 *   - ensureCtx / routeVideoGain / setVideoVolume (:1580-1613) - per-video gain
 *     nodes on one shared AudioContext (iOS ignores element volume)
 *   - manageVideo + hysteresis + kick backoff (:1669-1685) - decoder release
 *     when far, polite keep-alive when near
 *   - the cubed proximity fade (:3117-3126)
 *   - rounded mask / metal frame / soft glow canvas textures (:683-748, :1250)
 *
 * Memory contract: user media textures NEVER go through the assets.js cache
 * (it would pin blob URLs forever) - each card owns its texture and disposes
 * it on despawn, then releases its blob URL. Built-in art DOES use the cache.
 * ==========================================================================*/

import * as THREE from 'three';
import { Q } from '../shared/quality.js';
import { isMuted } from '../shared/audioMute.js';
import { getLevel } from './audioLevels.js';
import { loadTexture } from '../shared/assets.js';
import { getAudioCtx, getMasterOut } from './audioBus.js';
import { S, activeWords } from './settings.js';
import { createAssetTracker } from './assetTracker.js';

const SPAWN_AHEAD = 120;     // spawn cards this far ahead of the camera
const DESPAWN_BEHIND = 30;   // free cards this far behind it
const CARD_GAP = 14;         // depth between cards, +/- jitter (deep-run baseline)
const CARD_GAP_JITTER = 4;
// Progression: the tube starts almost empty and fills in. No cards until
// FIRST_CARD_SEC, then they trickle in at CARD_GAP_FAR spacing and thicken to
// CARD_GAP as the run heats up.
const FIRST_CARD_SEC = 18;   // hold every tube card until the run is this old
const CARD_GAP_FAR = 46;     // depth between cards at heat 0 (sparse, early) -> lerps to CARD_GAP deep
const CARD_W = 2.52;         // card bounding width (height follows aspect) - 40% smaller default
const CARD_MAX_H = 2.52;
const FRAME_SCALE = 1.04;    // metal frame plane vs content (slim border)
const SIDE_OFFSET = 2.4;     // how far off the spine a card sits
// Videos hug the POV: a clip parked out at the wall only lives inside the
// play/audio radius for a heartbeat as the camera screams past, so it's glanced,
// never watched. A tighter lateral seat keeps the closest approach small (the
// card stays big and near-center for the whole VIDEO_PLAY_NEAR window) while a
// >=1.1u floor keeps the spine clear for the camera and the screen center
// readable for the bubble field.
const VIDEO_SIDE_OFFSET = 1.3;
const VIDEO_NORMAL_JITTER = 0.4;  // vs the +/-0.9 photos get
const SPAWN_PER_FRAME = Q.tier === 'mobile' ? 1 : 2; // spawn budget per frame - one at a time on mobile so a card mount (new VideoTexture / decoder) never doubles up on a single frame

// Video-texture upload throttle: a VideoTexture re-uploads the current frame to
// the GPU on EVERY render by default. Content is ~24-30fps, so uploading at the
// full render rate for every live video card is wasted fill-rate (the dominant
// mobile cost). We turn autoUpdate off and flag needsUpdate at most this often.
const VIDEO_TEX_INTERVAL = 1 / (Q.tier === 'mobile' ? 20 : 30);
// Far video cards upload even less often: a card 25u away is a few dozen pixels
// wide, so pushing it fresh frames at the full rate is pure wasted upload time.
// The full rate returns as the card closes in (and the spotlight always gets it).
const VIDEO_TEX_FAR = 24, VIDEO_TEX_MID = 14; // distance gates for the 3x / 1.75x interval stretch

// Gif texture uploads are budgeted per frame: after any hitch EVERY live gif is
// "due" at once, and each upload is a canvas texImage2D - letting them all land
// on the recovery frame just causes the next hitch. Round-robin keeps them fair.
const GIF_UPLOADS_PER_FRAME = Q.tier === 'mobile' ? 2 : 4;

const AUDIO_NEAR = 14, AUDIO_FAR = 26; // cubed proximity fade; ceiling is the live 'video' level (audioLevels.js)
const VIDEO_PLAY_FAR = 34;   // beyond this, park the video (release the decoder)
const VIDEO_PLAY_NEAR = 30;  // resume inside this (hysteresis so it can't flap)

const VEIL_GAP_MIN = 180, VEIL_GAP_MAX = 280; // tunnel-spanning translucent veils - kept sparse (you pass THROUGH them to fire an effect)
const MAX_LIVE_VEILS = 1;
const FIRST_VEIL_SEC = 32;   // hold the effect-firing veils until the run is this old (later than the cards)
const VEIL_GAP_EARLY_MULT = 1.8; // spacing between veils is this much wider at heat 0 (sparser early) -> 1.0 deep

// Decoded-ahead pool: the next few user images/gifs are fully decoded BEFORE
// their card exists, pumped continuously in the background. Without it, cards
// mounted mid-decode and read as hollow frames at speed (alphaTest discards
// the still-empty texture).
// Mobile pool is smaller: every pooled item is real RAM/GPU memory (a gif
// filmstrip is ~10MB of ImageBitmaps, a photo texture several MB) and iOS
// memory pressure - not CPU - is what kills workers and hitches the fall.
// At mobile speeds 5 decoded-ahead items still outrun the spawn cadence.
const PREFETCH_POOL = Q.tier === 'mobile' ? 5 : 8; // decoded items waiting for a card (survives max speed)
const PREFETCH_VIDEO_MAX = 1;  // at most this many idle video decoders ahead
// In-flight decode cap: a drained pool at max speed used to kick off up to 8
// decodes AT ONCE (fetches, canvas work, texture pre-uploads all landing in the
// same second) - the "speed up and everything stutters" case. The pool still
// fills to PREFETCH_POOL, it just trickles.
const PREFETCH_INFLIGHT = Q.tier === 'mobile' ? 2 : 4;
const WARM_TIMEOUT_MS = 3500;  // max startup wait for the first decodes

// GIF frames are cached decoded (as ImageBitmaps) so the ImageDecoder can be
// closed right after prefetch. Holding a live decoder per on-screen gif is what
// saturated the browser's decode pool at speed - gifs froze on a frame (or died
// outright) until a video freed a slot. Caps bound the cached-frame memory.
const GIF_MAX_DIM = Q.tier === 'mobile' ? 320 : 512; // longest side of a cached frame
// Mobile cap is tight on purpose: cached frames are raw RGBA ImageBitmaps
// (320^2 x 4B = ~0.4MB each), and iOS kills the tab well before Android when
// a pool of gifs holds long filmstrips (see the zip notes in media.js).
const GIF_MAX_FRAMES = Q.tier === 'mobile' ? 24 : 100; // clamp absurdly long gifs

// User photos are downscaled into a canvas capped at this long side before they
// become textures. A raw phone photo (12MP+) kept full-size is a multi-ms
// texImage2D right as its card mounts - the classic mobile frame skip - and
// pins ~50MB of GPU memory per live card for detail no one can see on a
// 4-unit-wide plane.
const IMG_MAX_DIM = Q.tier === 'mobile' ? 1024 : 2048;

// Spotlight: a user video card detaches on approach, rides ahead of the POV
// playing a random cutout (S.spotSeconds long) with full audio; card spawning
// is suppressed while it holds the stage.
const GRAB_DIST = 4.6;         // units ahead a grabbed image/gif card hovers (centered like a spotlight)
const THROW_SPEED = 55;        // u/s a thrown card (Sticky Fingers capstone) rockets down-tube
const THROW_IMPACT_AT = 50;    // units ahead of the camera where the throw resolves
const SWIRL_RAMP_SEC = 300;    // seconds of run time to reach full hypnotic twirl on wall cards
const SWIRL_MAX_RATE = 0.34;   // rad/s peak swirl (~18s per revolution - dreamy, not dizzying)
const SPOTLIGHT_DIST = 2.8;    // units ahead of the camera it hovers (close = big)
const SPOTLIGHT_GAP = 130;     // depth units before the next feature is allowed
const SPOTLIGHT_FADE_S = 1.6;  // audio fade in the clip's final seconds
const SPOTLIGHT_FADE_V = 0.8;  // seconds of visual fade-out at the end

const rand = (a, b) => a + Math.random() * (b - a);
const pick = (arr) => arr[(Math.random() * arr.length) | 0];

// ---- shared canvas textures (copied from sectorBuilder.js) -----------------
function makeRoundedMask() {
  const w = 256, h = 256, r = 46;
  const c = document.createElement('canvas');
  c.width = w; c.height = h;
  const x = c.getContext('2d');
  x.fillStyle = '#fff';
  x.beginPath();
  x.moveTo(r, 0);
  x.arcTo(w, 0, w, h, r);
  x.arcTo(w, h, 0, h, r);
  x.arcTo(0, h, 0, 0, r);
  x.arcTo(0, 0, w, 0, r);
  x.closePath();
  x.fill();
  const t = new THREE.CanvasTexture(c);
  t.colorSpace = THREE.SRGBColorSpace;
  return t;
}

function makeMetalFrameTexture() {
  // Slim band: the plane is only FRAME_SCALE bigger than the content, and the
  // metal ring itself is a few texels wide (512px canvas keeps it crisp).
  const s = 512, c = document.createElement('canvas');
  c.width = c.height = s;
  const x = c.getContext('2d');
  const rr = (ix, iy, iw, ih, r) => {
    x.beginPath();
    x.moveTo(ix + r, iy);
    x.arcTo(ix + iw, iy, ix + iw, iy + ih, r);
    x.arcTo(ix + iw, iy + ih, ix, iy + ih, r);
    x.arcTo(ix, iy + ih, ix, iy, r);
    x.arcTo(ix, iy, ix + iw, iy, r);
    x.closePath();
  };
  const g = x.createLinearGradient(0, 0, 0, s);
  g.addColorStop(0.00, '#5b5563');
  g.addColorStop(0.16, '#e9e2ef');
  g.addColorStop(0.30, '#fdf7fb');
  g.addColorStop(0.46, '#9a8c9e');
  g.addColorStop(0.62, '#efe4ee');
  g.addColorStop(0.80, '#b7a6b9');
  g.addColorStop(1.00, '#4f4a57');
  const oI = 4, oR = 96;
  rr(oI, oI, s - oI * 2, s - oI * 2, oR);
  x.fillStyle = g; x.fill();
  const nI = 13, nR = 88;
  x.globalCompositeOperation = 'destination-out';
  rr(nI, nI, s - nI * 2, s - nI * 2, nR);
  x.fillStyle = '#000'; x.fill();
  x.globalCompositeOperation = 'source-over';
  x.lineWidth = 1.5;
  rr(nI, nI, s - nI * 2, s - nI * 2, nR);
  x.strokeStyle = 'rgba(255,255,255,0.85)'; x.stroke();
  x.lineWidth = 1;
  rr(oI, oI, s - oI * 2, s - oI * 2, oR);
  x.strokeStyle = 'rgba(255,255,255,0.35)'; x.stroke();
  const t = new THREE.CanvasTexture(c);
  t.colorSpace = THREE.SRGBColorSpace;
  t.minFilter = THREE.LinearFilter;
  t.generateMipmaps = false;
  return t;
}

function makeSoftDotTexture() {
  const s = 64, c = document.createElement('canvas');
  c.width = c.height = s;
  const x = c.getContext('2d');
  const g = x.createRadialGradient(s / 2, s / 2, 0, s / 2, s / 2, s / 2);
  g.addColorStop(0, 'rgba(255,255,255,1)');
  g.addColorStop(0.18, 'rgba(255,255,255,0.7)');
  g.addColorStop(0.5, 'rgba(255,255,255,0.12)');
  g.addColorStop(1, 'rgba(255,255,255,0)');
  x.fillStyle = g;
  x.beginPath(); x.arc(s / 2, s / 2, s / 2, 0, Math.PI * 2); x.fill();
  const t = new THREE.CanvasTexture(c);
  t.colorSpace = THREE.SRGBColorSpace;
  return t;
}

function makeWordTexture(word) {
  const W = 512, H = 256;
  const c = document.createElement('canvas');
  c.width = W; c.height = H;
  const x = c.getContext('2d');
  x.textAlign = 'center'; x.textBaseline = 'middle';
  x.font = '800 84px Poppins, system-ui, sans-serif';
  // shrink to fit long words/phrases
  let size = 84;
  while (size > 36 && x.measureText(word.toUpperCase()).width > W - 60) {
    size -= 6;
    x.font = `800 ${size}px Poppins, system-ui, sans-serif`;
  }
  const lr = (S.colLine >> 16) & 255, lg = (S.colLine >> 8) & 255, lb = S.colLine & 255;
  x.shadowColor = `rgba(${lr},${lg},${lb},0.95)`;
  x.shadowBlur = 26;
  x.fillStyle = `rgb(${Math.min(255, lr + 64)},${Math.min(255, lg + 64)},${Math.min(255, lb + 64)})`;
  x.fillText(word.toUpperCase(), W / 2, H / 2);
  x.shadowBlur = 0;
  x.fillStyle = 'rgba(255,255,255,0.92)';
  x.fillText(word.toUpperCase(), W / 2, H / 2);
  const t = new THREE.CanvasTexture(c);
  t.colorSpace = THREE.SRGBColorSpace;
  t.minFilter = THREE.LinearFilter;
  t.generateMipmaps = false;
  return t;
}

export function createSpawner({ scene, layout, media, renderer, camera, onCardApproach, onVeilPass }) {
  const MAX_LIVE_CARDS = Q.tier === 'mobile' ? 8 : 14;
  const MAX_LIVE_VIDEOS = Q.tier === 'mobile' ? 2 : 4;

  // Per-asset engagement tracker: accumulates weighted on-screen attention +
  // paddle interactions per user image/gif, drained to the host by the run brain.
  const tracker = createAssetTracker();
  const _av = new THREE.Vector3();   // scratch for the per-frame attention projection

  // Latest pointer in NDC, so a grabbed card can be steered like a paddle - it
  // tracks the cursor through the 3D space instead of pinning to the look axis.
  let cursorNx = 0, cursorNy = 0;
  function onCursorMove(e) {
    const r = renderer.domElement.getBoundingClientRect();
    if (!r.width || !r.height) return;
    cursorNx = ((e.clientX - r.left) / r.width) * 2 - 1;
    cursorNy = -((e.clientY - r.top) / r.height) * 2 + 1;
  }
  window.addEventListener('pointermove', onCursorMove, { passive: true });

  // Diagnostics: cheap counters at each stage of the media-card chain, readable
  // from any console as window.__sfCards (invaluable on a phone, where the gif
  // pipeline has broken silently more than once).
  const DIAG = (window.__sfCards = {
    draws: 0, gifTry: 0, gifOk: 0, gifFail: 0, imgOk: 0, vidOk: 0,
    spawnCalls: 0, poolHits: 0, lateBuilds: 0, added: 0, gifApplied: 0,
    gifWorkerOk: 0, gifWorkerTimeout: 0, prefetchStall: 0,
  });

  // ---- video proximity audio (copied from sectorBuilder.js) ----------------
  // The context itself is the fall-wide shared one (audioBus.js): video gains,
  // bubble SFX and the drone bed all mix through a single AudioContext instead
  // of three separate ones - iOS pays session overhead per context.
  const ensureCtx = getAudioCtx;
  function routeVideoGain(video) {
    const ctx = ensureCtx();
    if (!ctx) return null;
    try {
      const src = ctx.createMediaElementSource(video);
      const gain = ctx.createGain();
      gain.gain.value = 0;
      src.connect(gain); gain.connect(getMasterOut() || ctx.destination);
      try { video.volume = 1; } catch (e) { /* ignore (mobile) */ }
      return gain;
    } catch (e) { return null; }
  }
  function setVideoVolume(d, vol) {
    if (d.gainNode) { try { d.gainNode.gain.value = vol; } catch (e) { /* ignore */ } }
    else if (d.videoEl) { try { d.videoEl.volume = vol; } catch (e) { /* ignore */ } }
  }
  // Distance-managed play/pause with polite keep-alive (sectorBuilder.js:1684).
  function manageVideo(d, dist, t) {
    const el = d.videoEl;
    if (!el || document.hidden) return;
    if (dist >= VIDEO_PLAY_FAR) {
      if (!el.paused) { try { el.pause(); } catch (e) { /* ignore */ } }
      d.kickTries = 0; d.kickRest = 0;
      return;
    }
    if (!el.paused) { d.kickTries = 0; return; }
    if (dist > VIDEO_PLAY_NEAR) return;       // parked in the hysteresis band
    if (d.kickRest && t < d.kickRest) return; // resting after lost fights
    if ((d.kickTries || 0) >= 3) { d.kickRest = t + 30; d.kickTries = 0; return; }
    d.kickTries = (d.kickTries || 0) + 1;
    const p = el.play(); if (p && p.catch) p.catch(() => {});
  }

  // ---- shared card dressing --------------------------------------------------
  const roundMask = makeRoundedMask();
  const metalFrameTex = makeMetalFrameTexture();
  const glowTex = makeSoftDotTexture();
  const unitPlane = new THREE.PlaneGeometry(1, 1);

  // Push a ready texture to the GPU at PREFETCH time. Without this the first
  // texImage2D happens on the frame the card turns visible - stacked on top of
  // mount work, mid-fall. Here it lands on whatever quiet frame the decode
  // finished on instead.
  function preUpload(tex) {
    if (renderer) { try { renderer.initTexture(tex); } catch (e) { /* ignore */ } }
  }

  // WebKit (every iOS browser + desktop Safari) takes the pure-JS gif decoder:
  // its ImageDecoder/VideoFrame/createImageBitmap combos are exactly the APIs
  // that kept breaking gif cards on iPhone (missing resize options, rejected
  // decodes), and the old live-<img> sampler both froze when unpainted and
  // burned main-thread CPU animating full-size gifs when painted (audio
  // stutter). Chromium/Firefox keep the native ImageDecoder filmstrip.
  const IS_WEBKIT = /iP(hone|od|ad)/.test(navigator.platform)
    || (/Macintosh/.test(navigator.userAgent) && 'ontouchend' in document)
    || (/AppleWebKit/.test(navigator.userAgent) && !/Chrome|CriOS|FxiOS|Edg|OPR/.test(navigator.userAgent));

  // omggif (assets/vendor/omggif) loads lazily on the first gif, once.
  let gifReaderCtor = null;
  async function loadGifReader() {
    if (!gifReaderCtor) {
      ({ GifReader: gifReaderCtor } = await import('/dtrh/vendor/omggif/omggif.module.js'));
    }
    return gifReaderCtor;
  }

  // ---- gif decode worker ------------------------------------------------------
  // The whole omggif decode (LZW + compositing + downscale + ImageBitmap) runs
  // in a module worker (gifWorker.js) and streams frames back - the main thread
  // never blocks on a gif again. Feature-gated on OffscreenCanvas (iOS 16.4+);
  // anything older keeps the main-thread fallbacks below. Frames arrive into
  // the SAME anim/filmstrip shape the other decoders produce.
  let gifWorker = null, gifWorkerDead = false, gifJobId = 0, gifWorkerStrikes = 0;
  const gifJobs = new Map(); // id -> { frame(msg), done(), fail() }
  // The worker itself broke, or stopped answering: fall back for every waiting
  // job and stop using it for this session.
  function failGifWorker() {
    gifWorkerDead = true;
    for (const job of gifJobs.values()) job.fail('worker dead');
    gifJobs.clear();
    if (gifWorker) {
      try { gifWorker.terminate(); } catch (e) { /* ignore */ }
      gifWorker = null;
    }
  }
  function ensureGifWorker() {
    if (gifWorkerDead) return null;
    if (gifWorker) return gifWorker;
    if (typeof Worker !== 'function' || typeof OffscreenCanvas !== 'function'
      || typeof createImageBitmap !== 'function') { gifWorkerDead = true; return null; }
    try { gifWorker = new Worker('/dtrh/engine/gifWorker.js', { type: 'module' }); }
    catch (e) { gifWorkerDead = true; return null; }
    gifWorker.onmessage = (e) => {
      const m = e.data;
      const job = gifJobs.get(m.id);
      if (!job) { // job cancelled: frames may still be in flight - free them
        if (m.frame) { try { m.frame.bitmap.close(); } catch (err) { /* ignore */ } }
        return;
      }
      if (m.error) { gifJobs.delete(m.id); job.fail(m.error); }
      else if (m.frame) job.frame(m);
      else if (m.done) { gifJobs.delete(m.id); job.done(); }
    };
    gifWorker.onerror = failGifWorker; // script load / module failure
    return gifWorker;
  }

  // buf is CLONED to the worker (not transferred) so a per-gif failure can
  // still fall back to the main-thread decoders with the same bytes.
  function prefetchGifWorker(buf) {
    const w = ensureGifWorker();
    if (!w) return Promise.resolve(null);
    return new Promise((resolve) => {
      const id = ++gifJobId;
      let item = null;
      // Watchdog: a worker that silently never answers (a real failure mode on
      // iPhone) must NOT wedge this prefetch slot forever - with only two slots
      // on mobile, two hung jobs used to starve the whole card pipeline (no
      // gifs, no images, no videos). Give up on the job, fall back to the
      // main-thread decoders; two strikes in a row retires the worker.
      const watchdog = setTimeout(() => {
        if (!gifJobs.delete(id)) return;
        DIAG.gifWorkerTimeout += 1;
        try { if (gifWorker) gifWorker.postMessage({ cancel: id }); } catch (e) { /* ignore */ }
        if (++gifWorkerStrikes >= 2) failGifWorker();
        resolve(null);
      }, 8000);
      const job = {
        frame(m) {
          if (item) {
            if (item.anim.dead) { try { m.frame.bitmap.close(); } catch (e) { /* ignore */ } }
            else item.anim.frames.push(m.frame);
            return;
          }
          clearTimeout(watchdog);
          gifWorkerStrikes = 0;
          // first frame: build the canvas/texture shell and resolve the item
          const canvas = document.createElement('canvas');
          canvas.width = m.w; canvas.height = m.h;
          const ctx = canvas.getContext('2d');
          ctx.drawImage(m.frame.bitmap, 0, 0);
          const tex = new THREE.CanvasTexture(canvas);
          tex.colorSpace = THREE.SRGBColorSpace;
          tex.generateMipmaps = false;
          tex.minFilter = THREE.LinearFilter; tex.magFilter = THREE.LinearFilter;
          preUpload(tex);
          const anim = {
            dec: null, dead: false, complete: false,
            frames: [m.frame], i: 0, nextAt: performance.now() + m.frame.durMs,
          };
          item = {
            kind: 'gif', tex, canvas, ctx, anim, aspect: m.aspect,
            dispose() {
              anim.dead = true;
              if (gifJobs.delete(id) && gifWorker) { // still decoding: tell the worker to stop
                try { gifWorker.postMessage({ cancel: id }); } catch (e) { /* ignore */ }
              }
              for (const f of anim.frames) { try { f.bitmap.close(); } catch (e) { /* ignore */ } }
              anim.frames.length = 0;
              tex.dispose();
            },
          };
          DIAG.gifWorkerOk += 1;
          resolve(item);
        },
        done() { clearTimeout(watchdog); if (item) item.anim.complete = true; else resolve(null); },
        // failed BEFORE any frame: resolve null so prefetchGifAny falls back;
        // failed mid-stream: the gif just loops the frames it already has
        fail() { clearTimeout(watchdog); if (item) item.anim.complete = true; else resolve(null); },
      };
      gifJobs.set(id, job);
      try { w.postMessage({ id, buf, maxDim: GIF_MAX_DIM, maxFrames: GIF_MAX_FRAMES }); }
      catch (e) { clearTimeout(watchdog); gifJobs.delete(id); resolve(null); }
    });
  }

  const live = [];       // card records, in spawn (depth) order
  const dueGifs = [];    // gif cards due a frame this render (reused each update)
  let gifRotate = 0;     // round-robin cursor for the budgeted gif uploads
  let nextCardDepth = 30;
  let nextVeilDepth = 70;
  let videoWatch = 0;
  let liveVideos = 0;
  let liveVeils = 0;
  let paused = false;         // ESC pause (scene stops calling update too)
  let spotlight = null;       // the promoted video card, if any
  let grabbed = null;         // an image/gif card the player is holding in front of the POV
  let nextSpotlightOkAt = 60; // camera depth before the next feature may start
  let autoSpotlightOk = true; // Wave 2: the game turns approach-promotion off mid-run
  let pickupTimeScale = 1;    // Wave 2: pickups follow the game's freeze/slow-mo clock
  let throwOnRelease = null;  // Wave 2 (Sticky Fingers capstone): release hurls the card
  const _dir = new THREE.Vector3(), _target = new THREE.Vector3();
  const _ray = new THREE.Raycaster(), _ndc = new THREE.Vector2();
  const detached = new Set(); // junction-mouth cards: pixels-only, off the conveyor

  // group + glow + frame around a content plane, billboarded in update()
  function dressCard(group, contentMesh, w, h, accent) {
    const glowMat = new THREE.MeshBasicMaterial({
      map: glowTex, color: new THREE.Color(accent).lerp(new THREE.Color(0xffffff), 0.25),
      transparent: true, depthWrite: false, blending: THREE.AdditiveBlending, side: THREE.DoubleSide,
    });
    const glow = new THREE.Mesh(unitPlane, glowMat);
    glow.scale.set(w * 1.6, h * 1.75, 1);
    glow.position.z = -0.06;
    group.add(glow);

    const frameMat = new THREE.MeshBasicMaterial({
      map: metalFrameTex, color: new THREE.Color(accent).lerp(new THREE.Color(0xffffff), 0.2),
      transparent: true, depthWrite: false, side: THREE.DoubleSide,
    });
    const frame = new THREE.Mesh(unitPlane, frameMat);
    frame.scale.set(w * FRAME_SCALE, h * FRAME_SCALE, 1);
    frame.position.z = 0.01;
    group.add(frame);

    group.add(contentMesh);
    return [glowMat, frameMat];
  }

  function placeGroup(group, depth, kind) {
    const fr = layout.frameAtDepth(depth);
    const side = (live.length % 2 === 0 ? 1 : -1) * (Math.random() < 0.2 ? -1 : 1);
    // videos sit closer to the camera path than photos (see VIDEO_SIDE_OFFSET)
    const lat = kind === 'video' ? VIDEO_SIDE_OFFSET : SIDE_OFFSET;
    const vert = kind === 'video' ? VIDEO_NORMAL_JITTER : 0.9;
    group.position.copy(fr.pos)
      .addScaledVector(fr.binormal, side * lat)
      .addScaledVector(fr.normal, rand(-vert, vert));
  }

  // ---- user media: decode-ahead pipeline --------------------------------------
  // Decoders that turn an acquired blob URL into a mount-ready "item". They run
  // either in the background pool (before a card exists) or late, against an
  // already-spawned invisible shell.

  // Photo -> decoded, downscaled (IMG_MAX_DIM), GPU-uploaded texture + aspect.
  // The canvas copy bounds both the upload time and the per-card GPU memory no
  // matter how big the source photo is; preUpload then pays the texImage2D here
  // rather than on the mount frame. Releases the blob once decoded.
  async function prefetchImage(acquired) {
    try {
      const blob = await (await fetch(acquired.url)).blob();
      acquired.release();
      let source = null, w = 0, h = 0, bmp = null;
      try {
        bmp = await createImageBitmap(blob);
        source = bmp; w = bmp.width; h = bmp.height;
      } catch (e) {
        // createImageBitmap unavailable/refused (older Safari): <img> decode
        const url = URL.createObjectURL(blob);
        const img = new Image();
        img.decoding = 'async';
        img.src = url;
        try { if (img.decode) await img.decode(); } catch (e2) { /* fall through to load */ }
        if (!img.naturalWidth) {
          await new Promise((res) => {
            if (img.complete) return res();
            img.addEventListener('load', res, { once: true });
            img.addEventListener('error', res, { once: true });
          });
        }
        URL.revokeObjectURL(url);
        if (!img.naturalWidth) return null;
        source = img; w = img.naturalWidth; h = img.naturalHeight;
      }
      const shrink = Math.min(1, IMG_MAX_DIM / Math.max(w, h));
      const canvas = document.createElement('canvas');
      canvas.width = Math.max(2, Math.round(w * shrink));
      canvas.height = Math.max(2, Math.round(h * shrink));
      const ctx = canvas.getContext('2d');
      ctx.imageSmoothingQuality = 'high';
      ctx.drawImage(source, 0, 0, canvas.width, canvas.height);
      if (bmp) bmp.close();
      const t = new THREE.CanvasTexture(canvas);
      t.colorSpace = THREE.SRGBColorSpace;
      t.generateMipmaps = false;
      t.minFilter = THREE.LinearFilter; t.magFilter = THREE.LinearFilter;
      preUpload(t);
      DIAG.imgOk += 1;
      return { kind: 'image', tex: t, aspect: w / h, dispose() { t.dispose(); } };
    } catch (e) { acquired.release(); return null; }
  }

  // Cache a decoded gif frame at the shrunk size. createImageBitmap's resize
  // options are unimplemented on Safari (26+ ships ImageDecoder but rejects
  // them), so that path downscales through a scratch canvas instead. The
  // snapshot is taken synchronously at call time, so one shared scratch is
  // safe across concurrently-decoding gifs.
  let bitmapScratch = null;
  async function frameToBitmap(image, w, h) {
    try {
      return await createImageBitmap(image, {
        resizeWidth: w, resizeHeight: h, resizeQuality: 'low',
      });
    } catch (e) {
      if (!bitmapScratch) bitmapScratch = document.createElement('canvas');
      bitmapScratch.width = w; bitmapScratch.height = h;
      bitmapScratch.getContext('2d').drawImage(image, 0, 0, w, h);
      return createImageBitmap(bitmapScratch);
    }
  }

  // Animated GIF -> canvas texture whose frames are decoded ONCE into a cached
  // filmstrip of ImageBitmaps, after which the ImageDecoder is closed. A WebGL
  // texture only ever gets a GIF's FIRST frame (drawImage/TextureLoader use the
  // "default image" per spec - that is why dropped gifs looked static), so on
  // Chromium we decode frames ourselves. We used to keep the decoder live and
  // decode every frame on demand, but one open decoder per on-screen gif blew
  // past the browser's concurrent-decoder cap: decode() calls stalled or threw,
  // freezing gifs until a video released a slot. Now the decoder lives only for
  // the brief prefetch decode; playback just cycles the cached bitmaps.
  async function prefetchGif(buf) {
    try {
      const dec = new ImageDecoder({ data: buf, type: 'image/gif' });
      await dec.tracks.ready;
      const total = dec.tracks.selectedTrack ? dec.tracks.selectedTrack.frameCount : 1;
      const count = Math.min(total, GIF_MAX_FRAMES);
      const { image } = await dec.decode({ frameIndex: 0 });
      const w0 = image.displayWidth || 2, h0 = image.displayHeight || 2;
      const shrink = Math.min(1, GIF_MAX_DIM / Math.max(w0, h0));
      const canvas = document.createElement('canvas');
      canvas.width = Math.max(2, Math.round(w0 * shrink));
      canvas.height = Math.max(2, Math.round(h0 * shrink));
      const ctx = canvas.getContext('2d');
      ctx.drawImage(image, 0, 0, canvas.width, canvas.height); // show frame 0 now
      const dur0 = image.duration ? image.duration / 1000 : 100;
      // cache frame 0 at the shrunk size so the loop can redraw it later
      const bitmap0 = await frameToBitmap(image, canvas.width, canvas.height);
      image.close();
      const tex = new THREE.CanvasTexture(canvas);
      tex.colorSpace = THREE.SRGBColorSpace;
      tex.generateMipmaps = false;
      tex.minFilter = THREE.LinearFilter; tex.magFilter = THREE.LinearFilter;
      preUpload(tex);
      const anim = {
        dec, dead: false, complete: count < 2,
        frames: [{ bitmap: bitmap0, durMs: Math.max(20, dur0) }],
        i: 0, nextAt: performance.now() + Math.max(20, dur0),
      };
      // decode the rest in the background, then release the decoder
      if (count > 1) decodeGifFrames(anim, canvas.width, canvas.height, count);
      else { try { dec.close(); } catch (e) { /* ignore */ } anim.dec = null; }
      return {
        kind: 'gif', tex, canvas, ctx, anim, aspect: w0 / h0,
        dispose() {
          anim.dead = true;
          if (anim.dec) { try { anim.dec.close(); } catch (e) { /* ignore */ } anim.dec = null; }
          for (const f of anim.frames) { try { f.bitmap.close(); } catch (e) { /* ignore */ } }
          anim.frames.length = 0;
          tex.dispose();
        },
      };
    } catch (e) { console.warn('[sissy-fall] native gif decode failed:', e); return null; }
  }

  // Background pass: decode frames 1..count-1 into the cached filmstrip, then
  // close the decoder. Whatever frames land keep the gif animating; a card that
  // despawns mid-decode sets anim.dead and we bail (and free the bitmaps).
  async function decodeGifFrames(anim, w, h, count) {
    try {
      for (let idx = 1; idx < count; idx++) {
        if (anim.dead || !anim.dec) return;
        const { image } = await anim.dec.decode({ frameIndex: idx });
        if (anim.dead) { image.close(); return; }
        const durMs = image.duration ? image.duration / 1000 : 100;
        const bitmap = await frameToBitmap(image, w, h);
        image.close();
        if (anim.dead) { try { bitmap.close(); } catch (e) { /* ignore */ } return; }
        anim.frames.push({ bitmap, durMs: Math.max(20, durMs) });
      }
    } catch (e) { /* keep whatever frames we captured - the gif still loops */ }
    finally {
      anim.complete = true;
      if (anim.dec) { try { anim.dec.close(); } catch (e) { /* ignore */ } anim.dec = null; }
    }
  }

  // Animated GIF, universal path (WebKit, or anywhere ImageDecoder is missing
  // or failed): omggif parses the file and we composite frames ourselves into
  // the SAME downscaled ImageBitmap filmstrip the native path produces - no
  // live <img>, no VideoFrame, nothing the browser can silently freeze or
  // reject. Frame 0 lands before the card mounts; the rest decode in the
  // background, yielding to the event loop each frame so a long gif can never
  // stall the render loop or the audio.
  async function prefetchGifJS(buf) {
    try {
      const GifReader = await loadGifReader();
      const reader = new GifReader(new Uint8Array(buf));
      const w0 = reader.width, h0 = reader.height;
      const total = reader.numFrames();
      if (!w0 || !h0 || !total) return null;
      const count = Math.min(total, GIF_MAX_FRAMES);
      const shrink = Math.min(1, GIF_MAX_DIM / Math.max(w0, h0));
      // full-size compositor: gif frames are patches over the previous frame,
      // so one persistent RGBA buffer accumulates them (omggif blits a frame's
      // rect and skips its transparent pixels). Disposal 2 rects are cleared
      // before the next frame lands; disposal 3 (rare) is approximated as keep.
      const rgba = new Uint8ClampedArray(w0 * h0 * 4);
      const idata = new ImageData(rgba, w0, h0);
      const work = document.createElement('canvas');
      work.width = w0; work.height = h0;
      const wctx = work.getContext('2d');
      const scratch = document.createElement('canvas');
      scratch.width = Math.max(2, Math.round(w0 * shrink));
      scratch.height = Math.max(2, Math.round(h0 * shrink));
      const sctx = scratch.getContext('2d');
      let prevDisposal = 0, prevInfo = null;
      const decodeFrame = async (idx) => {
        if (prevDisposal === 2 && prevInfo) {
          for (let row = 0; row < prevInfo.height; row++) {
            const at = ((prevInfo.y + row) * w0 + prevInfo.x) * 4;
            rgba.fill(0, at, at + prevInfo.width * 4);
          }
        }
        const info = reader.frameInfo(idx);
        reader.decodeAndBlitFrameRGBA(idx, rgba);
        prevDisposal = info.disposal; prevInfo = info;
        wctx.putImageData(idata, 0, 0);
        sctx.clearRect(0, 0, scratch.width, scratch.height);
        sctx.drawImage(work, 0, 0, scratch.width, scratch.height);
        // snapshot is taken synchronously, so reusing scratch is safe
        const bitmap = await createImageBitmap(scratch);
        return { bitmap, durMs: Math.max(20, (info.delay || 10) * 10) };
      };
      const f0 = await decodeFrame(0);
      const canvas = document.createElement('canvas');
      canvas.width = scratch.width; canvas.height = scratch.height;
      const ctx = canvas.getContext('2d');
      ctx.drawImage(f0.bitmap, 0, 0); // show frame 0 now
      const tex = new THREE.CanvasTexture(canvas);
      tex.colorSpace = THREE.SRGBColorSpace;
      tex.generateMipmaps = false;
      tex.minFilter = THREE.LinearFilter; tex.magFilter = THREE.LinearFilter;
      preUpload(tex);
      const anim = {
        dec: null, dead: false, complete: count < 2,
        frames: [f0], i: 0, nextAt: performance.now() + f0.durMs,
      };
      if (count > 1) {
        (async () => {
          try {
            for (let idx = 1; idx < count; idx++) {
              if (anim.dead) return;
              // macrotask yield: LZW-decoding a big frame takes a few ms, and
              // several gifs can be prefetching at once - never stack them
              await new Promise((r) => setTimeout(r));
              if (anim.dead) return;
              const f = await decodeFrame(idx);
              if (anim.dead) { try { f.bitmap.close(); } catch (e) { /* ignore */ } return; }
              anim.frames.push(f);
            }
          } catch (e) { /* keep whatever frames we captured - the gif still loops */ }
          finally { anim.complete = true; }
        })();
      }
      return {
        kind: 'gif', tex, canvas, ctx, anim, aspect: w0 / h0,
        dispose() {
          anim.dead = true;
          for (const f of anim.frames) { try { f.bitmap.close(); } catch (e) { /* ignore */ } }
          anim.frames.length = 0;
          tex.dispose();
        },
      };
    } catch (e) { console.warn('[sissy-fall] js gif decode failed:', e); return null; }
  }

  // Pick the gif decode strategy: native ImageDecoder filmstrip on
  // Chromium/Firefox (off-main-thread decode, proven), then the worker decoder
  // (WebKit's primary path - fully off the main thread), then the main-thread
  // JS decoder as the last resort (pre-16.4 iOS, or a broken worker). All
  // consume the same fetched bytes, so a failed attempt costs nothing extra.
  async function prefetchGifAny(acquired) {
    DIAG.gifTry += 1;
    let buf;
    try { buf = await (await fetch(acquired.url)).arrayBuffer(); }
    catch (e) { acquired.release(); DIAG.gifFail += 1; return null; }
    acquired.release(); // the decoders own the bytes now
    let item = null;
    if (!IS_WEBKIT && typeof window.ImageDecoder === 'function') {
      item = await prefetchGif(buf);
    }
    if (!item) item = await prefetchGifWorker(buf);
    if (!item) item = await prefetchGifJS(buf);
    if (item) DIAG.gifOk += 1; else DIAG.gifFail += 1;
    return item;
  }

  // User video -> a preloading <video> element (paused + muted until mounted).
  // iOS WON'T buffer a paused video to 'loadeddata' - it stops at metadata (if
  // that; preload is a hint it mostly ignores). Requiring loadeddata here made
  // every video draw on iPhone time out to null: no video cards, and each
  // attempt held one of the two mobile prefetch slots for 8s, choking gifs and
  // images too. So: metadata is enough to ship the element - buildVideoCard
  // play()s it on mount, which is what actually pulls the data on iOS (that was
  // the pre-prefetch pipeline's behavior all along).
  function prefetchVideo(acquired) {
    return new Promise((resolve) => {
      const video = document.createElement('video');
      video.crossOrigin = 'anonymous'; // media served from https://ccp.assets (cross-origin): keep WebGL uploads untainted
      video.src = acquired.url;
      video.loop = true; video.playsInline = true;
      video.setAttribute('playsinline', '');
      video.preload = 'auto';
      video.muted = true;
      let done = false;
      let metaTimer = 0;
      const finish = (ok) => {
        if (done) return;
        done = true;
        clearTimeout(timer);
        clearTimeout(metaTimer);
        video.removeEventListener('loadeddata', onOk);
        video.removeEventListener('loadedmetadata', onMeta);
        video.removeEventListener('error', onErr);
        if (ok) {
          // Make + upload the texture NOW: the mount frame then reuses it
          // instead of paying VideoTexture creation + a first-frame texImage2D
          // stacked on the rest of the card build.
          let vtex = null;
          if (video.readyState >= 2) {
            vtex = new THREE.VideoTexture(video);
            vtex.colorSpace = THREE.SRGBColorSpace;
            vtex.minFilter = THREE.LinearFilter;
            vtex.generateMipmaps = false;
            vtex.autoUpdate = false;
            preUpload(vtex);
          }
          DIAG.vidOk += 1;
          resolve({
            kind: 'video', videoEl: video, vtex, release: acquired.release,
            dispose() {
              if (vtex) vtex.dispose();
              try { video.pause(); video.removeAttribute('src'); video.load(); } catch (e) { /* ignore */ }
              acquired.release();
            },
          });
        } else {
          try { video.removeAttribute('src'); video.load(); } catch (e) { /* ignore */ }
          acquired.release();
          resolve(null);
        }
      };
      const onOk = () => finish(true);
      const onErr = () => finish(false);
      // metadata proves the file decodes; give loadeddata a short grace for the
      // browsers that DO buffer paused video, then ship the element as-is
      const onMeta = () => { metaTimer = setTimeout(() => finish(true), 1200); };
      video.addEventListener('loadeddata', onOk, { once: true });
      video.addEventListener('loadedmetadata', onMeta, { once: true });
      video.addEventListener('error', onErr, { once: true });
      try { video.load(); } catch (e) { /* ignore */ } // iOS: preload alone fetches nothing
      const timer = setTimeout(() => finish(video.readyState >= 1), 8000);
    });
  }

  const ready = [];      // decoded items waiting for a card
  let prefetching = 0;   // decodes in flight

  async function prefetchOne() {
    const vidsAhead = ready.filter((r) => r.kind === 'video').length;
    const wantImage = vidsAhead >= PREFETCH_VIDEO_MAX || liveVideos >= MAX_LIVE_VIDEOS;
    const entry = (wantImage && media.drawKind('image')) || media.draw();
    if (!entry) return null;
    DIAG.draws += 1;
    const acquired = await entry.acquire();
    if (!acquired) return null;
    let item;
    if (entry.kind === 'video') item = await prefetchVideo(acquired);
    else if (/\.gif$/i.test(entry.name || '')) item = await prefetchGifAny(acquired);
    else item = await prefetchImage(acquired);
    // stamp the asset identity so the card can be attributed for engagement tracking
    if (item) { item.assetName = entry.name; item.assetKind = entry.kind; }
    return item;
  }

  // Keep the pool topped up; called every frame (cheap when full) so the next
  // few seconds of cards are always decoded before they are needed. In-flight
  // decodes are capped (PREFETCH_INFLIGHT) so a drained pool refills as a
  // trickle instead of eight simultaneous decode jobs.
  function pumpPrefetch() {
    if (!media.hasUserMedia()) return;
    while (ready.length + prefetching < PREFETCH_POOL && prefetching < PREFETCH_INFLIGHT) {
      prefetching += 1;
      // Backstop for the backstops: NO decode, whatever its path, may hold a
      // slot forever. A wedged one frees its slot after 20s so the pipeline
      // keeps flowing; if it settles late anyway, the item is still used.
      let slotOpen = true;
      const freeSlot = () => { if (slotOpen) { slotOpen = false; prefetching -= 1; } };
      const watchdog = setTimeout(() => { DIAG.prefetchStall += 1; freeSlot(); }, 20000);
      Promise.resolve(prefetchOne()).then((item) => {
        clearTimeout(watchdog);
        freeSlot();
        if (item) ready.push(item);
      }).catch(() => { clearTimeout(watchdog); freeSlot(); });
    }
  }

  function takeReady(allowVideo) {
    for (let i = 0; i < ready.length; i++) {
      if (ready[i].kind === 'video' && !allowVideo) continue;
      return ready.splice(i, 1)[0];
    }
    return null;
  }

  // Pre-compile the card shader programs while the loader still covers the
  // page. Card materials only enter the scene mid-fall, so without this the
  // FIRST user card (and first video) stalls a live frame on program
  // compilation. Two dummy materials cover both program shapes every card,
  // word, veil and dressing plane uses; renderer.compile also picks up the
  // tunnel/fog/fx materials already sitting in the scene.
  function prewarmGpu() {
    if (!renderer || !camera) return;
    const tiny = document.createElement('canvas');
    tiny.width = tiny.height = 2;
    const tex = new THREE.CanvasTexture(tiny);
    const mats = [
      new THREE.MeshBasicMaterial({ map: tex, alphaMap: roundMask, transparent: true, alphaTest: 0.5, side: THREE.DoubleSide }),
      new THREE.MeshBasicMaterial({ map: tex, transparent: true, depthWrite: false, blending: THREE.AdditiveBlending, side: THREE.DoubleSide }),
    ];
    const g = new THREE.Group();
    for (const m of mats) g.add(new THREE.Mesh(unitPlane, m));
    scene.add(g);
    try { renderer.compile(scene, camera); } catch (e) { /* ignore */ }
    scene.remove(g);
    for (const m of mats) m.dispose();
    tex.dispose();
  }

  // Startup warm-up: give the first decodes a head start while the loader is
  // still on screen, so the opening plunge never meets a hollow card.
  function warm() {
    prewarmGpu();
    if (!media.hasUserMedia()) return Promise.resolve();
    const s = media.stats();
    if (s.videos) ensureCtx(); // audio-graph spin-up off the first video's mount frame
    const want = Math.min(3, PREFETCH_POOL, s.images + s.videos);
    const until = performance.now() + WARM_TIMEOUT_MS;
    return new Promise((resolve) => {
      (function tick() {
        pumpPrefetch();
        if (ready.length >= want || performance.now() >= until) resolve();
        else setTimeout(tick, 120);
      })();
    });
  }

  // ---- card builders --------------------------------------------------------
  // Shared shell for user image/gif cards: frame + glow + content plane, kept
  // INVISIBLE until real pixels exist (a mounted-but-empty map used to render
  // as a hollow frame).
  function makeUserCardShell(depth) {
    const group = new THREE.Group();
    const mat = new THREE.MeshBasicMaterial({
      map: null, alphaMap: roundMask, transparent: true, alphaTest: 0.5,
      side: THREE.DoubleSide,
    });
    const mesh = new THREE.Mesh(unitPlane, mat);
    mesh.scale.set(CARD_W, CARD_W * 0.75, 1);
    const dressed = dressCard(group, mesh, CARD_W, CARD_W * 0.75, S.colLine);
    group.visible = false;
    placeGroup(group, depth);
    return { group, mat, mesh, dressed };
  }

  function sizeShell(shell, aspect) {
    let w = CARD_W, h = w / aspect;
    if (h > CARD_MAX_H) { h = CARD_MAX_H; w = h * aspect; }
    shell.mesh.scale.set(w, h, 1);
    shell.group.children[0].scale.set(w * 1.6, h * 1.75, 1);            // glow
    shell.group.children[1].scale.set(w * FRAME_SCALE, h * FRAME_SCALE, 1); // frame
  }

  function applyImageItem(rec, shell, item) {
    if (rec.dead) { item.dispose(); return; } // despawned while decoding
    rec.contentMat = shell.mat;      // melt fade + mirror swap need the content material
    rec.dressedMats = shell.dressed;
    shell.mat.map = item.tex;
    shell.mat.needsUpdate = true;
    sizeShell(shell, item.aspect);
    // Ken Burns: slow pan + zoom inside the frame. Only the photo's map
    // transforms - the rounded alphaMap keeps its own identity transform
    // (independent per-map transforms, three r152+). Card-owned texture, so
    // no shared-cache texture is ever mutated.
    const zoomIn = Math.random() < 0.5;
    const kb = {
      tex: item.tex, life: 0, dur: rand(14, 24),
      s0: zoomIn ? 0.98 : 0.82, s1: zoomIn ? 0.82 : 0.98,
      ax0: Math.random(), ay0: Math.random(),
      ax1: Math.random(), ay1: Math.random(),
    };
    item.tex.wrapS = item.tex.wrapT = THREE.ClampToEdgeWrapping;
    item.tex.repeat.set(kb.s0, kb.s0);
    item.tex.offset.set(kb.ax0 * (1 - kb.s0), kb.ay0 * (1 - kb.s0));
    rec.kb = kb;
    rec.tex = item.tex;
    shell.group.visible = true;
    if (mirror) applyMirror(rec);    // born mid-Mirror-Moment: wear the favorite
  }

  function applyGifItem(rec, shell, item) {
    if (rec.dead) { item.dispose(); return; }
    DIAG.gifApplied += 1;
    rec.contentMat = shell.mat;      // melt fade + mirror swap need the content material
    rec.dressedMats = shell.dressed;
    shell.mat.map = item.tex;
    shell.mat.needsUpdate = true;
    sizeShell(shell, item.aspect);
    rec.gifAnim = item.anim;
    rec.gifCanvas = item.canvas;
    rec.gifCtx = item.ctx;
    rec.gifTex = item.tex;
    rec.gifItem = item;
    shell.group.visible = true;
    if (mirror) applyMirror(rec);    // born mid-Mirror-Moment: wear the favorite
  }

  // Image/gif card from a decoded pool item - visible from its first frame.
  function buildImageCard(depth, item) {
    const shell = makeUserCardShell(depth);
    const rec = {
      group: shell.group, depth, kind: 'image', billboard: true, tex: null,
      dispose() {
        rec.dead = true;
        if (rec.tex) rec.tex.dispose();
        shell.mat.dispose();
        for (const m of shell.dressed) m.dispose();
      },
    };
    applyImageItem(rec, shell, item);
    return rec;
  }

  function buildGifCard(depth, item) {
    const shell = makeUserCardShell(depth);
    const rec = {
      group: shell.group, depth, kind: 'image', billboard: true,
      dispose() {
        rec.dead = true;
        if (rec.gifItem) rec.gifItem.dispose();
        shell.mat.dispose();
        for (const m of shell.dressed) m.dispose();
      },
    };
    applyGifItem(rec, shell, item);
    return rec;
  }

  // Late variants for when the pool ran dry: the shell spawns invisible and the
  // decode lands against it (or against nothing, if the card despawned first).
  function buildImageCardLate(depth, acquired) {
    const shell = makeUserCardShell(depth);
    const rec = {
      group: shell.group, depth, kind: 'image', billboard: true, tex: null,
      dispose() {
        rec.dead = true;
        if (rec.tex) rec.tex.dispose();
        shell.mat.dispose();
        for (const m of shell.dressed) m.dispose();
      },
    };
    prefetchImage(acquired).then((item) => { if (item) applyImageItem(rec, shell, item); });
    return rec;
  }

  function buildGifCardLate(depth, acquired) {
    const shell = makeUserCardShell(depth);
    const rec = {
      group: shell.group, depth, kind: 'image', billboard: true,
      dispose() {
        rec.dead = true;
        if (rec.gifItem) rec.gifItem.dispose();
        shell.mat.dispose();
        for (const m of shell.dressed) m.dispose();
      },
    };
    prefetchGifAny(acquired).then((item) => { if (item) applyGifItem(rec, shell, item); });
    return rec;
  }

  // A gif card is due when its next cached frame's timestamp has passed.
  const gifDue = (a) => !a.dead && a.frames.length > 1 && performance.now() >= a.nextAt;

  // Advance a due gif card to the cached frame that should be showing NOW. No
  // decoding here (frames were cached in prefetch), so this can never stall or
  // die. The walk skips over frames a hitch (or the upload budget) made us
  // miss, so a gif catches up instead of playing everything in slow motion -
  // and pays only ONE canvas draw + texture upload however far behind it fell.
  // Cycling modulo the current length lets a still-decoding gif loop what it
  // has so far.
  function advanceGif(rec) {
    const a = rec.gifAnim;
    const t = performance.now();
    let guard = a.frames.length * 2; // long stalls resync below instead of walking forever
    while (t >= a.nextAt && guard-- > 0) {
      a.i = (a.i + 1) % a.frames.length;
      a.nextAt += a.frames[a.i].durMs;
    }
    if (t >= a.nextAt) a.nextAt = t + a.frames[a.i].durMs;
    const f = a.frames[a.i];
    rec.gifCtx.drawImage(f.bitmap, 0, 0, rec.gifCanvas.width, rec.gifCanvas.height);
    rec.gifTex.needsUpdate = true;
  }

  // Glowing subliminal word card (generated canvas texture, card-owned).
  function buildWordCard(depth, words) {
    const group = new THREE.Group();
    const tex = makeWordTexture(pick(words));
    const mat = new THREE.MeshBasicMaterial({
      map: tex, transparent: true, depthWrite: false,
      blending: THREE.AdditiveBlending, side: THREE.DoubleSide,
    });
    const mesh = new THREE.Mesh(unitPlane, mat);
    mesh.scale.set(4.6, 2.3, 1);
    group.add(mesh);
    placeGroup(group, depth);
    return {
      group, depth, kind: 'word', billboard: true,
      dispose() { tex.dispose(); mat.dispose(); },
    };
  }

  // Video card - a user video (with proximity audio) or a muted flash clip.
  // Fresh <video> element per spawn: createMediaElementSource permanently binds
  // an element, so elements are never pooled (sectorBuilder's hard lesson).
  function buildVideoCard(depth, url, opts) {
    const withAudio = !!(opts && opts.withAudio);
    const release = (opts && opts.release) || null;

    // A prefetched element arrives already buffering (often with a decoded
    // first frame); otherwise start a fresh one from the URL.
    const video = (opts && opts.videoEl) || document.createElement('video');
    if (!(opts && opts.videoEl)) {
      video.crossOrigin = 'anonymous'; // cross-origin ccp.assets media: keep WebGL uploads untainted
      video.src = url;
      video.loop = true; video.playsInline = true;
      video.setAttribute('playsinline', '');
      video.preload = 'auto';
    }
    video.muted = !withAudio;
    if (withAudio) video.volume = 0;
    const preUploaded = !!(opts && opts.vtex);
    let vtex = opts && opts.vtex; // prefetched: already built + GPU-uploaded
    if (!vtex) {
      vtex = new THREE.VideoTexture(video);
      vtex.colorSpace = THREE.SRGBColorSpace;
      vtex.minFilter = THREE.LinearFilter;
      vtex.generateMipmaps = false;
      vtex.autoUpdate = false; // we drive needsUpdate ourselves, throttled (see update())
    }
    const pl = video.play();
    if (pl && pl.catch) pl.catch(() => { video.muted = true; const p2 = video.play(); if (p2 && p2.catch) p2.catch(() => {}); });

    const group = new THREE.Group();
    const mat = new THREE.MeshBasicMaterial({
      map: vtex, alphaMap: roundMask, transparent: true, alphaTest: 0.5, side: THREE.DoubleSide,
    });
    const mesh = new THREE.Mesh(unitPlane, mat);
    mesh.scale.set(CARD_W, CARD_W * (9 / 16), 1);
    const dressed = dressCard(group, mesh, CARD_W, CARD_W * (9 / 16), S.colLine);
    group.visible = false; // nothing shows until the first frame is decodable
    const onMeta = () => {
      const aspect = (video.videoWidth && video.videoHeight) ? video.videoWidth / video.videoHeight : 16 / 9;
      let w = CARD_W, h = w / aspect;
      if (h > CARD_MAX_H) { h = CARD_MAX_H; w = h * aspect; }
      mesh.scale.set(w, h, 1);
      group.children[0].scale.set(w * 1.6, h * 1.75, 1);
      group.children[1].scale.set(w * FRAME_SCALE, h * FRAME_SCALE, 1);
    };
    // push the first frame the moment the card shows - unless prefetch already
    // uploaded it (re-flagging would pay that texImage2D again on the mount frame)
    const onData = () => { group.visible = true; if (!preUploaded) vtex.needsUpdate = true; };
    video.addEventListener('loadedmetadata', onMeta);
    video.addEventListener('loadeddata', onData);
    if (video.readyState >= 1) onMeta(); // prefetched: events already fired
    if (video.readyState >= 2) onData();

    const rec = {
      group, depth, kind: 'video', billboard: true,
      videoEl: video, vtex, texAcc: 0, gainNode: withAudio ? routeVideoGain(video) : null,
      vol: 0, kickTries: 0, kickRest: 0,
      spotlightable: withAudio, // user videos take the stage; spice clips stay on the wall
      fadeMats: null, // set lazily at spotlight fade-out
      contentMat: mat, dressedMats: dressed,
      dispose() {
        video.removeEventListener('loadedmetadata', onMeta);
        video.removeEventListener('loadeddata', onData);
        try { video.pause(); } catch (e) { /* ignore */ }
        if (rec.gainNode) { try { rec.gainNode.disconnect(); } catch (e) { /* ignore */ } }
        try { video.removeAttribute('src'); video.load(); } catch (e) { /* ignore */ } // release the decoder
        vtex.dispose(); mat.dispose();
        for (const m of dressed) m.dispose();
        if (release) release();
      },
    };
    placeGroup(group, depth, 'video');
    return rec;
  }

  // Tunnel-spanning translucent veils you fall THROUGH - each is a screen effect
  // made physical: punching through one fires that effect (spiral / pink wash /
  // glitch / flash) with a synced trigger drop. The crossing is detected in
  // update() and reported once via onVeilPass(kind); scene.js turns that into
  // the DOM wash + whispered snap.
  const VEIL_KINDS = [
    { kind: 'spiral', w: 5, url: '/dtrh/assets/bubbles/effects/spiral.png', spin: true },
    { kind: 'pinkfilter', w: 5, url: '/dtrh/assets/bubbles/effects/pinkfilter.png' },
    { kind: 'glitch', w: 4, url: '/dtrh/assets/bubbles/effects/glitch.png' },
    { kind: 'flash', w: 3, url: '/dtrh/assets/bubbles/effects/flash.png' },
  ];
  const VEIL_W = VEIL_KINDS.reduce((s, k) => s + k.w, 0);
  function pickVeilKind() {
    let r = Math.random() * VEIL_W;
    for (const k of VEIL_KINDS) if ((r -= k.w) < 0) return k;
    return VEIL_KINDS[0];
  }
  // spiral/pink follow their paint sliders; glitch tracks the glitch slider (with
  // a visible floor); flash is a fixed bright wash.
  function veilOpacity(kind) {
    if (kind === 'spiral') return S.spiralOpacity;
    if (kind === 'pinkfilter') return S.pinkOpacity;
    if (kind === 'glitch') return Math.max(0.3, 0.6 * Math.min(1, Math.max(0, S.glitch)));
    return 0.42; // flash
  }

  function buildVeil(depth) {
    const spec = pickVeilKind();
    const mat = new THREE.MeshBasicMaterial({
      map: loadTexture(spec.url), transparent: true,
      opacity: veilOpacity(spec.kind),
      depthWrite: false, blending: THREE.AdditiveBlending, side: THREE.DoubleSide,
    });
    const mesh = new THREE.Mesh(unitPlane, mat);
    const size = layout.RADIUS * 2.1;
    mesh.scale.set(size, size, 1);
    const fr = layout.frameAtDepth(depth);
    mesh.position.copy(fr.pos);
    mesh.lookAt(fr.pos.clone().add(fr.tangent));
    return {
      group: mesh, depth, kind: 'veil', billboard: false,
      spin: spec.spin ? rand(0.15, 0.3) : 0,
      veilMat: mat, veilKind: spec.kind, passed: false, // opacity follows live settings
      dispose() { mat.dispose(); }, // shared texture stays cached
    };
  }

  // ---- pickups (Wave 2) --------------------------------------------------------
  // Small clickable objects riding the tube wall. A pickup reuses the card
  // conveyor (live list, despawn, billboard, raycast registration) but is
  // CONSUMED by a click instead of grabbed. speed > 0 makes it race down-tube
  // (the White Rabbit); 0 lets the camera stream past it (Condensation).
  function placePickupGroup(rec) {
    const fr = layout.frameAtDepth(rec.depth);
    rec.group.position.copy(fr.pos)
      .addScaledVector(fr.binormal, rec.side)
      .addScaledVector(fr.normal, rec.rad);
  }

  function spawnPickup({ kind = 'pickup', spriteUrl, w = 1.5, aheadDepth = 55, ttlSec = 12,
    speed = 0, glowColor = 0xffd700, spin = 0, onClick = null, onGone = null }) {
    const group = new THREE.Group();
    // Pickups are placed toward the tube wall (rec.rad), so on a curve or behind a
    // wall poster/junction-room wall they'd get depth-occluded and vanish while the
    // click raycast (pickups-only) still grabs them - "grabbed an invisible droplet,
    // no gold shows". depthTest:false + a high renderOrder draws them OVER the wall so
    // the gold droplet + glow stay visible wherever they sit (cf. junctions.js text).
    const glowMat = new THREE.MeshBasicMaterial({
      map: glowTex, color: new THREE.Color(glowColor), transparent: true,
      depthWrite: false, depthTest: false, blending: THREE.AdditiveBlending, side: THREE.DoubleSide,
    });
    const glow = new THREE.Mesh(unitPlane, glowMat);
    glow.scale.set(w * 2.1, w * 2.1, 1);
    glow.position.z = -0.05;
    glow.renderOrder = 20;
    group.add(glow);
    const mat = new THREE.MeshBasicMaterial({
      map: loadTexture(spriteUrl), transparent: true, depthWrite: false, depthTest: false, side: THREE.DoubleSide,
    });
    const mesh = new THREE.Mesh(unitPlane, mat);
    mesh.scale.set(w, w, 1);
    mesh.renderOrder = 21;
    group.add(mesh);
    const rec = {
      group, depth: camDepthNow + aheadDepth, kind, pickup: true, billboard: true,
      side: (Math.random() < 0.5 ? -1 : 1) * SIDE_OFFSET * rand(0.55, 1),
      rad: rand(-0.9, 0.9),
      ttl: ttlSec, pkSpeed: speed, spin,
      pulseT: Math.random() * 6,
      onPickupClick: onClick, onPickupGone: onGone,
      dispose() { rec.dead = true; mat.dispose(); glowMat.dispose(); }, // loadTexture stays cached
    };
    placePickupGroup(rec);
    addCard(rec);
    return rec;
  }

  function clearPickups() {
    for (let i = live.length - 1; i >= 0; i--) if (live[i].pickup) removeCard(i);
  }

  // ---- content selection -----------------------------------------------------
  function spawnSpiceCard(depth) {
    // With no user media loaded the tube stays nearly blank - we ship no default
    // images/clips. Only the generated subliminal word cards drift past (and if
    // the player has cleared the word list, nothing spawns at all).
    const words = activeWords();
    return words.length ? buildWordCard(depth, words) : null;
  }

  async function spawnCardAt(depth) {
    DIAG.spawnCalls += 1;
    // Cards show ONLY the user's media. Spice (word cards / effect art /
    // avatars / flash clips) exists purely as the zero-drop fallback ride.
    if (!media.hasUserMedia()) return spawnSpiceCard(depth);

    // decoded-ahead pool first: these mount with real pixels already in hand
    const item = takeReady(liveVideos < MAX_LIVE_VIDEOS);
    if (item) {
      DIAG.poolHits += 1;
      let rec;
      if (item.kind === 'video') {
        rec = buildVideoCard(depth, null, { withAudio: true, release: item.release, videoEl: item.videoEl, vtex: item.vtex });
      } else if (item.kind === 'gif') {
        rec = buildGifCard(depth, item);
      } else {
        rec = buildImageCard(depth, item);
      }
      tagAsset(rec, item.assetName, item.assetKind || item.kind);
      return rec;
    }

    // pool ran dry (giant files / run just started): late path - the card
    // spawns invisible and appears once its content decodes
    let entry = media.draw();
    if (entry && entry.kind === 'video' && liveVideos >= MAX_LIVE_VIDEOS) {
      entry = media.drawKind('image') || entry;
    }
    if (!entry) return null;
    if (entry.kind === 'video' && liveVideos >= MAX_LIVE_VIDEOS) return null;

    const acquired = await entry.acquire();
    if (!acquired) return null; // file vanished: skip this slot
    if (entry.kind === 'video') {
      const rec = buildVideoCard(depth, acquired.url, { withAudio: true, release: acquired.release });
      tagAsset(rec, entry.name, entry.kind);
      return rec;
    }
    DIAG.lateBuilds += 1;
    const rec = /\.gif$/i.test(entry.name || '')
      ? buildGifCardLate(depth, acquired)
      : buildImageCardLate(depth, acquired);
    tagAsset(rec, entry.name, entry.kind);
    return rec;
  }

  // Stamp a card record with the identity of the user asset it displays, so its
  // on-screen attention + paddle interactions can be attributed per asset.
  function tagAsset(rec, name, kind) {
    if (rec && name) { rec.assetName = name; rec.assetKind = kind || 'image'; }
  }

  function addCard(rec) {
    if (!rec) return;
    DIAG.added += 1;
    if (rec.videoEl) liveVideos += 1;
    if (rec.kind === 'veil') liveVeils += 1;
    rec.group.userData.rec = rec; // so a raycast hit can climb back to its card
    live.push(rec);
    scene.add(rec.group);
  }

  function removeCard(i) {
    const rec = live[i];
    live.splice(i, 1);
    if (rec.videoEl) liveVideos = Math.max(0, liveVideos - 1);
    if (rec.kind === 'veil') liveVeils = Math.max(0, liveVeils - 1);
    scene.remove(rec.group);
    try { rec.dispose(); } catch (e) { /* ignore */ }
  }

  let spawning = 0;    // in-flight async spawns (media acquire)
  let camDepthNow = 0; // latest camera depth, read by late-landing async spawns

  // ---- spotlight ---------------------------------------------------------------
  // Promote an approaching user video card to the stage: seek a random ~30s
  // cutout, ride ahead of the POV at full volume, suppress new cards until done.
  function promoteSpotlight(rec, camera) {
    if (grabbed) releaseGrab(true); // a video taking the stage lets any held card go (never a throw)
    spotlight = rec;
    rec.isSpotlight = true;
    const clipLen = Math.max(10, Math.min(30, S.spotSeconds || 30));
    rec.clipLeft = clipLen;
    rec.fade = 1;
    // Follow model: the card sits EXACTLY spotDist ahead of the camera along
    // its forward axis every frame (so it can never end up behind the POV, no
    // matter how hard the fall accelerates); spotDist eases from where the
    // card was picked up to SPOTLIGHT_DIST, and a lateral offset decays to 0.
    camera.getWorldDirection(_dir);
    rec.spotDist = Math.max(3, _target.subVectors(rec.group.position, camera.position).dot(_dir));
    _target.copy(camera.position).addScaledVector(_dir, rec.spotDist);
    rec.spotLat = new THREE.Vector3().subVectors(rec.group.position, _target);
    const el = rec.videoEl;
    const seek = () => {
      const dur = el.duration;
      if (isFinite(dur) && dur > clipLen + 5) {
        try { el.currentTime = rand(0, dur - clipLen - 2); } catch (e) { /* not seekable: play as-is */ }
      } // shorter videos just loop through the window
    };
    if (isFinite(el.duration) && el.duration > 0) seek();
    else el.addEventListener('loadedmetadata', seek, { once: true });
    const p = el.play();
    if (p && p.catch) p.catch(() => { el.muted = true; const q = el.play(); if (q && q.catch) q.catch(() => {}); });
  }

  function endSpotlight(index) {
    removeCard(index);
    spotlight = null;
    nextSpotlightOkAt = camDepthNow + SPOTLIGHT_GAP;
    // spawning was suppressed; restart the ring buffer just ahead of the camera
    nextCardDepth = Math.max(nextCardDepth, camDepthNow + 24);
    nextVeilDepth = Math.max(nextVeilDepth, camDepthNow + 40);
  }

  function setCardFade(rec, op) {
    if (!rec.fadeMats) {
      rec.fadeMats = [rec.contentMat, ...(rec.dressedMats || [])];
      for (const m of rec.fadeMats) { m.alphaTest = 0; m.depthWrite = false; m.needsUpdate = true; }
    }
    for (const m of rec.fadeMats) m.opacity = op;
  }

  // ---- grab: hold an image/gif card in front of the POV ----------------------
  // A pointerdown that lands on a visible user photo/gif card seizes it and
  // pins it ahead of the camera (same follow model as the spotlight), easing
  // from wherever it was for a seamless pickup. Released on pointerup, or
  // automatically when a spotlight video takes the stage.
  function grabbableGroups() {
    const out = [];
    for (const r of live) if (r.kind === 'image' && r.group.visible && !r.isGrabbed && !r.melting) out.push(r.group);
    return out;
  }

  // ---- biome seams: grab arbitration, the melt, the Mirror Moment -------------
  // THE BIOMES (game/biomeMech.js): the game may veto a grab ('melt' - the
  // Gallery's look-don't-touch), and wants to hear about grabs that hold.
  let grabHooks = null;   // { policy(rec)->'allow'|'melt', onGrab(rec), onMelt(rec) }
  function setGrabHooks(h) { grabHooks = h || null; }

  // The melt: a denied grab dissolves in your hand - the card squashes down,
  // droops and fades over ~0.7s, then despawns. Animated in update().
  function meltCard(rec) {
    if (!rec || rec.melting || rec.pickup || rec.kind === 'veil') return;
    rec.melting = { t: 0, dur: 0.7 };
    rec.isGrabbed = false;
    if (grabbed === rec) grabbed = null;
  }

  /** THE BIOMES (Incognito): the wipe - every live card melts at once. The one
   * in your hand is spared (yanking a held card reads as a bug, not a wipe).
   * Returns how many started melting. */
  function meltAll() {
    let n = 0;
    for (const rec of live) {
      if (rec.melting || rec.pickup || rec.kind === 'veil' || rec.isGrabbed) continue;
      meltCard(rec);
      if (rec.melting) n++;
    }
    return n;
  }

  // The Mirror Moment (Hall of Mirrors): every live image card temporarily
  // wears ONE texture - the player's most-engaged asset. Videos keep playing
  // (a stalled video reads as a bug, not a mirror); gif stepping and Ken Burns
  // pause while mirrored so nothing fights the shared texture.
  let mirror = null;   // { url, tex }
  function applyMirror(rec) {
    if (!mirror || rec.kind !== 'image' || !rec.contentMat || rec.melting || rec.mirrorSaved) return;
    rec.mirrorSaved = { map: rec.contentMat.map, kb: rec.kb, gifAnim: rec.gifAnim };
    rec.kb = null;
    rec.gifAnim = null;
    rec.contentMat.map = mirror.tex;
    rec.contentMat.needsUpdate = true;
  }
  function restoreMirror(rec) {
    if (!rec.mirrorSaved || !rec.contentMat) return;
    rec.contentMat.map = rec.mirrorSaved.map;
    rec.kb = rec.mirrorSaved.kb || null;
    rec.gifAnim = rec.mirrorSaved.gifAnim || null;
    rec.contentMat.needsUpdate = true;
    rec.mirrorSaved = null;
  }
  function setMirror(url) {
    if (!url) {
      if (!mirror) return;
      for (const r of live) restoreMirror(r);
      mirror = null;   // texture is owned by the shared assets cache - no dispose
      return;
    }
    if (mirror && mirror.url === url) return;
    if (mirror) setMirror(null);   // swap: restore first
    mirror = { url, tex: loadTexture(url) };   // cached: repeat moments reuse the decode
    for (const r of live) applyMirror(r);
  }
  function clickableGroups() {
    const out = [];
    for (const r of live) if (r.pickup && !r.dead) out.push(r.group);
    return out;
  }
  // Nearest interactive hit distance (pickups + grabbable cards) WITHOUT acting -
  // scene.js uses it to arbitrate a junction doorway-card click against a closer
  // grab target under the same cursor.
  function probeGrab(ndcX, ndcY, camera) {
    _ndc.set(ndcX, ndcY);
    _ray.setFromCamera(_ndc, camera);
    let best = Infinity;
    const ph = _ray.intersectObjects(clickableGroups(), true);
    if (ph.length) best = ph[0].distance;
    if (!grabbed && !spotlight) {
      const gh = _ray.intersectObjects(grabbableGroups(), true);
      if (gh.length && gh[0].distance < best) best = gh[0].distance;
    }
    return Number.isFinite(best) ? best : null;
  }
  function grabAtPointer(ndcX, ndcY, camera) {
    _ndc.set(ndcX, ndcY);
    _ray.setFromCamera(_ndc, camera);
    // pickups first: a small deliberate target beats a grab (and stays
    // clickable even while a card is held or a spotlight plays)
    const picks = clickableGroups();
    if (picks.length) {
      const ph = _ray.intersectObjects(picks, true);
      for (const h of ph) {
        let o = h.object;
        while (o && !(o.userData && o.userData.rec)) o = o.parent;
        const rec = o && o.userData.rec;
        if (rec && rec.pickup && !rec.dead) {
          const idx = live.indexOf(rec);
          // hand the onClick the grab's window-px location (canvas fills the window)
          // so it can pop a positioned burst right on the droplet it was clicked on.
          const sx = (ndcX + 1) * 0.5 * window.innerWidth;
          const sy = (1 - ndcY) * 0.5 * window.innerHeight;
          if (rec.onPickupClick) { try { rec.onPickupClick(rec, sx, sy); } catch (e) { /* ignore */ } }
          if (idx >= 0) removeCard(idx);
          return true;
        }
      }
    }
    if (grabbed || spotlight) return false; // one at a time; never during a video stage
    const hits = _ray.intersectObjects(grabbableGroups(), true);
    for (const h of hits) {
      let o = h.object;
      while (o && !(o.userData && o.userData.rec)) o = o.parent;
      const rec = o && o.userData.rec;
      if (rec && rec.kind === 'image') { startGrab(rec, camera); return true; }
    }
    return false;
  }
  function startGrab(rec, camera) {
    // THE BIOMES: the Gallery melts a denied grab in your hand instead of holding it
    if (grabHooks && grabHooks.policy && grabHooks.policy(rec) === 'melt') {
      meltCard(rec);
      if (grabHooks.onMelt) { try { grabHooks.onMelt(rec); } catch (e) { /* ignore */ } }
      return;
    }
    grabbed = rec;
    rec.isGrabbed = true;
    tracker.noteGrab(rec.assetName, rec.assetKind);   // deliberate act: strong "like" signal
    if (grabHooks && grabHooks.onGrab) { try { grabHooks.onGrab(rec); } catch (e) { /* ignore */ } }
    // seed the follow from the card's current offset so it eases in, not snaps
    camera.getWorldDirection(_dir);
    rec.spotDist = Math.max(3, _target.subVectors(rec.group.position, camera.position).dot(_dir));
    _target.copy(camera.position).addScaledVector(_dir, rec.spotDist);
    rec.spotLat = new THREE.Vector3().subVectors(rec.group.position, _target);
  }
  function releaseGrab(silent) {
    if (!grabbed) return;
    // Sticky Fingers capstone: a player release hurls the card down the tube
    // instead of letting it drift (silent releases - spotlight steals, resets -
    // never throw)
    if (!silent && throwOnRelease) { throwHeldCard(throwOnRelease); return; }
    const rec = grabbed;
    rec.isGrabbed = false;
    // hand it back to the conveyor just ahead of the camera so it recedes and
    // despawns normally (its world position is frozen; the camera flies past it)
    rec.depth = camDepthNow + Math.max(2, rec.spotDist || GRAB_DIST);
    grabbed = null;
  }

  // Sticky Fingers: wield a poster RIPPED off the tube wall (wallPosters.ripPoster).
  // The texture is BORROWED - the poster pool owns it, keeps its gif animating,
  // and may hand it to future posters - so this card never disposes it and never
  // Ken-Burns it (mutating repeat/offset would warp every poster sharing the map).
  // Once released it lives as a normal conveyor card and despawns on the cull.
  function grabExternalCard({ tex, aspect = 1, assetName = null, pos = null } = {}, camera) {
    if (!tex || grabbed || spotlight) return false;
    const shell = makeUserCardShell(camDepthNow);
    const rec = {
      group: shell.group, depth: camDepthNow, kind: 'image', billboard: true, tex: null,
      dispose() {
        rec.dead = true;   // borrowed map: NOT ours to dispose
        shell.mat.dispose();
        for (const m of shell.dressed) m.dispose();
      },
    };
    rec.contentMat = shell.mat;
    rec.dressedMats = shell.dressed;
    shell.mat.map = tex;
    shell.mat.needsUpdate = true;
    sizeShell(shell, aspect);
    if (pos) shell.group.position.copy(pos);   // ease in from where it hung on the wall
    shell.group.visible = true;
    tagAsset(rec, assetName, 'image');
    addCard(rec);
    startGrab(rec, camera);
    return grabbed === rec;   // false = the Gallery melted it in your hand
  }

  // Wave 2 (Sticky Fingers capstone): hurl the held card down the tube; the
  // impact callback fires once it's flown well ahead, then it despawns.
  function throwHeldCard(onImpact) {
    if (!grabbed) return false;
    const rec = grabbed;
    grabbed = null;
    rec.isGrabbed = false;
    rec.thrown = true;
    rec.onThrowImpact = onImpact || null;
    rec.depth = camDepthNow + Math.max(2, rec.spotDist || GRAB_DIST);
    const fr = layout.frameAtDepth(rec.depth);
    rec.throwLat = new THREE.Vector3().subVectors(rec.group.position, fr.pos);
    return true;
  }

  // Wave 2 (Sticky Fingers): project the held card's content plane to CSS-pixel
  // screen bounds, so the game can pop the DOM bubbles it sweeps over.
  const _hc = new THREE.Vector3();
  function heldCardScreenRect() {
    if (!grabbed) return null;
    const mesh = grabbed.group.children[2]; // dressCard order: glow, frame, content
    if (!mesh) return null;
    const r = renderer.domElement.getBoundingClientRect();
    if (!r.width || !r.height) return null;
    let minX = 1e9, minY = 1e9, maxX = -1e9, maxY = -1e9;
    for (let cx = -0.5; cx <= 0.5; cx += 1) {
      for (let cy = -0.5; cy <= 0.5; cy += 1) {
        _hc.set(cx, cy, 0);
        mesh.localToWorld(_hc);
        _hc.project(camera);
        if (_hc.z > 1) return null; // behind the camera: no rect
        const px = r.left + ((_hc.x + 1) / 2) * r.width;
        const py = r.top + ((1 - _hc.y) / 2) * r.height;
        if (px < minX) minX = px; if (px > maxX) maxX = px;
        if (py < minY) minY = py; if (py > maxY) maxY = py;
      }
    }
    // inflate ~8% so the paddle's contact feels generous (the card's art has
    // rounded corners, so its true silhouette is a touch inside the plane rect)
    const padX = (maxX - minX) * 0.08, padY = (maxY - minY) * 0.08;
    return { left: minX - padX, top: minY - padY, right: maxX + padX, bottom: maxY + padY };
  }

  // Wave 2 (Private Show): put a video on the stage RIGHT NOW, bypassing
  // approach auto-promotion (which the game disables during live runs).
  // url = null draws a random user video from the media pool. Async: resolves
  // true once the stage is taken, false if there was nothing to show.
  async function forceSpotlight(url, secs, release) {
    if (spotlight) return false;
    if (!url) {
      const entry = media.drawKind('video');
      if (!entry) return false;
      const acq = await entry.acquire();
      if (!acq) return false;
      if (spotlight) { if (acq.release) acq.release(); return false; }
      url = acq.url; release = acq.release;
    }
    const rec = buildVideoCard(camDepthNow + 12, url, { withAudio: true, release });
    if (!rec) return false;
    addCard(rec);
    promoteSpotlight(rec, camera);
    if (secs) rec.clipLeft = Math.max(6, Math.min(60, secs));
    return true;
  }

  // junction chamber span (scene.setBlockedSpan): no card/veil spawns inside it -
  // they'd float in the hidden-trunk cut or hide behind the opaque room, starving
  // the fork approach of grabbables.
  let blockedSpan = null;

  // ---- per-frame update -------------------------------------------------------
  function update(camera, camDepth, dt, t, heat = 1, runTime = 999) {
    camDepthNow = camDepth;
    // Junction rebase: fallNav zeroes its depth at the vein tail (the chosen
    // branch becomes a FRESH loop), but these cursors were minted on the OLD
    // loop's depths. Left alone they sit past the spawn window for the rest of
    // the run - no wall cards, no veils, no spotlights ever again (the "later
    // chambers have no gif cards" bug). A cursor stranded beyond any depth the
    // conveyor could legitimately have minted can only mean the camera jumped
    // backwards - reel it back in.
    if (nextCardDepth > camDepth + SPAWN_AHEAD + CARD_GAP_FAR + CARD_GAP_JITTER) nextCardDepth = camDepth + 30;
    if (nextVeilDepth > camDepth + SPAWN_AHEAD + VEIL_GAP_MAX * VEIL_GAP_EARLY_MULT) nextVeilDepth = camDepth + 70;
    if (nextSpotlightOkAt > camDepth + SPOTLIGHT_GAP) nextSpotlightOkAt = camDepth + 60;
    // keep the next few seconds of user media decoded (runs during spotlights
    // too, so the conveyor restarts with content in hand)
    pumpPrefetch();
    // spawn ahead (budgeted; async media acquires land within a frame or two).
    // The whole conveyor holds its breath while a spotlight plays - or while
    // parked (ESC pause, hidden tab, or the Warren hub idling in game mode: no
    // new video cards behind the menu).
    if (!spotlight && !paused) {
      // Progression gap: wide (sparse) at heat 0, tightening to CARD_GAP deep.
      const gap = CARD_GAP_FAR + (CARD_GAP - CARD_GAP_FAR) * Math.min(1, Math.max(0, heat));
      // Hold the conveyor at the top of the run: keep nextCardDepth pinned just
      // ahead of the camera so opening the gate later doesn't dump a catch-up
      // burst of everything that "should" have spawned during the quiet spell.
      if (runTime < FIRST_CARD_SEC) nextCardDepth = Math.max(nextCardDepth, camDepth + gap);
      let budget = runTime < FIRST_CARD_SEC ? 0 : SPAWN_PER_FRAME;
      while (budget > 0 && nextCardDepth < camDepth + SPAWN_AHEAD &&
             live.length + spawning < MAX_LIVE_CARDS) {
        const d = nextCardDepth;
        nextCardDepth += gap + rand(-CARD_GAP_JITTER, CARD_GAP_JITTER);
        budget -= 1;
        // a junction chamber owns this span - skip it (the stretch short of the
        // fork keeps its cards, so the approach still has things to grab)
        if (blockedSpan && d > blockedSpan.lo - 2 && d < blockedSpan.hi) continue;
        spawning += 1;
        Promise.resolve(spawnCardAt(d)).then((rec) => {
          spawning -= 1;
          if (rec && d > camDepthNow - DESPAWN_BEHIND && !spotlight) addCard(rec);
          else if (rec) { try { rec.dispose(); } catch (e) { /* ignore */ } }
        }).catch(() => { spawning -= 1; });
      }
      // veils on their own, sparser cadence - and held back even further than the
      // cards: none until FIRST_VEIL_SEC, and wider spacing early (heat 0) that
      // eases to the baseline gap deep.
      const veilGapMult = VEIL_GAP_EARLY_MULT + (1 - VEIL_GAP_EARLY_MULT) * Math.min(1, Math.max(0, heat));
      if (runTime < FIRST_VEIL_SEC) {
        nextVeilDepth = Math.max(nextVeilDepth, camDepth + VEIL_GAP_MIN);
      } else if (nextVeilDepth < camDepth + SPAWN_AHEAD && liveVeils < MAX_LIVE_VEILS) {
        if (blockedSpan && nextVeilDepth > blockedSpan.lo - 2 && nextVeilDepth < blockedSpan.hi) {
          nextVeilDepth = blockedSpan.hi + 5;   // no veils inside the chamber span
        } else {
          addCard(buildVeil(nextVeilDepth));
          nextVeilDepth += rand(VEIL_GAP_MIN, VEIL_GAP_MAX) * veilGapMult;
        }
      }
    }

    videoWatch += dt;
    const kickVideos = videoWatch >= 1;
    if (kickVideos) videoWatch = 0;

    // progressive hypnotic twirl: the deeper into the run, the more the user's
    // photo/gif wall cards slowly rotate in place. Ease-in so it barely stirs
    // early and builds into a lazy swirl - never the held card or a video.
    const swirlEase = Math.min(1, runTime / SWIRL_RAMP_SEC);
    const swirlRate = SWIRL_MAX_RATE * swirlEase * swirlEase;

    for (let i = live.length - 1; i >= 0; i--) {
      const rec = live[i];

      // throttled video-texture upload (autoUpdate is off): push a fresh frame
      // to the GPU only a few times/sec instead of every render, and less often
      // still while the card is far/small on screen. Runs before the
      // spotlight/grab branches so it applies to a card in any state.
      if (rec.vtex && rec.videoEl && !rec.videoEl.paused && !paused && !document.hidden) {
        rec.texAcc += dt;
        let iv = VIDEO_TEX_INTERVAL;
        if (!rec.isSpotlight) {
          const dv = camera.position.distanceTo(rec.group.position);
          if (dv > VIDEO_TEX_FAR) iv = VIDEO_TEX_INTERVAL * 3;
          else if (dv > VIDEO_TEX_MID) iv = VIDEO_TEX_INTERVAL * 1.75;
        }
        if (rec.texAcc >= iv) { rec.texAcc = 0; rec.vtex.needsUpdate = true; }
      }

      // the spotlight rides with the POV instead of scrolling past
      if (rec.isSpotlight) {
        camera.getWorldDirection(_dir);
        rec.spotDist += (SPOTLIGHT_DIST - rec.spotDist) * Math.min(1, dt * 1.8);
        rec.spotLat.multiplyScalar(Math.exp(-dt * 2.2));
        rec.group.position.copy(camera.position)
          .addScaledVector(_dir, rec.spotDist)
          .add(rec.spotLat);
        rec.group.quaternion.copy(camera.quaternion);
        rec.depth = camDepth; // pinned: never crosses the despawn line

        if (!paused && !document.hidden && rec.videoEl && !rec.videoEl.paused) rec.clipLeft -= dt;
        // full stage volume, fading over the clip's last seconds
        const fadeOut = Math.max(0, Math.min(1, rec.clipLeft / SPOTLIGHT_FADE_S));
        const want = isMuted() || paused ? 0 : getLevel('video') * fadeOut;
        rec.vol += (want - rec.vol) * Math.min(1, dt * 3);
        setVideoVolume(rec, rec.vol);
        // keep-alive: distance rules don't apply on stage
        if (kickVideos && !paused && !document.hidden && rec.videoEl && rec.videoEl.paused) {
          const p = rec.videoEl.play(); if (p && p.catch) p.catch(() => {});
        }
        // stuck-stage bailout: a video that won't play (dead decoder, bad file)
        // must never hold the conveyor hostage - clipLeft only ticks while
        // playing, so 6s of stage silence force-ends the feature instead.
        if (!paused && !document.hidden && rec.videoEl && rec.videoEl.paused) {
          rec.spotStall = (rec.spotStall || 0) + dt;
          if (rec.spotStall > 6) rec.clipLeft = 0;
        } else rec.spotStall = 0;
        if (rec.clipLeft <= 0) {
          rec.fade -= dt / SPOTLIGHT_FADE_V;
          setCardFade(rec, Math.max(0, rec.fade));
          if (rec.fade <= 0) { endSpotlight(i); continue; }
        }
        continue;
      }

      // THE BIOMES: a melting card (Gallery-denied grab) squashes, droops and
      // fades where it was seized, then despawns - it never joins the conveyor.
      if (rec.melting) {
        rec.melting.t += dt;
        const k = Math.min(1, rec.melting.t / rec.melting.dur);
        rec.group.scale.set(1 + k * 0.25, Math.max(0.03, 1 - k * 1.05), 1); // squash wide, drip down
        rec.group.position.y -= dt * 2.2;
        if (rec.contentMat) setCardFade(rec, 1 - k);
        if (k >= 1) { removeCard(i); continue; }
      }

      // a grabbed card is a PADDLE: it tracks the cursor through the space, placed
      // on a shell GRAB_DIST ahead of the camera so it stays under the pointer
      // wherever you push the mouse (the look is frozen while held - see fallNav).
      if (rec.isGrabbed) {
        _ndc.set(cursorNx, cursorNy);
        _ray.setFromCamera(_ndc, camera);
        _target.copy(_ray.ray.origin).addScaledVector(_ray.ray.direction, GRAB_DIST);
        rec.group.position.lerp(_target, Math.min(1, dt * 20)); // ease for a little weight
        rec.depth = camDepth; // pinned: never crosses the despawn line while held
        // fall through: billboard + gif/Ken-Burns keep animating below
      }

      // a thrown card (Sticky Fingers capstone) rockets down the tube's center
      if (rec.thrown) {
        rec.depth += THROW_SPEED * dt;
        const fr = layout.frameAtDepth(rec.depth);
        rec.throwLat.multiplyScalar(Math.exp(-dt * 2.5));
        rec.group.position.copy(fr.pos).add(rec.throwLat);
        if (rec.depth > camDepth + THROW_IMPACT_AT) {
          const cb = rec.onThrowImpact;
          removeCard(i);
          if (cb) { try { cb(); } catch (e) { /* ignore */ } }
          continue;
        }
      }

      // pickups: tick lifetime / dash motion on the game's clock; a spent or
      // escaped one reports out via onPickupGone (the missed-it case)
      if (rec.pickup) {
        const pdt = dt * pickupTimeScale;
        rec.ttl -= pdt;
        rec.pulseT += pdt;
        if (rec.pkSpeed) { rec.depth += rec.pkSpeed * pdt; placePickupGroup(rec); }
        rec.group.scale.setScalar(1 + 0.08 * Math.sin(rec.pulseT * 3.2));
        if (rec.ttl <= 0 || rec.depth > camDepth + SPAWN_AHEAD + 30) {
          if (rec.onPickupGone) { try { rec.onPickupGone(rec); } catch (e) { /* ignore */ } }
          removeCard(i); continue;
        }
      }

      // despawn behind the camera
      if (rec.depth < camDepth - DESPAWN_BEHIND) {
        if (rec.pickup && rec.onPickupGone) { try { rec.onPickupGone(rec); } catch (e) { /* ignore */ } }
        removeCard(i); continue;
      }

      if (rec.billboard) rec.group.quaternion.copy(camera.quaternion);
      // hypnotic swirl on the user's photo/gif wall cards - accumulates a twist
      // about the card's own axis, applied AFTER the billboard reset so it reads
      // as an absolute rotation. Each card gets its own sense + speed so the
      // wall churns organically. Skipped for the held card (obviously) and for
      // video/spotlight cards.
      if (swirlRate > 0 && rec.billboard && rec.assetName && rec.assetKind !== 'video' && !rec.isGrabbed) {
        if (rec.twirlDir === undefined) rec.twirlDir = (Math.random() < 0.5 ? -1 : 1) * rand(0.7, 1.3);
        rec.twirlAng = (rec.twirlAng || 0) + rec.twirlDir * swirlRate * dt;
        rec.group.rotateZ(rec.twirlAng);
      }
      if (rec.spin) rec.group.rotation.z += rec.spin * dt;

      // engagement: bank weighted on-screen time for the user asset this card
      // shows - a card that's large + centred (or held) earns far more than one
      // drifting past the edge, so `weighted` reads as attention, not mere dwell.
      if (rec.assetName && rec.group.visible) {
        _av.copy(rec.group.position).project(camera);
        if (_av.z <= 1 && Math.abs(_av.x) <= 1.05 && Math.abs(_av.y) <= 1.05) {
          const center = Math.max(0, 1 - Math.hypot(_av.x, _av.y));
          const size = Math.max(0, Math.min(1, 1 - (rec.depth - camDepth) / 60));
          const weight = center * size;
          if (weight > 0) tracker.noteVisible(rec.assetName, rec.assetKind, dt, weight);
        }
      }

      // veil opacity follows the live settings sliders; fire its effect + snap
      // the instant the camera punches through the plane (once per veil)
      if (rec.veilMat) {
        rec.veilMat.opacity = veilOpacity(rec.veilKind);
        if (!rec.passed && camDepth >= rec.depth) {
          rec.passed = true;
          if (onVeilPass) { try { onVeilPass(rec.veilKind); } catch (e) { /* ignore */ } }
        }
      }

      // animated gif cards: collect the due ones; uploads are budgeted below
      if (rec.gifAnim && !paused && gifDue(rec.gifAnim)) dueGifs.push(rec);

      // Ken Burns drift on user photos
      if (rec.kb) {
        const kb = rec.kb;
        kb.life += dt;
        const u = Math.min(1, kb.life / kb.dur);
        const e = u * u * (3 - 2 * u);
        const s = kb.s0 + (kb.s1 - kb.s0) * e;
        kb.tex.repeat.set(s, s);
        kb.tex.offset.set(
          (kb.ax0 + (kb.ax1 - kb.ax0) * e) * (1 - s),
          (kb.ay0 + (kb.ay1 - kb.ay0) * e) * (1 - s));
      }

      // approach hook (barks): once, just before the camera reaches the card
      if (!rec.approached && camDepth >= rec.depth - AUDIO_NEAR && camDepth < rec.depth) {
        rec.approached = true;
        if (onCardApproach) { try { onCardApproach(rec); } catch (e) { /* ignore */ } }
      }

      // a user video card takes the stage when the camera reaches it - but only
      // one that actually rendered (visible = loadeddata fired). Promoting a
      // never-loaded video used to freeze the WHOLE conveyor: an invisible
      // stage whose clipLeft never counts down (it only ticks while playing)
      // suppresses every card spawn until the run ends.
      if (rec.spotlightable && autoSpotlightOk && !paused && rec.group.visible && !spotlight && camDepth >= rec.depth - 16 && camDepth >= nextSpotlightOkAt) {
        promoteSpotlight(rec, camera);
        continue;
      }

      // proximity audio + keep-alive for video cards
      if (rec.videoEl) {
        const dist = camera.position.distanceTo(rec.group.position);
        if (rec.gainNode) {
          let v = (AUDIO_FAR - dist) / (AUDIO_FAR - AUDIO_NEAR);
          v = Math.max(0, Math.min(1, v)); v = v * v * v;      // steep near-only fade
          rec.vol += (v - rec.vol) * Math.min(1, dt * 4);      // smooth (no zipper)
          const duck = spotlight ? 0.15 : 1; // the stage owns the mix
          setVideoVolume(rec, isMuted() ? 0 : rec.vol * getLevel('video') * duck);
        }
        if (kickVideos && !paused) manageVideo(rec, dist, t);
      }
    }

    // Budgeted gif frame uploads, round-robin so every gif still animates under
    // load but a post-hitch stampede (every gif due at once) can't own a frame.
    // Skipped gifs stay due and catch up next frame via advanceGif's walk.
    if (dueGifs.length) {
      const n = Math.min(GIF_UPLOADS_PER_FRAME, dueGifs.length);
      for (let k = 0; k < n; k++) advanceGif(dueGifs[(gifRotate + k) % dueGifs.length]);
      gifRotate = (gifRotate + n) % 1024;
      dueGifs.length = 0;
    }

    tickDetached(dt); // branch-mouth cards keep animating off the conveyor
  }

  // ---- detached cards (junctions.js hangs one at each branch mouth) ----------
  // A standalone media card that is NOT on the spawn conveyor: the caller owns
  // its transform/parenting (a branch mouth), and the spawner only keeps its
  // pixels animating (gif frames / video texture / Ken Burns) via the detached
  // tick in update(). No despawn, no billboard, no spotlight promotion.
  async function acquireDetachedItem(prefer) {
    if (!media.hasUserMedia()) return null;
    // Mouth prize cards prefer a video (live motion in the opening); wall-dress
    // cards ('still') prefer gifs/photos - a cluster of muted videos is heavy
    // AND prefetchVideo can time out to null, which left walled-off doors bare.
    // Either way, retry a couple of draws so one bad file doesn't strip the card.
    for (let attempt = 0; attempt < 3; attempt++) {
      const entry = prefer === 'still'
        ? ((media.drawKind && media.drawKind('image')) || media.draw())
        : ((media.drawKind && media.drawKind('video')) || media.draw());
      if (!entry) return null;
      const acquired = await entry.acquire();
      if (!acquired) continue;
      let item = null;
      if (entry.kind === 'video') item = await prefetchVideo(acquired);
      else if (/\.gif$/i.test(entry.name || '')) item = await prefetchGifAny(acquired);
      else item = await prefetchImage(acquired);
      if (item) return item;
    }
    return null;
  }

  // opts: { pos:Vector3, quat:Quaternion, scale:number }. Returns a handle the
  // caller parents into the scene; content lands async under handle.group.
  function createDetachedCard(opts = {}) {
    const handle = { group: new THREE.Group(), ready: false, inner: null };
    let disposed = false;
    const applyTransform = () => {
      if (opts.pos) handle.group.position.copy(opts.pos);
      if (opts.quat) handle.group.quaternion.copy(opts.quat);
      handle.group.scale.setScalar(opts.scale || 1);
    };
    applyTransform();
    acquireDetachedItem(opts.prefer).then((item) => {
      if (disposed || !item) { if (item && item.dispose) item.dispose(); return; }
      let built = null;
      if (item.kind === 'video') {
        built = buildVideoCard(camDepthNow, null, { withAudio: false, release: item.release, videoEl: item.videoEl, vtex: item.vtex });
      } else if (item.kind === 'gif') {
        built = buildGifCard(camDepthNow, item);
      } else {
        built = buildImageCard(camDepthNow, item);
      }
      if (!built) { if (item.dispose) item.dispose(); return; }
      built.billboard = false;
      built.group.position.set(0, 0, 0);   // the builder placed it in the tube; re-home under our group
      built.group.quaternion.identity();
      handle.group.add(built.group);
      detached.add(built);
      handle.inner = built;
      handle.ready = true;
    }).catch(() => { /* ignore: mouth just shows no card */ });
    handle.setTransform = (pos, quat) => { if (pos) opts.pos = pos; if (quat) opts.quat = quat; applyTransform(); };
    // the content plane (dressCard order: glow[0], frame[1], content[2]) - used
    // by the junction shatter to sample the live texture + size.
    handle.getContent = () => (handle.inner && handle.inner.group && handle.inner.group.children[2]) || null;
    handle.hide = () => { handle.group.visible = false; };
    handle.dispose = () => {
      disposed = true;
      if (handle.inner) { detached.delete(handle.inner); try { handle.inner.dispose(); } catch (e) { /* ignore */ } }
      if (handle.group.parent) handle.group.parent.remove(handle.group);
    };
    return handle;
  }

  // Keep detached-card pixels flowing (called from update()): video-texture
  // uploads + gif frame advance + Ken Burns, with none of the conveyor logic.
  function tickDetached(dt) {
    if (paused || document.hidden) return;
    for (const rec of detached) {
      if (rec.vtex && rec.videoEl && !rec.videoEl.paused) {
        rec.texAcc += dt;
        if (rec.texAcc >= VIDEO_TEX_INTERVAL) { rec.texAcc = 0; rec.vtex.needsUpdate = true; }
      }
      if (rec.gifAnim && gifDue(rec.gifAnim)) advanceGif(rec);
      if (rec.kb) {
        const kb = rec.kb;
        kb.life += dt;
        const u = Math.min(1, kb.life / kb.dur);
        const e = u * u * (3 - 2 * u);
        const s = kb.s0 + (kb.s1 - kb.s0) * e;
        kb.tex.repeat.set(s, s);
        kb.tex.offset.set(
          (kb.ax0 + (kb.ax1 - kb.ax0) * e) * (1 - s),
          (kb.ay0 + (kb.ay1 - kb.ay0) * e) * (1 - s));
      }
    }
  }

  // Clear the conveyor cards (rebase: content was placed on the old spine) while
  // leaving the prefetch pool + junction-owned detached cards intact.
  function clearLive() {
    for (let i = live.length - 1; i >= 0; i--) removeCard(i);
    spotlight = null;
    if (grabbed) { grabbed.isGrabbed = false; grabbed = null; }
  }

  function reset(camDepth) {
    for (let i = live.length - 1; i >= 0; i--) removeCard(i);
    spotlight = null;
    grabbed = null;
    nextSpotlightOkAt = camDepth + 60;
    nextCardDepth = camDepth + 30;
    nextVeilDepth = camDepth + 70;
    camDepthNow = camDepth;
  }

  // ESC pause: park every live video; the scene stops calling update() too.
  function setPaused(v) {
    paused = !!v;
    for (const rec of live) {
      if (!rec.videoEl) continue;
      if (paused) { try { rec.videoEl.pause(); } catch (e) { /* ignore */ } }
      else if (rec.isSpotlight) { const p = rec.videoEl.play(); if (p && p.catch) p.catch(() => {}); }
      // other cards recover on their own via manageVideo within a second
    }
  }

  function dispose() {
    window.removeEventListener('pointermove', onCursorMove);
    for (let i = live.length - 1; i >= 0; i--) removeCard(i);
    for (const item of ready) { try { item.dispose(); } catch (e) { /* ignore */ } }
    ready.length = 0;
    unitPlane.dispose();
    roundMask.dispose();
    metalFrameTex.dispose();
    glowTex.dispose();
    // the shared AudioContext (audioBus.js) is closed by scene.dispose
    if (gifWorker) { try { gifWorker.terminate(); } catch (e) { /* ignore */ } gifWorker = null; gifJobs.clear(); }
  }

  // Panel diagnostics: a live pipeline snapshot, always available (unlike the
  // ?e2e-gated window.__sf) - this is what the phone shows in the gear panel.
  window.__sfPipe = () => ({
    ready: ready.length, inflight: prefetching, live: live.length,
    liveVideos, workerDead: gifWorkerDead,
  });

  return {
    update, reset, dispose, setPaused, warm,
    grabAtPointer, releaseGrab, probeGrab, grabExternalCard,
    setGrabHooks, setMirror, meltAll,
    setBlockedSpan: (s) => { blockedSpan = s || null; },
    // junction branch-mouth media cards
    createDetachedCard, clearLive,
    // Wave 2 game verbs
    spawnPickup, clearPickups,
    setPickupTimeScale: (f) => { pickupTimeScale = Math.max(0, f == null ? 1 : f); },
    heldCardScreenRect,
    // engagement tracking: attribute paddle hits to the held asset + drain the
    // accumulated per-asset delta for the run brain to post home.
    grabbedAssetName: () => (grabbed ? grabbed.assetName || null : null),
    notePaddleInteraction: (counts) => {
      if (grabbed && grabbed.assetName) tracker.noteInteraction(grabbed.assetName, counts);
    },
    drainAssetStats: () => tracker.drain(),
    hasAssetStats: () => tracker.hasPending(),
    // a wall-poster hold is the same deliberate "like" as grabbing a card, routed
    // through the same tracker so it drains home on the existing path.
    noteLike: (name, kind) => { if (name) tracker.noteGrab(name, kind || 'image'); },
    setThrowOnRelease: (cb) => { throwOnRelease = cb || null; },
    forceSpotlight,
    setAutoSpotlight: (on) => { autoSpotlightOk = !!on; },
    isGrabbing: () => !!grabbed,
    liveCount: () => live.length,
    liveKinds: () => live.map((r) => r.kind),
    liveVideoCount: () => liveVideos,
    prefetchCount: () => ready.length,
    spotlightActive: () => !!spotlight,
    // Cut the current spotlight short: drop its countdown so the normal
    // clipLeft<=0 path fades it out (audio + visual) and gates the next one.
    skipSpotlight: () => { if (spotlight) { spotlight.clipLeft = 0; return true; } return false; },
    spotlightInfo: () => spotlight ? {
      clipLeft: spotlight.clipLeft, fade: spotlight.fade, dist: spotlight.spotDist,
      paused: spotlight.videoEl.paused, muted: spotlight.videoEl.muted,
      ready: spotlight.videoEl.readyState, t: spotlight.videoEl.currentTime,
    } : null,
  };
}
