/* ============================================================================
 * bubbles.js (Sissy Fall) - the Bubble Pop field as the fall's MAIN mechanic.
 *
 * Copied from js/rabbit-hole/bubbles.js and extended for the game:
 *   - callbacks: onPop(kind, gain, combo), onMiss(), onEffect(kind), onCombo(combo)
 *   - setIntensity(0..1): population + rise speed ramp temple-run style
 *   - a MISS = an unpopped bubble finishing its rise (counted only while live)
 *   - getScore() / getCombo() / reset() for the HUD + run restarts
 *   - always-on: no dock/toggle chrome, no localStorage enable pref, no persona
 *   - pauses itself while the page is hidden
 *
 * Same DOM/CSS contract as the original (rh-bubble* classes, copied into
 * fall-styles.css). All audio honours the shared master mute (audioMute.js).
 * ==========================================================================*/

import { FLASH_VOICES, SUB_VOICES, SUBLIMINAL_DROPS } from '/dtrh/assets/bubbles/manifest.js';
import { S, activeWords, getCustomSpiral, getVeilWords } from './settings.js';
import { pickSpiralUrl } from './loomSpirals.js';
import { getLevel } from './audioLevels.js';
import { makeSfxPlayer } from './audioBus.js';

const BASE = '/dtrh/assets/bubbles/';

// Tuning -------------------------------------------------------------------
const RISE_MIN_CALM = 9, RISE_MAX_CALM = 16;   // seconds to cross the screen at intensity 0
const RISE_MIN_HOT = 4.5, RISE_MAX_HOT = 8;    // ... at intensity 1
const COMBO_WINDOW = 1.6;
const BASE_XP = 5;
const LUCKY_XP = 100;
const POP_SFX = ['Pop.mp3', 'Pop2.mp3', 'Pop3.mp3'];
const CHIME_SFX = ['chime1.mp3', 'chime2.mp3', 'chime3.mp3'];
const DROP_BASE = BASE + 'drops/';
// The default spiral overlay is a POOL (bundled sp1..8 + the player's Loom
// spirals, ~50/50): pickSpiralUrl() from loomSpirals.js owns that choice so the
// in-run payload fx shares the exact same source. A user-set custom spiral, if
// any, still wins (checked in fireEffect before the pool).
// slug a subliminal word to its drop key (matches gen_sub_drops.py: lowercase,
// runs of non-alphanumerics -> '_', trimmed).
const dropSlug = (w) => String(w).toLowerCase().replace(/[^a-z0-9]+/g, '_').replace(/^_|_$/g, '');
const FLASH_MAX_MS = 5000;   // a clip overlay plays one cycle, hard-capped at 5s
const FLASH_GEN_CAP = 5;     // hydra hard cap; the live value is S.hydraGen (0-5)
const FLASH_FLING_MULT = 1.6;// the ripple flings flash clips over a wider bite than it pops bubbles
const FILL_WINDOW = 2800;    // ms a (re)fill trickles its bubbles in over
const TOPUP_MS = 600;        // cadence of the population top-up tick

const KINDS = [
  { id: 'normal', w: 50 },
  { id: 'flash', w: 9 },
  { id: 'subliminal', w: 5 },   // whisper bubbles - kept lower; they unlock early and each flashes 3 words
  { id: 'spiral', w: 8 },
  { id: 'glitch', w: 7 },
  { id: 'braindrain', w: 6 },
  { id: 'pinkfilter', w: 6 },
  { id: 'prism', w: 2 },   // rare: randomizes the tube colors for ~10s
  { id: 'lucky', w: 2 },   // rare: golden jackpot bubble
];

// Progression: the fall opens with only normal bubbles, then each effect kind
// joins the spawn pool at its own gate (seconds of ACTIVE run time - pauses do
// not count), in the order the player learns them. Before a kind unlocks it
// cannot spawn; normal bubbles fill the gap. Everything is unlocked by ~5.5 min.
// (To slow or speed the whole ramp, scale these numbers together.)
const UNLOCK_AT = {
  normal: 0,
  subliminal: 30,     // 0:30 - whispered trigger words
  flash: 75,          // 1:15 - flash clips
  pinkfilter: 120,    // 2:00 - pink wash
  spiral: 165,        // 2:45 - spiral overlay
  glitch: 210,        // 3:30 - glitch
  braindrain: 240,    // 4:00 - drain wash
  lucky: 300,         // 5:00 - gold jackpot (rare)
  prism: 330,         // 5:30 - prism color-scramble (rare, last)
};

const rand = (a, b) => a + Math.random() * (b - a);
const pick = (arr) => arr[(Math.random() * arr.length) | 0];
const lerp = (a, b, t) => a + (b - a) * t;
const now = () => performance.now() / 1000;

// SFX go through the shared WebAudio player (audioBus.makeSfxPlayer): decoded
// AudioBuffers on the one fall-wide context are near-free to fire on the pop
// frame, overlap naturally, and - unlike element clones - actually respect the
// volume they're given on iOS.
export function createBubbles({ hud, canvas, media, onPop, onMiss, onEffect, onCombo }) {
  const mobile = window.innerWidth < 640;
  const COUNT_MIN = mobile ? 4 : 5;
  const COUNT_MAX = mobile ? 9 : 16;

  let paused = false;
  let frozen = false; // ESC pause: animations freeze in place (no clear/reseed)
  let intensity = 0;
  let targetCount = COUNT_MIN;
  let runTime = 0;    // active run seconds, fed by scene from director.getRunTime()
  const audio = makeSfxPlayer();
  // Decode the hot pop-path SFX up front so even the FIRST pop is buffer-backed
  // (drops decode lazily on first use - there are dozens of them).
  audio.preload([
    ...POP_SFX.map((f) => BASE + 'sfx/' + f),
    ...CHIME_SFX.map((f) => BASE + 'sfx/' + f),
    BASE + 'sfx/GG.mp3',
  ]);
  // Pops / chimes / combo stings are the loud layer - route them through the
  // live 'fx' level so the effects slider can duck them (subliminal drops keep
  // their own 'drops' level and are NOT scaled here).
  const playSfx = (src, vol) => audio.play(src, vol * getLevel('fx'));

  // ---- layers --------------------------------------------------------------
  const layer = document.createElement('div');
  layer.className = 'rh-bubbles-layer';
  hud.insertBefore(layer, hud.firstChild); // under the HUD chrome, above canvas

  const fx = document.createElement('div');
  fx.className = 'rh-bubble-fx';
  hud.appendChild(fx);

  let score = 0, combo = 0, lastPop = -999;

  // ---- bubble pool ---------------------------------------------------------
  const live = new Set();

  // Only kinds whose unlock gate has passed are eligible; the weighted draw runs
  // over that live subset, so the mix widens as the fall goes on.
  const isUnlocked = (id) => runTime >= (UNLOCK_AT[id] || 0);
  function pickKind() {
    let total = 0;
    for (const k of KINDS) if (isUnlocked(k.id)) total += k.w;
    let r = Math.random() * total;
    for (const k of KINDS) {
      if (!isUnlocked(k.id)) continue;
      if ((r -= k.w) < 0) return k.id;
    }
    return 'normal';
  }

  function riseRange() {
    return [lerp(RISE_MIN_CALM, RISE_MIN_HOT, intensity), lerp(RISE_MAX_CALM, RISE_MAX_HOT, intensity)];
  }

  function spawn(seed) {
    const kind = pickKind();
    const size = rand(80, 150) * (kind === 'lucky' ? 1.2 : 1) * S.bubbleSize;
    const [rMin, rMax] = riseRange();
    const rise = rand(rMin, rMax);
    const sway = rand(18, 46);
    const swayDur = rand(2.5, 5);

    const wrap = document.createElement('div');
    wrap.className = 'rh-bubble-wrap';
    wrap.style.left = `${rand(2, 92)}%`;
    wrap.style.setProperty('--rise', `${rise}s`);
    if (seed) wrap.style.animationDelay = `${-rand(0, rise)}s`;

    const bubble = document.createElement('div');
    bubble.className = `rh-bubble rh-bubble--${kind}`;
    bubble.style.width = bubble.style.height = `${size}px`;
    bubble.style.setProperty('--sway', `${sway}px`);
    bubble.style.setProperty('--sway-dur', `${swayDur}s`);
    wrap.appendChild(bubble);

    const rec = { wrap, bubble, kind, popped: false, seeded: !!seed };
    live.add(rec);

    function recycle() {
      if (!live.has(rec)) return;
      live.delete(rec);
      wrap.remove();
      if (!paused && !frozen && live.size < targetCount) spawn(false);
    }
    wrap.addEventListener('animationend', (e) => {
      if (e.target === wrap && !rec.popped) {
        // An unpopped bubble escaping off the top = a MISS - except seeded ones:
        // a refill scatters them mid-rise (negative animation delay), so some
        // finish almost immediately and would count as unfair misses.
        if (!rec.seeded && !paused && !frozen && !document.hidden && targetCount > 0 && onMiss) { try { onMiss(); } catch (err) { /* ignore */ } }
        recycle();
      }
    });
    bubble.addEventListener('pointerdown', (e) => {
      e.preventDefault();
      e.stopPropagation();
      if (rec.popped || paused || frozen) return;
      pop(rec, e.clientX, e.clientY);
      bubble.addEventListener('animationend', recycle, { once: true });
    });

    layer.appendChild(wrap);
  }

  // ---- effects -------------------------------------------------------------
  function floatText(text, x, y, cls) {
    const el = document.createElement('div');
    el.className = `rh-xp-pop ${cls || ''}`;
    el.textContent = text;
    el.style.left = `${x}px`;
    el.style.top = `${y}px`;
    fx.appendChild(el);
    el.addEventListener('animationend', () => el.remove(), { once: true });
  }

  // Chromium caps live media players per page (~75); an element that is just
  // .remove()d keeps its decoder until some eventual GC. Flash clips + effect
  // overlays churn fast enough to exhaust the cap in a long session - after
  // which NEW videos (cards, flashes) silently never load. Explicitly unloading
  // releases the player right away (same pattern as the card videos).
  function releasePlayer(v) {
    if (typeof v.pause !== 'function') return; // image overlays hold no player
    try { v.pause(); v.removeAttribute('src'); v.load(); } catch (e) { /* ignore */ }
  }

  // One overlay clip PER effect type (keyed by class): a newer clip of the SAME
  // type replaces the old one; DIFFERENT types stack (that stacking is the
  // "effect chain" the director listens for).
  const overlays = new Map();
  function overlayVideo(src, cls, maxMs, loop, fxop) {
    const prev = overlays.get(cls);
    if (prev) { window.clearTimeout(prev.timer); releasePlayer(prev.v); prev.v.remove(); overlays.delete(cls); }
    const v = document.createElement('video');
    v.className = cls;
    if (fxop != null) v.style.setProperty('--fxop', String(fxop));
    v.src = src;
    v.muted = true; v.autoplay = true; v.loop = !!loop; v.playsInline = true;
    fx.appendChild(v);
    const rec = { v, timer: 0 };
    overlays.set(cls, rec);
    const finish = () => {
      window.clearTimeout(rec.timer);
      releasePlayer(v);
      v.remove();
      if (overlays.get(cls) === rec) overlays.delete(cls);
    };
    if (!loop) v.addEventListener('ended', finish, { once: true });
    rec.timer = window.setTimeout(finish, maxMs);
    v.play().catch(() => {});
  }

  // Same slot as overlayVideo but for a still/gif image (custom spiral drops).
  // An <img> animates its gif natively - no decoding needed at this layer.
  function overlayImage(src, cls, maxMs, fxop) {
    const prev = overlays.get(cls);
    if (prev) { window.clearTimeout(prev.timer); releasePlayer(prev.v); prev.v.remove(); overlays.delete(cls); }
    const el = document.createElement('img');
    el.className = cls;
    if (fxop != null) el.style.setProperty('--fxop', String(fxop));
    el.src = src;
    fx.appendChild(el);
    const rec = { v: el, timer: 0 };
    overlays.set(cls, rec);
    rec.timer = window.setTimeout(() => {
      el.remove();
      if (overlays.get(cls) === rec) overlays.delete(cls);
    }, maxMs);
  }

  // Trigger flash clips at RANDOM positions, CLICKABLE: popping one spawns two
  // more (the hydra) for FLASH_MAX_GEN generations.
  const flashLive = new Set();
  // The flash bubbles pull from the player's own media pool (hostMedia). No pool =>
  // callers fall back to the built-in CSS flash. No bundled flash clips ship.
  const hasFlashMedia = () => !!(media && media.hasUserMedia && media.hasUserMedia());
  const FLASH_LIVE_CAP = mobile ? 3 : 20; // cap concurrent flash-clip decoders (raised so the 'flash gifs' slider can reach 10 + hydra children; phones stay conservative)
  function spawnFlash(gen) {
    if (flashLive.size >= FLASH_LIVE_CAP) return; // too many decoders live: skip this one
    // Flash bubbles draw from the PLAYER's own media pool (no bundled clips ship).
    // Resolve a URL async, then build the clickable clip; an empty pool spawns
    // nothing and fireEffect falls back to the built-in CSS flash.
    const picked = (media && media.hasUserMedia && media.hasUserMedia() && media.draw) ? media.draw() : null;
    if (!picked || !picked.acquire) return;
    picked.acquire().then((a) => { if (a && a.url) buildFlashClip(gen, picked.kind, a.url); }).catch(() => {});
  }

  // Build one clickable flash clip from a resolved user-media URL. A video plays
  // once and ends; an image just holds for the timeout. Both share the hydra pop +
  // ripple-fling teardown (releasePlayer no-ops on the <img>).
  function buildFlashClip(gen, kind, url) {
    if (flashLive.size >= FLASH_LIVE_CAP) return;   // re-check: the acquire resolved async
    const isVideo = kind === 'video';
    const v = document.createElement(isVideo ? 'video' : 'img');
    v.className = 'rh-fx-flashclip';
    v.src = url;
    if (isVideo) { v.muted = true; v.autoplay = true; v.loop = false; v.playsInline = true; }
    const sizeVw = (gen >= 2 ? 19 : gen === 1 ? 26 : 34) * 0.7 * S.gifSize; // 30% smaller base (bubbles + tube-zone flashes)
    v.style.setProperty('--w', `${sizeVw.toFixed(1)}vw`);
    v.style.setProperty('--gifop', String(S.gifOpacity));
    v.style.left = `${rand(16, 84)}%`;
    v.style.top = `${rand(22, 78)}%`;
    fx.appendChild(v);
    flashLive.add(v);

    let timer = 0, done = false;
    const finish = () => { if (done) return; done = true; window.clearTimeout(timer); releasePlayer(v); v.remove(); flashLive.delete(v); };
    v._flashFinish = finish;   // the ripple's fling reuses this exact teardown
    if (isVideo) v.addEventListener('ended', finish, { once: true });   // <img> has no 'ended'
    timer = window.setTimeout(finish, FLASH_MAX_MS);
    v.addEventListener('pointerdown', (e) => {
      // Right-click belongs to the ripple. chaosRun's onPointerDownGlobal is a
      // window CAPTURE-phase listener, so on a right-click it already fired the
      // ripple + flung this clip (flingFlashesNear) before this bubble-phase
      // handler runs. Bail so the fling plays out instead of finish() tearing the
      // clip away mid-flight. Only a left-click pops the clip by hand (the hydra).
      if (e.button != null && e.button !== 0) return;
      e.preventDefault(); e.stopPropagation();
      if (frozen) return;
      const x = e.clientX, y = e.clientY;
      finish();
      playSfx(BASE + 'sfx/' + pick(POP_SFX), 0.25);
      // phones split one generation less: a full hydra burst cold-starts a pile
      // of video decoders at once, which is what skips frames on a pop.
      const genCap = Math.min(FLASH_GEN_CAP, S.hydraGen, mobile ? 1 : FLASH_GEN_CAP);
      if (gen < genCap) {
        const gain = BASE_XP * 2;
        score += gain;
        floatText(`+${gain}`, x, y, '');
        if (onPop) { try { onPop('flashclip', gain, combo); } catch (err) { /* ignore */ } }
        // same deferral as the first clip, and the two children are staggered
        // apart too: even off the pop frame, two cold decoder starts landing on
        // the SAME frame is its own skip on phones
        if (mobile) {
          window.setTimeout(() => { if (!frozen) spawnFlash(gen + 1); }, 60);
          window.setTimeout(() => { if (!frozen) spawnFlash(gen + 1); }, 220);
        } else { spawnFlash(gen + 1); spawnFlash(gen + 1); }
      }
    });
    if (isVideo) v.play().catch(() => {});
  }

  function simpleFx(cls, ms, fxop) {
    const old = fx.querySelector('.' + cls);   // same wash refreshes; different ones stack
    if (old) old.remove();
    const el = document.createElement('div');
    el.className = `rh-fx ${cls}`;
    if (fxop != null) el.style.setProperty('--fxop', String(fxop));
    fx.appendChild(el);
    if (ms) window.setTimeout(() => el.remove(), ms);
    else el.addEventListener('animationend', () => el.remove(), { once: true });
  }

  function playVoice() {
    // Voiceover is on hold while the lines get re-recorded: effect visuals
    // still fire, but no voice clip plays.
  }

  function flashWords(n) {
    const words = activeWords();
    if (!words.length) return; // every word switched off: nothing to whisper
    for (let i = 0; i < n; i++) {
      window.setTimeout(() => {
        const word = pick(words);
        const el = document.createElement('div');
        el.className = 'rh-sublim-word';
        el.textContent = word;
        fx.appendChild(el);
        el.addEventListener('animationend', () => el.remove(), { once: true });
        // whisper the matching trigger drop as the word flashes (built-in words
        // only; custom words have no drop and flash silently)
        const drop = SUBLIMINAL_DROPS[dropSlug(word)];
        if (drop) audio.play(DROP_BASE + drop, getLevel('drops'));
      }, i * 220);
    }
  }
  function glitchCanvas() {
    // The .rh-canvas-glitch class runs a CSS filter (hue-rotate/saturate) over
    // the WHOLE WebGL canvas - a full-screen compositor pass that skips frames on
    // phones right as you pop. Skip it on mobile; the glitch wash overlay still fires.
    if (mobile) return;
    canvas.classList.add('rh-canvas-glitch');
    window.setTimeout(() => canvas.classList.remove('rh-canvas-glitch'), 420);
  }

  function fireEffect(kind) {
    switch (kind) {
      case 'flash':
        if (hasFlashMedia()) {
          // phones: push the clip's decoder cold-start off the pop frame, so
          // the pop animation + sfx land clean and the video joins a beat later
          if (mobile) window.setTimeout(() => { if (!frozen && !paused) spawnFlash(0); }, 60);
          else spawnFlash(0);
        } else simpleFx('rh-fx-flash', 0, S.pinkOpacity);   // no user media: abstract CSS flash
        playVoice(FLASH_VOICES);
        break;
      case 'spiral': {
        const custom = getCustomSpiral();
        if (custom && custom.isVideo) overlayVideo(custom.url, 'rh-fx-spiralvid', FLASH_MAX_MS, true, S.spiralOpacity);
        else if (custom) overlayImage(custom.url, 'rh-fx-spiralvid', FLASH_MAX_MS, S.spiralOpacity);
        else {
          // no custom spiral: draw from the shared pool (bundled + Loom, ~50/50)
          overlayImage(pickSpiralUrl(), 'rh-fx-spiralvid', FLASH_MAX_MS, S.spiralOpacity);
        }
        playVoice(FLASH_VOICES);
        break;
      }
      case 'subliminal':
        if (S.glitch > 0.25) glitchCanvas();
        flashWords(3); playVoice(SUB_VOICES);
        break;
      case 'glitch': {
        if (S.glitch > 0.02) { // at 0 the glitch bubble only scores
          if (S.glitch > 0.25) glitchCanvas();
          // phones use the cheap CSS wash rather than cold-starting a video
          // decoder on the pop frame (that decoder spin-up is the skip). Desktop
          // pulls a looping user VIDEO if the pool has one, else the CSS glitch.
          const vid = (!mobile && hasFlashMedia() && media.drawKind) ? media.drawKind('video') : null;
          if (vid && vid.acquire) {
            vid.acquire()
              .then((a) => { if (a && a.url) overlayVideo(a.url, 'rh-fx-glitchvid', FLASH_MAX_MS, true, 0.3 * S.glitch); else simpleFx('rh-fx-glitch'); })
              .catch(() => simpleFx('rh-fx-glitch'));
          } else simpleFx('rh-fx-glitch');
        }
        break;
      }
      case 'braindrain':
        simpleFx('rh-fx-drain', FLASH_MAX_MS); flashWords(1); playVoice(FLASH_VOICES);
        break;
      case 'prism':
        simpleFx('rh-fx-prism', 900); playVoice(FLASH_VOICES);
        break;
      case 'pinkfilter':
        simpleFx('rh-fx-flash', 0, S.pinkOpacity); playVoice(FLASH_VOICES);
        break;
    }
    if (onEffect) { try { onEffect(kind); } catch (err) { /* ignore */ } }
  }

  // A veil you fall THROUGH in the 3D tunnel fires the same wash/overlay a
  // popped bubble of that kind makes, PLUS a synced trigger drop: the whispered
  // snap + its flashed word, so punching through the veil reads as one hit that
  // lands with the ongoing drift voice. scene.js calls this from onVeilPass.
  // HEADPHONES (crafted, Part 2): the veil words come from the active word pack
  // (the classic pack keeps the shipped Sink/Drop/Blank/Empty set). Read live so
  // a pack change in THE BOUDOIR applies to the next crossing.
  // Veil drops fire on tube position, not a click - decode them up front so
  // the FIRST crossing is already buffer-backed (the player skips a sound
  // whose buffer is still decoding rather than play it at the wrong volume).
  audio.preload(Object.values(getVeilWords())
    .map((w) => SUBLIMINAL_DROPS[dropSlug(w)])
    .filter(Boolean)
    .map((d) => DROP_BASE + d));
  function flashDrop(word) {
    if (!frozen && !paused && !document.hidden) {
      const el = document.createElement('div');
      el.className = 'rh-sublim-word';
      el.textContent = word;
      fx.appendChild(el);
      el.addEventListener('animationend', () => el.remove(), { once: true });
    }
    const drop = SUBLIMINAL_DROPS[dropSlug(word)];
    if (drop) audio.play(DROP_BASE + drop, getLevel('drops'));
  }
  // `force` (game mode): the field is deliberately paused for the whole DtRH
  // session, but veil punch-throughs should still wash - the run brain gates
  // them on its own held/running state instead (scene.js onVeilPass).
  function triggerVeil(kind, force = false) {
    if ((paused && !force) || frozen) return;
    fireEffect(kind);            // the wash/overlay (+ director boost via onEffect)
    const word = getVeilWords()[kind];
    if (word) flashDrop(word);   // the snap: whispered drop + flashed word, in sync
  }

  // Fire clickable flash CLIPS (the hydra: popping one spawns two more) without
  // the veil word/drop - so the DtRH flash-bubble payload gets the same clickable,
  // multiplying flash the tunnel's flash veils do. Count defaults to the 'flash
  // gifs' slider (S.flashCount, 1-10); spawnFlash self-caps via FLASH_LIVE_CAP and
  // the pop-split honours S.hydraGen exactly like a veil clip.
  function flashBurst(n) {
    if (frozen || !hasFlashMedia()) return;
    const count = Math.max(1, (n != null ? n : S.flashCount) | 0);
    for (let i = 0; i < count; i++) {
      // stagger cold <video> starts so N decoders don't all land on one frame
      window.setTimeout(() => { if (!frozen) spawnFlash(0); }, i * (mobile ? 120 : 55));
    }
  }

  // The Ripple's shove: every live flash clip whose centre falls within a
  // ripple's bite of the viewport-px point (px,py) is hurled off screen ALONG
  // THE ANGLE of the hit (straight away from the ripple), spinning + fading as
  // it goes, then torn down. probeOnly = don't fling, just report whether any
  // clip is under the point (so the ripple can fire on a bare flash covering
  // the field, not silently no-op when no bubble sits under the cursor).
  function flingFlashesNear(px, py, radiusPx, probeOnly) {
    if (frozen) return probeOnly ? false : 0;
    const reach = Math.max(0, radiusPx || 0) * FLASH_FLING_MULT;
    let n = 0;
    for (const v of flashLive) {
      if (v._flung) continue;
      const rect = v.getBoundingClientRect();
      if (!rect.width || !rect.height) continue;
      const cx = rect.left + rect.width / 2, cy = rect.top + rect.height / 2;
      const dpx = cx - px, dpy = cy - py;
      const dist = Math.hypot(dpx, dpy);
      if (dist > reach) continue;
      if (probeOnly) return true;
      // fly away from the ripple point; a near dead-centre hit picks a random spoke
      let nx, ny;
      if (dist > 4) { nx = dpx / dist; ny = dpy / dist; }
      else { const a = rand(0, Math.PI * 2); nx = Math.cos(a); ny = Math.sin(a); }
      const travel = Math.max(window.innerWidth, window.innerHeight) * 1.25; // clears the viewport
      const rot = rand(-140, 140);
      v._flung = true;
      v.style.pointerEvents = 'none';    // can't pop it mid-flight
      const teardown = v._flashFinish;
      // Drive the fling with the Web Animations API, NOT a CSS transition: the
      // clip's base rule is opacity:0 and it's only visible because its CSS
      // `animation` holds it there, so killing that animation would blink it out
      // instantly. A script animation out-ranks the CSS animation for
      // transform+opacity and keyframes a real from->off-screen slide instead.
      const startOp = getComputedStyle(v).opacity || '1';
      try {
        const anim = v.animate([
          { transform: 'translate(-50%, -50%) translate(0px, 0px) rotate(0deg) scale(1)', opacity: startOp },
          { transform: `translate(-50%, -50%) translate(${(nx * travel).toFixed(0)}px, ${(ny * travel).toFixed(0)}px) rotate(${rot.toFixed(0)}deg) scale(0.55)`, opacity: 0 },
        ], { duration: 1670, easing: 'cubic-bezier(0.2, 0.6, 0.3, 1)', fill: 'forwards' });
        anim.onfinish = () => { if (teardown) teardown(); };
        anim.oncancel = () => { if (teardown) teardown(); };
      } catch (e) {
        if (teardown) teardown();   // no WAAPI: just clear it
      }
      n++;
    }
    return probeOnly ? false : n;
  }

  // ---- pop -----------------------------------------------------------------
  // Sparkle burst (ported from the WPF BubbleService pop): shards fly outward
  // from the pop point with a little gravity, shrinking + fading (CSS-driven).
  function sparkleBurst(x, y, kind) {
    const lucky = kind === 'lucky';
    const n = lucky ? 14 : 9;
    const color = lucky ? '#ffe27a' : '#ffd9ef';
    for (let i = 0; i < n; i++) {
      const p = document.createElement('div');
      p.className = 'rh-spark';
      const ang = (Math.PI * 2 * i) / n + rand(-0.35, 0.35);
      const dist = rand(42, 112) * (lucky ? 1.3 : 1);
      p.style.left = `${x}px`;
      p.style.top = `${y}px`;
      p.style.color = color;
      p.style.setProperty('--dx', `${Math.cos(ang) * dist}px`);
      p.style.setProperty('--dy', `${Math.sin(ang) * dist}px`);
      p.style.setProperty('--fall', `${rand(28, 66)}px`);
      fx.appendChild(p);
      p.addEventListener('animationend', () => p.remove(), { once: true });
    }
  }

  // Emote rain: Emotes are the run currency, so a pop weeps a few of them out.
  // Each glyph is flattened to a flat monocolour SILHOUETTE via the
  // `color:transparent + text-shadow:0 0 0 <tint>` trick (works on colour emoji
  // in Chromium/WebView2), tinted per-emotion so the fall is a soft rain of
  // mixed-colour feelings drifting down at ~10% opacity. Same DOM lifecycle as
  // sparkleBurst: append to the fx layer, self-remove on animationend.
  const EMORAIN = [
    { e: '😀', c: '#ffd34d' }, // joy   - gold
    { e: '😢', c: '#5fb7ff' }, // sad   - blue
    { e: '😠', c: '#ff5a5a' }, // anger - red
    { e: '😮', c: '#7ff0e0' }, // shock - cyan
    { e: '😍', c: '#ff6fd0' }, // love  - pink
    { e: '😴', c: '#b58cff' }, // sleep - violet
    { e: '😭', c: '#4d8dff' }, // cry   - deep blue
    { e: '😳', c: '#ff8fb0' }, // flush - rose
  ];
  function emojiRain(x, y) {
    const n = 5 + ((Math.random() * 4) | 0); // DEBUG loud: 5-8 glyphs (final: 2-4)
    for (let i = 0; i < n; i++) {
      const pickEm = pick(EMORAIN);
      const s = document.createElement('span');
      s.className = 'rh-emorain';
      s.textContent = pickEm.e;
      s.style.left = `${x}px`;
      s.style.top = `${y}px`;
      s.style.setProperty('--tint', pickEm.c);
      s.style.setProperty('--dx', `${rand(-70, 70)}px`);
      s.style.setProperty('--fall', `${rand(200, 360)}px`);
      s.style.setProperty('--rot', `${rand(-40, 40)}deg`);
      s.style.setProperty('--dur', `${rand(2.2, 3.4)}s`);
      s.style.fontSize = `${rand(30, 44) | 0}px`;
      fx.appendChild(s);
      s.addEventListener('animationend', () => s.remove(), { once: true });
    }
  }

  function pop(rec, x, y) {
    rec.popped = true;
    rec.bubble.classList.add('is-pop');
    sparkleBurst(x, y, rec.kind);
    emojiRain(x, y);

    const t = now();
    combo = (t - lastPop <= COMBO_WINDOW) ? combo + 1 : 1;
    lastPop = t;

    let gain;
    if (rec.kind === 'lucky') {
      gain = LUCKY_XP * combo;
      score += gain;
      floatText(`JACKPOT  20×  +${gain}`, x, y, 'rh-xp-lucky');
      playSfx(BASE + 'sfx/' + pick(CHIME_SFX), 0.375);
      simpleFx('rh-fx-flash', 0, S.pinkOpacity);
    } else {
      gain = BASE_XP * combo;
      score += gain;
      floatText(combo > 1 ? `+${gain}  ×${combo}` : `+${gain}`, x, y, combo > 1 ? 'rh-xp-combo' : '');
      playSfx(BASE + 'sfx/' + pick(POP_SFX), 0.25);
      if (rec.kind !== 'normal') fireEffect(rec.kind);
    }
    if (combo === 5 || combo === 10 || (combo > 10 && combo % 5 === 0)) {
      playSfx(BASE + 'sfx/GG.mp3', 0.35);
      if (onCombo) { try { onCombo(combo); } catch (err) { /* ignore */ } }
    }
    if (onPop) { try { onPop(rec.kind, gain, combo); } catch (err) { /* ignore */ } }
  }

  // ---- fill / population ---------------------------------------------------
  function clearAll() {
    for (const rec of live) rec.wrap.remove();
    live.clear();
    for (const v of flashLive) { releasePlayer(v); v.remove(); }
    flashLive.clear();
  }
  function fill() {
    clearEntrance();
    clearAll();
    if (paused) return;
    populate(true, FILL_WINDOW);
  }

  // First-load entrance: the screen starts EMPTY; a few seconds in, the bubbles
  // rise in from the bottom, staggered.
  const ENTRANCE_DELAY = rand(3000, 5000);
  const ENTRANCE_STAGGER = 2400;
  let entranceTimers = [];
  function clearEntrance() { for (const id of entranceTimers) window.clearTimeout(id); entranceTimers = []; }
  function populate(seed, windowMs) {
    const want = Math.max(0, targetCount - live.size);
    for (let i = 0; i < want; i++) {
      entranceTimers.push(window.setTimeout(() => {
        if (!paused && !frozen && live.size < targetCount) spawn(seed);
      }, Math.random() * windowMs));
    }
  }
  function scheduleEntrance() {
    clearEntrance();
    entranceTimers.push(window.setTimeout(() => {
      if (!paused) populate(false, ENTRANCE_STAGGER);
    }, ENTRANCE_DELAY));
  }

  // Population top-up: intensity can raise targetCount mid-run; a soft tick adds
  // one bubble at a time so a ramp never dumps a wall of bubbles in one frame.
  const topupTimer = window.setInterval(() => {
    if (!paused && !frozen && !document.hidden && live.size < targetCount) spawn(false);
  }, TOPUP_MS);

  // Called every frame, so it reads S.bubbleDensity live: the population slider
  // scales the intensity-driven target (0 = bubbles off, 1.25 = ~25% more than
  // the old ceiling). Lowering it lets the surplus rise off and not respawn;
  // raising it tops back up on the next tick.
  function setIntensity(v) {
    intensity = Math.min(1, Math.max(0, v));
    const density = Math.max(0, Math.min(1.25, S.bubbleDensity ?? 1));
    targetCount = Math.round(lerp(COUNT_MIN, COUNT_MAX, intensity) * density);
  }

  // Fed each frame from the director's run clock; gates which kinds can spawn.
  function setRunTime(sec) { runTime = Math.max(0, sec || 0); }

  function setPaused(v) {
    v = !!v;
    if (v === paused) return;
    paused = v;
    fill();
  }

  // ESC pause: freeze everything IN PLACE (CSS animations pause via the
  // .is-frozen class, overlay/flash videos pause too) - unlike setPaused,
  // nothing is cleared, so resume picks up exactly where the fall stopped.
  function setFrozen(v) {
    v = !!v;
    if (v === frozen) return;
    frozen = v;
    layer.classList.toggle('is-frozen', v);
    fx.classList.toggle('is-frozen', v);
    for (const rec of overlays.values()) {
      if (typeof rec.v.pause !== 'function') continue; // image overlays have nothing to pause
      try { if (v) rec.v.pause(); else { const p = rec.v.play(); if (p && p.catch) p.catch(() => {}); } } catch (e) { /* ignore */ }
    }
    for (const vid of flashLive) {
      try { if (v) vid.pause(); else { const p = vid.play(); if (p && p.catch) p.catch(() => {}); } } catch (e) { /* ignore */ }
    }
  }

  // Hidden-tab misses are already guarded in the rise animationend handler
  // (document.hidden check); pausing/resuming the field is the scene's call
  // (it also owns run-over pause), so no visibility listener here.

  setIntensity(0);
  scheduleEntrance();

  function dispose() {
    clearEntrance();
    window.clearInterval(topupTimer);
    canvas.classList.remove('rh-canvas-glitch');
    for (const rec of overlays.values()) { window.clearTimeout(rec.timer); releasePlayer(rec.v); rec.v.remove(); }
    overlays.clear();
    clearAll();
    layer.remove(); fx.remove();
  }

  return {
    setPaused,
    setFrozen,
    setIntensity,
    setRunTime,
    triggerVeil,
    flashBurst,
    flingFlashesNear,
    getScore: () => score,
    getCombo: () => combo,
    activeEffects: () => overlays.size + (flashLive.size ? 1 : 0),
    reset() { score = 0; combo = 0; lastPop = -999; runTime = 0; fill(); },
    dispose,
  };
}
