import test from 'node:test';
import assert from 'node:assert/strict';
import { makeRound, PICTURES, report } from './feel-model.js';

test('every deal contains precisely one lie with an unambiguous original', () => {
  let state = 17;
  const random = () => ((state = (Math.imul(state, 1664525) + 1013904223) >>> 0) / 4294967296);
  const seats = new Set(), symbols = new Set();
  for (let n = 0; n < 1000; n++) {
    const deal = makeRound(random, n);
    assert.equal(new Set(deal.original).size, 3);
    assert.equal(new Set(deal.replay).size, 3);
    const changed = deal.original.flatMap((id, i) => id === deal.replay[i] ? [] : [i]);
    assert.deepEqual(changed, [deal.changed]);
    assert.ok(!deal.original.includes(deal.replay[deal.changed]));
    for (const id of [...deal.original, ...deal.replay]) assert.ok(PICTURES[id]);
    seats.add(deal.changed); deal.original.forEach(id => symbols.add(id));
  }
  assert.equal(seats.size, 3); assert.equal(symbols.size, PICTURES.length);
});

test('perfect recall earns full accuracy without a speed term', () => {
  assert.deepEqual(report(6), { metrics: { composite: 1, correct: 6, rounds: 6 }, hardGates: {}, flavorXp: 0, feelVersion: 2 });
  assert.equal(report(3).metrics.composite, .5);
  assert.equal(report(0).metrics.composite, 0);
});

// Small host double: execute the real lifecycle with a controllable frame clock.
import { create } from './feel.js';
function hostDouble() {
  let id = 0, now = 0;
  const frames = new Map(), events = new Map();
  const win = { matchMedia: () => ({ matches: true }), requestAnimationFrame: fn => { frames.set(++id, fn); return id; }, cancelAnimationFrame: n => frames.delete(n) };
  const doc = { hidden: false, defaultView: win, createElement: tag => new Element(tag), addEventListener: (name, fn) => events.set(name, fn), removeEventListener: name => events.delete(name) };
  class Element {
    constructor(tag) {
      this.tag = tag; this.ownerDocument = doc; this.children = []; this.dataset = {}; this.style = {}; this.classes = new Set(); this.events = new Map();
      this.classList = { add: name => this.classes.add(name), toggle: (name, on) => on ? this.classes.add(name) : this.classes.delete(name) };
    }
    set className(value) { this.classes = new Set(value.split(' ')); }
    set innerHTML(value) {
      this.children = []; const stack = [this];
      for (const part of value.matchAll(/<([^>]+)>/g)) {
        const token = part[1];
        if (token.startsWith('/')) { if (stack.length > 1) stack.pop(); continue; }
        const node = new Element(token.split(/\s/)[0]);
        const cls = token.match(/class="([^"]*)"/); if (cls) node.className = cls[1];
        for (const attr of token.matchAll(/data-([a-z]+)(?:="([^"]*)")?/g)) node.dataset[attr[1]] = attr[2] || '';
        stack.at(-1).append(node); if (!token.endsWith('/')) stack.push(node);
      }
    }
    append(...nodes) { for (const node of nodes) { node.parent = this; this.children.push(node); } }
    replaceChildren() { this.children = []; }
    querySelectorAll(selector) { return this.children.flatMap(node => [...(node.matches(selector) ? [node] : []), ...node.querySelectorAll(selector)]); }
    querySelector(selector) { return this.querySelectorAll(selector)[0]; }
    matches(selector) { return selector.startsWith('.') ? this.classes.has(selector.slice(1)) : this.tag === selector; }
    closest(selector) { return this.matches(selector) ? this : this.parent?.closest(selector); }
    contains(node) { return node === this || this.children.some(child => child.contains(node)); }
    insertAdjacentHTML(_where, html) { const box = new Element('div'); box.innerHTML = html; this.append(...box.children); }
    addEventListener(name, fn) { this.events.set(name, fn); }
    removeEventListener(name) { this.events.delete(name); }
    remove() { if (this.parent) this.parent.children = this.parent.children.filter(node => node !== this); }
  }
  const root = new Element('main');
  return { root, doc, frames, events,
    advance(ms) { for (let i = 0; i < ms; i += 16) { now += 16; const due = [...frames.values()]; frames.clear(); due.forEach(fn => fn(now)); } },
    click(node) { root.querySelector('.ir-feel').events.get('click')({ target: node }); },
  };
}

test('pause, hidden page and destroy freeze the real picture timeline', () => {
  const host = hostDouble(); let reports = 0;
  const game = create({ root: host.root, audioAudible: false, endClass: () => reports++ });
  game.start({ random: () => 0 }); host.advance(1000);
  game.pause(); host.advance(20000);
  assert.ok(host.root.querySelector('.ir-watch')); assert.equal(host.frames.size, 0);
  game.resume(); host.doc.hidden = true; host.events.get('visibilitychange')(); host.advance(20000);
  assert.ok(host.root.querySelector('.ir-watch')); assert.equal(host.frames.size, 0);
  host.doc.hidden = false; host.events.get('visibilitychange')(); host.advance(3600);
  assert.equal(host.root.querySelectorAll('.ir-choice').length, 3);
  game.destroy(); host.advance(20000);
  assert.equal(reports, 0); assert.equal(host.frames.size, 0); assert.equal(host.events.size, 0); assert.equal(host.root.children.length, 0);
});

test('six repaired rooms finish once and accept no duplicate answer', () => {
  const host = hostDouble(), reports = [];
  const game = create({ root: host.root, audioAudible: false, endClass: value => reports.push(value) });
  game.start({ random: () => 0 });
  for (let n = 0; n < 6; n++) {
    host.advance(4600);
    const answer = host.root.querySelectorAll('.ir-choice')[0];
    host.click(answer); host.click(answer); host.advance(600);
    if (n < 5) host.click(host.root.querySelector('.ir-next'));
  }
  host.advance(6000);
  assert.equal(reports.length, 1); assert.equal(reports[0].metrics.composite, 1);
  assert.equal(host.root.querySelectorAll('.ir-filled').length, 6);
  game.destroy(); host.advance(6000); assert.equal(reports.length, 1);
});
