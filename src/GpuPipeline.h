#pragma once
// GPU resources and the two region presentation paths; no desktop acquisition.
struct Pipeline {
    ComPtr<ID3D11Device> device;
    ComPtr<ID3D11DeviceContext> context;
    ComPtr<ID3D11DeviceContext1> clearContext;
    ComPtr<ID3D11VertexShader> vs;
    ComPtr<ID3D11PixelShader> ps;
    ComPtr<ID3D11Buffer> constants;
    ComPtr<ID3D11SamplerState> sampler;
    ComPtr<ID3D11RasterizerState> scissor;
    void Init(IDXGIAdapter* adapter, bool warp = false) {
        D3D_FEATURE_LEVEL levels[] = {D3D_FEATURE_LEVEL_11_0, D3D_FEATURE_LEVEL_10_1};
        Check(D3D11CreateDevice(adapter, warp ? D3D_DRIVER_TYPE_WARP : D3D_DRIVER_TYPE_UNKNOWN,
            nullptr, D3D11_CREATE_DEVICE_BGRA_SUPPORT, levels, 2, D3D11_SDK_VERSION,
            &device, nullptr, &context));
        context.As(&clearContext);
        ComPtr<IDXGIDevice1> dxgi; if(SUCCEEDED(device.As(&dxgi)))dxgi->SetMaximumFrameLatency(1);
        D3D11_RASTERIZER_DESC raster{};raster.FillMode=D3D11_FILL_SOLID;raster.CullMode=D3D11_CULL_NONE;raster.DepthClipEnable=TRUE;raster.ScissorEnable=TRUE;
        Check(device->CreateRasterizerState(&raster,&scissor));
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
    void DrawRegions(ID3D11ShaderResourceView* input,ID3D11RenderTargetView* target,UINT width,UINT height,const Settings& s) {
        if(!clearContext) {Draw(input,target,width,height,s);return;}
        float color[4]{};context->ClearRenderTargetView(target,color);
        // Draw only visible fragments. Replaying every covered background window
        // otherwise copies the same full-resolution desktop several times.
        for(int i=0;i<int(s.regionCount);++i) {
            RECT r{LONG(s.regions[i][0]),LONG(s.regions[i][1]),LONG(s.regions[i][2]),LONG(s.regions[i][3])};
            if(s.gains[i][1]<=0 && s.gains[i][0]<=0)continue;
            std::vector<RECT> visible{r};
            for(int j=0;j<i && !visible.empty();++j) {
                RECT front{LONG(s.regions[j][0]),LONG(s.regions[j][1]),LONG(s.regions[j][2]),LONG(s.regions[j][3])};
                std::vector<RECT> next;
                for(auto& v:visible) {
                    RECT overlap{};
                    if(!IntersectRect(&overlap,&v,&front)){next.push_back(v);continue;}
                    if(v.top<overlap.top)next.push_back({v.left,v.top,v.right,overlap.top});
                    if(overlap.bottom<v.bottom)next.push_back({v.left,overlap.bottom,v.right,v.bottom});
                    if(v.left<overlap.left)next.push_back({v.left,overlap.top,overlap.left,overlap.bottom});
                    if(overlap.right<v.right)next.push_back({overlap.right,overlap.top,v.right,overlap.bottom});
                }
                visible=std::move(next);
            }
            if(visible.empty())continue;
            if(s.gains[i][1]>0)DrawProtected(input,target,width,height,s,s.gains[i][0],visible);
            else DrawOverlay(target,s.gains[i][0],visible);
        }
    }
    void DrawProtected(ID3D11ShaderResourceView* input,ID3D11RenderTargetView* target,UINT width,UINT height,const Settings& s,float gain,const std::vector<RECT>& visible) {
        Settings copy=s;copy.mode=3;copy.strength=gain;
        context->RSSetState(scissor.Get());
        for(auto& v:visible){context->RSSetScissorRects(1,&v);Draw(input,target,width,height,copy);}
        context->RSSetState(nullptr);
    }
    void DrawOverlay(ID3D11RenderTargetView* target,float gain,const std::vector<RECT>& visible) {
        float color[4]{0,0,0,gain};
        clearContext->ClearView(target,color,visible.data(),UINT(visible.size()));
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

