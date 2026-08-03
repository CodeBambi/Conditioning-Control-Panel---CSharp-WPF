// Sanity pass over net/ — signaling, the fake server, the transport base, loopback, relay
// mechanics, and session.js's relay-fallback dance.
//
//   node Resources/web/goon/test/selftest-net.js
//
// WebRTC itself is NOT testable under node (no RTCPeerConnection), which is why webrtcTransport is
// kept thin over the shared base: everything asserted below lives in transportBase/signaling/
// session, and the browser leg is a play-test item.

import { GoonSignalingClient, GoonSignalError, normalizeCode } from '../net/signaling.js';
import { GoonFakeSignalingServer } from '../net/fakeSignaling.js';
import { createLoopbackPair, loopbackOptions, loopbackPresets } from '../net/loopbackTransport.js';
import { GoonRelayTransport } from '../net/relayTransport.js';
import { GoonSession } from '../net/session.js';
import { GoonTransportState, makeEmote, makeTick } from '../core/contracts.js';

let failures = 0;
let n = 0;
function ok(cond, label, extra = '') {
  n++;
  if (!cond) { failures++; console.error(`  FAIL ${label} ${extra}`); }
}
const quiet = { info() {}, warn() {}, error() {}, log() {} };
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

async function until(fn, ms = 4000, step = 25) {
  const deadline = Date.now() + ms;
  while (Date.now() < deadline) {
    if (fn()) return true;
    await sleep(step);
  }
  return false;
}

// ============================================================ 1. fake signaling + error map
async function testSignaling() {
  const fake = new GoonFakeSignalingServer();
  const host = new GoonSignalingClient({ post: fake.post, unifiedId: 'u_host', appVersion: 'test', logger: quiet });
  const guest = new GoonSignalingClient({ post: fake.post, unifiedId: 'u_guest', appVersion: 'test', logger: quiet });

  const invite = await host.createInvite('Bambi', false);
  ok(!!invite && !!invite.code && !!invite.token, 'invite mints a code + token');
  ok(invite.pass === 'premium' && invite.relayAllowed === true, 'invite reports pass + relay_allowed');

  // Before the join the host sees peer_joined=false.
  const pre = await host.signal(invite.code, invite.token, 'host', 0, []);
  ok(pre && pre.peerJoined === false, 'peer_joined false before the guest redeems');

  const joined = await guest.join(invite.code.toLowerCase(), 'Circe');
  ok(!!joined && !!joined.token, 'join redeems the code (case-insensitive)');
  ok(joined.token !== invite.token, 'guest gets its own room token');
  ok(joined.peerDisplayName === 'host', 'join reports the peer display name');

  const dup = await guest.join(invite.code, 'Circe');
  ok(dup === null && guest.lastError === GoonSignalError.AlreadyJoined, 'second join -> already_joined', String(guest.lastError));

  // --- post-and-drain round trip ------------------------------------------------
  const h1 = await host.signal(invite.code, invite.token, 'host', 0, [{ kind: 'offer', data: 'OFFER' }]);
  ok(h1 && h1.messages.length === 0, 'no self-echo: the poster never gets its own message back');
  ok(h1 && h1.cursor === 1, 'cursor advances PAST our own message', String(h1 && h1.cursor));
  ok(h1 && h1.peerJoined === true, 'peer_joined true after the guest redeemed');

  const g1 = await guest.signal(invite.code, joined.token, 'guest', 0, [{ kind: 'answer', data: 'ANSWER' }]);
  ok(g1 && g1.messages.length === 1 && g1.messages[0].kind === 'offer', 'guest drains the offer');
  ok(g1 && g1.messages[0].from === 'host' && g1.messages[0].seq === 1, 'drained message carries seq + from');
  ok(g1 && g1.cursor === 2, 'guest cursor covers its own answer too', String(g1 && g1.cursor));

  const h2 = await host.signal(invite.code, invite.token, 'host', h1.cursor, []);
  ok(h2 && h2.messages.length === 1 && h2.messages[0].data === 'ANSWER', 'host drains the answer');
  const h3 = await host.signal(invite.code, invite.token, 'host', h2.cursor, []);
  ok(h3 && h3.messages.length === 0, 'exclusive cursor: nothing is redelivered');

  // --- relay mailbox is a separate ring -----------------------------------------
  const r1 = await host.relay(invite.code, invite.token, 'host', 0, ['{"t":"tick"}'], 10);
  ok(r1 && r1.frames.length === 0 && r1.cursor === 1, 'relay: no self-echo, cursor advances');
  const r2 = await guest.relay(invite.code, joined.token, 'guest', 0, [], 10);
  ok(r2 && r2.frames.length === 1 && r2.frames[0] === '{"t":"tick"}', 'relay: peer drains the frame');
  ok(r2 && r2.peerOnline === true, 'relay reports peer_online');

  // --- failure paths -------------------------------------------------------------
  const bad = await guest.join('ZZZZZZ', 'Circe');
  ok(bad === null && guest.lastError === GoonSignalError.UnknownCode, 'unknown code -> unknown_code', String(guest.lastError));

  fake.expire(invite.code);
  const dead = await host.signal(invite.code, invite.token, 'host', 0, []);
  ok(dead === null && host.lastError === GoonSignalError.Expired, 'expired room -> expired', String(host.lastError));
  const deadJoin = await guest.join(invite.code, 'Circe');
  ok(deadJoin === null && guest.lastError === GoonSignalError.UnknownCode, 'expired code reads as unknown_code to a joiner');

  const nope = await fake.post('/v2/goon/nope', {});
  ok(nope.status === 404 && JSON.parse(nope.body).error === GoonSignalError.NotDeployed, 'unknown route -> not_deployed');

  // --- the {kind, detail} error map the lobby renders -----------------------------
  const stub = (status, body) => new GoonSignalingClient({ post: () => Promise.resolve({ status, body }), logger: quiet });

  let c = stub(404, '');
  ok(await c.createInvite('x') === null && c.lastError === GoonSignalError.NotDeployed, 'bare 404 -> not_deployed');

  c = stub(404, '{"error":"expired"}');
  ok(await c.createInvite('x') === null && c.lastError === GoonSignalError.Expired, '404 WITH a body keeps the server error');

  c = stub(402, '{"error":"no_pass","next_pass_utc":"2026-08-10T00:00:00Z"}');
  await c.createInvite('x');
  ok(c.lastError === GoonSignalError.NoPass && c.lastErrorDetail === '2026-08-10T00:00:00Z', '402 -> no_pass + next_pass_utc');
  ok(c.lastErrorInfo.kind === 'no_pass' && c.lastErrorInfo.detail === '2026-08-10T00:00:00Z', 'lastErrorInfo shape');

  c = stub(429, '{"error":"rate_limited","cap":"user","count":41,"retry_after_seconds":17}');
  await c.createInvite('x');
  ok(c.lastError === GoonSignalError.RateLimited && c.retryAfterSeconds === 17, '429 -> rate_limited + retry_after_seconds');

  c = stub(409, '{"error":"already_joined"}');
  ok(await c.join('AAA') === null && c.lastError === GoonSignalError.AlreadyJoined, '409 -> already_joined');

  c = stub(401, '');
  await c.createInvite('x');
  ok(c.lastError === GoonSignalError.Unauthorized, '401 -> unauthorized');

  c = stub(0, '');
  await c.createInvite('x');
  ok(c.lastError === GoonSignalError.Network, 'status 0 (bridge transport failure) -> network');

  c = stub(200, 'not json');
  await c.createInvite('x');
  ok(c.lastError === GoonSignalError.Malformed, '2xx with a junk body -> malformed_response');

  c = stub(200, '{"ok":true}');
  await c.createInvite('x');
  ok(c.lastError === GoonSignalError.Malformed, '2xx with no code/token -> malformed_response');

  c = stub(500, '');
  await c.createInvite('x');
  ok(c.lastError === 'http_500', 'unmapped status -> http_<status>');

  // --- code normalization ---------------------------------------------------------
  ok(normalizeCode(' 7qk4-rm ') === '7QK4RM', 'normalizeCode trims, uppers, strips dashes/spaces');
  ok(normalizeCode(null) === '' && normalizeCode(undefined) === '', 'normalizeCode tolerates null');
  ok(normalizeCode('IL0O') === 'IL0O', 'normalizeCode does NOT fold crockford I/L/O (server owns the alphabet)');
}

// ============================================================ 2. loopback + clocks + base
async function testLoopback() {
  const logged = { info: [], warn: [], error: [] };
  const spy = {
    info(m) { logged.info.push(String(m)); },
    warn(m) { logged.warn.push(String(m)); },
    error(m) { logged.error.push(String(m)); },
  };

  const pair = createLoopbackPair(loopbackOptions(Object.assign(loopbackPresets.p2p(), { logger: spy })));
  const { host, guest } = pair;

  ok(host.isHost === true && guest.isHost === false, 'pair roles');
  ok(host.state === GoonTransportState.Disconnected, 'starts Disconnected');
  ok(host.clock.isClockMaster === true && guest.clock.isClockMaster === false, 'host owns the match clock');

  const hostStates = [];
  const guestMsgs = [];
  const hostMsgs = [];
  const unsubState = host.onStateChanged((s) => hostStates.push(s));
  guest.onMessageReceived((m) => guestMsgs.push(m));
  host.onMessageReceived((m) => hostMsgs.push(m));

  const synced = await pair.connect();
  ok(synced, 'both loopback clocks synced');
  ok(host.state === GoonTransportState.ConnectedP2P, 'host connected');
  ok(host.clock.offsetMs === 0, 'host offset pinned at 0', String(host.clock.offsetMs));
  ok(Math.abs(guest.clock.offsetMs + 3517) < 50, 'guest offset cancels the 3517ms skew', String(guest.clock.offsetMs));
  ok(Math.abs(guest.clock.nowMatchMs() - host.clock.nowMatchMs()) < 50, 'match clocks agree (<50ms)',
    String(guest.clock.nowMatchMs() - host.clock.nowMatchMs()));

  // Frames round-trip through the base: serialize -> deliver -> parse.
  await host.send(makeEmote({ text: 'hi', icon: '*' }));
  ok(await until(() => guestMsgs.length > 0, 2000), 'frame delivered to the peer');
  ok(guestMsgs[0] && guestMsgs[0].t === 'emote' && guestMsgs[0].text === 'hi', 'frame parsed back into a message');
  ok(guestMsgs[0] && guestMsgs[0].v === 1, 'protocol version rides along');
  ok(hostMsgs.length === 0, 'no self-echo through the loopback');

  // Clock ping/pong is private to MatchClock and must never surface as a message.
  ok(!guestMsgs.some((m) => m.t === 'clock_ping' || m.t === 'clock_pong'), 'clock traffic never surfaces on messageReceived');

  // Oversize frame: dropped on the SEND side with an error, never handed to the channel.
  const before = guestMsgs.length;
  logged.error.length = 0;
  await host.send(makeTick({ active_effects: new Array(4000).fill('effectname') }));
  await sleep(150);
  ok(guestMsgs.length === before, 'oversize frame never reaches the peer');
  ok(logged.error.some((m) => m.includes('oversize')), 'oversize frame logged as an error', logged.error.join('|'));

  // A message with an unknown discriminator is dropped by parse, not by the peer's engine.
  const beforeUnknown = guestMsgs.length;
  await host._sendRaw('{"t":"from_the_future","v":9}');
  await sleep(150);
  ok(guestMsgs.length === beforeUnknown, 'unknown message type dropped at the wire');

  await host.close();
  ok(host.state === GoonTransportState.Closed, 'closed');
  ok(hostStates.length === 2 && hostStates[0] === GoonTransportState.ConnectedP2P
    && hostStates[1] === GoonTransportState.Closed, 'state events fire in order', hostStates.join(','));
  unsubState();

  // Unsubscribe actually unsubscribes.
  const wasLen = hostStates.length;
  guest.markConnected();
  await guest.close();
  ok(hostStates.length === wasLen, 'unsubscribed listener stops firing');

  pair.dispose();
  ok(host.isDisposed && guest.isDisposed, 'pair disposed');

  // Instant + relay presets exist and carry the documented knobs.
  const inst = loopbackPresets.instant();
  ok(inst.latencyMs === 0 && inst.jitterMs === 0 && inst.guestClockSkewMs === 0, 'instant preset');
  const rel = loopbackPresets.relay();
  ok(rel.latencyMs === 900 && rel.jitterMs === 600 && rel.guestClockSkewMs === 3517, 'relay preset');
}

// ============================================================ 3. relay transport mechanics
async function testRelayTransport() {
  const fake = new GoonFakeSignalingServer();
  const hostSig = new GoonSignalingClient({ post: fake.post, unifiedId: 'u_host', logger: quiet });
  const guestSig = new GoonSignalingClient({ post: fake.post, unifiedId: 'u_guest', logger: quiet });

  // The fake answers instantly, so the cadence floor is what paces the loop — shortened here so
  // the test asserts CURSOR/ECHO correctness rather than wall-clock timing.
  const mk = (isHost, signaling) => new GoonRelayTransport({
    isHost, signaling, logger: quiet, waitMs: 50, minGapMs: 50,
  });
  const host = mk(true, hostSig);
  const guest = mk(false, guestSig);

  const hostMsgs = [];
  const guestMsgs = [];
  host.onMessageReceived((m) => hostMsgs.push(m));
  guest.onMessageReceived((m) => guestMsgs.push(m));

  const code = await host.createInvite();
  ok(!!code, 'relay host minted a room');
  ok(host.code === code && !!host.token, 'relay keeps code + token');

  const joined = await guest.join(code);
  ok(joined === true, 'relay guest joined');

  ok(await until(() => host.state === GoonTransportState.ConnectedRelay
    && guest.state === GoonTransportState.ConnectedRelay, 3000), 'both sides report ConnectedRelay',
  `${host.state}/${guest.state}`);

  await host.send(makeEmote({ text: 'relayed', icon: '~' }));
  ok(await until(() => guestMsgs.some((m) => m.t === 'emote'), 3000), 'frame crossed the relay mailbox');
  ok(guestMsgs.find((m) => m.t === 'emote').text === 'relayed', 'relayed frame intact');
  ok(!hostMsgs.some((m) => m.t === 'emote'), 'relay never echoes the sender its own frame');

  const room = fake.room(code);
  ok(room.relay.length > 0, 'server ring holds the posted frames');
  // The emote is the host's OWN frame: its cursor must have moved past it even though the server
  // never echoed it back. (Exact equality with the ring head would race the clock's ping traffic.)
  const emoteSeq = room.relay.find((f) => f.from === 'host' && f.data.includes('"emote"')).seq;
  ok(host._cursor >= emoteSeq, 'poster cursor advanced past its OWN frame', `${host._cursor} vs ${emoteSeq}`);
  ok(guest._cursor >= emoteSeq, 'peer cursor covers everything drained', `${guest._cursor} vs ${emoteSeq}`);

  // Clock sync rides the same mailbox — proof the relay path is a full transport, not a side door.
  ok(await until(() => host.clock.isSynced && guest.clock.isSynced, 4000), 'clocks sync over the relay');

  await host.close();
  await guest.close();
  ok(host.state === GoonTransportState.Closed, 'relay closed');
  host.dispose();
  guest.dispose();

  // A room that dies under the loop surfaces the terminal server verdict.
  const dead = new GoonRelayTransport({ isHost: true, signaling: hostSig, logger: quiet, waitMs: 20, minGapMs: 20 });
  const deadCode = await dead.createInvite();
  ok(await until(() => dead.state === GoonTransportState.ConnectedRelay, 2000), 'second room live');
  fake.expire(deadCode);
  ok(await until(() => dead.state === GoonTransportState.Disconnected, 3000), 'expired room disconnects the relay');
  ok(dead.lastError === GoonSignalError.Expired, 'relay surfaces expired', String(dead.lastError));
  dead.dispose();
}

// ============================================================ 4. session fallback dance (stubs)
async function testSessionFallback() {
  // --- stubs -------------------------------------------------------------------
  class StubWebrtc {
    constructor(isHost, signaling, { failIce = true } = {}) {
      this.isHost = isHost; this.signaling = signaling; this._failIce = failIce;
      this.code = null; this.token = null; this.iceFailed = false; this.lastError = null;
      this.closed = false; this.disposed = false;
    }
    async createInvite() { this.code = 'ROOM01'; this.token = 'tok-host'; return this.code; }
    async join(c) { this.code = c; this.token = 'tok-guest'; return true; }
    async waitForChannel() {
      await sleep(10);
      this.iceFailed = this._failIce;
      this.lastError = this._failIce ? 'ice_timeout' : 'unauthorized';
      return false;
    }
    async close() { this.closed = true; }
    dispose() { this.disposed = true; }
  }

  class StubRelay {
    constructor(isHost, signaling) {
      this.isHost = isHost; this.signaling = signaling;
      this.adopted = null; this.lastError = null; this.closed = false; this.disposed = false;
    }
    adoptRoom(code, token) { this.adopted = { code, token }; }
    async waitForChannel() { return true; }
    async close() { this.closed = true; }
    dispose() { this.disposed = true; }
  }

  class StubMatch {
    constructor(transport, isHost) {
      this.transport = transport; this.isHost = isHost;
      this.adoptedLobby = false; this.cancelled = null; this.disposed = false;
    }
    createInvite() { return this.transport.createInvite(); }
    join(c) { return this.transport.join(c); }
    adoptLobby() { this.adoptedLobby = true; return true; }
    cancelMatch(reason) { this.cancelled = reason; }
    dispose() { this.disposed = true; }
  }

  // --- the fallback path --------------------------------------------------------
  const matches = [];
  const signalings = [];
  const webrtcs = [];
  const relays = [];
  const changed = [];
  const failed = [];

  const session = new GoonSession({
    logger: quiet,
    createMatch: (t, isHost) => { const m = new StubMatch(t, isHost); matches.push(m); return m; },
    createSignaling: () => { const s = { id: signalings.length, dispose() { this.disposed = true; } }; signalings.push(s); return s; },
    createWebrtc: (isHost, sig) => { const w = new StubWebrtc(isHost, sig); webrtcs.push(w); return w; },
    createRelay: (isHost, sig) => { const r = new StubRelay(isHost, sig); relays.push(r); return r; },
  });
  session.onCurrentMatchChanged((m) => changed.push(m));
  session.onConnectFailed((r) => failed.push(r));

  const code = await session.host();
  ok(code === 'ROOM01', 'host() returns the invite code');
  ok(matches.length === 1 && session.currentMatch === matches[0], 'match built for the p2p transport');
  ok(changed.length === 1, 'onCurrentMatchChanged fired on session start');

  ok(await until(() => relays.length === 1 && !!relays[0].adopted, 3000), 'fallback opened a relay transport');
  ok(matches.length === 2, 'fallback REBUILT the match via the factory', String(matches.length));
  ok(session.currentMatch === matches[1], 'currentMatch points at the new match');
  ok(changed.length === 2 && changed[1] === matches[1], 'onCurrentMatchChanged fired for the rebuild');

  ok(matches[0].disposed === true, 'old match disposed');
  ok(matches[0].cancelled === null, 'old match was NOT cancelled (dispose is silent — never a forfeit)');
  ok(matches[1].adoptedLobby === true, 'new match adoptLobby()d instead of re-inviting');
  ok(relays[0].adopted.code === 'ROOM01' && relays[0].adopted.token === 'tok-host', 'code + token carried to adoptRoom');
  ok(webrtcs[0].closed && webrtcs[0].disposed, 'dead p2p transport closed and disposed');

  ok(signalings.length === 1, 'exactly ONE signaling client per session', String(signalings.length));
  ok(relays[0].signaling === webrtcs[0].signaling, 'relay reuses the SAME signaling instance (room + burned pass)');
  ok(failed.length === 0, 'a successful fallback raises no connectFailed');

  // leave() is the user path: it cancels, then folds.
  await session.leave();
  ok(matches[1].cancelled === 'left', 'leave() cancels the live match');
  ok(matches[1].disposed && relays[0].closed && relays[0].disposed, 'leave() tears the plumbing down');
  ok(signalings[0].disposed === true, 'leave() disposes the signaling client');
  ok(session.currentMatch === null && session.isBusy === false, 'session back to idle');
  ok(changed.length === 3 && changed[2] === null, 'teardown raises onCurrentMatchChanged(null)');

  // --- a signaling-level failure must NOT fall back to relay ---------------------
  const m2 = []; const r2 = []; const f2 = [];
  const s2 = new GoonSession({
    logger: quiet,
    createMatch: (t, isHost) => { const m = new StubMatch(t, isHost); m2.push(m); return m; },
    createSignaling: () => ({ dispose() {} }),
    createWebrtc: (isHost, sig) => new StubWebrtc(isHost, sig, { failIce: false }),
    createRelay: (isHost, sig) => { const r = new StubRelay(isHost, sig); r2.push(r); return r; },
  });
  s2.onConnectFailed((r) => f2.push(r));
  await s2.host();
  ok(await until(() => f2.length > 0, 3000), 'signaling failure raises connectFailed');
  ok(f2[0] === 'unauthorized', 'connectFailed carries the machine reason', String(f2[0]));
  ok(r2.length === 0, 'no relay is opened when ICE did not fail');
  ok(m2[0].disposed && m2[0].cancelled === null, 'failed lobby is disposed, not cancelled');
  ok(s2.currentMatch === null, 'failed session is idle');

  // A session already running refuses a second host().
  const busy = new GoonSession({
    logger: quiet,
    createMatch: (t, isHost) => new StubMatch(t, isHost),
    createSignaling: () => ({ dispose() {} }),
    createWebrtc: (isHost, sig) => new StubWebrtc(isHost, sig),
    createRelay: (isHost, sig) => new StubRelay(isHost, sig),
  });
  await busy.host();
  ok(await busy.host() === null, 'a busy session refuses a second host()');
  await busy.leave();
}

// ============================================================ run
const main = async () => {
  await testSignaling();
  await testLoopback();
  await testRelayTransport();
  await testSessionFallback();

  console.log(failures === 0 ? `PASS — ${n} checks` : `FAILED — ${failures}/${n} checks`);
  process.exit(failures === 0 ? 0 : 1);
};

main().catch((e) => { console.error(e); process.exit(1); });
