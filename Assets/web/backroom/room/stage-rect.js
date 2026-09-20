/** Where a box of the page sits inside the room's canvas, for the three spaces that have to agree on it.
 *
 * A scissored pass (the catalogue close-up, vending-view.js) lives in two coordinate systems at once. The
 * renderer measures in its own CSS pixels, the drawing buffer divided by the pixel ratio, which is whatever
 * the last resize() applied; the buttons drawn over the pass and the fingers that press them are in page
 * pixels. The two carry the same numbers only while the buffer still matches the canvas's CSS box, and a
 * phone that has just been turned breaks that for a frame or two, as does any box that the canvas clips.
 * Anything that then normalises a pointer against the raw mount rect is reading a rectangle the pass was
 * not drawn in, and the pick lands next to the finger: the owner's vending machine selecting the bay above
 * the plant he pressed (2026-09-17). So this returns one clip, three ways:
 *
 *   page - the box on screen, for normalising a pointer event into it,
 *   dom  - the same box relative to the mount, what an absolutely positioned child needs,
 *   gl   - the same box in the renderer's own pixels, y up from the buffer's bottom edge, for setViewport.
 *
 * `bufferWidth` and `bufferHeight` are the drawing buffer in the renderer's CSS pixels, so on any surface
 * whose buffer matches its CSS box - the desktop WebView2 host, and a phone that is holding still - the
 * scale is exactly 1 and `gl` is the number the caller would have computed by hand.
 */
export function stageRect(canvasRect, mountRect, bufferWidth, bufferHeight, min = 4) {
  if (!canvasRect || !mountRect) return null;
  const left = Math.max(mountRect.left, canvasRect.left), right = Math.min(mountRect.right, canvasRect.right);
  const top = Math.max(mountRect.top, canvasRect.top), bottom = Math.min(mountRect.bottom, canvasRect.bottom);
  const w = Math.floor(right - left), h = Math.floor(bottom - top);
  const cw = canvasRect.right - canvasRect.left, ch = canvasRect.bottom - canvasRect.top;
  if (!(w > min && h > min) || !(cw > 0) || !(ch > 0)) return null;
  const sx = (bufferWidth > 0 ? bufferWidth : cw) / cw, sy = (bufferHeight > 0 ? bufferHeight : ch) / ch;
  return {
    page: { x: left, y: top, w, h },
    dom: { x: Math.round(left - mountRect.left), y: Math.round(top - mountRect.top), w, h },
    gl: {
      x: Math.round((left - canvasRect.left) * sx), y: Math.round((canvasRect.bottom - bottom) * sy),
      w: Math.max(1, Math.round(w * sx)), h: Math.max(1, Math.round(h * sy)),
    },
  };
}

/** The page rectangle of a viewport the renderer was handed. setViewport measures in the renderer's own
 * CSS pixels with y up from the buffer's bottom edge, which is the space the room's partial passes are
 * described in (scene.js, customization-panel.js previewBox), while a pointer event arrives in page pixels
 * with y down from the top of the page. A caller that wants to normalise a finger into such a pass has to
 * turn one space into the other first. `rendererWidth` and `rendererHeight` are the size the viewport was
 * measured against, so a canvas whose CSS box has changed since then scales rather than drifts.
 */
export function viewportPageRect(canvasRect, viewport, rendererWidth, rendererHeight) {
  const cw = canvasRect.right - canvasRect.left, ch = canvasRect.bottom - canvasRect.top;
  const sx = rendererWidth > 0 ? cw / rendererWidth : 1, sy = rendererHeight > 0 ? ch / rendererHeight : 1;
  const left = canvasRect.left + viewport.x * sx, top = canvasRect.bottom - (viewport.y + viewport.h) * sy;
  const width = viewport.w * sx, height = viewport.h * sy;
  return { left, top, right: left + width, bottom: top + height, width, height };
}

/** A pointer event inside such a box as normalised device coordinates: -1..1, y up, the form a ray wants. */
export function ndcIn(box, clientX, clientY) {
  return { x: (clientX - box.page.x) / box.page.w * 2 - 1, y: -((clientY - box.page.y) / box.page.h) * 2 + 1 };
}
