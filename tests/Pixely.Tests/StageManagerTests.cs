using Pixely.App;
using Pixely.DependencyInjection;

namespace Pixely.Tests;

public class StageManagerTests
{
    [Test]
    public void Load_WithNullConfigure_Throws()
    {
        ServiceProvider root = BuildRootProvider(out _);
        StageManager stageManager = new(root);

        Assert.Throws<ArgumentNullException>(() => stageManager.Load(null!));
    }

    [Test]
    public void Load_DoesNotApplyImmediately()
    {
        ServiceProvider root = BuildRootProvider(out ServiceRegistry<IStageView> viewRegistry);
        StageManager stageManager = new(root);

        stageManager.Load(services =>
        {
            services.AddSingleton<IStageView>(new TestView("stage"));
        });

        Assert.That(ViewNames(viewRegistry), Is.Empty);
    }

    [Test]
    public void Load_AppliesOnPendingTransition()
    {
        ServiceProvider root = BuildRootProvider(out ServiceRegistry<IStageView> viewRegistry);
        StageManager stageManager = new(root);

        stageManager.Load(services =>
        {
            services.AddSingleton<IStageView>(new TestView("stage"));
        });
        stageManager.ApplyPendingTransition();

        Assert.That(ViewNames(viewRegistry), Is.EqualTo(new[] { "stage" }));
    }

    [Test]
    public void Load_MultipleBeforePendingTransition_LastWins()
    {
        ServiceProvider root = BuildRootProvider(out ServiceRegistry<IStageView> viewRegistry);
        StageManager stageManager = new(root);

        stageManager.Load(services =>
        {
            services.AddSingleton<IStageView>(new TestView("first"));
        });
        stageManager.Load(services =>
        {
            services.AddSingleton<IStageView>(new TestView("second"));
        });
        stageManager.ApplyPendingTransition();

        Assert.That(ViewNames(viewRegistry), Is.EqualTo(new[] { "second" }));
    }

    [Test]
    public void Reload_BeforePendingLoadIsApplied_Throws()
    {
        ServiceProvider root = BuildRootProvider(out ServiceRegistry<IStageView> viewRegistry);
        StageManager stageManager = new(root);

        stageManager.Load(services =>
        {
            services.AddSingleton<IStageView>(new TestView("stage"));
        });

        Assert.Throws<InvalidOperationException>(() => stageManager.Reload());

        stageManager.ApplyPendingTransition();
        Assert.That(ViewNames(viewRegistry), Is.EqualTo(new[] { "stage" }));
    }

    [Test]
    public void Reload_RebuildsLastAppliedStageAtPendingTransition()
    {
        ServiceProvider root = BuildRootProvider(out ServiceRegistry<IStageView> viewRegistry);
        StageManager stageManager = new(root);
        List<TestView> createdViews = new();

        stageManager.Load(services =>
        {
            TestView view = new("stage");
            createdViews.Add(view);
            services.AddSingleton<IStageView>(view);
        });
        stageManager.ApplyPendingTransition();

        stageManager.Reload();

        Assert.That(createdViews, Has.Count.EqualTo(1));
        Assert.That(viewRegistry.Single(), Is.SameAs(createdViews[0]));

        stageManager.ApplyPendingTransition();

        Assert.That(createdViews, Has.Count.EqualTo(2));
        Assert.That(createdViews[1], Is.Not.SameAs(createdViews[0]));
        Assert.That(viewRegistry.Single(), Is.SameAs(createdViews[1]));
    }

    [Test]
    public void Reload_MultipleBeforePendingTransition_RebuildsOnce()
    {
        ServiceProvider root = BuildRootProvider(out _);
        StageManager stageManager = new(root);
        int configureCount = 0;

        stageManager.Load(services =>
        {
            configureCount++;
        });
        stageManager.ApplyPendingTransition();

        stageManager.Reload();
        stageManager.Reload();
        stageManager.ApplyPendingTransition();

        Assert.That(configureCount, Is.EqualTo(2));
    }

    [Test]
    public void Reload_AfterPendingLoad_ReplacesItWithActiveStage()
    {
        ServiceProvider root = BuildRootProvider(out ServiceRegistry<IStageView> viewRegistry);
        StageManager stageManager = new(root);

        stageManager.Load(services =>
        {
            services.AddSingleton<IStageView>(new TestView("active"));
        });
        stageManager.ApplyPendingTransition();

        stageManager.Load(services =>
        {
            services.AddSingleton<IStageView>(new TestView("pending"));
        });
        stageManager.Reload();
        stageManager.ApplyPendingTransition();

        Assert.That(ViewNames(viewRegistry), Is.EqualTo(new[] { "active" }));
    }

    [Test]
    public void Load_AfterPendingReload_ReplacesItWithLoadedStage()
    {
        ServiceProvider root = BuildRootProvider(out ServiceRegistry<IStageView> viewRegistry);
        StageManager stageManager = new(root);

        stageManager.Load(services =>
        {
            services.AddSingleton<IStageView>(new TestView("active"));
        });
        stageManager.ApplyPendingTransition();

        stageManager.Reload();
        stageManager.Load(services =>
        {
            services.AddSingleton<IStageView>(new TestView("loaded"));
        });
        stageManager.ApplyPendingTransition();

        Assert.That(ViewNames(viewRegistry), Is.EqualTo(new[] { "loaded" }));
    }

    [Test]
    public void Reload_DisposesPreviousStageBeforeConfiguringReplacement()
    {
        ServiceProvider root = BuildRootProvider(out _);
        StageManager stageManager = new(root);
        DisposableService? previous = null;
        List<DisposableService> createdServices = new();

        stageManager.Load(services =>
        {
            Assert.That(previous == null || previous.IsDisposed, Is.True);
            DisposableService current = new();
            previous = current;
            createdServices.Add(current);
            services.AddSingleton(current);
        });
        stageManager.ApplyPendingTransition();

        stageManager.Reload();
        stageManager.ApplyPendingTransition();

        Assert.That(createdServices, Has.Count.EqualTo(2));
        Assert.That(createdServices[0].IsDisposed, Is.True);
        Assert.That(createdServices[1].IsDisposed, Is.False);
    }

    [Test]
    public void Load_DisposesPreviousStageOnPendingTransition()
    {
        ServiceProvider root = BuildRootProvider(out ServiceRegistry<IStageView> viewRegistry);
        StageManager stageManager = new(root);

        stageManager.Load(services =>
        {
            services.AddSingleton<IStageView>(new TestView("first"));
        });
        stageManager.ApplyPendingTransition();

        stageManager.Load(services =>
        {
            services.AddSingleton<IStageView>(new TestView("second"));
        });
        stageManager.ApplyPendingTransition();

        Assert.That(ViewNames(viewRegistry), Is.EqualTo(new[] { "second" }));
    }

    [Test]
    public void ApplyPendingTransition_WithNoPending_DoesNothing()
    {
        ServiceProvider root = BuildRootProvider(out ServiceRegistry<IStageView> viewRegistry);
        StageManager stageManager = new(root);

        stageManager.Load(services =>
        {
            services.AddSingleton<IStageView>(new TestView("stage"));
        });
        stageManager.ApplyPendingTransition();

        stageManager.ApplyPendingTransition();

        Assert.That(ViewNames(viewRegistry), Is.EqualTo(new[] { "stage" }));
    }

    [Test]
    public void Dispose_DisposesActiveStage()
    {
        ServiceProvider root = BuildRootProvider(out ServiceRegistry<IStageView> viewRegistry);
        StageManager stageManager = new(root);

        stageManager.Load(services =>
        {
            services.AddSingleton<IStageView>(new TestView("stage"));
        });
        stageManager.ApplyPendingTransition();

        stageManager.Dispose();

        Assert.That(ViewNames(viewRegistry), Is.Empty);
    }

    [Test]
    public void Dispose_ClearsPendingLoad()
    {
        ServiceProvider root = BuildRootProvider(out ServiceRegistry<IStageView> viewRegistry);
        StageManager stageManager = new(root);

        stageManager.Load(services =>
        {
            services.AddSingleton<IStageView>(new TestView("stage"));
        });

        stageManager.Dispose();
        stageManager.ApplyPendingTransition();

        Assert.That(ViewNames(viewRegistry), Is.Empty);
    }

    [Test]
    public void Dispose_ClearsLastStageConfiguration()
    {
        ServiceProvider root = BuildRootProvider(out _);
        StageManager stageManager = new(root);

        stageManager.Load(static _ => { });
        stageManager.ApplyPendingTransition();

        stageManager.Dispose();

        Assert.Throws<InvalidOperationException>(() => stageManager.Reload());
    }

    [Test]
    public void Load_RegistersStageServicesViaParentCallbacksOnPendingTransition()
    {
        ServiceProvider root = BuildRootProvider(out ServiceRegistry<IStageView> viewRegistry);
        StageManager stageManager = new(root);

        stageManager.Load(services =>
        {
            services.AddSingleton<IStageView>(new TestView("stage"));
        });
        stageManager.ApplyPendingTransition();

        Assert.That(ViewNames(viewRegistry), Is.EqualTo(new[] { "stage" }));
    }

    [Test]
    public void Load_StageServicesCanResolveRootServices()
    {
        ServiceCollection rootCollection = new();
        rootCollection.AddSingleton(new TestConfig("test"));
        ServiceProvider root = rootCollection.BuildServiceProvider();
        StageManager stageManager = new(root);

        TestConfig? resolved = null;
        stageManager.Load(services =>
        {
            services.AddSingleton<IStageView>(sp =>
            {
                resolved = sp.GetRequiredService<TestConfig>();
                return new TestView("stage");
            });
        });
        stageManager.ApplyPendingTransition();

        Assert.That(resolved, Is.Not.Null);
        Assert.That(resolved!.Title, Is.EqualTo("test"));
    }

    [Test]
    public void Load_DisposesPreviousStageOwnedDisposables()
    {
        ServiceProvider root = BuildRootProvider(out _);
        StageManager stageManager = new(root);

        DisposableService disposable = new();
        stageManager.Load(services =>
        {
            services.AddSingleton(disposable);
        });
        stageManager.ApplyPendingTransition();

        stageManager.Load(services =>
        {
            services.AddSingleton<IStageView>(new TestView("next"));
        });
        stageManager.ApplyPendingTransition();

        Assert.That(disposable.IsDisposed, Is.True);
    }

    private static ServiceProvider BuildRootProvider(
        out ServiceRegistry<IStageView> viewRegistry)
    {
        ServiceCollection rootCollection = new();
        rootCollection.AddRegistry<IStageView>();
        ServiceProvider provider = rootCollection.BuildServiceProvider();
        viewRegistry = provider.GetRequiredService<ServiceRegistry<IStageView>>();
        return provider;
    }

    private static string[] ViewNames(ServiceRegistry<IStageView> viewRegistry)
    {
        List<string> names = new();
        foreach (IStageView view in viewRegistry)
        {
            names.Add(((TestView)view).Name);
        }
        return names.ToArray();
    }

    private interface IStageView
    {
        string Name { get; }
    }

    private sealed class TestView : IStageView
    {
        public string Name { get; }
        public TestView(string name)
        {
            Name = name;
        }
    }

    private sealed class DisposableService : IDisposable
    {
        public bool IsDisposed { get; private set; }

        public void Dispose()
        {
            IsDisposed = true;
        }
    }

    private sealed record TestConfig(string Title);
}
