/** Hidden parents hide their children too; absent fixtures fail open for callers. */
export function visibleInTree(object) {
  for (let node = object; node; node = node.parent) if (node.visible === false) return false;
  return true;
}

/** Shared slot play and handle gestures own their reel offsets while active. */
export function idleReelEligible(model) {
  for (let node = model; node; node = node.parent) {
    if (node.userData?.slotPlaying || node.userData?.slotHandlePulling) return false;
  }
  return true;
}
