/* ============================================================================
 * vnPortrait.js - the Visual-Novel portrait overlay for the scripted first
 * descents. A beat halts the tunnel, brings the active persona in BIG on the
 * left (about a 3/4 figure), plays her pre-trimmed emote segment, freezes, and
 * she speaks: a mod-coloured high-contrast speech bubble on the right types her
 * line out (typewriter) while her voiceover plays and the rim-aura/bloom/motes
 * breathe with her voice. When she's done talking it fades and the tunnel
 * resumes.
 *
 * Frames are PRE-TRIMMED at build time (the segment editor's in/out) so a beat
 * just plays frame 0..N then holds - no runtime GIF decode. Per-persona lines +
 * voiceover + emote all resolve from assets/vn/manifest.json by active mod.
 *
 * API:  const vn = createVnPortrait(hud, { getModId, haltTunnel });
 *       await vn.beat('intro1');            // manifest beat id
 *       await vn.beat({ emote, line, voUrl });
 *       vn.hide();  vn.dispose();
 * ==========================================================================*/

const TINT = {
  'builtin-bambisleep': { a: '255,105,180', b: '255,60,120' },
  'builtin-sissyhypno': { a: '255,64,180',  b: '180,60,255' },
  'builtin-locked':     { a: '150,80,255',  b: '90,120,255' },
};
const NAME = {
  'builtin-bambisleep': 'BAMBI',
  'builtin-sissyhypno': 'SISSY',
  'builtin-locked':     'CIRCE',
};
const DEFAULT_TINT = TINT['builtin-sissyhypno'];

const CSS = `
.vn-root{position:absolute;inset:0;z-index:40;pointer-events:none;overflow:hidden;opacity:0;transition:opacity .45s ease}
.vn-root.vn-on{opacity:1;pointer-events:auto;cursor:pointer}
.vn-skip{position:absolute;right:5%;bottom:5.5%;z-index:7;font-size:.72rem;letter-spacing:.16em;text-transform:uppercase;
  color:rgba(255,255,255,.5);text-shadow:0 1px 6px #000;opacity:0;transition:opacity .5s;pointer-events:none}
.vn-root.vn-on .vn-skip{opacity:.85;transition-delay:.7s}
.vn-dim{position:absolute;inset:0;background:
  radial-gradient(80% 90% at 24% 55%, rgba(var(--vnA),.16), transparent 60%),
  linear-gradient(90deg, rgba(4,2,9,.55) 0%, rgba(4,2,9,.72) 55%, rgba(4,2,9,.86) 100%)}
.vn-back{position:absolute;inset:-8%;
  background:radial-gradient(52% 60% at 26% 46%,rgba(var(--vnA),.30),transparent 62%),
             radial-gradient(60% 60% at 30% 62%,rgba(var(--vnB),.20),transparent 66%);
  filter:saturate(1.1);animation:vnBack 24s ease-in-out infinite alternate}
@keyframes vnBack{from{transform:scale(1)}to{transform:scale(1.08) translateY(-2%)}}

/* ---- the figure: big, left, ~3/4 (lower body cropped off the bottom) ---- */
.vn-figure{position:absolute;left:1.5%;bottom:-24vh;height:124vh;z-index:2;
  filter:drop-shadow(0 20px 34px rgba(0,0,0,.6))}
.vn-kb{position:relative;height:100%;animation:vnKb 22s linear infinite;transform-origin:46% 42%;will-change:transform}
@keyframes vnKb{
  0%{transform:scale(1.03) translate(0,0)}
  25%{transform:scale(1.06) translate(-.8%,-.9%)}
  50%{transform:scale(1.04) translate(-1.2%,0)}
  75%{transform:scale(1.06) translate(-.4%,.8%)}
  100%{transform:scale(1.03) translate(0,0)}}
.vn-char{position:absolute;bottom:0;left:0;height:100%;width:auto;
  filter:drop-shadow(0 0 calc(7px + var(--vnG,0)*28px) rgba(var(--vnA),calc(.4 + var(--vnG,0)*.5)))
         drop-shadow(0 0 calc(3px + var(--vnG,0)*11px) rgba(var(--vnA),calc(.55 + var(--vnG,0)*.4)));
  will-change:filter}
.vn-halo{position:absolute;bottom:0;left:0;height:100%;width:auto;z-index:-1;
  filter:blur(calc(11px + var(--vnG,0)*22px)) saturate(1.6) brightness(1.25);
  mix-blend-mode:screen;opacity:calc(.35 + var(--vnG,0)*.5)}
.vn-shim{position:absolute;bottom:0;left:0;height:100%;width:80vh;
  background:linear-gradient(115deg,transparent 42%,rgba(255,255,255,.5) 50%,rgba(var(--vnA),.32) 53%,transparent 60%);
  background-size:280% 280%;mix-blend-mode:screen;opacity:0;animation:vnSheen 5.5s linear infinite;
  -webkit-mask-size:auto 100%;mask-size:auto 100%;-webkit-mask-position:left bottom;mask-position:left bottom;
  -webkit-mask-repeat:no-repeat;mask-repeat:no-repeat}
@keyframes vnSheen{0%{background-position:170% 0;opacity:0}6%{opacity:.85}40%{background-position:-130% 0;opacity:.85}48%{background-position:-130% 0;opacity:0}100%{background-position:-130% 0;opacity:0}}

.vn-parts{position:absolute;inset:0;z-index:3;pointer-events:none}

/* ---- the speech bubble: right, big, high-contrast, mod-coloured, typewriter ---- */
.vn-bubble{position:absolute;right:calc(4.5% + 300px);top:52%;transform:translateY(-46%) scale(.96);
  width:min(60vw,858px);z-index:6;opacity:0;transition:opacity .3s ease, transform .3s cubic-bezier(.2,.8,.2,1);
  background:rgba(9,4,15,.93);border:3px solid rgb(var(--vnA));border-radius:24px;padding:34px 42px 39px;
  box-shadow:0 0 46px rgba(var(--vnA),.55), 0 18px 50px rgba(0,0,0,.6), inset 0 0 34px rgba(var(--vnA),.14)}
.vn-bubble.vn-on{opacity:1;transform:translateY(-50%) scale(1)}
.vn-bubble .vn-tail{position:absolute;left:-22px;top:50%;width:0;height:0;transform:translateY(-50%);
  border-top:16px solid transparent;border-bottom:16px solid transparent;border-right:24px solid rgb(var(--vnA));
  filter:drop-shadow(-3px 0 0 rgba(var(--vnA),.001))}
.vn-bubble .vn-tail::after{content:"";position:absolute;left:5px;top:-13px;width:0;height:0;
  border-top:13px solid transparent;border-bottom:13px solid transparent;border-right:20px solid rgba(9,4,15,.93)}
.vn-name{color:rgb(var(--vnA));font-weight:800;letter-spacing:.28em;text-transform:uppercase;font-size:.82rem;
  margin-bottom:.65em;text-shadow:0 0 14px rgba(var(--vnA),.75)}
.vn-text{font-size:clamp(32px,4.3vh,50px);line-height:1.42;color:#fdf1ff;font-weight:600;
  text-shadow:0 1px 10px #000,0 0 22px rgba(var(--vnA),.35);min-height:1.4em}
.vn-caret{display:inline-block;width:.62ch;margin-left:1px;color:rgb(var(--vnA));animation:vnCaret .8s steps(1) infinite}
.vn-caret.vn-off{display:none}
@keyframes vnCaret{50%{opacity:0}}

.vn-vig{position:absolute;inset:0;z-index:4;background:radial-gradient(130% 110% at 30% 45%,transparent 52%,rgba(0,0,0,.55) 100%)}
.vn-grain{position:absolute;inset:0;z-index:5;opacity:.06;mix-blend-mode:overlay;
  background-image:url("data:image/svg+xml;utf8,<svg xmlns='http://www.w3.org/2000/svg' width='120' height='120'><filter id='n'><feTurbulence type='fractalNoise' baseFrequency='0.9' numOctaves='2'/></filter><rect width='100%25' height='100%25' filter='url(%23n)'/></svg>");
  animation:vnGrain 1.2s steps(3) infinite}
@keyframes vnGrain{0%{transform:translate(0,0)}33%{transform:translate(-4px,3px)}66%{transform:translate(3px,-2px)}100%{transform:translate(0,0)}}
`;

export function createVnPortrait(hud, opts = {}) {
  const getModId = opts.getModId || (() => 'builtin-sissyhypno');
  const haltTunnel = typeof opts.haltTunnel === 'function' ? opts.haltTunnel : null;
  const duckAudio = typeof opts.duckAudio === 'function' ? opts.duckAudio : null;

  if (!document.getElementById('vn-style')) {
    const st = document.createElement('style');
    st.id = 'vn-style'; st.textContent = CSS; document.head.appendChild(st);
  }

  const root = document.createElement('div');
  root.className = 'vn-root'; root.hidden = true;
  root.innerHTML =
    '<div class="vn-back"></div>' +
    '<div class="vn-dim"></div>' +
    '<div class="vn-figure"><div class="vn-kb">' +
      '<img class="vn-halo" alt="">' +
      '<img class="vn-char" alt="">' +
      '<div class="vn-shim"></div>' +
    '</div></div>' +
    '<canvas class="vn-parts"></canvas>' +
    '<div class="vn-vig"></div>' +
    '<div class="vn-grain"></div>' +
    '<div class="vn-skip">click to skip ▸</div>' +
    '<div class="vn-bubble">' +
      '<div class="vn-tail"></div>' +
      '<div class="vn-name"></div>' +
      '<div class="vn-text"><span class="vn-body"></span><span class="vn-caret vn-off"></span></div>' +
    '</div>';
  hud.appendChild(root);

  const $ = (s) => root.querySelector(s);
  const char = $('.vn-char'), halo = $('.vn-halo'), shim = $('.vn-shim'),
        bubble = $('.vn-bubble'), nameEl = $('.vn-name'), bodyEl = $('.vn-body'),
        caret = $('.vn-caret'), cv = $('.vn-parts'), cx = cv.getContext('2d');

  let manifest = null, manifestErr = null;

  async function ensureManifest() {
    if (manifest || manifestErr) return manifest;
    try { manifest = await (await fetch('/dtrh/assets/vn/manifest.json', { cache: 'force-cache' })).json(); }
    catch (e) { manifestErr = e; }
    return manifest;
  }
  function setFor(modId) {
    const m = manifest || {};
    const key = (m.personaSets && m.personaSets[modId]) || (m.personaSets && m.personaSets['builtin-sissyhypno']) || 'avatar0';
    return { key, def: m.sets && m.sets[key] };
  }
  function resolveBeat(id, modId) {
    const b = manifest && manifest.beats && manifest.beats[id];
    if (!b) return { emote: id };
    const line = (b.lines && (b.lines[modId] || b.lines['builtin-sissyhypno'])) || '';
    const voUrl = b.vo ? ('/dtrh/assets/vn/vo/' + modId + '/' + b.vo) : null;
    return { emote: b.emote, line, hold: b.hold, voUrl };
  }

  // ---- voice (per-beat mp3 -> reactive aura, and we wait for it to finish) ----
  let actx = null, audioEl = null, analyser = null, gain = null, vdata = null, voicePlaying = false, voiceEndCb = null;
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
      // a missing/blocked mp3 must unblock the beat immediately, not stall on waitVoice's timeout
      audioEl.addEventListener('error', endVoice);
    } catch (e) { actx = null; }
  }
  function startVoice(url) {
    ensureVoiceGraph(); if (!actx || !url) return;
    try {
      if (actx.state === 'suspended') actx.resume();
      if (gain) { try { gain.gain.cancelScheduledValues(actx.currentTime); gain.gain.setValueAtTime(1, actx.currentTime); } catch (e) {} }
      audioEl.src = url; audioEl.currentTime = 0; voicePlaying = true;
      // if play() rejects (404/autoplay block) 'ended' never fires - clear the flag and resolve the wait so the beat doesn't hang ~12s
      audioEl.play().catch(() => { voicePlaying = false; if (voiceEndCb) { const c = voiceEndCb; voiceEndCb = null; c(); } });
    } catch (e) { /* ignore */ }
  }
  function fadeVoice(ms) {
    if (!voicePlaying) { stopVoice(); return; }
    const secs = (ms || 280) / 1000;
    try {
      if (gain && actx) {
        const t = actx.currentTime;
        gain.gain.cancelScheduledValues(t);
        gain.gain.setValueAtTime(Math.max(0.0001, gain.gain.value), t);
        gain.gain.linearRampToValueAtTime(0.0001, t + secs);
      }
    } catch (e) { /* ignore */ }
    setTimeout(stopVoice, (ms || 280) + 30);
  }
  function stopVoice() { voicePlaying = false; try { if (audioEl) audioEl.pause(); } catch (e) {} if (voiceEndCb) { const c = voiceEndCb; voiceEndCb = null; c(); } }
  function waitVoice(maxMs) {
    if (!voicePlaying) return Promise.resolve();
    return new Promise((res) => { voiceEndCb = res; setTimeout(() => { if (voiceEndCb === res) { voiceEndCb = null; res(); } }, maxMs || 12000); });
  }
  function readVoice() {
    if (!voicePlaying || !analyser) return 0;
    analyser.getByteTimeDomainData(vdata);
    let s = 0; for (let i = 0; i < vdata.length; i++) { const v = (vdata[i] - 128) / 128; s += v * v; }
    return Math.min(1, Math.sqrt(s / vdata.length) * 3.2);
  }

  // ---- FX loop (only while a beat is on screen) ----
  let raf = 0, running = false, W = 0, H = 0, parts = [], t0 = 0, tintA = DEFAULT_TINT.a;
  function resize() { W = cv.width = cv.offsetWidth || window.innerWidth; H = cv.height = cv.offsetHeight || window.innerHeight; }
  function startFx() {
    if (running) return; running = true; resize(); t0 = performance.now();
    const tick = (now) => {
      if (!running) return;
      const ph = (now - t0) / 1000;
      const idle = 0.12 + Math.sin(ph * 1.6) * 0.06 + Math.sin(ph * 0.7) * 0.03;
      const glow = Math.max(idle, readVoice());
      root.style.setProperty('--vnG', glow.toFixed(3));
      // motes drift up around the left figure
      if (Math.random() < 0.4) parts.push({ x: W * (0.16 + Math.random() * 0.20), y: H * 0.96,
        vx: (Math.random() - 0.5) * 0.3, vy: -(0.3 + Math.random() * 0.7), r: 1 + Math.random() * 2.4, life: 1, fade: 0.003 + Math.random() * 0.004 });
      cx.clearRect(0, 0, W, H); cx.globalCompositeOperation = 'lighter';
      for (let i = parts.length - 1; i >= 0; i--) { const p = parts[i];
        p.x += p.vx; p.y += p.vy; p.vy -= 0.002; p.life -= p.fade;
        if (p.life <= 0) { parts.splice(i, 1); continue; }
        cx.globalAlpha = p.life * (0.3 + glow * 0.5); cx.fillStyle = 'rgb(' + tintA + ')';
        cx.beginPath(); cx.arc(p.x, p.y, p.r * (1 + glow), 0, 7); cx.fill(); }
      cx.globalAlpha = 1;
      raf = requestAnimationFrame(tick);
    };
    raf = requestAnimationFrame(tick);
  }
  function stopFx() { running = false; if (raf) cancelAnimationFrame(raf); raf = 0; parts = []; try { cx.clearRect(0, 0, W, H); } catch (e) {} }

  let beatToken = 0, restoreTimer = 0;
  const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

  // ---- click anywhere to skip: cut the beat short, fade the voice out ----
  let skipping = false, skipWaiters = [];
  function waitSkippable(p) { return Promise.race([p, new Promise((r) => skipWaiters.push(r))]); }
  function fireSkip() {
    if (skipping || root.hidden) return;
    skipping = true;
    fadeVoice(260);
    const w = skipWaiters; skipWaiters = []; w.forEach((r) => { try { r(); } catch (e) {} });
  }
  root.addEventListener('pointerdown', () => { if (root.classList.contains('vn-on')) fireSkip(); });
  function showFrame(src) {
    char.src = src; halo.src = src;
    shim.style.webkitMaskImage = shim.style.maskImage = 'url("' + src + '")';
  }
  // stop the tunnel + duck all non-bed audio while she holds the stage
  function sceneHold(on) {
    if (on && restoreTimer) { clearTimeout(restoreTimer); restoreTimer = 0; }
    try { if (haltTunnel) haltTunnel(on); } catch (e) { /* ignore */ }
    try { if (duckAudio) duckAudio(on); } catch (e) { /* ignore */ }
  }
  function scheduleResume() {
    if (!haltTunnel && !duckAudio) return;
    if (restoreTimer) clearTimeout(restoreTimer);
    // brief delay so a back-to-back beat keeps it held (no flicker/blip between lines)
    restoreTimer = setTimeout(() => {
      restoreTimer = 0;
      try { if (haltTunnel) haltTunnel(false); } catch (e) {}
      try { if (duckAudio) duckAudio(false); } catch (e) {}
    }, 400);
  }

  function typewrite(text, token) {
    return new Promise((res) => {
      bodyEl.textContent = ''; caret.classList.remove('vn-off');
      const chars = Array.from(text || ''); let i = 0;
      const step = () => {
        if (token !== beatToken) { caret.classList.add('vn-off'); return res(false); }
        if (skipping) { bodyEl.textContent = text || ''; caret.classList.add('vn-off'); return res(true); }
        bodyEl.textContent = chars.slice(0, i).join('');
        if (i >= chars.length) { caret.classList.add('vn-off'); return res(true); }
        const c = chars[i]; i++;
        const d = /[.,!?…]/.test(c) ? 220 : (c === ' ' ? 34 : 26);   // punctuation breathes
        setTimeout(step, d);
      };
      step();
    });
  }

  /** Play a beat: halt the tunnel, perform the emote, type + speak the line, hold, fade. */
  async function beat(arg) {
    await ensureManifest();
    const modId = getModId() || 'builtin-sissyhypno';
    const o = (typeof arg === 'string') ? resolveBeat(arg, modId) : (arg || {});
    const token = ++beatToken;
    const tint = TINT[modId] || DEFAULT_TINT; tintA = tint.a;
    root.style.setProperty('--vnA', tint.a); root.style.setProperty('--vnB', tint.b);

    const { key, def } = setFor(modId);
    const seg = def && def[o.emote];
    const frames = seg && seg.frames;

    skipping = false; skipWaiters = [];
    root.hidden = false; requestAnimationFrame(() => root.classList.add('vn-on'));
    sceneHold(true);
    startFx();

    // her first frame + the bubble arrive together
    if (frames && frames.length) showFrame(frames[0]);
    nameEl.textContent = NAME[modId] || '';
    bubble.classList.add('vn-on');
    if (o.voUrl) startVoice(o.voUrl); else stopVoice();
    const typing = typewrite(o.line || '', token);

    // perform the emote segment -> freeze on the last frame
    if (frames && frames.length) {
      const delays = seg.delays || frames.map(() => 40);
      for (let i = 0; i < frames.length; i++) {
        if (token !== beatToken) return false;
        if (skipping) break;
        showFrame(frames[i]);
        await waitSkippable(sleep(Math.max(30, delays[i])));
      }
      if (token !== beatToken) return false;
      showFrame(frames[frames.length - 1]);   // land on the freeze frame
    }

    // let her finish: typewriter done AND the voiceover played out (or a click skips)
    await waitSkippable(Promise.all([typing, waitVoice()]));
    if (token !== beatToken) return false;
    if (!skipping) await waitSkippable(sleep(o.hold != null ? o.hold : 1200));
    if (token !== beatToken) return false;
    await fadeOut(token);
    return true;
  }

  async function fadeOut(token) {
    root.classList.remove('vn-on'); bubble.classList.remove('vn-on'); fadeVoice(300);
    scheduleResume();
    await sleep(470);
    if (token !== beatToken) return;
    root.hidden = true; stopFx();
  }

  function hide() {
    beatToken++; stopVoice();
    root.classList.remove('vn-on'); bubble.classList.remove('vn-on'); root.hidden = true;
    stopFx(); sceneHold(false);
  }
  function dispose() { hide(); try { root.remove(); } catch (e) {} window.removeEventListener('resize', resize); try { if (actx) { actx.close(); actx = null; } } catch (e) {} }
  window.addEventListener('resize', resize);

  return { beat, hide, dispose, prime: ensureManifest };
}
