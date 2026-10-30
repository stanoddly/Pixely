using Pixely.Content;

namespace Pixely.Headless;

public interface IImageWriter
{
    void SavePng(Image image, string path);
}
