using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Pixely.Utilities;
using XdlSharp;
using static XdlSharp.Xdl;

namespace Pixely.Audio;

public unsafe sealed class AudioStream : AudioClip
{
    private readonly AudioSystem _audioSystem;
    private readonly Stream _stream;
    private GCHandle _selfHandle;
    private Pointer<SDL_IOStream> _sdlIoStream;
    private AudioSource? _source;
    private bool _disposed;

    internal AudioStream(AudioSystem audioSystem, Stream stream)
    {
        _audioSystem = audioSystem;
        _stream = stream;
    }

    internal override AudioSystem AudioSystem => _audioSystem;

    internal override void AttachTo(AudioSource source)
    {
        ThrowIfDisposed();
        source.ThrowIfDisposed();

        if (_source != null && !ReferenceEquals(_source, source))
        {
            throw new InvalidOperationException("Audio stream is already attached to a source.");
        }

        SdlError.ThrowOnFalse(
            MIX_SetTrackIOStream(source.SdlTrack, GetOrCreateIoStream(), false),
            nameof(MIX_SetTrackIOStream));
        _source = source;
    }

    internal override void DetachFrom(AudioSource source)
    {
        if (!ReferenceEquals(_source, source))
        {
            return;
        }

        SdlError.ThrowOnFalse(
            MIX_SetTrackIOStream(source.SdlTrack, Pointer<SDL_IOStream>.Null, false),
            nameof(MIX_SetTrackIOStream));
        _source = null;
    }

    public override void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _audioSystem.ReleaseStream(this);
    }

    internal void Release()
    {
        if (_disposed)
        {
            return;
        }

        AudioSource? source = _source;
        if (source != null)
        {
            if (source.Pointer.IsNull)
            {
                _source = null;
            }
            else if (ReferenceEquals(source.Clip, this))
            {
                source.Clip = null;
            }
            else
            {
                DetachFrom(source);
            }
        }

        _disposed = true;
        CloseStream();
    }

    private Pointer<SDL_IOStream> GetOrCreateIoStream()
    {
        if (_sdlIoStream.IsNull)
        {
            _sdlIoStream = CreateIoStream();
        }

        return _sdlIoStream;
    }

    private Pointer<SDL_IOStream> CreateIoStream()
    {
        SDL_IOStreamInterface ioInterface;
        SDL_INIT_INTERFACE(&ioInterface);
        ioInterface.size = &Size;
        ioInterface.seek = &Seek;
        ioInterface.read = &Read;
        ioInterface.write = &Write;
        ioInterface.flush = &Flush;
        ioInterface.close = &Close;

        _selfHandle = GCHandle.Alloc(this);
        Pointer<SDL_IOStream> ioStream = SDL_OpenIO(&ioInterface, GCHandle.ToIntPtr(_selfHandle));
        if (ioStream.IsNull)
        {
            _selfHandle.Free();
            SdlError.ThrowOnNull(ioStream, nameof(SDL_OpenIO));
        }

        return ioStream;
    }

    private void CloseIoStream()
    {
        Pointer<SDL_IOStream> ioStream = _sdlIoStream;
        if (ioStream.IsNull)
        {
            return;
        }

        _sdlIoStream = Pointer<SDL_IOStream>.Null;
        SDL_CloseIO(ioStream);
    }

    private void CloseStream()
    {
        if (_sdlIoStream.IsNull)
        {
            _stream.Dispose();
            return;
        }

        CloseIoStream();
    }

    private void CloseFromSdl()
    {
        _sdlIoStream = Pointer<SDL_IOStream>.Null;
        _stream.Dispose();

        if (_selfHandle.IsAllocated)
        {
            _selfHandle.Free();
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(AudioStream));
        }
    }

    private static AudioStream? GetStream(nint userdata)
    {
        if (userdata == 0)
        {
            return null;
        }

        return GCHandle.FromIntPtr(userdata).Target as AudioStream;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static long Size(nint userdata)
    {
        try
        {
            AudioStream? audioStream = GetStream(userdata);
            if (audioStream == null || !audioStream._stream.CanSeek)
            {
                return -1;
            }

            return audioStream._stream.Length;
        }
        catch
        {
            return -1;
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static long Seek(nint userdata, long offset, SDL_IOWhence whence)
    {
        try
        {
            AudioStream? audioStream = GetStream(userdata);
            if (audioStream == null || !audioStream._stream.CanSeek)
            {
                return -1;
            }

            SeekOrigin origin = whence switch
            {
                SDL_IOWhence.SDL_IO_SEEK_SET => SeekOrigin.Begin,
                SDL_IOWhence.SDL_IO_SEEK_CUR => SeekOrigin.Current,
                SDL_IOWhence.SDL_IO_SEEK_END => SeekOrigin.End,
                _ => throw new ArgumentOutOfRangeException(nameof(whence), whence, null)
            };

            return audioStream._stream.Seek(offset, origin);
        }
        catch
        {
            return -1;
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static nuint Read(nint userdata, nint pointer, nuint size, SDL_IOStatus* status)
    {
        try
        {
            AudioStream? audioStream = GetStream(userdata);
            if (audioStream == null || pointer == 0 || !audioStream._stream.CanRead)
            {
                SetStatus(status, SDL_IOStatus.SDL_IO_STATUS_ERROR);
                return 0;
            }

            int bytesRequested = (int)Math.Min(size, (nuint)int.MaxValue);
            Span<byte> buffer = new((void*)pointer, bytesRequested);
            int bytesRead = audioStream._stream.Read(buffer);

            SetStatus(
                status,
                bytesRead == 0 ? SDL_IOStatus.SDL_IO_STATUS_EOF : SDL_IOStatus.SDL_IO_STATUS_READY);
            return (nuint)bytesRead;
        }
        catch
        {
            SetStatus(status, SDL_IOStatus.SDL_IO_STATUS_ERROR);
            return 0;
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static nuint Write(nint userdata, nint pointer, nuint size, SDL_IOStatus* status)
    {
        SetStatus(status, SDL_IOStatus.SDL_IO_STATUS_READONLY);
        return 0;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static byte Flush(nint userdata, SDL_IOStatus* status)
    {
        SetStatus(status, SDL_IOStatus.SDL_IO_STATUS_READY);
        return 1;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static byte Close(nint userdata)
    {
        try
        {
            GetStream(userdata)?.CloseFromSdl();
            return 1;
        }
        catch
        {
            return 0;
        }
    }

    private static void SetStatus(SDL_IOStatus* status, SDL_IOStatus value)
    {
        if (status != null)
        {
            *status = value;
        }
    }
}
