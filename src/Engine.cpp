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
static std::atomic<int> frameLimit{120}, state{0}, monitorCount{0}, hdrMonitors{0}, lastError{0};
static std::atomic<unsigned long long> frames{0}, heartbeat{0};
static std::thread worker;
static std::vector<HWND> liveWindows;
// Explicit diagnostic requests sample only a caller-selected test pixel.
// These are unused during ordinary operation; no screen images are saved.
static std::atomic<int> probeX{-1}, probeY{-1}, probeComposite{0}, probeRequest{0}, probeDone{0};
static std::atomic<unsigned int> probeInput{0}, probeMask{0}, probeDisplay{0};
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

struct Pipeline {
    ComPtr<ID3D11Device> device;
    ComPtr<ID3D11DeviceContext> context;
    ComPtr<ID3D11DeviceContext1> clearContext;
    ComPtr<ID3D11VertexShader> vs;
    ComPtr<ID3D11PixelShader> ps;
    ComPtr<ID3D11Buffer> constants;
    ComPtr<ID3D11SamplerState> sampler;
    void Init(IDXGIAdapter* adapter, bool warp = false) {
        D3D_FEATURE_LEVEL levels[] = {D3D_FEATURE_LEVEL_11_0, D3D_FEATURE_LEVEL_10_1};
        Check(D3D11CreateDevice(adapter, warp ? D3D_DRIVER_TYPE_WARP : D3D_DRIVER_TYPE_UNKNOWN,
            nullptr, D3D11_CREATE_DEVICE_BGRA_SUPPORT, levels, 2, D3D11_SDK_VERSION,
            &device, nullptr, &context));
        context.As(&clearContext);
        Check(device->CreateVertexShader(vertexShader, sizeof(vertexShader), nullptr, &vs));
        Check(device->CreatePixelShader(pixelShader, sizeof(pixelShader), nullptr, &ps));
        D3D11_BUFFER_DESC bd{}; bd.ByteWidth = sizeof(Settings); bd.Usage = D3D11_USAGE_DEFAULT;
        bd.BindFlags = D3D11_BIND_CONSTANT_BUFFER;
        Check(device->CreateBuffer(&bd, nullptr, &constants));
        D3D11_SAMPLER_DESC sd{}; sd.Filter = D3D11_FILTER_MIN_MAG_MIP_POINT;
        sd.AddressU = sd.AddressV = sd.AddressW = D3D11_TEXTURE_ADDRESS_CLAMP;
        sd.MaxLOD = D3D11_FLOAT32_MAX;
        Check(device->CreateSamplerState(&sd, &sampler));
    }
    void DrawMask(ID3D11ShaderResourceView* input,ID3D11RenderTargetView* target,UINT width,UINT height,const Settings& s) {
        if(!clearContext) {Draw(input,target,width,height,s);return;}
        float color[4]{};context->ClearRenderTargetView(target,color);
        // ClearView clips rectangle bounds to the target; reverse Z order means
        // foreground regions (including transparent occluders) win exactly once.
        for(int i=int(s.regionCount)-1;i>=0;--i) {
            RECT r{LONG(s.regions[i][0]),LONG(s.regions[i][1]),LONG(s.regions[i][2]),LONG(s.regions[i][3])};
            color[3]=s.gains[i][0];clearContext->ClearView(target,color,&r,1);
        }
    }
    void Draw(ID3D11ShaderResourceView* input, ID3D11RenderTargetView* target,
              UINT width, UINT height, const Settings& s) {
        context->UpdateSubresource(constants.Get(), 0, nullptr, &s, 0, 0);
        D3D11_VIEWPORT vp{0, 0, float(width), float(height), 0, 1};
        context->RSSetViewports(1, &vp);
        context->OMSetRenderTargets(1, &target, nullptr);
        context->IASetPrimitiveTopology(D3D11_PRIMITIVE_TOPOLOGY_TRIANGLELIST);
        context->VSSetShader(vs.Get(), nullptr, 0);
        context->PSSetShader(ps.Get(), nullptr, 0);
        ID3D11Buffer* cb = constants.Get(); context->PSSetConstantBuffers(0, 1, &cb);
        ID3D11SamplerState* ss = sampler.Get(); context->PSSetSamplers(0, 1, &ss);
        context->PSSetShaderResources(0, 1, &input);
        context->Draw(3, 0);
        input = nullptr; context->PSSetShaderResources(0, 1, &input);
    }
};

#include "WindowAnalysis.h"

struct Monitor {
    WindowAnalyzer analyzer;
    Pipeline gpu;
    ComPtr<IDXGIOutputDuplication> duplication;
    ComPtr<IDXGISwapChain1> swapchain;
    ComPtr<ID3D11Texture2D> source;
    ComPtr<ID3D11ShaderResourceView> sourceView;
    ComPtr<ID3D11RenderTargetView> target;

    ComPtr<IDCompositionDevice> composition;
    ComPtr<IDCompositionTarget> compositionTarget;
    ComPtr<IDCompositionVisual> visual;
    HWND window = nullptr;
    DXGI_OUTPUT_DESC desc{};
    UINT width = 0, height = 0;
    bool hasFrame = false;
    double captureTime=0;
    bool hdr = false;
    float whiteLevel = 1;
    ULONGLONG whiteChecked = 0;
    Settings previous{};
    ULONGLONG zOrderTime = 0;
    unsigned int ReadPixel(ID3D11Texture2D* texture, UINT x, UINT y) {
        D3D11_TEXTURE2D_DESC td{}; texture->GetDesc(&td);
        if (x >= td.Width || y >= td.Height) throw E_INVALIDARG;
        td.Width = td.Height = 1; td.MipLevels = td.ArraySize = 1;
        td.Usage = D3D11_USAGE_STAGING; td.BindFlags = 0; td.CPUAccessFlags = D3D11_CPU_ACCESS_READ; td.MiscFlags = 0;
        ComPtr<ID3D11Texture2D> staging; Check(gpu.device->CreateTexture2D(&td, nullptr, &staging));
        D3D11_BOX box{x, y, 0, x+1, y+1, 1};
        gpu.context->CopySubresourceRegion(staging.Get(), 0, 0, 0, 0, texture, 0, &box);
        D3D11_MAPPED_SUBRESOURCE mapped{}; Check(gpu.context->Map(staging.Get(), 0, D3D11_MAP_READ, 0, &mapped));
        unsigned int pixel;
        if (td.Format == DXGI_FORMAT_R16G16B16A16_FLOAT) {
            auto half = static_cast<const DirectX::PackedVector::HALF*>(mapped.pData);
            float r = DirectX::PackedVector::XMConvertHalfToFloat(half[0]);
            float g = DirectX::PackedVector::XMConvertHalfToFloat(half[1]);
            float b = DirectX::PackedVector::XMConvertHalfToFloat(half[2]);
            float a = DirectX::PackedVector::XMConvertHalfToFloat(half[3]);
            auto byte = [](float v) { return unsigned(std::lround(std::clamp(v,0.0f,1.0f)*255)); };
            auto encode = [](float v) { return v <= .0031308f ? v*12.92f : 1.055f*std::pow(v,1/2.4f)-.055f; };
            pixel = byte(encode(b/whiteLevel)) | (byte(encode(g/whiteLevel))<<8) | (byte(encode(r/whiteLevel))<<16) | (byte(a)<<24);
        } else pixel = *static_cast<unsigned int*>(mapped.pData);
        gpu.context->Unmap(staging.Get(), 0); return pixel;
    }
    void Probe() {
        int version = probeRequest.load();
        if (!hasFrame || version == probeDone.load()) return;
        POINT pt{probeX.load(), probeY.load()};
        if (!PtInRect(&desc.DesktopCoordinates, pt)) return;
        UINT x = pt.x - desc.DesktopCoordinates.left, y = pt.y - desc.DesktopCoordinates.top;
        UINT sx = x, sy = y;
        if (desc.Rotation == DXGI_MODE_ROTATION_ROTATE90) { sx = y; sy = width - 1 - x; }
        if (desc.Rotation == DXGI_MODE_ROTATION_ROTATE180) { sx = width - 1 - x; sy = height - 1 - y; }
        if (desc.Rotation == DXGI_MODE_ROTATION_ROTATE270) { sx = height - 1 - y; sy = x; }
        // After a flip, the render target is the next back buffer. It can retain
        // an older mask now that unchanged masks no longer get repainted.
        // Recreate the submitted mask there for this explicit diagnostic only.
        gpu.DrawMask(sourceView.Get(),target.Get(),width,height,previous);
        probeInput = ReadPixel(source.Get(), sx, sy);
        ComPtr<ID3D11Resource> targetResource; target->GetResource(&targetResource);
        ComPtr<ID3D11Texture2D> targetTexture; Check(targetResource.As(&targetTexture));
        probeMask = ReadPixel(targetTexture.Get(), x, y);
        if (probeComposite.load()) {
            // No captures run between exposing the mask and destroying it.
            // Read one known test pixel from DWM to verify actual alpha blending.
            CheckWin(SetWindowDisplayAffinity(window, WDA_NONE));
            DwmFlush(); Sleep(80);
            HDC screen = GetDC(nullptr);
            COLORREF color = GetPixel(screen, pt.x, pt.y);
            ReleaseDC(nullptr, screen);
            if (hdr) {
                // GDI GetPixel does not expose HDR luminance faithfully. Read
                // the composed scRGB frame, normalized with the same SDR white.
                DXGI_OUTDUPL_FRAME_INFO frame{};
                ComPtr<IDXGIResource> resource;
                Check(duplication->AcquireNextFrame(500, &frame, &resource));
                try {
                    ComPtr<ID3D11Texture2D> texture; Check(resource.As(&texture));
                    unsigned int bgra=ReadPixel(texture.Get(),sx,sy);
                    color=RGB((bgra>>16)&255,(bgra>>8)&255,bgra&255);
                } catch(...) { duplication->ReleaseFrame();throw; }
                Check(duplication->ReleaseFrame());
            }
            SetWindowDisplayAffinity(window, WDA_EXCLUDEFROMCAPTURE);
            probeDisplay = color;
            rebuild = true;
        }
        probeDone = version;
    }
    ~Monitor() {
        if (window) {
            ShowWindow(window, SW_HIDE);
            { std::lock_guard<std::mutex> lock(windowsMutex);
              liveWindows.erase(std::remove(liveWindows.begin(), liveWindows.end(), window), liveWindows.end()); }
            compositionTarget.Reset(); visual.Reset(); composition.Reset();
            DestroyWindow(window);
        }
    }
    void Init(IDXGIFactory2* factory, IDXGIAdapter* adapter, IDXGIOutput* output, bool isHdr) {
        Check(output->GetDesc(&desc));
        hdr = isHdr;
        if (hdr) whiteLevel = WhiteLevel(desc.DeviceName);
        width = desc.DesktopCoordinates.right - desc.DesktopCoordinates.left;
        height = desc.DesktopCoordinates.bottom - desc.DesktopCoordinates.top;
        gpu.Init(adapter);
        if (hdr) {
            ComPtr<IDXGIOutput5> output5; Check(output->QueryInterface(IID_PPV_ARGS(&output5)));
            DXGI_FORMAT formats[] = {DXGI_FORMAT_R16G16B16A16_FLOAT, DXGI_FORMAT_B8G8R8A8_UNORM};
            Check(output5->DuplicateOutput1(gpu.device.Get(), 0, 2, formats, &duplication));
        } else {
            ComPtr<IDXGIOutput1> output1; Check(output->QueryInterface(IID_PPV_ARGS(&output1)));
            Check(output1->DuplicateOutput(gpu.device.Get(), &duplication));
        }
        window = CreateWindowEx(WS_EX_TOPMOST | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE |
            WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_NOREDIRECTIONBITMAP,
            overlayClass, L"Nocny Filtr — maska", WS_POPUP,
            desc.DesktopCoordinates.left, desc.DesktopCoordinates.top, width, height,
            nullptr, nullptr, module, nullptr);
        CheckWin(window != nullptr);
        { std::lock_guard<std::mutex> lock(windowsMutex); liveWindows.push_back(window); }
        CheckWin(SetLayeredWindowAttributes(window, 0, 255, LWA_ALPHA));
        // Essential: a subsequent desktop capture must omit our own mask.
        // Fail closed (no overlay) if Windows cannot provide this contract.
        CheckWin(SetWindowDisplayAffinity(window, WDA_EXCLUDEFROMCAPTURE));
        DWORD affinity = 0; CheckWin(GetWindowDisplayAffinity(window, &affinity));
        if (affinity != WDA_EXCLUDEFROMCAPTURE) throw E_NOTIMPL;
        DXGI_SWAP_CHAIN_DESC1 sd{};
        sd.Width = width; sd.Height = height; sd.Format = hdr ? DXGI_FORMAT_R16G16B16A16_FLOAT : DXGI_FORMAT_B8G8R8A8_UNORM;
        sd.SampleDesc.Count = 1; sd.BufferUsage = DXGI_USAGE_RENDER_TARGET_OUTPUT;
        sd.BufferCount = 2; sd.SwapEffect = DXGI_SWAP_EFFECT_FLIP_SEQUENTIAL;
        sd.AlphaMode = DXGI_ALPHA_MODE_PREMULTIPLIED;
        Check(factory->CreateSwapChainForComposition(gpu.device.Get(), &sd, nullptr, &swapchain));
        if (hdr) {
            ComPtr<IDXGISwapChain3> swap3; Check(swapchain.As(&swap3));
            Check(swap3->SetColorSpace1(DXGI_COLOR_SPACE_RGB_FULL_G10_NONE_P709));
        }
        ComPtr<IDXGIDevice> dxgiDevice; Check(gpu.device.As(&dxgiDevice));
        Check(DCompositionCreateDevice(dxgiDevice.Get(), IID_PPV_ARGS(&composition)));
        Check(composition->CreateTargetForHwnd(window, TRUE, &compositionTarget));
        Check(composition->CreateVisual(&visual));
        Check(visual->SetContent(swapchain.Get()));
        Check(compositionTarget->SetRoot(visual.Get()));
        Check(composition->Commit());
        ComPtr<ID3D11Texture2D> buffer; Check(swapchain->GetBuffer(0, IID_PPV_ARGS(&buffer)));
        Check(gpu.device->CreateRenderTargetView(buffer.Get(), nullptr, &target));
        float clear[4]{}; gpu.context->ClearRenderTargetView(target.Get(), clear);
        Check(swapchain->Present(0, 0));
    }
    bool Tick(Settings s) {
        s.rotation = float(desc.Rotation);
        if (hdr && GetTickCount64() - whiteChecked > 1000) {
            whiteLevel = WhiteLevel(desc.DeviceName); whiteChecked = GetTickCount64();
        }
        s.hdr = hdr ? 1.0f : 0.0f; s.whiteLevel = whiteLevel;
        s.previewRect[0] -= desc.DesktopCoordinates.left;
        s.previewRect[1] -= desc.DesktopCoordinates.top;
        s.previewRect[2] -= desc.DesktopCoordinates.left;
        s.previewRect[3] -= desc.DesktopCoordinates.top;
        DXGI_OUTDUPL_FRAME_INFO info{};
        ComPtr<IDXGIResource> resource;
        double acquireStart=PipelineTrace::Milliseconds();
        HRESULT hr = duplication->AcquireNextFrame(monitorCount.load() == 1 ? 8 : 0, &info, &resource);
        bool changed = false;
        if (hr != DXGI_ERROR_WAIT_TIMEOUT) {
            Check(hr);
            try {
                if (info.LastPresentTime.QuadPart != 0 || !hasFrame) {
                    ComPtr<ID3D11Texture2D> captured; Check(resource.As(&captured));
                    if (!source) {
                        D3D11_TEXTURE2D_DESC td{}; captured->GetDesc(&td);
                        if (hdr && td.Format != DXGI_FORMAT_R16G16B16A16_FLOAT) throw DXGI_ERROR_UNSUPPORTED;
                        td.Usage = D3D11_USAGE_DEFAULT; td.BindFlags = D3D11_BIND_SHADER_RESOURCE;
                        td.CPUAccessFlags = 0; td.MiscFlags = 0;
                        Check(gpu.device->CreateTexture2D(&td, nullptr, &source));
                        Check(gpu.device->CreateShaderResourceView(source.Get(), nullptr, &sourceView));
                    }
                    gpu.context->CopyResource(source.Get(), captured.Get());
                    LARGE_INTEGER frequency;QueryPerformanceFrequency(&frequency);
                    captureTime=info.LastPresentTime.QuadPart?double(info.LastPresentTime.QuadPart)/double(frequency.QuadPart):GraphTimeline::Clock();
                    hasFrame = changed = true;
                }
            } catch (...) { duplication->ReleaseFrame(); throw; }
            Check(duplication->ReleaseFrame());
        }
        PipelineTrace::Mark(1,0,changed?1:0,PipelineTrace::Milliseconds()-acquireStart,(GraphTimeline::Clock()-captureTime)*1000);
        double analysisStart=PipelineTrace::Milliseconds();
        if (hasFrame) analyzer.Update(gpu, sourceView.Get(), width, height, desc.DesktopCoordinates, s, changed,captureTime);
        double analysisEnd=PipelineTrace::Milliseconds();
        if (hasFrame && (memcmp(&s, &previous, sizeof(s)) != 0 || !IsWindowVisible(window))) {
            gpu.DrawMask(sourceView.Get(),target.Get(),width,height,s);
            Check(swapchain->Present(0, 0));
            PipelineTrace::Mark(3,0,analysisEnd-analysisStart,PipelineTrace::Milliseconds()-analysisEnd);
            previous = s;
            ++frames;
            if (enabled && !stopping && !IsWindowVisible(window)) ShowWindow(window, SW_SHOWNOACTIVATE);
        }
        if(hasFrame)analyzer.Publish();
        // Taskbar flyouts and other topmost windows can otherwise escape the mask.
        if (hasFrame && enabled && GetTickCount64() - zOrderTime > 1000) {
            SetWindowPos(window, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
            zOrderTime = GetTickCount64();
        }
        Probe();
        return hasFrame;
    }
};

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
    stopping = false; enabled = false;
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
API int __cdecl NfProbe(int x, int y, int composite) {
    probeX = x; probeY = y; probeComposite = composite;
    return ++probeRequest;
}
API int __cdecl NfProbeResult(unsigned int* input, unsigned int* mask, unsigned int* display) {
    int done = probeDone.load();
    *input = probeInput.load(); *mask = probeMask.load(); *display = probeDisplay.load();
    return done;
}
API void __cdecl NfGetStatus(Status* result) {
    if (result) *result = {state.load(), monitorCount.load(), hdrMonitors.load(), lastError.load(), frames.load(), heartbeat.load()};
}
API void __cdecl NfStop() {
    stopping = true; enabled = false; HideAll();
    if (worker.joinable()) worker.join();
    PipelineTrace::Save();
}

// Executes the exact compiled production shader against a synthetic texture.
// No desktop capture and no overlay is needed for this numerical test.
API int __cdecl NfTestShader(float t, float s, int curve, int rotate, int width, int height,
    const unsigned char* bgra, unsigned char* result) {
    try {
        if (!bgra || !result || width < 1 || height < 1 || width > 2048 || height > 2048) return int(E_INVALIDARG);
        Pipeline p; p.Init(nullptr, true);
        D3D11_TEXTURE2D_DESC d{};
        d.Width = width; d.Height = height; d.MipLevels = d.ArraySize = d.SampleDesc.Count = 1;
        d.Format = DXGI_FORMAT_B8G8R8A8_UNORM; d.BindFlags = D3D11_BIND_SHADER_RESOURCE;
        D3D11_SUBRESOURCE_DATA data{bgra, UINT(width * 4), 0};
        ComPtr<ID3D11Texture2D> input; Check(p.device->CreateTexture2D(&d, &data, &input));
        ComPtr<ID3D11ShaderResourceView> srv; Check(p.device->CreateShaderResourceView(input.Get(), nullptr, &srv));
        d.BindFlags = D3D11_BIND_RENDER_TARGET;
        if (rotate == 2 || rotate == 4) { d.Width = height; d.Height = width; }
        ComPtr<ID3D11Texture2D> output; Check(p.device->CreateTexture2D(&d, nullptr, &output));
        ComPtr<ID3D11RenderTargetView> rtv; Check(p.device->CreateRenderTargetView(output.Get(), nullptr, &rtv));
        Settings params{t, s, float(curve), float(rotate)};
        if(curve==9) {params.mode=1;params.regionCount=2;params.regions[0][2]=float(d.Width/4);params.regions[0][3]=float(d.Height);params.regions[1][2]=float(d.Width);params.regions[1][3]=float(d.Height);params.gains[1][0]=s;}
        if(curve==9)p.DrawMask(srv.Get(),rtv.Get(),d.Width,d.Height,params);
        else p.Draw(srv.Get(), rtv.Get(), d.Width, d.Height, params);
        d.Usage = D3D11_USAGE_STAGING; d.BindFlags = 0; d.CPUAccessFlags = D3D11_CPU_ACCESS_READ;
        ComPtr<ID3D11Texture2D> staging; Check(p.device->CreateTexture2D(&d, nullptr, &staging));
        p.context->CopyResource(staging.Get(), output.Get());
        D3D11_MAPPED_SUBRESOURCE mapped{}; Check(p.context->Map(staging.Get(), 0, D3D11_MAP_READ, 0, &mapped));
        for (UINT y = 0; y < d.Height; ++y) memcpy(result + y * d.Width * 4,
            static_cast<unsigned char*>(mapped.pData) + y * mapped.RowPitch, d.Width * 4);
        p.context->Unmap(staging.Get(), 0);
        return 0;
    } catch (HRESULT hr) { return int(hr); } catch (...) { return int(E_UNEXPECTED); }
}

API int __cdecl NfTestHdrShader(float t, float s, int curve, float white, int count, const float* rgba, float* result) {
    try {
        if (!rgba || !result || count < 1 || count > 2048 || white<=0) return int(E_INVALIDARG);
        Pipeline p; p.Init(nullptr, true);
        D3D11_TEXTURE2D_DESC d{};
        d.Width=count; d.Height=1; d.MipLevels=d.ArraySize=d.SampleDesc.Count=1;
        d.Format=DXGI_FORMAT_R32G32B32A32_FLOAT; d.BindFlags=D3D11_BIND_SHADER_RESOURCE;
        D3D11_SUBRESOURCE_DATA data{rgba,UINT(count*16),0};
        ComPtr<ID3D11Texture2D> input; Check(p.device->CreateTexture2D(&d,&data,&input));
        ComPtr<ID3D11ShaderResourceView> srv; Check(p.device->CreateShaderResourceView(input.Get(),nullptr,&srv));
        d.BindFlags=D3D11_BIND_RENDER_TARGET;
        ComPtr<ID3D11Texture2D> output; Check(p.device->CreateTexture2D(&d,nullptr,&output));
        ComPtr<ID3D11RenderTargetView> rtv; Check(p.device->CreateRenderTargetView(output.Get(),nullptr,&rtv));
        Settings settings{t,s,float(curve),1};settings.hdr=1;settings.whiteLevel=white;
        p.Draw(srv.Get(),rtv.Get(),count,1,settings);
        d.Usage=D3D11_USAGE_STAGING;d.BindFlags=0;d.CPUAccessFlags=D3D11_CPU_ACCESS_READ;
        ComPtr<ID3D11Texture2D> staging;Check(p.device->CreateTexture2D(&d,nullptr,&staging));
        p.context->CopyResource(staging.Get(),output.Get());
        D3D11_MAPPED_SUBRESOURCE mapped{};Check(p.context->Map(staging.Get(),0,D3D11_MAP_READ,0,&mapped));
        memcpy(result,mapped.pData,count*16);p.context->Unmap(staging.Get(),0);return 0;
    } catch(HRESULT hr) { return int(hr); } catch(...) { return int(E_UNEXPECTED); }
}

API void __cdecl NfTraceMark(int stage,int generation,double a,double b,double c) {PipelineTrace::Mark(stage,generation,a,b,c);}
API void __cdecl NfWindowReport(wchar_t* buffer, int length) { if(!buffer || length<1) return; std::lock_guard<std::mutex> lock(reportMutex); wcsncpy_s(buffer,length,windowReport.c_str(),_TRUNCATE); }
API void __cdecl NfGraphRead(unsigned long long after,wchar_t* buffer,int length) {GraphTimeline::Read(after,buffer,length);}
API void __cdecl NfBrowserUpdate(HWND window,int generation,double changedAt,int pending,int visible,int left,int top,int right,int bottom) {
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
API void __cdecl NfBrowserContext(HWND window,int generation) {
    std::lock_guard<std::mutex> lock(playerMutex);
    for(auto i=browserContexts.begin();i!=browserContexts.end();) {if(!IsWindow(i->first))i=browserContexts.erase(i);else ++i;}
    if(window)browserContexts[window]=generation;
}
API void __cdecl NfPlayer(HWND window,int left,int top,int right,int bottom,int generation) {
    std::lock_guard<std::mutex> lock(playerMutex);
    if(window!=playerWindow || unsigned(generation)!=playerGeneration) playerGeneration=unsigned(generation);
    playerWindow=window;playerRect={left,top,right,bottom};playerSeen=GetTickCount64();
    if(window) GetWindowRect(window,&playerWindowRect);
}
API int __cdecl NfTestResponse() {
    WindowGain video;ObserveVideoGain(video,.02f,0,false);
    ObserveVideoGain(video,.9f,.85f,false);
    if(video.current!=.85f) return 30;
    ObserveVideoGain(video,.05f,0,false);
    if(video.current!=0 || video.target!=0) return 31;
    video=WindowGain{};video.mean=.4f;video.current=.5f;video.hadSample=true;
    for(int i=0;i<240;i++) {
        float before=video.current;
        ObserveVideoGain(video,i%2?.5f:.3f,i%2?.7f:.3f,false,1000+i*8);
        if(video.current!=before) return 32;
        video.current=AdvanceVideoGain(video.current,video.target,1.f/120,75);
        if(std::abs(video.current-before)>.002f) return 33;
    }
    for(int fps : {30,60,120}) {
        float value=.8f;
        for(int i=0;i<fps;i++) value=AdvanceVideoGain(value,0,1.f/fps,75);
        if(std::abs(value-.8f*std::exp(-1.f/2.5f))>.00001f) return 34;
    }
    ObserveVideoGain(video,.5f,.1f,true);if(video.current!=.1f) return 35;
    WindowGain split;
    ObserveVideoGain(split,.8f,.8f,false,1000);
    ObserveVideoGain(split,.62f,.6f,false,1033);
    if(split.current!=.8f) return 36;
    ObserveVideoGain(split,.44f,.3f,false,1066);
    if(split.current!=.3f) return 37;
    ObserveVideoGain(split,.26f,.1f,false,1099);
    if(split.current!=.1f) return 38;
    ObserveVideoGain(split,.08f,0,false,1132);
    if(split.current!=0) return 39;
    split=WindowGain{};ObserveVideoGain(split,.05f,0,false,1000);
    ObserveVideoGain(split,.23f,.2f,false,1033);
    ObserveVideoGain(split,.41f,.6f,false,1066);
    if(split.current!=.6f) return 40;
    WindowGain stable;stable.current=stable.target=.5f;stable.mean=.4f;
    for(int i=0;i<20000;i++) ObserveWindowGain(stable,.4f,.5f+(i%2?.02f:-.02f),true,false);
    if(stable.target!=.5f || stable.current!=.5f) return 20;
    ObserveWindowGain(stable,.4f,.53f,true,false);if(std::abs(stable.target-.53f)>.00001f) return 21;
    WindowGain flash;flash.mean=.02f;
    ObserveWindowGain(flash,.9f,.8f,false,false);
    if(flash.current!=.8f || flash.target!=.8f) return 22;
    ObserveWindowGain(flash,.02f,0,true,false);if(flash.current!=.8f || flash.target!=0) return 23;
    for(float target : {.1f,.8f}) {
        float baseline=target*(1-std::exp(-.02f/.18f));
        if(std::abs(AdvanceWindowGain(0,target,.02f,50,50)-baseline)>.000001f) return 10;
        float slow=AdvanceWindowGain(0,target,.02f,0,50),fast=AdvanceWindowGain(0,target,.02f,100,50);
        if(!(slow<baseline && fast>baseline && fast<=target)) return 11;
        slow=AdvanceWindowGain(0,target,.02f,50,0);fast=AdvanceWindowGain(0,target,.02f,50,100);
        if(!(slow<baseline && fast>baseline && fast<=target)) return 12;
    }
    for(int hz : {120,60,30,12,4}) if(std::abs(AnalysisInterval(hz)*hz-1000)>.0001) return 13;
    for(int fps : {30,60,120}) {
        float value=0;
        for(int i=0;i<fps;i++) value=AdvanceWindowGain(value,.95f,1.0f/fps);
        if(value<.94f || value>.95f) return 1;
        for(int i=0;i<fps;i++) value=AdvanceWindowGain(value,0,1.0f/fps);
        if(value>.04f || value<0) return 2;
    }
    for(bool high : {false,true}) {
        int detected=-1;float value=0,target=0;double lastSample=0;
        for(int ms=0;ms<=1000;ms++) {
            if(ms-lastSample>=AnalysisInterval(high?60:30)) {lastSample=ms;target=.95f;if(detected<0)detected=ms;}
            value=AdvanceWindowGain(value,target,.001f);
            if(value<0 || value>.95f) return 3;
        }
        if(detected!=(high?17:34) || value<.93f) return 4;
    }
    return 0;
}
API void __cdecl NfTiming(int hz,int speed,int sudden) {
    analysisHz=hz==120||hz==60||hz==30||hz==12||hz==4?hz:30;
    changeSpeed=std::clamp(speed,0,100);suddenSpeed=std::clamp(sudden,0,100);
}
