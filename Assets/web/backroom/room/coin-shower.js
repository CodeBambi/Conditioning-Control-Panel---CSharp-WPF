import * as T from '../../vendor/three/three.module.min.js';

/** Cosmetic coins, released only after a paid result has been revealed. */
export function createCoinShower(parent) {
  const geometry = new T.CylinderGeometry(.028,.028,.007,16);
  const material = new T.MeshStandardMaterial({color:0xf6b52e,metalness:.7,roughness:.3,transparent:true});
  const coins = new T.InstancedMesh(geometry,material,64); coins.name='payout_coins';coins.count=0;
  coins.frustumCulled=false;parent.add(coins);
  const pose=new T.Object3D();let age=0,count=0,label='',active=false;
  return {
    start(amount,tier,text='') {
      if(!Number.isFinite(amount)||amount<=0)return false;
      count=[0,7,16,32,64][Math.max(1,Math.min(4,Math.floor(tier)||1))];
      age=0;active=true;label=text;material.opacity=1;return true;
    },
    update(dt,still=false) {
      if(!active)return;
      age+=Math.min(.05,Math.max(0,dt));
      if(age>=4){active=false;coins.count=0;return;}
      coins.count=still?0:count;material.opacity=Math.min(1,(4-age)/.6);
      for(let i=0;i<coins.count;i++) {
        const t=age-i/count*1.8,x=((i*37%101)/100-.5)*.42,z=.45+((i*19%53)/52-.5)*.16;
        const fall=.42,land=.276+(i%3)*.007;
        pose.position.set(x,t<0?.40:t<fall?.40-(.40-land)*(t/fall)**2:land+Math.abs(Math.sin((t-fall)*14))*.035*Math.exp(-(t-fall)*5),z);
        pose.rotation.set(t<fall?t*9:0,i*2.4,t<fall?t*7:0);pose.scale.setScalar(t<0?0:1);pose.updateMatrix();coins.setMatrixAt(i,pose.matrix);
      }
      coins.instanceMatrix.needsUpdate=true;
    },
    clear(){active=false;coins.count=0;},
    debug:()=>({active,age,count,label}),
    dispose(){coins.removeFromParent();geometry.dispose();material.dispose();}
  };
}
