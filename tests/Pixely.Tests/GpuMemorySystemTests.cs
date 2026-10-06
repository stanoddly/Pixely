using System.Runtime.CompilerServices;
using Pixely.Gpu;

namespace Pixely.Tests;

public class GpuMemorySystemTests
{
    [TestCase(UploadWait.Automatic, "V3DV Mesa", true)]
    [TestCase(UploadWait.Automatic, "radv", false)]
    [TestCase(UploadWait.Automatic, "NVIDIA", false)]
    [TestCase(UploadWait.Automatic, "v3dv", false)]
    [TestCase(UploadWait.Automatic, null, false)]
    [TestCase(UploadWait.On, "radv", true)]
    [TestCase(UploadWait.On, null, true)]
    [TestCase(UploadWait.Off, "V3DV Mesa", false)]
    public void ShouldWaitForUploads_WaitsOnlyWhenOnOrAutomaticOnV3dv(UploadWait uploadWait, string? nativeDriverName, bool expected)
    {
        Assert.That(GpuMemorySystem.ShouldWaitForUploads(uploadWait, nativeDriverName), Is.EqualTo(expected));
    }

    [Test]
    public void ShouldWaitForUploads_UnknownValue_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => GpuMemorySystem.ShouldWaitForUploads((UploadWait)42, "V3DV Mesa"));
    }

    [Test]
    public void Constructor_DoesNotWaitForUploads()
    {
        Assert.That(new GpuMemorySystem(null!).WaitsForUploads, Is.False);
    }

    // The stub device reports no native driver, so Automatic does not wait.
    [TestCase(UploadWait.Automatic, false)]
    [TestCase(UploadWait.On, true)]
    [TestCase(UploadWait.Off, false)]
    public void CreateGpuMemorySystem_WaitsAsConfigured(UploadWait uploadWait, bool expected)
    {
        using PixelyFactory factory = new(new PixelyConfig(UploadWait: uploadWait), null);

        GpuMemorySystem gpuMemorySystem = factory.CreateGpuMemorySystem((GpuDevice)RuntimeHelpers.GetUninitializedObject(typeof(GpuDevice)));

        Assert.That(gpuMemorySystem.WaitsForUploads, Is.EqualTo(expected));
    }
}
