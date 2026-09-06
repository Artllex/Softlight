// Window bounds are refreshed at presentation rate.
// The selected analysis rate controls ordinary changes; new frames detect cuts.
static std::mutex reportMutex;
static std::wstring windowReport;
struct WindowRegion { HWND handle; RECT rect; bool eligible; std::wstring title; int part=0; bool browser=false; };
static std::mutex playerMutex;
static HWND playerWindow=nullptr;
static RECT playerRect{},playerWindowRect{};
static ULONGLONG playerSeen=0;
static unsigned playerGeneration=0;
static std::map<HWND,int> browserContexts,announcedContexts;
struct BrowserSnapshot {
    double time; HWND window;RECT rect,windowRect;unsigned generation;std::map<HWND,int> contexts;
};
static std::deque<BrowserSnapshot> browserHistory;
static void RememberBrowser(double time) {
    browserHistory.push_back({time,playerWindow,playerRect,playerWindowRect,playerGeneration,browserContexts});
    while(browserHistory.size()>64)browserHistory.pop_front();
}
#include "DimmingResponse.h"
struct WindowAnalyzer {
    std::vector<WindowRegion> windows;
    std::map<std::pair<HWND,int>,WindowGain> states;
    unsigned seenPlayerGeneration=0;
    HWND lastActive=nullptr,pendingSelected=nullptr;
    std::wstring pendingReport,pendingReading;
    double pendingTime=0;int pendingPart=0,pendingGeneration=0;
    bool ownsSelection=false;
    std::map<HWND,int> contexts;
    void Publish() {
        {std::lock_guard<std::mutex> lock(reportMutex);windowReport=pendingReport;}
        // The gain in this sample has now been submitted to the compositor.
        if(ownsSelection)GraphTimeline::Sample(pendingTime,pendingSelected,pendingPart,pendingGeneration,pendingReading);
    }
    RECT desktop{};
    double sampleTime=0; ULONGLONG tickTime=0;
    float previousStrength=-1;
    ComPtr<ID3D11Texture2D> sampleTexture, staging;
    ComPtr<ID3D11RenderTargetView> smallTarget;
    static BOOL CALLBACK Enumerate(HWND h, LPARAM p) {
        auto& a=*reinterpret_cast<WindowAnalyzer*>(p);
        if(!IsWindowVisible(h)||IsIconic(h)) return TRUE;
        DWORD cloaked=0; DwmGetWindowAttribute(h,DWMWA_CLOAKED,&cloaked,sizeof(cloaked));
        if(cloaked) return TRUE;
        wchar_t cls[128]{}; GetClassNameW(h,cls,128);
        if(wcscmp(cls,overlayClass)==0 || wcscmp(cls,L"Progman")==0 || wcscmp(cls,L"WorkerW")==0) return TRUE;
        RECT r{}; if(FAILED(DwmGetWindowAttribute(h,DWMWA_EXTENDED_FRAME_BOUNDS,&r,sizeof(r)))) GetWindowRect(h,&r);
        RECT intersection{}; if(!IntersectRect(&intersection,&r,&a.desktop)) return TRUE;
        wchar_t title[256]{}; GetWindowTextW(h,title,256);
        DWORD pid=0; GetWindowThreadProcessId(h,&pid);
        LONG_PTR style=GetWindowLongPtr(h,GWL_EXSTYLE);
        bool eligible=title[0] && !(style&WS_EX_TOOLWINDOW) &&
            !(pid==GetCurrentProcessId() && wcscmp(title,L"Nocny Filtr")==0) &&
            wcscmp(cls,L"Shell_TrayWnd")!=0 && wcscmp(cls,L"Shell_SecondaryTrayWnd")!=0;
        // Non-eligible foreground windows still occlude lower windows.
        a.windows.push_back({h,intersection,eligible,title,0,wcscmp(cls,L"MozillaWindowClass")==0});
        return a.windows.size()<64;
    }
    void Update(Pipeline& gpu, ID3D11ShaderResourceView* source, UINT width, UINT height, RECT bounds, Settings& s,bool frameChanged,double captureTime) {
        desktop=bounds; windows.clear(); EnumWindows(Enumerate,reinterpret_cast<LPARAM>(this));
        ULONGLONG now=GetTickCount64();
        {
            std::lock_guard<std::mutex> lock(playerMutex);
            BrowserSnapshot snapshot{0,playerWindow,playerRect,playerWindowRect,playerGeneration,browserContexts};
            // Never reinterpret a captured frame with metadata from a later tab.
            if(!browserHistory.empty()) {
                snapshot=browserHistory.front();
                for(auto i=browserHistory.rbegin();i!=browserHistory.rend();++i)if(i->time<=captureTime){snapshot=*i;break;}
            }
            contexts=snapshot.contexts;
            HWND framePlayer=snapshot.window;unsigned frameGeneration=snapshot.generation;
            if(seenPlayerGeneration!=frameGeneration) {
                for(auto it=states.begin();it!=states.end();) {if(it->first.second)it=states.erase(it);else ++it;}
                seenPlayerGeneration=frameGeneration;
            }
            RECT current{};
            if(framePlayer && now-playerSeen<500 &&
                GetWindowRect(framePlayer,&current) && EqualRect(&current,&snapshot.windowRect)) {
                for(size_t i=0;i<windows.size() && windows.size()<64;i++) if(windows[i].handle==framePlayer && windows[i].eligible) {
                    RECT clipped{};
                    if(IntersectRect(&clipped,&windows[i].rect,&snapshot.rect)) {
                        WindowRegion video{framePlayer,clipped,true,L"Firefox video",1};
                        windows[i].title=L"Firefox page: "+windows[i].title;
                        windows.insert(windows.begin()+i,video);
                    }
                    break;
                }
            }
        }
        float dt=tickTime?std::min(.25f,float(now-tickTime)/1000):0;tickTime=now;
        for(auto& w:windows) {
            DWORD pid=0;GetWindowThreadProcessId(w.handle,&pid);
            auto& g=states[{w.handle,w.part}];
            if(g.process && g.process!=pid) g=WindowGain{};
            if(g.lastSeen && now-g.lastSeen>250) g.holdUntil=now+250;
            g.lastSeen=now;g.process=pid;
        }
        LARGE_INTEGER counter,frequency;QueryPerformanceCounter(&counter);QueryPerformanceFrequency(&frequency);
        double sampleNow=double(counter.QuadPart)*1000/double(frequency.QuadPart);
        bool sampleReady=sampleNow-sampleTime>=AnalysisInterval(analysisHz.load());
        bool manual=previousStrength!=s.strength;previousStrength=s.strength;
        // New captured frames are also checked for large bright jumps. Ordinary
        // target changes still obey the selected analysis interval.
        if(sampleReady || frameChanged || manual) {
            if(!sampleTexture) {
                D3D11_TEXTURE2D_DESC d{};d.Width=160;d.Height=100;d.MipLevels=d.ArraySize=d.SampleDesc.Count=1;
                d.Format=DXGI_FORMAT_R32G32B32A32_FLOAT;d.BindFlags=D3D11_BIND_RENDER_TARGET;
                Check(gpu.device->CreateTexture2D(&d,nullptr,&sampleTexture));
                Check(gpu.device->CreateRenderTargetView(sampleTexture.Get(),nullptr,&smallTarget));
                d.BindFlags=0;d.Usage=D3D11_USAGE_STAGING;d.CPUAccessFlags=D3D11_CPU_ACCESS_READ;
                Check(gpu.device->CreateTexture2D(&d,nullptr,&staging));
            }
            Settings sample=s;sample.mode=2;for(float& v:sample.previewRect)v=0;
            gpu.Draw(source,smallTarget.Get(),160,100,sample);
            gpu.context->CopyResource(staging.Get(),sampleTexture.Get());
            double mapStart=PipelineTrace::Milliseconds();
            D3D11_MAPPED_SUBRESOURCE map{};Check(gpu.context->Map(staging.Get(),0,D3D11_MAP_READ,0,&map));
            PipelineTrace::Mark(8,0,PipelineTrace::Milliseconds()-mapStart);
            double totals[64]{};int counts[64]{};double scene=0;int pixels=0;
            for(int y=0;y<100;y++) for(int x=0;x<160;x++) {
                auto rgb=reinterpret_cast<float*>(static_cast<unsigned char*>(map.pData)+y*map.RowPitch)+4*x;
                float lum=.2126f*rgb[0]+.7152f*rgb[1]+.0722f*rgb[2];
                // HDR is linear scRGB; SDR composition uses encoded RGB.
                lum=std::clamp(lum/(s.hdr?s.whiteLevel:1),0.0f,4.0f);
                POINT pt{bounds.left+LONG((x+.5)*width/160),bounds.top+LONG((y+.5)*height/100)};
                for(size_t i=0;i<windows.size();i++) if(PtInRect(&windows[i].rect,pt)) {
                    if(windows[i].eligible) { totals[i]+=lum;counts[i]++;scene+=lum;pixels++; }
                    break;
                }
            }
            gpu.context->Unmap(staging.Get(),0);
            // Relative to visible app content, with a comfortable reference
            // for a single maximized bright window. No user threshold needed.
            float reference=std::clamp(pixels?float(scene/pixels)*.8f:.18f,.12f,.28f);
            for(size_t i=0;i<windows.size();i++) {
                auto& g=states[{windows[i].handle,windows[i].part}];
                if(!windows[i].eligible) {g.target=g.current=0;continue;}
                if(!g.measurable && counts[i]>=8 && g.hadSample) g.holdUntil=now+200;
                g.measurable=counts[i]>=8;
                if(g.measurable)g.measuredMean=float(totals[i]/counts[i]);
                if(g.measurable && (windows[i].part || now>=g.holdUntil)) {
                    float mean=float(totals[i]/counts[i]);
                    bool browserPart=windows[i].part || windows[i].title.find(L"Firefox page: ")==0;
                    float desired=WindowTarget(mean,browserPart?.18f:reference,s.strength);
                    if(windows[i].part) ObserveVideoGain(g,mean,desired,manual);
                    else ObserveWindowGain(g,mean,desired,sampleReady,manual);
                    g.hadSample=true;
                }
                else g.target=g.current; // No visible samples is not a dark frame.
            }
            if(sampleReady) sampleTime=sampleNow;
        }
        s.mode=1;s.regionCount=float(windows.size());
        std::wstring report;pendingReading.clear();pendingTime=GraphTimeline::Clock();
        HWND active=GetForegroundWindow();DWORD activePid=0;GetWindowThreadProcessId(active,&activePid);
        if(activePid!=GetCurrentProcessId())lastActive=active;
        int selected=-1;
        for(size_t i=0;i<windows.size();i++) if(windows[i].eligible && windows[i].handle==lastActive) {selected=int(i);break;}
        for(size_t i=0;i<windows.size();i++) {
            auto& w=windows[i];auto& g=states[{w.handle,w.part}];
            if(s.strength<=0) g.target=g.current=0;
            if(g.measurable) g.current=w.part?AdvanceVideoGain(g.current,g.target,dt,float(changeSpeed.load())):
                AdvanceWindowGain(g.current,g.target,dt,float(changeSpeed.load()),float(suddenSpeed.load()));
            if(std::abs(g.current-g.target)<.0005f) g.current=g.target;
            s.regions[i][0]=float(w.rect.left-bounds.left);s.regions[i][1]=float(w.rect.top-bounds.top);
            s.regions[i][2]=float(w.rect.right-bounds.left);s.regions[i][3]=float(w.rect.bottom-bounds.top);
            s.gains[i][0]=g.current;
            if(w.eligible && report.size()<2500) {
                size_t rowStart=report.size();
                report+=std::to_wstring(int(std::round(g.current*100)))+L"%  "+w.title;
                report+=L"\t"+(g.measurable?std::to_wstring(int(std::round(g.measuredMean*100))):L"?");
                report+=L"\t"+std::to_wstring(reinterpret_cast<uintptr_t>(w.handle))+L":"+std::to_wstring(w.part);
                if(w.browser || w.part) {
                    auto context=contexts.find(w.handle);
                    report+=L":"+std::to_wstring(context!=contexts.end()?context->second:0);
                }
                if(int(i)==selected) {
                    report+=L"\tactive";pendingReading=report.substr(rowStart);
                    PipelineTrace::Mark(2, w.browser || w.part ? contexts[w.handle] : 0,g.measuredMean*100,g.current*100,w.part);
                }
                report+=L"\r\n";
            }
        }
        // Keep hidden/minimized/off-screen windows until their HWND is destroyed.
        for(auto i=states.begin();i!=states.end();) {if(!IsWindow(i->first.first))i=states.erase(i);else ++i;}
        pendingSelected=selected>=0?windows[selected].handle:nullptr;
        pendingPart=selected>=0?windows[selected].part:0;pendingGeneration=pendingSelected?contexts[pendingSelected]:0;
        ownsSelection=selected>=0 || MonitorFromWindow(lastActive,MONITOR_DEFAULTTONEAREST)==MonitorFromPoint({bounds.left,bounds.top},MONITOR_DEFAULTTONEAREST);
        pendingReport=std::move(report);
    }
};
