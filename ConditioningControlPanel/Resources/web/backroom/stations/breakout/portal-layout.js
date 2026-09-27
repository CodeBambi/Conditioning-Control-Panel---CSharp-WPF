// Portal recipes reserve broad lanes for balls, payloads and released weights.
export const PORTAL_FAMILIES = ['portal-backdoor', 'portal-delivery', 'portal-demolition'];
export function populatePortalBoard(board, {w,h,rng,add,hinges}) {
  const u=Math.min(w/1280,h/720), mirror=rng()<.5;
  const x=v=>mirror?w-v*u:v*u, y=v=>v*u;
  const angle=a=>mirror?Math.PI-a:a;
  const place=(cx,cy,bw,bh,row,col,extra={})=>add(x(cx),y(cy),bw*u,bh*u,row,col,extra);
  const pair=(a,b)=>{board.portals=[a,b].map((p,id)=>({id,pair:0,x:x(p[0]),y:y(p[1]),angle:angle(p[2]),halfLength:65*u,depth:16*u}));};
  const shift=Math.floor(rng()*25)-12;
  if(board.kind==='portal-backdoor') {
    board.name='Backdoor';board.mechanics=['portals','armour'];
    pair([312+shift,315,Math.PI/2],[1000,230+shift,Math.PI]);
    for(let row=0;row<7;row++)for(let col=0;col<6;col++)
      place(716+col*26,104+row*33,22,25,row,col);
    for(let row=0;row<8;row++)place(660,90+row*34,25,27,row,0,{strength:2,hp:2});
    for(let col=0;col<8;col++)place(175+col*41,104,34,23,0,col);
  } else if(board.kind==='portal-delivery') {
    board.name='Spiral delivery';board.mechanics=['portals','spiral-bricks','powerups'];
    pair([330+shift,390,-Math.PI/2],[1000,330+shift,Math.PI]);
    for(let row=0;row<3;row++)for(let col=0;col<10;col++) {
      const extra=row===2 ? (col%3===0?{spiral:'whirl',spin:mirror?-1:1,portalCargo:true}:col%3===1?{gif:col%8,tier:1,portalCargo:true}:{powerup:col%2?'laser':'shield',word:null,gif:-1,spiral:null}) : {};
      place(155+col*39,54+row*33,32,23,row,col,extra);
    }
    for(let row=0;row<3;row++)for(let col=0;col<6;col++)
      place(699+col*43,75+row*34,35,25,row+2,col);
  } else {
    board.name='Demolition express';board.mechanics=['portals','pendulums','dome'];board.dome=true;
    // Keep one source hinge and an empty destination lane into the central spiral.
    hinges();board.pendulums.length=1;
    for(let i=board.bricks.length-1;i>=0;i--)if(board.bricks[i].pendulumId===1||board.bricks[i].curtain)board.bricks.splice(i,1);
    // An exposed source hinge leaves the reverse exit clear for even a grown bubble.
    const p=board.pendulums[0], dx=w*.20-p.pivotX; p.pivotX+=dx;p.x+=dx;
    for(const b of board.bricks)if(b.pendulumId===0)b.x+=dx;
    board.portals=[{id:0,pair:0,x:w*.33,y:h*.42,angle:Math.PI,halfLength:65*u,depth:16*u},
      {id:1,pair:0,x:w*.745,y:h*.40,angle:Math.PI,halfLength:65*u,depth:16*u}];
    for(let row=0;row<2;row++)for(let col=0;col<11;col++)
      add(w*.48+col*39*u,(55+row*38)*u,32*u,24*u,row+2,col);
    for(let row=0;row<4;row++)for(let col=0;col<3;col++)
      add(w*.845+col*42*u,(165+row*38)*u,34*u,25*u,row+2,col);
  }
  return board;
}
