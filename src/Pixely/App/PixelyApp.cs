using System.Diagnostics.CodeAnalysis;
using Pixely.DependencyInjection;
using Pixely.Gpu;
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
    private readonly GpuMemorySystem? _gpuMemorySystem;

    internal PixelyApp(
        ServiceProvider serviceProvider,
        PixelyFrameClock frameClock,
        EventService eventService,
        AppControl appControl,
        ServiceRegistry<IRenderCoordinator> renderCoordinators,
        ServiceRegistry<IUpdatable> updatables,
        StageManager stageManager,
        GpuMemorySystem? gpuMemorySystem)
    {
        ServiceProvider = serviceProvider;
        _frameClock = frameClock;
        _eventService = eventService;
        _appControl = appControl;
        _renderCoordinators = renderCoordinators;
        _updatables = updatables;
        _stageManager = stageManager;
        _gpuMemorySystem = gpuMemorySystem;
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
        // start the frame before applying queued stage transitions
        _frameClock.StartFrame();
        _stageManager.ApplyPendingTransition();
        // then process events
        _eventService.Process();

        Update(_updatables);

        if (_appControl.QuitRequested)
        {
            return false;
        }

        // finally render
        bool drawn = Render(_renderCoordinators, _gpuMemorySystem);
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
    //
    // Render coordinators submit the uploads GpuMemorySystem records. Without one, nothing else would, so the uploads would
    // never run and every update of a buffer would cycle it into a new copy; they are submitted here instead, after the
    // updates that recorded them. While any coordinator runs they are left to it: in the browser a coordinator keeps them
    // pending on purpose on a frame that gets no swapchain texture. Only the root's GpuMemorySystem is submitted here, so a
    // stage that registers its own GPU device without window rendering must submit its own.
    internal static bool Render(ServiceRegistry<IRenderCoordinator> renderCoordinators, GpuMemorySystem? gpuMemorySystem)
    {
        bool hasCoordinator = false;
        bool drawn = false;
        foreach (IRenderCoordinator renderCoordinator in renderCoordinators)
        {
            hasCoordinator = true;
            drawn |= renderCoordinator.Execute();
        }

        if (!hasCoordinator)
        {
            gpuMemorySystem?.Submit();
        }

        return drawn || !hasCoordinator;
    }
}
