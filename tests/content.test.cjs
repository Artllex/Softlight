const assert=require('node:assert/strict'),vm=require('node:vm'),fs=require('node:fs');
const code=fs.readFileSync(require('node:path').join(__dirname,'../firefox/content.js'),'utf8');
async function run(rect, hidden=false) {
  const sent=[];
  const video={closest:()=>null,getBoundingClientRect:()=>rect,contains:x=>x===video};
  const context={WeakMap,Number,Math,performance:{now:()=>1000},document:{hidden,querySelectorAll:()=>[video],elementFromPoint:()=>video,addEventListener(){}},
    getComputedStyle:()=>({visibility:'visible',display:'block',opacity:'1'}),innerWidth:800,innerHeight:600,devicePixelRatio:2.5,
    window:{mozInnerScreenX:10,mozInnerScreenY:100},browser:{runtime:{sendMessage:async m=>sent.push(m)}},setInterval(){},addEventListener(){}};
  vm.runInNewContext(code,context);await new Promise(resolve=>setImmediate(resolve));return sent;
}
(async()=>{
  let messages=await run({left:-20,top:20,right:640,bottom:380});
  assert.equal(messages[0].left,25);assert.equal(messages[0].top,300);assert.equal(messages[0].right,1625);assert.equal(messages[0].bottom,1200);
  assert.equal((await run({left:900,top:20,right:1000,bottom:380}))[0].visible,false);
  assert.equal((await run({left:0,top:0,right:600,bottom:400},true)).length,0);
  console.log('PASS: DPI coordinates, viewport clipping, offscreen removal and hidden tab suppression.');
})().catch(error=>{console.error(error);process.exitCode=1});
