#pragma once
// Response policy shared by runtime analysis and native regression tests.
struct WindowGain { float current=0, target=0, mean=0; bool measurable=false; DWORD process=0; ULONGLONG lastSeen=0,holdUntil=0,cutUntil=0; bool hadSample=false; std::vector<std::pair<ULONGLONG,float>> recent; };
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
// Video: a +/-10 pp fluctuation can span 20 pp between two observations.
// Larger cuts in either direction immediately apply the corresponding dimming.
static void ObserveVideoGain(WindowGain& g,float mean,float desired,bool manual,ULONGLONG now=GetTickCount64()) {
    g.recent.erase(std::remove_if(g.recent.begin(),g.recent.end(),[now](const auto& p){return now-p.first>120;}),g.recent.end());
    bool cut=g.hadSample && std::abs(mean-g.mean)>.22001f;
    for(const auto& p:g.recent) if(std::abs(mean-p.second)>.22001f) cut=true;
    if(cut) g.cutUntil=now+150;
    g.recent.push_back({now,mean});
    if(g.recent.size()>64)g.recent.erase(g.recent.begin());
    g.target=desired;
    if(manual || cut || now<g.cutUntil || (!g.hadSample && desired>g.current)) g.current=desired;
    g.mean=mean;g.hadSample=true;
}
static float AdvanceVideoGain(float current,float target,float dt,float speed=50) {
    float seconds=std::clamp(5.0f/std::pow(4.0f,(speed-50)/50),1.5f,12.0f);
    return current+(target-current)*(1-std::exp(-dt/seconds));
}
static float WindowTarget(float mean, float reference, float strength) {
    // Automatic region selection, never a per-pixel threshold or curve.
    float excess=std::max(0.0f,mean-reference);
    float base=std::clamp(strength*excess/std::max(mean,.001f),0.0f,.95f);
    float upper=std::clamp((strength-.65f)/.30f,0.0f,1.0f);
    float boost=1+14*upper*upper;
    return std::clamp(1-std::pow(1-base,boost),0.0f,.95f);
}
