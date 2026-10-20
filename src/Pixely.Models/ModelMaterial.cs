using System.Numerics;
using Pixely.Gpu;

namespace Pixely.Models;

public enum AlphaMode
{
    Opaque,
    Mask,
    Blend
}

/// <summary>A texture with the sampler settings the file asks for. The texture is <see cref="TextureFormat.R8G8B8A8UnormSrgb"/>; the consumer creates and owns the <see cref="Sampler"/>.</summary>
public sealed record ModelTexture(Texture Texture, SamplerConfig Sampler);

/// <summary>The base color part of a glTF metallic-roughness material. <see cref="BaseColorFactor"/> is linear RGBA.</summary>
public sealed record ModelMaterial(string? Name, LinearColor BaseColorFactor, ModelTexture? BaseColorTexture, AlphaMode AlphaMode, float AlphaCutoff, bool DoubleSided)
{
    /// <summary>The material glTF uses for a primitive without one.</summary>
    public static ModelMaterial Default { get; } = new(null, new LinearColor(1, 1, 1, 1), null, AlphaMode.Opaque, 0.5f, false);
}
