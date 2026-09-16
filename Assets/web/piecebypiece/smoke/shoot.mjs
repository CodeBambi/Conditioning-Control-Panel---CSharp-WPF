/* ============================================================================
 * smoke/shoot.mjs - screenshot every ramp layer at three meter settings.
 *
 * The ramp ships inside WebView2, which has no devtools, so the only way to see
 * a layer is to photograph it. This drives headless msedge over the dev harness
 * once per (layer, meter) pair and writes a PNG a human can look at.
 *
 * Two rules this file is careful about, both learned the hard way:
 *   - ONE browser process at a time. The machine runs other agents' servers and
 *     a fan-out of headless Chromium would take the box down.
 *   - rAF is nearly starved under --virtual-time-budget, so the harness is told
 *     to pin the meter (?meter=) and to pre-spawn one-shots (?prime=) rather
 *     than being left to schedule them itself.
 *
 * Usage (the dev server must already be up):
 *     python -m http.server 8822       # from Resources/web
 *     node smoke/shoot.mjs [--layer flash] [--out DIR] [--port 8822]
 * ==========================================================================*/

import { spawn } from 'node:child_process';
import { mkdirSync, rmSync, existsSync } from 'node:fs';
import path from 'node:path';
import os from 'node:os';

const argv = process.argv.slice(2);
const arg = (name, dflt) => {
  const i = argv.indexOf('--' + name);
  return i >= 0 && argv[i + 1] ? argv[i + 1] : dflt;
};

const PORT = arg('port', '8822');
const OUT = path.resolve(arg('out', 'C:/Users/PC/Pictures/Screenshots/piecebypiece/b'));
const PROFILE = path.join(os.tmpdir(), 'pbp-shoot-edge');
const ONLY_LAYER = arg('layer', null);
const METERS = [0.3, 0.6, 0.9];

const EDGE = [
  'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe',
  'C:/Program Files/Microsoft/Edge/Application/msedge.exe',
].find((p) => existsSync(p));

/* Each layer says how to make itself visible in a single still frame.
 * `only` isolates a sustained layer; `fire` spawns a one-shot (or a harness
 * event); `budget` is the virtual time the page is allowed before the shot. */
const SHOTS = [
  { name: 'flash', only: 'none', fire: 'flash,flash,flash,flash', seek: 320, budget: 900 },
  { name: 'gifRain', only: 'none', fire: 'gifRain,gifRain,gifRain,gifRain,gifRain,gifRain', seek: 1300, budget: 1400 },
  { name: 'melt', only: 'melt', budget: 1200 },
  { name: 'blur', only: 'blur', budget: 1200 },
  { name: 'spiral', only: 'spiral', budget: 1600 },
  { name: 'overlay', only: 'overlay', budget: 1600 },
  { name: 'videoCard', only: 'none', fire: 'videoCard', budget: 2600 },
  { name: 'glitchGrab', only: 'none', fire: 'grab', budget: 1200 },
  // and one shot of the whole stack, which is what the player actually sees
  { name: 'all', prime: 6, card: true, seek: 500, budget: 1600 },
];

function urlFor(shot, meter) {
  // still=1 because a headless page sees no wall-clock time: without it every
  // layer is photographed at the first frame of its fade, which is nothing
  const q = new URLSearchParams({ meter: String(meter), quiet: '1', still: '1' });
  if (shot.seek) q.set('seek', String(shot.seek));
  if (shot.only) q.set('only', shot.only);
  if (shot.prime) q.set('prime', String(shot.prime));
  if (shot.fire) q.set('fire', shot.fire);
  // every layer but the card itself is photographed on a board the card is not
  // covering; the `all` shot keeps it, because that is what the player sees
  if (shot.card !== true) q.set('noturn', '1');
  return `http://localhost:${PORT}/piecebypiece/dev/ramp.html?${q}`;
}

/** One headless run, resolved when the process exits. Never runs in parallel. */
function shoot(url, file, budget) {
  return new Promise((resolve) => {
    const args = [
      '--headless=new', '--disable-gpu', '--no-first-run', '--no-default-browser-check',
      '--disable-background-networking', '--disable-extensions', '--mute-audio',
      '--autoplay-policy=no-user-gesture-required',
      `--user-data-dir=${PROFILE}`,
      '--window-size=1280,800',
      `--virtual-time-budget=${budget}`,
      '--dump-dom',
      `--screenshot=${file}`,
      url,
    ];
    const child = spawn(EDGE, args, { stdio: ['ignore', 'pipe', 'pipe'] });
    let dom = '';
    child.stdout.on('data', (b) => { dom += b; });
    child.stderr.on('data', () => { /* Edge is chatty on stderr; the DOM is truth */ });
    const kill = setTimeout(() => { try { child.kill(); } catch { /* gone */ } }, 45000);
    child.on('exit', () => {
      clearTimeout(kill);
      const m = /id="errors" data-count="(\d+)"/.exec(dom);
      const errors = m ? Number(m[1]) : -1;
      let text = '';
      if (errors > 0) {
        const t = /id="errors"[^>]*>([\s\S]*?)<\/div>/.exec(dom);
        text = t ? t[1].trim().slice(0, 400) : '';
      }
      resolve({ errors, text, ticks: /ticks (\d+)/.exec(dom)?.[1] });
    });
  });
}

async function main() {
  if (!EDGE) { console.error('msedge.exe not found'); process.exit(2); }
  mkdirSync(OUT, { recursive: true });
  try { rmSync(PROFILE, { recursive: true, force: true }); } catch { /* first run */ }

  let bad = 0;
  for (const shot of SHOTS) {
    if (ONLY_LAYER && shot.name !== ONLY_LAYER) continue;
    for (const meter of METERS) {
      const file = path.join(OUT, `layer-${shot.name}-m${String(meter).replace('.', '')}.png`);
      const res = await shoot(urlFor(shot, meter), file, shot.budget);
      const tag = res.errors === 0 ? 'ok  ' : (res.errors < 0 ? 'DOM?' : 'ERR ');
      if (res.errors !== 0) bad += 1;
      console.log(`${tag} ${shot.name} @ ${meter}  ->  ${path.basename(file)}${res.text ? '\n     ' + res.text : ''}`);
    }
  }
  console.log(bad ? `\n${bad} run(s) reported page errors` : '\nall runs clean, zero console errors');
  process.exit(bad ? 1 : 0);
}

main();
