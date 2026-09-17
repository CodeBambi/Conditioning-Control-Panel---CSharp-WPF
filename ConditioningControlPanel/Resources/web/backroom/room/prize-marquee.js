import * as T from 'three';

// The physical ticker carries room patter between priority win announcements.
// Motion Off holds a readable title. Canvas uploads are capped at 20 Hz.

/** One customer-facing marquee, replacing the two baked counter titles. */
export function createPrizeMarquee(text) {
  const c = document.createElement('canvas'); c.width = 2048; c.height = 256;
  const x = c.getContext('2d');
  const paint = (line, offset=null) => {
    const bg = x.createLinearGradient(0, 0, 0, 256);
    bg.addColorStop(0, '#492141'); bg.addColorStop(.5, '#24152e'); bg.addColorStop(1, '#351b39');
    x.fillStyle = bg; x.fillRect(0, 0, 2048, 256);
    x.strokeStyle = '#dfb77b'; x.lineWidth = 5;
    x.beginPath(); x.roundRect(12, 12, 2024, 232, 34); x.stroke();
    x.strokeStyle = '#8f5d86'; x.lineWidth = 2;
    x.beginPath(); x.roundRect(24, 24, 2000, 208, 27); x.stroke();
    x.save(); x.beginPath(); x.rect(38,32,1972,192); x.clip();
    x.font = '900 108px "Arial Rounded MT Bold", "Trebuchet MS", sans-serif'; x.textBaseline = 'middle';
    x.fillStyle = '#ffd783'; x.shadowColor = '#ff65be'; x.shadowBlur = 18;
    if(offset===null){x.textAlign='center';x.fillText(line,1024,133,1900);}
    else {x.textAlign='left';const span=x.measureText(line).width+150;for(let px=40-offset%span;px<2048;px+=span)x.fillText(line,px,133);}
    x.restore();
    x.shadowBlur = 0;
  };
  const name = String(text).toUpperCase();
  paint(name);
  const texture = new T.CanvasTexture(c); texture.colorSpace = T.SRGBColorSpace;
  const group = new T.Group(); group.name = 'prize_parlour_marquee';
  const back = new T.Mesh(new T.BoxGeometry(3.46, .45, .065),
    new T.MeshStandardMaterial({ color: '#c39456', metalness: .75, roughness: .32 }));
  const face = new T.Mesh(new T.PlaneGeometry(3.38, .41),
    new T.MeshStandardMaterial({ map: texture, emissiveMap: texture, emissive: 0xffffff, emissiveIntensity: .55, roughness: .5 }));
  face.position.z = .034; group.add(back, face);
  group.position.set(0, 2.58, 1.36);
  /** `name` is what the board says at rest; `text` is what it says now (the scene's debug seam reads it). */
  group.userData.name = name;
  group.userData.text = name;
  /** Carry a line, or null for the Parlour's own name. Returns whether the board actually changed. */
  group.userData.say = (line) => {
    const want = line == null || line === '' ? group.userData.name : String(line).toUpperCase();
    if (want === group.userData.text) return false;
    paint(want); group.userData.text = want; texture.needsUpdate = true;
    return true;
  };
  let clock=0, painted=-1;
  const patter=name+'   /   SPARKLES IN. SOUVENIRS OUT.   /   EMI HAS YOUR RECEIPT   /   GOOD TASTE. QUESTIONABLE LUCK.   /   TAKE A LITTLE GLOW HOME   /   ';
  group.userData.update=(dt,still)=>{
    if(!still)clock+=Math.min(.05,Math.max(0,dt));
    const tick=Math.floor(clock*20), mode=still?'still':group.userData.text===name?'scroll':'win';
    const key=mode+':'+tick+':'+group.userData.text;if(key===painted)return;painted=key;
    paint(mode==='scroll'?patter:group.userData.text,mode==='scroll'?clock*115:null);texture.needsUpdate=true;
  };
  return group;
}
