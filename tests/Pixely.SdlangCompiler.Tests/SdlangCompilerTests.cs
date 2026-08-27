using System.Text.Json;
using System.Text.Json.Nodes;
using Pixely.ShaderCommon;

namespace Pixely.SdlangCompiler.Tests;

public class SdlangCompilerTests
{
    private const string ShaderContent = """
                                         struct VertexInput
                                         {
                                             float3 Position : POSITION;
                                         };

                                         struct VertexToFragment
                                         {
                                             float4 Position : SV_Position;
                                         };

                                         [shader("vertex")]
                                         VertexToFragment vertexMain(VertexInput input)
                                         {
                                             VertexToFragment output;
                                             output.Position = float4(input.Position, 1.0);
                                             return output;
                                         }

                                         [shader("fragment")]
                                         float4 fragmentMain(VertexToFragment input) : SV_Target0
                                         {
                                             return float4(1.0);
                                         }
                                         """;

    private const string ShaderWithSourceDependencies = """
                                                        #include "shared $ sources/included # [value].slang"
                                                        import imported;

                                                        struct VertexInput
                                                        {
                                                            float3 Position : POSITION;
                                                        };

                                                        struct VertexToFragment
                                                        {
                                                            float4 Position : SV_Position;
                                                        };

                                                        [shader("vertex")]
                                                        VertexToFragment vertexMain(VertexInput input)
                                                        {
                                                            VertexToFragment output;
                                                            output.Position = float4(input.Position, 1.0);
                                                            return output;
                                                        }

                                                        [shader("fragment")]
                                                        float4 fragmentMain(VertexToFragment input) : SV_Target0
                                                        {
                                                            return includedColor() + importedColor();
                                                        }
                                                        """;

    private const string IncludedShaderSource = """
                                                 float4 includedColor()
                                                 {
                                                     return float4(0.25, 0.0, 0.0, 0.0);
                                                 }
                                                 """;

    private const string UpdatedIncludedShaderSource = """
                                                        float4 includedColor()
                                                        {
                                                            return float4(0.5, 0.0, 0.0, 0.0);
                                                        }
                                                        """;

    private const string ImportedShaderSource = """
                                                 public float4 importedColor()
                                                 {
                                                     return float4(0.0, 0.25, 0.0, 1.0);
                                                 }
                                                 """;

    private const string UpdatedImportedShaderSource = """
                                                        public float4 importedColor()
                                                        {
                                                            return float4(0.0, 0.5, 0.0, 1.0);
                                                        }
                                                        """;

    private const string ValidVertexShaderWithBindings = """
                                                         struct VertexInput {
                                                             float3 position : POSITION;
                                                             float2 texCoord : TEXCOORD0;
                                                         };

                                                         struct VertexOutput {
                                                             float4 position : SV_Position;
                                                             float2 texCoord : TEXCOORD0;
                                                         };

                                                         cbuffer VertexUniforms : register(b0, space1) {
                                                             float4x4 transform;
                                                         };

                                                         Texture2D<float4> myTexture : register(t0, space0);
                                                         SamplerState mySampler : register(s0, space0);

                                                         [shader("vertex")]
                                                         VertexOutput vertexMain(VertexInput input) {
                                                             VertexOutput output;
                                                             output.position = mul(transform, float4(input.position, 1.0));
                                                             output.texCoord = input.texCoord;
                                                             return output;
                                                         }

                                                         [shader("fragment")]
                                                         float4 fragmentMain(VertexOutput input) : SV_Target0 {
                                                             return input.position;
                                                         }
                                                         """;

    private const string VertexShaderWithSystemValueInputs = """
                                                            struct VertexInput {
                                                                float3 position : POSITION;
                                                                uint vertexId : SV_VertexID;
                                                                uint instanceId : SV_InstanceID;
                                                            };

                                                            struct VertexToFragment {
                                                                float4 position : SV_Position;
                                                            };

                                                            [shader("vertex")]
                                                            VertexToFragment vertexMain(VertexInput input)
                                                            {
                                                                VertexToFragment output;
                                                                output.position = float4(input.position.xy, float(input.vertexId + input.instanceId), 1.0);
                                                                return output;
                                                            }

                                                            [shader("fragment")]
                                                            float4 fragmentMain(VertexToFragment input) : SV_Target0
                                                            {
                                                                return input.position;
                                                            }
                                                            """;

    private const string ValidFragmentShaderWithBindings = """
                                                           struct FragmentInput {
                                                               float4 position : SV_Position;
                                                               float2 texCoord : TEXCOORD0;
                                                           };

                                                           struct VertexInput {
                                                               float3 position : POSITION;
                                                               float2 texCoord : TEXCOORD0;
                                                           };

                                                           cbuffer FragmentUniforms : register(b0, space3) {
                                                               float4 tintColor;
                                                           };

                                                           Texture2D<float4> albedo : register(t0, space2);
                                                           SamplerState albedoSampler : register(s0, space2);

                                                           [shader("vertex")]
                                                           FragmentInput vertexMain(VertexInput input) {
                                                               FragmentInput output;
                                                               output.position = float4(input.position, 1.0);
                                                               output.texCoord = input.texCoord;
                                                               return output;
                                                           }

                                                           [shader("fragment")]
                                                           float4 fragmentMain(FragmentInput input) : SV_Target {
                                                               return albedo.Sample(albedoSampler, input.texCoord) * tintColor;
                                                           }
                                                           """;

    private const string ValidComputeShaderWithBindings = """
                                                          RWTexture2D<float4> outputTexture : register(u0, space1);

                                                          ConstantBuffer<float> time : register(b0, space2);

                                                          [numthreads(8, 8, 1)]
                                                          [shader("compute")]
                                                          void main(uint3 dispatchThreadID : SV_DispatchThreadID)
                                                          {
                                                              outputTexture[dispatchThreadID.xy] = float4(time, 0.0, 0.0, 1.0);
                                                          }
                                                          """;

    private const string FragmentShaderWrongUniformSpace = """
                                                           struct FragmentInput {
                                                               float4 position : SV_Position;
                                                           };

                                                           struct VertexInput {
                                                               float3 position : POSITION;
                                                           };

                                                           cbuffer FragmentUniforms : register(b0, space0) {
                                                               float4 tintColor;
                                                           };

                                                           [shader("vertex")]
                                                           FragmentInput vertexMain(VertexInput input) {
                                                               FragmentInput output;
                                                               output.position = float4(input.position, 1.0);
                                                               return output;
                                                           }

                                                           [shader("fragment")]
                                                           float4 fragmentMain(FragmentInput input) : SV_Target {
                                                               return tintColor;
                                                           }
                                                           """;

    private const string VertexShaderWrongUniformSpace = """
                                                         cbuffer VertexUniforms : register(b0, space3) {
                                                             float4x4 transform;
                                                         };

                                                         struct VertexInput {
                                                             float3 position : POSITION;
                                                         };

                                                         struct VertexOutput {
                                                             float4 position : SV_Position;
                                                         };

                                                         [shader("vertex")]
                                                         VertexOutput vertexMain(VertexInput input) {
                                                             VertexOutput output;
                                                             output.position = mul(transform, float4(input.position, 1.0));
                                                             return output;
                                                         }

                                                         [shader("fragment")]
                                                         float4 fragmentMain(VertexOutput input) : SV_Target0 {
                                                             return input.position;
                                                         }
                                                         """;

    private const string FragmentShaderWrongTextureSpace = """
                                                           struct FragmentInput {
                                                               float4 position : SV_Position;
                                                               float2 texCoord : TEXCOORD0;
                                                           };

                                                           struct VertexInput {
                                                               float3 position : POSITION;
                                                               float2 texCoord : TEXCOORD0;
                                                           };

                                                           Texture2D<float4> albedo : register(t0, space0);
                                                           SamplerState albedoSampler : register(s0, space0);

                                                           [shader("vertex")]
                                                           FragmentInput vertexMain(VertexInput input) {
                                                               FragmentInput output;
                                                               output.position = float4(input.position, 1.0);
                                                               output.texCoord = input.texCoord;
                                                               return output;
                                                           }

                                                           [shader("fragment")]
                                                           float4 fragmentMain(FragmentInput input) : SV_Target {
                                                               return albedo.Sample(albedoSampler, input.texCoord);
                                                           }
                                                           """;

    private const string FragmentShaderWrongIndexOrder = """
                                                         struct FragmentInput {
                                                             float4 position : SV_Position;
                                                             float2 texCoord : TEXCOORD0;
                                                         };

                                                         struct VertexInput {
                                                             float3 position : POSITION;
                                                             float2 texCoord : TEXCOORD0;
                                                         };

                                                         StructuredBuffer<float4> myData : register(t0, space2);
                                                         Texture2D<float4> albedo : register(t1, space2);
                                                         SamplerState albedoSampler : register(s0, space2);

                                                         [shader("vertex")]
                                                         FragmentInput vertexMain(VertexInput input) {
                                                             FragmentInput output;
                                                             output.position = float4(input.position, 1.0);
                                                             output.texCoord = input.texCoord;
                                                             return output;
                                                         }

                                                         [shader("fragment")]
                                                         float4 fragmentMain(FragmentInput input) : SV_Target {
                                                             return albedo.Sample(albedoSampler, input.texCoord) + myData[0];
                                                         }
                                                         """;

    private const string VertexShaderMismatchedSamplerIndex = """
                                                              struct VertexInput {
                                                                  float3 position : POSITION;
                                                                  float2 texCoord : TEXCOORD0;
                                                              };

                                                              struct VertexOutput {
                                                                  float4 position : SV_Position;
                                                                  float4 color : COLOR0;
                                                              };

                                                              Texture2D<float4> albedo : register(t0, space0);
                                                              SamplerState albedoSampler : register(s1, space0);

                                                              [shader("vertex")]
                                                              VertexOutput vertexMain(VertexInput input) {
                                                                  VertexOutput output;
                                                                  output.position = float4(input.position, 1.0);
                                                                  output.color = albedo.SampleLevel(albedoSampler, input.texCoord, 0.0);
                                                                  return output;
                                                              }

                                                              [shader("fragment")]
                                                              float4 fragmentMain(VertexOutput input) : SV_Target0 {
                                                                  return input.color;
                                                              }
                                                              """;

    private const string FragmentShaderMismatchedSamplerIndex = """
                                                                struct FragmentInput {
                                                                    float4 position : SV_Position;
                                                                    float2 texCoord : TEXCOORD0;
                                                                };

                                                                struct VertexInput {
                                                                    float3 position : POSITION;
                                                                    float2 texCoord : TEXCOORD0;
                                                                };

                                                                Texture2D<float4> albedo : register(t0, space2);
                                                                SamplerState albedoSampler : register(s1, space2);

                                                                [shader("vertex")]
                                                                FragmentInput vertexMain(VertexInput input) {
                                                                    FragmentInput output;
                                                                    output.position = float4(input.position, 1.0);
                                                                    output.texCoord = input.texCoord;
                                                                    return output;
                                                                }

                                                                [shader("fragment")]
                                                                float4 fragmentMain(FragmentInput input) : SV_Target {
                                                                    return albedo.Sample(albedoSampler, input.texCoord);
                                                                }
                                                                """;

    private const string ComputeShaderMismatchedSamplerIndex = """
                                                               Texture2D<float4> inputTexture : register(t0, space0);
                                                               SamplerState inputSampler : register(s1, space0);
                                                               RWTexture2D<float4> outputTexture : register(u0, space1);

                                                               [numthreads(8, 8, 1)]
                                                               [shader("compute")]
                                                               void main(uint3 dispatchThreadID : SV_DispatchThreadID)
                                                               {
                                                                   float2 uv = float2(dispatchThreadID.xy) / float2(8.0, 8.0);
                                                                   outputTexture[dispatchThreadID.xy] = inputTexture.SampleLevel(inputSampler, uv, 0.0);
                                                               }
                                                               """;

    private string _testDir = string.Empty;

    [SetUp]
    public void SetUp()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "SdlangCompilerTests_" + Guid.NewGuid());
        Directory.CreateDirectory(_testDir);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_testDir))
        {
            Directory.Delete(_testDir, recursive: true);
        }
    }

    [Test]
    public void CompileShader_CreatesMetadataFile()
    {
        // Arrange
        string shaderPath = Path.Combine(_testDir, "test_shader.slang");
        File.WriteAllText(shaderPath, ShaderContent);

        SdlangCompiler compiler = SdlangCompilerTestFactory.Create();

        // Act
        compiler.Compile([shaderPath], force: true);

        // Assert
        string metadataPath = Path.Combine(_testDir, ".generated", "test_shader.metadata.json");
        Assert.That(File.Exists(metadataPath), Is.True, "Metadata file should be created");

        string json = File.ReadAllText(metadataPath);

        GraphicsShaderProgramMetadataDto? metadata = JsonSerializer.Deserialize(
            json,
            ShaderMetadataJsonContext.Default.GraphicsShaderProgramMetadataDto);

        Assert.That(metadata, Is.Not.Null);
        Assert.That(metadata.Kind, Is.EqualTo(ShaderKindDto.Graphics));
        AssertGraphicsGeneratedTargets(metadata, "test_shader");
        Assert.That(metadata.SourceHash, Is.Not.Empty);
        Assert.That(metadata.SourceDependencies, Is.EqualTo(new[] { "test_shader.slang" }));
        Assert.That(metadata.Vertex.SystemValueInputs.UsesVertexId, Is.False);
        Assert.That(metadata.Vertex.SystemValueInputs.UsesInstanceId, Is.False);

        using JsonDocument document = JsonDocument.Parse(json);
        Assert.That(document.RootElement.GetProperty("vertex").TryGetProperty("entryPoint", out JsonElement _), Is.False);
        Assert.That(document.RootElement.GetProperty("fragment").TryGetProperty("entryPoint", out JsonElement _), Is.False);
        Assert.That(document.RootElement.TryGetProperty("threadCountX", out JsonElement _), Is.False);
    }

    [Test]
    [Platform(Exclude = "Win")]
    public void CompileShader_SymbolicLinkInShaderPath_UsesRelativeSourceDependency()
    {
        string shaderDirectory = Path.Combine(_testDir, "shaders");
        string shaderDirectoryLink = Path.Combine(_testDir, "linked-shaders");
        Directory.CreateDirectory(shaderDirectory);
        Directory.CreateSymbolicLink(shaderDirectoryLink, shaderDirectory);
        string shaderPath = Path.Combine(shaderDirectoryLink, "test_shader.slang");
        File.WriteAllText(shaderPath, ShaderContent);

        SdlangCompiler compiler = SdlangCompilerTestFactory.Create();
        compiler.Compile([shaderPath], force: true);

        string metadataPath = Path.Combine(shaderDirectoryLink, ".generated", "test_shader.metadata.json");
        GraphicsShaderProgramMetadataDto metadata = ReadGraphicsMetadata(metadataPath);
        Assert.That(metadata.SourceDependencies, Is.EqualTo(new[] { "test_shader.slang" }));
    }

    [Test]
    public void CompileShader_ObsoleteGeneratedFiles_RemovesThem()
    {
        string shaderPath = Path.Combine(_testDir, "current.slang");
        string otherShaderPath = Path.Combine(_testDir, "other.slang");
        File.WriteAllText(shaderPath, ShaderContent);
        File.WriteAllText(otherShaderPath, ShaderContent);

        SdlangCompiler compiler = SdlangCompilerTestFactory.Create();
        compiler.Compile([shaderPath, otherShaderPath], force: true);

        string generatedDirectory = Path.Combine(_testDir, ".generated");
        string obsoleteCurrentOutput = Path.Combine(generatedDirectory, "current.spv");
        string removedMetadata = Path.Combine(generatedDirectory, "removed.metadata.json");
        string removedOutput = Path.Combine(generatedDirectory, "removed.vertex.spv");
        string retainedFile = Path.Combine(generatedDirectory, "retained.txt");
        File.WriteAllBytes(obsoleteCurrentOutput, [0]);
        File.Copy(Path.Combine(generatedDirectory, "current.metadata.json"), removedMetadata);
        File.WriteAllBytes(removedOutput, [0]);
        File.WriteAllText(retainedFile, "not a generated shader output");

        compiler.Compile([shaderPath], force: false);

        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(obsoleteCurrentOutput), Is.False);
            Assert.That(File.Exists(removedMetadata), Is.False);
            Assert.That(File.Exists(removedOutput), Is.False);
            Assert.That(File.Exists(Path.Combine(generatedDirectory, "other.vertex.spv")), Is.True);
            Assert.That(File.Exists(Path.Combine(generatedDirectory, "other.metadata.json")), Is.True);
            Assert.That(File.Exists(retainedFile), Is.True);
        });
    }

    [TestCase("{\"name\":\"vertexMain\"}", "bindings array")]
    [TestCase("{\"name\":\"vertexMain\",\"bindings\":{}}", "bindings array")]
    [TestCase("{\"name\":\"vertexMain\",\"bindings\":[0]}", "malformed binding")]
    [TestCase(
        "{\"name\":\"vertexMain\",\"bindings\":[{\"name\":\"color\",\"binding\":{}}]}",
        "malformed binding")]
    [TestCase(
        "{\"name\":\"vertexMain\",\"bindings\":[{\"name\":\"color\",\"binding\":{\"used\":true}}]}",
        "malformed binding")]
    public void GetUsedParameterNames_MalformedReflection_Throws(string json, string expectedMessage)
    {
        using JsonDocument document = JsonDocument.Parse(json);

        ShaderCompilationException? exception = Assert.Throws<ShaderCompilationException>(() =>
            SdlangCompiler.GetUsedParameterNames(document.RootElement));

        Assert.That(exception.Message, Does.Contain(expectedMessage));
    }

    [Test]
    public void CompileShader_MissingGeneratedShader_Recompiles()
    {
        string shaderPath = Path.Combine(_testDir, "missing_output.slang");
        File.WriteAllText(shaderPath, ShaderContent);

        SdlangCompiler compiler = SdlangCompilerTestFactory.Create();
        compiler.Compile([shaderPath], force: true);

        string generatedShaderPath = Path.Combine(_testDir, ".generated", "missing_output.vertex.spv");
        File.Delete(generatedShaderPath);

        compiler.Compile([shaderPath], force: false);

        Assert.That(File.Exists(generatedShaderPath), Is.True);
    }

    [Test]
    public void CompileShader_MissingSourceDependencies_Recompiles()
    {
        string shaderPath = Path.Combine(_testDir, "legacy_metadata.slang");
        File.WriteAllText(shaderPath, ShaderContent);

        SdlangCompiler compiler = SdlangCompilerTestFactory.Create();
        compiler.Compile([shaderPath], force: true);

        string generatedDirectory = Path.Combine(_testDir, ".generated");
        string generatedShaderPath = Path.Combine(generatedDirectory, "legacy_metadata.vertex.spv");
        string metadataPath = Path.Combine(generatedDirectory, "legacy_metadata.metadata.json");
        JsonObject metadata = JsonNode.Parse(File.ReadAllText(metadataPath))!.AsObject();
        metadata.Remove("sourceDependencies");
        File.WriteAllText(metadataPath, metadata.ToJsonString());
        File.WriteAllBytes(generatedShaderPath, [0]);

        compiler.Compile([shaderPath], force: false);

        Assert.That(new FileInfo(generatedShaderPath).Length, Is.GreaterThan(1));
    }

    [Test]
    public void CompileShader_LegacyMetadataFormat_Recompiles()
    {
        string shaderPath = Path.Combine(_testDir, "legacy_format.slang");
        File.WriteAllText(shaderPath, ShaderContent);

        SdlangCompiler compiler = SdlangCompilerTestFactory.Create();
        compiler.Compile([shaderPath], force: true);

        string generatedDirectory = Path.Combine(_testDir, ".generated");
        string generatedShaderPath = Path.Combine(generatedDirectory, "legacy_format.vertex.spv");
        string metadataPath = Path.Combine(generatedDirectory, "legacy_format.metadata.json");
        JsonObject metadata = JsonNode.Parse(File.ReadAllText(metadataPath))!.AsObject();
        metadata.Remove("kind");
        metadata["stage"] = "Vertex";
        File.WriteAllText(metadataPath, metadata.ToJsonString());
        File.WriteAllBytes(generatedShaderPath, [0]);

        compiler.Compile([shaderPath], force: false);

        Assert.That(new FileInfo(generatedShaderPath).Length, Is.GreaterThan(1));
    }

    [Test]
    public void CompileShader_LegacyTargetSet_RecompilesWithDxil()
    {
        string shaderPath = Path.Combine(_testDir, "legacy_targets.slang");
        File.WriteAllText(shaderPath, ShaderContent);

        SdlangCompiler compiler = SdlangCompilerTestFactory.Create();
        compiler.Compile([shaderPath], force: true);

        string generatedDirectory = Path.Combine(_testDir, ".generated");
        string dxilPath = Path.Combine(generatedDirectory, "legacy_targets.vertex.dxil");
        string metadataPath = Path.Combine(generatedDirectory, "legacy_targets.metadata.json");
        JsonObject metadata = JsonNode.Parse(File.ReadAllText(metadataPath))!.AsObject();
        JsonArray shaders = metadata["vertex"]!["shaders"]!.AsArray();
        JsonNode dxilShader = shaders.Single(shader => shader!["format"]!.GetValue<string>() == "Dxil")!;
        shaders.Remove(dxilShader);
        File.WriteAllText(metadataPath, metadata.ToJsonString());
        File.WriteAllBytes(dxilPath, [0]);

        compiler.Compile([shaderPath], force: false);

        GraphicsShaderProgramMetadataDto updatedMetadata = JsonSerializer.Deserialize(
            File.ReadAllText(metadataPath),
            ShaderMetadataJsonContext.Default.GraphicsShaderProgramMetadataDto)!;
        Assert.That(new FileInfo(dxilPath).Length, Is.GreaterThan(1));
        AssertGraphicsGeneratedTargets(updatedMetadata, "legacy_targets");
    }

    [Test]
    public void CompileShader_LegacyGraphicsEntryPointNames_Recompiles()
    {
        string shaderPath = Path.Combine(_testDir, "legacy_entry_points.slang");
        File.WriteAllText(shaderPath, ShaderContent);

        SdlangCompiler compiler = SdlangCompilerTestFactory.Create();
        compiler.Compile([shaderPath], force: true);

        string generatedDirectory = Path.Combine(_testDir, ".generated");
        string spirvPath = Path.Combine(generatedDirectory, "legacy_entry_points.vertex.spv");
        string metadataPath = Path.Combine(generatedDirectory, "legacy_entry_points.metadata.json");
        JsonObject metadata = JsonNode.Parse(File.ReadAllText(metadataPath))!.AsObject();
        JsonArray vertexShaders = metadata["vertex"]!["shaders"]!.AsArray();
        JsonNode vertexSpirv = vertexShaders.Single(shader => shader!["format"]!.GetValue<string>() == "SpirV")!;
        vertexSpirv["entryPoint"] = "vertexMain";
        File.WriteAllText(metadataPath, metadata.ToJsonString());
        File.WriteAllBytes(spirvPath, [0]);

        compiler.Compile([shaderPath], force: false);

        GraphicsShaderProgramMetadataDto updatedMetadata = ReadGraphicsMetadata(metadataPath);
        Assert.That(new FileInfo(spirvPath).Length, Is.GreaterThan(1));
        AssertGraphicsGeneratedTargets(updatedMetadata, "legacy_entry_points");
    }

    [Test]
    public void CompileShader_StageEntryPointsInMetadata_Recompiles()
    {
        string shaderPath = Path.Combine(_testDir, "stage_entry_points.slang");
        File.WriteAllText(shaderPath, ShaderContent);

        SdlangCompiler compiler = SdlangCompilerTestFactory.Create();
        compiler.Compile([shaderPath], force: true);

        string generatedDirectory = Path.Combine(_testDir, ".generated");
        string spirvPath = Path.Combine(generatedDirectory, "stage_entry_points.vertex.spv");
        string metadataPath = Path.Combine(generatedDirectory, "stage_entry_points.metadata.json");
        JsonObject metadata = JsonNode.Parse(File.ReadAllText(metadataPath))!.AsObject();
        metadata["vertex"]!["entryPoint"] = "vertexMain";
        metadata["fragment"]!["entryPoint"] = "fragmentMain";
        File.WriteAllText(metadataPath, metadata.ToJsonString());
        File.WriteAllBytes(spirvPath, [0]);

        compiler.Compile([shaderPath], force: false);

        JsonObject updatedMetadata = JsonNode.Parse(File.ReadAllText(metadataPath))!.AsObject();
        Assert.Multiple(() =>
        {
            Assert.That(new FileInfo(spirvPath).Length, Is.GreaterThan(1));
            Assert.That(updatedMetadata["vertex"]!["entryPoint"], Is.Null);
            Assert.That(updatedMetadata["fragment"]!["entryPoint"], Is.Null);
        });
    }

    [Test]
    public void CompileShader_SourceDependencyChanges_RecompilesGeneratedShaders()
    {
        string shaderPath = Path.Combine(_testDir, "dependency_shader.slang");
        string includedDirectory = Path.Combine(_testDir, "shared $ sources");
        string includedShaderPath = Path.Combine(includedDirectory, "included # [value].slang");
        string importedShaderPath = Path.Combine(_testDir, "imported.slang");
        Directory.CreateDirectory(includedDirectory);
        File.WriteAllText(shaderPath, ShaderWithSourceDependencies);
        File.WriteAllText(includedShaderPath, IncludedShaderSource);
        File.WriteAllText(importedShaderPath, ImportedShaderSource);

        SdlangCompiler compiler = SdlangCompilerTestFactory.Create();
        compiler.Compile([shaderPath], force: true);

        string generatedDirectory = Path.Combine(_testDir, ".generated");
        string spirvPath = Path.Combine(generatedDirectory, "dependency_shader.fragment.spv");
        string metalPath = Path.Combine(generatedDirectory, "dependency_shader.fragment.metal");
        string metadataPath = Path.Combine(generatedDirectory, "dependency_shader.metadata.json");
        byte[] originalSpirv = File.ReadAllBytes(spirvPath);
        string originalMetal = File.ReadAllText(metalPath);
        GraphicsShaderProgramMetadataDto originalMetadata = ReadGraphicsMetadata(metadataPath);

        Assert.That(originalMetadata.SourceDependencies, Is.EqualTo(new[]
        {
            "dependency_shader.slang",
            "imported.slang",
            "shared $ sources/included # [value].slang"
        }));

        File.WriteAllText(includedShaderPath, UpdatedIncludedShaderSource);
        compiler.Compile([shaderPath], force: false);

        byte[] includedUpdateSpirv = File.ReadAllBytes(spirvPath);
        string includedUpdateMetal = File.ReadAllText(metalPath);
        GraphicsShaderProgramMetadataDto includedUpdateMetadata = ReadGraphicsMetadata(metadataPath);
        Assert.That(includedUpdateMetadata.SourceHash, Is.Not.EqualTo(originalMetadata.SourceHash));
        Assert.That(includedUpdateSpirv, Is.Not.EqualTo(originalSpirv));
        Assert.That(includedUpdateMetal, Is.Not.EqualTo(originalMetal));

        File.WriteAllText(importedShaderPath, UpdatedImportedShaderSource);
        compiler.Compile([shaderPath], force: false);

        GraphicsShaderProgramMetadataDto importedUpdateMetadata = ReadGraphicsMetadata(metadataPath);
        Assert.That(importedUpdateMetadata.SourceHash, Is.Not.EqualTo(includedUpdateMetadata.SourceHash));
        Assert.That(File.ReadAllBytes(spirvPath), Is.Not.EqualTo(includedUpdateSpirv));
        Assert.That(File.ReadAllText(metalPath), Is.Not.EqualTo(includedUpdateMetal));
        Assert.That(File.Exists(Path.Combine(generatedDirectory, "imported.spv")), Is.False);
        Assert.That(File.Exists(Path.Combine(generatedDirectory, "included # [value].spv")), Is.False);
    }

    [Test]
    public void CompileShader_VertexShaderWithSystemValueInputs_CreatesSystemValueMetadata()
    {
        string shaderPath = Path.Combine(_testDir, "system_values.slang");
        File.WriteAllText(shaderPath, VertexShaderWithSystemValueInputs);

        SdlangCompiler compiler = SdlangCompilerTestFactory.Create();
        compiler.Compile([shaderPath], force: true);

        string metadataPath = Path.Combine(_testDir, ".generated", "system_values.metadata.json");
        string json = File.ReadAllText(metadataPath);

        GraphicsShaderProgramMetadataDto? metadata = JsonSerializer.Deserialize(
            json,
            ShaderMetadataJsonContext.Default.GraphicsShaderProgramMetadataDto);

        Assert.That(metadata, Is.Not.Null);
        Assert.That(metadata.Vertex.SystemValueInputs.UsesVertexId, Is.True);
        Assert.That(metadata.Vertex.SystemValueInputs.UsesInstanceId, Is.True);
    }

    [Test]
    public void CompileShader_ValidVertexShaderWithBindings_Succeeds()
    {
        string shaderPath = Path.Combine(_testDir, "valid_vertex.slang");
        File.WriteAllText(shaderPath, ValidVertexShaderWithBindings);

        SdlangCompiler compiler = SdlangCompilerTestFactory.Create();
        compiler.Compile([shaderPath], force: true);

        string metadataPath = Path.Combine(_testDir, ".generated", "valid_vertex.metadata.json");
        GraphicsShaderProgramMetadataDto metadata = JsonSerializer.Deserialize(
            File.ReadAllText(metadataPath),
            ShaderMetadataJsonContext.Default.GraphicsShaderProgramMetadataDto)!;
        AssertGraphicsGeneratedTargets(metadata, "valid_vertex");
    }

    [Test]
    public void CompileShader_ValidFragmentShaderWithBindings_Succeeds()
    {
        string shaderPath = Path.Combine(_testDir, "valid_fragment.slang");
        File.WriteAllText(shaderPath, ValidFragmentShaderWithBindings);

        SdlangCompiler compiler = SdlangCompilerTestFactory.Create();
        compiler.Compile([shaderPath], force: true);

        string metadataPath = Path.Combine(_testDir, ".generated", "valid_fragment.metadata.json");
        GraphicsShaderProgramMetadataDto metadata = JsonSerializer.Deserialize(
            File.ReadAllText(metadataPath),
            ShaderMetadataJsonContext.Default.GraphicsShaderProgramMetadataDto)!;
        AssertGraphicsGeneratedTargets(metadata, "valid_fragment");
    }

    [Test]
    public void CompileShader_ValidComputeShaderWithBindings_CreatesComputeMetadata()
    {
        string shaderPath = Path.Combine(_testDir, "valid_compute.slang");
        File.WriteAllText(shaderPath, ValidComputeShaderWithBindings);

        SdlangCompiler compiler = SdlangCompilerTestFactory.Create();
        compiler.Compile([shaderPath], force: true);

        string metadataPath = Path.Combine(_testDir, ".generated", "valid_compute.metadata.json");
        Assert.That(File.Exists(metadataPath), Is.True);

        string json = File.ReadAllText(metadataPath);
        ComputeShaderMetadataDto? metadata = JsonSerializer.Deserialize(json, ShaderMetadataJsonContext.Default.ComputeShaderMetadataDto);

        Assert.That(metadata, Is.Not.Null);
        Assert.That(metadata.Stage, Is.EqualTo(ShaderStageDto.Compute));
        Assert.That(metadata.ThreadCountX, Is.EqualTo(8));
        Assert.That(metadata.ThreadCountY, Is.EqualTo(8));
        Assert.That(metadata.ThreadCountZ, Is.EqualTo(1));
        AssertGeneratedTargets(metadata.Shaders, "valid_compute");
    }

    [Test]
    public void CompileShader_FragmentShaderWrongUniformSpace_ThrowsValidationException()
    {
        string shaderPath = Path.Combine(_testDir, "invalid_fragment.slang");
        File.WriteAllText(shaderPath, FragmentShaderWrongUniformSpace);

        SdlangCompiler compiler = SdlangCompilerTestFactory.Create();

        ShaderBindingValidationException? ex = Assert.Throws<ShaderBindingValidationException>(() =>
            compiler.Compile([shaderPath], force: true));

        Assert.That(ex.Message, Does.Contain("space 0"));
        Assert.That(ex.Message, Does.Contain("space 3"));
        Assert.That(ex.Message, Does.Contain("uniform buffers"));
    }

    [Test]
    public void CompileShader_VertexShaderWrongUniformSpace_ThrowsValidationException()
    {
        string shaderPath = Path.Combine(_testDir, "invalid_vertex.slang");
        File.WriteAllText(shaderPath, VertexShaderWrongUniformSpace);

        SdlangCompiler compiler = SdlangCompilerTestFactory.Create();

        ShaderBindingValidationException? ex = Assert.Throws<ShaderBindingValidationException>(() =>
            compiler.Compile([shaderPath], force: true));

        Assert.That(ex.Message, Does.Contain("space 3"));
        Assert.That(ex.Message, Does.Contain("space 1"));
        Assert.That(ex.Message, Does.Contain("uniform buffers"));
    }

    [Test]
    public void CompileShader_FragmentShaderWrongTextureSpace_ThrowsValidationException()
    {
        string shaderPath = Path.Combine(_testDir, "invalid_texture.slang");
        File.WriteAllText(shaderPath, FragmentShaderWrongTextureSpace);

        SdlangCompiler compiler = SdlangCompilerTestFactory.Create();

        ShaderBindingValidationException? ex = Assert.Throws<ShaderBindingValidationException>(() =>
            compiler.Compile([shaderPath], force: true));

        Assert.That(ex.Message, Does.Contain("space 0"));
        Assert.That(ex.Message, Does.Contain("space 2"));
    }

    [Test]
    public void CompileShader_FragmentShaderWrongIndexOrder_ThrowsValidationException()
    {
        string shaderPath = Path.Combine(_testDir, "invalid_order.slang");
        File.WriteAllText(shaderPath, FragmentShaderWrongIndexOrder);

        SdlangCompiler compiler = SdlangCompilerTestFactory.Create();

        ShaderBindingValidationException? ex = Assert.Throws<ShaderBindingValidationException>(() =>
            compiler.Compile([shaderPath], force: true));

        Assert.That(ex.Message, Does.Contain("index"));
    }

    [TestCase(VertexShaderMismatchedSamplerIndex)]
    [TestCase(FragmentShaderMismatchedSamplerIndex)]
    [TestCase(ComputeShaderMismatchedSamplerIndex)]
    public void CompileShader_MismatchedSamplerTextureIndex_ThrowsValidationException(string shaderContent)
    {
        string shaderPath = CreateTemporaryShaderFile(shaderContent);

        SdlangCompiler compiler = SdlangCompilerTestFactory.Create();

        ShaderBindingValidationException? ex = Assert.Throws<ShaderBindingValidationException>(() =>
            compiler.Compile([shaderPath], force: true));

        Assert.That(ex.Message, Does.Contain("same index and space"));
    }

    private const string FragmentShaderWithStructStorageBuffer = """
                                                                 struct FragmentInput {
                                                                     float4 position : SV_Position;
                                                                 };

                                                                 struct VertexInput {
                                                                     float3 position : POSITION;
                                                                 };

                                                                 struct MyData {
                                                                     float4 position;
                                                                     float2 texCoord;
                                                                     float intensity;
                                                                 };

                                                                 StructuredBuffer<MyData> dataBuffer : register(t0, space2);

                                                                 [shader("vertex")]
                                                                 FragmentInput vertexMain(VertexInput input) {
                                                                     FragmentInput output;
                                                                     output.position = float4(input.position, 1.0);
                                                                     return output;
                                                                 }

                                                                 [shader("fragment")]
                                                                 float4 fragmentMain(FragmentInput input) : SV_Target {
                                                                     MyData d = dataBuffer[0];
                                                                     return d.position * d.intensity;
                                                                 }
                                                                 """;

    private const string FragmentShaderWithPrimitiveStorageBuffer = """
                                                                     struct FragmentInput {
                                                                         float4 position : SV_Position;
                                                                     };

                                                                     struct VertexInput {
                                                                         float3 position : POSITION;
                                                                     };

                                                                     StructuredBuffer<float4> colorBuffer : register(t0, space2);

                                                                     [shader("vertex")]
                                                                     FragmentInput vertexMain(VertexInput input) {
                                                                         FragmentInput output;
                                                                         output.position = float4(input.position, 1.0);
                                                                         return output;
                                                                     }

                                                                     [shader("fragment")]
                                                                     float4 fragmentMain(FragmentInput input) : SV_Target {
                                                                         return colorBuffer[0];
                                                                     }
                                                                     """;

    [Test]
    public void CompileShader_FragmentShaderWithStructStorageBuffer_StoresElementSize()
    {
        string shaderPath = CreateTemporaryShaderFile(FragmentShaderWithStructStorageBuffer);
        SdlangCompiler compiler = SdlangCompilerTestFactory.Create();
        compiler.Compile([shaderPath], force: true);

        string metadataPath = Path.ChangeExtension(
            Path.Combine(_testDir, ".generated", Path.GetFileName(shaderPath)),
            ".metadata.json");
        string json = File.ReadAllText(metadataPath);

        GraphicsShaderProgramMetadataDto? metadata = JsonSerializer.Deserialize(
            json,
            ShaderMetadataJsonContext.Default.GraphicsShaderProgramMetadataDto);

        Assert.That(metadata, Is.Not.Null);
        // MyData: float4 (16) + float2 (8) + float (4) = 28 bytes at slot 0
        Assert.That(metadata.Fragment.BindingLayout.StorageBufferElementSizes.Slot0, Is.EqualTo(28u));
        Assert.That(metadata.Fragment.BindingLayout.StorageBufferElementSizes.Slot1, Is.EqualTo(0u));
    }

    [Test]
    public void CompileShader_FragmentShaderWithPrimitiveStorageBuffer_StoresElementSize()
    {
        string shaderPath = CreateTemporaryShaderFile(FragmentShaderWithPrimitiveStorageBuffer);
        SdlangCompiler compiler = SdlangCompilerTestFactory.Create();
        compiler.Compile([shaderPath], force: true);

        string metadataPath = Path.ChangeExtension(
            Path.Combine(_testDir, ".generated", Path.GetFileName(shaderPath)),
            ".metadata.json");
        string json = File.ReadAllText(metadataPath);

        GraphicsShaderProgramMetadataDto? metadata = JsonSerializer.Deserialize(
            json,
            ShaderMetadataJsonContext.Default.GraphicsShaderProgramMetadataDto);

        Assert.That(metadata, Is.Not.Null);
        // float4: 4 floats * 4 bytes = 16 bytes at slot 0
        Assert.That(metadata.Fragment.BindingLayout.StorageBufferElementSizes.Slot0, Is.EqualTo(16u));
    }

    private const string VertexShaderWithStorageAndUniformBuffers = """
                                                                    struct VoxelData {
                                                                        float positionX;
                                                                        float positionY;
                                                                        float positionZ;
                                                                    };

                                                                    StructuredBuffer<VoxelData> voxelData : register(t0, space0);
                                                                    StructuredBuffer<uint> visibleIndices : register(t1, space0);

                                                                    ConstantBuffer<float4x4> viewProjection : register(b0, space1);
                                                                    ConstantBuffer<float3> offset : register(b1, space1);

                                                                    struct Input {
                                                                        float3 Position : TEXCOORD0;
                                                                        uint InstanceID : SV_InstanceID;
                                                                    };

                                                                    struct VertexToFragment {
                                                                        float4 position : SV_Position;
                                                                    };

                                                                    [shader("vertex")]
                                                                    VertexToFragment vertexMain(Input input) {
                                                                        uint idx = visibleIndices[input.InstanceID];
                                                                        VoxelData v = voxelData[idx];
                                                                        float3 worldPos = input.Position + float3(v.positionX, v.positionY, v.positionZ) + offset;
                                                                        VertexToFragment output;
                                                                        output.position = mul(viewProjection, float4(worldPos, 1.0));
                                                                        return output;
                                                                    }

                                                                    [shader("fragment")]
                                                                    float4 fragmentMain(VertexToFragment input) : SV_Target0 {
                                                                        return input.position;
                                                                    }
                                                                    """;

    [Test]
    public void CompileShader_VertexWithStorageAndUniformBuffers_MetalHasNonConflictingBufferIndices()
    {
        string shaderPath = CreateTemporaryShaderFile(VertexShaderWithStorageAndUniformBuffers);

        SdlangCompiler compiler = SdlangCompilerTestFactory.Create();
        compiler.Compile([shaderPath], force: true);

        string metalPath = Path.Combine(
            _testDir, ".generated",
            Path.GetFileNameWithoutExtension(shaderPath) + ".vertex.metal");

        Assert.That(File.Exists(metalPath), Is.True, "Metal file should be created");

        string metalContent = File.ReadAllText(metalPath);

        // Extract all [[buffer(N)]] indices from the function signature
        var bufferIndices = System.Text.RegularExpressions.Regex.Matches(metalContent, @"\[\[buffer\((\d+)\)\]\]")
            .Cast<System.Text.RegularExpressions.Match>()
            .Select(m => int.Parse(m.Groups[1].Value))
            .ToList();

        // All buffer indices must be unique (no conflicts)
        Assert.That(bufferIndices, Is.Unique,
            $"Metal buffer indices must be unique but found: [{string.Join(", ", bufferIndices)}]");

        // SDL GPU's MSL binding order is uniform buffers first, then storage buffers.
        Assert.That(bufferIndices, Does.Contain(0), "First uniform buffer should be at index 0");
        Assert.That(bufferIndices, Does.Contain(1), "Second uniform buffer should be at index 1");
        Assert.That(bufferIndices, Does.Contain(2), "First storage buffer should be at index 2");
        Assert.That(bufferIndices, Does.Contain(3), "Second storage buffer should be at index 3");
        Assert.That(metalContent, Does.Match(@"viewProjection_\d+\s+\[\[buffer\(0\)\]\]"));
        Assert.That(metalContent, Does.Match(@"offset_\d+\s+\[\[buffer\(1\)\]\]"));
        Assert.That(metalContent, Does.Match(@"voxelData_\d+\s+\[\[buffer\(2\)\]\]"));
        Assert.That(metalContent, Does.Match(@"visibleIndices_\d+\s+\[\[buffer\(3\)\]\]"));
    }

    private string CreateTemporaryShaderFile(string shaderContent)
    {
        string filename = Path.ChangeExtension(Path.GetRandomFileName(), ".slang");
        string shaderPath = Path.Combine(_testDir, filename);
        File.WriteAllText(shaderPath, shaderContent);
        return shaderPath;
    }

    private static GraphicsShaderProgramMetadataDto ReadGraphicsMetadata(string metadataPath)
    {
        string json = File.ReadAllText(metadataPath);
        GraphicsShaderProgramMetadataDto? metadata = JsonSerializer.Deserialize(
            json,
            ShaderMetadataJsonContext.Default.GraphicsShaderProgramMetadataDto);
        return metadata ?? throw new InvalidOperationException($"Unable to read shader metadata from {metadataPath}");
    }

    private void AssertGraphicsGeneratedTargets(GraphicsShaderProgramMetadataDto metadata, string filename)
    {
        AssertGeneratedTargets(metadata.Vertex.Shaders, $"{filename}.vertex", "vertexMain");
        AssertGeneratedTargets(metadata.Fragment.Shaders, $"{filename}.fragment", "fragmentMain");
    }

    private void AssertGeneratedTargets(
        IReadOnlyCollection<ShaderInstanceDto> shaders,
        string filename,
        string sourceEntryPoint = "main")
    {
        Assert.Multiple(() =>
        {
            Assert.That(
                shaders.Select(shader => shader.Format),
                Is.EqualTo(new[] { ShaderFormatDto.SpirV, ShaderFormatDto.Dxil, ShaderFormatDto.Msl }));
            Assert.That(
                shaders.Select(shader => shader.EntryPoint),
                Is.EqualTo(new[]
                {
                    "main",
                    sourceEntryPoint,
                    sourceEntryPoint == "main" ? "main_0" : sourceEntryPoint
                }));
            Assert.That(File.Exists(Path.Combine(_testDir, ".generated", $"{filename}.spv")), Is.True);
            Assert.That(File.Exists(Path.Combine(_testDir, ".generated", $"{filename}.dxil")), Is.True);
            Assert.That(File.Exists(Path.Combine(_testDir, ".generated", $"{filename}.metal")), Is.True);
        });
    }
}
