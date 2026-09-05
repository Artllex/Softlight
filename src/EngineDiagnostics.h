#pragma once
// Explicit diagnostics only. These methods are not part of ordinary frame analysis.
unsigned int Monitor::ReadPixel(ID3D11Texture2D* texture, UINT x, UINT y) {
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
void Monitor::Probe() {
    int version = probeRequest.load();
    if (!capture.hasFrame || version == probeDone.load()) return;
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
    gpu.DrawRegions(capture.sourceView.Get(),target.Get(),width,height,previous);
    probeInput = ReadPixel(capture.source.Get(), sx, sy);
    ComPtr<ID3D11Resource> targetResource; target->GetResource(&targetResource);
    ComPtr<ID3D11Texture2D> targetTexture; Check(targetResource.As(&targetTexture));
    probeMask = ReadPixel(targetTexture.Get(), x, y);
    if (probeComposite.load()) {
        // Discard pending pre-probe frames, including deliberately held
        // captures. The diagnostic must read the composition after affinity changes.
        for(int i=0;i<8;i++) {
            DXGI_OUTDUPL_FRAME_INFO oldInfo{};ComPtr<IDXGIResource> oldFrame;
            HRESULT oldResult=capture.duplication->AcquireNextFrame(0,&oldInfo,&oldFrame);
            if(oldResult==DXGI_ERROR_WAIT_TIMEOUT)break;
            Check(oldResult);Check(capture.duplication->ReleaseFrame());
        }
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
            Check(capture.duplication->AcquireNextFrame(500, &frame, &resource));
            try {
                ComPtr<ID3D11Texture2D> texture; Check(resource.As(&texture));
                unsigned int bgra=ReadPixel(texture.Get(),sx,sy);
                color=RGB((bgra>>16)&255,(bgra>>8)&255,bgra&255);
            } catch(...) { capture.duplication->ReleaseFrame();throw; }
            Check(capture.duplication->ReleaseFrame());
        }
        SetWindowDisplayAffinity(window, WDA_EXCLUDEFROMCAPTURE);
        probeDisplay = color;
        if(!testHoldCapture.load())rebuild = true;
    }
    probeDone = version;
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
        if(curve==9 || curve==10) {params.mode=1;params.regionCount=2;params.regions[0][2]=float(d.Width/4);params.regions[0][3]=float(d.Height);params.regions[1][2]=float(d.Width);params.regions[1][3]=float(d.Height);params.gains[1][0]=s;params.gains[1][1]=curve==10?1.f:0.f;}
        if(curve==9 || curve==10)p.DrawRegions(srv.Get(),rtv.Get(),d.Width,d.Height,params);
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
        Settings settings{t,s,float(curve),1};settings.hdr=1;settings.whiteLevel=white;if(curve==10)settings.mode=3;
        p.Draw(srv.Get(),rtv.Get(),count,1,settings);
        d.Usage=D3D11_USAGE_STAGING;d.BindFlags=0;d.CPUAccessFlags=D3D11_CPU_ACCESS_READ;
        ComPtr<ID3D11Texture2D> staging;Check(p.device->CreateTexture2D(&d,nullptr,&staging));
        p.context->CopyResource(staging.Get(),output.Get());
        D3D11_MAPPED_SUBRESOURCE mapped{};Check(p.context->Map(staging.Get(),0,D3D11_MAP_READ,0,&mapped));
        memcpy(result,mapped.pData,count*16);p.context->Unmap(staging.Get(),0);return 0;
    } catch(HRESULT hr) { return int(hr); } catch(...) { return int(E_UNEXPECTED); }
}

API int __cdecl NfTestResponse() {
    WindowGain exposed;exposed.holdUntil=1250;
    ObserveWindowGain(exposed,.9f,.8f,false,false,1000);
    if(exposed.current!=.8f)return 70;
    ObserveWindowGain(exposed,.1f,0,true,false,1010);
    if(exposed.current!=.8f || exposed.target!=.8f)return 71;
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

API void __cdecl NfTestHoldCapture(int value) {testHoldCapture=value!=0;}
API int __cdecl NfProbe(int x, int y, int composite) {
    probeX = x; probeY = y; probeComposite = composite;
    return ++probeRequest;
}
API int __cdecl NfProbeResult(unsigned int* input, unsigned int* mask, unsigned int* display) {
    int done = probeDone.load();
    *input = probeInput.load(); *mask = probeMask.load(); *display = probeDisplay.load();
    return done;
}
