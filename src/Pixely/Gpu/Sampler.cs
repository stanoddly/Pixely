using Pixely.Utilities;
using XdlSharp;
using static XdlSharp.Xdl;

namespace Pixely.Gpu;

public class Sampler: IDisposable
{
    private readonly GpuDevice _gpuDevice;
    internal Pointer<SDL_GPUSampler> Pointer { get; set; }

    internal Sampler(GpuDevice gpuDevice, Pointer<SDL_GPUSampler> pointer)
    {
        _gpuDevice = gpuDevice;
        Pointer = pointer;
    }
    
    public void Dispose()
    {
        _gpuDevice.ReleaseSampler(this);
    }
}

public enum SamplerAddressMode: byte
{
    Repeat = SDL_GPUSamplerAddressMode.SDL_GPU_SAMPLERADDRESSMODE_REPEAT,
    MirroredRepeat = SDL_GPUSamplerAddressMode.SDL_GPU_SAMPLERADDRESSMODE_MIRRORED_REPEAT,
    ClampToEdge = SDL_GPUSamplerAddressMode.SDL_GPU_SAMPLERADDRESSMODE_CLAMP_TO_EDGE
}

public readonly struct SamplerAddressModes
{
    public SamplerAddressMode U { get; }
    public SamplerAddressMode V { get; }
    public SamplerAddressMode W { get; }

    public SamplerAddressModes(SamplerAddressMode all)
    {
        U = all;
        V = all;
        W = all;
    }
    
    public SamplerAddressModes(SamplerAddressMode u, SamplerAddressMode v, SamplerAddressMode w)
    {
        U = u;
        V = v;
        W = w;
    }
    
    public static readonly SamplerAddressModes Repeat = new SamplerAddressModes(SamplerAddressMode.Repeat);
    public static readonly SamplerAddressModes MirroredRepeat = new SamplerAddressModes(SamplerAddressMode.MirroredRepeat);
    public static readonly SamplerAddressModes ClampToEdge = new SamplerAddressModes(SamplerAddressMode.ClampToEdge);
}

public enum TextureFilter
{
    Nearest=SDL_GPUFilter.SDL_GPU_FILTER_NEAREST,
    Linear=SDL_GPUFilter.SDL_GPU_FILTER_LINEAR,
}

public enum SamplerMipmapMode : byte
{
    Nearest=SDL_GPUSamplerMipmapMode.SDL_GPU_SAMPLERMIPMAPMODE_NEAREST,
    Linear=SDL_GPUSamplerMipmapMode.SDL_GPU_SAMPLERMIPMAPMODE_LINEAR
}

public record SamplerConfig(
    TextureFilter MinFilter = default,
    TextureFilter MagFilter = default,
    SamplerMipmapMode MipmapMode = default,
    SamplerAddressMode AddressModeU = default,
    SamplerAddressMode AddressModeV = default,
    SamplerAddressMode AddressModeW = default,
    float MipLodBias = default,
    float MinLod = default,
    float MaxLod = default,
    float MaxAnisotropy = default,
    bool EnableAnisotropy = default,
    CompareOperation CompareOp = default,
    bool EnableCompare = default
)
{
    public static readonly SamplerConfig PixelArt = new(
        MinFilter: TextureFilter.Nearest,
        MagFilter: TextureFilter.Nearest,
        MipmapMode: SamplerMipmapMode.Nearest,
        AddressModeU: SamplerAddressMode.ClampToEdge,
        AddressModeV: SamplerAddressMode.ClampToEdge,
        AddressModeW: SamplerAddressMode.ClampToEdge
    );

    public static readonly SamplerConfig Linear = new(
        MinFilter: TextureFilter.Linear,
        MagFilter: TextureFilter.Linear,
        MipmapMode: SamplerMipmapMode.Linear,
        AddressModeU: SamplerAddressMode.ClampToEdge,
        AddressModeV: SamplerAddressMode.ClampToEdge,
        AddressModeW: SamplerAddressMode.ClampToEdge
    );
}