#pragma once
// Cached luminance readback, independent of windows and response policies.
struct LuminanceSampler {
    float luminance[160*100]{};
    bool hasLuminance=false;
    float sampledWhiteLevel=0;
    ComPtr<ID3D11Texture2D> sampleTexture,staging;
    ComPtr<ID3D11RenderTargetView> smallTarget;
    void Update(Pipeline& gpu,ID3D11ShaderResourceView* source,const Settings& s,bool frameChanged) {
        if(!sampleTexture) {
            D3D11_TEXTURE2D_DESC d{};d.Width=160;d.Height=100;d.MipLevels=d.ArraySize=d.SampleDesc.Count=1;
            d.Format=DXGI_FORMAT_R32_FLOAT;d.BindFlags=D3D11_BIND_RENDER_TARGET;
            Check(gpu.device->CreateTexture2D(&d,nullptr,&sampleTexture));
            Check(gpu.device->CreateRenderTargetView(sampleTexture.Get(),nullptr,&smallTarget));
            d.BindFlags=0;d.Usage=D3D11_USAGE_STAGING;d.CPUAccessFlags=D3D11_CPU_ACCESS_READ;
            Check(gpu.device->CreateTexture2D(&d,nullptr,&staging));
        }
        // Controls and regular response ticks can reuse the last measurement.
        // A fresh frame always gets a fresh readback before it is presented.
        if(frameChanged || !hasLuminance || sampledWhiteLevel!=s.whiteLevel) {
            Settings sample=s;sample.mode=2;for(float& v:sample.previewRect)v=0;
            gpu.Draw(source,smallTarget.Get(),160,100,sample);
            gpu.context->CopyResource(staging.Get(),sampleTexture.Get());
            double mapStart=PipelineTrace::Milliseconds();
            D3D11_MAPPED_SUBRESOURCE map{};Check(gpu.context->Map(staging.Get(),0,D3D11_MAP_READ,0,&map));
            PipelineTrace::Mark(8,0,PipelineTrace::Milliseconds()-mapStart);
            for(int y=0;y<100;y++)memcpy(luminance+y*160,static_cast<unsigned char*>(map.pData)+y*map.RowPitch,160*sizeof(float));
            gpu.context->Unmap(staging.Get(),0);hasLuminance=true;sampledWhiteLevel=s.whiteLevel;
        }
    }
};
