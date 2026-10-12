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
    // normalMatrix is the cofactor matrix of world, the inverse transpose up to scale, which keeps normals perpendicular under any
    // scale or mirroring.
    float3x3 normalMatrix = (float3x3)vertexConstants.normalMatrix;
    float matrixSizeSquared = dot(normalMatrix[0], normalMatrix[0]) + dot(normalMatrix[1], normalMatrix[1]) + dot(normalMatrix[2], normalMatrix[2]);
    float3 worldNormal = directionOrFallback(mul(normalMatrix, input.Normal), matrixSizeSquared, input.Normal);
    return toFragment(worldPosition, worldNormal, input.TexCoord, vertexConstants.viewProjection);
}

[shader("fragment")]
float4 fragmentMain(VertexToFragment input) : SV_Target0
{
    float4 baseColor = baseColorTexture.Sample(baseColorSampler, input.TexCoord) * fragmentConstants.baseColorFactor;
    return shade(input, baseColor, fragmentConstants);
}
