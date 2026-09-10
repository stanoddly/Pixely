using System.Diagnostics.CodeAnalysis;
using Pixely.DependencyInjection;
using Pixely.RenderOrchestration;

namespace Pixely.App;

public class PixelyApp : IPixelyApp
{
    public ServiceProvider ServiceProvider { get; }

    // Resolved on the first frame rather than in the constructor, so building the application does
    // not force SDL-backed singletons into existence.
    private bool _frameServicesResolved;
    private PixelyFrameContext _frameContext = null!;
    private EventService _eventService = null!;
    private AppControl _appControl = null!;
    private ServiceRegistry<IRenderCoordinator> _renderCoordinators = null!;
    private ServiceRegistry<IUpdatable> _updatables = null!;
    private StageManager _stageManager = null!;

    internal PixelyApp(ServiceProvider serviceProvider)
    {
        ServiceProvider = serviceProvider;
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

    /// <summary>
    /// Runs one frame and reports whether another should follow. A host that owns the loop itself,
    /// such as a browser driving frames from requestAnimationFrame, calls this instead of
    /// <see cref="Run"/>, which cannot yield to its caller.
    /// </summary>
    public bool RunFrame()
    {
        if (!_frameServicesResolved)
        {
            _frameContext = ServiceProvider.GetRequiredService<PixelyFrameContext>();
            _eventService = ServiceProvider.GetRequiredService<EventService>();
            _appControl = ServiceProvider.GetRequiredService<AppControl>();
            _renderCoordinators = ServiceProvider.GetRequiredService<ServiceRegistry<IRenderCoordinator>>();
            _updatables = ServiceProvider.GetRequiredService<ServiceRegistry<IUpdatable>>();
            _stageManager = ServiceProvider.GetRequiredService<StageManager>();
            _frameServicesResolved = true;
        }

        // start the frame before applying queued stage transitions
        _frameContext.StartFrame();
        _stageManager.ApplyPendingTransition();
        // then process events
        _eventService.Process();

        Update(_updatables);

        if (_appControl.QuitRequested)
        {
            return false;
        }

        // finally render
        Render(_renderCoordinators);
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

    private static void Render(ServiceRegistry<IRenderCoordinator> renderCoordinators)
    {
        foreach (IRenderCoordinator renderCoordinator in renderCoordinators)
        {
            renderCoordinator.Execute();
        }
    }
}
