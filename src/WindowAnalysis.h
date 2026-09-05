// Window bounds are refreshed at presentation rate; brightness is sampled at
// Up to 30 or 60 Hz. One alpha per HWND preserves internal tonal relationships.
static std::mutex reportMutex;
static std::wstring windowReport;
struct WindowRegion { HWND handle; RECT rect; bool eligible; std::wstring title; };
struct WindowGain { float current=0, target=0, mean=0; bool measurable=false; DWORD process=0; ULONGLONG lastSeen=0,holdUntil=0; bool hadSample=false; };
static void ObserveWindowGain(WindowGain& g,float mean,float desired,bool regular,bool manual) {
    bool flash=mean>g.mean+.12f && mean>g.mean*1.6f && desired>g.current+.08f;
    if(regular || flash || manual) {
        if(manual || std::abs(desired-g.target)>.02001f) g.target=desired;
        if(flash) g.current=g.target; // Attack now; never slowly fade into a flash.
        g.mean=mean;
    }
}
static std::atomic<int> analysisHz{30}, changeSpeed{50}, suddenSpeed{50};
static double AnalysisInterval(int hz) { return 1000.0/hz; }
static float AdvanceWindowGain(float current,float target,float dt,float speed=50,float sudden=50) {

    float general=std::pow(4.0f,(speed-50)/50);
    float distance=std::min(1.0f,std::abs(target-current)/.35f);
    float adaptive=std::pow(4.0f,(sudden-50)/50*distance);
    float blend=1-std::exp(-dt*general*adaptive/(target>current?.18f:.30f));
    return current+(target-current)*blend;
}
static float WindowTarget(float mean, float reference, float strength) {
    // Automatic region selection, never a per-pixel threshold or curve.
    float excess=std::max(0.0f,mean-reference);
    float base=std::clamp(strength*excess/std::max(mean,.001f),0.0f,.95f);
    float upper=std::clamp((strength-.65f)/.30f,0.0f,1.0f);
    float boost=1+14*upper*upper;
    return std::clamp(1-std::pow(1-base,boost),0.0f,.95f);
}
struct WindowAnalyzer {
    std::vector<WindowRegion> windows;
    std::map<HWND,WindowGain> states;
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
        a.windows.push_back({h,intersection,eligible,title});
        return a.windows.size()<64;
    }
    void Update(Pipeline& gpu, ID3D11ShaderResourceView* source, UINT width, UINT height, RECT bounds, Settings& s,bool frameChanged) {
        desktop=bounds; windows.clear(); EnumWindows(Enumerate,reinterpret_cast<LPARAM>(this));
        ULONGLONG now=GetTickCount64();
        float dt=tickTime?std::min(.25f,float(now-tickTime)/1000):0;tickTime=now;
        for(auto& w:windows) {
            DWORD pid=0;GetWindowThreadProcessId(w.handle,&pid);
            auto& g=states[w.handle];
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
            D3D11_MAPPED_SUBRESOURCE map{};Check(gpu.context->Map(staging.Get(),0,D3D11_MAP_READ,0,&map));
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
                auto& g=states[windows[i].handle];
                if(!windows[i].eligible) {g.target=g.current=0;continue;}
                if(!g.measurable && counts[i]>=8 && g.hadSample) g.holdUntil=now+200;
                g.measurable=counts[i]>=8;
                if(g.measurable && now>=g.holdUntil) {
                    float mean=float(totals[i]/counts[i]);
                    ObserveWindowGain(g,mean,WindowTarget(mean,reference,s.strength),sampleReady,manual);g.hadSample=true;
                }
                else g.target=g.current; // No visible samples is not a dark frame.
            }
            if(sampleReady) sampleTime=sampleNow;
        }
        s.mode=1;s.regionCount=float(windows.size());
        std::wstring report;
        for(size_t i=0;i<windows.size();i++) {
            auto& w=windows[i];auto& g=states[w.handle];
            if(s.strength<=0) g.target=g.current=0;
            if(g.measurable) g.current=AdvanceWindowGain(g.current,g.target,dt,float(changeSpeed.load()),float(suddenSpeed.load()));
            if(std::abs(g.current-g.target)<.0005f) g.current=g.target;
            s.regions[i][0]=float(w.rect.left-bounds.left);s.regions[i][1]=float(w.rect.top-bounds.top);
            s.regions[i][2]=float(w.rect.right-bounds.left);s.regions[i][3]=float(w.rect.bottom-bounds.top);
            s.gains[i][0]=g.current;
            if(w.eligible && report.size()<2500) {
                report+=std::to_wstring(int(std::round(g.current*100)))+L"%  "+w.title+L"\r\n";
            }
        }
        // Keep hidden/minimized/off-screen windows until their HWND is destroyed.
        for(auto i=states.begin();i!=states.end();) {if(!IsWindow(i->first))i=states.erase(i);else ++i;}
        {std::lock_guard<std::mutex> lock(reportMutex);windowReport=report;}
    }
};
