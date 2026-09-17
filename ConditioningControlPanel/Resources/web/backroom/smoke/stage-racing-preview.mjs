/** Stage the public race into the existing local casino preview. Never copies server code. */
import {cp,readFile,writeFile,mkdir,readdir,stat} from 'node:fs/promises';
import {resolve,dirname,join,extname} from 'node:path';
import {fileURLToPath} from 'node:url';
const here=dirname(fileURLToPath(import.meta.url));
const source=resolve(here,'../../dtrh');
const target=process.argv[2] && resolve(process.argv[2]);
if(!target) throw new Error('Usage: node stage-racing-preview.mjs <combined-preview-root>');
await stat(join(target,'backroom','room','main.js'));
if(target===resolve(source,'..')) throw new Error('Choose a preview directory, not the source web tree');
const destination=join(target,'backroom','racing');
await mkdir(destination,{recursive:true});
const filter=p=>!['smoke'].includes(p.split(/[\\/]/).at(-1))&&!['.md','.mjs'].includes(extname(p));
for(const name of ['race','engine','game','shared','vendor','chart'])
  await cp(join(source,name),join(destination,name),{recursive:true,filter});
for(const name of ['bubbles','items'])
  await cp(join(source,'assets',name),join(destination,'assets',name),{recursive:true});
for(const file of await readdir(source,{withFileTypes:true}))
  if(file.isFile()&&['.js','.css','.html','.json'].includes(extname(file.name)))
    await cp(join(source,file.name),join(destination,file.name));
async function rewrite(directory){
  for(const file of await readdir(directory,{withFileTypes:true})){
    const path=join(directory,file.name);
    if(file.isDirectory())await rewrite(path);
    else if(['.js','.css','.html','.json'].includes(extname(file.name)))
      await writeFile(path,(await readFile(path,'utf8')).replaceAll('/dtrh/','/backroom/racing/'),'utf8');
  }
}
await rewrite(destination);
const htmlPath=join(destination,'race.html');
let html=(await readFile(htmlPath,'utf8')).replace('src="./raceBoot.js"','src="./preview-host.js"');
html=html.replace('<body>','<body><a id="preview-return" href="/backroom/index.html?raceReturn=1" style="position:fixed;z-index:99;top:16px;right:16px;padding:10px 16px;background:#211421;color:#ffb6d9;border:1px solid #ffb6d9;border-radius:9px;font:14px sans-serif">back to casino</a>');
await writeFile(htmlPath,html,'utf8');
await cp(join(here,'racing-preview-host.js'),join(destination,'preview-host.js'));
await cp(join(here,'../room/race-portal.js'),join(target,'backroom/room/race-portal.js'));
console.log('Public race staged at '+destination+'. Casino room hooks must already be applied.');
