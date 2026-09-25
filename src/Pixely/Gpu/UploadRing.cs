using Pixely.Utilities;
using SDL;

namespace Pixely.Gpu;

/// <summary>
/// Upload transfer buffers owned by Pixely and reused across submissions, so that a steady stream of buffer updates does not
/// create a native transfer buffer per update. A submission writes into one slot at increasing offsets. A slot is reused
/// once the fence of the submission that last wrote into it has signalled. The fence is only queried, never waited on,
/// because waiting suspends the wasm stack in the browser; the swapchain acquire already bounds how many submissions are
/// in flight. Maps never cycle, so SDL keeps no hidden copies: what is allocated is what the slots hold.
/// </summary>
internal sealed class UploadRing : IDisposable
{
    // Enough for three frames in flight with two submissions each, plus one being written.
    internal const int MaxSlots = 8;

    // The most one slot grows to. An upload larger than this gets a transfer buffer of its own.
    internal const uint MaxSlotCapacity = 1 << 20;

    private const uint MinSlotCapacity = 64 << 10;

    // WebGPU needs offsets that are multiples of 4; 16 also keeps every vertex and storage element aligned.
    private const uint Alignment = 16;

    private readonly GpuDevice _gpuDevice;
    private readonly List<Slot> _slots = new(MaxSlots);
    private Slot? _current;

    internal UploadRing(GpuDevice gpuDevice)
    {
        _gpuDevice = gpuDevice;
    }

    /// <summary>Whether the open submission writes into a slot and so needs its fence recorded when it is submitted.</summary>
    internal bool NeedsFence => _current != null;

    internal int SlotCount => _slots.Count;

    /// <summary>
    /// Records the fence of the submission, or releases it when the submission wrote into no slot. A null fence means the
    /// submission failed and the slot is free again.
    /// </summary>
    internal void EndSubmission(Pointer<SDL_GPUFence> fence)
    {
        if (_current != null)
        {
            _current.Fence = fence;
            _current.IsWriting = false;
            _current = null;
        }
        else if (!fence.IsNull)
        {
            ReleaseFence(fence);
        }
    }

    /// <summary>The submission was cancelled, so nothing it wrote will be read.</summary>
    internal void CancelSubmission()
    {
        if (_current != null)
        {
            _current.IsWriting = false;
            _current.Offset = 0;
            _current = null;
        }
    }

    /// <summary>
    /// Reserves <paramref name="size"/> bytes in the submission's slot, taking a free slot on the submission's first reserve and
    /// growing or replacing its transfer buffer when the bytes do not fit. An upload larger than <see cref="MaxSlotCapacity"/>,
    /// one that does not fit a slot already at that capacity, or one made while all <see cref="MaxSlots"/> slots are busy, gets
    /// a transfer buffer of its own that <see cref="Complete"/> releases.
    /// </summary>
    internal UploadAllocation Reserve(uint size)
    {
        if (size > MaxSlotCapacity)
        {
            return ReserveTemporary(size);
        }

        // A submission that only uploads textures or oversized data takes no slot, so it needs no fence.
        _current ??= TakeFreeSlot();
        if (_current == null)
        {
            return ReserveTemporary(size);
        }

        uint offset = AlignUp(_current.Offset);

        if (_current.TransferBuffer.IsNull || offset + (ulong)size > _current.Capacity)
        {
            // A full slot at its largest keeps its buffer: replacing it would create one per submission for as long as the
            // uploads outgrow it, while a temporary buffer costs the same once and leaves the slot for the next submission.
            if (_current.Capacity == MaxSlotCapacity && !_current.TransferBuffer.IsNull)
            {
                return ReserveTemporary(size);
            }

            // Uploads already recorded from the old buffer still read it, which SDL allows: a released buffer is freed
            // only once the GPU is done with it.
            // The new buffer is created first, so a failed create leaves the slot with a buffer it still owns.
            uint capacity = Math.Min(MaxSlotCapacity, Math.Max(Math.Max(MinSlotCapacity, _current.Capacity * 2), System.Numerics.BitOperations.RoundUpToPowerOf2(size)));
            Pointer<SDL_GPUTransferBuffer> transferBuffer = CreateTransferBuffer(capacity);
            ReleaseTransferBuffer(_current.TransferBuffer);
            _current.TransferBuffer = transferBuffer;
            _current.Capacity = capacity;
            offset = 0;
        }

        _current.Offset = offset + size;
        return new UploadAllocation(_current.TransferBuffer, offset, false);
    }

    /// <summary>A transfer buffer of exactly <paramref name="size"/> bytes, released by <see cref="Complete"/>.</summary>
    internal UploadAllocation ReserveTemporary(uint size)
    {
        return new UploadAllocation(CreateTransferBuffer(size), 0, true);
    }

    /// <summary>Called once the upload from <paramref name="allocation"/> is recorded.</summary>
    internal void Complete(UploadAllocation allocation)
    {
        if (allocation.IsTemporary)
        {
            ReleaseTransferBuffer(allocation.TransferBuffer);
        }
    }

    public void Dispose()
    {
        foreach (Slot slot in _slots)
        {
            ReleaseFence(slot.Fence);
            ReleaseTransferBuffer(slot.TransferBuffer);
        }

        _slots.Clear();
        _current = null;
    }

    private Slot? TakeFreeSlot()
    {
        foreach (Slot slot in _slots)
        {
            if (IsFree(slot))
            {
                return Claim(slot);
            }
        }

        if (_slots.Count < MaxSlots)
        {
            Slot slot = new Slot();
            _slots.Add(slot);
            return Claim(slot);
        }

        return null;
    }

    private bool IsFree(Slot slot)
    {
        if (slot.IsWriting)
        {
            return false;
        }

        if (slot.Fence.IsNull)
        {
            return true;
        }

        unsafe
        {
            if (!SDL3.SDL_QueryGPUFence(_gpuDevice.SdlGpuDevice, slot.Fence))
            {
                return false;
            }
        }

        ReleaseFence(slot.Fence);
        slot.Fence = Pointer<SDL_GPUFence>.Null;
        return true;
    }

    private static Slot Claim(Slot slot)
    {
        slot.IsWriting = true;
        slot.Offset = 0;
        return slot;
    }

    private Pointer<SDL_GPUTransferBuffer> CreateTransferBuffer(uint size)
    {
        unsafe
        {
            SDL_GPUTransferBufferCreateInfo createInfo = new SDL_GPUTransferBufferCreateInfo
            {
                usage = SDL_GPUTransferBufferUsage.SDL_GPU_TRANSFERBUFFERUSAGE_UPLOAD,
                size = size
            };
            Pointer<SDL_GPUTransferBuffer> transferBuffer = SDL3.SDL_CreateGPUTransferBuffer(_gpuDevice.SdlGpuDevice, &createInfo);
            SdlError.ThrowOnNull(transferBuffer);
            return transferBuffer;
        }
    }

    private void ReleaseTransferBuffer(Pointer<SDL_GPUTransferBuffer> transferBuffer)
    {
        if (transferBuffer.IsNull)
        {
            return;
        }

        unsafe
        {
            SDL3.SDL_ReleaseGPUTransferBuffer(_gpuDevice.SdlGpuDevice, transferBuffer);
        }
    }

    private void ReleaseFence(Pointer<SDL_GPUFence> fence)
    {
        if (fence.IsNull)
        {
            return;
        }

        unsafe
        {
            SDL3.SDL_ReleaseGPUFence(_gpuDevice.SdlGpuDevice, fence);
        }
    }

    private static uint AlignUp(uint offset)
    {
        return (offset + Alignment - 1) & ~(Alignment - 1);
    }

    private sealed class Slot
    {
        public Pointer<SDL_GPUTransferBuffer> TransferBuffer;
        public uint Capacity;
        public uint Offset;
        public Pointer<SDL_GPUFence> Fence;
        public bool IsWriting;
    }
}

internal readonly record struct UploadAllocation(Pointer<SDL_GPUTransferBuffer> TransferBuffer, uint Offset, bool IsTemporary);
