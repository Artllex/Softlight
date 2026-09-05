#pragma once
// All bridge updates and frame-aligned snapshots share one lock.
namespace BrowserContext {
    static std::mutex playerMutex;
    static HWND playerWindow=nullptr;
    static RECT playerRect{},playerWindowRect{};
    static ULONGLONG playerSeen=0;
    static unsigned playerGeneration=0;
    static std::map<HWND,int> browserContexts,announcedContexts;
    struct BrowserSnapshot {
        double time; HWND window;RECT rect,windowRect;unsigned generation;std::map<HWND,int> contexts;ULONGLONG seenAt=0;
    };
    static std::deque<BrowserSnapshot> browserHistory;
    static void RememberBrowser(double time) {
        browserHistory.push_back({time,playerWindow,playerRect,playerWindowRect,playerGeneration,browserContexts});
        while(browserHistory.size()>64)browserHistory.pop_front();
    }
    static void Update(HWND window,int generation,double changedAt,int pending,int visible,int left,int top,int right,int bottom) {
        if(!window)return;
        std::lock_guard<std::mutex> lock(playerMutex);
        for(auto i=browserContexts.begin();i!=browserContexts.end();) {if(!IsWindow(i->first)){announcedContexts.erase(i->first);i=browserContexts.erase(i);}else ++i;}
        const auto old=announcedContexts.find(window);
        if(old==announcedContexts.end() || old->second!=generation)GraphTimeline::Boundary(window,GraphTimeline::EventTime(changedAt));
        announcedContexts[window]=generation;
        if(pending)return; // Keep the previous mask until this tab's geometry is known.
        bool contextChanged=browserContexts[window]!=generation;
        bool changed=contextChanged || playerWindow!=(visible?window:nullptr) ||
            playerRect.left!=left || playerRect.top!=top || playerRect.right!=right || playerRect.bottom!=bottom;
        if(browserHistory.empty())RememberBrowser(0);
        browserContexts[window]=generation;
        playerWindow=visible?window:nullptr;playerRect={left,top,right,bottom};playerSeen=GetTickCount64();
        playerGeneration=unsigned(generation);
        if(visible)GetWindowRect(window,&playerWindowRect);
        if(changed)RememberBrowser(contextChanged?GraphTimeline::EventTime(changedAt):GraphTimeline::Clock());
    }
    static void Context(HWND window,int generation) {
        std::lock_guard<std::mutex> lock(playerMutex);
        for(auto i=browserContexts.begin();i!=browserContexts.end();) {if(!IsWindow(i->first))i=browserContexts.erase(i);else ++i;}
        if(window)browserContexts[window]=generation;
    }
    static void Player(HWND window,int left,int top,int right,int bottom,int generation) {
        std::lock_guard<std::mutex> lock(playerMutex);
        if(window!=playerWindow || unsigned(generation)!=playerGeneration) playerGeneration=unsigned(generation);
        playerWindow=window;playerRect={left,top,right,bottom};playerSeen=GetTickCount64();
        if(window) GetWindowRect(window,&playerWindowRect);
    }

    static BrowserSnapshot ForFrame(double captureTime) {
        std::lock_guard<std::mutex> lock(playerMutex);
        BrowserSnapshot snapshot{0,playerWindow,playerRect,playerWindowRect,playerGeneration,browserContexts};
        // Never reinterpret a captured frame with metadata from a later tab.
        if(!browserHistory.empty()) {
            snapshot=browserHistory.front();
            for(auto i=browserHistory.rbegin();i!=browserHistory.rend();++i)if(i->time<=captureTime){snapshot=*i;break;}
        }
        snapshot.seenAt=playerSeen;
        return snapshot;
    }
}
