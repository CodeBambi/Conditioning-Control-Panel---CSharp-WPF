// One ending lifecycle for the station. Video is a rendered asset, not a second game loop.
export function createOfficeEnding(root, {reduced=false, actions, onReveal=()=>{}, make=tag=>document.createElement(tag)}={}) {
  const layer=make('div'),video=make('video'),still=make('img'),skip=make('button');
  layer.className='bo-office-ending';layer.hidden=true;
  video.className='bo-office-film';video.muted=true;video.playsInline=true;video.preload='none';video.tabIndex=-1;
  video.src=new URL('./assets/office-ending.mp4',import.meta.url).href;
  still.className='bo-office-still';still.alt='An empty grey office cubicle, with the finished game on its monitor.';still.hidden=true;
  still.src=new URL('./assets/office-ending.jpg',import.meta.url).href;
  skip.type='button';skip.className='bo-office-skip';skip.textContent='Skip';
  layer.append(video,still,skip);root.append(layer);
  let owner=null,state='idle',ready=false,disposed=false,startedAt=0,stillFailed=false;
  const removers=[];
  function listen(el,event,fn){el.addEventListener(event,fn);removers.push(()=>el.removeEventListener(event,fn));}
  function showActions(){actions.hidden=false;actions.querySelector('button')?.focus({preventScroll:true});}
  function finish(useStill=true){
    if(disposed||!owner||state==='idle'||state==='done')return;
    video.pause();state='done';layer.hidden=false;skip.hidden=true;
    if(useStill){still.hidden=stillFailed;video.hidden=true;if(stillFailed)layer.hidden=true;}
    showActions();
  }
  function reset(){
    video.pause();layer.hidden=true;still.hidden=true;video.hidden=false;skip.hidden=false;
    root.classList.remove('is-office-glitch');root.classList.remove('is-ending');actions.hidden=true;state='idle';ready=false;owner=null;
  }
  listen(skip,'click',()=>finish());
  listen(video,'playing',()=>{if(state==='starting'){state='playing';layer.hidden=false;skip.focus({preventScroll:true});}});
  listen(video,'ended',()=>{if(state==='playing')finish();});
  listen(video,'error',()=>{ready=false;if(['starting','playing'].includes(state))finish();});
  listen(video,'canplay',()=>{ready=true;});
  listen(still,'error',()=>{stillFailed=true;still.hidden=true;if(state==='done'){layer.hidden=true;showActions();}});
  return {
    update(finale){
      if(disposed)return;
      if(finale?.phase!=='outro'){if(owner)reset();return;}
      if(owner!==finale){reset();owner=finale;state='card';if(!reduced){video.preload='auto';video.load();}}
      const age=finale.outroAge||0;
      root.classList.toggle('is-ending',age>=3.8);
      root.classList.toggle('is-office-glitch',!reduced&&age>=7.05&&age<7.35&&state==='card');
      if(age>=7.35&&state==='card'){
        onReveal();root.classList.remove('is-office-glitch');
        if(reduced){finish();return;}
        if(ready||video.readyState>=2){
          state='starting';startedAt=age;
          try{const pending=video.play();pending?.catch(()=>{if(!disposed&&state==='starting')finish();});}catch{finish();}
        }else if(age>=10){finish();}
      }
      if((state==='starting'&&age-startedAt>3)||(state==='playing'&&age-startedAt>14))finish();
    },
    skip(){if(owner)finish();},
    suspend(value){if(state!=='playing')return;if(value)video.pause();else video.play()?.catch(()=>finish());},
    get state(){return state;},
    dispose(){disposed=true;video.pause();for(const remove of removers)remove();video.removeAttribute('src');video.load();layer.remove();},
  };
}
