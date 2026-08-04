// Self-contained sanity pass over the ABUSE REPORT surface — evidence
// generation, the report body, the in-match flag, and the recap card.
//   node Resources/web/goon/test/selftest-report.js
//
// The feature this covers is the one that has to work when everything else has
// already gone wrong: a duel partner sent this machine a file over a channel
// that never touched a server, and the player wants a human to look at it.
//
// What is asserted, and why each one is here:
//
//   1. IMPORT SWEEP. There are no devtools inside WebView2. A module that
//      throws at import does not error — the loader simply never settles and
//      the player watches a spinner forever. Every new/edited module is
//      imported here first, before anything else can matter.
//   2. THE EVIDENCE LADDER is pure arithmetic and is proved as such: no canvas,
//      no toBlob, no DOM. The stepping order (quality, then pixels) is the part
//      that decides whether a 700 KiB cap is ever hit at all.
//   3. THE BODY vs proxy/goon-routes.js POST /v2/goon/report. That route lives
//      in a DIFFERENT REPOSITORY, so it cannot be read from here: the contract
//      is transcribed below as data and pinned against buildReportBody. If the
//      route changes, this list is what has to change with it.
//   4. THE GATE. No peer media rendered -> no card. That is the whole privacy
//      story of this screen: a player is never offered the chance to report
//      their own library.
//   5. THE FLAG LIFECYCLE. Flag -> in the log -> survives the window's death ->
//      cleared by a NEW MATCH and by nothing else (clearing it on teardown
//      would wipe it milliseconds before the recap reads it).
//   6. THE FLAG BUTTON exists on a peer window and does not exist on a local
//      one, proved against the real createVideos() through the DOM stub.

import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

let failures = 0;
let n = 0;
function ok(cond, label, extra = '') {
  n++;
  if (!cond) { failures++; console.error(`  FAIL ${label} ${extra}`); }
}
const quiet = { info() {}, warn() {}, error() {}, debug() {} };
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

const HERE = path.dirname(fileURLToPath(import.meta.url));
const ROOT = path.join(HERE, '..');
// LF-normalized: the worktree is CRLF (core.autocrlf) and every pin below is
// written against \n.
const read = (rel) => fs.readFileSync(path.join(ROOT, rel), 'utf8').replace(/\r\n/g, '\n');

/* ===========================================================================
 * THE DOM STUB — the same shape selftest-exec.js carries, trimmed to what the
 * recap card and one floating video window need. Anything absent (canvas,
 * FileReader, requestAnimationFrame) is absent ON PURPOSE: every module here
 * must already degrade without it, and a throw means a guard is missing.
 * ========================================================================= */
class StubStyle {
  constructor() { this._p = new Map(); }
  setProperty(k, v) { this._p.set(k, String(v)); }
  getPropertyValue(k) { return this._p.get(k) || ''; }
}
class StubEl {
  constructor(tag) {
    this.tagName = String(tag).toUpperCase();
    this.childNodes = [];
    this.parentNode = null;
    this.isConnected = false;
    this.style = new StubStyle();
    this.textContent = '';
    this.value = '';
    this.disabled = false;
    this.hidden = false;
    this._cls = new Set();
    this._attrs = new Map();
    this._listeners = new Map();
  }
  get className() { return Array.from(this._cls).join(' '); }
  set className(v) { this._cls = new Set(String(v).split(/\s+/).filter(Boolean)); }
  get classList() {
    const set = this._cls;
    return {
      add: (...c) => c.forEach((x) => set.add(x)),
      remove: (...c) => c.forEach((x) => set.delete(x)),
      contains: (c) => set.has(c),
      toggle: (c, on) => (on ? set.add(c) : set.delete(c)),
    };
  }
  get childElementCount() { return this.childNodes.length; }
  appendChild(c) {
    if (c.parentNode) c.parentNode.removeChild(c);
    c.parentNode = this;
    mark(c, this.isConnected);
    this.childNodes.push(c);
    return c;
  }
  removeChild(c) {
    const i = this.childNodes.indexOf(c);
    if (i >= 0) this.childNodes.splice(i, 1);
    c.parentNode = null;
    mark(c, false);
    return c;
  }
  remove() { if (this.parentNode) this.parentNode.removeChild(this); else mark(this, false); }
  replaceChildren(...kids) {
    for (const c of this.childNodes.slice()) { c.parentNode = null; mark(c, false); }
    this.childNodes = [];
    for (const k of kids) this.appendChild(k);
  }
  setAttribute(k, v) { this._attrs.set(k, String(v)); }
  getAttribute(k) { return this._attrs.has(k) ? this._attrs.get(k) : null; }
  removeAttribute(k) { this._attrs.delete(k); if (k === 'src') this.src = ''; }
  addEventListener(t, fn) {
    if (!this._listeners.has(t)) this._listeners.set(t, []);
    this._listeners.get(t).push(fn);
  }
  removeEventListener(t, fn) {
    const a = this._listeners.get(t);
    if (!a) return;
    const i = a.indexOf(fn);
    if (i >= 0) a.splice(i, 1);
  }
  /** test-only: fire a listener set synchronously */
  fire(t, extra) {
    const evt = Object.assign({ type: t, preventDefault() {}, stopPropagation() {} }, extra || {});
    for (const fn of (this._listeners.get(t) || []).slice()) fn(evt);
  }
  /** test-only: every descendant carrying a class */
  findAll(cls, out = []) {
    for (const c of this.childNodes) {
      if (c._cls && c._cls.has(cls)) out.push(c);
      if (c.findAll) c.findAll(cls, out);
    }
    return out;
  }
  /** test-only: every descendant of a tag */
  findTag(tag, out = []) {
    const want = String(tag).toUpperCase();
    for (const c of this.childNodes) {
      if (c.tagName === want) out.push(c);
      if (c.findTag) c.findTag(tag, out);
    }
    return out;
  }
  /** test-only: does any descendant carry this text? */
  hasText(t) {
    if (String(this.textContent || '') === t) return true;
    return this.childNodes.some((c) => c.hasText && c.hasText(t));
  }
}
function mark(el, on) {
  el.isConnected = !!on;
  for (const c of el.childNodes || []) mark(c, on);
}

const byId = new Map();
for (const id of ['gg-stage', 'gg-fx-vwin']) {
  const el = new StubEl('div');
  el.setAttribute('id', id);
  el.isConnected = true;
  byId.set(id, el);
}
const documentElement = new StubEl('html');
documentElement.isConnected = true;
globalThis.document = {
  documentElement,
  body: (() => { const b = new StubEl('body'); b.isConnected = true; return b; })(),
  createElement: (tag) => new StubEl(tag),
  createTextNode: (t) => { const e = new StubEl('#text'); e.textContent = t; return e; },
  getElementById: (id) => byId.get(id) || null,
  addEventListener() {}, removeEventListener() {}, dispatchEvent() { return true; },
};
globalThis.window = { innerWidth: 1280, innerHeight: 720 };

/* ===========================================================================
 * IMPORTS — check 1. Everything this wave touched, in one place.
 * ========================================================================= */
const report = await import('../ui/report.js');
const recap = await import('../ui/screens/recap.js');
const videos = await import('../exec/videos.js');
const { S } = await import('../ui/strings.js');
const layersMod = await import('../exec/layers.js');

ok(typeof report.evidenceFor === 'function', 'ui/report.js exports evidenceFor');
ok(typeof report.submitReport === 'function', 'ui/report.js exports submitReport');
ok(typeof report.buildReportBody === 'function', 'ui/report.js exports buildReportBody');
ok(typeof recap.mount === 'function', 'ui/screens/recap.js still exports mount');
ok(typeof recap.assertStageClear === 'function', 'ui/screens/recap.js still exports assertStageClear');
ok(typeof recap.reportCandidates === 'function', 'ui/screens/recap.js exports reportCandidates');
ok(typeof recap.EXPLORE_URL === 'string' && /^https:\/\/cclabs\.app\//.test(recap.EXPLORE_URL),
  'ui/screens/recap.js exports EXPLORE_URL and it is an https cclabs.app link', recap.EXPLORE_URL);
// The site is static hosting with clean URLs OFF: /explore is a 404 and only
// /explore.html answers. Dropping the extension would ship a dead CTA.
ok(recap.EXPLORE_URL.endsWith('.html'), 'and it keeps the .html — /explore alone is a 404', recap.EXPLORE_URL);
ok(typeof videos.peerRenderLog === 'function', 'exec/videos.js exports peerRenderLog');
ok(typeof videos.resetPeerRenderLog === 'function', 'exec/videos.js exports resetPeerRenderLog');
ok(typeof videos.notePeerFlag === 'function', 'exec/videos.js exports notePeerFlag');

/* ===========================================================================
 * 2. THE EVIDENCE LADDER — pure arithmetic, no canvas anywhere near it.
 * ========================================================================= */
{
  const steps = report.evidenceSteps();
  ok(Array.isArray(steps) && steps.length >= 3, 'evidenceSteps returns a ladder', String(steps.length));
  ok(steps[0].maxDim === 320 && Math.abs(steps[0].quality - 0.7) < 1e-9,
    'the FIRST rung is the best one: 320px at q0.7',
    JSON.stringify(steps[0]));

  // Quality is spent before pixels are: every 320px rung comes before any 240px
  // one. A soft 320px thumbnail is still recognisable; a crisp 160px one is not.
  const firstBelow320 = steps.findIndex((s) => s.maxDim < 320);
  const lastAt320 = steps.map((s) => s.maxDim).lastIndexOf(320);
  ok(lastAt320 < firstBelow320, 'the whole quality ladder is spent at 320px before any pixels are');
  ok(steps.filter((s) => s.maxDim === 320).length === report.EVIDENCE_QUALITIES.length,
    'every quality is tried at the full dimension');

  // Monotonic descent: a rung is never bigger AND better than the one before it.
  let mono = true;
  for (let i = 1; i < steps.length; i++) {
    if (steps[i].maxDim > steps[i - 1].maxDim) mono = false;
    if (steps[i].maxDim === steps[i - 1].maxDim && steps[i].quality >= steps[i - 1].quality) mono = false;
  }
  ok(mono, 'the ladder only ever descends — first fit is therefore the best fit');
  ok(steps[steps.length - 1].maxDim === 160,
    'the last rung is the smallest dimension the module will ever offer');

  // Never re-raise quality after dropping dimensions: a small frame at q0.7 can
  // encode LARGER than the previous frame at q0.35, which would break first-fit.
  const dims = [...new Set(steps.map((s) => s.maxDim))];
  let noRaise = true;
  for (let i = 1; i < dims.length; i++) {
    const prevMin = Math.min(...steps.filter((s) => s.maxDim === dims[i - 1]).map((s) => s.quality));
    const curMax = Math.max(...steps.filter((s) => s.maxDim === dims[i]).map((s) => s.quality));
    if (curMax > prevMin) noRaise = false;
  }
  ok(noRaise, 'quality is never raised again after a dimension drop');
}

{
  // fitDims: aspect preserved, long edge capped, nothing ever collapses to 0.
  const a = report.fitDims(1920, 1080, 320);
  ok(a.w === 320 && a.h === 180, 'fitDims caps the LONG edge and keeps the aspect', JSON.stringify(a));
  const b = report.fitDims(1080, 1920, 320);
  ok(b.h === 320 && b.w === 180, 'fitDims caps a portrait source by its height', JSON.stringify(b));
  const c = report.fitDims(100, 60, 320);
  ok(c.w === 100 && c.h === 60, 'a source already smaller than the cap is left alone');
  const d = report.fitDims(4000, 1, 320);
  ok(d.w === 320 && d.h === 1, 'an extreme aspect never rounds a dimension to zero', JSON.stringify(d));
  const z = report.fitDims(0, 0, 320);
  ok(z.w >= 1 && z.h >= 1, 'a zero-size source clamps to 1x1 rather than producing NaN');
}

{
  // stripDims: three 320x180 frames stack into one 320x540 vertical strip.
  const s = report.stripDims(320, 180, 3);
  ok(s.w === 320 && s.h === 540 && s.frames === 3, 'three frames stack vertically', JSON.stringify(s));
  const one = report.stripDims(320, 180, 1);
  ok(one.h === 180 && one.frames === 1, 'a single-frame strip is just the frame');
  // The route rejects frames > 8, w > 4096 and h > 8192 outright, so the client
  // clamps rather than shipping something guaranteed to 400.
  const many = report.stripDims(320, 180, 99);
  ok(many.frames === report.EVIDENCE_MAX_FRAMES, 'frames clamp to the route ceiling', String(many.frames));
  const tall = report.stripDims(320, 9000, 8);
  ok(tall.h <= report.EVIDENCE_MAX_H, 'the strip height clamps to the route ceiling', String(tall.h));
  const wide = report.stripDims(99999, 10, 1);
  ok(wide.w <= report.EVIDENCE_MAX_W, 'the strip width clamps to the route ceiling', String(wide.w));
}

{
  ok(report.EVIDENCE_MAX_B64 === 700 * 1024,
    'the b64 cap matches goon-routes.js REPORT_EVIDENCE_MAX_B64 (700*1024 CHARS)');
  ok(report.fitsEvidence('A'.repeat(1000)) === true, 'a small payload fits');
  ok(report.fitsEvidence('A'.repeat(report.EVIDENCE_MAX_B64)) === true, 'exactly at the cap still fits');
  ok(report.fitsEvidence('A'.repeat(report.EVIDENCE_MAX_B64 + 1)) === false, 'one char over does not');
  ok(report.fitsEvidence('') === false, 'an empty payload is not evidence');
  ok(report.fitsEvidence(null) === false, 'and neither is a null');

  ok(report.stripDataPrefix('data:image/webp;base64,AAAA') === 'AAAA', 'the data: prefix is stripped');
  ok(report.stripDataPrefix('AAAA') === 'AAAA', 'a bare payload is passed through');
  ok(report.stripDataPrefix('') === '', 'an empty read resolves empty, never undefined');
}

/* ===========================================================================
 * 3. THE BODY — pinned against POST /v2/goon/report (another repository).
 * ========================================================================= */
const SERVER_CONTRACT = Object.freeze({
  path: '/v2/goon/report',
  // Read by the route, in the order it reads them.
  fields: ['unified_id', 'code', 'role', 'token', 'sha256', 'mime', 'bytes',
    'reason', 'note', 'at_match_ms'],
  optional: ['evidence'],
  reasons: ['csam', 'nonconsensual', 'gore', 'illegal', 'other'],
  noteMax: 500,
  evidenceKind: 'thumb_strip',
  evidenceMimes: ['image/webp', 'image/jpeg'],
  evidenceMaxB64: 700 * 1024,
});

const SHA_A = 'a'.repeat(64);
const SHA_B = 'b'.repeat(64);
const goodSession = {
  identity: { unifiedId: 'user-123', displayName: 'A' },
  room: { code: 'ABC123', token: 'tok-xyz', role: 'guest' },
};

{
  ok(report.REPORT_REASONS.length === SERVER_CONTRACT.reasons.length
    && report.REPORT_REASONS.every((r, i) => r === SERVER_CONTRACT.reasons[i]),
    'the five WIRE reason codes are exactly the route\'s REPORT_REASONS, in order',
    report.REPORT_REASONS.join(','));
  ok(report.NOTE_MAX === SERVER_CONTRACT.noteMax, 'NOTE_MAX matches REPORT_NOTE_MAX');
  ok(report.EVIDENCE_KIND === SERVER_CONTRACT.evidenceKind, 'the only evidence kind is thumb_strip');

  const body = report.buildReportBody({
    session: goodSession,
    artifact: { sha: SHA_A, mime: 'video/mp4', bytes: 1234567 },
    reason: 'gore',
    note: 'it was a real injury',
    atMatchMs: 42000,
  });
  ok(!!body, 'a complete request builds a body');
  for (const f of SERVER_CONTRACT.fields) {
    ok(Object.prototype.hasOwnProperty.call(body, f), 'the body carries ' + f);
  }
  const extra = Object.keys(body).filter(
    (k) => SERVER_CONTRACT.fields.indexOf(k) < 0 && SERVER_CONTRACT.optional.indexOf(k) < 0);
  ok(extra.length === 0, 'and nothing the route does not read', extra.join(','));
  ok(body.unified_id === 'user-123', 'unified_id comes from session.identity');
  ok(body.code === 'ABC123' && body.role === 'guest' && body.token === 'tok-xyz',
    'code/role/token come from the session.room stash');
  ok(body.sha256 === SHA_A && body.bytes === 1234567 && body.mime === 'video/mp4',
    'the artifact is reported by HASH, with mime/bytes as claims');
  ok(body.at_match_ms === 42000, 'at_match_ms rides along');
  ok(!('evidence' in body), 'no evidence -> the key is OMITTED, not null (the route branches on != null)');

  // THE TOKEN GOES OUT REGARDLESS. The route tries roomAuth first and falls back
  // to the 14-day pair record once the 30-minute room has expired; a client that
  // withheld the token would work for half an hour and then never again.
  ok(typeof body.token === 'string', 'the token is always sent, room alive or not');
}

{
  const long = 'x'.repeat(900);
  const b = report.buildReportBody({
    session: goodSession, artifact: { sha: SHA_A }, reason: 'other', note: long,
  });
  ok(b.note.length === SERVER_CONTRACT.noteMax, 'the note is clamped to 500 chars', String(b.note.length));

  const nl = report.clampNote('one\ntwo\r\nthree\tfour');
  ok(nl.indexOf('\n') < 0 && nl.indexOf('\r') < 0 && nl.indexOf('\t') < 0,
    'newlines and tabs are flattened before storage (the note lands in a console verbatim)', nl);
  ok(report.clampNote(null) === '', 'a missing note is an empty string, never "null"');
  ok(report.buildReportBody({ session: goodSession, artifact: { sha: SHA_A }, reason: 'csam' }).note === '',
    'and the field is still present so the route never reads undefined');
}

{
  // Refusals: anything that cannot be attributed is not sent at all.
  ok(report.buildReportBody({ session: goodSession, artifact: { sha: SHA_A }, reason: 'nope' }) === null,
    'an unknown reason code never leaves the page');
  ok(report.buildReportBody({ session: goodSession, artifact: { sha: 'zz' }, reason: 'gore' }) === null,
    'a malformed sha256 never leaves the page');
  ok(report.buildReportBody({
    session: { identity: { unifiedId: 'u' }, room: null }, artifact: { sha: SHA_A }, reason: 'gore',
  }) === null, 'no room stash -> nothing to report against (Practice has no peer)');
  ok(report.buildReportBody({
    session: { identity: null, room: goodSession.room }, artifact: { sha: SHA_A }, reason: 'gore',
  }) === null, 'no identity -> the route would 400 on unified_id anyway');
  ok(report.buildReportBody({
    session: { identity: { unifiedId: 'u' }, room: { code: 'ABC123', role: 'spectator' } },
    artifact: { sha: SHA_A }, reason: 'gore',
  }) === null, 'a role the route does not know is refused here, not there');
}

{
  // Evidence validation, mirroring the route's own checks.
  const base = { kind: 'thumb_strip', mime: 'image/webp', w: 320, h: 540, frames: 3, b64: 'AAAA' };
  ok(report.validEvidence(base) === true, 'a well-formed strip validates');
  ok(report.validEvidence(Object.assign({}, base, { kind: 'photo' })) === false, 'kind must be thumb_strip');
  ok(report.validEvidence(Object.assign({}, base, { mime: 'image/png' })) === false,
    'png is not one of the two accepted evidence mimes');
  ok(report.validEvidence(Object.assign({}, base, { mime: 'image/jpeg' })) === true,
    'jpeg is (the toBlob fallback)');
  ok(report.validEvidence(Object.assign({}, base, { b64: 'AA A' })) === false,
    'a payload with a non-base64 character is refused before it can 400');
  ok(report.validEvidence(Object.assign({}, base, { b64: 'AAA' })) === false,
    'and so is one whose length is not a multiple of 4');
  ok(report.validEvidence(Object.assign({}, base, { b64: 'A'.repeat(SERVER_CONTRACT.evidenceMaxB64 + 4) })) === false,
    'over the cap is refused HERE — a 413 would lose the whole report');
  ok(report.validEvidence(Object.assign({}, base, { frames: 0 })) === false, 'zero frames is not evidence');
  ok(report.validEvidence(Object.assign({}, base, { frames: 9 })) === false, 'nine frames is over the ceiling');
  ok(report.validEvidence(Object.assign({}, base, { h: 9000 })) === false, 'an over-tall strip is refused');
  ok(report.validEvidence(null) === false, 'null is not evidence');

  const withEv = report.buildReportBody({
    session: goodSession, artifact: { sha: SHA_A }, reason: 'csam', evidence: base,
  });
  ok(withEv.evidence && withEv.evidence.kind === 'thumb_strip' && withEv.evidence.b64 === 'AAAA',
    'valid evidence is carried through verbatim');
  const extraKeys = Object.keys(withEv.evidence).filter(
    (k) => ['kind', 'mime', 'w', 'h', 'frames', 'b64'].indexOf(k) < 0);
  ok(extraKeys.length === 0, 'and carries nothing else', extraKeys.join(','));

  // THE LOAD-BEARING ONE: evidence that could not be made must not stop a report.
  const noEv = report.buildReportBody({
    session: goodSession, artifact: { sha: SHA_A }, reason: 'csam', evidence: null,
  });
  ok(!!noEv && !('evidence' in noEv),
    'an EVIDENCE FAILURE STILL FILES THE REPORT — the route accepts a body without it');
  const badEv = report.buildReportBody({
    session: goodSession, artifact: { sha: SHA_A }, reason: 'csam',
    evidence: { kind: 'thumb_strip', mime: 'image/webp', w: 0, h: 0, frames: 0, b64: '' },
  });
  ok(!!badEv && !('evidence' in badEv), 'and so does a half-built one — it is dropped, not sent');
}

/* ===========================================================================
 * 3b. THE RESPONSE — four states, and one of them is "no answer at all".
 * ========================================================================= */
{
  const okRes = report.parseReportResponse({ status: 200, body: '{"ok":true,"id":"GR-ABCDEFGHJK","deduped":false}' });
  ok(okRes.ok && okRes.id === 'GR-ABCDEFGHJK' && !okRes.deduped, 'a stored report reads ok + id');

  const dup = report.parseReportResponse({ status: 200, body: '{"ok":true,"id":"GR-ABCDEFGHJK","deduped":true}' });
  ok(dup.ok && dup.deduped, 'a repeat of the same hash by the same reporter reads DEDUPED, not failed');

  const dead = report.parseReportResponse({ status: 0, body: '' });
  ok(!dead.ok && dead.status === 0 && dead.error === 'network',
    'status 0 is "we never got an answer" — postNet never rejects, so this is the timeout path');

  const limited = report.parseReportResponse({ status: 429, body: '{"error":"rate_limited","cap":"day"}' });
  ok(!limited.ok && limited.error === 'rate_limited', 'a 429 surfaces the route\'s own error string');

  const oops = report.parseReportResponse({ status: 500, body: 'not json at all' });
  ok(!oops.ok && oops.error === 'http_500', 'an unparseable body still yields a usable state, never a throw');

  const lying = report.parseReportResponse({ status: 200, body: '{"ok":false}' });
  ok(!lying.ok, 'a 200 that does not say ok is not a success');
}

/* ===========================================================================
 * 3c. submitReport end to end, over an injected post.
 * ========================================================================= */
{
  const seen = [];
  const post = (p, b) => { seen.push({ p, b }); return Promise.resolve({ status: 200, body: '{"ok":true,"id":"GR-2222222222"}' }); };
  const r = await report.submitReport({
    session: goodSession, artifact: { sha: SHA_B, mime: 'image/webp', bytes: 900 },
    reason: 'nonconsensual', note: 'that is not their footage', atMatchMs: 1000, evidence: null, post,
  });
  ok(r.ok && r.id === 'GR-2222222222', 'submitReport resolves the parsed result');
  ok(seen.length === 1 && seen[0].p === SERVER_CONTRACT.path, 'and posts to /v2/goon/report', seen[0] && seen[0].p);
  ok(seen[0].b.reason === 'nonconsensual' && seen[0].b.sha256 === SHA_B, 'with the body it built');

  const bad = await report.submitReport({ session: goodSession, artifact: { sha: 'nope' }, reason: 'gore', post });
  ok(!bad.ok && bad.error === 'incomplete' && seen.length === 1,
    'an unbuildable report never reaches the network at all');

  // A `post` that REJECTS is not a contract postNet has, but a host bridge that
  // has gone away might; it must not escape as an unhandled rejection.
  const boom = await report.submitReport({
    session: goodSession, artifact: { sha: SHA_A }, reason: 'gore',
    post: () => Promise.reject(new Error('bridge gone')),
  });
  ok(!boom.ok && boom.status === 0, 'a throwing transport reads as "no answer", not as a crash');
}

/* ===========================================================================
 * 3d. evidenceFor with no canvas at all — the WebView2-less path.
 * ========================================================================= */
{
  // The stub DOM never fires `load`, so the deadline is the only way out. It has
  // to resolve null — not throw, not hang, and not leave the report button
  // spinning. (The short timeouts are the module's declared test affordance.)
  const fast = { loadTimeoutMs: 20, seekTimeoutMs: 20 };
  const ev = await report.evidenceFor({ url: 'https://ccp.cache/recv/x.webp', kind: 'image', mime: 'image/webp' }, fast);
  ok(ev === null, 'a source that never becomes drawable yields NO evidence rather than an exception');
  const vid = await report.evidenceFor({ url: 'https://ccp.cache/recv/x.mp4', kind: 'video', mime: 'video/mp4' }, fast);
  ok(vid === null, 'and neither does a clip that never reports its metadata');
  const none = await report.evidenceFor(null, fast);
  ok(none === null, 'and so does a missing view');
  const noUrl = await report.evidenceFor({ url: '', kind: 'video' }, fast);
  ok(noUrl === null, 'and a row with no url');
}

/* ===========================================================================
 * 4. THE GATE — reportCandidates decides whether the card exists.
 * ========================================================================= */
const rowA = { sha: SHA_A, kind: 'video', mime: 'video/mp4', bytes: 10, url: 'https://ccp.cache/recv/a.mp4' };
const rowB = { sha: SHA_B, kind: 'image', mime: 'image/webp', bytes: 20, url: 'https://ccp.cache/recv/b.webp' };
{
  ok(recap.reportCandidates({}).length === 0, 'NOTHING peer-side -> no candidates -> NO CARD');
  ok(recap.reportCandidates({ landed: [], held: [], rendered: [] }).length === 0,
    'and empty inputs are the same answer');

  const landedOnly = recap.reportCandidates({ landed: [rowA] });
  ok(landedOnly.length === 1 && landedOnly[0].sha === SHA_A,
    'an artifact that LANDED this match is reportable even without a render record (flashes have none)');
  ok(landedOnly[0].rendered === false && landedOnly[0].flagged === false,
    'and it is honestly marked as neither rendered nor flagged');

  // The cross-session reuse win: `decline:'have'` means the artifact never
  // "lands" this match, but it can still render — so `held` is the lookup.
  const reused = recap.reportCandidates({
    landed: [], held: [rowA, rowB], rendered: [{ sha: SHA_B, at: 5, flagged: false }],
  });
  ok(reused.length === 1 && reused[0].sha === SHA_B,
    'an artifact reused from a previous session still appears when it RENDERED this match');
  ok(reused[0].url === rowB.url && reused[0].mime === rowB.mime,
    'resolved through `held`, so it has the mime/bytes/url the report body needs');

  // FLAGGED FIRST — the player already told us which one they meant.
  const ordered = recap.reportCandidates({
    landed: [rowA, rowB], held: [rowA, rowB],
    rendered: [{ sha: SHA_A, at: 1, flagged: false }, { sha: SHA_B, at: 2, flagged: true }],
  });
  ok(ordered.length === 2 && ordered[0].sha === SHA_B, 'the flagged artifact sorts first', ordered[0].sha.slice(0, 4));
  ok(ordered[0].flagged === true && ordered[1].flagged === false, 'and the flag survives into the row');

  // A rendered hash with no row anywhere is dropped: there is no thumbnail to
  // show and no mime/bytes to report.
  const orphan = recap.reportCandidates({ landed: [], held: [], rendered: [{ sha: SHA_A, at: 1, flagged: true }] });
  ok(orphan.length === 0, 'a flagged hash the store no longer holds is dropped, not rendered as a blank tile');

  // No duplicates, however many sources name the same hash.
  const dupes = recap.reportCandidates({
    landed: [rowA, rowA], held: [rowA], rendered: [{ sha: SHA_A, at: 1, flagged: true }],
  });
  ok(dupes.length === 1, 'one hash is one tile, however many lists name it');

  // The cap.
  const many = [];
  const marks = [];
  for (let i = 0; i < 20; i++) {
    const sha = String(i).padStart(2, '0').repeat(32);
    many.push({ sha, kind: 'image', mime: 'image/webp', bytes: 1, url: 'u' + i });
    marks.push({ sha, at: i, flagged: false });
  }
  const capped = recap.reportCandidates({ landed: many, held: many, rendered: marks });
  ok(capped.length === recap.MAX_REPORT_ITEMS, 'the shortlist is capped', String(capped.length));
  ok(recap.MAX_REPORT_ITEMS === 6, 'at six — a shortlist, not a gallery');
}

/* ===========================================================================
 * 5. THE FLAG LIFECYCLE — and the reset point, which is the trap.
 * ========================================================================= */
{
  videos.resetPeerRenderLog();
  ok(videos.peerRenderLog().rendered.length === 0, 'the log starts empty');

  videos.notePeerRender(SHA_A);
  let log = videos.peerRenderLog();
  ok(log.rendered.length === 1 && log.rendered[0].sha === SHA_A, 'a peer render is recorded');
  ok(log.rendered[0].flagged === false && log.flagged.length === 0, 'rendered is not flagged');
  ok(log.rendered[0].at > 0, 'with a timestamp, so the recap can place it in the match');

  videos.notePeerRender(SHA_A);
  ok(videos.peerRenderLog().rendered.length === 1, 'the same artifact twice is still one entry');

  videos.notePeerFlag(SHA_A);
  log = videos.peerRenderLog();
  ok(log.rendered[0].flagged === true && log.flagged[0] === SHA_A, 'flagging marks the existing entry');

  // A flag on something that was never recorded still counts: the recap card is
  // the only place a report can be filed and it must not lose one.
  videos.notePeerFlag(SHA_B);
  log = videos.peerRenderLog();
  ok(log.flagged.length === 2, 'a flag implies a render', String(log.flagged.length));

  // The returned rows are COPIES — a caller cannot mutate the log by accident.
  log.rendered[0].flagged = false;
  ok(videos.peerRenderLog().rendered[0].flagged === true, 'peerRenderLog hands out copies, not the live records');

  ok(videos.notePeerRender('') === false && videos.notePeerFlag(null) === false,
    'an empty hash is refused rather than creating a phantom entry');

  videos.resetPeerRenderLog();
  ok(videos.peerRenderLog().rendered.length === 0, 'reset empties it — and it is the ONLY thing that does');
}

/* ===========================================================================
 * 6. THE FLAG BUTTON — peer windows only, proved against the real renderer.
 * ========================================================================= */
function fakeMedia({ peer }) {
  const url = peer ? 'https://ccp.cache/recv/' + SHA_A + '.mp4' : 'https://ccp.assets/video/1';
  const entry = peer
    ? { kind: 'video', name: 'x', url, sha: SHA_A, mime: 'video/mp4', provenance: 'peer' }
    : { kind: 'video', name: 'v', url };
  return {
    drawKind: () => entry,
    drawFor: () => entry,
    draw: () => entry,
    acquire: (e) => (e
      ? (peer
        ? { url: e.url, release() {}, provenance: 'peer', sha: SHA_A, mime: 'video/mp4' }
        : { url: e.url, release() {}, provenance: 'local' })
      : null),
    hasMedia: () => true,
  };
}

{
  videos.resetPeerRenderLog();
  const vwin = byId.get('gg-fx-vwin');
  const layers = { get: (k) => (k === 'vwin' ? vwin : byId.get('gg-stage')), stopAll() {} };

  // --- a PEER window gets the flag.
  const peerVids = videos.createVideos({ layers, media: fakeMedia({ peer: true }), logger: quiet });
  peerVids.renderPayload({ duration_ms: 4000, intensity: 0.5 }, () => {});
  ok(peerVids.windowCount() === 1, 'a peer payload opened a window');
  let flags = vwin.findAll(videos.FLAG_BTN_CLASS);
  ok(flags.length === 1, 'a peer window carries exactly one flag button', String(flags.length));
  ok(flags[0]._cls.has(videos.SKIP_BTN_CLASS),
    'and it wears the SKIP_BTN_CLASS too, so fx.css owns every pixel of its chrome');
  ok(vwin.findAll(videos.SKIP_BTN_CLASS).length === 2, 'alongside the ✕, which is unchanged');

  // The press must not start a drag: the button stops pointerdown propagating.
  ok((flags[0]._listeners.get('pointerdown') || []).length === 1,
    'the flag swallows pointerdown so it can never start a window drag');
  ok((flags[0]._listeners.get('click') || []).length === 1, 'and acts on click, like the ✕');
  // …and it does NOT preventDefault the pointerdown, or the browser never
  // raises the click the control exists for.
  let defaulted = false;
  flags[0].fire('pointerdown', { preventDefault() { defaulted = true; } });
  ok(defaulted === false, 'the flag does NOT preventDefault the press — that would kill its own click');
  // Wheel is deliberately left to bubble: resizing over the button still works.
  ok((flags[0]._listeners.get('wheel') || []).length === 0,
    'the flag never intercepts wheel — resizing a window over it still works');

  // --- the click: hidden, flagged, receipted ENDURED.
  const receipts = [];
  const peer2 = videos.createVideos({ layers, media: fakeMedia({ peer: true }), logger: quiet });
  peer2.renderPayload({ duration_ms: 4000, intensity: 0.5 }, (endured) => receipts.push(endured));
  const btn = vwin.findAll(videos.FLAG_BTN_CLASS).pop();
  btn.fire('click');
  ok(peer2.windowCount() === 0, 'the flag settles THAT window immediately');
  ok(receipts.length === 1 && receipts[0] === true,
    'and receipts it ENDURED — byte-identical to a plain ✕ dismissal, so the wire never tells the sender they were flagged',
    JSON.stringify(receipts));
  ok(videos.peerRenderLog().flagged.indexOf(SHA_A) >= 0, 'the hash is queued for the recap card');
  ok(videos.peerRenderLog().rendered.length === 1, 'and the render log holds it');

  peerVids.stopWindows();
  peer2.stopWindows();
  ok(videos.peerRenderLog().rendered.length === 1,
    'stopWindows does NOT clear the log — clearForRecap runs it moments before the recap reads it');

  // --- a LOCAL window has no flag at all.
  videos.resetPeerRenderLog();
  vwin.replaceChildren();
  const localVids = videos.createVideos({ layers, media: fakeMedia({ peer: false }), logger: quiet });
  localVids.renderPayload({ duration_ms: 4000, intensity: 0.5 }, () => {});
  ok(localVids.windowCount() === 1, 'a local payload opened a window too');
  ok(vwin.findAll(videos.FLAG_BTN_CLASS).length === 0,
    'a window drawn from the RECEIVER\'S OWN library has no flag — there is nobody to report');
  ok(vwin.findAll(videos.SKIP_BTN_CLASS).length === 1, 'only the ✕');
  ok(videos.peerRenderLog().rendered.length === 0, 'and nothing was written to the peer render log');
  localVids.stopWindows();
  vwin.replaceChildren();
  videos.resetPeerRenderLog();
  await sleep(20);
  void layersMod;
}

/* ===========================================================================
 * 7. THE CARD ON THE SCREEN — mounted through the real recap module.
 * ========================================================================= */
function fakeCtx(store, rendered) {
  return {
    logger: quiet,
    audio: { sfx() {}, music() {}, stopMusic() {} },
    prefs: { get: () => 0, set() {} },
    matchLog: { stats: () => ({ landedOnYou: 0, enduredByYou: 0 }), sawPhase: () => false, payloads: () => [] },
    actions: { leave() {} },
    getMatch: () => null,
    session: goodSession,
    receivedStore: store,
    getPeerRenders: () => ({ rendered, flagged: rendered.filter((r) => r.flagged).map((r) => r.sha) }),
  };
}
const fakeStore = (landed, held) => ({
  thisMatch: () => landed,
  list: () => (held || landed),
});

{
  // --- nothing crossed: no card, and the recap is otherwise untouched.
  const box = new StubEl('section');
  const h = recap.mount(box, fakeCtx(fakeStore([], []), []));
  ok(box.findAll('gg-recap-report').length === 0, 'no peer artifacts -> the report card is not rendered at all');
  ok(box.findAll('gg-recap-hero').length === 1, 'while the rest of the recap paints as it always did');
  h.unmount();
}

{
  // --- one landed: the card appears with a thumbnail row.
  const box = new StubEl('section');
  const h = recap.mount(box, fakeCtx(fakeStore([rowA, rowB], [rowA, rowB]),
    [{ sha: SHA_B, at: 10, flagged: true }]));
  const cards = box.findAll('gg-recap-report');
  ok(cards.length === 1, 'peer artifacts landed -> the card is on the recap');
  const thumbs = cards[0].findAll('gg-report-thumb');
  ok(thumbs.length === 2, 'one tile per artifact', String(thumbs.length));
  ok(thumbs[0]._cls.has('is-flagged'), 'the one flagged in-match is first and says so');
  ok(cards[0].hasText(S.recap.reportTitle), 'the card is titled from strings');
  ok(cards[0].findAll('gg-report-reason').length === 0, 'and nothing is asked until a tile is picked');

  // The card sits ABOVE the actions: leaving must never be below a long form.
  const kids = box.childNodes[0].childNodes.map((c) => c.className);
  ok(kids.findIndex((c) => c.includes('gg-recap-report'))
    < kids.findIndex((c) => c.includes('gg-recap-actions')),
    'the card is above "Back to menu" — the way out is never buried under it');

  // --- pick one: the reason list appears.
  thumbs[0].fire('click');
  const card2 = box.findAll('gg-recap-report')[0];
  const reasons = card2.findAll('gg-report-reason');
  ok(reasons.length === report.REPORT_REASONS.length, 'picking a tile reveals all five reasons', String(reasons.length));
  ok(reasons.every((r, i) => r.textContent === S.report.reasons[i].label),
    'labelled in player language, from strings');

  // --- 'other' needs words before it can be sent.
  const otherAt = S.report.reasons.findIndex((r) => r.code === 'other');
  reasons[otherAt].fire('click');
  const card3 = box.findAll('gg-recap-report')[0];
  ok(card3.findTag('textarea').length === 1, "'other' opens the note field");
  ok(card3.findTag('textarea')[0].getAttribute('maxlength') === String(report.NOTE_MAX),
    'clamped at 500 in the markup as well as on the way out');
  const send = card3.findAll('gg-btn').find((b) => b.textContent === S.report.submit);
  ok(!!send && send.disabled === true, "…and 'other' with no note cannot be submitted");

  // --- a reason that speaks for itself can be sent straight away.
  const gore = S.report.reasons.findIndex((r) => r.code === 'gore');
  box.findAll('gg-report-reason')[gore].fire('click');
  const send2 = box.findAll('gg-btn').find((b) => b.textContent === S.report.submit);
  ok(!!send2 && send2.disabled === false, 'a self-explanatory reason needs no note');

  // --- and there is a visible way back out of it.
  const bail = box.findAll('gg-btn').find((b) => b.textContent === S.report.cancel);
  ok(!!bail && bail.disabled === false, 'a "never mind" sits beside submit — the form is never a trap');
  bail.fire('click');
  ok(box.findAll('gg-report-reason').length === 0, 'and it puts the whole form away');
  ok(box.findAll('gg-report-thumb').every((t) => t.getAttribute('aria-pressed') === 'false'),
    'clearing the pick clears the selection state with it');

  // --- unpicking by re-clicking the tile does the same.
  box.findAll('gg-report-thumb')[0].fire('click');
  ok(box.findAll('gg-report-reason').length === 1 * report.REPORT_REASONS.length,
    'and picking again re-opens it');
  box.findAll('gg-report-thumb')[0].fire('click');
  ok(box.findAll('gg-report-reason').length === 0, 'clicking the chosen tile again also puts the form away');

  h.unmount();
  ok(box.childNodes.length >= 0, 'unmount does not throw');
}

{
  // NO SESSION ROOM = NO CARD. Practice has no peer to report, and the body
  // could not be built anyway (buildReportBody returns null without a room).
  const box = new StubEl('section');
  const ctx = fakeCtx(fakeStore([rowA], [rowA]), []);
  ctx.session = { identity: goodSession.identity, room: null };
  const h = recap.mount(box, ctx);
  ok(box.findAll('gg-recap-report').length === 0,
    'a Practice recap never offers a report — there is no room, no peer and no body to build');
  h.unmount();
}

/* ===========================================================================
 * 7.5 THE STANDALONE CTA — the only piece of marketing on the end card, and
 *     the only external link the page has. It is aimed at the phone joiner who
 *     followed an invite link and has never seen the app the payloads came out
 *     of; hosted, it is absent rather than quiet.
 * ========================================================================= */
{
  // goodSession carries no `hosted` flag — that IS standalone (title.js reads
  // the same one).
  const box = new StubEl('section');
  const h = recap.mount(box, fakeCtx(fakeStore([], []), []));
  const cta = box.findAll('gg-recap-cta');
  ok(cta.length === 1, 'standalone -> the recap offers the app');
  const link = cta[0].findTag('a')[0] || null;
  ok(!!link && link.getAttribute('href') === recap.EXPLORE_URL,
    'the CTA is a real link to the explore page', link ? String(link.getAttribute('href')) : 'no <a>');
  ok(!!link && link.getAttribute('target') === '_blank' && link.getAttribute('rel') === 'noopener',
    'opened in a new tab, with no window.opener handle back into the duel');
  ok(!!link && link.textContent === S.recap.ctaLink, 'labelled from strings');
  ok(cta[0].hasText(S.recap.ctaTitle) && cta[0].hasText(S.recap.ctaLead),
    'and the copy is in ui/strings.js, not inline in the screen');

  // Below the way out, like everything else this screen appends.
  const kids = box.childNodes[0].childNodes.map((c) => c.className);
  ok(kids.findIndex((c) => c.includes('gg-recap-actions'))
    < kids.findIndex((c) => c.includes('gg-recap-cta')),
    'and it sits BELOW "Back to menu" — nothing this screen adds may bury the exit');
  h.unmount();
}

{
  // HOSTED (WebView2): the player is already inside the Conditioning Control
  // Panel, so the card would be selling them the desk they are sitting at.
  const box = new StubEl('section');
  const ctx = fakeCtx(fakeStore([], []), []);
  ctx.session = Object.assign({}, goodSession, { hosted: true });
  const h = recap.mount(box, ctx);
  ok(box.findAll('gg-recap-cta').length === 0, 'hosted -> no CTA at all');
  ok(box.findTag('a').length === 0, 'and the hosted recap grows no external link anywhere');
  ok(box.findAll('gg-recap-hero').length === 1, 'while the rest of the recap paints as it always did');
  h.unmount();
}

/* ===========================================================================
 * 8. THE WIRING — pinned as source, because these three lines are the whole
 *    reason the card has data to read.
 * ========================================================================= */
{
  const boot = read('boot.js');
  ok(/import \{ peerRenderLog, resetPeerRenderLog \} from '\.\/exec\/videos\.js'/.test(boot),
    'boot.js imports the peer-render seam from exec/videos.js');
  ok(/getPeerRenders: peerRenderLog/.test(boot), 'and hands it to every screen through ctx');

  // THE RESET POINT. It must be in attachMatch (a match STARTING) and nowhere
  // near detachMatch or clearForRecap, both of which run while the recap still
  // needs the list.
  const attach = boot.slice(boot.indexOf('function attachMatch('), boot.indexOf('function stashRoom('));
  ok(/resetPeerRenderLog\(\)/.test(attach), 'the log is reset inside attachMatch — at the START of a match');
  const detach = boot.slice(boot.indexOf('function detachMatch('), boot.indexOf('function mountHudNow('));
  ok(!/resetPeerRenderLog/.test(detach), 'and NOT in detachMatch, which runs when the player leaves the recap');
  const clear = boot.slice(boot.indexOf('function clearForRecap('), boot.indexOf('function forceRecap('));
  ok(!/resetPeerRenderLog/.test(clear), 'and NOT in clearForRecap, which runs moments BEFORE the card mounts');
  ok((boot.match(/resetPeerRenderLog\(\)/g) || []).length === 1, 'exactly one reset point exists');

  const vsrc = read('exec/videos.js');
  const stop = vsrc.slice(vsrc.indexOf('    stopWindows() {'), vsrc.indexOf('    windowCount:'));
  ok(!/peerRendered|resetPeerRenderLog/.test(stop), 'stopWindows() does not touch the log either');
}

{
  // The recap's stage defence is untouched — it is the fix for "clicking any
  // button does nothing", and this wave adds a card to that very screen.
  const rsrc = read('ui/screens/recap.js');
  ok(/assertStageClear\(ctx\?\.logger\)/.test(rsrc), 'recap.mount still runs assertStageClear first');
  ok(/replaceChildren\(\)/.test(rsrc), 'and still clears a dirty #gg-stage');
  // NO SHEETS, NO MODALS. The card is inline on the recap; nothing here may
  // open the z70 chrome, which is what made the end card unreachable before.
  ok(!/sheets|options\.open|showModal/.test(rsrc),
    'the report card opens NO sheet and NO modal — it is inline on the recap, by design');
}

{
  // Every string the card reaches for exists. A missing one renders "undefined"
  // in the middle of a safety control.
  for (const k of ['reportTitle', 'reportLead', 'reportPick', 'reportFlagged', 'reportPrivacy']) {
    ok(typeof S.recap[k] === 'string' && S.recap[k].length > 0, 'S.recap.' + k + ' exists');
  }
  for (const k of ['reasonHead', 'noteHead', 'notePlaceholder', 'noteNeeded', 'submit', 'submitting',
    'deduped', 'failed', 'retry', 'givenUp', 'cancel', 'evidenceNote']) {
    ok(typeof S.report[k] === 'string' && S.report[k].length > 0, 'S.report.' + k + ' exists');
  }
  ok(typeof S.report.done === 'function' && S.report.done('GR-X').indexOf('GR-X') >= 0,
    'S.report.done interpolates the id');
  ok(typeof S.report.noteHint === 'function' && S.report.noteHint(500).indexOf('500') >= 0,
    'S.report.noteHint interpolates the cap');
  ok(typeof S.report.thumbLabel === 'function'
    && S.report.thumbLabel('video', 2).indexOf('2') >= 0, 'S.report.thumbLabel interpolates the index');

  // The five labels map onto the five WIRE codes, exactly once each.
  const codes = S.report.reasons.map((r) => r.code);
  ok(codes.length === 5 && codes.every((c) => report.REPORT_REASONS.indexOf(c) >= 0),
    'every offered reason is a real wire code', codes.join(','));
  ok(new Set(codes).size === 5, 'and each appears once');
  ok(S.report.reasons.every((r) => r.label === r.label.toLowerCase()),
    'the labels are lowercase, like the rest of the furniture');
  ok(S.report.reasons.every((r) => r.label !== r.code),
    'and none of them is the raw wire code shown to a player');
}

{
  // The one CSS override the flag button needs, and the layer-order fact that
  // makes it necessary.
  const css = read('ui/screens.css');
  const html = read('index.html');
  ok(html.indexOf('ui/screens.css') < html.indexOf('exec/fx.css'),
    'screens.css is linked BEFORE fx.css — which is why the flag rule needs two class selectors');
  const rule = css.match(/\.gg-vwin \.gg-vwin-flag \{[^}]*\}/);
  ok(!!rule, 'ui/screens.css carries .gg-vwin .gg-vwin-flag');
  ok(!!rule && /display:\s*flex/.test(rule[0]),
    'and it un-hides the flag regardless of html[data-gg-vskip] (reporting is not a comfort preference)');
  ok(!!rule && /right:\s*8px/.test(rule[0]) && /left:\s*auto/.test(rule[0]),
    'and moves it to the opposite corner from the ✕');
  for (const cls of ['gg-recap-report', 'gg-report-thumb', 'gg-report-reason', 'gg-report-note',
    'gg-report-actions', 'gg-report-status',
    // the standalone CTA — an <a> wearing .gg-btn needs both of these or it
    // renders as underlined inline text in the middle of the card
    'gg-recap-cta', 'gg-recap-cta-lead', 'gg-recap-cta-link']) {
    ok(css.includes('.' + cls), 'ui/screens.css styles .' + cls);
  }
}

console.log(failures === 0 ? `PASS — ${n} checks` : `FAILED — ${failures}/${n} checks`);
process.exit(failures === 0 ? 0 : 1);
