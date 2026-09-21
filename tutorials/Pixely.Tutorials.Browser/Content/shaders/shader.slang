ConstantBuffer<float4> color : register(b0, space3);

struct VertexInput
{
    float4 Position : TEXCOORD0;
};

struct VertexToFragment
{
    float4 Position : SV_Position;
};

[shader("vertex")]
VertexToFragment vertexMain(VertexInput input)
{
    VertexToFragment output;
    output.Position = input.Position;
    return output;
}

[shader("fragment")]
float4 fragmentMain(VertexToFragment input) : SV_Target0
{
    return color;
}
