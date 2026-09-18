/** The authored window covers 1.4 cells. Keep two neighbours warm on each side. */
export function reelCellVisible(index, count, angle, spinning = false) {
  if (spinning || count <= 5) return true;
  const centre = (angle / (Math.PI * 2) + .5) * count - .5;
  const distance = ((index - centre + count / 2) % count + count) % count - count / 2;
  return Math.abs(distance) <= 2.5;
}

/** Static symbols do not change between highlights; animated art refreshes at 10 Hz. */
export function reelPaintStamp(kind, now, still, hit = 0, ghost = 0, mood = 'idle') {
  const animated = kind === 'gif' || kind === 'spiral' || kind === 'emi';
  return `${animated && !still ? Math.floor(now / 100) : 'still'}|${hit.toFixed(4)}|${ghost.toFixed(4)}|${kind === 'emi' ? mood : ''}`;
}
