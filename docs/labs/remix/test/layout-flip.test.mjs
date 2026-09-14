import test from 'node:test';
import assert from 'node:assert/strict';
import { LAYOUTS, MOTION_LAYOUTS, layoutAtFrame, rectsAtFrame, flipSlot } from '../engine/layout.js';
import { createProject } from '../engine/project.js';

const MODES = LAYOUTS.map((l) => l.id).concat(['deck']);
const ORIENTATIONS = ['landscape', 'portrait'];
const SEEDS = [0x51ee, 0xb00b, 12345];
const EPS = 1e-6;

const cfgFor = (mode, n, orientation, seed, frames) => ({
  tileCount: n,
  mode,
  stageMs: 600,
  deckHold: 15,
  fps: 15,
  frames,
  orientation,
  seed,
});

const close = (a, b, what) => assert.ok(Math.abs(a - b) < EPS, `${what}: ${a} vs ${b}`);

/** The one rule: a flipped slot is the plain slot turned around the middle. */
function assertMirrored(plain, flipped, where) {
  assert.equal(flipped.tileIndex, plain.tileIndex, `${where} tileIndex`);
  assert.equal(flipped.frameOffset, plain.frameOffset, `${where} frameOffset`);
  // mirrored across the canvas: the left edge of one is the right edge of the other
  close(flipped.rect.x + plain.rect.x + plain.rect.w, 1, `${where} x`);
  close(flipped.rect.y, plain.rect.y, `${where} y`);
  close(flipped.rect.w, plain.rect.w, `${where} w`);
  close(flipped.rect.h, plain.rect.h, `${where} h`);
  // size, fade and draw order read the same either way round
  assert.equal(flipped.alpha, plain.alpha, `${where} alpha`);
  assert.equal(flipped.scale, plain.scale, `${where} scale`);
  assert.equal(flipped.z, plain.z, `${where} z`);
  assert.equal(flipped.gap, plain.gap, `${where} gap`);
  assert.equal(flipped.plate, plain.plate, `${where} plate`);
  // a tilt to the right becomes the same tilt to the left
  close(flipped.rot || 0, -(plain.rot || 0), `${where} rot`);
  if (plain.crop && plain.crop.cx != null) {
    close(flipped.crop.cx + plain.crop.cx, 1, `${where} crop.cx`);
    close(flipped.crop.cy == null ? 0 : flipped.crop.cy, plain.crop.cy == null ? 0 : plain.crop.cy, `${where} crop.cy`);
    close(flipped.crop.z == null ? 1 : flipped.crop.z, plain.crop.z == null ? 1 : plain.crop.z, `${where} crop.z`);
  }
}

test('flip mirrors every mode at every frame, and never touches the pictures', () => {
  let checked = 0;
  for (const mode of MODES) {
    for (const orientation of ORIENTATIONS) {
      for (const n of [1, 2, 4, 7]) {
        const seed = SEEDS[n % SEEDS.length];
        const frames = 75;
        const plainCfg = cfgFor(mode, n, orientation, seed, frames);
        const flipCfg = Object.assign({}, plainCfg, { flip: true });
        for (const frame of [0, 1, 7, 18, 37, 56, 74, 75]) {
          const plain = layoutAtFrame(plainCfg, frame);
          const flipped = layoutAtFrame(flipCfg, frame);
          const where = `${mode}/${orientation}/${n}/f${frame}`;
          assert.equal(flipped.stage, plain.stage, `${where} stage`);
          assert.equal(flipped.slots.length, plain.slots.length, `${where} slot count`);
          for (let i = 0; i < plain.slots.length; i++) {
            assertMirrored(plain.slots[i], flipped.slots[i], `${where}#${i}`);
            checked++;
          }
        }
      }
    }
  }
  assert.ok(checked > 500, `enough slots checked, got ${checked}`);
});

test('flip leaves the plain layout alone and is its own undo', () => {
  const cfg = cfgFor('stack', 5, 'landscape', 0x51ee, 75);
  const before = JSON.parse(JSON.stringify(layoutAtFrame(cfg, 30).slots));
  layoutAtFrame(Object.assign({}, cfg, { flip: true }), 30);
  assert.deepEqual(layoutAtFrame(cfg, 30).slots, before, 'the unflipped call is unchanged');
  for (const slot of before) {
    const there = flipSlot(slot);
    const back = flipSlot(there);
    close(back.rect.x, slot.rect.x, 'flip twice is where it started');
    close(back.rot || 0, slot.rot || 0, 'flip twice is the same tilt');
  }
});

test('a flipped motion layout still closes its loop', () => {
  // only the motion layouts promise this; grow and friends are still arriving at frame 0
  for (const mode of MOTION_LAYOUTS.map((l) => l.id)) {
    const cfg = cfgFor(mode, 2, 'landscape', 0xb00b, 60);
    cfg.flip = true;
    assert.deepEqual(layoutAtFrame(cfg, 60).slots, layoutAtFrame(cfg, 0).slots, `${mode} loop`);
  }
});

test('the hit overlay flips with the picture', () => {
  const ids = ['t0', 't1', 't2'];
  for (const mode of ['grow', 'flat', 'shuffle', 'mirror', 'carousel', 'kenburns']) {
    const cfg = { tileIds: ids, mode, stageMs: 600, fps: 15, frames: 75, orientation: 'landscape', seed: 7 };
    const plain = rectsAtFrame(cfg, 40);
    const flipped = rectsAtFrame(Object.assign({}, cfg, { flip: true }), 40);
    assert.equal(flipped.length, plain.length, `${mode} count`);
    for (let i = 0; i < plain.length; i++) {
      if (!plain[i]) { assert.equal(flipped[i], null, `${mode}#${i} still off`); continue; }
      close(flipped[i].x + plain[i].x + plain[i].w, 1, `${mode}#${i} x`);
      close(flipped[i].w, plain[i].w, `${mode}#${i} w`);
    }
  }
});

test('flip is project state: default off, set, saved and read back', () => {
  const p = createProject({ orientation: 'landscape' });
  assert.equal(p.layout.flip, false, 'off until asked for');
  p.setLayout({ flip: true });
  assert.equal(p.layout.flip, true);
  const json = p.toJSON();
  assert.equal(json.layout.flip, true, 'toJSON carries it');
  const back = createProject.fromJSON(json, {});
  assert.equal(back.layout.flip, true, 'fromJSON reads it back');
  p.setLayout({ flip: false });
  assert.equal(p.layout.flip, false);
  p.setLayout({ mode: 'flat' });
  assert.equal(p.layout.flip, false, 'a mode change leaves flip where it was');
  p.dispose(); back.dispose();
});

test('a project saved before flip existed loads unflipped', () => {
  const p = createProject({ orientation: 'landscape' });
  const json = p.toJSON();
  delete json.layout.flip;
  const old = createProject.fromJSON(json, {});
  assert.equal(old.layout.flip, false);
  p.dispose(); old.dispose();
});

test('undo puts flip back', () => {
  const p = createProject({ orientation: 'landscape' });
  p.commit('base');
  p.setLayout({ flip: true });
  p.commit('flip');
  assert.equal(p.layout.flip, true);
  p.undo();
  assert.equal(p.layout.flip, false, 'undo takes it off again');
  p.redo();
  assert.equal(p.layout.flip, true, 'redo puts it back');
  p.dispose();
});
