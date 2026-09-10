using System.Text.Json;
using Pixely.ShaderCommon;

namespace Pixely.SdlangCompiler.Tests;

public sealed class GraphicsShaderProgramCompilerTests
{
    private const string ValidProgram = """
                                                struct VertexInput
                                                {
                                                    float3 Position : POSITION;
                                                };

                                                struct VertexToFragment
                                                {
                                                    float4 Position : SV_Position;
                                                    float2 TexCoord : TEXCOORD0;
                                                };

                                                [shader("vertex")]
                                                VertexToFragment vertexMain(VertexInput input)
                                                {
                                                    VertexToFragment output;
                                                    output.Position = float4(input.Position, 1.0);
                                                    output.TexCoord = input.Position.xy;
                                                    return output;
                                                }

                                                [shader("fragment")]
                                                float4 fragmentMain(VertexToFragment input) : SV_Target0
                                                {
                                                    return float4(input.TexCoord, 0.0, 1.0);
                                                }
                                                """;

    private string _testDirectory = string.Empty;

    [SetUp]
    public void SetUp()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), "GraphicsShaderProgramCompilerTests_" + Guid.NewGuid());
        Directory.CreateDirectory(_testDirectory);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_testDirectory))
        {
            Directory.Delete(_testDirectory, recursive: true);
        }
    }

    [Test]
    public void Compile_ResourcesUsedByDifferentStages_CreatesStageSpecificLayouts()
    {
        const string source = """
                              Texture2D<float4> vertexTexture : register(t0, space0);
                              SamplerState vertexSampler : register(s0, space0);
                              Texture2D<float4> fragmentTexture : register(t0, space2);
                              SamplerState fragmentSampler : register(s0, space2);

                              struct VertexInput
                              {
                                  float3 Position : POSITION;
                                  float2 TexCoord : TEXCOORD0;
                              };

                              struct VertexToFragment
                              {
                                  float4 Position : SV_Position;
                                  float2 TexCoord : TEXCOORD0;
                              };

                              [shader("vertex")]
                              VertexToFragment vertexMain(VertexInput input)
                              {
                                  VertexToFragment output;
                                  output.Position = vertexTexture.SampleLevel(vertexSampler, input.TexCoord, 0.0);
                                  output.TexCoord = input.TexCoord;
                                  return output;
                              }

                              [shader("fragment")]
                              float4 fragmentMain(VertexToFragment input) : SV_Target0
                              {
                                  return fragmentTexture.Sample(fragmentSampler, input.TexCoord);
                              }
                              """;

        GraphicsShaderProgramMetadataDto metadata = Compile(source);

        Assert.Multiple(() =>
        {
            Assert.That(metadata.Vertex.BindingLayout.BindingCounts.NumSamplers, Is.EqualTo(1));
            Assert.That(metadata.Fragment.BindingLayout.BindingCounts.NumSamplers, Is.EqualTo(1));
            Assert.That(
                metadata.Vertex.Shaders.Select(shader => shader.EntryPoint),
                Is.EqualTo(new[] { "main", "vertexMain", "vertexMain", "vertexMain" }));
            Assert.That(
                metadata.Fragment.Shaders.Select(shader => shader.EntryPoint),
                Is.EqualTo(new[] { "main", "fragmentMain", "fragmentMain", "fragmentMain" }));
            Assert.That(metadata.Vertex.Shaders.Select(shader => shader.Filename), Is.EqualTo(new[]
            {
                "program.vertex.spv",
                "program.vertex.dxil",
                "program.vertex.metal",
                "program.vertex.wgsl"
            }));
            Assert.That(metadata.Fragment.Shaders.Select(shader => shader.Filename), Is.EqualTo(new[]
            {
                "program.fragment.spv",
                "program.fragment.dxil",
                "program.fragment.metal",
                "program.fragment.wgsl"
            }));
        });
    }

    [TestCase("vertex", "fragment entry point")]
    [TestCase("fragment", "vertex entry point")]
    public void Compile_MissingGraphicsStage_Throws(string retainedStage, string expectedMessage)
    {
        int vertexIndex = ValidProgram.IndexOf("[shader(\"vertex\")]", StringComparison.Ordinal);
        int fragmentIndex = ValidProgram.IndexOf("[shader(\"fragment\")]", StringComparison.Ordinal);
        string source = retainedStage == "vertex"
            ? ValidProgram[..fragmentIndex]
            : ValidProgram[..vertexIndex] + ValidProgram[fragmentIndex..];

        ShaderCompilationException? exception = Assert.Throws<ShaderCompilationException>(() => Compile(source));

        Assert.That(exception.Message, Does.Contain(expectedMessage));
    }

    [Test]
    public void Compile_WrongEntryPointNames_Throws()
    {
        string source = ValidProgram.Replace("vertexMain", "vertex", StringComparison.Ordinal);

        ShaderCompilationException? exception = Assert.Throws<ShaderCompilationException>(() => Compile(source));

        Assert.That(exception.Message, Does.Contain("must be named 'vertexMain'"));
    }

    [Test]
    public void Compile_VertexOutputIsNotStructure_Throws()
    {
        const string source = """
                              [shader("vertex")]
                              float4 vertexMain(float3 input : POSITION) : SV_Position
                              {
                                  return float4(input, 1.0);
                              }

                              struct VertexToFragment
                              {
                                  float4 Position : SV_Position;
                              };

                              [shader("fragment")]
                              float4 fragmentMain(VertexToFragment input) : SV_Target0
                              {
                                  return input.Position;
                              }
                              """;

        ShaderCompilationException? exception = Assert.Throws<ShaderCompilationException>(() => Compile(source));

        Assert.That(exception.Message, Does.Contain("must return a named structure"));
    }

    [Test]
    public void Compile_PositionIsNotFirst_Throws()
    {
        string source = ValidProgram.ReplaceLineEndings("\n").Replace(
            "float4 Position : SV_Position;\n    float2 TexCoord : TEXCOORD0;",
            "float2 TexCoord : TEXCOORD0;\n    float4 Position : SV_Position;",
            StringComparison.Ordinal);

        ShaderCompilationException? exception = Assert.Throws<ShaderCompilationException>(() => Compile(source));

        Assert.That(exception.Message, Does.Contain("exactly once and as its first field"));
    }

    [Test]
    public void Compile_FragmentConsumesStructurallyEquivalentStructure_Succeeds()
    {
        string source = ValidProgram.Replace(
            "[shader(\"fragment\")]",
            "struct FragmentInput\n{\n    float4 Position : SV_Position;\n    float2 TexCoord : TEXCOORD0;\n};\n\n[shader(\"fragment\")]",
            StringComparison.Ordinal).Replace(
            "fragmentMain(VertexToFragment input)",
            "fragmentMain(FragmentInput input)",
            StringComparison.Ordinal);

        Assert.That(Compile(source), Is.Not.Null);
    }

    [Test]
    public void Compile_FragmentOmitsVertexOutputField_Throws()
    {
        string source = ValidProgram.Replace(
            "[shader(\"fragment\")]",
            "struct FragmentInput\n{\n    float4 Position : SV_Position;\n};\n\n[shader(\"fragment\")]",
            StringComparison.Ordinal).Replace(
            "fragmentMain(VertexToFragment input)",
            "fragmentMain(FragmentInput input)",
            StringComparison.Ordinal).Replace(
            "return float4(input.TexCoord, 0.0, 1.0);",
            "return input.Position;",
            StringComparison.Ordinal);

        ShaderCompilationException? exception = Assert.Throws<ShaderCompilationException>(() => Compile(source));

        Assert.That(exception.Message, Does.Contain("must consume the complete structure"));
    }

    [Test]
    public void Compile_FragmentConsumesAliasOfVertexOutput_Succeeds()
    {
        string source = ValidProgram.Replace(
            "[shader(\"fragment\")]",
            "typealias FragmentInput = VertexToFragment;\n\n[shader(\"fragment\")]",
            StringComparison.Ordinal).Replace(
            "fragmentMain(VertexToFragment input)",
            "fragmentMain(FragmentInput input)",
            StringComparison.Ordinal);

        Assert.That(Compile(source), Is.Not.Null);
    }

    private GraphicsShaderProgramMetadataDto Compile(string source)
    {
        string shaderPath = Path.Combine(_testDirectory, "program.slang");
        File.WriteAllText(shaderPath, source);
        SdlangCompiler compiler = SdlangCompilerTestFactory.Create();
        compiler.Compile([shaderPath], force: true);

        string metadataPath = Path.Combine(_testDirectory, ".generated", "program.metadata.json");
        GraphicsShaderProgramMetadataDto? metadata = JsonSerializer.Deserialize(
            File.ReadAllText(metadataPath),
            ShaderMetadataJsonContext.Default.GraphicsShaderProgramMetadataDto);
        return metadata ?? throw new InvalidOperationException("Graphics shader metadata was not produced.");
    }
}
