// Keep Loom-to-CPU transfers at recipe changes, not every animation frame.
export function createSoftwareFields(makeCanvas=()=>document.createElement('canvas')) {
  const fields=new Map();
  return {
    draw(kit,ctx,name,d,rot,alpha=1){
      let canvas=fields.get(name);
      if(!canvas){
        canvas=makeCanvas();canvas.width=canvas.height=128;
        const target=canvas.getContext('2d',{willReadFrequently:true});
        if(!kit.draw(target,name,0,0,128,128,{angle:0,backing:128}))return false;
        if(fields.size>=16)fields.delete(fields.keys().next().value);
        fields.set(name,canvas);
      }
      ctx.save();ctx.rotate(rot);ctx.globalAlpha*=alpha;
      ctx.drawImage(canvas,-d/2,-d/2,d,d);ctx.restore();return true;
    },
    invalidate(name){fields.delete(name);},
    clear(){fields.clear();}
  };
}
