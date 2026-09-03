/* ============================================================================
 * provider/remote.js — the remote-media request channel.
 *
 * The page NEVER talks to our server for media (GROUND-RULES §8 bright line):
 * it asks the HOST over the bridge and the host's RemoteMediaCache (ConsumerId
 * "arcademy") answers. Protocol v1 (BUILD-CONTRACT §4):
 *
 *   page -> host  'assets-request' { reqId, count, kind:'loop'|'still', niches? }
 *   host -> page  'assets'         { reqId, urls:[{url,kind,mime}], done }
 *
 * SORT added three frames and no new transport (2026-08-23):
 *   page -> host  'assets-request'       { ..., subs:[...], tag }   serve ONLY those subs
 *   page -> host  'local-sample-request' { reqId, count, kind, folders?|presetId, tag }
 *   host -> page  'assets'               the SAME reply shape, rows carrying {tag, src}
 *   page -> host  'probe-sub'     {reqId, name}      host -> page 'sub-probe' {reqId, name, ok, ...}
 *   page -> host  'library-remove' {name}            host -> page 'library'   {subLibrary:[...]}
 * The last two are not media, so they ride `sendRaw` / `subscribe` rather than
 * the reqId mailbox - one bridge seam, one place it is written.
 *
 * NEVER BLOCKS: the host replies with whatever is cached now and may stream more
 * later under the SAME reqId, so this module is a mailbox, not a promise the
 * caller waits on. An empty reply is a silent local fallthrough — never an error
 * a game can see (FlashService posture).
 *
 * The bridge seam is deliberately loose: `bridge` may be
 *   - a function            bridge(type, payload)                (send only)
 *   - {send, on}            the shell's ccp() bridge - send({type, ...})
 *   - {post}/{postMessage}  anything postMessage-shaped           (send only)
 * When no receive hook exists we listen for an 'arcademy-assets' CustomEvent on
 * document, and `channel.receive(msg)` lets the shell hand replies in by hand.
 * ==========================================================================*/

export function createRemoteChannel({ bridge, offlineMode = false, enabled = true, log = () => {} } = {}) {
  let seq = 0;
  const pending = new Map();       // reqId -> { kind, onBatch }
  let listening = false;
  let disposed = false;

  const send = (type, payload) => {
    if (!bridge) return false;
    try {
      if (typeof bridge === 'function') { bridge(type, payload); return true; }
      // The Arcademy bridge takes ONE object carrying its own `type` (bridge.js
      // `send(msg)` drops anything without a string `msg.type`), so a (type,
      // payload) call would be swallowed in silence and the host would never be
      // asked for media at all. Flatten it.
      if (typeof bridge.send === 'function') { bridge.send(Object.assign({ type }, payload)); return true; }
      if (typeof bridge.post === 'function') { bridge.post(Object.assign({ type }, payload)); return true; }
      if (typeof bridge.postMessage === 'function') { bridge.postMessage(Object.assign({ type }, payload)); return true; }
    } catch (e) { log('assets-request failed: ' + (e && e.message)); }
    return false;
  };

  function receive(msg) {
    if (disposed || !msg) return;
    const reqId = msg.reqId || (msg.detail && msg.detail.reqId);
    const urls = msg.urls || (msg.detail && msg.detail.urls) || [];
    const done = !!(msg.done || (msg.detail && msg.detail.done));
    const rec = pending.get(reqId);
    if (!rec) return;                        // a stale/foreign reply is not our problem
    try { rec.onBatch(Array.isArray(urls) ? urls : []); } catch { /* ignore */ }
    if (done) pending.delete(reqId);
  }

  function listen() {
    if (listening || disposed) return;
    listening = true;
    if (bridge && typeof bridge.on === 'function') {
      try { bridge.on('assets', receive); return; } catch { /* fall through */ }
    }
    if (typeof document !== 'undefined' && document.addEventListener) {
      try { document.addEventListener('arcademy-assets', receive); } catch { /* ignore */ }
    }
  }

  /**
   * request({count, kind, niches, onBatch}) -> reqId|null
   * Fire-and-forget. onBatch(entries) may be called several times for one reqId.
   *
   * ADDITIVE (SORT, 2026-08-23) - a caller that passes none of these behaves
   * byte-for-byte as before:
   *   type       'assets-request' (default) or 'local-sample-request'
   *   local      true = a LOCAL sample, so the remote gates do not apply (a
   *              player with no remote consent still has folders on disk; the
   *              OfflineMode / consent gate is about the NETWORK, not the disk)
   *   subs       an explicit sub list - "serve ONLY from these" (SORT's piles)
   *   tag        'target' | 'noise' - the host stamps it back onto every row
   *   folders / presetId   the local sample's scope
   * The reply is `assets {reqId, urls, done}` for BOTH message types, which is
   * why one mailbox covers both.
   */
  function request({ count = 6, kind = 'still', niches, subs, tag, folders, presetId, type, local, onBatch }) {
    if (disposed) return null;
    if (!local && (offlineMode || !enabled)) return null;      // OfflineMode kills every fetch
    if (!bridge) return null;
    listen();
    seq += 1;
    const reqId = 'ae-' + seq + '-' + Math.floor(Date.now() % 1e7);
    pending.set(reqId, { kind, tag, onBatch: typeof onBatch === 'function' ? onBatch : () => {} });
    const payload = { reqId, count: Math.max(1, count | 0), kind };
    if (niches && niches.length) payload.niches = niches.slice();
    if (subs && subs.length) payload.subs = subs.slice();
    if (folders && folders.length) payload.folders = folders.slice();
    if (presetId) payload.presetId = String(presetId);
    if (tag) payload.tag = String(tag);
    const msgType = type === 'local-sample-request' ? 'local-sample-request' : 'assets-request';
    if (!send(msgType, payload)) pending.delete(reqId);
    return reqId;
  }

  /* --------------------------------------------------------------------------
   * THE OTHER TWO FRAMES (SORT's door). Not media requests: a probe and a
   * library edit. They ride the SAME loose bridge seam rather than a second copy
   * of it, which is the whole reason `send` and the subscribe shim live here.
   * ----------------------------------------------------------------------- */
  const subs = new Set();          // [type, fn, off] for dispose()

  /** Post any frame over the same seam (`bridge.send({type, ...})`). */
  function sendRaw(type, payload) {
    if (disposed || !type) return false;
    return send(type, payload || {});
  }

  /**
   * Subscribe to a host frame type. Returns an unsubscribe.
   * bridge.on is multi-subscriber (trap 11), so this never steals another
   * module's frames; with no `on` we fall back to the document CustomEvent seam
   * the assets mailbox already uses.
   */
  function subscribe(type, fn) {
    if (disposed || typeof fn !== 'function' || !type) return () => {};
    if (bridge && typeof bridge.on === 'function') {
      try {
        const off = bridge.on(type, fn);
        const rec = { off: typeof off === 'function' ? off : () => {} };
        subs.add(rec);
        return () => { rec.off(); subs.delete(rec); };
      } catch { /* fall through to the document seam */ }
    }
    if (typeof document !== 'undefined' && document.addEventListener) {
      const wrap = (ev) => fn((ev && ev.detail) || ev);
      try {
        document.addEventListener('arcademy-' + type, wrap);
        const rec = { off: () => { try { document.removeEventListener('arcademy-' + type, wrap); } catch { /* ignore */ } } };
        subs.add(rec);
        return () => { rec.off(); subs.delete(rec); };
      } catch { /* ignore */ }
    }
    return () => {};
  }

  function dispose() {
    disposed = true;
    pending.clear();
    for (const rec of [...subs]) { try { rec.off(); } catch { /* ignore */ } }
    subs.clear();
    if (typeof document !== 'undefined' && document.removeEventListener) {
      try { document.removeEventListener('arcademy-assets', receive); } catch { /* ignore */ }
    }
  }

  return { request, receive, listen, dispose, sendRaw, subscribe, get pending() { return pending.size; } };
}

export default createRemoteChannel;
