// M0 spike: exercises the exact pipeline the DtRH port depends on.
//   1. fetch() with a Range header against https://ccp.assets/ (virtual host) -> expect 206
//   2. <video> metadata + arbitrary seek through the virtual host
//   3. cross-origin image -> WebGL texture (CORS taint check) + 2D canvas getImageData
//   4. cross-origin video frame -> WebGL texture
//   5. keyboard/pointer input reaching the page (reported live)
// Results go back over the bridge as {type:'spike-result', name, ok, detail}.

const logEl = document.getElementById('log');
const bridge = window.chrome?.webview;

function report(name, ok, detail) {
  const line = `${ok ? 'PASS' : 'FAIL'}  ${name}  ${detail}`;
  const span = document.createElement('div');
  span.className = ok ? 'ok' : 'fail';
  span.textContent = line;
  logEl.appendChild(span);
  bridge?.postMessage({ type: 'spike-result', name, ok, detail: String(detail) });
}

function once(target, ev, timeoutMs) {
  return new Promise((resolve, reject) => {
    const t = setTimeout(() => reject(new Error(`timeout waiting for '${ev}'`)), timeoutMs);
    target.addEventListener(ev, () => { clearTimeout(t); resolve(); }, { once: true });
    target.addEventListener('error', () => { clearTimeout(t); reject(new Error(`'error' event while waiting for '${ev}'`)); }, { once: true });
  });
}

async function testRangeFetch(url) {
  const r = await fetch(url, { headers: { Range: 'bytes=100-199' } });
  const buf = await r.arrayBuffer();
  const ok = r.status === 206 && buf.byteLength === 100;
  report('range-fetch', ok,
    `status=${r.status} len=${buf.byteLength} content-range=${r.headers.get('Content-Range')}`);
  return ok;
}

async function testVideoSeek(url) {
  const v = document.createElement('video');
  v.crossOrigin = 'anonymous';
  v.muted = true;
  v.preload = 'auto';
  v.src = url;
  await once(v, 'loadedmetadata', 15000);
  const dur = v.duration;
  const target = Math.max(1, dur * 0.7);
  v.currentTime = target;
  await once(v, 'seeked', 15000);
  const ok = isFinite(dur) && dur > 0 && Math.abs(v.currentTime - target) < 2;
  report('video-seek', ok, `duration=${dur.toFixed(1)}s target=${target.toFixed(1)}s landed=${v.currentTime.toFixed(1)}s`);
  return { ok, video: v };
}

function makeGl() {
  const c = document.createElement('canvas');
  c.width = 64; c.height = 64;
  return c.getContext('webgl2') || c.getContext('webgl');
}

function uploadTexture(gl, source) {
  const tex = gl.createTexture();
  gl.bindTexture(gl.TEXTURE_2D, tex);
  gl.texImage2D(gl.TEXTURE_2D, 0, gl.RGBA, gl.RGBA, gl.UNSIGNED_BYTE, source);
  const err = gl.getError();
  gl.deleteTexture(tex);
  return err; // 0 = clean upload, 1286/anything else = failure (taint throws before this)
}

async function testWebGLImage(url) {
  const img = new Image();
  img.crossOrigin = 'anonymous';
  img.src = url;
  await once(img, 'load', 15000);
  const gl = makeGl();
  if (!gl) { report('webgl-image', false, 'no WebGL context'); return false; }
  let err;
  try { err = uploadTexture(gl, img); }
  catch (ex) { report('webgl-image', false, `texImage2D threw: ${ex.message}`); return false; }
  // 2D taint check: getImageData throws on a tainted canvas.
  let taintOk = true, taintDetail = 'canvas untainted';
  try {
    const c2 = document.createElement('canvas');
    c2.width = 8; c2.height = 8;
    const ctx = c2.getContext('2d');
    ctx.drawImage(img, 0, 0, 8, 8);
    ctx.getImageData(0, 0, 1, 1);
  } catch (ex) { taintOk = false; taintDetail = `getImageData threw: ${ex.message}`; }
  const ok = err === 0 && taintOk;
  report('webgl-image', ok, `glError=${err} ${taintDetail} (${img.naturalWidth}x${img.naturalHeight})`);
  return ok;
}

async function testWebGLVideo(v) {
  if (!v) { report('webgl-video', false, 'no video element from seek test'); return false; }
  try {
    await v.play();
    await new Promise(r => setTimeout(r, 400)); // let a frame decode
    const gl = makeGl();
    if (!gl) { report('webgl-video', false, 'no WebGL context'); return false; }
    const err = uploadTexture(gl, v);
    v.pause();
    const ok = err === 0;
    report('webgl-video', ok, `glError=${err} at t=${v.currentTime.toFixed(1)}s`);
    return ok;
  } catch (ex) {
    report('webgl-video', false, `threw: ${ex.message}`);
    return false;
  }
}

// --- input probes: prove the window is a real game surface ---
let inputReported = false;
window.addEventListener('keydown', (e) => {
  if (!inputReported) { inputReported = true; report('input-keydown', true, `key=${e.key}`); }
});
window.addEventListener('pointerdown', (e) => {
  bridge?.postMessage({ type: 'spike-pointer', x: e.clientX, y: e.clientY });
});

// --- driver ---
let ran = false;
async function run(assets) {
  if (ran) return;
  ran = true;
  const results = [];
  try {
    if (assets.video) {
      results.push(await testRangeFetch(assets.video));
      const { ok, video } = await testVideoSeek(assets.video).catch(ex => {
        report('video-seek', false, ex.message); return { ok: false, video: null };
      });
      results.push(ok);
      results.push(await testWebGLVideo(video));
    } else {
      report('video-seek', false, 'no video in assets folder — seek/range untested');
    }
    if (assets.image) {
      results.push(await testWebGLImage(assets.image).catch(ex => {
        report('webgl-image', false, ex.message); return false;
      }));
    } else {
      report('webgl-image', false, 'no image in assets folder — CORS untested');
    }
  } finally {
    bridge?.postMessage({ type: 'spike-done', pass: results.every(Boolean), count: results.length });
  }
}

bridge?.addEventListener('message', (e) => {
  const m = e.data;
  if (m?.type === 'spike-run') run(m);
});
bridge?.postMessage({ type: 'ready' });
logEl.append('bridge ready, waiting for spike-run…\n');
