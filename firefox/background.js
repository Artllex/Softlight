let port = null, busy = false, identity = '', generation = 1;
function connect() {
  if (port) return;
  port = browser.runtime.connectNative('softlight_firefox');
  port.onDisconnect.addListener(disconnected => { const error=disconnected.error && disconnected.error.message || 'Native host disconnected'; console.error('Softlight:',error); port = null; busy = false; browser.browserAction.setBadgeText({text:'!'}); browser.browserAction.setTitle({title:'Softlight: '+error}); });
  port.onMessage.addListener(reply => { busy = false; browser.browserAction.setBadgeText({text: reply.connected ? 'ON' : '!'}); });
}
async function injectOpenTabs() {
  for(const tab of await browser.tabs.query({})) {
    if(/^https?:/.test(tab.url || '')) try {await browser.tabs.executeScript(tab.id,{file:'content.js'});}catch{}
  }
}
browser.browserAction.onClicked.addListener(() => {connect();injectOpenTabs();});
setInterval(() => {try {connect();}catch{}},1000);
browser.runtime.onMessage.addListener(async (message, sender) => {
  if (!sender.tab || sender.frameId !== 0 || busy) return;
  const tab = sender.tab;
  if (!tab.active) return;
  const nextIdentity = `${tab.id}:${sender.documentId || sender.url}:${message.id || 0}`;
  if (nextIdentity !== identity) { identity = nextIdentity; generation = (generation+1) % 2147483647; }
  const payload = {visible: message.visible === true, generation, title: (tab.title || '').slice(0,512)};
  for (const key of ['left','top','right','bottom']) {
    const value = message[key];
    if (payload.visible && (!Number.isInteger(value) || Math.abs(value)>100000)) return;
    if (payload.visible) payload[key] = value;
  }
  try { connect(); busy = true; port.postMessage(payload); } catch { busy = false; }
});
browser.tabs.onActivated.addListener(() => { identity=''; if(port && !busy) {busy=true;port.postMessage({visible:false,generation:++generation});} });

connect();
injectOpenTabs();
