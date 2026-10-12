import model_common;

ConstantBuffer<VertexConstants> vertexConstants : register(b0, space1);
Texture2D<float4> baseColorTexture : register(t0, space2);
SamplerState baseColorSampler : register(s0, space2);
ConstantBuffer<FragmentConstants> fragmentConstants : register(b0, space3);

struct VertexInput
{
    float3 Position : TEXCOORD0;
    float2 TexCoord : TEXCOORD1;
    float3 Normal : TEXCOORD2;
};

[shader("vertex")]
VertexToFragment vertexMain(VertexInput input)
{
    float3 worldPosition = mul(vertexConstants.world, float4(input.Position, 1.0)).xyz;
    // normalMatrix is the inverse transpose of world, which keeps normals perpendicular under any scale or mirroring.
    float3 worldNormal = mul((float3x3)vertexConstants.normalMatrix, input.Normal);
    return toFragment(worldPosition, worldNormal, input.TexCoord, vertexConstants.viewProjection);
}

[shader("fragment")]
float4 fragmentMain(VertexToFragment input) : SV_Target0
{
    float4 baseColor = baseColorTexture.Sample(baseColorSampler, input.TexCoord) * fragmentConstants.baseColorFactor;
    return shade(input, baseColor, fragmentConstants);
}
