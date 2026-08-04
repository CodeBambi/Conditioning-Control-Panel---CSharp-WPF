// Self-contained sanity pass over the VOICE-NOTE LIBRARY — ui/voice/noteStore.js,
// ui/screens/voice.js and the ui/emotes.js hook — driven against a minimal DOM
// stub. No microphone, no IndexedDB, no AudioContext, no network.
//
//   node Resources/web/goon/test/selftest-voice-ui.js
//
// The wire, the consents and the service are pinned by selftest-voice.js. THIS
// file is about the half a player touches, and five properties are worth more
// than the rest of it put together:
//
//   1. THE ACK GATE IS A GATE. The opt-in is inert until the acknowledgment
//      modal has been read: the toggle is disabled, the row opens the modal
//      instead, cancelling changes NOTHING, and only "I understand" is allowed
//      to write voiceNotesEnabled. A build where the switch works without the
//      modal is a build that records somebody's voice on an unread promise.
//   2. THE EMOTE HOOK CANNOT HURT THE EMOTE. A voice tier that is missing,
//      broken, throwing or rejecting must be indistinguishable from one with no
//      note pinned — the emote is already on the wire before any of it runs.
//   3. THE CAP IS A REFUSAL, NOT A THROW. The ninth note is answered with
//      {ok:false, reason:'full'}, which the screen turns into one calm line.
//   4. ONE NOTE PER EMOTE, AND ONE EMOTE PER NOTE. Re-pointing a note moves it;
//      it never ends up firing from two icons with only one of them on screen.
//   5. get() ANSWERS THE SHAPE THE SERVICE CONSUMES: {blob, durMs, emote}, with
//      the emote resolved by reverse lookup — voiceService.sendNote reads
//      exactly those three fields and nothing else.
//
// The recorder (ui/voice/recorder.js) is a SIBLING WAVE'S file and is
// deliberately never imported here; what is pinned instead is the adapter this
// screen talks to it through (normalizeRecording + the factory-name list).

import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

// ------------------------------------------------------------------ DOM stub
// Trimmed from selftest-assets.js — same shape, minus the bits only a
// virtualized grid needs (IntersectionObserver, styles, namespaced elements).

function installDom() {
  function makeNode(tagName) {
    const kids = [];
    const map = new Map();
    const classes = new Set();
    const attrs = new Map();
    const node = {
      tagName: String(tagName || 'div').toUpperCase(),
      nodeType: 1,
      children: kids,
      childNodes: kids,
      parentNode: null,
      isConnected: true,
      hidden: false,
      dataset: {},
      value: '',
      checked: false,
      disabled: false,
      textContent: '',
      title: '',
      get className() { return Array.from(classes).join(' '); },
      set className(v) { classes.clear(); String(v || '').split(/\s+/).filter(Boolean).forEach((c) => classes.add(c)); },
      classList: {
        add: (...c) => c.forEach((x) => classes.add(x)),
        remove: (...c) => c.forEach((x) => classes.delete(x)),
        toggle: (c, on) => (on ? classes.add(c) : classes.delete(c)),
        contains: (c) => classes.has(c),
      },
      appendChild(child) { if (child) { child.parentNode = node; kids.push(child); } return child; },
      append(...c) { c.forEach((x) => node.appendChild(x)); },
      prepend(child) { if (child) { child.parentNode = node; kids.unshift(child); } return child; },
      removeChild(child) { const i = kids.indexOf(child); if (i >= 0) kids.splice(i, 1); return child; },
      remove() { if (node.parentNode) node.parentNode.removeChild(node); node.parentNode = null; node.isConnected = false; },
      replaceChildren(...c) { kids.length = 0; c.forEach((x) => node.appendChild(x)); },
      replaceChild(next, old) { const i = kids.indexOf(old); if (i >= 0) { kids[i] = next; next.parentNode = node; } return old; },
      insertBefore(next, ref) {
        const i = kids.indexOf(ref);
        if (i >= 0) kids.splice(i, 0, next); else kids.push(next);
        next.parentNode = node;
        return next;
      },
      contains(other) {
        if (other === node) return true;
        for (const k of kids) if (k && typeof k.contains === 'function' && k.contains(other)) return true;
        return false;
      },
      setAttribute(k, v) { attrs.set(k, String(v)); if (k === 'class') node.className = String(v); },
      getAttribute(k) { return attrs.has(k) ? attrs.get(k) : null; },
      removeAttribute(k) { attrs.delete(k); },
      hasAttribute(k) { return attrs.has(k); },
      addEventListener(type, fn) { if (!map.has(type)) map.set(type, new Set()); map.get(type).add(fn); },
      removeEventListener(type, fn) { const s = map.get(type); if (s) s.delete(fn); },
      dispatchEvent(evt) {
        const s = map.get(evt && evt.type);
        if (s) for (const fn of Array.from(s)) { try { fn(evt); } catch (_e) { /* ignore */ } }
        return true;
      },
      focus() {}, blur() {},
      _listeners: map,
      _classes: classes,
      _attrs: attrs,
    };
    return node;
  }

  const doc = makeNode('#document');
  doc.documentElement = makeNode('html');
  doc.body = makeNode('body');
  doc.activeElement = null;
  const byId = new Map();
  for (const id of ['gg-modal', 'gg-drawer', 'gg-toasts', 'scr-voice', 'scr-title']) {
    const n = makeNode('div');
    n.id = id;
    n.hidden = true;
    byId.set(id, n);
    doc.body.appendChild(n);
  }
  doc.createElement = (tag) => makeNode(tag);
  doc.createElementNS = (_ns, tag) => makeNode(tag);
  doc.createTextNode = (t) => { const n = makeNode('#text'); n.textContent = String(t); return n; };
  doc.getElementById = (id) => byId.get(id) || null;

  const win = makeNode('window');
  globalThis.document = doc;
  globalThis.window = win;
  globalThis.requestAnimationFrame = (fn) => setTimeout(() => { try { fn(Date.now()); } catch (_e) { /* ignore */ } }, 8);
  globalThis.cancelAnimationFrame = (id) => clearTimeout(id);
  return { doc, byId, makeNode };
}

const dom = installDom();

// ------------------------------------------------------------------ harness

const HERE = path.dirname(fileURLToPath(import.meta.url));
const ROOT = path.join(HERE, '..');
// LF-normalized: the worktree is CRLF (core.autocrlf) and every source pin below
// is written against \n.
const read = (rel) => fs.readFileSync(path.join(ROOT, rel), 'utf8').replace(/\r\n/g, '\n');

let failures = 0;
let n = 0;
function ok(cond, label, extra = '') {
  n++;
  if (!cond) { failures++; console.error(`  FAIL ${label} ${extra}`); }
}
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
const quiet = { info() {}, warn() {}, error() {}, debug() {}, log() {} };

function findAll(root, className) {
  const out = [];
  (function walk(node) {
    if (!node) return;
    if (node._classes && node._classes.has(className)) out.push(node);
    for (const kid of node.children || []) walk(kid);
  })(root);
  return out;
}
const findOne = (root, className) => findAll(root, className)[0] || null;
function findTag(root, tag) {
  const out = [];
  (function walk(node) {
    if (!node) return;
    if (node.tagName === String(tag).toUpperCase()) out.push(node);
    for (const kid of node.children || []) walk(kid);
  })(root);
  return out;
}
const click = (node) => node.dispatchEvent({ type: 'click', target: node, preventDefault() {}, stopPropagation() {} });

/** Strip // and block comments so "is this code or a note about it?" is answerable. */
function stripComments(src) {
  return String(src)
    .replace(/\/\*[\s\S]*?\*\//g, '')
    .replace(/(^|[^:])\/\/[^\n]*/g, '$1');
}

/** A blob-shaped object. The store only ever measures `.size`. */
const fakeBlob = (size, tag = '') => ({ size, _tag: tag, arrayBuffer: () => Promise.resolve(new ArrayBuffer(size)) });

// ------------------------------------------------------------------ imports

import {
  createNoteStore, noteIdForEmote, emoteForNote, linkEmote, unlinkNote, pruneMap,
  VOICE_DB_NAME, VOICE_STORE_NAME,
} from '../ui/voice/noteStore.js';
import { VN_MAX_NOTES, VN_MAX_BYTES } from '../ui/voice/voiceService.js';
import { createPrefs, PREF_DEFAULTS } from '../ui/prefs.js';
import { mountEmotes, setVoiceProvider, fireVoiceForEmote, EMOTE_ICONS } from '../ui/emotes.js';
import * as voiceScreen from '../ui/screens/voice.js';
import { S } from '../ui/strings.js';
import { SCREEN_IDS } from '../ui/router.js';

// ============================================================ 1. the map maths
{
  const map = { '😏': 'n1', '💦': 'n2' };
  ok(noteIdForEmote(map, '😏') === 'n1', 'noteIdForEmote finds the bound note');
  ok(noteIdForEmote(map, '🔥') === '', 'and answers "" for a free emote');
  ok(emoteForNote(map, 'n2') === '💦', 'emoteForNote is the reverse lookup a row needs');
  ok(emoteForNote(map, 'nope') === '', 'and "" for a note with no emote');

  // Hostile / corrupt shapes must never reach a caller as anything but {}.
  ok(noteIdForEmote(null, '😏') === '' && noteIdForEmote(['x'], '😏') === '',
    'a null or array map answers "" rather than throwing');
  ok(noteIdForEmote({ '😏': 42 }, '😏') === '', 'a non-string value is not a note id');

  // ONE NOTE PER EMOTE: the re-map.
  const moved = linkEmote(map, '😏', 'n9');
  ok(moved.map['😏'] === 'n9', 'linking an occupied emote re-points it');
  ok(moved.moved === 'n1', 'and reports the note that lost it (S.voice.linkMoved)');
  ok(moved.changed === true, 'and says something changed');

  // ONE EMOTE PER NOTE: the note's old emote is released in the same write.
  const second = linkEmote(moved.map, '🔥', 'n9');
  ok(second.map['🔥'] === 'n9' && second.map['😏'] === undefined,
    'binding a note to a second emote MOVES it — a note never fires from two icons');
  ok(second.moved === '', 'and nothing was displaced, because 🔥 was free');

  const same = linkEmote(second.map, '🔥', 'n9');
  ok(same.changed === false, 're-picking the emote a note already has is a no-op (no pref write)');

  const un = unlinkNote(second.map, 'n9');
  ok(un.changed === true && un.map['🔥'] === undefined, 'unlinkNote clears every binding for a note');
  ok(unlinkNote(un.map, 'n9').changed === false, 'and is idempotent');

  const pruned = pruneMap({ a: 'live', b: 'dead' }, ['live']);
  ok(pruned.changed === true && pruned.map.a === 'live' && pruned.map.b === undefined,
    'pruneMap drops bindings whose note no longer exists');

  // The map is COPIED out, never aliased: a caller mutating what it got back
  // must not be able to edit the store's idea of the world.
  const src = { '😏': 'n1' };
  const out = linkEmote(src, '💦', 'n2');
  ok(src['💦'] === undefined, 'linkEmote returns a NEW map and leaves the input alone');
  ok(out.map['😏'] === 'n1', 'carrying the other bindings forward');
}

// ============================================== 2. the store (memory backend)
{
  // `idb: null` FORCES the fallback backend — which is also what node gets on
  // its own, so this is the same path a private-mode browser takes.
  const prefs = createPrefs();
  const store = createNoteStore({ prefs, logger: quiet, idb: null });

  ok(await store.ready() === 'memory', 'no indexedDB -> the memory backend, not an exception');
  ok(store.backend === 'memory', 'and it says so');
  ok(store.max === VN_MAX_NOTES, 'the cap is voiceService.VN_MAX_NOTES, never a second copy of 8');
  ok(VOICE_DB_NAME === 'gg-voice' && VOICE_STORE_NAME === 'notes', 'the contract names are pinned');

  ok((await store.list()).length === 0, 'a fresh library is empty');

  const a = await store.add(fakeBlob(1200, 'a'), { durMs: 3400, name: 'Note 1' });
  ok(a.ok && a.reason === 'added', 'add() takes a blob');
  ok(!!a.note && typeof a.note.id === 'string' && a.note.id.length > 3, 'and answers with the new note id');
  ok(a.note.durMs === 3400 && a.note.name === 'Note 1', 'carrying the duration and the name');

  const got = await store.get(a.note.id);
  ok(!!got && got.blob && got.blob._tag === 'a', 'get() returns the BLOB — the thing the service sends');
  ok(got.durMs === 3400, 'and durMs');
  ok(got.emote === '', 'and an emote of "" while nothing is bound');
  ok(await store.get('no-such-note') === null, 'an unknown id is null, not a throw');

  // THE SHAPE voiceService.sendNote() CONSUMES.
  for (const key of ['blob', 'durMs', 'emote']) {
    ok(Object.prototype.hasOwnProperty.call(got, key), 'get() answers the {blob, durMs, emote} shape: ' + key);
  }

  // ---- the emote association, through the store (one writer for the pref)
  const link = store.link(EMOTE_ICONS[0], a.note.id);
  ok(link.ok === true, 'link() writes the binding');
  ok(prefs.get('voiceEmoteMap')[EMOTE_ICONS[0]] === a.note.id, 'into prefs.voiceEmoteMap, keyed by emote');
  ok(store.noteFor(EMOTE_ICONS[0]) === a.note.id, 'noteFor() is the emote hook\'s lookup');
  ok(store.emoteFor(a.note.id) === EMOTE_ICONS[0], 'emoteFor() is the note row\'s lookup');
  ok((await store.get(a.note.id)).emote === EMOTE_ICONS[0], 'and get() now resolves the emote by reverse lookup');

  const b = await store.add(fakeBlob(900, 'b'), { durMs: 1500, name: 'Note 2' });
  const remap = store.link(EMOTE_ICONS[0], b.note.id);
  ok(remap.moved === a.note.id, 're-mapping an emote reports the note it was taken from');
  ok(store.emoteFor(a.note.id) === '', 'and the old note really lost it');

  store.unlink(b.note.id);
  ok(store.noteFor(EMOTE_ICONS[0]) === '', 'unlink() puts the note back "on its own"');

  // ---- the cap, as a refusal
  while ((await store.count()) < VN_MAX_NOTES) {
    const r = await store.add(fakeBlob(500), { durMs: 1000 });
    ok(r.ok, 'filling the library up to the cap');
  }
  const over = await store.add(fakeBlob(500), { durMs: 1000 });
  ok(over.ok === false && over.reason === 'full', 'the ninth note is REFUSED, cleanly, with reason "full"');
  ok(over.note === null, 'and no note comes back');
  ok((await store.count()) === VN_MAX_NOTES, 'the library is still exactly at the cap');

  // ---- the other refusals
  const empty = await store.add(null, { durMs: 100 });
  ok(empty.ok === false && empty.reason === 'empty', 'a null blob is refused, not stored');
  await store.remove((await store.list())[0].id);
  const big = await store.add(fakeBlob(VN_MAX_BYTES + 1), { durMs: 9000 });
  ok(big.ok === false && big.reason === 'too-big',
    'a blob past VN_MAX_BYTES is refused at RECORD time — a note that could never be sent is not a note');

  // ---- delete takes the binding with it
  const list = await store.list();
  const victim = list[0];
  store.link(EMOTE_ICONS[1], victim.id);
  ok(store.noteFor(EMOTE_ICONS[1]) === victim.id, 'a note bound before deletion');
  const del = await store.remove(victim.id);
  ok(del.ok === true, 'remove() takes it');
  ok(store.noteFor(EMOTE_ICONS[1]) === '',
    'and the emote binding goes WITH it — no map entry may point at a note that is gone');
  ok((await store.remove(victim.id)).ok === false, 'removing it twice is a clean "missing", not a throw');

  // ---- rename
  const keep = (await store.list())[0];
  ok((await store.rename(keep.id, 'ready when you are')).ok, 'rename() works');
  ok((await store.list())[0].name === 'ready when you are', 'and sticks');

  // ---- list() carries the binding, so the screen paints in one read
  store.link(EMOTE_ICONS[2], keep.id);
  const rows = await store.list();
  ok(rows.find((r) => r.id === keep.id).emote === EMOTE_ICONS[2], 'list() rows carry their emote');
  ok(rows.every((r) => !('blob' in r)), 'and NOT the blobs — a list repaint must not copy audio');

  // ---- prune
  prefs.set('voiceEmoteMap', Object.assign({}, prefs.get('voiceEmoteMap'), { '👀': 'ghost-note' }));
  ok(await store.prune() === true, 'prune() drops a binding whose note does not exist');
  ok(store.noteFor('👀') === '', 'and it is really gone');

  store.dispose();
  ok((await store.list()).length === 0, 'dispose() empties the mirror');
}

// ================================================= 3. the ack gate, on screen
function fakeSheets(answer = 'go') {
  const calls = [];
  return {
    calls,
    setAnswer(v) { answer = v; },
    open(o) { calls.push(o); return Promise.resolve(answer); },
    openNode() { return Promise.resolve(null); },
    get isOpen() { return false; },
  };
}
function fakeToasts() {
  const shown = [];
  return { shown, show(text, o) { shown.push({ text, kind: (o && o.kind) || 'info' }); return null; } };
}

async function mountVoiceScreen({ prefs, sheets, toasts, notes = null, match = null, audio = null }) {
  const container = dom.doc.getElementById('scr-voice');
  container.replaceChildren();
  const handle = voiceScreen.mount(container, {
    actions: { goTitle() {} },
    audio, prefs, sheets, toasts, logger: quiet,
    notes,
    getMatch: () => match,
  });
  await sleep(5);
  return { container, handle };
}

{
  const prefs = createPrefs();
  const sheets = fakeSheets('go');
  const toasts = fakeToasts();
  const store = createNoteStore({ prefs, logger: quiet, idb: null });
  const { container, handle } = await mountVoiceScreen({ prefs, sheets, toasts, notes: store });

  ok(!!findOne(container, 'gg-voicelib'), 'the screen mounts a .gg-voicelib card');
  ok(!findOne(container, 'gg-voice'), 'and NEVER wears .gg-voice — that is the HUD mic strip, absolute in the corner');
  const row = findOne(container, 'gg-voice-optin');
  ok(!!row, 'with the opt-in row');
  const input = findTag(row, 'input')[0];
  ok(!!input, 'and a real checkbox');

  // 1. INERT UNTIL ACKED.
  ok(input.disabled === true, 'THE GATE: the toggle is DISABLED until the ack modal has been read');
  ok(row._classes.has('is-disabled'), 'and the row says so visually');
  const sub = findOne(container, 'gg-voice-optsub');
  ok(sub && sub.textContent === S.voice.toggleLocked, 'the sub-line explains why, from strings');
  ok(prefs.get('voiceNotesEnabled') === false && prefs.get('voiceAckSeen') === false,
    'and nothing has been written to prefs by simply looking at the screen');

  // 2. THE BELT-AND-BRACES PATH: a host that ignores `disabled` still cannot
  //    switch the microphone on without the modal.
  input.checked = true;
  input.dispatchEvent({ type: 'change', target: input });
  // SYNCHRONOUSLY, before the modal can resolve: the switch is put back and
  // nothing is written. Whatever happens next is the MODAL's decision, not the
  // click's — which is the entire point of a gate.
  ok(input.checked === false, 'a change event on the un-acked toggle is REVERTED on the spot');
  ok(prefs.get('voiceNotesEnabled') === false, 'and writes nothing before the modal is answered');
  ok(sheets.calls.length >= 1, 'it opens the acknowledgment modal instead');
  await sleep(5);

  const ack = sheets.calls[sheets.calls.length - 1];
  ok(ack.headline === S.voice.ack.headline, 'the modal is the ack sheet, from strings');
  ok(ack.line === S.voice.ack.line, 'carrying the first paragraph');
  ok((ack.actions || []).some((a) => a.id === 'go' && a.label === S.voice.ack.go),
    'with an explicit "I understand"');
  ok((ack.actions || []).some((a) => a.id === 'cancel'), 'and a way out');
  ok(prefs.get('voiceAckSeen') === true, 'answering "I understand" records the acknowledgment');
  ok(prefs.get('voiceNotesEnabled') === true, 'and the switch the player reached for is now on');
  ok(input.disabled === false, 'the toggle is live from here on');

  handle.unmount();
  store.dispose();
}

{
  // CANCEL CHANGES NOTHING. The most important half of a consent modal.
  const prefs = createPrefs();
  const sheets = fakeSheets('cancel');
  const store = createNoteStore({ prefs, logger: quiet, idb: null });
  const { container, handle } = await mountVoiceScreen({ prefs, sheets, toasts: fakeToasts(), notes: store });

  const row = findOne(container, 'gg-voice-optin');
  click(row);
  await sleep(5);
  ok(sheets.calls.length === 1, 'clicking the ROW opens the modal (the disabled input cannot)');
  ok(prefs.get('voiceAckSeen') === false, 'cancelling does NOT record an acknowledgment');
  ok(prefs.get('voiceNotesEnabled') === false, 'and does not switch anything on');
  ok(findTag(row, 'input')[0].disabled === true, 'the toggle is still inert');

  handle.unmount();
  store.dispose();
}

{
  // ONCE ACKED: the toggle is an ordinary switch, and it mirrors onto a match.
  const prefs = createPrefs();
  prefs.set('voiceAckSeen', true);
  const declared = [];
  const match = { setLocalVoiceNotes(on) { declared.push(!!on); return true; } };
  const store = createNoteStore({ prefs, logger: quiet, idb: null });
  const { container, handle } = await mountVoiceScreen({
    prefs, sheets: fakeSheets(), toasts: fakeToasts(), notes: store, match,
  });

  const input = findTag(findOne(container, 'gg-voice-optin'), 'input')[0];
  ok(input.disabled === false, 'an acked player gets a live toggle on mount');
  input.checked = true;
  input.dispatchEvent({ type: 'change', target: input });
  await sleep(5);
  ok(prefs.get('voiceNotesEnabled') === true, 'flipping it writes the pref');
  ok(declared[declared.length - 1] === true,
    'AND mirrors the declaration onto the match — the pref is local, the consent frame is what they see');

  input.checked = false;
  input.dispatchEvent({ type: 'change', target: input });
  await sleep(5);
  ok(prefs.get('voiceNotesEnabled') === false && declared[declared.length - 1] === false,
    'and switching it off does both again');

  handle.unmount();
  store.dispose();
}

// =================================================== 4. the list and the picker
{
  const prefs = createPrefs();
  prefs.set('voiceAckSeen', true);
  const store = createNoteStore({ prefs, logger: quiet, idb: null });
  const played = [];
  const audio = {
    playVoiceNote(src, o) { played.push(src); setTimeout(() => { try { o && o.onEnd && o.onEnd('ended'); } catch (_e) { /* ignore */ } }, 1); return Promise.resolve('played'); },
    stopVoice() { return true; },
    sfx() {}, music() {}, stopMusic() {},
  };
  const toasts = fakeToasts();

  let { container, handle } = await mountVoiceScreen({ prefs, sheets: fakeSheets(), toasts, notes: store, audio });
  ok(findOne(container, 'gg-voice-empty') && !findOne(container, 'gg-voice-empty').hidden,
    'an empty library says so instead of showing nothing');
  handle.unmount();

  const one = await store.add(fakeBlob(800, 'one'), { durMs: 2500, name: S.voice.noteName(1) });
  const two = await store.add(fakeBlob(700, 'two'), { durMs: 4000, name: S.voice.noteName(2) });
  ({ container, handle } = await mountVoiceScreen({ prefs, sheets: fakeSheets(), toasts, notes: store, audio }));

  const rows = findAll(container, 'gg-voice-note');
  ok(rows.length === 2, 'both notes render as rows', String(rows.length));
  ok(findOne(container, 'gg-voice-empty').hidden === true, 'and the empty line is gone');
  const names = findAll(container, 'gg-voice-note-name').map((x) => x.textContent);
  ok(names.includes(S.voice.noteName(1)) && names.includes(S.voice.noteName(2)),
    'auto-named from strings ("Note 1", "Note 2")');
  const lens = findAll(container, 'gg-voice-note-len').map((x) => x.textContent);
  ok(lens.includes(S.voice.noteLength(2500)), 'each row carries its duration');

  // The picker offers the WHOLE emote set plus "on its own".
  const picker = findOne(rows[0], 'gg-voice-picker');
  const chips = findAll(picker, 'gg-voice-emote');
  ok(chips.length === EMOTE_ICONS.length + 1,
    'the picker offers every emote in ui/emotes.js plus "on its own"', String(chips.length));
  ok(chips[0]._classes.has('is-none') && chips[0].textContent === S.voice.linkNone,
    'with "on its own" first, so unbinding is as reachable as binding');
  ok(chips[0].getAttribute('aria-pressed') === 'true', 'and pressed while the note has no emote');

  // Bind → the pref is written and the row repaints.
  click(chips[1]);
  await sleep(5);
  ok(prefs.get('voiceEmoteMap')[EMOTE_ICONS[0]] === one.note.id, 'picking an emote writes voiceEmoteMap');
  const linkLine = findOne(findAll(container, 'gg-voice-note')[0], 'gg-voice-note-link');
  ok(linkLine.textContent === S.voice.linkedTo(EMOTE_ICONS[0]), 'and the row says which emote carries it');

  // Re-map to the OTHER note → the toast that warns the first row changed.
  const rows2 = findAll(container, 'gg-voice-note');
  click(findAll(findOne(rows2[1], 'gg-voice-picker'), 'gg-voice-emote')[1]);
  await sleep(5);
  ok(prefs.get('voiceEmoteMap')[EMOTE_ICONS[0]] === two.note.id, 'the emote moves to the second note');
  ok(toasts.shown.some((t) => t.text === S.voice.linkMoved(EMOTE_ICONS[0])),
    'and the player is told the other note lost it');

  // Un-associate.
  const rows3 = findAll(container, 'gg-voice-note');
  click(findAll(findOne(rows3[1], 'gg-voice-picker'), 'gg-voice-emote')[0]);
  await sleep(5);
  ok(prefs.get('voiceEmoteMap')[EMOTE_ICONS[0]] === undefined, '"on its own" un-associates');

  // Preview goes through the VOICE BUS, not an <audio> element, so the voice
  // slider and the master apply exactly once.
  const playBtn = findAll(rows3[0], 'gg-voice-note-btn')[0];
  click(playBtn);
  await sleep(10);
  ok(played.length === 1 && played[0]._tag === 'one', 'play sends the blob to audio.playVoiceNote');

  handle.unmount();
  store.dispose();
}

// ============================================ 4b. the record path, through the seam
//
// Driven with a FAKE recorder injected through ctx.recorderFactory. The real
// ui/voice/recorder.js is a sibling wave's file and reaches a microphone; what
// is proved here is that this screen speaks its documented shape, including the
// case a naive implementation gets wrong — the recorder capping ITSELF.

function fakeRecorder({ start = { ok: true, reason: 'recording' }, stop = null } = {}) {
  const seen = { started: 0, stopped: 0, disposed: 0, capped: new Set() };
  return {
    seen,
    start() { seen.started++; return Promise.resolve(start); },
    stop() {
      seen.stopped++;
      return Promise.resolve(stop || { ok: true, reason: 'stopped', blob: fakeBlob(1000, 'rec'), durMs: 3000, mime: 'audio/webm' });
    },
    cancel() { return Promise.resolve({ ok: false, reason: 'cancelled' }); },
    onCapped(fn) { seen.capped.add(fn); return () => seen.capped.delete(fn); },
    dispose() { seen.disposed++; },
  };
}

async function mountWithRecorder(prefs, store, rec, toasts) {
  const container = dom.doc.getElementById('scr-voice');
  container.replaceChildren();
  const handle = voiceScreen.mount(container, {
    actions: { goTitle() {} },
    audio: { sfx() {}, music() {}, stopMusic() {}, stopVoice() {} },
    prefs, sheets: fakeSheets(), toasts, logger: quiet, notes: store,
    getMatch: () => null,
    recorderFactory: () => rec,
  });
  await sleep(5);
  return { container, handle };
}

{
  // A whole recording, from press to row.
  const prefs = createPrefs();
  prefs.set('voiceAckSeen', true);
  const store = createNoteStore({ prefs, logger: quiet, idb: null });
  const toasts = fakeToasts();
  const rec = fakeRecorder();
  const { container, handle } = await mountWithRecorder(prefs, store, rec, toasts);

  const btn = findOne(container, 'gg-voice-record');
  ok(!!btn && btn.textContent === S.voice.record, 'the record button reads from strings');
  click(btn);
  await sleep(10);
  ok(rec.seen.started === 1, 'pressing it starts the recorder');
  ok(btn.textContent === S.voice.recordStop, 'and the button becomes Stop while it runs');
  ok(btn._classes.has('is-recording'), 'with the live-mic class the pulse hangs off');

  click(btn);
  await sleep(15);
  ok(rec.seen.stopped === 1, 'pressing it again stops the recorder');
  ok(rec.seen.disposed === 1, 'and DISPOSES it — the microphone is released, never left hot');
  ok((await store.count()) === 1, 'the note was stored');
  const row = (await store.list())[0];
  ok(row.durMs === 3000, 'with the duration the recorder measured');
  ok(row.name === S.voice.noteName(1), 'auto-named "Note 1"');
  ok(findAll(container, 'gg-voice-note').length === 1, 'and the list repainted');
  ok(btn.textContent === S.voice.record, 'the button went back to Record');

  handle.unmount();
  store.dispose();
}

{
  // THE CAP RACE: the recorder stops ITSELF at ten seconds. A screen that only
  // knows how to call stop() would get {ok:false, reason:'idle'} here and throw
  // the note away with a "that one did not record" — this is the pin against it.
  const prefs = createPrefs();
  prefs.set('voiceAckSeen', true);
  const store = createNoteStore({ prefs, logger: quiet, idb: null });
  const toasts = fakeToasts();
  const rec = fakeRecorder();
  const { container, handle } = await mountWithRecorder(prefs, store, rec, toasts);

  click(findOne(container, 'gg-voice-record'));
  await sleep(10);
  ok(rec.seen.capped.size === 1, 'the screen subscribes to the recorder\'s own ceiling');
  for (const fn of rec.seen.capped) fn({ ok: true, reason: 'stopped', blob: fakeBlob(4000, 'cap'), durMs: 10000, capped: true });
  await sleep(15);
  ok((await store.count()) === 1, 'a self-capped recording is KEPT, not lost to a late stop()');
  ok(rec.seen.stopped === 0, 'and stop() was never called after the fact');
  const note = findOne(container, 'gg-voice-recnote');
  ok(note && note.hidden === false && note.textContent === S.voice.recordCapped,
    'the player is told ten seconds was the lot');

  handle.unmount();
  store.dispose();
}

{
  // A refused microphone is an ANSWER, not an error: one line, nothing stored.
  const prefs = createPrefs();
  prefs.set('voiceAckSeen', true);
  const store = createNoteStore({ prefs, logger: quiet, idb: null });
  const toasts = fakeToasts();
  const rec = fakeRecorder({ start: { ok: false, reason: 'denied' } });
  const { container, handle } = await mountWithRecorder(prefs, store, rec, toasts);

  click(findOne(container, 'gg-voice-record'));
  await sleep(10);
  ok(toasts.shown.some((t) => t.text === S.voice.micDenied), 'a denied mic says so, from strings');
  ok((await store.count()) === 0, 'and nothing is stored');
  ok(findOne(container, 'gg-voice-record').textContent === S.voice.record,
    'the button never entered the recording state');

  handle.unmount();
  store.dispose();
}

{
  // A recorder that produces nothing usable.
  const prefs = createPrefs();
  prefs.set('voiceAckSeen', true);
  const store = createNoteStore({ prefs, logger: quiet, idb: null });
  const toasts = fakeToasts();
  const rec = fakeRecorder({ stop: { ok: false, reason: 'empty', blob: null, durMs: 0 } });
  const { container, handle } = await mountWithRecorder(prefs, store, rec, toasts);
  const btn = findOne(container, 'gg-voice-record');
  click(btn); await sleep(10);
  click(btn); await sleep(15);
  ok(toasts.shown.some((t) => t.text === S.voice.sendFailed), 'a silent recording is reported, not stored');
  ok((await store.count()) === 0, 'and no empty row appears');
  ok(rec.seen.disposed === 1, 'the mic is still released');
  handle.unmount();
  store.dispose();
}

{
  // A full library refuses BEFORE a microphone is ever opened.
  const prefs = createPrefs();
  prefs.set('voiceAckSeen', true);
  const store = createNoteStore({ prefs, logger: quiet, idb: null });
  for (let i = 0; i < VN_MAX_NOTES; i++) await store.add(fakeBlob(300), { durMs: 1000, name: S.voice.noteName(i + 1) });
  const rec = fakeRecorder();
  const { container, handle } = await mountWithRecorder(prefs, store, rec, fakeToasts());

  const btn = findOne(container, 'gg-voice-record');
  ok(btn.disabled === true, 'a full library disables the record button');
  const note = findOne(container, 'gg-voice-recnote');
  ok(note && note.hidden === false && note.textContent === S.voice.full(VN_MAX_NOTES),
    'and says which number it is full at, as a fact rather than a refusal');
  ok(rec.seen.started === 0, 'no microphone was opened to find that out');

  handle.unmount();
  store.dispose();
}

// ============================================ 5. the emote hook, fire-and-forget
function fakeMatch() {
  const sent = [];
  return { sent, sendEmote(text, icon) { sent.push({ text, icon }); return true; } };
}

function mountSheet(provider) {
  const host = dom.doc.createElement('div');
  const match = fakeMatch();
  const handle = mountEmotes({ host, match, audio: null, voiceProvider: provider });
  return { host, match, handle };
}

{
  // A note pinned to the emote goes out AFTER it, with the emote named.
  const calls = [];
  const voice = { sendNote(id, o) { calls.push({ id, o }); return Promise.resolve({ ok: true, reason: 'sent' }); } };
  const { match, handle } = mountSheet((key) => (key === EMOTE_ICONS[0] ? { voice, noteId: 'note-a' } : null));

  handle.send('', EMOTE_ICONS[0]);
  ok(match.sent.length === 1, 'the emote itself went out');
  ok(calls.length === 1 && calls[0].id === 'note-a', 'and the pinned note was fired');
  ok(calls[0].o && calls[0].o.emote === EMOTE_ICONS[0],
    'named with the emote, so the receiver can anchor it on the bubble');
  handle.unmount();
}

{
  // No binding for this emote: nothing is sent, and the emote is unaffected.
  const calls = [];
  const voice = { sendNote(id) { calls.push(id); return Promise.resolve({ ok: true }); } };
  const { match, handle } = mountSheet((key) => (key === EMOTE_ICONS[0] ? { voice, noteId: 'a' } : null));
  handle.send('', EMOTE_ICONS[1]);
  ok(match.sent.length === 1 && calls.length === 0, 'an emote with no note pinned sends only the emote');
  handle.unmount();
}

{
  // THE PROPERTY THAT MATTERS: a broken voice tier cannot break an emote.
  const thrower = { sendNote() { throw new Error('boom'); } };
  const { match, handle } = mountSheet(() => ({ voice: thrower, noteId: 'a' }));
  let threw = false;
  try { handle.send('', EMOTE_ICONS[0]); } catch (_e) { threw = true; }
  ok(threw === false, 'a voice service that THROWS does not throw into the emote path');
  ok(match.sent.length === 1, 'and the emote still went out');
  handle.unmount();
}

{
  // ...and a provider that throws, and one that answers rubbish.
  const { match, handle } = mountSheet(() => { throw new Error('provider is broken'); });
  let threw = false;
  try { handle.send('', EMOTE_ICONS[0]); } catch (_e) { threw = true; }
  ok(threw === false && match.sent.length === 1, 'a PROVIDER that throws is survivable too');
  handle.unmount();

  const junk = mountSheet(() => ({ voice: {}, noteId: 'a' }));
  junk.handle.send('', EMOTE_ICONS[0]);
  ok(junk.match.sent.length === 1, 'a voice object with no sendNote is simply ignored');
  junk.handle.unmount();
}

{
  // A rejected send must not surface as an unhandled rejection out of a click.
  let unhandled = 0;
  const onUnhandled = () => { unhandled++; };
  process.on('unhandledRejection', onUnhandled);
  const voice = { sendNote() { return Promise.reject(new Error('lane died')); } };
  const { match, handle } = mountSheet(() => ({ voice, noteId: 'a' }));
  handle.send('', EMOTE_ICONS[0]);
  await sleep(20);
  ok(unhandled === 0, 'a REJECTED send is caught inside the hook, not left to the page');
  ok(match.sent.length === 1, 'and, again, the emote was never affected');
  handle.unmount();
  process.off('unhandledRejection', onUnhandled);
}

{
  // The module-level provider (what boot.js sets) and its precedence.
  const modCalls = [];
  setVoiceProvider((key) => ({ voice: { sendNote(id) { modCalls.push([key, id]); return Promise.resolve({ ok: true }); } }, noteId: 'mod' }));
  const { match, handle } = mountSheet(null);
  handle.send('', EMOTE_ICONS[2]);
  ok(modCalls.length === 1 && modCalls[0][1] === 'mod',
    'the module-level provider boot.js sets is enough — the HUD does not have to thread a dep');
  ok(match.sent.length === 1, 'emote unaffected');

  const instCalls = [];
  const inst = mountSheet(() => ({ voice: { sendNote(id) { instCalls.push(id); return Promise.resolve({ ok: true }); } }, noteId: 'inst' }));
  inst.handle.send('', EMOTE_ICONS[2]);
  ok(instCalls.length === 1 && modCalls.length === 1, 'an instance provider WINS over the module one');
  handle.unmount();
  inst.handle.unmount();

  setVoiceProvider(null);
  ok(fireVoiceForEmote(EMOTE_ICONS[2]) === false, 'and unhooking it makes the hook a no-op');
}

{
  // The hook keys on the ICON where there is one, and falls back to the LINE —
  // so a future picker that offers the sentences needs no change in emotes.js.
  const seen = [];
  const voice = { sendNote(id, o) { seen.push(o.emote); return Promise.resolve({ ok: true }); } };
  const { handle } = mountSheet((key) => ({ voice, noteId: 'x-' + key }));
  handle.send('gg', '');
  ok(seen[seen.length - 1] === 'gg', 'a preset LINE can be a key');
  handle.unmount();
}

// ================================================= 6. the recorder seam adapter
{
  const { normalizeRecording, RECORDER_FACTORIES } = voiceScreen;
  ok(Array.isArray(RECORDER_FACTORIES) && RECORDER_FACTORIES.includes('default'),
    'the seam accepts a default export as well as named factories');

  const blobby = { size: 4000 };
  ok(normalizeRecording({ blob: blobby, durMs: 5000 }).ok === true, 'the plain {blob, durMs} shape is accepted');
  ok(normalizeRecording({ blob: blobby, durMs: 5000 }).durMs === 5000, 'and its duration read');
  ok(normalizeRecording(blobby).ok === true, 'a BARE blob is accepted too');
  ok(normalizeRecording({ ok: true, blob: blobby, durationMs: 900 }).durMs === 900, 'durationMs is read as an alias');
  ok(normalizeRecording({ ok: false, reason: 'denied' }).ok === false, 'a refusal stays a refusal');
  ok(normalizeRecording({ ok: false, reason: 'denied' }).reason === 'denied', 'with its reason intact');
  ok(normalizeRecording(null).ok === false && normalizeRecording(null).reason === 'empty',
    'and nothing at all is "empty", never an exception');
  ok(normalizeRecording({ durMs: 900 }).ok === false, 'a result with no blob is a failure, not a zero-byte note');
}

// ============================================================ 7. reachability
{
  const html = read('index.html');
  const routerSrc = read('ui/router.js');
  const bootSrc = read('boot.js');
  const titleSrc = read('ui/screens/title.js');

  ok(/id="scr-voice"/.test(html), 'index.html ships the tenth section, #scr-voice');
  ok(/id="scr-voice"[^>]*class="gg-screen"/.test(html), 'with the .gg-screen class the router hides');
  ok(/id="scr-voice"[^>]*hidden/.test(html), 'and hidden, like every other screen');
  ok(SCREEN_IDS.voice === 'scr-voice', 'router SCREEN_IDS maps voice -> scr-voice', String(SCREEN_IDS.voice));
  ok(/voice:\s*'scr-voice'/.test(routerSrc), 'and says so in the source, not only at runtime');

  ok(/\bvoice:\s*voiceScreen\b/.test(bootSrc), 'boot.js registers the screen in the router map');
  ok(/import \* as voiceScreen from '\.\/ui\/screens\/voice\.js'/.test(bootSrc), 'and imports it statically');
  ok(/goVoice\s*\(\)\s*\{\s*router\.show\('voice'\)/.test(bootSrc), 'boot.js actions.goVoice() routes to it');
  ok(/S\.voice\.menu/.test(titleSrc) && /actions\.goVoice\(\)/.test(titleSrc),
    'the title menu renders the item from strings and calls the action');

  // The store is built ONCE, page-scoped, and threaded into BOTH consumers.
  ok(/noteStore = createNoteStore\(\{\s*prefs, logger\s*\}\)/.test(bootSrc), 'buildApp builds the ONE note store');
  const storeBuilds = (bootSrc.match(/createNoteStore\(/g) || []).length;
  ok(storeBuilds === 1, 'exactly once — two libraries would be two answers to "which note"', String(storeBuilds));
  ok(/createVoiceService\(\{[\s\S]{0,400}?noteStore,/.test(bootSrc),
    'and hands it to the voice service (sendNote is dead without it)');
  ok(/notes: noteStore/.test(bootSrc), 'and to every screen through ctx');
  ok(/setVoiceProvider\(\(emoteKey\)/.test(bootSrc), 'boot.js registers the emote hook\'s provider');
  ok(!/noteStore: null/.test(bootSrc), 'the wave-1 placeholder is gone');

  // The declaration is seeded from the preference on EVERY attach (a relay
  // rebuild hands us a fresh match with the declaration off).
  const attach = bootSrc.slice(bootSrc.indexOf('function attachMatch'), bootSrc.indexOf('function armRecapFallback'));
  ok(/setLocalVoiceNotes\(true\)/.test(attach), 'attachMatch mirrors prefs.voiceNotesEnabled onto the match');
}

// ================================================= 8. strings, styles, hygiene
{
  const src = read('ui/screens/voice.js');

  // Every S.voice.* the screen reads must exist — an undefined string renders
  // the word "undefined" at the player.
  const keys = new Set();
  for (const m of src.matchAll(/S\.voice\.([a-zA-Z0-9_]+)/g)) keys.add(m[1]);
  for (const m of src.matchAll(/S\.voice\.ack\.([a-zA-Z0-9_]+)/g)) keys.add('ack.' + m[1]);
  ok(keys.size > 10, 'the screen reads its copy from strings.js, not from literals', String(keys.size));
  for (const key of keys) {
    const value = key.startsWith('ack.') ? S.voice.ack[key.slice(4)] : S.voice[key];
    ok(value !== undefined, 'S.voice.' + key + ' exists');
  }

  // Nothing user-visible is hard-coded English in the screen body.
  ok(!/textContent = '[A-Za-z]{4,}/.test(src), 'no hard-coded sentence is assigned to a node');

  // The screen must not import the sibling wave's recorder statically: it may
  // not exist, and a static import that fails takes the WHOLE page down with a
  // blank loader (there are no devtools in WebView2).
  ok(!/^import[^\n]*voice\/recorder\.js/m.test(src), 'recorder.js is NOT statically imported');
  ok(/await import\('\.\.\/voice\/recorder\.js'\)/.test(src), 'it is reached by dynamic import, at the press');

  // ...and the cap it enforces is voiceService's, never a second copy of 10000.
  ok(/import \{ VN_MAX_MS, VN_MAX_NOTES \} from '\.\.\/voice\/voiceService\.js'/.test(src),
    'the screen imports the pinned constants rather than redefining them');
  ok(!/10[_ ]?000|10000/.test(src.replace(/VN_MAX_MS/g, '')), 'and no literal 10000 hides in it');

  const storeSrc = read('ui/voice/noteStore.js');
  ok(!/^import[^\n]*from '\.\.\/(prefs|audio)\.js'/m.test(storeSrc),
    'the store reaches prefs through its constructor, not by importing the module');
  // Comments stripped: the header TALKS about indexedDB at length, which is not
  // the same as touching it. What must not exist is a module-level reference —
  // one of those makes the whole page fail to import under node and, worse,
  // under a browser with storage disabled.
  const storeCode = stripComments(storeSrc);
  ok(!/\bindexedDB\b/.test(storeCode.split('function idbFactory')[0]),
    'and never touches indexedDB above the function that looks it up (node-import-safe)');

  const css = read('ui/screens.css');
  ok(/\.gg-voicelib \{/.test(css), 'screens.css styles the screen');
  ok(/\.gg-voice-picker \{/.test(css) && /\.gg-voice-emote \{/.test(css), 'and the emote picker');
  ok(/\.gg-voice-note \{[^}]*minmax\(0, 1fr\)/.test(css), 'the note row is written minmax(0,1fr) — phone-safe');

  /* THE COLLISION, and the reason this screen once rendered as a pile in the
   * top-right corner: ui/hud.css owns `.gg-voice` / `.gg-voice-hint` for the
   * in-duel mic strip (position:absolute; right:0), and it LOADS AFTER
   * screens.css — so any name shared between the two files is decided by the
   * HUD. Not "prefer not to"; the screen has no way to win. */
  const hudCss = read('ui/hud.css');
  const classNames = (text) => new Set(
    Array.from(stripComments(text).matchAll(/\.(gg-voice[a-z0-9-]*)/g), (m) => m[1]),
  );
  const hudNames = classNames(hudCss);
  const shared = Array.from(classNames(css)).filter((c) => hudNames.has(c));
  ok(shared.length === 0,
    'no gg-voice* class is styled by BOTH screens.css and hud.css (hud.css loads last and would win)',
    shared.join(', '));
  const screenSrc = stripComments(read('ui/screens/voice.js'));
  for (const name of ['gg-voice', 'gg-voice-hint']) {
    ok(!new RegExp("['\" ]" + name + "['\" ]").test(screenSrc),
      'the screen never mounts .' + name + ' — hud.css owns it');
  }

  // THE TRAP: .gg-grad + a `background:` shorthand paints the text transparent.
  // From the first RULE (not the header comment, which discusses the trap by
  // name) to the motion section that follows the block.
  const voiceBlock = stripComments(css.slice(css.indexOf('.gg-voicelib {'), css.indexOf('MOTION — the player')));
  ok(!/gg-grad/.test(voiceBlock), 'and no RULE in the voice block touches .gg-grad');
  ok(/animation-play-state: running/.test(voiceBlock),
    'the recording pulse re-declares `running`, so the effect armor cannot freeze the live-mic tell');
}

console.log(failures === 0 ? `PASS — ${n} checks` : `FAILED — ${failures}/${n} checks`);
process.exit(failures === 0 ? 0 : 1);
