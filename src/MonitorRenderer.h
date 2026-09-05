#pragma once
// Per-monitor composition lifecycle: acquire, analyze, present, then publish.
struct Monitor {
    WindowAnalyzer analyzer;
    Pipeline gpu;
    DesktopCapture capture;
    ComPtr<IDXGISwapChain1> swapchain;
    ComPtr<ID3D11RenderTargetView> target;

    ComPtr<IDCompositionDevice> composition;
    ComPtr<IDCompositionTarget> compositionTarget;
    ComPtr<IDCompositionVisual> visual;
    HWND window = nullptr;
    DXGI_OUTPUT_DESC desc{};
    UINT width = 0, height = 0;
    bool hdr = false;
    float whiteLevel = 1;
    ULONGLONG whiteChecked = 0;
    Settings previous{};
    ULONGLONG zOrderTime = 0;
    unsigned int ReadPixel(ID3D11Texture2D* texture,UINT x,UINT y);
    void Probe();
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
        capture.Init(gpu,output,hdr);
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
        double acquireStart=PipelineTrace::Milliseconds();
        bool changed=capture.Acquire(gpu,hdr,monitorCount.load()==1?4:0,testHoldCapture.load());
        PipelineTrace::Mark(1,0,changed?1:0,PipelineTrace::Milliseconds()-acquireStart,(GraphTimeline::Clock()-capture.captureTime)*1000);
        double analysisStart=PipelineTrace::Milliseconds();
        if (capture.hasFrame) analyzer.Update(gpu, capture.sourceView.Get(), width, height, desc.DesktopCoordinates, s, changed,capture.captureTime);
        double analysisEnd=PipelineTrace::Milliseconds();
        bool copyFrame=false;for(int i=0;i<int(s.regionCount);i++)copyFrame=copyFrame || s.gains[i][1]>0;
        if (capture.hasFrame && ((changed && copyFrame) || memcmp(&s, &previous, sizeof(s)) != 0 || !IsWindowVisible(window))) {
            gpu.DrawRegions(capture.sourceView.Get(),target.Get(),width,height,s);
            Check(swapchain->Present(0, 0));
            PipelineTrace::Mark(3,0,analysisEnd-analysisStart,PipelineTrace::Milliseconds()-analysisEnd);
            previous = s;
            ++frames;
            if (enabled && !stopping && !IsWindowVisible(window)) ShowWindow(window, SW_SHOWNOACTIVATE);
        }
        if(capture.hasFrame)analyzer.Publish();
        // Taskbar flyouts and other topmost windows can otherwise escape the mask.
        if (capture.hasFrame && enabled && GetTickCount64() - zOrderTime > 1000) {
            SetWindowPos(window, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
            zOrderTime = GetTickCount64();
        }
        Probe();
        return capture.hasFrame;
    }
};

