/* ============================================================================
 * ui/screens/title.js — the storefront.
 *
 * Logo, one kicker, a five-item menu, and the fineprint that tells the player
 * the truth about the machine boundary before anything else does. Nothing on
 * this screen touches the network: Host and Join only ROUTE, and the actual
 * signaling call happens on the screen that can render its failure.
 *
 * "Practice" is always present and becomes the PRIMARY action when the page is
 * standalone, because a dev/play-tester with no server should never have to
 * guess which button works.
 * ==========================================================================*/

import { createLedger, el, button } from '../router.js';
import { S } from '../strings.js';

export function mount(container, ctx) {
  const ledger = createLedger();
  ledger.logger = ctx?.logger || null;

  const { session, actions, audio, prefs, sheets } = ctx;
  const standalone = !session.hosted;

  const card = el('div', { class: 'gg-card gg-title' });

  /* --- brand ---------------------------------------------------------- */
  const logo = el('img', {
    class: 'gg-title-logo',
    src: './assets/goon_game_logo.png',
    alt: 'Goon Game',
    decoding: 'async',
  });
  // A missing/blocked image must never leave a blank title screen.
  ledger.listen(logo, 'error', () => {
    try { logo.remove(); } catch (_e) { /* ignore */ }
    card.prepend(el('h1', { class: 'gg-grad gg-title-wordmark', text: 'Goon Game' }));
  });
  card.appendChild(logo);
  card.appendChild(el('p', { class: 'gg-title-kicker', text: S.title.kicker }));

  /* --- menu ----------------------------------------------------------- */
  const menu = el('nav', { class: 'gg-menu', 'aria-label': 'main menu' });

  const item = (label, onClick, { variant = '', note = '', sfx = 'ui-select' } = {}) => {
    const b = button(ledger, label, onClick, { variant, audio, sfx });
    b.classList.add('gg-menu-item');
    if (note) {
      b.appendChild(el('span', { class: 'gg-menu-note', text: note }));
      b.classList.add('has-note');
    }
    ledger.listen(b, 'pointerenter', () => { try { audio?.sfx?.('ui-move'); } catch (_e) { /* stub */ } });
    menu.appendChild(b);
    return b;
  };

  item(S.title.host, () => actions.goHost(), { variant: standalone ? '' : 'primary' });
  item(S.title.join, () => actions.goJoin());
  item(S.title.practice, () => actions.goPractice(), {
    variant: standalone ? 'primary' : '',
    note: S.title.practiceNote,
  });
  item(S.title.options, () => ctx.options?.open?.());
  item(S.title.how, () => showHowItWorks(), { variant: 'ghost' });
  if (session.hosted) item(S.title.quit, () => actions.quit('title'), { variant: 'ghost', sfx: 'ui-back' });

  card.appendChild(menu);

  /* --- status ribbon --------------------------------------------------
   * v1 renders NOTHING here: premium/pass state is only knowable after a
   * server round-trip, and a ribbon that says "free match available" before we
   * have asked is a lie the player would plan around. The node exists so the
   * pass check can drop straight in.
   * ------------------------------------------------------------------- */
  const ribbon = el('div', { class: 'gg-title-ribbon', hidden: true });
  card.appendChild(ribbon);

  card.appendChild(el('p', { class: 'gg-title-fineprint', text: S.title.fineprint }));

  // NOTE: #gg-boot-ok is owned by boot.js and lives on <body> — it has to stay
  // readable on every screen, not just this one, or the play-test driver loses
  // its probe the moment the player leaves the menu.

  container.appendChild(card);

  function showHowItWorks() {
    prefs?.set?.('seenHowItWorks', true);
    // Built as a sheet body rather than through sheets.open(), because six
    // bullets is a list, not a one-line notice.
    const body = el('div', { class: 'gg-how' }, [
      el('h2', { class: 'gg-sheet-headline', text: S.how.headline }),
      el('ul', { class: 'gg-how-list' }, S.how.bullets.map((b) => el('li', { text: b }))),
    ]);
    sheets?.openNode?.(body, { label: S.how.headline, closeLabel: S.how.close });
  }

  // The first-ever visit opens the explainer once, unprompted. After that it
  // is a menu item like any other.
  if (prefs && !prefs.get('seenHowItWorks')) ledger.timer(showHowItWorks, 420);

  try { audio?.music?.('title'); } catch (_e) { /* stub bus */ }
  ledger.add(() => { try { audio?.stopMusic?.(); } catch (_e) { /* stub bus */ } });

  return { unmount() { ledger.dispose(); } };
}

export default { mount };
