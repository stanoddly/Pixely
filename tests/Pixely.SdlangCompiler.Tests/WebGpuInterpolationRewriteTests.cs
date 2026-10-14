namespace Pixely.SdlangCompiler.Tests;

public class WebGpuInterpolationRewriteTests
{
    [Test]
    public void RemovesInterpolateFromStructMemberWithoutLocation()
    {
        string wgsl = """
            struct VertexToFragment_0
            {
                 Position_0 : vec4<f32>,
                @interpolate(flat)  Color_0 : vec4<f32>,
                @interpolate(perspective, centroid) @invariant Normal_0 : vec3<f32>,
            };
            """;

        string rewritten = SdlangCompiler.RemoveWebGpuInterpolationWithoutLocation(wgsl);

        Assert.Multiple(() =>
        {
            Assert.That(rewritten, Does.Not.Contain("@interpolate"));
            Assert.That(rewritten, Does.Contain("Color_0 : vec4<f32>,"));
            Assert.That(rewritten, Does.Contain("@invariant Normal_0 : vec3<f32>,"));
        });
    }

    [Test]
    public void RemovesInterpolateFromParameterWithoutLocation()
    {
        string wgsl = "fn shade_0(@interpolate(flat) color_0 : vec4<f32>, @builtin(position) position_0 : vec4<f32>) -> vec4<f32>";

        string rewritten = SdlangCompiler.RemoveWebGpuInterpolationWithoutLocation(wgsl);

        Assert.That(rewritten, Is.EqualTo("fn shade_0(color_0 : vec4<f32>, @builtin(position) position_0 : vec4<f32>) -> vec4<f32>"));
    }

    [Test]
    public void KeepsInterpolateWithLocation()
    {
        string wgsl = """
            struct pixelInput_0
            {
                @interpolate(flat) @location(0) Color_1 : vec4<f32>,
                @location(1) @interpolate(linear) Weight_0 : f32,
            };

            @fragment
            fn fragmentMain( _S1 : pixelInput_0, @interpolate(flat) @location(2) id_0 : u32) -> @location(0) vec4<f32>
            """;

        Assert.That(SdlangCompiler.RemoveWebGpuInterpolationWithoutLocation(wgsl), Is.EqualTo(wgsl));
    }
}
