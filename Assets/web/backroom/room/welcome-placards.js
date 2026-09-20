/* ============================================================================
 * backroom/room/welcome-placards.js - the first-visit card, hung on the counter.
 *
 * One plane per placard page of welcome.js's card (the room and the Parlour;
 * the pictures-and-sparkles page between them is reached with Next), in the
 * two framed panels either side of SPARKLES / PRIZES on the Prize Parlour's
 * apron (the records and the paint are welcome-wall.js). A tap on a placard
 * reopens the card at that page in read mode: scene.js finds
 * userData.welcomePage on the hit and calls onCard(page), so the pages a
 * player skipped past on their first visit stay on the wall like any other
 * piece of paper in the room.
 *
 * Static paper: one plane and one 1024 x 310 canvas per placard, painted once
 * at boot and once more when its picture lands. No render loop.
 * ==========================================================================*/
import * as T from 'three';
import { FALLBACK } from './welcome.js';
import { PLACARDS, PAPER, paintPlacard } from './welcome-wall.js';
export { PLACARDS, PANEL, PAPER, paintPlacard } from './welcome-wall.js';

/**
 * Hang the placards.
 * @param {{ scene: T.Scene, lex?: (key, fallback) => string }} o
 */
export function createWelcomePlacards({ scene, lex = null }) {
  const say = (key, fallback) => (lex ? lex(key, fallback) : fallback) || fallback;
  const root = new T.Group(); root.name = 'welcome_placards'; scene.add(root);
  const geometry = new T.PlaneGeometry(1, 1), targets = [], textures = [], materials = [];
  let disposed = false;
  for (const item of PLACARDS) {
    const canvas = document.createElement('canvas'); canvas.width = PAPER.w; canvas.height = PAPER.h;
    const ctx = canvas.getContext('2d');
    const map = new T.CanvasTexture(canvas); map.colorSpace = T.SRGBColorSpace; textures.push(map);
    const words = { title: say(item.titleKey, FALLBACK[item.titleKey]), sub: say(item.subKey, FALLBACK[item.subKey]), read: say(item.readKey, FALLBACK[item.readKey]) };
    const draw = image => { if (disposed) return; paintPlacard(ctx, { ...words, image }); map.needsUpdate = true; };
    draw(null);
    const img = new Image(); img.onload = () => draw(img); img.src = item.src;
    const material = new T.MeshBasicMaterial({ map, toneMapped: false }); materials.push(material);
    const mesh = new T.Mesh(geometry, material);
    mesh.scale.set(item.size[0], item.size[1], 1); mesh.position.fromArray(item.position); mesh.rotation.y = item.yaw;
    mesh.name = 'welcome_' + item.id; mesh.userData.welcomePage = item.page;
    root.add(mesh); targets.push(mesh);
  }
  return { root, targets, dispose() { disposed = true; root.removeFromParent(); geometry.dispose(); textures.forEach(t => t.dispose()); materials.forEach(m => m.dispose()); } };
}
