// npm install in this folder; node compress-customization.mjs ORIGINAL_DIR OUTPUT_DIR
// Always use the authored originals, never repeatedly simplify shipped outputs.
import {NodeIO} from '@gltf-transform/core';
import {ALL_EXTENSIONS} from '@gltf-transform/extensions';
import {dedup,weld,simplify,meshopt} from '@gltf-transform/functions';
import {MeshoptEncoder,MeshoptSimplifier} from 'meshoptimizer';
import {mkdir} from 'node:fs/promises';
import {resolve,join} from 'node:path';
const [input,output]=process.argv.slice(2);
if(!input||!output||resolve(input)===resolve(output))throw new Error('Use separate original and output directories');
await mkdir(output,{recursive:true});
await MeshoptEncoder.ready;await MeshoptSimplifier.ready;
const io=new NodeIO().registerExtensions(ALL_EXTENSIONS).registerDependencies({'meshopt.encoder':MeshoptEncoder});
for(const name of ['vending','knight','queen','rook']){
  const doc=await io.read(join(input,name+'.glb'));
  await doc.transform(dedup(),weld(),simplify({simplifier:MeshoptSimplifier,ratio:.5,error:.0001}),meshopt({encoder:MeshoptEncoder,level:'medium'}));
  await io.write(join(output,name+'.glb'),doc);
}
