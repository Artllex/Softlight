(() => {
  if (window.__softlightCleanup) window.__softlightCleanup();
  const ids = new WeakMap(); let nextId = 1, pending = false, scheduled = 0;
  let lastPayload = '', lastSent = 0, scanTime = -Infinity, videos = [];
  async function update() {
    if (pending || document.hidden) return;
    pending = true;
    try {
      let chosen = null, area = 0;
      const now = performance.now();
      if(now-scanTime>=500) { videos=Array.from(document.querySelectorAll('video'));scanTime=now; }
      for (const video of videos) {
        const style = getComputedStyle(video);
        if (style.visibility !== 'visible' || style.display === 'none' || Number(style.opacity) === 0) continue;
        const element = video.closest('.html5-video-player') || video;
        const r = element.getBoundingClientRect();
        const left = Math.max(0, r.left), top = Math.max(0, r.top);
        const right = Math.min(innerWidth, r.right), bottom = Math.min(innerHeight, r.bottom);
        const size = Math.max(0, right-left) * Math.max(0, bottom-top);
        if (size < 4096 || size <= area) continue;
        const hit = document.elementFromPoint((left+right)/2, (top+bottom)/2);
        if (!hit || !(element.contains(hit) || hit === element)) continue;
        if (!ids.has(video)) ids.set(video, nextId++);
        area = size; chosen = { id: ids.get(video), left, top, right, bottom };
      }
      const ratio = devicePixelRatio;
      const originX = window.mozInnerScreenX, originY = window.mozInnerScreenY;
      const message = { visible: !!chosen && Number.isFinite(originX) && Number.isFinite(originY) };
      if (message.visible) {
        message.id = chosen.id;
        for (const key of ['left','right']) message[key] = Math.round((originX+chosen[key])*ratio);
        for (const key of ['top','bottom']) message[key] = Math.round((originY+chosen[key])*ratio);
      }
      const payload=JSON.stringify(message);
      if(payload!==lastPayload || now-lastSent>=250) {
        await browser.runtime.sendMessage(message);
        lastPayload=payload;lastSent=now;
      }
    } catch {} finally { pending = false; }
  }
  function schedule() { if(!scheduled) scheduled=requestAnimationFrame(() => {scheduled=0;update();}); }
  const timer=setInterval(update, 100);
  addEventListener('scroll', schedule, {capture:true,passive:true});
  addEventListener('resize', schedule);
  document.addEventListener('visibilitychange', schedule);
  window.__softlightCleanup=() => {
    clearInterval(timer);cancelAnimationFrame(scheduled);
    removeEventListener('scroll',schedule,true);removeEventListener('resize',schedule);
    document.removeEventListener('visibilitychange',schedule);
  };
  update();
})();
