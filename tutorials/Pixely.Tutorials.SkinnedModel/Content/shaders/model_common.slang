// Shared by model_static.slang and model_skinned.slang.

// Matrices are System.Numerics matrices, which use row vectors. Uploaded as they are, the shader reads each one transposed, so
// mul(matrix, column) gives the same result as row * matrix in C#.
struct VertexConstants
{
    float4x4 viewProjection;
    float4x4 world;
    float4x4 normalMatrix;
};

struct FragmentConstants
{
    float4 baseColorFactor;
    // xyz: the direction the light travels, in world space.
    float4 lightDirection;
    float4 cameraPosition;
    // Fragments with less alpha are discarded; negative for materials that are not alpha-masked.
    float alphaCutoff;
    uint encodeSrgb;
    uint2 padding;
};

struct VertexToFragment
{
    float4 Position : SV_Position;
    float3 WorldPosition : TEXCOORD0;
    float3 Normal : TEXCOORD1;
    float2 TexCoord : TEXCOORD2;
};

VertexToFragment toFragment(float3 worldPosition, float3 worldNormal, float2 texCoord, float4x4 viewProjection)
{
    VertexToFragment output;
    output.Position = mul(viewProjection, float4(worldPosition, 1.0));
    output.WorldPosition = worldPosition;
    output.Normal = worldNormal;
    output.TexCoord = texCoord;
    return output;
}

float3 encodeSrgb(float3 linear)
{
    float3 low = linear * 12.92;
    float3 high = 1.055 * pow(max(linear, 0.0031308), 1.0 / 2.4) - 0.055;
    return select(linear <= 0.0031308, low, high);
}

float4 shade(VertexToFragment input, float4 baseColor, FragmentConstants constants)
{
    if (baseColor.a < constants.alphaCutoff)
    {
        discard;
    }

    float3 normal = normalize(input.Normal);
    // Light the side that faces the camera. This lights the back of a double-sided face, and it needs no winding, so a mirrored
    // model needs no flag.
    if (dot(normal, constants.cameraPosition.xyz - input.WorldPosition) < 0.0)
    {
        normal = -normal;
    }

    float diffuse = max(dot(normal, -normalize(constants.lightDirection.xyz)), 0.0);
    float3 color = baseColor.rgb * (0.25 + 0.75 * diffuse);
    if (constants.encodeSrgb != 0)
    {
        color = encodeSrgb(color);
    }

    return float4(color, baseColor.a);
}
