using System.Diagnostics.CodeAnalysis;
using Pixely.DependencyInjection;
using Pixely.RenderOrchestration;

namespace Pixely.App;

public class PixelyApp : IPixelyApp
{
    public ServiceProvider ServiceProvider { get; }

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
        try
        {
            return RunLoop();
        }
        catch (Exception exception)
        {
            ReportFatalError(exception);
            throw;
        }
    }

    private int RunLoop()
    {
        PixelyFrameContext frameContext = ServiceProvider.GetRequiredService<PixelyFrameContext>();
        EventService eventService = ServiceProvider.GetRequiredService<EventService>();
        AppControl appControl = ServiceProvider.GetRequiredService<AppControl>();
        ServiceRegistry<IRenderCoordinator> renderCoordinators =
            ServiceProvider.GetRequiredService<ServiceRegistry<IRenderCoordinator>>();
        ServiceRegistry<IUpdatable> updatables = ServiceProvider.GetRequiredService<ServiceRegistry<IUpdatable>>();
        StageManager stageManager = ServiceProvider.GetRequiredService<StageManager>();

        while (true)
        {
            // start the frame before applying queued stage transitions
            frameContext.StartFrame();
            stageManager.ApplyPendingTransition();
            // then process events
            eventService.Process();

            Update(updatables);

            if (appControl.QuitRequested)
            {
                return 0;
            }

            // finally render
            Render(renderCoordinators);
        }
    }

    public void Dispose()
    {
        ServiceProvider.Dispose();
    }

    private static void ReportFatalError(Exception exception)
    {
        Console.Error.WriteLine(exception);

        try
        {
            MessageBox.Show(MessageBoxSeverity.Error, "Fatal error", exception.Message);
        }
        catch (Exception messageBoxException)
        {
            // presenting the failure must never replace the failure being reported
            Console.Error.WriteLine(messageBoxException);
        }
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
