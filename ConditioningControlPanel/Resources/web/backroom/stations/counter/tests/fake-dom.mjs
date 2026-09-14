/* fake-dom.mjs - just enough DOM for station.js in node (createElement, children, dataset, classList, events).
 * station.js uses nothing else on purpose. install() returns { document, listeners, restore }. */

class El {
  constructor(tag) { this.tagName = tag.toUpperCase(); this.children = []; this.parent = null; this.dataset = {}; this.attrs = {};
    this.className = ''; this.hidden = false; this.disabled = false; this._text = ''; }
  get classList() {
    const set = () => new Set(this.className.split(/\s+/).filter(Boolean)), put = (s) => { this.className = [...s].join(' '); };
    return { add: (c) => { const s = set(); s.add(c); put(s); }, remove: (c) => { const s = set(); s.delete(c); put(s); },
      contains: (c) => set().has(c), toggle: (c, on) => { const s = set(); (on ?? !s.has(c)) ? s.add(c) : s.delete(c); put(s); } };
  }
  set textContent(v) { this._text = String(v); this.children = []; }
  get textContent() { return this._text + this.children.map((c) => c.textContent).join(''); }
  append(...nodes) { for (const n of nodes) { if (n.parent) n.remove(); n.parent = this; this.children.push(n); } }
  replaceChildren(...nodes) { for (const c of this.children) c.parent = null; this.children = []; this.append(...nodes); }
  remove() { if (this.parent) { this.parent.children = this.parent.children.filter((c) => c !== this); this.parent = null; } }
  setAttribute(k, v) { this.attrs[k] = String(v); }
  getAttribute(k) { return k in this.attrs ? this.attrs[k] : null; }
  focus() { globalThis.document.activeElement = this; }
  click() { if (!this.disabled && typeof this.onclick === 'function') this.onclick({ target: this }); }
  /** Every descendant (depth first) matching a class name. */
  all(cls) { const out = []; const walk = (e) => { for (const c of e.children) { if (c.className.split(' ').includes(cls)) out.push(c); walk(c); } }; walk(this); return out; }
  one(cls) { return this.all(cls)[0] || null; }
}

export function install() {
  const saved = { document: globalThis.document, addEventListener: globalThis.addEventListener, removeEventListener: globalThis.removeEventListener };
  const listeners = new Map();
  const document = { createElement: (t) => new El(t), head: new El('head'), body: new El('body'), activeElement: null };
  globalThis.document = document;
  globalThis.addEventListener = (type, fn) => { if (!listeners.has(type)) listeners.set(type, new Set()); listeners.get(type).add(fn); };
  globalThis.removeEventListener = (type, fn) => { if (listeners.has(type)) listeners.get(type).delete(fn); };
  const dispatch = (type, ev) => { for (const fn of [...(listeners.get(type) || [])]) fn(ev); };
  return { document, listeners, dispatch, El, restore() { Object.assign(globalThis, saved); } };
}
