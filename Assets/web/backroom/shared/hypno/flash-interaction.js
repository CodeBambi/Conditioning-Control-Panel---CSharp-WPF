const activeFlashes = new Set();
let exitGraceUntil = 0;
export function trackFlash(node) { flashesBusy(); activeFlashes.add(node); }
export function flashesBusy(now = performance.now()) {
  for (const node of activeFlashes) if (!node.isConnected) {
    activeFlashes.delete(node); exitGraceUntil = Math.max(exitGraceUntil, now + 500);
  }
  return activeFlashes.size > 0 || now < exitGraceUntil;
}
/** Back Room-only V2 samples. A gesture owns one flash, never the full-screen layer. */
export function interactionKind(roll, speed = 0) {
  return roll < 1 / 3 ? 'shatter' : speed >= .5 ? 'fling' : 'slide';
}
export function armFlashInteraction(node, { random = Math.random, motion = () => true } = {}) {
  trackFlash(node);
  let grab = null, ended = false;
  node.style.pointerEvents = 'auto'; node.style.touchAction = 'none'; node.style.cursor = 'grab';
  const animate = (target, frames, duration) => target.animate(frames, {duration, fill:'forwards', easing:'ease-in-out'});
  const finish = (kind, vx = 0, vy = 0) => {
    if (ended) return; ended = true; clearTimeout(node.__drop);
    node.style.pointerEvents = 'none'; node.dataset.interaction = kind;
    const alpha=Number(getComputedStyle(node).opacity)||.65;
    if (!motion()) { animate(node,[{opacity:alpha},{opacity:0}],150); node.__drop=setTimeout(()=>node.remove(),160); return; }
    if (kind === 'shatter') {
      for(let i=0;i<9;i++) {
        const col=i%3,row=Math.floor(i/3),piece=node.cloneNode(false);
        piece.dataset.shard=String(i);piece.style.pointerEvents='none';
        piece.style.clipPath=`inset(${row*100/3}% ${(2-col)*100/3}% ${(2-row)*100/3}% ${col*100/3}%)`;
        piece.style.transformOrigin=`${(col+.5)*100/3}% ${(row+.5)*100/3}%`;
        node.parentNode.append(piece);trackFlash(piece);
        const dx=(col-1)*90+(random()-.5)*40,dy=(row-1)*70+100;
        animate(piece,[{transform:'none',opacity:alpha},{transform:`perspective(600px) translate(${dx}px,${dy}px) rotateX(${(row-1)*65}deg) rotateY(${(col-1)*65}deg) rotate(${(random()-.5)*65}deg)`,opacity:0}],700);
        setTimeout(()=>piece.remove(),720);
      }
      node.remove(); return;
    }
    const r=node.getBoundingClientRect(),speed=Math.hypot(vx,vy);
    if(speed<.1){vx=r.x+r.width/2<innerWidth/2?-1:1;vy=(random()-.5)*.6;}
    const length=Math.hypot(vx,vy),distance=Math.max(r.width,r.height)*.65;
    animate(node,[{transform:'none',opacity:alpha,filter:'blur(0px)'},{transform:`translate(${vx/length*distance}px,${vy/length*distance}px) rotate(${vx<0?-24:24}deg) scale(.88)`,opacity:0,filter:'blur(9px)'}],kind==='fling'?550:650);
    node.__drop=setTimeout(()=>node.remove(),700);
  };
  node.addEventListener('pointerdown',e=>{
    if(ended||grab||e.button!==0||Number(getComputedStyle(node).opacity)<.15)return;
    e.preventDefault();e.stopPropagation();
    const r=node.getBoundingClientRect(),opacity=getComputedStyle(node).opacity;
    for(const a of node.getAnimations())a.cancel();clearTimeout(node.__drop);
    Object.assign(node.style,{left:r.x+'px',top:r.y+'px',width:r.width+'px',height:r.height+'px',transform:'none',opacity,cursor:'grabbing'});
    grab={id:e.pointerId,x:e.clientX,y:e.clientY,left:r.x,top:r.y,lastX:e.clientX,lastY:e.clientY,at:performance.now(),vx:0,vy:0};
    node.setPointerCapture(e.pointerId);
    node.__drop=setTimeout(()=>{grab=null;finish('slide');},8000);
  });
  node.addEventListener('pointermove',e=>{
    if(!grab||e.pointerId!==grab.id)return;
    e.preventDefault();e.stopPropagation();
    const now=performance.now(),dt=Math.max(1,now-grab.at);
    grab.vx=(e.clientX-grab.lastX)/dt;grab.vy=(e.clientY-grab.lastY)/dt;
    grab.lastX=e.clientX;grab.lastY=e.clientY;grab.at=now;
    node.style.left=grab.left+e.clientX-grab.x+'px';node.style.top=grab.top+e.clientY-grab.y+'px';
  });
  const release=e=>{
    if(!grab||e.pointerId!==grab.id)return;
    e.preventDefault();e.stopPropagation();
    const g=grab;grab=null;
    const stale=performance.now()-g.at>100,vx=stale?0:g.vx,vy=stale?0:g.vy;
    finish(e.type==='pointercancel'?'slide':interactionKind(random(),Math.hypot(vx,vy)),vx,vy);
    if(node.hasPointerCapture(e.pointerId))node.releasePointerCapture(e.pointerId);
  };
  node.addEventListener('pointerup',release);node.addEventListener('pointercancel',release);
  node.addEventListener('lostpointercapture',e=>{if(grab?.id===e.pointerId){grab=null;finish('slide');}});
  node.addEventListener('click',e=>{e.preventDefault();e.stopPropagation();});
}
