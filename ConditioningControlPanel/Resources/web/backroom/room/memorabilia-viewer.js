// Room keeps ownership of navigation; this viewer only owns the open document.
export function createMemorabiliaViewer({ onOpen = () => {}, onClose = () => {}, backLabel = 'Back' } = {}) {
  const style = document.createElement('link');
  style.rel = 'stylesheet';
  style.href = new URL('./memorabilia-viewer.css', import.meta.url).href;
  document.head.append(style);
  const dialog = document.createElement('dialog');
  dialog.className = 'memorabilia-viewer';
  dialog.setAttribute('aria-label', '');
  const back = document.createElement('button');
  back.type = 'button';
  back.className = 'memorabilia-back';
  back.textContent = backLabel;
  const paper = document.createElement('figure');
  paper.className = 'memorabilia-paper';
  const picture = document.createElement('img');
  picture.draggable = false;
  const title = document.createElement('strong');
  const caption = document.createElement('figcaption');
  paper.append(picture, title, caption);
  dialog.append(back, paper);
  document.body.append(dialog);
  let previousFocus = null;
  let disposed = false;
  function close() {
    if (!dialog.open) return false;
    dialog.close();
    picture.removeAttribute('src');
    onClose();
    if (previousFocus?.isConnected) previousFocus.focus({ preventScroll: true });
    previousFocus = null;
    return true;
  }
  function keys(event) {
    if (!dialog.open) return;
    // Keep movement and station shortcuts out of the room while reading.
    event.stopImmediatePropagation();
    if (event.key === 'Escape' || event.key === 'BrowserBack') {
      event.preventDefault();
      close();
    } else if (event.key === 'Tab') {
      event.preventDefault();
      back.focus();
    }
  }
  const stop = event => event.stopPropagation();
  dialog.addEventListener('pointerdown', stop);
  dialog.addEventListener('pointerup', stop);
  dialog.addEventListener('click', event => {
    event.stopPropagation();
    if (event.target === dialog || event.target === back) close();
  });
  dialog.addEventListener('cancel', event => { event.preventDefault(); close(); });
  window.addEventListener('keydown', keys, true);
  window.addEventListener('keyup', keys, true);
  return {
    open(item) {
      if (disposed || !item) return;
      const wasOpen = dialog.open;
      if (!wasOpen) previousFocus = document.activeElement;
      paper.dataset.kind = ['polaroid', 'poster', 'plaque', 'note'].includes(item.kind) ? item.kind : 'poster';
      dialog.setAttribute('aria-label', item.title || backLabel);
      title.textContent = item.title || '';
      title.hidden = !item.title;
      caption.textContent = item.caption || '';
      caption.hidden = !item.caption;
      picture.hidden = !item.src;
      picture.alt = item.title || '';
      if (item.src) picture.src = item.src;
      else picture.removeAttribute('src');
      if (!wasOpen) { dialog.showModal(); onOpen(); }
      back.focus({ preventScroll: true });
    },
    close,
    get opened() { return dialog.open; },
    dispose() {
      if (disposed) return;
      close();
      disposed = true;
      window.removeEventListener('keydown', keys, true);
      window.removeEventListener('keyup', keys, true);
      dialog.remove();
      style.remove();
    }
  };
}
