/* ============================================================================
 * payloadFx.js - in-world effect renderer for the DtRH browser game.
 *
 * The hard cutover (2026-07): a popped/detonated bubble's effect used to be
 * fired over the bridge (`fire-payload`) as a NATIVE WPF layered window on top
 * of the fullscreen game. Those WS_EX_LAYERED topmost windows were the root of
 * the freeze/OOM cluster (#431/#448/#451/#457/#476/#486). Since the WebView2
 * already covers the screen, the desktop is never visible during a run - so the
 * effect renders right here in the page (GPU-composited, off the WPF render
 * thread) with no visual loss.
 *
 * Every effect is a TIMED burst (mirroring EffectPayload.cs): strength (0..100,
 * from bubble size) scales intensity; durationMult stretches the on-screen time
 * (freeze / slow-mo / detonation). Sustained-looking overlays (spiral / pink /
 * braindrain) are ONE reused element per kind that just refreshes its fade-out
 * deadline, so overlapping pops never pile up DOM.
 *
 * Effects render as DOM under #sf-hud (never touching the shared fx state that
 * the weather/boon systems own - only the transient fx.pulseFlash glare is
 * borrowed). Mandatory VIDEO stays native (handled separately); AUDIO stays on
 * the `sfx` bridge. Everything else lives here.
 * ==========================================================================*/

import { S } from '../engine/settings.js';
import { isMuted, onMuteChange } from '../shared/audioMute.js';
import { pickSpiralUrl } from '../engine/loomSpirals.js';

const clamp = (v, lo, hi) => Math.min(hi, Math.max(lo, v));
const clamp01 = (v) => clamp(v, 0, 1);
// Strength 0..100 mapped onto [min,max] - the JS twins of EffectPayload.Scale/ScaleD.
const scale = (min, max, s) => min + Math.round((max - min) * clamp(s, 0, 100) / 100);
const scaleD = (min, max, s) => min + (max - min) * clamp(s, 0, 100) / 100;
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

// bundled effect sprites - the fallback imagery when the user's pool is empty
const SPRITE = (n) => `/dtrh/assets/bubbles/effects/${n}.png`;
const FALLBACK_SPRITES = [SPRITE('flash'), SPRITE('spiral'), SPRITE('pinkfilter'), SPRITE('glitch')];
// a small built-in whisper pool for word flashes when no subliminal text is piped
const WORDS = ['good girl', 'drop', 'sink', 'obey', 'deeper', 'blank', 'melt', 'pink', 'empty', 'yes'];
const pick = (a) => a[(Math.random() * a.length) | 0];

// hard caps so a dense wave can never flood the DOM with clips/flashes
const MAX_FLASH = 6;
const MAX_CASCADE = 14;

export function createPayloadFx({ hud, fx, media, flashBurst }) {
  // Two layers: washes/bursts render BEHIND the bubble field (.cf-layer, z6) so
  // bubbles stay crisp and clickable on top of pink/spiral/glitch/braindrain;
  // the video card rides a FRONT layer above the bubbles (in front of the POV).
  const root = document.createElement('div');
  root.className = 'sf-pfx';
  hud.appendChild(root);
  const front = document.createElement('div');
  front.className = 'sf-pfx-front';
  hud.appendChild(front);

  // sustained overlays: one persistent element per kind, opacity-toggled
  const holds = {};   // kind -> { el, hideTimer }
  const loops = new Set();   // active rAF loops (cascade / bouncer) for teardown
  const cascades = new Set(); // the gif-rain subset of loops (room arrival cuts these)
  let liveFlash = 0, liveCascade = 0;
  let videoCardEl = null;    // the single live video card (one at a time)
  let videoCardCancel = null; // early-out for the live card (room arrival)
  let heavyGen = 0;          // bumped by cancelHeavy; a card acquired across a bump is stale
  let disposed = false;

  /** Draw a URL from the user's image pool (null when the pool is empty). */
  async function pickImageUrl() {
    try {
      if (!media || !media.hasUserMedia || !media.hasUserMedia()) return null;
      const p = media.drawKind ? media.drawKind('image') : null;
      if (!p) return null;
      const got = await p.acquire();
      return got && got.url ? got.url : null;
    } catch { return null; }
  }
  // an image url for washes/cascades: user media first, bundled sprite otherwise
  const anyImageUrl = async () => (await pickImageUrl()) || pick(FALLBACK_SPRITES);

  // ---- sustained overlays (spiral / pink / braindrain) -----------------------
  function ensureHold(kind, cls) {
    let h = holds[kind];
    if (!h) {
      const el = document.createElement('div');
      el.className = 'sf-pfx-layer ' + cls;
      root.appendChild(el);
      h = holds[kind] = { el, hideTimer: 0 };
    }
    return h;
  }
  // Snap a hold on at `opacity`, then fade it out after `durMs` (a fresh pop
  // just refreshes the deadline instead of stacking a second element).
  function holdOn(kind, cls, opacity, durMs) {
    const h = ensureHold(kind, cls);
    if (h.hideTimer) { clearTimeout(h.hideTimer); h.hideTimer = 0; }
    h.el.style.opacity = String(opacity);
    h.hideTimer = setTimeout(() => { if (!disposed) h.el.style.opacity = '0'; h.hideTimer = 0; }, durMs);
    return h;
  }

  function showSpiral(strength, durMult) {
    const h = holdOn('spiral', 'sf-pfx-spiral', scaleD(0.25, 0.70, strength), scale(1500, 4500, strength) * durMult);
    // Draw from the shared pool (bundled sp1..8 + the player's Loom spirals)
    // instead of the single hardcoded spiral.png in .sf-pfx-spiral - the CSS
    // still owns cover/blend/spin, we only swap the image so in-run spirals vary.
    h.el.style.backgroundImage = `url('${pickSpiralUrl()}')`;
  }
  function showPink(strength, durMult) {
    holdOn('pink', 'sf-pfx-pink', scaleD(0.25, 0.70, strength), scale(1500, 4500, strength) * durMult);
  }
  async function showBraindrain(strength, durMult) {
    // full-screen dim + blur-behind (the iconic "drain") with a faint random-image
    // wash on top - the in-world port of ChaosFlashOverlay + the DWM blur. The
    // element opacity drives the blur/dim; the dark luminosity blend keeps the
    // washed image itself subtle so it reads as drained, not a slideshow.
    const h = holdOn('braindrain', 'sf-pfx-drain', scaleD(0.35, 0.62, strength), scale(1500, 4500, strength) * durMult);
    const url = await anyImageUrl();
    if (!disposed && url) h.el.style.backgroundImage = `url("${url}")`;
  }
  function showGlitch(strength, durMult) {
    // RGB-split + scanline shudder over the drain wash (the "glitch" flavor).
    showBraindrain(strength, durMult);
    const h = ensureHold('braindrain', 'sf-pfx-drain');
    h.el.classList.add('sf-pfx-glitching');
    // How long the shudder holds is now the player-earned 'glitch timer' dial
    // (S.glitchSeconds, 1-8s), still stretched by the bubble's durMult. Capped
    // so a big detonation can't strobe forever.
    const glitchMs = Math.min(12000, clamp(S.glitchSeconds || 2.5, 1, 8) * 1000 * durMult);
    clearTimeout(h._glitchTimer);
    h._glitchTimer = setTimeout(() => { if (!disposed) h.el.classList.remove('sf-pfx-glitching'); }, glitchMs);
    // Keep the drain wash underneath alive at least as long as the shudder, so a
    // long glitch never strobes over an already-faded screen.
    if (h.hideTimer) {
      clearTimeout(h.hideTimer);
      h.hideTimer = setTimeout(() => { if (!disposed) { h.el.style.opacity = '0'; } h.hideTimer = 0; }, glitchMs);
    }
  }

  // ---- transient bursts (flash / subliminal / bambi freeze) ------------------
  async function flash(strength, durMult) {
    try { fx && fx.pulseFlash && fx.pulseFlash(clamp01(0.45 + 0.004 * strength)); } catch { /* engine build without it */ }
    const amount = scale(1, 3, strength);
    // Preferred: the engine's clickable, MULTIPLYING hydra clips (same system the
    // tunnel's flash veils use) - popping one spawns two more. Count comes from the
    // 'flash gifs' slider (S.flashCount). Falls back to the scattered-image burst
    // below only if the engine hook isn't available.
    if (flashBurst) { flashBurst(); return; }
    const dur = scale(900, 2000, strength) * durMult;
    for (let i = 0; i < amount; i++) {
      if (disposed || liveFlash >= MAX_FLASH) break;
      const url = await anyImageUrl();
      if (!url) break;
      const img = document.createElement('img');
      img.className = 'sf-pfx-flash';
      img.style.setProperty('--pfx-dur', dur + 'ms');
      // scatter across the screen like the WPF floating flashes (not dead-center)
      img.style.left = (14 + Math.random() * 72) + 'vw';
      img.style.top = (16 + Math.random() * 64) + 'vh';
      img.style.setProperty('--pfx-rot', (Math.random() * 16 - 8) + 'deg');
      img.src = url;
      liveFlash++;
      const kill = () => { if (img.parentNode) img.remove(); liveFlash = Math.max(0, liveFlash - 1); };
      img.addEventListener('animationend', kill, { once: true });
      setTimeout(kill, dur + 600);   // safety net if animationend never fires
      root.appendChild(img);
      if (i < amount - 1) await sleep(dur * 0.32);
    }
  }

  async function subliminal(strength) {
    const dur = scale(320, 560, strength);   // a quick blip, like FlashSubliminal
    const url = Math.random() < 0.5 ? await pickImageUrl() : null;
    let el;
    if (url) { el = document.createElement('img'); el.src = url; el.className = 'sf-pfx-sub'; }
    else { el = document.createElement('div'); el.className = 'sf-pfx-sub sf-pfx-word'; el.textContent = pick(WORDS); }
    el.style.setProperty('--pfx-dur', dur + 'ms');
    root.appendChild(el);
    setTimeout(() => el.remove(), dur + 300);
  }

  function bambiFreeze() {
    const el = document.createElement('div');
    el.className = 'sf-pfx-freeze';
    const word = document.createElement('span');
    word.textContent = 'BAMBI FREEZE';
    el.appendChild(word);
    root.appendChild(el);
    setTimeout(() => { if (!disposed) el.classList.add('sf-pfx-out'); }, 1200);
    setTimeout(() => el.remove(), 1700);
  }

  // ---- rAF effects (bouncing text / gif cascade) -----------------------------
  function bouncingText(durMult) {
    const el = document.createElement('div');
    el.className = 'sf-pfx-bounce';
    el.textContent = pick(WORDS).toUpperCase();
    root.appendChild(el);
    // DVD-logo drift, recoloring on each wall hit; auto-stops after ~8s.
    let x = 10 + Math.random() * 60, y = 10 + Math.random() * 60;   // vw / vh
    let vx = (Math.random() < 0.5 ? -1 : 1) * 14, vy = (Math.random() < 0.5 ? -1 : 1) * 12; // per-second
    const endAt = performance.now() + 8000 * clamp(durMult, 0.5, 3);
    let last = performance.now(), stop = false;
    const recolor = () => el.style.color = `hsl(${(Math.random() * 360) | 0} 90% 70%)`;
    recolor();
    const step = (now) => {
      if (disposed || stop) return;
      const dt = Math.min(0.05, (now - last) / 1000); last = now;
      x += vx * dt; y += vy * dt;
      if (x < 0) { x = 0; vx = Math.abs(vx); recolor(); } else if (x > 92) { x = 92; vx = -Math.abs(vx); recolor(); }
      if (y < 0) { y = 0; vy = Math.abs(vy); recolor(); } else if (y > 92) { y = 92; vy = -Math.abs(vy); recolor(); }
      el.style.transform = `translate(${x}vw, ${y}vh)`;
      if (now >= endAt) { el.classList.add('sf-pfx-out'); setTimeout(() => el.remove(), 400); return; }
      requestAnimationFrame(step);
    };
    const handle = { cancel: () => { stop = true; el.remove(); } };
    loops.add(handle);
    requestAnimationFrame(step);
    setTimeout(() => loops.delete(handle), 8600 * clamp(durMult, 0.5, 3));
  }

  function gifCascade(durMult) {
    // images/gifs spawn at the top and slide down, growing, click-through - the
    // port of ChaosGifCascadeOverlay (SPAWN_RATE 1.67/s, ~6s window).
    const durationMs = 6000 * clamp(durMult, 0.5, 3);
    const gapMs = 1000 / 1.67;
    const endAt = performance.now() + durationMs;
    let nextAt = performance.now(), stop = false;

    async function spawnOne() {
      if (disposed || stop || liveCascade >= MAX_CASCADE) return;
      const url = await anyImageUrl();
      if (!url || disposed) return;
      const img = document.createElement('img');
      img.className = 'sf-pfx-cascade';
      img.style.left = (Math.random() * 80 + 4) + 'vw';
      img.style.setProperty('--pfx-fall', (2.4 + Math.random() * 1.4) + 's');
      img.src = url;
      liveCascade++;
      const kill = () => { if (img.parentNode) img.remove(); liveCascade = Math.max(0, liveCascade - 1); };
      img.addEventListener('animationend', kill, { once: true });
      setTimeout(kill, 5000);   // safety net
      root.appendChild(img);
    }
    const step = (now) => {
      if (disposed || stop) return;
      while (now >= nextAt && now < endAt) { spawnOne(); nextAt += gapMs; }
      if (now >= endAt) { loops.delete(handle); cascades.delete(handle); return; }
      requestAnimationFrame(step);
    };
    const handle = { cancel: () => { stop = true; } };
    loops.add(handle);
    cascades.add(handle);
    requestAnimationFrame(step);
  }

  // ---- video card (front layer) ----------------------------------------------
  // The in-world port of the mandatory video: the tape RUSHES UP AT THE POV and
  // STICKS there - dead center, way closer than the tube's seated clips - caged
  // in a red, electric, vibrating frame (styles.css .sf-pfx-vidframe). It cannot
  // be clicked away: it holds the full VIDEO_HOLD_SEC and only lets go early
  // when the fall reaches a room (the junction/draft antechamber calls
  // cancelHeavy). Only one at a time; the game keeps running underneath.
  const VIDEO_HOLD_SEC = 15;
  async function videoCard() {
    if (disposed || videoCardEl) return;   // one card at a time
    if (!media || !media.drawKind) return;
    const pickV = media.drawKind('video');
    if (!pickV) return;   // no user videos in the pool - silently skip
    // Snapshot the heavy generation: cancelHeavy() (room arrival / endRun) can fire while
    // we're awaiting the decrypt below, and videoCardCancel isn't wired up yet, so the card
    // would append AFTER the run ended and sit over the recap/hub for its full 15s hold.
    const gen = heavyGen;
    const got = await pickV.acquire();
    if (disposed || videoCardEl || !got || !got.url) return;
    if (gen !== heavyGen) return;   // a cancelHeavy landed during acquire: this card is stale

    const card = document.createElement('div');
    card.className = 'sf-pfx-videocard';
    // the frame carries the vibration + electric arcs so its jitter animation
    // never fights the card's zoom-in transform transition
    const frame = document.createElement('div');
    frame.className = 'sf-pfx-vidframe';
    const vid = document.createElement('video');
    vid.src = got.url;
    // the master mute is PARAMOUNT: the card is born muted when the HUD mute is
    // on, and it tracks live flips for its whole 15s hold (audioMute.js also
    // runs a page-level enforcer, but the card should behave on its own)
    vid.loop = true; vid.playsInline = true; vid.autoplay = true; vid.muted = isMuted();
    const offMute = onMuteChange((m) => { try { vid.muted = m; } catch { /* ignore */ } });
    frame.appendChild(vid);
    card.appendChild(frame);
    front.appendChild(card);
    videoCardEl = card;
    // autoplay-with-sound is allowed by the host flag; fall back to muted if blocked
    const play = () => { const p = vid.play(); if (p && p.catch) p.catch(() => { vid.muted = true; vid.play().catch(() => {}); }); };
    play();

    let removed = false;
    const remove = () => {
      if (removed) return; removed = true;
      offMute();
      card.classList.remove('in');            // recede back into the deep
      try { vid.pause(); } catch { /* ignore */ }
      // explicit unload: Chromium's per-page media-player cap counts a merely
      // .remove()d element's decoder until GC (same pattern as bubbles.js)
      setTimeout(() => { try { vid.removeAttribute('src'); vid.load(); } catch { /* ignore */ } card.remove(); }, 700);
      if (videoCardEl === card) { videoCardEl = null; videoCardCancel = null; }
    };
    // rush at the face on the next frame, stick, then recede
    requestAnimationFrame(() => { if (!disposed) card.classList.add('in'); });
    const life = setTimeout(remove, VIDEO_HOLD_SEC * 1000);
    const handle = { cancel: () => { clearTimeout(life); remove(); } };
    videoCardCancel = handle.cancel;
    loops.add(handle);
    setTimeout(() => loops.delete(handle), VIDEO_HOLD_SEC * 1000 + 900);
  }

  /** Room arrival cuts the heavies short: the stuck video card recedes and any
   * running gif rain stops spawning (falling gifs finish their slide). Returns
   * true if anything live was actually cut. */
  function cancelHeavy() {
    let cut = false;
    heavyGen++;   // invalidate any video card still mid-acquire (see videoCard's gen check)
    if (videoCardCancel) { try { videoCardCancel(); } catch { /* ignore */ } cut = true; }
    for (const c of cascades) { try { c.cancel(); } catch { /* ignore */ } cut = true; }
    cascades.clear();
    return cut;
  }

  // ---- pinned centre spiral (#647) -------------------------------------------
  // An OPTIONAL persistent spiral tacked to the tunnel's visual centre, spinning
  // slowly, its edges masked so it blends into the bore. Purely ambient: it lives
  // inside `root` (.sf-pfx, z4) so it inherits pointer-events:none and sits BELOW
  // the clickable bubble field (.cf-layer z6) and the HUD (z10) - it can never eat
  // a pop or cover a treat. Gated on the live S.runPinnedSpiral toggle; one element,
  // shown per run and re-picked between chambers.
  let pinnedEl = null;
  function ensurePinned() {
    if (!pinnedEl) {
      pinnedEl = document.createElement('div');
      pinnedEl.className = 'sf-pfx-pinned';
      root.appendChild(pinnedEl);
    }
    return pinnedEl;
  }
  /** Show (or, if the toggle is off, ensure hidden) the pinned spiral for a run. */
  function showPinnedSpiral() {
    if (disposed) return;
    if (!S.runPinnedSpiral) { hidePinnedSpiral(); return; }
    const el = ensurePinned();
    el.style.backgroundImage = `url('${pickSpiralUrl()}')`;
    el.classList.add('is-on');
  }
  /** Re-pick the image on a room/biome change (only if it's currently shown). */
  function refreshPinnedSpiral() {
    if (disposed || !pinnedEl || !S.runPinnedSpiral || !pinnedEl.classList.contains('is-on')) return;
    pinnedEl.style.backgroundImage = `url('${pickSpiralUrl()}')`;
  }
  /** Fade it out (run end / back to the hub). */
  function hidePinnedSpiral() {
    if (pinnedEl) pinnedEl.classList.remove('is-on');
  }

  /**
   * Render a bubble payload in-world. `spec.payload` is the fire-payload shape
   * ({ kind, overlay? }); `opts.durationMult` stretches the on-screen time.
   * audio is intentionally NOT handled here (native sfx bridge).
   */
  function applyPayload(spec, opts = {}) {
    if (disposed || !spec || !spec.payload) return;
    const durMult = clamp(opts.durationMult == null ? 1 : opts.durationMult, 0.1, 10);
    const strength = clamp(spec.strength == null ? 60 : spec.strength, 0, 100);
    const p = spec.payload;
    switch (p.kind) {
      case 'flash':        flash(strength, durMult); break;
      case 'subliminal':   subliminal(strength); break;
      case 'overlay':
        if (p.overlay === 'spiral') showSpiral(strength, durMult);
        else if (p.overlay === 'braindrain') showBraindrain(strength, durMult);
        else showPink(strength, durMult);   // pink_filter (default)
        break;
      case 'glitch':       showGlitch(strength, durMult); break;
      case 'bambiFreeze':  bambiFreeze(); break;
      case 'bouncingText': bouncingText(durMult); break;
      case 'gifCascade':   gifCascade(durMult); break;
      case 'video':        videoCard(); break;
      // 'audio' -> sfx bridge. Not rendered here.
      default: break;
    }
  }

  function dispose() {
    disposed = true;
    for (const h of Object.values(holds)) { if (h.hideTimer) clearTimeout(h.hideTimer); }
    for (const l of loops) { try { l.cancel(); } catch { /* best effort */ } }
    loops.clear();
    cascades.clear();
    videoCardEl = null;
    videoCardCancel = null;
    root.remove();
    front.remove();
  }

  return { applyPayload, cancelHeavy, showPinnedSpiral, refreshPinnedSpiral, hidePinnedSpiral, dispose };
}
