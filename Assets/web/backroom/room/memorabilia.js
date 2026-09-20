import * as T from 'three';
import { PHOTO_GROUPS, wallPhotos } from './memorabilia-wall.js';

const asset = name => new URL('./assets/memorabilia/' + name + '.jpg', import.meta.url).href;
// Archival artwork, not current product promises. Keep captions brief enough to read on paper.
export { PHOTO_GROUPS };
const photos = wallPhotos();
/* A polaroid that advertises a vault card needs a deeper foot than a bare snapshot: the card's
 * name, then two short lines of its own note. The tier sign is stamped on the photograph instead,
 * because a wall read at a glance should say what a feature COSTS in the same look as what it is. */
const CARD_FOOT = 250, SIGN_WIDTH = .26, SIGN_BAND = 98;
export const MEMORABILIA = [
  { id: 'eyes', kind: 'poster', title: 'Eyes front', src: asset('eyes-front'), position: [-6.82, 2.25, -3.8], yaw: Math.PI / 2, size: [.85, 1.25] },
  { id: 'work', kind: 'poster', title: 'Good work', src: asset('good-work'), position: [-6.82, 2.25, -5.8], yaw: Math.PI / 2, size: [.9, 1.3] },
  { id: 'late', kind: 'poster', title: 'Stay late', src: asset('stay-late'), position: [5.7, 1.95, 7.76], yaw: Math.PI, size: [.85, 1.25] },
  { id: 'attend', kind: 'poster', title: 'Attendance', src: asset('attend'), position: [-2.75, 3.05, 7.76], yaw: Math.PI, size: [.85, 1.25] },
  // 'deep' (Dive deep) used to hang at [4.25, 2.6, 7.76]. That is inside the fan-and-pennant relief
  // room/casino-decor.js builds at x 4.6 / z 7.86, so the poster sat on top of the banner. Taken down
  // rather than nudged: the entrance wall already carries five pieces and the relief is the feature.
  // assets/memorabilia/dive-deep.jpg stays on disk for re-hanging on a clear wall.
  { id: 'listen', kind: 'poster', title: 'Listen well', src: asset('listen-well'), position: [2.85, 2.5, 7.76], yaw: Math.PI, size: [.85, 1.25] },
  ...photos,
  { id: 'lost', kind: 'note', title: 'LOST & FOUND', caption: 'One train of thought.\nLast seen at the Arcademy.', position: [-6.8, 1.75, -.25], yaw: Math.PI / 2, tilt: .06, size: [.52, .4] },
  { id: 'meeting', kind: 'note', title: 'STAFF NOTICE', caption: 'Focus Gaze staring contest.\nBlink and you missed it.', position: [6.8, 2.4, -6.45], yaw: -Math.PI / 2, tilt: -.05, size: [.52, .4] },
  { id: 'plaque', kind: 'plaque', title: 'THE BACK ROOM', caption: 'A small feature.\nSeveral features ago.', position: [1.65, 1.65, 7.76], yaw: Math.PI, size: [.84, .48] },
];
/** Static paper costs one plane per item. No new render loop or WebGL context. */
export function createMemorabilia({ scene, lex = null }) {
  const say = (key, fallback) => (key && lex ? lex(key, fallback) : fallback);
  const root = new T.Group(); root.name = 'room_memorabilia'; scene.add(root);
  const targets = [], textures = [], materials = [], geometry = new T.PlaneGeometry(1, 1);
  let disposed = false;
  const paper = (width, height) => { const c = document.createElement('canvas'); c.width = width; c.height = height; return c; };
  function plane(parent, map, w, h, x = 0, y = 0, z = 0) {
    const material = new T.MeshBasicMaterial({ map, toneMapped: false }); materials.push(material);
    const mesh = new T.Mesh(geometry, material); mesh.scale.set(w, h, 1); mesh.position.set(x, y, z); parent.add(mesh); return mesh;
  }
  function texture(canvas) { const t = new T.CanvasTexture(canvas); t.colorSpace = T.SRGBColorSpace; textures.push(t); return t; }
  for (const raw of MEMORABILIA) {
    const item = raw.titleKey || raw.captionKey ? { ...raw, title: say(raw.titleKey, raw.title),
      caption: say(raw.captionKey, raw.caption), chip: say(raw.chipKey, raw.chip) } : raw;
    const canvas = paper(768, Math.round(768 * item.size[1] / item.size[0])), ctx = canvas.getContext('2d');
    const map = texture(canvas), w = canvas.width; let h = canvas.height;
    const advert = item.kind === 'polaroid' && !!item.caption;
    const pad = item.kind === 'poster' ? 15 : 18, foot = item.kind === 'poster' ? 15 : advert ? CARD_FOOT : 54;
    // The sign arrives on its own image, so the paper is repainted once it lands rather than waiting.
    let picture = null, sign = null;
    const draw = image => {
      if (image) picture = image;
      if (disposed) return;
      if (image && item.kind === 'poster') {
        h = Math.round(w * image.height / image.width);
        canvas.height = h;
        mesh.scale.y = item.size[0] * h / w;
        ctx.drawImage(image, 0, 0, w, h);
        map.needsUpdate = true;
        return;
      }
      if (image && item.kind === 'polaroid') {
        // Fit the paper to the photograph. An advert's foot carries its name and note; a bare
        // snapshot keeps the one short caption strip it always had.
        h = Math.round((w - pad * 2) * image.height / image.width + pad + foot);
        canvas.height = h;
        mesh.scale.y = item.size[0] * h / w;
      }
      const plaque = item.kind === 'plaque', note = item.kind === 'note';
      ctx.fillStyle = plaque ? '#b99055' : note ? '#f3dfa0' : '#fff6e6'; ctx.fillRect(0, 0, w, h);
      ctx.strokeStyle = plaque ? '#533727' : '#d3c6ae'; ctx.lineWidth = plaque ? 12 : 3; ctx.strokeRect(12, 12, w - 24, h - 24);
      if (image) {
        const scale = Math.min((w - pad * 2) / image.width, (h - pad - foot) / image.height);
        const box = { w: image.width * scale, h: image.height * scale };
        box.x = (w - box.w) / 2; box.y = pad + (h - pad - foot - box.h) / 2;
        ctx.drawImage(image, box.x, box.y, box.w, box.h);
      }
      ctx.fillStyle = '#39283a'; ctx.textAlign = 'center';
      if (advert && image) {
        // One band for the price, then the name, then the note. The band is reserved whether or
        // not the sign has arrived, so the paper never resizes underneath a loaded sign.
        if (sign) {
          const sw = Math.round(w * SIGN_WIDTH), sh = Math.round(sw * sign.height / sign.width);
          ctx.drawImage(sign, (w - sw) / 2, h - 244 + (SIGN_BAND - sh) / 2, sw, sh);
        }
        // The pass chip stands in for the tier sign on the one card that is not sold by tier.
        if (item.chip) { ctx.font = '600 26px Georgia'; ctx.fillStyle = '#8a6a2a'; ctx.fillText(item.chip, w / 2, h - 244 + SIGN_BAND / 2 + 9, w - 70); }
        ctx.fillStyle = '#39283a'; ctx.font = '600 34px Georgia'; ctx.fillText(item.title, w / 2, h - 112, w - 70);
        ctx.font = '26px Georgia'; ctx.fillStyle = '#5c4a5e';
        item.caption.split('\n').slice(0, 2).forEach((line, i) => ctx.fillText(line, w / 2, h - 72 + i * 36, w - 70));
      } else if (item.kind !== 'poster' || !image) {
        ctx.font = '600 36px Georgia'; ctx.fillText(item.title, w / 2, image ? h - 47 : h * .35, w - 55);
        if (!image) { ctx.font = '30px Georgia'; (item.caption || '').split('\n').forEach((line, i) => ctx.fillText(line, w / 2, h * .55 + i * 40, w - 55)); }
      }
      if (item.kind === 'polaroid' || item.kind === 'note') { ctx.fillStyle = '#b83a68'; ctx.beginPath(); ctx.arc(w / 2, 16, 10, 0, Math.PI * 2); ctx.fill(); }
      map.needsUpdate = true;
    };
    draw(null);
    if (item.src) { const img = new Image(); img.onload = () => draw(img); img.src = item.src; }
    if (item.badge) { const art = new Image(); art.onload = () => { sign = art; draw(picture); }; art.src = item.badge; }
    const mesh = plane(root, map, ...item.size);
    mesh.position.fromArray(item.position); mesh.rotation.set(0, item.yaw, item.tilt || 0, 'YXZ');
    mesh.name = 'memorabilia_' + item.id; mesh.userData.document = item; targets.push(mesh);
  }
  return { root, targets, dispose() { disposed = true; root.removeFromParent(); geometry.dispose(); textures.forEach(t => t.dispose()); materials.forEach(m => m.dispose()); } };
}
