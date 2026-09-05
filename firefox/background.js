let port=null, busy=false, pending=null, generation=Date.now()%1000000000;
const windows=new Map(), geometry=new Map();
function flush() {
  if(busy || !pending)return;
  try {connect();const message=pending;pending=null;busy=true;port.postMessage(message);}
  catch {busy=false;}
}
function post(message) {message.sentAt=Date.now();pending=message;flush();}
function connect() {
  if(port)return;
  port=browser.runtime.connectNative('softlight_firefox');
  port.onDisconnect.addListener(disconnected=>{
    const error=disconnected.error && disconnected.error.message || 'Native host disconnected';
    console.error('Softlight:',error);port=null;busy=false;
    browser.browserAction.setBadgeText({text:'!'});
    browser.browserAction.setTitle({title:'Softlight: '+error});
  });
  port.onMessage.addListener(reply=>{
    busy=false;browser.browserAction.setBadgeText({text:reply.connected?'ON':'!'});flush();
  });
}
function context(tab,changedAt=Date.now()) {
  let state=windows.get(tab.windowId);
  const key=`${tab.id}:${tab.url || ''}`;
  if(!state || state.key!==key) {
    state={key,tabId:tab.id,generation:++generation,changedAt};windows.set(tab.windowId,state);
  }
  return {windowId:tab.windowId,generation:state.generation,changedAt:state.changedAt,title:(tab.title || '').slice(0,512)};
}
async function injectOpenTabs() {
  for(const tab of await browser.tabs.query({})) {
    if(/^https?:/.test(tab.url || ''))try {await browser.tabs.executeScript(tab.id,{file:'content.js'});}catch{}
  }
}
browser.browserAction.onClicked.addListener(()=>{connect();injectOpenTabs();});
setInterval(()=>{try {connect();flush();}catch{}},1000);
browser.runtime.onMessage.addListener((message,sender)=>{
  if(!sender.tab || sender.frameId!==0)return;
  const shape={visible:message.visible===true};
  for(const key of ['left','top','right','bottom']) {
    const value=message[key];
    if(shape.visible && (!Number.isInteger(value) || Math.abs(value)>100000))return;
    if(shape.visible)shape[key]=value;
  }
  geometry.set(sender.tab.id,{shape,url:sender.tab.url,title:sender.tab.title,time:Date.now()});
  const state=windows.get(sender.tab.windowId);
  // onActivated has already selected the new tab; ignore a queued old message.
  if(!sender.tab.active || (state && state.tabId!==sender.tab.id))return;
  post({...shape,...context(sender.tab)});
});
let activation=0;
browser.tabs.onActivated.addListener(async info=>{
  const request=++activation,changedAt=Date.now();
  const previous=windows.get(info.windowId);
  const cached=geometry.get(info.tabId);
  // Known tabs can publish the context AND player geometry in the same message,
  // before waiting for tabs.get or a new content-script scan.
  if(cached) {
    const tab={id:info.tabId,windowId:info.windowId,url:cached.url,title:cached.title};
    const meta=context(tab,changedAt);windows.get(info.windowId).activating=true;
    post({...cached.shape,...meta});
  } else if(!previous || previous.tabId!==info.tabId) {
    windows.set(info.windowId,{tabId:info.tabId,activating:true});
  }
  try {
    const tab=await browser.tabs.get(info.tabId);
    if(request!==activation || !tab.active)return;
    const meta=context(tab,changedAt);
    const state=windows.get(info.windowId);state.activating=false;
    if(!cached)post({pending:true,visible:false,...meta});
    // Wake the content script immediately; do not wait for its heartbeat.
    try {await browser.tabs.sendMessage(tab.id,{softlight:'refresh'});}catch{}
  }catch{}
});
browser.tabs.onRemoved.addListener(id=>geometry.delete(id));
connect();injectOpenTabs();
