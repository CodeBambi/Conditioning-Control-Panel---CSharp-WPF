import * as T from 'three';

/** One customer-facing marquee, replacing the two baked counter titles. */
export function createPrizeMarquee(text) {
  const c = document.createElement('canvas'); c.width = 2048; c.height = 256;
  const x = c.getContext('2d');
  const bg = x.createLinearGradient(0, 0, 0, 256);
  bg.addColorStop(0, '#492141'); bg.addColorStop(.5, '#24152e'); bg.addColorStop(1, '#351b39');
  x.fillStyle = bg; x.fillRect(0, 0, 2048, 256);
  x.strokeStyle = '#dfb77b'; x.lineWidth = 5;
  x.beginPath(); x.roundRect(12, 12, 2024, 232, 34); x.stroke();
  x.strokeStyle = '#8f5d86'; x.lineWidth = 2;
  x.beginPath(); x.roundRect(24, 24, 2000, 208, 27); x.stroke();
  for (const side of [-1, 1]) {
    x.save(); x.translate(1024 + side * 870, 128); x.scale(side, 1);
    x.strokeStyle = '#dfb77b'; x.lineWidth = 3;
    x.beginPath(); x.moveTo(-46, 0); x.bezierCurveTo(-110, -58, -136, -30, -104, -14);
    x.moveTo(-46, 0); x.bezierCurveTo(-110, 58, -136, 30, -104, 14); x.stroke();
    x.fillStyle = '#ffbddd'; x.shadowColor = '#ff69bf'; x.shadowBlur = 18;
    x.beginPath(); x.moveTo(0, -42); x.quadraticCurveTo(7, -6, 32, 0);
    x.quadraticCurveTo(7, 6, 0, 42); x.quadraticCurveTo(-7, 6, -32, 0);
    x.quadraticCurveTo(-7, -6, 0, -42); x.fill(); x.restore();
  }
  x.font = '600 110px Georgia, serif'; x.textAlign = 'center'; x.textBaseline = 'middle';
  x.fillStyle = '#ffe4d0'; x.shadowColor = '#f65fba'; x.shadowBlur = 14;
  x.fillText(String(text).toUpperCase(), 1024, 133, 1490);
  const texture = new T.CanvasTexture(c); texture.colorSpace = T.SRGBColorSpace;
  const group = new T.Group(); group.name = 'prize_parlour_marquee';
  const back = new T.Mesh(new T.BoxGeometry(3.46, .45, .065),
    new T.MeshStandardMaterial({ color: '#c39456', metalness: .75, roughness: .32 }));
  const face = new T.Mesh(new T.PlaneGeometry(3.38, .41),
    new T.MeshStandardMaterial({ map: texture, emissiveMap: texture, emissive: 0xffffff, emissiveIntensity: .55, roughness: .5 }));
  face.position.z = .034; group.add(back, face);
  group.position.set(0, 2.58, 1.36);
  group.userData.text = String(text).toUpperCase();
  return group;
}
