using Pixely.Utilities;
using SDL;

namespace Pixely.Gpu;

/// <summary>
/// Records uploads into a native copy pass. Buffer uploads share the <see cref="UploadTransferBuffer"/> that outlives the pass,
/// which SDL cycles once per submission, so a steady stream of buffer updates does not create a transfer buffer per update.
/// </summary>
internal sealed class SdlCopyPass : CopyPass
{
    // WebGPU needs offsets that are multiples of 4; 16 also keeps every vertex and storage element aligned.
    private const uint Alignment = 16;

    private readonly SdlGpuDevice _gpuDevice;
    private readonly Pointer<SDL_GPUCopyPass> _sdlCopyPass;
    private readonly UploadTransferBuffer _transferBuffer;

    private uint _transferBufferOffset;

    internal SdlCopyPass(SdlGpuDevice gpuDevice, Pointer<SDL_GPUCopyPass> sdlCopyPass, UploadTransferBuffer transferBuffer) : base(gpuDevice)
    {
        _gpuDevice = gpuDevice;
        _sdlCopyPass = sdlCopyPass;
        _transferBuffer = transferBuffer;
    }

    internal override void EndCore()
    {
        unsafe
        {
            SDL3.SDL_EndGPUCopyPass(_sdlCopyPass);
        }
    }

    // An update passes cycle = true: without it, the upload overwrites a buffer that an earlier submission may still read, such
    // as the previous frame or another window in this frame. SDL then writes into a copy of the buffer when a command buffer in
    // flight or still recording uses it, and overwrites in place otherwise. A new buffer passes false: nothing reads it yet.
    // Cycling has these costs:
    // - The copy starts undefined, so an update replaces the whole contents, not a prefix.
    // - A pass that bound the buffer before the update keeps the previous copy until it binds the buffer again.
    // - Each copy has the buffer's full size: one per submission in flight, plus one per extra update in the same submission.
    //   SDL keeps the copies until the buffer is released, and the GPU memory tracking does not count them.
    // - In the browser, uploads are already ordered against earlier submissions, so the copies there prevent no race.
    internal override void UploadToBufferCore(ReadOnlySpan<byte> data, Pointer<SDL_GPUBuffer> buffer, bool cycle)
    {
        uint sizeBytes = (uint)data.Length;
        uint offset = AlignUp(_transferBufferOffset);
        bool fits = offset + (ulong)sizeBytes <= _transferBuffer.Capacity;

        unsafe
        {
            SDL_GPUBufferRegion destination = new SDL_GPUBufferRegion { buffer = buffer, offset = 0, size = sizeBytes };

            // A full buffer at its largest keeps its size: growing it no further, the upload gets a transfer buffer of its own.
            if (sizeBytes > UploadTransferBuffer.MaxCapacity || (!fits && _transferBuffer.Capacity == UploadTransferBuffer.MaxCapacity))
            {
                Pointer<SDL_GPUTransferBuffer> temporary = CreateFilledTransferBuffer(data);
                SDL_GPUTransferBufferLocation temporarySource = new SDL_GPUTransferBufferLocation { transfer_buffer = temporary, offset = 0 };
                SdlBoolInterop.SDL_UploadToGPUBuffer(_sdlCopyPass, &temporarySource, &destination, cycle);
                _gpuDevice.ReleaseTransferBufferCore(temporary);
            }
            else
            {
                if (!fits)
                {
                    _transferBuffer.Grow(sizeBytes);
                    offset = 0;
                }

                // Only a write at offset 0 cycles: the first of a submission, so that SDL hands out a copy no submission in flight
                // still reads, or the first into a grown buffer, which nothing uses yet, so SDL does not copy it. Later writes must
                // not cycle: the submission's own uploads already mark the buffer as in use, so cycling would create a copy per upload.
                Write(data, _transferBuffer.SdlTransferBuffer, offset, offset == 0);
                _transferBufferOffset = offset + sizeBytes;

                SDL_GPUTransferBufferLocation source = new SDL_GPUTransferBufferLocation { transfer_buffer = _transferBuffer.SdlTransferBuffer, offset = offset };
                SdlBoolInterop.SDL_UploadToGPUBuffer(_sdlCopyPass, &source, &destination, cycle);
            }
        }
    }

    // Textures are uploaded at load time and can be large, so each gets a transfer buffer of its own rather than growing the
    // shared one for good. It also leaves D3D12's 512-byte offset alignment for texture copies to the offset 0 of a new buffer.
    internal override void UploadToTextureCore(ReadOnlySpan<byte> data, Pointer<SDL_GPUTexture> texture, uint layer, uint width, uint height)
    {
        Pointer<SDL_GPUTransferBuffer> temporary = CreateFilledTransferBuffer(data);

        unsafe
        {
            SDL_GPUTextureTransferInfo source = new SDL_GPUTextureTransferInfo { transfer_buffer = temporary, offset = 0 };
            SDL_GPUTextureRegion destination = new SDL_GPUTextureRegion { texture = texture, layer = layer, w = width, h = height, d = 1 };
            SdlBoolInterop.SDL_UploadToGPUTexture(_sdlCopyPass, &source, &destination, false);
        }

        _gpuDevice.ReleaseTransferBufferCore(temporary);
    }

    private Pointer<SDL_GPUTransferBuffer> CreateFilledTransferBuffer(ReadOnlySpan<byte> data)
    {
        Pointer<SDL_GPUTransferBuffer> transferBuffer = _gpuDevice.CreateUploadTransferBufferCore((uint)data.Length);
        try
        {
            Write(data, transferBuffer, 0, false);
        }
        catch
        {
            _gpuDevice.ReleaseTransferBufferCore(transferBuffer);
            throw;
        }

        return transferBuffer;
    }

    private void Write(ReadOnlySpan<byte> data, Pointer<SDL_GPUTransferBuffer> transferBuffer, uint offset, bool cycle)
    {
        unsafe
        {
            byte* mapped = (byte*)SdlBoolInterop.SDL_MapGPUTransferBuffer(_gpuDevice.Pointer, transferBuffer, cycle);
            SdlError.ThrowOnNull(mapped);
            data.CopyTo(new Span<byte>(mapped + offset, data.Length));
            SDL3.SDL_UnmapGPUTransferBuffer(_gpuDevice.Pointer, transferBuffer);
        }
    }

    private static uint AlignUp(uint offset)
    {
        return (offset + Alignment - 1) & ~(Alignment - 1);
    }
}
