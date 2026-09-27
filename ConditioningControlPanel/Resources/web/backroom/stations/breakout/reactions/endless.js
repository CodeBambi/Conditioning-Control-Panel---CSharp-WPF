// Capture gathers inward; launch releases a small shower. Grey stays quiet.
export default {
  demolitionCapture(fx,d) {
    if(!fx.colour)return;
    const rgb=fx.colours.MINT;
    fx.stamps.push({kind:'ring',x:d.x,y:d.y,r0:150,r1:45,life:.55,rgb});
    if(fx.reduced)return;
    fx.P.burst(d.x,d.y,rgb,32,100,.65,{gv:0,r0:1,r1:2.5});
  },
  demolitionLaunch(fx,d) {
    if(!fx.colour)return;
    const rgb=fx.colours.GOLD;
    fx.stamps.push({kind:'ring',x:d.x,y:d.y,r0:24,r1:135,life:.5,rgb});
    if(fx.reduced)return;
    fx.P.spray(d.x,d.y,Math.atan2(d.vy||-1,d.vx||0)+Math.PI,1.25,rgb,48,240,.65,{gv:45});
    fx.P.burst(d.x,d.y,fx.colours.WHITE,12,170,.35,{gv:0,r0:1,r1:2});
    fx.cam.kick(4,.5,.008);
  },
};
