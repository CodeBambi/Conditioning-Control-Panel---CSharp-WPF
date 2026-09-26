import { createClock, duration, beats } from './timeline.mjs';
import { createFace } from '../arcademy/emi/face.js';
import { createVox } from '../arcademy/emi/vox.js';
import { createShowSound } from './sound.js';
import { createShowVfx } from './vfx.js';

const $ = id => document.getElementById(id), stage = $('stage'), layer = $('objects'), card = $('card');
const defaults = {
 title: 'THE EMI SHOW', greeting: 'Hey. Want to see what CCP can do?',
 pickHint: 'Pick pictures for the show. This loads demo media directly from Scrolller. Change it later, or use your own files.',
 warning: 'This 35-second show plays audio and flashes images and effects on your screen.',
 panicHint: 'stops everything. Esc is the default. Change it in Settings.', ready: 'I am ready', later: 'Later', loading: 'Getting your pictures ready...',
 stop: 'Stop show', apply: 'Make it mine', explore: 'Explore CCP', replay: 'Watch again', bundled: 'Use built-in pictures', mediaError: 'These pictures could not load. Try the built-in pictures.', popBubble: 'Pop bubble',
 beat0: 'Ready? {panicKey} stops everything.', beat1: 'Your pictures. All around you.', beat2: 'Pink. Spiral. Instant atmosphere.',
 beat3: 'Brain drain. Just for a moment.', beat4: 'Your words join the mix.', beat5: 'Go on. Pop one.',
 beat6: 'Now put it all together.', beat7: 'And bring it all back.', beat8: 'Your world. Your CCP.', word1: 'Your pictures', word2: 'Your world'
};
let config = {}, lex = defaults, media = [], objects = [], elapsed = 0, frame = 0, last = 0, nextFlash = 2, nextBubble = 19, serial = 0, sound = null;
let flashIndex = 0, mediaRequest = 0, loadingMedia = Promise.resolve(), selected = null, phase = 'intro', face = null;
let width = innerWidth, height = innerHeight, currentBeat = -1, beatTime = 0, reactionAt = -5, reactionPower = 1, faceText = '', cursor = {x:0,y:0}, glance = 0, nextTrail = 0, pointerPop = false;
let speechAt=-10, popAt=-10, bodyPose='', lookX=0, lookY=0;
const vox = createVox({ log() {} });
const send = data => window.chrome?.webview?.postMessage(data);
const motion = () => config.motion === 'off' ? 0 : config.motion === 'reduced' ? .4 : 1;
const clamp = v => Math.max(0, Math.min(1, v));
const ease = t => 1-(1-clamp(t))**3;
const back = t => {t=clamp(t)-1;return 1+2.70158*t*t*t+1.70158*t*t;};
const vfx = createShowVfx($('sparkles'),motion);
function resize() { width=innerWidth; height=innerHeight; vfx.resize(width,height); }
addEventListener('resize',resize); resize();
addEventListener('pointermove',e=>{cursor.x=(e.clientX-width/2)/width;cursor.y=(e.clientY-height/2)/height;});
document.addEventListener('arcademy-sfx',e=>{if(clock.running){speechAt=performance.now()/1000;sound?.talk(e.detail.pitch||1,e.detail.level||.4);}});
function react(power=1) { reactionAt=elapsed; reactionPower=power; }
function impact(x,y,power=1) { vfx.burst(x,y,85*power,power);vfx.ring(x,y,power); }
function add(kind,nx,ny,time,slot=-1) {
 const el=document.createElement(kind==='bubble'?'button':'div');el.className='object '+kind;
 const item={el,kind,x:nx*width,y:ny*height,born:time,seed:serial++,slot,removed:false,arrived:false};
 if(kind==='flash') {
  const source=media[flashIndex%media.length];if(!source)return;
  for(const old of objects)if(old.kind==='flash'&&old.slot===slot&&old.retire===undefined)old.retire=time;
  const visual=document.createElement(source.type==='video'?'video':'img');visual.src=source.url;visual.alt='';
  if(source.type==='video'){visual.muted=true;visual.loop=true;visual.autoplay=true;visual.playsInline=true;visual.play().catch(()=>{});}
  el.append(visual);el.style.setProperty('--card-color',['#ff9cdc','#abbeff','#be91ff','#91e9ee'][slot%4]);
  vfx.comet(width*.5,height*.44,item.x,item.y);sound?.cue('flash');glance=nx<.5?-1:1;react(.35);
 } else if(kind==='word') {el.textContent=slot?lex.word2:lex.word1;impact(item.x,item.y,.65);}
 else {
  const source=media[item.seed%media.length];if(source){const picture=document.createElement(source.type==='video'?'video':'img');picture.src=source.url;picture.alt='';if(source.type==='video'){picture.muted=true;picture.loop=true;picture.autoplay=true;picture.playsInline=true;picture.play().catch(()=>{});}el.append(picture);}
  el.setAttribute('aria-label',lex.popBubble);el.onclick=()=>{if(!clock.running||elapsed>=28)return;impact(item.drawX,item.drawY,1.25);sound?.cue('pop');react(1.2);pointerPop=true;popAt=elapsed;face?.draw('^___^');remove(item);};
 }
 layer.append(el);objects.push(item);
}
function remove(item){item.removed=true;item.el.querySelector('video')?.pause();item.el.remove();}
const positions=[[.17,.24],[.83,.24],[.11,.49],[.89,.49],[.23,.68],[.77,.68]];
const expressions=['^_~','*_*','^_~','@_@','^_^','^___^','*_*','o_o','^___^'];
const cueNames=['entrance','flash','pink','drain','words','bubbles','mix','gather','reveal'];
function beat(id) {
 currentBeat=id;beatTime=elapsed;reactionAt=elapsed;reactionPower=id===7?.7:1.15;pointerPop=false;
 const raw=lex['beat'+id],key=config.panicKey||'Esc',text=raw.replace('{panicKey}',key);
 $('line').replaceChildren();if(raw.includes('{panicKey}')){const [before,after]=raw.split('{panicKey}'),badge=document.createElement('kbd');badge.textContent=key;$('line').append(document.createTextNode(before),badge,document.createTextNode(after));}else $('line').textContent=text;
 $('beatLabel').textContent=String(id+1).padStart(2,'0');
 $('caption').classList.remove('land');void $('caption').offsetWidth;$('caption').classList.add('land');
 stage.dataset.beat=id;
 for(const [i,dot] of [...$('beats').children].entries())dot.classList.toggle('lit',i<=id);
 face?.draw(expressions[id]);faceText=expressions[id];vox.speak(text,{face:id===8||id===5?'celebration':'idle'});
 sound?.cue(cueNames[id]);send({type:'beat',id,text});
 impact(width/2,height*.43,id===8?3:id===0?1.8:1.15);
 if(id===4){add('word',.27,.36,15,0);add('word',.73,.57,15,1);}
 if(id===5){for(let i=0;i<5;i++)add('bubble',.12+i*.19,.67+(i%2)*.08,19-i*.1);nextBubble=19.55;}
 if(id===6){for(let i=0;i<4;i++)impact(width*(i%2?.83:.17),height*(i<2?.25:.66),.7);}
 if(id===7){objects.forEach(item=>{item.gatherX=item.drawX??item.x;item.gatherY=item.drawY??item.y;});}
 if(id===8){objects.forEach(remove);objects=[];vfx.ring(width/2,height*.43,3,'#fff2cc');}
}
const clock=createClock(beat,finish);
function animateEmi(time,dt) {
 const playing=phase==='playing'||phase==='ending',m=motion(),age=Math.max(0,elapsed-reactionAt);
 const beatAge=Math.max(0,elapsed-beatTime),popAge=elapsed-popAt;
 const hit=Math.sin(age*12)*Math.exp(-age*5)*reactionPower*m;
 const speech=Math.exp(-Math.max(0,time-speechAt)*15)*m;
 const next=beats[currentBeat+1],until=next===undefined?10:next-elapsed;
 const anticipate=playing&&until>0&&until<.32?Math.sin(until/.32*Math.PI)*m:0;
 const reveal=m?ease((elapsed-32)/.75):Number(elapsed>=32),entrance=playing&&m?ease(elapsed/.75):1;
 const targetX=playing?(elapsed>=28?0:glance*.85):cursor.x*2,targetY=playing?-.12:cursor.y*2;
 const follow=1-Math.exp(-dt*8);lookX+=(targetX-lookX)*follow;lookY+=(targetY-lookY)*follow;
 const breath=Math.sin(time*2.2),bob=(Math.sin(time*2.6)*5+Math.sin(time*1.1)*3)*m;
 const popHop=popAge>=0&&popAge<.85?Math.sin(popAge/.85*Math.PI)*29*m:0;
 const greetingHop=currentBeat===0?Math.sin(clamp(beatAge/1.1)*Math.PI)*21*m:0;
 const beatHop=beatAge<.75?Math.sin(beatAge/.75*Math.PI)*13*m:0;
 const dancing=currentBeat===6;
 const sidestep=dancing?Math.sin(beatAge*Math.PI*2)*17*m:lookX*11*m;
 const gatherLean=currentBeat===7?Math.sin(beatAge*3)*6*m:0;
 let turn=lookX*6*m+Math.sin(time*1.8)*1.5*m+hit*6+gatherLean-speech*2;
 let y=height*(playing?.43+.14*reveal:.225)+bob-hit*17-popHop-greetingHop-beatHop-speech*5+anticipate*9+(1-entrance)*60*m;
 let squash=1+hit*.08+speech*.025+breath*.008*m-anticipate*.09;
 if(currentBeat===3&&elapsed<14){turn+=Math.sin(time*9)*5*m;squash+=Math.sin(time*6)*.03*m;}
 const scale=(playing?1-.15*reveal:1)*entrance;
 $('emi').style.left=(width/2+sidestep)+'px';
 $('emi').style.top=y+'px';
 $('emi').style.transform=`translate(-50%,-50%) rotate(${turn}deg) scale(${scale*(2-squash)},${scale*squash})`;
 let pose='body-idle';
 if(m){
  if(!playing)pose=Math.floor(time/2.4)%3===0?'body-smug':'body-idle';
  else if(currentBeat===7||currentBeat===8||popAge>=0&&popAge<.85)pose='body';
  else if(currentBeat===3)pose='body-shock';
  else if(dancing)pose='body-sway'+([1,2,3,4,3,2][Math.floor(beatAge*8)%6]);
  else if(beatAge<.8)pose=currentBeat===2||currentBeat===5?'body-smug':'body-shock';
  else if(time-speechAt<.18)pose='body-smug';
 }else if(currentBeat>=7)pose='body';
 if(pose!==bodyPose){bodyPose=pose;$('emi').querySelector('img').src=config.emi||'../arcademy/art/emi/'+pose+'.png';}
 const blinkPhase=time%6.7,blink=m&&(blinkPhase<.1||blinkPhase>.23&&blinkPhase<.3);
 let expression=expressions[Math.max(0,currentBeat)];
 if(!playing)expression=time%7<.8?'^_~':'^_^';
 else if(popAge>=0&&popAge<1.1)expression='^___^';
 else if(currentBeat===7)expression=beatAge<.5?'o_o':beatAge<2.5?'>_<':'*_*';
 else if(currentBeat===8)expression=beatAge<1.2?'^___^':'^_~';
 else if(beatAge<.3&&currentBeat!==0)expression='o_o';
 else if(currentBeat===1&&age<.3)expression='*_*';
 else if(currentBeat===5&&beatAge>1.6&&beatAge<2.4)expression='^_~';
 if(blink)expression='-_-';
 if(expression!==faceText){face?.draw(expression);faceText=expression;}
 $('emiFace')?.style.setProperty('translate',`${lookX*3*m}px ${lookY*2*m}px`);
 $('emi').style.setProperty('--speech-glow',String(.5+speech*.5));
}
function paint(now) {
 const dt=Math.min(.05,Math.max(0,(now-last)/1000));last=now;
 const time=clock.tick(now);if(time!==null)elapsed=time;
 const playing=phase==='playing'||phase==='ending';
 if(playing) {
  sound?.tick(elapsed);
  stage.classList.toggle('pink',elapsed>=7&&elapsed<30.8);stage.classList.toggle('draining',elapsed>=11&&elapsed<13);
  stage.classList.toggle('melting',elapsed>=12.3&&elapsed<14.6);stage.classList.toggle('gather',elapsed>=28&&elapsed<32);stage.classList.toggle('reveal',elapsed>=32);
  const sub=$('subliminal');sub.textContent=Math.floor(elapsed)%2?lex.word1:lex.word2;
  sub.style.opacity=elapsed>=15&&elapsed<28&&elapsed%2.7<.2?(motion()?'.14':'0'):'0';
  $('progress').style.width=`${elapsed/duration*100}%`;$('time').textContent=`00:${String(Math.max(0,Math.ceil(duration-elapsed))).padStart(2,'0')}`;
  if(elapsed>=nextFlash&&elapsed<28){const slot=flashIndex%6,p=positions[slot];add('flash',p[0],p[1],elapsed,slot);flashIndex++;nextFlash=elapsed+(flashIndex<6?.39:1.03);}
  if(elapsed>=nextBubble&&elapsed<28){add('bubble',.06+Math.random()*.88,.77,elapsed);nextBubble=elapsed+.6;}
  for(const item of objects) {
   const age=Math.max(0,elapsed-item.born),m=motion();let x=item.x,y=item.y,rotation=0,scale=1,opacity=1;
   if(elapsed>=28){
    const delay=(item.seed%6)*.055,p=clamp((elapsed-28-delay)/3.5),pull=p*p,dx=(item.gatherX??item.x)-width/2,dy=(item.gatherY??item.y)-height*.43,a=p*12*m;
    x=width/2+(dx*Math.cos(a)-dy*Math.sin(a))*(1-pull);y=height*.43+(dx*Math.sin(a)+dy*Math.cos(a))*(1-pull);
    scale=1-pull;rotation=p*480*m;opacity=1-p**6;
   }else{
    const entry=back(age/.62),entryMove=1-(1-entry)*m;opacity=clamp(age/.2);scale=1-(1-(.2+.8*entry))*m;
    if(item.kind==='flash'){
     x=width/2+(item.x-width/2)*entryMove;y=height*.44+(item.y-height*.44)*entryMove;
     x+=Math.sin(age*1.1+item.seed)*12*m;y+=Math.cos(age*1.4+item.seed)*9*m;rotation=(Math.sin(age*1.2+item.seed)*3+(item.slot%2?2:-2))*m;
     if(age>.4&&!item.arrived){impact(item.x,item.y,.55);item.arrived=true;}
     if(item.retire!==undefined){const out=clamp((elapsed-item.retire)/.3);opacity*=1-out;scale*=1+out*.15;if(out===1)remove(item);}
    }
    if(item.kind==='bubble'){x+=Math.sin(age*1.2+item.seed)*39*m;y-=age*36*m;scale*=.8+(item.seed%3)*.15;rotation=Math.sin(age)*5*m;if(y<height*.12)remove(item);}
    if(item.kind==='word'){x+=Math.sin(age*1.25+item.seed)*width*.045*m;y+=Math.sin(age*2.3)*height*.045*m;rotation=Math.sin(age*1.8)*5*m;}
   }
   if(!m){x=item.x;y=item.y;rotation=0;scale=1;}
   item.drawX=x;item.drawY=y;item.el.style.opacity=opacity;item.el.style.transform=`translate(${x}px,${y}px) translate(-50%,-50%) rotate(${rotation}deg) scale(${scale})`;
   if(elapsed>=nextTrail&&item.kind!=='word'&&elapsed<32)vfx.trail(x+(Math.random()-.5)*35,y+(Math.random()-.5)*30,item.kind==='bubble'?'#9beaff':'#ffb6e8',2+Math.random()*3);
  }
  if(elapsed>=nextTrail)nextTrail=elapsed+.035;
  objects=objects.filter(item=>!item.removed);
  if(elapsed>=28&&elapsed<32&&Math.random()<.55){const a=Math.random()*Math.PI*2,r=80+(32-elapsed)*25;vfx.trail(width/2+Math.cos(a)*r,height*.43+Math.sin(a)*r,'#ffe1fc',4);}
 }
 animateEmi(now/1000,dt);vfx.paint(dt,elapsed,phase);
 if(phase!=='stopped')frame=requestAnimationFrame(paint);
}
function clear() {
 clock.stop();cancelAnimationFrame(frame);vox.stop();sound?.stop();sound=null;objects.forEach(remove);objects=[];vfx.reset();
 stage.className=config.motion||'full';delete stage.dataset.beat;$('ending').hidden=true;$('stop').hidden=true;
}
function start() {
 if(!media.length)return;clear();phase='playing';elapsed=0;nextFlash=2;nextBubble=19;nextTrail=0;serial=0;flashIndex=0;currentBeat=-1;reactionAt=-5;popAt=-10;speechAt=-10;lookX=0;lookY=0;bodyPose='';
 sound=createShowSound({volume:config.volume??.5});sound.start();
 card.inert=true;card.setAttribute('aria-hidden','true');stage.classList.add('playing');$('stop').hidden=false;
 last=performance.now();clock.start(last);send({type:'start'});frame=requestAnimationFrame(paint);
}
function finish(){phase='ending';stage.classList.remove('playing');stage.classList.add('ended');$('ending').hidden=false;$('stop').hidden=true;vox.stop();sound?.stop();sound=null;send({type:'finish'});}
function stop(){clear();phase='stopped';send({type:'stop'});}
async function setMedia(items) {
 const request = ++mediaRequest; media = []; $('ready').disabled = true;
 const valid = (items || []).filter(m => m && /^(https:\/\/ccp\.(assets|show)\/|data:image\/)/i.test(m.url));
 if (!valid.length) return;
 $('status').textContent = lex.loading;
 const loaded = await Promise.all(valid.slice(0,12).map(source => new Promise(resolve => {
  const visual = document.createElement(source.type === 'video' ? 'video' : 'img');
  let done = false; const finish = ok => { if (done) return; done = true; clearTimeout(timer);
   let result = ok ? source : null;
   if (ok && !motion()) { try { const still = document.createElement('canvas'); still.width = visual.videoWidth || visual.naturalWidth; still.height = visual.videoHeight || visual.naturalHeight; still.getContext('2d').drawImage(visual,0,0); result = {url:still.toDataURL(),type:'image'}; } catch { result = null; } }
   if (source.type === 'video') { visual.pause(); visual.removeAttribute('src'); visual.load(); }
   resolve(result);
  };
  const timer = setTimeout(() => finish(false),8000); visual.onload = () => finish(true); visual.onloadeddata = () => finish(true); visual.onerror = () => finish(false);
  visual.muted = true; visual.preload = 'auto'; visual.crossOrigin = 'anonymous'; visual.src = source.url;
 })));
 if (request !== mediaRequest) return;
 media = loaded.filter(Boolean); $('status').textContent = ''; $('ready').disabled = !media.length;
 if (!media.length) setError(lex.mediaError || 'These pictures could not load. Try the built-in pictures.');
}
function setError(message) { $('status').textContent = message; $('ready').disabled = true; const button = document.createElement('button'); button.textContent = lex.bundled; button.onclick = () => { button.remove(); $('status').textContent = lex.loading; send({type:'bundled'}); }; $('status').append(document.createElement('br'),button); }
function init(options) {
 clear(); config = options || {}; lex = {...defaults,...config.lex}; phase = 'intro'; elapsed=0; currentBeat=-1; popAt=-10; speechAt=-10; bodyPose=''; stage.className = config.motion || 'full'; card.inert=false; card.removeAttribute('aria-hidden'); $('time').textContent='00:'+duration; last=performance.now(); frame=requestAnimationFrame(paint);
 for (const id of ['greeting','warning','ready','later','stop','apply','explore','replay','panicHint']) $(id).textContent = lex[id];
 document.querySelector('.eyebrow').textContent = lex.title; $('hint').textContent = lex.pickHint; $('panic').querySelector('kbd').textContent = config.panicKey || 'Esc';
 $('emi').querySelector('img').src = config.emi || '../arcademy/art/emi/body-idle.png';
 if (!$('emiFace') && !config.emi) { const c = document.createElement('canvas'); c.id = 'emiFace'; $('emi').append(c); face = createFace(c); face.draw('^_^'); }
 $('logo').querySelector('img').src = config.logo || '';
 $('presets').replaceChildren();
 for (const preset of config.presets || []) { const b = document.createElement('button'); b.textContent = preset.label; b.onclick = () => { selected = preset.id; for (const other of $('presets').children) other.classList.toggle('selected', other === b); $('ready').disabled = true; $('status').textContent = lex.loading; send({type:'preset',id:selected}); }; $('presets').append(b); }
 $('warning').textContent = `${lex.warning} ${config.panicKey || 'Esc'} ${lex.panicHint}`;
 loadingMedia = setMedia(config.media || []);
}
$('ready').onclick = start; $('stop').onclick = stop; $('later').onclick = () => { clear(); send({type:'later'}); };
$('apply').onclick = () => { clear(); send({type:'apply'}); }; $('explore').onclick = () => { clear(); send({type:'later'}); }; $('replay').onclick = start;
addEventListener('keydown', event => { const names = {Esc:'Escape',Escape:'Escape',Space:' ',Return:'Enter',Back:'Backspace',Delete:'Delete',Left:'ArrowLeft',Right:'ArrowRight',Up:'ArrowUp',Down:'ArrowDown'}; const key = names[config.panicKey] || config.panicKey; if (event.key === 'Escape' || event.key.toLowerCase() === String(key).toLowerCase()) { event.preventDefault(); stop(); } });
addEventListener('pagehide', clear);
window.firstShow = { init, setMedia, setError, stop, preview: async () => { await loadingMedia; if (config.preview) start(); }, getState: () => ({elapsed,running:clock.running,phase,objects:objects.length,particles:vfx.count,emiPose:bodyPose}) };
send({type:'ready'});
