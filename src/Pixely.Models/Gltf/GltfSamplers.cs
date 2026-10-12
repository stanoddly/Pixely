using Pixely.Gpu;

namespace Pixely.Models.Gltf;

// Maps glTF samplers (glTF 2.0, section 5.26) to SamplerConfig. Textures have one mip level, so mipmap filters use their base filter.
internal static class GltfSamplers
{
    private const int Nearest = 9728;
    private const int Linear = 9729;
    private const int NearestMipmapNearest = 9984;
    private const int LinearMipmapNearest = 9985;
    private const int NearestMipmapLinear = 9986;
    private const int LinearMipmapLinear = 9987;
    private const int ClampToEdge = 33071;
    private const int MirroredRepeat = 33648;
    private const int Repeat = 10497;

    // A texture without a sampler repeats and filters linearly.
    public static SamplerConfig Default { get; } = new(
        MinFilter: TextureFilter.Linear,
        MagFilter: TextureFilter.Linear,
        MipmapMode: SamplerMipmapMode.Nearest,
        AddressModeU: SamplerAddressMode.Repeat,
        AddressModeV: SamplerAddressMode.Repeat,
        AddressModeW: SamplerAddressMode.ClampToEdge);

    public static SamplerConfig ToSamplerConfig(GltfSamplerDto sampler, string name)
    {
        return Default with
        {
            MagFilter = sampler.MagFilter switch
            {
                null or Linear => TextureFilter.Linear,
                Nearest => TextureFilter.Nearest,
                _ => throw new InvalidDataException($"{name}: magFilter {sampler.MagFilter} is not a glTF magnification filter.")
            },
            MinFilter = sampler.MinFilter switch
            {
                null or Linear or LinearMipmapNearest or LinearMipmapLinear => TextureFilter.Linear,
                Nearest or NearestMipmapNearest or NearestMipmapLinear => TextureFilter.Nearest,
                _ => throw new InvalidDataException($"{name}: minFilter {sampler.MinFilter} is not a glTF minification filter.")
            },
            AddressModeU = ToAddressMode(sampler.WrapS, name, "wrapS"),
            AddressModeV = ToAddressMode(sampler.WrapT, name, "wrapT")
        };
    }

    private static SamplerAddressMode ToAddressMode(int? wrap, string name, string property) => wrap switch
    {
        null or Repeat => SamplerAddressMode.Repeat,
        ClampToEdge => SamplerAddressMode.ClampToEdge,
        MirroredRepeat => SamplerAddressMode.MirroredRepeat,
        _ => throw new InvalidDataException($"{name}: {property} {wrap} is not a glTF wrap mode.")
    };
}
