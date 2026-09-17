import * as T from 'three';

const asset = name => new URL('./assets/memorabilia/' + name + '.jpg', import.meta.url).href;
// Archival artwork, not current product promises. Keep captions brief enough to read on paper.
// The owner will choose these eight pictures later. Each numbered slot stays independently replaceable.
export const PHOTO_GROUPS = [
  { id: 'entrance', position: [-1.75, 1.9, 7.76], yaw: Math.PI, photos: ['ccp-601'] },
  { id: 'southwest', position: [-3.95, 1.95, 7.76], yaw: Math.PI, photos: ['ccp-dashboard', 'ccp-feature'] },
  { id: 'cards', position: [6.82, 3.35, -5.4], yaw: -Math.PI / 2, photos: ['ccp-dashboard'] },
  { id: 'parlour', position: [5.65, 1.7, -7.76], yaw: 0, photos: ['ccp-feature', 'ccp-601'] },
  { id: 'slots', position: [-6.82, 3.5, 4.9], yaw: Math.PI / 2, photos: ['ccp-601', 'ccp-dashboard'] },
];
const photos = PHOTO_GROUPS.flatMap((group, g) => group.photos.map((file, i) => {
  const offset = (i - (group.photos.length - 1) / 2) * .7;
  return { id: 'photo-' + g + '-' + i, group: group.id, kind: 'polaroid', title: '', caption: '',
    src: asset(file), position: [group.position[0] + Math.cos(group.yaw) * offset,
      group.position[1] + (i ? -.07 : .04), group.position[2] - Math.sin(group.yaw) * offset],
    yaw: group.yaw, tilt: i ? .06 : -.065, size: [.61, .43] };
}));
export const MEMORABILIA = [
  { id: 'eyes', kind: 'poster', title: 'Eyes front', src: asset('eyes-front'), position: [-6.82, 2.25, -3.8], yaw: Math.PI / 2, size: [.85, 1.25] },
  { id: 'work', kind: 'poster', title: 'Good work', src: asset('good-work'), position: [-6.82, 2.25, -5.8], yaw: Math.PI / 2, size: [.9, 1.3] },
  { id: 'late', kind: 'poster', title: 'Stay late', src: asset('stay-late'), position: [5.7, 1.95, 7.76], yaw: Math.PI, size: [.85, 1.25] },
  { id: 'attend', kind: 'poster', title: 'Attendance', src: asset('attend'), position: [-4.9, 2.9, 7.76], yaw: Math.PI, size: [.85, 1.25] },
  { id: 'deep', kind: 'poster', title: 'Dive deep', src: asset('dive-deep'), position: [4.25, 2.6, 7.76], yaw: Math.PI, size: [.9, 1.3] },
  { id: 'listen', kind: 'poster', title: 'Listen well', src: asset('listen-well'), position: [2.85, 2.5, 7.76], yaw: Math.PI, size: [.85, 1.25] },
  ...photos,
  { id: 'lost', kind: 'note', title: 'LOST & FOUND', caption: 'One train of thought.\nLast seen at the Arcademy.', position: [-6.8, 1.75, -.25], yaw: Math.PI / 2, tilt: .06, size: [.52, .4] },
  { id: 'meeting', kind: 'note', title: 'STAFF NOTICE', caption: 'Focus Gaze staring contest.\nBlink and you missed it.', position: [6.8, 2.4, -6.45], yaw: -Math.PI / 2, tilt: -.05, size: [.52, .4] },
  { id: 'plaque', kind: 'plaque', title: 'THE BACK ROOM', caption: 'A small feature.\nSeveral features ago.', position: [1.65, 1.65, 7.76], yaw: Math.PI, size: [.84, .48] },
];
/** Static paper costs one plane per item. No new render loop or WebGL context. */
export function createMemorabilia({ scene }) {
  const root = new T.Group(); root.name = 'room_memorabilia'; scene.add(root);
  const targets = [], textures = [], materials = [], geometry = new T.PlaneGeometry(1, 1);
  let disposed = false;
  const paper = (width, height) => { const c = document.createElement('canvas'); c.width = width; c.height = height; return c; };
  function plane(parent, map, w, h, x = 0, y = 0, z = 0) {
    const material = new T.MeshBasicMaterial({ map, toneMapped: false }); materials.push(material);
    const mesh = new T.Mesh(geometry, material); mesh.scale.set(w, h, 1); mesh.position.set(x, y, z); parent.add(mesh); return mesh;
  }
  function texture(canvas) { const t = new T.CanvasTexture(canvas); t.colorSpace = T.SRGBColorSpace; textures.push(t); return t; }
  for (const item of MEMORABILIA) {
    const canvas = paper(768, Math.round(768 * item.size[1] / item.size[0])), ctx = canvas.getContext('2d');
    const map = texture(canvas), w = canvas.width; let h = canvas.height;
    const draw = image => {
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
        // Fit the paper to the photograph, with one short caption strip below.
        h = Math.round((w - 36) * image.height / image.width + 72);
        canvas.height = h;
        mesh.scale.y = item.size[0] * h / w;
      }
      const plaque = item.kind === 'plaque', note = item.kind === 'note';
      ctx.fillStyle = plaque ? '#b99055' : note ? '#f3dfa0' : '#fff6e6'; ctx.fillRect(0, 0, w, h);
      ctx.strokeStyle = plaque ? '#533727' : '#d3c6ae'; ctx.lineWidth = plaque ? 12 : 3; ctx.strokeRect(12, 12, w - 24, h - 24);
      if (image) {
        const pad = item.kind === 'poster' ? 15 : 18, foot = item.kind === 'poster' ? 15 : 54;
        const scale = Math.min((w - pad * 2) / image.width, (h - pad - foot) / image.height);
        ctx.drawImage(image, (w - image.width * scale) / 2, pad + (h - pad - foot - image.height * scale) / 2, image.width * scale, image.height * scale);
      }
      ctx.fillStyle = '#39283a'; ctx.textAlign = 'center';
      if (item.kind !== 'poster' || !image) {
        ctx.font = '600 36px Georgia'; ctx.fillText(item.title, w / 2, image ? h - 47 : h * .35, w - 55);
        if (!image) { ctx.font = '30px Georgia'; (item.caption || '').split('\n').forEach((line, i) => ctx.fillText(line, w / 2, h * .55 + i * 40, w - 55)); }
      }
      if (item.kind === 'polaroid' || item.kind === 'note') { ctx.fillStyle = '#b83a68'; ctx.beginPath(); ctx.arc(w / 2, 16, 10, 0, Math.PI * 2); ctx.fill(); }
      map.needsUpdate = true;
    };
    draw(null);
    if (item.src) { const img = new Image(); img.onload = () => draw(img); img.src = item.src; }
    const mesh = plane(root, map, ...item.size);
    mesh.position.fromArray(item.position); mesh.rotation.set(0, item.yaw, item.tilt || 0, 'YXZ');
    mesh.name = 'memorabilia_' + item.id; mesh.userData.document = item; targets.push(mesh);
  }
  return { root, targets, dispose() { disposed = true; root.removeFromParent(); geometry.dispose(); textures.forEach(t => t.dispose()); materials.forEach(m => m.dispose()); } };
}
