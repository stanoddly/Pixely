ConstantBuffer<float4x4> viewProjection : register(b0, space1);
ConstantBuffer<float4x4> world : register(b1, space1);
ConstantBuffer<float4> uvRect : register(b0, space3);
ConstantBuffer<float4> tintColor : register(b1, space3);
// x: 1 to average every texel a pixel covers, 0 to take the one sample under the pixel's centre.
ConstantBuffer<float4> sampling : register(b2, space3);
Texture2D<float4> texture : register(t0, space2);
SamplerState textureSampler : register(s0, space2);

// How many samples a pixel takes along each axis at most. Each is bilinear and so spans two texels,
// which keeps a texture shown up to this many times smaller free of aliasing.
static const int MaxTapsPerAxis = 8;

struct VertexInput
{
    float3 Position : TEXCOORD0;
    float2 TexCoord : TEXCOORD1;
};

struct VertexToFragment
{
    float4 Position : SV_Position;
    float2 TexCoord : TEXCOORD0;
};

[shader("vertex")]
VertexToFragment vertexMain(VertexInput input)
{
    VertexToFragment output;
    float4 worldPosition = mul(world, float4(input.Position, 1.0));
    output.Position = mul(viewProjection, worldPosition);
    output.TexCoord = input.TexCoord;
    return output;
}

float4 Premultiply(float4 color)
{
    return float4(color.rgb * color.a, color.a);
}

// Averages the texels under the pixel, which is what a mipmap chain would hand back, without one:
// a sprite is usually a region of an atlas, and a mipmap of an atlas blends neighbouring sprites
// into each other. Every sample stays half a texel inside the region for the same reason.
// Each sample is premultiplied before the samples are averaged, so a transparent sample does not
// darken the others. Within one bilinear sample the hardware still blends straight alpha.
float4 SampleFootprint(float2 uv, float2 footprint)
{
    uint width;
    uint height;
    texture.GetDimensions(width, height);
    float2 texelSize = 1.0 / float2(width, height);

    float2 low = min(uvRect.xy, uvRect.zw) + 0.5 * texelSize;
    float2 high = max(uvRect.xy, uvRect.zw) - 0.5 * texelSize;
    // A region one texel wide has no inside, so it is sampled at its centre.
    float2 middle = 0.5 * (low + high);
    low = min(low, middle);
    high = max(high, middle);

    int2 taps = clamp(int2(ceil(footprint / texelSize)), int2(1, 1), int2(MaxTapsPerAxis, MaxTapsPerAxis));
    float4 sum = float4(0.0, 0.0, 0.0, 0.0);

    for (int y = 0; y < taps.y; y++)
    {
        for (int x = 0; x < taps.x; x++)
        {
            float2 offset = ((float2(x, y) + 0.5) / float2(taps) - 0.5) * footprint;
            sum += Premultiply(texture.SampleLevel(textureSampler, clamp(uv + offset, low, high), 0.0));
        }
    }

    return sum / float(taps.x * taps.y);
}

// Every quad in the UI goes through this one shader. Solid fills are drawn with a 1x1 white
// texture, so a colour is just a tint, and the whole frame needs a single pipeline.
[shader("fragment")]
float4 fragmentMain(VertexToFragment input) : SV_Target0
{
    float2 uv = uvRect.xy + input.TexCoord * (uvRect.zw - uvRect.xy);

    // Taken before branching: derivatives are only defined where every pixel of a quad runs them.
    float2 footprint = max(abs(ddx(uv)), abs(ddy(uv)));

    // A branch rather than a conditional expression, which evaluates both sides.
    float4 color;
    if (sampling.x > 0.5)
    {
        color = SampleFootprint(uv, footprint);
    }
    else
    {
        color = Premultiply(texture.SampleLevel(textureSampler, uv, 0.0));
    }

    return color * float4(tintColor.rgb * tintColor.a, tintColor.a);
}
