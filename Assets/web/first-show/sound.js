// A small, disposable soundtrack. One context, one output, no timers surviving the show.
export function createShowSound({ volume = .5 } = {}) {
 let context = null, master = null, limiter = null, noise = null, lastStep = -1;
 const clamp = n => Math.max(0, Math.min(1, n));
 function tone(frequency, level, length, type = 'sine', delay = 0, end = frequency) {
  if (!context || context.state === 'closed') return;
  const t = context.currentTime + delay, oscillator = context.createOscillator(), gain = context.createGain();
  oscillator.type = type; oscillator.frequency.setValueAtTime(frequency, t);
  oscillator.frequency.exponentialRampToValueAtTime(Math.max(20, end), t + length);
  gain.gain.setValueAtTime(.0001, t); gain.gain.exponentialRampToValueAtTime(Math.max(.0002, level), t + .009);
  gain.gain.exponentialRampToValueAtTime(.0001, t + length);
  oscillator.connect(gain); gain.connect(master); oscillator.start(t); oscillator.stop(t + length + .02);
  oscillator.onended = () => { oscillator.disconnect(); gain.disconnect(); };
 }
 function air(level, length, frequency, delay = 0, end = frequency) {
  if (!context || !noise) return;
  const t = context.currentTime + delay, source = context.createBufferSource(), filter = context.createBiquadFilter(), gain = context.createGain();
  source.buffer = noise; source.loop = true; filter.type = 'bandpass'; filter.Q.value = .7;
  filter.frequency.setValueAtTime(frequency,t); filter.frequency.exponentialRampToValueAtTime(end,t+length);
  gain.gain.setValueAtTime(.0001,t); gain.gain.linearRampToValueAtTime(level,t+length*.23); gain.gain.exponentialRampToValueAtTime(.0001,t+length);
  source.connect(filter); filter.connect(gain); gain.connect(master); source.start(t); source.stop(t+length+.02);
  source.onended=()=>{source.disconnect();filter.disconnect();gain.disconnect();};
 }
 function chord(notes, level=.035, length=.55) { notes.forEach((n,i)=>tone(n,level,length,'triangle',i*.035)); }
 return {
  start() {
   if(context) return;
   try {
    context=new AudioContext(); master=context.createGain(); limiter=context.createDynamicsCompressor();
    master.gain.value=clamp(volume)*1.6; limiter.threshold.value=-12; limiter.knee.value=12; limiter.ratio.value=5; limiter.attack.value=.004; limiter.release.value=.18;
    master.connect(limiter); limiter.connect(context.destination);
    noise=context.createBuffer(1,context.sampleRate*2,context.sampleRate);
    const samples=noise.getChannelData(0);for(let i=0;i<samples.length;i++) samples[i]=Math.random()*2-1;
    lastStep=-1; context.resume().catch(()=>{});
   } catch { context=null; }
  },
  tick(seconds) {
   if(!context || seconds>=32) return;
   // 120 BPM, eighth-note glass motif, with percussion added as the picture fills.
   const step=Math.floor(seconds*4);
   if(step===lastStep) return;
   lastStep=step;
   const melody=[523.25,659.25,783.99,1046.5,880,783.99,659.25,783.99];
   if(seconds>=2 && seconds<28) {
    tone(melody[step%8],seconds>=24?.028:.019,.19,'sine');
    if(step%4===0) {tone(120,.19,.18,'sine',0,43);tone([130.81,164.81,110,196][Math.floor(step/16)%4],.035,.4,'triangle');}
    if(seconds>=7 && step%4===2) air(.044,.095,2100);
    if(seconds>=19 && step%2===1) air(.018,.035,6500);
   }
   if(seconds>=28 && step%2===0) tone(261.63*Math.pow(2,(seconds-28)/2),.026,.2,'sine');
  },
  talk(pitch=1,level=.4) {tone(620*pitch,.055*level,.085,'sine');},
  cue(name) {
   if(!context) return;
   switch(name){
    case 'entrance': air(.10,.45,1200,0,4200);chord([261.63,392,523.25],.05,.7);break;
    case 'flash': tone([523.25,659.25,783.99][Math.max(0,lastStep)%3],.035,.12,'triangle');break;
    case 'pink': air(.12,.8,700,0,4300);chord([329.63,523.25,659.25],.05,.9);break;
    case 'drain': air(.09,1.3,3300,0,160);tone(196,.075,1.1,'sine',0,65.4);break;
    case 'words': tone(783.99,.065,.12,'triangle');tone(1046.5,.04,.2,'sine',.13);break;
    case 'bubbles': [523.25,659.25,783.99,1046.5].forEach((n,i)=>tone(n,.05,.2,'sine',i*.07,n*1.15));break;
    case 'pop': tone(880,.09,.12,'sine',0,1760);air(.035,.08,5200);break;
    case 'mix': chord([261.63,392,523.25,659.25],.07,1.2);air(.08,.4,1500);break;
    case 'gather': air(.13,3.5,300,0,4800);tone(65.4,.08,3,'sine',0,523.25);break;
    case 'reveal': tone(110,.2,.45,'sine',0,38);air(.16,.7,3000,0,600);chord([261.63,392,523.25,659.25,1046.5],.065,2.4);break;
   }
  },
  stop() {
   const old=context;context=null;noise=null;master=null;limiter=null;lastStep=-1;
   if(old && old.state!=='closed') old.close().catch(()=>{});
  }
 };
}
