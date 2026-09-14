/* nodes.js - the Daily Daze glb node contract, shared by scene.js and tests/nodes-check.mjs so the page
 * and the owner's re-check read the same list (blender-scripting wheel/CONTRACT.md: root wheel_station,
 * Y up, front +Z, wheel_rotor spins on local Z, pointer hinged at the top, no baked animation).
 * Only names are contract, never positions, sizes or mesh counts. */

/** Without these the page shows a "model missing X" card. */
export const REQUIRED = ['wheel_station', 'wheel_rotor', 'pointer'];

/** These degrade quietly with a console warning. */
export const OPTIONAL = [
  'layout_sectors', 'hub_spiral', 'hub_lip', 'title_screen', 'title_letters', 'status_screen', 'status_letters',
  'star_mount', 'emi_topper', 'EMI_glass', 'shoulderL', 'shoulderR', 'cam_seat',
  ...Array.from({ length: 32 }, (_, i) => `bulb_${String(i).padStart(2, '0')}`),
];

/** Material the EMI face atlas binds to (on EMI_glass). */
export const FACE_MATERIAL = 'emi_face';

/** @param {Iterable<string>} present node names found in the file */
export function checkNodes(present) {
  const have = new Set(present);
  return {
    missingRequired: REQUIRED.filter(n => !have.has(n)),
    missingOptional: OPTIONAL.filter(n => !have.has(n)),
  };
}
