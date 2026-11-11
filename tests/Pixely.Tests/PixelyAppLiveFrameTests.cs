using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Pixely.App;
using XdlSharp;
using static XdlSharp.Xdl;

namespace Pixely.Tests;

// Builds real apps, which initializes SDL video with the dummy driver. SDL sends the live resize update only inside the system's
// move or resize loop, which runs inside SDL_PollEvent. The tests send the same event from there with a callback that another
// thread queues for the main thread, which SDL runs while it pumps events, and SDL hands a pushed event to the event watches as it
// would a sent one. Frame 2 is the stopped frame: the live frame is frame 3, and the stopped frame starts again as frame 4.
[NonParallelizable]
public class PixelyAppLiveFrameTests
{
    [Test]
    public void Run_LiveResizeUpdateWhilePolling_RunsAFrameInsideItAndStartsTheStoppedFrameAgain()
    {
        List<string> log = new();
        using IPixelyApp app = Build(log, quitAt: 3, onUpdate: (update, _) => QueueExposedAtFirstUpdate(update, data1: 1, count: 1));

        app.Run();

        Assert.That(log, Is.EqualTo(new[] { "update 1, frame 1", "update 2, frame 3", "update 3, frame 4" }));
    }

    [Test]
    public void Run_ExposeThatIsNotALiveResizeUpdate_RunsNoFrame()
    {
        List<string> log = new();
        using IPixelyApp app = Build(log, quitAt: 3, onUpdate: (update, _) => QueueExposedAtFirstUpdate(update, data1: 0, count: 1));

        app.Run();

        Assert.That(log, Is.EqualTo(new[] { "update 1, frame 1", "update 2, frame 2", "update 3, frame 3" }));
    }

    [Test]
    public void Run_LiveResizeUpdateFromAnEventHandler_RunsNoFrame()
    {
        List<string> log = new();
        using IPixelyApp app = Build(log, quitAt: 3, onUpdate: (update, _) =>
        {
            if (update == 1)
            {
                PushLocaleChanged();
            }
        }, onLocalesChanged: () => PushExposed(data1: 1));

        app.Run();

        Assert.That(log, Is.EqualTo(new[] { "update 1, frame 1", "locales changed", "update 2, frame 2", "update 3, frame 3" }));
    }

    [Test]
    public void Run_LiveResizeUpdateFromAnUpdate_RunsNoFrame()
    {
        List<string> log = new();
        using IPixelyApp app = Build(log, quitAt: 3, onUpdate: (update, _) =>
        {
            if (update == 1)
            {
                PushExposed(data1: 1);
            }
        });

        app.Run();

        Assert.That(log, Is.EqualTo(new[] { "update 1, frame 1", "update 2, frame 2", "update 3, frame 3" }));
    }

    [Test]
    public void Run_LiveFrame_TakesTheQueuedEventsBeforeItsUpdate()
    {
        List<string> log = new();
        using IPixelyApp app = Build(log, quitAt: 3, onUpdate: (update, _) =>
        {
            if (update == 1)
            {
                PushLocaleChanged();
                QueueExposed(data1: 1, count: 1);
            }
        });

        app.Run();

        Assert.That(log, Is.EqualTo(new[] { "update 1, frame 1", "locales changed", "update 2, frame 3", "update 3, frame 4" }));
    }

    [Test]
    public void Run_StageTransitionRequestedInALiveFrame_IsAppliedBeforeTheEventsThatFollow()
    {
        List<string> log = new();
        using IPixelyApp app = Build(log, quitAt: 3, onUpdate: (update, stageManager) =>
        {
            QueueExposedAtFirstUpdate(update, data1: 1, count: 1);
            if (update == 2)
            {
                PushLocaleChanged();
                stageManager.Load(services => services.AddSingleton<IUpdatable>(_ =>
                {
                    log.Add("stage loaded");
                    return new IdleUpdatable();
                }));
            }
        });

        app.Run();

        Assert.That(log, Is.EqualTo(new[] { "update 1, frame 1", "update 2, frame 3", "stage loaded", "locales changed", "update 3, frame 4" }));
    }

    [Test]
    public void Run_QuitInALiveFrame_RunsNoFurtherLiveFrame()
    {
        List<string> log = new();
        using IPixelyApp app = Build(log, quitAt: 2, onUpdate: (update, _) => QueueExposedAtFirstUpdate(update, data1: 1, count: 2));

        app.Run();

        Assert.That(log, Is.EqualTo(new[] { "update 1, frame 1", "update 2, frame 3", "update 3, frame 4" }));
    }

    [Test]
    public void Run_LiveFrameThrows_ThrowsOnceTheEventProcessingEnds()
    {
        List<string> log = new();
        InvalidOperationException failure = new("live frame");
        using IPixelyApp app = Build(log, quitAt: 3, onUpdate: (update, _) =>
        {
            QueueExposedAtFirstUpdate(update, data1: 1, count: 2);
            if (update == 2)
            {
                throw failure;
            }
        });

        Assert.That(app.Run, Throws.Exception.SameAs(failure));
        Assert.That(log, Is.EqualTo(new[] { "update 1, frame 1", "update 2, frame 3" }));
    }

    // The update throws, so Run returns without a quit, which would otherwise keep any live frame from running.
    [Test]
    public void RunFrame_AfterRunReturns_RunsNoLiveFrame()
    {
        List<string> log = new();
        InvalidOperationException failure = new("update");
        using IPixelyApp app = Build(log, quitAt: 3, onUpdate: (update, _) =>
        {
            if (update == 1)
            {
                throw failure;
            }
        });
        Assert.That(app.Run, Throws.Exception.SameAs(failure));
        QueueExposed(data1: 1, count: 1);

        app.RunFrame();

        Assert.That(log, Is.EqualTo(new[] { "update 1, frame 1", "update 2, frame 2" }));
    }

    private static IPixelyApp Build(List<string> log, int quitAt, Action<int, IStageManager> onUpdate, Action? onLocalesChanged = null)
    {
        SDL_SetHintWithPriority(SDL_HINT_VIDEO_DRIVER, "dummy"u8, SDL_HintPriority.SDL_HINT_DEFAULT);
        PixelyAppBuilder builder = new();
        builder.AddSingleton(new PixelyConfig { IsIsolated = true });
        builder.AddSingleton<IUpdatable>(provider => new LoggingUpdatable(log, quitAt, onUpdate, provider.GetRequiredService<AppControl>(),
            provider.GetRequiredService<FrameClock>(), provider.GetRequiredService<IStageManager>()));
        IPixelyApp app = builder.Build();

        app.ServiceProvider.GetRequiredService<IPlatformSettings>().PreferredLocalesChanged += _ =>
        {
            log.Add("locales changed");
            onLocalesChanged?.Invoke();
        };
        return app;
    }

    private static void QueueExposedAtFirstUpdate(int update, int data1, int count)
    {
        if (update == 1)
        {
            QueueExposed(data1, count);
        }
    }

    // Queues from another thread, because SDL runs a callback queued from the main thread at once.
    private static void QueueExposed(int data1, int count)
    {
        bool queued = Task.Run(() =>
        {
            bool all = true;
            for (int i = 0; i < count; i++)
            {
                unsafe
                {
                    all &= SDL_RunOnMainThread(&PushExposedOnMainThread, data1, false);
                }
            }

            return all;
        }).GetAwaiter().GetResult();
        Assert.That(queued, Is.True, () => SdlError.GetError() ?? string.Empty);
    }

    // An exception cannot cross the native callback, so a failed push shows as a missing live frame instead.
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void PushExposedOnMainThread(nint data1)
    {
        SDL_Event evt = default;
        evt.type = (uint)SDL_EventType.SDL_EVENT_WINDOW_EXPOSED;
        evt.window.data1 = (int)data1;
        unsafe
        {
            SDL_PushEvent(&evt);
        }
    }

    private static void PushLocaleChanged()
    {
        Push(new SDL_Event { type = (uint)SDL_EventType.SDL_EVENT_LOCALE_CHANGED });
    }

    private static void PushExposed(int data1)
    {
        SDL_Event evt = default;
        evt.type = (uint)SDL_EventType.SDL_EVENT_WINDOW_EXPOSED;
        evt.window.data1 = data1;
        Push(evt);
    }

    private static void Push(SDL_Event evt)
    {
        unsafe
        {
            Assert.That(SDL_PushEvent(&evt), Is.True, () => SdlError.GetError() ?? string.Empty);
        }
    }

    // Logs each update with its frame number and quits at the given one.
    private sealed class LoggingUpdatable(List<string> log, int quitAt, Action<int, IStageManager> onUpdate, AppControl appControl, FrameClock frameClock,
        IStageManager stageManager) : IUpdatable
    {
        private int _updates;

        public int UpdateOrder => UpdateOrders.Default;

        public void Update()
        {
            int update = ++_updates;
            log.Add($"update {update}, frame {frameClock.FrameNumber}");
            if (update == quitAt)
            {
                appControl.Quit();
            }

            onUpdate(update, stageManager);
        }
    }

    private sealed class IdleUpdatable : IUpdatable
    {
        public int UpdateOrder => UpdateOrders.Default;

        public void Update()
        {
        }
    }
}
