// Short pentatonic phrases sit inside the existing score and volume buses.
const silent = (s,d) => (d?.state || s.state) === 'grey';
export default {
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
