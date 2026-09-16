// Whole-page drop zone + paste. Shows the veil while a file drag hovers the window, hands files up.
export function initDropzone({ onFiles }) {
  const veil = document.getElementById('dropveil');
  let depth = 0;
  const hasFiles = e => Array.from(e.dataTransfer?.types || []).includes('Files');

  window.addEventListener('dragenter', e => {
    if (!hasFiles(e)) return;
    e.preventDefault(); depth++;
    veil.hidden = false;
  });
  window.addEventListener('dragover', e => { if (!hasFiles(e)) return; e.preventDefault(); e.dataTransfer.dropEffect = 'copy'; });
  window.addEventListener('dragleave', e => {
    if (!hasFiles(e)) return;
    depth = Math.max(0, depth - 1);
    if (depth === 0) veil.hidden = true;
  });
  window.addEventListener('drop', e => {
    if (!hasFiles(e)) return;
    e.preventDefault(); depth = 0; veil.hidden = true;
    const files = Array.from(e.dataTransfer.files || []).filter(f => /^(image|video)\//.test(f.type) || /^application\/(x-)?zip(-compressed)?$/.test(f.type) || /\.(gif|webp|png|jpe?g|mp4|webm|mov|zip)$/i.test(f.name));
    if (files.length) onFiles(files);
  });
  // paste a copied image straight in
  window.addEventListener('paste', e => {
    const files = Array.from(e.clipboardData?.files || []).filter(f => /^(image|video)\//.test(f.type));
    if (files.length) { e.preventDefault(); onFiles(files); }
  });
  // if the browser loses the drag (alt-tab), do not leave the veil stuck
  window.addEventListener('blur', () => { depth = 0; veil.hidden = true; });
}
