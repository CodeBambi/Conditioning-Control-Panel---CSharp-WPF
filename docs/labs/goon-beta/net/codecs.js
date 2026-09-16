// net/codecs.js — WHAT CAN THE OTHER SIDE ACTUALLY DECODE?
//
// THE BUG THIS EXISTS FOR, written down because the shape of it is not obvious. Until now the only
// decode question anybody asked was asked LOCALLY: ui/assetsStore.js probeVideoDecodable loads a
// picked clip into an off-DOM <video> and believes the answer. That answer is about THIS device.
// Safari decodes its own HEVC happily, adopts the clip, offers it, and the transfer succeeds
// perfectly — and the Windows peer, whose WebView2 has no HEVC decoder, mounts a <video> that
// fires no error worth the name and paints a silent black rectangle for the whole slot. Every
// layer reports success; the player sees nothing. That is why probeVideoDecodable's own comment
// says "KNOWN GAP, on purpose … a peer-capability handshake is the real fix", and this is it.
//
// THE CONTRACT IS FAIL-OPEN, IN BOTH DIRECTIONS, AND THAT IS THE WHOLE SAFETY STORY:
//   - a peer that sends no codec list (an old build, a build with no probe surface) is treated as
//     "accepts everything" — byte-identical to the behaviour before this file existed;
//   - an artifact whose codec we do not KNOW is offered, always. The compression cache knows
//     ("avc1" — it produced the file), an exempt original does not ("orig"), and a guess would
//     refuse good clips to prevent a black rectangle, which is the worse trade.
// Only a KNOWN codec against a KNOWN-negative peer list is ever blocked.
//
// NODE-IMPORT-SAFE, same rule as net/mediaChannel.js which imports it: every runtime lookup is
// guarded and nothing touches `document`, `window` or `MediaSource` at module scope. Under node
// the probe answers `null` ("cannot tell"), which the fail-open rule reads as "advertise nothing",
// which makes every peer accept everything from us. The selftests depend on that.

/** The families we speak about. Anything else normalizes to '' (= unknown, fail open). */
export const CodecFamily = Object.freeze({
  Avc: 'avc1',    // H.264, every profile — the universal one
  Hevc: 'hvc1',   // H.265, incl. the `hev1` box name. THE problem child (iPhone)
  Vp9: 'vp9',
  Av1: 'av01',
});

/**
 * THE PROBE LIST, deliberately four entries long.
 *
 * Each family is asked with ONE representative type string, because the question here is "is there
 * a decoder for this family at all", not "can you take this exact profile/level". A device with an
 * H.264 decoder that refuses one level still shows the clip; a device with no HEVC decoder shows
 * nothing, and that is the difference this list is drawn around.
 *
 *  - avc1.42E01E  Baseline 3.0. If a runtime says no to this it has no H.264 at all, which in
 *                 practice does not happen — but it is asked rather than assumed.
 *  - avc1.640028  High 4.0: what a phone camera and most real mp4s actually carry. A runtime that
 *                 takes Baseline but not High is a real (embedded) thing, and either answer maps
 *                 to the same family — we take the OR, because refusing H.264 outright would kill
 *                 the lane for everybody.
 *  - hvc1.1.6.L93.B0  Main L3.1. Asked in mp4, the only container HEVC arrives in here.
 *  - vp09.00.10.08    Profile 0, 8-bit. Asked in webm AND mp4 (Safari only knows it in one).
 *  - av01.0.04M.08    Main profile, level 3.0, 8-bit.
 */
export const DECODE_PROBES = Object.freeze([
  Object.freeze({ family: CodecFamily.Avc, types: Object.freeze([
    'video/mp4; codecs="avc1.42E01E"', 'video/mp4; codecs="avc1.640028"']) }),
  Object.freeze({ family: CodecFamily.Hevc, types: Object.freeze([
    'video/mp4; codecs="hvc1.1.6.L93.B0"', 'video/mp4; codecs="hev1.1.6.L93.B0"']) }),
  Object.freeze({ family: CodecFamily.Vp9, types: Object.freeze([
    'video/webm; codecs="vp09.00.10.08"', 'video/mp4; codecs="vp09.00.10.08"']) }),
  Object.freeze({ family: CodecFamily.Av1, types: Object.freeze([
    'video/mp4; codecs="av01.0.04M.08"']) }),
]);

/**
 * One codec string (a wire token, a `codecs=` parameter value, a C# cache label) reduced to its
 * FAMILY, or '' when we do not recognise it.
 *
 * Tolerant on purpose: the four things that produce these strings — the C# cache ("avc1"), the
 * page encoder ("avc1.42E01E"), an mp4 box name ("hev1") and a mime parameter ("vp09.00.10.08") —
 * all spell the same four families differently, and an unrecognised spelling must degrade to
 * "unknown" (offer it) rather than to a wrong family (maybe refuse it).
 */
export function normalizeCodec(codec) {
  const s = String(codec || '').trim().toLowerCase();
  if (!s) return '';
  if (s.startsWith('avc1') || s.startsWith('avc3') || s === 'h264' || s === 'h.264') return CodecFamily.Avc;
  if (s.startsWith('hvc1') || s.startsWith('hev1') || s === 'h265' || s === 'h.265' || s === 'hevc') return CodecFamily.Hevc;
  if (s.startsWith('vp09') || s === 'vp9') return CodecFamily.Vp9;
  if (s.startsWith('av01') || s === 'av1') return CodecFamily.Av1;
  return '';                                   // 'orig', 'webp', 'jpeg', 'vp8', anything new
}

/**
 * The codec family named by a mime's `codecs=` parameter, or ''. This is the OTHER cheap source
 * the design allows: a plain `video/mp4` says nothing, but `video/mp4;codecs="hvc1.1.6.L93.B0"`
 * says everything, and it costs a string split to read.
 */
export function codecFromMime(mime) {
  const m = /codecs\s*=\s*"?([^";,]+)/i.exec(String(mime || ''));
  return m ? normalizeCodec(m[1]) : '';
}

/** The mime with any parameters stripped — `video/mp4;codecs="…"` -> `video/mp4`. */
export function baseMime(mime) {
  return String(mime || '').split(';')[0].trim().toLowerCase();
}

/** Cached, because the probe is asked once per hello and the answer cannot change mid-session. */
let probed = null;

/**
 * WHAT THIS RUNTIME CAN DECODE, as a list of families — or `null` when it cannot be asked.
 *
 * `MediaSource.isTypeSupported` first (it is the strict one: it answers about a real decoder),
 * `<video>.canPlayType` second (it answers "maybe"/"probably", and "maybe" is deliberately taken
 * as YES — a hedged yes from the element is still a decoder, and the cost of a false yes is one
 * black slot while the cost of a false no is a refused good clip).
 *
 * `null` vs `[]` MATTERS. `null` = "no way to ask" (node, an exotic embedder): we advertise
 * nothing and every peer keeps offering us everything, which is today's behaviour. `[]` would be
 * a claim that we can decode NOTHING, and a peer honouring it would stop sending video entirely.
 * Nothing in this file ever returns `[]`: a runtime that answers "no" to every probe still gets
 * `null`, because a runtime with no H.264 at all is far likelier to be a broken probe than a real
 * device.
 *
 * @param {boolean} [force] re-probe instead of answering from the cache (tests only)
 * @returns {string[]|null}
 */
export function probeDecodeCodecs(force) {
  if (probed !== null && !force) return probed.length ? probed.slice() : null;

  const out = [];
  let asked = false;

  let MS = null;
  try {
    const g = globalThis;
    MS = (g && g.MediaSource && typeof g.MediaSource.isTypeSupported === 'function') ? g.MediaSource : null;
  } catch (_e) { MS = null; }

  let el = null;
  try {
    const d = typeof document !== 'undefined' ? document : null;
    el = (d && typeof d.createElement === 'function') ? d.createElement('video') : null;
    if (el && typeof el.canPlayType !== 'function') el = null;
  } catch (_e) { el = null; }

  if (MS || el) {
    for (const probe of DECODE_PROBES) {
      let yes = false;
      for (const type of probe.types) {
        try {
          if (MS && MS.isTypeSupported(type) === true) { asked = true; yes = true; break; }
        } catch (_e) { /* one type refusing to be asked is not an answer */ }
        try {
          // '' is a NO; 'maybe' and 'probably' are both a YES — see the header.
          if (el && String(el.canPlayType(type) || '') !== '') { asked = true; yes = true; break; }
        } catch (_e) { /* ditto */ }
      }
      if (yes) out.push(probe.family);
    }
  }

  // Nothing answered yes anywhere -> we could not really ask. Say "unknown", never "none".
  probed = asked && out.length ? out : [];
  return probed.length ? probed.slice() : null;
}

/** Test affordance: forget the cached probe so the next call re-asks this runtime. */
export function resetDecodeProbe() { probed = null; }

/**
 * Would a peer advertising `accepts` be able to decode an artifact encoded with `codec`?
 *
 * THE ONLY `false` THIS FUNCTION EVER RETURNS is "the peer sent a real list, the codec is one we
 * recognise, and it is not on their list". Every other combination — no list, empty list, unknown
 * codec, junk input — is `true`. Read the four early returns as the fail-open contract itself.
 *
 * @param {string[]|null|undefined} accepts the peer's advertised families (their hello)
 * @param {string} codec our artifact's codec, in any spelling normalizeCodec understands
 */
export function peerCanDecode(accepts, codec) {
  if (!Array.isArray(accepts) || accepts.length === 0) return true;   // old peer / no probe
  const want = normalizeCodec(codec);
  if (!want) return true;                                             // we do not know: offer it
  for (const a of accepts) if (normalizeCodec(a) === want) return true;
  return false;
}

/**
 * Every codec family named by one hello frame, read TOLERANTLY from both places a peer may have
 * put them, or `null` when it named none.
 *
 *   accepts_codecs: ['avc1','vp9']                     the field this build writes
 *   accepts: ['video/mp4;codecs="avc1.42E01E"', …]     a peer that parameterised its mime list
 *
 * The second form is read because `accepts` is the OLDER field and is documented as a mime
 * allowlist: a build that decides to carry codecs there instead is not wrong, and one regex is
 * cheaper than an interop bug. Plain mimes in `accepts` contribute nothing and are skipped, which
 * is exactly what an old peer sends.
 */
export function acceptedCodecsFromHello(hello) {
  const h = hello || {};
  const out = [];
  const push = (fam) => { if (fam && out.indexOf(fam) < 0) out.push(fam); };
  if (Array.isArray(h.accepts_codecs)) for (const c of h.accepts_codecs) push(normalizeCodec(c));
  if (Array.isArray(h.accepts)) for (const m of h.accepts) push(codecFromMime(m));
  return out.length ? out : null;
}

export default {
  CodecFamily, DECODE_PROBES, normalizeCodec, codecFromMime, baseMime,
  probeDecodeCodecs, resetDecodeProbe, peerCanDecode, acceptedCodecsFromHello,
};
