import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import vm from 'node:vm';
import { readDragLook, saveDragLook } from '../look-preference.js';

test('drag look is opt-in, persists both choices, and tolerates blocked storage', () => {
  const values = new Map();
  globalThis.localStorage = { getItem: k => values.get(k), setItem: (k, v) => values.set(k, v) };
  try {
    assert.equal(readDragLook(), false);
    saveDragLook(true); assert.equal(readDragLook(), true);
    saveDragLook(false); assert.equal(readDragLook(), false);
    globalThis.localStorage = { getItem() { throw Error('blocked'); }, setItem() { throw Error('blocked'); } };
    assert.equal(readDragLook(), false);
    assert.doesNotThrow(() => saveDragLook(true));
  } finally { delete globalThis.localStorage; }
});

test('mouse capture respects drag look while keeping touch and pointer drag available', () => {
  const source = readFileSync(new URL('../scene.js', import.meta.url), 'utf8');
  const start = source.indexOf("  canvas.addEventListener('pointerdown', (e) => {");
  const end = source.indexOf('  // Chromium refuses', start);
  for (const [dragLook, pointerType, expectedLocks] of [[false, 'mouse', 1], [true, 'mouse', 0], [false, 'touch', 0], [true, 'touch', 0]]) {
    let handler, locks = 0, captures = 0;
    const context = vm.createContext({
      canvas: { addEventListener: (_, f) => { handler = f; }, requestPointerLock: () => { locks++; }, setPointerCapture: () => { captures++; } },
      canWalk: () => true, o: { dragLook: () => dragLook }, locked: false, lockRefused: false, lockFailed() {},
    });
    vm.runInContext('let drag = null, tap = null;\n' + source.slice(start, end), context);
    handler({ button: 0, pointerType, pointerId: 1, clientX: 10, clientY: 20 });
    assert.equal(locks, expectedLocks);
    assert.equal(captures, 1);
    assert.equal(vm.runInContext('!!drag', context), true);
    assert.equal(vm.runInContext('!!tap', context), expectedLocks === 0);
  }
});
