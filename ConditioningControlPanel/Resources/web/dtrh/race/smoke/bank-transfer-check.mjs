// Exercise the real HUD: token arrival, cancellation and reduced-motion policy.
import assert from 'node:assert/strict';
import { createRaceHud } from '../hud.js';
let clock = 0, serial = 0;
const jobs = new Map();
globalThis.setTimeout = (fn, ms = 0) => { const id = ++serial; jobs.set(id, { fn, at: clock + ms }); return id; };
globalThis.clearTimeout = id => jobs.delete(id);
globalThis.requestAnimationFrame = fn => setTimeout(() => fn(clock), 16);
globalThis.cancelAnimationFrame = clearTimeout;
globalThis.performance = { now: () => clock };
globalThis.matchMedia = () => ({ matches: false });
globalThis.window = { innerWidth: 1000, innerHeight: 800 };
globalThis.localStorage = { getItem: () => null };
function advance(ms) {
  const end = clock + ms;
  while (true) {
    const next = [...jobs].filter(([, j]) => j.at <= end).sort((a, b) => a[1].at - b[1].at)[0];
    if (!next) break;
    clock = next[1].at; jobs.delete(next[0]); next[1].fn();
  }
  clock = end;
}
class Element {
  constructor() {
    this.children = []; this.parentNode = null; this.className = ''; this.textContent = '';
    this.style = { setProperty(k, v) { this[k] = v; } }; this.listeners = {};
    this.classList = {
      contains: c => this.className.split(' ').includes(c),
      add: c => { if (!this.classList.contains(c)) this.className += ` ${c}`; },
      remove: c => { this.className = this.className.split(' ').filter(x => x !== c).join(' '); },
      toggle: (c, v) => v ? this.classList.add(c) : this.classList.remove(c),
    };
  }
  appendChild(n) { this.children.push(n); n.parentNode = this; return n; }
  append(...nodes) { nodes.forEach(n => this.appendChild(n)); }
  remove() { if (this.parentNode) this.parentNode.children = this.parentNode.children.filter(n => n !== this); this.parentNode = null; }
  addEventListener(k, fn) { (this.listeners[k] ||= []).push(fn); }
  dispatch(k, e) { for (const fn of this.listeners[k] || []) fn(e); }
  get firstChild() { return this.children[0]; }
  get offsetWidth() { return 100; }
  set innerHTML(_) { this.children = []; }
  getBoundingClientRect() {
    if (this.classList.contains('rh-bank')) return { left: 80, top: 150, width: 100, height: 20 };
    if (this.classList.contains('rh-score')) return { left: 10, top: 30, width: 80, height: 30 };
    return { left: 30, top: 20, width: 1000, height: 800 };
  }
}
globalThis.document = { createElement: () => new Element() };
function fixture(opts) {
  const root = new Element(), hud = createRaceHud(root, opts);
  const find = (cls, n = root) => [n, ...n.children.flatMap(c => all(c))].filter(e => e.classList.contains(cls));
  const all = n => [n, ...n.children.flatMap(all)];
  return { hud, find, bank: () => find('rh-bank')[0].textContent };
}
{
  const f = fixture(); f.hud.bankTransfer(280);
  const tokens = f.find('rh-token'); assert.equal(tokens.length, 7);
  assert.equal(f.bank(), 'kept 0', 'no balance rollup ahead of arrival');
  const x0 = parseFloat(tokens[0].style.left), y0 = parseFloat(tokens[0].style.top);
  assert.equal(parseFloat(tokens[0].style['--dx']) + x0, 100, 'flight targets bank centre in HUD coordinates');
  assert.equal(parseFloat(tokens[0].style['--dy']) + y0, 140);
  advance(600); assert.equal(f.bank(), 'kept 0');
  tokens[0].dispatch('transitionend', { propertyName: 'opacity' }); assert.equal(f.bank(), 'kept 0');
  tokens[0].dispatch('transitionend', { propertyName: 'transform' }); assert.equal(f.bank(), 'kept 40');
  tokens[0].dispatch('transitionend', { propertyName: 'transform' }); assert.equal(f.bank(), 'kept 40', 'arrival is idempotent');
  f.hud.setScore(17);
  for (const tk of tokens.slice(1)) tk.dispatch('transitionend', { propertyName: 'transform' });
  assert.equal(f.bank(), 'kept 280');
  assert.equal(f.find('rh-token').length, 0);
  assert.equal(f.find('rh-toast--bank').length, 1, 'one confirmation at the end');
  advance(500); assert.equal(f.find('rh-score')[0].textContent, '17', 'new earned score survives transfer completion');
  f.hud.dispose();
}
{
  const f = fixture(); f.hud.bankTransfer(100); const stale = f.find('rh-token');
  f.hud.bankTransfer(240); assert.equal(f.bank(), 'kept 100', 'overlap settles the preceding authoritative total');
  f.hud.settleBankTransfer(); assert.equal(f.bank(), 'kept 240');
  for (const tk of stale) tk.dispatch('transitionend', { propertyName: 'transform' });
  advance(1500); assert.equal(f.bank(), 'kept 240'); assert.equal(f.find('rh-token').length, 0);
  f.hud.bankTransfer(400); f.hud.setBank(0); advance(1500); assert.equal(f.bank(), 'kept 0', 'reset cannot be overwritten by late arrivals');
  f.hud.dispose();
}
{
  const f = fixture({ reducedMotion: true }); f.hud.bankTransfer(123);
  assert.equal(f.bank(), 'kept 123'); assert.equal(f.find('rh-token').length, 0);
  assert.equal(f.find('rh-bank')[0].classList.contains('is-pop'), false);
  f.hud.dispose();
}
{
  const f = fixture(); f.hud.bankTransfer(11); advance(850);
  assert.equal(f.bank(), 'kept 11', 'missing browser transition events still settle exactly');
  assert.equal(f.find('rh-token').length, 0); f.hud.dispose();
}
assert.equal(jobs.size, 0, 'dispose cancels every scheduled callback');
console.log('bank-transfer-check: all good');
