using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using Pixely.DependencyInjection;
using Pixely.RenderOrchestration;
using XdlSharp;
using static XdlSharp.Xdl;

namespace Pixely.App;

public class PixelyApp : IPixelyApp
{
    // How long a frame that no window drew waits for an event, about one frame of a 60 Hz display.
    private const int UndrawnFrameWaitMilliseconds = 16;

    public ServiceProvider ServiceProvider { get; }

    private readonly PixelyFrameClock _frameClock;
    private readonly EventService _eventService;
    private readonly AppControl _appControl;
    private readonly ServiceRegistry<IRenderCoordinator> _renderCoordinators;
    private readonly ServiceRegistry<IUpdatable> _updatables;
    private readonly StageManager _stageManager;
    private readonly IFrameTimingRecorder _frameTimingRecorder;
#if !BROWSER
    private readonly bool _pacesUndrawnFrames;
#endif
    private bool _runsLiveFrame;
    private bool _ranLiveFrame;
    private ExceptionDispatchInfo? _liveFrameFailure;

    internal PixelyApp(
        ServiceProvider serviceProvider,
        PixelyFrameClock frameClock,
        EventService eventService,
        AppControl appControl,
        ServiceRegistry<IRenderCoordinator> renderCoordinators,
        ServiceRegistry<IUpdatable> updatables,
        StageManager stageManager,
        IFrameTimingRecorder frameTimingRecorder,
        bool pacesUndrawnFrames)
    {
        ServiceProvider = serviceProvider;
        _frameClock = frameClock;
        _eventService = eventService;
        _appControl = appControl;
        _renderCoordinators = renderCoordinators;
        _updatables = updatables;
        _stageManager = stageManager;
        _frameTimingRecorder = frameTimingRecorder;
#if !BROWSER
        _pacesUndrawnFrames = pacesUndrawnFrames;
#endif
    }

    public T GetRequiredService<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.Interfaces)] T>() where T : class
    {
        return ServiceProvider.GetRequiredService<T>();
    }

    public int Run()
    {
        GCHandle handle = GCHandle.Alloc(this);
        try
        {
            unsafe
            {
                if (!SDL_AddEventWatch(&WatchEvent, GCHandle.ToIntPtr(handle)))
                {
                    throw new PixelyException($"SDL_AddEventWatch failed: {SdlError.GetError()}");
                }
            }

            try
            {
                while (RunFrame())
                {
                }
            }
            finally
            {
                unsafe
                {
                    SDL_RemoveEventWatch(&WatchEvent, GCHandle.ToIntPtr(handle));
                }
            }
        }
        finally
        {
            handle.Free();
        }

        return 0;
    }

    // One frame, and whether another should follow. A host that owns the loop, such as a browser driving frames from
    // requestAnimationFrame, calls this instead of Run, which never yields to its caller.
    public bool RunFrame()
    {
        return RunFrame(live: false);
    }

    // On Windows, dragging or resizing a window runs the system's own loop inside SDL_PollEvent until the mouse button is released,
    // and so does a live resize on macOS, which would stop every window's frames with it. All through it SDL sends an expose event
    // with data1 set to 1, a live resize update, on a timer, which only an event watch hears in time. Each one runs a live frame. It
    // runs inside SDL_PollEvent of the frame that stopped, so it takes only the events already queued. None runs elsewhere, such as
    // in a modal dialog an event handler opened, inside another, or once a quit is requested. A stage transition it requests is
    // applied right after it, as it would be before the events that follow. An exception cannot cross the native callback, so it
    // is kept and thrown once the stopped frame's event processing ends, and no further live frame runs.
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static unsafe byte WatchEvent(nint userdata, SDL_Event* evt)
    {
        if ((SDL_EventType)evt->type == SDL_EventType.SDL_EVENT_WINDOW_EXPOSED && evt->window.data1 == 1)
        {
            PixelyApp app = (PixelyApp)GCHandle.FromIntPtr(userdata).Target!;
            if (app._eventService.IsPolling && !app._runsLiveFrame && app._liveFrameFailure == null && !app._appControl.QuitRequested)
            {
                app._runsLiveFrame = true;
                try
                {
                    app.RunFrame(live: true);
                    app._stageManager.ApplyPendingTransition();
                }
                catch (Exception exception)
                {
                    app._liveFrameFailure = ExceptionDispatchInfo.Capture(exception);
                }
                finally
                {
                    app._runsLiveFrame = false;
                    app._ranLiveFrame = true;
                }
            }
        }

        return 1;
    }

    private bool RunFrame(bool live)
    {
        StartFrame();
        // then process events
        if (live)
        {
            _eventService.ProcessQueued();
        }
        else
        {
            _eventService.Process();

            if (_liveFrameFailure is { } failure)
            {
                _liveFrameFailure = null;
                failure.Throw();
            }

            // A live frame ran since this one started, so this one starts again after it, with its own frame number and time.
            if (_ranLiveFrame)
            {
                _ranLiveFrame = false;
                StartFrame();
            }
        }

        Update(_updatables);
        _frameTimingRecorder.EndUpdate();

        if (_appControl.QuitRequested)
        {
            return false;
        }

        // finally render
        bool drawn = Render(_renderCoordinators);
        _frameTimingRecorder.EndRender();
#if !BROWSER
        // A frame that no window drew, such as one whose windows are all hidden or minimized, was not paced by waiting for
        // the display. Without this wait the loop would spin, and every update would record GPU uploads for frames nobody
        // sees. An event, such as the window being restored, ends the wait early and stays queued for the next frame. The
        // browser needs no wait: requestAnimationFrame paces its frames. An isolated app, headless or under test, needs none either:
        // its game time steps by a fixed amount per frame, and a test with a hidden window would otherwise wait every frame. A live frame
        // inside the system's loop must not wait, because waiting asks the platform for events, and the system's loop paces it.
        if (!drawn && _pacesUndrawnFrames && !live)
        {
            unsafe
            {
                SDL_WaitEventTimeout(null, UndrawnFrameWaitMilliseconds);
            }
        }
#endif
        _frameTimingRecorder.EndFrame();

        return true;
    }

    public void Dispose()
    {
        ServiceProvider.Dispose();
    }

    private void StartFrame()
    {
        _frameTimingRecorder.BeginFrame();
        // start the frame before applying queued stage transitions
        _frameClock.StartFrame();
        _stageManager.ApplyPendingTransition();
    }

    private static void Update(ServiceRegistry<IUpdatable> updatables)
    {
        foreach (IUpdatable updatable in updatables)
        {
            updatable.Update();
        }
    }

    // Whether a window drew the frame, or the app renders no window at all. An app without render coordinators keeps its
    // own pacing.
    internal static bool Render(ServiceRegistry<IRenderCoordinator> renderCoordinators)
    {
        bool hasCoordinator = false;
        bool drawn = false;
        foreach (IRenderCoordinator renderCoordinator in renderCoordinators)
        {
            hasCoordinator = true;
            drawn |= renderCoordinator.Execute();
        }

        return drawn || !hasCoordinator;
    }
}
