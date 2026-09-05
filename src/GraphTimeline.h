// Engine-timed samples are retained independently of the UI refresh timer.
// Boundaries carry the original activation time, even when delivered late.
namespace GraphTimeline {
    struct Entry { unsigned long long sequence; double time; int kind; std::wstring reading; };
    static std::mutex mutex;
    static std::deque<Entry> entries;
    static unsigned long long sequence=0;
    static std::atomic<HWND> selected{nullptr};
    static int lastPart=0,lastGeneration=0;
    static double Clock() {
        LARGE_INTEGER now,frequency;QueryPerformanceCounter(&now);QueryPerformanceFrequency(&frequency);
        return double(now.QuadPart)/double(frequency.QuadPart);
    }
    static double EventTime(double epochMilliseconds) {
        double age=(PipelineTrace::Milliseconds()-epochMilliseconds)/1000;
        return Clock()-((std::isfinite(age) && age>=0 && age<5)?age:0);
    }
    static void Add(double time,int kind,const std::wstring& reading=L"") {
        std::lock_guard<std::mutex> lock(mutex);
        entries.push_back({++sequence,time,kind,reading});
        const double cutoff=Clock()-10;
        while(!entries.empty() && (entries.front().time<cutoff || entries.size()>4096))entries.pop_front();
    }
    static void Boundary(HWND window,double time) {
        if(window==selected.load() || window==GetForegroundWindow()){PipelineTrace::Mark(9,0,(Clock()-time)*1000);Add(time,1);}
    }
    static void Sample(double time,HWND window,int part,int generation,const std::wstring& reading) {
        HWND previous=selected.exchange(window);
        if(previous && (window!=previous || (part!=lastPart && generation==lastGeneration)))Add(time,1);
        lastPart=part;lastGeneration=generation;
        Add(time,0,reading);
    }
    static void Read(unsigned long long after,wchar_t* buffer,int length) {
        if(!buffer || length<1)return;
        std::lock_guard<std::mutex> lock(mutex);
        std::wstring result;
        for(auto& e:entries)if(e.sequence>after) {
            std::wstring row=std::to_wstring(e.sequence)+L"\t"+std::to_wstring(e.time)+L"\t"+std::to_wstring(e.kind)+L"\t"+e.reading+L"\n";
            if(result.size()+row.size()>=size_t(length))break;
            result+=row;
        }
        wcsncpy_s(buffer,length,result.c_str(),_TRUNCATE);
        PipelineTrace::Mark(10,0,double(after),double(sequence),double(result.size()));
    }
}
