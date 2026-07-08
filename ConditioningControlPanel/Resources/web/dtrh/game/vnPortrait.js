/* ============================================================================
 * vnPortrait.js - the Visual-Novel portrait overlay for the scripted first
 * descents. A beat shows the active persona's portrait, plays a pre-trimmed
 * emote segment (baked frames from assets/vn/manifest.json), freezes on the
 * last frame, shows the line, holds, then fades out. All the "pop" is
 * procedural FX over a static-ish sprite - Hades-style: continuous Ken Burns
 * drift, a silhouette rim-aura + bloom halo, a masked shimmer sweep, and
 * persona-tinted ambient motes. Everything is a pointer-events:none HUD overlay
 * so the run underneath is never blocked.
 *
 * Frames are PRE-TRIMMED at build time (the segment editor's in/out), so a beat
 * just plays frame 0..N then holds - no runtime GIF decode, no in/out math here.
 *
 * API:  const vn = createVnPortrait(hud, { getModId });
 *       await vn.beat({ emote:'sultry', line:'...', hold:1400 });
 *       vn.hide();  vn.dispose();
 * ==========================================================================*/

const TINT = {
  'builtin-bambisleep': { a: '255,105,180', b: '255,60,120' },
  'builtin-sissyhypno': { a: '255,64,180',  b: '180,60,255' },
  'builtin-locked':     { a: '150,80,255',  b: '90,120,255' },
};
const DEFAULT_TINT = TINT['builtin-sissyhypno'];

const CSS = `
.vn-root{position:absolute;inset:0;z-index:40;pointer-events:none;opacity:0;transition:opacity .5s ease}
.vn-root.vn-on{opacity:1}
.vn-dim{position:absolute;inset:0;background:radial-gradient(120% 100% at 50% 42%,transparent 45%,rgba(4,2,10,.72) 100%)}
.vn-back{position:absolute;inset:-8%;
  background:radial-gradient(58% 52% at 50% 42%,rgba(var(--vnA),.28),transparent 60%),
             radial-gradient(74% 68% at 50% 60%,rgba(var(--vnB),.20),transparent 66%);
  filter:saturate(1.1);animation:vnBack 24s ease-in-out infinite alternate}
@keyframes vnBack{from{transform:scale(1)}to{transform:scale(1.08) translateY(-2%)}}
.vn-stage{position:absolute;left:50%;bottom:0;transform:translateX(-50%);height:82vh;display:grid;place-items:end center}
.vn-kb{position:relative;height:82vh;display:grid;place-items:end center;
  animation:vnKb 22s linear infinite;transform-origin:50% 40%;will-change:transform}
@keyframes vnKb{
  0%{transform:scale(1.05) translate(0,0)}
  25%{transform:scale(1.09) translate(-1%,-1.1%)}
  50%{transform:scale(1.065) translate(-1.6%,0)}
  75%{transform:scale(1.09) translate(-.6%,1%)}
  100%{transform:scale(1.05) translate(0,0)}}
.vn-char{position:relative;height:82vh;width:auto;image-rendering:auto;
  filter:drop-shadow(0 0 calc(6px + var(--vnG,0)*24px) rgba(var(--vnA),calc(.35 + var(--vnG,0)*.5)))
         drop-shadow(0 0 calc(2px + var(--vnG,0)*9px) rgba(var(--vnA),calc(.5 + var(--vnG,0)*.4)));
  will-change:filter,transform}
.vn-halo{position:absolute;height:82vh;width:auto;left:50%;bottom:0;transform:translateX(-50%);
  filter:blur(calc(10px + var(--vnG,0)*20px)) saturate(1.6) brightness(1.25);
  mix-blend-mode:screen;opacity:calc(.35 + var(--vnG,0)*.45);z-index:-1}
.vn-shim{position:absolute;height:82vh;width:100%;left:50%;bottom:0;transform:translateX(-50%);
  background:linear-gradient(115deg,transparent 42%,rgba(255,255,255,.5) 50%,rgba(var(--vnA),.32) 53%,transparent 60%);
  background-size:280% 280%;mix-blend-mode:screen;opacity:0;
  -webkit-mask-size:auto 82vh;mask-size:auto 82vh;-webkit-mask-position:bottom center;mask-position:bottom center;
  -webkit-mask-repeat:no-repeat;mask-repeat:no-repeat;animation:vnSheen 5.5s linear infinite}
@keyframes vnSheen{0%{background-position:170% 0;opacity:0}6%{opacity:.85}40%{background-position:-130% 0;opacity:.85}48%{background-position:-130% 0;opacity:0}100%{background-position:-130% 0;opacity:0}}
.vn-parts{position:absolute;inset:0;z-index:1;pointer-events:none}
.vn-say{position:absolute;left:50%;bottom:8%;transform:translateX(-50%);max-width:60ch;text-align:center;z-index:2;
  font-size:clamp(15px,2.4vh,26px);line-height:1.35;color:#f4e6ff;
  text-shadow:0 2px 18px #000,0 0 30px rgba(var(--vnA),.6);opacity:0;transition:opacity .4s}
.vn-say.vn-on{opacity:1}
.vn-vig{position:absolute;inset:0;background:radial-gradient(120% 100% at 50% 45%,transparent 55%,rgba(0,0,0,.5) 100%)}
`;

export function createVnPortrait(hud, opts = {}) {
  const getModId = opts.getModId || (() => 'builtin-sissyhypno');

  // one-time style inject
  if (!document.getElementById('vn-style')) {
    const st = document.createElement('style');
    st.id = 'vn-style'; st.textContent = CSS; document.head.appendChild(st);
  }

  const root = document.createElement('div');
  root.className = 'vn-root'; root.hidden = true;
  root.innerHTML =
    '<div class="vn-back"></div>' +
    '<div class="vn-dim"></div>' +
    '<div class="vn-stage"><div class="vn-kb">' +
      '<img class="vn-halo" alt="">' +
      '<img class="vn-char" alt="">' +
      '<div class="vn-shim"></div>' +
    '</div></div>' +
    '<canvas class="vn-parts"></canvas>' +
    '<div class="vn-vig"></div>' +
    '<div class="vn-say"></div>';
  hud.appendChild(root);

  const $ = (s) => root.querySelector(s);
  const char = $('.vn-char'), halo = $('.vn-halo'), shim = $('.vn-shim'),
        say = $('.vn-say'), cv = $('.vn-parts'), cx = cv.getContext('2d');

  let manifest = null, manifestErr = null;
  const preloaded = {};   // setKey/emote -> [Image,...]

  async function ensureManifest() {
    if (manifest || manifestErr) return manifest;
    try {
      const res = await fetch('/dtrh/assets/vn/manifest.json', { cache: 'force-cache' });
      manifest = await res.json();
    } catch (e) { manifestErr = e; }
    return manifest;
  }

  function setFor(modId) {
    const m = manifest || {};
    const key = (m.personaSets && m.personaSets[modId]) ||
                (m.personaSets && m.personaSets['builtin-sissyhypno']) || 'avatar0';
    return { key, def: m.sets && m.sets[key] };
  }

  function preload(setKey, emote, frames) {
    const k = setKey + '/' + emote;
    if (preloaded[k]) return preloaded[k];
    const imgs = frames.map((src) => { const im = new Image(); im.src = src; return im; });
    preloaded[k] = imgs; return imgs;
  }

  // ---- FX loop (runs only while a beat is on screen) ----
  let raf = 0, running = false, W = 0, H = 0, parts = [], t0 = 0, tintA = DEFAULT_TINT.a;
  function resize() { W = cv.width = cv.offsetWidth || window.innerWidth; H = cv.height = cv.offsetHeight || window.innerHeight; }
  function startFx() {
    if (running) return; running = true; resize(); t0 = performance.now();
    const tick = (now) => {
      if (!running) return;
      const ph = (now - t0) / 1000;
      const glow = 0.12 + Math.sin(ph * 1.6) * 0.06 + Math.sin(ph * 0.7) * 0.03;   // idle breath
      root.style.setProperty('--vnG', glow.toFixed(3));
      // ambient motes
      if (Math.random() < 0.4) parts.push({ x: W * 0.5 + (Math.random() - 0.5) * W * 0.3, y: H * 0.92,
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
  function stopFx() { running = false; if (raf) cancelAnimationFrame(raf); raf = 0; parts = []; try { cx.clearRect(0, 0, W, H); } catch {} }

  let beatToken = 0;
  const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

  function showFrame(src) {
    char.src = src; halo.src = src;
    shim.style.webkitMaskImage = shim.style.maskImage = 'url("' + src + '")';
  }

  /** Play a beat: perform the emote segment, freeze on the last frame, hold, fade out. */
  async function beat(o) {
    o = o || {};
    const token = ++beatToken;
    await ensureManifest();
    const modId = getModId() || 'builtin-sissyhypno';
    const tint = TINT[modId] || DEFAULT_TINT;
    tintA = tint.a;
    root.style.setProperty('--vnA', tint.a); root.style.setProperty('--vnB', tint.b);

    const { key, def } = setFor(modId);
    const seg = def && def[o.emote];
    if (!seg || !seg.frames || !seg.frames.length) {
      // no art for this emote/persona - degrade to a text-only beat so the tutorial still speaks
      return textOnlyBeat(o, token);
    }
    const frames = seg.frames, delays = seg.delays || frames.map(() => 40);
    preload(key, o.emote, frames);

    showFrame(frames[0]);
    root.hidden = false; requestAnimationFrame(() => root.classList.add('vn-on'));
    say.textContent = o.line || ''; if (o.line) say.classList.add('vn-on');
    startFx();

    // play frame 0..N-1 at their delays, then freeze on the last
    for (let i = 0; i < frames.length; i++) {
      if (token !== beatToken) return false;      // superseded / hidden
      showFrame(frames[i]);
      await sleep(Math.max(30, delays[i]));
    }
    if (token !== beatToken) return false;
    showFrame(frames[frames.length - 1]);         // hold the freeze
    await sleep(o.hold != null ? o.hold : 1400);
    if (token !== beatToken) return false;
    await fadeOut(token);
    return true;
  }

  async function textOnlyBeat(o, token) {
    root.hidden = false; requestAnimationFrame(() => root.classList.add('vn-on'));
    say.textContent = o.line || ''; if (o.line) say.classList.add('vn-on');
    startFx();
    await sleep((o.hold != null ? o.hold : 1400) + 800);
    if (token !== beatToken) return false;
    await fadeOut(token);
    return true;
  }

  async function fadeOut(token) {
    root.classList.remove('vn-on'); say.classList.remove('vn-on');
    await sleep(520);
    if (token !== beatToken) return;
    root.hidden = true; stopFx();
  }

  function hide() { beatToken++; root.classList.remove('vn-on'); say.classList.remove('vn-on'); root.hidden = true; stopFx(); }
  function dispose() { hide(); try { root.remove(); } catch {} window.removeEventListener('resize', resize); }
  window.addEventListener('resize', resize);

  return { beat, hide, dispose, prime: ensureManifest };
}
