namespace Pixely.SdlangCompiler.Tests;

public class SdlangCompileTaskTests
{
    [Test]
    public void CompiledShaderOutputExists()
    {
        string outputDir = Path.Combine(TestContext.CurrentContext.TestDirectory, "TestShaders", ".generated");

        // The shader file should be compiled by the build process
        // This test verifies that the compiled output file exists in the .generated/ subdirectory
        for (int i = 1; i < 3; i++)
        {
            string generatedVertexShaderPath = Path.Combine(outputDir, $"test{i}.vertex.spv");
            string generatedVertexDxilPath = Path.Combine(outputDir, $"test{i}.vertex.dxil");
            string generatedFragmentShaderPath = Path.Combine(outputDir, $"test{i}.fragment.spv");
            string generatedFragmentDxilPath = Path.Combine(outputDir, $"test{i}.fragment.dxil");
            string metadataPath = Path.Combine(outputDir, $"test{i}.metadata.json");

            Assert.That(File.Exists(generatedVertexShaderPath), Is.True, "Compiled shader output file should exist at: " + generatedVertexShaderPath);
            Assert.That(File.Exists(generatedVertexDxilPath), Is.True, "Compiled shader output file should exist at: " + generatedVertexDxilPath);
            Assert.That(File.Exists(generatedFragmentShaderPath), Is.True, "Compiled shader output file should exist at: " + generatedFragmentShaderPath);
            Assert.That(File.Exists(generatedFragmentDxilPath), Is.True, "Compiled shader output file should exist at: " + generatedFragmentDxilPath);
            Assert.That(File.Exists(metadataPath), Is.True, "Compiled shader output file should exist at: " + metadataPath);
        }
    }
}
