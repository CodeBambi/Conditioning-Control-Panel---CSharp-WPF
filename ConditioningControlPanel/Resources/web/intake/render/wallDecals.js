/* ============================================================================
 * wallDecals.js — the intake tube's wall dressing: from Section 2 onward,
 * stretches of the bore are PLASTERED with the user's own gifs.
 *
 * Straight port of DtRH's `engine/wallPosters.js` (the Four Chambers wall
 * dressing) onto the intake's simpler bore, keeping every technique that made
 * it cheap there:
 *   - flat, UNLIT quads flush on the wall, facing the axis (one draw each, no
 *     lighting, no post pass);
 *   - a RECYCLING slot pool — a quad that scrolls past the camera is retired
 *     and its slot reused, so a fixed budget dresses an endless fall;
 *   - a SMALL SHARED TEXTURE POOL decoded once and handed round-robin to the
 *     slots, so "plaster dozens of quads" costs "sample from ~14 textures";
 *   - gifs decoded OFF-THREAD by DtRH's `gifWorker.js` into small ImageBitmaps
 *     and cycled at POOL level (a gif cannot be a three.js texture directly:
 *     canvas drawImage always takes a gif's first frame per the HTML spec), so
 *     every slot sharing a texture animates in sync for one redraw. Gifs past
 *     the animated cap join as first-frame stills; no worker -> stills only.
 *
 * What is NEW here is the LAYOUT. DtRH sprays posters uniformly at a per-region
 * density; the intake wants SECTIONS — a stretch of plastered wall, then clean
 * tube, then another stretch. So the layer plans the bore ahead of the camera as
 * an alternating run of GAP and SECTION spans (both lengths rolled per-run), and
 * only sections get quads. Band drives the three knobs:
 *
 *   Section 1 Calibration  — nothing at all (a clean, clinical bore)
 *   Section 2 Establishing — a small patch every so often, long clean runs
 *   Section 3 Deepening    — noticeably more: bigger patches, shorter gaps
 *   Section 4 Climax       — maximum saturation: long patches, barely any gap
 *   Section 5 Recovery     — no new sections + the live ones fade off the wall,
 *                            so the tube is scrubbed clean as you come back up
 *
 * `beat.depth` (0..1) rides on top as a gentle multiplier so density still
 * breathes WITHIN a band, exactly like the tube's own dressing.
 *
 * No user media -> the layer simply stays empty (same graceful nothing DtRH
 * does). Reduced motion -> the caller never builds it at all.
 *
 * IMPORTANT: this module has NO top-level imports — three.js is handed in by
 * background.js from its own lazy `import('three')`, so importing this file can
 * never drag the vendor bundle into module load. Same guarantee background.js
 * makes for itself.
 * ==========================================================================*/

/* --- budgets ---------------------------------------------------------------
 * Everything the layer can ever cost is bounded here. Mobile roughly halves it;
 * the near field carries the look and the fog swallows the rest, so Climax still
 * reads "plastered" without wrecking fill-rate in a WebView2 that is also
 * running the DOM effect layer, the audio graph and the beat stage. */
const BUDGET = {
  desktop: { slots: 40, pool: 14, anim: 6, texMax: 320 },
  mobile:  { slots: 16, pool: 8,  anim: 3, texMax: 224 },
};
const POOL_INFLIGHT = 2;          // concurrent decodes while filling the pool
const ANIM_MAX_FRAMES = 18;       // frames kept per gif (long gifs loop their opening)
const ANIM_UPLOADS_PER_TICK = 2;  // texture redraws per anim tick (~20fps)

/* --- layout knobs per band --------------------------------------------------
 * `perUnit` is quads per world-unit of section length; `sec`/`gap` are the
 * rolled span lengths in world units. Calibration/Recovery are absent on
 * purpose — an unlisted band plans nothing (see planAhead). */
const BAND_LAYOUT = {
  establishing: { perUnit: 0.45, sec: [5, 10],  gap: [55, 95] },
  deepening:    { perUnit: 0.75, sec: [8, 15],  gap: [22, 45] },
  climax:       { perUnit: 1.15, sec: [12, 20], gap: [6, 16] },
};

const RETIRE_Z = 9;         // past this (behind the camera) a slot is recycled
const PLAN_Z = -128;        // plan the bore out to here (just inside the far lip)
const WALL_INSET = 0.18;    // how far inside the tube radius a decal sits
const BASE_OPACITY = 0.94;
const FADE_OUT_SEC = 2.5;   // Recovery: how long the wall takes to scrub clean

const rand = (a, b) => a + Math.random() * (b - a);
const clamp01 = (n) => (n < 0 ? 0 : n > 1 ? 1 : n);

/**
 * @param {object} o
 * @param {*} o.THREE     the already-loaded three namespace (background.js owns the import)
 * @param {*} o.scene     the background's scene (the decal group is added to it)
 * @param {*} o.renderer  used only for the optional initTexture warm-up
 * @param {object} o.media the intake MediaManifest ({ gifs:[], images:[] } URL lists)
 * @param {string} o.tier 'desktop' | 'mobile'
 * @param {number} o.radius the tube radius decals hug
 */
export function createWallDecals({ THREE, scene, renderer, media, tier, radius }) {
  const B = BUDGET[tier === 'mobile' ? 'mobile' : 'desktop'];

  // The URL deck: gifs first (this layer exists to plaster GIFs), stills only to
  // top the pool up when the user has few or none. Same manifest render/effects.js
  // draws its bursts from — no new sourcing, no hardcoded names.
  const gifUrls = (media && Array.isArray(media.gifs))
    ? media.gifs.filter((u) => typeof u === 'string' && u.length > 0) : [];
  const stillUrls = (media && Array.isArray(media.images))
    ? media.images.filter((u) => typeof u === 'string' && u.length > 0) : [];
  const urls = gifUrls.concat(stillUrls);

  const group = new THREE.Group();
  scene.add(group);

  const unit = new THREE.PlaneGeometry(1, 1);
  const pool = [];              // shared decoded textures { tex, aspect, anim? }
  let poolInflight = 0;
  let poolFilling = false;
  let urlCursor = 0;            // round-robin over a shuffled deck (no immediate repeat)
  let disposed = false;         // decodes resolving after teardown must not resurrect the pool

  const slots = [];             // { mesh, mat, active, z }
  let liveCount = 0;
  let layout = null;            // the live BAND_LAYOUT entry (null = plan nothing)
  let depth = 0;                // last beat depth 0..1 (density breathes within a band)
  let fade = 1;                 // 1 normally; eases to 0 through Recovery
  let fading = false;
  let frontier = PLAN_Z;        // far edge of the planned bore; scrolls with the wall
  let nextIsSection = false;    // spans alternate gap / section

  // shuffled URL deck so two runs never plaster the same wall in the same order
  const deck = urls.slice();
  for (let i = deck.length - 1; i > 0; i--) {
    const j = (Math.random() * (i + 1)) | 0;
    const t = deck[i]; deck[i] = deck[j]; deck[j] = t;
  }

  /* --- shared texture pool -------------------------------------------------- */
  function mkTex(c) {
    const tex = new THREE.CanvasTexture(c);
    tex.colorSpace = THREE.SRGBColorSpace;
    tex.generateMipmaps = false;
    tex.minFilter = THREE.LinearFilter;
    tex.magFilter = THREE.LinearFilter;
    if (renderer) { try { renderer.initTexture(tex); } catch (_e) { /* ignore */ } }
    return tex;
  }
  function mkCanvas(w, h) {
    const shrink = Math.min(1, B.texMax / Math.max(w, h));
    const c = document.createElement('canvas');
    c.width = Math.max(2, Math.round(w * shrink));
    c.height = Math.max(2, Math.round(h * shrink));
    return c;
  }

  /* --- gif decode worker ----------------------------------------------------
   * DtRH's off-thread decoder reused in place (same origin under ccp.game, so
   * the sibling path resolves; a standalone/file:// run just fails the spawn and
   * every gif falls back to a first-frame still). Frames arrive as ready-to-draw
   * ImageBitmaps — the main thread never blocks on an LZW decode. */
  let gifWorker = null, gifWorkerDead = false, gifJobId = 0;
  const gifJobs = new Map(); // id -> { frame(msg), done(), fail() }
  function failGifWorker() {
    gifWorkerDead = true;
    for (const job of gifJobs.values()) job.fail('worker dead');
    gifJobs.clear();
    if (gifWorker) { try { gifWorker.terminate(); } catch (_e) { /* ignore */ } gifWorker = null; }
  }
  function ensureGifWorker() {
    if (gifWorkerDead) return null;
    if (gifWorker) return gifWorker;
    if (typeof Worker !== 'function' || typeof OffscreenCanvas !== 'function'
      || typeof createImageBitmap !== 'function') { gifWorkerDead = true; return null; }
    try {
      gifWorker = new Worker(new URL('../../dtrh/engine/gifWorker.js', import.meta.url), { type: 'module' });
    } catch (_e) { gifWorkerDead = true; return null; }
    gifWorker.onmessage = (e) => {
      const m = e.data;
      const job = gifJobs.get(m.id);
      if (!job) { // job cancelled: frames may still be in flight — free them
        if (m.frame) { try { m.frame.bitmap.close(); } catch (_err) { /* ignore */ } }
        return;
      }
      if (m.error) { gifJobs.delete(m.id); job.fail(m.error); }
      else if (m.frame) job.frame(m);
      else if (m.done) { gifJobs.delete(m.id); job.done(); }
    };
    gifWorker.onerror = failGifWorker; // script load / module failure
    return gifWorker;
  }

  // Decode a gif into an ANIMATED pool item. Resolves on the FIRST frame so the
  // wall dresses fast; later frames keep streaming in and the loop just grows.
  // Resolves null on any failure so decodeOne falls back to a still.
  function decodeGifFrames(buf) {
    const w = ensureGifWorker();
    if (!w) return Promise.resolve(null);
    return new Promise((resolve) => {
      const id = ++gifJobId;
      let item = null;
      const watchdog = setTimeout(() => { // a silently-hung job must not wedge the pump
        if (!gifJobs.delete(id)) return;
        try { if (gifWorker) gifWorker.postMessage({ cancel: id }); } catch (_e) { /* ignore */ }
        resolve(null);
      }, 8000);
      gifJobs.set(id, {
        frame(m) {
          if (item) {
            if (item.anim.dead) { try { m.frame.bitmap.close(); } catch (_e) { /* ignore */ } }
            else item.anim.frames.push(m.frame);
            return;
          }
          clearTimeout(watchdog);
          const c = document.createElement('canvas');
          c.width = m.w; c.height = m.h;
          const x = c.getContext('2d');
          x.drawImage(m.frame.bitmap, 0, 0);
          item = {
            tex: mkTex(c), aspect: m.aspect, ctx: x, jobId: id,
            anim: { frames: [m.frame], i: 0, nextAt: performance.now() + m.frame.durMs, dead: false },
          };
          resolve(item);
        },
        done() { clearTimeout(watchdog); if (!item) resolve(null); },
        // failed mid-stream: the decal just loops the frames it already has
        fail() { clearTimeout(watchdog); if (!item) resolve(null); },
      });
      try { w.postMessage({ id, buf, maxDim: B.texMax, maxFrames: ANIM_MAX_FRAMES }); }
      catch (_e) { clearTimeout(watchdog); gifJobs.delete(id); resolve(null); }
    });
  }

  function animCount() {
    let n = 0;
    for (const item of pool) if (item.anim) n += 1;
    return n;
  }

  // Decode one manifest URL into a downscaled texture. A gif within the animated
  // budget becomes a frame-cycled texture via the worker; everything else is a
  // single first-frame snapshot (near-zero ongoing cost).
  async function decodeOne() {
    if (!deck.length) return null;
    const url = deck[urlCursor % deck.length];
    urlCursor += 1;
    try {
      const blob = await (await fetch(url)).blob();
      const animated = blob.type === 'image/gif' || /\.gif(\?|$)/i.test(url);
      if (animated && animCount() < B.anim) {
        const item = await decodeGifFrames(await blob.arrayBuffer());
        if (item) return item; // worker failed: fall through to a still
      }
      let bmp = null;
      try { bmp = await createImageBitmap(blob); } // gif blob -> first frame
      catch (_e) { return null; } // hosted WebView2 has createImageBitmap; bail otherwise
      const w = bmp.width, h = bmp.height;
      const c = mkCanvas(w, h);
      const x = c.getContext('2d');
      x.imageSmoothingQuality = 'high';
      x.drawImage(bmp, 0, 0, c.width, c.height);
      bmp.close();
      return { tex: mkTex(c), aspect: w / h };
    } catch (_e) { return null; }
  }

  // Trickle the pool up to its ceiling (called the moment a band wants decals).
  // A couple in flight at a time so a band change never fires a dozen decodes.
  function fillPool() {
    if (poolFilling || !deck.length) return;
    poolFilling = true;
    const pump = () => {
      if (disposed) { poolFilling = false; return; }
      while (pool.length + poolInflight < B.pool && poolInflight < POOL_INFLIGHT) {
        poolInflight += 1;
        decodeOne().then((item) => {
          poolInflight -= 1;
          if (disposed) { if (item) freeItem(item); return; }
          if (item) pool.push(item);
          pump();
        }).catch(() => { poolInflight -= 1; pump(); });
      }
      if (pool.length >= B.pool || (!pool.length && !poolInflight && urlCursor >= deck.length)) {
        poolFilling = false; // full, or the whole deck failed to decode — stop pumping
      }
    };
    pump();
  }

  function anyTex() {
    return pool.length ? pool[(Math.random() * pool.length) | 0] : null;
  }

  // Advance animated decals to the frame that should be showing NOW (a hitch
  // skips frames instead of playing slow-mo). Pool-level, so many slots sharing
  // a texture animate in sync for ONE redraw. Throttled to ~20fps with a small
  // per-tick upload budget; the round-robin cursor keeps every gif moving.
  let poolAnimT = 0;
  let animRotate = 0;
  const _due = [];
  function tickPool(dt) {
    poolAnimT += dt;
    if (poolAnimT < 0.05) return;
    poolAnimT = 0;
    const t = performance.now();
    _due.length = 0;
    for (const item of pool) {
      const a = item.anim;
      if (a && !a.dead && a.frames.length > 1 && t >= a.nextAt) _due.push(item);
    }
    if (!_due.length) return;
    const n = Math.min(ANIM_UPLOADS_PER_TICK, _due.length);
    for (let k = 0; k < n; k++) {
      const item = _due[(animRotate + k) % _due.length];
      const a = item.anim;
      let guard = a.frames.length * 2; // a long stall resyncs below instead of walking forever
      while (t >= a.nextAt && guard-- > 0) {
        a.i = (a.i + 1) % a.frames.length;
        a.nextAt += a.frames[a.i].durMs;
      }
      if (t >= a.nextAt) a.nextAt = t + a.frames[a.i].durMs;
      try { item.ctx.drawImage(a.frames[a.i].bitmap, 0, 0); item.tex.needsUpdate = true; }
      catch (_e) { /* ignore */ }
    }
    animRotate += n;
    _due.length = 0;
  }

  /* --- slots ---------------------------------------------------------------- */
  function makeSlot() {
    const mat = new THREE.MeshBasicMaterial({
      map: null, transparent: true, opacity: BASE_OPACITY,
      side: THREE.DoubleSide, depthWrite: false, toneMapped: false,
    });
    const mesh = new THREE.Mesh(unit, mat);
    mesh.visible = false;
    group.add(mesh);
    const slot = { mesh, mat, active: false, z: 0 };
    slots.push(slot);
    return slot;
  }
  function freeSlot(slot) {
    slot.active = false;
    slot.mesh.visible = false;
    slot.mat.map = null;
    liveCount -= 1;
  }

  // Paste one decal flush on the wall at `z`. The bore is a straight cylinder on
  // -Z, so orientation is fixed at placement: look at the axis point level with
  // the decal (face points inward), then roll for a bit of collage tilt.
  function place(slot, z) {
    const item = anyTex();
    if (!item) return false;
    const angle = rand(0, Math.PI * 2);
    const r = radius - WALL_INSET - rand(0, 0.16);
    slot.z = z;
    slot.mesh.position.set(Math.cos(angle) * r, Math.sin(angle) * r, z);
    slot.mesh.lookAt(0, 0, z);
    slot.mesh.rotateZ(rand(-0.45, 0.45));
    // size: keep the source aspect, scaled to a chunky wall tile
    const base = rand(2.4, 4.2);
    const asp = item.aspect || 1;
    slot.mesh.scale.set(asp >= 1 ? base : base * asp, asp >= 1 ? base / asp : base, 1);
    slot.mat.map = item.tex;
    slot.mat.needsUpdate = true;
    slot.mesh.visible = true;
    slot.active = true;
    liveCount += 1;
    return true;
  }

  /* --- the plan ------------------------------------------------------------
   * The bore ahead of the camera is planned as alternating GAP / SECTION spans.
   * `frontier` is the far edge of what has been planned; it scrolls toward the
   * camera with the wall, and whenever it drifts inside PLAN_Z we roll one more
   * span beyond it. Quads only ever exist inside a section span, which is what
   * makes the wall read as patches instead of a uniform coating. */
  function planAhead() {
    let guard = 8; // never plan more than a handful of spans in one frame
    while (frontier > PLAN_Z && guard-- > 0) {
      if (!layout) { frontier -= 60; nextIsSection = false; continue; } // bare band: pure gap
      const span = nextIsSection ? rand(layout.sec[0], layout.sec[1]) : rand(layout.gap[0], layout.gap[1]);
      const z1 = frontier, z0 = frontier - span;
      if (nextIsSection && pool.length) {
        // density breathes within the band with beat depth (0.75x .. 1.25x)
        const want = Math.round(span * layout.perUnit * (0.75 + 0.5 * depth));
        const room = B.slots - liveCount;
        const n = Math.max(0, Math.min(want, room));
        for (let i = 0; i < n; i++) {
          const slot = slots.find((s) => !s.active) || makeSlot();
          if (!place(slot, rand(z0, z1))) break;
        }
      }
      frontier = z0;
      nextIsSection = !nextIsSection;
    }
    if (frontier > PLAN_Z) frontier = PLAN_Z; // guard tripped: don't let the plan lag forever
  }

  /* --- public API ---------------------------------------------------------- */
  return {
    /** Band.* -> the layout knobs. Unknown/Calibration plans nothing; Recovery
     *  additionally scrubs the live wall clean over ~2.5s. */
    setBand(band) {
      if (disposed) return;
      layout = BAND_LAYOUT[band] || null;
      fading = (band === 'recovery');
      if (!fading) fade = 1;
      if (layout) fillPool();
    },

    /** Latest beat depth 0..1 — a gentle density multiplier within the band. */
    setDepth(d) {
      if (disposed) return;
      depth = clamp01(typeof d === 'number' ? d : 0);
    },

    /** One frame. `travel` is how far (world units) the wall moved toward the
     *  camera this frame — background.js derives it from the SAME scroll speed
     *  the shader uses, so decals ride the rings instead of sliding against
     *  them. `far` is the live fog distance, used to fade decals in out of the
     *  murk exactly as the shader fades the wall behind them. */
    update(dt, travel, far) {
      if (disposed) return;
      tickPool(dt);
      if (fading && fade > 0) fade = Math.max(0, fade - dt / FADE_OUT_SEC);
      frontier += travel;
      for (const slot of slots) {
        if (!slot.active) continue;
        slot.z += travel;
        if (slot.z > RETIRE_Z || (fading && fade <= 0)) { freeSlot(slot); continue; }
        slot.mesh.position.z = slot.z;
        // match the shader's distance fade (col = mix(col, fog, f*f)) so a decal
        // melts into the murk with the wall it is pasted on, never popping in.
        const f = clamp01(-slot.z / (far || 119));
        slot.mat.opacity = BASE_OPACITY * (1 - f * f) * fade;
      }
      planAhead();
    },

    /** Release every texture, frame bitmap, material and the geometry. Idempotent. */
    dispose() {
      if (disposed) return;
      disposed = true;
      for (const slot of slots) { try { slot.mat.dispose(); } catch (_e) { /* ignore */ } }
      slots.length = 0;
      for (const item of pool) freeItem(item);
      pool.length = 0;
      if (gifWorker) { try { gifWorker.terminate(); } catch (_e) { /* ignore */ } gifWorker = null; }
      gifJobs.clear();
      try { scene.remove(group); } catch (_e) { /* ignore */ }
      try { unit.dispose(); } catch (_e) { /* ignore */ }
    },
  };

  // Free one pool entry: the texture plus, for an animated one, every cached
  // frame bitmap and any decode still streaming in from the worker.
  function freeItem(item) {
    try { item.tex.dispose(); } catch (_e) { /* ignore */ }
    const a = item.anim;
    if (!a) return;
    a.dead = true;
    if (gifJobs.delete(item.jobId) && gifWorker) {
      try { gifWorker.postMessage({ cancel: item.jobId }); } catch (_e) { /* ignore */ }
    }
    for (const f of a.frames) { try { f.bitmap.close(); } catch (_e) { /* ignore */ } }
    a.frames.length = 0;
  }
}
