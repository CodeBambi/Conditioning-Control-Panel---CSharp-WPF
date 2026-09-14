// Bottom-centre toasts. toast('Saved remix-CCP-4K7R.gif'); toast('Removed gif 3', { action:'Undo', onAction }).
const host = () => document.getElementById('toast-host');
let live = [];

export function toast(text, { action, onAction, gold = false, ms = 2600 } = {}) {
  const el = document.createElement('div');
  el.className = 'toast' + (gold ? ' gold' : '');
  el.setAttribute('role', 'status');
  const span = document.createElement('span'); span.textContent = text; el.appendChild(span);
  if (action) {
    const b = document.createElement('button'); b.type = 'button'; b.textContent = action;
    b.addEventListener('click', () => { onAction?.(); dismiss(el); });
    el.appendChild(b);
  }
  // one toast at a time reads cleaner; older ones leave early
  live.forEach(dismiss); live = [el];
  host().appendChild(el);
  const t = setTimeout(() => dismiss(el), action ? ms + 1800 : ms);
  el._t = t;
  return el;
}

function dismiss(el) {
  if (!el.isConnected || el.classList.contains('out')) return;
  clearTimeout(el._t);
  el.classList.add('out');
  el.addEventListener('animationend', () => el.remove(), { once: true });
  setTimeout(() => el.remove(), 400);
  live = live.filter(x => x !== el);
}
