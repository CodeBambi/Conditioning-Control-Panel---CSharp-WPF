// Shared flexible spine: skin, jewellery, outline, depth and CPU clearance agree.
import * as THREE from 'three';

export const SILICONE_GLSL = `
uniform vec2 uLag;
uniform vec2 uFlex;
uniform float uTwist;
uniform float uBulge;
vec3 siliconeFrame(vec3 p, float h) {
  float a = uTwist * h * h;
  p.xz = mat2(cos(a), sin(a), -sin(a), cos(a)) * p.xz;
  float lagSlope = sin(3.14159265 * h) + 3.14159265 * h * cos(3.14159265 * h);
  vec2 slope = 2.0 * h * uBend + uLag * lagSlope;
  vec3 tangent = normalize(vec3(slope.x, uHeight * (1.0 + uFlex.x) + 2.0 * h * uFlex.y, slope.y));
  vec3 axis = vec3(tangent.z, 0.0, -tangent.x);
  return p + cross(axis, p) + cross(axis, cross(axis, p)) / max(0.02, 1.0 + tangent.y);
}
vec3 siliconePoint(vec3 p) {
  float h = clamp(p.y / uHeight, 0.0, 1.0);
  vec2 off = uBend * h * h + uLag * (sin(3.14159265 * h) * h);
  vec3 centre = vec3(off.x, p.y * (1.0 + uFlex.x) + uFlex.y * h * h, off.y);
  float width = inversesqrt(max(0.45, 1.0 + uFlex.x)) * (1.0 + uBulge * smoothstep(0.50, 0.85, h));
  return centre + siliconeFrame(vec3(p.x * width, 0.0, p.z * width), h);
}
`;

const tangent = new THREE.Vector3(), axis = new THREE.Vector3(), cross = new THREE.Vector3(), twice = new THREE.Vector3();
export function siliconePoint(p, height, act, out = new THREE.Vector3()) {
  const h = Math.max(0, Math.min(1, p.y / height)), stretch = act.stretch || 0, drop = act.drop || 0;
  const lx = act.lx || 0, lz = act.lz || 0, a = (act.twist || 0) * h * h;
  const crown = Math.max(0, Math.min(1, (h - .50) / .35));
  const width = (1 + (act.bulge || 0) * crown * crown * (3 - 2 * crown)) / Math.sqrt(Math.max(.45, 1 + stretch));
  out.set((p.x * Math.cos(a) - p.z * Math.sin(a)) * width, 0, (p.x * Math.sin(a) + p.z * Math.cos(a)) * width);
  const ls = Math.sin(Math.PI * h) + Math.PI * h * Math.cos(Math.PI * h);
  tangent.set(2 * h * act.x + lx * ls, height * (1 + stretch) + 2 * h * drop, 2 * h * act.z + lz * ls).normalize();
  axis.set(tangent.z, 0, -tangent.x);
  cross.crossVectors(axis, out); twice.crossVectors(axis, cross).divideScalar(Math.max(.02, 1 + tangent.y));
  out.add(cross).add(twice);
  out.x += act.x * h * h + lx * Math.sin(Math.PI * h) * h;
  out.y += p.y * (1 + stretch) + drop * h * h;
  out.z += act.z * h * h + lz * Math.sin(Math.PI * h) * h;
  return out;
}
