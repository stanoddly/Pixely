import model_common;

// The rows of a System.Numerics matrix, so a row-vector point transforms as p.x * Row0 + p.y * Row1 + p.z * Row2 + p.w * Row3.
// Four float4 rows read the same on every backend, unlike a float4x4 in a storage buffer, whose layout depends on the compiler.
struct JointMatrix
{
    float4 Row0;
    float4 Row1;
    float4 Row2;
    float4 Row3;
};

StructuredBuffer<JointMatrix> joints : register(t0, space0);
ConstantBuffer<VertexConstants> vertexConstants : register(b0, space1);
Texture2D<float4> baseColorTexture : register(t0, space2);
SamplerState baseColorSampler : register(s0, space2);
ConstantBuffer<FragmentConstants> fragmentConstants : register(b0, space3);

struct VertexInput
{
    float3 Position : TEXCOORD0;
    float2 TexCoord : TEXCOORD1;
    float3 Normal : TEXCOORD2;
    uint4 Joints : TEXCOORD3;
    float4 Weights : TEXCOORD4;
};

[shader("vertex")]
VertexToFragment vertexMain(VertexInput input)
{
    // The joint matrices place the mesh in the scene, and world places the copy being drawn; normalMatrix is not used here.
    JointMatrix skin;
    skin.Row0 = 0;
    skin.Row1 = 0;
    skin.Row2 = 0;
    skin.Row3 = 0;
    for (int i = 0; i < 4; i++)
    {
        JointMatrix joint = joints[input.Joints[i]];
        float weight = input.Weights[i];
        skin.Row0 += weight * joint.Row0;
        skin.Row1 += weight * joint.Row1;
        skin.Row2 += weight * joint.Row2;
        skin.Row3 += weight * joint.Row3;
    }

    // Each row of skin times world gives the same row of their product, so the result transforms by the joints, then by world.
    skin.Row0 = mul(vertexConstants.world, skin.Row0);
    skin.Row1 = mul(vertexConstants.world, skin.Row1);
    skin.Row2 = mul(vertexConstants.world, skin.Row2);
    skin.Row3 = mul(vertexConstants.world, skin.Row3);

    float3 worldPosition = input.Position.x * skin.Row0.xyz + input.Position.y * skin.Row1.xyz + input.Position.z * skin.Row2.xyz + skin.Row3.xyz;

    // Normals transform by the inverse transpose of the blended 3x3, which is its cofactor matrix divided by its determinant. The
    // rows of the cofactor matrix are these cross products. Multiplying by the sign of the determinant instead of dividing keeps
    // normals pointing outward under mirroring; the length does not matter because the result is normalized.
    float3 r0 = skin.Row0.xyz;
    float3 r1 = skin.Row1.xyz;
    float3 r2 = skin.Row2.xyz;
    float3 c0 = cross(r1, r2);
    float3 c1 = cross(r2, r0);
    float3 c2 = cross(r0, r1);
    float determinant = dot(r0, c0);
    float3 normal = (input.Normal.x * c0 + input.Normal.y * c1 + input.Normal.z * c2) * (determinant < 0.0 ? -1.0 : 1.0);
    float3 worldNormal = directionOrFallback(normal, dot(c0, c0) + dot(c1, c1) + dot(c2, c2), input.Normal);

    return toFragment(worldPosition, worldNormal, input.TexCoord, vertexConstants.viewProjection);
}

[shader("fragment")]
float4 fragmentMain(VertexToFragment input) : SV_Target0
{
    float4 baseColor = baseColorTexture.Sample(baseColorSampler, input.TexCoord) * fragmentConstants.baseColorFactor;
    return shade(input, baseColor, fragmentConstants);
}
