/** Wheel feedback uses the room's articulated controller; the room owns its clock and disposal. */
import { FACES } from './emi.js';
export function createRoomEmi(controller, hud) {
  let face='idle0_0', mode='idle', still=false;
  const g=hud?.getContext('2d');
  return {
    get face(){return face;}, get mode(){return mode;},
    setFace(name){face=Object.hasOwn(FACES,name)?name:'idle0_0';controller?.setExpression(FACES[face]);},
    setMode(next){if(next===mode)return;mode=next;if(!still)controller?.trigger(next==='jackpot'?'cheer':next==='win'?'cheer':next==='sleepy'?'shrug':next==='spin'?'anticipation':'greet', {interrupt:true});},
    setReduced(on){still=!!on;if(still)controller?.settle();},
    skip(){controller?.settle();},
    update(){const img=controller?.root.getObjectByName('EMI_glass')?.material?.map?.image;if(g&&img){g.clearRect(0,0,hud.width,hud.height);g.drawImage(img,FACES[face]*152,0,152,137,0,0,hud.width,hud.height);}},
    dispose(){controller?.setExpression(null);controller?.settle();},
  };
}
