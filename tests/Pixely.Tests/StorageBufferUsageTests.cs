using Pixely.Gpu;

namespace Pixely.Tests;

// The checks run before the device is touched, so buffers and a memory system without one are enough.
public class StorageBufferUsageTests
{
    [TestCase(0)]
    [TestCase(-1)]
    public void CreateStorageBuffer_LengthNotPositive_Throws(int length)
    {
        GpuMemorySystem gpuMemorySystem = new(null!);

        Assert.Throws<ArgumentOutOfRangeException>(() => gpuMemorySystem.CreateStorageBuffer<int>(length, StorageBufferUsage.ComputeStorageWrite));
    }

    [Test]
    public void CreateStorageBuffer_SizeUnder4Bytes_Throws()
    {
        GpuMemorySystem gpuMemorySystem = new(null!);

        Assert.Throws<ArgumentOutOfRangeException>(() => gpuMemorySystem.CreateStorageBuffer<byte>(3, StorageBufferUsage.ComputeStorageWrite));
    }

    [Test]
    public void CreateStorageBuffer_SizeOverIntMaxValue_Throws()
    {
        GpuMemorySystem gpuMemorySystem = new(null!);

        // 2^29 elements of 4 bytes are 2^31 bytes, one more than int.MaxValue.
        Assert.Throws<ArgumentOutOfRangeException>(() => gpuMemorySystem.CreateStorageBuffer<int>(1 << 29, StorageBufferUsage.ComputeStorageWrite));
    }

    [TestCase(0)]
    [TestCase(3)]
    public void CreateStorageBuffer_DataUnder4Bytes_Throws(int length)
    {
        CopyPass copyPass = new(null!, default, null!);

        Assert.Throws<ArgumentOutOfRangeException>(() => copyPass.CreateStorageBuffer<byte>(new byte[length]));
    }

    [TestCase(4, 4u)]
    [TestCase(5, 8u)]
    [TestCase(6, 8u)]
    [TestCase(8, 8u)]
    public void ValidateAllocationSize_RoundsUpToMultipleOf4(int length, uint expected)
    {
        Assert.That(GpuStorageBuffer<byte>.ValidateAllocationSize(length, "length"), Is.EqualTo(expected));
    }

    [TestCase(0u)]
    [TestCase(1u)]
    [TestCase(0x40u | (uint)StorageBufferUsage.GraphicsStorageRead)]
    public void CreateStorageBuffer_UsageNoneOrUndefined_Throws(uint usage)
    {
        GpuMemorySystem gpuMemorySystem = new(null!);

        Assert.Throws<ArgumentException>(() => gpuMemorySystem.CreateStorageBuffer<int>(4, (StorageBufferUsage)usage));
    }

    [TestCase(StorageBufferUsage.GraphicsStorageRead, StorageBufferUsage.GraphicsStorageRead)]
    [TestCase(StorageBufferUsage.ComputeStorageRead | StorageBufferUsage.GraphicsStorageRead, StorageBufferUsage.GraphicsStorageRead)]
    [TestCase(StorageBufferUsage.ComputeStorageWrite | StorageBufferUsage.GraphicsStorageRead, StorageBufferUsage.ComputeStorageWrite)]
    public void ValidateUsage_WithRequiredUsage_Succeeds(StorageBufferUsage usage, StorageBufferUsage requiredUsage)
    {
        Assert.DoesNotThrow(() => CreateBuffer(usage).ValidateUsage("Fragment storage buffer", 0, requiredUsage));
    }

    [TestCase(StorageBufferUsage.GraphicsStorageRead, StorageBufferUsage.ComputeStorageWrite)]
    [TestCase(StorageBufferUsage.GraphicsStorageRead, StorageBufferUsage.ComputeStorageRead)]
    [TestCase(StorageBufferUsage.ComputeStorageWrite, StorageBufferUsage.GraphicsStorageRead)]
    public void ValidateUsage_WithoutRequiredUsage_ThrowsNamingSlotAndUsages(StorageBufferUsage usage, StorageBufferUsage requiredUsage)
    {
        InvalidOperationException? exception = Assert.Throws<InvalidOperationException>(() => CreateBuffer(usage).ValidateUsage("Compute read-write storage buffer", 2, requiredUsage));

        Assert.That(exception!.Message, Does.Contain("Compute read-write storage buffer slot 2").And.Contain($"'{usage}'").And.Contain(requiredUsage.ToString()));
    }

    private static GpuStorageBuffer<int> CreateBuffer(StorageBufferUsage usage)
    {
        return new GpuStorageBuffer<int>(null!, default, 1, usage);
    }
}
