import { catalogueStyle } from './customization-panel-style.js';

/** The room is the live preview. No ownership or purchase is implied by selection. */
export function createCustomizationPanel({ mount, lex = (_, fallback) => fallback, select, getState, restore, preview = () => {}, onClose = () => {} }) {
  const doc = mount.ownerDocument, L = (key, fallback) => lex('br_custom_' + key, fallback) || fallback;
  const pieces = [['knight', 'Knight sculpture'], ['queen', 'Queen sculpture'], ['rook', 'Rook sculpture']];
  const groups = {
    displays: [['gallery', 'Gallery frame'], ['portraits', 'Portrait pair'], ['billboard', 'Wide billboard']],
    plants: [['monstera', 'Monstera'], ['ivy', 'Hanging ivy'], ['terrarium', 'Terrarium']],
    statues: [['none', 'Empty pedestal'], ...pieces], handles: [['original', 'Original handle'], ...pieces],
    floor: [['vortex', 'Velvet Vortex'], ['ribbon', 'Ribbon Galaxy'], ['bloom', 'Prism Bloom']],
    palette: [['jewel', 'Jewel'], ['lagoon', 'Lagoon'], ['sunset', 'Sunset']],
  };
  const style = doc.createElement('style'); style.textContent = catalogueStyle;
  const panel = doc.createElement('section'); panel.className = 'br-custom-panel'; panel.hidden = true; panel.tabIndex = -1;
  panel.setAttribute('role', 'dialog'); panel.setAttribute('aria-modal', 'true'); panel.setAttribute('aria-label', L('title', 'Room Service'));
  const make = (tag, text, cls, parent = panel) => { const n = doc.createElement(tag); n.textContent = text; if (cls) n.className = cls; parent.append(n); return n; };
  const button = (parent, text, action) => { const n = make('button', text, '', parent); n.type = 'button'; n.addEventListener('click', action); return n; };
  const header = make('header', '', 'br-custom-header');
  const closeButton = button(header, L('close', 'Close'), () => close()); closeButton.className = 'br-custom-close';
  make('span', '01 / 03', 'br-custom-edition', header);
  make('h2', L('title', 'Room Service'), '', header);
  make('p', L('live', 'Your room is the preview.'), '', header);
  const tabs = make('div', '', 'br-custom-tabs'); tabs.setAttribute('role', 'group'); tabs.setAttribute('aria-label', L('categories', 'Categories'));
  const body = make('div', '', 'br-custom-body');
  const sections = make('div', '', 'br-custom-sections', body);
  const targets = make('div', '', 'br-custom-targets', body);
  const choices = make('div', '', 'br-custom-choices', body);
  const palettes = make('div', '', 'br-custom-palettes', body);
  const footer = make('footer', '', 'br-custom-footer');
  const itemName = make('strong', '', 'br-custom-name', footer); itemName.setAttribute('aria-live', 'polite');
  const unavailable = button(footer, L('buy_unavailable', 'Purchases unavailable'), () => {}); unavailable.disabled = true;
  const reset = button(footer, L('restore', 'Restore preview'), () => { restore?.(structuredClone(snapshot)); paint(); showPreview(); });
  reset.className = 'br-custom-restore';
  make('p', L('unavailable', 'Local preview only. Purchases are not available yet.'), 'br-custom-note', footer);
  let tab = 'decor', group = 'displays', target = 0, snapshot, previousFocus, disposed = false;
  const state = () => getState?.() || { displays: 0, plants: 0, statues: [0, 1, 2], handles: [-1, -1, -1], floor: 0, palette: 0 };
  const current = () => { const value = state()[group]; return Array.isArray(value) ? value[target] : value; };
  const showPreview = () => preview(group, current(), target);
  function row(parent, entries, selected, action) {
    parent.replaceChildren();
    entries.forEach(([key, fallback], index) => {
      const n = button(parent, L(key, fallback), () => { action(index); parent.children[index]?.focus({ preventScroll: true }); }); n.setAttribute('aria-pressed', String(selected === index));
    });
  }
  function paintChoices() {
    const offset = group === 'statues' || group === 'handles' ? -1 : 0;
    row(choices, groups[group], current() - offset, index => { const pending=select(group, index + offset, target); paintChoices(); showPreview(); if(pending?.then)pending.then(()=>{if(!disposed)paintChoices();}); });
    [...choices.children].forEach((n, i) => {
      const number = doc.createElement('span'); number.className = 'br-custom-index'; number.textContent = String(i + 1).padStart(2, '0'); n.prepend(number);
    });
    const entry = groups[group][current() - offset]; itemName.textContent = entry ? L(...entry) : '';
    palettes.hidden = tab !== 'floor';
    if (tab === 'floor') {
      row(palettes, groups.palette, state().palette, index => { select('palette', index); paintChoices(); showPreview(); });
      [...palettes.children].forEach((n, i) => { n.className = 'br-custom-swatch swatch-' + i; });
    }
  }
  function paint() {
    [...tabs.children].forEach((n, i) => n.setAttribute('aria-pressed', String(['decor', 'handles', 'floor'][i] === tab)));
    header.querySelector('.br-custom-edition').textContent = '0' + (['decor', 'handles', 'floor'].indexOf(tab) + 1) + ' / 03';
    sections.hidden = tab !== 'decor';
    if (tab === 'decor') row(sections, [['displays', 'Displays'], ['plants', 'Plants'], ['statues', 'Statues']], ['displays', 'plants', 'statues'].indexOf(group), i => { group = ['displays', 'plants', 'statues'][i]; target = 0; paint(); showPreview(); });
    targets.hidden = group !== 'statues' && group !== 'handles';
    if (!targets.hidden) row(targets, group === 'handles' ? [['rose', 'Candy Rose'], ['violet', 'Candy Violet'], ['mint', 'Candy Mint']] : [['spot1', 'Pedestal 1'], ['spot2', 'Pedestal 2'], ['spot3', 'Pedestal 3']], target, i => { target = i; paint(); showPreview(); });
    paintChoices();
  }
  [['decor', 'Decor'], ['handles', 'Slot handles'], ['floor', 'Floor']].forEach(([key, fallback]) => button(tabs, L(key, fallback), () => { tab = key; group = key === 'decor' ? 'displays' : key; target = 0; paint(); showPreview(); }));
  function close() {
    if (panel.hidden) return; panel.hidden = true; onClose();
    if (previousFocus?.isConnected) previousFocus.focus({ preventScroll: true }); previousFocus = null;
  }
  panel.addEventListener('keydown', event => {
    event.stopPropagation();
    if (event.key === 'Escape') { event.preventDefault(); close(); return; }
    if (event.key !== 'Tab') return;
    const nodes = [...panel.querySelectorAll('button:not([disabled])')].filter(n => !n.closest('[hidden]'));
    const first = nodes[0], last = nodes.at(-1);
    if (event.shiftKey && (doc.activeElement === first || doc.activeElement === panel)) { event.preventDefault(); last.focus(); }
    else if (!event.shiftKey && doc.activeElement === last) { event.preventDefault(); first.focus(); }
  });
  for (const type of ['keyup', 'pointerdown', 'pointerup', 'click', 'wheel']) panel.addEventListener(type, event => event.stopPropagation());
  mount.append(style, panel); paint();
  return {
    open() { if (disposed || !panel.hidden) return; snapshot = structuredClone(state()); previousFocus = doc.activeElement; panel.hidden = false; paint(); showPreview(); closeButton.focus({ preventScroll: true }); },
    close, get opened() { return !panel.hidden && !disposed; },
    dispose() { if (disposed) return; close(); disposed = true; panel.remove(); style.remove(); },
  };
}
