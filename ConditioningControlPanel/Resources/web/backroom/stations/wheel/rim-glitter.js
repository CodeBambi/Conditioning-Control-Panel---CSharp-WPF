import * as T from 'three';

/** One draw call, fixed storage, no new lights or per-frame allocations. Diamonds stay outside the prize labels. */
export function createRimGlitter(parent, origin, count = 112) {
  const geometry = new T.BufferGeometry();
  const positions = new Float32Array(count * 3), colors = new Float32Array(count * 3), sizes = new Float32Array(count);
  const palette = [0xff72cb, 0x64ffe0, 0xffcf6b, 0xaa81ff, 0x6bcfff].map(c => new T.Color(c));
  for (let i = 0; i < count; i++) palette[i % palette.length].toArray(colors, i * 3);
  geometry.setAttribute('position', new T.BufferAttribute(positions, 3).setUsage(T.DynamicDrawUsage));
  geometry.setAttribute('color', new T.BufferAttribute(colors, 3));
  geometry.setAttribute('sparkSize', new T.BufferAttribute(sizes, 1).setUsage(T.DynamicDrawUsage));
  const material = new T.ShaderMaterial({
    transparent: true, depthWrite: false, blending: T.AdditiveBlending, toneMapped: false,
    uniforms: { viewportHeight: { value: 720 } },
    vertexShader: `attribute vec3 color; attribute float sparkSize; varying vec3 tint;
      uniform float viewportHeight;
      void main(){ tint=color; vec4 p=modelViewMatrix*vec4(position,1.);
        gl_Position=projectionMatrix*p;
        gl_PointSize=clamp(sparkSize*viewportHeight*projectionMatrix[1][1]/max(.1,-p.z),0.,22.); }`,
    fragmentShader: `varying vec3 tint; void main(){vec2 p=abs(gl_PointCoord-.5)*2.;
      float edge=1.-smoothstep(.35,1.,p.x+p.y); if(edge<.015)discard;
      gl_FragColor=vec4(mix(tint,vec3(1.),edge*.45),edge*.85);}`,
  });
  const points = new T.Points(geometry, material); points.name = 'wheel_rim_diamonds';
  points.position.copy(origin); parent.add(points); points.frustumCulled = false;
  let clock = 0;
  return {
    update(dt, still, energy = 0, visible = true, viewportHeight = 720) {
      points.visible = visible && !still;
      if (!points.visible) return;
      clock += Math.min(.05, Math.max(0, dt)) * (.6 + energy * 1.4);
      material.uniforms.viewportHeight.value = viewportHeight;
      const alive = Math.round(count * (.36 + energy * .64)); geometry.setDrawRange(0, alive);
      for (let i = 0; i < alive; i++) {
        const life = (clock * (.17 + (i % 5) * .023) + i * .618034) % 1;
        const angle = i * 2.399963 + clock * (.22 + energy * .45);
        const radius = .79 + life * (.11 + energy * .16);
        positions[i*3] = Math.sin(angle) * radius;
        positions[i*3+1] = Math.cos(angle) * radius + life * life * .075;
        positions[i*3+2] = .13 + Math.sin(life*Math.PI)*.06;
        sizes[i] = (.018 + (i%4)*.004 + energy*.010) * Math.sin(life*Math.PI);
      }
      geometry.attributes.position.needsUpdate = true; geometry.attributes.sparkSize.needsUpdate = true;
    },
    dispose(){ points.removeFromParent(); geometry.dispose(); material.dispose(); },
  };
}
