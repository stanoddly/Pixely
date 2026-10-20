struct VertexInput
{
    float4 Position : TEXCOORD0;
    float4 Color : TEXCOORD1;
};

struct VertexToFragment
{
    float4 Position : SV_Position;
    float4 Color : TEXCOORD0;
};

// Vertex colors are sRGB-encoded bytes. Decoding them here, once per vertex, gives the linear light the rest of the pipeline works in.
float3 decodeSrgb(float3 encoded)
{
    float3 low = encoded / 12.92;
    float3 high = pow((encoded + 0.055) / 1.055, 2.4);
    return select(encoded <= 0.04045, low, high);
}

[shader("vertex")]
VertexToFragment vertexMain(VertexInput input)
{
    VertexToFragment output;
    output.Position = input.Position;
    output.Color = float4(decodeSrgb(input.Color.rgb), input.Color.a);
    return output;
}

[shader("fragment")]
float4 fragmentMain(VertexToFragment input) : SV_Target0
{
    return input.Color;
}
