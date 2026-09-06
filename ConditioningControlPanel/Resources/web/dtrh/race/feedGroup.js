/* ============================================================================
 * race/feedGroup.js - the ONLINE FEED group inside the menu's `your media`
 * panel. Web only.
 *
 *   createFeedGroup({ slot, settings, post, ui }) ->
 *     { rows, els, paint(), settingEcho(frame) -> bool, dispose() } | null
 *
 * WHERE IT LIVES. race/menu.js builds the `your media` panel and ends it with an
 * empty `.rm-media-more` node (its `mediaSlot`). This group builds into that
 * node, so the heading, the consent row and the niche rows sit under the
 * pickers and above `back`, and `back` stays the last thing a thumb or an arrow
 * reaches. `rows` and `els` are handed back so the menu can splice them into the
 * panel's own walk; they are the shape MEDIA_ROWS already uses.
 *
 * WHEN IT EXISTS AT ALL. Two conditions, both from the host's `init.settings`:
 * `mediaControls === true` (the capability flag) AND a non-empty
 * `remoteCatalog`. The browser host shim (cclabs-web scripts/race-web-ext) ships
 * both; the C# desktop host ships neither, so on the desktop this returns null,
 * nothing is built and no `media.*` key can ever be posted. Same capability law
 * the Arcademy campus settings room lives under, RACE-MEDIA-CONTRACT section 2.
 *
 * WHAT THE PLAYER IS AGREEING TO. The line under the heading is the whole
 * truth and it is said plainly: the browser fetches api.scrolller.com ITSELF,
 * no CC Labs server proxies a byte, and what comes back is adult media out of
 * the public subreddits ticked underneath. Consent gates the NETWORK, not the
 * disk: with it off nothing is fetched, and the player's own picked files (the
 * pickers above) still work exactly as they did.
 *
 * ONLY THE ECHO MOVES A SETTING (RACE-MEDIA-CONTRACT section 0). A press posts
 * `set-setting` and paints the row `pending`; the value only changes when the
 * host's `setting` frame lands carrying WHAT IS STORED. So a refused list - the
 * host refuses an empty one - snaps the row back to the truth instead of lying
 * about it, and nothing in here keeps a local copy of the answer.
 *
 * `media.niches` is ONE key carrying the WHOLE list, so ticking any niche paints
 * EVERY niche row pending until the one echo lands. That is the contract, not a
 * shortcut.
 *
 * NOBODY ASKS FOR A MANIFEST FROM HERE. The host rebuilds and re-posts it after
 * a setting lands, and raceBoot's `manifest` handler feeds hostMedia whenever a
 * frame arrives - the pool is mutable and payloadFx holds the same object - so a
 * feed switched on with the menu open is on the walls of the very next run with
 * nothing re-created. race/smoke/remote-feed-check.mjs holds that property down.
 * ==========================================================================*/

/** The two `set-setting` keys this group owns (RACE-MEDIA-CONTRACT section 4). */
export const CONSENT_KEY = 'media.remoteConsent';
export const NICHES_KEY = 'media.niches';

/** The heading, the one honest line, and the label over the niche rows. */
export const HEADING = 'online feed';
export const CONSENT_LINE = 'this device pulls the feed straight off the source site. none of it passes through cc labs. it is adult media, out of the public subreddits ticked below.';
export const NICHES_LABEL = 'what it pulls';

/** How long a row waits for its echo before it stops saying `pending`. A host
 *  that never answers must not leave a row dead on the screen. Mirrors the
 *  pickers' own wait in race/menu.js. */
const ECHO_WAIT_MS = 6000;

const elm = (tag, cls, parent, text) => {
  const d = document.createElement(tag);
  d.className = cls;
  if (text != null) d.textContent = text;
  parent.appendChild(d);
  return d;
};

/**
 * @param {object}   o
 * @param {Element}  o.slot      the menu's `.rm-media-more` node (menu.mediaSlot)
 * @param {object}   o.settings  the host's `init.settings`
 * @param {function} o.post      send a frame to the host (menu's own `post`)
 * @param {function} [o.ui]      the menu blips, so a press sounds like every other press
 */
export function createFeedGroup({ slot, settings = {}, post, ui = null }) {
  if (!slot || typeof post !== 'function') return null;
  // The capability flag is checked STRICTLY: a stringy true is still no.
  if (settings.mediaControls !== true) return null;
  const catalog = (Array.isArray(settings.remoteCatalog) ? settings.remoteCatalog : [])
    .filter((r) => r && typeof r.id === 'string' && r.id)
    // the panel speaks in lower case like the rest of the menu; the ID is never touched
    .map((r) => ({ id: r.id, label: String(r.label == null ? r.id : r.label).toLowerCase() }));
  if (!catalog.length) return null;

  const known = new Set(catalog.map((c) => c.id));
  let consent = settings.remoteConsent === true;
  let picked = new Set((Array.isArray(settings.niches) ? settings.niches : [])
    .filter((v) => typeof v === 'string' && known.has(v)));
  const waiting = new Map();          // setting key -> the give-up timer
  const blip = (n) => { try { if (typeof ui === 'function') ui(n); } catch (e) { /* audio gone */ } };

  /* ---- the DOM ---------------------------------------------------------- */
  elm('h3', 'rm-h rm-feed-h', slot, HEADING);
  elm('div', 'rm-hint rm-feed-line', slot, CONSENT_LINE);
  const consentRow = row('the feed');
  elm('div', 'rm-hint rm-feed-sub', slot, NICHES_LABEL);
  const nicheRows = catalog.map((c) => row(c.label));

  /** One row: a button carrying its label and its state word, built the way the
   *  panel's own picker buttons are so it inherits their focus, hit and pending
   *  paint for free. */
  function row(label) {
    const b = elm('button', 'rm-btn rm-media-btn rm-feed-btn', slot);
    b.type = 'button';
    b.setAttribute('role', 'menuitem');
    elm('span', 'rm-feed-label', b, label);
    const val = elm('span', 'rm-feed-val', b, '');
    return { el: b, val };
  }

  /* ---- what a row says -------------------------------------------------- */
  const word = (key, on) => (waiting.has(key) ? 'pending' : (on ? 'on' : 'off'));

  function paint() {
    const c = word(CONSENT_KEY, consent);
    if (consentRow.val.textContent !== c) consentRow.val.textContent = c;
    consentRow.el.classList.toggle('is-pending', waiting.has(CONSENT_KEY));
    for (let i = 0; i < catalog.length; i++) {
      const r = nicheRows[i];
      const v = word(NICHES_KEY, picked.has(catalog[i].id));
      if (r.val.textContent !== v) r.val.textContent = v;
      r.el.classList.toggle('is-pending', waiting.has(NICHES_KEY));
      // the niches only mean anything while the feed is on, so they read as
      // inert until it is. Still pressable: picking ahead of consent is a real
      // thing to want, and it costs no network.
      r.el.classList.toggle('is-off', !consent);
    }
  }

  /* ---- pressing one ----------------------------------------------------- */
  /** Post the key and paint pending. Nothing is stored here and nothing waits on
   *  a reply: the echo is the only thing that moves a value. */
  function send(key, value) {
    if (waiting.has(key)) return;      // one ask at a time per key
    const timer = setTimeout(() => { waiting.delete(key); paint(); }, ECHO_WAIT_MS);
    waiting.set(key, timer);
    try { post({ type: 'set-setting', key, value }); }
    catch (e) { /* no host: the row simply stays pending, which is the truth */ }
    paint();
  }

  /** The whole list, with this one flipped. `media.niches` carries every id the
   *  player wants, never a delta, so the host stores exactly what it is shown. */
  const toggled = (id) => catalog
    .filter((c) => (c.id === id ? !picked.has(id) : picked.has(c.id)))
    .map((c) => c.id);

  const rows = [
    { id: 'feed', label: 'the feed', press: () => send(CONSENT_KEY, !consent) },
    ...catalog.map((c) => ({ id: `niche-${c.id}`, label: c.label, press: () => send(NICHES_KEY, toggled(c.id)) })),
  ];
  const els = [consentRow.el, ...nicheRows.map((r) => r.el)];

  // A pointer press goes through the same act() the pad and the keyboard do, so
  // the menu's own focus index follows the finger. The menu wires that up when
  // it splices these rows in; here we only make the button reachable.
  els.forEach((b, i) => { b.dataset.id = rows[i].id; });

  paint();

  return {
    rows,
    els,
    paint,
    /**
     * The host's `setting` echo. Returns true when this group owned the key, so
     * the menu knows not to hand it on to the pickers' own pending paint.
     */
    settingEcho(m) {
      const key = m && typeof m.key === 'string' ? m.key : '';
      if (key !== CONSENT_KEY && key !== NICHES_KEY) return false;
      const timer = waiting.get(key);
      if (timer) clearTimeout(timer);
      waiting.delete(key);
      // A `null` echo is a refusal with nothing stored to show, so the row keeps
      // the value it had and simply stops saying pending.
      if (key === CONSENT_KEY) { if (m.value != null) consent = m.value === true; }
      else if (Array.isArray(m.value)) {
        picked = new Set(m.value.filter((v) => typeof v === 'string' && known.has(v)));
      }
      paint();
      blip('tick');
      return true;
    },
    dispose() {
      for (const t of waiting.values()) { try { clearTimeout(t); } catch (e) { /* noop */ } }
      waiting.clear();
    },
  };
}

export default createFeedGroup;
