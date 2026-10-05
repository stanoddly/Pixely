using System.Diagnostics.CodeAnalysis;
using Pixely.DependencyInjection;
using Pixely.RenderOrchestration;
using SDL;

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

    internal PixelyApp(
        ServiceProvider serviceProvider,
        PixelyFrameClock frameClock,
        EventService eventService,
        AppControl appControl,
        ServiceRegistry<IRenderCoordinator> renderCoordinators,
        ServiceRegistry<IUpdatable> updatables,
        StageManager stageManager,
        IFrameTimingRecorder frameTimingRecorder)
    {
        ServiceProvider = serviceProvider;
        _frameClock = frameClock;
        _eventService = eventService;
        _appControl = appControl;
        _renderCoordinators = renderCoordinators;
        _updatables = updatables;
        _stageManager = stageManager;
        _frameTimingRecorder = frameTimingRecorder;
    }

    public T GetRequiredService<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.Interfaces)] T>() where T : class
    {
        return ServiceProvider.GetRequiredService<T>();
    }

    public int Run()
    {
        while (RunFrame())
        {
        }

        return 0;
    }

    // One frame, and whether another should follow. A host that owns the loop, such as a browser driving frames from
    // requestAnimationFrame, calls this instead of Run, which never yields to its caller.
    public bool RunFrame()
    {
        _frameTimingRecorder.BeginFrame();
        // start the frame before applying queued stage transitions
        _frameClock.StartFrame();
        _stageManager.ApplyPendingTransition();
        // then process events
        _eventService.Process();

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
        // browser needs no wait: requestAnimationFrame paces its frames.
        if (!drawn)
        {
            unsafe
            {
                SDL3.SDL_WaitEventTimeout(null, UndrawnFrameWaitMilliseconds);
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
