/* ============================================================================
 * bridge.js - WEB shim for the public Loom page (cclabs-site/loom).
 *
 * Drop-in replacement for the in-app dtrh/bridge.js, which speaks postMessage
 * to the WPF WebView2 host. On the website there is no host: everything the
 * Loom needs happens right here in the browser.
 *
 *   - The GIF is woven entirely client-side by loomWorker.js (gifenc). Nothing
 *     is uploaded and nothing is generated on a server, so producing/downloading
 *     a spiral costs the site owner zero compute and zero bandwidth.
 *   - SAVE (loom-save) -> trigger a browser download of the .gif, then hand the
 *     studio a synthetic loom-result so its UI shows success and unlocks.
 *   - The rack/library is a host feature; in download-only mode we answer the
 *     initial loom-list with an empty rack and ignore delete.
 *   - sfx / log / ready -> no-ops.
 *
 * Interface parity with the app bridge: PROTOCOL, on, send, log,
 * announceReady, isHosted.
 * ==========================================================================*/

export const PROTOCOL = 1;

// No WebView2 host on the web. Studio code that checks this stays on its
// browser-friendly paths.
export const isHosted = false;

const handlers = new Map();   // type -> fn

/** Register a handler for a host->page message type. */
export function on(type, fn) { handlers.set(type, fn); }

/** Deliver a synthetic host->page message to whatever registered for it. */
function emit(msg) {
  const h = handlers.get(msg.type);
  if (h) h(msg);
}

const slugify = (name) => String(name || '').toLowerCase().trim()
  .replace(/[^a-z0-9_-]+/g, '-').replace(/^-+|-+$/g, '').slice(0, 24);

function bytesFromB64(b64) {
  const bin = atob(b64);
  const out = new Uint8Array(bin.length);
  for (let i = 0; i < bin.length; i++) out[i] = bin.charCodeAt(i);
  return out;
}

/** SAVE clicked: the worker's finished GIF arrives here as base64. */
function downloadGif(msg) {
  const slug = slugify(msg.name) || 'spiral';
  try {
    const blob = new Blob([bytesFromB64(msg.gifBase64)], { type: 'image/gif' });
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = slug + '.gif';
    document.body.appendChild(a);
    a.click();
    a.remove();
    setTimeout(() => URL.revokeObjectURL(url), 4000);
    // Close the studio's loop the same way the host would.
    emit({ type: 'loom-result', op: 'save', ok: true, slug });
  } catch (e) {
    emit({ type: 'loom-result', op: 'save', ok: false, error: 'bad-gif' });
  }
}

/** Page->host in the app; here we service the few types that matter locally. */
export function send(msg) {
  if (!msg || typeof msg.type !== 'string') return;
  switch (msg.type) {
    case 'loom-save':
      downloadGif(msg);
      break;
    // sfx, log, ready, loom-delete: nothing to do on the web.
    default:
      break;
  }
}

export function log() { /* no host console to forward to */ }

/**
 * The app host flushes its saved-spiral library on ready. Download-only has no
 * library, so answer with an empty rack so the strip renders (and the "rack is
 * full" guard never trips).
 */
export function announceReady() {
  emit({ type: 'loom-list', spirals: [] });
}
