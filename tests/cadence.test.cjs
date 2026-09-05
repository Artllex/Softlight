const assert=require('node:assert/strict'),vm=require('node:vm'),fs=require('node:fs');
const code=fs.readFileSync(require('node:path').join(__dirname,'../firefox/content.js'),'utf8');
(async()=>{
 let now=0, scans=0, interval, frame, scroll, top=10, present=true, stopped=0;const sent=[];
 const video={closest:()=>null,getBoundingClientRect:()=>({left:10,top,right:610,bottom:top+300}),contains:x=>x===video};
 const ctx={performance:{now:()=>now},document:{hidden:false,querySelectorAll:()=>{scans++;return present?[video]:[]},elementFromPoint:()=>video,addEventListener(){},removeEventListener(){}},
 getComputedStyle:()=>({visibility:'visible',display:'block',opacity:'1'}),innerWidth:800,innerHeight:600,devicePixelRatio:1,
 window:{mozInnerScreenX:0,mozInnerScreenY:0},browser:{runtime:{sendMessage:async m=>sent.push(m)}},setInterval:f=>(interval=f,1),clearInterval:()=>stopped++,
 requestAnimationFrame:f=>(frame=f,1),cancelAnimationFrame(){},addEventListener:(name,f)=>{if(name==='scroll')scroll=f},removeEventListener(){}};
 const flush=()=>new Promise(r=>setImmediate(r));
 vm.runInNewContext(code,ctx);await flush();
 for(now=100;now<=1000;now+=100){interval();await flush()}
 assert.equal(sent.length,4,'steady geometry should only send a heartbeat');assert.equal(scans,3);
 for(let i=0;i<20;i++)scroll();top=30;frame();await flush();assert.equal(sent.length,5,'scroll burst coalesced into one frame');assert.equal(sent[4].top,30);
 present=false;now=1600;interval();await flush();assert.equal(sent.at(-1).visible,false,'removing player clears region');
 vm.runInNewContext(code,ctx);await flush();assert.equal(stopped,1,'reinjection cleans up old timer');
 console.log('PASS: heartbeat reduction, one update per scroll frame, removed player and reinjection cleanup');
})().catch(e=>{console.error(e);process.exitCode=1});
