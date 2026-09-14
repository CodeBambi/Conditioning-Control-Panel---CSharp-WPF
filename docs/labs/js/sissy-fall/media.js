/* ============================================================================
 * media.js - client-side media pool for the Sissy Fall. NOTHING IS UPLOADED.
 *
 * Ingest paths (best first):
 *   1. showDirectoryPicker() / drag-drop directory HANDLES (Chromium): we hold
 *      lazy FileSystemFileHandle refs and read a file only when a card needs
 *      it - a 2 GB folder costs nothing until (and unless) files are shown.
 *   2. <input webkitdirectory multiple> / plain file drops (Firefox, Safari):
 *      File objects, still lazy (a File is a handle until you read it).
 *
 * Session-only by design: no copies, no persistence; a reload forgets the drop.
 *
 * Memory contract: entries hold handles/Files, never bytes. acquire() turns an
 * entry into a blob: URL right before a card spawns; release() revokes it on
 * despawn. Only the ~dozen live cards ever hold decoded media.
 * ==========================================================================*/

const IMG_EXT = new Set(['jpg', 'jpeg', 'png', 'webp', 'gif']);
const VID_EXT = new Set(['mp4', 'webm', 'm4v']);
const MAX_IMG_BYTES = 50 * 1024 * 1024;   // skip absurd single files
const MAX_VID_BYTES = 500 * 1024 * 1024;
const MAX_FILES = 5000;
const MAX_WALK_DEPTH = 8;
const NO_ECHO = 8; // a reshuffled deck avoids repeating the last N draws

const extOf = (name) => {
  const i = name.lastIndexOf('.');
  return i < 0 ? '' : name.slice(i + 1).toLowerCase();
};
const kindOf = (name) => {
  const e = extOf(name);
  if (IMG_EXT.has(e)) return 'image';
  if (VID_EXT.has(e)) return 'video';
  return null;
};

export function createMediaSource() {
  const entries = [];   // { kind, name, file?, handle? }
  let skipped = 0;
  let deck = [];        // shuffled indices into entries, drawn from the end
  const recent = [];    // last NO_ECHO drawn indices (echo guard for tiny pools)

  const counts = () => {
    let images = 0, videos = 0;
    for (const e of entries) (e.kind === 'image' ? images++ : videos++);
    return { images, videos, skipped };
  };

  function addFile(file) {
    if (entries.length >= MAX_FILES) return;
    const kind = kindOf(file.name);
    if (!kind) { skipped++; return; }
    if (kind === 'image' && file.size > MAX_IMG_BYTES) { skipped++; return; }
    if (kind === 'video' && file.size > MAX_VID_BYTES) { skipped++; return; }
    entries.push({ kind, name: file.name, file });
  }
  function addHandle(handle) {
    if (entries.length >= MAX_FILES) return;
    const kind = kindOf(handle.name);
    if (!kind) { skipped++; return; }
    entries.push({ kind, name: handle.name, handle }); // size checked at acquire
  }

  async function walkDir(dirHandle, depth) {
    if (depth > MAX_WALK_DEPTH || entries.length >= MAX_FILES) return;
    try {
      for await (const [name, h] of dirHandle.entries()) {
        if (entries.length >= MAX_FILES) return;
        if (name.startsWith('.')) continue;
        if (h.kind === 'directory') await walkDir(h, depth + 1);
        else addHandle(h);
      }
    } catch (e) { /* permission hiccup on a subfolder: keep what we got */ }
  }

  function reshuffle() {
    deck = entries.map((_, i) => i);
    for (let i = deck.length - 1; i > 0; i--) {
      const j = (Math.random() * (i + 1)) | 0;
      [deck[i], deck[j]] = [deck[j], deck[i]];
    }
    // echo guard: push recently-drawn indices to the bottom of the fresh deck
    if (entries.length > NO_ECHO) {
      for (const r of recent) {
        const at = deck.indexOf(r);
        if (at >= 0) { deck.splice(at, 1); deck.unshift(r); }
      }
    }
  }

  function removeEntry(index) {
    entries.splice(index, 1);
    deck = deck.map((i) => (i > index ? i - 1 : i)).filter((i) => i !== index && i < entries.length);
    for (let k = recent.length - 1; k >= 0; k--) {
      if (recent[k] === index) recent.splice(k, 1);
      else if (recent[k] > index) recent[k] -= 1;
    }
  }

  // Turn an entry into a live blob URL. Rejects (returns null) if the file
  // vanished mid-session (moved/deleted on disk) - the entry is dropped.
  function makeAcquire(entry) {
    return async function acquire() {
      let file = entry.file;
      // zip-backed entry: the bytes already live in a (disk-backed) Blob, made
      // once at ingest - just hand out an object URL, no decompression here.
      if (!file && entry.blob) {
        const url = URL.createObjectURL(entry.blob);
        let released = false;
        return { url, release() { if (!released) { released = true; URL.revokeObjectURL(url); } } };
      }
      if (!file && entry.handle) {
        try {
          file = await entry.handle.getFile();
        } catch (e) {
          const at = entries.indexOf(entry);
          if (at >= 0) removeEntry(at);
          return null;
        }
        const cap = entry.kind === 'image' ? MAX_IMG_BYTES : MAX_VID_BYTES;
        if (file.size > cap) {
          const at = entries.indexOf(entry);
          if (at >= 0) removeEntry(at);
          return null;
        }
      }
      if (!file) return null;
      const url = URL.createObjectURL(file);
      let released = false;
      return {
        url,
        release() { if (!released) { released = true; URL.revokeObjectURL(url); } },
      };
    };
  }

  function drawIndex() {
    if (!entries.length) return -1;
    if (!deck.length) reshuffle();
    const i = deck.pop();
    if (i == null || i >= entries.length) return drawIndexSafe();
    recent.push(i);
    if (recent.length > NO_ECHO) recent.shift();
    return i;
  }
  function drawIndexSafe() {
    if (!entries.length) return -1;
    reshuffle();
    return deck.length ? deck.pop() : -1;
  }

  window.__sfMedia = counts; // panel diagnostics read the pool size live

  return {
    supportsFS: () => 'showDirectoryPicker' in window,

    // Folder picker (Chromium). Resolves with stats, or null if the user bailed.
    async pickFolder() {
      try {
        const dir = await window.showDirectoryPicker({ mode: 'read' });
        await walkDir(dir, 0);
        deck = []; // re-deal with the new entries in the mix
        return counts();
      } catch (e) {
        if (e && e.name === 'AbortError') return null;
        console.warn('[sissy-fall] folder pick failed:', e);
        return null;
      }
    },

    // Drag-drop: directory or file handles where supported, plain Files elsewhere.
    async handleDrop(dataTransfer) {
      const items = Array.from(dataTransfer.items || []);
      const jobs = [];
      for (const item of items) {
        if (item.kind !== 'file') continue;
        if (item.getAsFileSystemHandle) {
          jobs.push(item.getAsFileSystemHandle().then(async (h) => {
            if (!h) return;
            if (h.kind === 'directory') await walkDir(h, 0);
            else addHandle(h);
          }).catch(() => {}));
        } else {
          const f = item.getAsFile();
          if (f) addFile(f);
        }
      }
      await Promise.all(jobs);
      deck = [];
      return counts();
    },

    // <input webkitdirectory multiple> fallback.
    addFileList(fileList) {
      for (const f of Array.from(fileList || [])) addFile(f);
      deck = [];
      return counts();
    },

    // Ingest a .zip - the mobile-friendly path (phones can't pick folders).
    // onProgress(fraction 0..1, phase) drives the loading bar. Each media entry
    // is inflated once into a Blob and the compressed buffer is then dropped, so
    // the session holds no giant ArrayBuffer (Safari disk-backs the Blobs) - the
    // memory model that keeps iOS from OOM-crashing the tab.
    async addZip(file, onProgress) {
      const report = (frac, phase) => { if (onProgress) onProgress(Math.min(1, Math.max(0, frac)), phase); };
      const total = file.size || 0;

      // 1. Read the bytes into memory, streaming for a real progress bar when
      //    the browser supports it (Safari 14.1+, all Chromium/Firefox).
      let data;
      try {
        if (typeof file.stream === 'function' && total) {
          // Pre-allocate the WHOLE buffer once and write chunks straight into it.
          // The old chunk-array + combine step briefly held the zip TWICE, which
          // blew iOS Safari's per-tab memory ceiling right as the read finished
          // (the "a problem repeatedly occurred" crash). One buffer halves the peak.
          const reader = file.stream().getReader();
          data = new Uint8Array(total);
          let got = 0;
          for (;;) {
            const { done, value } = await reader.read();
            if (done) break;
            if (got + value.length > data.length) { // file.size under-reported: grow once
              const grown = new Uint8Array(got + value.length);
              grown.set(data.subarray(0, got)); grown.set(value, got);
              data = grown;
            } else {
              data.set(value, got);
            }
            got += value.length;
            report((got / total) * 0.85, 'reading');
          }
          if (got < data.length) data = data.subarray(0, got); // trim if size over-reported
        } else {
          data = new Uint8Array(await file.arrayBuffer());
          report(0.85, 'reading');
        }
      } catch (e) {
        try { data = new Uint8Array(await file.arrayBuffer()); }
        catch (e2) { console.warn('[sissy-fall] zip read failed:', e2); report(1, 'done'); return counts(); }
      }

      // 2. Load the unzip lib, then walk the central directory WITHOUT inflating
      //    anything (the filter returns false for every record) - just collect
      //    which media entries we want.
      report(0.9, 'unpacking');
      let unzipSync;
      try {
        ({ unzipSync } = await import('../../assets/vendor/fflate/fflate.module.js'));
      } catch (e) {
        console.warn('[sissy-fall] unzip lib failed to load:', e);
        report(1, 'done');
        return counts();
      }
      const wanted = [];
      try {
        unzipSync(data, { filter: (f) => {
          if (wanted.length >= MAX_FILES) return false;
          const name = f.name;
          if (!name || name.endsWith('/')) return false;         // directory record
          if (name.startsWith('__MACOSX')) return false;          // macOS resource forks
          const base = name.slice(name.lastIndexOf('/') + 1);
          if (!base || base.startsWith('.')) return false;        // dotfiles / ._ junk
          const kind = kindOf(base);
          if (!kind) { skipped++; return false; }
          const cap = kind === 'image' ? MAX_IMG_BYTES : MAX_VID_BYTES;
          if (f.originalSize > cap) { skipped++; return false; }
          wanted.push({ kind, base, entryName: name });
          return false; // never decompress during the walk
        } });
      } catch (e) {
        console.warn('[sissy-fall] not a readable zip:', e);
      }

      // 3. Inflate entries ONE AT A TIME into Blobs, then drop the compressed
      //    buffer. Safari disk-backs large Blobs (off the tight web-process
      //    heap), so the session never holds the whole archive in memory - the
      //    iOS-safe model. Yield periodically so the loader can paint.
      for (let i = 0; i < wanted.length; i++) {
        const w = wanted[i];
        try {
          const bytes = unzipSync(data, { filter: (f) => f.name === w.entryName })[w.entryName];
          if (bytes && bytes.length) {
            entries.push({ kind: w.kind, name: w.base, blob: new Blob([bytes]) });
          }
        } catch (e) { /* skip one corrupt entry, keep the rest */ }
        report(0.9 + 0.1 * ((i + 1) / wanted.length), 'unpacking');
        if ((i & 7) === 7) await new Promise((r) => setTimeout(r)); // let the UI/GC breathe
      }
      data = null; // release the compressed zip - the Blobs own their bytes now

      deck = []; // re-deal with the new entries mixed in
      report(1, 'done');
      return counts();
    },

    hasUserMedia: () => entries.length > 0,
    stats: counts,

    // Draw the next entry from the shuffled deck (null when the pool is empty).
    draw() {
      const i = drawIndex();
      if (i < 0) return null;
      const e = entries[i];
      return { kind: e.kind, name: e.name, acquire: makeAcquire(e) };
    },

    // Draw specifically an image/video (used when the live-video cap is hit).
    drawKind(kind) {
      if (!entries.some((e) => e.kind === kind)) return null;
      for (let tries = 0; tries < 24; tries++) {
        const i = drawIndex();
        if (i < 0) return null;
        const e = entries[i];
        if (e.kind === kind) return { kind: e.kind, name: e.name, acquire: makeAcquire(e) };
      }
      return null;
    },
  };
}
