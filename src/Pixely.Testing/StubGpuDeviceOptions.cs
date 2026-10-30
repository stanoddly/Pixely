using Pixely.Gpu;
using Pixely.Shaders;

namespace Pixely.Testing;

/// <summary>
/// What a <see cref="StubGpuDevice"/> reports it supports. By default it supports every shader format, texture format and
/// sample count, so any compiled shader loads and any texture is created; a test narrows them to check a fallback.
/// </summary>
public sealed record StubGpuDeviceOptions
{
    public static readonly StubGpuDeviceOptions Default = new();

    public ShaderFormats ShaderFormats { get; init; } = new(uint.MaxValue);

    public Func<TextureFormat, TextureType, TextureUsage, bool> IsTextureFormatSupported { get; init; } = static (_, _, _) => true;

    /// <summary>Asked only for a sample count above <see cref="SampleCount.Count1"/>, which is always supported.</summary>
    public Func<TextureFormat, SampleCount, bool> IsSampleCountSupported { get; init; } = static (_, _) => true;
}
