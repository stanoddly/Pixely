using System.Reflection;
using System.Runtime.CompilerServices;
using Pixely.Gpu;
using Pixely.Utilities;
using SDL;

namespace Pixely.Tests;

public class FrameObjectReuseTests
{
    [Test]
    public void CreateRenderPassBuilder_WhenReusing_ReturnsTheSameBuilder()
    {
        CommandBuffer commandBuffer = CreateRecordingCommandBuffer(reusesFrameObjects: true);

        Assert.That(commandBuffer.CreateRenderPassBuilder(), Is.SameAs(commandBuffer.CreateRenderPassBuilder()));
    }

    [Test]
    public void CreateRenderPassBuilder_UnderValidation_ReturnsANewBuilder()
    {
        CommandBuffer commandBuffer = CreateRecordingCommandBuffer(reusesFrameObjects: false);

        Assert.That(commandBuffer.CreateRenderPassBuilder(), Is.Not.SameAs(commandBuffer.CreateRenderPassBuilder()));
    }

    [Test]
    public void CreateRenderPassBuilder_WhenReusing_DropsTargetsOfAnAbandonedBuild()
    {
        CommandBuffer commandBuffer = CreateRecordingCommandBuffer(reusesFrameObjects: true);
        Texture texture = (Texture)RuntimeHelpers.GetUninitializedObject(typeof(UserTexture));
        commandBuffer.CreateRenderPassBuilder().AddColorTarget(texture).SetSharedColorTargetSettings(ColorTargetSettings.Clear);

        // The target added above is gone, so the reused builder has nothing to build.
        InvalidOperationException? exception = Assert.Throws<InvalidOperationException>(() => commandBuffer.CreateRenderPassBuilder().Build());
        Assert.That(exception!.Message, Does.Contain("At least one color target"));
    }

    [Test]
    public void CreateRenderPassBuilder_AfterSubmit_Throws()
    {
        CommandBuffer commandBuffer = CreateRecordingCommandBuffer(reusesFrameObjects: false);
        commandBuffer.Begin(Pointer<SDL_GPUCommandBuffer>.Null);

        Assert.Throws<ObjectDisposedException>(() => commandBuffer.CreateRenderPassBuilder());
    }

    // A command buffer that has begun recording on a pointer SDL never sees: creating a builder does not reach SDL.
    private static unsafe CommandBuffer CreateRecordingCommandBuffer(bool reusesFrameObjects)
    {
        GpuDevice gpuDevice = (GpuDevice)RuntimeHelpers.GetUninitializedObject(typeof(GpuDevice));
        FieldInfo field = typeof(GpuDevice).GetField($"<{nameof(GpuDevice.ReusesFrameObjects)}>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!;
        field.SetValue(gpuDevice, reusesFrameObjects);

        CommandBuffer commandBuffer = new CommandBuffer(gpuDevice);
        commandBuffer.Begin(new Pointer<SDL_GPUCommandBuffer>((SDL_GPUCommandBuffer*)1));
        return commandBuffer;
    }
}
