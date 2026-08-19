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
   */
  function request({ count = 6, kind = 'still', niches, onBatch }) {
    if (disposed || offlineMode || !enabled) return null;      // OfflineMode kills every fetch
    if (!bridge) return null;
    listen();
    seq += 1;
    const reqId = 'ae-' + seq + '-' + Math.floor(Date.now() % 1e7);
    pending.set(reqId, { kind, onBatch: typeof onBatch === 'function' ? onBatch : () => {} });
    const payload = { reqId, count: Math.max(1, count | 0), kind };
    if (niches && niches.length) payload.niches = niches.slice();
    if (!send('assets-request', payload)) pending.delete(reqId);
    return reqId;
  }

  function dispose() {
    disposed = true;
    pending.clear();
    if (typeof document !== 'undefined' && document.removeEventListener) {
      try { document.removeEventListener('arcademy-assets', receive); } catch { /* ignore */ }
    }
  }

  return { request, receive, listen, dispose, get pending() { return pending.size; } };
}

export default createRemoteChannel;
