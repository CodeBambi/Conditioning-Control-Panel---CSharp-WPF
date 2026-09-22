import test from 'node:test';
import assert from 'node:assert/strict';
import { BEAT, WINDOW, phrase, cuesFor, matchCue, scoreFor, makeClock } from './feel-score.js';

test('a hidden or paused clock neither advances nor catches up on resume', () => {
  let now = 30;
  const clock = makeClock(() => now);
  now += 2.4; clock.pause();
  const before = clock.time();
  now += 180; clock.pause();
  assert.equal(clock.time(), before);
  clock.resume();
  assert.ok(Math.abs(clock.time() - before) < 1e-10);
  now += .2;
  assert.ok(Math.abs(clock.time() - before - .2) < 1e-10);
});

test('judgement accepts early and late taps, but never credits a wrong or repeated pad', () => {
  const cues = cuesFor(phrase(0));
  assert.equal(matchCue(cues, cues[0].pad, cues[0].time - WINDOW + .001), cues[0]);
  assert.equal(matchCue(cues, cues[0].pad, cues[0].time + WINDOW - .001), cues[0]);
  assert.equal(matchCue(cues, 3, cues[0].time), null);
  assert.equal(matchCue(cues, cues[0].pad, cues[0].time + WINDOW + .001), null);
  cues[0].hit = true;
  assert.equal(matchCue(cues, cues[0].pad, cues[0].time), null);
});

test('the first response arrives within ten seconds and phrases have clear breathing room', () => {
  for (let round = 0; round < 6; round++) for (const branch of [0, 1]) {
    const notes = phrase(round, branch), cues = cuesFor(notes, BEAT * 2);
    assert.equal(notes.length, 4);
    assert.ok(cues[0].time < 10);
    assert.ok(cues.every((c, i) => !i || c.time - cues[i - 1].time > WINDOW * 2));
    assert.ok(notes.every(n => n.pad >= 0 && n.pad < 4));
  }
});

test('the room never plays the answer melody, while completed phrases return in the backing', () => {
  const notes = phrase(1), base = scoreFor(notes), fuller = scoreFor(notes, 2, phrase(0));
  assert.equal(base.filter(n => n.gain === .16 && n.time >= 8 * BEAT).length, 0);
  assert.ok(fuller.length > base.length);
  assert.equal(fuller.filter(n => n.gain === .055).length, 8);
  assert.ok(fuller.every((n, i) => !i || n.time >= fuller[i - 1].time));
  assert.ok(fuller.every(n => n.time < 16 * BEAT));
});

test('the full silent duet ends once and destroy releases every loop and listener', async () => {
  const { create } = await import('./feel.js');
  const previous = new Map(['document', 'window', 'performance', 'requestAnimationFrame', 'cancelAnimationFrame', 'setInterval', 'clearInterval'].map(k => [k, globalThis[k]]));
  class Node {
    constructor() { this.children = []; this.dataset = {}; this.events = new Map(); this.style = { setProperty() {} }; }
    append(...children) { this.children.push(...children); }
    replaceChildren(...children) { this.children = children; }
    setAttribute() {}
    addEventListener(type, fn) { if (!this.events.has(type)) this.events.set(type, new Set()); this.events.get(type).add(fn); }
    removeEventListener(type, fn) { this.events.get(type)?.delete(fn); }
    remove() {}
    fire(type, event = {}) { for (const fn of this.events.get(type) || []) fn(event); }
  }
  const doc = new Node(); doc.hidden = false; doc.createElement = () => new Node();
  let now = 0, serial = 0, reports = 0;
  const frames = new Map(), intervals = new Map(), root = new Node();
  globalThis.document = doc; globalThis.window = { matchMedia: () => ({ matches: false }) };
  globalThis.performance = { now: () => now * 1000 };
  globalThis.requestAnimationFrame = fn => { const id = ++serial; frames.set(id, fn); return id; };
  globalThis.cancelAnimationFrame = id => frames.delete(id);
  globalThis.setInterval = fn => { const id = ++serial; intervals.set(id, fn); return id; };
  globalThis.clearInterval = id => intervals.delete(id);
  const all = (node = root) => [node, ...node.children.flatMap(n => all(n))];
  const click = text => all().find(n => n.textContent === text)?.fire('click');
  const tick = seconds => {
    now += seconds;
    for (const fn of intervals.values()) fn();
    const batch = [...frames.values()]; frames.clear(); batch.forEach(fn => fn());
  };
  try {
    const game = create({ root, audioAudible: false, endClass() { reports++; } });
    game.start(); click('Play duet'); await Promise.resolve();
    tick(3); game.pause(); tick(120);
    assert.equal(frames.size, 0); assert.equal(reports, 0);
    game.resume(); tick(1);
    assert.equal(all().find(n => n.className === 'ef-progress').textContent, '1 / 6');
    for (let i = 0; i < 3000; i++) {
      tick(.03);
      const choice = all().find(n => n.textContent === 'Let it rise');
      if (choice && !all().find(n => n.className === 'ef-overlay').hidden) choice.fire('click');
    }
    assert.equal(reports, 1);
    assert.equal(all().find(n => n.className === 'ef-overlay').children[0].textContent, 'You made that');
    game.resume(); tick(100); assert.equal(reports, 1);
    game.destroy(); game.destroy();
    assert.equal(frames.size, 0); assert.equal(intervals.size, 0);
    assert.ok([...doc.events.values()].every(set => set.size === 0));
  } finally {
    for (const [key, value] of previous) globalThis[key] = value;
  }
});
