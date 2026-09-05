#pragma once
// Own the unmodified captured frame and its presentation timestamp together.
struct DesktopCapture {
    ComPtr<IDXGIOutputDuplication> duplication;
    ComPtr<ID3D11Texture2D> source;
    ComPtr<ID3D11ShaderResourceView> sourceView;
    bool hasFrame=false;
    double captureTime=0;
    void Init(Pipeline& gpu,IDXGIOutput* output,bool hdr) {
        if (hdr) {
            ComPtr<IDXGIOutput5> output5; Check(output->QueryInterface(IID_PPV_ARGS(&output5)));
            DXGI_FORMAT formats[] = {DXGI_FORMAT_R16G16B16A16_FLOAT, DXGI_FORMAT_B8G8R8A8_UNORM};
            Check(output5->DuplicateOutput1(gpu.device.Get(), 0, 2, formats, &duplication));
        } else {
            ComPtr<IDXGIOutput1> output1; Check(output->QueryInterface(IID_PPV_ARGS(&output1)));
            Check(output1->DuplicateOutput(gpu.device.Get(), &duplication));
        }
    }
    bool Acquire(Pipeline& gpu,bool hdr,UINT timeout,bool hold) {
        DXGI_OUTDUPL_FRAME_INFO info{};
        ComPtr<IDXGIResource> resource;
        HRESULT hr = hold?DXGI_ERROR_WAIT_TIMEOUT:duplication->AcquireNextFrame(timeout, &info, &resource);
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
        return changed;
    }
};
