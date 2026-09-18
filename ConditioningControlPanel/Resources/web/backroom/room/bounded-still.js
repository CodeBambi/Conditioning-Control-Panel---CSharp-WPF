import {checkDimensions,MediaLimitError} from './media-limits.js';
// Reuse already validated bytes. Never start a second transfer for still fallback.
export function boundedStill(data,type,maxEdge,signal) {
  return new Promise((resolve,reject)=>{
    if(signal.aborted){reject(new DOMException('Media load cancelled','AbortError'));return;}
    if(typeof Image==='undefined'){reject(new MediaLimitError('Still decoder unavailable','transfer'));return;}
    const img=new Image(),url=URL.createObjectURL(new Blob([data],{type}));
    const clean=()=>{img.onload=img.onerror=null;img.removeAttribute('src');URL.revokeObjectURL(url);signal.removeEventListener('abort',abort);};
    const fail=error=>{clean();reject(error?.name==='AbortError'||error?.name==='MediaLimitError'?error:new MediaLimitError('Still decoding failed','transfer'));};
    const abort=()=>fail(new DOMException('Media load cancelled','AbortError'));
    signal.addEventListener('abort',abort,{once:true});
    img.onload=()=>{
      try {
        checkDimensions(img.naturalWidth,img.naturalHeight);
        const k=Math.min(1,maxEdge/Math.max(img.naturalWidth,img.naturalHeight)),canvas=document.createElement('canvas');
        canvas.width=Math.max(1,Math.round(img.naturalWidth*k));canvas.height=Math.max(1,Math.round(img.naturalHeight*k));
        canvas.getContext('2d').drawImage(img,0,0,canvas.width,canvas.height);clean();
        resolve({canvas,byteLength:0,animated:false,frames:0,index:0,tick:()=>false,dispose(){canvas.width=canvas.height=1;}});
      } catch(error){fail(error);}
    };
    img.onerror=()=>fail(new MediaLimitError('Still decoding failed','transfer'));
    img.decoding='async';img.src=url;
  });
}
