/** Local collection preview only. Purchasing and ownership are handled elsewhere. */
export function createCustomizationPanel({ mount, lex = (_, fallback) => fallback, select, onClose = () => {} }) {
  const doc = mount.ownerDocument;
  const L = (key, fallback) => lex('br_custom_' + key, fallback) || fallback;
  const groups = [
    { id: 'displays', label: 'Displays', items: [['gallery', 'Gallery frame'], ['portraits', 'Portrait pair'], ['billboard', 'Wide billboard']] },
    { id: 'plants', label: 'Plants', items: [['monstera', 'Monstera'], ['ivy', 'Hanging ivy'], ['terrarium', 'Terrarium']] },
    { id: 'statues', label: 'Statues', items: [['knight', 'Knight sculpture'], ['queen', 'Queen sculpture'], ['rook', 'Rook sculpture']] },
  ];
  const style = doc.createElement('style');
  style.textContent = `
.br-custom-panel{position:fixed;right:20px;top:80px;bottom:24px;z-index:30;width:min(350px,calc(100vw - 40px));box-sizing:border-box;overflow:auto;padding:24px;background:linear-gradient(145deg,#281631f7,#130b22fa);color:#f6e1f0;border:1px solid #a67495;border-radius:22px;box-shadow:0 18px 65px #0009;font:15px/1.45 system-ui,sans-serif;pointer-events:auto}
.br-custom-panel[hidden]{display:none}.br-custom-panel h2{margin:0 0 8px;font:600 26px/1.2 Georgia,serif;color:#f4d29c}.br-custom-panel p{margin:0 0 20px;color:#cdb5ca;font-size:13px}.br-custom-panel .br-custom-close{float:right;padding:6px 10px;margin:-7px -8px 8px 10px}
.br-custom-panel button{font:inherit;color:inherit;background:#392344;border:1px solid #79556f;border-radius:12px;padding:11px 12px;cursor:pointer}.br-custom-panel button:hover{background:#513050}.br-custom-panel button:focus-visible{outline:2px solid #8de7d5;outline-offset:3px}.br-custom-panel button[aria-pressed=true]{background:#66395d;border-color:#e6b277;color:#fff0ce}
.br-custom-categories{display:flex;gap:6px;margin:22px 0 18px}.br-custom-categories button{flex:1;padding:9px 5px;font-size:13px}.br-custom-choices{display:grid;gap:10px}.br-custom-choices button{text-align:left;padding:16px}.br-custom-panel .br-custom-note{margin-top:24px;padding-top:16px;border-top:1px solid #674157;color:#baa6be}
@media(max-height:520px){.br-custom-panel{top:60px;bottom:10px;padding:16px}.br-custom-categories{margin:10px 0}.br-custom-panel .br-custom-note{margin-top:12px}}
`;
  const panel = doc.createElement('section');
  panel.className = 'br-custom-panel'; panel.hidden = true; panel.tabIndex = -1;
  panel.setAttribute('role', 'dialog'); panel.setAttribute('aria-modal', 'true');
  panel.setAttribute('aria-label', L('title', 'Room Service'));
  const make = (tag, value, cls) => { const node = doc.createElement(tag); node.textContent = value; if (cls) node.className = cls; panel.appendChild(node); return node; };
  const closeButton = make('button', L('close', 'Close'), 'br-custom-close'); closeButton.type = 'button';
  make('h2', L('title', 'Room Service'));
  make('p', L('preview', 'Collection preview. Try a little change.'));
  const categories = make('div', '', 'br-custom-categories');
  categories.setAttribute('role', 'group'); categories.setAttribute('aria-label', L('categories', 'Categories'));
  const choices = make('div', '', 'br-custom-choices');
  make('p', L('unavailable', 'Local preview only. Purchases are not available yet.'), 'br-custom-note');
  const selected = new Map(groups.map(g=>[g.id,0]));
  let active = 0, previousFocus = null, disposed = false;
  function paint() {
    [...categories.children].forEach((button, i) => button.setAttribute('aria-pressed', String(i === active)));
    choices.replaceChildren();
    const group = groups[active];
    group.items.forEach(([key, fallback], index) => {
      const button = doc.createElement('button'); button.type = 'button'; button.textContent = L(key, fallback);
      button.setAttribute('aria-pressed', String(selected.get(group.id) === index));
      button.addEventListener('click', () => {
        select(group.id, index); selected.set(group.id, index);
        [...choices.children].forEach((item, i) => item.setAttribute('aria-pressed', String(i === index)));
      });
      choices.appendChild(button);
    });
  }
  groups.forEach((group, index) => {
    const button = doc.createElement('button'); button.type = 'button'; button.textContent = L(group.id, group.label);
    button.addEventListener('click', () => { active = index; paint(); }); categories.appendChild(button);
  });
  function close() {
    if (panel.hidden) return;
    panel.hidden = true; onClose();
    if (previousFocus?.isConnected && typeof previousFocus.focus === 'function') previousFocus.focus({ preventScroll: true });
    previousFocus = null;
  }
  closeButton.addEventListener('click', close);
  const keyDown = event => {
    event.stopPropagation();
    if (event.key === 'Escape') { event.preventDefault(); close(); return; }
    if (event.key !== 'Tab') return;
    const buttons = [...panel.querySelectorAll('button:not([disabled])')], first = buttons[0], last = buttons.at(-1);
    if (event.shiftKey && (doc.activeElement === first || doc.activeElement === panel)) { event.preventDefault(); last.focus(); }
    else if (!event.shiftKey && doc.activeElement === last) { event.preventDefault(); first.focus(); }
  };
  panel.addEventListener('keydown', keyDown);
  panel.addEventListener('keyup', event => event.stopPropagation());
  for (const type of ['pointerdown', 'pointerup', 'click', 'wheel']) panel.addEventListener(type, event => event.stopPropagation());
  mount.append(style, panel); paint();
  return {
    open() { if (disposed || !panel.hidden) return; previousFocus = doc.activeElement; panel.hidden = false; closeButton.focus({ preventScroll: true }); },
    close,
    get opened() { return !panel.hidden && !disposed; },
    dispose() { if (disposed) return; close(); disposed = true; panel.remove(); style.remove(); },
  };
}
