import * as T from 'three';

// Linear colour values keep the floor rich without washing out the furniture.
const PALETTES = [
  [0xff269a, 0x762fff, 0x04d6c5],
  [0x12bf9a, 0x107eff, 0x96dc57],
  [0xf46a28, 0xe32892, 0x8c42e8],
];

export function createFloorStyle() {
  const uniforms = {
    angle: { value: 0 }, colorPhase: { value: 0 },
    design: { value: 0 },
    inkA: { value: new T.Color() }, inkB: { value: new T.Color() }, inkC: { value: new T.Color() },
  };
  const material = new T.ShaderMaterial({
    uniforms, side: T.DoubleSide,
    vertexShader: `varying vec2 p;
void main(){p=position.xy/7.;gl_Position=projectionMatrix*modelViewMatrix*vec4(position,1.);}`,
    fragmentShader: `uniform float angle,colorPhase,design;
uniform vec3 inkA,inkB,inkC;varying vec2 p;
vec3 ink(float phase){
  float h=mod(phase,3.);float blend=smoothstep(0.,1.,fract(h));
  if(h<1.)return mix(inkA,inkB,blend);
  if(h<2.)return mix(inkB,inkC,blend);
  return mix(inkC,inkA,blend);
}
void main(){
  float r=length(p), a=atan(p.y,p.x);
  vec3 base=vec3(.018,.006,.044), col=base;
  if(design<.5){
    // Broad velvet arms breathe slowly while colour travels towards the centre.
    float breathing=1.+.035*sin(colorPhase*1.7);
    float wave=pow(.5+.5*sin(3.*(a+angle)-r*20.*breathing),2.2);
    col=mix(base,ink(r*1.8+colorPhase)*.42,wave);
  }else if(design<1.5){
    // Three continuous curved ribbons, each with its own trailing colour crest.
    float winding=a+angle*.58-r*5.2;
    for(int i=0;i<3;i++){
      float n=float(i);
      float d=abs(atan(sin(winding-n*2.094395),cos(winding-n*2.094395)));
      float body=1.-smoothstep(.20,.38,d);
      float edge=(1.-smoothstep(.025,.07,abs(d-.27)))*.13;
      float trail=pow(.5+.5*cos(r*9.+angle*2.-n*1.8),3.);
      col+=ink(n+colorPhase+r*.55)*(body*(.19+.27*trail)+edge);
    }
  }else{
    // Curved diamond petals open and close in repeating radial tiers.
    float theta=a+angle*.22;
    float petals=abs(sin(theta*6.+r*.9));
    float radial=abs(sin(r*8.5-.16*sin(colorPhase*1.5)));
    float diamond=petals*.68+radial*.68;
    float fill=1.-smoothstep(.55,.83,diamond);
    float rim=1.-smoothstep(.025,.075,abs(diamond-.64));
    float sheen=.78+.22*sin(r*5.+colorPhase*1.4);
    col+=ink(r*1.4+colorPhase*.55+petals*.3)*(fill*.29+rim*.17)*sheen;
  }
  // A quiet woven finish that stays stable in world space.
  float weave=.5+.5*sin(p.x*640.)*sin(p.y*640.);
  col+=weave*.002;
  gl_FragColor=vec4(col,1.);
  #include <colorspace_fragment>
}`,
  });
  let design = 0, palette = 0, time = 0;
  function getFloorStyle() { return { design, palette }; }
  function setFloorStyle(nextDesign, nextPalette) {
    if (!Number.isInteger(nextDesign) || nextDesign < 0 || nextDesign > 2 ||
        !Number.isInteger(nextPalette) || nextPalette < 0 || nextPalette > 2) return false;
    design = nextDesign; palette = nextPalette;
    uniforms.design.value = design;
    ['inkA', 'inkB', 'inkC'].forEach((key, i) => uniforms[key].value.setHex(PALETTES[palette][i]));
    return true;
  }
  setFloorStyle(0, 0);
  return { material, setFloorStyle, getFloorStyle,
    update(dt, still) {
      if (!still && Number.isFinite(dt)) time += Math.max(0, Math.min(dt, .1));
      uniforms.angle.value = time * .36;
      uniforms.colorPhase.value = time * .22;
    },
  };
}
