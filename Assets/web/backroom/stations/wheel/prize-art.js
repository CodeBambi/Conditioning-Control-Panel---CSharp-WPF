import * as T from 'three';

// Shared by the walking room and seated game; odds determine the wedge widths.
export function prizeColor(s) {
  return s.kind==='jackpot'?'#C88727':s.kind==='nothing'?'#334852'
    :s.kind==='decoration'?'#167B72':s.kind==='double'?'#6740A0'
    :s.pay>=150?'#92274F':['#963A80','#4B3C85','#AC4566'][s.index%3];
}
export function sectorPoints(a0,a1,r0=.185,r1=.711) {
  const n=Math.max(3,Math.ceil((a1-a0)*48)),p=[];
  for(let j=0;j<=n;j++){const a=a0+(a1-a0)*j/n;p.push(new T.Vector2(Math.sin(a)*r1,Math.cos(a)*r1));}
  for(let j=n;j>=0;j--){const a=a0+(a1-a0)*j/n;p.push(new T.Vector2(Math.sin(a)*r0,Math.cos(a)*r0));}
  return p;
}
const printedLabels=new Map();
function textureFor(c){
  const tex=new T.CanvasTexture(c);Object.assign(tex,{colorSpace:T.SRGBColorSpace,generateMipmaps:true,minFilter:T.LinearMipmapLinearFilter,magFilter:T.LinearFilter,anisotropy:4});return tex;
}
function labelTexture(s,w,h,text) {
  const key=JSON.stringify([s.kind,w,h,text]);
  if(printedLabels.has(key))return textureFor(printedLabels.get(key));
  const c=document.createElement('canvas');c.height=1024;c.width=Math.max(80,Math.round(1024*w/h));
  const g=c.getContext('2d'),cw=c.width,ch=c.height,narrow=w<.19;
  g.textAlign='center';g.textBaseline='middle';g.lineJoin='round';
  const value=s.kind==='nothing'?'0':text.big.replace('\u2605 ','');
  function letter(v,x,y,size,max){
    g.font=`900 ${size}px Arial, sans-serif`;g.lineWidth=size*.035;
    g.strokeStyle='#30172F';g.shadowColor='#200d2c';g.shadowBlur=9;g.shadowOffsetY=7;
    g.strokeText(v,x,y,max);g.fillStyle='#FFF3CE';g.fillText(v,x,y,max);g.shadowBlur=0;g.shadowOffsetY=0;
  }
  if(narrow){g.translate(cw/2,ch/2);g.rotate(-Math.PI/2);letter(value,0,0,cw*.87,ch*.86);}
  else{
    const grad=g.createLinearGradient(0,0,cw,0);grad.addColorStop(0,'#ad7335');grad.addColorStop(.5,'#fff0b8');grad.addColorStop(1,'#ad7335');
    g.strokeStyle=grad;g.lineWidth=7;g.beginPath();g.moveTo(cw*.12,480);g.lineTo(cw*.88,480);g.stroke();
    if(s.kind==='decoration'){
      const u=Math.min(cw*.8,360)/140;g.save();g.translate(cw/2,260);g.scale(u,u);
      g.fillStyle='#fff0c7';g.shadowColor='#123934';g.shadowBlur=7;g.shadowOffsetY=6;
      g.fillRect(-57,-20,114,75);g.fillRect(-64,-35,128,20);
      g.shadowBlur=0;g.shadowOffsetY=0;g.fillStyle='#BA5578';g.fillRect(-9,-36,18,93);
      g.strokeStyle='#ffe3a5';g.lineWidth=9;g.beginPath();g.ellipse(-22,-51,24,14,.4,0,Math.PI*2);g.ellipse(22,-51,24,14,-.4,0,Math.PI*2);g.stroke();g.restore();
    }else letter(value,cw/2,265,Math.min(400,cw*.87),cw*.95);
    const words=text.small.split(/\s+/),lines=[];g.font=`800 ${Math.min(190,cw*.28)}px Arial, sans-serif`;
    for(const word of words){const i=lines.length-1;if(i>=0&&g.measureText(lines[i]+' '+word).width<cw*.95)lines[i]+=' '+word;else lines.push(word);}
    g.fillStyle='#FFF5E5';g.shadowColor='#210e29';g.shadowBlur=5;g.shadowOffsetY=3;
    lines.slice(0,3).forEach((line,i)=>g.fillText(line,cw/2,575+i*160,cw*.96));
  }
  printedLabels.set(key,c);if(printedLabels.size>16)printedLabels.delete(printedLabels.keys().next().value);
  return textureFor(c);
}
export function createPrizeSector(s,text,decorate=m=>m){
  const color=prizeColor(s),gap=Math.min(.003,s.span*.08),pts=sectorPoints(s.start+gap,s.end-gap),shape=new T.Shape(pts);
  const mat=decorate(new T.MeshPhysicalMaterial({color,roughness:.24,metalness:.18,clearcoat:1,clearcoatRoughness:.16}));
  mat.emissive.set(color).lerp(new T.Color(0xffdca8),.35);mat.emissiveIntensity=0;
  const mesh=new T.Mesh(new T.ExtrudeGeometry(shape,{depth:.018,bevelEnabled:true,bevelSegments:3,steps:1,bevelSize:.003,bevelThickness:.003}),mat);
  // A soft lacquer highlight keeps the raised enamel readable in room lighting.
  const pos=mesh.geometry.attributes.position,colors=[];
  for(let i=0;i<pos.count;i++){
    const x=pos.getX(i),y=pos.getY(i),r=Math.hypot(x,y);
    const glint=.66+.24*Math.exp(-Math.pow((x+y-.28)/.42,2))+.10*Math.min(1,r/.711);
    colors.push(glint,glint,glint);
  }
  mesh.geometry.setAttribute('color',new T.Float32BufferAttribute(colors,3));mat.vertexColors=true;
  mesh.position.z=.076;mesh.userData={index:s.index,h:0,base:color,mid:s.mid};
  const inset=Math.min(.014,s.span*.1),trimShape=new T.Shape(pts);
  trimShape.holes.push(new T.Path(sectorPoints(s.start+gap+inset,s.end-gap-inset,.194,.702)));
  const border=new T.Mesh(new T.ShapeGeometry(trimShape),decorate(new T.MeshStandardMaterial({color:0xf6d490,metalness:.65,roughness:.26})));border.position.z=.098;
  const w=Math.min(.44,s.span*.45*.92),h=.38;
  const label=new T.Mesh(new T.PlaneGeometry(w,h),new T.MeshBasicMaterial({map:labelTexture(s,w,h,text),transparent:true,depthWrite:false,toneMapped:false}));
  label.position.set(Math.sin(s.mid)*.475,Math.cos(s.mid)*.475,.102);label.rotation.z=-s.mid;label.userData={a:s.mid,r:.475,spin:-s.mid,index:s.index,print:text};
  const pr=Math.min(.009,s.span*.12);
  const peg=new T.Mesh(new T.CylinderGeometry(pr,pr,.038,10),new T.MeshStandardMaterial({color:0xf4d28d,metalness:.65,roughness:.3}));
  peg.rotation.x=Math.PI/2;peg.position.set(Math.sin(s.start)*.738,Math.cos(s.start)*.738,.094);peg.userData={a:s.start,r:.738};
  return {mesh,label,peg,border,pts,color};
}
