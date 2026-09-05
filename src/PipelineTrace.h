// Opt-in, bounded timing diagnostics. No titles, URLs or screen pixels are stored.
// SOFTLIGHT_TRACE names a CSV file written only when the engine stops.
namespace PipelineTrace {
    struct Event { double time; int stage, generation; double a,b,c; };
    static std::mutex mutex;
    static std::vector<Event> events;
    static wchar_t path[32768]{};
    static bool active=false;
    static double Milliseconds() {
        FILETIME ft;GetSystemTimePreciseAsFileTime(&ft);
        ULARGE_INTEGER t;t.LowPart=ft.dwLowDateTime;t.HighPart=ft.dwHighDateTime;
        return double(t.QuadPart-116444736000000000ULL)/10000;
    }
    static void Start() {
        active=GetEnvironmentVariableW(L"SOFTLIGHT_TRACE",path,32768)>0;
        events.clear();
        if(active)events.reserve(32768);
    }
    static void Mark(int stage,int generation=0,double a=0,double b=0,double c=0) {
        if(!active)return;
        Event e{Milliseconds(),stage,generation,a,b,c};
        std::lock_guard<std::mutex> lock(mutex);
        if(events.size()<32768)events.push_back(e);
    }
    static void Save() {
        if(!active)return;
        std::lock_guard<std::mutex> lock(mutex);
        FILE* file=nullptr;
        if(_wfopen_s(&file,path,L"w")==0) {
            fprintf(file,"time,stage,generation,a,b,c\n");
            for(auto& e:events)fprintf(file,"%.3f,%d,%d,%.3f,%.3f,%.3f\n",e.time,e.stage,e.generation,e.a,e.b,e.c);
            fclose(file);
        }
    }
}
