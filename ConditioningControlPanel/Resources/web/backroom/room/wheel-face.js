import * as T from 'three';
import { layoutOf } from '../stations/wheel/wheel.js';

/** Room-owned face: visible before sitting down, borrowed and hidden by the game. */
export function createWheelFace(holder) {
  const rotor = holder.getObjectByName('wheel_rotor');
  if (!rotor) return null;
  const canvas = document.createElement('canvas'); canvas.width = canvas.height = 1024;
  const ctx = canvas.getContext('2d'), texture = new T.CanvasTexture(canvas);
  texture.colorSpace = T.SRGBColorSpace;
  const material = new T.MeshBasicMaterial({ map: texture, toneMapped: false, side: T.DoubleSide });
  const face = new T.Mesh(new T.RingGeometry(.185, .711, 128), material);
  face.name = 'room_wheel_face'; face.position.z = .077; rotor.add(face);
  function paint(slices) {
    const layout = layoutOf(slices);
    ctx.clearRect(0, 0, 1024, 1024);
    // A neutral enamel face while the authoritative table is loading, with no invented prizes.
    ctx.fillStyle = '#f7bdd2'; ctx.fillRect(0, 0, 1024, 1024);
    if (!layout) return;
    for (const s of layout) {
      ctx.beginPath(); ctx.moveTo(512,512);
      ctx.arc(512,512,512,s.start-Math.PI/2,s.end-Math.PI/2); ctx.closePath();
      ctx.fillStyle = s.kind === 'jackpot' ? '#f4d896' : s.id === 'spoiled_rotten' ? '#b82c70' : ['#f7bdd2','#ffebdd','#e6a2c4','#ffe7d2'][s.index%4];
      ctx.fill(); ctx.strokeStyle = '#c99c56'; ctx.lineWidth = 4; ctx.stroke();
      if (s.span > .13) {
        ctx.save(); ctx.translate(512+Math.sin(s.mid)*355,512-Math.cos(s.mid)*355); ctx.rotate(s.mid);
        ctx.textAlign='center'; ctx.fillStyle='#42203f'; ctx.font='bold 32px sans-serif';
        ctx.fillText(s.pay > 0 ? String(s.pay) : s.kind === 'double' ? 'x2' : s.kind === 'decoration' ? '✦' : '○',0,0,120); ctx.restore();
      }
    }
    texture.needsUpdate = true; face.userData.sliceCount = layout.length;
  }
  face.userData.setSlices = paint; paint(null);
  return { dispose(){ face.removeFromParent(); face.geometry.dispose(); material.dispose(); texture.dispose(); } };
}

export function setWheelFace(scene, slices) {
  scene?.getObjectByName('room_wheel_face')?.userData.setSlices(slices);
}
