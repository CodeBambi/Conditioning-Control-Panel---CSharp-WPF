// Local watch page. Uses the playable engine and renderer; never writes a save.
import {createGame} from './game.js';
import {createRenderer} from './render.js';
import {createMedia} from './payloads.js';
import {createAudio} from './audio.js';
import {seededRandom} from './endless-layout.js';
import {createShowcasePilot} from './showcase-pilot.js';

const $=s=>document.querySelector(s), canvas=$('canvas');
const scenes=[
 {from:0,title:'SLINGSHOT GARDEN',warmup:30,duration:18,launchX:.44},
 {from:2,title:'TIDAL CHIMES',warmup:25,duration:18,launchX:.5},
 {from:5,title:'MOVING MANTRA',warmup:3,duration:24,launchX:.5},
];
const words=['BREATHE','RELAX','SOFT','FOCUS','STILL','DEEPER'];
const names=['cheer','giggle','flirt','adoring','blowkiss','entrancing','sultry','praise'];
const media=createMedia({ctx:{media:async()=>({gifs:names.map((name,i)=>({key:'g'+i,url:'/emotes/'+name+'.gif'})),words:words.map((text,i)=>({key:'w'+i,text}))})}});
const audio=createAudio({bpm:96});
audio.setMix('bed',.42);audio.setMix('sfx',.6);audio.setMix('word',0);
let game=null,renderer=null,pilot=null,elapsed=0,shown=0,acc=0,last=0,selected=0,paused=false,sound=false,warming=false,captionUntil=0,captionPriority=0;
const DT=1/120;
const captions={pendulumAnchorHit:['Hinge taking damage',1],pendulumRelease:['Hinge broken. The demolition ball is free.',3],demolitionCapture:['Caught by the spiral...',4],demolitionLaunch:['Launched toward the next hinge.',5],reform:['The surviving bricks gather into a new word.',2],capture:['The spiral catches the ball.',1],spiral:['Back into the board.',1],split:['One ball becomes several.',2]};
function onEvent(name,data={}){
 if(warming||!game)return;
 renderer?.onGameEvent(name,data);
 const s=game.snapshot();
 if(sound){
  audio.cue(name,{...data,xN:(data.x??640)/1280,sat:s.sat,state:s.state,combo:s.combo});
  if(name==='hit')audio.hit(data.kind,{combo:data.combo||0,x:(data.x??640)/1280});
  if(name==='brickDamage')audio.hit('damage',{x:(data.x??640)/1280});
  if(name==='pendulumRelease')audio.pendulumRelease({x:(data.x??640)/1280});
  if(name==='metronome')audio.metronome(data.accent);
 }
 const c=captions[name];
 if(c&&(shown>=captionUntil||c[1]>=captionPriority)){
  $('#caption').textContent=c[0];captionUntil=shown+(c[1]>=3?3:1.8);captionPriority=c[1];
 }
}
function resize(){if(renderer)renderer.resize(Math.min(1536,Math.round(canvas.clientWidth*devicePixelRatio)),Math.min(864,Math.round(canvas.clientHeight*devicePixelRatio)));}
function select(index){
 selected=(index+scenes.length)%scenes.length;const scene=scenes[selected];
 game?.dispose();renderer?.dispose();game=null;renderer=null;elapsed=shown=acc=0;last=0;captionUntil=0;captionPriority=0;
 pilot=createShowcasePilot({launchX:scene.launchX});warming=true;
 game=createGame({endless:true,seed:2709,from:scene.from,savedSaturation:.85,speedScale:.8,rng:seededRandom(270900+scene.from),
  audio:{now:()=>elapsed,beat:{spb:.625,origin:0,phase:()=>elapsed/.625%1}},onEvent});
 // Advancing to a highlight uses only paddle inputs and normal simulation steps.
 for(let t=0;t<scene.warmup;t+=DT){elapsed+=DT;game.step(DT,pilot.input(game.snapshot()));}
 warming=false;renderer=createRenderer(canvas,{media,rng:seededRandom(99+scene.from)});resize();
 $('#label').textContent=scene.title;$('#caption').classList.remove('visible');
 $('#play').href='./play.html?endless=1&seed=2709&board='+(scene.from+1)+'&sat=.85';
 for(const b of document.querySelectorAll('[data-scene]'))b.setAttribute('aria-pressed',String(Number(b.dataset.scene)===selected));
 audio.setState('colour');audio.setSaturation(.85);
 render(0);
}
function render(dt){
 const s=game.snapshot();media.tick(performance.now());
 renderer.draw(s,{media,words:media.trailWords(12),now:elapsed,dt,reduced:false,word:null});
 $('#caption').classList.toggle('visible',shown<captionUntil);
 $('#stats').textContent=s.stats.bricks+' BRICK'+(s.stats.bricks===1?'':'S')+' / '+s.balls.filter(b=>!b.lost).length+' BALL'+(s.balls.length===1?'':'S');
 $('#progress').style.width=(Math.min(1,shown/scenes[selected].duration)*100)+'%';
}
function frame(ts){
 requestAnimationFrame(frame);const dt=last?Math.min(.06,(ts-last)/1000):0;last=ts;
 if(paused||document.hidden)return;
 acc+=dt;
 while(acc>=DT){acc-=DT;elapsed+=DT;shown+=DT;game.step(DT,pilot.input(game.snapshot()));}
 if(shown>=scenes[selected].duration||game.snapshot().stats.walls!==scenes[selected].from){select(selected+1);return;}
 if(sound){audio.setSaturation(game.snapshot().sat);audio.setTimeScale(game.snapshot().timeScale);}
 render(dt);
}
$('#pause').onclick=()=>{paused=!paused;$('#pause').textContent=paused?'Resume':'Pause';if(paused)audio.stop();else if(sound)audio.start();};
$('#replay').onclick=()=>select(selected);
$('#sound').onclick=()=>{sound=!sound;$('#sound').textContent=sound?'Sound on':'Sound off';$('#sound').setAttribute('aria-pressed',String(sound));if(sound&&!paused)audio.start();else audio.stop();};
for(const b of document.querySelectorAll('[data-scene]'))b.onclick=()=>select(Number(b.dataset.scene));
window.addEventListener('resize',resize);
document.addEventListener('visibilitychange',()=>{last=0;if(document.hidden)audio.stop();else if(sound&&!paused)audio.start();});
window.addEventListener('pagehide',()=>{audio.destroy();media.dispose();renderer?.dispose();game?.dispose();});
await media.load();select(0);requestAnimationFrame(frame);
