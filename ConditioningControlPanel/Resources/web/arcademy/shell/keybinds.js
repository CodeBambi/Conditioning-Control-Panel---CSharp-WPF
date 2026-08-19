/* ============================================================================
 * shell/keybinds.js - ONE keybind framework (SYNTHESIS #7).
 *
 * Five dossiers asked for their own key (lf_peek_key, goKeyBinding, echoPadKeys,
 * composurePeekKey, misdirectionNumberKeys). They all ride this instead: a game
 * DECLARES verb slots in its manifest
 *
 *     keybinds: [{ verb:'peek', label_key:'lf_peek_key', default:'Space' }]
 *
 * and the shell owns the UI, the storage and the conflict checking. Games never
 * read a raw key: ctx.keys.on('peek', fn) is the only surface, so rebinding is
 * free and a game cannot bind something it did not declare.
 *
 * STORAGE: one flat map, one setting - `keybinds`, persisted by the host as
 * AppSettings.ArcademyKeybindsJson - keyed '<gameKey>.<verb>'. One blob keeps C#
 * to a single clamped property instead of one per verb per game (DTRH's per-game
 * key1/key2 pattern, widened).
 *
 * PANIC KEY: the app's emergency stop always wins (GROUND-RULES §5). If the init
 * projection carries a panic key we refuse to bind it and say why. If it does NOT
 * (the field is absent because the host build predates it), we skip the check
 * gracefully rather than inventing a default - a wrong guess here would either
 * block a legal key or, worse, quietly allow the panic key to be stolen.
 * ==========================================================================*/

/** The `set-setting` key for the whole map. The host stores it as JSON itself
 *  (AppSettings.ArcademyKeybindsJson), so we send an OBJECT, not a string - it
 *  refuses a string outright (`value is JObject`) and the rebind would vanish. */
export const KEYBINDS_SETTING_KEY = 'keybinds';

/** Keys the shell reserves for its own ladder / chrome. */
const RESERVED = Object.freeze(['Escape', 'F11', 'Tab']);

/** Normalize a KeyboardEvent (or a stored string) into our canonical form. */
export function normalizeKey(k) {
  if (!k) return '';
  const raw = (typeof k === 'string') ? k : (k.code || k.key || '');
  const s = String(raw);
  if (s === ' ' || s === 'Spacebar' || s === 'Space') return 'Space';
  if (/^Key[A-Z]$/.test(s)) return s.slice(3);           // KeyA -> A
  if (/^Digit[0-9]$/.test(s)) return s.slice(5);         // Digit4 -> 4
  if (/^Numpad[0-9]$/.test(s)) return s;                 // Numpad4 stays distinct
  if (s.length === 1) return s.toUpperCase();
  return s;
}

/** Does a KeyboardEvent match a canonical binding? */
export function eventMatches(e, binding) {
  if (!e || !binding) return false;
  const want = normalizeKey(binding);
  return normalizeKey(e.code) === want || normalizeKey(e.key) === want;
}

/** Human label for a key ('Space' -> 'Space', 'ArrowLeft' -> 'Arrow Left'). */
export function keyLabel(k) {
  const s = normalizeKey(k);
  if (!s) return '--';
  return s.replace(/([a-z])([A-Z])/g, '$1 $2');
}

/**
 * @param {Object} o
 * @param {Object} o.init      the init projection (settings + keybinds + panic key)
 * @param {Object} o.bridge
 * @param {Function=} o.log
 */
export function createKeybinds({ init, bridge, log } = {}) {
  const say = typeof log === 'function' ? log : () => {};
  const src = init || {};

  /* The host may hand keybinds over as an object or as a JSON string. */
  let map = {};
  const raw = src.keybinds;
  if (raw && typeof raw === 'object') map = Object.assign({}, raw);
  else if (typeof raw === 'string' && raw.trim()) {
    try { const p = JSON.parse(raw); if (p && typeof p === 'object') map = p; }
    catch (e) { say('keybinds: init blob is not JSON, starting empty'); }
  }

  /* PanicKey: the host projects `panicKey` / `panicKeyEnabled` at the TOP level of
     init (ArcademyHostService.BuildInit); init.settings is still checked first so a
     web shim may carry them in the settings bag instead. Absence stays legal. */
  const settings = (src.settings && typeof src.settings === 'object') ? src.settings : {};
  const panicRaw = (settings.panicKey != null) ? settings.panicKey : src.panicKey;
  const panicKey = (panicRaw == null || panicRaw === '') ? null : normalizeKey(panicRaw);
  const enabledRaw = (settings.panicKeyEnabled !== undefined) ? settings.panicKeyEnabled : src.panicKeyEnabled;
  const panicEnabled = panicKey != null && (enabledRaw === undefined ? true : !!enabledRaw);
  if (!panicKey) say('keybinds: no panic key in init - conflict check skipped (by design)');

  const declared = new Map();       // 'game.verb' -> {gameKey, verb, def, label_key}
  const slotId = (gameKey, verb) => String(gameKey) + '.' + String(verb);

  function save() {
    if (!bridge || typeof bridge.send !== 'function') return;
    bridge.send({ type: 'set-setting', key: KEYBINDS_SETTING_KEY, value: Object.assign({}, map) });
  }

  const api = {
    panicKey,
    panicEnabled,

    /** Register a game's declared slots (idempotent). */
    declare(gameKey, list) {
      const out = [];
      for (const slot of (Array.isArray(list) ? list : [])) {
        if (!slot || !slot.verb) continue;
        const id = slotId(gameKey, slot.verb);
        const entry = {
          id,
          gameKey: String(gameKey),
          verb: String(slot.verb),
          def: normalizeKey(slot.default) || '',
          labelKey: slot.label_key || slot.labelKey || null,
        };
        declared.set(id, entry);
        out.push(entry);
      }
      return out;
    },

    slotsFor(gameKey) {
      return Array.from(declared.values()).filter((s) => s.gameKey === String(gameKey));
    },

    /** Effective binding for a declared verb (stored, else manifest default). */
    get(gameKey, verb) {
      const id = slotId(gameKey, verb);
      const stored = map[id];
      if (stored) return normalizeKey(stored);
      const d = declared.get(id);
      return d ? d.def : '';
    },

    /**
     * Why `key` cannot be bound to this slot, or null if it can.
     * @returns {{reason:'panic'|'reserved'|'taken', with?:string}|null}
     */
    conflict(gameKey, verb, key) {
      const k = normalizeKey(key);
      if (!k) return { reason: 'reserved' };
      if (RESERVED.indexOf(k) >= 0) return { reason: 'reserved' };
      if (panicEnabled && panicKey && k === panicKey) return { reason: 'panic' };
      const me = slotId(gameKey, verb);
      for (const slot of api.slotsFor(gameKey)) {
        if (slot.id === me) continue;
        if (api.get(slot.gameKey, slot.verb) === k) return { reason: 'taken', with: slot.verb };
      }
      return null;
    },

    /**
     * Bind a key. Refuses on conflict (and says so) - the caller shows the reason.
     * @returns {{ok:boolean, conflict?:Object}}
     */
    bind(gameKey, verb, key) {
      const id = slotId(gameKey, verb);
      if (!declared.has(id)) {
        say('keybinds: refused undeclared slot ' + id);
        return { ok: false, conflict: { reason: 'reserved' } };
      }
      const k = normalizeKey(key);
      const c = api.conflict(gameKey, verb, k);
      if (c) return { ok: false, conflict: c };
      map[id] = k;
      save();
      return { ok: true };
    },

    /** Back to the manifest default. */
    reset(gameKey, verb) {
      delete map[slotId(gameKey, verb)];
      save();
    },

    /** Accept the host's clamped echo of the blob. */
    applyEcho(value) {
      if (typeof value === 'string') {
        try { const p = JSON.parse(value); if (p && typeof p === 'object') map = p; }
        catch (e) { say('keybinds: echo was not JSON'); }
      } else if (value && typeof value === 'object') {
        map = Object.assign({}, value);
      }
    },

    /**
     * The runtime surface handed to a game as ctx.keys. Listens on the CLASS ROOT
     * where possible so a keypress typed into a settings field can never fire a
     * game verb; falls back to window for a game that wants global keys.
     */
    runtime(gameKey, target) {
      const handlers = new Map();      // verb -> Set(fn)
      const node = target || (typeof window !== 'undefined' ? window : null);

      const onKeyDown = (e) => {
        if (e.repeat) return;
        for (const slot of api.slotsFor(gameKey)) {
          const binding = api.get(slot.gameKey, slot.verb);
          if (!binding || !eventMatches(e, binding)) continue;
          const set = handlers.get(slot.verb);
          if (!set || !set.size) continue;
          e.preventDefault();
          for (const fn of Array.from(set)) {
            try { fn({ verb: slot.verb, key: binding, event: e, down: true }); }
            catch (err) { say('keybind ' + slot.verb + ' threw: ' + ((err && err.message) || err)); }
          }
        }
      };
      const onKeyUp = (e) => {
        for (const slot of api.slotsFor(gameKey)) {
          const binding = api.get(slot.gameKey, slot.verb);
          if (!binding || !eventMatches(e, binding)) continue;
          const set = handlers.get(slot.verb + ':up');
          if (!set) continue;
          for (const fn of Array.from(set)) {
            try { fn({ verb: slot.verb, key: binding, event: e, down: false }); }
            catch (err) { say('keybind up ' + slot.verb + ' threw: ' + ((err && err.message) || err)); }
          }
        }
      };
      if (node) {
        node.addEventListener('keydown', onKeyDown);
        node.addEventListener('keyup', onKeyUp);
      }

      return {
        /** on('peek', fn) for press; on('peek:up', fn) for release. */
        on(verb, fn) {
          if (typeof fn !== 'function') return () => {};
          const key = String(verb);
          let set = handlers.get(key);
          if (!set) { set = new Set(); handlers.set(key, set); }
          set.add(fn);
          return () => set.delete(fn);
        },
        /** The current binding for a declared verb (for on-screen hints). */
        keyFor(verb) { return api.get(gameKey, verb); },
        labelFor(verb) { return keyLabel(api.get(gameKey, verb)); },
        destroy() {
          handlers.clear();
          if (node) {
            node.removeEventListener('keydown', onKeyDown);
            node.removeEventListener('keyup', onKeyUp);
          }
        },
      };
    },
  };

  return api;
}

export default createKeybinds;
