#!/usr/bin/env node
/* ============================================================================
 * build-backroom-room-assets.mjs - the Back Room 3D room's speed pass.
 *
 *   node ConditioningControlPanel/Scripts/build-backroom-room-assets.mjs [--source DIR] [--tools DIR] [--check-only] [--only counter.glb]
 *
 * READS blender-scripting (the shell and the five fixture glbs, the default wall
 * ads) and WRITES optimized copies into Resources/web/backroom/room/assets/.
 * Nothing is ever written back to the source tree: every source is opened
 * read-only and every output path is checked to sit under the client folder.
 *
 * Per glb, in order:
 *   1. strip textures the room replaces at runtime (wall screen ads, floor inlay)
 *   2. dedup, then pull static leaf meshes up to the nearest kept group
 *      (a light flatten that leaves the ceiling group and the dealers intact)
 *   3. join compatible static meshes per group (the draw-call win), prune
 *   4. textures to WebP, at most 1024 px
 *   5. meshopt (reorder + quantize + EXT_meshopt_compression)
 * Then a node check: every name the room looks up (PROTECT below) is present in
 * the copy as often as in the source, or the run fails.
 *
 * The room never moves a node that a mesh sits on (the floor spins in its
 * shader, reels scroll their texture), so quantization's node offsets are safe.
 *
 * Tools are pinned and installed once OUTSIDE the repo (default
 * %LOCALAPPDATA%/ccp-tools/gltf-transform-4.5.0). The decoder the page needs is
 * vendored at Resources/web/vendor/three/addons/libs/meshopt_decoder.module.js.
 * ==========================================================================*/

import { execSync } from 'node:child_process';
import { createHash } from 'node:crypto';
import { existsSync, mkdirSync, readFileSync, statSync, writeFileSync } from 'node:fs';
import { createRequire } from 'node:module';
import { dirname, join, resolve, relative, basename } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';

const HERE = dirname(fileURLToPath(import.meta.url));
const WEB = resolve(HERE, '..', 'Resources', 'web', 'backroom');
const OUT = join(WEB, 'room', 'assets');

const arg = (name, dflt) => { const i = process.argv.indexOf(name); return i > 0 ? process.argv[i + 1] : dflt; };
const SOURCE = resolve(arg('--source', 'C:/Projects/blender-scripting'));
const TOOLS = resolve(arg('--tools', join(process.env.LOCALAPPDATA || join(HERE, '..', '..', '.tools'), 'ccp-tools', 'gltf-transform-4.5.0')));
const CHECK_ONLY = process.argv.includes('--check-only');
const ONLY = arg('--only', null);

const PINS = ['@gltf-transform/core@4.5.0', '@gltf-transform/extensions@4.5.0', '@gltf-transform/functions@4.5.0',
  'meshoptimizer@1.2.0', 'sharp@0.35.4'];

/** Names the room reads. Leaves matching these are never joined, and their counts are checked. */
export const PROTECT = /^(shelf_(?:jackpot_remix|rt_demo|high_roller|flashes_v2|bubbles_v2|rt_bundle_[123])$|sconce_globe|media_screen_\d|spiral_inlay|ceiling$|lights_chase_\d|bulb_\d|canopy_bulb_\d|rim_bulb_\d|EMI_glass|marquee$|screen_jackpot|screen_status|reel_\d|title_screen|status_screen|center_spiral|inset_spiral_|emi_topper|emi_dealer|golden_emi_attendant|alcove_return)/;
/** Groups the room transforms or toggles as a whole: static leaves are pulled up to these, not past. */
const KEEP_GROUP = /^(shelf_(?:jackpot_remix|rt_demo|high_roller|flashes_v2|bubbles_v2|rt_bundle_[123])|ceiling|center_spiral|emi_topper|emi_dealer|golden_emi_attendant|EMI_root)$/;
/** Materials whose textures the room replaces with its own shader. */
const RUNTIME_TEXTURED = /^(screen_picture_\d|floor_spiral)$/;

const JOBS = [
  { from: 'backroom/out/shell.glb', to: 'shell.glb' },
  { from: 'slot/out/slot.glb', to: 'slot.glb' },
  { from: 'wheel/out/wheel.glb', to: 'wheel.glb' },
  { from: 'counter/prize-build/out/counter-prizes.glb', to: 'counter.glb' },
  { from: 'card-table/out/card-table.glb', to: 'card-table.glb' },
  { from: 'roulette/out/roulette.glb', to: 'roulette.glb' },
];
const ADS = ['arcademy', 'dtrh', 'focus-gaze'];
if (ONLY && !JOBS.some((job) => job.to === ONLY)) throw new Error('Unknown --only asset: ' + ONLY);

function inClient(p) {
  const r = relative(WEB, resolve(p));
  if (r.startsWith('..') || resolve(p).toLowerCase().startsWith(SOURCE.toLowerCase())) throw new Error('refusing to write outside the client: ' + p);
  return p;
}

function ensureTools() {
  if (existsSync(join(TOOLS, 'node_modules', '@gltf-transform', 'functions'))) return;
  console.log('installing pinned tools into ' + TOOLS);
  mkdirSync(TOOLS, { recursive: true });
  if (!existsSync(join(TOOLS, 'package.json'))) writeFileSync(join(TOOLS, 'package.json'), '{"private":true}');
  execSync('npm install --no-audit --no-fund ' + PINS.join(' '), { cwd: TOOLS, stdio: 'inherit' });
}

async function load() {
  ensureTools();
  const req = createRequire(join(TOOLS, 'package.json'));
  const imp = (m) => import(pathToFileURL(req.resolve(m)).href);
  const [core, ext, fn, mo, sharp] = await Promise.all([imp('@gltf-transform/core'), imp('@gltf-transform/extensions'),
    imp('@gltf-transform/functions'), imp('meshoptimizer'), imp('sharp')]);
  await mo.MeshoptEncoder.ready; await mo.MeshoptDecoder.ready;
  return { core, ext, fn, mo, sharp: sharp.default || sharp };
}

const sha = (buf) => createHash('sha256').update(buf).digest('hex');
const mb = (n) => (n / 1048576).toFixed(2) + ' MB';

function stats(doc) {
  const root = doc.getRoot();
  let tris = 0, draws = 0;
  for (const node of root.listNodes()) {
    const mesh = node.getMesh();
    if (!mesh) continue;
    for (const p of mesh.listPrimitives()) {
      draws++;
      const idx = p.getIndices();
      tris += (idx ? idx.getCount() : p.getAttribute('POSITION').getCount()) / 3;
    }
  }
  const counts = {};
  for (const n of root.listNodes()) { const m = n.getName().match(PROTECT); if (m) counts[m[1]] = (counts[m[1]] || 0) + 1; }
  return { nodes: root.listNodes().length, tris: Math.round(tris), draws, counts };
}

/** Pull static leaves up to the nearest kept group (or the scene root), baking their transforms. */
function liftStatics(doc) {
  const root = doc.getRoot();
  const scene = root.listScenes()[0];
  for (const node of root.listNodes()) {
    if (!node.getMesh() || node.listChildren().length || PROTECT.test(node.getName())) continue;
    let anchor = node.getParentNode();
    const path = [];
    while (anchor && !KEEP_GROUP.test(anchor.getName())) { path.push(anchor); anchor = anchor.getParentNode(); }
    if (!path.length) continue;   // already a direct child of its group
    const world = node.getWorldMatrix();
    let local = world;
    if (anchor) {
      const inv = new Array(16).fill(0);
      invert(inv, anchor.getWorldMatrix());
      local = multiply(new Array(16).fill(0), inv, world);
    }
    node.getParentNode().removeChild(node);
    if (anchor) anchor.addChild(node); else scene.addChild(node);
    node.setMatrix(local);
  }
}

// 4x4 column-major helpers (gl-matrix semantics), kept local so the script has no other deps.
function multiply(o, a, b) {
  for (let c = 0; c < 4; c++) for (let r = 0; r < 4; r++) {
    let s = 0; for (let k = 0; k < 4; k++) s += a[k * 4 + r] * b[c * 4 + k]; o[c * 4 + r] = s;
  }
  return o;
}
function invert(o, m) {
  const [a00, a01, a02, a03, a10, a11, a12, a13, a20, a21, a22, a23, a30, a31, a32, a33] = m;
  const b00 = a00 * a11 - a01 * a10, b01 = a00 * a12 - a02 * a10, b02 = a00 * a13 - a03 * a10, b03 = a01 * a12 - a02 * a11;
  const b04 = a01 * a13 - a03 * a11, b05 = a02 * a13 - a03 * a12, b06 = a20 * a31 - a21 * a30, b07 = a20 * a32 - a22 * a30;
  const b08 = a20 * a33 - a23 * a30, b09 = a21 * a32 - a22 * a31, b10 = a21 * a33 - a23 * a31, b11 = a22 * a33 - a23 * a32;
  const det = 1 / (b00 * b11 - b01 * b10 + b02 * b09 + b03 * b08 - b04 * b07 + b05 * b06);
  o[0] = (a11 * b11 - a12 * b10 + a13 * b09) * det; o[1] = (a02 * b10 - a01 * b11 - a03 * b09) * det;
  o[2] = (a31 * b05 - a32 * b04 + a33 * b03) * det; o[3] = (a22 * b04 - a21 * b05 - a23 * b03) * det;
  o[4] = (a12 * b08 - a10 * b11 - a13 * b07) * det; o[5] = (a00 * b11 - a02 * b08 + a03 * b07) * det;
  o[6] = (a32 * b02 - a30 * b05 - a33 * b01) * det; o[7] = (a20 * b05 - a22 * b02 + a23 * b01) * det;
  o[8] = (a10 * b10 - a11 * b08 + a13 * b06) * det; o[9] = (a01 * b08 - a00 * b10 - a03 * b06) * det;
  o[10] = (a30 * b04 - a31 * b02 + a33 * b00) * det; o[11] = (a21 * b02 - a20 * b04 - a23 * b00) * det;
  o[12] = (a11 * b07 - a10 * b09 - a12 * b06) * det; o[13] = (a00 * b09 - a01 * b07 + a02 * b06) * det;
  o[14] = (a31 * b01 - a30 * b03 - a32 * b00) * det; o[15] = (a20 * b03 - a21 * b01 + a22 * b00) * det;
  return o;
}

async function main() {
  const t = await load();
  const io = new t.core.NodeIO().registerExtensions(t.ext.ALL_EXTENSIONS)
    .registerDependencies({ 'meshopt.encoder': t.mo.MeshoptEncoder, 'meshopt.decoder': t.mo.MeshoptDecoder });
  mkdirSync(OUT, { recursive: true });
  const rows = [];
  let failed = 0;

  for (const job of JOBS.filter((job) => !ONLY || job.to === ONLY)) {
    const src = join(SOURCE, job.from);
    const dst = inClient(join(OUT, job.to));
    const srcBuf = readFileSync(src);
    const before = stats(await io.readBinary(new Uint8Array(srcBuf)));

    if (!CHECK_ONLY) {
      const doc = await io.readBinary(new Uint8Array(srcBuf));
      for (const m of doc.getRoot().listMaterials()) {
        if (!RUNTIME_TEXTURED.test(m.getName())) continue;
        m.setBaseColorTexture(null); m.setEmissiveTexture(null);
      }
      // The room supplies one decorated counter marquee, replacing the overlapping baked titles.
      const retired = job.to === 'counter.glb' ? ['header_title', 'service_title']
        : job.to === 'shell.glb' ? ['house_title', 'house_sign_frame', 'house_sign_face'] : [];
      for (const n of doc.getRoot().listNodes()) if (retired.includes(n.getName())) n.dispose();
      // Fit the east wall display inside its bay, clear of the card niche.
      if (job.to === 'shell.glb') {
        const screen = doc.getRoot().listNodes().find((n) => n.getName() === 'screen_mount_1');
        if (!screen) throw new Error('East wall screen mount missing');
        const p = screen.getTranslation();
        screen.setTranslation([p[0], p[1], p[2] + .65]);
        screen.setScale(screen.getScale().map((v) => v * .75));
      }
      await doc.transform(t.fn.dedup());
      liftStatics(doc);
      await doc.transform(
        t.fn.join({ keepNamed: false, filter: (n) => !PROTECT.test(n.getName()) }),
        t.fn.prune({ keepLeaves: false, keepAttributes: true }),   // the room paints its own maps onto untextured meshes
        t.fn.textureCompress({ encoder: t.sharp, targetFormat: 'webp', resize: [1024, 1024], quality: 88 }),
        t.fn.meshopt({ encoder: t.mo.MeshoptEncoder, level: 'medium' }),
      );
      writeFileSync(dst, await io.writeBinary(doc));
    }

    const outBuf = readFileSync(dst);
    const after = stats(await io.readBinary(new Uint8Array(outBuf)));
    const lost = Object.keys(before.counts).filter((k) => (after.counts[k] || 0) !== before.counts[k]);
    if (lost.length) { failed++; console.error(`FAIL ${job.to}: node names changed: ${lost.map((k) => `${k} ${before.counts[k]} -> ${after.counts[k] || 0}`).join(', ')}`); }
    rows.push({ file: job.to, srcMB: srcBuf.length, outMB: outBuf.length, b: before, a: after, sha: sha(srcBuf).slice(0, 12), ok: !lost.length });
  }

  // Default wall art: WebP copies at 1280 px wide (the screens are 2.12 m, seen from 2 m away).
  for (const ad of ONLY ? [] : ADS) {
    const src = join(SOURCE, 'backroom', 'out', 'ads', ad + '.png');
    const dst = inClient(join(OUT, 'ads', ad + '.webp'));
    if (!CHECK_ONLY) {
      mkdirSync(dirname(dst), { recursive: true });
      await t.sharp(readFileSync(src)).resize({ width: 1280, withoutEnlargement: true }).webp({ quality: 84 }).toFile(dst);
    }
    rows.push({ file: 'ads/' + basename(dst), srcMB: statSync(src).size, outMB: statSync(dst).size, sha: sha(readFileSync(src)).slice(0, 12), ok: true });
  }

  console.log('\nfile                 source      copy        tris before/after     mesh prims before/after  node names');
  let s = 0, o = 0;
  for (const r of rows) {
    s += r.srcMB; o += r.outMB;
    const geo = r.b ? `${String(r.b.tris).padStart(7)} / ${String(r.a.tris).padEnd(7)}   ${String(r.b.draws).padStart(5)} / ${String(r.a.draws).padEnd(5)}` : ''.padEnd(40);
    console.log(`${r.file.padEnd(20)} ${mb(r.srcMB).padStart(9)}  ${mb(r.outMB).padStart(9)}   ${geo.padEnd(44)}  ${r.ok ? 'ok' : 'CHANGED'}  src ${r.sha}`);
  }
  console.log(`${'total'.padEnd(20)} ${mb(s).padStart(9)}  ${mb(o).padStart(9)}`);
  if (failed) { console.error(`\nFAIL: ${failed} file(s) lost names the room reads`); process.exit(1); }
  console.log('\nOK: every protected node name survived');
}

main().catch((e) => { console.error(e); process.exit(1); });
