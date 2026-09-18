/* ============================================================================
 * backroom/room/welcome.js - the first-visit card (CONTRACT section 13).
 *
 * Shown once, over the room, the moment the loading veil lifts: the hero art on
 * top, the title, three numbered steps (walk in, take a seat, spend your
 * sparkles) and one button. Dismissed by the button, Escape, Enter, Space or a
 * tap on the veil. It is the loader's card chrome (.br-card-veil / .br-card),
 * so it looks like the room's own furniture and not a tutorial bolted on.
 *
 * WHO REMEMBERS: the host. Dismissing posts one room-option {welcomeSeen:true}
 * through main.js's setOption, the host writes AppSettings.BackRoomWelcomeSeen
 * and echoes it as init.welcomeSeen on the next open. No localStorage - the
 * room keeps none, on purpose - so the card follows the account, not the
 * browser profile.
 *
 * KEYS: while the card is up every key is swallowed at the window's capture
 * phase, so W/S cannot walk a room the player cannot see and E cannot seat
 * them at a station behind the veil. The pointer never reaches the stage
 * either: the veil is a full-inset node in #br-layer. main.js's own Escape
 * handler is registered earlier on the same target, so it asks this card
 * first (dismiss() answers true when it closed something) before it walks out
 * of the room.
 *
 * DOM only through createElement / append / remove / addEventListener, so the
 * node suite can run it against a stub document (trap: #1371 shipped a render
 * loop nothing executed).
 * ==========================================================================*/

/** The three steps: lexicon keys, with the phone wording where the desk one names keys. */
export const STEPS = Object.freeze([
  { lead: 'br_welcome_step1_lead', body: 'br_welcome_step1', touch: 'br_welcome_step1_touch' },
  { lead: 'br_welcome_step2_lead', body: 'br_welcome_step2', touch: 'br_welcome_step2_touch' },
  { lead: 'br_welcome_step3_lead', body: 'br_welcome_step3', touch: null },
]);

export const FALLBACK = Object.freeze({
  br_welcome_title: 'Welcome to the Back Room',
  br_welcome_sub: 'EMI keeps score. Three things to know.',
  br_welcome_step1_lead: 'Walk in',
  br_welcome_step1: 'W/S or the arrows to walk, drag to look around.',
  br_welcome_step1_touch: 'Push the stick to walk, drag to look around.',
  br_welcome_step2_lead: 'Take a seat',
  br_welcome_step2: 'Walk up to a station and press E, or tap it. Step back to stand up.',
  br_welcome_step2_touch: 'Walk up to a station and tap it. Pull the stick back to stand up.',
  br_welcome_step3_lead: 'Spend your sparkles',
  br_welcome_step3: 'Every play costs Sparkle Points, every win pays them back. The counter trades them for prizes.',
  br_welcome_go: 'Let me in',
});

/** Keys that close the card. Everything else is swallowed while it is up. */
export const CLOSE_KEYS = Object.freeze(['Escape', 'Enter', ' ']);

/** Where the hero lives, relative to index.html like every other room asset. */
export const HERO_SRC = 'room/assets/welcome-hero.webp';

const coarse = () => { try { return !!matchMedia('(pointer: coarse)').matches; } catch { return false; } };

/**
 * Which wording each step gets: the desk line names keys, the phone line names the stick.
 * @param {boolean} touch
 * @returns {string[]} lexicon keys, one per step, in order
 */
export function stepKeys(touch) {
  return STEPS.map(s => (touch && s.touch) ? s.touch : s.body);
}

/**
 * Should the card show on this open? Only when the host has never recorded a dismissal.
 * A missing field (an older host) reads as not seen: better one card too many than a room
 * with no explanation, and the dismissal is written the first time it shows.
 */
export function shouldShow(init) {
  return !(init && init.welcomeSeen === true);
}

/**
 * Mount the card.
 * @param {Object} o { layer, lex(key, fallback), onDone(), touch?, hero?, doc?, win? }
 * @returns {{ root, open(): boolean, dismiss(): boolean }}
 */
export function createWelcome(o) {
  const doc = o.doc || globalThis.document;
  const win = o.win || globalThis.window;
  const L = (k) => (typeof o.lex === 'function' ? o.lex(k, FALLBACK[k]) : FALLBACK[k]) || FALLBACK[k];
  const touch = typeof o.touch === 'boolean' ? o.touch : coarse();
  const el = (tag, cls, text) => {
    const n = doc.createElement(tag);
    if (cls) n.className = cls;
    if (text != null) n.textContent = text;
    return n;
  };

  const root = el('div', 'br-card-veil br-welcome-veil');
  const card = el('section', 'br-card br-welcome');
  card.setAttribute('role', 'dialog'); card.setAttribute('aria-modal', 'true');
  const hero = el('img', 'br-welcome-hero');
  hero.setAttribute('alt', ''); hero.setAttribute('decoding', 'async');
  hero.src = o.hero || HERO_SRC;
  const title = el('h2', 'br-card-title br-welcome-title', L('br_welcome_title'));
  card.setAttribute('aria-label', L('br_welcome_title'));
  const sub = el('p', 'br-welcome-sub', L('br_welcome_sub'));
  const list = el('ol', 'br-welcome-steps');
  const keys = stepKeys(touch);
  STEPS.forEach((s, i) => {
    const li = el('li', 'br-welcome-step');
    const num = el('b', 'br-welcome-num', String(i + 1));
    const text = el('div', 'br-welcome-text');
    text.append(el('strong', 'br-welcome-lead', L(s.lead)), el('span', 'br-welcome-body', L(keys[i])));
    li.append(num, text);
    list.append(li);
  });
  const go = el('button', 'br-welcome-go', L('br_welcome_go'));
  go.setAttribute('type', 'button');
  card.append(hero, title, sub, list, go);
  root.append(card);

  let opened = true;
  function dismiss() {
    if (!opened) return false;
    opened = false;
    win.removeEventListener('keydown', onKey, true);
    try { root.remove(); } catch (e) { /* a stub layer with no remove */ }
    try { o.onDone && o.onDone(); } catch (e) { /* the host write is the caller's */ }
    return true;
  }
  function onKey(e) {
    if (!opened) return;
    // Every key stops here while the card is up: the room behind the veil must not walk or seat.
    e.stopPropagation();
    if (CLOSE_KEYS.includes(e.key)) { e.preventDefault(); dismiss(); }
  }
  go.addEventListener('click', dismiss);
  root.addEventListener('pointerdown', (e) => { if (e.target === root) dismiss(); });
  win.addEventListener('keydown', onKey, true);
  o.layer.append(root);
  try { requestAnimationFrame(() => { try { go.focus(); } catch (e) { /* noop */ } }); } catch (e) { /* no rAF in node */ }
  return { root, open: () => opened, dismiss };
}
