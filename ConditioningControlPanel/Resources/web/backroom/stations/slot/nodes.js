/* nodes.js - the slot glb node contract (CONTRACT.md section 6), shared by scene.js and
 * tests/nodes-check.mjs so the page and the owner's re-check read the same list.
 * The glb is a moving snapshot: only names are contract, never positions, sizes or mesh counts. */

/** Without these the page shows a "model missing X" card. */
export const REQUIRED = ['cabinet', 'reel_1', 'reel_2', 'reel_3', 'lever', 'cam_seat', 'cam_target'];

/** These degrade quietly with a console warning. */
export const OPTIONAL = [
  'reel_window', 'freeze_1', 'freeze_2', 'freeze_3', 'screen_jackpot', 'screen_status', 'marquee',
  'marquee_glow', 'payout_tray', 'payout_spawn', 'emi_topper',
  ...Array.from({ length: 30 }, (_, i) => `lights_chase_${String(i).padStart(2, '0')}`),
];

/** Material the EMI face texture binds to (inside emi_topper). */
export const FACE_MATERIAL = 'emi_face';

/** @param {Iterable<string>} present node names found in the file */
export function checkNodes(present) {
  const have = new Set(present);
  return {
    missingRequired: REQUIRED.filter(n => !have.has(n)),
    missingOptional: OPTIONAL.filter(n => !have.has(n)),
  };
}
