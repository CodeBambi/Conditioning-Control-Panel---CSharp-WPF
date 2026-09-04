/* ============================================================================
 * shell/sharedeliver.js - THE SLIP, handed over.
 *
 * `shell/sharecard.js` decides where the marks go, `shell/sharepaint.js` puts
 * ink on the paper, and this file gets the finished PNG out of the school and
 * into wherever the player wanted to post it.
 *
 * THE LADDER, in the order a player would want it:
 *
 *   1. navigator.share({files})  - a phone. The OS sheet is the right answer
 *                                  there and nothing else comes close, so it
 *                                  goes first, but ONLY when canShare says
 *                                  files are actually supported: canShare
 *                                  exists on hosts that will then throw.
 *   2. clipboard ClipboardItem   - a desktop browser. Paste straight into
 *                                  Discord. Safari needs the PROMISE form of
 *                                  ClipboardItem, so the promise form is what
 *                                  everyone gets - Chrome takes it too.
 *   3. the host                  - WebView2 has no async clipboard image write
 *                                  worth the name, so the desktop app carries
 *                                  the PNG over the bridge and C# puts it on
 *                                  the Windows clipboard itself.
 *   4. <a download>              - the universal floor, and the rung the
 *                                  Discord Activity iframe can silently
 *                                  refuse (its sandbox drops navigations it
 *                                  did not start, and there is no event for
 *                                  that). Which is why rung 5 exists.
 *   5. nothing                   - and the player is TOLD so, out loud. A
 *                                  share that quietly did not happen is the
 *                                  worst of the five outcomes, not the least.
 *
 * Every rung is feature-tested before it is tried and every rung is wrapped:
 * a host that has the API and refuses anyway simply falls to the next one.
 * ==========================================================================*/

/** What actually happened. The caller turns one of these into a toast. */
export const DELIVERED = Object.freeze({
  SHARED: 'shared',
  COPIED: 'copied',
  SAVED: 'saved',
  NONE: 'none',
});

/** Biggest PNG we will push across the host bridge, as raw bytes. */
export const MAX_BRIDGE_BYTES = 3 * 1024 * 1024;

/** A File, when this host has one. Some webviews ship Blob and not File. */
function asFile(blob, fileName) {
  try {
    if (typeof File !== 'function') return null;
    return new File([blob], String(fileName || 'report.png'), { type: 'image/png' });
  } catch (e) { return null; }
}

/**
 * RUNG 1. The OS share sheet, files and all.
 *
 * `canShare` is the gate rather than `share`: every host that can share TEXT
 * has `navigator.share`, and handing one of those a file list is how you get a
 * rejected promise instead of a sheet.
 */
export async function shareFile(blob, fileName) {
  try {
    const nav = (typeof navigator !== 'undefined') ? navigator : null;
    if (!nav || typeof nav.share !== 'function' || typeof nav.canShare !== 'function') return false;
    const file = asFile(blob, fileName);
    if (!file) return false;
    if (!nav.canShare({ files: [file] })) return false;
    await nav.share({ files: [file] });
    return true;
  } catch (e) {
    /* AbortError is the player closing the sheet, which is not a failure of
     * this rung - but it is also not a share, and falling through to the
     * clipboard after someone deliberately backed out would be rude. */
    if (e && e.name === 'AbortError') return true;
    return false;
  }
}

/**
 * RUNG 2. The clipboard, as an image.
 *
 * The PROMISE form of ClipboardItem on purpose: Safari only honours a
 * clipboard write that was issued in the user gesture, and it accepts a
 * pending promise as the payload precisely so a slow encode does not lose the
 * gesture. Chrome accepts the same shape, so there is one code path.
 */
export async function copyImage(blob) {
  try {
    if (typeof ClipboardItem !== 'function') return false;
    const nav = (typeof navigator !== 'undefined') ? navigator : null;
    if (!nav || !nav.clipboard || typeof nav.clipboard.write !== 'function') return false;
    const item = new ClipboardItem({ 'image/png': Promise.resolve(blob) });
    await nav.clipboard.write([item]);
    return true;
  } catch (e) { return false; }
}

/**
 * The PNG as bare base64 (no `data:` prefix), or null.
 *
 * `arrayBuffer()` + `btoa` first because it is the shorter road and it works
 * everywhere this page runs; FileReader is the fallback for an older webview
 * that has Blob but not Blob.arrayBuffer. Chunked through fromCharCode because
 * spreading 300,000 bytes into one call blows the argument limit.
 */
export async function blobToBase64(blob) {
  try {
    if (!blob) return null;
    if (typeof blob.arrayBuffer === 'function' && typeof btoa === 'function') {
      const bytes = new Uint8Array(await blob.arrayBuffer());
      let bin = '';
      for (let i = 0; i < bytes.length; i += 8192) {
        bin += String.fromCharCode.apply(null, bytes.subarray(i, i + 8192));
      }
      return btoa(bin);
    }
  } catch (e) { /* fall through to the reader */ }
  return new Promise((resolve) => {
    try {
      if (typeof FileReader !== 'function') { resolve(null); return; }
      const fr = new FileReader();
      fr.onerror = () => resolve(null);
      fr.onload = () => {
        const s = String(fr.result || '');
        const comma = s.indexOf(',');
        resolve(comma >= 0 ? s.slice(comma + 1) : null);
      };
      fr.readAsDataURL(blob);
    } catch (e) { resolve(null); }
  });
}

/**
 * RUNG 3. Over the bridge, onto the Windows clipboard.
 *
 * `toHost` is the shell's own round trip (it owns the bridge; this module must
 * not). Size-capped before the encode, because a base64 string is a third
 * bigger again and the message channel is not a file transfer.
 */
export async function copyViaHost(blob, toHost) {
  try {
    if (typeof toHost !== 'function' || !blob) return false;
    if (!blob.size || blob.size > MAX_BRIDGE_BYTES) return false;
    const png = await blobToBase64(blob);
    if (!png) return false;
    return (await toHost(png)) === true;
  } catch (e) { return false; }
}

/**
 * Walk the ladder and report which rung caught.
 *
 * @param {Blob} blob
 * @param {string} fileName
 * @param {Object} opts  {toHost, download} - both optional; a missing one is
 *                       simply a rung this host does not have.
 * @returns {Promise<string>} one of DELIVERED
 */
export async function deliverShareCard(blob, fileName, opts) {
  const o = opts || {};
  if (!blob) return DELIVERED.NONE;
  if (await shareFile(blob, fileName)) return DELIVERED.SHARED;
  if (await copyImage(blob)) return DELIVERED.COPIED;
  if (await copyViaHost(blob, o.toHost)) return DELIVERED.COPIED;
  try {
    if (typeof o.download === 'function' && o.download(blob, fileName) === true) {
      return DELIVERED.SAVED;
    }
  } catch (e) { /* the floor is allowed to give way; rung 5 catches */ }
  return DELIVERED.NONE;
}

export default deliverShareCard;
