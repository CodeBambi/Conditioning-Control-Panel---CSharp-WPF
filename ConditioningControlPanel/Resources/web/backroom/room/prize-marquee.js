import * as T from 'three';

// The physical ticker carries room patter between priority win announcements.
// Motion Off holds a readable title.
//
// PERF (2026-09-18): the scrolling patter used to be repainted into a 2048 x 256 canvas 20 times a second
// (fillText with an 18 px shadow blur, then a full re-upload), and on an RTX 5080 that one board was more
// than half of what the browser's GPU process spent at rest in the room. The patter is now painted ONCE
// onto a strip exactly one period wide and scrolled by the texture's UV offset, which costs nothing per
// frame; the gold frame is a second static plane over it, so the look is the same. The still title and
// the win lines still paint into the board canvas, once per change, as they always did.
const BOARD_W = 2048, BOARD_H = 256;
const STRIP_SCALE = 0.5;                  // the strip is painted at half the board's resolution
const SCROLL_PX_PER_S = 115;              // board px per second, the speed the 20 Hz repaint used
const INTERIOR = { x: 38, y: 32, w: 1972, h: 192 };   // the text window inside the frame, board px

function background(x, w, h) {
  const bg = x.createLinearGradient(0, 0, 0, h);
  bg.addColorStop(0, '#492141'); bg.addColorStop(.5, '#24152e'); bg.addColorStop(1, '#351b39');
  x.fillStyle = bg; x.fillRect(0, 0, w, h);
}
function frameStrokes(x) {
  x.strokeStyle = '#dfb77b'; x.lineWidth = 5;
  x.beginPath(); x.roundRect(12, 12, 2024, 232, 34); x.stroke();
  x.strokeStyle = '#8f5d86'; x.lineWidth = 2;
  x.beginPath(); x.roundRect(24, 24, 2000, 208, 27); x.stroke();
}
function textStyle(x, scale = 1) {
  x.font = `900 ${Math.round(108 * scale)}px "Arial Rounded MT Bold", "Trebuchet MS", sans-serif`; x.textBaseline = 'middle';
  x.fillStyle = '#ffd783'; x.shadowColor = '#ff65be'; x.shadowBlur = 18 * scale;
}

/** One customer-facing marquee, replacing the two baked counter titles. */
export function createPrizeMarquee(text) {
  const c = document.createElement('canvas'); c.width = BOARD_W; c.height = BOARD_H;
  const x = c.getContext('2d');
  /** The board at rest or carrying a win line: the frame and one centred line, painted once per change. */
  const paint = (line) => {
    background(x, BOARD_W, BOARD_H);
    frameStrokes(x);
    x.save(); x.beginPath(); x.rect(INTERIOR.x, INTERIOR.y, INTERIOR.w, INTERIOR.h); x.clip();
    textStyle(x);
    x.textAlign = 'center'; x.fillText(line, 1024, 133, 1900);
    x.restore();
    x.shadowBlur = 0;
  };
  const name = String(text).toUpperCase();
  paint(name);
  const texture = new T.CanvasTexture(c); texture.colorSpace = T.SRGBColorSpace;

  // The frame alone, transparent inside the text window, laid over the scrolling strip.
  const fc = document.createElement('canvas'); fc.width = BOARD_W; fc.height = BOARD_H;
  const fx = fc.getContext('2d');
  background(fx, BOARD_W, BOARD_H);
  fx.clearRect(INTERIOR.x, INTERIOR.y, INTERIOR.w, INTERIOR.h);
  frameStrokes(fx);
  const frameTexture = new T.CanvasTexture(fc); frameTexture.colorSpace = T.SRGBColorSpace;

  const patter = name + '   /   SPARKLES IN. SOUVENIRS OUT.   /   EMI HAS YOUR RECEIPT   /   GOOD TASTE. QUESTIONABLE LUCK.   /   TAKE A LITTLE GLOW HOME   /   ';
  /** The patter, one period wide at STRIP_SCALE, painted once. Wrapped, so the UV offset scrolls it seamlessly. */
  const strip = (() => {
    const probe = document.createElement('canvas').getContext('2d');
    textStyle(probe, STRIP_SCALE);
    const span = Math.ceil(probe.measureText(patter).width + 150 * STRIP_SCALE);   // the same 150 px gap the repaint used
    const sc = document.createElement('canvas'); sc.width = Math.max(64, span); sc.height = Math.round(BOARD_H * STRIP_SCALE);
    const sx = sc.getContext('2d');
    background(sx, sc.width, sc.height);
    textStyle(sx, STRIP_SCALE);
    sx.textAlign = 'left';
    // Twice, so the glyphs that cross the seam read whole on both sides of it.
    sx.fillText(patter, 0, 133 * STRIP_SCALE); sx.fillText(patter, span, 133 * STRIP_SCALE);
    sx.shadowBlur = 0;
    const t = new T.CanvasTexture(sc); t.colorSpace = T.SRGBColorSpace; t.wrapS = T.RepeatWrapping;
    t.repeat.x = (BOARD_W * STRIP_SCALE) / sc.width;   // the face shows one board width of the strip
    return { texture: t, span: sc.width };
  })();

  const group = new T.Group(); group.name = 'prize_parlour_marquee';
  const back = new T.Mesh(new T.BoxGeometry(3.46, .45, .065),
    new T.MeshStandardMaterial({ color: '#c39456', metalness: .75, roughness: .32 }));
  const faceMaterial = new T.MeshStandardMaterial({ map: texture, emissiveMap: texture, emissive: 0xffffff, emissiveIntensity: .55, roughness: .5 });
  const face = new T.Mesh(new T.PlaneGeometry(3.38, .41), faceMaterial);
  face.position.z = .034;
  const frame = new T.Mesh(new T.PlaneGeometry(3.38, .41),
    new T.MeshStandardMaterial({ map: frameTexture, emissiveMap: frameTexture, emissive: 0xffffff, emissiveIntensity: .55, roughness: .5, transparent: true, depthWrite: false }));
  frame.position.z = .0345; frame.visible = false;
  group.add(back, face, frame);
  group.position.set(0, 2.58, 1.36);
  /** `name` is what the board says at rest; `text` is what it says now (the scene's debug seam reads it). */
  group.userData.name = name;
  group.userData.text = name;
  const showStrip = (on) => {
    const want = on ? strip.texture : texture;
    if (faceMaterial.map === want) return;
    faceMaterial.map = faceMaterial.emissiveMap = want;
    frame.visible = on;
  };
  /** Carry a line, or null for the Parlour's own name. Returns whether the board actually changed. */
  group.userData.say = (line) => {
    const want = line == null || line === '' ? group.userData.name : String(line).toUpperCase();
    if (want === group.userData.text) return false;
    paint(want); group.userData.text = want; texture.needsUpdate = true;
    return true;
  };
  let clock=0, painted=-1;
  group.userData.update=(dt,still)=>{
    if(!still)clock+=Math.min(.05,Math.max(0,dt));
    const mode=still?'still':group.userData.text===name?'scroll':'win';
    if(mode==='scroll'){
      showStrip(true); painted=-1;
      strip.texture.offset.x=((clock*SCROLL_PX_PER_S*STRIP_SCALE)/strip.span)%1;
      return;
    }
    showStrip(false);
    const key=mode+':'+group.userData.text;if(key===painted)return;painted=key;
    paint(group.userData.text);texture.needsUpdate=true;
  };
  return group;
}
