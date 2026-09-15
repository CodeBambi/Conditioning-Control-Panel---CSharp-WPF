import { createScene } from './scene.js';
/** Same view API, with the room owning the renderer, camera and frame loop. */
export function createRoomScene(o) {
  if (!o.stage) throw new Error('Daily Daze requires a room stage');
  return createScene({ ...o, canvas: o.stage.canvas });
}
