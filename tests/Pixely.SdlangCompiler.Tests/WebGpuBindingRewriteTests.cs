using Pixely.ShaderCommon;

namespace Pixely.SdlangCompiler.Tests;

// The WGSL binding rewrite on hand-written WGSL, so the failure paths and the declaration forms Slang emits are covered
// without a compile.
public class WebGpuBindingRewriteTests
{
    private string _testDir = null!;

    [SetUp]
    public void SetUp()
    {
        _testDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(_testDir);
    }

    [TearDown]
    public void TearDown()
    {
        Directory.Delete(_testDir, true);
    }

    private string Rewrite(string wgsl, ShaderStageDto stage, List<ResourceBinding> bindings)
    {
        string path = Path.Combine(_testDir, "shader.wgsl");
        File.WriteAllText(path, wgsl);
        SdlangCompiler.NormalizeWebGpuBindings(new FileInfo(path), stage, bindings);
        return File.ReadAllText(path);
    }

    [Test]
    public void InterleavesPairsThenStorageInEitherAttributeOrder()
    {
        string wgsl = """
            @binding(0) @group(2) var albedo_0 : texture_2d<f32>;
            @group(2) @binding(0) var albedoSampler_0 : sampler;
            @binding(1) @group(2) var mask_0 : texture_2d<f32>; @binding(1) @group(2) var maskSampler_0 : sampler_comparison;
            @binding(2) @group(2) var<storage, read> palette_0 : array<vec4<f32>>;
            @binding(0) @group(3) var<uniform> tint_0 : vec4<f32>;
            """;
        List<ResourceBinding> bindings =
        [
            new("albedo", ResourceType.SampledTexture, 2, 0),
            new("albedoSampler", ResourceType.Sampler, 2, 0),
            new("mask", ResourceType.SampledTexture, 2, 1),
            new("maskSampler", ResourceType.Sampler, 2, 1),
            new("palette", ResourceType.StorageBuffer, 2, 2),
            new("tint", ResourceType.UniformBuffer, 3, 0)
        ];

        string rewritten = Rewrite(wgsl, ShaderStageDto.Fragment, bindings);

        Assert.Multiple(() =>
        {
            Assert.That(rewritten, Does.Contain("@group(2) @binding(0) var albedo_0"));
            Assert.That(rewritten, Does.Contain("@group(2) @binding(1) var albedoSampler_0"));
            Assert.That(rewritten, Does.Contain("@group(2) @binding(2) var mask_0"));
            Assert.That(rewritten, Does.Contain("@group(2) @binding(3) var maskSampler_0 : sampler_comparison"));
            Assert.That(rewritten, Does.Contain("@group(2) @binding(4) var<storage, read> palette_0"));
            Assert.That(rewritten, Does.Contain("@group(3) @binding(0) var<uniform> tint_0"));
        });
    }

    [Test]
    public void ComputeGroupsKeepTheirOwnNumbering()
    {
        string wgsl = """
            @binding(0) @group(0) var source_0 : texture_2d<f32>;
            @binding(0) @group(0) var sourceSampler_0 : sampler;
            @binding(0) @group(1) var output_0 : texture_storage_2d<rgba8unorm, write>;
            @binding(1) @group(1) var<storage, read_write> totals_0 : array<vec4<f32>>;
            @binding(0) @group(2) var<uniform> time_0 : f32;
            """;
        List<ResourceBinding> bindings =
        [
            new("source", ResourceType.SampledTexture, 0, 0),
            new("sourceSampler", ResourceType.Sampler, 0, 0),
            new("output", ResourceType.ReadWriteStorageTexture, 1, 0),
            new("totals", ResourceType.ReadWriteStorageBuffer, 1, 1),
            new("time", ResourceType.UniformBuffer, 2, 0)
        ];

        string rewritten = Rewrite(wgsl, ShaderStageDto.Compute, bindings);

        Assert.Multiple(() =>
        {
            Assert.That(rewritten, Does.Contain("@group(0) @binding(1) var sourceSampler_0"));
            Assert.That(rewritten, Does.Contain("@group(1) @binding(0) var output_0"));
            Assert.That(rewritten, Does.Contain("@group(1) @binding(1) var<storage, read_write> totals_0"));
            Assert.That(rewritten, Does.Contain("@group(2) @binding(0) var<uniform> time_0"));
        });
    }

    // A buffer's element type is user-named, so the address space decides, not the type prefix.
    [Test]
    public void BufferWhoseTypeNameStartsLikeAHandleIsABuffer()
    {
        string wgsl = """
            @binding(0) @group(3) var<uniform> settings_0 : sampler_settings_std140_0;
            @binding(0) @group(2) var<storage, read> lookup_0 : array<texture_entry_0>;
            """;
        List<ResourceBinding> bindings =
        [
            new("settings", ResourceType.UniformBuffer, 3, 0),
            new("lookup", ResourceType.StorageBuffer, 2, 0)
        ];

        string rewritten = Rewrite(wgsl, ShaderStageDto.Fragment, bindings);

        Assert.Multiple(() =>
        {
            Assert.That(rewritten, Does.Contain("@group(3) @binding(0) var<uniform> settings_0"));
            Assert.That(rewritten, Does.Contain("@group(2) @binding(0) var<storage, read> lookup_0"));
        });
    }

    [Test]
    public void DeclarationTheReflectionDoesNotListFails()
    {
        string wgsl = "@binding(0) @group(2) var albedo_0 : texture_2d<f32>;\n";

        ShaderBindingValidationException? exception = Assert.Throws<ShaderBindingValidationException>(() =>
            Rewrite(wgsl, ShaderStageDto.Fragment, []));

        Assert.That(exception.Message, Does.Contain("'albedo_0'").And.Contain("reflection lists no such resource"));
    }

    [Test]
    public void ReflectedResourceWithoutADeclarationFails()
    {
        string wgsl = "@binding(0) @group(3) var<uniform> tint_0 : vec4<f32>;\n";
        List<ResourceBinding> bindings =
        [
            new("tint", ResourceType.UniformBuffer, 3, 0),
            new("albedo", ResourceType.SampledTexture, 2, 0)
        ];

        ShaderBindingValidationException? exception = Assert.Throws<ShaderBindingValidationException>(() =>
            Rewrite(wgsl, ShaderStageDto.Fragment, bindings));

        Assert.That(exception.Message, Does.Contain("'albedo'").And.Contain("no declaration in the generated WGSL"));
    }

    [Test]
    public void UnknownDeclarationFormFails()
    {
        string wgsl = "@binding(0) @group(2) var textures_0 : array<texture_2d<f32>, i32(2)>;\n";
        string message = "";
        try
        {
            Rewrite(wgsl, ShaderStageDto.Fragment, [new("textures", ResourceType.SampledTexture, 2, 0)]);
        }
        catch (ShaderBindingValidationException exception)
        {
            message = exception.Message;
        }

        Assert.That(message, Does.Contain("'textures_0'").And.Contain("not a resource the WebGPU binding rewrite knows"));
    }
}
