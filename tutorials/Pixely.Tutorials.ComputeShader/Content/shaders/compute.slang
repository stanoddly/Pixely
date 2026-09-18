RWTexture2D<float4> outputTexture : register(u0, space1);

ConstantBuffer<float> time : register(b0, space2);

[numthreads(8, 8, 1)]
[shader("compute")]
void computeMain(uint3 dispatchThreadID : SV_DispatchThreadID)
{
    uint2 pos = dispatchThreadID.xy;
    float2 uv = float2(pos) / 512.0;

    float r = sin(uv.x * 10.0 + time) * 0.5 + 0.5;
    float g = sin(uv.y * 10.0 + time * 1.3) * 0.5 + 0.5;
    float b = sin((uv.x + uv.y) * 5.0 + time * 0.7) * 0.5 + 0.5;

    outputTexture[pos] = float4(r, g, b, 1.0);
}
