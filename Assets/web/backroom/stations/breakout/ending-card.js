// Static, control-free end card. The same painter supplies the office monitor.
export const ENDING_CARD_AT = 3.8;
export const ENDING_READY_AT = 5;
export function paintEndingCard(g, w=1280, h=720) {
  g.save();g.scale(w/1280,h/720);
  const bg=g.createLinearGradient(0,0,1280,720);bg.addColorStop(0,'#160c2b');bg.addColorStop(.58,'#361349');bg.addColorStop(1,'#762b5a');
  g.fillStyle=bg;g.fillRect(0,0,1280,720);
  const halo=g.createRadialGradient(640,285,10,640,285,380);halo.addColorStop(0,'rgba(215,117,255,.15)');halo.addColorStop(1,'rgba(215,117,255,0)');g.fillStyle=halo;g.fillRect(0,0,1280,720);
  const colours=['#ab74ed','#d594f4','#ef9dcc','#948de3'];
  // The last wall has opened. Quiet fragments stay outside the reading area.
  for(let i=0;i<38;i++) {
    const x=70+(i*197%1140),y=52+(i*137%600);
    if(x>265&&x<1015&&y>225&&y<520)continue;
    g.globalAlpha=.18+(i%4)*.055;g.fillStyle=colours[i%4];g.fillRect(x,y,6+(i%3)*4,4+(i%2)*4);
  }
  g.globalAlpha=1;
  // Broken brick arch and a single released ball, drawn on the pixel grid.
  const pieces=[[-94,20,-.3],[-67,-3,-.2],[-36,-19,-.1],[6,-25,.1],[49,-12,.25],[83,12,.35]];
  for(let i=0;i<pieces.length;i++){const [x,y,a]=pieces[i];g.save();g.translate(640+x,212+y);g.rotate(a);g.fillStyle=colours[i%4];g.fillRect(-15,-7,30,14);g.restore();}
  g.fillStyle='#f8d5ee';g.fillRect(635,172,10,10);
  g.textAlign='center';g.textBaseline='middle';g.font='bold 15px monospace';g.fillStyle='#c497d7';g.fillText('THE END',640,300);
  const title=g.createLinearGradient(330,0,950,0);title.addColorStop(0,'#c99bff');title.addColorStop(1,'#ffc0de');g.fillStyle=title;
  g.font='bold 66px monospace';g.fillText('YOU BROKE OUT',640,375);
  g.fillStyle='#cbb4d2';g.font='18px monospace';g.fillText('TAKE A BREATH.',640,445);
  g.fillStyle='rgba(226,175,234,.3)';g.fillRect(608,495,64,2);
  g.restore();
}
export function createEndingCard(makeCanvas=()=>document.createElement('canvas')) {
  let card;
  return {draw(g,w,h,age,reduced=false){
    if(age<ENDING_CARD_AT)return false;
    if(!card){card=makeCanvas();card.width=1280;card.height=720;paintEndingCard(card.getContext('2d',{willReadFrequently:true}));}
    g.save();g.globalAlpha=reduced?1:Math.min(1,(age-ENDING_CARD_AT)/.8);
    const scale=Math.min(w/1280,h/720),dw=1280*scale,dh=720*scale;
    g.drawImage(card,(w-dw)/2,(h-dh)/2,dw,dh);g.restore();return true;
  },reset(){card=null;}};
}
