// The one serializer every Goon Game transport shares — port of Services/GoonGame/GoonWire.cs.
//
// JSON, snake_case, integer enum codes, integer milliseconds, 64-bit seeds as decimal STRINGS,
// nulls dropped. The wire is UNTRUSTED: parse() never throws, it returns null and logs.

import { GoonConsts, MessageFactories, PROTOCOL_VERSION, clampWindowCount } from './contracts.js';
import { seedFromAny, seedToString } from './rng.js';

export const MAX_WIRE_BYTES = 16 * 1024;

/**
 * Per-message-type list of ulong fields. Deliberately NOT a generic BigInt replacer: if a future
 * message grows a seed field and nobody adds it here, JSON.stringify throws on the BigInt and we
 * find out immediately, instead of shipping a silently-wrong seed to the peer.
 */
const SEED_FIELDS = Object.freeze({
  match_start: ['seed_contribution'],
  round: ['seed_contribution'],
});

/**
 * Per-message-type fields that are NORMALIZED IN BOTH DIRECTIONS, listed one by one for the same
 * reason SEED_FIELDS is: the wire is untrusted, and a field whose range the rest of the app relies
 * on has to be pinned where the frame is built and where it is read, not hopefully downstream.
 *
 * Outbound it keeps our own bug from putting an impossible number on the wire; inbound it means no
 * consumer of a parsed tick ever sees a `vwin` of -1, 9, "3" or NaN. Absent stays absent-shaped:
 * the factory default (0) is what gets clamped, so a peer that never heard of the field reads 0.
 */
const CLAMPED_FIELDS = Object.freeze({
  tick: Object.freeze({ vwin: clampWindowCount }),
});

const encoder = typeof TextEncoder === 'function' ? new TextEncoder() : null;

export function wireByteLength(str) {
  if (typeof str !== 'string') return 0;
  if (encoder) return encoder.encode(str).length;
  let n = 0;
  for (let i = 0; i < str.length; i++) {
    const c = str.codePointAt(i);
    if (c > 0xffff) i++;
    n += c < 0x80 ? 1 : c < 0x800 ? 2 : c < 0x10000 ? 3 : 4;
  }
  return n;
}

const noopLogger = Object.freeze({
  info() {}, warn() {}, error() {},
});

function log(logger, level, msg) {
  const l = logger || (typeof console !== 'undefined' ? console : noopLogger);
  const fn = l[level] || l.log;
  if (typeof fn === 'function') fn.call(l, msg);
}

/** Deep copy that drops null/undefined members (NullValueHandling.Ignore parity). */
function stripNulls(value) {
  if (Array.isArray(value)) return value.map(stripNulls);
  if (value && typeof value === 'object') {
    const out = {};
    for (const k of Object.keys(value)) {
      const v = value[k];
      if (v === null || v === undefined) continue;
      out[k] = stripNulls(v);
    }
    return out;
  }
  return value;
}

/**
 * Message object -> JSON string. Mirrors GoonWire.Serialize: no size check here (that belongs to
 * the send path, see serializeForSend).
 */
export function serialize(msg) {
  if (!msg || typeof msg !== 'object') throw new Error('serialize: message required');
  if (typeof msg.t !== 'string' || msg.t === '') throw new Error('serialize: message has no "t"');

  const body = stripNulls(msg);
  if (body.v === undefined) body.v = PROTOCOL_VERSION;

  for (const field of SEED_FIELDS[msg.t] || []) {
    if (msg[field] !== null && msg[field] !== undefined) body[field] = seedToString(msg[field]);
  }

  const clamps = CLAMPED_FIELDS[msg.t];
  if (clamps) for (const field of Object.keys(clamps)) body[field] = clamps[field](body[field]);

  return JSON.stringify(body);
}

/**
 * Send-path serialize: GoonTransportBase logs an error and DROPS an oversize frame rather than
 * wedging the channel. Returns null in that case.
 */
export function serializeForSend(msg, { logger, tag = 'GoonWire' } = {}) {
  const json = serialize(msg);
  const bytes = wireByteLength(json);
  if (bytes > MAX_WIRE_BYTES) {
    log(logger, 'error', `[${tag}] Refusing to send oversize ${msg.t} (${bytes} bytes, cap ${MAX_WIRE_BYTES})`);
    return null;
  }
  return json;
}

/**
 * Parse one inbound frame. Returns null for anything unroutable — oversize, empty, not an object,
 * missing/unknown "t" — exactly as GoonWire.Deserialize does. Callers drop nulls; a single bad
 * frame from a mismatched build never ends a match.
 *
 * Unknown members are dropped and missing members read as their C# default, because the result is
 * built by overlaying the frame onto a fresh factory object.
 */
export function parse(json, { logger, tag = 'GoonWire' } = {}) {
  if (typeof json !== 'string' || json.trim() === '') return null;

  const bytes = wireByteLength(json);
  if (bytes > MAX_WIRE_BYTES) {
    log(logger, 'warn', `[${tag}] Dropping oversize frame (${bytes} bytes, cap ${MAX_WIRE_BYTES})`);
    return null;
  }

  let obj;
  try {
    obj = JSON.parse(json);
  } catch (e) {
    log(logger, 'warn', `[${tag}] Unparseable frame (${json.length} chars): ${e && e.message}`);
    return null;
  }
  if (!obj || typeof obj !== 'object' || Array.isArray(obj)) {
    log(logger, 'warn', `[${tag}] Frame is not a JSON object`);
    return null;
  }

  const t = obj.t;
  if (typeof t !== 'string' || t === '') {
    log(logger, 'warn', `[${tag}] Frame has no "t" discriminator`);
    return null;
  }

  const factory = MessageFactories[t];
  if (!factory) {
    // A newer peer sending a kind we don't know. Forward-compatible by design: ignore, stay up.
    log(logger, 'info', `[${tag}] Ignoring unknown message type '${t}'`);
    return null;
  }

  const v = Number.isInteger(obj.v) ? obj.v : GoonConsts.ProtocolVersion;
  if (v > GoonConsts.ProtocolVersion) {
    log(logger, 'info', `[${tag}] Peer speaks protocol v${v} (we are v${GoonConsts.ProtocolVersion}) — parsing anyway`);
  }

  const out = factory();
  for (const k of Object.keys(out)) {
    if (k === 't') continue;
    const raw = obj[k];
    if (raw === undefined) continue;
    // An explicit null on a member whose default isn't null reads as the default, not as null —
    // C# would have deserialized it into the field's default value.
    if (raw === null && out[k] !== null) continue;
    if (k === 'caps' && raw && typeof raw === 'object' && !Array.isArray(raw)) {
      for (const ck of Object.keys(out.caps)) {
        if (raw[ck] !== undefined && raw[ck] !== null) out.caps[ck] = raw[ck];
      }
      continue;
    }
    out[k] = raw;
  }
  out.v = v;

  for (const field of SEED_FIELDS[t] || []) out[field] = seedFromAny(obj[field]);

  const clamps = CLAMPED_FIELDS[t];
  if (clamps) for (const field of Object.keys(clamps)) out[field] = clamps[field](out[field]);

  return out;
}
