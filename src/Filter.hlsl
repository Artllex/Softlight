// Uniform masks and synchronized, already-dimmed frames.
Texture2D desktopImage : register(t0);
SamplerState pointSampler : register(s0);
cbuffer Settings : register(b0) {
    float threshold;
    float strength;
    float curve;
    float rotation;
    float4 previewRect;
    float hdr;
    float whiteLevel;
    float mode;
    float regionCount;
    float4 regions[64];
    float4 gains[64];
};
struct Vertex { float4 position : SV_POSITION; float2 uv : TEXCOORD0; };
Vertex VS(uint id : SV_VertexID) {
    Vertex v;
    v.uv = float2((id << 1) & 2, id & 2);
    v.position = float4(v.uv * float2(2, -2) + float2(-1, 1), 0, 1);
    return v;
}
float Encode(float value) {
    return value <= 0.0031308 ? value * 12.92 : 1.055 * pow(max(value, 0), 1.0/2.4) - 0.055;
}
float Decode(float value) {
    return value <= 0.04045 ? value / 12.92 : pow(max(0, (value + 0.055) / 1.055), 2.4);
}
float3 SourceColor(Vertex v) {
    float2 uv=v.uv;
    if(rotation==2)uv=float2(v.uv.y,1-v.uv.x);
    if(rotation==3)uv=1-v.uv;
    if(rotation==4)uv=float2(1-v.uv.y,v.uv.x);
    return desktopImage.SampleLevel(pointSampler,uv,0).rgb;
}
float4 PS(Vertex v) : SV_TARGET {
    if(mode==3)return float4(SourceColor(v)*(1-strength),1);
    if (mode == 1) {
        [loop] for (int i=0; i<(int)regionCount; ++i) {
            float4 r=regions[i];
            if (v.position.x>=r.x && v.position.y>=r.y && v.position.x<r.z && v.position.y<r.w)
                return gains[i].y>0 ? float4(SourceColor(v)*(1-gains[i].x),1) : float4(0,0,0,gains[i].x);
        }
        return 0;
    }
    if (v.position.x >= previewRect.x && v.position.y >= previewRect.y &&
        v.position.x < previewRect.z && v.position.y < previewRect.w) return 0;
    float3 rgb=SourceColor(v);
    if (mode == 2) return float4(clamp(dot(rgb,float3(.2126,.7152,.0722))/(hdr>0?whiteLevel:1),0,4),0,0,1);
    float luminance = dot(rgb, float3(0.2126, 0.7152, 0.0722));
    float relativeLinear = max(luminance, 0) / max(whiteLevel, 0.0001);
    float y = hdr > 0 ? Encode(relativeLinear) : luminance;
    if (y <= threshold || strength <= 0) return 0;
    // Do not clip HDR values at SDR white: retain ordering of highlights.
    float x = max(0, (y - threshold) / max(1 - threshold, 0.0001));
    float straight = x * (1 - strength);
    float soft = x / (1 + (strength / max(1 - strength, 0.0001)) * x);
    float mapped = threshold + (1 - threshold) * lerp(straight, soft, curve);
    float ratio = hdr > 0 ? Decode(mapped) / max(relativeLinear, 0.0001) : mapped / max(y, 0.0001);
    return float4(0, 0, 0, saturate(1 - ratio));
}
