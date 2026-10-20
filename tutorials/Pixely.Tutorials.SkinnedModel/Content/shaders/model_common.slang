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
    float padding0;
    float2 padding1;
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

// A transformed normal far shorter than its matrix has no reliable direction, such as under a node collapsed to zero on an axis;
// the untransformed normal stands in then. matrixSizeSquared is the sum of the matrix's squared entries. The threshold is relative to
// it, because a cofactor matrix scales with the square of the model's scale, so a fixed threshold would reject small models.
float3 directionOrFallback(float3 transformed, float matrixSizeSquared, float3 fallback)
{
    return dot(transformed, transformed) > matrixSizeSquared * 1e-12 ? transformed : fallback;
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
    // Linear light: the sRGB swapchain encodes it on write.
    float3 color = baseColor.rgb * (0.25 + 0.75 * diffuse);
    return float4(color, baseColor.a);
}
