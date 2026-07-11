/* ============================================================================
 * cheshireVn.js - the Cheshire's Visual-Novel engine (FTUE overhaul, 2026-07).
 * Three surfaces, one voice, one queue:
 *
 *   scene(beats)   - FULLSCREEN. Dim + Cheshire-purple gradient over the live
 *                    tunnel, her cutout big on the left, beats advance on click.
 *                    ONE world-hold across the whole sequence (vnPortrait's
 *                    haltTunnel/duckAudio contract + the 400ms scheduleResume).
 *   say(beat)      - CORNER COMMENTARY. A small portrait rides the bottom-left
 *                    with a compact bubble while the run keeps falling. NEVER
 *                    holds the world - it ducks the drift whisper and flags
 *                    vn-speaking so the native barks stand down, nothing else.
 *   overlay(beat)  - PAUSE CARD. Her portrait + a line over a held field - the
 *                    lessonCard contract verbatim (world-hold + ARM_MS input
 *                    guard), for the rare moments that deserve a full stop.
 *
 * This intentionally does NOT extend vnPortrait.js (per-mod, frame-sequence,
 * always-pausing; it stays untouched as the CHESHIRE_DISABLED fallback) - the
 * proven pieces (voice graph + analyser aura, typewriter, world-hold, the
 * error->unblock path) are copied, the same duplication precedent lessonCard
 * set. Poses are STATIC cutouts (assets/vn/cheshire/{pose}.webp) with a CSS
 * breathe/bob idle + the --vnG voice aura; no frame decode, no manifest.
 *
 * Fully functional VO-less: while script.voReady is false no mp3 is even
 * requested and every beat times off its typewriter instead.
 *
 * API: const chesh = createCheshireVn(hud, { haltTunnel, duckAudio, driftDuck, send, script });
 *      await chesh.scene([{id,pose,text},...], {bg});   // fullscreen sequence
 *      chesh.say({id,pose,text,mood});                  // queued, non-blocking
 *      await chesh.overlay({id,pose,text,sub});         // world-hold card
 *      chesh.isBusy(); chesh.isHolding(); chesh.cancel(); chesh.dispose();
 * ==========================================================================*/

// Kill-switch: true = this engine never mounts, chaosRun falls back to the old
// hubGuide + vnPortrait + lesson cards byte-identically. (See cheshireGuide.js,
// which gates on the same constant.)
export const CHESHIRE_DISABLED = false;

const ACCENT_A = '255,64,200';   // her magenta
const ACCENT_B = '64,224,255';   // her cyan
const POSE_URL = (pose) => '/dtrh/assets/vn/cheshire/' + pose + '.webp';

// Visual aids: a beat's `prop` key -> what floats in the show-and-tell chip.
// Real field sprites where we have them, emoji where we don't.
const SPRITES = '/dtrh/assets/bubbles/';
const PROP_ART = {
  pink:       { img: SPRITES + 'effects/pinkfilter.png' },
  live:       { img: SPRITES + 'bubble.png', glow: 1 },
  spiral:     { img: SPRITES + 'effects/spiral.png', spin: 1 },
  braindrain: { img: SPRITES + 'effects/braindrain.png' },
  golden:     { img: SPRITES + 'effects/golden.png', glow: 1 },
  flash:      { img: SPRITES + 'effects/flash.png' },
  subliminal: { img: SPRITES + 'effects/subliminal.png' },
  glitch:     { img: SPRITES + 'effects/glitch.png' },
  prism:      { img: SPRITES + 'effects/prism.png' },
  gold:       { emoji: '🪙' },
  emotes:     { emoji: '✨' },
  purses:     { emoji: '✨🪙' },
  rabbit:     { emoji: '🐇' },
  doors:      { emoji: '🚪' },
  toybox:     { emoji: '🧸' },
  dials:      { emoji: '🎚️' },
  freeze:     { emoji: '🧊' },
  video:      { emoji: '🎬' },
  gifrain:    { emoji: '🌧️' },
  ripple:     { emoji: '🌊' },
  cards:      { emoji: '🃏' },
  tease:      { emoji: '🔴' },
  boom:       { emoji: '💥' },
  streak:     { emoji: '🔥' },
};

const ARM_MS = 300;      // overlay: ignore dismiss input this long (a grab click won't bounce it)
const RESUME_MS = 400;   // matches vnPortrait/lessonCard: hold across back-to-back beats
const SAY_GAP_MS = 350;  // breath between queued commentary lines

const CSS = `
/* ---------- shared ---------- */
.ch-fig-img{filter:drop-shadow(0 0 calc(5px + var(--vnG,0)*13px) rgba(${ACCENT_A},calc(.28 + var(--vnG,0)*.3)))
  drop-shadow(0 14px 26px rgba(0,0,0,.55));will-change:filter}
@keyframes chBreathe{0%,100%{transform:scale(1) translateY(0)}50%{transform:scale(1.015) translateY(-.6%)}}
@keyframes chCaret{50%{opacity:0}}
.ch-caret{display:inline-block;width:.62ch;margin-left:1px;color:rgb(${ACCENT_A});animation:chCaret .8s steps(1) infinite}
.ch-caret.ch-off{display:none}

/* ---------- prop chip (the "show and tell" visual aid) ---------- */
.ch-prop{position:absolute;top:-8vh;right:5%;min-width:12vh;height:12vh;padding:0 1.6vh;border-radius:6vh;
  display:flex;align-items:center;justify-content:center;gap:.4vh;pointer-events:none;
  background:radial-gradient(120% 120% at 30% 25%,rgba(${ACCENT_B},.16),rgba(12,4,20,.94) 70%);
  border:2px solid rgba(${ACCENT_B},.75);
  box-shadow:0 0 22px rgba(${ACCENT_B},.3), 0 10px 26px rgba(0,0,0,.5), inset 0 0 16px rgba(${ACCENT_B},.1);
  animation:chPropBob 4.6s ease-in-out infinite;transition:opacity .24s ease}
.ch-prop[hidden]{display:none}
.ch-prop img{width:9vh;height:9vh;object-fit:contain;filter:drop-shadow(0 4px 10px rgba(0,0,0,.55))}
.ch-prop span{font-size:6vh;line-height:1;white-space:nowrap;text-shadow:0 4px 14px rgba(0,0,0,.6)}
.ch-prop.ch-prop-glow{animation:chPropBob 4.6s ease-in-out infinite, chPropGlow 1.8s ease-in-out infinite}
.ch-prop-spin img{animation:chPropSpin 8s linear infinite}
@keyframes chPropBob{50%{transform:translateY(-9%)}}
@keyframes chPropSpin{to{transform:rotate(360deg)}}
@keyframes chPropGlow{0%,100%{box-shadow:0 0 18px rgba(${ACCENT_A},.35), 0 10px 26px rgba(0,0,0,.5)}
  50%{box-shadow:0 0 40px rgba(${ACCENT_A},.75), 0 10px 26px rgba(0,0,0,.5)}}

/* ---------- beat dissolve (fade out -> swap pose/text -> fade in) ---------- */
.chs-fig,.cho-fig{transition:opacity .3s ease}
.chs-fig.ch-fade,.cho-fig.ch-fade{opacity:0}
.chs-text{transition:opacity .24s ease}
.chs-bubble.ch-dissolve .chs-text,.chs-bubble.ch-dissolve .ch-prop{opacity:0}

/* ---------- scene (fullscreen) ---------- */
.chs-root{position:absolute;inset:0;z-index:41;pointer-events:none;overflow:hidden;opacity:0;transition:opacity .45s ease}
.chs-root.ch-on{opacity:1;pointer-events:auto;cursor:pointer}
.chs-dim{position:absolute;inset:0;background:
  radial-gradient(80% 90% at 24% 55%, rgba(${ACCENT_A},.14), transparent 60%),
  radial-gradient(60% 70% at 70% 20%, rgba(150,80,255,.12), transparent 65%),
  linear-gradient(90deg, rgba(10,3,18,.60) 0%, rgba(10,3,18,.76) 55%, rgba(10,3,18,.88) 100%)}
.chs-back{position:absolute;inset:-8%;
  background:radial-gradient(52% 60% at 26% 46%,rgba(${ACCENT_A},.26),transparent 62%),
             radial-gradient(60% 60% at 30% 62%,rgba(${ACCENT_B},.14),transparent 66%),
             radial-gradient(70% 55% at 65% 30%,rgba(150,80,255,.18),transparent 70%);
  filter:saturate(1.1);animation:chsBack 24s ease-in-out infinite alternate}
@keyframes chsBack{from{transform:scale(1)}to{transform:scale(1.08) translateY(-2%)}}
.chs-bg{position:absolute;inset:0;background-size:cover;background-position:center;opacity:0;transition:opacity .6s}
.chs-bg.ch-on{opacity:.85}
.chs-fig{position:absolute;left:3%;bottom:-4vh;height:94vh;z-index:2;animation:chBreathe 6s ease-in-out infinite;transform-origin:50% 100%}
.chs-fig img{height:100%;width:auto;display:block}
.chs-fig.ch-wide{height:auto;width:min(62vw,110vh);bottom:-2vh;left:2%}
.chs-fig.ch-wide img{width:100%;height:auto}
.chs-vig{position:absolute;inset:0;z-index:3;background:radial-gradient(130% 110% at 30% 45%,transparent 52%,rgba(0,0,0,.5) 100%)}
.chs-skip{position:absolute;right:5%;bottom:5.5%;z-index:7;font-size:.72rem;letter-spacing:.16em;text-transform:uppercase;
  color:rgba(255,255,255,.5);text-shadow:0 1px 6px #000;opacity:0;transition:opacity .5s;pointer-events:none}
.chs-root.ch-on .chs-skip{opacity:.85;transition-delay:.7s}
.chs-bubble{position:absolute;right:4.5%;top:50%;transform:translateY(-46%) scale(.96);
  width:min(63vw,1160px);z-index:6;opacity:0;transition:opacity .3s ease, transform .3s cubic-bezier(.2,.8,.2,1);
  background:rgba(12,4,20,.93);border:3px solid rgb(${ACCENT_A});border-radius:24px;padding:30px 38px 34px;
  box-shadow:0 0 46px rgba(${ACCENT_A},.5), 0 18px 50px rgba(0,0,0,.6), inset 0 0 34px rgba(${ACCENT_A},.13)}
.chs-root.ch-wide-root .chs-bubble{width:min(55vw,880px)}
.chs-bubble.ch-on{opacity:1;transform:translateY(-50%) scale(1)}
.chs-name{color:rgb(${ACCENT_A});font-weight:800;letter-spacing:.28em;text-transform:uppercase;font-size:.82rem;
  margin-bottom:.6em;text-shadow:0 0 14px rgba(${ACCENT_A},.75)}
.chs-text{font-size:clamp(26px,3.6vh,42px);line-height:1.42;color:#fdf1ff;font-weight:600;
  text-shadow:0 1px 10px #000,0 0 22px rgba(${ACCENT_A},.3);min-height:1.4em}

/* ---------- say (corner commentary, non-pausing) ---------- */
.chc-root{position:absolute;left:0;bottom:0;z-index:38;pointer-events:none;opacity:0;
  transition:opacity .35s ease;display:flex;align-items:flex-end;gap:0;padding:0 0 1.2vh 1vh;max-width:56vw}
.chc-root.ch-on{opacity:1}
.chc-fig{height:24vh;flex:0 0 auto;animation:chBreathe 6s ease-in-out infinite;transform-origin:50% 100%}
.chc-fig img{height:100%;width:auto;display:block}
.chc-fig.ch-wide{height:16vh}
.chc-bubble{position:relative;z-index:2;margin-bottom:3.5vh;margin-left:-4.5vh;background:rgba(12,4,20,.88);
  border:2px solid rgba(${ACCENT_A},.9);border-radius:16px;
  padding:12px 18px 14px;max-width:40vw;box-shadow:0 0 24px rgba(${ACCENT_A},.35), 0 8px 26px rgba(0,0,0,.5)}
.chc-bubble .ch-prop{min-width:8vh;height:8vh;padding:0 1vh;top:-6.6vh;right:-1.5vh;border-radius:4vh}
.chc-bubble .ch-prop img{width:6vh;height:6vh}
.chc-bubble .ch-prop span{font-size:4.2vh}
.chc-name{color:rgb(${ACCENT_A});font-weight:800;letter-spacing:.24em;text-transform:uppercase;font-size:.62rem;
  margin-bottom:.45em;text-shadow:0 0 10px rgba(${ACCENT_A},.7)}
.chc-text{font-size:clamp(15px,2vh,20px);line-height:1.4;color:#fdf1ff;font-weight:600;text-shadow:0 1px 8px #000}

/* ---------- overlay (world-hold pause card) ---------- */
.cho-root{position:absolute;inset:0;z-index:40;pointer-events:none;overflow:hidden;opacity:0;transition:opacity .38s ease}
.cho-root.ch-on{opacity:1;pointer-events:auto;cursor:pointer}
.cho-dim{position:absolute;inset:0;background:
  radial-gradient(70% 80% at 50% 42%, rgba(${ACCENT_A},.14), transparent 62%),
  linear-gradient(180deg, rgba(10,3,18,.72) 0%, rgba(10,3,18,.8) 60%, rgba(10,3,18,.88) 100%)}
.cho-fig{position:absolute;left:3%;bottom:-2vh;height:66vh;z-index:2;animation:chBreathe 6s ease-in-out infinite;transform-origin:50% 100%}
.cho-fig img{height:100%;width:auto;display:block}
.cho-fig.ch-wide{height:auto;width:min(46vw,80vh);bottom:-1vh}
.cho-fig.ch-wide img{width:100%;height:auto}
.cho-card{position:absolute;left:53%;top:50%;transform:translate(-50%,-46%) scale(.965);
  width:min(64vw,620px);z-index:3;opacity:0;transition:opacity .3s ease, transform .34s cubic-bezier(.2,.8,.2,1);
  background:rgba(12,4,20,.94);border:2.5px solid rgb(${ACCENT_A});border-radius:22px;padding:30px 38px 26px;
  box-shadow:0 0 44px rgba(${ACCENT_A},.45), 0 18px 50px rgba(0,0,0,.6), inset 0 0 30px rgba(${ACCENT_A},.11)}
.cho-root.ch-on .cho-card{opacity:1;transform:translate(-50%,-50%) scale(1)}
.cho-name{color:rgb(${ACCENT_A});font-weight:800;letter-spacing:.28em;text-transform:uppercase;font-size:.72rem;
  margin-bottom:.8em;text-shadow:0 0 12px rgba(${ACCENT_A},.7)}
.cho-text{font-size:clamp(20px,2.8vh,30px);line-height:1.45;color:#fdf1ff;font-weight:600;
  text-shadow:0 1px 10px #000,0 0 18px rgba(${ACCENT_A},.3);min-height:1.4em}
.cho-sub{margin-top:1em;font-size:clamp(14px,1.9vh,18px);line-height:1.5;color:#d9c9e8;font-weight:500}
.cho-hint{margin-top:1.4em;font-size:.7rem;letter-spacing:.18em;text-transform:uppercase;
  color:rgba(255,255,255,.42);opacity:0;transition:opacity .4s}
.cho-root.ch-on .cho-hint{opacity:.8;transition-delay:.5s}
`;

export function createCheshireVn(hud, opts = {}) {
  const haltTunnel = typeof opts.haltTunnel === 'function' ? opts.haltTunnel : null;
  const duckAudio = typeof opts.duckAudio === 'function' ? opts.duckAudio : null;
  const driftDuck = typeof opts.driftDuck === 'function' ? opts.driftDuck : null;
  const send = typeof opts.send === 'function' ? opts.send : null;
  const script = opts.script || {};
  const NAME = script.speaker || 'THE CHESHIRE';

  if (!document.getElementById('chvn-style')) {
    const st = document.createElement('style');
    st.id = 'chvn-style'; st.textContent = CSS; document.head.appendChild(st);
  }

  // ---------- DOM: three roots, built once ----------
  const sceneRoot = document.createElement('div');
  sceneRoot.className = 'chs-root'; sceneRoot.hidden = true;
  sceneRoot.innerHTML =
    '<div class="chs-back"></div><div class="chs-dim"></div><div class="chs-bg"></div>' +
    '<div class="chs-fig"><img class="ch-fig-img" alt=""></div>' +
    '<div class="chs-vig"></div>' +
    '<div class="chs-skip">click ▸</div>' +
    '<div class="chs-bubble"><div class="ch-prop" hidden><img alt=""><span></span></div><div class="chs-name"></div>' +
    '<div class="chs-text"><span class="chs-body"></span><span class="ch-caret ch-off"></span></div></div>';
  hud.appendChild(sceneRoot);

  const sayRoot = document.createElement('div');
  sayRoot.className = 'chc-root'; sayRoot.hidden = true;
  sayRoot.innerHTML =
    '<div class="chc-fig"><img class="ch-fig-img" alt=""></div>' +
    '<div class="chc-bubble"><div class="ch-prop" hidden><img alt=""><span></span></div>' +
    '<div class="chc-name"></div><div class="chc-text"></div></div>';
  hud.appendChild(sayRoot);

  const overlayRoot = document.createElement('div');
  overlayRoot.className = 'cho-root'; overlayRoot.hidden = true;
  overlayRoot.innerHTML =
    '<div class="cho-dim"></div>' +
    '<div class="cho-fig"><img class="ch-fig-img" alt=""></div>' +
    '<div class="cho-card"><div class="ch-prop" hidden><img alt=""><span></span></div><div class="cho-name"></div>' +
    '<div class="cho-text"><span class="cho-body"></span><span class="ch-caret ch-off"></span></div>' +
    '<div class="cho-sub"></div><div class="cho-hint">click to continue ▸</div></div>';
  hud.appendChild(overlayRoot);

  const $s = (q) => sceneRoot.querySelector(q);
  const $c = (q) => sayRoot.querySelector(q);
  const $o = (q) => overlayRoot.querySelector(q);
  $s('.chs-name').textContent = NAME;
  $c('.chc-name').textContent = NAME;
  $o('.cho-name').textContent = NAME;

  // ---------- pose loading (static webp cutouts; orientation-aware) ----------
  const poseCache = new Map();   // pose -> {url, wide|null until decoded}
  function poseInfo(pose) {
    const key = pose || 'base';
    let rec = poseCache.get(key);
    if (!rec) {
      rec = { url: POSE_URL(key), wide: null };
      poseCache.set(key, rec);
      const im = new Image();
      im.onload = () => { rec.wide = im.naturalWidth > im.naturalHeight; };
      im.src = rec.url;
    }
    return rec;
  }
  function setPose(figEl, imgEl, pose, rootEl) {
    const rec = poseInfo(pose);
    imgEl.src = rec.url;
    const apply = () => {
      figEl.classList.toggle('ch-wide', !!rec.wide);
      if (rootEl) rootEl.classList.toggle('ch-wide-root', !!rec.wide);
    };
    if (rec.wide == null) { imgEl.onload = () => { rec.wide = imgEl.naturalWidth > imgEl.naturalHeight; apply(); }; }
    else apply();
  }
  // The show-and-tell chip: a beat's `prop` key floats the matching sprite/emoji
  // over the bubble so "a live one" / "gold" is a picture, not just a word.
  function setProp(boxEl, key) {
    const el = boxEl.querySelector('.ch-prop');
    if (!el) return;
    const rec = key ? PROP_ART[key] : null;
    const img = el.querySelector('img'), sp = el.querySelector('span');
    if (!rec) {
      el.hidden = true;
      el.classList.remove('ch-prop-glow', 'ch-prop-spin');
      img.removeAttribute('src'); sp.textContent = '';
      return;
    }
    el.hidden = false;
    el.classList.toggle('ch-prop-glow', !!rec.glow);
    el.classList.toggle('ch-prop-spin', !!rec.spin);
    if (rec.img) { img.src = rec.img; img.style.display = ''; sp.style.display = 'none'; sp.textContent = ''; }
    else { img.removeAttribute('src'); img.style.display = 'none'; sp.style.display = ''; sp.textContent = rec.emoji; }
  }
  function prime() {
    try { for (const p of Object.keys(script.poses || {})) poseInfo(p); } catch (e) { /* ignore */ }
  }

  // ---------- one shared voice (mp3 -> analyser aura), vnPortrait's proven graph ----------
  let actx = null, audioEl = null, analyser = null, gain = null, vdata = null,
      voicePlaying = false, voiceEndCb = null;
  function ensureVoiceGraph() {
    if (actx) return;
    try {
      audioEl = new Audio(); audioEl.crossOrigin = 'anonymous';
      actx = new (window.AudioContext || window.webkitAudioContext)();
      const src = actx.createMediaElementSource(audioEl);
      analyser = actx.createAnalyser(); analyser.fftSize = 512;
      vdata = new Uint8Array(analyser.fftSize);
      gain = actx.createGain();
      src.connect(analyser); analyser.connect(gain); gain.connect(actx.destination);
      const endVoice = () => { voicePlaying = false; if (voiceEndCb) { const c = voiceEndCb; voiceEndCb = null; c(); } };
      audioEl.addEventListener('ended', endVoice);
      // a missing/blocked mp3 must unblock the beat immediately
      audioEl.addEventListener('error', endVoice);
    } catch (e) { actx = null; }
  }
  function voUrl(beat) {
    if (!script.voReady || beat.vo === false) return null;
    const id = beat.id;
    if (!id) return null;
    return '/dtrh/assets/vn/vo/' + (script.voFolder || 'cheshire') + '/' + id + '.mp3';
  }
  function startVoice(url) {
    ensureVoiceGraph(); if (!actx || !url) return;
    try {
      if (actx.state === 'suspended') actx.resume();
      if (gain) { try { gain.gain.cancelScheduledValues(actx.currentTime); gain.gain.setValueAtTime(1, actx.currentTime); } catch (e) {} }
      audioEl.src = url; audioEl.currentTime = 0; voicePlaying = true;
      // play() rejecting (404/autoplay) must resolve the wait, not hang the beat
      audioEl.play().catch(() => { voicePlaying = false; if (voiceEndCb) { const c = voiceEndCb; voiceEndCb = null; c(); } });
    } catch (e) { /* ignore */ }
  }
  function fadeVoice(ms) {
    if (!voicePlaying) { stopVoice(); return; }
    const secs = (ms || 260) / 1000;
    try {
      if (gain && actx) {
        const t = actx.currentTime;
        gain.gain.cancelScheduledValues(t);
        gain.gain.setValueAtTime(Math.max(0.0001, gain.gain.value), t);
        gain.gain.linearRampToValueAtTime(0.0001, t + secs);
      }
    } catch (e) { /* ignore */ }
    setTimeout(stopVoice, (ms || 260) + 30);
  }
  function stopVoice() {
    voicePlaying = false;
    try { if (audioEl) audioEl.pause(); } catch (e) {}
    if (voiceEndCb) { const c = voiceEndCb; voiceEndCb = null; c(); }
  }
  function waitVoice(maxMs) {
    if (!voicePlaying) return Promise.resolve();
    return new Promise((res) => { voiceEndCb = res; setTimeout(() => { if (voiceEndCb === res) { voiceEndCb = null; res(); } }, maxMs || 15000); });
  }
  function readVoice() {
    if (!voicePlaying || !analyser) return 0;
    analyser.getByteTimeDomainData(vdata);
    let s = 0; for (let i = 0; i < vdata.length; i++) { const v = (vdata[i] - 128) / 128; s += v * v; }
    return Math.min(1, Math.sqrt(s / vdata.length) * 2.4);
  }

  // ---------- the aura loop (--vnG) runs only while any surface is up ----------
  // The raw per-frame RMS flickers at syllable rate; an attack/decay envelope
  // (quick-ish up, slow down) turns it into a soft breathing shimmer instead.
  let raf = 0, glowT0 = 0, glowEnv = 0;
  function anySurfaceUp() { return !sceneRoot.hidden || !sayRoot.hidden || !overlayRoot.hidden; }
  function startGlow() {
    if (raf) return;
    glowT0 = performance.now();
    const tick = (now) => {
      if (!anySurfaceUp()) { raf = 0; return; }
      const ph = (now - glowT0) / 1000;
      const idle = 0.10 + Math.sin(ph * 0.9) * 0.045 + Math.sin(ph * 0.4) * 0.025;
      const target = Math.max(idle, readVoice());
      glowEnv += (target - glowEnv) * (target > glowEnv ? 0.09 : 0.035);
      const glow = Math.min(1, glowEnv).toFixed(3);
      sceneRoot.style.setProperty('--vnG', glow);
      sayRoot.style.setProperty('--vnG', glow);
      overlayRoot.style.setProperty('--vnG', glow);
      raf = requestAnimationFrame(tick);
    };
    raf = requestAnimationFrame(tick);
  }

  // ---------- world-hold contract (scene + overlay only; say NEVER holds) ----------
  let restoreTimer = 0;
  function sceneHold(on) {
    if (on && restoreTimer) { clearTimeout(restoreTimer); restoreTimer = 0; }
    try { if (haltTunnel) haltTunnel(on); } catch (e) { /* ignore */ }
    try { if (duckAudio) duckAudio(on); } catch (e) { /* ignore */ }
  }
  function scheduleResume() {
    if (!haltTunnel && !duckAudio) return;
    if (restoreTimer) clearTimeout(restoreTimer);
    restoreTimer = setTimeout(() => {
      restoreTimer = 0;
      try { if (haltTunnel) haltTunnel(false); } catch (e) {}
      try { if (duckAudio) duckAudio(false); } catch (e) {}
    }, RESUME_MS);
  }

  const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
  let token = 0;   // bumped by cancel/dispose and every new hold-surface

  // ---------- typewriter (punctuation breathes) ----------
  function typewrite(bodyEl, caretEl, text, tk, isDone) {
    return new Promise((res) => {
      bodyEl.textContent = ''; caretEl.classList.remove('ch-off');
      const chars = Array.from(text || ''); let i = 0;
      const step = () => {
        if (tk !== token) { caretEl.classList.add('ch-off'); return res(false); }
        if (isDone && isDone()) { bodyEl.textContent = text || ''; caretEl.classList.add('ch-off'); return res(true); }
        bodyEl.textContent = chars.slice(0, i).join('');
        if (i >= chars.length) { caretEl.classList.add('ch-off'); return res(true); }
        const c = chars[i]; i++;
        const d = /[.,!?…]/.test(c) ? 200 : (c === ' ' ? 30 : 24);
        setTimeout(step, d);
      };
      step();
    });
  }

  // ============================ SCENE (fullscreen) ============================
  let sceneUp = false;
  let sceneClickArm = 0;
  let sceneAdvance = null;   // pending click resolver

  sceneRoot.addEventListener('pointerdown', () => {
    if (!sceneRoot.classList.contains('ch-on')) return;
    if (performance.now() < sceneClickArm) return;
    if (sceneAdvance) { const a = sceneAdvance; sceneAdvance = null; a(); }
  });
  const onSceneKey = (e) => {
    if (!sceneUp) return;
    if (e.key === 'Shift' || e.key === 'Control' || e.key === 'Alt' || e.key === 'Meta') return;
    e.preventDefault(); e.stopPropagation();
    if (e.stopImmediatePropagation) e.stopImmediatePropagation();
    if (performance.now() < sceneClickArm) return;
    if (sceneAdvance) { const a = sceneAdvance; sceneAdvance = null; a(); }
  };
  window.addEventListener('keydown', onSceneKey, true);

  /**
   * Play a fullscreen sequence. `beats` = [{id,pose,text,hold?},...]. The world
   * is held ONCE across the whole run of beats. Click while typing completes
   * the line; click after completes advances. Resolves true if it played out,
   * false if cancelled.
   */
  async function scene(beats, sceneOpts = {}) {
    if (!beats || !beats.length) return false;
    cancelSay();                       // scene preempts commentary
    if (overlayUp) dismissOverlay();   // and any pause card
    const tk = ++token;
    sceneUp = true;
    const bgEl = $s('.chs-bg');
    if (sceneOpts.bg) { bgEl.style.backgroundImage = 'url("' + sceneOpts.bg + '")'; bgEl.classList.add('ch-on'); }
    else { bgEl.classList.remove('ch-on'); bgEl.style.backgroundImage = ''; }
    sceneRoot.hidden = false;
    requestAnimationFrame(() => sceneRoot.classList.add('ch-on'));
    sceneHold(true);
    startGlow();
    const fig = $s('.chs-fig'), img = $s('.chs-fig img'),
          bubble = $s('.chs-bubble'), body = $s('.chs-body'), caret = $s('.ch-caret');
    bubble.classList.add('ch-on');
    let completed = true;
    let lastPose = null;
    try {
      for (const beat of beats) {
        if (tk !== token) { completed = false; break; }
        // slide dissolve: fade the old pose + line out, swap, fade back in
        if (lastPose != null) {
          if (beat.pose !== lastPose) fig.classList.add('ch-fade');
          bubble.classList.add('ch-dissolve');
          await sleep(270);
          if (tk !== token) { completed = false; break; }
          body.textContent = '';
        }
        setPose(fig, img, beat.pose, sceneRoot);
        setProp(bubble, beat.prop);
        fig.classList.remove('ch-fade');
        bubble.classList.remove('ch-dissolve');
        lastPose = beat.pose;
        sceneClickArm = performance.now() + ARM_MS;
        let clicked = false;
        const clickP = new Promise((res) => { sceneAdvance = () => { clicked = true; res(); }; });
        startVoice(voUrl(beat));
        const typed = typewrite(body, caret, beat.text, tk, () => clicked);
        // First click: finish the line + cut the voice short. Then wait for a
        // SECOND click (or, on auto-hold beats, the hold playing out) to advance.
        await Promise.race([Promise.all([typed, waitVoice()]), clickP]);
        if (tk !== token) { completed = false; break; }
        if (clicked) {
          fadeVoice(240); await typed;
          if (tk !== token) { completed = false; break; }
          sceneClickArm = performance.now() + 150;
          await new Promise((res) => { sceneAdvance = res; });
        } else {
          // line played out on its own: auto-hold beats advance themselves,
          // everything else waits for the click
          const holdMs = beat.hold != null ? beat.hold : null;
          if (holdMs != null) await Promise.race([sleep(holdMs), clickP]);
          else await clickP;
        }
        if (tk !== token) { completed = false; break; }
        sceneAdvance = null;
        if (beat.onDone) { try { beat.onDone(); } catch (e) { /* ignore */ } }
      }
    } finally {
      sceneAdvance = null;
      if (tk === token) hideScene();
    }
    return completed;
  }

  function hideScene() {
    sceneUp = false;
    sceneRoot.classList.remove('ch-on');
    $s('.chs-bubble').classList.remove('ch-on');
    $s('.chs-bubble').classList.remove('ch-dissolve');
    $s('.chs-fig').classList.remove('ch-fade');
    fadeVoice(280);
    scheduleResume();
    setTimeout(() => { if (!sceneUp) sceneRoot.hidden = true; }, 470);
  }

  // ============================ SAY (corner commentary) ============================
  const sayQueue = [];
  let sayActive = false;
  let speakingSent = false;
  function setSpeaking(on) {
    if (speakingSent === !!on) return;
    speakingSent = !!on;
    try { if (send) send({ type: 'vn-speaking', on: !!on }); } catch (e) { /* ignore */ }
    try { if (driftDuck) driftDuck(on ? 0.25 : 1); } catch (e) { /* ignore */ }
  }

  /**
   * Queue a corner-commentary line. NON-BLOCKING and never holds the world:
   * the fall keeps falling, the drift ducks to 0.25 and the native barks stand
   * down (vn-speaking) while she talks. Text is always shown (subtitle).
   */
  function say(beat) {
    if (!beat || !beat.text) return;
    sayQueue.push(beat);
    if (!sayActive) drainSay();
  }
  async function drainSay() {
    sayActive = true;
    const fig = $c('.chc-fig'), img = $c('.chc-fig img'), textEl = $c('.chc-text');
    try {
      while (sayQueue.length) {
        // a hold surface owns the stage: park until it's gone
        if (sceneUp || overlayUp) { await sleep(300); continue; }
        const beat = sayQueue.shift();
        const tk = token;
        setPose(fig, img, beat.pose);
        setProp($c('.chc-bubble'), beat.prop);
        textEl.textContent = beat.text;
        sayRoot.hidden = false;
        requestAnimationFrame(() => sayRoot.classList.add('ch-on'));
        startGlow();
        setSpeaking(true);
        startVoice(voUrl(beat));
        // Duration: the voiceover if it's playing, else read-time off the text.
        const readMs = Math.max(2400, Math.min(9000, 1000 + (beat.text.length * 52)));
        if (voicePlaying) await waitVoice();
        else await sleep(readMs);
        if (tk !== token) break;
        // linger a moment so the tail of the line can be read
        await sleep(600);
        if (tk !== token) break;
        setSpeaking(false);
        if (beat.onDone) { try { beat.onDone(); } catch (e) { /* ignore */ } }
        if (sayQueue.length) { await sleep(SAY_GAP_MS); continue; }
        sayRoot.classList.remove('ch-on');
        await sleep(380);
      }
    } finally {
      sayActive = false;
      setSpeaking(false);
      sayRoot.classList.remove('ch-on');
      setTimeout(() => { if (!sayActive) sayRoot.hidden = true; }, 380);
    }
  }
  function cancelSay() {
    sayQueue.length = 0;
    setSpeaking(false);
    sayRoot.classList.remove('ch-on');
    setTimeout(() => { if (!sayActive) sayRoot.hidden = true; }, 380);
  }

  // ============================ OVERLAY (world-hold pause card) ============================
  let overlayUp = false;
  let overlayArm = 0;
  let overlayResolve = null;

  function dismissOverlay() {
    if (!overlayUp) return;
    overlayUp = false;
    overlayRoot.classList.remove('ch-on');
    fadeVoice(240);
    scheduleResume();
    setTimeout(() => { if (!overlayUp) overlayRoot.hidden = true; }, 460);
    if (overlayResolve) { const r = overlayResolve; overlayResolve = null; r(true); }
  }
  overlayRoot.addEventListener('pointerdown', () => {
    if (!overlayRoot.classList.contains('ch-on')) return;
    if (performance.now() < overlayArm) return;
    dismissOverlay();
  });
  const onOverlayKey = (e) => {
    if (!overlayUp) return;
    if (e.key === 'Shift' || e.key === 'Control' || e.key === 'Alt' || e.key === 'Meta') return;
    // swallow so ESC->pause / number keys->consumables can't leak under the card
    e.preventDefault(); e.stopPropagation();
    if (e.stopImmediatePropagation) e.stopImmediatePropagation();
    if (performance.now() < overlayArm) return;
    dismissOverlay();
  };
  window.addEventListener('keydown', onOverlayKey, true);

  /**
   * A single held beat: freeze the world (lessonCard contract), her portrait +
   * the line + optional sub-copy, click/any-key to resume. Resolves when dismissed.
   */
  function overlay(beat) {
    if (!beat || !beat.text) return Promise.resolve(false);
    cancelSay();
    if (sceneUp) return Promise.resolve(false);   // a scene outranks a card
    token++;
    const tk = token;
    overlayUp = true;
    const fig = $o('.cho-fig'), img = $o('.cho-fig img'),
          body = $o('.cho-body'), caret = $o('.ch-caret'), sub = $o('.cho-sub');
    setPose(fig, img, beat.pose);
    setProp($o('.cho-card'), beat.prop);
    sub.textContent = beat.sub || '';
    sub.style.display = beat.sub ? '' : 'none';
    overlayRoot.hidden = false;
    requestAnimationFrame(() => overlayRoot.classList.add('ch-on'));
    sceneHold(true);
    startGlow();
    overlayArm = performance.now() + ARM_MS;
    startVoice(voUrl(beat));
    typewrite(body, caret, beat.text, tk, () => !overlayUp);
    return new Promise((res) => { overlayResolve = res; });
  }

  // ---------- lifecycle ----------
  function cancel() {
    token++;
    cancelSay();
    if (overlayUp) dismissOverlay();
    if (sceneUp) hideScene();
    stopVoice();
  }
  function dispose() {
    cancel();
    if (restoreTimer) { clearTimeout(restoreTimer); restoreTimer = 0; sceneHold(false); }
    window.removeEventListener('keydown', onSceneKey, true);
    window.removeEventListener('keydown', onOverlayKey, true);
    try { sceneRoot.remove(); sayRoot.remove(); overlayRoot.remove(); } catch (e) {}
    try { if (actx) { actx.close(); actx = null; } } catch (e) {}
  }

  const api = {
    scene, say, overlay, cancel, dispose, prime,
    isBusy: () => sceneUp || overlayUp || sayActive || sayQueue.length > 0,
    /** True only while a HOLD surface (scene/overlay) owns the world. */
    isHolding: () => sceneUp || overlayUp,
    isSceneUp: () => sceneUp,
  };

  // dev seam: --dtrh-m2test drives the surfaces from the console
  try {
    if (typeof window !== 'undefined' && window.__dtrhVnTest) {
      window.__cheshire = Object.assign(window.__cheshire || {}, {
        say: (text, pose, prop) => say({ id: 'dev', text: String(text || 'testing, kitten.'), pose: pose || 'base', prop, vo: false }),
        overlay: (text, pose, prop) => overlay({ id: 'dev', text: String(text || 'a held moment.'), pose: pose || 'lean_fwd', prop, vo: false }),
        scene: (beats) => scene(Array.isArray(beats) ? beats
          : [{ id: 'dev1', pose: 'couch_invite', text: 'a fullscreen scene, kitten.', prop: 'live', vo: false },
             { id: 'dev2', pose: 'lean_fwd', text: 'click to advance. click again to leave.', prop: 'gold', vo: false }]),
        poses: () => Object.keys(script.poses || {}),
        props: () => Object.keys(PROP_ART),
        cancel,
      });
    }
  } catch (e) { /* ignore */ }

  return api;
}
