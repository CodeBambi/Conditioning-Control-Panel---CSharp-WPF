// Keyboard: space play/pause, F flips the layout, [ ] nudge the selected block, Delete removes
// block or tile, Ctrl+Z / Ctrl+Shift+Z / Ctrl+Y, Esc closes, arrows scrub one frame (shift: five).
export function initKeys(actions) {
  window.addEventListener('keydown', e => {
    const t = e.target;
    const typing = t && (t.tagName === 'INPUT' && t.type !== 'range' || t.tagName === 'TEXTAREA' || t.isContentEditable);
    const mod = e.ctrlKey || e.metaKey;
    if (mod && !e.shiftKey && e.key.toLowerCase() === 'z') { if (typing) return; e.preventDefault(); actions.undo(); return; }
    if (mod && (e.key.toLowerCase() === 'y' || e.shiftKey && e.key.toLowerCase() === 'z')) { if (typing) return; e.preventDefault(); actions.redo(); return; }
    if (e.key === 'Escape') { actions.escape(); return; }
    if (typing) return;
    switch (e.key) {
      case ' ': e.preventDefault(); actions.togglePlay(); break;
      case 'f': case 'F': e.preventDefault(); actions.flip(); break;
      case '[': e.preventDefault(); actions.nudge(e.shiftKey ? -5 : -1); break;
      case ']': e.preventDefault(); actions.nudge(e.shiftKey ? 5 : 1); break;
      case 'Delete': case 'Backspace': e.preventDefault(); actions.remove(); break;
      case 'ArrowLeft': e.preventDefault(); actions.step(e.shiftKey ? -5 : -1); break;
      case 'ArrowRight': e.preventDefault(); actions.step(e.shiftKey ? 5 : 1); break;
      case 'Home': e.preventDefault(); actions.seek(0); break;
      case 'End': e.preventDefault(); actions.seek(-1); break;
      default: return;
    }
  });
}
