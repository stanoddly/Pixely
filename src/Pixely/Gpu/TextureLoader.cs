using Pixely.Content;

namespace Pixely.Gpu;

public interface ITextureLoader
{
    Texture Load(string path);
    Texture Load(Image image);
}

public class TextureLoader : ITextureLoader
{
    private readonly IImageLoader _imageLoader;
    private readonly GpuMemorySystem _gpuMemorySystem;

    public TextureLoader(IImageLoader imageLoader, GpuMemorySystem gpuMemorySystem)
    {
        _imageLoader = imageLoader;
        _gpuMemorySystem = gpuMemorySystem;
    }

    public Texture Load(string path)
    {
        using Image image = _imageLoader.Load(path);
        return Load(image);
    }

    public Texture Load(Image image)
    {
        return _gpuMemorySystem.CreateTexture(image);
    }
}
