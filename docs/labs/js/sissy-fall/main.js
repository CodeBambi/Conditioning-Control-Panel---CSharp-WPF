/* ============================================================================
 * main.js - entry point & gates for the Sissy Fall (fall.html).
 *
 * Flow (mirrors js/rabbit-hole/main.js, minus the persona picker):
 *   capability gate -> 18+ age gate (cookie-consent.js) -> drop screen
 *   (optional media + challenge toggle) -> dynamic import of scene.js.
 *
 * Never statically imports three.js - visitors who bounce at a gate never
 * download the engine. Every path off the drop screen is a click, so audio
 * autoplay is unlocked before the fall starts.
 * ==========================================================================*/

import { detectMode } from '../rabbit-hole/capability.js';
import { createMediaSource } from './media.js';
import { getAudioCtx } from './audioBus.js';

const dom = {
  canvas: document.getElementById('sf-canvas'),
  hud: document.getElementById('sf-hud'),
  loader: document.getElementById('sf-loader'),
  drop: document.getElementById('sf-drop'),
  nope: document.getElementById('sf-nope'),
  nopeMsg: document.getElementById('sf-nope-msg'),
  dropzone: document.getElementById('sf-dropzone'),
  pick: document.getElementById('sf-pick'),
  dirinput: document.getElementById('sf-dirinput'),
  gallery: document.getElementById('sf-gallery'),
  galleryinput: document.getElementById('sf-galleryinput'),
  zip: document.getElementById('sf-zip'),
  zipinput: document.getElementById('sf-zipinput'),
  progress: document.getElementById('sf-progress'),
  progressFill: document.getElementById('sf-progress-fill'),
  progressLabel: document.getElementById('sf-progress-label'),
  stats: document.getElementById('sf-stats'),
  challenge: document.getElementById('sf-challenge'),
  begin: document.getElementById('sf-begin'),
};

const media = createMediaSource();
let engine = null;
let scenePromise = null; // the engine download overlaps the drop screen

// On-phone black box: keep the last few uncaught errors so the gear panel's
// diagnostics view can show them - there are no devtools on an iPhone mid-fall,
// and the card pipeline has now broken silently there more than once.
const errLog = (window.__sfErrors = []);
function logErr(line) {
  errLog.push(String(line).slice(0, 200));
  if (errLog.length > 8) errLog.shift();
}
window.addEventListener('error', (e) => {
  const src = e.filename ? ` @ ${String(e.filename).split('/').pop()}:${e.lineno}` : '';
  logErr((e.message || 'script error') + src);
});
window.addEventListener('unhandledrejection', (e) => {
  const r = e.reason;
  logErr('promise: ' + ((r && (r.message || r.stack || r)) || 'unknown'));
});

// Only genuine hard blocks (no WebGL / no importmap) or a runtime boot failure
// reach this screen now - so tell the visitor the real reason instead of always
// blaming WebGL. reduced-motion / low-end hardware never dead-end here anymore.
function showNope(reason) {
  if (dom.loader) dom.loader.hidden = true;
  if (dom.drop) dom.drop.hidden = true;
  if (dom.nope) dom.nope.hidden = false;
  if (dom.nopeMsg) {
    dom.nopeMsg.innerHTML = reason === 'no importmap support'
      ? 'This experience needs a browser that supports JavaScript import maps.<br>Try the latest Chrome or Edge on desktop.'
      : 'This experience needs WebGL, and your browser could not start it.<br>Check that hardware acceleration is turned on, then try Chrome or Edge on desktop.';
  }
}

function showStats() {
  const s = media.stats();
  if (!dom.stats) return;
  if (!media.hasUserMedia()) { dom.stats.hidden = true; return; }
  const bits = [];
  if (s.images) bits.push(`${s.images} picture${s.images === 1 ? '' : 's'}`);
  if (s.videos) bits.push(`${s.videos} video${s.videos === 1 ? '' : 's'}`);
  let line = `in the pool: ${bits.join(' + ')}`;
  if (s.skipped) line += ` (${s.skipped} skipped)`;
  dom.stats.textContent = line;
  dom.stats.hidden = false;
}

const isZip = (f) => !!f && (/\.zip$/i.test(f.name) || f.type === 'application/zip' || f.type === 'application/x-zip-compressed');

// Unpack a .zip with a live loading bar, then refresh the pool stats.
async function ingestZip(file) {
  if (!file) return;
  if (dom.progress) dom.progress.hidden = false;
  const setBar = (frac, phase) => {
    if (dom.progressFill) dom.progressFill.style.width = `${Math.round(frac * 100)}%`;
    if (dom.progressLabel) {
      dom.progressLabel.textContent =
        phase === 'reading' ? `reading… ${Math.round(frac * 100)}%`
        : phase === 'unpacking' ? 'unpacking…'
        : phase === 'done' ? 'ready' : 'working…';
    }
  };
  setBar(0, 'reading');
  try {
    await media.addZip(file, setBar);
  } catch (e) {
    console.error('[sissy-fall] zip ingest failed:', e);
  }
  if (dom.progress) dom.progress.hidden = true;
  showStats();
}

function wireDropScreen() {
  // drag + drop (a .zip, or a directory/Files: handles on Chromium, plain Files elsewhere)
  const zone = dom.dropzone;
  if (zone) {
    const over = (e) => { e.preventDefault(); zone.classList.add('is-over'); };
    const out = () => zone.classList.remove('is-over');
    zone.addEventListener('dragover', over);
    zone.addEventListener('dragleave', out);
    zone.addEventListener('drop', (e) => {
      e.preventDefault();
      out();
      const files = e.dataTransfer && e.dataTransfer.files;
      const zipFile = files && Array.from(files).find(isZip);
      if (zipFile) { ingestZip(zipFile); return; }
      media.handleDrop(e.dataTransfer).then(showStats);
    });
  }
  // folder picker: File System Access where available, webkitdirectory otherwise
  if (dom.pick) {
    dom.pick.addEventListener('click', () => {
      if (media.supportsFS()) media.pickFolder().then((s) => { if (s) showStats(); });
      else if (dom.dirinput) dom.dirinput.click();
    });
  }
  if (dom.dirinput) {
    dom.dirinput.addEventListener('change', () => {
      media.addFileList(dom.dirinput.files);
      showStats();
    });
  }
  // gallery picker: a plain multi-file input (no webkitdirectory) opens the
  // phone's photo library directly - the reliable mobile path alongside .zip.
  if (dom.gallery && dom.galleryinput) {
    dom.gallery.addEventListener('click', () => dom.galleryinput.click());
    dom.galleryinput.addEventListener('change', () => {
      media.addFileList(dom.galleryinput.files);
      showStats();
      dom.galleryinput.value = ''; // let the same shots be re-picked / added to
    });
  }
  // .zip upload - the reliable path on mobile (no folder pickers there)
  if (dom.zip && dom.zipinput) {
    dom.zip.addEventListener('click', () => dom.zipinput.click());
    dom.zipinput.addEventListener('change', () => {
      const f = dom.zipinput.files && dom.zipinput.files[0];
      if (f) ingestZip(f);
      dom.zipinput.value = ''; // let the same file be re-picked
    });
  }
  if (dom.begin) dom.begin.addEventListener('click', beginFall, { once: true });
}

async function beginFall() {
  // Create the shared AudioContext HERE, synchronously inside the Begin tap.
  // scene.start() is async work - a context first created down there is born
  // outside the gesture, which iOS answers with state "suspended": the drone,
  // voice and every routed video stay SILENT until some later touch happens to
  // hit the resume hook ("audio only started after a while").
  getAudioCtx();
  const challenge = !!(dom.challenge && dom.challenge.checked);
  if (dom.drop) dom.drop.hidden = true;
  if (dom.loader) dom.loader.hidden = false;
  try {
    const mod = await (scenePromise || import('./scene.js'));
    engine = await mod.start({
      canvas: dom.canvas,
      hud: dom.hud,
      tier: detectMode().tier,
      media,
      challenge,
    });
    if (dom.loader) dom.loader.hidden = true;
  } catch (err) {
    console.error('[sissy-fall] 3D boot failed:', err);
    showNope();
  }
}

// 18+ gate: identical handshake to rabbit-hole/main.js (cookie-consent.js
// exposes CCPCookieConsent.age + a one-shot 'cc-age-confirmed' event).
function ageConfirmed() {
  try {
    const c = window.CCPCookieConsent;
    return !!(c && c.age && c.age.isConfirmed && c.age.isConfirmed());
  } catch (e) { return false; }
}
function whenAgeConfirmed(cb) {
  if (ageConfirmed()) { cb(); return; }
  window.addEventListener('cc-age-confirmed', cb, { once: true });
}

// A drop that misses a drop zone must never navigate the tab to the file
// (that would kill a running fall). Zones still get their events first.
window.addEventListener('dragover', (e) => e.preventDefault());
window.addEventListener('drop', (e) => e.preventDefault());

function boot() {
  const decision = detectMode();
  console.info('[sissy-fall] mode:', decision.mode, '-', decision.reason);
  // The Fall has no 2D scene, so - unlike the rabbit hole - a '2d' verdict can't
  // route to a real fallback. But most '2d' reasons (reduced-motion, low-end
  // hardware) don't mean the fall can't run; they're comfort/perf downgrades
  // meant for a page that HAS a 2D mode. Only a genuine hardBlock (no WebGL /
  // no importmap) is a true wall here - everyone else reaches the drop screen
  // and opts in with the "begin the fall" click.
  if (decision.hardBlock) { showNope(decision.reason); return; }
  whenAgeConfirmed(() => {
    scenePromise = import('./scene.js'); // download while the visitor reads the drop screen
    wireDropScreen();
    if (dom.loader) dom.loader.hidden = true;
    if (dom.drop) dom.drop.hidden = false;
  });
}

boot();
