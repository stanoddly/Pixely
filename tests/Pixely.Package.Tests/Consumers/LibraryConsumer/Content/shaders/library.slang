struct VertexInput
{
    float3 Position : POSITION;
};

struct VertexToFragment
{
    float4 Position : SV_Position;
};

[shader("vertex")]
VertexToFragment vertexMain(VertexInput input)
{
    VertexToFragment output;
    output.Position = float4(input.Position, 1.0);
    return output;
}

[shader("fragment")]
float4 fragmentMain(VertexToFragment input) : SV_Target0
{
    return float4(1.0, 0.0, 1.0, 1.0);
}
