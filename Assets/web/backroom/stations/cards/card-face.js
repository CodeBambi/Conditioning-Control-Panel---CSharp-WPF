// Runtime card faces. Deck and Loom are shared with the audited canvas view.
import * as T from 'three';
import { rankLabel, suitOf } from './hand.js';
import { shakeOffset } from './glitch.js';
export function createCardFace() {
  const canvas = document.createElement('canvas'); canvas.width = 128; canvas.height = 180;
  const g = canvas.getContext('2d'), texture = new T.CanvasTexture(canvas);
  texture.colorSpace = T.SRGBColorSpace; texture.generateMipmaps = false;
  texture.minFilter = T.LinearFilter;
  return {
    texture,
    paint(code, face, o, kit, shake = 0) {
      g.clearRect(0, 0, 128, 180); g.fillStyle = face && code ? '#f7f0fb' : '#2a1745'; g.fillRect(0, 0, 128, 180);
      if (face && code) {
        if (o.gates.flash && o.deck) o.deck.draw(g, o.deck.keyFor(code), 4, 4, 120, 172, { alpha: .65 });
        g.fillStyle = '#f7f0fb99'; g.fillRect(4, 4, 42, 65);
        const s = suitOf(code); g.fillStyle = s.red ? '#d6246e' : '#22123a';
        // THE SLIP (glitch.js) tears the corner glyphs while the face crosses over. The big pip holds.
        const sx = shake ? shakeOffset(shake, 0, o.now) : 0, sy = shake ? shakeOffset(shake, 1, o.now) : 0;
        g.textAlign = 'center'; g.font = 'bold 32px Segoe UI'; g.fillText(rankLabel(code), 24 + sx, 33);
        g.font = '26px Segoe UI Symbol'; g.fillText(s.glyph, 24 - sy, 60);
        g.font = '56px Segoe UI Symbol'; g.fillText(s.glyph, 72, 135);
      } else if (!(o.gates.spiral && kit?.draw(g, 'backs', 0, 0, 128, 180, { now: o.now, backing: 'small' }))) {
        g.strokeStyle = '#e8c27a66';
        for (let i = -180; i < 300; i += 18) { g.beginPath(); g.moveTo(i, 0); g.lineTo(i + 180, 180); g.stroke(); }
      }
      g.strokeStyle = '#e8c27a'; g.lineWidth = 3; g.strokeRect(2, 2, 124, 176); texture.needsUpdate = true;
    },
    dispose() { texture.dispose(); canvas.width = canvas.height = 1; },
  };
}
