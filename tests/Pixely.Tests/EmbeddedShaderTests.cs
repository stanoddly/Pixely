using Pixely.Content;
using Pixely.Ui;

namespace Pixely.Tests;

public class EmbeddedShaderTests
{
    [TestCase("shaders/.generated/ui_quad.metadata.json")]
    [TestCase("shaders/.generated/ui_quad.vertex.spv")]
    [TestCase("shaders/.generated/ui_quad.fragment.spv")]
    [TestCase("shaders/.generated/ui_present.metadata.json")]
    [TestCase("shaders/.generated/ui_present.vertex.spv")]
    [TestCase("shaders/.generated/ui_present.fragment.spv")]
    public void UiGeneratedShader_CanBeOpened(string path)
    {
        using ContentSource contentSource = EmbeddedContentSource.Create(typeof(UiExtensions).Assembly);

        using Stream stream = contentSource.OpenStream(path);

        Assert.That(stream.Length, Is.GreaterThan(0));
    }
}
