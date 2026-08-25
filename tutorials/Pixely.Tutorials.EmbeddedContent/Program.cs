using Pixely.Content;

namespace Pixely.Tutorials.EmbeddedContent;

static class Program
{
    private const string ShaderPath = "shaders/nested/.generated/tutorial.vertex.spv";

    static int Main()
    {
        using ContentSource contentSource = EmbeddedContentSource.Create(typeof(Program).Assembly);
        using Stream shaderStream = contentSource.OpenStream(ShaderPath);

        Console.WriteLine($"Loaded embedded shader '{ShaderPath}' ({shaderStream.Length} bytes).");
        return 0;
    }
}
