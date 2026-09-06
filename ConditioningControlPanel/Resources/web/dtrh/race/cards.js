/* ============================================================================
 * race/cards.js - the four introduction cards of Racing Thoughts, the first
 * thing a new player reads. Not the RUN intro (race/intro.js): that one is the
 * podium, the cup and the gantry countdown, and it plays every race.
 *
 *   createCards({ root, audio, reducedMotion, log, start }) ->
 *     { show(): Promise<void>, dispose() }
 *   cardsSeen()      -> true once the cards have been through (finished OR escaped)
 *   markCardsSeen()  -> writes the gate
 *
 * A DOM layer (.rc-*, menu.css) at z26: above the menu's .rm-root (z25) and
 * below the boot splash (z30). It is laid out like the menu, a left column over
 * the same gradient and a hole on the right, so EMI keeps standing on her podium
 * while the cards read. The boot hides the menu around them and calls
 * menu.refreshView() so she does not slide back to the middle of the frame.
 *
 * The four cards, one at a time:
 *   1  the wordmark, racing thoughts, and the one line under it
 *   2  the road      what the road is made of and how you drive it
 *   3  the track     what a loaded file does to the road
 *   4  (^_^)         who is in the cup, and who is not steering
 *
 * Enter / space / right / d / a pointer press / pad A goes forward, left / a /
 * pad B goes back (nothing on card 1), esc ends it there. show() resolves when
 * the cards end, either way. THE BOUNCE on every advance and a spring on each
 * card as it lands (Law VIII, Law XI); reduced motion gets neither, through the
 * same [data-motion] hook the menu uses.
 *
 * GATE: localStorage `race.cards` = '1', written the moment they go up, so the
 * front door never repeats itself: read through, escaped or walked away from all
 * count the same. Its own key, so the options store `race.options` keeps the
 * shape menu.js documents. Every read and write is wrapped: the WebView can
 * refuse storage and the cards must still run.
 * The boot's `?cards=1` forces them, `?cards=0` skips them, `?card=N` opens on
 * card N (a screenshot aid, the way `?hold=` is one for the intro).
 * ==========================================================================*/

export const CARDS_KEY = 'race.cards';

/** The four cards, in order. `title` is the wordmark card, `face` puts the heading in the pink. */
export const CARDS = [
  {
    title: true,
    head: 'racing thoughts',
    line: 'a teacup, a road, and a girl who never stops smiling.',
  },
  {
    head: 'the road',
    line: 'the road is built from your own pictures. steer with the arrows, drift with shift, pop what you can reach. nothing here can be lost. it can only be missed.',
  },
  {
    head: 'the track',
    line: 'load a track and the road learns to listen. every drop, every count, every trigger on the file lands on the asphalt as a bubble, on the word. be under it when it does.',
  },
  {
    face: true,
    head: '(^_^)',
    line: 'she rides in the cup. she does not steer. that part is yours.',
  },
];

const PAD = { a: 0, b: 1, left: 14, right: 15 };
const KEYMAP = { Enter: 'next', Space: 'next', ArrowRight: 'next', KeyD: 'next', ArrowLeft: 'back', KeyA: 'back', Escape: 'end' };
const clamp = (v, a, b) => Math.max(a, Math.min(b, v));

/** Have the cards already been through once? A storage that refuses to answer reads as "not yet". */
export function cardsSeen() {
  try { return localStorage.getItem(CARDS_KEY) === '1'; } catch (e) { return false; }
}
/** Mark them seen. Read through, escaped or abandoned all count: nobody meets the front door twice. */
export function markCardsSeen() {
  try { localStorage.setItem(CARDS_KEY, '1'); } catch (e) { /* private mode, they get them again */ }
}

export function createCards({ root, audio = null, reducedMotion = false, log = null, start = 0 } = {}) {
  const say = (m) => { try { if (log) log('cards: ' + m); } catch (e) { /* no log */ } };
  const el = (tag, cls, parent, text) => { const d = document.createElement(tag); d.className = cls; if (text != null) d.textContent = text; parent.appendChild(d); return d; };
  const hit = (node, cls) => { node.classList.remove(cls); void node.offsetWidth; node.classList.add(cls); };

  const layer = el('div', 'rc-root', root);
  layer.hidden = true;
  layer.setAttribute('role', 'dialog');
  layer.setAttribute('aria-label', 'the story');
  layer.dataset.motion = reducedMotion ? 'on' : 'off';   // same hook menu.css reads: 'on' means hold nobody
  const col = el('div', 'rc-col', layer);
  const deck = el('div', 'rc-deck', col);
  deck.setAttribute('aria-live', 'polite');

  const cardEls = CARDS.map((c) => {
    const card = el('div', 'rc-card', deck);
    card.hidden = true;
    if (c.title) el('div', 'rc-word', card, c.head);
    else el('h2', `rc-h${c.face ? ' rc-face' : ''}`, card, c.head);
    el('p', 'rc-line', card, c.line);
    return card;
  });

  const rail = el('div', 'rc-dots', col);
  rail.setAttribute('aria-hidden', 'true');
  const dotEls = CARDS.map((c, n) => {
    if (n) el('span', 'rc-sep', rail, '·');
    return el('span', 'rc-dot', rail, String(n + 1));
  });

  const foot = el('div', 'rc-foot', col);
  const footNext = el('span', 'rc-foot-k', foot, 'enter · next');
  const footSkip = el('span', 'rc-foot-k', foot, 'esc · skip');

  // ---- state ----
  let i = clamp(Math.round(Number(start) || 0), 0, CARDS.length - 1);
  let live = false, disposed = false, raf = 0, resolve = null;
  const padWas = {};

  function click() {
    if (!audio) return;
    try { audio.sfx('ui_click', 0.5); } catch (e) { /* muted or gone */ }
  }

  function draw(animate) {
    cardEls.forEach((c, n) => { c.hidden = n !== i; });
    dotEls.forEach((d, n) => d.classList.toggle('is-on', n === i));
    const last = i === CARDS.length - 1;
    footNext.textContent = last ? 'enter · begin' : 'enter · next';
    footSkip.hidden = last;
    if (animate) hit(cardEls[i], 'is-in');
  }

  function go(dir) {
    if (!live) return;
    const n = i + dir;
    if (n < 0) return;                       // card 1 has nothing behind it
    hit(cardEls[i], 'is-hit');
    click();
    if (n >= CARDS.length) { end('read'); return; }
    i = n;
    draw(true);
  }

  function end(why) {
    if (!live) return;
    live = false;
    if (raf) { cancelAnimationFrame(raf); raf = 0; }
    window.removeEventListener('keydown', onKey);
    layer.removeEventListener('pointerdown', onPointer);
    layer.hidden = true;
    say('done (' + why + ')');
    const r = resolve; resolve = null;
    if (r) r();
  }

  function onKey(e) {
    if (!live || e.repeat || e.altKey || e.ctrlKey || e.metaKey) return;
    const what = KEYMAP[e.code];
    if (!what) return;
    e.preventDefault();
    if (what === 'end') { click(); end('skipped'); }
    else go(what === 'next' ? 1 : -1);
  }
  function onPointer(e) { if (live && e.button === 0) go(1); }
  function pollPad() {
    raf = 0;
    if (!live) return;
    const pads = navigator.getGamepads ? navigator.getGamepads() : null;
    let g = null;
    if (pads) for (const p of pads) if (p && p.connected) { g = p; break; }
    if (g) {
      const btn = (n) => !!(g.buttons[n] && g.buttons[n].pressed);
      const ax = g.axes || [];
      const now = { next: btn(PAD.a) || btn(PAD.right) || ax[0] > 0.6, back: btn(PAD.b) || btn(PAD.left) || ax[0] < -0.6 };
      for (const k of Object.keys(now)) {
        if (now[k] && !padWas[k]) go(k === 'next' ? 1 : -1);
        padWas[k] = now[k];
      }
    }
    if (live) raf = requestAnimationFrame(pollPad);
  }

  return {
    /** Put the cards up and hand back a promise that settles when they end, read through or escaped. */
    show() {
      if (disposed) return Promise.resolve();
      if (live) return Promise.resolve();
      live = true;
      markCardsSeen();   // the moment they go up: a window closed on card 2 is not a first open any more
      layer.hidden = false;
      draw(true);
      window.addEventListener('keydown', onKey);
      layer.addEventListener('pointerdown', onPointer);
      raf = requestAnimationFrame(pollPad);
      say('open on card ' + (i + 1));
      return new Promise((res) => { resolve = res; });
    },
    get index() { return i; },
    dispose() {
      if (disposed) return;
      disposed = true;
      end('disposed');
      layer.remove();
    },
  };
}

// self-check: node --check is the bar; cardsSeen / markCardsSeen are pure given a localStorage stub.
