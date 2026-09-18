using System.Diagnostics.CodeAnalysis;
using Pixely.DependencyInjection;
using Pixely.RenderOrchestration;

namespace Pixely.App;

public class PixelyApp : IPixelyApp
{
    public ServiceProvider ServiceProvider { get; }

    private readonly PixelyFrameContext _frameContext;
    private readonly EventService _eventService;
    private readonly AppControl _appControl;
    private readonly ServiceRegistry<IRenderCoordinator> _renderCoordinators;
    private readonly ServiceRegistry<IUpdatable> _updatables;
    private readonly StageManager _stageManager;

    internal PixelyApp(ServiceProvider serviceProvider)
    {
        ServiceProvider = serviceProvider;
        _frameContext = serviceProvider.GetRequiredService<PixelyFrameContext>();
        _eventService = serviceProvider.GetRequiredService<EventService>();
        _appControl = serviceProvider.GetRequiredService<AppControl>();
        _renderCoordinators = serviceProvider.GetRequiredService<ServiceRegistry<IRenderCoordinator>>();
        _updatables = serviceProvider.GetRequiredService<ServiceRegistry<IUpdatable>>();
        _stageManager = serviceProvider.GetRequiredService<StageManager>();
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
