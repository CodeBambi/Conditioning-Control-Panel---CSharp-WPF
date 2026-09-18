// A small, deliberate seated look. The room restores its base pose every frame.
export function createSeatLook(stage, { mount, enabled, surface }) {
  let drag = null, x = 0, y = 0, tx = 0, ty = 0, dead = false;
  const button = document.createElement('button');
  button.className = 'br-seat-center'; button.textContent = 'Center view';
  button.title = 'Drag empty felt to look around';
  button.style.cssText = 'position:absolute;right:16px;top:145px;z-index:6;pointer-events:auto;font-size:11px;padding:6px 10px;';
  button.hidden = true; mount.append(button);
  const reset = () => { tx = ty = 0; };
  button.addEventListener('click', reset);
  const end = (e) => {
    if(e?.pointerId!=null && drag && e.pointerId!==drag.id)return;
    const held = drag; drag = null;
    if (held && stage.canvas.hasPointerCapture?.(held.id)) stage.canvas.releasePointerCapture(held.id);
  };
  function down(e) {
    if (drag || e.button !== 0 || !stage.ready || !enabled() || !surface(e)) return;
    drag = { id:e.pointerId, x:e.clientX, y:e.clientY, tx, ty };
    stage.canvas.setPointerCapture?.(e.pointerId);
  }
  function move(e) {
    if (!drag || drag.id !== e.pointerId) return;
    if (!enabled() || !stage.ready) { end(); return; }
    const rect = stage.canvas.getBoundingClientRect();
    const dir = stage.lookInverted?.() ? -1 : 1;   // the room's Invert camera switch reaches the seated look too
    tx = Math.max(-.075, Math.min(.075, drag.tx - (e.clientX-drag.x)/rect.width*.3*dir));
    ty = Math.max(-.045, Math.min(.045, drag.ty - (e.clientY-drag.y)/rect.height*.2*dir));
    e.preventDefault();
  }
  const events = { pointerdown:down, pointermove:move, pointerup:end, pointercancel:end, lostpointercapture:end };
  for (const [name,fn] of Object.entries(events)) stage.canvas.addEventListener(name,fn);
  const blur = () => { end(); reset(); };
  window.addEventListener('blur',blur);
  document.addEventListener('visibilitychange',blur);
  const release = () => {
    if (dead) return; dead=true; end();
    for (const [name,fn] of Object.entries(events)) stage.canvas.removeEventListener(name,fn);
    window.removeEventListener('blur',blur); document.removeEventListener('visibilitychange',blur);
    button.remove();
  };
  const off = stage.register({
    update(dt) {
      if (!enabled() || !stage.ready) { end(); x=y=tx=ty=0; }
      const k=1-Math.exp(-Math.max(0,dt)*12);
      x+=(tx-x)*k; y+=(ty-y)*k;
      stage.camera.rotation.y+=x; stage.camera.rotation.x+=y;
      stage.lookShift = Math.abs(x)+Math.abs(y)>.001;
      button.hidden = Math.abs(x)+Math.abs(y)+Math.abs(tx)+Math.abs(ty)<.001;
    },
    dispose:release,
  });
  return () => { off(); release(); };
}
