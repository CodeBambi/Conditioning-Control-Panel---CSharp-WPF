// The P2P media-transfer protocol, end to end, under node.
//
//   node Resources/web/goon/test/selftest-transfer.js
//
// net/mediaChannel.js is transport-agnostic BY INJECTION, which is what makes this file possible:
// there is no RTCPeerConnection here, only the in-process bulk pair (loopbackTransport with
// `{bulk:true}`) and, where a test needs to hold the channel still, a stub that answers the same
// five functions. Everything the browser leg adds on top is 60 lines of RTCDataChannel wiring.
//
// A separate file rather than more selftest-net.js: the protocol is its own surface, and net's
// job is transports.
//
// THE HOSTILE-PEER CASES ARE THE POINT. Half of what follows never goes through our own `send()` —
// raw control frames and raw chunks are pushed straight onto the channel, because a peer that
// lies about a mime, a size or a tid is exactly what our own factories would never produce.

import {
  ACK_EVERY_BYTES, BULK_HIGH_WATER, CHUNK_BODY_BYTES, CHUNK_HEADER_BYTES, MAX_ARTIFACT_BYTES,
  STRAY_CHUNK_LIMIT, XFER_PROTO, XferCancel, XferDecline, XferFail, XferVerb,
  createMediaChannel, packChunk, unpackChunk,
} from '../net/mediaChannel.js';
import { createLoopbackPair, loopbackOptions } from '../net/loopbackTransport.js';

let failures = 0;
let n = 0;
function ok(cond, label, extra = '') {
  n++;
  if (!cond) { failures++; console.error(`  FAIL ${label} ${extra}`); }
}
const quiet = { info() {}, warn() {}, error() {}, log() {} };
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

async function until(fn, ms = 4000, step = 5) {
  const deadline = Date.now() + ms;
  for (;;) {
    if (fn()) return true;
    if (Date.now() >= deadline) return false;
    await sleep(step);
  }
}

// ------------------------------------------------------------------ fixtures

/** A deterministic, well-formed 64-hex "sha". The gate only ever checks the SHAPE. */
function shaOf(seed) {
  let out = '';
  let x = (seed * 2654435761) >>> 0;
  while (out.length < 64) {
    x = (x * 1664525 + 1013904223) >>> 0;
    out += x.toString(16).padStart(8, '0');
  }
  return out.slice(0, 64);
}

let sourceSeq = 0;

/**
 * One sendable artifact. `read` is deliberately ASYNC — the real one crosses the host bridge, and
 * the pump's `await` in the middle of a transfer is where the channel can die under it.
 */
function makeSource(bytes, o = {}) {
  const data = new Uint8Array(bytes);
  for (let i = 0; i < bytes; i++) data[i] = (i * 37 + 11) & 0xff;
  const reads = [];
  return {
    sha256: o.sha || shaOf(++sourceSeq),
    bytes,
    mime: o.mime || 'image/png',
    kind: o.kind || 'image',
    // The two ADVISORY fields (2026-08-05). Absent on almost every fixture here on purpose:
    // an offer without them is what a peer built before this change sends.
    origin: o.origin,
    codec: o.codec,
    data,
    reads,
    async read(offset, len) {
      reads.push(offset);
      if (o.onRead) await o.onRead(offset, len, reads.length);
      return data.buffer.slice(offset, offset + len);
    },
  };
}

/** A ReceivedStore (see the contract in net/mediaChannel.js) backed by memory. */
function makeStore(o = {}) {
  const committed = new Map();
  const partials = new Map();
  const blocked = new Set(o.blocked || []);
  const calls = { begin: 0, write: 0, commit: 0, abort: 0 };

  const store = {
    committed, partials, blocked, calls,
    failCommit: o.failCommit || null,
    refuseBegin: !!o.refuseBegin,
    refuseWrite: false,

    has(sha) { return committed.has(sha); },
    partialLength(sha) { const p = partials.get(sha); return p ? p.length : 0; },
    knowsBlocked(sha) { return blocked.has(sha); },

    /** `meta` is the offer's optional {origin, codec}; a store may ignore it and stay correct. */
    begin(sha, mime, bytes, meta) {
      calls.begin++;
      store.lastMeta = meta;
      if (store.refuseBegin) return false;
      if (!partials.has(sha)) partials.set(sha, { mime, bytes, chunks: [], length: 0 });
      return true;
    },

    write(sha, offset, buf) {
      calls.write++;
      if (store.refuseWrite) return false;
      const p = partials.get(sha);
      if (!p || offset !== p.length) return false;
      p.chunks.push(new Uint8Array(buf));
      p.length += buf.byteLength;
      return true;
    },

    async commit(sha) {
      calls.commit++;
      if (store.failCommit) return { ok: false, error: store.failCommit };
      const p = partials.get(sha);
      if (!p) return { ok: false, error: 'io-failed' };
      const all = new Uint8Array(p.length);
      let at = 0;
      for (const c of p.chunks) { all.set(c, at); at += c.length; }
      partials.delete(sha);
      const url = `https://ccp.cache/recv/${sha}`;
      committed.set(sha, { bytes: p.length, url, data: all });
      return { ok: true, url, bytes: p.length };
    },

    abort(sha) { calls.abort++; partials.delete(sha); },
  };
  return store;
}

/** Two transports + two channels, already open and already past the hello. */
async function makeRig(o = {}) {
  const pair = createLoopbackPair(loopbackOptions({
    latencyMs: o.latencyMs ?? 0, jitterMs: 0, guestClockSkewMs: 0, bulk: true, logger: quiet,
  }));
  // markConnected without pair.connect(): the clock sync is irrelevant to the bulk channel and
  // costs 20 round trips per rig.
  pair.host.markConnected();
  pair.guest.markConnected();

  const wire = (t) => ({
    sendBulk: (d) => t.sendBulk(d),
    bulkBufferedAmount: () => t.bulkBufferedAmount,
    bulkLowThreshold: () => t.bulkLowThreshold,
    onBulkMessage: (fn) => t.onBulkMessage(fn),
    onBulkStateChanged: (fn) => t.onBulkStateChanged(fn),
  });

  const hostStore = o.hostStore || makeStore();
  const guestStore = o.guestStore || makeStore();

  const hostCh = createMediaChannel(Object.assign(wire(pair.host), {
    store: hostStore, isHost: true, logger: quiet, tag: 'X:host',
    acceptOffers: o.hostAccepts || (() => true),
    // Absent by default: node cannot probe, so the DEFAULT rig is two peers that advertise no
    // decode list at all — which is also what every build before 2026-08-05 does.
    acceptsCodecs: o.hostCodecs,
    timeouts: o.timeouts, limits: o.limits,
  }));
  const guestCh = createMediaChannel(Object.assign(wire(pair.guest), {
    store: guestStore, isHost: false, logger: quiet, tag: 'X:guest',
    acceptOffers: o.guestAccepts || (() => true),
    acceptsCodecs: o.guestCodecs,
    timeouts: o.timeouts, limits: o.limits,
  }));

  // Passive taps: what each SIDE received, before either channel touched it.
  const hostSaw = [];
  const guestSaw = [];
  pair.host.onBulkMessage((raw) => { if (typeof raw === 'string') hostSaw.push(JSON.parse(raw)); });
  pair.guest.onBulkMessage((raw) => { if (typeof raw === 'string') guestSaw.push(JSON.parse(raw)); });

  hostCh.open({ alreadyOpen: pair.host.supportsBulk });
  guestCh.open({ alreadyOpen: pair.guest.supportsBulk });
  await until(() => hostCh.helloSeen && guestCh.helloSeen, 2000);

  const events = { host: [], guest: [] };
  const wireEvents = (ch, bucket) => {
    ch.onOffered((e) => bucket.push(Object.assign({ ev: 'offered' }, e)));
    ch.onLanded((e) => bucket.push(Object.assign({ ev: 'landed' }, e)));
    ch.onDeclined((e) => bucket.push(Object.assign({ ev: 'declined' }, e)));
    ch.onFailed((e) => bucket.push(Object.assign({ ev: 'failed' }, e)));
  };
  wireEvents(hostCh, events.host);
  wireEvents(guestCh, events.guest);

  const progress = { host: [], guest: [] };
  hostCh.onProgress((e) => progress.host.push(e));
  guestCh.onProgress((e) => progress.guest.push(e));

  return {
    pair, hostCh, guestCh, hostStore, guestStore, hostSaw, guestSaw, events, progress,
    /** Push a frame onto the wire WITHOUT going through send() — the hostile-peer door. */
    rawFromHost: (obj) => pair.host.sendBulk(JSON.stringify(obj)),
    rawChunkFromHost: (tid, offset, bytes) => pair.host.sendBulk(packChunk(tid, offset, new Uint8Array(bytes))),
    dispose() { hostCh.close(); guestCh.close(); pair.dispose(); },
  };
}

const sameBytes = (a, b) => {
  if (!a || !b || a.length !== b.length) return false;
  for (let i = 0; i < a.length; i++) if (a[i] !== b[i]) return false;
  return true;
};

// ============================================================ 1. framing
function testFraming() {
  ok(CHUNK_HEADER_BYTES === 8, 'the header is 8 bytes');
  ok(CHUNK_BODY_BYTES === 16376, 'body + header is exactly 16384', String(CHUNK_BODY_BYTES));
  ok(CHUNK_HEADER_BYTES + CHUNK_BODY_BYTES === 16384, 'the SCTP-safe ceiling');

  const body = new Uint8Array([1, 2, 3, 250, 251]);
  const frame = packChunk(0x01020304, 0xAABBCCDD, body);
  ok(frame.byteLength === CHUNK_HEADER_BYTES + 5, 'packed frame length', String(frame.byteLength));

  const dv = new DataView(frame);
  ok(dv.getUint8(0) === 0x04 && dv.getUint8(3) === 0x01, 'tid is LITTLE-endian on the wire');
  ok(dv.getUint8(4) === 0xDD && dv.getUint8(7) === 0xAA, 'offset is little-endian too');

  const back = unpackChunk(frame);
  ok(!!back && back.tid === 0x01020304, 'tid round-trips', String(back && back.tid));
  ok(!!back && back.offset === 0xAABBCCDD, 'offset round-trips (above 2^31, unsigned)', String(back && back.offset));
  ok(!!back && sameBytes(new Uint8Array(back.body), body), 'body round-trips');

  // The wire is untrusted: a frame too short to hold a header is dropped, never a throw.
  ok(unpackChunk(new ArrayBuffer(0)) === null, 'an empty frame unpacks to null');
  ok(unpackChunk(new ArrayBuffer(7)) === null, 'a truncated header unpacks to null');
  ok(unpackChunk('not a buffer') === null, 'a string unpacks to null');
  ok(unpackChunk(new ArrayBuffer(8)) !== null, 'a header with no body is legal (an empty chunk)');
  ok(unpackChunk(new Uint8Array(packChunk(7, 9, new Uint8Array([5])))).tid === 7,
    'a TypedArray view is accepted as well as an ArrayBuffer');
}

// ============================================================ 2. offer -> accept -> land
async function testHappyPath() {
  const rig = await makeRig();
  ok(rig.hostCh.helloSeen && rig.guestCh.helloSeen, 'both sides exchanged xfer_hello on open');
  ok(rig.hostSaw.some((m) => m.t === XferVerb.Hello && m.proto === XFER_PROTO),
    'the hello carries the proto version and the byte limits');
  ok(rig.hostSaw.find((m) => m.t === XferVerb.Hello).accepts.includes('video/mp4'),
    'and the mime allowlist');

  const src = makeSource(40000, { mime: 'image/webp', kind: 'image' });
  const tid = rig.hostCh.send(src);
  ok(typeof tid === 'number' && tid > 0, 'send() returns a tid', String(tid));
  ok(tid % 2 === 1, 'the HOST mints odd tids (disjoint spaces — see nextTid)', String(tid));

  ok(await until(() => rig.guestStore.committed.has(src.sha256), 4000), 'the artifact committed');
  const got = rig.guestStore.committed.get(src.sha256);
  ok(got.bytes === 40000, 'every byte arrived', String(got.bytes));
  ok(sameBytes(got.data, src.data), 'and they are the SAME bytes');
  ok(rig.guestStore.calls.commit === 1, 'commit called exactly once', String(rig.guestStore.calls.commit));
  ok(rig.guestStore.calls.begin === 1, 'begin called exactly once', String(rig.guestStore.calls.begin));
  ok(rig.guestStore.calls.abort === 0, 'nothing was aborted');

  ok(await until(() => rig.events.host.some((e) => e.ev === 'landed'), 2000), 'the sender saw it land');
  const outLanded = rig.events.host.find((e) => e.ev === 'landed');
  ok(outLanded.direction === 'out' && outLanded.sha256 === src.sha256, 'onLanded(out) names the artifact');
  const inLanded = rig.events.guest.find((e) => e.ev === 'landed');
  ok(!!inLanded && inLanded.direction === 'in', 'the receiver saw it land too');
  ok(!!inLanded && inLanded.url === `https://ccp.cache/recv/${src.sha256}`, 'with the store\'s url', inLanded && inLanded.url);

  ok(rig.guestSaw.some((m) => m.t === XferVerb.Offer && m.mime === 'image/webp'), 'an xfer_offer crossed');
  ok(rig.hostSaw.some((m) => m.t === XferVerb.Accept && m.from_offset === 0), 'answered by xfer_accept{from_offset:0}');
  ok(rig.guestSaw.some((m) => m.t === XferVerb.End), 'closed by xfer_end');
  ok(rig.hostSaw.some((m) => m.t === XferVerb.Done), 'and confirmed by xfer_done');

  ok(rig.hostCh.stats().outbound === 0 && rig.guestCh.stats().inbound === 0, 'both slots freed');
  ok(rig.hostCh.stats().sessionBytesOut === 40000, 'sender counted the session bytes',
    String(rig.hostCh.stats().sessionBytesOut));
  ok(rig.guestCh.stats().sessionBytesIn === 40000, 'receiver counted them too');
  ok(rig.hostCh.stats().lastRateBps > 0, 'a completed transfer leaves a measured throughput');

  // The sender is not allowed to run two at once — the queue depends on that answer.
  const a = rig.hostCh.send(makeSource(1000));
  const b = rig.hostCh.send(makeSource(1000));
  ok(typeof a === 'number' && b === null, 'MAX_CONCURRENT_OUT is honoured (second send -> null)');
  rig.dispose();
}

// ============================================================ 3. EVERY decline reason
async function testDeclines() {
  // Each case gets its own rig: a decline leaves state behind (a busy slot, a quota flag) and the
  // ORDER of the gate is the contract, so cases must not be able to contaminate each other.
  async function declineCase(label, build) {
    const rig = await makeRig(build.rig || {});
    if (build.setup) build.setup(rig);
    const offer = Object.assign({
      t: XferVerb.Offer, v: XFER_PROTO, tid: 1001,
      sha256: shaOf(9000), bytes: 5000, mime: 'image/png', kind: 'image',
    }, build.offer || {});
    rig.rawFromHost(offer);
    const seen = await until(() => rig.hostSaw.some((m) => m.t === XferVerb.Decline), 2000);
    const msg = rig.hostSaw.find((m) => m.t === XferVerb.Decline);
    ok(seen && msg && msg.why === build.want, `decline: ${label} -> '${build.want}'`,
      msg ? msg.why : 'nothing came back');
    ok(!seen || msg.tid === offer.tid, `decline: ${label} names the offered tid`);
    if (build.after) build.after(rig);
    rig.dispose();
  }

  // 1. the feature is off
  await declineCase('consent withdrawn / caps off', {
    rig: { guestAccepts: () => false }, want: XferDecline.Off,
    after: (rig) => ok(rig.guestStore.calls.begin === 0, 'decline:off never touched the store'),
  });

  // 2. mime allowlist, and kind/mime agreement
  await declineCase('a mime nobody carries', { offer: { mime: 'application/zip' }, want: XferDecline.BadMime });
  await declineCase('an image mime declared as a video',
    { offer: { mime: 'image/png', kind: 'video' }, want: XferDecline.BadMime });
  await declineCase('no kind at all', { offer: { kind: undefined }, want: XferDecline.BadMime });

  // 3. the sha is the only thing that ever becomes a filename
  await declineCase('a sha that is not a sha', { offer: { sha256: '../../etc/passwd' }, want: XferDecline.BadMime });
  await declineCase('an UPPERCASE sha', { offer: { sha256: shaOf(3).toUpperCase() }, want: XferDecline.BadMime });
  await declineCase('a 63-character sha', { offer: { sha256: shaOf(3).slice(0, 63) }, want: XferDecline.BadMime });

  // 4. size
  await declineCase('over MAX_ARTIFACT_BYTES', { offer: { bytes: MAX_ARTIFACT_BYTES + 1 }, want: XferDecline.TooBig });
  await declineCase('zero bytes', { offer: { bytes: 0 }, want: XferDecline.TooBig });
  await declineCase('a negative size', { offer: { bytes: -1 }, want: XferDecline.TooBig });
  await declineCase('a fractional size', { offer: { bytes: 1.5 }, want: XferDecline.TooBig });
  await declineCase('a size that is a string', { offer: { bytes: '5000' }, want: XferDecline.TooBig });

  // 5. blocklisted, from LOCAL knowledge only
  await declineCase('a hash we already know is blocked', {
    offer: { sha256: shaOf(77) },
    setup: (rig) => rig.guestStore.blocked.add(shaOf(77)),
    want: XferDecline.Blocked,
  });

  // 6. `have` — the cross-session reuse SUCCESS
  await declineCase('an artifact we already hold', {
    offer: { sha256: shaOf(55) },
    setup: (rig) => rig.guestStore.committed.set(shaOf(55), { bytes: 1, url: 'x', data: new Uint8Array(1) }),
    want: XferDecline.Have,
    after: (rig) => ok(rig.guestStore.calls.begin === 0, "decline:have costs the store nothing"),
  });

  // 7. one at a time
  await declineCase('a second offer while one is inbound', {
    setup: (rig) => rig.rawFromHost({
      t: XferVerb.Offer, v: XFER_PROTO, tid: 1003, sha256: shaOf(31), bytes: 5000,
      mime: 'image/png', kind: 'image',
    }),
    want: XferDecline.Busy,
  });

  // 8. the per-match, per-direction budget
  await declineCase('over the session byte budget', {
    rig: { limits: { maxSessionBytes: 4096, maxArtifactBytes: 1 << 20 } },
    want: XferDecline.Quota,
  });

  // And the ordering that matters most: `have` is answered even when we are BUSY, because it costs
  // nothing and it is the whole cross-session win.
  {
    const rig = await makeRig();
    rig.guestStore.committed.set(shaOf(101), { bytes: 1, url: 'x', data: new Uint8Array(1) });
    rig.rawFromHost({
      t: XferVerb.Offer, v: XFER_PROTO, tid: 2001, sha256: shaOf(102), bytes: 5000,
      mime: 'image/png', kind: 'image',
    });
    await until(() => rig.guestCh.stats().inbound === 1, 2000);
    rig.hostSaw.length = 0;
    rig.rawFromHost({
      t: XferVerb.Offer, v: XFER_PROTO, tid: 2003, sha256: shaOf(101), bytes: 5000,
      mime: 'image/png', kind: 'image',
    });
    await until(() => rig.hostSaw.some((m) => m.t === XferVerb.Decline), 2000);
    const why = (rig.hostSaw.find((m) => m.t === XferVerb.Decline) || {}).why;
    ok(why === XferDecline.Have, "gate order: 'have' beats 'busy'", String(why));
    rig.dispose();
  }

  // A store that refuses to open the partial is a FAIL, not a decline — we said yes and then could
  // not honour it, and the sender must learn that as store_full.
  {
    const rig = await makeRig({ guestStore: makeStore({ refuseBegin: true }) });
    rig.rawFromHost({
      t: XferVerb.Offer, v: XFER_PROTO, tid: 3001, sha256: shaOf(7), bytes: 5000,
      mime: 'image/png', kind: 'image',
    });
    ok(await until(() => rig.hostSaw.some((m) => m.t === XferVerb.Fail), 2000), 'a refused begin fails the transfer');
    ok((rig.hostSaw.find((m) => m.t === XferVerb.Fail) || {}).why === XferFail.StoreFull, "…as 'store_full'");
    ok(rig.guestCh.stats().inbound === 0, 'and no slot is left behind');
    rig.dispose();
  }
}

// ============================================================ 4. hostile chunks
async function testHostileChunks() {
  // --- strays: a peer cannot stream bytes we never agreed to receive ----------------
  {
    const rig = await makeRig();
    for (let i = 0; i < STRAY_CHUNK_LIMIT - 1; i++) rig.rawChunkFromHost(4242, i * 16, 16);
    await until(() => rig.guestCh.stats().strays === STRAY_CHUNK_LIMIT - 1, 3000);
    ok(rig.guestCh.stats().strays === STRAY_CHUNK_LIMIT - 1, 'stray chunks are counted, not written',
      String(rig.guestCh.stats().strays));
    ok(rig.guestStore.calls.write === 0, 'and never reach the store');
    ok(!rig.hostSaw.some((m) => m.t === XferVerb.Cancel), 'under the limit we say nothing');

    rig.rawChunkFromHost(4242, 9999, 16);
    ok(await until(() => rig.hostSaw.some((m) => m.t === XferVerb.Cancel), 2000),
      `at ${STRAY_CHUNK_LIMIT} strays we send xfer_cancel`);
    ok((rig.hostSaw.find((m) => m.t === XferVerb.Cancel) || {}).why === XferCancel.Stray, "…with why 'stray'");
    rig.dispose();
  }

  // --- over-run: writing stops at exactly the offered `bytes` -----------------------
  {
    const rig = await makeRig();
    rig.rawFromHost({
      t: XferVerb.Offer, v: XFER_PROTO, tid: 5001, sha256: shaOf(12), bytes: 100,
      mime: 'image/png', kind: 'image',
    });
    ok(await until(() => rig.hostSaw.some((m) => m.t === XferVerb.Accept), 2000), 'a legal offer is accepted');
    rig.rawChunkFromHost(5001, 0, 80);
    await until(() => rig.guestStore.partialLength(shaOf(12)) === 80, 2000);
    rig.rawChunkFromHost(5001, 80, 80);            // 160 > the offered 100
    ok(await until(() => rig.hostSaw.some((m) => m.t === XferVerb.Fail), 2000), 'the over-run fails the transfer');
    ok((rig.hostSaw.find((m) => m.t === XferVerb.Fail) || {}).why === XferFail.TooBig, "…as 'too_big'");
    ok(rig.guestStore.calls.abort >= 1, 'and the partial is thrown away');
    ok(rig.guestCh.stats().inbound === 0, 'the slot is released');
    rig.dispose();
  }

  // --- offset discipline on an ordered channel -------------------------------------
  {
    const rig = await makeRig();
    rig.rawFromHost({
      t: XferVerb.Offer, v: XFER_PROTO, tid: 5011, sha256: shaOf(13), bytes: 200,
      mime: 'image/png', kind: 'image',
    });
    await until(() => rig.guestCh.stats().inbound === 1, 2000);

    rig.rawChunkFromHost(5011, 0, 50);
    await until(() => rig.guestStore.partialLength(shaOf(13)) === 50, 2000);
    rig.rawChunkFromHost(5011, 0, 50);             // a duplicate of what we already have
    await sleep(60);
    ok(rig.guestStore.partialLength(shaOf(13)) === 50, 'a duplicated chunk is dropped, not appended',
      String(rig.guestStore.partialLength(shaOf(13))));
    ok(rig.guestCh.stats().inbound === 1, 'and does not kill the transfer');

    rig.rawChunkFromHost(5011, 120, 20);           // a hole the store could not honour
    ok(await until(() => rig.hostSaw.some((m) => m.t === XferVerb.Fail), 2000), 'an offset GAP fails the transfer');
    ok((rig.hostSaw.find((m) => m.t === XferVerb.Fail) || {}).why === XferFail.Io, "…as 'io'");
    rig.dispose();
  }

  // --- a store write that fails ------------------------------------------------------
  {
    const rig = await makeRig();
    rig.rawFromHost({
      t: XferVerb.Offer, v: XFER_PROTO, tid: 5021, sha256: shaOf(14), bytes: 200,
      mime: 'image/png', kind: 'image',
    });
    await until(() => rig.guestCh.stats().inbound === 1, 2000);
    rig.guestStore.refuseWrite = true;
    rig.rawChunkFromHost(5021, 0, 50);
    ok(await until(() => rig.hostSaw.some((m) => m.t === XferVerb.Fail), 2000), 'a refused write fails the transfer');
    ok((rig.hostSaw.find((m) => m.t === XferVerb.Fail) || {}).why === XferFail.Io, "…as 'io'");
    rig.dispose();
  }
}

// ============================================================ 5. ack cadence + progress
async function testAckCadence() {
  const rig = await makeRig();
  const bytes = 700000;                             // ~2.7 ack windows
  const src = makeSource(bytes);
  rig.hostCh.send(src);

  ok(await until(() => rig.guestStore.committed.has(src.sha256), 8000), '700 KB landed');

  const acks = rig.hostSaw.filter((m) => m.t === XferVerb.Ack);
  ok(acks.length === 2, `acks every ${ACK_EVERY_BYTES} bytes -> 2 for 700 KB`, String(acks.length));
  ok(acks[0].offset >= ACK_EVERY_BYTES && acks[0].offset < ACK_EVERY_BYTES + CHUNK_BODY_BYTES,
    'the first ack lands on the first window boundary', String(acks[0] && acks[0].offset));
  ok(acks[1].offset > acks[0].offset, 'offsets ascend');
  ok(acks.every((a) => a.offset <= bytes), 'and never claim more than was offered');
  ok(!rig.hostSaw.some((m) => m.t === XferVerb.Ack && m.offset === bytes),
    'the final bytes are confirmed by xfer_done, not by a redundant ack');

  const inProgress = rig.progress.guest.filter((p) => p.direction === 'in');
  const outProgress = rig.progress.host.filter((p) => p.direction === 'out');
  const chunks = Math.ceil(bytes / CHUNK_BODY_BYTES);
  ok(inProgress.length === chunks, 'one receiver progress event per chunk',
    `${inProgress.length} vs ${chunks}`);
  ok(outProgress.length === chunks, 'and one per chunk on the sender');
  ok(inProgress[inProgress.length - 1].transferred === bytes, 'the last one reports the whole artifact');
  ok(inProgress[inProgress.length - 1].bytes === bytes, 'alongside the total it is measured against');
  ok(outProgress.every((p, i) => i === 0 || p.transferred > outProgress[i - 1].transferred),
    'sender progress is monotonic');
  rig.dispose();
}

// ============================================================ 6. resume after a reopen
async function testResume() {
  let rig = null;
  let dropped = false;
  const bytes = 400000;

  // Drop the bulk channel mid-read. The pump is `await`ing us, so this is exactly the instant the
  // real channel dies under a transfer.
  const src = makeSource(bytes, {
    onRead: async (offset) => {
      if (!dropped && offset >= 100000 && rig) {
        dropped = true;
        rig.pair.host.dropBulkChannel();
        rig.pair.guest.dropBulkChannel();
      }
      await Promise.resolve();
    },
  });

  rig = await makeRig();
  rig.hostCh.send(src);

  ok(await until(() => dropped && !rig.hostCh.isOpen, 4000), 'the channel dropped mid-transfer');
  await sleep(80);
  ok(rig.hostCh.stats().outbound === 1, 'the sender PAUSED — the slot is kept, not cancelled',
    String(rig.hostCh.stats().outbound));
  ok(rig.guestCh.stats().inbound === 1, 'the receiver kept its slot too');
  const held = rig.guestStore.partialLength(src.sha256);
  ok(held > 0 && held < bytes, 'and a real partial is on disk', `${held} of ${bytes}`);
  ok(rig.guestStore.calls.abort === 0, 'nothing was aborted — a drop is not a failure');

  const readsBefore = src.reads.length;
  rig.hostSaw.length = 0;
  rig.guestSaw.length = 0;

  rig.pair.host.restoreBulkChannel();
  rig.pair.guest.restoreBulkChannel();

  ok(await until(() => rig.guestStore.committed.has(src.sha256), 8000), 'the transfer finished after the reopen');
  const finished = rig.guestStore.committed.get(src.sha256);
  ok(finished.bytes === bytes, 'with the whole artifact', String(finished.bytes));
  ok(sameBytes(finished.data, src.data), 'and byte-identical content across the seam');

  const reOffer = rig.guestSaw.find((m) => m.t === XferVerb.Offer);
  ok(!!reOffer && reOffer.sha256 === src.sha256, 'the sender re-offered the SAME artifact');
  const accept = rig.hostSaw.find((m) => m.t === XferVerb.Accept);
  ok(!!accept && accept.from_offset === held, 'the receiver answered from_offset = what it holds',
    `${accept && accept.from_offset} vs ${held}`);

  const afterReads = src.reads.slice(readsBefore);
  ok(afterReads.length > 0, 'the resumed half really was read');
  ok(afterReads.every((off) => off >= held), 'NOTHING below from_offset was re-read or re-sent',
    `min ${Math.min(...afterReads)} vs ${held}`);
  rig.dispose();
}

// ============================================================ 7. cancel + terminal close
async function testCancel() {
  {
    const rig = await makeRig();
    const src = makeSource(400000, { onRead: async () => { await sleep(3); } });
    const tid = rig.hostCh.send(src);
    ok(await until(() => rig.guestCh.stats().inbound === 1, 3000), 'the receiver took the offer');

    ok(rig.hostCh.cancel(tid, XferCancel.Superseded) === true, 'cancel() finds the slot');
    ok(rig.hostCh.cancel(9999) === false, 'and refuses a tid it does not own');
    ok(await until(() => rig.guestCh.stats().inbound === 0, 3000), 'the receiver dropped its slot');
    ok(rig.guestStore.calls.abort >= 1, 'and told the store to throw the partial away');
    ok(!rig.guestStore.committed.has(src.sha256), 'nothing was committed');
    ok(rig.events.host.some((e) => e.ev === 'failed' && e.why === XferCancel.Superseded),
      'the sender reports the cancel as a failure of that slot');
    rig.dispose();
  }

  // Terminal close: everything in flight dies, everything committed survives.
  {
    const rig = await makeRig();
    const done = makeSource(2000);
    rig.hostCh.send(done);
    await until(() => rig.guestStore.committed.has(done.sha256), 4000);

    const live = makeSource(400000, { onRead: async () => { await sleep(3); } });
    rig.hostCh.send(live);
    await until(() => rig.guestCh.stats().inbound === 1, 3000);

    rig.hostCh.close(XferCancel.MatchOver);
    ok(await until(() => rig.guestCh.stats().inbound === 0, 3000), 'close() cancels the in-flight transfer');
    ok(rig.guestStore.committed.has(done.sha256), 'the artifact that already committed is KEPT');
    ok(!rig.guestStore.partials.has(live.sha256), 'the partial is gone');
    ok(rig.hostCh.send(makeSource(1000)) === null, 'a closed channel takes no more work');
    rig.guestCh.close();
    rig.pair.dispose();
  }
}

// ============================================================ 8. backpressure
//
// A stub rather than the loopback pair: the whole point is holding `bulkBufferedAmount` still, and
// an in-process pair drains on the next microtask.
function stubTransport() {
  const msg = new Set();
  const state = new Set();
  const sent = [];
  const st = {
    sent,
    buffered: 0,
    failSend: false,
    sendBulk(d) {
      if (st.failSend) return false;
      sent.push(d);
      if (d instanceof ArrayBuffer) st.buffered += d.byteLength;
      return true;
    },
    bulkBufferedAmount: () => st.buffered,
    bulkLowThreshold: () => 1 << 18,
    onBulkMessage: (fn) => { msg.add(fn); return () => msg.delete(fn); },
    onBulkStateChanged: (fn) => { state.add(fn); return () => state.delete(fn); },
    deliver(obj) { const raw = JSON.stringify(obj); for (const f of Array.from(msg)) f(raw); },
    fireState(s) { for (const f of Array.from(state)) f(s); },
    chunks: () => sent.filter((d) => d instanceof ArrayBuffer),
    /** Only the control frames — `sent` also holds the binary chunks. */
    controls: () => sent.filter((d) => typeof d === 'string').map((d) => JSON.parse(d)),
  };
  return st;
}

async function testBackpressure() {
  const st = stubTransport();
  const ch = createMediaChannel(Object.assign({}, st, {
    store: makeStore(), isHost: true, logger: quiet, tag: 'X:bp',
    acceptOffers: () => true,
  }));
  ch.open({ alreadyOpen: true });
  st.deliver({ t: XferVerb.Hello, v: XFER_PROTO, proto: XFER_PROTO });
  ok(ch.helloSeen, 'the stub peer said hello');

  const src = makeSource(4 * 1024 * 1024);
  const tid = ch.send(src);
  st.deliver({ t: XferVerb.Accept, v: XFER_PROTO, tid, from_offset: 0 });

  ok(await until(() => st.buffered >= BULK_HIGH_WATER, 3000), 'the pump fills toward high water');
  await sleep(150);
  const atHighWater = st.chunks().length;
  ok(st.buffered >= BULK_HIGH_WATER, 'and stops there', String(st.buffered));
  ok(st.buffered < BULK_HIGH_WATER + CHUNK_BODY_BYTES * 2,
    'never runs far past it (one chunk of overshoot at most)', String(st.buffered));
  ok(atHighWater * (CHUNK_HEADER_BYTES + CHUNK_BODY_BYTES) === st.buffered, 'the arithmetic checks out');

  // The channel drains but nobody fires `bufferedamountlow` — the 50 ms watchdog must still get
  // the transfer moving, because a missed event is otherwise a transfer that never finishes.
  st.buffered = 0;
  ok(await until(() => st.chunks().length > atHighWater, 2000), 'the WATCHDOG alone restarts the pump');
  const afterWatchdog = st.chunks().length;

  // And the event path, which is what makes the common case immediate.
  st.buffered = 0;
  st.fireState('low');
  await sleep(10);
  ok(st.chunks().length > afterWatchdog, "a 'low' bulk state kicks the pump straight away");

  // sendBulk === false is a HALT, not a dropped chunk: the offset must not advance past bytes that
  // never left. (The transport silently drops frames in non-open states — that is the whole reason
  // the boolean exists.)
  st.buffered = 0;
  st.failSend = true;
  const stuckAt = st.chunks().length;
  await sleep(150);
  ok(st.chunks().length === stuckAt, 'sendBulk returning false stops the pump dead',
    String(st.chunks().length - stuckAt));

  st.failSend = false;
  ok(await until(() => st.chunks().length > stuckAt, 2000), 'and it resumes when the channel comes back');

  // Every chunk that was actually sent is contiguous and correctly framed — no gap was skipped.
  const offsets = st.chunks().map((c) => unpackChunk(c).offset);
  let contiguous = true;
  for (let i = 1; i < offsets.length; i++) if (offsets[i] !== offsets[i - 1] + CHUNK_BODY_BYTES) contiguous = false;
  ok(contiguous, 'the offsets the receiver sees are gapless across every halt');
  ok(st.chunks().every((c) => unpackChunk(c).tid === tid), 'and every chunk carries the right tid');

  ch.close();
}

// ============================================================ 9. host-side hash verification
async function testHashMismatch() {
  const rig = await makeRig({ guestStore: makeStore({ failCommit: 'hash-mismatch' }) });
  const src = makeSource(30000);
  rig.hostCh.send(src);

  ok(await until(() => rig.hostSaw.some((m) => m.t === XferVerb.Fail), 5000),
    'a commit-time hash mismatch fails the transfer');
  ok((rig.hostSaw.find((m) => m.t === XferVerb.Fail) || {}).why === XferFail.HashMismatch,
    "…with why 'hash_mismatch' — the page's hash was only ever a claim");
  ok(rig.guestStore.calls.abort >= 1, 'the temp is deleted');
  ok(rig.guestCh.neverAcceptSet.has(src.sha256), 'the receiver will NEVER accept that sha again this session');
  ok(rig.hostCh.neverOfferSet.has(src.sha256), 'and the sender stops offering it');

  // Which the gate must actually enforce, not merely record.
  rig.hostSaw.length = 0;
  rig.rawFromHost({
    t: XferVerb.Offer, v: XFER_PROTO, tid: 7001, sha256: src.sha256, bytes: 30000,
    mime: 'image/png', kind: 'image',
  });
  ok(await until(() => rig.hostSaw.some((m) => m.t === XferVerb.Decline), 2000), 'a re-offer is refused');
  ok((rig.hostSaw.find((m) => m.t === XferVerb.Decline) || {}).why === XferDecline.Blocked,
    '…as blocked, which reads to the sender exactly like a moderation block (no evasion oracle)');

  // The other store verdicts map onto their own reasons.
  const cases = [
    ['bad-format', XferFail.Magic, 'a magic-byte mismatch'],
    ['cap-reached', XferFail.StoreFull, 'a full store'],
    ['io-failed', XferFail.Io, 'a disk error'],
  ];
  for (const [storeError, want, label] of cases) {
    const r = await makeRig({ guestStore: makeStore({ failCommit: storeError }) });
    const s = makeSource(20000);
    r.hostCh.send(s);
    await until(() => r.hostSaw.some((m) => m.t === XferVerb.Fail), 5000);
    const why = (r.hostSaw.find((m) => m.t === XferVerb.Fail) || {}).why;
    ok(why === want, `commit '${storeError}' (${label}) -> xfer_fail '${want}'`, String(why));
    r.dispose();
  }
  rig.dispose();
}

// ============================================================ 10. both directions at once
async function testBidirectional() {
  const rig = await makeRig();
  const fromHost = makeSource(120000, { mime: 'image/jpeg', kind: 'image' });
  const fromGuest = makeSource(90000, { mime: 'video/mp4', kind: 'video' });

  const hostTid = rig.hostCh.send(fromHost);
  const guestTid = rig.guestCh.send(fromGuest);
  ok(typeof hostTid === 'number' && typeof guestTid === 'number', 'both sides may offer at once');
  ok(hostTid % 2 === 1 && guestTid % 2 === 0, 'and their tid spaces are DISJOINT (odd host / even guest)',
    `${hostTid} / ${guestTid}`);

  ok(await until(() => rig.guestStore.committed.has(fromHost.sha256)
    && rig.hostStore.committed.has(fromGuest.sha256), 8000), 'both artifacts landed');
  ok(sameBytes(rig.guestStore.committed.get(fromHost.sha256).data, fromHost.data), 'host -> guest bytes intact');
  ok(sameBytes(rig.hostStore.committed.get(fromGuest.sha256).data, fromGuest.data), 'guest -> host bytes intact');
  ok(rig.hostCh.stats().sessionBytesIn === 90000 && rig.hostCh.stats().sessionBytesOut === 120000,
    'the two directions are counted separately',
    `${rig.hostCh.stats().sessionBytesIn}/${rig.hostCh.stats().sessionBytesOut}`);
  ok(rig.hostCh.stats().outbound === 0 && rig.hostCh.stats().inbound === 0, 'and both slots freed');
  rig.dispose();
}

// ============================================================ 11. hello timeout -> dormant
async function testHelloTimeout() {
  const st = stubTransport();
  const ch = createMediaChannel(Object.assign({}, st, {
    store: makeStore(), isHost: false, logger: quiet, tag: 'X:dormant',
    acceptOffers: () => true,
    timeouts: { helloMs: 40, watchdogMs: 10 },
  }));
  ch.open({ alreadyOpen: true });

  ok(st.controls().some((m) => m.t === XferVerb.Hello),
    'we say hello the moment the channel opens');
  ok(ch.isDormant === false, 'and are not dormant yet');
  ok(ch.send(makeSource(1000)) === null, 'but will not offer anything before the peer answers');

  ok(await until(() => ch.isDormant, 1000), 'a peer that never says hello leaves us DORMANT');
  ok(ch.helloSeen === false, 'with no hello ever seen');

  // Dormant is not "broken": an offer from that peer is answered, and answered with `off`.
  st.sent.length = 0;
  st.deliver({
    t: XferVerb.Offer, v: XFER_PROTO, tid: 8001, sha256: shaOf(500), bytes: 1000,
    mime: 'image/png', kind: 'image',
  });
  await sleep(20);
  const reply = st.controls().find((m) => m.t === XferVerb.Decline);
  ok(!!reply && reply.why === XferDecline.Off, "a dormant side declines with 'off'", reply && reply.why);
  ok(ch.send(makeSource(1000)) === null, 'and never offers anything itself');

  // A late hello wakes it up — the peer was slow, not absent.
  st.deliver({ t: XferVerb.Hello, v: XFER_PROTO, proto: XFER_PROTO });
  ok(ch.isDormant === false && ch.helloSeen === true, 'a late hello un-dormants the channel');
  ok(typeof ch.send(makeSource(1000)) === 'number', 'and offers start flowing');
  ch.close();
}

// ============================================================ 12. offer + stall deadlines
async function testDeadlines() {
  // Nobody answers the offer.
  {
    const st = stubTransport();
    const ch = createMediaChannel(Object.assign({}, st, {
      store: makeStore(), isHost: true, logger: quiet, tag: 'X:offer-to',
      acceptOffers: () => true, timeouts: { offerMs: 60, watchdogMs: 10 },
    }));
    ch.open({ alreadyOpen: true });
    st.deliver({ t: XferVerb.Hello, v: XFER_PROTO, proto: XFER_PROTO });

    const failed = [];
    ch.onFailed((e) => failed.push(e));
    const tid = ch.send(makeSource(5000));
    ok(await until(() => ch.stats().outbound === 0, 1500), 'an unanswered offer times out');
    const cancel = st.controls().find((m) => m.t === XferVerb.Cancel);
    ok(!!cancel && cancel.tid === tid && cancel.why === XferCancel.Timeout,
      "…and we send xfer_cancel:'timeout' rather than waiting forever");
    ok(failed.length === 1 && failed[0].direction === 'out', 'the queue is told, so it can draw a replacement');
    ok(typeof ch.send(makeSource(5000)) === 'number', 'and the slot is free for the next pick');
    ch.close();
  }

  // Accepted, then the peer goes quiet.
  {
    const st = stubTransport();
    const ch = createMediaChannel(Object.assign({}, st, {
      store: makeStore(), isHost: true, logger: quiet, tag: 'X:stall',
      acceptOffers: () => true, timeouts: { stallMs: 60, watchdogMs: 10 },
    }));
    ch.open({ alreadyOpen: true });
    st.deliver({ t: XferVerb.Hello, v: XFER_PROTO, proto: XFER_PROTO });
    const tid = ch.send(makeSource(4 * 1024 * 1024));
    st.deliver({ t: XferVerb.Accept, v: XFER_PROTO, tid, from_offset: 0 });
    await until(() => st.buffered >= BULK_HIGH_WATER, 2000);
    // Buffered stays at high water: no drain, no acks, nothing moving.
    ok(await until(() => ch.stats().outbound === 0, 2000), 'a stalled transfer is cancelled');
    const cancel = st.controls().find((m) => m.t === XferVerb.Cancel);
    ok(!!cancel && cancel.why === XferCancel.Timeout, "…as xfer_cancel:'timeout'");
    ch.close();
  }
}

// ============================================================ 13. the sender's own guards
async function testSenderGuards() {
  const rig = await makeRig();
  ok(rig.hostCh.send(null) === null, 'send(null) is refused');
  ok(rig.hostCh.send({ sha256: 'nope', bytes: 10, mime: 'image/png', kind: 'image', read: () => null }) === null,
    'a malformed sha is refused BEFORE it reaches the wire');
  ok(rig.hostCh.send(makeSource(10, { mime: 'application/zip' })) === null, 'an unlistable mime is refused');
  ok(rig.hostCh.send(makeSource(10, { mime: 'image/png', kind: 'video' })) === null,
    'a kind that disagrees with the mime is refused');
  ok(rig.hostCh.send(Object.assign(makeSource(10), { bytes: MAX_ARTIFACT_BYTES + 1 })) === null,
    'an over-cap artifact is refused');
  ok(rig.hostCh.send(Object.assign(makeSource(10), { read: null })) === null, 'a source with no read() is refused');
  ok(rig.guestSaw.filter((m) => m.t === XferVerb.Offer).length === 0,
    'none of that put a single frame on the wire');
  rig.dispose();
}

/* ============================================================ 14. origin + codec on the offer
 *
 * Two optional fields, one rule: THEY MAY NEVER CHANGE WHETHER A TRANSFER HAPPENS. `origin` is
 * downstream taste (keep a converted gif loop out of the VIDEO lane while real footage exists) and
 * `codec` is only ever read by the SENDER's queue before it offers. An offer carrying neither —
 * which is every offer a peer built before 2026-08-05 sends — must behave exactly as it always has.
 */
async function testOfferMetadata() {
  // --- carried end to end, and handed to the store as advisory metadata
  {
    const rig = await makeRig();
    const src = makeSource(2048, { mime: 'video/mp4', kind: 'video', origin: 'gif', codec: 'avc1' });
    rig.hostCh.send(src);
    ok(await until(() => rig.guestStore.committed.has(src.sha256), 3000), 'the gif-origin clip landed');

    const offer = rig.guestSaw.find((m) => m.t === XferVerb.Offer);
    ok(offer && offer.origin === 'gif' && offer.codec === 'avc1', 'the offer carries origin + codec',
      JSON.stringify(offer && { origin: offer.origin, codec: offer.codec }));
    ok(rig.guestStore.lastMeta && rig.guestStore.lastMeta.origin === 'gif'
      && rig.guestStore.lastMeta.codec === 'avc1',
      'and store.begin() is handed both as its optional 4th argument');

    const landedIn = rig.events.guest.find((e) => e.ev === 'landed' && e.direction === 'in');
    ok(landedIn && landedIn.origin === 'gif',
      "the INBOUND landing reports it, which is how boot reaches exec/media.js addReceived");
    const landedOut = rig.events.host.find((e) => e.ev === 'landed' && e.direction === 'out');
    ok(landedOut && landedOut.origin === 'gif', 'and so does the outbound one, for the queue\'s own larder');
    rig.dispose();
  }

  // --- an offer with NEITHER field is an old peer, and lands identically
  {
    const rig = await makeRig();
    const src = makeSource(2048, { mime: 'video/mp4', kind: 'video' });
    rig.hostCh.send(src);
    ok(await until(() => rig.guestStore.committed.has(src.sha256), 3000),
      'an offer with no origin and no codec transfers exactly as before');
    const offer = rig.guestSaw.find((m) => m.t === XferVerb.Offer);
    ok(offer && offer.origin === undefined && offer.codec === undefined,
      'the fields are OMITTED rather than sent empty — an old parser sees the frame it expects');
    const landedIn = rig.events.guest.find((e) => e.ev === 'landed' && e.direction === 'in');
    ok(landedIn && landedIn.origin === '', 'and the landing reports origin "" — which reads as footage');
    rig.dispose();
  }

  // --- a hostile peer cannot smuggle anything through either field
  {
    const rig = await makeRig();
    const sha = shaOf(9911);
    rig.rawFromHost({
      t: XferVerb.Offer, v: XFER_PROTO, tid: 777, sha256: sha, bytes: 32,
      mime: 'video/mp4', kind: 'video',
      origin: '../../etc/passwd', codec: { nope: true },
    });
    await sleep(20);
    ok(rig.guestStore.lastMeta && rig.guestStore.lastMeta.origin === '',
      'a free-text origin is normalized to "" — the field is a two-valued flag, never a string we keep');
    ok(rig.guestStore.lastMeta && rig.guestStore.lastMeta.codec === '',
      'and a non-string codec normalizes to unknown rather than throwing');
    ok(rig.hostSaw.some((m) => m.t === XferVerb.Accept && m.tid === 777),
      'the offer was still ACCEPTED — neither field is ever a gate on the receiving side');
    rig.dispose();
  }
}

/* ============================================================ 15. the HEVC handshake
 *
 * The gap this closes, in one sentence: the sender's local decode probe only ever proved the
 * SENDER could play the clip, so Safari shipped its own HEVC to a Windows peer with no HEVC
 * decoder and the receiver watched a silent black window for the whole slot.
 *
 * The channel's job is only to ADVERTISE and to ANSWER; net/mediaQueue.js is what acts on the
 * answer (selftest-flow.js §6f drives that end).
 */
async function testCodecHandshake() {
  // --- advertised on the hello, and only when we have something to say
  {
    const rig = await makeRig({ hostCodecs: ['avc1', 'vp9'] });
    const hello = rig.guestSaw.find((m) => m.t === XferVerb.Hello);
    ok(hello && Array.isArray(hello.accepts_codecs)
      && hello.accepts_codecs.join(',') === 'avc1,vp9',
      'the hello carries accepts_codecs beside the untouched mime allowlist',
      JSON.stringify(hello && hello.accepts_codecs));
    ok(hello && Array.isArray(hello.accepts) && hello.accepts.includes('video/mp4'),
      '`accepts` still means what it always meant — the CONTAINER allowlist, unchanged');

    const back = rig.hostSaw.find((m) => m.t === XferVerb.Hello);
    ok(back && back.accepts_codecs === undefined,
      'a side with nothing to advertise OMITS the field (node cannot probe) — no empty-list claim');
    rig.dispose();
  }

  // --- the answer the queue asks for
  {
    const rig = await makeRig({ guestCodecs: ['avc1'] });
    ok(rig.hostCh.peerCanDecodeCodec('avc1.42E01E') === true, 'a listed family is decodable');
    ok(rig.hostCh.peerCanDecodeCodec('hvc1.1.6.L93.B0') === false,
      'THE POINT: an HEVC artifact is refused before it is ever offered to a peer without HEVC');
    ok(rig.hostCh.peerCanDecodeCodec('orig') === true,
      'an artifact whose codec we do not know is offered anyway — fail open');
    ok(rig.hostCh.peerCanDecodeCodec('') === true, '…and so is one with no codec at all');
    ok(rig.hostCh.stats().peer.acceptsCodecs.join(',') === 'avc1',
      'stats surfaces the peer list for the ?debug=1 overlay');
    rig.dispose();
  }

  // --- THE COMPATIBILITY CASE: a peer that says nothing takes everything
  {
    const rig = await makeRig();
    ok(rig.hostCh.stats().peer.acceptsCodecs === null,
      'a hello with no accepts_codecs reads as null — "they named none", not "they decode none"');
    ok(rig.hostCh.peerCanDecodeCodec('hvc1') === true
      && rig.hostCh.peerCanDecodeCodec('av01') === true,
      'so every codec is offerable, which is byte-identical to the behaviour before the handshake');
    const src = makeSource(1024, { mime: 'video/mp4', kind: 'video', codec: 'hvc1' });
    rig.hostCh.send(src);
    ok(await until(() => rig.guestStore.committed.has(src.sha256), 3000),
      'and an HEVC clip really does still transfer to an old peer');
    rig.dispose();
  }

  // --- the channel ADVERTISES and ANSWERS; it never refuses on its own
  {
    const rig = await makeRig({ guestCodecs: ['avc1'] });
    const src = makeSource(1024, { mime: 'video/mp4', kind: 'video', codec: 'hvc1' });
    const tid = rig.hostCh.send(src);
    ok(tid !== null,
      'send() does NOT gate on the peer\'s codecs: the queue is the policy, the channel is the wire');
    rig.dispose();
  }
}

// ============================================================ run
const main = async () => {
  testFraming();
  await testHappyPath();
  await testDeclines();
  await testHostileChunks();
  await testAckCadence();
  await testResume();
  await testCancel();
  await testBackpressure();
  await testHashMismatch();
  await testBidirectional();
  await testHelloTimeout();
  await testDeadlines();
  await testSenderGuards();
  await testOfferMetadata();
  await testCodecHandshake();

  console.log(failures === 0 ? `PASS — ${n} checks` : `FAILED — ${failures}/${n} checks`);
  process.exit(failures === 0 ? 0 : 1);
};

main().catch((e) => { console.error(e); process.exit(1); });
