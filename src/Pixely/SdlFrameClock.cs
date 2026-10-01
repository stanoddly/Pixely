using SDL;

namespace Pixely;

// Follows SDL's clock, the one SDL's event timestamps use, without the time spent paused.
internal sealed class SdlFrameClock : PixelyFrameClock
{
    // 100 ms = 0.1 seconds maximum delta time
    private const double MaxDeltaTime = 0.100;
    private ulong _pausedNanoseconds;
    private ulong _pauseStartNanoseconds;
    private bool _paused;

    internal override void StartFrame()
    {
        if (_paused)
        {
            return;
        }

        ulong previousElapsedNanoseconds = ElapsedNanoseconds;
        ulong elapsedNanoseconds = SDL3.SDL_GetTicksNS() - _pausedNanoseconds;

        // There could have been some loading, so the very first StartFrame would calculate several seconds! 😅
        double timeDelta = previousElapsedNanoseconds != 0 ? Math.Min((elapsedNanoseconds - previousElapsedNanoseconds) / 1_000_000_000.0, MaxDeltaTime) : TimeDelta64;

        AdvanceFrame(elapsedNanoseconds, timeDelta);
    }

    internal override void Pause()
    {
        if (_paused)
        {
            throw new InvalidOperationException("Frame clock is already paused.");
        }

        _pauseStartNanoseconds = SDL3.SDL_GetTicksNS();
        _paused = true;
    }

    internal override void Resume()
    {
        if (!_paused)
        {
            throw new InvalidOperationException("Frame clock is not paused.");
        }

        ulong pauseEndNanoseconds = SDL3.SDL_GetTicksNS();
        _pausedNanoseconds += pauseEndNanoseconds - _pauseStartNanoseconds;
        _pauseStartNanoseconds = 0;
        _paused = false;
    }
}
