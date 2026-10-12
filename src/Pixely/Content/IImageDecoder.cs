namespace Pixely.Content;

/// <summary>Decodes an encoded image, such as PNG or JPEG bytes, that is already in memory.</summary>
/// <remarks>
/// The result holds tightly packed R, G, B, A bytes (<see cref="Gpu.PixelFormat.Abgr8888"/>), and its width and height are not zero.
/// Decoding does not limit memory: a small encoded image can declare a size that takes gigabytes to decode, and the size is checked
/// only after decoding. A caller that decodes untrusted images reads their size from the header first, as Pixely.Models does.
/// </remarks>
public interface IImageDecoder
{
    Image Decode(ReadOnlySpan<byte> encodedImage);
}
