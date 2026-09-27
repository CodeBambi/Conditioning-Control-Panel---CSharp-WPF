// Capture gathers inward; launch releases a small shower. Grey stays quiet.
export default {
  portalTransit(fx,d) {
    if(fx.reduced)return;
    const rgb=fx.colour?fx.colours.MINT:fx.colours.GREY;
    fx.stamps.push({kind:'ring',x:d.fromX,y:d.fromY,r0:30,r1:5,life:.22,rgb});
    fx.stamps.push({kind:'ring',x:d.x,y:d.y,r0:6,r1:40,life:.32,rgb});
    if(!fx.colour)return;
    fx.P.implode(d.fromX,d.fromY,fx.colours.PINK,16,120,.25,{from:32});
    fx.P.spray(d.x,d.y,Math.atan2(d.vy||0,d.vx||1),.9,rgb,24,190,.4,{gv:0});
    fx.P.burst(d.x,d.y,fx.colours.WHITE,8,90,.25,{gv:0,r0:.7,r1:1.5});
  },
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
