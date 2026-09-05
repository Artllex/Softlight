const vm=require('node:vm'),fs=require('node:fs'),assert=require('node:assert/strict'),path=require('node:path');
(async()=>{
  let ack,activate,receive,remove;const sent=[],refresh=[];
  const a={id:1,windowId:7,url:'https://test/old',title:'Old',active:true};
  const b={id:2,windowId:7,url:'https://test/new',title:'New tab',active:true};
  const tabs=new Map([[1,a],[2,b]]);
  const port={onDisconnect:{addListener(){}},onMessage:{addListener:f=>ack=f},postMessage:m=>sent.push(m)};
  const browser={runtime:{connectNative:()=>port,onMessage:{addListener:f=>receive=f}},
    browserAction:{onClicked:{addListener(){}},setBadgeText(){},setTitle(){}},
    tabs:{query:async()=>[],get:async id=>tabs.get(id),sendMessage:async(id,m)=>refresh.push(id),
      onActivated:{addListener:f=>activate=f},onRemoved:{addListener:f=>remove=f}}};
  vm.runInNewContext(fs.readFileSync(path.join(__dirname,'../firefox/background.js'),'utf8'),{browser,setInterval(){},console});
  receive({visible:false},{tab:a,frameId:0});
  await activate({tabId:2,windowId:7});assert.equal(sent.length,1);ack({connected:true});
  assert.equal(sent[1].pending,true);assert.equal(sent[1].title,'New tab');const generation=sent[1].generation;
  receive({visible:true,left:1,top:2,right:100,bottom:100},{tab:b,frameId:0});ack({connected:true});
  assert.equal(sent[2].generation,generation);assert.equal(sent[2].visible,true);ack({connected:true});
  await activate({tabId:1,windowId:7});ack({connected:true});
  const start=sent.length;
  const activation=activate({tabId:2,windowId:7});
  assert.equal(sent.length,start+1,'cached geometry sent synchronously before tabs.get resolves');
  assert.equal(sent.at(-1).visible,true);assert.equal(sent.at(-1).left,1);
  assert.ok(sent.at(-1).changedAt<=sent.at(-1).sentAt);
  const returningGeneration=sent.at(-1).generation;
  await activation;ack({connected:true});
  receive({visible:false},{tab:a,frameId:0});
  assert.equal(sent.at(-1).generation,returningGeneration,'old active message cannot switch context back');
  receive({visible:true,left:NaN,top:2,right:100,bottom:100},{tab:b,frameId:0});
  assert.equal(sent.length,start+1,'invalid geometry never reaches native host');
  assert.ok(refresh.includes(2),'activation wakes the content script immediately');
  remove(2);
  console.log('PASS: bounded queue, activation time, immediate cached geometry, stable generation, stale message rejection and validation');
})().catch(e=>{console.error(e);process.exitCode=1});
