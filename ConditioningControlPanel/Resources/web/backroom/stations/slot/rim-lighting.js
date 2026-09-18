import * as T from 'three';

// One draw for the rim halos, two shadowless lights for colour on nearby lacquer.
export function createRimLighting(scene, bulbs, camera, renderer) {
  const count = bulbs.length * 5; // One halo and four drifting sparks per lamp.
  const positions = new Float32Array(count * 3), colors = new Float32Array(count * 3);
  const sizes = new Float32Array(count), geometry = new T.BufferGeometry();
  geometry.setAttribute('position', new T.BufferAttribute(positions, 3).setUsage(T.DynamicDrawUsage));
  geometry.setAttribute('color', new T.BufferAttribute(colors, 3).setUsage(T.DynamicDrawUsage));
  geometry.setAttribute('diameter', new T.BufferAttribute(sizes, 1).setUsage(T.DynamicDrawUsage));
  const material = new T.ShaderMaterial({
    transparent: true, depthWrite: false, blending: T.AdditiveBlending, vertexColors: true,
    uniforms: { pixelScale: { value: 400 } },
    vertexShader: `attribute float diameter;uniform float pixelScale;varying vec3 tint;
      void main(){tint=color;vec4 p=modelViewMatrix*vec4(position,1.);gl_Position=projectionMatrix*p;
      gl_PointSize=diameter*pixelScale/max(.05,-p.z);}`,
    fragmentShader: `varying vec3 tint;void main(){float r=length(gl_PointCoord-.5)*2.;
      if(r>1.)discard;float a=.28*pow(1.-r,2.);gl_FragColor=vec4(tint,a);}`,
  });
  const points = new T.Points(geometry, material);
  points.name = 'slot_rim_glow'; points.frustumCulled = false; points.renderOrder = 2;
  points.raycast = () => {};
  const lamps = [new T.PointLight(0xff64cf, .45, 1.4, 2), new T.PointLight(0x59dfff, .45, 1.4, 2)];
  lamps.forEach((l, i) => { l.name = 'slot_rim_spill_' + i; l.castShadow = false; });
  scene.add(points, ...lamps);
  const p = new T.Vector3(), scale = new T.Vector3(), screen = new T.Vector2();
  const sums = [new T.Vector3(), new T.Vector3()], hues = [new T.Color(), new T.Color()];
  const counts = [0, 0], forward = new T.Vector3();
  let disposed = false;
  return {
    update(now = 0, still = false) {
      if (disposed) return;
      renderer.getDrawingBufferSize(screen);
      material.uniforms.pixelScale.value = screen.y * camera.projectionMatrix.elements[5] * .5;
      sums.forEach(s => s.set(0,0,0)); hues.forEach(c => c.setRGB(0,0,0)); counts.fill(0);
      bulbs.forEach((b, i) => {
        b.updateWorldMatrix(true, false); b.getWorldPosition(p); b.getWorldScale(scale);
        positions.set(p.toArray(), i * 3);
        colors.set(b.material.color.toArray().map(v => v * Math.min(1.4, .55 + b.material.emissiveIntensity)), i * 3);
        if (!b.geometry.boundingSphere) b.geometry.computeBoundingSphere();
        sizes[i] = b.geometry.boundingSphere.radius * Math.max(scale.x, scale.y, scale.z) * 5;
        for (let j=0; j<4; j++) {
          const n=bulbs.length+i*4+j, age=(now*.00032+i*.173+j*.25)%1;
          const angle=i*2.399+j*1.7;
          positions[n*3]=p.x+Math.cos(angle)*age*.07;
          positions[n*3+1]=p.y+age*.11;
          positions[n*3+2]=p.z+.015+Math.sin(angle)*age*.035;
          const fade=Math.sin(age*Math.PI)*4.5;
          for(let k=0;k<3;k++) colors[n*3+k]=colors[i*3+k]*fade;
          sizes[n]=still ? 0 : .020*(1-age*.6);
        }
        const side = b.position.x < 0 ? 0 : 1;
        sums[side].add(p); if (counts[side] === 0) hues[side].copy(b.material.color); counts[side]++;
      });
      if (bulbs.length) {
        const parent = bulbs[0].parent;
        forward.set(0,0,1).transformDirection(parent.matrixWorld).multiplyScalar(.12);
        lamps.forEach((l, i) => {
          l.visible = counts[i] > 0;
          if (counts[i]) { l.position.copy(sums[i]).multiplyScalar(1/counts[i]).add(forward); l.color.copy(hues[i]); }
        });
      }
      for (const name of ['position', 'color', 'diameter']) geometry.attributes[name].needsUpdate = true;
    },
    dispose() { if (disposed) return; disposed = true; scene.remove(points, ...lamps); geometry.dispose(); material.dispose(); },
  };
}
