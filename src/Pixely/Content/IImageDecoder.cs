namespace Pixely.Content;

/// <summary>Decodes an encoded image, such as PNG or JPEG bytes, that is already in memory.</summary>
/// <remarks>The result holds tightly packed R, G, B, A bytes (<see cref="Gpu.PixelFormat.Abgr8888"/>), and its width and height are not zero.</remarks>
public interface IImageDecoder
{
    Image Decode(ReadOnlySpan<byte> encodedImage);
}
