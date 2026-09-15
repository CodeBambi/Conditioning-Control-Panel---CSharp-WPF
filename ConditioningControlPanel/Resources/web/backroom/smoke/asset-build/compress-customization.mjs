// npm install in this folder; node compress-customization.mjs ORIGINAL_DIR OUTPUT_DIR [--no-simplify]
// Every .glb in ORIGINAL_DIR is rebuilt. --no-simplify packs without decimating, for a model whose
// flat media planes have to keep their exact UVs (the gallery frames).
// Always use the authored originals, never repeatedly simplify shipped outputs.
import {NodeIO} from '@gltf-transform/core';
import {ALL_EXTENSIONS} from '@gltf-transform/extensions';
import {dedup,weld,simplify,meshopt} from '@gltf-transform/functions';
import {MeshoptEncoder,MeshoptSimplifier} from 'meshoptimizer';
import {mkdir,readdir} from 'node:fs/promises';
import {resolve,join} from 'node:path';
const [input,output]=process.argv.slice(2);
const decimate=!process.argv.includes('--no-simplify');
if(!input||!output||resolve(input)===resolve(output))throw new Error('Use separate original and output directories');
await mkdir(output,{recursive:true});
await MeshoptEncoder.ready;await MeshoptSimplifier.ready;
const io=new NodeIO().registerExtensions(ALL_EXTENSIONS).registerDependencies({'meshopt.encoder':MeshoptEncoder});
const names=(await readdir(input)).filter(n=>n.endsWith('.glb')).map(n=>n.slice(0,-4)).sort();
if(!names.length)throw new Error('No .glb in '+input);
for(const name of names){
  const doc=await io.read(join(input,name+'.glb'));
  const steps=[dedup(),weld()];
  if(decimate)steps.push(simplify({simplifier:MeshoptSimplifier,ratio:.5,error:.0001}));
  steps.push(meshopt({encoder:MeshoptEncoder,level:'medium'}));
  await doc.transform(...steps);
  await io.write(join(output,name+'.glb'),doc);
}
