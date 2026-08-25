using Pixely.Content;

namespace Pixely.Tutorials.ZipContent;

static class Program
{
    private const string ShaderPath = "shaders/.generated/tutorial.vertex.spv";

    static int Main()
    {
        using ContentSource contentSource = new ContentSourceBuilder()
            .AddZipPattern("Content.pk3")
            .AddDirectoryPattern("Content")
            .Create();
        using Stream shaderStream = contentSource.OpenStream(ShaderPath);

        Console.WriteLine($"Loaded distributed shader '{ShaderPath}' ({shaderStream.Length} bytes).");
        return 0;
    }
}
