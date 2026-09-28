// Short pentatonic phrases sit inside the existing score and volume buses.
const silent = (s,d) => (d?.state || s.state) === 'grey';
export default {
  portalTransit(s,d) {
    const pan=d?.xN??.5;
    // A physical crossing answers immediately; its pitched echo joins the score.
    s.play([s.noise(1500,.11,silent(s,d)?.018:.035,{hzTo:4600,pan})],s.now,s.bus.sfx);
    if(silent(s,d))return;
    const notes=[2,5].map((step,i)=>s.tone(s.ROOT_HZ*s.SEMI(s.pentatonic(step)),.13,.035,
      {at:i*.045,wave:'triangle',lp:3800,wet:true,pan}));
    s.play(notes,s.quantise(),s.bus.sfx);
  },
  demolitionCapture(s,d) {
    if(silent(s,d))return;
    const notes=[3,2,0].map((step,i)=>s.tone(s.ROOT_HZ*s.SEMI(s.pentatonic(step)),.18,.045,
      {at:i*.065,wave:'triangle',lp:2400,wet:true,pan:d?.xN??.5}));
    s.play(notes,s.quantise(),s.bus.sfx);
  },
  demolitionLaunch(s,d) {
    if(silent(s,d))return;
    const notes=[0,3,5].map((step,i)=>s.tone(s.ROOT_HZ*s.SEMI(s.pentatonic(step)),.16,.05,
      {at:i*.045,wave:'triangle',lp:4200,wet:true,pan:d?.xN??.5}));
    s.play(notes,s.quantise(),s.bus.sfx);
    s.play([s.noise(750,.12,.035,{hzTo:2300,pan:d?.xN??.5})],s.now,s.bus.sfx);
  },
};
