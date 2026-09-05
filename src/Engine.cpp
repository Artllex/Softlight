#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include <d3d11.h>
#include <d3d11_1.h>
#include <dxgi1_6.h>
#include <dcomp.h>
#include <dwmapi.h>
#include <wrl/client.h>
#include <atomic>
#include <thread>
#include <mutex>
#include <vector>
#include <memory>
#include <algorithm>
#include <cmath>
#include <cstdio>
#include <map>
#include <deque>
#include <string>
#include <DirectXPackedVector.h>
#include "VertexShader.h"
#include "PixelShader.h"
#include "PipelineTrace.h"
#include "GraphTimeline.h"
using Microsoft::WRL::ComPtr;

struct Settings {
    float threshold = .45f, strength = .65f, curve = 1, rotation = 1;
    float previewRect[4]{};
    float hdr = 0, whiteLevel = 1, mode = 0, regionCount = 0;
    float regions[64][4]{};
    float gains[64][4]{};
};
struct Status { int state, monitors, hdrMonitors, error; unsigned long long frames, heartbeat; };
static std::mutex configMutex, windowsMutex;
static Settings config;
static std::atomic<bool> enabled{false}, stopping{false}, rebuild{false};
static std::atomic<bool> protectFrames{false};
static std::atomic<int> frameLimit{120}, state{0}, monitorCount{0}, hdrMonitors{0}, lastError{0};
static std::atomic<unsigned long long> frames{0}, heartbeat{0};
static std::thread worker;
static std::vector<HWND> liveWindows;
#include "DiagnosticState.h"
static const wchar_t* overlayClass = L"NocnyFiltr.Overlay.v1";
static HINSTANCE module;

static float WhiteLevel(const wchar_t* deviceName) {
    for (int attempt = 0; attempt < 3; ++attempt) {
        UINT np = 0, nm = 0;
        LONG code = GetDisplayConfigBufferSizes(QDC_ONLY_ACTIVE_PATHS, &np, &nm);
        if (code != ERROR_SUCCESS) throw HRESULT_FROM_WIN32(code);
        std::vector<DISPLAYCONFIG_PATH_INFO> paths(np);
        std::vector<DISPLAYCONFIG_MODE_INFO> modes(nm);
        code = QueryDisplayConfig(QDC_ONLY_ACTIVE_PATHS, &np, paths.data(), &nm, modes.data(), nullptr);
        if (code == ERROR_INSUFFICIENT_BUFFER) continue;
        if (code != ERROR_SUCCESS) throw HRESULT_FROM_WIN32(code);
        for (UINT i=0; i<np; ++i) {
            DISPLAYCONFIG_SOURCE_DEVICE_NAME name{};
            name.header = {DISPLAYCONFIG_DEVICE_INFO_GET_SOURCE_NAME, sizeof(name), paths[i].sourceInfo.adapterId, paths[i].sourceInfo.id};
            if (DisplayConfigGetDeviceInfo(&name.header) != ERROR_SUCCESS || wcscmp(name.viewGdiDeviceName,deviceName) != 0) continue;
            DISPLAYCONFIG_SDR_WHITE_LEVEL white{};
            white.header = {DISPLAYCONFIG_DEVICE_INFO_GET_SDR_WHITE_LEVEL, sizeof(white), paths[i].targetInfo.adapterId, paths[i].targetInfo.id};
            code = DisplayConfigGetDeviceInfo(&white.header);
            if (code != ERROR_SUCCESS) throw HRESULT_FROM_WIN32(code);
            if (white.SDRWhiteLevel == 0) throw E_UNEXPECTED;
            return float(white.SDRWhiteLevel) / 1000.0f;
        }
    }
    throw HRESULT_FROM_WIN32(ERROR_NOT_FOUND);
}

static void Check(HRESULT hr) { if (FAILED(hr)) throw hr; }
static void CheckWin(BOOL ok) { if (!ok) throw HRESULT_FROM_WIN32(GetLastError()); }
static Settings Snapshot() { std::lock_guard<std::mutex> lock(configMutex); return config; }
static void HideAll() {
    std::lock_guard<std::mutex> lock(windowsMutex);
    for (HWND h : liveWindows) ShowWindowAsync(h, SW_HIDE);
}
static LRESULT CALLBACK OverlayProc(HWND h, UINT msg, WPARAM w, LPARAM l) {
    if (msg == WM_NCHITTEST) return HTTRANSPARENT;
    if (msg == WM_MOUSEACTIVATE) return MA_NOACTIVATE;
    if (msg == WM_ERASEBKGND) return 1;
    if (msg == WM_DISPLAYCHANGE) rebuild = true;
    if (msg == WM_CLOSE) { ShowWindow(h, SW_HIDE); return 0; }
    return DefWindowProc(h, msg, w, l);
}

#include "GpuPipeline.h"
#include "DesktopCapture.h"
#include "WindowAnalysis.h"

#include "MonitorRenderer.h"

static void Pump() { MSG msg; while (PeekMessage(&msg, nullptr, 0, 0, PM_REMOVE)) { TranslateMessage(&msg); DispatchMessage(&msg); } }
static void Worker() {
    HANDLE timer = CreateWaitableTimerExW(nullptr, nullptr, 0x2, TIMER_ALL_ACCESS);
    if (!timer) timer = CreateWaitableTimerExW(nullptr, nullptr, 0, TIMER_ALL_ACCESS);
    LARGE_INTEGER frequency{}; QueryPerformanceFrequency(&frequency);
    SetThreadDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
    CoInitializeEx(nullptr, COINIT_MULTITHREADED);
    WNDCLASSEX wc{sizeof(wc)}; wc.lpfnWndProc = OverlayProc; wc.hInstance = module; wc.lpszClassName = overlayClass;
    RegisterClassEx(&wc);
    while (!stopping) {
        heartbeat = GetTickCount64();
        if (!enabled) { state = 3; Pump(); Sleep(30); continue; }
        state = 1; rebuild = false; lastError = 0; hdrMonitors = 0;
        try {
            ComPtr<IDXGIFactory2> factory; Check(CreateDXGIFactory1(IID_PPV_ARGS(&factory)));
            std::vector<std::unique_ptr<Monitor>> monitors;
            for (UINT ai = 0;; ++ai) {
                ComPtr<IDXGIAdapter1> adapter;
                HRESULT ah = factory->EnumAdapters1(ai, &adapter);
                if (ah == DXGI_ERROR_NOT_FOUND) break;
                Check(ah);
                for (UINT oi = 0;; ++oi) {
                    ComPtr<IDXGIOutput> output;
                    HRESULT oh = adapter->EnumOutputs(oi, &output);
                    if (oh == DXGI_ERROR_NOT_FOUND) break;
                    Check(oh);
                    DXGI_OUTPUT_DESC od{}; Check(output->GetDesc(&od));
                    if (!od.AttachedToDesktop) continue;
                    ComPtr<IDXGIOutput6> out6;
                    bool isHdr = false;
                    if (SUCCEEDED(output.As(&out6))) {
                        DXGI_OUTPUT_DESC1 d1{}; Check(out6->GetDesc1(&d1));
                        isHdr = d1.ColorSpace == DXGI_COLOR_SPACE_RGB_FULL_G2084_NONE_P2020;
                    }
                    auto m = std::make_unique<Monitor>(); m->Init(factory.Get(), adapter.Get(), output.Get(), isHdr);
                    if (isHdr) ++hdrMonitors;
                    monitors.push_back(std::move(m));
                }
            }
            monitorCount = int(monitors.size());
            if (monitors.empty()) throw HRESULT_FROM_WIN32(ERROR_NOT_SUPPORTED);
            while (enabled && !stopping && !rebuild && factory->IsCurrent()) {
                LARGE_INTEGER start{}; QueryPerformanceCounter(&start);
                Settings s = Snapshot();
                bool ready = true;
                for (auto& m : monitors) ready = m->Tick(s) && ready;
                heartbeat = GetTickCount64();
                state = ready ? 2 : 1;
                Pump();
                LARGE_INTEGER end{}; QueryPerformanceCounter(&end);
                LONGLONG remaining = 10000000 / frameLimit.load() -
                    (end.QuadPart - start.QuadPart) * 10000000 / frequency.QuadPart;
                if (remaining > 0 && timer) {
                    LARGE_INTEGER due{}; due.QuadPart = -remaining;
                    if (SetWaitableTimer(timer, &due, 0, nullptr, nullptr, FALSE)) WaitForSingleObject(timer, 50);
                } else if (remaining > 0) Sleep(1);
            }
        } catch (HRESULT hr) {
            lastError = int(hr); state = 4;
        } catch (...) {
            lastError = int(E_UNEXPECTED); state = 4;
        }
        // All masks were destroyed above before waiting or rebuilding.
        monitorCount = 0;
        if (state == 4) for (int i = 0; i < 50 && enabled && !stopping && !rebuild; ++i) {
            heartbeat = GetTickCount64(); Pump(); Sleep(40);
        }
    }
    if (timer) CloseHandle(timer);
    state = 0; CoUninitialize();
}

#define API extern "C" __declspec(dllexport)
API int __cdecl NfStart() {
    if (worker.joinable()) return 1;
    stopping = false; enabled = false;testHoldCapture=false;
    PipelineTrace::Start();
    GetModuleHandleEx(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,
        reinterpret_cast<LPCWSTR>(&NfStart), &module);
    try { worker = std::thread(Worker); return 1; } catch (...) { return 0; }
}
API void __cdecl NfConfigure(float t, float s, int curve, int fps) {
    if (!std::isfinite(t) || !std::isfinite(s)) return;
    std::lock_guard<std::mutex> lock(configMutex);
    config.threshold = std::clamp(t, 0.0f, .95f);
    config.strength = std::clamp(s, 0.0f, .95f);
    config.curve = curve ? 1.0f : 0.0f;
    frameLimit = fps <= 30 ? 30 : 120;
}
API void __cdecl NfFlashProtection(int value) {protectFrames=value!=0;}
API void __cdecl NfEnable(int value) {
    enabled = value != 0;
    if (!enabled) HideAll();
}
API void __cdecl NfRefresh() { rebuild = true; }
API void __cdecl NfPreviewRect(int x, int y, int width, int height) {
    std::lock_guard<std::mutex> lock(configMutex);
    config.previewRect[0] = float(x); config.previewRect[1] = float(y);
    config.previewRect[2] = float(x + width); config.previewRect[3] = float(y + height);
}
API void __cdecl NfGetStatus(Status* result) {
    if (result) *result = {state.load(), monitorCount.load(), hdrMonitors.load(), lastError.load(), frames.load(), heartbeat.load()};
}
API void __cdecl NfStop() {
    stopping = true; enabled = false; HideAll();
    if (worker.joinable()) worker.join();
    PipelineTrace::Save();
}

API void __cdecl NfTraceMark(int stage,int generation,double a,double b,double c) {PipelineTrace::Mark(stage,generation,a,b,c);}
API void __cdecl NfWindowReport(wchar_t* buffer, int length) { if(!buffer || length<1) return; std::lock_guard<std::mutex> lock(reportMutex); wcsncpy_s(buffer,length,windowReport.c_str(),_TRUNCATE); }
API void __cdecl NfGraphRead(unsigned long long after,wchar_t* buffer,int length) {GraphTimeline::Read(after,buffer,length);}
API void __cdecl NfBrowserUpdate(HWND window,int generation,double changedAt,int pending,int visible,int left,int top,int right,int bottom) {
    BrowserContext::Update(window,generation,changedAt,pending,visible,left,top,right,bottom);
}
API void __cdecl NfBrowserContext(HWND window,int generation) {BrowserContext::Context(window,generation);}
API void __cdecl NfPlayer(HWND window,int left,int top,int right,int bottom,int generation) {BrowserContext::Player(window,left,top,right,bottom,generation);}
API void __cdecl NfTiming(int hz,int speed,int sudden) {
    analysisHz=hz==120||hz==60||hz==30||hz==12||hz==4?hz:30;
    changeSpeed=std::clamp(speed,0,100);suddenSpeed=std::clamp(sudden,0,100);
}

#include "EngineDiagnostics.h"
