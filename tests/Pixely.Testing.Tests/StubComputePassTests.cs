using Pixely.App;
using Pixely.Gpu;
using Pixely.Shaders;

namespace Pixely.Testing.Tests;

public class StubComputePassTests
{
    [Test]
    public void Dispatch_IsRecordedWithItsPipelineBindingsAndUniforms()
    {
        using IPixelyApp app = BuildApp();
        StubGpuDevice device = app.GetRequiredService<StubGpuDevice>();
        using ComputePipeline pipeline = app.GetRequiredService<ComputePipelineBuilder>().Build(app.GetRequiredService<IComputeShaderLoader>().Load("shaders/fill"));
        GpuStorageBuffer<int> output = app.GetRequiredService<GpuMemorySystem>().CreateStorageBuffer<int>(256, StorageBufferUsage.ComputeStorageWrite);
        StubCommandBuffer commandBuffer = (StubCommandBuffer)device.AcquireCommandBuffer();

        commandBuffer.PushComputeUniformData(0, 7);
        using (ComputePass computePass = commandBuffer.CreateComputePass([], [new StorageBufferReadWriteBinding { Buffer = output }]))
        {
            computePass.BindComputePipeline(pipeline);
            computePass.Dispatch(4, 1, 1);
        }

        commandBuffer.Submit();

        StubComputePass recordedPass = commandBuffer.ComputePasses.Single();
        StubDispatch dispatch = recordedPass.Dispatches.Single();
        Assert.Multiple(() =>
        {
            Assert.That(recordedPass.ReadWriteStorageBuffers.Single().Buffer, Is.SameAs(output));
            Assert.That(recordedPass.IsEnded, Is.True);
            Assert.That(dispatch.Pipeline, Is.SameAs(pipeline));
            Assert.That((dispatch.GroupCountX, dispatch.GroupCountY, dispatch.GroupCountZ), Is.EqualTo((4u, 1u, 1u)));
            Assert.That(dispatch.IndirectBuffer, Is.Null);
            Assert.That(dispatch.ReadUniform<int>(0), Is.EqualTo(7));
        });
    }

    [Test]
    public void Dispatch_WithoutThePipelinesReadWriteBuffer_FailsLikeOnTheGpu()
    {
        using IPixelyApp app = BuildApp();
        StubGpuDevice device = app.GetRequiredService<StubGpuDevice>();
        using ComputePipeline pipeline = app.GetRequiredService<ComputePipelineBuilder>().Build(app.GetRequiredService<IComputeShaderLoader>().Load("shaders/fill"));
        using CommandBuffer commandBuffer = device.AcquireCommandBuffer();
        using ComputePass computePass = commandBuffer.CreateComputePass();

        computePass.BindComputePipeline(pipeline);

        Assert.Throws<InvalidOperationException>(() => computePass.Dispatch(4, 1, 1));
    }

    private static IPixelyApp BuildApp()
    {
        PixelyAppBuilder builder = new();
        builder.UseDefaultContent();
        builder.UseStubGpu();
        return builder.Build();
    }
}
