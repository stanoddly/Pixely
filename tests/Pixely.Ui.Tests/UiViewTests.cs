using Pixely.Gpu;

namespace Pixely.Ui.Tests;

public class UiViewTests
{
    [Test]
    public void Construction_DoesNotBuildOrSync()
    {
        CounterViewModel viewModel = new();
        CounterView view = new(viewModel);

        Assert.Multiple(() =>
        {
            Assert.That(view.BuildCount, Is.Zero, "constructors stay assignment only");
            Assert.That(view.SyncCount, Is.Zero);
            Assert.That(view.IsAttached, Is.False);
        });
    }

    [Test]
    public void Root_BeforeAttach_Throws()
    {
        CounterView view = new(new CounterViewModel());

        Assert.Throws<InvalidOperationException>(() => _ = view.Root);
    }

    [Test]
    public void AddView_BuildsOnceAndSyncsOnce()
    {
        CounterView view = new(new CounterViewModel());
        UiRoot root = new();
        root.SetViewportSize(new Vector2Int(100, 100));

        root.AddView(view);

        Assert.Multiple(() =>
        {
            Assert.That(view.BuildCount, Is.EqualTo(1));
            Assert.That(view.SyncCount, Is.EqualTo(1), "the tree starts holding the current values");
        });
    }

    [Test]
    public void ViewModelChange_SyncsWithoutRebuilding()
    {
        CounterViewModel viewModel = new();
        CounterView view = new(viewModel);
        UiRoot root = new();
        root.SetViewportSize(new Vector2Int(100, 100));
        root.AddView(view);

        viewModel.Count = 1;
        viewModel.Count = 2;

        Assert.Multiple(() =>
        {
            Assert.That(view.BuildCount, Is.EqualTo(1), "the tree is built once, not per change");
            Assert.That(view.SyncCount, Is.EqualTo(3), "one on attach plus one per change");
        });
    }

    [Test]
    public void UnchangedViewModelAssignment_DoesNotSync()
    {
        CounterViewModel viewModel = new();
        CounterView view = new(viewModel);
        UiRoot root = new();
        root.SetViewportSize(new Vector2Int(100, 100));
        root.AddView(view);

        viewModel.Count = 0;

        Assert.That(view.SyncCount, Is.EqualTo(1));
    }

    [Test]
    public void ViewModelChange_ReachesTheTree()
    {
        CounterViewModel viewModel = new();
        CounterView view = new(viewModel);
        UiRoot root = new();
        root.SetViewportSize(new Vector2Int(100, 100));
        root.AddView(view);
        root.Update();

        viewModel.Count = 7;
        root.Update();

        Assert.That(view.Bar.Bounds.Width, Is.EqualTo(70));
    }

    [Test]
    public void RemoveView_StopsSyncing()
    {
        CounterViewModel viewModel = new();
        CounterView view = new(viewModel);
        UiRoot root = new();
        root.SetViewportSize(new Vector2Int(100, 100));
        root.AddView(view);

        Assert.That(root.RemoveView(view), Is.True);

        int syncsAtRemoval = view.SyncCount;
        viewModel.Count = 5;

        Assert.Multiple(() =>
        {
            Assert.That(view.SyncCount, Is.EqualTo(syncsAtRemoval), "a removed view must not keep handling changes");
            Assert.That(root.Layers, Is.Empty);
        });
    }

    [Test]
    public void AttachingTwice_Throws()
    {
        CounterView view = new(new CounterViewModel());
        UiRoot root = new();
        root.SetViewportSize(new Vector2Int(100, 100));
        root.AddView(view);

        Assert.Throws<InvalidOperationException>(() => root.AddView(view));
    }

    private sealed class CounterViewModel : IUiViewModel
    {
        private int _count;

        public event Action? Changed;

        public int Count
        {
            get => _count;
            set
            {
                if (_count == value)
                {
                    return;
                }

                _count = value;
                Changed?.Invoke();
            }
        }
    }

    private sealed class CounterView : UiView<CounterViewModel>
    {
        public CounterView(CounterViewModel viewModel) : base(viewModel)
        {
        }

        public int BuildCount { get; private set; }
        public int SyncCount { get; private set; }

        public Element Bar { get; } =
            new Column { Background = new SolidDrawable(new Color(255, 255, 255, 255)), Height = Sizing.Fixed(10) };

        protected override Element Build()
        {
            BuildCount++;
            return new Column { Children = { Bar } };
        }

        protected override void Sync()
        {
            SyncCount++;
            Bar.Width = Sizing.Fixed(ViewModel.Count * 10);
        }
    }
}
